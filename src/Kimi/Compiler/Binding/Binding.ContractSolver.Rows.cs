// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Binding.ContractSolver's request boundary (the entry file states the contract; split from it without a change, R7-C2c2 c2-3b):
// the requests for Enumerate's rows (PremiseRefines, SPEC 8.3.2's refinement row; FirstRows, the first walk's rows, which the
// Callable and effect-bound readers read; PremiseRows, the candidate readers' rows over them; PremiseCallable, SPEC 8.10's row),
// their shared frame (RowsRequest, PrepareRows, LeaveRows), the view's index, Closure(view) (IndexOf, IndexWritten,
// IndexNormalized, ViewIndex; a closure validator's read, BeginClosureRead to EndClosureRead; DropOwnedEntries, ClearClosure;
// R7-C2e2-1, R7-C2e2-2) and the tables a request owns or keeps (the request tables; the kept rows, ViewIndex.Rows: KeepRows,
// ForgetKeptTables, R7-C2e2 e2-3a). The walk the requests start (Enumerate and what it calls) is the entry file's.
public sealed partial class Binding
{
    // Enumerate's request tables, owned by one outermost request (emptied, capacity kept, when it returns) and keyed by the view value:
    // the rows of a link of the subject's chain in a view (null while the link is open) and the pooled lists.
    private readonly Dictionary<(BoundType, EvidenceView, int), List<PremiseRow>?> enumerateMemo = new();

    // Closure(view) (the class header's): the entry of each (view scope, requirement Contract), the entries the outermost request formed
    // (dropped when it returns unless Kept), and the free entries, each with its lists and their capacity.
    private readonly Dictionary<(EvidenceView, BindingSymbol?), ViewIndex> enumerateIndex = new();
    private readonly List<(EvidenceView, BindingSymbol?)> enumerateOwned = new();
    private readonly Stack<ViewIndex> enumerateIndexPool = new();
    private readonly List<(BindingScope Scope, BoundConstraint Raw, BoundConstraint Fact)> enumerateNormalized = new();

    // A Contract's head depth (the class header's Kept rows), emptied at each publication's start in the capability phase
    // (ForgetKeptTables), by each outermost request's return before it and each pass (ClearClosure), capacity kept; the rows
    // themselves are their entry's (ViewIndex.Rows).
    private readonly Dictionary<BindingSymbol, int> enumerateHeads = new(ReferenceEqualityComparer.Instance);
    private readonly List<List<PremiseRow>> enumeratePool = new();
    private readonly List<PremiseRow> premiseRows = new(); // FirstRows' and PremiseRows' readers' rows, a stack of ranges
    private readonly List<(BoundType A, BoundType B)> enumerateIdentities = new(); // c2-2: the outermost walk's identity rows
    private int enumeratePoolNext;
    private int enumerateDepth;
    private int enumerateWalking;

    // The outermost request's own scratch value (the class header's Kept rows): the meet of its walk's availability reads (WalkAvailable),
    // set at each outermost walk's start and read at its end (KeepRows' condition); no collection, so no store the library-layer dump lists.
    private Completion enumerateCompletion;
    private IdentityClasses? enumerateClasses; // R7-C2c2: the current request's view's identity classes, or null

    // The scaling rows' measure: the pairs Closure(view)'s entries indexed in this compilation, in every row (a fact of a row kind,
    // TypeIdentity or Semantics as written; a row kind's fact under its class key and a Contract fact as read), the validators' entries
    // included.
    internal int ClosureIndexWork { get; private set; }

    // R7-C2c2 c2-2: the extension rounds of this compilation (the scaling rows' measure of the loop's bound).
    internal int IdentityRounds { get; private set; }

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
    // row. The subject is as its reader has it, walked beside its class key in the view (the contract's Identity). The extension is
    // this predicate's alone (the class header's Extension): only a request whose demanded Contract is not proven extends.
    private bool PremiseRefines(BoundType subject, BindingSymbol contract, BindingScope scope)
    {
        var (view, serial, classes, key, final, keep) = this.RowsRequest(subject, scope);

        // Kept rows answer only where they decide (the class header's Extension): a refinement among them, or no identity row recorded.
        var keyed = default((List<PremiseRow> Rows, bool Extends));
        if (keep && this.enumerateIndex.TryGetValue((view, this.activeRequirementContract), out var entry) && entry.Serial == serial &&
            entry.Rows.TryGetValue(subject, out var kept) && (ReferenceEquals(key, subject) || entry.Rows.TryGetValue(key, out keyed)))
        {
            var answer = RowsRefine(kept.Rows, contract) || RowsRefine(keyed.Rows, contract);
            if (answer || !(kept.Extends || keyed.Extends))
            {
                return answer;
            }
        }

        var enclosing = this.enumerateClasses;
        this.enumerateClasses = classes;
        this.enumerateDepth++;
        try
        {
            var (index, prepared) = this.PrepareRows(subject, view, serial, final);
            keep &= prepared;
            this.enumerateWalking++;
            try
            {
                this.enumerateIdentities.Clear();
                this.enumerateCompletion = Completion.Complete;
                var refines = RowsRefine(this.Enumerate(subject, view, index), contract);
                refines |= !ReferenceEquals(key, subject) && RowsRefine(this.Enumerate(key, view, index), contract);

                // R7-C2c2 c2-2: the identity rows the walk recorded join the request's own classes, and the walk runs again under
                // them while a round records a row the last extension did not hold. Bound: rows only grow, and each is a clause
                // instance at a distinct link Type some round walked (the prefixes of the subject and of its successive keys, each key
                // no deeper than the Type it keys), so the rounds are at most those (link Type, clause) pairs plus one.
                for (var applied = 0; !refines && this.enumerateDepth == 1 && this.enumerateIdentities.Count > applied;)
                {
                    applied = this.enumerateIdentities.Count;
                    if (this.ExtendedClassesOf(scope, this.enumerateIdentities) is not { } extended)
                    {
                        break;
                    }

                    keep = false; // A request-local extension is not the view's (SPEC 8.3.3: no answer depends on who asked first).
                    this.enumerateClasses = classes = extended;
                    key = this.ClassKey(subject, extended);
                    this.enumerateMemo.Clear();

                    // The facts under the extended keys (N3 of e2-1's merge review): an entry is Kept only with final keys, and an
                    // extended table is never Final (its rows are Provisional), so a Kept entry is reopened as the outermost request's
                    // before a pair goes in, never written into; a repeated pair adds no row.
                    index.Kept &= extended.Final;
                    this.enumerateOwned.Add((view, this.activeRequirementContract));
                    this.IndexWritten(index, view);
                    refines = RowsRefine(this.Enumerate(subject, view, index), contract);
                    refines |= !ReferenceEquals(key, subject) && RowsRefine(this.Enumerate(key, view, index), contract);
                    this.IdentityRounds++;
                }

                if (keep && this.enumerateCompletion == Completion.Complete)
                {
                    this.KeepRows(index, view, serial);
                }

                return refines;
            }
            finally
            {
                this.enumerateWalking--;
            }
        }
        finally
        {
            this.LeaveRows(enclosing);
        }
    }

    // R7-C2c2 c2-3b: SPEC 8.10's row for a Callable atom `S is Callable<r, Sig>`, normalized by its caller (Supply's): the premises
    // of `scope` supply an available Callable row of S with the atom's receiver and signature (SameProofPremise's tests of two
    // Callable atoms; the subject is the rows'). A row's signature is read as the Callable selection reads it.
    private bool PremiseCallable(BoundConstraint atom, BindingScope scope)
    {
        var start = this.premiseRows.Count;
        this.FirstRows(atom.Subject!, scope, this.premiseRows);
        var supplied = false;
        for (var i = start; !supplied && i < this.premiseRows.Count; i++)
        {
            supplied = this.premiseRows[i] is { Available: true, Clause: { Kind: ConstraintKind.Callable } clause } && clause.Mask == atom.Mask &&
                SameCallableSignature(this.CallableContractType(clause.RequiredType!, scope), atom.RequiredType!);
        }

        this.premiseRows.RemoveRange(start, this.premiseRows.Count - start);
        return supplied;
    }

    // R7-C2c2 c2-3b: the request boundary of a reader that demands no Contract: the rows of `subject` and of its class key under
    // the premises of `scope`, appended to `into`, each row once (AddRow), whatever their availability and kind. They are the first
    // walk's rows, a function of (subject, view): such a reader never extends (the class header's Extension), and a kept hit reads
    // the kept rows, which are a first walk's. No row is Alias and no identity is judged here (the walk's class keys alone).
    // Returns the request's identity classes, in which PremiseRows forms its column.
    private IdentityClasses? FirstRows(BoundType subject, BindingScope scope, List<PremiseRow> into)
    {
        var (view, serial, classes, key, final, keep) = this.RowsRequest(subject, scope);
        var start = into.Count;
        var keyed = default((List<PremiseRow> Rows, bool Extends));
        if (keep && this.enumerateIndex.TryGetValue((view, this.activeRequirementContract), out var entry) && entry.Serial == serial &&
            entry.Rows.TryGetValue(subject, out var kept) && (ReferenceEquals(key, subject) || entry.Rows.TryGetValue(key, out keyed)))
        {
            Append(into, start, kept.Rows);
            Append(into, start, keyed.Rows);
            return classes;
        }

        var enclosing = this.enumerateClasses;
        this.enumerateClasses = classes;
        this.enumerateDepth++;
        try
        {
            var (index, prepared) = this.PrepareRows(subject, view, serial, final);
            keep &= prepared;
            this.enumerateWalking++;
            try
            {
                this.enumerateIdentities.Clear();
                this.enumerateCompletion = Completion.Complete;
                var rows = this.Enumerate(subject, view, index);
                var keyRows = ReferenceEquals(key, subject) ? null : this.Enumerate(key, view, index);
                if (keep && this.enumerateCompletion == Completion.Complete)
                {
                    this.KeepRows(index, view, serial);
                }

                Append(into, start, rows);
                Append(into, start, keyRows);
            }
            finally
            {
                this.enumerateWalking--;
            }
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

        var classes = HasTypeIdentity(scope) ? this.ClassesOf(scope, out _) : null;
        var key = classes is null ? subject : this.ClassKey(subject, classes);
        var view = this.ViewOf(scope);
        var serial = SerialOf(scope);
        var final = classes is null ? !HasTypeIdentity(scope) : classes.Final;
        return (view, serial, classes, key, final, this.enumerateDepth == 0 && this.KeptRegion && final);
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

    // A request returns: its enclosing request's classes are back, and the outermost one empties the request tables and drops the
    // entries it formed unless Kept.
    private void LeaveRows(IdentityClasses? enclosing)
    {
        this.enumerateClasses = enclosing;
        if (--this.enumerateDepth == 0)
        {
            this.enumerateMemo.Clear();
            this.DropOwnedEntries();
            this.enumeratePoolNext = 0;
            if (!this.KeepsAcrossRequests)
            {
                this.enumerateHeads.Clear(); // Before the capability phase the head depths live for this request.
            }
        }
    }

    // The outermost walk's rows of the view, kept in its Kept entry (pooled copies), each marked with whether the walk recorded an
    // identity row: a link's own walk records a subset of its request's.
    private void KeepRows(ViewIndex index, EvidenceView view, int serial)
    {
        foreach (var ((link, linkView, linkSerial), rows) in this.enumerateMemo)
        {
            if (rows is not null && linkView == view && linkSerial == serial)
            {
                index.KeepRow(link, rows, this.enumerateIdentities.Count != 0);
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
        this.enumerateHeads.Clear();
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

    // The view's facts as written into its entry, each row's under its subject and a fact of a row kind (RowKind) also under its class
    // key in the request's view. An extension's re-filing repeats the other rows' pairs in an entry no validator reads (the request's,
    // dropped when it returns).
    private void IndexWritten(ViewIndex index, EvidenceView view)
    {
        for (var scope = view.Premises; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact.Subject is not null && RowKind(fact))
                {
                    index.Add(scope, fact, fact, fact.Subject);
                    this.ClosureIndexWork++;

                    // R7-C2c2 c2-1 meets R7-C2e2-2 here: the class-key copy is the row kinds' alone (c2-3b: a leaf kind's fact too). The
                    // other rows keep written subjects, so the validators, which pair facts by bucket, never meet a fact of another
                    // written subject.
                    if (this.enumerateClasses is { } classes && this.ClassKey(fact.Subject, classes) is var at && !ReferenceEquals(at, fact.Subject))
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

    // The pass ended (ResetCapabilities): every entry goes back to the pool, and the head depths go.
    private void ClearClosure()
    {
        foreach (var index in this.enumerateIndex.Values)
        {
            this.enumerateIndexPool.Push(index);
        }

        this.enumerateIndex.Clear();
        this.enumerateHeads.Clear();
    }

    // The outermost request's view, under a parameter identity: each Contract fact as the view reads it (Reading, every reader's; a
    // leaf kind's fact is indexed as written and by its class key alone, the class header's Identity), added after all are read. A
    // reading's own demands are nested, so they read written facts alone: the index does not depend on the order of the facts.
    private int IndexNormalized(ViewIndex index, EvidenceView view)
    {
        int? after = index.Serial; // The loop writes nothing but through Reading.
        if (HasParameterIdentity(view.Premises))
        {
            var start = this.enumerateNormalized.Count;
            for (var scope = view.Premises; scope is not null; scope = scope.Parent)
            {
                if (scope.Constraints is not { Invalid: false } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact is { Kind: ConstraintKind.Contract, Subject: not null, Contract: not null } &&
                        this.Reading(fact, view.Premises, ref after) is { Subject: not null } normalized && !ReferenceEquals(normalized, fact))
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
    private sealed class ViewIndex
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

        // The kept rows (the class header's Kept rows): a link's rows and whether its walk recorded an identity row.
        internal Dictionary<BoundType, (List<PremiseRow> Rows, bool Extends)> Rows { get; } = new(ReferenceEqualityComparer.Instance);

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
        internal void KeepRow(BoundType link, List<PremiseRow> rows, bool extends)
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
            this.Rows.Add(link, (copy, extends));
        }

        // One fact of an environment of the view in its kind's row under the subject key of `at` (a projection's declaration and
        // receiver, else the Type; the fact's subject or its class key): `raw` as the environment holds it (its availability), `fact`
        // as the index reads it. A row kind's bucket (RowKind) keeps one order whatever the premises' (the class header's Invariants:
        // a row's Source): a fact whose own subject has the bucket's key before one under its class key or its reading's subject, then
        // Binding.TypeIdentity's Order of the facts' subjects; equal subjects keep their arrival. Another kind's bucket keeps its
        // environment's order, which its readers' pairs read (ValidateAssociatedOverlapAgreement: each identity with those before it).
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

            var position = RowKind(fact) ? 0 : list.Count;
            for (var end = list.Count; position < end;)
            {
                var middle = (position + end) >> 1;
                if (Before(raw.Subject!, list[middle].Raw.Subject!, at))
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
    }
}
