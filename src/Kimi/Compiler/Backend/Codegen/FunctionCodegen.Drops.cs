// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Backend;

// The Drop terminator of FunctionCodegen and the DropGlue items' functions (R5 package C 3.6, plan C4a; C0c brings the Keep and Elide
// verdicts and the string glue forward, DECISIONS R5-M78): A2's verdict for the item's view (R4-M6a), Keep destroying exactly when the
// dropped value's instance Type may need a drop, by calling its DropGlue item, the collector's target at the Drop, with the Place's address
// and the Drop's location text, Elide writing nothing; Flag (C4b) and Open (C4c) are rows not written yet. A glue function calls its Type's
// routine (DropGlue.Release) with its own parameters through the call writer (PD3); ModuleCodegen names and signs the item.
// Gate step 2's stand-in (R5-M78, removed by C4a's per-body step 2, package C 3.8, which reads R4-M71's MirProblem.Body): under sel the
// old analysis decides acceptance, so the MIR analyses' problems refuse nothing, and A2's verdicts are exact only for programs A1 accepts
// (R4-M6a); a Drop is written only for a program whose MIR analyses recorded no Ownership problem (A1 with its arena record, A2, and
// conservatively A4 and A5; A3's do not gate, PD12), else it is one located Unsupported_Kd.
internal sealed partial class FunctionCodegen
{
    private readonly HashSet<BoundType> seen = new(); // MirTypes.MayNeedDrop's scratch.
    private MirProgram program = null!;
    private ulong exactVersion; // The program version whose exactness is known (0: none).
    private bool exact;
    private int at; // The block being written.

    /// <summary>Writes the function of a DropGlue item by its Type's row (<see cref="DropGlue.Release"/>): the routine called with the
    /// function's own parameters, in order; a Type without a row is C4a's, one located <c>Unsupported_Kd</c> at the item's site.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The DropGlue item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteGlue(InstanceCollector collector, int item, TextWriter output)
    {
        var entry = collector.Set.Items[item];
        if (DropGlue.Release(entry.RepresentativeType!) is not { } release)
        {
            this.module.Report(DiagnosticCode.Unsupported_Kd, entry.Site);
            return;
        }

        var parameters = release.Parameters.Length;
        this.arguments = this.arguments.Length < parameters ? new Value[parameters] : this.arguments;
        for (var i = 0; i < parameters; i++)
        {
            this.arguments[i] = new('p', i);
        }

        (this.output, this.abi) = (output, this.module.Glue(item));
        output.Write(this.abi.GetDefinition(exported: false));
        output.Write("entry:\n");
        _ = this.WriteCall(release, this.arguments.AsSpan(0, parameters));
        output.Write("  ret void\n}\n");
    }

    private void Drop(in MirTerminator drop)
    {
        if (!this.Exact())
        {
            this.Report(DiagnosticCode.Unsupported_Kd, drop.Source); // C4a's per-body gate step 2.
            return;
        }

        var table = this.program.Drops;
        var group = table.Group(this.body.Ordinal, this.at, this.collector.Set.Items[this.item].View);
        if (group < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, drop.Source); // PD12: A1 and A2 judged every runtime Drop of the body.
            return;
        }

        switch (table.RowsOf(this.body.Ordinal)[group].Verdict)
        {
            case MirDropVerdict.Keep:
                if (!this.Keep(drop))
                {
                    return;
                }

                break;
            case MirDropVerdict.Elide:
                break; // Nothing is destroyed.
            case MirDropVerdict.Flag or MirDropVerdict.Open:
                this.Report(DiagnosticCode.Unsupported_Kd, drop.Source); // C4b tests the flag, C4c destroys the remaining parts.
                return;
            default:
                throw new InvalidOperationException("A drop verdict has no code generation row.");
        }

        this.output.Write("  br ");
        this.WriteLabel(drop.Target);
        this.output.Write('\n');
    }

    // Keep destroys exactly when the dropped value's instance Type may need a drop (MirTypes.MayNeedDrop, the rule the collector targets a
    // Drop's glue by, K package 4.5 row 9): refuted, nothing is written; else the collector's target, the Type's DropGlue item, is called on
    // the Place's address with the Drop's location, and a missing target is a broken contract (package C 3.2 I8). False after a record.
    private bool Keep(in MirTerminator drop)
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

        var location = this.Location(drop.Source);
        if (location < 0)
        {
            return false;
        }

        this.arguments = this.arguments.Length < 3 ? new Value[3] : this.arguments;
        (this.arguments[0], this.arguments[1], this.arguments[2]) = (address, new('@', location), new('\0', this.module.Constants[location].ByteLength));
        _ = this.WriteCall(this.module.Glue(glue), this.arguments.AsSpan(0, 3));
        return true;
    }

    // Whether the program's MIR analyses recorded no Ownership problem, once per program version (the stand-in above).
    private bool Exact()
    {
        if (this.exactVersion != this.program.Version)
        {
            this.exact = true;
            foreach (var problem in this.program.Problems.All)
            {
                this.exact &= problem.Requirement.Partition != DiagnosticPartition.Ownership;
            }

            this.exactVersion = this.program.Version;
        }

        return this.exact;
    }
}
