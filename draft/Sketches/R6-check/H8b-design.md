# R6-check H8b: the control-flow entry calls and the lint reads (design)

Status: revision 1. Revision 0 got REVISE from BU's design review (B1–B5, N1–N10;
`C:\Users\bwff1\AppData\Local\Temp\claude\bu\batch\H8b\design-review.md`, with the dual-read probe in the same folder: 637 reads
equal, census 122/122). R5's rulings and the changes are in §8, marked "(B1)", "(N4)" and so on. Unit row (H0 §4, R5-M182): "H8b | 55
control-flow entry calls and 14 lint reads → lint observations; ratchet 0 | R5 (BU reviews) | H8a, the sweep (N1), Q9–Q12 | group
G:check-tests | ≈ −700 t"; H0c §2 row 3; H8a-design §4 (D1, D3, D7).

Base: `claude/r5-h10a` head `3ed4d0582` (the in-scope test files equal `ce0b06fa1`'s; H8a's commit 8 touched only CallReservationTest).
Paths: `T/` = `tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`, `M/` = `tests/mir/`. Counts are greps on the base; values marked
"probe" are BU's dual reads. This session ran no build, test or probe.

Working time (Q12), logged in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\h8b\design-state.md`: revision 0 07:58–08:14; revision 1
08:41–08:47 (2026-11-05); about 22 minutes of the hour in all.

## 1. Inventory and F13 recount (item 1)

| Read | On the base | Files |
| --- | --- | --- |
| Entry calls (ratchet row "Test calls of the old control-flow entries (R6)", limit 64) | **54** in 15 files: 53 `AnalyzeControlFlow()`, 1 `ControlFlowAnalysis.Analyze(` | RuntimeType 10, ControlFlowAnalysis 6, PatternBinding 6, CurrentControlFlow 5, DefaultCompletion 5, SpecRevisionAnalysis 4, ControlFlowConformance 3, DirectiveConditionValidation 3, OptionalTryDiscard 3, StartupBinding 3, ReferenceLayerAdaptation 2, CharLiteralParse 1, OwnershipAnalysis 1, PatternLayerOrigin 1, ReferenceBindingAssignment 1 |
| What the calls read | `Issues` (now only `RequireFallthrough_Kd`, `ControlFlowAnalysis.cs:779`), `Nodes[..].CanCompleteNormally` / `Nodes.ContainsKey`, `Targets`, `PendingBinding`, `Reanalyze` (4 allocation windows, 1 API test) | — |
| Callers of the A3 twin helper `ControlFlowAnalysisTest.RequireFallthroughs` (R4-M13a: "R6 deletes its old half for its three callers") | 3 | `TheMirFindsEveryRequireFallthrough`; `T/RequireRecoveryDiagnosticTest:31, 63` |
| Lint reads `LintErrors` / `LintErrorRecords` in tests | **16** in 9 files; H8b moves **13 in 7** | stay: `X/MirTargetSide.cs:57` and `T/DiagnosticPrecisionTest.cs:315` (H10b, D6); `T/CurrentControlFlowTest.cs:126` (a D8 stop, B3) |

F13 recount: H0's "55/15 (ratchet 64)" is **54/15**. After H8b, **4** entry calls stay as D8 stops (D8, B3): 3 under PLAN G428 and 1 under
D11's Binding row.

## 2. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **H8a's rule (its D1) applies per read.** Every rule the old control-flow analysis observed is decided elsewhere: a jump's target is its plan row (`HirTables.PlanOf(jump).Plan`; `HirOp.Jump` covers return, exit and yield), null where Binding rejected the transfer (the HIR contract, `HirColumnsTest.cs:278–289`); completion **without cleanup** is the HIR Completion column (`HirTables.CompletionOf`, from `StructuralCompletion`, defaults included, which "must not evaluate … Scope Exit cleanup", `StructuralCompletion.cs:11`), Unset for Syntax and Body Ops and under a failed match (B2); completion **through a diverging cleanup** is the MIR's (the verdict or a golden, B1); `RequireFallthrough_Kd` is the MIR's (`MirLints`, `c.Problems`); the codes the old analysis reported before Q9–Q12 are the HIR lints' 12 rows (Binding's published records). A read of `PendingBinding`, of `Reanalyze` or of the analysis on an unbound tree has the old analysis itself as its subject and is deleted, naming its counterpart. | One owner per fact; nothing whose subject still exists is lost. | §3 |
| D2 | **"Lint observations" = the published records** of Binding's partition after `Binding.ReportDiagnostics()`: `CompilationTestHelper.BindingErrors(c)` / `BindingWarnings(c)` (codes, spans and text), or `c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding)` where a predicate needs no list (MirCensusTest; probe 122/122). `LintErrors == 0` with a complete Binding → no published Binding Error; `== n` / `!= 0` → the published codes, pinned (probe: `FunctionFallthrough_Kd`, `InvalidJumpTarget_Kd`, `UnsafeBlockRequired_Kd` for LintWalk); `LintErrorRecords` contains / lacks a code → the published record at its text. | `LintErrors` is the old analysis's read ("H10 deletes it"); the published record is a lint's lowest public layer. | the 7 files |
| D3 | **Verdicts.** A program the test calls valid: `Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c))` replaces `Issues`/`PendingBinding` emptiness (H8a D4's `NeverContinuationTest:446` precedent). A program it calls rejected (`!bound.IsComplete \|\| LintErrors > 0 \|\| flow.Issues.Count > 0`): `Assert.NotEmpty(CompilationTestHelper.PublishedErrors(c))`, non-vacuous at every row because the reject helpers already assert that the rows parse (N3; probe: each publishes a substantive code). A failed program that must report nothing dependent: **the published Errors equal exactly the cause's records** (B4), with the program supplying everything else a check requires (D10); the published set is never filtered by code afterwards. | `IsVerified` is "no Error row", exactly or more than the old `Issues`; a rejection is an explained published Error (H0b-6). | §3 |
| D4 | **One shared helper.** `DiagnosticPrecisionTest.PublishedErrors` moves into `CompilationTestHelper.PublishedErrors` (Binding, startup and analysis reports, then the Errors), plus one line asserting that the MIR exists (`c.ProgramVersion != 0`), so a caller that forgot `Analyze` fails instead of passing vacuously. DiagnosticPrecisionTest reads the shared one. | AGENTS: reuse shared helpers; no second copy. | `X/CompilationTestHelper.cs`, `T/DiagnosticPrecisionTest.cs` |
| D5 | **Allocation windows rebind (B5).** Every replaced window is `Measure(() => { c.Bind(); valid &= c.Analyze().IsVerified; })`: `Analyze` without a rebind is a cache hit (`MirBuilder.cs:48–52`, `MirAnalysis.cs:66–69`), which the old `Reanalyze` was not. Probe: 0 B in all four classes (PatternBinding and RuntimeType at 1, 32 and 128). RuntimeType's `Reanalyze`-only window (`:380`) then duplicates its Bind+Reanalyze window (`:382–386`) and goes, naming it. The guard reads move by D1. | AGENTS: allocation measurements stay and are never relaxed; nothing is. | DefaultCompletion, OptionalTryDiscard, PatternBinding, RuntimeType |
| D6 | **Two `LintErrorRecords` reads stay for H10b**: `MirTargetSide:57` (H10a Q6) and `DiagnosticPrecisionTest.AFallthroughPastAnUnresolvedExpressionIsRecordedDerived:315`, whose subject (R5-M32: the fallthrough is recorded derived, "not nothing") has no public observation: finalization suppresses a derived record whose cause is published in the same partition. H10b's Q6 decides both. | Moving it to the published list would lose R5-M32's rule. | none in H8b |
| D7 | **Split by method** (H8a D7): H8b moves or deletes, once, each method that calls the entry or reads a lint count, with every old name it uses; comments in the moved methods stop naming the old entry (H10b's zero-reference rows count comments). | One owner per method. | §3 |
| D8 | **Moved expectations and stops (H8a D3's kinds, OQ3's state).** (a) the same fact at the new layer; (b) a state the program makes, explicit (B2's null/Unset rows), or a code already classified, cited; (c) a lost fact: none. **Stops (B3):** a test whose successor would pin a wrong verdict keeps all its old reads in that commit (a stopped method, not its class: the ratchet row and the commit name it, so nothing is half-moved without a record). Stopped: `CurrentControlFlowTest.StatementCompletionAndNestedResultsBind` (row `:118`, G428 (1)), `OptionalTryDiscardTest.NeverSuccessStillChecksFailureAndHasNoDiscardWarning` (`:237`, G428 (2)), `PatternBindingTest.AbruptSubjectDoesNotPropagateGuardTransfers` (G428 (3), N9), `ControlFlowAnalysisTest.DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever` (D11). H8b pins no wrong verdict. | AGENTS: never weaken an expectation; R4's defects are fixed in R4's area (G428, due before H10b). | commit messages, ratchet row |
| D9 | **Ratchet (N4).** Each commit lowers the limit to its count: 54 → 42 → 29 → 13 → **4** (§5). The row's comment names the 4 stopped methods and their removal conditions: PLAN G428 for three, D11's row for one. G428's fix unit (R4) moves its three tests to §3's successors (V, H, P) and lowers the limit; if G428 has not landed, H10b's design decides their pins (H10b's input). At 0, H10b replaces the row by zero-reference rows (D-c4): `\bAnalyzeControlFlow\b\|\bControlFlowAnalysis\b\|\bControlFlowIssue\b\|\bControlFlowNodeInfo\b` in `tests`, `src/Benchmark`, `src/Playground` (`\bControlFlowAnalysis\b` does not match `ControlFlowAnalysisTest`), `LintErrors`/`LintErrorRecords` there as its Q6 decides, `src/Kimi` at H11. | A ratchet that admits a re-added call in an intermediate commit is no ratchet. | `T/ArchitectureRulesTest.cs` |
| D10 | **The startup a program lacks (B4).** The old readers compiled through `MinimalEmissionTest.Analyze`, which runs `CheckStartup(Application)`, so no compile without the startup requirement exists for them; the programs without top-level statements gain the startup they lack, `public func main() => ()` (the form of `ReferenceLayerAdaptationTest:102` and `DiagnosticPrecisionTest:314`): PatternLayerOrigin `:31`, ReferenceBindingAssignment rows 1–3 (in their `InlineData`; rows 4–6 have top-level statements), ReferenceLayerAdaptation `:81` and `:91`. Then the published Errors are exactly the Binding record the method names. | B4: `MissingStartupBody_Kd` is no record of the subject. | the 3 files |
| D11 | **DefersCoverage form 2** (`let x = if true\n    abort()\nelse => 1`) publishes `[UnresolvedBinding_Kd 'abort', TypeMismatch_Kd '1']` (probe). Classification: a **Binding defect candidate**. Hypothesis: the join's check at `1` reads the then-branch's Type, which rests on the unresolved call's completion, so SPEC 23.1.2 makes it dependent and it should be derived. Form 1 (`func f() -> i32\n    abort()`) gives the cause alone. The method stops whole (D8) until BU classifies it: (b) with an independence reason, then H8b's form pins both records; or a defect, then a PLAN row (R5 files it with BU) is its removal condition. `RejectsInvalidControlFlow` row 1's `TypeMismatch_Kd '1'` beside `UnlabeledYieldTarget_Kd` (probe, "info") is the same question, and that reject row is unaffected. | B4: classify before pinning; never pin a wrong verdict. | `T/ControlFlowAnalysisTest.cs` |

## 3. Readers and fates (item 2)

Kinds: **H** = HIR column (`CompletionOf` Completes/Diverges/Unset; `PlanOf(jump).Plan`), **V** = D3 verdict, **P** = published
records (D2, D3, D4), **M** = the MIR's problems, **G** = a MIR golden function (H8a D5), **A** = D5, **X** = deleted (D1), **S** = D8
stop. `Nodes.ContainsKey(n)` → `CompletionOf(n) != Unset`. Probe-equal rows are (a).

| File | Method | Subject | Fate |
| --- | --- | --- | --- |
| ControlFlowAnalysisTest | AcceptsValidControlFlow (19) | valid transfers, requires | V (keeps `BindingErrors` empty) |
| | RejectsInvalidControlFlow (9) | each message | P: `PublishedErrors(c)` messages |
| | TheMirFindsEveryRequireFallthrough (15) + `RequireFallthroughs` | A3's records | M: the helper keeps its MIR half; old half and `Assert.Equal(old, mir)` go (R4-M13a) |
| | ResolvesYieldsBeforeClassifyingDiscardedSelections | both selections are discarded, so both yields have no target | Selections by `KotoTree.Walk` (pre-order, the same objects, probe); **B2:** H asserts `PlanOf(yield).Plan` is null at both yields, and P pins the two `UnlabeledYieldTarget_Kd` records at their text (`yield 1`, `yield 2`) beside the existing "discarded selection" line. Kind (b): the old target (the inner `if`) was the old analysis's resolution of a transfer SPEC 14.9 makes invalid; the HIR contract gives it a null row, so no fact is lost. Valid nested yields' targets stay observed by `HirColumnsTest` and `ResolvesHeaders…` |
| | KeepsUnresolvedExhaustivenessPending… | an unresolved subject gives no coverage error | P: exactly `[UnresolvedBinding_Kd]` at `value` |
| | EnvironmentDirectivesAreSelectedBefore… | the excluded line is never checked | V |
| | DefersCoverageWhenAnUnresolvedCallCouldHaveTypeNever | no fallthrough past an unresolved call | **S** (D11); the `Analyze(string)` helper stays for it alone |
| | UsesBoundNeverCalls… (`LintErrorRecords`) | no `FunctionFallthrough_Kd` after a Never call | P: `BindingErrors` holds `TypeMismatch_Kd` and no `FunctionFallthrough_Kd` (probe: the mutant `-> i32 => 1` publishes it) |
| | ResolvesHeadersOutsideTheirOwnBoundary | exit/continue targets | H |
| | PreservesBodyFormAndAnalysisThroughSerialization | body form survives serialization | X for the `ControlFlowAnalysis.Analyze(restored)` run (old API, unbound tree); selection by tree walk; body-form asserts stay |
| RequireRecoveryDiagnosticTest | AGuessedFailureBody…, AWrittenFailureBody… | A3 at a guessed/written body | M: the MIR half only |
| ControlFlowConformanceTest | AcceptsSpecifiedResults (18); RejectsInvalidResultsAndTargets (44); EffectFreeDiscard… (4) | SPEC 14 results and targets | V; D3 reject form; V |
| CurrentControlFlowTest | BindsCurrentResultRules (12) | valid result rules | V + `BindingErrors` empty |
| | StatementCompletionAndNestedResultsBind (4) | valid result rules | **S** (G428 (1): row `:118` gets the validator's Type finding as `InternalInvariant_Kd`); keeps its `LintErrors` read |
| | RejectsInvalidResultContracts (9) | invalid contracts | D3 reject form |
| | UnreachableExitAndBlockedCleanupRetainTheirTypes | `a` diverges (loop); `b` cannot complete through its diverging `defer` | `a`: H Diverges; **B1** `b`: G: one valid function in `M/scopes`, `blockedExit()` = `let b = label work: do` / `defer => loop => ()` / `exit to work 2` (no read of `b` after it). Its MIR fact: the `exit to work` path enters the deferred `loop`, and the code after the labeled `do` is reached only through `unreachable check bbN` (the check-only continuation; probe dump). The Types line stays; V for the program |
| | ReportsSpecifiedWarnings (5) | the warning; no Error | V |
| | LintWalkReusesItsStorage (Allocation) | warm lint walk, 3 Errors | P: the three codes pinned before and after the window; the window is unchanged |
| DefaultCompletionTest | SelectedDefaults… (4), NestedDefaultsPropagateCompletion, RecursiveDefault… | a default's divergence makes the call diverge | H (invocation Completes/Diverges); `Nodes[function]` → `CompletionOf(function.ExpressionBody)` (N1: `f` is expression-bodied, `Body` is null; Completes in all 4 rows); V where the method had `Issues`/`PendingBinding` |
| | NoncompletingDefaultsSatisfyRequireFailure (3) | a noncompleting default satisfies SPEC 14.11's require failure | **B1:** V for all rows. Its MIR fact: no `RequireFallthrough_Kd` at the failure body `f()`, A3's `MustNotComplete` row holding through the inlined default (probe: the mutant `defer => ()` publishes it there). The H line goes: the column excludes row 3's cleanup divergence, and the call's completion for a diverging default is SelectedDefaults…' H. **Rows 2–3 fixed** (`OuterCloserInBody_Kd`, N2): closers on their own line, as H8a's commit 8 |
| | WarmDefaultFlowReanalysisAllocatesNothing (Allocation) | warm reuse | A + H |
| | class helper `Parse` | — | **N2:** `CompilationTestHelper.ParseSuccess` in place of `Parse`, so no row binds or verifies without parsing; the regression: rows 2–3 fail `ParseSuccess` before the fix (`OuterCloserInBody_Kd` at `)`) and pass after |
| SpecRevisionAnalysisTest | DeferredDivergenceAffectsExit…; UnreachedRegistration… | registration completes; an unreached defer blocks nothing | H (`after` Completes: registration, not cleanup) + V; V |
| | BranchScopedCleanupDoesNotBlockOtherReturnPaths | a branch's diverging defer blocks only its own exits | G: the valid variant (`return 2` in the branch) as one `M/scopes` function, `branchBlockedExit(flag: bool) -> i32`; the method keeps its `TypeMismatch_Kd` line |
| | helpers `Analyze`, `BindsAndFlows`, `AssertValidAnalysis` (callers' rows: 8, 6, 27, 7, 2) | Binding plus no control-flow Error | `BindsAndFlows` → `PublishedErrors(c)` empty (probe 34/34); `AssertValidAnalysis` → Bind complete, `BindingErrors` empty, V (16/16); `Analyze` X |
| OwnershipAnalysisTest | ReanalysisClearsPreviousFlowResults | the old instance re-run on another root | X (old API; `Analyze` rebuilds per version) |
| DirectiveConditionValidationTest | UnknownNamesAreErrors… | an invalid group leaves nothing to analyze | X: the read runs on an unbound tree; the single-diagnostic and no-generated-function asserts keep the subject |
| | LabeledBlockReportsUnknownName… | the unknown name changes no control flow | **N5:** bound and analyzed, P: exactly `[UnknownCompileTimeName_Kd 'missing']`, and `Single(body.Items)` stays |
| | FullyValidatedConditionsDoNotRequireBinding (3) | the selected group binds | Bind complete + V |
| RuntimeTypeTest | ObjectSemantics…, SourceOrigins…, NeverOperand… (2), ShortCircuit… (2) | the Type operand is not evaluated; a Never operand diverges | H (`test.Right` Unset; Diverges) + V |
| | RequiredCleanupCanStopAValidObjectTypedOperand | an object-typed operand stopped by its diverging `defer` | **B1:** G: `blockedExit()` (above), the same rule with an operand consumer; its MIR fact as above. V for the program; the Type and runtime-test lines stay. The runtime test over a Never operand stays NeverOperand…'s H |
| | RoundTripRetainsTypeSyntaxAndNegation (2) | round trip | X: the three reads run on the unbound `c` |
| | PrefixNotKeepsItsOrdinaryPrecedence, TestsSupplyBooleanInference (2) | valid | V |
| | RebindingUsesTheCurrentAlias… | an invalidated test | X (`PendingBinding`); `IsComplete` false and `RuntimeTestOf()` null keep the subject |
| | TrivialTestsRetainBothPaths… (`LintErrorRecords`) | the fallthrough | P: `FunctionFallthrough_Kd` at the falling statement, text `if x is Dog => return 1` (N7) |
| | WarmBindingReusesRetainedTypesWithoutAllocation (3, Allocation) | warm reuse | A; the `Reanalyze`-only window goes (D5) |
| PatternBindingTest | SupportedPatternsBindAndCheckFlow (29) | valid | V |
| | InvalidPatternsSuppressCoverageCascades (10) | no coverage cascade | X: the message read is vacuous; the `Binding.Issues` line keeps the rule (N6 declined, §8) |
| | CoverageHonorsGroupingAndGuards (2) | the coverage state | **B2:** the guard line goes (X). Row 1 has no guard (the old read was vacuous and `CompletionOf(Guard!)` would throw); row 2's guard is Unset under the failed match (Op Error), the old analysis's pending set having no successor. The coverage-state line is the subject and stays |
| | GuardReturnTargetsFunction… | the guard's return targets the function | H target (FunctionKoto, probe); **B2:** the match fails (`NonExhaustiveMatch_Kd`), so H asserts the guard `return 1` is Unset (Op Syntax), kind (b); the unsafe-guard lines stay |
| | AbruptSubjectDoesNotPropagateGuardTransfers | guard exit target; match and `do` diverge | **S** (G428 (3): `UninitializedPlace_Kd` at `result`, N9); the H reads were equal, G428's fix moves them |
| | WarmBindingAndFlowReusePatternStorage (3, Allocation) | warm reuse | A |
| PatternLayerOriginTest | AnExclusiveLayerBelowASharedOneKeepsTheOuterDependency | no dependent result mismatch | P exact `[UnprovenOriginRelation_Kd]` after D10's startup |
| ReferenceBindingAssignmentTest | ValueAssignmentsNameTheBindingMode (6) | no dependent mismatch | P exact `[code]`; rows 1–3 gain D10's startup, rows 4–6 are exact as they stand (probe) |
| ReferenceLayerAdaptationTest | DictionaryIndexing… (`:81`), IndexingThroughSeveralLayers… (`:91`) | one boundary record | P exact (the Binding records the method names) after D10's startup |
| CharLiteralParseTest | DeclaredCharTypesBind | valid | V |
| OptionalTryDiscardTest | NeverSuccessStillChecksFailure… | valid | **S** (G428 (2): `UninitializedPlace_Kd` at `$try.value`) |
| | DiscardedTryKeepsItsSuccessType | valid | V |
| | WarmTryBindingAndFlowReuseStorage (Allocation) | warm reuse | A |
| StartupBindingTest | StartupSelectionDoesNotPermitTopLevelReturn; CommittedCallsRetainUnsafePermissionChecks (2) | the lint Error | P: the published code (probe-equal); **N8:** `:355`'s `PendingBinding` read → V (holds in both rows: lint Errors are Binding's, not MIR rows) |
| | WriteLineSupplies… (4); FlowUsesTheCommittedReceiverAndArgumentNodes (2) | valid; receiver and arguments evaluated, method name not | V; H |
| MirCensusTest | `BindingComplete` | Binding accepted the program (R4-M47) | P: `Result.IsComplete`, `ReportDiagnostics()`, `!c.Diagnostics.HasErrorsIn(DiagnosticPartition.Binding)` (probe 122/122) |

**Test programs that do not parse (item 6).** DefaultCompletionTest rows 2–3 (`NoncompletingDefaults…`, probe: `OuterCloserInBody_Kd` at
`)`, and `IsVerified` true for both, so V alone would not catch it): fixed, with the class helper's `ParseSuccess` as the check (N2).
BU's scan found no other in-scope reader that parses through a non-asserting path except MinimalEmissionTest's V sites, whose
programs parse; the reject theories' helpers already assert parsing (N3).

## 4. The split (item 4)

| Unit | Takes |
| --- | --- |
| H8b | §3; the ratchet to 4; the shared helper; the two golden functions |
| G428's fix (R4, before H10b) | The three G428 stops: their successors as §3 names them (V, H; NeverSuccess V; AbruptSubject H and V) and the ratchet limit; the CurrentControlFlow `LintErrors` read with StatementCompletion's move |
| D11's row (BU) | DefersCoverage: (b) pins both records, or the fix, then §3's P |
| H8c | The enums and old-type names left (H8a §4); H8b leaves no control-flow name in moved methods |
| H9 | `src/Benchmark/Benchmarks/PatternBindingBenchmark.cs:42` |
| H10b | `Compilation.AnalyzeControlFlow`, `ControlFlowAnalysis.cs`, `LintErrors`, `LintErrorRecords` with its two readers (D6); any stop still open (its input names them, D9); D9's zero-reference rows |

## 5. Verification (item 5)

- **Class: Major.** BU's design review ran (REVISE, this revision answers it); BU takes the implementation review, which first checks
  B1–B5 (WORKFLOW Repeated reviews 1) and uses the probe as the checklist.
- **Commits** (the compiler unchanged; phases within Q12's hour: H8b-1 = 1–2, H8b-2 = 3, H8b-3 = 4–5):
  1. D4's helper move. Limit 64 → 54.
  2. ControlFlowAnalysisTest (with RequireRecoveryDiagnosticTest's two callers), ControlFlowConformanceTest, CurrentControlFlowTest
     (with `blockedExit`). 54 → **42** (DefersCoverage's helper and StatementCompletion stay).
  3. DefaultCompletionTest (rows 2–3, `ParseSuccess`), SpecRevisionAnalysisTest (with `branchBlockedExit`), OwnershipAnalysisTest,
     DirectiveConditionValidationTest. 42 → **29**.
  4. RuntimeTypeTest, PatternBindingTest, PatternLayerOriginTest. 29 → **13** (AbruptSubject stays).
  5. StartupBindingTest, OptionalTryDiscardTest, CharLiteralParseTest, ReferenceBindingAssignmentTest, ReferenceLayerAdaptationTest,
     MirCensusTest; the row's comment names the four stops. 13 → **4** (NeverSuccess stays).
- **Unit checks**: the 19 changed test classes; `MirDumpTest` (goldens rewritten with `KIMI_MIR_GOLDEN=write`, only the two added
  sections differ), `MirCensusTest` (the goldens have no problem), `HirColumnsTest`, `ArchitectureRulesTest`; `-trait Purpose=Allocation`
  of the five touched classes; the build with `-warnaserror`.
- **Group Session window** (H10a's): diagnostic snapshot 0 differences (tests only); semantic snapshot unchanged; no native fixture
  changes.
- **Moved expectations** (D8): (a) for the probe-equal rows; (b) for B2's null/Unset rows and the D10 rows' exact sets; (c) none.
- **Documents (Auto)**: none (CODEMAP keeps the class names; ARCH, STATUS, VERIFICATION unaffected). DECISIONS: none new or superseded.
  PLAN in R5's landing commit: position, F13's recount, G428's added cases (R4's row) and D11's row if BU finds a defect.
- **What BU needs**: the probe's checklist rows per commit; D2's pinned codes and texts; the two golden functions' `.mir` sections; the
  rows-2–3 fix and its regression; D10's startup per row; the four stops and the ratchet comment; D11's classification question.

## 6. Sizes and limits (item 6)

Net ≈ −190/+125 t (≈ −65 t), plus ≈ +120 generated `.mir` data for the two golden functions. The 4 stopped methods keep their ≈ 25
old lines. This is far from H0's ≈ −700 t: the readers' subjects (SPEC 14's rules) still exist, so D1 moves them. No structure limit
is reached.

## 7. Open questions for R5

| # | Question | Recommendation |
| --- | --- | --- |
| OQ1 | Accept ≈ −65 t against H0's ≈ −700 t (non-blocking) | Accept; the commit states Q5's net size |
| OQ2 | D6: leave `DiagnosticPrecisionTest:315` to H10b's Q6 (non-blocking) | Yes |
| OQ3 | D11: R5 asks BU to classify DefersCoverage form 2 (and RejectsInvalidControlFlow row 1's same pair); file a PLAN row with BU if it is a defect (non-blocking for H8b: the method is a stop) | Ask BU with G428's report |
| OQ4 | Stops by method, not by class (D8) (non-blocking) | By method: the ratchet comment records each, so no class is half-moved without a record, and 4 entry calls stay instead of 15 |

## 8. R5's rulings on BU's design review (REVISE, 2026-11-05; revision 1)

- **B1:** D1 restated: the HIR Completion column holds completion without cleanup (`StructuralCompletion.cs:11`). The three
  cleanup-divergence readers move to the MIR's fact: CurrentControlFlowTest `:112` field `b` and RuntimeTypeTest `:297` to the golden
  `blockedExit()` (the exit enters the deferred `loop`; what follows is reached only through `unreachable check`); DefaultCompletionTest
  `:42`'s method to V (no `RequireFallthrough_Kd` at `f()`), its H line gone.
- **B2:** ResolvesYields asserts the null target rows and the two `UnlabeledYieldTarget_Kd` records (kind (b), no lost fact);
  GuardReturn asserts the guard's Unset under the failed match; CoverageHonors' guard line goes in both rows (row 1 vacuous, row 2
  without a successor). No successor dereferences a null guard.
- **B3, with R4's PLAN G428** (dev `afb666d2d`, R4's, due before H10b): its three cases are D8 stops (StatementCompletion…, NeverSuccess…
  and, by the addition, AbruptSubject…, N9). They keep their old reads; the ratchet row records the residual and names G428 as their
  removal condition; H8b pins no wrong verdict; H10b's input names them.
- **B4:** D3's exact form never filters by code; D10 adds the startup each row lacks (the old tests compiled with the startup
  requirement, so no class helper without it exists); DefersCoverage form 2 is classified a Binding defect candidate and stops (D11).
- **B5:** D5: every window is Bind+Analyze; RuntimeType's `Reanalyze`-only window goes, naming its Bind+Reanalyze successor.
- **DefaultCompletionTest rows 2–3:** fixed to parse, and the class helper becomes `ParseSuccess` (N2).
- **Non-blocking:** N1 (`ExpressionBody`), N2, N3 (OQ4 closed: the reject form stays, citing the helpers' parse assertions), N4
  (per-commit limits), N5 (LabeledBlock's P), N7 (the pin text), N8 (CommittedCalls' V) and N9 (AbruptSubject in G428) are adopted.
  **N6 is declined**: InvalidPatterns' `Binding.Issues` line already states the rule, so a published-list line would be a second
  assertion of one rule in one method. N10 needs no change.
- **Beyond the review:** D11 adds a fourth stop (1 entry call), so the ratchet ends at 4, not "at most 8".

### R5's rulings after BU's re-check 1 (ACCEPT, 2026-11-05)

- **OQ3 (BU's classification):** DefersCoverage form 2's `TypeMismatch_Kd` is a Binding defect (a cascade on a resting input, SPEC
  23.1.2; `ResultStructure()` computes the then-body's completion without the guess hook), recorded as PLAN G52 (dev b98e3f068, owner BU).
  D11 stays a D8 stop with G52 as its removal condition; the TypeMismatch is not pinned.
- **R1-N1:** adopted if it fits the phase: a `blockedTest` golden makes RuntimeTypeTest:297 exact (otherwise it stays observed by
  composition, and the commit says so). **R1-N2:** adopted: GuardReturn's failed-match read keeps a valid variant (`true => 4`) as kind
  (a), so CoverageHonors' deletion loses no subject. **R1-N3:** noted (RejectsInvalidControlFlow row 1 is not D11's question).
