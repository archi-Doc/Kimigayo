// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Testing;

namespace Kimi.Compiler.Backend;

// The Drop terminator and A2's verdict rows of FunctionCodegen (R5 package C 3.6, plans C4a, C4b and C4c; C0c brings the Keep and Elide
// verdicts forward, DECISIONS R5-M78, R5-M99, R5-M100): A2's verdict group for the item's view (R4-M6a), its first row the dropped Place's.
// A value is destroyed by its row (DestroyByRow) exactly when its instance Type may need a drop: Keep and Flag by calling its DropGlue item,
// the collector's target at the Drop, with the Place's address and the Drop's location text through the one destruction-call writer
// (FunctionCodegen.Glue's Destroy), under the row's flag (FunctionCodegen.Flags' test; Keep's is none, so always); Elide writes nothing;
// Open destroys what remains after a Partial Move (SPEC 16.3.2, 15.1.3): one level of the value through its glue item (FunctionCodegen.Glue's
// Level), each part in destruction order by its child row, found by the part's move-path step (Governing), and a part without one by its
// parent's own bit, a component no Place names having no path of its own (MirMoveData). In a test build a destruction runs in the cleanup
// phase (FunctionCodegen.Phases; DECISIONS R5-M137). The Drop ends through Branch, after its own clear point. Gate step 2 has written only
// a body whose table is exact (FunctionCodegen.Write; DECISIONS R5-M96), so a runtime Drop without a group is a broken contract, and no
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
    // nothing is written, no test either; else by the collector's target, the Type's DropGlue item, on the Place's address with the Drop's
    // location, and a missing target is a broken contract (package C 3.2 I8). False after a record.
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

        var glue = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (glue < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source);
            return false;
        }

        if (!this.Address(drop.Place, out var address) || !this.Location(drop.Source, out var site))
        {
            return false; // A broken contract, recorded.
        }

        this.Open(TestRuntime.Phase.Cleanup); // A test build's destruction runs in the cleanup phase (FunctionCodegen.Phases).
        this.DestroyByRow(-1, group, glue, address, 0, site);
        return true;
    }

    // A value or a part at an offset from an address by its row (R4-M6a): the child row's verdict, Keep and Flag its glue under the row's
    // flag (Keep's none), Elide nothing (no address either), Open its own level by its child rows (Level); without a row (child -1), its
    // glue under its parent's own bit, the parent's flag (none: set on every path). The glue is the target's.
    private void DestroyByRow(int parent, int child, int glue, Value address, long offset, Site site)
    {
        var (verdict, flag) = child >= 0 ? (this.DropRow(child).Verdict, this.DropRow(child).Flag) : (MirDropVerdict.Keep, this.Own(parent));
        switch (verdict)
        {
            case MirDropVerdict.Keep or MirDropVerdict.Flag:
                var guard = this.Test(flag);
                this.Destroy(this.module.Glue(glue), this.Offset(address, offset), site);
                this.Join(guard);
                return;
            case MirDropVerdict.Elide:
                return; // Nothing remains.
            case MirDropVerdict.Open:
                this.Level(glue, child, this.Offset(address, offset), site);
                return;
            default:
                throw new InvalidOperationException("A drop verdict has no code generation row.");
        }
    }

    // The rows governing a direct part under an Open row (-1: none, a glue function's): its child row, named by the part's move-path step
    // (MirMoveData; the bridge from Component's numbering, which DropPart carries: a struct's own Field by its index, its position less one
    // after a base, the base by one Base step, a Tuple's element by its index); an enum Case payload element through its Case's row, by
    // that row's child at the payload's Field when the Case's row is Open, else by the Case's row; -1 for none, its parent's own bit.
    private (int Parent, int Child) Governing(BoundType type, in DropPart part, int row)
    {
        if (AdtDef.IsEnum(type))
        {
            var payload = this.Child(row, MirProjectionKind.Downcast, part.Ordinal);
            return payload >= 0 && this.DropRow(payload).Verdict == MirDropVerdict.Open
                ? (payload, this.Child(payload, MirProjectionKind.Field, part.Index)) : (row, payload);
        }

        var based = AdtDef.Base(type) is not null; // Component's numbering: a struct's base first, then its Fields.
        if (based && part.Index == 0)
        {
            return (row, this.Child(row, MirProjectionKind.Base, 1));
        }

        return (row, this.Child(row, MirProjectionKind.Field, based ? part.Index - 1 : part.Index));
    }

    // The child row of a row by its step, among the rows of its depth below it in the group's pre-order; -1 for none or no row.
    private int Child(int row, MirProjectionKind step, int arg)
    {
        if (row < 0)
        {
            return -1;
        }

        var rows = this.program.Drops.RowsOf(this.body.Ordinal);
        for (var child = row + 1; child < rows.Length && rows[child].Depth > rows[row].Depth; child++)
        {
            if (rows[child].Depth == rows[row].Depth + 1 && rows[child].Step == step && rows[child].Arg == arg)
            {
                return child;
            }
        }

        return -1;
    }

    // The child row of an Open fixed array's row at the highest element position not above last (each a ConstIndex step), or -1.
    private int Highest(int row, long last)
    {
        if (row < 0)
        {
            return -1;
        }

        var rows = this.program.Drops.RowsOf(this.body.Ordinal);
        var found = -1;
        for (var child = row + 1; child < rows.Length && rows[child].Depth > rows[row].Depth; child++)
        {
            if (rows[child].Depth == rows[row].Depth + 1 && rows[child].Arg <= last && (found < 0 || rows[child].Arg > rows[found].Arg))
            {
                found = child;
            }
        }

        return found;
    }

    // A row of the body's drop table, by its index in RowsOf.
    private MirDropRow DropRow(int index) => this.program.Drops.RowsOf(this.body.Ordinal)[index];

    // The flag of a row's own bit, which its parts without a row follow: -1 (none, set on every path) for no row, a glue function's.
    private int Own(int row) => row < 0 ? -1 : this.DropRow(row).Flag;
}
