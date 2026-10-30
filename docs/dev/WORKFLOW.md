# Change Workflow

This document owns how a change is classified, designed, reviewed, implemented and completed. Roles and principles are
in [AGENTS](../../AGENTS.md) (Implementation Design); checks are in [VERIFICATION](VERIFICATION.md).

## Change classes

| Class | Definition | Flow |
| --- | --- | --- |
| **Minor** | A local change: it stays in one owning component, keeps its contract and invariants, adds no branch, parameter, state or table, and a search finds no other site with the same cause. Its commit states the rule and the search. | Implementation → review |
| **Major** | Any change that is not Minor, Full or Owner. | Design → design review only for an open design issue → implementation → review |
| **Full** | A change to a shared representation or across phases (formerly "the full process"). | Problem statement → shared acceptance tests → two candidates, design-first and prototype-first, judged by the tests → choice by results → review of the chosen design and implementation ([Full changes](#full-changes)) |
| **Owner** | A change that would be Full, whose contract an owner-approved design already fixes, that design having passed a review and being recorded in DECISIONS. | Design → implementation → review |

- An Owner change that departs from the approved contract, including a new representation, component or vocabulary
  entry, becomes a Full change.
- The units implementing one group that an owner-approved design names (for example an A5 family) may share one review,
  in place of each unit's own, before the group is complete. Each unit is still verified and pushed; none merges into
  dev or main before that review passes. A unit that departs from the design leaves the group and becomes a Full change.
- A documentation-only change that records facts already decided (in DECISIONS or by the owner) or verified by the
  unit's checks needs no independent review: PLAN, STATUS, CODEMAP, PLAN_HISTORY, VERIFICATION evidence, and
  VERIFICATION procedures corrected to match changed tooling. Every other document keeps its review, including SPEC,
  IMPL, AGENTS, this document, DECISIONS, SETTLED, STYLE and proposals.
- R4's exit, before R6-check, adds a final end-to-end review of A5 and A6 (DECISIONS R4-M35).

## Design

1. Establish the failing condition and its cause from the code and a reproducer. Label an unverified hypothesis as one.
2. The planning session classifies the change; a reviewer who finds the class wrong reports it as a blocking finding.
3. A change other than Minor gets a short design as one chain: the required meaning (the SPEC rule and the forms it
   governs) → what the representation lacks or wrongly allows → the owning component that decides it → what becomes
   unnecessary. It lists the sites sharing the cause ([CODEMAP](CODEMAP.md)), the options (at least a fix inside the
   owning component and a change of shared structure), the deletions and the expected size and speed.
4. A Major change gets a design review only when a design issue must be settled before implementation, for example:
   the cause is still a hypothesis; the options differ in owning component or representation; the design departs from
   a DECISIONS or SETTLED entry; the sites sharing the cause span several components. The planning session decides and
   states the reason in the design. Otherwise the one review before completion covers the design (DECISIONS Q5).
5. A delegated prompt carries the request, the design, the completion conditions and the checks.

## Implementation

- Implement the design, or for a Minor change the fix its cause requires, with focused tests. While fixing, use
  incremental builds and selected tests only.
- An implementer who finds the design wrong stops and reports to the planning session.

## Working-time limit

A unit's design work and its implementation work each stop when the agent's working time on them exceeds one hour
(DECISIONS Q12).

- **Counting.**
  - Working time is summed per unit over all its rounds and agents; fix rounds do not reset it.
  - It excludes heavy runs and the wait for one (a full Session, a full mir ledger, native or milestone sweeps), another
    session's merge window or HOLD, waiting for the owner, and usage-limit stops.
  - The agent records its start time in its state file and checks the elapsed time at each step.
- **Stopping.**
  - The agent finishes the running command and starts no new build or test.
  - It pushes a WIP commit if the tree builds, and removes any heavy-run registration.
  - It records what is done, the findings (causes, counterexamples, probes), why the unit grew, the open issues and the
    next steps.
  - Unfinished design text and code may be discarded; the findings carry over.
- **Re-planning.** From the record, the planning session writes a plan of small, verifiable units. Each one:
  - is expected to finish within one hour;
  - has one owning component;
  - has tests that fail before it and pass after it;
  - has stated completion conditions.

  A behavior-preserving restructuring comes first. No unit that weakens soundness lands alone, for example a premise
  removed before its call-side proof.
- **Approval.**
  - The planning session reports the stop and the plan to the owner, and starts the split units only on the owner's GO
    or an explicit equivalent. A relayed answer recorded verbatim in a pushed commit counts.
  - Independent units continue meanwhile.
- **Repetition.**
  - A split unit that exceeds the limit again is split by the same steps.
  - A unit split twice for the same problem that still does not fit does not converge (Owner decisions).
- **Sizing first.** The planning session sizes each unit before it starts, and splits one not expected to fit in one
  hour.
- **Scope.** The limit applies to every agent of every planning session. Reviews follow Repeated reviews instead.

## Full changes

A Full change is decided by results: the same tests judge two candidates written independently, and the planning
session decides the design by choosing one.

1. **Problem statement.** The planning session states the required meaning (the SPEC rules and the owner's answers that
   govern the forms) and the acceptance forms with their verdicts, each with its valid or invalid counterpart. It names
   no mechanism.
2. **Acceptance tests.** An author who writes neither candidate turns the problem statement into tests before either
   candidate is implemented, at the lowest public layer, failing where the change alters behavior. The planning session
   checks the tests against the problem statement before the candidates use them. Rows that wait for an owner answer, or
   whose verdict the problem statement leaves to a design, are kept apart: they do not count toward passing, and they
   join the tests when the answer or the choice fixes them.
3. **Two candidates, written independently.** Neither author reads the other candidate's design or code before the
   choice. Each candidate is built on its own branch from one base; the two are one unit, and only the chosen one merges.
   Each delegated prompt carries the problem statement and the tests.
   - Design-first: a design ([Design](#design) step 3), a structure check by the planning session (the design goes
     through the owning components and adds no symptom fix), then the implementation. A design that fails the check is
     revised once; a second failure ends the candidate.
   - Prototype-first: an implementation iterated against the tests, then a design note covering Design step 3.
   - A candidate ends when it passes every counted test or its author reports the rows it cannot pass and why.
4. **Choice.** The planning session chooses, with the session that owns the files where it is another's. A candidate
   qualifies when it passes every counted test, shows no regression against the completion checks' baselines
   ([VERIFICATION](VERIFICATION.md#verification-scope)) and goes through the owning components; among qualified
   candidates, size and cost decide. The choice and its reasons are recorded; the other candidate's branch and results
   stay as evidence.
5. **Review** of the chosen design and implementation together, the problem statement and the tests included: an
   implementation review under [Review](#review)'s verdicts and counts, then [Completion](#completion).

## Review

The reviewer starts from the problem, names the general rule and tries a shorter or more general design. It checks that
the design was followed and that no symptom fix (AGENTS) remains. Blocking: a SPEC or contract violation, unsound
acceptance, wrong result, internal fault, regression, departure from the design, symptom fix or wrong class; other
findings become open issues. The reviewer states what is wrong and which rule it breaks, and writes no fix, so it never
becomes an author of the design or the implementation.

| Verdict | Design review | Implementation review | Next |
| --- | --- | --- | --- |
| **ACCEPT** | No blocking finding. | No blocking finding. | Implementation, or completion. |
| **REVISE** | The design can be corrected without changing its cause analysis or chosen option. | The implementation and tests can be fixed without changing the approved design. | The planning session fixes the design, or the implementer the change. |
| **REJECT** | The cause analysis or the chosen option must be revised. | The cause analysis or the approved design must be revised. | The planning session revises the analysis or the design; after an implementation review, the implementer changes the implementation to match. |

- A fix starts from the reviewed design or change but never keeps a wrong structure for that reason: reuse the
  reproducer, valid tests and unaffected code, and replace the structure the finding concerns where needed.
- The first review presents together every blocking finding it can check. A blocking defect found later is still
  reported, marked as missed earlier, introduced by a fix, a departure from the design, or a newly established fact
  (such as a constraint the implementation revealed).

### Repeated reviews

- The implementer and the reviewer continue as the same agents across reviews; the reviewer of a design review also
  reviews the implementation.
- Each repeated review identifies the reviewed version (a commit or a diff snapshot) and its diff from the previous one,
  and checks:
  1. the previous findings are resolved;
  2. the diff;
  3. the contracts and use sites the diff affects, including unchanged callers and consumers;
  4. no fix is a symptom fix;
  5. after a design revision, the earlier judgments it invalidated.
- Reviews count per unit, separately for design reviews and implementation reviews, and accumulate: revising the design
  or returning to a design review does not reset either count. A Full change's review of the chosen candidate and the
  diff review after a failed completion check are implementation reviews.
- A unit does not converge when two reviews' blocking findings share a cause or when its third design review or third
  implementation review does not ACCEPT; a Full change also when no candidate qualifies at the choice or the choosing
  sessions disagree. The planning session then suspends the unit and asks the owner how to proceed
  ([Owner decisions](#owner-decisions)). Any change stays on a `held/…` branch with its evidence and merges into neither
  dev nor main, and the unit resumes only on the owner's answer.
- A review count is never a reason to ACCEPT a change with a blocking defect.

## Owner decisions

At any point, suspend the unit and ask the owner on finding:

- a contradiction in the specification;
- a significant measured slowdown of compilation or generated code;
- a way to make the specification simpler and cleaner by unifying concepts;
- a unit that does not converge ([Repeated reviews](#repeated-reviews), [Working-time limit](#working-time-limit));
- a unit stopped by the working-time limit, with its split plan for the owner's GO
  ([Working-time limit](#working-time-limit)).

The report names the SPEC sections, the measurements or evidence (for a unit that does not converge, each review's
findings or each candidate's results and the cause analysis one level higher), and the options (a representation, a
whitelist, a redesign, a hold or another) with a recommendation; PLAN records the question. Independent units continue.

## Completion

1. After ACCEPT, run the completion checks that [VERIFICATION](VERIFICATION.md#verification-scope) requires on the final
   inputs. Reuse successful evidence for the same inputs, configuration and coverage instead of repeating it. After a
   failed check, the fix gets a diff review and the checks it invalidated run again.
2. Commit, associate the evidence with `scripts/verify-commit.ps1` and push (AGENTS Verification and Delivery).
3. The commit states the root cause, the concept changed, the sites sharing the cause, the deletions, the rejected
   options and what remains unverified.
