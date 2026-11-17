// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// TyCtxt.OriginChains: R4-M118's chain judgment of one Origin graph (SPEC 15.3.6, 15.6.5, 23.1.2; 2a0-D D15-D17, D20; DECISIONS R4-M94,
// R4-M118, BU-62; PLAN G318), a pure TyCtxt function family both judges read: A5's Origin stratum (MirOrigins) for a body's rows, and the
// declaration judge (Binding.OriginClauses.Chains.cs) for a declaration's. It reads no TyCtxt, Binding or MIR state; its one vocabulary is
// the C1-F4 view's OriginAtomKind (TyCtxt.Origins.cs).
// - Responsibility: MeetNode's rule for one relation (Relate: a meet at the longer end gives an edge from each operand and, unless it is
//   Fixed, its own; a non-Fixed meet at the shorter end is reached from each operand once per Begin, which A5 calls per class and the
//   declaration judge per row, so each operand edge carries its row's Condition); an edge between two Fixed nodes handed back to be
//   judged at once (Joins); Tarjan's components over the inner nodes (Finite, Inferred and Dependent); finite and then fixed sources
//   propagated in topological order, 64 per word, through one pooled arena; and every edge into a Fixed node handed to the judge with the
//   sources its tail carries (Refuted for the finite sources, Unknown per Fixed target for the fixed ones).
// - Inputs: a relation's ends, node kinds and meet operands (IOriginChainRelation); the graph's edges with the judge's labels (Joins); the
//   graph's node kinds and the judge's admission of the arena's words (IOriginChainJudge).
// - Outputs: a relation's edges (IOriginChainRelation.Related); Joins' hand-back; the judge's callbacks and what they read: an edge's Tail,
//   Head and Label, a target's InEdges, the fixed sources an in-edge's tail carries (Carried), the scratch words of the target being judged
//   (Failing), a source bit's node (FixedNode, FiniteNode); Work, the node visits, edge steps and bitset words, which A5 adds to its count
//   (V16) and the declaration judge reports (OriginProofMetrics.ChainWork); ArenaWords, the arena's size (D20); Index, the counting sort
//   of a graph's edges by tail or head, with which A5's meet law and Outlives query index the same class graph.
// - Invariants: only a Finite node or a Fixed tail is a source and only a Fixed head a judged target, so a Dependent node passes on what
//   reaches it, a chain through it is judged between its ends and one that starts or ends at it records nothing; a Fixed node passes
//   nothing on; storage is pooled, so a warm run allocates nothing.
// - Prohibitions: no proof, oracle, condition, limit or record of its own (each judge's: A5's oracle under its class's borrow binders,
//   the declaration judge's position judge under its paths' minimal conditions); no read of TyCtxt's, Binding's or MIR's state.

namespace Kimi.Compiler;

internal sealed partial class TyCtxt
{
    /// <summary>A relation of an Origin graph (<see cref="OriginChains.Relate"/>): its nodes' classes and meet operands, and the edges
    /// MeetNode's rule gives it.</summary>
    internal interface IOriginChainRelation
    {
        /// <summary>Gets a node's class.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its class.</returns>
        OriginAtomKind Kind(int node);

        /// <summary>Gets a meet's operand count, 0 for any other node.</summary>
        /// <param name="node">The node.</param>
        /// <returns>The count.</returns>
        int Operands(int node);

        /// <summary>Gets a meet's operand as a node.</summary>
        /// <param name="node">The meet.</param>
        /// <param name="index">The operand's index.</param>
        /// <returns>The operand's node.</returns>
        int Operand(int node, int index);

        /// <summary>Takes an edge of the relation.</summary>
        /// <param name="tail">The longer node.</param>
        /// <param name="head">The shorter node.</param>
        void Related(int tail, int head);
    }

    /// <summary>A judge of one Origin graph (<see cref="OriginChains.Chains"/>): its nodes' classes, its arena's admission and the verdicts
    /// at the chains' last edges.</summary>
    internal interface IOriginChainJudge
    {
        /// <summary>Gets a node's class.</summary>
        /// <param name="node">The node.</param>
        /// <returns>Its class.</returns>
        OriginAtomKind Kind(int node);

        /// <summary>Gets whether the judge admits the arena's words (2a0-D D20).</summary>
        /// <param name="words">The words.</param>
        /// <returns>False when the graph is over the judge's limit, and is not judged.</returns>
        bool Admits(long words);

        /// <summary>Judges an edge into a Fixed node with the finite sources its tail carries.</summary>
        /// <param name="edge">The edge.</param>
        /// <param name="sources">The finite-source bits (<see cref="OriginChains.FiniteNode"/>).</param>
        void Refuted(int edge, ReadOnlySpan<ulong> sources);

        /// <summary>Judges a Fixed node's in-edges with the fixed sources their tails carry (<see cref="OriginChains.InEdges"/>,
        /// <see cref="OriginChains.Carried"/>).</summary>
        /// <param name="target">The Fixed node.</param>
        void Unknown(int target);
    }

    /// <summary>R4-M118's chain judgment of one Origin graph, which A5's Origin stratum and the declaration judge read.</summary>
    internal sealed class OriginChains
    {
        private int[] tails = []; // The graph's edges, by edge index: tail, head and the judge's label.
        private int[] heads = [];
        private int[] labels = [];
        private int edgeCount;
        private int[] relateStamps = []; // Per node: the Begin that formed its operand edges as a shorter meet.
        private int relateStamp;
        private int[] outStart = []; // The edges by tail and by head (Index): node v's out-edges are outEdges[outStart[v]..outStart[v + 1]).
        private int[] outEdges = [];
        private int[] inStart = [];
        private int[] inEdges = [];
        private int[] cursors = [];
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
        private int components;
        private int words;

        /// <summary>Gets or sets the work since it was last reset (2a0-D §6.4): node visits, edge steps and bitset words.</summary>
        internal long Work { get; set; }

        /// <summary>Gets the words of a graph's arena (2a0-D §6.4, D20): each component's sources, the wider pass's words, and the fixed
        /// sources failing the target being judged.</summary>
        /// <param name="components">The inner components.</param>
        /// <param name="finiteSources">The finite sources.</param>
        /// <param name="fixedSources">The fixed sources.</param>
        /// <returns>The words.</returns>
        internal static long ArenaWords(int components, int finiteSources, int fixedSources)
            => ((long)components * Math.Max((finiteSources + 63) >> 6, (fixedSources + 63) >> 6)) + ((fixedSources + 63) >> 6);

        /// <summary>Indexes a graph's edges by a key (their tails or their heads; a negative key indexes none), a counting sort: node v's
        /// edges are index[start[v]..start[v + 1]), in edge order.</summary>
        /// <param name="keys">The edges' keys.</param>
        /// <param name="nodes">The node count.</param>
        /// <param name="start">Each node's start.</param>
        /// <param name="index">The edges by key.</param>
        /// <param name="cursors">Scratch for the insertion cursors.</param>
        internal static void Index(ReadOnlySpan<int> keys, int nodes, ref int[] start, ref int[] index, ref int[] cursors)
        {
            Grow(ref start, nodes + 1);
            Grow(ref index, keys.Length);
            Grow(ref cursors, nodes);
            start.AsSpan(0, nodes + 1).Clear();
            foreach (var key in keys)
            {
                start[key + 1] += key >= 0 ? 1 : 0;
            }

            for (var v = 0; v < nodes; v++)
            {
                start[v + 1] += start[v];
            }

            start.AsSpan(0, nodes).CopyTo(cursors);
            for (var e = 0; e < keys.Length; e++)
            {
                if (keys[e] >= 0)
                {
                    index[cursors[keys[e]]++] = e;
                }
            }
        }

        /// <summary>Begins a graph, or a scope of <see cref="Relate"/> (A5: a class; the declaration judge: a row, then its graph): no
        /// edges, and every shorter meet's operand edges to be formed again.</summary>
        internal void Begin()
        {
            if (this.relateStamp == int.MaxValue)
            {
                this.relateStamps.AsSpan().Clear();
                this.relateStamp = 0;
            }

            (this.edgeCount, this.relateStamp) = (0, this.relateStamp + 1);
        }

        /// <summary>Gives a relation's edges <c>longer -> shorter</c> by MeetNode's rule (2a0-D D15) to the relation (Related).</summary>
        /// <typeparam name="T">The relation's adapter type.</typeparam>
        /// <param name="relation">The relation.</param>
        /// <param name="longer">The longer node.</param>
        /// <param name="shorter">The shorter node.</param>
        internal void Relate<T>(ref T relation, int longer, int shorter)
            where T : struct, IOriginChainRelation
        {
            var operands = relation.Operands(longer);
            for (var i = 0; i < operands; i++)
            {
                relation.Related(relation.Operand(longer, i), shorter);
            }

            if (operands == 0 || relation.Kind(longer) != OriginAtomKind.Fixed)
            {
                relation.Related(longer, shorter);
            }

            operands = relation.Operands(shorter);
            if (operands == 0 || relation.Kind(shorter) == OriginAtomKind.Fixed)
            {
                return;
            }

            Grow(ref this.relateStamps, shorter + 1);
            if (this.relateStamps[shorter] == this.relateStamp)
            {
                return;
            }

            this.relateStamps[shorter] = this.relateStamp;
            for (var i = 0; i < operands; i++)
            {
                relation.Related(relation.Operand(shorter, i), shorter);
            }
        }

        /// <summary>Joins an edge to the graph (SPEC 15.6.5; 2a0-D §6.3).</summary>
        /// <param name="tail">The longer node.</param>
        /// <param name="tailKind">The tail's class.</param>
        /// <param name="head">The shorter node.</param>
        /// <param name="headKind">The head's class.</param>
        /// <param name="label">The judge's label of the edge.</param>
        /// <returns>False for an edge between two distinct Fixed nodes, which passes nothing on, so the judge judges it at once.</returns>
        internal bool Joins(int tail, OriginAtomKind tailKind, int head, OriginAtomKind headKind, int label)
        {
            if (tail == head)
            {
                return true;
            }

            if (tailKind == OriginAtomKind.Fixed && headKind == OriginAtomKind.Fixed)
            {
                return false;
            }

            var at = this.edgeCount++;
            Grow(ref this.tails, this.edgeCount);
            Grow(ref this.heads, this.edgeCount);
            Grow(ref this.labels, this.edgeCount);
            (this.tails[at], this.heads[at], this.labels[at]) = (tail, head, label);
            return true;
        }

        /// <summary>Judges the graph's chains (SPEC 15.6.5; 2a0-D D16, D17, D20): over Tarjan's components of the inner nodes, finite
        /// sources and then fixed sources propagate in topological order through one arena the judge admits; a Fixed node passes none on.</summary>
        /// <typeparam name="T">The judge's adapter type.</typeparam>
        /// <param name="judge">The judge.</param>
        /// <param name="nodes">The node count.</param>
        /// <returns>False when the judge does not admit the arena.</returns>
        internal bool Chains<T>(ref T judge, int nodes)
            where T : struct, IOriginChainJudge
        {
            Grow(ref this.finite, nodes);
            Grow(ref this.fixedBits, nodes);
            var (finiteCount, fixedCount) = (0, 0);
            for (var v = 0; v < nodes; v++)
            {
                (this.finite[v], this.fixedBits[v]) = (judge.Kind(v) == OriginAtomKind.Finite ? finiteCount++ : -1, -1);
                if (this.finite[v] >= 0)
                {
                    Grow(ref this.finiteNodes, finiteCount);
                    this.finiteNodes[finiteCount - 1] = v;
                }
            }

            for (var e = 0; e < this.edgeCount; e++)
            {
                if (this.tails[e] is var tail && judge.Kind(tail) == OriginAtomKind.Fixed && this.fixedBits[tail] < 0)
                {
                    this.fixedBits[tail] = fixedCount++;
                    Grow(ref this.fixedNodes, fixedCount);
                    this.fixedNodes[fixedCount - 1] = tail;
                }
            }

            this.Work += nodes + this.edgeCount;
            if (this.edgeCount == 0)
            {
                return true;
            }

            Index(this.tails.AsSpan(0, this.edgeCount), nodes, ref this.outStart, ref this.outEdges, ref this.cursors);
            this.components = this.Components(ref judge, nodes);
            var size = ArenaWords(this.components, finiteCount, fixedCount);
            if (!judge.Admits(size))
            {
                return false;
            }

            Grow(ref this.arena, (int)size);
            if (finiteCount > 0)
            {
                this.Refute(ref judge, this.Propagate(this.finite, finiteCount));
            }

            if (fixedCount > 0)
            {
                this.words = this.Propagate(this.fixedBits, fixedCount);
                Index(this.heads.AsSpan(0, this.edgeCount), nodes, ref this.inStart, ref this.inEdges, ref this.cursors);
                for (var g = 0; g < nodes; g++)
                {
                    if (judge.Kind(g) == OriginAtomKind.Fixed && this.inStart[g] < this.inStart[g + 1])
                    {
                        judge.Unknown(g);
                    }
                }
            }

            return true;
        }

        /// <summary>Gets an edge's tail.</summary>
        /// <param name="edge">The edge.</param>
        /// <returns>Its tail.</returns>
        internal int Tail(int edge) => this.tails[edge];

        /// <summary>Gets an edge's head.</summary>
        /// <param name="edge">The edge.</param>
        /// <returns>Its head.</returns>
        internal int Head(int edge) => this.heads[edge];

        /// <summary>Gets the judge's label of an edge.</summary>
        /// <param name="edge">The edge.</param>
        /// <returns>Its label.</returns>
        internal int Label(int edge) => this.labels[edge];

        /// <summary>Gets a Fixed target's in-edges (during <see cref="IOriginChainJudge.Unknown"/>).</summary>
        /// <param name="target">The target.</param>
        /// <returns>Its in-edges, in edge order.</returns>
        internal ReadOnlySpan<int> InEdges(int target)
            => this.inEdges.AsSpan(this.inStart[target], this.inStart[target + 1] - this.inStart[target]);

        /// <summary>Gets the fixed sources an in-edge's tail carries (during <see cref="IOriginChainJudge.Unknown"/>); every such tail is
        /// inner, as an edge between two Fixed nodes is judged when formed.</summary>
        /// <param name="edge">The edge.</param>
        /// <returns>The fixed-source bits (<see cref="FixedNode"/>).</returns>
        internal ReadOnlySpan<ulong> Carried(int edge) => this.arena.AsSpan(this.component[this.tails[edge]] * this.words, this.words);

        /// <summary>Gets the scratch words of the target being judged, one fixed-source set (during
        /// <see cref="IOriginChainJudge.Unknown"/>).</summary>
        /// <returns>The words.</returns>
        internal Span<ulong> Failing() => this.arena.AsSpan(this.components * this.words, this.words);

        /// <summary>Gets a fixed-source bit's node.</summary>
        /// <param name="bit">The bit.</param>
        /// <returns>The node.</returns>
        internal int FixedNode(int bit) => this.fixedNodes[bit];

        /// <summary>Gets a finite-source bit's node.</summary>
        /// <param name="bit">The bit.</param>
        /// <returns>The node.</returns>
        internal int FiniteNode(int bit) => this.finiteNodes[bit];

        private static void Grow<TItem>(ref TItem[] array, int length)
        {
            if (array.Length < length)
            {
                Array.Resize(ref array, Math.Max(length, 2 * array.Length));
            }
        }

        private static void Union(Span<ulong> target, ReadOnlySpan<ulong> source)
        {
            for (var w = 0; w < target.Length; w++)
            {
                target[w] |= source[w];
            }
        }

        // One pass's sources per component, in the arena: an inner node's own bit, a non-inner tail's bit into its head's component, then
        // each component's sources into those of the components its edges reach, highest component first (every edge between components
        // goes from a higher id to a lower one); returns the words per component.
        private int Propagate(int[] bit, int count)
        {
            var size = (count + 63) >> 6;
            this.arena.AsSpan(0, this.components * size).Clear();
            for (var e = 0; e < this.edgeCount; e++)
            {
                if (this.component[this.tails[e]] < 0 && bit[this.tails[e]] is var b and >= 0)
                {
                    this.arena[(this.component[this.heads[e]] * size) + (b >> 6)] |= 1UL << b;
                }
            }

            this.Work += this.edgeCount;
            for (var k = this.components - 1; k >= 0; k--)
            {
                var reached = this.arena.AsSpan(k * size, size);
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
                        this.Work += size;
                        if (into >= 0 && into != k)
                        {
                            Union(this.arena.AsSpan(into * size, size), reached);
                        }
                    }
                }
            }

            return size;
        }

        // Each edge into a Fixed node with the finite sources reaching its tail, to the judge (2a0-D D16).
        private void Refute<T>(ref T judge, int size)
            where T : struct, IOriginChainJudge
        {
            for (var k = this.components - 1; k >= 0; k--)
            {
                for (var m = this.memberStart[k]; m < this.memberStart[k + 1]; m++)
                {
                    var tail = this.members[m];
                    for (var e = this.outStart[tail]; e < this.outStart[tail + 1]; e++)
                    {
                        if (judge.Kind(this.heads[this.outEdges[e]]) == OriginAtomKind.Fixed)
                        {
                            judge.Refuted(this.outEdges[e], this.arena.AsSpan(k * size, size));
                        }
                    }
                }
            }
        }

        // Tarjan's strongly connected components of the inner nodes (Finite, Inferred, Dependent) over the edges between them, iteratively:
        // component ids in completion order, so every edge between components goes from a higher id to a lower one; returns the count and
        // fills the members by component. Other nodes keep component -1.
        private int Components<T>(ref T judge, int nodes)
            where T : struct, IOriginChainJudge
        {
            Grow(ref this.order, nodes);
            Grow(ref this.low, nodes);
            Grow(ref this.component, nodes);
            Grow(ref this.stack, nodes);
            Grow(ref this.frames, 2 * nodes);
            this.order.AsSpan(0, nodes).Clear();
            this.component.AsSpan(0, nodes).Fill(-1);
            var (visits, depth, count, top) = (0, 0, 0, 0);
            for (var root = 0; root < nodes; root++)
            {
                if (this.order[root] != 0 || !Inner(judge.Kind(root)))
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
                        if (!Inner(judge.Kind(w)))
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
                            this.component[w] = count;
                        }
                        while (w != v);
                        count++;
                    }

                    if (--top > 0)
                    {
                        var parent = this.frames[2 * (top - 1)];
                        this.low[parent] = Math.Min(this.low[parent], this.low[v]);
                    }
                }
            }

            Grow(ref this.memberStart, count + 1);
            Grow(ref this.members, nodes);
            this.memberStart.AsSpan(0, count + 1).Clear();
            for (var v = 0; v < nodes; v++)
            {
                if (this.component[v] >= 0)
                {
                    this.memberStart[this.component[v] + 1]++;
                }
            }

            for (var k = 0; k < count; k++)
            {
                this.memberStart[k + 1] += this.memberStart[k];
            }

            this.memberStart.AsSpan(0, count).CopyTo(this.order); // Scratch for the insertion cursors.
            for (var v = 0; v < nodes; v++)
            {
                if (this.component[v] >= 0)
                {
                    this.members[this.order[this.component[v]]++] = v;
                }
            }

            return count;

            static bool Inner(OriginAtomKind kind) => kind is OriginAtomKind.Finite or OriginAtomKind.Inferred or OriginAtomKind.Dependent;
        }
    }
}
