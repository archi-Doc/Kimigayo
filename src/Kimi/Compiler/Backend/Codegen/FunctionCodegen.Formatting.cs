// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Backend;

// The formatting rows of FunctionCodegen (R5 plan C9a, package C 3.2; SPEC 22.7, 22.9, 22.10.1, 22.10.5; DECISIONS R5-M30, R5-M128): a call of
// a Text, buffer, window or writer function or of writeLine(Utf8Slice), the Body-disposition kinds whose legacy row's writer is Formatting
// (LegacyRuntimeHelpers.Row), is the call of its row's routine in the formatting runtime (P1d's row ABI, one text, PD2) through the one call
// writer, at its direct call, its Item's Value call and its Item adapter's entry alike (SPEC 7.7.4), the module requesting the formatting
// runtime its row declares (ModuleCodegen.Request). Its Context words are facts the call's Types fix:
// - a capacity Context, which names a parameter by its logical index, is the length of the fixed array that parameter receives (SPEC
//   22.9.2's N): measured by the call writer from the operand's instance Type (Pass), or by an adapter's entry from its own input Type, its
//   target Function Type's (Forward) (Measure);
// - a kind Context, which names none, is the formatting runtime's kind of the call's witness target's Self (LegacyRuntimeHelpers.FormatKind),
//   the Type K6 resolved below the published Formats row's parameter (DECISIONS R5-M30), given here (Formatting's WitnessKind).
// A row whose call carries a witness target (WriterWrite, TextToString, TextTryFormat; K package 4.5 row 2) is decided by the target's kind:
// a Builtin item (BuiltinFormat on a built-in formattable Type) runs through the row's built-in routine, which is SPEC 22.10.1's frame around
// the runtime's built-in formatting of that kind, toString reserving its exact length once (SPEC 22.10.5); a Body item, a user or library
// formatter, needs C9b's wrapper, one located Unsupported_Kd at the requesting site; any other is a broken contract. The Builtin item stays
// admitted; its function is C9b's (FunctionCodegen.Shims). No row calls a witness here.
internal sealed partial class FunctionCodegen
{
    // A Formatting row's routine (above), its witness target's rule, its kind Context given from the target into `given`, and the formatting
    // runtime its row declares requested; null after a record at the source.
    private FunctionAbi? Formatting(LegacyRow row, int target, SourceInfo source, out ReadOnlySpan<Value> given)
    {
        given = default;
        var count = 0;
        foreach (var parameter in row.Routine!.Parameters)
        {
            if (parameter is { Kind: AbiParameterKind.Context, LogicalIndex: < 0 })
            {
                if (this.WitnessKind(target, source) is not { } kind)
                {
                    return null; // Recorded.
                }

                this.slotless[count++] = kind;
            }
        }

        if (count == 0 && target >= 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // Only a row with a kind Context runs a witness (K package 4.5 row 2).
            return null;
        }

        this.module.Request(row.Needs);
        given = this.slotless.AsSpan(0, count);
        return row.Routine;
    }

    // SPEC 22.10.1-22.10.2: a formatting row's witness kind, the kind Context of its witness target, the formatting runtime's number of its
    // Builtin item's Self (LegacyRuntimeHelpers.FormatKind); a Body item's is C9b's located Unsupported_Kd (a user or library formatter's
    // wrapper); no target, another item or a Self without a kind is a broken contract. Null after a record.
    private Value? WitnessKind(int target, in SourceInfo source)
    {
        var entry = target >= 0 ? this.collector.Set.Items[target] : default;
        if (target >= 0 && entry.Key.Kind == MonoKind.Body)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, source); // C9b writes a user formatter's wrapper.
            return null;
        }

        var builtin = target >= 0 && entry.Key is { Kind: MonoKind.Builtin, Symbol.CompilerFunction: CompilerFunctionKind.BuiltinFormat };
        var kind = builtin ? LegacyRuntimeHelpers.FormatKind(entry.RepresentativeType!) : -1;
        if (kind < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // The collector targets the call at its format witness (K6).
            return null;
        }

        return new Value('\0', kind);
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
