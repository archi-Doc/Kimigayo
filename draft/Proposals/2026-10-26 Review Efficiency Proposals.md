# Review Efficiency Proposals (WORKFLOW, DECISIONS Q10)

Status: proposal (the owner asked on 2026-10-26 for it to be placed here). Author: the R5 planning session. Scope: `docs/dev/WORKFLOW.md`
"Review", "Repeated reviews" and the documentation exemption; `docs/dev/VERIFICATION.md`'s checks (proposal 3); PLAN's open-issue rows
(proposal 4). Nothing here is adopted until the owner revises WORKFLOW.

Term: a **fold** is a commit that applies non-blocking review findings to a unit after its review returned ACCEPT and before it merges.

## 1. Evidence (R5's landings, before and after Q10)

| | Before Q10 (2026-10-24 21:38 – 10-25 20:50) | After Q10 (2026-10-25 22:35 – 10-26 10:10) |
| --- | --- | --- |
| Units landed | 6 (C6a/C6b, G157 (21), C6c, C8b, C8c, C8d) | 3 (W1 rule 1, C8e, C8f) |
| Throughput | about 0.26 units per hour | about 0.26 units per hour |
| Reviewer turns per unit | about 1 (folds read by the planning session) | about 4.3 (W1 rule 1: 3; C8e: 4; C8f: 6) |

Confounders after Q10: three designs (C8e, C8f, W1 rule 1) and several peer reviews also ran; C8e waited about 3.5 hours for another
session's unit; C8f's two REJECTs came from the planning session's own ruling error. The sample is one session's and small.

What Q10 improved:
- The same reviewer is resumed for repeated reviews and diff reviews, so it does not re-read the unit.
- A fix starts from the reviewed change: C8f's two revisions were one commit each.
- The rule that the planning session redoes the cause analysis after two reviews whose blocking findings share a cause, and the hold after a
  third implementation review without ACCEPT, forced C8f's root cause (a requesting site's prerequisites are read at that site) and led to a
  design review before the third implementation review, which then passed.
- The change classes: W1 rule 1, classed Major with a design review, passed its first implementation review.

What costs more than it returns:
- R5 gives every fold a diff review by the same reviewer (its reading of Q10; WORKFLOW names a diff review only after a failed completion
  check, and counts that one as an implementation review). C8e spent a REVISE round on one comment line of 166 characters, and R5 counted
  C8e's fold reviews toward its review count but not C8f's: WORKFLOW does not say whether a fold's diff review counts.
- A DECISIONS entry that only records an owner's confirmation verbatim still needs a review (R5-M125, R5-M127), since WORKFLOW's
  documentation exemption excludes DECISIONS.
- Non-blocking findings become open issues one unit at a time, which keeps PLAN at its 225-line limit; PLAN G157 holds 28 open items,
  numbered up to (34).

## 2. Proposals

1. **Folds.** State in "Repeated reviews" that a fold gets the same reviewer's diff review and that this review does not count toward the
   third-review hold (the hold stays for reviews of the unit's design and behavior).
2. **Owner records.** A DECISIONS entry that only records an owner's decision verbatim, with the consequences the owner's question already
   stated, joins the documentation-only changes that need no independent review.
3. **Machine checks before review.** Limits a script can check (lines over 160 characters, COMPILER_ARCHITECTURE's structure limits) are
   checked by `scripts/verify.ps1` or the strict build, so a review never spends a round on them. This changes VERIFICATION's checks and
   needs the owner's explicit instruction.
4. **Open issues by group.** Non-blocking findings of a group's units are recorded in PLAN at the group review that closes the group, as
   one entry per group, instead of one per unit.

## 3. How the Principles and AGENTS hold

- Proposal 1 keeps a reviewer who wrote neither design nor code on every change that reaches review (AGENTS Roles); only the counting
  changes.
- Proposal 2 removes a review whose content is the owner's own words; the entry's other text keeps its review.
- Proposal 3 moves a check from a reviewer to a script; it changes VERIFICATION, so it needs the owner's explicit instruction.
- Proposal 4 keeps open issues in PLAN (AGENTS forbids a management document for routine bookkeeping) and changes only when they are
  written.
- No blocking finding loses its reviewer: behavior changes, expectation changes and every first review keep WORKFLOW's flow.
