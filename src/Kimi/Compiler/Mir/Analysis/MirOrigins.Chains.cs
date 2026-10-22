// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// MirOrigins' judgment of one class (2a0-D §6.2, §6.3; D15, D16, D19): its edges (each active row, Longer -> Shorter and back for an equality,
// a meet expanded by MeetNode's rule; each containment edge), Tarjan's components over the inner nodes (Finite and Inferred), finite-source
// bitsets propagated in topological order, and a problem at each edge into a Fixed node per finite source reaching its tail; an edge with an
// Unclassed end forms nothing and gives the located limit. 2a-1b adds Unknown here.

using System.Numerics;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

internal sealed partial class MirOrigins
{
    private int[] tails = []; // The class's edges, by edge index: tail, head, site's Ref (or -1 for a row's Site) and row (-1: containment).
    private int[] heads = [];
    private int[] edgeRows = [];
    private SourceRef[] edgeSites = [];
    private int edgeCount;
    private int[] outStart = []; // The edges by tail (counting sort): node v's out-edges are outEdges[outStart[v]..outStart[v + 1]).
    private int[] outEdges = [];
    private int[] finite = []; // Per node: its finite-source bit, or -1.
    private int[] finiteNodes = []; // Per finite-source bit: its node.
    private int[] order = []; // Tarjan's state per node: visit order (0: unvisited), low link, component (-1: open).
    private int[] low = [];
    private int[] component = [];
    private int[] stack = [];
    private int[] frames = []; // Tarjan's call stack: node and next out-edge cursor, two ints per frame.
    private int[] members = []; // The nodes by component (counting sort), from memberStart.
    private int[] memberStart = [];
    private ulong[] sources = []; // Per component: the finite sources reaching it, words each.

    // A class's judgment: its edges, then the Refuted chains.
    private void JudgeClass(int c)
    {
        ref readonly var known = ref this.classes[c];
        this.edgeCount = 0;
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
                this.Limit(row.Site, known.Views); // 2a0-D §3 row 9: no edge; the located limit at the row.
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
                this.Limit(site, known.Views);
                continue;
            }

            this.Relate(edge.Tail, edge.Head, site, -1);
        }

        this.Refute(known.Views);
    }

    private bool Unclassed(int node) => this.slice.Atoms[node].Kind == OriginAtomKind.Unclassed;

    // An edge `longer -> shorter` by MeetNode's rule (2a0-D D15): a meet at the longer end gives an edge from each operand and, unless it is
    // Fixed, its own node's; a non-Fixed meet at the shorter end is its node, reached from each of its operands.
    private void Relate(int longer, int shorter, SourceRef site, int row)
    {
        var meet = this.slice.Atoms[longer];
        if (meet.Atom.Kind == OriginKind.Intersection)
        {
            var operands = meet.Atom.Operands;
            for (var i = 0; i < operands.Count; i++)
            {
                this.Join(types.OriginAtom(this.key, operands[i], site), shorter, site, row);
            }

            if (meet.Kind != OriginAtomKind.Fixed)
            {
                this.Join(longer, shorter, site, row);
            }
        }
        else
        {
            this.Join(longer, shorter, site, row);
        }

        var target = this.slice.Atoms[shorter];
        if (target.Atom.Kind == OriginKind.Intersection && target.Kind != OriginAtomKind.Fixed)
        {
            var operands = target.Atom.Operands;
            for (var i = 0; i < operands.Count; i++)
            {
                this.Join(types.OriginAtom(this.key, operands[i], site), shorter, site, row);
            }
        }
    }

    private void Join(int tail, int head, SourceRef site, int row)
    {
        if (tail == head)
        {
            return;
        }

        var at = this.edgeCount++;
        MirDataflow.Grow(ref this.tails, this.edgeCount);
        MirDataflow.Grow(ref this.heads, this.edgeCount);
        MirDataflow.Grow(ref this.edgeRows, this.edgeCount);
        MirDataflow.Grow(ref this.edgeSites, this.edgeCount);
        (this.tails[at], this.heads[at], this.edgeRows[at], this.edgeSites[at]) = (tail, head, row, site);
    }

    // SPEC 15.6.5 (2a0-D D16): a finite source reaching the tail of an edge into a Fixed node must outlive it, which a finite Origin never
    // does: one problem per source and edge, Reason (source, fixed end, false). Sources propagate over Tarjan's components of the inner nodes
    // in topological order, 64 per word; a Fixed node passes none on.
    private void Refute(ulong views)
    {
        var atoms = this.slice.Atoms;
        var nodes = atoms.Count;
        MirDataflow.Grow(ref this.finite, nodes);
        var count = 0;
        for (var v = 0; v < nodes; v++)
        {
            this.finite[v] = atoms[v].Kind == OriginAtomKind.Finite ? count++ : -1;
            if (this.finite[v] >= 0)
            {
                MirDataflow.Grow(ref this.finiteNodes, count);
                this.finiteNodes[count - 1] = v;
            }
        }

        this.Work += nodes + this.edgeCount;
        if (count == 0 || this.edgeCount == 0)
        {
            return;
        }

        this.IndexEdges(nodes);
        var components = this.Components(nodes);
        var words = (count + 63) >> 6;
        MirDataflow.Grow(ref this.sources, components * words);
        this.sources.AsSpan(0, components * words).Clear();
        for (var k = components - 1; k >= 0; k--)
        {
            var reached = this.sources.AsSpan(k * words, words);
            for (var m = this.memberStart[k]; m < this.memberStart[k + 1]; m++)
            {
                if (this.finite[this.members[m]] is var bit and >= 0)
                {
                    reached[bit >> 6] |= 1UL << bit;
                }
            }

            for (var m = this.memberStart[k]; m < this.memberStart[k + 1]; m++)
            {
                var tail = this.members[m];
                for (var e = this.outStart[tail]; e < this.outStart[tail + 1]; e++)
                {
                    var head = this.heads[this.outEdges[e]];
                    this.Work += words;
                    if (this.component[head] >= 0 && this.component[head] != k)
                    {
                        var into = this.sources.AsSpan(this.component[head] * words, words);
                        for (var w = 0; w < words; w++)
                        {
                            into[w] |= reached[w];
                        }
                    }
                    else if (atoms[head].Kind == OriginAtomKind.Fixed)
                    {
                        this.Refuted(this.outEdges[e], reached, views);
                    }
                }
            }
        }
    }

    // The edges by tail, a counting sort: node v's out-edges are outEdges[outStart[v]..outStart[v + 1]).
    private void IndexEdges(int nodes)
    {
        MirDataflow.Grow(ref this.outStart, nodes + 1);
        MirDataflow.Grow(ref this.outEdges, this.edgeCount);
        this.outStart.AsSpan(0, nodes + 1).Clear();
        for (var e = 0; e < this.edgeCount; e++)
        {
            this.outStart[this.tails[e] + 1]++;
        }

        for (var v = 0; v < nodes; v++)
        {
            this.outStart[v + 1] += this.outStart[v];
        }

        MirDataflow.Grow(ref this.low, nodes); // Scratch for the insertion cursors.
        this.outStart.AsSpan(0, nodes).CopyTo(this.low);
        for (var e = 0; e < this.edgeCount; e++)
        {
            this.outEdges[this.low[this.tails[e]]++] = e;
        }
    }

    // Tarjan's strongly connected components of the inner nodes (Finite, Inferred) over the edges between them, iteratively: component ids in
    // completion order, so every edge between components goes from a higher id to a lower one; returns the count and fills the members by
    // component. Other nodes keep component -1.
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

        static bool Inner(OriginAtomKind kind) => kind is OriginAtomKind.Finite or OriginAtomKind.Inferred;
    }

    // An edge into a Fixed node with the finite sources reaching its tail: one problem per source, at the edge's site, with its facts.
    private void Refuted(int edge, ReadOnlySpan<ulong> reached, ulong views)
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
                this.pending.Add(new(DiagnosticCode.UnsatisfiedOriginRelation_Kd, requirement, this.edgeSites[edge], reason, views, start, count));
            }
        }
    }

    // The located limit at a row or Ref with an Unclassed end (2a0-D §3 row 9, D19), whatever Binding's completeness (DECISIONS R4-M113).
    private void Limit(SourceRef site, ulong views)
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported);
        this.pending.Add(new(DiagnosticCode.Unsupported_Kd, requirement, site, default, views, this.pendingFacts.Count, 0));
    }
}
