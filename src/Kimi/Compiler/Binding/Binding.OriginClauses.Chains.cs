// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// R4-M118's chain rule for a declaration's rows (SPEC 15.6.5, 23.1.2; DECISIONS R4-M118, BU-62). Those rows reach no slice (R4-M101;
// Binding.OriginRows.cs), so the declaration judge is their only judge. The rule has two holders until R6-check, one contract: A5's chain
// judgment (MirOrigins.Chains) for a body's rows and this Transitional copy for a declaration's, each judging only the relations Binding
// does not prove (2a0-D D1), each chain under its rows' minimal conditions; PLAN's unit that moves A5's core below both phases makes it
// one component and deletes this file.
// - Responsibility: every declaration row of the pass whose ends hold an Unbound atom (unboundRows; JudgeDeclarationRows judges a row
//   with two Fixed ends at once, the rule's Fixed -> Fixed edge, and hands every other to this rule unjudged). An Unbound atom, or a meet
//   with one, is a Dependent node; any other end a declaration row can have (static, a parameter, an input, a meet of them; DeclarationAtom)
//   is Fixed. Nodes are the view's canonical Origins. Each row is an edge Longer -> Shorter, and back for an equality, a meet expanded by
//   MeetNode's rule (A5's Relate): at the longer end an edge from each operand and, unless Fixed, the meet's own; a Dependent meet at the
//   shorter end is reached from each operand.
// - Outputs: an edge between two Fixed nodes is judged at once by the position judge. Every edge from a Dependent node into a Fixed node X
//   is a last edge: each Fixed source S that reaches its tail through Dependent nodes is judged S >= X at that edge's use, once per edge
//   and source (A5's Unknown), under each minimal condition that reaches it until one records. The records are the pass's call relations
//   (PublishCallRelations).
// - Condition (SPEC 15.6.5): a row's Condition is the set of pair binders that must all be borrows for the row to be required, as bits of
//   PairBinders' order at its use (BindingObligation, PairSlotCondition), and a premise serves a request only when its binders are among
//   the request's (OriginPremiseClosure.Close). A chain is required exactly where each of its rows is, so its condition is the OR of its rows'
//   Conditions, the conjunction of their cases; a Dependent node adds none. Outer scopes' binders come first, so a row at an inner use only
//   adds bits an outer use does not have.
// - Invariants: a Dependent node is never a source or a judged target: a row with a Dependent end is never judged itself, a chain that
//   starts or ends at one records nothing, and such rows rest on their atom's cause (UnboundCause). A source's search visits each
//   (node, condition) once and keeps each (edge, condition) once, by per-node and per-edge stamps: O(visited + edges scanned + the ends'
//   conditions per edge, squared) per source.
// - Prohibitions: no proof of its own (the position judge proves, with JudgeDeclarationRows' premises); no body row (A5's).
public sealed partial class Binding
{
    private readonly List<(BindingObligation Obligation, BoundOrigin Longer, BoundOrigin Shorter, BoundOrigin Atom)> unboundRows = new();
    private readonly List<BoundOrigin> chainNodes = new();
    private readonly Dictionary<BoundOrigin, int> chainIndex = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int Tail, int Head, int Row)> chainEdges = new();
    private readonly List<(int Edge, ulong Condition, int Next)> chainEnds = new(); // One source's last edges and conditions, linked per edge.
    private readonly List<(ulong Condition, int Next)> chainSeen = new(); // The conditions a source's search met at a node, by chainSeenHead.
    private readonly Stack<(int Node, ulong Condition)> chainPending = new();
    private int[] chainStart = [];
    private int[] chainOut = [];
    private int[] chainStamp = []; // Per node: the source search that last met it, and the first condition it met there.
    private int[] chainSeenHead = [];
    private int[] chainEndStamp = []; // Per edge: the source search that last ended a chain there, and its first end.
    private int[] chainEndHead = [];
    private int[] chainRecorded = []; // Per edge: the source search that recorded at it.
    private int chainSearch;

    // A node of a declaration row's graph is Dependent when an Unbound atom is in it.
    private static bool Dependent(BoundOrigin origin) => UnboundAtom(origin) is not null;

    [Transitional("R6-check", Reason = "PLAN's R4-M118 component (A5's chain core below both phases) replaces this copy (DECISIONS BU-62).")]
    private void JudgeChains()
    {
        for (var r = 0; r < this.unboundRows.Count; r++)
        {
            var (row, longer, shorter, _) = this.unboundRows[r];
            (longer, shorter) = (this.CanonicalOrigin(longer), this.CanonicalOrigin(shorter));
            this.RelateChain(longer, shorter, r);
            if (row.Equality)
            {
                this.RelateChain(shorter, longer, r);
            }
        }

        this.IndexChainEdges();
        for (var source = 0; source < this.chainNodes.Count; source++)
        {
            if (!Dependent(this.chainNodes[source]))
            {
                this.ReachFrom(source);
            }
        }

        this.chainNodes.Clear();
        this.chainIndex.Clear();
        this.chainEdges.Clear();
    }

    // MeetNode's rule (2a0-D D15, A5's Relate) for the row's edge `longer -> shorter`.
    private void RelateChain(BoundOrigin longer, BoundOrigin shorter, int row)
    {
        if (longer.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                this.ChainEdge(longer.Operands[i], shorter, row);
            }
        }

        if (longer.Kind != OriginKind.Intersection || Dependent(longer))
        {
            this.ChainEdge(longer, shorter, row);
        }

        for (var i = 0; shorter.Kind == OriginKind.Intersection && Dependent(shorter) && i < shorter.Operands.Count; i++)
        {
            this.ChainEdge(shorter.Operands[i], shorter, row);
        }
    }

    // An edge between two Fixed nodes passes nothing on, so it is judged at once; any other joins the graph.
    private void ChainEdge(BoundOrigin tail, BoundOrigin head, int row)
    {
        if (ReferenceEquals(tail, head))
        {
            return;
        }

        if (!Dependent(tail) && !Dependent(head))
        {
            var obligation = this.unboundRows[row].Obligation;
            this.JudgeCallPosition(
                obligation.Use, tail, head, equality: false, null, null, wellFormed: obligation.Clause is null, condition: obligation.Condition);
            return;
        }

        this.chainEdges.Add((this.ChainNode(tail), this.ChainNode(head), row));
    }

    private int ChainNode(BoundOrigin origin)
    {
        if (!this.chainIndex.TryGetValue(origin, out var node))
        {
            this.chainIndex.Add(origin, node = this.chainNodes.Count);
            this.chainNodes.Add(origin);
        }

        return node;
    }

    // The edges by tail, a counting sort: node v's are chainOut[chainStart[v]..chainStart[v + 1]); and the search's per-node and per-edge
    // stamps, sized.
    private void IndexChainEdges()
    {
        var (nodes, edges) = (this.chainNodes.Count, this.chainEdges.Count);
        Grow(ref this.chainStart, nodes + 2);
        Grow(ref this.chainOut, edges);
        Grow(ref this.chainStamp, nodes);
        Grow(ref this.chainSeenHead, nodes);
        Grow(ref this.chainEndStamp, edges);
        Grow(ref this.chainEndHead, edges);
        Grow(ref this.chainRecorded, edges);
        this.chainStart.AsSpan(0, nodes + 2).Clear();
        foreach (var edge in this.chainEdges)
        {
            this.chainStart[edge.Tail + 2]++;
        }

        for (var v = 0; v < nodes; v++)
        {
            this.chainStart[v + 2] += this.chainStart[v + 1];
        }

        for (var e = 0; e < edges; e++)
        {
            this.chainOut[this.chainStart[this.chainEdges[e].Tail + 1]++] = e;
        }

        static void Grow(ref int[] array, int length)
        {
            if (array.Length < length)
            {
                Array.Resize(ref array, Math.Max(length, 2 * array.Length));
            }
        }
    }

    // A Fixed source's chains: a search through Dependent nodes, each (node, condition) once, the condition the OR of the rows passed; every
    // edge from a Dependent node into a Fixed one is a last edge, judged when the search ends.
    private void ReachFrom(int source)
    {
        if (++this.chainSearch == int.MaxValue)
        {
            this.chainStamp.AsSpan().Clear();
            this.chainEndStamp.AsSpan().Clear();
            this.chainRecorded.AsSpan().Clear();
            this.chainSearch = 1;
        }

        this.chainSeen.Clear();
        this.chainEnds.Clear();
        this.chainPending.Push((source, 0));
        while (this.chainPending.TryPop(out var state))
        {
            for (var i = this.chainStart[state.Node]; i < this.chainStart[state.Node + 1]; i++)
            {
                var (_, head, row) = this.chainEdges[this.chainOut[i]];
                var condition = state.Condition | this.unboundRows[row].Obligation.Condition;
                if (!Dependent(this.chainNodes[head]))
                {
                    this.AddChainEnd(this.chainOut[i], condition);
                }
                else if (this.FirstVisit(head, condition))
                {
                    this.chainPending.Push((head, condition));
                }
            }
        }

        for (var end = 0; end < this.chainEnds.Count; end++)
        {
            this.JudgeChainEnd(source, end);
        }
    }

    // Whether this source's search meets the node under this condition for the first time.
    private bool FirstVisit(int node, ulong condition)
    {
        if (this.chainStamp[node] != this.chainSearch)
        {
            (this.chainStamp[node], this.chainSeenHead[node]) = (this.chainSearch, -1);
        }

        for (var at = this.chainSeenHead[node]; at >= 0; at = this.chainSeen[at].Next)
        {
            if (this.chainSeen[at].Condition == condition)
            {
                return false;
            }
        }

        this.chainSeen.Add((condition, this.chainSeenHead[node]));
        this.chainSeenHead[node] = this.chainSeen.Count - 1;
        return true;
    }

    // A last edge reached under a condition, kept once per edge and condition, linked with the edge's other conditions.
    private void AddChainEnd(int edge, ulong condition)
    {
        if (this.chainEndStamp[edge] != this.chainSearch)
        {
            (this.chainEndStamp[edge], this.chainEndHead[edge]) = (this.chainSearch, -1);
        }

        for (var at = this.chainEndHead[edge]; at >= 0; at = this.chainEnds[at].Next)
        {
            if (this.chainEnds[at].Condition == condition)
            {
                return;
            }
        }

        this.chainEnds.Add((edge, condition, this.chainEndHead[edge]));
        this.chainEndHead[edge] = this.chainEnds.Count - 1;
    }

    // S >= X at a last edge's use, once per edge and source (A5's Unknown): under a minimal condition (no other of the edge narrower), until
    // one records.
    private void JudgeChainEnd(int source, int end)
    {
        var (edge, condition, _) = this.chainEnds[end];
        var (_, target, row) = this.chainEdges[edge];
        if (source == target || this.chainRecorded[edge] == this.chainSearch || this.Narrowed(end))
        {
            return;
        }

        var (obligation, records) = (this.unboundRows[row].Obligation, this.callRelationScratch.Count);
        var (longer, shorter) = (this.chainNodes[source], this.chainNodes[target]);
        this.JudgeCallPosition(obligation.Use, longer, shorter, equality: false, null, null, wellFormed: obligation.Clause is null, condition: condition);
        if (this.callRelationScratch.Count != records)
        {
            this.chainRecorded[edge] = this.chainSearch;
        }
    }

    // Whether another condition that reached the same edge has a strict subset of this one's binders.
    private bool Narrowed(int end)
    {
        var (edge, condition, _) = this.chainEnds[end];
        for (var at = this.chainEndHead[edge]; at >= 0; at = this.chainEnds[at].Next)
        {
            if (this.chainEnds[at].Condition != condition && (this.chainEnds[at].Condition & ~condition) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
