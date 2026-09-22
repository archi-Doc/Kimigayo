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

- **HEAD** `4ab7b271` plus this record. Debug/Release builds are warning-free; each full suite passes 11,218 tests (session verification `bin/verify/20260922-103947-session-p22-session-rest`; harnesses 1–12 and 14–18 pass after the 2026-09-22 harness variant fixes).
- **Programs 1–18** pass (O0/O2, target, variants and rejections). **Programs 19–21** exist but fail in Binding (see §4). Programs 22–38 are not written yet.
- **P22 progress:** each concrete call context of a verified generic body is re-analyzed under its substitution (`OwnershipAnalysis.AnalyzeInstance`) and lowered by the ordinary `BodyLowering` under the caller-facing entry ABI (`LlvmEmitter.LowerInstances`). Scalar functions, Never bodies, length-generic functions, generic struct constructors and field reads now monomorphize. Refused instances still use the shared path (`GenericStoragePlan*`, `LlvmModuleWriter.GenericStorage`/`SharedCalls`, `BodyLowering.Shared`); selected explicit specializations keep their selected body.

## 4. Milestones (execution order)

States: TODO / IN_PROGRESS / DONE. A milestone is DONE only when every condition in §5 holds.

| Order | ID | Program subject | State | Specific acceptance beyond §5 |
| --- | --- | --- | --- | --- |
| 1 | P22 | Generic generation by monomorphization | IN_PROGRESS | Every generic body reaches generation as per-substitution concrete bodies through ordinary lowering. Programs 8–11 and 18 and all existing generic tests pass without the shared path, which is then removed. Growing keys (`T -> Box<T>`) and oversized substitution sets produce resource diagnostics (§21.3.5). |
| 2 | P19 | Contracts, associated Types, conditional/nested conformance | TODO | Current `InvalidPattern_Kd` / `UnprovenConstraint_Kd` failures resolved by implementing the specified proofs, not by relaxing checks. |
| 3 | P20 | Type/length/Origin inference, defaults, forwarding | TODO | Generic returned element borrows dereference (no `UnsupportedBinding_Kd`); owned/borrowed defaults per §7.2. |
| 4 | P21 | Explicit full specialization | TODO | Length specialization and inherited default/Origin contracts, without cascading diagnostics. |
| 5 | P29 | Dynamic Array | TODO | §4.7 capacity, mutation, Non-Copy elements, owning iteration and mandatory allocation bounds; settles issue G3. |
| 6 | P30 | Equatable/Comparable Contracts | TODO | Generic requirement calls and composed comparisons. |
| 7 | P32 | Stringify and interpolation | TODO | Interpolation evaluation order and temporary cleanup; documented float formatting. |
| 8 | P31 | Dictionary | TODO | Equality, mutation, insertion order, iteration, Loans, allocation-free churn. |
| 9 | P23 | Basic Properties | TODO | Standard/custom/computed accessors over Copy values, permissions, evaluation order. |
| 10 | P24 | Ownership-bearing Properties | TODO | Non-Copy setters, getter results and temporaries, Contract witnesses. |
| 11 | P25 | Inheritance | TODO | Base storage, construction/destruction, inherited members; issue G7. |
| 12 | P26 | General closures and Callable | TODO | Composite/generic captures, function items and erasure (issue G10), indirect-call ABI. |
| 13 | P27 | General Slice/Index/Range | TODO | Partial/nested slices, bounds evaluation, retained Origins. |
| 14 | P28 | General Iterator/Iterable | TODO | User protocols, owned/borrowed elements, early-exit cleanup; general loop backedge/continue joins. |
| 15 | P33 | Exclusive objects and views | TODO | Base views, runtime tests/refinement, dynamic destruction. |
| 16 | P34 | Shared object ownership (rc/arc) | TODO | Kimi catalog entries for the ownership family (issue G4). |
| 17 | P35 | Weak | TODO | Downgrade/upgrade, expiration, cyclic construction, table release. |
| 18 | P36 | General static storage | TODO | First initialization, effects, cycles, shutdown. |
| 19 | P37 | Integrated processing application | TODO | Collections, borrows, iteration and closures combined. |
| 20 | P38 | Integrated core application | TODO | Properties, inheritance, objects and formatting combined. |

P22 comes first because monomorphization changes how every later generic body is generated. The Binding work of P19–P21 does not depend on P22 and may proceed if P22 is blocked. Collections, comparison and formatting (P29–P32) precede Properties and objects because most later programs use them.

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

1. **P22 remaining instance shapes:** calls inside instances (substitute the `BoundCall` via `Binding.InstantiateForwardedCall` and bind nested generic callees to their own instance entries; `BodyLowering.Calls`/`Closures` stay unsubstituted today), owned result joins (`choose<string>`: "Invalid slot result lifetime or arrival range"), reborrows and borrowed projections (programs 9, 11, 14). Find refusals with a temporary probe that records `Lower` failures in `LowerInstances`.
2. **P22 migration:** when no instance falls back, delete the shared path and its tests, drop the transitional `SharedEntries` bookkeeping, and add the §21.3.5 resource diagnostics (growing keys such as `T -> Box<T>`, oversized substitution sets).
3. **P22 program:** author `milestones/Milestone22.kimi` and its harness from the §21.3/§21.4 contract, after your instruction to create the program.

## 7. Open issues

| ID | Issue | Owner |
| --- | --- | --- |
| G3 | Internal Array/Dictionary/Slice storage must meet the §4.6.8/§4.7 costs; there is no public collection ABI. | P29 |
| G4 | The Kimi catalog lacks Weak and the rc/arc ownership family required by §22.1 and §13.5.8–9. | P34/P35 |
| G7 | `AggregateLayoutPool` uses 32-bit offsets and depth 64; larger layouts are rejected instead of supported or diagnosed per §21. | P25 |
| G10 | Named function groups used as Function Types need selection, Origin and erasure paths (overloads, generics, members). | P26 |
