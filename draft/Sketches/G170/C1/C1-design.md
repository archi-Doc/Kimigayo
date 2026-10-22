# G170 C1: executable design of C (ContractSolver premise supply)

Unit C1 of [PlanB](../../../Proposals/2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) (BU planning session,
2026-10-21), after [C0](../C0-evidence.md). Draft data only. Branch `claude/bu-g170-c1` from origin/dev `e0f24c83`; round 5 on
`claude/g170-c1-short` (track S of the owner's brief, `artifacts/handoff/g170-c1-owner/`; the planning session's design and its
proposal-B addendum under `artifacts/handoff/g170-c1-r5/design/`). Revised after each independent review (§10). Type identity
follows the owner's 2026-10-22 spec change, [proposal B](../../../Proposals/2026-10-22%20Type%20Identity%20by%20Structural%20Decomposition%20B.md)
§3 (cited as B 3.x), in place of SPEC 8.2.3's non-decomposition. [proto/](proto/): model, identity classes (`EGraph.cs`), shapes
(`Shapes.cs`), C (`Solver.cs`), judgments (`Checker.cs`), bounded reference, runner (modes and `G170_*` variables in `Runner.cs`;
`nested_probe.py`); [adapter/](adapter/G170C1AdapterTest.cs).

## 0. Verdict per completion condition

1. **Rule selection** (§1): every SPEC 8.3.2 supply row is selected by e-matching through static indexes keyed by conclusion heads,
   every identity row by one need-driven rule whichever side mentions the row's subject (owner, 2026-10-21), at its top or at a
   constructor kid position (B); a promised Type is one node of its class; unrelated requests are flat in program size.
2. **Agreement** (§2): 0 disagreements on 44,835 stable Supply queries and 4,043 enumerations over nine seeds and four profiles
   (one enumeration re-supplied at Resource); the reference is independent. 3. **Real-source witnesses** (§3): G170 at every depth
   whatever the spelling, G180's probes, frozen views; the corpus matches `ProveCopy`. 4. **Resource** (§4): two limits, order-free
   (2,416 requests, 7,662 boundary runs, 0 differ); a published Resource state per source; the Proven obtained stands at a limit.

## 1. Rule selection (§4.3 step 2)

**Rows are existing clauses** (no second clause language; variables: Self and the Contract's own parameters): **Input** (a scope
chain's `DirectFacts`, conjunct-split, exact compounds kept as written; seeded), **Refinement** (`Self is D<b̄>` per ancestor of K;
G180), **Clause** (each conjunct of each dependent clause of K, associated requirements included), both with antecedent `Self is
K<Ā>`; **Conformance** (`S<V̄> is K<ā>` of a verified path, never for a recursive K: SPEC 8.3.3) and **Binding** (`S<V̄>.(K).X is τ`
read through the view) with antecedent their conditions; a row's source state is its declaration's, conformance's or binding's
(§4). Identity is not a row: supplied identity facts are unioned into request-local classes closed under upward congruence and
under **decomposition** (B 3.3: the structure, never a row, a projection or a Contract argument, B 3.5), and every match is modulo
those classes; disjunction supplies nothing; `not` is a polarity of the same rows. **A row is live when its antecedent Contract can
have a fact** (rule 4): the static position index is a fixpoint over live rows, and a dead row enters no index (the brief's
program-size families are flat for this reason; §7's live witnesses measure the rules).

**One fixpoint over needs.** A request interns only its goal's Types and its inputs; every row below is selected by a need, a
meeting or an existing node, never eagerly:

- **Demand (K, s, ā)**: facts `s' is K<ā'>` with `s' ≡ s`, `ā' ≡ ā` where bound (s or any āᵢ may be ⋆). Selected: K's rows whose
  conclusion subject is a variable or has a head of s's class or its promises (a static index), e-matched arguments first (G180),
  then the subject; the bindings give the antecedent demand; a free parameter is read off an available antecedent fact, never
  invented. A demand meets the classes holding its Contract's facts, and their arguments once the subjects are one class.
- **Identity rows: one need-driven rule for every side** (owner, 2026-10-21: the general rule, R2-B1). Every positive identity
  side, whichever side mentions the row's subject, is in one index: (other side's head, side's head), then the heads of both sides'
  kids 0 and 1 (Any for a variable), rows sharing a side matched once. A side matching class c makes its other side a **promise**
  of c, requested only where **(a)** a pattern needs its head at c or at a kid of c's node, **(b)** c meets another class, or **(c)**
  a node of the other side's shape exists and c has a class demand (bound expansion). A **chain**: a side meeting c only through a
  promise makes its other side a promise too; only **chainable** sides are tried, excluding an **alias** (an other side that is a
  variable its side binds) and the row's own other side. Chains and deeper needs are rows (one level per round beyond the lookup's
  two); a lookup is answered once per round and replayed.
- **Promises** (round 5: R4-B1, R4-B2; the five rules of the planning session's common analysis, then B's rules 6–9):
  1. **A promise is a shape.** The shape of a term under bindings β: a bound variable or an interned ground subterm is its class, a
     free variable a shape variable numbered by first occurrence, any other subterm a shape node; hash-consed per request. A
     promise is identified by **(class, shape)**; its **members** are the rows producing it directly (row, β, the row variable per
     shape variable), the chains producing it (the hit's pattern, the bindings above the hit, the base promise) and the parents'
     promises it decomposes (rule 8): one agreement per pair, every member expanded; stored chain promises count as items.
  2. **A promise matches as a node: the top by head, every kid by the kid rule** (rule 7); a bare-variable or alias promise has no
     head and matches every need at its class. An expansion is answered once per round. **The deferral loses nothing**: every
     identity fact about a promised Type arrives through a pattern with free kids, which agrees without meets; a kid pair that must
     be one Type becomes one by rule 6.
  3. **Filters read classes only**: a filter's head set comes from a class (its heads, its promises' and its decomposed promises':
     per constructor use `F<…c at i…>` the kid-i heads of the rows' F-headed other sides); a kid that is or holds a shape variable
     contributes any head; a ground kid not interned its own head plus its promises'.
  4. **Dead rows are not rows** (above). 5. **Nothing else changes in selection**, except that **need rows are tagged effects**: a
     round whose only effects are need rows settles no polarity (§10).
  6. **The structure decomposes** (B 3.3, D6): one constructor (Prim or Con node) per class — two of one head in a merged class union
     their kids pairwise (queued like congruence), two of different heads make the class **conflicting**; a class that is its own
     proper part through constructor kids is **cyclic** (B 3.6); projections, Contract arguments and references are never decomposed
     (B 3.5, D5). `Refuted(a, b)`: a negated identity fact between the classes; a conflicting class, against any class; constructors
     of different heads; equal heads and a Refuted kid pair; a proper part — over materialized constructors and facts, coinductive.
  7. **Matching unifies against constructors** (D7; the kid rule at every kid position): two classes agree when equal, never when
     either is conflicting or both hold constructors of different heads (the full relation is for answers), and meet otherwise; a
     shape variable takes a class it meets (D3) and records the head it needs against a shape node (the static prune); a constructor
     node against a class recurses into the class's constructor of its head, is refused by another head, is deferred when the class
     has none; two constructor nodes recurse on equal heads. A **projection-headed** node or pattern is abstract (B 3.2) and decides
     nothing; a **reference** (D5) agrees only with the same reference head. Refuted classes never meet; pruning reads established
     constructors only, never promises.
  8. **Promises and class demands decompose with the structure** (D8). *Down:* a promise `F<s̄>` of a class p is, at each
     constructor node `F<…k at i…>` of p, a promise of the kid class k (a shape node at that shape, a class as an alias, a shape
     variable or an alias or bare-variable promise of p as the bare-variable promise), expanded by expanding the producer against
     the pattern lifted through position i; its identity is the producer's (class, shape) and the position, so the memo, `expansions`
     and chain re-resolution work as today, and a chain hit forwards to the producer; parents are looked up on the requester's
     behalf (their merges and chain gains wake it), a new constructor node wakes the rows that visited its kids' classes, a cycle
     stops at a class being decomposed. *Up:* a class demand at k requests every live identity row whose constructor side has a kid
     pattern (any depth under Con heads) e-matching a member of k modulo promises — a projection or constructor kid against k's
     nodes of that head, a variable kid against k itself — with the kid match's bindings (`expansions` dedupes): a static index of
     constructor sides by kid head beside `byShape`, which is keyed by the scanned side's own head and cannot answer a kid head at depth.
  9. **Outcomes and the structure need** (D9). `Holds(Eq)`: Proven = one class; Refuted = `Refuted`; both `+-` (a premise error the
     request's own derivation reaches: Error). Refuted reads materialized constructors and negated facts, so **an identity goal's
     negative polarity needs the structure of its two classes**: each class it reaches — the goal's two and, recursively, their
     constructors' kids — requests every promise at it (a need with head Any, served with the class demands' `NegatedFor`, which the
     kid class demands inherit), selects the negated identity rows there as at the goal pair, and asks again when a merge wakes it;
     the goal pair's meeting row stays for the positive polarity and overlaps this need. At a limit the Proven obtained stands
     (B 3.10); an established Refuted is Resource, deliberately (it could become Error).
- **Meeting row (a, b)** (b): one per class pair, its requesters' tags, answered once per round when new or woken (a merge it read
  or below either class); none where neither class can gain a node or the pair is Refuted. Every node of one class, real or
  promised, probes the other's promises through the index by its head and kid heads and agrees with each found (one agreement per
  pair); same-head real nodes meet at their kids. Requested by a demand for its facts' classes, a bound variable and its class, an
  identity goal's two classes, and Enumerate's same-declaration facts (R2-B2, SPEC 8.6.1).
- **Class demand (c)**: c's kids (congruence makes c depend on them), the identity goal's meeting, rule 8's upward half, the
  structure need and the negated identity rows where `NegatedFor` reaches. **Compound row (c)**: exact compounds whose subject
  e-matches c. Static indexes skip impossible expansions and polarities; tags recomputed each round say which polarity a row serves.
  **Subscriptions are owned rows** (§9): one per (template, stage, antecedent demand, sign, bindings, antecedents), serving its owners.

**Why the selection is complete.** If the supply has `s' is K<ā'>` with `s' ≡ s`, C derives it unless it stops with Resource.
Induction on derivation height: the last row instance concludes σ[β] ≡ s; each identity fact of that congruence has smaller height
and a side in a class C holds, in a kid class of one, or (B) in a class whose constructor decomposes against another of the same
head. E-matching σ there needs σ's head and kid heads: (a) asks every side that can match that class or a kid, chains reach sides
that meet only through a promise, a deeper need is a row, classes that must be one meet (b), (c) covers identities between existing
nodes, rule 8 finds a side at a constructor kid position, and a lookup or meeting is redone when a class it read merges, gains a
chain or a parent; so σ[β] joins s and the antecedent's fact is derived by hypothesis. Filters prune only heads a class, its kids or
its promises cannot have; rule 7 only pairs no premise can make one Type; a syntactic-prefix selector is B (§3's inputs). **Where it
does not terminate**: a polarity settles when established or when none of its rows has work; a negative answer over a recursive
Contract with growing subjects, and enumerating a closed Type's infinite class, end in Resource.

## 2. Prototype and exhaustive bounded reference

C runs §1 in **Jacobi rounds**: a round computes its effects from the previous round's state only (lookups and expansions answered
once per round and replayed; meeting rows answered once), applies them and restores congruence and decomposition; subscriptions
scan only facts added since their last scan (all after a merge). Answers: Complete, Provisional (a pending source was selected) or
Resource. The reference applies every row to every fact forward with Types up to a depth bound and decomposes on its own
union-find, an oracle where its answer is the same at bounds 5 and 7; it builds its own rows and classifies recursion by SPEC 8.3.3.
**Declaration errors** (B 3.10): a generated model whose reference has a structural conflict or a cycle at either bound is a
premise error of its declarations (including B 3.7 obligation failures: a binding or input against its Contract's constructed
associated requirement), reported there, never a request's answer: its requests are not compared and the model is counted.
Generated models (3–5 Contracts; 0–1 parameters, 0–2 in the two-parameter profile; associated requirements, deep, argument and
compound subjects, identity, `not`, `and`/`or`, refinement, verified, pending and conditional conformances, inferred bindings with
a frozen view; the nested profile; the **structural** profile: one constructed Type on the T-projection's other side where its
declared requirement is not itself constructed, and the identity goals `U is i32`, `U is List<i32>`, `T.(K).X is List<U>`): each
Supply query and up to 8 reference-derived facts compared at both bounds, each Enumerate fact by fact; harness 300 rounds, 3,000
items; the older generators draw nothing new, so the seeds' **model sets** stay comparable with `152dda62`'s ([results/](results/)).

| Seed (models; oracle overflows; declaration errors) | Stable Supply: agree / disagree / Resource | Enumerate: agree / disagree / Resource |
| --- | --- | --- |
| 4242 (200; 4; 7) — `152dda62`: 8,285 / 0 / 20 on 400; 4242 structural (200; 4; 45) | 3,963: 3,926 / 0 / 37; 3,660: 3,609 / 0 / 51 | 359: 358 / 0 / 1; 287: 285 / 0 / 2 |
| 20261022 (400; 7; 23) — `152dda62`: 8,251 / 0 / 64 | 7,769: 7,651 / 0 / 118 | 688: 672 / 0 / 16 |
| 4242 / 20261022 two-parameter (200 each; 12 / 14; 16 / 13) | 3,675: 3,640 / 0 / 35; 3,555: 3,502 / 0 / 53 | 330: 326 / 0 / 4; 327: 323 / 0 / 4 |
| 7 / 20261021 (400 each; 11 / 12; 22 / 30); 99 two-parameter (200; 8; 10) | 7,705: 7,613 / 0 / 92; 7,442: 7,338 / 0 / 104; 3,726: 3,621 / 0 / 105 | 685: 669 / 1 / 14 (model 322, the growing supply: the same three facts, one of them Resource when re-supplied under the limit); 679: 669 / 0 / 9; 339: 329 / 0 / 10 |
| nested probes 77 (40 models, 653 queries) / 78 (20, 324) | 550 / 0 / 3 (34 declaration-error queries, 49 overflows, 17 over budget: model 36's reference, C alone 0.2–0.3 s); 300 / 0 / 7 (all seed 78 model 2, 6–8 s each) | — |

No Supply query was unstable; exclusions depend on the reference's 5 s guard and the machine's load (R3-N2; track P ran
concurrently); classification differences (no answer differs) are C4's (N3). Resource answers rose with B (seed 4242: 6 → 37;
20261022: 64 → 118): §9's open row; the structural profile's four slow models (17–240 s) are the reference's at bound 7 (every
query of theirs under 1 s with C alone). Fixed cases (138, all pass, [results/fixed.txt](results/fixed.txt)): every round's witnesses
and B's. `pending` (seed 4245, models 0–14): 0 of 201 Complete answers change with pending sources usable, 10 Provisional (0 where
pending matters); from model 15 the reference does not finish.

## 3. Real-source witnesses

`BindingSource` (adapter) builds the model from Binding's state after `Bind`, read-only (`AncestorStorage`, `ClauseStorage`,
`DirectFacts`, verified paths with `AssociatedTypes` and `InferenceEvidence`; [results/adapter-output.txt](results/adapter-output.txt)):

G170 `inspect` depth 1 (Binding today Proven), 2 / 4 / 8 absent (Unknown) and with the stronger hint before or after (Proven), and
generated `T.Child^k` never spelled, k = 3 / 8 / 16 (Unknown at C0): C `+ Complete` for all, the same work with or without the hint
(d8: 18 rounds, V 29; k = 16: 34 rounds, V 53); the non-recursive `T.(Outer).Inner.(Holder).Item` absent / hinted (Unknown /
Proven): + / +; G180 stale0 (control), stale1 `associate X is D<Self>`, stale2 `contract C: D<Self>`, h4's required `T.(C).X is
D<T.(C).Y>` and D's clause `T.(C).Y is E`, h6 (none, `UnprovenConstraint_Kd` ×3, none ×2 today): + for all six, as the reference.
Frozen views (D: `x() -> i32`; E: `w() -> D.(P).X`; both orders): `D.(P).X is i32` is + in E's view, none in D's own; without read
sets (OQ3 (c)) `E.(Q).W is i32` is none in D's view (today: +). Corpus (44 milestones and the Kimi library: 121 scopes, 400
requests, 70 ms beside 975 ms of rebinds): all Complete; Copy evidence equals `ProveCopy` on all 99 (55 / 44); V ≤ 34.

## 4. Resource boundary and transport

- **Limits, per request** (owner, OQ2): **rounds** (SPEC 8.3.3's search depth) and **items** (rows, supports, identity nodes and
  stored chain promises; the seeded state counts); 4,096 and 65,536 provisional (§9); no join-work or read-alternatives limit.
  **Order-free**: each limit reads the state after a round and rounds are Jacobi; J (reported, never a limit) counts matching
  steps, index entries, probes and promise agreements (R3-N1), plus congruence repairs. `order`
  ([results/order.txt](results/order.txt)): 400 default models (1,832 requests, 5,748 boundary runs), 100 two-parameter (440; 1,434)
  and 30 nested (144; 480, where a merge follows a stored chain): 0 differ in verdict, outcome, reason, rounds, V, E, J, N or peak
  items, Complete at the need and Resource below it. **Tested values** (`resource`): G170 depth 8 and 64 complete at the need
  (depth 64: 130 rounds, 331 items) and are Resource at need − 1; a closed Type's infinite class is Resource at every limit;
  limit-keeps-proven (seed 20261022 model 312, `i32 is Cp` at 3,000 items) is judged Proven with one Resource record. **At a limit**
  (B 3.10, read literally): the answer keeps the polarities established at the stop; the judgment is Proven (both: Error) when the
  positive one is, otherwise Resource — an established Refuted, even from a directly available negated premise, is Resource,
  deliberately (more analysis could make it Error); the "source state Resource" rule is unchanged.
- **Time to the item limit** (R2-N2; `limit`): that enumeration reaches 1,000 / 3,000 / 10,000 items in 0.19–0.20 / 1.17–1.38 /
  4.9–5.6 s ([results/limit.txt](results/limit.txt); J 64 k / 0.56 M / 6.2 M), interleaved three times with a `152dda62` build at
  7–40 % load: 0.16–0.17 / 1.04–1.30 / 4.4–4.8 s with identical rounds, V and J — 10–20 % slower, the per-round overhead of rules
  7–9; the brief's ≤ 0.16 / 1.1 / 4.6 s is not met, reported, not weakened. **C3 gate**: the production evaluator reaches the chosen
  item value within about 1 s (a warm Bind) here and on seed 4242 model 12 (`List<i32> is K3`: Resource at 3,000 items after 106
  rounds, J 7.33 M, 4.2–4.7 s against `152dda62`'s 28 rounds, J 1.41 M, 1.5 s — over the brief's 2 s bound, a failed criterion
  reported, not weakened) by semi-naive deltas; otherwise IMPL records the value reached in 1 s.
- **Published Resource state.** A source whose own check ended at a limit publishes `Resource`; a selected row from it makes every
  polarity not established Resource, never absent. **Transport** (Q2): `ConstraintProof.Resource`, absorbed as Error is; one record per (scope, limit) (OQ1).

## 5. Lifecycle, scheduler (§4.4), representation and entry contract (C2/C3)

**Request-local**: identity classes, shapes, rows, supports, facts and the per-round answers; capacity is reused; an answer
(Resource included) is a function of (request, inputs); persistent answer-shaping state (C0's census) becomes request-local or keyed
by its inputs. **One scheduler**: the Jacobi worklist hosts premise rows now, capability nodes and conformance verification at C3,
G172's value demands later; limits count all kinds; a premise row reads only judgments final in the request (N5). **Source states**:
a published state per declaration, conformance path and binding (Valid, Pending, Invalid, Resource); C never runs verification; a
Pending read is Provisional, never cached, re-run after publication; the check under way is an `EvidenceView` exclusion (N4);
Invalid supplies nothing (N7); verification out of proof is its own unit (N6).

Files under `src/Kimi/Compiler/Binding/`, each within 5,000 tokens: `Binding.ContractSolver.cs` (entry file with this contract),
`.Rows.cs` (indexes over existing storage, liveness), `.Demands.cs` (rows, rounds, tags, limits), `.Matching.cs` (e-matching,
shapes, promises, chains, meetings, decomposition); identity classes are TypeIdentity's (Q4; OQ5), which own decomposition,
conflicts and cycles. `Supply(BoundConstraint proposition, EvidenceView view) -> Evidence { bool Positive, bool Negative, Completion }`;
`Enumerate(BoundType subject, EvidenceView view, declarations) -> SubjectEvidence { facts on the subject's class (positive or
negated, available or only discoverable), identity members, Completion }` (`declarations` the consumer's candidates; identity: none,
N8); `EvidenceView { BindingScope Premises, SourceState, FrozenDefinition, Inadmissible, UnderCheck }`; `Completion = Complete |
Provisional | Resource(limit, count | source)`. No read sets (B2, OQ3): "depends" is per derivation; G172 decides the flow-back.
Invariants: Jacobi rounds over request-local rows; answers, counts and limit outcomes functions of (request, inputs); indexes prune
only impossible heads; identity decomposed only by the structure, never projections or Contract arguments; nothing guessed;
inadmissible bindings never read; Pending never a cached negative; Resource never absent. Prohibitions: no Binding writes,
syntax-triggered supply, `DerivedFacts`, fallback scan, mode or bool parameter, cross-request cache, second scheduler, read sets.

## 7. Cost

A reference implementation (string keys, LINQ), not the production evaluator; no speed claim ([results/cost.txt](results/cost.txt);
median of 15 after 5; the machine shared with track P). G170 Copy depth 1 / 8 / 64 (as in round 1): 4 / 18 / 130 rounds, V 8 / 29 /
197, J 25 / 466 / 61,030, 0.06 / 0.7 / 18 ms, 49 KB / 0.46 / 17.6 MB allocated; wide (8 / 32 / 128 Contracts on T): 2 rounds, V 6,
J 46 / 166 / 646, 29 / 60 / 197 µs; diamond refinements of height 4 / 16 / 64: 2 rounds, V 74 / 650 / 8,714, J 126 / 1,230 /
17,166, 0.07 / 0.6 / 12 ms; the same rounds, V and J as `152dda62`; allocation at depth 8 458,360 B per request (412,024: +11 %).

**Program-size scaling** (`T is Copy` the unrelated request, `V is Copy` the Copy fact on another subject; one value where equal at every n):

| Family (n = 10, 400, 1,600 unless stated) | Request | Answer | Rounds | V | J | Time (µs) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| B3: n conformances binding `X = i32` | `List<i32> is Copy`; `T.(Holder).Item is Copy`; `S1.(Pp).X is Copy` | none; +; + | 1; 2; 2 | 3; 6; 4 | 1; 11; 22 | 7–8; 13; 22 |
| selfid: `Wi: associate X is Self`; with `V is Copy`; with `T is W3` (related) | `T is Copy`; `T.(Holder).Item is Copy`; `T is Copy`; `T.(W3).X is T`; `T.(W3).X.(Holder).Item is Copy` | none; +; none; +; + | 0; 2; 1; 3; 4 | 2; 6; 3; 6; 11 | 0; 11; 1; 179; 140 | 2; 7–14; 4; 96–116; 74–85 |
| open: `associate X is List<Self>`, `Self.(Wi).Y is Option<Self.(Wi).X>` | `List<T> is Copy`; `U.(W1).Y is Option<List<U>>` | none; + | 11; 4 | 32; 24 | 2,478; 2,299 | 2,210–2,270; 1,670–1,961 |
| palias: `Ki<A>: associate X is A`, `V is Copy` | `T is Copy`; `T.(K3<i32>).X is i32` | none; + | 1; 3 | 4; 9 | 19; 259 | 20–22; 122–148 |
| deep2: `Wi: Self is Self.(Wi).X.(Wi).X`, `V is Copy` | `T is Copy`; `T.(Holder).Item is Copy` | none; + | 4; 2 | 13; 16 | 516; 64 | 191–254; 54–58 |
| shared (R4-B1): `Ki<A>: Self is It; Self.(It).Item is A`, dead, n = 10 … 1,600; shared-pos (with `S is K1<T>`, `S is K2<U>`, `U is Copy`), n = 10 / 40 / 400 | `T is Copy`; `W is Copy` | none; none | 1; 5 | 3; 55 | 1; 2,748 | 3–4; 1,939–1,999 |
| shared-live: every Ki conformed to (the live witness); n = 10 / 40 / 100 / 400 | `T is Copy` | none | 2 | 68 / 248 / 608 / 2,408 | 1,085 / 3,665 / 8,825 / 34,625 | 866 / 3,818 / 26,421 / 339,007 |
| mix: `Wi: associate X is Self` and `Ki<A>: associate X is A`, `T is W3` | `T is Copy` | none | 1 | 4 | 19 | 12–13 |
| open-live (B, rule 8 up): n live `Wi: associate X is List<Self>`; n = 10 / 40 / 100 / 400 | `T is Copy` | none | 2 | 43 / 163 / 403 / 1,603 | 41 / 161 / 401 / 1,601 | 24 / 67 / 157 / 606 |

`152dda62` at the same sizes: selfid/palias/deep2 `T is Copy` J 145 / 5,605 / 22,405, `T.(W3).X is T` J 420 / 12,510 / 49,710, shared
V 256 / 906 / 3,406 at 10 / 20 / 40 and Resource at 100, shared-pos Resource at 40, mix J 289 / 11,209 at 10 / 400, shared-live V 456 /
6,606 and J 6,782 / 290,702 at 10 / 40. Unrelated requests read the same rows whatever n once dead rows are no rows (rule 4); where
every row is live, V and J are linear (shared-live: 2n demands, n subscriptions owned by every demand; open-live: V 4n + 3, J 4n + 1,
rule 8 up). Model 185 `List<i32> is Cp`: 12 rounds, V 102, J 84,760, 49 ms (`152dda62`: 17 rounds, V 781, J 1.63 M, 1.6 s). Nested
model 39 `T is V`: `+ Complete` under a 1,000-item limit, 15 rounds, V 211, J 665 k, 0.41 s (`152dda62`: Resource at 3,000 items).
Seed 78 nested model 2 `T is V`: `+` in round 2, then Resource at 3,000 items (12 rounds, V 3,872, 3.9 s) and at 65,536 (16 rounds,
V 97,898, J 38.7 M, 175 s; before B: `+ Complete`, 4 rounds, V 13, 1.6 ms): the structure need enumerates the goal classes' identity
closure in a nested-projection model (§9).

## 8. C2's input list

C0's census ([c0/census-raw.tsv](../c0/census-raw.tsv)) mapped to the operations; C2 routes each read as a pure move, C3 replaces the answer.

| Read site (method) | Operation | Behavior change at C3 |
| --- | --- | --- |
| `AvailableConstraintFact` (22 calls in 16 files), `ProveConstraint` scans (`Constraints.cs:640`, `:642`) and its presence gate; `ProveConformance` premise loop (`Contracts.cs:681`); `ResolveConformance`, `ProveConformanceConditions`, `ProveMemberConditions` Invalid walks | Supply | derived, not stored; the `HasAssociatedProjection` gate goes; the premise part only — verification leaves the proof in its own unit (N6) |
| `SymbolicCapability` (`Capabilities.cs:536`); `AvailableEffectBounds` (`EffectDeclarations.cs:217`), `AvailableCallableEffects` (`CallableEffects.cs:33`), `EffectPremises` with `TyCtxt.Copy`, `Sealed`, `RecursiveBase` | Supply (refinements of the intrinsic); Enumerate (identity members); Enumerate / Supply in the definition's view | unspelled projections (G170); derived bounds |
| `FindAssociated` (`AssociatedTypes.cs:114`), `RequirementMember` (`ContractCalls.cs:114`), `HasContractFact` (`Indexers.cs:209`), `SelectValueCall` (`CallableSelection.cs:49`, `:63`), `TryCallable` (`ValueCalls.cs:296`), `CallableBoundSignatures` (`Retention.cs:45`); `ArithmeticNumericDomain`, `SelectArithmetic` (`ArithmeticOperators.cs:44`, `:205`), `ValidateArithmeticDirections` (`ArithmeticDiagnostics.cs:51`, `:68`) | Enumerate (Contracts declaring the name; arithmetic Contracts) | derived candidates, spelling-independent; pending ones stay discoverable for ambiguity; arithmetic premises become rows |
| `HasParameterIdentity`, `SubstituteIdentityPremises`, `IdentityPremise` (`TypeIdentity.cs:12`–`:149`), `ContractTypeCore` (`AssociatedTypes.cs:684`), `NormalizeProofConstraint`/`NormalizeProofType` (`Constraints.cs:685`, `:714`), `HasValueRole` (`ConstraintValidation.cs:673`), `ValidateContractDeclarations` (`Contracts.cs:887`), `FailConstructorAbsence` (`Structs.cs:329`) | Enumerate (identity members, no declarations) | the identity unit (OQ5); the `HasParameterIdentity` flag goes |
| `AdmittedSemantics`, `ValidateConstraintEnvironments` (`Constraints.cs:401`), `ValidateConstraintUses`, `RecordReferenceConstraint`, `ReportQualification`, `ValidateConditionalBlock`; `DisjointConformanceFacts`, `RefutedOverlapInputs`, `ValidateAssociatedOverlapAgreement` | none: input validation over DirectFacts; Supply in the composed scope | Resource never marks an environment Invalid; composed scopes hold inputs only |
| **Declaration validation** (B 3.10): the bounded forward closure of a scope's identity — `ValidateConstraintEnvironments` over `DirectFacts` today — reporting a structural conflict, a cycle or a B 3.7 obligation failure once, at the declaration whose premises first produce it | C's input at C3 (not a request's answer) | a request reaching such a premise error answers `+-` on the identities it touches; one it never reaches is the declaration's |

Writers: `AddConstraintFact` stays for direct inputs only; `AddContractPremises` (4 callers), `ExpandScopeContractPremises` (6) and
`AddArithmeticPremises` go at C3; the seven scope composers copy inputs and substituted clauses as inputs; `DerivedFacts` and the
`HasAssociatedProjection` flag go. S5's view (R4-M87) must be SPEC 8.3.3's search over the supply rows with argument and identity
routes, closed Types included (N3). **Review additions.** `order`, the scaling workloads, the profiles' slow cases and §4's curve
become C3 regressions, with a `src/Benchmark` premise workload. Not modeled (a row or outside supply at C3): PrimitiveInteger →
Position, arithmetic, Semantics and Callable constraints, Origin families, generic value-side conformances, structured conditions,
identity conditions on conformances (`fill`'s second query is vacuous today: `Condition` requires `Is`). **Expected size** (N9,
DECISIONS Q5): the four ContractSolver files about 19,000 tokens (prototype 4,053 lines with string keys and LINQ), TypeIdentity's
classes about 3,000, `ConstraintProof.Resource` and the record 300; removed at C3 about 4,000 tokens of premise supply, scans,
`TypeIdentity.cs`, `DerivedFacts`, copies and guards.

## 9. Decisions, departures, unverified

| Question | Decision | By |
| --- | --- | --- |
| Q1/OQ1 Resource diagnostic; Q3/OQ3 "depends"; Q5/OQ2 limits; OQ4 D5 freeze | one shared Binding analysis-limit code, the limit as an argument, the last free `BindingFailure` entry (also G172's budget, G174's fixed points); "depends" per derivation, no read sets in C0–C4, G172 decides the flow-back (prefer option (c)); rounds and items (stored chain promises count), 4,096 and 65,536 provisional, confirmed by C3 (§4's gate), in IMPL 21.3.2/A.4 at C3; where C3 shows only the proof changes and the old Emission generates correctly the default pipeline accepts, otherwise a located `Unsupported_Kd` until R6-check | owner, 2026-10-21 |
| R2-B1 identity selection | one need-driven rule for every identity side (§1), not a whitelist or a hold | owner, 2026-10-21 (「一般規則 (推奨)」) |
| Q2 transport; Q4 identity classes, OQ5 scope | `ConstraintProof` gains a fifth member `Resource` (§4); TypeIdentity owns the classes (and their decomposition, B), C reads them, a separate reproduced identity unit before C3 | planning session (N10) |
| R4-B1/R4-B2 family (B3, R2-B1, R3-B2; R3-B1) | the general rule as a representation: promises as shapes, members as sets, filters by class (§1 rules 1–5); no whitelist, no hold | owner's standing choice; planning session's round-5 design (track S, concise code) |
| Round 5: subscriptions (D1); expansion memo (D2); a shape variable at a real node (D3); re-keying (D4) | owned rows keyed by what they subscribe to (the live witness had 2n² per-requester subscriptions: shared-live-n V 258 / 3,408 / 20,508 at 10 / 40 / 100; owned: §7); an expansion is the lookup memo's rule (once per (promise, pattern, owner, sign) per round; without it model 185 did not finish round 6 in 150 s); a shape variable meeting a real node binds to its class; stored promises re-keyed from their first producer | planning session, 2026-10-22 |
| D5: a Contract reference is not a Type (R4-N1) | a reference agrees only with the same reference head, arguments by the kid rule, never deferred; a reference class gains no promise head and meets only the same reference (in `Kid`, `VirtualHeads` and `Meet`). Before it a reference kid with a free argument agreed with any class: seed 4245 model 14 `T.(K1).Y is Cp or K0` was Resource at 3,000 items where `152dda62` completed in 1 round; with D5 it is `none Complete` in 1 round, and seed 7 model 322 `List<i32> is Cp` is Resource (items 3,041) after 62 rounds in 3.8 s where it took over 5 minutes | planning session, 2026-10-22 (both tracks) |
| **Proposal B** (the owner's spec change, 2026-10-22): D6 decomposition lives in the structure; D7 Refuted classes never meet and never agree; D8 promises and class demands decompose with the structure; D9 the structure need, and at a limit only Proven (and Error) stand | §1 rules 6–9, from the addendum `g170-c1-r5-specB.md` (two design-review rounds: constructors only have a head — projections stay deferred; `Refuted` owned by the Solver with the negated-fact base case; the structure need's kid classes carry `NegatedFor`). The brief's fixed decision "identity is never decomposed (SPEC 8.2.3)" is superseded: identity is still not a row; the structure decomposes it, never projections or Contract arguments (B 3.5). Everything else in the brief's §3 stands (R2-B1, OQ2's two limits, order-free counts, D5, the prohibitions). Declaration validation (B 3.10) is C3's (§8) | owner (the proposal), planning session (the addendum), 2026-10-22 |
| Open: the structure need's cost; growing supplies; model 12 | "Every promise at the goal classes" enumerates, in a nested-projection model, the goal classes' identity closure (each fired projection promise adds a node that matches more sides): seed 78 nested model 2 `T is V` V 13 → 97,898 (§7); on seed 4242 the default profile's Resource answers rose from 6 to 37 of 3,963 (rule 8 up at ground constructor kids — i32 under every `… is List<i32>` side — requests those rows with Self unbound: star demands; the structural profile 0 → 51 of 3,660). Measured option, not taken (a design decision): expanding only constructor-headed, alias and bare-variable promises at those classes (a projection promise stays a chain base) restores model 2 (`+ Complete`, 4 rounds, V 18, 89 ms) with fixed 138/138 and 0 disagreements on `random 200 4242` (default 31 Resource, structural 21) and `random 400 20261022` (94); its two slowest models (376: 296 s, 179: 216 s) persist, so they are rule 8 up's, not the structure need's. Growing supplies: seed 7 model 322 `List<i32> is Cp` is Resource at 3,000 items after 62 rounds in 3.8 s (`152dda62`: 9 s; before D5 over 5 minutes). Model 12: §4 | owner / planning session |

Departures from PlanB §4: identity classes are part of selection; every identity side is lazy under one rule; chains, needs,
meetings, bound expansions and subscriptions are rows (§1); verification is inside proof today (§5). **Unverified**: production cost
and item-limit time (C3 gates); effect premises, Enumerate lookups, arithmetic, composed scopes, identity conditions on conformances,
B 3.7 as the inference owner's view (C3/G172), G172's admissibility, mixed Owned/premise cycles (N5); the open rows above.

## 10. Review findings: rulings and evidence

| Finding | Ruling | Evidence |
| --- | --- | --- |
| R4-B1 same-key virtual nodes: chains per (row, base), agreements per pair | rule 1: a promise is identified by (class, shape), rows and chains are its members, a join agrees each pair of promises once, chains forward to promises; rule 4 removes dead rows from every index | shared-n (dead): V 3 / J 1 at n = 3…1,600 (`152dda62`: V 256 / 906 / 3,406 at 10 / 20 / 40, Resource at 100); shared-pos: V 55 / J 2,748 at 10, 40 and 400 (`152dda62`: 1,121 / 35,543 at 10, Resource at 40); shared-live: V 68 / 248 / 608 / 2,408 and J 1,085 / 3,665 / 8,825 / 34,625 at 10 / 40 / 100 / 400, Complete (`152dda62`: V 456 / 6,606, J 6,782 / 290,702 at 10 / 40, unfinished at 100); mix: V 4 / J 19 at 10…1,600 (`152dda62`: J 289 / 11,209 at 10 / 400); fixed cases `shared identity sides` ×3 and `mixed`, each failing on `152dda62` |
| R4-B2 a kid that is not a Type yet pruned by its head | rules 2, 3 and 7: only the top is matched by head, every kid by the kid rule; a kid with a free variable has neither head nor class and is decided once built, by every filter (`KidHeads`, `Fits`, `Agree`); under B a constructor kid is unified against the class's constructor, a projection kid stays deferred | shape5 in its B-valid form (`B0 is i32`) `T.(Gz).Z is Copy` `+` and one level deeper `+`; the reviewer's seven variants `+` ([results/witnesses.txt](results/witnesses.txt)); the original shape5 is a declaration error under B (below) |
| Inherited (track S design review): need rows untagged | rule 5: need rows are tagged effects of the round, like chains and meetings (`Effects.NeedTags`) | seed 20261022 model 41 `T is Cp` (`+ P`) and its minimal form (`+ C`), both `none` on `152dda62` (found by `random 50 20261022`) |
| R3-B1 matching against a virtual node compares bindings, not classes; R3-B2 a self-kid exclusion over a quadratic join | a promise matches as a node, modulo classes (rule 2), in needs (a) and meetings (b) alike; every node of one class probes the other's promises through the index, both ways | fixed cases (an involution law twice, a nested identity side, a bound pattern, a free Contract argument), each `none` on `f550b474`, `+` here and in the reference; §7: palias, deep2 and selfid with `V is Copy` flat |
| **B, rule 6/7** (addendum design review round 1: B1 projections are abstract, B2 a negated fact refutes a component, N5 the head test only where both sides are constructors) | `Constructor(s)` answers a head for a Prim/Con/Ref node or class only; `Kid` defers everything else; `Refuted` is the Solver's, over EGraph's constructors, proper parts and the request's negated facts, the reference's over its own atoms; a conflicting class agrees with nothing | proj-kids `V is Cp` + (through the V~W meeting's kid agreement `$1.(K).X` vs `$1.(K).Y`), proj-vs-con + (a projection kid against a class whose constructor is `List<i64>`), negated-component `T is Option<i32>` − with `U is not i32`; the old `+-` cases conflict, cyclic, conflict-reached-by-row |
| **B, rule 8** (down/up) | the decomposition members of a promise (`Parts`, expanded through the lifted pattern); the upward index `constructorKids` beside `byShape` (which cannot answer a kid head at depth: it is keyed by the scanned side's own head and holds one side per row); an alias or bare-variable promise of p is expanded for any need at a kid of p; parents are visited through the requester's lookup, so chain gains and merges at p wake it, and a new constructor node wakes the rows that visited its kids; the upward e-match is modulo promises (`Match` in expand mode) | number `+` (the clause row found through its projection kid, the binding's `Option<i32>` decomposed at Inner), two-variable-kids `+` (variable kids), ground-kid `+`, decomp-down `+`; all `none` on `152dda62`; open-live V 43 / 163 / 403 / 1,603 (4n + 3) and J 4n + 1 at 10 / 40 / 100 / 400: linear, as the addendum expects |
| **B, rule 9 / the structure need** (B0's finding; addendum design review round 2: kid classes carry `NegatedFor`) | every class the negative polarity reaches requests every promise at it and selects the negated rows; the goal pair's meeting row stays (an overlap, kept this unit); an established Refuted is Resource at a limit, deliberately | projection-side `T.(K).X is Array<U>` −, negated-row-kid `U is Option<i32>` −; the 19 B0 disagreements of `random 200 4242` (default: `S1.(K1).X is i64` with the binding promise `List<i32>`; structural: `U is i32` by decomposition) now 0; cost: §9's open row |
| **Old cases whose meaning B changes** | "an identity goal's classes meet" (`i32 is List<i32>`, `T.(K2).Y` ≡ both): `+-` — a Prim and a Con in one class is a structural conflict (B 3.3, 3.10) the request reaches through the structure need; "a bound variable meets the subject class" (binding `Y = i64` against `associate Y is i32`: a B 3.7 obligation failure; i32 and i64 never meet) and shape5 / one level deeper (`Opt<B0> is List<i32>` is a structural conflict): `none` where the reference, materializing the conflict, says `+!` | the three cases with the reason beside each; the B-valid shape5 witnesses beside them |
| Inherited: E and the item count were order-dependent on the nested profile | `Rekey` moves (not copies) the supports of facts that became one, so each support counts once whichever row survives | `order nested 30 77`: 20 differ on `152dda62`, 15 on B0 (model 1 `T is V`: E 13 vs 12, peak 29 vs 28), 0 here |
| R3-N1 J; R3-N2 oracle guard; R3-N3 generator; R4-N3 parser; round 2 (R2-B1, R2-B2, R2-N1–N5); round 1 (B1–B4, N1–N12) | J counts every probe and agreement (§4); exclusions stated with the machine's state (§2); nested profile and the workloads (C3 regressions); R4-N3 prototype only; one need-driven rule for every identity side (owner), Enumerate's same-declaration facts meet, two-parameter profile, item-limit time a C3 gate; no work limit, J order-free, no read sets, B3 flat, published Resource state and `ConstraintProof.Resource` | §2, §4, §7; fixed cases; `order`; `resource` transport |

**Round 5 deletions** (`Solver.cs`): `Virtual`, `VirtualKey`, `VirtualKeyOf`, `keys`, `storedKeys`, `StoredKeys`, `SameShape`, `HasFree`, `AgreeClass`,
`ExpandAt`, `MayUnify`, `Settle`, `Passes`, `IsAlias`, the term-head `KidHeads`, the `KidAt` filter of stored chains, the per-entry chain creation
in `Produce`, the pairwise loop in `MeetVirtuals`, `Sub.Requester`, `SubKey.Requester`; under B `Reference(int)`, `Kid`'s deferred-true branch for a
constructor node, `EGraph.Refuted`. Two-file total 4,053 lines (`Solver.cs` 3,856 + `Shapes.cs` 197) vs `152dda62`'s 3,364 (+689; B +509); `EGraph.cs` +74; `Reference.cs` +178.
