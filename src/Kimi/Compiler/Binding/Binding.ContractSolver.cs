// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// ContractSolver (PlanB §4.1; DECISIONS BU-29, R7-C2a): premise derivation and evidence availability (SPEC 8.3): what the premises supply
// for a proposition and its negation (Supply) under the request's view (ViewOf, the one derivation point); the rows they supply
// for a subject (Enumerate) and their readers: SPEC 8.3.2's refinement row over them (PremiseRefines, the row's one
// predicate: ProveConformance, SymbolicCapability, and the declaration queries of a parameter's indexing, iteration and arithmetic),
// the first walk's rows (FirstRows, R7-C2c2 c2-3b: a value call's Callable Constraints, TryCallable and SelectValueCall; the bounds
// of a Callable premise and of a requirement call, AvailableCallableEffects and AvailableEffectBounds; SPEC 8.3.2's Exact assumption
// over the leaf rows for every atom and its negation, ExactAssumption, which Supply reads (c2-5b, c2-5b2); the callables a contract hands its body, retention's
// HandedSignatures, R7-C2c2 c2-3c) and, over them, the candidate readers' rows (PremiseRows: name discovery, R7-C2c2 c2-3a); until C3 the eager derivation
// too (ExpandContractPremises, ExpandScopeContractPremises, AddContractPremises over a Contract premise's supply, ContractSupply)
// and the availability of a fact (AvailableConstraintFact).
// - Inputs: the view's scope chain (ConstraintEnvironments), derived facts' source Contracts, published source states (BU-59).
// - Outputs: Evidence (Positive, Negative; Invalid, temporary until C3: the chain's Invalid environment, the cause it names);
//   Enumerate's rows, the least set of Contracts `subject is R` closed under: (1) a direct Contract fact of the view stating
//   the subject or a Type of its identity class, as written and, under a parameter identity, as the view reads it (Reading); (2) a
//   Self-dependent clause of a row of a proper prefix of the subject's projection chain, Self being that prefix, whose stated subject
//   is the subject or of its class; (3) a Contract's own Self; (4) a row's ancestors; (5) at each link, the rows of each projection
//   of its identity class in the request's classes, by that projection's own walk (PLAN G881). Rows (1) and (2) take the Contract at the
//   subject's Origin arguments (RowContract; SPEC 8.5.1, 8.5.4: application substitutes Origin atoms positionally). A row carries
//   whether it is available (R7-C2c2 c2-3a): its fact and its Contract are, for a clause or an ancestor its source row too; the
//   strongest of its derivations wins. A constraint of another kind with a reader (RowAtom; R7-C2c2 c2-3b: Callable), and the
//   negation of any atom (c2-5b, c2-5b2; SPEC 8.3.2 Exact assumption), gives a leaf row under (1) and (2), filed and matched by its
//   atom's subject, a negation in the Not row: its proposition (Clause, the negation as stated) and no Contract, so the closure (3), (4), head depths and
//   active prefixes read Contract rows alone. A leaf row is one per proposition and stating scope, never merged and never Alias:
//   SPEC 8.10's weakest receiver is each premise's, 8.11.4's bound list each fact's Statements', and merging by signature is the
//   Callable selection's rule. Every row carries its Source: the written fact its derivation starts from, as its environment holds it (Raw; none for a
//   clause instance or an ancestor), and the scope stating it (Stated; a clause instance's and an ancestor's are their Contract row's, a
//   Contract's own Self row has none). Refinement reads the available rows (SPEC 8.3.1: only evidence proves); a candidate reader
//   reads every row (a lookup is not a proof: the use is judged), one per Contract: rows whose Contract references are one Type in
//   the request's classes are published as one non-Alias row, the closed spelling first, then Binding.TypeIdentity's Order, never
//   premise order. Whether two references are one Type is the identity judge's question (IdentityClasses.Same); ClassKey is the
//   supply's index of subjects only.
// - Invariants: innermost scope first, each environment in its order; an Invalid environment answers itself to Supply; availability
//   is asked per fact, lazily, in the caller's order. Enumerate is a leaf of the judgment graph: its walk compares interned Types
//   (FactStates, AssociatedIdentityMatches) and their class keys (SameClass: ClassKey reads a built table and interns) and calls no
//   judgment, so a request demands only while it prepares its view (ClassesOf, IndexNormalized); a demand made there is a request
//   over its own view, with its own classes (restored on return), and reads that view's written facts and keys alone; a request
//   (PremiseRefines, FirstRows) entered from a walk is InternalInvariant.
//   Enumerate's request tables (memo, pools) live for one outermost request and are keyed by the view value (the scope and its
//   serial, taken once per request), so a scope re-parented within a request is another view; a preparation that moves its own
//   view is InternalInvariant. The walk reads the requirement Contract (ContractSupply's rewrites substitute
//   activeRequirementContract), which is constant over a request (its writers bind calls, reached from no normalization and no
//   walk): the request tables need no call context in their keys, and the view's index and the kept rows, which outlive a request,
//   key it (Closure(view) and Kept rows, below). The caller of Reading holds its view's serial: a loop that writes the chain only
//   through Reading (Supply's, IndexNormalized's) passes its last observation and a formation takes it again; a loop with other
//   writes (SymbolicCapability's judgments) takes one per fact. A write added to such a loop outside Reading breaks this.
//   Rows depend on neither the order of facts or clauses nor on which check asked first (SPEC 8.3.3). Neither does a row's Source
//   (R7-C2c2 c2-3b): it is the first available derivation's, else the first one's, in this order: the walk's steps (the
//   Contract's own Self, the facts, the prefixes' clause instances, the ancestors); the facts innermost scope first and, in one
//   environment, a fact on the subject as written before a class mate's or a reading's, then Binding.TypeIdentity's Order of the
//   facts' subjects (ViewIndex.Add keeps each row kind's bucket so); the subject's rows before its key's. Two facts of one
//   environment on one subject give two rows or are one fact. A Contract row's Source decides no verdict: its one reader is the
//   editor's evidence (AvailableEffectBounds). A leaf row's Source is its own fact's, never a choice (the row is one per
//   proposition and stating scope): its bound list (SPEC 8.11.4) is the union over the fact's Statements in that environment
//   (ConstraintEnvironment.States: the in-place one and each a writer recorded, several where one fact instantiates several
//   written premises), which AvailableCallableEffects reads; SelectValueCall reads the Source for a failed selection's related
//   locations.
// - Identity (R7-C2c2 c2-1; SPEC 8.2.3): a premise's supply reaches every parameter and projection of its identity class; a
//   constructed member's conformance is ResolveConformance's. That is the implementation's limit, not SPEC's rule: where it is
//   Unknown, a premise is not yet read as evidence for that member (SPEC 8.2.3), so valid programs such as d09c stay rejected, as on
//   base, until c2-4 (a constructed member's conformance with premises for its arguments). Each request keys its
//   subject, its view's facts and the walk's comparisons by its own view's classes (ClassKey, Binding.TypeIdentity's: a class's least
//   parameter or projection; an erroneous or incomplete class keys nothing), whatever the table's finality, which decides only kept
//   rows. The written subject is walked beside its key and both refine, so a key only adds rows; each link's projection mates
//   (row (5)) add the clause instances their own chains supply, which no index holds. A mate whose chain holds a link open in the
//   walk (a cycle through a projection, `T.(Cy2).X.(Mid).Next is T`, SPEC 8.2.3 Cycles) is not walked from there, so no chain is
//   ever walked through a link whose rows are not yet formed: it supplies its rows of the request's last round (none in the
//   first), and the request walks it from the top once its own chains are closed, then walks again until no link's rows differ
//   from its own last round's (EnumerateClosed, PLAN G881b). The links a round walks are decided by chains and classes, never by
//   rows, and each link's rows are monotone in the rows it reads, so the rounds rise from no rows to the least fixpoint of the
//   links' equations: a function of (link, view), whichever link a request entered first or spelled (SPEC 8.3.3: a fact that depends
//   on its own judgment supplies nothing; no coinduction), in at most the (link, row) pairs plus one rounds. A key never leaves the supply: no
//   reader receives it as a Type (on a class match AppliedAssociatedContract reads the fact's own subject). Not yet supplied: a
//   subject only a binding completes beyond Reading's one level (ProjectionSpecification; c2-4), a leaf row's fact (a Callable fact,
//   a negation) whose subject only its reading, not its class, makes the subject (IndexNormalized reads Contract facts; c2-4), and
//   the clause kinds without a leaf row at depth (the eager supply's one level until c2-3c and d). A missing row rejects, but where it
//   is the evidence against a written premise it accepts: an identity premise putting a constructed Type in the subject's class (the
//   proposition asks that Type's rows, which no clause instance states: nts2c) and a positive identity clause at depth, which is no
//   row (nts2c-rev); PLAN's open issue against c2-5c's constructed roots.
// - Extension (R7-C2c2 c2-2; SPEC 8.2.3's grounds include the identity Constraints a Contract premise supplies at every depth): the
//   walk records the TypeIdentity clause instances of its available Contract rows, but a deferred mate's walk (PLAN G881b: a
//   cycle's mate, walked from the top) records none, so an extension rests on the chains a request walks without a cycle, as
//   before G881b: a cycle's mate is deeper than the link it is identical with, and under a recursive Contract whose clause
//   identifies a deeper projection with a shallower one (`Self.(Mk).Next.(Mk).Up is Self`) each of its links supplies one more
//   identity, so rows recorded there would never end. The implementation's limit, not SPEC's rule: an identity a Contract supplies
//   only at a prefix of a cycle's mate joins no extension. An outermost refinement request (PremiseRefines)
//   whose walk is not positive for its demanded Contract and recorded rows builds its own classes over the view's premises and
//   those rows (ExtendedClassesOf: request-local, the rows Provisional),
//   re-keys its subject and its view's facts and walks again while a round adds a row, at most PremiseClosureLimit rounds (SPEC 8.3.3
//   Limits; the owner's O-321 (b)): a mate's chain may be deeper than the link it is identical with (row (5)), so under a path
//   equation to a deeper projection (`Self.(Pq).Up is Self.(Pq).Next.(Pq).Next.(Pq).Up`, allowed) each round supplies a deeper
//   identity and the rounds have no end of their own. R7-C2c2 c2-4b (DECISIONS R4.13; the owner's O-345 (b)): the extension is the
//   class's closure (ExtendClosed): from its root over the declaration's grounds (ConstraintEnvironment.Declares) and its own rows,
//   re-rooted to a lower key, its rounds counted from the root's first walk, so an extending request's rounds and verdict are a
//   function of (its root, Contract, the declaration's grounds), never of a use's supply or an earlier request; whether a request
//   extends is not: its first walk reads the view's table, which holds the supply of every use bound before it (a Type spelled
//   anywhere in the declaration, a body statement checked earlier), so whether the bound is reached can depend on spelling and on
//   the order of checks: a known limit against SPEC 8.3.3 (STATUS) until R7-C3 removes the syntax-triggered supply (the owner's
//   O-413 (a)). A request still unproven at the bound is refused (RefuseProof:
//   PremiseClosureLimit, a Resource record, SPEC 23.1.1, at the use whose check asked, else at the owner of the request's scope)
//   and the proof that asked rests on the refusal, so a judgment that fails on it is a resource failure, never Unknown by
//   exhaustion; a judgment other evidence proves stands, a request proven within the bound is proven, and a query made after the
//   pass has counted its failures (a lint, MIR, emission) keeps Unknown (PLAN G651 U2's outcome). Nothing computed under an
//   extension is kept (no
//   kept row, no key column of the view's table), and a kept row answers only where it decides: a refinement among the rows, or rows
//   whose walk recorded no identity row, which no extension reaches; else the request walks and extends as if nothing were kept
//   (whether a walk extends depends on the Contract asked, the rows do not). So an answer is a function of (subject, view) (SPEC
//   8.3.3). A candidate reader (PremiseRows) demands no Contract, so it never extends: it reads the first walk's rows, kept or
//   fresh, also a function of (subject, view). The implementation's limit, not SPEC's rule: a member or an associated Type that only
//   an identity clause at depth gives its subject is not found (dm1), as on base, until R7-C2e2's Closure(view) holds the view's
//   identity closure. Under an extension the view's entry is the request's (Closure(view) below). Only depth-1 requests extend: a
//   nested request reads written facts alone (a1's leaf rule), so its answer can be Unknown where a depth-1 one is Proven, never the
//   reverse. Its reach (R7-C2e2 e2-3b): no table kept here (no Complete reading reads a judgment), no kept path verdict (only a
//   Proven one, which a nested request answers alike, or Invalid is kept), but a capability work evaluated inside a nested request
//   can keep for its epoch an Unknown a depth-1 evaluation would prove (no program shows it; removal: R7-C2c2 c2-4's request by
//   identity class, which every depth reads alike).
// - Prohibitions: no judgment, lookup, normalization or inference rule here but the request boundary's normalization of a fact, which
//   only Reading calls (temporary until R7-C2c3 removes its binding completion), and of a leaf row's atom, which only the reader
//   normalizes (ExactAssumption: as the proposition's intake reads it, NormalizeProofConstraint);
//   Enumerate's walk and what it calls never
//   reach ProveConformance, ProveConstraint, ResolveAssociated, NormalizeProof*, ClassForm or RequestCapability
//   (ArchitectureRulesTest); no mode or bool parameter; no answer kept across requests but a Complete reading and the kept rows,
//   each in the kept region alone, and the view's index when Complete (all below); no second scheduler; no read sets. From C3
//   (C1 §5), broken until then by the eager derivation moved here: no Binding writes, syntax-triggered supply, DerivedFacts or
//   fallback scan.
//
// The view and its closure (DECISIONS R7-C2e1). ViewOf names the state of a scope's chain: the serial changes when an environment's
// Version, a scope's LinkVersion or the parent's serial does (each counted at its writer). Reading is the one caller of the request
// boundary's normalization of a fact (NormalizeProofConstraint): in the capability phase, with normalizingAssociated empty, it keeps
// the reading per (view scope, fact, requirement Contract) under the serial taken before the formation (the caller's, above), for
// one publication of the source states (forgotten at the next one's start, ForgetKeptTables; emptied by ResetCapabilities), when its
// derivation is Complete. Any other request forms its readings itself.
// - Completion (C1 §5's, without Resource until f): each function of the normalization gives its answer with one; its zero is
//   Provisional, a composite rule's answer is the meet of its parts', and only these members give Complete (W1-W8;
//   ArchitectureRulesTest): NormalizeProofConstraint, NormalizeProofType (W1); ClassForm (as MemberOf: Complete without an identity
//   premise in the view, else the view's table's W7; a re-entered build answers the Type itself, Provisional, and keeps nothing; forms
//   are kept by their table alone; R7-C2c2 c2-4b, whose deletion of SubstituteIdentity and IdentityPremise left W7 for W2 and W3);
//   ContractType, ContractPart, ContractTypeCore, ContractLength, ApplyContractEnvironment, SubstituteContractReference (W4);
//   ProjectionSpecification, ResolveAssociated(receiver), BuiltinArithmeticOutput's and FixedArrayIteratorType's gates (W5);
//   AvailableConstraintFact and AvailableContractPremise (W6: the meet of every source state read, a failed declaration context
//   Provisional); the identity classes' Completion (W7); ReadSourceState (W8, SourceStates': a published Proven or Refuted); and
//   an outermost walk, PremiseRefines' or FirstRows' (the meet of its availability reads, which admits its rows; R7-C2e2 e2-3a). Provisional is a
//   conservative superset of C1's: the derivation took a route not shown final in the publication (a source read Pending or in
//   Error; a projection route past its gate; the batched route; a Callable fact; StoredType's null). Complete is final within one
//   publication of the source states (SourceStates resets them at each), so every table kept here lives one publication.
// - Within a member an unthreaded call counts as Complete, so a member calls only these besides the members above, each pure or
//   final in the capability phase; each change to a member is reviewed against this list: the view's reads (HasTypeIdentity,
//   EnclosingContractSelf, a chain environment's sets), FactStates, NegateConstraint, InternConstraint, interning and Type rewriting
//   (TyCtxt.Rebuild, Intern, HeldContext, HeldType, InterpolationBound, WithContext, RentTypes, StoredType and SubstituteType where
//   they return a Type), the static gates (KimiLibraryCatalog.IsArithmeticContract, WitnessGroup), SubstituteStoredOrigins (declared
//   Origins), SelectConformance for a query by declaration (final after the InheritedConformances query, Binding.cs's
//   RegisterInheritedConformances), and the PrepareTypeStorage memo (per storageVersion); R7-C2c2 c2-4's MemberOf (ContractTypeCore meets its Completion, the
//   table's), UnconditionalBinding (declarations only; an unfinished or refused instance is its `pending`, met as Provisional),
//   AssociatedInPath and OperandContexts (pure reads).
// - PremiseRefines' answer, and the judges' MemberOf reads (ProveConformance, RequestCapability, JudgeConstraintAtom; a read may rest
//   on a Provisional identity table, W6, W7), reach their callers unthreaded, as Complete; PremiseRefines' walk's Completion decides
//   only what is kept here. No judgment answer carries Completion (R7-C2e2 e2-3b): the kept judgment answers are the capability
//   results, which live one epoch (BU-59's staging judges a work that read a Pending state again next round), and the paths' verdicts
//   (IsVerified, Invalid), which keep only decided verdicts and rest on published states that a later publication changes only
//   through a declaration failure, the failure's own record (Inputs outside the key, below). No Provisional table is known to grow
//   after the read within a publication.
//
// Closure(view) (DECISIONS R7-C2e2-1, R7-C2e2-2): a view's index of its facts by kind and subject is one table, an entry per (view
// scope, requirement Contract) formed at the view's serial and rebuilt in place when the serial moves, its buckets by (chain scope,
// kind, SubjectKey).
// - Rows: a kind has a row when a reader reads it by subject: each row kind (RowKinds: Contract, R7-C2c2 c2-3b's leaf kind
//   Callable and c2-5b2's Not, every negated atom; Enumerate's walk), TypeIdentity
//   (ValidateAssociatedOverlapAgreement, DisjointConformanceFacts) and Semantics (DisjointConformanceFacts). A row kind's row holds
//   each fact as written, under its atom's subject and its class key in the view (R7-C2c2 c2-1: the forming request's classes,
//   which are the view's), in the Sources' order (the Invariants above), and the Contract row,
//   under a parameter identity, each fact as the view reads it (Reading) too. The TypeIdentity and Semantics rows hold each fact as
//   written under its written subject alone, in its environment's order, with no class key and no reading: a bucket of theirs holds
//   the facts of one written subject key, so a reader that pairs by bucket pairs no other subject's fact.
// - Kept: an entry is Kept when its content is Complete: written facts in every phase (a function of the chain's environments, which
//   the serial counts), their class keys when the view has no identity premise or its identity table is Final (W7, the rule the kept
//   rows ask: a table that is not is rebuilt per read), readings where KeptRegion kept them (the one rule), so an entry whose keys
//   are not final or whose preparation formed a reading that was not kept stays owned by its outermost request and goes when that
//   returns. A Kept entry lasts its publication (forgotten in place at the next one's start, ForgetKeptTables; emptied by ClearClosure
//   in ResetCapabilities) and spares a later outermost request its preparation;
//   its readings are read only in KeptRegion, so outside it an outermost request forms its own (R7-M20) and the entry again. A
//   nested demand reads an entry's written facts and their keys alone (the leaf rule), so it reads any entry. An extension (R7-C2c2
//   c2-2) files the written facts again under its extended keys, which are the request's whatever its table's finality (R7-C2c2
//   c2-4b: a round with no rows builds the declaration's table, which can be Final and is not the view's), so the entry is then the
//   request's: a Kept entry is reopened as owned by the outermost request before a pair goes in, never written into, and a later
//   request forms it again (N3 of e2-1's merge review). Each walking round first drops the class-key pairs an earlier table filed
//   (the view's, a previous round's or a previous root's; ViewIndex.DropClassKeys), so the round reads its own table's keys alone.
// - Readers: a request (PremiseRefines, FirstRows) and a closure validator's read (BeginClosureRead to EndClosureRead), which is
//   outside any request and reads the TypeIdentity and Semantics rows. Whoever holds an entry starts no request, since a request's
//   return drops the entries it does not keep: a request entered from a walk or from a validator's read is InternalInvariant, so a
//   validator takes what it needs from the entry before it judges. An entry a validator's read forms is the read's: formed outside a
//   request, its row kinds' rows have no class key and no reading, so no request may read it, and it goes back to the pool when the
//   read ends. A Kept entry serves a read as it is.
// - Retained memory is the pool's high water: an entry keeps the largest buckets and lists any view gave it, flat across warm
//   rebinds. Temporary: the readings go when R7-C2c3 removes Reading's binding completion. A view whose identity table is not
//   Final (a derived identity fact whose sources are not all final in the publication, W6) has its entry formed per request.
//
// Kept rows (DECISIONS R7-C2a1, the owner's O-206 (a)), a column of the view's Kept entry in Closure(view) (R7-C2e2 e2-3a).
// - What and key: the rows of each link an outermost request walked, in the entry of its (view scope, requirement Contract) at the
//   entry's serial, per link, with whether that walk recorded an identity row or deferred a mate (c2-2, PLAN G881b: such rows answer
//   only a refinement among them); and, in a table of their own, a Contract's head depth. Rows are kept only in a Kept entry (both need final keys and every reading
//   kept), so a row lives no longer than its entry.
// - Where: the kept region (KeptRegion, R7-M20's rule, which Reading asks and both keepers, PremiseRefines and FirstRows, ask
//   through RowsRequest), an outermost request, a preparation that formed no reading, final keys (no identity premise, or a Final
//   identity table), and a Complete walk: every availability read it made was Complete (W6, W8). Nested demands never read or fill
//   the rows.
// - Not pinned by a row (located invariants; R7-C2e2 e2-3a's implementation review and its W-224 merge review): the admission (each
//   keeper's test of enumerateCompletion, PremiseRefines' and FirstRows') and W8's Provisional for a Pending state (ReadSourceState)
//   decide only what is kept, and only a walk inside a publication round can keep a row that differs from a fresh walk's: outside a
//   round a Pending state stays as it is until the next publication's start, which forgets the row. Such a row is stale from the
//   source's settling until that start: in the rest of its round, in the later rounds and in the checks that follow the rounds.
//   What was measured, by mutation:
//   - PremiseRefines' admission (`admit`: rows kept whatever the walk's Completion) and W8 (`w8`: an Unknown read Complete), on a
//     base before FirstRows: 48 programs (the 16 coverage rows, Hello, Milestone26; cap6, cap6n, cap3b, cap9, cap10, n3b, n3c, n3d
//     and cyc1 in both orders; twelve probes) and the Unit's classes: no record and no test differs under either mutation. The
//     difference exists (under `admit`, ClosedConformancePrerequisiteBindingTest's cap10 with X before R answers `T is Copy` from
//     the stale rows after R settled; n3b likewise in both orders), but no record moves: Supply's one-level eager derivation answers
//     the same proposition. That shadow covers PremiseRefines' answers alone; the derivation goes at C3, and from then on the
//     admission alone keeps such rows out. Under `w8` no admission changes: a walk that reads a Contract Pending also reads the
//     derived facts the eager derivation filed from it, not found and so Provisional by W6.
//   - FirstRows' admission (`nofirstadmit`: its rows kept whatever the walk's Completion): the merge review's six twins (d3a, d3ar,
//     d3av, d3b, d3br, d3bv) and the refresh's 56 acceptance programs: no difference, and the admission unobserved: the invalid
//     twins never reach a Provisional FirstRows walk (an environment naming a failed Contract is Invalid, and the walk skips it).
//     FirstRows' readers (ExactAssumption, the Callable and effect-bound readers, HandedSignatures) read `Available` rows directly,
//     with no shadow, so a Pending read inside a round by one of them is the case left open; its row is owed with C3's.
// - Lifetime: the entry's, the publication, in the capability phase (KeepsAcrossRequests, the one phase fact); before it one
//   outermost request (a Contract's clauses are still being bound, so not even a head depth outlives it).
// - Inputs outside the key, and what covers a change to each inside a publication: Contract clause storage (HeadDepth,
//   ContractSupply): bound before the phase, none. An environment's fact sets and its Invalid: the serial. A published source state:
//   a Complete walk read only Proven or Refuted ones, final in the publication. A Contract's declaration context (and an ancestor's
//   or an enclosing declaration's), which validations fail between publications (ValidateConstraintEnvironments after the phase's
//   first publication; ValidateCopyDeclarations, ValidateApiAccess, ValidateBaseDeclarations and ValidateProperties before the
//   after-bodies one; InvalidateProjectionCertificates' validations before its loop's): a valid context only fails, and (i) the
//   failure's own record rejects the program, so an answer kept across it differs only in a rejected program's dependent records
//   (Q13, record selection); (ii) where the failed Contract is named by an environment, that environment goes Invalid, another
//   serial (not shown for an ancestor's or an enclosing declaration's failure: (i) carries the argument). The same holds for every
//   table kept for the publication (readings, entries, identity tables).
// - Not replayed: a kept answer skips the walk's per-check effects, which a capability result replays from its payload. A Complete
//   walk read only Proven or Refuted states and valid contexts, so two are left, both records only (Q13): a read outside every
//   proof scope opens and closes its own frame (ReadSourceState at proofNesting 0), and a context failed after the walk would
//   have been consulted (SPEC 23.1.2), the dependent record of (i) above.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The kinds whose constraints give a row, the one list the index and the walk's two sources read (RowAtom; each kind its own row
    // of Closure(view), R7-C2e2-2, a fact in its own kind's: ViewIndex.Add): Contract, each kind with a reader of its own, a leaf kind
    // (R7-C2c2 c2-3b: Callable), and Not, every negated atom (c2-5b, c2-5b2), filed by its atom's subject (one row per meaning, which
    // the walk reads and no validator). A later kind with a reader joins the list here alone.
    private static readonly ConstraintKind[] RowKinds = [ConstraintKind.Contract, ConstraintKind.Callable, ConstraintKind.Not, ConstraintKind.Error]; // Error: an unbound clause's failed fact (R7-C3)

    // The closure's kept readings (Reading), emptied at each publication's start in the capability phase (ForgetKeptTables) and each
    // pass (ResetCapabilities), capacity kept.
    private readonly Dictionary<(BindingScope View, BoundConstraint Fact, BindingSymbol? Requirement), (int Serial, BoundConstraint Reading)> readings = new();

    private BoundConstraint[] contractFactScratch = [];
    private int readingFormations;
    private int readingStores;

    // The premises a request reads: the scope its consumer holds. C3 adds the definition whose collection is active and the
    // inadmissible bindings here, in ViewOf (BU-29); a source's state and its check under way stay on the source (SourceStates, BU-59).
    internal readonly record struct EvidenceView(BindingScope Premises);

    // One of Enumerate's rows: a Contract the premises supply for the subject (Symbol), or the proposition of another kind they
    // supply for it (Clause, a leaf row: Symbol is null), and whether its derivation is available (the strongest of its
    // derivations'). Raw and Stated are its Source (the class header's Outputs and Invariants). Alias is the candidate readers'
    // column, formed by PremiseRows alone: another spelling of a published row. Internal as ViewIndex is (a pass part's entries hold
    // their kept rows, ViewIndex.Rows).
    internal readonly record struct PremiseRow(BindingSymbol? Symbol, bool Available)
    {
        internal bool Alias { get; init; }

        internal BoundConstraint? Clause { get; init; }

        internal BoundConstraint? Raw { get; init; }

        internal BindingScope? Stated { get; init; }
    }

    // Supply's answer: whether the valid premises supply the proposition (Positive) and its negation (Negative).
    private readonly struct Evidence
    {
        internal bool Positive { get; init; }

        internal bool Negative { get; init; }
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

    // The atom by whose subject a constraint gives a row, else none: a constraint of one of RowKinds (a Contract one naming its
    // Contract), or the operand of a negation that is an atom of any kind, no conjunction, disjunction or Unresolved (R7-C2c2 c2-5b,
    // c2-5b2; SPEC 8.3.2 Exact assumption and Negation: the negation is a Not row, its Clause; an operand is never a negation,
    // NegateConstraint folding `not not P`).
    private static BoundConstraint? RowAtom(BoundConstraint constraint)
    {
        if (constraint.Kind == ConstraintKind.Not)
        {
            return constraint.Left!.Kind is ConstraintKind.And or ConstraintKind.Or or ConstraintKind.Unresolved ? null : constraint.Left;
        }

        foreach (var kind in RowKinds)
        {
            if (constraint.Kind == kind)
            {
                return kind != ConstraintKind.Contract || constraint.Contract is not null ? constraint : null;
            }
        }

        return null;
    }

    // A row joins `list` from `start`: one row per Contract reference, and per proposition and stating scope for a leaf row. A row
    // derived again keeps the strongest availability with that derivation's Source, the earlier derivation's where they are equal
    // (the class header's Invariants give the order).
    private static void AddRow(List<PremiseRow> list, int start, PremiseRow row)
    {
        for (var i = start; i < list.Count; i++)
        {
            var same = list[i];
            if (ReferenceEquals(same.Symbol, row.Symbol) && ReferenceEquals(same.Clause, row.Clause) &&
                (row.Clause is null || ReferenceEquals(same.Stated, row.Stated)))
            {
                if (row.Available && !same.Available)
                {
                    list[i] = row;
                }

                return;
            }
        }

        list.Add(row);
    }

    // A link's rows as EnumerateClosed's stop test reads them: its rows, an available one twice. A link's rows and their
    // availability only grow from round to round, so equal weights are equal rows up to their Sources (a leaf row's Stated is its
    // prefix row's Source, the first available derivation's, which a later round may change at equal weight; it decides no verdict).
    private static int RowWeight(List<PremiseRow>? rows)
    {
        var weight = 0;
        for (var r = 0; rows is not null && r < rows.Count; r++)
        {
            weight += rows[r].Available ? 2 : 1;
        }

        return weight;
    }

    // O28 (PLAN G949 S1-1, E7): every scope of the pass's modules, and the scopes of the pass view's base paths (a path's, its
    // requirements' premises and its overlaps'), denote their current view, so a later pass's read of one of their view serials writes
    // nothing.
    private void SettleViewSerials()
    {
        foreach (var scope in this.scopes.Values.In(this.pass))
        {
            SerialOf(scope);
        }

        for (var p = 0; p < this.impls.InPass.Paths.Count; p++)
        {
            var path = this.impls.InPass.Paths[p];
            SerialOf(path.Scope);
            for (var i = 0; path.WitnessScopes is { } witnesses && i < witnesses.Count; i++)
            {
                SerialOf(witnesses[i].Premises);
            }

            for (var i = 0; path.OverlapScopes is { } overlaps && i < overlaps.Count; i++)
            {
                SerialOf(overlaps[i].Scope);
            }
        }
    }

    private EvidenceView ViewOf(BindingScope scope) => new(scope);

    // The kept tables' one phase fact: whether a table may outlive an outermost request. The capability phase (capabilitiesReady,
    // raised once per pass after every Contract clause is bound) keeps readings, entries with their rows, identity tables and head
    // depths for one publication of the source states: PublishSourceStates asks this fact and forgets them in place at each
    // publication's start (ForgetKeptTables). Before the phase no reading, row or head depth outlives an outermost request (the
    // class header's Kept rows); what else was kept before it (a Kept entry's written facts and keys, a Complete identity table)
    // the phase's first publication forgets.
    private bool KeepsAcrossRequests => this.capabilitiesReady;

    // R7-M20's kept region, the one rule every kept table asks (Reading's readings, PremiseRefines' rows): a table may outlive the
    // request, and no associated normalization is under way (its context is in no key; a ClassForm starts no request, R7-C2c2 c2-4b).
    private bool KeptRegion => this.KeepsAcrossRequests && this.normalizingAssociated.Count == 0;

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
        var typeIdentity = HasTypeIdentity(view.Premises);
        var negation = this.NegateConstraint(proposition);
        int? serial = null; // The loop writes nothing but through Reading.
        for (var current = view.Premises; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            positive |= this.AvailableConstraintFact(environment, proposition, out _);
            negative |= this.AvailableConstraintFact(environment, negation, out _);
            if (environment.HasAssociatedProjection || typeIdentity)
            {
                foreach (var fact in environment.Facts)
                {
                    // The consult follows the relevance test (F's C2 ruling 273): only a fact read as the proposition or its negation.
                    var identity = this.Reading(fact, view.Premises, ref serial);
                    var (states, refutes) = (this.SameProofPremise(identity, proposition), this.SameProofPremise(identity, negation));
                    if ((states || refutes) && this.AvailableConstraintFact(environment, fact, out _))
                    {
                        positive |= states;
                        negative |= refutes;
                    }
                }
            }
        }

        // R7-C2c2 c2-3b, c2-5b, c2-5b2: an atom's premise evidence at every depth, for the proposition and its negation, is the
        // subject's leaf rows (SPEC 8.3.2 Exact assumption: a clause at every depth, whatever its spelling).
        var rows = this.ExactAssumption(proposition, negation, view.Premises);
        return new() { Positive = positive || rows.Positive, Negative = negative || rows.Negative };
    }

    // R7-C3 (F's C2 ruling 278; SPEC 8.3.2 "evidence for both polarities ... is contradictory evidence", 8.2.3 "never again at uses"):
    // the one rule at the judges' exits (ProveProposition, ProveConformance) for a normalized proposition and the verdict of its valid
    // facts. A Failed premise of the view speaks to the proposition it would decide: read in the view, the proposition or its negation
    // (its polarity); an identity premise of the same subject (identical through the classes, a constructed Type included), which
    // would decide every proposition about it; or an admitted-set premise on the binder of a pair layer of the subject (SPEC 8.3.4,
    // F's C2 ruling 281: `s is obj` decides `s/A`). The last two give no verdict of their own. Unless the valid facts give the verdict
    // the speaking premises give, the answer is Error resting on the declaration's record; where none speaks, the valid facts decide.
    // The Contract judge's exit: the proposition is interned only in a view that holds a Failed premise. Both answer the verdict at once
    // while nothing failed (premiseFailures, the Failed state's census).
    private ConstraintProof SpokenConformance(BoundType type, BindingSymbol contract, BindingScope scope, ConstraintProof verdict)
    {
        if (this.premiseFailures == 0)
        {
            return verdict;
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { FailedPremise: not null })
            {
                return this.SpokenVerdict(this.InternConstraint(new(ConstraintKind.Contract, type, contract: contract)), scope, verdict);
            }
        }

        return verdict;
    }

    private ConstraintProof SpokenVerdict(BoundConstraint proposition, BindingScope scope, ConstraintProof verdict)
    {
        if (verdict == ConstraintProof.Error || this.premiseFailures == 0)
        {
            return verdict;
        }

        var view = this.ViewOf(scope).Premises;
        var (positive, negative, subject) = (false, false, ClauseSubject(proposition));
        BoundConstraint? negation = null;
        PremiseFailure? failure = null;
        int? serial = null;
        for (var current = view; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { FailedPremise: not null } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (!environment.IsFailed(fact))
                {
                    continue;
                }

                var reading = this.Reading(fact, view, ref serial);
                var states = this.SameProofPremise(reading, proposition);
                var refutes = this.SameProofPremise(reading, negation ??= this.NegateConstraint(proposition));
                if (states || refutes || (subject is not null && ((reading.Kind == ConstraintKind.TypeIdentity && ReferenceEquals(reading.Subject, subject)) || DecidesPairLayer(reading, subject))))
                {
                    (positive, negative) = (positive | states | !refutes, negative | refutes | !states);
                    failure ??= environment.CauseOf(fact);
                }
            }
        }

        var spoken = positive ? (negative ? ConstraintProof.Error : ConstraintProof.Proven) : ConstraintProof.Refuted;
        return failure is not null && verdict != spoken ? this.RestOnFailure(failure) : verdict;

        // An admitted-set premise (SemanticsPremise) on the binder of a pair layer of `type` or of one of its parts.
        static bool DecidesPairLayer(BoundConstraint premise, BoundType type)
        {
            if (TryPairLayer(type, out var whole, out _) && SemanticsPremise(premise, whole))
            {
                return true;
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (DecidesPairLayer(premise, type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // The walk's stacks: the chain of the subject being walked, its active prefixes and its links' class mates (Enumerate).
    private readonly List<BoundType> enumerateChain = new();
    private readonly List<(BoundType Prefix, int Reach)> enumerateActive = new();
    private readonly List<BoundType> enumerateMates = new();

    // The rows of `subject` in the view (the contract above): its chain is walked from the root up, each link once per request and
    // view and round; a link's rows are closed before a taller link reads them, and a prefix stays active while its height plus its
    // rows' head depth reaches the link. No chain holding an open link is walked (row (5) defers such a mate), so a link is never
    // demanded while open. A link that closes with rows that are not its last round's is noted (EnumerateClosed's stop test).
    private List<PremiseRow>? Enumerate(BoundType subject, EvidenceView view, ViewIndex index)
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
                    this.enumerateMemo[(link, view, serial)] = null; // Open: row (5) walks no chain holding it.
                    rows = this.EnumerateLink(link, view, index, activeStart);
                    this.enumerateMemo[(link, view, serial)] = rows;
                    this.enumerateMoved |= this.enumerateSeed.Count == 0 ? rows.Count != 0 : RowWeight(rows) != RowWeight(this.enumerateSeed.GetValueOrDefault((link, view, serial)));
                }

                if (rows is { Count: > 0 })
                {
                    var head = 0;
                    for (var r = 0; r < rows.Count; r++)
                    {
                        head = rows[r].Symbol is { } contract ? Math.Max(head, this.HeadDepth(contract)) : head; // A leaf row has no clause.
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

    private List<PremiseRow> EnumerateLink(BoundType subject, EvidenceView view, ViewIndex index, int activeStart)
    {
        List<PremiseRow> rows;
        if (this.enumeratePoolNext < this.enumeratePool.Count)
        {
            rows = this.enumeratePool[this.enumeratePoolNext++]; // empty: the pool's lists are emptied when their request returns (LeaveRows)
        }
        else
        {
            rows = new();
            this.enumeratePool.Add(rows);
            this.enumeratePoolNext++;
        }

        if (IsContractSelf(subject) && subject.Symbol!.SelfOf is { } self)
        {
            AddRow(rows, 0, new(self, this.WalkAvailable(this.AvailableContractPremise(self, out var selfRead), selfRead)));
        }

        for (var current = view.Premises; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            // A fact is indexed in its kind's row by subject key, as written and as normalized in the view: FactStates confirms a
            // match. A nested demand reads the written facts alone (the leaf rule), so an entry an outermost request prepared serves it
            // too. Each is a row with its Source: a Contract fact its Contract's, a fact of another row kind (RowKinds: a leaf kind's, a
            // negation's) a leaf row, matched by its atom (RowAtom).
            foreach (var kind in RowKinds)
            {
                if (index.Buckets.GetValueOrDefault((current, kind, SubjectKey(subject).Symbol, SubjectKey(subject).Type)) is not { } facts)
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

                    var atom = RowAtom(fact)!; // a row kind's bucket holds facts with a row atom alone (IndexWritten, IndexNormalized)
                    var states = this.FactStates(atom, subject, out _);
                    if (states || this.SameClass(atom.Subject!, subject))
                    {
                        var contract = fact.Kind == ConstraintKind.Contract;
                        var available = this.WalkAvailable(this.AvailableConstraintFact(environment, raw, out var factRead), factRead) &&
                            (!contract || this.WalkAvailable(this.AvailableContractPremise(fact.Contract!, out var contractRead), contractRead));
                        PremiseRow row = contract
                            ? new(this.RowContract(fact, subject, states), available) { Raw = raw, Stated = current }
                            : new(null, available) { Clause = fact, Raw = raw, Stated = current };
                        AddRow(rows, 0, row);
                    }
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
                if (prefix[i].Symbol?.Contract is not { } shape)
                {
                    continue;
                }

                foreach (var (_, _, premise, _) in new ContractSupply(this, shape, root, view.Premises))
                {
                    // An identity row is an available row's clause: an unavailable Contract premise supplies no ground (SPEC 8.3.1). A
                    // deferred mate's walk keeps none (EnumerateClosed; the class header's Extension).
                    if (premise is { Kind: ConstraintKind.TypeIdentity, Subject: { } left, RequiredType: { } right } && this.enumerateDepth == 1 &&
                        prefix[i].Available && !this.enumerateIdentities.Contains((left, right)))
                    {
                        this.enumerateIdentities.Add((left, right)); // c2-2: an identity row (SPEC 8.2.3 grounds), for the request's classes
                    }

                    // A clause instance stating the subject or a Type of its class is a row whose Source is its prefix row's scope; a
                    // negated clause states its atom's subject (RowAtom).
                    if (premise is not null && RowAtom(premise) is { Subject: { } stated } &&
                        ((AssociatedIdentityMatches(stated, subject) is var states && states) || this.SameClass(stated, subject)))
                    {
                        var contract = premise.Kind == ConstraintKind.Contract;
                        var available = prefix[i].Available && (!contract || this.WalkAvailable(this.AvailableContractPremise(premise.Contract!, out var premiseRead), premiseRead));
                        PremiseRow row = contract
                            ? new(this.RowContract(premise, subject, states), available) { Stated = prefix[i].Stated }
                            : new(null, available) { Clause = premise, Stated = prefix[i].Stated };
                        AddRow(rows, 0, row);
                    }
                }
            }
        }

        if (this.enumerateClasses is { } classes && subject.Kind is BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection)
        {
            this.EnumerateMates(subject, classes, view, index, rows); // Row (5).
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Symbol?.Contract is not { } shape)
            {
                continue;
            }

            // Row (4) reads only the supply's ancestors: instantiating its clauses here built Types that no row reads.
            var supply = new ContractSupply(this, shape, subject, view.Premises);
            for (var a = 0; a < supply.AncestorCount; a++)
            {
                AddRow(rows, 0, new(supply.InstantiatedAncestor(a).Symbol, rows[i].Available) { Stated = rows[i].Stated });
            }
        }

        return rows;
    }

    // Row (5) (PLAN G881; SPEC 8.2.3): the link is one Type with each member of its class in the request's classes, so their rows
    // are the link's. The class-key index gives the link every member's facts where the link or its key is walked; what no index
    // holds is a projection member's rows (2), the clause instances of its own chain's prefixes (`U is T.(A).Next.(B).Next`: T's
    // chain supplies `T.(A).Next.(B).Next is C`), so each projection mate's walk adds its rows, in Order, never premise order. A
    // mate whose chain holds a link open in this walk (a cycle, `T.(Cy2).X.(Mid).Next is T`; SPEC 8.2.3 Cycles) is not walked from
    // here: it would read the open link as empty and leave that partial answer in the memo. It is deferred: it adds its rows of
    // the request's last round, and EnumerateClosed walks it once the request's chains are closed and walks again until no link's
    // rows move (the least fixpoint, PLAN G881b). A constructed link (`Leaf` under `T.(Holder).Item is Leaf`) walks no mates of its
    // own: R7-C2c2 c2-4 keys it to its class's Representative, so the class's rows, the mates' included, are its rows, and its closed
    // world meets them by PremiseJoin, so a premise contradicting it is contradictory evidence (d09y).
    // Its own member, so EnumerateLink's body, walked for every link, holds no try region.
    private void EnumerateMates(BoundType subject, IdentityClasses classes, EvidenceView view, ViewIndex index, List<PremiseRow> rows)
    {
        var mates = this.enumerateMates.Count;
        classes.ProjectionMembers(subject, this.enumerateMates);
        var end = this.enumerateMates.Count; // A nested walk's mates are above it and gone when it returns.
        try
        {
            for (var m = mates; m < end; m++)
            {
                var mate = this.enumerateMates[m];
                if (ReferenceEquals(mate, subject))
                {
                    continue;
                }

                List<PremiseRow>? mateRows;
                if (this.HoldsOpenLink(mate, view, index.Serial))
                {
                    if (!this.enumerateDeferred.Contains(mate))
                    {
                        this.enumerateDeferred.Add(mate);
                    }

                    mateRows = this.enumerateSeed.GetValueOrDefault((mate, view, index.Serial));
                }
                else
                {
                    this.IdentityMateWork++;
                    mateRows = this.Enumerate(mate, view, index);
                }

                for (var r = 0; mateRows is not null && r < mateRows.Count; r++)
                {
                    AddRow(rows, 0, mateRows[r]);
                }
            }
        }
        finally
        {
            this.enumerateMates.RemoveRange(mates, this.enumerateMates.Count - mates);
        }
    }

    // A row's Contract from the constraint stating it, for rows (1) and (2) alike (R7-C2c2 c2-2, the owner's O-248 (b)): a use's Origin
    // arguments are applied to the Contract when the constraint states the subject as written (its universal family pattern, SPEC
    // 8.5.1; positionally, by binder, SPEC 8.5.4); a class match reads the constraint's own subject, so a key's spelling never reaches
    // the application.
    private BindingSymbol RowContract(BoundConstraint stating, BoundType subject, bool states)
        => this.AppliedAssociatedContract(stating, states ? subject : stating.Subject!);

    // Whether a link of `type`'s chain is open in this walk (Row (5)'s guard).
    private bool HoldsOpenLink(BoundType type, EvidenceView view, int serial)
    {
        for (var link = type; ; link = link.Components[0])
        {
            if (this.enumerateMemo.TryGetValue((link, view, serial), out var rows) && rows is null)
            {
                return true;
            }

            if (link.Kind != BoundTypeKind.AssociatedProjection)
            {
                return false;
            }
        }
    }

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
    // declaration context and its published source state (Binding.SourceStates). W6: the meet of the states it read (W8); a failed or
    // unresolved context is Provisional (its read consults the failure, which a kept answer would not replay).
    private bool AvailableContractPremise(BindingSymbol contract, out Completion completion)
    {
        completion = Completion.Complete;
        var ancestors = contract.Contract?.Ancestors;
        for (var i = -1; i < (ancestors?.Count ?? 0); i++)
        {
            var source = i < 0 ? contract : ancestors![i];
            if (this.InvalidDeclarationContext(source.Declaration) || UnresolvedTypeDeclarationContext(source.Declaration))
            {
                completion = Completion.Provisional;
                return false;
            }

            var state = this.ReadSourceState(source, QuerySite.Nested, out var read); // The solver answers inside the proof that asks it.
            completion = Meet(completion, read);
            if (state != ConstraintProof.Proven)
            {
                // R7-C3 C3-1b (F's C2 rulings 289-290; SPEC 8.3.1, 23.1.2): a Refuted source is an invalid declaration, read outside its
                // own check as a failed input: the proof's limit, on the cause SourceStates published (ReadVerdict joins none for
                // Refuted; an Error's it joins itself). The source's own checks are the failure's sites (ChecksOwnSite).
                if (state == ConstraintProof.Refuted && !this.ChecksOwnSite(source.Declaration))
                {
                    this.ScopeReads.Limit ??= ((DeclarationContainerKoto)source.Declaration).BoundSymbol!.SourceCause; // Published (RefutedCause).
                }

                return false;
            }
        }

        return true;
    }

    // R7-C3 (F's C2 rulings 273, 282): a premise derived from a Failed premise is Failed: it is that premise, its origin clause, whose
    // state the origin's environment holds (FailedClauseRead), so its cause is the origin declaration's record. No state is copied.
    private PremiseFailure? DerivedFailure(ConstraintEnvironment environment, BoundConstraint fact)
    {
        if (this.premiseFailures == 0)
        {
            return null; // Nothing failed (the Failed state's census).
        }

        for (var i = 0; i < environment.DerivedFacts.Count; i++)
        {
            if (environment.DerivedFacts[i] is { Origin: { BoundConstraint: { } origin } clause } derived && ReferenceEquals(derived.Fact, fact) &&
                this.FailedClauseRead(clause, origin) is { } failure)
            {
                return failure;
            }
        }

        return null;
    }

    // An availability read of the outermost walk: its Completion joins the walk's (the class header's Kept rows).
    private bool WalkAvailable(bool available, Completion completion)
    {
        this.enumerateCompletion = Meet(this.enumerateCompletion, completion);
        return available;
    }

    // W6: Complete when the fact is not in the environment or is direct; a derived fact found available is the meet of every source it
    // read (R7-M20's composite rule), and one not found is Provisional (a Pending source may still supply it).
    // R7-C3 (F's C2 rulings 272-273, 276): the environment's one premise consult. A failed premise is no evidence, and the read is the
    // proof's limit (ScopeReads.Limit, its PremiseFailure; SPEC 23.1.2): an answer the valid facts leave open (Unknown) rests on it where
    // it is no longer combined (EndProof), and one they prove or refute holds or keeps its own record.
    private bool AvailableConstraintFact(ConstraintEnvironment environment, BoundConstraint fact, out Completion completion)
    {
        completion = Completion.Complete;
        if (!environment.Facts.Contains(fact))
        {
            return false;
        }

        if (environment.IsFailed(fact))
        {
            this.ScopeReads.Limit ??= environment.CauseOf(fact);
            return false;
        }

        if (environment.DirectFacts.Contains(fact))
        {
            return true;
        }

        if (this.DerivedFailure(environment, fact) is { } failure)
        {
            this.ScopeReads.Limit ??= failure;
            return false;
        }

        var read = Completion.Complete;
        for (var i = 0; i < environment.DerivedFacts.Count; i++)
        {
            var derived = environment.DerivedFacts[i];
            if (ReferenceEquals(derived.Fact, fact))
            {
                var available = this.AvailableContractPremise(derived.Source, out var source);
                read = Meet(read, source);
                if (available)
                {
                    completion = read;
                    return true;
                }
            }
        }

        completion = Completion.Provisional;
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

        environment.MarkDeclared(); // R7-C2c2 c2-4b: what follows is a use's supply (ConstraintEnvironment.Declares)
    }

    private void AddContractPremises(BoundContract shape, BoundType self, BindingScope scope)
    {
        var environment = scope.Constraints ??= new();
        foreach (var (ancestor, declaration, premise, clause) in new ContractSupply(this, shape, self, scope))
        {
            if (premise is not null)
            {
                this.AddConstraintFact(environment, premise, shape.Symbol, clause);
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
    // (ContractConstraint, not normalized), evaluated when reached. Read by AddContractPremises and Enumerate's walk.
    private ref struct ContractSupply(Binding binding, BoundContract shape, BoundType self, BindingScope scope)
    {
        private int ancestor = -1;
        private int next; // The declaration's next item: 0 the declaration itself, i + 1 its clause i.

        // Clause: the clause Premise instantiates (its origin, a premise of the declaration's environment; R7-C3, ruling 282).
        public (int Ancestor, BoundContract Declaration, BoundConstraint? Premise, IsKoto? Clause) Current { get; private set; }

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
                    (this.next, this.Current) = (1, (this.ancestor, instantiated, null, null));
                    return true;
                }

                var declaration = this.Current.Declaration;
                for (; this.next <= declaration.ClauseStorage.Count; this.next++)
                {
                    if (declaration.ClauseStorage[this.next - 1].BoundConstraint is { } constraint &&
                        binding.DependentConstraint(constraint, contractSelf: true))
                    {
                        this.Current = (this.ancestor, declaration, binding.ContractConstraint(constraint, scope, self, false, declaration.Symbol), declaration.ClauseStorage[this.next - 1]);
                        this.next++;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
