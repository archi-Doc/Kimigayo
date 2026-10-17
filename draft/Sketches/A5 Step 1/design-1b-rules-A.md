# A5 step 1, 1b design diff, rule part: designer A (G119, 33c, G110)

Base: worktree `bin/worktrees/a5-step1` at `5c42b577`, read-only. Design A3 (`draft/Sketches/A5 Step 0/design-A3.md`), the
prototype's round 9 (`prototype.md`) and SPEC §15.6–§15.6.7, §8.11.3–§8.11.4 and §13.7.2. Files in this folder:
`proto.cs` (round 10), `run.txt` (its default run) and `work/` (scratch runs, including `runv.txt`, the `-v` run).
(H) marks an unverified claim.

## 0. Result

| Mode (140 cases: round 9's 113 plus 27 new) | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |
| --- | ---: | ---: | ---: | ---: | ---: |
| Design A, round 9 (113 cases) | 78 | 21 | 12 | 1 (33c) | 1 (36h) |
| **Design A, round 10** | 106 | 22 | 12 | **0** | **0** |
| **Design A with `FirstDerefBase`** | 112 | 21 | 7 | **0** | **0** |
| Design A with `PhantomParentsAtCall` | 106 | 22 | 12 | 0 | 0 |

- Of the 113 earlier cases, only 33c (now valid), 36h (now an error at the write) and 36e (the write is added to its
  error locations) change. All three move toward SPEC. For those 113 rows, `diff` of the design-A table against
  `proto-base/run.txt` shows only these three rows.
- **Encoding fix, no rule change.** A Call now reads its reservation holders, as A3's `use(H, ℓ)` already says
  (the operand `move $r` is a Move). Earlier cases left that use out where they named no `Use`, so a reservation
  holder looked dead inside its window. This fix alone changes no verdict on the 113 cases: they differ from round 9
  only in 33c, 36e and 36h, and `ExcuseAtCallOnly` restores 33c's round-9 verdict, `NoRouteReach` those of 36e and 36h.
- The exit code is 20, the number of S1′ failures. S1′ is unchanged and models neither G119 nor G110. It fails round 9's
  six cases (29a, 30c, 33c, 34c, 36f and 36h), 33e (an earlier error location) and the new 38a–38d, 38g, 38i, 38k, 39a–39c,
  39i, 39k and 39m.
- **Ablations**, each removing one round-10 rule from design A, and the cases each one changes:

  | Ablation | Cases that change |
  | --- | --- |
  | `NoRouteReach` | 36h, 38a–38d, 38g and 38i become **unsound**; 36e's error moves |
  | `NoChainRoute` | 38d becomes **unsound** |
  | `RouteAllParents` (option B) | rejects 38e (over-strict) |
  | `ExcuseAtCallOnly` | rejects 33c and moves 33e's error |
  | `LoanPastShared` | rejects 39a–39c, 39i, 39k and 39m (over-strict) |
  | `NoSharedCut` | **no change** (before G110 it rejected 27) |

## 1. G119: reservation routes (36h)

**Required meaning.** SPEC §15.6.7: the target "is evaluated, located and checked once, and its Place, owner and the
references it was reached through are protected: shared reads and Borrows may coexist; any write, Move, destruction,
reallocation or other exclusive operation on it is an error. A child reservation keeps its parent's authority."
§13.7.2 protects a writeback destination "like a call reservation". This governs the following forms, between a
reservation's formation and its activation:

- writes to a prefix of the reserved Place: `cur = b`, `cur.inner = s2`, `cur@follow = H(…)`, `arr[j] = …`;
- non-observing destructions of such a prefix;
- shallow destroying-call activations of such a prefix, such as `replace(cur@uniq, …)` (38g);
- the same forms along a G5 chain: `h@follow@uniq.c.bump(do { h = y@uniq; 1 })` (38d).

**What A3 lacks.** In `hits(A, X, k)`, a write or non-observing destroy reaches X only when no borrow Deref lies between
A and X. A reservation's Loan R on `cur.*.inner.*` is therefore unreachable from `cur = b`, which A3 accepts (36h).
Moves, borrows and observing destructions are deep, so they already reach R. A Loan reserved in its holder has mode
`ref`, so `conflicts` already admits reads and shared borrows. The only gap is reach.

**Rule** (A3 §5 additions; nothing else changes):

```text
// ---- Reservation routes (G119; SPEC 15.6.7, 13.7.2)
reservedIn(L, H)   :- L is TwoPhaseUniq and H is its reservation holder.   // exactly where mode(L, H) = ref
prep(R)            := place(R); if place(R) is rooted at the reservation holder of a TwoPhaseUniq site R′ and starts
                      with a Deref (MIR's G5 chain, DEC R4-M37), prep(R′) followed by place(R)'s steps after that Deref.
                      // pre-pass, once per TwoPhaseUniq site: `$a.*.c` with $a reserving h.* gives h.*.c
route(A, L, H)     :- reservedIn(L, H), within(prep(L), A).   // A is a prefix of prep(L), any borrow Derefs between
err(conflict,L,ℓ)  :- access(ℓ,X,k), L ∈ CS(ℓ) via H, real(L), ( hits(X, place(L), k) ; route(X, L, H) ),
                      conflicts(k, mode(L,H)), !excused(L,ℓ), !partition(L,ℓ,k,X).
err(activate,R,L,ℓ):- reserved(R,t,_,ℓ), L ∈ CS_call(ℓ,t) via H, real(L),
                      ( hits(place(R), place(L), uborrow) ; route(place(R), L, H) ),   // a destroying R: as write
                      conflicts(uborrow, mode(L,H)), !excused(L,ℓ).
```

- **Which accesses.** Every access, but `conflicts(k, ref)` keeps only exclusive kinds (write, Move, exclusive
  borrow, destroy and obs). Reads and shared borrows of the route coexist, as SPEC states (10a, 10c, 38h).
- **Against which Loans.** Only a Loan reserved in the holder that witnesses it. A3's mode rule already identifies
  that pair, and no Loan id is compared.
- **During which interval.** The reservation holder is live from the issue to its Call. The Call's operand Moves it,
  and on abandonment G6 places an `EndBorrow`. While the holder is in `live_out(ℓ)` (`CS`) or in `live_in(ℓ)`
  (`CS_call`), the reservation is pending. At the Call itself the holder is not live out, so the callee's own
  effects are not route-checked, while its activation is. After the activation, a retained R is in another holder
  with mode `uniq`, so `route` no longer applies (38f, 19a and 21a stay valid).
- **Where it is checked.** At the access, on `σ_in`, so before any `ended` the access causes. Under `FirstDerefBase`,
  `cur = b` ends R, and the check precedes the end (36h and 38a–38d err under both bases). Corollary: **a pending
  reservation is never ended silently.** Every in-place destruction that would end it, under either base, destroys
  a prefix of its Place, so it is a route write and an error.

**Owner.** `Mir/Analysis/MirBorrows.Checks.cs` holds the route clause in the conflict and activation rules.
`Mir/Analysis/MirBorrows.cs` derives `prep` in its pre-pass, beside `base(L, B)`, from one walk over the
TwoPhaseUniq Refs. `prep`'s root also counts in the pre-pass's per-root conflict flags.

- MIR contract to state with R4-M37 (H): each reservation temp has exactly one defining TwoPhaseUniq Ref, and is used
  only by its preparation (a child Ref) and its Call. The G5 builder makes this true today (not verified in
  `MirBuilder`).

**Options.**

| # | Option | Where | Verdicts (run) | Verdict |
| --- | --- | --- | --- | --- |
| 1 | Route clause for a Loan reserved in its holder, through `prep` | Checks + pre-pass | 140/140, no loss | **chosen** |
| 2 | Route clause for every Loan a pending reservation holder carries (`RouteAllParents`) | Checks | rejects 38e: writing `a` while `cur` (a Reborrow of `a.*`) is reserved, which SPEC accepts (a is not on the route) and Rust accepts | rejected: a new loss; it needs no chain fact |
| 3 | Shared structure: route pseudo-Loans, a `ref` Loan per route Place issued into the reservation holder | Loan representation | not run | rejected: a Loan kind that covers no referent (else P3 hits R itself), removed at the activation by id, else retained into results (`cursor = cursor.children[0]@uniq` would conflict) |
| 4 | Shared structure: the builder publishes each TwoPhaseUniq Ref's preparation Place (a `MirBody` column) | builder, Checks | same as 1 | fallback if the contract above cannot be stated: same check, chain resolved by the producer of G5 |
| 5 | Check at the activation: compare the route's values at formation and Call | Checks | not run | rejected: per-reservation snapshots; under `FirstDerefBase` the write has already ended R; SPEC errs at the write |

**Sites sharing the cause.** The cause: SPEC protects a route (prefixes across Derefs), while `hits` gives shallow
kinds no reach across a Deref. Search: every protection clause of SPEC §15.6–§15.7 and §13.7.2, against every
`hits(` use in A3 §5 (conflict, activate, destroy, effect, F6 disturbance).

| Site | Covered by | Change |
| --- | --- | --- |
| Call reservations (§15.6.7) | `route` in `err(conflict)` | this rule |
| Writeback reservations (§13.7.2) | `$w` is reserved in its holder | none beyond this rule (H: no route write can occur in a writeback window, since the right side is evaluated before locate) |
| A destroying-call activation of a route prefix (38g) | `route` in `err(activate)` | this rule |
| G5 chains (R4-M37) | `prep` | this rule (38d) |
| `err(destroy)` | obs is deep, and the shallow part is the activation | none |
| A4 effects | P5 keys are whole static Fields, so they already reach across Derefs | none |
| F6 disturbance | already compares `D ⊑ recv(c)` across Derefs | none (same `within`) |

**Becomes unnecessary.** No code. These records become unnecessary:

- the caveat in A3 §5's `desc(c)` comment ("R2 of recv(c) at ℓ agrees only while writes to the reservation's route
  are rejected");
- A3 §11's "Reservation route" risk;
- PLAN G119.

**Size and cost.** About +12 lines in Checks and +15 in the pre-pass. The clause is one more comparison per
(candidate access, scanned Loan) pair, inside the existing `Σ_{ℓ∈A★}` term of A3 §6.2. `prep` costs
Θ(TwoPhaseUniq sites × projection length), inside Θ(N + L). No new V16 term or scaling shape (shape 6 covers it).

**MIR-layer tests.**

- Rule tests: 36h and 36e; 38a–38k (route, owner, chain, alias, another name, index, destroying activation, after
  the activation, and the precision case 38e).
- SPEC §15.6.7's examples (`replace(p@uniq, with: p + 1)` and `tasks.insert(tasks.length, …)` valid,
  `tasks.append(tasks.remove(0))` an error) and §13.7.2's `x += x` (valid).
- A6 (1c): a route error whose witness is reserved in its holder maps to `CallReservationConflict_Kd`; at an
  activation (38g) to `CallActivationConflict_Kd`.

**`PhantomParentsAtCall`.** Under this rule it is sound on all 140 cases, with the same classes as `desc(c)`.

- **Why** (H, argued, not proved): with route writes rejected, `σ(root(recv(c)))` at the Call contains its value
  at the formation, except for `ended` and disturbance, which apply to both.
- **It does not change the choice.** `desc(c)` stays. It reads the operand, as every `ret(i, κ)` does (one path, no
  second R2 at the Call). After a route-write error it still reports 36e's independent `a.inner.clear()`, which SPEC
  §23 keeps visible and `PhantomParentsAtCall` loses.
- The only effect is that A3 §9 item 10's reason for rejecting it ("sound only while A3 rejects writes to a
  reservation's route") no longer holds. Its other reason, a second derivation, still does.

**Interactions.**

- **Case 23a proposal (first-dereference base).** Same verdicts on 36e, 36h and 38a–38k. The check runs before the
  end, as the proposal requires ("G119's check belongs at the write").
- **R4-M37's open chain refinement** (a parent reservation is `ref` in its child holder until the Call). If R4-M37
  also activates the chain parent at the Call, that activation must exclude the chain's holders. Otherwise the parent
  meets itself through the child's route (`prep(Rb) = h.*.c` lies under `h.*`). Not prototyped; A3's letter activates
  only the Call's operands, as encoded.

## 2. 33c: the excuse at c2's argument reservations

**Required meaning.** SPEC §8.11.3 accepts `takeTwo`. §15.6.4: "Protection begins when a Borrow is formed … and lasts
through the whole call", so c2's receiver reservation is part of c2. §8.11.4 rule 3 excludes the Loans kept by
earlier results of the same requirement on the same value "at callers". Forms: every argument reservation of a
`preserves results` call that is excused against `Φ_c1`.

**What was missing.** A3 §5 has the rule, the second `excused` row from the round-7 erratum, but the prototype
stated it by hand at 33a's formation node, and 33c recorded "A3's letter before the erratum". So 33c tested a
superseded rule, not a program.

**Rule** (encoded, design A mode). It is derived from facts A3 already has:

```text
excused(L, ℓ1) :- reserved(R, t, ℓ1, ℓ2), Call c2 at ℓ2, excused(L, ℓ2).   // only at the formation; not between
```

**Decision: encode the erratum, keep 33c's expectation (valid).**

- 33a keeps the formation excuse stated, and 33c states only the Call's excuse. Both pass, so 33c is now the
  regression for the derivation.
- `ExcuseAtCallOnly` reproduces round 7, rejecting 33c at `$r2`.
- New 33d: a reservation of a call that is not excused (`source.other()`). It is still an error at the formation
  and the Call, so the derivation never over-excuses.
- New 33e: a disturbance inside c2's argument (`source.takeWith(do { source@follow = fresh@move; 0 })`). The excuse
  holds at the formation only, while the write errs (through `Φ_c1` and the G119 route) and `Φ_c1^dist` errs at the
  Call.

**Options.**

| # | Option | Verdict |
| --- | --- | --- |
| 1 | Derive the excuse from `reserved` (above) | **chosen**: +3 lines in `MirBorrows.Checks.cs` (F6, unit 2f), no cost term |
| 2 | Redefine 33c's expectation as an error | rejected: SPEC accepts `takeTwo`, and 33c is the same program |
| 3 | Shared structure: make a reservation's formation no access, checking it only at the activation | rejected: SPEC checks the target at formation ("checked once"); (H, not run) it would move errors such as 10b's to activations and miss conflicts formed inside the window |

**Sites sharing the cause.** Only F6's `excused`. The excuse is S2's identity-based exclusion, which SPEC §8.11.4
itself states, not an ordinary Loan exclusion.

**MIR-layer tests:** 33a–33e, `takeTwo` and `takeAcross` (unit 2f).

## 3. G110: Borrows past a shared Deref (S1, SPEC §15.6.3)

**Required meaning.** SPEC §15.6.3: "A Borrow past a shared dereference forms no Loan of its own; it derives only from
the reference of its last shared dereference and the references it dereferences after that one, and carries their
Loans."

- Forms: written `@ref`, and the implicit shared Borrows (a `ref/Self` receiver, a comparison inspection, a shared
  capture and a `ref/T` adaptation), whose Place has a `ref`, `objref` or ref-case pair layer (per view,
  `MirCases.Layer`) among its borrow Derefs.
- This is Rust's rule: borrows behind shared references are not tracked.

**What A3 lacks.** A3's `issued` forms a Loan at every non-raw Ref, and R2 adds the parents of every followed layer.
The Loan's place then crosses a shared Deref, and P3's deep Move or Borrow meets it. A3 thereby rejects SPEC-valid
programs, which it never listed as losses:

- 39a, PLAN G110's case, the deep Move `let t = s@move`;
- 39i, the ref-case pair layer;
- 39k, an exclusive argument beside it;
- 39c and 39m, the Loan of an exclusive parent kept by the derived value (`p`'s Loan on `s` then blocks `s.n = 1`).

**Rule** (A3 §5 `issued`, replacing the R2 line). Let k be X's borrow Derefs and sh the 1-based index of its last
shared one, or 0 when it has none; sh comes from the pre-pass with `base(L, B)`, from the same step classes.

```text
issued(L,X,_,ℓ,H), sh = 0  ⇒  σ_out(H) = {L} ∪ ⋃_{0 ≤ j < k} content^j(σ_in(root(X)))       // unchanged
Ref X into H at ℓ, sh > 0  ⇒  σ_out(H) = ⋃_{sh−1 ≤ j < k} content^j(σ_in(root(X)))           // no issued row, no L
// The Ref is still an access (sborrow of X) and a use of root(X).
```

- Layer j's reference is the value read through j Derefs, so its Loans are `content^j(σ(root))` (P10).
- Such a Borrow carries what a Copy of the shared reference carries (`s.r@follow@ref` ≡ `let c = s.r`;
  `p.r@follow@ref` ≡ `p@follow.r`), plus the references after it. S1's soundness argument therefore applies: past a
  shared reference only reads are possible, and the shared reference's own Loans freeze what it reaches.

**What the other rules then see.**

- **P3.** A Move or Borrow of `s` reaches no Loan through `s.r`, because none is formed there. The derived value is
  protected by `s.r`'s own Loans, which survive the Move (39b: `x = 5` after `s@move` errs). P3 (a) and (b) are
  unchanged: `let z = r@ref` borrows `r` itself.
- **`obs`'s shared cut.** No formed Loan's place contains a shared Deref any more, so `throughShared` is vacuous.
  `NoSharedCut` changes nothing on the 140 cases (before G110 it rejected 27). **Deletable**, on one condition (H,
  unit 2f): F6 must place no `Φ_c` past a shared Deref. A `ref/Self` receiver reached through a shared layer would
  follow the same S1 rule, with `desc(c)` carrying the derived set.
- **Region containment (§7).** Unchanged. The derivation cut is §15.6.3's containment cut ("up to and including the
  first shared layer", counted outward from the parent), so a derived value carries exactly the Loans of the
  references whose Origins contain its Origin. Wording fix: A3 §7's "the same cut as obs" names the wrong cut, since
  obs counts inward from the destroyed Place; after G110 it should say "S1's derivation cut".
- **The activation rule.** Unchanged. A shared argument past a shared Deref carries only derived Loans, so
  `g(s@uniq, s.r@follow@ref)` is valid (39k).
  - (H) This reads §15.6.7's "overlapping arguments … one shared and one exclusive" through the Loans (§15.6:
    "checked against the values live into its call"). It is the same program as `g(s@uniq, s.r)`.
  - Rust accepts the two-phase method form `s.m(&*s.r)`.
- **`ended` and bases.** No base is needed for these sites, and `FirstDerefBase` gives identical verdicts on 39a–39n.
  The case 23a proposal already says "a Borrow past a shared reference follows S1's existing rule".
- **P1.** The derived set reads whole holders. 39c's `v` carries every Loan of `s` (P1, listed); this adds no loss.

**Owner.** The pre-pass in `MirBorrows.cs` (sh per Ref site and view, beside `base`) and the issue transfer in
`MirBorrows.Flow.cs`.

**Options.**

| # | Option | Verdict |
| --- | --- | --- |
| 1 | No Loan past a shared Deref; parents from layer sh−1 on | **chosen**: SPEC's text; 140/140; fewer Loans |
| 2 | Shared structure: keep the Loan, but stop every deep kind at shared Derefs (extend obs's cut to Move and Borrow) | rejected: R2 still adds every parent, so 39c and 39m stay over-strict (`p`'s Loan on `s`); Loans no rule needs |
| 3 | Re-anchor the Loan on the shared referent's own Place (resolve `s.r.*` to `x`) | rejected: access resolution through stored references, which A3 rejected (§3) |
| 4 | List the form as a loss (owner) | not needed: option 1 is sound and smaller |

**Sites sharing the cause.** Search: every rule that reads `issued`, R2 or `throughShared` in A3 §5.

| Rule | Change |
| --- | --- |
| R2 parent materialization | the same line, changed |
| `hits`' obs clause | becomes vacuous; delete under the F6 condition |
| §7 containment | wording only |
| `err(ended)` for guards | a guard local bound past a shared Deref has no issued Loan, so nothing to end (H, the Origin stratum still contains it) |
| `desc(c)` for a shared receiver past a shared Deref | no R_c to remove (unit 2f) |

**Becomes unnecessary.** The `throughShared` clause in `hits`, under the F6 condition. Also these sites' Loans and
their `ended` steps (27 no longer depends on StorageDead ending `L2`).

**Size and cost.** +8 lines in Flow (the branch) and +4 in the pre-pass (sh), −6 in Checks (`throughShared`): net
≈ +6. L shrinks and R2's unions get shorter. No new V16 term.

**MIR-layer tests.**

- Rule tests: 39a–39n, 27, 13d and 13e.
- S1 §2.1's example (39n: `e = q.p@follow@ref`, then `t = [4]` errs).
- S1 §10's `h.link.x@ref` with `h.link = o` (valid, 39e's shape).
- Per view: 39i (ref case, valid) against 39j (uniq case, an error at the Move).

## 4. New cases (SPEC verdicts; design A and `FirstDerefBase` agree on each)

| Case | Program | SPEC | Basis |
| --- | --- | --- | --- |
| 38a | `replace(r@follow@uniq, with: do { r = y@uniq; 5 })` | error at the write | §15.6.7: r is a reference the target was reached through |
| 38b | `first = cur.inner.takeWith(do { cur.inner = s2; 0 })` | error at the write | §15.6.7: the Place holding the reached reference |
| 38c | `… takeWith(do { cur@follow = H(inner: s2); 0 })` | error at the write | §15.6.7: the owner |
| 38d | `h@follow@uniq.c.bump(do { h = y@uniq; 1 })` | error at the write | §15.6.7: one preparation, even through written exclusive borrows |
| 38e | `var cur = a; first = cur.inner.takeWith(do { a = b; 0 })` | valid | `a` is not on the route; `a = b` ends `cur`'s child Loan (§15.6.3), a shallow write |
| 38f | 21a with indexUniq's receiver reservation and Call | valid | §15.6.3 `deepest`; the reservation ended at its Call |
| 38g | `… takeWith(do { replace(cur@uniq, with: b); 0 })` (review R1) | error at the replace | §15.6.7: a destruction of the route |
| 38h | `replace(arr[0]@follow@uniq, with: do { arr[1] = z@uniq; 5 })` | valid | §15.6.2: distinct constant indices |
| 38i | the same with `arr[i]` and `arr[j]` | error at the write | §15.6.2: other indices overlap |
| 38j | `let pc = cur@uniq; first = cur.inner.takeWith(do { pc@follow = b; 0 })` | error at the reservation | §15.6.2: reading through `cur` while `pc` is live |
| 38k | `var rr = hh@uniq; first = rr.inner.takeWith(do { hh.inner = s2; 0 })` | error at the write | §15.6.3 parent Loans; route by another name is covered by R2 (A errs through `rr`'s Loan) |
| 33d | `first = source.take(); n = source.other()` | error | §8.11.4 rule 3 covers the same requirement only |
| 33e | `first = source.take(); second = source.takeWith(do { source@follow = fresh@move; 0 })` | error at the write and the Call | §15.6.7; §8.11.3: replacing `v` is outside the guarantee |
| 39a | `let v = s.r@follow@ref; let t = s@move; inspect(v)` (`r: ref/i32`) | valid | §15.6.3 (S1 §9, PLAN G110) |
| 39b | 39a, then `x = 5` | error at `x = 5` | v carries `s.r`'s Loan |
| 39c | `var p = s@uniq; let v = p.r@follow@ref; s.n = 1; inspect(v)` | valid | §15.6.3: only the last shared layer's Loans; p is dead |
| 39d | 39c with `x = 5` | error at `x = 5` | as 39b |
| 39e | `let v = s.r@follow@ref; s.r = y@ref; inspect(v)` | valid | a write of the stored shared reference ends nothing v needs |
| 39f | 39e, then `x = 5` | error at `x = 5` | §15.6: a write ends none of v's Loans |
| 39g | `let o = item@objref; let v = o.name@ref; o = other@objref; inspect(v)` | valid | §15.6.2: objref is shared |
| 39h | the same with `let t = item@move` | error at the Move | §13.5.5: v keeps o's Loan on the handle |
| 39i | `p: s/i32`, ref view: `let v = s.p@follow@ref; let t = s@move` | valid in this view | §15.6.1, §8.15: per Semantics case |
| 39j | the same, uniq view | error at the Move | §15.6.3: a shared child's prefix is Moved |
| 39k | `g(s@uniq, s.r@follow@ref)` | valid (H) | §15.6.3, §15.6 activation sentence; see §3 |
| 39l | `rs: ref/(uniq/i32)` under `var p = s@uniq`: `let v = p.rs@follow@follow@ref; slot@follow = 5` | error at the write | v carries rs's Loan on slot |
| 39m | 39l with `s.rs = o@ref` | valid | p's Loan is not kept |
| 39n | S1 §2.1: `let q = s@ref; let e = q.p@follow@ref; t = [4]; inspect(e@follow)` | error at `t = [4]` | S1 §2.1, SPEC §15.6.3 |

No earlier expectation changed. 33c keeps "valid", and its title now says the formation excuse is derived.

## 5. Decisions for the planning session or the owner

1. **`prep(R)` as a new derived fact** (G119's chain; DECISIONS at 1b). Options:
   - (a) A5's pre-pass derives it from the reservation temp's defining Ref, with the single-definition builder
     contract added to R4-M37 (**recommended**);
   - (b) the builder publishes it;
   - (c) without a chain fact, `RouteAllParents`, which needs an owner-approved loss (38e).

   R4-M37's open chain refinement should read the same fact.
2. **39k's SPEC reading (H).** If the owner reads §15.6.7's "overlapping arguments" on Places rather than on Loans,
   39k becomes an error, and only an activation-specific clause could give it. That clause would be a symptom fix
   against S1's "forms no Loan". I recommend the Loan reading, and noting it in SPEC §15.6.7 with P12's revision.
3. **Deleting `throughShared`** waits for unit 2f to fix where `Φ_c` sits for a shared receiver past a shared Deref.
4. **No new loss.** The three rules add errors only where SPEC errs, and remove A3's unlisted over-strictness for
   G110. Unit 0b's condition (no unsound acceptance, no unlisted rejection) holds under both bases.

## 6. Unverified

- (H) The MIR builder gives each reservation temp one defining Ref and no use after its Call (G5/G6). Not checked
  in `MirBuilder`.
- (H) `PhantomParentsAtCall`'s soundness under G119 is argued from monotone growth of `σ(root)` in the window,
  shown on 140 cases, not proved.
- (H) A writeback window contains no route write: right sides are evaluated before locate (§13.7.2 step order).
- The chain parent's own activation (R4-M37) is not prototyped. Pair layers are modeled as their view's Deref class
  only.
