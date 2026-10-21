# Independent review, round 2: Failures Caused Only by Other Failures (2026-10-22)

Reviewer: independent (wrote neither round 1 nor round 2). Read in `bin/worktrees/r4-proposals` at `dev` `4902dd3c`:
`draft/Proposals/2026-10-21 Failures Caused Only by Other Failures.md` (uncommitted). Checked against AGENTS.md, SPEC §23.1.2,
§15.6 (introduction, §15.6.1–§15.6.6), §15.8.2, §14.8.3, §7.2.3, DIAGNOSTICS §4.1–§4.3 and the "MIR Loan problems" A6 table,
2a0-D §0, §1, §5.1, §6, §7, §9, §11, §12 and `2a0-D-review.md` (§2 probes, §4 table, R2.2, P-1, P-6), the A5 step 2 plan (PD11,
PD17, AR-7, rows 2a-2, 2g-1, 2g-5), DECISIONS R4-M10, R4-M94, R4-M96–R4-M99, INTEGRATED's last entry and round 1's review
(`draft/Sketches/R4 Proposals 2026-10-21/review-1.md`, section P2). Nothing was built, run or edited. The `rv/` and `cf/` probe
sources are outside the repository, so readings of those cases rest on their descriptions in 2a0-D and its review; "(H)" marks a
reading of mine that no run checked.

## Verdict: NOT ACCEPTED

Four blocking findings. The proposal's frame is right (define only the existing term, no "reported once", grouping stays free, the
relation is the primary record of an escape), and P2-B1 and P2-B5 are resolved. But the definition still derives too much: it
attributes to a rejected operation every fact that operation produces, not the fact it was rejected for. Read as written it loses
independent failures (B1), creates cycles and leaves what a Loan conflict rejects undecided (B2), and it does not support the
PreparedInput row or §3's "no pairing" claim (B3). The table omits or misreads cases the approved 2a0-D decides (B4).

This is the second round on one family of findings (the general definition of "caused only by"). AGENTS: "after two rounds on one
family of findings, the owner chooses between a representation, a whitelist and a hold." The shorter formulation at the end is a
whitelist (one concrete sentence in §15.6.5), option B is the representation route and option C the hold; I recommend presenting
that choice instead of a third general wording.

## Round 1's blocking findings

| Finding | Status in round 2 |
| --- | --- |
| P2-B1 "reported once" | **Resolved.** "One escape" and "reported once" are gone; grouping stays implementation-defined; two relations are both independent (§2 row 4). |
| P2-B2 testability | **Partly.** The definition now uses "operation", "fact" and "comes only from", but those terms are not precise (B2). `rv-g-annot-esc` is classified, wrongly (B1 (c)); round 1's `var r: ref/i32 during a = …; r = x@ref; return r` is still not in the table (B4). |
| P2-B3 narrowing | **Resolved for `return text`** (`text`'s Type comes from the annotation). The same narrowing returns for facts carried by a rejected operation's value (B1). The row is mislabelled as §23.1.2's example (N1). |
| P2-B4 PreparedInput | **Partly.** Grouping is SPEC-legal, but "one operation" contradicts §2's own bullets and the implementation remains a cross-strata pairing (B3). |
| P2-B5 facts | **Resolved in substance.** §3 lists facts to publish and §6 step 2 designs them in 2a-2's full process; fact 2 is misdescribed and provenance is missing (N3). |

## Citations checked

Accurate: §23.1.2's sentence and where §5 inserts the definition; the §15.6.5 and §15.8.2 quotations; §14.8.3 has no code
sentence and 2a0-D R6 reads "either code is admitted"; rules 1 and 2 as in 2a0-D §7; the PreparedInput rule as PD11's second
sentence and 2g-5's row; R4-M10 with 2g-1's one cause fact; R4-M94's recorded limits (`cf-g-reassign`, `rv-g-param`'s two codes)
and 2a0-D §9's DIAGNOSTICS/STATUS records; D13's order (Judge, MirBorrows per view, Record); R4-M96's one witness; 2a0-D §6.3's
`Anchor`; the merge context names; OQ-3 items (2) and (9); DIAGNOSTICS §4.3's invariant and suppression rule; 2g-D in plan §4.9;
the owner's quoted instruction. Inaccurate or unsupported: N1 (§23.1.2's example), N5 (AR-7's scope), N6 (OQ-4 (c)'s
direction), N10 (round 1's owner answers are not recorded in the repository).

## Blocking findings

### B1. The definition derives every fact of a rejected operation, not the fact it is rejected for (§2, §3)

An operation is rejected for one fact: a fit's Refuted relation for one Origin position and one finite source, a Type mismatch
for the Type, a Loan conflict for the access meeting one Loan. "Comes only from operations those failures reject" derives
everything else that operation carries too. Each program below is rejected either way (acceptance is unchanged), but the second
Error, which survives every repair of the first, becomes optional:

(a) A Refuted relation in a fit that also carries an unrelated Loan (within §3's own scope):

```kimi
func keep(p: uniq/(ref/i32 during a, ref/i32 during a), q: uniq/i32 during a)
    let local: i32 = 1
    p@follow = (local@ref, q@follow@ref)  // Error: local's Borrow cannot outlive a (Refuted)
    q@follow = 2                          // Error: p.* keeps the shared child Loan on q.*
```

The child Loan on `q.*` reaches `p.*` only through the fit the relation rejects, so by §2 the write conflict is caused only by the
relation, and by §3 it records the relation as its cause and DIAGNOSTICS §4.3 suppresses it. Replacing `local@ref` by any
`ref/i32 during a` repairs the relation and leaves the conflict.

(b) An Unknown relation (§2's text covers every failure, not only Refuted ones):

```kimi
func keep(p: uniq/(ref/i32 during a), q: uniq/i32 during b)
    p@follow = q@follow@ref   // Error (Proof): b outlives a is not proven
    q@follow = 2              // Error: p.* keeps the shared child Loan on q.*
```

Annotating `q` `during a` repairs the relation; the conflict stays. SPEC §15.6 says why: "Origin regions and `outlives`
relations only check that lifetimes are compatible; they never keep a Loan live", so a fit's Loan flow does not depend on its
relations' verdicts.

(c) The proposal's own row 6, `rv-g-annot-esc`, is §14.8.3's example with `saved` annotated:

```kimi
// Packet and keep as in §14.8.3.
func f(fallback: ref/Data during a, packet: Packet)
    var saved: ref/Data during a = fallback
    match packet
        .Data(let d) if keep(saved@uniq, d) => ()   // Refuted (g cannot outlive a) and err(ended)
        _ => ()
    use(saved)
```

Without the annotation this is §14.8.3's example, whose comment is exactly this err(ended). Row 6 makes it caused only by the
relation, so adding an annotation that fails on its own makes the SPEC example's Error optional; removing the annotation repairs
the relation and leaves the err(ended). The approved 2a-2 list expects "S and C" here (2a0-D §10, R4-M94). Row 3's own reason
("the Loan's liveness at the write comes from reading `r` at the return, and the read is no rejected operation") gives the
opposite verdict here: `saved`'s liveness at cleanup comes from `use(saved)`, an accepted read. Row 5 (`rv-g-param`) differs in
the way that matters: its carrier is storage the body does not own, used after every point by the destination itself, not by a
later operation.

A Type mismatch on an initializer that carries a Loan (`let r: string = x@ref`, then a write to `x` while `r` is used) is the same
case at the SPEC level (H: on this implementation the Borrow inside a failed node is not built, 2a0-D §5.1).

Fix: define by the rejected fact. For the Loan/Origin pair that motivates the proposal: a Loan conflict is caused only by Refuted
relations when the conflict's Loan is that of the relations' own Borrow (the chain's finite source), and every value that carries
it and may be used after the access is the result or storage the body does not own, having received it only through fits those
relations refute (see the shorter formulation). §3's cause rule must be per Loan and Borrow, not per fit.

### B2. What a failure rejects, and what a Loan conflict's fact is, are not decided; read literally, the definition has cycles (§2)

- "An expression or declaration the failure rejects" does not say what a storage-end conflict or an err(ended) rejects (a scope
  exit and a guard cleanup are neither). If a storage-end conflict rejects the local's declaration, j-bad's relation, whose chain
  starts at a Borrow anchored there, is caused only by the conflict.
- A Loan conflict at an assignment rejects the store that is also the fit; at a Borrow it rejects the operation that forms the Loan
  and the chain's finite Origin. "Every way the fact arises passes through such an operation" then derives:
  - a required relation from an unrelated conflict:

    ```kimi
    func g(p: ref/i32 during a)
        var t: ref/i32 during a = p
        let b = t@ref
        let x: i32 = 1
        t = x@ref        // Error: writes t while b's Loan is live; Error: x's Borrow cannot outlive a
        inspect(b)
    ```

    The relation's chain arises only through the assignment the conflict rejects, so the relation becomes optional, though removing
    `inspect(b)` leaves it;
  - a conflict from a conflict: `let r = x@ref; let u = x@uniq; inspect(r); x = 2; inspect(u)`. The second conflict's Loan arises
    at the Borrow the first rejects; moving `inspect(r)` above `let u` leaves it;
  - cycles. In `func fill(p: uniq/(uniq/i32 during a))` with `var x = 1` and `while more()` around `p@follow = x@uniq`, the
    relation's chain arises at the Borrow that the loop-carried conflict rejects (§15.6: a Borrow is checked against an earlier
    execution of its site carried by `p.*`), and that conflict's Loan reaches `p.*` only through the fit the relation rejects.
    The PreparedInput row is the same: the relation's chain arises at "the kept Borrow" `DefaultArgumentBorrow_Kd` rejects, and
    that requirement's fact (the result keeps the Borrow) arises only through the return fit the relation rejects.
  These contradict §2's "a relation is never caused only by the conflict that reports the same escape". Under DIAGNOSTICS §4.3 a
  `DerivedFrom` cycle is published unsuppressed, and its invariant makes both records `PrerequisiteUnavailable_Kd`: neither code
  would appear.
- The Loan conflict's fact is "the Loan's presence where the access meets it" in the bullet, but row 3 reasons by liveness, and
  row 6 needs presence. Presence alone is not a failing fact (a dead holder also has it); SPEC §15.6's fact is liveness (values that
  carry the Loan and may be used after the access).

Fix: state what each failure kind rejects as a fact (B1), that a Loan conflict rejects only the access's meeting of the Loan (never
the Loan, the Origin or the fit the access also forms), that relations are never caused only by Loan conflicts, and that the Loan
fact is liveness, with the use part counted (an accepted later read makes it independent; the result's and storage the body does
not own's implied uses do not).

### B3. The PreparedInput reading and §3's claims (§2 row 10, §3 "Causes at formation" and "What goes", §7 item 1)

- By §2's bullets the relation rejects the template's return fit and `DefaultArgumentBorrow_Kd` rejects "the kept Borrow": two
  operations, so "one operation" does not follow. Under the narrow reading the requirement is caused only by the relation (its fact
  arrives only through the return fit), which makes §7.2.3's record the optional one, opposite to "A5-F9 keeps §7.2.3's code";
  under the literal reading they form a cycle (B2). What is true is that both report one fact (a slot's new Borrow kept by the
  result), which §23.1.2's free grouping covers; say that.
- Grouping two records with different codes is not available in DIAGNOSTICS' model (problems aggregate by check key and code), so
  one producer must omit or merge the other. Judge's problem is recorded at `Record`, after the replay in which R26-borrow finds the
  Loan, so this is R4-M98/PD11's 2g-5 rule ("2g-5 drops an Origin-stratum problem whose chain end R26-borrow reported"): a pairing
  across both strata. That contradicts "no pairing runs after both strata have recorded", "2g-5's per-family merges are replaced
  by the causes" and §7's "one definition replaces four family rules". Under option A, rules 1 and 2 go; the Opaque rule and the
  PreparedInput rule stay as they are.

Fix: read the PreparedInput pair as one fact under two requirements, name the component that groups it and keep it under "What
stays"; correct §3, §4 (A's assessment) and §7.

### B4. The table omits cases the approved design decides and departs from it without naming the departure (§2, §4, §6)

- **m-write4** (`slot@follow = x@ref; x = 2`, `cf/p/m-write4`): 2a0-D §5.1 says "SPEC 23.1.2 also wants the write conflict" and
  §9 puts "reports only the relation" in STATUS as a limit until H11. Under §2 the write is caused only by the relation (its Loan
  reaches `slot.*` only through the rejected store). That reading is defensible (every repair of the relation that keeps `x` a local
  removes the conflict), but it reverses an approved SPEC reading and a STATUS limit; §6 lists the case without a reading, and §4
  does not name the departure.
- **jregion2**: the 2a0-D review's probe table gives "S, S (+C admitted, 23.1.2)" for "returned and also stored". Row 9 classes it
  with m-store as independent ("one flow passes no rejected operation"); if its second S is the store's (H), row 4's reading
  applies (caused only by both).
- Round 1's P2-B2 case `var r: ref/i32 during a = …; r = x@ref; return r` (the chain closes at the assignment, the witness is the
  return): not in the table. Under §2 it is caused only by the relation; say so.
- The P records of g-two and `rv-g-param` (2a-2's list: "S, C at cleanup, P"; "S, C and P") are still not classified (round 1's
  N3).
- `rv-g-annot-esc`: 2a-2 expects S and C; row 6 makes C optional (B1 (c)). Any reading that survives B1 must state its change to
  2a-2's list.

Fix: classify these cases; list in §4 and OQ-D1 every approved reading or record that A changes (2a0-D §5.1 and §9's STATUS limit,
2a-2's test expectations, DIAGNOSTICS §4.3's "Other ownership problems of the function stay direct", R4-M98's "ordinary
conflicts in a template are independent" where a template conflict becomes derived).

## Non-blocking findings

- **N1.** §2's last row is not §23.1.2's example. The example is `let text: string = 1` then `return missing` ("reported as well;
  it is independent"); `return text` is round 1's variant. Under §2, `return missing` is independent (no declaration supplies the
  name). Label the row "a variant of §23.1.2's example" or use the example.
- **N2.** §1 says three sentences fix codes; §15.6.1's "A failed relation adds no constraint to region inference, so it causes no
  Loan conflict or result mismatch of its own" and §15.6's "never keep a Loan live" are the SPEC basis for why the conflict exists
  at all and are consistent with the proposal. Cite them.
- **N3.** §3's facts. (i) Fact 2 is misdescribed: the Origin problem already names its last edge's row by the `Relation` fact
  (2a0-D §6.3: row index, with Site, Source and Destination), and `Result` is the conflict's fact, not the relation's. What is
  missing is the correspondence between a MIR transfer and the row it realizes; the 2a0-D review rejected correlating them by site
  (§3.8), so the owner publishes it. (ii) Missing: provenance. R4-M96's witness is a holder's next use, not the path by which the
  Loan entered the holder; "every flow passes a rejected fit" needs, per live holder, whether the Loan entered only through
  refuted fits for its Borrow. A Loan variant per refuted Borrow (the plain Loan and its "entered through a refuted fit" twin) would
  carry it (H), at a cost §9's "witness set's cost" does not cover.
- **N4.** Row 4's conflict has two causes, but 2g-1's representation is one cause fact and its done condition is "every derived
  problem has exactly one valid cause". §3 offers 2g-1's form as the alternative publication; it cannot hold row 4. Name the
  change to 2g-1.
- **N5.** AR-7 is R5's acceptance of H3 reading `Binding.FailureCauses` at publication (PD17). A MIR problem naming another MIR
  problem as its cause extends AR-7; say "extends".
- **N6.** OQ-4 (c) drops the Refuted relation when its fixed end is reached through a holder an err(ended) witnesses (keeping the
  err(ended)); A keeps the relation and derives the conflict. A uses the flow fact OQ-4 (c) proposed, in the opposite direction;
  "the general form" overstates it.
- **N7.** 2a0-D §6.3 already admits "a report caused only by an earlier failed edge ... as derived (R8)". §1's inventory omits
  this fifth reading; classify it under the definition.
- **N8.** Rows omit records: m-write also has the storage-end conflict through `_0` (derived; rule 1 drops it today). The 2a0-D
  review's R2.2 describes `rv-g-param` as an escape through `_0` that is also stored; if so it has two Refuted relations, and A's
  "one primary record (H)" becomes two.
- **N9.** §5 wording: the §14.8.3 sentence makes "a value" the failure and says "through a rejected fit" where the condition is
  "only through rejected fits". §15.6.5's first clause (a local's lifetime is a Loan conflict) involves no failed relation and is
  not an example of §23.1.2; only its second clause is.
- **N10.** The new OQ-D2 ("When?") reuses the ID of round 1's different OQ-D2, which the proposal says the owner answered (a);
  renumber. Round 1's owner answers are not recorded in DECISIONS or INTEGRATED (the commit `4902dd3c` says candidate 3 is being
  redone); cite where they are recorded, or mark them relayed.
- **N11.** 2g-5 also merges A5-F7's escape. §3 finds causes per body and view (D13); state that every cause lies in the same body
  and view, or how a Closure escape's cause in another body is found.
- **N12.** Principle 2: whether a record is caused only by another depends on the other stratum's verdicts (which fits are
  refuted), not on "the failing operation's own facts" (round 1's N8 again).
- **N13.** §6's success criterion ("no lost independent failure, and §2's readings") is circular where §2 defines independence;
  add an external check, such as the approved 2a-2 expectations or a repair variant per case (B1's repair test).
- **N14.** Readings reconstructed from probes outside the repository (`rv-g-param`, g-two, `cf-g-reassign`, m-store, jregion2)
  should carry "(H)" in the row, not only in §9.

## The six checks, briefly

1. P2-B1 and P2-B5 are resolved; P2-B2, P2-B3 and P2-B4 partly (table above). Citations are accurate except N1, N5, N6, N10.
2. Not precise enough to test (B2). m-write and j-bad are coherent under a presence reading (the Loan entered `r` through the
   accepted `let`; it entered `_0` only through the refused fit), but row 3 justifies by liveness, which contradicts row 6 (B1 (c)).
   Cycles exist under the literal reading (loop store; PreparedInput). The PreparedInput "same operation" reading does not hold by
   §2's bullets (B3). Consistent with §23.1.2's example and with DIAGNOSTICS §4.3's Binding cases (missing information, recovery,
   later phases); §4.3's "Other ownership problems of the function stay direct" needs an amendment under A.
3. Row by row: 1, 2, 3 (verdict), 4, 5, 7, 8 hold; 3's reason, 6, 9 (jregion2, H) and 10 do not; 11 is mislabelled. Lost
   independent failures: B1 (a)–(c), B2's assignment and Borrow cases. No acceptance change found.
4. Fact 2 is misdescribed, provenance is missing (N3); the H3 code consequence is right (the invariant makes every derived MIR
   problem `PrerequisiteUnavailable_Kd` from recording, which also changes `MirReport`'s key and the ledger's code comparison), and
   a cycle publishes both under that code (B2). A post-recording pairing remains for PreparedInput (B3).
5. The options are fair; the recommendation does not follow until B1–B3 are fixed. OQ-D1 and OQ-D2 are decidable. "(H)" is
   mostly global (N14). Superseded decisions named: R4-M94's rules and PD11's first half; not named: B4's list, 2g-1's one cause
   (N4), AR-7's extension (N5). Principle 2 does not hold as worded (N12).
6. Below.

## Shorter formulation (reviewer's suggestion, unverified)

Leave §23.1.2's general term undefined (SPEC already leaves derived reports implementation-defined) and give the one pair that
needs a rule a concrete sentence in §15.6.5, replacing its last clause and referenced from §14.8.3 and §15.8.2:

> A Loan conflict, including one at guard cleanup ([§14.8.3](14-control-flow.md#1483-guards)), on the Loan of a Borrow whose finite
> Origin a Refuted relation carries to a fixed Origin is caused only by those relations
> ([§23.1.2](23-compiler-services.md#2312-reporting)) when every value that carries the Loan and may be used after the access is the
> result or storage the body does not own, and each way it received the Loan passes a fit those relations refute.

Readings: derived: j-bad, the two-relation case (both causes), `rv-g-param`, m-write's storage-end conflict, m-write4 (2a0-D
§5.1's reading reversed, by name), round 1's `var r: ref/i32 during a` case, the loop's conflict. Independent: m-write's write,
the nested block, `rv-g-annot-esc` (a local carrier; 2a-2's S and C kept), g-two, `cf-g-reassign`, B1 (a) and (b), B2's
assignment and Borrow cases. Relations are never derived, so no cycle. §7.2.3's `DefaultArgumentBorrow_Kd` and the template's
Refuted relation report one fact, which §23.1.2's grouping covers; 2g-5's PreparedInput rule stays. The implementation is 2g-1's
cause list with the Borrow identity and a provenance bit per live carrier (N3), all recorded in MirBorrows after Judge. If the
owner wants a general sentence in §23.1.2 as well, it must be by fact ("the fact that makes it fail holds only as a consequence
of the facts that make those failures fail"), with §15.6.5's sentence as its normative instance.
