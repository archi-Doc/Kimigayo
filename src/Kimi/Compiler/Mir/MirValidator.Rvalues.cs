// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// Rvalues and operands (plan 5.4).
// - Responsibility: an rvalue's kind row, operand range, marked Use (a PairAcquire's only, A3 E13 (4)), Opaque site and arity by its
//   kind and Type (Rvalue, Arity), a Discriminant's enum Place and a Len's sequence Place (IsSequence); and each operand MirUses
//   reports (Operand): its kind row, a constant in range, a PairAcquire only as a marked Use's whole rvalue and of its Place's pair
//   binder, a function item constant only as an Assign's Use (G154).
// - Inputs: an Assign's rvalue with its destination's Type; each location's operands, with the positions Statement marks.
// - Outputs: UnknownKind, OperandRange, Operation, Opaque, Arity, Discriminant, Len, Const and Binder findings; each Opaque value
//   counted for Finish; an rvalue whose arity holds goes to the Type tier (MirValidator.Types).
// - Invariants: Rvalue returns false when its operands or its Opaque site are out of range, which MirUses then does not walk.
// - Prohibitions: no Type agreement (MirValidator.Types) and no Call rule (MirValidator.Calls).
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        public void Operand(in MirOperand operand)
        {
            if (!MirDescriptors.Of(operand.Kind).Defined)
            {
                this.Report(MirViolation.UnknownKind);
            }
            else if (operand.Kind == MirOperandKind.Const && (uint)operand.Arg >= (uint)body.Consts.Length)
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
            else if (operand.Kind == MirOperandKind.PairAcquire && this.IsValid(operand.Place) && !body.Binds(
                operand.Arg,
                PairBinderOf(body.TypeOf(operand.Place))))
            {
                this.Report(MirViolation.Binder);
            }
        }

        private static BindingSymbol? PairBinderOf(BoundType? type)
        {
            _ = TyCtxt.Layer(type, out _, out var binder);
            return binder;
        }

        private static bool IsSequence(BoundType type)
            => type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary ||
                ReferenceEquals(type, BoundType.String);

        // The operands an rvalue has: one or two, as many as its Type's elements, Fields (the base first) or Case payload, or captures; -1
        // when an aggregate's kind and Type disagree. A fixed array of a generic length is counted by its instance, and one built from no
        // operand stores no element (SPEC 4.3.4 noinit); an Array or a Dictionary is built from none, the empty collection (SPEC 12.4.1).
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
                    MirAggregateKind.FixedArray when type?.Kind == BoundTypeKind.FixedArray => type.LengthExpression is null && type.Length >= 0 &&
                        rvalue.Count > 0 ? (int)type.Length : rvalue.Count,
                    MirAggregateKind.FixedArray when type?.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary => 0,
                    MirAggregateKind.Adt when AdtDef.IsStruct(type) => AdtDef.Count(type!) + (AdtDef.Base(type!) is null ? 0 : 1),
                    MirAggregateKind.Adt when type is not null && AdtDef.CaseTypes(type) is { } cases => (uint)rvalue.Arg < (uint)cases.Length
                        ? cases[rvalue.Arg].Components.Count : -1,
                    _ => -1,
                },
                _ => 0,
            };
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
