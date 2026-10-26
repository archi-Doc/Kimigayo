# Review Efficiency Proposals (WORKFLOW, DECISIONS Q10)

Status: proposal (the owner asked on 2026-10-26 for it to be placed here). Author: the R5 planning session. Scope: `docs/dev/WORKFLOW.md`
"Review", "Repeated reviews" and the documentation exemption; `docs/dev/VERIFICATION.md`'s checks; PLAN's open-issue rows. Nothing here is
adopted until the owner revises WORKFLOW.

## 1. Evidence (R5's landings, before and after Q10)

| | Before Q10 (2026-10-24 21:38 – 10-25 20:50) | After Q10 (2026-10-25 22:35 – 10-26 10:10) |
| --- | --- | --- |
| Units landed | 6 (C6a/C6b, G157 (21), C6c, C8b, C8c, C8d) | 3 (W1 rule 1, C8e, C8f) |
| Throughput | about 0.26 units per hour | about 0.26 units per hour |
| Reviewer turns per unit | about 1 (folds read by the planning session) | about 4.3 (W1 rule 1: 3; C8e: 4; C8f: 6) |

Confounders after Q10: a group review, two designs and five peer reviews also ran; C8e waited about five hours for another session's unit;
C8f's two REJECTs came from the planning session's own ruling error. The sample is one session's and small.

What Q10 improved:
- The same reviewer, resumed for repeated and diff reviews, answers in seconds to minutes, without re-reading the unit.
- A fix starts from the reviewed change: C8f's two revisions were one commit each.
- "Two reviews whose findings share a cause → redo the cause analysis" and "a third review without ACCEPT holds the unit" forced C8f's root
  cause (a requesting site's prerequisites are read at that site) and led to a design review before the third implementation review, which
  then passed.
- The change classes: W1 rule 1, classed Major with a design review, passed its first implementation review.

What costs more than it returns:
- Every change after ACCEPT needs a diff review, including format-, comment- and test-only folds; C8e spent a REVISE round on one comment
  line of 166 characters.
- A DECISIONS entry that only records an owner's confirmation verbatim still needs a review (R5-M125, R5-M127).
- "Non-blocking findings become open issues" keeps PLAN at its 225-line limit; PLAN G157 holds 34 items.
- WORKFLOW does not say whether a diff review after ACCEPT counts toward the three-review hold.

## 2. Proposals

1. **Light post-ACCEPT folds.** A change after ACCEPT that is format-, comment- or test-only (no compiler behavior, no expectation weakened)
   is checked by the planning session's own reading of its diff; any other post-ACCEPT change keeps the same reviewer's diff review. State
   in "Repeated reviews" that a post-ACCEPT diff review does not count toward the three-review hold.
2. **Owner records.** A DECISIONS entry that only records an owner's decision verbatim, with the consequences the owner's question already
   stated, joins the documentation-only changes that need no independent review.
3. **Machine checks before review.** Limits a script can check (lines over 160 characters, the structure limits of COMPILER_ARCHITECTURE)
   are checked by `scripts/verify.ps1` or the strict build, so a review never spends a round on them.
4. **Open issues by group.** Non-blocking findings are recorded at the group review that closes a unit's group (or in an open-issues file
   beside PLAN), not per unit, keeping PLAN within its limit and readable.

## 3. How the Principles hold

Each proposal removes a review round or a document row whose finding a cheaper owner already gives (the planning session's diff reading,
the owner's own words, a script). No blocking finding loses its reviewer: behavior changes, expectation changes and every first review
keep WORKFLOW's flow. Local reasoning and explicit semantics are unaffected.
