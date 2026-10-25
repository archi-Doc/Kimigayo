# 2x-P7: A5-F7's prototype round with R4-M99's derivation column

Saved record (2026-10-25, unit spec-closure, DECISIONS R4-M123, which records the owner's answers to D1, D2 and F3): `p7\` keeps the
generators (`make_cases.py`, `make_analogs.py`, `merge_runs.py`), the probe test (`ScratchProbeP7Test.cs`) and the hand-written programs
of `cases-x\`, `param-slot\` and `failed-closure\`. The run outputs named below (`results.txt`, `runs.tsv`, `*.mir`) and the generated
folders (`cases\`, `cases-a5\`, `analogs\`) are not kept; §1's commands regenerate them. R4-M123 resolves §6 F3 (G12) for t4a and t4e, whose
bare entry names an enclosing local; t4g stays SPEC-invalid by §15.6.3's containment in the Shared receiver and belongs to G6 alone.

Unit 2x-P7 (A5 step 2 plan §4.1, PD3, PD13, OQ8; DECISIONS R4-M99). Designer slot, 2026-10-25. Investigation and draft only: no
compiler source, draft or record changed; nothing committed or pushed (the probe test below was copied into the worktrees for the runs and removed after).

- Base: dev `ded883970` (worktree `bin/worktrees/2x-p7`, local branch `claude/r4-2x-p7` at dev, no commit, kept clean with its Release
  build). MIR Origin relations also on the held `claude/a5-2a-1b` `65be867e9` (dev `9f9e552c0` + MirOrigins, R4-M113/M114) in a
  throwaway worktree `bin/worktrees/2x-p7-a5`, removed at the end (junction unlinked first).
- Programs and runs: `p7\` next to this file (`runs.tsv` merges every run; per folder `results.txt`; MIR dumps `*.mir` in `p7\cases\`).

## 0. Summary

- **127 programs**: 79 primary cases (the plan's f7a–f7n with twins, G147 (3)'s reproducer and the `let`-binding form, 26, §15.8.2's
  example among them; SPEC §7.7's examples, each commented error line uncommented, 10; §15.8.2's table rows, 21; review-1's Consuming
  `var` forms, 5; the `T is Owned` forms, 5; G229's class S probes and controls, 9; G216's headers, 2; G65's form, 1), 19 supplementary
  closure probes and 29 method analogs.
- **Derivation vs SPEC (R4-M99 says none may differ):** no verdict differs under the reading R4-M99's own text supports, but two
  points of the integrated wording decide verdicts and go to the owner (§4): **D1** retention into a `let` environment binding
  (`f7o`), **D2** §15.8.2's catch-all "any other source is an error" against a static source (`t8a`). Two more constraints on the
  wording, already in R4-M99, are now shown by runs: permissions stay §7.7.2's (`rc1`, `rc2`) and result elision stays §15.8.2's
  third paragraph (`t6b`).
- **SPEC vs today:** 13 gap classes (§5), all 2c-D's or already owned, plus three findings outside F7 (§6): a concrete Closure's fixed
  signature Origins are missing from its Owned proof (`ow1`, accepted on both pipelines), a failed indented closure gets a `Never`
  result whose MIR body has validator findings (`InternalInvariant_Kd`), and the bare-entry Reborrow capture's Origin (BU-28 vs SPEC
  3.5.1, `t4e`).
- The executable derivation: 29 method analogs (the Closure case rewritten as R4-M99 reads it: a struct whose Fields are the
  environment bindings, an instance method whose receiver is the call receiver) run on dev's real pipelines, where the receiver rules
  already exist (Binding's Origin judge, `TyCtxt.Retains`, `MirBorrows`' W). Wherever no closure-specific rule applies, the analog's
  MIR verdict equals SPEC's closure verdict (the generic-pair analog of A3-closure is inconclusive, held by P13); where §7.7.2's
  permissions or `let` bindings, or §15.8.2's elision, apply it differs, as R4-M99 predicts (m-rc1, m-rc2, m-f7o2, m-t6b).

## 1. How the cases ran

| What | Where | How |
| --- | --- | --- |
| Today's default and MIR verdicts | dev `ded883970`, all 98 closure programs | `ScratchProbeP7Test` (in `p7\`; copied into `tests/xUnitTest/Tests/` for the runs, then removed): binds each program as an Application under the mir pipeline, `Analyze`, then `MirDifferenceLedger.Sides`: default = Binding's records + the old analyses'; MIR = Binding's records + the MIR's construction records, validator findings and problems (A1–A5 shadow) |
| MIR Origin relations (A5-F4 held) | `claude/a5-2a-1b` `65be867e9`, 89 programs | same probe; differences from dev are only in Origin rows (§3) |
| Derivation (receiver rules) | by reading, chains in §2; executable where a method analog exists (29 programs, dev) | the analog's verdicts on both pipelines |
| `A5RulesPrototype.cs` | not used | every case runs on the real pipeline (the brief's preference), and the Loan-side derivation runs as method analogs on dev's MIR, which already implements the store rows 2c-3 will give closures. PD3's parity at X-5 (all rounds on one model) is not served by this round; a round 21 encoding C2's rows as `Store` rows (f7a–f7d, f7o/f7o4, t5b) is possible if the planning session wants it |

Commands (from the worktree; `P` = this folder's `p7`):
`python -I P/make_cases.py P/cases <BU g227 probes dir>`; `python -I P/make_analogs.py P/analogs`;
`KIMI_P7_DIR=P/<folder> [KIMI_P7_DUMP=1] dotnet tests/xUnitTest/bin/Release/net10.0/xUnitTest.dll -method XunitTest.ScratchProbeP7Test.Run`;
`python -I P/merge_runs.py P`. G229 sources are BU's probes `C:\Users\bwff1\AppData\Local\Temp\claude\bu\g227\dB\new\` copied unchanged.

## 2. Notation and the derivation's chains

Verdicts: **A** accepted; **E**(code@line) an Error; **U** the located `Unsupported_Kd`; Loan = `ComparisonLoanConflict_Kd`, Unsat =
`UnsatisfiedOriginRelation_Kd` (Refuted), Unpr = `UnprovenOriginRelation_Kd` (Unknown). Receivers S/E/C = Shared (`ref/Self`), Exclusive
(`uniq/Self`), Consuming (`owner/Self`), per admitted Semantics case where they differ; "today" is `BoundClosure.Receiver` (one, maximal).
Marks: **D** derivation ≠ SPEC; **Gd** / **Gm** / **Gm+** SPEC ≠ default / dev's MIR / 2a-1b's MIR (an implementation gap); **c** same
verdict, other code or location.

The derivation reads each case as R4-M99's sentence does: environment bindings are Fields of the receiver's referent; result Origins,
containment and retention follow the receiver rules; §7.7.2's permissions, §7.7.3's per-case requirement, the Closure Type (§3.2.1)
and §15.8.2's result elision stay. Chains used:

| Chain | Rule |
| --- | --- |
| C-env | A Borrow of an environment binding through `ref/Self` or `uniq/Self` is reached through the receiver borrow: an inferred region contained in the receiver Origin (§15.6.3, §15.6.5); a result over it depends on the receiver and, by §15.6.4 step 4, on OwnedOrigins of the Closure Type |
| C-ref | Reading a binding of Type `ref/T during o`, or a shared Borrow through it, meets that shared layer first: contained in `o` only |
| C-uniq | A Reborrow (or shared Borrow) through a captured `uniq/T during o`: contained in `o` and in each borrow it is reached through up to and including the first shared layer: under `uniq/Self` in `o` and the receiver; under `ref/Self` in `o` and the receiver (the receiver is the first shared layer) |
| C-own | Under `owner/Self` the bindings are parts of a by-value parameter root: a Borrow of one is body-local (Refuted against a fixed Origin); a Reborrow through a captured reference is reached through that borrow (contained in `o`), and the end of the receiver's storage ends the child while its parent Loans stay (§15.6.3) |
| C-move / C-arg / C-local | A binding moved out transfers its Type; a parameter's contract; a Borrow anchored in a local or temporary of the body is body-local, Refuted at the returned value |
| F-cap | Origins the bindings' Types name are Origins of the receiver's referent Type: fixed in the body (BU-28's row 6); a relation between two fixed Origins needs a premise visible in the body, else Unknown |
| R-E | `uniq/Self`: the writable storage the contract lets the call write may retain an argument whose value it can hold (§15.6.4, per argument): `var` bindings (layer 1, the closure holder) and captured exclusive referents (deeper layers); a referent outside the body's own storage is storage the body does not own (§15.6) |
| R-C | `owner/Self`: the environment dies with the call; captured exclusive referents may retain arguments; a result naming an Origin of the owned receiver argument carries all of that argument's Loans (§15.6.4, "any other Origin of an argument") |
| R-S | `ref/Self`: no writable environment storage; a result depending on the receiver carries every Loan a read through the receiver carries (all captured Loans) |
| O-own | OwnedOrigins of a concrete Closure Type: its captures and its fixed signature Origins (§15.2.3; unchanged by R4-M99) |
| P-772 | Per-call permissions (`let`/`var`, Consuming moves, nested `[x@uniq]` judged by the outer call's permission) are §7.7.2's |

## 3. The table

### 3.1 The plan's cases f7a–f7n (A5 step 2 plan row 2x-P7), with twins

Sources: `622`, `1379`, `575`, `576` are ledger rows `13972.*` of 1b-5's ledger (`raw-13972.tsv`, sources `4037bfa952e2`,
`40f3cbe34df1`, `a46e125d3817`, `358748d9dfcc`); row 18 and `[t@ref]` are audit-calls rows 17–18; A3-closure and A9-keep-ok are
`pair-cases.json`; `f7k3` is 2c-1's reproducer `13972.1438` (`24028725b625`, G147 (3)).

| Case | Form | Recv | Default | MIR dev [2a-1b] | SPEC | Derivation | Analog (dev: default / MIR) | Mark |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| f7a 622 | `[var state]` exchange; `z@follow = 42` before reading the 2nd result | E | E Loan@10 | A | E Loan@10 | R-E: 1st call stores `z.*`'s borrow into `var state` (`ref/i32 during x` holds it); 2nd result reads `state` → carries it → E | m-f7a: E / E | Gm |
| f7a2 | twin, read first | E | A | A | A | as f7a, last use before the write → A | — | |
| f7b 1379 | `c(y@ref)` in an inner block, `c(r)` after it | E | E Loan@10 (y's end) | A | E at y's storage end | R-E into `var start`; the closure carries y's Loan past the block → E | m-f7b: E / E | Gm |
| f7c 575 | `[holder@uniq]`, `holder@follow = b` | E | E Loan@8 | A | E Loan@8 | R-E into the captured referent (holder's slot); `return holder@follow` reads it → E | m-f7c2 (param slot): E / E | Gm |
| c-f7c2 | same, slot a parameter (`[slot@move]`) | E | **A** | A | E Loan@7 | R-E into storage the body does not own (used after every point) → E | m-f7c2-ok: A / E | Gd (G75 E7), Gm |
| f7d 576 | `store@move(z@follow@ref)` | E | E Loan@8 | A | E Loan@8 | the moved temporary is acquired as `uniq/Self`; R-E into the captured referent outlives the temporary → E | — | Gm |
| c-f7d2 | same, slot a parameter | E | **A** | A | E Loan@7 | as c-f7c2 → E | m-f7d2: A / E | Gd, Gm |
| f7e1 | SPEC §15.8.2 example as written | S, S | U@7 `get()`; Unsat@12 | U@7 [+ Unsat@12] | E Unpr@12 only | `get`: C-env, `view` depends on the receiver, `get` stays → A; `invalid`: elided `ref/string` is static (no borrowed input; the receiver is no elision input), `other@ref` is in the receiver (C-env), receiver→static Unknown → E Unpr@12 | m-t1a A/A; m-g65a Unpr | Gd (U; c), Gm (U; no Origin stratum; 2a-1b c) |
| f7e2 | `let moved = get@move` uncommented | S | U@7 | U@7 | E Loan@`get@move` | `view` carries `get`'s receiver Loan → Move while used → E | m-t1b: E / E | Gd, Gm |
| f7f | `[t@ref]`, `c()`, then `t = makeText()` | S | A | A | A | the entry Loan's last use precedes the write → A | — | |
| f7g row 18 | `let m = c@move; consumeText(t@move); m()` | S | E Loan@8 | E Loan@8 | E@8 | moving a Closure keeps its capture Loan (§15.8.3) → E | — | |
| f7h1 A3-closure | `[slot@uniq]` storing `y: s/T`; write `v` then read slot | E (both) | E Loan@10 | E Loan@10 | owner A, uniq E@10 | R-E per case into the captured referent (`s/T` holds `y`): uniq stores the child of `v` → E; owner Copies → A | m-A3: inconclusive (§6) | |
| f7h2 | twin, read first | E | A | **E Loan@11** | A | → A | — | Gm (G147 (8i), 2c-4) |
| f7i1 A9-keep-ok | `[v]` moved through `keep`, called, then write | S | A | **E Loan@10** | A | entry Loan travels with the moved Closure; last use before the write → A | — | Gm ((8i)) |
| f7i2 A9-keep | write before `kept()` | S | E Loan@9 | E Loan@9 | uniq E | → E | — | |
| f7j1 | nested `[count@uniq]` in `var outer` | E, E | A | A | A | the entry Borrow of `self.count` through outer's `uniq/Self` is contained in outer's receiver; inner used inside → A | — | |
| f7j2 | outer captures `count` as `let` | S, – | E InvalidAssignment@4 | same | E (§7.7.2: `let`) | P-772 → E | — | |
| f7j3 | `let outer` (Exclusive) | E, E | E InvalidAssignment@7 | same | E | P-772 / §7.7.3 → E | — | |
| f7k1 | `[x@ref]`, `x = 2`, then `c()` | S | E Loan@4 | E Loan@4 | E@4 | R-S: the Closure carries the entry Loan → E | — | |
| f7k2 | twin, call first | S | A | A | A | → A | — | |
| f7k3 G147 (3) | `[var n]`: `let r = n@uniq; let q = n; r@follow = 3` | **S** (SPEC E) | E Loan@5 | **A** | E Loan@5 | E receiver: `n@uniq` forms a child of `self.n`; reading `n` while `r` lives → E | m-f7k3: E / E | Gm (2c-1); receiver |
| f7k4 | twin | S | A | A | A | → A | — | |
| f7l1 per case | `[value]` of `s/T`, `let local = value`, `var h` | S/E (today E) | A | A | A | owner S, uniq E (bare Reborrow); `var h` writable → A | — | |
| f7l2 | same, `let h` | S/E (today E) | E InvalidAssignment@7, @8 | same | uniq E (owner A) | uniq: Exclusive call on a `let` Closure → E | — | (today: both cases, PD13) |
| f7m | `[var count]`, outer `count = 5`, two calls | E | A | A | A | a value capture is a snapshot → A | — | |
| f7n1 | `[cell@uniq]`, `Cell<s/T>`, store `y` into `cell.item` | E | E Loan@13 | E Loan@13 | uniq E | as f7h1 → E; after 2c-3 the store passes the guard only through `Cell`'s Type argument, so P13's hold makes the uniq view U (PD30, G148 (1)) | — | (expected U after 2c-3) |
| f7n2 | twin | E | A | **E Loan@14** | A | → A | — | Gm ((8i)) |
| f7o | `[r, w]`: `let r: ref/i32 during x`, `w@follow = v@follow` | E | E Loan@8 | A | A by §7.7.2 (a `let` binding is not writable) | **D1**: R-E over `var` bindings and captured referents → A; over the whole referent as for methods → E | m-f7o2 (`let` Field): E / E | **D1**; Gd (if A) |
| f7o4 | same with `[var r, w]`, `r` never written | E | E Loan@8 | A | E | R-E: a `var` binding of that Type may retain `v` (by contract, not body) → E | m-f7o3: E / E | Gm |

### 3.2 SPEC §7.7 examples (as written; each commented error line uncommented)

| Case | Form | Recv | Default | MIR dev | SPEC | Derivation | Mark |
| --- | --- | --- | --- | --- | --- | --- | --- |
| s771 | §7.7.1 (its `invalid` line is active) | S, S, – | E UnresolvedBinding@4 `value` | same | E@4 (no fixed input signature) | no Origin content → E | c (Binding) |
| s772a | §7.7.2 `Totals` (static `storage`) | S | U@4 (P42; spec-cases records it) | A | A | static access, no capture → A | Gd (P42) |
| s772b | §7.7.2 captures | S, S, E | A | A | A | `bump`: `[r]` Reborrows, writes the referent → E receiver, `var bump` → A | |
| s772b-bad | `let bad = func () => text` | S, S, S, E | E TransferRequired@8 ×2 | E StaticMovePathRequired@8 | E@8 | P-772 → E | c (MIR A1's code) |
| s773 | §7.7.3 | E, C | A | A | A | `next` snapshot; `take@move()` C-move → A | |
| s773-take | `take()` | E, C | E TransferRequired@16 | + MovedPlace@16 | E@16 | → E | |
| s774 | §7.7.4 | S | A | A | A | erasure: Shared, Owned environment, result contract independent of the receiver → A | |
| s774-view / -mixed / -result | the three commented lines | S | E TypeMismatch@9 / TypeMismatch@14 / NoApplicableOverload@15 | same | E each | no Origin content → E | |

§15.8.2's example is f7e1/f7e2.

### 3.3 §15.8.2's table rows (R4-M99 §3's rows), each as a program

| Case | Row / form | Recv | Default | MIR dev | SPEC | Derivation | Analog | Mark |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| t1a | env-owned Borrow `[n] () => n@ref`, `view` used | S | U@5 | U@5 | A | C-env → A | m-t1a: A / A | Gd, Gm (R1, OQ4) |
| t1b | + `get@move` while used | S | U | U | E Loan | → E | m-t1b: E / E | Gd, Gm |
| t1c | `[var n]`, `n += 1; return n@ref`, two calls | E | U ×2 | U ×2 | E at 2nd call | C-env through `uniq/Self`: the receiver Loan stays exclusive while `a` depends on it → E | m-t1c: E / E | Gd, Gm |
| t1d | `[n, r]` (`r = y@ref`), `y = 5` while `v` used | S | U | U | E@`y = 5` | R-S: `v` carries every Loan a read through the receiver carries (r's on y) → E | m-t1d: E / E | Gd, Gm |
| t2a / t2b | captured `ref` (`r`, `r@follow@ref`), move `c` with `v` live | S | A | A | A | C-ref: keeps `o`, not the receiver → A | m-t2a: A / A | |
| t3a / t3b | `var c = func [u] () => u` (bare Reborrow), move `c` before / after `a`'s last use | E | U | U | E / A | C-uniq under `uniq/Self`: in `o` and the receiver → E / A | m-t3a E, m-t3b A | Gd, Gm |
| t3c / t3d | `[u] () => u@follow@ref` | S | U | U | E / A | C-uniq under `ref/Self` (receiver = first shared layer) → E / A | m-t3c E, m-t3d A | Gd, Gm |
| t4a | Consuming, `-> uniq/i32 during u`, `return u` (`[u]` bare entry) | C | U@11 | U@11 | E Unpr at `return u` | C-own + F-cap: the binding's Origin is the entry Reborrow's, within `u`'s (§3.5.1) and fixed in the body; it cannot be shown to outlive `u` → E | — | Gd, Gm (U; see t4e) |
| t4b | same, result inferred | C | U | U | A | C-own: Reborrow through `u` contained in the binding's Origin, child ends with the receiver, parents stay → A | — | Gd, Gm |
| t4c | t4b + `x = 5` while `a` used | C | U | U | E@`x = 5` | `a` keeps the capture's parent Loan on `x` → E | — | Gd, Gm |
| t5 | `[u] () => u@move`, `c@move()` | C | U | U | A | C-move → A | m-t5: A / A | Gd, Gm |
| t5b | `[u, r]`, `y = 5` while `a` used | C | U | U | E@`y = 5` | R-C: `a` names a capture Origin of the owned receiver argument → carries all its Loans (r's on y) → E | m-t5b: **A** / E | Gd, Gm (analog: old path per Origin) |
| t6 | `func (y: ref/i32) => y`, move `c` with `a` live | S | A | A | A | C-arg → A | — | |
| t7a | call-local Borrow, indented body, result inferred | – | E Unsat@3 (header) + NotCallable@6 | + **InternalInvariant@5 ×2** | E Unsat at `return l@ref`, Closure typed | C-local → E at the returned value | — | c (location, derived failure), §6 F2 |
| t7b | Borrow of a consumed binding | – | E Unsat@7 (header) + UnresolvedBinding@10 | + InternalInvariant@9 ×2 | E Unsat at `return text@ref` | C-own → body-local → E | — | c, F2 |
| t7c | call-local, `-> ref/i32` | S | E Unsat@5 | A [E Unsat@5] | E Unsat@5 | → E | — | Gm (dev: no Origin stratum) |
| t7d | `(1 + n)@ref` | – | E Unsat@4 (closure) | + Loan@4 `(1 + n)` | E at the returned value only (§15.8.2: not a Loan conflict at the storage's end) | C-local → E | — | c (MIR also reports the temporary's end) |
| cc1 | Copy Closure, `n@move` (Consuming), called twice | C | A | A | A | a Consuming call Copies a Copy Closure → A | — | |

### 3.4 Review-1's Consuming `var` forms (P1-B1), the Owned forms (P1-B2)

| Case | Form | Recv | Default | MIR dev | SPEC | Derivation | Analog | Mark |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| rc1 | `[var first@move]`: move out, `first = makeResource()` | C | A | A | A | P-772 (a `var` binding is reinitializable) → A | m-rc1: E InvalidAssignment (by-value receiver, §7.2) | |
| rc1b | the same with a `let` capture | C | E InvalidAssignment@10 | + MovedPlace@10 | E | P-772 → E | — | |
| rc2 | `count += 1` beside a Move in the body | C | A | A | A | P-772 → A | m-rc2: E InvalidAssignment | |
| rc3 | `counter.bump()` (`uniq/Self` method) on a `var` capture, Consuming | C | A | A | A | §15.1.5: a binding is not an owned-Type parameter → A | — | |
| rc4 | nested `[count@uniq]` in a Consuming outer call | C, E | A | A | A | judged by the outer call's permission; the entry Borrow is body-local, used inside → A | — | |
| ow1 | `func (b: ref/i32 during x) -> ref/i32 during x => b` to `keep<T: Owned>` | S | **A** | **A** | E (Constraint: `x` in OwnedOrigins) | O-own → E | — | Gd, Gm (§6 F1) |
| ow2 | no fixed Origin | S | A | A | A | → A | — | |
| ow3 / ow4 | `[x]` capture / `keep(x)` | S / – | E UnprovenConstraint | same | E | O-own → E | — | |
| ow5 | ow1's Closure erased to `(ref/i32 during x) -> ref/i32 during x` | S | A | A | A | empty environment is Owned; written fixed Origins are allowed → A | — | |

### 3.5 G229's class S (BU-28's census), G216's headers, G65

| Case | Form | Recv | Default | MIR dev [2a-1b] | SPEC | Derivation | Mark |
| --- | --- | --- | --- | --- | --- | --- | --- |
| s-p3 | `[var arr@move]` of `Array<ref/i32 during p>`, `arr.append(x: ref/i32 during p)`, called | E | E Loan@3 (creation) + CallActivation@4 | A | A | F-cap: `p` outlives `p`; R-E retains `x` (a parameter of `g`, no Loan in `g`) → A | Gd (G229, now default only) |
| s-p3d / s-p3e | `[var arr@move, p]`, `arr.append(p)`; called / not called | E | E CallActivation@4 | A | A | → A | Gd |
| s-k5 | `[var h@move]`, `h.call(y)` over a set Origin | S | E Loan@7 (creation) | A | A | → A | Gd |
| s-r4 | `during r` (a local) | E | E CallActivation@6 | A | A | → A | Gd |
| s-r6 | `origin p outlives q` | E | E CallActivation@5 | A | A | the enclosing clause is a premise visible in the body → A | Gd |
| s-x-p5 | class S: two environment regions | E | E Unpr@6 | E Unpr@6 | E | F-cap, no premise → E | |
| s-x-e1 | class S: element returned as static | S | E Unpr@6 | A [E Unpr@6] | E | F-cap → E | Gm (dev) |
| s-x-n6 | receiver analog (method) | – | A | A | A | → A | |
| h1 | `func [] (p: ref/(ref/i32)) -> i32 => 1`, not called | S | A | A [**U@2** header] | A | the header's well-formedness (`ι` outlives the per-call `π`) is a premise of the body and an obligation of each call, as a method signature's; no call → A | Gm+ (G216) |
| h2 | called with `x@ref@ref` | S | A | A [**U@2**] | A | the call proves it with its fresh region in the calling body → A | Gm+ (G216) |
| g65a | `[n] () -> ref/i32 during p => n@ref` | S | E Unsat@3 | A [E Unsat@3] | E Unpr@3 | C-env: receiver outlives `p` Unknown → E Unpr | c (G65; row 4) |

### 3.6 Supplementary closure probes (dev; `p7\cases-x`, `p7\param-slot`, `p7\failed-closure`)

| Case | Form | Recv | Default | MIR dev [2a-1b] | SPEC | Derivation | Mark |
| --- | --- | --- | --- | --- | --- | --- | --- |
| r1a–r1c | `[p@move] () -> uniq/i32 during p => p` (and `ref/… => p@follow@ref`), called | E / S | U at each call | U | E Unpr in the body (+ the calls' Loans) | C-uniq: contained in `p` and the receiver; receiver outlives `p` Unknown → E | Gd, Gm |
| r1e / r1f | the same, not called | E / S | **A** | **A** [A] | E Unpr | → E (analog m-r1e/r1d: E Unpr both) | Gd, Gm, Gm+ (G226 C3) |
| r1g | `[n] () -> ref/i32 during p => n@ref`, not called | S | E Unsat | A [E Unsat] | E Unpr | C-env → E Unpr | c |
| t4e | `[u, t@move] () -> uniq/i32 during u`, `return u`, not called | C | **A** | **A** [A] | E Unpr | F-cap + §3.5.1 → E | Gd, Gm, Gm+ (§6 F3) |
| t4f | `[u@move, …]` (a Move capture), same result | C | A | A | A | the binding's Type is `u`'s; C-own → A | |
| t4g | `[u] () -> ref/i32 during u => u@follow@ref`, not called | S | **A** | **A** [A] | E Unpr | C-uniq and §3.5.1 → E | Gd, Gm, Gm+ |
| t6b | `func (y: ref/i32) -> ref/i32 => y`, move `c` with `a` live | S | A | A | A (§15.8.2 ¶3: elision never takes the hidden receiver) | A (R4-M99 keeps that paragraph; OQ-C2 (a)) | analog m-t6b: E / E (method elision takes the receiver, §15.4.3 rule 1) |
| t8a | `func () => Limits.max@ref` (immutable static) | – | U (P42) + UnresolvedBinding `ref` | same | **D2**: A by §15.6.5, E by the catch-all read literally | A (static outlives the per-call result Origin) | D2; Gd, Gm (P42) |
| t8b | mutable static | – | U (P42) | same | E (bounded by the body) | → E | |
| t8c | `(n, "text")` | S | A | A | A | → A | |
| e1 | `func (x: i32) => x@ref`, called | – | E Unsat (closure) | same | E at the returned value | C-local → E | c |
| e2 | the same with an indented body | – | E Unsat (header) + NotCallable | + InternalInvariant ×2 | E at `return x@ref` | → E | c, F2 |
| e3 | indented call-local, not called | – | E Unsat (header) | + Loan at `let l` | E at the returned value only | → E | c (double report) |

## 4. Differences for the owner (derivation vs SPEC's current text)

**D1. Retention into a `let` environment binding (`f7o`).** A Closure `[r, w]` with `let r: ref/i32 during x` captured as `let` and
`w: uniq/i32`, whose body writes `w@follow = v@follow` (so the call is Exclusive), is called with `z@follow@ref`; then `z@follow = 42`
and a later call reads `r`. SPEC today: §7.7.2 gives each binding its capture's mutability ("an Exclusive call may also assign a `var`
binding and use captured `uniq` referents"), so the call's writable storage is `w`'s referent (an `i32`, which cannot hold the
argument) and nothing retains `z`'s Loan: valid. R4-M99's sentence makes the bindings "Fields of the receiver's referent" and lets
retention "follow the receiver rules"; for a method the implemented rule retains by Type into the whole referent, `let` Fields
included (a `uniq/Self` body can replace its whole referent with `replace`, §15.7, which no Closure body can do to its environment):
the method analog with a `let` Field (m-f7o2) is rejected on both pipelines. R4-M99 §6 also describes 2c-2's Exclusive row as "the
environment and captured exclusive referents", the whole environment. Read that way the derivation rejects `f7o`: a verdict change
(more conservative). Read with §7.7.2 deciding which environment storage is writable (`var` bindings and captured exclusive
referents; the A5 step 2 plan's 2c-2 row calls the output "the environment's writable storage"), it accepts `f7o` and equals SPEC.
Today the default pipeline rejects `f7o` (H: its retention is by Type, as for methods) and the MIR accepts it only because it stores
nothing into captures yet. Options: (a, recommended) the SPEC session words the sentence so §7.7.2's permissions decide the writable
environment storage (no closure body can replace its environment), 2c-2 takes `var` bindings and captured exclusive referents, and
the default pipeline's rejection of `f7o` becomes a recorded old-path limit until R6-check; (b) retention over the whole environment by
Type, one rule with methods: `f7o` becomes invalid, a loss under OQ8 (library: no capture lists; milestones: none of the capture
lists a grep finds binds by `let` a Type that can hold an Exclusive call's argument, by reading, H; xUnit not counted).

**D2. §15.8.2's catch-all against a static source (`t8a`).** §15.8.2 says of an inferred Closure result: "Any other source, such as a
Borrow of call-local storage or of an environment consumed by the call, is an error." Read literally, `func () => Limits.max@ref`
(an immutable static Field, Origin `static`) is such an "other source" and an error. §15.6.5's chain rule (which R4-M99 makes the
only rule, the table becoming a restatement) proves `static` outlives the per-call result Origin: valid. Today both pipelines report
the static Borrow as the located `Unsupported_Kd` (P42), so no verdict is observable; library and milestones 0. Recommendation: the
SPEC session states the catch-all as what the receiver rules derive ("a source whose Origin is body-local, such as …, is Refuted at
the returned value"), so the restatement and the rule agree.

**Constraints already in R4-M99, now shown by runs** (no decision needed; for the SPEC session's wording): the sentence covers result
Origins, containment and retention only. Permissions stay §7.7.2's: the method analogs of the review's Consuming forms are rejected
(m-rc1, m-rc2: `InvalidAssignment_Kd`, a by-value receiver is `let`-like), the closures are valid. Result elision stays §15.8.2's third
paragraph: `func (y: ref/i32) -> ref/i32 => y` is valid with the Closure moved while its result lives (t6b), the method analog is
rejected (m-t6b, elision takes the receiver).

No other case's derivation differs from SPEC: the plan's f7a–f7n, every §7.7/§15.8.2 example, §15.8.2's seven rows, review-1's five
Consuming forms, the Owned forms, G229's class S and its controls, G216's headers and G65's form.

## 5. Implementation gaps (SPEC vs today) for 2c-D

| # | Gap | Cases | Today | Owner / unit |
| --- | --- | --- | --- | --- |
| G1 | A Closure call stores nothing into its receiver (the temporary rule, `MirBorrows.Calls.cs` `Call`) | f7a, f7b, f7c, f7d, c-f7c2, c-f7d2, f7o4 | MIR accepts (unsound shadow); default rejects except the parameter-slot forms (G75 E7, unsound on default) | 2c-3 (C2's store rows through W) |
| G2 | Owner-or-uniq pair carriers' self-store rows | f7h2, f7i1, f7n2 | MIR false Loan conflicts | BU's 0c (2c-4), joint merge |
| G3 | Exclusive use of a `var` capture classed Shared | f7k3 | MIR accepts the body conflict; `BoundClosure.Receiver` Ref | 2c-1 |
| G4 | Receiver-dependent results are `Unsupported_Kd` (R1) | f7e1, f7e2, t1a–t1d, t3a–t3d, t4a–t4c, t5, t5b, r1a–r1c | U on both | 2c-5 (receiver substitution), OQ4 (a)'s gate for the default |
| G5 | A Borrow of an environment binding through a borrowed receiver judged body-local | f7e1's `invalid`, g65a, r1g | Unsat on default and 2a-1b, SPEC Unpr (same verdict, other code) | 2c-D: C1-F4 row 4's interim (Inferred, contained in the receiver; Finite under `owner/Self`); G65 on the old path |
| G6 | Containment omits the call receiver for Borrows through captured references | r1e, r1f, t4g | A on both and 2a-1b; SPEC E Unpr (not reachable at a call today: every such call is U, and erasure or a Callable cannot carry the result, H) | 2c-D (G226 C3: BU-27's meet stops below the receiver) |
| G7 | An inferred result's Refuted source is reported at the Closure, which then fails | t7a, t7b, e2 (e1 at the Closure) | E at the header; NotCallable/UnresolvedBinding at uses; MIR validator findings (§6 F2) | 2c-6 (relation at the returned value) with Binding (`Binding.Closures.cs:431-435`, `CallLocalOrigin`) |
| G8 | The MIR also reports the escape as a Loan conflict at the storage's end | t7d, e3 | double report | 2c-6 with G:A5-F4's 2a-2 (rule W) |
| G9 | G229's class S rewrites | s-p3, s-p3d, s-p3e, s-k5, s-r4, s-r6 | default rejects; **dev's MIR accepts** (BU-28 recorded both) | old path: G65/R6-check; 2c-D must keep the MIR accepting them |
| G10 | Closure header rows with an omitted inner Origin | h1, h2 | U on 2a-1b's MIR (pinned) | 2c-D: the row is the call contract's obligation, proven at each call (G216) |
| G11 | `let`-binding retention | f7o | default rejects | per D1 |
| G12 | Bare-entry Reborrow capture's Origin | t4a, t4e, t4g | A (BU-28: the binding names the source's Origin) | §6 F3; 2c-D decides with BU |
| G13 | Maximal receiver instead of per case | f7l1, f7l2 | same verdicts in these forms | PD13 (strongest stays; per-case acquisition in PLAN) |

## 6. Findings outside 2c-D's design (for PLAN)

- **F1. A concrete Closure's fixed signature Origins are missing from its Owned proof.** `func use(x: ref/i32)` passing
  `func (b: ref/i32 during x) -> ref/i32 during x => b` (no capture) to `keep<T>(value: T)` with `T is Owned` is accepted on both
  pipelines (`ow1`); SPEC §15.2.3 puts "a concrete Closure's fixed signature Origins" in OwnedOrigins, so `x` makes it a Constraint
  failure, as the capture form (`ow3`) and `keep(x)` (`ow4`) are. Unrecorded in PLAN and STATUS. Binding (BU); exploitability H
  (static storage of generic Types is P42; erasure requires the environment only).
- **F2. A failed indented Closure.** When an indented body's inferred result is Refuted (`return l@ref`, a parameter's or a consumed
  binding's Borrow) and the Closure is called, its published signature has a `Never` result (the MIR's `closure () -> Never`), the call gets
  a derived `NotCallable_Kd`/`UnresolvedBinding_Kd`, and the MIR builds `_0: Never` with `_4 = move _3` (a `ref/i32` into a `Never`
  temp): validator findings Type and NeverWrite, published as `InternalInvariant_Kd` under the mir pipeline (t7a, t7b, e2; dumps in
  `p7\cases\t7a-call-local.mir`, `p7\failed-closure\e2-indented-param.mir`). R4-M12 requires no validator finding in any program. The
  expression form (e1) keeps its Type and builds cleanly. Binding (`CallLocalOrigin`'s failure) or the builder's contract for a failed
  Closure (BU / R5 W1); 2c-6 moving the relation to the returned value would remove the cause if the Closure keeps its Type.
- **F3. The bare-entry Reborrow capture's Origin.** SPEC §3.5.1: a Reborrow's child "keeps the stored Type with an outer Origin within
  the parent's"; `[u]` initializes the binding as `let u = u` would, so in the Closure body the binding's Origin is that child's, fixed
  and not provably as long as `u`'s. BU-28 made a binding's Type its source's ("a captured reference's Origin"), so an annotated result
  `during u` over a bare-entry capture is accepted (t4e, t4g), where SPEC's reading gives `UnprovenOriginRelation_Kd`; with a Move
  capture (t4f) both accept. No unsound use found (the region choice is one region inference could make, H). Either 2c-D implements
  §3.5.1's child Origin (a SPEC-conformance rejection, OQ7 (a), census needed) or the owner confirms BU-28's choice as a SPEC reading.
- Outside F7 (methods, old path, recorded for completeness, H whether G75 covers them): the default accepts m-t5b (per-Origin instead
  of per-argument retention) and m-f7c2-ok / m-f7d2 (a store into a parameter's referent through a method), which the MIR rejects as SPEC
  does; the generic-struct analog of A3-closure (m-A3) is accepted by the default and held by P13 on the MIR. s771's code
  (`UnresolvedBinding_Kd` for "no fixed input signature") and s772b-bad's MIR code (`StaticMovePathRequired_Kd` beside the default's
  `TransferRequired_Kd`) are Binding's/A1's, not F7's.

## 7. Inputs to 2c-D (not a design)

**C2 needs from Binding** (`BoundClosure` today: captures, one maximal `Receiver`, `EnvironmentType`, `ExclusiveUse`, `EvaluatingBody`):
1. Per capture: source, acquisition (Copy, Move, `@ref`/`@uniq` entry, bare Reborrow), the binding's `let`/`var`, its Type and Origins
   (F3 decides a bare Reborrow's Origin).
2. The receiver per admitted Semantics case (f7l), with G147 (3)'s one exclusive-use predicate (2c-1, f7k3); PD13 keeps the strongest
   for acquisition.
3. The environment Type with the fixed signature Origins (O-own; F1 shows Binding's Owned proof needs them too).
4. A CallRetention over the receiver slot and the parameters, per case: Exclusive: store rows from each parameter into the writable
   environment storage (D1: `var` bindings at layer 1 of the closure holder, captured exclusive referents deeper); Consuming: captured
   exclusive referents only, and a result naming a capture Origin retains the whole receiver argument (t5b); Shared: none; result rows
   from the receiver (C-env, C-uniq up to the first shared layer, the receiver included), each capture and each parameter (C-ref,
   C-arg). Memoized per (closure, context, scope) as 2c-2 plans.
5. The inferred result contract: the per-call result Origin with its sources by §15.6.3's walk; a Refuted source located at the
   returned value with the Closure still typed (G7, F2); the annotated result's elision without the receiver (t6b).
6. At value calls (2c-5): the receiver's Origin substituted by the call's receiver borrow, replacing R1's `Unsupported_Kd`
   (`Binding.ValueCalls.cs:464-471`, `:563-573`), `ResultFixedForCall` and `CallLocalOrigin`'s environment scan; OQ4 (a)'s gate keeps the default.
7. G216: a header's well-formedness row is a premise of the Closure body and an obligation of each call, not a row of the evaluating
   body at the literal.
8. Containment for Borrows through captures includes the call receiver up to the first shared layer (G6; G226 C3), and the class of a
   Borrow of a binding is Inferred under `ref/Self`/`uniq/Self`, Finite under `owner/Self` (G5).

**MirBorrows needs:** C2's rows for a Closure-typed Value callee with the callee operand in the receiver position, stores through W
(layer 1 the closure holder for a reference receiver; deeper layers the captured referents; storage the body does not own for a
parameter's referent, c-f7c2), P13's hold for stores past a Type argument (f7n1 → U until G148 (1)), a Consuming call's stores into
what the moved value names, and the result rows; the temporary rule deleted. With a correct receiver (2c-1) f7k3 needs no new rule (the
method analog is caught today). G2's false positives go with BU's 0c. Keep G229's forms accepted.

**MirOrigins needs (2a-1a/1b held):** row 4 per receiver case (G5: Unpr not Unsat for g65a, f7e1, r1g; Refuted for a consumed
binding, t7b), the receiver's containment edges for Borrows through captures (G6: r1e, r1f, t4g), 2c-6's result edges from each returned
source to the fixed result Origin located at that source (t7a–t7d, f7e1), G216's header rows classed as call obligations (h1, h2 no
longer U), and the bare-entry capture Origin (F3).

## 8. Not verified

- The derivation is by reading; the 29 method analogs check it only where a struct and method can express the case (none for per-case
  receivers, nested captures, Copy Closures or the G216 headers; the generic-pair analog of A3-closure is inconclusive).
- D1's census (library 0 capture lists; milestones by reading; xUnit not counted) and whether any test pins `f7o`'s default rejection.
- PD13's located `Unsupported_Kd` (a conflict whose only witness is a receiver Loan stronger than its case admits): no program built.
- The cases ran under the probe's Application binding, not each test class's harness; R5's code generation of the receiver (no native
  run); timing and allocation (no change was made).
- G6's and F3's acceptances are not shown exploitable or unexploitable beyond the forms listed (H).
- f7n1's expected U after 2c-3 is P13's rule read, not run.
