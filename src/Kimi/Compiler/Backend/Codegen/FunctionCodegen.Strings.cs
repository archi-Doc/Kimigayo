// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Backend;

// The string comparisons of FunctionCodegen (R5 plan C8c; SPEC 13.4, 13.4.1, 14.8.1, 3.4.3, IMPL 21.1.3; DECISIONS R5-M118), the operator
// table's string column (FunctionCodegen.Arithmetic): a comparison whose operands' terminal is string, whatever form gave it (a direct
// operator, a literal Pattern's test, a tuple's string leaf; DECISIONS R5-M75). Each operand's bytes (a string constant's pooled text, else
// the record its value reaches through its ref and uniq layers), its row's routine on them (LegacyRuntimeHelpers' StringEqual for == and
// !=, StringCompare for the orderings: the one text both generators write, which the module then holds once), the routine's i32 compared
// with 0 by the column's entry (SPEC 13.4.1), the bool widened into the Place. Nothing is checked, located or destroyed: a comparison
// acquires nothing and never fails. A string Place is never an operand (MIR inspects strings through shared borrows), a string has no
// other operator (SPEC 13.3.2), and no valid program reaches a pair layer in an operand (Binding rejects the built-in comparison through a
// pair target, UnprovenConstraint_Kd; MIR is built per body, so an abstract target is a requirement call): each is a broken contract.
// C8f's comparison shims read a string leaf's record through Record.
internal sealed partial class FunctionCodegen
{
    // SPEC 3.4.3, 13.4: the terminal of a comparison operand's Type below its safe value-reference layers (ref, uniq and pair layers; an
    // object handle or borrow is not followed): the one walk of those layers, which also counts the layers followed and, of those, the
    // ref and uniq ones.
    private static BoundType? Terminal(BoundType? type, out int layers, out int references)
    {
        (layers, references) = (0, 0);
        while (TyCtxt.Layer(type, out var below, out _) is var layer && (layer == HirPlaceKind.PairLayer ||
            (layer == HirPlaceKind.Deref && type!.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)))
        {
            layers++;
            references += layer == HirPlaceKind.Deref ? 1 : 0;
            type = below;
        }

        return type;
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

    // A string operand's bytes: a constant's (TextBytes), or the record its value reaches: the referent of its outer ref or uniq layer, through
    // the pointer each further one stores (the place writer's Deref rule). False after a record: a form not covered yet (Unsupported_Kd), or
    // a string Place or a pair layer, which no valid program makes an operand (InternalInvariant_Kd).
    private bool Bytes(in MirOperand operand, in SourceInfo source, out StringBytes bytes)
    {
        bytes = default;
        if (this.TextOf(operand) is { } text)
        {
            bytes = this.TextBytes(text);
            return true;
        }

        if (!this.Operand(operand, out var address))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, source);
            return false;
        }

        var terminal = Terminal(this.InstanceType(operand), out var layers, out var references);
        if (references == 0 || references != layers || !ReferenceEquals(terminal, BoundType.String))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source);
            return false;
        }

        for (var layer = 1; layer < references; layer++)
        {
            address = this.LoadAt(address, PointerLayout);
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
}
