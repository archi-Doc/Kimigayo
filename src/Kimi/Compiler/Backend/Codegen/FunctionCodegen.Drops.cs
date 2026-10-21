// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Drop terminator of FunctionCodegen (R5 package C 3.6, plans C4a and C4b; C0c brings the Keep and Elide verdicts forward, DECISIONS
// R5-M78, R5-M99): A2's verdict group for the item's view (R4-M6a). Keep and Flag destroy the dropped value exactly when its instance Type
// may need a drop, by calling its DropGlue item, the collector's target at the Drop, with the Place's address and the Drop's location text
// through the one destruction-call writer (FunctionCodegen.Glue's Destroy), under the row's flag: Keep's is none, so always; a Flag's
// where its flag is set (FunctionCodegen.Flags' test). Elide writes nothing; Open (C4c) is a row not written yet. The Drop ends through
// Branch, after its own clear point. FunctionCodegen.Glue.cs writes the glue functions (DECISIONS R5-M95). Gate step 2 has written only a
// body whose table is exact (FunctionCodegen.Write; DECISIONS R5-M96), so a runtime Drop without a group is a broken contract.
internal sealed partial class FunctionCodegen
{
    private readonly HashSet<BoundType> seen = new(); // MirTypes.MayNeedDrop's scratch.

    private void Drop(in MirTerminator drop)
    {
        var table = this.program.Drops;
        var group = table.Group(this.body.Ordinal, this.at, this.collector.Set.Items[this.item].View);
        if (group < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source); // PD12: A1 and A2 judged every runtime Drop of an exact body.
            return;
        }

        var row = table.RowsOf(this.body.Ordinal)[group];
        switch (row.Verdict)
        {
            case MirDropVerdict.Keep or MirDropVerdict.Flag:
                if (!this.DestroyPlace(drop, row.Flag))
                {
                    return;
                }

                break;
            case MirDropVerdict.Elide:
                break; // Nothing is destroyed.
            case MirDropVerdict.Open:
                this.Report(DiagnosticCode.Unsupported_Kd, drop.Source); // C4c destroys the remaining parts.
                return;
            default:
                throw new InvalidOperationException("A drop verdict has no code generation row.");
        }

        this.Branch(drop.Target);
    }

    // SPEC 16.2.1 (R4-M6a): the dropped value destroyed under a flag (-1: none, a Keep row's), exactly when its instance Type may need a drop
    // (MirTypes.MayNeedDrop, the rule the collector targets a Drop's glue by, K package 4.5 row 9): refuted, nothing is written, no test
    // either; else the collector's target, the Type's DropGlue item, is called on the Place's address with the Drop's location under the
    // flag's test, and a missing target is a broken contract (package C 3.2 I8). False after a record.
    private bool DestroyPlace(in MirTerminator drop, int flag)
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

        var glue = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (glue < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source);
            return false;
        }

        if (!this.Address(drop.Place, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, drop.Source);
            return false;
        }

        if (!this.Location(drop.Source, out var site))
        {
            return false;
        }

        var guard = this.Test(flag);
        this.Destroy(this.module.Glue(glue), address, site);
        this.Join(guard);
        return true;
    }
}
