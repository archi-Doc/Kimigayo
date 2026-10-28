// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The operator rows (R5 plan C1a, C1b, C8a, C8c, package C 3.2 I5 and I9; DECISIONS R5-M76, R5-M80, R5-M92, R5-M118): a BinaryOp or UnaryOp
// into its Place through LlvmScalarText (P4a), by one table keyed by the operator and its left operand's terminal Type class, each operand
// read at its terminal Place through its safe value-reference layers (SPEC 13.4); a string comparison through its row's routine
// (FunctionCodegen.Strings). A Proven operation (MirRvalue.Proven, the builder's IMPL 21.3.3 fact) writes no check; any other check Aborts
// at the statement's location text (SPEC 22.5.1-22.5.2). A checked 128-bit operation requests its overflow intrinsics' declarations, which
// the frame's fixed set omits (ModuleCodegen.Needs.WideOverflow); an operator with no entry is a broken contract (DECISIONS R5-M143).
internal sealed partial class FunctionCodegen
{
    // The string column of the table (Column).
    private const byte StringColumn = 6;

    // The first UnaryOp's row.
    private static readonly int UnaryRows = Enum.GetValues<MirBinaryOp>().Length;

    // SPEC 13.2-13.4, 3.1.1, 5.1, 5.3: a row per MIR operator, MirBinaryOp's members then MirUnaryOp's, with its entry for a signed
    // integer, an unsigned integer, a signed wrapping, an unsigned wrapping integer, a floating-point, a raw pointer and a string Type: the LLVM
    // operator (an instruction, an icmp or fcmp predicate or a checked operation's overflow intrinsic stem), the check SPEC 13.3.1 makes
    // and a unary operator's fixed operand, the other side's spelling (0 - x; x xor -1, logical on bool and bitwise on an integer; fneg has
    // none, so -0.0 keeps its sign). A wrapping entry is the operation without its check, which a Proven one writes; bool and char take the
    // unsigned integer entries, where their operators (equality, char's order, not) check nothing. Floating point follows IEEE 754 and
    // checks nothing (SPEC 13.3.1); its comparisons are ordered but for !=, so a NaN operand makes != true and the others false, and +0.0
    // == -0.0 (SPEC 13.4); it has no remainder, bitwise or shift operator. A raw pointer has == and != on its address and p + n and p - n,
    // a displacement by n * stride(T) (Displacement, Displace), unchecked; Binding admits no other pointer operator (DECISIONS R5-M92). A
    // string has the comparisons only (SPEC 13.3.2, 13.4): the predicate on its row's Routine's i32 against 0 (SPEC 13.4.1), the equality
    // routine for == and != and the ordering routine for the others, which compare UTF-8 bytes lexicographically (DECISIONS R5-M118).
    // Reflexive: the result on equal operands, a comparison of Unit's one value.
    private static readonly Row[] Rows = Complete(
    [
        new(
            [new("sadd", Check.Overflow, 0), new("uadd", Check.Overflow, 0), new("add", Check.None, 0), new("add", Check.None, 0), new("fadd", Check.None, 0),
                new("gep", Check.None, 1), default]), // Add
        new(
            [new("ssub", Check.Overflow, 0), new("usub", Check.Overflow, 0), new("sub", Check.None, 0), new("sub", Check.None, 0), new("fsub", Check.None, 0),
                new("gep", Check.None, -1), default]), // Sub
        new(
            [new("smul", Check.Overflow, 0), new("umul", Check.Overflow, 0), new("mul", Check.None, 0), new("mul", Check.None, 0), new("fmul", Check.None, 0),
                default, default]), // Mul
        new(
            [new("sdiv", Check.SignedQuotient, 0), new("udiv", Check.Zero, 0), new("sdiv", Check.WrappingQuotient, 0), new("udiv", Check.Zero, 0),
                new("fdiv", Check.None, 0), default, default]), // Div
        new([new("srem", Check.Zero, 0), new("urem", Check.Zero, 0), new("srem", Check.Zero, 0), new("urem", Check.Zero, 0), default, default, default]), // Rem
        new([new("shl", Check.Shift, 0), new("shl", Check.Shift, 0), new("shl", Check.Shift, 0), new("shl", Check.Shift, 0), default, default, default]), // Shl
        new(
            [new("ashr", Check.Shift, 0), new("lshr", Check.Shift, 0), new("ashr", Check.Shift, 0), new("lshr", Check.Shift, 0), default, default,
                default]), // Shr
        new([new("and", Check.None, 0), new("and", Check.None, 0), new("and", Check.None, 0), new("and", Check.None, 0), default, default, default]), // BitAnd
        new([new("or", Check.None, 0), new("or", Check.None, 0), new("or", Check.None, 0), new("or", Check.None, 0), default, default, default]), // BitOr
        new([new("xor", Check.None, 0), new("xor", Check.None, 0), new("xor", Check.None, 0), new("xor", Check.None, 0), default, default, default]), // BitXor
        new(
            [new("eq", Check.None, 0), new("eq", Check.None, 0), new("eq", Check.None, 0), new("eq", Check.None, 0), new("oeq", Check.None, 0),
                new("eq", Check.None, 0), new("eq", Check.None, 0)],
            Reflexive: true,
            Routine: LegacyRuntimeHelpers.StringEqual), // Eq
        new(
            [new("ne", Check.None, 0), new("ne", Check.None, 0), new("ne", Check.None, 0), new("ne", Check.None, 0), new("une", Check.None, 0),
                new("ne", Check.None, 0), new("ne", Check.None, 0)],
            Routine: LegacyRuntimeHelpers.StringEqual), // Ne
        new(
            [new("slt", Check.None, 0), new("ult", Check.None, 0), new("slt", Check.None, 0), new("ult", Check.None, 0), new("olt", Check.None, 0),
                default, new("slt", Check.None, 0)],
            Routine: LegacyRuntimeHelpers.StringCompare), // Lt
        new(
            [new("sle", Check.None, 0), new("ule", Check.None, 0), new("sle", Check.None, 0), new("ule", Check.None, 0), new("ole", Check.None, 0),
                default, new("sle", Check.None, 0)],
            Reflexive: true,
            Routine: LegacyRuntimeHelpers.StringCompare), // Le
        new(
            [new("sgt", Check.None, 0), new("ugt", Check.None, 0), new("sgt", Check.None, 0), new("ugt", Check.None, 0), new("ogt", Check.None, 0),
                default, new("sgt", Check.None, 0)],
            Routine: LegacyRuntimeHelpers.StringCompare), // Gt
        new(
            [new("sge", Check.None, 0), new("uge", Check.None, 0), new("sge", Check.None, 0), new("uge", Check.None, 0), new("oge", Check.None, 0),
                default, new("sge", Check.None, 0)],
            Reflexive: true,
            Routine: LegacyRuntimeHelpers.StringCompare), // Ge
        new(
            [new("ssub", Check.Overflow, 0, Left: "0"), new("usub", Check.Overflow, 0, Left: "0"), new("sub", Check.None, 0, Left: "0"),
                new("sub", Check.None, 0, Left: "0"), new("fneg", Check.None, 0), default, default]), // Neg
        new(
            [new("xor", Check.None, 0, Right: "-1"), new("xor", Check.None, 0, Right: "-1"), new("xor", Check.None, 0, Right: "-1"),
                new("xor", Check.None, 0, Right: "-1"), default, default, default]), // Not
    ]);

    // SPEC 13.3.1's checks, each one LlvmScalarText writer.
    private enum Check : byte
    {
        None,
        Overflow, // Through the overflow intrinsic its operator names.
        SignedQuotient, // A zero divisor and the minimum / -1 pair.
        Zero, // A zero divisor.
        WrappingQuotient, // A zero divisor; the minimum / -1 is the minimum.
        Shift, // A count outside 0 <= count < N.
    }

    // The column whose entry is a column's operation without its check, which a Proven operation writes: an integer's wrapping column, a
    // wrapping integer's and a floating-point Type's own.
    private static ReadOnlySpan<byte> Unchecked => [2, 3, 2, 3, 4, 5, StringColumn];

    // The entry an operand Type takes, by its terminal below its safe value-reference layers (Terminal; SPEC 13.4: a comparison operand
    // denotes its terminal Place, and MIR leaves the layers on an operand whose terminal it inspects in place, a string's or a raw pointer's;
    // DECISIONS R5-M75, R5-M143) (SPEC 3.1, 3.1.1, 13.4, 5.1): an integer's or a wrapping integer's by its signedness, a floating-point
    // Type's, bool's and char's the unsigned integer's, a raw pointer's, a string's; -1 for a Type with no operator.
    private static int Column(BoundType type)
    {
        type = Terminal(type, out _, out _)!;
        if (type.Semantics == SemanticsKind.Raw)
        {
            return 5;
        }

        if (ReferenceEquals(type, BoundType.String))
        {
            return StringColumn;
        }

        if (type.HasIntegerArithmetic)
        {
            return (type.IsWrappingInteger ? 2 : 0) + (type.Underlying.IsUnsignedInteger ? 1 : 0);
        }

        if (type.IsFloatingPoint)
        {
            return 4;
        }

        return ReferenceEquals(type, BoundType.Boolean) || ReferenceEquals(type, BoundType.Char) ? 1 : -1;
    }

    // The row of a MIR operator, a statement's or an arithmetic shim's (BuiltinArithmetic): MirBinaryOp's members, then MirUnaryOp's.
    private static int RowOf(MirRvalueKind kind, byte op) => (kind == MirRvalueKind.UnaryOp ? UnaryRows : 0) + op;

    // The table, whose rows follow the operator enums' order with an entry per column; a row or an entry missing is a broken contract.
    private static Row[] Complete(Row[] rows)
    {
        var complete = rows.Length == UnaryRows + Enum.GetValues<MirUnaryOp>().Length;
        foreach (var row in rows)
        {
            complete &= row.Entries.Length == Unchecked.Length;
        }

        return complete ? rows : throw new InvalidOperationException("The operator table needs a row per MIR operator and an entry per column.");
    }

    // An operator into its Place: its row and the entry for the left operand's terminal Type (its column, decided before any operand is
    // read), its operands each read through its layers in their computation representation (Compute), a raw pointer's displacement
    // (Displace, which stores it) or the operation (Apply), and the result stored in the Place's storage representation (bool widened).
    private void Operator(in MirStatement statement)
    {
        var rvalue = statement.Rvalue;
        var row = RowOf(rvalue.Kind, rvalue.Op);
        var operands = this.body.Operands.Slice(rvalue.Start, rvalue.Count);
        var column = this.InstanceType(operands[0]) is { } type ? Column(type) : -1; // None: a broken contract, recorded below.
        if (column == StringColumn)
        {
            this.CompareStrings(statement, Rows[row], operands); // Its operands are inspected, not computed (FunctionCodegen.Strings).
            return;
        }

        var right = default(Computed);
        if (!this.Compute(operands[0], out var left) || (operands.Length == 2 && !this.Compute(operands[1], out right)))
        {
            return; // A broken contract, recorded (Compute).
        }

        right = operands.Length == 2 ? right : left; // A unary operator's operand is on both sides; its row fixes the other.
        if (left.Lowering is null || right.Lowering is null || this.LoweringOf(statement.Place) is not { } target)
        {
            return; // A broken contract, recorded.
        }

        if (column >= 0 && Rows[row].Entries[column].Displacement is not 0 and var sign)
        {
            this.Displace(statement, left, right, sign); // A raw pointer, never zero-sized; its entry is its own unchecked one.
            return;
        }

        if (this.Apply(row, column, left, right, rvalue.Proven, statement.Source) is { } result)
        {
            this.Store(statement, this.Widen(target, result));
        }
    }

    // A row's operation on two operands in their computation representation (a unary operator's on both sides) at an entry's column: the
    // row's Reflexive result for zero-sized operands (SPEC 13.4: Unit has one value), else the entry's operation with its check unless
    // Proven (the column's unchecked entry), which Aborts at the source's location; its value, or null after a record.
    private Value? Apply(int row, int column, in Computed left, in Computed right, bool proven, in SourceInfo source)
    {
        if (left.Lowering!.Layout.Size == 0)
        {
            return new Value('\0', Rows[row].Reflexive ? 1 : 0);
        }

        column = column >= 0 && proven ? Unchecked[column] : column;
        var entry = column < 0 ? default : Rows[row].Entries[column];
        var check = proven ? Check.None : entry.Check;
        if (entry.Operator is null)
        {
            // No entry: an operator Binding admits on no operand of this terminal Type (a raw pointer's ordering, product, negation or
            // bitwise operator, a floating-point remainder, bitwise or shift operator, a Type without a column), so no accepted program
            // reaches it: a broken contract (DECISIONS R5-M143).
            this.Report(DiagnosticCode.InternalInvariant_Kd, source);
            return null;
        }

        if (check == Check.Overflow && left.Lowering.Layout.Size > 8)
        {
            this.module.Request(ModuleCodegen.Needs.WideOverflow); // A 128-bit overflow intrinsic, which the fixed declarations omit.
        }

        Site site = default;
        if (check != Check.None && !this.Location(source, out site))
        {
            return null; // Recorded.
        }

        var result = new Value('t', this.next++);
        Span<char> text = stackalloc char[3 * SpellingLength];
        var operation = new ScalarOperation(
            entry.Operator,
            left.Lowering.ComputationType,
            entry.Left ?? Spell(left.Value, text[..SpellingLength]),
            entry.Right ?? Spell(right.Value, text.Slice(SpellingLength, SpellingLength)),
            Spell(result, text[(2 * SpellingLength)..]),
            comparison: row >= (int)MirBinaryOp.Eq && row < UnaryRows);
        this.Write(check, operation, (int)result.Number, site, right.Lowering!.ComputationType);
        return result;
    }

    // An operation with its check and failure block, named by its result's number id (%checked<id>, abort<id>, the success label
    // c<id>), which Aborts at its site's location; countType is a shift count's Type.
    private void Write(Check check, in ScalarOperation operation, int id, Site site, string countType)
    {
        if (check == Check.None)
        {
            LlvmScalarText.WriteOperation(this.output, operation);
            return;
        }

        var failure = this.Failure(id, site, stackalloc char[3 * SpellingLength]);
        switch (check)
        {
            case Check.Overflow:
                LlvmScalarText.WriteOverflowChecked(this.output, operation, failure);
                return;
            case Check.SignedQuotient:
                LlvmScalarText.WriteSignedQuotient(this.output, operation, failure, Spell(new('t', this.next++), stackalloc char[SpellingLength]));
                return;
            case Check.Zero:
                LlvmScalarText.WriteZeroChecked(this.output, operation, failure);
                return;
            case Check.WrappingQuotient:
                LlvmScalarText.WriteWrappingQuotient(this.output, operation, failure);
                return;
            case Check.Shift:
                LlvmScalarText.WriteShift(this.output, operation, failure, countType);
                return;
            default:
                throw new InvalidOperationException($"The check {check} has no writer.");
        }
    }

    // SPEC 5.3: p + n and p - n displace a raw pointer by n * stride(T) bytes, T's stride in the item's instance, unchecked (overflow is
    // undefined behavior); a zero stride keeps the pointer, null included. The bytes go through the place writer's Offset.
    private void Displace(in MirStatement statement, in Computed pointer, in Computed count, int sign)
    {
        if (pointer.Type.Components is not [var pointee] || this.Laid(pointee) is not { } element)
        {
            return; // A broken contract, recorded.
        }

        var result = pointer.Value;
        if (element.Layout.Stride != 0)
        {
            var bytes = new Value('t', this.next++);
            var output = this.output;
            output.Write("  ");
            this.WriteValue(bytes);
            output.Write(" = mul ");
            output.Write(count.Lowering!.ComputationType);
            output.Write(' ');
            this.WriteValue(count.Value);
            output.Write(", ");
            WriteNumber(output, sign * element.Layout.Stride);
            output.Write('\n');
            result = this.Offset(pointer.Value, bytes);
        }

        this.Store(statement, result);
    }

    // A covered operand's value in its computation representation (I4: a local loaded, bool narrowed; a constant its literal), read at its
    // terminal Place through its safe value-reference layers (Referent; SPEC 13.4: only a comparison's operand has any, MIR's contract), its
    // terminal Type and that Type's representation in the item's instance; false after a broken contract's record (Operand's), true without
    // a representation after another's.
    private bool Compute(in MirOperand operand, out Computed computed)
    {
        computed = default;
        if (!this.Operand(operand, out var value))
        {
            return false;
        }

        if (this.Referent(operand, value, default, out var terminal, out var references) is not { } place)
        {
            return true; // A broken contract, recorded.
        }

        if ((references == 0 ? this.LoweringOf(operand) : this.Laid(terminal)) is { } lowering)
        {
            var read = references == 0 || lowering.Layout.Size == 0 ? place : this.LoadAt(place, lowering.Layout);
            var constant = operand.Kind == MirOperandKind.Const;
            computed = new(constant || lowering.Layout.Size == 0 ? read : this.Narrow(lowering, read), terminal!, lowering);
        }

        return true;
    }

    // An operand of an operator: its value, closed terminal Type and that Type's representation.
    private readonly record struct Computed(Value Value, BoundType Type, ValueLowering? Lowering);

    // An entry of the table: the LLVM operator (null: none, an operator Binding admits on no operand of the column's Types), the check, a
    // unary operator's fixed operand and a raw pointer displacement's sign.
    private readonly record struct Entry(string? Operator, Check Check, sbyte Displacement, string? Left = null, string? Right = null);

    // A row of the table: the entries by column, the result on equal operands and a comparison's string routine (the string column's).
    private readonly record struct Row(Entry[] Entries, bool Reflexive = false, FunctionAbi? Routine = null);
}
