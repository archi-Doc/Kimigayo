# G65 — Fixed-Origin Loan Retention Implementation Plan

Date: 2026-10-07. Revised: 2026-10-08 following the approved G0 decision.

## 1. Scope

Complete the premise-based fixed-input boundary of G65 for value calls and nested named calls. A successful fit must preserve the actual argument Loans in every public-contract destination that may retain them. Ended dependencies allow ordinary parent recovery.

This does not close every G65 item. Uncarried environment Origins, non-Place receivers, exclusive object roots, higher-rank Origins and general captured-exclusive mutation outside this handoff remain separate boundaries. Preserve located limits for unsupported forms. Requirement Items and arithmetic have their own integrated specifications and implementation records.

The formal specification is authoritative. Integrate the approved clarification into SPEC §15.6.4, with navigation in SPEC.md and representation guarantees in IMPL §21.2.4. Do not reopen the frozen §3 of the earlier Callable and Local Region proposal. Update the integration register when adopting or closing this proposal.

## 2. Approved semantic decision

Origin compatibility and Loan authority are separate. An Origin relation proves a lifetime fit; it neither identifies storage nor creates or releases a Loan. Fitting a value to a fixed Origin does not pin its Loan throughout that Origin's complete region.

A call preserves the actual acquisitions in the results and writable storage its public contract permits to retain them. Destinations include nested stored values, writable arguments, mutable Closure environments and storage reached through captured exclusive references. No private callee-body inspection is used to prove absence of storage.

Retention follows the values reaching those destinations. Copies and Moves preserve their own dependencies; complete replacement ends the old contents, while partial replacement preserves other parts. A later read inherits reaching dependencies without attaching a new Loan to unrelated earlier values with the same Origin. Destructors, defer and checking continuations retain the existing use rules. A discarded or Unit result does not end a Loan held elsewhere. When no conflicting child dependency remains, the parent recovers ordinary access; uncertainty never establishes recovery.

Equalities, equivalent outlives premises and moving the same fit into a local annotation must have the same authority consequences. Signature-only Origins and phantom values create no actual acquisition. Distinct acquired Loans remain distinct even when fitted to one Origin.

This replaces the original whole-fixed-region association of §§4–5. G0 exposed its dependence on adaptation placement and Origin spelling. The approved rule preserves local reasoning, published-contract checking and existing reborrow recovery without a second lifetime mechanism or runtime ledger.

## 3. Required invariants

1. Instantiate only the callee's own quantified Origins; enclosing fixed Origins remain fixed. Overload selection precedes Origin and ownership checking, with no retry after failure.
2. Keep source, adapted and parameter Types distinct. Acquire each receiver/argument once, preserving its storage path, capability and parent chain. A shared result never downgrades an exclusive acquisition.
3. Use only proven fits for a contract handoff. A failed proof retains its own Origin diagnostic; it does not authorize a fictitious successful store or a cascade at destruction.
4. Match complete-Type positions. A container's slot, its borrowed referent and references stored in it are different dependencies. Function signatures are not stored borrow payloads.
5. Start call retentions only on paths entering the call after preparation and reservation activation. Preserve contiguous CallEntry operations, argument order, defaults, cleanup and Never/checking paths.
6. Conflict checking, result ancestry and stored-value reads consume the same actual dependencies. When several sources are possible, retain all; never select the first equal Origin as a unique ancestor.
7. Reuse bounded compiler storage and the existing CFG, liveness and retention solvers. Avoid per-use whole-CFG walks, global Origin alias tables, an operation × Origin × root product, runtime allocation or reference counting.
8. Reset source-dependent records on rebinding and body/default/instance reuse while retaining buffer capacity. Keep the no-dependency path cheap and warm analysis allocation-free.

## 4. Implementation units

G0 precedes G1–G4; G5 closes the verified scope. G1–G4 may form one activation unit if source-driven verification needs them together. Do not commit partially sound public acceptance.

### G0 — Establish outcomes (completed prerequisite)

- **Targets:** the four original InputDependentValueCallTest cases, FixedInputsAreOrdinaryArguments, FixedOriginRetentionTest and ignored evidence.
- **Changes:** compare direct/local fits, equality and two outlives clauses; retain valid counterparts and independent-input controls. Repair the separately reproduced local annotation losing its acquired Loan.
- **Reason:** both valid and invalid programs were behind UnsupportedBinding. Mere guard removal was unsound; rejecting everything would not implement the feature.
- **Verification:** the previous Unit/Session and measurements are recorded in PLAN_HISTORY under 2026-10-08 G65 G0. The user approved destination-based retention after that review.

The original outcomes are now fixed:

| Case | Required outcome |
| --- | --- |
| A fixed-input closure returns the reborrow; its last use precedes parent reuse | Accept; also after an equivalent local fit or equality normalization. |
| A closure stores a shared reference through a captured writable holder, then the caller reads it | Accept; reject a conflicting source write before that read. |
| A nested helper returns an exclusive reborrow and a sibling view is acquired while it remains live | Reject the conflicting acquisition/access. |
| A nested helper stores through a writable argument, then the caller writes the source before reading the holder | Reject the source write. |

### G1 — Carry the committed contract

- **Targets:** Binding.ArgumentOperations, Binding.ValueCalls, OwnershipAnalysis.Call/CallValue, OwnershipBody.Calls, default and instance substitution.
- **Changes:** reuse BoundArgumentOperation's existing source/adapted/parameter Types instead of duplicating a fit tree. At each CallEntry retain only a differing, verified, concrete parameter contract beside the actual acquired Place. Preserve explicit/receiver/default mapping and clear the reusable records on body reset.
- **Reason:** ownership needs both actual provenance and the selected public destination contract; rewriting the Place's Origin loses authority.
- **Verification:** direct, value, common Function, named/default and generic paths; equality, local adaptation, complete-Type positions, failed proof and edited arguments. A Function signature alone must hold no Loan.

### G2 — Share actual Loan transport

- **Targets:** OwnershipBody.Borrows, LocalRegions, StoredBorrows and existing referent/ancestry helpers.
- **Changes:** transport actual source dependencies through matching contract positions into results and storage. Include callable environment contents and external destinations reached through its exclusive captures. Reuse stored-retention starts/transfers and per-root flow; do not introduce a synthetic whole-region holder. Use inferred-region bounds when a stored value is later read under its published Origin.
- **Reason:** return-only propagation loses stores, and store-only propagation loses subsequent results. Origin identity alone identifies neither source nor current holder.
- **Verification:** all four originals; independent inputs, stronger exclusive authority, shared/shared compatibility, nested results, copied/overwritten holders, stored-then-retrieved values and unrelated earlier snapshots. Parent recovery must coexist with correct rejection while a child remains live.

### G3 — Validate composition and repair shared prerequisites

- **Targets:** fixed/local-region, stored-reference, capture, call-preparation, default/generic and cleanup tests; their common compiler paths.
- **Changes:** exercise tuples, arrays and nominal storage, branch/loop transport, partial replacement, consuming receivers, deferred/destructor uses and edited/reused analysis. Reuse static projection paths for partial-store recovery. Check direct Origin premises before transitive search and skip Origin-free stored subtrees to avoid irrelevant recursive proof paths. Repair a shared prerequisite where it loses the new facts; do not add syntax-specific acceptance exceptions.
- **Reason:** opening the existing guards exposes compositions, not just scalar demonstrations.
- **Verification:** positive/negative pairs, generation and native O0/O2, correct cleanup and applicable P20/P26/P27/P37 originals. Preserve unrelated boundaries explicitly. A safety-critical gap blocks activation.

### G4 — Activate and review diagnostics

- **Targets:** OriginsAsWritten/EnclosingOriginsAsWritten guards and obsolete helpers, InputDependentValueCallTest, FixedOriginRetentionTest, common diagnostic adapters.
- **Changes:** remove both completed fixed-input guards together; retain genuine proof and ownership failures. Keep the original reproducers with independently reviewed expectations. Do not suppress independent errors or manufacture ownership consequences from a rejected fit.
- **Reason:** direct and indirect forms need one rule and truthful public outcomes.
- **Verification:** focused Unit; primary locations, actual retained-value evidence, CLI text/JSON and both LSP related-information modes; independent errors and invalidation. Acceptance must reach native execution, not a later Unsupported/GenerationFailed.

### G5 — Measure and close

- **Targets:** VerificationWorkloads, CallableRegionScalingTest, existing CompilerPlanMeasurements, CODEMAP, STATUS, PLAN, PLAN_HISTORY and INTEGRATED.
- **Changes:** share fixed-Origin workloads for repeated calls, independent roots and CFG joins at 4/8/16. Preserve the existing same-Origin control. Account for retained records and indexes; audit every consumer/reset/substitution path and remove superseded code.
- **Reason:** this is a hot ownership path; correctness alone does not establish bounded cost or reuse.
- **Verification:** strict allocation/capacity tests, focused Unit, whole-solution Session and explicit opt-in measurements. Use 32 warm-ups, seven samples of 64 iterations, unchanged inputs and isolated runs. Keep failures and superseded evidence. Timing is never a functional threshold or an unmeasured speedup claim.

## 5. Verification matrix

| Dimension | Required evidence |
| --- | --- |
| Fixed/per-call distinction | FixedInputsAreOrdinaryArguments; CallableOriginForwardingTest; finite-to-fixed and missing-proof rejection. |
| Actual authority | StoredReferenceLoanTest; StoredReborrowAncestryTest; one Origin must not merge independent inputs. |
| Results/storage | Both original call forms; Unit/discarded results; writable arguments/captures; later retrieval and old snapshots. |
| Complete Types | NestedValueCallOriginTest; LocalRegionCompositionTest; tuple/array/nominal slots; invariant and phantom/Function positions. |
| CFG/lifetime | Branches, joins, loops, before-call access, replacements/copies, local-region bounds and checking continuations. |
| Preparation/cleanup | CallReservationTest; FunctionDefaultTest; ClosureEnvironmentOriginTest; ConsumingCallableCleanupTest; destructor/defer observation. |
| Reuse/cost | Edited clauses/arguments/captures and defaults/instances; zero warm allocation, stable capacity, separate scaling measurements. |
| User output | Actual conflict and retained value; Origin errors remain the cause of rejected fits; CLI/JSON/LSP and independent errors. |

Use scripts/verify.ps1 with actual class/fixture names, not stale generated files. At session end run Mode Session with affected regenerated native fixtures and milestones. Never run NativeAOT for this work. Follow docs/dev/VERIFICATION.md and DIAGNOSTICS.md §10; no source edits during builds/tests.

## 6. Completion and deviations

Complete this scope only after the original dispositions and applicable compositions pass Binding, ownership, generation and native execution; no temporary guard may merely become a later implementation failure. Focused Verify and Session must pass for unchanged inputs. Associate the committed tree with the successful evidence before pushing.

Record only verified support in STATUS. Keep CODEMAP navigation-only, PLAN under 200 lines and PLAN_HISTORY brief. LIBRARY/STYLE need changes only if a public declaration or convention changes. Record the formal intake and final disposition in INTEGRATED; close/archive this proposal when every item is incorporated, rejected with a reason or transferred to an identified owner. Implementation status is independent of specification integration.

Revise the representation if it requires global Origin aliasing, new runtime tracking, multiplicative persistent tables or incompatible conflict/ancestry answers. Preserve located unsupported forms for independent limits; a demonstrated safety gap in the activated scope blocks its completion. Any further semantic revision needs a concrete counterexample, a Principles-based rationale and synchronized formal specification, examples and tests; incompleteness alone never justifies weakening a language rule.
