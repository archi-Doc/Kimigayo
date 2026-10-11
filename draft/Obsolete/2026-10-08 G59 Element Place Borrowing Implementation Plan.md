# G59: Element Place Borrowing Implementation Plan

Date: 2026-10-08 (third revision). Status: open implementation proposal; implementation and completion verification are pending.

Planning baseline: the compiler sources of `35bbc6ba`, unchanged when this revision was written; later commits changed documentation only. Re-establish the baseline before implementation if the checkout changes.

Two investigations informed this plan: one supplied the symptom inventory, three root causes and the classification design; the other supplied the recovery discipline and bounded address reuse. They are inputs, not specification authority or instructions to execute. This document is self-contained.

## 1. Summary

- **Problem.** Selecting a part of a runtime-selected element, or of a collection reached through a reference, has three wrong outcomes: accepted programs that update or address a temporary copy (native wrong code), `UnsupportedOwnership_Kd` preceded by fabricated conflicts (G59 itself), and false Language errors on valid paths (§3.1).
- **Causes.** A borrow falls back to acquiring the Place as a value (R1); element bases are classified by several divergent, Type-driven predicates (R2); a synthesized shared access reference acts as a path layer (R3) (§3.3).
- **Approach.** One Place-route classification with a fail-closed default (D2), capability derived from the use (D3), routes chosen by the root while working value paths stay (D4), failures that name the missing route and invent no state (D5), and a recorded route that emission validates (D7). Spellings that SPEC makes equivalent are tested against each other (§7.2).
- **Order.** U1 stops the wrong code and can ship alone; it is the next independent repair in PLAN. U2–U5 add the missing routes; U6–U7 complete composition, reuse and evidence (§6).

## 2. Scope

**Objective.** Borrow the storage selected by an element or member Place without first acquiring its value. Apply that rule to Copy and Non-Copy parts, to shared, exclusive and address-producing uses, and to the relevant owned and borrowed paths. Preserve the complete stored Type, access authority, evaluation order and every dependency needed to keep the selected storage valid.

**Priorities.**

1. Stop accepted programs from updating or addressing a temporary copy instead of their selected Place. PLAN requires unsupported forms to fail with a diagnostic, never with wrong code.
2. Support G59 and the related Place-selection failures through common classification and capability rules.
3. Prevent failed acquisitions from creating successful acquisition facts or fabricated ownership conflicts, while retaining independent diagnostics.

**Included.** Dynamic Array elements, fixed-array elements with runtime indices, their stored Field/Tuple projections, collections stored in Fields and reached through owned paths or safe references, and element members reached through `ref`/`uniq` arrays; explicit borrows, `@raw`, implicit shared arguments, borrowing receivers and the inspection/collection consumers that use the same paths. Slice, UniqSlice, Dictionary, user Indexable and published Place-result paths are compatibility checks, repaired here only when the same changed mechanism is responsible.

**Not included.** No new language feature, second Loan engine, general pointer/projection rewrite or library API. Object-handle adaptation, getter semantics and unrelated G75 boundaries keep their own contracts and owners. A dependency that prevents completion is identified and tracked; it is never hidden by narrowing required behavior. Model convergence beyond G59 is listed in §10 below. This proposal does not start implementation, close G59 or establish support.

## 3. Problem analysis

### 3.1. Baseline observations

Native probes used the Release compiler `0.1.1 (6416b537-90b5-40dd-8973-92473ab05ea8)` at O0 for `x86_64-pc-windows-msvc`. The DLL was built from the working tree shortly before the `35bbc6ba` commit and is not a verified-commit build; U0 repeats the probes on the identified source. Sources, diagnostics, outputs and IR are retained locally in `artifacts/verify/20261008-g59-baseline-probes/` (Git-ignored).

In the shapes below, `Item` has `name: string` and `n: i32`, `bump(target: uniq/i32)` increments its referent, `xs: Array<Item>` is a mutable owned local, `ys: [2 of i32]` is a mutable owned fixed array and `i` is a runtime `isize`.

| Case | Shape | Observed | SPEC result |
| --- | --- | --- | --- |
| `G59Call` | `Console.writeLine(xs[0].name)` | `UnsupportedOwnership_Kd`, preceded by `ComparisonLoanConflict_Kd` at the enclosing call | Accept; shared Field borrow |
| `G59Ref` | `let name = xs[0].name@ref` | Same pair of diagnostics | Accept |
| `G59Interpolation` | `Console.writeLine("\(xs[0].name)")` | `UnsupportedOwnership_Kd`, preceded by `CallActivationConflict_Kd` | Accept |
| `CopyUniqDynamic` | `bump(xs[0].n@uniq)` with `xs[0].n == 41` | **Accepted; prints 41** | The element becomes 42 |
| `CopyUniqFixedRuntime` | `bump(ys[i]@uniq)` with `ys[0] == 41` | **Accepted; prints 41** | The element becomes 42 |
| `RawCopyDynamic` | `unsafe` `let p = xs[0].n@raw; *p = 42` | **Accepted; prints 41**; the same code on a local struct Field prints 42 | `P@raw` is the address of `P` |
| `SharedCopyRetained` | `let r = xs[0].n@ref; xs[0].n = 5; use r` | `ComparisonLoanConflict_Kd` (correct rejection) | Reject |
| `SharedCopyArgument` | `show(xs[0].n)` with `show(v: ref/i32)` | Accepted; correct output | Accept as a new shared borrow of the Place |
| `UniqArrayMemberWrite` | `xs[0].n = 5` with `xs: uniq/Array<Item>` | `SharedPathAccess_Kd` (false rejection) | Accept |
| `RefFieldArrayRead` | `b.items[0].n` with `b: ref/Bank`, `items: Array<Item>` | Two `TransferRequired_Kd` (false rejection) | Accept as a Copy read |
| Control | `xs[0].n = 5` on the owned `xs` | Accepted; prints 5 | Accept |

Code reading shows that `SharedCopyRetained` and `SharedCopyArgument` borrow a materialized snapshot rather than the element Place. The retained root protection hides that deviation from safe code; `RawCopyDynamic` shows that the same route is observable through an address conversion.

### 3.2. Candidates and unknowns

Reported without native execution, for U0 to reproduce: Copy-struct receiver mutation `xs[0].c.inc()`; Tuple-part mutation; `xs[0].0.inc()` through `uniq/Array<(P, i32)>`; `xs[0].n = 4` through a `uniq` fixed array; `b.items[0].n = 3` through `uniq/Bank` (`InvalidAssignment_Kd`); `Console.writeLine(ys[i].name)` (`UnsupportedOwnership_Kd`); the consumers `xs[0].inner.show()`, `match xs[0].kind`, `for v in xs[0].items` and `xs[0].items.length`/`.append(3)`/`[1]`, with up to six cascading `MovedPlace_Kd`. PLAN G56 also records `b.items.length` through a Non-Copy part as `UnsupportedOwnership_Kd`, misreported as `CallActivationConflict_Kd` inside interpolation.

Working controls: `xs[0].show()`, `xs[0]@ref`, Copy Field reads and writes, `xs[0].name = "x"`, `xs[0].name == "Aki"`, Dictionary members, reads through `ref/Array` and literal-index fixed-array members.

Unknown until U0: the operation producing each enclosing conflict; whether interpolation, `match` and iteration consumers reach `BorrowStruct`; other routes that borrow a snapshot of a Place; UniqSlice element members; generic/Semantics instantiation behavior.

### 3.3. Root causes

**R1. A borrow falls back to acquiring the Place as a value.** When `BorrowStruct` finds no specific route, its final branch evaluates the source with `Expression(source, Read)` and borrows the result. Below a dynamic element this reaches `ElementValue → LocateElement → AcquireElement`. A Copy part is copied into a temporary, so exclusive and address uses lose writes or observe the wrong address. A Non-Copy part reports `Unsupported` after `AcquireElement` has already created a typed temporary and its Produce state; later checks can treat that state as a real acquisition, the likely source of the enclosing conflict that is located at the statement start and published before its cause. SPEC §3.4 states that designator outcomes are alternatives, not fallback stages.

**R2. Element bases are classified by divergent, Type-driven predicates.** `ReceiverElement` uses `IsSharedElement`, which excludes owned Arrays; `CompareInPlace` uses `IsBorrowedReceiver`, which includes them, so element string comparison works while element Field borrowing does not. `IsBorrowedSelection`, `ReachesThroughBorrow` and `IsExclusiveArrayElement` answer neighboring questions differently. None classifies by the actual selector kinds and access path, so an owned `Array` Field reached through `ref/Bank` is treated as an owned root. Borrow formation, value reads, writes and user arithmetic updates each select a route with their own dispatch; only borrow formation falls back to a value acquisition.

**R3. A synthesized access reference acts as a path layer.** `SharedElement` records a `ref/T` adaptation for a Non-Copy element base. `AccessType` returns it and `PathAuthority` counts it as a shared layer, so an exclusive path such as `uniq/Array<T>` loses Write. SPEC §3.4.1 instead selects an index's capability from the final acquisition of its use. User Indexable already implements that pattern: Binding binds `indexUniq` when the real path permits it, `AccessType(node, exclusive)` promotes the shared receiver, and `PathAuthority` treats the user-index root as representation rather than as a layer.

## 4. Governing contracts

### 4.1. Specification

| Contract | Required behavior |
| --- | --- |
| [SPEC §3.4 and §3.4.1](../../docs/spec/03-types-and-values.md#34-values-places-and-storage) | A Place carries its complete Type, path capabilities and dependencies; designator outcomes are alternatives, never fallback stages; the lending point of an owned Place follows standard Field, Tuple and element projections; `@ref`/`@uniq` borrow the written slot; an index selects its capability from the final acquisition; update requirements propagate along the selected path only. |
| [SPEC §4.6.1](../../docs/spec/04-arrays-indexing-and-slices.md#461-access-and-length-metadata), §4.6.4 and [§4.6.9](../../docs/spec/04-arrays-indexing-and-slices.md#469-indexable-contracts) | Locate storage without intermediate Copy/Move; use the capability required by the final acquisition; evaluate and protect receivers and indices in the specified order. |
| [SPEC §5.4](../../docs/spec/05-raw-pointers-and-unsafe-memory.md#54-addresses-and-pointer-conversions) | `P@raw` is the address of the written slot `P`, after the checks of an immediately ending `@ref`; only a Temporary Value is materialized. |
| [SPEC §10.2](../../docs/spec/10-overload-resolution-and-inference.md#102-common-adaptation-at-expected-types) and [§13.5.5](../../docs/spec/13-operators-and-assignment.md#1355-follow-borrow-and-reborrow) | A readable Place at `ref/U` is a new shared borrow; only an owner temporary is materialized. An explicit borrow of a written slot differs from adaptation/Reborrow of a stored reference. |
| [SPEC §15.6.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), §15.6.3 and [§15.6.7](../../docs/spec/15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations) | Overlap, ancestry, owner protection and reservation/activation rules; runtime indices prove no non-overlap. |
| [SPEC §23.3.6.4](../../docs/spec/23-compiler-services.md#23364-problems-prerequisites-and-suppression) | Report directly established problems, identify actual missing prerequisites and retain independent failures. |

No specification change is justified: the observed behavior violates consistent existing rules.

### 4.2. Project rules and settled decisions

- [PLAN](../../docs/dev/PLAN.md) §5 condition 4: unsupported forms fail with a diagnostic, never with wrong code.
- [DIAGNOSTICS](../../docs/dev/DIAGNOSTICS.md) rule 3: an unsupported form uses an `Unsupported` code with the missing feature in the Reason. This plan is ordinary diagnostic implementation, not the full diagnostic-development workflow.
- [SETTLED](../../docs/SETTLED.md), the accessor-receiver decision: a computed/custom accessor is never replaced by a direct stored-Field operation.
- [CODEMAP](../../docs/dev/CODEMAP.md) supplies navigation and [STATUS](../../docs/STATUS.md) the verified support boundary; neither overrides SPEC.

### 4.3. Implementation entry points

| Responsibility | Existing entry points |
| --- | --- |
| Selection and capability | [ElementAccess.cs](../../src/Kimi/Compiler/ElementAccess.cs): `AccessType`, `BorrowedPathRoot`, `OwnedPathRoot`, `IsSharedElement`, `IsBorrowedReceiver`, `IsExclusiveArrayElement`, `ReachesThroughBorrow`; [Binding.ArgumentOperations.cs](../../src/Kimi/Compiler/Binding/Binding.ArgumentOperations.cs): `ReceiverElement`, `SharedElement`, `CompareInPlace`, `PathAuthority`; [Binding.Expressions.cs](../../src/Kimi/Compiler/Binding/Binding.Expressions.cs): `Writable` |
| Use-driven precedent | [Binding.Indexers.cs](../../src/Kimi/Compiler/Binding/Binding.Indexers.cs): exclusive entry binding; `AccessType(node, exclusive)` promotion; the user-index case of `PathAuthority` |
| Borrow formation | [OwnershipAnalysis.Borrows.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Borrows.cs): `BorrowStruct` and its final branch, `Receiver`, `BorrowFieldAddress`, `ReadBorrowedField`, `WriteBorrowedField`; [OwnershipAnalysis.Elements.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Elements.cs): `LocateElement`, `BorrowElementAddress`, `AcquireElement`, `ElementValue`, `BorrowStringElement` |
| Route-dispatch precedent | [OwnershipAnalysis.Arithmetic.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Arithmetic.cs): the user-arithmetic update target dispatch selects one route per Place form, locates owned projections with `LocateElement`/`BorrowElementAddress` and reports every other form as `Unsupported` |
| Consumers and call preparation | [OwnershipAnalysis.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.cs): `Expression` member/element dispatch, address and borrow conversions, `Call`, `Unsupported`; [OwnershipAnalysis.Sequences.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Sequences.cs): `SequenceReceiver`; [OwnershipAnalysis.Reservations.cs](../../src/Kimi/Compiler/Analysis/OwnershipAnalysis.Reservations.cs): `PrepareCallArgument` |
| Diagnostics | [OwnershipModel.cs](../../src/Kimi/Compiler/Analysis/OwnershipModel.cs): `OwnershipIssue`; [DiagnosticCode.tinyhand](../../src/Kimi/Diagnostics/DiagnosticCode.tinyhand): `UnsupportedOwnership_Kd`, whose only evidence is `case:Text` |
| Loan evidence and code generation | [OwnershipBody.Borrows.cs](../../src/Kimi/Compiler/Analysis/OwnershipBody.Borrows.cs); [BodyLowering.Elements.cs](../../src/Kimi/Compiler/Emission/BodyLowering.Elements.cs); [BodyLowering.StructBorrows.cs](../../src/Kimi/Compiler/Emission/BodyLowering.StructBorrows.cs): `LowerStructBorrow`, `TryBorrowedPathOffset` |

### 4.4. Existing coverage

`DynamicArraySharedReadTest` covers `(values@uniq)[0].id` reads and `values[0].id += 1`. `ElementComparisonTest`, `PublishedPlaceProjectionTest`, `ElementReplacementEmissionTest`, `StaticElementUpdateTest`, `ElementBorrowEmissionTest` (including corrupted borrow plans), `PathCapabilityDiagnosticTest` and `UserArithmeticUpdateTest` cover neighboring routes. No test covers method calls on built-in Array element members (`xs[0].inner.m()`), `SharedPathAccess_Kd` for element member writes, borrows of Copy parts below dynamic elements, or `@raw` of such parts, and no test expects element-member Unsupported. `ElementBorrowEmissionTest.UnsupportedReceiversAndPathsRemainExplicit` contains `same(a[i], a[0])`, which may become accepted under SPEC in U4.

## 5. Design

### D1. Place identity is independent of Copy

An explicit borrow, a borrowing receiver, an address conversion and a fixed `ref/U` adaptation of a readable Place all target that Place. Copy capability decides whether a value acquisition can Copy, not which location a borrow targets. Only an owner temporary is materialized for a borrow (SPEC §10.2).

Classify the resolved operation, not just the syntax: getters, raw Places, published Place calls, stored references and object handles keep their own routes, and parentheses do not change the classification. The stored `T` of `xs[i]` is never rewritten into `ref/T`; its complete Type, including internal Origins and generic structure, stays separate from the operation used to access it.

### D2. One Place-route classification with a fail-closed default

Binding computes the facts of each element or member base once:

- root kind: owned local/parameter/capture/static, owner temporary, safe reference, Slice, published Place, raw Place, or getter/accessor boundary;
- selector kind at each level: static (Field, Tuple position, in-range literal fixed-array index) or runtime;
- real path authority (Owner, Uniq or Ref), computed from real layers only;
- the complete element Type and the Binding `PlaceOrigin` used for any synthesized access reference.

The facts live in existing side tables or interned forms, with no growth in warm rebind allocation; ownership never creates Origins. Predicates that answer different questions stay distinct: dynamic addressability, static Move-path eligibility, overlap proof and path authority are not interchangeable, and a literal index into dynamic Array storage is never a static Move Path or a disjointness proof.

Borrow formation derives exactly one route from these facts and the use:

| Route | Selected for | Borrow formation |
| --- | --- | --- |
| Value | A value expression: call or operator result, literal, owner temporary, getter or custom accessor result | Evaluate once, materialize when needed and borrow the temporary (SPEC §10.2) |
| Name | A local, parameter, capture or static | Borrow the named storage |
| Raw | `*p`, `p[n]` and their inline parts | Raw address route |
| Published | A Place call or a user `index`/`indexUniq` selection | Reborrow through the returned reference |
| Stored reference | A slot storing a reference or handle that a fixed expected reference Reborrows | Stored-reference or stored-object Reborrow |
| Borrowed path | A Field/Tuple path below a base reached through a safe reference or a synthesized base reference | `Receiver`, then `BorrowFieldAddress` |
| Sequence element | An element selected through a reference, a Slice or an exclusive handle | Sequence element borrow |
| Owned static path | An owned root with static selectors only (`OwnedPathRoot`) | Static-path borrow |
| Owned projection | An owned root with at least one runtime selector | `LocateElement`, then `BorrowElementAddress` (U3, U4) |
| Transitional snapshot | Until U5, a shared `ref` borrow of a Copy part that has no Place route yet | The existing snapshot, audited by D7 and recorded in STATUS (D5) |
| Unsupported | Any other Place selection | Located `Unsupported` before any state is emitted (D5) |

Every existing `BorrowStruct` branch maps to exactly one route, including implicit follow through pair layers and multi-layer reads. The routes form a closed enumeration and the dispatch has no evaluating default: only the Value route may acquire its source as a value, and the transitional snapshot is the one named, audited exception, which U5 removes. A Place form without a route therefore fails closed instead of reaching a snapshot. Later units add routes ahead of Unsupported, never exceptions inside it.

`BorrowStruct` adopts the classification in U1, and U2 rebases the element decisions of `ReceiverElement`, `CompareInPlace`, `PathAuthority`, `Writable` and `IsExclusiveArrayElement` on the same facts. Value reads, writes and user arithmetic updates adopt it only when a unit changes them; the user arithmetic dispatch, which already ends in `Unsupported`, is the template (follow-up F1).

### D3. Capability is derived from the use

A synthesized base reference is a representation, not a source-level layer. `PathAuthority` does not count it and continues to the selection, as it already does for a user-index root. `AccessType(node, exclusive)` promotes it to `uniq` with the same Origin when the base's real path authority permits exclusive access, as user Indexable already does. A real shared layer, a Slice or an owned `let` root still caps the capability; an immutable binding that stores a `uniq` reference is not immutable referent storage.

Use-time capability selection respects committed overload/member selection and never selects a different declaration after an acquisition or Loan failure. Stored-reference and object-handle adaptation stay distinct.

### D4. Routes follow the root; working value paths stay

An owned root, with any mix of static and runtime selectors, uses the ownership projection: `LocateElement` locates the Place once and `BorrowElementAddress` borrows it in the mode the use requires, as user arithmetic updates already do, with the static Field/Tuple suffix inside the projection. A reference root uses the receiver route.

Synthesized adaptations are not recorded for owned bases merely to reach the receiver route. Value reads, assignments and updates of owned bases stay in `ElementValue`, `AssignElement` and `UpdateElement`; ordinary Copy reads never become lasting borrows, copy intermediate arrays or widen an existing static-path Loan. The existing comparison adaptation for owned Arrays stays a working route that consumes the classification facts.

Ownership analysis, reservation handling, retained dependencies and lowering validation are completed together before a newly supported form is accepted; an address that works only during its construction is insufficient for a stored or returned borrow. If existing plans cannot carry the needed facts, add the smallest shared preparation result that does, never an independent lifetime engine or container-specific escape hatch.

### D5. Failures name the missing route and invent no state

The Unsupported route reports before any acquisition, Produce, projection or access Loan is emitted for the selection. During the transition it applies to every use that can observe a snapshot: exclusive borrows and receivers, Non-Copy parts, and address-producing conversions (`@raw` and address adaptations). A shared `ref` borrow of a Copy part takes the transitional snapshot route until its Place route exists (U3–U5), because the retained root protection hides the deviation from safe code; D7 audits it and STATUS records it until U5 removes it, including unsafe address observation through such a `ref`.

Following DIAGNOSTICS rule 3, the record names the missing route in its Reason, for example "a borrow below a runtime-selected element", with the selector or path segment that chose the route as its location or related location. `UnsupportedOwnership_Kd` currently has only `case:Text` evidence; add one bounded feature fact for these records and reuse it for every fail-closed route. Add no repair candidate and no new diagnostic code unless the catalog requires one.

An unsupported or failed acquisition never invents a Move, initialized result, usable borrow or executable call input. Preserve the effects of receivers, indices and earlier arguments already checked, and close temporary access protection on every exit. Independent nested expressions inside a rejected selection may still be analyzed for their own diagnostics, provided no result is consumed as a successful acquisition; U0 decides this per traced route.

Retain typed recovery where later independent checks need it; a recovered Type is not proof of a successful acquisition. Do not replace all `AcquireElement` recovery with `return -1`, and do not assume every caller treats `-1` as the required recovery state. When a dependent call requirement is undecidable, use the existing prerequisite model. Keep independently established conflicts; never suppress every error in a parent call or reorder diagnostics to hide an unchanged cause. A single-error expectation applies only to an isolated reproducer with one independently established problem.

### D6. Evaluation, lifetime and proof boundaries

- Evaluate each receiver and selector once in the specified order; assignment/update rules still secure the RHS at the required point (`values[key()].0 += rhs()`, `make()[index()].id`). Bound checks precede address use. Reservation protection never turns into premature exclusive activation.
- Borrow formation performs no element/Field Copy, Move, destruction or added runtime allocation. Cleanup stays exactly once on success, bounds failure and abandoned argument paths, and a replaced value is destroyed once.
- Owner protection and internal/stored-reference Loans needed by the selected target are retained through local storage, argument forwarding and permitted returns, and released after their actual last required use, not when an intermediate reference temporary ends.
- A borrow of a reference slot differs from a Reborrow of its referent. Parent/child Loan ancestry and dependencies carried by the complete stored Type are preserved.
- Target footprints stay separate from owner protection. A runtime-selected base protects its whole root while borrowed; runtime indices supply no new non-overlap proof, and structural non-overlap required by SPEC is not erased.
- `@move` of an element without a static Move Path stays `StaticMovePathRequired_Kd`; a bare Non-Copy read stays one `TransferRequired_Kd`.

### D7. Emission validates the recorded route

Each Borrow address operation records its route in existing operation storage, or in a side table keyed by operation, without per-operation allocation. Lowering checks that the recorded route agrees with the classification of the operation's source and that the route's evidence is present: a Value-route borrow takes a Temporary produced for that value; a Place route takes the named root, projection, receiver reference or published reference, never a Temporary produced by reading the same Place selection. A mismatch fails emission like other corrupted plans, and a fresh analysis recovers.

From U1 the check is an emission failure for exclusive, address-producing and Non-Copy borrows. Transitional snapshot borrows are counted by an audit hook (an internal count that tests can assert, with no diagnostic and no allocation) until U5 makes the check unconditional. U0 and U1 run the audit across the full functional suite and milestone harnesses, so other routes that borrow a snapshot are found from evidence rather than inferred from syntax.

### 5.1. Rejected alternatives

| Alternative | Reason not adopted |
| --- | --- |
| Suppress or reorder the secondary diagnostics only | Wrong code and the unsupported forms remain. |
| Loosen `OwnedPathRoot` to admit runtime selectors | Grants static Move, initialization and non-overlap facts that runtime selectors do not establish. |
| Classify element bases only in ownership | Every consumer that reads `BorrowedPathRoot`/`AccessType` needs parallel handling, and ownership would have to create Origins. |
| Add a retained projection-borrow model beside the Loan engine | Duplicates the Loan engine. |
| Record a base adaptation for owned Arrays and move all their value operations to the receiver route | Changes frequent value paths (`accounts[0].balance` in Programs 29/31/37/40), IR and allocation without a correctness need; kept as the gated option of §9. |
| Rewrite `xs[i]` to `ref/T` | Loses the complete stored Type and conflates access with Type. |
| Reject every snapshot borrow, including shared `ref` of Copy parts, in U1 | Rejects correct, accepted programs such as `show(xs[0].n)` before their route exists. |
| Fail closed only for exclusive and Non-Copy uses in U1 | Leaves `RawCopyDynamic` accepted as wrong code. |

### 5.2. Kimigayo Principles

| Principle | How this plan applies it |
| --- | --- |
| One Concept, One Canonical Form | One meaning for a Place borrow (D1); one route classification for every borrow (D2); spellings that SPEC makes equivalent are tested against each other (§7.2). |
| Local Reasoning | Capability follows the written path and the use (D3); the route follows from locally computed facts, not from a fallback chain (D2). |
| Explicit Semantics | No hidden Copy in place of a borrow (D1, D2); unsupported forms fail closed and name the missing route (D5). |
| Compiler Server Protocol | Diagnostics carry the cause and the selector (D5); the recorded route is checked before output (D7); evidence is tied to an identified source and configuration (§6, §7). |

## 6. Implementation units

Each completed unit contains its reproducer, implementation and focused regressions, and passes its required verification before it is committed and pushed. U0 supplies evidence and tests to later units; never commit a standalone red test suite. Keep units small enough to verify without changing sources during a build.

| Unit | Purpose | Resolves | Requires |
| --- | --- | --- | --- |
| U0 | Baseline, traces, audit sweep, test harness | Source-identified inventory | — |
| U1 | Route classification for `BorrowStruct`, fail-closed default, route validation, Unsupported Reason, recovery | Accepted wrong code; fabricated conflicts | U0 |
| U2 | Use-derived capability for borrowed receivers | `UniqArrayMemberWrite` and other false capability failures | U1 |
| U3 | Owned dynamic Array projection route | G59 forms, `CopyUniqDynamic`, `RawCopyDynamic`, element consumers | U1; U2 where traced |
| U4 | Runtime fixed-array selectors | `CopyUniqFixedRuntime`, `ys[i].name` | U3 |
| U5 | Paths through references; snapshot route removed | `RefFieldArrayRead`, the shared Copy deviation | U2–U4 |
| U6 | Composition and neighboring contracts | Remaining inventory | U5 |
| U7 | Reuse, measurements and completion | Completion evidence | U6 |

U1 is shippable alone and is the next independent repair in PLAN, because it stops accepted wrong code.

### U0. Baseline and operation-level causes

**Work:** Record the commit, working-tree input identity, compiler configuration and toolchain identity, and build from that source under the normal feedback/verification workflow; the baseline DLL of §3.1 is not that build. If isolation is necessary, use a suitable worktree and preserve unrelated changes. Keep disposable reproducers under `temp/` and baseline evidence, including failures and IR, under `artifacts/verify/`.

Create the proposed `ElementMemberPlaceTest` class with the stable case names of §3.1 and §7.1, and the paired-source helper of §7.2. Choose representatives along three axes rather than the full product: receiver (owned `var`/`let` Array, fixed array with a runtime index, Array Field of a local struct, `ref`/`uniq` Array, `uniq` fixed array, Array Field through a `ref`/`uniq` struct; Slice, Dictionary and a temporary Array as controls), part (string, `i32`, Copy struct with a `uniq` method, Tuple element, nested Array, enum Subject), and use (bare read, retained `@ref`, implicit `ref` argument, interpolation, `@uniq` argument, `uniq` receiver, `@raw`, assignment and compound assignment, `@move`, `append` while borrowed). Wrong-code cases become native fixtures with expected output.

For each failing path, map the selected Place, adaptation, ownership operations, acquisition state, Loan holder and call reservation. Trace the operation producing each enclosing conflict, whether the `AcquireElement` Produce or an unclosed access Loan, with environment-gated trace lines removed before commit. Confirm which consumers reach `BorrowStruct`. Run the D7 audit as uncommitted instrumentation across the full functional suite and milestone harnesses, and add every hit to the inventory.

**Exit:** A source-identified inventory distinguishes native execution, IR-only evidence, expected SPEC behavior and untested intersections. A reported symptom that no longer reproduces stays a regression candidate, and the inventory is corrected instead of forcing the old diagnosis.

### U1. Fail closed and validate the route

**Targets:** `BorrowStruct`, `Receiver`, the address conversions that call `BorrowStruct`, `LowerStructBorrow`, the `UnsupportedOwnership_Kd` evidence and the narrow recovery path of the traced callers.

**Work:**

1. Introduce the route classification of D2 and map every existing `BorrowStruct` branch to one route; the final branch splits into the Value route, the transitional snapshot route and Unsupported.
2. Report a Place selection without a route as Unsupported before emitting state, for exclusive, address-producing and Non-Copy uses, with the Reason and location of D5. Shared `ref` borrows of Copy parts take the transitional snapshot route.
3. Record routes on Borrow operations and add the D7 validation: an emission failure for the fail-closed set and the audit count for the transitional snapshot route.
4. Apply the D5 recovery rules to the traced callers so that G59's enclosing conflicts no longer arise from fabricated acquisitions, while independent errors remain.

**Verification:** `CopyUniqDynamic`, `CopyUniqFixedRuntime`, `RawCopyDynamic`, Copy-struct receiver mutation and Tuple-part mutation each produce one located `UnsupportedOwnership_Kd` with its Reason and no IR. The G59 forms produce Unsupported without the fabricated conflict; check the JSON diagnostics. `SharedCopyArgument`, `SharedCopyRetained` and every control are unchanged. Combine an unsupported borrow with an unrelated uninitialized use, a real conflict, a side-effecting selector, and a failed later argument after earlier successful preparation. A corrupted route record fails emission and a fresh analysis recovers. The Session-wide audit reports no fail-closed hit outside the inventory, and warm classification allocates nothing.

**Exit:** No identified wrong-address form is accepted; supported forms and accepted shared Copy reads are unchanged; recovery neither manufactures conflicts nor hides independent ones. U1 alone does not close G59.

### U2. Use-derived capability for borrowed receivers

**Targets:** The classification facts in Binding, `AccessType` promotion, `ReceiverElement`, `SharedElement`, `CompareInPlace`, `PathAuthority`, `Writable`, `IsExclusiveArrayElement` and the consumers of these adaptations.

**Work:** Apply D3 to receivers already on the receiver route: `ref`/`uniq` Arrays, `uniq` fixed arrays, Slices and Dictionary references. Rebase the listed element decisions on the D2 facts. Preserve user Indexable selection and declaration identity. Enable a classification only together with a correct execution path; when that requires U3 or U4, activate it with that unit.

**Verification:** `UniqArrayMemberWrite`, `xs[0].inner.rename()` and `xs[0].0.inc()` through `uniq/Array`, and `xs[0].n = 4` through a `uniq` fixed array are accepted with native O0/O2 results. Writes through a Slice, `ref/Array` or an owned `let` root keep one `SharedPathAccess_Kd` or `InvalidAssignment_Kd`. An immutable binding of a `uniq` reference still writes its referent. Comparison and ordinary borrow agree. Inspect effect summaries, Hover adaptation display and generic adaptation wherever changed facts are consumed.

**Exit:** Synthesized shared access no longer causes false permission failures, real shared layers still cap access, and no newly accepted operation relies on the Value route for a Place.

### U3. Owned dynamic Array element Places

**Targets:** The Owned projection route of `BorrowStruct`, `LocateElement`/`BorrowElementAddress`, retained Loan propagation, call reservations and checked lowering.

**Work:** Implement D4 for owned dynamic Arrays, including temporary Arrays and Arrays inside owned inline paths, and their Field/Tuple parts. Support immediate and retained borrows, address conversions, borrowing receivers and the consumers traced in U0. Keep the owner and its internal dependencies alive through forwarding and permitted borrowed results, without broadening a retained borrow from the selected target to an unrelated aggregate. Shared Copy borrows of these Places move from the snapshot to this route.

**Verification:** All G59 forms; `CopyUniqDynamic` and `RawCopyDynamic` printing 42; Copy-struct receivers; `SharedCopyArgument` and retained shared Copy borrows addressing the element; the consumer forms of §3.2; equivalence families E1, E2 and E5. Native O0/O2 check output, selector counts, bounds failures and cleanup. While a borrow is live, `clear`, reallocation, overlapping replacement, `append` and owner Move are rejected with one conflict naming the retained value; valid operations after its last use are accepted. Value reads and updates of owned bases keep their IR and allocation behavior.

**Exit:** Owned dynamic element Places are borrowed in place with correct retained dependencies.

### U4. Runtime fixed-array selectors

**Targets:** The classification (a non-literal fixed-array key is a runtime selector) and the Owned projection route for owned fixed arrays.

**Work:** Support exclusive, shared and address borrows of runtime-selected fixed-array elements and their parts through the same route, without granting static Move eligibility; literal-index static paths keep their precision.

**Verification:** `CopyUniqFixedRuntime` prints 42; `Console.writeLine(ys[i].name)` is accepted; a runtime index into a partially moved fixed array is still rejected; equivalence family E3; `same(a[i], a[0])` changes only as SPEC requires.

**Exit:** Runtime fixed-array selection reaches its Place without new Move or overlap facts.

### U5. Paths through references; removal of the snapshot route

**Targets:** The classification (an element whose path passes a reference is a borrowed base, whatever its stored Type), `IsExclusiveArrayElement`, the element decision of `Writable`, the fail-closed set and the D7 check.

**Work:** Make element decisions path-based, so `b.items[0].n` through `ref/Bank` is a Copy read and `b.items[0].n = 3` through `uniq/Bank` is a write. Then extend the fail-closed set to shared Copy borrows and make the D7 check unconditional: no Place selection reaches a value snapshot. Check `holder.items.insert(holder.items.length, 4)` from DIAGNOSTICS §11 only if its trace reaches this mechanism; resemblance alone does not fix it.

**Verification:** `RefFieldArrayRead` and its `uniq/Bank` write are accepted with native results; equivalence family E4; the audit count is zero across the Session and the hook is replaced by the unconditional check; the STATUS deviation is removed with a test showing that the borrow addresses the element.

**Exit:** Every Place borrow uses a Place route; the Value route is unreachable for Place selections.

### U6. Composition and neighboring contracts

**Targets:** The shared mechanisms and consumers identified by U0; no syntax-specific borrowing exceptions.

**Work:** Cover nested Field/Tuple/Array projections, generic Fields and supported Semantics cases (including `Array<s/T>`), stored references and dependency-carrying Fields, method receivers, interpolation, `match` inspection, borrowing iteration and nested collection operations under their acquisition contracts, through the §7.1 matrix and every applicable §7.2 family. Confirm Slice/UniqSlice, Dictionary, user Indexable, published Place results, temporary receivers, getters and static fixed-array paths as controls; a custom getter remains a call boundary.

**Verification:** Pair acceptance with shared-path rejection, invalid escape, conflicting parent use, lack of dynamic disjointness and partial-Move restrictions. Ordinary value reads stay independent Copy snapshots. Independently borrowed inputs with equal Origins detect accidental Loan-identity merging.

**Exit:** Every applicable §7.1 case has passing evidence or an explicit, separately owned prerequisite. An unresolved same-cause case keeps this plan incomplete; filing a gap alone does not satisfy completion.

### U7. Reuse, measurements and completion

**Work:** Add focused warm Binding/analysis/emission allocation and retained-capacity regressions for the classification, the new routes and the route records. Test edit/reanalysis recovery after successful and failed borrowing. Corrupt each newly relied-upon plan fact, the recorded route included, and require emission to reject it and a fresh analysis to recover. Extend existing test infrastructure rather than reproducing implementation branches in assertions.

If hot-path routing or performance changes, run matched measurements in `src/Benchmark` after a whole-solution Release build, including an existing value read/update control and the new borrow route, with fixed warm-up and measurement conditions; record actual costs, not unmeasured claims.

Prepare the documentation of §8 before the final Session, retain failed and successful evidence, associate successful formal verification with the committed source, and push.

**Exit:** The completion criteria of §9 hold. No second final Session is needed solely because an earlier run was labeled Unit; a Session label does not supply omitted native or milestone coverage.

## 7. Verification design

### 7.1. Required behavior matrix

The examples are expression-level test shapes, not complete programs. Use small declared Types and functions with SPEC-derived expected behavior, and select representatives for distinct paths and interactions rather than the full Cartesian product.

| Case family | Representative shapes | Acceptance/rejection oracle |
| --- | --- | --- |
| Original G59 | `Console.writeLine(xs[0].name)`, `xs[0].name@ref`, interpolation containing `xs[0].name` | Valid string Field borrow; no intermediate owner acquisition; no fabricated conflict. |
| Copy target identity | `bump(xs[0].n@uniq)`, `xs[0].counter.inc()`, Tuple-part mutation, `bump(ys[i]@uniq)` | The selected storage changes; no write to a detached copy. |
| Address identity | `xs[0].n@raw` written through in `unsafe` | The pointer addresses the element. |
| Shared Copy borrowing | Retain `xs[i].n@ref`; `show(xs[i].n)`; contrast with `let n = xs[i].n` | The borrow addresses the selected storage; an ordinary value read remains an independent Copy. |
| Fixed-array selectors | Runtime `ys[i]@uniq` and `ys[i].name@ref`; literal-index controls | Runtime address selection without static Move eligibility; static paths keep their precision. |
| Path capability | Owned mutable/immutable roots, `ref/Array`, `uniq/Array`, `uniq` fixed array, `ref/Bank` and `uniq/Bank` with `items` | Real path authority decides Write; a synthesized shared access never caps an exclusive path. |
| Composition and consumers | Nested fields/tuples/arrays; string and other Non-Copy parts; method, `match`, iteration, `length` and nested indexing | The same selected storage and acquisition contract across the traced consumers. |
| Stored references | Borrow a slot containing `ref/T` or `uniq/T`; adapt its stored reference to an expected borrow | Slot identity, referent identity, complete Type, internal Origins and ancestry stay distinct. |
| Loan retention and ending | Store/forward/return a Field borrow; attempt owner replacement, resize, `append`, Move or destruction | Invalidating actions fail while required; valid post-last-use actions pass; illegal escape fails. |
| Evaluation and failure | Side-effecting receiver/index/RHS; bounds failure; later argument failure; temporary Array receiver | Required order and single evaluation, cleanup and temporary lifetime; no state invented after failure. |
| Recovery and reasons | Unsupported borrow beside an uninitialized read or an independently conflicting Loan | Correct cause, Reason and location; independent diagnostics survive; derived records follow actual prerequisites. |
| Overlap and incomplete storage | Different dynamic indices; disjoint static parts; fixed array after Partial Move; `@move` of a dynamic element | No dynamic disjointness inference; structural proofs and initialization restrictions kept; `StaticMovePathRequired_Kd` kept. |
| Compatibility | Slice, UniqSlice, Dictionary, user Indexable, Place calls, getters, existing object paths | Each published capability and call boundary preserved; no accidental pointer or accessor bypass. |
| Reuse and validation | Repeated analysis/emission, source edits, failed-then-fixed input, corrupted plan or route record | Stable allocation/capacity behavior; stale or invalid evidence never reaches emission. |

For generic and Semantics cases, use currently expressible public contracts. An unrelated existing unsupported feature is a named dependency, not an excuse to remove a required interaction from the inventory.

### 7.2. Equivalence oracle

SPEC gives each Place one meaning, whatever spelling selects it. Tests therefore compare spellings that SPEC makes equivalent; a difference between them is an implementation defect, never an expectation to update.

| Family | Equivalent spellings |
| --- | --- |
| E1 Stepwise selection | `use(xs[i].f@m)` and `let e = xs[i]@b` followed by `use(e.f@m)`, where `b` is the base borrow mode that `m` requires |
| E2 Storage kind | A part below a dynamic element and the same part of a local struct with the same value |
| E3 Selector kind | `ys[i]` with `i == 0` and `ys[0]`, where SPEC grants no static-path difference to the use |
| E4 Path | `b.items[0].n` through `ref/Bank` or `uniq/Bank` and `bank.items[0].n` on the owned `bank`, for the uses the path's authority permits |
| E5 Adaptation | `show(xs[0].n)` at `ref/i32` and `show(xs[0].n@ref)` |

The paired-source helper instantiates both spellings from one template. Every pair compares the acceptance class and, for rejections, the diagnostic code at the corresponding selection; a representative subset also compares native stdout and exit code at O0 and O2. Never pair forms that SPEC distinguishes: static Move eligibility, literal-index disjointness, temporaries versus Places, and getter or accessor boundaries. Each unit adds the families it enables; U6 runs every applicable family.

### 7.3. Test selection by unit

Follow [VERIFICATION](../../docs/dev/VERIFICATION.md). These are starting selections; inspect actual changed callers and fixture producers before running them. `ElementMemberPlaceTest` and `ElementMemberPlace*.ll` are proposed artifacts, not existing coverage.

| Unit | Related existing regression classes and harnesses |
| --- | --- |
| U1 | `DynamicArrayIndexTest`, `CallReservationTest`, `ElementBorrowEmissionTest`, `DynamicArraySharedReadTest`, `StaticElementUpdateTest`, `StoredReferenceLoanTest`, `UserArithmeticUpdateTest` |
| U2 | `PathCapabilityDiagnosticTest`, `MemberThroughLayersTest`, `PublishedPlaceProjectionTest`, `BorrowedArrayWriteTest`, `IndexableContractTest`, `PairFollowTest`, `ElementComparisonTest`, `StoredObjectViewTest`, `EffectBoundImplementationTest` |
| U3 | `DynamicArrayBorrowTest`, `DynamicArraySharedReadTest`, `DynamicArrayElementTest`, `DynamicArrayCleanupTest`, `DynamicArrayCostTest`, `ElementReplacementEmissionTest`, `ElementAssignmentEmissionTest`, `ElementBorrowEmissionTest`, `NestedCollectionTest`, `ArrayMembersTest`, `GenericCompositeValueTest`; milestones 29, 31, 37 and 40 |
| U4 | `ElementMoveEmissionTest`, `FixedArrayCaptureTest`, `StaticElementUpdateTest`, `ElementBorrowEmissionTest`; milestones 17 and 27 |
| U5 | `NestedBorrowedProjectionTest`, `OwnedStoredReferenceTest`, `MemberThroughLayersTest`, `PathCapabilityDiagnosticTest` |
| U6 | `NestedBorrowedProjectionTest`, `PublishedPlaceProjectionTest`, `OwnedStoredReferenceTest`, `DictionaryStoredReferenceTest`, `StoredReferenceLoanTest`, `GenericBorrowedElementTest`, `GenericCompositeValueTest` |
| U7 | Related allocation tests in the selected classes, `DynamicArrayCostTest`, `StoredObjectViewTest`, plus the new path-specific reuse and validation tests |

For illustration, once the proposed class and fixtures exist, a focused U1 command may be:

```powershell
pwsh -Command "./scripts/verify.ps1 -Class ElementMemberPlaceTest,ElementBorrowEmissionTest,DynamicArraySharedReadTest,DynamicArrayIndexTest,StaticElementUpdateTest,UserArithmeticUpdateTest,CallReservationTest,StoredReferenceLoanTest -Fixtures 'ElementMemberPlace*.ll','DynamicArraySharedRead*.ll','ElementBorrow*.ll'"
```

Expand each unit's selection from the table and the recorded dependencies, and retain the effective method/fixture selection and check results with the source/configuration identity.

### 7.4. Workflow

Incremental test-project builds and selected method runs provide feedback only. Each completed code/test unit needs formal Unit verification, including related functional and allocation/reuse regressions and regenerated native fixtures at O0/O2 selected from the actual producers; stale IR from an earlier assembly is never completion evidence.

At session end, run one whole-solution Session covering all functional and allocation/reuse regressions, the final related native fixture set and [milestone harnesses](../../tests/milestones/README.md) 17/27 for fixed-array ownership and 29/31/37/40 for the affected collection paths. Milestone sources stay unchanged unless an independently justified authoring/specification change is required; Program 40 is a regression input, not a workaround target. The final Session can also satisfy the final Unit when the source/configuration and every required check match; targeted diagnostic checks and required measurements must still pass. Never edit sources while verification runs. NativeAOT is not part of this plan.

After a successful formal run and commit, associate the evidence with `scripts/verify-commit.ps1` and push without force. Stage only this work's explicit paths; other sessions may edit Binding files concurrently, so check for overlapping uncommitted work before U2. Reverify changed verification inputs; retain failures and rejected experiments under `artifacts/verify/` and measurements under `artifacts/benchmarks/`.

Revising this proposal is documentation-only: review its diff, links and referenced symbols/classes, without builds, tests, native fixtures or milestones.

### 7.5. Regression risks

- U1 may reject an accepted form missing from §3.1. Treat it as a missing route to trace, never as a reason to reopen the evaluating default.
- Recording routes on operations must not grow operation storage or warm allocation.
- U2 changes facts consumed by effect summaries (`EffectBounds` records `Ref` access for the base and `Uniq` for writes), `preserves` premises, Hover adaptation display and generic/Semantics instantiation.
- U3 and U4 touch the ownership projection used by frequent forms such as `accounts[0].balance`; value-path IR, allocation and timing stay unchanged unless measured and justified.
- Existing rejection tests may change diagnostic codes, especially partial Move combined with runtime indices.

## 8. Documentation and issue bookkeeping

- `docs/dev/PLAN.md`: G59 records the confirmed wrong code and schedules U1 as the next independent repair (done with this revision). Each unit updates confirmed scope and remaining prerequisites. Close G59 only after the original borrowing and misleading-conflict symptoms and the same-cause wrong-code and false-rejection cases are resolved with evidence; otherwise retain an explicit open owner.
- `docs/STATUS.md`: change only with verified support. Its places paragraph already lists the confirmed wrong code, the shared Copy snapshot, the G59 Unsupported forms and the false rejections as limits (2026-10-08). U1 replaces the wrong-code limit with the explicit rejection, which is not borrowing support; each feature unit narrows the limits to its verified path/Type boundaries; U5 removes the snapshot deviation.
- `docs/dev/CODEMAP.md`: update in the implementation commit when entry points or phase responsibilities change, including the route classification in the "Copy, Move, borrows and element paths" and "Arrays" rows. Keep it navigation-only.
- `docs/dev/DIAGNOSTICS.md`: change only if the internal model contract changes, for example if the Unsupported feature fact needs a model rule beyond rule 3. No full diagnostic review is requested.
- `docs/dev/PLAN_HISTORY.md`: a few lines of session outcome and evidence pointers; detailed traces belong in commits and artifacts.
- SPEC, STYLE and LIBRARY stay unchanged unless the work establishes a corresponding rule, convention or public declaration change; implementation commits record that SPEC needed no change. Never weaken a formal rule to accommodate implementation difficulty.
- `draft/INTEGRATED.md` needs no entry, because saving this plan is no specification intake. A later deliberate spec revision follows AGENTS.md, including affected examples/tests/milestones and same-commit intake registration. This document remains open.

## 9. Completion criteria and decision gates

The plan is complete only when:

1. The three original G59 spellings and the confirmed same-cause Copy, exclusive, address and false-rejection cases have correct end-to-end behavior, with native O0/O2 results where executable.
2. Every Place borrow uses a Place route and emission enforces it unconditionally; ordinary value reads keep their required Copy behavior.
3. Shared/exclusive authority, stored-reference adaptation, evaluation/cleanup, static Move eligibility and overlap restrictions have passing paired regressions, and every applicable equivalence family passes.
4. Failed acquisitions create no successful acquisition facts, Unsupported records name their missing route, dependent diagnostics follow real prerequisites, and independent errors remain visible.
5. The applicable composition inventory is accounted for, checked-plan rejection and reuse tests pass, and measurements justify any performance-sensitive routing change.
6. Required Unit/Session, selected native fixtures, milestones and source/commit evidence are complete, with support documentation matching the verified boundary.

Revisit the approach at these gates:

| Finding | Required response |
| --- | --- |
| A reported symptom does not reproduce on the identified baseline | Correct the inventory and investigate the differing source/configuration; never claim an unobserved fix. |
| The audit finds snapshot borrows outside the inventory | Add them to the inventory; fail them closed in U1 when observable, and route them in the owning unit. |
| U1 rejects accepted forms beyond the observable set | Keep U1 to exclusive, address-producing and Non-Copy uses, and trace the form as a missing route. |
| Borrow-forming consumers cannot share the Owned projection route | Give them one common entry. Record a Binding base adaptation for owned Arrays, moving their value operations to the receiver route, only if Binding-phase facts require it and IR, allocation and timing parity is measured. |
| A classification change alters working value paths | Keep those paths and share only the facts and address preparation needed for correctness; global routing uniformity is not a completion requirement. |
| Route records cost operation storage or warm allocation | Derive the route from existing operation fields or a side table keyed by operation; never relax allocation assertions. |
| Existing projection borrowing cannot retain the required evidence | Add a bounded shared preparation representation and document the gap; never loosen Loan or lowering validation. |
| Capability depends on Binding-time use, such as overload selection | Stop and redesign, preserving committed declaration selection; raise a specification question if SPEC is unclear. |
| A synthesized access type affects overload choice, effect bounds or generic case selection | Preserve committed declaration selection and complete contracts; repair the phase boundary before enabling the affected cases. This alone does not justify a spec change. |
| Early failure handling loses independent checks or earlier valid effects | Refine typed recovery and prerequisite propagation; never broaden suppression or return sentinels indiscriminately. |
| A separate G75 or other prerequisite is reached | Record its exact reproducer, dependency and owner; implement the necessary bounded dependency or keep an explicit incomplete status, without expanding into a general ownership rewrite. |
| A real specification contradiction is established, for example over non-overlap of distinct Fields below one runtime-selected element | Document the contradiction and a Principles-preserving revision, then update formal sources and affected evidence together under AGENTS.md. A missing implementation alone is not a contradiction. |
| Work stops after U1 or before native/toolchain verification | Report the verified intermediate boundary and outstanding checks; G59 and this plan stay open as applicable, and U1 may ship alone. |

## 10. Follow-up outside this plan

These items are not completion conditions and are not folded into G59's units.

- **F1. Remaining route dispatchers.** Value reads (`Expression`), the user arithmetic update target, `WriteBorrowedField`, `WriteBorrowedArrayElement`, `AssignElement` and `UpdateElement` adopt the D2 classification and the D7 validation when they are next changed, with the user arithmetic dispatch as the template. Each migration keeps its IR and allocation behavior unless measured.
- **F2. One internal Place model.** After G59, evaluate converging the owned-projection and receiver models, and the owned-Array comparison adaptation, into one model that locates a Place once and acquires it by use, the form SPEC §3.4 describes. Start it as a separate proposal in `draft/Proposals` with matched IR, allocation and timing measurements; adopt it only when it preserves semantics and cost.
