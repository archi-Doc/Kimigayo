// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The formatting rows of FunctionCodegen (R5 plans C9a and C9b, package C 3.2; SPEC 22.7, 22.9, 22.10.1, 22.10.5; DECISIONS R5-M30, R5-M128,
// R5-M130): a call of a Text, buffer, window or writer function or of writeLine(Utf8Slice), the Body-disposition kinds whose legacy row's
// writer is Formatting (LegacyRuntimeHelpers.Row), is the call of its row's routine in the formatting runtime (P1d's row ABI, one text, PD2)
// through the one call writer, at its direct call, its Item's Value call and its Item adapter's entry alike (SPEC 7.7.4), the module
// requesting the formatting runtime its row declares (ModuleCodegen.Request). Its Context words are facts the call's Types fix:
// - a capacity Context, which names a parameter by its logical index, is the length of the fixed array that parameter receives (SPEC
//   22.9.2's N): measured by the call writer from the operand's instance Type (Pass), or by an adapter's entry from its own input Type, its
//   target Function Type's (Forward) (Measure);
// - a kind Context, which names none, is the formatting runtime's kind of the call's witness target's Self (LegacyRuntimeHelpers.FormatKind),
//   the Type K6 resolved below the published Formats row's parameter (DECISIONS R5-M30), given here (WitnessKind).
// A row whose call carries a witness target (WriterWrite, TextToString, TextTryFormat; K package 4.5 row 2) is decided by the target's kind:
// a Builtin item (BuiltinFormat on a built-in formattable Type) runs through the row's built-in routine, which is SPEC 22.10.1's frame around
// the runtime's built-in formatting of that kind, toString reserving its exact length once (SPEC 22.10.5); a Body item, a user or library
// formatter, through the row's wrapper over it (ModuleCodegen.Wrapper): the write wrapper, SPEC 22.10.1's frame around the witness's call
// (WriteWrapper), or a conversion over the write wrapper (the old generator's text, LegacyRuntimeHelpers.WriteFormattingConversion); any
// other is a broken contract. Text.writer, the ErasedWriter intrinsic's row (K package 4.5 row 6), calls P1d's TextWriter routine with the
// writer kind of its destination and the dispatch word of its reserve witness (Writer).
internal sealed partial class FunctionCodegen
{
    /// <summary>Writes a user or library formatter's write wrapper (SPEC 22.10.1; DECISIONS R5-M130): the adapter's write over a Body
    /// target, its recorded failure tested through the runtime (<c>__kimi_format_healthy</c>): when one is recorded, it is returned
    /// (<c>__kimi_writer_fail</c>); otherwise the witness is called once through its run (BodyRun, its slot included; CallWitness) with the
    /// value and the adapter at its logical inputs 0 and 1, and a returned failure or a failure a nested write recorded is recorded and
    /// returned the same way. No format witness takes a location (none is caller-location), so the wrapper takes none.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="target">The Body target, the witness's item.</param>
    /// <param name="wrapper">The wrapper's signature (ModuleCodegen.Wrapper): the result slot, the adapter and the value.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteWrapper(InstanceCollector collector, int target, FunctionAbi wrapper, TextWriter output)
    {
        (this.collector, this.item, this.output, this.next, this.abi) = (collector, target, output, 0, wrapper);
        this.body = this.program.Bodies[collector.Set.Items[target].BodyOrdinal];
        output.Write(wrapper.GetDefinition(exported: false));
        output.Write("entry:\n");
        var ret = new Value('p', 0);
        var adapter = new Value('p', 1);
        var value = new Value('p', 2);
        var id = this.next++;
        this.Branch(this.WriteCall(LegacyRuntimeHelpers.FormatHealthy, [adapter])!.Value, "%format", "%failure", id);
        Name(output, "format", id);
        output.Write(":\n");
        if (this.BodyRun(target) is not { } run || !this.CallWitness(run, [value, adapter], ret, null, out _))
        {
            return; // Recorded.
        }

        var tag = Layout.Tag;
        var returned = this.Compared("eq", tag.ComputationType, this.LoadAt(ret, tag.Layout), "0");
        var healthy = this.WriteCall(LegacyRuntimeHelpers.FormatHealthy, [adapter])!.Value;
        this.Branch(this.Combine("and", "i1", returned, healthy), "%done", "%failure", id);
        Name(output, "failure", id);
        output.Write(":\n");
        _ = this.WriteCall(LegacyRuntimeHelpers.WriterFail, [ret, adapter]);
        Name(output, "  br label %done", id);
        output.Write('\n');
        Name(output, "done", id);
        output.Write(":\n  ret void\n}\n");
    }

    // A Formatting row's routine (above) with its kind Context given from its witness target into `given`, or a Body target's wrapper
    // (ModuleCodegen.Wrapper, which requests its needs), and the formatting runtime its row declares requested; null after a record at the
    // source: only a kind-bearing row has a target, a Builtin or a Body one (K package 4.5 row 2).
    private FunctionAbi? Formatting(LegacyRow row, int target, SourceInfo source, out ReadOnlySpan<Value> given)
    {
        given = default;
        var routine = row.Routine!;
        var count = 0;
        foreach (var parameter in routine.Parameters)
        {
            count += parameter is { Kind: AbiParameterKind.Context, LogicalIndex: < 0 } ? 1 : 0;
        }

        if (count > 0 && target >= 0 && this.collector.Set.Items[target].Key.Kind == MonoKind.Body)
        {
            return this.module.Wrapper(target, routine);
        }

        var kind = this.WitnessKind(target);
        if (count > 0 ? kind < 0 : target >= 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // The collector targets a kind-bearing row's call at its format witness (K6).
            return null;
        }

        this.module.Request(row.Needs);
        this.slotless.AsSpan(0, count).Fill(new Value('\0', kind));
        given = this.slotless.AsSpan(0, count);
        return routine;
    }

    // SPEC 22.10.1-22.10.2: the kind Context of a formatting row's Builtin witness target, the formatting runtime's number of its Self
    // (LegacyRuntimeHelpers.FormatKind); -1 without a target, for another item or for a Self without a kind.
    private int WitnessKind(int target)
        => target >= 0 && this.collector.Set.Items[target] is { Key: { Kind: MonoKind.Builtin, Symbol.CompilerFunction: var function } } entry &&
            function == CompilerFunctionKind.BuiltinFormat ? LegacyRuntimeHelpers.FormatKind(entry.RepresentativeType!) : -1;

    // SPEC 22.9.2, 22.9.4, 22.10.1 (K package 4.5 row 6; DECISIONS R5-M130): Text.writer's call, its Item's Value call and its adapter's entry:
    // P1d's TextWriter routine, given its two Context words: the formatting runtime's writer kind of its destination W, the call's Type
    // argument (LegacyRuntimeHelpers.WriterKind), and the dispatch word: null for a FixedBuffer or a HeapBuffer, whose reserve the runtime runs
    // itself (a body-less compiler function, so the call carries no target), else the function of W's reserve witness, the call's Body target,
    // whose signature is the runtime's dispatch call (Dispatches). Any other pairing is a broken contract: Binding admits no virtual reserve
    // witness (a virtual function's receiver is objref/Self or objuniq/Self, which Binding rejects or leaves Unsupported there). Null after a record.
    private FunctionAbi? Writer(IntrinsicRow row, BoundType? destination, int target, out ReadOnlySpan<Value> given)
    {
        given = default;
        var kind = destination is null ? -1 : LegacyRuntimeHelpers.WriterKind(destination);
        Value? dispatch = target switch
        {
            < 0 when kind is 0 or 1 => new Value('n', 0),
            >= 0 when kind == 2 && this.Dispatches(target) => new Value('e', target),
            _ => null,
        };
        if (dispatch is not { } entry)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The collector targets a user Writer's reserve witness (K 4.5 row 6).
            return null;
        }

        this.module.Request(LegacyRuntimeHelpers.FormattingRow(CompilerFunctionKind.TextWriter)!.Needs);
        (this.slotless[0], this.slotless[1]) = (new('\0', kind), entry);
        given = this.slotless.AsSpan(0, 2);
        return row.Routine;
    }

    // Whether a target is a body the runtime calls as a user Writer's reserve (__kimi_writer_reserve's `void (ptr ret, ptr destination, i64
    // minimum)`) through the adapter's dispatch word: a function without a virtual slot under that signature.
    private bool Dispatches(int target)
    {
        if (this.collector.Set.Items[target].Key.Kind != MonoKind.Body || this.BodyRun(target) is not { Slot: null, Signature: var signature })
        {
            return false;
        }

        return signature is { Result: "void", NoReturn: false, ResultSlot: true, Parameters: [{ Kind: AbiParameterKind.ResultSlot }, var self, var amount] } &&
            self.Type == "ptr" && amount.Type == "i64";
    }

    // `br i1 <condition>, label <then><id>, label <otherwise><id>`.
    private void Branch(Value condition, string then, string otherwise, int id)
    {
        var output = this.output;
        output.Write("  br i1 ");
        this.WriteValue(condition);
        output.Write(", label ");
        Name(output, then, id);
        output.Write(", label ");
        Name(output, otherwise, id);
        output.Write('\n');
    }

    // SPEC 22.9.2: a capacity Context's word, the length of the fixed array below the reference its parameter receives; null after one
    // InternalInvariant_Kd for another Type, a broken contract (Binding admits only uniq/[N of u8] there): Referent's for a Type without a
    // reference layer, else this one.
    private Value? Measure(BoundType? reference)
    {
        if (this.Referent(reference) is not { } referent)
        {
            return null; // Recorded.
        }

        if (referent is { Kind: BoundTypeKind.FixedArray, Length: >= 0 })
        {
            return new Value('\0', referent.Length);
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default); // A capacity measures a fixed array (Binding's).
        return null;
    }
}
