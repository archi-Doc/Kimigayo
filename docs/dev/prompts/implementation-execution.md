# Kimigayo Implementation Execution

Implement the target milestone of [docs/dev/PLAN.md](../PLAN.md) with verified, committed units. Spend the time on implementation, not on records: commits and `artifacts/verify/` are the evidence.

## Inputs

- **Plan:** `docs/dev/PLAN.md` (§4 milestone order, §5 completion conditions, §6 next actions, §7 open issues).
- **Target:** the milestone or track stage (for example `D2a`) named by the request; otherwise the target that PLAN §6 assigns. A paused milestone is not a target until PLAN resumes it.
- **Diagnostics:** during ordinary implementation, create or update diagnostic facts following [AGENTS.md](../../../AGENTS.md#diagnostics). Apply the full [Diagnostic Development Workflow](../DIAGNOSTIC_WORKFLOW.md) only when the user explicitly requests it or a detailed diagnostic review; diagnostic changes and bug reports alone do not activate it.
- **Constraints:** follow `AGENTS.md`: within SPEC correctness and native O2 guarantees, write consistent, short and fast code, in that order.
- **Documents:** [AGENTS Documentation](../../../AGENTS.md#documentation) owns update conditions. Change only affected facts whose condition holds; do not scan or synchronize all related documents. Specification revisions, policy changes, translations, guides and history require explicit scope.
- **Permissions:** commit each verified unit. Milestone Program sources are immutable by default: change one only to re-spell it for a SPEC revision or an authoring correction where the source contradicts its own specification-derived output (recorded in `tests/milestones/README.md`), or with the user's approval. Creating or completing the target milestone's harness (`src/backend/windows-x64/test-milestone<N>.ps1`) is always allowed. Draft files and NativeAOT tests need explicit instruction.

## 1. Start (keep it under 10 minutes)

1. Record the start time.
2. Run `git status` and `git log -3`, and read PLAN §3–§7.
3. Read only the owning SPEC sections and the code paths needed for the next action.
4. Use commit-associated evidence in `artifacts/verify/` for the baseline; do not maintain a copy in PLAN. Do not rerun matching successful checks on unchanged inputs. Otherwise select checks for the affected area under VERIFICATION; documentation-only work needs diff/reference review, no build or test run.
5. Preserve uncommitted user changes; if they overlap the work, ask before touching them.
6. Sessions that may run in parallel use separate git worktrees, so a build or `scripts/verify.ps1` run never reads another session's edits. Stage only the files the unit changed (`git add <paths>`), never `git add -A`.

Do not print a long inspection report. Begin the first unit.

## 2. Unit cycle

Each unit follows [WORKFLOW](../WORKFLOW.md): its class sets the design and reviews.

```
reproducer (a failing test or program variant)
→ implementation following the agreed design, with incremental builds and selected tests
→ reviews until ACCEPT or a hold
→ ./scripts/verify.ps1 -Class <related tests> [-Fixtures '<pattern>'] [-Milestone <n>]
→ fix failures at their root (the fix gets a diff review; the checks it invalidated run again)
→ commit (descriptive message; this is the unit's record)
→ next unit
```

- Keep units small enough to verify in minutes. Split large work into committed steps that each keep the tree green.
- Unit verification covers only the related tests, native fixtures and harnesses. Do not run the full Debug/Release suites per unit.
- For the final unit, Session may replace the separate Unit run when it covers every required check against the same source/configuration; follow [VERIFICATION.md](../VERIFICATION.md#feedback-and-completion), then commit, associate evidence and push.
- Never edit sources while a build or `scripts/verify.ps1` run is in progress. `scripts/verify.ps1` isolates fixtures by run and configuration; direct test invocations still share `temp/scalar-fixtures` unless `KIMI_FIXTURE_DIRECTORY` is set.
- Write test source strings in C# with the Edit tool; shell heredocs corrupt backslashes.

## 3. Correctness rules

- docs/SPEC.md and its chapters define the required behavior; revise them only as AGENTS.md describes. Items that Appendix D defers are out of scope; ObjectCallCompatible stage 2 (Appendix D.2: same-build inference, use-time enforcement and publication) is in scope. Never weaken SPEC, tests or diagnostics to match a missing or incomplete implementation.
- Report invalid source through the existing diagnostics at the correct stage, with an `Unsupported` code for a form the SPEC permits but the implementation lacks (SPEC §23.1.1). Unsupported generation must be diagnosed and must never produce wrong code.
- Removing an unsupported guard requires every newly reachable analysis and generation path to be implemented and tested.
- Preserve ownership, Loan, Origin, evaluation order, cleanup and Abort semantics; keep internal invariant checks.
- Tests: add positive and negative cases for each observable change and a regression test for each fix. A test may be corrected only when SPEC shows it is wrong; say why in the commit.
- Before treating an example's rejection as an implementation gap, check its declared result, acquisitions and Constraints against SPEC and compile a valid control. An omitted named-function result means Unit; a body result is not evidence for changing that rule. Keep the intended failure and the valid control independently specified.
- Reuse existing structures; avoid new layers, speculative abstraction and unrelated reformatting.

## 4. Decisions and blockers

- Resolve implementation details yourself when SPEC behavior is preserved.
- If progress needs an undefined rule, record the blocker and proposed decision in PLAN §7 and continue independent work. A case under [WORKFLOW's owner decisions](../WORKFLOW.md#owner-decisions), including a SPEC contradiction, suspends the unit and goes to the owner. Revise SPEC/IMPL only when specification or contract revision is explicitly in the user's requested scope; otherwise report the issue. A possible improvement alone does not authorize rewriting the rules.
- A blocked unit leaves the tree green: revert or finish its partial change before switching.

## 5. Session end

1. The duration covers unit work only. Stop starting units when it has elapsed; finish or cleanly revert the current unit.
2. Prepare affected source and condition-triggered documentation changes before verification. For compiler/library/test or Benchmark/Playground changes, run `./scripts/verify.ps1 -Mode Session [-Fixtures ...] [-Milestone <required programs>]` once; it may run past the duration. Include the last Unit's required checks if this replaces its Unit run. Documentation-only work uses diff/reference review; tooling follows VERIFICATION's scope table. Do not repeat successful checks whose inputs and coverage still match.
3. Commit, associate Unit/Session evidence where applicable, and push. Changed covered inputs require reverification. Keep ordinary session history and test results in the commit and artifacts:
   - **PLAN:** update only changed position, milestone/track state, next actions or blockers. Mark DONE only when the applicable completion conditions hold. Do not add the session's timestamp, HEAD or test count.
   - **STATUS:** update only the affected support boundary or known limit, with its verification qualification.
   - **Other documents:** apply the AGENTS table. Do not append PLAN_HISTORY, synchronize SPECJ/GUIDE or change specification/policy documents unless explicitly requested.

## 6. Final report

Keep it short:

1. **Result:** units completed and milestone state.
2. **Commits:** hashes with one-line descriptions.
3. **Verification:** Session result (tests per configuration, native, harnesses) and anything not run.
4. **Next:** the first action from PLAN §6, and any question for the user.
5. **Time:** elapsed time and stopping reason.
