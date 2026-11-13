// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The owning interpolation's row of FunctionCodegen (R5 plans C9c and C9d; SPEC 22.10.3, 22.10.5; DECISIONS R3-10, R5-M133): the temporary
// intrinsic FormatSegment (MirBuilder.Format), a routine of the formatting runtime (P1d's, one text, PD2). A step's
// estimate (Estimate) is computed in the item's instance from the step's own constants (R3-10): its literal bytes plus the bound of each
// value Type of its Types token (LegacyRuntimeHelpers.FormatEstimate over FormattingTypes.Bound), each Type through TypeOf (a write's Type
// argument, which the collector made). The private buffer is the library's (MirBuilder.Heap: Text.heap(0), a body; R8-W2 c3b), and so
// is its end (MirBuilder.Finish: Utf8Writer.finish and HeapBuffer.intoString, bodies; R8-W2 c3a); a direct line's region is MIR's
// (MirBuilder.Line: a fixed array local, Text.fixed and FixedBuffer.text, bodies), not this row's.
// - FormatSegment, one step per write position: __kimi_format_hint of the adapter and the hint its estimate of the later writes gives.
//   The position's write follows its step in MIR, a literal's text a library call (MirBuilder.Text: Utf8Writer.deferLiteral or string's
//   built-in format; R8-W2 c1); only the hint depends on the instance. On a direct line's FixedBuffer adapter (kind 0) the hint is inert.
// A step's constants are read unchecked (MirValidator's Constants row); a value Type TypeOf cannot make is a broken contract
// (InternalInvariant_Kd).
internal sealed partial class FunctionCodegen
{
    // FormatSegment (R3-10's FormatHint; DECISIONS R5-M133): the adapter (operand 0) given the hint of the later literal bytes and values
    // (operands 1 and 2).
    private void FormatSegment(in MirTerminator call, in IntrinsicRow row)
    {
        if (this.Estimate(call, 1) is not { } estimate || !this.Operand(this.body.Operands[call.Start], out var adapter))
        {
            return; // Recorded.
        }

        this.module.Request(HelperNeeds.FormattingRuntime);
        _ = this.WriteCall(row.Routine!, [adapter, new('\0', estimate.Hint)]);
        this.Finish(call, null);
    }

    // R3-10: a step's estimate in the item's instance, its literal bytes the Integer constant at operand `at`, then each value Type of the
    // Types token at `at + 1`; null after a record.
    private FormatEstimate? Estimate(in MirTerminator call, int at)
    {
        var values = this.Constant(call, at + 1).Type.Components;
        var estimate = default(FormatEstimate).Add((long)this.Constant(call, at).Bits);
        for (var i = 0; i < values.Count; i++)
        {
            if (this.collector.TypeOf(this.item, values[i]) is not { } type)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // Each is a write's Type argument, which the collector made.
                return null;
            }

            estimate = estimate.Add(FormattingTypes.Bound(type));
        }

        return estimate;
    }

    // A step's constant at an operand position, of the kind MirValidator's Constants row gives that position.
    private MirConst Constant(in MirTerminator call, int at) => this.body.Consts[this.body.Operands[call.Start + at].Arg];
}
