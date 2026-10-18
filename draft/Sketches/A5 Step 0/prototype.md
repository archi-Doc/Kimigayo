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
dotnet run proto.cs -- -cases Round11 NoOwnValue  # one tab-separated row per case and named variant (round 12)
dotnet run proto.cs -- -cases None+PK NoDestroyValue+PK  # +PK runs a variant on the design (round 13)
dotnet run proto.cs -- -r13      # only round 13's tables
```

- `proto.cs` is a single-file app for .NET SDK 11.0.100-rc.2, 4,227 lines after round 13 and its 2026-10-18 correction.
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
   36c). A3 F6 must state whether Φ_c's values carry the receiver's Loans (PLAN G113). This blocks unit 2f
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

## Round 9 (PLAN G113: parent Loans of `Φ_c`)

**Cause.** A3's `out` adds `Φ_c` alone. A descending result names only Origins of the receiver's referent Type, so
the receiver is retained by `Content`, and `ret(i, Content) = content(val(a_i))` omits `val(a_i)`: the receiver
Loan R and its R2 parents. A `preserves results` call always has this form (§8.11.3 eligibility). `Φ_c`'s Place runs
through the receiver's borrow Derefs (`cur.*.inner.*`), and distinct roots are disjoint only because a value that
carries a Loan through a reference also carries that reference's Loans (§15.6.2, §15.6.3; A3 §3). So an access to the
same referent by another name (`a.*.inner.*`) meets no Loan of `first` (36c).

**Rule** (A3 erratum): `desc(c) = {Φ_c} ∪ (val(a_r) \ {R_c})`. Here `a_r` is the receiver operand and `R_c` the Loan
of the Ref that produces it, if any. `val(a_r)` is R_c plus the R2 parents of `recv(c)` at the Ref. So the result
keeps `Φ_c` with the parent Loans a Reborrow of the receiver would have, but not R_c itself. Like every
`ret(i, κ)`, the rule reads the call's operand.

| Variant (A mode) | Rule instead | Result |
| --- | --- | --- |
| `PhantomAlone` | rounds 7–8: `{Φ_c}` | 36c, 36e and 36g **UNSOUND** (36a too with `FirstDerefBase`, round 8) |
| `PhantomKeepsReceiver` | `{Φ_c} ∪ val(a_r)` | 33a rejected at the second reservation: R_c is not excused. A result that keeps the receiver Loan contradicts §8.11.3 |
| `PhantomParentsAtCall` | `issued(Φ_c, recv(c), …)` at the call: R2 parents from `recv(c)`'s root at ℓ | Same verdicts, except that it accepts 36e, whose `do` argument writes `cur` as 36h does. SPEC rejects that write, but A3 misses it |

| Case | Program | SPEC | A | `FirstDerefBase` |
| --- | --- | --- | --- | --- |
| 33a | `takeTwo` | valid | valid | valid |
| 33b | `takeAcross` | error | error | error |
| 33c | 33a under A3's letter (round 7) | valid | error (unchanged) | error (unchanged) |
| 36a | `first = cur.inner.take(); cur = b; cur = a; cur.inner.clear()` | error | error at `cur = a` and the clear | error at `cur = a` (round 8: **UNSOUND**) |
| 36b | 36a, first retains the receiver | error | error | error at `cur = a` |
| 36c | `first = cur.inner.take(); cur = b; a.inner.clear()` | error | error at the clear (round 8: **UNSOUND**) | error (round 8: **UNSOUND**) |
| 36d | 36c, first retains the receiver | error | error | error |
| 36e | `first = cur.inner.takeWith(do { cur = b; 0 }); a.inner.clear()` | error at the write (§15.6.7) | error at the clear | error at the clear |
| 36f | `var r = h@uniq; first = r.inner.take(); h.other = 5; return first` | valid | error at `h.other = 5` (P12) | error (P12) |
| 36g | `var it = a; first = it.take(); it = a; second = it.take()`, `Take` only | error | error at `it = a` | error at `it = a` |
| 36h | `first = cur.inner.takeWith(do { cur = b; 0 }); return first` | error at the write (§15.6.7) | **valid: UNSOUND** (outside G113) | **UNSOUND** |

**Findings.**

1. **G113 closes under either base.** 36a–36e and 36g are rejected, and 33a stays valid. 33b errs at the
   replacement, the second reservation and the second call. 33c records A3's letter before the round-7 erratum and
   is unchanged.
2. **`Φ_c` alone is unsound under the plan base too** (36g). `it = a` destroys `Φ_c`'s base `it`, so `ended` removes
   the phantom, and the second `take` meets nothing. With `desc(c)`, `first` keeps `it`'s Loan on `a.*`, and the
   reborrow `it = a` conflicts with it.
3. **New gap outside G113** (36h, found in review). §15.6.7 protects the references a reservation was reached
   through: "any write … is an error". A3's write is shallow, so it does not reach the reservation's Loan across the
   Deref (`hits`), and every mode accepts 36h. 36e shows that `desc(c)` does not depend on that rule; R2 at the call
   does.
   - `replace(cur@uniq, …)` as the argument is not such a case. It replaces no holder (§15.6), so `σ(cur)` keeps a's
     Loan, and `PhantomParentsAtCall` also rejects it (review case R1, run in a scratch copy).
4. **Cost: P12** (36f). SPEC accepts 36f: `first` keeps what a read through the receiver reaches, `σ(h)`, but not
   `r`'s Loan on `h`, so writing `h.other` meets nothing. rustc 1.97.1 accepts it too (`RustComparison.rs`): `S::Item`
   does not borrow `r`. `desc(c)` keeps `r`'s Loan on all of `h`, as every child of a Reborrow of `r` would.
   - P12 needs two things: a receiver reached through a local reference, and an access that conflicts with that
     reference's Loan while the result is live and the holder is dead.
   - The access may also go through another name to the receiver itself: `h.inner = src2` (review R6). SPEC and
     rustc accept that too (`RustComparison.rs`). While the holder is live, SPEC rejects such an access as well.
     So a disturbance by another name never depends on `^dist` (round 7, finding 4).
   - `second = h.inner.take()` after `first = r.inner.take()` (review R5, `StableTake`) is valid by the current
     SPEC's value identity, and rustc accepts it. Under S2, which A3 assumes, it is a call on another receiver Place,
     so it is not excused and is an error with or without P12. `Φ_c` alone accepts it.
   - Census (grep proxy): lib 0, ms 0, tests 0. The library's abstract descending calls
     (`Iteration.kimi:32, 59`) reach their receiver through the parameter `self`, whose only Loan is its entry Loan.
     The milestones' `cursor.next()` calls are concrete. All abstract receivers in xUnit literals are parameters.
5. **S1′ with the same rule** rejects 36c, 36e and 36g, and rejects 36f (P12). Without materialization
   (`NoMaterialize`), it accepts 36c, 36e and 36g again, so the rule depends on R2 at the receiver Ref.
6. **Open (H), not prototyped: stores.** A3 issues `Φ_c` only into the result, never into storage that
   `retains(c, i, κ, store j)` writes. A stored value that retains the receiver's content then carries no `Φ_c`, yet
   §8.11.4 rule 1 lets a later abstract call on the receiver reach its Loans. A fix stores `desc(c)`, not `Φ_c`
   alone, or G113 returns for stores.
7. **Not encoded.** `\ {R_c}` removes R_c by id. An earlier incarnation of R_c in its own parents would need a value
   that left the call carrying R_c. A descending result does not keep R_c, so only a Value-retaining result or store
   could. That case was argued, not run.

**Rerun: 113 cases.**

- **A:** 78 same, 21 with an earlier or moved error, 12 listed losses (36f added); the unlisted 33c; 36h unsound
  (outside G113).
- **A with `FirstDerefBase`:** 84 same, 20 with an earlier or moved error, 7 listed losses; 33c; 36h.
- `KillAcrossDeref` and `P3RootMost` no longer accept 36a; their 34a and 34b remain unsound.
- **S1′:** fails 29a and 30c (unsound, round 6), 33c, 34c, 36f and 36h. The exit code is 6.

## Round 10 (A5 step 1's 1b design diff; unit 0b closed)

**Inputs.** The planning session's choice `../A5 Step 1/choice-1b.md`, built from two independent rule designs, the case
23a proposal's independent review (cases 50–94) and the choice's design review (cases 100–112b). Round 10's file is
built from those copies; earlier cases keep their expectations.

**Rules added in design A mode** (S1′, PROV and S1 unchanged):

- **G119:** while a Loan sits in its reservation holder, a write or destruction of a prefix of its Place, across borrow
  Derefs, reaches it, a destroying activation (`replace(cur@uniq, …)`) included; checked on `σ_in`. No chain fact: under
  G92b's fold, A's 38d (`h@follow@uniq.c.bump(do { h = y@uniq; 1 })`) is one reservation on `(*h).c`. The prototype's
  window is positional; A3 states it by the reservation holder.
- **33c:** the excuse at c2's reservations is derived from `reserved` (round 7's erratum). Encoding fix: a Call uses its
  reservation holders, as A3's `use` says.
- **G110:** a Ref past a shared Deref forms no Loan and carries the layers from its last shared Deref on; no `obs` cut.
- **Own-value seed:** the closure of `CS(ℓ)` is seeded with `σ_in(T)` for each T named by a direct Loan (E for `Φ_E`) of
  the value ℓ stores into a holder live as `transit` reads it (the Ref's own Loan and its R2 parents), and that of
  `CS_call(ℓ, t)` with those of `σ_in(t)`. It replaces the narrower 39o row of the rule design.
- **A destroying call** (A3 E11): its observing part is checked at the call on `CS_call(ℓ, t_K)`, excluding the Loans
  the activation's own kind hits (write for `replace`, uborrow for `clear`).
- **Slice index:** a Slice handle's Index is a shared borrow Deref. UniqSlice has no built-in Index (42c is encoded as
  `indexUniq`/`index` calls).
- **Guard-local marker `γ`:** a guard candidate's values carry `γ_H` (SPEC 14.8.3's `g`); `err(ended)` fires when a
  value other than H carries `γ_H` at `EndBorrow H`. The Loan-based `err(ended)` row is deleted.
- **`PrefixKill`** (the case 23a review's form of the first-dereference base): destroying in place a Move Path ends every
  Loan whose access path it exactly prefixes. The owner adopted it on 2026-10-17 (DECISIONS R4-M52).

**Cases: 246.** Round 9's 113; the rule designs' 33d, 33e, 38a–38n, 39a–39n, 40c–40h, 41c–41q (B's duplicates of A's
cases skipped); 42a–42d (Slice); 43a (SPEC 14.8.3's example); 44a–44c (`EndBorrow` on a transfer edge); the 23a review's
50–94 with 92g and 93g; the design review's 100–112b; its round 2's 120–127b (with 123b, 123c).

| Mode (246 cases) | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |
| --- | ---: | ---: | ---: | ---: | ---: |
| Design A (last base) | 199 | 24 | 23 | 0 | 0 |
| Design A with `FirstDerefBase` = with `PrefixKill` | 218 | 21 | 7 | 0 | 0 |

- Against round 9, design A changes only 33c (valid), 36e (adds the write) and 36h (error at the write).
- `PrefixKill`'s seven listed losses are P1 (5b, 7, 18a), S1′'s base operations (9k, 9l, 14) and P12 (36f).
- Unit 0b's condition (A3 §10: no unsound acceptance, no unlisted rejection) holds under both bases. **0b is closed.**

**Ablations** (design A; cases each one changes):

| Variant | Effect |
| --- | --- |
| `NoRouteReach` (G119 off) | UNSOUND 36h, 38a–38d, 38g, 38i, 40g, 85, 107 |
| `RouteWriteOnly` | UNSOUND 38g |
| `RouteAllParents` | over-strict 38e, 40e |
| `ExcuseAtCallOnly` | over-strict 33c |
| `LoanPastShared` (A3's letter) | over-strict 39a, 39c, 39i, 39k, 39m, 41c, 41f, 41l |
| `ObsSharedCut` | none: the cut is vacuous |
| `FreshOnly` / `FreshParents` / `NoFreshClosure` | UNSOUND 100, 100d, 101, 109 / 100d, 101, 109 / those and 41o |
| `RefParentsInTransit` | over-strict 30b |
| `SliceIndexInline` | over-strict 42b, 42d |
| `EndedByLoan` (no `γ`) | UNSOUND 43a, 92g, 102, 108 (and 93, 93g with the first-dereference kill) |
| `KillAcrossDeref` | UNSOUND 34a, 34b, 72 |
| `DestroyAsDrop` (a destroying call's destruction as a Drop on `CS`, rounds 6–10's first encoding) | UNSOUND 123 |
| `SeedLiveOutOnly` (E5's "live" as `live_out` only) | UNSOUND 120b |

**Findings recorded elsewhere:** 109 (`s.r = s.n@ref; s.n += 1` with s dead after) is an error by SPEC 15.6's sentence, as
in rustc (E0506); the builder never forms a guard candidate through a user holder (93; `MirBuilder.Match`).
The prototype models no Types, views or statics; writeback and effect paths are argued, not run.

## Round 11 (A3 E13 and E14; the core lane's review B1, B2, B4)

**Inputs.** The core lane's group review of units 1b-1 to 1b-4
(`artifacts/handoff/r4-a5-step1/reviews/core-1b1-1b4/review.md`, B1, B2, B4) and the planning session's rules E13 and E14.
The review found these outside round 10's model: it had no Value callee operand, no operand access at a Call, no
PairAcquire and no intrinsic store. Round 10's 246 cases are unchanged.

**Rules added in design A mode:**

- **E13 (1), inputs at the Call (B2).** A Call's inputs are its argument operands and a Value callee's operand. The
  accesses they make at the Call ℓ are checked on `CS_call(ℓ, −)` = closure(⋃{σ_in(H) | H ∈ live_in(ℓ)} ∪ σ_in(E)), not
  on `CS(ℓ)`. Two kinds of access count:
  - `Node.Input`: the read or Move of a direct Place operand that the MIR leaves at the Call;
  - `Node.AcquireAt`: a PairAcquire's exclusive Reborrow left at the Call.

  No holder is excluded and no seed is added. A reservation in its own holder stays `ref`. The Call's destination write
  and its stores keep `CS(ℓ)`.
- **E13 (2), the Value callee (B1).** The callee's reservation activates at the Call, as an argument's does
  (`Node.Callee`).
- **E13 (3), PairAcquire (B2).** A Reborrowing PairAcquire that the MIR secures into a temp is a TwoPhaseUniq reservation
  in that temp (`Node.Acquire`) and is activated at the Call. One left at the Call is (1)'s exclusive access.
- **E14, raw stores (B4).** A call that writes an input into storage reached through a raw pointer stores
  `ret(i, Value)` into E (`Node.RawStore`), as `Raw.initialize(storage, value)` does.

**Variants.** Each one restores one behavior of the reviewed branch (`claude/a5-1b-core` at `8961f577`), or applies this
round's fix:

- `CalleeNotActivated` (B1);
- `InputsOnCS` (B2, the implementation's `CS(ℓ)`);
- `PairAcquireLive` (a PairAcquire as a live Uniq Loan wherever it is formed);
- `NoRawStore` (B4, A3 §4's intrinsic row);
- `Implemented1b` (all four);
- `SecureAcquire` (the finding below; the case carries its secured encoding as `Case.Secured`).

**Cases: 278.** Round 10's 246 plus 32 new ones:

- 130a–130g: B1. `bump = func [r] (k: i32) => r@follow += k` with `r = value@uniq` makes an Exclusive call; `peek` makes a
  Shared one.
- 131a–131g: B2, non-generic. `give(a: uniq/i32, b: i32)`, `giveR(a: ref/i32, b: i32)` and
  `giveS(a: uniq/i32, b: ref/i32)`.
- 132a–132n: B2 with pair layers. Each case is one Semantics view, in a generic caller `outer<s/T>(p: s/T)` where
  `s is ref or uniq`.
- 133a–133d: B4.

Where an operand sits (at the Call or secured) follows G123 as the MIR lane builds it (R4-M55; `MirBuilder.Builds` and
`Call` on `claude/a5-1b-mir`). An operand is secured into a temp when a later operand of the operation builds code, and
every operand builds code except a constant and a Place read as it is (Copied or Moved). So a PairAcquire
(`HirAcquire.ByCase`) builds code and secures every operand before it, while a Copy or Move of a Place builds none.

| Case | Program | SPEC | Design A | Reviewed branch (`Implemented1b`) |
| --- | --- | --- | --- | --- |
| 130a | `let v = bump@ref; bump(); inspect(v)` (review B1) | error at the call | error at the call | **valid: UNSOUND** |
| 130b | `let u = bump@uniq; let v = u@ref; u(); inspect(v)` | error at the call | error at the call | **UNSOUND** |
| 130c | Shared call: `let v = peek@ref; peek(); inspect(v)` | valid | valid | valid |
| 130d | `bump(do { let v = bump@ref; inspect(v); 1 })` | valid | valid | valid |
| 130e | `let v = bump@ref; bump(n); inspect(v)` | error at the call | error at the call | **UNSOUND** |
| 130f | `let u = bump@uniq; u(do { keep = u@ref; 1 }); inspect(keep)` | error at the call | error at the call | **UNSOUND** |
| 130g | `bump(value)`: `value` read at the Call while the callee's temp carries r's Loan as a parent | error at the call | error at the call | **UNSOUND** (`InputsOnCS`) |
| 131a | `let r = x@uniq; give(r, x)` (`$4 = &uniq two-phase (*r)`; `give(move $4, copy x)`) | error at the call | error at the call | **UNSOUND** |
| 131b | `let r = x@uniq; giveR(r, x)` (`$4 = &(*r)`; `giveR(move $4, copy x)`) | error at the call | error at the call | **UNSOUND** |
| 131c | `let r = x@uniq; giveS(r, x)` (`$5 = &x` before the Call) | error at `$5 = &x` | same | same |
| 131d | `give(x@uniq, x)` | valid | valid | valid |
| 131e | `let r = x@uniq; give(r, r@follow)` | valid | valid | valid |
| 131f | `s.set(s.n)` | valid | valid | valid |
| 131g | `replace(x@uniq, with: x)`, the read left at the Call | valid | valid | valid |
| 132a | uniq view: `var v = p; inner2(v@ref, v)` (`inner2(move $4, acquire v)`) | error at the call | error at the call | **UNSOUND** |
| 132b | 132a, ref view | valid | valid | valid |
| 132c | uniq view: `inner3(v, v@ref)` (`$3 = acquire v` secured) | error at the call | error at the call | error at `$4 = v@ref` |
| 132d | 132c, ref view | valid | valid | valid |
| 132e | uniq view: `inner7(p, p.n + 1)` (`$3 = acquire p` secured) | valid | valid | **error at `$4 = p.n + 1`** (`PairAcquireLive`) |
| 132f | uniq view: `inner7(p, bumpS(p))` | error at `bumpS` | same | same |
| 132g | uniq view: `inner8(p.n + 1, p)` (the PairAcquire left at the Call, last) | valid | valid | valid |
| 132h | uniq view: `var v = p; inner9(v, v)` (`$3 = acquire v` secured; `inner9(move $3, acquire v)`) | error at the call | error at the call | **UNSOUND** |
| 132i | 132h, ref view | valid | valid | valid |
| 132j | uniq view: `inner11(p, p.n)` (`inner11(acquire p, copy (*p).n)`) | valid | valid | valid |
| 132k | uniq view: `var w = W(a: p); innerW(w.a, w)` (`innerW(acquire w.a, move w)`) | error at the call | **valid: UNSOUND** | **UNSOUND** |
| 132l | 132k, ref view | valid | valid | valid |
| 132m | uniq view: `var v = p; let u = v@follow@ref; innerR(v, u)` (`innerR(acquire v, copy u)`) | error at the call | error at the call | **UNSOUND** |
| 132n | 132m, ref view | valid | valid | valid |
| 133a | `Raw.initialize(p, x@ref); x = 2; let q = p[0]; inspect(q)` (review B4) | error at `x = 2` | same | **UNSOUND** |
| 133b | 133a with `x = 2` before the initialize | valid | valid | valid |
| 133c | 133a without the write; `inspect(x)` at the end | valid | valid | valid |
| 133d | `Raw.initialize(p, x@uniq); inspect(x)` | error at the read | same | **UNSOUND** |

`PrefixKill` gives the same verdict and error location as design A on every new case.

| Mode (278 cases) | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |
| --- | ---: | ---: | ---: | ---: | ---: |
| Design A (last base) | 230 | 24 | 23 | 0 | 1 (132k) |
| Design A with `PrefixKill` | 249 | 21 | 7 | 0 | 1 (132k) |
| Design A with `SecureAcquire` | 231 | 24 | 23 | 0 | 0 |
| Design A with `PrefixKill` and `SecureAcquire` | 250 | 21 | 7 | 0 | 0 |
| The reviewed branch (`Implemented1b`) | 216 | 25 | 23 | 1 (132e) | 13 |

Round 10's 246 cases keep their classification, case by case.

| Variant | Effect |
| --- | --- |
| `CalleeNotActivated` (B1) | UNSOUND 130a, 130b, 130e, 130f |
| `InputsOnCS` (B2) | UNSOUND 130g, 131a, 131b, 132a, 132h, 132m |
| `PairAcquireLive` | 132c's error moves to `$4 = v@ref`; 132e over-strict at `$4 = p.n + 1` |
| `NoRawStore` (B4) | UNSOUND 133a, 133d |
| `SecureAcquire` | 132k errs at the call; no other case changes |

**Findings.**

1. **E13 misses a Move left at the Call after a Reborrowing PairAcquire left there (132k).**
   - **Facts (`-v`, `-dump`).** At `Call innerW(w.a, w)`, live_in = {w} and σ_in(w) = {Lw on `p.*`, Lp}, where Lp is the
     parameter's entry Loan and never conflicts. The at-Call Reborrow of `w.a.*` forms a Loan that no holder carries, so
     the Move of w, checked on `CS_call(ℓ, −)`, meets no Loan. The Reborrow itself meets only Lw, whose root is p.
   - **SPEC 15.6.7 rejects it.** The first input is a reservation of `*(w.a)`, and the Move of w moves the route's owner
     during it. The ref view (132l) is valid, so the generic body is rejected.
   - **Scope.**
     - G123 leaves both operands at the Call, because a Move of a Place read as it is builds no code. A PairAcquire
       does build code, so in `inner9(v, v)` the first PairAcquire is secured and 132h errs as SPEC requires.
     - Only a PairAcquire forms a Loan at ℓ. A Copy or Move operand's Loans are its source holder's, and that holder is
       live into ℓ, so a Copy after an at-Call PairAcquire is sound: 132m errs through `Lu` in u, and 132j is valid as
       SPEC requires.
     - The gap is a Move operand after an at-Call PairAcquire (a receiver or a Value callee too) that moves a prefix of
       the Reborrow's Place.
   - **Fix (M), MIR (amends R4-M55).** A PairAcquire operand is also secured where it is evaluated when any later
     operand of the operation is not a constant. A PairAcquire then stays at its Call only as the last operand read there,
     and E13 is unchanged. `SecureAcquire` runs this on 132j, 132k and 132m. 132k errs with
     `L3 live at Call innerW(w.a, w) vs Move w`, 132j stays valid, 132m keeps its error, and no other case changes.
   - **Fix (A), A5.** A later operand's access at the Call is also checked against an earlier at-Call PairAcquire's
     Loan, which counts as a reservation (`ref`). This needs operand order in A5's Call facts.
   - The planning session decides between (M) and (A).
2. **E13 (1) needs every input's holder, the callee's included.** 130g errs only through the parent Loan that the
   callee's temp carries. `CalleeNotActivated` alone keeps 130g's error, and `InputsOnCS` loses it.
3. **E13 (3) is what keeps 132e valid.** A live Uniq Loan rejects the shared read `p.n` that SPEC 15.6.7 allows during
   the reservation. A write-class acquisition during it (132f) is still rejected.
4. **E14 changes no round-10 case.** E is always checked, so after a raw store of a Loan every later conflicting access
   errs (SPEC 15.6: "a value stored there may be used after every point").
5. **Open for A6.** A3 §5's code table has no row for a failure of an access an input makes at the Call (131a, 132a):
   should it be `CallActivationConflict_Kd` or `ComparisonLoanConflict_Kd` ("Otherwise")?

**Not modeled.**

- Where the MIR leaves an operand is hand-encoded from `MirBuilder.Builds` and `Call` on `claude/a5-1b-mir`. The
  prototype does not run them.
- Each pair case is hand-encoded for one view. The view's choice of Copy or Reborrow (`MirCases.Reborrows`) is not
  computed.
- An at-Call PairAcquire issues no Loan, so a callee that retains it (`id<s/T>(a: s/T) -> s/T`) is not modeled.
- A Value callee that is itself a PairAcquire is not modeled.
- Closures are modeled only as a Move of the capture into the closure holder and a hand-chosen call kind. `TyCtxt.Closure`
  receiver modes and retention rows are not modeled.
- E14 covers only `Raw.initialize`'s store row. Other raw-writing catalog rows are not enumerated, and the raw referent
  `p.*` gets no access, because its fresh anchor compares with nothing here.
- Diagnostic codes, A4 effects, statics, the inputs' Drops and callee cleanup are not modeled.

## Round 12 (A3 E13: operand evaluation order, the owner's option R1)

**Inputs.**

- The planning session's E13, after the owner chose on 2026-10-18 that A5 handles operand evaluation order (option R1).
- The E13 design review. Its extension (variant `AlwaysSecure` = M′, cases 134a–134g and 135a–135c, a `-cases` dump) is
  merged first, exactly its additions to round 11.
- The planning session's decisions on this round's first run: findings 1 and 2 below became design A's rule.
- Old-path verdicts come from `Kimi.dll check` (legacy pipeline) of a `claude/a5-1b-core` build (`f6b365bb`) on probe
  programs: the review's o1–o4 and its rprobe set, plus this round's.

**Rules added in design A mode:**

- **E13 (1), evaluation order (SPEC 12.2).** An operation's operands are evaluated in order before it; a Call's receiver
  or Value callee operand comes first. Operand i's access at ℓ is checked on
  `CS_i(ℓ) = closure(⋃{σ_in(H) | H live after ℓ, ℓ does not overwrite H} ∪ ⋃_{j≤i} val_in(o_j) ∪ ⋃_{j>i} σ_in(root(o_j)) ∪ σ_in(E))`.
  - An operand's value, its own included, is used by its operation after the access (finding 1; SPEC 15.6; rustc E0505).
  - The set is scanned by holder: an operand's value is attributed to its root's holder, so a reservation in its own
    holder stays `ref`.
  - It holds at every operation with operands: Call, Aggregate, Closure, BinaryOp.
  - The destination write and the stores keep `CS(ℓ)`. `CS_i` has no transit and no seed.
  - In the cases, `Node.Input` now marks every operand left at ℓ, in evaluation order, including a Move of a secured temp
    (round 11's cases list their temps so). `Node.Operation` marks an operation other than a Call.
- **Single-operand rvalues (finding 2).** A Use of a Copy or Move, a Cast or a UnaryOp uses its operand's value at ℓ, so
  that operand's access is also checked against `val_in(o)` on `CS(ℓ)`. A Ref is not included: its Place access is a
  Borrow, checked as before.
  - Rounds 1–11 mark no operands. The prototype therefore applies the term to a node's Read or Move whose Place is the
    source of one of the node's flows, that is, the value ℓ stores.
  - `OwnValueEverywhere` applies it to every node Read or Move instead. Because the term only adds Loans, it bounds every
    finer reading from above.
- **E13 (2)** is unchanged from round 11.
- **E13 (3), M′ and the marker.** Design A runs each case's M′ form: `Case.Always`, else `Case.Secured`, else the case
  itself.
  - `Node.Acquire` is a marked PairAcquire or explicit case adaptation: a reservation in its temp, activated at its Call.
  - A Uniq `Borrow` is an unmarked one: a `let`, a control-flow result, an aggregate operand or a capture.
  - A pair layer and a borrow Deref are both `*`, so N3 holds by construction.
- **Classification.** A case 130+ analysed in another MIR form must err at that form's node for the same operation.
  This affects 132a, 132f and 132h, whose M′ nodes have their own labels.

**Variants.** Every variant restores an earlier rule or MIR form, or changes one clause:

- `NoOwnValue`: E13 as first stated. `CS_i` takes `val_in(o_j)` for `j < i` only, and single-operand rvalues get no own
  value.
- `OwnValueEverywhere`: the own-value term on every node Read or Move, the upper bound described above.
- `Round11`: round 11's E13 on round 11's MIR (G123). A Call's inputs are checked on `CS_call(ℓ, −)` and another
  operation's operands on `CS(ℓ)`.
- `AcquireAtOperation`: `CS_i` on G123's MIR, where a PairAcquire stays at its operation as an operand; there is no M′.
- `ShapeReservation`: A5 derives the reservation from the MIR shape (134d's `Case.Shape`).
- `IntrinsicAllOperands`: A3 §4's intrinsic row, which E14 replaced (135b's `Case.IntrinsicRow`).
- `NoEarlierValues` and `NoLaterRoots`: `CS_i` without its earlier-value or later-root term.
- `EarlierRoots`: earlier operands by `σ_in(root)` instead of their values.

Round 11's variants change as follows:

- The first four (`CalleeNotActivated`, `InputsOnCS`, `PairAcquireLive`, `NoRawStore`) now apply on top of round 12's
  design A.
- `Implemented1b` keeps the reviewed branch's MIR, with no own value.
- `SecureAcquire` and `AlwaysSecure` are round 11's rule on round 11's M and on M′ (the review's run), also without the
  own-value term.

**Cases: 316.**

- Round 11's 278 cases are kept. 132k and 132l are respelled `innerW(w.a, w@move)`: round 11's `innerW(w.a, w)` is a
  `TransferRequired_Kd`. The MIR is the same, and so are the SPEC verdicts (error, valid).
- The review's 10 cases are added. Where the review's encoding is a form round 12 rejects, design A runs this round's
  form and the review's form becomes a variant:

  | Case | Design A runs | Variant that runs the review's form |
  | --- | --- | --- |
  | 134a, 134e | 134b's and 134f's M′ | `AcquireAtOperation` (G123's MIR) |
  | 134d | the marker (134c) | `ShapeReservation` |
  | 135b | E14's Content row (135a) | `IntrinsicAllOperands` |

- 28 new cases. The callees are `give2(a: i32, b: i32)`, `give2T<T>(a: T, b: i32)` and `inner11<s/T>(a: s/T, b: i32)`.
  The pair cases are uniq-view bodies of `outer<s/T>(p: s/T)` unless marked ref. In 140, the setup is
  `var z = 0; var s = S3(r: z@ref, n: 1)`.

In the old-path column, **old path wrong** marks an old-path verdict that differs from SPEC: a ledger fact, not an E13
issue.

| Case | Program | SPEC | Design A | `Round11` | Old path |
| --- | --- | --- | --- | --- | --- |
| 136a | o1: `let r = x@uniq; give2(r@follow, x)` | valid | valid | **over-strict** | valid |
| 136b | o2: `give2(x, r@follow)` | error | error at the call | same | error at `x` |
| 136c | o3: `let t = (r@follow, x); inspect(t)` | valid | valid | valid | valid |
| 136d | `let t = (x, r@follow); inspect(t)` | error | error at the Aggregate | **UNSOUND** | error at `x` |
| 136e | o4: `let s = r@follow + x` | valid | valid | valid | valid |
| 136f | `let s = x + r@follow` | error | error at the Add | **UNSOUND** | error at `x` |
| 136g | `x.plus(r@follow)`, a Copy receiver (`T is Plus`) | error | error at the call | same | error at the receiver |
| 136h | `r@follow.plus(x)` | valid | valid | **over-strict** | valid |
| 137a | w1's counterpart: `_ = (x, x@uniq)` (`$1 = copy x` secured) | valid | valid | valid | valid |
| 137b | `let t = (x@uniq, x); inspect(t)` | error | error at the Aggregate | same | error at `x` |
| 137c | w3: `let r = x@uniq; _ = (r, x)` (`$1 = &uniq (*r)`) | error | error at the Aggregate | **UNSOUND** | error at `x` |
| 137d | `let r = x@ref; _ = (r, x)`, a dead aggregate that reads only | valid | valid | valid | valid |
| 137e | `let r = x@uniq; _ = (x, r)` | error | error at `$1 = copy x` | same | error at `x` |
| 138a | z3: `let u = v@follow@ref; let c = func [v, u] () => ()` | error | error at `$1 = acquire v` | same | error |
| 138b | 138a, ref view | valid | valid | valid | (one generic body) |
| 138c | `func [u, v] () => ()` | error | error at `$2 = acquire v` | same | **old path wrong: valid** |
| 138d | `run(func [v] () => (), peek(v@follow@ref))` | error | error at `$b = &(*v)` | same | error at `v@follow@ref` |
| 139a | x1: `inner11(v, peek(v@follow@ref))` | valid | valid | valid | **old path wrong: error at `v@follow@ref`** |
| 139b | 139a, ref view | valid | valid | valid | (one generic body) |
| 139c | `inner11(x@(s/T), peek(x@ref))` | valid | valid | valid | valid |
| 139d | `let a = x@(s/T); let k = peek(x@ref); inner11(a, k)` | error | error at `$b = &x` | same | **old path wrong: valid** |
| 140a | `s.r = s.n@ref; give2T(s@move, 1)` | error | error at the call | same | error at `s@move` |
| 140b | `s.r = s.n@ref; _ = (s@move, 1)` | error | error at the Aggregate | **UNSOUND** | error at `s@move` |
| 140c | `s.r = s.n@ref; let t = (s@move, 1); inspect(t)` | error | error at the Aggregate | same | error at `s@move` |
| 140d | `s.r = s.n@ref; give2(s.n, s.n)` | valid | valid | valid | valid |
| 140e | `s.r = s.n@ref; let t = s@move`, t dead | error | error at `let t = s@move` | **UNSOUND** | error at `s@move` |
| 140f | 140e with `inspect(t)` | error | error at `let t = s@move` | same | error at `s@move` |
| 140g | `let r2 = y@ref; s.r = r2; let t = s@move` | valid | valid | valid | valid |

- **The old path wrong rows.**
  - 139a (x1): the old path is over-strict.
  - 138c (`func [u, v]`): the old path accepts it, also with the closure used after (`c@ref`), although SPEC 7.7 executes
    the entries left to right and the old path itself rejects `func [v, u]` (138a).
  - 139d (a `let`-stored `x@(s/T)`): the old path accepts it, against SPEC 15.6.7, although it rejects the non-generic
    `let a = x@uniq` form.
- **rustc on 140e** (by SPEC-equivalent Rust reasoning, not run): E0505. The Move uses s, whose type holds the borrow's
  region, so the Loan on `s.n` is live at the Move.
- **The review's cases under design A.**
  - 134a and 134b (z1) err at `$1 = acquire v`. The old path reports `PlacementActivationConflict_Kd`.
  - 134c and 134d (x2) err at `$b = &(*v)`.
  - 134e, 134f (v1) and 134g (w1) err at the Aggregate.
  - 135a and 135b are valid; 135c errs at `x = 5`.
- `PrefixKill` gives design A's verdict and error location on every case 130+.

| Mode (316 cases) | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |
| --- | ---: | ---: | ---: | ---: | ---: |
| Design A (last base) | 269 | 24 | 23 | 0 | 0 |
| Design A with `PrefixKill` | 288 | 21 | 7 | 0 | 0 |
| `NoOwnValue` (E13 as first stated) | 265 | 24 | 23 | 0 | 4 (140a, 140b, 140c, 140e) |
| `PrefixKill` and `NoOwnValue` | 284 | 21 | 7 | 0 | 4 (140a, 140b, 140c, 140e) |
| `OwnValueEverywhere` | 268 | 25 (adds 127) | 23 | 0 | 0 |
| `Round11` (round 11's design A) | 258 | 24 | 23 | 2 (136a, 136h) | 9 (132k, 134a, 134e, 134g, 136d, 136f, 137c, 140b, 140e) |
| `SecureAcquire` (round 11's rule, M) | 259 | 24 | 23 | 2 (136a, 136h) | 8 (134a, 134e, 134g, 136d, 136f, 137c, 140b, 140e) |
| `AlwaysSecure` (round 11's rule, M′) | 261 | 24 | 23 | 2 (136a, 136h) | 6 (134g, 136d, 136f, 137c, 140b, 140e) |
| `CalleeNotActivated` | 265 | 24 | 23 | 0 | 4 (130a, 130b, 130e, 130f) |
| `InputsOnCS` | 257 | 24 | 23 | 0 | 12 (130g, 131a, 131b, 132k, 134g, 136b, 136d, 136f, 136g, 137c, 140a, 140b) |
| `PairAcquireLive` | 260 | 29 | 23 | 4 (132e, 132j, 139a, 139c) | 0 |
| `NoRawStore` | 267 | 24 | 23 | 0 | 2 (133a, 133d) |
| `Implemented1b` (the reviewed branch) | 241 | 25 | 23 | 3 (132e, 139a, 139c) | 24 |
| `AcquireAtOperation` | 267 | 24 | 23 | 0 | 2 (132k, 134e) |
| `ShapeReservation` | 268 | 24 | 23 | 0 | 1 (134d) |
| `IntrinsicAllOperands` | 268 | 24 | 23 | 1 (135b) | 0 |
| `NoEarlierValues` | 260 | 24 | 23 | 0 | 9 (130g, 131a, 131b, 132k, 134e, 134f, 134g, 137b, 137c) |
| `NoLaterRoots` | 265 | 24 | 23 | 0 | 4 (136b, 136d, 136f, 136g) |
| `EarlierRoots` | 265 | 24 | 23 | 4 (136a, 136c, 136e, 136h) | 0 |

**Round 11's 278 cases, row by row.**

- 275 rows are identical: verdict, error node, class and every `-v` error fact of PROV, S1, S1′, A and A with
  `FirstDerefBase`.
- 132a, 132f and 132h err at the same Call, now in M′'s nodes; 132h adds L4's fact.
- 132k now errs, as SPEC requires.
- `Round11` reproduces round 11's design A on all 278 rows.
- **The single-operand own-value term (finding 2) changes no round 1–11 row.** Design A's flow-source reading changes
  nothing.
- Under the upper bound `OwnValueEverywhere`, only 127 changes, and its verdict stays error. 127 gains
  `Lr live at inspect(s.r@follow) vs Read s.r.*`, beside its error at `s.n = 5`.
  - **Facts (`-dump 127`).** After `r = s2@uniq` ends the child Lc, s keeps its parent Lr, a uniq Loan on s, because
    s.r holds the stored `$t`. So `σ_in(s) = {Lz, Lr}`.
  - The read of `s.r.*` is `inspect`'s operand, and its value, `content(σ_in(s))`, carries Lr.
  - SPEC agrees: the read value carries Lr and is used by the Call, so the read meets a live uniq Loan on its prefix s.
    127 marks no Call operand, so design A does not reach this second report;
    under E13 (1) a marked `inspect` operand would.

**Findings.**

1. **E13 (1) as first stated missed the operand's own value (140a–140c UNSOUND).** Decided by the planning session:
   `j ≤ i` is design A's rule, and `NoOwnValue` keeps the first statement.
   - **Facts (`-dump 140a`).** At `give2T(move s, const 1)`, `σ_in(s) = {Lz, Ln}` with Ln on `s.n`, `live_out(ℓ) = ∅`,
     and there is no later operand. So without the term `CS_1 = ∅`.
   - **Rejected alternatives.**
     - Transit catches only 140c. `InputsOnCS` (operands on `CS(ℓ)`, transit included) errs on 140c alone.
     - Round 11's `CS_call(ℓ, −)` caught 140a and 140c, but it rejects o1 (136a) and 136h.
2. **Single-operand rvalues (140e).** Decided by the planning session: the same reading holds for a Use, a Cast and a
   UnaryOp.
   - Without the term, `let t = s@move` with t dead is accepted (`NoOwnValue`, `Round11`): A3's transit sees no live
     holder there.
   - 140f is rejected either way, through transit, and 140g stays valid: s's Loan is on another root.
3. **Each clause of `CS_i` is load-bearing.**
   - `NoEarlierValues` accepts nine error cases, w1 among them. `NoLaterRoots` accepts o2 and its kin.
   - `EarlierRoots` reproduces round 11's over-strict o1, and also rejects o3 and o4.
   - M′ is load-bearing at every operation. `AcquireAtOperation` accepts 132k and 134e, because a Loan formed at ℓ is in
     no entry set. 134a (z1) errs by order alone: its acquire is checked against the later operand u's holder.
   - The marker is load-bearing. `PairAcquireLive` rejects 139a (x1), 139c, 132e and 132j; `ShapeReservation` accepts
     134d (x2).
4. **Round 11's E13 on the new cases.** `Round11` is over-strict on 136a (o1) and 136h, and accepts 134a, 134e, 134g,
   136d, 136f, 137c, 140b and 140e. Evaluation order is needed at Aggregates and BinaryOps as well as Calls.

**Not modeled.**

- Operand order is hand-encoded per case.
  - The 1–127b cases encode their Calls and aggregates without operand order, so `CS_i` runs only on cases 130+.
  - For their single-operand rvalues, the flow-source reading and the `OwnValueEverywhere` bound stand in for marked
    operands.
  - Where the MIR leaves an operand (G123, M′) and which PairAcquire the builder marks are hand-encoded too. The builder
    is not run.
- These orders are not modeled:
  - defaults after explicit arguments (SPEC 7.2.3);
  - index targets before indices;
  - dictionary entries and interpolation.
- Casts and UnaryOps have no case of their own; they take the same term as a Use.
- A Value callee that is itself a PairAcquire is still not modeled.
- Closure receiver modes are not modeled, nor the views' computed choice of Copy or Reborrow.
- Diagnostic codes are not modeled. Like round 11's finding 5, A6 has no row for an operand-order conflict.

## Round 13 (A3 E13 rewritten: one rule for every access)

**Inputs.**

- A3 §12 E13 as rewritten after its second design review. On 2026-10-18 the owner chose the representation, "one rule for
  every access".
- The second design review's patched prototype, merged first with exactly its additions: the flags DOV, ROP and AV, its
  alternatives ROS and RPT, its `-r13` run (reproduced exactly), and its 13 cases. The flags are now the design's terms,
  and each one has a named variant.
- The planning session's update of E13 after this round's first run (finding 1). The borrow row keeps E5's seed and takes
  E3's bound. The Drop row states that MIR never destroys and stores at one location.
- The review's MIR dumps of today's builder (probes p1–p8) and its old-path probes. Old-path verdicts come from the review:
  its case titles, and its ledger record for d3.

**The design is design A with E8's ending (`PrefixKill`):** `Analysis(c, Mode.A, x, prefixKill: true)`. `Design A (last
base)` keeps A3 §3's base kill, as "design A" did in rounds 1–12. A `-cases` name ending in `+PK` runs on the design.

**Rules (E13 (1)).** Each access at ℓ is checked on closure(σ_in of the holders live after ℓ that ℓ does not overwrite,
σ_in(E), T). T depends on the access:

| Access | T | Variant restoring the superseded clause |
| --- | --- | --- |
| Operand i (`Node.Input`) | `⋃_{j≤i} val(o_j) ∪ ⋃_{j>i} σ_in(root(o_j))` (round 12) | `NoOwnValue`, `NoEarlierValues`, `NoLaterRoots`, `EarlierRoots` |
| Activation of a reservation in t; a destroying call's destroy | `⋃_{o_j≠t} val(o_j)`, seeded by σ_in(t); t is left out of the live holders | `ActivationLiveIn` (CS_call's live_in(ℓ)) |
| A Ref's or Reborrowing PairAcquire's borrow of X | `⋃_{m≤j<k} content^j(σ_in(root(X)))`, where m is the index of the last shared Deref (`SharedAt − 1`), else 0; whatever its destination's liveness; E5's seed; no transit | `BorrowSeedOnly` (R4-M57), `ParentsAsTransit` (the review's RPT with ROS), `BorrowWithoutSeed` (the first run), `ParentsEveryLayer` (⋃_{j<k}) |
| Observing destruction of X: a Drop, and a destroying call's destroy | val(X); a Drop takes no transit and no E5 seed | `NoDestroyValue` |
| Destination write, stores | transit and E5's seed, as before | — |

- `KeepCSTerms` is the review's additive run. The borrow and Drop rows keep round 12's CS(ℓ), transit and seed included,
  and also take their own terms.
- `CurrentBuilder` runs today's builder MIR (`Case.Built`).
- `OnePointDrop` runs rounds 5–12's one-point encodings of 24c, 29b and 91 (`Case.OnePoint`).
- In the code, `Acc.From` marks a borrow access by its Issue.
- Rounds 1–10 mark no operands. On their nodes, an activation keeps live_in(ℓ), whose closure bounds the operands' values
  from above. Their Reads and Moves keep CS(ℓ) and round 12's flow-source own value.

**MIR forms (E13 (2), (4)) and corrected encodings.** These come from the review's dumps of today's builder:

- An explicit `@move` builds code.
  - 132k is now `$4 = acquire w.a; $5 = move w; innerW(move $4, move $5)`, and 132l is its ref view. Round 11's `Secured`
    form and M′ are now this same form.
  - 140a–140c are now `$7 = move s` followed by the operation. 140c is not named by the review, but the same rule builds
    it. The review's m140a and m140b are these forms, so they are not kept twice.
- **A Drop is a terminator, and the store follows it** (`MirTerminatorKind.Drop`; the review's fD2 dump: `drop _2 -> bb3;
  _2 = move _7`). 24c, 29b and 91 are now `drop X (old value)` followed by the store. Their one-point form is
  `Case.OnePoint`.
- Today's builder builds captures without G123 or M′ (`Closure(acquire b0 v, copy u)`). Case 138 keeps E13 (2)/(4)'s
  form (`$1 = acquire v` and the securing of u). Today's shape is `Case.Built`, with the PairAcquire left at the Closure.
  In the uniq view it is a live Uniq formed there.
- Named arguments: E13 (2) secures a Place operand that is evaluated before an operand that precedes it in operand order.
  n1 is `$1 = copy (*r); give2(copy x, move $1)`, and n2 is `$1 = copy x; give2(copy (*r), move $1)`. Today's
  builder passes the arguments by slot (`Case.Built`).
- A setter's value is evaluated before its receiver (SPEC 13.7.1): `$1 = copy (*r).level; $5 = &uniq two-phase g;
  level.Set(move $5, move $1)`. Today's builder for a Place value is not probed, so s1 and s2 have no `Built` form.
- A single capture is never secured, so `func [s@move]` is `Closure(move s)` both today and under E13 (c1).

**Cases: 332.**

- Round 12's 316, with eight re-encoded: 24c, 29b, 91, 132k, 132l and 140a–140c.
- The review's 11 (its 13 without m140a and m140b).
- This round's d3, s1, s2, b1 and b2.

In the old-path column, **wrong** marks an old-path verdict that differs from SPEC, and — means not probed.

| Case | Program | SPEC | The design | Old path |
| --- | --- | --- | --- | --- |
| 24c | `s = S(r: s.r@follow@uniq)` with a writing drop (re-encoded) | error | error at `drop s (old value)` (was at the store) | — |
| 29b | `p@follow = S(r: p.r@follow@uniq)` (re-encoded) | error | error at `drop (*p) (old value)` | — |
| 91 | `t = T(r: slot2@uniq, c: t.r@follow@follow@uniq)` (re-encoded) | error | error at `drop t (old value)` | — |
| 132k | `innerW(w.a, w@move)`, uniq view (re-encoded) | error | error at `$5 = move w` (was at the Call) | — |
| 132l | 132k, ref view (re-encoded) | valid | valid | — |
| 140a | `s.r = s.n@ref; give2T(s@move, 1)` (re-encoded) | error | error at `$7 = move s` (was at the Call) | error at `s@move` |
| 140b | `_ = (s@move, 1)` (re-encoded) | error | error at `$7 = move s` (was at the Aggregate) | error at `s@move` |
| 140c | `let t = (s@move, 1); inspect(t)` (re-encoded) | error | error at `$7 = move s` (was at the Aggregate) | error at `s@move` |
| r13a1 | `let r = y@ref; give(y@uniq, r@follow)` | valid | valid | valid |
| r13a2 | r13a1 with `inspect(r)` | error | error at the call | — |
| r13b1 | `var s = S5(r: y@ref); var w = W5(p: s@uniq, q: o@uniq); w.q = w.p@follow.r@uniq; let t = w.p@follow.r@follow@ref; inspect(t)`, w dead after t | valid | valid | — |
| r13b2 | b1 with `inspect(w.q)` | error | error at `let t = …` | — |
| r13c1 | `s.r = s.n@ref; let c = func [s@move] () => ()` (j = i at a Closure) | error | error at `c = Closure(move s)` | error |
| r13d1 | `var q = Q2(r: z@uniq); q.r = q.n@uniq`, scope end | error | error at `drop q` | error |
| r13d2 | d1 with `q.r = y@uniq` | valid | valid | — |
| r13d3 | d1, then `q = Q2(r: z2@uniq)` (`drop q; q = move $7`) | error | error at `drop q (old value)` | **wrong: valid** |
| r13rf1 | `var s = S7(r: z@uniq, q: w@ref); s.q = s.r@follow@ref; let t = s.r@follow@uniq`, t dead | error | error at `let t = …` | **wrong: valid** |
| r13rf2 | rf1 with `inspect(t)` | error | error at `let t = …` | **wrong: valid** |
| r13u1 | `s.r = s.n@uniq; give2(s.n, 1)` (j = i at a Call) | error | error at the call | **wrong: valid** |
| r13u2 | `s.r = s.n@uniq; let t = s.n`, t dead | error | error at `let t = s.n` | **wrong: valid** |
| r13n1 | `let r = x@uniq; give2(b: r@follow, a: x)` | valid | valid | valid |
| r13n2 | `let r = x@uniq; give2(b: x, a: r@follow)` | error | error at `$1 = copy x` | error |
| r13s1 | `var g = G(); let r = g@uniq; g.level = r@follow.level` | valid | valid | — |
| r13s2 | s1 with `inspect(r)` | error | error at `$5 = &uniq two-phase g` and at the Set | — |

The error facts under the design (`-v`, `-dump`):

- **24c, 29b, 91:** at the drop, the stored value's holder `$v` is live after ℓ, so L2 meets both the deep destroy and
  the drop's write. These are the same two facts the one-point form had in round 12.
- **a2:** Lr, through r live after the call, meets the activation (uborrow y). a1 is valid because the activation takes
  val(r.*) = content(σ_in(r)) = σ_in(y) = ∅.
- **b2:** Lq, a uniq borrow of s.r held in w.q, is in σ_in(w), and w is live after ℓ. Lq meets the shared borrow of
  `w.p.*.r.*`. In b1, w is dead after ℓ, and E3's bound (m = 1) takes only content(σ_in(w)), which is σ(s) ∪ σ(o). So Lq
  is not in the check set.
- **d1, d3:** val(q) = {Lz, Ln}, where Ln is the Uniq Loan on q.n, meets the deep destroy of q.
- **rf1, rf2:** the parents σ_in(s) = {Lz, Lw, Lq}, where Lq is the Shared Loan on s.r.*, meet the uborrow of s.r.*.
- **u1, u2, c1:** the operand's own value {Lz, Ln} meets the read of s.n, or the Move of s.
- **n2:** at `$1 = copy x`, r is live after (it is read at the Call), and σ_in(r) = {Lr}.

**Totals (332 cases).** The design and design A (last base), then each round-12 and round-13 variant run on the design.

| Mode | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |
| --- | ---: | ---: | ---: | ---: | ---: |
| **The design** (design A with `PrefixKill`) | 304 | 21 | 7 | 0 | 0 |
| Design A (last base) | 284 | 24 | 23 | 1 (30b) | 0 |
| `BorrowWithoutSeed` (this round's first run) | 303 | 21 | 7 | 0 | 1 (41o) |
| `ParentsEveryLayer` | 303 | 21 | 7 | 1 (r13b1) | 0 |
| `BorrowSeedOnly` (R4-M57) | 302 | 21 | 7 | 0 | 2 (r13rf1, r13rf2) |
| `ParentsAsTransit` | 304 | 21 | 7 | 0 | 0 |
| `KeepCSTerms` (the review's additive run) | 304 | 21 | 7 | 0 | 0 |
| `OnePointDrop` | 304 | 21 | 7 | 0 | 0 |
| `NoDestroyValue` | 302 | 21 | 7 | 0 | 2 (r13d1, r13d3) |
| `ActivationLiveIn` | 303 | 21 | 7 | 1 (r13a1) | 0 |
| `CurrentBuilder` | 302 | 21 | 7 | 1 (r13n1) | 1 (r13n2) |
| `NoOwnValue` | 300 | 21 | 7 | 0 | 4 (140e, r13u1, r13u2, r13c1) |
| `NoEarlierValues` | 296 | 21 | 7 | 0 | 8 (130g, 131a, 131b, 134e, 134f, 134g, 137b, 137c) |
| `NoLaterRoots` | 300 | 21 | 7 | 0 | 4 (136b, 136d, 136f, 136g) |
| `EarlierRoots` | 300 | 21 | 7 | 4 (136a, 136c, 136e, 136h) | 0 |
| `AcquireAtOperation` | 303 | 21 | 7 | 0 | 1 (134e) |
| `ShapeReservation` | 303 | 21 | 7 | 0 | 1 (134d) |
| `IntrinsicAllOperands` | 303 | 21 | 7 | 1 (135b) | 0 |
| `OwnValueEverywhere` | 303 | 22 | 7 | 0 | 0 |
| `RefParentsInTransit` | 304 | 21 | 7 | 0 | 0 |
| `CalleeNotActivated` | 300 | 21 | 7 | 0 | 4 (130a, 130b, 130e, 130f) |
| `InputsOnCS` | 293 | 21 | 7 | 0 | 11 (130g, 131a, 131b, 134g, 136b, 136d, 136f, 136g, 137c, r13u1, r13c1) |
| `PairAcquireLive` | 295 | 26 | 7 | 4 (132e, 132j, 139a, 139c) | 0 |
| `NoRawStore` | 302 | 21 | 7 | 0 | 2 (133a, 133d) |
| `Round11` (round 11's rules) | 288 | 21 | 7 | 3 (136a, 136h, r13a1) | 13 (134a, 134e, 134g, 136d, 136f, 137c, 140e, r13d1, r13d3, r13rf1, r13rf2, r13u2, r13c1) |
| `Implemented1b` (the reviewed branch) | 271 | 22 | 7 | 4 (132e, 139a, 139c, r13a1) | 28 |

- Under `CurrentBuilder`, captures keep their verdicts. 138a and 138c err at the Closure itself, by operand order alone,
  as 134a does.
- Under `OnePointDrop`, 24c, 29b and 91 keep their verdicts but lose their `Destroy` fact.

**Round 12's 316 cases, row by row.** Compared are `-cases None PrefixKill FirstDerefBase`: verdict, error nodes, class
and every error fact, against round 12's file.

- 307 cases are identical in all three modes.
- The corrected encodings change 132k and 140a–140c: the same verdict, with the error at the new Move node.
- 24c, 29b and 91 keep their verdict and both error facts, now at the drop node.
- **30b and 30d, last base only:** the borrow row rejects 30b (an unlisted loss) and adds an error node to 30d's listed
  loss. This is R4-M57's reason, which E13 says E8's ending removes. The design keeps both as in round 12.
- `KeepCSTerms` changes the same rows as the design and nothing else.
- In the full output, `NoTransit`'s witnesses are now 25a, 25c and 29c: destination writes and stores. They were 24c,
  29b and 91 in their one-point form. Round 4's `AText` row for 24c is gone too. That form keeps both histories, as
  `Case.OnePoint`.

**Findings.**

1. **The first run's borrow row had no E5 seed, and it accepted 41o (UNSOUND).** That row is `BorrowWithoutSeed` now. The
   program is `s.r = s.n@ref; let p = s@uniq; p.n = 5; inspect(p.r@follow)`.
   - At `let p = s@uniq`, σ_in(s) = {Lz, Ln}, with Ln on s.n. live_out(ℓ) = {p}, which ℓ overwrites.
   - The borrow has k = 0, so its parents are ∅. The write of p hits no Loan on p.
   - The planning session gave the row E5's seed back, and the design now errs at `let p = s@uniq`.
2. **E3's bound separates from ⋃_{j<k} only on b1, which this round adds.** No earlier case does.
   - b1 follows SPEC 15.6.3: "A Borrow past a shared dereference … derives only from the reference of its last shared
     dereference and the references it dereferences after that one". It is checked against the values used after ℓ
     (15.6), and w is not one of them.
   - The every-layer reading also takes w's Lq, a uniq Loan on s.r that w.q holds, and rejects b1. b2, where w is used
     later, errs under both readings.
   - b1 is sound: w.q's Loan is dead together with w, and t reaches y only through s.r's value.
   - The Rust analogue would be rejected (E0502, by reasoning, not run), because NLL counts the read of `w.p` at the
     borrow as a use of w. This is a SPEC fact, not an E13 problem.
3. **The one-point encoding was the only reason 24c, 29b and 91 lost a fact.** As MIR builds them, the stored value's holder
   is live after the drop, so both facts return. `OnePointDrop` shows the loss again.
4. **Each term is load-bearing.**
   - val(X): `NoDestroyValue` accepts d1 and d3.
   - The parents: `BorrowSeedOnly` accepts rf1 and rf2.
   - The seed: `BorrowWithoutSeed` accepts 41o.
   - E3's bound: `ParentsEveryLayer` rejects b1.
   - The activation's operand values: `ActivationLiveIn` rejects a1.
   - j = i: `NoOwnValue` accepts 140e, u1, u2 and c1.
   - E13 (2): `CurrentBuilder` rejects n1 and accepts n2.
   - Transit at the destination write and stores: `NoTransit` accepts 25a, 25c and 29c.
   - Round 12's terms and E13 (3)/(4) remain load-bearing, as the variant rows show.
5. **val(X) at a destroying call** changes no case: `NoDestroyValue` changes only Drops.

No rule problem is left: under the design, every case is "same" or "same verdict, error moves".

**Not modeled.**

- Today's builder is not run. MIR forms are hand-encoded from the review's dumps, and `Case.Built` exists only for n1,
  n2 and 138a–138d.
- Rounds 1–10 mark no operands: their activations keep live_in(ℓ), an upper bound, and their reads keep CS(ℓ).
- A call's effects (`err(effect)`: statics), E13 (5)'s cost and E13 (6)'s codes.
- Labelled payloads (E13 (2)) have no case. They take the named-argument rule at an Aggregate.
- Today's builder for a setter's Place value is not probed, and the old path is not probed for s1, s2, b1 and b2.

## Correction of 2026-10-18 (case 75, SPEC's letter)

The owner kept SPEC's letter on case 75 (an obj payload's Field replaced while a child borrowed through it lives; PLAN G147 (6)):
a Loan ends only where a Move Path is destroyed in place (SPEC 15.6.3), and a Move Path takes no own step (15.1.3, 15.6.2), so
`h@follow.r = slot2@uniq` ends nothing and `h@follow.r@follow@follow = 7` is an error. Changes:

- Case 75's SPEC verdict is that error (it was valid, with design A's error a listed 23a-family loss).
- `PrefixKill` and `FirstDerefBase` end nothing at a K through `payload`. Without it, the design accepted 75, now unsound.
- Rerun, the design: 304 same, 21 earlier or moved, 7 listed losses, 0 unlisted, 0 unsound (unchanged totals; 75 is "same" as an
  error). Design A (last base): 285 same, 22 listed losses (75 left them). Only the rejected `KillAcrossDeref` accepts 75.
- A5 agrees: `MirBorrowsTest`'s c75 expects the error at `h@follow.r@follow@follow = 7` (1b-5, `806110fc`).
