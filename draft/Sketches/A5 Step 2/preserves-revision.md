# A5 step 2, unit 2f-S1: the S2 revision sketch

Date: 2026-10-19. Base: `dev` `30fe97e1`; "SPEC 8.11.4:789" is line 789 of `docs/spec/08-…md` at that commit. Status:
working record for round 2x-P6, design 2f-D and the owner's OQ3 (DECISIONS R4-M74); SPEC and DECISIONS decide. It changes
no SPEC, compiler, library or test. The proposal `draft/Proposals/2026-10-16 Preserves Results by Receiver Place.md`
("S2", base `de80a4c0`) stays untouched until 2f-S2 revises it from this sketch.

Inputs: S2; design A3 (`../A5 Step 0/design-A3.md`) §5 and §12; the A5-F6 audit
(`artifacts/handoff/2026-10-18-pause/r4-scratch/step2-audit/F6/audit.md`, "F6 audit"); PLAN G113; the step-2 plan
(`draft/Proposals/2026-10-18 A5 Step 2 Implementation Plan.md`): PD7, PD16, PD23, PD30 and rows 2x-P6, 2f-D. R4-M68 is
1c-5's entry, not yet on dev; R4-M75 cites it: an activation meets only Loans shared in the holder where it finds them,
with two conditions for 2f. Nothing was built or run: every verdict is a reading of
the source, and (H) marks an unverified inference. Φ is the phantom that stands for Loans an abstract call's result may
keep (A3's `Φ_c`); cases `c1`… are listed in §8.

## 1. Rebase: S2's references at dev

| S2 site (at `de80a4c0`) | At `30fe97e1` | For the revision |
| --- | --- | --- |
| 8.11.3 :774; 8.11.4 :785, rule 3 :791; 8.11.5 :816; 8.11.6 :843, :845, :849, :852 | Same lines, same text | S2 §2.4's replacements apply, except the statics clause of its Conformance paragraph (§3) |
| 6.4.6 :349 | :349, same text | Applies. P6's intake deleted 6.4.6's anchor rules, not this sentence (INTEGRATED 2026-10-17, P6 item) |
| 15.6.3 :517 ("A result may be kept across later calls on the same receiver …") | :582, same text | Applies at :582; :570 (P12) and :528 (E8) are new (§2) |
| 22.2.3 :135; App. E :87 | :135; App. E :88 | Apply |
| Loan Identity by Base §2, §2.1 (access paths; caller Loans without a path) | Integrated (R4-M39): SPEC 3.4.1, 15.6.2:500, 15.6:450 ("The Loans a body receives through its parameters' Origins have no access path in it"); its base superseded by E8 (R4-M52) | Cite SPEC; S2 uses no base |
| STATUS line 158 (containment, P34) | SPEC 15.6.3:528 ("The child is contained in the Origin of … a receiver"); A5 unit 2a (A3 E4) | Cite SPEC |
| PLAN line 38 (R1g); `CallableEffectBoundTest` 134–149, 396–407 | PLAN:39; 134–150, 397–408, rows unchanged | — |
| Library and milestone lines (§4, §7) | Unchanged, except `Core.kimi:326, :359` → `:327, :360` | — |
| §7 test usage: 19 files, 59 lines, 70 occurrences | 21, 61, 71 (added: a comment at `MirBorrowsTest.Prototype.cs:10`, `spec-cases.json:27`'s 8.11.3 row); library 1 (`Iterator.kimi:6`), milestones 0 | — |

## 2. SPEC 15.6.3 with P12 and E8

1. **:582.** S2's text ("on the same receiver Place") replaces it as written.
2. **:570, P12's sentence** ("the value also carries every Loan of the receiver argument except the receiver Loan") stays.
   Its Loans are kept by the result, so they lie inside rule 3's exclusion (8.11.3:774 "including Loans kept after
   transfer or Reborrow"). They never meet the later call of r on X anyway: they are Loans of the references X was
   reached through, on roots other than the later receiver Borrow's path (15.6.2:505; in `takeFirst`, :574–579, `r`'s Loan
   on `h.*` against a second `r.0.take()`), and they carry no mutable-static Loan (§3). Proposed: only Φ carries the tag;
   P12's Loans stay ordinary, so `h.1 = 5` still errs (c11). (H)
3. **A dynamic virtual call is not a P12 call** (INTEGRATED 2026-10-17, P12 item), but 6.4.6:347 gives it rules 1–3, so it
   needs Φ (rule 1, §4) without P12's Loans (c27).
4. **E8 (:528).** Φ has no access path, so no in-place destruction ends it, as :570 requires ("Destroying the Iterator or
   remainder does not end published child Loans"). A Place phantom (A3 §5: on `recv(c)`'s referent) is ended by E8 when
   the receiver's Move Path is destroyed in place. Today two things cover that: an observing destruction is checked
   first and conflicts (:528; prototype 33b), and P12's parent Loans catch a replaced holder (36g). A Place phantom that
   carries Moves (§5.1) must also move its Place, or it is ended at the moved-from receiver's storage end (c5).
5. An assignment or destruction of X both invalidates the relation (§5.2) and, by E8, ends the Loans X prefixes; Φ stays
   and only its tag drops.

## 3. Statics under P6

**S2 §2.2's refinement is vacuous (H) and is deleted.** It compares a conformance body's static effects with the Loans of
the receiver's Origins, which "may be anchored in any mutable static". A valid caller never passes such a Loan where it
matters:

- a body's mutable-static Borrows never leave it (15.6.4:602, P6);
- an input that carries a mutable-static Loan makes a call with unknown environment effects an error at that call
  (15.6.4:599; 15.6.7:689, "every reservation must be able to become live at once, with … effects on static storage";
  A3 E13 (1)(b)), so the first of two non-confined calls already fails, and a valid first call's result keeps none;
- a `confined` body has no mutable-static effect (8.11.2:718).

"Anchored" also left SPEC with P6. Cases c37–c39.

**S2 §2.1's static-rooted invalidation is kept.** A call between the two calls invalidates X when X is rooted at a static
key and the call's summary may write, exclusively borrow, replace or destroy that static, or the call has unknown
environment effects. The rule is not vacuous for `confined` requirements; with a non-confined one, the first call fails
as above. It reads A4's keys (A5-F5, a prerequisite of 2f-4). Cases c40–c42.

## 4. How rule 1 is decided

SPEC 8.11.4:789 is Origin-based: "an abstract input may reach every Loan its Origins may denote". 8.11.5's `Merge` errs
only through it (F6 audit §1.1 item 4), as does 8.11.1's Take-only `takeTwo` (:704). Those Loans have no path in a
generic body (15.6:450). A5's `Φ_E` never conflicts, and A3's `desc(c)` adds Φ only for descent from the receiver. So MIR
today accepts the ledger's rule-1 rows (`5e1a449cc7bf`: Take-only `0c1b007e8702`, `145e6068e0a6`; tupled
`583395f779cc`; Callables f and g; two requirements `626adb54494d`; an unbounded Callable twice `e1c1b93d054c`; F6
audit §4).

**Proposed: carrier reach** (PD16's first option).

- **Issue.** An abstract call c issues a phantom Φ(c,i) for each input i that one of its retention rows ties to its result,
  or through G148's W (rule G) to storage it writes (G113's store part). An abstract call is a requirement call without a
  known concrete callee, a call through a Callable-constrained value, or a dynamic virtual call. Φ(c,i) replaces `Φ_c` in
  `desc(c)`; P12's Loans stay.
- **Carriers.** The Origin atoms of i's Type after normalization: Type parameters and projections with their hidden atoms,
  and signature Origins. Each atom has a mode, `ref` when its slot's Loan requirement is `ref` (15.3.5) or it is reached
  past a shared layer (8.11.4:789), else i's access mode. For a receiver this is R4-M68's condition ("a shared-receiver
  Φ_c takes the receiver's mode").
- **Tag.** `(r, X)` when r has `preserves results` and X is c's receiver Place without a non-path index; for a Callable,
  X is the callable's Place (S2's 8.11.6 text). Otherwise the tag is `none`.
- **No access path.** No read, write, Move or Borrow reaches Φ; E8 never ends it (§2); no reservation meets it.
- **Reach.** An abstract call c2 reaches Φ when one of c2's inputs has one of Φ's carrier atoms, the same atom or one the
  body relates by an equality or `outlives` fact, in conflicting modes (15.6.2:517). An observing destruction of a value
  whose Type has such an atom also reaches it (8.11.2:721; 15.6.6:661). A concrete call reaches Φ only if 2x-S1 reads
  `59e2289ce4f4` so; today's implementation composes user generic helpers (`EffectBoundCallerTest` row `through`,
  `UserGenericCallsKeepTheUnboundedModel`).
- **Exclusion (rule 3).** Φ is excused at a call of r on X when every copy of Φ in the check set carries `(r, X)`. This is
  S2 §2.1's must-fact: the tag is part of the element, so a join that keeps a `none` copy defeats the excuse.
- **Codes.** Φ met at a call is `CallEffectConflict_Kd` at that call, related to the earlier call (PD7, DIAGNOSTICS:157). Φ
  met by a destruction takes the destroy row (`ComparisonLoanConflict_Kd`). In a conformance it is 2f-5's
  `IncompatibleContractImplementation_Kd` or `UnsatisfiedEffectBound_Kd`.
- **Deletes.** E2's derived excuse at c2's reservations, `Φ_c^dist` (it becomes the `none` tag) and E3's residue (§5.7).

**Fallback: A3's Place phantom.** Φ sits on i's referent with its content, with the mode above. It accepts `Merge` and
Callables f and g (other Places, one carrier): these are SPEC errors, so it needs a second rule. It rejects `moved`, c3,
c10 and puts takeAcross's error at the `swap` (S2 §3 places it at the second call). These are listed losses (OQ8). R4-M68's
conditions and E3's residue apply to it.

**Open reading (G2, §9).** By the letter, two values of one abstract Type share atoms, so `EffectBoundCallerTest`
(`AcceptsCallsThatTheBoundsOrTheValuesSeparate`) rows `zip` and `both`, valid today ("through the same value", its header
comment), are errors. Its row `pairs`, a nested `for` over one `ref/C`, stays valid through the `ref` modes; `takeLate` and
`sumTwo` stay valid. Cases c13–c26.

## 5. Divergences from A3 §5 and the A5 plan

**5.1 Move carry.**
- S2 §2.1: a Move of X or an ancestor renames the tag to the destination path when that path occurs in the body, and
  otherwise drops it; `moved` is valid.
- SPEC: 22.2.3:135, "A Move of the Iterator carries the bound"; :133, moving it invalidates no item.
- A3: `disturbed` lists Moves (§5), and its Place Φ (mode `uniq`) is hit by the Move itself (`hits`, deep move), so A3
  rejects `moved` (F6 audit §1.1 item 3).
- **Resolution:** S2's carry, a rename of the tag in every holder at the Move. In `MirLoanSets` it is Remove+Add per holder,
  or one operation superseding R4-M53's four (2f-D, after 2x-R10). Under carrier reach the Move meets no Φ.
- **Cases:** c1–c6.

**5.2 Invalidation.**
- S2: a write, assign, destroy, part Move or exclusive Borrow of X, a part of it or an ancestor invalidates; so does passing
  one exclusively to a call; the receiver Borrow of a call of r on X does not, nor do shared Borrows, reads or calls of r
  on X (6.4.6:349); a non-path index relates nothing.
- SPEC now: value identity (8.11.3:774 "Destroying or replacing `v` …").
- A3: writes, Moves or destructions of a direct `D ⊑ recv(c)` and writes through a Place hitting it; no exclusive-Borrow or
  index clause.
- **Resolution:** S2's list. Its one exception, c2's receiver Borrow, subsumes E2's row. Under carrier reach the only effect
  of an intervening exclusive Borrow is the dropped tag, so takeAcross errs at the second call, as S2 §3 says.
- **Cases:** c7–c12 and §7.

**5.3 Calls inside a conformance.**
- S2 §2.2: calls contribute what they contribute in any body. A concrete call contributes its argument Borrows, retention
  and summary (15.6.4); an abstract call contributes rules 1–3. Callee bodies contribute no Place effects.
- A5P's S2 row (A5P:481, :525–528), design A (`design-A.md:231-233`) and `audit-calls.md:164-169` use declared effects only.
- SPEC 8.11.4:785 checks bounds against completed transitive summaries.
- **Resolution:** S2's (its §8 gives the reasons). It is A5's ordinary call rule, with no new fact, and :785's summaries then
  serve `confined` alone (S2 §2.4).
- **Cases:** c28, c36.

**5.4 The earlier result.**
- S2 §2.2: a value live from entry to every exit that keeps the Loans the body's results keep, computed at the body's
  returns.
- A3 §5 (H in A3): an escaped `Φ_prev` on `*self` with a fresh anchor. `Storage.splitFirst(self.state@uniq)` meets it, and
  no excuse covers it, so A3 rejects ArrayIterator and DictionaryIterator (O3; F6 audit §1.1 item 3).
- **Resolution:** S2's holder, one more holder that the body never writes, whose set is the union of σ(`_0`) at the returns.
- **No fixed point (H).** One forward pass gives that set, so no fixed point is needed. The 2f-D row's "fixed point" is
  needed only if the holder took part in carrying, which it must not.
- **Its own site.** By site identity, a Borrow meets its own earlier execution (15.6:450, :455).
- **What it holds** for a valid body: tagged Φ of delegated calls, `Φ_E` and markers, and Loans on paths of parameters
  other than `self`.
- **A Loan through `self`** is a containment failure (15.6.3:528) that the Origin stratum reports, so its conformance
  problem merges into that one (A6, as E12; 2f-D).
- **Cases:** c28–c35.
- **G1:** whether "the Loans its results may keep" means the body's returns or the declared Type.

**5.5 Unclassifiable effects (not in the plan's list).**
- S2 §2.2: "An effect that cannot be classified (§8.11.2) conflicts, as today." 8.11.2:721–722: "a conflict under either
  bound".
- **Today's verdicts** (tests on the old pipeline):
  - Rejected: bodies that destroy an abstract item (`IteratorOriginEffectsTest`: `ASelectionThatMayDropTheItemIsRejected`
    for Choose, `ABranchThatMayDestroyTheItemIsRejected` for Branch, `AnArmThatDropsThePayloadIsRejected` for Pick,
    `ANestedPatternThatDropsAPartIsRejected` for StoredPair, `AnItemStoredInADroppedAggregateIsRejected` for
    NumberedOnce).
  - Valid: `Plain` (Item `i32`; `_ = self.inner.next()`; `EffectBoundImplementationTest.PreservesResultsTracesResultLoans`).
    Today's conflict is with what the declared result may keep
    (`VirtualEffectObligationTest.PreservesResultsComparesLoansRetainedByTheDeclaredResult`).
- **Under carrier reach**, destroying an abstract value reaches the earlier result's Φ through its atoms. With S2's
  by-body earlier result, bodies that return only `.None` keep nothing and conform. That is 8 of the ledger's 35
  `a4-preserves` rows: StoredPair's 3, NumberedOnce's 2, Pick's 2 and `ALocalItemThatMayBeDestroyedIsRejected`'s third
  row (`293217626cfd`, `9e3eebb7dad4`, `de480c244515`, `5cb6ada766a6`, `a32e2a134524`, `31958147e78a`, `4d840fc71ba2`,
  `446e364d4ad6`).
- **The F6 audit's (H) is refuted.** It says Choose, Branch and StoredPair (`0bd427e70ef6`, `2ac7e65b4d02`,
  `293217626cfd`) conform under S2 as writes of disjoint Fields. They are 8.11.2 rejections that 2f-5 reports; only
  StoredPair's verdict depends on G1.
- **Their writes of disjoint Fields** (`self.steps`, `self.skipped`) do not invalidate `self.*.inner` (§5.6). The
  destruction decides the verdict, not the writes.
- **Unknown environment effects** of calls (8.11.2:722) meet only mutable-static Loans (15.6.4:599), and there are none
  here (§3). The delegated call's are therefore harmless (S2 §4: "together with rule 2's unknown environment effects").
- **Cases:** c31–c33, c36c.

**5.6 Delegated tags.**
- S2 §2.2: a delegated Loan is excluded at the body's calls of d on w, unless the body moves a value on w's path or
  invalidates w for d anywhere.
- SPEC 8.11.5:816: "replaces, swaps or moves a value on the path to `w`". A3 §5: "undisturbed since entry".
- **Resolution:** S2's. Its invalidation list covers replace and swap. "Anywhere" holds because the earlier result comes
  from an earlier execution, so the check is body-wide, before the conformance pass.
- **Cases:** c29, c30, c35.

**5.7 Reservation windows (R4-M68), E2 and E3.**
- **R4-M68.** An activation meets only shared-mode Loans, on two conditions: "a Φ_c met at a receiver's formation is
  located at its later call; a shared-receiver Φ_c takes the receiver's mode, or 2f reruns this rule's prototype variant
  with that case".
- **Under carrier reach** Φ meets no formation or activation, so the first condition is vacuous. The second is the carrier
  mode (§4), so R4-M68's argument holds unchanged.
- **Under a Place phantom** both conditions bind. A `uniq` Φ issued inside a reservation window (`it.consume(it.peek())`)
  is missed by the formation, which comes before Φ, and by the activation, which skips `uniq`: unsound (c43).
- **E3's residue** (A3 §12 E3: re-add the shared cut if Φ lies past a shared Deref) is vacuous under carrier reach; a Place
  phantom needs it (c44).

## 6. The 12 + 3 conformances at dev

| Conformance | `next` | Earlier result (A5) | Effects | Verdict |
| --- | --- | --- | --- | --- |
| ArrayIterator, DictionaryIterator (`Array.kimi:274`, `Dictionary.kimi:96`) | `Storage.splitFirst(self.state@uniq)` | Content retention of `state` (`during state.source`): `Φ_E`, σ(E); no Φ (concrete call) | Argument Borrow of `self.*.state`; `splitFirst` (`Storage.kimi:110`, `:126`) touches no static | Conforms (§5.3, §5.4) |
| ArrayUniqIterator, DictionaryUniqIterator (`Array.kimi:284`, `Dictionary.kimi:106`) | Same (`Storage.kimi:118`, `:136`) | Same | Same | Conforms |
| ArrayOwningIterator, FixedArrayOwningIterator, DictionaryOwningIterator (`Array.kimi:293`, `:304`, `Dictionary.kimi:115`) | `Storage.takeFirst(self.state@uniq)` (`Storage.kimi:102`, `:174`, `:145`) | `Φ_E` (element Loans, no path) | Argument Borrow; a raw Take; nothing destroyed | Conforms |
| RangeIterator, ClosedRangeIterator (`Core.kimi:327`, `:360`) | Copy reads and writes through `self` | Nothing (`T is PrimitiveInteger`) | Copy writes | Conforms |
| SliceIterator (`Slice.kimi:89`) | `self.values[index]@ref/T` | No Loan past the Slice's shared Deref (A3 E3, E6); the Slice's Loans, no path | Shared reads; the Copy write of `self.position` | Conforms |
| `Iteration.OwningIterator<I>` when `I is Iterator` (`Iteration.kimi:15`, `:25`) | `self.iterator.next()` (`:32`) | Φ tagged `(next, self.*.iterator)`; P12's `Φ_E` | The delegated call: Φ excused, nothing on the path moved or invalidated (§5.6); its environment effects meet nothing (§5.5) | Conforms, checked under its condition (8.11.4:785) |
| `Iteration.BorrowingIterator<I>` (`Iteration.kimi:42`, `:52`) | `self.iterator.next()` (`:59`) through `uniq/I during source` | Φ tagged `(next, self.*.iterator.*)` | Same | Conforms |
| `Cursor<T>` (`Milestone13.kimi:6`, `Milestone29.kimi:45`) | SliceIterator's shape | Same | Same | Conforms |
| `Drain<T>` (`Milestone29.kimi:13`) | `exchange(self.first@uniq, with: .None)` by position (`:30–33`) | Content retention of the target: `Φ_E` | Exclusive Borrows of `self.*.first`…; `exchange` runs no user code and destroys nothing | Conforms |

Under A3's earlier result (§5.4), `Φ_prev` on `*self` meets every row whose body borrows or writes through `self`, so
every row except the adapters fails (H). The F6 audit names the `splitFirst` rows. No row depends on G1 or G2 (H): none
destroys an abstract value, and none calls two abstract receivers. The library's only generic iterator code is the adapters
(`Iteration.kimi:6, 11, 16, 43`, by grep for bounds other than `Self is`); the milestones have none.

## 7. S2 §5's losses at dev

Library and milestone counts come from the greps of §1 and §6. Test counts come from S2 §7's two line scans, rerun on dev
over the joined, unescaped C# strings; every hit was checked by hand (scratch script, not tracked).

| # | Form | Current SPEC → S2 | lib / ms / tests | Until 2f-S2 (PD30) |
| --- | --- | --- | --- | --- |
| 1 | The same value through Places that share no path: `let r = it@uniq; r.next()` after `it.next()`, joins, returned receivers (c6), clones, reloaded elements; also `h.inner.take()` after `r.inner.take()` with `r = h@uniq` (prototype round 9 finding 4) | valid → error at the later call (rule 1) | 0 / 0 / 0 | Located `Unsupported_Kd` at the later call |
| 2 | Other operations on X between the calls | (H) for an abstract receiver, another requirement call or a whole write between the calls already errs by rule 1 or the destruction, so only an exclusive Borrow passed to a concrete call changes a verdict (c12), where the current text cannot decide (R1g) | 0 / 0 / 2 rows (`CallReservationTest:218`, `EffectBoundImplementationTest:193`): concrete callers, rule 3 not involved, unchanged | `Unsupported_Kd` (c12) |
| 3 | A slot call through a Reborrow of the handle (6.4.6:349 "Reborrows … preserve the relation") | valid → error | 0 / 0 / 0 | `Unsupported_Kd` |
| 4 | A receiver value moved by `exchange` or `swap` | valid → error | 0 / 0 / 0 | `Unsupported_Kd` |
| 5 | A non-path index (`arr[i].next()` twice) | valid → error | 0 / 0 / 0 | `Unsupported_Kd` |

Every test S2 §5 lists as "not a loss" exists at dev under the same name. Those tests change with containment (2a), not
with S2. Two verdict changes come from other rules and are not S2 losses: G1 (8 rows become acceptances under (ii)) and
G2 (`zip` and `both` become errors under rule 1's letter).

## 8. Cases for round 2x-P6

Each verdict is SPEC's as S2 revises it (OQ3). Brackets give the current SPEC's verdict where it differs, and "today"
gives the current implementation's verdict where that matters. 2x-P6 numbers the cases (PD4) and adds the plan row's
cases (33a–33e, 36a–36h, the 5e1a449cc7bf family, Choose/Branch/StoredPair) as listed there.

| Id | Program (sketch) | Expected | Decides |
| --- | --- | --- | --- |
| c1 | `moved` (S2 §3) | valid | Move carry; A3 rejects |
| c2 | `var h = (it@move, 1)`; `first = h.0.next(); var g = h@move; second = g.0.next()` | valid | Carry through an ancestor |
| c3 | `first = it.next(); var w = Wrap(it: it@move); second = w.it.next()` | valid if the rename follows the aggregate operand (H); otherwise a loss [valid] | Rename domain |
| c4 | `first = it.next()`; `b` takes `it@move` on one path and `other@move` on the other; `b.next()` | error | Must-fact at joins |
| c5 | c1 with `it`'s storage ending while `first` lives | valid | E8 against a Place phantom (§2) |
| c6 | `first = it.next(); var j = id(it@move); j.next()` | error [valid] | Loss 1 |
| c7 | takeAcross with `swap(source, other)` | error at the second take only | Exclusive-Borrow invalidation; location |
| c8 | takeAcross with `source@follow = fresh@move` (33b) | error at the write and at the second take | Destruction reach plus invalidation |
| c9 | 33e (`source.takeWith(do { source@follow = fresh@move; 0 })`) | error | Invalidation inside c2's argument |
| c10 | `first = it.next(); look(it@ref); second = it.next()`, `look` concrete | valid | Shared Borrows do not invalidate; Place phantom rejects |
| c11 | 15.6.3:574's `takeFirst` with `S is StableTake`: `var r = h@follow@uniq; first = r.0.take(); second = r.0.take(); return (first, second)`; the same with `h.1 = 5` between | valid; error at the write (P12) | P12's Loans outside the tag (§2) |
| c12 | `first = it.next(); touch(it@uniq); second = it.next()`, `touch` concrete | error [undecidable today: `Unsupported_Kd`] | Loss 2's only verdict change |
| c13 | Take-only `takeTwo` (8.11.1:704; `0c1b007e8702`) | error at the second take | Rule 1 by Φ |
| c14 | `tupled` (`583395f779cc`) | error | Φ inside an aggregate |
| c15 | `through` (a user generic helper calling `take`; `59e2289ce4f4`'s family) | 2x-S1's reading (error today) | Concrete calls and Φ |
| c16 | 33d (`source.other()`, another requirement) and `626adb54494d` | error | Rule 3 covers one requirement |
| c17 | Callables f and g of one F (`5e1a449cc7bf`; `ReplacingTheCallable…` row 3) | error at g | Φ from a retained argument |
| c18 | R1g: `next(source)` twice with the bound; `f = two@move` between; without the bound (`e1c1b93d054c`) | valid; error; error (today: `Unsupported_Kd`, `Unsupported_Kd`, error) | Callable Place tags |
| c19 | `zip<I>(left, right)`, `both<S>(left, right)` (`EffectBoundCallerTest`) | error by the letter (today: valid) | G2 |
| c20 | `pairs` (nested `for` over one `ref/C`) | valid | `ref` modes of carriers |
| c21 | `takeLate`, `sumTwo` | valid | No live earlier result; shared reach |
| c22 | `first = it.next(); n = it.count()`, `count` a `ref`-receiver requirement | error | Shared reach against a `uniq` Φ |
| c23 | `first = it.next(); swap(it, other); return first` | valid | Invalidation alone is no error |
| c24 | `a = i.next(); b = c.take()`, `i: uniq/I`, `c: uniq/C`, `C ≠ I` unrelated; also `g(5)` through `g: ref/G`, `G is Callable<(i32) -> i32>`, while `a` lives | valid by atom identity (H) | G2's second half; reach needs a carrier atom |
| c25 | An abstract call whose store row writes a value retaining the receiver's content into `slot`, then another abstract call on the receiver; the same with the second call a preserves call on the same Place | error; located `Unsupported_Kd` until 2x-S1 (PD16) | G113 stores through W; stored Φ |
| c26 | `a = h.left.next(); b = h.right.next()`, `h` a local `Pair<J>` | error by the letter | G2 (Merge's caller shape) |
| c27 | A virtual `preserves results` slot on an open struct with an Origin: twice on one handle Place; through a Reborrow; after a handle Move | valid; error [valid]; valid | Slots (§2.3; loss 3) |
| c28 | The 15 shapes of §6 | conform | §5.3, §5.4 |
| c29 | Drain (8.11.5), `Nested` (`self.wrapper.iterator.next()`), `Lent` (`uniq/J during source`) | conform | Delegated tags |
| c30 | Merge (8.11.5) | violation at `self.left.next()` | Rule 1 in a conformance |
| c31 | Choose, Branch, and `Hold`'s rows except the third (an abstract item destroyed on some path, another returned) | violation | §5.5 |
| c32 | The 8 rows of §5.5 that return only `.None` (StoredPair, NumberedOnce, Pick, `Hold` row 3) | G1 | §5.5 |
| c33 | `Plain` (Item `i32`, `_ = self.inner.next()`) | conforms | §5.5 |
| c34 | S2 §2.3's `Cursor` with a stored `value: uniq/i32 during source` returning `.Some(self.value)`; `Wrap` and `Twice` over it | one containment error at the return (2a), no second problem; no violation of their own (H) | §5.4 merge; S2's H2 |
| c35 | `Swapped` (a swap of `self.inner` and `self.spare` before the delegated call); `Topped` (a result from the argument `spare`) | violation; violation | "Anywhere"; untagged argument Loans |
| c36 | Drain through a concrete generic helper (`return Helpers.pull(self.inner@uniq)`) | conforms (S2 §2.2); a violation if 2x-S1 composes helpers | §5.3 with 2x-S1 |
| c36b | An explicit specialization of an implementation; Function Item and concrete Closure evidence for a Callable bound | checked as bodies | S2 §2.2's scope (C2 for Closures) |
| c36c | `next` calls a non-confined Callable stored in `self` while delegating to `self.inner.next()` | conforms under G1 (ii); violation under (i), (iii) | G1 |
| c37 | `var it = M.make(G.arr@ref); first = it.next()`, non-confined | error at the first call | §3 vacuity |
| c38 | c37 with a `confined` and `preserves results` requirement, called twice | valid | §3 |
| c39 | A `confined` preserves implementation writing a mutable static | A4's `confined` error only | §3 |
| c40 | A static-rooted receiver of abstract Type with a `confined` StableTake, taken twice | valid | Static-rooted relation |
| c41 | c40 with a call between whose keys write the static | error at the second take | A4-key invalidation |
| c42 | c40 with a call between that only reads it | valid | Reads do not invalidate |
| c43 | `it.consume(it.peek())`, `peek` a `ref`-receiver requirement with a descending result, `consume` a `uniq`-receiver one | error at `consume` | R4-M68's condition |
| c44 | Φ from `w.r.peek()` with `r: ref/S`, then an observing destruction of `w` while the result lives | valid | E3's residue (Place phantom) |
| c45 | 33a–33e, 36a–36h rerun | SPEC's verdicts unchanged; 33c moot under carrier reach | Earlier cases unchanged |

## 9. Owner questions and SPEC gaps

**G1. What the conformance compares** (SPEC 8.11.3:774, "the Loans its results may keep", with 8.11.2:721–722, "a conflict
under either bound"). The three options give these verdicts on c31–c33 and c36c.

- **(i) The declared result Type.** An unclassifiable effect violates the bound when the declared result may hold a Loan,
  except a delegated call's. This keeps today's verdicts and adds a Type predicate beside the body check.
- **(ii) S2's by-body earlier result with carrier reach.** A destruction or call violates the bound only when it reaches a
  Loan or Φ that the earlier result keeps. The 8 rows of §5.5 become valid, and c36c conforms.
- **(iii) Unconditional.** Every unclassifiable effect violates the bound except a delegated call's, which rejects `Plain`.

Library 0, milestones 0 (§6). **Recommendation: (ii).** It is S2's text and the ordinary body check, with no second
concept. 2f-S2 integrates it, and the 8 rows change at 2f-7. Until then those forms report a located `Unsupported_Kd`, so
neither an acceptance nor a rejection runs ahead of the SPEC text (PD30's interim, applied in the accepting direction).

**G2. Rule 1 across values of one abstract Type** (SPEC 8.11.4:789, "Origins may denote").

- **(a) Atoms of the Type.** One Type's values share atoms, so `zip`, `both` and c26 err; distinct Types need a relation the
  body proves (c24 valid). This is consistent with 8.11.5's `Merge`.
- **(b) Per value in callers, per Type in conformances.** This is today's implementation: two rules for one sentence.
- **(c) A SPEC revision to per-value reach,** with `Merge`'s example rewritten.

Library 0, milestones 0: the only generic iterator code is the adapters, which call once per body. Tests: 2
`EffectBoundCallerTest` rows change from valid to error. **Recommendation: (a)**, the text and its example. The two rows'
new expectation needs the owner's decision recorded in their commit (AGENTS Verification).

**Pointers, no new question.** 2x-S1's readings of `59e2289ce4f4` (c15, c36) and of the stored Φ (c25) decide whether a
concrete call reaches Φ and whether a stored Φ is excused. Deleting S2's statics clause (§3) is a 2f-S2 text change with no
verdict change. The F6 audit's ledger note on Choose, Branch and StoredPair is corrected in §5.5: 2f-6 classifies those
rows as 2f-5's rejections, not as old-path errors.

## 10. Not verified

Nothing was run. The conformance and loss readings (§6, §7) and the carrier modes are (H) until 2x-P6 models them with
derived rules. S2 §2.3's H2 rests on the ordinary body check, containment (15.6.3:528) and the raw-split obligations, and
stays argued until 2x-P6 runs it. Test counts come from line scans; accessor and constructor forms may be missed, as S2 §7
notes.
