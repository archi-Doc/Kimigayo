# Kimigayo Implementation Execution

Implement the target milestone of [docs/dev/PLAN.md](../PLAN.md) with verified, committed units. Spend the time on implementation, not on records: commits and `artifacts/verify/` are the evidence.

## Inputs

- **Plan:** `docs/dev/PLAN.md` (§4 milestone order, §5 completion conditions, §6 next actions, §7 open issues).
- **Target:** the milestone or track stage (for example `D2a`) named by the request; otherwise the target that PLAN §6 assigns. A paused milestone is not a target until PLAN resumes it.
- **Diagnostics:** when a unit adds or changes a diagnostic, a check that reports diagnostics, recovery or suppression, or diagnostic publication, follow the Diagnostic Development Workflow of `AGENTS.md` (detail in `docs/dev/DIAGNOSTICS.md` §10).
- **Constraints:** follow `AGENTS.md`. Minimize allocations on hot paths and remove unnecessary code, but correctness and progress come first.
- **Permissions:** commit each verified unit. Milestone Program sources are immutable by default: change one only to re-spell it for a SPEC revision or an authoring correction where the source contradicts its own specification-derived output (recorded in `tests/milestones/README.md`), or with the user's approval. Creating or completing the target milestone's harness (`src/backend/windows-x64/test-milestone<N>.ps1`) is always allowed. Draft files and NativeAOT tests need explicit instruction.

## 1. Start (keep it under 10 minutes)

1. Record the start time.
2. Run `git status` and `git log -3`, and read PLAN §3–§7.
3. Read only the owning SPEC sections and the code paths needed for the next action.
4. Do not rerun a baseline if HEAD matches the last session's verified commit (PLAN §3). Otherwise run `./scripts/verify.ps1` on the affected area first.
5. Preserve uncommitted user changes; if they overlap the work, ask before touching them.
6. Sessions that may run in parallel use separate git worktrees, so a build or `scripts/verify.ps1` run never reads another session's edits. Stage only the files the unit changed (`git add <paths>`), never `git add -A`.

Do not print a long inspection report. Begin the first unit.

## 2. Unit cycle

```
reproducer (a failing test or program variant)
→ smallest coherent implementation
→ ./scripts/verify.ps1 -Class <related tests> [-Fixtures '<pattern>'] [-Milestone <n>]
→ fix failures at their root
→ commit (descriptive message; this is the unit's record)
→ next unit
```

- Keep units small enough to verify in minutes. Split large work into committed steps that each keep the tree green.
- Unit verification covers only the related tests, native fixtures and harnesses. Do not run the full Debug/Release suites per unit.
- Never edit sources while a build or `scripts/verify.ps1` run is in progress. `scripts/verify.ps1` isolates fixtures by run and configuration; direct test invocations still share `temp/scalar-fixtures` unless `KIMI_FIXTURE_DIRECTORY` is set.
- Write test source strings in C# with the Edit tool; shell heredocs corrupt backslashes.

## 3. Correctness rules

- docs/SPEC.md and its chapters define the required behavior; revise them only as AGENTS.md describes. Items that Appendix D defers are out of scope; ObjectCallCompatible stage 2 (Appendix D.5.1: same-build inference, use-time enforcement and publication) is in scope. Never weaken SPEC, tests or diagnostics to match a missing or incomplete implementation.
- Report invalid source through the existing diagnostics at the correct stage, with an `Unsupported` code for a form the SPEC permits but the implementation lacks (SPEC §23.3.6.1). Unsupported generation must be diagnosed and must never produce wrong code.
- Removing an unsupported guard requires every newly reachable analysis and generation path to be implemented and tested.
- Preserve ownership, Loan, Origin, evaluation order, cleanup and Abort semantics; keep internal invariant checks.
- Tests: add positive and negative cases for each observable change and a regression test for each fix. A test may be corrected only when SPEC shows it is wrong; say why in the commit.
- Reuse existing structures; avoid new layers, speculative abstraction and unrelated reformatting.

## 4. Decisions and blockers

- Resolve implementation details yourself when SPEC behavior is preserved.
- If progress needs an undefined rule or meets a SPEC conflict, or a simpler, more consistent or more efficient rule is found, revise the SPEC as AGENTS.md describes in its own verified unit; the commit states the rationale and the Kimigayo Principles check. For a scope change, add one row to PLAN §7 (issue, evidence, proposed decision), ask the user in one short message, and continue independent work.
- A blocked unit leaves the tree green: revert or finish its partial change before switching.

## 5. Session end

1. The duration covers unit work only. Stop starting units when it has elapsed; finish or cleanly revert the current unit.
2. Then run `./scripts/verify.ps1 -Mode Session [-Fixtures ...] [-Milestone <completed programs>]` once; it may run past the duration.
3. Update the documents briefly, then commit:
   - **docs/dev/PLAN.md:** §3 position (the session's target, HEAD, test count), milestone and track states, §6 next three actions, §7 issues. Mark a milestone DONE only when every PLAN §5 condition holds, and a track stage only when its own completion condition holds.
   - **docs/dev/PLAN_HISTORY.md:** one table row for the session (date, what changed, result).
   - **docs/STATUS.md:** only if a support boundary changed; state it in one or two sentences.
   - **docs/SPEC.md:** changed only by the unit that revises it, never to describe implementation state.

## 6. Final report

Keep it short:

1. **Result:** units completed and milestone state.
2. **Commits:** hashes with one-line descriptions.
3. **Verification:** Session result (tests per configuration, native, harnesses) and anything not run.
4. **Next:** the first action from PLAN §6, and any question for the user.
5. **Time:** elapsed time and stopping reason.
