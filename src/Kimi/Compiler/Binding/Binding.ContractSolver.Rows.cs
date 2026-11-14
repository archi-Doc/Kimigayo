// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Binding.ContractSolver's request boundary (the entry file states the contract; split from it without a change, R7-C2c2 c2-3b):
// the requests for Enumerate's rows (PremiseRefines, SPEC 8.3.2's refinement row; FirstRows, the first walk's rows, which the
// Callable and effect-bound readers read; PremiseRows, the candidate readers' rows over them; PremiseCallable, SPEC 8.10's row),
// their shared frame (RowsRequest, PrepareRows, LeaveRows), the view's index of facts by subject key (IndexWritten,
// IndexNormalized, IndexFact) and the tables a request owns or keeps (the request tables; the kept rows: KeepRows,
// ClearKeptRows). The walk the requests start (Enumerate and what it calls) is the entry file's.
public sealed partial class Binding
{
    // Enumerate's request tables, owned by one outermost request (emptied, capacity kept, when it returns) and keyed by the view value:
    // the rows of a link of the subject's chain in a view (null while the link is open), the view's row-giving facts by subject key
    // (IndexWritten, IndexNormalized), the views indexed, and the pooled lists.
    private readonly Dictionary<(BoundType, EvidenceView, int), List<PremiseRow>?> enumerateMemo = new();

    private readonly Dictionary<(EvidenceView, int, BindingScope, BindingSymbol?, BoundType), List<(BoundConstraint Raw, BoundConstraint Fact)>>
        enumerateIndex = new();

    private readonly HashSet<(EvidenceView, int)> enumeratePrepared = new();
    private readonly List<(BindingScope Scope, BoundConstraint Raw, BoundConstraint Fact)> enumerateNormalized = new();

    // The kept tables (the class header's Kept rows), emptied by ClearKeptRows as their epoch ends, capacity and pooled lists kept:
    // a Contract's head depth, and the rows an outermost request walked in the kept region per link, view value and requirement
    // Contract, with whether that walk recorded an identity row (an extension may then supply what the rows lack).
    private readonly Dictionary<BindingSymbol, int> enumerateHeads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(BoundType, EvidenceView, int, BindingSymbol?), (List<PremiseRow> Rows, bool Extends)> enumerateKept = new();
    private readonly List<List<PremiseRow>> enumerateKeptPool = new();
    private readonly List<List<PremiseRow>> enumeratePool = new();
    private readonly List<PremiseRow> premiseRows = new(); // FirstRows' and PremiseRows' readers' rows, a stack of ranges
    private readonly List<List<(BoundConstraint Raw, BoundConstraint Fact)>> enumerateFactPool = new();
    private readonly List<(BoundType A, BoundType B)> enumerateIdentities = new(); // c2-2: the outermost walk's identity rows
    private int enumeratePoolNext;
    private int enumerateFactPoolNext;
    private int enumerateDepth;
    private int enumerateWalking;
    private int enumerateKeptPoolNext;
    private IdentityClasses? enumerateClasses; // R7-C2c2: the current request's view's identity classes, or null

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
        var (view, serial, classes, key, keep) = this.RowsRequest(subject, scope);

        // Kept rows answer only where they decide (the class header's Extension): a refinement among them, or no identity row recorded.
        var keyed = default((List<PremiseRow> Rows, bool Extends));
        if (keep && this.enumerateKept.TryGetValue((subject, view, serial, this.activeRequirementContract), out var kept) &&
            (ReferenceEquals(key, subject) || this.enumerateKept.TryGetValue((key, view, serial, this.activeRequirementContract), out keyed)))
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
            keep &= this.PrepareRows(subject, view, serial);
            this.enumerateWalking++;
            try
            {
                this.enumerateIdentities.Clear();
                var refines = RowsRefine(this.Enumerate(subject, view, serial), contract);
                refines |= !ReferenceEquals(key, subject) && RowsRefine(this.Enumerate(key, view, serial), contract);

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
                    this.IndexWritten(view, serial); // The facts under the extended keys; a repeated entry adds no row.
                    refines = RowsRefine(this.Enumerate(subject, view, serial), contract);
                    refines |= !ReferenceEquals(key, subject) && RowsRefine(this.Enumerate(key, view, serial), contract);
                    this.IdentityRounds++;
                }

                if (keep)
                {
                    this.KeepRows(view, serial);
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
        var (view, serial, classes, key, keep) = this.RowsRequest(subject, scope);
        var start = into.Count;
        var keyed = default((List<PremiseRow> Rows, bool Extends));
        if (keep && this.enumerateKept.TryGetValue((subject, view, serial, this.activeRequirementContract), out var kept) &&
            (ReferenceEquals(key, subject) || this.enumerateKept.TryGetValue((key, view, serial, this.activeRequirementContract), out keyed)))
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
            keep &= this.PrepareRows(subject, view, serial);
            this.enumerateWalking++;
            try
            {
                this.enumerateIdentities.Clear();
                var rows = this.Enumerate(subject, view, serial);
                var keyRows = ReferenceEquals(key, subject) ? null : this.Enumerate(key, view, serial);
                if (keep)
                {
                    this.KeepRows(view, serial);
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

    // A request's terms (PremiseRefines' and FirstRows'): its view, the view's identity classes, the subject's key in them and
    // whether its rows may be kept. R7-C2c2: a request keys its subject and its view's facts by the view's identity classes (SPEC
    // 8.2.3), whatever the table's finality; finality decides only whether the rows may be kept for a later request.
    private (EvidenceView View, int Serial, IdentityClasses? Classes, BoundType Key, bool Keep) RowsRequest(BoundType subject, BindingScope scope)
    {
        if (this.enumerateWalking > 0)
        {
            throw new InvalidOperationException($"A request for the rows of `{subject}` entered from Enumerate's walk (InternalInvariant; R7-C2a).");
        }

        var classes = HasTypeIdentity(scope) ? this.ClassesOf(scope, out _) : null;
        var key = classes is null ? subject : this.ClassKey(subject, classes);
        var view = this.ViewOf(scope);
        var serial = SerialOf(scope);
        return (view, serial, classes, key, this.enumerateDepth == 0 && this.KeptRegion && (classes is null ? !HasTypeIdentity(scope) : classes.Final));
    }

    // An entered request's preparation: the view's index. Returns whether every reading it formed was a kept one (the rows may then
    // be kept).
    private bool PrepareRows(BoundType subject, EvidenceView view, int serial)
    {
        if (this.enumeratePrepared.Add((view, serial)))
        {
            this.IndexWritten(view, serial);
        }

        // The walk demands nothing, so a nested demand comes from the outermost request's preparation: it reads the written facts
        // alone, and the rows it left are dropped before the outermost walk reads the whole index. The request's premises are the
        // view it took: a demand of its preparation that moved them is a builder's broken contract.
        if (this.enumerateDepth != 1)
        {
            return true;
        }

        var formations = this.readingFormations;
        if (this.IndexNormalized(view, serial) != serial)
        {
            throw new InvalidOperationException($"The preparation of the request for `{subject}` moved its own view (InternalInvariant; R7-C2a).");
        }

        this.enumerateMemo.Clear();
        return formations == this.readingFormations;
    }

    // A request returns: its enclosing request's classes are back, and the outermost one empties the request tables.
    private void LeaveRows(IdentityClasses? enclosing)
    {
        this.enumerateClasses = enclosing;
        if (--this.enumerateDepth == 0)
        {
            this.enumerateMemo.Clear();
            this.enumerateIndex.Clear();
            this.enumeratePrepared.Clear();
            this.enumeratePoolNext = 0;
            this.enumerateFactPoolNext = 0;
            if (!this.KeepsAcrossRequests)
            {
                this.ClearKeptRows(); // Before the capability phase the epoch is this request.
            }
        }
    }

    // The outermost walk's rows of the view, kept for the epoch under the request's requirement Contract (pooled copies), each marked
    // with whether the walk recorded an identity row: a link's own walk records a subset of its request's.
    private void KeepRows(EvidenceView view, int serial)
    {
        foreach (var ((link, linkView, linkSerial), rows) in this.enumerateMemo)
        {
            if (rows is null || linkView != view || linkSerial != serial ||
                this.enumerateKept.ContainsKey((link, view, serial, this.activeRequirementContract)))
            {
                continue;
            }

            List<PremiseRow> copy;
            if (this.enumerateKeptPoolNext < this.enumerateKeptPool.Count)
            {
                copy = this.enumerateKeptPool[this.enumerateKeptPoolNext++];
                copy.Clear();
            }
            else
            {
                copy = new(rows.Count);
                this.enumerateKeptPool.Add(copy);
                this.enumerateKeptPoolNext++;
            }

            copy.AddRange(rows);
            this.enumerateKept[(link, view, serial, this.activeRequirementContract)] = (copy, this.enumerateIdentities.Count != 0);
        }
    }

    // The kept tables' epoch ended: the capability epoch (ClearCapabilityResults) or, before the phase, the outermost request.
    private void ClearKeptRows()
    {
        this.enumerateKept.Clear();
        this.enumerateKeptPoolNext = 0;
        this.enumerateHeads.Clear();
    }

    // The view's row-giving facts (RowKind) by subject key, as written.
    private void IndexWritten(EvidenceView view, int serial)
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
                    this.IndexFact(view, serial, scope, fact, fact, fact.Subject);
                    if (this.enumerateClasses is { } classes && this.ClassKey(fact.Subject, classes) is var key && !ReferenceEquals(key, fact.Subject))
                    {
                        this.IndexFact(view, serial, scope, fact, fact, key); // R7-C2c2: also under its class key in the request's view
                    }
                }
            }
        }
    }

    // The outermost request's view, under a parameter identity: each Contract fact as the view reads it (Reading, every reader's; a
    // leaf kind's fact is indexed as written and by its class key alone, the class header's Identity), added after
    // all are read. A reading's own demands are nested, so they read written facts alone: the index does not depend on the order of the
    // facts.
    private int IndexNormalized(EvidenceView view, int serial)
    {
        int? after = serial; // The loop writes nothing but through Reading.
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
                this.IndexFact(view, serial, scope, raw, fact, fact.Subject!);
            }

            this.enumerateNormalized.RemoveRange(start, this.enumerateNormalized.Count - start);
        }

        return after ?? SerialOf(view.Premises);
    }

    // One fact of an environment of the view under its subject key (a projection's declaration and receiver, else the Type): `raw` as
    // the environment holds it (its availability), `fact` as the index reads it. A list keeps one order whatever the premises'
    // (the class header's Invariants: a row's Source): a fact whose own subject has the list's key before one under its class key
    // or its reading's subject, then Binding.TypeIdentity's Order of the facts' subjects; equal subjects keep their arrival.
    private void IndexFact(EvidenceView view, int serial, BindingScope scope, BoundConstraint raw, BoundConstraint fact, BoundType at)
    {
        var key = (view, serial, scope, SubjectKey(at).Symbol, SubjectKey(at).Type);
        if (!this.enumerateIndex.TryGetValue(key, out var list))
        {
            if (this.enumerateFactPoolNext < this.enumerateFactPool.Count)
            {
                list = this.enumerateFactPool[this.enumerateFactPoolNext++];
                list.Clear();
            }
            else
            {
                list = new();
                this.enumerateFactPool.Add(list);
                this.enumerateFactPoolNext++;
            }

            this.enumerateIndex[key] = list;
        }

        var index = 0;
        for (var end = list.Count; index < end;)
        {
            var middle = (index + end) >> 1;
            if (Before(raw.Subject!, list[middle].Raw.Subject!, at))
            {
                end = middle;
            }
            else
            {
                index = middle + 1;
            }
        }

        list.Insert(index, (raw, fact));

        static bool Before(BoundType subject, BoundType other, BoundType at)
            => Own(subject, at) != Own(other, at) ? Own(subject, at) : Order(subject, other) < 0;

        static bool Own(BoundType subject, BoundType at)
            => ReferenceEquals(SubjectKey(subject).Symbol, SubjectKey(at).Symbol) && ReferenceEquals(SubjectKey(subject).Type, SubjectKey(at).Type);
    }
}
