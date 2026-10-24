# Failures Caused Only by Other Failures

Status: 取り込み済み (closed 2026-10-24): §2's sentence is in SPEC §15.6.5 with the owner's read-through clause, cited from §14.8.3 and
§15.8.2 (DECISIONS R4-M104, R4-M122); not implemented (2a-2). The owner chose the whitelist W after two rounds were not accepted (verbatim 「P2は、W：個別規則に決定します」);
round 3 passed its review with fixes, a re-check and a confirmation, all applied. Date: 2026-10-22. Written by the R4
planning session on the owner's instruction (2026-10-21):
「候補1（クロージャを受け手つき呼び出しとして定義）と候補3（派生失敗の定義）を、2c-D・2g-D の前に判断できるよう draft/Proposals に提案書として書いてください。」
History: round 1 ("Escapes and Derived Failures", not kept) and round 2 (a general definition in §23.1.2, `dev` `4b6613ca`) were
not accepted (`../Sketches/R4 Proposals 2026-10-21/review-1.md`, `review-2.md`); round 3 (`dev` `9031f7bc`) was accepted with fixes
(`../Sketches/R4 Proposals 2026-10-21/review-p2r3.md` with its re-check), whose corrected sentence §2 uses. The owner's answers to round 1 are not applied. The first unit affected is
2a-2, G:A5-F4's merge ([2a0-D](../Sketches/A5%20Step%202/2a0-D-package.md) §7). "(H)" marks a claim no run has checked.

## 1. Problem

[§23.1.2](../../docs/spec/23-compiler-services.md#2312-reporting) requires each independent failed requirement to be reported and
leaves implementation-defined whether a failure caused only by another reported failure is reported; it defines neither term.
For one escape the MIR sees two records: A5's Origin stratum reports the Refuted relation, its Loan stratum a conflict on the
escaping Borrow's Loan. 2a0-D §7 decides which stays by two family rules (rule 1: a storage-end conflict found only through `_0`;
rule 2, GuardReverse: a guard-sourced Refuted relation with a `ResultFit` last edge, dropped where the candidate has an err(ended)),
which match by atom, candidate and view. DECISIONS R4-M94 records the results: `cf-g-reassign` loses an independent report and
`rv-g-param` reports one failure twice.

[§15.6.5](../../docs/spec/15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations) and
[§15.8.2](../../docs/spec/15-ownership-and-lifetime-analysis.md#1582-closure-dependencies) already fix the code of the common case
(returning a borrow of a local is the Refuted relation at the returned value, not a Loan conflict at the storage's destruction);
[§14.8.3](../../docs/spec/14-control-flow.md#1483-guards) has no such sentence. Two rounds showed that a general definition of
"caused only by" either makes required failures optional or gives cycles, because a fit's Loan flow does not depend on whether its
relations hold ([§15.6.1](../../docs/spec/15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness): a
failed relation adds no constraint and causes no Loan conflict of its own).

## 2. Proposal (W)

One sentence in §15.6.5, after the sentence on locals:

> A Loan conflict on the Loan of a Borrow whose finite Origin Refuted chains carry to fixed Origins is caused only by the Refuted
> relations of those chains ([§23.1.2](../../docs/spec/23-compiler-services.md#2312-reporting)) when every value that carries the Loan
> and may be used after the access is the result or storage the body does not own and received the Loan only as the borrow or a
> Copy or Reborrow of it ([§15.6](../../docs/spec/15-ownership-and-lifetime-analysis.md#156-borrow-checking)), stored into a part
> with a fixed Origin; a call's result, or storage the call writes, that may retain such a value as an argument counts as a Copy of
> it ([§15.6.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1564-calls)).

§14.8.3 and §15.8.2 cite it. Whether the Loan arrived as the escape is decided by what carries it, in §15.6's own terms (the borrow
itself, its Copies and Reborrows, as opposed to an aggregate holding them or a value read from a holder, which carries every Loan
of the holder; §15.6's own "derives from" includes such reads, so the sentence does not use it; a whole-value Move of the borrow
is the borrow itself), not by Origins: fixed Origins are shared names and regions are flow-insensitive. The call clause is safe
because a whole-value borrow argument's own Loan is retained only through its outer Origin, so the retaining part lies on the
Borrow's chain; a Tuple or aggregate argument holding a Copy is not itself a Copy. The sentence is per part and per Borrow, so a
fit that carries two values is read per value, and a Borrow with no finite Origin (one formed by reborrowing through a parameter)
derives nothing.

- **Relations are never caused only by a conflict.** Only Loan conflicts are derived, and only from Refuted relations: no cycle;
  Unproven (Proof) relations derive nothing.
- **Only escapes.** A conflict that a value carried by a local and used later keeps alive is independent.
- **Other requirements unchanged.** §7.2.3's default Borrow requirement (`DefaultArgumentBorrow_Kd`) is not a Loan conflict; its pair
  with the Origin relation of the same kept Borrow stays a grouping choice (§23.1.2 leaves grouping free), and A5-F9 keeps §7.2.3's
  code (PD11, unit 2g-5).
- **§23.1.2 unchanged.** The general term stays undefined; this is one case of it, and other derived readings (2a0-D §6.3's R8
  admission of a relation derived from an earlier failed edge) stay implementation choices.

## 3. Readings (the prototype's expected records)

S = Refuted relation, P = Unproven relation, C = Loan conflict; "derived" = caused only by the S records named.

| Case (2a0-D §7, the reviews) | Records | Under W |
| --- | --- | --- |
| j-bad: `return local@ref` | S at the return; C at the local's storage end | C derived |
| `var r: ref/i32 during a` / `r = x@ref` / `return r`, the function returning `ref/i32 during a` | S at the assignment; C at `x`'s storage end | C derived: `_0` receives `r`'s value, a Copy of the borrow, into its fixed part |
| m-write: `let r = x@ref` / `x = 1` / `return r` | S at the return; C at the write | Both independent: `r`, a local, is used after the write |
| m-write4: `slot@follow = x@ref` / `x = 2` (`slot` a parameter's referent) | S at the store; C at the write | C derived. A named change (§5) |
| m-store: `target@follow = local@ref` | S at the store; C at the storage end | C derived (the only later carrier is `target.*`). A named change (§5) |
| Two flows: `p@follow = r` / `return r`, `r` a body-local borrow; `cf/p/jregion2` (`var r = local@ref` / `slot@follow = r` / `return r`) | Two S; C at the storage end | Both S independent; C derived from both (suppressed only when both are published, DIAGNOSTICS §4.3). A named change for jregion2 (§5) |
| A store in a loop: `while …` / `p@follow = x@uniq` | S per store; C at the next iteration's Borrow | C derived |
| m-twice / same-root: two Borrows of one root, `pair.0` returned | S; C at the storage end per Borrow | The other Borrow's C independent (its Loan reaches `_0` through a read from `pair`); the returned Borrow's C is derived by the sentence, and an implementation without per-part facts may also report it, which is SPEC-legal |
| m-pair (two roots, one returned) | S; C per root | The same reading as m-twice |
| `return id(local@ref)` (`id` returns its argument) | S at the return; C at the local's storage end | C derived: the call's result may retain the borrow as its argument, so it counts as a Copy |
| `fK`: `func k(x: (ref/i32 during o, ref/i32 during q)) -> ref/i32 during q`; `let r = x@ref` / `p@follow = r` / `return k((r, p0))` | S at the store; C at `x`'s storage end | C independent: the retained argument is a Tuple holding a Copy, not a Copy; deleting the store leaves the conflict (§15.6.4's per-argument retention) |
| `rv-g-param`: a guard candidate stored through a parameter's referent | S at the store; C (err(ended)); P | C derived (through `keep`'s write, the call clause); S and P independent. Today the S and C both are reported |
| `rv-g-annot-esc`: a candidate stored into `var t: ref/i32 during a`, `t` used after cleanup | S at the store; C (err(ended)) | Both independent: `t`, a local, is used after cleanup |
| g-two: a candidate stored into an unannotated `saved`, `return saved` | S at the return; C at cleanup; P | All independent |
| g-return (2a-2's list): `saved`, a local, read by `return saved` after cleanup | S at the return; C; P | All independent. Today (rule 2) C and P |
| `cf-g-reassign`: the candidate escapes through holder H; `saved = fb2` / `return saved` is refuted | C witnessed by H's use; S at the return; two P | All independent. Today the S is lost (rule 2) |
| m-scope, m-scope-use: a local read by the failing return after a nested scope ends | S at the return; C at the nested storage end | Both independent: the local carries the Loan after the access |
| m-36f | C | Independent: no chain from its Borrow |
| PreparedInput (PD11) | `DefaultArgumentBorrow_Kd` at the Borrow; S at the template's return | Not a Loan conflict: grouping, 2g-5's rule kept |
| Review-2's B1 (a): `p@follow = (local@ref, q@follow@ref)` / `q@follow = 2` | S (local's position); C at the write | C independent: `q`'s Borrow has no finite Origin, and its part's relation holds |
| Review-2's B1 (b): an Unknown relation, then a write | P; C | Independent (no S) |
| Round 3's B1 (b): `var v = q@follow@ref` / `if c => v = local@ref` / `p@follow = v` / `q@follow = 2` | S; C at the write | C independent: the Reborrow has no finite Origin |
| Round 3's B1 (c): `let r = x@ref` / `p@follow = r` / `let pair = (r, p0)` / `slot@follow = pair.1` | S; C at `x`'s storage end | C independent: `slot.*` received `x`'s Loan through a value read from `pair` (the holder rule), not as the borrow or a Copy of it |
| Round 3's B1 (d): `func make() -> uniq/(ref/i32 during q) during q`; `var r: ref/i32 during anchor` / `r = x@ref` / `s = make()` / `s@follow = r` | S; C at `x`'s storage end | C independent: the part's Origin `q` is not fixed (a region of the call) |
| Re-check R1: `var t: (ref/i32 during a, ref/i32 during a) = (p0, p0)` / `t.0 = x@ref` / `slot@follow = t.1` | S at `t.0`; C at `x`'s storage end | C independent: `t.1` carries `x`'s Loan by the holder rule, not as the borrow; removing `t`'s annotation removes S and leaves C |
| Re-check R1, flow-insensitive regions: `v = x@ref` / `let keep = v` / `v = p0` / `let pair = (v, keep)` / `slot@follow = pair.0` | S; C at `x`'s storage end | C independent: `slot.*` received the Loan through a read from `pair` |
| A conflict without a Refuted twin | C | Independent |

`rv/guard-annot`, m-write3 (its storage-end conflict derived, its write independent), j-region, the block, `if` and `match` result forms of
2a-2's list are read by the prototype (§6) (H).

## 4. Implementation direction (H)

- **A5 (2a-2, full process for its merge part).** D13 runs `MirOrigins.Judge` for all classes before `MirBorrows` per view, so the
  Borrows whose finite Origin a Refuted chain carries to a fixed Origin are known when MirBorrows runs. Two facts decide a conflict:
  a "direct" bit per (Loan, holder), set where the Borrow forms the Loan in its destination, kept by whole-value Copies, Moves,
  Reborrows and the call clause, cleared by part reads and any other arrival, combined by AND at joins; and, for `_0` and E, a
  per-store check that the receiving part's Origin is fixed. Without per-part facts a part read clears the bit conservatively
  (m-twice, m-pair and same-root then also report their returned Borrow's conflict). A conflict whose live later carriers are all
  `_0` or E with the bit set records the Refuted relations of those chains as its causes; nothing is dropped on arrival. An access that meets several Loans
  is one MIR problem with one witness (R4-M96): its causes are per Loan, and it is direct when any Loan's conflict is independent.
  Loans are already per Borrow site (R4-M97's D14), so no Borrow-identity fact is needed.
- **Facts to design (not available today).** The link from a MIR transfer to the fit and the relation row it realizes (`Relation`
  names a row); the bit and its memory term under `MirDataflow`'s limit (R4-M15a) with a V16 shape.
- **Publication (H3, R5).** The causes are MIR-to-MIR (a Loan problem caused by Origin problems), an extension of AR-7 that R5
  reviews. DIAGNOSTICS §4.3's invariant ties `DerivedFrom` to `PrerequisiteUnavailable_Kd`; keeping a derived problem's own code
  with cause facts (2g-1's pattern, extended to several causes) changes that invariant, R5's contract. A derived conflict whose cause
  is itself dropped (2g-5's PreparedInput rule) is published unsuppressed by §4.3; whether that occurs is a prototype question (H:
  not in templates, which end no slot's storage). Whether an Origin-stratum cause can be held is unverified (H): R4-M97 applies
  `Record`'s hold to every failure the body owns.
- **What goes.** 2a0-D §7's rules 1 and 2, the merge context (`RefutedAtResult`, `Ended`), `MirFactKind.Result` and 2a0-D §8's R5 ask
  for it, and 2g-5's A5-F4 and A5-F7 escape parts (they become W's causes). Stays: 2g-5's PreparedInput rule, R4-M10's Opaque cause
  (2g-1), the grouping of inline copies (R4-M97).

## 5. Departures from approved texts (named)

- R4-M94: 2a0-D §7's rules 1 and 2, OQ-4 (b) (GuardReverse), OQ-3 items (2) and (9); PD11's first half, and its second half with
  A3 E7's merge sentence, which rule 2 had replaced: W records these conflicts as derived instead of dropping them.
- The A5 step 2 plan's 2g-5 row: its A5-F4 and A5-F7 escape parts become W's causes; only the PreparedInput rule stays.
- 2a0-D §10's 2a-2 row: its done condition's exception ("a conflict another live holder or storage the body does not own also
  takes (m-store, `cf/p/jregion2`)") goes; the expectations of m-store, jregion2, m-twice/same-root, g-return, `rv-g-param` (the
  pinned DOUBLE becomes S and P, with C derived) and `cf-g-reassign` (the pin "no S at the return" flips) change.
- 2a0-D §5.1 and its planned STATUS record: m-write4's write conflict becomes derived; the pinned 2a-2 test ("the relation only")
  stays, now as a conforming result, and H11's list loses the item.
- 2a0-D §9's planned records of N-6's residual and the DOUBLE (DIAGNOSTICS' A6 merge rows, STATUS) are not written.
- DIAGNOSTICS §4.3's "Other ownership problems of the function stay direct" gains this one derived kind, and its invariant if H3
  keeps the Loan code (R5).
- PLAN G113's E12 sentence (an Auto PLAN update with 2a-2).

## 6. Verification before integration

1. **Prototype round** over 2a-2's list and §3's rows (2x-P4's merge model, `S2/A5OriginsPrototype.cs`, extended with §4's bit;
   §3's records replace its oracle, whose `Opt` rows for m-twice, same-root and m-scope and either-code rows for g-return and g-two
   differ). Success: §3's records, no acceptance change, and an external check: for an independent conflict, a repair variant that
   leaves the Loan flow unchanged leaves it; for a derived one, every repair of the named relations removes the conflict or changes
   its Loan flow. A differing case returns to the owner with the case.
2. **SPEC session** for §15.6.5's sentence and the two citations after the prototype, before 2a-2's design review.
3. **R5** reviews §4's publication part.

## 7. Principles

1. **One concept, one canonical form.** One sentence replaces two family rules; §15.6.5's and §15.8.2's code sentences are its
   instances where the returned value's only later carrier is the result (in m-scope's shape both records stay).
2. **Local reasoning.** Whether a conflict is derived follows from the values that carry its Loan and the parts they received it
   into, all facts of the body.
3. **Explicit semantics.** The derived case is stated, and independence everywhere else is the default.
4. **Compiler Server Protocol.** An escape reports its relation; its consequent conflict keeps the relation as its cause.

## 8. SPEC text (planned mapping)

| Section | Change |
| --- | --- |
| §15.6.5 | §2's sentence after the sentence on locals |
| §14.8.3 | One citation: a value kept past guard cleanup is judged by §15.6.5's sentence |
| §15.8.2 | Its sentence on a returned body-local borrow cites §15.6.5's |

## 9. Unverified

- §3's readings (from the case descriptions and round 3's probes, not run on the MIR).
- The bit's memory and work terms (V16) and its per-view masks.
- H3's publication choice.
