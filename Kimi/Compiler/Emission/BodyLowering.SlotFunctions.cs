// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] slotFunctionPlaces = [];
    private int[] slotFunctionProduces = [];
    private int[] slotFunctionInitializations = [];
    private int[] slotCallProduces = [];

    // Roles belong to verified parameter, return and call plans, never to Place Kind alone.
    private bool PrepareSlotFunctions(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        Grow(ref this.slotFunctionPlaces, body.Places.Count);
        Grow(ref this.slotFunctionProduces, body.Operations.Count);
        Grow(ref this.slotFunctionInitializations, body.Places.Count);
        Grow(ref this.slotCallProduces, body.Operations.Count);
        this.slotFunctionPlaces.AsSpan(0, body.Places.Count).Clear();
        this.slotFunctionProduces.AsSpan(0, body.Operations.Count).Clear();
        this.slotFunctionInitializations.AsSpan(0, body.Places.Count).Fill(-1);
        this.slotCallProduces.AsSpan(0, body.Operations.Count).Fill(-1);
        for (var p = 0; p < body.Places.Count; p++)
        {
            function.SlotAddresses.Add(new(EmissionOperandKind.SlotAddress, p));
        }

        // Parameter Produce operations form the entry prefix, after reserved Exit markers.
        var producer = 1;
        while (producer < body.Operations.Count && body.Operations[producer].Kind == OwnershipOperationKind.Exit)
        {
            producer++;
        }

        for (var i = 0; i < body.Function.Parameters.Count; i++, producer++)
        {
            var parameter = body.Function.Parameters[i];
            if ((uint)producer >= (uint)body.Operations.Count || body.Operations[producer] is not { Kind: OwnershipOperationKind.Produce } operation ||
                (uint)operation.Place >= (uint)body.Places.Count)
            {
                return Fail("Missing parameter entry initialization.", out failure);
            }

            var place = body.Places[operation.Place];
            if (place.Kind != OwnershipPlaceKind.Parameter || !ReferenceEquals(place.Source, parameter.Type) ||
                !ReferenceEquals(operation.Source, parameter.Type) || !ReferenceEquals(place.Type, parameter.Type.BoundType))
            {
                return Fail("Parameter storage does not match its logical signature.", out failure);
            }

            if (FunctionAbi.GetValue(place.Type, this.aggregateLayouts)?.Layout.Size > 0 && !ReferenceTypes.IsString(place.Type))
            {
                function.SlotAddresses[place.Id] = new(EmissionOperandKind.Argument, i);
            }

            if (SlotTypes.IsResult(place.Type))
            {
                this.slotFunctionPlaces[place.Id] = 1;
                this.slotFunctionProduces[producer] = 1;
                this.slotFunctionInitializations[place.Id] = producer;
            }
        }

        var returns = 0;
        for (var p = 0; p < body.Places.Count; p++)
        {
            var place = body.Places[p];
            if (place.Kind == OwnershipPlaceKind.Result && ReferenceEquals(place.Source, body.Function) && SlotTypes.IsResult(place.Type))
            {
                if (function.Abi.ResultSlot != FunctionAbi.HasResultSlot(place.Type, this.aggregateLayouts) || ++returns != 1)
                {
                    return Fail("Stored return storage has no unique ABI result slot.", out failure);
                }

                this.slotFunctionPlaces[p] = 2;
                if (function.Abi.ResultSlot)
                {
                    function.SlotAddresses[p] = new(EmissionOperandKind.ReturnAddress, 0);
                }
            }
        }

        if (SlotTypes.IsResult(body.Function.BoundSymbol?.Type) != (returns == 1))
        {
            return Fail("Missing stored return value.", out failure);
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            var call = body.Operations[id];
            if (call.Kind != OwnershipOperationKind.Call || !SlotTypes.IsResult(call.Source.BoundType))
            {
                continue;
            }

            if (call.Place == -1 && !body.IsReachable(id) && call.Source is InvocationKoto invocation && this.CannotCompleteCall(invocation))
            {
                continue; // No acquired argument set, so no result storage was initialized.
            }

            if (call.Source is not InvocationKoto || (uint)call.Place >= (uint)body.Places.Count || id + 1 >= body.Operations.Count ||
                body.Operations[id + 1] is not { Kind: OwnershipOperationKind.Produce } produce || produce.Place != call.Place ||
                !ReferenceEquals(produce.Source, call.Source) || this.slotFunctionPlaces[call.Place] != 0)
            {
                return Fail("Stored call has no unique normal result initialization.", out failure);
            }

            var place = body.Places[call.Place];
            if (place.Kind != OwnershipPlaceKind.Temporary || !ReferenceEquals(place.Source, call.Source) || !ReferenceEquals(place.Type, call.Source.BoundType))
            {
                return Fail("Stored call result must have its own temporary storage.", out failure);
            }

            this.slotFunctionPlaces[call.Place] = 3;
            this.slotFunctionProduces[id + 1] = 3;
            this.slotFunctionInitializations[call.Place] = id + 1;
            this.slotCallProduces[id] = id + 1;
        }

        return true;
    }

    private bool IsSlotValue(OwnershipPlace place) => ReferenceEquals(place.Type, BoundType.String)
        ? this.IsStringValue(place)
        : this.aggregatePlaces[place.Id] is not null &&
            (place.Kind == OwnershipPlaceKind.Temporary || (place.Kind == OwnershipPlaceKind.Result && this.slotResultPlaces[place.Id] != 0));

    private bool ValidateSlotCallResult(OwnershipBody body, int id, out string? failure)
    {
        failure = null;
        var call = body.Operations[id];
        var produce = this.slotCallProduces[id];
        if (produce < 0 || (body.GetInputState(id, call.Place) & PlaceState.MayInit) != 0)
        {
            return Fail("Stored call result storage is already live or lacks initialization.", out failure);
        }

        // Only normal return enters Produce. Neither Abort nor any other predecessor may initialize it.
        var normal = 0;
        for (var edgeId = body.EdgeHeads[id]; edgeId >= 0; edgeId = body.Edges[edgeId].Next)
        {
            var edge = body.Edges[edgeId];
            if (edge.Kind != OwnershipEdgeKind.Abort)
            {
                if (edge.To != produce || ++normal != 1)
                {
                    return Fail("Stored result initialization is not the call's normal continuation.", out failure);
                }
            }
        }

        if (body.IsReachable(id) && (normal != 1 || !this.Dominates(id, produce) || this.LogicalIncoming(produce) != 1))
        {
            return Fail("Stored result initialization has an invalid predecessor.", out failure);
        }

        return true;
    }
}
