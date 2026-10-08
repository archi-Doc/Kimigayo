// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    // SPEC 13.7: the one replacement at an address, for an element, a field, a referent or a Place call result. The old
    // value is destroyed by its Type's destruction plan (a string, an aggregate that needs destruction, nothing for a
    // scalar), unless the caller destroyed its remaining parts, and the input's responsibility moves in. Zero-sized values
    // access no bytes. Returns false for a scalar, which the caller stores in its own addressing form.
    private static bool AddReplacement(EmissionFunction function, int id, EmissionOperand destination, int input, ValueLowering representation, AggregateLayout? layout, bool text, int location, bool destroy)
    {
        if (destroy && (text || layout is { NeedsDestruction: true }))
        {
            AddOwnedDestruction(function, id, destination, location, layout); // Including zero-sized logical values.
        }

        if (representation.Layout.Size == 0)
        {
            return true;
        }

        if (layout is not null)
        {
            Transfer(function, id, layout, [new(EmissionOperandKind.SlotAddress, input), destination]);
            return true;
        }

        if (text)
        {
            function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.SlotAddress, input), destination]);
            return true;
        }

        return false;
    }

    private bool LowerPointerProjection(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        // SPEC 5.2, 12: displace the containing raw address to one stored part. A computed array
        // index is bounds-checked like ordinary fixed-array access (SPEC 4); the result keeps the
        // container's provenance, no bytes are accessed and no inbounds is claimed.
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        var address = Input(body, id, 0);
        var index = value.Count == 2 ? Input(body, id, 1) : -1;
        var position = index < 0 ? value.Constant : -1;
        if (operation.Kind != OwnershipOperationKind.Produce || operation.Source is not Parsing.BinaryKoto element ||
            ValueType(body, id) is not { } type || !ReferenceTypes.IsPointer(type) ||
            ValueType(body, address) is not { } containerType || !ReferenceTypes.IsPointer(containerType) ||
            !ReferenceEquals(body.Resolve(element.Left.BoundType, body.ContextAt(id)), containerType.Components[0]) ||
            !ElementAccess.TryType(element, out var part, out _) || !ReferenceEquals(body.Resolve(part, body.ContextAt(id)), type.Components[0]) ||
            this.aggregateLayouts.Get(containerType.Components[0]) is not { } layout ||
            (index < 0 ? ElementAccess.PathSelector(element, out _, out _) != position || position < 0 || position >= layout.StorageCount
                : value.Constant != -1 || !layout.IsArray || element is not Parsing.IndexKoto || !ReferenceEquals(ValueType(body, index), BoundType.ISize)) ||
            (body.IsReachable(id) && (!this.Dominates(address, id) || (index >= 0 && !this.Dominates(index, id)))))
        {
            return Fail("Pointer projection requires a dominating container address and a matching stored part.", out failure);
        }

        var location = -1;
        var stride = index < 0 ? 0 : FunctionAbi.GetValue(part!, this.aggregateLayouts)?.Layout.Stride ?? -1;
        if (index >= 0 && (stride < 0 || !this.TryGetLocation(element, directory, constants, out location)))
        {
            return Fail("Pointer element bounds check requires an element stride and a source location.", out failure);
        }

        // The pointer representation keeps the address even for zero-sized parts.
        function.AddScalar(
            EmissionOpcode.ElementAddress,
            id,
            [this.PhysicalOperand(body, address), index < 0 ? new(EmissionOperandKind.Integer, layout.StorageOffset((int)position)) : this.PhysicalOperand(body, index),
                new(EmissionOperandKind.Integer, layout.Count), new(EmissionOperandKind.Integer, stride)],
            place: body.Operations.Count + id,
            location: location,
            check: index < 0 ? ArithmeticCheckKind.None : ArithmeticCheckKind.Bounds,
            representation: WindowsLowering.GetValue(type));
        return true;
    }

    private bool LowerPointer(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var store = body.Values[id].Kind == OwnershipValueKind.PointerStore;
        if (operation.Kind != (store ? OwnershipOperationKind.StorePointer : OwnershipOperationKind.Produce) || (uint)operation.Place >= (uint)body.Places.Count)
        {
            return Fail("Pointer access requires a matching acquisition or transfer plan.", out failure);
        }

        var place = body.Places[operation.Place];
        var type = place.Type;
        var address = Input(body, id, 0);
        // SPEC 3.3: a Copy borrow's referent is loaded through the reference; the
        // operation's source is then the borrow itself, and its address needs no Unsafe obligation.
        // SPEC 7.1.1: a write through a Place call stores through the reference the call returns.
        var pointerType = ValueType(body, address);
        // SPEC 13.7: a field replaced through a reference is stored through the exclusive address of that field.
        var sourceType = store && ((operation.Source is BinaryKoto inline && ElementAccess.IsSyntax(inline) && ElementAccess.WritableRoot(inline) is not null) ||
            (operation.Source is MemberAccessKoto replaced && ElementAccess.BorrowedPathRoot(replaced) is not null) ||
            ElementAccess.IsExclusiveArrayElement(operation.Source)) &&
            ReferenceEquals(body.Resolve(operation.Source.BoundType, body.ContextAt(id)), type) ? pointerType
            : body.Resolve(ElementAccess.PlaceCallReference(operation.Source) ?? operation.Source.BoundType, body.ContextAt(id));
        // SPEC 3.5.3, 13.5.5.1: a load through a safe reference copies its referent layer by layer, and a
        // referent write stores through a uniq reference; both use the reference value as the address.
        // SPEC 10.2, 3.4.1: a stored ref or uniq reference may be loaded as one reference to the same referent, shared,
        // or exclusive when the stored reference is exclusive; ownership has checked the combined Origin and capability.
        var sharedRead = !store && pointerType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            pointerType.Components[0] is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } storedReference &&
            type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            (type.Semantics == SemanticsKind.Ref || (storedReference.Semantics == SemanticsKind.Uniq && pointerType.Semantics == SemanticsKind.Uniq)) &&
            ReferenceEquals(storedReference.Components[0], type.Components[0]); // SPEC 15.6.2: a shared layer grants no exclusive load.
        // Origins were checked before emission; a replacement may shorten dependencies without changing storage.
        var sameStorage = pointerType is { Components: [var storedType] } && (store ? ReferenceTypes.StorageMatches(storedType, type) : ReferenceEquals(storedType, type));
        var referent = pointerType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            (sameStorage || sharedRead) &&
            (store ? pointerType.Semantics == SemanticsKind.Uniq && ReferenceEquals(sourceType, pointerType) : place.Acquisition == AcquisitionKind.Copy);
        if ((place.Kind != OwnershipPlaceKind.Temporary && (!store || place.Kind != OwnershipPlaceKind.Result)) ||
            place.Acquisition is not (AcquisitionKind.Copy or AcquisitionKind.Move) ||
            (!referent && !(store ? ReferenceTypes.StorageMatches(sourceType, type) : ReferenceEquals(sourceType, type))) ||
            pointerType is null || !(referent || ReferenceTypes.IsPointer(pointerType)) ||
            (!sameStorage && !sharedRead) || (body.IsReachable(id) && !this.Dominates(address, id)))
        {
            return Fail("Pointer access requires a matching pointee and a dominating address.", out failure);
        }

        var input = store && IsScalar(type) ? Input(body, id, 1) : -1;
        var source = store && body.Values[id].Constant >= 0 && body.Values[id].Constant < body.Places.Count ? (int)body.Values[id].Constant : -1;
        if (store && (source != place.Id || operation.Acquisition != place.Acquisition ||
            body.Places[source].Kind is not (OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ||
            (body.IsReachable(id) && (body.GetInputState(id, source) & PlaceState.MustInit) == 0) ||
            (IsScalar(type) && (!ReferenceEquals(ValueType(body, input), type) || (body.IsReachable(id) && !this.Dominates(input, id))))))
        {
            return Fail("Pointer write requires a dominating acquired value of the pointee Type.", out failure);
        }

        var layout = store ? this.aggregateLayouts.GetStored(type) : this.aggregatePlaces[place.Id];
        var text = ReferenceEquals(type, BoundType.String);
        var representation = layout?.Value ?? WindowsLowering.GetValue(type);
        if (representation is null ||
            (layout is null && !text && !ScalarTypes.Supports(type) && !ReferenceTypes.IsPointer(type) && !ReferenceTypes.IsBorrow(type) && !ReferenceEquals(type, BoundType.Unit)))
        {
            return Fail("Pointer access requires a supported complete pointee representation.", out failure);
        }

        if (store)
        {
            var location = -1;
            if ((text || layout is { NeedsDestruction: true }) && !this.TryGetLocation(operation.Source, directory, constants, out location))
            {
                return Fail("Pointer replacement requires a destruction location.", out failure);
            }

            var destination = this.PhysicalOperand(body, address);
            if (text)
            {
                // Field-wise string Moves take the verified raw address as an element address; no padding is read.
                function.AddScalar(EmissionOpcode.ElementAddress, id, [destination, new(EmissionOperandKind.Integer, 0)], representation: representation);
                destination = new(EmissionOperandKind.ElementAddress, id);
            }

            // Caller-provided storage meets the pointee alignment; add no provenance or alias attributes.
            if (!AddReplacement(function, id, destination, source, representation, layout, text, location, destroy: true))
            {
                function.AddScalar(EmissionOpcode.StorePointer, id, [this.PhysicalOperand(body, input), this.PhysicalOperand(body, address)], representation.ComputationType, representation: representation);
            }

            this.AddStringFlags(function, operation, id);
            return true;
        }

        this.AddStringFlags(function, operation, id);
        if (text)
        {
            // Reuse field-wise string Moves; do not read its padding or invent a
            // second handle owner. ElementAddress carries the verified raw address.
            function.AddScalar(EmissionOpcode.ElementAddress, id, [this.PhysicalOperand(body, address), new(EmissionOperandKind.Integer, 0)], representation: representation);
            function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.ElementAddress, id)], place: place.Id);
            return true;
        }

        // Zero-sized values retain operand evaluation and logical acquisition but access no bytes.
        if (representation.Layout.Size == 0)
        {
            return true;
        }

        if (layout is not null)
        {
            Transfer(function, id, layout, [this.PhysicalOperand(body, address)], place.Id);
            return true;
        }

        function.AddScalar(EmissionOpcode.LoadPointer, id, [this.PhysicalOperand(body, address)], representation.ComputationType, representation: representation);
        return true;
    }
}
