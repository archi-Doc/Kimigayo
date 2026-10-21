# G170 C1: executable design of C (ContractSolver premise supply)

Unit C1 of [PlanB](../../../Proposals/2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) (BU planning session,
2026-10-21), after [C0](../C0-evidence.md). Draft data only. Branch `claude/bu-g170-c1` from origin/dev `e0f24c83`. Revised three times
after the independent review (round 1: accept with changes; round 2: R2-B1, R2-B2; round 3: R3-B1, R3-B2): §10 gives each ruling.

[proto/](proto/): model, identity classes (`EGraph.cs`), C (`Solver.cs`), judgments (`Checker.cs`), bounded reference, runner
(`dotnet run -c Release -- fixed|random [n] [seed]|order [n]|resource|cost`, probes `enum|scale|pending|limit`; `G170_ITEMS`,
`G170_REVIEW_PROFILE=params|nested`, `nested_probe.py`); [adapter/](adapter/G170C1AdapterTest.cs) runs in a scratch test assembly.

## 0. Verdict per completion condition

1. **Rule selection** (§1): every SPEC 8.3.2 supply row is selected by e-matching through static indexes keyed by conclusion heads,
   every identity row by one need-driven rule whichever side mentions the row's subject (owner, 2026-10-21): a request reads only
   rows that can meet its classes, and unrelated requests are flat in program size. A prefix selector is B.
2. **Agreement** (§2): 12,289 stable Supply queries and 1,103 enumerations on 600 models (seed 4242, one and two Contract
   parameters) and 587 nested-profile queries this round, 0 disagreements; round 2's seeds likewise; the reference is independent.
3. **Real-source witnesses** (§3): G170 at every depth whatever the spelling, G180's probes, frozen views; the corpus matches `ProveCopy`.
4. **Resource** (§4): two limits (rounds, items), order-free (1,832 requests, 6,264 boundary runs, 0 differ); a published Resource
   state per source; transport through real judgments; time to the item limit is a C3 gate with a number.
5. **Lifecycle, scheduler, contract, cost** (§5–§7): one Jacobi worklist, stratified; no read sets; C2's inputs (§8); decisions (§9).

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

**One fixpoint over needs.** A request interns only its goal's Types and its inputs; a Type enters when a fired row, a condition or
a goal names it. Every row below is selected by a need, a meeting or an existing node, never eagerly:

- **Demand (K, s, ā)**: facts `s' is K<ā'>` with `s' ≡ s`, `ā' ≡ ā` where bound (s or any āᵢ may be ⋆). Selected: K's rows whose
  conclusion subject is a variable, and those whose subject head is a head of s's class or of its virtual nodes (a static index);
  each is e-matched, arguments first (a goal's `D<Self>` binds Self: G180), then the subject; the bindings β give the antecedent
  Demand(K_α, β(Self) or ⋆, β(Ā) or ⋆); a free parameter is read off an available antecedent fact, never invented. A demand meets
  the classes holding its Contract's facts, and their arguments once the subjects are one class; a variable bound to class x and
  matched against class c makes c and x meet (`A is K0<A>` with A = i64 from the argument and subject i32).
- **Identity rows: one need-driven rule for every side** (owner, 2026-10-21: the general rule, R2-B1). Every positive identity
  side, whichever side mentions the row's subject, is in one index: (other side's head, side's head), then the heads of both sides'
  kids 0 and 1 (Any for a variable or no kid), rows sharing a side matched once. A side matching class c makes its other side a
  **virtual node** of c, never stored per row, requested only where **(a)** a pattern needs its head at c or at a kid of c's node
  (with the heads the pattern's kids 0 and 1 can have: `Self.(W1).X` and `Self.(W2).X` differ only in kid 1), **(b)** c meets
  another class, or **(c)** a node of the other side's shape exists and c has a class demand (bound expansion, one entry per row,
  waiting for an absent node). A **chain**: a side meeting c only through a virtual node makes its other side one too; only
  **chainable** sides are tried (some row's other side unifies with them), excluding an **alias** (an other side that is a
  variable its side binds: an existing class, expanded wherever a need or real node meets it, after which the side is direct) and
  the row's own other side when matching fixes nothing new (the base instance itself). Chains and deeper needs are rows; a lookup
  answers each virtual node once, keyed by its row, bindings and base.
- **A virtual node matches as a node** (R3, one rule for (a) and (b)): against a pattern, a real node's kid classes or another
  virtual node, a subterm instantiated to a class (a bound variable, a built Type) matches by its class; equal classes agree,
  different classes meet there as a kid meeting row; a free variable takes the class; a compound subterm with a free variable is
  no Type until its row binds the variable and agrees by shape, decided once built; other subterms compare by head and kids.
  Agreement requests the row; a pending kid meeting wakes the requester when it merges.
- **Meeting row (a, b)** (b): one per class pair, its requesters' tags, answered once per round when new or woken (a merge it read
  or below either class); none where neither class can gain a node (no virtual node, no head a virtual node can have, no such kid).
  Every node of one class, real or virtual, probes the other's virtual nodes through the index by its head and kid heads and
  matches each found (both ways, at heads the index gives both classes; no loop over both lists); same-head real nodes meet at
  their kids; a pair needing the meeting itself is its own row; an alias or a free-variable virtual node meets real nodes only.
  Requested by a demand for its facts' classes (above), a bound variable and its class, an identity goal's two classes, and
  Enumerate's same-declaration facts: once no row has work, those facts on the subject's class meet pairwise at their arguments
  (R2-B2: one instance per class, SPEC 8.6.1); what that requests re-enters the fixpoint.
- **Class demand (c)**: c's kids (congruence makes c depend on them), the identity goal's meeting and negated identity rows for an
  identity goal's pair. **Compound row (c)**: exact compounds whose subject e-matches c, by subject head. Static indexes (heads a
  K-fact can have per position; whether any row or input concludes `not … is K`; the heads a virtual node can have, an alias
  contributing any) skip impossible expansions and polarities; tags recomputed each round say which polarity a row serves.

**Why the selection is complete.** If the supply has `s' is K<ā'>` with `s' ≡ s`, C derives it unless it stops with Resource.
Induction on derivation height: the last row instance concludes σ[β] ≡ s; each identity fact of that congruence has smaller height
and a side in a class C holds or in a kid class of one. E-matching σ there needs σ's head and kid heads: (a) asks every side that can
match that class or a kid; chains reach sides that meet only through a virtual node (a side no other side unifies with meets none;
an alias merges its class first); a deeper need is a row; classes that must be one meet (b) at their real, virtual and kid nodes;
(c) covers identities between existing nodes; a lookup or meeting is redone when a class it read merges or gains a chain. So σ[β]
joins s and the antecedent's fact is derived by hypothesis. Filters prune only heads a class, its kids or its virtual nodes cannot
have. Evidence: the agreement runs (§2), the two-parameter generator included, and a fixed case per gap (§10).

**A syntactic-prefix selector is B**: §3's B counterexamples (an identity to another subject, a Contract-argument clause, a deep
subject through an identity, a closed-Type identity, congruence through a kid) are fixed inputs, `none` under it and `+` for C and
the reference; §3's D input is finite. **Where it does not terminate**: a polarity settles when established or when none of its rows
has work; a negative answer over a recursive Contract with growing subjects or arguments, and enumerating a closed Type's infinite
class (`It: associate Child is It; Self.(It).Value is i32; Self.(It).Value is Dd<Self>`, enumerating `i32`), end in Resource (§4).

## 2. Prototype and exhaustive bounded reference

C runs §1 in **Jacobi rounds**: a round computes its effects from the previous round's state only (lookups answered once per round
and replayed for later requesters; meeting rows answered once), applies them and restores congruence; subscriptions scan only facts
added since their last scan (all after a merge). Answers: Complete, Provisional (a pending source was selected) or Resource. The
reference applies every row to every fact forward with Types up to a depth bound, an oracle where its answer is the same at bounds 5
and 7; it builds its own rows from the declarations and classifies recursion by SPEC 8.3.3's definition (a closure from `T0 is C<P̄>`).

Generated models (3–5 Contracts with 0–1 parameters, or 0–2 in the review's two-parameter profile; associated requirements, deep,
argument and compound subjects, identity, `not`, `and`/`or`, refinement, verified, pending and conditional conformances, inferred
bindings with a frozen view): each Supply query and up to 8 reference-derived facts compared at both bounds, each Enumerate fact by
fact and class by class; harness 300 rounds, 3,000 items ([results/](results/)):

| Seed (models; oracle overflows excluded) | Stable Supply: agree / disagree / Resource | Enumerate: agree / disagree / Resource | Recursive Contracts (differ) |
| --- | --- | --- | --- |
| 4242 (400; 9) | 8,305: 8,285 / 0 / 20 | 745: 738 / 0 / 7 | 523 of 1,939 (0) |
| 4242, two parameters (200; 13) | 3,984: 3,982 / 0 / 2 | 358: 356 / 0 / 2 | 162 of 937 (2) |
| 77, nested (R3-N3: nested-projection identity clauses, different subjects' projections equal; 40, 653 queries, each its own process; 66) | 587: 576 / 0 / 11 | not run | not compared |
| Round 2 (`f550b474`): 20261021, 7 (400 each); 99 two-parameter (200) | 20,121: 20,090 / 0 / 31 | 1,812: 1,802 / 0 / 8 | 1,193 of 4,797 (1) |

No Supply query was unstable. Exclusions depend on the reference's 5 s guard and the machine's load (R3-N2): seed 4242 excluded 9
models here and on `f550b474`, 10 on an intermediate build; nested: 50 overflows and model 36's 16 queries over the 60 s budget (the
reference's; C alone ≤ 0.5 s). Classification differences (two-parameter only, no answer differs): the prototype's declaration search
misses what the closure finds; C4 classifies by SPEC 8.3.3's search (N3). Nested Resource (models 2, 39; 1–6 s): chain virtual nodes
multiply (model 39 `T is V`: V 2,636 at 3,000 items, + at 30,000 in 12 s; round 1's stored rows: V 243, 3.5 s; `f550b474` > 200 s).

Fixed cases (94, all pass, [results/fixed.txt](results/fixed.txt)): G170 depth 1–8, §3's B and D inputs, the absent-head binder,
ancestors, G180, negation, compounds, cycles, recursion routes, pending and Resource sources, the check under way, frozen views, the
round-2 and round-3 workloads at 4 and 64 Contracts, Enumerate's same-declaration facts, R3-B1's witnesses, one case per gap of §10.
`pending` (round 2, seed 4245): 0 of 2,663 Complete answers change with pending sources usable.

## 3. Real-source witnesses

`BindingSource` (adapter) builds the model from Binding's state after `Bind`, read-only (`AncestorStorage`, `ClauseStorage`,
`DirectFacts`, verified paths with `AssociatedTypes` and `InferenceEvidence`; [results/adapter-output.txt](results/adapter-output.txt)):

| Program (C0's and the 2b-S5 review's) | Binding today | C over Binding's own clauses | Reference |
| --- | --- | --- | --- |
| G170 `inspect` depth 1; 2, 4, 8 absent; 2, 4, 8 with the stronger hint before or after | Proven; Unknown; Proven | + Complete for all ten, same work with or without the hint (d8: 18 rounds, V 33) | + |
| Generated `T.Child^k` (never spelled), k = 3, 8, 16 | Unknown (C0) | + Complete (k = 16: 34 rounds, V 57) | - |
| Non-recursive `T.(Outer).Inner.(Holder).Item`, absent / hint | Unknown / Proven | + / + | + |
| G180 stale0 control; stale1 `associate X is D<Self>`; stale2 `contract C: D<Self>` | none; `UnprovenConstraint_Kd` ×2 | + ; + ; + | + |
| G180 h4: the required `T.(C).X is D<T.(C).Y>`; D's clause `T.(C).Y is E`; h6 | `UnprovenConstraint_Kd`; none; none | + ; + ; + | + |

Frozen views (D: `x() -> i32`; E: `w() -> D.(P).X`; both orders): `D.(P).X is i32` is + in E's view, none in D's own; without read
sets (OQ3 (c)) E.W's evidence in a view excluding D is none, so `E.(Q).W is i32` is none in D's view (today: +). Corpus (44 milestones
and the Kimi library: 121 scopes with input premises, 400 requests): all Complete; Copy evidence equals `ProveCopy` on all 99 (55 / 44); V ≤ 45.

## 4. Resource boundary and transport

- **Limits, per request** (owner, OQ2): **rounds** (SPEC 8.3.3's search depth) and **items** (rows, supports, identity nodes; the
  seeded state counts); 4,096 and 65,536 provisional, confirmed by C3's measurements and recorded in IMPL 21.3.2/A.4 at C3. No
  join-work and no read-alternatives limit. Resource replaces the whole answer (a nested judgment is in the closure).
- **Order-free.** Each limit reads the state after a round and rounds are Jacobi. J (reported, never a limit) counts matching steps,
  index entries, probes and every virtual-node agreement examined (R3-N1), a function of the round's starting state (subscriptions
  examine every new candidate; lookups and meeting rows are answered once per round; meetings probe both ways), plus classes merged
  and nodes over a merged class. `order` ([results/order.txt](results/order.txt)): 1,832 requests on 400 models,
  templates and Contracts shuffled, ordinary and frozen views, and 6,264 runs at each request's need and need − 1 per limit: 0
  differ in verdict, outcome, reason, rounds, V, E, J, N or peak items; Complete/Provisional at the need, Resource below it.
- **Tested values** (`resource`, [results/resource.txt](results/resource.txt)): G170 depth 8 and 64 complete at the need (depth 64:
  130 rounds, 331 items) and are Resource at need − 1; enumerating a closed Type's infinite class is Resource at every limit.
- **Time to the item limit** (R2-N2; `limit`; machine loaded): that enumeration reaches 1,000 / 3,000 / 10,000 items in 0.16 / 1.1 /
  4.6 s, J 64 k / 0.56 M / 6.2 M (about items²: rows woken by merges are re-selected in full). **C3 gate**: the production evaluator
  reaches the chosen item value within about 1 s (a warm Bind) here and on the two-parameter Resource cases (seed 4242 model 12,
  `List<i32> is K3`: 3,000 items in 28 rounds, J 1.4 M, 1.6 s here) by semi-naive deltas (a demand reads only new facts and merged
  classes, bound expansion only new or merged nodes); otherwise IMPL records the value reached in 1 s, or the owner reconsiders a work limit.
- **Published Resource state.** A source whose own check ended at a limit publishes `Resource` (beside Valid, Pending, Invalid); a
  selected row from it makes every polarity not established Resource, never absent (never Unknown or `UnprovenConstraint_Kd`).
- **Transport** (planning session, Q2): `ConstraintProof` gains a fifth member `Resource`; `ProveConstraint` maps Complete evidence to
  Proven, Refuted, Unknown or Error, Provisional to the same value not final (judged again after publication), Resource to
  `ConstraintProof.Resource`, absorbed as Error is (SPEC 8.3.2); one record per (scope, limit) at the scope owner's signature (OQ1).
  Tested: `S1 is Deep` checked at a 6-round limit publishes Resource and records it once; its uses (alone, `and`/`or`, a condition,
  its binding) are Resource and record nothing; `U is Copy` records `Unproven`; before or after the check, the records are the same.

## 5. Lifecycle and scheduler (§4.4)

- **Request-local**: identity classes, rows (demands, class demands, subscriptions, chain, need and meeting rows), supports, facts
  and the per-round lookup answers; capacity is reused; an answer (Resource included) is a function of (request, inputs).
- **Persistent answer-shaping state** (C0's census) becomes request-local or keyed by its inputs: capability results, `Normalized`
  (per frozen batch), `retentions` (Enumerate), composed scopes' `DerivedFacts` (inputs only), the three re-entry guards.
- **One scheduler**: the Jacobi worklist hosts premise rows now, capability nodes and conformance verification at C3, G172's value
  demands later; each kind keeps its transfer rule and initial value; limits count all kinds. **Stratification** (N5): a premise row
  reads only judgments final in the request, Owned's optimistic start never feeds one; a cross-kind cycle in a valid program needs
  an explicit rule or PlanB's recorded contract difference; C3 tests it.
- **Source states**: C reads a published state per declaration, conformance path and binding (Valid, Pending, Invalid, Resource),
  never runs verification; a Pending read is Provisional, never cached, re-run after publication. **The check under way** is an
  `EvidenceView` exclusion (N4): its own row supplies nothing, the answer stays Complete (SPEC 8.3.3). **Invalid** supplies nothing (N7;
  Error per PlanB §4.3.5 is C3's); moving `VerifyConformance` and `CheckClosedDeclarationConstraints` out of proof is its own unit (N6).

## 6. Representation and entry contract (C2/C3)

Files under `src/Kimi/Compiler/Binding/`, each within 5,000 tokens: `Binding.ContractSolver.cs` (entry file with this contract; entry
operations, request driver), `.Rows.cs` (indexes over existing storage: conclusion heads, the identity side index, chainable sides,
bound-expansion shapes, static indexes), `.Demands.cs` (rows, subscriptions, need and meeting rows, rounds, tags, limits),
`.Matching.cs` (e-matching, virtual nodes, chains, meetings); identity classes are TypeIdentity's (Q4; its own unit first: OQ5).

- `Supply(BoundConstraint proposition, EvidenceView view) -> Evidence { bool Positive, bool Negative, Completion }`.
- `Enumerate(BoundType subject, EvidenceView view, declarations) -> SubjectEvidence { facts on the subject's class (positive or
  negated, available or only discoverable), identity members, Completion }`; `declarations`: the consumer's candidates (identity: none, N8).
- `EvidenceView { BindingScope Premises, SourceState (provisional pass or final), FrozenDefinition (null for ordinary proof),
  Inadmissible (the inference owner's published set), UnderCheck (the conformance or declaration whose own check is running) }`.
- `Completion = Complete | Provisional | Resource(limit, count | source)`. No read sets (B2, OQ3): "depends" is per derivation and
  G172's design decides how a definition's value reaches another definition's view (prefer option (c), §3).

Invariants: Jacobi rounds over request-local rows; answers, counts and limit outcomes functions of (request, inputs); indexes prune
only impossible heads; identity never decomposed; nothing guessed; inadmissible bindings never read; Pending never a cached negative;
Resource never absent. Prohibitions: no Binding writes, syntax-triggered supply, `DerivedFacts`, fallback fact scan, mode or bool
parameter, cross-request semantic cache, second scheduler or read sets.

## 7. Cost

A reference implementation (string keys, LINQ), not the production evaluator; no speed claim ([results/cost.txt](results/cost.txt);
median of 15 after 5; machine loaded by other sessions; V, J and time at the listed sizes):

| Workload | Rounds | V | J | Time | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| G170 Copy, depth 1 / 8 / 64 (as in round 1) | 4 / 18 / 130 | 8 / 29 / 197 | 25 / 466 / 61,030 | 0.05 / 0.6 / 29 ms | 44 KB / 0.4 / 17 MB |
| Wide: 8 / 32 / 128 Contracts on T | 2 | 6 | 46 / 166 / 646 | 26 / 58 / 176 µs | 48 / 114 / 395 KB |
| Diamond refinements, height 4 / 16 / 64 | 2 | 74 / 650 / 8,714 | 126 / 1,230 / 17,166 | 0.06 / 0.5 / 15 ms | 0.2 / 1.3 / 17 MB |

**Program-size scaling.** B3's n conformances binding `X = i32` (`T is Holder`); R2-B1's n Contracts declaring `associate X is Self`
(selfid) or `associate X is List<Self>` with `Self.(Wi).Y is Option<Self.(Wi).X>` (open, `U is W1`); R3-B2's `Ki<A>: associate X is
A` (palias) and `Wi: Self is Self.(Wi).X.(Wi).X` (deep2), with `V is Copy`:

| Request (n = 10, 400, 1,600; B3 also 100) | Answer | Rounds | V | J | Review (round 2) V; J |
| --- | --- | ---: | ---: | ---: | --- |
| B3: `List<i32> is Copy`, `T.(Holder).Item is Copy`; `i32 is Copy`, `S1.(Pp).X is Copy`, `i32 is Pp` | none, +; +, +, none | 1, 2; 1, 2, 0 | 3, 6; 2, 4, 2 | 1, 11; 2, 21, 0 | flat (B3 resolved) |
| selfid `T is Copy` | none | 0 | 2 | 0 | 23 / 803 / 3,203; 135 / 5,205 / 20,805 |
| selfid `T.(Holder).Item is Copy` | + | 2 | 6 | 11 | 67 / 2,407 / 9,607 |
| open `List<T> is Copy` | none | 1 | 3 | 1 | 24 / 804 / 3,204 |
| open `U.(W1).Y is Option<List<U>>` | + | 2 | 10 | 104 | J 2,602 / 101,272 / 404,872 |
| selfid with `T is W3`: `T.(W3).X is T` (related; a meeting) | + | 2 | 7 | 420 / 12,510 / 49,710 (0.2 / 8 / 17 ms) | round 2, pairs uncounted: 194 / 2,534 / 9,734 |
| palias, deep2, selfid with `V is Copy`: `T is Copy` (a meeting) | none | 1 | 4 | 145 / 5,605 / 22,405 (0.1 / 2–3 / 8–11 ms) | 0.2–0.5 / 32–43 / 506–547 ms, 2n² unifications |
| palias `T.(K3<i32>).X is i32`; deep2 `T.(Holder).Item is Copy` | +, + | 2 | 10, 8 | 502 / 14,152 / 56,152; 156 / 5,616 / 22,416 | |

Every request reads the same rows (V) whatever n. J counts every probe and agreement (R3-N1): a meeting of two classes every variable
side reaches probes each such side's virtual node once each way (J ≈ 14 n, linear time) and requests none. The diamond is quadratic.
Adapter (loaded): `ProveCopy` of the G170 value 4–17 µs against 0.14–1.2 ms; the corpus's 400 requests 99 ms beside 1.6 s of rebinds.

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

Writers: `AddConstraintFact` stays for direct inputs only; `AddContractPremises` (4 callers), `ExpandScopeContractPremises` (6) and
`AddArithmeticPremises` go at C3; scope composers (`VerifyConformanceAgreement`, `ContractOverlapScope`, `AddOverlapFacts`,
`CompatibleRequirement`, `AddInheritedPremises`, `PrepareVirtual*Premises`, `InheritVirtualConstraints`) copy inputs and substituted
clauses as inputs; `DerivedFacts` and the `HasAssociatedProjection` flag go. S5's view (`Binding.RecursiveContracts.cs`, R4-M87) must
be SPEC 8.3.3's search over the supply rows with argument and identity routes, closed Types included (N3), and inherits G180's
correction. Behavior-preserving restructures first (C2): `Binding.Contracts.cs` (6,516 tokens) premise supply to
`Binding.ContractSolver.cs`; `Binding.Constraints.cs` (5,324) to `.Rows.cs`; `Binding.AssociatedTypes.cs` (5,047) lookup to `Binding.AssociatedLookup.cs`.

**Review additions.** `order`, the scaling workloads, the generated profiles' slow cases and §4's curve become C3 regressions; a
`src/Benchmark` premise workload (requests per warm Bind and A1/A4 analysis, per-request time and allocation, time to each limit, warm
zero-allocation, cold/warm Bind against C0, retained capacity); a per-pass memo only on measurement. Rows C1 did not model, each a
row or outside supply at C3: PrimitiveInteger → Position, `AddArithmeticPremises`, Semantics and Callable constraints, Origin
families, generic value-side conformances, structured conditions.

**Expected size (N9, DECISIONS Q5).** Added: the four ContractSolver files about 18,000 tokens (prototype `Solver.cs` 24,700
with string keys and LINQ; 20,700 in round 1), TypeIdentity's classes about 2,500, `ConstraintProof.Resource` and the record 300.
Removed at C3 (measured on `e0f24c83`): `Binding.Contracts.cs` premise supply about 1,000 tokens, `Constraints` 450, `FindAssociated`'s
scan 300, `TypeIdentity.cs` 550, `AddArithmeticPremises` 155, `DerivedFacts`, copy paths and guards 1,500. Net about +18,000 tokens:
C adds a responsibility Binding lacks (the supply's closure).

## 9. Decisions, departures, unverified

| Question | Decision | By |
| --- | --- | --- |
| Q1/OQ1 Resource diagnostic | one shared Binding analysis-limit code, the limit as an argument, the last free `BindingFailure` entry; also G172's inference budget and G174's fixed points | owner, 2026-10-21 |
| R2-B1 identity selection | one need-driven rule for every identity side (§1), not a whitelist or a hold | owner, 2026-10-21 (「一般規則 (推奨)」) |
| Q2 transport | `ConstraintProof` gains a fifth member `Resource` (§4) | planning session (N10) |
| Q3/OQ3 "depends" | per derivation; no read sets in C0–C4; G172's design decides its flow-back (prefer option (c)) | owner |
| Q4 identity classes; OQ5 scope | TypeIdentity owns them, C reads them; a separate reproduced identity unit before C3 | planning session |
| Q5/OQ2 limits | rounds and items; 4,096 and 65,536 provisional, confirmed by C3 (§4's gate), in IMPL 21.3.2/A.4 at C3 | owner |
| OQ4 D5 freeze | where C3 shows only the proof changes and the old Emission generates correctly, the default pipeline accepts; otherwise a guard reports the located `Unsupported_Kd` until R6-check | owner |

Departures from PlanB §4: identity classes are part of selection; every identity side is lazy under one rule; chains, needs,
meetings and bound expansions are rows (§1); verification is inside proof today (§5). **Unverified**: production cost and item-limit
time (C3 gates); witnesses for effect premises, Enumerate lookups, arithmetic, composed scopes; G172's admissibility; mixed
Owned/premise cycles (N5); Origin families, structured conditions, deeper goals (C3 gate); the J of meetings variable sides reach
(§7); kid meetings of unrelated classes (model 185 `List<i32> is Cp`: 17 rounds, V 781, 1.7 s; `f550b474` 1); chain virtual nodes
multiplying with nested-projection identity rows (§2: Resource where round 1's stored rows answer; open).

## 10. Review findings: rulings and evidence

| Finding | Ruling | Evidence |
| --- | --- | --- |
| R3-B1 matching against a virtual node compares bindings, not classes | a virtual node matches as a node, modulo classes (§1): a bound subterm by its class, different classes meet as kid meeting rows, in needs (a) and meetings (b) alike | fixed cases (an involution law twice, a nested identity side, a bound pattern, a free Contract argument), each `none` on `f550b474`, `+` here and in the reference ([results/witnesses.txt](results/witnesses.txt)) |
| R3-B2 a self-kid exclusion over a quadratic join | every node of one class probes the other's virtual nodes through the index, both ways, at heads the index gives both; `SelfKids`, `KidFilter` and the tie rule deleted | §7: palias, deep2 and selfid with `V is Copy` linear, V 4 at every n |
| R3-N1 J; R3-N2 oracle guard; R3-N3 generator | J counts every probe and agreement (§4); exclusions stated with the machine's state (§2); a nested profile and the palias/deep2 workloads (C3 regressions) | §2, §7 |
| Round 2: R2-B1, R2-B2, R2-N1–N5 | one need-driven rule for every identity side (owner); Enumerate's same-declaration facts meet; two-parameter profile; item-limit time a C3 gate; cost and N7 to C3 | §7; fixed cases; §2; §4 |
| Round 1: B1–B4, N1–N12 | no work limit, J order-free; no read sets; B3 flat; published Resource state and `ConstraintProof.Resource`; N1–N12 as recorded | `order` (§4); §3; §7; `resource` transport |

Gaps this redo found: stored chains answered lookups without the lookup's kid heads (now filtered like index rows); a join at every
head read the open program's n chains (V 199 at n = 64; at heads the index gives both classes: 10); a compound kid with a free
Contract argument met the meeting itself (now agreed by shape until built); a lookup answered a chain once per path that found it
(two-parameter model 185, `List<i32> is Cp`: J 307 M, 54 s; as a set: J 1.6 M, 1.7 s).
