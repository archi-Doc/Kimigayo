// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Backend;

// The string comparisons of FunctionCodegen (R5 plan C8c; SPEC 13.4, 13.4.1, 14.8.1, 3.4.3, IMPL 21.1.3; DECISIONS R5-M118), the operator
// table's string column (FunctionCodegen.Arithmetic): a comparison whose operands' terminal is string, whatever form gave it (a direct
// operator, a literal Pattern's test, a tuple's string leaf; DECISIONS R5-M75). Each operand's bytes (a string constant's pooled text, else
// the record its value reaches through its ref and uniq layers), its row's routine on them (LegacyRuntimeHelpers' StringEqual for == and
// !=, StringCompare for the orderings: the one text, which the module holds once), the routine's i32 compared
// with 0 by the column's entry (SPEC 13.4.1), the bool widened into the Place. Nothing is checked, located or destroyed: a comparison
// acquires nothing and never fails. A string Place is never an operand (MIR inspects strings through shared borrows), a string has no
// other operator (SPEC 13.3.2), and no valid program reaches a pair layer in an operand (Binding rejects the built-in comparison through a
// pair target, UnprovenConstraint_Kd; MIR is built per body, so an abstract target is a requirement call): each is a broken contract.
// C8f's comparison shims read a string leaf's record through Record. Referent, the walk to an operand's terminal Place, serves every
// comparison operand (FunctionCodegen.Arithmetic's Compute; DECISIONS R5-M143).
internal sealed partial class FunctionCodegen
{
    // SPEC 3.4.3, 13.4: where an operand of safe value-reference layers denotes its terminal Place, given the operand's value: the referent of
    // its outer ref or uniq layer through the pointer each further one stores (the place writer's Deref rule), or the value itself without
    // layers (References 0); its Terminal. Null after a record: a pair layer, which no valid program leaves on an operand (above).
    private Value? Referent(in MirOperand operand, Value value, in SourceInfo source, out Terminal terminal)
    {
        terminal = Terminal.Of(this.InstanceType(operand));
        if (terminal.Layers != terminal.References)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source);
            return null;
        }

        for (var layer = 1; layer < terminal.References; layer++)
        {
            value = this.LoadAt(value, PointerLayout);
        }

        return value;
    }

    // A string comparison into its Place (above).
    private void CompareStrings(in MirStatement statement, in Row row, ReadOnlySpan<MirOperand> operands)
    {
        var entry = row.Entries[StringColumn];
        if (entry.Operator is null || row.Routine is null)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source); // Binding admits no other operator on strings (SPEC 13.3.2).
            return;
        }

        if (!this.Bytes(operands[0], statement.Source, out var left) || !this.Bytes(operands[1], statement.Source, out var right) ||
            this.LoweringOf(statement.Place) is not { } target)
        {
            return; // Recorded.
        }

        var order = this.CompareBytes(row.Routine, left, right);
        var result = new Value('t', this.next++);
        Span<char> text = stackalloc char[2 * SpellingLength];
        var test = new ScalarOperation(
            entry.Operator, "i32", Spell(order, text[..SpellingLength]), "0", Spell(result, text[SpellingLength..]), comparison: true);
        LlvmScalarText.WriteOperation(this.output, test);
        this.Store(statement, this.Widen(target, result));
    }

    // A string operand's bytes: a constant's (TextBytes), or the record at its terminal Place (Referent, the one walk every comparison
    // operand takes). False after a broken contract's record: the operand's (Operand), or a string Place or a pair layer, which no valid
    // program makes an operand.
    private bool Bytes(in MirOperand operand, in SourceInfo source, out StringBytes bytes)
    {
        bytes = default;
        if (this.TextOf(operand) is { } text)
        {
            bytes = this.TextBytes(text);
            return true;
        }

        if (!this.Operand(operand, out var value))
        {
            return false; // Recorded.
        }

        if (this.Referent(operand, value, source, out var terminal) is not { } address)
        {
            return false; // Recorded.
        }

        if (terminal.References == 0 || !ReferenceEquals(terminal.Type, BoundType.String))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source);
            return false;
        }

        bytes = this.Record(address);
        return true;
    }

    // The bytes of the string record at an address (IMPL 21.1.3, RuntimeAbi.String): its Field 0, their address, and its Field 1, their count.
    private StringBytes Record(Value address)
    {
        var offsets = RuntimeAbi.String.Layout.FieldOffsets.Span;
        var data = this.LoadAt(this.Offset(address, offsets[0]), PointerLayout);
        return new(data, this.LoadAt(this.Offset(address, offsets[1]), Layout.Row(BoundType.ISize).Layout));
    }

    // A string routine's call on two strings' bytes through the one call writer, its text requested for the module: its i32.
    private Value CompareBytes(FunctionAbi routine, StringBytes left, StringBytes right)
    {
        this.module.Request(ModuleCodegen.Needs.Strings);
        return this.WriteCall(routine, [left.Data, left.Length, right.Data, right.Length])!.Value;
    }

    // SPEC 3.4.3, 13.4: the terminal of a comparison operand's Type below its safe value-reference layers (ref, uniq and pair layers; an
    // object handle or borrow is not followed), the layers followed and, of those, the ref and uniq ones (References).
    private readonly record struct Terminal(BoundType? Type, int Layers, int References)
    {
        // The one walk of those layers.
        internal static Terminal Of(BoundType? type)
        {
            var layers = 0;
            var references = 0;
            while (TyCtxt.Layer(type, out var below, out _) is var layer && (layer == HirPlaceKind.PairLayer ||
                (layer == HirPlaceKind.Deref && type!.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)))
            {
                layers++;
                references += layer == HirPlaceKind.Deref ? 1 : 0;
                type = below;
            }

            return new(type, layers, references);
        }
    }
}
