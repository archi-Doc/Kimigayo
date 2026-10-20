# G160 plan: independent review, round 6

Reviewer: the reviewer of rounds 1–5; author of none of the designs. Scope:

- the revised plan (833 lines) in worktree `bin/worktrees/g160-plan` (base `59b78023`, uncommitted);
- PLAN's G182 row.

This round's change replaces round 5's declaration-located `Unsupported_Kd` with a temporary `MirWrites.Held` bit and a W rule 4
that holds the view of a non-empty store into a `Held` target, by R4-M73's body hold.

New probe, on the dev `a1677549` binary through the same harness: `scratchpad/g160/review6/w1.kimi` (+ `.out`).

| Probe | Shape | Today |
| --- | --- | --- |
| w1 `storeThenGet` | `OwnRefs` (undeclared); `storeO(o@uniq, x@ref)`; `x = 5; inspect(o.get())` | error `x = 5` (raw clause) |
| w1 `initThenGet` | `var o = OwnRefs.init(seed: x@ref); x = 5; inspect(o.get())` | accepted |
| w1 `initThenRead` | `var o = OwnRefs.init(seed: x@ref); unsafe => q = o.data[0]; x = 5; inspect(q)` | accepted |

Today's acceptances in w1 are the author's breach under SPEC 5.2.1: undeclared, `OwnRefs` keeps its slot through neither a Type
argument nor a `Loan<T>` Field. With `via self`, minimal-core item 2 makes the declaration keep them, so the same programs become
valid unsafe code that A5 must check.

## Blocking finding

### R6-B1. A P13-style hold cannot cover this class: it reclassifies problems A5 finds, and this class's conflicts are never found

- **Location.** §1.3 (the paragraph after item 5: "Every Loan problem the view owns is then a located `Unsupported_Kd`: never an
  unsound acceptance"), §4.2 (`Held`), §4.3 rule 4, §4.6, §1.2 rows `OwnRefs`, `ownRefsRead` and `projOwner`, and the G182 row.
- **Problem.** R4-M73's hold "never accepts: it reclassifies problems already found". Its completeness argument ("a conflict that
  depends on such a store is found in the view whose Call makes it") holds for P13, because P13's stores reach over-approximated
  targets, so the dependent conflicts are found and then downgraded. The widening class is the opposite case:
  - A declared `via self` keeper with a `Concrete` pointee holds the call-stored Loan in its own holder.
  - The danger is an in-body raw read (Raw, {Φ_E}) that misses that Loan, so no conflict is found at all.
  - The view is held, but it owns no problem, so nothing is reported and the program is accepted.
  - `ownRefsRead` with `via self` runs `storeO(o@uniq, x@ref)` (held), `q = o.data[0]`, `o` replaced, then `x = 5; inspect(q)`.
    It has no Loan problem and is therefore accepted. Today it is an error through the raw clause.
  - The §1.2 cells saying "held (`Unsupported_Kd`)" for `ownRefsRead` and `projOwner` are wrong.

  Rule 4 also covers only W's store rows. A Loan enters the keeper by a call's result row too: a constructor, as in w1's
  `initThenRead` with `OwnRefs.init(seed: x@ref)` declared `via self`, or a factory function. That route never reaches `Writes`.
- **Evidence.**
  - R4-M73's text ("It never accepts: it reclassifies problems already found").
  - `Calls.cs:44-66`: result rows join `result` and bypass `Writes`.
  - w1: the shapes bind; `initThenRead` is accepted today as the author's breach and becomes valid code under `via self`.
- **Correction.** The interim must report unconditionally, not reclassify. Three complete forms, each owned by A5 with `Keeping`'s
  fact and each with the removal condition G182:
  - (1) A located `Unsupported_Kd` at every Call whose row puts a non-empty value into a `Held` holder. This covers a store row's W
    target and a result row's destination root, and follows R4-M51's located-at-the-call pattern. Moves keep the Loans in holders
    the same view flagged, and a parameter's Loans are fixed, so this is complete within a body.
  - (2) A located `Unsupported_Kd` at each body local whose case Type has `Held`. This is coarser and trivially complete.
  - (3) Round 5's declaration-located form, reported where `Keeping` decides it.

  The reach is not an issue: `Held` lives in `writable[...]`, so a keeper reached by a guard step or in rule 3's second pass is
  seen (`writingKind` keeps the whole flags). A keeper inline in another holder propagates `Held` through the guard walk's Field
  walk.
- **Process.** This is the third rejected round on the widening-class family (R4-B1, R5-B1, R6-B1). AGENTS: "after two rounds on
  one family of findings, the owner chooses between a representation, a whitelist and a hold". Do not iterate again; put the
  choice to the owner with Q5's alternatives:
  - (a) record the class in G182's limit;
  - (b) a complete interim `Unsupported_Kd`, one of (1)–(3) above;
  - (c) the reader obligation.

  State in the plan that the P13-style hold was found insufficient. The rest of the plan need not wait. G160-0 can include this
  choice, and G160-3 implements whichever form the owner picks.

## Non-blocking findings

- **N1.** After R6-B1, drop "by R4-M73's body hold" and "never an unsound acceptance" from §1.3, §4.6 and the G182 row, unless the
  chosen form makes the statement true.
- **N2.** `Held` widens `MirWrites` again. If form (1) or (2) is chosen, `Held` can stay a per-Type fact. If form (3) is chosen,
  it is unnecessary.

## Checks with no finding

- R5-B1's boundary is now `Concrete` on the pointee, with projections counted and one owner (`Keeping`). `projOwner` is in the
  class, and the `MarkBox` over-catch is stated.
- N2 and N3 of round 5 are adopted: the `KeptBox<uniq/(ref/i32)>` guard pin, and the P13 → Language change listed in §7.
- The call side (`Keeping`, the Unowned walk, `via self` in the guard walk, W rules 2 and 3) and the G182 split are unchanged and
  sound.

## Verdict

**Reject (blocking finding R6-B1).** The interim mechanism chosen this round cannot reach the class it targets. Escalate the
widening-class interim to the owner (Q5) per AGENTS instead of a further design round. Everything else in the plan is ready.
