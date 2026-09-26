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
        if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow } selected)
        {
            // SPEC 13.5.5.2: a Reborrow or payload borrow lends the referent's capability through the parent
            // reference or handle, which is read but never moved; the borrowed address is the parent's value.
            return this.BorrowStruct(selected.Left, type, reservation);
        }

        if (unwrapped is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Dictionary } dictionaryIndex && type.Semantics == SemanticsKind.Ref)
        {
            var depth = this.comparisonDepth++;
            var projection = this.LocateElement(dictionaryIndex);
            if (projection < 0)
            {
                this.EndComparisonLoans(depth, source);
                this.comparisonDepth = depth;
                return -1;
            }

            var plan = this.body.Projections[projection];
            var elementReference = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
            var borrow = this.Emit(OwnershipOperationKind.Borrow, source, plan.Root, elementReference, loanMode: LoanRequirement.Ref, projection: projection);
            this.SetValue(borrow, OwnershipValueKind.Address, [], constant: plan.Root);
            this.body.Projections[projection] = plan with { Borrow = borrow };
            this.EndComparisonLoans(depth, source);
            this.comparisonDepth = depth;
            return this.RegisterTemporary(elementReference);
        }

        if (unwrapped is IndexKoto slice && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef &&
            (slice.Left.BoundType?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array || ReferenceTypes.IsDynamicArray(slice.Left.BoundType)))
        {
            // A shared Reborrow of a stored exclusive reference or an objref view of a stored handle loads the stored
            // pointer (SPEC 10.2, 4.6.9); any other shared borrow takes the element slot's address.
            var reborrow = slice.BoundType is { } stored && SharedReadTypes.ReadsStoredPointer(stored, type);
            var depth = this.comparisonDepth++;
            var handle = this.SequenceReceiver(slice.Left, out var projection);
            var subscript = this.Value(this.Expression(slice.Right));
            var borrowedElement = handle < 0 || subscript < 0 ? -1 : this.SequenceValue(slice, type, reborrow ? SequenceOperation.Read : SequenceOperation.Borrow, handle, projection, index: subscript);
            this.EndComparisonLoans(depth, slice);
            this.comparisonDepth = depth;
            return borrowedElement;
        }

        if (unwrapped is IndexKoto index && ElementAccess.AccessType(index.Left) is { Semantics: SemanticsKind.Ref } array && ReferenceTypes.IsArray(array) &&
            type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef)
        {
            // SPEC 4.6.9: the fixed array is a declared reference or an element Place borrowed as the receiver. A stored
            // exclusive reference or handle is read as its shared view; any other element is borrowed at its address.
            if (index.BoundType is { } stored && SharedReadTypes.ReadsStoredPointer(stored, type))
            {
                if (!ReferenceTypes.IsArray(index.Left.BoundType))
                {
                    this.Unsupported(index); // An element Place borrowed as the receiver holds no stored pointer to read.
                    return -1;
                }

                var depth = this.comparisonDepth++;
                var handle = this.Expression(index.Left, PlaceUseKind.Read);
                var position = handle < 0 ? -1 : this.Value(this.Expression(index.Right));
                var read = position < 0 ? -1 : this.SequenceValue(index, type, SequenceOperation.Read, handle, index: position);
                this.EndComparisonLoans(depth, index);
                this.comparisonDepth = depth;
                return read;
            }

            if (type.Semantics != SemanticsKind.Ref)
            {
                this.Unsupported(index);
                return -1;
            }

            var receiver = this.Receiver(index.Left);
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

        if (unwrapped is MemberAccessKoto field && !Binding.IsGetterResult(field) && !this.SpecialField(field) && !ReferenceTypes.IsStorage(field.BoundType) &&
            ElementAccess.BorrowedPathRoot(field) is { } root)
        {
            var receiver = this.Receiver(root);
            if (receiver < 0)
            {
                return -1;
            }

            var projected = this.Place(field, type, OwnershipPlaceKind.Temporary, false);
            var address = this.Emit(OwnershipOperationKind.Borrow, field, receiver, projected, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
            this.SetValue(address, OwnershipValueKind.Address, [this.Value(receiver)], constant: receiver);
            return this.RegisterTemporary(projected);
        }

        if (unwrapped is BinaryKoto path && !Binding.IsGetterResult(path) && !this.SpecialField(path) && ReferenceEquals(type.Components[0], path.BoundType) && !ObjectTypes.IsOwner(path.BoundType) &&
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

        var place = this.SpecialField(unwrapped) ? this.Local(unwrapped)
            : source is FormattingKoto hidden && !ReferenceTypes.IsBorrow(hidden.BoundType) && this.formattingPlaces.TryGetValue(hidden, out var prepared) ? prepared
            : (StructStorage.IsStruct(source.BoundType) || EnumStorage.IsEnum(source.BoundType) || ReferenceEquals(source.BoundType, BoundType.String) || source.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple || ScalarTypes.Supports(source.BoundType)) && unwrapped is IdentifierNameKoto && unwrapped.BoundSymbol?.Kind != BindingSymbolKind.PatternCandidate
            ? this.Local(unwrapped) : this.Expression(source, PlaceUseKind.Read);
        if (place < 0)
        {
            return -1;
        }

        var result = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        var operation = this.Emit(OwnershipOperationKind.Borrow, source, place, result, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
        // A scalar temporary is materialized at the borrow from its one prepared value (SPEC 3.6.2, 10.2).
        var actual = this.body.Places[place];
        var materialized = ScalarTypes.Supports(actual.Type) && actual.Kind == OwnershipPlaceKind.Temporary;
        this.SetValue(operation, OwnershipValueKind.Address, ReferenceTypes.IsBorrow(actual.Type) || materialized ? [this.Value(place)] : [], constant: place);
        return this.RegisterTemporary(result);
    }

    // SPEC 3.4.1: a receiver with a recorded adaptation is evaluated to its one reference; any other receiver is read.
    private int Receiver(Koto root)
        => this.compilation.Binding.TryGetAdaptation(root, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.SharedBorrow
            ? this.BorrowStruct(root, adaptation.Type) : this.Expression(root, PlaceUseKind.Read);

    private int ReadBorrowedField(MemberAccessKoto field)
    {
        var receiver = this.Receiver(ElementAccess.BorrowedPathRoot(field)!);
        if (receiver < 0)
        {
            return -1;
        }

        if (this.Concrete(field.BoundType) is not { } type || !this.SupportsCopySnapshot(type, field))
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
        if (ElementAccess.AccessType(root)?.Semantics != SemanticsKind.Uniq ||
            !ReferenceTypes.IsValue(field.BoundType))
        {
            this.Unsupported(assignment);
            return -1;
        }

        var input = this.Expression(assignment.Right);
        var receiver = this.Receiver(root);
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
        var receiverType = ElementAccess.AccessType(root);
        if (receiverType?.Semantics != SemanticsKind.Uniq || field.BoundType?.IsNumeric != true || operation == KotoKind.Invalid)
        {
            this.Unsupported(source);
            return -1;
        }

        // SPEC 13.7.2: secure the RHS, then the receiver and old value. The receiver is read like that of a simple
        // write; nothing runs between reading the old value and storing the new one.
        var right = source is BinaryKoto binary ? this.Value(this.Expression(binary.Right)) : 0;
        var receiver = right < 0 ? -1 : this.Receiver(root);
        var receiverValue = this.Value(receiver);
        var previous = -1;
        if (receiver >= 0)
        {
            var read = this.Temporary(field);
            previous = this.Value(read);
            this.SetValue(previous, OwnershipValueKind.BorrowedField, [receiverValue]);
        }

        if (source is not BinaryKoto)
        {
            right = previous >= 0 ? this.IncrementOne(source) : -1;
        }

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
