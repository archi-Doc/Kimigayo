// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// MirOrigins' judgment of one class (2a0-D §6.2-§6.4; D15-D17, D19, D20), A5's adapter of R4-M118's chain judgment (TyCtxt.OriginChains, PLAN
// G318): its edges (each active row, Longer -> Shorter and back for an equality, a meet expanded by MeetNode's rule, a shorter meet's
// operand edges once per class; each containment edge; the meet law's, MirOrigins.Meets.cs); an edge between two Fixed Origins judged at
// once, one oracle test (an Equality row failing both ways is one problem); the chains through the inner nodes (OriginChains); and at each
// edge into a Fixed node a problem per finite source (Refuted) and per fixed source the oracle does not derive (Unknown), the oracle closed
// once per target and part under the class's borrow binders. An edge of the layer's part (a layer row, or an edge between a Field
// initializer's atoms) is judged with the layer's oracle, the Type's public premises (DECISIONS R4-M101 E4, R4-M110 Q1 (a), R4-M114); a row
// or containment edge with an Unclassed end forms nothing and is the located limit. A Dependent node, an end a Binding failure left Unbound
// or an unclassified Closure's pending atom (DECISIONS R4-M126 §2.5 (c)), is never a source or a judged target: it passes on what reaches
// it, so a chain through it is judged between its ends and one ending at it records nothing (SPEC 23.1.2; DECISIONS R4-M118). A failing
// chain with an end the view marks an enclosing body's call region is the located limit instead of its record (NestedLimit).

using System.Numerics;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

internal sealed partial class MirOrigins
{
    private readonly TyCtxt.OriginChains chains = new();
    private ulong classViews; // The judged class's views and the borrow binders they all share, the oracle's condition (SPEC 15.6.5).
    private ulong condition;
    private long arenaWords; // The last arena the chains asked for, with the meet law's words (D20).
    private (int Tail, int Head, int Row, int Pending) lastDirect; // The last failed direct edge and its problem's index.
    private ClassEdge[] classEdges = []; // A class's edges by MeetNode's rule, in order (ClassEdges), reused.
    private int classEdgeCount;

    // A class's judgment: its edges, then its chains; false when its arena is over the limit, so the stratum skips the body.
    private bool JudgeClass(int c, MirReport report)
    {
        ref readonly var known = ref this.classes[c];
        (this.classViews, this.condition, this.lastDirect) = (known.Views, known.Borrows, (-1, -1, -1, -1));
        if (!this.ClassEdges(c))
        {
            this.StorageLimit(report, this.meetWords);
            return false;
        }

        var atoms = this.slice.Atoms;
        for (var i = 0; i < this.classEdgeCount; i++)
        {
            var edge = this.classEdges[i];
            if (edge.Tail < 0)
            {
                this.Limit(edge.Site); // 2a0-D §3 row 9: no edge; the located limit at the row or Ref.
                continue;
            }

            if (!this.chains.Joins(edge.Tail, atoms[edge.Tail].Kind, edge.Head, atoms[edge.Head].Kind, i))
            {
                this.Direct(edge.Tail, edge.Head, edge.Site, edge.Row);
            }
        }

        var judge = new ChainJudge(this, default, -1);
        if (this.chains.Chains(ref judge, atoms.Count))
        {
            return true;
        }

        this.StorageLimit(report, this.arenaWords);
        return false;
    }

    // A class's edges, in order (classEdges): each active row's, Longer -> Shorter and back for an equality, then each containment edge's
    // (row -1), by MeetNode's rule (OriginChains.Relate); one with an Unclassed end forms none and is an entry with Tail -1 at its site; then
    // the meet law's (MirOrigins.Meets.cs). False when the law's words are over the limit (D20).
    private bool ClassEdges(int c)
    {
        ref readonly var known = ref this.classes[c];
        this.classEdgeCount = 0;
        this.chains.Begin();
        var rows = this.slice.Rows;
        var bits = this.rowBits.AsSpan(c * this.rowWords, this.rowWords);
        for (var r = 0; r < rows.Count; r++)
        {
            if ((bits[r >> 6] & (1UL << r)) == 0)
            {
                continue;
            }

            var row = rows[r];
            if (this.Unclassed(row.Longer) || this.Unclassed(row.Shorter))
            {
                this.Related(-1, -1, row.Site, r);
                continue;
            }

            var judge = new ChainJudge(this, row.Site, r);
            this.chains.Relate(ref judge, row.Longer, row.Shorter);
            if (row.Equality)
            {
                this.chains.Relate(ref judge, row.Shorter, row.Longer);
            }
        }

        foreach (var edge in this.contained.AsSpan(known.Start, known.Count))
        {
            var site = this.refs[edge.Ref].Site;
            if (this.Unclassed(edge.Tail) || this.Unclassed(edge.Head))
            {
                this.Related(-1, -1, site, -1);
                continue;
            }

            var judge = new ChainJudge(this, site, -1);
            this.chains.Relate(ref judge, edge.Tail, edge.Head);
        }

        return this.MeetLaw();
    }

    private void Related(int tail, int head, SourceRef site, int row)
    {
        MirDataflow.Grow(ref this.classEdges, this.classEdgeCount + 1);
        this.classEdges[this.classEdgeCount++] = new(tail, head, site, row);
    }

    private bool Unclassed(int node) => this.slice.Atoms[node].Kind == OriginAtomKind.Unclassed;

    // An edge's part (DECISIONS R4-M101 E4, R4-M114): a row's by its index, a containment edge's by its atoms, the layer's when either is the
    // layer's (a Field initializer's Ref); Fixed atoms are shared.
    private bool InLayer(int tail, int head, int row) => row >= 0 ? row >= this.slice.LayerStart : this.slice.InLayer(tail) || this.slice.InLayer(head);

    // A meet's operand count and, resolved at a site, its operand's node (OriginChains.Relate, MeetNode's rule).
    private int MeetOperands(int node) => this.slice.Atoms[node].Atom is { Kind: OriginKind.Intersection } meet ? meet.Operands.Count : 0;

    private int MeetOperand(int node, int index, SourceRef site) => types.OriginAtom(this.key, this.slice.Atoms[node].Atom.Operands[index], site);

    // SPEC 15.6.5: a relation between two fixed Origins is Proven only when the premises derive it, one oracle test under the class's
    // condition; an Equality row failing in both directions is one problem with Equality set, the second direction failing right after the
    // first (2a0-D §6.3).
    private void Direct(int tail, int head, SourceRef site, int row)
    {
        var atoms = this.slice.Atoms;
        var oracle = this.Oracle(this.InLayer(tail, head, row));
        oracle.Close(atoms[head].Atom, this.condition);
        this.work += 2;
        if (oracle.Outlives(atoms[tail].Atom))
        {
            return;
        }

        if (row >= 0 && this.lastDirect == (head, tail, row, this.pending.Count - 1))
        {
            var first = this.pending[^1];
            this.pending[^1] = first with { Reason = first.Reason with { Equality = true } };
            this.lastDirect = (-1, -1, -1, -1);
            return;
        }

        this.lastDirect = this.Unproven(tail, head, site, row) ? (tail, head, row, this.pending.Count - 1) : (-1, -1, -1, -1);
    }

    // D20: whether the class's arena and the meet law's words are within the analyses' byte limit (JudgeClass records the limit).
    private bool Admits(long words)
    {
        this.arenaWords = words + this.meetWords;
        return MirDataflow.Within(this.arenaWords);
    }

    // SPEC 15.6.5 (2a0-D D17): the fixed sources reaching the tails of g's in-edges of one part, each tested once with that part's oracle
    // closed at g under the class's condition (the mask over the sources, computed for those that reach g); each edge reports the failing
    // sources its tail carries, Reason (source, g, false). Every tail is inner: an edge between Fixed ends was judged when formed.
    private void Unknown(int g, bool layer)
    {
        var failing = this.chains.Failing();
        var words = failing.Length;
        failing.Clear();
        foreach (var e in this.chains.InEdges(g))
        {
            if (this.InLayer(this.chains.Tail(e), g, this.classEdges[this.chains.Label(e)].Row) == layer)
            {
                MirDataflow.Union(failing, this.chains.Carried(e));
                this.work += words;
            }
        }

        if (failing.IndexOfAnyExcept(0UL) < 0)
        {
            return;
        }

        var atoms = this.slice.Atoms;
        var oracle = this.Oracle(layer);
        oracle.Close(atoms[g].Atom, this.condition);
        this.work += words + 1;
        for (var w = 0; w < words; w++)
        {
            for (var word = failing[w]; word != 0; word &= word - 1)
            {
                var bit = BitOperations.TrailingZeroCount(word);
                this.work++;
                if (oracle.Outlives(atoms[this.chains.FixedNode((w << 6) + bit)].Atom))
                {
                    failing[w] &= ~(1UL << bit);
                }
            }
        }

        foreach (var e in this.chains.InEdges(g))
        {
            var edge = this.classEdges[this.chains.Label(e)];
            if (this.InLayer(this.chains.Tail(e), g, edge.Row) != layer)
            {
                continue;
            }

            var carried = this.chains.Carried(e);
            this.work += words;
            for (var w = 0; w < words; w++)
            {
                for (var word = carried[w] & failing[w]; word != 0; word &= word - 1)
                {
                    this.Unproven(this.chains.FixedNode((w << 6) + BitOperations.TrailingZeroCount(word)), g, edge.Site, edge.Row);
                }
            }
        }
    }

    // An edge into a Fixed node with the finite sources reaching its tail: one problem per source, at the edge's site, with its facts.
    private void Refuted(int e, ReadOnlySpan<ulong> reached)
    {
        var atoms = this.slice.Atoms;
        var edge = this.classEdges[this.chains.Label(e)];
        var head = this.chains.Head(e);
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin);
        for (var w = 0; w < reached.Length; w++)
        {
            for (var word = reached[w]; word != 0; word &= word - 1)
            {
                var source = this.chains.FiniteNode((w << 6) + BitOperations.TrailingZeroCount(word));
                if (this.NestedLimit(source, head, edge.Site))
                {
                    continue;
                }

                var start = this.pendingFacts.Count;
                this.pendingFacts.Add(new(ProblemFactKind.Relation, edge.Site, edge.Row));
                if (source < this.anchorCount && this.anchors[source] >= 0)
                {
                    this.pendingFacts.Add(new(ProblemFactKind.Anchor, this.refs[this.anchors[source]].Site));
                }

                var reason = new MirReason(atoms[source].Atom, atoms[head].Atom, Equality: false);
                var count = this.pendingFacts.Count - start;
                this.pending.Add(new(DiagnosticCode.UnsatisfiedOriginRelation_Kd, requirement, edge.Site, reason, this.classViews, start, count));
            }
        }
    }

    // A fixed source the target's premises do not derive, at the last edge's site with its row (SPEC 15.6.5, 23.1.2; 2a0-D D18); false
    // when it is the nested-body limit instead.
    private bool Unproven(int source, int target, SourceRef site, int row)
    {
        if (this.NestedLimit(source, target, site))
        {
            return false;
        }

        var atoms = this.slice.Atoms;
        var start = this.pendingFacts.Count;
        this.pendingFacts.Add(new(ProblemFactKind.Relation, site, row));
        var reason = new MirReason(atoms[source].Atom, atoms[target].Atom, Equality: false);
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin);
        this.pending.Add(new(DiagnosticCode.UnprovenOriginRelation_Kd, requirement, site, reason, this.classViews, start, 1));
        return true;
    }

    // The nested-body limit (U3-design twelfth amendment, N4): a failing chain with an end the view marks an enclosing body's call region,
    // which only the enclosing body's rows decide, is the located limit at the chain's site, never a Language record.
    private bool NestedLimit(int source, int target, SourceRef site)
    {
        var atoms = this.slice.Atoms;
        if (!atoms[source].EnclosingCallRegion && !atoms[target].EnclosingCallRegion)
        {
            return false;
        }

        this.Limit(site);
        return true;
    }

    // The located limit at a row or Ref with an Unclassed end (2a0-D §3 row 9, D19), whatever Binding's completeness (DECISIONS R4-M113).
    private void Limit(SourceRef site)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
        this.pending.Add(new(DiagnosticCode.Unsupported_Kd, requirement, site, default, this.classViews, this.pendingFacts.Count, 0));
    }

    // D20 (R4-M15a): a class's arena over the analyses' byte limit is OwnershipStorageLimit_Kd once at the body in every view, with its
    // arguments (MirDataflow.StorageLimit, G213), and the stratum skips the body, its other Origin problems dropped.
    private void StorageLimit(MirReport report, long words)
    {
        this.pending.Clear();
        this.pendingFacts.Clear();
        MirDataflow.StorageLimit(report, this.body, this.allViews, MirStorageTable.Origins, words);
    }

    // A5's judge of a class's graph (OriginChains): the view's atoms as nodes, a relation's edges into classEdges at its site and row, and
    // the class's verdicts.
    private readonly struct ChainJudge(MirOrigins owner, SourceRef site, int row) : TyCtxt.IOriginChainRelation, TyCtxt.IOriginChainJudge
    {
        public OriginAtomKind Kind(int node) => owner.slice.Atoms[node].Kind;

        public int Operands(int node) => owner.MeetOperands(node);

        public int Operand(int node, int index) => owner.MeetOperand(node, index, site);

        public void Related(int tail, int head) => owner.Related(tail, head, site, row);

        public bool Admits(long words) => owner.Admits(words);

        public void Refuted(int edge, ReadOnlySpan<ulong> sources) => owner.Refuted(edge, sources);

        public void Unknown(int target)
        {
            owner.Unknown(target, false);
            owner.Unknown(target, true);
        }
    }
}
