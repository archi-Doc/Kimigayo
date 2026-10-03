// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    internal bool SupportsOriginObligations() => this.UnprovenOriginObligation() is null;

    // The use of the first Origin obligation that Binding left unproven and this analysis cannot check, or null.
    private Koto? UnprovenOriginObligation()
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

            // A well-formed borrowed input, or a pair-layer input whose outer slot is active only for a borrow binding (SPEC 8.1.2),
            // guarantees its nested stored Origins outlive that input. Call-site borrow formation checks the concrete
            // nested dependencies, including drop uses, in VerifyBorrows.
            if (obligation.Kind != BindingObligationKind.OriginOutlives || obligation.Deadline != BindingDeadline.BodyOrigins ||
                obligation.Shorter is not { Kind: OriginKind.Input, Binder: FunctionKoto function } outer ||
                (uint)outer.Slot >= (uint)function.Parameters.Count ||
                function.Parameters[outer.Slot].Type.BoundType is not { } input || !(ReferenceTypes.IsStruct(input) || Binding.TryPairLayer(input, out _, out _)) ||
                !ReferenceEquals(input.Origin, outer) || !ReferenceEquals(input.Components[0], obligation.Type) ||
                !(input.Components[0].OriginArguments.Contains(obligation.Longer!) || ReferenceEquals(input.Components[0].Origin, obligation.Longer)))
            {
                return obligation.Use;
            }
        }

        return null;
    }

    private int BorrowStruct(Koto source, BoundType type, int reservation = -1)
    {
        var unwrapped = this.SelectedPlace(KotoHelper.UnwrapParentheses(source));
        if (ElementAccess.IsRawPlace(unwrapped))
        {
            // Like a selected element, a reserved argument Reborrows the converted reference, so its reservation and
            // activation refer to a real exclusive borrow (SPEC 15.6.7).
            var raw = this.BorrowRawPlace(source, unwrapped, type);
            return raw >= 0 && reservation >= 0 ? this.BorrowThrough(source, raw, type, reservation) : raw;
        }

        if (this.ImplicitlyFollowsReference(unwrapped, type))
        {
            return this.BorrowStoredReference(unwrapped, unwrapped, type, reservation); // SPEC 7.3: a receiver through a pair layer.
        }

        if (this.instance is not null && this.compilation.Binding.ImplicitPairAdmitted(unwrapped) != SemanticsMask.None &&
            this.ReferenceLayers(unwrapped.BoundType, type.Components[0]) > 1)
        {
            // SPEC 3.4.1, 10.2, 13.5.5.1: several existing layers yield one reference in the receiver's mode (exclusive only
            // through exclusive layers, which Binding checked), which the receiver borrows through.
            var reference = this.ReadReference(unwrapped, type);
            return reference < 0 ? -1 : this.BorrowThrough(unwrapped, reference, type, reservation);
        }

        if ((ElementAccess.IsUserIndex(unwrapped) || ElementAccess.IsPlaceCall(unwrapped)) && ReferenceTypes.IsBorrow(unwrapped.BoundType) &&
            ReferenceEquals(unwrapped.BoundType!.Components[0], type.Components[0]))
        {
            return this.BorrowStoredReference(unwrapped, unwrapped, type, reservation);
        }

        if (unwrapped is IndexKoto userIndex && this.compilation.Binding.IndexerCall(userIndex, type.Semantics == SemanticsKind.Uniq) is { } indexer)
        {
            return this.BorrowStruct(indexer, type, reservation); // SPEC 4.6.9: a borrow of receiver[key] selects index or indexUniq.
        }

        if (unwrapped is InvocationKoto placeCall && ElementAccess.IsPlaceCall(placeCall))
        {
            // SPEC 7.1.1: a borrow of a published Place is a Reborrow through the reference the call returns; the call is
            // evaluated as that reference below, and the borrowed address is its value.
            this.referenceCalls.Add(placeCall);
        }

        if (unwrapped is ConversionKoto storedFollow && this.ReadsStoredReference(storedFollow))
        {
            return this.BorrowStoredReference(storedFollow, type, reservation); // SPEC 13.5.5.1: a Reborrow through the stored reference.
        }

        if (unwrapped is ConversionKoto { ConversionBinding: ConversionBinding.Follow or ConversionBinding.PayloadFollow or ConversionBinding.PairFollow } selected)
        {
            // SPEC 13.5.5.2: a Reborrow or payload borrow lends the referent's capability through the parent
            // reference or handle, which is read but never moved; the borrowed address is the parent's value.
            return this.BorrowStruct(selected.Left, type, reservation);
        }

        if (unwrapped is IndexKoto element && element.Right is not RangeKoto && type.Semantics == SemanticsKind.Uniq &&
            (element.Left.BoundType?.Kind == BoundTypeKind.Array || ElementAccess.IsExclusiveArrayElement(element)))
        {
            // SPEC 4.6.9, 4.5: an exclusive element borrow uses the array's existing exclusive capability (an owned
            // Array is borrowed like an exclusive receiver; a fixed/dynamic array reference is read) and selects the element
            // through that reference, as exclusive enumeration does; the element keeps the reference's dependency.
            var depth = this.comparisonDepth++;
            var handle = element.Left.BoundType!.Kind == BoundTypeKind.Array
                ? this.BorrowStruct(element.Left, this.compilation.Binding.ExclusiveArrayHandle(element.Left))
                : this.Expression(element.Left, PlaceUseKind.Read);
            var subscript = handle < 0 ? -1 : this.Value(this.SelectionKey(element, handle));
            var borrowedElement = handle < 0 || subscript < 0 ? -1 : this.SequenceValue(element, type, SequenceOperation.Borrow, handle, index: subscript);
            this.EndComparisonLoans(depth, element);
            this.comparisonDepth = depth;
            // A call reservation needs a real Reborrow of the selected address, not the Sequence operation that
            // computed it. This keeps the parent capability and makes activation refer to the acquired element.
            return borrowedElement >= 0 && reservation >= 0 ? this.BorrowThrough(source, borrowedElement, type, reservation) : borrowedElement;
        }

        if (unwrapped is IndexKoto slice && type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef &&
            (slice.Left.BoundType?.Kind is BoundTypeKind.Slice or BoundTypeKind.Array || ReferenceTypes.IsDynamicArray(slice.Left.BoundType)))
        {
            // A shared Reborrow of a stored exclusive reference or an objref view of a stored handle loads the stored
            // pointer (SPEC 10.2, 4.6.9); any other shared borrow takes the element slot's address.
            var reborrow = slice.BoundType is { } stored && SharedReadTypes.ReadsStoredPointer(stored, type);
            var depth = this.comparisonDepth++;
            var handle = this.SequenceReceiver(slice.Left, out var projection, type.Origin);
            var subscript = this.Value(this.SelectionKey(slice, handle, projection));
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
                var position = handle < 0 ? -1 : this.Value(this.SelectionKey(index, handle));
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
            var subscript = this.SelectionKey(index, receiver);
            if (receiver < 0 || subscript < 0)
            {
                return -1;
            }

            var projected = this.Place(index, type, OwnershipPlaceKind.Temporary, false);
            var address = this.Emit(OwnershipOperationKind.Borrow, index, receiver, projected, loanMode: LoanRequirement.Ref);
            this.SetValue(address, OwnershipValueKind.Address, [receiverValue, this.Value(subscript)], constant: receiver);
            return this.RegisterTemporary(projected);
        }

        if (unwrapped is MemberAccessKoto storedField && !Binding.IsGetterResult(storedField) && !this.SpecialField(storedField) &&
            ReferenceTypes.IsBorrow(storedField.BoundType) && ReferenceEquals(storedField.BoundType!.Components[0], type.Components[0]) &&
            ElementAccess.BorrowedPathRoot(storedField) is not null)
        {
            // SPEC 10.2: a fixed expected reference reborrows the stored pointer; an explicit borrow of the slot has
            // the complete field Type as its referent and therefore continues through the ordinary projection below.
            return this.BorrowStoredReference(storedField, storedField, type, reservation);
        }

        if (unwrapped is BinaryKoto storedPart && !Binding.IsGetterResult(storedPart) && !this.SpecialField(storedPart) &&
            ReferenceTypes.IsBorrow(storedPart.BoundType) && ReferenceEquals(storedPart.BoundType!.Components[0], type.Components[0]) &&
            ElementAccess.OwnedPathRoot(storedPart) is not null)
        {
            // SPEC 10.2, 13.5.5.1: the same Reborrow of a pointer stored in an inline part of an owned root.
            return this.BorrowStoredReference(storedPart, storedPart, type, reservation);
        }

        // A field holding a reference is borrowed here only as its slot (`p.0@ref` of `p: ref/(ref/i32, i32)`), never through
        // a copy of the stored reference, whose temporary would not outlive the call (SPEC 3.3.6, 15.6.2).
        if (unwrapped is MemberAccessKoto field && !Binding.IsGetterResult(field) && !this.SpecialField(field) &&
            (!ReferenceTypes.IsStorage(field.BoundType) || ReferenceEquals(type.Components[0], field.BoundType)) &&
            ElementAccess.BorrowedPathRoot(field) is { } root)
        {
            var receiver = this.Receiver(root, type.Semantics == SemanticsKind.Uniq);
            if (receiver < 0)
            {
                return -1;
            }

            var projected = this.Place(field, type, OwnershipPlaceKind.Temporary, false);
            var address = this.Emit(OwnershipOperationKind.Borrow, field, receiver, projected, loanMode: type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, reservation: reservation);
            this.SetValue(address, OwnershipValueKind.Address, [this.Value(receiver)], constant: receiver);
            return this.RegisterTemporary(projected);
        }

        if (unwrapped is BinaryKoto path && !Binding.IsGetterResult(path) && !this.SpecialField(path) && ReferenceEquals(type.Components[0], path.BoundType) && ObjectTypes.HandleMode(path.BoundType) is null &&
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

        var direct = (StructStorage.IsStruct(source.BoundType) || EnumStorage.IsEnum(source.BoundType) || ReferenceEquals(source.BoundType, BoundType.String) || source.BoundType?.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Tuple or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication || ScalarTypes.Supports(source.BoundType) || ReferenceTypes.IsPointer(source.BoundType)) &&
            unwrapped is IdentifierNameKoto && unwrapped.BoundSymbol?.Kind != BindingSymbolKind.PatternCandidate;
        var place = this.SpecialField(unwrapped) ? this.Local(unwrapped)
            : source is FormattingKoto hidden && !ReferenceTypes.IsBorrow(hidden.BoundType) && this.formattingPlaces.TryGetValue(hidden, out var prepared) ? prepared
            : direct ? this.Local(unwrapped) : this.Expression(source, PlaceUseKind.Read);
        if (place < 0)
        {
            return -1;
        }

        if (direct && ReferenceTypes.IsBorrow(this.body.Places[place].Type))
        {
            // SPEC 8.9: an instance's generic Place storing a reference is Reborrowed through the stored pointer it reads.
            this.Emit(OwnershipOperationKind.Read, source, place);
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
    private int Receiver(Koto root, bool exclusive = false)
    {
        if (this.compilation.Binding.TryGetAdaptation(root, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.SharedBorrow)
        {
            return this.BorrowStruct(root, ElementAccess.AccessType(root, exclusive)!);
        }

        // SPEC 13.5.5.1, 15.6.2: for a shared access the reference of an explicitly selected referent (`p@follow.x`) lends its
        // referent as the adapted receiver of `p.x` does; an exclusive access uses the reference itself, so a write is judged
        // by its own path and disjoint parts stay separate.
        if (!exclusive && ElementAccess.IsFollowedRoot(root) && this.Concrete(root.BoundType) is { Components.Count: 1 } reference)
        {
            return this.BorrowStruct(root, this.compilation.Binding.Reference(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, reference.Components[0], reference.Origin));
        }

        return this.Expression(root, PlaceUseKind.Read);
    }

    private int ReadBorrowedField(MemberAccessKoto field)
    {
        var receiver = this.Receiver(ElementAccess.BorrowedPathRoot(field)!);
        if (receiver < 0)
        {
            return -1;
        }

        if (this.Concrete(field.BoundType) is not { } type)
        {
            this.Unsupported(field);
            return -1;
        }

        if (this.compilation.Binding.ProveCopy(type, field) != ConstraintProof.Proven)
        {
            // SPEC 3.5: a Place is never moved by bare acquisition; a non-Copy field needs an explicit borrow or @move, which
            // the shared path refutes. The read is still modeled, so later uses are checked and the result is delivered.
            this.body.ReportIssue(new(field, OwnershipFailure.TransferRequired));
        }
        else if (!this.SupportsCopySnapshot(type, field))
        {
            this.Unsupported(field);
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

        // SPEC 13.7: secure the RHS, then replace the field through its exclusive address, borrowed through the receiver,
        // like a referent: the old value is destroyed by its Type's plan and the new one moves in.
        var root = ElementAccess.BorrowedPathRoot(field)!;
        if (ElementAccess.AccessType(root, true) is not { Semantics: SemanticsKind.Uniq } receiverType || field.BoundType is not { } stored)
        {
            this.Unsupported(assignment);
            return -1;
        }

        var input = this.Expression(assignment.Right);
        var address = input < 0 ? -1 : this.BorrowStruct(field, this.compilation.Binding.Reference(SemanticsKind.Uniq, stored, receiverType.Origin));
        if (address < 0)
        {
            return -1;
        }

        this.StorePointer(field, this.Value(address), input);
        return this.Temporary(assignment);
    }

    // A generic integer or generic wrapping integer in a definition body (SPEC 8.4.7.3): numeric in every instance. Universal
    // verification accepts it, and each instance plan sees the concrete Scalar.
    private bool GenericInteger(Koto source)
        => this.instance is null && source.BoundType is { } type &&
        ((type.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection && this.compilation.Binding.ProvePrimitiveInteger(type, source) == ConstraintProof.Proven) ||
            this.compilation.Binding.IsGenericWrappingInteger(type, source));

    // SPEC 13.7: every compound update computes `target op right` through the same plan, which exists for a numeric Scalar target,
    // a generic integer, and a raw pointer displaced by + or - (SPEC 5.3) where the target may hold one. Any other target, such
    // as a string, is outside the implemented subset on every path that reaches it.
    private bool SupportsUpdate(Koto target, BoundType? type, KotoKind operation, bool pointer = false)
        => operation != KotoKind.Invalid && this.Concrete(type) is { } concrete &&
            (concrete.IsNumeric || this.GenericInteger(target) || (pointer && ReferenceTypes.IsPointer(concrete) && operation is KotoKind.Plus or KotoKind.Minus));

    private int UpdateBorrowedField(Koto source, MemberAccessKoto field)
    {
        var operation = ElementAccess.UpdateOperator(source.Akind);
        var root = ElementAccess.BorrowedPathRoot(field)!;
        var receiverType = ElementAccess.AccessType(root, true);
        if (receiverType?.Semantics != SemanticsKind.Uniq || !this.SupportsUpdate(field, field.BoundType, operation))
        {
            this.Unsupported(source);
            return -1;
        }

        // SPEC 13.7.2: secure the RHS, then the receiver and old value. The receiver is read like that of a simple
        // write; nothing runs between reading the old value and storing the new one.
        var right = source is BinaryKoto binary ? this.Value(this.Expression(binary.Right)) : 0;
        var receiver = right < 0 ? -1 : this.Receiver(root, true);
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
