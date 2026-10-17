# Independent design review: A5 step 1, the 1b design diff (choice-1b.md)

Reviewed: `draft/Sketches/A5 Step 1/choice-1b.md` at `bin/worktrees/a5-step1` (origin/dev `5c42b577` plus the uncommitted
records), against the four designs, review-23a, design A3, DECISIONS R4-M35..R4-M42/R4-M50, SPEC §15.6–§15.7, §14.8.3,
§14.10.3, §4.7.4, §4.8, §13.7, §12.2 and the MirBuilder/MirValidator/MirTypes/MirEffects code. I wrote none of the designs.
Nothing in the repository was edited or built. Evidence here: `proto.cs` (round 10 plus 17 review cases 100–112b and two
variants, `FreshParents` and `SeedOwnValue`; 234 cases) and `run.txt` (`dotnet run proto.cs`). Round 10's 217 rows are
unchanged. (H) marks an unverified claim.

## 0. Verdicts

| Item | Verdict |
| --- | --- |
| G119 route reach, destroying activation | Accept; wording change W1 |
| 33c derived excuse | Accept |
| G110, obs cut omitted, containment wording | Accept |
| 39o fresh-closure seeding | **Redo (B1)**: one member of a family; the chosen rule leaves unsound acceptances |
| Slice index as a borrow Deref | Accept with change: drop "(UniqSlice)" (N1) |
| Guard-local marker γ, Loan-based ended row deleted | Accept (the representation of SPEC 14.8.3's `g`); notes N2, N3 |
| G92a assumed completion + α | Accept with changes: **B2** (Replace's value temp) and N4–N6 |
| G92b (b2) fold | Accept, recommend (b2); clarify N7 |
| G94 Locate | Accept with changes: the handle copy moves to the §3 rule; N1, N8 |
| G91 Enter cut + growth probe | Accept; N9 |
| §3 Copy operands read late | Same cause as G94's handle copy; its own unit before G94 (§4) |

Run (`run.txt` summary, 234 cases): design A **4 UNSOUND (100, 100d, 101, 109)**, 23 listed, 183 same, 24 moves;
FirstDerefBase 4 UNSOUND (same cases), 7 listed, 202 same, 21 moves. With `SeedOwnValue` (B1's fix): no UNSOUND and no
unlisted rejection under either base (23/187/24 and 7/206/21); it changes only those four cases.

## 1. Blocking findings

**B1. 39o fixes one member of a family; the closure misses holders used only through ℓ's own value.**

- General rule (SPEC 15.6): "A value is also used wherever a value that carries a Loan on a Place overlapping the first
  value's holder is used", restricted, as A3's closure is, to Loans with no dereference. A check at ℓ must therefore include
  `σ_in(T)` for every holder T named by a direct Loan of a value used at or after ℓ. A3's closure starts from `σ_in` of the
  holders live after ℓ, where ℓ's own value is absent: the Loan a Ref forms, its R2 parents, and at an activation the
  reservation holder `t`, which `CS_call` drops whole (A3 §5). 39o seeds only the first.
- Counterexamples (S3 {r: ref/i32, n: i32}; SPEC verdict error, Rust E0502 for the first three (H, not run); all accepted by the chosen
  rules under both bases):
  - **100** `let r = s@uniq; r.r = r.n@ref; let q = r@follow@uniq; q.n = 5; inspect(q.r@follow)`. q's parent Loan `Lr`
    names s, so s is used after ℓ and its `Lc` (`r.*.n`, shared) must meet the exclusive Borrow of `r.*`. Error at `let q`.
    With `n: Array<i32>` and `r` into an element, `q.n = []` then `inspect` is a use after free.
  - **100d** the same through `let q = keep(r@follow@uniq)` (retains): error at the activation; accepted.
  - **101** `s.r = s.n@ref; setN(s@uniq, 5)`, s dead after: the callee may write `n` and read `r`; SPEC's activation sentence
    checks "the values live into its call other than its own argument", and s is used through `$a`'s direct Loan. Accepted.
  - **109** (SPEC-literal; Rust accepts) `s.r = s.n@ref; s.n += 1`, s dead after: the writeback activation is an error by
    the same sentence. Report the precision question as a SPEC gap (§6 here), no rule change.
- Ablations: `RefParentsInTransit` (A3's letter for transit) rejects 100 but is over-strict on 30b/30d, which is why the
  choice excludes parents from transit; the cause is that parents are *closure seeds*, not conflict candidates.
- **Fix (one rule replacing 39o's row, `MirBorrows.Checks`, ≈ +4 lines over 39o):** the closure of `CS(ℓ)` is seeded with
  `σ_in(T)` for each T named by a direct Loan (or `E` for `Φ_E`) of the value ℓ stores into a live holder or `E` — the Ref's
  own Loan and its R2 parents from the layers it carries (G110's start layer) — and the closure of `CS_call(ℓ, t)` with those
  of `σ_in(t)`. Conflict candidates are unchanged (ℓ's own Loan, its parents and the reservation in `t` stay unchecked
  against ℓ). `SeedOwnValue` implements it: 100, 100d, 101, 109 rejected at the SPEC site; 100b, 110, 111 valid; no other
  verdict changes on 234 cases. It also covers `err(destroy)`, which uses `CS_call(ℓ, t_K)` (H, not run). A3 erratum aligning with SPEC
  text, like G110 and 39o; a superseding DECISIONS entry for A3 §5's `CS_call` and a PLAN row until it lands.

**B2. G92a's "Never value" predicate, as designed, places a Never form's non-value through `Replace`.**

- `Replace` types its value temp by the *target* when the target completes (`MirBuilder.Values.cs:345`), and a Never call
  or transfer leaves that `i32` temp unwritten (a diverging Call writes no destination, `MirDescriptors.cs:270`). MIR-B's
  `Absent` ("each input is of Type Never") and its site table ("Replace: unchanged, Type choice") then build
  `x = move value` (and the old value's drop) for `var x: i32; x = stop(); return x` and `x = (return 1)`, initializing x in
  check-only code. That contradicts the choice's own contract ("the implementation keeps 'no'", §5) and splits `let x = stop()`
  (unwritten, `MirInitTest.CheckOnlyCodeKeepsItsOwnState`) from `x = stop()` (written).
- Fix: define the Never value by the source node's Binding Type (a node of Type Never writes nothing; its consumer's only-
  Never operation is not built) and give `Replace` a Never-typed temp when its right side has Type Never (one line). Add the
  `x = stop()` row to the C3 test. Also state "has an input" (MIR-B's `Absent`), else a no-input operation is vacuously
  "all Never".

## 2. Rules (choice §1)

- **G119.** General rule: a reservation's Place is re-resolved at its activation, so while reserved (mode `ref` in its holder)
  a shallow write or destruction of any prefix reaches it across Derefs. A rule over a Loan state, not a form: it serves call
  reservations, writeback and destroying activations. New cases: 106 (`exchange(cur@uniq, …)` in the window) is rejected by
  the deep activation; 107 (route through a parameter, `p@follow = o`) is rejected only by G119 (`NoRouteReach` UNSOUND).
  No shorter rule found (`RouteAllParents` over-strict on 38e, 40e). **W1:** state the window as "the witness holder is L's
  reservation holder" (designer A's liveness reading, which `CS`/`CS_call` already evaluate), not designer B's positional
  `ℓ1 < ℓ < ℓ2`, which is not a CFG notion.
- **33c.** Derivation from `reserved` and `excused`, no copy of a rule; sound because an argument of c2 that meets `Φ_c1` also
  overlaps c2's receiver reservation, and that pair is not excused (33d, 33e hold).
- **G110 / obs cut / containment.** SPEC 15.6.3's text; every Loan-forming site (explicit, implicit, guard candidate, G94
  guard, range backing, ref-case pair layer) goes through `issued`, so no formed Loan passes a shared Deref and the obs cut is
  vacuous. rc/arc payloads stay own steps (handle Loans kept). 39k's Loan reading is correctly a choice §5 SPEC gap.
- **Slice index.** A step-class row ("a new form adds a row"), not a case: SPEC 4.8.1 makes the element the handle's
  referent, and it also gives element Copies the content layer. **N1:** UniqSlice has no built-in Index: `UniqSlice.kimi:36,42`
  declare `index`/`indexUniq`, `Binding.Hir.cs:279` makes such an `IndexKoto` a `HirOp.Call` (`CallResult`), and `:358`
  gives `HirPlaceKind.Index` only to built-ins. Drop "(UniqSlice)" from the rule and from G94; MIR-B's "Index: … UniqSlice"
  is wrong and prototype 42c encodes a lowering the builder never produces (its verdict comes from `indexUniq`'s retention).
- **γ.** Not a side table: it is the Loan-side placeholder of SPEC 14.8.3's guard-local Origin `g`, an Origin with a known end
  inside the body that the Origin stratum (no liveness) cannot judge, issued from the published `GuardCandidate` role and
  carried by the one carrying rule, as `Φ_E`/`τ_s` are markers. Only guards have such an Origin (44c). New cases: 102 (stored
  through a live reference, found by the closure) and 108 (returned from the guard) rejected; 103 (Copy out) and 104 (loop,
  overwritten before use) valid; `EndedByLoan` is UNSOUND on 102 and 108 too. **N2:** 108 is also a Refuted relation in the
  Origin stratum (the candidate's Borrow is body-local), so A3's partition row (destroy only) must extend to `err(ended)` with
  `γ` reaching `_0`, else one failure gets two codes (unit 1c). **N3:** γ makes review-23a's C2 lowering contract unnecessary
  (it is that review's second option, "err(ended) independent of the Loan's survival"); restate C2 as "γ lands with or
  before the first-dereference kill".

## 3. MIR facts (choice §2), checked against the code

- **G92a.** Sites verified: `Set` Values.cs:398, `Reached` Calls.cs:161 (callers 75, 96, 114), `Overwrite` :354, `TypeTest`
  Calls.cs:212, Control.cs:209/244, Formatting.cs:44/88/104, Expansions.cs:290/357/416, validator Types.cs:213-216; kept
  structure Control.cs:54/128/277/302, MirBuilder.cs:236, and unlisted but structural Defaults.cs:110, MirBuilder.cs:339/371/431.
  Plus B2. **N4:** A4 counts check-only statements' own effects (`MirEffects.cs:216-218`); AV builds more of them (unproven
  operator checks, bounds `Fail`s, appends), so "runtime unchanged" does not bound the A4 impact: add `MirEffectsTest` and the
  ledger to its checks. **N5:** 12 goldens contain diverging forms (calls, continuations, control, defaults, interpolations,
  matches, objects, opaque, scopes, units, updates, values); the designs name 6. **N6:** the validator rule "a Never operand's
  Type is unknown" (MIR-A, `MirTypes.Agree(_, null)` is true) is one line and covers Use, operators, aggregates and calls.
- **G92b (b2).** Verified: the sites are `Place(node, input)`'s Value arm (Places.cs:34) and `Ref` (Values.cs:208-219, via
  `Into`'s Borrow row :84-86); no golden, library or milestone line has the form (grep). (b2) follows SPEC 15.6.7's
  "same call" sentence and removes G119's chain fact (38d). **N7:** state the fold's scope for a written exclusive borrow
  on the path of a *shared* input (`inspect(holder@uniq.items)`): today its `TwoPhaseUniq` is never activated (only operands activate) and acts as a `uniq` parent; folded it is the same as
  `inspect(holder.items)`. Consistent with "the same call", but say so in the DECISIONS entry.
- **G94.** Verified: `Element` Places.cs:211-233 forms nothing before the position; `Slice` Expansions.cs:340 borrows the
  backing after the boundaries; raw `p[n]` copies `p` after the position (Places.cs:222-226); a Slice element projects through
  the variable after the index (:213, :232). The handle copy is the §3 cause (below), so it belongs to that rule; G94 keeps
  the storage guard and the earlier backing. New cases 112 (`values[reset(values@uniq)]`, rejected at the activation against
  the guard) and 112b (`values[peek(values@ref)]`, valid). **N8:** give the guard the element access's problem subject, so a
  write into an uninitialized or partly Moved array is one A1 problem (`MirReport` merges by code and subject), not two;
  8 goldens have runtime indexes (cases, closures, collections, defaults, expansions, places, updates, values).
- **G91.** Verified: the walk has no repeat cut (Destruction.cs:68-122), Observer cuts payloads (:151), A4 dedups in
  `Destroy` (Solve.cs:407); `HoldsRaw` (Holding.cs:15-16, not MIR-B's ":175") has a depth bound only and one caller
  (Places.cs:160, so the scratch touches MirBuilder). `Enter` on reported parts keeps today's sets. **N9:** the composed-atom
  restart (Solve.cs:300, `Process` → `Destruction` at depth 0 per substituted atom) is a separate cause (the bound is not
  threaded through `Compose`); probing it in G91's unit is fine, but a hang becomes its own finding and commit.

## 4. §3: Copy operands read late — the same cause as G94's handle copy

Verified: a Place operand is formed as `Copy(place)` or `Move(place)` (Values.cs:177-180) and read only where its consumer is
built, after later operands' code: call receiver and arguments (Calls.cs:91-114, flushed at the terminator, :138/:168),
aggregates (Values.cs:264-267, :304), operators (Operators.cs:44-47), collection literals (Expansions.cs:411, appended at
:423), raw and Slice selections (Places.cs:213-232), and pair-layer arguments acquired at their Call (R4-M37 Open). So
`f(x, do { x = 5; 0 })`, `(x, do { x = 5; 0 })`, `x + do { x = 5; 0 }` and `f(x@move, do { x = …; 0 })` read the new x
(SPEC 12.2: "Operands are evaluated once, left to right"); the last also drops the old x early. This is wrong code at run
time, not only an analysis gap, and G94's Slice/raw receiver is one site. One builder rule, "an operand is read where it is
evaluated" (secure a Place operand into a temp when a later operand of the same operation builds code; the existing `Secure`,
Control.cs:176), owned by MirBuilder's operand formation, covers all sites. Recommendation: its own unit (PLAN G121) before
G94, taking G94's handle copy and deleting `Slice`'s own copy (Expansions.cs:266-272); measure MIR size and A1 time.

## 5. Order and dependencies

- Decisions first: owner §4 items 1–2; B1 and B2 recorded as errata.
- Parallel, no serialized MIR file: α (Binding, before G92a); G91 except its one MirBuilder line; A5 core development
  (`MirLoanSets`, `MirBorrows*`) with G119, G110, the own-value seed (B1), the Slice row and γ.
- Serialized MirBuilder lane (with R5 and the G96 session): G92b → G121 (§4) → G94 → G92a. G92a last: largest golden churn,
  needs α, and deletes the `Slice` gate G94 touches.
- 1b's joint core completes after G92b (38d, 38m, 38n need the fold), G94 (112 is an unsound acceptance without the guard)
  and G91 (recursive Types' Drops are deep `Unsupported_Kd` without it); G92a may follow the core in shadow (R4-M50) with
  its check-only errors listed as known gaps until it lands.
- Case 23a: C1 holds if the first-dereference kill lands with or after the core (G119 is in it); C2 per N3; C3/C4 are
  text conditions of its SPEC revision (§15.6.3, Appendix E, §13.5.5), which can go with P12's. B1 is base-independent.

## 6. Owner decisions beyond choice §4

None required. B1 and B2 align the design with adopted SPEC text and the choice's own contract (planning session,
DECISIONS). Report two SPEC gaps beside choice §5's: (a) SPEC 15.6's "also used" sentence makes 109 an error although Rust accepts
it (precision, no rule change); (b) whether a written exclusive borrow on a shared input's path is part of "one preparation"
(N7).

## 7. Non-blocking findings (open issues)

- N1–N9 above. Also: choice §1's G119 row cites 84, 85 correctly (rejected at the write under both bases); the "Slice index"
  rationale should cite element Copies (content layer) besides 42a–42d.
- The prototype models no Types, views or statics; G119's writeback and effect paths, G94's guard with G5 inputs and the
  operand rule are argued from the code, not run (H).
