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

- **Verified source HEAD** `a2779d6a`. Debug/Release builds are warning-free; each full suite passes 11,488 tests. Session evidence: `bin/verify/20260923-023637-session-p29-session2b`; all 21 harnesses (1–20 and 22, Release, 1,011 checks) pass.
- **P20 complete:** the Copy read (§3.3, §10.2, §13.4) reads a `ref/T`/`uniq/T` value as its Copy referent wherever a `T` is expected, omitted call defaults evaluate once per omission (§7.2.3), and Program 20 runs unchanged (SHA-256 `EBC7433C627C4734D04736D9A82717C8776635EF520C21867995B7720307CD44`) with 55 harness checks per configuration. The completion assertion formerly recorded in §6.1 was a cascade from the unsupported call default; an internal invariant is now reported only for a body without an Unsupported issue.
- **Optional/try/discard:** the finalized rules are integrated into SPEC.md and its chapters. Parsing, Binding, analysis and native generation support the combinations in STATUS §3.1; the draft is unchanged. No NativeAOT run.
- **Programs 1–20 and 22** have verified native execution. P22 is complete: unchanged source (SHA-256 `F65CC4AC23ED1476157F5A86F4DC850081BCE8B5E72609D267180582F9CFF280`), Debug/Release O0/O2, 45 harness checks per configuration. **Program 21** retains its failed Binding probe. Programs 23–24 are authored targets with prior failed build probes; 25–38 are not written yet. Source creation does not complete an implementation milestone.
- **P22 progress:** each concrete call context of a verified generic body is re-analyzed under its substitution (`OwnershipAnalysis.AnalyzeInstance`) and lowered by the ordinary `BodyLowering` under the caller-facing entry ABI (`LlvmEmitter.LowerInstances`). Every generic shape in the suites and Programs 8–11, 13, 14, 18 and 19 now monomorphizes (a full-suite probe found no refused instance); selected explicit specializations are called directly, and a refused instance fails generation instead of falling back. Growing keys, the depth bound and more than 1024 distinct contexts per body issue `GenerationResourceLimit_Kd` (§21.3.5). The shared storage writer, shared entry planning and the shared walk are deleted; `BodyLowering` validates every instance under its substitution, generic entry ABIs come from `FunctionAbiPool`, lowering compares call storage without Origins, and a refused instance is reported with its function and closed substitution. Self-containing Types are `InvalidInlineLayout_Kd` at Binding and the layout depth bound is `GenerationResourceLimit_Kd`.
- **Explicit transfer and exclusive borrow (2026-09-23):** the lending rule (`draft/Changes/2026-09-23 Explicit Transfer and Exclusive Borrow.md`) is integrated into SPEC.md and its chapters and implemented across the Parser (`@move`, `try` precedence 85), Binding (`TransferRequired_Kd`/`ExclusiveBorrowRequired_Kd`, shared `match`/`for` subjects, `try` payload transfers), ownership analysis, lowering and the runtime (`writeLine` borrows). Examples, Programs 1–24 and the harness rejections are re-spelled. Follow-ups: G14.
- **P21 progress (2026-09-23, session 1):** length slots in specialization keys and selection, inherited named (written or omitted) Origin binders, inherited defaults and Constraints, receiver specializations, compound arguments, no diagnostic cascade from an invalid specialization, header redeclarations diagnosed, and `test-milestone21.ps1` (61 Debug checks against the Copy-read spelling). The committed program still spells `*selected` (G15), so P21 stays IN_PROGRESS.
- **P29 progress (2026-09-23, session 2):** Program 29 authored (`0a4e74f8`); `Array<T>` declared as a compiler-managed catalog struct and bound with Non-Copy classification, `length`/`capacity`/`indices` metadata, typed literals and explicit borrows (`66e08aca`). Ownership analysis rejects Array values as unsupported until the runtime lifecycle lands.
- **P19 generation boundary:** associated-value and requirement-call bodies use concrete entries and verified conformance witnesses. Unlike the transitional P22 paths above, these entries must pass concrete ownership/lowering; they have no shared fallback.

## 4. Milestones (execution order)

States: TODO / IN_PROGRESS / DONE. A milestone is DONE only when every condition in §5 holds.

| Order | ID | Program subject | State | Specific acceptance beyond §5 |
| --- | --- | --- | --- | --- |
| 1 | P22 | Generic generation by monomorphization | DONE | Every generic body reaches generation as per-substitution concrete bodies through ordinary lowering; the shared generation path is removed (its walk remains as template validation only). Programs 8–11, 18 and 22 and all generic tests pass; growing keys, depth and oversized substitution sets issue `GenerationResourceLimit_Kd` (§21.3.5). |
| 2 | P19 | Contracts, associated Types, conditional/nested conformance | DONE | Associated identities normalize after container substitution; verified requirement mappings select concrete instances. Unchanged target and 53 checks pass in Debug/Release; full regressions and earlier harnesses pass. Broader Contract boundaries remain in STATUS. |
| 3 | P20 | Type/length/Origin inference, defaults, forwarding | DONE | Returned element borrows are read as Copy referents (§3.3, §13.4; issue G12 resolved); omitted call defaults evaluate once per omission (§7.2.3). Unchanged target and 55 checks pass in Debug/Release. Owned/borrow-producing defaults remain a STATUS limit owned by P23/P24. |
| 4 | P21 | Explicit full specialization | IN_PROGRESS | Length specialization and inherited default/Origin contracts, without cascading diagnostics. Length slots, inherited named binders and inherited defaults bind and generate; the harness is authored; the program build waits on G15. |
| 5 | P29 | Dynamic Array | IN_PROGRESS | §4.7 capacity, mutation, Non-Copy elements, owning iteration and mandatory allocation bounds; settles issue G3. Program 29 and its README are authored; `Array<T>` binds with metadata and literals; storage, operations and generation are pending. |
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

P22 (generation migration), P19 (completed first at the user's direction) and P20 are done; the next milestone in order is P21. Collections, comparison and formatting (P29–P32) precede Properties and objects because most later programs use them.

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

1. **P21 (in progress):** resolve G15 (the five `*name` reads in `milestones/Milestone21.kimi` conflict with SPEC §13.3; the bare Copy read produces the README output exactly and `test-milestone21.ps1` passes 61 Debug checks against that spelling), then run the harness inside the session verification, mark the README program 21 row and the §5 conditions, and record the boundary in STATUS. Remaining P21 limits: a binder that the original uses only in its result cannot be omitted from the specialization signature; attribute specializations stay unsupported.
2. **P29 (in progress):** next unit is the Array runtime lifecycle: lay `Array<T>` out as {pointer, length, capacity} (AggregateLayout, mirroring the Slice sequence path), let ownership analysis support Array places with a free-on-cleanup operation (element destruction in reverse index order for cleanup-bearing T), lower the empty literal (zeroed handle), `length`/`capacity`/`indices` reads and `@ref`/`@uniq` borrows, and add `__kimi_array_grow`/`__kimi_array_free` to WindowsRuntime.ll.in. Then register the mutation operations (`reserve`, `append`, `insert`, `pop`, `remove`, `clear`, `shrinkToFit`) as catalog compiler functions on the Array declaration (new KimiLibraryContainer/CompilerFunctionKind entries validated like writeLine), add the `Index` Type for `^n`, and finish with owning iteration and `test-milestone29.ps1` (issue G3).
3. **Generic generation follow-ups (any session):** per-instantiation source rebinding is not planned; a refused instance is reported with its function and substitution, and every instance is validated by `BodyLowering` (the transitional walk is deleted). Remaining: G11 (whole Copy aggregate-field acquisition through a shared receiver) and the aggregate Copy referent read of item 5.
4. **Copy read and default follow-ups:** aggregate Copy referent reads (a storage copy through a borrowed aggregate, currently `UnsupportedOwnership_Kd`), default calls with receivers, borrowed arguments or nested omitted defaults, and the Loan-conflict diagnostic cascade (G13).
5. **P23/P24 programs:** `milestones/Milestone23.kimi` and `Milestone24.kimi` and their README expectations are authored; implement their Property operations and harnesses at P23/P24.
6. **Remaining optional/try combinations:** preserve the same rules as explicit Option/Result when extending representation support. Add Never enum payload layout and unreachable-arm plans (`OwnershipAnalysis.Enums`/`Match`, `AggregateLayout`), static scalar-reference storage (`ReferenceTypes`), and owning unnamed iteration under P28/P29. Custom getter execution follows P23/P24. Add native cleanup cases before removing the corresponding STATUS limits.

## 7. Open issues

| ID | Issue | Owner |
| --- | --- | --- |
| G3 | Internal Array/Dictionary/Slice storage must meet the §4.6.8/§4.7 costs; there is no public collection ABI. | P29 |
| G4 | The Kimi catalog lacks Weak and the rc/arc ownership family required by §22.1 and §13.5.8–9. | P34/P35 |
| G7 | `AggregateLayoutPool` uses 32-bit offsets and depth 64; larger layouts are rejected instead of supported or diagnosed per §21. | P25 |
| G10 | Named function groups used as Function Types need selection, Origin and erasure paths (overloads, generics, members). | P26 |
| G11 | Whole Copy aggregate-field acquisition through a shared receiver needs a concrete storage-copy plan; currently rejected with UnsupportedOwnership_Kd (AssociatedForwardingTest). | P23 |
| G15 | `milestones/Milestone21.kimi` reads its selected references with `*selected == 30` (five sites), but SPEC §13.3 defines prefix `*` only for raw pointers (§5.2); a `ref{source}/i32` value is read as its Copy referent by §3.3 (`selected == 30`). Proposed decision: change the five `*name` reads in Program 21 to bare `name` (user approval needed to edit the program). | P21 |
| G14 | Shared-subject matching and iteration: structural/literal patterns through a borrowed subject (`match x` on an owned Non-Copy Place, `ref/E` parameters), `string` subjects, tuple bindings over a bare array iteration and `ref/string` locals are `UnsupportedBinding_Kd`; programs write `match x@move` / `for (a, b) in values@move`. | P28/P29 |
| G13 | After a comparison-Loan conflict, every later operation of the body reports `ComparisonLoanConflict_Kd` (the Program 13 harness rejection MovedSamples yields dozens of diagnostics for one fault); report one diagnostic per conflicting Loan and stop the cascade. | P29 |
