// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding.ContractSolver's request boundary (the entry file states the contract; split from it without a change, R7-C2c2 c2-3b):
// the requests for Enumerate's rows (PremiseRefines, SPEC 8.3.2's refinement row; FirstRows, the first walk's rows, which the
// Callable and effect-bound readers read; PremiseRows, the candidate readers' rows over them; ExactAssumption, SPEC 8.3.2's Exact
// assumption over the leaf rows;
// PremiseUse, the premise-row locator a record reads),
// their shared frame (RowsRequest, EnterRows and its PrepareRows, LeaveRows), the extension's round (ExtendRound), the view's index,
// Closure(view) (IndexOf, IndexWritten,
// IndexNormalized, ViewIndex; a closure validator's read, BeginClosureRead to EndClosureRead; DropOwnedEntries, ClearClosure;
// R7-C2e2-1, R7-C2e2-2) and the tables a request owns or keeps (the request tables; the kept rows, ViewIndex.Rows: KeepRows,
// ForgetKeptTables, R7-C2e2 e2-3a). The walk the requests start (Enumerate and what it calls) is the entry file's.
public sealed partial class Binding
{
    // Closure(view) (the class header's): the entries the outermost request formed (dropped when it returns unless Kept); the running
    // pass's entry of each (view scope, requirement Contract), its T(G) entry (GroundsEntry), lent by position, and its free entries,
    // each with its lists and their capacity, are the pass's part's, handed over by EnterPass (enumerateIndex, enumerateGrounds,
    // enumerateGroundsPool, enumerateIndexPool below; PassPart.EnumerateIndex, PassPart.GroundsIndex, PassPart.GroundsPool).
    private readonly List<(EvidenceView, BindingSymbol?)> enumerateOwned = new();
    private readonly List<(BindingScope Scope, BoundConstraint Raw, BoundConstraint Fact)> enumerateNormalized = new();

    // A Contract's head depth (HeadDepth) and the walk's Complete supply instances (SupplyOf, G1406-30), one lifetime (the class
    // header's Kept rows): emptied at each publication's start in the capability phase (ForgetKeptTables), by each outermost request's
    // return before it and each pass (ClearClosure), capacity kept (ForgetHeads); the rows themselves are their entry's (ViewIndex.Rows).
    private readonly Dictionary<BindingSymbol, int> enumerateHeads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(BoundContract Shape, BoundType Root, BindingScope Scope, BindingSymbol? Requirement), (int Start, int Count)> enumerateSupplies = new();
    private readonly List<(int Ancestor, BoundContract Declaration, BoundConstraint? Conjunct, IsKoto? Clause)> enumerateSupplied = new(); // their ranges' items
    private readonly List<List<PremiseRow>> enumeratePool = new();
    private readonly List<PremiseRow> premiseRows = new(); // FirstRows' and PremiseRows' readers' rows, a stack of ranges
    private readonly List<IdentityRow> enumerateIdentities = new(); // c2-2: the outermost walk's identity rows, each with its source
    private readonly HashSet<(BoundType, BoundType, BindingSymbol)> enumerateIdentityKeys = new(); // their (pair, source), emptied with them
    private readonly Dictionary<BoundType, int> enumerateJoinKeys = new(ReferenceEqualityComparer.Instance); // O-533 (a): RowJoins' numbers of the round's class keys
    private readonly List<int> enumerateJoins = new(); // their union-find (each number's parent), emptied each round
    private readonly List<BoundType> enumerateDeferred = new(); // PLAN G881b: the round's mates row (5) did not walk (a cycle)
    private readonly List<BoundType> extendRoots = new(); // c2-4b: a closure's roots (CloseRoot, ExtendRound), a request table (LeaveRows)
    private Dictionary<(EvidenceView, BindingSymbol?), ViewIndex> enumerateIndex; // The running pass's Closure(view) entries.
    private Dictionary<(EvidenceView, BindingSymbol?), ViewIndex> enumerateGrounds; // e2-7 (i): the running pass's T(G) entries.
    private List<ViewIndex> enumerateGroundsPool; // their entries: a pass's k-th T(G) key takes the k-th (GroundsEntry)
    private Stack<ViewIndex> enumerateIndexPool; // The running pass's free entries.

    // Enumerate's request tables, owned by one outermost request (emptied, capacity kept, when it returns) and keyed by the view value:
    // the rows of a link of the subject's chain in a view (null while the link is open), each link's rows of the request's last round
    // (EnumerateClosed's seed; the two tables change places between rounds) and, above, the pooled lists, which belong to the tables:
    // each is emptied with them when the request returns (LeaveRows), so between requests the pool holds empty lists only.
    private Dictionary<(BoundType, EvidenceView, int), List<PremiseRow>?> enumerateMemo = new();
    private Dictionary<(BoundType, EvidenceView, int), List<PremiseRow>?> enumerateSeed = new();
    private int enumeratePoolNext;
    private int enumerateDepth;
    private int enumerateWalking;
    private bool enumerateMoved; // PLAN G881b: a link of the round closed with rows that are not its last round's
    private int enumerateJoined; // O-533 (a): the closure's rows that joined two classes as they were added (RowJoins), emptied with the rows

    // The outermost request's own scratch value (the class header's Kept rows): the meet of its walk's availability reads (WalkAvailable),
    // set at each outermost walk's start and read at its end through WalkComplete; no collection, so no store the library-layer dump lists.
    private Completion enumerateCompletion;
    private IdentityClasses? enumerateClasses; // R7-C2c2: the current request's view's identity classes, or null

    // The scaling rows' measure: the pairs Closure(view)'s entries indexed in this compilation, in every row (a fact of a row kind,
    // TypeIdentity or Semantics as written; a row kind's fact under its class key and a Contract fact as read), the validators' entries
    // included.
    internal int ClosureIndexWork { get; private set; }

    /// <summary>The budget of one root's closure, its rounds past a root's first walk and its re-rootings together, and its rows, this many per Type of the root and of its declaration's grounds (SPEC 8.3.3 Limits; the class header's Closure; O-532 (b)).</summary>
    internal const int PremiseClosureLimit = 32;

    // R7-C2c2 c2-2: the extension rounds of this compilation (the scaling rows' measure of the loop's bound).
    internal int IdentityRounds { get; private set; }

    // R7-C2c2 c2-4b: the re-rootings of this compilation's closures (ExtendRound; TypeIdentityTest's re-root rows).
    internal int IdentityRestarts { get; private set; }

    // PLAN G881b: the rounds EnumerateClosed walked again (a request through a cycle; the scaling rows' measure of its bound).
    internal int EnumerateRounds { get; private set; }

    // PLAN G881: row (5)'s work, the class members ProjectionMembers scanned and the mates walked (the scaling rows' measure).
    internal long IdentityMateWork { get; private set; }

    // The request boundary's one test of a walk's Completion (G1406-30): whether the outermost walk read only what is final in the
    // publication (every availability read it made was Complete, W6 and W8). Its readers keep what the walk formed past the request:
    // the keepers' rows (PremiseRefines and FirstRows, KeepRows) and a closure's filing (CloseRoot, FileClosure's serial).
    private bool WalkComplete => this.enumerateCompletion == Completion.Complete;

    // SPEC 8.3.2's refinement row over Enumerate's rows: an available one is a refinement of `contract`.
    private static bool RowsRefine(List<PremiseRow>? rows, BindingSymbol contract)
    {
        for (var i = 0; rows is not null && i < rows.Count; i++)
        {
            if (rows[i] is { Available: true, Symbol: { } row } && IsRefinement(row, contract))
            {
                return true;
            }
        }

        return false;
    }

    // The Alias column of a candidate reader's range (R7-C2c2 c2-3a). Rows whose Contract references are one Type in the request's
    // classes are one Contract (SPEC 8.2.3: `Getter<U>` and `Getter<S>` under `U is S`), by the canonical judge's Proven
    // (IdentityClasses.Same: decomposition and constructed members included; ClassKey is the supply's index of subjects, a key and no
    // judgment, so it cannot answer this). One of them stays non-Alias, the more determined spelling: a closed
    // one first (its members' signatures need no identity reading), then Binding.TypeIdentity's total Order; never premise order
    // (SPEC 8.3.3). Without classes, each reference is its own row; a candidate reader identifies references that are one Type by
    // another ground (SameNormalizedRequirement, IsSelectionCandidate). A leaf row has no reference and is never Alias.
    private static void FormAliases(List<PremiseRow> rows, int start, IdentityClasses? classes)
    {
        for (var r = start; classes is not null && r < rows.Count; r++)
        {
            if (rows[r].Symbol?.Type is not { } type)
            {
                continue;
            }

            var i = start;
            while (i < r && (rows[i].Alias || rows[i].Symbol?.Type is not { } other || !classes.Same(type, other)))
            {
                i++;
            }

            if (i < r)
            {
                var alias = Spelling(type, rows[i].Symbol!.Type!) < 0 ? i : r;
                rows[alias] = rows[alias] with { Alias = true };
            }
        }

        static int Spelling(BoundType a, BoundType b)
            => a.ContainsParameter != b.ContainsParameter ? (a.ContainsParameter ? 1 : -1) : Order(a, b);
    }

    // SPEC 8.3.2's refinement row: the premises of `scope` supply `subject is R` for a refinement R of `contract`, by an available
    // row. The subject is as its reader has it, walked beside its class key in the view (the contract's Identity), over the table its
    // reading reads (RowsRequest: the subject's roots closed at entry, e2-7). A kept hit answers (a kept entry is a closed root's). At a
    // Limited reading (R(A)) the request refines over its own entry, re-filed by the reading's keys and keeping no row, and an
    // unrefined one is refused (RefuseProof: AnalysisLimit, SPEC 8.3.3; R2.1's Requests row).
    private bool PremiseRefines(BoundType subject, BindingSymbol contract, BindingScope scope)
    {
        var (view, serial, classes, key, final, keep) = this.RowsRequest(subject, scope);
        var keyed = default((List<PremiseRow> Rows, ICheckCause? Limit));
        if (keep && this.enumerateIndex.TryGetValue((view, this.activeRequirementContract), out var entry) && entry.Serial == serial &&
            entry.Rows.TryGetValue(subject, out var kept) && (ReferenceEquals(key, subject) || entry.Rows.TryGetValue(key, out keyed)))
        {
            this.ScopeReads.Limit ??= kept.Limit ?? keyed.Limit; // W6: the limit the kept rows' walk wrote (Not replayed)
            return RowsRefine(kept.Rows, contract) || RowsRefine(keyed.Rows, contract);
        }

        var (index, prepared, enclosing) = this.EnterRows(subject, view, serial, classes, final);
        try
        {
            var (rows, keyRows, limit) = this.EnumerateClosed(subject, key, view, index);
            var refines = RowsRefine(rows, contract) || RowsRefine(keyRows, contract);
            if (!refines && this.enumerateDepth == 1)
            {
                this.ReadIdentityLimit(null, scope, classes);
            }

            if (keep && prepared && this.WalkComplete)
            {
                this.KeepRows(index, view, serial, subject, key, limit);
            }

            return refines;
        }
        finally
        {
            this.LeaveRows(enclosing);
        }
    }

    // e2-7 (G1406-11 C2, C-e; SPEC 8.3.3 Limits; the owner's O-345 (b)): a closure's round (CloseRoot's), one per call: from its roots
    // (extendRoots; first the part asked) over the declaration's grounds and the identity rows its own walks from those roots record
    // (ExtendedClassesOf under `serial`, identity facts alone: C-e), never the view's table or what a use's supply or an earlier
    // request left. The first round reads T(G), kept per serial; each later round builds the table from the rows so far, and a walk
    // whose rows join no two classes builds nothing (R3.4). When a round keys a root lower by Order, the root is that key and the closure starts
    // again with no rows, and a restart onto a root filed in the publication ends it (CloseRoot files the entry with what the filed
    // key spent, G1406-16 C2). Otherwise
    // the round returns its table and the entry its walk reads: a root's first walk reads the view's T(G) entry (GroundsEntry,
    // `grounds`), a later round `own`, a copy of it re-filed by the round's table (FileByTable); CloseRoot walks each root and its key.
    // e2-7 rows (b): a walk whose new rows join no two classes of its table (enumerateJoined unmoved; `closure.Joined`, its count at the
    // round's start, -1 before a root's first walk) leaves the next table's classes and keys as they are, so the next walk would
    // repeat it: the closure ends (null) with its rows, spending nothing. G1406-19: the closure has one budget,
    // PremiseClosureLimit, that its rounds past a root's first walk and its re-rootings spend together (`closure.Spent`, counted from
    // the part asked and never reset; `closure.Rooted`, what was spent when the last re-rooting started the current root's own
    // rounds). O-532 (b), O-533 (a): the same budget bounds the closure's rows that join two classes as they are added (enumerateJoined,
    // RowJoins; a row within one class adds no identity), at most `closure.Rows`, PremiseClosureLimit
    // per Type of its root and of the premises of the declaration's grounds G it reaches (T(G).TypesWith(root), the root's connected
    // part of G, G1406-27; set at the root's first walk): one count, a
    // function of (root, G) and linear in both, so a closure whose classes grow without bound (two recursive associates double them a
    // round) ends however its rounds go. Rows past it spend the whole budget. Past the budget the closure ends (null) and CloseRoot
    // files it Limited, so the round refuses nothing: a reader of a Limited root refuses (ReadIdentityLimit: AnalysisLimit_Kd, the
    // premise closure's bound). So a closure builds at most PremiseClosureLimit round tables beside T(G), however often it re-roots,
    // each from at most `closure.Rows` rows, and keeps at most those and its last round's walk's.
    // Returns the round's table and the entry its walk reads, or null when the closure ended or the table's build answered none.
    private (IdentityClasses Table, ViewIndex Entry)? ExtendRound(ref (int Joined, int Spent, int Rooted, int Rows) closure, BindingScope scope, int serial, EvidenceView view, ViewIndex grounds, ViewIndex own)
    {
        while (true)
        {
            var first = closure.Joined < 0; // a root's first walk, which spends nothing
            if (first)
            {
                this.enumerateIdentities.Clear(); // the closure starts (again) from its roots with no rows
                this.enumerateIdentityKeys.Clear();
                this.enumerateJoined = 0;
            }
            else if (this.enumerateJoined == closure.Joined)
            {
                return null; // e2-7 rows (b): the walk joined no two classes
            }
            else if (++closure.Spent > PremiseClosureLimit || this.enumerateJoined > closure.Rows)
            {
                closure.Spent = PremiseClosureLimit + 1; // the budget is spent: the closure is Limited (CloseRoot reads it); its reader refuses
                return null;
            }

            closure.Joined = this.enumerateJoined;
            if (this.ExtendedClassesOf(scope, serial, this.enumerateIdentities) is not { } extended)
            {
                return null;
            }

            if (first)
            {
                closure.Rows = PremiseClosureLimit * extended.TypesWith(this.extendRoots[0]); // T(G): the first walk reads no row
            }

            this.enumerateClasses = extended;
            this.enumerateJoinKeys.Clear(); // RowJoins numbers this round's keys: the table holds the earlier rounds' rows
            this.enumerateJoins.Clear();
            var restarted = false;
            for (var r = 0; r < this.extendRoots.Count; r++)
            {
                var key = this.ClassKey(this.extendRoots[r], extended);
                if (Order(key, this.extendRoots[r]) < 0)
                {
                    this.extendRoots[r] = key;
                    this.IdentityRestarts++;
                    restarted = true;
                }
            }

            if (restarted)
            {
                closure = (-1, closure.Spent + 1, closure.Spent + 1, closure.Rows); // a re-rooting spends one and ends this root's closure
                return null; // 5e': CloseRoot closes the key in its own frame (its own budget) and files this root over it
            }

            this.enumerateMemo.Clear();
            if (!first)
            {
                own.CopyFrom(grounds);
                this.FileByTable(own, view); // a later round reads its own table alone
            }

            this.IdentityRounds += first ? 0 : 1; // a root's first walk is no extension round
            return (extended, first ? grounds : own);
        }
    }

    // e2-7 (G1406-11 C2, C-e, C-f; SPEC 8.2.3 grounds, 8.3.3 Limits): the closure of the dependent abstract part `root` in `scope`'s
    // view under the reading's `serial`, CloseAsked's one closer (at the request boundary: no request is open). The root is its own key
    // in T(G) (CloseAsked closes a part's key, G1406-42 (2) (iii)), so its first round re-roots nothing. Its rounds (ExtendRound) walk
    // the root and its key over the table of the declaration's grounds and the rows so far, first T(G); each walk records its supplies
    // as rows (C-f). A lower key in a later round's table re-roots and restarts with no rows; a restart onto a root filed in the
    // publication ends it. A walk whose rows join no two classes ends it with its rows; past its one budget
    // (PremiseClosureLimit: rounds and re-rootings together, G1406-19, and rows per Type of the root and G, O-532 (b)) it is Limited
    // with the last round's rows, and its reader refuses (ReadIdentityLimit). Filed by FileClosure, which keeps E (the rows that join
    // the key's class), the entry's pair row and what the entry and its key spent (G1406-16 B1, C2). It records nothing: the scope's
    // premise error is its validation step's (RecordIdentityPremiseError, R3.2). A closure is a function of (root, G): no use's ask or
    // earlier request's rows enter it. Every proof limit its frame writes (its tables' builds, its walks, its key's nested closure,
    // which joins first) is set aside from the enclosing proof until both filings are made, so its filing keeps the first (W6;
    // FileClosure), which each read of the filing joins (CloseAsked): the asker that closes it and one that reads its filing rest on
    // the same cause (G1406-41's open point, G1406-43). Its first round walks the view's T(G) entry (GroundsEntry, kept per pass with
    // its formation's limit), so a closure forms, re-files and un-Keeps no request's entry (G1406-42 (2) (i)). That walk (one root:
    // a round's root is its own key in the round's table, ExtendRound re-roots otherwise), when Complete (KeepRows' rule), is kept with
    // a Kept entry as the root's whole walk: it started with no identity row, so the rows it recorded are the root's exactly, and its
    // first proof limit is kept with them (W6, CC-66's form: the root's alone, never a nested link's, recorded against its walk's
    // earlier rows). A later walk over the entry replays it at its memo miss on the root (Enumerate; G1406-43 (3)).
    // Returns the closure.
    private int CloseRoot(BindingScope scope, int serial, BoundType root)
    {
        var outer = this.SetAsideLimit(); // the frame's own limit is ScopeReads.Limit until both filings are made (FileClosure)
        try
        {
            var view = this.ViewOf(scope);
            var enclosing = this.EnterRequest(null);
            var grounds = this.GroundsEntry(scope, view, serial);
            var own = this.enumerateIndexPool.Count != 0 ? this.enumerateIndexPool.Pop() : new(); // a later round's entry (ExtendRound)
            this.StartWalk();
            (int Joined, int Spent, int Rooted, int Rows) closure;
            BoundType key;
            Completion completion;
            int kept; // the serial the closure is kept for, -1: not kept (FileClosure)
            try
            {
                this.extendRoots.Add(root);
                closure = (Joined: -1, Spent: 0, Rooted: 0, Rows: 0);
                while (this.ExtendRound(ref closure, scope, serial, view, grounds, own) is { } round)
                {
                    var at = this.extendRoots[0];
                    var (rows, _, limit) = this.EnumerateClosed(at, this.ClassKey(at, round.Table), view, round.Entry);
                    if (ReferenceEquals(round.Entry, grounds) && grounds.Kept && this.WalkComplete && rows is { } walked)
                    {
                        grounds.KeepWalk(at, walked, limit, this.enumerateIdentities); // e2-7 (ii): the root's whole walk over T(G)
                    }
                }

                key = this.extendRoots[0];
                (completion, kept) = (this.enumerateCompletion, this.WalkComplete ? serial : -1);

                // Rooted 0: no re-rooting, so the key is the root.
                if (closure.Rooted == 0 || this.identityRoots.ContainsKey((scope, key)))
                {
                    return this.FileClosure(scope, root, key, closure.Spent, kept, completion);
                }
            }
            finally
            {
                this.LeaveRows(enclosing);
                this.enumerateIndexPool.Push(own);
            }

            this.CloseRoot(scope, serial, key); // 5e': an unfiled key's own closure, with its own budget: a function of (key, G)
            return this.FileClosure(scope, root, key, closure.Spent, kept, completion);
        }
        finally
        {
            this.TakeOwnLimit(outer); // W6: the enclosing proof joins the frame's own limit after it
        }
    }

    // A request's own entry over a Limited reading (EnterRows): the view's entry reopened as the request's (dropped when it returns,
    // DropOwnedEntries) and re-filed by its table (FileByTable).
    private void OwnEntry(ViewIndex index, EvidenceView view)
    {
        this.enumerateOwned.Add((view, this.activeRequirementContract));
        this.FileByTable(index, view);
    }

    // An entry re-filed by the open request's table (enumerateClasses): OwnEntry's, and a closure's later round's copy of its T(G)
    // entry (ExtendRound, never the view's entry, so not registered as owned): not Kept, the class-key pairs an earlier table filed
    // there dropped and the view's facts filed under the table's keys (IndexClassKeys), so its walk reads its own table alone. The
    // facts as written and their readings stay as filed (DropClassKeys), so each is under its subject once (G1406-49 (4)).
    private void FileByTable(ViewIndex index, EvidenceView view)
    {
        index.Kept = false;
        index.DropClassKeys();
        this.IndexClassKeys(index, view);
    }

    // e2-7 (i) (G1406-42 (2)): the view's T(G) entry, its closures' first round (CloseRoot's preparation, before its walk starts: a
    // reading demands nested requests): the view's facts as written and under T(G)'s keys in one filing, as IndexOf files a request's
    // entry under its table's keys, then its readings (IndexNormalized, as a request's preparation forms them). It is never a request's
    // entry (RowsRequest keys by its reading's table), so a closure leaves a request's Kept entry Kept. Kept for the pass
    // (ForgetKeptTables, ClearClosure) while T(G) is memoized for `serial` and every reading it formed was kept, read as IndexOf reads
    // a Kept entry (readings only in KeptRegion); otherwise formed again in place. The first proof limit its formation wrote (its
    // readings', T(G)'s) is kept with it and joined at each read (W6, G1406-43 (2)), so a closure reading it rests on what one that
    // formed it rests on. Its entry is lent by position, as a pass's inline summaries are (Binding.InlineLayouts.cs): a pass's k-th
    // key takes GroundsPool's k-th entry (enumerateGrounds only grows within a pass), so a warm pass forms each view's entry in the
    // object, and over the capacity, that formed it in the pass before; a shared free entry would come back in another role each pass.
    private ViewIndex GroundsEntry(BindingScope scope, EvidenceView view, int serial)
    {
        var key = (view, this.activeRequirementContract);
        if (this.enumerateGrounds.TryGetValue(key, out var entry) && entry.Serial == serial && entry.Kept && (!entry.Readings || this.KeptRegion))
        {
            this.ScopeReads.Limit ??= entry.Limit;
            return entry;
        }

        if (entry is null)
        {
            var count = this.enumerateGrounds.Count;
            if (count == this.enumerateGroundsPool.Count)
            {
                this.enumerateGroundsPool.Add(new());
            }

            entry = this.enumerateGroundsPool[count];
            this.enumerateGrounds[key] = entry;
        }

        var outer = this.SetAsideLimit();
        var (formations, stores) = (this.readingFormations, this.readingStores);
        entry.Open(serial);
        this.enumerateClasses = this.GroundsOf(scope, serial) ?? throw new InvalidOperationException("T(G) re-entered at a closure's entry (InternalInvariant; e2-7).");
        this.IndexWritten(entry, view); // as written and under T(G)'s keys
        this.enumerateClasses = null;
        if (this.IndexNormalized(entry, view) != serial)
        {
            throw new InvalidOperationException("A closure's T(G) entry moved its own view (InternalInvariant; e2-7).");
        }

        entry.Limit = this.TakeOwnLimit(outer);
        entry.Kept = this.identityGrounds[scope].Serial == serial && this.readingFormations - formations == this.readingStores - stores;
        return entry;
    }

    // PLAN G881b (SPEC 8.2.3 Cycles, 8.3.3): the rows of a request's subject and of its class key as the walk's least fixpoint, the
    // requests' one entry to the walk. A round walks the subject and its key, then each mate row (5) deferred (its chain held an
    // open link: a cycle), from the top, where no link is open; a deferred mate's walk may defer others and records no identity row.
    // A round that deferred a mate is walked again, its links' rows the seed a deferred mate supplies, until no link closes with
    // rows that are not its own last round's (the per-link stop test: Enumerate compares each link with its own seed). A round
    // that deferred none is exact, so a request through no cycle walks once. Each round visits the same links in the same order and
    // each link's rows are monotone in what it reads, so a link's rows only grow from round to round and the last round is the least
    // fixpoint of the links' equations: its rows are a function of (link, view) and are kept as a single walk's (KeepRows). Bound:
    // the (link, row) pairs of the request plus one. Also returns the walk's own first proof limit (W6, SetAsideLimit; the class
    // header's Not replayed), which KeepRows keeps with the rows.
    private (List<PremiseRow>? Rows, List<PremiseRow>? KeyRows, ICheckCause? Limit) EnumerateClosed(BoundType subject, BoundType key, EvidenceView view, ViewIndex index)
    {
        var outer = this.SetAsideLimit();
        while (true)
        {
            this.enumerateMoved = false;
            this.enumerateDeferred.Clear();
            var rows = this.Enumerate(subject, view, index);
            var keyRows = ReferenceEquals(key, subject) ? null : this.Enumerate(key, view, index);

            // A deferred mate's walk keeps the identity rows it records (R1.1's cycle stop bounds them; R1.4 C5).
            for (var d = 0; d < this.enumerateDeferred.Count; d++)
            {
                this.IdentityMateWork++;
                this.Enumerate(this.enumerateDeferred[d], view, index);
            }

            if (this.enumerateDeferred.Count == 0 || !this.enumerateMoved)
            {
                this.enumerateSeed.Clear();
                return (rows, keyRows, this.TakeOwnLimit(outer));
            }

            (this.enumerateSeed, this.enumerateMemo) = (this.enumerateMemo, this.enumerateSeed);
            this.enumerateMemo.Clear();
            this.EnumerateRounds++;
        }
    }

    // R7-C2c2 c2-3b, c2-5b, c2-5b2: SPEC 8.3.2's Exact assumption over the rows, Supply's one rows reader, for a proposition about S
    // and its negation (`negation`), normalized by its caller (Supply's), both polarities in one request. Its domain is the rows' one
    // decision: the atom (RowAtom) of the Not row that would state the proposition or its negation, so every atom and no conjunction,
    // disjunction or Unresolved proposition. An available leaf row of S (a Callable row, a negation's Not row) supplies the
    // proposition where its atom (RowAtom), re-subjected to S (the rows matched it) and read as the proposition's intake reads it
    // (NormalizeProofConstraint: each Type and the Contract reference's arguments in their class's form, a Callable signature as the
    // Callable selection reads it), is the proposition's atom (SameProofPremise) and their polarities agree, and its negation where
    // they differ.
    private (bool Positive, bool Negative) ExactAssumption(BoundConstraint proposition, BoundConstraint negation, BindingScope scope)
    {
        if (RowAtom(proposition.Kind == ConstraintKind.Not ? proposition : negation) is not { Subject: { } subject } atom)
        {
            return default;
        }

        var start = this.premiseRows.Count;
        this.FirstRows(subject, scope, this.premiseRows);
        var (positive, negative) = (false, false);
        for (var i = start; !(positive && negative) && i < this.premiseRows.Count; i++)
        {
            if (this.premiseRows[i] is { Available: true, Clause: { } clause } && RowAtom(clause) is { } row && row.Kind == atom.Kind && row.Mask == atom.Mask &&
                this.SameProofPremise(this.NormalizeProofConstraint(this.InternConstraint(new(row.Kind, subject, row.RequiredType, row.Contract, row.Mask)), scope, out _), atom))
            {
                var agrees = (clause.Kind == ConstraintKind.Not) == (proposition.Kind == ConstraintKind.Not);
                (positive, negative) = (positive || agrees, negative || !agrees);
            }
        }

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);
        return (positive, negative);
    }

    // R7-C2c2 c2-3b: the request boundary of a reader that demands no Contract: the rows of `subject` and of its class key under
    // the premises of `scope`, appended to `into`, each row once (AddRow), whatever their availability and kind. They are the first
    // walk's rows, a function of (subject, view): a kept hit (a closed root's, the class header's Closure) reads
    // the kept rows, which are a first walk's, and joins the limit their walk wrote (W6, the class header's Not replayed). No row
    // is Alias and no identity is judged here (the walk's class keys alone).
    // Returns the request's identity classes, in which PremiseRows forms its column. G1406-12, e2-7 R3.3 (SPEC 8.3.3 Limits): it answers
    // no judgment. Its rows at a Limited reading are cut at the round bound, so a reader whose answer the cut rows could make negative
    // (CallableSelection, ValueCalls, EffectDeclarations, CallableEffects; Retention at `true`, G1406-16 C3; discovery's miss:
    // RequirementMember, G1406-57) refuses through ReadIdentityLimit with these classes and its asking node, once per reading;
    // ExactAssumption and the adapter answer no rows judgment, and neither does discovery's found member (PremiseRows), whose use is
    // judged afterwards.
    private IdentityClasses? FirstRows(BoundType subject, BindingScope scope, List<PremiseRow> into)
    {
        var (view, serial, classes, key, final, keep) = this.RowsRequest(subject, scope);
        var start = into.Count;
        var keyed = default((List<PremiseRow> Rows, ICheckCause? Limit));
        if (keep && this.enumerateIndex.TryGetValue((view, this.activeRequirementContract), out var entry) && entry.Serial == serial &&
            entry.Rows.TryGetValue(subject, out var kept) && (ReferenceEquals(key, subject) || entry.Rows.TryGetValue(key, out keyed)))
        {
            this.ScopeReads.Limit ??= kept.Limit ?? keyed.Limit;
            Append(into, start, kept.Rows);
            Append(into, start, keyed.Rows);
            return classes;
        }

        var (index, prepared, enclosing) = this.EnterRows(subject, view, serial, classes, final);
        try
        {
            var (rows, keyRows, limit) = this.EnumerateClosed(subject, key, view, index);
            if (keep && prepared && this.WalkComplete)
            {
                this.KeepRows(index, view, serial, subject, key, limit);
            }

            Append(into, start, rows);
            Append(into, start, keyRows);
        }
        finally
        {
            this.LeaveRows(enclosing);
        }

        return classes;

        static void Append(List<PremiseRow> into, int start, List<PremiseRow>? rows)
        {
            for (var r = 0; rows is not null && r < rows.Count; r++)
            {
                AddRow(into, start, rows[r]);
            }
        }
    }

    // R7-C2c2 c2-3a: the candidate readers' rows (name discovery: FindAssociated, RequirementMember): FirstRows' rows, whatever
    // their availability (a lookup is not a proof: the use is judged, SPEC 9.6, 8.5.2), with one non-Alias row per Contract
    // (FormAliases). A candidate reader demands no Contract, so it never extends. Returns FirstRows' classes, with which a miss
    // refuses (G1406-57).
    private IdentityClasses? PremiseRows(BoundType subject, BindingScope scope, List<PremiseRow> into)
    {
        var start = into.Count;
        var classes = this.FirstRows(subject, scope, into);
        FormAliases(into, start, classes);
        return classes;
    }

    // The premise-row locator (R7-E3 E3b-1 and E3c-1, one component; DECISIONS R7-M75): the location a record relates for a premise.
    // - Input: a premise row and the view it was read in, or a written fact and the scope stating it.
    // - Output: the written use of its fact: in the stating declaration's clause that holds the fact (a conjunction's part included),
    //   the requirement naming its bound Contract reference, else that clause (a premise of another kind, such as a Callable one, has
    //   no Contract: its clause); else the stating scope's owner (a clause instance's or an ancestor's: its Contract row's scope), else
    //   the view's owner.
    // - Prohibitions: no proof and no search beyond the stating declaration's own clauses; read only when a record is written.
    private Koto PremiseUse(PremiseRow row, BindingScope view)
        => row is { Raw: { } raw, Stated: { } where } ? this.PremiseUse(raw, where) : row.Stated?.Owner ?? view.Owner;

    private Koto PremiseUse(BoundConstraint fact, BindingScope scope)
    {
        var clauses = scope.Owner is FunctionKoto function ? function.TypeConstraints : scope.Owner is DeclarationContainerKoto container ? container.ConstraintNodes : [];
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is IsKoto clause && Contains(clause.BoundConstraint, fact))
            {
                return fact.Contract is { } contract ? ContractUse(clause.Right, contract) ?? clause : clause;
            }
        }

        return scope.Owner;

        static bool Contains(BoundConstraint? root, BoundConstraint fact)
            => ReferenceEquals(root, fact) || (root?.Kind == ConstraintKind.And && (Contains(root.Left, fact) || Contains(root.Right, fact)));

        // The requirement naming the premise's bound Contract reference (ContractReferenceOf: its Type, else its selection).
        Koto? ContractUse(Koto node, BindingSymbol contract)
            => ReferenceEquals(this.ContractReferenceOf(node), contract) ? node
            : node is BinaryKoto { Akind: KotoKind.And } conjunction ? ContractUse(conjunction.Left, contract) ?? ContractUse(conjunction.Right, contract)
            : node is ParenthesizedKoto parentheses ? ContractUse(parentheses.Operand, contract) : null;
    }

    // A request's terms (PremiseRefines' and FirstRows'): its view, the view's identity classes, the subject's key in them, whether
    // those keys are final (a function of the view and the pass) and whether its rows may be kept. R7-C2c2: a request keys its
    // subject and its view's facts by the view's identity classes (SPEC 8.2.3), whatever the table's finality; finality decides
    // only whether the rows and the view's entry may be kept for a later request.
    private (EvidenceView View, int Serial, IdentityClasses? Classes, BoundType Key, bool Final, bool Keep) RowsRequest(BoundType subject, BindingScope scope)
    {
        if (this.enumerateWalking > 0)
        {
            throw new InvalidOperationException($"A request for the rows of `{subject}` entered from Enumerate's walk or a closure validator's read (InternalInvariant; R7-C2a).");
        }

        var classes = this.RequestClasses(scope, subject);
        var key = this.RequestKey(subject, classes);
        var view = this.ViewOf(scope);
        var serial = SerialOf(scope);
        var final = classes is null ? !HasTypeIdentity(scope) : classes.Final && !classes.Limited; // a Limited reading keeps nothing
        return (view, serial, classes, key, final, this.enumerateDepth == 0 && this.KeptRegion && final);
    }

    // The identity classes a request over `scope` keys by (null: no identity premise in the view's chain) and a subject's key in them,
    // the supply's index of subjects: one rule for the requests (RowsRequest) and for a reader that asks once per key (the arithmetic
    // direction check's seeds).
    private IdentityClasses? RequestClasses(BindingScope scope, BoundType? subject) => HasTypeIdentity(scope) ? this.ClassesOf(scope, [subject], out _) : null;

    private BoundType RequestKey(BoundType subject, IdentityClasses? classes) => classes is null ? subject : this.ClassKey(subject, classes);

    // A request enters its walk (PremiseRefines' and FirstRows' frame, which LeaveRows closes): its view's classes are the walk's,
    // it is one request deeper, its preparation (PrepareRows) runs, and its walk starts with no identity row recorded and Complete
    // until a read meets less (the class header's Kept rows). Returns the entry, whether the preparation formed no reading and the
    // enclosing request's classes.
    private (ViewIndex Index, bool Prepared, IdentityClasses? Enclosing) EnterRows(BoundType subject, EvidenceView view, int serial, IdentityClasses? classes, bool final)
    {
        var enclosing = this.EnterRequest(classes);
        var (index, prepared) = this.PrepareRows(subject, view, serial, final);
        if (classes is { Limited: true })
        {
            this.OwnEntry(index, view); // a Limited reading is the request's table alone (G1406-11 C-a): its entry keys by it
        }

        this.StartWalk();
        return (index, prepared, enclosing);
    }

    // A request's frame opens (EnterRows', CloseRoot's; LeaveRows closes it): its classes are the walk's and it is one request deeper.
    // Returns the enclosing request's classes.
    private IdentityClasses? EnterRequest(IdentityClasses? classes)
    {
        var enclosing = this.enumerateClasses;
        this.enumerateClasses = classes;
        this.enumerateDepth++;
        return enclosing;
    }

    // An entered request's walk starts after its preparation: no request starts inside it (RowsRequest), and it starts with no
    // identity row recorded and Complete until a read meets less (the class header's Kept rows).
    private void StartWalk()
    {
        this.enumerateWalking++;
        this.enumerateIdentities.Clear();
        this.enumerateIdentityKeys.Clear();
        this.enumerateJoined = 0;
        this.enumerateCompletion = Completion.Complete;
    }

    // An entered request's preparation: the view's entry in Closure(view) and, for an outermost request whose entry is not Kept, the
    // view's readings in it, after which the entry is Kept when every reading the preparation formed was kept and the request's class
    // keys are `final` (R7-C2e2-1's N1). Returns the entry and whether the preparation formed no reading (the rows may then be kept:
    // every reading it read was a kept one).
    private (ViewIndex Index, bool Keep) PrepareRows(BoundType subject, EvidenceView view, int serial, bool final)
    {
        var index = this.IndexOf(view, serial);

        // The walk demands nothing, so a nested demand comes from the outermost request's preparation: it reads the written facts
        // alone, and the rows it left are dropped before the outermost walk reads the whole index. The request's premises are the
        // view it took: a demand of its preparation that moved them is a builder's broken contract. A Kept entry needs no preparation.
        if (this.enumerateDepth != 1)
        {
            return (index, true);
        }

        var keep = true;
        if (!index.Kept)
        {
            var (formations, stores) = (this.readingFormations, this.readingStores);
            if (this.IndexNormalized(index, view) != serial)
            {
                throw new InvalidOperationException($"The preparation of the request for `{subject}` moved its own view (InternalInvariant; R7-C2a).");
            }

            keep = formations == this.readingFormations; // Every reading of the preparation was a kept one.

            // Every reading it formed was kept, and its class keys are final (R7-C2e2-1's N1).
            index.Kept = final && this.readingFormations - formations == this.readingStores - stores;
        }

        this.enumerateMemo.Clear();
        return (index, keep);
    }

    // A request returns: its walk has ended, its enclosing request's classes are back, and the outermost one empties the request
    // tables, the pooled link lists it lent and the extension's roots among them (capacity kept: no row outlives its request), and
    // drops the entries it formed unless Kept.
    private void LeaveRows(IdentityClasses? enclosing)
    {
        this.enumerateWalking--;
        this.enumerateClasses = enclosing;
        if (--this.enumerateDepth == 0)
        {
            this.enumerateMemo.Clear();
            this.enumerateSeed.Clear();
            this.extendRoots.Clear();
            this.DropOwnedEntries();
            for (var i = 0; i < this.enumeratePoolNext; i++)
            {
                this.enumeratePool[i].Clear(); // the tables' contract above: a request's rows do not outlive it
            }

            this.enumeratePoolNext = 0;
            if (!this.KeepsAcrossRequests)
            {
                this.ForgetHeads(); // Before the capability phase the head depths and supplies live for this request.
            }
        }
    }

    // The outermost walk's rows of the view, kept in its Kept entry (pooled copies), each marked with whether the walk recorded an
    // identity row: a link's own walk records a subset of its request's. A request that deferred a mate (PLAN G881b) marks them too: a
    // deferred walk records none, so a link only it walked may record one in its own request. Each carries the first proof limit the
    // walk wrote (W6, the class header's Not replayed), which a read of it joins; a walk that wrote one keeps its request's subject
    // and key links alone, read by their memo keys, since another link's own walk may write none (F's CC-66 B1; a key that is the
    // subject is kept once, as KeepRow keeps a link's first copy).
    private void KeepRows(ViewIndex index, EvidenceView view, int serial, BoundType subject, BoundType key, ICheckCause? limit)
    {
        if (limit is not null)
        {
            Keep(subject);
            Keep(key);
            return;
        }

        foreach (var ((link, linkView, linkSerial), rows) in this.enumerateMemo)
        {
            if (rows is not null && linkView == view && linkSerial == serial)
            {
                index.KeepRow(link, rows, null);
            }
        }

        void Keep(BoundType link)
        {
            if (this.enumerateMemo.TryGetValue((link, view, serial), out var rows) && rows is not null)
            {
                index.KeepRow(link, rows, limit);
            }
        }
    }

    // A publication of the source states starts (PublishSourceStates, their one writer): every table kept in the capability phase
    // may rest on a published state, so each is forgotten, the entries and identity tables in place (the same entry per view, warm 0 B).
    private void ForgetKeptTables()
    {
        this.readings.Clear();
        foreach (var index in this.enumerateIndex.Values)
        {
            index.Forget();
        }

        foreach (var index in this.enumerateGrounds.Values)
        {
            index.Forget();
        }

        this.ForgetIdentityClasses();
        this.ForgetHeads();
    }

    // The entry of the view value in Closure(view): when the view has none at its serial, its entry is formed in place with the written
    // facts, owned by the outermost request until Kept. A Kept entry's readings are read only in KeptRegion (R7-M20): outside it an
    // outermost request forms its own readings, so it forms the entry again (a nested demand reads written facts alone).
    private ViewIndex IndexOf(EvidenceView view, int serial)
    {
        var key = (view, this.activeRequirementContract);
        if (this.enumerateIndex.TryGetValue(key, out var index) && index.Serial == serial &&
            (!index.Readings || this.enumerateDepth != 1 || this.KeptRegion))
        {
            return index;
        }

        index ??= this.enumerateIndexPool.Count != 0 ? this.enumerateIndexPool.Pop() : new();
        index.Open(serial);
        this.enumerateIndex[key] = index;
        this.enumerateOwned.Add(key);
        this.IndexWritten(index, view);
        return index;
    }

    // The view's facts as written into its entry, each row's under its subject and a fact with a row atom (RowAtom) under its atom's
    // subject, then under the open table's keys (IndexClassKeys). A bucket's order is Add's, whatever the filing order of the two.
    private void IndexWritten(ViewIndex index, EvidenceView view)
    {
        for (var scope = view.Premises; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (RowAtom(fact) is { Subject: { } subject })
                {
                    index.Add(scope, fact, fact, subject);
                    this.ClosureIndexWork++;
                }
                else if (fact is { Kind: ConstraintKind.TypeIdentity or ConstraintKind.Semantics, Subject: not null })
                {
                    index.Add(scope, fact, fact, fact.Subject);
                    this.ClosureIndexWork++;
                }
            }
        }

        this.IndexClassKeys(index, view);
    }

    // The view's facts with a row atom (RowAtom) whose subject's class key in the open table (enumerateClasses) is another Type, filed
    // also under that key. R7-C2c2 c2-1 meets R7-C2e2-2 here: the class-key copy is the row kinds' alone (c2-3b: a leaf kind's fact
    // too). The other rows keep written subjects, so the validators, which pair facts by bucket, never meet a fact of another written
    // subject.
    private void IndexClassKeys(ViewIndex index, EvidenceView view)
    {
        if (this.enumerateClasses is not { } classes)
        {
            return;
        }

        for (var scope = view.Premises; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (RowAtom(fact) is { Subject: { } subject } && this.ClassKey(subject, classes) is var at && !ReferenceEquals(at, subject))
                {
                    index.Add(scope, fact, fact, at);
                    this.ClosureIndexWork++;
                }
            }
        }
    }

    // A closure validator's read of the view `scope` denotes (the class header's Readers): outside any request, and until
    // EndClosureRead nothing may start one. The entry is a Kept one or the read's own.
    private ViewIndex BeginClosureRead(BindingScope scope)
    {
        if (this.enumerateDepth != 0 || this.enumerateWalking != 0)
        {
            throw new InvalidOperationException("A closure validator's read was entered from a request or another read (InternalInvariant; R7-C2e2-2).");
        }

        var index = this.IndexOf(this.ViewOf(scope), SerialOf(scope));
        this.enumerateWalking++;
        return index;
    }

    // The read ended: an entry it formed goes back to the pool.
    private void EndClosureRead()
    {
        this.enumerateWalking--;
        this.DropOwnedEntries();
    }

    // The outermost request returned: the entries it formed go back to the pool unless Kept.
    private void DropOwnedEntries()
    {
        foreach (var key in this.enumerateOwned)
        {
            if (this.enumerateIndex.TryGetValue(key, out var index) && !index.Kept)
            {
                this.enumerateIndex.Remove(key);
                this.enumerateIndexPool.Push(index);
            }
        }

        this.enumerateOwned.Clear();
    }

    // The pass ended (ResetCapabilities, as the pass runs again): every request entry of the pass goes back to the pass's pool, the T(G)
    // entries stay lent by position (GroundsEntry), and the head depths and supplies go.
    private void ClearClosure()
    {
        foreach (var index in this.enumerateIndex.Values)
        {
            this.enumerateIndexPool.Push(index);
        }

        this.enumerateIndex.Clear();
        this.enumerateGrounds.Clear();
        this.ForgetHeads();
    }

    // HeadDepth's and SupplyOf's tables go (their one lifetime, enumerateHeads), capacity kept.
    private void ForgetHeads()
    {
        this.enumerateHeads.Clear();
        this.enumerateSupplies.Clear();
        this.enumerateSupplied.Clear();
    }

    // The outermost request's view: each Contract fact whose reading can differ from it (R7-C2c2 c2-5c: ReadingCanDiffer, J1's test,
    // per environment, then per fact) as the view reads it (Reading, every reader's; a leaf row's fact is indexed as written and by its
    // class key alone, the class header's Identity), added after all are read. A reading's own demands are nested, so they read written
    // facts alone: the index does not depend on the order of the facts.
    private int IndexNormalized(ViewIndex index, EvidenceView view)
    {
        int? after = index.Serial; // The loop writes nothing but through Reading.
        var typeIdentity = HasTypeIdentity(view.Premises);
        var start = this.enumerateNormalized.Count;
        for (var scope = view.Premises; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } environment || !ReadingCanDiffer(environment.HasAssociatedProjection, typeIdentity))
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                // R7-M25 (D), R7-C3 C6: a reading whose subject has no abstract part is not filed; a concrete constructed member's
                // conformance is ResolveConformance's (never Unknown), so its row would repeat it or be a premise error.
                if (fact is { Kind: ConstraintKind.Contract, Subject: not null, Contract: not null } && ReadingCanDiffer(fact.HasAssociatedProjection, typeIdentity) &&
                    this.Reading(fact, view.Premises, ref after) is { Subject.HasAbstractPart: true } normalized && !ReferenceEquals(normalized, fact))
                {
                    this.enumerateNormalized.Add((scope, fact, normalized));
                }
            }
        }

        for (var i = start; i < this.enumerateNormalized.Count; i++)
        {
            var (scope, raw, fact) = this.enumerateNormalized[i];
            index.Add(scope, raw, fact, fact.Subject!);
        }

        this.ClosureIndexWork += this.enumerateNormalized.Count - start;
        this.enumerateNormalized.RemoveRange(start, this.enumerateNormalized.Count - start);
        return after ?? SerialOf(view.Premises);
    }

    // An entry of Closure(view) (the class header's): the serial it was formed at, whether it is Kept, its buckets by (chain scope,
    // kind, SubjectKey) and its kept rows by link. Its lists stay with it, capacity kept.
    internal sealed class ViewIndex
    {
        private readonly List<List<(BoundConstraint Raw, BoundConstraint Fact)>> lists = new();
        private readonly List<List<PremiseRow>> rowLists = new();
        private readonly List<List<IdentityRow>> walkLists = new();
        private int used;
        private int rowsUsed;
        private int walksUsed;

        internal int Serial { get; private set; }

        internal bool Kept { get; set; }

        internal bool Readings { get; private set; }

        internal Dictionary<(BindingScope Scope, ConstraintKind Kind, BindingSymbol? Symbol, BoundType Type), List<(BoundConstraint Raw, BoundConstraint Fact)>> Buckets { get; }
            = new();

        // The kept rows (the class header's Kept rows): a link's rows, walked over closed roots (e2-7), so a kept hit answers, and the
        // first proof limit its walk wrote (W6).
        internal Dictionary<BoundType, (List<PremiseRow> Rows, ICheckCause? Limit)> Rows { get; } = new(ReferenceEqualityComparer.Instance);

        // A T(G) entry's kept whole walks (Binding.CloseRoot; e2-7 (ii)): the identity rows a closure's first walk recorded from its
        // root, in order, beside the root's Rows, replayed at a walk's memo miss on the root (Binding.Enumerate).
        internal Dictionary<BoundType, List<IdentityRow>> Walks { get; } = new(ReferenceEqualityComparer.Instance);

        // A T(G) entry's formation's first proof limit (Binding.GroundsEntry; W6), joined at each read of the kept entry.
        internal ICheckCause? Limit { get; set; }

        // A publication started: the entry is formed again in place at its view's next read.
        internal void Forget() => this.Serial = -1;

        internal void Open(int serial)
        {
            this.Serial = serial;
            this.Kept = false;
            this.Readings = false;
            this.Buckets.Clear();
            this.used = 0;
            this.Rows.Clear();
            this.rowsUsed = 0;
            this.Walks.Clear();
            this.walksUsed = 0;
        }

        // A link's rows, copied into a pooled list; a link already kept keeps its first copy.
        internal void KeepRow(BoundType link, List<PremiseRow> rows, ICheckCause? limit)
        {
            if (this.Rows.ContainsKey(link))
            {
                return;
            }

            if (this.rowsUsed == this.rowLists.Count)
            {
                this.rowLists.Add(new(rows.Count));
            }

            var copy = this.rowLists[this.rowsUsed++];
            copy.Clear();
            copy.AddRange(rows);
            this.Rows.Add(link, (copy, limit));
        }

        // A closure's whole walk from `link` (Binding.CloseRoot): its rows as KeepRow keeps them and its identity rows, copied into a
        // pooled list; a link already kept keeps its first.
        internal void KeepWalk(BoundType link, List<PremiseRow> rows, ICheckCause? limit, List<IdentityRow> identities)
        {
            if (this.Walks.ContainsKey(link))
            {
                return;
            }

            if (this.walksUsed == this.walkLists.Count)
            {
                this.walkLists.Add(new(identities.Count));
            }

            var copy = this.walkLists[this.walksUsed++];
            copy.Clear();
            copy.AddRange(identities);
            this.Walks.Add(link, copy);
            this.KeepRow(link, rows, limit);
        }

        // A closure's later round's entry (Binding.ExtendRound) starts as its first round's: `first`'s buckets copied into this
        // entry's own lists in their order, with no kept row; FileByTable then re-files it by the round's table.
        internal void CopyFrom(ViewIndex first)
        {
            this.Open(first.Serial);
            foreach (var (key, list) in first.Buckets)
            {
                if (this.used == this.lists.Count)
                {
                    this.lists.Add(new(list.Count));
                }

                var copy = this.lists[this.used++];
                copy.Clear();
                copy.AddRange(list);
                this.Buckets.Add(key, copy);
            }

            this.Readings = first.Readings;
        }

        // An extension's walking round (Binding.ExtendRound) reads its own table's keys alone: the pairs filed under another subject
        // key than their filing atom's (FilingAtom; an earlier table's class-key pairs) go; the facts as written and their readings
        // stay, each under its filing atom's subject, in their order.
        internal void DropClassKeys()
        {
            foreach (var (key, list) in this.Buckets)
            {
                var kept = 0;
                for (var i = 0; i < list.Count; i++)
                {
                    var own = SubjectKey(FilingAtom(list[i].Fact).Subject!);
                    if (ReferenceEquals(own.Symbol, key.Symbol) && ReferenceEquals(own.Type, key.Type))
                    {
                        list[kept++] = list[i];
                    }
                }

                list.RemoveRange(kept, list.Count - kept);
            }
        }

        // One fact of an environment of the view in its own kind's bucket under the subject key of `at` (a projection's declaration
        // and receiver, else the Type; its filing atom's subject, FilingAtom, or its class key): `raw` as the environment holds it (its
        // availability), `fact` as the index reads it. A fact with a row atom (RowAtom) is in its kind's row, a negated atom in the
        // Not row (R7-C2c2 c2-5b2), and a row kind's bucket keeps one order whatever the premises' (the class header's Invariants:
        // a row's Source): a fact whose filing atom's subject has the bucket's key before one under its class key or its reading's
        // subject, then Binding.TypeIdentity's Order of the atoms' subjects; equal subjects keep their arrival. Another kind's bucket
        // keeps its environment's order, which its readers' pairs read (ValidateAssociatedOverlapAgreement: each identity with those
        // before it).
        internal void Add(BindingScope scope, BoundConstraint raw, BoundConstraint fact, BoundType at)
        {
            var key = (scope, fact.Kind, SubjectKey(at).Symbol, SubjectKey(at).Type);
            if (!this.Buckets.TryGetValue(key, out var list))
            {
                if (this.used == this.lists.Count)
                {
                    this.lists.Add(new());
                }

                list = this.lists[this.used++];
                list.Clear();
                this.Buckets[key] = list;
            }

            var position = RowAtom(fact) is not null ? 0 : list.Count;
            for (var end = list.Count; position < end;)
            {
                var middle = (position + end) >> 1;
                if (Before(FilingAtom(raw).Subject!, FilingAtom(list[middle].Raw).Subject!, at))
                {
                    end = middle;
                }
                else
                {
                    position = middle + 1;
                }
            }

            list.Insert(position, (raw, fact));
            this.Readings |= !ReferenceEquals(raw, fact);

            static bool Before(BoundType subject, BoundType other, BoundType at)
                => Own(subject, at) != Own(other, at) ? Own(subject, at) : Order(subject, other) < 0;

            static bool Own(BoundType subject, BoundType at)
                => ReferenceEquals(SubjectKey(subject).Symbol, SubjectKey(at).Symbol) && ReferenceEquals(SubjectKey(subject).Type, SubjectKey(at).Type);
        }

        // The atom whose subject a fact is filed under, Add's order's and DropClassKeys' own subject: its row atom (RowAtom), else the
        // fact itself (a TypeIdentity or Semantics fact, filed under its written subject: IndexWritten). The bucket's kind is the
        // fact's own (Add): a negation's is Not, whatever its atom's (R7-C2c2 c2-5b2).
        private static BoundConstraint FilingAtom(BoundConstraint fact) => RowAtom(fact) ?? fact;
    }
}
