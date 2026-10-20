# G170 Premise Dependencies and Inference Plan

Status: 一部取り込み (open; selected implementation plan, no specification integration or implementation).
Request: 2026-10-20, compare several solutions and implementation plans, independently review them, and select the
most fundamental, concise and efficient design consistent with the Kimigayo Principles.
Source baseline: `a415c755fe239e61c6754940923cbbba6b2fef0f`.

Select **C: demand-driven declaration premises with explicit evidence dependencies**. This is a design selection,
not a measured performance result. It replaces the G170 supplier design in §§3–4 and supply work PS0–PS5 in the
[earlier plan](2026-10-20%20Canonical%20Premise%20Supply%20and%20Transferable%20Plan.md); that plan's Transferable
T0–T3, S5 connection and R4-M83 hold remain. This document sharpens the inference boundary and executable design
gates; it does not declare the earlier direction implemented or approve its proposed diagnostic vocabulary.
References to the earlier PS5 supply exit now mean C4 here; Transferable's remaining tests and exit gates still apply.

## 1. Problem, evidence and required meaning

G170 records a missing proof, not a demonstrated infinite loop, unsound acceptance or associated-value inference
cycle. The source inspection in this unit runs no compiler, tests or benchmarks. The historical observation is
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
independently determined value of B may be used without waiting for B's remaining associated values. This is a
design test, not a newly reproduced defect in the existing inference implementation.

Authority: [SPEC §8.2.3, §8.3, §8.5.5 and §8.6.3](../../docs/spec/08-generics-constraints-and-contracts.md).
Concrete conformance to a Contract classified recursive by §8.3.3 is prohibited, a stronger rule than merely
discarding an unseeded proof cycle. Structural capability fixed points keep their own SPEC rules. Neither rule
is replaced by a generic "cycle succeeds" or "cycle fails" policy. Proof uses no member search or guessed Type.

## 2. Owning code and common cause

Paths are under `src/Kimi/Compiler/Binding/`; methods are stable navigation anchors at the source baseline.

| Site | Present behavior / obligation |
| --- | --- |
| `Binding.Contracts.cs`: `ExpandScopeContractPremises` | Takes one snapshot of Facts; additions do not cause another expansion round. |
| `Binding.Contracts.cs`: `AddContractPremises` | Substitutes own and inherited clauses and adds ancestor facts. Reuse these rules, not their eager publication policy. |
| `Binding.AssociatedTypes.cs`: `BindAssociatedProjection` | Adds premises for the proven Contract chosen by the written qualifier. This makes querying syntax a semantic producer. |
| `Binding.ContractCalls.cs`: `ContractMembers`; `Binding.VirtualOverrides.cs`: `PrepareVirtualOverridePremises` | Member access and override preparation also expand premises. |
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
antecedent, declaration validity and associated-value evidence dependencies. Inference supplies its own frozen
evidence context; it does not become an unrestricted fixed point. Section 4 specifies the contract; section 6 gives
the implementation units.

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

Two semantic entry operations share rule application: prove a bound proposition; enumerate candidate constraints
for a bound subject. Enumeration retains pending declarations for discovery and ambiguity; proof/normalization
uses only available evidence. A discovered candidate does not prove its prerequisites. Neither operation silently
truncates, resolves an unbound program Type by guessing, or changes overload selection rules.

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

Publish necessary dependencies of an inferred value with its completion; every downstream proof/normalization
must retain them. Under frozen definition D, a support depending directly or transitively on any value inferred in
D is inadmissible. Do not reject an independent support merely because another path is cyclic. Do not use an
individual matching candidate as a certificate while omitting the producer's conflict/completion obligations.
Other definitions complete under their own rules; a caller's exclusion context controls admissibility of their
published evidence, not the global meaning of their associated values. An unrelated unresolved value in another
definition is not a reason to block a determined independent value. C1 must demonstrate this through real source
binding, not only an abstract graph with manually supplied dependencies.

### 4.3 Demand evaluation

1. Seed finite input assumptions and bound declaration sources from the proper lexical/implementation/frozen view.
   Registered conformances may be candidates; only verified compatible conformances are positive evidence.
2. Select existing clause templates capable of supplying the demand, its negation, required identity or candidate
   subject. Include ancestor clauses and non-immediate/deep subjects. Start with exhaustive template selection in
   the bounded reference model; a production index is an optimization requiring the same answers.
3. Instantiate only template Self/declared parameters using existing bound antecedent instances and substitution.
   For `C<A>` with consequence `Self.X is Copy`, A is absent from the head: obtain A from an available bound source
   such as `T is C<i32>`. Never invent A, enumerate all Types or solve program T to match the rule.
4. Register antecedent, source-validity, identity and inferred-value support dependencies. Tabling merges repeated
   demands within one evidence view; keep alternative derivations separate. New available evidence wakes dependents.
5. Apply only SPEC logic: conjunction supplies conjuncts; disjunction supplies no disjunct. Preserve exact compound
   assumptions, negation, concrete judgments and error propagation. Inspect both polarities and relevant Error
   dependencies; a first positive hit is not a completed answer.
6. Use only proven identity for matching; reconsider dependent matches when identity becomes available. Preserve
   full Type structure, applied Contract/Origin identity and congruence; never decompose equality to infer arguments.
   Any required identity correction belongs to that owner and is separately reproduced/designed, not hidden here.
7. Re-entry records an edge. An unseeded finite proof component contributes no evidence. A finite independent
   support may establish its goal, subject to recursive-Contract prohibition and the inference exclusion above.
   Keep Copy/Owned structural fixed-point semantics and capability purpose keys with their existing owner.
8. Complete only after the relevant work and source dependencies are settled. Unknown means a completed permitted
   search found neither polarity. A still-pending declaration is provisional, not a cached negative or final Unknown.

The hard part is step 2, not drawing the dependency graph. C1 must give a rule-by-rule selection argument covering
identities, absent-head binders and candidate enumeration, then compare against an exhaustive bounded evaluator.
A syntactic-prefix selector is B, not C. Finite templates do not bound generated obligations. Growing substitutions
need explicit work/materialization limits and a located Resource failure, never an arbitrary depth returning
Unknown/Refuted. Limit values, failure transport and dependent-use stopping are fixed and tested at C1; the existing
generation-limit diagnostic cannot be repurposed. Any new diagnostic/vocabulary takes its applicable approval.

### 4.4 Lifecycle, cost and deletion contract

Start with reusable request-local work arrays/rows and support edges. Nested proof, identity and candidate reads
share the appropriate request/view. Retain capacity after completion, not semantic answers across top-level
requests. Premise-dependent capability evaluated state must obey this lifetime too. Within-request deduplication
must not merge inference views or capability purposes. Cross-request caches require a later measured design.

Binding's existing source-completion and invalidation lifecycle remains authoritative. No query writes an
authoritative premise. If validation callbacks mutate source validity during evaluation, C1/C2 must separate that
publication from the completed read or explicitly restart the affected provisional work under the new state.
Never hide such mutation with a new generation counter or certify a declaration from its own active check.

One coherent semantic cutover removes:

- `ExpandScopeContractPremises` as the completeness mechanism and its scratch snapshot buffer;
- syntax-triggered `AddContractPremises` publication at projection/member sites and override expansion;
- obsolete DerivedFacts materialization/copying in scope composition;
- semantic fact-bag scans and presence shortcuts that can conceal derivable evidence;
- persistent premise-dependent answer state made redundant by the request contract.

Keep direct-input construction, declaration validation, projection/source certificates and inference's own
extraction/identification rules. Account for every remaining raw-fact access as input/storage/validation or remove
it. Do not leave an old/new fallback, an optional mode, a CopyTree case, or two live premise suppliers.

Let V be requested bound obligations, E instantiated support edges and J template/identity join work. Request
storage should follow O(V + E) plus reusable input indexes; running work must expose J as well as V/E. No unconditional
linear-time claim is made: matching, alternative supports and identities can be costly. Avoid enumerating every
root-to-goal proof path; share dependency rows and retain alternatives. Record cold/warm time, allocations, retained
capacity and repeated top-level work. Add an index/summary only when equivalent-work measurements justify it.

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
| C0: exact-head evidence | Reconstruct G170 in its test owner; record same bound Type/scope, ordinary/MIR results and current supply/read census. Inspect inference dependency boundaries. | Absent/before/after spelling and query-order evidence; desired regression fails before repair; no new inference defect claimed without a reproducer. Capture source/configuration, sizes/hotspots and existing baselines. |
| C1: executable design | Prototype demand selection/support in `draft/Sketches`; compare B/C/D on bounded equivalent inputs. Exercise production source adapters for frozen inference and validity, without shipping a second solver. | Rule-selection argument plus exhaustive-model agreement, real-source dependency witnesses, resource boundary, lifecycle and cost results. Exact representation/entry contract and approvals reviewed before production change. Prototype retirement: archive as evidence when C3 regressions cover its cases. |
| C2: preparation | Move existing responsibilities into the ContractSolver target concern; separate source publication/read where necessary, split due files. Independently repair identity only if a reproduced prerequisite demands it. | Pure moves have unchanged diagnostic/semantic HIR+MIR/generated-fixture evidence per VERIFICATION. Any identity/lifecycle behavior change is a distinct reviewed unit, not called a move. Record moved responsibilities in DECISIONS/architecture/CODEMAP. |
| C3: shared cutover | Implement the reviewed evaluator, evidence view and source dependencies; migrate every §2 consumer and delete §4.4's alternatives together. Resource publication ships with evaluation. | G170 plus §7 matrix, allocation/reuse checks, no stale consumer/cache/derived-bag path; default acceptance matches its prior disposition. No partial fallback branch. |
| C4: cost and final review | Measure equivalent workloads, inspect size/deletion and hotspot census; remove new duplication with separately checked units. Independent final review covers C2–C4. | Correct proof/candidates and all source/inference boundaries; recorded cold/warm/retained comparisons, native guarantees unchanged; focused checks and Session pass. Only now update verified support and close G170. |

C3 is one semantic unit after preparation, not permission for an unreviewably large change. If C1's concrete census
cannot make it reviewable, revise the unit design before coding while preserving one owner at every mergeable head.
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
| Frozen inference | Same-definition chain and A.Item→B.Value→A.Other exclusion; explicit counterpart; independent external value while another is pending; missing/ambiguous matches and pending compatibility; `AssociatedInferenceTest`. |
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

Compare the original baseline, C and any viable B/D prototype under the same inputs and fixed conditions. Report
correctness differences before timings. Record code additions/deletions by owner, time distributions, allocations,
retained memory and V/E/J work counts. A net size increase can be justified by deleting competing semantics; a speed
claim requires measurements. An unexplained regression or an existing assertion failure blocks completion; never
add warm-up, weaken a ratchet or claim reduced work by dropping permitted evidence. Native O2 runtime/allocation
guarantees remain constraints. Generated code need not change for an existing accepted input merely to fix proof.

Implementation verification follows [VERIFICATION](../../docs/dev/VERIFICATION.md): selected Unit classes/methods
for changed behavior, `Purpose=Allocation` where appropriate, and a final Session with explicit relevant native
fixture/milestone selections. A Session covering the last unchanged unit replaces its Unit run. NativeAOT is not
selected. Associate Unit/Session evidence with `scripts/verify-commit.ps1` before pushing implementing commits.
This planning-only unit requires diff/reference review and independent proposal review, no builds or tests; it has
no Unit/Session evidence to associate. Preserve its review and reference evidence under `artifacts/verify/`.

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
