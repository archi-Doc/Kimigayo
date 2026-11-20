// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A4's static keys for A5 (SPEC 15.6.4, 16.3.3, 22.4.4; DECISIONS R4-M38, R4-M91, R4-M92, Z7): the mutable static Fields a Call or a Drop
// may access through what it runs, its first-access initializations included. Under the owner's P5 every key of one Field is one key, so
// keys are Fields, never substituted. A staticEnsure of a static with one slot (TyCtxt.Instantiates false), whose initializer holds no
// atom, is an initialization entry (AddInit), no edge: the fixed point composes no initializer of an entry, so a node's classes are its own.
// A static with several slots keeps its ensure's edge into the initializer, whose atoms each call's substitution resolves, until P42's
// per-access key (R4-M103) gives the entry the key's Type arguments; its slot bit tells only that the site may run that initialization.
// After the fixed point Spread joins, once per Run, each node's row: its local accesses (AddKey, as Local walks the view) and slot bits,
// and its classes where entries exist, with those of every body view it composes and of every entry's initializer it ensures. A reader
// composes those rows (Compose); a query that excludes initializations (A5's second, MirBorrows.Effects) composes instead, from the local
// facts, what each node reaches through its calls and the entries not excluded (Reach). Keys answers a site of one view with its key and
// slot row and classes; Summary gives a body view's row and classes; Items its classes and its atoms (what holds a published operation's
// body within its row, SPEC 4.9.6); Field numbers a Field's bits, Initialization a static's entry and Slot a static's slot bit.
// Invariant: an entry's initializer node keeps no atom (a definition that does not instantiate names only closed Types); a broken one is
// InternalInvariant.
internal sealed partial class MirEffects
{
    private readonly Dictionary<BindingSymbol, int> fieldIds = new(ReferenceEqualityComparer.Instance); // Each mutable static Field's number (P5).
    private readonly Dictionary<BindingSymbol, int> initIds = new(ReferenceEqualityComparer.Instance); // Each entry static's initialization number.
    private readonly Dictionary<BindingSymbol, int> fixedIds = new(ReferenceEqualityComparer.Instance); // Each static with several slots' number.
    private int[] initBodies = []; // By initialization number: the ordinal of the static's initializer.
    private ulong[] keys = []; // Each node's row after Spread, rowWords words: key bits (P5: of Fields, never substituted), slot bits, then
    private ulong[] keyScratch = []; // a word of its classes where entries exist; and the current query's row.
    private ulong[] excluding = []; // The initializations the current query leaves out (Begin), by initialization number;
    private bool excludes; // whether it leaves any out.
    private int[] reached = []; // Reach's nodes: each node's query stamp, and the walk's stack.
    private int[] reaching = [];
    private int reachStamp;
    private int keyWords;
    private int slotWords;
    private int classWords;
    private int rowWords;
    private int[] visits = []; // Spread's components: each node's visit number (int.MaxValue once finished),
    private int[] lows = []; // its lowest open visit it reaches, then its component's end in members,
    private int[] members = [];
    private (int Node, int Edge)[] frames = []; // and the walk's path, each node with its next dependency.

    /// <summary>Gets the key row of a Call or a Drop of a body view, from the summaries of the last <see cref="Run"/>: its terminator's
    /// inputs, as <see cref="Local"/> records them, evaluated against them in the view, with every first-access initialization they may run
    /// composed but those excluded. Bit 2f is a read and bit 2f + 1 a write of the mutable static Field that <see cref="Field"/> numbers f;
    /// a read bit beside its write bit is the write; a static with several slots whose initialization the site may run has its
    /// <see cref="Slot"/> bit. The classes tell apart what the site runs and A4 does not classify: <see cref="Unknown"/> (an unknown effect,
    /// or an atom left abstract under the body's premises), which may access any key, <see cref="Limit"/>, <see cref="Deep"/> (past the
    /// destruction walk's bound) and <see cref="Derived"/> (a dependent of an error).</summary>
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
        classes = (byte)(this.Found & (Unknown | Limit | Derived | Deep)); // With no reader an atom left abstract is Unknown (AddAtom).
        return this.keyScratch.AsSpan(0, this.keyWords + this.slotWords);
    }

    /// <summary>Gets the summary of a body view from the last <see cref="Run"/>: the key and slot row of what the body may run (its local
    /// keys joined with every body it composes, its initializations included) and its classes. An atom the summary keeps is
    /// <see cref="Unknown"/> here, since a site that composes the body may resolve it so (R4-M92).</summary>
    /// <param name="body">A body of the last <see cref="Run"/>'s program.</param>
    /// <param name="view">The view.</param>
    /// <param name="classes">The summary's classes among <see cref="Unknown"/>, <see cref="Limit"/>, <see cref="Deep"/> and <see cref="Derived"/>.</param>
    /// <returns>The row, A4's storage, valid until the next <see cref="Run"/>.</returns>
    internal ReadOnlySpan<ulong> Summary(MirBody body, long view, out byte classes)
    {
        var node = this.Base(body.Ordinal) + (int)view;
        classes = (byte)(this.Classes(node) & (Unknown | Limit | Derived | Deep));
        if (this.atomCounts[node] > 0)
        {
            classes |= Unknown;
        }

        return this.keys.AsSpan(node * this.rowWords, this.keyWords + this.slotWords);
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
        classes = this.Classes(node);
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

    /// <summary>Gets a static with several slots' bit in a row of <see cref="Keys"/> or <see cref="Summary"/>, set where the site may run its
    /// first-access initialization, which no excluded row leaves out (until P42's per-access key, R4-M103).</summary>
    /// <param name="field">The static.</param>
    /// <returns>The bit; -1 for a static with one slot or one no body of the version ensures.</returns>
    internal int Slot(BindingSymbol field) => this.fixedIds.TryGetValue(field, out var id) ? (64 * this.keyWords) + id : -1;

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
    // (until P42's per-access key, R4-M103), and an entry of its slot number's complement (its slot bit).
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
            this.writing.AddInit(~slots);
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

    // Begins a reader's composition (Keys, the obligations): an empty row, those excluded left out, a new stamp for Reach where any is.
    private void Begin(ReadOnlySpan<ulong> excluded)
    {
        this.keyScratch.AsSpan(0, this.rowWords).Clear();
        var excluding = this.excluding.AsSpan(0, (this.initIds.Count + 63) >> 6);
        excluding.Clear();
        excluded[..Math.Min(excluded.Length, excluding.Length)].CopyTo(excluding);
        this.excludes = excluding.ContainsAnyExcept(0UL);
        this.reachStamp += this.excludes ? 1 : 0;
    }

    // A node's classes with its initializations: its row's word where entries exist, else the fixed point's.
    private byte Classes(int node) => this.classWords > 0 ? (byte)this.keys[(node * this.rowWords) + this.rowWords - 1] : this.classes[node];

    // Whether the current query leaves an entry's initialization out.
    private bool Excluded(int init) => this.excludes && (this.excluding[init >> 6] & (1UL << init)) != 0;

    // A query with exclusions (A5's second, MirBorrows.Effects): the local keys and slot bits of every node a node reaches through its calls
    // and through the entries not excluded, each once per query, with the classes of each initializer entered (a callee's are its caller's
    // already, by the fixed point); Θ(reached nodes + their local facts).
    private void Reach(int start)
    {
        var depth = 0;
        Enter(start);
        while (depth > 0)
        {
            var node = this.reaching[--depth];
            var store = this.Of(node);
            var (from, to) = (this.Start(store, node), store[node]);
            foreach (var key in store.Keys[from.Keys..to.Keys])
            {
                this.keyScratch[key >> 6] |= 1UL << key;
            }

            foreach (var edge in store.Edges[from.Edges..to.Edges])
            {
                for (var mask = edge.Views; mask != 0; mask &= mask - 1)
                {
                    Enter(this.Base(edge.Body) + BitOperations.TrailingZeroCount(mask));
                }
            }

            foreach (var init in store.Inits[from.Inits..to.Inits])
            {
                if (init < 0)
                {
                    this.keyScratch[this.keyWords + (~init >> 6)] |= 1UL << ~init;
                }
                else if (!this.Excluded(init))
                {
                    var initializer = this.program.Body(this.initBodies[init]);
                    for (var mask = this.Views(initializer, null); mask != 0; mask &= mask - 1)
                    {
                        var entered = this.Base(initializer.Ordinal) + BitOperations.TrailingZeroCount(mask);
                        this.found |= this.reached[entered] != this.reachStamp ? this.classes[entered] : (byte)0;
                        Enter(entered);
                    }
                }
            }
        }

        void Enter(int node)
        {
            if (this.reached[node] != this.reachStamp)
            {
                this.reached[node] = this.reachStamp;
                this.reaching[depth++] = node;
            }
        }
    }

    // P5's keys and the slot bits after the fixed point (R4-M38), which neither classes nor atoms read: a node's local keys and slot bits,
    // and its classes where entries exist, joined with those of every node it read, as its dependencies record them (read sets only grow),
    // and of every view of each entry's initializer (an edge from it to the node, prepended to its dependencies; one with a validator finding
    // adds Derived, R5-M183, as Views does), once per strongly connected component of that graph (Tarjan's, iterative), the components
    // whose nodes others read first, so Θ((nodes + dependencies + entries) × rowWords). It checks that an entry's initializer keeps no atom.
    private void Spread(int count)
    {
        MirDataflow.Grow(ref this.visits, count);
        MirDataflow.Grow(ref this.lows, count);
        MirDataflow.Grow(ref this.members, count);
        MirDataflow.Grow(ref this.frames, count);
        this.visits.AsSpan(0, count).Fill(-1);
        var row = this.rowWords;
        for (var node = 0; node < count; node++)
        {
            var store = this.Of(node);
            var (from, to) = (this.Start(store, node), store[node]);
            foreach (var key in store.Keys[from.Keys..to.Keys])
            {
                this.keys[(node * row) + (key >> 6)] |= 1UL << key;
            }

            if (this.classWords > 0)
            {
                this.keys[(node * row) + row - 1] = this.classes[node];
            }

            foreach (var init in store.Inits[from.Inits..to.Inits])
            {
                if (init < 0)
                {
                    this.keys[(node * row) + this.keyWords + (~init >> 6)] |= 1UL << ~init;
                }
                else
                {
                    this.Initializer(node, this.program.Body(this.initBodies[init]));
                }
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

        // A component after every component it reads: its rows, then each reader's.
        for (var start = 0; start < count; start = this.lows[this.members[start]])
        {
            var union = this.keyScratch.AsSpan(0, row);
            union.Clear();
            for (var i = start; i < this.lows[this.members[start]]; i++)
            {
                _ = MirDataflow.Union(union, this.keys.AsSpan(this.members[i] * row, row));
            }

            for (var i = start; i < this.lows[this.members[start]]; i++)
            {
                union.CopyTo(this.keys.AsSpan(this.members[i] * row, row));
                for (var d = this.dependentHeads[this.members[i]]; d >= 0; d = this.dependents[d].Next)
                {
                    _ = MirDataflow.Union(this.keys.AsSpan(this.dependents[d].Node * row, row), union);
                }
            }
        }
    }

    // An entry's initialization in Spread's graph: an edge from each view of its initializer to the node that ensures it.
    private void Initializer(int node, MirBody initializer)
    {
        if (initializer.HasFinding)
        {
            this.keys[(node * this.rowWords) + this.rowWords - 1] |= Derived; // R5-M183, as Views.
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
            this.dependents[this.dependentCount] = (node, this.dependentHeads[source]);
            this.dependentHeads[source] = this.dependentCount++;
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
