# G170 C1: executable design of C (ContractSolver premise supply)

Unit C1 of [PlanB](../../../Proposals/2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) (BU planning session,
2026-10-21), after [C0](../C0-evidence.md). Draft data only: no production, test-project, SPEC, DECISIONS or PLAN change. Branch
`claude/bu-g170-c1` from origin/dev `e0f24c83` (Binding and milestones unchanged at `86a0fed8`). Parallel sessions loaded the machine.

[proto/](proto/): model, identity classes (`EGraph.cs`), C (`Solver.cs`), exhaustive bounded reference, runner (`dotnet run -c Release --
fixed|random [n] [seed]|order|resource|cost`); [adapter/](adapter/G170C1AdapterTest.cs): compiled with the four prototype files into a
scratch test assembly (`G170_C1_OUT=<file> [G170_C1_CORPUS=tests/milestones] xUnitTest.exe -method …Probe`); [results/](results/).

## 0. Verdict per completion condition

1. **Rule selection** (§1): every SPEC 8.3.2 supply row is selected by e-matching its conclusion against identity classes; the
   argument is complete except for termination, which ends in Resource. A prefix selector is B (§3's counterexamples, fixed).
2. **Agreement** (§2): 81,397 stable Supply queries on 4,000 generated models, 0 disagreements; 7,204 enumerations and 2,937
   frozen-view read sets, 0 disagreements; Resource on 320 stable queries (0.39%), all over recursive Contracts.
3. **Real-source witnesses** (§3): G170 at every depth whatever the spelling, the non-recursive variant, G180's probes, frozen-view
   exclusion and completed external values with their reads; the corpus has no Resource and matches `ProveCopy` on 99 parameters.
4. **Resource boundary** (§4): per-request rounds, materialization, join work and read alternatives, order-independent (0 of 4,800
   shuffled requests differ), exact at need/need − 1; transport and dependent-use stopping fixed and tested; Q1, Q2.
5. **Lifecycle, scheduler** (§5): request-local rows, one Jacobi worklist; entry contract (§6), cost (§7), C2's inputs (§8), Q1–Q5 (§9).

## 1. Rule selection (§4.3 step 2)

**Rows are existing clauses.** The rows are SPEC 8.3.2's supply rows over storage Binding already has (no second clause language):

| Row | Conclusion (variables: Self, the Contract's own parameters) | Antecedent |
| --- | --- | --- |
| Input | a scope chain's `DirectFacts`, conjunct-split, exact compounds kept as written | none (seeded) |
| Refinement | `Self is D<b̄>` for each ancestor `D<b̄>` of K (`AncestorStorage`; Self may occur in b̄: G180) | `Self is K<Ā>` |
| Clause | each conjunct of each dependent clause of K (`ClauseStorage`, associated requirements included) | `Self is K<Ā>` |
| Conformance | `S<V̄> is K<ā>` of a verified path (`BoundConformancePath`); never for a recursive K (SPEC 8.3.3) | its conditions |
| Binding | `S<V̄>.(K).X is τ`, a completed binding read through the view (§4.2) | its conditions |

Identity is not a row: supplied identity facts are unioned into request-local classes closed under upward congruence, never
decomposed (SPEC 8.2.3), and every match is modulo those classes. Disjunction supplies nothing; `not` is a polarity of the same rows.

**Demands and selection.** A request seeds its goal Types and the inputs and works on request-local rows:

- **Demand (K, s, ā)**: facts `s' is K<ā'>` with `s' ≡ s`, `ā' ≡ ā` where bound (s or any āᵢ may be ⋆). Selected: every row whose
  conclusion is `σ is K<p̄>` (at the goal also `not σ is K<p̄>`): e-match p̄ against the bound arguments first, then σ against s; the
  bindings β give the antecedent Demand(K_α, β(Self) or ⋆, β(Ā) or ⋆). A free parameter is read off an available antecedent fact,
  never invented (`contract C<A>: Tree` with `Self.(Tree).Child is Copy` needs some `T is C<a>`); a ⋆ subject arises only from a
  conclusion without Self (a Contract-argument clause `A is E`).
- **Identity row (c)**: identity facts with a side in class c; every identity row is e-matched on both sides. If the match binds
  Self (or binds every variable of a row without antecedent) it is requested eagerly; otherwise (a closed side such as `i32`, or a
  parameter-only side) the other side is a **virtual node** of c. It expands fully bound against every existing node of its shape
  (the node's kids, or a ground Type matched against c, bind its variables: finite); an unbound expansion, an enumeration of the
  antecedent Contract, happens only when (a) a pattern needs its head in c, or (b) c meets a class holding facts of a demanded
  Contract, or one of their argument classes, that has a virtual node of that shape. Two static row indexes (heads a K-fact can
  have per position, closed over identity rows; whether any row concludes `not … is K`) skip impossible work and polarities.
- **Compound row (c)**: exact compounds whose shared subject e-matches c.

Every class a match visits and every class holding facts of an active demand gets an identity row. A goal demand serves both
polarities, antecedent demands only positive facts; tags recomputed each round from the goal say which polarity a row serves.

**Why the selection is complete.** Claim: if the supply has `s' is K<ā'>` with `s' ≡ s`, C derives it unless it stops with
Resource. Induction on the supply's derivation height: the last row instance concludes σ[β] ≡ s. Each identity fact of that
congruence has smaller height, so C supplies it once requested, and each has a side in a class C holds: the match of σ against s
descends only into classes it visits, which are identity-saturated (and woken when they gain nodes), so the fact is requested
eagerly, or it is a virtual node expanded against an existing node of its shape, or unbound where the match needs its head (rule
a) or where its class meets the fact's subject or argument class (rule b). So σ[β]'s node joins s's class, e-matching yields β
(or β' ≡ β), the antecedent demand exists, and its fact is derived by hypothesis.
Refinement rows have σ = Self; Contract-argument rows match `A` against s with a ⋆ antecedent; deep and non-prefix subjects are
matched node by node (no prefix restriction); arguments are matched before the subject, so a goal's `D<Self>` binds Self (G180).
Negated facts use the same rows; antecedents are positive. Nothing is searched that no row can conclude, and nothing is guessed.

**A syntactic-prefix selector is B.** It searches s and its prefixes. §3's B counterexamples are fixed inputs, each `none` under a
prefix selector and `+` for C and the reference: an identity to another subject (`U is T.(Aa).X`, input or derived), a
Contract-argument clause (`Dd<A>: A is Ee` through `T.(Cc).X is Dd<T.(Cc).Y>`, h4's shape; in a valid program forming
`Dd<Self.(Cc).Y>` also obliges `Self.(Cc).Y is Ee`, a second, prefix route), a deep subject through an identity (`U.(Bb).Y`), a
closed-Type identity (`i32 is Copy` via `Self.(It).Value is i32`) and a fact-holding class joined through a closed identity
(random model 63: `U ≡ T.(K3).Y ≡ List<i32>`, found only by rule b). §3's D input (`Gc<A>: associate Child is Gc<List<A>>;
A is Copy`) is finite both ways: `List<List<i32>> is Copy` +, `List<List<i64>> is Copy` none.

**Where it does not terminate.** The closure grows through identity instantiation and ⋆ demands. A polarity settles when
established (in a frozen view by a read-free support: ∅ is the minimal read set) or when none of its rows has work; a
subscription whose bindings fix its conclusion retires after one witness (outside frozen views). Infinite remain: a negative
answer over a recursive Contract with growing subjects or arguments, enumerating a closed Type's infinite class (`It: associate
Child is It; Self.(It).Value is i32; Self.(It).Value is Dd<Self>`, enumerating `i32`), every support when each reads a binding.
These end in Resource (§4), never Unknown.

## 2. Prototype and exhaustive bounded reference

C (`Solver.cs`) runs §1 in **Jacobi rounds**: a round computes its effects (demands, subscriptions, firings, identity rows, virtual
nodes) from the previous round's state only, then applies them and restores congruence. Rows are tabled by canonical classes within
the request; one view per request, so views never mix. A support records its row, antecedent facts, the binding read and the
identity facts of its classes; wake-ups re-select rows whose visited classes merged or gained a virtual node, and demands whose
Contract gained facts; re-entry reaches the same row (an edge, no evidence), so an unseeded component supplies nothing (least
fixed point). Answers: Complete, Provisional (a pending source was selected and skipped; never a cached negative) or Resource.
The reference (`Reference.cs`) applies every row to every fact forward, with Types up to a depth bound, naive congruence closure and
no selection; it is an oracle only where its answer is the same at bounds 5 and 7 ("stable").

Generated models (`random`: 3–5 Contracts with 0–1 parameters, associated requirements, deep and argument subjects, identity,
`not`, `and`/`or`, refinement with Self and arguments, verified, pending and conditional conformances, pending declarations,
inferred bindings with a frozen view). Each Supply query (plus up to 8 reference-derived facts per model) is compared at both
bounds, each Enumerate fact by fact, each frozen-view read set with the minimal binding sets under which the reference derives it:

| Seed (2,000 models each) | Stable Supply: agree / disagree / Resource | Enumerate: agree / disagree / Resource | Reads: exact / over / disagree |
| --- | --- | --- | --- |
| 20261021 (58 oracle overflows excluded) | 40,723: 40,588 / 0 / 135 | 3,627: 3,594 / 0 / 30 (3 unstable) | 1,442: 1,396 / 46 / 0 |
| 7 (66 excluded) | 40,674: 40,489 / 0 / 185 | 3,577: 3,542 / 0 / 32 (3 unstable) | 1,495: 1,424 / 71 / 0 |

No Supply query was unstable. The 320 Resource answers come from 70 models, all with recursive Contracts: 285 have no evidence in
the reference (a negative answer needing an infinite search); of 35 with evidence, 8 are frozen views (every support's reads) and
27 establish one polarity while the other's search is unbounded (contradiction must still be excluded). Agreement covers 22,488
positive, 384 negated and 737 contradictory (`+-`) answers. Per stable request: V median 4 (p95 ≤ 26), E 3 (≤ 12), J 12 (≤ 206),
rounds 1 (6). Runs and Resource cases with models: `results/random-seed-*.txt`, `results/resource-stable-*.txt`.

Fixed cases (54, all pass, [results/fixed.txt](results/fixed.txt)): G170 depth 1–8, the non-recursive variant, §3's B and D inputs,
the absent-head binder with and without an instance, ancestors with arguments, G180's forms, negation, contradiction, disjunction,
conjunction, a negated compound, an unseeded conformance cycle and one with an independent support, the recursive-Contract
prohibition, a pending declaration (Provisional; discoverable), frozen views and closed identities in recursive Contracts.

## 3. Real-source witnesses

`BindingSource` (adapter) builds the model from Binding's state after `Bind`, read-only: each Contract's `AncestorStorage` and
`ClauseStorage` (library and source), a scope chain's `DirectFacts` (never `DerivedFacts`), verified `BoundConformancePath`s with
`AssociatedTypes` and `InferenceEvidence`. Types translate structurally (a Contract's Self and own parameters become variables,
`Binding.ContainerSlot`); nothing is interned and no Binding state is written ([results/adapter-output.txt](results/adapter-output.txt)):

| Program (C0's and the 2b-S5 review's) | Binding today | C over Binding's own clauses | Reference |
| --- | --- | --- | --- |
| G170 `inspect` depth 1; 2, 4, 8 absent; 2, 4, 8 with the stronger hint before or after | Proven; Unknown; Proven | + Complete for all ten, same work with or without the hint (d8: 18 rounds, V 33, E 19) | + |
| Generated `T.Child^k` (never spelled), k = 3, 8, 16 | Unknown (C0) | + Complete (k = 16: 34 rounds, V 57) | - |
| Non-recursive `T.(Outer).Inner.(Holder).Item`, absent / hint | Unknown / Proven | + / + | + |
| G180 stale0 control `T.(C).X is D<i32>` | no issue | + | + |
| G180 stale1 `associate X is D<Self>`: `T.(C).X is D<T>` | `UnprovenConstraint_Kd` | + | + |
| G180 stale2 `contract C: D<Self>`: `T is D<T>` | `UnprovenConstraint_Kd` | + | + |
| G180 h4: the required `T.(C).X is D<T.(C).Y>`; D's clause `T.(C).Y is E` | `UnprovenConstraint_Kd` | + ; + | + ; + |
| G180 h6: `T.(C).X.(D<T.(C).Y>).N is D<…M>` | no issue | + | + |

Frozen views over real inference state (D: `x() -> i32`; E: `w() -> D.(P).X`; same rows in both declaration orders): in E's view
`D.(P).X is i32` is + with reads `{D.X}` (a completed external value); in D's own view none (exclusion); with E.W's returned
dependency `{D.X}` (§4.2's flow-back) `E.(Q).W is i32` is none in D's view, which today's owner state, publishing no dependency,
lets through. The same-definition chain (S: `w() -> Self.(P).X`) is none in S's own view (today `AssociatedTypeInferenceFailed_Kd`).

Corpus: every function scope with input premises in the 44 milestones and the Kimi library (121 scopes, 400 requests: inputs, input
subjects' enumerations, Copy of abstract parameters): all Complete, no Resource; C's Copy evidence equals `ProveCopy` on all 99
parameters (55 Proven, 44 Unknown); V ≤ 45, J ≤ 50.

## 4. Resource boundary

- **Limits, per request**: rounds (SPEC 8.3.3's proof-search depth), materialization (rows, supports, identity nodes), join work
  (matching steps, fact scans, congruence repairs), read alternatives per fact (frozen views). Each is a function of the state
  after a round and rounds are Jacobi, so reaching one depends only on the request and the program, never on query order,
  declaration order or earlier requests. Tested (`order`): 4,800 requests on 400 models with the rows shuffled, each at a
  generous, a 6-round and a 60-item limit: 0 differ in verdict, outcome, rounds, V, E or N. Rows of equal content are one row
  (keys include the requesting match's polarity and a chained virtual node's origin; merged rows merge their subscriptions).
- **Values** (Q5): 4,096 rounds, 65,536 items, 2^24 join steps, 64 alternatives (corpus maximum V 45, J 50). Tested (`resource`):
  G170 depth 8 and 64 complete with each limit equal to the need (depth 64: 130 rounds, 331 items) and are Resource at need − 1;
  every support of `i32 is Copy` through a binding over `It` is Resource at every size limit.
- **No partial answer**: Resource replaces the whole answer (polarities, reads); a nested judgment is in the closure, so Resource too.
- **Transport and stopping rule**: C returns Resource (limit, count); `ProveConstraint` passes it on without combining it: a
  dependent judgment (And/Or, capability, conformance condition) absorbs it as Error is absorbed (SPEC 8.3.2), so no dependent
  Language diagnostic follows, and it never marks an environment Invalid (today `ValidateConstraintEnvironments` turns an Error
  fact into `Invalid`). One record per (scope, limit), located at the scope owner's signature as `CaseLimit` is; independent
  judgments are unaffected (`resource` test 4: a provable, a Resource and an independent unprovable requirement give `ok`,
  `Resource`, `Unproven`). No existing code fits: `GenerationResourceLimit_Kd` is generation's (PlanB forbids reuse), the others
  are ownership, expansion and interpolation limits (Q1, Q2).

## 5. Lifecycle and scheduler (§4.4)

- **Request-local**: identity classes, rows, subscriptions, virtual nodes, supports, facts; capacity is reused, no semantic answer
  survives a top-level request, and an answer (Resource included) is a function of (request, inputs).
- **Persistent answer-shaping state** (C0's census) becomes request-local or a function of its key's inputs: capability results
  (`capabilityNodes`, kept until `ClearCapabilityResults`); `AssociatedInference.Normalized` (per frozen batch: its view is its key);
  TyCtxt `retentions` by scope (read through Enumerate, so a function of the scope's inputs); composed scopes' copied `DerivedFacts`
  (inputs only); the guards `contractPremiseQueries`, `normalizingAssociated`, `conformanceQueries` (row identity: re-entry is an edge).
- **One scheduler**: the request-local Jacobi worklist hosts premise rows now, capability nodes and conformance verification at C3,
  G172's value demands later. Shared contract: each node kind keeps its transfer rule and initial value (Owned's optimistic start
  stays the capability owner's); a round reads only the previous round's state; wake-ups go to rows whose last evaluation read a
  changed row or class; limits count all kinds. Today's capability graph differs in FIFO order (for monotone rules the same fixed
  points) and in living across queries (PlanB's Finality risk); both go, so no second scheduler. Unverified until C3: a cycle mixing
  an optimistic Owned node with an inductive premise row (premise rows read capabilities only through judgments of the same request).
- **Validation callbacks**: `ResolveConformance` runs `VerifyConformance` inside a proof (writes `IsVerified`, `Checking`, witness
  maps, `failedConformance`); `AvailableContractPremise` runs `CheckClosedDeclarationConstraints`. C reads a published state per
  declaration and conformance path (Valid, Pending, Invalid) and never runs verification: a Pending read is Provisional, never
  cached; verification is a scheduled node that publishes, after which requests that read Pending re-run (request-local). A path
  under its own check reads itself as Pending and supplies nothing to itself.

## 6. Representation and entry contract (C2/C3)

Files under `src/Kimi/Compiler/Binding/`, concern ContractSolver, each within the 5,000-token limit: `Binding.ContractSolver.cs`
(entry file, beginning with this contract; the entry operations and request driver), `.Rows.cs` (the row index over existing
storage and the two static indexes, referencing `BoundConstraint`/`BindingSymbol`/`IsKoto`), `.Demands.cs` (rows, subscriptions,
rounds, tags, limits), `.Matching.cs` (e-matching, virtual nodes, meetings); `Binding.TypeIdentity.Classes.cs` (request-local
identity classes over interned `BoundType`s; Q4).

- `Supply(BoundConstraint proposition, EvidenceView view) -> Evidence { bool Positive, bool Negative, Completion, Reads }`.
- `Enumerate(BoundType subject, EvidenceView view, declarations) -> SubjectEvidence { facts on the subject's class (positive or
  negated bound Contract references, available or only discoverable), identity members, Completion }`; `declarations` is the
  consumer's candidate set (Contracts declaring the looked-up member or associated name; all for identity normalization).
- `EvidenceView { BindingScope Premises, SourceState (provisional pass or final), FrozenDefinition (null for ordinary proof),
  Inadmissible (the inference owner's published set for it) }`; reads are recorded exactly when FrozenDefinition is set.
- `Completion = Complete | Provisional | Resource(limit, count)`; `Reads` = the antichain of minimal binding-read sets over every
  admissible support (Q3). Nested reads with the same view join the open request.

Invariants: Jacobi rounds over request-local rows; answers are functions of (request, inputs); selection over all of §1's rows;
identity never decomposed; nothing guessed; conformance rows only at subjects present; inadmissible bindings never read; Pending
never cached as negative; one view per request. Prohibitions: no Binding writes, no syntax-triggered supply, no `DerivedFacts`, no
fallback fact scan, no mode or bool parameter, no cross-request semantic cache, no second scheduler. Approvals: a DECISIONS entry
(R7's Contract-solver premise responsibility brought forward, representation, rejected options), COMPILER_ARCHITECTURE (Canonical
components row, R7 ledger note), CODEMAP at C2, the owner's answers to Q1–Q5.

## 7. Cost

Measured while other sessions' builds and tests kept the 32-thread machine at about 82% CPU; indicative, no speed claim. The
prototype is a reference implementation (string keys, LINQ, full fact rescans per round), not the production evaluator.

| Workload (`cost`, [results/cost.txt](results/cost.txt); median of 15 after 5) | Rounds | V | E | J | Time | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| G170 Copy, depth 1 / 8 / 64 | 4 / 18 / 130 | 8 / 29 / 197 | 5 / 19 / 131 | 20 / 398 / 58,694 | 0.14 / 0.63 / 35 ms | 26 KB / 173 KB / 5.9 MB |
| G170, an unrelated Contract, depth 8 / 64 (settled statically) | 0 | 2 | 1 | 0 | 10 / 41 µs | 15 / 71 KB |
| Wide: 8 / 32 / 128 Contracts on T | 2 | 6 | 10 / 34 / 130 | 35 / 131 / 515 | 27 / 65 / 242 µs | 33 / 85 / 308 KB |
| Diamond refinements, height 4 / 16 / 64 | 2 | 74 / 650 / 8,714 | 3 | 64 / 616 / 8,584 | 0.1 / 0.9 / 38 ms | 0.1 / 1.2 / 17 MB |
| §3's D input, depth 6: derivable / not | 14 / 7 | 29 / 24 | 15 / 1 | 1,142 / 161 | 251 / 43 µs | 278 / 64 KB |

V, E and nodes grow linearly with depth and width; J and time faster, as the prototype rescans every fact of a Contract per
subscription per round (semi-naive deltas are C3's). The diamond is quadratic because rows copy the transitive `AncestorStorage`;
the production index should hold direct parents (rows ∝ refinement edges). 1,000 identical requests cost 1,000 times one (215 ms):
no cross-request reuse by design (PlanB's requery risk; a cache needs Finality-preserving keys and measurement).

Today's supply on the same inputs (adapter, interleaved medians of 41): `ProveCopy` of the G170 value takes 11–35 µs and 96 B (the
measurement's delegate included) against 0.19–0.94 ms and 52–191 KB for the prototype, while today's answer is Unknown wherever
the spelling hides CopyTree; a whole warm Bind of those programs takes 36–130 ms. Corpus: warm rebinds of the 44 milestone
programs take 2,256 ms in all; the prototype's 400 requests take 83 ms and 14 MB (not equivalent work: Bind does far more than
premise supply, and today's eager expansion is not separable without instrumentation). C0's benchmark (`--associated-inference`)
times inference binds; no premise workload exists yet (PlanB §7: added at C3/C4 if it is a distinct responsibility).

## 8. C2's input list

C0's census ([c0/census-raw.tsv](../c0/census-raw.tsv)) mapped to the operations; C2 routes each read as a pure move (answered from
today's Facts), C3 replaces the answer. Paths under `src/Kimi/Compiler/Binding/`.

| Read site (method) | Operation | Behavior change at C3 |
| --- | --- | --- |
| `AvailableConstraintFact` (22 calls in 16 files), `ProveConstraint` scans (`Constraints.cs:640`, `:642`) and its presence gate | Supply | derived, not stored; the `HasAssociatedProjection` gate goes |
| `ProveConformance` premise loop (`Contracts.cs:681`); `ResolveConformance`, `ProveConformanceConditions`, `ProveMemberConditions` Invalid walks | Supply | premise part only; `VerifyConformance` leaves the proof (§5) |
| `SymbolicCapability` (`Capabilities.cs:536`) | Supply (refinements of the intrinsic); Enumerate (identity members) | unspelled projections (G170) |
| `FindAssociated` (`AssociatedTypes.cs:114`), `RequirementMember` (`ContractCalls.cs:114`), `HasContractFact` (`Indexers.cs:209`), `SelectValueCall` (`CallableSelection.cs:49`, `:63`), `TryCallable` (`ValueCalls.cs:296`), `CallableBoundSignatures` (`Retention.cs:45`) | Enumerate (Contracts declaring the name) | derived candidates, spelling-independent; pending ones stay discoverable for ambiguity |
| `ArithmeticNumericDomain`, `SelectArithmetic` (`ArithmeticOperators.cs:44`, `:205`), `ValidateArithmeticDirections` (`ArithmeticDiagnostics.cs:51`, `:68`) | Enumerate (arithmetic Contracts) | arithmetic premises become rows |
| `AvailableEffectBounds` (`EffectDeclarations.cs:217`), `AvailableCallableEffects` (`CallableEffects.cs:33`), `EffectPremises` with `TyCtxt.Copy`, `Sealed`, `RecursiveBase` | Enumerate / Supply in the definition's view | derived bounds |
| `HasParameterIdentity`, `SubstituteIdentityPremises`, `IdentityPremise` (`TypeIdentity.cs:12`–`:149`), `ContractTypeCore` (`AssociatedTypes.cs:684`), `NormalizeProofConstraint`/`NormalizeProofType` (`Constraints.cs:685`, `:714`), `HasValueRole` (`ConstraintValidation.cs:673`), `ValidateContractDeclarations` (`Contracts.cs:887`), `FailConstructorAbsence` (`Structs.cs:329`) | Enumerate (identity members) | Q4; the `HasParameterIdentity` flag goes |
| `AdmittedSemantics` (`ConstraintValidation.cs:59`), `ValidateConstraintEnvironments` (`Constraints.cs:401`), `ValidateConstraintUses` (`ConstraintValidation.cs:404`), `RecordReferenceConstraint` (`FunctionItems.cs:526`), `ReportQualification` (`Qualification.cs:291`), `ValidateConditionalBlock` (`ConditionalMembers.cs:74`) | none: input validation over DirectFacts | Resource never marks an environment Invalid |
| `DisjointConformanceFacts`, `RefutedOverlapInputs`, `ValidateAssociatedOverlapAgreement` | Supply in the composed scope | composed scopes hold inputs only |

Writers: `AddConstraintFact` stays for direct inputs only; `AddContractPremises` (4 callers: the eager Contract-scope Self, the
snapshot, `BindAssociatedProjection`, `RequirementMember`), `ExpandScopeContractPremises` (6 callers, `contractFactScratch`) and
`AddArithmeticPremises` go at C3 (a Contract scope's Self becomes one input). Scope composers (`VerifyConformanceAgreement`,
`ContractOverlapScope`, `AddOverlapFacts`, `CompatibleRequirement`, `AddInheritedPremises`, `PrepareVirtualHeaderPremises`,
`PrepareVirtualOverridePremises`, `InheritVirtualConstraints`) copy inputs and substituted clauses as inputs; `DerivedFacts` goes.
Flags: `HasAssociatedProjection` goes; `HasParameterIdentity` becomes Enumerate's members. Caches: §5. S5's view
(`Binding.RecursiveContracts.cs`, R4-M87) reads the row index at C4 (the prototype's `IsRecursive` is SPEC 8.3.3's search over
refinement and clause rows) and inherits G180's correction (rows substitute Self inside reference arguments, §3).

Behavior-preserving restructures first (C2, unchanged diagnostic/HIR/MIR evidence): `Binding.Contracts.cs` (6,516 tokens) premise
supply (`ExpandContractPremises`, `ExpandScopeContractPremises`, `AddContractPremises`, `ContractConstraint`, `ProveConformance`) to
`Binding.ContractSolver.cs`; `Binding.Constraints.cs` (5,324) facts and availability (`AddConstraintFact`,
`AvailableConstraintFact`, `AvailableContractPremise`) to `.Rows.cs`, the proof stays; `Binding.AssociatedTypes.cs` (5,047) lookup
(`FindAssociated`, `BindAssociatedProjection`) to `Binding.AssociatedLookup.cs`, resolution stays.

## 9. Departures, owner questions, unverified

PlanB §4's contract holds; C1 does not stop. Details §4 left open, and findings:

1. **Identity classes are part of selection.** Matching modulo SPEC 8.2.3 needs classes over the request's Types. Today's identity
   owner substitutes parameter identities and rewrites a projection towards an identity's required side; it has no classes. Without
   them even a prefix-free selector misses §1's identity cases.
2. **Closed and parameter-only identity sides need laziness**: virtual nodes (bound against existing nodes; unbound only for a
   needed head or a meeting), per-polarity settlement, one-witness retirement, the two static indexes, read-free settlement in
   frozen views; without them `Self.(It).Value is i32` in a recursive Contract makes every request touching `i32` Resource (as the
   first prototype did). Each rule and each content key was added for a reproduced disagreement, non-terminating or order-dependent
   case (fixed cases; random models 63, 186, 202, 306, 422, 769, 1533, 1824, 1825 of seed 20261021, 1850 of seed 7), all agreeing
   now. §1's completeness argument is a sketch; the agreement runs are its evidence.
3. **Reads (§4.2)**: every admissible support, searched exhaustively, reported as the antichain of minimal binding-read sets, not
   their union: a union makes a value depend on a binding an independent derivation does not read, against PlanB §1's "an
   independent permitted derivation may still settle the goal". The class-level identity explanation over-approximates (117 of
   2,937 read sets, always supersets: it can only exclude more); exact reads need explanations along the identity proof.
4. **Verification inside proof**: `ResolveConformance` runs `VerifyConformance` (writes); C reads published state (§5).
5. **Not modeled** (rows or their owners at C2/C3): Semantics and Callable constraints, built-in arithmetic premises and
   PrimitiveInteger's implication, Origin-family arguments (variables; the prototype keys them into heads), conditions on structured
   subjects (bare variables and ground Types only), generic conformances seen from a value side.

**Owner questions**:

- **Q1** A new Resource code, e.g. `ProofResourceLimit_Kd` ("Proving {0} exceeds the implementation limit of {1} {2}"; Arguments
  proposition:Text, limit:Number, kind:Text) with its DiagnosticRequirement entry, reported at the scope owner's signature as
  `CaseLimit` is, or through a new `BindingFailure` member (88 of 89 used).
- **Q2** Transport: a fifth `ConstraintProof` member `Resource` (every switch absorbs it, none reports it), or `Error` plus a side
  record every Error-to-Invalid path must recognize. Recommended: the member (one meaning per value, no side channel).
- **Q3** SPEC 8.5.5's "depends": C1 reads it per derivation (item 3). Confirm, or choose the union reading (simpler, rejects more).
- **Q4** The identity classes are SPEC 8.2.3's identity: TypeIdentity should own them and C read them; normalization moving onto
  them is the identity owner's own reproduced and reviewed unit (PlanB §4.3 step 6). Private to C they would be a second identity
  representation (rejected).
- **Q5** Limit values (§4).

**Unverified**: production cost (§7); real-source witnesses for effect premises, lookup under Enumerate, arithmetic, effect bounds
and composed scopes; G172's in-progress admissibility; mixed Owned/premise cycles, restart after publication and nested requests
(no judgments in the prototype); Origin families; that stability at bounds 5 and 7 gives the unbounded answer.
