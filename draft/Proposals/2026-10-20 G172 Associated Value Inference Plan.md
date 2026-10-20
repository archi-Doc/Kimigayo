# G172 Associated Value Inference Plan

Status: 一部取り込み (open; selected implementation plan, no specification integration or implementation).
Request: 2026-10-20. The owner adopted the [G170 PlanB](2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md),
then requested several G172 solutions, independent designs and review, comparison and a selected plan. The owner
also requested revision and evaluation of the supplied `2026-10-20 G170 Associated Value Scheduling Plan.md`.
Source baseline: `3a272f3e7f3f2402e3d137bf7764c4a9fcddb155`.

Select **C: evidence-local associated-value demand**, the revised Scheduling candidate in §3, option C. Keep associated
inference as the semantic owner; replace definition-batch availability with value completion and dependencies of
individual evidence. This is a design selection, not a measured claim of shortest code or fastest compilation.
G1 must settle what evidence depends on (§4.3) and demonstrate the production boundary before implementation.

## 1. Required meaning and evidence

Authority: [SPEC §8.5.5](../../docs/spec/08-generics-constraints-and-contracts.md#855-inference), §8.2.3, §8.3 and
§8.6.3. [PLAN G172](../../docs/dev/PLAN.md) records the defect. G170 supplies declaration consequences; G172 determines
omitted associated Types. Either defect can occur without the other.

SPEC's example has these value dependencies (the left value reads the right):

```text
PA.X <- PB.Y <- i64
PB.W <- PA.Z <- i32
```

The definitions refer to each other, but these value chains do not cycle. Each independent value of another
definition may be used as soon as it is determined. All conformances and merged fragments of the current nominal
definition share one exclusion boundary; nested definitions are separate. No value inferred in that definition,
including one reached through other definitions, supplies its inference evidence. This prohibition is stronger
than detecting identity cycles: `A.Other <- B.Value <- A.Item <- i32` cannot infer A.Other, although the graph is
acyclic. An explicit A.Item changes the boundary.

Retained CLI probes at `bc3fcd3f` reject the PA/PB example in both declaration orders: Q.W fails with PA first,
P.X with PB first, followed by other conformance/formation diagnostics. Sources, outputs and environment are in
`artifacts/verify/20261020-g170-plan-revision/`. Inspection found no Binding or owning SPEC change between that
revision and this baseline. This planning task does not rerun the compiler or claim fresh execution evidence.
G0 reproduces the failure on the implementing head, including requirement-order permutations.

The supplied Scheduling plan's probes ran at `8400ce72`, whose compiler, Library, SPEC and IMPL trees equal this
baseline's; its result table and abbreviated sources are retained in
`artifacts/verify/20261020-g172-inference-plan/original-scheduling-plan.md` (§1.2, Appendix A), its outputs are not.
Besides PA/PB it recorded order-dependent results: `alt` (§4.3) is accepted with E first and rejected with D first,
and an exclusion chain through one or two other definitions reports different diagnostics in different orders.
§8.5.5 gives textual order no role and Finality judges over the program's facts, so these are checking-order
dependences, which §8.3.3 and IMPL A.1 forbid whatever §4.3 decides: today one order of `alt` is either an unsound
acceptance or a wrong rejection. These are recorded observations, not reruns; G0 reproduces them.

Source explains the observation:

1. `InferAssociatedTypes` walks requirements and normalizes/extracts each result in order.
2. Normalizing PA.x starts PB's whole inference batch before PA.z has been extracted.
3. `ResolveAssociated` exposes no inferred value of a batch until the whole batch finishes.
4. The unresolved read becomes a dependency/input failure; that batch is not resumed when the needed value becomes
   available. Its partial result is treated as finished.

The required design chain is:

> An external value is usable as soon as it is determined, and evidence that depends on a value inferred in the
> current definition supplies none -> requirement-ordered extraction, definition-level availability and unrecorded
> normalization reads withhold independent values and cannot tell exclusion from unavailability -> associated
> inference owns evidence, completion and admissibility per value, recording the dependencies §4.3's rule counts ->
> definition batches, host-stack nesting, stack-based exclusion and structural pending tests used as readiness
> become unnecessary.

### Evidence locality

The following is a reasoned design example, not a new executed probe:

```text
P: associate X; pair() -> (Self.X, i32)
Q: associate W; w() -> Self.W
D is P: pair() -> (i32, E.(Q).W)
E is Q: w() -> D.(P).X
```

This plan proposes that X's evidence is the i32 at the first tuple position, so X need not read W and W may use X;
the supplied plan's whole-result reads add `D.X -> E.W -> D.X` and reject the program. SPEC's extraction text fixes
which positions supply evidence, not which normalization reads count, so the choice is §4.3's. Under either reading,
at a whole missing position the complete actual Type is the evidence and every dependency needed to form it counts.
The existing `PartialResultsCompleteIndependentOriginsWithTheOrdinaryHeader` test covers only a same-definition
sibling.

## 2. Ownership and common-cause census

Paths below are under `src/Kimi/Compiler/Binding/`; methods, not baseline line numbers, are navigation anchors.
[CODEMAP](../../docs/dev/CODEMAP.md) names the existing owners and tests.

| Site | Responsibility affected by the cause |
| --- | --- |
| `Binding.AssociatedInference.cs`: `PrepareAssociatedInference`, `InferAssociatedTypes` | Collect paths, identify implementations, extract evidence, complete values and publish results; currently a definition batch controls all of them. |
| `Binding.AssociatedTypes.cs`: `ResolveAssociated` | Canonical explicit/inferred/inherited binding read; currently nests a batch and withholds its values. |
| `Binding.AssociatedInference.cs`: `ExtractAssociatedEvidence`, `ContainsInferenceHole`, scope checks | Permitted structural extraction, identity and binder validity; retain the rules, change the granularity of evaluation. |
| `Binding.AssociatedTypes.cs`: `ContractType`, `ContractTypeCore` | Whole-Type normalization and batch cache; currently normalize all components before extraction chooses relevant positions. |
| `Binding.AssociatedInference.cs`: `IdentifyRequirement`, `requirementMatches` | Once-only identification under frozen premises; distinguish language-level pending identification from a temporarily unavailable external value. |
| `Binding.Types.cs`, `Binding.BindHeader`, `Binding.OriginDeclarations`, `Binding.TypeIdentity` | Partial header/Origin/identity completion; an early contextual result must not become a context-free cache entry. |
| `Binding.ContractMatching.cs`: `VerifyConformance` | Ordinary compatibility, batch collecting guard and diagnostic publication; value determination is not conformance certification. |
| `AssociatedBinding`, `AssociatedEvidence`, `BoundConformancePath.InferenceStorage` | One binding identity and source/evidence publication; do not add a second Type representation or value map. |
| `ResetAssociatedInference`, `InvalidateAssociatedEvidence`, projection certificates | Rebind lifetime, source invalidation and dependent failures; preserve late validation obligations. |
| G170's evidence and subject operations; `Binding.Capabilities.cs` work graph | Premise dependencies cross the inference boundary; capability transfer semantics and source lifetime remain distinct. |

There is no related SETTLED entry permitting different inference semantics. DECISIONS Q4–Q6 require owner-local
root repair, deletion-adjusted size, measured cost and independent review. R7's target separates inference and
Contract proof responsibilities. This plan does not reopen a compiler decision or change a SPEC rule.

## 3. Alternatives: designs and implementation plans

These are executable-change plans, not authorization to implement all alternatives. They use the same acceptance
matrix (§7). A smaller patch that rejects valid inputs is not an equivalent-work performance improvement.

### A. Publish partial definition batches

**Design.** Retain the current batch and expose an individual `Inferred` value before State 2.

**Units.** A0 reproduces PA/PB in declaration and requirement orders; A1 separates value reads; A2 publishes values
as extraction proceeds and tests later contradictory evidence, same-definition chains and cache reuse.

**Assessment.** The smallest edit, but PA.Z is not yet extracted when PA.x demands it. Reordering requirements merely
moves the failure. Publishing the first source before all usable sources settle also misses conflicts. Adding an
on-demand value evaluator, dependencies and resumable evidence to repair this turns it into C. Reject the small
version. No wrong-code observation justifies a temporary symptom patch: `alt`'s possible wrong acceptance (§1) is an
acceptance question whose correct side §4.3's rule decides (shown by G1, or by the owner on a reported gap), and G0
records the accepting order's values to classify it.

### B. Re-run batches after dependencies finish

**Design.** Record blocked batch reads and repeat affected definitions until results stop changing, retaining the
current extraction and publication loops.

**Units.** B0 adds order/conflict/edit reproducers; B1 records waiting consumers without semantic change; B2 prototypes
rounds and cache invalidation; B3 would migrate every early header/match/normalization result before cutover.

**Assessment.** More plausible than A for PA/PB, but retries repeat unrelated work and retain the wrong availability
unit. Conflict and evidence exclusion are not a monotone accumulation of Types. Convergence needs a semantic rule,
not a retry count or stable hash. Making retries sound requires C's evidence states plus an otherwise redundant
batch layer. Reject as the final architecture.

### C. Revised Associated Value Scheduling: evidence-local value demand — selected

**Design.** Reuse canonical associated bindings as value identities. Discover potential evidence from frozen
implementation selection and permitted result paths. Evaluate that evidence independently, retaining exact semantic
dependencies and progress. Determine a value only after all its possible usable evidence has a final disposition.
An unresolved external read schedules the required value; it does not recursively run a whole definition or become
a final language failure. Sections 4–6 specify this plan.

**Units.** G0 evidence; G1 semantic/executable boundary; G2 behavior-preserving owner preparation; G3 one value/evidence
cutover; G4 G170 integration, measurements and final review. The units and their exit checks are in §6.

**Size/cost.** Moderate new evaluation state, with direct deletion of the batch state machine and duplicate readiness
logic. Work follows evidence positions, value demands and premise queries. It avoids repeated whole-batch scans and,
under §4.3's proposed position reading, unrelated sibling normalization. Type normalization, joins, repeated views
and dependency checks still cost work; neither linear total time nor zero extra retained memory follows from the
design alone.

### D. One work driver for values, premises and capabilities

**Design.** Keep each semantic owner, but put their tagged work rows and wake-ups into G170's request driver.
Inference still has C's extraction/evidence rules; only scheduling is shared. Completed values remain declaration
outputs, while proof rows are request-local and capabilities retain their purpose-specific transfer rules.

**Units.** D0 reproduces C's matrix plus mixed request lifetimes; D1 defines the tagged rows and maps every existing
queue/reset/publication operation; D2 separately restructures scheduling; D3 cuts over inference and G170 at the
reviewed boundary; D4 compares size, mixed-query time and retained state against C.

**Assessment.** A credible structural alternative. It may remove one queue, but adds lifetime routing, dispatch and
cross-owner coordination before G170's driver exists. A queue by itself is not the recurring semantic responsibility.
Select C's owner-specific completion driver for the contract difference in §4.5; revisit physical queue sharing
only if the concrete G1 comparison removes code without merging lifetimes or transfer rules.

### E. One solver for associated values, normalization and all Contract judgments

**Design.** Replace the separate inference/proof owners with a general obligation engine and a common rule graph,
covering identity, conformance, capabilities and inferred values.

**Units.** E0 specifies every judgment and evidence view; E1 prototypes all cycle/exclusion/identity cases; E2 replaces
the shared representations; E3 migrates all consumers; E4 removes the former owners and compares complete workloads.

**Assessment.** The largest change and broadest invalidation/review surface. It adds a rule representation beside
bound clauses unless those are replaced too. Generality does not remove SPEC's distinct inference, proof and
structural-capability rules. No measured need warrants this scope for G172. Reject as the initial repair.

### Comparison

| Criterion | A | B | C | D | E |
| --- | --- | --- | --- | --- | --- |
| PA/PB in every requirement order | No | Possible, incomplete contract | Required | Required | Required |
| Evidence locality, conflicts, frozen exclusion | Missing | Must add C's structure | Explicit owner contract | C plus shared scheduling | Entire rule migration |
| Canonical path and deletions | Batch remains | Batch plus retries | Replaces batch/readiness paths | C plus queue consolidation | Replaces most owners |
| Expected code size | Small but incomplete | Invalidation/retry growth | Smallest credible complete repair | Extra lifetime/dispatch cost | Largest |
| Main performance risk | Wrong early results | Repeated batches | Normalization/dependencies/views | Cross-domain scheduling and retention | General rule matching/caches |
| Decision | Reject | Reject | Select | Retain as measured alternative | Reject for G172 |

## 4. Selected contract

### 4.1 One value identity, evidence per permitted position

Associated inference owns determination and admissibility. Keep the existing bound identity: conforming nominal
definition, root conformance path and `BoundRequirement` (associated declaration plus bound defining Contract).
Equal names are insufficient; inherited bindings retain their substitutions and are not inferred again.

Retain `AssociatedBinding` as the binding owner. Its evaluation state and evidence references replace batch
availability; explicit and inferred bindings still use one read operation. Keep per-definition storage only for
frozen paths, identification and reusable capacity, not as the availability unit. Key evaluation, wait and
dependency state by bound identities, never by syntax (COMPILER_ARCHITECTURE's Syntax identity rule; the Koto-keyed
`inferenceFailures` does not grow). Use concern files in Binding's
inference owner, with the entry contract required by COMPILER_ARCHITECTURE; no service interface, callback framework,
generic task bag or second language of Types.

An evidence item identifies the destination binding, source path/requirement, permitted extraction position and
typed result/problem. It records the dependencies needed to identify that source, expose that position, form its
complete Type and prove the source premises. Store shared dependency edges, not root-to-leaf proof paths or a
transitive definition bitset on every value. Operational waits and published semantic evidence are different
relations: a failed or irrelevant attempted read is not automatically a dependency of a determined value.

### 4.2 Evaluation and completion

1. Freeze conformance-path inputs and register canonical holes and source requirements before evaluating values.
   Discover evidence lazily from these finite declaration sources. Exhausting the candidate sources is part of
   completion; an undemanded or not-yet-exposed source is never silently discarded.
2. Identify each implementation under its frozen premises with the existing key rule. A temporarily unavailable
   external value suspends that comparison. SPEC's own-definition pending match remains pending for later
   compatibility only; missing/ambiguous matches remain final. Results and newly inferred values never reselect.
3. Traverse the required/actual result pair only through SPEC's permitted extraction structure. Normalize enough
   head/identity/length information to expose the next permitted position, preserving all dependencies used to do
   so. At a missing position, normalize and validate the entire actual Type. Other result parts receive ordinary
   compatibility later; which normalization reads count as dependencies is §4.3's rule. Share existing
   normalization primitives; do not copy their identity or substitution rules.
4. An external value read returns completed admissible evidence, a typed pending dependency, or a final
   exclusion/failure. Pending schedules that value and retains the extraction/normalization progress; it supplies
   no Type or final Unknown. Cache reuse replays semantic dependencies and completion state under the same view.
5. Source-premise completion uses G170's evidence view and returned dependencies. An implementation's private
   premises never become path-wide evidence. Every potential source must be resolved as usable, excluded or
   unusable before its destination completes; no source is skipped merely because another has yielded a Type.
   Only a complete premise answer makes a source unusable (§8.3.2 Closed world). A provisional one, such as today's
   Unknown for a nominal conformance before `contractHeadersReady`, stays pending and is never dropped at
   quiescence, so a hidden source cannot conceal a conflict. G1 defines how it completes, or reports it as a located
   `Unsupported_Kd`, the valid form the readiness contract does not yet cover (§5, stage readiness); either changes
   today's silent drop, so it takes §6's D5/O3 disposition and a STATUS boundary.
6. All usable evidence must agree on one complete Type, including Origins. Publish that Type and its semantic
   dependencies once; record disagreement or absence through the existing inference diagnostic owner. A failure
   records its typed cause where it occurs, including conflict, a failed value read and exclusion naming the
   current-definition value reached, with related locations from the recorded edges; formatting never re-runs
   inference, and the batch meaning of today's `Dependency` reason ("in this inference unit") goes. A result
   needed by another definition is available without waiting for unrelated values of its producer.
7. At quiescence, inspect fully discovered unresolved dependency components. Re-entry records a wait edge, not
   evidence and not an immediate exclusion. Settle them only using SPEC's exclusion/recursion rules established
   by G1. An independent admissible support must still be explored. SCC traversal may find components, but its
   Open/Closed state is not a language-level evidence verdict.
8. Complete ordinary compatibility, projection formation, conformance and effect checks through their existing
   owners. Determining a Type does not certify its producing conformance or the whole program.

This is a demand evaluator over declared evidence, not iterative guessing of Types. The G1 model must prove that
the chosen state transitions implement the rules; a worklist alone is not a proof of order independence.

### 4.3 Frozen views and the meaning of "depends"

Each inference request carries its path's frozen premises and current nominal definition explicitly. The producer
determines a value under its own rules; a consumer's view decides whether that published evidence is admissible.
Never recompute an external value with a different meaning to make it fit the caller. Own-definition inferred values
and transitive evidence depending on them are excluded; independently determined external values are usable.
An explicit specification can contain an external projection: explicit status does not erase that dependency.

§8.5.5 excludes evidence that "depends" on a value inferred in the current definition and makes an identification
pending when a key comparison "depends" on a missing associated Type of the definition, without defining the word.
It is one concept: G1, together with G170 C1 and before G2, shows that §8.5.5 and §8.3.3 Finality (a judgment uses
every fact that does not depend on it) fix one meaning for both uses, or reports the precise SPEC gap to the owner.
That meaning decides:

- **Extraction positions.** Whether reads made to normalize result parts off the extraction path count (§1's sibling
  example). This plan proposes that they do not; the supplied plan counted them.
- **Identification keys.** Whether a key comparison reading another definition's value whose evidence depends on the
  current definition is pending (identified again for compatibility only), like one reading an own missing value,
  or final (the supplied g3).
- **Alternative supports.** D.X has a constant i32 source and a source E.W, while E.W reads D.X (`alt`, whose
  acceptance depends on declaration order today, §1). Whether excluded or check-only branches enter D.X's published
  dependencies decides whether E.W may use D.X. The supplied plan chose all operational reads; G170 PlanB §4.2 leaves
  exhaustive admissible supports versus one canonical minimal support open.
- **Proof alternatives.** Identity and source-condition proofs can have several supports. First-found support,
  source order, dictionary order and incidental failed search work cannot decide later acceptance, cache hits included.

The rule is order-independent and covers conflict completion; each example then becomes a regression with an
approved expectation, not a scheduler-selected outcome. C's per-value evidence, completion and waits do not depend
on the answer; step 3's on-path normalization and §3's sibling saving assume the proposed position reading and
change with it. Neither finite-model agreement nor a uniquely behaving algorithm authorizes
a new language rule. If clarification is required, implementation stops at G1; this plan supplies no catch-all
rejection, silent fallback or SPEC rewrite. The selected architecture remains the proposal for the clarified rule.

### 4.4 G170 boundary, caches and source validity

G170 PlanB remains the adopted premise design. Its evidence/subject operations receive the frozen view; this owner
publishes associated-value admissibility, and premise supports return the associated bindings they depend on.
Both normal results and cache hits carry that information. In-progress queries remain provisional until their
source dependencies settle; no callback certifies its own active declaration.

Cross-engine waits never synchronously drain another driver. A G170 request needing an unavailable value returns
a typed pending dependency to the inference driver, which schedules the producer and later wakes the blocked
evidence task. G1 fixes one retained-or-replayed request policy: stable binding/demand identities and the correct
view survive suspension, but no edge refers to discarded request storage. A replay must account for repeated work
and cannot evade Resource accounting by resetting a budget. Mixed value/premise chains must not grow the host stack.

G1 uses real source adapters in both directions. G170 and G172 retain separate causes and cutovers. G170's closure
remains its own C4 gate; G172 does not declare a new unconditional prerequisite for it. If the adapters expose a
missing producer contract, record the specific prerequisite in PLAN and review the integration order before either
dependent cutover. Preserve inference rules across G170; added lawful consequences may legitimately change an
inference outcome and require explicit regression evidence, not preservation of an existing defect.

Cache audit includes `batch.Normalized`, requirement matches, explicit `State`/`Result`, completed headers/syntax,
pending-Type memoization, Type identity and premise-dependent capability results. A normalized i32 can still have
context-dependent provenance; lack of an unresolved projection does not prove a cache is view-independent.
Keep context-free source forms separately from contextual completed reads where required, using existing bound
Types and the owning cache. No abandoned computation publishes a completed result.

On a new Binding pass, clear semantic results, dependencies, pending work and view-specific caches; reuse capacity.
After source invalidation within a pass, revoke affected provisional/completed evidence through the existing
producer lifecycle before reuse. Keep projection obligations and inference source verification; a late invalid
source still makes dependent uses invalid. G1 must specify which producer publication restarts a request. No
generation counter, missing invalidation edge or global collection-order assumption may hide that obligation.

### 4.5 Driver and lifetime decision

Select a finite declaration-pass completion driver in the associated-inference owner, using explicit work storage,
value/evidence waits and dependent wake-ups. It replaces host-stack nesting and whole-batch retries. Suspended
evidence retains the position/progress needed to resume; if a normalization primitive must replay, account for
that replay explicitly and bound it in the scaling tests.

This is the contract difference required by G170 PlanB §4.4 for a separate driver:

- A completed associated value is a declaration output reused through the pass. Its producer must resolve all
  usable sources and conflicts before publication; it has no provisional guessed Type.
- G170 premise rows are request-local, view-specific judgments and discard semantic answers at request exit.
- Existing capability work has purpose-dependent structural fixed-point rules, including optimistic Owned state;
  those rules cannot determine inferred Types.

These are separate semantic work owners, not two premise suppliers. G1 inventories queue, deduplication, wake-up and
reset code against D. If sharing the physical transport actually deletes code while preserving these contracts,
review that restructuring separately; do not build a general scheduler pre-emptively or merge transfer rules.
The selected algorithm and public dependency boundary do not rely on a particular queue layout.

## 5. Revision of the supplied Scheduling candidate

The original supplied file has SHA-256
`0e1f07077a0d2d59470ab39c86aa33154849651393ddbeecbf3e8c094883bba8`.
Its revised design and implementation plan are C and G0–G4 here; the original file is retained as review evidence,
not treated as an implementation instruction. This is a substantive revision, not rejection of the candidate by title.

| Original section | Retained | Revised disposition |
| --- | --- | --- |
| §§1, 3, 4.1 | Per-value ownership, PA/PB and the order-dependent probes (§1), one read entry, explicit work storage, batch deletion | Separate G172's value cause from G170's premise cause; include requirement order; result positions follow §4.3's rule. |
| §2 g1–g4, Appendix B | Need for completion and explicit dependencies | No new SPEC wording. g1 is §4.2's completion; g2–g4 and result positions are facets of §4.3's one "depends" gate, not adopted readings. |
| §2 g5–g6 | Projection obligations and explicit equations matter | Preserve existing owners and test their lifecycle; no blanket exemption from formation or conformance prerequisites. |
| §§4.3–4.4 | Demand missing values without host recursion | Open is pending, not a final Excluded verdict. SCC is bookkeeping after dependency discovery, not the evidence rule. |
| §§4.2, 4.4 | Shared storage and dependency reuse | No mandatory dense reach set per SCC; retain progress instead of unrestricted item restart. |
| §4.5 | Typed problems naming the value read; one report per failing value | §4.2 step 6. Problem kinds and reason text are part of G1's reviewed package; no new diagnostic code. |
| §4.8 | Cache hits must replay dependencies | Audit headers/syntax and explicit values too; a complete Type need not be context-free. |
| §§2, 4.6 | Stage readiness is a real integration constraint | Today's nominal-conformance/capability blackout is implementation state, not a new language prohibition. A permitted proof it hides is a readiness obligation or a separately reproduced prerequisite. |
| §§4.7, 4.9, 4.10, 5, 10 | Premise-supply contract, deletions, order-independence model, units, document maintenance | §4.4, §6's deletions, G1's oracle and G0–G4; documents follow AGENTS' table. The prepared located `Unsupported_Kd` for nested reads, which would narrow support for a form whose meaning is open, is not adopted in advance; it is an owner option only if G1 reports a gap. |
| §6 | PA/PB, chains, cycles, keys, edits and conflicts | Add sibling-position and view-cache cases. Replace the assumed outcomes of `AlternativeSupportFollowsReads` and `KeyReadsAreDependencies` with G1's expectations. Distinguish determined external values from verified conformances. |
| §7 | Measure time, allocation and retained memory | Withdraw +100-line and unconditional linear-time claims; include continuation/replay, dependency-query and normalization cost. |
| §8 | Owner decisions stated before cutover | OD1 is this plan's selection of C. OD2/OD3 become §4.3's gate; no reading or wording is adopted. OD4 is §6's D5/O3 disposition, including `alt` and the order-dependent diagnostics. OD5 is met by §8's designs and reviews. |
| §9 | Other causes stay separate | `projectionUses` duplicates: G0 reproduces; a confirmed one becomes its own open issue. Completion hidden by the stage blackout: §4.2 step 5. Premise mutation and G124 stay with G170 and R4-M83. |

An item restarted from its beginning after each of k reads can perform `1 + ... + k` work. SCC reach bitsets cost
`S * ceil(D / 64)` words for S components and D definitions, plus word-wise unions. Neither cost disappears because
each edge is discovered once. The revised candidate must measure its actual storage and work.

## 6. Implementation units and deletions

Implementation starts only when the owner resumes the applicable Binding work. This planning request does not lift
R4-M79's BU/R5 pause or R4-M83's Transferable hold. Before acceptance-changing cutover, obtain any applicable D5/O3
disposition with concrete old/new acceptance and generation evidence. No new diagnostic code or vocabulary is
approved by this document.

| Unit | Work | Exit condition |
| --- | --- | --- |
| G0: reproduce and inventory | PA/PB and the supplied plan's `alt`, exclusion-chain and cycle probes at the exact head, in every declaration/requirement order, with the inferred values of `alt`'s accepting order; sibling example, conflicts, explicit equations, stage readiness (collecting-guard reachability, a completion source the blackout hides, `projectionUses` duplicates); inventory every value read/cache and touched-file hotspot. | Existing failure reproduced, semantic expectations separated from observations, baselines/evidence retained. No compiler change or committed failing regressions. |
| G1: executable design | Bounded declarative evidence model versus C's demand evaluator; compare D's transport with C; exercise real Binding/G170 adapters, identification, partial headers, source conditions, source invalidation and cache hits. | §4.3's one dependency rule settled or the precise owner gap reported; all permutations agree with the independent oracle; no outcome chosen by SCC order; complete concrete state/entry/lifetime package independently reviewed. No production cutover on model-only success. |
| G2: preparation | Split owner concerns and route canonical reads/dependency outputs while preserving current batch semantics. Keep source-normalization ownership intact; record any behavior-changing prerequisite as its own unit. | Unchanged diagnostics, semantic HIR/MIR and generated-fixture evidence per VERIFICATION; decisions/responsibility moves recorded; no temporary second inference supplier. |
| G3: value/evidence cutover | Implement §4, migrate all affected reads/caches and remove the batch state/host recursion together; add regressions and typed diagnostic facts. | §7 functional and allocation checks pass; each defect regression fails before the fix; ordinary conformance/formation checks preserved; G170 boundary works on the selected integration head; required Unit verification passes. |
| G4: cost and completion | Workloads/measurements, deletion and hotspot census, joint G170 consumer checks, independent final review of G2–G4. | Equivalent-work measurements and source/configuration identity recorded; no unexplained regression; final Session passes with relevant native selections; only then update support and close G172. |

Delete in G3: `AssociatedInference.State` as availability, `collectingAssociated` as semantic exclusion, nested
`InferAssociatedTypes` batch evaluation, the batch-dependent `ResolveAssociated` gate, per-batch cache clearing and
post-sweep explicit-value resetting made redundant by the new lifecycle. Source inspection finds the
collecting-conformance guard unreachable: `collectingAssociated` is set only during the sweep, which runs before
`ValidateConformances` sets `contractHeadersReady`, and until then a nominal conformance proof returns Unknown
without verifying. G0 confirms this; G3 deletes the guard and states the readiness it relies on in the reviewed
source-readiness contract. Remove structural pending tests used
as substitutes for readiness; retain their genuine Type-completeness uses. Preserve identification, extraction,
binder checks, explicit-candidate conflicts, projection certificates and source invalidation in their owners.

Pure restructuring and behavior change are separate commits. If G2 cannot isolate a reviewable G3, redesign the
units before coding. Every mergeable head has one value supplier; no batch/scheduler fallback or per-form gate.
New representations, responsibilities and approved diagnostic details are recorded in DECISIONS and the affected
architecture/diagnostic contracts in the first implementing commit. Update CODEMAP only when navigation changes.

## 7. Verification and performance

Reuse `AssociatedInferenceTest`, `AssociatedInferenceBoundaryTest`, their helpers and the owning identity/conformance
tests. Existing successful/rejected inference, warm-zero-allocation and edit/recovery expectations remain. Results,
diagnostic categories and locations are order-independent (IMPL A.1): small cases, the supplied plan's probes
among them, test every declaration and requirement order; workloads test reversed and shuffled orders.

| Obligation | Coverage |
| --- | --- |
| Independent values | PA/PB in both declaration orders and requirement permutations; independent external value while another value in its definition waits. |
| Exclusion | Own-definition chain across conformances/merged fragments; indirect and three-definition chains; explicit counterpart; nested definitions separate. Assert value determination separately from later conformance rejection. |
| Evidence locality | Under §4.3's rule: fixed sibling and sibling inferred value; whole-hole Tuple/Function Type still keeps all required dependencies; shared subtrees with distinct positions; existing partial-Origin result case. |
| Completion | Multiple agreeing sources, conflict including different Origins, an independent source beside a pending/failed/excluded source or one whose premise answer is provisional; frozen missing/ambiguous and pending compatibility-only selection. |
| Dependencies | Key normalization, shape/length/qualifier exposure, identity proofs and source-condition proofs; keys reaching the current definition through another one and alternative supports under §4.3's rule; repeated reads and cache hits. |
| Diagnostics | Each failing value reports once with its typed cause and related locations from recorded edges; no report re-runs inference. |
| Explicit/inherited bindings | Explicit projection equations, inherited substitution, distinct bound Contracts and late invalid source/effect checks; no re-inference of inherited bindings. |
| Lifecycle | Reverse demand order, reload, unrelated edit, remove/restore explicit evidence, previously complete cache under a different frozen view, source invalidation during a request. |
| Scheduling | Long cross-definition chains, wide evidence items, diamonds/shared goals, finite unseeded cycles and independent supports; no host-stack growth proportional to the chain. |
| G170 interaction | Ordinary/frozen views, premise-supplied identities/conditions with dependencies returned to inference, provisional/final source transitions, mixed value-to-premise-to-value chains and cycles; shared-supply regressions and actual consumers remain separate obligations. |
| Resources | Any growing normalization/proof work uses its producer's approved located Resource outcome, never an invented Unknown or inference rejection. Finite value identities alone do not bound all work. |

Keep timing in `src/Benchmark`, extending [AssociatedInferenceMeasurements](../../src/Benchmark/AssociatedInference.md)
and shared `tests/Workloads/AssociatedInferenceWorkloads.cs`. Measure cold/warm Bind, unrelated edits, rejected work,
multiple views, long/wide/shared graphs, allocations and retained bytes/capacity. Keep the benchmark's fixed
conditions and failed evidence; add no warming or relaxed allocation assertions to obtain a pass.

Let V be demanded value identities, E direct dependency edges, P visited extraction/normalization tasks and Q premise
work. Storage should follow the actual V/E/P plus retained inputs and caches, not every possible value-by-definition
pair. Report dependency reach-query work, task resumptions/replay and Q separately; no unconditional O(V+E) time
claim. The code census counts additions and deletions by owner, including context propagation and tests.
Native O2 runtime/allocation guarantees remain constraints; already accepted programs need no generated-code
change merely to schedule inference correctly.

Implementation verification follows [VERIFICATION](../../docs/dev/VERIFICATION.md): focused Unit checks and a final
Session, with explicit relevant native fixtures/milestones and no NativeAOT. Associate successful Unit/Session
evidence using `scripts/verify-commit.ps1` before pushing implementing commits. This document-only unit instead uses
diff/reference and independent review; it has no Unit/Session evidence to associate.

## 8. Design comparison and review record

Two independent designers, `g172_design_owner` and `g172_design_structure`, inspected the problem, adopted G170
contract, current code and tests without editing the proposal or coordinating their designs. The first examined
owner-local repairs and revised Scheduling; the second examined shared work transport and a unified solver.
The planning session compared their designs and selected C, retaining D as the narrower structural alternative.
Both found that publishing partial batches alone is insufficient and that whole-result read provenance is too broad.

Independent reviewer `independent_plan_review`, author of neither design nor this proposal, reviewed A–E, C's
contract and units, the Scheduling revision, SPEC/source/test anchors and the adopted G170 boundary. It accepted
plan selection with no blocking findings. Its shorter alternative, a memoized recursive per-binding evaluator,
still needs evidence locality, all-source completion, admissibility and explicit suspension; satisfying them
converges to C. Its nonblocking clarification was applied in §4.4 and §7: cross-engine pending propagation,
request retention/replay and resource accounting, and mixed value/premise chains.

Executable semantics, current-head reproducers, final dependency vocabulary, performance and production verification
remain G0–G4 obligations; planning review is not evidence that those gates have passed. Review/reference evidence
is retained under `artifacts/verify/20261020-g172-inference-plan/`. No compiler, build, test, model or performance
measurement was executed by this planning task.

### Revision (2026-10-20)

The owner asked for the plan to be scrutinized and corrected against the Principles. The planning session checked
its claims against source, SPEC and IMPL at `0c8ec470` (compiler trees equal to `3a272f3e`, `bc3fcd3f` and
`8400ce72`) and the supplied plan's retained probes, and changed:

| Finding | Principle / rule | Correction |
| --- | --- | --- |
| §1 recorded only PA/PB; the supplied plan's probes on identical trees also showed `alt`'s acceptance and the exclusion chains' diagnostics changing with declaration order. | Explicit Semantics; §8.3.3 Finality; IMPL A.1 | §1 states them as recorded, not rerun; G0 reproduces them; §7 requires one result in every order. |
| §§1 and 4.3 called result-position locality required by SPEC while "depends" stayed open for alternative supports; the supplied plan read the same text as whole-result normalization. | One Concept, One Canonical Form; Explicit Semantics | §4.3 makes "depends" one G1 gate: positions, identification keys, alternative supports, proof alternatives. This plan's position reading is a proposal; C is unchanged. |
| How a key comparison reaching the current definition through another definition is classified was unstated. | Explicit Semantics | A facet of §4.3's gate (the supplied g3). |
| Completion drops a source on any non-Proven premise answer, so the stage blackout can hide a usable source and conceal a conflict. | Local Reasoning; AGENTS Diagnostics | §4.2 step 5: only a complete answer makes a source unusable; G0 probes the blackout. |
| §6 kept the collecting-conformance guard as protection; source inspection finds it unreachable during the sweep. | Short; Consistent | G3 deletes it and states the readiness it relies on; G0 confirms. |
| No diagnostic contract; the supplied §§4.5, 4.7, 4.9, 4.10, 5 and 8–10 had no disposition. | Compiler Server Protocol; AGENTS Diagnostics | §4.2 step 6 and §7: typed causes and related locations from recorded edges; §5 adds four rows. |
| Evaluation-state keys were unstated. | COMPILER_ARCHITECTURE Syntax identity | §4.1: bound identities; `inferenceFailures` does not grow. |

PLAN's G172 row and STATUS's inference limit record the order dependence; the INTEGRATED entry records this
revision. The selection of C, the assessments of A–E, the unit structure and the holds are unchanged. No compiler,
build, test or model ran in this revision.

Independent reviewer `g172_revision_review`, author of neither the plan nor the revision, checked the tree
identities, the supplied plan's hash and table, the completion, readiness and guard call sites, the diagnostic and
key rules and the G170 PlanB boundary. Its attempted refutation of the open position reading failed: SPEC's
extraction text fixes positions, and a functional reading of "depends" is itself undefined. Round 1 requested one
change: §4.3 claimed C independent of the answer although step 3's on-path normalization assumes the proposed
reading. Its non-blocking points were also applied: a provisional answer's end state, "shows" rather than "derives"
a meaning, SPEC's own pending wording, the probe evidence path, A's wrong-code assessment, one order-independence
sentence in §7, a shorter §1, PLAN's G1 timing, STATUS, INTEGRATED's scope and §5's remaining sections. Round 2
accepted with no blocking finding; its points were applied: who decides `alt`'s correct side, step 5's reported
limit and its acceptance disposition, testable order coverage in §7, the stale §8 row and §5's row order.
