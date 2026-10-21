# G170 C1: executable design of C (ContractSolver premise supply)

Unit C1 of [PlanB](../../../Proposals/2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) (BU planning session,
2026-10-21), after [C0](../C0-evidence.md). Draft data only. Branch `claude/bu-g170-c1` from origin/dev `e0f24c83`. Revised after the
independent review (ACCEPT WITH CHANGES): §10 gives the ruling and evidence for each finding.

[proto/](proto/): model, identity classes (`EGraph.cs`), C (`Solver.cs`), judgments (`Checker.cs`), bounded reference, runner
(`dotnet run -c Release -- fixed|random [n] [seed]|order [n]|resource|cost`; harness item limit `G170_ITEMS`);
[adapter/](adapter/G170C1AdapterTest.cs) runs in a scratch test assembly (`G170_C1_OUT=<file> [G170_C1_CORPUS=<abs>] xUnitTest.exe -method …Probe`); [results/](results/).

## 0. Verdict per completion condition

1. **Rule selection** (§1): every SPEC 8.3.2 supply row is selected by e-matching through static indexes keyed by conclusion heads,
   so a request examines only rows that can meet its classes; one laziness rule covers every identity row matched only through a
   closed side. A prefix selector is B.
2. **Agreement** (§2): 16,222 stable Supply queries on 800 generated models and 1,456 stable enumerations, 0 disagreements; the
   reference builds its own rows and recursion classification, and the classifications agree on all 3,843 Contracts.
3. **Real-source witnesses** (§3): G170 at every depth whatever the spelling, G180's probes, frozen views; the corpus matches `ProveCopy`.
4. **Resource** (§4): two limits (rounds, items); counts and limit outcomes order-free, J included (1,832 requests, 6,276 boundary
   runs, 0 differ); a published Resource state per source; transport through real judgments, including the restart.
5. **Lifecycle, scheduler, contract, cost** (§5–§7): one Jacobi worklist with a stratification rule; no read sets; unrelated requests
   flat in program size (n = 10…1,600); C2's inputs (§8); decisions (§9).

## 1. Rule selection (§4.3 step 2)

**Rows are existing clauses** (no second clause language):

| Row | Conclusion (variables: Self, the Contract's own parameters) | Antecedent |
| --- | --- | --- |
| Input | a scope chain's `DirectFacts`, conjunct-split, exact compounds kept as written | none (seeded) |
| Refinement | `Self is D<b̄>` for each ancestor `D<b̄>` of K (`AncestorStorage`; Self may occur in b̄: G180) | `Self is K<Ā>` |
| Clause | each conjunct of each dependent clause of K (`ClauseStorage`, associated requirements included) | `Self is K<Ā>` |
| Conformance | `S<V̄> is K<ā>` of a verified path (`BoundConformancePath`); never for a recursive K (SPEC 8.3.3) | its conditions |
| Binding | `S<V̄>.(K).X is τ`, a completed binding read through the view | its conditions |

A row's source state is its declaration's (a refinement row: the worst along its path), its conformance's or its binding's (§4).
Identity is not a row: supplied identity facts are unioned into request-local classes closed under upward congruence, never
decomposed (SPEC 8.2.3), and every match is modulo those classes. Disjunction supplies nothing; `not` is a polarity of the same rows.

**Demands and selection.** A request interns only its goal's Types and its inputs; a Type enters when a fired row, a condition or
a goal names it.

- **Demand (K, s, ā)**: facts `s' is K<ā'>` with `s' ≡ s`, `ā' ≡ ā` where bound (s or any āᵢ may be ⋆). Selected: K's rows whose
  conclusion subject is a variable, and those whose subject head is a head of s's class or of its virtual nodes (a static index);
  each is e-matched, arguments first (a goal's `D<Self>` binds Self: G180), then the subject; the bindings β give the antecedent
  Demand(K_α, β(Self) or ⋆, β(Ā) or ⋆). A free parameter is read off an available antecedent fact, never invented. A demand meets
  the classes holding its Contract's facts (and their argument classes); a variable already bound to class x and matched against
  another class c makes c and x meet too (`A is K0<A>` with A = i64 from the argument and subject i32).
- **Identity row (c)**: an identity row's side is **open** when it mentions the row's subject (Self; a binding's projection) and
  **closed** otherwise (`i32`, `A`, `List<A>`, a binding's value). Open sides, indexed by (head, first kid head), are matched against
  c's nodes and requested eagerly. **One laziness rule for every closed side, ground or not**: matched only through it, the other
  side is a **virtual node** of c, never stored per row, found through the closed-side index (other side's head, closed side's head,
  other side's first kid head; rows sharing a closed side are one match) where (a) a pattern needs its head in c (a ground pattern
  also sends the need down to the kids of c's nodes with its head: upward congruence), (b) c meets another class, or (c) an existing
  node has its shape (bound expansion, finite). A side meeting c only through a virtual node makes its row's other side one too (a
  **chain**, forwarding to its base, possibly another instance of the same row), detected for a need and the needs its candidates
  meet and stored as a row; a need met deeper is a **need row**, answered as its own lookup next round: every need gets its chains,
  one level per round, with bounded work per lookup. A class demand covers its nodes' kid classes; an identity goal's classes meet.
- **Compound row (c)**: exact compounds whose subject e-matches c, by subject head.

Two further static indexes (heads a K-fact can have per position; whether any row or input concludes `not … is K`) skip impossible
expansions and polarities. Tags recomputed each round from the goal say which polarity a row serves.

**Why the selection is complete.** If the supply has `s' is K<ā'>` with `s' ≡ s`, C derives it unless it stops with Resource.
Induction on derivation height: the last row instance concludes σ[β] ≡ s; each identity fact of that congruence has smaller height
and a side in a class C holds or in a kid class of one; those classes are identity-saturated (and woken on merges and new chains), so
the fact is requested through its open side or, through its closed side, expanded where σ needs its head (at c or a kid), where classes
meet, or against an existing node; a side meeting c only through such nodes is a chain, found one level per round. So σ[β] joins s,
e-matching yields β and the antecedent's fact is derived by hypothesis. The indexes prune only heads a class or its virtual nodes
cannot hold. A sketch: the agreement runs (§2) are its evidence, and each rule was added for a reproduced miss (§10).

**A syntactic-prefix selector is B.** §3's B counterexamples are fixed inputs, each `none` under a prefix selector and `+` for C and
the reference: an identity to another subject, a Contract-argument clause (h4's shape), a deep subject through an identity, a
closed-Type identity and congruence through a kid. §3's D input (`Gc<A>: associate Child is Gc<List<A>>; A is Copy`) is finite.

**Where it does not terminate.** A polarity settles when established or when none of its rows has work; a subscription whose
bindings fix its conclusion retires after the round that found a witness. Infinite remain: a negative answer over a recursive Contract
with growing subjects or arguments, and enumerating a closed Type's infinite class (`It: associate Child is It; Self.(It).Value is
i32; Self.(It).Value is Dd<Self>`, enumerating `i32`). These end in Resource (§4), never Unknown. Frozen views settle like ordinary ones.

## 2. Prototype and exhaustive bounded reference

C runs §1 in **Jacobi rounds**: a round computes its effects from the previous round's state only (lookups answered once per round
and replayed with their visits, needs and Resource reads for later requesters), applies them and restores congruence; subscriptions
scan only facts added since their last scan (all after a merge). Answers: Complete, Provisional (a pending source was selected) or
Resource. The reference applies every row to every fact forward with Types up to a depth bound and no selection, an oracle where its
answer is the same at bounds 5 and 7; it builds its rows from the declarations (direct parents, conjunct-split clauses, conformances
and bindings at materialized Types) and classifies recursion by SPEC 8.3.3's definition (a bounded closure from `T0 is C<P̄>`) (N2).

Generated models (3–5 Contracts with 0–1 parameters, associated requirements, deep, argument and compound subjects, identity, `not`,
`and`/`or`, refinement, verified, pending and conditional conformances, inferred bindings with a frozen view): each Supply query and
up to 8 reference-derived facts compared at both bounds, each Enumerate fact by fact; harness 300 rounds, 3,000 items ([results/](results/)):

| Seed (400 models) | Stable Supply: agree / disagree / Resource | Enumerate: agree / disagree / Resource | Recursive Contracts (differ) |
| --- | --- | --- | --- |
| 20261021 (15 oracle overflows excluded) | 8,038: 7,986 / 0 / 52 | 731: 719 / 0 / 11 (1 unstable) | 506 of 1,924 (0) |
| 7 (11 excluded) | 8,184: 8,175 / 0 / 9 | 727: 721 / 0 / 5 (1 unstable) | 470 of 1,919 (0) |

No Supply query was unstable. Of 61 Resource answers, 56 have no evidence in the reference (an unbounded search); in 5 (models 49,
371) C had established the positive polarity when the negated search reached the limit. Per stable request: V median 6 (p95 ≤ 53),
J median 11 (p95 ≤ 2,546), rounds median 2 (p95 ≤ 7).

Fixed cases (76, all pass, [results/fixed.txt](results/fixed.txt)): G170 depth 1–8, §3's B and D inputs, the absent-head binder,
ancestors, G180's forms, negation, contradiction, compounds, conformance cycles, recursion classification routes, pending and
Resource sources, the check under way, frozen views, closed identities in recursive Contracts, and one case per gap of §10.

## 3. Real-source witnesses

`BindingSource` (adapter) builds the model from Binding's state after `Bind`, read-only: `AncestorStorage`, `ClauseStorage`, a scope
chain's `DirectFacts` (never `DerivedFacts`), verified `BoundConformancePath`s with `AssociatedTypes` and `InferenceEvidence`
([results/adapter-output.txt](results/adapter-output.txt)):

| Program (C0's and the 2b-S5 review's) | Binding today | C over Binding's own clauses | Reference |
| --- | --- | --- | --- |
| G170 `inspect` depth 1; 2, 4, 8 absent; 2, 4, 8 with the stronger hint before or after | Proven; Unknown; Proven | + Complete for all ten, same work with or without the hint (d8: 18 rounds, V 33) | + |
| Generated `T.Child^k` (never spelled), k = 3, 8, 16 | Unknown (C0) | + Complete (k = 16: 34 rounds, V 57) | - |
| Non-recursive `T.(Outer).Inner.(Holder).Item`, absent / hint | Unknown / Proven | + / + | + |
| G180 stale0 control; stale1 `associate X is D<Self>`; stale2 `contract C: D<Self>` | none; `UnprovenConstraint_Kd` ×2 | + ; + ; + | + |
| G180 h4: the required `T.(C).X is D<T.(C).Y>`; D's clause `T.(C).Y is E`; h6 | `UnprovenConstraint_Kd`; none; none | + ; + ; + | + |

Frozen views over real inference state (D: `x() -> i32`; E: `w() -> D.(P).X`; both declaration orders): in E's view `D.(P).X is
i32` is +; in D's own view none (exclusion). Without read sets (OQ3 option (c), G172's to decide), using E.W in D's view evaluates
E.W's own evidence (`D.(P).X is i32`) in a view excluding D: none, so E.W is inadmissible there and `E.(Q).W is i32` is none in D's
view (today's owner admits it: +). Both steps are plain Supply requests. The same-definition chain is none in S's own view.
Corpus: every function scope with input premises in the 44 milestones and the Kimi library (121 scopes, 400 requests): all
Complete, no Resource; Copy evidence equals `ProveCopy` on all 99 parameters (55 / 44); V ≤ 45, J ≤ 62.

## 4. Resource boundary and transport

- **Limits, per request** (owner, OQ2): **rounds** (SPEC 8.3.3's search depth) and **items** (rows, supports, identity nodes; the
  seeded state counts); 4,096 and 65,536 provisional, confirmed by C3's measurements and recorded in IMPL 21.3.2/A.4 at C3. No
  join-work and no read-alternatives limit. Resource replaces the whole answer (a nested judgment is in the closure).
- **Order-free.** Each limit reads the state after a round and rounds are Jacobi. J (reported, never a limit) counts matching steps
  and index entries examined, a function of the round's starting state (a subscription examines every new candidate, then retires;
  lookups are answered once per round), plus classes merged and distinct nodes over a merged class (a function of the partition, not
  of union order). Tested (`order`, [results/order.txt](results/order.txt)): 1,832 requests on 400 models with templates and
  Contracts shuffled, ordinary and frozen views, compared in verdict, outcome, reason, rounds, V, E, J, N and peak items, plus 6,276
  runs at each request's need and need − 1 per limit: 0 differ; Complete/Provisional at the need, Resource below it, in every case.
- **Tested values** (`resource`, [results/resource.txt](results/resource.txt)): G170 depth 8 and 64 complete at the need (depth 64:
  130 rounds, 331 items) and are Resource at need − 1; enumerating a closed Type's infinite class is Resource at 1,000, 3,000 and
  10,000 items (0.7 s, 3.3 s, 118 s; J 0.65 M, 16 M, 586 M): time to the item limit grows faster than the items (rows woken by
  merges are re-selected in full); C3 measures it at the chosen value; no work limit returns without the owner.
- **Published Resource state.** A declaration, conformance or binding whose own check ended at a limit publishes `Resource` (beside
  Valid, Pending, Invalid). C reads it as Resource: a selected row from such a source makes every polarity the request did not
  establish Resource, never absent (so never Unknown or `UnprovenConstraint_Kd`), and never marks anything Invalid.
- **Transport** (planning session, Q2): `ConstraintProof` gains a fifth member `Resource`. `ProveConstraint` maps Complete evidence
  to Proven, Refuted, Unknown or Error (both polarities); Provisional to the same value, not final (no diagnostic; judged again after
  the pending sources publish; at a final pass no source is Pending); Resource to `ConstraintProof.Resource`. Judgments absorb it as
  Error is absorbed (SPEC 8.3.2: And/Or, capability, conditions, conformance checks; with both, Error). One record per (scope, limit)
  where the limit was hit, at the scope owner's signature as `CaseLimit` is, with the shared analysis-limit code (owner, OQ1).
- **Tested** (`resource` transport): `conformance S1 is Deep`, whose obligation (a G170 depth-8 proof) is checked at a 6-round limit,
  publishes Resource and records `Resource(check S1 is Deep: rounds>6)`; then `S1 is Deep`, `S1 is Deep and U is Copy`, `S1 is Deep
  or T is Copy`, `S2 is Bb` (conditioned on S1) and `S1.(Deep).D is Copy` (through its binding) are Resource and record nothing; an
  independent `U is Copy` records `Unproven`. Judged before the check (reading Pending: not final, judged again after publication)
  or after it, the records are the same; at the default limit S1 is Valid and only the two `U is Copy` uses are Unproven.

## 5. Lifecycle and scheduler (§4.4)

- **Request-local**: identity classes, rows (demands, class demands, subscriptions, chain rows, need rows), supports, facts and the
  per-round lookup answers; capacity is reused; an answer (Resource included) is a function of (request, inputs).
- **Persistent answer-shaping state** (C0's census) becomes request-local or keyed by its inputs: capability results, `Normalized`
  (per frozen batch), `retentions` (through Enumerate), composed scopes' `DerivedFacts` (inputs only), the three re-entry guards.
- **One scheduler**: the Jacobi worklist hosts premise rows now, capability nodes and conformance verification at C3, G172's value
  demands later; each node kind keeps its transfer rule and initial value; a round reads only the previous round's state; limits
  count all kinds. **Stratification rule** (N5): a premise row reads only judgments final in the request, and Owned's optimistic
  start never feeds a premise row; a cross-kind cycle in a valid program needs an explicit rule or the contract difference PlanB
  allows, recorded; C3 tests it.
- **Source states**: C reads a published state per declaration, conformance path and binding (Valid, Pending, Invalid, Resource) and
  never runs verification; a Pending read is Provisional, never cached, and re-run after publication. **The check under way** is an
  `EvidenceView` exclusion (N4): its own conformance row supplies nothing to it and the answer stays Complete (SPEC 8.3.3; fixed
  case). **Invalid** supplies nothing, as today (N7); SPEC 8.3.1 makes a proposition naming an invalid declaration Error through the
  judgment's own validation, and whether a selected Invalid source makes the answer Error (PlanB §4.3.5's "relevant Error
  dependencies") is C3's to decide with a reproducer. Moving `VerifyConformance` and `CheckClosedDeclarationConstraints` out of
  proof is its own reviewed unit with a reproducer (N6).

## 6. Representation and entry contract (C2/C3)

Files under `src/Kimi/Compiler/Binding/`, each within the 5,000-token limit: `Binding.ContractSolver.cs` (entry file, beginning with
this contract; entry operations and request driver), `.Rows.cs` (row indexes over existing storage: conclusion heads, open sides,
closed sides, static indexes), `.Demands.cs` (rows, subscriptions, need rows, rounds, tags, limits), `.Matching.cs` (e-matching,
virtual nodes, chains, meetings); identity classes are TypeIdentity's (Q4; its own unit first: OQ5).

- `Supply(BoundConstraint proposition, EvidenceView view) -> Evidence { bool Positive, bool Negative, Completion }`.
- `Enumerate(BoundType subject, EvidenceView view, declarations) -> SubjectEvidence { facts on the subject's class (positive or
  negated, available or only discoverable), identity members, Completion }`; `declarations` is the consumer's candidate set;
  identity normalization passes none and reads members only (N8).
- `EvidenceView { BindingScope Premises, SourceState (provisional pass or final), FrozenDefinition (null for ordinary proof),
  Inadmissible (the inference owner's published set), UnderCheck (the conformance or declaration whose own check is running) }`.
- `Completion = Complete | Provisional | Resource(limit, count | source)`. No read sets (B2, OQ3): "depends" is per derivation and
  G172's design decides how a definition's value reaches another definition's view (prefer option (c), §3).

Invariants: Jacobi rounds over request-local rows; answers, counts and limit outcomes functions of (request, inputs); indexes prune
only impossible heads; identity never decomposed; nothing guessed; inadmissible bindings never read; Pending never cached as
negative; Resource never read as absent. Prohibitions: no Binding writes, no syntax-triggered supply, no `DerivedFacts`, no fallback
fact scan, no mode or bool parameter, no cross-request semantic cache, no second scheduler, no read sets.

## 7. Cost

A reference implementation (string keys, LINQ), not the production evaluator; no speed claim ([results/cost.txt](results/cost.txt);
median of 15 after 5; V, J and time at the listed sizes):

| Workload | Rounds | V | J | Time | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| G170 Copy, depth 1 / 8 / 64 | 4 / 18 / 130 | 8 / 29 / 197 | 25 / 466 / 61,030 | 0.2 / 1.8 / 27 ms | 38 KB / 0.3 / 12 MB |
| G170, an unrelated Contract, depth 8 / 64 (settled statically) | 0 | 2 | 0 | 27 / 94 µs | 19 / 96 KB |
| Wide: 8 / 32 / 128 Contracts on T | 2 | 6 | 46 / 166 / 646 | 48 / 109 / 372 µs | 43 / 98 / 338 KB |
| Diamond refinements, height 4 / 16 / 64 | 2 | 74 / 650 / 8,714 | 126 / 1,230 / 17,166 | 0.14 / 1.0 / 35 ms | 0.2 / 1.3 / 17 MB |
| §3's D input, depth 6: derivable / not | 14 / 7 | 34 / 24 | 652 / 77 | 233 / 34 µs | 335 / 98 KB |

**Program-size scaling** (the review's B3 workload: n conformances binding `X = i32`, `T is Holder`, `Self.(Holder).Item is Copy`):

| Request (identical at n = 10, 100, 400, 1,600) | Answer | Rounds | V | J | N |
| --- | --- | ---: | ---: | ---: | ---: |
| `List<i32> is Copy`, `T.(Holder).Item is Copy` (unrelated) | none, + | 1, 2 | 3, 6 | 1, 11 | 5, 4 |
| `i32 is Copy`, `S1.(Pp).X is Copy` (through `i32`), `i32 is Pp` | +, +, none | 1, 2, 0 | 2, 4, 2 | 2, 21, 0 | 4, 7, 4 |

Unrelated requests no longer grow with the program (review: J 23/203/803 and 68/608/2,408 at n = 10/100/400); related requests use
only the rows that can meet their classes. V and N grow linearly with depth and width, J faster (woken rows are re-selected in full;
semi-naive deltas for every row kind are C3's); the diamond is quadratic because rows copy the transitive `AncestorStorage`. No
cross-request reuse by design. Adapter: today's `ProveCopy` of the G170 value 8–13 µs against about 0.9 ms for the prototype (today's
answer is Unknown wherever the spelling hides CopyTree); the corpus's 400 requests take 52–72 ms beside 0.9–1.4 s of warm rebinds.

## 8. C2's input list

C0's census ([c0/census-raw.tsv](../c0/census-raw.tsv)) mapped to the operations; C2 routes each read as a pure move (answered from
today's Facts), C3 replaces the answer. Paths under `src/Kimi/Compiler/Binding/`.

| Read site (method) | Operation | Behavior change at C3 |
| --- | --- | --- |
| `AvailableConstraintFact` (22 calls in 16 files), `ProveConstraint` scans (`Constraints.cs:640`, `:642`) and its presence gate | Supply | derived, not stored; the `HasAssociatedProjection` gate goes |
| `ProveConformance` premise loop (`Contracts.cs:681`); `ResolveConformance`, `ProveConformanceConditions`, `ProveMemberConditions` Invalid walks | Supply | premise part only; verification leaves the proof in its own unit (N6) |
| `SymbolicCapability` (`Capabilities.cs:536`) | Supply (refinements of the intrinsic); Enumerate (identity members) | unspelled projections (G170) |
| `FindAssociated` (`AssociatedTypes.cs:114`), `RequirementMember` (`ContractCalls.cs:114`), `HasContractFact` (`Indexers.cs:209`), `SelectValueCall` (`CallableSelection.cs:49`, `:63`), `TryCallable` (`ValueCalls.cs:296`), `CallableBoundSignatures` (`Retention.cs:45`) | Enumerate (Contracts declaring the name) | derived candidates, spelling-independent; pending ones stay discoverable for ambiguity |
| `ArithmeticNumericDomain`, `SelectArithmetic` (`ArithmeticOperators.cs:44`, `:205`), `ValidateArithmeticDirections` (`ArithmeticDiagnostics.cs:51`, `:68`) | Enumerate (arithmetic Contracts) | arithmetic premises become rows |
| `AvailableEffectBounds` (`EffectDeclarations.cs:217`), `AvailableCallableEffects` (`CallableEffects.cs:33`), `EffectPremises` with `TyCtxt.Copy`, `Sealed`, `RecursiveBase` | Enumerate / Supply in the definition's view | derived bounds |
| `HasParameterIdentity`, `SubstituteIdentityPremises`, `IdentityPremise` (`TypeIdentity.cs:12`–`:149`), `ContractTypeCore` (`AssociatedTypes.cs:684`), `NormalizeProofConstraint`/`NormalizeProofType` (`Constraints.cs:685`, `:714`), `HasValueRole` (`ConstraintValidation.cs:673`), `ValidateContractDeclarations` (`Contracts.cs:887`), `FailConstructorAbsence` (`Structs.cs:329`) | Enumerate (identity members, no declarations) | the identity unit (OQ5); the `HasParameterIdentity` flag goes |
| `AdmittedSemantics`, `ValidateConstraintEnvironments` (`Constraints.cs:401`), `ValidateConstraintUses`, `RecordReferenceConstraint`, `ReportQualification`, `ValidateConditionalBlock` | none: input validation over DirectFacts | Resource never marks an environment Invalid |
| `DisjointConformanceFacts`, `RefutedOverlapInputs`, `ValidateAssociatedOverlapAgreement` | Supply in the composed scope | composed scopes hold inputs only |

Writers: `AddConstraintFact` stays for direct inputs only; `AddContractPremises` (4 callers), `ExpandScopeContractPremises` (6
callers) and `AddArithmeticPremises` go at C3; scope composers (`VerifyConformanceAgreement`, `ContractOverlapScope`,
`AddOverlapFacts`, `CompatibleRequirement`, `AddInheritedPremises`, `PrepareVirtual*Premises`, `InheritVirtualConstraints`) copy
inputs and substituted clauses as inputs; `DerivedFacts` and the `HasAssociatedProjection` flag go. S5's view
(`Binding.RecursiveContracts.cs`, R4-M87) must be SPEC 8.3.3's search over the supply rows with argument and identity routes, the
latter including identities with closed Types (N3; the prototype's declaration search and the reference's closure agree on all
generated Contracts), not today's refinement-and-clause walk, and inherits G180's correction. Behavior-preserving restructures
first (C2): `Binding.Contracts.cs` (6,516 tokens) premise supply to `Binding.ContractSolver.cs`; `Binding.Constraints.cs` (5,324)
facts and availability to `.Rows.cs`; `Binding.AssociatedTypes.cs` (5,047) lookup to `Binding.AssociatedLookup.cs`.

**Review additions.** The extended `order` test and the scaling workload become C3 regressions. A premise workload in
`src/Benchmark`: request counts per warm Bind and per A1/A4 analysis (milestones, library), per-request time and allocation on §7's
workloads, the diamond with a direct-parent index, time to each limit, warm zero-allocation tests, cold/warm Bind against C0,
retained capacity; a per-pass (request, view) memo stamped with the pass version (R4-M87's pattern) only on measurement. Rows C1
did not model, each a row or outside supply at C3: PrimitiveInteger → Position, `AddArithmeticPremises`, Semantics and Callable
constraints, Origin families, generic conformances seen from a value side, structured conditions. N4, N6, N7: §5.

**Expected size (N9, DECISIONS Q5).** Added: the four ContractSolver files about 15,000 tokens (prototype `Solver.cs` 20,500 with
string keys and LINQ), TypeIdentity's classes about 2,500 (`EGraph.cs` 1,700), `ConstraintProof.Resource` and the record 300.
Removed at C3 (measured on `e0f24c83`): `Binding.Contracts.cs` premise supply about 1,000 tokens (`ExpandContractPremises` 118,
`ExpandScopeContractPremises` 177, `AddContractPremises` 200, `ContractConstraint` 269, a `ProveConformance` loop 264), `Constraints`
450, `FindAssociated`'s scan 300, `TypeIdentity.cs` 550, `AddArithmeticPremises` 155, `DerivedFacts`, copy paths and guards 1,500.
Net about +15,000 tokens: C adds a responsibility Binding lacks (the supply's closure).

## 9. Decisions, departures, unverified

| Question | Decision | By |
| --- | --- | --- |
| Q1/OQ1 Resource diagnostic | one shared Binding analysis-limit code, the limit as an argument, the last free `BindingFailure` entry; also G172's inference budget and G174's fixed points | owner, 2026-10-21 |
| Q2 transport | `ConstraintProof` gains a fifth member `Resource` (§4) | planning session (N10) |
| Q3/OQ3 "depends" | per derivation; no read sets in C0–C4; G172's design decides its flow-back (prefer option (c)) | owner |
| Q4 identity classes | TypeIdentity owns them; C reads them | planning session (N10) |
| OQ5 identity scope | a separate reproduced identity unit before C3 | planning session |
| Q5/OQ2 limits | rounds and items; 4,096 and 65,536 provisional, confirmed by C3, in IMPL 21.3.2/A.4 at C3 | owner |
| OQ4 D5 freeze | where C3 shows only the proof changes and the old Emission generates correctly, the default pipeline accepts; otherwise a guard reports the located `Unsupported_Kd` until R6-check | owner |

Departures from PlanB §4: identity classes are part of selection; closed sides need the laziness rule and its indexes; class demands
and needs reach kids; chains and needs are rows (§1); verification is inside proof today (§5). **Unverified**: production cost and
time to the item limit; real-source witnesses for effect premises, Enumerate lookups, arithmetic, effect bounds, composed scopes;
G172's admissibility; mixed Owned/premise cycles (N5); Origin families; generator gaps (N1, a C3 gate: 2-parameter Contracts,
Origin families, structured conditions, deeper goals at bounds 7/9); that stability at bounds 5 and 7 gives the unbounded answer.

## 10. Review findings: rulings and evidence

| Finding | Ruling | Evidence |
| --- | --- | --- |
| B1 join-work limit order-dependent | no work limit (OQ2); J order-free by definition (§4) | `order`: 1,832 requests + 6,276 boundary runs, 0 differ (J and limit outcomes included) |
| B2 reads wrong, unused before G172 | `Reads`, read-free settlement, alternatives limit removed; frozen views settle like ordinary ones; option (c) shown with Supply | fixed case (the former "every support reads it": + Complete); adapter §3 |
| B3 work grows with the program | no up-front interning; head and first-kid indexes; one laziness rule for closed sides | §7: J 1, 11, 2, 21, 0 at n = 10…1,600 |
| B4 Resource transport | published Resource state; `ConstraintProof.Resource`; `ProveConstraint` mapping (§4) | `resource` transport with judgments and restart; Resource-state fixed cases |
| N1 generator | compound subjects added; the rest a C3 gate (§9) | |
| N2 shared rows | the reference builds rows and classifies recursion itself | 0 of 3,843 classifications differ |
| N3 recursion search | argument and identity routes, identities with closed Types included; a C4 requirement (§8) | fixed rec-arg and closed-Type cases |
| N4–N7 | contract text (§5) | N4 fixed case |
| N8 / N9 / N10–N11 / N12 | §6 / §8 / §9 / gone with B2 | |

Gaps this revision found, each fixed in the rules with a fixed case that fails before the fix: congruence through a kid; an input's
Contract in the polarity index; identity goals meet; a chain through another instance of a row; a chain through a chain (20261021
model 359); a virtual node's first kid in the open-side index and needs below a ground Type's kid as need rows (seed 7 model 225); a
bound variable meets the subject class (model 395); recursion through an identity with a closed Type (model 85). Need rows keyed by
class and head, not by first requester, fixed an order difference the `order` test found.
