# G59: Element Place Borrowing Implementation Plan

Date: 2026-10-08. Status: open implementation proposal; implementation and execution verification are pending.

Planning baseline: `35bbc6ba4a501ec7942e6ac3452313d4c4562f33`. The working tree was clean when this document was prepared. The earlier comparison examined `295e5c38`; re-establish the baseline before implementation if the checkout changes.

This plan adopts the shared Place classification and capability reasoning of the supplied `G59o.md`, together with the recovery discipline and bounded implementation changes of `G59a.md`. Those documents are investigation inputs, not specification authority or instructions to execute. This document is self-contained and does not require their local download paths.

## 1. Objective and scope

Borrow the storage selected by an element or member Place without first acquiring its value. Apply that rule to Copy and Non-Copy parts, shared and exclusive uses, and the relevant owned and borrowed paths. Preserve the complete stored Type, access authority, evaluation order and all dependencies needed to keep the selected storage valid.

The work has three priorities:

1. Stop accepted programs from updating a temporary copy instead of their selected Place.
2. Support G59 and the related Place-selection failures through common classification and capability rules.
3. Prevent failed acquisitions from creating successful acquisition facts or fabricated ownership conflicts, while retaining independent diagnostics.

The scope includes dynamic Array elements, fixed-array elements with runtime indices, their stored Field/Tuple projections, and collections reached through stored Fields and safe references. It includes explicit borrows, implicit shared arguments, borrowing receivers and the inspection/collection consumers that use the same paths. Existing Slice, UniqSlice, Dictionary, user Indexable and published Place-result paths are compatibility checks; repair them here only when the same changed mechanism is responsible.

The plan does not introduce a new language feature, a second Loan engine, a general pointer/projection rewrite, or new library APIs. Object-handle adaptation, getter semantics and unrelated G75 boundaries retain their own contracts and issue owners. A dependency that prevents completion must be identified and tracked; it cannot be hidden by narrowing the required language behavior.

This proposal does not start implementation, close G59, change milestone scheduling or establish support. The current task creates and reviews this document only.

## 2. Evidence and governing contracts

### 2.1. What is established, reported and still unknown

| Evidence | Meaning for this plan |
| --- | --- |
| [PLAN G59](../../docs/dev/PLAN.md) records failed Non-Copy Field borrowing and an enclosing conflict preceding the cause. | Existing implementation issue; the three original spellings remain required reproducers. |
| Source inspection finds `BorrowStruct` falling back to `Expression(source, Read)` and borrowing its result, while `AcquireElement` can record Unsupported after creating a typed temporary. | A concrete route can confuse storage selection with value acquisition and expose recovery state to later checks. |
| `ReceiverElement`/`SharedElement`, `CompareInPlace`, `AccessType` and `PathAuthority` classify related paths differently; synthesized shared access can become a capability restriction. | Binding consistency is part of the repair, not merely an emission detail. |
| `G59o.md` reports compiler checks and IR showing Copy-part updates to temporary storage, plus false rejection through `uniq` paths. | High-priority hypotheses to reproduce. Its DLL included then-uncommitted changes; native execution was not performed. |
| `G59a.md` identifies existing projection/address and Loan machinery and explicitly leaves retained-borrow propagation unverified. | Reuse is a supported design candidate, not evidence that all lifetime cases already work. |
| The operation causing each enclosing conflict, all consumer routes, UniqSlice intersections and generic/Semantics behavior have not been established by this planning task. | U0 and the corresponding implementation units must supply evidence. |

Do not describe the supplied IR investigation as a native execution result, or infer that every additional symptom has one cause before tracing it on the chosen source snapshot.

### 2.2. Specification basis

| Contract | Required behavior |
| --- | --- |
| [SPEC §3.4 and §3.4.1](../../docs/spec/03-types-and-values.md#34-values-places-and-storage) | Place selection retains complete stored Types and path capabilities; acquisition applies to the selected Place. Update requirements propagate along the selected path only. |
| [SPEC §4.6.1](../../docs/spec/04-arrays-indexing-and-slices.md#461-access-and-length-metadata), §4.6.4 and [§4.6.9](../../docs/spec/04-arrays-indexing-and-slices.md#469-indexable-contracts) | Locate storage without intermediate Copy/Move; use the capability required by the final acquisition; evaluate and protect receivers and indices in the specified order. |
| [SPEC §10.2](../../docs/spec/10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) and [§13.5.5](../../docs/spec/13-operators-and-assignment.md#1355-follow-borrow-and-reborrow) | Distinguish an explicit borrow of a written slot from adaptation/Reborrow of a stored reference. |
| [SPEC §15.6.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), §15.6.3 and [§15.6.7](../../docs/spec/15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations) | Preserve overlap, ancestry, owner protection and reservation/activation rules. |
| [SPEC §23.3.6.4](../../docs/spec/23-compiler-services.md#23364-problems-prerequisites-and-suppression) | Report directly established problems, identify actual missing prerequisites and retain independent failures. |

No specification change is currently justified. [SETTLED](../../docs/SETTLED.md), especially the accessor-receiver decision, remains applicable: a computed/custom accessor cannot be replaced by a direct stored-Field operation. [CODEMAP](../../docs/dev/CODEMAP.md) supplies navigation, [STATUS](../../docs/STATUS.md) supplies verified support boundaries, and neither overrides SPEC.

### 2.3. Relevant implementation entry points

| Responsibility | Existing entry points |
| --- | --- |
| Selection and capability | [ElementAccess.cs](../../src/Kimi/Compiler/ElementAccess.cs): `AccessType`, `BorrowedPathRoot`, `OwnedPathRoot`, selector and receiver predicates; [Binding.ArgumentOperations.cs](../../src/Kimi/Compiler/Binding/Binding.ArgumentOperations.cs): `ReceiverElement`, `SharedElement`, `CompareInPlace`, `PathAuthority`; [Binding.Expressions.cs](../../src/Kimi/Compiler/Binding/Binding.Expressions.cs): `Writable` |
| Published index capability | [Binding.Indexers.cs](../../src/Kimi/Compiler/Binding/Binding.Indexers.cs): existing shared/exclusive entry binding |
| Storage selection and borrowing | [OwnershipAnalysis.Borrows.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Borrows.cs): `BorrowStruct`, `Receiver`, `BorrowFieldAddress`; [OwnershipAnalysis.Elements.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Elements.cs): `LocateElement`, `BorrowElementAddress`, `AcquireElement`, `BorrowStringElement` |
| Consumers and call preparation | [OwnershipAnalysis.Sequences.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Sequences.cs): `SequenceReceiver`; [OwnershipAnalysis.Reservations.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Reservations.cs): `PrepareCallArgument`; [OwnershipAnalysis.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.cs): `Call` |
| Loan evidence and code generation | [OwnershipBody.Borrows.cs](../../src/Kimi/Compiler/Analysis/OwnershipBody.Borrows.cs); [BodyLowering.Elements.cs](../../src/Kimi/Compiler/Emission/BodyLowering.Elements.cs); [BodyLowering.StructBorrows.cs](../../src/Kimi/Compiler/Emission/BodyLowering.StructBorrows.cs): `LowerStructBorrow` |

## 3. Design decisions and invariants

### D1. Preserve Place identity independently of Copy

An explicit borrow of a Place must target that Place. A fixed expected borrow or receiver adaptation must follow its own acquisition contract; it must not silently borrow a Copy snapshot when it should borrow selected storage. Copy capability decides whether a value acquisition can Copy, not which location a borrow targets.

Owned temporary values may still be materialized under the ordinary temporary-lifetime rules. Classify the resolved operation, not just the syntax: getters, raw Places, published Place calls, stored references and object handles retain their respective paths. Parentheses do not change the classification.

Do not rewrite the stored `T` of `xs[i]` into `ref/T`. Keep its complete Type, including internal Origins and generic structure, separate from the operation used to access it.

### D2. Share selection facts and derive capability from the use

Establish one bounded source of facts for element-base classification: owning versus reference path, static versus runtime selector, complete element Type, actual path authority and the required use capability. Reuse existing bound facts and interned Types where possible; the exact helper name or representation is an implementation choice.

Route `ReceiverElement`, comparison preparation and relevant access/permission consumers through those facts. A synthesized access reference is an implementation representation, not a new source-language shared layer. Do not let it erase an existing exclusive capability, and never promote through a real shared layer. Owned `let` storage still lacks Write; an immutable binding that stores a `uniq` reference is not the same as immutable owned referent storage.

Preserve predicates that answer genuinely different questions. Dynamic addressability, static Move-path eligibility, overlap proof and path authority are not interchangeable Boolean properties. A literal index into dynamic Array storage is still not a static Move Path or a disjointness proof.

Use-time capability selection must respect committed overload/member selection. Do not select a different declaration after an acquisition or Loan failure. Any new adaptation must preserve the existing Binding-established Origin and complete-Type evidence; Origin equality alone supplies neither Loan identity nor access authority.

### D3. Reuse checked address preparation; keep working value paths unless necessary

For an owned storage projection, prefer the existing `LocateElement`/`BorrowElementAddress` machinery when it can express the required borrow and lifetime. For an existing prepared reference, reuse `Receiver`/`BorrowFieldAddress` and the checked borrowed projection. Both routes must meet the same semantic contract and feed the existing Loan machinery.

Complete ownership analysis, reservation handling, retained dependencies and lowering validation together before accepting a newly supported form. An address that works only during its construction is insufficient for a stored or returned borrow.

Do not require every existing value read, assignment or arithmetic update to move to a borrowed-receiver route. Broaden that routing only when a concrete correctness gap requires it or measured simplification preserves semantics and cost. In particular, do not replace scalar Copy reads with lasting borrows, copy intermediate arrays, or widen an existing static-path Loan merely to share code.

If existing plans cannot carry the needed facts, introduce the smallest shared preparation result that does. Document its inputs, authority and ownership of checks; do not create an independent lifetime engine or container-specific escape hatch.

### D4. Separate failed acquisition from successful storage facts

An unsupported or failed acquisition must not invent a Move, initialized result, usable borrow or executable call input. Detect unsupported construction before emitting its successful acquisition state where feasible. Preserve effects of receivers, indices and earlier arguments that have already been checked; close temporary access protection on every exit.

Retain typed recovery where later independent checks need it. A recovered Type is not proof of a successful acquisition. Do not replace all `AcquireElement` recovery with `return -1`, and do not assume every caller treats `-1` as the required recovery state. Make failure status explicit through the existing mechanisms, adding a small shared representation only if those mechanisms cannot distinguish the states.

Trace dependent call preparation, reservation activation and continuation checks. When their requirement is undecidable, use the existing prerequisite model. Keep conflicts that are independently established. Do not suppress every error in a parent call or sort diagnostics to hide an unchanged cause. A single-error expectation applies only to an isolated reproducer with one independently established problem.

This is ordinary diagnostic implementation under [DIAGNOSTICS](../../docs/dev/DIAGNOSTICS.md), not activation of the full diagnostic-development workflow.

### D5. Preserve evaluation, lifetime and proof boundaries

- Evaluate each receiver and selector once in the specified order; assignment/update rules still secure the RHS at the required point. Bound checks precede address use. Reservation protection must not turn into premature exclusive activation.
- Borrow formation performs no element/Field Copy, Move, destruction or added runtime allocation. Preserve exactly-once cleanup on success, bounds failure and abandoned argument paths.
- Retain the owner protection and internal/stored-reference Loans needed by the selected target through local storage, argument forwarding and permitted returns. Release them after their actual last required use, not when an intermediate reference temporary ends.
- Distinguish a borrow of a reference slot from a Reborrow of its referent. Preserve parent/child Loan ancestry and dependencies carried by the complete stored Type.
- Keep target footprints separate from owner protection. Runtime indices supply no new non-overlap proof; conversely, do not erase structural non-overlap already required by SPEC.
- Lowering must reject inconsistent root, Type, capability, selector order or Loan evidence. Correct output alone does not justify bypassing checked-plan validation.

These decisions uphold one canonical meaning for borrowing, locally determined path authority, explicit acquisition semantics and changes verifiable against an identified source snapshot.

## 4. Implementation units

Each completed unit contains its reproducer, implementation and focused regressions. Run its required verification before committing and pushing. U0 supplies evidence and tests to subsequent units; do not commit a standalone red test suite. Keep units small enough to verify without changing sources during a build.

### U0. Establish reproducible behavior and operation-level causes

**Work:** Record the commit, working-tree input identity, compiler configuration and toolchain identity. Build from that source under the normal feedback/verification workflow; an older DLL is not the baseline. If isolation is necessary, use a suitable worktree and preserve unrelated changes. Store disposable reproducers under `temp/` and baseline evidence, including failures and IR, under `artifacts/verify/`.

Create a proposed `ElementMemberPlaceTest` class or place cases in existing focused classes. Assign stable case names; avoid unexplained probe labels. Reproduce the three G59 forms, Copy-field `@uniq`, Copy-struct receiver mutation, fixed-array runtime-index borrowing, and reference-path capability failures. Use the matrix in §5 for controls and expected outcomes.

For each failing path, map the selected Place, adaptation, ownership operations, acquisition state, Loan holder and call reservation. Trace the exact cause of any enclosing conflict. Confirm the consumers for interpolation, match, iteration and nested collections rather than inferring their route from syntax.

**Exit:** A source-identified failure inventory distinguishes observed execution, IR-only evidence, expected SPEC behavior and untested intersections. If a supplied symptom no longer reproduces, retain it as a regression candidate and correct the inventory instead of forcing the old diagnosis.

### U1. Stop invalid borrow fallback and preserve diagnostic recovery

**Targets:** `BorrowStruct`, `Receiver`, `AcquireElement`, `BorrowStringElement`, call preparation/activation and the narrow related recovery path.

**Work:** Before a fallback borrows an acquired temporary, distinguish a real temporary value from a Place whose selected storage is required. Keep supported address routes. For uncovered Place borrows, report located Unsupported and prevent emission rather than accepting a snapshot borrow. Cover exclusive and shared Place borrows; do not treat Copy shared snapshots as the final acceptable implementation.

Implement D4 with the traced callers. Preserve valid prior effects and independent errors, and ensure no failed borrow leaves synthetic Move/initialization facts or unclosed access Loans. A local sentinel return is acceptable only when its callers preserve these guarantees. Temporary rejection may ship as a verified safety fix, with the unimplemented acceptance boundary recorded explicitly.

**Verification:** Pair each rejected miscompilation reproducer with a supported control. Require no executable output for unsupported forms. Combine failure with an unrelated uninitialized use, a real conflict, a side-effecting selector and a failed argument after earlier successful preparation. Check diagnostic codes/spans and the relevant analysis state; do not presume all cascades disappear until verified.

**Exit:** Identified wrong-address acceptance is prevented, supported forms remain correct, and recovery neither manufactures conflicts nor hides independent ones. U1 alone does not close G59.

### U2. Make element classification and capability selection consistent

**Targets:** `ElementAccess`, `ReceiverElement`, `SharedElement`, `CompareInPlace`, `PathAuthority`, `Writable`, and affected consumers of access adaptations.

**Work:** Implement D2 across the relevant built-in sequence paths. Treat generated access references separately from source-level reference layers. Support both Copy and Non-Copy element bases, with shared/exclusive access chosen by the final use and bounded by actual path authority. Keep stored-reference and object-handle adaptation distinct.

Start with paths that already have ownership/emission support. Enable a new classification together with its correct execution path; if it needs U3, combine that activation with U3 rather than accepting an incomplete plan. Preserve existing user Indexable selection and declaration identity.

**Verification:** Valid member writes/borrows through `uniq/Array` and `uniq` fixed arrays; invalid shared/Slice and immutable owned-root writes; immutable reference bindings with valid exclusive referents; comparison versus ordinary borrow consistency. Inspect effect summaries and generic adaptation wherever changed facts are consumed.

**Exit:** Synthetic shared access no longer causes false permission failures, real shared layers still cap access, and no newly accepted operation relies on the old value fallback.

### U3. Complete borrowing of selected element storage

**Targets:** Ownership projection/address preparation, retained Loan propagation, call reservations and checked lowering.

**Work:** Connect D3 end to end for owned dynamic Array elements and their Field/Tuple parts, then fixed-array runtime-index elements and their parts. Use the common classification to obtain the correct mode; preserve existing value acquisition/update paths unless a demonstrated failure requires changing them.

Support both immediate and retained borrows. Keep the original owner and any internal dependencies alive through forwarding and permitted borrowed results. Verify the prepared address and reservation identity; do not silently broaden a retained borrow from the selected target to an unrelated enclosing aggregate.

**Verification:** All original G59 forms; shared borrowing of Copy parts without snapshot acquisition; `bump(xs[0].n@uniq)`, a Copy-subobject mutating receiver and `bump(ys[i]@uniq)` with post-call reads proving the original element became `42`. Native O0/O2 must check output, selector counts, bounds failures and cleanup. While a borrow remains live, reject invalidating `clear`, reallocation, overlapping replacement and owner Move; allow valid operations after its last use.

**Exit:** Both the original Non-Copy failure and the confirmed Copy wrong-address cases use actual selected storage with correct retained dependencies. IR inspection supplements native execution; it does not replace it.

### U4. Complete composition and preserve neighboring contracts

**Targets:** The same shared mechanisms and consumers identified by U0; avoid new syntax-specific borrowing exceptions.

**Work:** Cover an Array Field through owned, `ref` and `uniq` struct paths; nested Field/Tuple/array projections; generic Fields and supported Semantics cases; stored references and dependency-carrying Fields. Exercise method receivers, interpolation, match inspection, borrowing iteration and nested collection operations under their specified acquisition contracts.

Confirm Slice/UniqSlice, Dictionary, user Indexable, published Place results, temporary receivers and fixed-array static paths as controls. A custom getter remains a call boundary. Check the recorded `holder.items.insert(holder.items.length, 4)` issue only if its trace reaches the changed mechanism; do not claim it is fixed from resemblance alone.

**Verification:** Pair acceptance with shared-path rejection, invalid escape, conflicting parent use, lack of dynamic disjointness and partial-Move restrictions. Preserve independent Copy snapshots for ordinary value reads. Use independently borrowed inputs with equal Origins to detect accidental Loan-identity merging.

**Exit:** Every applicable §5 case has passing evidence or an explicit separately owned prerequisite. Any unresolved case within G59's same-cause scope keeps this plan incomplete; filing a gap alone does not satisfy completion.

### U5. Validate reuse, proof checks and completion evidence

**Work:** Add focused warm Binding/analysis/emission allocation and retained-capacity regressions for the new paths. Test edit/reanalysis recovery after successful and failed borrowing. Corrupt the specific plan evidence newly relied upon and require emission to reject it, then verify a fresh analysis recovers. Extend existing test infrastructure rather than reproducing implementation branches in assertions.

If hot-path routing or performance changes, run representative matched measurements using `src/Benchmark` after a whole-solution Release build. Include an existing value read/update control and the new borrow path; unchanged workloads must not regress unnoticed. Keep timing outside ordinary verification and retain fixed warm-up/measurement conditions. Record actual costs, not an unmeasured performance claim.

Prepare the applicable documentation changes in §7 before the final Session. Retain failed and successful evidence. Associate successful formal verification with the committed source, then push the current branch.

**Exit:** The completion criteria in §8 all hold. No second final Session is required solely because an earlier run was labeled Unit; conversely, a Session label does not supply omitted native or milestone coverage.

## 5. Required behavior matrix

The examples below are expression-level test shapes, not complete programs. Use small declared Types and functions with SPEC-derived expected behavior. Select representatives for distinct paths and interactions rather than generating the full Cartesian product.

| Case family | Representative shapes | Acceptance/rejection oracle |
| --- | --- | --- |
| Original G59 | `Console.writeLine(xs[0].name)`, `xs[0].name@ref`, interpolation containing `xs[0].name` | Valid string Field borrow; no intermediate owner acquisition. |
| Copy target identity | `bump(xs[0].n@uniq)`, `xs[0].counter.inc()`, Tuple-part mutation | Original selected storage changes; no write to a detached copy. |
| Shared Copy borrowing | Retain `xs[i].n@ref`; contrast with `let n = xs[i].n` | Borrow plan addresses selected storage; ordinary value read remains an independent Copy. |
| Fixed-array selectors | Runtime `ys[i]@uniq` and `ys[i].name@ref`; literal-index controls | Runtime address selection works without gaining static Move eligibility; supported static paths keep their precision. |
| Path capability | Owned mutable/immutable roots, `ref/Array`, `uniq/Array`, `ref/Bank` and `uniq/Bank` with `items` | Real path authority decides Write; a generated shared access does not cap an exclusive path. |
| Composition and consumers | Nested fields/tuples/arrays; string and other Non-Copy parts; method, match, iteration, length and nested indexing | Same selected storage and acquisition contract across the traced consumers. |
| Stored references | Borrow a slot containing `ref/T` or `uniq/T`; adapt its stored reference to an expected borrow | Slot identity, referent identity, complete Type, internal Origins and ancestry remain distinct. |
| Loan retention and ending | Store/forward/return a Field borrow; attempt owner replacement, resize, Move or destruction | Invalidating actions fail while required; valid post-last-use actions pass; illegal escape fails. |
| Evaluation and failure | Side-effecting receiver/index/RHS; bounds failure; later argument failure; temporary Array receiver | Required order and single evaluation, cleanup and temporary lifetime; no state invented after failure. |
| Recovery independence | Unsupported borrow beside an uninitialized read or independently conflicting Loan | Correct cause and location; independent diagnostics survive; derived records follow actual prerequisites. |
| Overlap and incomplete storage | Different dynamic indices; disjoint static parts; fixed array after Partial Move | No dynamic disjointness inference; preserve structural proofs and initialization restrictions. |
| Compatibility | Slice, UniqSlice, Dictionary, user Indexable, Place calls, getters, existing object paths | Preserve each published capability and call boundary; no accidental pointer or accessor bypass. |
| Reuse and validation | Repeated analysis/emission, source edits, failed-then-fixed input, corrupted plan | Stable allocation/capacity behavior; stale or invalid evidence never reaches emission. |

For generic and Semantics cases, use currently expressible public contracts. An unrelated existing unsupported feature is a named dependency, not an excuse to remove a required interaction from the inventory.

## 6. Verification selection and workflow

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md). The groups below are starting selections; inspect actual changed callers and fixture producers before running them. New `ElementMemberPlaceTest` and `ElementMemberPlace*.ll` names are proposed artifacts, not existing coverage.

| Unit/concern | Related existing regression classes |
| --- | --- |
| U1 recovery and fallback | `DynamicArrayIndexTest`, `CallReservationTest`, `ElementBorrowEmissionTest`, `StoredReferenceLoanTest`, `UserArithmeticUpdateTest` |
| U2 capability and adaptation | `PathCapabilityDiagnosticTest`, `MemberThroughLayersTest`, `BorrowedArrayWriteTest`, `IndexableContractTest`, `PairFollowTest`, `ElementComparisonTest`, `EffectBoundImplementationTest` |
| U3 storage and execution | `DynamicArrayBorrowTest`, `DynamicArraySharedReadTest`, `DynamicArrayElementTest`, `DynamicArrayCleanupTest`, `ElementReplacementEmissionTest`, `ElementAssignmentEmissionTest`, `ElementBorrowEmissionTest`, `StaticElementUpdateTest`, `ElementMoveEmissionTest`, `FixedArrayCaptureTest` |
| U4 composition and dependencies | `NestedBorrowedProjectionTest`, `PublishedPlaceProjectionTest`, `OwnedStoredReferenceTest`, `DictionaryStoredReferenceTest`, `StoredReferenceLoanTest`, `GenericBorrowedElementTest`, `GenericCompositeValueTest` |
| U5 cost and reuse | Related allocation tests in the selected classes, `DynamicArrayCostTest`, `StoredObjectViewTest`, plus the new path-specific reuse/validation tests |

During implementation, incremental test-project builds and selected method runs provide feedback only. Each completed code/test unit needs formal Unit verification, including related functional and allocation/reuse regressions. Select regenerated native fixture names from the actual producers, including affected existing fixtures, and run O0/O2. Do not use stale IR from an earlier assembly as completion evidence.

For illustration, after the proposed test class and fixtures exist, a focused Unit command may be:

```powershell
./scripts/verify.ps1 -Class ElementMemberPlaceTest,DynamicArraySharedReadTest,ElementBorrowEmissionTest -Fixtures 'ElementMemberPlace*.ll','DynamicArraySharedRead*.ll','ElementBorrow*.ll'
```

This example is not the whole U1-U5 selection. Expand it for the specific unit using the table and recorded dependencies. Retain the effective method/fixture selection and the check results with the source/configuration identity.

At session end, run one whole-solution Session covering all functional and allocation/reuse regressions. Explicitly select the final related native fixture set and [milestone harnesses](../../tests/milestones/README.md) 17/27 for fixed-array ownership and 29/31/37/40 for the affected collection paths. Keep milestone sources unchanged unless an independently justified authoring/specification change is required; Program 40 remains a regression input, not a workaround target.

The final Session can also satisfy the final Unit when the source/configuration and all required checks match. Additional targeted diagnostic checks and any required measurements must still pass. Prepare implementation, tests and support documentation before that run; never edit sources while verification is running. NativeAOT is not part of this plan.

After a successful formal run and commit, use `scripts/verify-commit.ps1` to associate the evidence, then push without force. Stage only this work's explicit paths. Reverify changed verification inputs; retain failures and rejected experiments under `artifacts/verify/`, and measurements under `artifacts/benchmarks/`.

Creating this proposal is documentation-only: review its diff, links and referenced symbols/classes. Do not run builds, tests, native fixtures or milestones for the proposal itself.

## 7. Documentation and issue bookkeeping

- Update `docs/STATUS.md` only when verified support changes. U1 may establish safer explicit rejection; that is not implemented borrowing support. Describe the accepted path/Type boundaries after each feature unit.
- Update `docs/dev/PLAN.md` with confirmed scope and remaining prerequisites during implementation. Close G59 only after its original borrowing and misleading-conflict symptoms are resolved with evidence. If newly confirmed same-cause failures remain, retain an explicit open owner and do not mark this plan complete.
- Update `docs/dev/CODEMAP.md` in the implementation commit when entry points or phase responsibilities change. Keep it navigation-only.
- Record a few lines of session outcome and evidence pointers in `docs/dev/PLAN_HISTORY.md`; detailed traces and results belong in commits and artifacts.
- Change `docs/dev/DIAGNOSTICS.md` only if a verified existing finding or the internal model contract changes. No automatic full diagnostic review is requested.
- Keep SPEC, STYLE and LIBRARY unchanged unless the actual work establishes a corresponding rule, convention or public declaration change. This plan requires none. Do not weaken a formal rule to accommodate implementation difficulty.
- No specification intake occurs when saving this plan, so `draft/INTEGRATED.md` needs no intake entry now. Any later deliberate spec revision must follow AGENTS.md, including affected examples/tests/milestones and same-commit intake registration. Proposal closure and freezing follow the register's disposition rules; this new document remains open.

## 8. Completion criteria and decision gates

The plan is complete only when:

1. The three original G59 spellings and the confirmed same-cause Copy/exclusive cases have correct end-to-end acceptance, with native O0/O2 results where executable.
2. Place borrows preserve target identity and retained dependencies independently of Copy; ordinary value reads retain their required Copy behavior.
3. Shared/exclusive authority, stored-reference adaptation, evaluation/cleanup, static Move eligibility and overlap restrictions have passing paired regressions.
4. Failed acquisitions create no successful acquisition facts, their dependent diagnostics follow real prerequisites, and independent errors remain visible.
5. The applicable composition inventory is accounted for, checked-plan rejection and reuse tests pass, and relevant measurements justify any performance-sensitive routing changes.
6. Required Unit/Session, selected native fixtures, milestones and source/commit evidence are complete, with support documentation matching the verified boundary.

Revisit the implementation approach at these gates:

| Finding | Required response |
| --- | --- |
| A supplied symptom fails to reproduce on the identified baseline | Correct the evidence inventory and investigate the differing source/configuration; do not claim an unobserved fix. |
| A proposed classification changes working value paths unnecessarily | Keep those paths and share only the facts/address preparation needed for correctness. Do not make global routing uniformity a completion requirement. |
| Existing projection borrowing cannot retain the required evidence | Add a bounded shared preparation representation, documenting the gap; never loosen Loan or lowering validation. |
| A synthesized access type affects overload choice, effect bounds or generic case selection | Preserve committed declaration selection and complete contracts; trace and repair the phase boundary before enabling the affected cases. This does not by itself justify a spec change. |
| Early failure handling loses independent checks or earlier valid effects | Refine typed recovery and prerequisite propagation; do not broaden suppression or return sentinels indiscriminately. |
| A genuinely separate G75 or other prerequisite is reached | Record its exact reproducer, dependency and owner. Implement the necessary bounded dependency or retain an explicit incomplete status; do not silently expand this into a general ownership rewrite. |
| A real specification contradiction is established | Document the contradiction and Principles-preserving revision, then update formal sources and affected evidence together under AGENTS.md. A missing implementation alone is not a contradiction. |
| Work stops after the safety unit or before native/toolchain verification | Report the verified intermediate boundary and outstanding checks. G59 and this plan remain open as applicable. |
