// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
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
        if ((place.Kind != OwnershipPlaceKind.Temporary && (!store || place.Kind != OwnershipPlaceKind.Result)) ||
            place.Acquisition is not (AcquisitionKind.Copy or AcquisitionKind.Move) ||
            !ReferenceEquals(operation.Source.BoundType, type) ||
            ValueType(body, address) is not { } pointerType || !ReferenceTypes.IsPointer(pointerType) ||
            !ReferenceEquals(pointerType.Components[0], type) || (body.IsReachable(id) && !this.Dominates(address, id)))
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

        var layout = this.aggregatePlaces[place.Id];
        var text = ReferenceEquals(type, BoundType.String);
        var representation = layout?.Value ?? WindowsLowering.GetValue(type);
        if (representation is null ||
            (layout is null && !text && !ScalarTypes.Supports(type) && !ReferenceTypes.IsPointer(type) && !ReferenceEquals(type, BoundType.Unit)))
        {
            return Fail("Pointer access requires a supported Owned representation.", out failure);
        }

        if (store)
        {
            if (text || layout is { NeedsDestruction: true })
            {
                if (!this.TryGetLocation(operation.Source, directory, constants, out var location))
                {
                    return Fail("Pointer replacement requires a destruction location.", out failure);
                }

                // Destroy at the original address, including zero-sized logical values.
                AddOwnedDestruction(function, id, this.PhysicalOperand(body, address), location, layout);
            }
        }

        this.AddStringFlags(function, operation, id);

        if (text)
        {
            // Reuse field-wise string Moves; do not read its padding or invent a
            // second handle owner. ElementAddress carries the verified raw address.
            function.AddScalar(EmissionOpcode.ElementAddress, id, [this.PhysicalOperand(body, address), new(EmissionOperandKind.Integer, 0)], representation: representation);
            if (store)
            {
                function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.SlotAddress, source), new(EmissionOperandKind.ElementAddress, id)]);
            }
            else
            {
                function.AddScalar(EmissionOpcode.MoveString, id, [new(EmissionOperandKind.ElementAddress, id)], place: place.Id);
            }

            return true;
        }

        // Zero-sized values retain operand evaluation and logical acquisition but access no bytes.
        if (representation.Layout.Size == 0)
        {
            return true;
        }

        if (layout is not null)
        {
            if (store)
            {
                Transfer(function, id, layout, [new(EmissionOperandKind.SlotAddress, source), this.PhysicalOperand(body, address)]);
            }
            else
            {
                Transfer(function, id, layout, [this.PhysicalOperand(body, address)], place.Id);
            }

            return true;
        }

        // Caller-provided storage meets the pointee alignment; add no provenance or alias attributes.
        if (store)
        {
            function.AddScalar(EmissionOpcode.StorePointer, id, [this.PhysicalOperand(body, input), this.PhysicalOperand(body, address)], representation.ComputationType, representation: representation);
        }
        else
        {
            function.AddScalar(EmissionOpcode.LoadPointer, id, [this.PhysicalOperand(body, address)], representation.ComputationType, representation: representation);
        }

        return true;
    }
}
