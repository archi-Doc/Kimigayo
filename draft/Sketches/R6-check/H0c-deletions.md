# R6-check H0c: the deletions, migrations and the exit gate (units H6–H15)

Status: H0's part c (plan §4.10 row H0: the deletion order, the benchmark successors, the check-only pass, OQ12–OQ15; units
H6–H15), revision 0, design only, not reviewed; integrated by [H0-design](H0-design.md) (the planning session's rulings R1–R4 and the design review's
findings applied in place, each marked "(ruling Rn)" or "(review Bn/Nn)"). Working record, not authority: what it decides becomes authority only through H0's
DEC entry after OQ11's approval (DECISIONS R5-M1), and its owner items only by the owner's answers.

Standing request: the owner approved R6-check's division of work on 2026-11-02 (R5 leads: H0, H3b, H5's Core side, H8–H10, H12,
H13, H15, the windows; R4 owns the MIR side; BU owns the Binding side: H1, H7, H11, the end of the old-analysis D5 exceptions) and
told R5 「その役割分担でOKです。H0の設計から始めて下さい。終わったら、R4の完了を待ってください。」

Base: dev `1ec539c79`. "Plan" is the [R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md);
"PLAN10" the [Compiler Size Reduction plan](../../Proposals/2026-10-10%20Compiler%20Size%20Reduction%20and%20Single%20MIR%20Plan.md).
Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`, `B/` = `src/Benchmark/Benchmarks/`. Counts are greps over the
base (comments included, as the zero-reference rows count them). No build or test was run. (H) marks an unchecked inference.

Working time (WORKFLOW "Working-time limit", DECISIONS Q12): 2026-11-02 17:40–17:58 (18 min of the hour).

## 0. Decisions in one screen

| ID | Proposal |
| --- | --- |
| D-c1 | Between H5 and H10b the old analysis runs only when its test or benchmark readers call one explicit entry, `Compilation.AnalyzeLegacyOwnership()` (`[Transitional("R6-check")]`, removal H10b); `Compilation.Analyze` no longer runs it, since frozen code throws on shapes the MIR accepts and would fault checks and the editor service (review B3). The 30 `Bodies` and 16 `Issues` readers reach it through their helpers while G:check-tests migrates them; the 55 control-flow readers keep `AnalyzeControlFlow()`; H10b deletes the entry with the code. PLAN10-P3 is taken after H10b (§3) |
| D-c2 | H10 splits: **H10a** (the ledger harness and its 13 reader files, `Compilation.Analyzed`, `KIMI_MIR_DIFF`, `Binding.IsTwinRecord`, R5, G:check-tests) right after H5's post-merge sweep (H6 is in H5, review B3); **H10b** (the old Analysis itself, R5, G:check-del) after H8a–c, H9 and H10a. After the flip nothing classifies against the ledger, and H10a alone touches 15 test files: one unit for both exceeds Q12's hour |
| D-c3 | Order after G:R6-check-flip (H5 with H6, review B3): the post-merge sweep → H10a → H8a → H8b → H8c → H9 → H10b → H11 → H12 → H14; P42's remainder, H7, H8b and H8c are held until the sweep passes (review N1); H7 after the sweep once G157 (1) is settled; H13 is documentation and may land first; H15 last (§2) |
| D-c4 | The zero-reference rows (§5.5 item 2) land in two steps: tests, `src/Benchmark` and `src/Playground` with H10b; `src/Kimi` with H11, because Binding's comments and the R4-M152 gate members (`Binding.ValueCallGate.cs:8`, `:27`, `Binding.RawOrigins.cs:48`) name `OwnershipAnalysis` until BU deletes or rewords them |
| D-c5 | Stage 4 of the compiler pipeline series keeps its report name `ControlFlowAndOwnership` (PD26; the O2 corpus harness compares names) and times `MirAnalysis.Run` plus collection (from H5 `Analyze` runs no old analysis, review B3); stage 3 times HIR publication, construction and validation. H9 splits `Compilation.BuildMir`'s two composed steps at that boundary (behavior-preserving commit first), no benchmark-only seam (§3) |
| D-c6 | H14 also covers the forwarding-property reads in Mir and the Backend (`MirBuilder.cs:112`, `:134`, `MirBuilder.Opaque.Roots.cs:133`, `MirBuilder.Values.cs:273`, `InstanceCollector.Objects.cs:64`, `:70`, and any others its exact count finds): R5-M9 postponed only Binding's reads, so the rest are due at R6-check |
| D-c7 | Record corrections: H7's site is now `C/Binding/Binding.Operators.cs:331-334`; H11's A3 gates are `Binding.OriginFits.cs:96-100` and `Binding.TypeOrigins.cs:187-198`; H5's ledger effect filter is `MirDifferenceLedger.ComparedSet.cs:21, 160`; H10's `ReportAnalysis` control-flow read is `Compilation.Pipeline.cs:84` (it stops at H5, ruling R2); G186's old walk `EffectBounds.Comparison` goes at H5 (it is inside `EffectSummary`), not H11 |
| D-c8 | `ComparisonPlan(CallPlan)` and `DictionaryComparison` go at H11 as their Transitional tags say (their compiler readers are in `EffectSummary`, deleted at H5; one test reader, `T/ComparisonCompositionTest.cs:575`); the plan's "stays until R7" holds only for `InstantiateRequirementCall` and `CallPlan.TupleOperator` |

## 1. The reference graph today

**Old Analysis** (`C/Analysis/`): 50 files, 16,180 lines (`OwnershipAnalysis*` 29 files 7,985; `OwnershipBody*` 13 files 6,226;
`ControlFlowAnalysis` 988; `OwnershipModel` 539; `OwnershipFlow` 176; `BorrowLiveness` 145; `PackedAnalysisTable` 59; `OwnershipStorage*`
43; `InterpretationContext` 19). The names `OwnershipAnalysis|OwnershipBody|OwnershipModel|OwnershipFlow|BorrowLiveness|PackedAnalysisTable|
OwnershipStorage|InterpretationContext|ControlFlowAnalysis` occur 133 times in 54 `src/Kimi` files, 1 in `src/Benchmark`, 0 in
`src/Playground`, 22 in 9 test files.

| Reader outside `C/Analysis` | Site | Removed by |
| --- | --- | --- |
| `Compilation.Ownership`, `AnalyzeControlFlow` | `C/Core/Compilation.cs:126-129`, `:314-318` | H10b |
| `Analysis`, `Problems`, `Analyze`, `ReportAnalysis` | `C/Core/Compilation.Pipeline.cs:39, 56, 73, 84, 93` | H5 (decide by MIR; :84 and :93 stop publishing, ruling R2; :73's `Ownership.Analyze()` leaves `Analyze` for the transitional entry, review B3), H10b (the entry) |
| `Accepts` through `Analysis` | `C/Core/Compilation.Generation.cs:46`; `Project.cs:550-554` (the plan's `:510`) | H5 |
| Hover "Ownership checks" | `src/Kimi/Checking/Hover/HoverBuilder.Virtual.cs:75` | H5 (H6 is part of H5, review B3) |
| `EffectSummary` (`TemplateBody`, `Ownership.ControlFlow`, `CollectCaseDestructions`) | `C/Binding/Binding.EffectBounds.cs` (1,421 lines: :17, :98, :140-143, :983, :1071), `Binding.PublishedOperations.cs` (48) | H5 |
| Virtual effect check | `C/Binding/Binding.VirtualEffects.cs:41` | H5 |
| `ElementAccess` `OwnershipBody` overloads | `C/ElementAccess.cs:102`, `:142` | H10b |
| Comments naming the old types | `Binding.RawOrigins.cs:48`, `Binding.ValueCallGate.cs:8, 27` | H11 (D-c4) |

**Old-Analysis-only Binding members (H11)**, readers outside `C/Analysis` after H5:

| Member | Readers today | After H10b |
| --- | --- | --- |
| `PropertyUpdateStorage` (`Binding.PropertyCalls.cs:18`) | `OwnershipAnalysis{,.Arithmetic,.Values}` only | dead |
| `CollectWitnesses` (`Binding.Specializations.cs:42`) | `OwnershipAnalysis.cs:1674` only (the plan's library-reach check: no MIR reader) | dead |
| `ElementAccess.IsIndexerCall`, `DesignatedType` (PR-1) | old Analysis 6 sites; `ElementAccess.cs:56, 61, 126` (inside the H10b overloads) | dead |
| `MatchTypes` (`C/MatchTypes.cs`, 88) | `OwnershipAnalysis.Match.cs:149, 164` | dead |
| `StaticScalar` (`C/StaticScalar.cs`, 59) | old Analysis 5 sites; `Binding.EffectBounds.cs:208` (H5) | dead |
| `DeferredExpansionLimit_Kd` | its one reporter `OwnershipModel.cs:198` | unreported: delete at H10b (plan: H11) |
| `TryResolveFormattingCallback`, `DestructionCall` | `Binding.EffectBounds.cs:768`, `:1289` (H5) | dead |
| `SelectSpecialization` (CallPlan overload) | `Binding.EffectBounds.cs:880` (H5) | dead (H) |
| `InstantiateForwardedCall` | `Binding.EffectBounds.cs:679` (H5); tests `TyCtxtTest.cs:1390, 1518, 1824, 2009`, `VirtualCallSelectionTest.cs:87, 91` | retarget tests to F5b's twin, then delete |
| `ComparisonPlan(CallPlan)`, `DictionaryComparison` | `Binding.EffectBounds.cs:782`, `:243` (H5); `T/ComparisonCompositionTest.cs:575` | D-c8 |
| `RequirementImplementation` (`Binding.Formatting.cs:58`, tagged R6 for H11) | `Binding.Formatting.cs:36` (dead after H5) **and** `Binding.ComparisonPlans.cs:68`, the R7-tagged private composition read by A4's Witnesses until A-12 | stays unless A-12 lands first (Q-c2) |
| `ContainsOrigin(BoundType, …)` (BU-63) | `OwnershipBody.Borrows.cs:615, 2568`, `OwnershipBody.Updates.cs:176`; Binding's own calls use the `BoundOrigin` overload | dead |
| R4-M152 gate: `receiverDependentCalls`, `receiverDependentPass`, `ReceiverDependentCalls`, `RecordReceiverDependence`, `CallPlan.receiverDependence`, `ReceiverDependentResult`, `ReceiverDependentListed` | `OwnershipAnalysis.cs:176, 178` | dead |
| `LintErrors`, `LintErrorRecords`, `FallthroughKey` (`Binding.Lints.cs:81-139`, tags: "H10 deletes it") | old Analysis; `LegacyRecords.cs:29`, `MirDifferenceLedger.cs:79`; tests (14 reads in 7 files) | delete at H10b after H8b/H10a; `FallthroughKey` (one reader, the old `ReportDiagnostics`, `OwnershipAnalysis.cs:314`) at H5 (ruling R4) |
| A3's gates | `Binding.OriginFits.cs:96-100`, `Binding.TypeOrigins.cs:187-198` | acceptance consequence: name in H11's snapshot classification |
| `Binding.WithoutRawOrigins` (BU-24's exception) | old Analysis 4 sites; **also** AdtDef's view (`Binding.Storage.cs:71, 81`, BU-41) | stays; only the comment's "removed with it at R6-check" changes |
| `Binding.RequirementAt` (BU-44's exception) | `OwnershipBody.Borrows.cs:1044` and 8 Binding sites | stays; `OwnershipBody.SlotLoanRequirement` goes at H10b |

## 2. Units H6–H15 in order

Groups: G:check-del (H7, H9, H10b, H11, H12, H14), G:check-tests (H8a–c, H10a). Sizes are counted lines where a file is deleted
whole, else the plan's estimate re-checked (t = test lines). Each unit's implementation stays within Q12's hour; H8a–c split by
file family at their start where the count exceeds about eight files. Every commit builds: a unit deletes only names whose last
reader an earlier unit (or itself) removed.

| Order | ID | Owner | Change (files) | Prereq | Verification | Size | Review |
| --- | --- | --- | --- | --- | --- | --- | --- |
| in H5 (review B3; supersedes ruling R3) | H6 | R5 | `HoverBuilder.Virtual.cs:75` reads "completed" when the function's MIR body has no problem in `MirReport`, else "pending" (R4's `MirReport` API from H3); `T/VirtualHoverTest.cs:54`; HOVER row | H5, H3 | Comp: Hover tests | ±20 | R |
| 1 | H10a | R5 | Delete `tests/xUnitTest/MirDifferenceLedger{,.Aggregation,.ComparedSet}.cs` (474), `LegacyRecords.cs` (56), `T/MirDifferenceTest{,.ComparedSet}.cs` (342), `Compilation.Analyzed` (`Compilation.Pipeline.cs:31-32`, its invocation), `KIMI_MIR_DIFF` (`FreshProcess.cs`, `FreshProcessTest`), `Binding.IsTwinRecord` (its last reader goes; the twins stay to H11). The MIR half of `Sides`, `Entry`, `MirAnalysisOf` moves into `CompilationTestHelper` for its 13 reader files (`ClosureEnvironmentOriginTest` 5, `ClosureResultOriginTest{,.Makers}` 7, `MirCensusTest` 3, `NeverContinuationTest` 4, `SemanticSnapshotTest` 8, `VerifiedSpecExampleTest` 3, `AbstractCallReachTest`, `MinimalEmissionTest`, `MirOriginsTest.Proofs`, `OriginRelationDiagnosticTest`, `PairFollowTest`); `T/PipelineSwitchTest.cs:337-370`'s `Analyzed` test goes; `MirTargetSide` keeps its MIR assertion only if an OldRule test survived H5, else goes; `SemanticSnapshotTest` prints the published MIR records in place of `LegacyRecords.Of` (snapshot re-baselined, reason: the flip, H5) | H5's post-merge sweep (review N1) | Comp: the 13 classes, `SemanticSnapshotTest`, `VerifiedSpecExampleTest`; snapshot classified | ≈ −1,000 t, +80 t | G:check-tests |
| 2 | H8a | R5 (R4 reviews MIR-dump successors) | `Bodies` and `Issues` residue: 30 `.Ownership.Bodies` reads in 16 files and the 3 `Body(c)` helpers (`T/EnumOwnershipTest.cs:306`, `T/MatchOwnershipTest.cs:368`, `T/OwnershipAnalysisTest.cs:311`; 29 calls); 16 `.Ownership.Issues` reads in 10 files (incl. `TempProbeTest.cs`, the tracked KIMI_PROBE dumper), read through `Compilation.AnalyzeLegacyOwnership()` from H5 (review B3) → MIR dump goldens, MIR problems or deletion; the `Bodies` and `Issues` rows tightened to their counts as reads leave | H5, H10a, Q18 or H2 | Comp: the touched classes | ≈ −900 t | G:check-tests |
| 3 | H8b | R5 (BU reviews Binding/Parsing classes) | 55 `AnalyzeControlFlow()`/`ControlFlowAnalysis.Analyze(` calls in 15 files (ratchet 64) and 14 `LintErrors`/`LintErrorRecords` reads in 7 test files → lint observations; ratchet → 0 | H8a (D-c3's order), H5's sweep (review N1), Q9–Q12 | Comp; ratchet 0 | ≈ −700 t | G:check-tests |
| 4 | H8c | R5 (R4 reviews MIR rewrites) | The 16 Analysis-declared enums (`OwnershipOperationKind` 48, `CleanupAction` 31, `OwnershipPlaceKind` 23, `PlaceState` 20, …) in 10 test files, and the 22 old-type names in 9 test files (`OwnershipStorageLimitTest` 5, `SharedEngineTotalityTest` 5, `MirInitTest` 1, …): rewrite as MIR assertions or delete with their classes (`OwnershipAnalysisTest`, `EnumOwnershipTest`, `MatchOwnershipTest`, `BorrowDependencyStorageTest`, `CallableRegionScalingTest` → a MIR scaling check) | H8b (D-c3's order), H5's sweep (review N1) | Comp | ≈ −1,100 t | G:check-tests |
| 5 | H9 | R5 | §3 | H5's sweep; after H8c (D-c3) | Bench: slnx build, `--compiler-pipeline-check` | ≈ −350, +30 | G:check-del |
| 7 | H10b | R5 | Delete `C/Analysis/` (50 files, 16,180), `Compilation.Ownership`/`AnalyzeControlFlow`, the transitional entry `Compilation.AnalyzeLegacyOwnership()` (review B3; `:73`'s call and `:84` left `Analyze`/`ReportAnalysis` at H5, ruling R2), `ElementAccess.cs:102, 142`, `Binding.Lints.cs:81-139`'s `LintErrors` and `LintErrorRecords` (`FallthroughKey` went at H5, ruling R4), `DeferredExpansionLimit_Kd`; the `.Ownership.*` and control-flow rows of `ArchitectureRulesTest` → zero-reference rows (D-c4); the ARCH structure-limit exemption of the old Analysis ends | H8a–c, H9, H10a | Comp: Session with milestones | ≈ −16,300 | G:check-del |
| 8 | H11 | BU | §1's H11 table; the `src/Kimi` zero-reference row (D-c4) | H10b, Q-c2 | Comp; snapshot (A3's gates) | ≈ −700 | G:check-del |
| 9 | H12 | R5 | §4.2 | H10a, H10b | Tool + Comp | ≈ −600 | G:check-del |
| 10 | H14 | R5 (BU reviews) | §4.4 | H10b, H11 | Comp; snapshots equal | ≈ −150 | G:check-del |
| any, after H5's sweep (review N1) | H7 | BU | Delete Binding's wide-division gate `Binding.Operators.cs:331-334` (A3b's `Capability.WideDivision` owns it) | H5's sweep, K9a, Q-c1 | Comp: i128 tests, A3b tests; snapshot | −4 | G:check-del |
| any | H13 | R5 | §4.3 (documentation only) | none | Doc | 0 | R |
| last | H15 | R5 | §8 | H7–H14, OQ15 | Doc + Session | docs | final review |

**Constraints this order puts on H0's other parts (the flip, H5).** H5 must remove `Ownership.Analyze()` from
`Analyze`, add `Compilation.AnalyzeLegacyOwnership()` for its readers (D-c1, review B3) and keep the harness compiling until H10a; delete `IsEffectRecord` together with its ledger reader
(`MirDifferenceLedger.ComparedSet.cs:21, 160`); delete the OldRule traits (25 lines in 13 files today, plan: 19) with the
`ArchitectureRulesTest` OldRule ratchet row (`:80`) and `MirTargetSide`'s legacy half (10 ledger reads); and name the successors of
the old analysis's three published reports that H5 stops publishing (ruling R2; H0b §2.3 names them): `RequireFallthrough_Kd` (`ControlFlowAnalysis.cs:800`; `MirLints`
reports it too), the derived `Unresolved` fallback (`OwnershipAnalysis.cs:337-341`) and `ReportUnprovenOriginObligations`
(`OwnershipAnalysis.Borrows.cs:56`).

## 3. Benchmark successors (H9) and the PLAN10-P3 record

Ten `src/Benchmark` files read the old Analysis today, plus `Program.cs` (the plan's "12"); two more read the pipeline switch (H12).

| File | Old reads | Successor |
| --- | --- | --- |
| `B/BorrowStorageMeasurements.cs` (65) + `BorrowStorage.md` (73) | `Ownership.Bodies`, `Ownership.Analyze` | delete; `Program.cs:122` |
| `B/OwnershipAnalysisBenchmark.cs` (67) | `Ownership.Analyze` | delete; `Program.cs:207` |
| `B/MatchOwnershipBenchmark.cs` (55) | `Ownership.Analyze` | delete; `Program.cs:208` |
| `B/PatternBindingBenchmark.cs` (63) | `ControlFlowAnalysis`, `AnalyzeControlFlow` (:14, :42, :60) | keep `Bind`; `BindAndAnalyze` → `Compilation.Analyze()` |
| `B/CompilerPlanMeasurements.cs` | `BorrowStorageBytes`, `LocalRegionStorageBytes`, `LocalRegionIndexCapacity`, `PeakLocalLoanCells`, `DefaultContexts` (:106-110); `Ownership.Analyze` (:119) | columns deleted; precondition → `Compilation.Analyze()` |
| `B/InheritanceLifecycleMeasurements.cs` | five `Bodies` columns (:141-145) | columns deleted |
| `B/PairCaseMeasurements.cs` | `Bodies` places/operations (:56-62), `Ownership.Analyze` (:93) | columns deleted; → `Compilation.Analyze()` |
| `B/OriginProofMeasurements.cs` | `Ownership.Analyze` (:91, :165), `Ownership.Issues` (:93) | → `Compilation.Analyze()`, `Problems` codes |
| `B/HoverMeasurements.cs`, `B/VerificationMeasurements.cs` | `Ownership.Analyze` (:77; :53) | → `Compilation.Analyze()` |
| `B/CollectionCostMeasurements.cs:183`, `B/DiagnosticMeasurements.cs:207` | `Pipeline` | H12 |

The `.md` series keep their historical rows; each gets one line naming the removed column or benchmark. Stage 4 (D-c5): today
`ControlFlowAndOwnership` times `Compilation.Analyze`, the old analysis over stage 3's memoized MIR (`CompilerPipeline.md:17`).
After H10b it would time nothing, so H9 moves the MIR analyses and collection into stage 4; from H5 `Analyze` runs no old analysis (review B3), so stage 4
loses it at H5 (a series break H5's commit names) and H10b changes no stage. H9 must land before H10b, which deletes the
`Compilation.Ownership` these files read. The **PLAN10-P3 record** (exit gate item 7), taken after H10b by H15:
`--compiler-pipeline` stages 3 + 4 ("BuildMir + collection") on the O2 corpus inputs against the R6-gen exit series (its base in
`artifacts/benchmarks/r6-gen-exit-areas.md`), where stage 4 was the old analysis; `--collection-cost` for collection's share; the
warm Bind/Analyze zero-allocation tests.

## 4. H6, H12, H13, H14

**4.1 H6** — part of H5 (review B3). The line keeps its two values; "completed" means the published MIR records name no problem in the function's
body, never "no error anywhere".

**4.2 H12, the switch's removal** (after H10a, which removes the ledger, the switch's last behavior):
- Code: `C/Core/Compilation.Switch.cs` (whole file: `DefaultPipeline`, `pipeline`, `Pipeline`, `PipelineOf`, `CompilerPipeline`;
  the plan's "6 declarations" are 5 Transitional plus `CompilerIdentity`), `CompilerIdentity`'s `[mir]` (readers:
  `src/Kimi/Checking/CheckDocument.cs:50`, `B/AnalyzeCostMeasurements.cs:35`, `B/CompilerPipelineMeasurements.cs:71, 97`,
  `B/SyntaxEditMeasurements.cs:68`, `T/CheckJsonOutputTest.cs:91` keep `CompilerVersion`), `src/Kimi/Testing/TestCatalog.cs:122`
  (the plan's `:108`), `C/Core/Compilation.Generation.cs:15` (comment).
- Tests: `T/PipelineSwitchTest.cs` (449), `T/MirBuildTest.Places.cs:225-228`, `T/RawStorageTest.cs:371`'s name,
  `tests/xUnitTest/FreshProcess.cs:18`, `T/AbstractCallReachTest.cs:350` (comment).
- Scripts: `scripts/verify.ps1:25-27, 41, 69-71, 328-329`; `scripts/compare-fixtures.ps1:66, 104, 118`;
  `src/backend/windows-x64/benchmark-o2-corpus.ps1:21`; `src/backend/windows-x64/test-library.ps1` (2).
- Docs (the update table's Auto rows): VERIFICATION `-Pipeline` paragraph (now near :95), CODEMAP (1), ARCH temporary structures
  (`KIMI_PIPELINE` switch), `src/Benchmark/CompilerPipeline.md` (2), `O2Corpus.md` (2). DECISIONS and PLAN_HISTORY keep history.

**4.3 H13** (OQ14 answered; R5-M10): ARCH already carries the amended text (`COMPILER_ARCHITECTURE.md:172-174`, `:189`) and Q16
landed (`508f12a12`, R5-M79). H13 is a confirmation table: Mir/Build today has 27 `KotoKind.` references in 2 files, 17 Koto type
tests, 37 `(XKoto)` casts and 9 Koto switch arms (raw greps, 90 matches against R5-M10's 63 + 17); H13 classifies each as syntax
data under a published `HirOp`, a child cast, or a violation (then a fix unit), with no code change expected.

**4.4 H14** (OQ12, OQ13 answered; R5-M9): Parsing's reads of the forwarding properties (≤ 50 in 11 files, an upper bound: some
names are also other types' members), the root helpers' (`ElementAccess.cs` ≤ 21, `ScalarTypes.cs` 1; `StaticScalar.cs` goes at
H11), and Mir's and the Backend's (D-c6; ≤ 21 and ≤ 24 name matches, 6 confirmed) rewritten to `HirTables`, each area with a
zero-reference row; the 37 forwarding declarations (`Koto.cs` 10, `BinaryKoto.cs` 14, `FunctionKoto.cs` 3, `ForKoto.cs` 3,
`InvocationKoto.cs` 3, `UnaryKoto.cs` 2, `CollectionLiteralKoto.cs` 1, `TryKoto.cs` 1; the plan's "36") retagged
`[Transitional("R7")]` citing R5-M9 (the owner's approval) in the commit. The synthetic Kotos and ARCH:163 stay for R7 (R5-M9).

## 5. Census: what ends at R6-check

**5.1 Transitional declarations**: 51 `("R6")` and 13 `("R6-check")` in `src` (tests: none).

| Declarations | Count | End |
| --- | --- | --- |
| Koto forwarding properties (§4.4) | 37 R6 | retagged R7 at H14 (owner-approved, R5-M9) |
| `Compilation.Switch.cs` | 5 R6 | H12 |
| `Compilation.Analyzed` | 1 R6 | H10a |
| `Compilation.AnalyzeLegacyOwnership()` (added at H5, review B3) | 1 R6-check | H10b |
| `Binding.IsEffectRecord` | 1 R6 | H5 |
| `Binding.IsTwinRecord` | 1 R6 | H10a (D-c2; tag says H11) |
| `Binding.Lints` `LintErrors`, `LintErrorRecords`, `FallthroughKey` | 3 R6 | H10b; `FallthroughKey` at H5 (ruling R4) |
| `Binding.ComparisonPlan(CallPlan)`, `DictionaryComparison` | 2 R6 | H11 (D-c8) |
| `Binding.RequirementImplementation` | 1 R6 | H11 only if A-12 lands first; else retag R7 with owner approval (Q-c2) |
| `OwnershipAnalysis` stops and gate (`:27, 173, 192, 207`; R4-M152, R4-M159) | 4 R6-check | H10b |
| R4-M152 gate in Binding (`ValueCallGate.cs:29, 33, 37, 65`, `CallPlan.cs:58, 358, 366`) | 7 R6-check | H11 |
| `Binding.ContainsOrigin(BoundType, …)` (BU-63) | 1 R6-check | H11 |
| `Binding.JudgeChains` (BU-62) | 1 R6-check | PLAN G318 (R4 with BU, "due R6-check"): not a deletion of the old Analysis; a precondition of H15 |

**5.2 The D5 freeze's owner exceptions** (ARCH "Freeze (D5)"); "verdict" means the flip classification (H3b/H5) must name the
forms the exception covers, because their default verdict changes from the old analysis's to MIR's; "code" means the exception
ends with deleted code and no verdict change of its own.

| Exception | DEC | Ends at | Kind |
| --- | --- | --- | --- |
| Place result removal | PR-1 | H10b (`ElementAccess` overloads), H11 (`IsIndexerCall`, `DesignatedType`) | code |
| G147 (2b) whitelist and guard `Unsupported_Kd` | BU-10 | H5 (verdict), H10b (code) | verdict: guarded forms move from `Unsupported_Kd` to MIR's verdict |
| 2x-E7 pair-layer Pattern guard | BU-14 | H5, H10b | verdict (as BU-10) |
| G140 performance memos | BU-19 | H10b | code (identical verdicts by its terms) |
| Q-O3: `WithoutRawOrigins` at `Place`/`Resolve`; `ProjectPointer` | BU-24 | H10b (the view stays for AdtDef, BU-41) | code |
| G86 per-case `RequirementAt` through `SlotLoanRequirement` | BU-44 | H10b | code |
| A5-F7 2c-5: the gate stops the whole analysis | R4-M126, R4-M152 | H5 (verdict), H10b and H11 (code) | verdict: the stopped programs regain their independent records (rule W, Q13 (3) in the gaining direction) |
| G273: the stop at `SupportsType`'s refusal | R4-M159 | H5, H10b | verdict; **not listed in ARCH's freeze text** (PLAN `:36` and the code tags record it): H15 adds it to the ledger close-out, or the R4 merge that adopted it adds the ARCH clause (Q-c3) |
| Abstract Calls Reach by Paths U1, U2 | R4-M106 | H5 (`EffectSummary`, `PublishesIndependentResults`' old part), H10b | verdict only where the ledger shows a difference |
| g2never element forms, `AcquireElement` | R5-M159, R5-M160 | H10b (old generator half ended at G4) | verdict as ledger rows |

**5.3 PLAN rows that close at R6-check**, with the closing unit (H) where the row's text says only "R6-check":

| Unit | Rows |
| --- | --- |
| H5, not H3 (review B1: H3 changes no published record) | G164, G203 (their "acceptance at H3" moves to H5), G213, G218, G268, G291 |
| H5 (MIR decides) | G124, G165 (verdict), G177 (owner 「R6-checkを待ちます」), G186's old walk (D-c7), G211, G217, G228, G299, G313, G160's default verdicts; G121's 2x-P10 regression: a lost fallthrough record in a failed `do` (Q13 (3)) must be fixed before H5 or classified (Q-c4) |
| H7 | G157 (1) (precondition, Q-c1) |
| H10b ("closes with the old path") | G47 (old-Ownership candidates), G65, G75, G100 (old Analysis part), G159 (4), G162 (2), G205, G231, G232, G269 (A), G315 (after its SPEC reading) |
| H11 | G56 (the Binding-level relation stop) |
| Before H15 (preconditions, not deletions) | G215 (rule or ruling), G220, G222 (R4), G318 (R4 with BU), G335, G351 (BU; R4 with BU), G258 (A5's cost at R6-check, measured with §3's record), G289 (5) |
| Starts after H5's post-merge sweep (review N1) | P42's remainder (R5-M13) |

## 6. Check-only pass: PLAN10 §11's R6 rows and R4-M15a's IMPL text

| Row | Current text | Holds after the flip? |
| --- | --- | --- |
| IMPL §21 (old :603, :605, :698, :702), App. B | The 2026-10-15 rewrite: IMPL 21.3.2 names "analysis storage" among implementation-defined limits, a Resource diagnostic; no memo-table or per-instance ownership-table text remains | Holds; no revision. R4-M15a's "IMPL 21.3.2's text changes in R6" is already satisfied by the rewrite (the 64 MiB value is implementation-defined, unstated) |
| IMPL App. A §A.3 (old :57-69, stage order) | §A.3 is now "No silent acceptance" (`A-compiler-requirements.md:29-33`); no stage list | Holds (every check before acceptance; Unsupported before generation). §A.4's "at most 64 Semantics cases" is Binding's `CaseLimit` (`Binding.CaseLimits.cs:59`), independent of the old Analysis: holds |
| SPEC §14.10.3, §15.6.1 Liveness, §14.9.2 defer example, App. A:153 (unit a) | Runtime-paths-only drop elaboration was written by R4 (R4-M6a, plan §16-4); §14 now states assumed entry and normal completion on assumed paths (`14-control-flow.md:410`, `:537`) | Holds (H: no clause-by-clause comparison with PLAN10 §5.6.3 was made); H3b's classification cites these clauses |
| SPEC §8.4.10.4/§8.4.5, §15.6.4 (callee summaries under own premises) | `15-ownership-and-lifetime-analysis.md:635`: summaries are computed once per declaration in terms of its own Type parameters and premises; requirement calls use effect bounds (§8.11.3, §8.11.4) | Holds; A4 implements it |
| SETTLED "Instance-evaluated generic defaults" | OQ15 → R5-M11 | Rewritten at H15 as an explicit revision against `MirBuilder.Defaults`, with its own independent review |
| DIAGNOSTICS rules 3, 6, §3, §4.1, §4.2, §11/§12 | Rule text was revised at R1; `DIAGNOSTICS.md:163-174` still describe old-analysis behavior (escaped borrows, Origin relations, unreachable source, Semantics cases) | H15 updates the affected facts (Auto row); no policy change |
| VERIFICATION, HOVER | VERIFICATION's `-Pipeline` paragraph; HOVER has no "Ownership checks" row (`HOVER.md:109` mentions ownership timing only) | H12 and H6 update them; HOVER gains the line's meaning only if a row exists (Auto: none needed) |

No SPEC or IMPL change is proposed.

## 7. OQ12–OQ15, answered

Plan §2.2's outcomes: "Every question was answered 'as recommended' except OQ17 (b) and (c)". OQ12: "R7 for Binding's
forwarding-property reads and ARCH's Binding-wide Syntax restriction; Parsing and root-helper reads at H14" (R5-M9). OQ13: "R7,
after the CallPlan pipeline gives syntax-free selection" (R5-M9). OQ14: "ARCH:135 and ARCH:149 amended; the 17 real sites fixed
(Q16)" (R5-M10, applied at ARCH `:172-174`, `:189`). OQ15: "The SETTLED rewrite is performed at H15 as an explicit, formal
revision; the R4 session's note: write it against the implemented inline fold (`MirBuilder.Defaults`), with its own independent
review" (R5-M11).

## 8. The exit gate (plan §5.5)

| Item | Satisfied by | Evidence |
| --- | --- | --- |
| 1 MIR decides; Session green; snapshot differences classified; `VerifiedSpecExampleTest` MIR-only; M12's OldRule (25), spec-case Old (34 today, plan 22) and pair-case Old (1) at 0 | H5 (H3b's table, each row with its class and category, H0b-14, review N4), H10a | H5's Session + milestones + `'*.ll'` + `-DiagnosticSnapshot` evidence; greps at H15 |
| 2 Zero-reference rows; Ownership ratchets at 0 | H10b, H11 (D-c4) | `ArchitectureRulesTest` rows in the H11 Session |
| 3 No `[Transitional("R6")]`/`("R6-check")` except R7 postponements | H5, H10a, H10b, H11, H12, H14, G318 | grep at H15; the 37 retags cite R5-M9; Q-c2's answer if `RequirementImplementation` is retagged |
| 4 `KIMI_PIPELINE`, `CompilerPipeline`, `-Pipeline`, `[mir]` removed | H12 | grep over src, tests, scripts, docs |
| 5 MIR problems through `MirReport`; TransferRequired in Binding; only A4's effect checks | H3, H1, H4, H5 | their tests (H0a/H0b) |
| 6 ARCH:135/:149 hold or amended | done (R5-M10); H13 confirms | H13's table |
| 7 PLAN10-P3 "BuildMir + collection"; warm zero allocation | H9 (stage map), H15 (record) | `artifacts/benchmarks/` record; `Purpose=Allocation` tests |
| 8 Records and the final review | H15 | STATUS (pipeline-lag line, D2 rows), PLAN, CODEMAP (50 lines naming an old component; plan: ≈ 65), DIAGNOSTICS :106-174, `tests/milestones/README`, ARCH ledger and freeze end (§5.2), area sizes, hotspots, duplication candidates, the owner's review of §5.1–§5.2 |

## 9. Open questions

| ID | Addressee | Question (one line) |
| --- | --- | --- |
| Q-c1 | BU | Deleting the wide-division gate lets `InstanceCollector.Run`'s "no records once the program has findings" (G157 (1)) hide A3b's records: fix G157 (1) before H7, or keep the gate until it is fixed? |
| Q-c2 | R4 | Does A-12 (A4's Witnesses reading `TyCtxt.Comparison`) land before H11, so the private composition and `RequirementImplementation` go then? If not, the owner approves `RequirementImplementation`'s retag to R7. |
| Q-c3 | the owner | Add R4-M159's G273 stop to ARCH's D5 exception list now (it is recorded in PLAN and the code, not ARCH)? Recommended: yes, in the next ARCH edit. |
| Q-c4 | R4 | G121's lost `do` fallthrough record (2x-P10): fixed before H5, or a classified row at H5? |

## 10. Not done

- No reading of the H0a/H0b parts (MirReport publication, the flip); §2's constraints on H5 are stated for their reconciliation
  (reconciled in `H0-design.md`, rulings R1–R4).
- H8a–c's per-file split, and which of `OwnershipAnalysisTest`/`EnumOwnershipTest`/`MatchOwnershipTest`'s 69 methods have MIR
  successors; the plan's "114 methods, 52 files" for H8c could not be reproduced with the Analysis-declared enum set (10 files today).
- H13's site-by-site classification; H14's exact counts (§4.4 gives upper bounds).
- Clause-by-clause comparison of PLAN10 §5.6.3 with SPEC §14 (§6, row 3).
