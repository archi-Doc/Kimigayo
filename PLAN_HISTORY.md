# Kimigayo Plan History

A few lines per session. Evidence lives in commits and `bin/verify/` (earlier runs: `bin/verification-*`, `bin/plan-execution/`, `bin/milestone*-work/`). The full detailed records up to 2026-09-22 are preserved in git: `git show 32324537:PLAN_HISTORY.md` (and `:PLAN.md`, `:STATUS.md` for the previous plan and status).

## Sessions

| Date | Summary |
| --- | --- |
| 2026-09-09 – 09-16 | Parser, Koto syntax and reload; Binding foundations; scalar, string, aggregate, match and cleanup execution (examples); Programs 1–5. |
| 2026-09-17 | Completion plan with M1–M15 / I1–I34 (units 1–32); default-expression and ownership continuations; Kimi library organization; Programs 6–11. |
| 2026-09-18 | Ownership joins and terminal guards (units 33–67); Programs 12–15; 38-program roadmap. |
| 2026-09-19 | Program 16; source modules and Library inspection; NativeRequirements/`#LibraryImport` validation; language test runner. |
| 2026-09-20 | Independent Documentation Markdown parser and product switch (DM1–DM6); import symbol agreement; unsafe function values; call borrow reservations. |
| 2026-09-21 | Origin system redesign integrated; Programs 17 and 18. |
| 2026-09-22 | Named argument boundary; direct foreign import calls; raw pointer operations and C layout (T26s–T26ah); final suites 11,212 tests per configuration. |
| 2026-09-22 | Restructuring: initial profile monomorphizes and generic code sharing is deferred (§21.3.1, Appendix D); Mod host deferred (§20.7, Appendix D); PLAN rebuilt around Milestone Programs with finite completion conditions (old M/I/T IDs superseded); PLAN/STATUS/history compacted; `verify.ps1` added; per-unit commits authorized. |
| 2026-09-22 | P22 probe (reverted, no code commit): lowering a verified generic body with substituted Place Types and signature Types through the ordinary `BodyLowering` fails for every existing generic fixture, because the verified plan itself depends on the Types (`CopyOrMove` acquisitions, `ScalarResult` value flow such as Phi vs. slot arrivals). P22 next action changed to per-substitution ownership analysis. Session verification passed at `fdaeed9b` (11,212 tests per configuration; programs 8–11 and 18 Release). |
| 2026-09-22 | P22 units `131c1b38` (per-substitution ownership analysis and concrete instance lowering; specializations preserved) and `4ab7b271` (struct constructors/field reads). 11,218 tests per configuration; 195 generic-entry fixtures native O0/O2; harnesses 5 and 16 fail on stale variants that fail the same way at `fdaeed9b` (issue H1). |
| 2026-09-22 | Harness variant fixes (user-approved): milestone 5 `ImmediateTemporary` and `MissingStoredOrigin` use the current `ref{origin}/T` syntax, milestone 16 `Values` replaces the current `Cell.init(n)`; both harnesses now reject mutations that no longer change the source. Release harnesses pass (5: 47 checks, 16: 57 checks). |

## Retained anchors

Links from other documents point here. Each record's full text is in `git show 32324537:PLAN_HISTORY.md`.

<a id="program14-completion"></a>
- **Program 14** completed 2026-09-18: generic Slice pipeline, Iterator, exclusive borrowed capture, obj creation/Move/destruction.

<a id="program15-completion"></a>
- **Program 15** completed 2026-09-18: initialization/Move/Loan joins, loop backedges, repair before continue.

<a id="program16-completion"></a>
- **Program 16** completed 2026-09-19: external Origin forwarding, intersections, exclusive reborrow.

<a id="program17-completion"></a>
- **Program 17** completed 2026-09-21: static element updates, exchange/swap/replace, ordered cleanup.

<a id="program18-completion"></a>
- **Program 18** completed 2026-09-21: composite generic values, Copy acquisition and per-Type destruction.

<a id="units62-67-verification"></a>
- **Units 62–67 audit** 2026-09-18: terminal guards and owned Patterns with full regressions.

<a id="programs38-restructure"></a>
- **38-program roadmap** 2026-09-18: program 20 split into 20 (inference/defaults) and 21 (specialization); later numbers shifted.

<a id="programs18-20-design"></a>
- **Programs 18–20 authoring** 2026-09-18: specification-derived targets and initial failing build probes.

<a id="documentation-markdown-product-switch-20260920"></a>
- **Documentation Markdown product switch** 2026-09-20: the compiler uses the independent parser; Markdig remains only in comparison tests and benchmarks.
