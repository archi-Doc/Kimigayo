# A5 step 0 records

Status (2026-10-17): step 0 of [the A5 plan](../../Proposals/2026-10-16%20Complete%20Polonius%20A5%20Plan.md) (plan §6.2)
is in progress. The design is not final:

- design A2 is in review;
- round 2 returned "revise";
- the owner's decision on the access-check family is pending (see below).

These are working records, not authority. SPEC and DECISIONS decide.

## Files

| File | Content |
| --- | --- |
| [audit-core.md](audit-core.md) | Rule audit for F1, F2 and F10; MIR producer gaps G1–G7; S1 and §14.10.3 findings |
| [audit-calls.md](audit-calls.md) | Rule audit for F3 and F5–F9, A6; the Binding fact inventory; C5 and S2 |
| [audit-origins.md](audit-origins.md) | F4; Origin strata; S3; the Origin boundary table B1–B20 |
| [census.md](census.md) | Family sizes, old-path codes, the SPEC-example oracle, Semantics views, the ledger command |
| [design-A.md](design-A.md), [design-B.md](design-B.md) | Two independent designs written from one brief |
| [choice.md](choice.md) | Planning-session choice: A as the base, with candidate grafts from B |
| [review-design.md](review-design.md) | Independent review, round 1: revise (3 blocking) |
| [design-A2.md](design-A2.md) | Full revision of A after round 1 |
| [review-design-r2.md](review-design-r2.md) | Independent review, round 2: revise (2 blocking) and the owner decision list |
| [prototype.md](prototype.md), [A5RulesPrototype.cs](A5RulesPrototype.cs) | Executable rules prototype: 75 hand-encoded programs, rounds 1–5 |

S1's own prototype is `draft/Sketches/LoanIdentityByBase.mjs` on branch `claude/gracious-brahmagupta-85336b`, from the
S1/S2 proposal session.

## Conventions

- `S\` or `S/` in these files means this folder.
- `plan-snapshot.md` and `P:n` refer to the plan before its first commit (`e32dc0ef`), so line numbers differ.
- The census helper scripts were not kept. `census.md` gives the regexes.

## Open owner decision

Under AGENTS, two review rounds on one family of findings move the decision to the owner. The family is the one that
asks what an access at P is checked against. The owner chooses one of:

- **Representation** (recommended): add the transit clause to the check set. A value that P stores into a holder live
  after P, into `E`, or through a reference is checked too. Prototype round 5 validated this as the fix for case 24c.
- **Hold.**

A2 must also gain a destroy access for calls whose row destroys their target (review round 2, finding 2). That
finding is new, not a second round.
