# R6-check H11b: R4-M108's list per position (design, revision 2)

> Status (2026-11-06): moved to R7's Problem records unit by the owner (verbatim 「Cで決定します」; H11b-0's design §15-§16); kept as R7's evidence, not a plan of R6-check.

**Revision 2 (2026-11-05):** answers BU's re-check 1 (REVISE, B-4; N-a), under R5's instruction to change nothing else. Changes are
marked "(B-4)" and "(N-a)". §7-§12 are kept as records, and revision 1 is `notes/H11b-design.r1.md`.
- (B-4) The judge takes the failing `node` beside `use` and `at`. §2 names all three for every caller. `ResultPropagation.cs:581`
  records through the judge.
- Two operation inputs (Dictionary keys, value-call inputs) fail the input, not the operation, as at calls, so no independent record
  is lost there (`b4_el1`, `b4_vc3`).
- (N-a) `rp7` is classified: pre-existing, unchanged by H11b, a PLAN issue.
- No new acceptance move and no lost independent record.

**Revision 1 (2026-11-05):** answers BU's design review (C-36, REVISE: `bu\batch\H11b\design-review.md`) under R5's rulings: B-1, B-2 and
B-3 are fixed at their cause, and the non-blocking N-1 to N-8 are applied (§11 says how). Changes are marked "(B-1)" … "(N-8)". §7-§9 are
kept as records. Revision 0 is `notes-H11b-design.r0.md` beside this file. **No new acceptance move and no lost independent record
appear in the revised census.** Revision 0's census had three cases (fn2, fn3: an invalid program accepted with its only record lost;
r1_raw_ff: the same); revision 1 keeps each record (§1).

Status: for BU's re-check (findings only) and the owner's G1 answer (OQ2). Owner class. Base: origin/dev `6ef56f387`; BU's review ran
at `dafe062a3`, whose `src/` and `tests/` are the same. Probes: worktree `bin/worktrees/r5-h11b`, dev's CLI copied to `cli/`, and the
throwaway attribution test `tests/xUnitTest/Tests/H11bCensusProbe.cs` (never committed). Programs `p/<form>/Main.kimi` (BU's are `p/bu_*`);
outputs `census-cli.txt` and `p/census-test.txt`. Paths: `B/` = `src/Kimi/Compiler/Binding/`, `T/` = `tests/xUnitTest/Tests/`. Working
time: `design-state.md`.

## 0. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **The judge: `CheckTypeUse` is the one non-call use-site fit (B-1, B-2).** It makes one root walk (the premise walk, a recording root, `ValueFits.cs:89-90`), and then:<br>• the walk fits with no local region on either side: true, no rows (`:95-98` unchanged; B-2);<br>• a structural failure (`Shape`): false, nothing enumerated;<br>• otherwise one enumeration of the positions (today's `CheckTypeUseCore` recursion, **without its per-component sub-walks**, so `comparisonFacts` is never cleared after the root walk; B-1's third point, B-2's double walk).<br>Each position, its ends resolved at the use:<br>(1) **(N-1)** an end of kind Unbound: Binding's record unless `FailedOriginEnvironment` names the failed clause it rests on (today's `OriginFits.cs:102-112`), and no row, because A5 never judges a Dependent end (R4-M118);<br>(2) a body chain (`BodyChain`): a row, whatever the premise proof, as at `JudgeCallPosition` (`CallRelations.cs:304-307`);<br>(3) fixed-fixed: nothing when the premises prove it; a fixed `Unrepresentable` is a row (as `CallRelations.cs:329-337`); otherwise Binding's failing chains through `FailedOriginPart`, recorded as in D3.<br>A Function component forms no position (D6). **Result (B-1):** false exactly when the root walk kept a non-position fact (a condition, `Limit`, `Shape`) or a fixed-fixed or Unbound position failed. | R4-M108's list read per position (C-26). B-2: `CheckTypeUse` keeps its own regional rule (`:95`), so no caller gets a walk-then-judge chain. B-1: a fit that fails only in a Function component's condition returns false, so its contract record stays. | `B/Binding.ValueFits.cs` |
| D2 | **Every recording consumer calls `CheckTypeUse`; §2's table gives each caller's fate (B-2, B-3).** The fallback `!FitsTypeAt(..) && !CheckLocalTypeUse(..)` (6 sites) and `HasLocalRegion ? CheckTypeUse : FitsTypeAt` (`ControlFlow.cs:335`) become one call. `CheckLocalTypeUse` goes (`LocalRegions.Types.cs:247-248`). Each caller passes `at`, the location its `FailMismatch` names. Pure queries keep `FitsTypeAt`: trials whose false answer leads to another attempt (`ResultPropagation.cs:589, 664, 885, 894`, `ArgumentOperations.cs:299-317`, `ArgumentKinds.Fit.cs:179`, `Enums.cs:267`'s structure-failed arm, `IsSubtypeAt`). **(B-4)** `ResultPropagation.cs:557-581` is a recording caller: its trial (`:557`) and `TryPropagate` stay first and pure, and its record (`:581`) goes through the judge (§2). | One entry for one decision. A pure trial must not record: the late adaptation it leads to records at the part. | §2 |
| D3 | **The judge records Binding's fixed-fixed and Unbound chains where it judges (B-1, B-3).** **(B-4)** It fails `node`, the node the caller's `FailMismatch`/`RecordMismatch` names (today's `RecordOriginFitFailure` node), or at an operation input the input itself (§2); `use` stays the walk's use, and §2 names `use`/`node`/`at` per caller. It records them through `RecordOriginChains` on `node` at `at` with the fit's destination, the cause coming from `UnrefutedCause`/`FailedOriginEnvironment` (SPEC 23.1.2, BU-69; N-4). The first chain is the node's own record; later chains (other positions, a join's other sources) are further records. **`relationScratch`'s rule (B-3):** it is cleared when `CheckTypeUse` is entered and emptied by its own publication before `CheckTypeUse` returns (the invariant `RecordOriginFitFailure` states today), so it never crosses a call. The judge leaves `comparisonFacts` holding the root walk's non-position facts in walk order. `out failure` is the first of them, or else the first failed position's fact. **`RecordComparisonFacts` (B-1):** its own record is the first non-position fact (`Limit` gives `Unsupported`, a condition `ContractFact`), and only while the node is unfailed. Every later condition is a further record, also beside the judge's record (today `FailExplained` would drop it on a failed node). A `Position` failure whose node the judge did not fail is `InternalInvariant_Kd` (today's `Diagnostics.cs:482-484` contract). **(B-4)** Every caller passes the judge the node it then records, so this happens only on a path that bypassed the judge, a real contract break. At an operation input, a `Position`-only failure does not stop the caller (§2). `RecordOriginFitFailure`, its two searches, **the fit-failure gate** (`:99-109`), `OriginFitSearch.FailedOriginRelation` and **(N-4) the whole `OriginFitMode` enum and the search's `mode`** (`OriginProof.cs:369` included) go. `FailedOriginPart` and its chain helpers stay as the one fixed-fixed judge (OQ4: no third copy). | A check records typed facts where it fails (AGENTS). The judge alone knows the positions, so no later reader re-derives them. A join's sources each pass through the judge, so the scratch cannot hold a stale source's chains. | `B/Binding.OriginFits.cs`, `B/Binding.Diagnostics.cs`, `B/Binding.OriginProof.cs` |
| D4 | **The twins end at all three formation sites** (unchanged). (i) Fit: no body chain reaches `RecordOriginChains`, which loses its `rows` result and the twin add. (ii) Pending relation (`OriginProof.cs:361`): `BodyChain(..)` alone, Refuted included, so a pending body chain stays a row. (iii) Local clause (`OriginClauses.cs:247-266`): a body-chain failure is the row alone; a fixed-fixed one is the record. `originTwins`, `IsTwinRecord` and MirSide's filter (`MirTargetSide.cs:51`, `:13`, `:39-40`) go. | R4-M108, R4-M111; H11-design §15. | as listed |
| D5 | **Site (R4-M113, C-26; N-3): a new field `BindingObligation.Site`**, the location of the row's report, which `Binding.OriginRows` converts into `OriginRow.Site` (`Site ?? Use`).<br>The row's `Use` stays the use: it resolves the ends (`OriginAtUse` starts at it, `OriginProof.cs:221-227`), reads the premises and the case mask (`OriginRows.cs:193`).<br>The judge sets `Site = at`, and a body-chain clause row sets the clause record's `At`. `RelationAt` (`OriginFits.cs:48-49`) becomes `obligation.Site ?? obligation.Use`: its local-annotation rule is now the caller's `at` (`Variables.cs:95` passes the initializer), so one rule remains. | N-3: the record's location is not always a descendant of the use (a clause's initializer is a sibling; a classified Closure value; `BodyCaseMask`). A separate Site is a representation, so it is decided here and recorded in DECISIONS. It follows R4-M113 as it stands (§9: G's option (2) would publish exactly this field; under (3), R5 re-checks before C1). | `B/BindingModel.cs` (the record), `B/Binding.OriginRows.cs`, `B/Binding.OriginFits.cs`, `B/Binding.OriginClauses.cs` |
| D6 | **`fncomp`:** at a Function component the enumeration forms no position and goes on, at every level, the root included. The component's condition stays the root walk's fact, so the result is false (D1). Positions after it are judged like any other. **(B-1)** The skip changes only the enumeration; it never makes `CheckTypeUse` return true: fn2, fn3, T5 and T6 keep their `UnprovenOriginContract_Kd` (§1). | R4-M108's list (BU's `fncomp`); `T/OriginRowsTest.Boundary.cs:60-68` flips. | `B/Binding.ValueFits.cs` |
| D7 | **G432's input:** the enumeration's Function-component step is where G432's U1 adds the member row fact (a body-chain member position as a row). The fact comes from the root walk's kept facts, which D3 now preserves (B-1: G432 extends this publication, not today's per-recursion `comparisonFacts`). H11b adds no member row, and R4-M108's "the first differing Function Type component" stays G432's (N-6). | R4's proposal (§14 of H11-design); C-35 (3). | — |
| D8 | **Tests** (N-8 holds). Pins move once in §5's files. `OriginRelationDiagnosticTest.Twins.cs` becomes the per-position test. `…HasNoRowUntilH11` flips and is renamed; `AnIllFormedHeadLosesTheIndependentRecordUntilH11` is renamed by subject. New rows, failing on dev first: G1 (gate4, gate5, gate7, gate9) and its invalid twins G2 (gate5n, `g1_inner_scope_borrow`, **N-1**), the G56 store, f8, v1 and r1_raw_ff (Binding's record at the value), and j2 (a join's second source). **(B-4)** Also b4_el1 (the key fails, NoApplicableOverload_Kd kept) and b4_vc3 (both inputs recorded), and b4_fn1/b4_pr4 as node guards. Guards that must not move: fn1-fn3, lim1-lim3, cf1, cf1_join_ok, cf2, as1, u1-u4, r1_raw_ok, r2, r3 and l4. | AGENTS tests; BU's B-1 to B-3. | §5 |

## 1. Census (revision 1; probes on dev `6ef56f387`; "B" Binding, "A5" the MIR's Origin stratum; locations by span text)

Formation sites today: the fit's `RecordOriginChains` (`OriginFits.cs:163`, reached from `RecordOriginFitFailure` and from
`RoutePendingRelation`, `OriginProof.cs:379`) and the clause's `JudgeLocalClause` (`OriginClauses.cs:263`). The filter is
`MirTargetSide.cs:51`. No publisher removes a twin, so every twin is published twice. Re-run in revision 1: every form below, BU's 26
included.

| Row | Form (probe) | Path | Published today | After H11b | Q13 |
| --- | --- | --- | --- | --- | --- |
| T1 | return twin (`f1_bad`) | fit | Unsat A5 `return local@ref`; Unsat B `local@ref` | Unsat A5 `local@ref` | none |
| T2 | payload twin (`f7_payload_bad`; r03_n, cr5/cr6) | fit | Unsat B + Unsat A5, both `short@ref` | Unsat A5 `short@ref` | none |
| T3 | store twin (`g56_opaque`) | fit | Unsat A5 statement; Unsat B `local@ref`; the store's Loan is lost (Opaque) | Unsat A5 `local@ref`; A5's Loan record at `local = 2` (SPEC 15.6.5's example expects it, as Dependent, `spec-cases.json:154`) | none (N-2: OQ7) |
| T4 | mixed fit, plain (`f4_mixed_plain`, both orders) | fit | Unprov+Unsat A5 `return t`; Unprov+Unsat B `t` | Unprov B `t`; Unsat A5 `t` | none |
| T5 | `fncomp`, Function first (`f2`) **(B-1)** | fit | UnprovContract B `t`; Unsat B `t` | UnprovContract B `t` (the node's own record, the first non-position fact); Unsat A5 `t` | none |
| T6 | `fncomp`, borrow first (`f3`) **(B-1)** | fit | Unsat A5 `return t`; UnprovContract B `t`; Unsat B `t` | UnprovContract B `t` (own record); Unsat A5 `t` | none |
| T7 | closure result over call-local storage (`k1`) | fit | Unsat ×2 `x@ref` | Unsat A5 `x@ref` | none |
| T8 | environment borrow, pending (`k2`) | pending | Unsat ×2 `keep(a@move, b@ref)` | one Unsat A5 there | none |
| T9 | clause twin (`c1`) | clause | Unsat B `H.init(local@ref)`; Unsat A5 `origin x.s outlives a` | Unsat A5 `H.init(local@ref)` (D5) | none |
| T10 | return twin to a result-only binder (`bu_u2_unbound_local`; `nope` is a signature Origin, not Unbound) | fit | Unsat A5 `return local@ref`; Unsat B `local@ref` | Unsat A5 `local@ref` | none |
| T11 | join twin (`bu_cf3_return_join`) | fit (join) | Unsat B + Unsat A5, both at the `if` | Unsat A5 at the `if` | none |
| T12 | join, body chain and a fixed-fixed second source (`j2_join_two`) | fit (join) | Unsat B + Unsat A5 at the `if` (only the first failed source is judged) | Unsat A5 + **Unprov B** (`b outlives a`) at the `if` | none (an independent record added to a rejected program) |
| G1 | the gate's valid forms: assignment into a borrow-typed local (`gate4` `s = a`, `gate5` `s = z@ref`, `gate7` Tuple, **(N-1)** `gate9`, earlier misnamed `_bad`) | gate | Unsupported_Kd B at the assignment | **accepted** (their `let` analogues `gate4_let_analogue`, `gate5_let_analogue` and `bu_g1_inner_scope_let` are accepted on dev) | **acceptance move (named, §5)** |
| G2 | their invalid twins (`gate5n_conflict`; **(N-1)** `bu_g1_inner_scope_borrow`: `s = z@ref` in a block, `s@follow` after it) | gate | Unsupported_Kd B | ComparisonLoanConflict_Kd A5 (`z = 4`; `let z = 3`), as their analogues `gate5n_let_analogue` and `bu_g1_inner_scope_region` give on dev | none (still rejected) |
| L1 | let, body chain (`f6`) | CheckTypeUse | Unsat A5 at the whole `let` | Unsat A5 at the initializer `local@ref` (D5) | none |
| L2 | let, fixed-fixed (`f8`) | CheckTypeUse | Unprov A5 at the whole `let` | Unprov B at the initializer `b` | none |
| L3 | let and assignment, mixed (`f9`; `f10`, a twin today: 4 records) | CheckTypeUse / fit | f9: Unprov+Unsat A5 `let`; f10: Unprov+Unsat A5 statement, Unprov+Unsat B `(b, local@ref)` | Unprov B + Unsat A5 at the initializer or value | none |
| L4 | value-call input, fixed-fixed (`v1_valuecall_ff`, `h(b)`) **(B-3)** | CheckTypeUse (`ValueCalls.cs:490`) | Unprov A5 `b` | Unprov B `b` | none |
| L5 | value-call input, body chain (`v2_valuecall_body`) | CheckTypeUse | Unsat A5 `x@ref` | same | none |
| L6 | raw conversion, fixed-fixed (`bu_r1_raw_ff`) **(B-3)** | CheckTypeUse (`Conversions.Raw.cs:73`) | Unprov A5 `p` | Unprov B `p` (the caller records on false, §2) | none |
| L7 | join, fixed-fixed (`j1_join_ff`) | fit (join) | Unprov B at the `if` | same | none |
| L8 | **(B-4)** Dictionary key, fixed-fixed (`b4_el1_key_ff`) | CheckTypeUse (`Elements.cs:34`, an input) | NoApplicableOverload_Kd B `d[b]`; Unprov A5 `b` | NoApplicableOverload_Kd B `d[b]` (kept: the lookup goes on); Unprov B `b` (node the key) | none |
| L9 | **(B-4)** value call, two fixed-fixed inputs (`b4_vc3_two_inputs_ff`, `h(b, c)`) | CheckTypeUse (`ValueCalls.cs:490`, inputs) | Unprov A5 `b`, `c` | Unprov B `b`, `c` (the loop goes on) | none |
| T13 | **(B-4)** join of bound parts, body chain (`b4_rp13_join_vars_body`) | fit (join) | Unsat B + Unsat A5 at the `if` | Unsat A5 at the `if` | none |
| N-a | **(N-a)** join whose awaiting literal part misfits fixed-fixed (`bu_rp7_join_ff_plain`; `bu_rp4_noexp_ff` is the same form) | join (`ControlFlow.cs:347-351`) | **InternalInvariant_Kd** B at the `if` (pre-existing) | same (H11b does not change this path) | none (pre-existing; PLAN issue, OQ10) |
| — | guards that must not move: `bu_fn1` (UnprovContract B `g` + A5's well-formedness row at the Type); **(B-1)** `bu_fn2`, `bu_fn3` (UnprovContract B `g`, their only record: the result is false); **(B-1)** `bu_lim1`-`lim3` (Unsupported_Kd: the `Limit` is the first non-position fact, and the region positions become rows); **(B-2)** `bu_cf1_join_conflict`, `bu_cf2_let_join_conflict`, `bu_as1_reassign_conflict` (ComparisonLoanConflict_Kd A5 `x = 5`: the regional rows are kept by `:95`), `bu_cf1_join_ok` (accepted); `bu_u1` (Unprov B `a`, fixed-fixed), `bu_u3` (MissingOriginBinding_Kd), `u4_call_unbound` (accepted: `nope` is a call binder); `bu_r1_raw_ok` (accepted), `bu_r2`, `bu_r3` (Unsat B `p` from `AddObligation`'s Unstated rule); `bu_l4` (accepted); `bu_e1`/`e2` (TypeMismatch_Kd, not this path); `f5`, `c2`, `gate8`, `p1_call_positions`, `f1_ok`, `g56_opaque_ok`, `gate1`-`3`, the let analogues; **(B-4)** `b4_fn1_exprbody_ff` (`Functions.cs:138`: Unprov B `x`, node `x`), `b4_pr4_getter_ff` (`Properties.cs:474`: Unprov B `self.other`, node the body), `b4_pr5_getter_ok`, `b4_fn2_exprbody_body` (accepted), `b4_rp11`/`b4_rp12` (Unprov B at the join), `bu_rp2` (Unprov B at the join), `bu_rp1` (a join twin, as T11), `bu_rp3`, `bu_rp8`, `bu_rp10` (accepted), `bu_rp6`, `bu_rp9` (TypeMismatch_Kd, not this path), `b4_el2_key_body` (Unsat A5 `x@ref` + NoApplicableOverload_Kd, unchanged), `b4_el3_key_ok` (NoApplicableOverload_Kd only); G432's forms (Unsupported_Kd / UnprovContract, G432's U1) | — | as listed | same | none |

**Counts (26 rows, (B-4) and (N-a) included): acceptance move 1** (G1: 4 probes); **new error on a valid program 0; lost independent record 0; none 25.** The N-a row's InternalInvariant_Kd is pre-existing and unchanged; it is not new.
Revision 0's lost-record acceptances (fn2 and fn3 by B-1, r1_raw_ff by B-3) do not arise. Expected snapshot differences are `code`
(one record fewer per twin; G2's Unsupported_Kd becomes A5's record; T12's added record), `location` (A5's statement sites move to the
value; L2, L4 and L6 move from A5 to Binding at the same or the initializer's location) and `acceptance` (G1 only).

**Not observed; step-0 checks** (any acceptance move among them blocks, C-35 (a)):
- (R1) rows now formed at premise-proven body-chain positions inside a failing or regional fit, as at calls (today's sub-walks skipped
  such a component). These can add A5 Loan records.
- (R2) regional rows at D2's former `FitsTypeAt`-first sites when the walk fits; no form found.
- (R3) Binding's fixed-fixed judgment replacing A5's at L2-L4, L6, under premises and Semantics cases.
- (R4) a true Unbound end at a call, which goes to a row (`JudgeCallPosition`) while a fit's goes to Binding (D1 (1)); no form found.
- The corpus `IsTwinRecord` dump (OQ5, a gate).

## 2. Callers of the judge and their fate (B-3; (B-4): each site's `use`, failing `node` and `at`)

**(B-4)** `CheckTypeUse(actual, expected, use, node, at, out failure)`:
- `use` is the walk's and the judge's use. It gives the premises and resolves the ends, as each site's `FitsTypeAt`/`CheckTypeUse`
  passes it today, and it is the rows' `Use`.
- `node` is the node the judge fails with its records. It is the node the caller passes to `FailMismatch`/`RecordMismatch`, the node
  `RecordOriginFitFailure` failed today.
- `at` is the records' location and the rows' `Site`.

A caller passes the same `node` to the judge and to its `FailMismatch`. So `RecordComparisonFacts` always finds a `Position` failure's
node already failed, and D3's `InternalInvariant_Kd` rule is reached only by a real contract break (a recording path that bypassed the
judge). Two rows (marked "input") are operation inputs, where an Origin relation fails the input value, not the operation, as at a
selected call (`JudgeCallPosition`'s records fail the argument and the call proceeds; SPEC 10.1, 15.6.1). There `node` is the input,
and a `Position`-only failure does not stop the caller.

| Caller | `use` / `node` / `at` | Today on false | After H11b | Probe (dev today → after) |
| --- | --- | --- | --- | --- |
| `Variables.cs:95` (let annotation) | variable / variable / initializer | `FailMismatch(variable, initializer)` | same | f8: Unprov A5 `let` → Unprov B, node variable, at `b` |
| `Elements.cs:34` (Dictionary key), **input** | `source.Right` / **`source.Right`** / `source.Right` | `FailMismatch(source, source.Right)`, then return | a non-position failure: same. **A `Position`-only failure (the key already failed by the judge): go on to `TryBindIndexer`** | `b4_el1_key_ff`: NoApplicableOverload_Kd B `d[b]` + Unprov A5 `b` → the same NoApplicableOverload_Kd + Unprov B `b` (node: the key). With `node = source` the lookup would stop and lose that independent record |
| `ValueCalls.cs:490` (value-call input), **input** | `Source` / `Source` / `Source` | Shape and no erasure: `NoApplicableCandidate`; else `RecordMismatch` + `CompleteDependent(call, Source)` | the same for non-position failures. **A `Position`-only failure: the loop goes on to the next input** | `b4_vc3_two_inputs_ff` (`h(b, c)`): Unprov A5 `b`, `c` → Unprov B `b`, `c`. Stopping would lose `c`'s independent record. `v1` (L4) likewise |
| `ObjectViews.cs:229` | conversion / conversion / conversion | `IsCondition ? RecordMismatch : Fail(InvalidAssignment)` | **`Kind == Shape ? Fail(InvalidAssignment) : RecordMismatch(conversion, …)`**: an Origin failure is never a wrong-kind error | none found (step 0) |
| `Conversions.cs:314` | conversion / conversion / conversion | `IsCondition ? RecordMismatch : Fail(BorrowFailure)` | `BorrowFailure` only for `!fits`, `!IsSubtype` or a `Shape` failure; otherwise `RecordMismatch(conversion, …)` | none found (`cv1`-`cv3` are not valid syntax; step 0) |
| `FunctionTypes.cs:327` | node / node / node | true or a non-condition: `return false` (the enclosing fit records) | unchanged: a Function root forms no position (D6), so the judge records nothing here | `bu_fn2`: UnprovContract B `g` (unchanged) |
| `Conversions.Raw.cs:73` | `conversion.Left` / **conversion** / `conversion.Left` | result discarded | **on false: `return FailMismatch(conversion, conversion.Left, …)`**; the conversion fails | `bu_r1_raw_ff`: Unprov A5 `p` → Unprov B `p`, node the conversion |
| `LocalRegions.Types.cs:142` | value / value / value | result discarded | unchanged: every position is a row or skipped. A false result is `InternalInvariant_Kd` (a broken contract) | — |
| `ControlFlow.cs:335` (join) | node / node / node | regional: `CheckTypeUse`, else `FitsTypeAt`; the first failed source goes to `FailMismatch(node, node)` | `CheckTypeUse` for every source. Each source's fixed-fixed chains are judged and recorded on the join; the first failed source's non-position facts go through `FailMismatch(node, node)` | `j1`, `b4_rp11`, `b4_rp12`: Unprov B at the `if` (unchanged); `j2` (T12), `b4_rp13` (T13) |
| `Expressions.cs:585` (return) | jump / jump / `jump.Expression ?? jump` | `FitsTypeAt`, `CheckLocalTypeUse(…, jump.Expression ?? jump)`, `FailMismatch(jump, …)` | `CheckTypeUse`, same handling. The regional rows' `Use` moves from the value to `jump` (OQ6's step 0) | `f1_bad`, `bu_u1`: node `return …` as today |
| `Expressions.cs:632` (`RequireType`) | node / node / node | fallback, then `FailMismatch(node, node)` | same | `bu_cf1`/`cf2` guards |
| `Enums.cs:271` (construction) | use / use / use | fallback, then `RecordMismatch(use, use)` | same | step 0 |
| `Enums.cs:515` (payload) | source / source / source | `FitsTypeAt` or the fallback, then `RecordMismatch(source, source)` | same | `f7_payload_bad` (T2) |
| `Operators.cs:316` (assignment) | binary / binary / `binary.Right` | fallback (`CheckLocalTypeUse` at `binary.Right`), `FailMismatch(binary, binary.Right)` | same. The regional rows' `Use` moves to `binary` (OQ6) | `gate4`, `f10`, `bu_as1` |
| `Operators.cs:127` (update), `:272` (compound destination) | unary / unary / `unary.Operand`; binary / binary / `binary.Left` | `FailMismatch(unary, unary.Operand)`; `FailMismatch(binary, binary.Left)` | same | step 0 |
| `ClosureResults.cs:149` | node / `node.Parent is JumpKoto jump ? jump : node` / node | fallback, `FailMismatch(jump or node, node)` | same | `k1` (T7) |
| `Functions.cs:138` (expression body) | function / **expression** / expression | `FailMismatch(expression, expression)` | same (`node` ≠ `use`) | `b4_fn1_exprbody_ff` (= `f5`): Unprov B `x`, node `x`, unchanged |
| `Properties.cs:474` (accessor expression body) | syntax / **body** / body | `Shape ? Fail(body, TypeMismatch) : RecordMismatch(body, body)` | same (`node` ≠ `use`) | `b4_pr4_getter_ff`: Unprov B `self.other`, node the body, unchanged; `b4_pr5_getter_ok` accepted |
| `ArithmeticOperators.cs:152` | binary / binary / binary | `FailMismatch(binary, binary)` | same | step 0 |
| **(B-4)** `ResultPropagation.cs:557-581` (a join's bound part at the Target Result Type) | node / **source** / source | the trial `FitsTypeAt(…, node)` (`:557`), then `TryPropagate`, then `FailMismatch(source, source, …, fit)` with the trial's fact (`:581`) | **The trial and `TryPropagate` stay first and pure. When both fail and the part is recorded (`!awaits && !rests`, not an erasure), the record is the judge: `CheckTypeUse(Sources[i], type, node, source, source)`, then `FailMismatch(source, source, …, failure)` and `RestOn(source)`.** So the judge, not the trial's fact or a re-search, decides the part's record | no probe reached `:581`: rp1-rp13 record at the join through `ControlFlow.cs:335` (`rests` is then set). Step 0 adds a probe if one exists; by construction the judge fails `source`, the node `FailMismatch` names |
| Pure: `ResultPropagation.cs:589, 664` (`TryPropagate`, also `PropagateBound`'s trial `:703`), `:885`, `:894` (`LateFits`, `LateResultType`: late-Type predicates), `ArgumentOperations.cs:299-317` (alternatives), `ArgumentKinds.Fit.cs:179` (pre-selection), `Enums.cs:267` (a structure-failed arm: `Shape`), `IsSubtypeAt` | — / — / — | no record of their own | keep `FitsTypeAt`. Their false leads to another attempt, or to a record through a judged row above. `rp7` (N-a) is the one known exception, a pre-existing join record without a fact | — |

Generated-gate axes come from this table (B-3; C-35 (a)). For each caller (let, assignment, compound assignment, return, join, payload,
Dictionary key, value-call input, raw and object-view conversion, accessor result, `?` propagation), the axes are: end {body-local
Borrow, inferred `var`, enclosing atom, parameter, `static`, Unbound} × {plain, mixed, after a Function component} × {premises, a
Semantics case condition} × the valid and invalid twin. **(B-4)** Each axis asserts that no `InternalInvariant_Kd` appears that dev
lacks.

## 3. Mechanism summary and G432

- **Binding keeps** each fixed-fixed position's judgment and record, with code and location unchanged at a mixed fit. It also keeps
  Unbound-end records (D1 (1)), declared clauses, call positions and Function components' whole-contract conditions.
- **A5 forms** a row for:
  - every body-chain position of a fit the root walk fails, or of a regional fit;
  - every pending body chain;
  - every body-chain clause.
  It forms none for a fixed-fixed position. No `Mir/**` change.
- **Removed:**
  - `RecordOriginFitFailure`, the gate, `OriginFitSearch.FailedOriginRelation` and `OriginFitMode`;
  - `CheckLocalTypeUse`, and `CheckTypeUseCore`'s per-component sub-walks;
  - `originTwins`, `IsTwinRecord` and MirSide's filter;
  - `RecordOriginChains`' rows result, the pending path's `!refuted` and the clause's twin branch;
  - `RelationAt`'s local-annotation rule (D5).
  **Added:** `BindingObligation.Site`.
- **Comments** (H11-design §15; **N-5**):
  - `OriginFits.cs:7-21, 43, 81-86, 127-131`, `OriginProof.cs:348-354`, `OriginClauses.cs:17, 259-260`;
  - `ValueFits.cs:7-19` (the contract header), `:64-68`, `:82-84`, `:105-107`;
  - `CallRelations.Records.cs:13`, `ClosureResults.cs:12`, `TypeRelations.Origins.cs:136`, `Diagnostics.cs:421-428, 450-455, 482`,
    `LocalRegions.Types.cs:9`;
  - `MirTargetSide.cs:13, 39-40`, and the test comments of H11-design §15.
  The `OriginFits.cs` and `ValueFits.cs` entry contracts are restated (AGENTS).
- **Size:** about −110 src: `RecordOriginFitFailure` 40, `FailedOriginRelation` 80, twins 15, the gate and the fallback 10, the
  sub-walks 6; the judge's split +25, `Site` +4, callers ±12. Tests about ±240, docs about ±15. `Binding.OriginFits.cs` goes from 388
  to about 250 lines, `Binding.ValueFits.cs` from 159 to about 185. No limit grows.
- **G432: two units, H11b first** (as revision 0, and C-35 (3)).
  - G432 is Full (its review B3); joining it would make H11b Full.
  - Its U1 extends only D6/D7's step, over the root walk's kept facts that D3 preserves.
  - Its B2 (the call path) stays G432's.
  - G216 (a): H11b comes before U4 (M's U4-d B1). U3a/U3b change no per-position judge.
  - G is told that `FailedOriginRelation`'s removal moved from U2.2 to H11b (board, 18:32).

## 4. Class, the owner, verification

- **Class: Owner** (unchanged). BU's same reviewer re-checks this revision (findings only) before C1. R4 reviews the implementation
  (C-35 (3)).
- **The owner is asked to approve (acceptance only, Q13), (N-1):** "A non-call fit whose structure holds and whose only unproven Origin
  positions are body chains is judged by A5 instead of a located Unsupported_Kd.
  - Valid programs of that form are accepted (SPEC 15.6.5: a chain with a finite end that is not Refuted is Proven; 15.6.1). An example
    is assigning a parameter's reference, or another live local's borrow, to a local whose Type names a body-local Borrow
    (`var s: ref/i32 during y = y`, then `s = a`, `s = z@ref`, `s = (a, 2)`). Their `let` forms are already accepted.
  - The invalid twins stay rejected, now with A5's Loan conflict instead of Unsupported_Kd: a write to `z` while `s` holds `z@ref`, and
    `s = z@ref` with `z` in an inner block and `s` read after it."
  Record changes are record selection, which R5 decides (Q13).
- **Verification:**
  - **Step 0, before C1**, reported to BU and R4 (OQ5 is a gate):
    - the corpus `IsTwinRecord` dump: a twin node whose A5 side has no record of the same failure stops C2;
    - OQ6's `Use`/`Site` check, as N-3 extends it;
    - C-35 (a)-(d) as checks: (a) equal verdicts on L1-L7 and §2's axes; (b) no A5 chain through a fixed-fixed row loses an edge, and
      no new dependent A5 record where Binding now records (T3 excepted by name, OQ7); (c) C-26's conditions; (d) no `Mir/**` change;
    - §1's R1-R4;
    - the Unbound case (N-1);
    - `ResultPropagation`'s record path.
  - **Unit, per commit:**
    - the classes of §5's files, plus `MirOriginsTest`, `OriginRowsTest`, `VerifiedSpecExampleTest`, `DiagnosticCatalogTest`,
      `ArchitectureRulesTest`, and the `LocalRegion*`/`ElementAccess*` classes by grep;
    - the four `*ScalingTest` classes, `HirColumnsTest`, `MirCensusTest`, `SemanticSnapshotTest` check, `-Trait Purpose=Allocation`;
    - the slnx build.
  - **Session:** natives, `-DiagnosticSnapshot -DiagnosticAllowed code,location,acceptance,text,attribution,order`, and the semantic
    snapshot. Each difference is classified by §1's rows, with an acceptance move only in G1.
  - **(N-7) The generated gate runs before R4's implementation review**, dev against head, over §2's axes plus G216 (a)'s
    `gen-origin.py` twins. It blocks on: an invalid twin accepted, a valid program newly rejected, or a fixed-fixed record moved off
    Binding at a call.
  - Pinned timing of the fit path from one folder: one walk fewer per enumerated component, rows at more positions.
  - No mir ledger (R5-M188). RP17 is met: R4 reviews the `MirTargetSide`/`Mir*Test` hunks.

## 5. Commits, documents, DECISIONS

| Commit | Content | Phase (Q12) |
| --- | --- | --- |
| C1 (bp) | `BindingObligation.Site` (set to `Use`; `RelationAt` reads it); the `at` parameter threaded into `CheckTypeUse` and `CheckLocalTypeUse` (not yet read by a decision); `Conversions.Raw.cs:73` recording on false and `ObjectViews.cs:229`/`Conversions.cs:314` mapping by `Shape` (no change on dev: `CheckTypeUse` returns false there only for `Shape` or a condition today). No behavior change (snapshot 0). The sites' switch to `CheckTypeUse` is C2's (it changes rows) | H11b-i (≈ 45 min) |
| C2 | D1, D3-D7; MirSide's filter; pins in `MirTargetSide.cs`, BorrowedDefaultTest, CallOriginRelationTest, CallableOriginForwardingTest(.Conversions), ClosureEnvironmentOriginTest, ClosureResultOriginTest(.Makers), GenericStorageEmissionTest, LocalOriginClauseTest, MirBorrowsTest.Causes, MirOriginsTest, OriginRelationDiagnosticTest(.ContextualParts, .FailedClauses, .Twins), OriginRowsTest.Boundary, RawStorageTest, ResultPremiseTest, SharedObjectOwnershipTest, UniqSliceTest, `tests/diagnostics/spec-cases.json` :152, :154; D8's rows; DECISIONS | H11b-ii, -iii (src, then pins; one commit) |
| C3 (doc) | DIAGNOSTICS :164: the gate sentence, the twin sentences, "Pending (G56)", A5 judging body-chain rows only, a let's fixed-fixed position now Binding's, the Site field. R4-M155's Limit sentence stays for G432. STATUS :24, :49 (G1's boundary; G56's Opaque limit gone). ARCH: the stage ledger and `:232-236`. PLAN at landing: G206 (6) restated for the new step (R4-M118 site (1), "the fit judge"; **N-4**) and OQ4's open issue | landing |

**DECISIONS:** one R5 entry, numbered with the next free R5-M at commit time (≥ R5-M194). It records:
- the per-position rule and the one judge (D1-D3), with its result rule and `relationScratch`'s rule;
- the Unbound case;
- the `Site` field (D5);
- the twins' end (D4);
- `fncomp` (D6);
- G432's step (D7);
- the named acceptance move, with the owner's words;
- BU's consent.

Lines on other entries:
- "Superseded in part by R5-M19x (H11b)" on R4-M108: its "Rows go by node, not by record…" sentence, its H11 list (done except "the
  first differing Function Type component", G432's), and **(N-6)** its Opaque-limit and Conditions (Site) bullets;
- "Ended by" on R4-M111 (whole);
- "Ended by" on R4-M155's Staged `OriginFitSearch` item, worded as C-35 (2) asks;
- a pointer on R4-M113's Site sentence to D5;
- **(N-6)** "Ended by" on R5-M188's departure item "`IsTwinRecord` stays to H11";
- **(N-6)** on R4-M94's "non-call rows only where Binding records a failure until H11" and its OQ-B3;
- **(N-6)** R5-M192's H11b scope, marked done.

## 6. Open questions for R5 (revision 1)

| # | Question | State |
| --- | --- | --- |
| OQ1 | The judge covers `CheckTypeUse`'s existing callers (§2's table) | Ruled yes (§7), with C-35 (a)-(d) as step 0's checks (§4). BU: YES on conditions, met by §2 |
| OQ2 | **Blocking (owner):** §4's G1 text, now with gate9 and the invalid twins | asked by R5 |
| OQ3 | R4-M155's "Ended by" line | consented (C-35 (2)) |
| OQ4 | The meet decomposition's second copy (calls against fits) | PLAN open issue at landing (§7). The judge reuses `FailedOriginPart`, adding no third copy |
| OQ5 | The corpus `IsTwinRecord` dump | step 0, **a gate** (BU) |
| OQ6 | D5's `Use`/`Site` | decided as a field (D5, N-3). Step 0 checks that every row's `Use` keeps today's ends, premises and case mask |
| OQ7 | **(N-2)** T3's added A5 Loan record at `local = 2` against C-35 (b) | Non-blocking. It lies outside (b)'s scope: (b) is about fixed-fixed positions now recorded by Binding, and T3 is a body chain. SPEC 15.6.5's own example expects it (`spec-cases.json:154`, Dependent). R5 asks M to exempt it by name |
| OQ8 | (R4) A true Unbound end at a call becomes a row (`JudgeCallPosition` counts it a body chain), while a fit's stays Binding's record (D1 (1)) | Non-blocking. No such form was found (`u4_call_unbound` names a call binder). If step 0 finds one losing its record, it is a PLAN open issue for the call path, not H11b's |
| OQ10 | **(N-a)** `rp7`/`rp4`: a join whose awaiting part misfits records `InternalInvariant_Kd` on dev. **Classification:** pre-existing and not caused by H11b; H11b does not change it (its path is not touched). The cause: `BindWaitedSources` returns the misfit without its fit's fact (`ResultPropagation.cs:607`; `TryPropagate`'s `FitsTypeAt(…, out _)`, `:664`), and the join records it with its own unset `failure` (`ControlFlow.cs:347-351`), which `RecordMismatch` maps to `Internal` (`failure.Kind == None`). That is the same family as B-4 (a record that does not come from the judging fit's fact) but a different site: the awaiting branch and the join's own record, which records nothing in the part today. Fix direction: the awaiting part's late fit is judged and recorded at the part, and the join never records a fact-less misfit. **R5 opens the PLAN issue**; not in H11b | for R5 |
| OQ9 | §9: the owner's answer on a record site's owner | under (1) or (2), D5 stands; under (3), R5 re-checks D5 before C1 |

## 7. R5's rulings (2026-11-05)

| # | Ruling |
| --- | --- |
| OQ1 | **Yes** (one judge for one fact): D1 covers `CheckTypeUse`'s existing callers, so a let's, a conversion's, an element's and a value-call input's fixed-fixed position becomes Binding's record and its rows move to the value. Conditional on R4's confirmation (C-26's condition) and BU's consent; both asked on the board (C-34, C-35). |
| OQ2 | R5 asks the owner now (non-blocking for the reviews): the named acceptance move G1 (the fit-failure gate's valid forms, accepted because A5 proves their rows, as their `let` analogues already are on dev). |
| OQ3 | R4's consent for R4-M155's "Ended by" line, asked with C-35. |
| OQ4 | Not H11b's: a PLAN open issue (the meet decomposition's second copy) in H11b's landing PLAN commit. |
| OQ5, OQ6 | The implementer's step 0 (the corpus `IsTwinRecord` dump; `OriginAtUse`'s ends for a local annotation), before C1. |
| Review | Owner class: one independent design review, by BU's reviewer (C1: BU approves the design), then R4's implementation review. |

## 8. R4's answer, board C-35 (2026-11-05); BU's design review runs as C-36

- (1) **OQ1 YES** under C-26's conditions plus: (a) the verdicts are equal: on §1's L1-L3 and the gate's let, conversion, element and
  value-call-input axes every program keeps its acceptance (Binding's Judged proof replaces A5's fixed-fixed oracle there); any
  acceptance move there blocks (record moves are Q13's, verdict moves are not); (b) no A5 chain loses an edge: a census of A5 chains
  through a fixed-fixed row at those callers, and no new dependent A5 record where Binding now records the fixed-fixed failure
  (SPEC 23.1.2); (c) C-26's conditions (code and location kept; Sites per R4-M113 with OQ6's step-0 check; every moved record classified
  under Q13); (d) no `Mir/**` change; if step 0 finds one, it comes to R4 as a consent. These are step 0's and the snapshot classes'
  checks.
- (2) YES to "Ended by R5-M19x (H11b)" on R4-M155's Staged item (word it: H11b deletes `OriginFitSearch.FailedOriginRelation` with its
  Function-component stop, met in H11b instead of G216 (a) U2.2; G is told), the pointer line on R4-M113's Site sentence to D5,
  "Superseded in part" on R4-M108 and "Ended by" on R4-M111.
- (3) R4 reviews H11b's implementation. G432 stays R4's separate Full unit after H11b (its U1 extends only D6/D7's step; G432's B2 stays
  G432's).

## 9. G's note (C-37 addendum, 2026-11-05): who owns a record's site

M's review 2 of G216 (a) U4 found that a record's site has no single owner (Binding's record site, A5's row use per R4-M113, H11b's D5
`at`, G216 (a)'s rule 6). G asks the owner: (1) drop U4 and keep the payload sites (G's recommendation), (2) a cross-phase Full change
after H11b (one published report Site per position, decided once after the final solve), or (3) the owner fixes the contract. H11b's D5
follows R4-M113 as it stands; under (1) or (2) H11b proceeds unchanged; under (3) R5 re-checks D5 and OQ6 against the owner's contract
before H11b's C1.

## 10. Revision 1's answers to BU's review (C-36)

| Finding | Answer |
| --- | --- |
| B-1 | D1's result rule: the result is false when a non-position fact remains. D6's skip never makes the result true. D3: the judge records its own chains; `RecordComparisonFacts` takes the first non-position fact as the own record only while the node is unfailed, and every other condition as a further record. The enumeration has no sub-walk, so `comparisonFacts` survives. Census T5, T6 and guards fn2, fn3, lim1-lim3 |
| B-2 | `CheckTypeUse` is the one entry, and `:95` keeps the regional rule. There is one root walk and no walk-then-judge chain. Guards cf1, cf1_join_ok, cf2 and as1 keep their records |
| B-3 | §2's table (every caller and its fate: Raw records on false; ObjectViews and Conversions map by `Shape`; LocalRegions' false is a broken contract). `relationScratch`'s rule is in D3. §2's axes; census rows L4, L6, L7, T11, T12 |
| N-1 | gate9 is moved to G1 and `g1_inner_scope_borrow` to G2 (pinned; both in the G1 text). The Unbound case is D1 (1), and step 0 traces it. u1-u4 are guards: `nope` is a signature Origin, so u1 stays a fixed-fixed record and u2 a body chain (T10) |
| N-2 | OQ7 |
| N-3 | D5: the `Site` field; OQ6's step 0 covers clause siblings, Closure values and `BodyCaseMask` |
| N-4 | D3: the whole `OriginFitMode` goes, `UnrefutedCause`/`FailedOriginEnvironment` are named, and PLAN G206 (6) is restated at landing (§5) |
| N-5 | §3's comment list |
| N-6 | §5's DECISIONS lines; D7 keeps "the first differing Function Type component" with G432 |
| N-7 | §4: the generated gate runs before R4's implementation review |
| N-8 | Kept (D8) |

## 10. R5's rulings on revision 1 (2026-11-05)

- B-1, B-2, B-3 fixed as ruled; the census (22 rows) keeps one named acceptance move (G1, now with gate9) and its invalid twins in G2
  (A5's ComparisonLoanConflict_Kd), no new error on a valid program, no lost independent record. The owner's G1 question stands as asked
  (gate9 is the same form).
- N-3's new field `BindingObligation.Site` is accepted as the representation of a row's report location (one field, `Site ?? Use`,
  read by `Binding.OriginRows` into `OriginRow.Site`; R4-M113): R5's decision, recorded in H11b's DECISIONS entry in C1. If the owner
  takes option (3) of G's site-ownership question (§9), R5 re-checks this field against the owner's contract before C1.
- R1-R4 and the corpus dump are step 0's gates, as written.
- (G, C-37 closed, 2026-11-05 19:28) The owner dropped G216 (a) U4 (verbatim 「U4 を落とす」), i.e. option (1) of §9: H11b proceeds
  unchanged; D-1's argument site is a later item, designed with H11b's D5 / `BindingObligation.Site` and R4-M113 when taken up.

## 11. The owner's approval (2026-11-05, R5's dialog)

Verbatim 「H11b の受理の変化 G1　承認します。」: G1 (the fit-failure gate's valid forms: assignment into a borrow-typed local, gate4,
gate5, gate7, gate9; accepted because A5 proves their rows; their invalid twins G2 get A5's ComparisonLoanConflict_Kd) is a named
acceptance move under Q13. H11b's DECISIONS entry cites it.

## 12. Addition from H10b-2's review (R4, N2, 2026-11-05)

`StructuralCompletion.CollectEvaluationChildren` (`:28`) and `UnionTransfers` (`:41`) are `internal static` only for the deleted
control-flow analysis; their readers are now in their own file. H11b makes them private (BU's file; BU's C-36 consent covers the Binding
hunks), or records why not.

## 13. Revision 2's answers to BU's re-check 1

| Finding | Answer |
| --- | --- |
| B-4 | §2: `use`/`node`/`at` per caller. The judge fails the caller's `FailMismatch` node, so D3's `InternalInvariant_Kd` rule cannot be reached on an invalid program. Probes: `Functions.cs:138` (`b4_fn1_exprbody_ff`), `Properties.cs:474` (`b4_pr4_getter_ff`), the Raw row (`bu_r1_raw_ff`) and `Elements.cs:34` (`b4_el1_key_ff`) each keep one record on the named node. Operation inputs (`Elements.cs:34`, `ValueCalls.cs:490`) fail the input and go on, as at calls (`b4_el1`, `b4_vc3`: no independent record lost). `ResultPropagation.cs:557-581` records through the judge after its trial and `TryPropagate`. No probe reaches `:581` (rp1-rp13), so step 0 adds one if it exists |
| N-a | Census row N-a and OQ10: pre-existing, unchanged by H11b, the same family at another site; R5 opens the PLAN issue |

## 13. C-36 answered (BU, 2026-11-05): design approved (revision 2's B-4 line-check ACCEPT); Binding consent with conditions

- **C-1 (before C2):** at `ResultPropagation.cs:581`, call `FailMismatch` and `RestOn` only when the judge returns false; when it returns
  true the part counts as fitting (otherwise a body-chain misfit reaches InternalInvariant_Kd).
- **Step 0's gates are reported to BU (through R5) before C1.**
- BU reviews the implementation's Binding hunks; R4 reviews the implementation (C-35 (3)); the owner approved G1 (§11).

## 14. Step 0's OQ5 stop and R4's reading (2026-11-05; R4's trace `...\r4-notes\post-exit\h11b-a5-skip.md`)

- Cause: no A5 rule skips the body; the MIR builds none. An Origin clause failure fails the definition (`FailOriginClause`,
  `Binding.OriginDeclarations.Failures.cs:60-64`); the HIR publisher's `PartsPublished` publishes no part of an unresolved definition
  (DECISIONS R4-M18, "a rule of every unresolved function"); `MirBuilder.Published` builds no MirBody. Every MIR-only record of such a
  body (A1, A3, A4, A5's Loans) is lost on dev today, not only H11b's twin. R4-M18's reason holds for CaseLimit only; for InvalidOrigin
  the parts are bound with the signature's Types and only the premise oracle reads the failed contract.
- **R5's ruling: R4's option (c), as H11b's prerequisite unit H11b-0** (fix the cause in shared structure, AGENTS): the publisher
  publishes the parts of an unresolved definition whose failure the parts do not read, as a closed list on `PartsPublished` (a row per
  failure; the first row InvalidOrigin; CaseLimit and a failed signature Type stay out); the builder builds the body; A5 judges its rows
  with no new rule; a fixed source's Unknown verdict that reads the unavailable contract is Binding's dependence fact (BU-69's query),
  published by the view, and A5 marks the problem derived with a Binding cause (`MirFactKind.Cause`). DECISIONS supersedes R4-M18's
  rule. Under C1: R5 designs and implements the Binding part, BU approves and consents, R4 implements A5's derived mark on request.
  Rejected: (b), keeping Binding's twin for bodies of unresolved definitions (a second judge of body chains, and the A1/A3/A4 losses
  stay); (a) as framed (no MirBody to judge). H11b's C1 waits for H11b-0; H11b's design gains one line naming the prerequisite.
