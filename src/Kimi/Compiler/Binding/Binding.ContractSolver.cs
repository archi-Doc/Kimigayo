// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ContractSolver (PlanB §4.1; DECISIONS BU-29, R7-C2a): premise derivation and evidence availability (SPEC 8.3): what the premises supply
// for a proposition and its negation (Supply) under the request's view (ViewOf, the one derivation point); the Contract rows they
// supply for a subject (Enumerate) and SPEC 8.3.2's refinement row over them (PremiseRefines, the row's one predicate: ProveConformance,
// SymbolicCapability); until C3 the eager derivation too (ExpandContractPremises, ExpandScopeContractPremises, AddContractPremises over
// a Contract premise's supply, ContractSupply, which retention's handed signatures read too) and the availability of a fact
// (AvailableConstraintFact).
// - Inputs: the view's scope chain (ConstraintEnvironments), derived facts' source Contracts, published source states (BU-59).
// - Outputs: Evidence (Positive, Negative; Invalid, temporary until C3: the chain's Invalid environment, the cause it names);
//   Enumerate's rows, the least set of Contracts `subject is R` closed under: (1) an available direct Contract fact of the view stating
//   the subject, as written and, under a parameter identity, as the view reads it (Reading); (2) a Self-dependent clause of a row of a
//   proper prefix of the subject's projection chain, Self being that prefix, whose stated subject is the subject as written; (3) a
//   Contract's own Self; (4) a row's ancestors.
// - Invariants: innermost scope first, each environment in its order; an Invalid environment answers itself to Supply; availability
//   is asked per fact, lazily, in the caller's order. Enumerate is a leaf of the judgment graph: its walk compares interned Types
//   (FactStates, AssociatedIdentityMatches) and calls no judgment, so a request demands only while it prepares its view
//   (IndexNormalized), and a demand made there reads the written facts alone; PremiseRefines entered from a walk is InternalInvariant.
//   Enumerate's tables live for one outermost request and are keyed by the view value (the scope and its serial, taken once per
//   request), so a scope re-parented within a request is another view; a preparation that moves its own view is InternalInvariant.
//   The walk reads the requirement Contract (ContractSupply's rewrites substitute activeRequirementContract), which is constant
//   over a request (its writers bind calls, reached from no normalization and no walk): the keys need no call context, and an index
//   kept across requests would have to key it. The caller of Reading holds its view's serial: a loop that writes the chain only
//   through Reading (Supply's, IndexNormalized's) passes its last observation and a formation takes it again; a loop with other
//   writes (SymbolicCapability's judgments) takes one per fact. A write added to such a loop outside Reading breaks this.
//   Rows depend on neither the order of facts or clauses nor on which check asked first (SPEC 8.3.3). Enumerate is complete modulo
//   identity of supplied subjects (facts and clause instances): a derivation that must rewrite a supplied subject by an identity
//   premise or a binding yields no row (beside `T is CopyTree`, `U is T` supplies nothing to U's projections while `T is U` does);
//   R7-C2c2's identity key map adds those rows, and a missing row only rejects.
// - Prohibitions: no judgment, lookup, normalization or inference rule here but the request boundary's normalization of a fact, which
//   only Reading calls (temporary until R7-C2c2 keys Types by identity class); Enumerate's walk and what it calls never
//   reach ProveConformance, ProveConstraint, ResolveAssociated, NormalizeProof*, SubstituteIdentity or RequestCapability
//   (ArchitectureRulesTest); no mode or bool parameter; no answer kept across requests but a Complete reading (below); no second
//   scheduler; no read sets. From C3 (C1 §5), broken until then by the eager derivation moved here: no Binding writes,
//   syntax-triggered supply, DerivedFacts or fallback scan.
//
// The view and its closure (DECISIONS R7-C2e1). ViewOf names the state of a scope's chain: the serial changes when an environment's
// Version, a scope's LinkVersion or the parent's serial does (each counted at its writer). Reading is the one caller of the request
// boundary's normalization of a fact (NormalizeProofConstraint): in the capability phase, with normalizingAssociated and
// identityExpansions empty, it keeps the reading per (view scope, fact, requirement Contract) under the serial taken before the
// formation (the caller's, above), for one pass (ResetCapabilities), when its derivation is Complete. Any other request forms its
// readings itself.
// - Completion (C1 §5's, without Resource until f): each function of the normalization gives its answer with one; its zero is
//   Provisional, a composite rule's answer is the meet of its parts', and only these members give Complete (W1-W6;
//   ArchitectureRulesTest): NormalizeProofConstraint, NormalizeProofType (W1); SubstituteIdentity (W2); IdentityPremise (W3);
//   ContractType, ContractPart, ContractTypeCore, ContractLength, ApplyContractEnvironment, SubstituteContractReference (W4);
//   ProjectionSpecification, ResolveAssociated(receiver), BuiltinArithmeticOutput's and FixedArrayIteratorType's gates (W5);
//   AvailableConstraintFact (W6). Provisional is a conservative superset of C1's: the derivation took a route not shown final in the
//   pass (a derived fact's availability; a projection route past its gate; the batched route; a Callable fact; StoredType's null).
// - Within a member an unthreaded call counts as Complete, so a member calls only these besides the members above, each pure or
//   final in the capability phase; each change to a member is reviewed against this list: the view's reads (HasParameterIdentity,
//   EnclosingContractSelf, a chain environment's sets), FactStates, NegateConstraint, InternConstraint, interning and Type rewriting
//   (TyCtxt.Rebuild, Intern, HeldContext, HeldType, InterpolationBound, WithContext, RentTypes, StoredType and SubstituteType where
//   they return a Type), the static gates (ArithmeticContracts.IsArithmetic, WitnessGroup), SubstituteStoredOrigins (declared
//   Origins), ConformanceByDeclaration (final after the InheritedConformances query, Binding.cs's RegisterInheritedConformances),
//   and the PrepareTypeStorage memo (per storageVersion).

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The closure's kept readings (Reading), emptied with the capability results each pass (ResetCapabilities), capacity kept.
    private readonly Dictionary<(BindingScope View, BoundConstraint Fact, BindingSymbol? Requirement), (int Serial, BoundConstraint Reading)> readings = new();

    private BoundConstraint[] contractFactScratch = [];

    // The premises a request reads: the scope its consumer holds. C3 adds the definition whose collection is active and the
    // inadmissible bindings here, in ViewOf (BU-29); a source's state and its check under way stay on the source (SourceStates, BU-59).
    private readonly record struct EvidenceView(BindingScope Premises);

    // Supply's answer: whether the premises supply the proposition (Positive) and its negation (Negative). Invalid (temporary, BU-29;
    // removed at C3 when B 3.10's declaration validation answers a request reaching a premise error): the chain's Invalid environment,
    // which ProveProposition answers Error resting on (InvalidEnvironment).
    private readonly struct Evidence
    {
        internal bool Positive { get; init; }

        internal bool Negative { get; init; }

        internal ConstraintEnvironment? Invalid { get; init; }
    }

    // C1 §5: whether an answer rests only on what is final in the pass (Complete) or not (Provisional, the zero).
    private enum Completion : byte
    {
        Provisional,
        Complete,
    }

    private static Completion Meet(Completion a, Completion b) => a < b ? a : b;

    // The serial of the view `scope` denotes: changed when the scope's environment, its link or its parent's view changed since the
    // scope last denoted a view; so one serial of one scope names one state of its chain.
    private static int SerialOf(BindingScope scope)
    {
        var parent = scope.Parent is { } next ? SerialOf(next) : 0;
        var state = (scope.Constraints?.Version ?? 0, scope.LinkVersion, parent);
        if (state != scope.ViewState)
        {
            scope.ViewState = state;
            scope.ViewSerial++;
        }

        return scope.ViewSerial;
    }

    private static (BindingSymbol? Symbol, BoundType Type) SubjectKey(BoundType type)
        => type.Kind == BoundTypeKind.AssociatedProjection ? (type.Symbol, type.Components[0]) : (null, type);

    // SPEC 8.3.2's refinement row over Enumerate's rows: one of them is a refinement of `contract`.
    private static bool RowsRefine(List<BindingSymbol>? rows, BindingSymbol contract)
    {
        for (var i = 0; rows is not null && i < rows.Count; i++)
        {
            if (IsRefinement(rows[i], contract))
            {
                return true;
            }
        }

        return false;
    }

    private EvidenceView ViewOf(BindingScope scope) => new(scope);

    // The reading of `fact` in the view `scope` denotes (the class header's closure). `serial` is the view's serial as the caller last
    // took it (null: not taken). Only a formation can write the chain, so a caller whose loop makes no other write takes it once: a
    // formation leaves the serial it took after itself, or null where it took none.
    private BoundConstraint Reading(BoundConstraint fact, BindingScope scope, ref int? serial)
    {
        if (!this.capabilitiesReady || this.normalizingAssociated.Count != 0 || this.identityExpansions.Count != 0)
        {
            serial = null;
            this.readingFormations++;
            return this.NormalizeProofConstraint(fact, scope, out _);
        }

        var before = serial ??= SerialOf(scope);
        var key = (scope, fact, this.activeRequirementContract);
        if (this.readings.TryGetValue(key, out var kept) && kept.Serial == before)
        {
            return kept.Reading;
        }

        this.readingFormations++;
        var reading = this.NormalizeProofConstraint(fact, scope, out var completion);
        serial = SerialOf(scope);
        if (completion == Completion.Complete && serial == before)
        {
            this.readings[key] = (before, reading);
        }

        return reading;
    }

    // `proposition` is normalized by its caller (NormalizeProofConstraint); a fact is normalized only under the presence gate.
    private Evidence Supply(BoundConstraint proposition, EvidenceView view)
    {
        var positive = false;
        var negative = false;
        var parameterIdentity = HasParameterIdentity(view.Premises);
        var negation = this.NegateConstraint(proposition);
        int? serial = null; // The loop writes nothing but through Reading.
        for (var current = view.Premises; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            if (environment.Invalid)
            {
                return new() { Invalid = environment };
            }

            positive |= this.AvailableConstraintFact(environment, proposition, out _);
            negative |= this.AvailableConstraintFact(environment, negation, out _);
            if (environment.HasAssociatedProjection || parameterIdentity || proposition.Kind == ConstraintKind.Callable || proposition is { Kind: ConstraintKind.Not, Left.Kind: ConstraintKind.Callable })
            {
                foreach (var fact in environment.Facts)
                {
                    if (!this.AvailableConstraintFact(environment, fact, out _))
                    {
                        continue;
                    }

                    var identity = this.Reading(fact, view.Premises, ref serial);
                    positive |= SameProofPremise(identity, proposition);
                    negative |= SameProofPremise(identity, negation);
                }
            }
        }

        return new() { Positive = positive, Negative = negative };
    }

    // Enumerate's tables, owned by one outermost request (emptied, capacity kept, when it returns) and keyed by the view value: the rows
    // of a link of the subject's chain in a view (null while the link is open), the view's Contract facts by subject key (IndexWritten,
    // IndexNormalized), the views indexed, a Contract's head depth, and the pooled lists.
    private readonly Dictionary<(BoundType, EvidenceView, int), List<BindingSymbol>?> enumerateMemo = new();

    private readonly Dictionary<(EvidenceView, int, BindingScope, BindingSymbol?, BoundType), List<(BoundConstraint Raw, BoundConstraint Fact)>>
        enumerateIndex = new();

    private readonly HashSet<(EvidenceView, int)> enumeratePrepared = new();
    private readonly List<(BindingScope Scope, BoundConstraint Raw, BoundConstraint Fact)> enumerateNormalized = new();
    private readonly Dictionary<BindingSymbol, int> enumerateHeads = new(ReferenceEqualityComparer.Instance);

    // The rows an outermost request walked in the kept region (Reading's), per link, view value and requirement Contract, for one
    // capability epoch (ClearCapabilityResults empties them with the capability results, capacity and pooled lists kept).
    private readonly Dictionary<(BoundType, EvidenceView, int, BindingSymbol?), List<BindingSymbol>> enumerateKept = new();
    private readonly List<List<BindingSymbol>> enumerateKeptPool = new();
    private readonly List<List<BindingSymbol>> enumeratePool = new();
    private readonly List<List<(BoundConstraint Raw, BoundConstraint Fact)>> enumerateFactPool = new();
    private readonly List<BoundType> enumerateChain = new();
    private readonly List<(BoundType Prefix, int Reach)> enumerateActive = new();
    private int enumeratePoolNext;
    private int enumerateFactPoolNext;
    private int enumerateDepth;
    private int enumerateWalking;
    private int enumerateKeptPoolNext;
    private int readingFormations;

    // SPEC 8.3.2's refinement row: the premises of `scope` supply `subject is R` for a refinement R of `contract`. The subject is as
    // its reader has it (identity is the request boundary's: the reader's subject and, for the outermost request, the view's facts).
    private bool PremiseRefines(BoundType subject, BindingSymbol contract, BindingScope scope)
    {
        if (this.enumerateWalking > 0)
        {
            throw new InvalidOperationException($"PremiseRefines of `{subject}` entered from Enumerate's walk (InternalInvariant; R7-C2a).");
        }

        var view = this.ViewOf(scope);
        var serial = SerialOf(scope);
        var keep = this.enumerateDepth == 0 && this.capabilitiesReady && this.normalizingAssociated.Count == 0 && this.identityExpansions.Count == 0;
        if (keep && this.enumerateKept.TryGetValue((subject, view, serial, this.activeRequirementContract), out var kept))
        {
            return RowsRefine(kept, contract);
        }

        this.enumerateDepth++;
        try
        {
            if (this.enumeratePrepared.Add((view, serial)))
            {
                this.IndexWritten(view, serial);
            }

            // The walk demands nothing, so a nested demand comes from the outermost request's preparation: it reads the written facts
            // alone, and the rows it left are dropped before the outermost walk reads the whole index. The request's premises are the
            // view it took: a demand of its preparation that moved them is a builder's broken contract.
            if (this.enumerateDepth == 1)
            {
                var formations = this.readingFormations;
                if (this.IndexNormalized(view, serial) != serial)
                {
                    throw new InvalidOperationException($"The preparation of the request for `{subject}` moved its own view (InternalInvariant; R7-C2a).");
                }

                this.enumerateMemo.Clear();
                keep &= formations == this.readingFormations; // Every reading of the preparation was a kept one.
            }

            this.enumerateWalking++;
            try
            {
                var rows = this.Enumerate(subject, view, serial);
                if (keep)
                {
                    this.KeepRows(view, serial);
                }

                return RowsRefine(rows, contract);
            }
            finally
            {
                this.enumerateWalking--;
            }
        }
        finally
        {
            if (--this.enumerateDepth == 0)
            {
                this.enumerateMemo.Clear();
                this.enumerateIndex.Clear();
                this.enumeratePrepared.Clear();
                this.enumeratePoolNext = 0;
                if (!this.capabilitiesReady)
                {
                    this.enumerateHeads.Clear(); // Before the capability phase a Contract's clauses may still be bound.
                }

                this.enumerateFactPoolNext = 0;
            }
        }
    }

    // The outermost walk's rows of the view, kept for the epoch under the request's requirement Contract (pooled copies).
    private void KeepRows(EvidenceView view, int serial)
    {
        foreach (var ((link, linkView, linkSerial), rows) in this.enumerateMemo)
        {
            if (rows is null || linkView != view || linkSerial != serial ||
                this.enumerateKept.ContainsKey((link, view, serial, this.activeRequirementContract)))
            {
                continue;
            }

            List<BindingSymbol> copy;
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
            this.enumerateKept[(link, view, serial, this.activeRequirementContract)] = copy;
        }
    }

    // The capability epoch ended (ClearCapabilityResults): kept rows and head depths go with the capability results.
    private void ClearKeptRows()
    {
        this.enumerateKept.Clear();
        this.enumerateKeptPoolNext = 0;
        this.enumerateHeads.Clear();
    }

    // The view's Contract facts by subject key, as written.
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
                if (fact is { Kind: ConstraintKind.Contract, Subject: not null, Contract: not null })
                {
                    this.IndexFact(view, serial, scope, fact, fact);
                }
            }
        }
    }

    // The outermost request's view, under a parameter identity: each fact as the view reads it (Reading, every reader's), added after
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
                this.IndexFact(view, serial, this.enumerateNormalized[i].Scope, this.enumerateNormalized[i].Raw, this.enumerateNormalized[i].Fact);
            }

            this.enumerateNormalized.RemoveRange(start, this.enumerateNormalized.Count - start);
        }

        return after ?? SerialOf(view.Premises);
    }

    // The rows of `subject` in the view (the contract above): its chain is walked from the root up, each link once per request and
    // view; a link's rows are final before a taller link reads them, and a prefix stays active while its height plus its rows' head
    // depth reaches the link. A link demanded again while open supplies nothing (SPEC 8.3.3).
    private List<BindingSymbol>? Enumerate(BoundType subject, EvidenceView view, int serial)
    {
        var chainStart = this.enumerateChain.Count;
        var activeStart = this.enumerateActive.Count;
        for (var link = subject; ; link = link.Components[0])
        {
            this.enumerateChain.Add(link);
            if (link.Kind != BoundTypeKind.AssociatedProjection)
            {
                break;
            }
        }

        try
        {
            for (var i = this.enumerateChain.Count - 1; i >= chainStart; i--)
            {
                var link = this.enumerateChain[i];
                var height = this.enumerateChain.Count - 1 - i; // The link's index from the root.
                for (var a = this.enumerateActive.Count - 1; a >= activeStart; a--)
                {
                    if (this.enumerateActive[a].Reach < height)
                    {
                        this.enumerateActive.RemoveAt(a);
                    }
                }

                if (!this.enumerateMemo.TryGetValue((link, view, serial), out var rows))
                {
                    this.enumerateMemo[(link, view, serial)] = null; // Open: a re-entry supplies nothing.
                    rows = this.EnumerateLink(link, view, serial, activeStart);
                    this.enumerateMemo[(link, view, serial)] = rows;
                }

                if (rows is { Count: > 0 })
                {
                    var head = 0;
                    for (var r = 0; r < rows.Count; r++)
                    {
                        head = Math.Max(head, this.HeadDepth(rows[r]));
                    }

                    this.enumerateActive.Add((link, head == int.MaxValue ? int.MaxValue : height + head));
                }
            }
        }
        finally
        {
            this.enumerateChain.RemoveRange(chainStart, this.enumerateChain.Count - chainStart);
            this.enumerateActive.RemoveRange(activeStart, this.enumerateActive.Count - activeStart);
        }

        return this.enumerateMemo[(subject, view, serial)];
    }

    private List<BindingSymbol> EnumerateLink(BoundType subject, EvidenceView view, int serial, int activeStart)
    {
        List<BindingSymbol> rows;
        if (this.enumeratePoolNext < this.enumeratePool.Count)
        {
            rows = this.enumeratePool[this.enumeratePoolNext++];
            rows.Clear();
        }
        else
        {
            rows = new();
            this.enumeratePool.Add(rows);
            this.enumeratePoolNext++;
        }

        if (IsContractSelf(subject) && subject.Symbol!.SelfOf is { } self && this.AvailableContractPremise(self))
        {
            Add(rows, self);
        }

        for (var current = view.Premises; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { Invalid: false } environment)
            {
                continue;
            }

            // A fact is indexed by subject key, as written and as normalized in the view: FactStates confirms a match.
            if (this.enumerateIndex.GetValueOrDefault((view, serial, current, SubjectKey(subject).Symbol, SubjectKey(subject).Type)) is not { } facts)
            {
                continue;
            }

            for (var f = 0; f < facts.Count; f++)
            {
                var (raw, fact) = facts[f];
                if (this.FactStates(fact, subject, out _) && this.AvailableConstraintFact(environment, raw, out _) &&
                    this.AvailableContractPremise(fact.Contract!))
                {
                    Add(rows, this.AppliedAssociatedContract(fact, subject));
                }
            }
        }

        for (var a = activeStart; a < this.enumerateActive.Count; a++)
        {
            var root = this.enumerateActive[a].Prefix;
            if (this.enumerateMemo.GetValueOrDefault((root, view, serial)) is not { } prefix)
            {
                continue;
            }

            for (var i = 0; i < prefix.Count; i++)
            {
                if (prefix[i].Contract is not { } shape)
                {
                    continue;
                }

                foreach (var (_, _, premise) in new ContractSupply(this, shape, root, view.Premises))
                {
                    if (premise is { Kind: ConstraintKind.Contract, Subject: { } stated, Contract: { } symbol } &&
                        AssociatedIdentityMatches(stated, subject) &&
                        this.AvailableContractPremise(symbol))
                    {
                        Add(rows, symbol);
                    }
                }
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Contract is not { } shape)
            {
                continue;
            }

            foreach (var (ancestor, declaration, premise) in new ContractSupply(this, shape, subject, view.Premises))
            {
                if (premise is null && ancestor >= 0)
                {
                    Add(rows, declaration.Symbol);
                }
            }
        }

        return rows;

        static void Add(List<BindingSymbol> list, BindingSymbol contract)
        {
            if (!list.Contains(contract))
            {
                list.Add(contract);
            }
        }
    }

    // One fact of an environment of the view under its subject key (a projection's declaration and receiver, else the Type): `raw` as
    // the environment holds it (its availability), `fact` as the index reads it.
    private void IndexFact(EvidenceView view, int serial, BindingScope scope, BoundConstraint raw, BoundConstraint fact)
    {
        var key = (view, serial, scope, SubjectKey(fact.Subject!).Symbol, SubjectKey(fact.Subject!).Type);
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

        list.Add((raw, fact));
    }

    // The deepest Self-rooted head among a Contract's and its ancestors' Self-dependent clauses; int.MaxValue when a clause's subject is
    // not rooted at Self (it may name any Type, so its prefix stays active).
    private int HeadDepth(BindingSymbol contract)
    {
        if (this.enumerateHeads.TryGetValue(contract, out var known))
        {
            return known;
        }

        if ((contract.Declaration.BoundSymbol?.Contract ?? contract.Contract) is not { } origin)
        {
            this.enumerateHeads[contract] = 0;
            return 0;
        }

        var depth = this.ClauseHeads(origin);
        for (var i = 0; i < origin.Ancestors.Count && depth != int.MaxValue; i++)
        {
            if (origin.Ancestors[i].Declaration.BoundSymbol?.Contract is { } ancestor)
            {
                depth = Math.Max(depth, this.ClauseHeads(ancestor));
            }
        }

        this.enumerateHeads[contract] = depth;
        return depth;
    }

    private int ClauseHeads(BoundContract declaration)
    {
        var depth = 0;
        for (var i = 0; i < declaration.ClauseStorage.Count; i++)
        {
            if (declaration.ClauseStorage[i].BoundConstraint is { } constraint && this.DependentConstraint(constraint, contractSelf: true))
            {
                depth = Math.Max(depth, Heads(constraint));
            }
        }

        return depth;

        static int Heads(BoundConstraint constraint)
        {
            if (constraint.Left is not null || constraint.Right is not null)
            {
                return Math.Max(constraint.Left is { } left ? Heads(left) : 0, constraint.Right is { } right ? Heads(right) : 0);
            }

            var steps = 0;
            var root = constraint.Subject;
            for (; root is { Kind: BoundTypeKind.AssociatedProjection }; root = root.Components[0])
            {
                steps++;
            }

            return root is null || IsContractSelf(root) ? steps : int.MaxValue;
        }
    }

    // A Contract premise is available when the Contract and each ancestor (the deduplicated transitive closure) are valid sources: its
    // declaration context and its published source state (Binding.SourceStates).
    private bool AvailableContractPremise(BindingSymbol contract)
    {
        var ancestors = contract.Contract?.Ancestors;
        for (var i = -1; i < (ancestors?.Count ?? 0); i++)
        {
            var source = i < 0 ? contract : ancestors![i];
            if (this.InvalidDeclarationContext(source.Declaration) || UnresolvedTypeDeclarationContext(source.Declaration) ||
                this.ReadSourceState(source) != ConstraintProof.Proven)
            {
                return false;
            }
        }

        return true;
    }

    // W6: Complete when the fact is not in the environment or is direct; a derived fact's availability reads its sources' states.
    private bool AvailableConstraintFact(ConstraintEnvironment environment, BoundConstraint fact, out Completion completion)
    {
        completion = Completion.Complete;
        if (!environment.Facts.Contains(fact))
        {
            return false;
        }

        if (environment.DirectFacts.Contains(fact))
        {
            return true;
        }

        completion = Completion.Provisional;
        for (var i = 0; i < environment.DerivedFacts.Count; i++)
        {
            var derived = environment.DerivedFacts[i];
            if (ReferenceEquals(derived.Fact, fact) && this.AvailableContractPremise(derived.Source))
            {
                return true;
            }
        }

        return false;
    }

    private void ExpandContractPremises()
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not (FunctionKoto or DeclarationContainerKoto or SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto }))
            {
                continue;
            }

            var scope = this.scopes[this.nodes[n]];
            if (scope.Owner is ContractKoto contract)
            {
                // SPEC 8.7.3: inside a Contract, an inherited Self clause holds for its Self, as one declared directly does.
                this.AddContractPremises(contract.BoundSymbol!.Contract!, this.ContractSelfType(contract.BoundSymbol), scope);
            }

            this.ExpandScopeContractPremises(scope);
        }
    }

    private void ExpandScopeContractPremises(BindingScope scope)
    {
        if (scope.Constraints is not { } environment || environment.Facts.Count == 0)
        {
            return;
        }

        var count = environment.Facts.Count;
        if (this.contractFactScratch.Length < count)
        {
            this.contractFactScratch = new BoundConstraint[Math.Max(count, Math.Max(16, this.contractFactScratch.Length * 2))];
        }

        // Expansion performs no proof or nested scope expansion, so one owned buffer suffices.
        var facts = this.contractFactScratch;
        environment.Facts.CopyTo(facts);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (facts[i] is { Kind: ConstraintKind.Contract, Contract.Contract: { } shape, Subject: { } subject })
                {
                    this.AddContractPremises(shape, subject, scope);
                }
            }
        }
        finally
        {
            Array.Clear(facts, 0, count);
        }
    }

    private void AddContractPremises(BoundContract shape, BoundType self, BindingScope scope)
    {
        var environment = scope.Constraints ??= new();
        foreach (var (ancestor, declaration, premise) in new ContractSupply(this, shape, self, scope))
        {
            if (premise is not null)
            {
                this.AddConstraintFact(environment, premise, shape.Symbol);
                continue;
            }

            if (ancestor >= 0)
            {
                var inherited = this.InternConstraint(new(ConstraintKind.Contract, self, contract: declaration.Symbol));
                this.AddConstraintFact(environment, inherited, shape.Symbol);
            }

            this.AddArithmeticPremises(declaration, self, environment, shape.Symbol);
        }
    }

    // SPEC 8.3.2: what a Contract premise `self is shape` supplies from its declarations, the one enumeration of it, in the order
    // AddContractPremises adds it: the Contract, then each ancestor of its declaration (Ancestor: -1, then the ancestor's index there) as
    // `self` and the shape make it (InstantiatedContract: the declaration's Self is `self`, the shape's arguments keep theirs; G180), each
    // first as itself (Premise null: its own facts, an ancestor's `self is A` and the arithmetic premises, AddContractPremises'), then each
    // of its clause constraints that depends on the Contract's Self (DependentConstraint) as `self`, `scope` and that reference make it
    // (ContractConstraint, not normalized), evaluated when reached. Read by AddContractPremises and by retention's HandedContract.
    private ref struct ContractSupply(Binding binding, BoundContract shape, BoundType self, BindingScope scope)
    {
        private int ancestor = -1;
        private int next; // The declaration's next item: 0 the declaration itself, i + 1 its clause i.

        public (int Ancestor, BoundContract Declaration, BoundConstraint? Premise) Current { get; private set; }

        // The shape's declaration, whose ancestors are written in its own terms.
        private readonly BoundContract Origin => shape.Symbol.Declaration.BoundSymbol?.Contract ?? shape;

        public readonly ContractSupply GetEnumerator() => this;

        public bool MoveNext()
        {
            for (; this.ancestor < this.Origin.Ancestors.Count; (this.ancestor, this.next) = (this.ancestor + 1, 0))
            {
                if (this.next == 0)
                {
                    var instantiated = this.ancestor < 0 ? shape
                        : binding.InstantiatedContract(this.Origin.Ancestors[this.ancestor], scope, self, normalize: false, shape.Symbol).Contract!;
                    (this.next, this.Current) = (1, (this.ancestor, instantiated, null));
                    return true;
                }

                var declaration = this.Current.Declaration;
                for (; this.next <= declaration.ClauseStorage.Count; this.next++)
                {
                    if (declaration.ClauseStorage[this.next - 1].BoundConstraint is { } constraint &&
                        binding.DependentConstraint(constraint, contractSelf: true))
                    {
                        this.Current = (this.ancestor, declaration, binding.ContractConstraint(constraint, scope, self, false, declaration.Symbol));
                        this.next++;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
