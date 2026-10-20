# G160 plan: independent review, round 1

Reviewer: independent (wrote none of A, B, X or the final plan). Scope: `draft/Proposals/2026-10-21 G160 Raw Storage Kept by
Declarations.md` (the plan) and the edited G160 row of `docs/dev/PLAN.md` in worktree `bin/worktrees/g160-plan` (base
`59b78023`, uncommitted). Read: AGENTS; SPEC 5 (all), 15.3.5, 15.6, 15.6.2, 03:164, 03:243, 08:789; `MirTypes.Holding.cs`,
`MirBorrows.Values.cs`, `.Calls.cs`, `.Flow.cs`, `.Loans.cs`, `.Solve.cs` (Holders), `Binding.Retention.cs` (RetentionWalk);
DECISIONS R4-M36, R4-M73, R4-M76; COMPILER_ARCHITECTURE "Type facts in MIR", structure limits and D5; designs A, B and plan X;
every probe and output under `draft/Sketches/G160/probes/`.

Extra probes (same harness, dev `a1677549` binary), sources and outputs in
`scratchpad/g160/review/r1.kimi`, `r2.kimi`, `r3.kimi` (+ `.out`):

| Probe | Shape | Today |
| --- | --- | --- |
| r1 `keptReadRef` | `storeKept(b@uniq, x@ref)`; `let r = b@ref`; `q = r.p[0]`; `b` replaced; `x = 5`; `inspect(q)` | accepted |
| r1 `keptReadUniq` | `let r = b@uniq`; `storeKept(r, x@ref)`; `q = r.p[0]`; `b` replaced; `x = 5`; `inspect(q)` | accepted |
| r1 `keptDirectDead` | `KeptBox<ref/i32>`; `unsafe => b.p[0] = x@ref`; `x = 5`; b dead | error `x = 5` |
| r1 `keptInitDead` | the same with `Raw.initialize(b.p, x@ref)` | error `x = 5` |
| r2 `rawArrayIter` | `var arr: Array<raw/(ref/Data)> = [cell]`; `for p in arr`; `arr.append(cell2)`; `inspect(arr)` | accepted |
| r3 `cursorWrite` | `struct Cursor<T> {p: raw/T}` built from `w@raw` (`w = y@ref`); `setC(c@uniq, x@ref)` writing `c.p[0] = v`; `x = 5`; `inspect(w)` | accepted |
| r3 `cursorWriteLive` / `directWrite` | the same with `inspect(c)` / the in-body twin `p = w@raw; p[0] = x@ref` | error / error |

Every factual anchor checked (Holding.cs:15-16, :75-81, :153-156, :174-179, :189-199; Values.cs:72, :95-97, :227-230;
Flow.cs:132-147; Calls.cs:53-63; Loans.cs:135; Checks.cs:148; MirTypesTest.cs:46-72; SPEC 05:38, :69, :73, :101, 143-191;
15:319-321, :325, :453-454, :522; 03:164, :243; 08:789; design-A3.md:759-763; STATUS:49) matches, except where N9 says.
Every §1.2 "Today" cell matches its probe output.

## Blocking findings

### B1. §4.2's walk does not produce two verdicts the plan claims (rule 2 needs reference transitions per place)

- **Location.** §4.2 (walk rules), §4.3 rule 2, §1.2 rows (a″) and (c), §7 `twoUniq` and `rawShared`.
- **Problem.** Rule 2 relies on "the Type walk follows every route a call may write by", but §4.2 states only: raw outside a
  declaration → RawPointee; in Shared, exclusive referents → Shared; "Unchanged: … the guard for every non-raw part". Today a
  referent is walked only from Own (`Holding.cs:174`, `own && …`). Nothing in §4.2 walks an exclusive referent from Exclusive,
  or a shared referent from Own or Exclusive. As written:
  - `rawBoxUU` (`storeRawUU(u@uniq, …)`, operand `uniq/(uniq/RawBox)`): the inner `uniq/RawBox` is met in Exclusive and not
    descended, so RawBox's `Bare` is never met, `through` lacks `Unowned`, and rule 3 does not apply (pass 0). (a″) stays
    accepted; `twoUniq` fails after the change.
  - `cRawShared` (operand `uniq/(ref/(raw/(ref/i32)))`): `ref/…` is met in Exclusive and not descended. (c) stays accepted.
  - The same miss hits a holder whose Field is `uniq/RawBox` (`H {a} {u: uniq/(RawBox during a) during a}`): Keeping does not
    copy a nested instance's `Bare` (B §4.2), and the instance walk meets RawBox only past a second exclusive layer.
  - The early exit is not addressed: `done` is `Loan|exclusive` in Own and `met` elsewhere (`:182`), so a Tuple
    `(ref/i32, raw/(ref/i32))` in Exclusive stops at `Write` before its raw component. `Unowned` must join `done`, which makes
    every walk without `Unowned` exhaustive; §8 says "HoldsLoans keeps its order".
- **Evidence.** `Holding.cs:174-184`; design A §4.1's table had "met, referent in Exclusive (new: deeper layers)" and shared
  referents to Shared from every place; the final plan dropped those rows. R4-M73 rejected "exclusive-mode descent" for W, so an
  implementer cannot assume it.
- **Correction.** Give §4.2 a per-place transition table: from Own and Exclusive, an exclusive referent (and a UniqSlice's
  elements) is walked in Exclusive at every depth, adding only `Unowned` past the first layer, so the guard is unchanged; a
  shared referent (`ref`, `objref`, Slice, `rc`, `arc`, Weak) goes to Shared from every place. Put `Unowned` into each mode's
  `done` set, state the cost in §8, and add `H`-shaped and Tuple-in-Exclusive rows to MirTypesTest.

### B2. The read reach misses kept storage read through a reference to the keeper (same cause, unsound acceptance)

- **Location.** §4.4 ("reached from a root holder h by inline steps only"), §1.2, §7.
- **Problem.** Under K, a call's store into kept storage goes only to the keeper's holder. An in-body read of the kept Field
  past a borrow (`r.p[0]` with `r = b@ref` or `b@uniq`) gets reach {E} under §4.4. It loses σ(b), which `Read` had computed
  (`Content(σ(r)) = σ(b)` at the Deref, `Values.cs:66-74`). `keptReadRef` and `keptReadUniq` (r1) are accepted today and stay
  accepted, although they have the same cause as `keptReadKill`, which the plan fixes. Under K's own sentence 2 such a read is
  "through that value": every library accessor (`get(self: ref/Self)`) reads exactly this way, so the obligation cannot excuse it.
- **Evidence.** r1 output (no line for either function). Design A §4.3.3 covered the case with its carrier term C ("the root for
  an inline prefix, else refs of the layer before the last borrow step"), and the final plan adopted A's reach without it.
- **Correction.** A kept raw step keeps the layer that `Read` and `Issued` have already computed: value := layer ∪ {Φ_E}
  instead of {Φ_E}. This is shorter than the inline-path test and covers both shapes. For `Store`, use A's C (or only E, if B4 is
  settled that way). Add `keptReadRef` and `keptReadUniq` to §1.2 and §7 (fail before, pass after).

### B3. Reading K is unsound for a non-owning, marker-less `raw/T` keeper; K's sentences do not cover it

- **Location.** §1.3 (K and its two sentences), §1.2, §4.1 (kept test), §6 (X row), §10 Q1.
- **Problem.** K decides "kept" from shape only: `raw/T` written by D at route Own over D's parameter. It cannot tell storage D
  owns (`Buffer`, `OwnedRemainder`) from storage D merely points into. `Cursor<T> {p: raw/T}` built from `w@raw` is kept under K.
  `setC(c@uniq, x@ref)` writes `x@ref` into w's storage, but W (rule G) reaches only c, so `x = 5; inspect(w)` is accepted
  (r3 `cursorWrite`; the in-body twin `directWrite` is rejected). SPEC 5.2.1 makes `setC`'s write valid:
  - authority: `w` is a `var` and the address was taken from it directly;
  - no live Loan conflicts (`@raw` leaves none).

  So no unsafe obligation is broken, and the borrow violation lies in safe code (`x = 5`, `inspect(w)`). K's sentence 2 covers
  only *reads* through a non-owning pointer, and it puts the duty on "the reader", here safe code (SPEC 5: "Safe code alone never
  causes undefined behavior"). A keeper with an exclusive marker (`View`, `UniqRemainder`) or a `uniq` Field (`V2`) is safe,
  because rule G reaches the owner (P13 hold until R4-M76). A keeper with a shared marker cannot write. A marker-less one is
  unsound. The plan neither lists the shape nor discusses it.
- **Evidence.** r3 output (`cursorWrite` absent; `cursorWriteLive` and `directWrite` report `x = 5`). Under U (design A: `@raw`
  gives {Φ_E}) and under X (no `via` declaration means bare, so E) the shape is rejected.
- **Correction.**
  - Add a third K sentence: a raw layer K treats as kept addresses only storage its value owns, or storage whose owner's Loan it
    keeps by a `Loan<T>` Field (`uniq` for writes). Any other write through it breaks the writer's unsafe obligation.
  - Put the shape in §1.2 with its verdict under K, U and X, and in Q1.
  - In §6, credit X: its explicit `via self` / `via loan` separates owned from borrowed storage, which K's shape rule cannot.
    "The same distinction made explicit" understates it.

### B4. Stores through a kept Field reach E, which gives a Language error that K's sentence 1 makes wrong

- **Location.** §4.4 ("In-body stores through a kept Field still reach E, as today … conservative and sound"; "a pointer
  extracted … reaches E only"), §1.3 sentence 1, §1.2, §7.
- **Problem.** The proposed sentence 1 ("kept raw storage carries its values' Loans in the keeper's holder") makes
  `keptDirectDead` (`b.p[0] = x@ref`, b dead) and `keptInitDead` (`Raw.initialize(b.p, x@ref)`) valid. Their call twin
  `storeKept` is accepted. The design still reports `x = 5` for both:
  - `Store` joins E;
  - `b.p` passed by value has `Unowned`, so rule 2 adds E.

  That is a conservative Language error on a form valid under the recommended reading, which AGENTS' Diagnostics forbid. Neither
  shape is in §1.2 or §7.
- **Evidence.** r1 output (`keptDirectDead: x = 5`, `keptInitDead: x = 5`). Plan X adopts the opposite rule explicitly, as part of
  its proposed SPEC text ("Direct raw Store continues to target E"; X §5.2: extracting the raw Field "intentionally loses that
  static abstraction").
- **Correction.** Choose one and state it:
  - (i) a store through a kept Field, inline or past borrows, joins the keeper's carriers (not E), like a call;
  - (ii) a K sentence, X's principle: direct raw access to kept storage, and a pointer read out of the keeper, follow 15.6's raw
    rule (E).

  Put the choice into Q1, give §1.2 rows `keptDirectDead` and `keptInitDead`, and add tests. Under (i), the read reach in B2
  serves the store too.

### B5. Reading K is presented as "what R5 says", but R5 also names `Loan<T>` Fields, and K needs an R5 amendment

- **Location.** §1.3 K ("This is what R5 says: the dependency is kept 'through a Type argument'"), R-S's rejection reason,
  §1.4 ("What a marker does state is authority … `Loan<uniq/E>` grants one"), §10 Q1, "What K needs from SPEC".
- **Problem.**
  - R5 (05:69) reads: "the Type **owning** the storage must keep them through a Type argument **or a `Loan<T>` Field**". An
    owning, non-generic Type that allocates its storage and keeps slot `a` by `Loan<ref/i32 during a>` (MarkBox's shape) meets
    R5's text, yet K makes it `Bare` (E). K therefore narrows R5's second way. The SPEC revision needs to amend R5's clause, not
    only add two sentences, or the revised SPEC will call MarkBox kept while A5 reports an error.
  - The reason given against R-S, "the meaning of `p` depends on another Field", applies equally to R5's own `Loan<T>` way.
  - R-S bundles two different readings: slots kept by a `Loan<T>` Field (R5's letter) and slots kept by a stored reference
    (beyond R5).
  - §1.4's "grants one into the source's storage" contradicts R7 and 15:325 ("grants no access"), which §1.1 itself quotes.
  - Design B's actual argument, that in SPEC's only example (`BufferView`) a `Loan<T>` keeps the storage owner's Loan in a
    non-owning view, was dropped. That argument is the honest basis for K.
- **Correction.**
  - Quote R5 in full in K. Say that K reads R5's `Loan<T>` way as a non-owning view keeping its owner's Loan (B's argument), which
    is a reading that needs a SPEC amendment of 05:69.
  - Split Q1's alternatives into R-L (slots a `Loan<T>` Field keeps: R5's letter for owners) and R-S (also stored references).
  - Restate §1.4: a marker keeps the Loan under whose liveness the pointer's authority holds (5.2.1 condition 3), not
    "grants".

## Non-blocking findings

- **N1. Shorter alternative and a speculative field (§4.1, §4.2, §4.5).**
  - Make `Keeping(D).Bare` transitive over nested instances, by any route. Then `Unowned` needs no Field walk in InDeclaration:
    it is the OR of
    - raw layers in the body-level Type, past any reference;
    - `D.Bare` for each instance met;
    - Bare-routed arguments, walked as RawPointee.

    Instance Fields would be walked only for the guard, as today. This drops the InDeclaration mode, keeps the `byte` memo and
    keeps the early exit (see B1). It is worth comparing before G160-1 fixes the memo width.
  - `Routes[k]`'s Own, Exclusive and Shared bits are not consumed by G160; only Bare is. Keeping them "for R4-M76" is a
    speculative mechanism (AGENTS "Short"). Also, that claim needs `Loan<T>` and `Weak<S>` to count as storing their argument
    (15.3.5, 3.2.2) in Keeping's DFS, and §4.1 does not say so. Keep only the Bare bit, or design the per-part guard's needs now.
- **N2. `toRaw` expectation contradicts §4.2 (§7).** §4.2 says a raw pointer past an exclusive layer gives "`Unowned` where
  `Write` was". Then `uniq/(raw/(ref/i32))` loses `Through`, as design B's §7 says, and so does `S<ref/i32>`. §7 expects
  `Through | Unowned`. Make the two agree, and say that the guard drops for raw-past-exclusive: harmless, because a raw pointer
  is never a holder.
- **N3. Projections (§1.3, §4.1, §8).** B's `Projections` were dropped, and the two sections disagree:
  - K says "(or their projections)", while §4.1 counts "an abstract part that is not D's parameter" as concrete.
  - If `AdtDef.FieldType` resolves an associated projection Field to `raw/(ref/i32)`, the InDeclaration rule drops a body-level
    raw layer, so `Unowned` is missed.

  State the rule (B's Projections, or show that the instance keeps the projection abstract, so rule 3 and Q4 apply) instead of
  leaving it only in "Unverified".
- **N4. A verdict change not listed (§1.2, §7).** By §4.2–§4.3, `ArrayIterator<raw/(ref/Data)>` gets `Unowned`, because the
  Type argument is a body-level raw layer. `next`'s self-row then also stores σ(it), which holds the Loan on `arr`, into E. r2
  `rawArrayIter` (accepted today) is expected to become an error at `arr.append(cell2)` (inferred from the rules, not run). This is consistent with the letter (R4-M73's coarse
  rows). List it, test it and confirm the census; it is the cost of `Slice`, `Option` or `Array` of Loan-capable raw pointers.
- **N5. Fixed point (§4.1).** Starting from empty summaries is optimistic for the kept test (Concrete false), and kept paths and
  Routes are not monotone in it. Say that `Concrete` is solved first, or that every round recomputes kept paths and Routes. A
  stale "kept" must never survive, because B2 and B4 (i) would make it unsound. Also give B's `compose` table, at least for Bare.
- **N6. Records (§9).** G160-1 introduces the flags representation and the name `Unowned`. AGENTS records a new
  representation "in the commit that first implements" it, so record it in G160-1, or keep the name `Raw` until G160-2. Register
  the plan's intake in `draft/INTEGRATED.md` in the commit that adds it, as other implementation plans are (2026-10-21 section).
- **N7. Fairness to X (§6, §1.3).**
  - "Adds code the D5 freeze forbids": X's U5 already requires an explicit freeze exception, so say "needs a D5 exception".
  - "A SPEC extension … before any fix" applies to K too (G160-0 needs the SPEC revision before G160-2).
  - X's §5.1 (4), "`via self` rules out Copy", is missing from K. Name Copy keepers in K's alias sentence.
- **N8. U's rejection (§1.3).** "It also contradicts R5's Buffer sentence" overclaims. Under U, the Loans are kept at least
  while the Buffer lives (design A §1.3), so the sentence becomes a lower bound, not false. The Local Reasoning argument stands.
- **N9. Anchors.**
  - `MirBorrows.Endings.cs` has no `StepClass.Raw` use (§4.4).
  - R7 merges `@raw` ("leaves no Loan", 05:101) with borrowing a raw Place ("fresh anchor", 05:73).
  - R6's "Such facts are computed once per declaration": 15.3.5 names four facts, so keeping follows them by analogy.
  - The "four-layer chain … 121596, 204840, 513216" are R4-M73's review values at `8a7632d9`, not a test baseline. Measure
    the unit's base.
- **N10. Sites (§5).**
  - Add G147 (1): `placeEntry(handle: raw/u8, …)` stores into raw storage with no contract row. It is a related miss with a
    different cause (typed content behind `raw/u8`; 2d-3), so its line reads "no".
  - The (d) unit's Binding fact ("raw layers in a nominal Type's declared storage") overlaps Keeping's walk. Name the shared
    sub-fact so the (d) design decides one owner.
- **N11. Wording.** "Store joins every target in the reach" and "In-body stores through a kept Field still reach E" read as
  contradictory. Settle this with B4.
- **N12. PLAN row.**
  - "A call's store through raw storage that no declaration keeps misses E" presumes K. (a) is a defect under K and U, not under
    R-S. Add "(under the recommended reading K; Q1)".
  - "the naive E … and the `Loan<>`-marker rule both give false errors (shared and owning iteration, SPEC's `Buffer`)" mixes the
    two: the naive rule errs on shared iteration, the marker rule on owning iteration and `Buffer`.
  - After B2 and B3, extend the read-side and Cursor facts.
  - Otherwise the row states facts, not progress.
- **N13. Rule 3 (§4.3).** B's rule 3 had "a holder **or root local** reached in the second pass". Today's raw clause fires for a
  direct Loan naming a raw-only local in either pass. Show that pass 1 never needs it (the operand Type shows every non-abstract
  route), or keep the root local.

## What the plan gets right

The root cause (the holding walk has lost which declaration wrote a raw layer, R6) is correct and complete for calls. So are:

- the per-declaration owner in `MirTypes`;
- `Unowned` as a flag independent of the guard;
- E by the operand's Type, exact per route (`put(b.r@uniq, …)` adds none, and this is sound because no route reaches `b.p`);
- P13 composition (rule 2 concrete, rule 3 past `clean`);
- 2e-D, 2c and 2f taking `through`;
- adopting A's read-side finding against B's §4.5;
- the warm-allocation plan;
- (d) as a separate Binding unit.

The rejections of the naive rule, the marker rule, the instance-walk exemption (TupBox and Outer need composition), visibility
and Φ_R are correct.

## Verdict

**Reject (blocking findings B1–B5).**

- B1, B2 and B4 are design-level gaps with short corrections: transition rules, the layer-keeping read reach, and one store rule.
- B3 and B5 change what the owner is asked in Q1. K, as proposed, is unsound for non-owning marker-less keepers and is presented
  as R5's letter when it amends R5.

Redo §1.2–§1.4, §4.2, §4.4, §6, §7 and §10 accordingly, then re-review.
