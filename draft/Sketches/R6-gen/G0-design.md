# R6-gen G0: the switch design (K-package §6 applied to dev)

Status: G0's one design (plan §4.9 row G0, Doc, D+R under S2), revision 2 after the re-review (PASS) and its rulings. Working record, not authority:
what it decides becomes authority only as the DECISIONS entry of §6, and its owner items only by the owner's answers.

Base: dev `07a5ca2da` (code, DECISIONS, PLAN, STATUS). "Plan" is the [R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md);
"K" is `draft/Sketches/R5 Step 0/K-package.md`; S1–S8 are K §6 (owner-approved, R5-M18). Paths: `C/` = `src/Kimi/Compiler/`,
`T/` = `tests/xUnitTest/Tests/`. "sel" = `KIMI_PIPELINE=mir` + `KIMI_CODEGEN=mir`. Evidence read, not rerun: X1
(`artifacts/verify/reduction-R5/17cd554ab/`), `artifacts/verify/shared-base/228d6f0b4/` (default Session, mir-diff ledger) and batch
56's Sessions (`artifacts/verify/r5-units/session/batch56-228d6f0b4/`). No build or test was run. (H) marks an unchecked inference.

## Revisions

Revision 1 (findings B1, N1–N8) and revision 2 (R2-1–R2-5, the re-review's rulings):

| Finding | Change |
| --- | --- |
| B1 (blocking) | §3, §6: the five pinned own-limit methods keep their pins until G4, which deletes them or re-points kept ones to `Compilation.Emission` (R5-M85); the four unpinned ones change with their readers; S7's G4 list names the pins |
| N1 | §2 S1, §3: `CompilerIdentity`'s rule once the pairing goes; `OnlyMirMarksTheIdentity` in G2's list |
| N2 | §2 S2: the reading of "the 20 instance-failure methods" (not an owner item) |
| N3 | §6: R4-M1's supersession rests on R5-M4; R5-M145's "leave with P42" superseded where OI-1 applies |
| N4 | §4, §10: OI-1 re-posed as (a), (a′), (b) with exact costs (sel failure messages counted); recommendation (a′) |
| N5 | §2 S2: STATUS `:40`, `:89`, DIAGNOSTICS `:340`, PLAN G128 added to G2's list |
| N6 | §8: G157 (15)'s two parts, one unit before G3 |
| N7 | §0, §5, §8: G1's reason is the switch's own concern; G289 (2) moves to G2 |
| N8 | §1: the MIR builder's 19 `Unsupported_Kd` sites with verdicts; two G128-cause limits join OI-2 |
| R2-1 (re-review) | §4, §10: `Statics.Length` 2,435, the calls' records from 2,447; the check (a′) would drop is named and kept: the pin test gets a row with a Binding error beside the prelude |
| R2-2 | §1, §10: a CLI probe replaces "(H) the old build refuses": default `check` accepts, the old `emit` refuses with unlocated `GenerationFailed_Kd`; sel rejects at check |
| R2-3 | §2, §10: the two limits stay MIR construction records; they widen PD30's (e) row, not a `CapabilityGap` row |
| R2-4 | §6: the G0 entry states OI-1's recommendation (a′), as §10 does |
| R2-5 | §2 S7: G4 handles "the remaining pins" (G2 deletes about 6) |

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| G0-1 | Every S8 prerequisite holds on dev; of the builder's 19 sites, G167's is the one a valid program the default builds reaches, and two G128-cause limits reach valid programs the old build already refuses (CLI probe) | 1 |
| G0-2 | S1 as applied: the "sel branch" now lives in `Analyze`'s pipeline branch and `MirRecords`' generator gate; both go at G2 with PD4's pairing (`KIMI_CODEGEN=mir` valid on either pipeline), which G3 needs | 2 |
| G0-3 | The 110 P42 rows move at **G2**, not G3: their record is the collector's check-time `StaticStorage` row | 3, 4 |
| G0-4 | Recommended for them (OI-1): rows assert their own records exactly; one test pins the prelude's 26 `StaticStorage` records, also beside a Binding error | 4 |
| G0-5 | G1 adds `Compilation.Switch.cs`: the Transitional switch as its own concern, its contract and removal stages in one place | 5 |
| G0-6 | G2b is gone (BU-48): G:R6-gen-switch is G2 + G3 | 2 |
| G0-7 | Cost estimate: about +7–8 ms and +0.4 MB per small fresh check, +0.3–1.3 MB retained, warm 0 B (H); G2 records an A/B against its base | 7 |
| G0-8 | Order: G285 → G167/OI-2 → G1 → G2 (+ G289 (2)) → G3 after G286, G287, G157 (15) → G4; R6-gen holds `Compilation.Pipeline.cs` from G1 to G4 | 8 |
| G0-9 | After G2, `KIMI_PIPELINE` changes no published record; `mir` adds only the ledger's observation and the identity mark until H12 | 9 |

## 1. Prerequisites (S8)

| Prerequisite | State | Evidence |
| --- | --- | --- |
| X3 (R5's exit) | Met | DECISIONS R5-M148 (owner 「推奨どおり承認」, 2026-10-29); ARCH ledger "R5 Done" (`docs/dev/COMPILER_ARCHITECTURE.md:292`) |
| R5-M4 (OQ5) | Met | Owner 2026-10-18; it also accepts the shadow driver in every default `Analyze` from G2, its cost measured in G2 |
| R5-M5 with Q4 | Met | R5-M88 supersedes OD10's "check-only step"; Q2/Q3 at `4eb19789`; `AnalyzeInstance` has only old-Emission callers (G7) |
| R4's MIR lane; A5 step 1 | Met | K7 and C4a landed (R5-M18's ask A-2); OQ6's fallback (d) is void (R5-M5) |
| A-8 (G143) | Met | F13 `16fab6ca7` (R5-M42); PLAN `a33809925` closed G143 |
| A-6 (G103-E9a/E9b; every MIR construction `Unsupported_Kd` in a valid fixture source, or its (e) record) | Met as worded | below; no fixture source reaches a builder record |

A-6 evidence. X1's sel Session (`17cd554ab`): 6,089 fixtures' native outcomes equal (6 decided deviations), 40/40 milestones, five
CLI harnesses; MIR census rows only in incomplete programs. Batch 56's sel Session (`228d6f0b4`): failures = the 110 pending rows.
The ledger at `228d6f0b4` (3,431 rows, `Analyze` compilations only): one construction record in complete programs, G167 (3 + 3 `B`
rows, source `92bc1b2de957`); no `InternalInvariant_Kd` row. PLAN G103's open items (R1, constructs under an unresolved operation,
`MirBuilder.Rejected`, R4-M109's L1/L2, Bare's `(Unset, None)`) all need a Binding error; (ii) `$tryWrite` is latent with right MIR;
the `Arity`/`Component` duplication is structure. None is reachable from a valid fixture, milestone or library program.

The builder's `Unsupported_Kd` sites (`C/Mir/Build/`; `derived` and `Rejected(value)` mean no record, `MirBuilder.Opaque.cs:29-37`):

| Site | Form | Valid program? | Shown by |
| --- | --- | --- | --- |
| `Selections.cs:87` | a position of a Type parameter (G167) | **yes**; the default builds it today | ledger `92bc1b2de957`; `MirBuildTest.Places.cs:154` |
| `Calls.Comparisons.cs:54` | `TyCtxt.Comparison` Limit: a tuple operator's leaf past Binding's storage bound (PD30) | **yes**, past the G128 bound; default `check` accepts, the old `emit` refuses (probe below) | `MirBuildTest.Calls.cs:272` (BuildMir, not in the ledger) |
| `Defaults.cs:124` | a default its call does not instantiate, a Limit only (R4-M127) | **yes**, past the G128 bound; default `check` accepts, the old `emit` refuses (probe below) | `MirBuildTest.Defaults.cs:186`; `InstanceCollectorTest.cs:1936` |
| `Calls.cs:54` | a plan whose operations do not cover the parameters | after a Binding error (H) | no ledger row |
| `Operators.cs:58`, `:91` | a Primitive or metadata form without a row | no published form reaches them (H) | no ledger row |
| `Places.cs:49`, `:104`, `:117`, `:181` | a Place kind, Res, receiver-less storage or ownerless Field outside the rows | after a Binding error (`Rejected`) (H) | `MirBuildTest.Values.cs:26` rows |
| `Values.cs:147`, `:216`, `:286`, `:310` | an unbuilt Op, aggregate, unbound capture, setter-less assignment | after a Binding error (H); `:216` also an `Array`-typed fill literal if Binding types one (R4-M81 keeps its record; none observed, H) | `MirBuildTest.Values.cs:101`, `:394` |
| `Values.cs:194` | an unset acquisition (derived); an adaptation not built | derived after an Error proof; else none observed (H) | no ledger row |
| `Acquisitions.cs:100`, `Places.cs:149`, `Transfers.cs:64`, `Values.cs:360` | broken contracts or a rejected static: `derived`, no `Unsupported_Kd` | no | 0 `InternalInvariant_Kd` ledger rows |

R4's state (PLAN §3; `artifacts/handoff/2026-10-28-r4-pause/`): 2c-5 + G270 landed (R4-M152); order WK → G272 → G216 (a) (revision 5)
→ G161 → G264 → G273. What G1–G3 touch of it: no R4 unit names `Compilation.Pipeline.cs`, `Compilation.Generation.cs` or the
Backend (H). From G2 every default `Analyze` runs R4's driver (`C/Mir/Analysis/MirAnalysis.cs:55-81`), so R4's merges change default
`check` and LSP cost and an analysis exception faults default `check`. From G3 gate step 2 reads A2's `MirDropTable.Incomplete` on
default builds (R5-M96; ask A-10). G272, WK and G264 are A5 problems (unpublished until H5). G273 meets G7's `Binding.ClosureCall`.

## 2. S1–S7 as applied

| Clause | Verdict | As applied at `07a5ca2da` | Reason, cost |
| --- | --- | --- | --- |
| S1 driver and collector in every `Analyze`; "the sel branch in `BuildMir` goes" | Holds, refined | `BuildMir` (`C/Core/Compilation.Pipeline.cs:204-209`) has had no branch since BU-48. The branch is `Analyze`'s (`:136-144`) and `MirRecords`' gate (`:247-248`). G2: `Analyze` always sets `this.mir = this.BuildMir()`; `MirRecords` counts every record; PD4's pairing goes (`:81`, `:96-99`, `:188-193`; R5-M35), as C-package §3.8 planned ("the default pipeline with `KIMI_CODEGEN=mir` from G2"). `CompilerIdentity` (`:63-68`) then marks each non-default selection, pipeline first: `[mir]`, `[codegen mir]` (the legacy pipeline with MIR code generation), `[mir, codegen mir]`; the test manifest (`src/Kimi/Testing/TestCatalog.cs:114-122`) already does; G3 applies the rule with `codegen legacy` as the non-default | Core about +10/−30 (H); no edit of `MirAnalysis.cs` (PD6) |
| S2 publication at check, entries, tests, baselines, STATUS | Holds, refined | Entries §6. Reading of "the 20 instance-failure methods assert check-time records" (S2, plan §5.4 item 2): the methods whose program the collector rejects at check; the old generator's own-limit methods are pinned to it and go by G4 under R5-M85, a later decision, as PLAN's R5 bullet notes ("G2 rewrites fewer instance-failure methods than its row's 20"). Documents: STATUS `:40` and `:89` ("from G2 on every pipeline"), `:86` (two "until G2"), `:88` (the default's accepted generic-group `let`, X33 (a)); DIAGNOSTICS `:59`, `:340` ("a collector limit under MIR code generation"), `:354`; PLAN G128's "which Binding and the old path accept" → check accepts, the old build refuses unlocated (K0) | P42 rows and G167: owner items |
| S3 PD12's gate wherever MIR codegen runs | Holds | Step 1 `C/Core/Compilation.Generation.cs:92-95`; step 2 in `FunctionCodegen.Write` (R5-M96; arena skip until H3). R5-M96's "G0 extends it": from G2 under `KIMI_CODEGEN=mir` on either pipeline, from G3 by default | None |
| S4 measurement | Holds, refined | K4b's files (`r5-k4b-analyze-cost-*`) are absent; `--analyze-cost` exists (`src/Benchmark/Benchmarks/AnalyzeCostMeasurements.cs`). G2 measures an A/B against its base (§7) | One quiet slot |
| S5 G1 split; G2, G2b, G3 merged together after G:R6-gen-switch | Holds, refined | BU-48 deleted Binding's eager C-layout check (R5-M148), so the group is G2 + G3. G1 adds one file (§5) | −1 unit |
| S6 rollback | Holds | §9 | None |
| S7 deletion order | Holds, refined | G4 → G5, G6 → G7 → G8 → G9a → G9b → G10 (R5-M18). G4 also deletes the remaining G3 deviations of `tests/xUnitTest/Codegen.cs`, the empty pending ledger, `verify.ps1 -Codegen`, and the remaining `CompilerCodegen.Legacy` pins (110 sites in 77 test files at the base, of which G2 deletes about 6; K13a–K13c's, G157 (21)'s, R5-M73's depth test): each deleted or re-pointed to `Compilation.Emission` until G7 (R5-M85: "goes by G4 at the latest", since G4 deletes the `Codegen` setter) | None |
| S8 prerequisites | Holds | §1 | — |

Departures from owner-approved text (§10): **D1** the P42 rows and Milestone 42's baseline change at G2, against R5-M144's "(not
G2's)" and R5-M145's "leave with P42" (OI-1). **D2** valid forms become `Unsupported_Kd` at default `check` from G2 without being on
PD27's list: G167 (OI-2). The builder's two G128-cause limits stay MIR construction records and widen the PD30 (e) row; the old build
already refuses them (probe, §10), so OI-2 asks the owner only to confirm PD30 covers them. The rest is refinement within S1–S8.

## 3. G2's records census

| Records newly at default `check` (A5's verdicts excluded) | Today on default | From G2 | Evidence |
| --- | --- | --- | --- |
| MIR construction records | none (R3-1) | G167; the two G128-cause limits (§1) | ledger; MirBuildTest |
| Validator findings | none | none observed | ledger: 0 rows |
| Collector Unsupported (`C/Backend/Capability.cs:28-53`: `StaticStorage`, `StorageNesting`, `ExclusiveInheritedWitness`, `BodylessWitness`, `EnclosingInstance`, `WideDivision`, `WideFloatConversion`), Resource (`GenerationResourceLimit_Kd`) and Internal | not published (`:248`) | published; Language records already are (BU-48) | sel = G2's publication; X1; batch 56 |
| G285's derived `InternalInvariant_Kd` | sel only | none: G285 (R5-M151, in implementation) drops it while `TyCtxt.BindingComplete` is false | its own census |

What moves at G2 (grep counts at the base):

| Item | At G2 |
| --- | --- |
| 110 pending rows (`tests/xUnitTest/TestData/codegen-pending/C-frame.tsv`): `PathCapabilityDiagnosticTest.AStaticBorrowIsUnsupported` 98, `.AStaticCalleeKeepsTheCallsRecords` 10, `EffectBoundReportTest.ReportsTheViolatingEffect` 1, `MilestoneSourcesTest.AuthoredProgramsMatchTheirRecordedStageAndDiagnostic` 1 | per OI-1 (§4); `StaticStorage` is reported at check (`InstanceCollector.Capabilities.cs:65`; R5-M145) |
| `tests/milestones/stage-baselines.json:4` (Milestone 42) | its first diagnostic (`Unsupported_Kd` at `Registry`) becomes the first `StaticStorage` record's; G2 records it (X1's sel run differs only at 42, R5-M148 item 9) |
| `CodegenDeviations.CheckedSetAtCheck` (`tests/xUnitTest/Codegen.cs:111-116`): 10 `Codegen.Expect` sites in 7 files (`BuiltinArithmeticContractTest:115`, `GenericGenerationDiagnosticTest:34, :118`, `InlineLayoutTest:84`, `MinimalEmissionTest:348`, `PipelineSwitchTest:220`, `PrimitiveIntegerTest:77`, `VirtualGenericDispatchTest:67, :80, :85`) | reduced to the target; the entry deleted (R5-M85) |
| Legacy halves after a check that now rejects ("until G2"): `MinimalEmissionTest:343, :356-359`, `GenericGenerationDiagnosticTest:60-79`, `VirtualGenericDispatchTest:89-95`, `PrimitiveIntegerTest:81`, `PipelineSwitchTest:241-246`, `BuiltinArithmeticContractTest` after `:115` (H) | deleted |
| Instance-failure methods whose program the collector rejects: `AssertGenerationResourceLimit`'s 7 callers (`GrowingDestructorContexts…`, `GrowingSubstitutionKeys…`, `NestingBeyondTheDepthBound…`, `FiniteLayoutsBeyond…`, `EnumTagAndAlignment…`, `FactoriesKeepDestructorExpansionBounded`, `GrowingDefaultSubstitutions…`), `InstanceFailuresAreLocatedOnly`, `IndependentUnsupportedOperationsRetainSeparateLocations`, `GeneratePublishesAnUnsupportedOperationInsteadOfAnInternalFailure` (H), `GrowingDescriptorKeysAreResourceDiagnosed` | assert check-time records only |
| Own-limit methods pinned to the old generator: `OversizedSubstitutionSetsAreResourceDiagnosed` (`GenericStorageEmissionTest:295`), `DestructorSubstitutionsRespectTheGenerationLimit` (`GenericDestructorEmissionTest:191`), `ConcreteInstanceLimitsRemainResourceFailuresAndRecover` (`OwnershipStorageLimitTest:18`), `TypeAndLengthSlotsAppearOnceInDeclarationOrder` and `InstanceStorageLimitsKeepResourceFactsAndRecover` (`GenericGenerationDiagnosticTest:95, :129`) | unchanged: their programs pass check (old limits lowered in-test, H); pins kept until G4, which deletes them or re-points kept ones to `Compilation.Emission` (R5-M85) |
| Unpinned: `FiniteGenericRecursionReusesItsInstance` (`TryPrepare`), `SuccessfulInstancesKeepTheExistingAllocationFreePath`, `WarmSuccessfulInstancePreparationAllocatesNothing` (`FailedInstance`/`FailureInstance` reads), `VerifiedTemplateBuildsDependentConcreteReads` (`AnalyzeInstance`) | unchanged; G5 and G7 delete their reads (G3 makes the warm `WriteIr` MIR's) |
| `PipelineSwitchTest` | `TheEnvironmentSelectsTheCodegenUnderMirOnly` (pairing), `OnlyMirMarksTheIdentity` (`:105`, the rule of §2), `OnlyMirPublishesTheMirRecords` (`:310-337`), `MirCodegenPublishesTheCollectorRecords`, `TheTestCommandPublishesTheGenerationRecords`, `AnalyzeNotifiesTheObserverOnce` (`:385`, legacy's 0 errors) |
| G157 (2); `ArchitectureRulesTest.cs:61-62` | K9b's library-body record gets its test; ratchets 11 and 13 fall to their new counts |
| Diagnostic snapshot; milestones, fixtures, CLI, library | Milestone 42's five `StaticStorage` rows (H, X1's sel snapshot); otherwise none (H, X1 sel) |

Not covered by any run: the 110 pins. Under sel a pinned compilation hides non-Language collector records (`:248`); from G2 it hides
nothing. I expect no further movement (H). The exact census is G2's first default Session (with `-DiagnosticSnapshot` and the
milestones) on its prototype; **not run: it needs approval** (a heavy run).

## 4. The 110 P42 rows

Their record is the `StaticStorage` capability row at the static's declaration, reported at check (R5-M82) and published today only
under MIR code generation. G2 publishes it on every pipeline, so the rows fail in G2's default Session. Counted from batch 56's sel
failure messages: every one of the 108 `PathCapabilityDiagnosticTest` rows gains exactly the shared `Statics` prelude's 26 records
(positions 711–2158, all inside the prelude, `PathCapabilityDiagnosticTest.cs:74`; `Statics.Length` = 2,435), beside its own 1 (98
rows) or 2 (10 rows) records after it; `EffectBoundReportTest`'s row gains 1 (its `public var puts`); Milestone 42 has 5.

| | (a) exact sets gain the records | (a′) rows assert after `Statics.Length`; one pin | (b) Binding's records only |
| --- | --- | --- | --- |
| G2 change | 108 rows × 26 added records (one shared expected list in 2 methods), EffectBound +1, Milestone 42's baseline | 2 methods assert the records located after `Statics.Length` exactly (98 rows 1, 10 rows 2); one new test pins the prelude's 26 `StaticStorage` records once, with a second row whose program adds a Binding error after the prelude and asserts the 26 still appear; EffectBound +1; Milestone 42's baseline | 2 methods filter to Binding's partition; EffectBound and Milestone 42 as (a) |
| G3, G4 | nothing at G3 (check rejects); G4 drops the empty ledger | same | same |
| P42 reverts | the shared list in 2 methods (2,808 asserted records), EffectBound's record, the baseline | the one pin test, EffectBound's record, the baseline | nothing; the filter stays |
| Checks kept | everything, the `StaticStorage` rule asserted 108 times (duplicate cases) | each rule once at its layer; `AStaticCalleeKeepsTheCallsRecords`' SPEC 23.1.2 check (the callee's `Unsupported_Kd` beside the call's own record) lies wholly after `Statics.Length` (its records from position 2,447), so it is kept. Without the pin's second row it would drop the check that the prelude's `StaticStorage` records appear beside the rows' Binding errors (SPEC 23.1.2), which only EffectBound's +1 row would still assert, on the path G285 changes (records while `BindingComplete` is false); the second row keeps it | loses records of other partitions at the body, so the independence check where the call's record is not Binding's (H): a weakening |

Rejected outright: publishing collector records except `StaticStorage` (a per-row gate); G2 waiting for P42 (P42 follows H5, which
follows G11: circular); P42 before G2 (a full-process package, untestable natively while the old analysis rejects static `var`,
against R5-M13 (b)); a `Codegen.Expect` deviation (rejected by R5-M144 (b), keyed by the generator). **Recommendation (a′)**: it
asserts each rule once (AGENTS: no duplicate cases), keeps both independence checks (the calls' records, and the prelude's records
beside a Binding error, in the pin test's second row), and P42 reverts one test, not 108 rows.

## 5. G1: split `Compilation.Pipeline.cs` (behavior-preserving)

Today 291 lines, about 1,230 non-trivia tokens. Its three responsibilities:

| Lines | Responsibility | Removal |
| --- | --- | --- |
| 22-37, 60-107, 169-193, 260-280 (~125) | the pipeline switch: `DefaultPipeline`, `DefaultCodegen`, `CompilerIdentity`, `Pipeline`, `Codegen`, `PipelineOf`, `CodegenOf`, the two enums (Transitional) | G2 the pairing; G4 the generator half; H12 the rest |
| 33, 44-58, 113-167, 195-199, 282-291 (~95) | the analysis entry: `Analyzed`, `Analysis`, `Problems`, `Analyze`, `ReportAnalysis`, `SyntaxOf`, `AnalysisResult`, `Problem` | H5 decides by MIR |
| 38-42, 109-111, 201-257 (~70) | the MIR program, checked set and records: `BuildMir`, `Collect`, `MirRecords`, `Instances` | H3 folds the records into `MirReport` |

Target: **`C/Core/Compilation.Switch.cs`** takes the switch. Reason: the Transitional switch is its own concern (AGENTS
`Owner.Concern.cs`), so its contract (responsibility, inputs `KIMI_PIPELINE`/`KIMI_CODEGEN`, the read-once invariant of PLAN G199,
prohibitions) and its removal stages (G2, G4, H12) sit in one place. `Compilation.Pipeline.cs` keeps the analysis entry with its
records, which `Analysis`, `Problems` and `ReportAnalysis` read and H3 replaces (a third file rejected). Verification:
`PipelineSwitchTest` and bp. G289 (2), the comment at `src/Benchmark/Benchmarks/CompilerPipelineBenchmark.cs:79-83`, goes into G2,
which rewrites that comment anyway ("a legacy Analyze builds and collects without the MIR analyses" stops being true).

Hold: R6-gen records in PLAN §3, at G1's start, `Core/Compilation.Pipeline.cs` (R5-M7's list) and `Compilation.Switch.cs`, kept
through G2, G3 and G4 and released in G4's merge. `Compilation.Generation.cs` and `Backend/**` are R6-gen's. R4 has no planned
change there (H).

## 6. DECISIONS text

**G0's entry (draft R5-M152; G285 takes R5-M151), landed by the planning session with this design:**

> **R5-M152 The R6-gen switch contract as applied (G0)** (2026-10-29, planning session; plan G0, K §6 S1–S8 under R5-M18; R5-M4,
> R5-M5, R5-M88, R5-M148) S1–S8 hold on dev `07a5ca2da` with these applications:
> - S1: from G2 `Compilation.Analyze` builds, validates, analyzes (the whole `MirAnalysis.Run`, unpublished) and collects on every
>   pipeline; the branch S1 names lives in `Analyze` and `MirRecords`' generator gate (BU-48 moved it out of `BuildMir`); PD4's
>   pairing (R5-M35) ends at G2, and `CompilerIdentity` marks each non-default selection (`[mir]`, `[codegen mir]`, `[mir, codegen mir]`).
> - S2: G2 publishes construction records, validator findings and collector records at check on every pipeline. "The 20
>   instance-failure methods" reads as those whose program the collector rejects; the old generator's own-limit methods keep their
>   pins until G4, which deletes them or re-points kept ones to `Compilation.Emission` (R5-M85). The P42 rows [OI-1; recommended (a′)]: each row asserts its records after `Statics.Length`; one test pins the
>   prelude's 26 `StaticStorage` records, also beside a Binding error; EffectBound's row gains 1; Milestone 42 re-baselined. [OI-2's
>   answer: G167; the two G128-cause construction records under the PD30 (e) row.]
> - S3 as written, wherever MIR code generation runs (R5-M96). S4: an A/B of default `check` and warm Analyze against G2's base, K4b's
>   files being absent. S5: G2b was done by BU-48 (R5-M148); G:R6-gen-switch is G2 and G3; G1 adds `Compilation.Switch.cs`, the switch
>   as its own concern. S6: no environment rollback for G2; `KIMI_CODEGEN=legacy` until G4. S7 with G9a/G9b (R5-M18) and the pins at G4.
> - Prerequisites (S8): X3 (R5-M148), R5-M4, R5-M5 with Q4 (R5-M88), A-6 (X1, the ledger at `228d6f0b4`, the builder's sites), A-8
>   (R5-M42).
> - Order: G285 (R5-M151) before G2; G286, G287 and G157 (15) before G3; H1a after G2; R6-gen holds `Compilation.Pipeline.cs` G1–G4.
> - Rejected: a per-row exception for `StaticStorage`; G2 waiting for P42 (circular); a third G1 file for the records.

ARCH, same landing (ARCH:279-280 says G0 records them): "R6-gen progress: the plan's §5.1 M4 (Emission lines), M7 (deviations), M8
(ratchets) and the pending ledger (0 from G2), and §5.2's two-session and switch-revert rules."

**G2's entry (number at G2), superseding by name:**
- **R3-1**: MIR records publish on every pipeline from G2, not only under `mir`.
- **R4-M1**, for construction records, validator findings and collector records: they join acceptance at check on every pipeline;
  MIR analysis problems stay unpublished until H3 and decide from H5. Reason: the owner decided this publication in R5-M4 (OQ5).
- **OD10's split** (R4-M35): R6-gen moves these MIR-owned records into acceptance; MIR problems move at R6-check (H5).
- R5-M35's pairing; R5-M64's `verify.ps1` rule (`-Codegen mir` needs `-Pipeline mir`); R5-M41's "until G2"; R5-M85's
  `checked-set-at-check`; under OI-1 (a′), R5-M144's "(not G2's)" and R5-M145's "leave with P42" for the 108 rows and EffectBound's
  row, which leave the pending ledger at G2 (only the pin test, EffectBound's record and Milestone 42's baseline leave with P42).

## 7. Cost (S4)

| Source (none is G2's configuration or a quiet `--analyze-cost`) | What it shows |
| --- | --- |
| BU-48 (pinned, tiering off) | Default cold `Analyze` (build + collect): Hello 2.93 MB, about 4.4 ms; Milestone26 3.24 MB, 5.3 ms; warm 0 B |
| R5-M149 (quiet slot, 5 rounds) | Default cold `Analyze` 4,484 µs (Hello), 4,841 µs (Milestone26) |
| X1 quiet `--compiler-pipeline`, legacy pipeline (`17cd554ab/quiet/20261028-232516-x1-p3-17cd554ab/pipeline-A-*.json`) | Stage 3 (`BuildMir`: publish, build, validate, whole driver, collect): 12.0 ms / 3.43 MB (Scalar 1, 218 MIR bodies), 12.3 / 3.60 (Generic 1), 19.9 / 5.29, 26.1 / 7.37, 36.8 / 11.38 (Generic 32/64/128) |
| `--analyze-cost`, G158 prototype (`artifacts/benchmarks/r5-g158-proto/analyze-cost-candidate.json`; shared machine, n = 1) | mir − legacy (the driver): fresh +0.44, +0.93, +1.43, +2.13 MB; retained +0.32, +0.57, +0.86, +1.27 MB; warm bytes equal; times too noisy to use |
| R5-M148 `--collection-cost`; `--diagnostics` under mir | Collection 0.12–25.3 ms; no saved `--diagnostics` output |

Estimate (H): a small program's fresh default `check` gains about 12.0 − 4.4 ≈ 7–8 ms and +0.4–0.5 MB (mostly the bundled library's
~210 bodies); Generic 128 +2.1 MB; retained +0.3–1.3 MB. A warm LSP Analyze (rebind, new version) reruns build, driver and collection
at 0 B extra, about the driver's time more. G2 records (`artifacts/benchmarks/r6-g2-cost/`, figures in its entry):
1. `--analyze-cost`, G2's head against its merge base, one folder, quiet slot, `KIMI_CPU=2`, `DOTNET_TieredCompilation=0`, ≥ 5
   interleaved rounds: per Generic 1/32/64/128 fresh check ms and bytes, warm Analyze ms and bytes, retained bytes and growth.
2. BU-48's pinned cold and warm `Analyze` A/B on Hello and Milestone26 (cold bytes exact, warm 0 B).
3. The `Purpose=Allocation` tests on the default pipeline at 0 B (S4).

## 8. Order and holds

```
G285 (R5-M151, in implementation) ─┐
G167 / OI-2 answered ──────────────┼─► G1 (Compilation.Switch.cs) ─► G2 (records at check; OI-1; G157 (2); G289 (2)) ─┐
OI-1 answered ─────────────────────┘                                                                                ├─► G:R6-gen-switch ─► merge ─► G4 ─► …
G286, G287, G157 (15) unit (beside G1/G2) ───────────────────────────────► G3 (default codegen = MIR) ───────────────┘
H1a (Q8) after G2. G157 (48): off this path.
```

| Item | Place | Note |
| --- | --- | --- |
| G285 | Lands before G2 | R5-M151: the collector drops `InternalInvariant_Kd` while `TyCtxt.BindingComplete` is false (BU-38's Guard generalized, `Derived` deleted) |
| G167 | Before G2, or an (e) row (OI-2) | Binding resolves a generic key through `PositionSyntax.resolved` (PLAN G167); W1-type hold; its tests move to another unbuilt form |
| G286, G287 | Before G3 | Native region fixture at each writer kind's widest value; M5's three classes counted |
| G157 (15) | One unit before G3, beside G1/G2 (Backend, R6-gen's files; R) | (1) the Library signature row's native compile (FrameMain holds only `main`); (2) a closure in a refuted override is not Generated in a Library: `InstanceCollector.Generates` (`InstanceCollector.Objects.cs:34-51`) takes a closure root as slot-less (H). Not in G3: a switch unit carries no behavior fix |
| G289 (2) | G2 | The comment G2 rewrites |
| G157 (2) | G2 | Its test |
| G157 (48) | Off the switch path | The builder's read of `TyCtxt.SignatureSite` (R4, W1 hold); `Binding.Attributes.cs`' one reset (BU) |
| BU's files | None in G1–G3 | G2b is gone; G7 and G9a delete Binding members (BU reviews that part) |
| Driver (PD6); `MirDifferenceLedger.cs` (R4) | No edit | G2 calls `MirAnalysis.Run` through `BuildMir` (`:206`); the ledger still observes mir compilations only (`:118`) |

## 9. Rollback (S6) and `KIMI_PIPELINE` after G2

- Before the group merges, a regression in G2 or G3 is reverted on its branch; after, the group is reverted on dev (plan §5.2). G4
  lands only after G3's full sweep on dev (`'*.ll'` O0/O2, all milestones, CLI harnesses, snapshot, O2 corpus).
- G2 has no environment rollback: an env value hiding the records would be a second mode. `KIMI_CODEGEN=legacy` selects the old
  generator on either pipeline until G4; it is G3's rollback.
- `KIMI_PIPELINE` after G2: `legacy` (the default) = the old analysis decides; the MIR is built, validated, analyzed (unpublished) and
  collected; construction records, validator findings and collector records publish at check. `mir` publishes the same and differs
  only in the ledger's observation (`MirDifferenceLedger.cs:118`), the `[mir]` mark and the test manifest entry. VERIFICATION and
  `CompilerPipeline.md` say so; H12 removes the switch.

## 10. Owner questions and risks

**OI-1 (D1). The 110 P42 rows at G2** (§4). (a′) Recommended: each row's exact set covers its records after `Statics.Length`, one
test pins the prelude's 26 `StaticStorage` records, with a row whose program has a Binding error beside the prelude, asserting the 26
still appear (SPEC 23.1.2; G285 changes that path); EffectBound's row gains 1; Milestone 42 is re-baselined; P42 reverts the one test.
(a) Every row's exact set gains the 26 records; P42 reverts 2,808 asserted records through one shared list. (b) Binding's records only
(a weakening). Either (a) or (a′) supersedes R5-M144's "(not G2's)" and R5-M145's "leave with P42" for these rows.

**OI-2 (D2). Valid forms rejected at default `check` from G2.** G167 (`values[p]`, `p: I`), which the default builds today; and the
builder's two G128-cause limits (`Calls.Comparisons.cs:54`, `Defaults.cs:124`). (a) Recommended: G167 lands before G2 as an R6-gen
unit (Binding with BU, a W1-type hold); the owner confirms that PD30's "From G2 such a program is an (e) row in STATUS and PLAN until
R7" covers the two limits, which stay MIR construction records (not a `CapabilityGap` row of R5-M146's table): the probe below shows
the old build already refuses both, so G2 makes no program unbuildable that builds today. (b) G167 as a PD27 (e) row too, G167
later. (c) Neither: not allowed.

Probe (built CLI of `bin/worktrees/r6-g286`, `src` = `07a5ca2da`, copied to the G0 session's scratchpad with the sources in `g0probe/`; `check` and `emit`, the generation step
of `build`; native `build` not run, no toolchain beside the copy):

| Program | default `check` | default `emit` (old generator) | sel `check` |
| --- | --- | --- | --- |
| `a == b` over `(Wrap^80<i32>, i32)`, uncalled / called from `main` | accepts | `GenerationFailed_Kd` at the project, unlocated: "Generic entry parameter requires a concrete storage representation." | `Unsupported_Kd` at `a == b`; called, also at the tuple's `Wrap^80.init()` |
| `f(b)` omitting `k: Option<T> = .None`, `b: ref/Bx^65<i32>`, uncalled / called | accepts | the same `GenerationFailed_Kd` | `GenerationResourceLimit_Kd` + `Unsupported_Kd` at `f(b)`; called, also at the local and its construction |
| twin `Bx^30` (control) | accepts | writes the module | accepts; writes the module |

**OI-3. Verification lanes.** From G2 the default Session publishes what `-Pipeline mir` does, from G3 what sel does. (a)
Recommended: from G2 the mir lane runs only as the ledger's run, from G3 the sel lane stops (G4 removes its rows). (b) Keep all to G4.

| Risk | Response |
| --- | --- |
| An exception in R4's driver faults default `check` from G2 | R4's merges run the mir Session over every test; G2's Session runs the driver by default |
| LSP latency: the driver reruns over every library body per rebind (≈ 7 ms small; H) | G2's A/B; if too high, the driver at generation only until H5 is a departure from R5-M4 for the owner |
| Census gap: the 110 pins | G2's first default Session is the census (approval needed) |
| OI-1 or OI-2 unanswered blocks G2 | Ask now, non-blocking; G1, G285, G286, G287, G157 (15) proceed |
| X1 evidence is at `17cd554ab` | Batch 56's sel Session at `228d6f0b4` confirms the 110 rows; later units added only Language records |
| The group branch lives across G2 and G3 beside R4 | Hold from G1; G3's sweep in a quiet slot |
