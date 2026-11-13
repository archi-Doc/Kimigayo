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
//   the subject or a Type of its identity class, as written and, under a parameter identity, as the view reads it (Reading); (2) a
//   Self-dependent clause of a row of a proper prefix of the subject's projection chain, Self being that prefix, whose stated subject
//   is the subject or of its class; (3) a Contract's own Self; (4) a row's ancestors.
// - Invariants: innermost scope first, each environment in its order; an Invalid environment answers itself to Supply; availability
//   is asked per fact, lazily, in the caller's order. Enumerate is a leaf of the judgment graph: its walk compares interned Types
//   (FactStates, AssociatedIdentityMatches) and their class keys (SameClass: ClassKey reads a built table and interns) and calls no
//   judgment, so a request demands only while it prepares its view (ClassesOf, IndexNormalized); a demand made there is a request
//   over its own view, with its own classes (restored on return), and reads that view's written facts and keys alone; PremiseRefines
//   entered from a walk is InternalInvariant.
//   Enumerate's request tables (memo, pools) live for one outermost request and are keyed by the view value (the scope and its
//   serial, taken once per request), so a scope re-parented within a request is another view; a preparation that moves its own
//   view is InternalInvariant. The walk reads the requirement Contract (ContractSupply's rewrites substitute
//   activeRequirementContract), which is constant over a request (its writers bind calls, reached from no normalization and no
//   walk): the request tables need no call context in their keys, and the view's index and the kept rows, which outlive a request,
//   key it (Closure(view) and Kept rows, below). The caller of Reading holds its view's serial: a loop that writes the chain only
//   through Reading (Supply's, IndexNormalized's) passes its last observation and a formation takes it again; a loop with other
//   writes (SymbolicCapability's judgments) takes one per fact. A write added to such a loop outside Reading breaks this.
//   Rows depend on neither the order of facts or clauses nor on which check asked first (SPEC 8.3.3).
// - Identity (R7-C2c2 c2-1; SPEC 8.2.3): a premise's supply reaches every parameter and projection of its identity class; a
//   constructed member's conformance is ResolveConformance's. That is the implementation's limit, not SPEC's rule: where it is
//   Unknown, a premise is not yet read as evidence for that member (SPEC 8.2.3), so valid programs such as d09c stay rejected, as on
//   base, until c2-4 (a constructed member's conformance with premises for its arguments). Each request keys its
//   subject, its view's facts and the walk's comparisons by its own view's classes (ClassKey, Binding.TypeIdentity's: a class's least
//   parameter or projection; an erroneous or incomplete class keys nothing), whatever the table's finality, which decides only kept
//   rows. The written subject is walked beside its key and both refine, so a key only adds rows. A key never leaves the supply: no
//   reader receives it as a Type (on a class match AppliedAssociatedContract reads the fact's own subject). Not yet supplied: a
//   subject only a binding completes beyond Reading's one level (ProjectionSpecification; c2-4), and other clause kinds at depth (the
//   eager supply's one level until c2-2, c2-3); a missing row only rejects.
// - Prohibitions: no judgment, lookup, normalization or inference rule here but the request boundary's normalization of a fact, which
//   only Reading calls (temporary until R7-C2c2 c2-4 removes its binding completion); Enumerate's walk and what it calls never
//   reach ProveConformance, ProveConstraint, ResolveAssociated, NormalizeProof*, SubstituteIdentity or RequestCapability
//   (ArchitectureRulesTest); no mode or bool parameter; no answer kept across requests but a Complete reading and the kept rows,
//   each in the kept region alone, and the view's index when Complete (all below); no second scheduler; no read sets. From C3
//   (C1 §5), broken until then by the eager derivation moved here: no Binding writes, syntax-triggered supply, DerivedFacts or
//   fallback scan.
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
// - PremiseRefines' answer may rest on a Provisional identity table (a view holding a derived identity fact, W6, W7) and reaches its
//   callers unthreaded, as Complete, as its availability reads do; carrying it is R7-C2e2's (Completion carried by the judgments'
//   answers). No such table is known to grow after the read within a pass.
//
// Closure(view) (DECISIONS R7-C2e2-1, R7-C2e2-2): a view's index of its facts by kind and subject is one table, an entry per (view
// scope, requirement Contract) formed at the view's serial and rebuilt in place when the serial moves, its buckets by (chain scope,
// kind, SubjectKey).
// - Rows: a kind has a row when a reader reads it by subject: Contract (Enumerate's walk), TypeIdentity
//   (ValidateAssociatedOverlapAgreement, DisjointConformanceFacts) and Semantics (DisjointConformanceFacts). The Contract row holds
//   each fact as written, under its subject and its class key in the view (R7-C2c2 c2-1: the forming request's classes, which are
//   the view's), and, under a parameter identity, as the view reads it (Reading). The TypeIdentity and Semantics rows hold each fact
//   as written under its written subject alone, with no class key and no reading: a bucket of theirs holds the facts of one written
//   subject key, so a reader that pairs by bucket pairs no other subject's fact.
// - Kept: an entry is Kept when its content is Complete: written facts in every phase (a function of the chain's environments, which
//   the serial counts), their class keys when the view has no identity premise or its identity table is Final (W7, the rule the kept
//   rows ask: a table that is not is rebuilt per read), readings where KeptRegion kept them (the one rule), so an entry whose keys
//   are not final or whose preparation formed a reading that was not kept stays owned by its outermost request and goes when that
//   returns. A Kept entry lasts the pass (ClearClosure, in ResetCapabilities) and spares a later outermost request its preparation;
//   its readings are read only in KeptRegion, so outside it an outermost request forms its own (R7-M20) and the entry again. A
//   nested demand reads an entry's written facts and their keys alone (the leaf rule), so it reads any entry.
// - Readers: a request (PremiseRefines) and a closure validator's read (BeginClosureRead to EndClosureRead), which is outside any
//   request and reads the TypeIdentity and Semantics rows. Whoever holds an entry starts no request, since a request's return drops
//   the entries it does not keep: PremiseRefines entered from a walk or from a validator's read is InternalInvariant, so a validator
//   takes what it needs from the entry before it judges. An entry a validator's read forms is the read's: formed outside a request,
//   its Contract row has no class key and no reading, so no request may read it, and it goes back to the pool when the read ends. A
//   Kept entry serves a read as it is.
// - Retained memory is the pool's high water: an entry keeps the largest buckets and lists any view gave it, flat across warm
//   rebinds. Temporary: the readings go when R7-C2c2 c2-4 removes Reading's binding completion.
//
// Kept rows (DECISIONS R7-C2a1, the owner's O-206 (a)). Temporary: R7-C2e2's part 3 joins a view's rows to Closure(view) and deletes
// enumerateKept, its pool, KeepRows and the kept region's part of PremiseRefines.
// - What and key: the rows of each link an outermost request walked, per (link, view scope, serial, requirement Contract), and a
//   Contract's head depth per Contract.
// - Where: the kept region (KeptRegion, R7-M20's rule, which Reading and PremiseRefines both ask), an outermost request, a
//   preparation that formed no reading, a view with no identity premise or a Final identity table (its keys are then a function of
//   the view and the pass; a view with a derived identity fact keeps none until R7-C2e2): each fact of its index was a kept, Complete
//   reading, so the index is a function of the key and the pass. Nested demands never read or fill the rows.
// - Lifetime: one epoch (KeepsAcrossRequests, the one phase fact; ClearKeptRows, the one clear). In the capability phase it is the
//   capability epoch, which ClearCapabilityResults ends with the capability results and the component summaries; before the phase it
//   is one outermost request (a Contract's clauses are still being bound, so not even a head depth outlives it).
// - Inputs outside the key, and what covers a write to each inside an epoch: Contract clause storage (HeadDepth, ContractSupply):
//   bound before the phase, none. The availability reads (AvailableConstraintFact, AvailableContractPremise): an environment's fact
//   sets and its Invalid (in the serial); a published source state, which only leaves Pending and is written as each check of a
//   publication round ends; a Contract's declaration context, which the validations only fail (ValidateConstraintEnvironments;
//   ValidateClosedTypeConstraints in InvalidateProjectionCertificates' loop). A kept row may be read across such a write until
//   the epoch ends, exactly as a capability result may: a round or a loop iteration that wrote one is followed by a clear and
//   another, a declaration left Pending is checked again in a fresh epoch, and the last round and the last iteration write none,
//   so what outlives them read final states (SourceStates: no answer computed from a Pending read outlives its round). A row read
//   across a write is a Pending read's (the row of a Contract proven since is absent) or names a Contract that has failed since,
//   whose own record rejects the program and whose failure makes each environment naming it Invalid, another serial. Only a
//   premise-bearing subject has rows (one rooted in a generic input or a Contract's Self: a closed proposition is never a
//   premise), so a closed clause's own requests, over closed subjects, have empty rows on every walk; what a non-final round does
//   not redo is a proof step that asked such a subject in another declaration's scope before a state it read changed (a
//   hypothesis the capability results share: a node is keyed by proposition and scope).
// - Not replayed: a kept answer skips the walk's per-check effects, which a capability result replays from its payload:
//   InvalidDeclarationContext's Consulted (SPEC 23.1.2), ReadSourceState's join of a limit, a U fact or a cause (BU-22) and, outside
//   a proof scope, its frame. They exist only where a source is not valid, whose own record is reported: what can differ is the
//   dependence or the cause of a failing check's record in a rejected program, never an acceptance (DECISIONS Q13).

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

    // The scaling rows' measure: the pairs Closure(view)'s entries indexed in this compilation, in every row (a Contract, TypeIdentity
    // or Semantics fact as written; a Contract fact under its class key and as read), the validators' entries included.
    internal int ClosureIndexWork { get; private set; }

    private EvidenceView ViewOf(BindingScope scope) => new(scope);

    // The kept tables' one phase fact: whether a table may outlive an outermost request. The capability phase (capabilitiesReady,
    // raised once per pass after every Contract clause is bound) keeps readings for the pass and rows and head depths for the
    // capability epoch; before it an epoch is one request (the class header's Kept rows).
    private bool KeepsAcrossRequests => this.capabilitiesReady;

    // R7-M20's kept region, the one rule every kept table asks (Reading's readings, PremiseRefines' rows): a table may outlive the
    // request, and neither an associated normalization nor an identity expansion is under way (their contexts are in no key).
    private bool KeptRegion => this.KeepsAcrossRequests && this.normalizingAssociated.Count == 0 && this.identityExpansions.Count == 0;

    // The reading of `fact` in the view `scope` denotes (the class header's closure). `serial` is the view's serial as the caller last
    // took it (null: not taken). Only a formation can write the chain, so a caller whose loop makes no other write takes it once: a
    // formation leaves the serial it took after itself, or null where it took none.
    private BoundConstraint Reading(BoundConstraint fact, BindingScope scope, ref int? serial)
    {
        if (!this.KeptRegion)
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
            this.readingStores++;
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

    // Enumerate's request tables, owned by one outermost request (emptied, capacity kept, when it returns) and keyed by the view value:
    // the rows of a link of the subject's chain in a view (null while the link is open) and the pooled lists.
    private readonly Dictionary<(BoundType, EvidenceView, int), List<BindingSymbol>?> enumerateMemo = new();

    // Closure(view) (the class header's): the entry of each (view scope, requirement Contract), the entries the outermost request formed
    // (dropped when it returns unless Kept), and the free entries, each with its lists and their capacity.
    private readonly Dictionary<(EvidenceView, BindingSymbol?), ViewIndex> enumerateIndex = new();
    private readonly List<(EvidenceView, BindingSymbol?)> enumerateOwned = new();
    private readonly Stack<ViewIndex> enumerateIndexPool = new();
    private readonly List<(BindingScope Scope, BoundConstraint Raw, BoundConstraint Fact)> enumerateNormalized = new();

    // The kept tables (the class header's Kept rows), emptied by ClearKeptRows as their epoch ends, capacity and pooled lists kept:
    // a Contract's head depth, and the rows an outermost request walked in the kept region per link, view value and requirement
    // Contract.
    private readonly Dictionary<BindingSymbol, int> enumerateHeads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(BoundType, EvidenceView, int, BindingSymbol?), List<BindingSymbol>> enumerateKept = new();
    private readonly List<List<BindingSymbol>> enumerateKeptPool = new();
    private readonly List<List<BindingSymbol>> enumeratePool = new();
    private readonly List<BoundType> enumerateChain = new();
    private readonly List<(BoundType Prefix, int Reach)> enumerateActive = new();
    private int enumeratePoolNext;
    private int enumerateDepth;
    private int enumerateWalking;
    private int enumerateKeptPoolNext;
    private int readingFormations;
    private int readingStores;
    private IdentityClasses? enumerateClasses; // R7-C2c2: the current request's view's identity classes, or null

    // SPEC 8.3.2's refinement row: the premises of `scope` supply `subject is R` for a refinement R of `contract`. The subject is as
    // its reader has it, walked beside its class key in the view (the contract's Identity).
    private bool PremiseRefines(BoundType subject, BindingSymbol contract, BindingScope scope)
    {
        if (this.enumerateWalking > 0)
        {
            throw new InvalidOperationException($"PremiseRefines of `{subject}` entered from Enumerate's walk or a closure validator's read (InternalInvariant; R7-C2a).");
        }

        // R7-C2c2: a request keys its subject and its view's facts by the view's identity classes (SPEC 8.2.3), whatever the table's
        // finality; finality decides only whether the rows and the view's entry may be kept for a later request.
        var classes = HasTypeIdentity(scope) ? this.ClassesOf(scope, out _) : null;
        var key = classes is null ? subject : this.ClassKey(subject, classes);
        var view = this.ViewOf(scope);
        var serial = SerialOf(scope);
        var final = classes is null ? !HasTypeIdentity(scope) : classes.Final; // The keys are a function of the view and the pass.
        var keep = this.enumerateDepth == 0 && this.KeptRegion && final;
        if (keep && this.enumerateKept.TryGetValue((subject, view, serial, this.activeRequirementContract), out var kept) &&
            (ReferenceEquals(key, subject) || this.enumerateKept.ContainsKey((key, view, serial, this.activeRequirementContract))))
        {
            return RowsRefine(kept, contract) ||
                RowsRefine(this.enumerateKept.GetValueOrDefault((key, view, serial, this.activeRequirementContract)), contract);
        }

        var enclosing = this.enumerateClasses;
        this.enumerateClasses = classes;
        this.enumerateDepth++;
        try
        {
            var index = this.IndexOf(view, serial);

            // The walk demands nothing, so a nested demand comes from the outermost request's preparation: it reads the written facts
            // alone, and the rows it left are dropped before the outermost walk reads the whole index. The request's premises are the
            // view it took: a demand of its preparation that moved them is a builder's broken contract. A Kept entry needs no preparation.
            if (this.enumerateDepth == 1)
            {
                if (!index.Kept)
                {
                    var (formations, stores) = (this.readingFormations, this.readingStores);
                    if (this.IndexNormalized(index, view) != serial)
                    {
                        throw new InvalidOperationException($"The preparation of the request for `{subject}` moved its own view (InternalInvariant; R7-C2a).");
                    }

                    keep &= formations == this.readingFormations; // Every reading of the preparation was a kept one.

                    // Every reading it formed was kept, and its class keys are final (R7-C2e2-1's N1).
                    index.Kept = final && this.readingFormations - formations == this.readingStores - stores;
                }

                this.enumerateMemo.Clear();
            }

            this.enumerateWalking++;
            try
            {
                var refines = RowsRefine(this.Enumerate(subject, view, index), contract);
                refines |= !ReferenceEquals(key, subject) && RowsRefine(this.Enumerate(key, view, index), contract);
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
            this.enumerateClasses = enclosing;
            if (--this.enumerateDepth == 0)
            {
                this.enumerateMemo.Clear();
                this.DropOwnedEntries();
                this.enumeratePoolNext = 0;
                if (!this.KeepsAcrossRequests)
                {
                    this.ClearKeptRows(); // Before the capability phase the epoch is this request.
                }
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

    // The kept tables' epoch ended: the capability epoch (ClearCapabilityResults) or, before the phase, the outermost request.
    private void ClearKeptRows()
    {
        this.enumerateKept.Clear();
        this.enumerateKeptPoolNext = 0;
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
                    index.Add(scope, fact, fact, fact.Subject);
                    this.ClosureIndexWork++;

                    // R7-C2c2 c2-1 meets R7-C2e2-2 here: the class-key copy is the Contract row's alone. The other rows keep written
                    // subjects, so the validators, which pair facts by bucket, never meet a fact of another written subject.
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

        return index;
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

    // The pass ended (ResetCapabilities): every entry goes back to the pool.
    private void ClearClosure()
    {
        foreach (var index in this.enumerateIndex.Values)
        {
            this.enumerateIndexPool.Push(index);
        }

        this.enumerateIndex.Clear();
    }

    // The outermost request's view, under a parameter identity: each fact as the view reads it (Reading, every reader's), added after
    // all are read. A reading's own demands are nested, so they read written facts alone: the index does not depend on the order of the
    // facts.
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

    // The rows of `subject` in the view (the contract above): its chain is walked from the root up, each link once per request and
    // view; a link's rows are final before a taller link reads them, and a prefix stays active while its height plus its rows' head
    // depth reaches the link. A link demanded again while open supplies nothing (SPEC 8.3.3).
    private List<BindingSymbol>? Enumerate(BoundType subject, EvidenceView view, ViewIndex index)
    {
        var serial = index.Serial;
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
                    rows = this.EnumerateLink(link, view, index, activeStart);
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

    private List<BindingSymbol> EnumerateLink(BoundType subject, EvidenceView view, ViewIndex index, int activeStart)
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

            // A fact is indexed by subject key, as written and as normalized in the view: FactStates confirms a match. A nested demand
            // reads the written facts alone (the leaf rule), so an entry an outermost request prepared serves it too.
            if (index.Buckets.GetValueOrDefault((current, ConstraintKind.Contract, SubjectKey(subject).Symbol, SubjectKey(subject).Type)) is not { } facts)
            {
                continue;
            }

            for (var f = 0; f < facts.Count; f++)
            {
                var (raw, fact) = facts[f];
                if (this.enumerateDepth != 1 && !ReferenceEquals(raw, fact))
                {
                    continue; // A nested demand reads the written facts and their keys alone.
                }

                var states = this.FactStates(fact, subject, out _);
                if ((states || this.SameClass(fact.Subject!, subject)) && this.AvailableConstraintFact(environment, raw, out _) &&
                    this.AvailableContractPremise(fact.Contract!))
                {
                    Add(rows, this.AppliedAssociatedContract(fact, states ? subject : fact.Subject!)); // a key's spelling never reaches it
                }
            }
        }

        for (var a = activeStart; a < this.enumerateActive.Count; a++)
        {
            var root = this.enumerateActive[a].Prefix;
            if (this.enumerateMemo.GetValueOrDefault((root, view, index.Serial)) is not { } prefix)
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
                        (AssociatedIdentityMatches(stated, subject) || this.SameClass(stated, subject)) &&
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

            // Row (4) reads only the supply's ancestors: instantiating its clauses here built Types that no row reads.
            var supply = new ContractSupply(this, shape, subject, view.Premises);
            for (var a = 0; a < supply.AncestorCount; a++)
            {
                Add(rows, supply.InstantiatedAncestor(a).Symbol);
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

    // R7-C2c2: the walk's identity: two Types of one class of the request's view (ClassKey).
    private bool SameClass(BoundType a, BoundType b)
        => this.enumerateClasses is { } classes && ReferenceEquals(this.ClassKey(a, classes), this.ClassKey(b, classes));

    // An entry of Closure(view) (the class header's): the serial it was formed at, whether it is Kept, and its buckets by (chain scope,
    // kind, SubjectKey). Its lists stay with it, capacity kept.
    private sealed class ViewIndex
    {
        private readonly List<List<(BoundConstraint Raw, BoundConstraint Fact)>> lists = new();
        private int used;

        internal int Serial { get; private set; }

        internal bool Kept { get; set; }

        internal bool Readings { get; private set; }

        internal Dictionary<(BindingScope Scope, ConstraintKind Kind, BindingSymbol? Symbol, BoundType Type), List<(BoundConstraint Raw, BoundConstraint Fact)>> Buckets { get; }
            = new();

        internal void Open(int serial)
        {
            this.Serial = serial;
            this.Kept = false;
            this.Readings = false;
            this.Buckets.Clear();
            this.used = 0;
        }

        // One fact of an environment of the view in its kind's row under the subject key of `at` (a projection's declaration and
        // receiver, else the Type; the fact's subject or its class key): `raw` as the environment holds it (its availability), `fact`
        // as the index reads it.
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

            list.Add((raw, fact));
            this.Readings |= !ReferenceEquals(raw, fact);
        }
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

        // The number of the declaration's ancestors (Enumerate's ancestor row reads them alone).
        public readonly int AncestorCount => this.Origin.Ancestors.Count;

        // The shape's declaration, whose ancestors are written in its own terms.
        private readonly BoundContract Origin => shape.Symbol.Declaration.BoundSymbol?.Contract ?? shape;

        public readonly ContractSupply GetEnumerator() => this;

        // Ancestor `index` of the declaration as `self` and the shape make it, the supply's one instantiation of an ancestor.
        public readonly BoundContract InstantiatedAncestor(int index)
            => binding.InstantiatedContract(this.Origin.Ancestors[index], scope, self, normalize: false, shape.Symbol).Contract!;

        public bool MoveNext()
        {
            for (; this.ancestor < this.AncestorCount; (this.ancestor, this.next) = (this.ancestor + 1, 0))
            {
                if (this.next == 0)
                {
                    var instantiated = this.ancestor < 0 ? shape : this.InstantiatedAncestor(this.ancestor);
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
