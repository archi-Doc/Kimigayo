# Independent review, round 2: Raw Pointer Rules Left Open by G160 (2026-10-22)

Reviewer: independent (wrote neither round). Read in `bin/worktrees/r4-proposals` at `dev` `127b6f0e`: the uncommitted
`draft/Proposals/2026-10-22 Raw Pointer Rules Left Open by G160.md` and `draft/Sketches/R4 Proposals 2026-10-21/review-3.md` §1.
Checked against AGENTS.md; SPEC §1.3, §3.5.2 (Copy opt-in), §5 (intro, §5.1, §5.2.1, §5.4, §5.6, §5.7), §6.3.4, §7 (parameters),
§11.2, §11.2.1, §15.2.1, §15.2.2, §15.3.1, §15.3.5, §15.3.6, §15.6 (Addresses), §15.6.1, §15.6.2, §16.3, §16.3.1, §17.5, §23.1.1,
§23.1.3, Appendix D.4; `draft/Changes/2026-10-21 G160 Raw Reach and Raw Origins.md` (D1–D20, §3, §6, §7, §9.2); DECISIONS R4-M95;
DIAGNOSTICS §2 rule 11; COMPILER_ARCHITECTURE "Closed vocabularies"; BU's `design-final.md` (main checkout, read only: §0, §3,
§4, §5, §9, §13, §14). Nothing was built, run or edited; the evidence below is SPEC text and greps. "(H)" marks a reading of mine
that no run checked.

## Verdict: ACCEPT WITH FIXES

All four round-1 blocking findings are resolved (table below), and 2.2 and 2.3 are sound as recommended. Three blocking
findings remain, two in 2.1 and one in 2.4; each has a local fix that keeps the recommended option (one whitelist sentence, a
corrected example, one trigger removed). The fixes need no new design round, but the planning session should re-check the 2.1
sentence against B′+ before the owner sees it.

## 1. Round-1 blocking findings

| Finding | Resolved? | How |
| --- | --- | --- |
| RP-B1 (2.1: `null` lost; Appendix D vs Unsupported) | Yes | `null` stays valid for Fields and Cases; no Appendix D line; a decidable verdict (Origin relation error) with a repair; A's row names the narrowing from B+'s U3. New gaps: RP2-B1, RP2-B2 |
| RP-B2 (2.2: D18 revised silently; substitution missing) | Yes | OQ-R2 (a) and §2.2 state the supersession for formed Origins and a reason; row 2, A's assessment ("three routes") and §3 ("Typed paths", "Reading a Field", "Calls") include substitution; review-3's two non-blocking items (`RawStep` row, write-back cost) are adopted |
| RP-B3 (2.3: false representation claim; A rejects defined programs) | Yes | "What a check needs" states the own-step marker carrying the owning Place, §15.6.2 comparison, and the change to the owner-approved address; A is now the rejected option with the defined uses (`p == null`, `p@usize`, …) and the second-liveness-kind cost; B (Warning) is recommended; Take, `exchange`, `swap`, replacement and destruction are covered; reach scope stated. OQ-R3 still does not name the representation change (N14) |
| RP-B4 (2.4: C-layout exemption; G160 §6 Advice missing) | Yes | Exemption dropped; the canonical unstated form is not warned; G160 §6's Advice is option A. Its added constructor trigger is new (RP2-B3); the `Option<raw/T>` scope note is not addressed (N21) |

Review-3's other non-blocking items are adopted (the `unsafe =>` in the example, B+'s write reading marked as an addition, D
removed, §2.3's operations, required warnings stated in their chapters, STATUS/Appendix D wording, §7 additions), except N21.

## 2. Blocking

**RP2-B1 (§2.1, rule): the rule is not closed over its family; a `static` source and exclusive borrows of the Place are
undecided or contradict existing rules.**

1. *Sources.* The rule infers "only `null` fits" from "no Origin expression names" the holder's own storage. Naming decides
   claims, not fits. Fits are decided by `outlives`: "narrowing it to one it outlives, is a fit" (§5.7) and "`static` outlives
   every Origin" (§15.2.2). So for `b.data = q` with `q: raw/T during static`, the general rule says fit and the new bullet says
   error: two rules, two verdicts. The same store through a borrow (`r.data = q`, target Origin `r`) is a fit under B+'s reading,
   which §3 makes SPEC text. State the reason that excludes every stated Origin (own storage is an own step, §5.7's table, not a
   region, so no Origin, `static` included, is related to it), and name the `static` case in the narrowing (or decide that
   `static` fits). Either answer is decidable; the text must give one.
2. *Operations.* The rule speaks of writing and Case construction. An exclusive borrow of the Place through no dereference
   (`let q = b.data@uniq`, also the implicit `uniq` arguments of `exchange(b.data@uniq, p)` and `swap`, §15.7) writes it later
   *through a dereference* of `q`, where the proposal's other rule applies: the target's Origin is `q`'s referent Type's. That Type
   must equal the Place's (`uniq` is invariant in its target, §15.3.5), which states own storage that no body Type can state. If
   B′+'s `ReadHolder` gives the Place `raw/T` (H: B+ §5 lists no borrow row for `Holder` and U3 covers writes only), `q@follow =
   w@raw` stores an unstated pointer into a `during self` layer in safe code, which is D5's "adding an Origin is a claim, only by
   `E@(raw/U during o)` in an unsafe context" broken; A5 would then treat that storage as the holder's own (W, `RawStep` Own).
   Compound assignment (`b.data += 1`, a write through its reservation, §13.7.2) is a third member, an error under the rule but not
   named.

   Fix (one sentence, a whitelist): "Through no dereference a `during self` layer's Place admits reads, shared borrows and writes
   of `null` only; any other write, an exclusive borrow (also as an argument of `exchange`, `swap` or `replace`) and a Case
   construction with another payload are Origin relation errors; the repair goes through `b@uniq`." This covers 1 and 2, keeps
   constructors and `drop` on their route (§6.3.4 and §16.3.1 already give `self.data@uniq` there, and `exchange` is drop's
   canonical way to take a Field), and lists the narrowing exactly.

**RP2-B2 (§2.1, example): it is not valid Kimigayo.** `Buffer<T>.data` is `internal let data: raw/T during self` (§5.6). "A
`let` slot is never exclusively borrowed or written (it has no `set`)" (§11.2.1; §6.3.4: "an initialized `let` Field is
read-only"). So `b.data = null`, marked "Valid", is an error, and so is `r.data = other@…`. The example is the rule's showcase and
would go into §5.7. Use a Type with a `var` raw Field (for example `struct Cell<T>` with `var data: raw/T during self`), declare
`other`, and add the cases RP2-B1 names (`static` source, `b.data@uniq`) as marked errors.

**RP2-B3 (§2.4, option A): the constructor trigger extends G160 §6 and its repair can be false.** G160 §6 plans "as Advice, a
Field a `drop` releases without `during self`"; A adds "or that a constructor fills from `Raw.allocate`", without saying it
extends the owner-adopted text. A Type with that Field but no `drop` may hand the storage to another owner (a registry or arena),
where `during self` is a false ownership claim, and it may opt in to Copy (`Self is Copy` needs no user `drop`, §3.5.2), where
`during self` makes the opt-in an error (§5.7: "a Type with a `during self` layer is not Copy"). The check cannot decide
ownership, so DIAGNOSTICS rule 11 ("a fix that needs a choice is not offered") and §23.1.3 rule the candidate out, and A's "no
false repair" and §5's "repairs that are true" do not hold for this trigger. The `drop` trigger is sound in both respects: a
`drop` makes the Type non-Copy (§16.3) and releasing the storage is the ownership fact. Fix: keep G160 §6's trigger only (or keep
the constructor trigger without a repair candidate).

## 3. Non-blocking

§1 and §2.1

- N1. Row 1's "a Field of an owned `self: Self` receiver" is no open form: parameters, receivers included, are immutable `let`-like
  bindings that "cannot be reassigned, reinitialized or newly borrowed exclusively" (§7), so `self.data = p` is a mutability error
  before any Origin rule and the repair `self@uniq` is invalid there. The route is `var s = self@move`. Drop it from row 1 or say so.
- N2. "B+'s U3 becomes this Language error" (A's row, §4): B+ §5 reports every such write as U3, `null` included, so U3 becomes
  acceptance for `null` and the error otherwise. Also "today" is B+'s plan; on dev every raw `during` is still G160-0's
  `Unsupported_Kd`.
- N3. OQ-R1 (a) should name the narrowing it decides (non-null sources, `static` included or not, compound assignment, exclusive
  borrows and `exchange`/`swap` through no dereference), not only A's assessment row.
- N4. The repair adds an unsafe claim whose promise the check cannot decide; the candidate lists it as undecided (§23.1.3), so §5's
  "repairs that are true" overstates. For a Case, a route may exist after construction with `null`: a write through an exclusive
  pattern binding on a borrow of the enum (H); "constructed with `null` only" is right for construction itself.
- N5. Field declaration initializers (`var data: raw/T during self = …`) are not covered; constructor privileges do not extend to
  them (§6.3.4), so they take the no-dereference rule. Say so.
- N6. When §3 adds "through a dereference the target's Origin", note that through a raw pointer stating none the target states
  none, so any source fits there (an unsafe context is needed anyway).
- N7. Evidence for the (H) (grep, not a run): the library's only `var` raw Field is `DictionaryStorage.Handle.buffer`, written
  only through `(*handle)` (`DictionaryStorage.kimi:109`, `:160`, `:219`), a raw dereference; the library has no `during self` yet;
  `tests/milestones` contains no `raw/`. The census after G160-3 stays (H).

§2.2

- N8. G160 records no reason for D18 (its row, §3.2 and R4-M95 state the rule only). The stated reason is a reconstruction; say
  so, and that it rests on D15's "the borrow that Origin belongs to" (a written meet of two inputs belongs to neither; a formed one
  belongs to the borrow it was obtained through, whose Loans cover what it may address).
- N9. §4 should list the DECISIONS entry that supersedes R4-M95's "no intersection" for formed Origins (AGENTS, Record decisions).
- N10. For slot substitution (`lens.at`) the binding replaces the slot; "the outer Origin of the borrow it is formed through" reads
  oddly there. Shorter and exact: "`during` writes one atom other than an intersection; an Origin a raw layer obtains by a typed
  path, a `self` read or substitution may be any Origin, and it belongs to the borrow it was obtained through."
- N11. BU's §14 (item 11) says "if the owner adopts OQ-R2 (b) (R4's proposal)", round 1's numbering; round 2 recommends (a). Say so
  in the header so BU's condition is read correctly.
- N12. Outside this proposal (an open issue, not a fix here): the Authority error is kept at formation on all three routes, but a
  fit can narrow a Shared Origin to an exclusive one it outlives (`raw/T during x`, `x: ref`, to `raw/T during o`, `o: uniq`, under
  `origin x outlives o`), after which Binding's Authority check no longer refuses writes (H: B+ `FitOuterOrigins` relates both
  sides through `RegionOf`). Condition 3 still makes such writes undefined.

§2.3

- N13. Agreed: the condition is decidable with the marker, rejects nothing, and A's assessment is correct.
- N14. OQ-R3 (b) should say it asks the owner to approve a change to the owner-approved address (R4-M95 "Representations", D19)
  at G160-4's design, not only "if it adds a closed-vocabulary entry".
- N15. "changes … D1 ('an address conflicts with nothing')": a Warning check can keep that sentence true for Loan checking; what
  changes is the marker on the approved address. (The quoted sentence is §15.6's and R4-M95's, not D1's row text.)
- N16. Compare with the owning value's Place or an ancestor, not §15.6.2's symmetric overlap: a Take of `b.inner.x` leaves `b.inner`
  in its holder.
- N17. State whether a call with exclusive access to the owning value counts (`b.push(…)`; a user `reset(b@uniq)` that writes
  `b@follow`): it may replace the value, so counting it warns on every `uniq` method while the pointer is live, and not counting it
  misses those cases while `exchange` and `swap`, also calls, are counted.
- N18. Shorter: an implementation warning (§23.1.1: "an implementation may issue others") needs no SPEC text and leaves the
  precision (N16, N17) to G160-4's full-process design. A required warning (§3's row) freezes the over-approximation into SPEC and
  needs its condition in SPEC terms (an address with an own-step marker is not one yet, so §15.6 joins the mapping). Either way,
  state the repair (re-read the pointer from the `during self` Field after the operation, §5.2.1 condition 3's last sentence) and
  when the Warning reaches the default pipeline (D9).

§2.4

- N19. "Advice" is not a severity (§23.1.1: Error, Warning, Information, Hint), and DIAGNOSTICS rule 11 forbids Advice in records.
  Name the severity (Information or Hint) and the repair candidate.
- N20. The candidate must also add the claim at each fill: after `during self`, a constructor's `self.data = Raw.allocate<T>(n)`
  is an Origin relation error without `@(raw/T during self)` (§5.6's `Buffer.init`), and the claim's promise is listed as
  undecided.
- N21. Limit the trigger to raw layers in inline storage: inside a Type argument (`Option<raw/T>`) `during self` is unavailable
  (D17), so the repair would be invalid (review-3's note, not addressed).
- N22. Define "releases" over a closed derivation (a Field read, local Copies, displacements and raw conversions reaching
  `Raw.release`'s argument in `drop`); a release through a helper is then missed, which is acceptable for an implementation
  diagnostic. Its phase (Binding) is (H).
- N23. A covers only the owned subset of row 4; a non-owning hidden pointer (`Cursor<T>{p: raw/T}`) stays condition 5's
  undiagnosed obligation by design (D3: unstated is sound). Say so.
- N24. As an implementation diagnostic it needs no SPEC text, so §3's 2.4 row can go (DIAGNOSTICS records it). Vocabulary: a new
  diagnostic code is implementation-defined (§23.1.1) and not a closed vocabulary; owner approval is needed only if Binding reports
  it through a new `BindingFailure` entry (89 of 89; B+ uses 88). Name which.

§5 and §7

- N25. "(H)" is missing on D18's reconstructed reason (N8), A's "no false repair" (RP2-B3) and §5's "repairs that are true" (N4).

## 4. Principles

Hold for 2.2 and 2.3. For 2.1, Principle 1 (one verdict per store) and Principle 3 (explicit semantics) hold only after RP2-B1:
today a `static` source has two verdicts and an exclusive borrow none. For 2.4, Principle 4 (true repairs) holds only for the
`drop` trigger (RP2-B3).

## 5. Shorter formulations

- 2.1: the whitelist sentence of RP2-B1 replaces the rule paragraph and the Case sentence.
- 2.2: N10's sentence.
- 2.3: an implementation warning designed in G160-4, with no SPEC text (N18); OQ-R3 then asks only for the representation change.
- 2.4: G160 §6's `drop` trigger only, as an Information whose candidate adds `during self` and the fills' claims (undecided: the
  claims' promise); no SPEC text.
