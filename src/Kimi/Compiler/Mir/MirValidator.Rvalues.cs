// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// Rvalues and operands (plan 5.4).
// - Responsibility: an rvalue's kind row, operand range, marked Use (a PairAcquire's only, A3 E13 (4)), Opaque site and arity by its
//   kind and Type (Rvalue, Arity), an aggregate's by its shape, the one rule of its operand count and each operand's Type (Shape), a
//   Discriminant's enum Place and a Len's sequence Place (IsSequence); and each operand MirUses reports (Operand): its kind row, a
//   constant in range and an Integer constant its Type's canonical payload or of the widthless family MirTypes.HoldsIntegerConstant
//   publishes, ranged by its member (Holds, G620, G644), a PairAcquire only as a marked Use's whole rvalue and of its Place's pair
//   binder, a function item constant only as an Assign's Use (G154), and a Copy, but a case site's (Views), only of a Type Binding
//   proves Copy under the body definition's premises (its TyCtxt.Copies; DECISIONS BU-88).
// - Inputs: an Assign's rvalue with its destination's Type; each location's operands, with the positions Statement marks.
// - Outputs: UnknownKind, OperandRange, Operation, Opaque, Arity, Discriminant, Len, Const and Binder findings; each Opaque value
//   counted for Finish; an rvalue whose arity holds goes to the Type tier (MirValidator.Types), which reads an aggregate's shape.
// - Invariants: Rvalue returns false when its operands or its Opaque site are out of range, which MirUses then does not walk.
// - Prohibitions: no Type agreement (MirValidator.Types), no Call rule (MirValidator.Calls) and no proof of its own: a Copy reads
//   Binding's.
internal sealed partial class MirValidator
{
    // An aggregate's shape (Shape): Count operands, -1 when its kind and Type disagree, each initializing, as the Place it initializes
    // projects it, Element (a fixed array's, a Fill's or an Array's), or else the struct's Base first, then the Fields of Parts in order
    // (a Tuple's elements, a Closure's captures, a struct's own Fields, a Case's payload Tuple's elements).
    private readonly record struct AggregateShape(int Count, BoundType? Element = null, BoundType? Base = null, BoundType? Parts = null);

    private partial struct Checker
    {
        public void Operand(in MirOperand operand)
        {
            if (!MirDescriptors.Of(operand.Kind).Defined)
            {
                this.Report(MirViolation.UnknownKind);
            }
            else if (operand.Kind == MirOperandKind.Const && ((uint)operand.Arg >= (uint)body.Consts.Length || !Holds(body.Consts[operand.Arg])))
            {
                this.Report(MirViolation.Const);
            }
            else if (operand.Kind == MirOperandKind.PairAcquire && !this.acquires)
            {
                this.Report(MirViolation.Operation); // A3 E13 (4): always the whole rvalue of an Assign to a local, marked.
            }
            else if (operand.Kind == MirOperandKind.Const && body.Consts[operand.Arg].Kind == MirConstKind.FnItem && !this.used)
            {
                this.Report(MirViolation.Operation); // G154: the collector finds a function item's edge only at a Use (K 4.8).
            }
            else if (operand.Kind == MirOperandKind.Const && body.Consts[operand.Arg].IsRecord && !this.calling)
            {
                this.Report(MirViolation.Operation); // ConstantRecords: a constant record is only a Call's input.
            }
            else if (operand.Kind == MirOperandKind.PairAcquire && this.IsValid(operand.Place) && !body.Binds(
                operand.Arg,
                TyCtxt.PairBinder(body.TypeOf(operand.Place))))
            {
                this.Report(MirViolation.Binder);
            }
            else if (operand.Kind == MirOperandKind.Copy && !this.byCase && this.TypeOf(operand) is { } type && !this.copies.Copy(type))
            {
                this.Report(MirViolation.Operation); // DECISIONS BU-88: Binding's proof under the definition's premises, read, never made.
            }
        }

        // G620, G644: an Integer constant has a Type of the family MirTypes.HoldsIntegerConstant publishes and holds a value of it: (i) a
        // Type with a width its canonical N-bit payload, sign-extended for every Type (ScalarTypes.Normalize, MirConstKind.Integer; an
        // unsigned value of 2^(N-1) and above is negative), isize and usize at the profile's width; (ii) char a Unicode scalar value (SPEC
        // 3.1); (iii) a raw pointer null, 0 (SPEC 5.1); (iv) a Type whose instance decides it a generic literal, 0 through 127, a value of
        // every instance (SPEC 8.7.4; Binding types a literal it fits through an identity premise at the complete Type, BU-72); else a
        // finding.
        private static bool Holds(in MirConst constant) => constant.Kind != MirConstKind.Integer ||
            (constant.Type.Width is > 0 and var width ? ScalarTypes.Normalize(constant.Bits, width) == constant.Bits
            : MirTypes.HoldsIntegerConstant(constant.Type) && (ReferenceEquals(constant.Type, BoundType.Char)
                ? constant.Bits >= 0 && constant.Bits <= 0x10FFFF && Rune.IsValid((int)constant.Bits)
                : constant.Bits >= 0 && constant.Bits <= (constant.Type.Semantics == SemanticsKind.Raw ? 0 : 127)));

        private static bool IsSequence(BoundType type)
            => type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary ||
                ReferenceEquals(type, BoundType.String);

        // The operands an rvalue has: one or two, a keyed Storage's two, or an aggregate's by its shape (Shape); none for the other kinds.
        private static int Arity(in MirRvalue rvalue) => rvalue.Kind switch
        {
            MirRvalueKind.Use or MirRvalueKind.UnaryOp or MirRvalueKind.Cast => 1,
            MirRvalueKind.BinaryOp or MirRvalueKind.Adapt => 2,
            MirRvalueKind.Storage when rvalue.Count != 0 => 2, // A key selection's: the start and the key's Type token (G597).
            MirRvalueKind.Aggregate => Shape(rvalue).Count,
            _ => 0,
        };

        // An aggregate's shape by its kind and Type, the one rule its arity and its operands' Types read: as many operands as its Type's
        // elements, Fields (the base first) or Case payload elements, or captures, -1 when its kind and Type disagree. A fixed array built
        // from no operand stores no element (SPEC 4.3.4 noinit), the only count at a generic length, and a Fill has its one value (SPEC
        // 4.3.3, DECISIONS R4-M81); an Array has any number of elements, none for `[]`, and a Dictionary is built from none, the empty
        // collection (SPEC 4.3.1, 12.4.1; DECISIONS R5-M129).
        private static AggregateShape Shape(in MirRvalue rvalue)
        {
            var type = rvalue.Type;
            return (MirAggregateKind)rvalue.Op switch
            {
                MirAggregateKind.Tuple when type?.Kind == BoundTypeKind.Tuple => new(type.Components.Count, Parts: type),
                MirAggregateKind.Closure when type?.Kind == BoundTypeKind.Closure => new(type.Components.Count, Parts: type),
                MirAggregateKind.FixedArray when type?.Kind == BoundTypeKind.FixedArray => new(
                    rvalue.Count == 0 || type.LengthExpression is not null ? 0 : (int)type.Length,
                    type.Components[0]),
                MirAggregateKind.FixedArray when type?.Kind == BoundTypeKind.Dictionary => new(0),
                MirAggregateKind.Array when type?.Kind == BoundTypeKind.Array => new(rvalue.Count, type.Components[0]),
                MirAggregateKind.Fill when type?.Kind == BoundTypeKind.FixedArray => new(1, type.Components[0]),
                MirAggregateKind.Adt when AdtDef.IsStruct(type) && AdtDef.Base(type!) is var parent => new(
                    AdtDef.Count(type!) + (parent is null ? 0 : 1),
                    Base: parent,
                    Parts: type),
                MirAggregateKind.Adt when type is not null && AdtDef.CaseTypes(type) is { } cases => (uint)rvalue.Arg < (uint)cases.Length &&
                    cases[rvalue.Arg] is { } payload ? new(payload.Components.Count, Parts: payload) : new(-1),
                _ => new(-1),
            };
        }

        // False when the operands or the Opaque site are out of range, which MirUses then does not walk.
        private bool Rvalue(in MirRvalue rvalue, BoundType? destination)
        {
            if (!MirDescriptors.Of(rvalue.Kind).Defined || (rvalue.Kind == MirRvalueKind.Ref && rvalue.Op > (byte)MirRefKind.Raw) ||
                (rvalue.Kind == MirRvalueKind.Storage && rvalue.Op != (byte)MirRefKind.Raw) ||
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
