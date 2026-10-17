# Independent review: Loan Termination Across Borrow Dereferences (case 23a proposal)

Reviewed: `draft/Proposals/2026-10-17 Loan Termination Across Borrow Dereferences.md` at `origin/dev` 5c42b577,
against SPEC §13.5.5, §14.8.3, §15.1.3, §15.1.6, §15.6–§15.7, §16.3.3, A3 (§3, §5, §7, §11) and DECISIONS R4-M39.
I did not write the proposal. Nothing in the repository was edited or built. Evidence:

- `proto.cs`: round-9 prototype plus round 10: cases 50–94, variant `PrefixKill`, `EndBorrow`/err(ended), and
  mutation variants `NoP3A`, `NoParentsA`. `run.txt`: its output (`dotnet run proto.cs`, 158 cases). The 113
  round-9 rows are byte-identical to the baseline run.
- `rust/cases.py` and `rust-run.txt`: rustc 1.97.1 analogs of 29 new cases. `RustComparison.rs` reproduces as
  documented.

## 1. Verdict

**Adopt with conditions.** Under A3's exact representation, the first-dereference base with the exact Move-Path
matcher is sound on every case I could construct. Its unsound acceptances (84, 85, 93) have two causes outside the
rule: the missing reservation-route write check (PLAN G119) and a guard-candidate lowering that the MIR builder does
not produce. The rule only removes error facts. On all 158 cases, `FirstDerefBase` reports no error fact that design A
does not (`run.txt`), so no accepted program in any census class changes verdict. The conditions (§2) are ordering
and recording obligations; none of them changes the rule.

| Rule, 45 new cases | same | listed loss | error moves | UNSOUND |
| --- | --- | --- | --- | --- |
| design A (last base, plan `within`) | 31 | 11 (all 23a family) | 2 (59, 84) | 1 (85) |
| A with `FirstDerefBase` | 42 | 0 | 0 | 3 (84, 85, 93) |
| A with `PrefixKill` (N1) | 42 | 0 | 0 | 3 (84, 85, 93) |
| A with round 6's `KillAcrossDeref` | | | | 72, 84, 85, 93 |

On all 158 cases:

- A has 2 UNSOUND (36h, 85) and 23 listed losses.
- `FirstDerefBase` has 4 UNSOUND (36h, 84, 85, 93) and 7 listed losses (P1, S1′ base operations).
- 33c remains the unlisted loss under both.

## 2. Findings

### Conditions of adoption (blocking for the implementation unit, not for the rule)

**C1. G119 must land at the write before or with the unit (cases 84, 85).** Case 84 is
`res = rr@follow@follow.keepWith(do { rr = slot2@uniq; 0 })`, where the result retains the receiver, followed by
`rr@follow@follow = 7; inspect(res@follow)`.

- SPEC §15.6.7 rejects the write to `rr` in the argument, because `rr` holds a reference the reservation was reached
  through. A3 misses that write (G119).
- Design A rejects 84 only later, at `rr@follow@follow = 7`. The reservation's Loan `rr.*.*` never ends, so the stale
  Loan meets the write: the 23a false conflict happens to mask G119.
- The first-dereference base ends that Loan at the argument's write, so 84 becomes a new UNSOUND row against A.
- 85, without retention, is UNSOUND under both rules, so G119 is the cause.
- The parents (`Lo` on `slot`, `L1` on `x`) still protect the old route. The violation is of the SPEC rule, not a
  memory-safety hole.
- Add 84 and 85 to G119's regressions. 84 distinguishes a check at the write (σ_in, proposal §2) from one that relies
  on a stale Loan.

**C2. Record that EndBorrow-checked holders get no Loan with a user-holder first base (92, 93).** A3 enforces
§14.8.3's guard-local Origin end only through `err(ended)` on Loans issued into the candidate. Its Origin stratum
(§7) reads no liveness.

- 93 lowers the candidate as `Ref(rr.*.*.data)`, with first base `rr`. The guard
  `saved = d@follow@ref; rr = slot2@uniq` then ends that Loan before cleanup, and `saved` escapes the guard.
  A rejects 93; `FirstDerefBase` accepts it.
- 92 lowers the candidate through the internal Subject Place `$s` (§15.1.6), as `MirBuilder.Match.cs:27-28, 82-84`
  does. It is rejected under every rule.
- Two options: state the lowering as an A3 §5 contract, or make `err(ended)` independent of the Loan's survival.
  Keep 92 and 93 as tests.

**C3. Amend §4.** Its structure (holders `Y1…Yn`, parents) is right, but it omits A3 facts it depends on:

- **(a) "Through B" fails for `rc`/`arc` own steps between G and B.** A surviving clone keeps B's storage (78).
  Soundness instead rests on two facts:
  - past `rc`/`arc` only shared access exists (§15.6.2), so the child is `ref` and the clone cannot write;
  - a release that may be final observes the payload and is checked on σ_in before the end (§16.3.3, §15.6.6;
    case 80).
- **(b) Reservations and guards.** Reservations depend on G119 (C1); guards depend on the lowering in C2.
- **(c) Add the missing step: G's new value cannot reach L's old storage** except through a value that already
  carries L's parents (89). Every other route needs a Borrow of `slot` or `x`, or a reborrow through `rr`, and each
  conflicts with the parents (87, 94) or with L before the end (51, 53, 62–64).

**C4. The SPEC mapping (§7) misses §13.5.5's pair-layer table.** Its `uniq` cell says "`P` is the child's base". That
is false when `P` is behind a dereference: `c[i]@follow@uniq` with `c: uniq/Collection<s/T>` has base `c`. Rewrite the
cell together with §15.6.3, Appendix E and the borrow-handle sentence. With N1, it becomes "the child runs through
`P`".

### Non-blocking

**N1. A shorter equivalent: drop "base".** `PrefixKill` reads: destroying in place a Move Path K ends every Loan whose
access path has K as an exact prefix.

- It gives the same verdicts and sites as `FirstDerefBase` on all 158 cases.
- Child Loans: K has no dereference or runtime index, so "exact prefix of the path" equals "exact prefix of the Place
  before the first dereference".
- Direct Loans: ending them is unobservable. A live direct Loan on a part of K is reached by K's destruction with no
  dereference between them (§15.6.2), so it is reported on σ_in first. A Loan that is not live is not checked.
- SPEC text: "Destroying in place a Move Path whose access path is a prefix of a child Loan's access path ends the
  child, once …".
- It deletes the Base glossary row, the base pre-pass and field (A3 §3/§4), and the cell in C4.

**N2. A3 already disagrees with the integrated SPEC on 34c** (§15.6.3, "contains it with no dereference between
them"). Align it under either base, as §7 says.

**N3. Correct the rustc sentence in §4.** rustc gives the same verdict on 25 of the 29 analogs. It differs on four:

- 61: rustc rejects (NLL's location-insensitive region on the loop re-borrow); 61b, without that re-borrow,
  compiles.
- 67 and 71: rustc rejects, because MIR `Index` places get `NoOverlap`. Kimigayo's constant index is a Move Path
  (§15.1.3), so soundness rests on Move Path identity, not rustc.
- 66: rustc accepts the write through a reference; the rule rejects it on purpose.

**N4. Mutation evidence (§6).** One safeguard removed from `FirstDerefBase` each time; cases newly UNSOUND:

| Mutation | Newly UNSOUND cases |
| --- | --- |
| No P3 | 51, 53, 62–64 (plus 9g–9i, 12a/b, 22a, 29a) |
| No R2 parents | 55, 59, 77, 79, 81, 83, 87, 88, 90, 92, 94 |
| No transit | 91 (plus 24c, 25a, 25c, 29b, 29c) |
| Exclusion by Loan id | 20b, 20f |

50, 52 and 56 rest on the prefix rule (§15.6.2), not on P3.

**N5. Precision on 59.** A also errs at `let c = …`, where a stale incarnation still names `rr.*.*`. `FirstDerefBase`
reports only `rr = slot2@uniq`, the real double-`uniq` alias (rustc E0499).

## 3. New cases: SPEC verdict and basis

"P" marks the 23a family: valid under the proposal, a listed loss under the current §15.6.3, rejected by A and
accepted by `FirstDerefBase`. Every other row has the same verdict under A and `FirstDerefBase`, except 84 and 93.

| Cases | Form | SPEC verdict (basis) | rustc |
| --- | --- | --- | --- |
| 50, 52 | 29a reversed; receiver `view(q)` retained, then `c` | error at `c` (§15.6.2: a Loan prefix of the op's path is reached) | E0502 (50) |
| 51, 53 | `c`, then a shared receiver or a uniq argument retaining `q.*` | error at the formation or activation (§15.6.3 ban, §15.6.7) | E0502, E0499 |
| 54 P | two siblings `q.a`, `q.b`; `q = t`; writes through `q` | valid (§3.1; siblings end together) | valid |
| 55, 56 | write through the old `s.a`; two `uniq` children of `q.a` | error (parents; §15.6.3 region splitting) | E0506, E0499 |
| 57, 60 | replacement on one branch, or conditionally in a loop | error at the later write (§15.6 may-liveness) | E0506, E0499 |
| 58 P, 61 P | both branches; replacement before and inside a loop | valid | valid; 61: E0499 (NLL) |
| 59 | back edge: `keep = c; rr = slot2@uniq` | error at `rr = slot2@uniq` (parent on `slot2`, earlier incarnation) | E0499 |
| 62–64 | Move into a call, `exchange`, `swap` of `rr` | error (§15.6.3: base op while the child is live) | E0499 (63) |
| 65 P | `replace(rr@uniq, …)` | valid (§15.7: destroyed in place) | valid |
| 66 | `replace(rr@follow@uniq, …)` | error under both texts (a write through a reference ends nothing) | valid |
| 67 P, 71 P, 73 P | `arr[0] = …`; `m[0] = …` under `m[0][i]`; `p.a = …` | valid (exact Move Path prefix, §15.1.3) | E0506 for 67, 71; 73 valid |
| 68–70, 72, 74 | other constant, runtime write, `m[i][0]`, sibling Field | error at the write through the old route | E0506 |
| 75 P, 76 P, 77 | `obj` payload Field or whole handle; old `slot` | valid, valid, error (own step is storage; parents) | valid, valid, E0506 |
| 78, 79, 80 | `rc` clone kept; old `slot`; observing payload drop | valid; error; error at `h = …` (§16.3.3 may-final release) | — |
| 81, 83, 88 | child stored into `E`; direct step into its own base; temp base | error (parents kept; §15.6 storage not owned) | — |
| 82 P, 89 P | stride-2 walk; child stored into its own outer holder | valid | valid |
| 86, 87, 90, 94 | child of `c`; old `slot` write; same-point old route; `slot` reborrow | error (§15.6.3 parents) | E0506 |
| 84, 85 | reservation route written in an argument (with/without retention) | error at the write (§15.6.7; G119) | — |
| 91 | 24c through two Derefs | error at the store (§15.6.6: checked before the end) | — |
| 92, 93 | guard stores a candidate-derived ref, then replaces `rr` | error at cleanup (§14.8.3) | — |

## 4. §4 step by step against A3

| Element | Holds? | Basis |
| --- | --- | --- |
| Every carrier of L carries its parents | Yes | R2 at issue; whole-root copies (P1); `step` removes only L; EndBorrow, StorageDead and `prune` empty whole holders; `desc(c)`. F6 stores (G113) carry no `Φ_c` under either base. |
| Transit; check before `step` | Yes | 91; same-point flows keep the parents (89, 90). |
| Global `ended` by id; repeated sites | Yes | Moving, exchanging or borrowing G conflicts while an incarnation lives (51, 53, 62–64), so every live incarnation uses G's current value (59–61, 20a–20f). |
| P3 deep Move/Borrow; `obs` cut | Yes; unchanged | 51, 53, 62–64; 27, 80. |
| Joins and loops | Yes | 57, 60 keep the old route; 58, 61 recover. |
| `E`, obj, Index steps after the base | Yes | 81, 23b; 75–77; 34a–34c, 67–72 (`KillAcrossDeref` UNSOUND on 72). |
| rc/arc own steps | Yes, by C3a | 78–80. |
| Reservations | Needs G119 | 84, 85. |
| Guard-local end (EndBorrow) | Needs C2 | 92, 93. |

**§2 and §3.3 against the run: confirmed.**

- Round 8/9 rows:
  - `KillAcrossDeref` is UNSOUND on 34a and 34b;
  - `FirstDerefBase` changes exactly 23a, 30c (error moves), 30d, 34c, 35a, 35b, and 36a/36b (only at `cur = a`);
  - counts: 78/21/12 against 84/20/7, with 33c and 36h in both.
- 35a and 35b err in their next step under A.
- 37 is valid under A. This is the strongest consistency argument: today's verdict depends on whether the
  intermediate holder is named.
- §3.3's errors hold: 29a, 50–53, `x` (9j), the old `slot` (87, 94), and 30c, 34a, 34b, 36a, 36c.

## 5. Alternatives and the shared-layer rule

- **The root-most kill with the exact matcher is this proposal.** B is identical. Round 6's root-most variant
  differed only in its may-overlap matcher (34a, 34b and 72 UNSOUND).
- **Shorter: N1.** Same verdicts, one concept less.
- **More general: Rust's kill on any definite write, including through references (66).** It looks sound in A3:
  while `rr`'s uniq Loan on `slot` is live, every live Loan through `slot`'s referent runs through `rr`. But it needs a
  definite-update rule for non-path indexes and for `replace` targets behind a Deref. Deferring it is right.
- **"First base only for exclusive layers": reject.** Under §15.6.3 no Loan forms past a shared Deref, so every Loan
  path is all-exclusive and this coincides with the proposal. In the prototype's G110 superset it would only keep
  stale shared children; 13a–13e, 27 and 78 are unchanged under FDB.
- **G110: no interaction with the base.** A Borrow past a shared Deref carries its last shared reference's Loans and
  those after it, and these end only under their own bases. Prototype evidence stays valid for exclusive paths only,
  as §2 says.
- **I agree with the two rejected options**, a separate outer-ending relation and an id or 23a exemption.

## 6. Cost and census

**A3 deletions.**

- §3: "no borrow Deref step between", "Index only for identical constants" and "a B containing a borrow Deref is
  never ended".
- The three "No kill across a Deref" bullets.
- The S1′ `within` loss row (23a, 30d); 35a, 35b and 34c are never added.
- §5's `within(B, K) // never across a Deref` becomes an exact prefix.
- With N1, also `base(L, B)` and the base field.

**A3 additions.** None: no state, mode, table or fixed-point dimension. Ended sets stay static per location; they
grow, and the carried sets shrink. Net size: down by a few lines (H). Performance: not measured; expected neutral.

**Census** (grep proxies, H; the rule only removes errors, so only rejected forms can change):

| Class | Two-dereference Borrow sites | Then a destruction of the outer holder | Verdict changes |
| --- | --- | --- | --- |
| `src/Kimi/Library` | 0 | 0 | 0 |
| `tests/milestones` | 0 (2 compound writes, `Milestone33.kimi:73-74`) | 0 | 0 |
| xUnit literals | 8 (`DictionaryStoredReferenceTest.cs:95-96`, `OwnedStoredReferenceTest.cs:200-217`, `StoredReferenceResultBoundTest.cs:20`) | 0 | 0 |
| SPEC examples | 0 | 0 | 0 |

Implicit multi-Deref paths (`cur.inner.take()`) are not grepped; A3's census counts 0 / 0 / 0 traversal lines.

## 7. Recommendation for the owner

**Options.**

| Option | Effect |
| --- | --- |
| (a) Keep the last base | 23a, 30d, 35a, 35b and the 11 new P rows stay losses; 35a and 35b join A3's loss rows; 34c is aligned. |
| (b) Adopt the first-dereference base as proposed | Recovers them all. |
| (c) Adopt (b) worded without "base" (N1) | Same verdicts; shorter SPEC and A3. |

**Recommendation: (c), or (b) if the owner prefers to keep the term**, with these conditions:

- C1: G119's write check lands first or together, with 84 and 85 as regressions;
- C2: the guard-candidate lowering is recorded, with 92 and 93 as tests;
- C3: §4 is amended;
- C4: §7 includes §13.5.5's pair-layer cell;
- N2: 34c is aligned.

**Census.** Library 0, milestones 0, xUnit 0 verdict changes (H).
