# G160 plan: independent review, round 3

Reviewer: the reviewer of rounds 1 and 2; author of none of the designs. Scope:

- the rewritten plan (635 lines) in worktree `bin/worktrees/g160-plan`, base `59b78023`, uncommitted;
- the G160 row in PLAN;
- the INTEGRATED entry.

Read: §0–§11; `MirBorrows.Places.cs` (`StepClass`, `Deref`, `Walk`, `Through`); `.Loans.cs` (`Issue`, E3); `.Checks.cs` (`Hits`);
`.Values.cs` (`Read`, `Issued`, `Content`, `Refs`, `Writes`); `.Flow.cs` (`Store`); `.Solve.cs` (`Holders`);
`Mir/Build/MirBuilder.Selections.cs` (`Element`); `MirBuilder.Places.cs` (`Layer`); `MirBuildTest.AnOperandIsReadWhereItIsEvaluated`.

New probes, on the dev `a1677549` binary through the same harness, are in `scratchpad/g160/review3/t1.kimi`, `t2.kimi` and
`t3.kimi`, with their `.out`/`.log` files:

| Probe | Shape | Today |
| --- | --- | --- |
| t1 `rawReadOfHolder`, `rawReadOfHolderIdx` | `var w = y@ref; let p = w@raw; q = *p` (or `p[0]`); `y = 5; inspect(q)` | accepted (safe twin `q = w`: error) |
| t1 `derefKill` | `storeK(b@uniq, x@ref)` into `KB<T>{p: raw/T}`; `q = *b.p`; `b` replaced; `x = 5; inspect(q)` | accepted |
| t2 `phiLoss2` | `KB2<T> {a} {p: raw/T, r: ref/i32 during a}` holding `y@ref`; `Raw.initialize(b.p, cell2[0]@uniq)`; `put(*b.p, x@ref)`; `x = 5` | error `x = 5` |
| t3 `viewDirect` | a user `View<T> {s} {data: raw/T, loan: Loan<uniq/T during s>}` over `w@uniq` (design A's p4 shape); `*v.data = x@ref; x = 5; inspect(w)` | error `x = 5` |

## Round-2 findings

| Finding | Status | Where |
| --- | --- | --- |
| R2-B1 `Concrete` | Resolved. `Concrete` is restored, with `via self` pointees counted. Past a raw layer, an instance gives `Bare`/`Unowned` iff `Concrete`, and its arguments are walked past the raw layer. The HolderR, BoxH and Wrap/BoxW rows are right. | §4.1, §4.2 |
| R2-B2 `via self` holder eligibility | Resolved. The guard walk walks a `via self` pointee as the keeper's own storage, decided per Field. The minimal core has holder eligibility, slot properties and non-Copy. | §1.3, §4.2 |
| N1–N8 | Adopted. The K′ layers, Q1's scope, G160-2's process, references, the `via loan` row, projections and records are all in. | |

The Own-step semantics in themselves look sound, for `*b.p` as written:
- a direct Loan through a `via self` Deref conflicts with replacing, moving or destroying the keeper, and with writing its
  pointer Field (`Hits` meets no stop class), as an owning handle's payload does;
- disjoint Fields stay disjoint;
- a Shared `via loan` step issues no Loan (E3) and carries σ(k) as parents;
- a Uniq step's Store reaches refs(σ(k)), the owner through the marker.

The problems are in the scope and the inputs of the classification.

## Blocking findings

### R3-B1. Raw indexing never meets a kept Field, so §4.4's classification misses `p[n]`, the form every shape uses

- **Location.** §4.4: "A raw Deref whose pointer is a kept Field of the enclosing struct (by `Keeping`, read from the step's
  prefix)". The §1.2 rows that depend on it, and §7's read-side and `keptDirectDead` tests.
- **Problem.** The MIR builder lowers a raw element `p[n]` (SPEC 5.3: `*(p + n)`) through a pointer temporary:
  `offset := Add(Copy(sequence), n); Project(offset, Deref, 0)` (`MirBuilder.Selections.cs:36-42`). This includes `n = 0`, and the
  receiver is read by value (`AnOperandIsReadWhereItIsEvaluated`, row `p[jump(p@uniq)]`). So for `b.p[0]`, `buf.data[0]`,
  `r.p[0]` and `v.data[0]`, the Deref's prefix is a raw temporary, not the Field. Only an explicit `*b.p` (`MirBuilder.Places.cs:73`,
  `Layer`) keeps the Field as the prefix. Every kept step the plan relies on therefore stays Raw:
  - **Unsound acceptance under X-decl.** `bufferReadKill` (`buf.data[0]`) stays accepted: `push` stores into the Buffer's
    holder, and the in-body read gets {Φ_E}. SPEC's own `get` is written `self.data[index]`. §1.2 claims an error.
  - **Conservative Language error.** `keptDirectDead` with `via self` (`b.p[0] = x@ref`) stays an error (E), although §1.2 says
    the revised SPEC makes it valid (AGENTS Diagnostics).
  - **Tests.** The borrow test (`r = b.p[0]@ref`) and `keptReadRef`/`keptReadUniq` (`r.p[0]`) do not change through the kept
    step. Under X-decl they err only because the undeclared `KeptBox` sends the call's store to E.
  - **Earlier rounds.** The defect predates this round: the round-1 and round-2 "kept step" tests keyed on the Field too, and
    review rounds 1 and 2 missed it.
- **Evidence.** The builder code cited above. t1 `rawReadOfHolderIdx` and the existing probes show indexing behaves exactly like
  `*p` today. A kept classification by prefix can only see `*b.p` (t1 `derefKill`).
- **Correction.** Decide how kept-ness follows `p + n`. SPEC 5.3 says the result keeps the provenance, so `b.p[i]` is an access
  through the keeper, not an extraction. Options:
  - (i) The MIR builder publishes a raw element as a projection of the pointer's Place, an `Index` on a raw prefix classed like a
    raw Deref, instead of a temporary. This is a MIR form change shared with R5: builder, `MirValidator`, MIR codegen and MirInit
    (which already treats an `Index` on a raw prefix as by-value). It takes the full process and a unit in §9.
  - (ii) The builder publishes on the offset temporary the Place it came from.

  A5 recognising `Add(Copy(field), n)` by shape is not an option: that is a consumer deciding by syntax what the builder owns.
  Then restate which pointer arithmetic counts as extraction, under minimal-core item 5 and K′ sentence 3, and re-derive §1.2 and §7.

### R3-B2. Reads and borrows through a kept step drop {Φ_E}, which W needs; §4.4's last paragraph covers Loans, not Φ_E

- **Location.** §4.4 ("Reads through a kept step need no {Φ_E} … already in E, which is live at every point"), both classes.
- **Problem.** E being live at every point makes Loans stored there conflict anywhere, but Φ_E also steers W:
  - `refs` adds E only where a set carries Φ_E or no direct Loan;
  - S1-a: storage the body does not own "follows the holder as Loans do".

  Initializing kept storage in-body always goes through an extracted pointer (`Raw.initialize(b.p, v)`, or `b.p + i`). So a value
  that reaches E is joined into E, not into σ(b). A later `*b.p` read or reborrow (Own) yields σ(b) without Φ_E. If σ(b) holds a
  direct Loan, W through that value has no E.
  - t2 `phiLoss2`: `KB2` holds `y@ref` in a Field; a borrow of raw storage is initialized into its kept `p`; then
    `put(*b.p, x@ref)` writes x's reference into raw storage. Today `x = 5` is an error. With `KB2` declared `via self`, the
    reborrow's parents are σ(b) = {Loan on y}, y is no holder, and no E is added, so x's Loan lands nowhere and `x = 5` is accepted.
  - The same holds for a `via loan` step: `Content(σ(k))` omits E's state when k carries direct Loans, the marker included.
- **Evidence.** t2 output; `Values.cs` `Refs`/`Content` (`escaped || !direct`); S1-a (R4-M76).
- **Correction.** A kept step adds {Φ_E} to the value read and to Issued's parents, as round 2 did. Then Read and Issued must know
  the step is kept, by a dedicated class or a flag on the step, so §4.4's "`Read`, `Issued` … are unchanged" no longer holds. State
  the rule and add t2's shape (with the declaration) to §7.

### R3-B3. K′'s Own class makes a user view with an exclusive marker unsound, and §1.2 misstates the form as library-only

- **Location.** §4.1 (K′ kept layers "all with form self"); §4.4 (`via self` → Own); the §1.2 row "In-body store through an
  exclusive `via loan` Field", K′ cell: "reaches the keeper only … (only library-internal Fields have the form)"; §1.3 K′ sentence 2.
- **Problem.** K′ sentence 2 allows a kept layer to address "storage whose owner's Loan it keeps in a `Loan<T>` Field (`uniq`
  for writes)". A user `View<T> {s}` (t3, design A's `p4`) meets it and is kept under K′, so `*v.data` becomes an Own step. Then:
  - `*v.data = x@ref` joins only v, while the storage is w's;
  - `x = 5; inspect(w)` (an error today) is accepted;
  - a read `*v.data` misses σ(w), which only a borrow-class step reaches;
  - K′ sentences 1 and 3 then charge w's own safe access as the breach, which is incoherent.

  The form is not library-only; t3 is a user program.
- **Evidence.** t3 output; `Flow.cs` `Store` (no borrow step: a direct part of the root is joined).
- **Correction.** Choose one:
  - Under K′, restrict sentence 2 to storage the value owns, dropping the marker alternative, so the View's write breaks the
    writer's obligation.
  - Under K′, class a kept step as a borrow Deref through the keeper's state, which reaches the owner through the marker.

  In either case, rewrite the K′ cell and Q1's K′ description. X-decl, the recommended answer, is unaffected.

## Non-blocking findings

- **N1. A related unsound acceptance with another cause: raw reads of a safe holder's storage.** t1 `rawReadOfHolder` and
  `rawReadOfHolderIdx` are accepted; their safe twin is rejected. A Raw step's read gives {Φ_E}, and E's state lacks σ(w).
  - SPEC 15:454 says "A read through a reference carries the Loans of what it reaches", and raw is a reference. 5.2.1 makes the
    unsafe read itself valid (authority, no conflicting Loan), so the borrow violation is in safe code.
  - No design here represents an `@raw` address's holder (`@raw` leaves no Loan), so this is a separate cause.
  - Open a PLAN row and list it in §5 as "related, another cause".
- **N2. Keeping's DFS bullets.** Add "in before-raw, a concrete Loan-capable part sets `Concrete`". The definition implies it,
  but the bullets list only the past-raw rule.
- **N3. Own-step verdict changes.** Under `via self`, a borrow through `*b.p` now conflicts with replacing, moving or destroying
  b and with writing `b.p` (today Raw stops `Hits`). This follows from `via self` ownership, but it is a verdict change: list it
  and test one, for example `let r = (*b.p)@ref; b = KB.init(…); inspect(r)` as an error.
- **N4. `via loan` borrows.** A Shared `via loan` step issues no Loan (E3) and carries σ(k). Add a test so a later change of E3
  cannot silently change this.
- **N5. Units.** If R3-B1's correction (i) is chosen, §9 needs a builder unit coordinated with R5, the owner of `MirBuilder` and
  `MirValidator`, before G160-3.

## Checks with no finding

- `Keeping`'s `Concrete`/`Bare`/`BareParams` fixed point is monotone.
- The Unowned walk reads no Fields and is sound for body-supplied Type arguments, through `BareParams` and argument walks.
- Rules 2 and 3 of W and their P13 composition are sound.
- The X-decl minimal-core scope and costs are stated fairly.
- The unit order G160-0 → G160-1 → G160-2 → G160-3 holds, apart from N5.
- The PLAN row states facts, and the INTEGRATED entry follows the convention.

## Verdict

**Reject (blocking findings R3-B1, R3-B2 and R3-B3).**

- R3-B1 is the main one: the declaration-classed step never meets `p[n]`, because MIR indexes raw storage through a temporary.
  The read-side fix and `keptDirectDead` do not take effect, `bufferReadKill` stays unsoundly accepted under the recommended
  reading, and the fix needs a MIR publication decision shared with R5.
- R3-B2 and R3-B3 are local:
  - restore {Φ_E} on kept steps;
  - repair K′'s classing of views or its sentence 2.
