# G160 plan: final consistency check (round 7)

Reviewer: the reviewer of rounds 1–6. This check covers consistency only, not design. Scope:

- the plan (851 lines);
- PLAN's G160 and G182 rows;
- the INTEGRATED entry.

All three are in worktree `bin/worktrees/g160-plan`, uncommitted.

## Agreement with the round-6 verdict

- **No stale hold claims.** `Held`, W rule 4 and "never an unsound acceptance" are gone from §0–§10. §4.2's `MirWrites` has
  `Through`, `ThroughArgument` and `Unowned`; §4.3 ends with rule 4 "Deleted". "R4-M73's body hold" survives only in §11's round-5
  history, which round 6's entry marks as withdrawn. The only remaining holds are P13's, which are legitimate.
- **§1.2 cells agree with the escalation.** The `OwnRefs`, `ownRefsRead`, `initThenRead`, `projOwner` and `MarkBox` cells all say
  "Q5's interim", with `MarkBox` marked as an over-catch.
- **§1.3 and §4.1 agree.** §1.3 states the P13-style hold's insufficiency and the options, and §4.1's marked Fields are exactly
  the class.
- **The escalation is stated honestly.**
  - §4.6 gives (a), (b)(1)–(3) and (c). (b) is unconditional and A5-owned, with G182 as its removal condition.
  - §10 Q5 says a P13-style hold is no option and that the owner chooses; the planning session's suggestion is (b)(2).
  - §9 has G160-3 implement the interim the owner picks, and §11 records round 6 and the plan's state.
  - The G182 row and the INTEGRATED entry match these.
- **Nothing blocking remains outside the escalated family.** The call side, the G182 split, the units and the other owner questions
  are as accepted in rounds 4–6.

## Non-blocking notes

1. **G182 candidate (ii)** (§4.6, the G182 row). The marking condition ("stores through a call into a keeper with kept storage")
   misses a Loan that enters a keeper through a call's result row. This is round 6's second point; `initThenRead` shows the route,
   and constructors or factories of today's holders use it too. Align the trigger with (b)(1): a call whose store or result row
   puts a non-empty value into a holder with kept storage. G182's own design unit would catch this anyway.
2. **§11, round-5 entry.** The text "(§4.3 rule 4)" points at a withdrawn rule; add "(withdrawn in round 6)".
3. **§1.3 "Why".** "Only through the declaration would such a Type become a holder" is false for the over-catch (`MarkBox`), as
   the next bullet says. Qualify it with "for a Type with no other Loan-capable part".
4. **INTEGRATED.** If this file is committed with the plan, cite `review-7.md` beside rounds 1–6.

## Verdict

**Ready for commit with the owner's questions (Q1–Q5) open.** The four notes are optional edits.
