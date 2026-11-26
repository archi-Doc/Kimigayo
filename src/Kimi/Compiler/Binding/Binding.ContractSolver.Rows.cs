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
    // pass's entry of each (view scope, requirement Contract) and its free entries, each with its lists and their capacity, are the
    // pass's part's, handed over by EnterPass (enumerateIndex, enumerateIndexPool below; PassPart.EnumerateIndex).
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
    // (IdentityClasses.Same: decomposition and constructed members included; ClassKey is the supply's index of subjects and keys a
    // constructed member to itself, so it cannot answer this). One of them stays non-Alias, the more determined spelling: a closed
    // one first (its members' signatures need no identity reading), then Binding.TypeIdentity's total Order; never premise order
    // (SPEC 8.3.3). Without classes, each reference is its own Contract. A leaf row has no reference and is never Alias.
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
        var keyed = default(List<PremiseRow>);
        if (keep && this.enumerateIndex.TryGetValue((view, this.activeRequirementContract), out var entry) && entry.Serial == serial &&
            entry.Rows.TryGetValue(subject, out var kept) && (ReferenceEquals(key, subject) || entry.Rows.TryGetValue(key, out keyed)))
        {
            return RowsRefine(kept, contract) || RowsRefine(keyed, contract);
        }

        var (index, prepared, enclosing) = this.EnterRows(subject, view, serial, classes, final);
        try
        {
            var (rows, keyRows) = this.EnumerateClosed(subject, key, view, index);
            var refines = RowsRefine(rows, contract) || RowsRefine(keyRows, contract);
            if (!refines && this.enumerateDepth == 1)
            {
                this.ReadIdentityLimit(null, scope, classes);
            }

            if (keep && prepared && this.WalkComplete)
            {
                this.KeepRows(index, view, serial);
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
    // request left. The first round reads T(G), kept per serial; each later round builds the table from the rows so far, and a round
    // that adds no row builds nothing (R3.4). When a round keys a root lower by Order, the root is that key and the closure starts
    // again with no rows, and a restart onto a root filed in the publication ends it (CloseRoot files the entry with what the filed
    // key spent, G1406-16 C2). Otherwise
    // the round reopens the view's entry as the closure's (OwnEntry) and returns its table, and CloseRoot walks each root and its key.
    // Rows only grow from the same roots, so a round that adds none ends the closure (null). G1406-19: the closure has one budget,
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
    // Returns the round's table, or null when the closure ended or the table's build answered none.
    private IdentityClasses? ExtendRound(ref (int Applied, int Spent, int Rooted, int Rows) closure, BindingScope scope, int serial, EvidenceView view, ViewIndex index)
    {
        while (true)
        {
            var first = closure.Applied < 0; // a root's first walk, which spends nothing
            if (first)
            {
                this.enumerateIdentities.Clear(); // the closure starts (again) from its roots with no rows
                this.enumerateIdentityKeys.Clear();
                this.enumerateJoined = 0;
            }
            else if (this.enumerateIdentities.Count == closure.Applied)
            {
                return null;
            }
            else if (++closure.Spent > PremiseClosureLimit || this.enumerateJoined > closure.Rows)
            {
                closure.Spent = PremiseClosureLimit + 1; // the budget is spent: the closure is Limited (CloseRoot reads it); its reader refuses
                return null;
            }

            closure.Applied = this.enumerateIdentities.Count;
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
            this.OwnEntry(index, view);
            this.IdentityRounds += first ? 0 : 1; // a root's first walk is no extension round
            return extended;
        }
    }

    // e2-7 (G1406-11 C2, C-e, C-f; SPEC 8.2.3 grounds, 8.3.3 Limits): the closure of the dependent abstract part `root` in `scope`'s
    // view under the reading's `serial`, CloseAsked's one closer (at the request boundary: no request is open). Its rounds
    // (ExtendRound) walk the root and its key over the table of the declaration's grounds and the rows so far, first T(G), whose key
    // enters it (a re-rooting); each walk records its supplies as rows (C-f). A lower key re-roots and restarts with no rows; a restart
    // onto a root filed in the publication ends it. A round that adds no row ends it with its rows; past its one budget
    // (PremiseClosureLimit: rounds and re-rootings together, G1406-19, and rows per Type of the root and G, O-532 (b)) it is Limited
    // with the last round's rows, and its reader refuses (ReadIdentityLimit). Filed by FileClosure, which keeps E (the rows that join
    // the key's class), the entry's pair row and what the entry and its key spent (G1406-16 B1, C2). It records nothing: the scope's
    // premise error is its validation step's (RecordIdentityPremiseError, R3.2). A closure is a function of (root, G): no use's ask or
    // earlier request's rows enter it.
    // Returns the closure.
    private int CloseRoot(BindingScope scope, int serial, BoundType root)
    {
        var view = this.ViewOf(scope);
        var (index, _, enclosing) = this.EnterRows(root, view, serial, null, false);
        (int Applied, int Spent, int Rooted, int Rows) closure;
        BoundType key;
        Completion completion;
        int kept; // the serial the closure is kept for, -1: not kept (FileClosure)
        try
        {
            this.extendRoots.Add(root);
            closure = (Applied: -1, Spent: 0, Rooted: 0, Rows: 0);
            while (this.ExtendRound(ref closure, scope, serial, view, index) is { } table)
            {
                var at = this.extendRoots[0];
                this.EnumerateClosed(at, this.ClassKey(at, table), view, index);
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
        }

        this.CloseRoot(scope, serial, key); // 5e': an unfiled key's own closure, with its own budget: a function of (key, G)
        return this.FileClosure(scope, root, key, closure.Spent, kept, completion);
    }

    // A request's own entry (a closure's round, ExtendRound; a request over a Limited reading, EnterRows): the view's entry reopened as the request's
    // (not Kept, dropped when it returns), the class-key pairs an earlier table filed there dropped and the view's facts filed under the
    // request's table's keys (enumerateClasses), so its walk reads its own table alone.
    private void OwnEntry(ViewIndex index, EvidenceView view)
    {
        index.Kept = false;
        this.enumerateOwned.Add((view, this.activeRequirementContract));
        index.DropClassKeys();
        this.IndexWritten(index, view);
    }

    // PLAN G881b (SPEC 8.2.3 Cycles, 8.3.3): the rows of a request's subject and of its class key as the walk's least fixpoint, the
    // requests' one entry to the walk. A round walks the subject and its key, then each mate row (5) deferred (its chain held an
    // open link: a cycle), from the top, where no link is open; a deferred mate's walk may defer others and records no identity row.
    // A round that deferred a mate is walked again, its links' rows the seed a deferred mate supplies, until no link closes with
    // rows that are not its own last round's (the per-link stop test: Enumerate compares each link with its own seed). A round
    // that deferred none is exact, so a request through no cycle walks once. Each round visits the same links in the same order and
    // each link's rows are monotone in what it reads, so a link's rows only grow from round to round and the last round is the least
    // fixpoint of the links' equations: its rows are a function of (link, view) and are kept as a single walk's (KeepRows). Bound:
    // the (link, row) pairs of the request plus one.
    private (List<PremiseRow>? Rows, List<PremiseRow>? KeyRows) EnumerateClosed(BoundType subject, BoundType key, EvidenceView view, ViewIndex index)
    {
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
                return (rows, keyRows);
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
    // the kept rows, which are a first walk's. No row is Alias and no identity is judged here (the walk's class keys alone).
    // Returns the request's identity classes, in which PremiseRows forms its column. G1406-12, e2-7 R3.3 (SPEC 8.3.3 Limits): it answers
    // no judgment. Its rows at a Limited reading are cut at the round bound, so a reader whose answer the cut rows could make negative
    // (CallableSelection, ValueCalls, EffectDeclarations, CallableEffects; Retention at `true`, G1406-16 C3) refuses through
    // ReadIdentityLimit with these classes and its asking node, once per reading; ExactAssumption, discovery (PremiseRows) and the
    // adapter answer no rows judgment.
    private IdentityClasses? FirstRows(BoundType subject, BindingScope scope, List<PremiseRow> into)
    {
        var (view, serial, classes, key, final, keep) = this.RowsRequest(subject, scope);
        var start = into.Count;
        var keyed = default(List<PremiseRow>);
        if (keep && this.enumerateIndex.TryGetValue((view, this.activeRequirementContract), out var entry) && entry.Serial == serial &&
            entry.Rows.TryGetValue(subject, out var kept) && (ReferenceEquals(key, subject) || entry.Rows.TryGetValue(key, out keyed)))
        {
            Append(into, start, kept);
            Append(into, start, keyed);
            return classes;
        }

        var (index, prepared, enclosing) = this.EnterRows(subject, view, serial, classes, final);
        try
        {
            var (rows, keyRows) = this.EnumerateClosed(subject, key, view, index);
            if (keep && prepared && this.WalkComplete)
            {
                this.KeepRows(index, view, serial);
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
    // (FormAliases). A candidate reader demands no Contract, so it never extends.
    private void PremiseRows(BoundType subject, BindingScope scope, List<PremiseRow> into)
    {
        var start = into.Count;
        FormAliases(into, start, this.FirstRows(subject, scope, into));
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
        var enclosing = this.enumerateClasses;
        this.enumerateClasses = classes;
        this.enumerateDepth++;
        var (index, prepared) = this.PrepareRows(subject, view, serial, final);
        if (classes is { Limited: true })
        {
            this.OwnEntry(index, view); // a Limited reading is the request's table alone (G1406-11 C-a): its entry keys by it
        }

        this.enumerateWalking++;
        this.enumerateIdentities.Clear();
        this.enumerateIdentityKeys.Clear();
        this.enumerateJoined = 0;
        this.enumerateCompletion = Completion.Complete;
        return (index, prepared, enclosing);
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
    // deferred walk records none, so a link only it walked may record one in its own request.
    private void KeepRows(ViewIndex index, EvidenceView view, int serial)
    {
        foreach (var ((link, linkView, linkSerial), rows) in this.enumerateMemo)
        {
            if (rows is not null && linkView == view && linkSerial == serial)
            {
                index.KeepRow(link, rows);
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
    // subject and also its class key in the request's view. An extension's re-filing repeats the other rows' pairs in an entry no
    // validator reads (the request's, dropped when it returns).
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

                    // R7-C2c2 c2-1 meets R7-C2e2-2 here: the class-key copy is the row kinds' alone (c2-3b: a leaf kind's fact too). The
                    // other rows keep written subjects, so the validators, which pair facts by bucket, never meet a fact of another
                    // written subject.
                    if (this.enumerateClasses is { } classes && this.ClassKey(subject, classes) is var at && !ReferenceEquals(at, subject))
                    {
                        index.Add(scope, fact, fact, at); // also under its class key in the request's view
                        this.ClosureIndexWork++;
                    }
                }
                else if (fact is { Kind: ConstraintKind.TypeIdentity or ConstraintKind.Semantics, Subject: not null })
                {
                    index.Add(scope, fact, fact, fact.Subject);
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

    // The pass ended (ResetCapabilities, as the pass runs again): every entry of the pass goes back to the pass's pool, and the head
    // depths and supplies go.
    private void ClearClosure()
    {
        foreach (var index in this.enumerateIndex.Values)
        {
            this.enumerateIndexPool.Push(index);
        }

        this.enumerateIndex.Clear();
        this.ForgetHeads();
    }

    // HeadDepth's and SupplyOf's tables go (their one lifetime, enumerateHeads), capacity kept.
    private void ForgetHeads()
    {
        this.enumerateHeads.Clear();
        this.enumerateSupplies.Clear();
        this.enumerateSupplied.Clear();
    }

    // The outermost request's view, under a parameter identity: each Contract fact as the view reads it (Reading, every reader's; a
    // leaf row's fact is indexed as written and by its class key alone, the class header's Identity), added after all are read. A
    // reading's own demands are nested, so they read written facts alone: the index does not depend on the order of the facts.
    private int IndexNormalized(ViewIndex index, EvidenceView view)
    {
        int? after = index.Serial; // The loop writes nothing but through Reading.
        if (HasTypeIdentity(view.Premises))
        {
            var start = this.enumerateNormalized.Count;
            for (var scope = view.Premises; scope is not null; scope = scope.Parent)
            {
                if (scope.Constraints is not { } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    // R7-M25 (D), R7-C3 C6: a reading whose subject has no abstract part is not filed; a concrete constructed member's
                    // conformance is ResolveConformance's (never Unknown), so its row would repeat it or be a premise error.
                    if (fact is { Kind: ConstraintKind.Contract, Subject: not null, Contract: not null } &&
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
        }

        return after ?? SerialOf(view.Premises);
    }

    // An entry of Closure(view) (the class header's): the serial it was formed at, whether it is Kept, its buckets by (chain scope,
    // kind, SubjectKey) and its kept rows by link. Its lists stay with it, capacity kept.
    internal sealed class ViewIndex
    {
        private readonly List<List<(BoundConstraint Raw, BoundConstraint Fact)>> lists = new();
        private readonly List<List<PremiseRow>> rowLists = new();
        private int used;
        private int rowsUsed;

        internal int Serial { get; private set; }

        internal bool Kept { get; set; }

        internal bool Readings { get; private set; }

        internal Dictionary<(BindingScope Scope, ConstraintKind Kind, BindingSymbol? Symbol, BoundType Type), List<(BoundConstraint Raw, BoundConstraint Fact)>> Buckets { get; }
            = new();

        // The kept rows (the class header's Kept rows): a link's rows, walked over closed roots (e2-7), so a kept hit answers.
        internal Dictionary<BoundType, List<PremiseRow>> Rows { get; } = new(ReferenceEqualityComparer.Instance);

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
        }

        // A link's rows, copied into a pooled list; a link already kept keeps its first copy.
        internal void KeepRow(BoundType link, List<PremiseRow> rows)
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
            this.Rows.Add(link, copy);
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
