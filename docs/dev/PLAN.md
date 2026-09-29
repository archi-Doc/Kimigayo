# Kimigayo Compiler Plan

Current plan only: position, milestone order, completion conditions, next actions and open issues. Required behavior is in [SPEC.md](../SPEC.md); implemented support is in [STATUS.md](../STATUS.md); a few lines per session are in [PLAN_HISTORY.md](PLAN_HISTORY.md). Items deferred by [Appendix D](../spec/appendices/D-deferred-features.md) are not planned here.

## 1. Scope

Implement the finalized language of SPEC.md (Chapters 1–19 and 22) and the implementation contracts of [IMPL.md](../IMPL.md) (Chapters 20–21, the test execution profile and Appendix A) for the Windows x64 profile, excluding Appendix D. Progress is driven vertically by the [Milestone Programs](../../tests/milestones/README.md): each milestone completes one program end to end (Binding, ownership, generation, native execution and required rejections).

- **Generics:** the initial profile monomorphizes (§21.3.1). Generic code sharing is deferred; milestone P22 replaced the existing shared generation path.
- **Origins and the Contract/type system** (§8, §15) remain complete requirements; implementation limits never narrow them.
- **Draft files** (`draft/`) and **NativeAOT tests** need explicit instruction.

## 2. Working rules

| Rule | Detail |
| --- | --- |
| Unit | One coherent change: reproducer, implementation, focused tests. Commit each verified unit with a descriptive message. |
| Unit verification | `./scripts/verify.ps1 -Class <test classes> [-Fixtures '<pattern>'] [-Milestone <n>]`: Release build with warnings as errors, related tests, related native O0/O2 fixtures and harnesses. Pass `-Configuration Debug` explicitly when needed. |
| Session verification | Once at session end: `./scripts/verify.ps1 -Mode Session [-Fixtures ...] [-Milestone ...]` (Release build and full suite). Pass `-Configuration Debug` explicitly when needed; all stages use the selected compiler configuration, with native O0/O2 coverage unchanged. Do not edit sources while it runs. |
| Performance | Measure allocation (warm zero-allocation probes) only on hot paths and at milestone completion. Do not claim unmeasured improvements. |
| Documents | PLAN: position, milestone states and next actions at session end. PLAN_HISTORY: a few lines per session. STATUS: only when support boundaries change. Evidence lives in commits and `artifacts/verify/`. |
| Failures | Fix root causes; never weaken tests or diagnostics to pass. Record a blocker with the exact next step. |

Toolchain identity is checked during setup/update or with `kimi toolchain verify`; `verify.ps1 -VerifyToolchain` performs it once before tests. Ordinary runs record it as not performed. CLI tests use `run --no-build` except when checking automatic builds.

## 3. Current position

- **Diagnostics track (2026-09-30, user decision):** the diagnostics plan is integrated (D0): SPEC §23.3.3, §23.3.6 and §23.4.7 define outcomes, acceptance, records, categories, prerequisites, limits, order and rendering; [DIAGNOSTICS.md](DIAGNOSTICS.md) holds the internal model, common rules and migration rules; AGENTS.md holds the Diagnostic Development Workflow. JSON records are prepared but not emitted. D1 is done (`892dc9b3`, `51bc6705` and the snapshot unit): categories, strict catalog loading and record-time contract checks, one test helper, the corpus snapshot (`verify.ps1 -DiagnosticSnapshot`) and measurements (`Benchmark --diagnostics`); audit findings are in DIAGNOSTICS.md §11. P31 is paused until D2b completes, and no language milestone runs in parallel with D2a or D2b.
- **Verification performance (2026-09-30):** toolchain checks are explicit, kernel32 is shared, and 32 original-source milestone runners pass O0/O2. Session `20260929-195754-059-session-verification-performance-retry` passes a warning-free Release build, 13,461 tests and 58 related native fixture executions. CLI, module and installation-failure checks pass separately. Milestone 1 takes 2.54s versus 22.13s before workload simplification (single samples; not compiler throughput). Failed initial runs remain recorded; see PLAN_HISTORY.

- **Repository layout and outputs (2026-09-28):** `426563d2` separates disposable `temp/` work from retained `artifacts/` evidence/packages and reorganizes the English README. Projects remain under `src/` and `tests/`; verification starts at `scripts/verify.ps1`. Session `20260928-123203-403-session-temp-artifacts` passes warning-free Debug/Release builds, all 13,038 tests in each configuration, 2 native O0/O2 executions and 25 Release Milestone 1 checks. Milestone 28 and its retry are blocked by Windows application control at native process startup. Evidence is under `artifacts/verify/`; language milestone position is unchanged.

- **Order (2026-09-26):** the Place foundation (P27) came first; the collection track (P27 → P39 → P28 → P31 → P40 → P26 → P37) precedes the Property/object track (P24 → P25 → P33 → P34 → P35 → P36 → P38). P25 and P33 keep their stopping points and are paused, not abandoned.
- **Decisions (2026-09-29, user):** P29 was reopened for zero-sized Array elements and completed first (SPEC §5.3 now allows a zero pointer stride); then P28 is finished. G32 takes option A and G33 option A'; both are implemented (STATUS, Fixed-array iteration). `draft/Changes/2026-09-29 Redesign Ranges B.md` becomes P41 after P28: `Position`/`PositionRange` are closed ordinary Contracts whose implied capabilities use refinement, and its fixed-array read API (`tryGet`/`splitAt`/`trySplitAt`) uses the fixed-array member group of G32.
- **P28 DONE (2026-09-29):** every standard collection iterates through Kimigayo entries over `Kimi.Storage`: Array, Dictionary, fixed arrays (G32 member group, G33 `InlineStorage<A>` owning remainder, `1860ecd8`) and Slices (`c2f82613`, which removes the compiler fixed-array/Slice loop lowering; only ResolvedRange keeps a compiler loop). Associated families may borrow a pair target stored in Self (`bedd0898`); the remaining family limits have owners (P26, P33–P35, G34). P29 was reopened and completed for zero-sized Array elements (`3c931842`).
- **P41 DONE (2026-09-30):** positions and ranges follow the integrated Redesign Ranges B (SPEC §4.6, `8c180cf3`), in units U1–U10 (`c15aed3d`…`debcad88`). SPEC §4.6.6 was corrected during implementation: the range form of `tryGet` is `trySlice`, because Constraints alone never distinguish overloads (§9.1). Aborts of invalid positions passed to the Kimigayo Array entries report the library call site. Completion follow-up (`756f7005`, `e6d2c5ee`): operation-argument warnings, compile-time folding on fixed arrays, the model-based property tests, the integer-specialization selection fix and the before/after comparison. Fixed-array `length`/`indices` stay compiler metadata (library README). Measurements: `artifacts/benchmarks/p41-closed-range-iterator`, `p41-saved-range-slicing`, `p41-before-after`.
- **P31 IN_PROGRESS:** the unchanged target and all 71 Debug/Release harness checks pass. Search, ordered links, slot reuse, initialization, reverse cleanup and shrink-to-fit compile from `DictionaryStorage.kimi`. Public generic API/capacity migration, nonempty runtime literals and borrowed/nested storage forms remain; the indexing and iteration bridges wait for P27/P28.
- **P25/P33 paused (IN_PROGRESS):** Program 25 stops at inherited field ownership projection; Program 33 stops at flow-refined member lookup.
- **P28 declaration validation complete (2026-09-29):** review item 6 is done: every compiler-known function, Contract shape and compiler-constructed record is checked against complete bound identities (iteration entries and Iterator `b811c8c5`, formatting operations `619deed4`, comparison/indexing Contracts `5fb68613`, Option/Result Cases and Index/range fields `df6f17dd`). Slice §4.6.8 cost evidence exists (`dcd03ac3`, G3 resolved), and warm emission of Slice `Index` overload calls no longer allocates (`bbdc72ed`).
- **P28 Dictionary storage boundary (2026-09-29):** the shared, exclusive and owning Dictionary remainder families of SPEC §22.1.2.5 are declared in `Kimi.Storage`, validated, generated and used by Kimigayo `splitFirst`/`takeFirst`/`deinit` (`2bafe315`, `4a86cc2f`, `1fb00204`, `7562cd52`); `Dictionary` declares all three entries with `DictionaryIterator`, `DictionaryUniqIterator` and `DictionaryOwningIterator` (`71657368`). Explicit iteration executes; `for` over a Dictionary still uses the compiler path.
- **P28 Dictionary `for` and G28 (2026-09-29):** nested pattern components descend from their outermost Subject (`98eb7ba5`); `for` over a Dictionary uses the Kimigayo entries in all modes (`eba77369`, `a2396b5b`, `1a4f222e`) and the compiler Dictionary and owning-Array loop paths are removed (`66c4e0cb`, `3a0204ba`); a by-value match or an immutable local that transfers an item at once no longer counts as destroying it (`0eac53d1`, `648071ec`); the fixed-array `borrowStorage` overloads exist and execute (`9fc3457c`, `76ced139`).
- **P28 bound-reference disambiguation (2026-09-29):** specifications qualified by a bound Contract reference (`associate Indexable<isize>.Element is i32`) belong to that conformance, and `receiver[key]` selects among several `Indexable<Key>`/`UniqIndexable<Key>` conformances by the key's own Type (`bf6c09cd`).
- **P28 G28 item transfers (2026-09-29, superseded):** the syntactic transfer rules of the Iterator effect summary (commits `776e71ef`…`735735cb`, `111abd36`) were replaced the same day by cleanup-based destruction effects (below).
- **P28 generic bound references (2026-09-29):** associated Types and families of a generic Contract carry their declaring bound reference, so `S.(Indexable<isize>).Element` and `S.(Indexable<Name>).Element` are distinct; requirement candidates are kept per reference and a generic requirement call dispatches through the instantiated reference's conformance (`292fdd4a`, families `f0939ef1`, exclusive updates `0452d89c`, warm-allocation guard `774b03c4`). Value-producing `if`/`match` selections now store an item delivered by every branch (`111abd36`).
- **Implementation review follow-up (2026-09-29):** all six implementation items of the session review are done. (6) the conformance collision check reuses its scratch map, so a Type with two `Indexable` conformances binds warm with zero bytes (`afea3079`); (5) every requirement call records its Contract reference and dispatches only through it, which also fixed `Self is Indexable<K>` projections of generic Types (`31f32723`); (2) `EvaluatedKoto` is the one operand for values a desugaring already evaluated, so `make()[^1]` evaluates its receiver once (`c83a21e5`); (3) writes through a Place call, a followed `uniq` reference or a borrowed field are one replacement (`StorePointer`) for any stored Type, sharing the element write's destroy-then-move lowering (`809d185e`); (1) effect bounds count destructions after ownership analysis from its planned cleanups, with a `MayOwn` dataflow lane and Subject payloads per arm, and every syntactic G28 rule is removed (`84ce4198`, G28 resolved); (4) Contract `Self` is a dedicated Type parameter and every projection names its declaring Contract reference (`4e020fc4`).
- **Verified implementation HEAD:** `debcad88`; Session `20260929-171813-572-session-p41-completion` passes a warning-free Release build, 13,454 tests, 7,782 native O0/O2 executions (every regenerated fixture) and 32 Release harnesses (1–23, 27–32, 37, 39, 41). No NativeAOT run.
- The program status table is [milestones/README.md](../../tests/milestones/README.md); product support boundaries are in STATUS. Source authoring or a successful target alone does not complete a milestone.

## 4. Milestones (execution order)

States: TODO / IN_PROGRESS / DONE. A milestone is DONE only when every condition in §5 holds.

| Order | ID | Program subject | State | Specific acceptance beyond §5 |
| --- | --- | --- | --- | --- |
| 1 | P22 | Generic generation by monomorphization | DONE | Every generic body reaches generation as per-substitution concrete bodies through ordinary lowering; the shared generation path is removed (its walk remains as template validation only). Programs 8–11, 18 and 22 and all generic tests pass; growing keys, depth and oversized substitution sets issue `GenerationResourceLimit_Kd` (§21.3.5). |
| 2 | P19 | Contracts, associated Types, conditional/nested conformance | DONE | Associated identities normalize after container substitution; verified requirement mappings select concrete instances. Unchanged target and 53 checks pass in Debug/Release. Broader Contract boundaries remain in STATUS. |
| 3 | P20 | Type/length/Origin inference, defaults, forwarding | DONE | Returned element borrows are read as Copy referents (issue G12 resolved); omitted call defaults evaluate once per omission (§7.2.3). Unchanged target and 55 checks pass in Debug/Release. Owned/borrow-producing defaults remain a STATUS limit owned by P24. |
| 4 | P21 | Explicit full specialization | DONE | Length specialization, inherited named/omitted Origin binders, inherited defaults and Constraints, receiver and compound specializations, no diagnostic cascade from an invalid specialization. Unchanged target and 61 checks pass in Debug/Release (G15 resolved 2026-09-23). |
| 5 | P29 | Dynamic Array | DONE | §4.7 capacity, mutation, Non-Copy elements, owning iteration and mandatory allocation bounds are verified; the Array part of G3 is settled. Unchanged Program 29 and 129 harness checks pass in Debug/Release O0/O2 (G17 resolved). Zero-sized elements (reopened and completed 2026-09-29) use stride zero and a never-allocated substitute buffer. |
| 6 | P32 | UTF-8 formatting and interpolation | DONE | Buffers, erased Writers, reserve effects, builtin/user/generic formatting, Text conversions, interpolation and `$tryWrite` pass focused, native/cost and full-session checks. Program 32 and its variants/rejections pass in Debug/Release O0/O2. |
| 7 | P30 | Equatable/Comparable Contracts | DONE | Primitive/user/generic, Tuple and borrow composition share finalized witnesses; Copy snapshots and NaN semantics are preserved. Unchanged source and 43 checks pass in Debug/Release. |
| 8 | P23 | Basic Properties | DONE | Unchanged target and 67 checks pass in Debug/Release O0/O2. Compound operations, getter-result projections/lifetimes, construction and storage access/Move restrictions pass focused and full-suite verification. |
| 9 | P27 | General Slice/Index/Range and the Place foundation | DONE (2026-09-26) | `IndexRange`/`Index`/`ResolvedRange` values and keys, `resolve`/`tryResolve`, try-prefixed Slice operations, `splitAt`, nested views, generic reslicing with Origin equalities, separate backing/element Origins and the §4.6.8 costs (G3). Place foundation: `place ref/T`/`place uniq/T` results in declarations, Function Types and Contract requirements; Contract Type parameters; user `Indexable<Key>`/`UniqIndexable<Key>` with `index`/`indexUniq` selected from the use, including through a generic Constraint; the standard Array/fixed-array/Slice/Dictionary indexing keeps its verified behavior and is recorded as compiler-provided conformance where not yet declared in Kimigayo. |
| 10 | P39 | Semantics-generic follow | DONE (2026-09-27) | A pair `s/T` Place is followed to its stored target `T` under the admitted Semantics set; generic accessors over `Collection<s/T>` return `ref/T`/`uniq/T` with the dependencies of the followed layer. The re-spelled target passes 29 harness checks in Debug and Release. |
| 11 | P28 | General Iterator/Iterable | DONE (2026-09-29) | User lending loops in all three modes, LendingIterator/Iterator with the effect bound, the `Kimi.Iteration` adapters, associated families and every standard collection iterate through Kimigayo entries over `Kimi.Storage` (G28–G30, G32, G33 resolved). The unchanged target passes 43 harness checks in Debug and Release. |
| 12 | P41 | Positions and ranges | DONE (2026-09-30) | The SPEC integrates `draft/Changes/2026-09-29 Redesign Ranges B.md`; Type identity as substitution, closed `Position`/`PositionRange`, the value read, `FromEnd<T>`/`Start`/`End`/`Range<S, E>`/`ClosedRange<S, E>`, generic position entries, merged bounds checks, the measured closed iterator and the §17.4.4 warning are implemented. Program 41 passes 77 harness checks; programs 7, 27, 29 and 37 are re-spelled and keep their state. |
| 13 | P31 | Dictionary | IN_PROGRESS (paused for the Diagnostics track) | Unchanged target and 71 Debug/Release harness checks pass. Finish the source migration over the Place and storage boundaries (G20): indexing through `UniqIndexable<K>`, iteration through the standard entries, generic operation/result dispatch and reserve/growth in Kimigayo; nonempty runtime literals; borrowed indexing and nested owning storage. Place-independent items may be finished earlier at a convenient point. |
| 14 | P40 | Disjoint exclusive element access | TODO | Depends on the API decision recorded with G22. Two distinct elements of a standard collection are borrowed exclusively at the same time through a splitting operation built on `Kimi.Storage`; equal keys are rejected at runtime with `None`; conflicting whole-collection access is rejected statically. Program 40 is authored after the decision. |
| 15 | P26 | General closures and Callable | TODO | Composite/generic captures, function items and erasure (issue G10), indirect-call ABI. |
| 16 | P37 | Integrated processing application | DONE (2026-09-27) | Collections, borrows, iteration and closures combined. The unchanged target passes 47 harness checks in Debug and Release (variants, Abort path, Loan violations) and its allocation/complexity observations (`WorkloadCostTest`). |
| 17 | P24 | Ownership-bearing Properties | TODO | Non-Copy setters, getter results and temporaries, Contract witnesses. |
| 18 | P25 | Inheritance | IN_PROGRESS (paused) | Explicit base prefix construction and layered/partial destruction pass. Finish implicit base calls and inherited member projections; the unchanged target stops at ownership analysis of inherited fields. |
| 19 | P33 | Exclusive objects and views | IN_PROGRESS (paused) | Runtime Type tests, explicit concrete base upcasts, payload exchange and complete dynamic destruction/release pass focused native checks. The authored target still fails Binding on flow refinement; inherited field projections and the unchanged target harness remain. |
| 20 | P34 | Shared object ownership (rc/arc) | TODO | Validate source declarations, Binding, ownership and runtime support for the already-cataloged ownership family (issue G4). Program 34 is authored; Binding stops at `Kimi.Intrinsics.makeRc`. |
| 21 | P35 | Weak | TODO | Downgrade/upgrade, expiration, cyclic construction, table release. Program 35 is authored; Binding stops at `Weak<rc/Node>` formation. |
| 22 | P36 | General static storage | TODO | First initialization, effects, cycles, shutdown. Program 36 is authored and binds; ownership analysis stops at the static group. |
| 23 | P38 | Integrated core application | TODO | Properties, inheritance, objects and formatting combined. Program 38 is authored; Binding stops at `Weak<rc/Lamp>`. |

P22, P19, P20, P21, P29, P32, P30, P23, P27, P39, P28, P37 and P41 are done. P31 is paused while the Diagnostics track runs D1–D2b (user decision, 2026-09-30); it resumes afterwards.

### Diagnostics track (active)

Stages follow [DIAGNOSTICS.md](DIAGNOSTICS.md) §8; each unit follows the Diagnostic Development Workflow (AGENTS.md). The authority is SPEC §23.3.3, §23.3.6 and §23.4.7.

| ID | Stage | State | Completion condition |
| --- | --- | --- | --- |
| D0 | Documentation intake: SPEC, Appendices D/E, DIAGNOSTICS.md, AGENTS.md, execution prompt; the draft is frozen | DONE (2026-09-30) | Work proceeds from the permanent documents alone. |
| D1 | Baseline and definitions, with no published change: snapshot harness and performance baseline; one test helper for diagnostics; catalog categories, Advice, load validation with template arity; audit of shared codes, free-text codes (such as `ControlFlow_Kd`) and implementation limits reported with `Language` codes; location fixes; `DiagnosticPrecisionTest` and `MilestoneSourcesTest` read public records | DONE (2026-09-30) | Every code has a category; enumeration and catalog consistency tests pass; existing tests produce no invalid location; the snapshot equals the baseline. Baseline snapshot `artifacts/verify/20260929-225614-367-unit-diagnostic-baseline/diagnostic-snapshot.json` (75 cases); measurements `artifacts/benchmarks/diagnostics-d1-baseline`. |
| D2a | Common finalization boundary: error state separated from display; diagnostic owner, partitions and source table; `CheckDiagnostic` with the fault path; one CLI finalization point; language-server conversion | TODO | Snapshot differences are only the intended kinds (timing, order, attribution, error-state fixes). |
| D2b | Identity and explicit prerequisites, recorder by recorder (lexing and parsing with the recovery map, Binding, startup, control flow, ownership); then remove the transitional filter, `DiagnosticDependencyVisitor` and the `BorrowOriginHint` suppression | TODO | Independent problems at one position survive; syntax cascades are suppressed; milestone and mutation runs show no unexplained rejection and no fallback; order is deterministic; milestone stage baselines are reviewed and updated. |
| D3 | Explanations: bounded formatting, excerpts and evidence; Type mismatch and invalid assignment Reasons and ranges; Move, borrow and overload detail after a cost review | TODO | Target cases show the expected locations, Reasons and evidence. |
| D4 | Evaluation: CLI and language-server results, performance, verified scope and remaining limits | TODO | DIAGNOSTICS.md §9 evaluation is recorded, and STATUS states the verified boundary. |

### Toolchain track (after P38, or earlier when instructed)

The language server and the check foundation for a future CSP adapter (SPEC Chapter 23) are **implemented** (L1–L6; plan archived as [draft/Design/2026-09-28 Language Server and Compiler Services.md](../../draft/Design/2026-09-28%20Language%20Server%20and%20Compiler%20Services.md)). The CSP adapter itself is not planned yet. The compiler milestone order is unchanged.

The 2026-09-28 LSP review fixed transport/lifecycle and snapshot-consistency defects and reduced hot-path allocations; its follow-up fixed lone-surrogate, request-ID and unreadable-change defects and made typing at one place independent of document length. [Measurements and verification](../../src/Benchmark/Lsp.md) include the sandbox-specific process-test reruns.

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

1. The spec-derived program source builds unchanged with the selected compiler configuration (Release by default; Debug explicitly) and runs at O0 and O2 with the expected stdout, exit code and stderr.
2. `src/backend/windows-x64/test-milestone<N>.ps1` exists and passes: original source only, one build/direct run per O0/O2; dedicated feature tests cover behavior and required rejection diagnostics.
3. Session verification passes: zero warnings, the full suite for the selected compiler configuration, and the harnesses of all earlier completed programs.
4. The owning SPEC sections exercised by the program have focused positive and negative tests; unsupported neighboring forms fail with a diagnostic, never with wrong code.
5. STATUS.md describes the new support boundary, and the milestone is marked DONE here and in `tests/milestones/README.md`.

A specification change that re-spells a completed program keeps it DONE when conditions 1–3 hold for the re-spelled source; `tests/milestones/README.md` records the re-spelling.

Features that a program's source does not use belong to the milestone that owns them, not to that program.

## 6. Next actions

1. Diagnostics track D1, then D2a and D2b (P31 paused), then D3 and D4.
2. Resume §4 with P31, P40 and P26. Independent P31 source work remains `sort()` using the existing heapsort and `init(! repeating:count:)`; `first`/`last` wait for P24, followed by the Property/object track.

Concurrent sessions use separate worktrees and stage only their own paths. Library bodies reached only through generic dispatch are collected through `CollectWitnesses`; keep that path in view when a body has no verified generic instance.

## 7. Open issues

| ID | Issue | Owner |
| --- | --- | --- |
| G4 | Weak and the rc/arc ownership family already have individual catalog identities with `SourceExpected: false`; source declarations, validation, Binding and runtime implementations remain unavailable (§22.1, §13.5.8–9). Catalog presence is not implemented support. | P34/P35 |
| G10 | Named function groups used as Function Types need selection, Origin and erasure paths (overloads, generics, members). | P26 |
| G20 | The user requested Dictionary in Kimigayo. Ordered storage algorithms compile from ordinary source, but public generic operation/result dispatch, reserve/growth and typed index/iteration bridges still reside in the compiler. The index bridge becomes `UniqIndexable<K>` (P27); iteration now uses the standard Dictionary entries over `Kimi.Storage` (P28). See `src/Kimi/Library/README.md`. | P31 |
| G22 | Two exclusive element borrows of one collection conflict even for distinct runtime indices (§3.4). A splitting API over `Kimi.Storage` (for example `pairUniq(first, second) -> Option<(uniq/E, uniq/E)>`) keeps disjointness out of the Get operation. Needs an API decision after P28 provides the storage boundary. | P40 |
| G34 | A Semantics application `s/U` to a Type other than the pair target is not formed as a struct Field or an associated family, and a struct Field of pair Type `s/T` instantiated with a reference (`Holder<ref/i32>.init(n@ref)`) is `UnsupportedOwnership_Kd` (SPEC §8.1.1, §8.1.2). | P39 area, unscheduled |
| G35 | An element write through a `uniq` reference to a built-in array or Array (`a[0] = v` for `a: uniq/[N of T]` or `uniq/Array<T>`) is `InvalidAssignment_Kd`, and an exclusive borrow of a Non-Copy element through such a reference is `TransferRequired_Kd`, although SPEC §3.4.1 selects the element through the reference; both would reuse the one replacement of the element write. | P27 area, unscheduled |
