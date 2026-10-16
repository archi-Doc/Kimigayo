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

# Implementation Design

SPEC correctness and the native O2 run-time and allocation guarantees are constraints. Within them, write code that is **consistent, short and fast**, in that order:

- **Consistent:** each concept has one owning component, one representation, one canonical path and one name in every phase.
- **Short:** the least code that does the job coherently, counting deletions; no speculative mechanisms, wrappers without semantics, single-implementation interfaces or utility bags.
- **Fast:** fast compilation and fast generated code, shown by measurement.

- **Roles.** The owner is the user. The planning session, which received the request, decides the design; the implementer follows it; a reviewer wrote neither, so a session working alone delegates review.
- **Design before code.** First establish the failing condition and its cause from the code and a reproducer; label an unverified hypothesis as one. A change is local when it stays in one owning component, keeps its contract and invariants, adds no branch, parameter, state or table, and a search finds no other site with the same cause; its commit states the rule and the search. Any other change first gets a short design as one chain: the required meaning (the SPEC rule and the forms it governs) → what the representation lacks or wrongly allows → the owning component that decides it → what becomes unnecessary. It lists the sites sharing the cause ([CODEMAP](docs/dev/CODEMAP.md)), the options (at least a fix inside the owning component and a change of shared structure), the deletions and the expected size and speed. A delegated prompt carries the request, design, completion conditions and checks; an implementer who finds the design wrong stops and reports.
- **Record decisions.** New representations, components, vocabulary entries, temporary structures and responsibilities moved between phases are decided by the planning session (or the owner where COMPILER_ARCHITECTURE says so) and recorded in [DECISIONS](docs/dev/DECISIONS.md) in the commit that first implements them. Read the area's DECISIONS and SETTLED entries first; departing from one adds a superseding entry naming the reason that no longer holds.
- **Fix causes in shared structure,** even across phases; other findings become open issues or separate units. Each decision is made once, by its owning phase; a consumer lacking a fact has the owning component publish it. Each IR has one validator, owned by its producer (MIR: `MirValidator`); consumers trust validated IR, and a broken contract is `InternalInvariant_Kd` without recovery.
- **Share by meaning.** Decisions with the same meaning and contract share one owning component; a similar shape under another contract stays separate. A new concept answers a recurring decision or an unowned responsibility, states what it removes, takes a SPEC name and a contract, replaces every existing use in the same unit and becomes a canonical component. Under a canonical table a new form adds a row, not a case.
- **Symptom fixes** (stop and redo the analysis): a clause or switch arm for one form; a bool, mode or optional parameter steering a function; a side table for one feature; a consumer deciding by syntax what its owning component should publish; suppressing duplicate reports afterwards; a second copy of a rule, a `Supports*` gate or a shape allowlist; a try-then-fallback chain; a catch-all, silent default or fallback that hides a failure; one more case of an open family; a temporary structure without a removal condition.
- A second fix for a related symptom means the cause was missed: analyze one level higher and replace both. Close an open family with a representation every path passes through (a Type, an IR form, a published column) or a whitelist, every other operation a located `Unsupported_Kd`. If the common fix exceeds the unit, keep the `Unsupported_Kd` or open issue and record the proposed change in PLAN. A local patch is allowed only for wrong code that cannot wait, with its removal condition.
- **Structure.** A responsibility's code lives in its owning component's files (`Owner.Concern.cs`). Follow COMPILER_ARCHITECTURE's target structure, not the old Analysis/Emission paths or Binding code awaiting R7. Split a file or member by responsibility before it exceeds the structure limits. Each canonical component's entry file begins with its contract: responsibility, inputs, outputs, invariants and prohibitions.
- **Separate commits.** When a change does not fit the current structure as one row or function, first commit the restructuring with behavior unchanged ([VERIFICATION](docs/dev/VERIFICATION.md#behavior-preserving-commits)), then the behavior change. Remove what the change makes redundant in the same unit, or record the staged removal in PLAN or the stage ledger.
- **Review weight.** A change to a shared representation or across phases compares two independently written designs, has the chosen design reviewed before coding and gets a final review. Any other change gets one review before completion, which also covers its design.
- **Independent review.** The reviewer starts from the problem, names the general rule and tries a shorter or more general design. It checks that the design was followed and no symptom fix remains. Blocking: a SPEC or contract violation, unsound acceptance, wrong result, internal fault, regression, departure from the design or symptom fix; other findings become open issues. A rejected design or diff is redone, not patched; after two rounds on one family of findings, the owner chooses between a representation, a whitelist and a hold. The commit states the root cause, the concept changed, the sites sharing the cause, the deletions, the rejected options and what remains unverified.
- Write core libraries, including Iterator and Dictionary, in Kimigayo. An intrinsic or hand-written IR needs inexpressibility in Kimigayo or a measured O2 need; a temporary one names the stage that removes it. Follow [STYLE](docs/STYLE.md); `[Kimi]` rules are required in `src/Kimi/Library` and `docs/spec/` examples.

# Performance and Compiler Structure

- A compiler allocation is allowed when it makes equivalent work faster or removes code at a reasonable memory cost; measure time, allocations and retained memory, and update the affected allocation regressions in the same commit.
- Follow [COMPILER_ARCHITECTURE](docs/dev/COMPILER_ARCHITECTURE.md) for dependencies, closed vocabularies, canonical components, structure limits, stage gates and the migration freeze; review enforces them (DECISIONS CR-R1). New vocabulary entries, new or larger exceptions and postponed removal stages need owner approval recorded in the commit. Mechanical splitting, argument-only bags and renaming to evade a limit fail review.
- At each milestone and stage exit, record area sizes, change hotspots and duplication candidates in `artifacts/benchmarks/`. Restructure a file changed by three consecutive units before its next change.
- Timing-only benchmarks stay in `src/Benchmark`. Measure equivalent workloads under fixed conditions, keep failed evidence and never add warm-up or relax native allocation assertions to pass.

# Documentation

- Write documentation in concise, clear English; Japanese documentation uses concise, clear Japanese and [SPECJ_TERMINOLOGY](docs/SPECJ_TERMINOLOGY.md).
- Each document owns the topics below and states current rules in short rows; narrative, history and evidence belong in commits, PLAN_HISTORY and artifacts.

| Document | Owns |
| --- | --- |
| [SPEC](docs/SPEC.md) and chapters | Language rules; self-contained; drafts are not authority. |
| [IMPL](docs/IMPL.md) | Implementation contracts; never overrides language rules. |
| [COMPILER_ARCHITECTURE](docs/dev/COMPILER_ARCHITECTURE.md) | Phases, dependencies, canonical components, structure limits, vocabularies, stage ledger and migration rules. |
| [DECISIONS](docs/dev/DECISIONS.md) | Compiler design decisions with reasons and rejected options, above planning and handoff files; a superseded entry stays and names its replacement. |
| [VERIFICATION](docs/dev/VERIFICATION.md) | Verification commands, selection, measurements and evidence. |
| [CODEMAP](docs/dev/CODEMAP.md) | Navigation to owning components, entry points and representative tests; updated in the same commit. |
| [PLAN](docs/dev/PLAN.md) | Current scope, order, completion conditions, next actions and open issues, under 200 lines; deferred items belong in SPEC Appendix D. |
| [STATUS](docs/STATUS.md) | Verified support boundaries and remaining limits; never claims unverified support. |
| [PLAN_HISTORY](docs/dev/PLAN_HISTORY.md) | A few lines per session. |
| [SETTLED](docs/SETTLED.md) | Rejected design changes with their reasons; consult before design reviews, reopen only when a reason no longer holds. |
| [STYLE](docs/STYLE.md) / [LIBRARY](docs/LIBRARY.md) | Conventions / public declarations and guarantees, following SPEC (§4.9.1 naming pairs). The [Library README](src/Kimi/Library/README.md) owns implementation policy. |

- Never narrow the specification to fit missing implementation: report Unsupported and record the boundary in STATUS and PLAN. Deliberate spec improvements need no approval: state the rationale and how the Principles hold, and update affected chapters, examples, tests and milestone programs together; pre-alpha changes need no version bump.
- Create or change proposal text only on explicit instruction, in `draft/Proposals` (prototypes in `draft/Sketches`). Record each intake and its proposal-to-spec mapping in `draft/INTEGRATED.md` in the same commit. An open proposal is 一部取り込み (partially integrated); its integrated text is frozen. A closed proposal moves to `draft/Changes` as 取り込み済み (integrated) if anything was incorporated, otherwise to `draft/Obsolete` as 完了 (closed).
- Never change frozen text, including typos; record corrections in INTEGRATED. Integration and closure never imply support.

# Diagnostics

- Specification violations are Language diagnostics, valid but unimplemented forms the single location-only `Unsupported_Kd`, and internal contract violations Internal errors. A valid form that no common rule covers yet stays `Unsupported_Kd`: never accept a narrow subset of it or report a conservative Language error for it.
- A check records typed facts where it fails; message text comes only from the diagnostics adapter, and no check re-runs inference or proof to explain a failure. Follow SPEC §23 and [DIAGNOSTICS](docs/dev/DIAGNOSTICS.md); never invent evidence, Advice or repair guarantees. Apply [DIAGNOSTIC_WORKFLOW](docs/dev/DIAGNOSTIC_WORKFLOW.md) only on explicit request.

# Verification and Delivery

- Serialize editing, builds, verification and commits within a worktree; parallel or unfinished work uses separate worktrees. Never edit sources during builds or verification. Stage only the unit's own paths.
- Work in units of design, reproducer, implementation and focused tests. Select checks by changed behavior and files; mixed changes combine the rows.

| Change | Required verification |
| --- | --- |
| Documentation only | Diff and reference review; no builds, tests or native verification. |
| Compiler, Kimi library or compiler tests | Unit verification of the related checks (plus `KimiLibraryShapeTest` for library changes) and a Session at the end of the working session. |
| Benchmark / Playground | Session, including the whole-solution build, plus relevant execution or measurements. |
| Verification scripts, CI or tooling | Affected workflow checks under [verification scope](docs/dev/VERIFICATION.md#verification-scope). |
| VS Code extension and its build/distribution scripts | [Extension AGENTS](src/kimi-ext/AGENTS.md). |

- Feedback uses incremental builds and selected test methods; completion uses `scripts/verify.ps1` as VERIFICATION describes. Native fixtures and milestones need explicit selection, even in Session. Run NativeAOT tests only on explicit instruction.
- Tests reuse the owning class and shared helpers and assert each rule at the lowest public layer that observes it (acceptance, diagnostics, IR or MIR). No duplicate cases; native fixtures only for generated-code behavior; scaling checks for affected analyses; another form under the same rule and an invalid counterpart where relevant. Tag allocation/reuse tests `Purpose=Allocation`. A defect fix adds a regression that fails before the fix and passes after it.
- Never weaken an expectation, assertion, ratchet or baseline to make a check pass; a change required by a SPEC revision or an owner decision states that reason in the commit.
- Commit each unit after its required checks pass, associate its evidence with `scripts/verify-commit.ps1`, then push the branch to `origin`; never force-push. A Session covering the last unit's checks on the same inputs replaces its Unit run; do not repeat checks on unchanged inputs. A failed required check leaves the work incomplete and unpushed.

# Artifacts

- `temp/`: disposable files, deleted only when no build or test is running. Preserve project-local `bin/` and `obj/`.
- `artifacts/verify/`: verification evidence, including failures; `artifacts/benchmarks/`: measurements; `artifacts/packages/`: packages. `temp/` and `artifacts/` are Git-ignored; back up evidence that must be kept.
