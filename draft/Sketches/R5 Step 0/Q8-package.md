# Q8 package: the R6-check prerequisite design

Date: 2026-10-18, round 2. Status: working record of R5 step 0 (full process). It compares design 1
(`deep-dives/q5-r6check-prereqs.md`, "d1") and design 2 (`Q8-design-2.md`, "d2") and states the design the planning session
decided (`Q8-decisions.md`: open points 1–8 and design review round 1, which govern), for the round-2 design review and then
the owner once (OQ10, DECISIONS R5-M1). Not authority.

Base: code at `origin/dev` = `a1c87dcd` (Q6, Q6b and R4's MIR lane landed); R4's core record at `origin/claude/a5-1b-core` =
`8961f577`. Paths are under `src/Kimi/Compiler/` unless they start with `src/`, `tests/`, `docs/` or `draft/`. "(H)" marks an
inference not checked by running code. "Probe" = the throwaway runs of §12 (tests in detached worktrees, never committed);
"review probe" = the reviewer's evidence in `artifacts/verify/r5-evidence/q8-review/` (main checkout). Plan = the R5 and R6
Implementation Plan; PLAN10, CFA (`Analysis/ControlFlowAnalysis.cs`), DEC, ARCH as in the plan.

Governs (OQ10): Q7, the lint group G:L (Q9–Q12), Q16's Binding views, Q17, H1, the new units H1a and Q21 and the validator
rule (Q8-3); PD29, PD31, PD32; the dispositions of `Binding.ClosureInstances` and `Binding.RequirementInstances`. Q13 is closed
(r5-open-findings).

## 0. Decisions in one page

| # | Decision | § |
| --- | --- | --- |
| D1 | `OwnershipRequirement` (the 18 names of `OwnershipFailure`, same order and values) in `src/Kimi/Diagnostics/DiagnosticRequirement.cs` replaces `OwnershipFailure`; the old analysis uses it as its failure kind | 3.1 |
| D2 | One Binding component, **HIR lints**: a 12-row table, one Final walk at the end of `Bind(Final)`, one pooled record list, reported by `ReportDiagnostics`. Rows 1–11 come from the walk, row 12 from the end-arrival check of result validation, which the walk calls. `UnsafeFunctionValue_Kd` gets no row unless Q10's reproducers find an uncovered form, which then becomes lint row 13 | 3.2 |
| D3 | PD31's form: requirement `Binding` (Kind 0), condition = row number, never a node failure; `Koto.ReportDerived` gains `condition`; the old analysis reads `Binding.LintErrors` where it read `flow.Issues` | 3.2 |
| D4 | The jump binding writes the jump's target into its `PlanRow.Plan`, null when the transfer is invalid; the publisher stops resolving targets; the walk records rows 9–10 by the same rule. HIR contract: a null target row means Binding recorded row 9 or 10 there (ask A-8) | 3.3 |
| D5 | FunctionFallthrough is result validation (PD32): `EndArrival` beside `AddBodyResult`, called once per function and accessor by the walk; one guess rule; MIR records nothing at that end (probe), so no MIR dependent exists | 3.4 |
| D6 | Q16: two views, a reassignability helper (a `Binding` static in a new partial, replacing two copies) and the Tuple-element position as `PlanRow.Folded`; no constructor view (`AdtDef.IsConstructor` exists) | 3.5 |
| D7 | **Q17 departs from the plan row**: each MIR recording rule names its requirement (`MirProblem.Requirement`); `MirReport.Add` checks that merged problems agree; no code → requirement table (revised ask A-5). This restores PLAN10 §7 | 3.6 |
| D8 | H1 judges TransferRequired on the **published columns** by a linear scan inside the Final pass, after an eager publication (behavior-preserving unit H1a, refining R2b-D2); order producers → publish → H1 → counting → lint walk; a failed node triggers one republication, which creates no new subject (asserted). Sources: consumed children (S1) and unconsumed Places (S2), both excluding Places rooted in a `HirRes.PreparedParam` name, and inferred captures (S3, reported by `ReportCaptureEntry`'s omitted-list form). The record carries the failing Semantics cases from `BareAcquisition`'s mask | 3.7 |
| D9 | **Q21 (new; G121 applied before H1)**: a node failure does not hide the children Binding resolved independently: a closure whose creation failed keeps its body published and built, and a failed compound Place keeps its operands published (in place). Changes R4-M61's node-failure contract | 3.8 |
| D10 | **Validator rule (Q8-3)**: `MirValidator` checks that a `Copy` operand's Type is Copy, by Binding's published proof under the body definition's premises; `PairAcquire` and per-view `Adapt` operands are exempt; shadow in Q14, lands in G:R6-check-flip | 3.9 |
| D11 | `Binding.ClosureInstances` and `Binding.RequirementInstances` are re-disposed from "delete R6" to R7 | 3.10 |

## 1. Required meaning and design chain

| Rule (SPEC) | Forms | Judge today | Owner after Q8 |
| --- | --- | --- | --- |
| 17.5, 17.5.1–17.5.3, 23.1.3 (`ExplicitDiscard`, `PropagateFailure`) | `DiscardedTail`, `DiscardedResult`, `UnusedTrySuccess`, `DiscardedValue` | CFA :641-656, :426-576 | HIR lints (Binding P5) |
| 17.5 ¶3, 22.10.4; 14.9.3 last ¶ | `OwningWriteArgument`; `StaticWhileTrue` | CFA :895-904; :1449-1452 | HIR lints |
| 14.3, 5, 4.3.4, 23.1.3 (`RemoveUnsafe`) | `UnsafeBlockRequired`, `UnnecessaryUnsafeBlock` | CFA :679-688, :849-863, :1085-1123 | HIR lints |
| 7.6 | `UnsafeFunctionValue` | Binding node failure at 4 sites, and CFA :658-677 again | Binding only |
| 14.5.2, 14.4 | `InvalidJumpTarget`, `UnlabeledYieldTarget`, `OverlappingLabel` | CFA :1279-1301, :1511-1526 | HIR lints; target fact by the jump binding |
| 14.9.1–14.9.3 | `FunctionFallthrough` | CFA :1230-1252 | Binding result validation (PD32) |
| 3.5, 15.1.5, 14.2.2, 14.2.4, 7.7.2 | `TransferRequired`: a bare Place never Moves; no list ⇒ each capture Copy | Binding (7 sites) + old analysis (10 sites) | Binding (H1) |
| 7.2.3 | `DefaultArgumentMove`: a default never moves a preceding argument | old analysis | old analysis until H5, then A1 for PreparedInput roots (R4's 1c) |
| 23.1.2; DIAGNOSTICS rule 1, §4.1 ("Check: requirement and condition number (independent of the code)", `DIAGNOSTICS.md:64`), §4.3 (`:106`, `:107`, `:152`, `:166`) | independent errors; one requirement, one phase; the documented records | all | all |

Chain (AGENTS "Design before code"):

- **Lints.** Meaning: 12 checks over syntax and Binding facts (none reads `Op`, `Adjust` or `Completion`; d1 §1.1). Lacking:
  an owner after R6-check (ARCH:15 names "HIR lints" in P2–P5s). Owner: Binding. Unnecessary: CFA's lint half, its
  `ReadsBinding` list (:275-277), priority code (:450-467), the `ControlFlow` requirement on these records.
- **Jump targets.** Meaning: the lexical target (14.5.2), one pure rule (`KotoHelper.ResolveTransferTarget`,
  `Parsing/Koto/Common/ControlFlowContext.cs:13`). Wrongly allowed: the publisher resolves it (`Binding.Hir.cs:98-102`) against
  its own header ("Must not resolve again", `:6`). Owner: the jump binding (`Binding.Expressions.cs:716-757`). Unnecessary:
  the publisher's resolution; MIR's located `Unsupported_Kd` at an invalid jump (A-8).
- **Requirement of a MIR problem.** Meaning: the check identity is the requirement the deciding check names, never looked up
  from the code (PLAN10 §7, line 499). Lacking: `MirProblem` carries none (R4-M3a; `Mir/Analysis/MirReport.cs:22`). Owner:
  each recording rule. Unnecessary: a code → requirement table, its fact selectors, a MirReport derivation.
- **TransferRequired.** Meaning: SPEC 3.5. Wrongly allowed: MIR moves or Copies a bare non-Copy Place with no problem (probe
  p1–p4, p7, p8). Owner: Binding (`BareAcquire`, "the one bare-acquisition rule", ARCH:92). Lacking besides: a node failure
  hides independently resolved children (G121), and nothing checks that a `Copy` operand is Copy. Unnecessary: the 10 old
  sites, `OwnershipRequirement.TransferRequired`, A1's `StaticMovePathRequired_Kd` dependents (derived by construction).

## 2. Decision table

| # | Topic | Design 1 | Design 2 | Decided | Reason |
| --- | --- | --- | --- | --- | --- |
| 1 | Ownership requirement kinds | Diagnostics enum, same 18 names | same (§2.1) | agree | names are already public (`DiagnosticRequirement.tinyhand:590-641`) |
| 2 | Lint component shape | P5 walk, units L1–L4 | one component, 12-row table | d2's table; the walk reaches Binding's synthesized calls through call plans (d1's point) | one record path |
| 3 | Lint records | owner question | PD31 reuse, condition = row, non-failing | d2 | PD31; the problem key excludes the code (`Diagnostics/DiagnosticFact.cs:26`) |
| 4 | Walk placement | after `ReportDiagnostics`' Final block | end of `Bind(Final)` | d2 | `Analyze` reads `LintErrors`; test helpers run Bind → CheckStartup → Analyze without `ReportDiagnostics` (`tests/xUnitTest/Tests/MinimalEmissionTest.cs:324-333`) |
| 5 | Rows 9–11 | at the jump binding | at the jump and label bindings | in the walk, by the pure rule (open point 5) | one record source; no tentative records; CFA's coverage of bodies Binding does not bind |
| 6 | Jump target fact | HIR contract only | jump binding writes `PlanRow.Plan` | d2, null for both invalid forms | publisher header; one contract |
| 7 | `UnsafeFunctionValue` | delete CFA's copy if covered, else move | no row; else a fifth node failure | no row; an uncovered form becomes lint row 13 (review) | `IsValueUse` (`Binding.Expressions.cs:142-143`) is CFA's walk (:661-667); probe p5, p6; a lint row keeps PD29/PD31 |
| 8 | FunctionFallthrough | beside `AddBodyResult`; `a1-result` unchanged | same, once from the walk; one guess rule | d2's placement, d1's MIR fact | probe p9, p10: MIR records nothing at the end arrival (R4-M21) |
| 9 | Guess query of CFA's remainder | Binding's, CFA calls it | — | d1 (open point 7) | no second copy until H10 |
| 10 | Q16 views | mutability, Tuple ordinal, literal bytes | stored field, `Folded`, conditional constructor view | reassignability helper (computed, open point 6; a `Binding` static in a new partial, review); `Folded`; literal bytes by `HirOp.Literal`; no constructor view | no state, one rule, no `BindingModel.cs` edit (R5-M7) |
| 11 | Q17 | derivation in `MirReport` (R4-M3a) | recording rule names it | d2 (open point 1) | PLAN10 §7 |
| 12 | Q13 | extract an enumeration | closed | closed | `Consume` already is it; 2,620 children written twice (findings) |
| 13 | H1's subject source | the extracted enumeration | scan after eager publication | scan (open point 3) | §3.7 |
| 14 | H1's subject set | consumed children | S1 + S2 + S3 | S1–S3, S2 on the columns (open point 4), S1/S2 without PreparedParam roots (Q8-1a) | §3.7 |
| 15 | H1 timing | owner question | with H5 (PD29) | PD29; H1a any time after G2 (open point 8) | a node failure without the flip makes the old analysis skip the program |
| 16 | Dispositions | R5 views + R7 | R7 | R7 | definition-time callers |

Design review round 1 (Q8-decisions): Q8-1a PreparedParam exclusion; Q8-1b case fact; Q8-1c S3's record; Q8-1d Q14's
comparison; Q8-2 Q21; Q8-3 the validator rule; and the adopted non-blocking items, each placed in §3, §6 and §7 below.

## 3. Components and contracts

### 3.1 Q7: `OwnershipRequirement`

As d2 §2.1: `public enum OwnershipRequirement : byte`, the 18 members in `OwnershipFailure`'s order
(`Analysis/OwnershipModel.cs:139-176`), so every Kind, name and tinyhand row stays (bp);
`DiagnosticRequirement.Ownership(OwnershipRequirement)` (`DiagnosticRequirement.cs:42`), `All` and `NameOf` switch to it;
`OwnershipFailure` deleted, `OwnershipIssue.Failure` typed by it; every reference renamed (the unit re-measures: d1 78 + ≈ 52,
d2 85 + 51). Invariant: one kind enum per partition. Prohibition: no Analysis-side copy, no mapping switch, no member added
(PD21). Later: H1 removes `TransferRequired`; H10/H11 remove the members whose last producer they delete. Lands before R4's
1c names requirements (open point 2). Hold: adv on `DiagnosticRequirement.cs` and `MirDifferenceLedger.cs` (R5-M7).

### 3.2 HIR lints (Q9, Q10, Q11's rows, Q12's call)

**Contract** (entry file `Binding/Binding.Lints.cs`; a second file by responsibility if the unsafe rows need it):

| Item | Content |
| --- | --- |
| Responsibility | P5 "HIR lints" (ARCH:15): the rows below, judged once per Final pass as non-failing Binding records, with repairs and their derivation from Binding failures and parser recoveries |
| Inputs | The Final pass's bound tree and facts: Types, adaptations, call plans (`CallPlan.Of(node, i)`), Copy proofs, `InfersUnit`, `ResultStructure()`, `FailureCauses` (H1's failures included), recovery causes, directive selection |
| Outputs | `lints`: a pooled `List<(Koto Node, byte Row, Koto? Root)>` cleared in `ResetPass`; `LintErrors` (rows whose catalog severity is Error); records published by `ReportDiagnostics` at Final, next to the alias, pattern and position warnings (`Binding.cs:144-160`) |
| Walk | Last step of `Bind(Final)`: producers → publish → H1 → counting → lint walk (§3.7). One walk of the source modules over evaluated positions (Types, attributes, Origin clauses, effect items and constraints skipped as CFA :761-830; excluded tests and unselected directive groups as today). A node's synthesized evaluation (getter, setter, range value, view range, key resolution, arithmetic and comparison calls, formatting; CFA :689-760) is reached through the node's call plans and their synthesized argument sources, never through a copy of CFA's switch. Walk state: the innermost Unsafe Block's used and uncertain bits, the function boundary |
| Invariants | Never fails a node. One row per code; condition = row number; a derived Error keeps its row's condition. Warm rebind 0 B: no Koto-keyed set or dictionary (ARCH "Syntax identity"); duplicates and the 17.5 priority resolved by sorting `lints` by (SyntaxId, priority) at report time |
| Prohibitions | No inference or proof re-run to explain a record; no lint of library bodies; no record from a Provisional or tentative binding; no node failure |

**The table** (replaces CFA's `ReadsBinding` :275-277, the priorities :450-467 and `GuessedBy`'s code list :392):

| Row | Code | Sev. | Judged from | Derived from `FailureCauses` (a warning records nothing) | Rests on a guess | 17.5 priority |
| ---: | --- | --- | --- | --- | --- | ---: |
| 1 | `DiscardedTail_Kd` | W | `do`/`if`/`match` whose Unit Binding inferred | yes | yes (descent root) | 1 |
| 2 | `DiscardedResult_Kd` | W | Discard Context occurrence (`!KotoHelper.IsValueContext`, as CFA :641) | yes | no | 2 |
| 3 | `UnusedTrySuccess_Kd` | W | same | yes | no | 3 |
| 4 | `DiscardedValue_Kd` | W | same, `IsEffectFree` | yes | no | 4 |
| 5 | `OwningWriteArgument_Kd` | W | `WriterWrite` call | yes | no | — |
| 6 | `StaticWhileTrue_Kd` | W | syntax | no | no | — |
| 7 | `UnsafeBlockRequired_Kd` | E | each call plan's target, `Deref`, `NoInit`, pointer arithmetic (one `RequiresUnsafeContext` rule) | no | no | — |
| 8 | `UnnecessaryUnsafeBlock_Kd` | W | block end; unknown need counts as use | yes | no | — |
| 9 | `InvalidJumpTarget_Kd` | E | the transfer-validity rule (§3.3) | no | yes (unlabeled jump's target) | — |
| 10 | `UnlabeledYieldTarget_Kd` | E | same rule | no | yes | — |
| 11 | `OverlappingLabel_Kd` | E | ancestor labels (CFA :1511-1526 moved) | no | yes (label) | — |
| 12 | `FunctionFallthrough_Kd` | E | `EndArrival` (§3.4), called by the walk | no | yes | — |

`UnsafeFunctionValue_Kd` has no row: Binding fails the node at its four sites. Q10 keeps the reproducers as tests (single
declaration; a group selected by function item, Function Type and requirement item; an unselected group, which fails
`Ambiguous`) and deletes CFA :658-677. A form still uncovered becomes lint row 13 (Error, non-failing, derived from
`FailureCauses`), not a node failure (PD29, PD31).

**Record form.** `node.Report(DiagnosticRequirement.Binding(BindingFailure.None), code, …, condition: row)`; a derived Error
`node.ReportDerived(…, keys, condition: row)` (`Parsing/Koto/Koto.cs:665` gains the optional `condition` that `Report` has at
`:627`). Existing Binding warnings use condition 0, so the rows never share a key with them. Acceptance reads the Binding
partition's error state as today (`src/Kimi/SolutionAndProject/Project.cs:510`).

**Guess rule.** `SuppliedCompletion` and `IsSuppliedBody` (CFA :398-424) move into Binding beside `StructuralCompletion`,
generalized as d2 §2.4 (a supplied body or an unresolved expression); CFA's structural `RequireFallthrough` calls that query
until H10.

**Transitional reads of the old analysis** (deleted at H10):

| Old read | After |
| --- | --- |
| `earlierRejected = flow.Issues.Count != 0 \|\| …` (`OwnershipAnalysis.cs:101`) | adds `\|\| binding.LintErrors != 0`, from Q10 (the first unit moving an Error row) |
| `verified = flow.Issues.Count == 0 && …` (`:116`) | same addition; acceptance unchanged |
| `flow.Reported(body, FunctionFallthrough_Kd)` (`:262-267`) | `binding.FallthroughKey(function)` (Q12) |
| `LegacyRecords` reads `ControlFlow.Issues` (`tests/xUnitTest/LegacyRecords.cs:28-31`) | also reads the lint Errors as Binding records |
| `ControlFlowAnalysis.Targets` (`OwnershipAnalysis.cs:1966`) | unchanged: CFA keeps calling the pure rule until H10 |

**DIAGNOSTICS** (Auto, contract change): Q10 rewrites §4.3 "Later phases" (`:106`) for the unsafe rows (owner Binding);
Q11 its jump and label sentences; Q12 its fallthrough sentences (`ControlFlowAnalysis.GuessedBy` and `.Reported` →
Binding's guess query and `FallthroughKey`).

Structure: `Binding.cs` (1,917 lines) is over the 5,000-token limit and may not grow; Q9's first commit moves the Final
non-failing-record report out of it (behavior-preserving).

### 3.3 Binding-owned jump targets (Q11)

| Item | Content |
| --- | --- |
| Rule | One function in `KotoHelper` decides validity: `ResolveTransferTarget` plus the unlabeled-`yield` clause now at CFA :1288 (`YieldKoto { Label: null }` targeting an `if`/`match` not in Value Context) |
| Fact | The jump binding (`Binding.Expressions.cs:716`) writes `HirTables.PlanFor(jump, valid).Plan = valid ? target : null` (an inline write; no forwarding property, R5-M9) |
| Records | The walk records row 9 or 10 from the same function, so bodies Binding does not bind keep CFA's coverage |
| Publisher | Deletes `Binding.Hir.cs:98-102` |
| HIR contract (ARCH "Binding facts for MIR") | "A published Jump or Continue has its target row; a null row means Binding recorded `InvalidJumpTarget_Kd` or `UnlabeledYieldTarget_Kd` there." Tested: every null target row has a row 9 or 10 record |
| MIR consumer | Ask A-8 (§9) |
| Other readers | Binding readers during binding (`Binding.ControlFlow.cs:490`, `StructuralCompletion.cs:280`, `Binding.ArgumentOperations.cs:395`, `Binding.Try.cs:20`, `Binding.Testing.cs:31`, `Binding.OriginDiagnostics.cs:151`) keep calling the rule |

Rejected: publishing the jump as `HirOp.Error` (its operand would be replaced); a node failure (the regression PD31 avoids);
a new column.

### 3.4 FunctionFallthrough (Q12, PD32)

As d2 §2.4: `EndArrival(owner)` beside `AddBodyResult` (`Binding.ControlFlow.cs`), for a function or accessor with a Block
body whose declared result (written, else bound contract; Unit for a discarding body or setter, as CFA's `DeclaredResult`) is
not Unit: if `ResultStructure()` says the body completes, the end-arrival Unit source cannot fit → row 12 at the body.
Called once per function and accessor by the walk. `FallthroughKey(function)` returns that record's key for the old derived
link until H10. MIR: nothing to derive (probe p9, p10: `return` with `_0` unset and no A1 record, R4-M21); ledger row
`a1-result` unchanged.

### 3.5 Q16 views (in W1)

The 17 sites (R5-M10) take existing facts: `HirOp.Continue` (`Mir/Build/MirBuilder.Control.cs:121`), `HirOp.Closure` or
`BoundClosure.Of` (`MirBuilder.cs:204, 462`, `MirBuilder.Places.cs:145`, `MirBuilder.Defaults.cs:341`), `HirOp.Group`
(`MirBuilder.Places.cs:41`), Type kind (`MirBuilder.Values.cs:377`), `HirOp.Literal` for the literal byte count
(`MirBuilder.Formatting.cs:15`), symbol kind and signature (`MirBuilder.cs:119-138`), `AdtDef.IsConstructor`
(`Binding/AdtDef.cs:134`, for `MirBuilder.cs:166`). Two views remain:

| View | Contract | Replaces |
| --- | --- | --- |
| V1 reassignability | `internal static bool Reassignable(BindingSymbol symbol) => symbol.MutableCapture \|\| IsMutableDeclaration(symbol.Declaration)` on `Binding`, in a new partial `Binding/Binding.Reassignable.cs` (SPEC 6.6, 14.6.1, 14.8.2; a static `var` Property is a `VariableKoto`); computed, no state; recorded in ARCH's "Binding facts for MIR" row with its reason (R4-M9 pattern) | `MirBuilder.Match.cs:167`, `MirBuilder.Expansions.cs:109`, `MirBuilder.Places.cs:175` (and `MirBuilder.cs:399` if Q16 counts it); the two existing copies (`Binding.Expressions.cs:114`, `ElementAccess.cs:176`) |
| V2 Tuple element position | The publisher writes `PlanRow.Folded` on a Tuple-element Field node, as it does for a ConstIndex (`Binding.Hir.cs:112-116`, R4-M61) | `MirBuilder.Places.cs:182` |

No `BindingModel.cs` edit (R5-M7); 0 B warm.

### 3.6 Q17: the requirement of a MIR problem (departure from the plan row)

| Item | Content |
| --- | --- |
| Representation | `MirProblem(DiagnosticCode Code, DiagnosticRequirement Requirement, SourceRef Subject, ulong Views)`; `DiagnosticRequirement` is a byte and a ushort (`DiagnosticRequirement.cs:17`), so the field fills existing padding (24 bytes stay 24, H) |
| Who names it | The recording rule: A1 (`MirInit.cs:61, 170, 236, 243, 278`) `StorageLimit`, `StaticMovePathRequired`, `PossiblyMovedUse`/`UninitializedUse`, `ReassignedLet`; A2 (`MirDrops.cs:294`) `PossiblyMovedUse`; A3 (`MirLints.cs:38`) `ControlFlow`; A4: the obligation row, which already carries its code from Binding (`Binding.EffectObligations.cs:14-15`), carries its requirement beside it (`EffectBound`, `CallableEffectBound`, `VirtualEffectBound`), and `Unsupported`; A5 (`MirBorrows*` on the core branch) `ComparisonLoanConflict`, `CallEffectConflict`, `DefaultArgumentAccess`, `DefaultArgumentBorrow`, `UnprovenOrigin`, `StorageLimit`, `Unsupported`; A6's PreparedInput mapping to `DefaultArgument*_Kd` (R4-M10, 1c) sets the matching requirement with the code it writes; construction records `ControlFlow` when H3 folds them in |
| Merge | Key unchanged (code, subject; G125 is R4's). `MirReport.Add` checks that merged problems agree on the requirement; a disagreement is a recording-rule defect, reported as R4 reports MIR contract breaks (`InternalInvariant_Kd`) |
| Old `Binding.*` names | The old path reports some A4/A5 decisions under `Binding.VirtualEffectBound`, `Binding.IncompatibleImplementation`, `Binding.OriginRelation` (`tests/xUnitTest/MirDifferenceLedger.cs:56-58, 69-70`); MIR names the Ownership requirement (DIAGNOSTICS rule 1), a public rename at H5 |
| Ledger (Q17) | The MIR side's requirement column is the problem's requirement (no longer "-", R4-M17); a pairing with different requirements is a `requirement` difference kind; `Compared` gains the MIR requirement per row and a test asserts every MIR (code, requirement) appears there (PLAN10 §7's consistency set), deleted with the ledger at H10 |
| Publication (H0) | The condition when two codes share one requirement at one subject, and the partition, stay H0's |

Fallback (only if R4 declines revised A-5): the plan's table with a selector for `UnsatisfiedEffectBound_Kd`,
`IncompatibleContractImplementation_Kd`, `UnsatisfiedOriginRelation_Kd`, `UnprovenOriginRelation_Kd` and `Unsupported_Kd`, which
needs A4's obligation kind and A5's stratum as new `MirFact` kinds (absent: `Anchor`, `Use`, `Store`, `Reservation`).

### 3.7 H1: TransferRequired in Binding, with H1a

**Subjects.** Every way the builder acquires a written Place:

| Source | What MIR builds today | Old judge | H1 |
| --- | --- | --- | --- |
| S1 consumed children | `Move` from an Adjust `(Move, _)` written by `Take`'s bare rule or the group erasure (`Binding.Hir.Adjust.cs:39, 169-199`); probe p4: `_2 = move _1`, no problem | 9 of the 10 sites | Op a value Op (not `Syntax`, `Error`); the node a Place (`IsPlace`: bare, raw or static; not a formatting plan's storage); Adjust `Move`; parent not `ConversionKoto { Transfer }` (the explicit Move writer, `:42`) |
| S2 unconsumed Places | An `Unset` Adjust on an evaluated Place (a statement item, a non-value tail: `Consume`'s `Result`, `:162`) is read "by case" (`Mir/Build/MirBuilder.Values.cs:148-151`), a `Copy` for a non-exclusive Type (`:229`) into a temp the statement drops (`MirBuilder.Drops.cs:72-80`); probe p1, p8: double drop, no problem | `OwnershipAnalysis.cs:833-836` via the statement path (`:926`) | Op a value Op with `PlaceOf` not `Unset`/`Value`; Adjust `Unset`; `BareAcquire(type) == Move` with a proof other than Error |
| S1, S2 exclusion (Q8-1a) | a default's bare read of a preceding parameter (`bad(x: string, y: string = x)`; review probe s01, s03) | `DefaultArgumentMove_Kd`, no repair | a subject whose Place is rooted (through parentheses and projections) in a `HirRes.PreparedParam` name is not H1's: `DefaultArgumentMove_Kd` stays with its producer, the old path until H5 and from H5 A1 for PreparedInput roots (R4's 1c). See open point 1 for S2 |
| S3 inferred captures | `CaptureAcquisition` never set for an omitted list (`Binding.Closures.cs:424-500`; default `Copy`, `BindingModel.cs:77-80`; pooled symbols reused without reset), so the builder Copies (`MirBuilder.Values.cs:386-398`); probe p2, p3, p7: `Closure(copy _)` of a `string`, a `uniq/i32`, a `T` | `OwnershipAnalysis.Closures.cs:44-61`, reported by its `ReportCapture` (`OwnershipAnalysis.cs:304-311`) | at the end of closure binding: a capture whose Type is not proven Copy fails the closure's creation with `TransferRequired` (SPEC 7.7.2 "no list: each must be Copy"); a proven one sets `Copy` explicitly. Defaults are included (DIAGNOSTICS:107) |
| Others | Pattern bindings (match plan, Binding-judged), `@move` (written), defaults' copies | — | Q14 confirms none decides an unwritten Move or a non-Copy Copy |

Binding's seven existing failures stay; they fail the consumer or source, so S1 never judges those nodes twice.

**Records.**

- S1 and S2 go through `FailAcquisition` (`Binding.Diagnostics.cs:1253`) and `ReportAcquisition`: located at the Place,
  repair by `TakeJudgment`. For a pair-layer Place, the failing Semantics cases come from `BareAcquisition`'s mask
  (`Binding.PairCases.cs:266`), as the explicit-entry form does (`FailingCaseNote`, `:62`), and the record carries them as the
  documented fact of DIAGNOSTICS `:166` (`TransferRequired_Kd` as `target:Text,case:Text`, the Note `Found under the Semantics
  case …`, one related binder declaration per named binding), rendered by the existing Semantics-case renderer (Q8-1b; review
  probe t01–t03). `ReportAcquisition` is Binding's one acquisition reporter, so its argument sites gain the same fact.
- S3 is reported by `ReportCaptureEntry` (`Binding.Diagnostics.cs:1276`), whose fact (`captureFailures`, an entry, Type and
  source) gains the omitted-list form (no entry). Its record: requirement `Binding.TransferRequired`, at the closure, evidence
  `[name]`, the Note `The omitted capture list captures {name} only by Copy; it never infers a Move, a borrow or a Reborrow`,
  plus `, and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)` when
  `DefaultParameters.InLaterDefault(closure, source)` (DIAGNOSTICS `:107`); no repair (Q8-1c; the old `ReportCapture`'s text).
  The node fails once, so a closure reports its first failing capture in occurrence order, as the old path and the explicit
  list do.

**Why a scan of the published columns.** It reads exactly the row the builder reads, after every write, so last-write-wins
(findings) is irrelevant, and the publisher keeps deciding nothing (`Binding.Hir.cs:5-6`, R4-M61). A judgment at the child's
visit would rest on the pre-order invariant (all writers are ancestors) and leave the ancestors' Op, Completion and `Broken`
rows stale after a mid-walk failure, which needs the same republication.

**H1a (new, behavior-preserving, D+R).** A failure must exist before `Check` counts states and collects issues
(`Binding.cs:1006-…`), but publication is lazy and needs a finished pass (`Binding.Hir.cs:35-51`, R2b-D2). H1a moves `Check`
into its own file, splits it into failure producers (definition obligations, OCC paths, call and runtime-test constraint
checks, `Unvisited` → `Unresolved`, part prerequisites) and counting, and publishes every module (library included) between
them in a Final pass; `PublishHir()` then returns the pass's version without work. Production cost after G2 is nil (the only
production `Bind` caller, `Project.cs:502`, is followed by `Analyze`, which from G2 builds MIR); tests that only bind pay one
publication. `HirColumnsTest.PublishIsInvisible` (`:303`) becomes vacuous and is re-pointed with its reason; `CheckBound()`
repeats producers, publication, H1 and counting.

**H1, the Final order.** producers → publish → `CheckTransfers()` (one linear, 0 B scan of each published module's `Ops`,
`Adjusts`, `Places` and nodes for S1 and S2; S3 ran at closure binding) → if a node failed, one republication → counting →
lint walk (rows 1–5 and 8 derive from `FailureCauses`, which then includes H1's failures). Republication creates no new
subject: failed nodes become `Error`, and the children Q21 keeps under them are written in place (Adjust `None`), which
neither S1 nor S2 selects; H1 asserts it by re-checking the republished columns (`InternalInvariant_Kd` on a new subject;
cost only in rejected programs). An accepted program pays the scan only.

**Soundness.** (1) Completeness: every Move or Copy of a written Place in MIR comes from the Adjust column (S1), an `Unset`
row (S2), a capture (S3), a Pattern acquisition from Binding's plan, `@move`, or a default's copy; the validator rule (§3.9)
checks the Copy half for every producer. (2) Agreement: the scan reads the builder's row of the same pass. (3) No dependents:
a failed node is `Error`, so the builder builds a derived Opaque value; A1 sees no Move (ARCH:228, by construction).
(4) Independent errors: from H5 MIR runs on an incomplete Binding, and Q21 keeps the children Binding resolved; before H5 the
old analysis would skip the program, hence PD29. (5) An `Unset` row from an Error proof (`Binding.Hir.Adjust.cs:198`) is
derived. A column row saying Move where SPEC says borrow is a column defect, fixed in the publisher's rule before H1.

**Q14** (probe, no commit, `artifacts/verify/r6-q14/`): S1–S3 run read-only after `PublishHir()` over the tests, the
diagnostic corpus, the library and the 40 harnessed milestones. At each subject it compares every old record with H1's: code,
Note, case fact and repair, normalizing parentheses (old at `s`, H1 at `(s)`; review probe s03); each difference classified
(a) old miss, (b) column defect (fixed first), (d) intended. It also runs the validator rule (§3.9) in shadow over the same
MIR: every finding must be at an S1–S3 subject or a PreparedParam root (open point 1). The library and milestones give zero
records. Probe p1–p4, p7, p8 and review probes t01–t03, s01, s03 become H1's regression tests.

**Deletions.** H1 deletes the 10 old report sites (`OwnershipAnalysis.cs:836, 1217`, `.Borrows.cs:447`, `.Closures.cs:60`,
`.Elements.cs:253`, `.Objects.cs:27`, `.Pointers.cs:87, 302`, `.Sequences.cs:65, 109`) and `OwnershipRequirement.TransferRequired`
(later Kind values shift; only names are published). Every TransferRequired record is then `Binding.TransferRequired`; the old
cascade after it (probe p1: `MovedPlace_Kd` at the later use) disappears. DIAGNOSTICS (H1): `:107` (the closure's
TransferRequired is Binding's; `DefaultArgumentMove_Kd`'s producer), `:152` (Binding rejects the omitted-list capture; the
closure's creation fails, its body is still built, the binding stays initialized), `:166` (TransferRequired's case fact from
`BareAcquisition`'s mask, not a case run).

### 3.8 Q21: a node failure keeps independently resolved children (G121, before H1)

| Item | Content |
| --- | --- |
| Required meaning | SPEC 23.1.2: a failure reported at a node does not hide independent errors in the children Binding resolved (PLAN G121; review probes p7, t03: a failed capture would replace the closure's body; a failed compound S1/S2 Place would hide MIR problems in its receiver or index operands) |
| Contract change (R4-M61) | Today "without its facts the node is `HirOp.Error`, which the builder replaces whole". After Q21: an `Error` node's value is derived (Opaque), but the children Binding resolved stay published as evaluated. An anonymous function whose creation failed keeps its body published, and the builder builds that body as its own MIR body; a failed compound Place (Field, Index, Deref, Group) keeps its operands published, written in place (Adjust `None`), and the builder evaluates them, as Places or as values into temporaries, before the Opaque value, never acquiring them |
| Files | HIR publisher (`Binding/Binding.Hir.cs`: `PositionOf`'s `Replaced`, the `Consume` gate at `:118-122`); MIR builder (`Mir/Build/MirBuilder.cs`: `Opaque` at `:424`, the closure body at `:204`) |
| Invariants | No acquisition is built for an `Error` node or from its kept children, so no `Copy` or `Move` of them reaches the validator rule or A1; a child Binding did not resolve stays replaced |
| Unit | Q21, before H1 (and before Q14's final run). Placement: window W1 or an R4 unit, agreed with R4 (ask A-14); review D+R under this package, with an R4 reviewer (A1–A5 trust the builder) |
| Verification | `HirColumnsTest` (kept children, in-place rows), `MirBuildTest` (a failed closure's body built; a failed `a[f(x)]` builds `f(x)`), mir goldens; snapshot under `mir` classified (more independent records in rejected programs, SPEC 23.1.2) |

### 3.9 The validator rule (Q8-3)

| Item | Content |
| --- | --- |
| Rule | In `Mir/MirValidator*.cs`: a `Copy` operand's Type is Copy, read from Binding's published proof under the body definition's premises, `TyCtxt.Copy(definition, type)` (`Binding/TyCtxt.Effects.cs:59`); no new proof. Exempt: `PairAcquire` operands (by case, SPEC 8.14) and the operands of per-view `Adapt` (`MirValidator.Types.cs:119`) |
| Owner and meaning | A producer invariant of validated MIR (the validator is the builder's, AGENTS): Binding stays the judge of TransferRequired; a finding means the builder or Binding broke its contract (`InternalInvariant_Kd`, refusing generation) |
| Lands | Inside G:R6-check-flip, in H1 or the unit right after it, before the group merges; Q14 runs it in shadow first |
| Hold | `MirValidator.cs` is in W1's file list (S2-S4 X29); the rule follows F11's (G131) validator rule and shares its hold window |
| Cost | One Copy lookup per `Copy` operand of each built body (measured in its unit) |

### 3.10 Dispositions

As d2 §2.8: both files go from R0's "delete R6" (`artifacts/verify/reduction-R0/disposition.md:106, 191`) to R7.
`ClosureSignature` has 8 Binding callers plus `CloseClosureTypes` (`Binding.Structs.cs:34`) and one Emission caller;
`InstantiateRequirementCall` serves bind-time selection (`Binding.RequirementItems.cs:131`, `Binding.SelectedRequirements.cs:43`,
`Binding.Specializations.cs:164`) and `Witnesses` serves A4 (`TyCtxt.Effects.cs:40`, `MirEffects.Solve.cs`). K2 deletes the
never-cleared closure-context list, F5a extracts the witness resolver, H11 deletes `InstantiateForwardedCall`; the rest goes
with R7's ContractSolver and Types consolidation. Both stay PD20 hold files (R5-M7).

## 4. What becomes unnecessary; size and speed

| Removed or never built | When | Size (H) |
| --- | --- | --- |
| CFA's lint half (discard, unsafe, jump, label, fallthrough checks, repairs, `ReadsBinding`, priorities, `GuessedBy` copy, `MultiLineLiteralFinder`) | Q9–Q12 | ≈ −450 of 1,558 |
| The publisher's target resolution; CFA's `UnsafeFunctionValue` copy | Q11; Q10 | −4; −20 |
| `OwnershipFailure` (replaced) | Q7 | ±30 (+≈ 130 renamed) |
| Two copies of the reassignability expression; 17 builder sites | Q16 | −40 |
| A code → requirement table, its selectors, two `MirFact` kinds, the `MirReport` derivation | never built (D7) | (≈ −80 against the plan's Q17) |
| The Q13 extraction; d2's stored field; a constructor view | never built | (±20, +10) |
| The 10 old TransferRequired sites and the enum member; A1 dependents at those Places | H1 | ≈ −75 |
| MIR's located `Unsupported_Kd` at invalid jumps | A-8 (R4) | small |

Added: lints ≈ +480 (Q9–Q12), H1a ±70, H1 ≈ +110 (scan, S3 form, case fact, republication, assertion), Q21 ≈ +60 (H),
validator rule ≈ +25, Q17 ≈ +60 t, Q16 ≈ +30. Net Q stream ≈ +240 until H10 deletes CFA's remainder (≈ −1,100). Speed: Q9
adds one Final walk to Bind and takes the lint work out of CFA's walk (both measured, P3); H1a adds no production work after
G2; H1's scan is linear and 0 B; republication and its assertion only in rejected programs; the validator rule adds one Copy
lookup per `Copy` operand. Every new structure is pooled per pass.

## 5. Interfaces

| Other design | Q8 provides | Q8 needs | Mismatch / open |
| --- | --- | --- | --- |
| Chapter F (S3), W1 | Q16 is W1's last unit (F0 → F7 → F8 → F9 → Q16); Q21 may join W1 | W1 open; F11 (G131) first in W1, whose `MirValidator.cs` window the Q8-3 rule shares | none |
| Chapter K (S2) | `Binding.ClosureInstances` stays until R7 except K2's list deletion | K2 | none |
| F5a / PD16 | `InstantiateRequirementCall` stays (bind-time materializer) until R7 | F5a | none |
| Chapter C (S4) | — | — | sel uses the old acceptance, which rejects S1–S3 and the PreparedParam shapes, so R5 codegen never generates them |
| R4's 1c | requirements named per rule (A-5) | DefaultArgument* for PreparedInput roots (needed from H5, since H1 excludes PreparedParam roots) | the S2 shape of that root builds a Copy, not a Move (open point 1) |
| H0 | each `MirProblem`'s requirement; lint records under `Binding` (conditions 1–12); H1, Q21 and the validator rule for G:R6-check-flip; `Binding.TransferRequired` and the A4/A5 renames for H5's classification | the publication condition for codes sharing a requirement at one subject; the partition of MIR problems | plan rows H0 and H3 ("requirement derived from code and facts with … Q17's table") change to "the problem's own requirement" |
| G0 / R4 G103-E9b | Q11's HIR contract on null rows | A-8 | none |

## 6. Unit mapping

| Unit | Change | Prereq | Verification | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- |
| Q7 | §3.1; DEC entry superseding R4-M3a's naming clause | OQ10 | as plan; bp | ±30 (+≈ 130 renamed) | D+R (Q8) | adv |
| Q9 | restructuring commit; `Binding.Lints.cs` contract, table, record form, `ReportDerived` condition, walk, rows 1–6, guess query moved (CFA's `RequireFallthrough` calls it); CFA rows 1–6 removed; DIAGNOSTICS `:106`; ARCH canonical row "HIR lints"; DEC entry | Q6, OQ10 | as plan; snapshot classified (`ControlFlow` → `Binding`); warm Bind 0 B (`Purpose=Allocation`); Bind and Analyze timing | +280/−230 | G:L | no |
| Q10 | rows 7–8 with `RemoveUnsafe` through call plans; `UnsafeFunctionValue` reproducers as tests, CFA copy deleted (row 13 only if one is uncovered); `LintErrors`, the two old-analysis reads, `LegacyRecords`; the tinyhand `ControlFlow` description; DIAGNOSTICS `:106` | Q9 | as plan | +120/−150 | G:L | no |
| Q11 | §3.3; ARCH HIR-contract sentence; DIAGNOSTICS `:106` | Q9 | as plan; `HirColumnsTest` target rows; every null row has a row 9/10 record | +40/−45 | G:L | no |
| Q12 | §3.4; DEC entry (PD32); DIAGNOSTICS `:106` | Q9 | as plan; `syntax.json` `guessed-*`/`written-*` rows equal; ledger `a1-result` unchanged | +45/−30 | G:L | no |
| Q13 | closed (findings) | — | — | 0 | — | — |
| Q14 | probe per §3.7 (records compared by code, Note, case fact, repair; validator rule in shadow) | Q8's design review; Q21 for the final run | Evid | 0 | — | no |
| Q16 | §3.5 | F0 (W1), OQ10 | as plan; bp | +30/−40 | D+R (Q8) | W1 |
| Q17 | §3.6 ledger part | Q7, A-5 (revised) | `MirDifferenceTest` requirement column; ledger re-baseline | +60 t | D+R (Q8), R4's A6 lane | adv |
| **Q21** (new) | §3.8; DEC entry superseding R4-M61's "replaced whole" for kept children | OQ10, A-14 | §3.8 | +60/−10 | D+R (Q8) + R4 reviewer | W1 or R4's |
| **H1a** (new) | `Check` moved and split; eager Final publication; `PublishIsInvisible` re-pointed; DEC entry refining R2b-D2 | G2, OQ10 | bp (HIR snapshot equal); cold and warm Bind time and allocation | ±70 | D+R (Q8) | no |
| H1 | §3.7: `CheckTransfers` (S1, S2), S3 form, case fact, republication and its assertion, 10 sites and the enum member deleted, ledger rows reclassified; DIAGNOSTICS `:107`, `:152`, `:166` | H1a, Q21, Q14, Q7, R4's exit, G11, H0, OQ11 | as plan; the regression tests of §3.7; `MirInitTest` | +110/−75 | final review of Q8; G:R6-check-flip | no |
| H1 or the next unit | §3.9 validator rule | H1 | `MirValidatorTest` cases (a non-Copy `Copy`; `PairAcquire`, per-view `Adapt` exempt); Session with milestones | +25 | G:R6-check-flip | W1's validator window (F11) |

G:L = Q9–Q12 (DEC Q6 form 2; Q11's jump part feeds A-8 and so G0). Order: Q7 and Q9 first after OQ10; Q10–Q12 after Q9;
Q16 in W1; Q21 when R4 agrees (A-14), before Q14's final run; H1a after G2; H1 and the validator rule with H5.

## 7. Departures from owner-approved and recorded texts (for the owner's summary)

| Text | Departure | Reason |
| --- | --- | --- |
| PLAN10 §5.5 A3 row and §9 | FunctionFallthrough judged by Binding (PD32) | SPEC 14.9 makes it result validation by Structural Completion |
| Plan row Q17, rows H0 and H3, ask A-5 (R5-M1) | No code → requirement table; each MIR recording rule names its requirement | PLAN10 §7 (approved): the decision point records the requirement, never looked up from the code |
| Plan row Q11 | Its records move from the jump binding to the lint walk; the jump binding writes only the target row | one record source; coverage of bodies Binding does not bind |
| DEC R4-M3a, two clauses | Diagnostics owns the kinds (Q7); R6 unit a's derivation superseded (D7) | the names are public diagnostic vocabulary; the stated reason ends with Q7 |
| DEC R4-M61 ("an Error node is replaced whole") | Kept children (Q21, G121) | SPEC 23.1.2 independent errors (review Q8-2) |
| DEC R2b-D2 ("lazily, at most once per pass"; owner, as recommended) | Eager at the end of each Final pass, twice when H1 failed a node (H1a) | H1 judges the published columns inside the pass; nil production cost after G2 |
| Plan rows Q13, Q14, H1; new units H1a, Q21 | Q13 closed; Q14 depends on Q8 and compares full records; H1 depends on H1a and Q21 | findings; review |
| DEC R4-M4 (TransferRequired passes through) | Ends at H1 | TransferRequired becomes Binding's on both paths |
| R0 disposition rows | `ClosureInstances`, `RequirementInstances` → R7 | definition-time callers |
| Public requirement names in `kimi check` output | 12 lint codes `ControlFlow` → `Binding` (Q9–Q12); `Ownership.TransferRequired` → `Binding.TransferRequired` (H1/H5); at H5, A4/A5 decisions the old path reports under `Binding.VirtualEffectBound`, `Binding.IncompatibleImplementation` and `Binding.OriginRelation` become `Ownership.*` | PD31; DIAGNOSTICS rule 1 |

Owner summary additions (review): Q8-1a keeps `DefaultArgumentMove_Kd` (no default code change); Q8-1b and Q8-1c keep the
documented records (case fact; the omitted-list Note, no repair, the default Note); Q8-2 changes R4-M61's node-failure
contract (Q21); Q8-3 adds the validator rule.

## 8. Items needing owner approval (inside OQ10)

- No new vocabulary entry, diagnostic code or requirement name (PD21): lint rows are condition numbers.
- New canonical component: HIR lints (ARCH canonical-component row, entry contract §3.2).
- Representations and contracts: `MirProblem.Requirement` and `MirReport.Add`'s agreement check (R4's record; A-5); the
  reassignability helper; Tuple-element rows in `PlanRow.Folded`; the jump target row's writer; R4-M61's change (Q21); the
  validator rule.
- R2b-D2's refinement and the new units H1a and Q21; Q13's closure.
- D7 and its fallback; the public requirement-name changes of §7.

## 9. Asks to the R4 stream

- **A-5 (revised; replaces "Loan facts on `MirProblem` and the requirement derivation in `MirReport`").** In unit 1c, or the
  first R4 unit after Q7 merges: (1) Loan facts on `MirProblem` (A3 §6.4), as R4 designs them; (2) G125, R4's choice;
  (3) `MirProblem` gains `DiagnosticRequirement Requirement`, set by each recording rule as §3.6 lists (A4's obligation row
  carries its requirement beside its code; A6's PreparedInput mapping sets `DefaultArgument*` with its code); `MirReport.Add`
  checks that merged problems agree; no code → requirement table. Needed by Q17, H3. If 1c must land before Q8's approval:
  A-5a = (1)+(2) in 1c, A-5b = (3) right after Q7.
- **A-8 (refined).** G103-E9b's jump part: in `MirBuilder.Transfer` (`Mir/Build/MirBuilder.Control.cs:105-117`), a Jump or
  Continue whose published target row is null is derived: its operand is still built (its independent errors stay) and no
  record is made; a non-null target not built yet (for, match, defer) keeps its located `Unsupported_Kd`. Probe p11: today an
  `exit` without a target gives a construction `Unsupported_Kd` beside CFA's `InvalidJumpTarget_Kd`. Needed by G0, after Q11.
- **A-13 (information, no work; Q8-decisions).** Until H1, MIR accepts with no problem a bare Move of a non-Copy local, a
  non-Copy Place used as a statement (copied, then dropped twice) and inferred captures of non-Copy values; H1 closes them with
  the flip (PD29); the ledger cannot show them (R4-M4).
- **A-14 (new).** Agree where Q21 lands (W1 or an R4 unit) and review its change of R4-M61's contract (§3.8); it precedes H1.
- **Dependency, no new ask.** 1c's "DefaultArgument* for PreparedInput roots" is needed from H5, because H1 leaves PreparedParam
  roots to it (Q8-1a).

## 10. Risks

1. Q14 may find column rows that say Move where SPEC says borrow; each is fixed in the publisher's rule before H1.
2. R4 may decline revised A-5 or A-14 while its lanes are active; A-5 has a fallback; without Q21, H1 would lose independent
   errors under failed closures and compound Places, which the review blocks.
3. Q21 widens what MIR builds in rejected programs; more independent records appear under `mir` and must be classified.
4. The validator rule could flag a legitimate `Copy` the exemptions miss; Q14's shadow run lists every finding first.
5. The lint walk is a second walk beside CFA until H10 (measured in Q9; P3 allows a recorded regression).
6. Eager publication changes when `HirTables.Broken` is computed: every fact-producing step must precede publication (H1a's
   bp: HIR snapshot equal).
7. Public requirement names change in `kimi check` JSON (§7); snapshot classification per unit.
8. Q9–Q12 edit CFA under the D5 freeze (removals and one call to Binding's guess query): recorded as R6-check items landed
   early (ARCH R6 row).

## 11. Open points for the planning session

The decisions of `Q8-decisions.md` settle the round-1 points. New:

1. **S2 on a PreparedParam root (round-2 finding).** Q8-1a's exclusion is right for S1: the builder moves the prepared input
   (`_0 = move (*_1)`) and A1, through 1c's PreparedInput mapping, reports `DefaultArgumentMove_Kd` from H5. For S2 it leaves
   no producer: in a default whose block discards a non-Copy preceding parameter, the builder Copies through the prepared
   reference and drops the copy (probe u2: `_2 = copy (*_1)`, `drop _2`, no MIR problem; old path `DefaultArgumentMove_Kd`).
   A1 sees no Move, and the validator rule (§3.9) would raise `InternalInvariant_Kd` on a user error. Options: (a) S2 keeps
   PreparedParam roots, reported as `TransferRequired_Kd` with the default Note and no repair, the record Binding already gives a
   bare capture entry of a preceding parameter in a default (`ReportCaptureEntry`, DIAGNOSTICS `:107`); a code change for this
   shape only (d); (b) the column records the bare acquisition of unconsumed Places (the container takes its items), so S2 folds
   into S1 and the Move reaches A1's mapping; it needs a published "container value" fact for the builder's `Result` and a
   builder change in R4's files; (c) A1 reports `DefaultArgumentMove_Kd` at a `Copy` of a PreparedInput root without a Copy
   proof, with the validator exempting it (a second Copy judgment). Recommend (a): smallest, keeps one judge and the documented
   default record; (b) is the cause-level fix for R7's inline writes.
2. **Validator hold timing.** G:R6-check-flip lands after R4's exit, when plan §3.5 needs no hold; the shared window with F11
   matters only if R4 still edits `MirValidator*.cs` then. Recommend: no separate hold unless R4 is active.

## 12. Evidence

- Probe 1–2 (2026-10-18, dev `b56cbf72`) and probe 3 (round 2, dev `a1c87dcd`): throwaway tests (`MinimalEmissionTest.Analyze`,
  `BuildMir()`, `MirDump`); sources, the test text and outputs in `bin/worktrees/r5-pkg-q8/artifacts/verify/r5-pkg-q8/`
  (Git-ignored; back up before removing the worktree). Review probe: `artifacts/verify/r5-evidence/q8-review/` (main checkout).

| Probe | Source (abridged) | Old path | MIR |
| --- | --- | --- | --- |
| p1 | statement `text` (`string`) | `TransferRequired`, then a dependent `MovedPlace_Kd` | `copy _1`, `drop _2`, later `drop _1`: double drop |
| p2, p3 | closure without list using `text`; using `r: uniq/i32` | `TransferRequired` at the closure | `Closure(copy _)`; no problem |
| p4 | `let b = text` | `TransferRequired` | `_2 = move _1`; no problem |
| p5, p6 | unsafe overload group as a value | `Binding.Ambiguous` at the name | not built |
| p7 | `f<T>` closure using `value` | two `TransferRequired` (capture, body) | `Closure(copy _1)`; only `StaticMovePathRequired_Kd` in the closure |
| p8 | statement `value` of `T` | `TransferRequired` | `copy _1`, double drop |
| p9, p10 | `-> i32` body ending after `if b => return 1` | `FunctionFallthrough_Kd`, derived `UninitializedUse` | `return` with `_0` unset; no problem |
| p11 | `exit` with no target | `InvalidJumpTarget_Kd`, `Ownership.Unsupported` | construction `Unsupported_Kd` |
| u2 | default `do` block discarding `a: string` | `DefaultArgumentMove_Kd` | `_2 = copy (*_1)` (PreparedInput), `drop _2`; no problem |
| u3 | `b: string = a` | `DefaultArgumentMove_Kd` | `_0 = move (*_1)`; `StaticMovePathRequired_Kd` |

## 13. DEC entry after OQ10 (draft)

- **R5-Q8 R6-check prerequisite package** (owner, after the design review; OQ10, R5-M1) `draft/Sketches/R5 Step 0/Q8-package.md`
  with `Q8-decisions.md`, as reviewed, is the owner-approved design of DEC Q6 form 1 for Q7, Q16, Q17, Q21 and H1a, and of form
  2 for G:L (Q9–Q12); H1 and the validator rule take it with the final review in G:R6-check-flip. It decides: Ownership
  requirement kinds in Diagnostics; one HIR lint component (12 rows, PD31's form, condition = row, one Final walk); the jump
  binding writing the target row, with the HIR contract on null rows; FunctionFallthrough in result validation (PD32,
  departing from PLAN10 §5.5/§9); Q16's two views; MIR problems naming their requirement (restoring PLAN10 §7; superseding
  R4-M3a's derivation and naming clauses); TransferRequired judged by a scan of the published columns with three sources after
  an eager Final publication (refining R2b-D2), PreparedParam roots left to `DefaultArgumentMove_Kd`'s producer, the case fact
  and the omitted-list record kept; kept children of failed nodes (G121; superseding R4-M61's "replaced whole" for them); the
  validator's Copy rule; `Binding.ClosureInstances` and `Binding.RequirementInstances` at R7; Q13 closed. Each component,
  contract change and superseded entry is recorded again by the unit that first implements it. Rejected: restructuring
  `Consume`; a judgment at the publisher's visit; node failures for lints; a new `BindingFailure`; a code → requirement table
  (fallback only if R4 declines A-5).
