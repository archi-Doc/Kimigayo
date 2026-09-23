# Coding Guidelines

- Minimize memory allocations and optimize code for performance wherever practical.
- Update `SPEC.md` and `STATUS.md` as needed to reflect the changes made. Write all updates in English.
- Do not automatically update files in the `draft` folder unless explicitly instructed.
- Do not run NativeAOT tests unless explicitly specified.

# Documentation Responsibilities

- `SPEC.md` and its referenced specification chapters define required language behavior. Implementation limitations must not weaken these requirements.
- The `draft` folder contains proposals. Once finalized, their content is incorporated into `SPEC.md` and its referenced specification chapters. This flow is one-way: do not propagate changes from the formal specification back to `draft`. The formal specification must be self-contained and must not reference or depend on `draft`; do not use `draft` as an authority for required language behavior.
- When a `draft` file has been incorporated into `SPEC.md`, record it as 取り込み済み (integrated) in `draft/INTEGRATED.md` with its target sections or commit, and freeze the file: it is not edited afterwards.
- `PLAN.md` records the current plan only: scope, working rules, current position, milestone order with completion conditions, next actions and open issues. Keep it under 200 lines. Deferred items (Appendix D) are not planned there.
- `STATUS.md` summarizes product-wide implemented capabilities, verified support boundaries, and remaining limitations. Do not describe planned or unverified work as completed support. Update it only when a support boundary changes.
- `PLAN_HISTORY.md` holds a few lines per session. Detailed evidence lives in commits and `bin/verify/`; records before the 2026-09-22 compaction are in git (`git show 32324537:PLAN_HISTORY.md`).
- The language is pre-alpha: a specification change needs neither a language-version bump nor breaking-change or migration documentation. Update the affected specification chapters, examples and milestone programs in place.

# Implementation Workflow

- Work in coherent units: reproducer, implementation, focused tests. Commit each verified unit with a descriptive message.
- Unit verification: `./verify.ps1 -Class <test classes> [-Fixtures '<pattern>'] [-Milestone <n>]` (Debug build with warnings as errors, related tests, related native O0/O2 fixtures and milestone harnesses).
- Session verification, once at the end of a session: `./verify.ps1 -Mode Session [...]` (Debug and Release builds and full suites). Never edit sources while a build or verification run is in progress.
- Measure allocations only on hot paths and at milestone completion.
