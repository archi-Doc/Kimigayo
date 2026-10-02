# Kimigayo Principles — A Programming Language for AI

> Types are everything in programming languages; words are everything in design.
> 
>**Shape ideas with words; let types carry the weight.**

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

- Consult [docs/dev/CODEMAP.md](docs/dev/CODEMAP.md) before compiler changes. It maps features and entry points to source and representative tests. Update it in the same commit for added/moved features, changed phase responsibilities or entry points, or stale references. Keep it concise and navigation-only, without spec rules or support status.
- `docs/SPEC.md` and its chapters define required behavior; implementation limits must not weaken it. The formal spec must be self-contained: never reference, depend on, or treat `draft` proposals as authority. Integrate finalized proposals into the spec; never propagate spec changes back to proposals.
- Record every spec intake in `draft/INTEGRATED.md` in the same commit as the spec changes, mapping proposal sections to target sections or commit. Integration and freezing do not require completed implementation or tests.
- For unresolved proposals, mark 一部取り込み (partially integrated), record integrated scope and open items, and freeze only that scope. Edit unresolved content only on explicit instruction, preserving frozen scope.
- Freeze an entire proposal once every item is recorded as incorporated, rejected with a reason, or transferred to an identified separate proposal. Record closure with the final disposition and any final spec changes in the same commit: 取り込み済み (integrated) if any item was incorporated; otherwise 完了 (closed). Neither implies implementation support.
- Create every new draft proposal, change or design, in `draft/Proposals`, the only working folder. In the commit that closes one, move it to `draft/Changes` (取り込み済み) or `draft/Obsolete` (完了). `draft/Design` and `draft/Decisions` are frozen archives that take no new files; prototype sources go in `draft/Sketches`. Folder roles: `draft/INTEGRATED.md`.
- Preserve frozen text, including typos. Append corrections/notes to `draft/INTEGRATED.md`, make later spec changes in the formal spec, and place new proposals in separate draft files. The register stays editable for status updates and appended notes; freezing proposals never reopens previously frozen files.
- `docs/SETTLED.md`: changes the design has deliberately decided not to make, each with its problem, a short example, the proposed fix and the reason not applied. Consult it before reviewing the spec or proposing changes; do not re-raise an entry unless its reason no longer holds. When a spec change invalidates a reason, update or remove the entry in the same commit. Non-normative; never overrides the spec.
- `docs/STYLE.md`: non-normative conventions for naming, layout, API design, ownership, failure and doc comments. Update for adopted/changed Kimi library conventions; stay consistent with the spec, including §4.7.1 naming pairs, and never override it.
- `docs/LIBRARY.md`: public Kimi declarations (Types, Contracts, groups, functions, Properties), each with a brief guarantee. Update in the same commit as declaration additions, removals or changes. The spec is authoritative; `src/Kimi/Library/README.md` covers embedding and implementation policy.
- `docs/dev/PLAN.md`: current scope, working rules, position, milestone order/completion conditions, next actions and open issues. Keep under 200 lines; exclude deferred items (Appendix D).
- `docs/STATUS.md`: product-wide implemented capabilities, verified support boundaries and remaining limits. Update only when a support boundary changes; never report planned/unverified work as supported.
- `docs/dev/DIAGNOSTICS.md`: internal diagnostic model, common rules and workflow details. SPEC §23 governs observable behavior.
- `docs/dev/PLAN_HISTORY.md`: a few lines per session; detailed evidence belongs in commits and `artifacts/verify/`. Pre-2026-09-22-compaction records: `git show 32324537:PLAN_HISTORY.md`.
- Pre-alpha spec changes need no language-version bump, breaking-change or migration docs. Update affected chapters, examples and milestone programs in place.

# Generated Files

- Put disposable work in `temp/`; delete only when no build/test is running.
- Store verification evidence (including failures) in `artifacts/verify/`, measurements in `artifacts/benchmarks/`, and packages in `artifacts/packages/`.
- `temp/` and `artifacts/` are Git-ignored; retain/back up artifacts separately. Preserve project-local `bin/` and `obj/` paths.

# Implementation Workflow

- Work in units of reproducer, implementation and focused tests; formally verify each before committing with a descriptive message.
- Commit each completed unit and push the current branch to `origin` without waiting to be asked. Push only commits whose required verification passed, and never force-push. Stage only the files the unit changed, since other sessions may share this working tree.
- For intermediate feedback, incrementally build `tests/xUnitTest/xUnitTest.csproj` and directly run selected test methods. Do not run formal Verify after every edit. Feedback is not completion evidence: only Verify's build counts, since direct `dotnet build` can hide analyzer warnings from later incremental builds.
- Unit: `./scripts/verify.ps1 -Class <test classes> [-Fixtures '<pattern>'] [-Milestone <n>]` — non-incremental Release build of Kimi/tests with warnings as errors, related tests, native O0/O2 fixtures and milestone harnesses. Benchmark/Playground changes also require the whole-solution Session build before completion.
- Session, once at session end: `./scripts/verify.ps1 -Mode Session [...]` — non-incremental whole-solution Release build and all functional and allocation/reuse regressions.
- Both modes default to Release; use `-Configuration Debug` explicitly for Debug. The selected compiler configuration governs builds, tests, fixture selection and milestone harnesses; native O0/O2 coverage stays unchanged. Native parallelism: `-NativeParallel` (default up to 4; 1 for serial). Never edit sources during builds or verification.
- Toolchain identity: setup/update or `kimi toolchain verify`; Verify's `-VerifyToolchain` checks once before tests, otherwise records it as not performed. Milestones build original sources once per O0/O2 and execute each binary directly once; feature/rejection and CLI tests remain separate. Use `run --no-build` for built CLI artifacts.
- Separate functional regressions, allocation/reuse regressions (zero allocation, retained capacity, storage bounds), and opt-in performance measurements (timing, throughput, scaling). Tag allocation/reuse tests `Purpose=Allocation`; unmarked tests are functional. Unit includes both by default; narrow focused checks with `-TestPurpose Functional` or `Allocation`. Include related allocation/reuse regressions for hot-path changes. Session always runs both in full.
- Keep timing-only repetition and measurement reports in `src/Benchmark`, outside normal Verify. Run relevant measurements explicitly for performance changes and at milestone completion. Share regression workload inputs where applicable; keep warm-up/measurement conditions fixed; never relax allocation assertions or warm until a failing measurement passes. Commands/isolation: [docs/dev/VERIFICATION.md](docs/dev/VERIFICATION.md).

# Diagnostic Development Workflow

Apply to additions/changes in diagnostics, reporting checks, recovery, suppression, publication or presentation, including new paths reusing existing codes, and reports of confusing, missing or misleading diagnostics. Details: [docs/dev/DIAGNOSTICS.md](docs/dev/DIAGNOSTICS.md) §10.

1. **Capture.** Add a minimal reproducer or known-valid program mutation with the intended failure and independently written public-record expectations. Confirm it reaches the intended check; add a valid counterpart against false positives.
2. **Review.** Check code, severity/category, primary range/label, factual Reason, related locations, Note and Advice. Decide whether each fix is a repair candidate or Advice; a candidate's verified conditions are only those established from the check's facts, and a candidate with a refuted condition is not offered. The message must stand alone and retain required facts under limits. Inspect representative CLI and language-server output for user-visible changes.
3. **Exercise interactions.** Add focused cases covering relevant prerequisites, recovery, independent errors, input relations and every affected output adapter. Keep causes explained and independent problems visible; acceptance must never depend on suppression or presentation.
4. **Repair and repeat.** Fix missing evidence, misleading explanations, wrong ranges and unwanted cascades at their source; rerun and inspect. Never weaken expectations or equate fewer diagnostics with improvement. Retain reproducers/expectations as regressions.
5. **Verify and record.** Follow the implementation workflow; measure changed hot paths. Record reviewed behavior, intended public-output changes, results and uncertainty in commits and `artifacts/verify/`.

Completion requires accurate, understandable explanations at correct locations, passing interaction/output checks, and repair and reverification of every observed quality defect. Tests alone do not establish clarity. Otherwise, record unresolved conditions and next actions; leave the unit incomplete.

# VS Code Extension (`src/kimi-ext/`)

For extension changes, including development/build/distribution configuration or scripts, follow [src/kimi-ext/AGENTS.md](src/kimi-ext/AGENTS.md).
