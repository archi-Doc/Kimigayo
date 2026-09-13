// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<EmissionOperand> phiOperands = new();
    private int[] phiSeen = [];
    private int[] reverseHeads = [];
    private int[] reverseNext = [];
    private int[] dominators = [];
    private int[] ranks = [];
    private int[] traversal = [];
    private int[] cursors = [];
    private int[] domStart = [];
    private int[] domEnd = [];

    private bool LowerReturn(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (this.deliveries[id] < 0 || operation.Place < 0 || body.Places[operation.Place].Kind != OwnershipPlaceKind.Result ||
            !ReferenceEquals(body.Places[operation.Place].Source, body.Function))
        {
            return Fail("Missing function result delivery.", out failure);
        }

        // Checking-only deliveries are not returns, and may follow an operand with no value.
        if (!body.IsReachable(id))
        {
            return true;
        }

        var type = body.Places[operation.Place].Type;
        if (function.Abi.NoReturn || !ReferenceEquals(type, body.Function.BoundSymbol?.Type ?? (body.Function.IsGenerated ? BoundType.Unit : null)))
        {
            return Fail("A function returns contrary to its signature.", out failure);
        }

        if (ReferenceEquals(type, BoundType.Unit))
        {
            function.Add(EmissionOpcode.ReturnVoid, id);
            return true;
        }

        var delivery = body.Deliveries[this.deliveries[id]];
        if (!IsScalar(type) || (uint)delivery.Value >= (uint)body.Values.Count || (uint)delivery.Write >= (uint)body.Operations.Count ||
            !ReferenceEquals(ValueType(body, delivery.Value), type) ||
            body.Operations[delivery.Write] is not { Kind: OwnershipOperationKind.Write, Placement: PlacementKind.Initialization } write ||
            write.Place != operation.Place || body.Values[delivery.Write].Kind != OwnershipValueKind.Alias ||
            Input(body, delivery.Write, 0) != delivery.Value || !this.Dominates(delivery.Value, delivery.Write) || !this.Dominates(delivery.Write, id))
        {
            return Fail("Return value was not secured before cleanup.", out failure);
        }

        function.AddScalar(EmissionOpcode.ReturnScalar, id, [this.PhysicalOperand(body, delivery.Value)], function.Abi.Result);
        return true;
    }

    // Cooper's reverse-postorder immediate dominators, using retained linear scratch storage.
    // Intervals in the resulting tree make every subsequent value-availability check constant time.
    private void BuildDominators(OwnershipBody body)
    {
        var count = body.Operations.Count;
        Grow(ref this.reverseHeads, count);
        Grow(ref this.reverseNext, Math.Max(count, body.Edges.Count));
        Grow(ref this.dominators, count);
        Grow(ref this.ranks, count);
        Grow(ref this.traversal, count);
        Grow(ref this.cursors, count);
        Grow(ref this.domStart, count);
        Grow(ref this.domEnd, count);
        Grow(ref this.phiSeen, count);
        this.reverseHeads.AsSpan(0, count).Fill(-1);
        this.dominators.AsSpan(0, count).Fill(-1);
        this.ranks.AsSpan(0, count).Fill(-1);
        this.phiSeen.AsSpan(0, count).Fill(-1);
        for (var op = 0; op < count; op++)
        {
            if (this.blocks[op] < 0)
            {
                continue;
            }

            for (var e = body.EdgeHeads[op]; e >= 0; e = body.Edges[e].Next)
            {
                var edge = body.Edges[e];
                if (edge.Kind != OwnershipEdgeKind.Abort)
                {
                    this.reverseNext[e] = this.reverseHeads[edge.To];
                    this.reverseHeads[edge.To] = e;
                }
            }
        }

        var depth = 0;
        var visited = 0;
        this.traversal[0] = 0;
        this.ranks[0] = -2;
        this.cursors[0] = body.EdgeHeads[0];
        while (depth >= 0)
        {
            var op = this.traversal[depth];
            var e = this.cursors[op];
            if (e < 0)
            {
                this.queue[visited++] = op;
                depth--;
                continue;
            }

            var edge = body.Edges[e];
            this.cursors[op] = edge.Next;
            if (edge.Kind != OwnershipEdgeKind.Abort && this.ranks[edge.To] == -1)
            {
                this.ranks[edge.To] = -2;
                this.cursors[edge.To] = body.EdgeHeads[edge.To];
                this.traversal[++depth] = edge.To;
            }
        }

        this.queue.AsSpan(0, visited).Reverse();
        for (var i = 0; i < visited; i++)
        {
            this.ranks[this.queue[i]] = i;
        }

        this.dominators[0] = 0;
        bool changed;
        do
        {
            changed = false;
            for (var i = 1; i < visited; i++)
            {
                var op = this.queue[i];
                var common = -1;
                for (var e = this.reverseHeads[op]; e >= 0; e = this.reverseNext[e])
                {
                    var from = body.Edges[e].From;
                    if (this.dominators[from] < 0)
                    {
                        continue;
                    }

                    if (common < 0)
                    {
                        common = from;
                    }
                    else
                    {
                        while (from != common)
                        {
                            if (this.ranks[from] > this.ranks[common])
                            {
                                from = this.dominators[from];
                            }
                            else
                            {
                                common = this.dominators[common];
                            }
                        }
                    }
                }

                if (this.dominators[op] != common)
                {
                    this.dominators[op] = common;
                    changed = true;
                }
            }
        }
        while (changed);

        // Reuse the reverse-adjacency arrays for children of the dominator tree.
        this.reverseHeads.AsSpan(0, count).Fill(-1);
        for (var i = 1; i < visited; i++)
        {
            var op = this.queue[i];
            var parent = this.dominators[op];
            this.reverseNext[op] = this.reverseHeads[parent];
            this.reverseHeads[parent] = op;
        }

        depth = 0;
        var clock = 0;
        this.traversal[0] = 0;
        this.cursors[0] = this.reverseHeads[0];
        this.domStart[0] = clock++;
        while (depth >= 0)
        {
            var op = this.traversal[depth];
            var child = this.cursors[op];
            if (child < 0)
            {
                this.domEnd[op] = clock;
                depth--;
            }
            else
            {
                this.cursors[op] = this.reverseNext[child];
                this.domStart[child] = clock++;
                this.cursors[child] = this.reverseHeads[child];
                this.traversal[++depth] = child;
            }
        }
    }

    private bool Dominates(int definition, int use) => this.blocks[definition] >= 0 &&
        this.domStart[definition] <= this.domStart[use] && this.domStart[use] < this.domEnd[definition];

    private bool LowerPhi(OwnershipBody body, EmissionFunction function, int id, string llvm, out string? failure)
    {
        failure = null;
        var value = body.Values[id];
        if (!body.IsReachable(id))
        {
            return value.Count == 0 || Fail("Unreachable result has incoming values.", out failure);
        }

        if (value.Count == 0 || value.Count != this.incoming[id])
        {
            return Fail("Phi inputs do not cover actual predecessors.", out failure);
        }

        this.phiOperands.Clear();
        for (var n = 0; n < value.Count; n++)
        {
            var input = body.PhiInputs[value.Start + n];
            if ((uint)input.Edge >= (uint)body.Edges.Count)
            {
                return Fail("Invalid result arrival edge.", out failure);
            }

            var edge = body.Edges[input.Edge];
            if ((uint)edge.From >= (uint)body.Operations.Count)
            {
                return Fail("Invalid result predecessor.", out failure);
            }

            var block = this.blocks[edge.From];
            if (edge.To != id || edge.Kind != OwnershipEdgeKind.Normal || block < 0 || this.phiSeen[block] == id ||
                !this.Dominates(input.Value, edge.From))
            {
                return Fail("Phi value is unavailable at its actual predecessor.", out failure);
            }

            this.phiSeen[block] = id;
            if (input.Write >= 0)
            {
                if ((uint)input.Write >= (uint)body.Operations.Count || body.Operations[input.Write] is not { Kind: OwnershipOperationKind.Write, Placement: PlacementKind.Initialization } write ||
                    write.Place != body.Operations[id].Place || body.Values[input.Write].Kind != OwnershipValueKind.Alias ||
                    !this.Dominates(input.Write, edge.From) || Definition(body, Input(body, input.Write, 0)) != input.Value)
                {
                    return Fail("Result was not secured before cleanup.", out failure);
                }
            }
            else if (input.Write != -1 || body.Operations[id].Source is not (Parsing.AndKoto or Parsing.OrKoto))
            {
                return Fail("Missing result acquisition operation.", out failure);
            }

            this.phiOperands.Add(this.PhysicalOperand(body, input.Value));
            this.phiOperands.Add(new(EmissionOperandKind.Block, this.blockEnds[block]));
        }

        function.AddScalar(EmissionOpcode.Phi, id, CollectionsMarshal.AsSpan(this.phiOperands), llvm);
        return true;
    }
}
