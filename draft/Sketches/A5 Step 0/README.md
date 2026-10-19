# A5 step 0 records

Status (2026-10-17): step 0 is complete (unit 0b closed by prototype round 10); the step 0 design work of [the A5 plan](../../Proposals/2026-10-16%20Complete%20Polonius%20A5%20Plan.md)
(plan §6.2) is done.

- **Current design: [design-A3.md](design-A3.md).** Review round 3 confirmed it.
- The owner approved its §9 decisions on 2026-10-17 (see "Decisions taken").
- The step 0 implementation units (A3 §10: ledger re-baseline, MIR fact contracts, A4 keys, Binding views) follow.

These are working records, not authority. SPEC and DECISIONS decide. DEC entries come with the commits that first
implement them.

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
| [review-design-r2.md](review-design-r2.md) | Round 2: revise. One blocking finding was a second round in one family, so the owner chose |
| [design-A3.md](design-A3.md) | Full revision after round 2 and the owner's decision, with round 3's four corrections |
| [review-design-r3.md](review-design-r3.md) | Round 3: confirmed |
| [prototype.md](prototype.md), [A5RulesPrototype.cs](A5RulesPrototype.cs) | Executable rules prototype: 425 hand-encoded programs, rounds 1–17 (round 7 is A3 unit 0b; round 8 the case 23a proposal review; round 9 PLAN G113; round 10 step 1's 1b design diff, which closed 0b; round 11 the core lane review's B1, B2 and B4 for errata E13 and E14; round 12 E13's operand evaluation order, with the E13 design review's cases; round 13 E13 as rewritten after its second design review, "one rule for every access", with that review's cases; corrected on 2026-10-18 for case 75; round 14 group 1c's 26b, an activation meets only shared Loans, DECISIONS R4-M68; round 15 PLAN G148's store rules, merged from its design reviews' union prototype, 382 cases; round 16 G148's final review B1, route taint, superseded; round 17 the owner's body hold, with round 16's review's cases) |
| [RustComparison.rs](RustComparison.rs) | rustc check of rounds 8 and 9: 23a, 34c, 35a, 35b, 36f and the round-9 review forms R5 and R6 compile; with `--cfg neg`, 34a and 23a's old slot are E0506 |

S1's own prototype is `draft/Sketches/LoanIdentityByBase.mjs` on branch `claude/gracious-brahmagupta-85336b`, from the
S1/S2 proposal session.

## Decisions taken

- **2026-10-16, owner:** simple first, then fast, then precise. Soundness is never traded. Each precision loss is a
  SPEC simplification (plan OD12, OD13).
- **2026-10-17, owner:** for the access-check family, the owner chose the representation: the transit clause in the
  check set.
- **2026-10-17, owner:** design A3 §9 items 2–9 were approved as recommended:
  - losses P1, P3, P4, P10 and P11, plus the optional P5, P6 and P9;
  - relaxations (a) and (b);
  - the S1 additions, c1/c2, V16, the Origin boundary, the MIR contracts, and the DEC and DIAGNOSTICS records;
  - not adopted: the root-most kill recovery and relaxation (c).
- **2026-10-17, owner:** design A3 §9 item 10, the needed loss P12 (`desc(c)`, PLAN G113, prototype round 9), was
  approved: 「承認します。」 (relayed by the planning session)
- **2026-10-17, owner:** design A3 §9 item 11 (DECISIONS R4-M52): the step 1 choices of `../A5 Step 1/choice-1b.md` §4,
  each as recommended: G92b the fold, G92a assumed completion with α, the case 23a proposal as option (c) (A3 §12 E8),
  and the SPEC revision in a separate session.
- **Planning session:**
  - design A is the base;
  - A3 follows the plan's `within` kill, never across a borrow Deref;
  - the `replace` exemption follows the plan.

## Conventions

- `S\` or `S/` in these files means this folder.
- `plan-snapshot.md` and `P:n` refer to the plan before its first commit (`e32dc0ef`), so line numbers differ.
- The census helper scripts were not kept. `census.md` gives the regexes.
