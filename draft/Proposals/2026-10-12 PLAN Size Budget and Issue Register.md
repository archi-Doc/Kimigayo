# PLAN Size Budget and Open Issue Register

Date: 2026-10-12. Status: accepted by the owner on 2026-10-12, to be applied while no unit is running; not yet applied. Changes AGENTS.md (the owner's decision), one verification script with its test, and documentation. No SPEC or compiler change.

Base: `ec7585f7` (`dev`). The numbers below are the recommended values; adjust them before applying if needed.

## 1. Problem

| Measure (at the base) | Value |
| --- | --- |
| PLAN.md | 172 lines, 42,509 bytes, 5,293 words (about 10k tokens) |
| Line length | median 180, p90 595, max 1,368 characters |
| §7 Open issues | 29 lines, 15,839 bytes (37% of the file), 24 issue rows of up to 1,368 characters |
| §4 Milestones | 74 lines, 13,214 bytes, including DONE rows and two completed tracks |
| Edit rate | 314 commits touched PLAN.md from 2026-10-01 to 2026-10-12 |

Causes:

1. **Wrong unit.** The AGENTS rule "under 200 lines" is met by packing several items into one line, so the budget pressure moves into line length instead of content. The compiler budgets moved from lines to tokens for the same reason (`70de3b71`).
2. **Several items per line.** Edits to different items on one line conflict. Parallel units (R4) edit PLAN in nearly every commit; for example, §3 "Carried forward" holds the R2a–R3 and R1 carry-overs on one line.
3. **Mixed owners.**
   - The open-issue register grows with the number of issues, while the plan should shrink as work completes.
   - Completed history (DONE milestone rows with evidence, "Diagnostics track (complete)", "Repair track (complete)") belongs in PLAN_HISTORY under the AGENTS Documentation rule.
4. **Unit progress in PLAN.** Progress lines written inside a stage turn PLAN into a progress log.

## 2. Proposal

### 2.1 PLAN: a character budget and one item per line

AGENTS Documentation table, PLAN row:

- Now: "Current scope, order, completion conditions, next actions and open issues; under 200 lines; deferred items belong in SPEC Appendix D."
- New: "Current scope, order, completion conditions and next actions; at most 20,000 characters, one item per line, each line or table row at most 400 characters; changes when the position, the next actions or an issue's state changes, not for unit progress; deferred items belong in SPEC Appendix D."

Characters are counted as by `wc -m`. Unit progress goes to the stage handoff and PLAN_HISTORY.

### 2.2 ISSUES: the open-issue register

Add `docs/dev/ISSUES.md` with this AGENTS row: "Open issues: one row per issue (id, verified boundary, remaining work, owner), each at most 400 characters with links to details; a resolved issue's row is removed and its closure recorded in PLAN_HISTORY."

- **No total cap.** A cap would push open issues out of the record; the row cap keeps the register easy to scan.
- **Ids unchanged** (G-ids, OCC-X).
- **Rows over 400 characters** keep the verified boundary and the remaining work in one sentence each. The detail moves to the existing detail document, STATUS or a PLAN_HISTORY anchor.
- **AGENTS lines that send open issues to PLAN** name ISSUES instead:
  - line 38: "record the proposed common change in PLAN" becomes "… in ISSUES";
  - line 72: "record the boundary in STATUS and PLAN" becomes "… in STATUS and ISSUES";
  - line 41 (a staged removal recorded in the stage ledger or PLAN) is unchanged.
- **PLAN** loses "open issues" from its header sentence, and §7 becomes one line that links ISSUES.

### 2.3 Verification inputs

ISSUES.md stays outside the verification inputs, as PLAN.md is today, because units update issue rows after verification.

- `scripts/verification-inputs.ps1:32` (`Test-KimiVerificationInput`) also excludes `docs/dev/ISSUES.md`.
- `tests/scripts/VerificationInputsTest.ps1` gets the matching expectation (as for PLAN.md at :37–:54).
- The policy name ("…-plan-bookkeeping") stays accurate.

### 2.4 Completed history out of PLAN

- **§4.** The DONE milestone rows become one line, "DONE: P22, P19, …; evidence in PLAN_HISTORY", after checking that each row's evidence is reachable from PLAN_HISTORY.
- **Completed tracks.** "Diagnostics track (complete)" and "Repair track (complete)" are deleted after confirming that PLAN_HISTORY holds their checkpoints (`PLAN_HISTORY.md#diagnostics-checkpoints`). The one inbound link to `PLAN.md#diagnostics-track-complete` is repointed.
- **§3.** One fact per line; the "Carried forward" line is split by stage.

### 2.5 References to PLAN issues

About 118 lines cite "PLAN Gnn" or "PLAN §7":

| Where | Lines | Treatment |
| --- | ---: | --- |
| Documentation (docs/dev 15, STATUS 13, Benchmark .md) | about 30 | Rewritten in the same commit. |
| Source and test comments (Binding 27, xUnit tests 21, Analysis 18, Emission 6, milestone and workload files 8, ElementAccess 3, Benchmark .cs) | about 85 | Kept. The ids stay valid; PLAN keeps one line: "Open issues, including those cited as 'PLAN Gnn', are in ISSUES." A comment is rewritten when its file next changes; the old Analysis/Emission comments go away in R6. |

Comment-only edits in compiler files would need unit verification and buy no behavior.

### 2.6 Enforcement

A review rule only. PLAN and ISSUES are outside the verification inputs, so a test that reads them would make evidence depend on files the manifest does not record.

## 3. Expected result

| File | Before | After (estimate) |
| --- | --- | --- |
| PLAN.md | 42.5 KB; longest line 1,368 characters | at most 20,000 characters; no line over 400. Sources of the reduction: §7 (−15.8 KB), DONE rows and tracks (about −4 KB), one-fact lines. |
| ISSUES.md | — | about 10 KB (24 rows of at most 400 characters plus the header) |

## 4. Rejected options

- **Keep "under 200 lines".** It does not limit size (§1).
- **A token budget, as for the compiler.** The toolchain has no Markdown tokenizer, and characters track tokens closely enough for English prose.
- **Keep §7 in PLAN with only a row cap.** PLAN would stay at about 30 KB and still mix a growing register with the plan.
- **A total cap on ISSUES.** It pushes open issues out of the record (AGENTS: keep the open issue).
- **Open issues in STATUS.** STATUS owns verified support boundaries; remaining work and owners are planning.

## 5. Application

**Preconditions:**
- No unit is running. R4 units edit PLAN in nearly every commit; check `git worktree list` for `temp/r4-*` and the active sessions.
- No other session has pending AGENTS.md edits.

**Steps** (two commits, in this order, so that ISSUES.md is excluded from the inputs as soon as it exists):

1. **Tooling.** Make the §2.3 change and run `tests/scripts/VerificationInputsTest.ps1` plus the related checks of the tooling row in VERIFICATION.
2. **Documentation.**
   - AGENTS rows and lines 38 and 72 (§2.1, §2.2).
   - ISSUES.md created from §7.
   - PLAN: header, §3, §4, and §7 reduced to the pointer.
   - Documentation references (§2.5).
   - One line in PLAN_HISTORY.

   Review: no dangling links (grep the `PLAN.md#` anchors, "PLAN §7" and "PLAN G" in documentation); sizes measured with `wc -m`; maximum line length checked.

**Done when** PLAN.md has at most 20,000 characters and no line over 400, ISSUES.md holds every former §7 row, the references follow §2.5, and both commits are pushed.
