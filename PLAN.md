# Kimigayo Compiler Plan

Current plan only: position, milestone order, completion conditions, next actions and open issues. Required behavior is in [SPEC.md](SPEC.md); implemented support is in [STATUS.md](STATUS.md); a few lines per session are in [PLAN_HISTORY.md](PLAN_HISTORY.md). Items deferred by [Appendix D](spec/appendices/D-deferred-features.md) are not planned here.

## 1. Scope

Implement the finalized language of SPEC.md (Chapters 1–19 and 22) and the implementation contracts of [IMPLEMENTATION.md](IMPLEMENTATION.md) (Chapters 20–21, the test execution profile and Appendix A) for the Windows x64 profile, excluding Appendix D. Progress is driven vertically by the [Milestone Programs](milestones/README.md): each milestone completes one program end to end (Binding, ownership, generation, native execution and required rejections).

- **Generics:** the initial profile monomorphizes (§21.3.1). Generic code sharing is deferred; milestone P22 replaced the existing shared generation path.
- **Origins and the Contract/type system** (§8, §15) remain complete requirements; implementation limits never narrow them.
- **Draft files** (`draft/`) and **NativeAOT tests** need explicit instruction.

## 2. Working rules

| Rule | Detail |
| --- | --- |
| Unit | One coherent change: reproducer, implementation, focused tests. Commit each verified unit with a descriptive message. |
| Unit verification | `./verify.ps1 -Class <test classes> [-Fixtures '<pattern>'] [-Milestone <n>]`: Debug build with warnings as errors, related tests, related native O0/O2 fixtures and harnesses. |
| Session verification | Once at session end: `./verify.ps1 -Mode Session [-Fixtures ...] [-Milestone ...]` (Debug and Release builds and full suites, then Release native runs). Do not edit sources while it runs. |
| Performance | Measure allocation (warm zero-allocation probes) only on hot paths and at milestone completion. Do not claim unmeasured improvements. |
| Documents | PLAN: position, milestone states and next actions at session end. PLAN_HISTORY: a few lines per session. STATUS: only when support boundaries change. Evidence lives in commits and `bin/verify/`. |
| Failures | Fix root causes; never weaken tests or diagnostics to pass. Record a blocker with the exact next step. |

## 3. Current position

- **Order (2026-09-26):** the Place foundation (P27) came first; the collection track (P27 → P39 → P28 → P31 → P40 → P26 → P37) precedes the Property/object track (P24 → P25 → P33 → P34 → P35 → P36 → P38). P25 and P33 keep their stopping points and are paused, not abandoned.
- **P28 IN_PROGRESS (2026-09-27):** all three user iteration entries execute concrete/generic lending loops, selected through every reference layer and checked against the conformance witnesses; associated families keep their verified formation domains and Loans. `Iterator.next` and `BufferWriter.reserve` share one fail-closed transitive effect summary (synthesized calls, Place paths through stored references and statics, abstract Items, published requirement bounds). The standard storage boundary `Kimi.Storage` (SPEC §22.1.2.5) is declared in Kimigayo and lowered natively for Array borrowing (`borrowStorage`, `splitFirst`); `Array<T>` conforms to Iterable/UniqIterable through the Kimigayo iterators `ArrayIterator<T>`/`ArrayUniqIterator<T>`, whose exclusive items are split children (region splitting). A wrapper passes the effect bound when it steps the one Iterator stored in a single Field, from any number of call sites (item provenance). The owning boundary (`ownStorage`/`takeFirst`, `OwnedRemainder` with its drop) and `Array<T>`'s IntoIterable entry execute natively. Unchanged Program 28 passes 43 Debug/Release checks. The `Kimi.Iteration` adapters enumerate LendingIterators without an entry and are Iterators exactly when their input is. Fixed-array and Dictionary boundaries, switching `for` to the Kimigayo iterators, the Kimigayo storage operations and the remaining family forms remain.
- **P31 IN_PROGRESS:** the unchanged target and all 71 Debug/Release harness checks pass. Search, ordered links, slot reuse, initialization, reverse cleanup and shrink-to-fit compile from `DictionaryStorage.kimi`. Public generic API/capacity migration, nonempty runtime literals and borrowed/nested storage forms remain; the indexing and iteration bridges wait for P27/P28.
- **P25/P33 paused (IN_PROGRESS):** Program 25 stops at inherited field ownership projection; Program 33 stops at flow-refined member lookup.
- **Verified implementation HEAD:** see the latest PLAN_HISTORY row and its session evidence. No NativeAOT run.
- The program status table is [milestones/README.md](milestones/README.md); product support boundaries are in STATUS. Source authoring or a successful target alone does not complete a milestone.

## 4. Milestones (execution order)

States: TODO / IN_PROGRESS / DONE. A milestone is DONE only when every condition in §5 holds.

| Order | ID | Program subject | State | Specific acceptance beyond §5 |
| --- | --- | --- | --- | --- |
| 1 | P22 | Generic generation by monomorphization | DONE | Every generic body reaches generation as per-substitution concrete bodies through ordinary lowering; the shared generation path is removed (its walk remains as template validation only). Programs 8–11, 18 and 22 and all generic tests pass; growing keys, depth and oversized substitution sets issue `GenerationResourceLimit_Kd` (§21.3.5). |
| 2 | P19 | Contracts, associated Types, conditional/nested conformance | DONE | Associated identities normalize after container substitution; verified requirement mappings select concrete instances. Unchanged target and 53 checks pass in Debug/Release. Broader Contract boundaries remain in STATUS. |
| 3 | P20 | Type/length/Origin inference, defaults, forwarding | DONE | Returned element borrows are read as Copy referents (issue G12 resolved); omitted call defaults evaluate once per omission (§7.2.3). Unchanged target and 55 checks pass in Debug/Release. Owned/borrow-producing defaults remain a STATUS limit owned by P24. |
| 4 | P21 | Explicit full specialization | DONE | Length specialization, inherited named/omitted Origin binders, inherited defaults and Constraints, receiver and compound specializations, no diagnostic cascade from an invalid specialization. Unchanged target and 61 checks pass in Debug/Release (G15 resolved 2026-09-23). |
| 5 | P29 | Dynamic Array | DONE | §4.7 capacity, mutation, Non-Copy elements, owning iteration and mandatory allocation bounds are verified; the Array part of G3 is settled. Unchanged Program 29 and 123 harness checks pass in Debug/Release O0/O2 (G17 resolved). |
| 6 | P32 | UTF-8 formatting and interpolation | DONE | Buffers, erased Writers, reserve effects, builtin/user/generic formatting, Text conversions, interpolation and `$tryWrite` pass focused, native/cost and full-session checks. Program 32 and its variants/rejections pass in Debug/Release O0/O2. |
| 7 | P30 | Equatable/Comparable Contracts | DONE | Primitive/user/generic, Tuple and borrow composition share finalized witnesses; Copy snapshots and NaN semantics are preserved. Unchanged source and 43 checks pass in Debug/Release. |
| 8 | P23 | Basic Properties | DONE | Unchanged target and 67 checks pass in Debug/Release O0/O2. Compound operations, getter-result projections/lifetimes, construction and storage access/Move restrictions pass focused and full-suite verification. |
| 9 | P27 | General Slice/Index/Range and the Place foundation | DONE (2026-09-26) | `Range`/`Index`/`ResolvedRange` values and keys, `resolve`/`tryResolve`, try-prefixed Slice operations, `splitAt`, nested views, generic reslicing with Origin equalities, separate backing/element Origins and the §4.6.8 costs (G3). Place foundation: `place ref/T`/`place uniq/T` results in declarations, Function Types and Contract requirements; Contract Type parameters; user `Indexable<Key>`/`UniqIndexable<Key>` with `index`/`indexUniq` selected from the use, including through a generic Constraint; the standard Array/fixed-array/Slice/Dictionary indexing keeps its verified behavior and is recorded as compiler-provided conformance where not yet declared in Kimigayo. |
| 10 | P39 | Semantics-generic follow | DONE (2026-09-27) | A pair `s/T` Place is followed to its stored target `T` under the admitted Semantics set; generic accessors over `Collection<s/T>` return `ref/T`/`uniq/T` with the dependencies of the followed layer. The re-spelled target passes 29 harness checks in Debug and Release. |
| 11 | P28 | General Iterator/Iterable | IN_PROGRESS | Shared, Exclusive and ByValue user lending loops, Contract-parameter formation domains and collection results pass, as does the unchanged target. Array borrowing and owning over the `Kimi.Storage` boundary, its Kimigayo iterators and the Kimi.Iteration adapters pass. Finish the remaining collections, the `for` migration, the Kimigayo storage operations and the remaining associated-family forms. |
| 12 | P31 | Dictionary | IN_PROGRESS | Unchanged target and 71 Debug/Release harness checks pass. Finish the source migration over the Place and storage boundaries (G20): indexing through `UniqIndexable<K>`, iteration through the standard entries, generic operation/result dispatch and reserve/growth in Kimigayo; nonempty runtime literals; borrowed indexing and nested owning storage. Place-independent items may be finished earlier at a convenient point. |
| 13 | P40 | Disjoint exclusive element access | TODO | Depends on the API decision recorded with G22. Two distinct elements of a standard collection are borrowed exclusively at the same time through a splitting operation built on `Kimi.Storage`; equal keys are rejected at runtime with `None`; conflicting whole-collection access is rejected statically. Program 40 is authored after the decision. |
| 14 | P26 | General closures and Callable | TODO | Composite/generic captures, function items and erasure (issue G10), indirect-call ABI. |
| 15 | P37 | Integrated processing application | DONE (2026-09-27) | Collections, borrows, iteration and closures combined. The unchanged target passes 47 harness checks in Debug and Release (variants, Abort path, Loan violations) and its allocation/complexity observations (`WorkloadCostTest`). |
| 16 | P24 | Ownership-bearing Properties | TODO | Non-Copy setters, getter results and temporaries, Contract witnesses. |
| 17 | P25 | Inheritance | IN_PROGRESS (paused) | Explicit base prefix construction and layered/partial destruction pass. Finish implicit base calls and inherited member projections; the unchanged target stops at ownership analysis of inherited fields. |
| 18 | P33 | Exclusive objects and views | IN_PROGRESS (paused) | Runtime Type tests, explicit concrete base upcasts, payload exchange and complete dynamic destruction/release pass focused native checks. The authored target still fails Binding on flow refinement; inherited field projections and the unchanged target harness remain. |
| 19 | P34 | Shared object ownership (rc/arc) | TODO | Validate source declarations, Binding, ownership and runtime support for the already-cataloged ownership family (issue G4). Program 34 is authored; Binding stops at `Kimi.Intrinsics.makeRc`. |
| 20 | P35 | Weak | TODO | Downgrade/upgrade, expiration, cyclic construction, table release. Program 35 is authored; Binding stops at `Weak<rc/Node>` formation. |
| 21 | P36 | General static storage | TODO | First initialization, effects, cycles, shutdown. Program 36 is authored and binds; ownership analysis stops at the static group. |
| 22 | P38 | Integrated core application | TODO | Properties, inheritance, objects and formatting combined. Program 38 is authored; Binding stops at `Weak<rc/Lamp>`. |

P22, P19, P20, P21, P29, P32, P30, P23, P27, P37 and P39 are done. P28 is the active milestone.

### Toolchain track (after P38, or earlier when instructed)

The user-requested [LSP diagnostics plan](LSP_PLAN.md) defines a separate tooling track (L1–L6, with L5 split into L5a/L5b): reuse the compiler front end, debounce editor input, and establish shared check inputs/results for a future CSP adapter. **Planning complete; implementation not started.** The compiler milestone order is unchanged.

| ID | Subject | Acceptance |
| --- | --- | --- |
| T1 | Foreign imports across modules | Dependency-module imports link against that module's own `NativeLibraries` supplies through link manifest schema 4 with scoped requirement identities (§20.8.3, §18.5.2), plus archive member kind/provider validation (§20.8.2.3). Two-module native test. |
| T2 | Raw pointer residue | Dependent (reference-containing) pointees with an explicit Origin/access plan; C-exchangeability certification (§21.1.6); C-layout diagnostics for inferred instantiations. |
| T3 | Source packages and stores | §18.6 pack/load/publish/store with integrity checks and CLI commands. |
| T4 | Verified semantic reuse | §18.7 records, invalidation and cold/warm equivalence. |
| T5 | Test runner completion | Cross-feature test generation and product/test region separation (§21.3.7, testing profile). |
| T6 | Resource limits | Deterministic finite generation/analysis limits with resource diagnostics. |
| T7 | Conformance and delivery | Appendix A coverage for implemented areas; README and examples updated. |

## 5. Completion conditions (every program milestone)

1. The spec-derived program source builds unchanged with the Debug and Release compilers and runs at O0 and O2 with the expected stdout, exit code and stderr.
2. `backend/windows-x64/test-milestone<N>.ps1` exists and passes: target, listed variants and required rejections with their specified diagnostic codes.
3. Session verification passes: zero warnings, full Debug and Release suites, and the harnesses of all earlier completed programs.
4. The owning SPEC sections exercised by the program have focused positive and negative tests; unsupported neighboring forms fail with a diagnostic, never with wrong code.
5. STATUS.md describes the new support boundary, and the milestone is marked DONE here and in `milestones/README.md`.

A specification change that re-spells a completed program keeps it DONE when conditions 1–3 hold for the re-spelled source; `milestones/README.md` records the re-spelling.

Features that a program's source does not use belong to the milestone that owns them, not to that program.

## 6. Next actions

0. **P28 (resume here), review follow-up in this order (decided 2026-09-27):** (a) closed-world refutation of a concrete Type's conformance (§8.7), with exact negative diagnostics; (b) the Iterator effect bound composed by item provenance instead of the one-forwarded-step rule (§22.1.2.4); composite adapters (zip/chain/map/filter) stay out of scope; (c) `for` over Array through the Kimigayo iterators once O2 IR leaves no helper call in the loop body and the cost tests show no difference; (d) internal unsafe primitives (pointer to reference with an Origin, raw-storage initialization) so that the `Kimi.Storage` operations are written in Kimigayo; (e) table-driven validation of compiler-known library declarations. Then the fixed-array boundary overloads and `[N of E]` entries, and the Dictionary boundary.
1. **P28:** fixed-array and Dictionary boundaries; switch `for` over standard collections to the Kimigayo iterators once their cost matches the compiler path; remaining Origin-bearing family forms and multiple bound-reference disambiguation; the §4.6.8 Slice cost evidence (G3).
2. **Then P31, P40, P26** in §4 order. Independent library work remains `sort()` using the existing heapsort and `init(! repeating:count:)`; `first`/`last` wait for P24, followed by the Property/object track.

## 7. Open issues

| ID | Issue | Owner |
| --- | --- | --- |
| G3 | Array and Dictionary growth, allocation, reuse and warm compilation costs have native/counter evidence (`DynamicArrayCostTest`, `DictionaryCostTest`). The remaining general Slice operations still need their §4.6.8 cost evidence; there is no public collection ABI. | P27 |
| G4 | Weak and the rc/arc ownership family already have individual catalog identities with `SourceExpected: false`; source declarations, validation, Binding and runtime implementations remain unavailable (§22.1, §13.5.8–9). Catalog presence is not implemented support. | P34/P35 |
| G10 | Named function groups used as Function Types need selection, Origin and erasure paths (overloads, generics, members). | P26 |
| G20 | The user requested Dictionary in Kimigayo. Ordered storage algorithms compile from ordinary source, but public generic operation/result dispatch, reserve/growth and typed index/iteration bridges still reside in the compiler. The index bridge becomes `UniqIndexable<K>` (P27) and the iteration bridge the standard entries over `Kimi.Storage` (P28). See `Kimi/Library/README.md`. | P31 |
| G28 | The Iterator effect summary treats a local or a match Subject of an abstract item Type as destroyed even when its payload is moved out, so wrappers that inspect an item before returning it (retry after None, peek) fail the bound although no destructor runs. Proposed: apply the ownership analysis' move-out facts, or a local rule for a by-value payload binding that is moved on every path. | P28 |
| G22 | Two exclusive element borrows of one collection conflict even for distinct runtime indices (§3.4). A splitting API over `Kimi.Storage` (for example `pairUniq(first, second) -> Option<(uniq/E, uniq/E)>`) keeps disjointness out of the Get operation. Needs an API decision after P28 provides the storage boundary. | P40 |
