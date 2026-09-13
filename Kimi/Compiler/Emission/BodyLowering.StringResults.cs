// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] stringResultPlaces = [];
    private int[] stringResultDeclarations = [];
    private int[] stringResultWrites = [];
    private int[] stringResultJoins = [];
    private int[] stringArrivalSeen = [];

    private bool PrepareStringResults(OwnershipBody body, out string? failure)
    {
        failure = null;
        var count = body.Operations.Count;
        Grow(ref this.stringResultPlaces, body.Places.Count);
        Grow(ref this.stringResultDeclarations, count);
        Grow(ref this.stringResultWrites, count);
        Grow(ref this.stringResultJoins, count);
        Grow(ref this.stringArrivalSeen, body.Edges.Count);
        this.stringResultPlaces.AsSpan(0, body.Places.Count).Clear();
        this.stringResultDeclarations.AsSpan(0, count).Clear();
        this.stringResultWrites.AsSpan(0, count).Clear();
        this.stringResultJoins.AsSpan(0, count).Clear();
        this.stringArrivalSeen.AsSpan(0, body.Edges.Count).Clear();
        var arrivals = 0;
        for (var i = 0; i < body.StringResults.Count; i++)
        {
            var result = body.StringResults[i];
            if ((uint)result.Place >= (uint)body.Places.Count || (uint)result.Declare >= (uint)count || (uint)result.Join >= (uint)count ||
                result.Start != arrivals || result.Count < 0 || result.Count > body.ResultArrivals.Count - arrivals ||
                this.stringResultDeclarations[result.Declare] != 0 || this.stringResultJoins[result.Join] != 0)
            {
                return Fail("Invalid string result lifetime or arrival range.", out failure);
            }

            var place = body.Places[result.Place];
            var declaration = body.Operations[result.Declare];
            var join = body.Operations[result.Join];
            if (place.Kind != OwnershipPlaceKind.Result || !ReferenceEquals(place.Type, BoundType.String) ||
                place.Source is not (IfKoto or DoKoto or LoopKoto) || !ReferenceEquals(place.Source.BoundType, place.Type) ||
                declaration.Kind != OwnershipOperationKind.Declare || declaration.Place != result.Place || !ReferenceEquals(declaration.Source, place.Source) ||
                join.Kind != OwnershipOperationKind.Branch || join.Place != -1 || !ReferenceEquals(join.Source, place.Source) || body.Values[result.Join].Kind != OwnershipValueKind.None)
            {
                return Fail("String result is not an explicit expression lifetime.", out failure);
            }

            arrivals += result.Count;
            this.stringResultPlaces[result.Place] = 1;
            this.stringResultDeclarations[result.Declare] = i + 1;
            this.stringResultJoins[result.Join] = i + 1;
        }

        if (arrivals != body.ResultArrivals.Count)
        {
            return Fail("Unowned string result arrivals.", out failure);
        }

        foreach (var write in body.ResultWrites)
        {
            if ((uint)write.Operation >= (uint)count || (uint)write.Declare >= (uint)count || this.stringResultWrites[write.Operation] != 0 ||
                this.stringResultDeclarations[write.Declare] is not (> 0 and var plan))
            {
                return Fail("String result Write has no unique lifetime.", out failure);
            }

            var operation = body.Operations[write.Operation];
            if (operation.Kind != OwnershipOperationKind.Write || operation.Place != body.StringResults[plan - 1].Place)
            {
                return Fail("String result Write has the wrong destination.", out failure);
            }

            this.stringResultWrites[write.Operation] = plan;
        }

        return true;
    }

    private bool ValidateStringResults(OwnershipBody body, out string? failure)
    {
        failure = null;
        foreach (var write in body.ResultWrites)
        {
            if (!body.IsReachable(write.Operation))
            {
                continue;
            }

            var result = body.StringResults[this.stringResultWrites[write.Operation] - 1];
            var operation = body.Operations[write.Operation];
            if (operation.Placement != PlacementKind.Initialization || !this.Dominates(write.Declare, write.Operation) ||
                this.Dominates(result.Join, write.Operation) || (body.GetInputState(write.Operation, result.Place) & PlaceState.MayInit) != 0)
            {
                return Fail("String result Write is not fresh placement in its lifetime.", out failure);
            }
        }

        for (var i = 0; i < body.StringResults.Count; i++)
        {
            var result = body.StringResults[i];
            if (body.IsReachable(result.Declare) && (body.GetInputState(result.Declare, result.Place) & PlaceState.MayInit) != 0)
            {
                return Fail("String result storage is reused while its prior value is live.", out failure);
            }

            if (!body.IsReachable(result.Join))
            {
                if (result.Count != 0)
                {
                    return Fail("Unreachable string result has arrivals.", out failure);
                }

                continue;
            }

            if (result.Count == 0 || result.Count != this.incoming[result.Join] || !this.Dominates(result.Declare, result.Join) ||
                (body.GetInputState(result.Join, result.Place) & PlaceState.MustInit) == 0)
            {
                return Fail("String result is not initialized on every arrival.", out failure);
            }

            for (var n = 0; n < result.Count; n++)
            {
                var arrival = body.ResultArrivals[result.Start + n];
                if ((uint)arrival.Edge >= (uint)body.Edges.Count || (uint)arrival.Write >= (uint)body.Operations.Count ||
                    this.stringArrivalSeen[arrival.Edge] != 0 || this.stringResultWrites[arrival.Write] != i + 1)
                {
                    return Fail("String arrival has no unique matching Write.", out failure);
                }

                var edge = body.Edges[arrival.Edge];
                if ((uint)edge.From >= (uint)body.Operations.Count || edge.To != result.Join || edge.Kind != OwnershipEdgeKind.Normal ||
                    !body.IsReachable(edge.From) || !this.Dominates(arrival.Write, edge.From))
                {
                    return Fail("String result was not secured before its arrival cleanup.", out failure);
                }

                this.stringArrivalSeen[arrival.Edge] = 1;
            }
        }

        return true;
    }
}
