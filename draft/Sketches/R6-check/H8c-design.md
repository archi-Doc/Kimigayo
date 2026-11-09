# R6-check H8c: the old analysis's enums and type names left in tests (design)

Status: revision 1. Revision 0 got REVISE from R4's design review (B1, B2, N1–N6; probe at 3ed4d0582,
`…\r4-notes\post-exit\review-h8c-design.md`). R5's rulings are in §8, and the changes are marked "(B1)", "(N3)" and so on. Unit row
(H0 §4, R5-M182): "H8c | Analysis-declared enums and old-type names in tests | R5 (R4 reviews) | H8b, the sweep (N1) | group
G:check-tests | ≈ −1,100 t"; H0c §2 row 4; H8a-design §4 (D7, OQ2, OQ4); H8b-design §4.

Base: `claude/r5-h10a` head `3ed4d0582` (H10a, H8a commits 1–8, H8b designed), read as if H8b were implemented as designed. Paths:
`C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`. Counts are `git grep -w` on the base over the 50
non-private types the 48 files of `C/Analysis/` declare (namespace `Kimi.Compiler`; no other `src/Kimi` file declares one of these names).
This session ran no build, test, probe or run; the numbers in §5 are R4's probe.

Working time (Q12), logged in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\h8c\design-state.md`: revision 0 08:16–08:28; revision 1
08:38–08:43 (2026-11-05); about 17 minutes of the hour in all.

## 1. Inventory and F13 recount (item 1)

| Use | On the base | Taken by |
| --- | --- | --- |
| Old type names in tests | 22 lines in 8 files: SharedEngineTotalityTest 9, PipelineSwitchTest 3, ControlFlowAnalysisTest 2, OwnershipStorageLimitTest 2, EnumBindingTest 2, `X/CompilationTestHelper` 2 (comments), MirInitTest 1 (comment), SpecRevisionAnalysisTest 1 | H8b: ControlFlowAnalysisTest `:171`, `:219`, SpecRevisionAnalysisTest `:276` (its §3 X rows). **H8c: 19 lines in 6 files** |
| Of them, Analysis-declared enums | 7 enums (`OwnershipOperationKind`, `OwnershipValueKind`, `OperationFlow`, `ValueFlow`, `OwnershipPlaceKind`, `OwnershipEdgeKind`, `AcquisitionKind`) on 7 lines in 2 files (SharedEngineTotalityTest 5, EnumBindingTest 2) | H8c |
| Entry reads left after H8a | `AnalyzeLegacyOwnership()` 3 bare calls in `OriginObligationVerdictTest:28, 33, 39` (H8a §4 put them at H10b); `PipelineSwitchTest:347-349` (the entry's own test) | H8c takes the first (D5); the second is H10b's |
| Class and method names after old storage or the old analysis | `SharedEngineTotalityTest` (G75's old "shared engine"), `BorrowDependencyStorageTest` (the old borrow-dependency table) and its methods `DependentBodiesUseCompactTables`, `InspectionOnlyBodiesNeedNoLocalDependencyTable` (N4), `CallableRegionScalingTest` (the old local-region storage), `OwnershipAnalysisTest.AllocationTests.WarmBindingAndBothAnalysesReuseStorage` ("both" = the old ownership and control-flow analyses), `TypeDagScalingTest.OldAnalysisWalksAreLinearInDistinctParts` (N4) | D6 |
| Comments stating a current fact about the old analysis (false since H5) | 14 lines in 10 files (§3, D7). Another ≈ 95 lines in ≈ 60 files name the old analysis in the past tense as the provenance of a moved expectation (Q13 citations); no zero-reference row matches them | D7 |
| `src/Benchmark` | 5 type-name lines in 3 files; the entry read in 9 files (§4) | H9 |
| `src/Playground` | none | — |

F13 recount: H0's "H8c 114 methods/52 files" and H0c's "16 enums in 10 files, 22 old-type names in 9 files" counted before H8a, which
moved every method reading `Bodies`/`Issues` with its enums (H8a D7). Left for H8c: **7 enums on 7 lines in 2 files; 19 type-name lines
in 6 files (6 methods, 3 comments)**, plus one method's entry reads, 6 names and 14 comment lines.

**Finding for H10b (src/Kimi, not H8c's; R4 confirmed it, N6).** H0c §1's reference graph lacks two old-only readers that H10b's
deletion of `C/Analysis/OwnershipModel.cs` breaks:
- `ElementAccess.SupportsBorrowRoot`/`SupportsMoveRoot(OwnershipPlace)` (`C/ElementAccess.cs:10-15`), read only by
  `OwnershipAnalysis.Elements.cs:68, 75, 251` and `OwnershipBody.Comparisons.cs:363`;
- `BoundEnumConstruction.Acquisitions`/`acquisitions`/`SetAcquisition` (`C/Binding/Binding.Enums.cs:32, 40, 56, 63`), typed by the old
  `AcquisitionKind`, written at `:629` and read only by `OwnershipAnalysis.Enums.cs:294` and EnumBindingTest `:72-73` (which D3 removes).

No Benchmark or other reader exists. H10b deletes both, with BU's consent for the Binding hunk (OQ3).

## 2. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **H8a's rule (its D1), per use.** A use whose subject still exists moves to the MIR's, the HIR's or the published vocabulary; a use whose subject is the old analysis (its tables, engine, storage arithmetic, verdict counter) is deleted and its commit names the counterpart, or states that none exists. | One owner per fact; nothing whose subject exists is lost. H10b then deletes code only. | §3 |
| D2 | **The multi-code helpers' image check moves to the census.** `PipelineSwitchTest.TheMultiCodeHelpersAreTheOwnershipMapping` and its `OwnershipCodes()` (its one caller) go. `MirCensusTest` gains `TheMultiCodeHelpersAreTheCensusImages`: over `Enum.GetValues<DiagnosticCode>()`, `IsLoanConflict()` holds exactly for `Pairs`' codes of `Ownership(ComparisonLoanConflict)` (4, `MirCensusTest.cs:41-44`) and `IsUnprovenOrigin()` exactly for those of `Ownership(UnprovenOrigin)` (2, `:47-48`; BU-85). `X/CompilationTestHelper.cs:86, 90` cite "the census pairs (`MirCensusTest.Pairs`)" instead of `OwnershipIssue.Code`. | The helpers' contract is "the codes one requirement publishes"; since H5 the MIR's records publish it and the census table owns that mapping (H10a D6 moved its consistency checks there). The old image is the old analysis's (H10a D6: "the image half goes"). | `T/PipelineSwitchTest.cs`, `T/MirCensusTest.cs`, `X/CompilationTestHelper.cs` |
| D3 (B2) | **EnumBindingTest's acquisitions move to the HIR, on the operand.** `PayloadOperationsKeepOrderAndAcquisition`'s two `AcquisitionKind` lines become `Assert.Equal(HirAcquire.Move, HirTables.AdjustOf(((ConversionKoto)invocation.ArgumentNodes[i]).Left).Acquire)` for both payloads. The fact pinned: each payload operand Place's own acquisition, the explicit `@move` the MIR builder reads (SPEC 3.5, 6.5.2, 13.5.3), the column `MirInitTest.ANeverPlaceIsAcquiredAsBindingPublishes` reads (`:330`). The generic `a`'s old `CopyOrMove` is that Move (R4's probe: `Move` for `a` and `b`). The argument node itself (the conversion, op Group) has `Temporary`, its materialization, a different fact not pinned here. The order and parameter-index lines stay. | `BoundEnumConstruction.Acquisitions` is read only by the old analysis; the payload acquisition (the subject) is the HIR's, at the operand (AGENTS: the lowest public layer that observes the rule). | `T/EnumBindingTest.cs` |
| D4 (N3) | **Old engine tests go.** `SharedEngineTotalityTest.EveryKindHasAFlowRow` (E4, the old flow table): counterpart `MirValidatorTest.EveryKindHasADescriptorRow`. `LivenessConsumesCleanupFactsAndDistinguishesAbort` (`OwnershipBody`, `PackedAnalysisTable`, `BorrowLiveness` built by hand) has three parts, each with its row: a Drop that observes Loans keeps them → `MirBorrowsTest.ADestructionRetainsAsItsDropsCall` row `localBad` (Writer's drop); one that does not, does not → the same theory's row `quietOk` (Quiet, the same Fields without a drop), with `MirTypesTest.DestructionObservesUserDropsAndUnknownParts` for the observation fact; an abort runs no cleanup → `MirEffectsTest.ACheckOnlyDropRunsNothing` (R4-M6a: a Drop only the checks reach, past an Abort, runs no destructor). `OwnershipStorageLimitTest.OversizedProductsReportResourcesBeforeAllocationOrOverflow` (`OwnershipStorage.Cells`): its limit boundary's counterpart is `MirOriginsTest.Unknown.TheArenaIsAdmittedByTheAnalysesLimit` (`:224-231`); its overflow-before-allocation part has **no MIR counterpart** (`MirOrigins.ArenaWords` computes in `long`), and the commit says so. | Their subject is the old analysis's representation (D1). | `T/SharedEngineTotalityTest.cs`, `T/OwnershipStorageLimitTest.cs` |
| D5 (N5) | **OriginObligationVerdictTest's counting half goes at H8c** (moved from H10b, H8a §4). `EachObligationIsJudgedOncePerAnalysis` loses its 3 `AnalyzeLegacyOwnership()` calls, the `count` line and the 3 `metrics.Verdicts` assertions (fed only by `OwnershipAnalysis.Borrows.cs:77`); it keeps `Accepts` and the family's one record, before and after the rebind and re-analysis, and is renamed `EachFamilyKeepsItsOneRecordAcrossARebind`. It gains one line asserting no syntax diagnostic (R5's H8b OQ4 ruling; its helper `Analyze` asserts none). The class summary and the method comment drop the old verdict count. "Each obligation is judged once per analysis" was the old counter's rule and has **no MIR counterpart**; the commit says so. `Binding.OriginProofMetrics.Verdict()`/`Verdicts` become dead: H11's list (BU). | Its subject is the old analysis's counter (D1); taking it now leaves H10b only the entry's own test (item 4). | `T/OriginObligationVerdictTest.cs` |
| D6 (N4) | **Names (H8a OQ4).** A class or method named after old storage or an old fact is renamed to its subject; a name that is also a SPEC term or a published code stays. **Renamed:** `SharedEngineTotalityTest` → `LoanHolderTotalityTest` (its rows: a root's Loan reaches every holder, SPEC 13.5.5, 15.6.3, 15.8.2; the class comment says A5's records); `BorrowDependencyStorageTest` → `InspectionLoanTest` (its workload `VerificationWorkloads.InspectionLoans`, the inspection extent), with `DependentBodiesUseCompactTables` → `AWarmAnalysisOfStoredInspectionsAllocatesNothing` and `InspectionOnlyBodiesNeedNoLocalDependencyTable` → `AWarmAnalysisOfInspectionsAllocatesNothing` (since H8a they assert only the MIR's zero-allocation reruns); `WarmBindingAndBothAnalysesReuseStorage` → `WarmBindingAndAnalysisReuseStorage`; `TypeDagScalingTest.OldAnalysisWalksAreLinearInDistinctParts` → `WalksAreLinearInDistinctParts` (its comment, D7, states the MIR's walks). **Kept:** `CallableRegionScalingTest` (it names its workload family `VerificationWorkloads.CallableRegions`, its 18 fixture ids, the benchmark axes of `CallablePlans.md`, and the R7 plan's scaling-class list, and D8 makes it a scaling check again); `OwnershipAnalysisTest`, `ControlFlowAnalysisTest` (SPEC 15's and 14's "analysis"; the zero-reference regexes `\bOwnershipAnalysis\b`, `\bControlFlowAnalysis\b` do not match them); `EnumOwnershipTest`, `MatchOwnershipTest` (SPEC subjects); `OwnershipStorageLimitTest` (the published `OwnershipStorageLimit_Kd`). **Fixture ids stay** (`SharedEngine*` 6, `BorrowDependencyStorage*` 2): data names; renaming their expected files needs a native run for no semantic change (OQ4). | AGENTS: reuse the owning class, one name per concept. A rename that only moves names across phases (fixtures, Benchmark) is churn. | 2 `git mv`, `T/OwnershipAnalysisTest.cs`, `T/TypeDagScalingTest.cs`, CODEMAP `:35`, `:140` |
| D7 | **Comments.** A comment that names an old type or states a current fact of the old analysis is rewritten to the MIR's fact, or the old part is dropped; a past-tense provenance comment ("the old analysis accepted and ran it", a Q13 citation) stays. The commit lists each line. §3 lists the 14 lines found. | Current facts in comments; history stays where it explains a moved expectation. No zero-reference row reads prose. | §3 |
| D8 (B1, N1, N2) | **The scaling check (H8a OQ2), on the MIR, with no new accessor.** Measures, per series at the sizes below: A5's work and Loan-set bytes (`MirBorrows.Work`, `MirBorrows.Bytes`, A3 6.2's ν, through `MirBorrowsTest.Measured`, made `internal static`; it reads no instance state) and the cold `Analyze` allocation (`AllocationMeasurement.MeasureSeries`); the series print. Bounds:<br>- **Work:** `MirBorrowsTest.Grows(work, 5)` on regions, fixed-calls, fixed-roots, fixed-joins and both inspection forms: A3 6.3's dense terms "(ended points) × holders" and "accesses × live nodes", as TheWorkScalesWithTheProgram's dense rows state them (`MirBorrowsTest.cs:2345-2350`); these programs hold N holders across N writes, StorageLives and StorageDeads, or read N references at the end. `Grows(work, 2)` on results.<br>- **Bytes:** `Grows(bytes, 2)` on every series.<br>- **Cold allocation:** the V16 form `a[2] − a[1] ≤ 3(a[1] − a[0])` on results, regions, fixed-calls and both inspection forms. **fixed-roots and fixed-joins stop** under this design's rule: their cold growth (§5) has no A3 term, since A5's own Bytes are linear, so it lies elsewhere in `Analyze`. Their cold rows are not landed, no old assertion is kept for them, and one open issue (§8) carries them.<br>- **candidates (N1):** no MIR term. Its N `Callable` constraints are Binding's; A5's work and bytes, the MIR size and the cold allocation are constant over N, so a row would check nothing, and the theory omits the axis. Its fixtures and warm reruns stay; Binding's constraint scaling is BU's, outside H8c.<br>Placement: `CallableRegionScalingTest.EachAxisScalesWithinItsBound(string axis)` over its five MIR axes at N = 16, 32, 64; `InspectionLoanTest.InspectionLoansScaleWithinTheirBound(bool stored)` at N = 32, 64, 128, which replaces the old compactness bound. `Grows` becomes `internal`; `Stratum` stays private (N2). The warm zero-allocation reruns stay. | R4 asked for a per-axis bound (AGENTS: scaling checks for affected analyses; never a relaxed factor without its term), and R4 named the terms (B1). `MirBorrows.Bytes`/`Work` are internal facts already read by MirBorrowsTest's V16 rows (InternalsVisibleTo), so no test-only accessor and no published fact is needed. Small N would leave `Grows`' +64 slack vacuous, so the sizes are the V16 rows'. | `T/CallableRegionScalingTest.cs`, `T/InspectionLoanTest.cs`, `T/MirBorrowsTest.cs` |

## 3. Uses and fates (item 2)

| File | Member | Old name | Fate |
| --- | --- | --- | --- |
| SharedEngineTotalityTest → LoanHolderTotalityTest | `EveryKindHasAFlowRow` | `OwnershipOperationKind`, `OwnershipValueKind`, `OwnershipFlow.OperationFlow`, `.ValueFlow`, `OwnershipFlow.FlowOf` | Deleted (D4); `MirValidatorTest.EveryKindHasADescriptorRow` |
| | `LivenessConsumesCleanupFactsAndDistinguishesAbort` (2) | `OwnershipBody`, `OwnershipPlaceKind`, `AcquisitionKind`, `OwnershipOperationKind`, `OwnershipValue`, `OwnershipEdgeKind`, `PackedAnalysisTable`, `BorrowLiveness` | Deleted (D4); `ADestructionRetainsAsItsDropsCall` rows `localBad`/`quietOk`, `DestructionObservesUserDropsAndUnknownParts`, `ACheckOnlyDropRunsNothing` |
| | class comment, `RejectsAccesses…` (8), `RunsTheOrders…` (6 fixtures) | "the concrete engine" | Rename (D6); comment: "A5 carries…"; rows unchanged |
| OwnershipStorageLimitTest | `OversizedProductsReportResources…` | `OwnershipStorageLimitException`, `OwnershipStorage` | Deleted (D4); `TheArenaIsAdmittedByTheAnalysesLimit`; overflow part: none |
| PipelineSwitchTest | `TheMultiCodeHelpersAreTheOwnershipMapping`, `OwnershipCodes()` | `OwnershipIssue` (3) | → `MirCensusTest.TheMultiCodeHelpersAreTheCensusImages` (D2) |
| `X/CompilationTestHelper` | `IsLoanConflict`, `IsUnprovenOrigin` comments | `OwnershipIssue.Code` (2) | "the census pairs" (D2) |
| EnumBindingTest | `PayloadOperationsKeepOrderAndAcquisition` | `AcquisitionKind` (2) | HIR `AdjustOf(conversion.Left).Acquire` is `Move` (D3) |
| MirInitTest | `:72-73` comment of `AMoveOfACopyTypedPartMarksItMoved` | `OwnershipAnalysis.AcquireElement` | "Before H5 the old analysis accepted these forms; the rows pin the MIR's verdicts" (D7) |
| OriginObligationVerdictTest | `EachObligationIsJudgedOncePerAnalysis`, class summary | entry ×3, `Verdicts` | D5 |
| BorrowDependencyStorageTest → InspectionLoanTest | class, 2 methods; `+ InspectionLoansScaleWithinTheirBound(bool)` | old table names | D6, D8 |
| CallableRegionScalingTest | `+ EachAxisScalesWithinItsBound(string)` | — | D8 |
| OwnershipAnalysisTest | `WarmBindingAndBothAnalysesReuseStorage` | "both analyses" | D6 |
| TypeDagScalingTest | `OldAnalysisWalksAreLinearInDistinctParts`; `:125-127`, `:245`, `:347-349`, `:352` | "the old analysis's walks / answer tables / rows", "the old path's" | D6; D7: the MIR's walks (A5's cleanup observation, Origin projections, completion) and `c.Problems` (the MIR's) |
| MirBorrowsTest | `:427` ("accepts … until R6-check"); `.Closures:421` ("the old analysis's gate keeps it"); `.Referents:535` ("reports no") | present tense | D7: past tense or dropped |
| InstanceCollectorTest `:2316`, MirOriginsTest `:322`, RawStorageTest `:416`, EmissionArtifactsTest `:219` ("Old ownership and startup") | comments | present tense | D7 |

## 4. After H8c (item 4): what still references `C/Analysis/**`

| Where | Reference | Removed by |
| --- | --- | --- |
| `T/PipelineSwitchTest.TheLegacyOwnershipEntryRunsOncePerVersion` | the entry ×3 (its own test) | H10b, with the entry |
| `T/ArchitectureRulesTest` `:28-29`, `:57-59`, `:63` | ratchet rows naming the entry and `ControlFlowAnalysis` (limits 0 after H8a/H8b) | H10b: zero-reference rows (D-c4) |
| `T/DeferredEmissionTest` `:141` and its `DeferredExpansionLimit_Kd` line | the old limit's code | H10b, with the code |
| `X/MirTargetSide.cs:57`, `T/DiagnosticPrecisionTest.cs:315` | `LintErrorRecords` (Binding's, Transitional) | H10b's Q6 (H8b D6) |
| `src/Benchmark` (9 files) | entry reads: BorrowStorageMeasurements, CompilerPlanMeasurements, InheritanceLifecycleMeasurements, MatchOwnershipBenchmark, OriginProofMeasurements, OwnershipAnalysisBenchmark, PairCaseMeasurements, VerificationMeasurements; `ControlFlowAnalysis`/`AnalyzeControlFlow` in PatternBindingBenchmark; type `OwnershipResult` in 2. Also `BorrowStorageMeasurements.cs:10` and `BorrowStorage.md:3` name the renamed `BorrowDependencyStorageTest` (D6) | H9 |
| `src/Playground` | none | — |
| Past-tense provenance comments (≈ 95 lines) | prose, no type name | stay |

So H10b's deletion meets only the entry, its own test, the ratchet rows, the old limit's code and `LintErrorRecords`' Q6 in tests.

## 5. Verification (item 5)

- **Class: Major; the design review is done** (R4, REVISE, rulings in §8). R4 takes the implementation review and first checks B1 and
  B2 (WORKFLOW Repeated reviews 1).
- **Commits** (tests and test comments only; phases within Q12's hour: H8c-1 = 1–2, H8c-2 = 3–4):
  1. Behavior-preserving renames (D6): the two `git mv`, the class and method names and comments; CODEMAP `:140`'s test column (`:35`'s
     flow-table sentence goes with E4 in commit 2). Test ids change; no assertion changes (VERIFICATION behavior-preserving).
  2. D1–D5 and the D7 comments of the touched methods: the deletions with their counterparts named (or "none"), D2's move, D3, D5.
  3. D8: `Measured`'s and `Grows`' visibility, the two theories.
  4. D7's remaining comments (§3's list).
- **The probe's series (R4, Release at 3ed4d0582), to re-measure in commit 3.** Work and bytes from `Measured`, cold allocation from
  `MeasureSeries` of `Analyze`; N = 16, 32, 64, 128 for the axes and 32, 64, 128, 256 for inspection. D8 lands the first three sizes;
  the fourth is the next window.

| Series | Work | Bytes | Cold allocation | D8 |
| --- | --- | --- | --- | --- |
| candidates | 106841 ×4 | 3492 ×4 | 3177104 ×3, 3069616 | omitted (N1) |
| results | 107854, 108654, 110254, 113454 | 3508 ×4 | 3133008, 3243432, 3484064, 4005928 | work 2, bytes 2, cold ×3 |
| regions | 136251, 194285, 398725, 1158881 | 4460 … 11180 (×2.00) | 3434608, 4015888, 5225704, 7627504 | work 5, bytes 2, cold ×3 |
| fixed-calls | 127573, 168065, 311929, 851177 | 4280 … 9208 (×2.00) | 3244936, 3457432, 3956792, 4914320 | work 5, bytes 2, cold ×3 |
| fixed-roots | 122688, 147303, 225031, 493045 | 4096 … 8128 (×2.00) | 3485376, 3846064, 4808120, 7761400 (×2.67, ×3.07) | work 5, bytes 2; cold stops |
| fixed-joins | 138285, 200653, 421840, 1248527 | 4984 … 14840 (×2.00) | 3324248, 3625616, 4423280, 6554264 (×2.65, ×2.67) | work 5, bytes 2; cold stops |
| inspection, stored false | 121272, 148248, 239320, 570456 | 3632 … 4528 (×2.00) | 3332664, 3746256, 4413608, 5729288 | work 5, bytes 2, cold ×3 |
| inspection, stored true | 121768, 150264, 247448, 603096 | 3632 … 4528 (×2.00) | 3359936, 3913640, 4766488, 7492384 (×1.54, then ×3.20 at 256) | work 5, bytes 2, cold ×3 |

- The MIR size (blocks + statements) is linear in N on every series (candidates constant at 9715). The constant bases (about 107 k work,
  3.5 k bytes) are the library bodies `Measured` also runs. No `Measured` precondition failed. A re-measured series outside its bound
  stops its row for R4, never a relaxed factor.
- **Unit checks:** the 11 changed classes (LoanHolderTotality, OwnershipStorageLimit, PipelineSwitch, MirCensus, EnumBinding, MirInit,
  OriginObligationVerdict, InspectionLoan, CallableRegionScaling, OwnershipAnalysis, TypeDagScaling), MirBorrowsTest (the helpers'
  callers), the named counterparts (MirValidatorTest, `ADestructionRetainsAsItsDropsCall`, MirTypesTest, MirEffectsTest, MirOriginsTest),
  unchanged, `ArchitectureRulesTest` (the ratchets hold), `-trait Purpose=Allocation` of the touched classes, the build with
  `-warnaserror` (StyleCop member order for D2's moved method and the helpers).
- **Group Session window:** diagnostic snapshot 0 differences (no producer change); semantic snapshot unchanged; no native fixture
  changes (ids and programs kept), so none runs.
- **Moved expectations:** none of kinds (b) or (c): D2's and D3's rows are (a) (R4's probe gives D3's `Move`); D8 adds new bounds.
- **Documents (Auto):** CODEMAP (commits 1–2); PLAN in R5's landing commit: H8a OQ2's open issue closes with commit 3 for the landed
  rows, the cold-allocation issue (§8) is added beside it, F13's recount, and §1's H10b finding as H10b's input; DECISIONS: none (no new
  representation, component or vocabulary; D5 changes a design note, not an entry). ARCH, STATUS, VERIFICATION unaffected.
- **What R4 needs for the implementation review:** commit 3's re-measured series against §5's; D3's operand rows; D4's and D5's
  counterparts or "none"; D2's census check; D6's renames; D7's comment list.

## 6. Sizes and limits (item 6)

| Part | Lines (t) |
| --- | --- |
| Commit 1: renames, CODEMAP | ±14 |
| Commit 2: D4 (−40), D2 (−46, +14), D3 (±2), D5 (−9, +3), comments (±4) | −95, +21 |
| Commit 3: D8's theories (+40), helpers (±2) | +42 |
| Commit 4: comments | ±16 |

Net ≈ −30 t, far from H0's ≈ −1,100 t: H8a took the five classes' methods (its ≈ −720 t, D7) and H8b the control-flow names; what is
left is 6 methods, names and comments, and D8 adds the scaling check. No structure limit is reached; MirBorrowsTest.cs only changes
two modifiers.

**Test programs that may not parse (item 6).** The B3 shape (`exit to scope n))` closers on a body line) has no hit in `tests/xUnitTest`
on the base. H8c's readers parse through helpers that assert no Error before binding (`MirBuildTest.Compile`, `CompilationTestHelper.
ParseSuccess`), assert the single published record (`DiagnosticCorpus.Check` in LoanHolderTotality), or run as native fixtures
(CallableRegions, InspectionLoans). The exception is OriginObligationVerdictTest's `Analyze` helper, which asserts nothing about syntax;
D5 adds the no-syntax line.

## 7. Open questions for R5

| # | Question | Recommendation |
| --- | --- | --- |
| OQ1 | Accept ≈ −30 t against H0's ≈ −1,100 t (non-blocking) | Accept; the commit states Q5's net size, PLAN's row the recount |
| OQ2 | D8's per-axis factors | Ruled (§8, B1) |
| OQ3 | §1's finding: H10b deletes `ElementAccess.cs:10-15` and `BoundEnumConstruction.Acquisitions` (BU consents), or BU removes the Binding member before H10b (non-blocking for H8c; blocking for H10b's build) | H10b, with BU's consent for the Binding hunk; recorded in PLAN as H10b's input (R4 confirmed the readers, N6) |
| OQ4 | Rename the 8 fixture ids with their classes (D6) (non-blocking) | Keep the ids; a rename needs a native run and changes no meaning |
| OQ5 | D5 moves OriginObligationVerdictTest's counting half from H10b to H8c (non-blocking) | Yes: its subject is the old counter, and H10b then meets only the entry's own test |

## 8. R5's rulings on R4's design review (REVISE, 2026-11-05; revision 1)

- **B1 (D8), R4's decisions under the design's stop rule:**
  - Work: factor 5 on the six quadratic series (regions, fixed-calls, fixed-roots, fixed-joins, both inspection forms), citing A3 6.3's
    dense terms "(ended points) × holders" and "accesses × live nodes" as `MirBorrowsTest.cs:2345-2350`'s dense rows state them;
    results stays at 2.
  - Bytes: linear (2) on every series, as designed.
  - Cold allocation: the V16 ×3 rows land except fixed-roots and fixed-joins, which stop; their old assertions are not kept against the
    old analysis. **Open issue** (R5 records it in PLAN at the landing, beside H8a OQ2's): the cold-allocation growth of `Analyze` on
    fixed-roots (×2.67, ×3.07, failing at 128) and fixed-joins (×2.65, ×2.67) is outside A5's linear Bytes. Its next step is to
    attribute the stage, then that stage's owner bounds it. (An observation for the same attribution: inspection stored=true, landed at
    32–128, reaches ×3.20 at 256.)
  - §5 names the probe's numbers for the implementer's re-measurement.
- **B2 (D3):** the assertion reads the operand, `AdjustOf(conversion.Left).Acquire`, which is `Move` for both payloads, and pins the
  operand Place's acquisition; the argument's `Temporary` is its materialization and is not pinned.
- **N1:** adopted: candidates has no MIR term and leaves D8's theory, with the reason stated (its check would be empty).
- **N2:** adopted: `Stratum` stays private; only `Measured` and `Grows` widen.
- **N3:** adopted: D4 names the rows per part, and states that the overflow-before-allocation part has no MIR counterpart.
- **N4:** adopted: the two InspectionLoanTest methods and `TypeDagScalingTest.OldAnalysisWalksAreLinearInDistinctParts` are renamed (D6).
- **N5:** adopted: D5 states that "once per analysis" has no MIR counterpart.
- **N6:** adopted: R4's reader list is in §1, and OQ3 stands.
- None is declined.

### R5's rulings on the open questions (revision 0, restored after R4's re-check 1 N8)

- **Class Major with one design review by R4** (H0: "R5 (R4 reviews)"; WORKFLOW Design 4: unverified premises), with the probe; the
  same reviewer reviews the implementation. The design is read as if H8b had landed: H8c's implementation starts after H8b's.
- **OQ1:** accepted (≈ −30 t; H8a already moved the five classes' methods). **OQ2:** decided by R4's review (B1 above). **OQ3:** H10b,
  with BU's consent for the Binding hunk (`ElementAccess.cs:10-15`, `BoundEnumConstruction.Acquisitions`; confirmed by R4's review);
  R5 records it in PLAN as H10b's input at the group's landing. **OQ4:** keep the fixture ids. **OQ5:** yes (OriginObligationVerdictTest's
  counting half moves from H10b to H8c).
- The scaling check reads A5's internal `Work` and `Bytes`; R4's review confirmed they are reachable without a new accessor.
- **R4's re-check 1 N7:** the abort part's counterpart is a different rule: D4 names it as "no MIR counterpart for the old rule; the
  MIR's abort rule is pinned by <its test>" (the implementer names the test), not as a counterpart.
