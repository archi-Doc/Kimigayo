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

Priorities: SPEC correctness and the native O2 and allocation guarantees are constraints. Within them, a coherent structure comes first: each concept has one owner, one representation and one canonical path. Then less code, then faster compilation. A small diff, low test churn or ease of review are not criteria.

- **Diagnose from evidence.** Establish the failing condition and its cause from the code and a reproducer before designing; label an unverified hypothesis as one in designs, commits and reports, never as the cause.
- **Design before code.** Unless the change is local (one owner; no new branch, parameter, state or table), first write a short design: the SPEC rule or invariant; the root cause (the representation or responsibility that allowed the gap); the decision's owner; every site sharing the cause, found by search ([CODEMAP](docs/dev/CODEMAP.md), canonical components); the options, including the local patch and why it is rejected; what will be deleted; expected net lines and performance. Someone other than the implementer reviews it before coding. A delegating session puts the standing request, the design, the completion conditions and the required checks in the prompt; an implementer who finds the design wrong stops and reports instead of patching around it.
- **Record design decisions.** A new representation, component, vocabulary entry or temporary structure, or a responsibility moved between phases, is decided in the design step by the planning session (by the owner where this file or COMPILER_ARCHITECTURE requires it) and recorded in [DECISIONS](docs/dev/DECISIONS.md) in the commit that first implements it; an implementer never introduces one on its own. For a decision that changes a shared representation or crosses phases, compare two independently written designs and choose the one with fewer concepts and less code. Before designing, read the DECISIONS and SETTLED entries for the area; a design that departs from one adds an entry superseding it and states which reason no longer holds.
- **Fix causes in shared structure.** Changing the representation or responsibility that causes a defect is in scope, even across phases; only changes the cause does not require are unrelated redesign. Findings outside the unit's cause become open issues or separate units, not fixes in passing. Each decision is made once, by its owning phase; a consumer lacking a fact has the owner publish it instead of re-deriving it from syntax or another phase's state. Each IR has one validator, owned by its producer (MIR: `MirValidator`, all configurations); consumers trust validated IR without re-checking it, and a broken contract is `InternalInvariant_Kd` without recovery paths.
- **Create a concept when a decision, fact or shape recurs.** Operations with the same meaning and contract share one owner. Name the concept in SPEC terms, state its contract (inputs, invariants, producers, consumers), migrate every existing use in the same unit and register it as a canonical component. Prefer data over branches: a new form adds a table or plan row, not a case. No wrappers without semantics, single-implementation interfaces or utility bags.
- **Signs of a symptom fix** (stop and redo the analysis): an extra clause in an existing predicate or switch arm for one form; a new bool, mode or optional parameter steering an existing function; a side table keyed by node or symbol for one feature; a consumer deciding by syntax class what its owner should publish; suppressing duplicate or derived reports afterwards; a second copy of a rule in another phase, a `Supports*` gate or a shape allowlist; a try-then-fallback chain; a catch-all, a silent default or a fallback value that hides a failure; a temporary structure without a removal condition.
- A second fix for a related symptom in the same area, or a unit revising the previous unit's rule, means the cause was missed: analyze one level higher and replace both fixes.
- If the common fix exceeds the unit (frozen paths, stage scope, an owner decision), do not patch locally: keep the located `Unsupported_Kd` or the open issue, record the proposed common change in PLAN and report it. A local patch is allowed only for wrong code that cannot wait, recorded with its removal condition.
- **Keep the structure predictable.** A responsibility's code lives in its owner's files (`Owner.Concern.cs`), each concern uses its one canonical form, and a concept has one name in every phase. Consistency means the canonical forms and the target structure in COMPILER_ARCHITECTURE, not the surrounding code: where an area still deviates (the old Analysis/Emission paths, Binding code awaiting R7), do not copy its patterns. Split a file or member by responsibility before it exceeds the structure limits. Once the budget counts tokens (Code Rules Analyzer A1), the entry file of each canonical component begins with its contract (responsibility, inputs, outputs, invariants and what it must not do), kept current with the component.
- **Restructure and change behavior in separate commits.** When a change does not fit the current structure as one row or one function, first commit the restructuring with behavior unchanged, shown as [VERIFICATION](docs/dev/VERIFICATION.md#behavior-preserving-commits) describes, then commit the behavior change on it.
- Remove branches, state, conversions and old paths made redundant by the change in the same unit. A necessary staged removal is recorded in the stage ledger or PLAN with the stage that removes it.
- **Independent review.** Before completion, someone other than the implementer checks that the design was followed, no sign of a symptom fix remains and reuse, duplication and efficiency are addressed; a rejected design or diff is redone, not patched over. The commit states the root cause, the concept changed, the sites sharing the cause, the deleted code, the rejected options and what remains unverified. Test another form under the same rule and an invalid counterpart where relevant.
- Write core libraries, including Iterator and Dictionary, in Kimigayo. A compiler intrinsic or hand-written IR needs either inexpressibility in Kimigayo or a measured O2 need; a temporary one names the stage that removes it. Follow [STYLE](docs/STYLE.md); `[Kimi]` rules are required in `src/Kimi/Library` and `docs/spec/` examples, recommended elsewhere.

# Performance and Compiler Structure

- Generated native code: O2 performance and allocation counts may not regress. In the compiler, an allocation is allowed when it makes equivalent work faster or reduces compiler code with a reasonable increase in memory; measure time, allocations and retained memory, record the tradeoff and update the affected compiler allocation regressions in the same commit.
- Follow [COMPILER_ARCHITECTURE](docs/dev/COMPILER_ARCHITECTURE.md) for dependencies, closed vocabularies, canonical components, structure limits, size caps, stage gates and the migration freeze. Permission to revise the specification does not authorize vocabulary or budget changes. Raising a cap or closing a stage above its ceiling is the owner's decision, except a cap raised to preserve the native O2 and allocation guarantees, with the reason recorded.
- State the net size per compiler area in each compiler commit in non-trivia C# tokens, with lines for reference (Code Rules Analyzer A1); LLVM templates retain their line count. Growth above 2,000 tokens explains what it replaced or removed and why the rest is needed.
- At each milestone completion and reduction-stage exit, record area sizes, change hotspots and duplication candidates in `artifacts/benchmarks/`. A file changed by three consecutive units is restructured before its next change, or the deferral and its stage are recorded in the stage ledger.
- Timing-only benchmark code stays in `src/Benchmark`, apart from functional and allocation/reuse regressions. Measure on equivalent workloads under fixed conditions and keep failed evidence; never add warm-up or relax native allocation assertions to pass.

# Documentation

Write in English. Each document owns the following; keep changes consistent with the owner. Documents state current rules and positions in short rows; narrative, completed history and evidence belong in commits, PLAN_HISTORY and artifacts.

| Document | Owns |
| --- | --- |
| [SPEC](docs/SPEC.md) and chapters | Language rules; self-contained, drafts are not authority. |
| [IMPL](docs/IMPL.md) | Implementation contracts; never overrides language rules. |
| [COMPILER_ARCHITECTURE](docs/dev/COMPILER_ARCHITECTURE.md) | Phases, dependencies, canonical components, structure limits, vocabularies, budgets, stage ledger and migration rules. |
| [DECISIONS](docs/dev/DECISIONS.md) | Compiler design decisions with their reasons and rejected options; the authority over planning and handoff files. A superseded entry stays and names its replacement. |
| [VERIFICATION](docs/dev/VERIFICATION.md) | Verification commands, selection, measurements and evidence. |
| [CODEMAP](docs/dev/CODEMAP.md) | Navigation to owners, entry points and representative tests; changed or stale mappings are updated in the same commit. |
| [PLAN](docs/dev/PLAN.md) | Current scope, order, completion conditions, next actions and open issues; under 200 lines; deferred items belong in SPEC Appendix D. |
| [STATUS](docs/STATUS.md) | Verified support boundaries and remaining limits; updated when a boundary changes; never claims unverified support. |
| [PLAN_HISTORY](docs/dev/PLAN_HISTORY.md) | A few lines per session; evidence belongs in commits and artifacts. |
| [SETTLED](docs/SETTLED.md) | Rejected design changes: problem, example, proposed fix and reason. Consult before design reviews; reopen only when the reason no longer holds, updating the entry with the spec change. |
| [STYLE](docs/STYLE.md) / [LIBRARY](docs/LIBRARY.md) | Conventions / public declarations and guarantees, following SPEC, including §4.7.1 naming pairs. The [Library README](src/Kimi/Library/README.md) owns implementation policy. |

- Never narrow the specification to fit missing implementation: report Unsupported and record the boundary in STATUS and PLAN. Deliberate spec improvements need no separate approval: state the rationale and how the Principles are preserved, and update affected chapters, examples, tests and milestone programs together. Pre-alpha changes need no version bump or migration guide.
- Create or change proposal text only on explicit instruction, in `draft/Proposals` (prototypes in `draft/Sketches`; `draft/Design` and `draft/Decisions` are frozen archives). Record each intake in `draft/INTEGRATED.md` with proposal-to-spec mappings in the same commit. An open proposal is 一部取り込み (partially integrated), listing integrated and open scope; its integrated text is frozen. Close it when every item is incorporated, rejected with a reason or transferred: to `draft/Changes` as 取り込み済み (integrated) if anything was incorporated, otherwise to `draft/Obsolete` as 完了 (closed).
- Never change frozen text, including typos: record corrections in INTEGRATED and later normative changes in the formal spec. Integration and closure never imply support.

# Diagnostics

- Specification violations are Language diagnostics, valid but unimplemented forms the single location-only `Unsupported_Kd`, and internal contract violations Internal errors. A valid form that no common rule covers yet stays `Unsupported_Kd`: never accept a narrow subset of it or report a conservative Language error for it.
- A check records typed facts where it fails; message text comes only from the diagnostics adapter, and no check re-runs inference or proof to explain a failure. Follow SPEC §23 and [DIAGNOSTICS](docs/dev/DIAGNOSTICS.md); never invent evidence, Advice or repair guarantees. Apply [DIAGNOSTIC_WORKFLOW](docs/dev/DIAGNOSTIC_WORKFLOW.md) only on explicit request for that workflow or a detailed diagnostic review; this limits work scope, not required diagnostic behavior.

# Verification and Delivery

- Serialize editing, builds, verification and commits within a worktree; parallel work and other unfinished changes use separate worktrees. Never edit sources during builds or verification. Stage only the unit's own paths.
- Work in units of design, reproducer, implementation and focused tests. Select completion checks by changed behavior and files; mixed changes combine the rows.

| Change | Required verification |
| --- | --- |
| Documentation only | Diff and reference review; no builds, tests or native verification. |
| Compiler, Kimi library or compiler tests | Unit verification of the explicitly selected related checks (library changes add `KimiLibraryShapeTest`) plus the common `CompilerSizeBudgetTest` and `ArchitectureRulesTest`, which Verify adds, and a successful Session at the end of the working session. |
| Benchmark / Playground | Session, including the whole-solution build, plus relevant execution or measurements. |
| Verification scripts, CI or tooling | Affected workflow checks under [verification scope](docs/dev/VERIFICATION.md#verification-scope). |
| VS Code extension, including build/distribution scripts | [Extension AGENTS](src/kimi-ext/AGENTS.md). |

- Feedback uses incremental builds and selected test methods; completion uses `scripts/verify.ps1` as VERIFICATION describes. Native fixtures and milestones need explicit selection, even in Session. Run NativeAOT tests only on explicit instruction.
- Tests reuse the owning class and shared helpers and assert each rule through public acceptance, diagnostics, IR or MIR entry points at the lowest layer that observes it; internal state only in a component's own rule tests. No duplicate cases; native fixtures only for generated-code behavior; input-size scaling checks for affected analyses. Tag allocation/reuse tests `Purpose=Allocation` and include related ones for hot-path changes. A defect fix adds a regression that fails before the fix and passes after it.
- Never weaken an expectation, assertion, ratchet, cap or baseline to make a check pass. A change required by a SPEC revision or an owner decision states that reason in the commit.
- Commit each unit after its required checks pass, associate Unit/Session evidence with `scripts/verify-commit.ps1`, then push the branch to `origin`; never force-push. A Session that covers every required check of the last unit on the same inputs replaces its Unit run. Do not repeat checks for mode names or unchanged inputs; changed inputs need reverification. A failed required check leaves the work incomplete and unpushed.

# Artifacts

- `temp/`: disposable files, deleted only when no build or test is running. Preserve project-local `bin/` and `obj/`.
- `artifacts/verify/`: verification evidence, including failures; `artifacts/benchmarks/`: measurements; `artifacts/packages/`: packages. `temp/` and `artifacts/` are Git-ignored; back up evidence that must be kept.
