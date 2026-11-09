// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler;

// The declaration judge's adapter of R4-M118's chain judgment (TyCtxt.OriginChains; SPEC 15.6.5, 23.1.2; DECISIONS R4-M118, BU-62; PLAN
// G318) for a declaration's rows, which reach no slice (R4-M101; Binding.OriginRows.cs), so the declaration judge is their only judge. A5's
// Origin stratum (MirOrigins) reads the same component for a body's rows.
// - Responsibility: every declaration row of the pass whose ends hold an Unbound atom (unboundRows; JudgeDeclarationRows judges a row
//   with two Fixed ends at once and hands every other to this adapter unjudged, the input rule, 2a0-D D1). An Unbound atom, or a meet
//   with one, is a Dependent Origin; any other end a declaration row can have, fixed in the declaration judge's frame (static, a parameter,
//   an input, an enclosing body's Origin the declaration names, a meet of them; DeclarationAtom, DECISIONS BU-89), is Fixed. Origins are
//   the view's canonical ones; each row is a relation Longer -> Shorter, and back for an equality, whose edges MeetNode's rule gives
//   (TyCtxt.OriginChains.Relate) for each row alone, so a shorter meet's operand edges are each row's.
// - Condition (SPEC 15.6.5): a row's Condition is the set of pair binders that must all be borrows for the row to be required, as bits of
//   PairBinders' order at its use (BindingObligation, PairSlotCondition), and a premise serves a request only when its binders are among
//   the request's (OriginPremiseClosure.Close). A chain is required exactly where each of its rows is, so its condition is the OR of its
//   rows' Conditions. The graph's nodes are the chains' states (chainStates): each Fixed Origin, and each Dependent Origin under each
//   condition a path from a Fixed Origin reaches it with, formed forward from the Fixed Origins' edges; a row's edge from a state under P
//   goes to its Dependent head's state under P with the row's Condition, or to its Fixed head. One graph per pass, of the states the
//   chains reach, the copy's (Origin, condition) search states merged over its sources.
// - Outputs: an edge between two Fixed Origins is judged at once by the position judge under its row's Condition, in row order. At every
//   edge from a Dependent state into a Fixed Origin X, each Fixed source S its tail carries reaches X under the tail's condition with the
//   row's: each (row, last edge, source) is judged S >= X at the row's use under each of its minimal conditions (none of its others
//   narrower) until one records. The records are the pass's call relations (PublishCallRelations).
// - Cost: the states and their edges, the core's components x sources / 64 words, and per (row, last edge, source) its conditions
//   squared; the copy's per-source searches visited the same states and kept the same conditions.
// - Invariants: a Dependent Origin is never a source or a judged target (TyCtxt.OriginChains): a row with a Dependent end is never judged
//   itself, a chain that starts or ends at one records nothing, and such rows rest on their atom's cause (UnboundCause). Storage is pooled
//   per pass.
// - Prohibitions: no proof of its own (the position judge proves, with JudgeDeclarationRows' premises, the premise contract); no body row
//   (A5's); no source propagation of its own (TyCtxt.OriginChains').
public sealed partial class Binding
{
    private const int ChainRecorded = -2; // A (row, last edge, source) that recorded: none of its other conditions is judged.

    private readonly List<(BindingObligation Obligation, BoundOrigin Longer, BoundOrigin Shorter, BoundOrigin Atom)> unboundRows = new();
    private readonly TyCtxt.OriginChains chains = new();
    private readonly List<BoundOrigin> chainNodes = new(); // The rows' canonical Origins, with their classes and last-formed out-edges.
    private readonly List<OriginAtomKind> chainKinds = new();
    private readonly List<int> chainFirst = new();
    private readonly Dictionary<BoundOrigin, int> chainIndex = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int Tail, int Head, int Row, int Next)> chainEdges = new(); // The rows' edges, linked per tail.
    private readonly List<(int Node, ulong Condition)> chainStates = new(); // The graph's nodes (a Fixed Origin's condition is 0).
    private readonly Dictionary<(int Node, ulong Condition), int> chainStateIndex = new();
    private readonly List<((int Row, int Tail, int Head, int Source) Key, ulong Condition, int Next)> chainEnds = new(); // Linked per key.
    private readonly Dictionary<(int Row, int Tail, int Head, int Source), int> chainEndHead = new(); // A key's last end, or ChainRecorded.

    // A node of a declaration row's graph is Dependent when an Unbound atom is in it.
    private static bool Dependent(BoundOrigin origin) => UnboundAtom(origin) is not null;

    private void JudgeChains()
    {
        if (this.unboundRows.Count == 0)
        {
            return;
        }

        this.chains.Work = 0;
        for (var r = 0; r < this.unboundRows.Count; r++)
        {
            var (row, longer, shorter, _) = this.unboundRows[r];
            var relation = new DeclarationRelation(this, r);
            var from = this.ChainNode(this.CanonicalOrigin(longer));
            var to = this.ChainNode(this.CanonicalOrigin(shorter));
            this.chains.Begin();
            this.chains.Relate(ref relation, from, to);
            if (row.Equality)
            {
                this.chains.Relate(ref relation, to, from);
            }
        }

        this.chains.Begin();
        for (var e = 0; e < this.chainEdges.Count; e++)
        {
            if (this.chainKinds[this.chainEdges[e].Tail] == OriginAtomKind.Fixed)
            {
                this.ChainStep(this.ChainState(this.chainEdges[e].Tail, 0), e);
            }
        }

        for (var s = 0; s < this.chainStates.Count; s++)
        {
            var node = this.chainStates[s].Node;
            if (this.chainKinds[node] != OriginAtomKind.Dependent)
            {
                continue;
            }

            for (var e = this.chainFirst[node]; e >= 0; e = this.chainEdges[e].Next)
            {
                this.ChainStep(s, e);
            }
        }

        var graph = new DeclarationGraph(this);
        _ = this.chains.Chains(ref graph, this.chainStates.Count);
        for (var end = 0; end < this.chainEnds.Count; end++)
        {
            this.JudgeChainEnd(end);
        }

        this.OriginProofMetrics?.Chained(this.chains.Work);
        this.chainNodes.Clear();
        this.chainKinds.Clear();
        this.chainFirst.Clear();
        this.chainIndex.Clear();
        this.chainEdges.Clear();
        this.chainStates.Clear();
        this.chainStateIndex.Clear();
        this.chainEnds.Clear();
        this.chainEndHead.Clear();
    }

    private int ChainNode(BoundOrigin origin)
    {
        if (!this.chainIndex.TryGetValue(origin, out var node))
        {
            this.chainIndex.Add(origin, node = this.chainNodes.Count);
            this.chainNodes.Add(origin);
            this.chainKinds.Add(Dependent(origin) ? OriginAtomKind.Dependent : OriginAtomKind.Fixed);
            this.chainFirst.Add(-1);
        }

        return node;
    }

    // A relation's edge of row r, linked first among its tail's; an edge from an Origin to itself relates nothing.
    private void ChainEdge(int tail, int head, int r)
    {
        if (tail != head)
        {
            this.chainEdges.Add((tail, head, r, this.chainFirst[tail]));
            this.chainFirst[tail] = this.chainEdges.Count - 1;
        }
    }

    // A chain's state: a Fixed Origin's, or a Dependent Origin's under a path condition, formed once.
    private int ChainState(int node, ulong condition)
    {
        if (!this.chainStateIndex.TryGetValue((node, condition), out var state))
        {
            this.chainStateIndex.Add((node, condition), state = this.chainStates.Count);
            this.chainStates.Add((node, condition));
        }

        return state;
    }

    // A chain's step from state s along edge e, into its head's state under s's condition with the row's (a Fixed head's own state); an
    // edge between two Fixed Origins is judged at once under its row's Condition.
    private void ChainStep(int s, int e)
    {
        var (tail, head, row, _) = this.chainEdges[e];
        var condition = this.chainStates[s].Condition | this.unboundRows[row].Obligation.Condition;
        var to = this.ChainState(head, this.chainKinds[head] == OriginAtomKind.Fixed ? 0 : condition);
        if (!this.chains.Joins(s, this.chainKinds[tail], to, this.chainKinds[head], e))
        {
            _ = this.JudgeChainPair(row, tail, head, condition);
        }
    }

    // S >= X by the position judge at row r's use under a condition; true when it recorded.
    private bool JudgeChainPair(int r, int longer, int shorter, ulong condition)
    {
        var obligation = this.unboundRows[r].Obligation;
        var from = this.chainNodes[longer];
        var to = this.chainNodes[shorter];
        var records = this.callRelationScratch.Count;
        this.JudgeCallPosition(obligation.Use, from, to, equality: false, null, null, wellFormed: obligation.WellFormed, condition: condition);
        return this.callRelationScratch.Count != records;
    }

    // A Fixed target's last edges, each with the fixed sources its tail carries under the tail's condition with the row's.
    private void ChainEnds(int target)
    {
        foreach (var x in this.chains.InEdges(target))
        {
            var (tail, head, row, _) = this.chainEdges[this.chains.Label(x)];
            var condition = this.chainStates[this.chains.Tail(x)].Condition | this.unboundRows[row].Obligation.Condition;
            var carried = this.chains.Carried(x);
            for (var w = 0; w < carried.Length; w++)
            {
                for (var word = carried[w]; word != 0; word &= word - 1)
                {
                    var source = this.chainStates[this.chains.FixedNode((w << 6) + BitOperations.TrailingZeroCount(word))].Node;
                    this.ChainEnd((row, tail, head, source), condition);
                }
            }
        }
    }

    // A source at a last edge under a path condition, kept once per condition and linked with its key's others; S >= S holds.
    private void ChainEnd((int Row, int Tail, int Head, int Source) key, ulong condition)
    {
        if (key.Source == key.Head)
        {
            return;
        }

        if (!this.chainEndHead.TryGetValue(key, out var first))
        {
            first = -1;
        }

        for (var at = first; at >= 0; at = this.chainEnds[at].Next)
        {
            if (this.chainEnds[at].Condition == condition)
            {
                return;
            }
        }

        this.chainEnds.Add((key, condition, first));
        this.chainEndHead[key] = this.chainEnds.Count - 1;
    }

    // S >= X at a last edge's use under one of its key's minimal conditions (none of the key's others narrower), until one records.
    private void JudgeChainEnd(int end)
    {
        var (key, condition, _) = this.chainEnds[end];
        var first = this.chainEndHead[key];
        for (var at = first; at >= 0; at = this.chainEnds[at].Next)
        {
            if (this.chainEnds[at].Condition != condition && (this.chainEnds[at].Condition & ~condition) == 0)
            {
                return;
            }
        }

        if (first != ChainRecorded && this.JudgeChainPair(key.Row, key.Source, key.Head, condition))
        {
            this.chainEndHead[key] = ChainRecorded;
        }
    }

    // A row's relation (TyCtxt.OriginChains.Relate): the canonical Origins as nodes, and the row's edges.
    private readonly struct DeclarationRelation(Binding owner, int row) : TyCtxt.IOriginChainRelation
    {
        public OriginAtomKind Kind(int node) => owner.chainKinds[node];

        public int Operands(int node) => owner.chainNodes[node] is { Kind: OriginKind.Intersection } meet ? meet.Operands.Count : 0;

        public int Operand(int node, int index) => owner.ChainNode(owner.chainNodes[node].Operands[index]);

        public void Related(int tail, int head) => owner.ChainEdge(tail, head, row);
    }

    // The pass's graph (TyCtxt.OriginChains.Chains): the chains' states as nodes, and the chains' verdicts.
    private readonly struct DeclarationGraph(Binding owner) : TyCtxt.IOriginChainJudge
    {
        public OriginAtomKind Kind(int node) => owner.chainKinds[owner.chainStates[node].Node];

        public bool Admits(long words) => true;

        // A declaration's graph has no Finite node (DeclarationAtom).
        public void Refuted(int edge, ReadOnlySpan<ulong> sources) => throw new InvalidOperationException("A declaration row has no finite Origin.");

        public void Unknown(int target) => owner.ChainEnds(target);
    }
}
