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

    private int BorrowStruct(Koto source, BoundType type)
    {
        var unwrapped = KotoHelper.UnwrapParentheses(source);
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

        if (unwrapped is MemberAccessKoto field && ReferenceTypes.IsStruct(field.Left.BoundType))
        {
            var receiver = this.Expression(field.Left, PlaceUseKind.Read);
            if (receiver < 0)
            {
                return -1;
            }

            var projected = this.Place(field, type, OwnershipPlaceKind.Temporary, false);
            var address = this.Emit(OwnershipOperationKind.Borrow, field, receiver, projected, loanMode: type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
            this.SetValue(address, OwnershipValueKind.Address, [this.Value(receiver)], constant: receiver);
            return this.RegisterTemporary(projected);
        }

        var place = (StructStorage.IsStruct(source.BoundType) || source.BoundType?.Kind == BoundTypeKind.FixedArray) && unwrapped is IdentifierNameKoto
            ? this.Local(unwrapped) : this.Expression(source, PlaceUseKind.Read);
        if (place < 0)
        {
            return -1;
        }

        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref);
        this.SetValue(operation, OwnershipValueKind.Address, ReferenceTypes.IsStorage(source.BoundType) ? [this.Value(place)] : [], constant: place);
        return this.RegisterTemporary(result);
    }

    private int ReadBorrowedField(MemberAccessKoto field)
    {
        var receiver = this.Expression(field.Left, PlaceUseKind.Read);
        if (receiver < 0)
        {
            return -1;
        }

        if (!ReferenceTypes.IsValue(field.BoundType) || field.BoundType!.Semantics == SemanticsKind.Uniq)
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
        if (assignment.Akind != KotoKind.Equals || field.Left.BoundType?.Semantics != SemanticsKind.Uniq ||
            !ReferenceTypes.IsValue(field.BoundType))
        {
            this.Unsupported(assignment);
            return -1;
        }

        var input = this.Expression(assignment.Right);
        var receiver = this.Expression(field.Left, PlaceUseKind.Read);
        if (input < 0 || receiver < 0)
        {
            return -1;
        }

        var operation = this.Emit(OwnershipOperationKind.WriteBorrowedField, assignment, receiver, input);
        this.SetValue(operation, OwnershipValueKind.BorrowedFieldWrite, [this.Value(receiver), this.Value(input)]);
        return this.Temporary(assignment);
    }
}
