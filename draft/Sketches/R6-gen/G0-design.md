# R6-gen G0: the switch design (K-package §6 applied to dev)

Status: G0's one design (plan §4.9 row G0, Doc, D+R under S2); an independent review follows. Working record, not authority:
what it decides becomes authority only as the DECISIONS entry of §6, and its owner items only by the owner's answers.

Base: dev `07a5ca2da` (code, DECISIONS, PLAN, STATUS). "Plan" is the [R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md);
"K" is `draft/Sketches/R5 Step 0/K-package.md`; S1–S8 are K §6 (owner-approved, R5-M18). Paths: `C/` = `src/Kimi/Compiler/`,
`T/` = `tests/xUnitTest/Tests/`. "sel" = `KIMI_PIPELINE=mir` + `KIMI_CODEGEN=mir`. Evidence read, not rerun: X1
(`artifacts/verify/reduction-R5/17cd554ab/`), the shared base `artifacts/verify/shared-base/228d6f0b4/` (default Session, mir-diff
ledger) and batch 56's Sessions (`artifacts/verify/r5-units/session/batch56-228d6f0b4/`). No build or test was run. (H) marks an
estimate or an inference not checked by running code.

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| G0-1 | Every S8 prerequisite holds on dev; G167 is the one valid form whose MIR construction record G2 would newly publish | 1 |
| G0-2 | S1 as applied: the "sel branch" now lives in `Analyze`'s pipeline branch and `MirRecords`' code generator gate; both go at G2, and so does PD4's pairing (`KIMI_CODEGEN=mir` valid on either pipeline), which G3 needs | 2 |
| G0-3 | The 110 P42 rows move at **G2**, not G3: their record is the collector's check-time `StaticStorage` row | 3, 4 |
| G0-4 | Recommended for them: G2 adds the declaration's `StaticStorage` record to each row's exact expected set (owner item OI-1) | 4 |
| G0-5 | G1 adds one file, `Compilation.Switch.cs` (the Transitional selection); the analysis entry and its MIR records stay together in `Compilation.Pipeline.cs` | 5 |
| G0-6 | G2b is gone (BU-48 deleted Binding's eager C-layout check): G:R6-gen-switch is G2 + G3 | 2 |
| G0-7 | G2's cost estimate: about +7–8 ms and +0.4 MB per small fresh check, +0.3–1.3 MB retained, warm 0 B (H); G2 records an A/B against its base | 7 |
| G0-8 | Order: G285 → (G167, OI-2) → G1 → G2 ∥ (G286, G287) → G3 → G4; R6-gen holds `Compilation.Pipeline.cs` from G1 to G4 | 8 |
| G0-9 | After G2, `KIMI_PIPELINE` changes no published record: `legacy` = old acceptance with MIR records; `mir` adds only the ledger's observation and the identity mark until H12 | 9 |

## 1. Prerequisites (S8)

| Prerequisite | State | Evidence |
| --- | --- | --- |
| X3 (R5's exit) | Met | DECISIONS R5-M148 (owner 「推奨どおり承認」, 2026-10-29); ARCH stage ledger "R5 Done" (`docs/dev/COMPILER_ARCHITECTURE.md:292`) |
| R5-M4 (OQ5) | Met | Owner 2026-10-18, every sub-item as recommended; it also accepts the shadow driver in every default `Analyze` from G2 with its cost measured in G2 |
| R5-M5 with Q4 | Met | R5-M88 (Q4) supersedes OD10's "check-only step"; Q2/Q3 landed at `4eb19789`; `AnalyzeInstance` has only old-Emission callers (G7) |
| R4's MIR lane merged; A5 step 1 | Met | K7 and C4a landed long ago (R5-M18's ask A-2 through C4a); OQ6's fallback (d) is void (R5-M5) |
| A-8 (G143) | Met | F13 `16fab6ca7` (R5-M42: a jump without a target row is derived from Binding's record); PLAN `a33809925` closed G143 |
| A-6 (G103-E9a/E9b; every MIR construction `Unsupported_Kd` in a valid fixture source, or its (e) record) | Met as worded; one test-only valid form remains (G167) | below |

A-6 evidence:
- Sel publishes construction records and validator findings (the mir pipeline) and every collector record. X1's sel Session at
  `17cd554ab`: all 6,089 fixtures' native outcomes equal (6 decided deviations), 40/40 harnessed milestones, five CLI harnesses
  pass, MIR census over the library 0 rows; census rows exist only in incomplete programs (Milestone 41's two A1 rows, the `opaque`
  golden, spec case `5.7-enclosing-tuple/0`). Batch 56's sel Session at `228d6f0b4`: failures = the 110 pending rows, 0 differ.
- The mir-diff ledger at `228d6f0b4` (3,431 rows): the only construction record in a complete program is `B Unsupported_Kd
  IdentifierNameKoto` (3 + 3 rows, one source `92bc1b2de957`): PLAN G167, `values[p]` with `p: I`, a valid form (SPEC 4.6.1, 4.7.1)
  the old path accepts and builds. It occurs only in test sources (`T/PipelineSwitchTest.cs:22`, `T/MirBuildTest.Places.cs:156`,
  `T/MirBuildTest.Values.cs:30`, `tests/mir/opaque.kimi:4`), so A-6's wording holds; at G2 the default rejects it at check (OI-2).
  No ledger row carries `InternalInvariant_Kd` (no validator finding in any compared compilation).
- PLAN G103's open items, each checked for a valid program: R1 (`_ = if c => 1 else => missing`), a construct under an unresolved
  operation (G121), `MirBuilder.Rejected`'s untyped value, R4-M109's L1 (an Error receiver) and L2 (a folded Error), and (i) Bare's
  `(Unset, None)` (`t.5`, `let x = missing`) all need a Binding error, so they are reachable only from incomplete programs;
  (ii) `$tryWrite`'s Adjust column is latent and its MIR right (no record); the `Arity`/`Component` aggregate-shape duplication is
  structure, not a record. **None is reachable from a valid fixture, milestone or library program.**

R4's state (PLAN §3 R4 bullet; `artifacts/handoff/2026-10-28-r4-pause/`): 2c-5 + G270 landed (R4-M152). R4's order: WK (G276) →
G272's landing → G216 (a) (redesign, proposal revision 5, R4 implements after WK) → G161 → G264 (U1, U0′, U2, U3) → G273. What R4's
open work holds that G1–G3 touch:
- No R4 unit names `Core/Compilation.Pipeline.cs`, `Compilation.Generation.cs` or the Backend (H, from PLAN and the handoff's §4).
- G2 runs R4's whole driver (`C/Mir/Analysis/MirAnalysis.cs:55-81`) in every default `Analyze`: from G2 an R4 merge changes default
  `check` and LSP cost, and an analysis exception faults default `check`, not only mir's (R4-M1's "fails the test").
- From G3, gate step 2 reads A2's `MirDropTable.Incomplete` on default builds (R5-M96): an R4 change to A1/A2 completeness changes
  default build outcomes (ask A-10 continues).
- G272's `OwnershipDepthLimit_Kd`, WK and G264 are A5 problems: unpublished until H5, so outside G2's census.
- G273 (Closure Origin arguments) touches `TyCtxt` variance sites and codegen-visible `ClosePart`/`ClosureCall` (PLAN G273): it
  meets G7's `Binding.ClosureCall` deletion, not G1–G3.

## 2. S1–S7 as applied

| Clause | Verdict | As applied at `07a5ca2da` | Reason, cost |
| --- | --- | --- | --- |
| S1 driver and collector in every `Analyze`; "the sel branch in `BuildMir` goes" | Holds, refined | `BuildMir` (`C/Core/Compilation.Pipeline.cs:204-209`) has no branch since BU-48. The branch is now `Analyze`'s (`:136-144`: legacy builds and collects without the driver, `mir` stays null) and `MirRecords`' gate (`:247-248`: non-Language collector records only under MIR code generation). G2: `Analyze` always sets `this.mir = this.BuildMir()`; `MirRecords` counts every record; PD4's pairing goes (`:81`, `:96-99`, `:188-193`, R5-M35), since G3's default is `KIMI_CODEGEN=mir` under the default `legacy` pipeline (C-package §3.8 already planned "the default pipeline with `KIMI_CODEGEN=mir` from G2") | Core about +10/−30 (H). No edit of `MirAnalysis.cs` (PD6) |
| S2 publication at check, entries, tests, baselines, STATUS | Holds, refined | Entries as §6. Fewer than 20 instance-failure methods change at G2 (§3); the 110 P42 rows and G167 change too (§3, §4); STATUS's "Generic generation" row (`docs/STATUS.md:86`) loses its two "until G2" clauses, "Static members" (`:88`) its "the default pipeline's check accepts an immutable scalar `let` in a generic container's group" (X33 (a), PD27 list); PD30's G128 wording is already STATUS's; DIAGNOSTICS `:59` and `:354` (the plan's :56, :285, :306) | The P42 rows and G167 are new owner items (OI-1, OI-2) |
| S3 PD12's gate wherever MIR codegen runs | Holds | Gate step 1 `C/Core/Compilation.Generation.cs:92-95`, step 2 in `FunctionCodegen.Write` (R5-M96; arena-skip `Unsupported_Kd` until H3, R5-M148). R5-M96's "G0 extends it to the default pipeline": from G2 under `KIMI_CODEGEN=mir` on either pipeline, from G3 by default | None |
| S4 measurement | Holds, refined | K4b's files (`r5-k4b-analyze-cost-*`, named by R5-M41's entry) are not in `artifacts/benchmarks/`; `--analyze-cost` exists (`src/Benchmark/Benchmarks/AnalyzeCostMeasurements.cs`). G2 measures an A/B against its merge base (§7) | One quiet slot |
| S5 G1 split; G2, G2b, G3 pushed separately, merged together after G:R6-gen-switch | Holds, refined | G2b is done: BU-48 deleted `CheckCLayoutInstance`, `cLayoutInstances` and the replay queue (none left in `src/`), and R5-M148 says so. The group is G2 + G3. G1 adds one file (§5) | −1 unit |
| S6 rollback | Holds | §9. G2 has no environment rollback: `KIMI_PIPELINE=legacy` no longer hides the records | None |
| S7 deletion order | Holds, refined | G4 → G5, G6 → G7 → G8 → G9a → G9b → G10 (R5-M18 split G9). G4 also deletes `tests/xUnitTest/Codegen.cs`'s remaining G3 deviations, the empty pending ledger and `verify.ps1 -Codegen`; each unit re-greps its counts | None |
| S8 prerequisites | Holds | §1 | — |

Departures from owner-approved text (owner items, §10): **D1** the P42 rows and Milestone 42's baseline change at G2, against R5-M144's
"`MilestoneSourcesTest` ... is a P42 decided row (not G2's)" (OI-1). **D2** G167's valid form becomes `Unsupported_Kd` at default
`check` from G2 without being on PD27's approved list (OI-2). Everything else above is a refinement within S1–S8.

## 3. G2's records census

What default `check` newly publishes at G2 (A5's verdicts excluded; they publish at H5):

| Records | Today on default | From G2 | Evidence |
| --- | --- | --- | --- |
| MIR construction records | none (mir only, R3-1) | G167's position of a Type parameter; nothing else in a complete program | ledger `228d6f0b4`; X1 census |
| Validator findings | none | none observed | ledger: 0 `InternalInvariant_Kd` rows; sel Sessions |
| Collector Language records (`InvalidCLayout_Kd`, `OversizedLayout_Kd`) | published (BU-48) | unchanged | — |
| Collector Unsupported (capability rows: `StaticStorage`, `StorageNesting`, `ExclusiveInheritedWitness`, `BodylessWitness`, `EnclosingInstance`, `WideDivision`, `WideFloatConversion`; `C/Backend/Capability.cs:28-53`), Resource (`GenerationResourceLimit_Kd`) and Internal records | not published (gate `:248`) | published, rejecting at check | sel = G2's publication; X1 sel snapshot; batch 56 |
| G285's derived `InternalInvariant_Kd` beside a Binding error | sel only | none: G285 (R5-M151, accepted, in implementation) drops the collector's `InternalInvariant_Kd` while `TyCtxt.BindingComplete` is false, through one record path | lands before G2; its census is its own |

Tests, rows and baselines that move (static census; method counts are grep counts at the base):

| Item | At G2 | Why |
| --- | --- | --- |
| The 110 pending rows (`tests/xUnitTest/TestData/codegen-pending/C-frame.tsv`: `PathCapabilityDiagnosticTest.AStaticBorrowIsUnsupported` 98, `.AStaticCalleeKeepsTheCallsRecords` 10, `EffectBoundReportTest.ReportsTheViolatingEffect` 1, `MilestoneSourcesTest.AuthoredProgramsMatchTheirRecordedStageAndDiagnostic` 1) | fail unless changed (§4) | `InstanceCollector.Capabilities.cs:65` reports `StaticStorage` at check; R5-M145 "fail only on sel's located `Unsupported_Kd` ... at check" |
| `tests/milestones/stage-baselines.json:4` (Milestone 42) | Its recorded first diagnostic (`Ownership`, `Unsupported_Kd` at `Registry`) changes to the first `StaticStorage` record's; G2 records the new value | `MilestoneSourcesTest.cs:52-58` reads the first published Error after `ReportAnalysis`; X1's sel run of the test differs only at 42 (R5-M148 item 9) |
| `CodegenDeviations.CheckedSetAtCheck` (`tests/xUnitTest/Codegen.cs:111-116`): 10 `Codegen.Expect` sites in 7 files (`BuiltinArithmeticContractTest:115`, `GenericGenerationDiagnosticTest:34, :118`, `InlineLayoutTest:84`, `MinimalEmissionTest:348`, `PipelineSwitchTest:220`, `PrimitiveIntegerTest:77`, `VirtualGenericDispatchTest:67, :80, :85`) | reduced to the target; the catalog entry deleted | R5-M85: "G2 for acceptance-time records" |
| Legacy-generator halves after an accepted check ("until G2", 6 sites: `MinimalEmissionTest.AssertGenerationResourceLimit:356-359`, `GenericGenerationDiagnosticTest:60-79`, `VirtualGenericDispatchTest:89-95`, `PrimitiveIntegerTest:81`, `MinimalEmissionTest:343`, `PipelineSwitchTest.MirCodegenPublishesTheCollectorRecords:241-246`), and `BuiltinArithmeticContractTest`'s pinned refusal after `:115` ("by G4 at the latest", H) | deleted | Their pinned compilation's check now rejects, so the old generator is never reached |
| The instance-failure methods | Change at G2: the helper's 7 callers (`GrowingDestructorContextsAreResourceDiagnosed`, `GrowingSubstitutionKeysAreResourceDiagnosed`, `NestingBeyondTheDepthBoundIsAResourceLimit`, `FiniteLayoutsBeyond...`, `EnumTagAndAlignment...`, `FactoriesKeepDestructorExpansionBounded`, `GrowingDefaultSubstitutionsAreResourceDiagnosed`), `InstanceFailuresAreLocatedOnly`, `IndependentUnsupportedOperationsRetainSeparateLocations`, `GeneratePublishesAnUnsupportedOperationInsteadOfAnInternalFailure` (H) and `GrowingDescriptorKeysAreResourceDiagnosed` (K13c split it from `DescriptorDependenciesReuseFiniteCyclesAndRejectGrowingKeys`, now a fixture test whose comment drops "AndRejectGrowingKeys" once it leaves the pending ledger, which it has). Unchanged until G5/G7: the old generator's own limits and structure (`OversizedSubstitutionSetsAreResourceDiagnosed`, `DestructorSubstitutionsRespectTheGenerationLimit`, `ConcreteInstanceLimitsRemainResourceFailuresAndRecover`, `InstanceStorageLimitsKeepResourceFactsAndRecover`, `FiniteGenericRecursionReusesItsInstance`, `TypeAndLengthSlotsAppearOnceInDeclarationOrder`, `SuccessfulInstances...`, `WarmSuccessfulInstance...`, `VerifiedTemplateBuildsDependentConcreteReads`) | The collector rejects the first set's programs at check; the second set's programs pass check (pinned, old limits) |
| `PipelineSwitchTest` | `TheEnvironmentSelectsTheCodegenUnderMirOnly` (PD4 pairing), `OnlyMirPublishesTheMirRecords` (`:310-337`, G167 on legacy), `MirCodegenPublishesTheCollectorRecords`, `TheTestCommandPublishesTheGenerationRecords`, `AnalyzeNotifiesTheObserverOnce` (`:385`, legacy's 0 errors) rewritten to "every pipeline publishes" | G167 and the gate |
| G157 (2) | K9b's open-operation record at a library body's location gets its test | PLAN G157 (2): "R6-gen G2" |
| `ArchitectureRulesTest.cs:61-62` | "instance analysis" 11 and "generation failure facts" 13 fall to their new counts | the deleted halves |
| Diagnostic snapshot | Milestone 42's five `StaticStorage` rows (H) | X1's sel snapshot differs from legacy only there; proj and G288 added no snapshot row (R5-M149, R5-M150) |
| Milestones, fixtures, CLI harnesses, library | none (H) | X1 sel: 40/40, 6,089 fixtures, five harnesses, library census 0 |

Not covered by any existing run: 110 `CompilerCodegen.Legacy` pin sites in 77 test files. Under sel a pinned compilation still hides
non-Language collector records (gate `:248`), so the sel Session says nothing about their programs; from G2 the pin hides nothing. I
expect only the instance-failure methods above to move (the G157 (21) pins are old-generator representation tests of supported forms,
H). The exact census is one default Session (with `-DiagnosticSnapshot` and the milestones) on G2's prototype build, which is G2's
first Session anyway; **not run, it needs your approval** (a heavy run).

## 4. The 110 P42 rows

They do not wait for G3. Their record is the `StaticStorage` capability row, reported by the collector at check
(`InstanceCollector.Capabilities.cs:65`, R5-M82) and published today only under MIR code generation (`MirRecords`, `:247-248`). G2
publishes it on every pipeline, so the rows fail in G2's default Session, which must be green.

| Stage | Option (a), recommended | Option (b) | Option (c) |
| --- | --- | --- | --- |
| G2 | Each row's exact expected set gains its program's `StaticStorage` record(s) at the static declaration (the shared `Statics` prelude's records once per method, `PathCapabilityDiagnosticTest.cs:74`; rows declaring statics name theirs); Milestone 42's Anchor is re-baselined; each change cites R5-M82, R5-M13 (b) and the PD27 list. The pending ledger falls to 0 rows | The rows observe only Binding's records (filtered by partition) | P42 lands before G2 |
| G3 | Nothing: their programs are rejected at check, so code generation never runs | as (a) | — |
| G4 | The pending tooling and its empty data file go; the rows stay as ordinary tests | as (a) | — |
| P42 (after H5, R5-M13 (b)) | Removes the row; the expected sets lose the record; Milestone 42 restores or passes | as (a) | — |
| Cost | ~2 method edits + 2 rows + 1 baseline; the sets stay exact | Smaller edit, but drops the exactness that pins SPEC 23.1.2 independence (`AStaticCalleeKeepsTheCallsRecords` exists for it): a weakening | A full-process package across Binding, MIR, A4/A5, codegen and runtime (PLAN P42), untestable natively while the old analysis still rejects static `var`; departs from R5-M13 (b) |

Rejected outright: publishing collector records except `StaticStorage` (a per-row gate, a symptom fix); G2 waiting for P42 (P42
follows H5, which follows G11: circular); a `Codegen.Expect` deviation (rejected by R5-M144 (b), and keyed by the code generator,
which G2 does not switch). **Recommendation (a)**: the records are true and independent (SPEC 23.1.2) and on the owner-approved PD27
list; the change adds expectations, but it changes 110 expected sets and supersedes R5-M144's "(not G2's)", so it is OI-1.

## 5. G1: split `Compilation.Pipeline.cs` (behavior-preserving)

Today: 291 lines, about 1,230 non-trivia tokens (well within ARCH's 5,000); changed by K1, K4b, K10a, K10b, C0a, G199, 1c-8,
G158 (3) and x3b1. Three responsibilities:

| Lines | Responsibility | Members | Removal |
| --- | --- | --- | --- |
| 22-37, 60-107, 169-193, 260-280 (~125) | The pipeline switch (Transitional) | `DefaultPipeline`, `DefaultCodegen`, their fields, `CompilerIdentity`, `Pipeline`, `Codegen`, `PipelineOf`, `CodegenOf`, `CompilerPipeline`, `CompilerCodegen` | G2 drops the pairing; G4 the generator half; H12 the rest |
| 33, 44-58, 113-167, 195-199, 282-291 (~95) | The analysis entry and its publication | `Analyzed`, `Analysis`, `Problems`, `Analyze`, `ReportAnalysis`, `SyntaxOf`, `AnalysisResult`, `Problem` | H5 decides by MIR |
| 38-42, 109-111, 201-257 (~70) | The MIR program, checked set and their records | fields, `Instances`, `BuildMir`, `InvalidateOwnership`, `Collect`, `MirRecords` | H3 folds the records into `MirReport` |

Target: **`C/Core/Compilation.Switch.cs`** (new, contract header: responsibility, inputs `KIMI_PIPELINE`/`KIMI_CODEGEN`, outputs, the
read-once invariant of PLAN G199, prohibitions, removal stages) takes the switch; `Compilation.Pipeline.cs` keeps the analysis entry
and the records, its header updated. Rejected: a third file for the records (they are read by `Analysis`, `Problems` and
`ReportAnalysis` in the same file, and H3 replaces them). G1 also carries G289 (2): the comment at
`src/Benchmark/Benchmarks/CompilerPipelineBenchmark.cs:79-83` names the pipeline and generator its stages run under; G2 must rewrite
its "a legacy Analyze builds and collects without the MIR analyses" anyway, so folding G289 (2) into G2 saves one edit (planning
session's choice). Verification: `PipelineSwitchTest` and bp; the Session's whole-solution build covers the Benchmark comment.

Hold line: R6-gen records in PLAN §3, at G1's start, `Core/Compilation.Pipeline.cs` (R5-M7's list) and the new `Compilation.Switch.cs`,
kept through G2, G3 and G4 (each changes them) and released in G4's merge. `Compilation.Generation.cs` and `Backend/**` are R6-gen's.
R4 has no planned change there (H); if one arises, R4 merges dev after the hold's release.

## 6. DECISIONS text

**G0's entry (draft R5-M152; G285 takes R5-M151), landed by the planning session with this design:**

> **R5-M152 The R6-gen switch contract as applied (G0)** (2026-10-29, planning session; plan G0, K §6 S1–S8 under R5-M18; R5-M4,
> R5-M5, R5-M88, R5-M148) S1–S8 hold on dev `07a5ca2da` with these applications:
> - S1: from G2 `Compilation.Analyze` builds, validates, analyzes (the whole `MirAnalysis.Run`, unpublished) and collects on every
>   pipeline; the branch S1 names now lives in `Analyze`'s pipeline branch and `MirRecords`' code generator gate (BU-48 moved it out of
>   `BuildMir`); PD4's pairing (R5-M35) ends at G2, so `KIMI_CODEGEN=mir` is valid on either pipeline (C-package §3.8).
> - S2: G2 publishes construction records, validator findings and collector records at check on every pipeline; the methods whose
>   program the collector rejects lose their legacy values, the old generator's own-limit methods keep their pins until G5/G7.
>   [OI-1's answer: the P42 rows and Milestone 42's baseline.] [OI-2's answer: G167.]
> - S3 as written; R5-M96's refusal applies wherever MIR code generation runs. S4: an A/B of default `check` and warm Analyze against
>   G2's base (`--analyze-cost`, Hello and Milestone26 pinned), K4b's files being absent. S5: G2b was done by BU-48 (R5-M148), so
>   G:R6-gen-switch is G2 and G3; G1 adds `Compilation.Switch.cs`. S6: G2 has no environment rollback; `KIMI_CODEGEN=legacy` until G4.
>   S7 with G9a/G9b (R5-M18).
> - Prerequisites (S8): X3 (R5-M148), R5-M4, R5-M5 with Q4 (R5-M88), A-6 (X1; the ledger at `228d6f0b4`), A-8 (R5-M42).
> - Order: G285 (R5-M151) lands before G2; G286 and G287 before G3; H1a after G2; R6-gen holds `Compilation.Pipeline.cs` from G1
>   through G4.
> - Rejected: a per-row exception for `StaticStorage`; G2 waiting for P42 (circular); a third G1 file for the records.

ARCH (the same landing; ARCH:279-280 says G0 records them): "R6-gen progress: the plan's §5.1 metrics M4 (Emission lines), M7
(deviations), M8 (ratchets) and the pending ledger (0 from G2), and §5.2's two-session and switch-revert rules."

**G2's entry (number at G2), superseding by name:**
- **R3-1**: MIR records publish on every pipeline from G2, not only under `mir`.
- **R4-M1**, for construction records, validator findings and collector records: they join acceptance at check on every pipeline;
  MIR analysis problems stay unpublished until H3 and decide from H5. The reason that no longer holds: R4-M1 kept MIR out of
  acceptance to avoid duplicate records and test churn twice; these records have no old-path twin and their tests flip once.
- **OD10's split** (R4-M35): R6-gen moves these MIR-owned records into acceptance; MIR problems still move at R6-check (H5).
- R5-M35's pairing clause and R5-M64's `verify.ps1` rule (`-Codegen mir` needs `-Pipeline mir`); R5-M41's "until G2"; R5-M85's
  `checked-set-at-check` entry (reduced and deleted); R5-M144's "(not G2's)" if OI-1 (a).

## 7. Cost (S4)

Existing measurements (none is G2's configuration; none is a quiet `--analyze-cost`):

| Source | What it shows |
| --- | --- |
| BU-48 (pinned, tiering off; bytes deterministic) | Default cold `Analyze` after build + collect: Hello 2.93 MB, about 4.4 ms; Milestone26 3.24 MB, 5.3 ms; warm 0 B |
| R5-M149 (quiet slot, 5 interleaved rounds) | Default cold `Analyze` 4,484 µs (Hello), 4,841 µs (Milestone26) at `2576b0537` + proj |
| X1 quiet `--compiler-pipeline`, legacy pipeline, `17cd554ab` (`quiet/20261028-232516-x1-p3-17cd554ab/pipeline-A-*.json`) | Stage 3 (`BuildMir`: publish, build, validate, the whole driver, collect): 12.0 ms / 3.43 MB (Scalar 1, 218 MIR bodies), 12.3 / 3.60 (Generic 1), 19.9 / 5.29 (Generic 32), 26.1 / 7.37 (64), 36.8 / 11.38 (128) |
| `--analyze-cost`, G158 prototype (`artifacts/benchmarks/r5-g158-proto/analyze-cost-candidate.json`; shared machine, n = 1; legacy = build + collect, mir = + driver) | Fresh check bytes mir − legacy: +0.44, +0.93, +1.43, +2.13 MB (Generic 1/32/64/128); retained +0.32, +0.57, +0.86, +1.27 MB; warm Analyze bytes equal (312–1,191 B both); warm ms mir − legacy +14 to +24 ms (noise; unusable for judgment) |
| R5-M148 `--collection-cost` | Collection 0.12–0.36 ms on small inputs, 5.97 / 11.75 / 25.3 ms on Generic 32/64/128 |
| `--diagnostics` under mir | No saved output in `artifacts/benchmarks/` |

Estimate for G2 (H): a small program's fresh default `check` gains the driver, stage 3 minus today's Analyze, about 12.0 − 4.4 ≈ 7–8
ms and about +0.4–0.5 MB, mostly the bundled library's ~210 bodies; Generic 128 about +2.1 MB. Retained +0.3–1.3 MB. A warm LSP
Analyze (rebind, new HIR version) reruns build, driver and collection at 0 B extra; its time grows by about the driver's share,
≈ 7 ms on small programs, more on large ones. The two time sources differ in input and sha; only a quiet A/B settles it.

G2 must record (artifacts under `artifacts/benchmarks/r6-g2-cost/`, figures in G2's entry):
1. `Benchmark --analyze-cost`, G2's head against its merge base, both builds in one folder, quiet slot, `KIMI_CPU=2`,
   `DOTNET_TieredCompilation=0`, ≥ 5 interleaved rounds: per Generic 1/32/64/128 the default configuration's fresh check ms and
   bytes, warm Analyze ms and bytes, retained bytes and warm growth. (At G2 the "legacy" and "mir" configurations publish alike;
   `CompilerPipeline.md` says so.)
2. BU-48's pinned cold and warm `Analyze` A/B on Hello and Milestone26 (cold bytes exact; warm 0 B).
3. The `Purpose=Allocation` tests on the default pipeline (S4: the mir pipeline's Allocation tests at 0 B by default).
The owner accepted the cost in R5-M4; the entry reports it.

## 8. Order and holds

```
G285 (in parallel, own unit) ─┐
G167 or its (e) row (OI-2) ───┼─► G1 (split; G289 (2)) ─► G2 (records at check; P42 rows per OI-1; G157 (2)) ─┐
OI-1 answered ────────────────┘                                                                            ├─► G:R6-gen-switch ─► merge G2+G3 ─► G4 ─► G5, G6 ─► G7 …
                                          G286 (fixture), G287 (M5 count) ─► G3 (default codegen = MIR) ───┘
H1a (Q8) after G2.  G157 (48): not on this path.
```

| Item | Place | Note |
| --- | --- | --- |
| G285 | Lands before G2 | Design accepted, in implementation (R5-M151): the collector drops `InternalInvariant_Kd` while `TyCtxt.BindingComplete` is false (BU-38's Guard generalized, `Derived` deleted); G2 rebases on it |
| G167 | Before G2, or an approved (e) row (OI-2) | Binding resolves a generic key through `PositionSyntax.resolved` (PLAN G167); W1-type hold on the builder's file; its three tests move to another unbuilt form |
| G286, G287 | Before G3 | Native region fixture at each writer kind's widest value; M5's three classes counted |
| G289 (2) | G1 (or G2, §5) | Code comment |
| G157 (2) | G2 | Its test |
| G157 (48) | Off the switch path | The builder's read of `TyCtxt.SignatureSite` at the next change to `Mir/Build`'s body locals (R4, W1 hold); `Binding.Attributes.cs`'s one reset at BU's next change |
| BU's files | None in G1–G3 | G2b is gone; `Binding.Attributes.cs` is G157 (48)'s (BU). G7 and G9a delete Binding members (BU reviews the Binding part) |
| Driver (PD6) | No edit | G2 calls `MirAnalysis.Run` through `BuildMir` (`:206`) only |
| `MirDifferenceLedger.cs` (R4) | No edit | It still observes only mir compilations (`:118`) |

## 9. Rollback (S6) and `KIMI_PIPELINE` after G2

- Before the group merges: a regression in G2 or G3 is reverted on its branch. After: the whole group is reverted on dev (no patch on
  dev, plan §5.2). G4 lands only after G3's full sweep on dev (`'*.ll'` O0/O2, all milestones, CLI harnesses, snapshot, O2 corpus).
- G2 has no environment rollback. From G2 every pipeline publishes the records; an env value that hid them would be a second mode.
- `KIMI_CODEGEN=legacy` selects the old generator until G4 on either pipeline; it is G3's rollback.
- `KIMI_PIPELINE` after G2: `legacy` (the default) = the old analysis decides; the MIR is built, validated, analyzed (unpublished)
  and collected; construction records, validator findings and collector records publish at check. `mir` publishes the same; it differs
  only in the ledger's observation (`MirDifferenceLedger.cs:118`), the `[mir]` identity mark (`Compilation.Pipeline.cs:63-68`) and the
  test manifest entry (`src/Kimi/Testing/TestCatalog.cs:114-117`). VERIFICATION and `CompilerPipeline.md` say so; H12 removes the switch.

## 10. Owner questions and risks

**OI-1 (departure D1). The 110 P42 rows at G2.** (a) Recommended: G2 adds the declaration's `StaticStorage` record to each row's exact
set and re-baselines Milestone 42's anchor, superseding R5-M144's "(not G2's)"; P42 reverts them. (b) Observe only Binding's records
in those rows (a weakening). (c) P42 before G2 (against R5-M13 (b); a full-process package). See §4.

**OI-2 (departure D2). G167 at G2.** A valid form the default builds today (`values[p]`, `p: I`) becomes a located `Unsupported_Kd`
at default `check`. (a) Recommended: G167 lands before G2 as an R6-gen unit (Binding with BU, a W1-type hold), the PLAN row's shorter
rule. (b) Add it to PD27's D2 (e) list (STATUS and PLAN rows), G167 later. (c) Neither: not allowed (STATUS must state the boundary).

**OI-3. Verification lanes.** From G2 the default Session publishes what `-Pipeline mir` does, and from G3 what sel does. (a)
Recommended: from G2 the mir lane runs only as the ledger's run, from G3 the sel lane stops (G4 removes its rows); one heavy run less
per window. (b) Keep every lane until G4.

| Risk | Response |
| --- | --- |
| An analysis exception in R4's driver faults default `check` from G2 | R4's merges already run the mir Session over every test; G2's Session runs the driver by default |
| LSP latency: the driver reruns over every library body per rebind (≈ 7 ms small, more large; H) | G2's A/B (§7); if the owner finds it too high, running the driver at generation only until H5 is a departure from R5-M4 for the owner |
| Census gap: 110 legacy pins not covered by sel evidence | G2's first default Session on the prototype is the census (approval needed) |
| OI-1 or OI-2 unanswered blocks G2 | Ask now, non-blocking; G1, G285, G286, G287 proceed meanwhile |
| X1 evidence is at `17cd554ab` | Batch 56's sel Session at `228d6f0b4` confirms the 110-row set; later units added only Language records on every pipeline |
| The group branch lives across G2 and G3 beside R4 | Hold on `Compilation.Pipeline.cs` from G1; G3's sweep scheduled in a quiet slot; R4 merges dev after the release |
| `KIMI_PIPELINE=mir` keeps a name with almost no effect until H12 | Documented (§9) |
