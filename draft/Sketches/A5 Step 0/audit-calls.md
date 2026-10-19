# Rule-Expressibility Audit: F3, F5–F9, A6

Base: `claude/a5-step0` = `b2edea8b`. Read-only. Plan = `plan-snapshot.md` (provisional wording).
Paths: `S15` = `docs/spec/15-ownership-and-lifetime-analysis.md`, `S8` = `docs/spec/08-…md`, `S7`, `S4`, `S11`, `S13`,
`S14`, `S16`, `S22` likewise; `B/` = `src/Kimi/Compiler/Binding/`, `M/` = `src/Kimi/Compiler/Mir/`, `A/` =
`src/Kimi/Compiler/Analysis/`, `DEC` = `docs/dev/DECISIONS.md`, `DIAG` = `docs/dev/DIAGNOSTICS.md`.

## Summary

- In scope, **one rule is not finitely expressible**: `preserves results` (row 10) under the current SPEC. The C5 program
  below confirms the claim. The real obstacle is that "the same value" is identity across invocations, not the recursive
  Type alone. Under S2 the rule is expressible as a must-variant of Loan flow.
- Every other rule is **expressible**. Most need a new fact. The central one is `retains(c, in, out)`: Binding publishes no
  retention table today, and the old analysis decides retention (`A/OwnershipAnalysis.Calls.cs:11-89`).
- **Row 9, Iterator descent,** needs retention per Origin position. Under the "provenance" reference representation the
  result takes every Loan the Iterator stores. That is coarser than the SPEC, so this is a step-0 input (plan §4.1).
- **Unsound producer gaps found:**
  - Slices carry no Loan on their backing. MIR forms a raw `Ref` (`M/Build/MirBuilder.Expansions.cs:288-315`), so the
    S4:570-575 `sort` example would be accepted.
  - The guard-local Origin `g` has no Loan-end event. S14:336 would be accepted (H).
  - Compound assignment has no writeback reservation (`Expansions.cs:127-240`).
- **Rejecting producer gaps** (verified in code): written `@uniq` arguments and reborrow inputs are plain `Uniq`
  (`M/Build/MirBuilder.Values.cs:84,175`). So S15:619 `replace(p@uniq, with: p + 1)` would be rejected.
- **A4 publishes no static keys, anchors or per-call summary** (`M/Analysis/MirEffects.cs:8,308-314`). Static roots are
  keyed by symbol only (`M/MirBody.cs:441-450`). Depth-32 atoms are `Unsupported_Kd` (`M/Analysis/MirEffects.Solve.cs:394-397`).
- **S2:**
  - It keeps `takeTwo`.
  - It keeps the 12 library Iterator conformances only if its phantom earlier-result Loans are region-split children with
    fresh anchors (H).
  - Its SPEC column omits §8.11.5, which also needs restating.
- **Binding facts** exist almost everywhere, but in per-pass mutable objects (`CallPlan`, `BoundClosure`, `BoundMatch`,
  failure-cause dictionaries). None is an immutable view.

## Table

| Rule (App. A) | SPEC | Class | New facts | Family | Bound |
| --- | --- | --- | --- | --- | --- |
| 2, 11 Calls, retention | S15:452, 521-529 | Expressible + fact | `retains` (C1+C8) | F3 | Σ calls × retained pairs × carried Loans, live-pruned |
| 9 Iterator descent | S15:515 | Expressible + fact + representation | `retains` per Origin position | F3 | as 2 |
| 10 `preserves results` | S15:517; S8:772-774, 787-793, 816, 843 | **Not finitely expressible**; with S2 expressible + fact | `preserves(c)`, `recv_place(c,p)`, preserves obligations | F6 | S2: ≤ `carries` work |
| 12 Effects, anchors | S15:531-537; S11:242; S22:374 | Expressible + fact | `eff_key`, `eff_unknown`, `res_anchor` (A4); `fn_use` (Binding) | F5 | calls × keys + output |
| 15 Reservations | S15:608-614 | Expressible (MIR fact exists, producer gaps) | — | F3 | intervals × indexed accesses |
| 16 Whole-value updates | S15:650-653, 671, 675-680 | Expressible as written | — | F8 | as 2, 15 |
| 17 Closure dependencies | S15:700-712; S7:330, 349-361 | Expressible + fact | `closure_src` (C2) | F7 | sources × positions per view |
| 18 Escape, retention | S15:728 | Expressible as written | — (`escaped`) | F7 | as F1 |
| 21 Match subject, guards | S15:128-139, 154; S14:325-342 | Expressible + MIR fact (H) | `guard_end(P, L)` | F8 | output |
| 25 Compound assignment | S13:595; S15:611 | Expressible + MIR fact | `wb_reserved` | F8 | as 15 |
| 26 Default inspection | S7:77-79; S15:614 | Expressible (derived fact) | `default_retains` (A5-derived) | F9 | templates once; SCC worklist (H) |
| 29 Erased destruction | S16:194-196; S15:534 | Expressible + fact | `drop_eff(P, …)` (A4) | F5 | Drops × static Loans |
| 30 Collections | S4:552-568 | Expressible + fact | `backing` (U8p) | F3, F8 | as 2 |
| A6 Reporting | DIAG §4.3; DEC R4-M10 | Expressible + fact | `cause(site, keys)`, E4 parts | A6 | clean stratum ≤ 2×; E5 policy |

## Shared new relations

These extend plan §4.2. In the notation, `P` is a point and `c` a call.

| Relation | Meaning | Producer | Kind |
| --- | --- | --- | --- |
| `call(P,c)`, `arg(c,i,H)`, `dest(c,H)`, `recv_place(c,p)` | Call terminator, operand holders, destination, receiver Place | MIR (`M/MirBody.cs:155-162`) | input |
| `retains(c,i,o)` | Under the instantiated public contract, output `o` (result position, or the writable referent of input `j` at a position) may retain input `i` at a position | Binding view `TyCtxt.Retains(callee, Subst)` | input |
| `preserves(c)` | The callee's bound under the caller's premises includes `preserves results` | Binding (`B/Binding.EffectObligations.cs:83,98`) | input |
| `eff_key(c,k,kind)`, `eff_unknown(c)`, `res_anchor(c,k)` | Static keys a call may access, unknown environment effects, per-result mutable-static anchors, composed through the Subst | A4 | input |
| `drop_eff(P,k\|unknown)` | Effects of the destruction at Drop `P` | A4 local facts | input |
| `fn_use(site,f,kind)` | A witness, slot, erasure or Callable binding of function `f` | Binding (EffectObligations) + MIR erasure Casts | input |
| `closure_src(k,v,pos,s)` | Result position `pos` of Closure `k` in view `v` is outlived by source `s` ∈ {receiver, capture j, parameter i} | Binding (C2) | input |
| `guard_end(P,L)` | Loan `L`, issued in a guard, must not be live after `P` | MIR builder | input |
| `wb_reserved(L,P1,P2)` | Writeback reservation of a compound-assignment destination | MIR builder | input |
| `backing(c,place,mode)` | A Slice or view value depends on its backing Place | Binding (U8p) → MIR | input |
| `default_retains(d,i)` | Default `d`'s result may carry the Loans of prepared input `i` | A5 over template bodies | derived |
| `excl(L,p,r,P)` | `L` is excluded for calls of `r` on receiver Place `p` (S2) | rules | derived |
| `cause(site,keys)` | Binding failure keys behind an Opaque site | Binding `FailureCauses` → `MirOpaque` | input |

## Per-rule sections

### Rows 2 and 11: calls and retention (F3)

1. **SPEC:**
   - S15:452: after a call, results and contract-writable storage carry the Loans of the arguments they may retain.
   - S15:521-529: steps 1–5. Step 4 adds OwnedOrigins of a borrowed input's referent. Protection lasts through the whole
     call. Retention follows the instantiated public contract for direct and indirect calls.
2. **Rule:**
   ```text
   carries(Hd, L, Q) :- call(P,c), arg(c,i,Hi), dest(c,Hd), retains(c,i,result), carries(K,L,P), within(K,Hi)∨ancestor, edge(P,Q).
   carries(T, L, Q)  :- call(P,c), arg(c,i,Hi), carries(K,L,P), within(K,Hi), retains(c,i,store(j)),
                        arg(c,j,Rj), reaches(deref(Rj),T,P), edge(P,Q).          // adds, ends none (S15:454)
   carries(Hd, L, Q) :- call(P,c), retains(c,i,result_via_borrow), arg(c,i,Ri), carries(Ri,L0,P), names(L0,T),
                        carries(T,L,P), edge(P,Q).                               // step 4 OwnedOrigins
   access(P, place(L), mode(L)) for every argument Loan L, during the call.
   ```
   `retains` is reachability in the callee's Origin graph, from input positions to output positions. The graph's edges
   come from:
   - the declared clauses;
   - signature well-formedness;
   - Type-parameter positions, substituted by the Subst.

   Example: `h(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b` with `a outlives b` retains both `x` and
   `y`.
3. **Class:** expressible with a new fact, `retains`.
   - Producer: Binding's premise entailment (`B/Binding.OriginPremises.cs:42-78`, `B/OriginPremiseClosure.cs:6-34`) per
     declaration, applied through the call's Subst.
   - Owner: Binding.
4. **Bound:** rows ≤ Σ calls × input positions × output positions, which is input-sized. The work moves each carried Loan
   once per retained pair. Liveness pruning (plan §4.6) stops propagation into dead destinations, so work ≤ c·(input +
   output). V16 holds.
5. **Tests:**
   - Invalid: S15:623-630 (`inventory.newest = makeItem()` while `item` is used).
   - Valid: S15:408 `first`: `let r = first(x@ref); y = 3; use(r)`.
   - Valid: `let n = len(d@ref); d = makeData(); use(n)`, where `len(x: ref/Data) -> i32`.
   - Invalid (writable storage): `store(s@uniq, a@ref); a = 2; use(s)`, where `store(dst: uniq/(ref/i32 during q), v: ref/i32 during q)`.
6. **Gaps:**
   - Binding publishes no retention table. The old analysis decides it (`A/OwnershipAnalysis.Calls.cs:11-89`,
     `A/OwnershipBody.Calls.cs:7-23`, `A/OwnershipBody.Borrows.cs:1054-1090, 1442-1530`).
   - Premises are never stored (`B/Binding.OriginPremises.cs:42-78`).
   - MIR keeps only Symbol, Subst and Contract (`M/Build/MirBuilder.Calls.cs:128-134`). The Virtual slot `Arg` is never
     set (`Calls.cs:131`), contrary to `M/MirBody.cs:103-107`.

### Row 9: Iterator descent (F3)

1. **SPEC:** S15:515. A result whose Origins are only the receiver Type's, and are named by no other input, descends from
   the Iterator. Destroying the Iterator does not end published child Loans.
2. **Rule:**
   ```text
   carries(Hd, L, Q) :- call(P,c), retains(c, recv·ref_pos(o), result·pos), arg(c,0,R),
                        carries_pos(referent(R), o, L, P), edge(P,Q).
   ```
   Nothing kills `Hd` when the Iterator is destroyed. A StorageDead or Drop of the Iterator is only an access to its own
   Place.
3. **Class:** expressible with `retains` at Origin-position granularity. It also needs a referent read per position:
   - Plan §4.1's "Origin positions" option meets the SPEC.
   - "Provenance" gives the result every Loan the Iterator's referent stores. It would reject a valid program: an Iterator
     with two independently borrowed Fields whose `Item` names one of them, followed by a write to the other collection
     while the item is used (H).

   This is a step-0 representation input.
4. **Bound:** as rows 2 and 11.
5. **Tests:**
   - Invalid: `let first = it.next()` on `ArrayUniqIterator` (`src/Kimi/Library/Array.kimi:285`); destroy `it`;
     `xs.clear()`; `use(first)`.
   - Valid: the same without `use(first)`.
   - Valid: `takeTwo` (S8:687-690) relies on descent plus row 10.
6. **Gaps:** none beyond `retains`.

### Row 10: `preserves results` (F6)

1. **SPEC:**
   - S15:517: a result may be kept across calls on the same receiver.
   - S8:772: eligibility.
   - S8:774: no conflict with Loans kept by earlier results of `r` on the same value `v`. Replacing or destroying `v` is
     outside the guarantee.
   - S8:791: rule 3 at callers.
   - S8:816: delegation through Field paths, "borrowed Fields included".
   - S8:843: Callable values.
2. **Rule.** This is S2's rule (plan :354).

   Caller side:
   ```text
   kept(L,c1,Q)     :- call(P,c1), preserves(c1), dest(c1,Hd), carries(Hd,L,Q), edge(P,Q).   // + child+ of those (reborrow)
   excl(L,p,r,Q)    :- kept(L,c1,Q), recv_place(c1,p), callee(c1,r).
   excl(L,p,r,Q)    :- excl(L,p,r,P), edge(P,Q), !disturbed(p,P).                      // must-fact: intersect at joins
   disturbed(p,P)   :- replaced(H,P), overlap(H,path(p)) ; access(P,pl,move|destroy), prefix(pl,p)
                     ; written_through(_,_,P), reaches(_,T,P), overlap(T,path(p)).
   error(L,P)       :- call(P,c2), preserves(c2), callee(c2,r), recv_place(c2,p), effect_conflict(c2,L), !excl(L,p,r,P).
   ```
   Conformance:
   - Seed phantom Loans `Φ` for the result Type's receiver-Type Origins, as region-split children (fresh anchors, S15:496)
     tagged by return-path source.
   - Make them escaped, so they are live throughout.
   - For each call of a preserves requirement on a `self` Field-path Place `w`, if the call is undisturbed since entry,
     exclude `Φ` tagged `(d, w)`.
   - Every other call contributes its declared effects only.
3. **Class:** not finitely expressible as written (hard item C5). Under S2 it is expressible with:
   - `preserves(c)`. It exists: `TyCtxt.DispatchBounds` and `CallableBounds` return `(Confined, Preserves)`
     (`B/Binding.EffectObligations.cs:83,98`; `B/TyCtxt.Effects.cs:45,49`).
   - `recv_place`, from MIR.
   - Preserves obligation rows. Today `EffectObligations` lists `confined` only (`B/Binding.EffectObligations.cs:24-77`).
4. **Bound:** `excl` has the shape of `carries`, restricted to Loans of preserves results × receiver Places, so work is ≤
   the `carries` work. V16 holds.

   Repeated sites (H): static site IDs suffice. An older result whose Loan merges with a newer one at site `s` was already
   checked at the intervening call of `s`. If it is live later, it was live there (liveness is monotone backwards), and
   `excl` was killed there. A Sketches prototype should check this.
5. **Tests:**
   - Valid: `takeTwo` (S8:687-690).
   - Invalid: the same with only `S is Take` (S8:704).
   - Invalid: `takeAcross` (plan :378-384).
   - Valid: Drain (S8:819-825).
   - Invalid: Merge (S8:827-836).
   - Invalid: Peek (S8:777-780, Binding eligibility).
6. **Gaps:**
   - The old path identifies `v` as (root, first Field) (`A/OwnershipAnalysis.cs:1717-1729`) and never kills the
     exclusion on replacement (`A/OwnershipBody.Borrows.cs:926-962`). So it would accept `takeAcross` (H, no test).
   - Callable conflicts are `Unsupported` in the old path (`A/OwnershipBody.Borrows.cs:911-917`).
   - MIR lacks the selected signature of a several-signature Callable input (DEC R4-M23 open issue;
     `M/Analysis/MirEffects.Solve.cs:171-182`).

### Row 12: effect summaries and mutable-static anchors (F5)

1. **SPEC:**
   - S15:531: summaries of static keys and per-result anchors.
   - S15:533-534: a conflict with a live Loan is an error. Unknown effects conflict with every mutable-static Loan.
   - S15:537: an anchor leaves the body only through a result bounded by a borrowed input, and its callable has
     restricted uses.
   - S11:242.
   - S22:374 and S15:499: key identity and open keys.
2. **Rule:**
   ```text
   error(L,P) :- call(P,c), eff_key(c,k,kind), live_loan(L,P), key(place(L),k2), key_overlap(k,k2), conflicts(kind,mode(L)).
   error(L,P) :- call(P,c), eff_unknown(c), live_loan(L,P), mutable_static(place(L)).
   key_overlap(k1,k2) :- field(k1)=field(k2), (open(k1) ; open(k2) ; k1=k2).
   anchor(Hd,k) :- call(P,c), res_anchor(c,k), dest(c,Hd).
   anchor(H,k)  :- issued(L,pl,_,P,H), mutable_static_root(pl,k).                  // flows like carries
   // Origin stratum: the Origin of an anchored value is finite, bounded by the body and borrowed-input-bounded results.
   error(site) :- fn_use(site,f,_), res_anchor_fn(f,_).
   ```
3. **Class:** expressible with new facts:
   - From A4: `eff_key`, `eff_unknown`, `res_anchor`.
   - From Binding: `fn_use`, built from `EffectObligations` witness and Callable rows, plus MIR erasure Casts
     (`M/Build/MirBuilder.Values.cs:181-185`).
4. **Bound:** per call, its keys plus the conflicting Loans, indexed by key. A4's fixed point is linear in calls
   (`M/Analysis/MirEffects.Solve.cs:33-34, 100`).
5. **Tests:**
   - S15:540-558: `g()` is invalid while `c` is used. `l` alone is valid. `current` is valid. `leak` and the erasure
     `let f = current` are invalid.
   - S11:249-251: `limitRef` is valid; `nameRef` is invalid.
   - S22:377-381: `Cache<i32>` and `Cache<string>` keys are disjoint.
6. **Gaps:**
   - A4 keeps classes and atoms only. A mutable static just sets `Environment` (`M/Analysis/MirEffects.cs:308-314`).
     There is no accessor, although the header promises one (`MirEffects.cs:8`).
   - `MirStatic` is keyed by symbol (`M/MirBody.cs:200, 441-450`), with no enclosing Type arguments.
   - Atoms past depth 32 are a Limit, reported as `Unsupported_Kd` (`M/Analysis/MirEffects.Solve.cs:394-397`; DEC
     R4-M22). That is a family left Unsupported.

### Row 29: erased destruction effects (F5)

1. **SPEC:**
   - S16:196: destroying a value of statically unknown Dynamic or environment Type has unknown effects. It conflicts with
     every live mutable-static Loan.
   - In a generic body, destroying an abstract non-Copy `T` is unknown for the body's own checks.
   - Also S15:534 and S8:721-722.
2. **Rule:**
   ```text
   error(L,P) :- drop_eff(P,unknown), live_loan(L,P), mutable_static(place(L)).
   error(L,P) :- drop_eff(P,k), …as eff_key.
   ```
3. **Class:** expressible with a new fact, `drop_eff`, published per Drop from A4's local facts. The classification already
   exists, but only per body: Function value → Unknown, non-Sealed handle → Unknown, abstract part → atom
   (`M/Analysis/MirEffects.Solve.cs:277-291`). A4 needs `TyCtxt.Copy` (`B/TyCtxt.Effects.cs:57`) to settle atoms per Drop.
4. **Bound:** Drops × live mutable-static Loans; every pair found is an error, so the work is output.
5. **Tests:**
   - Invalid: S16:199-202.
   - Valid: the same with `view` dead.
   - Valid: an `rc/Sealed` handle.
   - Invalid: in a generic body, `self.item = .None` while `State.text@ref` is live (cf. S8:756-760).
   - Valid: the `CopySlot` analogue (S8:762-767).
6. **Gaps:** A2's `Group` returns −1 for both "check-only" and "not analysed" (`M/Analysis/MirDrops.cs:86-88`). A4 tells
   them apart only by reachability (`MirEffects.cs:197-208`).

### Row 15: call reservations (F3)

1. **SPEC:**
   - S15:608-614: exclusive receiver and argument borrows, including reborrows at `uniq` parameters and written `@uniq` on
     the preparation path, start reservations.
   - Shared access may coexist with a reservation.
   - Every reservation activates at once.
   - An abandoned call ends its reservations without activation.
2. **Rule:**
   ```text
   reserved(L,P1,P2) :- issued(L,pl,twophase,P1,H), call(P2,c), uses(c,H).
   error(L,P)  :- reserved(L,P1,P2), P1<P<P2, access(P,pl,k), overlaps(pl,place(L)), k∈{write,move,uniq,destroy}.
   error(L,P2) :- reserved(R,_,P2), live_loan(L,P2), L≠R, overlaps(place(L),place(R)), conflicts(uniq,mode(L)).
   error(R,P2) :- reserved(R,_,P2), call(P2,c), eff_key(c,k,_), key_overlap(k,key(place(R))).
   // abandonment: EndBorrow(H) on the transfer path, with no activation access.
   ```
3. **Class:** expressible; the MIR fact `TwoPhaseUniq` exists (`M/MirVocabulary.cs:113`; `M/MirTypes.cs:128-141`).
4. **Bound:** reservation intervals ⊆ calls. Accesses inside an interval are checked against reservations indexed by Place.
5. **Tests:**
   - S15:618-621: `replace` is valid, `insert` is valid, `append(tasks.remove(0))` is invalid.
   - S15:627-630: invalid.
   - S15:665: `swap(p@uniq, p@uniq)` is invalid.
   - S4:570-575: `sort` is invalid.
6. **Gaps:**
   - A written `@uniq` is a plain `Uniq` (`M/Build/MirBuilder.Values.cs:84`), so S15:619 would be rejected.
   - A reborrow at a call input is plain (`Values.cs:175`).
   - The `Borrow` helper used by collection literals is plain (`Values.cs:232`).
   - `Kills` merges EndBorrow with StorageLive and StorageDead (`M/MirDescriptors.cs:22`).

### Row 16: whole-value updates (F8)

1. **SPEC:**
   - S15:650-653: targets are arguments.
   - S15:671: `swap` targets must be proven disjoint.
   - S15:675-680: an update may not invalidate live borrows of the old contents; independent external dependencies
     survive.
2. **Rule:** `replace`, `exchange` and `swap` are calls. They follow rows 2 and 15 with signature-derived `retains`:
   - `exchange`'s result retains the target's referent and `value`, per its contract.
   - The target is a reserved argument.
   - Disjointness is the activation rule plus S15:494's "distinct anchors" row.
   - Old-content borrows conflict with the activation's `uniq` access.
3. **Class:** expressible as written.

   The write goes through the argument reference, so by S15:454 it ends no Loans. Under the current SPEC,
   `var r = a@ref; replace(r@uniq, with: b@ref); a = 3; inspect(r)` is an **error**, while `r = b@ref` is valid
   (S15:458-465). This depends on S4 and OD11.
4. **Bound:** as rows 2 and 15.
5. **Tests:**
   - S15:660-666.
   - S15:683-689: invalid.
   - Valid: `let copy = slot; slot = other@ref; inspect(copy)`.
6. **Gaps:** row 15's.

### Row 17: Closure dependencies (F7)

1. **SPEC:**
   - S15:700-712: a Closure keeps captured Loans. The result-source table sets the contract.
   - An inferred result Origin is per call and fixed in the body. Other sources are errors.
   - Annotated results use ordinary elision.
   - Also S7:330 and S7:349-361 (receivers), S15:565 and S15:567.
2. **Rule:**
   - Formation: each capture operand flows into the environment Field (an F1 aggregate flow,
     `M/Build/MirBuilder.Values.cs:304-322`). An `x@ref` or `x@uniq` entry issues a Loan on the outer slot.
   - Call `c` of Closure value `k`:
     ```text
     retains(c,recv,result·pos) :- closure_src(k,v,pos,receiver), mode(c)∈{Shared,Exclusive}.
     carries(Hd,L,Q)            :- closure_src(k,v,pos,capture j), carries(field(H_k,j),L,P), edge(P,Q).
     retains(c,i,result·pos)    :- closure_src(k,v,pos,param i).
     ```
     A Consuming call moves the environment.
   - Closure body (Origin stratum): `ρ` is fixed, and each returned source must be in the table.
   - Erasure: `flow(source, erased)`.
3. **Class:** expressible with a new fact, `closure_src` (C2).
4. **Bound:** sources ≤ (captures + parameters + 1) × result positions × views (≤ 64, DEC R4-M18).
5. **Tests:**
   - S15:715-719: invalid.
   - S15:721-723: invalid.
   - S7:341-344: valid.
   - Valid: `let c = func [t@ref] () => (); c(); t = makeText()` (no retention).
6. **Gaps:**
   - Binding rejects results that depend on the environment at value calls as `Unsupported`
     (`B/Binding.ValueCalls.cs:428-442, 531-543`). This is a family left Unsupported.
   - No source set is published. `CallLocalOrigin` computes it transiently (`B/Binding.Closures.cs:138-186`; environment
     slots at `B/Binding.ArgumentOperations.cs:107-118`).
   - A capture whose source is unbound is `Unsupported` (`Values.cs:313-316`).

### Row 18: escape and retention (F7)

1. **SPEC:** S15:728. Returned values, stored Fields and indirect callees are checked against their destination's
   contracts. Moving or heap-allocating a Closure never extends a lifetime.
2. **Rule:**
   ```text
   live_loan(L,P) :- carries(H,L,P), escaped(H).
   ```
   The holders that escape are the return local, parameter and capture referents, and statics. At a local's
   StorageDead or Drop, `access(destroy)` conflicts with the live Loans on it. Origin fits are F4's.
3. **Class:** expressible as written. This confirms the plan's "escape to caller storage".
4. **Bound:** as F1.
5. **Tests:**
   - Invalid: S15:580-582.
   - Invalid: `let c = func [t@ref] () => (); let m = c@move; consume(t@move); m()`.
6. **Gaps:** none in scope.

### Row 21: match subject and guards (F8)

1. **SPEC:**
   - S15:128-139: a bare-Place Subject is borrowed in place. After selection, its Loan follows the bindings.
   - S15:154.
   - S14:325-329: a candidate is `ref/T during g`, with `g` ending at guard cleanup.
   - S14:342: Subject protection.
2. **Rule:**
   - The Subject temp carries `L_s` (`M/Build/MirBuilder.Match.cs:27-28`). Bindings derive from it.
   - Liveness through the bindings alone gives "follows the bindings".
   - Protection: the FakeRead of the root position after the guard keeps `L_s` live, and F1 conflicts do the rest
     (`Match.cs:92`).
   - `g`:
     ```text
     error(L,P) :- guard_end(P,L), live_loan(L,P).
     ```
3. **Class:** the Subject and its protection are expressible as written. `g` needs a new MIR fact, `guard_end`.
   - S14:336 has no fixed Origin at the other end of its chain.
   - S15:573's only refutation is finite → fixed. S15:577 sends lifetime overruns to Loan conflicts at destruction.
   - So without a Loan-end event, S14:336 is accepted (H). The alternative is an IMPL or SPEC clarification.
4. **Bound:** each `guard_end` probes live Loans; every hit is an error, so the work is output.
5. **Tests:**
   - S15:157-162: valid.
   - S14:345-351: both invalid.
   - S14:332-339: arm 1 is invalid, arm 2 valid.
   - Valid: `match state` with arm `.Idle => state = .Busy` (S15:139).
6. **Gaps:**
   - EndBorrow is emitted only for transfers (`M/Build/MirBuilder.Control.cs:135-141`). On normal paths, the candidate
     temps end by StorageDead, which ends carrying only (plan §4.1).
   - `BoundMatch` is mutable and `Reset` per pass (`B/Binding.Patterns.Model.cs:69-121`).

### Row 25: compound assignment (F8)

1. **SPEC:** S13:595. The destination, or a custom setter's receiver, is protected like a reservation from locate to write.
   This includes the `get` and operator effects. The exclusive Loan becomes live at the write. See also S15:611.
2. **Rule:** row 15's rules with `wb_reserved(L,P_locate,P_write)`, plus `eff_key` of the intermediate calls.
3. **Class:** expressible with a new MIR fact. `TwoPhaseUniq` activates "at the Call" (`M/MirVocabulary.cs:113`), so a
   write activation is a vocabulary decision (DEC O1).
4. **Bound:** as row 15.
5. **Tests:**
   - Valid: S13:600-602.
   - Invalid: `Counter.total += x` where `add` (Addable) writes `Counter.total`.
6. **Gaps:** `Update` has no protection form (`M/Build/MirBuilder.Expansions.cs:127-240`).

### Row 26: default inspection (F9)

1. **SPEC:**
   - S7:77-79: a default may inspect a reserved input through shared Loans that end before entry.
   - It cannot Move or modify the input, and it cannot keep a new Borrow or Reborrow; Copying an existing shared borrow is
     allowed.
   - The default is checked once, like a generic body.
   - Also S15:614, DIAG:107 and DEC R4-M10.
2. **Rule:**
   - Template body: the result carrying a Loan issued on a prepared-input root gives `DefaultArgumentBorrow_Kd`. A write
     or Move of one gives `…Access`/`…Move`.
   - Inline copy at the caller: inspection Loans are shared, and row 15's activation rule rejects one still live at entry.
   - Template call (recursive SCC, `M/Build/MirBuilder.Defaults.cs:135`):
     ```text
     retains(c_t,i,result) :- default_retains(d,i).
     default_retains(d,i)  :- Arg i's Loans reach d's return local (A5 on the template).
     ```
3. **Class:** expressible. `default_retains` is derived by A5. It is not a Binding fact.
4. **Bound:** each template is analysed once. An SCC uses a worklist on growing bits, ≤ Σ bits × template size. It is
   linear only when defaults have boundedly many prepared inputs (H).
5. **Tests:**
   - S7:84-88: `label` is valid, `bad` invalid, `erased` valid.
   - Invalid: `func f(x: uniq/Data, y: ref/Data = x)`.
   - Valid: `func g(x: ref/Data, y: ref/Data = x)`.
   - Valid: `func h(x: uniq/Data, n: i32 = Data.size(x))`.
6. **Gaps:**
   - No retention fact exists. MIR's `Omission.Reads` is private (`Defaults.cs:22, 82, 129`).
   - The rule is checked only by the old path (`A/OwnershipAnalysis.DefaultDeclarations.cs:15-44`).

### Row 30: collection operations (F3, F8)

1. **SPEC:**
   - S4:552-568: the receiver is reserved.
   - Dependencies join; none is subtracted.
   - Removed values carry full dependencies.
   - `tryGet` adds the storage Loan.
   - The published rows are the complete effect.
2. **Rule:** rows 2 and 15 with signature `retains` give the dependency rules. For example, `remove` returns `T` from a
   `uniq/Self` referent. A4's published rows supply `eff_key` (DEC R4-M28). These are ordinary `Fn` calls
   (`M/Build/MirBuilder.Calls.cs:122-133`).
3. **Class:** expressible with `backing` (U8p).
4. **Bound:** as row 2.
5. **Tests:**
   - Invalid: S4:570-575.
   - Invalid: `a.append(x@ref); a.clear(); x = 2; use(a)` (nothing subtracts).
   - Invalid: `let r = d.tryGet(k); d.insertOrReplace(k2, v); use(r)`.
6. **Gaps:**
   - A Slice is a raw `Ref` plus `RawSlice` (`M/Build/MirBuilder.Expansions.cs:288-315`), with a fresh anchor (S15:496).
     So it carries no backing Loan, and S4:573 would be accepted: **unsound**.
   - Binding has the dependency as the Slice Type's Origin, `PlaceOrigin(receiver)` (`B/Binding.Ranges.cs:176, 246`;
     `B/Binding.ArgumentOperations.cs:684-720`).

### A6: reporting

- **Facts:** each problem is (code, subject, Loan anchor, access, witness use, view mask). `live_why(H,P,Q)` records one use
  per live holder during liveness, because a check must not re-run analysis to explain itself (AGENTS "Diagnostics").
- **Derived (DEC R4-M10):**
  - A clean stratum re-evaluates the rules without facts tainted by `opaque(site)`.
  - `error ∧ clean` is direct; `error ∧ ¬clean` is derived, with causes per E5.
  - Problems in Inlined scopes go to the template. A PreparedInput root maps to `DefaultArgument*_Kd`.
- **Class:** expressible with `cause(site, keys)` and E4's independently bound parts.
- **Bound:** the clean stratum is ≤ 2× evaluation.
- **Gaps:**
  - `MirOpaque` carries no keys (`M/MirBody.cs:204`).
  - `MirReport.Add` has none of the drop rules its header claims (`M/Analysis/MirReport.cs:6` vs 36-48).
  - Children of an Error are published as Syntax (`B/Binding.Hir.cs:20-31, 156-158, 230-234`), so independent Loan
    errors inside them are never analysed.

## Hard items

**C5 (`preserves results`) under the current SPEC.** The claim is confirmed; the program is answer (c).

```kimi
// Node<J> has `var inner: J` and `var children: Array<Node<J>>`.
struct Deepest<J> {n}
    J is Iterator
    Self is StableTake
    associate Take.Item is J.Item
    var root: uniq/Node<J> during n
    public func take(self: uniq/Self) -> Option<J.Item>
        var cursor = self.root                    // A Reborrow through self.
        while cursor.children.length > 0
            cursor = cursor.children[0]@uniq      // Descends; replaces nothing on the path.
        return cursor.inner.next()                // w_k = root.children[0]^k.inner
```

- **The domain.** The result keeps Loans of `next` on `w_k` for unbounded k.
- **The second call.** It runs `next` on `w_k'`. `J` is abstract, so that call "may reach every Loan its Origins may
  denote" (S8:789), which includes the first result's Loans. Only rule 3 excludes them, and only if `w_k'` is the same
  value as `w_k`.
- **Why that is undecidable here.** k' = k holds exactly when no operation outside `take` changed the tree between the
  calls. S8:774 puts only "destroying or replacing `v`" outside the guarantee. So a verdict needs value identity across
  invocations over an unbounded family of paths.
- **Why the obvious abstractions fail.** No finite domain decides it. A finite path language
  (`root.children[0]*.inner`) cannot prove k = k', so it would reject a program the text arguably accepts.
- **Where the domain is finite.** In bodies with no loop or recursion through recursive Fields, the domain is the finite
  set of syntactic `self` paths. This is what the old path implements (`B/Binding.EffectBounds.cs:1363-1406`, matched
  syntactically).
- **Refinement of the plan.** The claim is "unbounded in recursive Types". It is more precisely "loops or recursion through
  recursive Types, plus cross-invocation identity". Owned recursion through `Array` suffices; borrowed Fields are not
  needed.
- **Under S2,** `cursor.inner` is not a `self` Field-path Place, so nothing is delegated and the program is rejected. S2
  should state this.

**E5: the per-requirement unavailable frontier** (OBS13:73; "require" read as requirement, (H)).

- **The frontier.** For a derived A5 problem, it is the set of Opaque sites through which every derivation of that problem
  passes.
- **Exact cuts.** Exact why-provenance costs |requirements| × |sites| (plan §5.2).
- **DIAG:76** copies prerequisite arrays at the recording boundary. Shared memoized sets therefore need a DIAGNOSTICS
  change.
- **The alternative** is one representative cause per tainted fact (the minimum site key), propagated with the taint. It
  is O(`carries`). It fits DIAG:101 and DIAG:109, since suppression needs every named cause to reach a published Error.
  But it replaces the reviewed "terminal cut" policy (`docs/dev/PLAN_HISTORY.md:32`).
- **Verdict.** Expressible either way. The choice is a DIAGNOSTICS and policy decision, not a SPEC change. This confirms
  the plan.

**A5-closure / C2.** Confirmed with conditions:

- Sources are finite per view and cut by the fixed `ρ` (S15:565, 710).
- Two Binding items are required, not one:
  - remove the `Unsupported` at `B/Binding.ValueCalls.cs:531-534`;
  - publish the source set and the per-view receivers. Nested Closures mutate the outer receiver
    (`B/Binding.Closures.cs:700-727`).
- MIR already acquires the callee with `input: true` (`M/Build/MirBuilder.Calls.cs:66-70`).

**M39.**

- **One outcome per case.** MIR already resolves PairAcquire and Adapt per view (`M/MirCases.cs:121-157`; DEC R4-M19).
  A5 runs per view (R4-M11). Binding keeps no table: the masks are computed on demand (`B/Binding.PairCases.cs:251-283`).
  A Case terminator does not exist (`M/MirVocabulary.cs:41-49`); it is needed only if OD8 adopts one.
- **Template calls for every default (OD9).** These reopen `docs/SETTLED.md:194-209`.
- **For A5, both forms are expressible.**
  - Inline: inspection Loans plus row 15.
  - Template: `default_retains`, positional by `MirCallee.Position(function, ParameterIndex)`
    (`M/Build/MirBuilder.Calls.cs:105`).
- **Bound:** views ≤ 64, a Resource limit (DEC R4-M18; S8 §8.15.2).

**C3: retained transfers and positional prepared-input transfer.** Both are expressible:

- Retained transfers are row 2's result flow plus the store write-through.
- Positional transfer is `default_retains` indexed by parameter position.
- Phantom slots are row 10's `Φ`.
- Escape is row 18.

The repeated-site parent/child item is out of scope (S1).

## Binding fact inventory

| Fact | Key → value | Computed today | Published or mutated | Smallest view |
| --- | --- | --- | --- | --- |
| C8 selected callee | call (SyntaxId + ordinal, `B/CallPlan.cs:147`; `B/HirTables.cs:92-100`) → Kind, Target, Subst, RequirementContract, ArgumentToParameter, DefaultArguments, value-call Signature | `B/Binding.Calls.cs:889-895`; `B/Binding.ValueCalls.cs:553`; `B/CallPlan.cs:109-115` | Mutable and reused (`CallPlan.cs:46-47`). Written outside `Set` (`Calls.cs:893`, `B/Binding.SelectedRequirements.cs:30-51`). Hidden when CheckBound unresolves a node (`CallPlan.cs:173`). MIR copies it at build (`M/Build/MirBuilder.Calls.cs:128-134`). | Already `MirCallee`; add the selected value-call Signature and the Virtual slot |
| C8 effective contract | instance call → implementation, with the requirement's ReturnType | `B/Binding.RequirementInstances.cs:53-123`, instances only | Hybrid plan, mutated by `ProjectWitnessCall` (`:137-165`) | None for generic bodies; `TyCtxt.Implementation` exists (`B/TyCtxt.Effects.cs:62`) |
| C1 retention | (callee, Subst) → (in slot, in pos, out kind, out pos) | Old analysis only (`A/OwnershipAnalysis.Calls.cs:11-89`) | Not published | `TyCtxt.Retains(function, subst)`, per declaration from clauses and WF, substituted |
| C1 premises and obligations | definition → premises; Use → `BindingObligation` | Premises never stored (`B/Binding.OriginPremises.cs:42-78`); `Binding.Obligations` keyed by Use, with no call id (`B/Binding.cs:25, 55`; `B/Binding.Origins.cs:756-764`) | Compacted after bodies (`B/Binding.ConstraintValidation.cs:417-431`); purged (`B/Binding.ImplicitConstructors.cs:216-250`); CheckBound re-fails (`Binding.cs:1005-1011`) | Fold the callee premises into `Retains`; obligations by (call, position) for F4 |
| C2 | Closure → captures (source, mode, mutable), receiver per view, sources per result position | `BoundClosure` (`B/Binding.Closures.cs:17-39`); `CaptureAcquisition` (`B/BindingModel.cs:77-98, 359`); sources transient (`Closures.cs:138-186`) | Cleared and refilled per pass (`Closures.cs:545-549, 656-662`); receiver mutated (`:669-842`) | `TyCtxt.Closure(k)` |
| C5 (S2) | call → Preserves; witness → preserves obligation | `DispatchBounds`/`CallableBounds` (`B/Binding.EffectObligations.cs:83-98`); eligibility (`B/Binding.EffectDeclarations.cs:654-675`) | Obligations are confined only (`:24-77`). The certificate loop reruns after ownership (`B/Binding.EffectBounds.cs:40-56`). | Add Preserves rows to `EffectObligation` |
| M39 | acquisition × case → outcome; default → template | On demand (`B/Binding.PairCases.cs:251-283`); interned adaptations (`B/Binding.SemanticsAdaptations.cs:129-161`); `DefaultDecl` (`CallPlan.cs:34-44`) | Adaptations immutable; `HirTables.Defaults` refilled per publish (`HirTables.cs:37`) | None new for A5 (`TyCtxt.Adaptation`, `CaseType` exist, `B/TyCtxt.cs:75, 80`) |
| E4/E5 | node → failure keys; failed node → bound children | `FailureCauses` (`B/Binding.Diagnostics.cs:341-343, 1568-1600`) | Reset per pass (`:1398-1440`); children published as Syntax (`B/Binding.Hir.cs:20-31`) | A key range on `MirOpaque`; publish bound children |
| U8p | Slice or view node → (backing Place, mode) | Slice Origin `PlaceOrigin` (`B/Binding.Ranges.cs:176, 246`); element-range adaptation (`B/Binding.ArgumentOperations.cs:506-513`) | `resolvedSlices` mutable (`Ranges.cs:20, 47`) | `Backing(node)`, or MIR issuing the Loan from the Slice Type's Semantics |

The plan cites DEC R4-M32 for late mutations. R4-M32 is superseded (DEC:863-875) and covered only `OriginFit`, which does
not exist in code. The actual rule is OBS13:74-75, and the mutation sites are the ones listed above.

## Findings confirmation

1. **Exactly two rules are not finitely expressible.** In scope, this is **confirmed**: only `preserves results`.
   - The C5 claim is confirmed with the refinement given in the hard items.
   - Two in-scope items are not SPEC changes but representation or clarification inputs: row 9's granularity and row 21's
     `g` (H).
2. **C3's other parts are expressible.** **Confirmed.** `default_retains` is derived by A5 from templates (finite fixed
   point), not "published" by Binding.
3. **A5-closure needs C2 only.** **Confirmed with conditions:**
   - C2 must also remove Binding's `Unsupported` (`B/Binding.ValueCalls.cs:531-534`).
   - It must carry per-view receivers.
4. **Writes through references are bounded by the output.** **Confirmed only with liveness pruning.** A write into dead
   holders is work that does not count toward the output.
5. **E5 is a DIAGNOSTICS change.** **Confirmed** (DIAG:76).
6. **Growing atoms are an "implementation decision".** **Refuted in part.**
   - Today they become `Unsupported_Kd` (`M/Analysis/MirEffects.Solve.cs:394-397`).
   - Widening them to unclassifiable rejects conformances whose exact summary is harmless. That narrows acceptance, so it
     needs a SPEC sentence like S22:374's open key.
7. **The case limit is a Resource failure.** **Confirmed** (DEC R4-M18; `B/Binding.CaseLimits.cs:55-61`).
8. **Q1-ref and S4.** **Confirmed** (S15:454). It decides row 16's `replace` verdict.
9. **Plan §4.2 producers.**
   - Corrected: `retains` is not a Binding fact today; see the inventory.
   - **Confirmed:** `call_effect` is not published (`M/Analysis/MirEffects.cs:8`).
   - **Verified in code:** `reserved` has the gaps in row 15 (`M/Build/MirBuilder.Values.cs:84, 175`). This upgrades the
     plan's (H).
   - **Confirmed:** `Kills` is split by StorageLive/StorageDead and EndBorrow only in the plan, not in code
     (`M/MirDescriptors.cs:22`).
   - **Confirmed:** there is no `MirDropRow` Path column (`M/Analysis/MirDrops.cs:40`).
10. **S2:**
    - It **keeps `takeTwo`** (plan :375; S8:687-690: same receiver Place, nothing between).
    - The **12 Iterator conformances** (`src/Kimi/Library/Iterator.kimi:6`; listed: `Array.kimi:275, 285, 294, 305`,
      `Core.kimi:328, 361`, `Dictionary.kimi:97, 107, 116`, `Iteration.kimi:25, 52`, `Slice.kimi:90`) are **kept (H),
      conditionally.**
      - Under "declared effects", `Storage.splitFirst(state: uniq/UniqRemainder<E>)` (`src/Kimi/Library/Storage.kimi:118`)
        reaches through the remainder's `uniq … during source` Loan Field.
      - S2 must state that the earlier results' phantom Loans are region-split children with fresh anchors (S8:774 "using
        region splitting"). Otherwise `ArrayUniqIterator` and `DictionaryUniqIterator` are rejected. The old path needs
        an exception for exactly this (`B/Binding.EffectBounds.cs:1846-1883`).
    - **S2 must add §8.11.5.** Its delegation is phrased by "value `w`" (S8:816). `OwningIterator` and
      `BorrowingIterator` delegate through it (`src/Kimi/Library/Iteration.kimi:31-32, 58-59`).
    - **Counts differ from the plan:**
      - xUnit: 18 files contain "preserves results" (the plan says about 25).
      - Milestones: 0 declarations, but 3 Iterator conformances that S2 governs (`tests/milestones/Milestone13.kimi:7`,
        `Milestone29.kimi:14, 46`).
    - The old path already uses receiver-Place identity without the replacement kill
      (`A/OwnershipAnalysis.cs:1717-1729`). S2 mostly specifies today's behaviour, plus the kill.
