// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private int[] slotResultPlaces = [];
    private int[] slotResultDeclarations = [];
    private int[] slotResultWrites = [];
    private int[] slotResultJoins = [];
    private int[] slotArrivalSeen = [];
    private int[] slotResultNext = [];
    private int[] slotPendingVisits = [];
    private int slotPendingSource = -1;

    private bool PrepareSlotResults(OwnershipBody body, out string? failure)
    {
        failure = null;
        this.slotPendingSource = -1;
        var count = body.Operations.Count;
        Grow(ref this.slotResultPlaces, body.Places.Count);
        Grow(ref this.slotResultDeclarations, count);
        Grow(ref this.slotResultWrites, count);
        Grow(ref this.slotResultJoins, count);
        Grow(ref this.slotArrivalSeen, body.Edges.Count);
        Grow(ref this.slotResultNext, body.SlotResults.Count);
        this.slotResultPlaces.AsSpan(0, body.Places.Count).Clear();
        this.slotResultDeclarations.AsSpan(0, count).Clear();
        this.slotResultWrites.AsSpan(0, count).Clear();
        this.slotResultJoins.AsSpan(0, count).Clear();
        this.slotArrivalSeen.AsSpan(0, body.Edges.Count).Clear();
        var arrivals = 0;
        for (var i = 0; i < body.SlotResults.Count; i++)
        {
            var result = body.SlotResults[i];
            if ((uint)result.Place >= (uint)body.Places.Count || (uint)result.Declare >= (uint)count || (uint)result.Join >= (uint)count ||
                result.Start != arrivals || result.Count < 0 || result.Count > body.ResultArrivals.Count - arrivals ||
                this.slotResultDeclarations[result.Declare] != 0 || this.slotResultJoins[result.Join] != 0)
            {
                return Fail("Invalid slot result lifetime or arrival range.", out failure);
            }

            var place = body.Places[result.Place];
            var declaration = body.Operations[result.Declare];
            var join = body.Operations[result.Join];
            if (place.Kind != OwnershipPlaceKind.Result || !SlotTypes.IsResult(place.Type) ||
                place.Source is not (IfKoto or DoKoto or LoopKoto or MatchKoto) || !ReferenceEquals(place.Source.BoundType, place.Type) ||
                declaration.Kind != OwnershipOperationKind.Declare || declaration.Place != result.Place || !ReferenceEquals(declaration.Source, place.Source) ||
                join.Kind != OwnershipOperationKind.Branch || join.Place != -1 || !ReferenceEquals(join.Source, place.Source) || body.Values[result.Join].Kind != OwnershipValueKind.None)
            {
                return Fail("Slot result is not an explicit expression lifetime.", out failure);
            }

            arrivals += result.Count;
            this.slotResultNext[i] = this.slotResultPlaces[result.Place] - 1;
            this.slotResultPlaces[result.Place] = i + 1;
            this.slotResultDeclarations[result.Declare] = i + 1;
            this.slotResultJoins[result.Join] = i + 1;
        }

        if (arrivals != body.ResultArrivals.Count)
        {
            return Fail("Unowned slot result arrivals.", out failure);
        }

        foreach (var write in body.ResultWrites)
        {
            if ((uint)write.Operation >= (uint)count || (uint)write.Declare >= (uint)count || this.slotResultWrites[write.Operation] != 0 ||
                this.slotResultDeclarations[write.Declare] is not (> 0 and var plan))
            {
                return Fail("Slot result Write has no unique lifetime.", out failure);
            }

            var operation = body.Operations[write.Operation];
            if (operation.Kind != OwnershipOperationKind.Write || operation.Place != body.SlotResults[plan - 1].Place)
            {
                return Fail("Slot result Write has the wrong destination.", out failure);
            }

            this.slotResultWrites[write.Operation] = plan;
        }

        return true;
    }

    private bool ValidateSlotResults(OwnershipBody body, out string? failure)
    {
        failure = null;
        if (body.SlotResults.Count == 0)
        {
            return true;
        }

        foreach (var write in body.ResultWrites)
        {
            if (!body.IsReachable(write.Operation))
            {
                continue;
            }

            var result = body.SlotResults[this.slotResultWrites[write.Operation] - 1];
            var operation = body.Operations[write.Operation];
            if (operation.Placement != PlacementKind.Initialization || !this.Dominates(write.Declare, write.Operation) ||
                this.Dominates(result.Join, write.Operation) || (body.GetInputState(write.Operation, result.Place) & PlaceState.MayInit) != 0)
            {
                return Fail("Slot result Write is not fresh placement in its lifetime.", out failure);
            }
        }

        for (var i = 0; i < body.SlotResults.Count; i++)
        {
            var result = body.SlotResults[i];
            if (body.IsReachable(result.Declare) && (body.GetInputState(result.Declare, result.Place) & PlaceState.MayInit) != 0)
            {
                return Fail("Slot result storage is reused while its prior value is live.", out failure);
            }

            if (!body.IsReachable(result.Join))
            {
                if (result.Count != 0)
                {
                    return Fail("Unreachable slot result has arrivals.", out failure);
                }

                continue;
            }

            if (result.Count == 0 || result.Count != this.LogicalIncoming(result.Join) || !this.Dominates(result.Declare, result.Join) ||
                (body.GetInputState(result.Join, result.Place) & PlaceState.MustInit) == 0)
            {
                return Fail("Slot result is not initialized on every arrival.", out failure);
            }

            var physicalArrivals = 0;
            for (var n = 0; n < result.Count; n++)
            {
                var arrival = body.ResultArrivals[result.Start + n];
                if ((uint)arrival.Edge >= (uint)body.Edges.Count || (uint)arrival.Write >= (uint)body.Operations.Count ||
                    this.slotArrivalSeen[arrival.Edge] != 0 || this.slotResultWrites[arrival.Write] != i + 1)
                {
                    return Fail("Slot arrival has no unique matching Write.", out failure);
                }

                var edge = body.Edges[arrival.Edge];
                if ((uint)edge.From >= (uint)body.Operations.Count || edge.To != result.Join || edge.Kind != OwnershipEdgeKind.Normal ||
                    !body.IsReachable(edge.From) || !this.Dominates(arrival.Write, edge.From))
                {
                    return Fail("Slot result was not secured before its arrival cleanup.", out failure);
                }

                this.slotArrivalSeen[arrival.Edge] = 1;
                physicalArrivals += this.blocks[edge.From] >= 0 ? 1 : 0;
            }

            if (physicalArrivals != this.incoming[result.Join])
            {
                return Fail("Slot arrivals do not cover execution predecessors.", out failure);
            }
        }

        // Securing bytes is not delivery. In particular, a deferred operation
        // cannot consume the value while the result's cleanup is still running.
        for (var id = 0; id < body.Operations.Count; id++)
        {
            if (!body.IsReachable(id))
            {
                continue;
            }

            var operation = body.Operations[id];
            var source = operation.Kind switch
            {
                OwnershipOperationKind.Write or OwnershipOperationKind.WriteElement or OwnershipOperationKind.PayloadPlacement or OwnershipOperationKind.InitializeSubject => operation.Input,
                OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Borrow or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Cleanup or OwnershipOperationKind.LocateReceiver or OwnershipOperationKind.ProjectElement => operation.Place,
                _ => -1,
            };
            if (source < 0 || this.slotResultPlaces[source] == 0)
            {
                continue;
            }

            if (operation.Kind == OwnershipOperationKind.Cleanup && (body.GetInputState(id, source) & PlaceState.MayInit) == 0)
            {
                continue;
            }

            var lifetime = -1;
            for (var r = this.slotResultPlaces[source] - 1; r >= 0; r = this.slotResultNext[r])
            {
                var result = body.SlotResults[r];
                if (this.Dominates(result.Declare, id) && (lifetime < 0 || this.Dominates(body.SlotResults[lifetime].Declare, result.Declare)))
                {
                    lifetime = r;
                }
            }

            if (lifetime < 0 || !this.Dominates(body.SlotResults[lifetime].Join, id))
            {
                if (operation.Kind != OwnershipOperationKind.Cleanup || this.borrowedTemporaries[source] == 0 ||
                    (body.GetInputState(id, source) & PlaceState.MustInit) != 0 || !this.IsDeliveredSlotCleanup(body, source, id))
                {
                    return Fail("Slot result use must follow normal delivery in its current lifetime.", out failure);
                }
            }
        }

        return true;
    }

    private bool IsDeliveredSlotCleanup(OwnershipBody body, int source, int id)
    {
        // A short-circuit path may skip the entire result lifetime. For conditional
        // cleanup, prove that no secured value can reach it without passing Join.
        // Cache the latest source traversal over existing adjacency lists and graph scratch.
        if (this.slotPendingSource != source)
        {
            Grow(ref this.slotPendingVisits, body.Operations.Count);
            this.slotPendingVisits.AsSpan(0, body.Operations.Count).Clear();
            this.slotPendingSource = source;
            var count = 0;
            foreach (var write in body.ResultWrites)
            {
                if (body.Operations[write.Operation].Place == source && body.IsReachable(write.Operation))
                {
                    this.slotPendingVisits[write.Operation] = 1;
                    this.queue[count++] = write.Operation;
                }
            }

            for (var q = 0; q < count; q++)
            {
                for (var e = body.EdgeHeads[this.queue[q]]; e >= 0; e = body.Edges[e].Next)
                {
                    var edge = body.Edges[e];
                    var join = this.slotResultJoins[edge.To] - 1;
                    if (edge.Kind == OwnershipEdgeKind.Abort || !body.IsReachable(edge.To) || this.slotPendingVisits[edge.To] != 0 ||
                        (join >= 0 && body.SlotResults[join].Place == source))
                    {
                        continue;
                    }

                    this.slotPendingVisits[edge.To] = 1;
                    this.queue[count++] = edge.To;
                }
            }
        }

        return this.slotPendingVisits[id] == 0;
    }
}
