# A5 step 1, design 1b rules (designer B): G119, 33c, G110

Base: worktree `bin/worktrees/a5-step1` at `5c42b577` (read only). Design A3 (`draft/Sketches/A5 Step 0/design-A3.md`)
is the frame; `S15:n` is a line of `docs/spec/15-ownership-and-lifetime-analysis.md`, `S8:n` of chapter 8. Evidence:
`proto.cs` here (round 9 prototype plus round 10), run outside the repository; `run.txt` is its final output. (H) marks
an unverified claim. Nothing in the repository was edited or built.

## 0. Summary

| Item | Rule (A3 terms) | Owner (A3 §8) | Net lines | Run (design A / FirstDerefBase) |
| --- | --- | --- | ---: | --- |
| G119 (36h) | Inside a reservation's window, a write or destruction of a prefix of its Place, across borrow Derefs, reaches the reserved Loan in its holder | `MirBorrows.Checks` (`hits`) | +4 | 36h, 38a, 38b, 38g rejected at the write; 38c–38f, 38h valid |
| 33c | A3 §5's second `excused` row, derived from `reserved`: the formations of the reservations an excused Call activates are excused | `MirBorrows.Checks` (S2) | +3 | 33a, 33c valid; 33b error |
| G110 | A Ref past a shared Deref forms no Loan; its value carries the layers from its last shared Deref on; the obs cut is deleted | step classes (`MirBorrows.Checks`), pre-pass (`MirBorrows.cs`), `MirBorrows.Flow`, `MirOrigins` | ≈0 | 39a, 39c, 39f, 39h, 39l valid; 39b, 39d, 39g, 39i–39k, 39m, 39n, 39p rejected |
| New: 39o | The closure of `CS(ℓ)` is also seeded by `σ_in(T)` for a direct Loan formed at ℓ into a live holder, naming T | `MirBorrows.Checks` (check sets) | +3 | 39o rejected at the formation; 39q valid |

Final run, 138 cases (113 + 25 new): **design A: 12 listed loss, 105 same, 21 same verdict, error moves**;
**FirstDerefBase: 7 listed loss, 111 same, 20 same verdict, error moves**. No unlisted rejection, no unsound acceptance
in either. Of the 113 earlier cases only 33c (now valid), 36e (adds the write) and 36h (now rejected at the write)
change in design A. The exit code (16) still counts the frozen S1′ mode, which has none of these rules (§5).

## 1. G119: a reservation's route (36h)

**Required meaning.** S15:663: during a call reservation, "its Place, owner and the references it was reached through
are protected: shared reads and Borrows may coexist; any write, Move, destruction, reallocation or other exclusive
operation on it is an error". The forms: every TwoPhaseUniq of an invocation's receiver or argument (G5, R4-M37) and the
writeback reservation of a compound update (S13:595, "protected like a call reservation"). The window runs from the
formation to the activation; the Call's own destination write follows the activation.

**What A3 lacks.** A reserved Loan R has mode `ref` in its reservation holder t. Its route (the Places holding the
references the target was reached through) are the prefixes of `place(R)` at borrow Derefs: for `cur.*.inner.*`, `cur`,
`cur.*` and `cur.*.inner`. A Move, an exclusive Borrow and an observing destruction of such a prefix already reach R
(deep, P3 and §15.6.6), and `conflicts(k, ref)` holds for them. A write and a plain destruction are shallow
(`hits`: `!through`), so `cur = b`, `cur@follow = …`, `cur.inner = …` and `refs[j] = …` miss R. Inline prefixes (the
owner without a Deref, `holder` for `holder.items`) already hit R. So the gap is exactly the shallow kinds across a
borrow Deref, inside the window.

**Rule** (A3 §5, `hits` gains one disjunct; `reserved` is A3's existing derived fact):

```text
window(R, t, ℓ)       :- reserved(R, t, ℓ1, ℓ2), ℓ1 < ℓ < ℓ2.       // strictly: the Call's destination write is after ℓ2's activation
hits(A, X, k; L, H, ℓ) :- within(A, X)
                       ;  within(X, A), ( !through(X, A) ; deep(k)
                                        ; k ∈ {write, destroy} ∧ window(L, H, ℓ) ).   // S15:663: the route of a reservation
```

A TwoPhaseUniq is never formed past a shared Deref (exclusive access), so every Deref of `place(R)` is exclusive and
"a prefix across borrow Derefs" is exactly the Place, its owners and its route references. Checked at the write, on
`σ_in` (A3's `CS`), with `conflicts(write, ref)`; the problem is A3 §5's `CallReservationConflict_Kd` row (a TwoPhaseUniq
Loan in its reservation holder). Not deferred to the activation.

**Why it is complete (argued).** Aliases of a route Place are covered by the existing rules: an alias formed before ℓ1
and used in the window is a Loan on a route prefix that R's deep formation hits (P3); an exclusive alias formed in the
window is a deep `uborrow` that conflicts with R; a shared alias cannot write; a write to a route holder by another name
meets the direct parent Loan that R2 put into t (`a.*.inner = …` against L0, 36e's clear); parameters are disjoint by
the caller's activation. Indexes use `within`'s rules: `refs[j]` meets a route through `refs[i]` (38g), distinct
constants do not (38h).

**Options.**

| Option | Where | Verdict |
| --- | --- | --- |
| A. The `hits` disjunct above | inside the owner, `MirBorrows.Checks` | **Chosen**: no new state; `reserved` and the per-holder scan exist (A3 computes `mode(L, H)` from the same window) |
| B. Route Loans: at formation, issue a `ref` phantom per route prefix into t, so a shallow write hits it as a prefix | shared structure (reservation representation) | Rejected: k Loans per reservation, removed again at activation and kept out of retention; a second representation of one window |
| C. A builder column listing each TwoPhaseUniq's route Places | shared structure (MIR fact) | Rejected: a copy of what `place(R)`'s Deref positions already state |
| D. Check at activation that the route holders were not rewritten | owner, deferred | Rejected: SPEC errs at the write; under the first-Deref base the write ends R before the activation (FirstDerefBase); needs state per reservation |

**Sites sharing the cause.** Every shallow access kind (`write`, `destroy`) against a reserved Loan: assignments,
writes through references (P10's write is checked on its syntactic Place), StorageDead and non-observing Drops. Move,
`uborrow` and `obs` already reach (deep). Effects (`err(effect)`) compare by static root, so they already reach. The
writeback reservation uses the same `reserved` fact, so one rule serves calls and compound updates (S13:595).

**What becomes unnecessary.** PLAN G119; A3 §11 "Reservation route"; the caveat in A3 §5's `desc(c)` comment and §9
item 10 ("R2 of recv(c) at the call: sound only while A3 rejects writes to a reservation's route, which it does not
yet") — the rule now holds.

**Size and cost.** `MirBorrows.Checks` +4 lines. At each candidate access the scan already visits (L, H) and tests
`mode(L, H)` against the window; the disjunct is O(1) per visited pair. V16 (A3 §6.2) gains no term.

**First-Deref base (23a proposal).** Under that base a route write would end R (its base is the holder before the first
Deref, a route Place). The check reads `σ_in`, so the write is rejected before `step` ends R (FirstDerefBase column:
36h, 38a, 38b, 38g rejected at the write). In an accepted program no route Place is destroyed in place in the window, so
R is never ended in its window under either base (argued; G119 makes the proposal's §2 "G119's check belongs at the
write" hold).

**`PhantomParentsAtCall`.** It no longer accepts anything: 36e is rejected at the write (`run.txt`, "Design A with
PhantomParentsAtCall": only 36e, now an error at the write). In an accepted program the route holders are not
rewritten in the window and their sets only grow (writes add, `ended` removes from t as well), so R2 of `recv(c)` at
the Call contains `val(a_r) \ {R_c}` (argued). It does not matter for the design: `desc(c)` reads the Call's operand as
every `ret(i, κ)` does, needs no recomputation at ℓ2, does not depend on G119, and is at least as precise (H: a
Loan-carrying write through a route reference in the window would add Loans to R2-at-the-Call only).

**Verdicts.** 36h is rejected at `cur = b` only; 36e gains that error. Unchanged under both bases: 10a
(`replace(p@uniq, with: p + 1)`: a read in the window), 10b–10d, 19a/21a (no reservation on the step), 28a–28d, 31a/31b,
33a/33b, 36a–36d, 36f, 36g, and every other earlier case.

**Tests (MIR layer, unit 1b; no F6 needed).** A concrete receiver call with a `do` argument: rejected at the write for
`cur = b`, `cur@follow = …`, `cur.inner = …`, `refs[j] = …`; valid for a sibling Field, a shared Borrow of the route,
a reservation through a reborrow `cur2` while `cur` is written (38e), distinct constant indexes (38h), and
`node = node.next()` (38f, the destination write at the Call). 36e/36h with `takeWith` move to unit 2f's F6 tests.

## 2. 33c: the excuse at c2's reservations

**Required meaning.** S8:774 and S8:791: a `preserves results` call of r on v conflicts with no Loan kept by an earlier
result of r on v; the excluded effects include the call's borrows. SPEC's `takeTwo` (§8.11.3) is valid. MIR forms
c2's receiver reservation before the Call, as a deep `sborrow` that meets the earlier result's exclusive `Φ_c1`.

**What A3 lacks.** Nothing in its text: A3 §5's second row (round-7 erratum) states the excuse. The prototype encoded
it by hand at one node (33a) and not at all in 33c, which therefore recorded "A3's letter before the erratum".

**Decision: encode the erratum, keep 33c's expectation.** 33c is SPEC's `takeTwo` with the excuse stated only at the
Call (row 1's input); its SPEC verdict is valid, so redefining its expectation as an error would contradict S8:774.
The row becomes a derivation from facts A3 already has:

```text
excused(L, ℓ1) :- reserved(R, _, ℓ1, ℓ2), excused(L, ℓ2).   // ℓ1 forms a reservation that the excused Call at ℓ2 activates
```

It inherits row 1's conditions (`Φ_c1` undisturbed, same requirement, `recv(c1) ≡ recv(c2)`), so 33b (`Φ_c1^dist`)
still errs at the replacement, the reservation and the Call. A3 says "argument reservations of c2": every reservation
c2 activates. Only those on c2's receiver path can meet `Φ_c1` without also conflicting with c2's receiver reservation
at the activation (an argument overlapping `recv(c2)` is two overlapping reservations), so this scope is sound and
equals a receiver-only scope in verdicts (argued).

**Options.** A. the derived row in the owner (S2 part of `MirBorrows.Checks`): **chosen**. B. redefine 33c as an
expected error: rejected (SPEC). C. a per-Loan "excused against" column on the reservation Loan: rejected (a side
table for one feature, symptom list).

**Sites, size, cost.** Only F6's `excused`; a later S2 rule (per receiver Place) uses the same derivation. +3 lines; one
lookup per reservation formation (its activating Call's excuse set). Base-independent. Tests: unit 2f, 33a/33c valid,
33b error. The prototype applies the derivation in both modes, so S1′ also passes 33c now.

## 3. G110: no Loan past a shared Deref

**Required meaning.** S15:526 (S1): "A Borrow past a shared dereference forms no Loan of its own; it derives only from
the reference of its last shared dereference and the references it dereferences after that one, and carries their
Loans." S15:528: such a Borrow is contained only up to and including the first shared layer counted from the Borrow
("a shared Borrow through a shared reference is thus contained only in that reference's Origin"). The forms: explicit
`@ref` and implicit shared Borrows (receivers, adaptations, comparison inspections, captures, guard candidates) whose
Place has a `ref`, `objref` or ref-case pair layer Deref.

**What A3 wrongly allows (rejects).** A3 forms a Loan per Ref site with every parent layer. Its P3 ban then rejects a
Move, an exclusive Borrow (`exchange`, `swap`, `w = r@uniq`) or an overlapping exclusive argument of the shared
reference's holder or an ancestor: 39a (S1 §9's program), 39c, 39f, 39h, 39l are SPEC-valid and rejected by A3's
letter (`LoanPastShared`). Rust's supporting prefixes stop at a shared Deref the same way.

**Rule.** With X's borrow Derefs indexed 0..k−1 and `m(X)` the index of its last shared one (−1 if none), from the step
classes per view (`MirCases.Layer` decides a pair layer):

```text
Ref X into H at ℓ ⇒ σ_out(H) = own(X) ∪ ⋃_{max(m(X),0) ≤ j < k} content^j(σ_in(root(X)))
own(X) = {L_site} if m(X) = −1, else ∅          // issued(L, X, …) exists only when m(X) = −1
deep(k) :- k ∈ {move, sborrow, uborrow, obs}    // throughShared deleted: no Loan's Place contains a shared Deref
contain(o_j, o_new) for the layers j ≥ max(m(X),0) only   // MirOrigins: the same layers whose Loans the value carries
```

The Ref is still an `sborrow` access of X (deep): a live `uniq` Loan on a prefix (`p = s@uniq` used later) still meets
it. A TwoPhaseUniq never has a shared Deref (m = −1). With m = −1 the rule is A3's R2 unchanged.

**What P3, obs, containment and activation then see.**

- **P3** has nothing past the shared layer to reach: a Move or Borrow of the holder of the shared reference or of an
  ancestor is checked only against the Loans the derived value carries, on other Places (`x` in 39a). The referent is
  still protected by the shared reference's Loans (39b, 39d, 39g, 39p: `x = 5` rejected) and the holders after the last
  shared layer by its Loan on them (39j `t = [4]`, 39k `s.p = …`; S1 §2.1's example).
- **obs's shared cut** becomes vacuous and is deleted with `throughShared`: no Loan lies past a shared Deref (27 stays
  valid; `NoSharedCut` now changes nothing).
- **Containment** follows S15:528 from the Borrow outward. A3 §7 says "the same cut as `obs`" (root side), which adds
  `contain(o_p, o_new)` for `p.r@follow@ref` with `p: uniq/S`, `r: ref/i32 during o`, and so rejects returning it
  `during o` (H, not prototyped: the Origin stratum is not in the prototype). One function of the step classes, the
  first carried layer `max(m(X), 0)`, then decides own Loan, parents and containment.
- **Activation** reads the derived value's Loans: `g(s@uniq, s.r@follow@ref)` is valid (39h),
  `g(x@uniq, s.r@follow@ref)` with `s.r = x@ref` errs (39i).
- **Kill / bases** are unaffected: a value without its own Loan has no base under either rule (the 23a proposal's §3.1
  rule 2 says the same); FirstDerefBase gives the same verdicts on every 39 case.

**Soundness (argued; 138 cases).** The derived value is a reborrow of a copy of the shared reference: it reaches only
the shared reference's referent and, read-only, the references stored there. Writes to that storage need an exclusive
path that does not pass the shared reference, which meets the shared reference's Loans, carried by the value; the
holders after the last shared layer lie inside its referent and are protected by the same Loans. The references before
the last shared layer only locate the holder of the shared reference; moving, borrowing or rewriting that holder leaves
the copy intact (39e: `p.r = y@ref` while v lives is valid; 39d: `x = 5` afterwards still errs). P1's `content` is a
superset of what the stored reference carries. Self-reference through the shared layer stays protected (39n).

**Options.** A. the issue rule follows S1 in the owner of step classes and the issue transfer: **chosen**. B. keep a Loan
per site with a "derived" class that P3, kill and conflict skip: rejected (two representations of one fact, a skip in
every rule). C. an owner-approved loss for 39a/39c/39f/39h/39l: rejected (alignment is sound, removes Loans, deletes the
obs cut); no owner decision is needed, as S1 (R4-M39) already states the rule.

**Sites sharing the cause.** The pre-pass's `issued`; R2 in the issue transfer; `hits`'s `throughShared`; A3 §7's
containment cut; F6's `Φ_c` for a receiver Place past a shared Deref (a `ref`/`objref` receiver reached through `ref`):
no Loan forms there either, so no `Φ_c` (H, unit 2f); guard candidates past a shared Deref have no Loan for
`err(ended)`, so their guard-local Origin's escape is MirOrigins' alone (H, unit 2d).

**Size and cost.** Pre-pass +3 (m(X) from the step classes), Flow +2 (start layer, no own Loan), Checks −6
(`throughShared`, the obs cut), Origins ±0 (the containment start reads the same index): ≈ −1 line. L shrinks by one
Loan per Ref site past a shared Deref; the issue transfer does no more work than R2 did. No V16 term changes.

**Tests (MIR layer, unit 1b).** 39a–39q; 27; 13d/13e; the §13.5.6/S1 §2.1 example (SPEC-example table); one objref
pair (39l/39m); one Semantics-pair body whose ref case has no Loan and whose uniq case forms one (per view). The
prototype has no Types or cases, so a ref-case pair layer is encoded exactly as a `ref` Deref; no separate case.

## 4. New finding: 39o, a Borrow of a self-referential holder

**Program** (S3 {`r: ref/i32`, `n: i32`}): `s.r = s.n@ref; let p = s@uniq; p.n = 5; inspect(p.r@follow)`. `p.n = 5`
writes what `p.r` reads. SPEC rejects it at `let p = s@uniq`: S15:455, "a value is also used wherever a value that
carries a Loan on a Place overlapping the first value's holder is used", so s, which carries L_n on `s.n`, is used
after the Borrow through p, and a `uniq` Borrow of s reaches L_n. Rust rejects it (E0502). Design A accepted it in
every mode, and A3's letter does too: `CS(ℓ)` takes `σ_in(H)` for holders in `live_out(ℓ)`, s is dead after ℓ except
through p, and the closure starts from entry-set Loans, where p's new Loan is not ("Borrowing T is a use of T, so no
backward feedback is needed" covers the points after ℓ, not ℓ itself). S1′, with closure feedback, rejects it.

**Rule** (A3 §5):

```text
fresh(ℓ) = {T | ℓ issues a direct Loan L, named(L, T), into a holder live after ℓ or into E}
CS(ℓ)    = closure( ⋃{σ_in(H) | H ∈ live_out(ℓ), ℓ does not overwrite H} ∪ σ_in(E) ∪ transit(ℓ) ∪ ⋃{σ_in(T) | T ∈ fresh(ℓ)} )
```

The new Loan itself is not added (no value carries it before ℓ). Only self-referential holders change (a Loan carried
by T on a part of T); after ℓ the ordinary closure covers T. `NoFreshClosure` reproduces the hole; nothing else
changes; 39q (`let q = s@ref` of the same s) stays valid. Owner: `MirBorrows.Checks`, +3 lines, one node per direct
Ref at its own point, inside V16's scan term. Base-independent. Not a loss: A3 erratum, needs a PLAN row.

**Related wording (verified).** A3's transit comment ("`val_in`: the stored value's set computed from `σ_in`") must not
count a Ref's R2 parents as a stored value: `RefParentsInTransit` rejects the SPEC-valid 30b (and moves 30d), because
the walk's holder carries its own site's earlier Loan and is dead after the step. A Ref stores a value no entry set
carries; only `fresh(ℓ)` applies.

## 5. Prototype round 10 (`proto.cs`)

Changes, all in design A mode unless noted (S1′ mode is frozen except the shared `excused` derivation):

- G119: `InWindow(h, l, i)` (strict window, reservation holder); a `Write` or `Destroy` reaches a reserved Loan across
  Derefs inside it. Ablation `RouteShallow` = A3's letter.
- Encoding fix: an activating Call uses its reservation holder (A3's `use`: callee operand). Rounds 7–9 left it out of
  the F6 call nodes, so `$r1` was dead in 36h's window. Without the route rule it changes no verdict: `RouteShallow`
  keeps it and differs from A only in 36e, 36h, 38a, 38b, 38g, and from round 9's A on the 113 cases only in 33c.
- 33c: `excusedAt` derives row 2 from `Activate`; ablation `ExcuseAtCallOnly`.
- G110: `Issue.SharedAt` = m(X), hand-encoded step classes (13d/13e, 27 and the 39 cases); no own Loan and parents from
  layer m; the obs cut only under ablation `LoanPastShared` (A3's letter).
- 39o: `fresh` seeds the closure; ablations `NoFreshClosure` and `RefParentsInTransit`.
- Cases 38a–38h (G119) and 39a–39q (G110, 39o), SPEC verdicts below. No earlier expectation changed.

| Case | Program (short) | SPEC | Basis |
| --- | --- | --- | --- |
| 38a | `takeWith(do { cur@follow = H(…) })` | error at the write | S15:663 (owner of the reserved Place) |
| 38b | `takeWith(do { cur.inner = s2 })` | error at the write | S15:663 (reference reached through) |
| 38c | `takeWith(do { cur.other = 5 })` | valid | distinct Field (S15:506) |
| 38d | `takeWith(do { inspect(cur@ref) })` | valid | S15:663 "shared reads and Borrows may coexist" |
| 38e | `var cur2 = cur; cur2.inner.takeWith(do { cur = b })` | valid | route is cur2; a write of a base is shallow (S15:512, 6b) |
| 38f | `node = node.next()` | valid | the destination write follows the activation (S15:663, 19a) |
| 38g | `refs[i]@follow.push(do { refs[j] = s3 })` | error at the write | S15:663; indexes overlap (S15:509) |
| 38h | 38g with `refs[0]`, `refs[1]` | valid | distinct constants (S15:506) |
| 39a | `v = s.r@follow@ref; t = s@move; inspect(v)` | valid | S15:526 (S1 §9) |
| 39b | 39a + `x = 5` | error at `x = 5` | v carries s.r's Loan (S15:526) |
| 39c | `p = s@uniq; v = p.r@follow@ref; t = s@move; inspect(v)` | valid | S15:526: only the last shared layer |
| 39d | `p.r = y@ref; x = 5` while v lives | error at `x = 5` | S15:526, S15:454 |
| 39e | 39d without `x = 5` | valid | S15:454 (write ends nothing, v is a copy) |
| 39f | `v = r@follow@ref; w = r@uniq; w@follow = y@ref; inspect(v)` | valid | S15:526 |
| 39g | 39f + `x = 5` | error at `x = 5` | S15:526 |
| 39h | `g(s@uniq, s.r@follow@ref)` | valid | S15:663 + S15:526 (disjoint: s vs x) |
| 39i | `g(x@uniq, s.r@follow@ref)`, `s.r = x@ref` | error at the call | S15:663 overlapping arguments |
| 39j | `q = s@ref; e = q.p@follow@ref; t = [4]` | error at `t = [4]` | S1 §2.1, §13.5.6 |
| 39k | 39j with `s.p = t2@uniq` | error there | e carries q's Loan on s (S15:526) |
| 39l | objref: `f = h.view@follow.count@ref; g = h@move` | valid | S15:500 (objref shared), S15:526 |
| 39m | objref: `item.count = 5` while f lives | error there | S15:526 |
| 39n | `s.r = s.n@ref; v = s.r@follow@ref; s.n = 5` | error at `s.n = 5` | S15:526, S15:512 |
| 39o | `s.r = s.n@ref; p = s@uniq; p.n = 5; inspect(p.r@follow)` | error at `p = s@uniq` | S15:455 (used through p) |
| 39p | loop `keep = r@follow@ref; r = y@ref`; `x = 5` | error at `x = 5` | S15:456 (may-join) |
| 39q | `s.r = s.n@ref; q = s@ref; inspect(q.r@follow)` | valid | shared vs shared |

Run summary lines (`run.txt`): design A "12 listed loss, 105 same, 21 same verdict, error moves"; FirstDerefBase "7
listed loss, 111 same, 20 same verdict, error moves". Ablation lines: `RouteShallow` makes 36h, 38a, 38b, 38g unsound;
`ExcuseAtCallOnly` rejects 33c; `LoanPastShared` rejects 39a, 39c, 39f, 39h, 39l; `NoFreshClosure` accepts 39o;
`PhantomAlone` keeps 36c and 36g unsound but no longer 36e (G119). S1′ (exit code 16): unsound on 29a, 30c (round 6),
36h, 38a, 38b, 38g (no route rule); rejects 34c, 36f (losses) and 39a, 39c, 39f, 39h, 39l (a Loan per site); 39b, 39g,
39o fail on location only.

## 6. Interactions and open points

- **23a proposal (first-Deref base).** All four rules hold under both bases (FirstDerefBase column, 138 cases). G119
  is what makes "accesses are checked on `σ_in`, so the ending does not hide the write" (proposal §2) true for route
  writes; G110 gives past-shared Borrows no base; 33c and 39o are base-independent.
- **G113 stores (H).** If `Φ_c` goes into stores (`desc(c)` stored), G110 applies there too: no `Φ_c` for a receiver
  past a shared Deref.
- **R4-M37's open mode note** (`holder@uniq.items.append(x)`: a parent reservation is `uniq` in the child's holder) is
  independent: G119 reads the same `reserved`/holder pair as `mode(L, H)`.
- **Writeback (H).** The rule covers `$w = TwoPhaseUniq(D)` with no extra case; a custom setter's receiver is reserved
  only at the setter call (R4-M37), so its window starts there. Not prototyped (no write can occur in that window
  except through effects, which compare by root).
- **Unverified (H):** A3 §7's containment cut (not in the prototype); `Φ_c` and guard candidates past a shared Deref;
  the precision of `desc(c)` against R2 at the Call; the completeness argument of §1 beyond the 138 cases.

## 7. Decisions for the planning session

No owner decision is needed: G119, 33c and G110 align A3 with integrated SPEC text (S15:663, S8:774, S15:526/528) and
add no loss; 39o is SPEC's own S15:455 rule. Each is an A3 erratum for the planning session to record (A3 §5, §7, §11;
DECISIONS with the implementing commit; PLAN G110 and G119 closed by the 1b design; a new PLAN row for 39o until it
lands). If the planning session reads G110's dropped Loan as a new representation, the owner chooses between it
(recommended: sound, smaller, deletes the obs cut) and listing 39a/39c/39f/39h/39l as a loss.
