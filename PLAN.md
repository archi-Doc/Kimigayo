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

- **Order revised (2026-09-26):** the Place foundation of §7.1.1 and §4.6.9 (`place ref/T` results, Contract Type parameters, user `Indexable`/`UniqIndexable`) is the common root of the remaining Dictionary migration (G20), of the borrowing iteration entries and of the two open collection designs (G21, G22). It is therefore implemented first, inside P27, and the collection track (P27 → P39 → P28 → P31 → P40 → P26 → P37) precedes the Property/object track (P24 → P25 → P33 → P34 → P35 → P36 → P38). P25 and P33 keep their stopping points and are paused, not abandoned. Programs 39 and 40 were added to the [roadmap](milestones/README.md#roadmap-from-program-15-to-core-completion); their sources are written when their specifications are decided.
- **P27 DONE (2026-09-26):** Program 27 passes 97 Debug harness checks (`bin/verify/20260926-154051-101-unit-m27-harness1`). Place results and the Indexable Contracts (Contract Type parameters, `receiver[key]` through `index`/`indexUniq`, generic `firstPlace`), Kimigayo `Index`/`Range`/`ResolvedRange` with `resolve`/`tryResolve`, Index/Range/ResolvedRange keys, the Slice operations, nested views and generic reslicing are implemented; the boundaries are in STATUS. The Release harness runs with the next session verification.
- **Programs 34–39 authored (2026-09-27):** sources per the README verification scopes (Program 39 per `draft/Design/2026-09-26 Semantics-Generic Follow.md`), first diagnostics in `milestones/stage-baselines.json`. Program 37 binds, verifies and passes its Debug harness (unchanged target, two view variants, three rejections); Program 36 binds and stops at static-storage ownership analysis; Programs 34, 35 and 38 stop at the undeclared rc/arc/Weak intrinsics (G4) and Program 39 at the pair follow (G21).
- **Verified implementation HEAD `3f8e2284`:** session `bin/verify/20260926-102959-163-session-follow-copy-slot` passes warning-free Debug/Release builds, 12,619 tests each, 306 Shared native O0/O2 executions and the Release harnesses of Programs 1–23 and 29–32; all 3,618 native fixtures pass at O0. No NativeAOT run.
- **P31 IN_PROGRESS:** the unchanged target and all 71 Debug/Release harness checks pass. Search, ordered links, slot reuse, initialization, reverse cleanup and shrink-to-fit compile from `DictionaryStorage.kimi`; warm compiler allocation probes remain zero. Public generic API/capacity migration, nonempty runtime literals and borrowed/nested storage forms remain; the indexing and iteration bridges wait for P27/P28.
- **P25/P33 paused (IN_PROGRESS):** explicit base construction, layered destruction, concrete runtime Type tests, payload exchange and explicit base views pass focused/native checks. Program 25 stops at inherited field ownership projection; Program 33 stops at flow-refined member lookup.
- **Places, borrowing and iteration (2026-09-26):** integrated into SPEC and implemented through `80f74a75` (`@follow`, slot borrows, RHS-first updates, Subject rule, reference-binding Patterns, `for` modes, element Places, implicit borrows at expected references, `@copy`, single-slot `during`). The specification forms not yet parsed (`place ref/T` and `place uniq/T` results, Contract Type parameters, parameterized `associate` with formation Types, `T.(C).LentItem(a)`) are owned by P27 and P28; the verified boundaries are in STATUS.
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
| 10 | P39 | Semantics-generic follow | TODO | Depends on the specification decision recorded in `draft/Design/2026-09-26 Semantics-Generic Follow.md` (G21). A pair `s/T` Place is followed to its stored target `T` under the admitted Semantics set; generic accessors over `Collection<s/T>` return `ref/T`/`uniq/T` with the dependencies of the followed layer. Program 39 is authored (2026-09-27); its Binding stops at `c[i]@follow` until the decision is implemented. |
| 11 | P28 | General Iterator/Iterable | TODO | User `Iterable`/`UniqIterable`/`IntoIterable` protocols, `LendingIterator`, parameterized `associate` with formation Types, `T.(C).LentItem(a)`, the `Kimi.Iteration` adapters, `Kimi.Storage` region splitting and the standard iterators in Kimigayo; owned/borrowed elements, early-exit cleanup; general loop backedge/continue joins. |
| 12 | P31 | Dictionary | IN_PROGRESS | Unchanged target and 71 Debug/Release harness checks pass. Finish the source migration over the Place and storage boundaries (G20): indexing through `UniqIndexable<K>`, iteration through the standard entries, generic operation/result dispatch and reserve/growth in Kimigayo; nonempty runtime literals; borrowed indexing and nested owning storage. Place-independent items may be finished earlier at a convenient point. |
| 13 | P40 | Disjoint exclusive element access | TODO | Depends on the API decision recorded with G22. Two distinct elements of a standard collection are borrowed exclusively at the same time through a splitting operation built on `Kimi.Storage`; equal keys are rejected at runtime with `None`; conflicting whole-collection access is rejected statically. Program 40 is authored after the decision. |
| 14 | P26 | General closures and Callable | TODO | Composite/generic captures, function items and erasure (issue G10), indirect-call ABI. |
| 15 | P37 | Integrated processing application | TODO | Collections, borrows, iteration and closures combined. The authored target passes its Debug harness (2026-09-27); the Release harness, allocation/complexity observations and the remaining workload-derived rejections are open. |
| 16 | P24 | Ownership-bearing Properties | TODO | Non-Copy setters, getter results and temporaries, Contract witnesses. |
| 17 | P25 | Inheritance | IN_PROGRESS (paused) | Explicit base prefix construction and layered/partial destruction pass. Finish implicit base calls and inherited member projections; the unchanged target stops at ownership analysis of inherited fields. |
| 18 | P33 | Exclusive objects and views | IN_PROGRESS (paused) | Runtime Type tests, explicit concrete base upcasts, payload exchange and complete dynamic destruction/release pass focused native checks. The authored target still fails Binding on flow refinement; inherited field projections and the unchanged target harness remain. |
| 19 | P34 | Shared object ownership (rc/arc) | TODO | Validate source declarations, Binding, ownership and runtime support for the already-cataloged ownership family (issue G4). Program 34 is authored; Binding stops at `Kimi.Intrinsics.makeRc`. |
| 20 | P35 | Weak | TODO | Downgrade/upgrade, expiration, cyclic construction, table release. Program 35 is authored; Binding stops at `Weak<rc/Node>` formation. |
| 21 | P36 | General static storage | TODO | First initialization, effects, cycles, shutdown. Program 36 is authored and binds; ownership analysis stops at the static group. |
| 22 | P38 | Integrated core application | TODO | Properties, inheritance, objects and formatting combined. Program 38 is authored; Binding stops at `Weak<rc/Lamp>`. |

P22, P19, P20, P21, P29, P32, P30, P23 and P27 are done. The P39 decision is the active step.

### Toolchain track (after P38, or earlier when instructed)

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

1. **P39 decision:** review `draft/Design/2026-09-26 Semantics-Generic Follow.md`; on acceptance integrate it into §3.4.1, §8.1.1, §8.9 and §13.5.5.1 and implement it before P28. Program 39 is the target.
2. **Then P28, P31, P40, P26, P37** in the order of §4; the Property/object track follows. The §4.6.8 Slice cost evidence (G3) is collected with P28's iteration work. Program 37 already passes its Debug harness; P37 adds the Release harness, allocation observations and remaining rejections.

## 7. Open issues

| ID | Issue | Owner |
| --- | --- | --- |
| G3 | Array and Dictionary growth, allocation, reuse and warm compilation costs have native/counter evidence (`DynamicArrayCostTest`, `DictionaryCostTest`). The remaining general Slice operations still need their §4.6.8 cost evidence; there is no public collection ABI. | P27 |
| G4 | Weak and the rc/arc ownership family already have individual catalog identities with `SourceExpected: false`; source declarations, validation, Binding and runtime implementations remain unavailable (§22.1, §13.5.8–9). Catalog presence is not implemented support. | P34/P35 |
| G10 | Named function groups used as Function Types need selection, Origin and erasure paths (overloads, generics, members). | P26 |
| G20 | The user requested Dictionary in Kimigayo. Ordered storage algorithms compile from ordinary source, but public generic operation/result dispatch, reserve/growth and typed index/iteration bridges still reside in the compiler. The index bridge becomes `UniqIndexable<K>` (P27) and the iteration bridge the standard entries over `Kimi.Storage` (P28). See `Kimi/Library/README.md`. | P31 |
| G21 | Generic code over `Collection<s/T>` cannot reach `T` through the pair Place: reference-path selection stops at an undetermined `s/T` (§3.4.1). Proposed rule: `@follow` on a pair Place whose admitted set lies in `value or valueborrow` selects the stored target for every `s` (`draft/Design/2026-09-26 Semantics-Generic Follow.md`). Needs the user's decision. | P39 |
| G22 | Two exclusive element borrows of one collection conflict even for distinct runtime indices (§3.4). A splitting API over `Kimi.Storage` (for example `pairUniq(first, second) -> Option<(uniq/E, uniq/E)>`) keeps disjointness out of the Get operation. Needs an API decision after P28 provides the storage boundary. | P40 |
