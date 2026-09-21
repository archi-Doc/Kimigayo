// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    internal bool SupportsOriginObligations()
    {
        var obligations = this.compilation.Binding.Obligations;
        for (var i = 0; i < obligations.Count; i++)
        {
            var obligation = obligations[i];
            if (this.compilation.Binding.IsVerifiedLengthObligation(obligation))
            {
                continue; // Definition conditions and each call's substituted lengths were checked by Binding.
            }

            if (this.compilation.Binding.IsVerifiedOriginObligation(obligation))
            {
                continue;
            }

            // A well-formed borrowed input guarantees its nested stored Origins
            // outlive that input. Call-site borrow formation checks the concrete
            // nested dependencies, including deinit uses, in VerifyBorrows.
            if (obligation.Kind != BindingObligationKind.OriginOutlives || obligation.Deadline != BindingDeadline.BodyOrigins ||
                obligation.Shorter is not { Kind: OriginKind.Input, Binder: FunctionKoto function } outer ||
                (uint)outer.Slot >= (uint)function.Parameters.Count ||
                function.Parameters[outer.Slot].Type.BoundType is not { } input || !ReferenceTypes.IsStruct(input) ||
                !ReferenceEquals(input.Origin, outer) || !ReferenceEquals(input.Components[0], obligation.Type) ||
                !input.Components[0].OriginArguments.Contains(obligation.Longer!))
            {
                return false;
            }
        }

        return true;
    }

    private int BorrowStruct(Koto source, BoundType type, int reservation = -1)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(source);
        if (unwrapped is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Slice } slice && type.Semantics == SemanticsKind.Ref)
        {
            var handle = this.Expression(slice.Left);
            var subscript = this.Value(this.Expression(slice.Right));
            return handle < 0 || subscript < 0 ? -1 : this.SequenceValue(slice, type, SequenceOperation.Borrow, handle, index: subscript);
        }

        if (unwrapped is IndexKoto index && ReferenceTypes.IsArray(index.Left.BoundType) &&
            index.Left.BoundType!.Semantics == SemanticsKind.Ref && type.Semantics == SemanticsKind.Ref)
        {
            var receiver = this.Expression(index.Left, PlaceUseKind.Read);
            var receiverValue = this.Value(receiver);
            var subscript = this.Expression(index.Right);
            if (receiver < 0 || subscript < 0)
            {
                return -1;
            }

            var projected = this.Place(index, type, OwnershipPlaceKind.Temporary, false);
            var address = this.Emit(OwnershipOperationKind.Borrow, index, receiver, projected, loanMode: LoanRequirement.Ref);
            this.SetValue(address, OwnershipValueKind.Address, [receiverValue, this.Value(subscript)], constant: receiver);
            return this.RegisterTemporary(projected);
        }

        if (unwrapped is MemberAccessKoto field && !ReferenceTypes.IsStorage(field.BoundType) &&
            ElementAccess.BorrowedPathRoot(field) is { } root)
        {
            var receiver = this.Expression(root, PlaceUseKind.Read);
            if (receiver < 0)
            {
                return -1;
            }

            var projected = this.Place(field, type, OwnershipPlaceKind.Temporary, false);
            var address = this.Emit(OwnershipOperationKind.Borrow, field, receiver, projected, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
            this.SetValue(address, OwnershipValueKind.Address, [this.Value(receiver)], constant: receiver);
            return this.RegisterTemporary(projected);
        }

        if (unwrapped is MemberAccessKoto path && ReferenceEquals(type.Components[0], path.BoundType) && !ObjectTypes.IsOwner(path.BoundType) &&
            ElementAccess.OwnedPathRoot(path) is { } owner)
        {
            // Borrow the inline part in place; its Loan footprint is the static path (SPEC 15.6.2).
            var ownerPlace = this.Local(owner);
            if (ownerPlace < 0)
            {
                return -1;
            }

            var borrowed = this.Place(path, type, OwnershipPlaceKind.Temporary, false);
            var borrow = this.Emit(OwnershipOperationKind.Borrow, path, ownerPlace, borrowed, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
            this.SetValue(borrow, OwnershipValueKind.Address, [], constant: ownerPlace);
            return this.RegisterTemporary(borrowed);
        }

        var place = (StructStorage.IsStruct(source.BoundType) || source.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple || ScalarTypes.Supports(source.BoundType)) && unwrapped is IdentifierNameKoto
            ? this.Local(unwrapped) : this.Expression(source, PlaceUseKind.Read);
        if (place < 0)
        {
            return -1;
        }

        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
        // A scalar temporary is materialized at the borrow from its one prepared value (SPEC 3.6.2, 10.2).
        var materialized = ScalarTypes.Supports(source.BoundType) && this.body.Places[place].Kind == OwnershipPlaceKind.Temporary;
        this.SetValue(operation, OwnershipValueKind.Address, ReferenceTypes.IsBorrow(source.BoundType) || materialized ? [this.Value(place)] : [], constant: place);
        return this.RegisterTemporary(result);
    }

    private int ReadBorrowedField(MemberAccessKoto field)
    {
        var receiver = this.Expression(ElementAccess.BorrowedPathRoot(field)!, PlaceUseKind.Read);
        if (receiver < 0)
        {
            return -1;
        }

        if ((!ReferenceTypes.IsValue(field.BoundType) && field.BoundType?.Kind != BoundTypeKind.Slice) || field.BoundType!.Semantics == SemanticsKind.Uniq)
        {
            this.Unsupported(field); // Non-Copy fields require an explicit reborrow, never an implicit Move.
            return -1;
        }

        var result = this.Temporary(field);
        this.SetValue(this.Value(result), OwnershipValueKind.BorrowedField, [this.Value(receiver)]);
        return result;
    }

    private int WriteBorrowedField(BinaryKoto assignment, MemberAccessKoto field)
    {
        if (assignment.Akind != KotoKind.Equals)
        {
            return this.UpdateBorrowedField(assignment, field);
        }

        var root = ElementAccess.BorrowedPathRoot(field)!;
        if (root.BoundType?.Semantics != SemanticsKind.Uniq ||
            !ReferenceTypes.IsValue(field.BoundType))
        {
            this.Unsupported(assignment);
            return -1;
        }

        var input = this.Expression(assignment.Right);
        var receiver = this.Expression(root, PlaceUseKind.Read);
        if (input < 0 || receiver < 0)
        {
            return -1;
        }

        var operation = this.Emit(OwnershipOperationKind.WriteBorrowedField, assignment, receiver, input);
        this.SetValue(operation, OwnershipValueKind.BorrowedFieldWrite, [this.Value(receiver), this.Value(input)]);
        return this.Temporary(assignment);
    }

    private int UpdateBorrowedField(Koto source, MemberAccessKoto field)
    {
        var operation = ElementAccess.UpdateOperator(source.Akind);
        var root = ElementAccess.BorrowedPathRoot(field)!;
        if (root.BoundType?.Semantics != SemanticsKind.Uniq || field.BoundType?.IsNumeric != true || operation == KotoKind.Invalid)
        {
            this.Unsupported(source);
            return -1;
        }

        // Secure the receiver and old value before evaluating the RHS. Keep the
        // original SSA receiver even if later evaluation reads the same Place.
        var receiver = this.BorrowStruct(root, root.BoundType!);
        var receiverValue = this.Value(receiver);
        var previous = -1;
        if (receiver >= 0)
        {
            var read = this.Temporary(field);
            previous = this.Value(read);
            this.SetValue(previous, OwnershipValueKind.BorrowedField, [receiverValue]);
        }

        var right = source is BinaryKoto binary ? this.Value(this.Expression(binary.Right)) : previous >= 0 ? this.IncrementOne(source) : -1;
        if (previous < 0 || right < 0 || !this.flow!.Nodes[source].CanCompleteNormally)
        {
            return -1;
        }

        var updated = this.ComputeUpdate(source, field.BoundType, previous, right, operation);
        var write = this.Emit(OwnershipOperationKind.WriteBorrowedField, source, receiver, updated);
        this.SetValue(write, OwnershipValueKind.BorrowedFieldWrite, [receiverValue, this.Value(updated)]);
        return this.UpdateResult(source, previous, updated);
    }
}
