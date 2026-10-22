# G170 C1: executable design of C (ContractSolver premise supply)

Unit C1 of [PlanB](../../../Proposals/2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md), after [C0](../C0-evidence.md). Draft data only. Branch `claude/bu-g170-c1` from
origin/dev `e0f24c83`; round 5 on `claude/g170-c1-perf` (track P). Revised after each independent review (rounds 1–4: R2-B1, R2-B2, R3-B1, R3-B2, R4-B1, R4-B2; round 5: the planning session's
two-track design under the owner's spec change of 2026-10-22, [Type Identity by Structural Decomposition B](../../../Proposals/2026-10-22%20Type%20Identity%20by%20Structural%20Decomposition%20B.md),
below "B", and its accepted addendum `g170-c1-r5-specB.md`; the final review's round 1: B1–B3, N1, N3, N5); §10 gives each ruling. [proto/](proto/): model, identity structure (`EGraph.cs`), shapes
(`Shapes.cs`), C (`Solver.cs`), judgments (`Checker.cs`), bounded reference, runner (`dotnet run -c Release -- fixed|random [n] [seed]|order [n]|resource|cost`, probes
`enum|scale|time|pending|limit|profile|trace`; `G170_ITEMS`, `G170_THREADS`, `G170_REVIEW_PROFILE=params|nested|structural`, `nested_probe.py`); [adapter/](adapter/G170C1AdapterTest.cs) runs in a
scratch test assembly.

## 0. Verdict per completion condition

1. **Rule selection** (§1): every SPEC 8.3.2 supply row is selected by e-matching through static indexes keyed by conclusion heads, every identity row by one need-driven rule whichever side mentions
   its subject (owner), at the top or at a constructor kid position (B); a request reads only live rows that can meet its classes.
2. **Agreement** (§2): 29,068 stable Supply queries and 2,526 enumerations on 1,600 generated models (seeds 4242 and 20261022; one, two and structural profiles) and 977 nested-profile queries, 0
   disagreements; the reference is independent and implements B.
3. **Real-source witnesses** (§3): G170 at every depth whatever the spelling, G180's probes, frozen views; the corpus matches `ProveCopy`.
4. **Resource** (§4): two limits (rounds, items), order-free (4,088 requests, 12,916 boundary runs, 72 merge-every-round runs: 0 differ); a published Resource state per source; transport through real
   judgments; B 3.10's rule at a limit.
5. **Lifecycle, scheduler, contract, cost** (§5–§7): one Jacobi worklist, stratified; no read sets; C2's inputs (§8); decisions (§9). Not met: seed 4242 two-parameter model 12 over 2 s (§7, a C3
   gate); the item-limit curve (§4: 0.17 / 1.0–1.1 / 4.8–5.2 s against the brief's 0.16 / 1.1 / 4.6 s, equal J). Open (§7): shared-live-n's superlinear time; the structure need over an infinite Type
   universe (seed 78 model 2).

## 1. Rule selection (§4.3 step 2)

**Rows are existing clauses** (no second clause language), conclusion ⇐ antecedent, variables Self and the Contract's own parameters: **Input**, a scope chain's `DirectFacts`, conjunct-split, exact
compounds kept as written ⇐ none (seeded); **Refinement**, `Self is D<b̄>` for each ancestor `D<b̄>` of K (`AncestorStorage`; Self may occur in b̄: G180) ⇐ `Self is K<Ā>`; **Clause**, each conjunct of
each dependent clause of K (`ClauseStorage`, associated requirements included) ⇐ `Self is K<Ā>`; **Conformance**, `S<V̄> is K<ā>` of a verified path (`BoundConformancePath`; never for a recursive K,
SPEC 8.3.3) ⇐ its conditions; **Binding**, `S<V̄>.(K).X is τ`, a completed binding read through the view ⇐ its conditions. A row's source state is its declaration's (a refinement row: the worst along
its path), its conformance's or its binding's (§4). Identity is not a row: supplied identity facts are unioned into request-local classes closed under upward congruence and, under B 3.3, under
decomposition of constructed Types — one constructor (Prim/Con node) per class; constructors of different heads in one class make it **conflicting**, a Type identical to its own proper part through
constructor kids **cyclic** (B 3.6, 3.10: premise errors) — never decomposing projections or Contract arguments (B 3.5); every match is modulo those classes. Disjunction supplies nothing; `not` is a
polarity of the same rows. **Refuted** (B 3.3, 3.9) is owned where the negated facts live: a negated identity fact between two classes, a conflicting class, constructors of different heads, a Refuted
kid pair, one class a proper part of the other. **One fixpoint over needs.** A request interns only its goal's Types and its inputs; a Type enters when a fired row, a condition or a goal names it.
Every row below is selected by a need, a meeting or an existing node, never eagerly:

- **Demand (K, s, ā)**: facts `s' is K<ā'>` with `s' ≡ s`, `ā' ≡ ā` where bound (s or any āᵢ may be ⋆). Selected: K's rows whose conclusion subject is a variable, and those whose subject head is in
  `HeadsFor` of s's class; each is e-matched, arguments first (a goal's `D<Self>` binds Self: G180), then the subject; the bindings β give the antecedent Demand(K_α, β(Self) or ⋆, β(Ā) or ⋆); a free
  parameter is read off an available antecedent fact, never invented. A demand meets the classes holding its Contract's facts, and their arguments once the subjects are one class; a variable bound to
  class x and matched against class c makes c and x meet.
- **Identity rows: one need-driven rule for every side** (owner, 2026-10-21: the general rule, R2-B1), stated as nine rules (round 5):
  1. **A promise is a shape.** A side matching class c makes its other side, under the bindings β the match established, a **promise** of c, identified by (c, shape): a term's shape under β is its
     class for a bound variable or an interned instantiated subterm, a shape variable numbered by first occurrence for a free variable, a shape node (head, kids) otherwise; hash-consed per request
     (`ShapeTable`). The rows producing it, the chains producing it (the side's sub-pattern that met a base promise, the base's identity (class, shape), the bindings) and, under rule 8, the parents'
     promises it decomposes are its **members**: however many produce one shape at c, it is one promise, found once per lookup, agreed once per pair, expanded as a whole. A promise whose shape is an
     existing class has no head of its own (an alias, found by every need at its class); a chain's base is an identity, re-resolved to the round's promise at that (class, shape) when forwarded, never
     a snapshot; stored chain promises are re-keyed by canonical (class, shape) after every round (`ShapeTable.Canon`). A promise's index key is its shape's head (Any for an existing class or a bare
     variable; a decomposed promise: its own shape's head, not its producer's).
  2. **A promise matches as a node: the top by head, every kid by the kid rule.** Heads must be equal at the top only; a bare shape variable matches every need at its class. Per kid: two classes agree
     when equal and meet otherwise (a meeting row; the requester is woken when they merge, and the row is requested only when no kid meeting is pending); a shape variable takes a class it meets (D3)
     and records the head of a shape node it meets for the static prune on the antecedent's position heads; a projection-headed node or pattern subterm is abstract (B 3.2) and decides nothing until
     built. A chain forwards to its base's rows under the chain's pattern, one level, whatever the requester's pattern.
  3. **Filters read classes only, through one head function.** `HeadsFor(c)` — the heads of c's nodes, the heads its promises can have and, through rule 8, its parents' promises' kid heads — is read
     by every filter, index and candidate selection whose match expands promises (demand and compound candidates, rule 8 up, the join's heads, every kid filter); a key over existing nodes (the bound
     expansion, negated sides: their matches read real nodes) uses the nodes' heads. A pattern kid that is not a Type yet contributes Any; a ground kid not interned contributes its own head and that
     head's promises. The static trie keys over the rows' own terms stay (matched only against class-derived filters).
  4. **Dead rows are not rows.** A row is live when its antecedent Contract can have a fact (`anyHead` or a non-empty `positionHeads` at its position 0, a fixpoint over live rows; inputs and
     conformances are always live). Dead rows enter no index.
  5. **Nothing else changes**: needs (a: a pattern needs a promise's head at c or at a kid of c's node), meetings (b), bound expansion (c: a node of the other side's shape exists), chains one level
     per round through **chainable** sides (a `Trivial` side's own promise excluded as the base itself), need rows beyond the chain-detection depth (tagged effects of their round), Jacobi rounds, the
     two limits, tags, source states and Enumerate's canonicalization. An expansion (a promise against a pattern, per sign) and a lookup are functions of the round's starting state, answered once per
     round and replayed for later requesters, who join the meeting rows and subscriptions made (D1: a subscription is identified by what it subscribes to, its requesters are its owners; D2). Every
     stored key — chain promises, expansions, a subscription's fired facts — is re-keyed by canonical class, shape and fact after every round (`Rekey`). **One member expansion** (`Expand`: the
     agreement's meets or its rows under the bindings the pattern fixes, its chains forwarded, its producers under the lifted pattern) serves every site — a pattern need, a node meeting, the join, the
     structure need; a forwarded chain reaches its base's rows and producers, one level.
  6. **The structure decomposes** (B 3.3; D6): merging two classes unions the kids of their same-head constructors pairwise (queued like congruence; the two nodes become one, and a node whose key
     another node owns leaves its class and its kids' use lists and owns no key: one node per Type, a P decision, §9) and marks different heads conflicting. Projections, Contract arguments and
     references (D5) are never decomposed.
  7. **Matching unifies against constructors** (B 3.3, 3.9; D7): heads are compared only where both sides are constructors (Prim/Con; a Ref against a Ref, D5). Two classes never agree when either is
     conflicting or their established constructors differ in head (the top-level head test; the full Refuted relation is for answers); a constructor shape node against a class recurses into the
     class's constructor of its head, is refused by another head, is deferred when the class has none; two constructor nodes recurse on equal heads and never agree on different ones. Refused pairs
     form no meeting row; a ground Type stops against a Refused class; pruning reads established constructors only, never promises.
  8. **Promises and demands decompose with the structure** (B 3.3; D8). Down: a promise F<s̄> of a class p whose constructor is F<…c at i…> is a promise s_i of c (its producer the parent's promise at
     position i; constructor uses only; the parent's promises are the round's top-level lookup at (p, F) whatever the nesting, a lookup asking itself through a cycle answering nothing); expanding it
     expands the producer against the pattern lifted through the path (fresh shape variables at the other positions); rule 3's head sets add, per constructor use, the kid heads of the other sides with
     that head. Up: a class demand requests every live identity row whose constructor side has a kid pattern, at any depth under Con heads, e-matching a member of the class (a variable kid: the class
     itself), with the kid match's bindings, once per (row, class, bindings).
  9. **The structure need** (B 3.9; D9): the negative polarity of an identity goal requests every promise at each of its two classes (head Any, agreed with a bare variable) and, recursively, at their
     constructors' kid classes, each of which carries the goal's negated polarity so negated identity rows are selected at each kid pair (a negated row is requested with the bindings its side's match
     gives; the antecedent binds the rest and `Request` gates the firing); a merge wakes the requester. The goal's meeting row stays.
- **Meeting row (a, b)** (b): one per class pair, its requesters' tags, answered once per round when new or woken (a merge it read or below either class); none where neither class can gain a node or
  the pair is Refused. Each real node of one class probes the other's promises by its head and kid heads and agrees with each found; the two classes' promises **join by unification through an index**,
  once per class pair: at each head the index gives both, both lookups without kid filters (the join's work is a function of the pair); promises are indexed per kid position by class (a shape variable
  or node is wild), every pair of distinct classes meets per position unless Refused, a promise's partners are the other side's promises classed as it is or wild at each of its classed positions (the
  smallest list iterated), and each pair is expanded once by the one member expansion (rule 5; the partner's shape is the pattern) — equal to the pairwise agreement, bounded by distinct classes² per
  position. Same-head real nodes meet at their kids; a pair needing the meeting itself is its own row; alias and bare-variable promises meet real nodes only. Requested by a demand for its facts'
  classes, a bound variable and its class, an identity goal's two classes, and Enumerate's same-declaration facts (R2-B2: one instance per class, SPEC 8.6.1).
- **Class demand (c)**: c's kids (congruence makes c depend on them), rule 8 up, the identity goal's meeting, negated identity rows and the structure need (rule 9) for a class carrying a goal's
  negated polarity. **Compound row (c)**: exact compounds whose subject e-matches c. Static indexes (heads a K-fact can have per position, negated conclusions per K, the heads a promise can have,
  constructor kid heads, constructor sides by kid head) skip impossible expansions and polarities; tags recomputed each round say which polarity a row serves.

**Why the selection is complete.** If the supply has `s' is K<ā'>` with `s' ≡ s`, C derives it unless it stops with Resource: by induction on derivation height, the last row instance concludes σ[β] ≡
s and each identity fact of that congruence has a side in a class, in a kid class of one or (B 3.3) in a constructor kid position of one; e-matching σ there needs its head and kid heads, which (a)
asks of every side that can match that class or a kid (chains reach sides that meet only through a promise; a deeper need is a row), (b) meets the classes that must be one at their real, promised and
kid nodes, (c) covers identities between existing nodes, rule 8 gives a parent's promise to its kids and asks the rows whose constructor kid matches, and a lookup or meeting is redone when a class it
read merges or gains a chain. Filters and the head test prune only pairs that can never be one Type (rules 3, 7), never a kid that is not a Type yet; a dead row concludes nothing (rule 4); Refuted is
decided from positive promises and negated facts alike at every class the negative polarity reaches (rule 9). Evidence: §2, and a fixed case per gap (§10). **A syntactic-prefix selector is B** (§3's B
counterexamples: `none` under it, `+` for C and the reference; §3's D input is finite). **Where it does not terminate**: a polarity settles when established or when none of its rows has work; a
negative answer over a recursive Contract with growing subjects or arguments (an infinite Type universe: `associate Y is K2` with K2's own clauses on `Self.(K2).Y`, seed 78 model 2, §7) and
enumerating a closed Type's infinite class (`It: associate Child is It; Self.(It).Value is i32; Self.(It).Value is Dd<Self>`, enumerating `i32`) end in Resource (§4).

## 2. Prototype and exhaustive bounded reference

C runs §1 in **Jacobi rounds**: a round computes its effects from the previous round's state only (lookups and expansions answered once per round and replayed; meeting rows answered once), applies
them and restores congruence and decomposition; subscriptions scan only facts added since their last scan (all after a merge). Answers: Complete, Provisional (a pending source was selected) or
Resource. The reference applies every row to every fact forward with Types up to a depth bound, an oracle where its answer is the same at bounds 5 and 7; it decomposes like B, reports
`Conflict`/`Cyclic` (`!`) and answers `not` identity by the same Refuted over its own atoms. **Declaration-error models** (B 3.10): a generated model whose reference has a conflict or cycle at either
bound is counted and not compared (B 3.7's obligation failures fall under it); fixed cases cover the conflicts and cycles a request reaches. Generated models (3–5 Contracts with 0–1 parameters, 0–2 in
the two-parameter profile; associated requirements, deep, argument and compound subjects, identity, `not`, `and`/`or`, refinement, verified, pending and conditional conformances, inferred bindings
with a frozen view; the **structural profile** adds a constructed Type on the T-projection's other side — only where the projection's declared requirement is not itself a constructed Type — and
identity goals with a constructed side): each Supply query and up to 8 reference-derived facts compared at both bounds, each Enumerate fact by fact and class by class; harness 300 rounds, 3,000 items
([results/](results/)):

| Seed (models; oracle overflows; declaration errors) | Stable Supply: agree / disagree / Resource | Enumerate: agree / disagree / Resource |
| --- | --- | --- |
| 4242 (400; 9; 20) | 7,745 / 0 / 68 | 701 / 0 / 6 |
| 20261022 (400; 7; 23) | 7,650 / 0 / 119 | 671 / 0 / 17 |
| 4242, two parameters (200; 12; 16) | 3,640 / 0 / 35 | 326 / 0 / 4 |
| 20261022, two parameters (200; 14; 13) | 3,502 / 0 / 53 | 323 / 0 / 4 |
| 4242, structural (200; 6; 70) | 2,949 / 0 / 50 | 227 / 0 / 5 |
| 20261022, structural (200; 8; 64) | 3,132 / 0 / 125 | 234 / 0 / 8 |
| nested, each query its own process: seed 77 (40 models, 653 queries; 50 overflows, 16 over the 60 s budget, 34 declaration-error queries of 2 models); seed 78 (20, 324; 17) | 553 / 0 / 0; 307 / 0 / 0 | not run |

No Supply query was unstable. The seed-4242 rows and the nested probes are the final build's; the seed-20261022 rows are the P2 build's (9c2325e8f, before the redo), not re-run. Exclusions depend on
the reference's 5 s guard and the machine's load (R3-N2). The reference's answers changed under B wherever decomposition applies, so the counts are re-based, not reused; the default, two-parameter and
nested profiles generate the same models as before. Nested model 39 `T is V`, Resource at 3,000 items on `152dda62`, is `+ Complete` at 300 items here (V 211). Fixed cases (153, all pass,
[results/fixed.txt](results/fixed.txt)): rounds 1–4's (G170 depth 1–8, §3's B and D inputs, G180, negation, compounds, cycles, recursion, pending and Resource sources, the check under way, frozen
views, the 4- and 64-Contract workloads, Enumerate's same-declaration facts, the review witnesses), round 5's (shared-n, shared-pos, shared-live-n, the wildcard join, a shape variable at two
positions, m41, the kid meetings) and B's 22 (decomposition, conflicts, cycles, Refuted at kids and components, projection sides, negated rows, limit-keeps-proven, open-live-n) and the final review's
witnesses (w2b-retire, w13a, w13b, w15, w3). Three old cases change meaning under B and are `none C` with the reason beside each (a binding against its requirement, B 3.7; `Opt<B0> is List<i32>`
conflicts: shape5, shape7), with valid R4-B2 witnesses added. `pending` (seed 4245): Complete 2,629 (pending would change 0); Provisional 123 (pending changes 19).

## 3. Real-source witnesses

`BindingSource` (adapter) builds the model from Binding's state after `Bind`, read-only (`AncestorStorage`, `ClauseStorage`, `DirectFacts`, verified paths with `AssociatedTypes` and
`InferenceEvidence`; [results/adapter-output.txt](results/adapter-output.txt)); Binding today → C over Binding's own clauses → the reference: G170 `inspect` depth 1; 2, 4, 8 absent; 2, 4, 8 with the
stronger hint before or after: Proven; Unknown; Proven → + Complete for all ten, same work with or without the hint (d8: 18 rounds, V 29) → +. Generated `T.Child^k` (never spelled), k = 3, 8, 16:
Unknown (C0) → + Complete (k = 16: 34 rounds, V 53) → -. Non-recursive `T.(Outer).Inner.(Holder).Item`, absent / hint: Unknown / Proven → + / + → +. G180 stale0 control; stale1 `associate X is
D<Self>`; stale2 `contract C: D<Self>`: none; `UnprovenConstraint_Kd` ×2 → + ; + ; + → +. G180 h4: the required `T.(C).X is D<T.(C).Y>`; D's clause `T.(C).Y is E`; h6: `UnprovenConstraint_Kd`; none;
none → + ; + ; + → +. Frozen views (D: `x() -> i32`; E: `w() -> D.(P).X`; both orders): `D.(P).X is i32` is + in E's view, none in D's own; without read sets (OQ3 (c)) E.W's evidence in a view
excluding D is none, so `E.(Q).W is i32` is none in D's view (today: +). Corpus (44 milestones and the Kimi library: 121 scopes, 400 requests): all Complete; Copy evidence equals `ProveCopy` on all 99
(55 / 44); V ≤ 34.

## 4. Resource boundary and transport

- **Limits, per request** (owner, OQ2): **rounds** (SPEC 8.3.3's search depth) and **items** (rows — demands, class demands, subscriptions, need and meeting rows, stored chain promises (one per
  (class, shape); members are not items) —, supports, identity nodes; the seeded state counts); 4,096 and 65,536 provisional (C3 confirms them, IMPL 21.3.2/A.4). No join-work or read-alternatives
  limit. **At a limit** (B 3.10, D9): the polarities established at the stop stand in the answer; `Checker.Judge` maps Resource with the positive polarity established to Proven (both: Error),
  everything else to `Proof.Resource` — an established Refuted does not stand (more analysis could turn it into Error); the limit's record is written as before. Declaration validation (B 3.10: a
  premise error reported once at the declaration, never at a use) is not the request's: C3's input (§8).
- **Order-free.** Each limit reads the state after a round and rounds are Jacobi. J (reported, never a limit) counts matching steps, index entries, probes, agreements, member requests, join steps,
  expansions computed (replays add nothing), classes merged and nodes over a merged class: a function of the round's starting state. `order` ([results/order.txt](results/order.txt)): 1,816 requests on
  400 models (seed 4242), 440 two-parameter and 444 structural requests (100 models each), templates and Contracts shuffled, ordinary and frozen views, 5,734 + 1,430 + 1,380 runs at each request's
  need and need − 1 per limit, plus 3 × 30 shuffled runs of the unbounded enumeration at 300 items, G170 depth 8, nested 39 at 3,000 and 300 items and the fired-set witness (a merge every round,
  chains stored before merges): 0 differ in verdict, outcome, reason, rounds, V, E, J, N or peak items (seed 20261021: 1,832 requests, 0 differ, on the P2 build). The five causes this round found and
  removed are in §10.
- **Tested values** (`resource`, [results/resource.txt](results/resource.txt)): G170 depth 8 and 64 complete at the need and are Resource at need − 1; enumerating a closed Type's infinite class is
  Resource at every limit; limit-keeps-proven (seed 20261022 model 312, 5,438 items needed) is Proven at 3,000 items with the limit's record. **Time to the item limit** (R2-N2; `limit`, three runs
  interleaved with `152dda62`'s build, load 2–13 %, [results/limit.txt](results/limit.txt)): 0.17 / 1.0–1.1 / 4.8–5.2 s to 1,000 / 3,000 / 10,000 items, J 63,858 / 564,267 / 6,214,809 (`152dda62`:
  0.16 / 1.2 / 4.6–4.7 s; J 63,767 / 563,994 / 6,213,900; the same rounds, V, E, N). The brief's 0.16 / 1.1 / 4.6 s is unmet at 1,000 and 10,000 items by this build and at 3,000 by the base: a failed
  (marginal) criterion at equal J. **C3 gate**: the production evaluator reaches the chosen item value within about 1 s (a warm Bind). §7's attribution says where the time goes; the open item: rows
  woken by a merge re-examine every candidate (`Scan` after a merge, `SelectDemand`'s re-match, every node pair in a meeting, `Rekey` over every index). A semi-naive device (a failed candidate's
  reason as the classes it read, popped when one merges or gains a chain; facts indexed by (declaration, subject root) for `Scan`) is the proposed change for C3, its memory to be stated against the
  allocation criterion; no part of it is in C1.
- **Published Resource state.** A source whose own check ended at a limit publishes `Resource` (beside Valid, Pending, Invalid); a selected row from it makes every polarity not established Resource,
  never absent (never Unknown or `UnprovenConstraint_Kd`). **Transport** (planning session, Q2): `ConstraintProof` gains a fifth member `Resource`; `ProveConstraint` maps Complete evidence to Proven,
  Refuted, Unknown or Error, Provisional to the same value not final (judged again after publication), Resource to `ConstraintProof.Resource`, absorbed as Error is (SPEC 8.3.2); one record per (scope,
  limit) at the scope owner's signature (OQ1). Tested: `S1 is Deep` at a 6-round limit publishes Resource, recorded once; its uses are Resource and record nothing; `U is Copy` records `Unproven`; the
  same before or after the check.

## 5. Lifecycle and scheduler (§4.4)

- **Request-local**: identity classes, shapes, rows (demands, class demands, subscriptions, chain promises, need and meeting rows), supports, facts and the per-round lookup and expansion answers;
  capacity is reused; an answer (Resource included) is a function of (request, inputs). C0's persistent answer-shaping state (capability results, `Normalized`, `retentions`, composed scopes'
  `DerivedFacts`, re-entry guards) becomes request-local or keyed by its inputs.
- **One scheduler**: the Jacobi worklist hosts premise rows now, capability nodes and conformance verification at C3, G172's value demands later; each kind keeps its transfer rule and initial value;
  limits count all kinds. **Stratification** (N5): a premise row reads only judgments final in the request, Owned's optimistic start never feeds one; a cross-kind cycle in a valid program needs an
  explicit rule or PlanB's recorded contract difference; C3 tests it.
- **Source states**: C reads a published state per declaration, conformance path and binding (Valid, Pending, Invalid, Resource), never runs verification; a Pending read is Provisional, never cached,
  re-run after publication. **The check under way** is an `EvidenceView` exclusion (N4): its own row supplies nothing, the answer stays Complete (SPEC 8.3.3). **Invalid** supplies nothing (N7; Error
  per PlanB §4.3.5 is C3's); moving `VerifyConformance` and `CheckClosedDeclarationConstraints` out of proof is its own unit (N6).

## 6. Representation and entry contract (C2/C3)

Files under `src/Kimi/Compiler/Binding/`, each within 5,000 tokens: `Binding.ContractSolver.cs` (entry file with this contract, entry operations, request driver), `.Rows.cs` (indexes over existing
storage: heads, identity sides, chainable sides, constructor kids, liveness), `.Demands.cs` (rows, rounds, tags, limits), `.Matching.cs` (e-matching, shapes and promises, chains, the join, Refuted);
identity classes are TypeIdentity's (Q4; its own unit first: OQ5), which decomposes under B. `Supply(BoundConstraint proposition, EvidenceView view) -> Evidence { bool Positive, bool Negative,
Completion }`; `Enumerate(BoundType subject, EvidenceView view, declarations) -> SubjectEvidence { facts on the subject's class (positive or negated, available or only discoverable), identity members,
Completion }`, `declarations` the consumer's candidates (identity: none, N8); `EvidenceView { BindingScope Premises, SourceState (provisional pass or final), FrozenDefinition (null for ordinary
proof), Inadmissible (the inference owner's published set), UnderCheck (the conformance or declaration whose own check is running) }`; `Completion = Complete | Provisional | Resource(limit, count |
source)`. No read sets (B2, OQ3): "depends" is per derivation and G172's design decides how a definition's value reaches another definition's view (prefer option (c), §3). Invariants: Jacobi rounds
over request-local rows; answers, counts and limit outcomes functions of (request, inputs); indexes prune only pairs that can never be one Type; identity decomposed only by the structure, never
projections or Contract arguments; nothing guessed; inadmissible bindings never read; Pending never a cached negative; Resource never absent. Prohibitions: no Binding writes, syntax-triggered supply,
`DerivedFacts`, fallback fact scan, mode or bool parameter, cross-request semantic cache, second scheduler or read sets.

## 7. Cost

A reference implementation (struct keys since round 5, LINQ), not the production evaluator; no speed claim ([results/cost.txt](results/cost.txt),
[results/cost-152dda62.txt](results/cost-152dda62.txt), [results/scale.txt](results/scale.txt), [results/limit.txt](results/limit.txt), [results/profile.txt](results/profile.txt); `cost`: median of 15
after 5, allocation per request; `scale`: median of 21 after 5; the machine shared with other sessions, load 3–56 % during `cost` and `scale`; the `152dda62` column is the same workload on that build,
its `cost` from the P2 session's run at load 82–96 %):

| Workload | Rounds | V | J | Time | Allocated | `152dda62` |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| G170 Copy, depth 1 / 8 / 64 | 4 / 18 / 130 | 8 / 29 / 197 | 25 / 466 / 61,030 | 60 µs / 0.68 ms / 27 ms | 46 KB / 409 KB / 15.3 MB | same rounds, V, J; 55 µs / 0.64 ms / 31 ms; 44 KB / 412 KB / 16.8 MB |
| Wide: 8 / 32 / 128 Contracts on T; diamond refinements, height 4 / 16 / 64 | 2; 2 | 6; 74 / 650 / 8,714 | 46 / 166 / 646; 126 / 1,230 / 17,166 | 28 / 59 / 188 µs; 0.06 / 0.48 / 9.3 ms | 50 / 115 / 396 KB; 150 KB / 1.26 MB / 17.4 MB | same; 30 / 67 / 208 µs; 0.11 / 0.89 / 17.0 ms |
| Item limit 1,000 / 3,000 / 10,000 (`limit`) | 93 / 275 / 911 | 457 / 1,367 / 4,547 | 63,858 / 564,267 / 6,214,809 | 0.17 / 1.0–1.1 / 4.8–5.2 s | | J 63,767 / 563,994 / 6,213,900; 0.16 / 1.2 / 4.6–4.7 s |
| Two-parameter 185 `List<i32> is Cp` (none) | 14 | 109 | 55,814 | 44 ms | | 17 rounds, V 781, J 1.63 M, 0.62 s; meeting rows answered 73 (round 4: 118) |
| Seed 4242 two-parameter model 12 `List<i32> is K3` (Resource at 3,016 items; **≤ 2 s not met**) | 106 | 2,389 | 5.55 M | 3.0 s | | 28 rounds, V 2,380, J 1.41 M, 1.4 s |
| Nested 39 `T is V` (+ Complete); seed 78 model 2 `T is V` (+ Resource, 4,671 items); seed 4242 model 361 `T.(K2).X is K0` (none Resource, 3,078 items) | 17; 12; 58 | 211; 3,987; 2,459 | 400 K; 600 K; 6.71 M | 1.6 s; 3.5–4.4 s; 1.1–2.3 s | | Resource (3,036 items), 13 rounds, V 2,636, J 11.5 M, 4.2 s; + Complete, 4 rounds, V 16, J 1,120, 65 ms; Resource (3,079), 20 rounds, V 1,840, J 3.27 M, 1.1 s |
| 1,000 repeated requests of G170 depth 8 | | | | 116 ms (0.12 ms each) | 385 KB per request | 209 ms, 410 KB |

**Phase attribution** (`profile`, the second, warm run, ms; load 6 %): item limit 3,000 (374 in all): Meetings 122, Eager 97, Rekey 76, DemandWoken 51, DemandFirst 13, Apply 8 (74,255 candidates
rescanned, 37,943 new; 37,401 meeting rows answered; 273 of 275 rounds merged); 185 (231): Meetings 179; nested 39 (809): Meetings 702; model 361 (1,062): Meetings 800, Eager 109, DemandWoken 85,
Rekey 51; seed 78 model 2 (3,517): Meetings 3,360 (4,393 meeting rows answered). **Open items**: (i) model 12: the final build reaches the limit in 106 rounds (`152dda62`: 28) with the same V, each
round smaller (a C3 gate: fewer items per round reach the limit later; no budget, no cache); (ii) seed 78 model 2: the positive polarity is established at round 1; the structure need (rule 9) then
explores the infinite universe `associate Y is K2` opens (need rows 4 → 54,148 over 12 rounds; without rules 7–9: + Complete, 5 rounds, 70 ms): §1's non-terminating class, the designed Resource with
the Checker's Proven; its cost at the production limits is a C3 measurement; (iii) model 361: fixed this round (J 1.3 G → 1.4 M by one node per Type; 6.7 M once the join expands by the one expansion,
§10), still Resource at 3,000 items as on `152dda62`.

**Program-size scaling** (`T is Copy` unrelated unless stated; n = 10 / 400 / 1,600; shared-n also 20 / 40 / 100; shared-live-n and open-live-n 10 / 40 / 100 / 400). Rule 4 makes every family whose n
Contracts have no fact flat (their rows are dead). shared-live-n (every Ki live through a conformance) is the live witness of R4-B1: one promise per class holds the n rows, V and J are linear; its
**time is superlinear** (0.8 / 3.5 / 23 / 187 ms at 10 / 40 / 100 / 400, load 56 %): each of the 2n antecedent demands owns the n subscriptions (D1's `Owners` and `Requested` sets and the per-owner
`expansions` keys: 2n² memberships), work not counted in J; recorded, not removed by a cache (an owner question, §9). open-live-n (n live `Wi: associate X is List<Self>`) measures rule 8 up: V and J
linear (a class demand requests each row once). Adapter (loaded): `ProveCopy` of the G170 value against C, interleaved medians of 41: d2 7.8 µs against 124 µs and 76 KB, d8 28 µs against 1,088 µs and
409 KB; the corpus's 400 requests 38 ms, 15.2 MB (the 44 programs' warm rebinds 1,070 ms).

| Request | Answer | Rounds | V | J | Time | `152dda62` |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| B3: `List<i32> is Copy`, `T.(Holder).Item is Copy`; `i32 is Copy`, `S1.(Pp).X is Copy`, `i32 is Pp` | none, +; +, +, none | 1, 2; 1, 2, 0 | 3, 6; 2, 4, 2 | 1, 11; 2, 23, 0 | ≤ 20 µs | flat |
| selfid `T is Copy`; `T.(Holder).Item is Copy`; open `List<T> is Copy`; `U.(W1).Y is Option<List<U>>` | none; +; none; + | 0; 2; 4; 4 | 2; 6; 12; 22 | 0; 11; 35; 723 | 2 µs; 7 µs; 34 µs; 0.52 ms | flat |
| selfid + `T is W3`: `T.(W3).X is T` (a meeting); palias `T.(K3<i32>).X is i32`; deep2 `T.(Holder).Item is Copy` | +; +; + | 3; 3; 2 | 7; 10; 16 | 270; 344; 58 | 0.15 ms; 0.17 ms; 40 µs | J 420 / 12,510 / 49,710; 502 / 14,152 / 56,152; 156 / 5,616 / 22,416 |
| palias, deep2, selfid + `V is Copy`: `T is Copy` (a meeting) | none | 1; 4; 1 | 4; 19; 3 | 13; 1,303; 1 | 15 µs; 0.6 ms; 4 µs | J 145 / 5,605 / 22,405 |
| shared-n `T is Copy` (10 / 20 / 40 / 100 / 400 / 1,600); shared-pos `W is Copy`; mix `T is Copy`; `T.(W3).X is T` | none; none; none; + | 1; 5; 1; 3 | 3; 57; 4; 7 | 1; 2,206; 11; 270 | 4 µs; 1.4 ms; 11 µs; 0.16 ms | V 256 / 906 / 3,406, Resource at 100; V 1,121 at 10, Resource at 40; V 4, J 289 / 11,209 |
| shared-live-n `T is Copy` (10 / 40 / 100 / 400) | none | 2 | 68 / 248 / 608 / 2,408 | 643 / 2,173 / 5,233 / 20,533 | 0.8 / 3.5 / 23 / 187 ms | V 456 / 6,606, 28 ms / 1.3 s, 100 unfinished |
| open-live-n `T is Copy` (10 / 40 / 100 / 400) | none | 2 | 43 / 163 / 403 / 1,603 | 41 / 161 / 401 / 1,601 | 0.09 / 0.28 / 0.65 / 2.0 ms | (new under B) |

## 8. C2's input list

C0's census ([c0/census-raw.tsv](../c0/census-raw.tsv)) mapped to the operations; C2 routes each read as a pure move (answered from today's Facts), C3 replaces the answer. Paths under
`src/Kimi/Compiler/Binding/`.

| Read site (method) | Operation | Behavior change at C3 |
| --- | --- | --- |
| `AvailableConstraintFact` (22 calls in 16 files), `ProveConstraint` scans (`Constraints.cs:640`, `:642`) and its presence gate | Supply | derived, not stored; the `HasAssociatedProjection` gate goes |
| `ProveConformance` premise loop (`Contracts.cs:681`); `ResolveConformance`, `ProveConformanceConditions`, `ProveMemberConditions` Invalid walks | Supply | premise part only; verification leaves the proof in its own unit (N6) |
| `SymbolicCapability` (`Capabilities.cs:536`) | Supply (refinements of the intrinsic); Enumerate (identity members) | unspelled projections (G170) |
| `FindAssociated` (`AssociatedTypes.cs:114`), `RequirementMember` (`ContractCalls.cs:114`), `HasContractFact` (`Indexers.cs:209`), `SelectValueCall` (`CallableSelection.cs:49`, `:63`), `TryCallable` (`ValueCalls.cs:296`), `CallableBoundSignatures` (`Retention.cs:45`) | Enumerate (Contracts declaring the name) | derived candidates, spelling-independent; pending ones stay discoverable for ambiguity |
| `ArithmeticNumericDomain`, `SelectArithmetic` (`ArithmeticOperators.cs:44`, `:205`), `ValidateArithmeticDirections` (`ArithmeticDiagnostics.cs:51`, `:68`); `AvailableEffectBounds` (`EffectDeclarations.cs:217`), `AvailableCallableEffects` (`CallableEffects.cs:33`), `EffectPremises` with `TyCtxt.Copy`, `Sealed`, `RecursiveBase` | Enumerate (arithmetic Contracts); Enumerate / Supply in the definition's view | arithmetic premises become rows; derived effect bounds |
| `HasParameterIdentity`, `SubstituteIdentityPremises`, `IdentityPremise` (`TypeIdentity.cs:12`–`:149`), `ContractTypeCore` (`AssociatedTypes.cs:684`), `NormalizeProofConstraint`/`NormalizeProofType` (`Constraints.cs:685`, `:714`), `HasValueRole` (`ConstraintValidation.cs:673`), `ValidateContractDeclarations` (`Contracts.cs:887`), `FailConstructorAbsence` (`Structs.cs:329`) | Enumerate (identity members, no declarations) | the identity unit (OQ5), decomposing under B; the `HasParameterIdentity` flag goes |
| `AdmittedSemantics`, `ValidateConstraintEnvironments` (`Constraints.cs:401`), `ValidateConstraintUses`, `RecordReferenceConstraint`, `ReportQualification`, `ValidateConditionalBlock` | none: input validation over DirectFacts; **declaration validation** (B 3.10: the bounded forward closure of a scope's identity reports structural conflicts and cycles once at the declaration) becomes C's input at C3 | Resource never marks an environment Invalid |
| `DisjointConformanceFacts`, `RefutedOverlapInputs`, `ValidateAssociatedOverlapAgreement` | Supply in the composed scope | composed scopes hold inputs only |

Writers: `AddConstraintFact` stays for direct inputs only; `AddContractPremises` (4 callers), `ExpandScopeContractPremises` (6) and `AddArithmeticPremises` go at C3; scope composers copy inputs and
substituted clauses as inputs; `DerivedFacts` and the `HasAssociatedProjection` flag go. S5's view (`Binding.RecursiveContracts.cs`, R4-M87) must be SPEC 8.3.3's search over the supply rows with
argument and identity routes, closed Types included (N3), and inherits G180's correction. C2 first restructures behavior-preservingly (`Binding.Contracts.cs` premise supply to
`Binding.ContractSolver.cs`, `Binding.Constraints.cs` to `.Rows.cs`, `Binding.AssociatedTypes.cs` lookup to `Binding.AssociatedLookup.cs`); `order`, the scaling workloads, the profiles' slow cases and
§4's curve become C3 regressions, with a `src/Benchmark` premise workload. Rows C1 did not model (each a row or outside supply at C3): PrimitiveInteger → Position, arithmetic premises, Semantics and
Callable constraints, Origin families, generic value-side conformances, structured and identity conditions. **Expected size** (N9, DECISIONS Q5): the four ContractSolver files about 22,000 tokens
(prototype `Solver.cs` about 19,900 identifier tokens with LINQ, the join, rules 6–9 and the phase counters; 15,300 on `152dda62`), TypeIdentity's decomposing classes about 3,000,
`ConstraintProof.Resource` 300; removed at C3 about 4,000 (measured on `e0f24c83`). Net about +21,000.

## 9. Decisions, departures, unverified

| Question | Decision | By |
| --- | --- | --- |
| Q1/OQ1 Resource diagnostic; R2-B1 identity selection | one shared Binding analysis-limit code, the limit as an argument, the last free `BindingFailure` entry (also G172's inference budget and G174's fixed points); one need-driven rule for every identity side (§1), not a whitelist or a hold (「一般規則 (推奨)」) | owner, 2026-10-21 |
| R4-B1 / R4-B2 (round 5) | the general rule: §1's rules 1–5 — a promise is a shape, matched as a node, filters from classes only, dead rows are not rows | owner's brief 2026-10-22 §3–§4; planning session (common design §4) |
| Proposal B (2026-10-22) | the fixed decision "identity is not a row and is never decomposed (SPEC 8.2.3)" is superseded by the owner's spec change: identity is still not a row; the structure decomposes it (B 3.3), never projections or Contract arguments (B 3.5). Addendum decisions D5 (a Contract reference is not a Type: a reference agrees only with the same reference head, its arguments by the kid rule; reference classes gain no promise and meet only the same reference), D6 (rule 6), D7 (rule 7), D8 (rule 8), D9 (rule 9; at a limit only Proven and Error stand; declaration validation is C3's) | owner; planning session's addendum (three design-review rounds) |
| Round 5, track P | struct keys (no string key in a request); the join by unification through an index, both lookups unfiltered; no semi-naive re-examination device in C1 (§4 records it as C3's input); one re-keying mechanism, `ShapeTable.Canon`; D1 subscriptions as owned rows; D2 the expansion memo per (promise, pattern, sign) without the requester; a chain's base re-resolved per round, never a snapshot; a parent's promises the round's top-level lookup; one node per Type in the structure by removal (a congruent duplicate leaves its class and its kids' use lists and owns no key; no flag, no filtered use list; the alternative, B0's duplicates kept until the memo merges them, rejected: a duplicate member is matched as another node and multiplies through nested patterns — model 361 J 1.3 G, 257 s — and the join and every lookup read class members); a promise's index key from its shape; one head function and one member expansion (rules 3, 5); a subscription's fired facts re-keyed when facts become one (rule 5); need rows as tagged effects (m41); no cache and no budget for a slow model (361, 12, seed 78 model 2: a cause fixed or an open item) | planning session, design P §1–§4, common §9 D1–D4; §10 |
| Q2 transport; Q4 identity classes, OQ5 scope | `ConstraintProof` gains a fifth member `Resource` (§4; N10); TypeIdentity owns the classes, C reads them (promises are C's, keyed by class); a separate reproduced identity unit before C3 | planning session |
| Q3/OQ3 "depends"; Q5/OQ2 limits; OQ4 D5 freeze | per derivation, no read sets in C0–C4, G172's design decides its flow-back (prefer option (c)); rounds and items, 4,096 and 65,536 provisional, confirmed by C3 (§4's gate), in IMPL 21.3.2/A.4 at C3; where C3 shows only the proof changes and the old Emission generates correctly, the default pipeline accepts, otherwise a guard reports the located `Unsupported_Kd` until R6-check | owner |

Departures from PlanB §4: identity classes are part of selection; every identity side is lazy under one rule; chains, needs, meetings and bound expansions are rows (§1); verification is inside proof
today (§5). **Unverified**: production cost and item-limit time (C3 gates); seed 4242 model 12 over 2 s (§7); the structure need's cost over an infinite Type universe at the production limits (seed 78
model 2, §7); B 3.7 (specifications as premises only for undetermined associated Types: the inference owner's view, C3/G172); identity conditions on conformances (`fill`'s second query is vacuous:
`Condition` requires `Is`); witnesses for effect premises, Enumerate lookups, arithmetic, composed scopes; G172's admissibility; mixed Owned/premise cycles (N5); Origin families, structured
conditions, deeper goals (C3 gate); shared-live-n's superlinear time (§7, an owner question: a rule that removes D1's 2n² memberships without a side table; a candidate: subscriptions owned by the
subject class's demand, n memberships).

## 10. Review findings: rulings and evidence

| Finding | Ruling | Evidence |
| --- | --- | --- |
| R4-B1 same-key virtual nodes processed per pair (quadratic in program size) | rule 1: a promise is identified by (class, shape); rows, bases and producers of one shape are members expanding together; chains forward to records; the join agrees each pair once, by unification through an index (§1) | §7: shared-n V 3 at every n (`152dda62`: 256 / 906 / 3,406 at 10 / 20 / 40, Resource at 100), shared-pos flat, shared-live-n V and J linear; fixed cases shared-n, shared-pos, shared-live-n, the join with a wildcard kid, a shape variable at two positions |
| R4-B2 a kid that is not a Type yet pruned by its head | rules 2–3: only the top is matched by head, every kid by the kid rule, a kid that is not a Type yet is decided once built by every filter and the agreement | shape5/shape7 in their valid form (`B0 is i32`) `+`; the round-4 review's seven variants `+` |
| Round 5 (track S review): `Effects.Tags` omitted need rows | need rows are tagged effects (rule 5) | m41 `T is Cp` `+` (`152dda62`: none Complete, rounds 0) |
| Round 5: per-requester subscriptions and expansions (shared-live-n quadratic) | D1: a subscription identified by what it subscribes to, requesters as owners; D2: an expansion answered once per round per (promise, pattern, sign) and replayed | shared-live-n V 68 / 248 / 608 / 2,408, J 643 / 2,173 / 5,233 / 20,533 (n = 10 / 40 / 100 / 400) |
| Round 5: `order` differed (10 of 440 two-parameter requests, 8 of 24 merge-every-round runs, 52 default; then 5 after rule 8) | four representations read an accident of order and were replaced by their rule: the join filtered one class's lookup by the other's with the roles assigned by class id (both lookups unfiltered); a class-shaped promise took its index head from the first producing term (rule 2: an existing class has no head); a chain's base was the last detecting lookup's snapshot (rule 1: a base is an identity, re-resolved each round); rule 8's parent lookups ran inside the decomposing lookup's nesting with a per-class cycle guard (a lookup is a function of (class, need, filters): the parents are the round's top-level lookup, a cycle is a lookup asking itself) | `order`: 0 differ (§4); the witnesses (models 307, 122 and 164 of seed 4243) |
| Round 5: seed 4242 model 361 `T.(K2).X is K0` (J 1.3 G, 257 s at 3,000 items; `df1ddea4` > 150 s) | per-site attribution: lookups per round grew linearly, `Match` calls 1.7× per round, because after each congruence a class kept the node whose key another node owned (a duplicate member, matched as another one; duplicates multiply through nested patterns). Rule 6: one node per Type per class; the node leaves its class and its kids' use lists | same rounds, V, E, N; J 1.4 M, 1.0–2.9 s warm (§7) |
| Addendum review (rounds 1–3): a negated component refutes the parent (B2); projection-headed kids compared by head (B1); the structure need (B3); the kid pair selects the negated row (round 2 B1); Refuted owned by the Solver; at a limit an established Refuted is Resource (N2); the structural profile's exclusions | rules 7 and 9 as stated; Refuted with its negated-fact base case in `Solver`; `Checker.Judge`; the profile emits one constructed other side per projection, only where the declared requirement is not a constructed Type | fixed cases negated-component, proj-kids, proj-vs-con, projection-side, negated-row-kid; structural declaration errors 70 and 64 of 200 (B0: 99) |
| Final review, round 1: B1 a congruent duplicate stayed in `usesOf`, owned the re-keyed key and retired the survivor (w2b-retire `A is Cp`: none); B2 rule 8's kid heads lived only in `Shapes` while demand and compound candidates and the join's heads read `VirtualHeads(HeadsOf(c))`, and the join's `Partners` expanded rows and chains but never producers (w13a, w13b, w15 `V is Cp`: none); B3 a negated row was requested only when its side bound the other side (w3 `T.(K).X is U`: none); N1 the item-limit curve; N3 a decomposed promise keyed by its producer's head; N5 `inProgress` | B1: one node per Type by removal (§9; `Retired` and the `UsesOf` filter deleted; the bound expansion scans a removed id as its survivor, same class and kids); B2: one head function `HeadsFor` (rule 3) at every filter, index and candidate selection, one member expansion `Expand` at every site including the join (`Partners`' own agreement, row requests and conditional forwarding deleted); N3: `Key` from the shape (rule 1); B3: the `Unbound` filter deleted, `Request` gates (rule 9); N1 stated (§0, §4); N5: a lookup asking itself through a cyclic structure answers nothing — reached by premise-error structures only (B 3.6) | fixed cases w2b-retire, w13a, w13b, w15, w3 (`+`, `+`, `+`, `+`, `-`); §2, §4 and §7 re-run on the redo |
| Redo: structural `order` (seed 4242 model 37) differed in E, J and peak items (9 of 444 requests; also on the P2 build) | a fifth representation read an accident of order: a subscription's fired set held fact ids, and when two facts became one (`i32 is K0<List<i32>>` and `U is K0<i32>` under U = i32) the union's root was whichever had the lower id, so one order fired the row again. Rule 5: every stored key is re-keyed after a round; the fired set follows the facts' union | the order mode's fifth fixed model (`FiredRekey`: 4 of 6 shuffles differed before, 0 after); structural `order` 0 differ |
| R4-N1 references as Types; R4-N2 classification; R4-N3 parser; R3-B1, R3-B2; R2-B1, R2-B2, R2-N1–N5; round 1 B1–B4, N1–N12 | D5 adopted (§9); the C4 gate stands; prototype only; the rest as recorded in rounds 2–4 (`c1-review.md`) | §9; fixed cases; `order`; §3; `resource` |

Deleted since round 4 (`Solver.cs` 4,164 lines, `Shapes.cs` 188, `EGraph.cs` 356; `152dda62`: 3,364, none, 268): `Virtual`, `VirtualKey`, `VirtualKeyOf`, `keys`, `storedKeys`, `StoredKeys`,
`MeetVirtuals`, `ExpandAt`, `Settle`, `Agree`'s term form, `AgreeClass`, `SameShape`, `HasFree`, `MayUnify`, `BindKey`, `SubText`'s string, `Sub.Requester`, `Groups`/`Group`, `Admits`, `Reachable`,
`Effects.Refreshed`, `KidHeads`' term-head branch, the `KidAt` filter on stored chains, the deferred-true branches of `Kid` for constructor nodes, `EGraph.Refuted`/`Part` (now `ProperPart`, `Clash`
and the Solver's `Refuted`); after the final review: `EGraph.Retired` and the `retired` list, `UsesOf`'s filter, `Shapes`/`KidShapes` (one `HeadsFor` with `PromiseHeads` as its promises' part),
`VirtualHeads(HeadsOf(c))` at three sites, `Partners`' agreement loop, the `Unbound` filter on negated rows.
