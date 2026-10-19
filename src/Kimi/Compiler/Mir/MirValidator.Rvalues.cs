// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// One rvalue's validation: its kind, operand range and arity, admitted operand Types, destination Type and Never-value writers.
// Shared Type agreement and case-view resolution remain in MirValidator.Types.cs; structure is walked only after its ranges are valid.
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        private static bool IsSequence(BoundType type)
            => type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary ||
                ReferenceEquals(type, BoundType.String);

        // The operands an rvalue has: one or two, as many as its Type's elements, Fields (the base first) or Case payload, or captures; -1
        // when an aggregate's kind and Type disagree. A fixed array built from no operand stores no element (SPEC 4.3.4 noinit), the only
        // count at a generic length, and a Fill has its one value (SPEC 4.3.3, DECISIONS R4-M81); an Array or a Dictionary is built from
        // none, the empty collection (SPEC 12.4.1).
        private static int Arity(in MirRvalue rvalue)
        {
            var type = rvalue.Type;
            return rvalue.Kind switch
            {
                MirRvalueKind.Use or MirRvalueKind.UnaryOp or MirRvalueKind.Cast => 1,
                MirRvalueKind.BinaryOp or MirRvalueKind.Adapt => 2,
                MirRvalueKind.Aggregate => (MirAggregateKind)rvalue.Op switch
                {
                    MirAggregateKind.Tuple when type?.Kind == BoundTypeKind.Tuple => type.Components.Count,
                    MirAggregateKind.Closure when type?.Kind == BoundTypeKind.Closure => type.Components.Count,
                    MirAggregateKind.FixedArray when type?.Kind == BoundTypeKind.FixedArray => rvalue.Count == 0 || type.LengthExpression is not null
                        ? 0 : (int)type.Length,
                    MirAggregateKind.FixedArray when type?.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary => 0,
                    MirAggregateKind.Fill when type?.Kind == BoundTypeKind.FixedArray => 1,
                    MirAggregateKind.Adt when AdtDef.IsStruct(type) => AdtDef.Count(type!) + (AdtDef.Base(type!) is null ? 0 : 1),
                    MirAggregateKind.Adt when type is not null && AdtDef.CaseTypes(type) is { } cases => (uint)rvalue.Arg < (uint)cases.Length
                        ? cases[rvalue.Arg].Components.Count : -1,
                    _ => -1,
                },
                _ => 0,
            };
        }

        // An integer Type, or one whose instance decides it (SPEC 8.7.4: a PrimitiveInteger T, Wrapping<T>).
        private static bool Integral(BoundType type) => type.HasIntegerArithmetic || type.HasAbstractPart;

        // SPEC 13.5.4, 13.6.1, 5.4, 10.2: whether a Cast of its kind converts a source Type to a target Type.
        private static bool Casts(MirCastKind kind, BoundType source, BoundType target) => kind switch
        {
            MirCastKind.Integer or MirCastKind.Wrap => Integral(source) && Integral(target),
            MirCastKind.Floating => source.IsFloatingPoint && target.IsFloatingPoint,
            MirCastKind.Numeric or MirCastKind.Bits => source.IsFloatingPoint ? Integral(target) : Integral(source) && target.IsFloatingPoint,
            MirCastKind.Pointer => source.Semantics == SemanticsKind.Raw && (target.Semantics == SemanticsKind.Raw || ReferenceEquals(target, BoundType.USize)),
            MirCastKind.IntToPointer => Integral(source) && target.Semantics == SemanticsKind.Raw,
            MirCastKind.Upcast => source.Semantics == target.Semantics && (source.HandleMode is not null || source.IsObjectBorrow),
            MirCastKind.Erasure => source.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem && target.Kind == BoundTypeKind.Function,
            _ => false,
        };

        // Whether an rvalue may write a Temp of Type Never (DECISIONS R5-M38): an unadapted Copy or Move of a Place of Type Never, or an
        // Opaque value; a diverging Call's destination is the third writer.
        private readonly bool WritesNever(in MirRvalue rvalue) => rvalue.Kind == MirRvalueKind.Opaque ||
            (rvalue is { Kind: MirRvalueKind.Use, Op: 0, Count: 1 } &&
                body.Operands[rvalue.Start] is { Kind: MirOperandKind.Copy or MirOperandKind.Move } operand &&
                this.IsValid(operand.Place) && ReferenceEquals(body.TypeOf(operand.Place), BoundType.Never));

        // The Type of an aggregate's operand, as the Place it initializes projects it: a Tuple's element, a Closure's capture, a struct's base
        // then its own Fields, a Case's payload, a fixed array's or a Fill's element.
        private readonly BoundType? Component(in MirRvalue rvalue, int index) => (MirAggregateKind)rvalue.Op switch
        {
            MirAggregateKind.FixedArray or MirAggregateKind.Fill => rvalue.Type!.Components[0],
            MirAggregateKind.Adt when AdtDef.IsEnum(rvalue.Type) => body.ProjectedType(
                body.ProjectedType(
                    rvalue.Type,
                    MirProjectionKind.Downcast,
                    rvalue.Arg),
                MirProjectionKind.Field,
                index),
            MirAggregateKind.Adt when AdtDef.Base(rvalue.Type!) is { } parent => index == 0 ? parent : body.ProjectedType(
                rvalue.Type,
                MirProjectionKind.Field,
                index - 1),
            _ => body.ProjectedType(rvalue.Type, MirProjectionKind.Field, index),
        };

        // SPEC 8.14: an Adapt acquires its operand by value, a Place's Copy or a temporary's Move, beside the token of Binding's source Type,
        // into its target Type. A Place or a constant out of range is the walk's finding.
        private readonly bool Adapts(in MirRvalue adapt, ReadOnlySpan<MirOperand> operands)
            => adapt.Type is not null && operands[0].Kind is MirOperandKind.Copy or MirOperandKind.Move &&
                operands[1] is { Kind: MirOperandKind.Const, Arg: var token } &&
                ((uint)token >= (uint)body.Consts.Length || body.Consts[token].Kind == MirConstKind.TypeToken);

        // SPEC 3.2-3.4, 13.2-13.5: the Type an rvalue gives agrees with its destination's: a Use's or a unary operator's operand's, another
        // operator's left operand's, a comparison's bool, a Discriminant's or Len's isize, else its own Type. An operator's operands agree (a
        // comparison's below their layers) but for a shift count, any integer, and a raw pointer's displacement, an isize (SPEC 5.3); a Cast
        // converts what its kind admits; a Ref's Type is its kind's reference to its Place; an aggregate's operands are its components; an
        // Adapt gives its target Type.
        private void Types(in MirRvalue rvalue, BoundType? destination)
        {
            var operands = body.Operands.Slice(rvalue.Start, rvalue.Count);
            var op = (MirBinaryOp)rvalue.Op;
            var first = operands.Length > 0 ? this.TypeOf(operands[0]) : null;
            var second = operands.Length > 1 ? this.TypeOf(operands[1]) : null;
            var place = rvalue.Kind == MirRvalueKind.Ref && this.IsValid(rvalue.Place) ? body.TypeOf(rvalue.Place) : null;
            var (type, admitted) = rvalue.Kind switch
            {
                MirRvalueKind.Use or MirRvalueKind.UnaryOp => (first, true),
                MirRvalueKind.BinaryOp => (op >= MirBinaryOp.Eq ? BoundType.Boolean : first, op switch
                {
                    MirBinaryOp.Shl or MirBinaryOp.Shr => second is null || second.IsInteger || second.IsAbstract,
                    >= MirBinaryOp.Eq => this.Agrees(Terminal(first), Terminal(second)),
                    MirBinaryOp.Add or MirBinaryOp.Sub when first?.Semantics == SemanticsKind.Raw => second is null || ReferenceEquals(second, BoundType.ISize),
                    _ => this.Agrees(first, second),
                }),
                MirRvalueKind.Discriminant or MirRvalueKind.Len => (BoundType.ISize, true),
                MirRvalueKind.Cast => (rvalue.Type, first is null || rvalue.Type is null || Casts((MirCastKind)rvalue.Op, first, rvalue.Type)),
                MirRvalueKind.Adapt => (rvalue.Type, this.Adapts(rvalue, operands)),
                MirRvalueKind.Ref => (rvalue.Type, place is null ||
                    (rvalue.Type is { Kind: BoundTypeKind.Semantics, Components: [var referent] } reference && this.Agrees(referent, place) &&
                    (MirRefKind)rvalue.Op switch
                    {
                        MirRefKind.Shared => reference.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef,
                        MirRefKind.Raw => reference.Semantics == SemanticsKind.Raw,
                        _ => MirTypes.IsExclusive(reference),
                    })),
                _ => (rvalue.Type, true),
            };

            this.Agree(destination, type);
            if (!admitted)
            {
                this.Report(MirViolation.Operation);
            }
            else if (rvalue.Kind == MirRvalueKind.Adapt)
            {
                this.Agree(second, first); // Binding's source Type, the token, is the operand's.
                if (second is not null && this.IsValid(operands[0].Place) && this.IsValid(rvalue.Place))
                {
                    adapts.Add((rvalue, this.at)); // Its case views resolve it: its Places and token are in range.
                }
            }

            for (var i = 0; rvalue.Kind == MirRvalueKind.Aggregate && i < operands.Length; i++)
            {
                this.Agree(this.Component(rvalue, i), this.TypeOf(operands[i]));
            }
        }

        // False when the operands or the Opaque site are out of range, which MirUses then does not walk.
        private bool Rvalue(in MirRvalue rvalue, BoundType? destination)
        {
            if (!MirDescriptors.Of(rvalue.Kind).Defined || (rvalue.Kind == MirRvalueKind.Ref && rvalue.Op > (byte)MirRefKind.Raw) ||
                (rvalue.Kind == MirRvalueKind.Adapt && rvalue.Op > (MirRvalue.AdaptShorthand | MirRvalue.AdaptInput)))
            {
                this.Report(MirViolation.UnknownKind);
                return false;
            }

            if (!InRange(rvalue.Start, rvalue.Count, body.Operands.Length))
            {
                this.Report(MirViolation.OperandRange);
                return false;
            }

            if (rvalue is { Kind: MirRvalueKind.Use, Op: not 0, Count: 1 } && body.Operands[rvalue.Start].Kind != MirOperandKind.PairAcquire)
            {
                this.Report(MirViolation.Operation); // Only a PairAcquire's Use is marked (A3 E13 (4)).
            }

            if (rvalue.Kind == MirRvalueKind.Opaque && (uint)rvalue.Arg >= (uint)body.OpaqueSites.Length)
            {
                this.Report(MirViolation.Opaque);
                return false;
            }

            this.opaqueValues += rvalue.Kind == MirRvalueKind.Opaque ? 1 : 0;
            if (Arity(rvalue) != rvalue.Count)
            {
                this.Report(MirViolation.Arity);
            }
            else
            {
                this.Types(rvalue, destination);
            }

            var type = rvalue.Kind is MirRvalueKind.Discriminant or MirRvalueKind.Len && this.IsValid(rvalue.Place) ? body.TypeOf(rvalue.Place) : null;
            if (type is not null && (rvalue.Kind == MirRvalueKind.Discriminant ? !AdtDef.IsEnum(type) : !IsSequence(type)))
            {
                this.Report(rvalue.Kind == MirRvalueKind.Discriminant ? MirViolation.Discriminant : MirViolation.Len);
            }

            return true;
        }
    }
}
