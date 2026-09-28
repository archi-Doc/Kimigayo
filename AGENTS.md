# Kimigayo Principles — A Programming Language for AI

1. **One Concept, One Canonical Form**
   Give each concept and operation one clear, consistent form. Allow alternatives only when they provide a distinct practical benefit.

2. **Local Reasoning**
   Make meaning and legality understandable from local code, explicit context, and published contracts. Keep inference and resolution bounded and predictable.

3. **Explicit Semantics**
   Express ownership, failure, conversion, and dependency semantics in syntax or contracts. Allow omission only when meaning remains locally unambiguous.

4. **Compiler Server Protocol**
   Provide a structured interface for:

   - **Diagnostics:** causes, related locations, and repair candidates with explicit preconditions and guarantees.
   - **AST Inspection and Manipulation:** inspect syntax and semantics, apply edits against identified source snapshots, and produce reviewable source changes.
   - **Verifiable Changes:** associate checks, tests, and measurements with exact source and configuration, reporting outcomes and remaining uncertainty.

# Coding Guidelines

- Minimize memory allocations and optimize code for performance wherever practical.
- Implement Kimi's core libraries, including Iterator and Dictionary, in Kimigayo rather than hand-written LLVM IR whenever possible.
- Update `docs/SPEC.md` and `docs/STATUS.md` as needed to reflect the changes made. Write all updates in English.
- Do not automatically update files in the `draft` folder unless explicitly instructed.
- Do not run NativeAOT tests unless explicitly specified.
- Follow `docs/STYLE.md` when writing Kimigayo code. Rules tagged `[Kimi]` are required in `src/Kimi/Library` and in code examples under `docs/spec/`; elsewhere they are recommended.

# Documentation Responsibilities

- `docs/SPEC.md` and its referenced specification chapters define required language behavior. Implementation limitations must not weaken these requirements.
- The `draft` folder contains proposals. Once finalized, their content is incorporated into `docs/SPEC.md` and its referenced specification chapters. This flow is one-way: do not propagate changes from the formal specification back to `draft`. The formal specification must be self-contained and must not reference or depend on `draft`; do not use `draft` as an authority for required language behavior.
- When a `draft` file has been incorporated into `docs/SPEC.md`, record it as 取り込み済み (integrated) in `draft/INTEGRATED.md` with its target sections or commit, and freeze the file: it is not edited afterwards.
- `docs/STYLE.md` records non-normative Kimigayo coding conventions (naming, layout, API design, ownership, failure and documentation comments). It never overrides the specification. Update it when a Kimi library convention is adopted or changed, and keep it consistent with conventions stated in the specification, such as the naming pairs of §4.7.1.
- `docs/LIBRARY.md` lists the public Kimi declarations (Types, Contracts, groups, functions and Properties) with a brief guarantee each. Update it in the same commit that adds, removes or changes a public Kimi declaration. The specification remains the authority, and `src/Kimi/Library/README.md` covers embedding and implementation policy.
- `docs/dev/PLAN.md` records the current plan only: scope, working rules, current position, milestone order with completion conditions, next actions and open issues. Keep it under 200 lines. Deferred items (Appendix D) are not planned there.
- `docs/STATUS.md` summarizes product-wide implemented capabilities, verified support boundaries, and remaining limitations. Do not describe planned or unverified work as completed support. Update it only when a support boundary changes.
- `docs/dev/PLAN_HISTORY.md` holds a few lines per session. Detailed evidence lives in commits and `artifacts/verify/`; records before the 2026-09-22 compaction are in git (`git show 32324537:PLAN_HISTORY.md`).
- The language is pre-alpha: a specification change needs neither a language-version bump nor breaking-change or migration documentation. Update the affected specification chapters, examples and milestone programs in place.

# Generated Files

- Put disposable repository work in `temp/`; it may be deleted when no build or test is running.
- Keep verification evidence in `artifacts/verify/`, measurements in `artifacts/benchmarks/`, and distribution packages in `artifacts/packages/`. Retain failed-run evidence too.
- Both roots are ignored by Git; retain or back up artifacts separately. Keep standard project-local `bin/` and `obj/` paths unchanged.

# Implementation Workflow

- Work in coherent units: reproducer, implementation, focused tests. Commit each verified unit with a descriptive message.
- Unit verification: `./scripts/verify.ps1 -Class <test classes> [-Fixtures '<pattern>'] [-Milestone <n>]` (non-incremental Debug build with warnings as errors, related tests, related native O0/O2 fixtures and milestone harnesses). A direct `dotnet build` may leave analyzer warnings unreported to later incremental builds, so only the script's build counts as evidence.
- Session verification, once at the end of a session: `./scripts/verify.ps1 -Mode Session [...]` (Debug and Release builds and full suites). Never edit sources while a build or verification run is in progress.
- Measure allocations only on hot paths and at milestone completion.

# VS Code Extension (`src/kimi-ext/`)

- Keep extension source, regression tests, lockfile and build configuration in `src/kimi-ext/`. Root `.vscode/` contains its development launch/task configuration.
- Maintain extension usage and QuickStart in the root `README.md`, under **Visual Studio Code**. Packaging generates the extension's README and LICENSE from the root documents; do not edit or commit these generated copies.
- Run `npm --prefix src/kimi-ext ci`, then `npm --prefix src/kimi-ext test` for extension changes. Before packaging a release, run `npm --prefix src/kimi-ext run test:integration` with a managed Kimi executable and its toolchain; integration tests use isolated VS Code profiles. These checks complement compiler verification when compiler code also changes.
- Use `npm --prefix src/kimi-ext run package` to build a VSIX. Keep dependencies, compiled output, test profiles and VSIX files out of Git; exclude development-only files from the VSIX.
- Kimi.exe owns toolchain discovery and management. Do not duplicate that logic in the extension.
- Extension versions use `major.minor.patch`. Automatic or agent-initiated releases may increment only `patch`; change `major` or `minor` only on explicit user instruction, including for feature additions.
- Run `npm --prefix src/kimi-ext run version:patch` once per new release. It updates the package and lockfile without a Git tag. Keep `src/kimi-ext/CHANGELOG.md`, the VSIX and the installed version consistent; compilation, tests, packaging retries and reinstalls do not increment the version.
