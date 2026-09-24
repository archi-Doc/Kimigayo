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

- **Review remediation (2026-09-24): IN_PROGRESS.** Native fixtures are isolated by verification run/configuration (`abcebbca`). Copy aggregate snapshots, whole-field reads, generic results, enum payloads and comparison evaluation order are verified (`c51bf54a`), resolving G11/G19; nested reference Loans are retained and warm comparison Binding remains allocation-free. Specification examples and Dictionary search semantics are being clarified. The remaining review items still require implementation and session verification; no new milestone is DONE.

- **Programs 31 and 33 authored (2026-09-24).** Dictionary entry ownership/order and exclusive object views/refinement now have specification-derived sources, expected output and separate verification scopes. Both pass syntax parsing but fail all eight Debug/Release O0/O2 Application probes at Binding; native tests are NOT_RUN. P31/P33 remain TODO and the next implementation milestone remains P30. Evidence: [authoring session](PLAN_HISTORY.md#programs31-33-authoring).
- **P30 session 1 (2026-09-24): IN_PROGRESS.** Ten verified units add recognized Equatable/Comparable declarations, primitive witnesses, user/generic comparison operators, preserved specialization and cleanup/effect checks, and zero-allocation warm operator Binding. Program 30 and its harness are authored with user approval; its four Debug/Release O0/O2 builds fail Binding on Tuple/shared-borrow composition (G18), while 16 independent rejection checks pass in each configuration. Copy aggregate referent reads remain G19. Evidence: [session](PLAN_HISTORY.md#p30-session1).
- **Programs 25–28 authored (2026-09-24).** Inheritance, general closures, Slice/Index/Range and user Iterable/Iterator now have independent specification-derived sources and README expectations. All four pass syntax parsing but fail Debug/Release O0/O2 Application builds at Binding; native tests are NOT_RUN. Full suites pass 12,119 tests per configuration, and final source catalog/alias checks pass 58 each. Evidence: [authoring session](PLAN_HISTORY.md#programs25-28-authoring). P25–P28 remain TODO; the next implementation milestone remains P30.
- **Implicit exclusive receivers and ObjectPayload (2026-09-24).** Both proposals (`draft/Changes/2026-09-23 Implicit Exclusive Receiver.md`, `2026-09-23 Object Payload.md`) are integrated into SPEC.md and its chapters (`cf96a774`), recorded in `draft/INTEGRATED.md` and frozen, and implemented: `Kimi.ObjectPayload`, the inherited `Self is not ObjectPayload` opt-out, Object Target/pair evidence and the Semantics admitted set replace Sealed as object-formation evidence (`c11e51c2`); Receiver Expressions are acquired implicitly, every other position keeps `@uniq`/`@objuniq`, one receiver shape per function group is enforced and Best Candidate compares explicit arguments only (`52ad793d`). Milestones 13/14/29/32, the UTF-8 example and the Milestone 29 harness use bare receivers. Boundaries are in STATUS. The next planned milestone is P30.
- **UTF-8 formatting integration complete (P32 DONE).** The proposal is integrated into the normative profile and affected chapters; its draft is frozen. The implementation, effect/allocation audits, example and Program 32 pass full-session verification. Program 32 passes 41 checks in each configuration; float encoders pass 15,104 exact-rational oracle cases at O0/O2. Evidence and boundaries are in STATUS.

- **Verified source HEAD** `d7aa1df1`. Debug/Release builds are warning-free; each full suite passes 12,169 tests (`bin/verify/20260924-010013-session-milestones31-33-authoring`). Compiler implementation is unchanged from `ad39b8cc`; its P30 session remains the latest native evidence: 48 Comparison O0/O2 executions and all 24 completed Release harnesses (1–22, 29 and 32; 1,232 checks). Those native suites were not rerun for authoring. P30 remains IN_PROGRESS; P29 and P32 are DONE.
- **Borrow Origin suffix:** contextual `during`, Optional attachment, grouping and Adaptation boundaries, source output and diagnostics are integrated and implemented. The library, affected milestone programs and tests use the new spelling; the proposal is recorded as integrated and frozen. Existing Origin, ownership and generation boundaries are unchanged.
- **P20 complete:** the Copy read (§3.3, §10.2, §13.4) reads a `ref/T`/`uniq/T` value as its Copy referent wherever a `T` is expected, omitted call defaults evaluate once per omission (§7.2.3), and Program 20 uses postfix Origin annotations and passes 55 harness checks per configuration. The completion assertion formerly recorded in §6.1 was a cascade from the unsupported call default; an internal invariant is now reported only for a body without an Unsupported issue.
- **Optional/try/discard:** the finalized rules are integrated into SPEC.md and its chapters. Parsing, Binding, analysis and native generation support the combinations in STATUS §3.1; the draft is unchanged. No NativeAOT run.
- **Programs 1–22, 29 and 32** have verified native execution. Programs 23–28, 30–31 and 33 are authored targets with failed build probes; 34–38 are not written yet. Source creation or a successful target run alone does not complete an implementation milestone.
- **P22 progress:** each concrete call context of a verified generic body is re-analyzed under its substitution (`OwnershipAnalysis.AnalyzeInstance`) and lowered by the ordinary `BodyLowering` under the caller-facing entry ABI (`LlvmEmitter.LowerInstances`). Every generic shape in the suites and Programs 8–11, 13, 14, 18 and 19 now monomorphizes (a full-suite probe found no refused instance); selected explicit specializations are called directly, and a refused instance fails generation instead of falling back. Growing keys, the depth bound and more than 1024 distinct contexts per body issue `GenerationResourceLimit_Kd` (§21.3.5). The shared storage writer, shared entry planning and the shared walk are deleted; `BodyLowering` validates every instance under its substitution, generic entry ABIs come from `FunctionAbiPool`, lowering compares call storage without Origins, and a refused instance is reported with its function and closed substitution. Self-containing Types are `InvalidInlineLayout_Kd` at Binding and the layout depth bound is `GenerationResourceLimit_Kd`.
- **Explicit transfer and exclusive borrow (2026-09-23):** the lending rule (`draft/Changes/2026-09-23 Explicit Transfer and Exclusive Borrow.md`) is integrated into SPEC.md and its chapters and implemented across the Parser (`@move`, `try` precedence 85), Binding (`TransferRequired_Kd`/`ExclusiveBorrowRequired_Kd`, shared `match`/`for` subjects, `try` payload transfers), ownership analysis, lowering and the runtime (`writeLine` borrows). Examples, Programs 1–24 and the harness rejections are re-spelled. Follow-ups: G14.
- **P21 complete (2026-09-23):** length slots in specialization keys and selection, inherited named (written or omitted) Origin binders, inherited defaults and Constraints, receiver specializations, compound arguments, no diagnostic cascade from an invalid specialization, header redeclarations diagnosed, and `test-milestone21.ps1` (61 Debug checks against the Copy-read spelling). Program 21 reads its references as Copy referents (G15 resolved) and passes its harness in Debug and Release.
- **P29 complete (2026-09-23, sessions 2–7):** handles, literals, metadata, owned parameters/results, mutation, indexed replacement, Index overloads, owning and shared iteration, slices, element borrows, checked capacity limits and allocation bounds run natively (sessions 2–6). Session 7 adds ownership-stage `UnsupportedOwnership_Kd` for zero-sized elements and shared `ref/string` iteration (G17, `abb90b86`), call-argument literals fitting owned/generic `Array<T>` parameters, destruction of a replaced whole Array (previously leaked) and of conditionally live Arrays, one definition per Array helper (`682b1287`), and owned generic `Array<T>` parameters/results (`901c25c2`). Program 29 is unchanged (SHA-256 `512684923B6A23722376FB8B845C565AB67A44ACD84D7DCDEE36AA8BA67246CE`) and passes 119 checks per configuration; all five warm compilation stages allocate zero bytes.
- **Review follow-ups (2026-09-23, session 4):** specialization target diagnostics and the by-original index (`b7ad3d8d`, `f3f18346`), `Array<T>` Owned derived from `T` and aggregates holding a handle rejected at ownership (`d2af9acb`), harness anchors checked by `Edit-KimiSource` with a required diagnostic per rejection (`b93f046c`), README check counts dropped and the diagnosis policy recorded (`20d14c7b`), and one diagnostic per invalidated Loan root (`0db6a7b4`, issue G13 closed).
- **P19 generation boundary:** associated-value and requirement-call bodies use concrete entries and verified conformance witnesses. Unlike the transitional P22 paths above, these entries must pass concrete ownership/lowering; they have no shared fallback.

## 4. Milestones (execution order)

States: TODO / IN_PROGRESS / DONE. A milestone is DONE only when every condition in §5 holds.

| Order | ID | Program subject | State | Specific acceptance beyond §5 |
| --- | --- | --- | --- | --- |
| 1 | P22 | Generic generation by monomorphization | DONE | Every generic body reaches generation as per-substitution concrete bodies through ordinary lowering; the shared generation path is removed (its walk remains as template validation only). Programs 8–11, 18 and 22 and all generic tests pass; growing keys, depth and oversized substitution sets issue `GenerationResourceLimit_Kd` (§21.3.5). |
| 2 | P19 | Contracts, associated Types, conditional/nested conformance | DONE | Associated identities normalize after container substitution; verified requirement mappings select concrete instances. Unchanged target and 53 checks pass in Debug/Release; full regressions and earlier harnesses pass. Broader Contract boundaries remain in STATUS. |
| 3 | P20 | Type/length/Origin inference, defaults, forwarding | DONE | Returned element borrows are read as Copy referents (§3.3, §13.4; issue G12 resolved); omitted call defaults evaluate once per omission (§7.2.3). Unchanged target and 55 checks pass in Debug/Release. Owned/borrow-producing defaults remain a STATUS limit owned by P23/P24. |
| 4 | P21 | Explicit full specialization | DONE | Length specialization, inherited named/omitted Origin binders, inherited defaults and Constraints, receiver and compound specializations, no diagnostic cascade from an invalid specialization. Unchanged target and 61 checks pass in Debug/Release; Program 21 reads references as Copy referents (G15 resolved 2026-09-23). |
| 5 | P29 | Dynamic Array | DONE | §4.7 capacity, mutation, Non-Copy elements, owning iteration and mandatory allocation bounds are verified; the Array part of G3 is settled. Unchanged Program 29 and 119 harness checks pass in Debug/Release O0/O2. Shared element/view access and iteration, call-argument literals, whole replacement and conditional destruction are verified; unsupported neighboring forms are ownership-stage `UnsupportedOwnership_Kd` (G17 resolved). |
| 6 | P32 | UTF-8 formatting and interpolation | DONE | Buffers, erased Writers, reserve effects, builtin/user/generic formatting, Text conversions, interpolation and `$tryWrite` pass focused, native/cost and full-session checks. Program 32 and its variants/rejections pass in Debug/Release O0/O2. |
| 7 | P30 | Equatable/Comparable Contracts | IN_PROGRESS | Primitive/user/generic calls/operators, Copy snapshots and Tuple/shared-borrow composition pass. Unchanged Program 30 passes the complete Debug harness; final Release/session verification and neighboring-form audit remain. |
| 8 | P31 | Dictionary | TODO | Authored target fails Binding on Dictionary formation/operations; implement equality, mutation, insertion order, iteration, Loans and allocation-free churn. |
| 9 | P23 | Basic Properties | TODO | Standard/custom/computed accessors over Copy values, permissions, evaluation order. |
| 10 | P24 | Ownership-bearing Properties | TODO | Non-Copy setters, getter results and temporaries, Contract witnesses. |
| 11 | P25 | Inheritance | TODO | Base storage, construction/destruction and inherited members. Finite layout limits use resource diagnostics (G7 resolved). |
| 12 | P26 | General closures and Callable | TODO | Composite/generic captures, function items and erasure (issue G10), indirect-call ABI. |
| 13 | P27 | General Slice/Index/Range | TODO | Partial/nested slices, bounds evaluation, retained Origins. |
| 14 | P28 | General Iterator/Iterable | TODO | User protocols, owned/borrowed elements, early-exit cleanup; general loop backedge/continue joins. |
| 15 | P33 | Exclusive objects and views | TODO | Authored target fails Binding on base construction/upcasts/refinement; complete base views, runtime tests/refinement, payload updates and dynamic destruction. |
| 16 | P34 | Shared object ownership (rc/arc) | TODO | Validate source declarations, Binding, ownership and runtime support for the already-cataloged ownership family (issue G4). |
| 17 | P35 | Weak | TODO | Downgrade/upgrade, expiration, cyclic construction, table release. |
| 18 | P36 | General static storage | TODO | First initialization, effects, cycles, shutdown. |
| 19 | P37 | Integrated processing application | TODO | Collections, borrows, iteration and closures combined. |
| 20 | P38 | Integrated core application | TODO | Properties, inheritance, objects and formatting combined. |

P22 (generation migration), P19 (completed first at the user's direction), P20, P21, P29 and P32 are done. P30 is in progress. Collections, comparison and formatting (P29–P32) precede Properties and objects because most later programs use them.

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

1. **P30 completion (G18):** recursive comparison composition passes 415 related tests, 146 native O0/O2 executions and the unchanged Debug harness (`bin/verify/20260924-021918-773-unit-comparison-composition-regressions`). Audit neighboring forms and finish Release/session verification; preserve the original Program 30.
2. **Review infrastructure and diagnostics:** record exact pending-program stages/diagnostic anchors, distinguish generation resource limits and suppress dependent cascades without hiding independent errors. Consolidate implicit witness calls for effects and generation.
3. **Review feature completion:** finish shared matching/iteration (G14), Dictionary storage/search/order/allocation guarantees (P31), then inheritance/object identity, content refinement and dynamic destruction (P33 prerequisites). Complete P30's unchanged harness and every §5 condition before marking it DONE. General ObjectCallCompatible inference remains deferred by Appendix D.

## 7. Open issues

| ID | Issue | Owner |
| --- | --- | --- |
| G3 | The Array part is verified by `DynamicArrayCostTest`/`DynamicArrayCapacityLimitTest` and native O0/O2 probes (session 6). Dictionary and the remaining general Slice operations still need their §4.6.8/§4.7 cost evidence; there is no public collection ABI. | P31/P27 |
| G4 | Weak and the rc/arc ownership family already have individual catalog identities with `SourceExpected: false`; source declarations, validation, Binding and runtime implementations remain unavailable (§22.1, §13.5.8–9). Catalog presence is not implemented support. | P34/P35 |
| G10 | Named function groups used as Function Types need selection, Origin and erasure paths (overloads, generics, members). | P26 |
| G14 | Shared Tuple iteration now Copies complete Copy components (including stored references) and borrows Non-Copy owned components with retained Origins. Shared structural/literal matching, string subjects, ref/string local storage, and general dependent shared-read families remain; use the common shared-read acquisition rule when extending these forms. | P28/P29 |
| G18 | Tuple/shared-borrow composition and the unchanged Program 30 Debug harness now pass; neighboring-form audit and full Release/session verification remain before P30 completion. Built-in Tuple operators and Contract mappings retain distinct floating semantics. | P30 |
