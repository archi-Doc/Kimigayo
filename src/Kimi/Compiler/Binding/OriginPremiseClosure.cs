// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

/// <summary>The rule that introduced a premise edge or reached a node (PLAN G74 catalog, proposal §3.3).</summary>
internal enum OriginPremiseRule : byte
{
    /// <summary>I1: the target itself.</summary>
    Target,

    /// <summary>I2/I3: `static` or a raw-borrow anchor, which outlive every target.</summary>
    Top,

    /// <summary>I4: an operand outlives its meet.</summary>
    MeetOperand,

    /// <summary>I4: a meet outlives a target when every operand does.</summary>
    MeetCompletion,

    /// <summary>R1: a relation of an enclosing declaration's established contract (`==` adds both directions, I5).</summary>
    Declaration,

    /// <summary>R2: input well-formedness: a Type's own relations and its stored Origins outliving an outer borrow.</summary>
    Input,

    /// <summary>R3: result well-formedness of the enclosing named function.</summary>
    Result,

    /// <summary>R4: associated formation and requirement relations.</summary>
    Associated,

    /// <summary>R5: the stored Origins of a borrowed complete local Place outlive that Place.</summary>
    LocalPlace,
}

/// <summary>
/// SPEC 15.3.6: a finite premise graph over Origin expressions and its single-target closure (PLAN G74, proposal §3.5). Nodes are
/// interned expressions; an edge `A >= B` is stored in B's incoming list, and every meet keeps its operands. `Outlivers(S)` seeds
/// S and the top-like nodes, follows incoming edges, adds the operands of a reached meet, and adds a meet once all of its operands
/// are reached. Each node is inserted once, each edge activated once and each operand incidence decremented once per closure, so a
/// closure is linear in nodes, edges and meet incidences. Storage is reused across environments and allocates nothing when warm.
/// An edge may carry a Semantics-case condition (SPEC 15.6.5): the pair binders that must all be borrows for it to hold; a closure
/// follows only the edges whose condition the request's condition contains.
/// </summary>
internal sealed class OriginPremiseClosure
{
    private readonly Dictionary<BoundOrigin, int> index = new(ReferenceEqualityComparer.Instance);
    private BoundOrigin[] nodes = new BoundOrigin[32];
    private int[] firstIncoming = new int[32];
    private int[] firstMeet = new int[32];
    private int[] operandCount = new int[32];
    private int[] operandStart = new int[32];
    private int[] operandNodes = new int[64];
    private int[] reachedStamp = new int[32];
    private int[] unmetStamp = new int[32];
    private int[] unmet = new int[32];
    private int[] reachedBy = new int[32];
    private int[] reachedFrom = new int[32];
    private int[] queue = new int[32];
    private int[] edgeLonger = new int[64];
    private int[] edgeNext = new int[64];
    private OriginPremiseRule[] edgeRule = new OriginPremiseRule[64];
    private ulong[] edgeCondition = new ulong[64];
    private int[] meetLink = new int[64];
    private int[] meetNext = new int[64];
    private int[] topNodes = new int[8];
    private int nodeCount;
    private int edgeCount;
    private int meetLinkCount;
    private int operandNodeCount;
    private int topCount;
    private int stamp;

    /// <summary>Gets the nodes of the current graph.</summary>
    internal int NodeCount => this.nodeCount;

    /// <summary>Gets the premise edges of the current graph.</summary>
    internal int EdgeCount => this.edgeCount;

    /// <summary>Gets the operand incidences of the current graph's meets.</summary>
    internal int MeetIncidences => this.meetLinkCount;

    /// <summary>Gets the retained node and edge capacity, the largest graph since this storage was created.</summary>
    internal (int Nodes, int Edges) Capacity => (this.nodes.Length, this.edgeLonger.Length);

    /// <summary>Gets a value indicating whether the storage still references an Origin expression.</summary>
    internal bool RetainsOrigins
    {
        get
        {
            if (this.index.Count != 0)
            {
                return true;
            }

            for (var i = 0; i < this.nodes.Length; i++)
            {
                if (this.nodes[i] is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the work of the last closure: node insertions, edge activations and operand decrements.</summary>
    internal (int Insertions, int Activations, int Decrements) LastWork { get; private set; }

    /// <summary>Clears the graph, keeping storage; old object references are released.</summary>
    internal void Clear()
    {
        Array.Clear(this.nodes, 0, this.nodeCount);
        this.index.Clear();
        this.nodeCount = 0;
        this.edgeCount = 0;
        this.meetLinkCount = 0;
        this.operandNodeCount = 0;
        this.topCount = 0;
    }

    /// <summary>Gets an expression's node, interning it with its meet operands; <paramref name="added"/> tells a new node.</summary>
    /// <param name="origin">The expression.</param>
    /// <param name="added">Whether the node is new.</param>
    /// <returns>The node.</returns>
    internal int Node(BoundOrigin origin, out bool added)
    {
        if (this.index.TryGetValue(origin, out var node))
        {
            added = false;
            return node;
        }

        added = true;
        node = this.nodeCount++;
        Grow(ref this.nodes, this.nodeCount);
        Grow(ref this.firstIncoming, this.nodeCount);
        Grow(ref this.firstMeet, this.nodeCount);
        Grow(ref this.operandCount, this.nodeCount);
        Grow(ref this.operandStart, this.nodeCount);
        Grow(ref this.reachedStamp, this.nodeCount);
        Grow(ref this.unmetStamp, this.nodeCount);
        Grow(ref this.unmet, this.nodeCount);
        Grow(ref this.reachedBy, this.nodeCount);
        Grow(ref this.reachedFrom, this.nodeCount);
        Grow(ref this.queue, this.nodeCount);
        this.nodes[node] = origin;
        this.firstIncoming[node] = -1;
        this.firstMeet[node] = -1;
        this.operandCount[node] = 0;
        this.reachedStamp[node] = 0;
        this.unmetStamp[node] = 0;
        this.index.Add(origin, node);
        if (origin.Kind is OriginKind.Static or OriginKind.Anchor)
        {
            Grow(ref this.topNodes, this.topCount + 1);
            this.topNodes[this.topCount++] = node;
        }
        else if (origin.Kind == OriginKind.Intersection)
        {
            this.operandCount[node] = origin.Operands.Count;
            var start = this.operandNodeCount;
            this.operandNodeCount += origin.Operands.Count;
            Grow(ref this.operandNodes, this.operandNodeCount);
            this.operandStart[node] = start;
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                var operand = this.Node(origin.Operands[i], out _);
                this.operandNodes[start + i] = operand;
                Grow(ref this.meetLink, this.meetLinkCount + 1);
                Grow(ref this.meetNext, this.meetLinkCount + 1);
                this.meetLink[this.meetLinkCount] = node;
                this.meetNext[this.meetLinkCount] = this.firstMeet[operand];
                this.firstMeet[operand] = this.meetLinkCount++;
            }
        }

        return node;
    }

    /// <summary>Gets the expression of a node.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The expression.</returns>
    internal BoundOrigin Origin(int node) => this.nodes[node];

    /// <summary>Adds the premise edge `longer >= shorter`.</summary>
    /// <param name="longer">The longer node.</param>
    /// <param name="shorter">The shorter node.</param>
    /// <param name="rule">The catalog rule that introduced it.</param>
    /// <param name="condition">The pair binders that must be borrows for the edge to hold, as bits; none for an unconditional edge.</param>
    internal void AddEdge(int longer, int shorter, OriginPremiseRule rule, ulong condition = 0)
    {
        if (longer == shorter)
        {
            return;
        }

        Grow(ref this.edgeLonger, this.edgeCount + 1);
        Grow(ref this.edgeNext, this.edgeCount + 1);
        Grow(ref this.edgeRule, this.edgeCount + 1);
        Grow(ref this.edgeCondition, this.edgeCount + 1);
        this.edgeLonger[this.edgeCount] = longer;
        this.edgeRule[this.edgeCount] = rule;
        this.edgeCondition[this.edgeCount] = condition;
        this.edgeNext[this.edgeCount] = this.firstIncoming[shorter];
        this.firstIncoming[shorter] = this.edgeCount++;
    }

    /// <summary>Whether `longer >= shorter` is entailed: whether the closure from <paramref name="shorter"/> reaches it.</summary>
    /// <param name="longer">The longer node.</param>
    /// <param name="shorter">The target node.</param>
    /// <param name="condition">The pair binders that are borrows in every case the relation is required in, as bits.</param>
    /// <returns>Whether the relation is entailed.</returns>
    internal bool Entails(int longer, int shorter, ulong condition = 0)
    {
        if (++this.stamp == int.MaxValue)
        {
            Array.Clear(this.reachedStamp);
            Array.Clear(this.unmetStamp);
            this.stamp = 1;
        }

        var head = 0;
        var tail = 0;
        var activations = 0;
        var decrements = 0;
        this.Reach(shorter, -1 - (int)OriginPremiseRule.Target, -1, ref tail);
        for (var i = 0; i < this.topCount; i++)
        {
            this.Reach(this.topNodes[i], -1 - (int)OriginPremiseRule.Top, -1, ref tail);
        }

        var entailed = false;
        while (head < tail)
        {
            var node = this.queue[head++];
            if (node == longer)
            {
                entailed = true;
                break;
            }

            for (var edge = this.firstIncoming[node]; edge >= 0; edge = this.edgeNext[edge])
            {
                if ((this.edgeCondition[edge] & ~condition) != 0)
                {
                    continue; // A premise of a case the relation is not required in.
                }

                activations++;
                this.Reach(this.edgeLonger[edge], edge, node, ref tail);
            }

            for (int i = this.operandStart[node], end = i + this.operandCount[node]; i < end; i++)
            {
                this.Reach(this.operandNodes[i], -1 - (int)OriginPremiseRule.MeetOperand, node, ref tail);
            }

            for (var link = this.firstMeet[node]; link >= 0; link = this.meetNext[link])
            {
                var meet = this.meetLink[link];
                if (this.unmetStamp[meet] != this.stamp)
                {
                    this.unmetStamp[meet] = this.stamp;
                    this.unmet[meet] = this.operandCount[meet];
                }

                decrements++;
                if (--this.unmet[meet] == 0)
                {
                    this.Reach(meet, -1 - (int)OriginPremiseRule.MeetCompletion, node, ref tail);
                }
            }
        }

        this.LastWork = (tail, activations, decrements);
        return entailed || this.reachedStamp[longer] == this.stamp;
    }

    /// <summary>The rule or edge that first reached a node in the last closure: an edge index, or the negated rule minus one.</summary>
    /// <param name="node">A reached node.</param>
    /// <returns>The edge index, or <c>-1 - rule</c>.</returns>
    internal int ReachedBy(int node) => this.reachedBy[node];

    /// <summary>The node whose processing first reached a node in the last closure, or -1 for a seed; following it from an
    /// entailed node back to the target gives the witness of the entailment (proposal §3.6).</summary>
    /// <param name="node">A reached node.</param>
    /// <returns>The predecessor node, or -1.</returns>
    internal int ReachedFrom(int node) => this.reachedFrom[node];

    /// <summary>Gets the rule that first reached a node in the last closure.</summary>
    /// <param name="node">A reached node.</param>
    /// <returns>The rule.</returns>
    internal OriginPremiseRule ReachedRule(int node) => this.reachedBy[node] is var by && by >= 0 ? this.edgeRule[by] : (OriginPremiseRule)(-1 - by);

    /// <summary>Gets the rule of an edge.</summary>
    /// <param name="edge">The edge.</param>
    /// <returns>The rule.</returns>
    internal OriginPremiseRule EdgeRule(int edge) => this.edgeRule[edge];

    private static void Grow<T>(ref T[] array, int count)
    {
        if (count > array.Length)
        {
            Array.Resize(ref array, Math.Max(count, array.Length * 2));
        }
    }

    private void Reach(int node, int by, int from, ref int tail)
    {
        if (this.reachedStamp[node] == this.stamp)
        {
            return;
        }

        this.reachedStamp[node] = this.stamp;
        this.reachedBy[node] = by;
        this.reachedFrom[node] = from;
        this.queue[tail++] = node;
    }
}
