// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// MirOrigins' judgment of one class (2a0-D §6.2-§6.4; D15-D17, D19, D20): its edges (each active row, Longer -> Shorter and back for an
// equality, a meet expanded by MeetNode's rule; each containment edge); an edge between two Fixed Origins judged at once, one oracle test
// (an Equality row failing both ways is one problem); Tarjan's components over the inner nodes (Finite, Inferred and Dependent);
// finite-source and then fixed-source bitsets propagated in topological order through one pooled arena that MirDataflow.Within admits; and
// at each edge into a Fixed node a problem per finite source (Refuted) and per fixed source the oracle does not derive (Unknown), the oracle
// closed once per target and part. An edge of the layer's part (a layer row, or an edge between a Field initializer's atoms) is judged with
// the layer's oracle, the Type's public premises (DECISIONS R4-M101 E4, R4-M110 Q1 (a), R4-M114); a row or containment edge with an
// Unclassed end forms nothing and is the located limit. A Dependent node, an end a Binding failure left Unbound or an unclassified
// Closure's pending atom (DECISIONS R4-M126 §2.5 (c)), is never a source or a judged target: it passes on what reaches it, so a chain
// through it is judged between its ends and one ending at it records nothing (SPEC 23.1.2; DECISIONS R4-M118).

using System.Numerics;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

internal sealed partial class MirOrigins
{
    private int[] tails = []; // The class's edges between non-Fixed or mixed ends, by edge index: tail, head, site and row (-1: containment).
    private int[] heads = [];
    private int[] edgeRows = [];
    private SourceRef[] edgeSites = [];
    private int edgeCount;
    private int[] outStart = []; // The edges by tail and by head (counting sorts): node v's out-edges are outEdges[outStart[v]..outStart[v + 1]).
    private int[] outEdges = [];
    private int[] inStart = [];
    private int[] inEdges = [];
    private int[] finite = []; // Per node: its finite-source bit, or -1.
    private int[] finiteNodes = []; // Per finite-source bit: its node.
    private int[] fixedBits = []; // Per node: its fixed-source bit (a Fixed tail of an edge), or -1.
    private int[] fixedNodes = []; // Per fixed-source bit: its node.
    private int[] order = []; // Tarjan's state per node: visit order (0: unvisited), low link, component (-1: open).
    private int[] low = [];
    private int[] component = [];
    private int[] stack = [];
    private int[] frames = []; // Tarjan's call stack: node and next out-edge cursor, two ints per frame.
    private int[] members = []; // The nodes by component (counting sort), from memberStart.
    private int[] memberStart = [];
    private ulong[] arena = []; // Each component's sources, one pass's words each, then the failing sources of the target being judged.
    private ulong classViews; // The judged class's views and the borrow binders they all share, the oracle's condition (SPEC 15.6.5).
    private ulong condition;
    private (int Tail, int Head, int Row, int Pending) lastDirect; // The last failed direct edge and its problem's index.
    private ClassEdge[] classEdges = []; // A class's edges by MeetNode's rule, in order (ClassEdges), reused.
    private int classEdgeCount;

    /// <summary>Gets the words of a class's arena (2a0-D §6.4, D20): each component's sources, the wider pass's words, and the fixed sources
    /// failing the target being judged.</summary>
    /// <param name="components">The inner components.</param>
    /// <param name="finiteSources">The finite sources.</param>
    /// <param name="fixedSources">The fixed sources.</param>
    /// <returns>The words.</returns>
    internal static long ArenaWords(int components, int finiteSources, int fixedSources)
        => ((long)components * Math.Max((finiteSources + 63) >> 6, (fixedSources + 63) >> 6)) + ((fixedSources + 63) >> 6);

    // A class's judgment: its edges, then its chains; false when its arena is over the limit, so the stratum skips the body.
    private bool JudgeClass(int c, MirReport report)
    {
        ref readonly var known = ref this.classes[c];
        (this.classViews, this.condition, this.edgeCount, this.lastDirect) = (known.Views, known.Borrows, 0, (-1, -1, -1, -1));
        foreach (var edge in this.ClassEdges(c))
        {
            if (edge.Tail < 0)
            {
                this.Limit(edge.Site); // 2a0-D §3 row 9: no edge; the located limit at the row or Ref.
                continue;
            }

            this.Join(edge.Tail, edge.Head, edge.Site, edge.Row);
        }

        return this.Chains(report);
    }

    // A class's edges, in order (classEdges): each active row's, Longer -> Shorter and back for an equality, then each containment edge's
    // (row -1), by MeetNode's rule (Relate); one with an Unclassed end forms none and is an entry with Tail -1 at its site.
    private ReadOnlySpan<ClassEdge> ClassEdges(int c)
    {
        ref readonly var known = ref this.classes[c];
        this.classEdgeCount = 0;
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

            this.Relate(row.Longer, row.Shorter, row.Site, r);
            if (row.Equality)
            {
                this.Relate(row.Shorter, row.Longer, row.Site, r);
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

            this.Relate(edge.Tail, edge.Head, site, -1);
        }

        return this.classEdges.AsSpan(0, this.classEdgeCount);
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

    // An edge `longer -> shorter` by MeetNode's rule (2a0-D D15), into classEdges: a meet at the longer end gives an edge from each operand
    // and, unless it is Fixed, its own node's; a non-Fixed meet at the shorter end is its node, reached from each of its operands.
    private void Relate(int longer, int shorter, SourceRef site, int row)
    {
        var meet = this.slice.Atoms[longer];
        if (meet.Atom.Kind == OriginKind.Intersection)
        {
            var operands = meet.Atom.Operands;
            for (var i = 0; i < operands.Count; i++)
            {
                this.Related(types.OriginAtom(this.key, operands[i], site), shorter, site, row);
            }

            if (meet.Kind != OriginAtomKind.Fixed)
            {
                this.Related(longer, shorter, site, row);
            }
        }
        else
        {
            this.Related(longer, shorter, site, row);
        }

        var target = this.slice.Atoms[shorter];
        if (target.Atom.Kind == OriginKind.Intersection && target.Kind != OriginAtomKind.Fixed)
        {
            var operands = target.Atom.Operands;
            for (var i = 0; i < operands.Count; i++)
            {
                this.Related(types.OriginAtom(this.key, operands[i], site), shorter, site, row);
            }
        }
    }

    // An edge of the graph; one between two Fixed Origins passes nothing on, so it is judged at once instead (SPEC 15.6.5; 2a0-D §6.3).
    private void Join(int tail, int head, SourceRef site, int row)
    {
        if (tail == head)
        {
            return;
        }

        var atoms = this.slice.Atoms;

        if (atoms[tail].Kind == OriginAtomKind.Fixed && atoms[head].Kind == OriginAtomKind.Fixed)
        {
            this.Direct(tail, head, site, row);
            return;
        }

        var at = this.edgeCount++;
        MirDataflow.Grow(ref this.tails, this.edgeCount);
        MirDataflow.Grow(ref this.heads, this.edgeCount);
        MirDataflow.Grow(ref this.edgeRows, this.edgeCount);
        MirDataflow.Grow(ref this.edgeSites, this.edgeCount);
        (this.tails[at], this.heads[at], this.edgeRows[at], this.edgeSites[at]) = (tail, head, row, site);
    }

    // SPEC 15.6.5: a relation between two fixed Origins is Proven only when the premises derive it, one oracle test under the class's
    // condition; an Equality row failing in both directions is one problem with Equality set, the second direction failing right after the
    // first (2a0-D §6.3).
    private void Direct(int tail, int head, SourceRef site, int row)
    {
        var atoms = this.slice.Atoms;
        var oracle = this.Oracle(this.InLayer(tail, head, row));
        oracle.Close(atoms[head].Atom, this.condition);
        this.Work += 2;
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

        this.Unproven(tail, head, site, row);
        this.lastDirect = (tail, head, row, this.pending.Count - 1);
    }

    // SPEC 15.6.5 (2a0-D D16, D17, D20): over Tarjan's components of the inner nodes, finite sources and then fixed sources propagate in
    // topological order, 64 per word, through one arena MirDataflow.Within admits; a Fixed node passes none on. False when the arena is over
    // the limit.
    private bool Chains(MirReport report)
    {
        var atoms = this.slice.Atoms;
        var nodes = atoms.Count;
        MirDataflow.Grow(ref this.finite, nodes);
        MirDataflow.Grow(ref this.fixedBits, nodes);
        var (finiteCount, fixedCount) = (0, 0);
        for (var v = 0; v < nodes; v++)
        {
            (this.finite[v], this.fixedBits[v]) = (atoms[v].Kind == OriginAtomKind.Finite ? finiteCount++ : -1, -1);
            if (this.finite[v] >= 0)
            {
                MirDataflow.Grow(ref this.finiteNodes, finiteCount);
                this.finiteNodes[finiteCount - 1] = v;
            }
        }

        for (var e = 0; e < this.edgeCount; e++)
        {
            if (this.tails[e] is var tail && atoms[tail].Kind == OriginAtomKind.Fixed && this.fixedBits[tail] < 0)
            {
                this.fixedBits[tail] = fixedCount++;
                MirDataflow.Grow(ref this.fixedNodes, fixedCount);
                this.fixedNodes[fixedCount - 1] = tail;
            }
        }

        this.Work += nodes + this.edgeCount;
        if (this.edgeCount == 0)
        {
            return true;
        }

        this.IndexEdges(this.tails, nodes, ref this.outStart, ref this.outEdges);
        var components = this.Components(nodes);
        var words = ArenaWords(components, finiteCount, fixedCount);
        if (!MirDataflow.Within(words))
        {
            this.StorageLimit(report, words);
            return false;
        }

        MirDataflow.Grow(ref this.arena, (int)words);
        if (finiteCount > 0)
        {
            this.Refute(this.Propagate(this.finite, finiteCount, components), components);
        }

        if (fixedCount > 0)
        {
            var fixedWords = this.Propagate(this.fixedBits, fixedCount, components);
            this.IndexEdges(this.heads, nodes, ref this.inStart, ref this.inEdges);
            for (var g = 0; g < nodes; g++)
            {
                if (atoms[g].Kind == OriginAtomKind.Fixed && this.inStart[g] < this.inStart[g + 1])
                {
                    this.Unknown(g, false, fixedWords, components);
                    this.Unknown(g, true, fixedWords, components);
                }
            }
        }

        return true;
    }

    // One pass's sources per component, in the arena: an inner node's own bit, a non-inner tail's bit into its head's component, then each
    // component's sources into those of the components its edges reach, highest component first (every edge between components goes from a
    // higher id to a lower one); returns the words per component.
    private int Propagate(int[] bit, int count, int components)
    {
        var words = (count + 63) >> 6;
        this.arena.AsSpan(0, components * words).Clear();
        for (var e = 0; e < this.edgeCount; e++)
        {
            if (this.component[this.tails[e]] < 0 && bit[this.tails[e]] is var b and >= 0)
            {
                this.arena[(this.component[this.heads[e]] * words) + (b >> 6)] |= 1UL << b;
            }
        }

        this.Work += this.edgeCount;
        for (var k = components - 1; k >= 0; k--)
        {
            var reached = this.arena.AsSpan(k * words, words);
            for (var m = this.memberStart[k]; m < this.memberStart[k + 1]; m++)
            {
                if (bit[this.members[m]] is var b and >= 0)
                {
                    reached[b >> 6] |= 1UL << b;
                }
            }

            for (var m = this.memberStart[k]; m < this.memberStart[k + 1]; m++)
            {
                var tail = this.members[m];
                for (var e = this.outStart[tail]; e < this.outStart[tail + 1]; e++)
                {
                    var into = this.component[this.heads[this.outEdges[e]]];
                    this.Work += words;
                    if (into >= 0 && into != k)
                    {
                        MirDataflow.Union(this.arena.AsSpan(into * words, words), reached);
                    }
                }
            }
        }

        return words;
    }

    // SPEC 15.6.5 (2a0-D D16): a finite source reaching the tail of an edge into a Fixed node must outlive it, which a finite Origin never
    // does: one problem per source and edge, Reason (source, fixed end, false).
    private void Refute(int words, int components)
    {
        var atoms = this.slice.Atoms;
        for (var k = components - 1; k >= 0; k--)
        {
            for (var m = this.memberStart[k]; m < this.memberStart[k + 1]; m++)
            {
                var tail = this.members[m];
                for (var e = this.outStart[tail]; e < this.outStart[tail + 1]; e++)
                {
                    if (atoms[this.heads[this.outEdges[e]]].Kind == OriginAtomKind.Fixed)
                    {
                        this.Refuted(this.outEdges[e], this.arena.AsSpan(k * words, words));
                    }
                }
            }
        }
    }

    // SPEC 15.6.5 (2a0-D D17): the fixed sources reaching the tails of g's in-edges of one part, each tested once with that part's oracle
    // closed at g under the class's condition (the mask over the sources, computed for those that reach g); each edge reports the failing
    // sources its tail carries, Reason (source, g, false). Every tail is inner: an edge between Fixed ends was judged when formed.
    private void Unknown(int g, bool layer, int words, int components)
    {
        var failing = this.arena.AsSpan(components * words, words);
        failing.Clear();
        for (var i = this.inStart[g]; i < this.inStart[g + 1]; i++)
        {
            var e = this.inEdges[i];
            if (this.InLayer(this.tails[e], g, this.edgeRows[e]) == layer)
            {
                MirDataflow.Union(failing, this.arena.AsSpan(this.component[this.tails[e]] * words, words));
                this.Work += words;
            }
        }

        if (failing.IndexOfAnyExcept(0UL) < 0)
        {
            return;
        }

        var atoms = this.slice.Atoms;
        var oracle = this.Oracle(layer);
        oracle.Close(atoms[g].Atom, this.condition);
        this.Work += words + 1;
        for (var w = 0; w < words; w++)
        {
            for (var word = failing[w]; word != 0; word &= word - 1)
            {
                var bit = BitOperations.TrailingZeroCount(word);
                this.Work++;
                if (oracle.Outlives(atoms[this.fixedNodes[(w << 6) + bit]].Atom))
                {
                    failing[w] &= ~(1UL << bit);
                }
            }
        }

        for (var i = this.inStart[g]; i < this.inStart[g + 1]; i++)
        {
            var e = this.inEdges[i];
            if (this.InLayer(this.tails[e], g, this.edgeRows[e]) != layer)
            {
                continue;
            }

            var carried = this.arena.AsSpan(this.component[this.tails[e]] * words, words);
            this.Work += words;
            for (var w = 0; w < words; w++)
            {
                for (var word = carried[w] & failing[w]; word != 0; word &= word - 1)
                {
                    this.Unproven(this.fixedNodes[(w << 6) + BitOperations.TrailingZeroCount(word)], g, this.edgeSites[e], this.edgeRows[e]);
                }
            }
        }
    }

    // The edges by a key (their tails or their heads), a counting sort: node v's edges are index[start[v]..start[v + 1]), in edge order.
    private void IndexEdges(int[] keys, int nodes, ref int[] start, ref int[] index)
    {
        MirDataflow.Grow(ref start, nodes + 1);
        MirDataflow.Grow(ref index, this.edgeCount);
        start.AsSpan(0, nodes + 1).Clear();
        for (var e = 0; e < this.edgeCount; e++)
        {
            start[keys[e] + 1]++;
        }

        for (var v = 0; v < nodes; v++)
        {
            start[v + 1] += start[v];
        }

        MirDataflow.Grow(ref this.low, nodes); // Scratch for the insertion cursors.
        start.AsSpan(0, nodes).CopyTo(this.low);
        for (var e = 0; e < this.edgeCount; e++)
        {
            index[this.low[keys[e]]++] = e;
        }
    }

    // Tarjan's strongly connected components of the inner nodes (Finite, Inferred, Dependent) over the edges between them, iteratively:
    // component ids in completion order, so every edge between components goes from a higher id to a lower one; returns the count and fills
    // the members by component. Other nodes keep component -1.
    private int Components(int nodes)
    {
        var atoms = this.slice.Atoms;
        MirDataflow.Grow(ref this.order, nodes);
        MirDataflow.Grow(ref this.low, nodes);
        MirDataflow.Grow(ref this.component, nodes);
        MirDataflow.Grow(ref this.stack, nodes);
        MirDataflow.Grow(ref this.frames, 2 * nodes);
        this.order.AsSpan(0, nodes).Clear();
        this.component.AsSpan(0, nodes).Fill(-1);
        var (visits, depth, components, top) = (0, 0, 0, 0);
        for (var root = 0; root < nodes; root++)
        {
            if (this.order[root] != 0 || !Inner(atoms[root].Kind))
            {
                continue;
            }

            this.order[root] = this.low[root] = ++visits;
            this.stack[depth++] = root;
            (this.frames[0], this.frames[1], top) = (root, this.outStart[root], 1);
            while (top > 0)
            {
                var v = this.frames[2 * (top - 1)];
                ref var cursor = ref this.frames[(2 * (top - 1)) + 1];
                if (cursor < this.outStart[v + 1])
                {
                    var w = this.heads[this.outEdges[cursor++]];
                    this.Work++;
                    if (!Inner(atoms[w].Kind))
                    {
                        continue;
                    }

                    if (this.order[w] == 0)
                    {
                        this.order[w] = this.low[w] = ++visits;
                        this.stack[depth++] = w;
                        (this.frames[2 * top], this.frames[(2 * top) + 1]) = (w, this.outStart[w]);
                        top++;
                    }
                    else if (this.component[w] < 0)
                    {
                        this.low[v] = Math.Min(this.low[v], this.order[w]);
                    }

                    continue;
                }

                if (this.low[v] == this.order[v])
                {
                    int w;
                    do
                    {
                        w = this.stack[--depth];
                        this.component[w] = components;
                    }
                    while (w != v);
                    components++;
                }

                if (--top > 0)
                {
                    var parent = this.frames[2 * (top - 1)];
                    this.low[parent] = Math.Min(this.low[parent], this.low[v]);
                }
            }
        }

        MirDataflow.Grow(ref this.memberStart, components + 1);
        MirDataflow.Grow(ref this.members, nodes);
        this.memberStart.AsSpan(0, components + 1).Clear();
        for (var v = 0; v < nodes; v++)
        {
            if (this.component[v] >= 0)
            {
                this.memberStart[this.component[v] + 1]++;
            }
        }

        for (var k = 0; k < components; k++)
        {
            this.memberStart[k + 1] += this.memberStart[k];
        }

        this.memberStart.AsSpan(0, components).CopyTo(this.order); // Scratch for the insertion cursors.
        for (var v = 0; v < nodes; v++)
        {
            if (this.component[v] >= 0)
            {
                this.members[this.order[this.component[v]]++] = v;
            }
        }

        return components;

        static bool Inner(OriginAtomKind kind) => kind is OriginAtomKind.Finite or OriginAtomKind.Inferred or OriginAtomKind.Dependent;
    }

    // An edge into a Fixed node with the finite sources reaching its tail: one problem per source, at the edge's site, with its facts.
    private void Refuted(int edge, ReadOnlySpan<ulong> reached)
    {
        var atoms = this.slice.Atoms;
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin);
        for (var w = 0; w < reached.Length; w++)
        {
            for (var word = reached[w]; word != 0; word &= word - 1)
            {
                var source = this.finiteNodes[(w << 6) + BitOperations.TrailingZeroCount(word)];
                var start = this.pendingFacts.Count;
                this.pendingFacts.Add(new(MirFactKind.Relation, this.edgeSites[edge], this.edgeRows[edge]));
                if (source < this.anchorCount && this.anchors[source] >= 0)
                {
                    this.pendingFacts.Add(new(MirFactKind.Anchor, this.refs[this.anchors[source]].Site));
                }

                var reason = new MirReason(atoms[source].Atom, atoms[this.heads[edge]].Atom, Equality: false);
                var count = this.pendingFacts.Count - start;
                this.pending.Add(new(DiagnosticCode.UnsatisfiedOriginRelation_Kd, requirement, this.edgeSites[edge], reason, this.classViews, start, count));
            }
        }
    }

    // A fixed source the target's premises do not derive, at the last edge's site with its row (SPEC 15.6.5, 23.1.2; 2a0-D D18).
    private void Unproven(int source, int target, SourceRef site, int row)
    {
        var atoms = this.slice.Atoms;
        var start = this.pendingFacts.Count;
        this.pendingFacts.Add(new(MirFactKind.Relation, site, row));
        var reason = new MirReason(atoms[source].Atom, atoms[target].Atom, Equality: false);
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin);
        this.pending.Add(new(DiagnosticCode.UnprovenOriginRelation_Kd, requirement, site, reason, this.classViews, start, 1));
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
}
