# Kimigayo Compiler Plan

Current plan only: position, milestone order, completion conditions, next actions and open issues. Required behavior is in [SPEC.md](SPEC.md); implemented support is in [STATUS.md](STATUS.md); a few lines per session are in [PLAN_HISTORY.md](PLAN_HISTORY.md). Items deferred by [Appendix D](spec/appendices/D-deferred-features.md) are not planned here.

## 1. Scope

Implement the finalized language of SPEC.md (Chapters 1–22 and Appendix A) for the Windows x64 profile, excluding Appendix D. Progress is driven vertically by the [Milestone Programs](milestones/README.md): each milestone completes one program end to end (Binding, ownership, generation, native execution and required rejections).

- **Generics:** the initial profile monomorphizes (§21.3.1). Generic code sharing is deferred; milestone P22 replaces the existing shared generation path.
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

- **P23 active target (2026-09-24):** the user selected Program 23 independently of the table's execution order. Copy accessor calls, contextual storage and direct first construction placement now reach LLVM verification and native O0/O2 execution with the unchanged target output. Finish compound-operation order, permissions/temporary regressions, then Debug/Release session verification. P31 remains unfinished and is outside this session.
- **P31 session (2026-09-24):** the 60-minute unit window was 08:50:04–09:50:04 UTC (17:50:04–18:50:04 JST). Eleven verified units through `446565f3` add mandatory static duplicate-key diagnostics and move Dictionary algorithms into ordinary Kimigayo at the user's request. No new unit started after the cutoff; final verification finished at 09:58:57 UTC.
- **Verified implementation HEAD `446565f3`:** warning-free Debug/Release builds, 12,399 tests each, 104 fresh Dictionary O0/O2 executions and 26 Release harnesses (1,350 checks) in `bin/verify/20260924-095037-109-session-p31-kimigayo-library-session`. Program sources are unchanged. No NativeAOT or specification change; the unrelated user draft commit is preserved. Earlier broad native evidence remains in PLAN_HISTORY.
- **P30 DONE:** the unchanged target, all 43 Debug/Release harness checks, recursive witness/effect/NaN regressions and allocation probes pass.
- **P31 IN_PROGRESS:** the unchanged target and all 71 Debug/Release harness checks pass. Search, ordered links, slot reuse, initialization, reverse cleanup and shrink-to-fit compile from `DictionaryStorage.kimi`; warm compiler allocation probes remain zero. Public generic API/capacity migration, nonempty runtime literals and broader borrowed/nested storage forms remain.
- **P25/P33 IN_PROGRESS:** explicit base construction, layered destruction, concrete runtime Type tests, payload exchange and explicit base views pass focused/native checks. Program 25 now stops at inherited field ownership projection; Program 33 still stops at flow-refined member lookup.
- The program status table is [milestones/README.md](milestones/README.md); product support boundaries are in STATUS. Source authoring or a successful target alone does not complete a milestone.

## 4. Milestones (execution order)

States: TODO / IN_PROGRESS / DONE. A milestone is DONE only when every condition in §5 holds.

| Order | ID | Program subject | State | Specific acceptance beyond §5 |
| --- | --- | --- | --- | --- |
| 1 | P22 | Generic generation by monomorphization | DONE | Every generic body reaches generation as per-substitution concrete bodies through ordinary lowering; the shared generation path is removed (its walk remains as template validation only). Programs 8–11, 18 and 22 and all generic tests pass; growing keys, depth and oversized substitution sets issue `GenerationResourceLimit_Kd` (§21.3.5). |
| 2 | P19 | Contracts, associated Types, conditional/nested conformance | DONE | Associated identities normalize after container substitution; verified requirement mappings select concrete instances. Unchanged target and 53 checks pass in Debug/Release; full regressions and earlier harnesses pass. Broader Contract boundaries remain in STATUS. |
| 3 | P20 | Type/length/Origin inference, defaults, forwarding | DONE | Returned element borrows are read as Copy referents (§3.3, §13.4; issue G12 resolved); omitted call defaults evaluate once per omission (§7.2.3). Unchanged target and 55 checks pass in Debug/Release. Owned/borrow-producing defaults remain a STATUS limit owned by P23/P24. |
| 4 | P21 | Explicit full specialization | DONE | Length specialization, inherited named/omitted Origin binders, inherited defaults and Constraints, receiver and compound specializations, no diagnostic cascade from an invalid specialization. Unchanged target and 61 checks pass in Debug/Release; Program 21 reads references as Copy referents (G15 resolved 2026-09-23). |
| 5 | P29 | Dynamic Array | DONE | §4.7 capacity, mutation, Non-Copy elements, owning iteration and mandatory allocation bounds are verified; the Array part of G3 is settled. Unchanged Program 29 and 123 harness checks pass in Debug/Release O0/O2. Shared element/view access and iteration, call-argument literals, whole replacement and conditional destruction are verified; unsupported neighboring forms are ownership-stage `UnsupportedOwnership_Kd` (G17 resolved). |
| 6 | P32 | UTF-8 formatting and interpolation | DONE | Buffers, erased Writers, reserve effects, builtin/user/generic formatting, Text conversions, interpolation and `$tryWrite` pass focused, native/cost and full-session checks. Program 32 and its variants/rejections pass in Debug/Release O0/O2. |
| 7 | P30 | Equatable/Comparable Contracts | DONE | Primitive/user/generic, Tuple and borrow composition share finalized witnesses; Copy snapshots and NaN semantics are preserved. Unchanged source and 43 checks pass in Debug/Release, with focused/native/allocation and full-session regressions. |
| 8 | P31 | Dictionary | IN_PROGRESS | Unchanged target and 71 Debug/Release harness checks pass. Empty storage, all seven APIs, generic/zero-sized entries, owned indexing, shared/owning iteration, cleanup, static duplicate-key rejection and allocation/cost guarantees are verified. Finish the requested source migration, nonempty runtime literals, borrowed indexing and nested owning storage. |
| 9 | P23 | Basic Properties | IN_PROGRESS | Unchanged target executes in Debug at O0/O2. Finish compound-operation order, permissions/temporary regressions and full session verification. |
| 10 | P24 | Ownership-bearing Properties | TODO | Non-Copy setters, getter results and temporaries, Contract witnesses. |
| 11 | P25 | Inheritance | IN_PROGRESS | Explicit base prefix construction and layered/partial destruction pass. Finish implicit base calls and inherited member projections; unchanged target currently stops at ownership analysis of inherited fields. |
| 12 | P26 | General closures and Callable | TODO | Composite/generic captures, function items and erasure (issue G10), indirect-call ABI. |
| 13 | P27 | General Slice/Index/Range | TODO | Partial/nested slices, bounds evaluation, retained Origins. |
| 14 | P28 | General Iterator/Iterable | TODO | User protocols, owned/borrowed elements, early-exit cleanup; general loop backedge/continue joins. |
| 15 | P33 | Exclusive objects and views | IN_PROGRESS | Runtime Type tests, explicit concrete base upcasts, payload exchange and complete dynamic destruction/release pass focused native checks. Base erasure retains Owned/Origin/Loan requirements. Authored target still fails Binding on flow refinement; inherited field projections and the unchanged target harness remain. |
| 16 | P34 | Shared object ownership (rc/arc) | TODO | Validate source declarations, Binding, ownership and runtime support for the already-cataloged ownership family (issue G4). |
| 17 | P35 | Weak | TODO | Downgrade/upgrade, expiration, cyclic construction, table release. |
| 18 | P36 | General static storage | TODO | First initialization, effects, cycles, shutdown. |
| 19 | P37 | Integrated processing application | TODO | Collections, borrows, iteration and closures combined. |
| 20 | P38 | Integrated core application | TODO | Properties, inheritance, objects and formatting combined. |

P22, P19, P20, P21, P29, P32 and P30 are done. P31 is next. Collections, comparison and formatting precede Properties and complete object refinement because later programs use them.

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

Features that a program's source does not use belong to the milestone that owns them, not to that program.

## 6. Next actions

1. **Finish P31 source migration:** move the remaining generic operation/result dispatch and reserve/growth into Kimigayo over common memory/ownership primitives. Preserve equality/destructor effects, operation Abort locations and allocation bounds; keep backend support limited to physical representation and primitives (G20).
2. **Finish P31 language coverage:** implement runtime literal duplicate checks before value evaluation, borrowed-handle indexing and nested owning storage through existing acquisition/storage plans. Static mandatory duplicate checking is complete; extend the harness for the remaining forms.
3. **Resume the milestone order:** P23/P24 custom/computed and ownership-bearing Properties, then P25 inherited projections/implicit base calls, closure/Slice/Iterator work and P33 flow refinement. Preserve object identity and invalidate content facts on payload updates; general ObjectCallCompatible remains deferred.

## 7. Open issues

| ID | Issue | Owner |
| --- | --- | --- |
| G3 | Array and Dictionary growth, allocation, reuse and warm compilation costs have native/counter evidence (`DynamicArrayCostTest`, `DictionaryCostTest`). The remaining general Slice operations still need their §4.6.8 cost evidence; there is no public collection ABI. | P27 |
| G4 | Weak and the rc/arc ownership family already have individual catalog identities with `SourceExpected: false`; source declarations, validation, Binding and runtime implementations remain unavailable (§22.1, §13.5.8–9). Catalog presence is not implemented support. | P34/P35 |
| G10 | Named function groups used as Function Types need selection, Origin and erasure paths (overloads, generics, members). | P26 |
| G20 | The user requested Dictionary in Kimigayo. Ordered storage algorithms now compile from ordinary source, but public generic operation/result dispatch, reserve/growth and typed index/iteration bridges still reside in the compiler. Continue the migration over common primitives; the implementation direction is already approved and needs no new language rule. See `Kimi/Library/README.md`. | P31 |
