// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Mir;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Backend;

// The Drop terminator of FunctionCodegen (R5 package C 3.6, plan C4a; C0c brings the Keep and Elide verdicts forward, DECISIONS R5-M78): A2's
// verdict for the item's view (R4-M6a), Keep calling the dropped Type's DropGlue item, the collector's target at the Drop, with the
// Place's address and the Drop's location text, Elide writing nothing; Flag (C4b) and Open (C4c) are rows not written yet.
// Gate step 2's stand-in (R5-M78, removed by C4a's per-body step 2, package C 3.8, once R4 publishes MirProblem.Body, G125): under sel the
// old analysis decides acceptance, so the MIR analyses' problems refuse nothing, and A2's verdicts are exact only for programs A1 accepts
// (R4-M6a); a Drop is written only for a program whose MIR analyses recorded no Ownership problem (A1 with its arena record, A2, and
// conservatively A4 and A5; A3's do not gate, PD12), else it is one located Unsupported_Kd.
internal sealed partial class FunctionCodegen
{
    private FunctionAbi?[] glues = []; // Each DropGlue item's signature, by item.
    private MirProgram program = null!;
    private ulong exactVersion; // The program version whose exactness is known (0: none).
    private bool exact;
    private int at; // The block being written.

    /// <summary>Gets the signature of a DropGlue item (package C 3.6: <c>__kimi_drop</c> and the item, <see cref="DropGlue.Parameters"/>).</summary>
    /// <param name="item">The item.</param>
    /// <returns>The signature, kept per item.</returns>
    internal FunctionAbi Glue(int item)
    {
        if (item >= this.glues.Length)
        {
            Array.Resize(ref this.glues, Math.Max(item + 1, 2 * this.glues.Length));
        }

        return this.glues[item] ??= new("__kimi_drop" + item.ToString(CultureInfo.InvariantCulture), "void", DropGlue.Parameters);
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

    // Keep: the dropped Type's DropGlue item called on the Place's address with the Drop's location; false after a record. The collector
    // targets the glue of exactly the instance Types that may need a drop (K package 4.5 row 9), so a Keep of the body's view without a
    // target, a Type parameter whose instance needs none, destroys nothing.
    private bool Keep(in MirTerminator drop)
    {
        var glue = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (glue < 0)
        {
            return true;
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
        this.WriteCall(this.Glue(glue), this.arguments.AsSpan(0, 3));
        return true;
    }

    // The item the collector targets at a site of the item's body, or -1: its targets are in location order (K package 4.3).
    private int Target(MirLocation site)
    {
        var targets = this.collector.Set.Targets(this.item);
        var (low, high) = (0, targets.Length);
        while (low < high)
        {
            var middle = (low + high) >> 1;
            var here = targets[middle].At;
            (low, high) = here.Block < site.Block || (here.Block == site.Block && here.Index < site.Index) ? (middle + 1, high) : (low, middle);
        }

        return low < targets.Length && targets[low].At == site ? targets[low].Item : -1;
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
