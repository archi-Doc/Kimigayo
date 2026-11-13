// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ContractSolver (PlanB §4.1; DECISIONS BU-29, R7-C2a): premise derivation and evidence availability (SPEC 8.3): what the premises supply
// for a proposition and its negation (Supply) under the request's view (ViewOf, the one derivation point); the Contract rows they
// supply for a subject (Enumerate) and their two readers: SPEC 8.3.2's refinement row over them (PremiseRefines, the row's one
// predicate: ProveConformance, SymbolicCapability, and the declaration queries of a parameter's indexing, iteration and arithmetic)
// and the candidate readers' rows (PremiseRows: name discovery, R7-C2c2 c2-3a); until C3 the eager derivation too
// (ExpandContractPremises, ExpandScopeContractPremises, AddContractPremises over a Contract premise's supply, ContractSupply, which
// retention's handed signatures read too) and the availability of a fact (AvailableConstraintFact).
// - Inputs: the view's scope chain (ConstraintEnvironments), derived facts' source Contracts, published source states (BU-59).
// - Outputs: Evidence (Positive, Negative; Invalid, temporary until C3: the chain's Invalid environment, the cause it names);
//   Enumerate's rows, the least set of Contracts `subject is R` closed under: (1) a direct Contract fact of the view stating
//   the subject or a Type of its identity class, as written and, under a parameter identity, as the view reads it (Reading); (2) a
//   Self-dependent clause of a row of a proper prefix of the subject's projection chain, Self being that prefix, whose stated subject
//   is the subject or of its class; (3) a Contract's own Self; (4) a row's ancestors. Rows (1) and (2) take the Contract at the
//   subject's Origin arguments (RowContract; SPEC 8.5.1, 8.5.4: application substitutes Origin atoms positionally). A row carries
//   whether it is available (R7-C2c2 c2-3a): its fact and its Contract are, for a clause or an ancestor its source row too; the
//   strongest of its derivations wins. Refinement reads the available rows (SPEC 8.3.1: only evidence proves); a candidate reader
//   reads every row (a lookup is not a proof: the use is judged), one per Contract: rows whose Contract references are one Type in
//   the request's classes are published as one non-Alias row, the closed spelling first, then Binding.TypeIdentity's Order, never
//   premise order. Whether two references are one Type is the identity judge's question (IdentityClasses.Same); ClassKey is the
//   supply's index of subjects only.
// - Invariants: innermost scope first, each environment in its order; an Invalid environment answers itself to Supply; availability
//   is asked per fact, lazily, in the caller's order. Enumerate is a leaf of the judgment graph: its walk compares interned Types
//   (FactStates, AssociatedIdentityMatches) and their class keys (SameClass: ClassKey reads a built table and interns) and calls no
//   judgment, so a request demands only while it prepares its view (ClassesOf, IndexNormalized); a demand made there is a request
//   over its own view, with its own classes (restored on return), and reads that view's written facts and keys alone; a request
//   (PremiseRefines, PremiseRows) entered from a walk is InternalInvariant.
//   Enumerate's request tables (memo, index, pools) live for one outermost request and are keyed by the view value (the scope and
//   its serial, taken once per request), so a scope re-parented within a request is another view; a preparation that moves its own
//   view is InternalInvariant. The walk reads the requirement Contract (ContractSupply's rewrites substitute
//   activeRequirementContract), which is constant over a request (its writers bind calls, reached from no normalization and no
//   walk): the request tables need no call context in their keys, and the kept rows, which outlive a request, key it (Kept rows,
//   below). The caller of Reading holds its view's serial: a loop that writes the chain only
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
//   eager supply's one level until c2-3b, c and d); a missing row only rejects.
// - Extension (R7-C2c2 c2-2; SPEC 8.2.3's grounds include the identity Constraints a Contract premise supplies at every depth): the
//   walk records the TypeIdentity clause instances of its available Contract rows; an outermost refinement request (PremiseRefines)
//   whose walk is not positive for its demanded Contract and recorded rows builds its own classes over the view's premises and
//   those rows (ExtendedClassesOf: request-local, the rows Provisional),
//   re-keys its subject and its view's facts and walks again while a round adds a row. Nothing computed under an extension is kept (no
//   kept row, no key column of the view's table), and a kept row answers only where it decides: a refinement among the rows, or rows
//   whose walk recorded no identity row, which no extension reaches; else the request walks and extends as if nothing were kept
//   (whether a walk extends depends on the Contract asked, the rows do not). So an answer is a function of (subject, view) (SPEC
//   8.3.3). A candidate reader (PremiseRows) demands no Contract, so it never extends: it reads the first walk's rows, kept or
//   fresh, also a function of (subject, view). The implementation's limit, not SPEC's rule: a member or an associated Type that only
//   an identity clause at depth gives its subject is not found (dm1), as on base, until R7-C2e2's Closure(scope) holds the view's
//   identity closure. At R7-C2e2 e2-1's merge,
//   index entries built under an extension stay request-owned, never admitted to the view's index. Only depth-1 requests extend: a
//   reading formed inside a preparation (a nested request) may differ from one formed through Supply, which joins note B's
//   Completion row (R7-C2e2 e2-3).
// - Prohibitions: no judgment, lookup, normalization or inference rule here but the request boundary's normalization of a fact, which
//   only Reading calls (temporary until R7-C2c2 c2-4 removes its binding completion); Enumerate's walk and what it calls never
//   reach ProveConformance, ProveConstraint, ResolveAssociated, NormalizeProof*, SubstituteIdentity or RequestCapability
//   (ArchitectureRulesTest); no mode or bool parameter; no answer kept across requests but a Complete reading and the kept rows,
//   each in the kept region alone (both below); no second scheduler; no read sets. From C3 (C1 §5), broken until then by the
//   eager derivation moved here: no Binding writes, syntax-triggered supply, DerivedFacts or fallback scan.
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
// Kept rows (DECISIONS R7-C2a1, the owner's O-206 (a)). Temporary: R7-C2e2's Closure(scope) holds a view's rows with its index and
// readings and deletes enumerateKept, its pool, KeepRows and the kept region's part of PremiseRefines and PremiseRows.
// - What and key: the rows of each link an outermost request walked, per (link, view scope, serial, requirement Contract), with
//   whether that walk recorded an identity row (c2-2: such rows answer only a refinement among them), and a Contract's head depth.
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
    private int readingFormations;

    // The premises a request reads: the scope its consumer holds. C3 adds the definition whose collection is active and the
    // inadmissible bindings here, in ViewOf (BU-29); a source's state and its check under way stay on the source (SourceStates, BU-59).
    private readonly record struct EvidenceView(BindingScope Premises);

    // One of Enumerate's rows: a Contract the premises supply for the subject and whether its derivation is available (the strongest
    // of its derivations'). Alias is the candidate readers' column, formed by PremiseRows alone: another spelling of a published row.
    private readonly record struct PremiseRow(BindingSymbol Symbol, bool Available, bool Alias = false);

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

    // The walk's stacks: the chain of the subject being walked and its active prefixes (Enumerate).
    private readonly List<BoundType> enumerateChain = new();
    private readonly List<(BoundType Prefix, int Reach)> enumerateActive = new();

    // The rows of `subject` in the view (the contract above): its chain is walked from the root up, each link once per request and
    // view; a link's rows are final before a taller link reads them, and a prefix stays active while its height plus its rows' head
    // depth reaches the link. A link demanded again while open supplies nothing (SPEC 8.3.3).
    private List<PremiseRow>? Enumerate(BoundType subject, EvidenceView view, int serial)
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
                        head = Math.Max(head, this.HeadDepth(rows[r].Symbol));
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

    private List<PremiseRow> EnumerateLink(BoundType subject, EvidenceView view, int serial, int activeStart)
    {
        List<PremiseRow> rows;
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

        if (IsContractSelf(subject) && subject.Symbol!.SelfOf is { } self)
        {
            Add(rows, new(self, this.AvailableContractPremise(self)));
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
                var states = this.FactStates(fact, subject, out _);
                if (states || this.SameClass(fact.Subject!, subject))
                {
                    var available = this.AvailableConstraintFact(environment, raw, out _) && this.AvailableContractPremise(fact.Contract!);
                    Add(rows, new(this.RowContract(fact, subject, states), available));
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
                if (prefix[i].Symbol.Contract is not { } shape)
                {
                    continue;
                }

                foreach (var (_, _, premise) in new ContractSupply(this, shape, root, view.Premises))
                {
                    // An identity row is an available row's clause: an unavailable Contract premise supplies no ground (SPEC 8.3.1).
                    if (premise is { Kind: ConstraintKind.TypeIdentity, Subject: { } left, RequiredType: { } right } && this.enumerateDepth == 1 &&
                        prefix[i].Available && !this.enumerateIdentities.Contains((left, right)))
                    {
                        this.enumerateIdentities.Add((left, right)); // c2-2: an identity row (SPEC 8.2.3 grounds), for the request's classes
                    }

                    if (premise is { Kind: ConstraintKind.Contract, Subject: { } stated, Contract: { } symbol } &&
                        ((AssociatedIdentityMatches(stated, subject) is var states && states) || this.SameClass(stated, subject)))
                    {
                        Add(rows, new(this.RowContract(premise, subject, states), prefix[i].Available && this.AvailableContractPremise(symbol)));
                    }
                }
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Symbol.Contract is not { } shape)
            {
                continue;
            }

            // Row (4) reads only the supply's ancestors: instantiating its clauses here built Types that no row reads.
            var supply = new ContractSupply(this, shape, subject, view.Premises);
            for (var a = 0; a < supply.AncestorCount; a++)
            {
                Add(rows, new(supply.InstantiatedAncestor(a).Symbol, rows[i].Available));
            }
        }

        return rows;

        // A row's availability is the strongest of its derivations'.
        static void Add(List<PremiseRow> list, PremiseRow row)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i].Symbol, row.Symbol))
                {
                    list[i] = new(row.Symbol, list[i].Available || row.Available);
                    return;
                }
            }

            list.Add(row);
        }
    }

    // A row's Contract from the constraint stating it, for rows (1) and (2) alike (R7-C2c2 c2-2, the owner's O-248 (b)): a use's Origin
    // arguments are applied to the Contract when the constraint states the subject as written (its universal family pattern, SPEC
    // 8.5.1; positionally, by binder, SPEC 8.5.4); a class match reads the constraint's own subject, so a key's spelling never reaches
    // the application.
    private BindingSymbol RowContract(BoundConstraint stating, BoundType subject, bool states)
        => this.AppliedAssociatedContract(stating, states ? subject : stating.Subject!);

    // R7-C2c2: the walk's identity: two Types of one class of the request's view (ClassKey).
    private bool SameClass(BoundType a, BoundType b)
        => this.enumerateClasses is { } classes && ReferenceEquals(this.ClassKey(a, classes), this.ClassKey(b, classes));

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
