# R6-gen G4: removing the R5 scaffolding

Status: G4's design (plan §4.9 row G4, review G:R6-gen-del, R5-M18 form 2), revision 0, for the R5 planning session. Working record, not
authority: what it decides becomes authority only as the DECISIONS entry of §7.

Base: `origin/claude/r6-g3` `f6828983f` (G3's phase-1 checkpoint, carrying G2 at `bd4f31b0c`). `claude/r6-g3`'s local merge `703297a9d`
(G2's `13da65219`) is unpushed; the worktree `bin/worktrees/r6-g3` is clean, so phase 2 has not changed a file yet. "Plan" is the
[R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` = `src/Kimi/Compiler/`,
`T/` = `tests/xUnitTest/Tests/`. Line numbers are at the base. No build, test or probe was run. Counts come from grep and a method-span
script over the base (scratch only). **(H)** marks an unchecked claim. **(P2)** marks anything that depends on G3 phase 2's final shape,
whose brief (Q1 (b), Q2 (a)) is known but whose text is not. To avoid PLAN's issue G4 (Weak), commit messages say "R6-gen G4".

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| G4-1 | G4 ends the Transitional selection (`KIMI_CODEGEN`, `Compilation.Codegen`, `CompilerCodegen`) and with it G3's rollback window. After G4 no product path reaches the old generator. The old generator stays as test-only code (and two `src/Benchmark` readers) until G7 | 1 |
| G4-2 | G4 deletes R5's metric machinery (PD10): the pending ledger, its gate and tooling, the deviation form and `.deviation` support. It also deletes OQ9's VERIFICATION rows and the coverage-row remnants in comments and ARCH | 2 |
| G4-3 | A pin that reaches the old generator only through `Compilation.WriteIr`/`Validate` cannot be re-pointed. `ArchitectureRulesTest`'s limit-0 row bans `.Emission.WriteIr`/`.Validate` in tests (`T/ArchitectureRulesTest.cs:55`), so G4 **deletes** these tests: G157 (21)'s 76 injection methods and K13's 5 own-limit methods, as PLAN:29 schedules ("pending removals ... at G4"). "Re-point" therefore reduces to dropping the pin on the 2 tests that already read `Compilation.Emission.TryPrepare` (G5 deletes them) | 3 |
| G4-4 | One pin observes a rule: TypeDagScaling's chain generation (a scaling check). Dropping its pin moves it to MIR code generation. The tests that ran on both generators keep their MIR row; the legacy row goes | 3 |
| G4-5 | No restructuring commit is needed: G4 adds no row or function. It lands in four commits (tests, tooling, selector, records). Each is behavior-preserving on the default, shown by the snapshot, IR identity against G3's landing run and the ratchets, which only fall | 4 |
| G4-6 | Duplicates the selection leaves behind go in the same unit: `MinimalEmissionTest.Analyze`'s `codegen` parameter, `CLayoutUses`' `sel` twin, `--analyze-cost`'s three configurations (one remains) and the scripts' selector lists | 5 |

## 1. Required meaning

**What G4 ends.**
- The Transitional selection of PD4: `KIMI_CODEGEN` and the five `[Transitional("R6-gen")]` declarations of `C/Core/Compilation.Switch.cs`
  (`:27`, `:32`, `:63`, `:94`, `:115`). After G4 every compilation generates through `ModuleCodegen`. `Compilation.Generate` keeps
  the gate (`C/Core/Compilation.Generation.cs:91-111`) and loses its old-generator branch. G3's rollback (`KIMI_CODEGEN=legacy`,
  R5-M152 S6, "rollback for one unit") closes. From then on a regression is fixed forward, or G4 and G3 are reverted together (plan
  §5.2's revert rule).
- R5's metric machinery (PD10): M1's pending ledger and gate, M7's deviation form, `.deviation` support in E4's comparison, and OQ9's
  VERIFICATION rows (R5-M8: "both removed at G4"). Coverage-row remnants: PD11's rows ended at X2 (R5-M146), and only references in
  comments and ARCH remain (§2.5).
- The pins (R5-M85: "every test pinned to the old generator ... goes by G4 at the latest"; R5-M152 S7).

**What stays.**

| Kept | Until | Why |
| --- | --- | --- |
| `KIMI_PIPELINE`, `CompilerPipeline`, `Compilation.Pipeline`, `-Pipeline`, the `[mir]` mark, the manifest's `pipeline mir` | H12 | The difference ledger's observation (R5-M156; `MirDifferenceLedger.cs`, R4's hold) |
| `LlvmEmitter` as `Compilation.Emission` (`C/Core/Compilation.cs:118`): `TryPrepare` inspections in tests (count 114, limit 119), `CompilerPlanMeasurements.cs:116`, `InheritanceLifecycleMeasurements.cs:131` | G5 (tests), G7 | Plan G5/G7; S7's call-graph order |
| `LlvmEmitter.ReportFailure` for a publication failure (`src/Kimi/SolutionAndProject/Project.cs:272`) | G9b | `ArtifactPublicationFailed_Kd` (R5-M18) |
| `WriteIr`/`Validate` adapters, now MIR-only, with `Refusal`/`MirFailure` (`Compilation.Generation.cs:58-85, 218-234`) | G9b | R5-M64 |
| Gate step 1; step 2's arena-skip `Unsupported_Kd` | H3/H5 | R5-M96, R5-M148 |
| PD8's three `EmitFixture` checks (`T/ScalarEmissionTest.cs:135-140`) | — | PD8 |
| `compare-fixtures.ps1 -O2Metrics` (information) | — | Still compares two runs (G7/G8 bp, O2 records); only its "MIR/legacy" wording goes |

**Invariant after G4.** Every generation request (`EmissionArtifacts.Publish`, `Project.PublishArtifacts`, `kimi build`, `kimi test`, the
adapters) writes MIR code generation's module or its records. The old generator runs only from tests reading `Compilation.Emission` and
from the two benchmark readers. Default output is unchanged: G3 made MIR the default, and G4 deletes only the non-default path.

## 2. Census of deletions, by owner

Sizes are lines at the base unless marked (P2). Net totals are in §4.3.

### 2.1 The selector (Core and its readers)

| Site | Deletion | Size |
| --- | --- | --- |
| `C/Core/Compilation.Switch.cs` | `DefaultCodegen` (`:25-28`), `codegen` (`:32-33`), `Codegen` (`:61-75`), `CodegenOf` (`:89-100`), `CompilerCodegen` (`:114-123`). `CompilerIdentity` (`:35-44`) becomes `DefaultPipeline == Mir ? Version + " [mir]" : Version`. The header (`:3-13`) loses the code generator, R5-M156's pairing note and "G4" from Removal | ≈ −45 of 123 |
| `C/Core/Compilation.Generation.cs` | `WriteIr` (`:61-69`) and `Validate` (`:78-84`) lose the old branch. `Generate` (`:98-110`) is the gate plus today's `GenerateMir` (fold it in, one method). Header `:5-11` ("under MIR code generation …; under the old generator its own record …"), `:14-15` ("the old generator checks its own inputs"), `:26-33` ("three stage measurements" becomes two; "fall back to the old generator") | ≈ −25 |
| `src/Kimi/Testing/TestCatalog.cs:119-122` | the `codegen legacy` manifest rows | −4 |
| `T/PipelineSwitchTest.cs` | `Configurations` (`:27-31`) becomes the two pipelines. `TheEnvironmentSelectsTheCodegenOnEitherPipeline` (`:47-72`) is deleted. `RaceTheFirstCompilationsOfAFreshProcess` (`:85-112`) keeps `DefaultPipeline` only. `EachNonDefaultSelectionMarksTheIdentity` (`:115-131`) keeps `[mir]` and `pipeline mir`. `MirCodegenWritesEachOutputForm` loses its legacy half (`:147-158`). `TheGateRefusesMirRecordsForEveryCaller` drops its pin (`:194`). `Manifest` (`:445-450`) drops `codegen`. 18 pin sites | ≈ −75 |
| `tests/xUnitTest/FreshProcess.cs:18` | `"KIMI_CODEGEN"` from `SelectionVariables` | 0 |
| `T/MinimalEmissionTest.cs:328-331` | the `CompilerCodegen? codegen` parameter of `Analyze` (the pin channel) | −2 |
| `src/Benchmark/Benchmarks/CompilerPipelineBenchmark.cs:107-121` | the legacy branch (R5-M94: "G4 … deletes the branch"). G7's "four `.Emission.` readers" become two (the base has three) | −9 |
| `src/Benchmark/Benchmarks/AnalyzeCostMeasurements.cs:22-27, 35-37, 61-99` | **One configuration** replaces the three (coordinator's addition). Since R5-M156 they measure the same check, and R5-M156's measurement found mir and sel equal to the default within spread. `Create` sets no selection; the `configuration` field goes (`schemaVersion` 2 → 3). Rejected: one row per `KIMI_PIPELINE` value until H12. In Benchmark no observer subscribes, so a `mir` row repeats the default row exactly. A process that sets `KIMI_PIPELINE` is still named in `compiler` (`[mir]`) | ≈ −12 |
| `src/Benchmark/Benchmarks/CollectionCostMeasurements.cs:184` | `c.Codegen = Mir` (the pipeline line is H12's) | −1 |
| `src/Benchmark/Benchmarks/DiagnosticMeasurements.cs:208` | the `codegen` JSON field | −1 |
| `src/backend/windows-x64/benchmark-o2-corpus.ps1:21`, `test-library.ps1:24, 84` | `KIMI_CODEGEN` from the inherited-selector checks and records (an inherited value selects nothing after G4) | 0 |
| `scripts/verify.ps1` | `-Codegen` (`:28-29, :48`), `$Codegen` set/restore (`:80, :82, :84, :384`), `codegen` in `summary.json` (`:385`) (P2: G3 flips the default to `mir`) | ≈ −10 |
| Docs | `src/Benchmark/CompilerPipeline.md:38-46, 78-82, 118-119`; `O2Corpus.md:33-35`; CODEMAP:55 (`Codegen`, `KIMI_CODEGEN`, `-Codegen`), :64 ("`PipelineSwitchTest` the selector", "under MIR code generation"); ARCH:98 ("under mir, the code generator selector", stale since G2), :99, :147 ("under MIR code generation") (P2: G3 rewrites some) | Auto |

**Mir pins** (`CompilerCodegen.Mir`, 20 sites in 7 files: PipelineSwitchTest 12, `LayoutAttributeBindingTest.CLayoutUses.cs:369, :372`,
`FunctionCodegenTest.cs:1245, :1642`, `FunctionCodegenTest.Testing.cs:290`, `InstanceCollectorTest.cs:2086`, `GeneratedIrTest.cs:146`,
`SyntaxIdTest.Transplants.cs:22`): the codegen half of each assignment goes. A `c.Pipeline = Mir` beside one stays for H12.

### 2.2 The pending ledger (R5-M66, R5-M73, R5-M81; R5 E10/E11)

| Site | Deletion | Size |
| --- | --- | --- |
| `tests/xUnitTest/TestData/codegen-pending/C-frame.tsv` | the empty ledger and its directory (R5-M156: "C-frame.tsv kept, G4 deletes it with its tooling") | 1 file |
| `scripts/codegen-pending.ps1` | the whole file: read, filing rule `Get-KimiCodegenGroup`, run, totals, report, `Update-KimiCodegenLedger` | −199 |
| `tests/scripts/CodegenPendingTest.ps1` | the whole file | −225 |
| `scripts/verify.ps1` | header `:30-33`; `-UpdatePending` (`:60`, `:69`); the dot-source (`:73`); `:78`'s "codegen-pending.ps1" comment; full theory names (`:179-180`); the test step's sel branch (`:190-194`); `Test-Selection` back to Unit only (`:280-281`); the native step's sel branch (`:318-324`); the `codegen pending` step (`:342-355`); `update pending` (`:371-382`); `codegenPending` (`:386`) (P2: Q2 (a) gates these on an explicit `-Codegen mir`) | ≈ −45 |
| `scripts/verification-native.ps1:1-3, 12-20` | `Get-KimiNativePendingResult` and the header's "pending-coverage" clause | −10 |
| `tests/scripts/VerificationNativeTest.ps1:32-56` | its pending assertions | ≈ −25 |
| `tests/scripts/NativeFixtureRunnerTest.ps1:127-192` | the launch-gap case keeps its runner half (an incomplete record, `:177-178`) and loses the ledger half (`:129-130`, `:179-191`). CI runs this script (`.github/workflows/test.yml:66`) | ≈ −15 |
| Docs | VERIFICATION:94 (the `-Codegen mir` sentences from "Under `-Codegen mir`" to the `CodegenPendingTest.ps1` regression), :96 (R5's sel paragraph, P2: OI-3 ends the lane at G3), :252-253 ("codegen-pending.json", "pending codegen defect", "ledger updates"), :259 ("pending-ledger rejection", "No incomplete result may update the pending ledger"); CODEMAP:13 ("pending-coverage evidence"), :16-17 (`codegen-pending.ps1`, `CodegenPendingTest.ps1`); `src/backend/windows-x64/README.md:87` ("cannot become a known pending codegen failure") | Auto |

### 2.3 The deviation form (R5-M85, R5-M106; E12)

After G3 phase 2 the catalog is empty, the 54 `Expect` uses (38 constant-temporaries, 16 inline-env) and both `LegacyNative` sites are
reduced, and `CodegenDeviationTest` runs on synthetic samples (Q1 (b), P2).

| Site | Deletion | Size |
| --- | --- | --- |
| `tests/xUnitTest/Codegen.cs` | the whole file: `Codegen.Legacy`, `Expect`, `Sidecar`, `LegacyNative`, `Deviation`, `DeviationClass`, `CodegenDeviations` | −146 at the base (P2) |
| `T/CodegenDeviationTest.cs` | the whole file | −81 (P2) |
| `T/ScalarEmissionTest.cs:131-161` | the `LegacyNative? legacy` parameter of `EmitFixture`/`WriteFixture` and the sidecar block `:153-160` | ≈ −10 |
| `scripts/compare-fixtures.ps1` | header `:6-9`; `codegen` (`:66`, `:123`); the deviating block (`:101-120`); `deviating` (`:129`), its text line (`:141`) and section (`:145`); `-O2Metrics`' "R5's MIR/legacy measurement" (`:11`) reworded | ≈ −25 |
| `tests/scripts/FixtureComparisonTest.ps1` | `-codegen` of `New-Run` (`:18-20`); the deviation cases (`:113-149`) | ≈ −37 |
| Before deleting | grep 0 for `Codegen.Expect`, `legacy:` (2 named arguments at the base, `InheritedObjectCallTest.cs:52`, `StaticInitializationTest.cs:135`), `new LegacyNative(`. A remaining one is a G3 omission and blocks G4 | — |
| Docs | VERIFICATION:127-131 (the deviating clause), :141-143 ("R5 measures MIR code generation against a legacy run", reworded as information) | Auto |

### 2.4 The old-generator pins

See §3.

### 2.5 Coverage-row remnants (PD11, ended at X2, R5-M146)

No row, type or test remains, only references: ARCH:47-49 ("under MIR code generation, until H3 …; the codegen coverage rows of PD11 ended
at X2"), `C/Backend/Codegen/FunctionCodegen.cs:7` ("which ended PD11's coverage rows"), and the comments
`T/FunctionCodegenTest.cs:1372-1373, :1552`. G4 trims them to the present rule. The net is about 0 lines.

### 2.6 Records describing them

| Document | G4's change |
| --- | --- |
| DECISIONS | G4's entry (§7) and amendment lines on R5-M8, R5-M18 (temporary sel-only behaviors), R5-M35, R5-M64, R5-M66, R5-M73, R5-M81, R5-M85, R5-M94, R5-M106 and R5-M152 (S6, S7), each naming G4's entry |
| ARCH | :98, :99, :147 (§2.1); :254 (Transitional list: drops "the `KIMI_CODEGEN` selector, the pending ledger, the `CodegenDeviations` catalog and `.deviation` targets (G4)"); :279-281 (R6-gen progress drops M7 and "the pending ledger (0 from G2)"); :47-49 (§2.5) |
| VERIFICATION | §2.1-§2.3 rows: the end of OQ9's rows (P2: G3 writes "`-Codegen legacy` is the rollback lane until G4", which G4 deletes) |
| STATUS | Rows `:71` (static cycle, P2: G3 corrects it), `:72` Virtual dispatch, `:73` Updates, `:74` Tuple ordering (all "(old generator)"). They describe a generator no product path runs after G4, so they go now (AGENTS: Auto, a known limit changes), not at G11 (Q-P3) |
| DIAGNOSTICS | `:59`'s old-generation sentences ("Generation retains a refused generic instance's pooled ownership issues … `GenericGenerationDiagnosticTest` exercises publication and recovery"). The contract leaves product, and the named test leaves with §3's P2 |
| CODEMAP | §2.1-§2.2 rows |
| PLAN (planning session) | §3 hold lines: R5's pending-ledger hold (PLAN:34) ends. R6-gen's `Compilation.Pipeline.cs`/`Compilation.Switch.cs` hold is released in G4's merge (R5-M152). PLAN:29's "Pending removals: K13a-K13c's and G157 (21)'s pinned old-generator tests at G4" is done. G6's count is restated (§3) |

## 3. The `CompilerCodegen.Legacy` pins

Re-count at the base: **99 lines, 101 occurrences, 71 files** (98 lines in 70 test files, plus `tests/xUnitTest/Codegen.cs:24`). G3's
census said 102 in 71; the difference is the counting method (H). The coordinator's addition (H, not yet on `claude/r6-g2` at
`13da65219`): G2's fold restores `InstanceCollectorTest.TheDepthLimitCountsPlacedLevels`' old half for the 64-level rows with a pin
until G4 (R5-M73's depth test). That makes **100 lines**.

**The constraint.** `Compilation.WriteIr`/`Validate` follow `this.codegen` (`Compilation.Generation.cs:61, :78`). Once the field goes,
a pinned test that reaches the old generator through them would run MIR code generation. Re-pointing it to `c.Emission.WriteIr`/
`.Validate` would raise `ArchitectureRulesTest`'s limit-0 row "Test calls of the old analysis and generation entry points"
(`T/ArchitectureRulesTest.cs:55`) by 218 calls (206 in the injection methods, 12 in the own-limit ones). That is a larger exception
needing the owner (AGENTS). Re-pointing to `c.Emission.TryPrepare` would raise the 119 row. So a pin is deleted with its test, or
dropped where the test already reads `Compilation.Emission`. Both are R5-M152 S7's options ("deleted or re-pointed").

| Group | Sites / methods / files | At G4 | Reason | Later row |
| --- | --- | --- | --- | --- |
| P1 G157 (21)'s injection tests: mutate `c.Ownership.Bodies` (65 methods) or a published syntax fact or profile (11: `SetSymbol`, `SetType`, `SetConversion`, `closure.Signature`, `DefaultArguments`, `IrTarget`), then assert that the old generator refuses through the adapters, then recover. Examples: `T/AggregateEmissionTest.cs:73`, `T/CharEmissionTest.cs:131`, `T/IntegerEmissionTest.cs:211`, `T/GuardedDefaultContinuationTest.cs:81`, `T/EmissionPlanTest.cs:54` (also one `TryPrepare`) | 77 / 76 / 59 (≈ 2,340 lines incl. attributes) | **delete** | They observe the old generator's own representation: its plans, slots, profile and failure text (PLAN:29's G157 (21) classification). MIR code generation trusts validated MIR (AGENTS: one validator per IR, `MirValidator`), so no MIR counterpart is owed. Recovery and reload equality on MIR are covered elsewhere (`WideIntegerEmissionTest.SerializationRebuildsBothHalvesOfWideValues`, `NumericConversionEmissionTest`'s MIR half) (H) | PD14's G6 had 87 injection reads; G4 deletes 75 of the 159 `Ownership.Bodies` reads |
| P2 K13a-c's own-limit methods: `DestructorSubstitutionsRespectTheGenerationLimit` (`T/GenericDestructorEmissionTest.cs:191`), `OversizedSubstitutionSetsAreResourceDiagnosed` (`T/GenericStorageEmissionTest.cs:295`), `TypeAndLengthSlotsAppearOnceInDeclarationOrder` and `InstanceStorageLimitsKeepResourceFactsAndRecover` (`T/GenericGenerationDiagnosticTest.cs:75`, `:109`), `ConcreteInstanceLimitsRemainResourceFailuresAndRecover` (`T/OwnershipStorageLimitTest.cs:18`); with them the depth test's restored old half (H) | 5 (+1 H) / 5 (+1) / 4 (+1) (≈ 134 lines + H) | **delete** | They test the old generator's own limits (`GenericStoragePlan.SubstitutionSetLimit`, `InstanceStorageLimit`, its "While instantiating" note, `LlvmEmitter.cs:413`, and the agreement of the old pool's depth). R5-M85: "goes by G4 at the latest". SPEC 21.3.2/23.1.1's rule stays on the MIR side: check-time `GenerationResourceLimit_Kd` (`AssertGenerationResourceLimit`'s 7 callers, G2), A1's arena `OwnershipStorageLimit_Kd` (`T/MirDataflowTest.cs:53`), and the MIR half of the depth test | G7 deletes the limits themselves |
| P3 Already direct: `ConditionalFlagsRejectMissingOrIncorrectTransitions` (`T/ElementBorrowOwnerEmissionTest.cs:110`), `ConditionalPathFlagsRequireParameterInitialization` (`T/ElementParameterMoveEmissionTest.cs:95`) | 2 / 2 / 2 | **drop the pin** ("re-pointed") | They read `c.Emission.TryPrepare` and `BodyLowering.Validate*Flags` directly. Their last `c.WriteIr(TextWriter.Null)` then runs MIR code generation, which writes the program (H) | G5 (its 8 `Validate*Flags` methods) |
| P4 Both-generator rows: `GeneratedIrTest.OneRequirementHoldsForBothGenerators` (`:53-59`, `Generate` `:142-149`), `FunctionCodegenTest.TheStorageBytePrimitivesAreDefinedOnceFromTheirTexts` (`:1206-1209`), `SyntaxIdTest.CompoundTransplants…` (`.Transplants.cs:12-29`), `NumericConversionEmissionTest.ConversionPlansAreRevalidatedAndSurviveReload`'s old half (`:226-236`), `PipelineSwitchTest.MirCodegenWritesEachOutputForm`'s legacy half | 5 / 5 / 5 | **delete the legacy row or half** | The rule (IMPL 21.4.1/21.3, one storage text, transplant binding, reload equality) keeps its MIR row. The transplant theory loses the `mir` parameter: since G2 the pipeline changes nothing there, so its `false` rows would be duplicate cases. Fixtures `G235TransplantedFunctionLegacy{DonorFirst,RecipientFirst}` leave. The MIR stems keep their names, so the comparison stays exact (§4.2) | — |
| P5 Check-only pins (no-ops since G2: `Compilation.Pipeline.cs` reads no code generator): `MirBorrowsTest.Guard.cs:558, :645` (R4's test file: hold or R4's consent, §4.4), `TypeDagScalingTest.OldPath` (`:328`; its 7 callers read `Problems`), `LayoutAttributeBindingTest.CLayoutUses.cs:368-378` (`Configurations`, `Name`, `Check`) | 4 / 4 / 3 | **drop the pin** | Analysis and publication are the same under either generator (R5-M156) | H12 the pipeline parts |
| P6 A rule: `TypeDagScalingTest.OldAnalysisWalksAreLinearInDistinctParts`' chain generation (`:143-146`, through `OldPath`) | in P5's site | **moves to MIR** by dropping the pin | A scaling check (AGENTS: scaling checks for affected analyses): chain shapes at 26 must generate. After G4 it checks MIR code generation, the product's generator (H: X1's sel runs did not exercise it pinned) | — |
| P7 The selector's own tests (`PipelineSwitchTest`) | 6 / 5 / 1 | delete or reduce (§2.1) | The selection is gone | H12 the rest |
| `Codegen.Legacy` readers (`Codegen.cs:24`, `ScalarEmissionTest.cs:159`, `CodegenDeviationTest.cs:47`) | 3 | with the form (§2.3) | — | — |

Totals: **81 methods deleted** (≈ 2,474 lines, in 62 files; none is left without a test), 2 + 4 pins dropped, 5 legacy rows or halves
deleted, PipelineSwitchTest reduced. Orphaned helper constants (`Conditional`, `Snapshot`, `Echo`, `Cell`, `Same`, …) go with their last
user (H; the implementer greps each).

**Ratchets that fall** (`T/ArchitectureRulesTest.cs`; each limit lowered to its new count in the same commit):

| Row | Limit | Count now | After G4 |
| --- | --- | --- | --- |
| Test reads of ownership bodies (`:57`) | 159 | 159 | 84 (H) |
| Test inspections of a prepared emission module (`:60`) | 119 | 114 | 113 (`EmissionPlanTest`) |
| Test reads of instance analysis (`:61`) | 7 | 7 | 5 (or 1 with Q-P4 (b)) |
| Test reads of generation failure facts without an entry point (`:62`) | 9 | 9 | 1 (`GenericStorageEmissionTest.cs:323`, G5) |
| Test calls of the old analysis and generation entry points (`:55`) | 0 | 0 | 0, kept (the reason for G4-3) |

G6 after G4 (H): 84 `Ownership.Bodies` reads, target 33 (24 with R4's U10). That leaves about 51 reads (the 40 pairing reads and about 11
unpinned ones), so G6 drops from its planned ≈ −1,800 to roughly −1,000. Within G:R6-gen-del the total is unchanged.

## 4. Order, commits and checks

### 4.1 Start condition and holds

- G2 and G3 have landed together on dev after G:R6-gen-switch, and **G3 has passed its full sweep on dev** (plan row G4, G0 §9: `'*.ll'`
  at O0/O2, milestones, CLI harnesses, snapshot, O2 corpus). G4 branches from that dev head. Its reference runs are that landing's
  default Session (snapshot, fixture directory, `summary.json`) in `artifacts/verify/`.
- First the implementer re-runs this census at that head, against P2: verify.ps1, VERIFICATION, Codegen.cs, CodegenDeviationTest,
  STATUS:71, CompilerPipeline.md, the 0 `Expect` sites.
- Holds: `Compilation.Switch.cs`, `Compilation.Pipeline.cs` (R6-gen through G4; Pipeline.cs needs no edit), `Compilation.Generation.cs`.
  The pending ledger (R5's PLAN:34) is released at G4's merge. Peer test files: `MirBorrowsTest.Guard.cs` (R4) and the
  `LayoutAttributeBinding*` and `TypeDagScaling` tests (Binding's) each lose a pin line only, under a PLAN §3 advisory line. No other
  active branch adds a pin, `Expect` or ledger row (checked over the 25 newest `origin/claude/*` branches: only `r6-g2` and `r6-g3`).

### 4.2 Commits

| # | Commit | Content | Checks (the implementer's Unit runs; one Session at the end) |
| --- | --- | --- | --- |
| c1 | tests: the pinned old-generator tests | §3 P1, P2 deleted; P3, P5, P6 pins dropped; P4 rows and halves deleted; `CLayoutUses`' `sel` twin (§5); ratchets lowered. Compiler untouched; the selector still exists | Unit: the 62+ touched classes and `ArchitectureRulesTest`, on the default |
| c2 | tooling: the pending ledger and the deviation form | §2.2, §2.3; verify.ps1's ledger and sel branches; VERIFICATION, CODEMAP, README rows | `FixtureComparisonTest.ps1`, `VerificationNativeTest.ps1`, `VerificationSelectionTest.ps1`, `VerificationInputsTest.ps1`, `NativeFixtureRunnerTest.ps1` (toolchain); Unit `ScalarEmissionTest`, `InheritedObjectCallTest`, `StaticInitializationTest` with `-Fixtures 'InheritedObjectReceiverLayers*.ll','StaticInitializationCycle*.ll'` |
| c3 | the selector | §2.1 (Core, Testing, Benchmark, scripts, Benchmark docs, ARCH :98/:99/:147/:47-49/:254/:279-281, STATUS, DIAGNOSTICS, CODEMAP) | Unit `PipelineSwitchTest`, `CheckJsonOutputTest`, `InstanceCollectorTest`, `FunctionCodegenTest`, `GeneratedIrTest`, `SyntaxIdTest`; the slnx build; `Benchmark --compiler-pipeline-check`, `--collection-cost-check`, an `--analyze-cost` smoke run |
| c4 | DECISIONS | §7 entry and the 11 amendment lines | Doc |
| — | Session on c4's head | `-Mode Session -DiagnosticSnapshot -DiagnosticBaseline <G3 landing>/diagnostic-snapshot.json`; `-Fixtures 'InheritedObjectReceiverLayers*.ll','StaticInitializationCycle*.ll','G235*.ll'`; the 40 milestones; `test-library`, `test-emission`, `test-cli`, `test-modules`, `test-testing`; then `compare-fixtures.ps1 -Baseline <G3 landing> -Candidate <run>` | The proof below |

DECISIONS and ARCH lie in the input manifest, so the Session runs after c4, and one Session covers c1-c4 (AGENTS). Each commit still has
its own Unit evidence for `verify-commit.ps1`.

**Why no restructuring commit.** G4 adds no row, function or file, and nothing it deletes needs a structure moved first. Folding
`GenerateMir` into `Generate` is part of the deletion. Each commit is behavior-preserving for the default configuration, and the
Session proves it:

- **Diagnostic snapshot**: 0 differences against G3's landing snapshot (no record moves).
- **IR identity**: `compare-fixtures` reports every common `.ll` and expectation sidecar byte-identical and native results equal. Missing
  are exactly `G235TransplantedFunctionLegacy{DonorFirst,RecipientFirst}.*`; nothing is added (H). G4 touches no line of Backend, MIR
  or the toolchain, so identical `.ll` and sidecars give identical native outcomes. **No `'*.ll'` sweep is needed** (Q-P2): the three
  families above run natively for the changed fixture writer.
- **Ratchets** fall only (§3). The semantic snapshot is not needed (no Binding or MIR change).
- **Identity**: the default `CompilerIdentity` and test manifest are unchanged (`CheckJsonOutputTest`, `PipelineSwitchTest`).
- **Zero-grep** over src, tests, scripts, `src/Benchmark`, docs/dev (except DECISIONS and history): `KIMI_CODEGEN`, `CompilerCodegen`,
  `DefaultCodegen`, `CodegenOf`, `.Codegen` of a compilation, `Codegen.Expect`, `LegacyNative`, `CodegenDeviation`, `codegen-pending`,
  `CodegenPending`, `codegenPending`, `UpdatePending`, `.deviation`.

### 4.3 Expected net size (H; P2 for the form and verify.ps1)

| Area | Net |
| --- | --- |
| `src/Kimi` (Core, Testing) | ≈ −75 |
| `src/Benchmark` (code and `.md`) | ≈ −35 |
| `scripts/` | ≈ −280 |
| `tests/scripts/` | ≈ −300 |
| `tests/xUnitTest/` | ≈ −2,950 (P1 −2,340, P2 −134, the form −227, PipelineSwitchTest −75, the rest −175) |
| `tests/xUnitTest/TestData` | −1 file |
| docs (VERIFICATION, ARCH, CODEMAP, STATUS, DIAGNOSTICS, README) | ≈ −15 long lines; DECISIONS ≈ +35 |
| **Total** | **≈ −3,600**, of which ≈ −2,500 are G157 (21)'s and K13's pinned tests. Without them, the plan's ≈ −400 holds as ≈ −1,100, because the plan did not count the ledger tooling's tests and the twin (§5) |

### 4.4 After G4

PLAN (planning session): hold release; next actions G5 and G6 (independent after G4, S7), then G7. Plan rows G5-G7 are restated with
§3's counts. G7's Benchmark readers are two. R4's reviewers stop running the sel pending check (R5-M8's ask A-10 ended at G3, P2).

## 5. Sites sharing a cause beyond the plan row

| Site | Cause | At G4 |
| --- | --- | --- |
| `MinimalEmissionTest.Analyze(…, CompilerCodegen? codegen = null)` (`T/MinimalEmissionTest.cs:328`) | An optional parameter steering a test helper: the pins' channel | Deleted |
| `LayoutAttributeBindingTest.AssertUses(…, bool sel)` (`.CLayoutUses.cs:313-317`; `sel: false` ×10, `.cs:140-141`, `.LayoutNeeds.cs:102`) and its twin `InstanceCollectorTest.CLayoutUses.cs` (33 lines, 3 theories over the same rows) | A test-side copy of the selection. Since G2 every configuration publishes the same records, and after G4 `sel` is `(Mir)`, already one of the default configurations: duplicate cases (AGENTS) | Delete the twin and the `sel` parameter (Q-P5; H: rows equal, checked by the run) |
| `--analyze-cost`'s three configurations, `CollectionCostMeasurements.Checked`'s "under sel", `DiagnosticMeasurements`' `codegen` field, `CompilerPipelineBenchmark`'s branch | Benchmark copies of the selection | §2.1 |
| `FreshProcess.SelectionVariables`, `benchmark-o2-corpus.ps1:21`, `test-library.ps1:84`, `verify.ps1`'s `codegen`, `compare-fixtures.ps1`'s `codegen`, `FixtureComparisonTest.ps1`'s `-codegen` | Scripts carrying the variable or its record | §2.1-§2.3 |
| A fallback reading `KIMI_CODEGEN` | None found: `Compilation.Switch.cs:28` is the only reader in src. After G4 a stale value selects nothing, because no other generator exists. A guard that rejects it was rejected: it would keep the name alive and be a second reading | No guard |
| `C/Backend/Instances/CheckedSet.cs:7`, `InstanceCollector.cs:14`: "its Language records on every pipeline and the others under MIR code generation" | A second statement of the publication rule, stale since R5-M156 (every record on every pipeline): **a G2 finding** | Fix in G2's review round, else in G4's c3 (Q-P4 (a)) |
| `T/GenericGenerationDiagnosticTest.cs:67-68, :149-150` (in `SuccessfulInstancesKeepTheExistingAllocationFreePath` `:62`, `WarmSuccessfulInstancePreparationAllocatesNothing` `:143`): `Assert.Null(c.Ownership.FailedInstance)`/`c.Emission.FailureInstance` after a MIR `Validate`/`WriteIr` | Vacuous since G3: MIR code generation never sets them. **A G3 finding** | Delete the 4 reads in G3 phase 2, else in G4 (ratchet 5 → 1) (Q-P4 (b)) |
| ARCH:98 "under mir, the code generator selector (DECISIONS R5-M35)" | Stale since G2 | c3 |
| "the old generator", "both generators" in comments (src 35/19, tests 49/27) | Mostly describe components the old generator still uses (`LlvmModuleText`, `LegacyRuntimeHelpers`) | Untouched; G7/G8/G11. G4 edits only text that states a selection G4 deletes ("under MIR code generation": src 6, docs 11) |
| PLAN issue "G4" (Weak) and the plan's unit G4 | A name collision | "R6-gen G4" in commits and the entry |

## 6. Alternatives, risks, open questions

### 6.1 Alternatives

| | Option | Cost | Verdict |
| --- | --- | --- | --- |
| A | As above: G4 removes the selector, the ledger, the form and every pin, and deletes the adapter-reaching pinned tests (PLAN:29) | ≈ −3,600 in one review group; G6 shrinks | **Recommended**: removes what the change makes redundant with its cause; no exception; no mode left |
| B | G4 as literally planned with re-pointing: `c.WriteIr` → `c.Emission.WriteIr` in the 81 methods until G6/G7 | +218 calls on a limit-0 ratchet (owner approval, AGENTS); ≈ 220 edits deleted again by G6/G7 | Rejected: a larger exception and a double edit for tests of a generator no product path runs |
| C | Fold the pins into G6/G7: G4 removes `KIMI_CODEGEN`, the ledger and the form, but keeps an internal test-only `Compilation.Codegen` and the generation entry's old branches | G4 ≈ −1,100; the Transitional setter and three branches live through G7 | Rejected: a mode steering three functions survives G4 (symptom list); contradicts R5-M85 ("goes by G4 at the latest") and plan row G4 ("the selector") |
| D | Fold G4 into G7 | The rollback lives until G7 | Rejected: contradicts S6 (rollback for one unit, owner-approved with R5-M18) |
| E | Split G4 into G4a (ledger, form, verify `-Codegen`, VERIFICATION) and G4b (selector and tests) | Two reviews, two Sessions | Acceptable if the reviewer wants smaller pieces; the commits of §4.2 already separate them |

### 6.2 Risks

| Risk | Response |
| --- | --- |
| A deleted injection test was the only check of a rule, not of the old generator's representation | Classification by mutation target and assertion target (§3). The 11 syntax or profile mutations are listed one by one in c1's message. `PointerWidthMustMatchTheEmissionProfile` (`T/IntegerEmissionTest.cs:211`) forges `IrTarget` by reflection; no product path does. The reviewer checks that list |
| After G4, MIR code generation has no rollback | G4 starts only after G3's dev sweep; plan §5.2 (revert G4 with G3) |
| G3 phase 2 differs from this design's P2 assumptions | §4.1's re-census at the landed head; the zero-grep |
| The fixture set drops 2 stems and the evidence shows "missing" | Expected, and listed exactly (§4.2) |
| `--analyze-cost` JSON loses `configuration` | `schemaVersion` 3; `CompilerPipeline.md` says so; old reports stay readable by hand |
| A peer branch adds a pin, an `Expect` site or a ledger row after G4 | None does today (§4.1); PLAN §3 states that from G4 none exists |

### 6.3 Open questions

**For the planning session** (none blocks starting c1-c3 at G3's landing):
- **Q-P1.** Accept G4-3: adapter-reaching pinned tests are deleted at G4 (ratchet `:55`), not re-pointed, which takes PD14's injection
  reads from G6, as PLAN:29's "pending removals … at G4" already reads. Recommended: yes; G4's entry records the reallocation and PLAN
  restates G6.
- **Q-P2.** Native evidence: IR identity plus three families plus the milestones, instead of a `'*.ll'` sweep. Recommended.
- **Q-P3.** STATUS's old-generator rows (`:71-74`) at G4 (the boundary changes here) rather than G11. Recommended.
- **Q-P4.** (a) G2's stale `CheckedSet.cs:7`/`InstanceCollector.cs:14` comments: fix in G2's review round. (b) G3's four vacuous
  instance-fact reads: delete in G3 phase 2. Each falls back to G4 if not done there.
- **Q-P5.** The `sel` twin (`InstanceCollectorTest.CLayoutUses.cs` and `AssertUses`' `sel`): delete at G4. Recommended.
- **Q-P6.** The DECISIONS number: R5-M159 if G3 takes R5-M158 (its brief).

**For the owner:** none under option A. It adds no vocabulary entry and departs from no owner-approved text: S6 and the plan rows hold,
and R5-M152 S7's "deleted or re-pointed" allows deletion. It weakens no check: ratchets only fall, the one rule-observing pin moves to
MIR, and the deleted tests guard code no product path runs. Options B (a larger exception) and C/D (keeping the selection past G4,
against S6) would each need the owner.

## 7. DECISIONS text (draft, number at G4's landing)

> **R5-M159 R6-gen G4: the R5 scaffolding removed** (2026-10-xx, R6-gen; plan §4.9 row G4, PD10, PD4, OQ9; under R5-M18, R5-M152 and
> R5-M158; Major, G:R6-gen-del) The code generator selection ends (G3's rollback, R5-M152 S6): `KIMI_CODEGEN`, `CompilerCodegen`,
> `Compilation.DefaultCodegen`/`Codegen`/`CodegenOf`, the `[codegen legacy]` mark, the manifest's `codegen` rows and `verify.ps1
> -Codegen`. `Compilation.Generate` is the gate and MIR code generation; `WriteIr` and `Validate` are MIR-only until G9b. No product path
> reaches the old generator; tests reading `Compilation.Emission` and two Benchmark measurements do until G5 and G7, and a publication
> failure's record does until G9b.
> - R5's metric machinery goes: the pending ledger with its gate, report, filing rule, `-UpdatePending` and regression (R5-M66, R5-M73,
>   R5-M81); the deviation form, its catalog, `.deviation` sidecar and `compare-fixtures.ps1`'s deviating count (R5-M85, R5-M106); OQ9's
>   VERIFICATION rows (R5-M8); `--analyze-cost` measures one configuration (its three were one check since R5-M156).
> - The pins (R5-M85, R5-M152 S7): `Compilation.WriteIr`/`Validate` follow the compilation's generator, and tests may not call
>   `Compilation.Emission`'s `WriteIr`/`Validate` (ArchitectureRulesTest, limit 0), so a pinned test that reached the old generator
>   only through them is deleted with its pin: G157 (21)'s 76 injection methods and K13's 5 own-limit methods (and R5-M73's restored
>   depth half). They observed the old generator's own plans, profile, limits and failure text; MIR code generation trusts validated MIR,
>   and the limits' rule is the collector's and A1's at check. Pins on tests that read `Compilation.Emission.TryPrepare` are dropped
>   (G5). TypeDagScaling's chain generation now checks MIR code generation. A test that ran on both generators keeps its MIR row.
>   PD14's injection reads thereby leave at G4, not G6. Ratchets: ownership bodies 159 → 84, prepared modules 119 → 113, instance reads
>   7 → 5, failure facts 9 → 1.
> - Shown behavior-preserving on the default: the diagnostic snapshot against G3's landing run 0; every common fixture `.ll` and sidecar
>   identical, `G235TransplantedFunctionLegacy*` removed with its row.
> - Rejected: re-pointing to `Compilation.Emission.WriteIr` (+218 on a limit-0 ratchet); a test-only `Codegen` setter until G7 (a mode
>   kept past its stage); a guard rejecting a stale `KIMI_CODEGEN` (a second reading of a removed name); one `--analyze-cost` row per
>   `KIMI_PIPELINE` value (identical checks).
> - Supersedes by name, each with its amendment line: R5-M8's VERIFICATION rows; R5-M35's, R5-M64's and R5-M94's selector clauses;
>   R5-M66, R5-M73, R5-M81; R5-M85 and R5-M106's native half; R5-M18's temporary sel-only behaviors; R5-M152 S6 (the rollback ended) and
>   S7 ("re-pointed" applies only to direct `Compilation.Emission` readers).
