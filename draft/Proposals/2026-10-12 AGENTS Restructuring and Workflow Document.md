# AGENTS Restructuring and the Workflow Document

Date: 2026-10-12. Status: to be applied after the R4 exit while no unit is running (owner, 2026-10-12); not yet applied. Owner decisions of 2026-10-12: the session templates follow option A (§5); no size budgets are added (§6). Changes AGENTS.md, adds `docs/dev/WORKFLOW.md`, moves rows into COMPILER_ARCHITECTURE, VERIFICATION and `draft/INTEGRATED.md`, and revises `docs/dev/prompts` (§5). No SPEC, compiler, test or tooling change.

Base: `ec7585f7` (`dev`). Line numbers refer to AGENTS.md at that commit. Apply by anchor (bullet title or quoted phrase): the [PLAN Size Budget](<2026-10-12 PLAN Size Budget and Issue Register.md>) proposal edits some of the same lines. Sizes are counted with `wc -m`, as in that proposal.

## 1. Problem

| Measure | Value |
| --- | --- |
| AGENTS.md | 102 lines, 3,031 words, 20,861 characters; 17,470 at `04873385` (+19%) |
| Implementation Design | 9,774 characters (47%); three bullets over 150 words: Design before code 231, Independent review 186, Share decisions 151 |
| Edit rate | 8 commits from 2026-10-11 16:03 to 2026-10-12 15:39 (`8071549f` … `ec7585f7`) |
| Procedure outside Git | `artifacts/handoff/r4/common.md` 10,237 and `design-template.md` 3,454 characters; r3 has its own `common.md` |
| Tracked procedure older than the priorities | `docs/dev/prompts/` 21,039 characters, last changed 2026-10-08 |

Causes:

1. **Two kinds of rule in one bullet.** Some rules every session applies: the local-change test, the signs of a symptom fix, sharing by meaning. Others one role uses at one moment: the design chain and contents, the comparison, delegation prompt contents, the review rubric, the commit record. Bullets mix both, so every session loads the procedures and the decision rules are diluted.
2. **Restated owners.** Rows restate COMPILER_ARCHITECTURE (compiler allocation policy, cap raising, code-rules approval and evasions, splitting before the limits, component contracts) and VERIFICATION (measurement conditions). They also define the draft lifecycle that `draft/INTEGRATED.md` summarizes "as defined in AGENTS.md".
3. **No tracked owner for the procedure.** Each stage copies it into Git-ignored handoff files. Work outside a stage sees only AGENTS's compressed form.
4. **Stale tracked procedure.** `docs/dev/prompts` contradicts AGENTS's priorities and DECISIONS Q3:
   - "Prefer the smallest coherent change" (implementation-planning.md:25);
   - "smallest coherent implementation" (implementation-execution.md:28);
   - "correctness and progress come first" (implementation-execution.md:10);
   - "Minimize allocations as much as possible" (implementation-planning.md:13).

   These prompts, DIAGNOSTICS.md:245 and DIAGNOSTIC_WORKFLOW.md:5 and :15 link AGENTS anchors that no longer exist (`#diagnostic-development`, `#implementation-workflow`).
5. **Accretion.** Each revision appended clauses where the need arose instead of where their kind belongs; §2 gives each kind one home.

## 2. Principles of the split

| Kind | Home | Test |
| --- | --- | --- |
| Decision rule every session applies | AGENTS.md | Would an implementer or a one-off fixing session decide wrongly without it? |
| Procedure or format used by one role at one moment | WORKFLOW.md | Is it needed only when writing a design, a decision, a delegation prompt, a review or a commit record, or when running parallel units? |
| Rule another document owns | That document; AGENTS keeps at most a pointer | The AGENTS Documentation table |
| Stage-specific item | The stage handoff | It cites WORKFLOW sections and adds only the stage's items |

Constraints:

- Every sentence of the base AGENTS.md ends in exactly one place (§3), moved rather than summarized.
- No rule changes meaning in the move. The Principles section stays byte-identical.
- The prompt revision (§5) is a separate commit.

## 3. Allocation table

Dispositions:

- **Keep:** unchanged.
- **Keep, rewritten:** merged or shortened; the draft text is in Appendix A.
- **→ WORKFLOW §n:** moved to the new document.
- **→ document:** moved to the document that owns it.
- **Delete:** its owner already states it.

### Principles and Implementation Design

| ID | Line | Item | Disposition |
| --- | --- | --- | --- |
| P1 | 1–23 | Principles and the revision paragraph | Keep, byte-identical |
| I1 | 27 | Priorities | Keep, rewritten: absorbs I5a |
| I2 | 29 | Roles | Keep, rewritten: adds that designs, decisions, delegations and reviews follow WORKFLOW |
| I3 | 30 | Diagnose from evidence | Keep |
| I4a | 31 | Local-change test | Keep |
| I4b | 31 | A local change's commit states its rule, for the family of forms, and the search | → WORKFLOW §6 |
| I4c | 31 | Design required; the five-link chain | Keep, rewritten: one sentence from the SPEC rule and its family of forms to what becomes unnecessary. The chain → WORKFLOW §2.2 |
| I4d | 31 | Further design contents (sites, options, deletions, size, performance) | → WORKFLOW §2.3 |
| I4e | 31 | A reviewer checks the design before coding | Merged into I15a |
| I4f | 31 | Delegation prompt contents | → WORKFLOW §4 |
| I4g | 31 | An implementer who finds the design wrong stops and reports | Keep |
| I5a | 32 | Comparison by the priorities; the coherence definition; never merge responsibilities or hide distinctions | Merged into I1 |
| I5b | 32 | Local patch definition; a correction inside the right owning component is a root fix | Keep, merged into I7a |
| I6a | 33 | What needs a recorded decision, who decides, implementers never introduce one | Keep, rewritten (I6b and I6c removed) |
| I6b | 33 | Two independently written designs for shared or cross-phase decisions | → WORKFLOW §2.4 |
| I6c | 33 | Read the DECISIONS and SETTLED entries first; a departure supersedes with its reason | → WORKFLOW §2.1 and §3 |
| I7a | 34 | Fix causes in shared structure; out-of-cause findings; one decision per owning phase; owners publish missing facts | Keep, rewritten (gains I5b) |
| I7b | 34 | One validator per IR; consumers trust it; `InternalInvariant_Kd` | → COMPILER_ARCHITECTURE (Dependency rules) |
| I8a | 35 | Share by meaning; the justification of a new concept; canonical-table rows; prohibitions; migrate every use | Keep, rewritten |
| I8b | 35 | Name in SPEC terms, state the contract, register as a canonical component | → COMPILER_ARCHITECTURE (Canonical components introduction) |
| I9 | 36 | Signs of a symptom fix | Keep |
| I10 | 37 | Second fix; closing an open family | Keep |
| I11 | 38 | The common fix exceeds the unit | Keep. "PLAN" becomes "ISSUES" if the PLAN Size Budget proposal lands first |
| I12a | 39 | Owner files, one canonical form, one name; consistency means the target structure | Keep, rewritten (I12b and I12c removed) |
| I12b | 39 | Split before exceeding the structure limits | Delete (COMPILER_ARCHITECTURE Structure limits) |
| I12c | 39 | Entry-file contract | → COMPILER_ARCHITECTURE (Canonical components), dropping the obsolete "Once the budget counts tokens (A1)" |
| I13 | 40 | Restructure and change behavior in separate commits | Keep |
| I14 | 41 | Remove what the change makes redundant; staged removals | Keep |
| I15a | 42 | Review before coding and before completion; redo, not patch | Keep, rewritten: one sentence |
| I15b | 42 | Review method: start from the problem, try a smaller design with a token estimate, the checks | → WORKFLOW §5 |
| I15c | 42 | Blocking and non-blocking findings; escalation after two rounds | → WORKFLOW §5 |
| I15d | 42 | Commit contents (root cause … what remains unverified) | → WORKFLOW §6 |
| I15e | 42 | Test another form under the same rule and an invalid counterpart | → V4 |
| I16 | 43 | Core libraries in Kimigayo; intrinsics; STYLE | Keep |

### Performance and Compiler Structure

| ID | Line | Item | Disposition |
| --- | --- | --- | --- |
| C1a | 47 | Native O2 run time and allocation counts may not regress | Keep, rewritten together with C2a |
| C1b | 47 | Compiler allocation policy; update the affected regressions in the same commit | → COMPILER_ARCHITECTURE (Performance and allocation already states the policy; add "in the same commit") |
| C2a | 48 | Follow COMPILER_ARCHITECTURE; spec revision does not authorize vocabulary or budget changes | Keep, rewritten together with C1a |
| C2b | 48 | Raising a cap or closing a stage above its ceiling | Delete (COMPILER_ARCHITECTURE Sizes) |
| C3 | 49 | Net size per area in commits; growth above 2,000 tokens explained | → WORKFLOW §6 |
| C4 | 50 | Stage-exit size and duplication records; restructuring after three consecutive units | → COMPILER_ARCHITECTURE (Structure limits) |
| C5 | 51 | Benchmark placement; fixed conditions; no warm-up | → VERIFICATION (measurements and allocation paragraphs, :179 and :202–:208; add only what they lack) |
| C6a | 52 | KR and IDE rules are build errors and never suppressed; the approval trailer | Keep, rewritten |
| C6b | 52 | No approval for pruning; evasions; CodeRuleException scope | → COMPILER_ARCHITECTURE (Code rules; the evasion list replaces "Mechanical evasions fail review") |

### Documentation, Diagnostics, Verification and Delivery, Artifacts

| ID | Line | Item | Disposition |
| --- | --- | --- | --- |
| D1 | 56 | Write in English; ownership; short rows | Keep |
| D2 | 58–70 | Ownership table | Keep; add the WORKFLOW row (Appendix A). The ISSUES row comes from the PLAN Size Budget proposal |
| D3 | 72 | Never narrow the specification; spec improvements | Keep |
| D4a | 73–74 | Proposal text only on explicit instruction in `draft/Proposals`; never change frozen text | Keep, rewritten into one row |
| D4b | 73–74 | Statuses, folders, intake records, closure; integration never implies support | → `draft/INTEGRATED.md`, which becomes the owner |
| G1–G2 | 78–79 | Diagnostics rows | Keep |
| V1–V3 | 83–94 | Serialization, units, the verification table, feedback and completion | Keep |
| V4 | 95 | Tests | Keep; gains I15e |
| V5–V6 | 96–97 | Never weaken checks; commit and push | Keep |
| R1–R2 | 101–102 | Artifacts | Keep |

### Destinations outside WORKFLOW

Moved sentences keep the base wording, except that "owner" becomes "project owner" where needed.

| Document | Receives |
| --- | --- |
| COMPILER_ARCHITECTURE | I7b (Dependency rules); I8b and I12c (Canonical components introduction, replacing "From Code Rules Analyzer A1 … (AGENTS)"); C4 (Structure limits); C6b (Code rules); C1b (Performance and allocation) |
| VERIFICATION | C5, only the sentences it does not already state; ":202 AGENTS' performance policy" becomes a COMPILER_ARCHITECTURE reference |
| `draft/INTEGRATED.md` | D4b; its first paragraph changes from "defined in `AGENTS.md` and summarized below" to owning the lifecycle |

### Measured effect

Building AGENTS.md from the base with §3 and Appendix A gives:

- 97 lines, 2,345 words, 16,267 characters: −22% against the base, and below `04873385`.
- Implementation Design 7,040 characters (from 9,774), Performance and Compiler Structure 660 (from 2,352), Documentation 2,904 (from 3,159).

## 4. WORKFLOW.md

- **Owns:** how a unit of work goes from a request to a pushed commit: design, choice, delegation, review, commit record, parallel units and stage handoffs, and the session templates in `docs/dev/prompts`.
- **Does not restate AGENTS:** it cites AGENTS rules by bullet title. VERIFICATION owns which checks run and how, DECISIONS the decisions, COMPILER_ARCHITECTURE the structure.
- **Read:** before writing a design, a decision, a delegation prompt or a review, and when starting parallel units. Delegation prompts name the sections the agent follows.
- **Size (estimate):** about 1,700–1,900 words, 12,000–13,000 characters.

| Section | Contents | Sources |
| --- | --- | --- |
| Header | Ownership, readers and triggers, relation to the other documents | — |
| §1 Unit flow | One table of step, role, output and where it is recorded. Steps: request → local test → design → choice → implementation (a restructuring commit first when needed) → review → verify, commit, push | AGENTS I2, I4, I13; r4 `common.md` "Process per unit" |
| §2 Design | **2.1** Design agents: read-only, independent of each other, about 150 lines; they read the DECISIONS and SETTLED entries of the area first. **2.2** The chain: required meaning (the SPEC rule and the family of forms it governs) → what the representation lacks or wrongly allows (for a defect, from a reproducer; hypotheses labelled) → owning component → shared representation or operation → what becomes unnecessary (for new work, why no existing concept expresses it). **2.3** Required sections: requirement; chain; owner and published facts; sites sharing the cause at path:line; design (members with contracts, files, structure limits, contract text of a new component, temporary structures with their removal stage); options (at least a correction inside the existing owning component and a change of the shared structure); deletions; DECISIONS entry text; tests and checks (valid and invalid, scaling, allocation); commits; forecast (net tokens per area, performance); risks and owner questions. **2.4** When two independent designs are required | I4c, I4d, I6b, I6c; r4 `design-template.md`, its stage items generalized |
| §3 Choice | One row per option: the coherence items of the priorities (pass or fail), net size over the affected scope and speed. Then: combining designs; the decision file (chosen design, amendments, hypotheses the implementer must confirm by reproducer); recording in DECISIONS; a departure supersedes an entry and states the reason that no longer holds | I1 (cited), I6c; practice of `u0a-decision.md` |
| §4 Delegation | Prompt contents: the standing request in the user's words (later questions do not narrow it), the design, completion conditions, required checks (VERIFICATION rows), worktree and branch, and the stop-and-report conditions (design wrong, owner decision, vocabulary or budget) | I4f; r4 `common.md` |
| §5 Review | Inputs. Method: first from the problem (the general rule; at least one smaller or more general design with a token estimate, shown worse or required), then the design followed, symptom signs, reuse, duplication, efficiency, structure limits, the other form and the invalid counterpart. Blocking and non-blocking lists. Verdict (accept, accept with fixes, reject) with findings at path:line. Rejected work is redone rather than patched. Two rounds on one family go to the project owner (representation, whitelist or hold) | I15b, I15c; r4 reviews |
| §6 Commit record | One short paragraph per field: root cause; concept; sites sharing the cause; deleted; rejected; tests; verified (evidence); unverified; size per area in non-trivia tokens with lines, against the forecast (COMPILER_ARCHITECTURE Forecasts), with growth above 2,000 tokens explained; DECISIONS entry; trailers. A local change records its rule (for its family of forms) and its search | I4b, I15d, C3; r4 `common.md` "Commit and push" |
| §7 Parallel units | One worktree and branch per unit; never two writers in one tree; formal runs only after the final rebase; sequential verification, commits and pushes on `dev`; separate TEMP directories; evidence copied out before reruns; documentation-only commits land between units | AGENTS V1 (cited); r4 `common.md` "Parallel units" |
| §8 Stage handoffs | A handoff holds the stage's state, plan, stage checks and scripts. It cites WORKFLOW and DECISIONS and restates neither. Decision files and reviews are kept as records | r4 `common.md`; DECISIONS header |

## 5. docs/dev/prompts (option A, owner, 2026-10-12)

The three session templates hold the user's inputs and session style. They stay as templates that cite AGENTS and WORKFLOW instead of restating them:

- implementation-planning.md §5–§6 and implementation-execution.md §2–§3 shrink to citations.
- The four conflicting statements (§1, cause 4) are removed and the stale anchors fixed.
- session-request.md changes only its references (PLAN §7 becomes ISSUES with the PLAN Size Budget proposal).

## 6. Rejected options

- **Wording only.** It removes at most 15–20%. It leaves the restated owners and the handoff copies, and denser wording is followed less.
- **Moving all of Implementation Design to WORKFLOW.** Its decision rules were added because sessions ignored the structure; a document read on demand would not reach implementers and one-off fixing sessions.
- **The procedure in VERIFICATION, DECISIONS or COMPILER_ARCHITECTURE.** They own checks, a decision log and the structure; mixing in the procedure would repeat cause 1 there.
- **Keeping the procedure in stage handoffs.** They are Git-ignored, copied per stage and invisible outside a stage.
- **A directory-scoped `src/Kimi/AGENTS.md`.** Planning starts at the repository root, the rules also govern the library, tests and tools, and agent tools load nested files differently.
- **The prompts as the procedure owner.** They carry session inputs and predate the priorities.
- **Retiring the planning and execution templates** (option B; owner, 2026-10-12). They stay as templates (§5).
- **Leaving the templates unchanged.** They contradict the priorities that the owner set to stop size-first changes.
- **Size budgets for AGENTS.md and WORKFLOW.md** (owner, 2026-10-12). No character budget and no replace-before-adding rule is added; the move records the measured sizes in its commit.

## 7. Application

**Preconditions:**

- The R4 exit is committed.
- No unit is running: `git worktree list` shows no `temp/r4-*`.
- No other session has pending AGENTS edits.
- The PLAN Size Budget proposal is applied first. It is accepted and edits AGENTS lines 38 and 72 and the Documentation table; this plan then carries its "ISSUES" wording.

**Commits** (documentation only; VERIFICATION "Documentation only": diff and reference review, no builds or tests):

1. **Move.**
   - WORKFLOW.md is written from the §4 sources.
   - AGENTS rows change per §3, with the Appendix A text.
   - COMPILER_ARCHITECTURE, VERIFICATION and INTEGRATED receive their rows.
   - The WORKFLOW Documentation row is added and the references below are fixed.
   - The DECISIONS Working process entry (Appendix B) and one PLAN_HISTORY line are added.
   - No rule changes meaning.
2. **Prompts.** §5.

**References to fix in commit 1:**

| Reference | Location | New target |
| --- | --- | --- |
| `AGENTS.md#diagnostic-development` | DIAGNOSTICS.md:245; prompts/implementation-execution.md:9 | The AGENTS Diagnostics section |
| AGENTS anchor | DIAGNOSTIC_WORKFLOW.md:5 | The AGENTS Diagnostics section |
| `AGENTS.md#implementation-workflow` | DIAGNOSTIC_WORKFLOW.md:15 | WORKFLOW |
| "(AGENTS)" | COMPILER_ARCHITECTURE:61 | Removed: the contract rule moves there |
| "(AGENTS, `8071549f`)" | COMPILER_ARCHITECTURE:210 | DECISIONS Q3 |
| "(AGENTS, Implementation Design)" | VERIFICATION:149 | Kept: the rule stays in AGENTS (I13) |
| "AGENTS' performance policy" | VERIFICATION:202 | COMPILER_ARCHITECTURE |
| Work policy sentence | PLAN:16 | AGENTS and WORKFLOW |

DECISIONS entries, PLAN_HISTORY rows and frozen draft text are records and stay unchanged.

**Review** (by an agent that did not write the change):

- A sentence-level map from the base AGENTS.md to the destinations: every sentence appears exactly once, with the same meaning.
- The Principles are byte-identical: lines 1–23 show no diff.
- WORKFLOW restates no AGENTS rule: AGENTS bullet titles appear there only as citations.
- No dangling links or anchors (`AGENTS.md#`, `WORKFLOW.md#`).
- Sizes are measured and recorded in the commit.

**Done when:**

- The sentence map is complete and the references resolve.
- The next stage's handoff cites WORKFLOW instead of copying r4 `common.md` and `design-template.md`.
- The commits are pushed.

**Unverified:**

- The size of WORKFLOW.md.
- Whether agents read WORKFLOW at its triggers. Check this at the first design and review after application: the delegation prompt names the sections, and the reviewer records whether the design follows §2.

## Appendix A. Rewritten AGENTS rows (draft)

Rows marked "Keep, rewritten" in §3, in AGENTS order. Rows marked "Keep" are unchanged. The measurement in §3 used this text.

```
Priorities: SPEC correctness and the native O2 and allocation guarantees are constraints. Within them, a coherent structure comes first: each concept and decision has one owning component, each fact one representation, each concept one canonical path and a contract stated without modes or special cases, and every dependency follows COMPILER_ARCHITECTURE. Then the smaller net size over the affected scope, deletions included, then faster compilation. Designs and options are chosen in this order; never merge distinct responsibilities, or hide a distinction that the SPEC or a contract needs, to save concepts or tokens. A small diff, low test churn or ease of review are not criteria.

- **Roles.** The owner in this file and COMPILER_ARCHITECTURE is the project owner, the user; a component that owns a decision is its owning component. The planning session, the session that received the request, designs or commissions the design and decides; the implementer follows the chosen design; a reviewer is an agent that wrote neither the design nor the code under review, so a session working alone delegates the review. Designs, decisions, delegations and reviews follow [WORKFLOW](docs/dev/WORKFLOW.md).
- **Design before code.** A change is local when it stays inside one owning component, leaves its responsibility, contract and invariants unchanged, adds no branch, parameter, state or table, and the search for sites sharing its cause finds none. Every other change first gets a design, written and reviewed as WORKFLOW describes, that starts from the required meaning (the SPEC rule and the family of forms it governs, not the failing input) and ends with the special cases, state, conversions and paths that become unnecessary. An implementer who finds the design wrong stops and reports instead of patching around it.
- **Record design decisions.** A new representation, component, vocabulary entry or temporary structure, or a responsibility moved between phases, is decided in the design step by the planning session (by the project owner where this file or COMPILER_ARCHITECTURE requires it) and recorded in [DECISIONS](docs/dev/DECISIONS.md) in the commit that first implements it; an implementer never introduces one on its own.
- **Fix causes in shared structure.** A local patch, a fix that leaves the cause or a duplicated decision in place, is rejected for that reason, not for its size; correcting the rule inside the right owning component is a root fix, however small. Changing the representation or responsibility that causes a defect is in scope, even across phases; only changes the cause does not require are unrelated redesign. Findings outside the unit's cause become open issues or separate units, not fixes in passing. Each decision is made once, by its owning phase; a consumer lacking a fact has the owning component publish it instead of re-deriving it from syntax or another phase's state.
- **Share decisions with the same meaning.** Decisions and operations with the same meaning, contract and invariants share one owning component; a similar shape under a different contract stays separate. A new concept answers a recurring decision or a responsibility that no existing concept holds: it states the decisions it owns and the duplicated decisions, state, conversions or paths it removes, or, for new work with nothing to remove, why no existing concept expresses it; it migrates every existing use in the same unit and is registered as COMPILER_ARCHITECTURE describes. Where a canonical table exists (COMPILER_ARCHITECTURE), a new form under its contract adds a row, not a case; elsewhere a table replaces branches only when every row has one interpretation. No mechanism for a superficial similarity or a speculative use; no wrappers without semantics, single-implementation interfaces or utility bags.
- **Keep the structure predictable.** A responsibility's code lives in its owning component's files (`Owner.Concern.cs`), each concern uses its one canonical form, and a concept has one name in every phase. Consistency means the canonical forms and the target structure in COMPILER_ARCHITECTURE, not the surrounding code: where an area still deviates (the old Analysis/Emission paths, Binding code awaiting R7), do not copy its patterns.
- **Independent review.** A reviewer checks the design before coding and the change before completion, starting from the problem rather than the diff, as WORKFLOW describes; a rejected design or diff is redone, not patched over.

# Performance and Compiler Structure

- Generated native code may not regress in O2 run time or allocation counts. Follow [COMPILER_ARCHITECTURE](docs/dev/COMPILER_ARCHITECTURE.md) for dependencies, closed vocabularies, canonical components, structure limits, compiler performance and allocations, size caps, stage gates and the migration freeze. Permission to revise the specification does not authorize vocabulary or budget changes.
- KR rules and IDE0051/IDE0052/IDE0060 are build errors; never suppress them. A code-rules change that COMPILER_ARCHITECTURE reserves to the project owner carries `Code-Rules-Approved: <approver> <yyyy-MM-dd>: <reason>`, checked by `scripts/verify-commit.ps1`.

Documentation table, after the VERIFICATION row:
| [WORKFLOW](docs/dev/WORKFLOW.md) | How a unit goes from request to pushed commit: design, choice, delegation, review, commit record, parallel units and stage handoffs, and the session templates in `docs/dev/prompts`. Read before writing a design, a decision, a delegation prompt or a review. |

Documentation, replacing lines 73–74:
- Create or change proposal text only on explicit instruction, in `draft/Proposals`; never change frozen text, including typos. [INTEGRATED](draft/INTEGRATED.md) owns the draft lifecycle: statuses, folders, intake records and closure.

Verification and Delivery, appended to the tests row:
A rule is tested with another form under it and an invalid counterpart where relevant.
```

## Appendix B. DECISIONS entry (draft)

Use the next free Q id (Q5 at `ec7585f7`).

```
- **Q5 AGENTS restructuring and the workflow document** (<date>, owner).
  - Adopted: AGENTS.md keeps the decision rules every session applies; docs/dev/WORKFLOW.md owns the procedure of a
    unit (design, choice, delegation, review, commit record, parallel units, stage handoffs) and the session templates,
    and cites AGENTS without restating it; rules another document owns move to it (COMPILER_ARCHITECTURE,
    VERIFICATION, draft/INTEGRATED.md). Stage handoffs cite WORKFLOW and add only stage items. The session templates in
    docs/dev/prompts stay and cite AGENTS and WORKFLOW instead of restating them.
  - Reasons: AGENTS.md grew 19% over 8 commits in one day and mixed decision rules with role procedures that every
    session loads; the procedure lived in Git-ignored per-stage copies; tracked prompts contradicted the priorities.
  - Rejected: wording only; moving all of Implementation Design; the procedure in VERIFICATION, DECISIONS or
    COMPILER_ARCHITECTURE; stage handoffs as its owner; a directory-scoped AGENTS.md; retiring the planning and
    execution templates; character budgets for AGENTS.md and WORKFLOW.md.
```
