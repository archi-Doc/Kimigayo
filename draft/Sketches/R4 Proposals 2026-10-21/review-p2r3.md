# Independent review, round 3 (W): Failures Caused Only by Other Failures (2026-10-22)

Reviewer: independent (wrote none of rounds 1–3). Read in `bin/worktrees/p2r3-rev`, detached at `origin/dev` `9031f7bc`:
`draft/Proposals/2026-10-21 Failures Caused Only by Other Failures.md`, against AGENTS.md, SPEC §15.6 (introduction),
§15.6.1–§15.6.6, §15.8.2, §15.3.4, §14.8.3, §23.1.2, §7.2.3, DIAGNOSTICS §4.3 and the "MIR Loan problems" table, 2a0-D §0,
§1, §5.1, §6.3, §7, §9, §10, §11, `2a0-D-review.md` (probe tables), the 2x-P4 prototype (`A5OriginsPrototype.cs` and the
review's `cf/protoC` copy), DECISIONS R4-M10, R4-M94, R4-M96–R4-M98, the A5 step 2 plan (PD11, 2a-2, 2g-1, 2g-5), round 2's
text (`4b6613ca`, its W row), INTEGRATED's entry and both prior reviews. The planning session's clarification (the relation is
meant per Origin position) is taken into account. Nothing in the repository was built or edited. "(H)" marks a reading no run
checked.

Probes: the 2a0-D review's probe sources (outside the repository) were read for `jregion2`, `m-write3`, `m-write4`. Five small
programs were checked with the existing Release `Kimi.dll` of the main checkout (built 2026-10-17, not rebuilt; default
pipeline only, A5's MIR problems are unpublished): B1's (b), (c) and (d) programs bind with exactly the one
`UnsatisfiedOriginRelation_Kd` claimed below, and (d)'s unannotated variant binds with no record.

## Verdict: ACCEPT WITH FIXES

Three blocking findings, all text fixes inside W. The frame holds: only Loan conflicts are derived and only from Refuted
relations, so there is no cycle and no acceptance change; Unknown relations derive nothing; a local holder used later keeps a
conflict independent; §7.2.3's pair stays grouping; the departures from R4-M94 and 2a0-D §5.1 that the owner's W row named are
named. But the sentence as written is not anchored to the Loan's own Borrow, reads per fit, does not say which relations of a
Refuted chain count, and does not tie the escape to the holder, and each gap makes a failure optional that §23.1.2 requires
(B1). §4's bit implements the per-fit reading (B2). Two §3 rows contradict the sentence and the approved 2a-2 row they change
is not named (B3). The reviewer's corrected sentence (end) is shorter, restores the anchor of the owner's chosen W text ("the
Loan of a Borrow refuted to a fixed Origin"), keeps every §3 reading except the corrected rows, and needs no owner answer.
Re-check the corrected sentence, §3 and §4 once (a confirmation, not a redo) before the prototype round.

Test used for "required" (as in review-2): a conflict is independent when a repair of the failing relation that leaves the
conflict's Loan flow unchanged (a changed annotation, or a changed value of another Borrow) leaves the conflict.

## Blocking findings

### B1. The sentence derives conflicts the Refuted relations do not explain (§2)

Each program is rejected either way; the second Error survives a repair of the relation, so §23.1.2 requires it, but W makes
it optional.

(a) **Per fit, not per relation.** A fit forms one relation per Origin position (§15.6.1), so "a fit whose relation is
Refuted" does not select one. Read per fit, a Loan that enters a holder through a Refuted fit at a position that does not carry
its Borrow is derived. Review-2's B1 (a) (`p@follow = (local@ref, q@follow@ref)`, then `q@follow = 2`) is one case. Another
is in 2a-2's own list: m-twice / same-root (2x-P4: `let a = x@ref`, `let b = x@ref`, `let pair = (a, b)`, `return pair.0`;
same-root `(x.f@ref, x.g@ref)`). `_0` carries `b`'s Loan only because reading `pair.0` carries every Loan of `pair` (§15.6
holders). Replace `a` by `p` in the pair: the return relation holds, and x's storage-end conflict on `b`'s Loan stays. m-pair's
`y` Loan is the same case with two roots. The text must state the per-position reading. Stating it is not enough, because of
(b)–(d).

(b) **Not anchored to the Borrow's finite Origin.** "The Refuted relations that carry that Borrow's value" also covers a
relation refuted for another Borrow at the same position:

```kimi
func keepA(p: uniq/(ref/i32 during a), q: uniq/i32 during a, c: bool)
    let local: i32 = 1
    var v = q@follow@ref     // Inferred Origin (reached through q), contained in a; a child Loan on q.*
    if c
        v = local@ref
    p@follow = v             // Error: local@ref cannot outlive a (Refuted; probe: this one record)
    q@follow = 2             // Error: p.* may keep the child Loan on q.*
```

The store's single relation `v' outlives a` is Refuted and carries the Reborrow's value to `a`. p.* is the only later
carrier and received the child Loan only through that relation, so W derives the write conflict, also under the per-position
reading. Delete the `if` (or store any `ref/i32 during a`): the relation holds and the write conflict stays. The owner's W text
said "the Loan of a Borrow refuted to a fixed Origin" and review-2's said "a Borrow whose finite Origin a Refuted relation
carries"; round 3 dropped the anchor.

(c) **Which relations of a Refuted chain count is not decided.** §15.6.5 refutes chains; it calls only the closing one "the
Refuted relation at the returned value". If every relation on a Refuted chain counts, W loses a required failure:

```kimi
func fC(p: uniq/(ref/i32 during a), slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    let r = x@ref            // relation x's Origin -> r', on the Refuted chain closed below
    p@follow = r             // Error: x@ref cannot outlive a (probe: this one record)
    let pair = (r, p0)
    slot@follow = pair.1     // relation holds; slot.* carries x's Loan by the holder rule
    // x's storage end: Error: slot.* (and p.*) keep x's Loan
```

slot.*'s flow passes `let r = x@ref`, whose relation lies on the Refuted chain. Delete `p@follow = r`: no relation fails and
the storage-end conflict stays through slot.*. If only closing relations count, `rv-g-param`'s row fails instead: the
candidate's Loan enters `target.*` through `keep`'s value argument and the callee's store, while the closing relation is the
target argument's equality. That is SPEC-safe (an extra report) but contradicts §3. The sentence must name the closing
relations and say that a call's argument fits count together, or (simpler, see the end) test the value's Origin where the
holder received it.

(d) **The holder's Origin is not tied to the escape.** Storage the body does not own includes a call result its contract ties
to no argument (§15.6), whose Origins are fresh regions at the call (§15.3.4 allows an Origin only in the result):

```kimi
func make() -> uniq/(ref/i32 during q) during q   // q only in the result: a fresh region per call
    loop => ()

func fB(anchor: ref/i32)
    var x = 1
    var r: ref/i32 during anchor = anchor
    r = x@ref                // Error: x@ref cannot outlive anchor (probe: this one record)
    let s = make()
    s@follow = r             // relation anchor -> q' holds
    // x's storage end: Error: s.* keeps x's Loan
```

s.* is storage the body does not own, used after every point, and its only flow passes the Refuted assignment, so W derives
the storage-end conflict. Remove r's annotation: the probe gives no record at all, and SPEC still rejects the program by the
storage-end conflict. The conflict is independent; nothing reports that s.* keeps the Loan, because its Origin is not fixed.
(The variant `let v = x@ref; p@follow = v; make()@follow = v` fails the same way under (c)'s inclusive reading.)

Fix: replace §2's sentence by one anchored on the Borrow's finite Origin, per position, testing the value and the part where
each later carrier received the Loan (reviewer's formulation at the end), and drop the second sentence (N1). Add (a)–(d) and
review-2's B1 (a) as §3 rows and prototype cases, each with its repair variant (N8).

### B2. §4's bit implements the per-fit reading (§4)

- "A flow through a Refuted fit sets it in the destination" sets the bit for every Loan crossing the fit. Review-2's B1 (a)
  (the child Loan of `q@follow@ref` crosses the Refuted Tuple store), B1 (a) above (m-twice, m-pair) and B1 (b) all become
  derived, so review-2's blocking counterexample is not resolved by §4 as written.
- The bit is cleared only by a join. A later arrival through a holding fit adds the Loan to the holder (§15.6: "a written value's
  Loans are added"), so B1 (c)'s slot.* would keep a bit set elsewhere. Every arrival must combine, not only joins.
- Per-holder Loan sets do not record the part a Loan arrived in: `_0 = pair.0` takes every Loan of `pair`. The condition needs
  the arriving value's Origin at the receiving part: MirOrigins' finite-source bitsets (D16) give whether the Loan's Borrow
  reaches that Origin. The part's Origin must be fixed (B1 (d); E abstracts all such storage, so the test is per store).
- Per-atom identity (2a0-D §7: two Borrows of one root share `Projection[x]`) cannot tell m-twice's two Loans apart. Under W the
  second Loan's conflict is independent, so either the facts distinguish Borrows of one atom or the case is a recorded limit
  (a lost independent failure, the program stays rejected). Rule 1 used per-atom identity to drop more; W needs per-Borrow
  identity to derive less.

Fix: state the bit's rule: an arrival of a Loan into a part keeps the bit only when the part's Origin is fixed and the arriving
value's Origin there is reached by a Refuted chain from that Loan's Borrow (or is its fixed end). Every other arrival clears it.
Add to "Facts to design" the part and arriving value per arrival, the Borrow identity, and a call's arguments as one unit.

### B3. §3 misreads named cases; the approved 2a-2 row changes unnamed (§3, §5)

- **m-store and `cf/p/jregion2` are derived under W, not independent.** 2x-P4's m-store is `target@follow = local@ref`: its
  only later carrier is target.*, received through the Refuted store (the prototype already marks the conflict optional).
  jregion2's source (`jr2`) is `var r = local@ref`, `slot@follow = r`, `return r`, and the 2a0-D review's probe output shows two
  Refuted rows at both uses. That is §3's "Two flows" row, derived from both. Its records are known, so the "(H)" can go.
  2a0-D §10's 2a-2 done condition excepts exactly these ("a conflict another live holder or storage the body does not own also
  takes (m-store, `cf/p/jregion2`)"). Under W they go too: a named change.
- **m-twice / same-root.** The row describes "the second returned" and reads "C derived". 2a-2's cases return `pair.0`, and
  under the per-position reading the other Borrow's conflict is independent (B1 (a)). The prototype's oracle (`Opt("C end Lb")`,
  `Opt("C endx Lg")`) has the per-fit reading, so §6's "§3's records exactly" would accept the wrong result.
- **g-return missing** (2a-2's list; p12 guardReturn). `saved` is a local read by `return saved` after cleanup, so under W the
  err(ended) is independent and the return's Refuted relation is kept: S, C and P, where rule 2 gives C and P. That is SPEC-legal
  (both codes for one escape), but it is a change in the guard half that §3 and §5 should state.

Fix: correct the three rows; add g-return, m-pair and the B1 cases. In §5, name the 2a-2 row's changes: the done condition's
exception, and the expectations of m-store, jregion2, m-twice/same-root, g-return, `rv-g-param` (the pinned DOUBLE becomes S
and P, with C derived) and `cf-g-reassign` (the pin "no S at the return" flips). Also name the records 2a0-D §9 plans for N-6's
residual and the DOUBLE (the DIAGNOSTICS A6 merge rows and STATUS).

## Non-blocking findings

- **N1.** The second sentence ("count as used after every access") repeats §15.6 ("A value stored there may be used after every
  point") for storage the body does not own; the result is used at the return. As a §15.6.5 sentence it reads like a liveness
  rule. Drop it, or scope it to this rule.
- **N2.** Terms. §15.6's holders are locals, parameters and temporaries; storage the body does not own is not one. Use §15.6's
  "every value that carries the Loan and may be used after the access" in place of "holder that keeps the Loan and is used after".
- **N3.** §5's DIAGNOSTICS item cites the Binding bullet ("every other failure is direct"). The ownership sentence W changes is
  "Other ownership problems of the function stay direct" (the Later phases bullet), plus the invariant if H3 keeps the Loan code.
- **N4.** §7 item 1 ("§15.6.5's and §15.8.2's code sentences become its instances") holds only where the returned value's only
  later carrier is the result. In m-scope's shape (a local read by the failing return after a nested scope ends), W reports the
  storage-end conflict as well. Say so.
- **N5.** Row 2 (`var r: ref/i32 during a`) should state the function's result Origin (round 1's case: `during a`).
- **N6.** An access that meets several Loans is one MIR problem with one witness (R4-M96), for example x's storage end with
  m-twice's two Loans. The cause is per Loan, and the problem is direct when any Loan's conflict is independent. State it in §4.
- **N7.** A derived conflict whose cause is dropped (2g-5's PreparedInput rule) or held (R4-M73) has a `DerivedFrom` naming an
  unpublished problem, which DIAGNOSTICS §4.3 publishes unsuppressed (under the invariant, as `PrerequisiteUnavailable_Kd`). Say
  whether this occurs in templates (H: probably not, since a template does not end a slot's storage), or name the published cause.
- **N8.** §6's success criterion ("§3's records exactly") is circular where §3 defines independence (review-2 N13 again). Add
  an external check: for each case, a repair variant that leaves the Loan flow unchanged must leave every conflict W calls
  independent and remove every conflict W calls derived.
- **N9.** §5 / §4 "What goes" also omit: 2g-5's A5-F4 and A5-F7 escape parts (they become W's causes; only PreparedInput stays),
  `MirFactKind.Result` and 2a0-D §8's R5 ask for the `Result` fact (rule 1's mechanism), and PLAN G113's E12 sentence (an Auto
  PLAN update). 2a0-D §6.3's R8 admission (a relation derived from an earlier failed edge) stays an implementation choice under
  the undefined general term; say so (review-2 N7).
- **N10.** The `cf-g-reassign` row omits its two P records (independent).
- **N11.** The m-write4 change is justified (every relation repair that keeps x a local removes the write conflict) and
  complete for 2a0-D §5.1 and its STATUS record. Add that the pinned 2a-2 test ("the relation only") stays, now as a conforming
  result, and that H11's list loses the item.
- **N12 (outside this proposal; H).** The probe build accepts `let s = make()`, `s@follow = x@ref`, `x = 2` (and B1 (d)'s
  unannotated variant). By §15.6, s.* is storage the body does not own (a call result tied to no argument), so the write and x's
  storage end are Loan conflicts. This is a possible acceptance gap on the default pipeline: the build is 2026-10-17's, not
  checked on dev, and the MIR's verdict is unpublished. It is a candidate PLAN issue for the planning session.

## Checks

1. **Precision.** "Holder" and "keeps" need §15.6's terms (N2). "Passes a fit whose relation is Refuted" is per fit and does not
   say which relations of a chain count (B1 (a), (c)). Losses of a required failure: B1 (a)–(d). No cycle (relations are never
   derived) and no acceptance change (every derived conflict has a Refuted relation, an Error).
2. **§3 row by row.** j-bad, row 2 (N5), m-write, m-write4 (named, justified; N11), Two flows, the loop store, `rv-g-param`
   (depends on the call unit, B1 (c)), `rv-g-annot-esc`, g-two, `cf-g-reassign` (N10), PreparedInput and the twin-less conflict
   hold. m-store/jregion2 and m-twice/same-root do not (B3). jregion2 is fairly flagged "(H)" but readable now and misplaced. Of
   the unread list, m-pair is not "beyond the rows above": its `y` Loan tests the per-position reading. g-return is missing.
3. **Prior blocking findings.** Review-2 B1 (a) is resolved only by the clarified per-position reading, which the text must state
   and §4 must implement (B1 (a), B2). B1 (b) is resolved: only Refuted relations derive, and the Reborrow's Origin is not finite.
   B1 (c) is resolved (`t` is a local; 2a-2's S and C). B2 (cycles), B3 (PreparedInput as grouping, 2g-5 named) and review-1's
   P2-B1/B3/B4 are resolved. B4 is resolved except jregion2 (B3).
4. **§4 and §5.** The order (D13), the missing transfer-to-row link, the memory term and H3's two choices are accurate. The bit's
   rule is wrong (B2). Superseded but not named: B3's list and N9. DIAGNOSTICS' cited sentence is the wrong one (N3). STATUS has
   no current record to supersede (2a0-D §9's are planned).
5. **Owner.** None needed. The fixes restore the anchor of the owner's chosen W text. The reversed owner decisions (R4-M94's
   rules 1 and 2, OQ-4 (b), OQ-3 (2) and (9); 2a0-D §5.1's m-write4 reading) were named in the W row the owner chose. B3's
   further changes follow from W and need naming, not an answer. The bit and the cause fact are representations for 2a-2's full
   process (planning session, DECISIONS); no closed vocabulary grows (2a0-D: `MirFactKind` is not listed in ARCH). H3 keeping the
   Loan code on a derived problem changes DIAGNOSTICS §4.3's invariant, which is R5's contract (an AR-7 extension), not an owner
   question.
6. **Shorter formulation** below.

## Shorter formulation (reviewer's suggestion, unverified)

One sentence in §15.6.5, after the sentence on locals, replacing §2's two:

> A Loan conflict on the Loan of a Borrow is caused only by the Refuted relations of the chains from that Borrow's finite
> Origin ([§23.1.2](23-compiler-services.md#2312-reporting)) when every value that carries the Loan and may be used after the
> access is the result or storage the body does not own and received the Loan only into parts with a fixed Origin, in values
> whose Origin there such a chain reaches.

"Reaches" includes the chain's fixed end (row 2's `r: ref/i32 during a`). It is per part and per Borrow, so the per-position
reading needs no separate statement, and it does not depend on which relations of a chain are "Refuted".

Readings (H):
- **Derived:** j-bad; jregion; row 2; m-write3's storage-end conflict; m-write4; m-store; Two flows and jregion2 (both causes);
  the loop store; `rv-g-param` (d's value, Origin `g_d`, into `target.*` at `a`); m-twice's and m-pair's returned Borrow.
- **Independent:**
  - review-2's B1 (a) and (b), and B1 (b) above: the Borrow has no finite Origin;
  - m-twice's and m-pair's other Borrow, and B1 (c): the arriving value's Origin is not reached from the Borrow;
  - B1 (d) and its variant: the part's Origin is a fresh region;
  - the local holders: m-write, m-scope, `rv-g-annot-esc`, g-two, g-return, `cf-g-reassign`;
  - m-36f: no chain from its Borrow.
- **Unchanged:** relations are never derived, so there is no cycle; Unknown relations derive nothing; PreparedInput stays
  grouping (2g-5).

An even shorter variant, "received the Loan only through those relations" (the arriving fit's own relation must close such a
chain), also closes (c) and (d). It makes row 2's and m-write3's storage-end conflicts independent (an extra report).

## Re-check (2026-10-22): the revised proposal

Read: `bin/worktrees/r4-proposals`, `draft/Proposals/2026-10-21 Failures Caused Only by Other Failures.md` (uncommitted, on
`127b6f0e`). Not edited.

### Final verdict: ACCEPT WITH FIXES

One blocking finding, R1, in §2 and §4. It is a defect in my own round-3 wording, which the revision applied faithfully. The
other items are non-blocking text fixes.

### Round 3's findings

| Finding | Status |
| --- | --- |
| B1 (a)–(d) | **Resolved** for the four cases: per part and per Borrow, the finite-Origin anchor, Origins rather than "Refuted fits", and the fixed part. §3 has rows for each. But the corrected sentence has a new hole (R1). |
| B2 | **Mostly resolved.** The per-fit setting is gone, a later arrival clears the bit, the test is per store for E, a grouped access gets per-Loan causes, and the H3 invariant and the dropped-cause case are stated. Two gaps remain: round 3's join rule was dropped (R1, N-R2), and the arrival test is R1's. |
| B3 | **Resolved.** m-store, jregion2 and m-twice/same-root are corrected; g-return, m-pair, m-scope, m-36f and the B1 cases are added. §5 names the changes to the 2a-2 row, 2a0-D §9's unwritten records, the right DIAGNOSTICS sentence, G113 and `MirFactKind.Result`. |
| N1–N12 | Applied (N1, N2, N3, N4, N5, N6, N7, N8, N9, N10, N11). N12 is outside the proposal. |

### R1 (blocking, §2 and §4). Testing the arrival by Origins misses Loans that the holder rule carries

The sentence asks whether the value that brought the Loan into the part has an Origin that a chain from the Borrow "reaches",
and the note adds that "reaches" includes the chain's fixed end. Fixed Origins are shared names, and the holder rule (§15.6: a
value read from a part carries every Loan the holder carries) moves a Loan into a value whatever that value's Origin. So the
test can pass for a value that never carried the Borrow:

```kimi
func fT(slot: uniq/(ref/i32 during a), p0: ref/i32 during a)
    var x = 1
    var t: (ref/i32 during a, ref/i32 during a) = (p0, p0)
    t.0 = x@ref              // Error: x@ref cannot outlive a (Refuted)
    slot@follow = t.1        // relation a -> a holds; t.1 carries x's Loan by the holder rule
    // x's storage end: Error: slot.* keeps x's Loan
```

slot.* is the only later carrier. It received the Loan in t.1, whose Origin `a` is the fixed end of the chain from x's Borrow,
so the sentence derives the conflict. Remove t's annotation: no relation fails (the two slots are now separate regions), and the
storage-end conflict stays. §23.1.2 requires it.

Dropping "fixed end included" does not cure the cause, because regions are flow-insensitive:

```kimi
var v = p0
v = x@ref
let keep = v
v = p0
let pair = (v, keep)
slot@follow = pair.0
```

pair.0's region is reached from x's Borrow through `v`'s earlier value, yet pair.0 carries the Loan only by the holder rule, from
`keep`. Replace `v` by `p0` in the pair: nothing fails, and the conflict stays. The Origin-based bit in §4 inherits both holes.

**Cause (one level higher).** A Loan reaches a value independently of that value's Origins. So whether the Loan arrived *as the
escape* must be judged by the identity of the carrier, using §15.6's own distinction ("the borrow itself, its Copies, references
derived from it", as against aggregates holding them and values read from a holder's part), not by Origins.

Fix (§2):

> A Loan conflict on the Loan of a Borrow whose finite Origin Refuted chains carry to fixed Origins is caused only by the
> Refuted relations of those chains ([§23.1.2](23-compiler-services.md#2312-reporting)) when every value that carries the Loan
> and may be used after the access is the result or storage the body does not own and received the Loan only as the borrow, a
> Copy of it or a reference derived from it ([§15.6](#156-borrow-checking)), stored into a part with a fixed Origin.

"Whose finite Origin Refuted chains carry" builds in existence: m-36f, raw laundering and Reborrows derive nothing. The note
"'Reaches' includes the chain's fixed end" goes.

Readings (H), each checked against §3: all of §3's verdicts are unchanged.
- Row 2 and m-write3's storage-end conflict stay derived: `_0` receives a Copy of the borrow into its fixed part.
- `rv-g-param` stays derived (the candidate reference into `target.*` at `a`).
- B1 (c) is independent (pair.1 is not a Copy of `x@ref`). B1 (d) is independent (a Copy, but into the fresh part `q`).
- m-twice's other Borrow is independent (`pair.0` is a Copy of the first Borrow).
- The two programs above are independent.

Fix (§4):
- MirBorrows' per-(Loan, holder) bit means the Loan arrived directly. An arrival keeps it only when the Loan comes as the
  borrow, a Copy or a derived reference into a part with a fixed Origin. A read of a part of an aggregate holder (the holder rule)
  and every other arrival clear it.
- At a join the bit holds only if every incoming copy that carries the Loan has it. Round 3's text had this; the revision dropped
  it.
- Loans are already per Borrow site (R4-M97 D14, `Loan.Location`), so "Borrow identity where two Borrows share one atom" becomes
  unnecessary.
- Precision on aggregates needs per-part Loan facts. Without them, a part read conservatively clears the bit: m-twice's, m-pair's
  and same-root's returned Borrow then report the conflict as well. That is SPEC-legal; §3's m-twice and m-pair rows should
  allow it.
- The causes are the Origin problems whose Reason source is the Borrow's atom. Two Borrows of one atom share causes, which is
  harmless for suppression.

If this wording fails its confirmation, that is the second round on the arrival condition (AGENTS). It does not reopen the
owner's choice of W.

Probe (the same 2026-10-17 build, default pipeline): `fT` gives the one `UnsatisfiedOriginRelation_Kd` at `t.0 = x@ref`. The
unannotated repair gives no record. By §15.6's holder rule that program is still rejected by x's storage-end conflict, so the
old analysis appears more precise per element than §15.6. That is acceptance beyond the SPEC letter, not a miscompile, since
t.1 holds `p0` (H; outside this proposal, like N12).

### Non-blocking (re-check)

- **N-R1 (§3).** Round 3's B1 (c) row: the conflict is at `x`'s storage end (p.* and slot.* keep the Loan), not "slot.*'s storage
  end". B1 (d)'s operative reason is that the part's Origin is not fixed. Add `fT` and the flow-insensitive case as rows.
- **N-R2 (§4).** Restore the join rule (R1's fix). An OR-join would derive `if c => slot@follow = r else => slot@follow = pair.1`.
- **N-R3 (§6).** The prototype's oracle differs from §3 in more than m-twice. same-root's `Opt` and m-scope's
  `Opt("C scope Ll")` are required under W. g-return's and g-two's either-code `Fail("S …", "C …")` become both required. Say
  that §3's records replace the oracle.
- **N-R4 (§5).** 2g-5's A5-F4 and A5-F7 escape parts appear only in §4's "What goes". 2g-5 is a row of the owner-approved A5
  step 2 plan, so name it in §5.
- **N-R5 (§4).** R4-M73's hold applies to Loan problems (`MirBorrows.Record`). Whether an Origin-stratum cause can be held needs
  a line (H). Otherwise "held" in the dropped-cause bullet does not apply.

## Confirmation (2026-10-22): the re-check's fixes

Read: `bin/worktrees/r4-proposals`, the same uncommitted file. Not edited. Probes used the same 2026-10-17 build and the
default pipeline.

### Final verdict: ACCEPT WITH FIXES

R1 is confirmed. The arrival test by the carrier's identity closes both of R1's programs, and §3's readings follow from it:
- Row 2 and m-write3: `_0` receives a Copy of the borrow.
- B1 (c), `fT` and the flow-insensitive case: holder-rule reads.
- B1 (d): the part is not fixed.
- m-twice/m-pair: the second Borrow's value is not a Copy.

N-R1 to N-R5 are applied. The directness rule did not fail again.

One blocking item remains, C1: the sentence does not say how a call passes a Loan on. It is a gap in what the rule covers. Its
strict reading is SPEC-legal, so it is not a second failure of the arrival condition. No owner question is needed.

### C1 (blocking, §2, §3, §5). Calls are not decided

§15.6 lists "the borrow itself, its Copies, references derived from it" apart from a call's results and the storage it writes,
which "carry the Loans of the arguments they may retain" (§15.6.4). The two readings of the sentence differ:

- **Strict** (a call's result or written storage is none of the three). `rv-g-param`'s candidate reaches `target.*` through
  `keep`'s write, so its C is independent. §3's row ("C derived") and §5's item ("the pinned DOUBLE becomes S and P") then
  contradict the sentence. The owner's W row expected this DOUBLE to close (H). `return id(local@ref)` also reports both records
  (probe: one S at the return). This reading is SPEC-legal.
- **Generous** (a retaining result is "a reference derived from it"). It loses a required failure:

  ```kimi
  func k(x: (ref/i32 during o, ref/i32 during q)) -> ref/i32 during q   // retains q, so carries all of x's Loans (15.6.4)
      return x.1

  func fK(p: uniq/(ref/i32 during a), p0: ref/i32 during a) -> ref/i32 during a
      var x = 1
      let r = x@ref
      p@follow = r              // Error: x@ref cannot outlive a (probe: this one record)
      return k((r, p0))         // relation q' -> a holds; _0 carries x's Loan by retention
      // x's storage end: Error: p.* and _0 keep x's Loan
  ```

  Delete `p@follow = r`: nothing fails (the probe gives no record), and SPEC still rejects the program by the storage-end
  conflict through `_0`.

**Fix (recommended).** Add one clause, using §15.6's narrow term:

> … received the Loan only as the borrow or a Copy or Reborrow of it ([§15.6](#156-borrow-checking)), stored into a part with
> a fixed Origin; a call's result, or storage the call writes, that may retain such a value as an argument counts as a Copy of it
> ([§15.6.4](#1564-calls)).

This is safe. A whole-value borrow argument's own Loan is retained only through its outer Origin (§15.6.4: "any other Origin of
an argument carries the argument's Loans"; an inner Origin carries only a read's Loans). So the retaining part's Origin is on the
chain from the Borrow, and the fixed-part condition still applies to written storage, which keeps `make()`-like parts out.

Readings under the clause:
- Derived: `rv-g-param` (`keep`'s slot may retain `value`, a Copy of the candidate borrow); `return id(local@ref)`.
- Independent: `fK` (the retained argument is a Tuple holding a Copy, not such a value); a write through a call into `make()`'s
  storage (the part is not fixed).

Add `fK` and `id` as §3 rows. **Alternative:** keep the strict reading, change `rv-g-param`'s row to "all independent", and
change §5's item to "the DOUBLE stays, now as a conforming result". Either closes C1.

### Non-blocking

- **C2 (§2).** "A reference derived from it" collides with §15.6's own "derives from", which (for reach into storage the body
  does not own) includes "a value read from a part of a holder that holds one", which is exactly the holder rule the sentence
  excludes. The explanatory paragraph is proposal text, not SPEC. Write "a Copy or Reborrow of it" (§15.6's non-owned bullet uses
  "a Copy or Reborrow of one"). A Borrow past a shared dereference is a Reborrow here (§15.6.3).
- **C3 (§4).** One bit "set when the Loan arrives … into a part with a fixed Origin" cannot judge `return r` in jregion: whether
  r's value is "a Copy of the borrow" depends on r holding the Loan directly, and r's part is not fixed. Read literally, jregion,
  Two flows and `rv-g-param` (all through an intermediate holder) would be reported. That is SPEC-legal but contradicts §3. Use
  two facts:
  - "Direct" per (Loan, holder): set where the Borrow forms the Loan in its destination; kept by whole-value Copies, Moves,
    Reborrows and C1's call clause; cleared by a part read of an aggregate holder (conservative) or any other arrival; combined
    by AND at joins.
  - For `_0` and E: every direct arrival was into a part with a fixed Origin, checked per store.
- **C4 (§3).** Row 2's reason still reads by Origins. Write: "`_0` receives r's value, a Copy of the borrow, into its fixed
  part". In the m-twice/m-pair rows, the returned Borrow's conflict is derived by the sentence. The conservative implementation
  reports it as well, so it is not "independent too".
- **C5 (§4).** "An Origin-stratum cause is never held" is unverified: R4-M97 has `Record`'s hold apply to every failure the body
  owns, and MirOrigins records at D13's `Record` step. Mark it (H) or check it.
- **C6 (§6).** The repair check cannot show "removes every conflict W calls derived": a derived conflict has no relation repair
  that keeps its Loan flow, which is the point (my N8's wording). Write "for a derived conflict, every repair of the named
  relations removes the conflict or changes its Loan flow".
- **C7 (outside; H).** The probe build accepts `fK` without its store. By §15.6.4's per-argument retention that program is
  rejected by x's storage-end conflict. This is the same pattern as N12 and the `fT` repair (the old analysis is more precise, or
  has a gap). It is a candidate PLAN issue, not this proposal's.
