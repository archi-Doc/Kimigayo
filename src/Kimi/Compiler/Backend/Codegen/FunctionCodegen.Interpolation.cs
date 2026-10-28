// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The owning interpolation's rows of FunctionCodegen (R5 plans C9c and C9d; SPEC 22.7, 22.10.3, 22.10.5; DECISIONS R3-10, R5-M131, R5-M133):
// the temporary intrinsics FormatBuffer, FormatSegment and FormatFinish (MirBuilder.Format), each a routine of the formatting runtime (P1d's,
// one text, PD2) given the words its estimate gives. A step's estimate (Estimate) is computed in the item's instance from the step's own
// constants (R3-10): its literal bytes plus the bound of each value Type of its Types token (LegacyRuntimeHelpers.FormatEstimate over
// FormatBound), each Type through TypeOf (a write's Type argument, which the collector made).
// - FormatBuffer: when its estimate places the buffer on the stack (FormatEstimate.Stack: passed directly to Console.writeLine, bounded and
//   within the windows-x64 limit), the frame's walk gives its destination a Region home of the estimate's total (Region,
//   FunctionCodegen.Frame), and the buffer is __kimi_text_fixed over that region; else __kimi_text_heap of the estimate's capacity at the
//   interpolation's location.
// - FormatSegment, one step per write position: __kimi_format_hint of the adapter and the hint its estimate of the later writes gives;
//   then the position's literal text when not empty (a literal segment's; a value's write follows its step in MIR): deferred to the
//   adapter's next reservation when a value follows, its Types token not empty (__kimi_format_literal: the size checked at once, the static
//   bytes kept until then or flushed by the end), else written (WriterWrite's built-in routine), the string's kind given, and the adapter
//   checked (__kimi_format_check: KIMI_E_FORMAT at the interpolation). Its slots are the frame walk's (TextSlots). Whether to defer is
//   fixed by the template; only the hint depends on the instance. Allocations and bytes are the old generator's (DECISIONS R5-M133).
// - FormatFinish: __kimi_format_finish over the moved adapter and its buffer, the adapter's destination (Utf8Writer's Field 0), at the
//   interpolation's location; or, when the same rule over its own operands (R3-10 gives it FormatBuffer's) places the buffer on the stack,
//   __kimi_format_stack_finish, which marks the string static.
// A Region local's value owns no storage, so its Drop destroys nothing (FunctionCodegen.Drops, OwnsStorage). The adapter over a region
// keeps its destination Type's writer kind (FunctionCodegen.Formatting's Writer): the region's size is the instance maximum of every
// reservation (the literals, a flushed deferred one included, and the bounded values' maxima), so the HeapBuffer arm of the runtime's
// reserve never grows it.
// A step whose constants are not its row's, or a value Type TypeOf cannot make, is a broken contract (InternalInvariant_Kd).
internal sealed partial class FunctionCodegen
{
    // Utf8Writer's Field holding the destination the adapter borrows, the interpolation's buffer (Formatting.kimi; the runtime's word 0).
    private const int AdapterDestination = 0;

    // The frame's row of a FormatBuffer (FunctionCodegen.Arguments' walk, before the prologue): when its estimate places the buffer on the
    // stack, its destination, a whole local, becomes a Region of the estimate's total.
    private void Region(in MirTerminator call)
    {
        if (this.Estimate(call, 0) is not { } estimate || this.StepConstant(call, 2, MirConstKind.Bool) is not { } direct ||
            !estimate.Stack(direct.Bits != 0))
        {
            return; // On the heap, or a broken contract, recorded.
        }

        if (call.Place is not { Root: >= 0, Projection: 0 } place || this.slots[place.Root].Home != Home.Slot)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // The builder's buffer is a temp local of its own.
            return;
        }

        this.slots[place.Root] = this.slots[place.Root] with { Home = Home.Region, Index = (int)estimate.Total };
    }

    // FormatBuffer (R3-10): the private buffer into its destination, over its Region's bytes or on the heap (above).
    private void FormatBuffer(in MirTerminator call, in IntrinsicRow row)
    {
        if (this.Estimate(call, 0) is not { } estimate || !this.Address(call.Place, out var buffer))
        {
            return; // Recorded.
        }

        this.module.Request(HelperNeeds.FormattingRuntime);
        if (call.Place is { Root: >= 0, Projection: 0 } && this.slots[call.Place.Root] is { Home: Home.Region, Index: var size })
        {
            var region = LegacyRuntimeHelpers.FormattingRow(CompilerFunctionKind.TextFixed)!.Abi;
            _ = this.WriteCall(region, [buffer, new('r', call.Place.Root), new('\0', size)]);
        }
        else if (this.Location(call.Source, out var site))
        {
            _ = this.WriteCall(row.Routine!, [buffer, new('\0', estimate.Capacity), site.Text, site.Length]);
        }
        else
        {
            return; // Recorded.
        }

        this.Finish(call, null);
    }

    // The frame's row of a FormatSegment (FunctionCodegen.Arguments' walk, before the prologue): for a non-empty text, its record's copy slot
    // (CopySlot's string-constant rule) and the slot its write's result takes, the runtime's Result tag, numbered by the call's block as a
    // Value callee's copy is (CalleeCopy), since a block's terminator is its one call.
    private void TextSlots(in MirTerminator call, int block)
    {
        var text = call.Start + 1;
        if (call.Count > 1 && this.TextOf(this.body.Operands[text]) is { Length: > 0 })
        {
            this.CopySlot(this.body.Operands[text], text);
            this.AddSlot(this.body.Operands.Length + block, Layout.Tag.Layout);
        }
    }

    // FormatSegment (R3-10's FormatHint widened; DECISIONS R5-M133): the adapter (operand 0) given the hint of the later literal bytes and
    // values (operands 2 and 3), then the position's literal text (operand 1) when not empty (SegmentText).
    private void FormatSegment(in MirTerminator call, in IntrinsicRow row)
    {
        if (this.Estimate(call, 2) is not { } estimate || this.StepConstant(call, 1, MirConstKind.String) is not { Value: string text } ||
            !this.Operand(this.body.Operands[call.Start], out var adapter))
        {
            return; // Recorded.
        }

        this.module.Request(HelperNeeds.FormattingRuntime);
        _ = this.WriteCall(row.Routine!, [adapter, new('\0', estimate.Hint)]);
        if (text.Length > 0 && !this.SegmentText(call, adapter))
        {
            return; // Recorded.
        }

        this.Finish(call, null);
    }

    // SPEC 22.10.3, 22.10.5 (DECISIONS R5-M133): a segment's literal text after its hint: deferred to the adapter's next reservation when a
    // value follows (the step's Types token not empty), else written, the string's kind given, its record in its copy slot and its result in
    // the step's slot (TextSlots); then the adapter checked, a recorded failure Aborting at the interpolation. False after a record.
    private bool SegmentText(in MirTerminator call, Value adapter)
    {
        var text = call.Start + 1;
        if (!this.Location(call.Source, out var site) ||
            !this.Argument(this.body.Operands[text], this.copyOf[text], AbiParameterKind.OwnedSlot, out var record))
        {
            return false; // Recorded.
        }

        var routine = this.StepConstant(call, 3, MirConstKind.TypeToken)!.Value.Type.Components.Count > 0 ? LegacyRuntimeHelpers.FormatLiteral
            : LegacyRuntimeHelpers.FormattingRow(CompilerFunctionKind.WriterWrite)!.Abi;
        var kind = new Value('\0', LegacyRuntimeHelpers.FormatKind(BoundType.String));
        _ = this.WriteCall(routine, [new('s', this.CalleeCopy()), adapter, record, kind, site.Text, site.Length]);
        _ = this.WriteCall(LegacyRuntimeHelpers.FormatCheck, [adapter, site.Text, site.Length]);
        return true;
    }

    // FormatFinish (R3-10): the string into its destination from the moved adapter (operand 0) and its buffer, by the estimate of FormatBuffer's
    // operands (1 to 3) it repeats: the stack form for a buffer on the stack, else the row's routine.
    private void FormatFinish(in MirTerminator call, in IntrinsicRow row)
    {
        var operand = this.body.Operands[call.Start];
        if (this.Estimate(call, 1) is not { } estimate || this.StepConstant(call, 3, MirConstKind.Bool) is not { } direct ||
            this.LoweringOf(operand) is not { } adapterLowering)
        {
            return; // Recorded.
        }

        if (!this.Address(call.Place, out var result) || !this.Operand(operand, out var adapter) || !this.Location(call.Source, out var site))
        {
            return; // Recorded.
        }

        this.module.Request(HelperNeeds.FormattingRuntime);
        var buffer = this.LoadAt(this.Offset(adapter, adapterLowering.Layout.FieldOffsets.Span[AdapterDestination]), PointerLayout);
        var routine = estimate.Stack(direct.Bits != 0) ? LegacyRuntimeHelpers.FormatStackFinish : row.Routine!;
        _ = this.WriteCall(routine, [result, buffer, adapter, site.Text, site.Length]);
        this.Finish(call, null);
    }

    // R3-10: a step's estimate in the item's instance, its literal bytes the Integer constant at operand `at`, then each value Type of the
    // Types token at `at + 1`; null after a record.
    private FormatEstimate? Estimate(in MirTerminator call, int at)
    {
        if (this.StepConstant(call, at, MirConstKind.Integer) is not { } bytes ||
            this.StepConstant(call, at + 1, MirConstKind.TypeToken) is not { Type.Components: var values })
        {
            return null;
        }

        var estimate = FormatEstimate.Empty.Add((long)bytes.Bits);
        for (var i = 0; i < values.Count; i++)
        {
            if (this.collector.TypeOf(this.item, values[i]) is not { } type)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // Each is a write's Type argument, which the collector made.
                return null;
            }

            estimate = estimate.Add(LegacyRuntimeHelpers.FormatBound(type));
        }

        return estimate;
    }

    // A step's constant of a kind at an operand position; null after a record (the builder writes each, MirBuilder.Estimate).
    private MirConst? StepConstant(in MirTerminator call, int at, MirConstKind kind)
    {
        if (at < call.Count && this.body.Operands[call.Start + at] is { Kind: MirOperandKind.Const } operand &&
            this.body.Consts[operand.Arg] is var constant && constant.Kind == kind)
        {
            return constant;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
        return null;
    }
}
