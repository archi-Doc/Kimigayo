# Change Workflow

This document owns how a change is classified, designed, reviewed, implemented and completed. Roles and principles are
in [AGENTS](../../AGENTS.md) (Implementation Design); checks are in [VERIFICATION](VERIFICATION.md).

## Change classes

| Class | Definition | Flow |
| --- | --- | --- |
| **Minor** | A local change: it stays in one owning component, keeps its contract and invariants, adds no branch, parameter, state or table, and a search finds no other site with the same cause. Its commit states the rule and the search. | Implementation → review |
| **Major** | Any change that is not Minor, Full or Owner. | Design → design review only for an open design issue → implementation → review |
| **Full** | A change to a shared representation or across phases (formerly "the full process"). | Problem statement → shared acceptance tests → two independent lanes, design-first and prototype-first, judged by the tests → choice by results → one review of the chosen design and implementation ([Full changes](#full-changes)) |
| **Owner** | A change that would be Full, whose contract an owner-approved design already fixes, that design having passed as a Full change and being recorded in DECISIONS. | Design → implementation → review |

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

## Full changes

A Full change replaces the single design with two lanes that the same tests judge, so the choice rests on results rather than on
reading two designs.

1. **Problem statement.** The planning session states the required meaning (the SPEC rules and the owner's answers that govern the
   forms) and the acceptance forms with their verdicts, each with its twin. It names no mechanism.
2. **Acceptance tests.** An author who writes neither lane turns the problem statement into tests before either lane implements, at the
   lowest public layer, failing where the change alters behavior. Rows that wait for an owner answer, or that the problem statement
   lets a design decide, are grouped and scored apart.
3. **Two lanes, written independently.** Neither lane reads the other's design or code before the choice.
   - Design-first: a design (the chain in [Design](#design)), a short structure check by the planning session (the design goes through
     the owning components and adds no symptom fix), then a prototype.
   - Prototype-first: a prototype iterated against the acceptance tests, then a design note stating the chain, the owners, the
     deletions and what remains open.
4. **Choice.** The planning session chooses by results, with the session that owns the files where it is another's: every acceptance
   test passes; no regression against the completion checks' baselines (diagnostic snapshot, MIR ledger, the probe census); the change
   goes through the owning components; size and cost. It records the choice and its reasons.
5. **One review** of the chosen design and implementation together (an implementation review), then [Completion](#completion).

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
  or returning to a design review does not reset either count. A Full change's review of the chosen lane and the diff
  review after a failed completion check are implementation reviews.
- A unit does not converge when two reviews' blocking findings share a cause, when its third design review or third
  implementation review does not ACCEPT, or, for a Full change, when neither lane passes the acceptance tests or the chosen
  lane's review does not ACCEPT. The planning session then suspends the unit and asks the owner how to proceed: any change
  stays on a `held/…` branch with the evidence it has and merges into neither dev nor main; the owner receives each review's
  findings or each lane's results, why they did not converge and the options (a representation, a whitelist, a redesign, a
  hold or another) with a recommendation; PLAN records the question. The unit resumes only on the owner's answer.
  Independent units continue.
- A review count is never a reason to ACCEPT a change with a blocking defect.

## Owner decisions

At any point, suspend the unit and ask the owner on finding:

- a contradiction in the specification;
- a significant measured slowdown of compilation or generated code;
- a way to make the specification simpler and cleaner by unifying concepts;
- a unit that does not converge ([Repeated reviews](#repeated-reviews)).

The report names the SPEC sections, the measurements or evidence, and the options; PLAN records the question.
Independent units continue.

## Completion

1. After ACCEPT, run the completion checks that [VERIFICATION](VERIFICATION.md#verification-scope) requires on the final
   inputs. Reuse successful evidence for the same inputs, configuration and coverage instead of repeating it. After a
   failed check, the fix gets a diff review and the checks it invalidated run again.
2. Commit, associate the evidence with `scripts/verify-commit.ps1` and push (AGENTS Verification and Delivery).
3. The commit states the root cause, the concept changed, the sites sharing the cause, the deletions, the rejected
   options and what remains unverified.
