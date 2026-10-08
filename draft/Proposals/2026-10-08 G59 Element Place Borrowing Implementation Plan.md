# G59: Element Place Borrowing Implementation Plan

Date: 2026-10-08 (revised the same day). Status: open implementation proposal; implementation and completion verification are pending.

Planning baseline: `22a1a958eda927303b4b1a1dfbd75feb378781e1`; its compiler sources are identical to `35bbc6ba`, and later commits change documentation only. Re-establish the baseline before implementation if the checkout changes.

Two investigations informed this plan: one supplied a symptom inventory, three root causes and a concrete classification design; the other supplied the recovery discipline and bounded address reuse. They are inputs, not specification authority or instructions to execute. This revision adds native baseline probes (§2.1) and is self-contained.

## 1. Objective and scope

Borrow the storage selected by an element or member Place without first acquiring its value. Apply that rule to Copy and Non-Copy parts, shared, exclusive and address-producing uses, and the relevant owned and borrowed paths. Preserve the complete stored Type, access authority, evaluation order and all dependencies needed to keep the selected storage valid.

The work has three priorities:

1. Stop accepted programs from updating or addressing a temporary copy instead of their selected Place. Native execution confirms such wrong code (§2.1); PLAN requires unsupported forms to fail with a diagnostic, never with wrong code.
2. Support G59 and the related Place-selection failures through common classification and capability rules.
3. Prevent failed acquisitions from creating successful acquisition facts or fabricated ownership conflicts, while retaining independent diagnostics.

The scope includes dynamic Array elements, fixed-array elements with runtime indices, their stored Field/Tuple projections, collections stored in Fields and reached through owned paths or safe references, and element members reached through `ref`/`uniq` arrays. It includes explicit borrows, `@raw` address conversion, implicit shared arguments, borrowing receivers and the inspection/collection consumers that use the same paths. Existing Slice, UniqSlice, Dictionary, user Indexable and published Place-result paths are compatibility checks; repair them here only when the same changed mechanism is responsible.

The plan does not introduce a new language feature, a second Loan engine, a general pointer/projection rewrite, or new library APIs. Object-handle adaptation, getter semantics and unrelated G75 boundaries retain their own contracts and issue owners. A dependency that prevents completion must be identified and tracked; it cannot be hidden by narrowing the required language behavior.

This proposal does not start implementation, close G59, change milestone scheduling or establish support.

## 2. Evidence and governing contracts

### 2.1. Baseline observations

Native probes were run with the Release compiler `0.1.1 (6416b537-90b5-40dd-8973-92473ab05ea8)` at O0 for `x86_64-pc-windows-msvc`. The DLL was built from the working tree shortly before the `35bbc6ba` commit and is not a verified-commit build; U0 must repeat the probes on the identified source. Sources, diagnostics, outputs and IR are retained locally in `artifacts/verify/20261008-g59-baseline-probes/` (Git-ignored).

In the shapes below, `Item` has `name: string` and `n: i32`, `bump(target: uniq/i32)` increments its referent, `xs: Array<Item>` is a mutable owned local, `ys: [2 of i32]` is a mutable owned fixed array and `i` is a runtime `isize`.

| Case | Shape | Observed | SPEC result |
| --- | --- | --- | --- |
| `G59Call` | `Console.writeLine(xs[0].name)` | `UnsupportedOwnership_Kd`, preceded by `ComparisonLoanConflict_Kd` at the enclosing call | Accept; shared Field borrow |
| `G59Ref` | `let name = xs[0].name@ref` | Same pair of diagnostics | Accept |
| `G59Interpolation` | `Console.writeLine("\(xs[0].name)")` | `UnsupportedOwnership_Kd`, preceded by `CallActivationConflict_Kd` | Accept |
| `CopyUniqDynamic` | `bump(xs[0].n@uniq)` with `xs[0].n == 41` | **Accepted; prints 41** | Element becomes 42 |
| `CopyUniqFixedRuntime` | `bump(ys[i]@uniq)` with `ys[0] == 41` | **Accepted; prints 41** | Element becomes 42 |
| `RawCopyDynamic` | `unsafe` `let p = xs[0].n@raw; *p = 42` | **Accepted; prints 41**; the same code on a local struct Field prints 42 | `P@raw` converts the address of `P` |
| `SharedCopyRetained` | `let r = xs[0].n@ref; xs[0].n = 5; use r` | `ComparisonLoanConflict_Kd` (correct rejection) | Reject |
| `SharedCopyArgument` | `show(xs[0].n)` with `show(v: ref/i32)` | Accepted; correct output | Accept as a new shared borrow of the Place (§10.2) |
| `UniqArrayMemberWrite` | `xs[0].n = 5` with `xs: uniq/Array<Item>` | `SharedPathAccess_Kd` (false rejection) | Accept |
| `RefFieldArrayRead` | `b.items[0].n` with `b: ref/Bank`, `items: Array<Item>` | Two `TransferRequired_Kd` (false rejection) | Accept as a Copy read |
| Control | `xs[0].n = 5` on the owned `xs` | Accepted; prints 5 | Accept |

Code reading shows that `SharedCopyRetained` and `SharedCopyArgument` borrow a materialized snapshot rather than the element Place. The root protection currently hides that deviation from safe code; `RawCopyDynamic` shows that the same route is observable through an address conversion.

The investigation input additionally reported, without native execution, these same-cause candidates for U0 to reproduce: Copy-struct receiver mutation `xs[0].c.inc()`, Tuple-part mutation, `xs[0].0.inc()` through `uniq/Array<(P, i32)>`, `xs[0].n = 4` through `uniq/[2 of A]`, `b.items[0].n = 3` through `uniq/Bank` (`InvalidAssignment_Kd`), `Console.writeLine(ys[i].name)` (`UnsupportedOwnership_Kd`), and the consumers `xs[0].inner.show()`, `match xs[0].kind`, `for v in xs[0].items` and `xs[0].items.length`/`.append(3)`/`[1]`, with cascades of up to six `MovedPlace_Kd`. Working controls include `xs[0].show()`, `xs[0]@ref`, Copy Field reads and writes, `xs[0].name = "x"`, `xs[0].name == "Aki"`, Dictionary members, reads through `ref/Array` and literal-index fixed-array members.

Unknown until U0: the exact operations producing each enclosing conflict, whether interpolation, `match` and iteration consumers reach `BorrowStruct`, UniqSlice element members, and generic/Semantics instantiation behavior.

### 2.2. Root causes

**R1. Place access falls back to value acquisition.** When `BorrowStruct` finds no specific route, its final branch evaluates the source with `Expression(source, Read)` and borrows the result. For a Place selection below a dynamic element this reaches `ElementValue → LocateElement → AcquireElement`. A Copy part is copied into a temporary, so exclusive and address uses lose writes or observe the wrong address. A Non-Copy part reports `Unsupported` after `AcquireElement` has already created a typed temporary and its Produce state. Later checks can treat that state as a real acquisition, which is the likely source of the enclosing conflict located at the statement start and published before its cause; U0 confirms the exact operation. SPEC §3.4 states that designator outcomes are alternatives, not fallback stages.

**R2. Element-base classification is duplicated and Type-driven.** `ReceiverElement` uses `IsSharedElement`, which excludes owned Arrays; `CompareInPlace` uses `IsBorrowedReceiver`, which includes them, which is why element string comparison works while element Field borrowing does not. `IsBorrowedSelection`, `ReachesThroughBorrow` and `IsExclusiveArrayElement` answer neighboring questions differently. None classifies by the actual selector kinds and access path, so an owned `Array` Field reached through `ref/Bank` is treated as an owned root.

**R3. A synthesized access reference acts as a path layer.** `SharedElement` records a `ref/T` adaptation for a Non-Copy element base. `AccessType` returns it and `PathAuthority` counts it as a shared layer, so an exclusive path such as `uniq/Array<T>` loses Write. This contradicts SPEC §3.4.1, where an index selects its capability from the final acquisition of its use. User Indexable already implements the intended pattern: Binding binds `indexUniq` when the real path permits it, `AccessType(node, exclusive)` promotes the shared receiver, and `PathAuthority` treats the user-index root as representation rather than as a layer.

### 2.3. Specification basis

| Contract | Required behavior |
| --- | --- |
| [SPEC §3.4 and §3.4.1](../../docs/spec/03-types-and-values.md#34-values-places-and-storage) | A Place carries its complete Type, path capabilities and dependencies; designator outcomes are alternatives, never fallback stages; the lending point of an owned Place follows standard Field, Tuple and element projections; `@ref`/`@uniq` borrow the written slot; an index selects its capability from the final acquisition; update requirements propagate along the selected path only. |
| [SPEC §4.6.1](../../docs/spec/04-arrays-indexing-and-slices.md#461-access-and-length-metadata), §4.6.4 and [§4.6.9](../../docs/spec/04-arrays-indexing-and-slices.md#469-indexable-contracts) | Locate storage without intermediate Copy/Move; use the capability required by the final acquisition; evaluate and protect receivers and indices in the specified order. |
| [SPEC §10.2](../../docs/spec/10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) and [§13.5.5](../../docs/spec/13-operators-and-assignment.md#1355-follow-borrow-and-reborrow) | A readable Place at `ref/U` is a new shared borrow; only an owner temporary is materialized. Distinguish an explicit borrow of a written slot from adaptation/Reborrow of a stored reference. |
| [SPEC §5.4](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#54-addresses-and-pointer-conversions) | `P@raw` is the address of the written slot `P`, after the checks of an immediately ending `@ref`; only a Temporary Value is materialized. |
| [SPEC §15.6.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), §15.6.3 and [§15.6.7](../../docs/spec/15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations) | Preserve overlap, ancestry, owner protection and reservation/activation rules; runtime indices prove no non-overlap. |
| [SPEC §23.3.6.4](../../docs/spec/23-compiler-services.md#23364-problems-prerequisites-and-suppression) | Report directly established problems, identify actual missing prerequisites and retain independent failures. |

No specification change is currently justified: the observed behavior violates consistent existing rules. [SETTLED](../../docs/SETTLED.md), especially the accessor-receiver decision, remains applicable: a computed/custom accessor cannot be replaced by a direct stored-Field operation. [CODEMAP](../../docs/dev/CODEMAP.md) supplies navigation, [STATUS](../../docs/STATUS.md) supplies verified support boundaries, and neither overrides SPEC.

### 2.4. Relevant implementation entry points

| Responsibility | Existing entry points |
| --- | --- |
| Selection and capability | [ElementAccess.cs](../../src/Kimi/Compiler/ElementAccess.cs): `AccessType`, `BorrowedPathRoot`, `OwnedPathRoot`, `IsSharedElement`, `IsBorrowedReceiver`, `IsExclusiveArrayElement`, `ReachesThroughBorrow`; [Binding.ArgumentOperations.cs](../../src/Kimi/Compiler/Binding/Binding.ArgumentOperations.cs): `ReceiverElement`, `SharedElement`, `CompareInPlace`, `PathAuthority`; [Binding.Expressions.cs](../../src/Kimi/Compiler/Binding/Binding.Expressions.cs): `Writable` |
| Use-driven precedent | [Binding.Indexers.cs](../../src/Kimi/Compiler/Binding/Binding.Indexers.cs): exclusive entry binding; `AccessType(node, exclusive)` promotion; the user-index case of `PathAuthority` |
| Storage selection and borrowing | [OwnershipAnalysis.Borrows.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Borrows.cs): `BorrowStruct` and its final fallback, `Receiver`, `BorrowFieldAddress`, `ReadBorrowedField`, `WriteBorrowedField`; [OwnershipAnalysis.Elements.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Elements.cs): `LocateElement`, `BorrowElementAddress`, `AcquireElement`, `ElementValue`, `BorrowStringElement` |
| Route-dispatch precedent | [OwnershipAnalysis.Arithmetic.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Arithmetic.cs): the user-arithmetic update target dispatch, which selects one route per Place form, locates owned projections with `LocateElement`/`BorrowElementAddress` and reports any other form as `Unsupported` |
| Consumers and call preparation | [OwnershipAnalysis.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.cs): `Expression` member/element dispatch, address and borrow conversions, `Call`; [OwnershipAnalysis.Sequences.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Sequences.cs): `SequenceReceiver`; [OwnershipAnalysis.Reservations.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Reservations.cs): `PrepareCallArgument` |
| Loan evidence and code generation | [OwnershipBody.Borrows.cs](../../src/Kimi/Compiler/Analysis/OwnershipBody.Borrows.cs); [BodyLowering.Elements.cs](../../src/Kimi/Compiler/Emission/BodyLowering.Elements.cs); [BodyLowering.StructBorrows.cs](../../src/Kimi/Compiler/Emission/BodyLowering.StructBorrows.cs): `LowerStructBorrow`, `TryBorrowedPathOffset` |

### 2.5. Existing coverage

`DynamicArraySharedReadTest` covers `(values@uniq)[0].id` reads and `values[0].id += 1`; `ElementComparisonTest`, `PublishedPlaceProjectionTest`, `ElementReplacementEmissionTest`, `StaticElementUpdateTest`, `ElementBorrowEmissionTest`, `PathCapabilityDiagnosticTest` and `UserArithmeticUpdateTest` cover neighboring routes. No test covers method calls on built-in Array element members (`xs[0].inner.m()`), `SharedPathAccess_Kd` for element member writes, borrows of Copy parts below dynamic elements, or `@raw` of such parts. No test expects element-member Unsupported. `ElementBorrowEmissionTest.UnsupportedReceiversAndPathsRemainExplicit` contains `same(a[i], a[0])`, which may become accepted under SPEC in U4.

## 3. Design decisions and invariants

### D1. Preserve Place identity independently of Copy

An explicit borrow, a borrowing receiver, an address conversion and a fixed `ref/U` adaptation of a readable Place all target that Place. Copy capability decides whether a value acquisition can Copy, not which location a borrow targets. Only an owner temporary is materialized for a borrow (§10.2).

Classify the resolved operation, not just the syntax: getters, raw Places, published Place calls, stored references and object handles retain their respective paths. Parentheses do not change the classification. Do not rewrite the stored `T` of `xs[i]` into `ref/T`; keep its complete Type, including internal Origins and generic structure, separate from the operation used to access it.

### D2. One element-base classifier; capability derived from the use

Establish one classifier for element and member bases that reports orthogonal facts:

- root kind: owned local/parameter/capture, owner temporary, safe reference, Slice, published Place, raw Place, or getter/accessor boundary;
- selector kind at each level: static (Field, Tuple position, in-range literal fixed-array index) or runtime;
- actual path authority (Owner, Uniq or Ref) computed from real layers only;
- the complete element Type and the Binding `PlaceOrigin` used for any synthesized access reference.

Route `ReceiverElement`, `CompareInPlace` and the element decisions of `PathAuthority`, `Writable` and `IsExclusiveArrayElement` through these facts. Predicates that answer different questions stay distinct: dynamic addressability, static Move-path eligibility, overlap proof and path authority are not interchangeable. A literal index into dynamic Array storage is never a static Move Path or a disjointness proof.

Treat a synthesized base reference as a representation, not a source-level layer. `PathAuthority` does not count it and continues to the selection, as it already does for a user-index root. `AccessType(node, exclusive)` promotes it to `uniq` with the same Origin when the base's real path authority permits exclusive access, as user Indexable already does. A real shared layer, a Slice or an owned `let` root still caps the capability; an immutable binding that stores a `uniq` reference is not immutable referent storage.

Compute the facts once per node during Binding and store them in existing side tables or interned forms, with no growth in warm rebind allocation. Ownership never creates Origins; it consumes the Binding-recorded Types. Use-time capability selection respects committed overload/member selection and never selects a different declaration after an acquisition or Loan failure.

### D3. Route by root; keep working value paths

The classifier's facts select one borrow route:

- An owned root, with any mix of static and runtime selectors, uses the ownership projection: `LocateElement` locates the Place once, and `BorrowElementAddress` borrows it in the mode the use requires, as user arithmetic updates already do. The static Field/Tuple suffix stays inside the projection.
- A reference root uses the receiver route: `Receiver`, then `BorrowFieldAddress` or the Sequence element borrow.

Do not record synthesized adaptations for owned bases merely to reach the receiver route. Value reads, assignments and updates of owned bases stay in `ElementValue`, `AssignElement` and `UpdateElement`; ordinary Copy reads must not become lasting borrows, copy intermediate arrays or widen an existing static-path Loan. The existing comparison adaptation for owned Arrays stays a working route; it consumes the classifier facts, and converging it is not a completion requirement.

Complete ownership analysis, reservation handling, retained dependencies and lowering validation together before accepting a newly supported form. An address that works only during its construction is insufficient for a stored or returned borrow. If existing plans cannot carry the needed facts, introduce the smallest shared preparation result that does; do not create an independent lifetime engine or container-specific escape hatch.

### D4. Fail closed; separate failed acquisition from successful facts

`BorrowStruct` accepts a value acquisition only for a value expression. A Place selection that reaches the final branch without a supported route is a located `Unsupported` before any acquisition, Produce, projection or access Loan is emitted for it. This check-before-emit rule is permanent: later units add routes ahead of it, never exceptions inside it.

During the transition, the rule applies to every use whose result can observe the snapshot: exclusive borrows and receivers, Non-Copy parts, and address-producing conversions (`@raw` and address adaptations). A shared `ref` borrow of a Copy part keeps its current snapshot route until its correct route exists (U3–U5), because the retained root protection hides the deviation from safe code; STATUS records it as a known deviation from §10.2 until then. Unsafe address observation through such a `ref` remains part of that deviation and must be covered by U5's removal.

An unsupported or failed acquisition must not invent a Move, initialized result, usable borrow or executable call input. Preserve the effects of receivers, indices and earlier arguments that have already been checked, and close temporary access protection on every exit. Independent nested expressions inside a rejected selection may still be analyzed for their own diagnostics, provided their results are not consumed as a successful acquisition; U0 decides this per traced route.

Retain typed recovery where later independent checks need it. A recovered Type is not proof of a successful acquisition. Do not replace all `AcquireElement` recovery with `return -1`, and do not assume every caller treats `-1` as the required recovery state. When a dependent call requirement is undecidable, use the existing prerequisite model. Keep independently established conflicts; do not suppress every error in a parent call or reorder diagnostics to hide an unchanged cause. A single-error expectation applies only to an isolated reproducer with one independently established problem.

This is ordinary diagnostic implementation under [DIAGNOSTICS](../../docs/dev/DIAGNOSTICS.md), not activation of the full diagnostic-development workflow.

### D5. Preserve evaluation, lifetime and proof boundaries

- Evaluate each receiver and selector once in the specified order; assignment/update rules still secure the RHS at the required point (`values[key()].0 += rhs()`, `make()[index()].id`). Bound checks precede address use. Reservation protection must not turn into premature exclusive activation.
- Borrow formation performs no element/Field Copy, Move, destruction or added runtime allocation. Preserve exactly-once cleanup on success, bounds failure and abandoned argument paths; a replaced value is destroyed once.
- Retain the owner protection and internal/stored-reference Loans needed by the selected target through local storage, argument forwarding and permitted returns. Release them after their actual last required use, not when an intermediate reference temporary ends.
- Distinguish a borrow of a reference slot from a Reborrow of its referent. Preserve parent/child Loan ancestry and dependencies carried by the complete stored Type.
- Keep target footprints separate from owner protection. A runtime-selected base protects its whole root while borrowed; runtime indices supply no new non-overlap proof, and structural non-overlap required by SPEC is not erased.
- `@move` of an element without a static Move Path stays `StaticMovePathRequired_Kd`; a bare Non-Copy read stays one `TransferRequired_Kd`.
- Lowering must reject inconsistent root, Type, capability, selector order or Loan evidence. Correct output alone does not justify bypassing checked-plan validation.

These decisions uphold one canonical meaning for borrowing, locally determined path authority, explicit acquisition semantics and changes verifiable against an identified source snapshot.

### 3.1. Rejected alternatives

| Alternative | Reason not adopted |
| --- | --- |
| Suppress or reorder the secondary diagnostics only | Wrong code and the unsupported forms remain. |
| Loosen `OwnedPathRoot` to admit runtime selectors | Grants static Move, initialization and non-overlap facts that runtime selectors do not establish. |
| Classify element bases only in ownership | Every consumer that reads `BorrowedPathRoot`/`AccessType` needs parallel handling, and ownership would have to create Origins. |
| Add a retained projection-borrow model beside the Loan engine | Duplicates the Loan engine. |
| Record a base adaptation for owned Arrays and move all their value operations to the receiver route | Changes frequent value paths (`accounts[0].balance` in Programs 29/31/37/40), IR and allocation without a correctness need; kept only as the gated option of §8. |
| Rewrite `xs[i]` to `ref/T` | Loses the complete stored Type and conflates access with Type. |
| Reject every snapshot borrow, including shared `ref` of Copy parts, in the safety unit | Rejects correct, currently accepted programs such as `show(xs[0].n)` before their correct route exists. |

## 4. Implementation units

Each completed unit contains its reproducer, implementation and focused regressions. Run its required verification before committing and pushing. U0 supplies evidence and tests to subsequent units; do not commit a standalone red test suite. Keep units small enough to verify without changing sources during a build. U1 is shippable alone and, because it stops wrong code, should be scheduled ahead of the remaining units.

### U0. Establish reproducible behavior and operation-level causes

**Work:** Record the commit, working-tree input identity, compiler configuration and toolchain identity. Build from that source under the normal feedback/verification workflow; the baseline DLL of §2.1 is not that build. If isolation is necessary, use a suitable worktree and preserve unrelated changes. Store disposable reproducers under `temp/` and baseline evidence, including failures and IR, under `artifacts/verify/`.

Create the proposed `ElementMemberPlaceTest` class with the stable case names of §2.1 and §5; avoid unexplained probe labels. Choose representatives along three axes rather than the full product: receiver (owned `var`/`let` Array, fixed array with runtime index, Array Field of a local struct, `ref`/`uniq` Array, `uniq` fixed array, Array Field through `ref`/`uniq` struct; Slice, Dictionary and temporary Array as controls), part (string, `i32`, Copy struct with a `uniq` method, Tuple element, nested Array, enum Subject), and use (bare read, retained `@ref`, implicit `ref` argument, interpolation, `@uniq` argument, `uniq` receiver, `@raw`, assignment and compound assignment, `@move`, `append` while borrowed). Wrong-code cases become native fixtures with expected output.

For each failing path, map the selected Place, adaptation, ownership operations, acquisition state, Loan holder and call reservation. Trace the operation producing each enclosing conflict, for example whether the `AcquireElement` Produce or an unclosed access Loan, using environment-gated trace lines that are removed before commit. Confirm whether interpolation, `match`, iteration and nested-collection consumers reach `BorrowStruct` rather than inferring it from syntax.

**Exit:** A source-identified failure inventory distinguishes native execution, IR-only evidence, expected SPEC behavior and untested intersections. If a reported symptom no longer reproduces, retain it as a regression candidate and correct the inventory instead of forcing the old diagnosis.

### U1. Fail closed at the borrow fallback

**Targets:** The final branch of `BorrowStruct`, `Receiver`, the address-conversion paths that call `BorrowStruct`, and the narrow recovery path of the traced callers.

**Work:** Before the final branch evaluates `source`, classify it as a value expression or a Place selection (Field, Tuple element, element; excluding getter results, raw Places and Place calls, which keep their routes). For a Place selection with an exclusive or address-producing use, or a Non-Copy target, report `Unsupported` at the selection and return without emitting plan state (D4). Keep shared `ref` snapshots of Copy parts. Keep every supported earlier route.

Implement the D4 recovery rules with the traced callers so that G59's enclosing conflicts no longer arise from fabricated acquisitions, while independent errors remain.

**Verification:** `CopyUniqDynamic`, `CopyUniqFixedRuntime`, `RawCopyDynamic`, Copy-struct receiver mutation and Tuple-part mutation each produce one located `UnsupportedOwnership_Kd` and no IR. The G59 forms produce `Unsupported` without the fabricated conflict; check the JSON diagnostics. `SharedCopyArgument`, `SharedCopyRetained` and all controls are unchanged. Combine an unsupported borrow with an unrelated uninitialized use, a real conflict, a side-effecting selector and a failed later argument after earlier successful preparation.

**Exit:** No identified wrong-address form is accepted; supported forms and accepted shared Copy reads are unchanged; recovery neither manufactures conflicts nor hides independent ones. U1 alone does not close G59.

### U2. One classifier and use-derived capability for borrowed receivers

**Targets:** `ElementAccess` (the classifier and `AccessType` promotion), `ReceiverElement`, `SharedElement`, `CompareInPlace`, `PathAuthority`, `Writable`, and the consumers of these adaptations.

**Work:** Implement D2 for receivers already on the receiver route: `ref`/`uniq` Arrays, `uniq` fixed arrays, Slices and Dictionary references. Treat synthesized base references as representation, promote them for exclusive uses bounded by real path authority, and keep stored-reference and object-handle adaptation distinct. Preserve user Indexable selection and declaration identity. Enable a classification only together with a correct execution path; if that requires U3 or U4, combine the activation with that unit.

**Verification:** `UniqArrayMemberWrite`, `xs[0].inner.rename()` and `xs[0].0.inc()` through `uniq/Array`, and `xs[0].n = 4` through a `uniq` fixed array are accepted with native O0/O2 results. Writes through a Slice, `ref/Array` or an owned `let` root keep one `SharedPathAccess_Kd` or `InvalidAssignment_Kd`. An immutable binding of a `uniq` reference still writes its referent. Comparison and ordinary borrow agree. Inspect effect summaries, Hover adaptation display and generic adaptation wherever changed facts are consumed.

**Exit:** Synthesized shared access no longer causes false permission failures, real shared layers still cap access, and no newly accepted operation relies on the value fallback.

### U3. Owned dynamic Array element Places

**Targets:** `BorrowStruct` (a projection route ahead of the fail-closed branch), `LocateElement`/`BorrowElementAddress`, retained Loan propagation, call reservations and checked lowering.

**Work:** Implement D3 for owned dynamic Arrays, including temporary Arrays and Arrays inside owned inline paths, and their Field/Tuple parts. Support immediate and retained borrows, borrowing receivers and the consumers traced in U0. Keep the original owner and any internal dependencies alive through forwarding and permitted borrowed results; do not broaden a retained borrow from the selected target to an unrelated aggregate. Once the route exists, shared Copy borrows of these Places use it instead of the snapshot.

**Verification:** All G59 forms; `CopyUniqDynamic` printing 42; Copy-struct receivers; `SharedCopyArgument` and retained shared Copy borrows addressing the element; the consumer forms of §2.1. Native O0/O2 check output, selector counts, bounds failures and cleanup. While a borrow is live, reject `clear`, reallocation, overlapping replacement, `append` and owner Move with one conflict naming the retained value; allow valid operations after its last use. Value reads and updates of owned bases keep their IR and allocation behavior.

**Exit:** Owned dynamic element Places are borrowed in place with correct retained dependencies.

### U4. Fixed-array runtime selectors

**Targets:** The classifier (a non-literal fixed-array key is a runtime selector) and the `BorrowStruct` projection route for owned fixed arrays.

**Work:** Support exclusive and shared borrows of runtime-selected fixed-array elements and their parts through the same projection route, without granting static Move eligibility. Preserve the precision of literal-index static paths.

**Verification:** `CopyUniqFixedRuntime` prints 42; `Console.writeLine(ys[i].name)` is accepted; a runtime index into a partially moved fixed array is still rejected; update `same(a[i], a[0])` only as SPEC requires.

**Exit:** Runtime fixed-array selection reaches its Place without new Move or overlap facts.

### U5. Paths through references, and removal of the snapshot route

**Targets:** The classifier (an element whose path passes a reference is a borrowed base whatever its stored Type), `IsExclusiveArrayElement`, `Writable`'s element decision, and the fail-closed branch.

**Work:** Make element decisions path-based, so `b.items[0].n` through `ref/Bank` is a Copy read and `b.items[0].n = 3` through `uniq/Bank` is a write. Then extend the fail-closed rule of D4 to shared Copy borrows: no Place selection reaches a value snapshot. Check the recorded `holder.items.insert(holder.items.length, 4)` issue only if its trace reaches this mechanism; do not claim it is fixed from resemblance alone.

**Verification:** `RefFieldArrayRead` and its `uniq/Bank` write are accepted with native results; the shared deviation recorded in STATUS is removed with a test showing that the borrow addresses the element.

**Exit:** Every Place borrow uses a Place route; the snapshot route is unreachable for Place selections.

### U6. Composition and neighboring contracts

**Targets:** The shared mechanisms and consumers identified by U0; no new syntax-specific borrowing exceptions.

**Work:** Cover nested Field/Tuple/Array projections, generic Fields and supported Semantics cases, stored references and dependency-carrying Fields, method receivers, interpolation, `match` inspection, borrowing iteration and nested collection operations under their acquisition contracts. Confirm Slice/UniqSlice, Dictionary, user Indexable, published Place results, temporary receivers, getters and static fixed-array paths as controls; a custom getter remains a call boundary.

**Verification:** Pair acceptance with shared-path rejection, invalid escape, conflicting parent use, lack of dynamic disjointness and partial-Move restrictions. Preserve independent Copy snapshots for ordinary value reads. Use independently borrowed inputs with equal Origins to detect accidental Loan-identity merging.

**Exit:** Every applicable §5 case has passing evidence or an explicit, separately owned prerequisite. Any unresolved case within G59's same-cause scope keeps this plan incomplete; filing a gap alone does not satisfy completion.

### U7. Reuse, checked-plan validation and completion

**Work:** Add focused warm Binding/analysis/emission allocation and retained-capacity regressions for the new routes and the classifier. Test edit/reanalysis recovery after successful and failed borrowing. Corrupt the plan evidence newly relied upon and require emission to reject it, then verify that a fresh analysis recovers. Extend existing test infrastructure rather than reproducing implementation branches in assertions.

If hot-path routing or performance changes, run matched measurements in `src/Benchmark` after a whole-solution Release build, including an existing value read/update control and the new borrow route. Keep timing outside ordinary verification with fixed warm-up and measurement conditions. Record actual costs, not unmeasured performance claims.

Prepare the documentation of §7 before the final Session. Retain failed and successful evidence. Associate successful formal verification with the committed source, then push the current branch.

**Exit:** The completion criteria in §8 hold. No second final Session is required solely because an earlier run was labeled Unit; a Session label does not supply omitted native or milestone coverage.

## 5. Required behavior matrix

The examples are expression-level test shapes, not complete programs. Use small declared Types and functions with SPEC-derived expected behavior. Select representatives for distinct paths and interactions rather than generating the full Cartesian product.

| Case family | Representative shapes | Acceptance/rejection oracle |
| --- | --- | --- |
| Original G59 | `Console.writeLine(xs[0].name)`, `xs[0].name@ref`, interpolation containing `xs[0].name` | Valid string Field borrow; no intermediate owner acquisition; no fabricated conflict. |
| Copy target identity | `bump(xs[0].n@uniq)`, `xs[0].counter.inc()`, Tuple-part mutation, `bump(ys[i]@uniq)` | Original selected storage changes; no write to a detached copy. |
| Address identity | `xs[0].n@raw` written through in `unsafe`; compare with the same code on a local struct | The pointer addresses the element. |
| Shared Copy borrowing | Retain `xs[i].n@ref`; `show(xs[i].n)`; contrast with `let n = xs[i].n` | Borrow addresses selected storage; ordinary value read remains an independent Copy. |
| Fixed-array selectors | Runtime `ys[i]@uniq` and `ys[i].name@ref`; literal-index controls | Runtime address selection without static Move eligibility; static paths keep their precision. |
| Path capability | Owned mutable/immutable roots, `ref/Array`, `uniq/Array`, `uniq` fixed array, `ref/Bank` and `uniq/Bank` with `items` | Real path authority decides Write; a synthesized shared access does not cap an exclusive path. |
| Composition and consumers | Nested fields/tuples/arrays; string and other Non-Copy parts; method, `match`, iteration, `length` and nested indexing | Same selected storage and acquisition contract across the traced consumers. |
| Stored references | Borrow a slot containing `ref/T` or `uniq/T`; adapt its stored reference to an expected borrow | Slot identity, referent identity, complete Type, internal Origins and ancestry remain distinct. |
| Loan retention and ending | Store/forward/return a Field borrow; attempt owner replacement, resize, `append`, Move or destruction | Invalidating actions fail while required; valid post-last-use actions pass; illegal escape fails. |
| Evaluation and failure | Side-effecting receiver/index/RHS; bounds failure; later argument failure; temporary Array receiver | Required order and single evaluation, cleanup and temporary lifetime; no state invented after failure. |
| Recovery independence | Unsupported borrow beside an uninitialized read or an independently conflicting Loan | Correct cause and location; independent diagnostics survive; derived records follow actual prerequisites. |
| Overlap and incomplete storage | Different dynamic indices; disjoint static parts; fixed array after Partial Move; `@move` of a dynamic element | No dynamic disjointness inference; structural proofs and initialization restrictions preserved; `StaticMovePathRequired_Kd` kept. |
| Compatibility | Slice, UniqSlice, Dictionary, user Indexable, Place calls, getters, existing object paths | Each published capability and call boundary preserved; no accidental pointer or accessor bypass. |
| Reuse and validation | Repeated analysis/emission, source edits, failed-then-fixed input, corrupted plan | Stable allocation/capacity behavior; stale or invalid evidence never reaches emission. |

For generic and Semantics cases, including `Array<s/T>`, use currently expressible public contracts. An unrelated existing unsupported feature is a named dependency, not an excuse to remove a required interaction from the inventory.

## 6. Verification selection and workflow

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md). The groups below are starting selections; inspect actual changed callers and fixture producers before running them. New `ElementMemberPlaceTest` and `ElementMemberPlace*.ll` names are proposed artifacts, not existing coverage.

| Unit/concern | Related existing regression classes and harnesses |
| --- | --- |
| U1 fail-closed fallback | `DynamicArrayIndexTest`, `CallReservationTest`, `ElementBorrowEmissionTest`, `DynamicArraySharedReadTest`, `StaticElementUpdateTest`, `StoredReferenceLoanTest`, `UserArithmeticUpdateTest` |
| U2 classifier and capability | `PathCapabilityDiagnosticTest`, `MemberThroughLayersTest`, `PublishedPlaceProjectionTest`, `BorrowedArrayWriteTest`, `IndexableContractTest`, `PairFollowTest`, `ElementComparisonTest`, `StoredObjectViewTest`, `EffectBoundImplementationTest` |
| U3 owned dynamic Arrays | `DynamicArrayBorrowTest`, `DynamicArraySharedReadTest`, `DynamicArrayElementTest`, `DynamicArrayCleanupTest`, `DynamicArrayCostTest`, `ElementReplacementEmissionTest`, `ElementAssignmentEmissionTest`, `ElementBorrowEmissionTest`, `NestedCollectionTest`, `ArrayMembersTest`, `GenericCompositeValueTest`; milestones 29, 31, 37 and 40 |
| U4 fixed-array runtime selectors | `ElementMoveEmissionTest`, `FixedArrayCaptureTest`, `StaticElementUpdateTest`, `ElementBorrowEmissionTest`; milestones 17 and 27 |
| U5 reference paths | `NestedBorrowedProjectionTest`, `OwnedStoredReferenceTest`, `MemberThroughLayersTest`, `PathCapabilityDiagnosticTest` |
| U6 composition and dependencies | `NestedBorrowedProjectionTest`, `PublishedPlaceProjectionTest`, `OwnedStoredReferenceTest`, `DictionaryStoredReferenceTest`, `StoredReferenceLoanTest`, `GenericBorrowedElementTest`, `GenericCompositeValueTest` |
| U7 cost and reuse | Related allocation tests in the selected classes, `DynamicArrayCostTest`, `StoredObjectViewTest`, plus the new path-specific reuse/validation tests |

During implementation, incremental test-project builds and selected method runs provide feedback only. Each completed code/test unit needs formal Unit verification, including related functional and allocation/reuse regressions. Select regenerated native fixture names from the actual producers, including affected existing fixtures, and run O0/O2. Do not use stale IR from an earlier assembly as completion evidence.

For illustration, after the proposed test class and fixtures exist, a focused U1 command may be:

```powershell
pwsh -Command "./scripts/verify.ps1 -Class ElementMemberPlaceTest,ElementBorrowEmissionTest,DynamicArraySharedReadTest,DynamicArrayIndexTest,StaticElementUpdateTest,UserArithmeticUpdateTest,CallReservationTest,StoredReferenceLoanTest -Fixtures 'ElementMemberPlace*.ll','DynamicArraySharedRead*.ll','ElementBorrow*.ll'"
```

Expand each unit's selection from the table and recorded dependencies. Retain the effective method/fixture selection and the check results with the source/configuration identity.

At session end, run one whole-solution Session covering all functional and allocation/reuse regressions. Explicitly select the final related native fixture set and [milestone harnesses](../../tests/milestones/README.md) 17/27 for fixed-array ownership and 29/31/37/40 for the affected collection paths. Keep milestone sources unchanged unless an independently justified authoring/specification change is required; Program 40 remains a regression input, not a workaround target.

The final Session can also satisfy the final Unit when the source/configuration and all required checks match. Additional targeted diagnostic checks and any required measurements must still pass. Prepare implementation, tests and support documentation before that run; never edit sources while verification is running. NativeAOT is not part of this plan.

After a successful formal run and commit, use `scripts/verify-commit.ps1` to associate the evidence, then push without force. Stage only this work's explicit paths; other sessions may be editing Binding files concurrently, so check for overlapping uncommitted work before U2. Reverify changed verification inputs; retain failures and rejected experiments under `artifacts/verify/`, and measurements under `artifacts/benchmarks/`.

Revising this proposal is documentation-only: review its diff, links and referenced symbols/classes. Do not run builds, tests, native fixtures or milestones for the proposal itself.

### 6.1. Regression risks

- U2 changes facts consumed by effect summaries (`EffectBounds` records `Ref` access for the base and `Uniq` for writes), `preserves` premises, Hover adaptation display and generic/Semantics instantiation.
- U3 and U4 touch the ownership projection used by frequent forms such as `accounts[0].balance`; value-path IR, allocation and timing must stay unchanged unless measured and justified.
- Existing rejection tests may change diagnostic codes, especially partial Move combined with runtime indices.
- U1's fail-closed branch may reject an accepted form not covered by §2.1; treat every such case as a classification gap to trace, not as a reason to reopen the fallback.

## 7. Documentation and issue bookkeeping

- Update `docs/dev/PLAN.md` in U1: record the confirmed wrong-code cases under G59 and its schedule. Close G59 only after its original borrowing and misleading-conflict symptoms and the same-cause wrong-code and false-rejection cases are resolved with evidence; otherwise retain an explicit open owner.
- Update `docs/STATUS.md` only when verified support changes. U1 records the safer explicit rejection and the remaining shared Copy snapshot deviation; that is not borrowing support. Each feature unit describes its accepted path/Type boundaries, and U5 removes the deviation.
- Update `docs/dev/CODEMAP.md` in the implementation commit when entry points or phase responsibilities change, including the classifier in the "Copy, Move, borrows and element paths" and "Arrays" rows. Keep it navigation-only.
- Record a few lines of session outcome and evidence pointers in `docs/dev/PLAN_HISTORY.md`; detailed traces and results belong in commits and artifacts.
- Change `docs/dev/DIAGNOSTICS.md` only if a verified existing finding or the internal model contract changes. No automatic full diagnostic review is requested.
- Keep SPEC, STYLE and LIBRARY unchanged unless the actual work establishes a corresponding rule, convention or public declaration change; record in the implementation commits that SPEC needed no change. This plan requires none. Do not weaken a formal rule to accommodate implementation difficulty.
- No specification intake occurs when saving this plan, so `draft/INTEGRATED.md` needs no intake entry now. Any later deliberate spec revision must follow AGENTS.md, including affected examples/tests/milestones and same-commit intake registration. Proposal closure and freezing follow the register's disposition rules; this document remains open.

## 8. Completion criteria and decision gates

The plan is complete only when:

1. The three original G59 spellings and the confirmed same-cause Copy, exclusive, address and false-rejection cases have correct end-to-end behavior, with native O0/O2 results where executable.
2. Place borrows preserve target identity and retained dependencies independently of Copy; no Place selection reaches the value-snapshot route; ordinary value reads retain their required Copy behavior.
3. Shared/exclusive authority, stored-reference adaptation, evaluation/cleanup, static Move eligibility and overlap restrictions have passing paired regressions.
4. Failed acquisitions create no successful acquisition facts, their dependent diagnostics follow real prerequisites, and independent errors remain visible.
5. The applicable composition inventory is accounted for, checked-plan rejection and reuse tests pass, and relevant measurements justify any performance-sensitive routing changes.
6. Required Unit/Session, selected native fixtures, milestones and source/commit evidence are complete, with support documentation matching the verified boundary.

Revisit the implementation approach at these gates:

| Finding | Required response |
| --- | --- |
| A reported symptom fails to reproduce on the identified baseline | Correct the evidence inventory and investigate the differing source/configuration; do not claim an unobserved fix. |
| U1 rejects accepted forms beyond the observable set | Keep U1 to exclusive, address-producing and Non-Copy uses; trace the form as a missing route. |
| Borrow-forming consumers cannot share the `BorrowStruct` projection route | Give them one common entry. Record a Binding base adaptation for owned Arrays, moving their value operations to the receiver route, only if Binding-phase facts require it, and only with measured IR, allocation and timing parity. |
| A classification change alters working value paths | Keep those paths and share only the facts/address preparation needed for correctness. Do not make global routing uniformity a completion requirement. |
| Existing projection borrowing cannot retain the required evidence | Add a bounded shared preparation representation, documenting the gap; never loosen Loan or lowering validation. |
| Capability turns out to depend on Binding-time use, such as overload selection | Stop and redesign. Preserve committed declaration selection; raise a specification question if SPEC is unclear. |
| A synthesized access type affects overload choice, effect bounds or generic case selection | Preserve committed declaration selection and complete contracts; trace and repair the phase boundary before enabling the affected cases. This does not by itself justify a spec change. |
| Early failure handling loses independent checks or earlier valid effects | Refine typed recovery and prerequisite propagation; do not broaden suppression or return sentinels indiscriminately. |
| A genuinely separate G75 or other prerequisite is reached | Record its exact reproducer, dependency and owner. Implement the necessary bounded dependency or retain an explicit incomplete status; do not silently expand this into a general ownership rewrite. |
| A real specification contradiction is established, for example over non-overlap of distinct Fields below one runtime-selected element | Document the contradiction and a Principles-preserving revision, then update formal sources and affected evidence together under AGENTS.md. A missing implementation alone is not a contradiction. |
| Work stops after the safety unit or before native/toolchain verification | Report the verified intermediate boundary and outstanding checks. G59 and this plan remain open as applicable; U1 alone may ship. |
