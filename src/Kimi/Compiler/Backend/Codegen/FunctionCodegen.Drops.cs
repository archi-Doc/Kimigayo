// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Testing;

namespace Kimi.Compiler.Backend;

// The Drop terminator and A2's verdict rows of FunctionCodegen (R5 package C 3.6, plans C4a, C4b and C4c; C0c brings the Keep and Elide
// verdicts forward, DECISIONS R5-M78, R5-M99, R5-M100): A2's verdict group for the item's view (R4-M6a), its first row the dropped Place's.
// A value is destroyed exactly when its instance Type may need a drop, by its group's walk (DropGlue.Destroyed, PLAN G641; written by
// FunctionCodegen.Glue's Destroys), the walk the collector targets the Drop by, reading the collector's targets at the Drop in its order:
// a Keep or Flag group calls the value's DropGlue item with the Place's address and the Drop's location text through the one
// destruction-call writer (FunctionCodegen.Glue's Destroy), under the row's flag (FunctionCodegen.Flags' test; Keep's is none, so
// always); Elide writes nothing; Open destroys what remains after a Partial Move (SPEC 16.3.2, 15.1.3), each part in destruction order by
// its governing row, a part without one by its parent's own bit, a component no Place names having no path of its own (MirMoveData).
// In a test build a destruction runs in the cleanup
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

    // SPEC 16.2.1 (R4-M6a): the dropped value destroyed by its group's first row, exactly when its instance Type may need a drop
    // (MirTypes.MayNeedDrop, the rule the collector targets a Drop's glue by, K package 4.5 row 9): refuted, nothing is written, no test
    // either; else by the collector's targets at the Drop (I8's cursor, Target), what the group destroys in the walk's order
    // (DropGlue.Destroyed), on the Place's address with the Drop's location; a target missing (NextTarget) or left over is a broken
    // contract (package C 3.2 I8). False after a record.
    private bool DestroyPlace(in MirTerminator drop, int group)
    {
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
        this.Destroys(group, type, address, site); // FunctionCodegen.Glue: the group's walk.
        if (this.read != this.end)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source); // The collector targets exactly what the group destroys.
        }

        return true;
    }

    // A row of the body's drop table, by its index in RowsOf.
    private MirDropRow DropRow(int index) => this.program.Drops.RowsOf(this.body.Ordinal)[index];

    // The flag of a row's own bit, which its parts without a row follow: -1 (none, set on every path) for no row, a glue function's.
    private int Own(int row) => row < 0 ? -1 : this.DropRow(row).Flag;
}
