// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;

namespace Kimi.Compiler.Mir;

// A4's static keys for A5 (SPEC 15.6.4, 16.3.3, 22.4.4; DECISIONS R4-M38, R4-M91, R4-M92, Z7): the mutable static Fields a Call or a Drop
// may access through what it runs, its first-access initializations included. Under the owner's P5 every key of one Field is one key, so
// keys are Fields, never substituted. A staticEnsure of a static with one slot (TyCtxt.Instantiates false), whose initializer holds no
// atom, is an initialization entry (AddInit), no edge: the fixed point composes no initializer of an entry, so a node's classes are its own.
// A static with several slots keeps its ensure's edge into the initializer, whose atoms each call's substitution resolves, until P42's
// per-access key (R4-M103) gives the entry the key's Type arguments; its slot bit tells only that the site may run that initialization.
// What a node runs is stated once, as its successors in the read graph: every body view the fixed point read for it (Compose records the
// read, MirEffects.Solve) and each view of its entries' initializers (Spread appends them, an entry's one statement). After the fixed
// point Spread joins, once per Run, each node's own facts (Seed: its local accesses (AddKey, as Local walks the view), slot bits, and
// its classes where entries exist) with the rows of every node it reaches. A reader composes those rows (Compose); a query that excludes
// initializations (A5's second, MirBorrows.Effects) composes instead the own facts of every node the same graph reaches without the
// excluded entries' edges (Without), so with none excluded it is Spread's row. Keys answers a site of one view with its key and
// slot row and classes; Summary gives a body view's row and classes; Items its classes and its atoms (what holds a published operation's
// body within its row, SPEC 4.9.6); Field numbers a Field's bits, Initialization a static's entry and Slot a static's slot bit.
// Invariants: an entry's initializer node keeps no atom (a definition that does not instantiate names only closed Types); a broken one is
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
    private int[] reached = []; // Without's nodes: each node's query stamp, and the walk's stack.
    private int[] reaching = [];
    private int reachStamp;
    private int keyWords;
    private int slotWords;
    private int classWords;
    private int rowWords;
    private int[] visits = []; // Spread's components: each node's visit number (int.MaxValue once finished),
    private int[] lows = []; // its lowest open visit it reaches,
    private int[] members = []; // the open nodes,
    private (int Node, int Edge)[] frames = []; // and the walk's path, each node with its next read.

    /// <summary>Gets the nodes the queries with exclusions walked since the last <see cref="Run"/>, each once per query: linear in their
    /// reach (V16).</summary>
    internal int Walked { get; private set; }

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
    /// effect's, a static access's (<see cref="Static"/>) and its initializations' included, and its atoms over the definition's abstract
    /// Types (SPEC 15.6.4: its parameter-dependent effects).</summary>
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

    // Begins a reader's composition (Keys, the obligations): an empty row and, where it excludes an initialization A4 numbered, those
    // excluded (excluding, read only while excludes holds, which needs an entry and so the class word) and a new stamp for Without.
    private void Begin(ReadOnlySpan<ulong> excluded)
    {
        this.keyScratch.AsSpan(0, this.rowWords).Clear();
        var excluding = this.excluding.AsSpan(0, (this.initIds.Count + 63) >> 6);
        excluded = excluded[..Math.Min(excluded.Length, excluding.Length)];
        this.excludes = excluded.ContainsAnyExcept(0UL);
        if (this.excludes)
        {
            excluding.Clear();
            excluded.CopyTo(excluding);
            this.reachStamp++;
        }
    }

    // A node's classes with its initializations: its row's word where entries exist, else the fixed point's.
    private byte Classes(int node) => this.classWords > 0 ? (byte)this.keys[(node * this.rowWords) + this.rowWords - 1] : this.classes[node];

    // Whether the current query leaves an entry's initialization out.
    private bool Excluded(int init) => this.excludes && (this.excluding[init >> 6] & (1UL << init)) != 0;

    // A query with exclusions (A5's second, MirBorrows.Effects): the own facts (Seed) of every node the read graph reaches from a node but
    // through an edge of an entry the query excludes, each once per query, Derived for an edge to none it does not exclude (an initializer
    // with a validator finding), the classes joining the query's; with none excluded, Spread's row. Θ(nodes reached + their reads, keys and
    // entries).
    private void Without(int start)
    {
        var row = this.keyScratch.AsSpan(0, this.rowWords);
        var depth = 0;
        Enter(start);
        while (depth > 0)
        {
            var node = this.reaching[--depth];
            this.Seed(node, row);
            this.Walked++;
            for (var r = this.readHeads[node]; r >= 0; r = this.reads[r].Next)
            {
                var (read, init) = (this.reads[r].Node, this.reads[r].Init);
                if (init >= 0 && this.Excluded(init))
                {
                    continue;
                }

                if (read >= 0)
                {
                    Enter(read);
                }
                else
                {
                    row[^1] |= Derived;
                }
            }
        }

        this.found |= (byte)row[^1];

        void Enter(int node)
        {
            if (this.reached[node] != this.reachStamp)
            {
                this.reached[node] = this.reachStamp;
                this.reaching[depth++] = node;
            }
        }
    }

    // P5's keys and the slot bits after the fixed point (R4-M38), which neither classes nor atoms read: each node's own facts (Seed) joined
    // with the rows of every node its read graph reaches (read sets only grow, so the last reads are all of them), once per strongly
    // connected component of that graph (Tarjan's, iterative), a component after every component it reads, so Θ((nodes + reads + entry
    // edges) × rowWords). First each node's entries join the graph (Initializer).
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
            this.Seed(node, this.keys.AsSpan(node * row, row));
            var store = this.Of(node);
            foreach (var init in store.Inits[this.Start(store, node).Inits..store[node].Inits])
            {
                if (init >= 0)
                {
                    this.Initializer(node, init);
                }
            }
        }

        var (visited, open) = (0, 0);
        for (var root = 0; root < count; root++)
        {
            if (this.visits[root] < 0)
            {
                this.Walk(root, ref visited, ref open);
            }
        }
    }

    // A node's own facts in a row of rowWords words (Spread's seed, and each node a query with exclusions reaches, Without): its local keys,
    // its slot bits and, where the row has a class word, its classes as the fixed point solved them, which hold every node it read's.
    private void Seed(int node, Span<ulong> row)
    {
        var store = this.Of(node);
        var (from, to) = (this.Start(store, node), store[node]);
        foreach (var key in store.Keys[from.Keys..to.Keys])
        {
            row[key >> 6] |= 1UL << key;
        }

        foreach (var init in store.Inits[from.Inits..to.Inits])
        {
            if (init < 0)
            {
                row[this.keyWords + (~init >> 6)] |= 1UL << ~init;
            }
        }

        if (this.classWords > 0)
        {
            row[^1] |= this.classes[node];
        }
    }

    // An entry's initialization in the read graph, its one statement: an edge from the node that ensures it to each view of its initializer,
    // tagged with the entry's number; for an initializer with a validator finding, which has no node (step 2 skips it), one edge to none,
    // Derived (R5-M183, as Views).
    private void Initializer(int node, int init)
    {
        var initializer = this.program.Body(this.initBodies[init]);
        if (initializer.HasFinding)
        {
            this.Read(node, -1, init);
            return;
        }

        for (var mask = this.program.Drops.Holds(initializer.Ordinal) ? cases.Views(initializer, null) : 0; mask != 0; mask &= mask - 1)
        {
            var source = this.Base(initializer.Ordinal) + BitOperations.TrailingZeroCount(mask);
            if (this.atomCounts[source] != 0)
            {
                throw new InvalidOperationException($"The initializer of {initializer.Key.Decl!.Name} keeps an atom (InternalInvariant).");
            }

            this.Read(node, source, init);
        }
    }

    // A successor of a node in the read graph (MirEffects.Solve's reads): a node it read (init -1), or a view of an entry's initializer.
    private void Read(int node, int successor, int init)
    {
        MirDataflow.Grow(ref this.reads, this.readCount + 1);
        this.reads[this.readCount] = (successor, init, this.readHeads[node]);
        this.readHeads[node] = this.readCount++;
    }

    // The depth-first walk of Spread from an unvisited root along the read graph, finishing each component once every component it reads
    // is finished.
    private void Walk(int root, ref int visited, ref int open)
    {
        var depth = 0;
        for (var next = root; next >= 0;)
        {
            this.visits[next] = this.lows[next] = visited++;
            this.members[open++] = next;
            this.frames[depth++] = (next, this.readHeads[next]);
            next = -1;
            while (depth > 0 && next < 0)
            {
                ref var frame = ref this.frames[depth - 1];
                if (frame.Edge < 0)
                {
                    this.Finish(frame.Node, --depth, ref open);
                    continue;
                }

                var read = this.reads[frame.Edge].Node;
                frame.Edge = this.reads[frame.Edge].Next;
                if (read >= 0 && this.visits[read] < 0)
                {
                    next = read;
                }
                else if (read >= 0 && this.visits[read] != int.MaxValue)
                {
                    this.lows[frame.Node] = Math.Min(this.lows[frame.Node], this.visits[read]); // Open: on this path's component.
                }
            }
        }
    }

    // A node whose reads are all walked: its parent's low lowered to its own and, at its component's root, the component's row, the union of
    // its members' seeds and of the row of each node they read (a finished component's, or a member's seed), Derived for an edge to none,
    // written to every member.
    private void Finish(int node, int depth, ref int open)
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

        var (row, end) = (this.rowWords, open);
        var union = this.keyScratch.AsSpan(0, row);
        union.Clear();
        int member;
        do
        {
            member = this.members[--open];
            _ = MirDataflow.Union(union, this.keys.AsSpan(member * row, row));
            for (var r = this.readHeads[member]; r >= 0; r = this.reads[r].Next)
            {
                var read = this.reads[r].Node;
                if (read >= 0)
                {
                    _ = MirDataflow.Union(union, this.keys.AsSpan(read * row, row));
                }
                else
                {
                    union[^1] |= Derived;
                }
            }
        }
        while (member != node);

        foreach (var finished in this.members.AsSpan(open, end - open))
        {
            union.CopyTo(this.keys.AsSpan(finished * row, row));
            this.visits[finished] = int.MaxValue;
        }
    }
}
