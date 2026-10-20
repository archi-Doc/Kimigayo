# G170 Premise Dependencies and Inference Plan

Status: 一部取り込み (open; selected implementation plan, no specification integration or implementation).
Request: 2026-10-20, compare several solutions and implementation plans, independently review them, and select the
most fundamental, concise and efficient design consistent with the Kimigayo Principles.
Source baseline: `a415c755fe239e61c6754940923cbbba6b2fef0f`; revised at `bc3fcd3f` against the Principles (§8).

Select **C: demand-driven declaration premises with explicit evidence dependencies**. This is a design selection,
not a measured performance result. It replaces the G170 supplier design in §§3–4 and supply work PS0–PS5 in the
[earlier plan](2026-10-20%20Canonical%20Premise%20Supply%20and%20Transferable%20Plan.md); that plan's Transferable
T0–T3, S5 connection and R4-M83 hold remain. This document sharpens the inference boundary and executable design
gates; it does not declare the earlier direction implemented or approve its proposed diagnostic vocabulary.
References to the earlier PS5 supply exit now mean C4 here; Transferable's remaining tests and exit gates still apply.

## 1. Problem, evidence and required meaning

G170 records a missing proof, not a demonstrated infinite loop, unsound acceptance or associated-value inference
cycle. The original planning unit ran no compiler, tests or benchmarks; the revision ran one CLI check of the
inference example below (§8). The historical observation is
[PLAN G170](../../docs/dev/PLAN.md) and the corrected
[S5 intake](../INTEGRATED.md); the retained program is
[S5Probe.cs](../Sketches/A5%20Step%202/S5Probe.cs). Its older labels are not an acceptance oracle.

```kimi
contract Tree
    associate Child is Tree

contract CopyTree: Tree, Copy
    Self.(Tree).Child is CopyTree

func inspect<T>(value: T.(Tree).Child.(Tree).Child)
    T is CopyTree
    ()
```

By SPEC §8.3.2, the input `T is CopyTree` supplies `T.Child is CopyTree`, then `T.Child.Child is CopyTree`, then
`T.Child.Child is Copy`. This is a finite derivation from an explicit abstract premise; no concrete implementation
of CopyTree is needed. Recorded behavior: the canonical lexical and effect queries of the same Type and scope are
Unknown. An unused reference parameter spelling the projection through CopyTree, before or after `value`, makes
the original Type Proven. Zero `Binding.Issues` did not mean ordinary acceptance; the helper programs also had
ordinary effect-bound errors, while the shorter helper passed MIR A4. Reproduce these observations on the exact
implementation head before changing code.

The required design chain is:

> SPEC §8.3 supplies all permitted independent declaration consequences, regardless of spelling and checking order
> → the mutable fact bag records only consequences expanded so far and insufficient dependency structure
> → Binding's Contract solver owns premise derivation and evidence availability
> → syntax-triggered expansion, derived-fact copying and independent consumer scans become unnecessary.

Three different dependencies must retain different meanings:

| Dependency | Required treatment |
| --- | --- |
| Recursive abstract premise: `T: CopyTree → T.Child: CopyTree → …` | Derive a finite requested consequence; never eagerly enumerate every descendant. |
| Proof dependency: proving A asks for B, which asks for A | Re-entry contributes an edge, not evidence. An independent permitted derivation may still settle the goal. |
| Associated-value inference: a result depends on a value inferred in its own definition | SPEC §8.5.5 excludes that evidence, even if the value dependency graph is acyclic. |

For the last row, suppose `A.Item` is inferred as i32, `B.Value` depends on `A.Item`, and `A.Other` depends on
`B.Value`. The graph `A.Item → B.Value → A.Other` has no identity cycle, but it cannot infer `A.Other`: its evidence
depends on an inferred value of A. Explicitly specifying `A.Item` changes that evidence boundary. Conversely, an
independently determined value of B may be used without waiting for B's remaining associated values.

The existing inference owner does not meet that last rule, a cause separate from G170
([PLAN G172](../../docs/dev/PLAN.md)). At the revision head, SPEC §8.5.5's own PA/PB example is rejected in both
declaration orders, and the order decides which definition's inference fails: `AssociatedTypeInferenceFailed_Kd` on
`Q.W` when PA is declared first, on `P.X` when PB is, the other definition then failing its conformance (a CLI check,
§8). Source explanation, a hypothesis until a test confirms it: `InferAssociatedTypes` extracts evidence requirement
by requirement and `ResolveAssociated` withholds a definition's values until its whole batch completes. With PA
first, PB's batch starts while `x`'s result is normalized, before `z` is extracted, so PA.Z is not yet determined.
The repair is per-value demand in AssociatedInference (determine the needed value, or suspend the extraction that
needs it), which the rule's pre-rewrite text already required; publication granularity alone would make acceptance
depend on requirement order. §4.2 states what C needs from that owner.

Authority: [SPEC §8.2.3, §8.3, §8.5.5 and §8.6.3](../../docs/spec/08-generics-constraints-and-contracts.md).
Concrete conformance to a Contract classified recursive by §8.3.3 is prohibited, a stronger rule than merely
discarding an unseeded proof cycle. Structural capability fixed points keep their own SPEC rules. Neither rule
is replaced by a generic "cycle succeeds" or "cycle fails" policy. Proof uses no member search or guessed Type.

## 2. Owning code and common cause

Paths are under `src/Kimi/Compiler/Binding/`; methods are stable navigation anchors at the source baseline.

| Site | Present behavior / obligation |
| --- | --- |
| `Binding.Contracts.cs`: `ExpandScopeContractPremises` | Takes one snapshot of Facts; additions do not cause another expansion round. Six callers: `ExpandContractPremises` (every declaration scope, after eagerly adding a Contract scope's own, inherited and ancestor clauses for its Self), `BindConditionalDeclarations`, `AddInheritedPremises`, `CompatibleRequirement` (ContractMatching), `PrepareVirtualHeaderPremises` and `PrepareVirtualOverridePremises`. |
| `Binding.Contracts.cs`: `AddContractPremises` | Substitutes own and inherited clauses and adds ancestor facts. Reuse these rules, not their eager publication policy. |
| `Binding.AssociatedTypes.cs`: `BindAssociatedProjection` | Adds premises for the proven Contract chosen by the written qualifier. This makes querying syntax a semantic producer. |
| `Binding.ContractCalls.cs`: `RequirementMember` | Member access adds premises for each proven Contract of the referenced receiver, the same syntax trigger. |
| `Binding.Constraints.Model.cs`: `ConstraintEnvironment` | Facts, DirectFacts and DerivedFacts coexist; a derived row records a source Contract, not the complete antecedent/support graph. |
| `Binding.Constraints.cs`: `AvailableConstraintFact`, `AvailableContractPremise`, `ProveConstraint` | Checks existing materialized evidence and declaration availability; a recursion guard cannot manufacture a missing premise. |
| `Binding.Capabilities.cs`: `SymbolicCapability` and capability work | Reads the same supply, with purpose-sensitive structural judgments and cached results. |
| `Binding.TypeIdentity.cs`, `Binding.AssociatedTypes.cs`, `Binding.Constraints.cs` | Identity, projection normalization and cached presence flags must see derivable identities, not only already stored ones. |
| `Binding.AssociatedInference.cs`: `InferAssociatedTypes`, `ExtractAssociatedEvidence`; `Binding.AssociatedTypes.cs`: `ResolveAssociated` | Own frozen evidence, identification, extraction and completion. Preserve these responsibilities; extend dependency publication only where the new consumer needs it. |

The source explains the recorded G170 effect: Tree-qualified projections expand Tree, although a stronger CopyTree
premise can apply to the same receiver and associated identity. A CopyTree-qualified occurrence triggers the missing
expansion. This is a source-supported explanation of the historical observation, not a new execution measurement.

The cutover census covers the following meanings, including indirect helpers and presence flags:

| Meaning | Existing reader/writer families |
| --- | --- |
| Proof and capabilities | Constraints, Capabilities, Contracts, conformance validation |
| Candidate discovery | AssociatedTypes, ContractCalls, Indexers, CallableSelection, ValueCalls, Retention |
| Identity and formation | TypeIdentity, AssociatedTypes, ConstraintValidation, Structs' identity shortcut |
| Arithmetic and effects | ArithmeticOperators/Diagnostics/Contracts, EffectDeclarations, CallableEffects, EffectObligations, TyCtxt.Effects |
| Scope composition | ConditionalConformances, ContractAgreement/Matching, InheritedConformances, VirtualOverrides |
| Source validity and inference | ConstraintValidation/projection certificates, AssociatedInference, associated bindings and their consumers |

[CODEMAP](../../docs/dev/CODEMAP.md) owns navigation. The inventory is not permission to duplicate inference,
capability or overload rules in a new solver. The shared responsibility is supplying evidence and dependencies.

## 3. Alternatives and their implementation plans

All estimates below are structural expectations, not line budgets or measured speed claims. Each plan must be
judged on the same SPEC-valid workload; a faster incomplete answer is not an improvement.

### A. More eager expansion: smallest symptomatic change

**Design.** Repeat `ExpandScopeContractPremises` for a fixed number of rounds, or until the fact bag stops growing.
Keeping the existing bag and consumers makes the immediate diff very small.

**Plan.** A0 reproduces G170 and measures current expansion; A1 prototypes repeated expansion outside production;
A2 tests deeper projections, recursive `Child` chains and changing generic arguments. No production cutover follows
unless it satisfies the shared contract; the counterexamples below already disqualify this version.

**Failure.** A fixed round count makes acceptance depend on depth. Unbounded iteration need not terminate because
`Child`, `Child.Child`, … are distinct Types. Treating a limit as Unknown is forbidden; a proper Resource failure
avoids a false logical answer but still does unnecessary unrelated expansion. Syntax effects, shallow provenance,
inference contexts and consumer scans survive. Expected size: smallest addition, almost no deletion. Expected cost:
repeated whole-bag work and potentially unbounded retained Types. **Reject as the formal solution.** G170 is not an
observed urgent miscompile that would justify an AGENTS temporary symptom patch.

### B. Subject-directed expansion within the current owner

**Design.** At a proof or lookup request, take the requested Type and its projection prefixes. Expand every available
Contract on those subjects, including refinements, until no relevant facts change. Deduplicate subject/Contract
instances and stop modifying the scope merely because a projection was written. Keep existing proposition judgment.

**Plan.** B0 captures G170 plus identity/deep-head counterexamples; B1 separates expansion from syntax with unchanged
behavior; B2 prototypes the subject worklist and a common subject-candidate operation; B3 measures depth/width and
migrates the full consumer census only if the relevance and dependency proofs pass. Resource/finality and frozen
inference checks are prerequisites to B3, not cleanup afterwards.

**Failure of the small version.** A declaration may constrain a deep subject, publish an identity reaching a different
subject, or carry relevant arguments not visible in the requested prefixes. Ignoring those misses evidence; following
every new subject restores A's unbounded expansion. Keeping old derived rows also loses antecedent dependencies.
Expected size: small-to-medium addition plus deletion of syntax expansion; useful on simple chains, but repeated
fact scans remain. Completing relevance, provenance and all consumers makes B approach C. **Reject B as a complete
design; retain its subject-candidate interface in C.** No shape-based fallback to B is allowed.

### C. Demand-driven rules and evidence dependencies — selected

**Design.** Retain finite declared assumptions and existing bound Contract clauses. Evaluate requested propositions
or subject candidates against them using one request-local dependency graph. Each derivation retains its bound
antecedent, declaration validity and the associated bindings it read under its evidence view. Inference supplies its
own frozen evidence context and admissibility; it does not become an unrestricted fixed point. Section 4 specifies
the contract; section 6 gives the implementation units.

**Expected size and cost.** Medium addition for request work and support edges, with the broadest directly justified
deletion of duplicate supply paths. Reuse interned Types/constraints and existing substitution/identity rules. Work
tracks requested rule instances and support edges rather than all descendants. Request-local reuse avoids repeated
work within a request; resetting evidence between requests avoids another persistent invalidation system. Repeated
top-level queries are a measurable risk. This is the shortest credible complete design among these alternatives,
subject to the executable gate and code/deletion census, not a promise of net negative source size.

### D. Declaration-level symbolic consequence network

**Design.** Compile Contract clauses into a finite graph of declaration-level transformations over Self, bound
parameters and associated paths. Summarize recursive components once; answer proof and lookup by applying summaries
to a scope and Type. Preserve inference and structural capability rules as distinct operations over shared support.

**Plan.** D0 defines edge/substitution meaning and recursive classification; D1 prototypes finite summaries for deep
paths, identities, changing arguments and both polarities; D2 proves composition and frozen-source correctness;
D3 replaces the same consumer census and old fact bag; D4 compares cold build, repeated queries, edits and retained
summary memory against C. No assumption that finite declarations imply finite instantiated consequences is permitted.

**Tradeoff.** Potentially amortizes many repeated queries better than C. However `C<A> → Child is C<List<A>>` can
generate unbounded substitutions. A plain declaration SCC or finite set of paths is not a complete summary. A sound
general summary algebra, source dependencies and identity integration add code and a second representation of
existing clauses. If summaries instead instantiate obligations lazily, D converges to C plus an optimization.
Expected size: largest initial addition; greatest cold/retained-memory risk, possible warm benefit. **Do not select
for G170.** Reconsider only if C's equivalent-work measurements justify a specific summary/index and its semantics.

### Comparison and choice

| Criterion | A | B | C | D |
| --- | --- | --- | --- | --- |
| Fixes recorded example | Likely with enough rounds | Expected | Required gate | Required gate |
| General §8.3 coverage | No | Prefix-only version: no | Explicit completeness gate | Summary expressiveness gate |
| Frozen inference and source validity | Old limitations remain | Needs extra dependency design | Explicit input/support contract | Needs same contract plus summary composition |
| Canonical path / deletions | Weak | Partial | All supply consumers, same unit | All consumers, larger representation change |
| Expected implementation size | Smallest but incomplete | Small until made general | Smallest credible complete option | Largest |
| Expected performance | Eager and growing | Good for simple subjects | Demand-proportional, requery risk | Potential warm reuse, cold/memory risk |
| Principles | Depth/spelling leaks | Relevance leaks unless enlarged | One owner, explicit scope/support, bounded work | Viable only with a proved summary contract |

Select C for coherence first, deletion-adjusted size second, measured speed third (DECISIONS Q5). D is not more
fundamental merely because it rewrites more of Binding: C removes the faulty completeness assumption at its owner.
The compiler-server obligation is also clearer: completed judgments and located producer failures remain tied to
the source/configuration used, with no silently partial proof exposed as a stable answer.

## 4. Selected contract and algorithm

### 4.1 Ownership, representation and entry points

Bring forward only the Contract-solver premise responsibility of R7. Use `ContractSolver` concern files under
Binding, with an entry contract stating inputs, outputs, invariants and prohibitions. Exact dependency-facing
members are settled by C1/C2 before production edits; no service interface, callback utility bag or second Type/
proposition language. Binding's existing public proof and TyCtxt views remain adapters to their one semantic owner.
Record this responsibility and representation in DECISIONS/COMPILER_ARCHITECTURE at the first implementing commit.

Retain `BoundConstraint`/`ConstraintKey`, bound Contract references, declaration clause storage, substitution and
the existing identity owner. The finite input environment contains declared assumptions and source references;
derived results are request state, never new assumptions copied into another scope.

Two semantic entry operations share rule application, both taking the evidence view (§4.2): supply the available
evidence for a bound proposition and for its negation; enumerate the constraints available or discoverable for a
bound subject. Each answer states whether it is complete, provisional or a Resource failure. `ProveConstraint`'s
four-valued judgment and the concrete and capability judgments keep their owners and read evidence through the
first; identity normalization reads a subject's available identity evidence, and lookup its candidates, through the
second. Enumeration retains pending declarations for discovery and ambiguity; proof/normalization uses only
available evidence. A discovered candidate does not prove its prerequisites. Neither operation silently truncates,
resolves an unbound program Type by guessing, or changes overload selection rules.

### 4.2 Evidence view and associated inference

A request identifies its premise scope, source-completion state and admissible evidence. Ordinary proof uses that
scope's valid evidence. Inference uses the existing conformance path's frozen premises and the current nominal
definition as the exclusion boundary. All conformances and merged fragments of that definition share this boundary;
nested definitions are separate. A requirement's private premises never become path-wide inference evidence.

The request key is the raw interned proposition (or subject demand) plus its evidence view. Distinct capability
derivation purposes remain distinct. A normalized representative alone is unsafe while identity is being proved.
A plain `(Type, scope)` key is insufficient when frozen and ordinary contexts differ. The evidence view is a semantic
input replacing hidden dependence on collection state, not a boolean flag choosing two implementations.

The inference owner still identifies once, extracts only allowed declared-result structure, checks every usable
candidate for agreement and publishes completed values. Pending matches are reconsidered for compatibility only;
missing/ambiguous matches do not become new evidence after completion. Bodies and call sites supply nothing.

Admissibility of an inferred value is a fact the inference owner publishes, not a second rule inside C. C reads an
associated binding only through the owner's state for the request's view. In frozen definition D's view, every
value inferred in D, and every value whose evidence depends directly or transitively on one, is inadmissible; a
determined value of another definition outside that set is admissible while that definition still has unresolved
values. A derivation that read an inadmissible value supplies nothing in that view, and the view key keeps its rows
from other views. Do not reject an independent support merely because another path is cyclic. Do not use an
individual matching candidate as a certificate while omitting the producer's conflict/completion obligations.
Other definitions complete under their own rules; a caller's view controls admissibility of their published
evidence, not the global meaning of their associated values.

Dependencies also flow back. Inference evidence passes through premises C supplies, such as projection
normalization over identity facts and conformance-condition proofs. The associated bindings read by those supports,
transitively through step 4's edges, are returned to the owner, which includes them in the inferred value's
published dependencies; a reused row returns its recorded reads. Which supports' reads count must not depend on
search order: C1 fixes either every admissible support, searched exhaustively, or one canonical minimal support.
If §8.5.5's "depends" does not decide between them, that is reported as a SPEC gap, not chosen here.

Today the owner withholds every value of an in-progress definition and collects evidence requirement by requirement
(§1, G172). G172's own unit, per-value demand in AssociatedInference, is not part of C0–C4. C2's entry operations
take the view as an explicit input, derived from today's collection state (`collectingAssociated`) as a pure move;
C3 leaves inference outcomes unchanged under whichever owner state is current. C1 demonstrates the view through real
source binding, not only an abstract graph with manually supplied dependencies, for exclusion and for values of
completed definitions; the admissible value of an in-progress definition is G172's to demonstrate. G170's C4 closure
does not wait for G172.

### 4.3 Demand evaluation

1. Seed finite input assumptions and bound declaration sources from the proper lexical/implementation/frozen view.
   Registered conformances may be candidates; only verified compatible conformances are positive evidence.
2. Select existing clause templates capable of supplying the demand, its negation, required identity or candidate
   subject. Include ancestor clauses and non-immediate/deep subjects. Start with exhaustive template selection in
   the bounded reference model; a production index is an optimization requiring the same answers.
3. Instantiate only template Self/declared parameters using existing bound antecedent instances and substitution.
   In `contract C<A>: Tree` with clause `Self.(Tree).Child is Copy`, the head binds Self but not A (a projection
   through C's own associated Type would carry A in its bound Contract reference, §8.6.1), so a demand
   `T.(Tree).Child is Copy` needs an available bound instance such as `T is C<i32>`. Never invent A, enumerate all
   Types or solve program T to match the rule.
4. Register antecedent, source-validity, identity and associated-binding support dependencies. Tabling merges
   repeated demands within one evidence view. A pending, cyclic or inadmissible derivation never blocks an
   independent one; each polarity is decided by one admissible support or by exhausting its permitted search, and
   alternatives are stored only as far as §4.2's dependency rule needs. New available evidence wakes dependents.
5. Supply only by SPEC logic: conjunction supplies conjuncts; disjunction supplies no disjunct; exact compound
   assumptions stay available as written. Negation, concrete judgments and error propagation stay with
   `ProveConstraint`. Search both polarities and relevant Error dependencies; a first positive hit is not a
   complete answer.
6. Use only proven identity for matching; reconsider dependent matches when identity becomes available. Preserve
   full Type structure, applied Contract/Origin identity and congruence; never decompose equality to infer arguments.
   Any required identity correction belongs to that owner and is separately reproduced/designed, not hidden here.
7. Re-entry records an edge. An unseeded finite proof component contributes no evidence. A finite independent
   support may establish its goal, subject to recursive-Contract prohibition and the inference exclusion above.
   Keep Copy/Owned structural fixed-point semantics and capability purpose keys with their existing owner.
8. An answer is complete only after the relevant work and source dependencies are settled; `ProveConstraint` returns
   Unknown only from complete answers with neither polarity. A still-pending declaration makes the answer
   provisional, not a cached negative or final Unknown.

The hard part is step 2, not drawing the dependency graph. C1 must give a rule-by-rule selection argument covering
identities, absent-head binders and candidate enumeration, then compare against an exhaustive bounded evaluator.
A syntactic-prefix selector is B, not C. Finite templates do not bound generated obligations. Growing substitutions
need explicit work/materialization limits, counted per request so that reaching one never depends on earlier
queries, and a located Resource failure, never an arbitrary depth returning Unknown/Refuted. Limit values, failure
transport and dependent-use stopping are fixed and tested at C1; the existing generation-limit diagnostic cannot be
repurposed. Any new diagnostic/vocabulary takes its applicable approval.

### 4.4 Lifecycle, cost and deletion contract

Start with reusable request-local work arrays/rows and support edges. Nested proof, identity and candidate reads
share the appropriate request/view. Retain capacity after completion, not semantic answers across top-level
requests. This lifetime adds no query-order dependence (§8.3.3 Finality): C's answers, a Resource outcome included,
are functions of the request and its inputs. For the same reason premise-dependent capability evaluated state must
obey it too. Other persistent state that shapes answers, such as capability results kept until
`ClearCapabilityResults` and normalization caches, is in C0's census. Within-request deduplication must not merge
inference views or capability purposes. Cross-request caches require a later measured design that keeps that
independence.

The capability work graph (`capabilityNodes`, its edges and queue) already schedules dependency-driven evaluation
with wake-ups, and G172's per-value demand needs the same. Whichever of C1 and G172's design comes first decides
one worklist for premise, capability and value demands, each node kind keeping its own rule, or records the
contract difference that needs a second scheduler; an unexplained second scheduler fails review.

Binding's existing source-completion and invalidation lifecycle remains authoritative. No query writes an
authoritative premise. If validation callbacks mutate source validity during evaluation, C1/C2 must separate that
publication from the completed read or explicitly restart the affected provisional work under the new state.
Never hide such mutation with a new generation counter or certify a declaration from its own active check.

One coherent semantic cutover removes:

- `ExpandScopeContractPremises` as the completeness mechanism, at all six callers (§2), and its scratch snapshot
  buffer;
- syntax-triggered `AddContractPremises` publication in `BindAssociatedProjection` and `RequirementMember`;
- `ExpandContractPremises`'s eager clauses for a Contract scope's Self, which becomes one input assumption;
- obsolete DerivedFacts materialization/copying in scope composition;
- semantic fact-bag scans and presence shortcuts that can conceal derivable evidence;
- persistent premise-dependent answer state made redundant by the request contract.

Keep direct-input construction, declaration validation, projection/source certificates and inference's own
extraction/identification rules. Account for every remaining raw-fact access as input/storage/validation or remove
it. Do not leave an old/new fallback, an optional mode, a CopyTree case, or two live premise suppliers.

Let V be requested bound obligations, E instantiated support edges and J template/identity join work. Request
storage should follow O(V + E) plus reusable input indexes; running work must expose J as well as V/E. No unconditional
linear-time claim is made: matching, alternative supports and identities can be costly. Avoid enumerating
root-to-goal proof paths; share dependency rows and store alternatives only as step 4 allows. Record cold/warm time,
allocations, retained capacity and repeated top-level work. Add an index/summary only when equivalent-work
measurements justify it.

## 5. Review and adoption gates

Design authors work independently: the local-owner design author examines A/B and shared alternatives; the
structure design author examines symbolic summaries and demand evaluation; the planning session compares them
and owns C's selected contract. The independent reviewer wrote neither design nor proposal, starts from SPEC and
G170, attempts a shorter/general alternative and reviews all four plans plus the chosen one.

Review completion and dispositions are recorded in §8. Implementation still needs C0/C1 evidence,
the exact component/dependency package review, applicable vocabulary approval and the migration disposition.
R4-M79's BU/R5 pause and R4-M83's Transferable hold are not lifted by this planning request. No unrequested SPEC,
SETTLED, translation, guide, library or policy change is made.

Before production cutover, assess the default pipeline: a shared Binding correction can admit previously rejected
programs even if old Analysis/Emission files do not change. Obtain any required D5/O3 disposition with concrete
acceptance/generation evidence before C3. A located legacy boundary, if required, needs its own approved shape and
removal stage; this plan does not invent one. Transferable's finite structural representation remains a separate
T0 gate; G170 closure cannot establish G124 closure, F5 Landed or acceptance of every S5 helper example.

## 6. Selected implementation units

Each unit is serialized within its worktree, verified and committed/pushed separately. Do not land failing desired
regressions. A rejected executable design is redesigned, not supplemented with exceptions; after two blocking
review rounds on the same family, the owner chooses a representation, whitelist or hold under AGENTS.

| Unit | Work / deletions | Completion condition |
| --- | --- | --- |
| C0: exact-head evidence | Reconstruct G170 in its test owner; record same bound Type/scope, ordinary/MIR results and current supply/read census. Record which inference outcomes the frozen view must keep, G172's included. | Absent/before/after spelling and query-order evidence; desired regression fails before repair; no inference defect beyond G172 claimed without a reproducer. Capture source/configuration, sizes/hotspots and existing baselines. |
| C1: executable design | Prototype C's demand selection/support in `draft/Sketches` against an exhaustive bounded reference evaluator; §3's B/D counterexamples are its inputs, not further prototypes. Exercise production source adapters for frozen inference and validity, without shipping a second solver. | Rule-selection argument plus exhaustive-model agreement, real-source dependency witnesses, resource boundary, lifecycle, scheduler (§4.4) and cost results. Exact representation/entry contract and approvals reviewed before production change. Prototype retirement: archive as evidence when C3 regressions cover its cases. |
| C2: preparation | Move existing responsibilities into the ContractSolver target concern and route each §2 read through §4.1's two entry operations, with the view (§4.2), still answered from the existing Facts, wherever that is a pure move; separate source publication/read where necessary, split due files. No consumer-specific mode or parameter is added to make a move pure. Independently repair identity only if a reproduced prerequisite demands it. | Pure moves have unchanged diagnostic/semantic HIR+MIR/generated-fixture evidence per VERIFICATION. Reads left unrouted, writers, scope composers that copy DerivedFacts and presence flags are listed for C3 with the behavior change each needs. Any identity/lifecycle behavior change is a distinct reviewed unit, not called a move. Record moved responsibilities in DECISIONS/architecture/CODEMAP. |
| C3: shared cutover | Replace what answers the entry operations with the reviewed evaluator, evidence view and source dependencies; migrate the consumers C2 listed and delete §4.4's alternatives together. Resource publication ships with evaluation. | G170 plus §7 matrix, allocation/reuse checks, no stale consumer/cache/derived-bag path; default acceptance matches its prior disposition. No partial fallback branch. |
| C4: cost and final review | Measure equivalent workloads, inspect size/deletion and hotspot census; remove new duplication with separately checked units. Independent final review covers C2–C4. | Correct proof/candidates and all source/inference boundaries (G172's in-progress half excepted, §4.2); recorded cold/warm/retained comparisons, native guarantees unchanged; focused checks and Session pass. Only now update verified support and close G170. |

C3 is one semantic unit after preparation, not permission for an unreviewably large change: C2's routing leaves it
the evaluator, the listed consumers and the deletions, with one supplier behind the entry operations at every head.
If C1's concrete census cannot make it reviewable, revise the unit design before coding while preserving one owner
at every mergeable head.
Record new representations and rejected options in the first implementing commit; document intake alone adds no
implementation DECISIONS entry. S5 and Transferable resume through their own existing units after this supply gate.

## 7. Verification and performance acceptance

Reuse owning tests and helpers; add cases at the lowest public observable layer and search for duplicates first.

| Obligation | Cases / existing owner |
| --- | --- |
| Spelling-independent G170 | Same original Type/scope, unused stronger qualifier absent/before/after, depth 1/2/4/8 and generated queries; `AssociatedRefinementCapabilityBindingTest`. |
| General rule selection | Deep non-prefix head, inherited refinements, missing head binder, Contract arguments and Origin families; `AssociatedSubjectConstraintBindingTest`, `AssociatedContractParameterTest`, `AssociatedIdentityTest`. |
| Logic / identity | And/Or/exact compound, negation, contradictory and Error evidence; congruent nested Types with no inverse inference; `ConstraintBindingTest`, `NormalizedConstraintProofBindingTest`, `TypeIdentityTest`, `AssociatedIdentityTest`. |
| Proof recursion / availability | Recursive abstract input versus forbidden concrete conformance; unseeded cycle versus independent support; invalid outer/refined source; `ClosedConformancePrerequisiteBindingTest`, `ProvisionalContractPremiseBindingTest`, `IntrinsicCapabilityTest`. |
| Frozen inference | Same-definition chain; explicit counterpart; missing/ambiguous matches and pending compatibility; a completed external value in either declaration order; dependencies returned through C-supplied normalization and conditions; outcomes unchanged by C3; `AssociatedInferenceTest`. G172's unit owns §8.5.5's PA/PB example in both declaration and requirement orders, the A.Item→B.Value→A.Other exclusion (today, with A first, both A.Other and B.Value fail) and an independent value of an in-progress definition. |
| Inference publication / edits | Merged fragments, distinct bound Contracts, late source/effect failure, remove/restore explicit evidence; `AssociatedInferenceBoundaryTest`, existing inference lifecycle/allocation checks. |
| Discovery and scopes | Pending candidates remain discoverable and ambiguity is preserved; scope unions, conditional/override/property premises, Callable/indexer/arithmetic/effect consumers; existing owners in CODEMAP and §2. |
| Finality | Reverse declaration/query order, ordinary versus frozen views, provisional→final, late invalidation within final validation, reload/edit/repair; `ClosedConstraintLifecycleBindingTest`, `LateConstraintEnvironmentBindingTest`, `ConstraintProjectionCertificateBindingTest`, `UnresolvedCapabilityProofBindingTest`. |
| Resources | Finite limit and limit+1; growing arguments/paths and broad candidate searches; typed located Resource failure propagates before dependent Language rejection, independent errors survive. Owner selected with C1's approved diagnostic contract. |
| Consumption | Direct Copy and TyCtxt.Copy agree under identical premises; actual moved-use/destruction/effect records; `MirInitTest`, `MirEffectsTest`, ordinary-analysis owners. Report separately from Binding completeness. |

Performance workloads vary depth, width, shared subgoals, alternatives, identities, repeated requests, frozen views,
edits and late invalidation. Keep both successful and exhausted searches. Reference bounds are an oracle only for
finite models, not permission to limit language semantics. Timing belongs in `src/Benchmark`, with shared
`tests/Workloads` inputs where useful; extend the existing associated-inference measurement for its meaning and
add a premise workload only if it is a distinct measured responsibility. Preserve zero-allocation tests, including
`WarmAssociatedRefinementProofsAllocateNothing` and existing inference/deep-refinement warm checks.

Compare the original baseline and C under the same inputs and fixed conditions; a B or D prototype is built only if
these measurements reopen §3. Report correctness differences before timings. Record code additions/deletions by
owner, time distributions, allocations, retained memory and V/E/J work counts. A net size increase can be justified
by deleting competing semantics; a speed claim requires measurements. An unexplained regression or an existing
assertion failure blocks completion; never add warm-up, weaken a ratchet or claim reduced work by dropping
permitted evidence. Native O2 runtime/allocation guarantees remain constraints. Generated code need not change for
an existing accepted input merely to fix proof.

Implementation verification follows [VERIFICATION](../../docs/dev/VERIFICATION.md): selected Unit classes/methods
for changed behavior, `Purpose=Allocation` where appropriate, and a final Session with explicit relevant native
fixture/milestone selections. A Session covering the last unchanged unit replaces its Unit run. NativeAOT is not
selected. Associate Unit/Session evidence with `scripts/verify-commit.ps1` before pushing implementing commits.
This planning-only unit requires diff/reference review and independent proposal review, no builds or tests; it has
no Unit/Session evidence to associate. Preserve its review and reference evidence under `artifacts/verify/`. The
revision's CLI check (§8) is evidence for G172, not a Unit or Session run.

## 8. Review record

On 2026-10-20, `design_local` and `design_structure` independently inspected the problem and code without editing
this document. The former supplied the within-owner subject expansion plan and shared dependency alternative;
the latter supplied the symbolic declaration-network plan and its growing-substitution counterexamples. Both
recommended C. The planning session compared their designs and selected/authored the contract above.

Independent reviewer `review`, author of neither design nor this document, reviewed all A–D plans and C's details.
It found no blocking issue for this planning artifact. Its independently sought shorter/general alternative was
the same reuse of existing clauses/Types with one dependency owner, without a new global scheduler or rule IR.
It confirmed that A/B are incomplete and that D's general summary is unproved; it checked frozen-definition
exclusion, producer conflict completion, candidate discovery, both polarities, recursion, purpose/view keys and
lifecycle. Two editorial points were corrected: link directly to the intake record, and explicitly state that
the prior supplier design and PS5 exit are replaced, eliminating competing ownership instructions.

Second independent reviewer `review_cost`, also author of neither design nor proposal, accepted the four-document
planning change with no blocking findings. It checked the source/SPEC/test references, compared A–D and tried a
simpler source-aware lazy clause supplier; that alternative is already C's constrained scope. It confirmed that
the size/performance choice is an expectation with explicit cold/warm/requery/retained-state risks, and that intake
and selection do not imply implementation, vocabulary approval or release of any hold. Both reviewers confirmed
that the real-source dependency and completeness work remains an implementation prerequisite.

Executable selection completeness, real-source dependency publication, resources and performance remain C1/C3/C4
gates. This review approves a plan to establish those facts; it is neither evidence that they hold nor approval to
resume paused implementation work. No compiler, model, build, test or performance measurement ran in this unit.

### Revision (2026-10-20)

The owner asked for the plan to be scrutinized and corrected against the Principles. The planning session checked
the anchors and claims against `bc3fcd3f` (compiler sources at `0ccf13ce`) and changed:

| Finding | Principle / rule | Correction |
| --- | --- | --- |
| §1 called the cross-definition case "a design test, not a newly reproduced defect"; SPEC §8.5.5's PA/PB example is rejected in both declaration orders, the order deciding which inference fails. | Explicit Semantics; §8.3.3 Finality | G172 recorded as a separate inference-owner cause (per-value demand); §4.2 and §7 leave it to its own unit; G170's closure does not wait for it. |
| §4.2 carried inferred-value dependency sets in proof rows beside the inference owner's own admissibility decision. | One Concept, One Canonical Form | The owner publishes admissibility per view, D's own values included; C keys rows by view and returns the bindings its supports read; C1 fixes an order-independent reporting rule. |
| §2 named a nonexistent `ContractMembers` and only one of six `ExpandScopeContractPremises` callers. | Local Reasoning (census) | `RequirementMember`; all six callers and the eager Contract-scope clauses; §4.4's deletions name them. |
| §4.1 made the solver "prove" propositions, against §2's "supplying evidence" and its own premise-only scope. | One owning component | The solver supplies evidence (by proposition) and subject evidence/candidates, with completion state; judgment, normalization and lookup keep their owners. |
| §4.3's absent-head example carried A in its head (§8.6.1). | Explicit Semantics | An inherited projection whose head lacks A. |
| §4.3/§4.4 retained alternative supports without a stated need. | Short, Fast | One admissible support decides an answer; alternatives are kept only for §4.2's dependency rule. |
| Request-local answers lacked their reason; a second dependency scheduler beside the capability work graph went unmentioned. | Local Reasoning; Consistent | Finality and per-request limits stated; one worklist for premise, capability and G172's value demands, or a recorded contract difference. |
| C1/C4 re-prototyped B and D, which §3 had rejected. | Short | C against an exhaustive reference evaluator; B/D counterexamples are inputs; D reopens only on measurements. |
| C3 migrated every consumer together with the evaluator. | AGENTS separate commits | C2 routes reads, with the view, through the entry operations as pure moves without mode parameters; C3 replaces their answers and handles the listed writers and flags. |

Evidence: one `Kimi.exe check` of SPEC §8.5.5's example in each declaration order, sources and outputs under
`artifacts/verify/20261020-g170-plan-revision/`; no Unit/Session run. G170's selection, C's contract otherwise, the
gates and holds are unchanged.

Independent reviewer `review_revision`, author of neither the plan nor the revision, re-ran the probes (and
variants) and checked every claim against source and SPEC. Round 1 requested changes: G172's first explanation
missed requirement-order extraction, so its repair is per-value demand rather than publication granularity, and the
rule predates the 2026-10-15 rewrite (not S1015); admissibility omitted D's own values; the dependencies returned
through C-supplied premises and an order-independent reporting rule were missing; normalization needs subject
reads. Non-blocking: completion state in answers, the view from C2, no mode parameters, G172's ordering, one
scheduler, an overclaimed order independence, stale text and wording. All were applied as above. Round 2 accepted
the revision with no blocking finding; its four editorial points (exhaustive supports, the A-first condition, C4's
G172 exception, wrapping) were applied.
