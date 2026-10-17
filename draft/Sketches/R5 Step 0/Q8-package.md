# Q8 package: the R6-check prerequisite design

Date: 2026-10-18. Status: working record of R5 step 0 (full process). It compares design 1
(`deep-dives/q5-r6check-prereqs.md`, "d1") and design 2 (`Q8-design-2.md`, "d2") and is the design the planning session
decides on, sends to an independent design review and then to the owner once (OQ10, DECISIONS R5-M1). Not authority.

Base: code at `origin/dev` = `b56cbf72` (Q6 and Q6b landed); R4's MIR record at `origin/claude/a5-1b-core` = `8961f577`.
Paths are under `src/Kimi/Compiler/` unless they start with `src/`, `tests/`, `docs/` or `draft/`. "(H)" marks an inference
not checked by running code. "Probe" = the throwaway run of 2026-10-18 (§12): a test written into a detached worktree at
`b56cbf72`, never committed. Plan = the R5 and R6 Implementation Plan; PLAN10, CFA (`Analysis/ControlFlowAnalysis.cs`),
DEC, ARCH as in the plan.

Governs (OQ10): Q7, the lint group G:L (Q9–Q12), Q16's Binding views, Q17, H1 and the new unit H1a; PD29, PD31, PD32; the
dispositions of `Binding.ClosureInstances` and `Binding.RequirementInstances`. Q13 closes as found (r5-open-findings).

## 0. Decisions in one page

| # | Decision | § |
| --- | --- | --- |
| D1 | `OwnershipRequirement` (the 18 names of `OwnershipFailure`, same order and values) in `src/Kimi/Diagnostics/DiagnosticRequirement.cs` replaces `OwnershipFailure`; the old analysis uses it as its failure kind | 3.1 |
| D2 | One Binding component, **HIR lints**: a 12-row table (code, severity, derivation, guess, 17.5 priority), one Final walk at the end of `Bind(Final)`, one pooled record list, reported by `ReportDiagnostics`. Rows 1–11 come from the walk, row 12 from the end-arrival check of result validation, which the walk calls. `UnsafeFunctionValue_Kd` gets no row | 3.2 |
| D3 | PD31's form: requirement `Binding` (Kind 0), condition = row number, never a node failure; `Koto.ReportDerived` gains `condition`; the old analysis reads `Binding.LintErrors` where it read `flow.Issues` | 3.2 |
| D4 | The jump binding writes the jump's target into its `PlanRow.Plan`, null when the transfer is invalid; the publisher stops resolving targets. HIR contract: a null target row means Binding recorded row 9 or 10 there (ask A-8) | 3.3 |
| D5 | FunctionFallthrough is result validation (PD32): `EndArrival` beside `AddBodyResult`, called once per function and accessor by the walk; one guess rule; MIR records nothing at that end (probe), so no MIR dependent exists | 3.4 |
| D6 | Q16: two views, a computed `BindingSymbol` mutability view (replacing two existing copies) and the Tuple-element position as `PlanRow.Folded`; no constructor view (`AdtDef.IsConstructor` exists) | 3.5 |
| D7 | **Q17 departs from the plan row**: each MIR recording rule names its requirement (`MirProblem.Requirement`); no code → requirement table, no derivation in `MirReport` (revised ask A-5). This restores PLAN10 §7 | 3.6 |
| D8 | H1 judges TransferRequired on the **published Adjust column** by a linear scan inside the Final pass, after an eager publication (new behavior-preserving unit H1a, refining R2b-D2); a failed node triggers one republication. Three subject sources: consumed children (S1), unconsumed Places (S2), inferred captures (S3); S2 and S3 are MIR acceptance gaps confirmed by the probe | 3.7 |
| D9 | `Binding.ClosureInstances` and `Binding.RequirementInstances` are re-disposed from "delete R6" to R7 | 3.8 |

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
| 23.1.2; DIAGNOSTICS rule 1, §4.1 ("Check: requirement and condition number (independent of the code)", `DIAGNOSTICS.md:64`), §4.3 "Later phases" (`:106`) | independent errors; one requirement, one phase | all | all |

Chain (AGENTS "Design before code"):

- **Lints.** Meaning: 12 checks over syntax and Binding facts (none reads `Op`, `Adjust` or `Completion`; d1 §1.1). Lacking:
  an owner after R6-check (ARCH:15 names "HIR lints" in P2–P5s). Owner: Binding. Unnecessary: CFA's lint half, its
  `ReadsBinding` code list (:275-277), priority code (:450-467), the `ControlFlow` requirement on these records.
- **Jump targets.** Meaning: the lexical target (14.5.2), one pure rule (`KotoHelper.ResolveTransferTarget`,
  `Parsing/Koto/Common/ControlFlowContext.cs:13`). Wrongly allowed: the publisher resolves it (`Binding.Hir.cs:98-102`) against
  its own header ("Must not resolve again", `:6`). Owner: the jump binding (`Binding.Expressions.cs:716-757`). Unnecessary:
  the publisher's resolution; MIR's located `Unsupported_Kd` at an invalid jump (A-8).
- **Requirement of a MIR problem.** Meaning: the check identity is the requirement the deciding check names, never looked up
  from the code (PLAN10 §7, line 499). Lacking: `MirProblem` carries none (R4-M3a). Owner: each recording rule. Unnecessary:
  a code → requirement table, its fact selectors, a MirReport derivation.
- **TransferRequired.** Meaning: SPEC 3.5. Wrongly allowed: MIR moves or Copies a bare non-Copy Place with no problem (probe
  p1, p2, p3, p4, p7, p8). Owner: Binding (`BareAcquire`, "the one bare-acquisition rule", ARCH:92). Unnecessary: the 10
  old sites, `OwnershipRequirement.TransferRequired`, A1's `StaticMovePathRequired_Kd` dependents (derived by construction).

## 2. Decision table

| # | Topic | Design 1 | Design 2 | Chosen | Reason |
| --- | --- | --- | --- | --- | --- |
| 1 | Ownership requirement kinds | Diagnostics enum, same 18 names; `OwnershipFailure` deleted (§2) | same (§2.1) | agree | names are already public (`DiagnosticRequirement.tinyhand:590-641`) |
| 2 | Lint component shape | P5 walk in `Binding.Lints.cs` (+ `.Unsafe.cs`), units L1–L4 | one component, 12-row table, one record list | d2's table, plus d1's point that the walk must reach Binding's synthesized calls | one record path; see contract in §3.2 |
| 3 | Lint records | owner question (new `BindingFailure` vs reuse) | PD31 reuse, condition = row, non-failing | d2 | PD31 decided; the problem key excludes the code (`Diagnostics/DiagnosticFact.cs:26`), so rows need distinct conditions |
| 4 | Walk placement | after `ReportDiagnostics`' Final block | end of `Bind(Final)`, reported by `ReportDiagnostics` | d2 | `Analyze` reads `LintErrors`, and test helpers run Bind → CheckStartup → Analyze without `ReportDiagnostics` (`tests/xUnitTest/Tests/MinimalEmissionTest.cs:324-333`) |
| 5 | Where rows 9–11 are recorded | at the jump binding | at the jump and label bindings | in the walk, by the pure rule | one record source; no record from tentative bindings; syntax-only checks cover bodies Binding does not bind as CFA does today (definitions not resolved, `Binding.Hir.cs:163`, H) |
| 6 | Jump target fact | HIR contract only | jump binding writes `PlanRow.Plan`; publisher stops resolving | d2, null also for an unlabeled `yield` to a discarded selection | publisher header; one contract for both invalid forms |
| 7 | `UnsafeFunctionValue` | delete CFA's copy if a reproducer shows coverage, else move | no row; an uncovered form becomes a fifth Binding node failure | d2 | `IsValueUse` (`Binding.Expressions.cs:142-143`) is CFA's walk (:661-667) exactly; probe p5, p6: an unselected unsafe group fails `Ambiguous` in Binding |
| 8 | FunctionFallthrough | beside `AddBodyResult`; ledger `a1-result` unchanged | beside `AddBodyResult`, once per function from the walk; one guess rule; MIR dependent left to H0 | d2's placement and guess rule, d1's MIR fact | probe p9, p10: MIR records nothing at the end arrival (R4-M21: a Return checks nothing), so d2's open question 3 is moot |
| 9 | Guess query of CFA's remainder | Binding query; CFA's structural `RequireFallthrough` calls it | not addressed | d1 | no second copy of `SuppliedCompletion` until H10 |
| 10 | Q16 views | mutability, Tuple ordinal, literal bytes | stored `BindingSymbol.Mutable`, Tuple `Folded`, conditional constructor view | computed mutability view; Tuple `Folded`; literal bytes by `HirOp.Literal`; no constructor view | `Binding.IsMutableDeclaration` (`Binding.Expressions.cs:10-12`) already decides; `AdtDef.IsConstructor` exists (`Binding/AdtDef.cs:134`) |
| 11 | Q17 | derivation in `MirReport` from code and facts (R4-M3a), enum moved | the recording rule names it; table as fallback | d2 | PLAN10 §7; five codes are ambiguous (`MirDifferenceLedger.cs:45-74`), so a table needs facts that exist only to re-derive |
| 12 | Consumed-children source (Q13) | extract one enumeration (T1a) | closed | closed | `HirPublisher.Consume` already is it; 2,620 children are written twice, 480 with different values (findings) |
| 13 | H1's subject source | the extracted enumeration | scan of the published column after eager publication | d2 (scan), not a judgment at the publisher's visit | §3.7 |
| 14 | H1's subject set | consumed children | S1 + S2 + S3 | S1–S3; S2 defined on the columns, not by a Discard Context predicate | §3.7; the probe confirms S2 and S3 |
| 15 | H1 timing | owner question | with H5 (PD29) | PD29 | a node failure without the flip makes the old analysis skip the program (`OwnershipAnalysis.cs:89-92`) |
| 16 | Dispositions | R5 views + R7 | R7 | R7 | definition-time callers (§3.8) |

## 3. Components and contracts

### 3.1 Q7: `OwnershipRequirement`

As d2 §2.1, unchanged: `public enum OwnershipRequirement : byte`, the 18 members in `OwnershipFailure`'s order
(`Analysis/OwnershipModel.cs:139-176`), so every Kind, name and tinyhand row stays (bp);
`DiagnosticRequirement.Ownership(OwnershipRequirement)` (`DiagnosticRequirement.cs:42`), `All` and `NameOf` switch to it;
`OwnershipFailure` deleted, `OwnershipIssue.Failure` typed by it; every reference renamed (d1 counts 78 Analysis + ≈ 52
test, d2 85 src + 51 test; the unit re-measures). Invariant: one kind enum per partition. Prohibition: no Analysis-side copy,
no mapping switch, no member added (PD21). Later: H1 removes `TransferRequired`; H10/H11 remove the members whose last
producer they delete. Hold: adv on `DiagnosticRequirement.cs` and `MirDifferenceLedger.cs` (R5-M7).

### 3.2 HIR lints (Q9, Q10, Q11's rows, Q12's call)

**Contract** (entry file `Binding/Binding.Lints.cs`; a second file by responsibility if the unsafe rows need it):

| Item | Content |
| --- | --- |
| Responsibility | P5 "HIR lints" (ARCH:15): the 12 rows below, judged once per Final pass as non-failing Binding records, with repairs and with their derivation from Binding failures and parser recoveries |
| Inputs | The Final pass's bound tree and facts: Types, adaptations, call plans (`CallPlan.Of(node, i)`), Copy proofs, `InfersUnit`, `ResultStructure()`, `FailureCauses`, recovery causes, directive selection |
| Outputs | `lints`: a pooled `List<(Koto Node, byte Row, Koto? Root)>` cleared in `ResetPass`; `LintErrors` (rows whose catalog severity is Error); records published by `ReportDiagnostics` at Final, next to the alias, pattern and position warnings (`Binding.cs:144-160`) |
| Walk | One walk of the source modules at the end of `Bind(Final)`, over evaluated positions (Types, attributes, Origin clauses, effect items and constraints skipped as CFA :761-830), excluded tests and unselected directive groups as today. A node's synthesized evaluation (getter, setter, range value, view range, key resolution, arithmetic and comparison calls, formatting; CFA :689-760) is reached through the node's call plans and their synthesized argument sources, never through a copy of CFA's switch. Walk state: the innermost Unsafe Block's used and uncertain bits, the function boundary |
| Invariants | Never fails a node: `Binding.Result` and every later phase see the completeness they see without lints. One row per code; condition = row number; a derived Error keeps its row's condition. Warm rebind 0 B: no Koto-keyed set or dictionary (ARCH "Syntax identity"); duplicates and the 17.5 priority resolved by sorting `lints` by (SyntaxId, priority) at report time |
| Prohibitions | No inference or proof re-run to explain a record; no lint of library bodies; no record from a Provisional or tentative binding; no node failure |

**The table** (replaces CFA's `ReadsBinding` :275-277, the priorities :450-467 and `GuessedBy`'s code list :392):

| Row | Code | Sev. | Judged from | Derived from `FailureCauses` (a warning records nothing) | Rests on a guess | 17.5 priority |
| ---: | --- | --- | --- | --- | --- | ---: |
| 1 | `DiscardedTail_Kd` | W | `do`/`if`/`match` whose Unit Binding inferred | yes | yes (descent root) | 1 |
| 2 | `DiscardedResult_Kd` | W | Discard Context occurrence (`!KotoHelper.IsValueContext`) | yes | no | 2 |
| 3 | `UnusedTrySuccess_Kd` | W | same | yes | no | 3 |
| 4 | `DiscardedValue_Kd` | W | same, `IsEffectFree` | yes | no | 4 |
| 5 | `OwningWriteArgument_Kd` | W | `WriterWrite` call | yes | no | — |
| 6 | `StaticWhileTrue_Kd` | W | syntax | no | no | — |
| 7 | `UnsafeBlockRequired_Kd` | E | each call plan's target, `Deref`, `NoInit`, pointer arithmetic (one `RequiresUnsafeContext` rule) | no | no | — |
| 8 | `UnnecessaryUnsafeBlock_Kd` | W | block end; unknown need counts as use | yes | no | — |
| 9 | `InvalidJumpTarget_Kd` | E | the transfer-target rule (§3.3) | no | yes (unlabeled jump's target) | — |
| 10 | `UnlabeledYieldTarget_Kd` | E | same rule | no | yes | — |
| 11 | `OverlappingLabel_Kd` | E | ancestor labels (CFA :1511-1526 moved) | no | yes (label) | — |
| 12 | `FunctionFallthrough_Kd` | E | `EndArrival` (§3.4), called by the walk | no | yes | — |

`UnsafeFunctionValue_Kd` has no row (decision 7). Q10 keeps the reproducers as tests (single declaration, a group selected
by function item, Function Type and requirement item, an unselected group) and deletes CFA :658-677; a form still uncovered
becomes a Binding node failure at its binding site.

**Record form.** `node.Report(DiagnosticRequirement.Binding(BindingFailure.None), code, …, condition: row)`; a derived Error
`node.ReportDerived(…, keys, condition: row)` (`Parsing/Koto/Koto.cs:665` gains the optional `condition` that `Report` has at
`:627`). Existing Binding warnings use condition 0, so rows 1–12 never share a key with them. Acceptance reads the Binding
partition's error state as today (`src/Kimi/SolutionAndProject/Project.cs:510`): a lint Error rejects without making Binding
incomplete.

**Guess rule.** `SuppliedCompletion` and `IsSuppliedBody` (CFA :398-424) move into Binding beside `StructuralCompletion`,
generalized as d2 §2.4 ("a node whose completion Binding did not establish": a supplied body or an unresolved expression).
CFA's remaining structural `RequireFallthrough` calls that query until H10.

**Transitional reads of the old analysis** (deleted at H10):

| Old read | After |
| --- | --- |
| `earlierRejected = flow.Issues.Count != 0 \|\| …` (`OwnershipAnalysis.cs:101`) | adds `\|\| binding.LintErrors != 0`, from Q10 (the first unit moving an Error row) |
| `verified = flow.Issues.Count == 0 && …` (`:116`) | same addition; acceptance unchanged either way |
| `flow.Reported(body, FunctionFallthrough_Kd)` (`:262-267`) | `binding.FallthroughKey(function)` (Q12) |
| `LegacyRecords` reads `ControlFlow.Issues` (`tests/xUnitTest/LegacyRecords.cs:28-31`) | also reads the lint Errors as Binding records |
| `ControlFlowAnalysis.Targets` (`OwnershipAnalysis.cs:1966`) | unchanged: CFA's flow keeps calling the pure rule until H10 (D5 freeze; no reader gains anything) |

Structure: `Binding.cs` (1,917 lines) is over the 5,000-token limit and may not grow (ARCH structure limits); Q9's first
commit moves the Final non-failing-record report out of it (behavior-preserving) so the walk's call adds no net line.

### 3.3 Binding-owned jump targets (Q11)

| Item | Content |
| --- | --- |
| Rule | One function in `KotoHelper` decides validity: `ResolveTransferTarget` plus the unlabeled-`yield` clause now at CFA :1288 (`YieldKoto { Label: null }` targeting an `if`/`match` not in Value Context) |
| Fact | The jump binding (`Binding.Expressions.cs:716`, which already resolves the target for result typing) writes `HirTables.PlanFor(jump, valid).Plan = valid ? target : null` (R7's inline write; no forwarding property, R5-M9) |
| Records | The walk records row 9 or 10 from the same function (covers bodies Binding does not bind) |
| Publisher | Deletes `Binding.Hir.cs:98-102` |
| HIR contract (ARCH "Binding facts for MIR") | "A published Jump or Continue has its target row; a null row means Binding recorded `InvalidJumpTarget_Kd` or `UnlabeledYieldTarget_Kd` there." |
| MIR consumer | Ask A-8 (§9) |
| Other readers | Binding readers that run during binding (`Binding.ControlFlow.cs:490`, `StructuralCompletion.cs:280`, `Binding.ArgumentOperations.cs:395`, `Binding.Try.cs:20`, `Binding.Testing.cs:31`, `Binding.OriginDiagnostics.cs:151`) keep calling the rule |

Rejected: publishing the jump as `HirOp.Error` (its operand would be Replaced, losing the operand's independent errors);
a node failure (the regression PD31 avoids); a new column (the row slot exists).

### 3.4 FunctionFallthrough (Q12, PD32)

As d2 §2.4: `EndArrival(owner)` beside `AddBodyResult` (`Binding.ControlFlow.cs`), for a function or accessor with a Block
body whose declared result (written, else bound contract; Unit for a discarding body or setter, as CFA's `DeclaredResult`) is
not Unit: if `ResultStructure()` says the body completes, the end-arrival Unit source cannot fit → row 12 at the body.
Called once per function and accessor by the walk (one call site; no tentative record). `FallthroughKey(function)` returns
that record's key for the old derived link until H10. MIR: nothing to derive (probe p9, p10: the end arrival builds
`return` with `_0` unset and A1 records nothing, R4-M21); ledger row `a1-result` unchanged.

### 3.5 Q16 views (in W1)

The 17 sites (R5-M10) take existing facts: `HirOp.Continue` (`MirBuilder.Control.cs:121`), `HirOp.Closure`/`BoundClosure.Of`
(`MirBuilder.cs:201, 459`, `MirBuilder.Places.cs:131`), `HirOp.Group` (`MirBuilder.Places.cs:27`), Type kind
(`MirBuilder.Values.cs:300`), `HirOp.Literal` for the literal byte count (`MirBuilder.Formatting.cs:15`), symbol kind and
signature (`MirBuilder.cs:116-135`), `AdtDef.IsConstructor` (`MirBuilder.cs:163`). Two views remain:

| View | Contract | Replaces |
| --- | --- | --- |
| V1 `BindingSymbol` reassignability (computed property, no state) | `MutableCapture \|\| Binding.IsMutableDeclaration(Declaration)` (SPEC 6.6, 14.6.1, 14.8.2; a static `var` Property is a `VariableKoto`) | `MirBuilder.Match.cs:167`, `MirBuilder.Expansions.cs:107`, `MirBuilder.Places.cs:161`; and the two existing copies of the expression (`Binding.Expressions.cs:114`, `ElementAccess.cs:176`) |
| V2 Tuple element position | The publisher writes `PlanRow.Folded` on a Tuple-element Field node, as it does for a ConstIndex (`Binding.Hir.cs:112-116`, R4-M61) | `MirBuilder.Places.cs:168` |

V1 edits `BindingModel.cs` (one adv hold line, R5-M7; sequence with F2b and K2). Each view joins `PublishedBindingTypes`
with its reason if it adds a type (R4-M9); 0 B warm.

### 3.6 Q17: the requirement of a MIR problem (departure from the plan row)

| Item | Content |
| --- | --- |
| Representation | `MirProblem(DiagnosticCode Code, DiagnosticRequirement Requirement, SourceRef Subject, ulong Views)`; `DiagnosticRequirement` is a byte and a ushort (`DiagnosticRequirement.cs:17`), so the field fills existing padding (24 bytes stay 24, H) |
| Who names it | The recording rule: A1 (`MirInit` :58, :167, :215, :222, :257) `StorageLimit`, `StaticMovePathRequired`, `PossiblyMovedUse`/`UninitializedUse`, `ReassignedLet`; A2 (`MirDrops.cs:293`) `PossiblyMovedUse`; A3 (`MirLints.cs:38`) `ControlFlow`; A4: the obligation row, which already carries its code from Binding (`Binding.EffectObligations.cs:14-15`), carries its requirement beside it (`EffectBound`, `CallableEffectBound`, `VirtualEffectBound`), and `Unsupported`; A5 (`MirBorrows*`) `ComparisonLoanConflict`, `CallEffectConflict`, `DefaultArgumentAccess`, `DefaultArgumentBorrow`, `UnprovenOrigin`, `StorageLimit`, `Unsupported`; A6's PreparedInput mapping to `DefaultArgument*_Kd` (R4-M10) sets the matching `DefaultArgument*` requirement with the code it writes; construction records `ControlFlow` when H3 folds them in |
| Merge | Key unchanged (code, subject; G125 is R4's); merged problems agree on the requirement |
| Old `Binding.*` names | The old path reports some A4/A5 decisions under `Binding.VirtualEffectBound`, `Binding.IncompatibleImplementation`, `Binding.OriginRelation` (`MirDifferenceLedger.cs:56-58, 69-70`); MIR names the Ownership requirement (DIAGNOSTICS rule 1); the ledger classifies the difference (d) |
| Ledger (Q17) | The MIR side's requirement column is the problem's requirement (no longer "-", R4-M17); a pairing with different requirements is a `requirement` difference kind; `Compared` gains the MIR requirement per row and a test asserts every MIR (code, requirement) appears there (PLAN10 §7's consistency set), deleted with the ledger at H10 |
| Publication (H0) | The condition when two codes share one requirement at one subject, and the partition, stay H0's |

Fallback (only if R4 declines revised A-5): the plan's table in Diagnostics with a selector for `UnsatisfiedEffectBound_Kd`,
`IncompatibleContractImplementation_Kd`, `UnsatisfiedOriginRelation_Kd`, `UnprovenOriginRelation_Kd` and `Unsupported_Kd`,
which needs A4's obligation kind and A5's stratum as new `MirFact` kinds (absent: `Anchor`, `Use`, `Store`, `Reservation`).

### 3.7 H1: TransferRequired in Binding, with H1a

**Subject sources** (every way the builder acquires a written Place):

| Source | What MIR builds today | Old judge | H1 check |
| --- | --- | --- | --- |
| S1 consumed children | `Move` from an Adjust `(Move, _)` written by `Take`'s bare rule or the group erasure (`Binding.Hir.Adjust.cs:39, 169-199`); probe p4: `_2 = move _1`, no MIR problem | 9 of the 10 sites | Op is a value Op (not `Syntax`, `Error`), the node is a Place (`IsPlace`: bare, raw or static; not a formatting plan's storage), Adjust `Move`, parent not `ConversionKoto { Transfer }` (the one explicit Move writer, `:42`) |
| S2 unconsumed Places | An Adjust left `Unset` on an evaluated Place (a statement item, a non-value tail: `Consume`'s `Result`, `:162`) is read "by case" (`MirBuilder.Values.cs:105-108`), a `Copy` for a non-exclusive Type (`:177`) into a temp the statement drops (`MirBuilder.Drops.cs:72-80`); probe p1, p8: `copy _1` then `drop _2` and `drop _1`, a double drop, no MIR problem | `OwnershipAnalysis.cs:833-836` via the statement path (`:926`) | Op a value Op with `PlaceOf` not `Unset`/`Value`, Adjust `Unset`, `BareAcquire(type) == Move` with a proof other than Error. Defined on the columns, so it covers any consumer that leaves a Place untaken |
| S3 inferred captures | `CaptureAcquisition` never set for an omitted list (`Binding.Closures.cs:424-500`; default `Copy`, `BindingModel.cs:77-80`; pooled symbols reused without reset), so the builder Copies (`MirBuilder.Values.cs:309-321`); probe p2: `Closure(copy _1)` of a `string`, double drop; p3: a copied `uniq/i32`; p7: a copied `T` | `OwnershipAnalysis.Closures.cs:44-61` | At the end of closure binding: SPEC 7.7.2 "no list: each must be Copy": a capture whose Type is not proven Copy fails the closure with `TransferRequired`, condition = capture ordinal (the explicit-entry form); a proven one sets `Copy` explicitly |
| Others | Pattern bindings (match plan, Binding-judged), `@move` (written), defaults' copies | — | Q14 confirms none decides an unwritten Move or a non-Copy Copy |

Binding's seven existing failures stay; they fail the consumer or source, so S1 never judges those nodes twice.

**Why a scan of the published column, not a judgment at the publisher's visit.** The scan reads exactly the value the builder
reads, after every write, so last-write-wins (findings) is irrelevant; the publisher keeps deciding nothing (`Binding.Hir.cs:5-6`,
R4-M61). A judgment at the child's visit would be correct only through the pre-order invariant (all writers are ancestors),
which needs its own check, and failing a child mid-walk leaves the already written Op, Completion and `Broken` rows of its
ancestors stale, which needs the same republication anyway.

**H1a (new, behavior-preserving, D+R).** The failure must exist before `Check` counts states and collects issues
(`Binding.cs:1006-…`), but publication is lazy and needs a finished pass (`Binding.Hir.cs:35-51`, R2b-D2). H1a moves `Check`
into its own file (Binding.cs may not grow), splits it into failure producers (definition obligations, OCC paths, call and
runtime-test constraint checks, `Unvisited` → `Unresolved`, part prerequisites) and counting, and publishes every module
(library included) between them in a Final pass; `PublishHir()` then returns the pass's version without work. Production
cost after G2 is nil: the only production `Bind` caller is `Project.cs:502`, followed by `Analyze`, which from G2 builds MIR
and publishes anyway; tests that only bind pay one publication. `HirColumnsTest.PublishIsInvisible` (`:303`) becomes vacuous
and is re-pointed with its reason (publication is part of the pass; bp's HIR snapshot proves equality); `CheckBound()` repeats
producers, publication, H1 and counting.

**H1.** `CheckTransfers()` between publication and counting: one linear scan of each published module's `Ops`, `Adjusts`,
`Places` and nodes (pooled, 0 B) applying S1 and S2; S3 runs at closure binding. A failure goes through `FailAcquisition`
(`Binding.Diagnostics.cs:1253`, located at the Place, `TakeJudgment` repair). If any node failed, the modules are published
once more (failed nodes `Error`, subtrees Replaced, `Broken` recomputed); an accepted program pays the scan only.

**Soundness.** (1) Completeness: every Move or Copy of a written Place in MIR comes from the Adjust column (S1), an `Unset`
row (S2), a capture (S3), a Pattern acquisition from Binding's plan, `@move`, or a default's copy (Q14 checks the last).
(2) Agreement: the scan reads the builder's row of the same pass. (3) No dependents: a failed node is `Error`, so the builder
builds a derived Opaque value; A1 sees no Move (ARCH:228 "makes A1's StaticMovePathRequired … derived", by construction).
(4) Independent errors: from H5 MIR runs on an incomplete Binding; before H5 the old analysis would skip it, hence PD29.
(5) An `Unset` row from an Error proof (`Binding.Hir.Adjust.cs:198`) is derived, not judged. A column row saying Move where
SPEC says borrow is a column defect: Q14 finds it and the fix goes into the publisher's rule, never into H1.

**Q14 must confirm** (probe, no commit, `artifacts/verify/r6-q14/`): S1–S3 run read-only after `PublishHir()` over the
tests, the diagnostic corpus, the library and the 40 harnessed milestones, compared by (code, subject) with the old
analysis's and Binding's TransferRequired records, each difference classified (a) old miss, (b) column defect (fixed
first), (d); the library and milestones give zero records; no other builder path decides an acquisition without the column
(defaults included); probe p1–p4, p7, p8 become H1's regression tests. The MIR side of S2 and S3 needs no further proof.

**Records and deletions.** H1 deletes the 10 old report sites (`OwnershipAnalysis.cs:836, 1217`, `.Borrows.cs:447`,
`.Closures.cs:60`, `.Elements.cs:253`, `.Objects.cs:27`, `.Pointers.cs:87, 302`, `.Sequences.cs:65, 109`) and
`OwnershipRequirement.TransferRequired` (later Kind values shift; only names are published). Every TransferRequired record is
then `Binding.TransferRequired`; the old cascade after it (probe p1: `MovedPlace_Kd` at the later use) disappears.

### 3.8 Dispositions

As d2 §2.8: both files go from R0's "delete R6" (`artifacts/verify/reduction-R0/disposition.md:106, 191`) to R7.
`ClosureSignature` has 8 Binding callers plus `CloseClosureTypes` (`Binding.Structs.cs:34`) and one Emission caller;
`InstantiateRequirementCall` serves bind-time selection (`Binding.RequirementItems.cs:131`, `Binding.SelectedRequirements.cs:43`,
`Binding.Specializations.cs:164`) and `Witnesses` serves A4 (`TyCtxt.Effects.cs:40`, `MirEffects.Solve.cs:391`). K2 deletes
the never-cleared closure-context list, F5a extracts the witness resolver, H11 deletes `InstantiateForwardedCall`; the rest
goes with R7's ContractSolver and Types consolidation. Both stay PD20 hold files (R5-M7).

## 4. What becomes unnecessary; size and speed

| Removed or never built | When | Size (H) |
| --- | --- | --- |
| CFA's lint half (discard, unsafe, jump, label, fallthrough checks, repairs, `ReadsBinding`, priorities, `GuessedBy` copy, `MultiLineLiteralFinder`) | Q9–Q12 | ≈ −450 of 1,558 |
| The publisher's target resolution | Q11 | −4 |
| CFA's `UnsafeFunctionValue` copy | Q10 | −20 |
| `OwnershipFailure` (replaced) | Q7 | ±30 (+≈ 130 renamed) |
| Two copies of the reassignability expression; 17 builder sites | Q16 | −40 |
| A code → requirement table, its selectors, two new `MirFact` kinds, the `MirReport` derivation | never built (D7) | (≈ −80 against the plan's Q17) |
| The Q13 extraction unit; d2's stored `Mutable` field; a constructor view | never built | (±20, +10) |
| The 10 old TransferRequired sites, the enum member; A1 dependents at those Places | H1 | ≈ −75 |
| MIR's located `Unsupported_Kd` at invalid jumps | A-8 (R4) | small |

Added: lints ≈ +480 (Q9–Q12), H1a ±70, H1 ≈ +100 (scan, S3, republication), Q17 ≈ +60 t, Q16 ≈ +30. Net Q stream ≈ +150
until H10, which deletes CFA's remainder (≈ −1,100). Speed: Q9 adds one Final walk to Bind and takes the lint work out of
CFA's walk (Bind up, Analyze down; both measured in Q9, P3); H1a adds no production work after G2; H1's scan is linear and
0 B; republication only in rejected programs. Every new structure is pooled per pass.

## 5. Interfaces

| Other design | Q8 provides | Q8 needs | Mismatch / open |
| --- | --- | --- | --- |
| Chapter F (S3), W1 | Q16 is W1's last unit (F0 → F7 → F8 → F9 → Q16, plan §3.5); V1 is one adv line in `BindingModel.cs` | W1 open; F2b and K2's `BindingModel.cs` edits sequenced with V1 | F-design-2 lists no Q16 view; none expected |
| Chapter K (S2) | Disposition: `Binding.ClosureInstances` stays until R7 except K2's list deletion | K2 deletes `closureContexts` | none |
| F5a / PD16 | Disposition: `InstantiateRequirementCall` stays (bind-time materializer) until R7 | F5a's extraction | none |
| Chapter C (S4) | — | — | sel uses the old acceptance, which rejects S1–S3, so R5 codegen never generates those programs (no dependency on H1 before H5) |
| H0 | Each `MirProblem` carries its requirement (A-5); lint records under `Binding` with conditions 1–12; H1's design for G:R6-check-flip; TransferRequired as `Binding.TransferRequired` for H5's classification | the publication condition when codes share a requirement at one subject; the partition of MIR problems | plan rows H0 and H3 say "the published requirement derived from code and facts with Q7's kinds and Q17's table": they change to "the problem's own requirement" (open point 1) |
| G0 / R4 G103-E9b | Q11's HIR contract on null target rows | A-8 | none |

## 6. Unit mapping

| Unit | Change | Prereq | Verification | Size (H) | Review | Hold |
| --- | --- | --- | --- | --- | --- | --- |
| Q7 | §3.1; DEC entry superseding R4-M3a's naming clause | OQ10 | as plan; bp | ±30 (+≈ 130 renamed) | D+R (Q8) | adv |
| Q9 | restructuring commit (Final record report out of `Binding.cs`); `Binding.Lints.cs` contract, table, record form, `ReportDerived` condition, walk, rows 1–6, guess query moved (CFA's `RequireFallthrough` calls it); CFA rows 1–6 removed; DIAGNOSTICS "Later phases"; ARCH canonical row "HIR lints"; DEC entry | Q6, OQ10 | as plan; snapshot classified (requirement `ControlFlow` → `Binding`); warm Bind 0 B (`Purpose=Allocation`); Bind and Analyze timing | +280/−230 | G:L | no |
| Q10 | rows 7–8 with `RemoveUnsafe` through call plans; `UnsafeFunctionValue` reproducers kept as tests, CFA copy deleted; `LintErrors`, the two old-analysis reads, `LegacyRecords`; the tinyhand `ControlFlow` description | Q9 | as plan | +120/−150 | G:L | no |
| Q11 | §3.3: one validity function, target row at the jump binding, rows 9–11 in the walk, publisher resolution deleted; ARCH HIR-contract sentence | Q9 | as plan; `HirColumnsTest` target rows (an invalid jump has a null row) | +40/−45 | G:L | no |
| Q12 | §3.4: `EndArrival`, `FallthroughKey`, old derived link re-pointed; DEC entry (PD32) | Q9 | as plan; `syntax.json` `guessed-*`/`written-*` rows equal; ledger `a1-result` unchanged | +45/−30 | G:L | no |
| Q13 | closed (findings) | — | — | 0 | — | — |
| Q14 | probe per §3.7 | Q8's design review | Evid | 0 | — | no |
| Q16 | §3.5 | F0 (W1), OQ10 | as plan; bp | +30/−40 | D+R (Q8) | W1 + adv (`BindingModel.cs`) |
| Q17 | §3.6: ledger column, `requirement` difference kind, `Compared` MIR names, consistency test | Q7, A-5 (revised) | `MirDifferenceTest` requirement column; ledger re-baseline | +60 t | D+R (Q8), R4's A6 lane | adv |
| **H1a** (new) | `Check` moved and split; eager publication at the end of a Final pass; `PublishIsInvisible` re-pointed; DEC entry refining R2b-D2 | G2, OQ10 | bp (HIR snapshot equal); cold and warm Bind time and allocation recorded; `HirColumnsTest`, `MirBuildTest` | ±70 | D+R (Q8) | no |
| H1 | §3.7: `CheckTransfers` (S1, S2), S3 at closure binding, republication, 10 sites and the enum member deleted, ledger rows reclassified | H1a, Q14, Q7, R4's exit, G11, H0, OQ11 (Q13 removed) | as plan; probe p1–p4, p7, p8 and Q14's cases as regressions; `MirInitTest` | +100/−75 | final review of Q8; G:R6-check-flip | no |

G:L = Q9–Q12 (DEC Q6 form 2; no merge into dev before its review; Q11's jump part feeds A-8 and so G0). Order: Q7 and Q9
first after OQ10 (Q7 before R4 adds the requirement field, open point 2); Q10–Q12 after Q9; Q16 in W1; H1a after G2; H1 with H5.

## 7. Departures from owner-approved and recorded texts (for the owner's summary)

| Text | Departure | Reason |
| --- | --- | --- |
| PLAN10 §5.5 A3 row and §9 | FunctionFallthrough judged by Binding (PD32, already in the plan) | SPEC 14.9 makes it result validation by Structural Completion; A3's runtime reachability differs where a cleanup blocks delivery |
| Plan row Q17, rows H0 and H3, ask A-5 (R5-M1) | No code → requirement table; each MIR recording rule names its requirement | PLAN10 §7 (approved): "the decision point records the requirement … never looked up from the code"; the table re-derives a decision at a consumer (AGENTS symptom list) |
| DEC R4-M3a, two clauses | "Mir may not name `OwnershipFailure` … moving it would make old-path vocabulary shared": Diagnostics owns the kinds (Q7). "R6 unit a derives the published requirement from the code and the facts": superseded (D7) | the names are public diagnostic vocabulary (tinyhand); the stated reason ends with Q7 |
| DEC R2b-D2 ("lazily, at most once per Binding pass"; owner, as recommended) | Eager at the end of each Final pass, twice when H1 failed a node (H1a) | H1 judges the published column inside the pass; nil production cost after G2; R7's inline writes go further |
| Plan rows Q13, Q14, H1; new unit H1a | Q13 closed; Q14 depends on Q8 and probes S1–S3; H1 depends on H1a and has three sources | findings; §3.7 |
| DEC R4-M4 (TransferRequired passes through) | Ends at H1 | TransferRequired becomes Binding's on both paths |
| R0 disposition rows | `ClosureInstances`, `RequirementInstances` → R7 | definition-time callers |
| Public requirement names in `kimi check` output | 12 lint codes `ControlFlow` → `Binding` (Q9–Q12); TransferRequired `Ownership.TransferRequired` → `Binding.TransferRequired` (H1/H5) | PD31; DIAGNOSTICS rule 1 |

## 8. Items needing owner approval (inside OQ10)

- No new vocabulary entry, diagnostic code or requirement name (PD21): lint rows are condition numbers.
- New canonical component: HIR lints (ARCH canonical-component row, entry contract §3.2).
- Representations: `MirProblem.Requirement` (R4's record; revised A-5); the `BindingSymbol` reassignability view; Tuple
  element rows in `PlanRow.Folded`; the jump target row's writer moved to the jump binding.
- R2b-D2's refinement and the new unit H1a; Q13's closure.
- D7 and its fallback; the public requirement-name changes of §7.

## 9. Asks to the R4 stream

- **A-5 (revised; replaces the plan's text "Loan facts on `MirProblem` and the requirement derivation in `MirReport`").**
  In unit 1c, or the first R4 unit after Q7 merges: (1) Loan facts on `MirProblem` (A3 §6.4 fact rows), as R4 designs them;
  (2) G125, R4's choice; (3) instead of a requirement derivation, `MirProblem` gains `DiagnosticRequirement Requirement`, set
  by each recording rule as §3.6 lists (A4's obligation row carries its requirement beside its code); `MirReport` holds no
  code → requirement table and merged problems agree on the requirement. Needed by Q17, H3. If 1c merges before Q7, split:
  A-5a = (1)+(2) in 1c, A-5b = (3) right after Q7.
- **A-8 (refined).** G103-E9b's jump part: in `MirBuilder.Transfer` (`MirBuilder.Control.cs:105-117`), a Jump or Continue whose
  published target row is null is derived: its operand is still built (its independent errors stay, SPEC 23.1.2) and no record
  is made; a non-null target not built yet (for, match, defer) keeps its located `Unsupported_Kd`. Probe p11: today an `exit`
  without a target gives a MIR construction `Unsupported_Kd` beside CFA's `InvalidJumpTarget_Kd` (and an old
  `Ownership.Unsupported`). Needed by G0, after Q11.
- **A-11 (information, no work).** Until H1, MIR acceptance is unsound for bare Places: it moves a bare non-Copy local (S1),
  Copies an unconsumed non-Copy Place (S2) and Copies inferred captures (S3), double-dropping, with no MIR problem (probe), and
  TransferRequired passes the ledger uncompared (R4-M4), so no ledger row shows it. No R4 change is requested: H1 merges with
  H5 (PD29), so no dev state lets MIR decide such a program. R4's exit review should not certify bare acquisitions, and no
  MIR rule should treat a `Copy` operand as proof that its Type is Copy before H5 (a validator check would need a Copy proof,
  which the validator does not run, and would fault on old-rejected programs under `mir`).

## 10. Risks

1. Q14 may find column rows that say Move where SPEC says borrow; each is fixed in the publisher's rule before H1 (it would be
   a builder miscompile too), never excused in H1.
2. R4 may decline revised A-5 while its A6 lane is active; the fallback costs two `MirFact` kinds and a table.
3. Sequencing: R4's 1c may land before Q7; A-5's split handles it.
4. The lint walk is a second walk beside CFA until H10 (measured in Q9; P3 allows a recorded regression).
5. Eager publication changes when `HirTables.Broken` is computed: every fact-producing step must precede publication (H1a's
   bp check: HIR snapshot equal).
6. Failing a closure for an inferred capture (S3) replaces its body on the MIR side, so independent errors inside that body
   are not seen by MIR; the explicit-entry form does the same today.
7. Public requirement names change in `kimi check` JSON (12 lint codes; TransferRequired); snapshot classification per unit.
8. Q9–Q12 edit CFA under the D5 freeze (removals and one call to Binding's guess query): recorded as R6-check items landed
   early (ARCH R6 row).

## 11. Open points for the planning session

1. **Q17's form.** (a) Each recording rule names its requirement (D7, revised A-5); (b) the plan's code → requirement table
   with fact selectors. Recommend (a): PLAN10 §7, no reverse lookup, no facts added only to re-derive.
2. **Order of Q7 and R4's 1c.** (a) Q7 lands right after OQ10 (mechanical, bp) and 1c adds the field; (b) 1c first, then A-5b.
   Recommend (a), with (b) as the stated fallback in the ask.
3. **H1's subject source.** (a) Scan of the published column after H1a's eager publication, republish on failure; (b) a
   judgment at the publisher's visit. Recommend (a) (§3.7).
4. **S2's definition.** (a) On the columns (evaluated Place, Adjust `Unset`, bare acquisition Move); (b) d2's Discard Context
   predicate plus `Unset`. Recommend (a): it is exactly the builder's by-case path and covers any consumer that leaves a Place
   untaken; Q14 shows whether (a) and (b) differ.
5. **Rows 9–11.** (a) In the Final walk by the pure rule, the jump binding writing only the target row; (b) recorded at the
   jump and label bindings (d2). Recommend (a): one record source, no tentative records, CFA's coverage of unbound bodies kept.
6. **Q16's V1.** (a) Computed `BindingSymbol` view replacing the two existing copies; (b) d2's stored field set at declaration.
   Recommend (a): no state, one rule.
7. **CFA before H10.** Should CFA's flow read Binding's target rows? Recommend no (frozen code, no reader gains). Should CFA's
   structural `RequireFallthrough` call Binding's guess query? Recommend yes (no second copy).
8. **H1a's timing.** (a) Any time after G2; (b) with H1. Recommend (a): behavior-preserving, nil production cost after G2, and
   it takes the restructuring out of the G:R6-check-flip group.

## 12. Evidence (probe, 2026-10-18)

Throwaway test in a detached worktree at `b56cbf72` (`MinimalEmissionTest.Analyze`, then `BuildMir()` and `MirDump`); sources,
the test text and outputs in `bin/worktrees/r5-pkg-q8/artifacts/verify/r5-pkg-q8/` (Git-ignored; back up before removing the
worktree).

| Probe | Source (abridged) | Old path | MIR |
| --- | --- | --- | --- |
| p1 | `let text = "owned"` then statement `text` | `Ownership.TransferRequired`, then a dependent `MovedPlace_Kd` | `_2 = copy _1`, `drop _2`, later `drop _1`: double drop; no problem |
| p2 | closure without list using `text` | `Ownership.TransferRequired` at the closure | `Closure(copy _1)`; `drop _2`, `drop _1`; no problem |
| p3 | closure without list using `r: uniq/i32` | `Ownership.TransferRequired` | `Closure(copy _2)` of `uniq/i32`; no problem |
| p4 | `let b = text` | `Ownership.TransferRequired` | `_2 = move _1`; no problem |
| p5, p6 | unsafe overload group as a value | `Binding.Ambiguous` at the name | not built (Binding incomplete) |
| p7 | `func f<T>(value: T)` closure using `value` | two `TransferRequired` (capture, body) | `Closure(copy _1)` of `T`; only `StaticMovePathRequired_Kd` inside the closure |
| p8 | statement `value` of `T` | `TransferRequired` | `copy _1`, double drop; no problem |
| p9, p10 | `-> i32` body ending after `if b => return 1` | `FunctionFallthrough_Kd`, derived `UninitializedUse` | `return` with `_0` unset; no problem |
| p11 | `exit` with no target | `InvalidJumpTarget_Kd` and `Ownership.Unsupported` | construction `Unsupported_Kd` |

## 13. DEC entry after OQ10 (draft)

- **R5-Q8 R6-check prerequisite package** (owner, after the design review; OQ10, R5-M1) `draft/Sketches/R5 Step 0/Q8-package.md`
  as reviewed is the owner-approved design of DEC Q6 form 1 for Q7, Q16, Q17 and H1a, and of form 2 for G:L (Q9–Q12); H1
  takes it with the final review in G:R6-check-flip. It decides: Ownership requirement kinds in Diagnostics; one HIR lint
  component (12 rows, PD31's form, condition = row, one Final walk); the jump binding writing the target row, with the HIR
  contract on null rows; FunctionFallthrough in result validation (PD32, departing from PLAN10 §5.5/§9); Q16's two views;
  MIR problems naming their requirement (restoring PLAN10 §7; superseding R4-M3a's derivation and naming clauses);
  TransferRequired judged by a scan of the published columns with three sources after an eager Final publication (refining
  R2b-D2); `Binding.ClosureInstances` and `Binding.RequirementInstances` at R7; Q13 closed. Each component, contract change and
  superseded entry is recorded again by the unit that first implements it. Rejected: restructuring `Consume`; a judgment at
  the publisher's visit; node failures for lints; a new `BindingFailure`; a code → requirement table (fallback only if R4
  declines A-5).
