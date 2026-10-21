# Failures Caused Only by Other Failures

Status: open; **not accepted in two review rounds** (`../Sketches/R4 Proposals 2026-10-21/review-1.md`, round 1 "Escapes and
Derived Failures"; `review-2.md`, round 2, the text from §1 on). Under AGENTS (Independent review), after two rounds on one family
of findings the owner chooses between a representation, a whitelist and a hold (§0). Not integrated, not implemented. Date:
2026-10-22. Written by the R4 planning session on the owner's instruction (2026-10-21):
「候補1（クロージャを受け手つき呼び出しとして定義）と候補3（派生失敗の定義）を、2c-D・2g-D の前に判断できるよう draft/Proposals に提案書として書いてください。」
The owner answered round 1's questions (OQ-D1 (a), OQ-D2 (a), OQ-D3 (a), 2026-10-21) on round 1's text, which was not accepted;
those answers are not applied. The first unit affected is 2a-2, G:A5-F4's merge ([2a0-D](../Sketches/A5%20Step%202/2a0-D-package.md)
§7). "(H)" marks a claim no run has checked.

## 0. Owner choice after two rounds

Both rounds tried a general definition of "caused only by" in §23.1.2. Round 2's review found it derives every fact of a rejected
operation rather than the fact it is rejected for (a fit's Loan flow does not depend on whether its relations hold, §15.6), which
makes required failures optional (a later write conflict after a refuted store; §14.8.3's own guard example with an annotated
destination) and gives cycles (a store in a loop; the PreparedInput pair). The three choices:

| Choice | Content | Assessment |
| --- | --- | --- |
| **W, whitelist (recommended)** | One concrete sentence in §15.6.5, cited from §14.8.3 and §15.8.2 (round 2's review, unrun): "A Loan conflict on the Loan of a Borrow refuted to a fixed Origin is caused only by those relations when every carrier used after the access is the result or storage the body does not own and received the Loan only through refuted fits." The general §23.1.2 term stays undefined | Relations are never caused only by a conflict, so no cycle. Replaces 2a0-D §7's rules 1 and 2 by one rule: `rv-g-param`'s double report and `cf-g-reassign`'s lost report both close (H); `rv-g-annot-esc` and g-two keep two records, as 2a-2 expects; m-write4's write conflict becomes caused only by the relation, a named change from 2a0-D §5.1's expectation. The PreparedInput pair stays grouping, and 2g-5's PreparedInput rule stays. Implementation needs Loan provenance per live holder (the carriers used after the access and the fits each received the Loan through), a MirBorrows fact designed in 2a-2's full process; departs from R4-M94 (rules 1 and 2, OQ-4 (b), OQ-3 (2) and (9)) |
| R, representation | No SPEC change: 2a-2 publishes the same Loan-provenance fact and decides rules 1 and 2 by it (2a0-D's OQ-4 (c)); the rule is A6's implementation contract in DIAGNOSTICS | The same behaviour as W; SPEC keeps no statement of which escape record is primary |
| H, hold | 2a0-D §7 as approved (R4-M94): rules 1 and 2 in 2a-2; `cf-g-reassign`'s lost report and `rv-g-param`'s double report stay recorded limits (X-2/OQ9) | No new design; one independent failure stays unreported at R4's exit |

Under W the round-2 text below is superseded; the sentence and its test list are written and reviewed after the choice.

## 1. Problem

[§23.1.2](../../docs/spec/23-compiler-services.md#2312-reporting) requires each **independent** failed requirement to be
reported, and leaves implementation-defined "whether a failure caused only by another reported failure is also reported". It
defines neither "independent" nor "caused only by". Three sentences fix the code of particular cases instead:

- [§15.6.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations): "a use beyond a local Place's
  lifetime is a Loan conflict at its destruction or Move, never an Origin relation failure; returning a borrow of a local or
  temporary is the Refuted relation at the returned value";
- [§15.8.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1582-closure-dependencies): "returning a body-local borrow is an
  error at the returned value, not a Loan conflict at the storage's destruction";
- a guard escape ([§14.8.3](../../docs/spec/14-control-flow.md#1483-guards)) has none; 2a0-D R6 reads "either code is admitted".

The MIR analyses meet one escape in two strata: A5's Origin stratum reports the Refuted relation, its Loan stratum the conflict
at the end of the Borrow's storage or at guard cleanup. With no definition, each family decides with its own rule:

| Rule | Where | What it decides |
| --- | --- | --- |
| Rule 1 (PD11's first half) | 2a0-D §7, unit 2a-2 | Drops a storage-end conflict whose Loan is found only through `_0` when its Anchor atom is the finite source of a chain refuted at a `ResultFit` row, per view |
| Rule 2 (GuardReverse; R4-M94, OQ-4 (b)) | 2a0-D §7, unit 2a-2 | Drops a guard-sourced Refuted relation whose last edge is a `ResultFit` row, in the views where the candidate has an err(ended) |
| PreparedInput rule (PD11) | unit 2g-5 | Drops the Origin problem of a PreparedInput-rooted Borrow that A5-F9 reports as `DefaultArgumentBorrow_Kd` |
| Opaque rule (R4-M10) | unit 2g-1 | A problem rooted at an Opaque site is derived; 2g-1 keeps one cause fact |

Rules 1 and 2 match by atom, candidate and view, which is coarser than the cause. R4-M94 records the result: `cf-g-reassign`
loses an independent report (rule 2 drops a Refuted relation at a return whose value no longer carries the candidate's Loan, while
the candidate escapes through another holder), and `rv-g-param` reports one failure with two codes. Both are 2a-2's recorded limits
(DIAGNOSTICS and STATUS receive them with 2a-2, 2a0-D §9).

## 2. Proposal

Define the existing term in §23.1.2, and nothing more:

> A failure is **caused only by** other failures when the fact that makes it fail comes only from operations those failures
> reject. A failure caused only by no other failure is **independent**.

What the definition reads, with the terms the analyses already use:

- **The operation a failure rejects.** An expression or declaration the failure rejects; for an Origin relation, the fit that
  formed it (the transfer of a value into its destination: a store, a return into the result, an argument into a parameter,
  [§15.6.1](../../docs/spec/15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)); for §7.2.3's
  default Borrow requirement, the kept Borrow.
- **The fact that makes it fail.** For a Loan conflict, the Loan's presence where the access meets it; for an Origin relation, its
  chain; for any other requirement, the facts its rule reads.
- **Comes only from.** Every way the fact arises passes through such an operation. A Loan that reaches the conflicting holder by
  two flows, one of them through no rejected operation, is not caused only by the rejection.

Grouping stays implementation-defined: failures that reject the same operation may share a record, as §23.1.2 already allows. The
three code sentences of §1 become consequences, and a guard escape gets the same reading:

| Program | Records | Reading |
| --- | --- | --- |
| `return local@ref` (2a-2's j-bad) | Refuted relation at the return; storage-end conflict through `_0` | The conflict's Loan reaches `_0` only through the rejected return fit: caused only by the relation. The relation is independent (§15.6.5's second clause, §15.8.2's sentence) |
| `var r = …`, a nested block borrows a local into `r` and ends, then `use(r)` | Storage-end conflict only (no chain reaches a fixed Origin) | Independent (§15.6.5's first clause) |
| `let r = x@ref` / `x = 1` / `return r` (m-write) | Refuted relation at the return; the write conflict | Both independent: the Loan's liveness at the write comes from reading `r` at the return, and the read is no rejected operation; only the return's fit is |
| `p@follow = r` / `return r`, `r` a body-local borrow | Two Refuted relations; one storage-end conflict | Both relations independent; the conflict is caused only by both (DIAGNOSTICS §4.3 suppresses it only when both are published) |
| A guard candidate stored through a parameter's referent (`rv-g-param`) | Refuted relation at the call; err(ended) | The Loan reaches storage the body does not own only through the rejected store: the err(ended) is caused only by the relation |
| A candidate stored into `var t: ref/i32 during a` in the guard, `t` used after cleanup (`rv-g-annot-esc`) | Refuted relation at the store; err(ended) | The same: the err(ended) is caused only by the relation |
| A candidate stored into an unannotated `saved` (the store's relation holds), `return saved` after cleanup (g-two) | Refuted relation at the return; err(ended) | Both independent: the Loan entered `saved` through an accepted store |
| A candidate escapes through holder H; separately `saved = fb2` / `return saved` is refuted (`cf-g-reassign`) | err(ended) witnessed by H's use; Refuted relation at the return | Both independent (today's lost report is kept) |
| A storage-end conflict another live holder also takes (m-store, `cf/p/jregion2`) | Refuted relation at the return; storage-end conflict | Independent: one flow passes no rejected operation |
| A PreparedInput Borrow kept by its template's result (PD11) | `DefaultArgumentBorrow_Kd` at the Borrow; Refuted relation at the template's return | Both reject the template's keeping of the Borrow: one operation, so they may share one record (grouping); A5-F9 keeps §7.2.3's code |
| `let text: string = 1` / `return text` in a function returning `i32` (§23.1.2's example) | Two Type mismatches | Independent: `text`'s Type comes from its annotation, not from the rejected initializer |

So: a relation is never caused only by the conflict that reports the same escape, and the conflict is caused only by the relation
exactly when every flow that carries the Loan passes a rejected fit. Rule 2's choice is reversed where the guard escape goes
through a rejected store (the relation is reported) and both records stay where it does not.

## 3. Implementation direction (H)

**Causes at formation (A5).** D13 already runs `MirOrigins.Judge` for all classes before `MirBorrows` runs per view, so when
`MirBorrows` records a storage-end conflict or an err(ended), the Refuted relations of that view, and the fits they reject, are
known. The conflict records as causes the relation problems whose rejected fits every witness flow passes, or none. Nothing is
dropped on arrival and no pairing runs after both strata have recorded. This is the general form of 2a0-D's OQ-4 (c) (a
Loan-stratum flow fact), which the owner did not choose in R4-M94; it is preferred now because it follows from a SPEC definition
instead of a third family rule, and it keeps `cf-g-reassign`'s report.

**Facts to publish (part of 2a-2's full-process design, not available today).**

- The witness set of a bound-end Loan problem: every flow by which its Loan reaches the holder or use, not only the first found
  (R4-M96 records one witness).
- The fit each Refuted relation rejects, by site and destination; `Result` marks `_0` but names no return.
- The relation's Borrow identity: the Origin problem's `Anchor` is the first Ref in body order with the atom (2a0-D §6.3), so two
  Borrows of one root (m-twice/same-root) need the per-atom reading rule 1 uses today.

**Publication (H3, R5).** A problem caused only by others carries its causes through DIAGNOSTICS §4.3's prerequisite model
(`DerivedFrom`; suppressed only when every cause reaches a published direct Error). §4.3's invariant ties `DerivedFrom` to the code
`PrerequisiteUnavailable_Kd`; publishing a MIR problem's causes that way changes the code of a derived MIR problem published with
an unresolved cause. 2g-1's alternative keeps the MIR code with one cause fact. Which of the two H3 uses is R5's acceptance question
(AR-7).

**What goes.** Rules 1 and 2, the merge context (`RefutedAtResult`, `Ended`) and 2g-5's per-family merges are replaced by the
causes. R4-M10's Opaque rule is 2g-1's cause, unchanged. The PreparedInput pair is grouping by A5-F9, unchanged.

## 4. Options

| Option | Content | Assessment |
| --- | --- | --- |
| **A (recommended)** | §2's definition in SPEC; §3's causes in 2a-2 and A6 | One definition; independence becomes testable; `cf-g-reassign` keeps its report and `rv-g-param` gets one primary record (H); rules 1 and 2 and the merge context go. Departs from R4-M94 (2a0-D §7's rules 1 and 2, OQ-4 (b), OQ-3 items (2) and (9)) and PD11's first half, so 2a-2's merge part takes the full process |
| B | §3's causes as an implementation rule only (2a0-D's OQ-4 (c) inside the A6 contract), SPEC unchanged | The same implementation; SPEC keeps "caused only by" undefined, so another implementation may read independence differently |
| C | No change: 2a0-D §7's rules in 2a-2, per-family rules in 2g-5 | The lost report stays a recorded limit at R4's exit (X-2) and `rv-g-param` keeps two codes |

## 5. SPEC text (planned mapping)

| Section | Change |
| --- | --- |
| §23.1.2 | After "whether a failure caused only by another reported failure is also reported, are implementation-defined", add §2's definition (two sentences) |
| §15.6.5 | Keep the sentence on locals; mark it as an example of §23.1.2 |
| §15.8.2 | Keep "returning a body-local borrow is an error at the returned value, not a Loan conflict at the storage's destruction", the same example |
| §14.8.3 | One sentence: a value kept past guard cleanup through a rejected fit is caused only by that relation (§23.1.2) |
| Appendix E | "Caused only by", "independent failure" |

No example changes its verdict. A comment naming a guard escape's code changes only where the escape passes a rejected store (H).

## 6. Verification before adoption

1. **Prototype round.** Re-run 2x-P4's merge model (`S2/A5OriginsPrototype.cs`, 2a0-D's `rv/protoA`) with causes in place of rules
   1 and 2, over 2a-2's list: j-bad, j-region, g-return, g-two, `rv/guard-annot`, `rv-g-annot-esc`, `rv-g-param` (its S, C and P),
   `cf-g-reassign`, m-36f, m-write and its `var r` form, `cf/p/m-write3`, m-write4, m-pair, m-scope, m-scope-use, m-store,
   m-twice/same-root, block/`if`/`match` results, a conflict without a Refuted twin, and §2's two-relation case. Success: no
   acceptance change, no lost independent failure, and §2's readings. A failing case returns to the owner.
2. **Facts.** §3's three facts are designed in 2a-2's full process; none is a fallback.
3. **R5.** H3's publication choice (§3) is reviewed by R5's planning session.

## 7. Principles

1. **One concept, one canonical form.** One definition replaces four family rules and reads the three code sentences.
2. **Local reasoning.** Whether a record is caused only by another follows from where its fact comes from, which the failing
   operation's own facts show.
3. **Explicit semantics.** "Independent" and "caused only by" get a definition that a test can check.
4. **Compiler Server Protocol.** Each escape has one primary record; a record caused only by others keeps its causes.

## 8. Owner questions (round 2; superseded by §0)

| ID | Question | Options | Recommendation |
| --- | --- | --- | --- |
| OQ-D1 | Adopt the definition? | (a) A: SPEC §23.1.2 after the prototype round, the causes in 2a-2 (full process for its merge part) and A6; (b) B: an implementation rule only; (c) C | (a) |
| OQ-D2 | When? | (a) before 2a-2, which then implements the causes; (b) 2a-2 keeps 2a0-D §7, and 2g-5 replaces it at A6 | (a) |

Round 1's OQ-D2 (which record reports one escape) is not asked: under the definition the relation is reported and the conflict is
caused only by it where every flow passes the rejected fit, which round 1's OQ-D2 (a) chose; where some flow does not, both are
independent.

## 9. Unverified

- The readings of §2 are derived from 2a0-D's case descriptions, not run (§6).
- That every flow of a bound-end Loan can be enumerated within V16's bounds (the witness set's cost).
- Per-view masks: a conflict caused only by a relation in some views and independent in others.
- H3's publication choice and its code consequence (§3).
