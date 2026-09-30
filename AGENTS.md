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

The specification and implementation are not set in stone. The specification guides implementation, but implementation insights can also lead to revisions when the specification is contradictory, overly complex, or detrimental to performance. Let’s think carefully and refine both together, guided by the Kimigayo Principles, to build a better language.

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
- Record each formal-specification intake in `draft/INTEGRATED.md` in the same commit as the specification changes, identifying the integrated proposal sections and their target sections or commit. Implementation and test completion are not prerequisites for integration or freezing.
- If proposal items remain unresolved, mark the file 一部取り込み (partially integrated), record the integrated scope and open items, and freeze only the integrated scope. Unresolved proposal content may be edited only when explicitly instructed; do not change the integrated scope during those edits.
- Freeze the whole proposal file when every proposal item has a recorded disposition: incorporated into the formal specification, rejected with a reason, or transferred to an identified separate proposal. Record closure in the same commit as the final disposition (and any final specification changes). Use 取り込み済み (integrated) when at least one item was incorporated, or 完了 (closed) when all items were rejected or transferred; neither status implies implementation support.
- Preserve frozen proposal text, including typos. Append corrections or explanatory notes to `draft/INTEGRATED.md`; make subsequent specification changes in the formal specification and put new proposals in separate draft files. The integration register remains editable for status updates and appended notes; freezing a proposal does not freeze the register or reopen previously frozen files.
- `docs/STYLE.md` records non-normative Kimigayo coding conventions (naming, layout, API design, ownership, failure and documentation comments). It never overrides the specification. Update it when a Kimi library convention is adopted or changed, and keep it consistent with conventions stated in the specification, such as the naming pairs of §4.7.1.
- `docs/LIBRARY.md` lists the public Kimi declarations (Types, Contracts, groups, functions and Properties) with a brief guarantee each. Update it in the same commit that adds, removes or changes a public Kimi declaration. The specification remains the authority, and `src/Kimi/Library/README.md` covers embedding and implementation policy.
- `docs/dev/PLAN.md` records the current plan only: scope, working rules, current position, milestone order with completion conditions, next actions and open issues. Keep it under 200 lines. Deferred items (Appendix D) are not planned there.
- `docs/STATUS.md` summarizes product-wide implemented capabilities, verified support boundaries, and remaining limitations. Do not describe planned or unverified work as completed support. Update it only when a support boundary changes.
- `docs/dev/DIAGNOSTICS.md` records the compiler's internal diagnostic model, its common rules and the detail of the diagnostic workflow below. SPEC §23 remains the authority for observable diagnostic behavior.
- `docs/dev/PLAN_HISTORY.md` holds a few lines per session. Detailed evidence lives in commits and `artifacts/verify/`; records before the 2026-09-22 compaction are in git (`git show 32324537:PLAN_HISTORY.md`).
- The language is pre-alpha: a specification change needs neither a language-version bump nor breaking-change or migration documentation. Update the affected specification chapters, examples and milestone programs in place.

# Generated Files

- Put disposable repository work in `temp/`; it may be deleted when no build or test is running.
- Keep verification evidence in `artifacts/verify/`, measurements in `artifacts/benchmarks/`, and distribution packages in `artifacts/packages/`. Retain failed-run evidence too.
- Both roots are ignored by Git; retain or back up artifacts separately. Keep standard project-local `bin/` and `obj/` paths unchanged.

# Implementation Workflow

- Work in coherent units: reproducer, implementation, focused tests. Commit each verified unit with a descriptive message.
- Unit verification: `./scripts/verify.ps1 -Class <test classes> [-Fixtures '<pattern>'] [-Milestone <n>]` (non-incremental Release build with warnings as errors, related tests, related native O0/O2 fixtures and milestone harnesses). A direct `dotnet build` may leave analyzer warnings unreported to later incremental builds, so only the script's build counts as evidence.
- Session verification, once at the end of a session: `./scripts/verify.ps1 -Mode Session [...]` (Release build and full suite). Both modes use Release by default; pass `-Configuration Debug` explicitly for Debug verification. Builds, tests, fixture selection and milestone harnesses use the selected compiler configuration; native O0/O2 coverage is unchanged. Never edit sources while a build or verification run is in progress.
- Toolchain identity is checked by setup/update or `kimi toolchain verify`; add `-VerifyToolchain` to verify.ps1 to run it once before tests. Normal verification records it as not performed. Milestones build original sources once at O0 and O2 and execute each binary directly once; feature/rejection and CLI tests are separate. Use `run --no-build` for already-built CLI artifacts.
- Measure allocations only on hot paths and at milestone completion.

# Diagnostic Development Workflow

Apply this cycle to every change that adds or modifies a diagnostic, a check that reports diagnostics, recovery or suppression, or diagnostic publication and presentation. Reusing an existing diagnostic code does not exempt a new reporting path. A reported confusing, missing or misleading diagnostic also starts it. Details: [docs/dev/DIAGNOSTICS.md](docs/dev/DIAGNOSTICS.md) §10.

1. **Capture the intended problem.** Add a minimal reproducer or a mutation of a known-valid program with the intended failure and independently written expectations of the public records. Confirm that the case reaches the intended check, and add a valid counterpart against false positives.
2. **Review the explanation.** Check the code, severity and category, the primary range and label, the factual Reason, related locations, Note and Advice. The message must explain the problem on its own, and required facts must survive limits. Inspect representative CLI and language-server output when user-visible behavior changes.
3. **Exercise interactions.** Add focused cases for the relevant prerequisites and recovery, independent errors and input relations. The cause stays explained, independent problems stay visible, acceptance never depends on suppression or presentation, and every affected output adapter is covered.
4. **Repair and repeat.** Fix missing evidence, misleading explanations, wrong ranges and unwanted cascades at their source, then rerun and inspect again. Never weaken expectations or treat fewer diagnostics as an improvement. Keep the reproducer and expectations as regression tests.
5. **Verify and keep evidence.** Follow the implementation workflow; measure when a changed path is hot. Record reviewed behavior, intended public-output changes, results and remaining uncertainty in commits and `artifacts/verify/`.

A unit is complete only when its problem is explained accurately and understandably at the right locations, the interaction and output checks pass, and every quality defect observed in the unit is repaired and verified again. Passing tests alone does not establish clarity; otherwise record the unresolved condition and the next action without marking the unit complete.

# VS Code Extension (`src/kimi-ext/`)

For changes to the extension or its development, build and distribution configuration or scripts, follow [src/kimi-ext/AGENTS.md](src/kimi-ext/AGENTS.md).
