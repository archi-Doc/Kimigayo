# R6-check H8b: the control-flow entry calls and the lint reads (design)

Status: revision 0. Unit row (H0 §4, R5-M182): "H8b | 55 control-flow entry calls and 14 lint reads → lint observations; ratchet 0 |
R5 (BU reviews) | H8a, the sweep (N1), Q9–Q12 | group G:check-tests | ≈ −700 t"; H0c §2 row 3; H8a-design §4 (D1, D3, D7).

Base: `claude/r5-h10a` head `ce0b06fa1` (H10a, H8a commits 1–7). Paths: `T/` = `tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`,
`M/` = `tests/mir/`. Counts are greps on the base. This session ran no build, test, probe or run: every "HIR row equals the old value"
below is read from the code (`Binding.Hir.cs:106`, `HirVocabulary.cs`), a hypothesis until the probe of §5 runs.

Working time (Q12), logged in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\h8b\design-state.md`: revision 0 07:58–08:14 (2026-11-05), about 16 minutes of the hour.

## 1. Inventory and F13 recount (item 1)

| Read | On the base | Files |
| --- | --- | --- |
| Entry calls (ratchet row "Test calls of the old control-flow entries (R6)", limit 64) | **54** in 15 files: 53 `AnalyzeControlFlow()`, 1 `ControlFlowAnalysis.Analyze(` | RuntimeType 10, ControlFlowAnalysis 6, PatternBinding 6, CurrentControlFlow 5, DefaultCompletion 5, SpecRevisionAnalysis 4, ControlFlowConformance 3, DirectiveConditionValidation 3, OptionalTryDiscard 3, StartupBinding 3, ReferenceLayerAdaptation 2, CharLiteralParse 1, OwnershipAnalysis 1, PatternLayerOrigin 1, ReferenceBindingAssignment 1 |
| What the calls read | `Issues` (the old analysis now reports only `RequireFallthrough_Kd`, `ControlFlowAnalysis.cs:779`), `Nodes[..].CanCompleteNormally` / `Nodes.ContainsKey`, `Targets`, `PendingBinding`, `Reanalyze` (4 allocation windows, 1 API test) | — |
| Callers of the A3 twin helper `ControlFlowAnalysisTest.RequireFallthroughs` (R4-M13a: "R6 deletes its old half for its three callers") | 3 | `TheMirFindsEveryRequireFallthrough`; `T/RequireRecoveryDiagnosticTest:31, 63` (outside the ratchet's 15 files) |
| Lint reads `LintErrors` / `LintErrorRecords` in tests | **16** in 9 files; H8b takes **14 in 7** (CurrentControlFlow 5, ControlFlowConformance 2, SpecRevisionAnalysis 2, StartupBinding 2, ControlFlowAnalysis 1, RuntimeType 1, MirCensus 1) | 2 stay for H10b (D6): `X/MirTargetSide.cs:57`, `T/DiagnosticPrecisionTest.cs:315` |

F13 recount: H0's "55/15 (ratchet 64)" is **54/15** (ratchet 64). H0c's "14 reads in 7 files" holds for H8b's share; H8a's "14 become
13" counted without `MirCensusTest:125` and `DiagnosticPrecisionTest:315`.

## 2. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **H8a's rule (its D1) applies per read.** Since Q9–Q12 and H5, every rule the old control-flow analysis observed is decided elsewhere: completion is the HIR Completion column (`HirTables.CompletionOf`, from Binding's `StructuralCompletion`, defaults included), a jump's target is its plan row (`HirTables.PlanOf(jump).Plan`, `HirOp.Jump` covers return, exit and yield), `RequireFallthrough_Kd` is the MIR's (`MirLints`, `c.Problems`), and the codes it reported before Q9–Q12 are the HIR lints' 12 rows (Binding's published records; `UnsafeFunctionValue_Kd` at Binding's own sites, Q10). A read of `PendingBinding`, of `Reanalyze` or of the analysis on an unbound tree has the old analysis itself as its subject and is deleted, naming its counterpart. | One owner per fact; nothing whose subject still exists is lost. | §3 |
| D2 | **"Lint observations" = the published records** of Binding's partition after `Binding.ReportDiagnostics()`: `CompilationTestHelper.BindingErrors(c)` / `BindingWarnings(c)` (codes and spans), or `c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding)` where a predicate needs no list (MirCensusTest). `LintErrors == 0` with a complete Binding → no published Binding Error; `LintErrors == n` / `!= 0` → the published lint codes, pinned; `LintErrorRecords` contains / lacks a code → the published record at its span. | `LintErrors` is the old analysis's read (Transitional, "H10 deletes it"); the published record is the lowest public layer of a lint (SPEC 17.5, 14.3–14.5, 7.1; DIAGNOSTICS). | the 7 files |
| D3 | **The verdict where a test calls a program valid**: `Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c))` replaces `Assert.Empty(flow.Issues)` and `Assert.Empty(flow.PendingBinding)` (H8a D4's `NeverContinuationTest:446` precedent). Where a test calls it rejected (`!bound.IsComplete \|\| LintErrors > 0 \|\| flow.Issues.Count > 0`), `Assert.NotEmpty(CompilationTestHelper.PublishedErrors(c))`. Where a failed program must report nothing dependent ("no dependent result mismatch"), the published Errors are exactly the cause's record. | `IsVerified` is "no Error row", exactly or more than "no RequireFallthrough" (the old `Issues`' only code); a rejection is an explained published Error (H0b-6). A failing row is a D3 stop (§2 D8), never a weakened line. | §3 |
| D4 | **One shared helper.** `DiagnosticPrecisionTest.PublishedErrors` moves unchanged into `CompilationTestHelper.PublishedErrors` (Binding, startup and analysis reports, then the Errors), with one added line asserting the MIR exists (`c.ProgramVersion != 0`), so a caller that forgot `Analyze` fails instead of passing vacuously. DiagnosticPrecisionTest reads the shared one. | AGENTS: reuse shared helpers; a reject check without the MIR's records would miss `RequireFallthrough_Kd` rows. No new copy: the private one goes. | `X/CompilationTestHelper.cs`, `T/DiagnosticPrecisionTest.cs` |
| D5 | **Allocation windows keep their workload.** The four `flow.Reanalyze(root)` windows measure the MIR's warm analysis instead, in the established form `Measure(() => valid &= c.Analyze().IsVerified)` (with `c.Bind()` where the window rebinds); their guard reads move by D1 (`Single(invocation, Diverges)` by the HIR column). | AGENTS: allocation measurements stay; the old analysis is consumed by no product path (D-c1). A nonzero result is a finding, never relaxed. | DefaultCompletion, OptionalTryDiscard, PatternBinding, RuntimeType |
| D6 | **Two `LintErrorRecords` reads stay for H10b**: `MirTargetSide:57` (H10a Q6) and `DiagnosticPrecisionTest.AFallthroughPastAnUnresolvedExpressionIsRecordedDerived:315`, whose subject (R5-M32: the fallthrough is recorded derived, "not nothing") has no public observation: finalization suppresses a derived record whose cause is published in the same partition. H10b's Q6 decides both (keep the member as Binding's fact with a new reason, or another observation). | Moving it to the published list would lose R5-M32's rule; deleting it likewise. | none in H8b |
| D7 | **Split by method** (H8a D7's form): H8b moves or deletes, once, each method that calls the entry or reads a lint count, with every old name it uses (`ControlFlowAnalysis`, `ControlFlowIssue`, `Reanalyze`, the two `Analyze` helpers). Comments in the moved methods stop naming the old entry (H10b's zero-reference rows count comments). | One owner per method; leaves H10b a pure deletion. | §3 |
| D8 | **Moved expectations (H8a D3's kinds).** (a) the same fact at the new layer; (b) a code or location already classified, cited; (c) a lost fact: none expected (D6 keeps the one candidate). A HIR row that differs from the old value at a boundary node (a labeled `do`, a failed match's guard), or a valid program the MIR does not verify, stops that test (H8a OQ3's state): its old reads stay in that commit, the ratchet records the count, the test goes to R5 and BU. | AGENTS: never weaken an expectation. | commit messages |
| D9 | **Ratchet**: 54 → 40 → 27 → 10 → 0 commit by commit (§5); the row keeps limit 0 until H10b, which replaces it by zero-reference rows (D-c4): `\bAnalyzeControlFlow\b\|\bControlFlowAnalysis\b\|\bControlFlowIssue\b\|\bControlFlowNodeInfo\b` in `tests`, `src/Benchmark`, `src/Playground` (`\bControlFlowAnalysis\b` does not match `ControlFlowAnalysisTest`), and `LintErrors`/`LintErrorRecords` there as H10b's Q6 decides; `src/Kimi` at H11. | The ratchets' rule; R4-M13a's row ends. | `T/ArchitectureRulesTest.cs` |

## 3. Readers and fates (item 2)

Kinds: **H** = HIR column (`CompletionOf` Completes/Diverges/Unset; `PlanOf(jump).Plan`), **V** = D3 verdict, **P** = published
records (D2, D4), **M** = the MIR's problems, **G** = a MIR golden function (H8a D5), **A** = D5, **X** = deleted (D1). `Nodes.ContainsKey(n)`
→ `CompletionOf(n) != Unset` (HirColumnsTest's contract: a node the builder evaluates has a completion; Syntax and Body Ops have none).

| File | Method | Subject | Fate |
| --- | --- | --- | --- |
| ControlFlowAnalysisTest | AcceptsValidControlFlow (19) | valid transfers, requires | V (keeps `BindingErrors` empty) |
| | RejectsInvalidControlFlow (9) | each message: lint or `require` fallthrough | P: `PublishedErrors(c)` messages (rows 7–8 are the MIR's record) |
| | TheMirFindsEveryRequireFallthrough (15) + helper `RequireFallthroughs` | A3's records | M: the helper keeps its MIR half (returns `Koto[]`); old half and `Assert.Equal(old, mir)` go (R4-M13a) |
| | ResolvesYieldsBeforeClassifyingDiscardedSelections | yield targets | H: selections by `KotoTree.Walk` (pre-order, as the old keys), targets by `PlanOf` |
| | KeepsUnresolvedExhaustivenessPending… | an unresolved subject gives no coverage error | P: the published Errors are the unresolved name's record only |
| | EnvironmentDirectivesAreSelectedBefore… | the excluded line is never checked | V |
| | DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever (2 forms) | no fallthrough past an unresolved call | P: exactly the cause's record (as `DiagnosticPrecisionTest:316`) |
| | UsesBoundNeverCalls… (`LintErrorRecords`) | no `FunctionFallthrough_Kd` after a Never call | P: `BindingErrors` holds `TypeMismatch_Kd` and no `FunctionFallthrough_Kd` |
| | ResolvesHeadersOutsideTheirOwnBoundary | exit/continue targets | H (`PlanOf`) |
| | PreservesBodyFormAndAnalysisThroughSerialization | body form survives serialization | The `ControlFlowAnalysis.Analyze(restored)` run goes (X: old API on an unbound tree); the selection is found by tree walk; body-form asserts stay. Counterpart for reload-then-analyze: `CompilationTestHelper.Reload` users (e.g. `CallbackEmissionTest.ReloadAndReanalysis…`) |
| | the `Analyze(string)` helper | — | X |
| RequireRecoveryDiagnosticTest | AGuessedFailureBody…, AWrittenFailureBody… | A3 at a guessed/written body | M: the MIR half only |
| ControlFlowConformanceTest | AcceptsSpecifiedResults; RejectsInvalidResultsAndTargets (44); EffectFreeDiscard… | SPEC 14 results and targets; effect-free discard | V; D3 reject form; V |
| CurrentControlFlowTest | BindsCurrentResultRules (12), StatementCompletionAndNestedResultsBind (4) | valid result rules | V (+ `BindingErrors` empty for `LintErrors == 0`) |
| | RejectsInvalidResultContracts (9) | invalid contracts | D3 reject form |
| | UnreachableExitAndBlockedCleanupRetainTheirTypes | initializers diverge | H (Diverges) + V |
| | ReportsSpecifiedWarnings (5) | the warning; no Error | V |
| | LintWalkReusesItsStorage (Allocation) | warm lint walk, 3 Errors | P: the three published Error codes pinned before and after the window (a); the window is unchanged |
| DefaultCompletionTest | SelectedDefaults… (4), NoncompletingDefaultsSatisfyRequireFailure (3), NestedDefaultsPropagateCompletion, RecursiveDefault… | a default's divergence makes the call diverge | H: the invocation's Completes/Diverges; `Nodes[function]` → the function's `Body` row (the declaration's Op is Body, no completion); V where the method had `Issues`/`PendingBinding` |
| | WarmDefaultFlowReanalysisAllocatesNothing (Allocation) | warm reuse | A + H |
| SpecRevisionAnalysisTest | DeferredDivergenceAffectsExit…; UnreachedRegistration… | registration completes; an unreached defer blocks nothing | H (`after` Completes) + V; V |
| | BranchScopedCleanupDoesNotBlockOtherReturnPaths | a branch's diverging defer blocks only its own exits | G: the valid variant (`return 2` in the branch) as one `M/scopes` function, unless `M/scopes` or `M/control` shows it (named); the method keeps its `TypeMismatch_Kd` line. The function boundary has no HIR row |
| | helpers `Analyze`, `BindsAndFlows`, `AssertValidAnalysis` (callers: AcceptsLabeledBlockResults, AcceptsDeferredLocalTransfers, ChecksKnownUnsafe… (27), ChecksUnsafeCalls… (7), LargeUnsigned…) | Binding plus no control-flow Error | `BindsAndFlows` → `PublishedErrors(c)` empty; `AssertValidAnalysis` → Bind complete, `BindingErrors` empty, V; `Analyze` X |
| OwnershipAnalysisTest | ReanalysisClearsPreviousFlowResults | the old instance re-run on another root | X (old API; the MIR has no such instance: `Analyze` rebuilds per version) |
| DirectiveConditionValidationTest | UnknownNamesAreErrors…; LabeledBlockReportsUnknownName… | an invalid group leaves nothing to analyze | X: the reads run on unbound trees (the old skip rule); the methods' single-diagnostic and body asserts keep the subject |
| | FullyValidatedConditionsDoNotRequireBinding (3) | the selected group binds | Bind complete + V |
| RuntimeTypeTest | ObjectSemantics…, SourceOrigins…, NeverOperand… (2), ShortCircuit… (2), RequiredCleanup… | the Type operand is not evaluated; Never/cleanup operands diverge | H (`test.Right` Unset; Diverges) + V |
| | RoundTripRetainsTypeSyntaxAndNegation (2) | round trip | X: the three reads run on the unbound `c` |
| | PrefixNotKeepsItsOrdinaryPrecedence, TestsSupplyBooleanInference (2) | valid | V |
| | RebindingUsesTheCurrentAlias… | an invalidated test | X: `PendingBinding`; the method's `IsComplete` false and `RuntimeTestOf()` null keep the subject |
| | TrivialTestsRetainBothPaths… (`LintErrorRecords`) | the fallthrough | P: `FunctionFallthrough_Kd` published at `f` |
| | WarmBindingReusesRetainedTypesWithoutAllocation (3, Allocation) | warm reuse | A |
| PatternBindingTest | SupportedPatternsBindAndCheckFlow | valid | V |
| | InvalidPatternsSuppressCoverageCascades (10) | no coverage cascade | X: the message read is vacuous (the old analysis reports no coverage code); the `Binding.Issues` line keeps it |
| | CoverageHonorsGroupingAndGuards (2), GuardReturnTargetsFunction… | the guard is analyzed; return targets the function | H (`CompletionOf(guard) != Unset`; `PlanOf(return)` is the function); row 2's guard row is read under a failed match: D8 |
| | AbruptSubjectDoesNotPropagateGuardTransfers | guard exit target; match and `do` diverge | H; the `do`'s row is a D8 checkpoint |
| | WarmBindingAndFlowReusePatternStorage (3, Allocation) | warm reuse | A |
| PatternLayerOriginTest, ReferenceBindingAssignmentTest (6), ReferenceLayerAdaptationTest (2) | the 4 methods | a failed node reports nothing dependent | P: published Errors are exactly the Binding record(s) the method names |
| CharLiteralParseTest | DeclaredCharTypesBind | valid | V |
| OptionalTryDiscardTest | NeverSuccessStillChecksFailure…, DiscardedTryKeepsItsSuccessType | valid | V |
| | WarmTryBindingAndFlowReuseStorage (Allocation) | warm reuse | A |
| StartupBindingTest | StartupSelectionDoesNotPermitTopLevelReturn; CommittedCallsRetainUnsafePermissionChecks (2) | the lint Error | P: its code (`InvalidJumpTarget_Kd` / `UnsafeBlockRequired_Kd`, read from the record) |
| | WriteLineSupplies…(4); FlowUsesTheCommittedReceiverAndArgumentNodes (2) | valid; receiver and arguments evaluated, method name not | V; H |
| MirCensusTest | `BindingComplete` (`LintErrors == 0`) | Binding accepted the program (R4-M47) | P: `Result.IsComplete`, `ReportDiagnostics()`, then `!c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding)` |

**Test programs that may not parse (item 6).** `DefaultCompletionTest.NoncompletingDefaultsSatisfyRequireFailure` rows 2–3 put the
default's closers on the body's last line (`… exit to scope n)) => ()`, `… exit to scope 3)) => ()`), the shape of H8a's B3 defect
(`OuterCloserInBody_Kd`); its `CompilationTestHelper.Parse` asserts no diagnostics, and the method reads only old results. Hypothesis:
fixed as H8a's B3 (closers on their own line) with the regression that the rows' published Errors are empty. Every other reader
either parses through a helper that asserts no diagnostics (ControlFlowAnalysis, ControlFlowConformance, CurrentControlFlow,
PatternBinding, SpecRevisionAnalysis, RuntimeType, StartupBinding) or now reads `TestDiagnostics`, which includes the syntax
partition, so a hidden syntax error fails at the first run.

## 4. The split (item 4)

| Unit | Takes |
| --- | --- |
| H8b | §3; the ratchet to 0; the shared helper |
| H8c | The enums and old-type names left (H8a §4), plus the old-storage class names (H8a OQ4). H8b leaves no control-flow name in tests |
| H9 | `src/Benchmark/Benchmarks/PatternBindingBenchmark.cs:42` (`AnalyzeControlFlow()`) with the other benchmarks |
| H10b | `Compilation.AnalyzeControlFlow`, `ControlFlowAnalysis.cs`, `LintErrors` (no reader left), `LintErrorRecords` with its two readers (D6, H10a Q6), and D9's zero-reference rows |

## 5. Verification (item 5)

- **Class: Major** (tests only, 20 files, one shared helper). **Design review: yes, BU's** (WORKFLOW Design 4: the premises are
  hypotheses: the HIR rows at about 30 sites equal the old values, the MIR verifies every program the readers call valid, the four
  warm MIR analyses allocate nothing). As R4 did for H8a, the review runs one throwaway dual-read probe (old read and successor at each
  §3 site, on the base) and returns its table; BU then takes the implementation review.
- **Commits** (each builds, tests and behavior of the compiler unchanged, so no restructuring commit; phases within Q12's hour:
  H8b-1 = 1–2, H8b-2 = 3, H8b-3 = 4–6):
  1. D4's helper move (DiagnosticPrecisionTest reads it). Ratchet 54.
  2. ControlFlowAnalysisTest (with RequireRecoveryDiagnosticTest's two callers), ControlFlowConformanceTest, CurrentControlFlowTest. 54 → 40.
  3. DefaultCompletionTest (rows 2–3 fix and regression), SpecRevisionAnalysisTest (with the golden function), OwnershipAnalysisTest,
     DirectiveConditionValidationTest. 40 → 27.
  4. RuntimeTypeTest, PatternBindingTest, PatternLayerOriginTest. 27 → 10.
  5. StartupBindingTest, OptionalTryDiscardTest, CharLiteralParseTest, ReferenceBindingAssignmentTest, ReferenceLayerAdaptationTest,
     MirCensusTest; the row's limit 64 → 0. 10 → 0.
  6. Documents only if §5's list changes (none expected).
- **Unit checks**: the 19 changed test classes (§3, `DiagnosticPrecisionTest`, `ArchitectureRulesTest`); `MirDumpTest` (golden rewritten with
  `KIMI_MIR_GOLDEN=write`, only the added section differs); `MirCensusTest`; `HirColumnsTest` (unchanged, the contract D1 relies on);
  `-trait Purpose=Allocation` of the five touched classes; the build with `-warnaserror`.
- **Group Session window** (H10a's): diagnostic snapshot 0 differences (tests only); semantic snapshot unchanged; no native fixture
  changes (DefaultCompletionTest's emission fixtures are untouched), so none runs.
- **Moved expectations** (D8): kinds (a) for the H, V, P and A rows; (b) none expected; (c) none (D6). Each commit names them per test.
- **Documents (Auto)**: none. CODEMAP's rows keep the class names; ARCH, STATUS, VERIFICATION are not affected; DECISIONS: no new or
  superseded decision (R4-M13a's "R6 deletes its old half" is carried out); PLAN in R5's landing commit (position; F13's recount).
- **What BU needs**: the probe's table (old value, successor value, kind, per site); D2's pinned lint codes per row with their SPEC
  rows; the rows-2–3 fix and its failing-before regression; D6's two reads and why; the golden function's `.mir` section; the four
  allocation windows' results; the ratchet count per commit.

## 6. Sizes and limits (item 6)

| Part | Lines (t) |
| --- | --- |
| 54 entry calls and about 95 old reads | −150 |
| Successor reads (H, V, P, A) | +95 |
| Deleted: ReanalysisClears…, the two `Analyze` helpers, `RequireFallthroughs`' old half, the unbound-tree and `PendingBinding` reads | −55 |
| D4 helper (moved, +1 line), the golden function | +15, +60 generated `.mir` |

Net ≈ −200/+110 t (≈ −90 t), far from H0's ≈ −700 t: the readers' subjects (SPEC 14's rules) still exist, so D1 moves them rather
than deleting classes. No structure limit is reached; every file shrinks or stays.

## 7. Open questions for R5

| # | Question | Recommendation |
| --- | --- | --- |
| OQ1 | Accept ≈ −90 t against H0's ≈ −700 t (non-blocking) | Accept; the commit states Q5's net size, PLAN's row the recount |
| OQ2 | D6: leave `DiagnosticPrecisionTest:315` to H10b's Q6 with MirSide's read (non-blocking) | Yes |
| OQ3 | D8 stop: blocks only that test's commit (blocking only if it happens) | Yes, as H8a OQ3 |
| OQ4 | The reject theories (53 rows) keep "some published Error" (the exact successor), not a pinned code per row (non-blocking) | Keep; pinning is a separate strengthening, not H8b's |
| OQ5 | BU's design review with the dual-read probe, or the probe as the implementer's first step and one review (non-blocking) | Design review with the probe (H8a's form) |

## 8. R5's rulings (2026-11-05)

- **Class Major with one design review by BU** (H0: "R5 (BU reviews)"; WORKFLOW Design 4: unverified premises), with the dual-read
  probe (OQ5: H8a's form); the same reviewer reviews the implementation.
- **OQ1:** accepted (≈ −90 t; the subjects still exist, so readers move); the commit states Q5's net size, PLAN's row the recount.
- **OQ2:** yes (H10b's Q6). **OQ3:** yes, as H8a's OQ3.
- **OQ4:** keep "some published Error" as each reject row's successor, **and** each reject row asserts that its program parses and binds
  to the stage it names (no syntax diagnostic), so a parse error cannot satisfy it vacuously (H8a's B3 shape). Pinning a code per row
  stays a separate strengthening.
- **Suspected non-parsing rows** (DefaultCompletionTest rows 2–3 and any the scan finds): fixed as test data with a no-syntax assertion,
  as H8a's commit 8; the commit names the scan's pattern and result.
