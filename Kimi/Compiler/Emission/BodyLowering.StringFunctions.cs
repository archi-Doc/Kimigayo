// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] stringFunctionPlaces = [];
    private int[] stringFunctionProduces = [];
    private int[] stringCallProduces = [];

    // Roles belong to verified parameter, return and call plans, never to Place Kind alone.
    private bool PrepareStringFunctions(OwnershipBody body, EmissionFunction function, out string? failure)
    {
        failure = null;
        Grow(ref this.stringFunctionPlaces, body.Places.Count);
        Grow(ref this.stringFunctionProduces, body.Operations.Count);
        Grow(ref this.stringCallProduces, body.Operations.Count);
        this.stringFunctionPlaces.AsSpan(0, body.Places.Count).Clear();
        this.stringFunctionProduces.AsSpan(0, body.Operations.Count).Clear();
        this.stringCallProduces.AsSpan(0, body.Operations.Count).Fill(-1);
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

            if (!ReferenceEquals(place.Type, BoundType.Unit))
            {
                function.SlotAddresses[place.Id] = new(EmissionOperandKind.Argument, i);
            }

            if (ReferenceEquals(place.Type, BoundType.String))
            {
                this.stringFunctionPlaces[place.Id] = 1;
                this.stringFunctionProduces[producer] = 1;
            }
        }

        var returns = 0;
        for (var p = 0; p < body.Places.Count; p++)
        {
            var place = body.Places[p];
            if (place.Kind == OwnershipPlaceKind.Result && ReferenceEquals(place.Source, body.Function) && ReferenceEquals(place.Type, BoundType.String))
            {
                if (!function.Abi.ResultSlot || ++returns != 1)
                {
                    return Fail("String return storage has no unique ABI result slot.", out failure);
                }

                this.stringFunctionPlaces[p] = 2;
                function.SlotAddresses[p] = new(EmissionOperandKind.ReturnAddress, 0);
            }
        }

        if (function.Abi.ResultSlot != (returns == 1))
        {
            return Fail("Missing string return storage.", out failure);
        }

        for (var id = 0; id < body.Operations.Count; id++)
        {
            var call = body.Operations[id];
            if (call.Kind != OwnershipOperationKind.Call || !ReferenceEquals(call.Source.BoundType, BoundType.String))
            {
                continue;
            }

            if (call.Source is not InvocationKoto || (uint)call.Place >= (uint)body.Places.Count || id + 1 >= body.Operations.Count ||
                body.Operations[id + 1] is not { Kind: OwnershipOperationKind.Produce } produce || produce.Place != call.Place ||
                !ReferenceEquals(produce.Source, call.Source) || this.stringFunctionPlaces[call.Place] != 0)
            {
                return Fail("String call has no unique normal result initialization.", out failure);
            }

            var place = body.Places[call.Place];
            if (place.Kind != OwnershipPlaceKind.Temporary || !ReferenceEquals(place.Source, call.Source) || !ReferenceEquals(place.Type, BoundType.String))
            {
                return Fail("String call result must have its own temporary storage.", out failure);
            }

            this.stringFunctionPlaces[call.Place] = 3;
            this.stringFunctionProduces[id + 1] = 3;
            this.stringCallProduces[id] = id + 1;
        }

        return true;
    }

    private bool ValidateStringCallResult(OwnershipBody body, int id, out string? failure)
    {
        failure = null;
        var call = body.Operations[id];
        var produce = this.stringCallProduces[id];
        if (produce < 0 || (body.GetInputState(id, call.Place) & PlaceState.MayInit) != 0)
        {
            return Fail("String call result storage is already live or lacks initialization.", out failure);
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
                    return Fail("String result initialization is not the call's normal continuation.", out failure);
                }
            }
        }

        if (body.IsReachable(id) && (normal != 1 || !this.Dominates(id, produce) || this.incoming[produce] != 1))
        {
            return Fail("String result initialization has an invalid predecessor.", out failure);
        }

        return true;
    }
}
