// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private bool SupportsPointerValue(Koto source, bool abstractRead = false)
    {
        var type = this.Concrete(source.BoundType);
        // SPEC 5.2, 21.3.1: a generic read acquires CopyOrMove without inventing a Loan.
        // The closed ownership plan must still prove a supported pointee representation.
        if (abstractRead && this.instance is null && type?.Kind == BoundTypeKind.Parameter)
        {
            return true;
        }

        // Acquisition preserves the complete pointee Type, including its internal Origins. Raw access supplies no
        // new Loan or lifetime; initialized storage and valid Copy/Move permission remain the unsafe caller's obligations.
        return ScalarTypes.Supports(type) || ReferenceTypes.IsPointer(type) || ReferenceTypes.IsBorrow(type) ||
            (type is not null && ObjectTypes.HandleMode(type) is not null && this.SupportsType(type)) ||
            ReferenceEquals(type, BoundType.Unit) || ReferenceEquals(type, BoundType.String) ||
            (type is not null && (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Function or BoundTypeKind.FunctionItem || StructStorage.IsStruct(type) || EnumStorage.IsEnum(type)));
    }

    private int PointerAddress(Koto source)
    {
        if (source is DereferenceKoto dereference)
        {
            return this.Value(this.Expression(dereference.Operand, PlaceUseKind.Read));
        }

        if (source is not IndexKoto index || !ReferenceTypes.IsPointer(index.Left.BoundType))
        {
            return this.ProjectPointer((BinaryKoto)source);
        }

        // SPEC 5.3: form p + n once without evaluating a synthetic syntax tree.
        var pointer = this.Value(this.Expression(index.Left, PlaceUseKind.Read));
        var offset = this.Value(this.Expression(index.Right, PlaceUseKind.Read));
        return pointer >= 0 && offset >= 0 && this.flow!.Nodes[source].CanCompleteNormally
            ? this.Value(this.ComputeUpdate(source, index.Left.BoundType, pointer, offset, KotoKind.Plus)) : -1;
    }

    private int ProjectPointer(BinaryKoto element)
    {
        // The containing Place's address, displaced to one inline stored part. Nothing is read,
        // so the rest of the pointee need not be initialized and no Loan is created.
        if (!ElementAccess.TryType(element, out var type, out var position) || !ReferenceEquals(type, element.BoundType))
        {
            this.Unsupported(element);
            return -1;
        }

        // A computed array index is evaluated after the container address and bounds-checked
        // against the fixed length during lowering; a literal in-range index is a static offset.
        var pointer = this.PointerAddress(KotoHelper.UnwrapParentheses(element.Left));
        var selector = element is IndexKoto ? ElementAccess.StaticSelector(element) : position;
        var index = pointer >= 0 && selector < 0 ? this.Value(this.PositionPlace(element.Right, this.Expression(element.Right, PlaceUseKind.Read))) : -1;
        if (pointer < 0 || (selector < 0 && (index < 0 || !this.flow!.Nodes[element].CanCompleteNormally)))
        {
            return -1;
        }

        var projected = this.Place(element, this.compilation.Binding.PointerType(type!), OwnershipPlaceKind.Temporary, true);
        this.Emit(OwnershipOperationKind.Produce, element, projected);
        this.RegisterTemporary(projected);
        this.SetValue(this.Value(projected), OwnershipValueKind.PointerProject, selector >= 0 ? [pointer] : [pointer, index], constant: selector);
        return this.Value(projected);
    }

    private int ReadPointer(Koto source, PlaceUseKind use, AcquisitionKind? acquisition)
    {
        // A non-consuming access needs a raw Place/borrow plan, not a Move to a
        // disposable owner. In particular, string comparisons must not consume *p.
        var copy = this.compilation.Binding.ProveCopy(source.BoundType!, source) == ConstraintProof.Proven;
        if (!this.SupportsPointerValue(source, abstractRead: true) || (use != PlaceUseKind.Consume && !copy))
        {
            this.Unsupported(source);
            return -1;
        }

        if (use == PlaceUseKind.Consume && acquisition is null && !copy)
        {
            // SPEC 3.5, 5.2.3: a raw Place, like every Place, never Moves by bare acquisition; (*p)@move takes its value.
            this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
        }

        var pointer = this.PointerAddress(source);
        return pointer < 0 ? -1 : this.LoadPointer(source, pointer);
    }

    // SPEC 3.5.3: a Scalar read follows every safe reference layer to its terminal Scalar and copies it.
    private int LoadReferent(Koto source)
    {
        var layers = 0;
        for (var type = this.Concrete(source.BoundType); type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }; type = this.Concrete(type.Components[0]))
        {
            layers++;
        }

        if (this.compilation.Binding.ImplicitPairAdmitted(source) != SemanticsMask.None && this.compilation.Binding.TryGetAdaptation(source, out var read))
        {
            // SPEC 3.5.3, 13.5.5.1: a Scalar read through a pair layer. Owner reads the operand itself; ref and uniq load
            // through the stored reference; the universal verification reads through a shared borrow of the operand.
            var operand = KotoHelper.UnwrapParentheses(source);
            if (layers == 0)
            {
                return this.instance is null ? this.CopyThroughPair(source, operand, read.Type) : this.ExpressionCore(operand, PlaceUseKind.Consume, null);
            }

            if (this.instance is not null && operand is not IdentifierNameKoto)
            {
                var stored = this.StoredReference(operand, SemanticsKind.Ref);
                return stored < 0 ? -1 : this.LoadThrough(source, stored, layers);
            }
        }

        return this.LoadReferent(source, layers);
    }

    // SPEC 3.5.3, 13.5.5.1: the reference expression is read, and the referent is loaded through it (a valid
    // address by the reference's Origin, no new Loan) into a fresh Copy temporary, once per layer. The referent
    // stays initialized. The acquired Type retains nested Origins; the outer reference Origin is not attached
    // to an independent snapshot.
    private int LoadReferent(Koto source, int layers)
    {
        var reference = this.StoredReference(KotoHelper.UnwrapParentheses(source), SemanticsKind.Ref);
        return reference < 0 ? -1 : this.LoadThrough(source, reference, layers);
    }

    // Loads the referent through an already evaluated reference, so that an update reads and writes the one Place
    // its target expression designates (SPEC 13.7.2).
    private int LoadThrough(Koto source, int reference, int layers, BoundType? referenceType = null)
    {
        if (layers <= 0)
        {
            this.Unsupported(source);
            return -1;
        }

        var loaded = -1;
        for (var type = referenceType ?? this.Concrete(source.BoundType); layers > 0; layers--)
        {
            // An inner layer is read only for its address; the terminal referent is a Copy snapshot.
            if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ||
                (!(layers > 1 && type.Components[0] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }) &&
                    !this.SupportsCopySnapshot(type.Components[0], source)))
            {
                this.Unsupported(source);
                return -1;
            }

            var pointer = loaded < 0 ? reference : loaded;
            loaded = this.Place(source, type.Components[0], OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, source, loaded);
            this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [this.Value(pointer)]);
            this.RegisterTemporary(loaded);
            type = this.Concrete(type.Components[0]);
        }

        return loaded;
    }

    // SPEC 10.2: one shared reference through several reference layers. The outer layers are read only for their
    // addresses, and the reference stored in the last layer is loaded as the shared reference; the Loans follow the
    // Origin of that Type, so a Copied inner reference no longer depends on the layers above it.
    private int ReadReference(Koto source, BoundType result)
    {
        var reference = this.StoredReference(KotoHelper.UnwrapParentheses(source), result.Semantics == SemanticsKind.Uniq ? SemanticsKind.Uniq : SemanticsKind.Ref);
        if (reference < 0)
        {
            return -1;
        }

        result = this.Concrete(result)!;
        if (this.compilation.Binding.ImplicitPairAdmitted(source) != SemanticsMask.None &&
            (this.instance is null || this.ReferenceLayers(source.BoundType, result.Components[0]) == 1))
        {
            // SPEC 13.5.5.1, 10.2: the inner reference below a pair layer is Copied from the Place itself in the universal
            // verification and an owner instance; a ref or uniq instance loads it through the stored reference below.
            var copy = this.Place(source, result, OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
            var produced = this.Emit(OwnershipOperationKind.Produce, source, copy);
            this.SetValue(produced, OwnershipValueKind.Alias, [this.Value(reference)]);
            return this.RegisterTemporary(copy);
        }

        var loaded = reference;
        for (var type = this.Concrete(source.BoundType); ; type = this.Concrete(type.Components[0]))
        {
            if (type is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ||
                this.Concrete(type.Components[0]) is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef, Components.Count: 1 } stored)
            {
                this.Unsupported(source);
                return -1;
            }

            var last = ReferenceEquals(this.Concrete(stored.Components[0]), result.Components[0]);
            var pointer = loaded;
            loaded = this.Place(source, last ? result : stored, OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, source, loaded);
            this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [this.Value(pointer)]);
            this.RegisterTemporary(loaded);
            if (last)
            {
                return loaded;
            }
        }
    }

    private bool SupportsCopySnapshot(BoundType type, Koto source)
        => this.compilation.Binding.ProveCopy(type, source) == ConstraintProof.Proven &&
        (ReferenceTypes.IsValue(type) || ReferenceEquals(type, BoundType.Unit) ||
            type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.FunctionItem or BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication || StructStorage.IsStruct(type) || EnumStorage.IsEnum(type));

    // SPEC 5.2.2: a borrow of a raw Place converts its address. The referent is a fresh anchor that no Loan of another Place
    // covers, so accesses through the result, and Reborrows from it, are checked under the result alone.
    private int BorrowRawPlace(Koto source, Koto place, BoundType type)
    {
        var pointer = this.PointerAddress(place);
        if (pointer < 0)
        {
            return -1;
        }

        var reference = this.Place(source, type, OwnershipPlaceKind.Temporary, false);
        this.Emit(OwnershipOperationKind.Produce, source, reference);
        this.SetValue(this.Value(reference), OwnershipValueKind.Convert, [pointer], constant: OwnershipValue.RawPlaceBorrow);
        if (type.Origin is { Kind: OriginKind.Anchor, Binder: { } borrow })
        {
            this.Anchor(borrow, this.body.Places[reference].Type);
        }

        return this.RegisterTemporary(reference);
    }

    // The anchor Place that the borrow's Anchor Origin names. It has the borrow's Type, so it is represented like the borrow and
    // depends on the referent's own Origins; it holds no value, needs no storage or cleanup, and no operation uses it.
    private void Anchor(Koto borrow, BoundType reference)
    {
        var anchors = this.body.Anchors ??= new();
        for (var i = 0; i < anchors.Count; i++)
        {
            if (ReferenceEquals(this.body.Places[anchors[i]].Source, borrow))
            {
                return;
            }
        }

        anchors.Add(this.AddPlace(new(this.body.PlaceStorage.Count, borrow, reference, OwnershipPlaceKind.Anchor, false, AcquisitionKind.None)));
    }

    private int LoadPointer(Koto source, int pointer)
    {
        // SPEC 5.2: acquire Copy/Move into a fresh owner without a Loan. For Move,
        // the caller must prevent reads and destruction of the raw source storage.
        var loaded = this.Temporary(source);
        this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [pointer]);
        return loaded;
    }

    private int WritePointer(BinaryKoto assignment, Koto target)
    {
        if (!this.SupportsPointerValue(target) ||
            (assignment.Akind != KotoKind.Equals && !this.SupportsUpdate(target, target.BoundType, KotoHelper.CompoundOperation(assignment.Akind), pointer: true)))
        {
            this.Unsupported(assignment);
            return -1;
        }

        // SPEC 13.7: the RHS is secured before the destination is located, for simple and compound forms.
        int value;
        var right = this.Expression(assignment.Right);
        var pointer = right < 0 ? -1 : this.PointerAddress(target);
        if (assignment.Akind == KotoKind.Equals)
        {
            value = right;
        }
        else
        {
            var loaded = pointer < 0 ? -1 : this.LoadPointer(target, pointer);
            value = loaded >= 0 && right >= 0 && this.flow!.Nodes[assignment].CanCompleteNormally
                ? this.ComputeUpdate(assignment, target.BoundType, this.Value(loaded), this.Value(right), KotoHelper.CompoundOperation(assignment.Akind)) : -1;
        }

        if (pointer < 0 || value < 0)
        {
            return -1;
        }

        this.StorePointer(target, pointer, value);
        return this.Temporary(assignment);
    }

    // SPEC 7.1.1: a value use of a published Place reads the referent through the reference the call returns, like a
    // selected referent: a Copy snapshot of a proven-Copy stored Type, never a Move.
    private int ReadPlaceCall(InvocationKoto call, AcquisitionKind? acquisition)
    {
        var stored = this.Concrete(call.BoundType);
        if (acquisition == AcquisitionKind.Move || stored is null || !this.SupportsCopySnapshot(stored, call))
        {
            if (acquisition is null && call.BoundType is { } element && this.compilation.Binding.ProveCopy(element, call) != ConstraintProof.Proven)
            {
                var source = call.Parent is IndexKoto index && ReferenceEquals(this.compilation.Binding.IndexerCall(index, false), call) ? (Koto)index : call;
                this.body.ReportIssue(new(source, OwnershipFailure.TransferRequired));
                // Keep checking the call and its typed result after this rejected acquisition, as for a bare local.
                // Dropping the result would invent an uninitialized return and hide independent argument/Loan errors.
            }
            else
            {
                this.Unsupported(call);
                return -1;
            }
        }

        var reference = this.PlaceCallReference(call);
        if (reference < 0)
        {
            return -1;
        }

        var loaded = this.Place(call, stored, OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
        this.Emit(OwnershipOperationKind.Produce, call, loaded);
        this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [this.Value(reference)]);
        return this.RegisterTemporary(loaded);
    }

    // Evaluates a Place call as the reference it returns.
    private int PlaceCallReference(InvocationKoto call)
    {
        this.referenceCalls.Add(call);
        var reference = this.ExpressionCore(call, PlaceUseKind.Read, null);
        this.referenceCalls.Remove(call);
        return reference;
    }

    // SPEC 7.1.1, 13.7: an assignment or compound update through a place uniq/T result secures the right-hand side,
    // evaluates the call once as its reference, and replaces the referent through it (any stored Type for `=`, numeric for
    // a compound update).
    private int WritePlaceCall(Koto source, InvocationKoto call)
    {
        var type = call.BoundType;
        var operation = source.Akind == KotoKind.Equals ? KotoKind.Equals : ElementAccess.UpdateOperator(source.Akind);
        if (ElementAccess.PlaceCallReference(call)?.Semantics != SemanticsKind.Uniq || type is null ||
            (operation != KotoKind.Equals && !this.SupportsUpdate(call, type, operation)))
        {
            this.Unsupported(source);
            return -1;
        }

        var right = source is BinaryKoto binary ? this.Expression(binary.Right) : -1;
        if (source is BinaryKoto && right < 0)
        {
            return -1;
        }

        var address = this.PlaceCallReference(call);
        var pointer = this.Value(address);
        if (pointer < 0)
        {
            return -1;
        }

        int value;
        var previous = -1;
        if (operation == KotoKind.Equals)
        {
            value = right;
        }
        else
        {
            var loaded = this.Place(call, type, OwnershipPlaceKind.Temporary, true, AcquisitionKind.Copy);
            this.Emit(OwnershipOperationKind.Produce, call, loaded);
            this.SetValue(this.Value(loaded), OwnershipValueKind.PointerLoad, [pointer]);
            this.RegisterTemporary(loaded);
            previous = this.Value(loaded);
            var operand = source is BinaryKoto ? this.Value(right) : previous >= 0 ? this.IncrementOne(source) : -1;
            value = operand >= 0 && this.flow!.Nodes[source].CanCompleteNormally
                ? this.ComputeUpdate(source, type, previous, operand, operation) : -1;
        }

        if (value < 0)
        {
            return -1;
        }

        this.StorePointer(call, pointer, value);
        return operation == KotoKind.Equals ? this.Temporary(source) : this.UpdateResult(source, previous, value);
    }

    // SPEC 13.5.5.1, 13.7: r@follow = v and r@follow op= v write the referent of a uniq reference through it, securing the
    // RHS first, as the same replacement.
    private int WriteReferent(Koto source, ConversionKoto followed)
    {
        var reference = followed.Left;
        var type = this.Concrete(followed.BoundType);
        var operation = source.Akind == KotoKind.Equals ? KotoKind.Equals : ElementAccess.UpdateOperator(source.Akind);
        if (this.Concrete(reference.BoundType)?.Semantics != SemanticsKind.Uniq || type is null ||
            (operation != KotoKind.Equals && !this.SupportsUpdate(followed, followed.BoundType, operation)))
        {
            this.Unsupported(source);
            return -1;
        }

        var right = source is BinaryKoto binary ? this.Expression(binary.Right) : -1;
        if (source is BinaryKoto && right < 0)
        {
            return -1;
        }

        var address = this.ReadsStoredReference(followed) ? this.StoredReference(KotoHelper.UnwrapParentheses(reference), SemanticsKind.Uniq)
            : this.Expression(reference, PlaceUseKind.Read);
        var pointer = this.Value(address);
        if (pointer < 0)
        {
            return -1;
        }

        int value;
        var previous = -1;
        if (operation == KotoKind.Equals)
        {
            value = right;
        }
        else
        {
            var loaded = this.LoadThrough(reference, address, 1);
            previous = this.Value(loaded);
            var operand = source is BinaryKoto ? this.Value(right) : previous >= 0 ? this.IncrementOne(source) : -1;
            value = loaded >= 0 && operand >= 0 && this.flow!.Nodes[source].CanCompleteNormally
                ? this.ComputeUpdate(source, type, previous, operand, operation) : -1;
        }

        if (value < 0)
        {
            return -1;
        }

        this.StorePointer(reference, pointer, value);
        return operation == KotoKind.Equals ? this.Temporary(source) : this.UpdateResult(source, previous, value);
    }

    // SPEC 5.2, 7.1.1, 13.5.5.1, 13.7: the one replacement through an address, whether a raw pointer, the reference a Place
    // call publishes, a followed uniq reference or the exclusive address of a field reached through a reference. The old
    // value is destroyed by its Type's destruction plan and the acquired value's responsibility moves into the destination
    // (LowerPointer); no compiler-owned destination or temporary is created for it.
    private void StorePointer(Koto target, int pointer, int value)
    {
        var stored = this.Emit(OwnershipOperationKind.StorePointer, target, value, acquisition: this.body.Places[value].Acquisition);
        if (ScalarResult(this.body.Places[value].Type))
        {
            this.SetValue(stored, OwnershipValueKind.PointerStore, [pointer, this.Value(value)], constant: value);
        }
        else
        {
            this.SetValue(stored, OwnershipValueKind.PointerStore, [pointer], constant: value);
        }
    }

    private int UpdatePointer(UnaryKoto source, Koto target)
    {
        if (target.BoundType?.IsInteger != true)
        {
            this.Unsupported(source);
            return -1;
        }

        var pointer = this.PointerAddress(target);
        if (pointer < 0)
        {
            return -1;
        }

        var previous = this.Value(this.LoadPointer(target, pointer));
        var updated = this.ComputeUpdate(source, target.BoundType, previous, this.IncrementOne(source), ElementAccess.UpdateOperator(source.Akind));
        this.StorePointer(target, pointer, updated);
        return this.UpdateResult(source, previous, updated);
    }
}
