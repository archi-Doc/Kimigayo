// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Testing;

namespace Kimi.Compiler.Backend;

// The Drop terminator and A2's verdict rows of FunctionCodegen (R5 package C 3.6, plans C4a, C4b and C4c; C0c brings the Keep and Elide
// verdicts forward, DECISIONS R5-M78, R5-M99, R5-M100): A2's verdict group for the item's view (R4-M6a), its first row the dropped Place's.
// A value is destroyed by its row (DestroyByRow) exactly when its instance Type may need a drop, reading the collector's targets at the Drop
// in the walk's order (DropGlue.Destroyed, PLAN G641): Keep and Flag by calling its DropGlue item, the next target, with the Place's address
// and the Drop's location text through the one destruction-call writer (FunctionCodegen.Glue's Destroy), under the row's flag
// (FunctionCodegen.Flags' test; Keep's is none, so always); Elide writes nothing; Open destroys what remains after a Partial Move (SPEC
// 16.3.2, 15.1.3): one level of the value's Type (FunctionCodegen.Glue's Level), each part in destruction order by its governing row
// (DropGlue.Governing), and a part without one by its parent's own bit, a component no Place names having no path of its own (MirMoveData),
// an Open row reading no target of its own. In a test build a destruction runs in the cleanup
// phase (FunctionCodegen.Phases; DECISIONS R5-M137). The Drop ends through Branch, after its own clear point. Write writes only a body
// whose table is exact (FunctionCodegen.Write; DECISIONS R5-M96, R5-M185), so a runtime Drop without a group is a broken contract, and no
// Open row is a struct's that declares a drop (A2's MovedPlace_Kd, R4-M6b).
internal sealed partial class FunctionCodegen
{
    private readonly HashSet<BoundType> seen = new(); // MirTypes.MayNeedDrop's scratch.

    private void Drop(in MirTerminator drop)
    {
        var group = this.program.Drops.Group(this.body.Ordinal, this.at, this.collector.Set.Items[this.item].View);
        if (group < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source); // PD12: A1 and A2 judged every runtime Drop of an exact body.
            return;
        }

        if (this.DropRow(group).Verdict != MirDropVerdict.Elide && !this.DestroyPlace(drop, group))
        {
            return;
        }

        this.Branch(drop.Target);
    }

    // SPEC 16.2.1 (R4-M6a): the dropped value, when it owns storage (OwnsStorage), destroyed by its group's first row, exactly when its
    // instance Type may need a drop (MirTypes.MayNeedDrop, the rule the collector targets a Drop's glue by, K package 4.5 row 9): refuted,
    // nothing is written, no test either; else by the collector's targets at the Drop (I8's cursor, Target), what the group destroys in the
    // walk's order (DropGlue.Destroyed), on the Place's address with the Drop's location; a target missing (NextTarget) or left over is a
    // broken contract (package C 3.2 I8). False after a record.
    private bool DestroyPlace(in MirTerminator drop, int group)
    {
        if (!this.OwnsStorage(drop.Place))
        {
            return true; // A Region's bytes are the frame's (FunctionCodegen.Frame).
        }

        if (this.body.TypeOf(drop.Place) is not { } dropped || this.collector.TypeOf(this.item, dropped) is not { } type)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source); // The collector made every Type of an admitted item.
            return false;
        }

        if (!MirTypes.MayNeedDrop(type, this.seen))
        {
            return true;
        }

        var at = new MirLocation(this.at, this.body.Blocks[this.at].Count);
        var targets = this.collector.Set.Targets(this.item);
        this.destruction = drop.Source;
        (this.read, this.end) = this.Target(at) < 0 ? (this.cursor, this.cursor) : (this.cursor - 1, this.cursor); // The site's first target.
        while (this.end < targets.Length && targets[this.end].At == at)
        {
            this.end++; // The site's others, in the walk's order.
        }

        this.cursor = this.end;
        if (!this.Address(drop.Place, out var address) || !this.Location(drop.Source, out var site))
        {
            return false; // A broken contract, recorded.
        }

        this.Open(TestRuntime.Phase.Cleanup); // A test build's destruction runs in the cleanup phase (FunctionCodegen.Phases).
        this.DestroyByRow(-1, group, type, this.GlueOf(group), address, 0, site);
        if (this.read != this.end)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source); // The collector targets exactly what the group destroys.
        }

        return true;
    }

    // A value or a part of a Type at an offset from an address by its row (R4-M6a): the child row's verdict, Keep and Flag its glue under
    // the row's flag (Keep's none), Elide nothing (no address either), Open its own level by its child rows (Level), with no glue of its
    // own; without a row (child -1), its glue under its parent's own bit, the parent's flag (none: set on every path). The glue is the
    // destruction's target the caller read (GlueOf), -1 for none.
    private void DestroyByRow(int parent, int child, BoundType type, int glue, Value address, long offset, Site site)
    {
        var (verdict, flag) = child >= 0 ? (this.DropRow(child).Verdict, this.DropRow(child).Flag) : (MirDropVerdict.Keep, this.Own(parent));
        switch (verdict)
        {
            case MirDropVerdict.Keep or MirDropVerdict.Flag when glue >= 0:
                var guard = this.Test(flag);
                this.Destroy(this.module.Glue(glue), this.Offset(address, offset), site);
                this.Join(guard);
                return;
            case MirDropVerdict.Keep or MirDropVerdict.Flag or MirDropVerdict.Elide:
                return; // Nothing remains, or a missing target, recorded (NextTarget).
            case MirDropVerdict.Open:
                this.Level(type, child, this.Offset(address, offset), site);
                return;
            default:
                throw new InvalidOperationException("A drop verdict has no code generation row.");
        }
    }

    // The glue a row's destruction calls, the destruction's next target (DropGlue.Destroyed: a row's Type whole, an Elide row's too until
    // R3-U2b); none (-1) for an Open row, whose parts read theirs.
    private int GlueOf(int row) => row >= 0 && this.DropRow(row).Verdict == MirDropVerdict.Open ? -1 : this.NextTarget() ?? -1;

    // The body's drop table rows a level reads under a row: none for no row, a glue function's.
    private ReadOnlySpan<MirDropRow> DropRows(int row) => row < 0 ? default : this.program.Drops.RowsOf(this.body.Ordinal);

    // A row of the body's drop table, by its index in RowsOf.
    private MirDropRow DropRow(int index) => this.program.Drops.RowsOf(this.body.Ordinal)[index];

    // The flag of a row's own bit, which its parts without a row follow: -1 (none, set on every path) for no row, a glue function's.
    private int Own(int row) => row < 0 ? -1 : this.DropRow(row).Flag;
}
