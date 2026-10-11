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

The specification and implementation are not set in stone. The specification guides implementation, but implementation insights can also lead to revisions: when the specification is contradictory, unsound or overly complex, or when a simpler, more consistent, more efficient or faster rule exists. Every revision must uphold the Kimigayo Principles and keep the language consistent and robust; these conditions are absolute. Let’s think carefully and refine both together to build a better language.

# Implementation Design

- Prefer fewer concepts, less duplication and less work over a smaller local diff. Fix the common cause by changing shared representations or responsibilities where needed; avoid unrelated redesign.
- Before coding, briefly identify the required rule or invariant, the cause of the gap, the component that should own the decision, and related implementations. Consult [CODEMAP](docs/dev/CODEMAP.md); consider deletion, consolidation and moving responsibilities before adding code.
- Put operations with the same meaning and contract under one owner. If none fits, consider moving or merging existing code before introducing a minimal component. An abstraction must express shared semantics and remove duplication, not merely wrap similar code or anticipate hypothetical uses.
- Make each semantic decision in its owning phase and reuse the result downstream. Each phase still checks the contracts of the IR it produces or receives. Replace source-shape special cases with common representations and rules; do not add `Supports*` gates or shape allowlists.
- Remove branches, state, conversions and old paths made redundant by the change in the same unit. Necessary staged removal belongs in the existing migration plan with an explicit removal condition.
- Before completion, review the diff for reuse, duplication and efficiency. Explain the common rule changed, what became unnecessary, and why any remaining growth is needed. Test another form governed by the same rule and an invalid counterpart where relevant.
- Implement core libraries, including Iterator and Dictionary, in Kimigayo where practical. Follow [STYLE](docs/STYLE.md); `[Kimi]` rules are required in `src/Kimi/Library` and `docs/spec/` examples, recommended elsewhere.

# Performance and Compiler Structure

- Preserve generated native-code allocation guarantees and O2 performance. Inside the compiler, additional allocations are allowed in initial or reused processing when they make equivalent work faster or reduce compiler code, with a reasonable increase in memory use. Measure time, allocations and retained memory; record the tradeoff and update compiler allocation regressions to reflect the adopted decision in the same commit.
- Follow [COMPILER_ARCHITECTURE](docs/dev/COMPILER_ARCHITECTURE.md) for dependencies, closed vocabularies, size caps and the old Analysis/Emission migration freeze. Permission to revise the specification does not authorize vocabulary or budget changes. Raise a cap only on owner instruction or to preserve native O2 performance and allocation guarantees, recording why.
- Record net lines per compiler area in compiler commits. Growth above 300 lines also explains what was replaced or removed and why the remaining additions are needed. At milestone completion, record area sizes, change hotspots and duplication candidates in `artifacts/benchmarks/`. Review repeatedly growing files for mixed responsibilities and duplication; resolve these before extending them again.
- Keep timing-only benchmark code in `src/Benchmark`, separate from functional and allocation/reuse regressions. Measure performance changes and milestone results on equivalent workloads under fixed conditions. Preserve failed evidence; never add warm-up or relax native allocation assertions to obtain a pass.

# Documentation

Write updates in English. Each document owns the following information; keep changes consistent with its owner.

| Document | Responsibility and update rule |
| --- | --- |
| [SPEC](docs/SPEC.md) and chapters | Language rules. Keep self-contained; drafts are not authority. |
| [IMPL](docs/IMPL.md) | Implementation contracts; never overrides language rules. |
| [COMPILER_ARCHITECTURE](docs/dev/COMPILER_ARCHITECTURE.md) | Phase responsibilities, dependencies, vocabularies, budgets and migration rules. |
| [VERIFICATION](docs/dev/VERIFICATION.md) | Verification commands, selection, measurements and evidence procedures. AGENTS defines work policy. |
| [CODEMAP](docs/dev/CODEMAP.md) | Navigation to owners, entry points and representative tests. Update changed or stale mappings in the same commit; no specification or support rules. |
| [PLAN](docs/dev/PLAN.md) | Current scope, order, completion conditions, next actions and open issues. Under 200 lines; deferred items belong in SPEC Appendix D. |
| [STATUS](docs/STATUS.md) | Verified support boundaries and remaining limits. Update when a boundary changes; never claim unverified support. |
| [PLAN_HISTORY](docs/dev/PLAN_HISTORY.md) | A few lines per session; detailed evidence belongs in commits and artifacts. |
| [SETTLED](docs/SETTLED.md) | Rejected design changes: problem, example, proposed fix and reason. Consult before design reviews; reopen only when the reason no longer holds, updating the entry with any affected spec change. |
| [STYLE](docs/STYLE.md) / [LIBRARY](docs/LIBRARY.md) | Conventions / public declarations and guarantees. Update with changed conventions or APIs; follow SPEC, including §4.7.1 naming pairs. [Library README](src/Kimi/Library/README.md) owns implementation policy. |

- Never narrow the specification to fit missing implementation; report Unsupported and record the boundary in STATUS and PLAN. Deliberate spec improvements need no separate approval: explain the rationale and preservation of the Principles, and update affected chapters, examples, tests and milestone programs together. Pre-alpha changes need no version bump or migration guide.
- Proposal creation or text changes require explicit instruction. An instructed intake or closure includes register updates and required moves. Create proposals only in `draft/Proposals`, prototypes in `draft/Sketches`; `draft/Design` and `draft/Decisions` remain frozen archives.
- Record each intake in `draft/INTEGRATED.md` with proposal-to-spec mappings in the same commit. For open proposals, mark 一部取り込み (partially integrated), list integrated and open scope, and freeze the integrated text. Close when every item is incorporated, rejected with a reason, or transferred to an identified proposal: move to `draft/Changes` as 取り込み済み (integrated) if anything was incorporated, otherwise `draft/Obsolete` as 完了 (closed).
- Never change frozen text, including typos. Record corrections in INTEGRATED; make later normative changes in the formal spec. Integration and closure do not require implementation or test completion and never imply support.

# Diagnostics

- Classify specification violations as language diagnostics, valid but unimplemented forms as the single location-only `Unsupported_Kd`, and internal contract violations as internal errors.
- Follow SPEC §23 and [DIAGNOSTICS](docs/dev/DIAGNOSTICS.md). Ordinary changes use the existing schema and required code, severity/category, location and factual cause; do not invent evidence, Advice or repair guarantees. Apply [DIAGNOSTIC_WORKFLOW](docs/dev/DIAGNOSTIC_WORKFLOW.md) only on explicit request for that workflow or a detailed diagnostic review. This limits work scope, not required diagnostic behavior.

# Verification and Delivery

- Serialize editing, builds, verification and commits within each worktree; use separate worktrees for parallel work or other unfinished changes. Never edit sources during builds or verification. Stage only the unit's changes.
- Work in reproducer, implementation and focused-test units. Select completion checks by changed behavior and files; combine requirements for mixed changes.

| Change | Required verification |
| --- | --- |
| Documentation only | Diff and reference review; no builds, tests or native verification. |
| Compiler, Kimi library or compiler tests | Related Unit checks and a successful final Session. Compiler Units include `CompilerSizeBudgetTest` and `ArchitectureRulesTest` as common mandatory checks; library changes include `KimiLibraryShapeTest`. Select these explicitly alongside related checks. |
| Benchmark / Playground | Final Session, including the whole-solution build, plus relevant execution or measurements. |
| Verification scripts, CI or tooling | Affected workflow checks under [verification scope](docs/dev/VERIFICATION.md#verification-scope). |
| VS Code extension, including build/distribution scripts | [Extension AGENTS](src/kimi-ext/AGENTS.md). |

- For feedback, incrementally build `tests/xUnitTest/xUnitTest.csproj` and run selected methods. Completion uses `scripts/verify.ps1`: non-incremental builds with warnings as errors, Release by default. Unit builds Kimi/tests; Session builds the solution and runs all functional and allocation/reuse regressions. Native fixtures and milestones require explicit selection even in Session. Do not run NativeAOT tests without explicit instruction.
- Reuse the owning test class and shared helpers; test through public acceptance, diagnostics, IR or MIR entry points at the lowest layer that observes the rule. Internal state assertions belong in the component's own rule tests. Avoid duplicate cases; emit native fixtures only for generated-code behavior. Add input-size scaling checks for affected analyses. Tag allocation/reuse tests `Purpose=Allocation`; include related ones for hot-path changes.
- Commit each unit after its required checks pass. A final Session can also satisfy the last Unit when it covers all required checks on the same inputs and configuration. Verify the final inputs successfully; do not repeat checks solely for mode names or unchanged inputs. Changed verification inputs require reverification under VERIFICATION's manifest policy.
- Before pushing, associate each Unit/Session-verified commit with its evidence using `scripts/verify-commit.ps1`. After the required final Session succeeds, automatically push the completed commits on the current branch to `origin`; never force-push. Changes that need no Session may be pushed after their own required checks. A failed required check leaves the work incomplete.
- Check native-toolchain identity at setup/update or explicitly with `-VerifyToolchain`; ordinary Verify does not imply this check. Use `run --no-build` for built CLI artifacts. See VERIFICATION for selection, native O0/O2 coverage, parallelism and measurement isolation.

# Artifacts

- Disposable files: `temp/`; delete only when no build/test is running. Preserve project-local `bin/` and `obj/`.
- Verification evidence, including failures: `artifacts/verify/`; measurement results: `artifacts/benchmarks/`; packages: `artifacts/packages/`.
- `temp/` and `artifacts/` are Git-ignored. Retain or back up required evidence separately.
