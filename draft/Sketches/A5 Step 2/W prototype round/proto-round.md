# 2a-2 prototype round for rule W (DECISIONS R4-M104; proposal §6 item 1)

Status: **done** (2026-10-24). Investigation only: no repository commit or push. The run's base is `origin/claude/a5-2a-1b`
`65be867e9` (2a-1a's MirOrigins and 2a-1b's Unknown on dev `9f9e552c0`). The worktree `bin\worktrees\2a-2-proto` held a throwaway
WIP commit `1d2aeb73c` plus later edits. The worktree was removed at the end with its junction unlinked first.
`proto\w-bit.patch` holds the whole prototype diff, and `proto\ScratchWProbeTest.cs.txt` the probe test.
"(H)" marks a reading no run checked.

## 1. Result in brief

- **Programs.** 113 programs were run in `proto\cases\`: 64 case forms and 49 repair variants.
  - Every row of §3 and every item of 2a-2's list has a program.
  - Where the literal form's failing node is a Binding twin, the table also lists a `var` form. A twin is an Opaque on the MIR
    (2a0-D §5.1), so its Borrow or its escaping value is never built. The `var` form has the same Loan flow, and a relation that is a
    row A5 judges.
- **Readings.** Every Loan conflict that exists on today's MIR got the reading §3 gives it.
  - 25 of §3's 27 rows reach a reading, all matching. For the twin forms, the reading comes from the `var` form.
  - Of the other two: PreparedInput is not a Loan conflict, so W does nothing, as §3 says; m-36f has no conflict on this base
    (§4 item 2).
  - All 2a-2 list items match.
- **Verdicts.** No verdict or acceptance changed. The prototype only labels records. All 113 programs have byte-identical MIR
  problem lists with the bit off and on. The default pipeline is untouched. The MIR classes (MirOriginsTest, MirBorrowsTest,
  MirAnalysisTest, MirBuildTest, MirDifferenceTest: 369 tests) give the same results with the bit off and on. Their 14 failures are
  all warm-allocation tests, caused by the throwaway channel's dictionaries, and they occur with the bit off too.
- **Repair check (§6).**
  - All 25 repair variants of independent conflicts kept the conflict.
  - All 14 variants of the 12 derived conflicts with a variant lost the conflict or changed its Loan flow.
  - No exception was found.
- **Differences from §3** (detail in §4 below):
  - (a) The literal twin forms have **no conflict at all** on today's MIR. W can derive nothing there before H11, and three
    independent conflicts §3 requires are lost today (a pre-existing limit).
  - (b) m-36f's conflict no longer exists on this base.
  - (c) Two conservative independent readings, both SPEC-legal: m-pair's returned Borrow, which §3 allows, and B1 (a)'s storage
    end, which §3 does not list.
  - (d) m-write3's (H) storage-end conflict does not exist.
  - (e) One extra derived record that §3 does not list (keepA's storage end).
- **Facts the real MIR lacks** (§5 below): the link from the returned value's transfer to the result, and the twins' Loan flow.
  The readings also needed one rule the proposal's §4 does not state: a read through a reference to a whole root is a Copy of
  it. Per-part Loan facts are absent, which is conservative.

## 2. Model and what ran where

**Real pipeline (every row below).** MirOrigins judges the Refuted chains (2a-1a/2a-1b) and MirBorrows finds the conflicts, unchanged.
On top of them sits a throwaway direct bit per (Loan or guard marker, holder).

- **Representation.** The bit is held as a shadow "indirect" element per Loan or marker inside MirBorrows' interned sets. The
  node-union join therefore ANDs the bit at joins.
- **Set where the Borrow forms the Loan in its destination.** A Ref's own Loan arrives direct; a guard candidate's γ_H too.
- **Kept by:**
  - a whole-root Copy or Move;
  - a Reborrow of a root's whole value;
  - a read through references to whole roots. This rule is new: the guard name `v@ref` lowers to `&*(*&v)`;
  - the call clause: a whole argument a retention row of kind Value retains, into the result, and into E where the written
    argument's referent Origin is fixed. A local the call writes takes the Loan indirectly, which is conservative.
- **Cleared by:**
  - a part read, a read through a part-Loan or through E;
  - an aggregate;
  - a Content retention;
  - a Value callee's environment;
  - a store into a local that is not a whole replacement.
- **The store check for `_0` and E.** At each store into `_0` or E, the bit is kept only where the destination Place's case Type
  has an outer Origin that `TyCtxt.OriginAtom` classes Fixed at the statement.
- **Derived test, per conflict.** Each conflicting Loan of the access's root, or the candidate's marker for err(ended), is read
  with:
  - its Borrow's atom (MirOrigins' Ref table; for a marker, the Ref assigned into the candidate);
  - the Refuted problems from that atom in the view;
  - its carriers in the check set's passes.

  A Loan or marker is derived when its atom is Finite, it is refuted, and every carrier is `_0`, E or a result temp (§5 item 1) with
  the bit set. A problem is derived when all its Loans are (R4-M96). Its causes are those Refuted problems. Nothing is dropped.

**2x-P4 model (`A5OriginsPrototype.cs`): not used.** The brief's fallback is for cases where a fact the bit needs is missing.
- The one missing link (§5 item 1) was emulated on the real MIR, so every conflict that exists today ran on the real pipeline.
- The literal twin forms have no Loan flow on any pipeline before H11, so no model of today's MIR gives them a conflict. Their
  post-H11 Loan flow is exactly their `var` form's, which ran on the real pipeline.
- Encoding them by hand in 2x-P4 would re-state §3, not test it. Their post-H11 readings stay (H).

## 3. Table

Codes:
- S = Unsatisfied (Refuted), P = Unproven, C = Loan conflict (ComparisonLoanConflict_Kd).
- "C ← S" = C derived from S; "C ind" = independent.
- Carriers: `_0'` = the result temp (§5 item 1); `~` = the bit cleared.
- "Default" is the default pipeline's records. The old analysis does not run after a Binding record, so it shows only that record.
- "MIR today" is identical with the bit off and on.
- All programs and outputs are in `proto\cases\<id>.kimi`, `.off.out`, `.on.out`; `proto\results.txt` gathers them.

| Case | Sources | Default | MIR today | Prototype (W) | §3 / 2a-2 expected | Verdict change | Repair check |
| --- | --- | --- | --- | --- | --- | --- | --- |
| j-bad | §3 r1, 2a-2; `j-bad` | S | S only (twin) | — (no C) | S; C ← S | none | — |
| j-bad (var) | `j-bad-v` | S | S `r`; C end | C ← S (`_0'`) | C ← S | none | `.r1` (return x): C gone ✓ |
| j-region | 2a-2; `j-region` (LocalRegionInferenceTest) | S | S `view`; C end | C ← S (`_0'`) | C ← S | none | `.r1`: C gone ✓ |
| row 2 (`var r: … during a`; `r = x@ref`) | §3 r2; `row2` | S | S only (twin) | — | C ← S | none | — |
| row 2 (var) | `row2-v` (`r = u`), `row2.r2` (r unannotated) | S | S; C end | C ← S (`_0'`) both | C ← S | none | `row2-v.r1`: C gone ✓ |
| m-write | §3 r3, 2a-2; `m-write` | S | S `return r`; C write | C ind (r) | both ind | none | `.r1` (inspect r; return p): C stays ✓ |
| m-write, `var r` form | 2a-2; `m-write-var` | C, S | S; C write | C ind | both ind | none | `.r1`: C stays ✓ |
| m-write, annotated `var r` | `m-write-var2` (twin), `m-write-var2-v` | S | twin: S only; var: S, C write | var: C ind (r) | ind (r a local) | none | `m-write-var2.r1`: C stays ✓ |
| m-write3 (annotated `let` initializer) | 2a-2; `cf/p/m-write3`; `m-write3` | S | S (row); C write | C ind | write ind; "storage end derived" (H) | none | `.r1`: C stays ✓ |
| m-write4 | §3 r4, 2a-2; `m-write4`, `m-write4b` | S | S only (twin) | — | C ← S | none | — |
| m-write4 (var) | `m-write4-v` | S | S; C write | C ← S (E) | C ← S | none | `.r1` (no store): C gone ✓ |
| m-store | §3 r5, 2a-2; `m-store`, `m-store-b` | S | S only (twin) | — | C ← S | none | — |
| m-store (var) | `m-store-v` | S | S; C end | C ← S (E) | C ← S | none | `.r1`: C gone ✓ |
| Two flows | §3 r6; `two-flows` | S, S | S, S (twins) | — | 2 S ind; C ← both | none | — |
| Two flows (var), `cf/p/jregion2` | `two-flows-v`, `jregion2` | S, S | S, S; C end (2 locations, merged) | C ← both S (`_0'`+E; E) | C ← both | none | `.r1` (no store): C ← return's S; `.r2` (no return): C ← store's S; `.r3`: gone ✓ |
| Store in a loop | §3 r7; `loop-store` | S | S only (twin) | — | C ← S | none | — |
| Store in a loop (var) | `loop-store-v` | S | S; C at next `x@uniq`; C end | both C ← S (E) | C ← S | none | `.r1`: both gone ✓ |
| m-twice / same-root | §3 r8, 2a-2; `m-twice`, `same-root` | S | S only (twin): **b's C lost** | — | other C ind; returned may be reported | none | `.r1` (`(p, b)`): C ind present ✓ |
| m-twice / same-root (var) | `m-twice-v`, `same-root-v` | S | S; one C at x's end (both Loans) | C ind (both Loans `~`) | other ind; returned derived or reported | none | `-v.r1`: C stays ✓ |
| m-pair | §3 r9, 2a-2; `m-pair` | S | S only (twin): **y's C lost** | — | C(y) ind; C(x) derived or reported | none | `.r1`: C(y) ✓ |
| m-pair (var) | `m-pair-v` | S | S; C(x), C(y) | both ind (`~`) | C(y) ind ✓; C(x) "may also report" ✓ | none | `-v.r1`: C(y) stays ✓ |
| `return id(local@ref)` | §3 r10; `id-return` | S | S only (twin) | — | C ← S (call clause) | none | — |
| id (var) | `id-return-v` | S | S `r`; C end | C ← S (`_0'`, call clause) | C ← S | none | `.r1`: C gone ✓ |
| fK | §3 r11, round 3 C1; `fK` | S | S; C x's end | C ind (`_0'~`: Tuple argument) | C ind | none | `.r1` (store deleted): C stays ✓ |
| rv-g-param | §3 r12, 2a-2; `rv-g-param` | S | S `d`, P `d`, C (err(ended)) | C ← S (E, call clause); S, P ind | same | none | `.r1` (store another value): C gone ✓ |
| rv-g-param, `v@ref` form | `rv-g-param-i` | S | S, S, C | C ← S (E) | C ← S | none | — |
| rv-g-annot-esc | §3 r13, 2a-2 | S | S, S, C | C ind (t) | both ind | none | `.r1` (t unannotated): S gone, C stays ✓ |
| g-two | §3 r14, 2a-2 | C, P, S | C, S, P | C ind | all ind | none | `.r1` (return fallback): C stays ✓ |
| g-return | §3 r15, 2a-2 | C, P, S | C, S, P | C ind (saved) | all ind | none | `.r1`: C stays ✓ |
| cf-g-reassign | §3 r16, 2a-2 | C, 2P, S | C, S, 2P | C ind (other) | all ind | none | `.r1`: C stays ✓ |
| m-scope | §3, 2a-2 | C, S | S, C | C ind (r) | both ind | none | `.r1`: C stays ✓ |
| m-scope-use | §3, 2a-2 | C, S | S, C | C ind | both ind | none | `.r1`: C stays ✓ |
| m-36f | §3, 2a-2; `m-36f` | — | — (valid) | — | C ind | none | — |
| PreparedInput | §3 r19 (PD11); `prepared`, `prepared2` | S; DefaultArgumentBorrow | S (twin); DefaultArgumentBorrow | not a Loan conflict (n/a) | grouping, 2g-5 | none | — |
| Review-2 B1 (a) | §3 r20; `r2-b1a` | S | S only (twin) | — | C write ind | none | — |
| Review-2 B1 (a) (var) | `r2-b1a-v` | S | S; C write; C local's end | write ind (Reborrow atom Fixed); end ind (`~`, aggregate) | write ind | none | `r2-b1a.r1`: write C stays ✓ |
| Review-2 B1 (b) | §3 r21; `r2-b1b` | P (Binding fixed–fixed) | **nothing** (Opaque): **write C lost** | — | P; C ind | none | — |
| Review-2 B1 (b) (var) | `r2-b1b-v` | P | P `u`; C write | C ind (atom Fixed) | C ind | none | `.r1` (`during a`): P gone, C stays ✓ |
| Round 3 B1 (b) keepA | §3 r22; `r3-b1b` | S | S `v`; C write; C local's end | write ind; end C ← S (E) | write ind | none | `.r1` (no `if`): write C stays ✓ |
| Round 3 B1 (c) fC | §3 r23; `r3-b1c` | S | S (twin store); C x's end | C ind (E `~`: `pair.1`) | C ind | none | `.r1`: C stays ✓ |
| Round 3 B1 (d) fB | §3 r24; `r3-b1d`, `r3-b1d-v`, `r3-b1d2` | S | twin: S only; var, fB2: S, C | C ind (E `~`: part `q` not fixed) | C ind | none | `r3-b1d.r1` (r unannotated): C stays ✓ |
| Re-check R1 fT | §3 r25; `rc-fT`, `rc-fT-v` | S | twin: S only; var: S, C | C ind (`t.1` part read) | C ind | none | `.r1` (t unannotated): C stays ✓ |
| Re-check R1, flow-insensitive | §3 r26; `rc-flow` | S | S `pair.0`; C | C ind | C ind | none | `.r1` (`(p0, keep)`): C stays ✓ |
| Conflict without a Refuted twin | §3 r27, 2a-2; `no-twin` | C | C | C ind (refuted 0) | ind | none | — |
| `rv/guard-annot` | 2a-2; MirOriginsTest's `Guard` | S | S, S (candidate and Subject borrow); no C | — | S kept | none | — |
| block / `if` / `match` result | 2a-2; `rf-block`, `rf-if`, `rf-match` (`-v`) | S | S only (Binding judges the result fit: twin) | — | (H) | none | — |
| block / `if` / `match` result (var) | `rf-block-v2`, `rf-if-v2`, `rf-match-v2` | S | S `r`; C end | C ← S (`_0'`, joined from one branch: AND) | C ← S | none | `rf-if-v2.r1`: C gone ✓ |

## 4. Cases that differ from §3, for the owner

1. **The literal twin forms have no conflict today.**
   - *Cases:* j-bad, row 2, m-write4, m-store, Two flows, the loop store, `id`, m-twice/same-root, m-pair, B1 (a), B1 (d), fT,
     block/if/match. B1 (b) belongs here too: it is Binding's fixed–fixed record.
   - *Cause:* the failing node is `HirOp.Error`, an Opaque whose value is {Φ_E} (2a0-D §5.1, R4-M70). So the escaping Borrow is
     never built (`return local@ref`), or the escaping value never reaches `_0` or E (`p@follow = r`, `return pair.0`).
   - *Effect under W:* W has nothing to derive in these forms before H11. §3's "Records" column assumes the post-H11 MIR.
   - *Independent conflicts lost today:* in m-twice/same-root (b's Loan), m-pair (y's) and B1 (b) (the write), a conflict §3 calls
     independent is not reported at all. The programs stay rejected by their S or P. This is the twin's recorded Opaque limit, not
     W's. Its reach is wider than 2a0-D §9's STATUS sentence states: that sentence names only "a Borrow formed inside the failed
     node". Here a value the failed node reads is also lost after the node, because the node's result carries {Φ_E}.
   - *The `var` forms:* each `var` form has the same Loan flow and gets exactly §3's reading. After H11, the literal forms should
     read like them (H).
   - *Decision asked:* none for W. A STATUS/H11 note covering the lost independent conflicts is the planning session's call.
2. **m-36f has no conflict on this base.** P12 was deleted by "Abstract Calls Reach by Paths" (2026-10-22), so `first` no longer
   keeps h's Loan, and both pipelines accept the program. §3's row is vacuous here. Its claim, that a conflict whose Borrow has no
   Refuted chain is independent, holds on `no-twin` and on every conflicting Loan with no Refuted chain from its Borrow: all 31 such per-Loan readings are
   independent.
3. **Conservative readings, both SPEC-legal.**
   - m-pair's returned Borrow (x) reads independent. The pair is an aggregate and `pair.0` a part read. §3 allows this ("an
     implementation without per-part facts may also report it").
   - B1 (a)'s `var` form also has a conflict at `local`'s storage end that §3 does not list. E holds local's Loan in a Tuple stored
     into `p.*`. Per part, the sentence derives it (`p.*.0`'s Origin `a` is fixed); without per-part facts the prototype reports it
     independent.
4. **m-write3.** §3's (H) note expects a derived storage-end conflict. None exists: the write `x = 2` ends x's Loans (E8) before
   the storage end. Only the independent write conflict remains.
5. **keepA (round 3 B1 (b)) has one more record than §3 lists:** a conflict at `local`'s storage end, derived from the S at `v`.
   E receives v's value, a Copy of `local@ref`, into `p.*` (Origin `a`). Removing the `if` removes it. This is consistent with the
   sentence.
6. **One added rule.** `rv-g-param-i` (the `v@ref` guard form) is derived only because a read through a reference to a whole root
   keeps the bit (a Copy of that root's value; the MIR lowers `v@ref` to `&*(*&v)`). §4 of the proposal does not state this rule.
   Without it the conflict reads independent, which is SPEC-legal but contradicts §3's `rv-g-param` reading. 2a-2's design should
   state the rule.

## 5. Facts the real MIR lacks

1. **The result in transit (the brief's "link from a transfer to its relation row").**
   - The builder evaluates `return e` into builder temps and moves them into `_0` only after the body's storage ends. For example:
     `_6 = copy view; _7 = move _6; StorageDead(local); …; _0 = move _7`.
   - So at every storage end, the result is a temp, not `_0`.
   - Without a fact linking the return's transfer to the result, j-region, `jregion2`'s normal path and every `_0` case read
     independent.
   - Emulated by a structural rule: a `Temp` local whose every read is a whole Copy or Move into `_0` or into such a temp, where
     `_0`'s Origin is fixed.
   - User locals must stay out. A first version that admitted any local made m-write, m-scope and m-write3 derive wrongly.
   - The real design should publish the fact: the ResultFit transfer's row (`Relation`), or a role on the return temp.
2. **The twins' Loan flow** (§4 item 1): absent until H11.
3. **A store's receiving part class.** It is available through `TyCtxt.OriginAtom(body, origin, site)` on the destination Place's
   case Type. But the call appends atoms to the body's table (a side effect inside MirBorrows) and resolves per store. 2a-2 should
   have the class published, for example by MirOrigins per store or per Place Type.
4. **A Loan's Borrow atom.** Available for Ref Loans (MirOrigins' Ref table; Loans are per Ref site, R4-M97 D14), but not
   published.
   - A PairAcquire Reborrow has no Ref statement, so it derives nothing (conservative).
   - A guard marker's Borrow was found by scanning for the Ref assigned into the candidate. A published link would be the one form.
5. **Per-part Loan facts:** absent. Aggregates and part reads clear the bit, which is conservative (§4 item 3).

## 6. Cost (indication only)

Measured with `ScratchWProbeTest.Cost`: MirBorrows' Work and Loan-set Bytes on the `shape` body, bit off then on, N = 32 / 64 / 128.

| Shape | Work off → on | Bytes off → on |
| --- | --- | --- |
| N Loans, each through 8 whole copies | 71,639 / 255,815 / 961,511 → +0.7 / +0.4 / +0.2 % | 132 / 260 / 516 → 516 / 1,028 / 2,052 (×4) |
| N holders joined from two Loans in a branch (V16-like) | 35,672 / 118,473 / 425,195 → +1.3 / +0.8 / +0.5 % | 388 / 772 / 1,540 → ×2.0 |
| N Loans into nested Tuples, read by parts | 29,078 / 103,512 / 388,315 → +47 / +50 / +52 % | 2,376 / 8,840 / 34,056 → ×4.9 / ×4.9 / ×5.0 |
| N Loans through a retaining call and a copy | 61,319 / 223,719 / 851,879 → +3.2 / +2.7 / +2.4 % | 516 / 1,028 / 2,052 → ×2.5 |
| N borrow/write pairs of one root (`TheEndedSetsOfAMovePathAreMadeOnce`) | 9,135 / 22,354 / 61,205 → +5.4 / +4.4 / +3.2 % | 268 / 524 / 1,036 → ×2.5 |

- **Scaling.** The bit keeps every shape's growth: Bytes stays linear where it was, and Work's ratio is flat or falling in N.
- **Where Bytes grows.** The shadow elements live in the interned sets: the ended sets carry their shadows, and every indirect Loan
  doubles its element. A first version that added one shadow per `Add` was quadratic in Bytes (`TheEndedSets…` failed). Building
  shadows by runs (`Range` + `Union`) fixed that.
- **What Work does not count.** Work counts the set operations only. The result-temp pre-pass (O(statements)) and the
  whole-content scans are not counted.
- **Alternative representation.** A dedicated per-holder bitset (A3's dense term, holders × ⌈Loans/64⌉ words, under `MirDataflow`'s
  limit, R4-M15a) would avoid the ×2–5 Bytes. 2a-2's design should choose between the two.
- **Size.** The throwaway diff is +486/−16 in `src`, but most of it is the channel and the trace. The bit's transfer rules are
  about 60 lines, the store check about 30, the result temp about 90 and the derived test about 60 (H for the real design).

## 7. Unverified

- The literal twin forms' post-H11 readings (H: as their `var` forms).
- Pair-layer bodies with several views. Every case here has one view, so per-view masks and causes are untested.
- Inline default copies (the `Evaluated` subject) and held views (R4-M73).
- Publication (H3, R5's): DIAGNOSTICS §4.3's suppression and the invariant change are not prototyped. Whether an Origin-stratum
  cause can be held is not checked.
- The carriers include every pass of the check set, the access's taken values included. This is conservative; the exact set a
  check takes is not modelled.
- No heavy run (full Session, snapshots or ledger). The prototype changes no record on 113 programs and 369 MIR tests, and by
  construction labels only.

**Outside W, for the planning session.** The default pipeline accepts these repair variants, while the MIR rejects them with a
storage-end or write conflict that SPEC §15.6's holder rule requires:
- `fK.r1`, `r3-b1c.r1`, `r3-b1d.r1`, `rc-fT.r1`, `rc-fT-v.r1`, `rc-flow.r1`;
- `m-twice.r1`, `m-twice-v.r1`, `same-root.r1`, `same-root-v.r1`, `m-pair.r1`, `m-pair-v.r1`;
- `r2-b1a.r1`, `r2-b1b-v.r1`.

These are review-p2r3's N12 and C7 and the fT note, now confirmed on this base. They are pre-existing ledger differences.

## 8. Files

- `proto\cases\*.kimi` — the 113 programs.
- `*.off.out` and `*.on.out` — the bit-off and bit-on records.
- `proto\results.txt` — per case: source, default records, Binding records, MIR problems, W readings.
- `proto\compact.txt` — one line per case.
- `proto\cases.py` — the program generator.
- `proto\w-bit.patch` — the prototype diff against `65be867e9`.
- `proto\ScratchWProbeTest.cs.txt` — the probe and cost test.
- `proto\cost.log` — the cost run.
- `proto\classes-on2.xml` and `proto\classes-off2.xml` — the MIR classes with the bit on and off.
