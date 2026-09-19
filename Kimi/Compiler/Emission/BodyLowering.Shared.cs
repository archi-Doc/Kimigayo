// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    internal bool SharedDominates(int definition, int use) => this.Dominates(definition, use);

    internal bool ValidateSharedGraph(OwnershipBody body)
    {
        var count = body.Operations.Count;
        Grow(ref this.blocks, count);
        Grow(ref this.queue, count);
        Grow(ref this.incoming, count);
        this.incoming.AsSpan(0, count).Clear();
        for (var id = 0; id < count; id++)
        {
            this.blocks[id] = body.IsReachable(id) ? id : -1;
            var seen = 0;
            for (var edgeId = body.EdgeHeads[id]; edgeId >= 0; edgeId = body.Edges[edgeId].Next)
            {
                if ((uint)edgeId >= (uint)body.Edges.Count || ++seen > body.Edges.Count)
                {
                    return false;
                }

                var edge = body.Edges[edgeId];
                if (edge.From != id || (uint)edge.To >= (uint)count ||
                    edge.Kind is not (OwnershipEdgeKind.Normal or OwnershipEdgeKind.Return or OwnershipEdgeKind.Back or OwnershipEdgeKind.True or OwnershipEdgeKind.False or OwnershipEdgeKind.Abort or OwnershipEdgeKind.MatchArm or OwnershipEdgeKind.Unmatched))
                {
                    return false;
                }

                if (body.IsReachable(id) && !body.IsReachable(edge.To))
                {
                    return false; // Reject stale reachability before building the dominator graph.
                }

                if (body.IsReachable(id) && edge.Kind != OwnershipEdgeKind.Abort)
                {
                    this.incoming[edge.To]++;
                }
            }
        }

        this.BuildDominators(body);
        for (var id = 0; id < count; id++)
        {
            var value = body.Values[id];
            if (value.Kind == OwnershipValueKind.Phi)
            {
                if (value.Count != this.incoming[id] || (body.IsReachable(id) && value.Count == 0))
                {
                    return false;
                }

                for (var n = 0; n < value.Count; n++)
                {
                    if (!this.ValidatePhiInput(body, id, value.Start + n, out var block, out _) || block < 0)
                    {
                        return false;
                    }
                }

                continue;
            }

            // Owned aggregate/symbolic flow can retain a secured-storage alias rather
            // than an SSA producer. Only scalar values become shared SSA operands.
            var scalar = ReferenceTypes.IsValue(ValueType(body, id)) || body.Operations[id].Kind == OwnershipOperationKind.Branch;
            for (var n = 0; scalar && n < value.Count; n++)
            {
                var input = body.ValueOperands[value.Start + n];
                if ((uint)input >= (uint)id || (body.IsReachable(id) && !this.Dominates(input, id)))
                {
                    return false;
                }
            }

            if (value.Kind == OwnershipValueKind.Sequence && value.Constant >= 0 && value.Constant < body.Sequences.Count &&
                body.Sequences[(int)value.Constant].Index is >= 0 and var index &&
                ((uint)index >= (uint)id || (body.IsReachable(id) && !this.Dominates(index, id))))
            {
                return false;
            }
        }

        return true;
    }
}
