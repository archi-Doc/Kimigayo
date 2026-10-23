# A5 step 2, unit 2f-S1: the S2 revision sketch

Date: 2026-10-19, revised the same day after its independent review. Base: `dev` `30fe97e1`; line references rechecked at
`d55bce08` (`docs/spec` unchanged; DIAGNOSTICS's requirement-call row moved from :157 to :158). "SPEC 8.11.4:789" is line
789 of `docs/spec/08-…md`. Status: closed by Abstract Calls Reach by Paths (DECISIONS R4-M106); it was the working record for round 2x-P6, design 2f-D
and the owner's OQ3 (DECISIONS R4-M74);
SPEC and DECISIONS decide. It changes no SPEC, compiler, library or test. The proposal
`draft/Obsolete/2026-10-16 Preserves Results by Receiver Place.md` ("S2", base `de80a4c0`) stays untouched until 2f-S2
revises it from this sketch.

Inputs:
- S2; design A3 (`../A5 Step 0/design-A3.md`) §5 and §12.
- The A5-F6 audit (`artifacts/handoff/2026-10-18-pause/r4-scratch/step2-audit/F6/audit.md`, "F6 audit") and PLAN G113.
- The step-2 plan (`draft/Proposals/2026-10-18 A5 Step 2 Implementation Plan.md`): PD7, PD16, PD23, PD30 and rows 2x-P6, 2f-D.
- R4-M68, which is 1c-5's entry and not yet on dev; R4-M75 cites it: an activation meets only Loans shared in the holder
  where it finds them, with two conditions for 2f.
- The 35 `a4-preserves` ledger sources (`a5-1b5-b1`) and the review's probes. A probe verdict is labelled "today": it is
  `check` on the default pipeline (`Kimi.dll` of `a5-1c` `70493c67`), not A5's shadow.

Conventions: nothing was built or run, so every other verdict is a reading, and (H) marks an unverified inference. Φ is the
phantom that stands for Loans an abstract call's result may keep (A3's `Φ_c`). Cases `c1`… are in §8; G1–G4 are in §9.

## 1. Rebase: S2's references at dev

| S2 site (at `de80a4c0`) | At dev | For the revision |
| --- | --- | --- |
| 8.11.3 :774; 8.11.4 :785, :791; 8.11.5 :816; 8.11.6 :843, :845, :849, :852; 6.4.6 :349; 22.2.3 :135 | Same lines, same text | S2 §2.4 applies, except its Conformance paragraph's statics clause (§3) |
| 15.6.3 :517; App. E :87 | :582 and :88, same text | Applies; 15.6.3:570 (P12) and :528 (E8) are new (§2) |
| §1's "§15.6.4 summary (line 531)" | 15.6.4:596 | Problem text only |
| Loan Identity by Base §2, §2.1 | SPEC 3.4.1, 15.6.2:500, 15.6:450 (R4-M39); its base superseded by E8 (R4-M52) | Cite SPEC; S2 uses no base |
| STATUS line 158 (containment) | SPEC 15.6.3:528; A5 unit 2a (A3 E4) | Cite SPEC |
| PLAN line 38 (R1g); `CallableEffectBoundTest` 134–149, 396–407 | PLAN:39; 134–150, 397–408 | — |
| Library, milestone and test lines; §7's usage (19 files, 59 lines, 70 occurrences) | Unchanged except `Core.kimi:327, :360`; 21, 61, 71 (added `MirBorrowsTest.Prototype.cs:10`, `spec-cases.json:27`) | — |

## 2. SPEC 15.6.3 with P12 and E8

1. **:582.** S2's text ("on the same receiver Place") replaces it as written.
2. **:570, P12's sentence** stays. In a caller, P12's Loans that have a path are ordinary Loans. They never meet the later
   call of r on X: they lie on other roots (15.6.2:505; `takeFirst`, :574–579) and carry no mutable-static Loan (§3). They
   still meet other accesses (`h.1 = 5`, c11). So they need no tag (H). P12's path-less Loans, and every Loan of a
   delegated result in a conformance, carry the call's tag (§4).
3. **A dynamic virtual call is not a P12 call** (INTEGRATED 2026-10-17), but 6.4.6:347 gives it rules 1–3, so it needs Φ
   without P12's Loans (c27).
4. **E8 (:528).** Φ has no access path, so no in-place destruction ends it, as :570 requires ("Destroying the Iterator or
   remainder does not end published child Loans"). A Place phantom (A3 §5) is ended by E8 when the receiver's Move Path
   is destroyed in place. Today an observing destruction's check comes first (33b), and P12's parent Loans catch a
   replaced holder (36g). A Place phantom that carries Moves must also move its Place, or the moved-from receiver's
   storage end ends it (c5).
5. An assignment or destruction of X invalidates the relation (§5.2) and, by E8, ends the Loans X prefixes. Φ stays; only
   its tag drops.

## 3. Statics under P6

**S2 §2.2's refinement is vacuous (H) and is deleted.** It compares a conformance body's static effects with the Loans of the
receiver's Origins, which "may be anchored in any mutable static". A valid caller never passes such a Loan where it
matters:

- a body's mutable-static Borrows never leave it (15.6.4:602);
- an input carrying a mutable-static Loan makes a call with unknown environment effects an error at that call
  (15.6.4:599; 15.6.7:689, "every reservation must be able to become live at once, with … effects on static storage";
  A3 E13 (1)(b)), so a valid first call's result keeps no such Loan;
- a `confined` body has no mutable-static effect (8.11.2:718).

"Anchored" also left SPEC with P6. Cases c37–c39.

**S2 §2.1's static-rooted invalidation is kept.** A call between the two calls invalidates X, rooted at a static key, when its
summary may write, exclusively borrow, replace or destroy that static, or its environment effects are unknown. The rule
is not vacuous for `confined` requirements; with a non-confined one the first call fails as above. It reads A4's keys
(A5-F5, a prerequisite of 2f-4). Cases c40–c42.

## 4. Rule 1 and the earlier result

SPEC 8.11.4:789 is Origin-based: "an abstract input may reach every Loan its Origins may denote". 8.11.5's `Merge` errs only
through it, as does 8.11.1's Take-only `takeTwo` (:704). In a generic body those Loans have no path (15.6:450). A5's
`Φ_E` never conflicts, and A3's `desc(c)` adds Φ only for descent from the receiver. So MIR today accepts the ledger's
rule-1 rows (`0c1b007e8702`, `145e6068e0a6`, `583395f779cc`, `5e1a449cc7bf`, `626adb54494d`, `e1c1b93d054c`; H, by the F6
audit §4's reading).

### 4.1 In a caller: carrier reach (PD16's first option)

- **Issue.** An abstract call c issues a phantom Φ(c,i) into its result for each input i that a retention row ties to the
  result. Through G148's W (rule G) it also issues Φ(c,i) into storage the call writes (G113). An abstract call is a
  requirement call without a known concrete callee, a call through a Callable-constrained value, or a dynamic virtual
  call. Φ(c,i) replaces `Φ_c` in `desc(c)`, and P12's Loans stay. A retained argument gets one too (Callables f and g, c17).
- **Carriers.** Φ's carriers are the Origin atoms of i's Type after normalization: Type parameters, signature Origins and
  hidden atoms.
  - (Proposed; G3.) A projection's atoms are its base's: an associated Type's Origins come from its base's binding, so
    `J`'s Origins may denote a `J.Item`'s Loans (Keep, §4.2).
  - (Proposed; G3.) An equality between projections (`K.Elem is I.Item`) identifies their atoms.
  - Distinct Type parameters share no atom unless the body relates them; this is a reading (G2).
  - Each atom has a mode: `ref` when its slot's Loan requirement is `ref` (15.3.5) or it is reached past a shared layer
    (8.11.4:789), else i's access mode. For the receiver this is R4-M68's condition.
- **Tag.** The tag is `(r, X)` when r has `preserves results` and X is c's receiver Place without a non-path index; for a
  Callable, X is the callable's Place (S2's 8.11.6 text). It sits on Φ and on c's path-less Loans, and is `none`
  otherwise.
- **No access path.** No read, write, Move or Borrow reaches Φ. E8 never ends it. No reservation meets it.
- **Reach.** An abstract call c2 reaches Φ when an input of c2 has one of Φ's carrier atoms in conflicting modes
  (15.6.2:517).
  - **Own inputs (proposed; G3).** A Φ carried by input `o_j`, or flowing from `o_j` into c2's result or stores, is
    reached only through another input: a value's Loans cover its aliases (as 15.6.2:505), and an activation skips its
    own argument (15.6:455).
  - So chained splitting (c53) and `pred(item@ref)` in a `find` loop (c55) meet nothing, and c43 errs through its
    receiver.
  - `sink.put(item@move)` with `K.Elem is I.Item` reaches the item's Φ through `sink` and errs by the letter (c54; G2, G3).
- **Destructions reach no Φ in a caller** (proposed reading; G4). It covers every abstract destruction in a caller,
  a Take-only receiver's included:
  - A descending result names only Origins of the receiver's Type, so it cannot name the receiver's storage
    (15.6.3:570).
  - 15.6.6:661 reaches by paths, and Φ has none.
  - 16.3.3:196's "unknown for the body's own Loan checks" is then the destruction's environment effects, which meet only
    static Loans (§3).
  - For Iterators, 22.2.3:133 says so directly ("moving or freeing the Iterator invalidates no returned item").
  - The run-time argument is (H), as S2 §2.3's H2: the drop cannot reach an item except through a raw split's obligation
    or a Reborrow that containment forbids.
  - Under this reading `takeOne` and `replaceIt` stay valid (c56, c57).
  - takeAcross with `source@follow = fresh@move` errs only at the second take (c8). If G4 is answered this way, 2f-S2
    also drops S2 §3's remark that this destruction "has its own conflict with first".
- **Parameter Loans stay unreached.** Loans received through parameters are checked where the body is called (15.6:450),
  so `pass` stays valid (c58, today too).
- **Exclusion (rule 3).** Φ is excused at a call of r on X when every copy of it in the check set carries `(r, X)`. This
  is S2 §2.1's must-fact: the tag is part of the element, so a join keeping a `none` copy defeats it.
- **Codes.** Φ met at a call: `CallEffectConflict_Kd` at that call, related to the earlier call (PD7, DIAGNOSTICS:158).
- **Deletes.** E2's derived excuse at c2's reservations, `Φ_c^dist` (the `none` tag) and E3's residue (§5.6).

**Fallback: A3's Place phantom.** Φ sits on i's referent with its content.
- It accepts `Merge` and Callables f and g, which are SPEC errors, so it needs a second rule.
- It rejects `moved`, c3 and c10, and puts takeAcross's error at the `swap`; these are listed losses (OQ8).
- R4-M68's conditions and E3's residue apply to it.

### 4.2 In a conformance (S2 §2.2)

The caller's rule 3 excludes every Loan an earlier result keeps, so the conformance is their only check (S2 §2.3, H2
item 3). The check must therefore reach all of them, path-less ones included:

- **The holder.** One more holder, live from entry to every exit and never written by the body. Its set is the union of
  σ(`_0`) at the returns: the results by body (S2 §2.2; 8.11.3:774, "its results"). One forward pass computes it, so no
  fixed point is needed (H). By site identity a Borrow meets its own earlier execution (15.6:455). A3 §5's alternative,
  `Φ_prev` on `*self`, meets every body that borrows or writes through `self`: every §6 row except the adapters would
  fail (H).
- **Carriers for path-less Loans.** Each Loan the earlier result keeps without a path is an earlier marker. It is distinct
  from the current execution's parameters, whose values are other values. Its atoms, with §4.1's modes, are:
  - for content taken out of `self` (`exchange`, `takeFirst`, a Copy of a stored reference): the Type of the Place it
    was read from if G2 keeps c24 valid, or the receiver's referent Type if G2 makes c24 an error (see "Carrier
    granularity" below);
  - for P12's parent Loans of `self`: `self`'s outer Origin and its parents, not the referent's atoms. These are Loans of
    the receiver reference itself, and no Field-typed input carries them;
  - for an argument's Loans (Keep's `spare`): the parameter's Type.
- **Delegated tags.** Every Loan a delegated result keeps (its Φ, P12's and descent's Loans) carries `(d, w)`
  (8.11.5:816, "a Loan that its result keeps from the result of … `d`"). Without these tags the adapters' delegated call
  would meet the P12 and descent Loans its earlier item keeps, and `Iteration.kimi:32, :59` would fail (O3).
- **Reach.** A rule-1 call (§4.1, own-input rule included) meets every earlier marker or Φ with a carrier atom. It is
  excused only when it is a call of d on w, every copy of the element carries `(d, w)`, and the body neither invalidates
  `(d, w)` nor moves a value on w's path (§5.4). Unclassifiable effects conflict as G1 decides. A call's unknown
  environment effects meet only static Loans (8.11.4:790, 15.6.4:599), which is vacuous by §3.
- **No own-input exemption for the holder.** The own-input rule never exempts the holder's elements: they stand for an
  earlier item's Loans, even where site identity gives them the current item's names.
- **Carrier granularity is G2's c24 answer applied to conformances.** With referent-Type carriers, two unrelated Type
  parameters of one receiver are related in a conformance although c24 relates them in no caller. Granularity therefore
  decides rule-1 verdicts:
  - `Hooked` (c36c: `self.hook()` with `F is Callable<() -> i32>`, then `return self.inner.next()`) and `Tagged`
    (c59: a `confined` `self.p.ping()` with `P is Ping`, then `return exchange(self.item@uniq, with: .None)`) violate
    with referent-Type carriers and conform with per-Place carriers.
  - `Lending` changes under G1 (ii).
  - Both conformances are rejected today.
  - Per-Place carriers keep every B1 case rejected: Keep and `Topped` by the parameter's Type, `Two` and `96f3cb16122d`
    by `Option<T>`, and Peekable by `Option<I.(Iterator).Item>` through the projection rule (proposed; G3). The adapters
    stay valid, because their P12 markers carry `self`'s outer Origin and their descent markers are tagged.
- **Containment.** A Loan through `self` in the earlier result is a containment failure (15.6.3:528), which the Origin
  stratum reports. The conformance problem merges into it (A6, as E12; 2f-D).
  - A delegation through a local Reborrow, `var r = self.inner@uniq; return r.next()`, is a violation. P12's Loan of `r`
    on `self.*.inner` meets the next execution's Borrow at the same site, and the current 8.11.5:816 gives the same
    verdict, since `r` is no Field path (c47).
- **Non-receiver parameter paths.** By site identity, a Loan on `p.*` for a non-receiver parameter `p` (a result that
  reborrows through `p`) meets the next execution's Borrow at that site. Yet the later call's `p` is another value, and a
  caller already rejects passing the same Place while the earlier result keeps it.
  - Proposed: re-root such a Loan as the earlier argument's path-less Loan, with `p`'s Type atoms, so that only rule 1
    reaches it (H; no loss).
  - Alternative: a loss for OQ8.
  - Library 0, milestones 0 (c46).
- **Open 2f-D representation point.** A5's single `Φ_E` marker cannot express any of this. 2f-D has two choices:
  - in the conformance pass, per-entry markers (one per parameter and one for the receiver's content), or a provenance
    pass over σ(`_0`);
  - where the tags sit on markers that the body also carries untagged.

Verdicts under this rule: Keep, `Topped` and a Peekable violate, as they do today: rule 1 reaches their untagged argument
or `head` Loans (c48, c35, c51). `Two` and `96f3cb16122d` violate: the destroyed `T` meets the earlier result's `T`
content (c49, c50). `Merge` violates (c30). Drain, the adapters and the 15 shapes of §6 conform (c29, c52, c28).

## 5. Divergences from A3 §5 and the A5 plan

**5.1 Move carry.**
- S2 §2.1: a Move of X or an ancestor renames the tag to the destination path when that path occurs in the body, and
  drops it otherwise.
- SPEC 22.2.3:135: "A Move of the Iterator carries the bound".
- A3: Moves disturb (§5 `disturbed`), and its Place Φ is hit by the Move itself (deep move), so A3 rejects `moved` (F6
  audit §1.1).
- **Resolution:** S2's carry, a rename of the tag in every holder (`MirLoanSets` Remove+Add, or one operation superseding
  R4-M53's four; 2f-D, after 2x-R10).
- **Cases:** c1–c6.

**5.2 Invalidation.**
- S2: a write, assign, destroy, part Move or exclusive Borrow of X, a part or an ancestor invalidates, as does passing one
  exclusively to a call. A call of r on X does not (its receiver Borrow; 6.4.6:349), nor do shared Borrows and reads. A
  non-path index relates nothing.
- SPEC now: value identity (8.11.3:774).
- A3: writes, Moves or destructions of `D ⊑ recv(c)` and writes through a Place hitting it.
- **Resolution:** S2's list; its one exception, c2's receiver Borrow, subsumes E2's row. Under carrier reach the only
  effect of an intervening exclusive Borrow is the dropped tag, so takeAcross errs at the second call (S2 §3).
- **Cases:** c7–c12, c23.

**5.3 Calls inside a conformance.**
- S2 §2.2: a concrete call contributes 15.6.4's argument Borrows, retention and summary; an abstract call contributes rules
  1–3; callee bodies contribute no Place effects.
- A5P's S2 row (A5P:481, :525–528), design A (`design-A.md:231-233`) and `audit-calls.md:164-169`: declared effects only.
- SPEC 8.11.4:785 checks bounds against completed transitive summaries.
- **Resolution:** S2's (its §8). It is A5's ordinary call rule, and :785's summaries then serve `confined` alone.
- **Cases:** c28, c36.

**5.4 Delegated tags.**
- S2 §2.2: excluded at the body's calls of d on w, unless the body moves a value on w's path or invalidates w for d
  anywhere.
- SPEC 8.11.5:816: "replaces, swaps or moves a value on the path to `w`". A3: "undisturbed since entry".
- **Resolution:** S2's. Its invalidation list covers replace and swap. "Anywhere" holds because the earlier result comes
  from an earlier execution, and the tag covers every Loan of the delegated result (§4.2).
- **Cases:** c29, c35, c52.

**5.5 Unclassifiable effects.**
- S2 §2.2: "An effect that cannot be classified (§8.11.2) conflicts, as today". 8.11.2:721–722: "a conflict under either
  bound".
- How the conflict is read is G1 (§9).
- **The F6 audit's (H) is refuted.** It says Choose (`0bd427e70ef6`), Branch (`2ac7e65b4d02`) and StoredPair
  (`293217626cfd`) conform under S2 as writes of disjoint Fields.
  - Choose and Branch destroy an abstract item while their earlier result keeps a delegated Φ. They violate under every
    G1 option, and 2f-5 reports them.
  - StoredPair's verdict follows G1. StoredPair is no delegation: it takes its item out of `self.pending`.
  - The disjoint-Field writes invalidate nothing.

**5.6 Reservation windows (R4-M68), E2 and E3.**
- **R4-M68's conditions:** "a Φ_c met at a receiver's formation is located at its later call; a shared-receiver Φ_c takes
  the receiver's mode, or 2f reruns this rule's prototype variant with that case".
- **Under carrier reach:** Φ meets no formation or activation, so the first condition is vacuous. The second is §4.1's
  mode rule.
- **Under a Place phantom:** a `uniq` Φ issued inside a reservation window is missed by both the formation and the
  activation, which is unsound (c43).
- **E3's residue** (re-add the shared cut if Φ lies past a shared Deref) is vacuous under carrier reach (c44).

## 6. The 12 + 3 conformances at dev

Every row conforms under §4.2; no rule-1 call meets an untagged element, and no body destroys an abstract value.

| Conformance | `next` | Earlier result (§4.2) | Effects |
| --- | --- | --- | --- |
| ArrayIterator, DictionaryIterator (`Array.kimi:274`, `Dictionary.kimi:96`) | `Storage.splitFirst(self.state@uniq)` (`Storage.kimi:110`, `:126`) | Content retention of `state`: an untagged receiver-content marker; no Φ | Argument Borrow of `self.*.state`; no rule-1 call; no static |
| ArrayUniqIterator, DictionaryUniqIterator (`Array.kimi:284`, `Dictionary.kimi:106`) | Same (`Storage.kimi:118`, `:136`) | Same | Same |
| ArrayOwningIterator, FixedArrayOwningIterator, DictionaryOwningIterator (`Array.kimi:293`, `:304`, `Dictionary.kimi:115`) | `Storage.takeFirst(self.state@uniq)` (`Storage.kimi:102`, `:174`, `:145`) | Receiver-content marker (element Loans) | Argument Borrow; a raw Take; nothing destroyed |
| RangeIterator, ClosedRangeIterator (`Core.kimi:327`, `:360`) | Copy reads and writes through `self` | Nothing (`T is PrimitiveInteger`) | Copy writes |
| SliceIterator (`Slice.kimi:89`) | `self.values[index]@ref/T` | No Loan past the Slice's shared Deref (A3 E3, E6); the Slice's receiver-content marker | Shared reads; a Copy write |
| `Iteration.OwningIterator<I>` when `I is Iterator` (`Iteration.kimi:15`, `:25`) | `self.iterator.next()` (`:32`) | Φ, P12's and descent's markers, all tagged `(next, self.*.iterator)` | The delegated call: excused; its environment effects meet nothing (§4.2) |
| `Iteration.BorrowingIterator<I>` (`Iteration.kimi:42`, `:52`) | `self.iterator.next()` (`:59`) | Same, tagged `(next, self.*.iterator.*)` | Same |
| `Cursor<T>` (`Milestone13.kimi:6`, `Milestone29.kimi:45`) | SliceIterator's shape | Same as SliceIterator | Same |
| `Drain<T>` (`Milestone29.kimi:13`) | `exchange(self.first@uniq, with: .None)` by position | Receiver-content marker | Exclusive Borrows of `self.*.first`…; `exchange` destroys nothing |

The adapters are checked under their condition (8.11.4:785). The library's only generic iterator code is the adapters
(`Iteration.kimi:6, 11, 16, 43`); its only other abstract calls are the Callables of `Array.kimi:205` and `:253`, which
return `bool` and `i32` and issue no Φ. The milestones have none.

## 7. Losses and verdict changes

S2 §5's losses, recounted at dev. Library and milestone counts come from the greps of §6. Test counts come from S2 §7's
scans, rerun on the joined C# strings (scratch script, untracked).

| # | Form | Current SPEC → S2 | lib / ms / tests | Until 2f-S2 (PD30) |
| --- | --- | --- | --- | --- |
| 1 | The same value through Places that share no path: a Borrow of the receiver, joins, returned receivers (c6), clones, reloaded elements; `h.inner.take()` after `r.inner.take()` with `r = h@uniq` | valid → error | 0 / 0 / 0 | Located `Unsupported_Kd` |
| 2 | Other operations on X between the calls: only an exclusive Borrow passed to a concrete call changes a verdict (c12); another requirement call or a whole write already errs (H) | undecidable (R1g) → error | 0 / 0 / 2 concrete-caller rows (`CallReservationTest:218`, `EffectBoundImplementationTest:193`), unchanged | `Unsupported_Kd` |
| 3 | A slot call through a Reborrow of the handle | valid → error | 0 / 0 / 0 | `Unsupported_Kd` |
| 4 | A receiver value moved by `exchange` or `swap` | valid → error | 0 / 0 / 0 | `Unsupported_Kd` |
| 5 | A non-path index (`arr[i].next()` twice) | valid → error | 0 / 0 / 0 | `Unsupported_Kd` |

These verdict changes come from other rules; none is an S2 loss:

- **G1 (ii′):** the 8 rows of §9 become valid.
- **G2 (a), rule 1's letter:** `zip`, `both` and c26 become errors; so does `sink` (c54), valid today.
- **G2's c24-valid answer with per-Place carriers:** `Hooked` (c36c, under G1 (ii) or (ii′)) and `tagged2` (c59) become
  valid. Both are rejected today.
- **G4's proposed reading:** 33b (c8) loses its error at the replacement, which reverses prototype round 9's verdict
  ("33b errs at the replacement, the second reservation and the second call"); the program stays an error.
- **G4's other reading:** `takeOne`, `replaceIt` and c8's write become errors, valid today. Library 0, milestones 0 (H).
  Tests: at least the native fixture `UserIterationTest.GenericLoopTransfersItsProjectedItemIntoTheResult`
  (`for item in batch@move => return .Some(item@move)`), found by a grep for generic owned iteration.
- **§4.2's alternative for non-receiver parameter paths** would be a loss (c46).

Every test S2 §5 lists as "not a loss" exists at dev; those tests change with containment (2a), not with S2.

## 8. Cases for round 2x-P6

Each verdict is SPEC's as S2 revises it (OQ3). Brackets give the current SPEC's verdict where it differs; "today" gives
the probe or implementation verdict where it matters. 2x-P6 numbers the cases (PD4). It adds the plan row's cases
(33a–33e, 36a–36h, the `5e1a449cc7bf` family, Choose/Branch/StoredPair) and every row of §9's table.

| Id | Program (sketch) | Expected | Decides |
| --- | --- | --- | --- |
| c1 | `moved` (S2 §3) | valid | Move carry; A3 rejects |
| c2 | `var h = (it@move, 1)`; `first = h.0.next(); var g = h@move; second = g.0.next()` | valid | Carry through an ancestor |
| c3 | `first = it.next(); var w = Wrap(it: it@move); second = w.it.next()` | valid if the rename follows the aggregate (H); else a loss [valid] | Rename domain |
| c4 | `b` takes `it@move` on one path and `other@move` on the other after `first = it.next()`; `b.next()` | error | Must-fact at joins |
| c5 | c1 with `it`'s storage ending while `first` lives | valid | E8 against a Place phantom |
| c6 | `first = it.next(); var j = id(it@move); j.next()` | error [valid] | Loss 1 |
| c7 | takeAcross with `swap(source, other)` | error at the second take only | Exclusive-Borrow invalidation |
| c8 | takeAcross with `source@follow = fresh@move` (33b) | under G4's proposed reading: error at the second take only (today: valid; prototype round 9: also at the replacement); G4's other reading keeps the write's error | Destruction reach (G4) |
| c9 | 33e (`source.takeWith(do { source@follow = fresh@move; 0 })`) | error | Invalidation inside c2's argument |
| c10 | `first = it.next(); look(it@ref); second = it.next()`, `look` concrete | valid | Shared Borrows do not invalidate |
| c11 | 15.6.3:574's `takeFirst` with StableTake, `r.0.take()` twice; the same with `h.1 = 5` between | valid; error at the write (P12) | P12's Loans untagged (§2) |
| c12 | `first = it.next(); touch(it@uniq); second = it.next()`, `touch` concrete | error [undecidable: `Unsupported_Kd`] | Loss 2 |
| c13 | Take-only `takeTwo` (8.11.1:704; `0c1b007e8702`) | error at the second take | Rule 1 by Φ |
| c14 | `tupled` (`583395f779cc`) | error | Φ inside an aggregate |
| c15 | `through` (`59e2289ce4f4`'s family) | 2x-S1's reading (today: error) | Concrete calls and Φ |
| c16 | 33d (`source.other()`) and `626adb54494d` | error | Rule 3 covers one requirement |
| c17 | Callables f and g of one F (`5e1a449cc7bf`) | error at g | Φ from a retained argument |
| c18 | R1g: `next(source)` twice with the bound; `f = two@move` between; without the bound | valid; error; error (today: `Unsupported_Kd`, `Unsupported_Kd`, error) | Callable Place tags |
| c19 | `zip<I>(left, right)`, `both<S>(left, right)` | error by the letter (today: valid) | G2 |
| c20 | `pairs` (nested `for` over one `ref/C`) | valid | `ref` modes |
| c21 | `takeLate`, `sumTwo` | valid | No live earlier result; shared reach |
| c22 | `first = it.next(); n = it.count()`, `count` a `ref`-receiver requirement | error | Shared reach against a `uniq` Φ |
| c23 | `first = it.next(); swap(it, other); return first` | valid | Invalidation alone is no error |
| c24 | `a = i.next(); b = c.take()` with `C ≠ I` unrelated; `g(5)` through a Callable `G` while `a` lives | valid by a reading (G2) | Distinct Type parameters |
| c25 | An abstract call storing a value that retains the receiver's content, then another abstract call on the receiver; then a preserves call on the same Place | error; `Unsupported_Kd` until 2x-S1 (PD16) | Stores through W; stored Φ |
| c26 | `a = h.left.next(); b = h.right.next()`, `h` a local `Pair<J>` | error by the letter | G2 |
| c27 | A virtual `preserves results` slot with an Origin: twice on one handle Place; through a Reborrow; after a handle Move | valid; error [valid]; valid | Slots; loss 3 |
| c28 | The 15 shapes of §6 | conform | §4.2 |
| c29 | Drain (8.11.5), `Nested`, `Lent` | conform | Delegated tags |
| c30 | Merge (8.11.5) | violation at `self.left.next()` | Rule 1 in a conformance |
| c31 | Class D of §9 (22 rows) | violation | G1, every option |
| c32 | Class N of §9 (8 rows) | G1 | G1 |
| c33 | `Plain` (Item `i32`, `_ = self.inner.next()`), `Lending` | conforms; violation (under (ii), by carrier granularity) | G1; §4.2 |
| c34 | S2 §2.3's `Cursor` with a stored `uniq` returning `.Some(self.value)`; `Wrap`, `Twice` over it | one containment error (2a); no violation of their own (H) | Containment merge; H2 |
| c35 | `Swapped`; `Topped` (a result from the argument `spare`) | violation; violation | "Anywhere"; argument carriers |
| c36 | Drain through a concrete generic helper (`return Helpers.pull(self.inner@uniq)`) | conforms; a violation if 2x-S1 composes helpers | §5.3 with 2x-S1 |
| c36b | An explicit specialization; Function Item and concrete Closure evidence | checked as bodies | S2 §2.2's scope |
| c36c | `Hooked`: `let n = self.hook()` (`F is Callable<() -> i32>`), then `return self.inner.next()` | today: violation; conforms under (ii), (ii′) only with per-Place carriers and c24 valid, and violates otherwise | G1; G2 (granularity) |
| c37 | `var it = M.make(G.arr@ref); first = it.next()`, non-confined | error at the first call | §3 |
| c38 | c37 with a `confined` preserves requirement, twice | valid | §3 |
| c39 | A `confined` preserves implementation writing a mutable static | A4's `confined` error only | §3 |
| c40 | A static-rooted receiver of abstract Type, `confined` StableTake, taken twice | valid | Static-rooted relation |
| c41 | c40 with a call between whose keys write the static | error at the second take | A4-key invalidation |
| c42 | c40 with a call between that only reads it | valid | Reads do not invalidate |
| c43 | `it.consume(it.peek())`, `peek` a `ref`-receiver requirement with a descending result | error at `consume` | R4-M68's condition; own-input rule |
| c44 | Φ from `w.r.peek()` with `r: ref/S`, then an observing destruction of `w` | valid | E3's residue |
| c45 | 33a–33e, 36a–36h rerun | SPEC verdicts unchanged, except 33b (c8): under G4's proposed reading it loses its error at the replacement; 33c moot | Earlier cases |
| c46 | A preserves requirement whose result reborrows through a `uniq` non-receiver parameter `p` | conforms if re-rooted (§4.2); else a violation (a loss) | Non-receiver paths |
| c47 | `var r = self.inner@uniq; return r.next()` | violation [violation] | Local Reborrow in a delegation |
| c48 | Keep (`.Some(s) => return .Some(s@move)`, else `return self.inner.next()`) | violation (today: violation) | Argument carriers (8.11.5:816) |
| c49 | `Two<T>` (two `exchange`s, one returned) | violation (today: violation) | Receiver-content carriers |
| c50 | `96f3cb16122d` (a guard's `return .None` destroys a taken `T`) | violation | Same |
| c51 | Peekable (`exchange(self.head@uniq, …)` returned, else `self.inner.next()`) | violation (today: violation) | Untagged receiver content met by rule 1 |
| c52 | The adapters under §4.2 | conform | Every Loan of a delegated result tagged |
| c53 | `var a = s.splitOff(); let b = a.splitOff()` | valid (today: valid) | Own-input rule |
| c54 | `sink.put(item@move)` with `K.Elem is I.Item` | error by the letter (today: valid) | G2, G3 |
| c55 | `find`: `if pred(item@ref) => return .Some(item@move)` in a loop | valid (today: valid) | Own-input rule |
| c56 | `takeOne`: `var it = iterator@move; return it.next()` | valid (today: valid); G4's other reading: error | G4 |
| c57 | `replaceIt`: `first = it.next(); it = other@move; return first` | valid (today: valid); G4's other reading: error | G4 |
| c58 | `pass`: `keep = other@move; a = left.next(); return (keep, a)` | valid (today: valid) | Parameter Loans unreached in callers |
| c59 | `Tagged` (probe `tagged2`): `let n = self.p.ping()` with a `confined` `ping` on `P is Ping`, then `return exchange(self.item@uniq, with: .None)`; and its non-confined twin (probe `tagged`) | today: both violate. `tagged2` conforms only with per-Place carriers and c24 valid; `tagged` also needs (ii) or (ii′), where its unknown-effect call is vacuous; otherwise both violate | G2 (granularity); G1 |

## 9. Owner questions and SPEC gaps

### G1. What a conformance's unclassifiable effects conflict with (SPEC 8.11.3:774, 8.11.2:721–722)

Two choices:

- **Choice A, the earlier result:**
  - A1, by body: S2 §2.2; 8.11.3:774's "its results".
  - A2, by declared Type: today, `VirtualEffectObligationTest.PreservesResultsComparesLoansRetainedByTheDeclaredResult`.
- **Choice B, how an unclassifiable destruction conflicts** (8.11.2:721, in the body or in a concrete callee's summary):
  - B1, with every Loan the earlier result keeps;
  - B2, only with those it reaches by atoms;
  - B3, unconditionally, except a delegated call's.
- A call's unknown environment effects meet only static Loans (8.11.4:790; 15.6.4:599), which is vacuous by §3.

The options are (i) A2 + B1, today's verdicts; (ii) A1 + B2; (ii′) A1 + B1; (iii) B3. Over the 35 `a4-preserves` rows:

| Class | Rows | (i) | (ii) | (ii′) | (iii) |
| --- | --- | --- | --- | --- | --- |
| D: an abstract item destroyed while the earlier result keeps a delegated Φ | 22: `6440a7d24123`, `322dc8b64640`, `ffa62e9a4a05`, `ddb84d765289`, `c761a3ede3c1`, `58c7ff654864`, `7b2853516b27`, `0bd427e70ef6`, `5f5b9e82ef48`, `b296145f75c2`, `2ac7e65b4d02`, `d3c40649e93c`, `7c953b234d3c`, `86a88b2bde36`, `f4a1b558d34f`, `60e4cbbd0407`, `a7d78e650c97`, `934a55b98125`, `d254209a7214`, `8a3c1758b413`, `d358838ecb7b`, `75ab41dec9ab` | V | V (projection atoms, §4.1) | V | V |
| T: a `T` taken out of `self` destroyed while another is returned | 1: `96f3cb16122d` | V | V (§4.2's carriers) | V | V |
| C: the destruction is in a concrete callee (Helper, Entries) | 3: `8e27d1c3bf12`, `4391d975783a`, `5795bde275be` | V | undetermined (callee summaries carry no atoms) | V | V |
| N: no return keeps a Loan | 8: `293217626cfd`, `9e3eebb7dad4`, `de480c244515`, `5cb6ada766a6`, `a32e2a134524`, `31958147e78a`, `4d840fc71ba2`, `446e364d4ad6` | V | conforms | conforms | V |
| K: containment (`return .Some(self.value)`) | 1: `3508c37bbfe8` | containment (2a) | same | same | same |

Outside the 35 rows:

- `Plain` conforms except under (iii).
- `Lending` (`EffectBoundImplementationTest`) keeps `a`'s Loans in its earlier result while it destroys a `J` item. It
  violates under (i), (ii′) and (iii). Under (ii) it violates with §4.2's receiver-referent carriers and conforms with
  per-Place carriers.
- `Hooked` (c36c) and `tagged` (c59) are rejected today, because today's rule counts a call with unknown effects as
  unclassifiable. They stay rejected under (i) and (iii). Under (ii) and (ii′) that call's environment effects are
  vacuous, so their verdicts follow G2's carrier granularity.

**Changes against today, as G1 statements:** (i) none; (ii) the 8 rows, with 3 rows undetermined, plus `Lending` under
per-Place carriers; (ii′) exactly the 8 rows; (iii) `Plain`. Library 0, milestones 0 under every option (§6).

**G2-side changes:** with per-Place carriers and c24 valid, c36c (under (ii) or (ii′)) and `tagged2` (c59) also become
valid.

**Recommendation: (ii′).** It reads S2's text literally: the by-body earlier result, and "conflicts, as today". It needs no
atom reach or concrete-call composition, so it is independent of G2, G3 and 2x-S1, and it keeps every other current
rejection.

**Until 2f-S2:** if the owner answers G1 as a reading of 8.11.3:774, no interim is needed, and 2f-5 implements it. If the
owner answers it as a revision, the current rejection stays until 2f-7. Which of the two applies is the owner's or the
planning session's call, not the sketch's.

### G2. Rule 1 across values of one abstract Type (SPEC 8.11.4:789)

**Scope.** This is rule 1's reach by Type atoms: of Φ in callers, and of the earlier result's path-less Loans in
conformances (§4.2). Loans received through parameters stay unreached in callers (15.6:450; c58).

**Options:**

- **(a) Atoms of the Type.** One Type's values share atoms.
  - Callers: `zip`, `both` and c26 err, and `sink` (c54) errs through the equality.
  - Conformances: Keep, `Topped` and Peekable err, as they do today. This is consistent with 8.11.5's `Merge`.
- **(b) Per value in callers, per Type in conformances.** This is today's: two rules for one sentence.
- **(c) A SPEC revision to per-value reach,** with `Merge`'s example rewritten. The sketch designs no per-value rule.

**c24 is a reading inside G2.** Distinct, unrelated Type parameters share no atom, so c24 is valid. The letter admits the
stricter answer: 15.6.5:626 quantifies over every caller, including one that binds `I` and `C` to one Type. Under that
answer c24 errs.

**The question includes the conformance carrier granularity (§4.2).** Does the c24 answer also set it?
- c24 valid: content markers per Place, so `Hooked` (c36c) and `Tagged` (c59) conform, as G1 allows; both are rejected
  today.
- c24 an error: content markers by the receiver's referent Type, so both violate.

Counts: library 0 and milestones 0 (§6). Tests: 2 `EffectBoundCallerTest` rows change; `sink` is a probe, with no test (H).

**Recommendation: (a), with the c24 answer setting the granularity.** (a) is the text and its 8.11.5 example. Tying the
granularity to the c24 answer keeps callers and conformances under one rule. c24 goes with the answer, or to 2x-S1.

### SPEC gaps to report

- **G3, 8.11.4:789 and own operands.** The letter, "may reach every Loan its Origins may denote", excludes no Loan of a
  call's own operands. It is also silent on associated projections and on equalities between them.
  - By the letter, c53, c55 and c54 err; all three are valid today.
  - §4.1's own-input rule keeps c53 and c55 valid; c54 still errs through `sink`.
  - 2f-3 lands rule 1 before S2, so this goes to 2x-S1 or OQ8 before 2f-3.
- **G4, a destruction in a caller.** Does an abstract destruction in a caller meet an earlier result's Φ? This covers
  every abstract destruction in a caller, a Take-only receiver's included, not only Iterators.
  - Proposed reading: no (§4.1). A descending result names only Origins of the receiver's Type, not its storage
    (15.6.3:570), and 15.6.6:661 reaches by paths. 16.3.3:196's "unknown for the body's own Loan checks" then names
    only the mutable-static conflict. 22.2.3:133 says the same for Iterators.
  - The run-time argument is (H): raw-split obligations and containment, as in S2 §2.3's H2.
  - Under the proposed reading, prototype 33b (c8) loses its error at the replacement (§7).
  - Under the other reading, `takeOne`, `replaceIt`, c8's write and a generic `for` loop that returns its item are
    losses: valid today; library 0, milestones 0 (H); tests at least 1 (§7).
  - For 2x-S1.

**Pointers, no new question.**
- 2x-S1's readings of `59e2289ce4f4` (c15, c36) and of the stored Φ (c25) stand.
- 2f-S2 deletes S2's statics clause (§3). It deletes S2 §3's destruction remark only if G4 takes the proposed reading
  (§4.1).
- The F6 audit's ledger note is corrected: Choose and Branch are 2f-5's violations under every option; StoredPair follows
  G1 (§5.5).

## 10. Not verified

Nothing was run.
- The conformance and loss readings, the carrier atoms and modes, the own-input rule and the class assignment of the 35
  rows are (H) until 2x-P6 models them with derived rules.
- S2 §2.3's H2 rests on the ordinary body check, containment (15.6.3:528) and the raw-split obligations, and stays argued.
- Test counts come from line scans and may miss accessor and constructor forms.
