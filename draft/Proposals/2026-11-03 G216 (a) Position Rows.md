# G216 (a) root fix: position rows (design package)

Status (2026-11-03): the design and its two SPEC readings were approved by the owner in this planning session's dialog
(verbatim 「推奨の内容でOKです。」), and the policy was fixed (verbatim 「その方針で確定します。」; 「着手時期: H5 後に開始」,
「担当: あなたが開始し、R4をレビュアーとして、他エージェントと協調して作業してほしい」, 「DECISIONS の番号の扱い：BU 番号」,
「U0 を H5 前に始めてよい」, 「「H5 後」は掃除完了後、です。」). **Placement decided (the owner, 2026-11-03, verbatim 「設計書はdraft/Proposalsに保存してください。健全性優先の計画でお願いします。」): option A of §9.** The [R7, R8 and R9 Implementation Plan](2026-11-03%20R7%20R8%20R9%20Implementation%20Plan.md), adopted the same day (DECISIONS R7-M1–R7-M3), had assigned G388, G390 and G391, revision B and B3a, and the files `Binding.ArgumentKinds*.cs` and `Binding.ResultPropagation*.cs` to the front session's stream I (I1 tests in phase B, I2 `InferCtxt` as a Full unit in phase C, I3 "positions through InferCtxt" in phase C); its revision 2 (plan §14) makes this design I3's content and U0 I1's G216 (a) part (§9). This file is a working proposal (INTEGRATED, 2026-11-03): a revision edits it in place and adds a line in INTEGRATED; nothing is frozen.

Working time (DECISIONS Q12): analysis and design in dialog 2026-11-03; this file 40 minutes. Base: dev `737514d54`. Paths: `C/` =
`src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`. No build, test or probe was run for this file; the code facts were read on
`origin/dev`.

## 1. The defect and what remains

G216 (a): a call must prove the well-formedness of every instantiated parameter Type (SPEC 15.6.4 step 3, 15.6.1). dev's economization
judged only positions where the fit replaced an Origin, so a part that took its Type from the call was judged by nobody. R4-M162 (the
strict form), R4-M164 (F1 `Contribution`, SolveLate's bounds over the instantiated parameter Type, `RequireChildPremises`, U4 "a Case
construction is a call") closed the direct, aggregate and construction forms. R4 exited under the owner's 90% rule (R4-M165); the
remainder is a known defect (STATUS "Contextual `.None` arguments"; PLAN G216's "Proposed common fix", G388–G392, G396 (6)):

| Group | Forms | State on dev |
| --- | --- | --- |
| Root join arguments | jt1, mj1, jt2, mj2, kd, dn2, lj, ly | accepted (unsound); a U2j-a prototype passes the root rows |
| Nested join arguments | nn, nd (G390) | accepted (unsound); waited for B3c, which is held with revision B |
| Result-position children | rb, mk2, c3a (G388) | accepted (unsound); Result is per argument, so a child position is skipped whole |
| Record sites (U3) | js2–js5, dn2p, same9_n, swap3, dl1 | rejected, recorded at the payload; the owner's reading 「混合のままでよい」 |
| Shared judgments | G391 (two owners of "which slots evidence binds"), G392 (an annotated local's omitted Origin) | rows pinned |

Held, evidence only, never merged: revision B (`held/bu104-specb-A` 5ee090855), B3a (`held/bu111-b3a-A` ea0187d75), U3a
(`held/u22-u3a-fix` 630f3d6d4), U3b-1 (WIP 12a8d560b), the U2j-a dev prototype (R4 notes `g216a-u22-impl\u2ja-dev-design.md`).
Revision 5's per-position open regions (R4 notes `g216a-r3\redesign-proposal-r5-20261029.md`) are superseded by F1: Signature
positions are judged at the final solution, so nothing needs opening; U7 is F1's definition (a Signature position is no evidence).

## 2. The one remaining cause

"What is this position to this call" is computed three times, per argument, at three moments, by three owners, and never published
per position:

- `Contribution` is one value per argument (`SlotSolution.Contributions[i]`, written by `Contribute`): a child position cannot differ
  from its argument (G388).
- `PositionKind` is recomputed by every walk (evidence, defaults, `IndependentPositionType`, `TakeWaiting`, `RequireChildPremises`) and
  stored nowhere; revision B's RC1/RC2 (the position's common-Type formation decided in two places) is this.
- `PartContribution` re-reads a part's producer record at judgment time; a join bound by completion turns `KindOf` Typed, so the fact
  "this join is classified by its call" is gone when `JudgeSelectedCall` runs (U2j-a asked for a published fact, U2j-b for B3c).
- Which input fixed each Origin (caller, another input's equality, the solve) is in the solve but attached to no position (U3's sites,
  G391).

U2j-a, U2j-b, G388, U3 and G391 are one missing representation: the selected solution's table of argument positions.

## 3. Rules, in SPEC's words

1. **Position is the unit.** "Who gave this position its Type" is a property of a Type position of the instantiated parameter, not of a
   syntax part and not of a `BoundType` (interned). SPEC 10.5.1 classifies parts at positions; 14.9.2 step 2 forms a Type part by part.
2. **A call judges every position under its final solution** (15.6.4 step 3). Evidence (an independently typable part) is well formed where
   it was formed (15.6.1), so only a position the fit replaced can fail there; a Signature position (the callee's instantiated Type gives
   it) is judged at every position; a Result position is the expected result's contributor's.
3. **Result is per position's sub-Type (approved reading 1).** The exemption of 15.6.4 step 3 through 10.3 covers only a position whose
   instantiated sub-Type *is* a slot the expected result solved; a layer the callee's pattern adds around that slot is the call's.
4. **Membership is structure (approved reading 2).** A control-flow expression at an argument position supplies child positions by
   14.9.2 step 2's structure (a scalar one position; a Tuple by element; an array by index, the owner's 「(I) 添字ごと」), root or nested,
   whether or not the join waited or Binding's position waiting bound it first; each position's kind decides Evidence or Signature. A
   position that is Typed with a Type is Evidence and its sources are not entered (10.5.1 "independently typable").
5. **One formation.** 14.9.2 step 2's common Type at a position has one implementation (`PositionType(start, end)`), shared by `let` joins
   and argument joins; the survey path goes.
6. **Record site from the solve** (D-1 and 「混合のままでよい」): a Type or length slot records at the argument whoever fixes it; an Origin
   records at the argument only where the call's solve opens it (the leaf part related); an Origin fixed by another input's equality
   records at the part's own fit (S3). The rule covers well-formedness rows; a fit relation keeps DIAGNOSTICS' site, the result's fit
   at the call (BU-51's pin): when a parameter's row is a bound the solve satisfies and the result's fit then fails, the record is the
   call's (revision 2, 2026-11-03: U0's re-review R1).
7. **A form the rule does not cover is a located `Unsupported_Kd` at the argument**, never a clause or a case.

### 3.1 Reading 1: Result per position (mk2 invalid, id valid)

```kimi
func mk2<T>(w: Option<ref/T during static>) -> T => $abort("example")

func use(q: ref/i32) -> ()
    let y: ref/i32 during q = mk2(.None)     // invalid: q does not outlive static
```

`.None` waits; no evidence binds `T`; the annotation (the expected result) solves `T = ref/i32 during q` (10.3). The instantiated
parameter is `Option<ref/(ref/i32 during q) during static>`; 15.6.1 needs `q outlives static`. dev marks the whole argument Result
(the parameter *mentions* a solved slot) and the call skips it: accepted, unsound. The row design: the position of `T` is Result (the
annotation proved `ref/i32 during q`); the layer `ref/T during static` is Signature and the call judges `q outlives static`. Both ends
are fixed Origins (a parameter Origin and `static`), so the relation is Unknown under Finality, not Refuted (15.6.5; DIAGNOSTICS "Origin
relations": only a finite Origin required to outlive a fixed one is Refuted): `UnprovenOriginRelation_Kd` at `.None` (revision 1,
2026-11-03: the first text said Unsatisfied; U0's review B1).

```kimi
func id<T>(v: T) -> T => v@move

func use2(q: ref/i32) -> ()
    let y: Option<ref/i32 during q> = id(.None)   // valid: the position's sub-Type is the solved slot itself
```

The annotation proved `Option<ref/i32 during q>` at its own occurrence; the call adds no layer and proves nothing more (q10, i01_n:
one record only, at the contributor).

### 3.2 Reading 2: membership is structure (jt1; a Tuple join)

```kimi
func k(q: ref/i32, r: ref/i32, c: bool) -> ref/i32 during r
    func inner(p: Option<ref/(ref/i32 during q) during r>, x: ref/i32 during q) -> ref/i32 during r => x
    return inner(if c => .None else => .None, q)   // invalid without `origin q outlives r`
```

`inner`'s body assumes its signature well formed (15.6.1), so the caller proves `q outlives r`. The argument is a control-flow expression
at an argument position; both sources wait, so the position is Waiting (Signature); the selected candidate gives it `p`'s Type and the
call judges it: `UnprovenOriginRelation_Kd` at the argument. On dev the membership fact lives in `ClassifiedByCall`, the Join record and
`WaitingResult`, decided during the probe and gone after completion (`KindOf` Typed), so `JudgeSelectedCall` cannot enter the join
and the bound join's Type is a copy of the parameter: accepted, unsound. A nested join (nn, nd) never gets a record.

```kimi
func k2(q: ref/i32, r: ref/i32, c: bool) -> ref/i32 during r
    func inner2(p: (ref/i32 during q, Option<ref/(ref/i32 during q) during r>), x: ref/i32 during q) -> ref/i32 during r => x
    return inner2(if c => (q, .None) else => (q, .None), q)   // invalid; the record is index 1's, at the argument
```

Formed part by part (10.5.1 paragraph 2, 14.9.2 step 2): index 0 holds `q` twice, Typed, Evidence, nothing replaced; index 1 holds
`.None` twice, Waiting, Signature, judged. On dev the whole join is bound and Typed because index 0 is typed, and index 1 disappears.
A join whose every source is typed with one common Type is one Typed position, Evidence, judged where the join was formed.

## 4. Representation: position rows

The selected candidate's solution publishes once, after selection and before its final solve, one row per argument position of the
call, child positions included (Tuple elements, array indices, nested aggregates, join sources root and nested, Case payloads of a
construction, defaults). Rows are index-keyed (argument index, parent row, child index), pooled on the solution like
`CallEvaluation.Contributions` (`ScratchBuffers`), live through `JudgeSelectedCall`, and are no Koto-keyed table. One writer per column.

| Column | Content | Writer | SPEC |
| --- | --- | --- | --- |
| Parts | the parts at this position: the root part and, for the record, the first leaf part | the final classification walk (the position composition, once) | 10.5.1, 14.9.2 step 2 |
| Kind | Typed (with or without a Type), Literal, Aggregate, Waiting | the same walk, `PositionKind` at the final solution | 10.5.1's three kinds |
| ParameterType | the instantiated parameter sub-Type at this position | the same walk | 15.6.4 step 2 |
| Contribution | Evidence or Signature; a row whose pattern is a slot is its solution's contributor's: the premise visits take the pattern beside the instantiated Type and stop at the slot, a layer's stored premise ranging over the solution (revision 3, 2026-11-05: U2's design; no Result value, no slot set) | the same walk, from Kind | 10.3, 15.6.4 step 3 |
| Decided | per Origin and slot of the sub-Type: caller-fixed, fixed by another input's equality, opened by the solve | the solve (`MatchInputOrigins` records the fixing position once) | 15.3.6 |

Readers read rows only: `JudgeSelectedCall` (every Signature row judged under the final solution, the premise visits stopping at a slot pattern, revision 3), `SolveLate` (Signature rows' `ParameterType` as bounds), `CompletePayloads` (a construction is a call since U4), the record sites
(§3 rule 6 from Decided), `CollectFitBounds`. Candidate trials keep computing `PositionKind` per candidate and persist nothing. Under
R7-I2 (`InferCtxt`, `SlotSolution` a view), the rows stay the selected solution's table and Decided reads the variable store's binding
source; the design does not depend on which I2 candidate is chosen.

## 5. Code

| Component (file) | Change |
| --- | --- |
| Position composition (`C/Binding/Binding.ArgumentKinds.Positions.cs`) | the one final walk writes rows; `ComposedJoin` enters a control-flow part by structure (rule 4), its `WaitingResult` clause and the Join-record test go; `PositionKind` keeps typed-first and loses its join-only rows |
| `SlotSolution` (`Binding.ArgumentKinds.Solution.cs`, `.Evidence.cs`) | rows replace `Contributions[i]`; the Result encoding goes, a slot pattern being the premise visits' stop (revision 3). Revision 8: `Contribute` writes the root rows and `PartContribution` stays as the one rule "a part's Contribution from its recorded kind" (its callers: the root rows, the payload rows, the child rows, the value call's rows, and the late evidence pass for a leaf, which runs before the rows exist). Decided is not published (U4 dropped, revision 7) |
| Judgment (`Binding.CallRelations.cs`, `Binding.ResultPremises.cs`) | `JudgeSelectedCall` iterates rows; the whole-argument Result guard and `RequireChildPremises`' own recursion go; `RequireInputPremises` and `CollectInputPremises` take the pattern beside the instantiated Type and stop at a slot pattern, a layer's stored premise ranging over the slot's solution (revision 3) |
| Constructions (`Binding.Enums.cs`) | `SolveConstructionOrigins`' duplicate premise loop and the construction's derivation of evidence-bound slots go (G391); the construction reads rows |
| Argument kinds (`Binding.ArgumentKinds.cs`) | `IsArgumentJoin`, `IsProbeJoin` go (gone since U1.6). **Revision 8 (2026-11-05, U6's design review 1, B1): `ClassifiedByCall`, `ConflictWaits` and the Join record stay**: the record is a fact of its own, "a waiting join its call classifies position by position" (P3/P4, an unbound-core lifetime), which differs from the Waiting outcome (BU-87) in five states; the rows replaced its membership readers only (U3). `PartKind` Join/Call/Case/Reference/Function stay as the producer records of waiting parts (10.5.1 Waiting); `AwaitsCall` unchanged |
| Result propagation (`Binding.ResultPropagation*.cs`) | keeps position waiting (Deferred, AwaitsFor, BU-87's `Awaiting`) only; argument joins no longer decide membership there; `PositionType` is the one formation (rule 5) through `SelectCommonType`, which compares the sources' structural parts and forms region Origins (SPEC 14.9.2 step 2; U6b). Revision 8: the survey (`ResultSurvey`) stays: it classifies a control-flow part's sources before binding them and forms no Type |
| Shared relation judge (`Binding.OriginProof.cs`, `Binding.OriginClasses.cs`) | an annotated local's omitted Origin is a region of its body (G392: cx2q Refuted, as A5 gives) |
| Ratchets (`ArchitectureRulesTest`, RP6) | zero-reference rows for the names that left the code: `IsArgumentJoin`, `IsProbeJoin`, `ContributionAt`, `Contribution.Result`, `CommonBorrowResult` (revision 8: `ClassifiedByCall`, `ConflictWaits`, `PartContribution` and `SolveConstructionOrigins` stay) |

Kept: `OpenResultOnlyOrigins`, `OpenKnownOrigins`, `HasOpenOrigin` (existing open regions); the fit-failed guard and `HasCallRelation`
(one record per failed input, SPEC 23.1.2); rule W (BU-79). Not done: revision 5's per-position open regions; any new mark on `BoundType`;
a per-syntax-part fact table; a second completion path beside the rows.

## 6. Units (each phase within one hour, Q12; one review per unit, Q13; the oracle and the generated gate before every review, RP14)

| Unit | Content | Fail-first rows | Done when | Class |
| --- | --- | --- | --- | --- |
| U0 | the oracle (§7): the generated family with rule-predicted verdicts; the 13 known forms, U3's rows, G388–G392's rows, revision B's R2-1/R2-2 rows, each with its twin; the planning session checks the predictions against §3 | — | every prediction that differs from dev names a row of §1 | tests; a neutral author |
| U1 | position rows on the solution, written by the final walk (Parts, Kind, ParameterType, Contribution); root rows replace `Contributions` | — | behavior unchanged (bp triple); Bind and warm allocation measured (RP8) | Major, bp |
| U2 | the premise visits stop at a slot pattern (Result per sub-Type) and leaf rows are `SolveLate`'s bounds; the Result value and the whole-argument guard go (`artifacts/verify/g216a-oracle/U2-design.md`, revision 3) | rb, mk2 (s3p bare and Tuple), the s6/s7 Tuple site rows; c3a if it passes | the Result twins valid; one record at the contributor for q10/i01_n; the Tuple site at the call | Major |
| U3 | joins as rows by structure, root and nested: a candidate's child rows are composed once at its solve and retained (`Index`; `Type` the reader's), the judgment reads them wherever a root has child rows, and a composite beneath a slot composes with a null pattern (the sixth amendment's twin; M's N8); value calls get rows; the membership readers of the completion state retired (`artifacts/verify/g216a-oracle/U3-design.md`, revision 4); in two parts, U3a (the retained composition, the judgment by rows, value calls: the join rows) and U3b (beneath a slot: the N8 family and a generated slot family), one review each (revision 5) | jt1, mj1, jt2, mj2, kd, dn2, lj, ly, nn, nd; the N8 family | the `_ok` twins valid; g046 and g122 valid; B3a/B3c and revision B obsolete | Major (Full if the owner asks two candidates) |
| U4 | the solve publishes Decided; sites derived (rule 6); constructions read rows. **Dropped by the owner (revision 7, 2026-11-05, verbatim 「U4 を落とす」)**: after R5's H11b the one record of a payload's failed fit is A5's row at the payload consumer's use, so rule 6's argument site (D-1) needs a cross-phase report Site; the site stays DIAGNOSTICS' failed node and D-1's argument site is deferred to its own item after H11b (PLAN G216); Decided is not published (`PremiseSolved` stays as BU-99 has it); the rows that predicted the argument site are kept tagged `deferred` | js2–js5, dn2p, same9_n (deferred); dl1 a guard | — | — |
| U5 | the shared judge: an annotated local's omitted Origin is a body region. **Closed without an implementation (revision 6, 2026-11-05)**: since the flip (R6-check H5) the MIR decides, cx2q/cx2e/cx2c are pinned Unsatisfied on dev and g1, g2, g5 give Unsatisfied on both pipelines; the Binding judge's classification belongs to the old analysis, removed with it | cx2q, cx2e, cx2c (dev pins), g392_g1, g392_g2, g392_g5 (guards) | held by rows | — |
| U6 | **revision 8**: U6b, one formation (rule 5: `SelectCommonType` compares structure and forms region Origins; `CommonBorrowResult` folded), with a generated rule-5 family first; and the closing records (STATUS's boundary row rewritten, the ratchet rows for names that left, the proposal's closure, the SPEC 10.5.1 gap reported to the owner: 10.5.1's last paragraph already states the structure, reading 2 adds explicitness on nested joins and "whether or not the join waited"). The retirements of the Join record, its producers and `PartContribution` are withdrawn (§5). **Revision 9 (2026-11-05, the owner, verbatim 「G216 (a) を締めくくる で決定します」): U6 is not implemented; rule 5's array case is PLAN's open row for the join formation's owner (U6's design review 2: three mechanisms, not one); the closing records are documentation at the lane's end** | q20 (rule 5); the rule-5 family (rows revision 16) | generated family, library, milestones and SPEC examples 0 moved; ratchet rows at 0 | Major |

Mapping to the R7 plan's stream I (§9): U0 is I1's G216 part; U1–U6 are I3 (positions), with U5 added; I2's parts d (join states as
constraints) and i (the waited-join state deleted) read this design, since membership becomes structural.

## 7. The oracle (U0)

- **Axes:** contribution of the part at the leaf {typed value, literal, `.None`, `.Some(x)`, nested call, anonymous function, `[]`,
  default} × composition {bare, Tuple element, array index, nested aggregate, `if` join root, `match` join, labeled join, join at an
  element, join in a join} × Origin solution of the sub-Type {caller-fixed, equality from another input, opened by the solve, Type
  slot, expected result, annotation} × call form {named, property, value call, Case construction} × twin {valid, invalid}.
- **Predicted verdict, by rule, not by dev:** (1) kinds per 10.5.1 at each leaf; (2) positions per 14.9.2 step 2's structure (§3 rule 4);
  (3) Contribution per row (§3 rules 2–3); (4) obligations: for each Signature row, 15.6.1's well-formedness of `ParameterType` under
  the solution, the solution being the meet of the upper bounds (15.3.6); Refuted → `UnsatisfiedOriginRelation_Kd`, Unknown →
  `UnprovenOriginRelation_Kd`; (5) the site per §3 rule 6. A form the procedure cannot classify is predicted `Unsupported_Kd`.
- **Generator:** extends R4's `gen-gate.py` (R4 notes `g216a-g1a\gen-gate.py`, 843 programs) with the prediction column; runs on dev
  and on head through the CLI; a difference table by class (dev-vs-prediction, head-vs-prediction, dev-vs-head).
- **Acceptance:** head equals the prediction on every row; every dev-vs-head move is in §1's list or a named new finding; the library,
  the 44 milestones, the SPEC and IMPL examples and the Benchmark corpus move 0 programs; the mir ledger while it exists (until H10a).
- **Existing rows to fold in:** dev's `T/OriginRelationDiagnosticTest.{ContextualParts,JoinArguments,ArgumentCompletion,NestedSignatures,
  CallRelation,FailedFits}.cs` and `ResultPremiseTest*.cs`; the T2 branch `claude/a5-g216a-t2-tests` (ed294aeb2) holds the rows kept
  apart for U2.2: jt1, mj1, jt2, mj2, kd, dn2, dn2p, js2–js5 and their twins, fn0, t7c, the b5/b6/b7/pb1 families.
- **Cost rows (RP8):** the census harness cold and warm, Hello and Milestone26; `InferenceSlotScalingTest` and the four `*ScalingTest`
  classes; warm allocation 0 B.

## 8. Stop and process rules

- Cost: RP8 (DECISIONS R7-M3): Bind with publication B/A′ > 1.03 outside the A′/A spread, or warm allocation growth, stops for the owner.
- Convergence: a review finding on a form the oracle does not predict adds the row to the oracle first; no interim predicate is
  written for a form outside the oracle (a located `Unsupported_Kd` instead). Q11 stands.
- Prototype first (RP13): U1's rows are prototyped on dev and measured before the design is frozen.
- Owners: the design and U0's rows by a session that implements nothing (this one); implementation by the Binding owner's lane; one
  review per unit by the middle (R4), the owner's choice; DECISIONS entries under BU numbers with the Binding owner's consent; holds on
  the board before the first edit; every landing through a window.

## 9. Placement: the overlap with the R7 plan, and the options for the owner

What the plan already says: I0 absorbs revision B and B3a into I2; I1 (phase B, after H5) writes the problem statement and tests,
G388/G390/G391 included; I2 (phase C) is `InferCtxt` as a Full unit with two candidates, owning `Binding.ArgumentKinds*.cs` and
`Binding.ResultPropagation*.cs`, with part d "join states as constraints with one `Awaiting` fact" and part i "the waited-join state
deleted (B3c)"; I3 (phase C, after I2) fixes G388, G390 ("after B3c"), G391, G378 and G356 one rule per unit. The arrangement
(R7-M2) has three sessions; the front owns Binding; the middle runs the windows and the board.

What this design adds: one representation (rows) for I3's rules, which also removes B3c and I2's part i (membership is structural),
the oracle for I1's G216 part, U5 (G392), and the two approved SPEC readings. What it changes in the plan if adopted as written in this
dialog: a fourth session leading Binding units; I1's G216 part in phase A (the owner's 「U0 を H5 前に始めてよい」); I3 before or beside I2.

| Option | Order | For | Against |
| --- | --- | --- | --- |
| **A (recommended): soundness first** | U0 now (I1's G216 part, phase A); U1–U6 after the flip group's sweep, on today's `SlotSolution`, before I2; I2 then keeps the rows as a view over `InferCtxt` | closes 13 unsound acceptances weeks earlier; the rows are a table on the solution and survive I2; I2's parts d and i shrink | one more unit changing the solve's shared files before I2 (§3.3's "one at a time" holds since I2 has not started); U1's writer is re-pointed when I2 lands |
| B: the plan's order | I1 (phase B) → I2 (phase C) → I3 (phase C) with this design as I3's design and I1's oracle | no plan amendment; one state change (I2) before the rows | the unsound acceptances stay until phase C; I3's "after B3c" prerequisite must still be replaced by rule 4 |

Roles under either option: this session writes the design and U0 (no Binding edit); the front's lane implements; R4 (the middle)
reviews each unit (the owner's choice, one review); the Binding owner consents to the contract changes and gives the BU numbers; holds
and consents on the board; landings in the middle's windows. If the owner prefers this session to run an implementer on the front's
files, RP17 applies (a board hold, and the files' owner reviews), which conflicts with "R4 reviews"; the owner decides which.

### 9.1 Decision (the owner, 2026-11-03)

Verbatim 「設計書はdraft/Proposalsに保存してください。健全性優先の計画でお願いします。」: **option A**. The R7 plan's revision 2 (its §14)
records the amendments:

- I1: its G216 (a) part is U0 of this proposal, written in phase A (now) by the G216 planning session (this one), tests only; the
  rest of I1 keeps phase B.
- I3: its content is this proposal's U1–U6 (position rows), landing after the flip group's sweep and before I2 (RP5 holds: U2–U6
  change acceptance only after H5); G378 and G356 follow as I3's remaining rules; the prerequisite "after B3c" is replaced by §3
  rule 4.
- I2: parts d (join states as constraints) and i (the waited-join state deleted) read this proposal; i needs no B3c; I2 keeps the
  rows as a view over `InferCtxt`.
- Roles: the front's lane implements U1–U6 (its files); the middle (R4) reviews each unit, one review (the owner's choice); the
  Binding owner consents to the contract changes and gives the BU numbers; holds and consents on the board; landings in the
  middle's windows. This session writes the design and U0 and implements nothing in Binding.
- Evidence: revision B, B3a, U3a, U3b-1 and the U2j prototype stay unmerged.

## 10. Not in scope; evidence

Not in scope: G216 (b) and G215 (the middle, O3 with the middle's A5 rows); G389 (h4_n, A5 region rows); G375's per-use re-proof
(V1); G273; A5 causes. Evidence: `artifacts/handoff/2026-11-03-r4-pause/r4-notes/g216a-*` (design, probes, gates, reviews),
`C:\Users\bwff1\AppData\Local\Temp\claude\bu\g216a-u22\design.md` (Rule C, F1–F3), `artifacts/handoff/2026-10-28-r4-pause/r4-notes/
g216a-r3/owner-analysis-20261028.md` (the owner's four root causes), PLAN G216's row and G388–G392, STATUS "Contextual `.None`
arguments", DECISIONS R4-M137, R4-M162, R4-M164, R4-M165, BU-79, BU-87.
