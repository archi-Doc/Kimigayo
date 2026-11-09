// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The formatting rows of FunctionCodegen (R5 plans C9a and C9b; SPEC 22.7, 22.9.2, 22.9.4; DECISIONS R5-M128, R5-M130): a call of
// writeLine(Utf8Slice), the one Body-disposition kind whose legacy row's writer is Formatting (LegacyRuntimeHelpers.Row) until W1c-3, is
// the call of its row's routine in the formatting runtime through the one call writer, the module requesting the formatting runtime its row
// declares (Formatting). Text.writer, the ErasedWriter intrinsic's row (K package 4.5 row 6), calls P1d's TextWriter routine with the writer
// kind of its destination and the dispatch word of its reserve witness (Writer). The Text operations, the buffers, the window and the
// adapter's write are bodies (R8-W1a, W1b, W1c).
internal sealed partial class FunctionCodegen
{
    // A Formatting row's routine (above), the formatting runtime its row declares requested; null after a record at the source: no
    // formatting row's call carries a target or a kind Context (WriteLineUtf8's, the one Body kind left until W1c-3).
    private FunctionAbi? Formatting(LegacyRow row, int target, SourceInfo source, out ReadOnlySpan<Value> given)
    {
        given = default;
        if (target >= 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // The collector targets no formatting row's call.
            return null;
        }

        this.module.Request(row.Needs);
        return row.Routine;
    }

    // SPEC 22.9.2, 22.9.4, 22.10.1 (K package 4.5 row 6; DECISIONS R5-M130): Text.writer's call, its Item's Value call and its adapter's entry:
    // P1d's TextWriter routine, given its two Context words: the formatting runtime's writer kind of its destination W, the call's Type
    // argument (LegacyRuntimeHelpers.WriterKind), and the dispatch word: null for a FixedBuffer or a HeapBuffer, whose reserve the runtime runs
    // itself (the collector takes no target for them until WriterKind goes, InstanceCollector.Edges), else the function of W's reserve
    // witness, the call's Body target, whose signature is the runtime's dispatch call (Dispatch). Any other pairing is a broken contract:
    // Binding admits no virtual reserve witness (a virtual function's receiver is objref/Self or objuniq/Self, which Binding rejects or leaves
    // Unsupported there). Null after a record.
    private FunctionAbi? Writer(IntrinsicRow row, BoundType? destination, int target, out ReadOnlySpan<Value> given)
    {
        given = default;
        var kind = destination is null ? -1 : LegacyRuntimeHelpers.WriterKind(destination);
        if (this.Dispatch(kind, target) is not { } entry)
        {
            return null; // Recorded.
        }

        this.module.Request(LegacyRuntimeHelpers.FormattingRow(CompilerFunctionKind.TextWriter)!.Needs);
        this.module.Request(ModuleCodegen.Needs.ErasedWriter);
        (this.slotless[0], this.slotless[1]) = (new('\0', kind), entry);
        given = this.slotless.AsSpan(0, 2);
        return row.Routine;
    }

    // Text.writer's dispatch word (Writer): null for a FixedBuffer's or a HeapBuffer's writer kind without a target; for a user Writer's,
    // its Body target, a function without a virtual slot the runtime calls as its reserve (__kimi_writer_reserve's `void (ptr ret, ptr
    // destination, i64 minimum)`). Null after one record of a broken contract: BodyRun's, else this one.
    private Value? Dispatch(int kind, int target)
    {
        if (target < 0 && kind is 0 or 1)
        {
            return new Value('n', 0);
        }

        if (target >= 0 && kind == 2 && this.collector.Set.Items[target].Key.Kind == MonoKind.Body)
        {
            if (this.BodyRun(target) is not { } run)
            {
                return null; // Recorded.
            }

            if (run is { Slot: null, Signature: { Result: "void", NoReturn: false, ResultSlot: true } signature } &&
                signature.Parameters is [{ Kind: AbiParameterKind.ResultSlot }, { Type: "ptr" }, { Type: "i64" }])
            {
                return new Value('e', target);
            }
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets a user Writer's reserve witness (K 4.5 row 6).
        return null;
    }
}
