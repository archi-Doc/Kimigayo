// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using System.Runtime.InteropServices;

namespace Kimi.Compiler.Mir;

// A4's static keys for A5 (SPEC 15.6.4, 16.3.3, 22.4.4; DECISIONS R4-M38): the mutable static Fields a Call or a Drop may access through
// what it runs. Under the owner's P5 every key of one Field is one key, so keys are Fields, never substituted: a node's keys are its local
// accesses (AddKey, as Local walks the view) joined with those of every body view it composes (Spread, after Run's fixed point); Keys
// answers a site from the summaries and keys of the last Run.
internal sealed partial class MirEffects
{
    private readonly Dictionary<BindingSymbol, int> fieldIds = new(ReferenceEqualityComparer.Instance); // Each mutable static Field's number,
    private readonly List<BindingSymbol> fields = []; // and the Field of each number (P5: one key per Field).
    private readonly List<(BindingSymbol Field, bool Write)> accessed = []; // The last query's keys.
    private int[] localKeys = []; // A key's bit: 2 × its Field's number, plus 1 for a write.
    private int[] keyEnds = []; // The end of each node's local keys in localKeys.
    private int keyCount;
    private ulong[] keys = []; // Each node's static keys, keyWords words of key bits (P5: of Fields, never substituted), after Spread,
    private ulong[] keyScratch = []; // and those of the current query.
    private int keyWords;
    private int[] visits = []; // Spread's components: each node's visit number (int.MaxValue once finished),
    private int[] lows = []; // its lowest open visit it reaches, then its component's end in members,
    private int[] members = [];
    private (int Node, int Edge)[] frames = []; // and the walk's path, each node with its next dependency.

    /// <summary>Gets the mutable static Fields a Call or a Drop of a body view may access through what it runs, from the summaries of
    /// the last <see cref="Run"/>: its terminator's inputs, as <see cref="Local"/> records them, evaluated against them, each Field once,
    /// written where any access writes it. What the site runs and A4 does not classify (an unknown effect, an atom left abstract under
    /// the body's premises, a limit, or a dependent of an error) makes it unknown, so it may access any key.</summary>
    /// <param name="body">A body of the last <see cref="Run"/>'s program, with which the dataflow is begun.</param>
    /// <param name="view">The view, which the case views select.</param>
    /// <param name="block">The block whose terminator is the Call or the Drop.</param>
    /// <param name="unknown">Whether the site may access any key.</param>
    /// <returns>The keys, in a buffer the next query reuses; none for another terminator.</returns>
    internal ReadOnlySpan<(BindingSymbol Field, bool Write)> Keys(MirBody body, long view, int block, out bool unknown)
    {
        cases.Select(body, view);
        var (items, edges) = (this.itemCount, this.edgeCount);
        (this.definition, this.reader, this.found, this.scratchCount) = (body.Key.Decl, -1, 0, 0);
        this.glued.Clear();
        this.keyScratch.AsSpan(0, this.keyWords).Clear();
        this.Terminator(this.program, body, block, view, body.Blocks[block].Terminator);
        this.Inputs(items, this.itemCount, edges, this.edgeCount);
        (this.itemCount, this.edgeCount) = (items, edges); // The site's inputs are not the body's.
        unknown = (this.found & (Unknown | Limit | Derived)) != 0; // With no reader an atom left abstract is Unknown (AddAtom).
        this.accessed.Clear();
        for (var word = 0; word < this.keyWords; word++)
        {
            for (var bits = this.keyScratch[word]; bits != 0; bits &= bits - 1)
            {
                // A read bit beside its write bit is the write's key.
                var bit = (word << 6) + BitOperations.TrailingZeroCount(bits);
                if ((bit & 1) != 0 || (bits & (2UL << (bit & 63))) == 0)
                {
                    this.accessed.Add((this.fields[bit >> 1], (bit & 1) != 0));
                }
            }
        }

        return CollectionsMarshal.AsSpan(this.accessed);
    }

    // A local access to a mutable static Field: its key, numbered by Field once per program version.
    private void AddKey(BindingSymbol field, bool write)
    {
        if (!this.fieldIds.TryGetValue(field, out var id))
        {
            (id, this.fieldIds[field]) = (this.fields.Count, this.fields.Count);
            this.fields.Add(field);
        }

        MirDataflow.Grow(ref this.localKeys, this.keyCount + 1);
        this.localKeys[this.keyCount++] = (2 * id) + (write ? 1 : 0);
    }

    // P5's keys after the fixed point (R4-M38), which neither classes nor atoms read: a node's local keys joined with those of every node
    // it read, as its dependencies record them (read sets only grow), once per strongly connected component of the read graph (Tarjan's,
    // iterative), the components whose nodes others read first, so Θ((nodes + dependencies) × keyWords).
    private void Spread(int count)
    {
        MirDataflow.Grow(ref this.visits, count);
        MirDataflow.Grow(ref this.lows, count);
        MirDataflow.Grow(ref this.members, count);
        MirDataflow.Grow(ref this.frames, count);
        this.visits.AsSpan(0, count).Fill(-1);
        for (var node = 0; node < count; node++)
        {
            for (var k = node == 0 ? 0 : this.keyEnds[node - 1]; k < this.keyEnds[node]; k++)
            {
                this.keys[(node * this.keyWords) + (this.localKeys[k] >> 6)] |= 1UL << this.localKeys[k];
            }
        }

        // members holds the open nodes from its start and the finished components from its end, the last finished first.
        var (visited, open, finished) = (0, 0, count);
        for (var root = 0; root < count; root++)
        {
            if (this.visits[root] < 0)
            {
                this.Walk(root, ref visited, ref open, ref finished);
            }
        }

        // A component after every component it reads: its keys, then each reader's.
        var words = this.keyWords;
        for (var start = 0; start < count; start = this.lows[this.members[start]])
        {
            var union = this.keyScratch.AsSpan(0, words);
            union.Clear();
            for (var i = start; i < this.lows[this.members[start]]; i++)
            {
                _ = MirDataflow.Union(union, this.keys.AsSpan(this.members[i] * words, words));
            }

            for (var i = start; i < this.lows[this.members[start]]; i++)
            {
                union.CopyTo(this.keys.AsSpan(this.members[i] * words, words));
                for (var d = this.dependentHeads[this.members[i]]; d >= 0; d = this.dependents[d].Next)
                {
                    _ = MirDataflow.Union(this.keys.AsSpan(this.dependents[d].Node * words, words), union);
                }
            }
        }
    }

    // The depth-first walk of Spread from an unvisited root along the dependencies (each node to its readers), finishing each component
    // once every component it reaches is finished.
    private void Walk(int root, ref int visited, ref int open, ref int finished)
    {
        var depth = 0;
        for (var next = root; next >= 0;)
        {
            this.visits[next] = this.lows[next] = visited++;
            this.members[open++] = next;
            this.frames[depth++] = (next, this.dependentHeads[next]);
            next = -1;
            while (depth > 0 && next < 0)
            {
                ref var frame = ref this.frames[depth - 1];
                if (frame.Edge < 0)
                {
                    this.Finish(frame.Node, --depth, ref open, ref finished);
                    continue;
                }

                var dependent = this.dependents[frame.Edge].Node;
                frame.Edge = this.dependents[frame.Edge].Next;
                if (this.visits[dependent] < 0)
                {
                    next = dependent;
                }
                else if (this.visits[dependent] != int.MaxValue)
                {
                    this.lows[frame.Node] = Math.Min(this.lows[frame.Node], this.visits[dependent]); // Open: on this path's component.
                }
            }
        }
    }

    // A node whose readers are all walked: its parent's low lowered to its own and, at its component's root, the component finished.
    private void Finish(int node, int depth, ref int open, ref int finished)
    {
        if (depth > 0)
        {
            ref var parent = ref this.lows[this.frames[depth - 1].Node];
            parent = Math.Min(parent, this.lows[node]);
        }

        if (this.lows[node] != this.visits[node])
        {
            return;
        }

        var end = finished;
        int member;
        do
        {
            member = this.members[--open];
            this.visits[member] = int.MaxValue;
            this.lows[member] = end;
            this.members[--finished] = member;
        }
        while (member != node);
    }
}
