# G65 Fixed Origin Loan Retention — Implementation Plan

Date: 2026-10-07. Status: proposed implementation plan; no implementation or specification change in this document.

Investigation baseline: `2bf8e74fe3f49b244d2df7fb45a9b148d2e0e790` (`dev`). The working tree was clean before this document was added. Source, specification and existing tests were inspected, including separate reviews of ownership flow, specification requirements and test dispositions. Proposed representations and future test names below are not existing implementation claims.

## 1. Objective and scope

Complete the G65 **fixed-input retention** work identified in [PLAN](../../docs/dev/PLAN.md): when an acquired argument fits a parameter over an enclosing fixed Origin through a proven relation, preserve that argument's actual Loans for the required region. Use one mechanism for function-value calls and nested named calls. Accept valid programs; replace temporary Unsupported rejections with the appropriate Origin or ownership error for invalid programs.

This is the remaining composition blocker of §7.5 of the [Callable and Local Region plan](2026-10-06%20Callable%20and%20Local%20Region%20Implementation%20Plan.md), as recorded in [INTEGRATED](../INTEGRATED.md). It is the scope of the preceding G65 effort estimate, not a restart of the whole Callable/local-region implementation.

The scope inventory matters because the G65 label has accumulated several concerns:

| Concern | Present state and disposition in this plan |
| --- | --- |
| Premise-based adaptation to a fixed input Origin | Main implementation target; both call forms and their stored/returned dependencies must be handled. |
| Fresh per-call input results, fixed external results, receiver-dependent closure results | Existing support to preserve and use as regression controls. |
| Environment Origins neither carried by captures nor fixed in the calling body | Separate remaining G65 boundary. Inspect interactions, keep its existing rejection, and do not claim it closed by fixed-input retention. |
| General stores through an exclusive captured reference (G75 E7) | Adjacent to the second G65 reproducer. Fixed-input retention must make that reproducer sound, but does not by itself establish general environment Loan flow or close E7. |
| A receiver-dependent result whose receiver cannot be represented as a Place | Separate remaining G65 boundary; do not manufacture a Place or widen erasure to solve this task. |
| Requirement Function Items such as `T.compare` | Independently listed P26 work. G78's parenthetical G65 cross-reference needs bookkeeping clarification; it does not expand this work into requirement-reference implementation. |
| Object capability roots, asynchronous execution, general higher-rank Origins | Existing independent boundaries; no automatic expansion. In particular, current exclusive-input root detection does not cover `objuniq` as an ordinary `uniq` root. |

Completion of this plan closes the identified retention blocker only. G65 as a whole and the older proposal must be closed only after their other open items have explicit dispositions. Do not edit the older proposal or its frozen §3 as a side effect of this work.

## 2. Required semantics and invariants

Authoritative sources are [SPEC §3.5](../../docs/spec/03-types-and-values.md), [§7.6](../../docs/spec/07-functions-and-callable-values.md), [§8.6](../../docs/spec/08-generics-constraints-and-contracts.md), [§10](../../docs/spec/10-overload-resolution-and-inference.md), and [§15.3–15.8](../../docs/spec/15-ownership-and-lifetime-analysis.md). [SETTLED](../../docs/SETTLED.md) was consulted. Neither banning result-only Origins nor requiring explicit declaration of every signature Origin is part of the solution.

1. **Origin proof is not Loan authority.** `z outlives x` proves region containment. It does not identify the Places behind `z` and `x`, merge their Loans, grant exclusivity, or create a reference from a phantom Origin (§15.3.5).
2. **Binder identity determines quantification.** Only the callee's own quantified positions are instantiated per call. An enclosing fixed Origin stays fixed even if its slot number matches the argument index. Nested Function contracts are not stored reference payloads.
3. **Selection precedes ownership.** Structural applicability, ranking and contract proof retain their existing boundaries. A Loan conflict does not cause candidate retry, and a failed Origin proof is not repaired by retaining more Loans.
4. **Acquisition preserves actual provenance.** Record the value acquired once, its storage path, original root, parent/child Reborrow relationship and capability. Fitting to a parameter Type must not overwrite this identity. A shared result cannot downgrade an exclusive Loan already acquired (§15.6.4).
5. **Fixed-region retention is not result liveness.** The callee can retain through a writable argument or closure environment and return Unit. Ending or discarding the call result does not establish that the required fixed-region retention has ended.
6. **Protection has a start point.** A fixed Origin contains every point of the body (§15.6.5), but the actual Loan starts at acquisition. Existing preparation/reservation protects the input before entry. The additional possible-escape retention becomes effective at the logical call boundary and flows only along applicable CFG edges; it cannot retroactively affect earlier operations or a path on which entry never occurs.
7. **Reborrow recovery requires a semantic distinction, not a spelling test.** `FunctionValueHoldsNoLoan` in `InputDependentValueCallTest.FixedInputsAreOrdinaryArguments` is an existing positive regression to preserve: the parent is used after its returned child ends. A function signature mentioning `x` does not itself hold a Loan on `x`. G0 must reconcile this behavior with the proposed non-identity fixed-region retention; Origin object identity alone is not a reason for different acceptance. If the formal rules prove this existing expectation wrong, document and resolve that contradiction deliberately rather than treating the test as specification. Conversely, identical Type annotations alone do not prove that independently acquired references have identical authority.
8. **All dependencies survive composition.** Multiple actual Loans mapped to one contract Origin remain distinct. Aggregate parts, copied holders, closure captures, partial writes, destructor/defer observation and checking continuations keep their existing rules.
9. **Analysis remains bounded and reusable.** No runtime retention structure, new allocation in generated programs, body reanalysis per candidate, global Origin alias table, or per-use whole-CFG traversal. Rebind/invalidation must revoke the previous facts and preserve warm storage reuse.

No established specification contradiction was found. The implementation should first satisfy these rules without changing the language. G0 below must explicitly resolve any apparent tension between full fixed regions, acquisition start points and the same-Origin positive control; it must not encode a convenient interpretation without independent semantic expectations.

## 3. Current control flow, responsibilities and dependencies

Navigation entry: [CODEMAP](../../docs/dev/CODEMAP.md). The important paths are:

```text
selected declaration / selected Callable contract
  -> committed argument acquisition and Origin fit
  -> BoundCall or BoundValueCall
  -> receiver, explicit arguments, omitted defaults acquired once
  -> reservation activation -> contiguous CallEntry records -> Call
  -> dependency preparation and retained-authority analysis
  -> per-root CFG Loan flow + liveness + ancestor/conflict checks
  -> verified cleanup / lowering -> ordinary physical call
```

| Phase | Existing entry points | Responsibility relevant to G65 |
| --- | --- | --- |
| Named-call binding | [Binding.Calls](../../src/Kimi/Compiler/Binding/Binding.Calls.cs): selected-call finalization, `BoundCall.Set`; [Binding.CallRelations](../../src/Kimi/Compiler/Binding/Binding.CallRelations.cs): `JudgeSelectedCall`, `CollectCallRelations`, `JudgeCallPosition` | Map source arguments to parameter positions, retain acquisition plans, judge selected fits and clauses. `EnclosingOriginsAsWritten` currently blocks the missing retention path. |
| Value-call binding | [Binding.ValueCalls](../../src/Kimi/Compiler/Binding/Binding.ValueCalls.cs): `BindValueCall`, `CalleeBinder`, `OriginsAsWritten`, `BoundValueCall.Set`; `Binding.ResultPremises.SolveValueCallOrigins` | Distinguish fixed and per-call positions, instantiate the complete signature, preserve selected Callable contract/receiver and result mode. A fitting but non-identical Origin currently reaches a separate Unsupported guard before finalization. |
| Acquisition contract | [Binding.ArgumentOperations](../../src/Kimi/Compiler/Binding/Binding.ArgumentOperations.cs): `BoundArgumentOperation`, prepared borrow Types | Preserve source and adapted Types separately from the parameter Type, including mapping and acquisition kind. A relation witness is not an additional acquisition. |
| Ownership call construction | [OwnershipAnalysis](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.cs): `Call`; [OwnershipAnalysis.Closures](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Closures.cs): `CallValue`; [OwnershipAnalysis.Reservations](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Reservations.cs): `PrepareCallArgument` | Prepare values in evaluation order, activate reservations, emit entries, establish result storage and end transient call protection. Prepared borrows deliberately retain their actual Origin before parameter fitting. |
| Defaults and instances | [OwnershipBody.Defaults](../../src/Kimi/Compiler/Analysis/OwnershipBody.Defaults.cs): `CallAt`, `SubstituteDefaultCall`; [OwnershipAnalysis.Instances](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Instances.cs): `AnalyzeInstance` | Remap declaration-context contracts to the selected call/instance without reselecting it. Any new plan payload must survive these paths; an omitted default still has a parameter identity. |
| Dependency and authority preparation | [OwnershipBody.Borrows](../../src/Kimi/Compiler/Analysis/OwnershipBody.Borrows.cs): `VerifyBorrows`, `AddExplicitDependencies`, `PrepareRetentions`, `RetainBorrowAuthority` | Track actual stored references, writable referents and closure environments. Retention records are currently associated with concrete holding Places/storage contracts. Origin-name queries alone cannot represent the G65 adaptation. |
| CFG flow and result ancestry | [OwnershipBody.LocalRegions](../../src/Kimi/Compiler/Analysis/OwnershipBody.LocalRegions.cs): `PrepareLoanFlow`, `OriginKeepsRoot`, `FixedResultKeepsRoot`; `OwnershipBody.Borrows.ResultArgument`, `SoleResultInput` | Follow acquisition snapshots and sparse storage paths through joins/loops; recover actual ancestry for returned/reloaded references. A fixed contract association needs to participate here without merging independent roots. |
| Publication and physical generation | `OwnershipAnalysis.ReportDiagnostics`; `BodyLowering.Calls`, `BodyLowering.Closures`, ordinary emission validation | Report the actual conflict and its evidence; consume the verified plan. The planned fix changes static proof, not calling convention or library algorithms. |

Two fast paths are material: `VerifyBorrows` currently detects work from projections/exclusive inputs, and `PrepareLoanFlow` from local region bounds. A body containing only fixed-Origin retention must not be skipped. Preserve the fast path for bodies that have neither old nor new work.

`CallEntry` records are contiguous and are consumed by existing mapping/ancestry code. Do not insert executable pseudo-operations among them. Function Types are intentionally skipped by stored-dependency traversal; inspecting their contracts for fitting must not make a function value appear to own its hypothetical inputs.

## 4. Root cause and observable cases

The missing link is between **a proven parameter fit** and **the acquired value that must remain protected under that parameter's fixed Origin**.

`BoundCall` and `BoundValueCall` retain argument operations and signatures. Ownership then prepares the actual value and its original Origin. Existing result/storage dependency matching can follow written Origin identity and local-region bounds, but it has no call-scoped association saying that this particular acquisition's Loans are now retained under another fixed Origin. `PrepareRetentions` covers writable storage/environment paths; it is not a general representation of such a contract association. Temporary and holder liveness alone is insufficient when the public contract permits retention independent of them.

The two Binding guards deliberately prevent this missing association from admitting unsound programs. Deleting them, replacing the argument Origin with the expected Origin, or adding a global `z -> x` alias would remove the symptom while losing provenance or affecting unrelated acquisitions.

### 4.1 Existing reproducer dispositions

The four cases are the Unsupported rows of [InputDependentValueCallTest.AFixedInputIsNoPerCallInput](../../tests/xUnitTest/Tests/InputDependentValueCallTest.cs). Preserve their original source and assign expectations independently:

| Case | Intended behavior after the fix | Why |
| --- | --- | --- |
| Closure accepts `z: uniq/i32` at `uniq/i32 during x`, returns that reference, then caller uses `z` again | **Provisional: reject** if the public contract requires the new Loan to remain for the full fixed region. G0 must settle this outcome before implementation. | Full fixed-region retention suggests rejection after the returned local's last use, but §15.6.3 parent recovery and the positive control in §2.7 require a justified semantic distinction. Non-identical Origin objects are not sufficient justification. |
| Exclusive closure captures `holder@uniq`, stores `z@follow@ref` in `holder`, then caller reads `holder` | Accept; add a native wrapper and a sibling case that writes `z` before reading the holder. | The original case contains no competing later access to `z`. This is a positive storage-retention case, not a reason to change all four rows to success. |
| Nested named helper returns `uniq/i32 during x`; caller forms another view from `z` while using the returned reference | Reject at the conflicting access/acquisition. | The distinct alias cannot bypass the retained exclusive Loan. |
| Nested named helper stores `z@follow@ref` through a writable holder; caller writes `z` then reads the holder | Reject at the write to `z`. | The stored shared reference still depends on the actual `z` Loan. |

G0 must pin exact primary locations, categories, Reason and related evidence. Do not preselect a diagnostic code solely to reuse current test assertions. `ComparisonLoanConflict_Kd` is the existing likely path for ordinary conflicts, while acquisition/activation conflicts retain their corresponding codes. Case 1 remains an explicit semantic-oracle question; cases 2–4 have the direct storage/conflict rationale shown above.

### 4.2 Controls that prevent an overcorrection

- Keep every existing `FixedInputsAreOrdinaryArguments` case, especially `FunctionValueHoldsNoLoan`, as a positive control.
- Keep `CallableOriginForwardingTest.AFixedImplementationNeverFitsAPerCallRequirement`: retention cannot make an invalid callable contract compatible.
- Keep `StoredReferenceLoanTest.EqualOriginNamesDoNotMergeIndependentInputCapabilities` and `StoredReborrowAncestryTest.EqualOriginNamesDoNotAuthorizeSiblingAccess`.
- A finite local borrow escaping into an enclosing universal Origin remains Refuted. Missing fixed-to-fixed proof remains Unknown/Proof failure, not a Loan conflict or successful retention.
- A phantom Origin or an empty value with no actual stored borrow creates no Loan by its annotation alone.

## 5. Proposed minimal design

### 5.1 One retained description of the selected fit

Add one common internal representation for the retention required by a **committed** argument fit. Tentative name: `BoundFixedOriginRetention`. Use the same payload and builder for `BoundCall` and `BoundValueCall`, attached as a reused flat span/array with an active count. Do not introduce a second type-fitting engine. The following lifetime policy is a design candidate subject to G0; if G0 changes the required extent, retain the common provenance handoff and revise its extent policy before activation.

Each record needs a stable source/parameter identity, the relevant complete-Type position, the acquired and required Origin at that position, and enough source/contract evidence for diagnostics. Read acquisition kind, expected Loan requirement and complete Types from the existing argument operation wherever possible; do not duplicate whole Types or place ownership facts in Binding. A concrete path encoding should reuse an existing structural path representation if it can distinguish nested storage positions without treating nested Function signatures as stored values.

Construct records after target selection, contextual completion, substitution and successful fit judgment. The walker must share the existing variance and Origin-position rules; only positions that transport actual borrow dependencies can require retention. Distinguish unchanged/per-call fits from a proven adaptation to an enclosing fixed Origin under G0's normalized semantic rule, not reference inequality or source syntax alone. Do not rank, re-prove by callee-body inspection, reacquire an argument, or expose facts for a failed/incomplete call.

Audit receivers, explicit arguments in source order, parameter mappings and defaults. A record refers to the prepared parameter acquisition, not an ordinal guessed from contiguous entries. Preserve facts through generic/default substitution and reset their active counts on every rebind. Remove the two legacy guard predicates only after the common consumer is complete; keep independently necessary residual checks.

### 5.2 Resolve records to acquisition snapshots at call entry

Both `Call` and `CallValue` hand the retained facts to one ownership helper once prepared input identities and the logical invocation operation are known. Use an ownership side table keyed by the invocation, with records referring to the acquisition/value snapshot and fixed destination Origin. Keep existing `CallEntry` layout, reservation order, executable operations and ABI unchanged.

The helper preserves every actual contributing root, Loan and ancestor capability, including a stronger exclusive parent behind a shared child. No authority is generated from a relation or an Origin name. Multiple arguments fitting the same fixed Origin produce distinct contributing snapshots; they are not coalesced merely because the destination matches.

Keep current access mode separate from retained parent authority, as `borrowDependencies` and `retainedBorrowAuthority` already do. A shared child is not promoted to an exclusively acquired Loan because its parent is exclusive. Compatible shared descendants remain usable together; writes through a conflicting parent/sibling remain forbidden.

Abandoned argument/default evaluation never activates the unentered call's persistent association. Acquired values still receive the existing cleanup and checking-continuation treatment. A Never-returning call must still be checked at its entry; do not create a normal return edge to model retention.

### 5.3 Extend the existing per-root flow, without inventing a runtime holder

Represent contract retention as an additional reason for a Loan to remain active, separate from a current result or concrete holding Place. Reuse the existing CFG predecessor index, worklist, per-root queries and packed/sparse storage policies. Do not allocate an operation × all Origins × all roots product.

For each affected root, the analysis must know whether a particular call-origin association can reach the current point. Seed it at entry after successful preparation/activation; propagate monotonically through existing relevant edges and joins/backedges. A branch that never executes the call contributes no seed. Loop iterations use a finite set of static acquisition sites and existing join semantics, not a new object per dynamic iteration.

The required fixed region controls lifetime; result destruction, overwrite of the acquired temporary, and loss of one concrete holder cannot alone kill a potentially escaped contract association. Equally, no edge reaches earlier sequential points before acquisition. Keep ordinary local-region and same-Origin reborrow lifetimes unchanged. If a body-local inferred destination is encountered, route it through the existing local-region mechanism rather than treating it as a universal region.

`CallEntry` currently ends an ordinary borrowed holder, and ordinary liveness/`MayInit` tests assume initialized runtime Places. Do not relax those rules globally or pretend the consumed argument temporary stays initialized. Query the additional contract extent explicitly. A loop backedge may legitimately carry a prior iteration's association to a syntactically earlier operation; the rule forbids retroactive straight-line activation, not loop convergence.

This extension must participate in both conflict checking and dependency/ancestor queries. Otherwise a returned or reloaded legitimate descendant would conflict with an artificial unrelated pin. Update `ResultArgument`/`SoleResultInput` and fixed-result/root matching to consult the represented association at the operation, preserving multiple possible ancestors as multiple dependencies. A shared lifetime name never selects one privileged ancestor.

Include a later call that retrieves a value over `x` after an earlier call may have stored an admitted value. Its public contract may require consulting the reaching association; this must not retroactively attach the new root to every pre-existing value whose Type already mentions `x`. G0/G2 must establish the precise producer/consumer rule with a positive retrieval and an unrelated-existing-value control before acceptance is enabled.

### 5.4 Evidence, reuse and bounds are part of the representation

Keep the argument acquisition and call/contract site that introduced retention. A conflict after the call reports the conflicting operation and relates the real acquisition/contract retention cause. Do not label a nonexistent synthetic holder as a user variable. Use the common diagnostic model and its limits; add catalog facts only if existing facts cannot accurately describe the cause.

Include retained counts/capacities in the existing ownership storage accounting and budget checks. Check arithmetic before allocating. Clear call associations, origin-path caches and snapshot mappings on `ResetPass`, ownership invalidation, body reuse and default/instance substitution. Existing no-retention calls should retain a cheap empty path and zero warm allocation.

Body-owned records reset with `OwnershipBody.Reset` in `OwnershipModel.cs`; preserve reusable capacity. Generic and Semantics-case analyses resolve complete Types through the existing concrete context and maintain independent body records, without retrying selection or sharing one instance's acquired roots with another.

### 5.5 Rejected shortcuts

| Shortcut | Reason not adopted |
| --- | --- |
| Remove Unsupported once `FitsTypeAt` succeeds | Lifetime proof still supplies no actual Loan or storage authority. |
| Substitute the supplied Origin with the expected one | Destroys the identity needed to protect the original Place and distinguish independent inputs. |
| Globally alias every pair related by `outlives` | A relation applies to regions; actual acquisitions are path- and value-specific. |
| Keep only returned-value dependencies | Misses Unit returns, writable arguments and retained closure environments. |
| Pin every borrow mentioning a fixed Origin | Rejects ordinary same-Origin reborrows and makes a callable signature falsely hold input Loans. |
| Inspect the selected implementation to prove it does not store | Violates public-contract reasoning and changes behavior under generic/indirect calls. |
| Attach persistent retention only to the acquired temporary | Temporary/holder death can occur while the fixed contract still permits retained aliases. |
| Add a separate direct-call and closure analysis | Duplicates semantics and leaves the original cross-path inconsistency. |

## 6. Implementation units

Dependencies: `G0 -> G1 -> G2 -> G3 -> G4 -> G5`. Each completed unit receives its own verified commit and push; stage only that unit's files. Units that add internal support but keep the feature gated must say so. Do not report new support before the required consumer and diagnostics pass.

### G0 — Establish the semantic oracle and baseline

- **Targets:** `InputDependentValueCallTest`, the existing forwarding/stored-reference tests, a proposed `FixedOriginRetentionTest`, and ignored evidence under `artifacts/verify/`.
- **Changes:** retain the four original sources and independently specify the outcomes in §4, including the unresolved first case; add positive counterparts and minimal Unit-return/opaque-callee cases. Establish a table of fixed, fresh per-call, finite/local, phantom and nested Function positions. Compare proven equality (`origin z == x` or both outlives directions), a prior local adaptation (`let y: uniq/i32 during x = z` followed by `c(y)`), and equivalent published contracts through direct closure, nested named and common Function calls. Record current Unsupported/valid behavior without turning all failures into acceptance expectations. Resolve the fixed-region/reborrow-recovery distinction before implementing a storage model; equivalent normalization or moving an adaptation to a local must not bypass required retention.
- **Reason:** the guard has both valid and invalid programs behind it. An acceptance-only reproducer would hide unsoundness; an all-rejected solution would not implement the feature.
- **Verification:** current baseline checks and existing positive controls; inspect representative CLI text/JSON and LSP records. Prove each intended failure reaches the relevant gate rather than an unrelated parse, capture or generic constraint error. Record the disposition of every baseline case and existing unrelated failure.
- **Exit:** reviewed expected outcomes and minimal invariant pairs; no compiler behavior changed. If the same-Origin control cannot be reconciled with the proposed non-identity behavior under the spec, stop this sequence for a documented semantic review.

### G1 — Retain one common description of fixed-input fits

- **Targets:** `Binding.ArgumentOperations`, `Binding.Calls`, `Binding.ValueCalls`, shared relation/type traversal, and the actual default/instance plan-copy sites located from `BoundCall.Set`/`InstantiateDefaultCall` callers.
- **Changes:** add the compact common payload of §5.1; derive facts only for the selected successfully proven fit; preserve parameter/path/source identity and reuse storage. Keep unsupported feature paths guarded until G4. Do not make an incomplete plan available as a successful call merely to test its metadata.
- **Reason:** ownership needs a stable bridge from the selected contract to actual acquisitions, and both call forms must classify the same relation identically.
- **Verification:** structural/binder classification cases, argument and clause reordering, nested/invariant positions, missing proof, per-call controls, default substitution, and invalidation of an edited relation. Baseline acceptance/diagnostics must remain unchanged. Internal representation tests should assert semantic mappings rather than reproduce the implementation loop.
- **Exit:** one shared description with bounded reusable storage; no advertised support expansion.

### G2 — Carry the acquired Loans through the fixed contract region

- **Targets:** `OwnershipAnalysis.Call`/`CallValue`, `OwnershipBody.Borrows`, `OwnershipBody.LocalRegions`, `OwnershipModel` only if an internal side-table record belongs there, ownership storage accounting and invalidation.
- **Changes:** connect prepared arguments to call-side records; add the forward contract association to the existing per-root flow; integrate result/reload ancestry and conflict queries. Account for the new work in early-exit predicates. Preserve the call-entry sequence, cleanup and physical lowering. Keep source-level rejection in place until the joint behavior is verified.
- **Reason:** this is the missing semantic operation. It must carry original capabilities and survive opaque retention, rather than rely on a live result/temporary.
- **Verification:** independently built analysis-level graphs/cases for no-call paths, joins, loops, snapshots, multiple independent Loans, stronger parent authority, ended concrete holders, checking continuations and resource limits. Include compatible shared/shared descendants and a conflicting parent/sibling write. Keep the G0-reviewed reborrow recovery and no-Loan function controls. Assert stable repeated capacities and empty-path allocation behavior.
- **Exit:** dataflow and ancestry agree on the same association; no hidden holder, global aliasing, runtime cost or source acceptance expansion. If meaningful verification cannot be separated from Binding activation, keep G1–G4 as reviewable internal patches in one verified activation unit. Exercise source cases while developing that unit, but do not commit/push partial public acceptance or a test-only semantic bypass.

### G3 — Complete composition before opening the public boundary

- **Targets:** existing stored-reference/reborrow, closure, local-region, Place-result, generic/default and instance tests; common producers/consumers revealed by those tests. Extend the same mechanism, not special-case recognizers.
- **Changes:** cover nested aggregate slots and multiple inputs, writable arguments and mutable captured holders, returned/reloaded descendants, complete/partial replacement, copied holders, branch/loop transport and destructor/defer uses. Ensure contract facts survive generic/default substitution and function-value storage/Move/reuse. Add a narrow repair only where a shared path loses the new facts. Keep the public guard until the complete activation unit in G4 passes; combine these units if source-driven testing requires activation.
- **Reason:** G65 is a provenance and region problem; a direct scalar example does not establish correctness for the already-supported surrounding language.
- **Verification:** the matrix in §7, positive/negative counterparts, native O0/O2 effects and exactly-once cleanup, edited snapshots, P20/P26/P27/P37 where affected. Run the same inputs through both call forms when their semantics permit it. An independent blocker gets a located reproducer and explicit boundary, never a broad success claim.
- **Exit:** the compositions that guard removal will expose use one retention mechanism; no lost sibling Loan, invented ancestor or unnecessary retention in positive controls. A safety-critical gap here blocks G4, not merely final documentation. Do not work around it with syntax-specific acceptance whitelists.

### G4 — Enable both call forms and publish accurate outcomes

- **Targets:** the two Binding rejection sites, ordinary Origin/ownership diagnostic publication, `InputDependentValueCallTest`, proposed `FixedOriginRetentionTest`, affected output-adapter tests.
- **Changes:** replace the blanket G65 guards with the completed shared contract/retention path. Keep unrelated environment/receiver restrictions. Move each original reproducer to its independently established success or Language/Proof diagnostic. Retain actual acquisition provenance when parameter fitting completes.
- **Reason:** direct and indirect behavior must become consistent in one user-visible unit. The same guard removal admits composed forms too, so G3's safety checks are prerequisites, not a later follow-up. A later Unsupported or GenerationFailed is not success.
- **Verification:** all four originals, all controls in §4.2, G3's applicable composition matrix, standalone valid wrappers at native O0/O2, Unit-returning stores, same-Origin and per-call recovery, missing/refuted Origin proof, exact output records in CLI text/JSON and both LSP related-information modes. Check independent errors, prerequisites, rebinding and diagnostic limits. Include relevant allocation/reuse tests and P20/P26/P27/P37 as affected.
- **Exit:** the scoped target is supported end to end; negative cases fail for their actual cause with understandable evidence. Update only the verified support boundary in STATUS, PLAN and CODEMAP. Do not commit/push this acceptance expansion before all compositions it exposes have passed their required checks.

### G5 — Measure, audit remaining guards and close the verified scope

- **Targets:** `tests/Workloads/VerificationWorkloads.cs`, existing Callable/local-region allocation and invalidation tests, `src/Benchmark` measurement helpers, STATUS/PLAN/CODEMAP/PLAN_HISTORY; SPEC/IMPL and INTEGRATED only if an actual specification clarification/change is adopted.
- **Changes:** add a fixed-origin-retention workload family to the existing measurement infrastructure, retaining existing workload inputs and repetition conditions. Vary calls/associations, independent roots, nested positions and CFG joins separately. Audit residual Unsupported guards and all plan-copy sites. Record exact verified scope and leave other G65 items explicit.
- **Reason:** this is a hot ownership path; repeated root/Origin scans or a new multiplicative table can regress unrelated code even when functional cases pass.
- **Verification:** focused Unit with allocation/storage limits and invalidation; whole-solution Session, selected native fixtures and affected milestones; fixed measurements before/after with retained failures. Review diagnostics and unchanged sources/configuration. Associate successful evidence with each completed implementation commit before push.
- **Exit:** all scoped completion conditions in §9, with measured costs and no unsupported performance claim. Benchmark changes require the whole-solution Session build. Timing is not a normal regression-test threshold.

## 7. Verification matrix and execution

| Dimension | Positive / negative pair or observation | Existing entry points |
| --- | --- | --- |
| Fixed versus per-call | Same-Origin reborrow resumes parent; fixed implementation cannot satisfy arbitrary per-call input | `InputDependentValueCallTest`, `CallableOriginForwardingTest` |
| Relation proof | Declared/transitive/meet proof; missing proof and finite-to-fixed escape | `CallOriginRelationTest`, `OriginRelationDiagnosticTest`, `LocalOriginClauseTest`, `ResultPremiseTest` |
| Stored authority | Safe closure/helper store; later source mutation rejected | Four G65 rows, `StoredReferenceLoanTest`, `StoredReborrowAncestryTest` |
| Independent inputs | Multiple Loans under one Origin retained; no sibling authority from equal names | Named methods in §4.2; `BorrowedInputResultTest` |
| Result and absence of result | Borrow/Place/nested result usable through its own ancestry; discard/Unit does not erase opaque retention | `InputDependentValueCallTest`, `CallablePlaceResultTest`, proposed new G65 fixtures |
| Aggregate shape | Tuple, fixed array, nominal slot/enum, nested borrow; invariant position remains invariant; empty/phantom value grants no Loan | `NestedValueCallOriginTest`, `LocalRegionCompositionTest`, `ResultOnlyOriginTest` |
| CFG and holders | Before-call access, untaken branch, joins/backedges; snapshots and copied/overwritten holders | `LocalRegionInferenceTest`, `LocalRegionCompletionTest`, `FiniteRegionCompositionTest` |
| Call preparation | Source order, receiver, named mapping, defaults, activation, abandoned preparation, Never/checking path | `CallReservationTest`, `FunctionDefaultTest`, `CallOriginRelationTest` |
| Captures and cleanup | Shared/exclusive/consuming receiver; real capture versus signature-only Origin; defer/destructor observation | `ClosureEnvironmentOriginTest`, `ClosureResultOriginTest`, `ConsumingCallableCleanupTest` |
| Generic and conversion | Definition contract retained under instance/default substitution; erasure does not relax signature compatibility | `CallableOriginForwardingTest`, `GenericCallbackEmissionTest`, existing common Function tests |
| Output and reuse | Correct conflict/related acquisition under limits; edited clause/argument/receiver/capture revokes old facts | `StoredReferenceLoanTest.PublicDiagnosticsIdentifyTheConflictAndRetainedReference`, `CallableRegionInvalidationTest`, allocation companions |

This is a set of relevant semantic dimensions, not an indiscriminate Cartesian product. Confirm every selected class/method and emitted fixture prefix exists. `FixedOriginRetentionTest` and `FixedOriginRetention*.ll` are proposed additions, not current selectors.

An initial command shape after those additions is:

```powershell
./scripts/verify.ps1 -Class FixedOriginRetentionTest,InputDependentValueCallTest,CallableOriginForwardingTest,StoredReferenceLoanTest,StoredReborrowAncestryTest,CallOriginRelationTest -Fixtures 'FixedOriginRetention*.ll','FixedInputCall*.ll' -Milestone 26,27 -Name g65-retention
```

Expand related classes and regenerated fixture patterns from the actual changes. Do not count a generation-only test as native execution, and do not select old fixture files that the current run did not generate. Follow [VERIFICATION](../../docs/dev/VERIFICATION.md) and [DIAGNOSTICS §10](../../docs/dev/DIAGNOSTICS.md).

At implementation-session completion, run `./scripts/verify.ps1 -Mode Session` with the affected regenerated native fixtures and milestone selections. NativeAOT is excluded. Toolchain identity is separately recorded; use `-VerifyToolchain` when it is required, not an implicit assumption.

For performance, reuse the conditions in [CallablePlans.md](../../src/Benchmark/CallablePlans.md): 32 warm-ups, seven samples of 64 iterations for opt-in measurements, with fixed source hashes and no extra warming after failure. Keep functional assertions, zero-allocation/retained-capacity assertions and timing reports separate. Reuse existing region indices/worklists and expose the new retained payload/capacity in the existing accounting. State exactly what those byte counts exclude.

`VerificationWorkloads.FixedInputValueCall` exercises the existing same-Origin path, not the new premise-based adaptation. Preserve it as a baseline and add a separate workload that actually exercises the new associations. Use the existing 4/8/16 scaling sizes and capacity bounds where applicable, keeping old inputs and results separately identifiable.

### Current baseline evidence and its limits

The earlier verification in this chat ran on the same source HEAD:

- `artifacts/verify/20261007-073255-355-unit-callable-local-plan-focused-audit`: passed; 368 tests, 352 native O0/O2 executions, P20/P26/P27/P37; source/configuration stability passed. It verifies existing support and the temporary G65 rejections, not this proposed solution.
- `artifacts/verify/20261007-072123-168-session-callable-local-plan-audit`: Release whole-solution build passed; 18,809 tests ran and 47 failed. Failures included file-operation access denials/process issues and `SyntaxDiagnosticTest.EveryFormHasAPhraseAndACase` missing `VirtualModifier`. Do not classify all failures as environmental. The Session stopped before its requested native/milestone phase.

These ignored artifacts need separate retention and are not carried by this document's Git commit. No new compiler build, test or timing run is claimed for writing the plan. At implementation start, establish a current baseline in the appropriate execution environment. Track unrelated failures separately; they do not justify weakening tests or reporting a failed Session as successful.

## 8. Risks, unknowns and deviation conditions

| Risk / unconfirmed point | Required investigation or trigger to change course |
| --- | --- |
| Full fixed-region semantics accidentally become whole-body pinning of every reborrow | Settle §4.1 case 1 against `FunctionValueHoldsNoLoan`, equality normalization and prior local adaptation in G0. If the distinction cannot be stated from public facts, revisit the design/spec before adding special cases. Any discovered adaptation-site escape bypass expands the common handoff scope beyond call syntax and requires replanning. |
| A synthetic retention cause conflicts with its legitimate descendants | Prove returned and reloaded reference ancestry against the same record used for conflicts. If a unique ancestor is not justified, retain a set; do not pick the first matching Origin. |
| Existing flow storage assumes every dependency has a killable concrete holder | Use a separate contract-retention reason in the shared solver. If that requires a different convergence model, revise G2 and its cost estimate before enabling any acceptance. |
| Two Binding guards cover different sets | Inventory `OriginsAsWritten` and `EnclosingOriginsAsWritten` with nested/per-call/local cases. Replace the shared semantic gap, preserving other documented boundaries rather than indiscriminately deleting checks. |
| Defaults/instances lose evidence during plan reconstruction | Audit every relevant `Set`/substitution path and verify edited/reused calls. Missing parameter mapping is a prerequisite defect, not a reason to infer correspondence from AST order. |
| Empty/phantom/Function contract positions create fake roots | Separate type well-formedness dependencies from actual stored borrow authority; include zero-sized and empty-value controls. |
| Stronger exclusive authority is downgraded by a shared destination | Preserve original acquired authority and parent suspension; compare both call forms and stored descendants. |
| Root/Origin/call combinations make memory or time grow unexpectedly | Measure axes separately before/after; retain checked budgets and warm-zero assertions. Redesign indexing if repeated full scans or multiplicative persistent storage is required. |
| Captured-store, object-root or requirement-reference cases hit independent limits | Record exact prerequisite and next owner. Extend this plan only if that repair is necessary for the defined retention scope; otherwise keep a visible boundary and do not close affected composition claims. |
| New diagnostics obscure the actual cause | Review location and factual retention evidence in CLI/JSON/LSP. Fix at the analysis/publication source; fewer messages alone is not improvement. |
| Full Session still fails on unrelated code/environment | Separate baseline versus introduced failures with evidence; do not silently repair unrelated features or claim completion. Re-establish the required verification gate before completed implementation commits are pushed. |

A specification revision is justified only by a demonstrated contradiction, unsoundness, unnecessary complexity, or a simpler consistent rule satisfying all four Kimigayo Principles. A currently unsupported valid program is not such a reason. If a revision is needed, write its minimal counterexample and rationale, update the self-contained formal spec and affected examples/tests/milestone sources, and record the intake in INTEGRATED in the same commit. Do not make the spec depend on this proposal or reopen frozen proposal text.

The largest remaining uncertainty is the exact reusable representation of the association and its interaction with multiple-source ancestry. The preceding 6–12 hour estimate is a rough estimate for this scoped work; G0/G2 may reveal a necessary redesign. Do not use the estimate as a reason to omit negative cases, diagnostics, measurements or Session verification.

## 9. Completion and document responsibilities

The fixed-input retention scope is complete only when:

1. The four original G65 cases have the reviewed dispositions, their valid counterparts execute, and both call forms consume one shared retention mechanism.
2. The §2 invariants and applicable §7 compositions pass through Binding, ownership, generation and native O0/O2; no failure merely moves to another Unsupported or GenerationFailed path.
3. Same-Origin NLL/reborrow recovery, independent input authority, fixed/per-call compatibility, capture cleanup and Origin proof diagnostics remain intact.
4. Rebinding/default/instance substitution preserves or revokes the exact facts correctly, and required allocation/storage/timing evidence is recorded without relaxed conditions.
5. Focused Verify and required whole-solution Session succeed for the final sources/configuration; residual unrelated failures are resolved or formally handled by the repository's workflow, never hidden.
6. STATUS describes only the verified support change; CODEMAP names the new common producer/consumer; PLAN records remaining G65 items; PLAN_HISTORY has a short evidence pointer. No public library API change is expected, so LIBRARY/STYLE updates are conditional on actual declaration/convention changes.

This planning task changes only this new Markdown proposal. It does not change compiler sources, tests, formal specifications, the earlier proposal, implementation status or the integration register. Proposal closure and archive movement occur only when every item has a recorded disposition under the repository's draft lifecycle.
