// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The operator rows (R5 plan C1a, package C 3.2 I5 and I9; DECISIONS R5-M76): a BinaryOp or UnaryOp into an unprojected local through
// LlvmScalarText (P4a), by one table keyed by the operator and its left operand's Type class. A Proven operation (MirRvalue.Proven, the
// builder's IMPL 21.3.3 fact) writes no check; any other check Aborts at the statement's location text (SPEC 22.5.1-22.5.2).
internal sealed partial class FunctionCodegen
{
    // The longest spelling of an operand, a label or a location operand: a two-character prefix and an Int128, or @ and a constant's name.
    private const int SpellingLength = 48;

    // The first UnaryOp's row.
    private const int UnaryRows = (int)MirBinaryOp.Ge + 1;

    // SPEC 13.2-13.4, 3.1.1: a row per MIR operator, MirBinaryOp's members then MirUnaryOp's, with its entry for a signed integer, an
    // unsigned integer, a signed wrapping and an unsigned wrapping integer Type: the LLVM operator (an instruction, an icmp predicate or a
    // checked operation's overflow intrinsic stem) and the check SPEC 13.3.1 makes. A wrapping entry is the operation without its check,
    // which a Proven one writes; bool and char take the unsigned integer entries, where their operators (equality, char's order, not)
    // check nothing. Reflexive: the result on equal operands, a comparison of Unit's one value; a unary operator's fixed operand is the
    // other side's spelling (0 - x; x xor -1, logical on bool and bitwise on an integer).
    private static readonly Row[] Rows =
    [
        new([new("sadd", Check.Overflow), new("uadd", Check.Overflow), new("add"), new("add")]), // Add
        new([new("ssub", Check.Overflow), new("usub", Check.Overflow), new("sub"), new("sub")]), // Sub
        new([new("smul", Check.Overflow), new("umul", Check.Overflow), new("mul"), new("mul")]), // Mul
        new([new("sdiv", Check.SignedQuotient), new("udiv", Check.Zero), new("sdiv", Check.WrappingQuotient), new("udiv", Check.Zero)]), // Div
        new([new("srem", Check.Zero), new("urem", Check.Zero), new("srem", Check.Zero), new("urem", Check.Zero)]), // Rem
        new([new("shl", Check.Shift), new("shl", Check.Shift), new("shl", Check.Shift), new("shl", Check.Shift)]), // Shl
        new([new("ashr", Check.Shift), new("lshr", Check.Shift), new("ashr", Check.Shift), new("lshr", Check.Shift)]), // Shr
        new([new("and"), new("and"), new("and"), new("and")]), // BitAnd
        new([new("or"), new("or"), new("or"), new("or")]), // BitOr
        new([new("xor"), new("xor"), new("xor"), new("xor")]), // BitXor
        new([new("eq"), new("eq"), new("eq"), new("eq")], Reflexive: true), // Eq
        new([new("ne"), new("ne"), new("ne"), new("ne")]), // Ne
        new([new("slt"), new("ult"), new("slt"), new("ult")]), // Lt
        new([new("sle"), new("ule"), new("sle"), new("ule")], Reflexive: true), // Le
        new([new("sgt"), new("ugt"), new("sgt"), new("ugt")]), // Gt
        new([new("sge"), new("uge"), new("sge"), new("uge")], Reflexive: true), // Ge
        new([new("ssub", Check.Overflow), new("usub", Check.Overflow), new("sub"), new("sub")], Left: "0"), // Neg
        new([new("xor"), new("xor"), new("xor"), new("xor")], Right: "-1"), // Not
    ];

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

    // The entry an operand Type takes (SPEC 3.1, 3.1.1, 13.4): an integer's or a wrapping integer's by its signedness, bool's and char's
    // the unsigned integer's; -1 for a Type whose operators have no row yet: floating point (C1b), raw pointers (C8a), string borrows (C8c).
    private static int Column(BoundType type) => type.HasIntegerArithmetic ? (type.IsWrappingInteger ? 2 : 0) + (type.Underlying.IsUnsignedInteger ? 1 : 0)
        : ReferenceEquals(type, BoundType.Boolean) || ReferenceEquals(type, BoundType.Char) ? 1 : -1;

    // A value's spelling (LlvmScalarText's operand precondition): a prefixed name such as %t<n>, or an integer's signed decimal.
    private static ReadOnlySpan<char> Spell(Value value, Span<char> text)
    {
        var start = 0;
        if (value.Prefix != '\0')
        {
            text[0] = '%';
            text[1] = value.Prefix;
            start = 2;
        }

        value.Number.TryFormat(text[start..], out var digits, default, CultureInfo.InvariantCulture);
        return text[..(start + digits)];
    }

    // An operator into an unprojected local: its operands in their computation representation, its row's entry for the left operand's
    // Type, the operation with its check unless Proven, and the result stored in the local's storage representation (bool widened).
    private void Operator(in MirStatement statement, FunctionAbi abi)
    {
        var rvalue = statement.Rvalue;
        var row = Rows[(rvalue.Kind == MirRvalueKind.UnaryOp ? UnaryRows : 0) + rvalue.Op];
        var operands = this.body.Operands.Slice(rvalue.Start, rvalue.Count);
        var right = default(Computed);
        if (!this.Compute(operands[0], out var left) || (operands.Length == 2 && !this.Compute(operands[1], out right)))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // A projected Place (C2a), a static (C7b) or a constant of a later row.
            return;
        }

        right = operands.Length == 2 ? right : left; // A unary operator's operand is on both sides; its row fixes the other.
        if (left.Lowering is null || right.Lowering is null)
        {
            return; // A broken contract, recorded.
        }

        var local = statement.Place.Root;
        if (left.Lowering.Layout.Size == 0)
        {
            this.Store(local, this.Widen(this.slots[local].Value, new('\0', row.Reflexive ? 1 : 0))); // SPEC 13.4: Unit has one value.
            return;
        }

        var column = Column(left.Type);
        var entry = column < 0 ? default : row.Entries[rvalue.Proven ? 2 | (column & 1) : column];
        var check = rvalue.Proven ? Check.None : entry.Check;
        if (entry.Operator is null || (check == Check.Overflow && left.Lowering.Layout.Size > 8))
        {
            // A Type of a later row, or a checked 128-bit operation, whose intrinsics' declarations are ModuleCodegen's request (open issue).
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        var location = check == Check.None ? -1 : this.Location(statement.Source, abi);
        if (check != Check.None && location < 0)
        {
            return; // Recorded.
        }

        var result = new Value('t', this.next++);
        Span<char> text = stackalloc char[3 * SpellingLength];
        var operation = new ScalarOperation(
            entry.Operator,
            left.Lowering.ComputationType,
            row.Left ?? Spell(left.Value, text[..SpellingLength]),
            row.Right ?? Spell(right.Value, text.Slice(SpellingLength, SpellingLength)),
            Spell(result, text[(2 * SpellingLength)..]),
            comparison: rvalue.Kind == MirRvalueKind.BinaryOp && rvalue.Op >= (byte)MirBinaryOp.Eq);
        this.Write(check, operation, (int)result.Number, location, right.Lowering.ComputationType);
        this.Store(local, this.Widen(this.slots[local].Value, result));
    }

    // An operation with its check and failure block, named by its result's number id (%checked<id>, abort<id>, the success label
    // c<id>), which Aborts at the pooled location constant; countType is a shift count's Type.
    private void Write(Check check, in ScalarOperation operation, int id, int location, string countType)
    {
        if (check == Check.None)
        {
            LlvmScalarText.WriteOperation(this.output, operation);
            return;
        }

        Span<char> text = stackalloc char[3 * SpellingLength];
        var success = Spell(new('c', id), text[..SpellingLength]);
        var constant = this.module.Constants[location];
        var address = text.Slice(SpellingLength, SpellingLength);
        address[0] = '@';
        constant.Name.CopyTo(address[1..]);
        var length = text[(2 * SpellingLength)..];
        constant.ByteLength.TryFormat(length, out var digits, default, CultureInfo.InvariantCulture);
        var failure = new ScalarCheck(id, success, address[..(constant.Name.Length + 1)], length[..digits]);
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

    // A covered operand's value in its computation representation (I4: a local loaded, bool narrowed; a constant its literal), its Type
    // and that Type's representation in the item's instance; false for a form not covered yet, true without a representation after a
    // broken contract's record.
    private bool Compute(in MirOperand operand, out Computed computed)
    {
        computed = default;
        if (!this.Operand(operand, out var value))
        {
            return false;
        }

        if (this.LoweringOf(operand) is { } lowering)
        {
            var constant = operand.Kind == MirOperandKind.Const;
            var type = constant ? this.collector.TypeOf(this.item, this.body.Consts[operand.Arg].Type)! : this.types[operand.Place.Root];
            computed = new(constant || lowering.Layout.Size == 0 ? value : this.Narrow(lowering, value), type, lowering);
        }

        return true;
    }

    // An operand of an operator: its value, closed Type and representation.
    private readonly record struct Computed(Value Value, BoundType Type, ValueLowering? Lowering);

    // An entry of the table: the LLVM operator and the check (null: no row yet).
    private readonly record struct Entry(string? Operator, Check Check = Check.None);

    // A row of the table: the entries by column, the result on equal operands and a unary operator's fixed operand.
    private readonly record struct Row(Entry[] Entries, bool Reflexive = false, string? Left = null, string? Right = null);
}
