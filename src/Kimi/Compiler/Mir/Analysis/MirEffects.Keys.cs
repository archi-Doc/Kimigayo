// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A4's static keys for A5 (SPEC 15.6.4, 16.3.3, 22.4.4; DECISIONS R4-M38, R4-M91, R4-M92, Z7): the mutable static Fields a Call or a Drop
// may access through what it runs, and the statics whose first-access initialization it may run. Under the owner's P5 every key of one
// Field is one key, so keys are Fields, never substituted. A node's row is its local accesses (AddKey, as Local walks the view) and its
// local initialization entries (AddInit: a staticEnsure of a static with one slot, TyCtxt.Instantiates false, whose initializer holds
// no atom) joined with those of every body view it composes (Spread, after Run's fixed point): key bits, then one initialization bit per
// such static (initWords words), then one bit per static with several slots whose initialization it may run (fixedWords words). An
// initialization entry is no edge: the fixed point composes no initializer of an entry, and a reader composes it (Expand) unless it
// excludes the static. A static with several slots keeps its ensure's edge into the initializer, whose atoms each call's substitution
// resolves, until P42's per-access key (R4-M103) gives the entry the key's Type arguments; its bit is information only (Initializes),
// which Expand never reads, since the edge already composed the initializer. The memo (Spread, once per Run): each node's row and classes
// with every initialization composed, what Expand gives with nothing excluded, spread over the read graph with the initialization edges;
// a reader composes a node's memo unless an excluded initialization lies in it. Keys answers a site of one view with its key row and
// classes from the summaries and keys of the last Run, the initializations it excludes left out; Items gives a body view's solved
// classes, its initializations composed, and its atoms (what holds a published operation's body within its row, SPEC 4.9.6); Field
// numbers a Field's bits, Initialization a static's initialization bit, and Initializes tells whether the last query's site may run the
// initialization of a static with several slots.
// Invariant: an entry's initializer node keeps no atom (a definition that does not instantiate names only closed Types); a broken one is
// InternalInvariant.
internal sealed partial class MirEffects
{
    private readonly Dictionary<BindingSymbol, int> fieldIds = new(ReferenceEqualityComparer.Instance); // Each mutable static Field's number (P5).
    private readonly Dictionary<BindingSymbol, int> initIds = new(ReferenceEqualityComparer.Instance); // Each entry static's initialization number.
    private readonly Dictionary<BindingSymbol, int> fixedIds = new(ReferenceEqualityComparer.Instance); // Each static with several slots' number.
    private int[] initBodies = []; // By initialization number: the ordinal of the static's initializer.
    private ulong[] keys = []; // Each node's row, rowWords words: keyWords of key bits (P5: of Fields, never substituted), then its
    private ulong[] keyScratch = []; // initialization bits, after Spread, and those of the current query.
    private ulong[] expanded = []; // The initializations the current query composed (Expand, the memo).
    private ulong[] excluding = []; // The initializations the current query leaves out.
    private ulong[] fullRows = []; // The memo: each node's row with its initializations composed, then a word of its classes,
    private int[] fullHeads = []; // over the read graph with the initialization edges: each node's ensuring readers, then dependentHeads'.
    private int keyWords;
    private int initWords;
    private int rowWords;
    private int[] visits = []; // Spread's components: each node's visit number (int.MaxValue once finished),
    private int[] lows = []; // its lowest open visit it reaches, then its component's end in members,
    private int[] members = [];
    private (int Node, int Edge)[] frames = []; // and the walk's path, each node with its next dependency.

    /// <summary>Gets the key row of a Call or a Drop of a body view, from the summaries of the last <see cref="Run"/>: its terminator's
    /// inputs, as <see cref="Local"/> records them, evaluated against them in the view, with every first-access initialization they may run
    /// composed but those excluded. Bit 2f is a read and bit 2f + 1 a write of the mutable static Field that <see cref="Field"/> numbers f;
    /// a read bit beside its write bit is the write. The classes tell apart what the site runs and A4 does not classify:
    /// <see cref="Unknown"/> (an unknown effect, or an atom left abstract under the body's premises), which may access any key,
    /// <see cref="Limit"/>, <see cref="Deep"/> (past the destruction walk's bound) and <see cref="Derived"/> (a dependent of an
    /// error).</summary>
    /// <param name="body">A body of the last <see cref="Run"/>'s program, with which the dataflow is begun.</param>
    /// <param name="view">The view, which the case views select (a caller reselects its own afterwards).</param>
    /// <param name="block">The block whose terminator is the Call or the Drop.</param>
    /// <param name="excluded">The initialization bits (<see cref="Initialization"/>) of the statics whose initializations the row leaves
    /// out; empty for none.</param>
    /// <param name="classes">The site's classes among <see cref="Unknown"/>, <see cref="Limit"/>, <see cref="Deep"/> and <see cref="Derived"/>.</param>
    /// <returns>The row, valid until the next query or <see cref="Run"/>; no bit for another terminator.</returns>
    internal ReadOnlySpan<ulong> Keys(MirBody body, long view, int block, ReadOnlySpan<ulong> excluded, out byte classes)
    {
        cases.Select(body, view);
        var (items, edges, inits) = (this.writing.ItemCount, this.writing.EdgeCount, this.writing.InitCount);
        (this.definition, this.reader, this.found, this.scratchCount) = (body.Key.Decl, -1, 0, 0);
        this.glued.Clear();
        this.Begin(excluded);
        this.Terminator(this.program, body, block, view, body.Blocks[block].Terminator);
        this.Inputs(this.writing, items, this.writing.ItemCount, edges, this.writing.EdgeCount, inits, this.writing.InitCount);
        this.writing.Truncate(items, edges, inits); // The site's inputs are not the body's.
        this.Expand();
        classes = (byte)(this.Found & (Unknown | Limit | Derived | Deep)); // With no reader an atom left abstract is Unknown (AddAtom).
        return this.keyScratch.AsSpan(0, this.keyWords);
    }

    /// <summary>Gets the solved summary of a body view from the last <see cref="Run"/> as A4 keeps it: its classes, an environment
    /// effect's and its initializations' included, and its atoms over the definition's abstract Types (SPEC 15.6.4: its
    /// parameter-dependent effects).</summary>
    /// <param name="body">A body of the last <see cref="Run"/>'s program.</param>
    /// <param name="view">The view.</param>
    /// <param name="classes">The summary's classes.</param>
    /// <returns>The atoms, A4's storage, valid until the next <see cref="Run"/>.</returns>
    internal ReadOnlySpan<Atom> Items(MirBody body, long view, out byte classes)
    {
        var node = this.Base(body.Ordinal) + (int)view;
        classes = this.initWords > 0 ? (byte)this.fullRows[(node * (this.rowWords + 1)) + this.rowWords] : this.classes[node];
        return this.atoms.AsSpan(this.atomStarts[node], this.atomCounts[node]);
    }

    /// <summary>Gets A4's number of a mutable static Field in the program version of the last <see cref="Run"/>, whose bits in a key row
    /// are 2f and 2f + 1 (<see cref="Keys"/>). Every Field a body of the version accesses has one, so a borrowed one always does.</summary>
    /// <param name="field">The Field.</param>
    /// <returns>The number; -1 for a Field no body of the version accesses or one that is not mutable.</returns>
    internal int Field(BindingSymbol field) => this.fieldIds.TryGetValue(field, out var id) ? id : -1;

    /// <summary>Gets A4's initialization bit of a static in the program version of the last <see cref="Run"/>, which an excluded row of
    /// <see cref="Keys"/> sets to leave the static's first-access initialization out.</summary>
    /// <param name="field">The static.</param>
    /// <returns>The bit; -1 for a static no body of the version ensures or one with several slots, whose initialization is an edge.</returns>
    internal int Initialization(BindingSymbol field) => this.initIds.TryGetValue(field, out var id) ? id : -1;

    /// <summary>Gets whether the site of the last <see cref="Keys"/> query may run the first-access initialization of a static with several
    /// slots, which its row composes through an edge and no excluded row leaves out (until P42's per-access key, R4-M103).</summary>
    /// <param name="field">The static.</param>
    /// <returns>Whether it may; false for a static with one slot or one no body of the version ensures.</returns>
    internal bool Initializes(BindingSymbol field)
        => this.fixedIds.TryGetValue(field, out var id) && (this.keyScratch[this.keyWords + this.initWords + (id >> 6)] & (1UL << id)) != 0;

    // A local access to a mutable static Field: its key, numbered by Field once per program version.
    private void AddKey(BindingSymbol field, bool write)
    {
        if (!this.fieldIds.TryGetValue(field, out var id))
        {
            this.fieldIds[field] = id = this.fieldIds.Count;
            this.writing.Fields.Add(field);
        }

        this.writing.AddKey((2 * id) + (write ? 1 : 0));
    }

    // SPEC 15.6.4, 22.4.3: a staticEnsure's first-access initialization. A static with one slot is an initialization entry, numbered by
    // static once per program version; one with several keeps the edge into its initializer, whose atoms each call's substitution resolves
    // (until P42's per-access key, R4-M103).
    private void Initialize(MirBody initializer)
    {
        var field = initializer.Key.Decl!;
        if (types.Instantiates(field))
        {
            if (!this.fixedIds.TryGetValue(field, out var slots))
            {
                this.fixedIds[field] = slots = this.fixedIds.Count;
                this.writing.Initialized.Add(field);
            }

            this.AddEdge(initializer, null);
            this.writing.AddInit(~slots); // Information only (Initializes).
            return;
        }

        if (!this.initIds.TryGetValue(field, out var id))
        {
            this.initIds[field] = id = this.initIds.Count;
            this.writing.Initialized.Add(field);
            MirDataflow.Grow(ref this.initBodies, id + 1);
        }

        this.initBodies[id] = initializer.Ordinal;
        this.writing.AddInit(id);
    }

    // Begins a reader's composition (Keys, the obligations): an empty row, no initialization composed yet, those excluded left out.
    private void Begin(ReadOnlySpan<ulong> excluded)
    {
        this.keyScratch.AsSpan(0, this.rowWords).Clear();
        this.expanded.AsSpan(0, this.initWords).Clear();
        var excluding = this.excluding.AsSpan(0, this.initWords);
        excluding.Clear();
        excluded[..Math.Min(excluded.Length, excluding.Length)].CopyTo(excluding);
    }

    // SPEC 15.6.4: the first-access initializations in the current composition's row that it has not composed, each not excluded composed
    // once through its initializer's views (Compose, its memo where none excluded lies in it), whose own initializations join it (R5-M183:
    // one with a validator finding is Derived, Views).
    private void Expand()
    {
        var words = this.initWords;
        var done = this.expanded.AsSpan(0, words);
        for (var grew = words > 0; grew;)
        {
            grew = false;
            for (var w = 0; w < words; w++)
            {
                for (var next = this.keyScratch[this.keyWords + w] & ~done[w] & ~this.excluding[w]; next != 0; next &= next - 1)
                {
                    var id = (w << 6) + BitOperations.TrailingZeroCount(next);
                    if ((done[w] & (1UL << id)) == 0)
                    {
                        done[w] |= 1UL << id;
                        var initializer = this.program.Body(this.initBodies[id]);
                        this.Compose(initializer.Ordinal, this.Views(initializer, null), null);
                        grew = true;
                    }
                }
            }
        }
    }

    // Whether a node's memo leaves out no initialization the current query excludes.
    private bool Memo(int node)
    {
        var row = this.fullRows.AsSpan((node * (this.rowWords + 1)) + this.keyWords, this.initWords);
        for (var w = 0; w < row.Length; w++)
        {
            if ((row[w] & this.excluding[w]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    // P5's keys and the initialization entries after the fixed point (R4-M38), which neither classes nor atoms read: a node's local keys
    // and entries joined with those of every node
    // it read, as its dependencies record them (read sets only grow), once per strongly connected component of the read graph (Tarjan's,
    // iterative), the components whose nodes others read first, so Θ((nodes + dependencies) × rowWords).
    // Then the memo (fullRows): each node's local facts and classes spread over the read graph with an edge from each entry's initializer's
    // views to the node that ensures it (fullHeads, prepended to dependentHeads' lists; one with a validator finding adds Derived instead,
    // Views), so Θ((nodes + dependencies + entries) × rowWords) more; it checks the invariant that an entry's initializer keeps no atom.
    private void Spread(int count)
    {
        var (row, full) = (this.rowWords, this.rowWords + 1);
        MirDataflow.Grow(ref this.fullRows, count * full);
        this.fullRows.AsSpan(0, count * full).Clear();
        for (var node = 0; node < count; node++)
        {
            var store = this.Of(node);
            var own = store.Keys;
            for (var k = this.Start(store, node).Keys; k < store[node].Keys; k++)
            {
                this.keys[(node * row) + (own[k] >> 6)] |= 1UL << own[k];
            }

            var inits = store.Inits;
            for (var k = this.Start(store, node).Inits; k < store[node].Inits; k++)
            {
                var bit = inits[k] >= 0 ? (64 * this.keyWords) + inits[k] : (64 * (this.keyWords + this.initWords)) + ~inits[k];
                this.keys[(node * row) + (bit >> 6)] |= 1UL << bit;
            }

            this.keys.AsSpan(node * row, row).CopyTo(this.fullRows.AsSpan(node * full));
            this.fullRows[(node * full) + row] = this.classes[node];
        }

        this.Components(count, this.dependentHeads, this.keys, row);
        if (this.initWords == 0)
        {
            return;
        }

        MirDataflow.Grow(ref this.fullHeads, count);
        this.dependentHeads.AsSpan(0, count).CopyTo(this.fullHeads);
        for (var node = 0; node < count; node++)
        {
            var store = this.Of(node);
            var inits = store.Inits;
            for (var k = this.Start(store, node).Inits; k < store[node].Inits; k++)
            {
                if (inits[k] >= 0)
                {
                    this.Initializer(node, this.program.Body(this.initBodies[inits[k]])); // A static with several slots has its edge.
                }
            }
        }

        this.Components(count, this.fullHeads, this.fullRows, full);
    }

    // An initialization edge of the memo: from each view of an entry's initializer to the node that ensures it.
    private void Initializer(int node, MirBody initializer)
    {
        if (initializer.HasFinding)
        {
            this.fullRows[(node * (this.rowWords + 1)) + this.rowWords] |= Derived; // R5-M183, Views.
            return;
        }

        for (var mask = this.program.Drops.Holds(initializer.Ordinal) ? cases.Views(initializer, null) : 0; mask != 0; mask &= mask - 1)
        {
            var source = this.Base(initializer.Ordinal) + BitOperations.TrailingZeroCount(mask);
            if (this.atomCounts[source] != 0)
            {
                throw new InvalidOperationException($"The initializer of {initializer.Key.Decl!.Name} keeps an atom (InternalInvariant).");
            }

            MirDataflow.Grow(ref this.dependents, this.dependentCount + 1);
            this.dependents[this.dependentCount] = (node, this.fullHeads[source]);
            this.fullHeads[source] = this.dependentCount++;
        }
    }

    // The rows of a graph's components (Tarjan's, iterative), the components whose nodes others read first: a component's rows joined, then
    // each reader's (heads: each node's readers in dependents).
    private void Components(int count, int[] heads, ulong[] rows, int words)
    {
        MirDataflow.Grow(ref this.visits, count);
        MirDataflow.Grow(ref this.lows, count);
        MirDataflow.Grow(ref this.members, count);
        MirDataflow.Grow(ref this.frames, count);
        this.visits.AsSpan(0, count).Fill(-1);

        // members holds the open nodes from its start and the finished components from its end, the last finished first.
        var (visited, open, finished) = (0, 0, count);
        for (var root = 0; root < count; root++)
        {
            if (this.visits[root] < 0)
            {
                this.Walk(heads, root, ref visited, ref open, ref finished);
            }
        }

        // A component after every component it reads: its rows, then each reader's.
        for (var start = 0; start < count; start = this.lows[this.members[start]])
        {
            var union = this.keyScratch.AsSpan(0, words);
            union.Clear();
            for (var i = start; i < this.lows[this.members[start]]; i++)
            {
                _ = MirDataflow.Union(union, rows.AsSpan(this.members[i] * words, words));
            }

            for (var i = start; i < this.lows[this.members[start]]; i++)
            {
                union.CopyTo(rows.AsSpan(this.members[i] * words, words));
                for (var d = heads[this.members[i]]; d >= 0; d = this.dependents[d].Next)
                {
                    _ = MirDataflow.Union(rows.AsSpan(this.dependents[d].Node * words, words), union);
                }
            }
        }
    }

    // The depth-first walk of Components from an unvisited root along a graph's dependencies (each node to its readers), finishing each
    // component once every component it reaches is finished.
    private void Walk(int[] heads, int root, ref int visited, ref int open, ref int finished)
    {
        var depth = 0;
        for (var next = root; next >= 0;)
        {
            this.visits[next] = this.lows[next] = visited++;
            this.members[open++] = next;
            this.frames[depth++] = (next, heads[next]);
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

    // A node whose readers are all walked (Walk): its parent's low lowered to its own and, at its component's root, the component finished.
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
