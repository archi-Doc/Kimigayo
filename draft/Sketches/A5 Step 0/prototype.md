# Loan-rule prototype for A5 step 0 (SPEC §15.6, plan §4.1–§4.3, S1 and S1′)

## Summary

- The rules run on 39 hand-encoded programs (count corrected from 41). Under the final rules (S1′), all 39 match the expected verdict and error
  location.
- **UNSOUND: S1 as written accepts five programs the current SPEC rejects** (9g, 9m, 9n, 9o, 9h). 9n is the
  coordinator's program.
  - In each, the old parent reference leaves the base H through an exclusive borrow of H: `exchange`, `swap`, or a
    user callee that exchanges through a Reborrow.
  - The leaked reference then carries the parent Loan as authority, exactly as the child does.
- **S1′ rejects all five** and every other adversarial program.
  - Its load-bearing clause is **no exclusive borrow of H or an ancestor while the child is live**. Without it, 9g, 9m
    and 9h stay UNSOUND.
  - The `exchange`/`swap`-target clause catches only 9n and 9o.
  - The kill-only-on-destroying-replacements clause changes no verdict.
- S1's stated hypothesis held in every program and is still insufficient. S1′'s invariant ("nothing outside the
  child's descendants holds H's old reference") is the right statement.
- The literal plan §4.3 rules needed six refinements; removing any one makes a case fail (variant table below).
- The provenance rule set with one static Loan ID per site reproduces OBS13 (8a accepted).

## How to run

Copy `A5RulesPrototype.cs` (this folder; called `proto.cs` below) to an empty folder outside the repository, so
the repository's `Directory.Build.props` does not apply, and run it there:

```text
dotnet run proto.cs              # result table + variant table; exit code = number of cases S1′ fails
dotnet run proto.cs -- -v        # also every error fact (Loan, point, access) per rule set
dotnet run proto.cs -- -dump 9n  # per-point carries, liveness and Loan places for one case
```

- `proto.cs` is a single-file app for .NET SDK 11.0.100-rc.2, 1,376 lines after rounds 2–7.
- The first run takes about 30 s, mostly compilation; evaluation is a naive fixpoint.
- `out.txt` holds the last `-v` run.

## Model

- **Paths.**
  - Holders and Places are dotted paths: `p.left`, `refs.0`, `refs.[]` (non-path index), `r.*` (the referent of `r`),
    `cursor.*.children.[]`.
  - `within` is segment-wise; `[]` matches any index.
  - A parameter's referent is the escaped holder `^p`, named by an entry Loan `Lp` that `p` carries. `$ret` is escaped.
- **Facts per point** (builder methods in `Node`):
  - `Def` is `replaced`. `Flow` is `flow`.
  - `Borrow` is `issued`; it also generates the borrow access.
  - `Through` is `written_through`; it also generates a write access.
  - `Exchange` marks an `exchange`/`swap` target that is wholly replaced while its old value moves out.
  - `A` is `access`, with kinds read, write, move, sborrow, uborrow and destroy.
  - `Use` gives uses for liveness.
  - `Activate` ends a reservation. `Goto` and `Assume` give runtime and assumed edges.
- **Derived.** `reaches`, `names`, `child`, `carries`, `live`, `live_loan` and `error`. `child(L, L1, P)` is
  materialized: L1 ranges over the Loans carried at P by every reference the borrow path follows.
- **Rule sets.**
  - **PROV**: current SPEC rules with provenance places (§15.6.2 "Other followed references", plan §4.1) and one
    static Loan ID per site. No Loan kill.
  - **S1 as written** (plan §5.2):
    - A child Loan is named from its base (`r.*`).
    - It is killed when the base is completely replaced (including as an `exchange`/`swap` target) or its storage ends.
    - A Move of the base or an ancestor conflicts with the live child.
  - **S1′** (the plan's revised proposal; matches the fix I derived independently as "S1F"):
    - The kill happens only on `=`, `replace` and the end of storage.
    - An `exchange`/`swap` target ends the holder's carrying but not the child Loan, and is a Move access of H.
    - Move, exclusive borrow and `exchange`/`swap` target of the base or an ancestor conflict with the live child.
    - **These are the final rules.**

## Results

Expected verdicts come from the current SPEC. "S1 family" is the verdict S1 and S1′ intend: the SPEC verdict, except for
the listed losses, and for 9g, 9m, 9n, 9o and 9h, where the fixed rules report the same error earlier. An error
location is the program point that reports the error.

| Case | Program | Expected (SPEC) | Expected (S1 family) | PROV | S1 as written | S1′ (final) | Pass |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `var r = a@ref; r = b@ref; a = 3; inspect(r)` | valid | = | valid | valid | valid | pass |
| 2 | `let r = if ready => x@ref else => y@ref; x = 3; use(r)` | error @ `x = 3` | = | error @ `x = 3` | error @ `x = 3` | error @ `x = 3` | pass |
| 3 | slot / `r = slot@ref` / `copy = r@follow` / `consume(data@move)` / `inspect(copy)` | error @ `consume` | = | error @ `consume` | error @ `consume` | error @ `consume` | pass |
| 3b | 3 with `slot = other@ref` instead of `consume` | valid | = | valid | valid | valid | pass |
| 4 | 3 with `inspect(r@follow@follow)` | error @ `consume` | = | error @ `consume` | error @ `consume` | error @ `consume` | pass |
| 4b | 4 with `consume` before `let r = slot@ref` | error @ `consume` | = | error @ `consume` | error @ `consume` | error @ `consume` | pass |
| 5 | `refs[n] = a@ref; a = 3; inspect(refs)` (`[2 of ref/i32]`) | error @ `a = 3` | = | error @ `a = 3` | error @ `a = 3` | error @ `a = 3` | pass |
| 5b | `refs[0] = a@ref; refs[0] = z@ref; a = 3; inspect(refs)` | valid | = | valid | valid | valid | pass |
| 5c | `refs[n] = a@ref; refs[n] = z@ref; a = 3; inspect(refs)` | error @ `a = 3` | = | error @ `a = 3` | error @ `a = 3` | error @ `a = 3` | pass |
| 6 | `var r = x@uniq; let c = r@follow@uniq; r = y@uniq; x = 3; inspect(c)` | error @ `x = 3` | = | error @ `x = 3` | error @ `x = 3` | error @ `x = 3` | pass |
| 6b | 6 without `x = 3` | valid | = | valid | valid | valid | pass |
| 7 | `var q = p; q.left = c@ref; a = 3; inspect(q)` | valid | = | valid | valid | valid | pass |
| 7b | the same with `inspect(p)` | error @ `a = 3` | = | error @ `a = 3` | error @ `a = 3` | error @ `a = 3` | pass |
| 8a | `deepest` with `cursor.children.clear()` | error @ `clear()` | = | **valid (UNSOUND, OBS13)** | error @ `clear()` | error @ `clear()` | pass |
| 8b | `deepest` as written | valid | = | valid | valid | valid | pass |
| 8c | 8b: `$ret` carries root's Loan L0 at exit | yes | = | yes | yes | yes | pass |
| 9a | shared child copied, base replaced, `x = 3` | error @ `x = 3` | = | error | error | error | pass |
| 9b | child stored in an aggregate, base replaced | error @ `x = 3` | = | error | error | error | pass |
| 9c | reborrow r→s→d, replace s then r | error @ `x = 3` | = | error | error | error | pass |
| 9d | base replaced inside a loop, `x = 3` after | error @ `x = 3` | = | error | error | error | pass |
| 9d' | the same with `y = 3` | valid | = | valid | valid | valid | pass |
| 9e | reborrow through `h.link`, replace ancestor `h` | error @ `x = 3` | = | error | error | error | pass |
| 9e' | reborrow through `h.link`, `h.n = 5`, `h.link@follow = 7` | error @ `h.link@follow = 7` | = | error | error | error | pass |
| 9f | child passed through `pass(v) -> uniq/i32 during v`, base replaced | error @ `x = 3` | = | error | error | error | pass |
| 9g | `old = exchange(r@uniq, with: y@uniq)` lowered as a call; `old@follow = 7; inspect(c)` | error @ `old@follow` | error @ `exchange` | error @ `old@follow` | **valid (UNSOUND)** | error @ `exchange` | pass |
| 9m | 9g, then `r = z@uniq` before `old@follow = 7` | error @ `old@follow` | error @ `exchange` | error @ `old@follow` | **valid (UNSOUND)** | error @ `exchange` | pass |
| 9n | **coordinator's case**: `exchange(r@uniq, …)` as a complete replacement of r; `old@follow = 3; c@follow = 4` | error @ `old@follow = 3` | error @ `exchange` | error @ `old@follow = 3` | **valid (UNSOUND)** | error @ `exchange` | pass |
| 9o | `swap(r@uniq, s@uniq)` while c is live; `s@follow = 3; c@follow = 4` | error @ `s@follow = 3` | error @ `swap` | error @ `s@follow = 3` | **valid (UNSOUND)** | error @ `swap` | pass |
| 9h | c through `w = r@uniq`; `old = steal(w, with: y@uniq)` exchanges `w@follow` in the callee | error @ `old@follow` | error @ `steal` | error @ `old@follow` | **valid (UNSOUND)** | error @ `steal` | pass |
| 9i | `z = r@ref` while c is live; `inspect(z@follow@follow)` | error @ `inspect(z…)` | = | error | error | error | pass |
| 9j | `c = r@follow@follow@uniq` (r → slot → x); replace r; `x = 3` | error @ `x = 3` | = | error | error | error | pass |
| 9k | `let q = r@move` while c is live (S1's listed loss) | valid | error @ `r@move` | valid | error @ `r@move` | error @ `r@move` | pass |
| 9l | `let w = r@uniq; w@follow = y@uniq` while c is live | valid | error @ `r@uniq` (S1′ loss) | valid | valid | error @ `r@uniq` | pass |
| 10a | `Kimi.Intrinsics.replace(p@uniq, with: p + 1)` | valid | = | valid | valid | valid | pass |
| 10b | `tasks.append(tasks.remove(0))` | error @ `remove` activation | = | error | error | error | pass |
| 10c | `tasks.insert(tasks.length, Task.init(9))` | valid | = | valid | valid | valid | pass |
| 10d | `Kimi.Intrinsics.swap(p@uniq, p@uniq)` | error @ `swap` | = | error | error | error | pass |
| 11 | `let r = x@uniq; return 0; x = 3; inspect(r)` | error @ `x = 3` | = | error | error | error | pass |
| 11b | two assumed predecessors carry different Loans | error under union (ambiguous) | = | error | error | error | info |

Each error is reported at the expected point only.

- The `-v` output names the Loan. In 6, 9a–9f and 9j it is the materialized parent L1; PROV also reports the child.
- In 9n and 9o, S1′ reports L2 against both `UBorrow r` (the argument `r@uniq`) and `Move r` (the target's old value
  moving out).

### SPEC basis per case

Paths are under `docs/spec/`, in the a5-step0 worktree.

- **1**: 15-ownership-and-lifetime-analysis.md:454 ("Completely replacing a holder ends the Loans it carried") and the
  example at :462–463.
- **2**: 14-control-flow.md:427–428; joins are may-facts (15:455).
- **3, 3b**: 15:678. A Copy of a stored `ref/Data` "keeps only its external Loan".
- **4, 4b**: 15:453. A Loan is live while a value carrying it may be used. `slot`'s value is used later through `r`.
- **5, 5b, 5c**: runtime indices form no Move Path (15:58). A write through a non-path index ends no Loan (15:454).
- **6, 6b**: 15:513, "The child does not depend on the variable that holds the parent". A Reborrow depends on the
  referent and the parent Loan (13-operators-and-assignment.md:399; 03-types-and-values.md:359).
- **7, 7b**: partial replacement (15:454). `Pair` declares `Self is Copy` (§3.5.2).
- **8**:
  - 8a: the `clear()` receiver is an exclusive acquisition overlapping the live `next` (15:502–507).
  - 8b and 8c are plan §5.2.
  - `cursor = next` Reborrows (03:337), and `next` ends at the end of each iteration.
- **9g, 9m, 9n, 9o, 9h**:
  - `exchange` and `swap` transfer the old contents (15:644–647).
  - The holder that receives them (`old`, or `s` in 9o) then holds the parent capability. Access through it is
    suspended while the child is live (15:513).
  - The current SPEC reports the error at that access. The exchange itself is valid, because c is anchored on x, not on
    r's old contents (15:678).
- **9i**: reading x through `ref/(uniq/i32)` gives shared access (§3.5.1), which conflicts with the live uniq child.
- **9k**: S1's listed loss.
- **9l**: valid by 15:513 and 15:678. `r@uniq` borrows the slot, and the write replaces r's reference value, not x.
- **10**: 15:614–621. 10d is 15:665.
- **11**: 14:514, "All checks apply in this state: … a Loan conflict are errors". §14.10.3 has no Loan example, so the
  program follows its `consume(x@move); fail()` example.

## Failures, causes and fixes

### F1: S1 as written is unsound (9g, 9m, 9n, 9o, 9h); fixed by S1′

**Cause.** The old value of base H leaves through an exclusive borrow of H, carrying the parent Loan L1.

- L1 authorizes access through the leaked reference exactly as it authorizes access through c. Only the child Loan L2
  separated them.
- S1 loses L2 in two ways:
  - It kills L2. In 9n and 9o the exchange or swap counts as a complete replacement; in 9m a later `r = z@uniq` does.
  - Or L2 keeps naming `r.*` while H now holds another value, so access through `old` never meets it (9g, 9h).
- 9n dump under S1: L2 is not carried after the exchange. At `old@follow = 3` and `c@follow = 4` both holders carry
  only L1 and use it as authority.

**Lowering does not help.** 9g lowers `exchange` as a call and 9n as a whole replacement; both are accepted. In 9h a
user callee exchanges through a Reborrow of `w`, so no fact for `exchange` or `swap` in the caller can close it.

**Fix: S1′, the plan's revision.** Under S1′, 9n is rejected at the `exchange`, as are all the others.

**Which S1′ clause carries the weight** (variants of S1′):

| S1′ clause removed | Result |
| --- | --- |
| No exclusive borrow of H or an ancestor while the child is live (`NoDeepUBorrow`) | 9g, 9m, 9h **UNSOUND**. 9n and 9o are still rejected by the target clause. 9l is accepted. |
| Kill only on replacements that destroy the old value in place (`ExchangeKills`) | No verdict changes. An `exchange`/`swap` target is always exclusively borrowed, so the deep exclusive borrow rejects it first. |
| `exchange`/`swap` target as a Move of H | Subsumed by the exclusive-borrow clause in every case (9n and 9o fire both). |

So the exclusive-borrow clause is necessary, and it implies the other two clauses for these verdicts. Keep the other
two as statements of the invariant, not as separate checks.

**Rejected alternative.** Resolving the Loan's own place through the base at use (variant `S1ResolvePlaces`) accepts
9m (UNSOUND) and falsely rejects 9d'.

**Cost of S1′.** 9l: an exclusive borrow of a base while its child is live, which the current SPEC accepts. The same
applies to `exchange`, `swap` and `replace` of the base. Rust rejects these too, as a deep access to a borrowed
prefix.

### F2: PROV with one Loan ID per site accepts 8a (known; not fixed)

- The loop-head may-fact makes `cursor` carry Ln, and the access through `cursor` treats Ln as its authority.
- Ln's place set is `{^root.children.[], ^root.children.[].children.[], …}` up to the 8-segment widening cap (dump of
  8a).
- This reproduces OBS13 and the plan's §5.1 finding. Using must-authority instead would reject 8b at the reissue; I
  argued this and did not run it.
- S1 and S1′ remove the failure: `cursor = next` kills Ln, so `cursor` never carries its own site's Loan.

### Refinements over the literal plan §4.3 rules

Each row of the variant table removes one; every removal makes a case fail.

| Refinement (final rules) | Removed → result |
| --- | --- |
| R1. The Loan kill applies to every rule that propagates across P (keep, flow, written_through, child materialization), as Polonius `loan_killed_at` does. Plan §4.3 has only the holder kill on `keep`. | `KillKeepOnly`: 8a **UNSOUND**. `cursor = next` re-materializes Ln through the Reborrow of `next`. |
| R2. A reborrow materializes, at P, the Loans of every reference its borrow path follows, through all layers (9j). Ancestors already killed are not resurrected. | `NoMaterialize`: 6, 9a–9f, 9j **UNSOUND**. |
| R3. Only the Loan's own place is base-relative. Access Places are still resolved through provenance; all names are compared and the followed references' Loans (the authority) are excluded. | `SyntacticAccess`: 9i **UNSOUND**. `NoAuthority`: over-strict almost everywhere (6b, 8b, 9d', 10a). |
| R4. `live(T, P)` from the `names` closure feeds backward liveness like a use. | `NoClosureFeedback`: 4b **UNSOUND**. |
| R5. A flow out of a referent is derived through provenance. | `FlatFlow`: 3b over-strict. Plan §4.3 calls the flat flow unsound; with R4 it is over-strict instead, but still wrong. |
| R6. A reservation is shared until activation. Activation is an exclusive access that excludes its own Loan. | `NoReservation`: 10a and 10c over-strict. |

## Final rules (S1′)

```text
// Paths: Field names, constant indices (digits), [] (non-path index, matches any index), * (follow a reference).
within(K, H)        :- K is H or a descendant of H.
overlap(K, H)       :- within(K, H) ; within(H, K).
runs_through(p, H)  :- within(p, H), the segments of p below H contain *.
hits(a, l, deep)    :- within(a, l) ; within(l, a), (deep ; !runs_through(l, a)).   // inline parts exclude referents

// Provenance at P for p = h.*.s (h is the holder before the first *):
//   p ∈ names(p, P);  for every L with carries(K, L, P), overlap(K, h), and T ∈ place(L):
//     L ∈ auth(p, P);  names, leaves and auth of T.s are included;
//   p ∈ leaves(p, P) when p has no * or h carries no Loan (an escaped or unknown referent).
// place(L) = the issued place, base-relative for a child Loan (r.*, cursor.*.children.[]).
names(L, T, P)      :- T ∈ leaves(place(L), P).
reaches(p, T, P)    :- T0 ∈ leaves(p, P), T = T0 cut before its first [].

// replaced(H, P): =, replace, StorageLive/Dead.  exchanged(H, P): an exchange/swap target (old value moved out).
killed(K, P)        :- (replaced(H, P) ; exchanged(H, P)), within(K, H).
loan_killed(L, P)   :- replaced(H, P), runs_through(place(L), H).        // S1 as written: also exchanged(H, P)
access(P, H, move)  :- exchanged(H, P).                                  // S1′

// carries(H, L, Q): H carries L on entry to Q. No rule propagates a Loan killed at P.
carries(H, L, Q)    :- carries(H, L, P), !killed(H, P), !loan_killed(L, P), edge(P, Q).
carries(H2·s, L, Q) :- flow(H1, H2, P), S ∈ leaves(H1, P), carries(S·s, L, P), !loan_killed(L, P), edge(P, Q).
carries(H2, L, Q)   :- flow(H1, H2, P), S ∈ leaves(H1, P), carries(K, L, P), within(S, K), K ≠ S,
                       !loan_killed(L, P), edge(P, Q).
carries(T, L, Q)    :- written_through(p, H, P), carries(K, L, P), overlap(K, H), reaches(p, T, P),
                       !loan_killed(L, P), edge(P, Q).
carries(H, L, Q)    :- issued(L, _, _, P, H), edge(P, Q).
child(L, L1, P)     :- issued(L, p, _, P, _), L1 ∈ auth(p, P).
carries(H, L1, Q)   :- issued(L, _, _, P, H), child(L, L1, P), !loan_killed(L1, P), edge(P, Q).

// edge: runtime edges, plus assumed edges into points no runtime path reaches; joins are unions.

live(H, P)          :- use(H, P).
live(H, P)          :- live(H, Q), edge(P, Q), !killed(H, P).
live(T, P)          :- live(R, P), carries(K, L, P), overlap(K, R), names(L, T, P).  // feeds the line above
live_loan(L, P)     :- carries(K, L, P), live(H, P), overlap(K, H).
live_loan(L, P)     :- carries(H, L, P), escaped(H).                                 // ^param, $ret

// access facts: explicit, plus every issue (sborrow for ref or reserved uniq, uborrow for uniq),
// plus activation access(P, place(L), uborrow) excluding L itself when reserved(L, _, P).
uniq_at(L, P)       :- mode(L) = uniq, not (reserved(L, P1, P2), P1 < P ≤ P2).
conflicts(k, L, P)  :- uniq_at(L, P) ; k ∈ {write, move, uborrow, destroy}.
deep(k)             :- k ∈ {move, uborrow}.                                          // S1 as written: move only
error(L, P)         :- access(P, p, k), live_loan(L, P), L is not excluded, L ∉ auth(p, P), !entry_loan(L),
                       conflicts(k, L, P), a ∈ names(p, P), hits(a, place(L), deep(k)).
```

Under PROV, `place(L)` is the widened set of leaves of the issued place at its issue, `loan_killed` is empty and
`deep` is empty.

## S1 soundness verdict

**S1 as written is UNSOUND. S1′ is sound on every program tried.**

**The S1 hypothesis holds but is not enough.**

- In 9a–9f and 9j, every holder that reaches the old referent after a complete replacement carries the parent Loan,
  and the rules report each SPEC error through L1.
- The same holds through a Field base (9e), two reference layers (9j) and a back edge (9d, 8a, 8b), given R1, R2
  and R4.
- But in 9g, 9m, 9n, 9o and 9h the leaked old reference also carries the parent Loan. The hypothesis is satisfied and
  exclusivity still breaks.

**S1′'s invariant is the correct one.** "After the kill, nothing outside the child's descendants holds H's old
reference." The prototype shows what enforces it:

- the deep exclusive borrow, which is necessary;
- the deep Move, which is S1's own clause (9k).

The kill restriction and the target clause are implied for verdicts.

**Coordinator's two checks.**

- S1 as written accepts 9n, as predicted (UNSOUND).
- S1′ rejects 9n at the `exchange` and rejects every other adversarial program (all of 9a–9o pass).

**Losses.** 9k is S1's listed loss. 9l is the loss S1′ adds: an exclusive borrow, `exchange`, `swap` or `replace` of a
base while its child is live. Library and milestone use was not counted.

**Untested risks.**

- Authority is the may-set of the followed references. I argued it is safe because the kill keeps a site's earlier
  incarnation out of the accessor's set, and a sibling alias of a live child is caught at its issue. I did not prove
  it.
- A `written_through` into the base inside a loop is the next case to try.

## Limitations

- **Hand-encoded facts.** The lowering choices are mine: temporaries, `exchange` as a call (9g) or as a whole
  replacement (9n), the Reborrow at `cursor = next`, StorageDead at the end of the loop body.
- **Scope.** One Semantics case. No Types or Origin stratum, so Origin errors such as escaping locals go unchecked.
- **Layer-blind provenance.** Materialized ancestor Loans over-approximate resolution: in 9h, `$t.*` resolves to
  `{r, x}`. This is sound but imprecise; Origin positions (plan §4.1) would fix it.
- **Liveness.** No destruction observation (user `drop`, DEC R4-M6a runtime-only Drops). The `destroy` access kind
  exists but no case uses it.
- **Reservations.** Straight-line point ranges. No abandonment by control transfers. Formation is checked as a shared
  borrow.
- **Not modeled.** Effect summaries, statics, Closures, region splitting, Iterators and Slices, object borrows, raw
  Places. Escaped holders are only `^param` and `$ret`.
- **Assumed joins.** Union only (11b).
- **PROV widening.** Places are capped at 8 segments.
- **No bounded-work measurement** (plan §4.6).

## Open questions for the planning session

1. **§14.10.3, plan ambiguity (c).** At an assumed-only join, "keeping only what holds on all of them" read as
   intersection accepts 11b; the may-facts of §15.6 reject it. The prototype uses union.
2. **Current SPEC, not S1.**
   - `let z = r@ref` while an exclusive child of r is live, then `z` passed to a call that reads `z@follow@follow`.
     The caller sees `r` and `x` as disjoint (15:500, "Inline parts exclude referents"), so the text seems to accept
     the call.
   - Rust rejects it, and 9i (the read in the body) is rejected.
   - Worth a SPEC check; the prototype has no callee model.
3. **Plan §4.3 wording.** "A flat `flow(r, copy)` would be unsound here" should say over-strict (3b), given the
   `names` closure.
4. **S1′ text.** State the exclusive-borrow clause as the rule. The `exchange`/`swap`-target clause and the kill
   restriction follow from it. The MIR needs no special fact for `exchange`/`swap` beyond the `uborrow` of the target.

## Round 2 (plan e32dc0ef)

**Alignment.** `proto.cs` now uses the plan's names (§4.2–§4.3):

- `base(L)`: the first move path a child Loan's borrow path enters.
- `destroyed_in_place`: `=`, a `replace` target, and the end of storage.
- `ended(L, P) :- base(L, H), destroyed_in_place(K, P), within(H, K)`.
- A separate base-operation error rule, `base_op`.

S1′ now follows the plan's text: an exclusive borrow used as a `replace` target is not a base operation. All 39
earlier cases still pass (39 is the corrected count; earlier text said 41), including 9n and 9o.

| Case | Program | SPEC | S1′ | Prototype (S1′) |
| --- | --- | --- | --- | --- |
| 12a | `c = r@follow@uniq; if cond => stash(r@move); r = y@uniq; c@follow = 1` | error at `stash` | error at `stash`'s Move of r | error at the Move ✓ |
| 12b | 12a without `r = y@uniq` | error at `stash` | error at the Move | error at the Move ✓ |
| 13a | `rr = slot@uniq; c = rr@follow@follow@uniq; rr = slot2@uniq; c@follow = 1` | valid | valid (L2 ended; c keeps Lo, L1) | valid ✓ |
| 13b | 13a with `slot = y@uniq` (inner) | error there | error (Lo, carried by c) | error ✓ |
| 13c | 13a with `rr@follow = y@uniq` (inner, through the outer) | valid | valid (nothing ends) | valid ✓ |
| 13d | `rs = slot@ref; c = rs@follow@follow@ref; rs = slot2@ref` | valid | valid | valid ✓ |
| 13e | 13d with `slot = y@uniq` | error there | error | error ✓ |
| 9l | `let w = r@uniq` while c of `var r` is live | valid | error at `r@uniq` (loss) | error ✓ |
| 14 | `let g = h@move` (an ancestor of base `h.link`) while c is live | valid | error at the Move (loss) | error ✓ |
| 15a | `replace(r@uniq, with: y@uniq)` while c is live | valid | valid (exempt; ended) | valid ✓ |
| 15b | 15a, then `x = 3` | error at `x = 3` | error (L1 via c) | error ✓ |

**Modeling `stash`.** `stash` is a call that keeps its input in storage the body does not own. Its `uniq` parameter
gives the callee exclusive use of the referent during the call (15:529); that use is where the current SPEC errs (my
reading).

**Findings.**

1. **The Move ban carries the weight in 12a and 12b.** Variant `NoMoveBaseOp` accepts both (UNSOUND).
   - In 12a, `r = y@uniq` ends L2, although on the `stash` path r's old reference moved out instead of being
     destroyed.
   - In 12b, the stashed reference reaches x as `$v.*` and never meets L2's `r.*`.
2. **The plan's base-operation rule must range K over Places, not holders.**
   - Read literally (`within(H, K)` with K a holder, variant `BaseOpMovePathsOnly`), it accepts 9h (UNSOUND). There the
     base operation is an exclusive Reborrow of `w.*`, a referent path through base `w`, which no holder K covers.
   - Fix: `error(L, P) :- live_loan(L, P), base_op(P, K), runs_through(place(L), K)`, where K is any name of the
     accessed Place. For a move-path K this equals `within(base(L), K)`.
3. **The `replace` exemption is needed and sound.**
   - Without it (`NoReplaceException`), 15a is rejected.
   - It is sound because `replace` destroys the old value in place. 15b shows the parent Loan still protects x.
4. **Reference-held bases behave as the SPEC requires.**
   - Replacing the outer holder ends the child.
   - Replacing the inner holder conflicts with the outer reference's Loan, which the child carries.
   - Replacing the inner holder through the outer reference ends nothing and conflicts with nothing.
5. **`ExchangeKills` still changes no verdict.**

## Round 3 (design A, `S/design-A.md` §2, §3, §5, §6.1)

**A mode** (`Mode.A`, beside the unchanged S1′):

- P1 root holders plus `E` (`^param` maps to E). Partial writes and Moves add and end nothing; a part read reads the
  whole holder.
- P3: Move, shared borrow and exclusive borrow are deep.
- Syntactic access with no resolution or authority sets (R3 dropped). `replace` is an exclusive borrow (A §2).
- P10 content through direct Loans plus E.
- Liveness: borrowing T uses T; a check-time closure with no feedback.
- Timing: the check reads entry sets with live-in, where A specifies the sets after the point with live-out. The two
  coincide on these programs.

**Results over all 60 cases** (39 + Round 2's 10 + 11 new):

| Class | Cases (A's verdict) |
| --- | --- |
| Same verdict and location as the SPEC (44) | 1, 2, 3, 3b, 4, 4b, 5, 5c, 6, 6b, 7b, 8a–8c, 9a–9f (with 9d', 9e'), 9j, 10a–10d, 11, 11b, 13a–13e, 16a–16e, 17a, 17b, 18b, 19a, 19b |
| Same verdict, earlier error (9) | 9g, 9m, 9n, 9h (at the reservation `r@uniq`/`w`: a deep shared borrow), 9o (at `$a = r@uniq`), 9i (at `let z = r@ref`, P3), 12a, 12b (at the Move, as S1′), 15b (at `replace`) |
| (a) Extra rejection, listed loss (7) | 5b, 7, 18a: P1. 9k, 14: S1′ Move ban. 9l: S1′ exclusive-borrow ban. 15a: A's "replace counts as an exclusive borrow" (A §2), **which contradicts plan e32dc0ef's `replace` exemption** |
| (b) Unlisted loss | none after the fix below. **Before it: 19a, the in-place traversal** |
| (c) UNSOUND | **none** |

S1′'s verdicts are the SPEC verdicts, except for 9k, 9l and 14 (its losses).

**New adversarial programs.** Each matches the SPEC in A mode and under S1′, except 18a (P1).

| Case | Program | SPEC |
| --- | --- | --- |
| 16a | alias through a Reborrow: `a = r; c = a@follow@uniq; r@follow = 5; inspect(c)` | error at `r@follow = 5` |
| 16b | alias through a Copy: `z = r@follow@ref; z2 = z; c = r@follow@uniq; inspect(z2)` | error at `c = …` |
| 16c | alias in an aggregate: `c = r@follow@uniq; h = H{link: c}; r@follow = 5` | error at `r@follow = 5` |
| 16d | alias stored through a reference: `w = s@uniq; w@follow = r; r@follow = 5` | error at `r@follow = 5` |
| 16e | inner holder: `rr = slot@uniq; c = rr@follow@follow@uniq; slot@follow = 5` | error there |
| 17a | dead holders: `mid = slot@ref; top = mid@ref; consume(data@move); read through top` | error at `consume` |
| 17b | reference formed later: `w = slot@uniq; consume(data@move); t = w@follow@ref; read t` | error at `consume` |
| 18a | partial Move: `l = p.left@move; p.left = c@ref; a = 3; inspect(p)` | valid (A errs, P1) |
| 18b | partial Move: `l = p.left@move; a = 3; inspect(l)` | error at `a = 3` |
| 19a | in-place traversal: `cursor = cursor.children[0]@uniq` in a loop | valid |
| 19b | 19a plus `root.value = 2` | error there |

**Finding: (b) without a fix, in 19a, the in-place traversal (a common pattern).**

- Under the plan's issue rule (`carries(H, L, Q) :- issued(L, …, P, H)`, which has no `ended` check), the Loan issued by
  `cursor = cursor.children[0]@uniq` stays in `cursor`. Its place, `cursor.*.children.[]`, is named from base `cursor`.
- Without authority sets, every later access through `cursor` conflicts with it: the loop head and the reissue are
  rejected.
- S1′ accepts 19a only through its authority set.
- **Minimal fix:** a child Loan issued at a point that destroys its base in place is ended there too: `!ended(L, P)`
  in the issue rule. It is sound by S1′'s invariant, because the old reference is destroyed and the new value carries
  the parent Loans (19b still errs).
- Without the fix (variant `AIssueNotEnded`), 19a errs at the loop head and the reissue.

**Hypotheses.**

- Syntactic access without authority held on every case, given P3 and the fix.
- No closure feedback held: 4b passes because borrowing `slot` is a use, and 17a and 17b pass through the closure.
- P1 does not leak through a partial Move: the moved part takes the whole holder's Loans (18b). Its cost, 18a, is
  A §2's "Partial replacement ending a part's Loans".

**Conflict to resolve.** Design A narrows S1′ by treating `replace` as an exclusive borrow (15a rejected). The committed
plan exempts a `replace` target. Either A adopts the exemption, at the cost of a `replace` fact, or the plan drops it.

## Round 4 (Loan identity across loop iterations)

**The defect.** One Loan id covers every iteration of a site, so skipping a Loan by id also skips an earlier
incarnation that another holder still carries. Three places did this before the fix:

- A's letter: "L not issued at ℓ" over σ after ℓ (A:204, A:208), and activation `L ≠ R` (A:218);
- my activation exclusion `l == a.Except` (old proto.cs:385);
- my reservation mode, decided by straight-line position.

**The fix**, in both modes (the old rule is variant `IdExclusion`, A's letter is `AText`):

- An access at P is checked against entry sets over holders live after P, minus the holders P overwrites.
- An activation is checked over holders live into P, minus the reservation's own holder.
- A Loan is reserved only in its own reservation holder, between issue and activation. Reservations never pass through
  storage (§15.6.7), so any other holder carries an activated earlier incarnation, in loops too.

**Loop cases.** The current SPEC rejects all six: the earlier value is live when the site borrows again.

| Case | Program (in a loop) | S1′ by id | S1′ by holder | A by id | A text | A by holder |
| --- | --- | --- | --- | --- | --- | --- |
| 20a | `let cur = v@uniq; inspect(prev); prev = cur` | error | error | error | **valid: UNSOUND** | error |
| 20b | `let k2 = f(v@uniq)` (reserved, retained); `inspect(keep); keep = k2` | **valid: UNSOUND** | error | **valid: UNSOUND** | **valid: UNSOUND** | error |
| 20c | `let u = v@uniq; u@follow = 1; inspect(keep); let a = v@ref; keep = a` | error | error | error | error | error |
| 20d | `let s = v@ref; inspect(s); inspect(keep); let a = v@uniq; keep = a` | error | error | error | error | error |
| 20e | 20a with the child site `cur = r@follow@uniq` | error | error | error | **valid: UNSOUND** | error |
| 20f | 20b with a read of `v` inside the reservation window | **valid: UNSOUND** | error | **valid: UNSOUND** | **valid: UNSOUND** | error |

**Reading the table.**

- My entry-set modes caught 20a and 20e already: the Loan issued at P is not in P's entry set.
- A's letter uses σ after ℓ, so it must exclude the fresh Loan, and doing so by id hides the earlier incarnation.
- 20c and 20d mix two sites, so every variant rejects them. In 20c, S1′ also reports the write through `u`.

**Rerun of all 66 cases.**

- S1′ by holder: all pass.
- A by holder: no unlisted loss and no unsound acceptance.
- **The 60 earlier cases: no verdict or location changes** in either mode, and **no new over-strict rejection**. The
  output's Round 4 table lists only rows that differ, and only 20a–20f appear.

**Still by id.**

- S1′'s authority sets still skip Loans by id. I argued, without proving it, that this is safe: a site's reissue is
  checked by place against every live earlier incarnation (now including reserved formations), and S1′'s kill removes
  child incarnations.
- Design A has no authority sets.
- **Limitation.** Plain (non-reserved) arguments of one call are not checked against each other.

## Round 5 (plan fe1a5b34)

**Alignment** (both modes):

- `base(L, B)`: the prefix before the last Deref.
- `ended`: only for a move-path B, with `within(B, K)`. It is applied on every carrying rule (`step`).
- `base_op`: `runs_through(B, K)`, unchanged.
- (H) Drop: destroying a value with a `drop` is a Destroy base operation, and the drop's write through `self.r` is an
  access.

| Case | Program | SPEC | S1′ | A |
| --- | --- | --- | --- | --- |
| 21a | `_t = Ref(cursor.children[0])` at P1, `cursor = move _t` at P2, in a loop | valid | valid | valid |
| 21b | 21a plus `cursor.children.clear()` while `_t` is live | error at `clear` | error | error |
| 22a | `c = arr[i]@follow@uniq; old = exchange(arr[j]@uniq, with: z@uniq); old@follow = 7; inspect(c)` | error | error at `exchange` | error at the reservation |
| 22b | 22a with `arr[0]` and `arr[1]` | valid | valid | valid |
| 23a | `c = rr@follow@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1` | valid | **error** | **error** |
| 23b | `var q2 = q` (q a `uniq/(uniq/i32)` parameter); `c = q2@follow@follow@uniq; q2 = s2@uniq; q@follow@follow = 5` | error | error | error |
| 24a | `s = S(r: s.r@follow@uniq)`, S's drop writes through `self.r`; drop and store at separate points | error | error | error |
| 24b | the same, S without a drop | valid | valid | valid |
| 24c | 24a with the drop and the store at one point | error | error (after the fix below) | error (after the fix) |

**Findings.**

1. **Temp shape.** `ended` holds on the flow out of `_t`, so 21a and 19a pass, and 21b errs. PROV accepts 21b
   (OBS13).
2. **Index.** 22a's `arr.[]` may equal `arr.[]`. 22b's constants are distinct (§15.6.2).
3. **Reference-held bases.** With `B = rr.*`, replacing the outer holder no longer ends the child.
   - 9j, 13a–13e, 16e and 23b pass.
   - **Unlisted rejection, 23a**, in both modes: L2 still names `rr.*.*`, so a write through the new `rr` conflicts
     with it. This is the plan's open (H), whether `q = …` ends a Loan based at `(*q).p`.
   - `KillAcrossDeref` (round 2's rule) accepts 23a and changes nothing else. It is sound because the child carries
     the outer reference's Loan on the inner slot.
   - **Recommendation:** kill across the Deref, or list 23a as a loss.
4. **Drop.**
   - 24a errs in every mode.
   - **24c was UNSOUND in every mode before this round's fix.** The stored value is in transit from `$v`, which dies
     at P, and round 4's rule drops it.
   - **Fix:** the live facts at P also include the entry facts of every value P stores. `NoTransit` reproduces the
     hole; the fix changes no other case. The alternative is a MIR contract that destruction and store are separate
     points.
   - Design A's letter also accepts 24a and 24c: its σ after ℓ drops L2 at the kill before the drop is checked.

**Rerun: 75 cases.**

- S1′ fails only 23a (over-strict).
- A has no unsound acceptance; its one unlisted rejection is 23a.
- The earlier 66 cases are unchanged.

## Round 6 (review r2, the transit clause, plan b7705831's `within` kill)

**Changes.**

- Both modes:
  - the transit clause, with live destinations judged by overlap;
  - destroying calls as deep destroy accesses;
  - the shared-Deref cut.
- A mode: the plan's kill (last-Deref B, `within`, identical constant indexes only) and shallow destroying Refs.
- 23a is a listed loss.

| Case | Program | SPEC | S1′ | A |
| --- | --- | --- | --- | --- |
| 25a, 25c | f1 `x = f(x@ref)` (no drop), enum `x = pin(x@ref)` | error | error | error |
| 25b | 25a, x dead afterwards | valid | valid | valid |
| 26a, 26b | f2 `replace(s@uniq, …)`, `arr.clear()` | error | error | error |
| 27 | f3 shared field | valid | valid | valid |
| 28a–28d | f9 `g(a@uniq, a@ref)`, `g(a@ref, a@ref)`, disjoint Fields, stored `a@uniq` | E, V, V, E | same | same |
| 29a | the coordinator's 25a (crossing kill) | error | **valid: UNSOUND** | error (P3, at `w`) |
| 29b | escaped destination: `p@follow = S(r: p.r@follow@uniq)` | error | error | error |
| 29c | path-exact: `s.inner = f(s.inner@ref)` | error | error | error |
| 30a | ll1: obj links, `node = node.next…@follow@uniq` | valid | valid | valid |
| 30b | ll2: `uniq/Node` links | valid | valid | valid |
| 30c | ll3: ll2 plus a write through the old link | error | **valid: UNSOUND** | error |
| 30d | ll2 plus a read through the new link | valid | valid | error (23a family) |

**Findings.**

1. **Transit.** Without it, 24c, 25a, 25c, 29b and 29c are accepted. With it, 25b, 19a and 21a stay valid.
2. **Cut.** Without the shared-Deref cut, 27 is rejected.
3. **29a.**
   - A mode rejects it under either kill, through P3 at `w`.
   - S1′ accepts it under both kills: under `within` too, w's Loan (base `q`) ends at `q = o@uniq`.
   - S1′ plus P3 (`SharedBaseOp`) rejects it, so **the counterexample needs the absence of P3**.
4. **Linked lists.**
   - 30a: the obj payload is an owned step, so B = `node`.
   - 30b: the stale Loan sits in a dead `node`.
   - **30d is in 23a's family.**
   - **30c is UNSOUND in S1′**: `node` carries its own site's earlier Loan, and the id-based authority set excludes it
     (round 4's open risk).
5. **P3 plus the root-most kill** (A with `KillAcrossDeref`, S1′ with `P3RootMost`) is sound on all 92 cases and
   accepts 23a and 30d.

**Rerun: 92 cases.**

- **S1′, the plan's rules:** unsound on 29a and 30c. Every other case matches.
- **A:**
  - 70 same and 14 with an earlier error.
  - 8 listed losses: P1 (5b, 7, 18a); S1′ (9k, 9l, 14); the `within` kill (23a, 30d).
  - No unsound acceptance and no unlisted rejection.
- **Recommendation:** keep P3 in S1. With P3 the root-most kill is sound and loses nothing here. Keeping `within`
  instead costs 23a's family, including borrowed-link traversals that read through the new node.

## Round 7 (design A3, unit 0b)

**Additions.** Both modes now model A3's F6:

- `Φ_c` is a phantom exclusive Loan on the receiver's referent, carried by an abstract call's result.
- A disturbing point turns it into `Φ_c^dist` in every set.
- A later `preserves results` call on the same receiver excuses an undisturbed `Φ_c`.

| Case | Program | SPEC | S1′ | A |
| --- | --- | --- | --- | --- |
| 31a | `func f(a: uniq/T, b: uniq/T) => swap(a, b)` | valid | valid | valid |
| 31b | its caller `f(x@uniq, x@uniq)` | error at the call | error | error |
| 32a | `let h = x@ref; y = h@follow + 1; x = 3; inspect(y)` | valid | valid | valid |
| 32b | `var h = x@uniq; let c = h@follow@uniq; y = h@follow + 1; c@follow = 2` | error at the read | error | error |
| 33a | `takeTwo` (§8.11.3): `first = source.take(); second = source.take()` | valid | valid | valid |
| 33b | `takeAcross`: `source@follow = fresh@move` between the calls | error | error | error |
| 33c | 33a with the excuse at the Call only (A3's letter) | valid | **error** | **error** |

**Findings.**

1. **31a.** The two parameter reborrows activate against each other's holders. `a.*` and `b.*` have distinct roots, so
   they are disjoint (row 8, backed by the caller's activation, S15:614). The caller 31b errs at activation.
2. **32a.** The read through `h` copies `x`'s content, which carries no Loan, and the `+ 1` result carries none. So
   `x = 3` is valid. In 32b the read through the parent conflicts with the live child.
3. **Unlisted rejection, 33c, under A3's letter.**
   - `excused(L, ℓ) :- Call c2 at ℓ, …` applies only at the Call.
   - MIR forms the second receiver's TwoPhaseUniq Ref earlier, as a shared-borrow access, and it conflicts with
     first's live exclusive `Φ_c`.
   - **Fix:** excuse `Φ_c1` at c2's own argument reservations too, from their formation to the Call (33a). The excuse
     still applies only where c2 is a `preserves results` call on the same receiver, so 33b is unaffected.
4. **`Φ_c^dist` decided no verdict here.**
   - In 33b, the replacement `source@follow = fresh@move` already conflicts with first's live `Φ_c`, as the ordinary
     rule requires.
   - `^dist` adds the errors at the second call.
   - A disturbance changes a verdict only when the disturbing access does not itself hit `Φ_c`'s Place. An example is a
     write through an unrelated reference that the disturbance rule counts but the syntactic check misses. The
     prototype has no such case.

**Rerun: 99 cases.**

- **A:** 75 same, 15 with an earlier error, 8 listed losses, and one unlisted rejection, 33c (A3's letter). No unsound
  acceptance.
- **S1′:** unsound on 29a and 30c (round 6), and rejects 33c.
- **No new unsound acceptance.** The `Φ_c` code changes no earlier case.

## Round 8 (case 23a proposal review)

**Additions.**

- `FirstDerefBase`: A mode with B before the first borrow Deref and a definite matcher. The destroyed K is a move path
  and an exact prefix of B; B's later steps may be any index. `KillAcrossDeref` keeps round 2's `Within`, which
  matches a non-path index with any index.
- Cases:
  - 34a–34c: indexes;
  - 35a/35b: a Borrow kept during a `uniq`-linked walk;
  - 36a–36d: an F6 phantom through a replaced holder;
  - 37: 23a through a named intermediate.

| Case | Program | SPEC | A | `KillAcrossDeref` | `FirstDerefBase` |
| --- | --- | --- | --- | --- | --- |
| 34a | `var arr: [2 of uniq/i32] = [x@uniq, y@uniq]; c = arr[i]@follow@uniq; arr[0] = z@uniq; arr[i]@follow = 7; c@follow = 1` | error | error | **valid: UNSOUND** | error |
| 34b | 34a through two Derefs (`[2 of uniq/uniq/i32]`, `arr[i]@follow@follow`) | error | error | **valid: UNSOUND** | error |
| 34c | 34a with `arr = [z@uniq, w@uniq]` | valid | error | valid | valid |
| 35a | ll2: `node = next; b = node.value@uniq; node = next; b@follow = 1` | valid | error at the second step | valid | valid |
| 35b | ll2 loop: `keep = node.value@uniq; node = next`, `keep` used after the loop | valid | error at `_t` | valid | valid |
| 36a | `first = cur.inner.take()` (Φ_c only); `cur = b; cur = a; cur.inner.clear(); return first` | error | error | **valid: UNSOUND** | **valid: UNSOUND** |
| 36b | 36a, `first` also retaining the receiver | error | error | error at `cur = a` | error at `cur = a` |
| 36c | 36a with `a.inner.clear()` instead of `cur = a; cur.inner.clear()` | error | **valid: UNSOUND** | **valid: UNSOUND** | **valid: UNSOUND** |
| 36d | 36c, `first` also retaining the receiver | error | error | error | error |
| 37 | `t = rr@follow@uniq; c = t@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1` | valid | valid | valid | valid |

**Findings.**

1. **The may-overlap matcher is unsound.** Round 2's `Within` lets `arr[0] = …` end a child based at `arr[i]` (34a,
   34b). 34a has one Deref, so the variant changes single-Deref verdicts too. Round 6's "sound on 92 cases" for the
   root-most kill held because no case combined a non-path index with a constant-index assignment.
2. **The plan's matcher is narrower than S1's text.** It ends nothing across a non-path index after K, so it rejects
   34c. S1 §2.1 compares only the steps of K, and `FirstDerefBase` follows it.
3. **The `within` loss reaches the next step of a walk.** In 35a and 35b, a value Borrowed through the walk's holder
   carries the previous step's Loan, whose path names the new node's link. The next step's Borrow then conflicts.
   This is the `to_refs` shape: keep a Borrow of a node while advancing.
4. **Φ_c without the receiver's Loans is unsound in design A too (36c).** The phantom is the only Loan `first` keeps
   on the iterator, and its path names it through `cur`. After `cur = b`, nothing keeps `a`, so `a.inner.clear()` is
   accepted. `FirstDerefBase` also ends Φ_c at `cur = b`, so it also accepts 36a. When `first` retains the receiver's
   Loans, its parent Loans, both rules are sound (36b, 36d). Rounds 7–8 encode Φ_c without them (33a–33c, 36a,
   36c). A3 F6 must state whether Φ_c's values carry the receiver's Loans (PLAN G110, part 2). This blocks unit 2f
   and the adoption of the case 23a proposal.
5. **37:** 23a through a named intermediate is valid under every rule. `FirstDerefBase` makes the one-step form agree.

**Rerun: 109 cases.**

- **A:**
  - 78 same, 18 with an earlier error, 11 listed losses (35a, 35b and 34c added);
  - the unlisted 33c and **the unsound 36c**.
- **A with `FirstDerefBase`:**
  - 84 same, 16 with an earlier error, 6 listed losses (P1 and S1′'s base operations);
  - 33c, and **36a and 36c unsound**.
- **S1′:** fails 34c (loss) and 36c (unsound) as well as round 7's 29a, 30c and 33c. The exit code is 5.
