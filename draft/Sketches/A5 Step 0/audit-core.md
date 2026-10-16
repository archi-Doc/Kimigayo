# A5 rule-expressibility audit: F1, F2, F10

Date: 2026-10-16. Checkout: `bin/worktrees/a5-step0` (`b2edea8b`). Read-only. Plan = the step-0 plan snapshot.
Paths: SPEC chapters are cited as `S15:n` = `docs/spec/15-ownership-and-lifetime-analysis.md` line n (S3, S5, S13, S14, S16
likewise); code paths are relative to `src/Kimi/Compiler/`. (H) = unverified hypothesis.

## Summary

- Every in-scope rule except one is finitely expressible over MIR facts; most need a MIR fact contract first.
- **Not finitely expressible as written:** child Loans at a repeated site (row 9, C3). Cause: the current text creates a Loan
  per borrow evaluation and names a reborrow's place by its referent, so a reborrow loop needs unboundedly many Loan instances.
  Every finite summary by site makes the child its own parent: it either accepts the conflicting access (OBS13's failure) or
  rejects the valid reborrow. S1 fixes this. It needs two wording points: the base is a path whose prefix holder's whole write
  kills, and the base-Move ban is necessary, not optional. One "Lost" form must be added (raw fresh anchors, row 24).
- **§14.10.3 join:** not a (c) ambiguity. "What holds on all of them" keeps guarantees, so Loans (may-facts) are joined by
  union. This is S15:455 applied to every join, and A1 and `MirDataflow` already do it.
- **Plan §4.3 corrections:** the error rule is right only with base-path (S1) place naming; with referent naming it needs an
  authority exclusion that negates a may-fact. `EndBorrow` must be a use and then an end. `replaced` needs Move. Drops that
  only the checks reach must be Loan-checked under the current §14.10.3.
- **V16:** two bounds of plan §5.1/§4.6 fail as stated: per-holder liveness (an alias factor) and write-through
  (writes × reached × carried). Both hold with shared Loan-set nodes and event counting.
- **Producer gaps verified:** no backward liveness, no `Observes`, no drop-row Path, shared `Kills`, `TwoPhaseUniq` missing
  for written and Reborrow inputs, assumed continuations dropping reservations, and lazily created holder paths.

## Table

N points, H holders (H′ after the pre-pass), L Loans, A accesses, W writes through references, O = Σ_P live Loans + errors.

| # | Rule | SPEC | Class | New facts | Family | Bound / V16 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Program points | S15:448 | Expressible | — | F1 | Θ(N) ✓ |
| 2 | Loans carried by values | S15:452 | New fact | `flow`, `moved`, flow-closed paths | F1 | Θ(N+Σflow·carried) ✓ (live-pruned) |
| 3 | Liveness: may, lfp, destruction, no regions | S15:453–456, :469 | New fact | `live` (backward), `drop_observes`, `use` incl. EndBorrow | F1 | Loan part ✓; holder part dense (R4-M20) |
| 4 | Whole/partial replacement, Move Path granularity | S15:454; S15:58 | New fact | `replaced` (split Kills), flow-closed paths | F1 | Θ(writes) ✓ |
| 5 | Write through reference/index/container adds, ends none | S15:454 | Expressible | `written_through`, `reaches` (derived) | F1/F8 | ✗ as product; ✓ with shared sets |
| 6 | Join is union | S15:455; S14:427 | Expressible | — | F1 | Θ(heads·words) ✓ |
| 8 | Overlap, conflict table, provenance | S15:487–509 | Expressible (S1 for repeated sites) | `escaped` (MIR), `must_carries` or base paths | F1 | ≤ c·O ✓ |
| 9a | Reborrow child Loans | S15:513 | **Not finitely expressible** → S1 | `base`, `loan_killed` | F2 | Θ(Σissue·carried) ✓ |
| 9b | Region splitting | S15:515 | Expressible | — | F2 | as 8 ✓ |
| 14 | Destruction observation | S15:596; S15:507 | New fact | `drop_observes`, drop-row Path, check-only flag | F1 | Θ(drops·rows) ✓ |
| 20 | A Loan ends at last use | S15:312; S3:359 | Expressible | (row 3) | F1 | as 3 |
| 22 | Unreachable code; abandoned comparisons | S14:514; S15:614 | New fact (producer fix) | per-edge EndBorrow, assumed activation, check-only Drop checks | F10 | ≤ 2× rows 2–8 ✓ |
| 23 | Temporaries end; lent temporary is a Loan root | S3:427; S3:294 | Expressible | — | F1 | Θ(1)/StorageDead ✓ |
| 24 | `@raw` as ending `@ref`; fresh anchor | S5:101; S5:73; S15:496 | Expressible (site anchors: S1 Lost item) | `fresh(L)` (derived) | F1 | Θ(1) ✓ |
| 27 | Comparison inspection Loans | S13:189 | Expressible | EndBorrow as use | F1 | Θ(1) ✓ |
| 28 | Handle Loans; pair-layer dependencies | S13:353, :369; S13:399 | Expressible | `handle_loan` (derived) | F2 | Θ(path length) ✓ |
| 29 | Handle destruction | S16:194 | Expressible | (rows 8, 14, 28) | F1 | as 14 ✓ |

## Core rules (shared by the rows)

Corrections to plan §4.3, in the plan's notation. One view at a time (DEC R4-M11).

```text
// Inputs (MIR). Kills split into StorageLive / StorageDead / EndBorrow (G4).
replaced(H,P)   :- whole write of a Direct path H (Assign, Call destination), StorageLive or StorageDead of root H.
moved(H,P)      :- Move operand of a Direct path H.          // new: a Moved Place holds no value (S15:15)
use(H,P)        :- Read/Move/Borrow of a Place covering H; EndBorrow(H,P); observed Drop (row 14).
access(P,X,k)   :- k ∈ {read, ref, write, move, uniq, destroy}; destroy at Drop and StorageDead.
flow(H1,H2,P)   :- Assign of Use, Aggregate (operand i → H2·i), Cast Erasure/Upcast/Pointer, Adapt (per view); calls F3.
                   BinaryOp, UnaryOp, Discriminant and Len produce read Types and carry nothing (S3:409).

killed(K,P)     :- (replaced(H,P) ; moved(H,P) ; EndBorrow(H,P)), within(K,H).
carries(H,L,Q)  :- carries(H,L,P), edge(P,Q), !killed(H,P), !loan_killed(L,P).    // loan_killed: S1 only (§Hard items)
live(H,P)       :- backward may-analysis of use, ended by replaced/moved; P1 chains join runtime successors only (row 22).
live_loan(L,P)  :- carries(K,L,P), live(H,P), overlap(K,H).
live_loan(L,P)  :- carries(H,L,P), escaped(H).
error(L,P)      :- live_loan(L,P), access(P,X,k), overlaps(X,place(L)), conflicts(k, mode(L,P)).
```

- **Place naming decides the error rule.** With referent naming (current text, S15:495, S3:292), an access through R to its
  own referent overlaps R's own Loan. The rule then needs `!authorized`, with `authorized :- via(P,R), carries(R,L,P)`.
  That negates a may-fact, which is unsound at joins (§Hard items). A sound variant is a must-lane `must_carries`
  (intersection join). With base-path naming (S1, Polonius), a reference's own Loan lies on its parent path, never on a path
  through it, and the rule above is correct as written.
- **`EndBorrow` is a use, then an end.** An invocation or comparison whose operand diverges is not built
  (`Mir/Build/MirBuilder.Calls.cs:116-120`, `MirBuilder.Values.cs:395-401`, `MirBuilder.Operators.cs:42-47`). The
  EndBorrow before the transfer (`MirBuilder.Control.cs:135-142`) is therefore the pending operand's last use. If it only kills,
  the temporary is dead at once and the protection that SPEC keeps "while its operand is evaluated" (S15:614, S13:189) is lost.
- **`mode(L,P)`** is `ref` while `reserved(L,P1,P2)` and P1 ≤ P < P2, and the Loan's own mode otherwise. The conflict table of
  S15:504-507 then gives the reservation rule of S15:614 (F3 owns the activation check).

## Per-rule sections

**1. Program points.** S15:448: a point is before or after an operation. Facts: point = (MirLocation, before|after). Edges are
in-block order plus terminator successors (`MirDataflow` P1/P2, `Mir/Analysis/MirDataflow.cs:7-8`). Expressible, Θ(N). Tests:
valid S15:458-465. Invalid: `var a = 1; let r = a@ref; a = 3; inspect(r)` (Error at `a = 3`). Gap: none for points.
Static keys are F5. MIR has per-symbol static roots (`Mir/MirBody.cs:198-200, 441-450`) but no key dimension.

**2. Loans carried by values.** S15:452: the borrow, its Copies, derived references and holding storage carry the Loan. Rules:
`flow` above. Through a reference, the read takes the referent's Loans (plan §4.1). A child carries its parents because it is
a "reference derived from" the parent (S15:452). This is current text, not S1. New facts: `flow` (A5-derived), `moved` (MIR),
and holder paths closed under flow (G7). Bound: each flow costs |carried(source)|; flows into dead holders are skipped.
Tests: valid S15:458-465. Invalid (plan §4.3):
```kimi
var data = makeData()
var other = makeData()
var slot: ref/Data = data@ref
let r = slot@ref
let copy = r@follow        // copy carries the Loan on data, not the one on slot
consume(data@move)         // Error: copy is used below.
inspect(copy)
```
The valid twin replaces the `consume` line with `slot = other@ref`: r is dead and copy keeps only data's Loan (S15:678,
S15:686).

**3. Liveness.** S15:453 (used after P, destruction included), S15:455 (may, least fixed point), S15:456 and S15:469 (regions
never keep a Loan live). Rules: `live` and `live_loan` above. Loans flow forward; liveness runs backward. They form strata:
`carries` reads no liveness. New facts: `live` from a backward pass and `drop_observes` (row 14). Tests (back edge):
```kimi
var x = 1
var y = 2
var r = y@ref
while cond()
    x = 3          // Error: from the second iteration r carries the Loan on x and is used below.
    inspect(r)
    r = x@ref
```
The valid twin orders the body `x = 3; r = x@ref; inspect(r)`: r is replaced before its next use. The region test is row
20's. Gaps: no backward pass (G1), no `Observes` (G2). Bound: the Loan-level work is Θ(Σ holder status changes × |carried|)
(R4-M24's counting), within V16. Per-holder liveness costs heads × ⌈H′/64⌉ words, which V16 admits only through R4-M20's
dense-arena reading. With H copies of one reference live over N points, it is H·N against O = N.

**4. Replacement.** S15:454: a complete replacement ends the holder's Loans, a partial one ends the part's, at Move Path
granularity (S15:58). Rule: `killed` on Direct paths. A write through Deref or Index is row 5. A flow into H at P is not killed
by `replaced(H,P)` (plan §4.3). Tests:
```kimi
var x = 1
var y = 2
var z = 3
var b = (x@ref, y@ref)
var a = b
a.0 = z@ref        // ends a.0's Loan on x
x = 4              // Valid: a no longer carries it; b is dead.
inspect(a)
```
The invalid twin writes `a.1 = z@ref`: Error at `x = 4`. Gap G7: `b.0` has no path because no Place names it, so `a = b`
gives `a` the whole Loan set, and the valid program is rejected. Paths must be closed under flow, or use Origin positions.

**5. Write through a reference, a non-path index or a container.** S15:454: it adds Loans to every Place it may reach and ends
none. S4 confirms this; `72fb4b79` removed the old "Complete replacement ends…" sentence (`git log -S`). Rule: plan
`written_through`/`reaches`. Tests:
```kimi
var a = 1
var b = 2
var slot: ref/i32 = a@ref
let r = slot@uniq
r@follow = b@ref       // ends nothing
a = 3                  // Error: slot may still carry the Loan on a.
inspect(slot)
```
The valid twin writes `slot = b@ref` directly. Non-path index: with `var refs: [2 of ref/i32] = [x@ref, y@ref]`,
`refs[i] = y@ref; x = 3; inspect(refs)` is an Error, and `refs[0] = y@ref` instead is Valid. Bound: plan §5.1 claims
"bounded by the output", which is refuted. A write through r reaching k holders with a value carrying m Loans makes k·m facts
against O ≈ k+m. Example: r and v are each chosen by k-way `if` chains. The bound holds when a holder points to a shared,
hash-consed Loan-set node: the write is then k pointer unions.

**6. Join.** S15:455; the example S14:427-429. Rule: union at heads (`MirDataflow.cs:284-302`). Tests: invalid S14:427-429.
The valid twin moves `x = 3` after `use(r)`.

**8. Overlap and conflicts.** S15:487-507. `overlaps` follows the table: ranges for move paths (R4-M21), the prefix path for
Index, escaped holders for parameters' referents, and provenance (current) or base paths (S1). The conflict table is
S15:504-507. New fact: `escaped(H)` comes from MIR alone for reference-typed `Arg` locals and statics; captures need C2 (F7). The
plan names Binding for parameters, which is not needed. Tests:
```kimi
var pair = (1, 2)
let a = pair.0@uniq
let b = pair.1@uniq     // Valid: distinct Tuple elements (S15:493).
// let c = pair@ref     // Error: overlaps the live exclusive Loan on pair.0.
a@follow = 3
b@follow = 4
```
Further tests: S3:298-305 (a slot borrow blocks replacing the slot), and `items[0]@uniq` with a live `items[i]@ref` (Error,
S15:500). Observation, a possible (c): the table proves disjointness only between two live `uniq` referents (S15:494). Read
literally, `func f(p: ref/i32, q: uniq/i32)` with `let r = p@follow@ref; q@follow = 5; inspect(r)` is an Error ("Unproven",
S15:497). (H) The rule probably means a live `uniq` referent is disjoint from any other live borrow with a distinct anchor.
Ask the owner.

**9a. Child Loans.** S15:513: borrowing through an exclusive borrow creates a child anchored on the referent. Access through
the parent is suspended while the child is live, and the child does not depend on the parent's holder. Rule (S1): §Hard items.
Tests: S3:367-377 (valid; its commented `r@follow += 1` is an Error), S13:403-407, and plan §4.3's `r = y@uniq` (Valid) /
`x = 3` (Error). Class: not finitely expressible at a repeated site.

**9b. Region splitting.** S15:515: exclusive children and the remainder are pairwise disjoint; shared children may overlap; a
Reborrow through a child stays in it. Rule: rows 8 and 9a. Forming child 2 is an access through the parent and is checked
against live child 1. The library's raw split is row 24 plus F3 descent. Tests:
```kimi
var pair = (1, 2)
let r = pair@uniq
let a = r@follow.0@uniq
let b = r@follow.1@uniq      // Valid: disjoint exclusive children.
// let c = r@follow.0@ref    // Error: overlaps the live exclusive child a.
a@follow += 1
b@follow += 1
```

**14. Destruction observation.** S15:596: Loans a destruction may observe must be valid there. A user `drop` observes every
reachable Origin; destruction that observes none adds nothing. S15:507: destroying a Place conflicts with live Loans on it.
Rule: `use(H,P)` for each part a Drop destroys whose Type's destruction observes Origins, read from the A2 rows. Elide uses
nothing; Open uses its own layer, then its child rows. `access(P,X,destroy)` at Drop and StorageDead. Tests:
```kimi
var w = makeWriter()
let logger = Logger.init(w@uniq)   // Logger as in S15:598-604 (its drop observes sink)
w = makeWriter()                   // Error: logger's drop observes the Loan on w.
```
The valid twin is `let pair = (w@uniq, makeResource())`. The Tuple declares no drop, and Resource's drop reaches no Loan
on w. Gaps: no `Observes` (G2), no row Path (G3), check-only and not analysed are conflated (G3).

**20. Last use.** S15:307-313: `child` has the fixed Origin `bound`, whose region is the whole body, yet `source@follow` is
Valid after child's last use. Regions keep nothing live (S15:456). The invalid twin is `let v = source@follow` placed before
`child@follow = 42` (Error). A call-free test is S3:367-377. Expressible through row 3.

**22. Unreachable code (F10).** S14:514: an item without a real path uses the join of its assumed paths. All checks apply,
Loan conflicts included, and Loans only an abandoned comparison needed end before the assumed continuation. S14:389 and
S14:514: assumed paths add no Runtime Reachability edge. Rules:
- `carries` uses P1 and P2 edges (exists, `MirDataflow.cs:236-270`).
- `live` joins runtime successors in P1 chains and all successors in P2 chains (R4-M24's reading).
- A Drop that only the checks reach is still a destroy access and an observation. The current text says "Cleanup is checked
  the same way" and "all checks apply". The runtime-only restriction of R4-M6a and `docs/dev/PLAN.md:31` covers drop elaboration and the
  destructor rule, not Loan checks. With `moved` in `killed`, such a Drop needs no A2 verdict.
- A reservation survives the assumed continuation. Only comparison Loans end there.

Tests:
```kimi
var x = 1
let r = x@ref
return
x = 2              // Error: Loan conflict in the assumed state; r is used below.
inspect(r)
```
- Valid twin (per R4-M24's reading): `x = 2` before `return`. r's only later use is unreachable.
- Runtime protection: in `func h(text: string) -> string`, `let s = text == (return text@move)` is an Error (S13:189), and
  `… == (return "b")` is Valid.
- Assumed end: `if text == (return "b")` followed by `consume(text@move)` in its body is Valid.
- Reservation:
```kimi
// three(a: uniq/i32, b: i32, c: uniq/i32) -> ()
var p: i32 = 1
three(p@uniq, (return), p@uniq)   // Error (assumed state): overlapping exclusive reservations. Current MIR accepts it.
```
Gap G6: the reservation case. (H) on the parenthesized-transfer syntax; `[exit: value]` (S14:155) shows that transfers are
operands.

**23. Temporaries.** S3:427: a temporary ends with its outermost expression, and a borrow of it cannot outlive it. Its lending
point is the temporary (S3:294). Rule: a Temp's Drop and StorageDead are destroy accesses (`Mir/Build/MirBuilder.Drops.cs:71-81`).
Tests: S3:429-431 (Error). `let rr = makeRef()@ref` then `inspect(rr)` is an Error: the Loan is on the temporary slot. `let r =
makeRef()` then `inspect(r)` is Valid.

**24. `@raw` and fresh anchors.** S5:101: it checks like an immediately ending `@ref` and leaves no Loan. S5:73 and S15:496: a
raw-Place borrow's anchor is fresh and is compared only with Places derived from it. Rule: a Ref of kind Raw (`MirVocabulary.cs:114`)
is `access(P,X,ref)` and issues nothing. `fresh(L)` holds when the Ref's Place passes a raw Deref (`MirMoveData.Raw`). Tests:
S5:105-114 (Valid). `let r = number@uniq; let p = number@raw; r@follow += 1` is an Error. Finding: the current text gives each
evaluation its own anchor. A site summary, or S1, makes two live borrows from one raw site in a loop share one anchor. So
`refs[0]@follow@uniq`, live while `refs[1]@follow = 5` writes through the other borrow, is Valid now and an Error under site
identity. Add this to S1's "Lost" column (lib/ms (H) 0).

**27. Comparison inspection Loans.** S13:189. Binding marks a non-Copy operand `SharedBorrow` (`Binding/Binding.Hir.Adjust.cs:167-178`),
and MIR forms a shared Ref into a pending temporary used by the BinaryOp (`MirBuilder.Operators.cs:42-47`). The Loan is live
while the right operand runs, and the bool carries nothing. Tests: S13:192-200. Expressible, given that EndBorrow is a use.

**28. Handle and pair layers.** S13:353: a borrow into an object also holds a Loan in its own mode on the handle it was reached
through. S13:369 and S13:399: a pair layer or Reborrow depends on the referent and the parent Loan, not the slot. Rules:
`handle_loan(L′,L)` for each Deref of an obj/rc/arc/objref/objuniq in the Ref's Place, from projection Types. PairLayer is
resolved per view (`MirCases.Layer`). Tests:
```kimi
var h: obj/Item = makeItem()@obj     // Item is Sealed
let view = h@follow@ref
let other = h@objref                 // Valid: a shared handle Loan permits reading.
// h = makeItem()@obj                // Error: view is used below.
inspect(view)
```
Pair layers: S13:375-379 is Valid. With `s is owner or ref`, `let r = c.item@follow@ref` followed by
`Kimi.Intrinsics.replace(c.item@uniq, with: fresh@move); inspect(r)` is an Error (the owner case borrows the slot).

**29. Handle destruction.** S16:194: destroying a handle compares only the handle Place. Payload storage is never the target,
because every payload borrow holds a handle Loan. Rule: rows 14 and 28. An rc/arc release observes what payload destruction
observes. Tests: replacing an unrelated handle `g` while `view` borrows `h`'s payload is Valid. The invalid case:
```kimi
var view: ref/Item = fallback@ref
do
    let h: obj/Item = makeItem()@obj
    view = h@follow@ref
inspect(view)        // Error at h's destruction (S16:194, S15:577).
```

## Hard items

### C3: repeated-site parent/child Loans

Program (helper `first(t: uniq/Tree) -> uniq/Tree during t`):
```kimi
func deepest(root: uniq/Tree) -> uniq/Tree during root
    var cursor = root                // P0: Reborrow, L0 on root's referent
    while cursor.children.length > 0
        let next = first(cursor)     // P1: site S, exclusive Reborrow of cursor's referent (an argument)
        // cursor.value = 0          // Error: access through the parent while next is live
        cursor = next@move           // P2
    return cursor
```

**Current text.** A borrow creates a Loan (S15:513) whose place is its referent (S3:292). Places through references are
compared by provenance (S15:495). The facts per iteration k are:
- `issued(L_S^k, place ⊆ place(L_S^{k−1}), uniq, P1, t)` and `child(L_S^k, L_S^j)` for j < k. Parents are materialized,
  because a derived reference carries the parent (S15:452).
- `cursor = next@move`: cursor carries {L0, L_S^1 … L_S^k}.

At the loop head the carried set grows without bound, so the least fixed point (S15:455) is an infinite ascending chain, an
unbounded domain. Even without loops, the definition "a Loan is (place, mode)" (S15:450) cannot be the identity:
`let c = r@follow@uniq` gives the child and the parent the same (place, mode).

Any finite summary by site (one L_S) yields `child(L_S, L_S)`. At P1 in iteration 2, cursor carries L_S on the back edge but
not on the entry edge:
- with the authority exclusion on may-`carries`, `cursor.value = 0` is accepted (unsound, OBS13's failure);
- with a must-lane, the valid Reborrow `first(cursor)` itself conflicts with the summarized live L_S (a false error).

No finite identity is given by the text, and an invented one changes verdicts. Class: **not finitely expressible**, and it
needs a SPEC change.

**Under S1 (base paths, Polonius kill):**
```text
base(L,B,π)       :- issued(L,X,_,P,_), X = (*B).π, B the holder of the reference dereferenced (from the Ref's Place chain).
loan_killed(L,P)  :- base(L,B,_), (replaced(K,P) ; StorageDead(K,P)), K is B or a Move-Path prefix holder of B.
error(L,P)        :- live_loan(L,P), access(P,X,k), overlaps(X,(*B).π), conflicts(k,mode(L,P)).   // paths compared syntactically
error(L,P)        :- live_loan(L,P), base(L,B,_), moved(K,P), K is B or a prefix of B.          // the base cannot be Moved
```
Facts:
- P1: `issued(L_S, *cursor, uniq, P1, t)` and `base(L_S, cursor, ε)`. next carries L_S and cursor's Loans.
- P2: `replaced(cursor)` and `loan_killed(L_S)`.
- The loop head stabilizes after one pass with cursor carrying {L0}. In iteration 2 the child is `child(L_S, L0)`, never itself.
- `cursor.value = 0` overlaps `(*cursor)` while L_S is live: an Error. `next.value = 1` is Valid.

Bound: Θ(Σ issues × |carried(parent)|) plus kills, indexed by base: each Loan dies once per kill point.

Cross-check against S1–S4. S1 resolves the item; S2 and S3 are unrelated. S4 is coherent with S1: kills come only from whole
writes of Move Paths, never from writes through references (Polonius kills only on local overwrites). Findings for S1's text:
1. **The base-Move ban is necessary.** Without it:
```kimi
var p = x@uniq
let c = p@follow.a@uniq
let q = p@move
p = y@uniq            // kills c's Loan
q@follow.a = 1        // now accepted: c and q both carry only x's Loan
use(c)
```
   This is unsound; the current text rejects the write through q.
2. **(H) The base must be the access path.** "Completely replacing H" must cover a whole write of any Move-Path prefix
   holder. In `let n = cursor.next@follow@uniq; cursor = n@move` in a loop, the base `(*cursor).next` is never itself
   replaced, so the next iteration's Reborrow conflicts with its own stale child.
3. Add the row 24 form to "Lost".
4. **(H)** The kill's interaction with split children and shared bases is for the `draft/Sketches` prototype.

### §14.10.3 assumed-path join

- **Text.** S14:514 says "keeping only what holds on all of them". What holds is the guarantees an operation needs:
  initialized, not Moved, Effective Types, condition facts, and "no conflicting live Loan".
- **Same pattern elsewhere.** S14:498 keeps "the most derived base guaranteed by every incoming path".
- **Guarantee reading.**
  - S15:455 governs every join: "after a join a Loan is live if it is live on any incoming path".
  - S14:514 applies the checks "as on real paths".
  - S15:18: "the absence of a path never makes an unusable value usable".
- **Rejected reading.** Intersecting Loan sets would let a two-way assumed join accept a conflict that either path alone
  rejects, which breaks S15:18.
- **Code.** A1 already implements the guarantee reading as union over MaybeUninit (`Mir/Analysis/MirInit.cs:8`), and
  `MirDataflow` joins by union in P2 (`MirDataflow.cs:7-8`).
- **Verdict:** must-facts keep their intersection, and Loans join by union. **Not a (c) ambiguity.** An editorial note
  ("a Loan live on any assumed path stays live") is optional.

## Findings confirmation (plan §5.1, in scope)

| Finding | Verdict | Evidence |
| --- | --- | --- |
| Reborrow Loan identity at a repeated site is not finitely expressible | Confirmed. Cause refined: Loans per evaluation; the site summary fails both ways | §Hard items; S15:450, :452, :495, :513 |
| Places are named by carried Loans, giving `root.children[0]…` without bound | Confirmed | S3:292, S15:495 |
| A static ID per site plus authority accepts access through the parent | Confirmed. The must-authority alternative gives false errors instead | §Hard items |
| The other §15 rules in F1/F2/F10 are expressible | Confirmed. Rows 22 and 24 need producer fixes, row 24 an S1 note | Table |
| Write-through cost is bounded by the output | **Refuted** as stated; holds with shared set nodes | Row 5 |
| Q1-ref is not a rule; S4 only confirms | Confirmed: S15:454; `72fb4b79` removed the old sentence | Row 5 |
| S1 loses only base Moves | **Extended**: also row 24's raw-site form; the base-Move ban is necessary; prefix kill (H) | §Hard items |
| The kill option is sound because the reborrow holder carries its parents | Confirmed as the current text (S15:452), but only together with the base-Move ban | §Hard items |
| (H) Some call inputs form plain `Uniq` | **Verified** | G5 |

## New facts and producers

| Fact | Meaning | Producer / owner | Kind |
| --- | --- | --- | --- |
| `replaced`, `StorageDead`, `EndBorrow` (split of `Kills`) | Fresh lifetime; storage end with destroy access; use-then-end of a reference temp | `MirDescriptors` (MirAccess rows; vocabulary, owner O1) | Input |
| `moved(H,P)` | A Moved Place carries nothing | `MirUses` (Moves on a Direct path) | Input |
| `use(H,P)` | Read/Move/Borrow, EndBorrow, observed Drop | `MirUses` + row 14 | Input |
| `live(H,P)` | Backward may-liveness | `MirDataflow` backward pass (to rebuild) | Derived |
| `drop_observes(P,path,view)` | Parts whose destruction observes Origins | `MirTypes.Observes` over `Destruction` (to rebuild) × `MirDropTable` rows with Path | Input |
| check-only vs not analysed | Separates Group −1 cases | `MirDropTable` (`MirDrops`) | Input |
| flow-closed holder paths | Part precision across flows | `MirMoveData`, or the Origin-position representation (step-0 choice) | Input |
| `flow(H1,H2,P)` | Value flow, part-wise | A5 from Assign rvalues; calls from C1/C8 (F3) | Derived |
| `escaped(H)` | Referents of reference `Arg` locals, statics | MIR locals; captures via C2 (F7) | Input |
| `base(L,B,π)`, `loan_killed(L,P)` | S1 identity and kill | A5 from the Ref's Place chain | Derived |
| `fresh(L)`, `handle_loan(L′,L)` | Raw anchors; handle Loans | A5 from projection Types (`MirMoveData.Raw`, `TypeOf`) | Derived |
| `reserved(L,P1,P2)` | Two-phase reservation | `MirBuilder` emitting `TwoPhaseUniq` on every reserved input (G5) | Input |
| assumed activation / per-edge EndBorrow | Reservations survive assumed continuations | `MirBuilder.Control`/`.Calls` (G6) | Input |
| `must_carries` (only without S1) | Authority as a must-fact | A5 forward intersection lane | Derived |

**Producer gaps (verified in code):**
- **G1.** `MirDataflow` has only `Forward` (`Mir/Analysis/MirDataflow.cs:202`). The backward pass left with `0d989e68`.
- **G2.** `MirTypes.Observes` is absent (`Mir/MirTypes.cs`). It was removed by `0d989e68`.
- **G3.** `MirDropRow` has no Path (`Mir/Analysis/MirDrops.cs:40`; `0d989e68` deleted `int Path`). `Group` returns −1 both for
  check-only Drops and for bodies not analysed (`MirDrops.cs:86-88`).
- **G4.** `Kills` covers StorageLive, StorageDead and EndBorrow (`Mir/MirDescriptors.cs:23, 77-79`). A1 switches on the
  statement kind instead (`MirInit.cs:112-121`).
- **G5.** `TwoPhaseUniq` is formed only by adapted call inputs (`Mir/Build/MirBuilder.Values.cs:196-199`, `MirTypes.cs:128-141`).
  The following are plain `Uniq`:
  - written `@uniq` arguments (`MirBuilder.Values.cs:84`), so S15:619 `replace(p@uniq, with: p + 1)` is rejected;
  - Reborrow acquisitions at `uniq` parameters (`MirBuilder.Values.cs:175`);
  - synthesized receivers (`MirBuilder.Expansions.cs:396, 443`; `MirBuilder.Formatting.cs:70, 81, 87`).
- **G6.** The EndBorrow of every pending reference operand precedes a Goto with both edges (`MirBuilder.Control.cs:135-142, 167`).
  An invocation with a diverging operand is not built (`MirBuilder.Calls.cs:116-120`). So reservations end, and are never
  activated, on the assumed path (S14:514, S15:614).
- **G7.** Move paths are created only for Places a use names (`Mir/Analysis/MirMoveData.cs:157-186, 259-280`).
