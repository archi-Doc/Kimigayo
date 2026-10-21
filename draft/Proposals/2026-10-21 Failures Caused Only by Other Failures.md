# Failures Caused Only by Other Failures

Status: open; round 3 for review. The owner chose the whitelist W after two rounds were not accepted (2026-10-22, verbatim
「P2は、W：個別規則に決定します」). Not integrated, not implemented. Date: 2026-10-22. Written by the R4 planning session on the
owner's instruction (2026-10-21): 「候補1（クロージャを受け手つき呼び出しとして定義）と候補3（派生失敗の定義）を、2c-D・2g-D の前に判断できるよう draft/Proposals に提案書として書いてください。」
History: round 1 ("Escapes and Derived Failures", not kept) and round 2 (a general definition in §23.1.2, `dev` `4b6613ca`) were
not accepted (`../Sketches/R4 Proposals 2026-10-21/review-1.md`, `review-2.md`); W is round 2's review's suggested formulation,
written out here. The owner's answers to round 1 are not applied. The first unit affected is 2a-2, G:A5-F4's merge
([2a0-D](../Sketches/A5%20Step%202/2a0-D-package.md) §7). "(H)" marks a claim no run has checked.

## 1. Problem

[§23.1.2](../../docs/spec/23-compiler-services.md#2312-reporting) requires each independent failed requirement to be reported and
leaves implementation-defined whether a failure caused only by another reported failure is reported; it defines neither term.
For one escape the MIR sees two records: A5's Origin stratum reports the Refuted relation, its Loan stratum a conflict on the
escaping Borrow's Loan. 2a0-D §7 decides which stays by two family rules (rule 1: a storage-end conflict found only through `_0`;
rule 2, GuardReverse: a guard-sourced Refuted relation with a `ResultFit` last edge, dropped where the candidate has an err(ended)),
which match by atom, candidate and view. DECISIONS R4-M94 records the results: `cf-g-reassign` loses an independent report and
`rv-g-param` reports one failure twice.

[§15.6.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations) and
[§15.8.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1582-closure-dependencies) already fix the code of the two common
cases (returning a borrow of a local is the Refuted relation at the returned value, not a Loan conflict at the storage's
destruction); [§14.8.3](../../docs/spec/14-control-flow.md#1483-guards) has no such sentence. Two rounds showed that a general
definition of "caused only by" either makes required failures optional or gives cycles, because a fit's Loan flow does not depend
on whether its relations hold ([§15.6.1](../../docs/spec/15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness):
a failed relation adds no constraint and causes no Loan conflict of its own).

## 2. Proposal (W)

One sentence in §15.6.5, after the sentence on locals:

> A Loan conflict on the Loan of a Borrow is caused only by the Refuted relations that carry that Borrow's value to a fixed Origin
> ([§23.1.2](../../docs/spec/23-compiler-services.md#2312-reporting)) when every holder that keeps the Loan and is used after the
> conflicting access is the result or storage the body does not own, and every flow by which such a holder received the Loan passes
> a fit whose relation is Refuted. The result and storage the body does not own count as used after every access in the body.

§14.8.3 and §15.8.2 cite it. Its properties:

- **Relations are never caused only by a conflict.** The sentence derives only Loan conflicts, and only from Refuted relations,
  so no cycle can arise; Unproven (Proof) relations derive nothing.
- **Only escapes.** A conflict that a local holder keeps alive (a later use of a local, a `var` that is read again) is
  independent, whatever relations failed.
- **Other requirements unchanged.** §7.2.3's default Borrow requirement (`DefaultArgumentBorrow_Kd`) is not a Loan conflict; its pair
  with the Origin relation of the same kept Borrow stays a grouping choice (§23.1.2 leaves grouping free), and A5-F9 keeps §7.2.3's
  code (PD11, unit 2g-5).
- **§23.1.2 unchanged.** The general term stays undefined; this is one case of it.

## 3. Readings (the prototype's expected records)

S = Refuted relation, P = Unproven relation, C = Loan conflict; "derived" = caused only by the S records named.

| Case (2a0-D §7 and the reviews) | Records | Under W |
| --- | --- | --- |
| j-bad: `return local@ref` | S at the return; C at the local's storage end | C derived: its only later carrier is the result, received through the Refuted return fit |
| `var r: ref/i32 during a` / `r = x@ref` / `return r` | S at the assignment; C at `x`'s storage end | C derived: the result received the Loan through `r`, and that flow passes the Refuted assignment |
| m-write: `let r = x@ref` / `x = 1` / `return r` | S at the return; C at the write | Both independent: `r`, a local, is used after the write |
| m-write4: `slot@follow = x@ref` / `x = 2` (`slot` a parameter's referent) | S at the store; C at the write | C derived: the only carrier is storage the body does not own, received through the Refuted store. A named change: 2a0-D §5.1 and its STATUS record expect the write conflict reported |
| Two flows: `p@follow = r` / `return r`, `r` a body-local borrow | Two S; C at the storage end | Both S independent; C derived from both (suppressed only when both are published, DIAGNOSTICS §4.3) |
| A store in a loop: `while …` / `p@follow = x@uniq` | S per store; C at the next iteration's Borrow | C derived: the Loan meeting the Borrow is held by `p`'s referent, received through the Refuted store |
| `rv-g-param`: a guard candidate stored through a parameter's referent | S at the store; C (err(ended)); P | C derived; S and P independent. Today: S and C both |
| `rv-g-annot-esc`: a candidate stored into `var t: ref/i32 during a`, `t` used after cleanup | S at the store; C (err(ended)) | Both independent: `t` is a local used after cleanup. As 2a-2 expects |
| g-two: a candidate stored into an unannotated `saved` (the store's relation holds), `return saved` | S at the return; C at cleanup; P | All independent: `saved` is a local |
| `cf-g-reassign`: the candidate escapes through holder H; `saved = fb2` / `return saved` is refuted | C witnessed by H's use; S at the return | Both independent. Today the S is lost (rule 2) |
| m-store, `cf/p/jregion2`: a storage-end conflict another live holder also takes | S; C | C independent where the other holder is a local or received the Loan through no Refuted fit (H for jregion2's records "S, S, C") |
| m-twice/same-root: two Borrows of one root, the second returned | S; C at the storage end | C derived (the per-Borrow Loan's only later carrier is the result) |
| PreparedInput (PD11) | `DefaultArgumentBorrow_Kd` at the Borrow; S at the template's return | Not a Loan conflict: grouping, 2g-5's rule kept |
| A conflict without a Refuted twin | C | Independent |

`rv/guard-annot`, m-36f, m-pair, m-scope, m-scope-use, `cf/p/m-write3` and the block, `if` and `match` result forms of 2a-2's list
are read by the prototype (§6); none has a carrier that is both an escape destination and reached only through Refuted fits beyond
the rows above (H).

## 4. Implementation direction (H)

- **A5 (2a-2, full process for its merge part).** D13 runs `MirOrigins.Judge` for all classes before `MirBorrows` per view, so the
  Refuted fits of each view are known when MirBorrows runs. MirBorrows carries, per (Loan, holder), one bit "received only through
  Refuted fits" through its transfer function: a flow through a Refuted fit sets it in the destination; a join with a copy that does
  not have it clears it. A conflict whose live later carriers are all `_0` or E with the bit set records the Refuted relations of
  those fits as its causes; nothing is dropped on arrival.
- **Facts to design (not available today).** The fit a MIR transfer realizes and its relation row (the `Relation` fact names a
  row; the link from the MIR transfer to it is missing); the per-(Loan, holder) bit above, with its memory term under `MirDataflow`'s
  limit (R4-M15a) and a V16 shape.
- **Publication (H3, R5).** The causes are MIR-to-MIR (a Loan problem caused by an Origin problem), an extension of AR-7 that R5's
  planning session reviews. DIAGNOSTICS §4.3's invariant ties `DerivedFrom` to `PrerequisiteUnavailable_Kd`; H3 either publishes a
  derived MIR problem with its own code and a cause fact (2g-1's pattern, extended to several causes) or follows the invariant.
- **What goes.** 2a0-D §7's rules 1 and 2 and the merge context (`RefutedAtResult`, `Ended`). Stays: 2g-5's PreparedInput rule,
  R4-M10's Opaque cause (2g-1), the grouping of inline copies (R4-M97).

## 5. Departures from approved texts (named)

- R4-M94: 2a0-D §7's rules 1 and 2, OQ-4 (b) (GuardReverse) and OQ-3 items (2) and (9); PD11's first half.
- 2a0-D §5.1 and its planned STATUS record: m-write4's write conflict becomes derived.
- DIAGNOSTICS §4.3's "every other failure is direct" gains this one derived kind; R4-M98's reading that ordinary conflicts in a
  template are independent is unchanged for conflicts kept alive by a local holder (H: to confirm per template case in §6).

## 6. Verification before adoption

1. **Prototype round** over 2a-2's list and §3's rows (2x-P4's merge model, `S2/A5OriginsPrototype.cs`, extended with the bit of
   §4). Success: §3's records exactly, no acceptance change. A differing case returns to the owner with the case.
2. **SPEC session** for §15.6.5's sentence and the two citations after the prototype, before 2a-2's design review.
3. **R5** reviews §4's publication part.

## 7. Principles

1. **One concept, one canonical form.** One sentence replaces two family rules; §15.6.5's and §15.8.2's code sentences become its
   instances.
2. **Local reasoning.** Whether a conflict is derived follows from the holders that keep its Loan and the fits they received it
   through, all facts of the body.
3. **Explicit semantics.** The derived case is stated, and independence everywhere else is the default.
4. **Compiler Server Protocol.** An escape reports its relation; its consequent conflict keeps the relation as its cause.

## 8. SPEC text (planned mapping)

| Section | Change |
| --- | --- |
| §15.6.5 | §2's sentence after the sentence on locals |
| §14.8.3 | One citation: a value kept past guard cleanup is judged by §15.6.5's sentence |
| §15.8.2 | Its sentence on a returned body-local borrow cites §15.6.5's |

## 9. Unverified

- §3's readings (from the case descriptions, not run); jregion2's records.
- The bit's memory and work terms (V16) and its per-view masks.
- H3's publication choice.
