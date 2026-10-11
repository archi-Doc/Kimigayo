// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Operators (SPEC 13.2-13.5): the built-in operators, a sequence's metadata, `and`/`or` and the conversions, each a MIR rvalue of its operands
// with its check Proven where IMPL 21.5.3 omits it.
internal sealed partial class MirBuilder
{
    private static MirBinaryOp? BinaryOp(KotoKind kind) => kind switch
    {
        KotoKind.Plus => MirBinaryOp.Add,
        KotoKind.Minus => MirBinaryOp.Sub,
        KotoKind.Asterisk => MirBinaryOp.Mul,
        KotoKind.Slash => MirBinaryOp.Div,
        KotoKind.Percent => MirBinaryOp.Rem,
        KotoKind.LessThanLessThan => MirBinaryOp.Shl,
        KotoKind.GreaterThanGreaterThan => MirBinaryOp.Shr,
        KotoKind.Ampersand => MirBinaryOp.BitAnd,
        KotoKind.Bar => MirBinaryOp.BitOr,
        KotoKind.Caret => MirBinaryOp.BitXor,
        KotoKind.EqualsEquals => MirBinaryOp.Eq,
        KotoKind.ExclamationEquals => MirBinaryOp.Ne,
        KotoKind.LessThan => MirBinaryOp.Lt,
        KotoKind.LessThanEquals => MirBinaryOp.Le,
        KotoKind.GreaterThan => MirBinaryOp.Gt,
        KotoKind.GreaterThanEquals => MirBinaryOp.Ge,
        _ => null,
    };

    // SPEC 13.2, 13.3: a built-in operator on its operands, its check Proven where IMPL 21.5.3 omits it; or a sequence's metadata. The left
    // operand is pending while the right one is evaluated, so a transfer there ends a comparison's inspection Loan (SPEC 13.4, 14.10.3).
    private void Primitive(MirPlace destination, Koto node, BoundType type)
    {
        switch (node)
        {
            case MemberAccessKoto member:
                this.Metadata(destination, member);
                break;
            case BinaryKoto binary when BinaryOp(binary.Akind) is { } op:
                var start = this.arguments.Count;
                this.arguments.Add(this.Operand(binary.Left));
                this.arguments.Add(this.Operand(binary.Right));
                var proven = this.Proven(op, type, this.arguments[^1]);
                this.Set(destination, MirRvalue.Binary(op, this.Flush(start), proven), node);
                break;
            case UnaryKoto { Akind: KotoKind.PrefixPlus } plus:
                this.Assign(destination, plus.Operand);
                break;
            case UnaryKoto { Akind: KotoKind.PrefixMinus or KotoKind.Not } unary:
                var negate = unary.Akind == KotoKind.PrefixMinus;
                this.Set(
                    destination,
                    MirRvalue.Unary(
                    negate ? MirUnaryOp.Neg : MirUnaryOp.Not,
                    this.body.AddOperand(this.Operand(unary.Operand)),
                    !negate || type.IsFloatingPoint || type.IsWrappingInteger),
                    node);
                break;
            default:
                this.Unsupported(node, destination);
                break;
        }
    }

    // SPEC 4.6.1, 4.6.3: a sequence's length, whether it is empty (its length equals 0) and its indices (the ResolvedRange of start 0 and
    // end its length), from Len of the receiver; of a Utf8Slice, the one struct Binding serves as a sequence (SequenceMember), Len of its
    // byte Slice. SPEC 4.7.4: an Array's or a Dictionary's capacity is the temporary intrinsic Capacity of a shared view of the handle
    // (owner decision 2026-10-11) until R8's representation structs make it a Field read.
    private void Metadata(MirPlace destination, MemberAccessKoto member)
    {
        var form = IntrinsicDisposition.FormOf(member);
        if (form == BuiltinForm.None)
        {
            this.Unsupported(member, destination);
            return;
        }

        var sequence = this.Place(member.Left);
        if (form == BuiltinForm.Capacity)
        {
            var start = this.arguments.Count;
            this.arguments.Add(this.Borrow(SemanticsKind.Ref, sequence, member));
            this.Invoke(destination, MirCallee.Intrinsic(IntrinsicId.Capacity), start, member);
            return;
        }

        if (AdtDef.IsStruct(this.body.TypeOf(sequence)))
        {
            sequence = this.body.Project(sequence, MirProjectionKind.Field, AdtDef.IndexOf(this.body.TypeOf(sequence)!, "value"));
        }

        var length = form == BuiltinForm.Length ? destination : this.Temp(BoundType.ISize, member);
        this.Set(length, MirRvalue.Len(sequence), member);
        if (form != BuiltinForm.Length)
        {
            var start = this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize))));
            this.body.AddOperand(MirOperand.Move(length));
            var value = form == BuiltinForm.IsEmpty ? MirRvalue.Binary(MirBinaryOp.Eq, start, true)
                : MirRvalue.Aggregate(MirAggregateKind.Adt, start, 2, TypeOf(member));
            this.Set(destination, value, member);
        }
    }

    // SPEC 13.3: `and` and `or` evaluate the right operand only where the left does not decide the result: the left into the destination,
    // then the branch on it to the right operand or past it.
    private void Logical(MirPlace destination, BinaryKoto logical)
    {
        this.Assign(destination, logical.Left);
        var right = this.body.AddBlock();
        var join = this.body.AddBlock();
        var and = logical.Akind == KotoKind.And;
        this.Branch(destination, logical.Left, and ? join : right, and ? right : join);
        this.current = right;
        this.Assign(destination, logical.Right);
        this.Join(join, logical.Right);
        this.current = join;
    }

    // SPEC 13.5.4, 13.5.7, 5.4: a numeric, wrapping, bit, pointer or object conversion, a Cast of its operand, Proven where it checks
    // nothing; a folded direct literal is its constant and evaluates nothing, a fitted literal is its operand, and a Never operand only
    // diverges. A case adaptation (SPEC 8.9), whose operation each Semantics case selects (an acquisition, a borrow, an address or an object
    // creation), has no MIR form: the only case-dependent forms are PairLayer and PairAcquire (R3 U16a: an owner decision).
    private void Convert(MirPlace destination, BinaryKoto conversion, BoundType type)
    {
        var binding = (ConversionBinding)HirTables.PlanOf(conversion).Mode;
        if (HirTables.PlanOf(conversion).Folded is { } folded)
        {
            this.Use(
                destination,
                MirOperand.Const(
                this.body.Const(
                new(
                type.IsFloatingPoint ? MirConstKind.Float : MirConstKind.Integer,
                type,
                folded))),
                conversion);
        }
        else if (binding is ConversionBinding.Literal or ConversionBinding.Abrupt)
        {
            this.Assign(binding == ConversionBinding.Literal ? destination : this.Temp(conversion.Left), conversion.Left);
        }
        else if (binding == ConversionBinding.CaseAdaptation)
        {
            this.Unsupported(conversion, destination);
        }
        else
        {
            // SPEC 13.5.7: an upcast to a view of another Semantics borrows its operand's payload in the view's mode, then changes the view.
            var borrowed = HirTables.AdjustOf(conversion.Left).Adaptation is HirAdaptation.SharedBorrow or HirAdaptation.ExclusiveBorrow &&
                TypeOf(conversion.Left).Components is [var payload]
                ? compilation.Types.Reference(type.Semantics, payload) : null;
            var operand = this.Operand(conversion.Left, borrowed);
            var source = this.TypeOf(operand) ?? type;
            var kind = binding switch
            {
                ConversionBinding.Wrap => MirCastKind.Wrap,
                ConversionBinding.Bits => MirCastKind.Bits,
                ConversionBinding.ObjectUpcast => MirCastKind.Upcast,
                ConversionBinding.Pointer => source.Semantics == SemanticsKind.Raw ? MirCastKind.Pointer : MirCastKind.IntToPointer,
                _ when source.IsFloatingPoint == type.IsFloatingPoint => source.IsFloatingPoint ? MirCastKind.Floating : MirCastKind.Integer,
                _ => MirCastKind.Numeric,
            };

            // IMPL 21.5.3: an integer conversion whose source range lies within its target's checks nothing (T and Wrapping<T>, u8@u32,
            // i8@i64), nor does a widening floating-point one or one from an integer to a floating-point Type.
            var proven = kind switch
            {
                MirCastKind.Integer => this.Width(source, out var signed) is var from and > 0 && this.Width(type, out var target) is var to and > 0 &&
                    (target || !signed) && from - (signed ? 1 : 0) <= to - (target ? 1 : 0),
                MirCastKind.Floating => ReferenceEquals(type, BoundType.F64),
                MirCastKind.Numeric => type.IsFloatingPoint,
                _ => true,
            };

            this.Set(destination, MirRvalue.Cast(kind, this.body.AddOperand(operand), type) with { Proven = proven }, conversion);
        }
    }

    // IMPL 21.5.3: whether an operation cannot Abort: it checks nothing (a comparison, a bit operation, or an operation of a floating-point,
    // raw pointer or wrapping Type), or a literal right operand proves its check: a divisor other than 0 and, for a checked signed
    // quotient, -1; a shift count below the left operand's width.
    private bool Proven(MirBinaryOp op, BoundType type, in MirOperand right)
    {
        var literal = right.Kind == MirOperandKind.Const && this.body.Consts[right.Arg].Kind == MirConstKind.Integer
            ? this.body.Consts[right.Arg].Bits : (Int128?)null;
        return op switch
        {
            MirBinaryOp.Add or MirBinaryOp.Sub or MirBinaryOp.Mul => type.IsFloatingPoint || type.IsWrappingInteger || type.Semantics == SemanticsKind.Raw,
            MirBinaryOp.Div or MirBinaryOp.Rem => type.IsFloatingPoint || (literal is { } divisor && divisor != 0 && (divisor != -1 ||
                op == MirBinaryOp.Rem || !type.IsInteger || type.IsUnsignedInteger)),
            MirBinaryOp.Shl or MirBinaryOp.Shr => literal is { } count && count >= 0 && count < this.Width(type, out _),
            _ => true,
        };
    }

    // SPEC 3.1: the bit width of an integer Type's representation and its signedness, isize and usize the target's pointer width; 0 for
    // another Type, such as a Type parameter, whose instance decides it.
    private int Width(BoundType type, out bool signed)
    {
        var integer = type.Underlying;
        signed = !integer.IsUnsignedInteger;
        if (integer is not { IsInteger: true, Kind: BoundTypeKind.Primitive })
        {
            return 0;
        }

        if (integer.Name is "isize" or "usize")
        {
            return compilation.PointerWidth;
        }

        return int.TryParse(integer.Name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var width) ? width : 0;
    }
}
