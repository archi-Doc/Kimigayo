# R6-gen G9: Emission-only Binding members and the successor of `GenerationFailed_Kd`

Status: G9's census and design (plan §4.9 row G9, review G:R6-gen-del, hold "no"), revision 1: accepted with the planning session's
rulings Q1-Q10 (2026-10-31); G9a implemented on `claude/r6-g9` from `25263b627` (G8 landed, R5-M163). Class Major, no design
review. This is a working record, not authority. What it decides becomes authority only through the DECISIONS entries of §6.4, as
they land. Base of revision 0: dev `f406b0905` (G2-G7 landed, R5-M162), G8 counted as designed (`claude/r6-g8-design`
`b4d5b9abf`, its §2.1 and §2.2), so a reader inside G8's deleted files does not keep a member alive.

## G9a as implemented

| Item | As implemented | Departure from revision 0 |
| --- | --- | --- |
| §1.1 six deletions | as designed | none |
| §1.2 `DictionaryStorageCall` | `DictionaryStorageCall(key, value)`: the findEntry call only, keyed by `(key, value)` | none |
| §1.3 `requireClosed` (Q2 yes) | parameter deleted; the 5 product callers and `TyCtxtTest:1798` drop the argument; the 3 default-argument test callers are unchanged | none |
| §1.4 stale text | the three ruling-Q3 sites and the five further sites, each checked to mean the old generator; plus `KimiLibrary.DictionaryStorage.cs`'s `DictionaryCallee` comments (G8's review N5: the Related index, the initialization bridge and the old emitter) | N5 added by the planning session |
| DECISIONS | R5-M164 with amendment lines on BU-24 (`PlaceOriginSource`'s removal condition: "BU states it at review", Q3), R5-M28 and R5-M162 | none |
| Net | Binding −92, Analysis 0, tests 0, DECISIONS +23 | the design's ≈ −95 |

## G9b1 and G9b2 as implemented

| Item | As implemented | Departure |
| --- | --- | --- |
| G9b1 regex (510 files) | 1,227 `Describe` calls; 913 adapter calls: 634 `out _` forms, 155 `Assert.True(X.M(.., out var e), e)` forms, 124 forms whose text fed `Describe` | the 279 regex-able text forms moved here from G9b2: removing `Describe`'s text parameter needs them |
| G9b1 by hand | 7 sites whose text fed `Describe` through a local, a helper or a reused variable; `Describe(Compilation)` adds the Emission partition's records | — |
| G9b1, G8 review | `FunctionCodegenTest`'s `Signature` calls `WrittenModule(c, module, out written)`; `WrittenModule(c)` makes its own module | — |
| G9b2 | 12 residual hand forms; the 3 text asserts plus `FloatEmissionTest.AssertEmissionSupport`'s and `AggregateEmissionTest`'s, which restated the bool (an adapter returned text exactly when false); `AggregateEmissionTest` asserts an Error explains the refusal; Benchmark ×3 (`Require`'s message no longer nullable); the 4 adapters deleted; ARCH, CODEMAP, VERIFICATION | ARCH's Generation row cites R5-M64 (R5-M165 lands with G9b3) |
| Net | G9b1: tests +1; G9b2: Core −37, Benchmark 0, tests −3, docs 0 | the design's ≈ −45 |

"Plan" is the [R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` =
`src/Kimi/Compiler/`, `Bi/` = `C/Binding/`, `T/` = `tests/xUnitTest/Tests/`. Line numbers are at `f406b0905`. No build, test or
probe was run. Reach comes from word-boundary greps over `src` (with `src/Benchmark` and `src/Playground`) and `tests`. One history
check was run (`git log -S`) for the old reader of `DictionaryStorageCall`. **(H)** marks an unchecked claim. Commit messages say
"R6-gen G9" (PLAN's issue G9 is unrelated).

Authority used: plan row G9; R5-M18 (the owner's choice of `ArtifactPublicationFailed_Kd`, and the G9a/G9b split); C-package §3.10 and
its G9b line (`draft/Sketches/R5 Step 0/C-package.md:335-352, :569`), which govern C-design-2's table where they differ; R5-M64 with its
amendments (the adapters); R5-M162 (G7: `ReportPublicationFailure` and G9's two added members); and the G8 ruling Q3 (Binding's stale
text goes to G9).

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| G9-1 | Six members of G9's list are dead after G7 and go: `IsInapplicableVirtualBody`, `ImplementationContext`, `IsVerifiedSpecialization`, `SameModuloOpenOrigins` (both overloads), `AdtDef.IsOrInherits` and `KimiLibraryCatalog.IsArrayOperation`. None has a reader in `src`, `tests` or `src/Benchmark` | 1.1 |
| G9-2 | `PrepareTypeStorage` **stays**: Binding's retention, dispatch, identity and Item contexts call it. `DictionaryStorageCall` **stays but narrows** to the find call: its one reader is `CallPlan.Of` for a dictionary literal, which MIR construction (`Mir/Build/MirBuilder.Expansions.cs:273`) and HIR completeness (`Bi/Binding.Hir.Facts.cs:88`) read. The `ClearEntries` arm and its throw lost their last caller, `LlvmEmitter`, at G7 | 1.2 |
| G9-3 | G9a also removes what the deletions make redundant: `FunctionItemContext`'s `requireClosed` mode (after `ImplementationContext` goes, every product caller passes `false`; Q2), the stale Binding text (G8 ruling Q3) and amendment lines in BU-24 and R5-M28 | 1.3, 1.4 |
| G9-4 | `GenerationFailed_Kd` has one producer, `Compilation.ReportPublicationFailure`, fed by `EmissionArtifacts.Publish`'s failure text. G9b maps each throw site (§2.1): output and publication-time native-configuration failures become `ArtifactPublicationFailed_Kd` (Input, the project file, the cause as a note), the missing embedded backend record becomes `InternalInvariant_Kd`, and the repeated `NativeConfiguration.Validate` is deleted (C-package §3.10) | 2.1 |
| G9-5 | Publication records its own failure (`EmissionArtifacts` owns publication, as R5-M64 decided). `Project.PublishArtifacts` only renders, and `ReportPublicationFailure` goes. The out-string adapters `WriteIr`, `Validate`, `Refusal` and `MirFailure` go, and their 1,060 test and Benchmark calls call `Generate`. The `R6-gen` Transitional count goes from 5 to 0 | 2.2, 2.3 |
| G9-6 | The code is owner-approved: R5-M18 records 「GenerationFailed_Kdの後継はArtifactPublicationFailed_Kd」 (2026-10-18), an Input code with net 0 codes. `DiagnosticCode` is not in ARCH's closed-vocabulary table. No further owner approval is needed, unless Q4 chooses a new `Unsupported_Kd` reporter | 3 |
| G9-7 | Four units: G9a (Binding, BU's review); G9b1 (test helpers, regex-only); G9b2 (adapters deleted); G9b3 (the record change, a behavior change). Total ≈ 3 h 05 min | 5 |

## 1. G9a: Binding members (BU reviews the commit)

### 1.1 Dead members: no reader after G7 and G8

| Member | Site | Readers (src, tests, `src/Benchmark`) | What it used, still read elsewhere | Lines |
| --- | --- | --- | --- | ---: |
| `IsInapplicableVirtualBody` | `Bi/Binding.VirtualOverrides.cs:193-198` | none. `LlvmEmitter`'s caller went at G7. The DECISIONS R5-M77 line 10703 is history | `VirtualApplicability` (`TyCtxt.Virtuals.cs:25`, `VirtualApplicabilityDefinitionTest:99`), `virtualOverrides` | −7 |
| `ImplementationContext` | `Bi/Binding.FunctionItems.cs:44-46` | none | `FunctionItemContext` (live), `FunctionItemType` (caller `:463`) | −4 |
| `IsVerifiedSpecialization` | `Bi/Binding.Specializations.cs:37` | none | `specializations` (`:40`, `:217`, …) | −2 |
| `SameModuloOpenOrigins` ×2 | `Bi/Binding.OpenOrigins.cs:74-122` | none (only its own recursion) | `HasOpenOrigin` (9 callers), `BoundType.SameExceptOrigins` (5) | −49 |
| `AdtDef.IsOrInherits` | `Bi/AdtDef.cs:126-138` | none (R5-M162's G7 addition) | `Base` | −14 |
| `KimiLibraryCatalog.IsArrayOperation` | `Bi/KimiLibraryCatalog.cs:29` (**shared-file hold**, ARCH migration rules) | none (R5-M162's G7 addition). The `ArrayReserve…ArraySwap` order carries no comment that names it | — | −2 |

No `ArchitectureRulesTest` row and no test pins any of these by name or file (grep).

### 1.2 Members that stay

| Member | Verdict | Readers today |
| --- | --- | --- |
| `PrepareTypeStorage` (`Bi/Binding.Structs.cs:54`) | **live**, stays | `Binding.FunctionItems.cs:107`, `Binding.Retention.Rows.cs:566`, `Binding.Retention.Sources.cs:181`, `Binding.Retention.Storage.cs:539`, `Binding.Structs.cs:87`, `Binding.TypeIdentity.cs:83`, `TyCtxt.Dispatch.cs:101`, `TyCtxt.Substitutions.cs:120`; tests `AssociatedIdentityTest:276, :305`, `TyCtxtTest:871, :1299, :1442` |
| `DictionaryStorageCall` (`Bi/Binding.DictionaryStorage.cs:14-34`) | **live, narrowed** | `CallPlan.Of` (`Bi/CallPlan.cs:176-177`, always `FindEntry`), which `MirBuilder.Expansions.cs:273` and `Binding.Hir.Facts.cs:88` read. The old reader that passed any callee was `LlvmEmitter.cs:38` (deleted at G7, `d1d2e40d0`) |

Narrowing (Bi): `DictionaryStorageCall(BoundType key, BoundType value)` builds the find call only. The `callee` parameter, the
`find` switch with its `InvalidOperationException` arm, the unit-result branch and the `Callee` part of the `dictionaryStorageCalls`
key all go. The comment's "these generation bridges" becomes "the dictionary literal's find call". `CallPlan.cs:177` drops its
argument. The net is ≈ −10 (H). `DictionaryCallee` itself stays: the Backend, the collector, the old analysis and tests read it.
R5-M28 (DECISIONS:9526-9527, "takes the callee and fails closed …") gets an amendment line.

### 1.3 Made redundant by the deletions

| Item | Site | Change | Lines |
| --- | --- | --- | ---: |
| `FunctionItemContext(type, bool requireClosed = true)` | `Bi/Binding.FunctionItems.cs:84-89` | After `ImplementationContext` goes, the `true` arm has only test callers (`CallableRegionInvalidationTest:22, :34`, `VirtualCallSelectionTest:66`), and its comment's "generation requires closed arguments" names no reader. Delete the parameter. Callers drop their argument: `Binding.EffectBounds.cs:322, :705`, `TyCtxt.Effects.cs:56` (**hold file**), `TyCtxt.Substitutions.cs:84` (**hold file**), `C/Analysis/OwnershipAnalysis.cs:917` (old Analysis; announce to R4), `TyCtxtTest:1798`. **Q2** | ≈ −4 |
| Comment of `FunctionItemType`'s declaring argument | `Bi/Binding.FunctionItems.cs:19` | none: `:463` still passes it | 0 |

The 3 test callers that use the default pass Items of closed programs, so the result does not change (H).

### 1.4 Stale Binding text (G8 ruling Q3) and DECISIONS

| Site | Now | Change |
| --- | --- | --- |
| `Bi/Binding.ComparisonPlans.cs:23` | Transitional reason "… the old effect walk (H5) and the old generator (G7)" | "… its readers, the old effect walk (H5)". The stage `R6` stays (H11) |
| `Bi/TyCtxt.Substitutions.cs:63-65` (**hold file**) | "as the old analyses and generator have always …; the old generator's at G7, the old analyses' at R6-check …" | drop the generator clauses. The `R7` Transitional stays |
| `Bi/Binding.PlaceAuthority.cs:199-201` | "Exception until R6-gen: `PlaceOriginSource` keeps its own walk, which the old Emission reads and which also passes forms this treats as roots" | "Exception: … which passes forms this treats as roots (an Array range selection)". Its removal condition is **Q3** |
| `Bi/Binding.ClosureReceivers.cs:21`, `Bi/BoundClosure.cs:12, :21`, `Bi/Binding.RawOrigins.cs:48`, `Bi/Binding.Structs.cs:82` | list "Emission" or "generator" among the readers (H: each means the old generator) | drop the deleted reader. These lines are found by the same grep as Q3's three and are not in its list (H) |
| DECISIONS BU-24 (`:6750`; `:6766` names `SameModuloOpenOrigins`; `:6790` the `PlaceOriginSource` exception) | — | amendment line: `SameModuloOpenOrigins` is deleted, and the exception's first reason ("old Emission reads it") ended at G7; the second stays (Q3) |
| DECISIONS R5-M28, R5-M162 | — | amendment lines (§1.2; "G9's list done") |

## 2. G9b: `GenerationFailed_Kd`, the publication record and the adapters

### 2.1 Producers and successors

The one producer in `src` is `Compilation.ReportPublicationFailure` (`C/Core/Compilation.Generation.cs:126-131`). Its one caller is
`Project.PublishArtifacts` (`src/Kimi/SolutionAndProject/Project.cs:265-273`), which runs for `kimi build` (`Project.cs:577`) and
`kimi test` (`Unit/Command/TestCommand.cs:131`). The text comes from `EmissionArtifacts.Publish`'s `catch` (`C/Emission/EmissionArtifacts.cs:83-87`).
Each throw site:

| Site (`EmissionArtifacts.cs`) | Cause | Kind | Successor |
| --- | --- | --- | --- |
| `:31-34` | the compiler's embedded backend version or hash is missing (`WindowsProfile.cs:81-82` reads it from the release resource) | contract (compiler build) | `InternalInvariant_Kd` at the project, no note (**Q6**) |
| `:98-101` | `NativeConfiguration.Validate` repeated | — | **deleted**. Preparation already runs it (`DependencyConfiguration.cs:77`, through `Compilation.cs:209`, `Project.cs:323`; C-package §3.10 "one owner") |
| `:114`, `:119`, `:46` (`CheckLibraryPath`), `:48` (archive hash) | `kimi_backend` kind or Sha256, a `.lib` input path, an archive that does not match | publication-time native configuration | `ArtifactPublicationFailed_Kd` |
| `:55` (`CheckPath(LlvmBin)`) | invalid tool path | publication-time configuration | `ArtifactPublicationFailed_Kd` |
| `:169` | an import without a `NativeLibraries` supply for the target. Binding's `MissingNativeRequirement_Kd` covers only a missing requirement or self-targeted record (H: no overlap) | native supply | `ArtifactPublicationFailed_Kd` |
| `:152` | "Native supplies required by dependency modules are not linked yet" | a valid form that is not implemented (H) | `ArtifactPublicationFailed_Kd` under C-package §3.10; **Q4** |
| `IOException`, `UnauthorizedAccessException`, `ArgumentException`, `NotSupportedException` from paths, `CreateDirectory`, the streams, `Generate`'s writer and `Replace` | output | output | `ArtifactPublicationFailed_Kd` |
| `Generate` returns false | generation | — | unchanged: `Generate`'s own records (located codes or `InternalInvariant_Kd`) |

Record shape: `ArtifactPublicationFailed_Kd`, Category `Input`, in `DiagnosticPartition.Emission` with the `Emission` requirement
(R5-M18: both names stay through R6). It sits at the project file, with the exception's message as its note, as
`ProjectPreparationFailed_Kd` (`Checking/CheckService.cs:44`) and `SourceReadFailed_Kd` (`Project.cs:556`) carry theirs. Typed facts
(cause, path) are **Q5**. `Publish` invalidates the Emission partition before it records, as `ReportPublicationFailure` did. A
publication failure after a successful `Generate` therefore stands alone, and one before it precedes any generation record.
Toolchain failures (`NativeToolchain`, `Project.cs:229-231`) stay plain text (C-package §3.10; R9).

### 2.2 Code changes (product)

| File | Change | Net (H) |
| --- | --- | ---: |
| `DiagnosticCode.cs:159` | `GenerationFailed_Kd` → `ArtifactPublicationFailed_Kd`, in place (no other code's ordinal moves) | 0 |
| `DiagnosticCode.tinyhand:774-776` | the row is replaced: `Category="Input"`, `Message="The build artifacts cannot be published"` (wording: **Q9**), no `Arguments` | 0 |
| `EmissionArtifacts.cs` | `Publish(Compilation, ArtifactPaths?, out string? irPath)` records its failure as in §2.1 and returns false. The `failure` out parameter goes. The public 3-argument overload goes, or keeps only `irPath` (**Q8**). The `:98-101` call goes | ≈ −2 |
| `Project.cs:263-276` | no failure branch: on false, it renders the Emission partition | −5 |
| `Compilation.Generation.cs` | delete `ReportPublicationFailure` (`:123-131`). Delete the adapters `WriteIr`, `Validate`, `Refusal`, `MirFailure` (`:50-66`, `:192-208`) in G9b2. The contract header (`:9-10`, `:24-28`) names `EmissionArtifacts.Publish`'s record and drops the adapters | ≈ −45 |

### 2.3 The out-string adapters and their callers

| Caller form (test files unless named) | Count | Re-point | Unit |
| --- | ---: | --- | --- |
| `X.WriteIr(W, out _)`, `X.Validate(out _)` (asserts, `&=`, measured lambdas, theory switches) | 763 | `X.Generate(W)`, `X.Generate(TextWriter.Null)` | G9b1 |
| text-binding forms (`out var e` read as a message, in `if`, or reused) | 297 | `X.Generate(W)`, with the message `MinimalEmissionTest.Describe(X)`. Uncommon shapes are edited by hand (≈ 40, H) | G9b2 |
| text asserts: `FunctionCodegenTest:1424`, `PipelineSwitchTest:129` (`Assert.Null(failure)`), `PipelineSwitchTest:275` (`Assert.Contains("MIR", failure)`) | 3 | delete the two nulls (the bool asserts it). `:275` asserts the gate's refusal by the version's MIR records (`MirRecords` > 0, or the Emission partition empty) (H) | G9b2 |
| `CompilationTestHelper.WriteIr(Compilation)` (`tests/xUnitTest/CompilationTestHelper.cs:57-65`; 114 callers) | 1 | keeps its name and callers; calls `Generate`, `Assert.Fail(MinimalEmissionTest.Describe(c))` | G9b2 |
| `MinimalEmissionTest.Describe(Compilation, string? error)` (`T/MinimalEmissionTest.cs:363-366`) | 1,345 calls, 424 files | becomes `Describe(Compilation)`. It adds the Emission partition's records and the MIR record count, which are what the adapter text summarized. Callers are rewritten by one regex (`Describe\((\w+), [^()]*\)` → `Describe($1)`); 1,163 of them passed `null` | G9b1 |
| `src/Benchmark`: `CompilerPipelineBenchmark.cs:110`, `CompilerPlanMeasurements.cs:118`, `InheritanceLifecycleMeasurements.cs:133` | 3 | `Generate`. `:110`'s message becomes the Emission partition's codes (H). `InternalsVisibleTo("Benchmark")` exists (`Kimi/Properties/AssemblyInfo.cs:5`) | G9b2 |

The total is 1,060 adapter calls in 366 files plus `Describe` in 424, a union of 522 files. Every edit is one line, so the net is ≈ 0.
`Generate` is internal, and the tests see it (`AssemblyInfo.cs:6`). Allocation: `out _` callers allocated no text on success, so measured
lambdas are unchanged. On failure the text allocation disappears (H).

### 2.4 Tests of the record (G9b3)

| Site | Change |
| --- | --- |
| `EmissionArtifactsTest` (16 `Publish` calls; failure asserts `:67-180`, `:240`, `:333-354`) | the `out error` argument is dropped. A failure asserts the one `ArtifactPublicationFailed_Kd` record, reading its note text where it was asserted ("Sha256 assertion", "SHA-256", "has no NativeLibraries supply", "dependency modules"). `:286` reports `ArtifactPublicationFailed_Kd` |
| `EmissionArtifactsTest.ExplicitKernel32PathRequiresMigration` (`:131-141`) and `BackendSupplyExpandsItsSelfTargetedRequirement`'s "disagrees" step (`:125-127`) | these settings errors come from `NativeConfiguration.Validate` (`NativeConfiguration.cs:54, :64, :85`), which publication no longer repeats. They move to the preparation layer (`NativeConfiguration.Validate` on the settings, or a prepared project's `ProjectPreparationFailed_Kd`), and the no-output-directory assertion stays (H) |
| `LibraryEmissionTest:59`, `PipelineSwitchTest:154-156` | drop `failure`. `:156` asserts that no `ArtifactPublicationFailed_Kd` exists |
| `GenericGenerationDiagnosticTest:106`, `GenericObjectFactoryTest:40`, `InlineLayoutTest:155`, `PipelineSwitchTest:183`: `DoesNotContain("GenerationFailed_Kd")` | `"InternalInvariant_Kd"`, the generation entry's catch-all. The meaning is the same: a located check record and no generic failure (**Q7**) |
| New | one Fact: a publication failure (an output directory blocked by a file) gives `ArtifactPublicationFailed_Kd` (Input, Emission partition, note) and no IR. Its CLI rendering goes through `DiagnosticContractTest.DiagnosticConsole` (`kimi build`). `DiagnosticContractTest`'s catalog checks cover the new row |
| History comments naming the old code (`ElementBorrowEmissionTest:136`, `FunctionCodegenTest:44`, `GenericCallbackEmissionTest:36`, `InlineLayoutTest:143`, `ReferenceDiagnosticTest:112`) | trim the old-generator clause (optional; 5 lines) |

**Diagnostic snapshot:** no expected difference. `DiagnosticSnapshotTest` checks and never generates or publishes, and the latest
snapshot (`20261029-210933-274-session-bu70-r1-session`) has no `GenerationFailed_Kd`. G9b3 runs it to show this. Neither the CLI
harnesses nor `scripts/` name the code (git grep).

## 3. Vocabulary, Transitional table, ledger and records

- **Code:** owner-approved (R5-M18, quoted in §0 G9-6; S2-S4-decisions O2; C-package §3.10 (a)). It is net 0 and needs no further
  approval. ARCH's closed-vocabulary table has no `DiagnosticCode` row. Q4's alternative (`Unsupported_Kd` from publication) would add
  a reporter beyond ARCH "Closed vocabularies"' three and need the owner.
- **Catalog hooks:** the enum row and the tinyhand row only. It is not a Binding failure, so it needs no `BindingFailure`,
  `DiagnosticRequirement.tinyhand` row or publication branch (the memory checklist's Binding hooks do not apply). The report uses
  `DiagnosticRequirement.Emission` through the partition, unchanged.
- **DIAGNOSTICS §4.2:** a row after "Later phases": "Emission (one per generation request): `Compilation.Generate`'s records (located
  codes and `InternalInvariant_Kd`) or `EmissionArtifacts.Publish`'s `ArtifactPublicationFailed_Kd` (Input: output I/O and
  publication-time native configuration and supplies, at the project file with its cause as a note), invalidated by each generation or
  publication. The partition and requirement names stay through R6 (DECISIONS R5-M18)" (G9b3).
- **ARCH:** `:260`'s temporaries lose "the out-string `WriteIr`/`Validate` adapters (C0a to G9b)" (G9b2) and "`ReportPublicationFailure`
  (G7 to G9b)" (G9b3). The `src` count of `[Transitional("R6-gen"` goes 5 → 1 → 0, which §5.4 item 5 needs. The stage ledger is
  unchanged: G11 records R6-gen's end. `:99`'s "Generation" row needs no edit (H).
- **CODEMAP `:53`** (`WriteIr`, `Validate`) and **VERIFICATION `:186`** ("Analysis entry points … `WriteIr`, `Validate`") name `Generate`
  (G9b2). These edits are Auto, because the change invalidates them. VERIFICATION is an input of the verify manifest, so the unit
  needs fresh evidence.
- **PLAN (the planning session's):** G147 (2), G158 and G300 still name "the old generator's `GenerationFailed_Kd`", which ended at
  G4. The next actions also need updating.

## 4. Owners, holds and reviews

| Area | Owner | Units | Hold or announcement |
| --- | --- | --- | --- |
| `Bi/**` | BU (reviews G9a's commit) | G9a | `KimiLibraryCatalog.cs`, `TyCtxt.Substitutions.cs`, `TyCtxt.Effects.cs` (Q2) are shared-protocol files (ARCH migration rules): the planning session records the hold in PLAN §3 |
| `C/Analysis` (old) | D5 until R6-check | G9a (one argument, Q2) | announce to R4; G8c edits `OwnershipAnalysis.cs` too, so serialize |
| Core `Compilation.Generation.cs` | R6-gen | G9b2, G9b3 | none (not a listed shared file) |
| `C/Emission/EmissionArtifacts.cs`, `SolutionAndProject/Project.cs` | R6-gen (Emission freeze, D5) | G9b3 | none |
| Diagnostics catalog, DIAGNOSTICS | Diagnostics | G9b3 | none |
| 522 test files, `src/Benchmark` | R6-gen | G9b1, G9b2 | announce a window: R4's and BU's open branches that touch these tests rebase over one-line edits (risk §6.3) |

All units are in G:R6-gen-del. G9a's Binding commit takes BU's review (G0's "G7 and G9a delete Binding members"). G9b3 is a behavior
change (a code's identity and category) with its own tests, and the planning session reviews it in the group.

## 5. Sizing and split (implementation time without heavy runs)

| Unit | Content | Completion condition | Time (H) | After |
| --- | --- | --- | ---: | --- |
| G9a | §1: six deletions, the narrowing, `requireClosed` (Q2), stale text, DECISIONS R5-M164 with its amendment lines | grep: no hit of the six names outside history; the build passes; the G9a classes pass; diagnostic and semantic snapshots equal to the base | 35 min | G8 by plan order. There is no file overlap with G8a, G8b or G8d, so it may run beside them (Q1) |
| G9b1 | `Describe(Compilation)` and its 1,345 calls; the 763 `out _` adapter calls (two regexes) | the build passes; the whole xUnit pass set equals the base's (per name, with counts) | 40 min | G8 (fewer test files moving) |
| G9b2 | the 297 text forms, 3 text asserts, `CompilationTestHelper.WriteIr`, Benchmark, deletion of the 4 adapters, CODEMAP, VERIFICATION, ARCH line | no `.WriteIr(`/`.Validate(out` outside history; the slnx build passes; the pass set is equal | 50 min | G9b1 |
| G9b3 | §2.1-§2.2's record, §2.4's tests, DIAGNOSTICS §4.2, ARCH, DECISIONS R5-M165 | `GenerationFailed_Kd` occurs only in history; the G9b3 classes pass; the snapshot is equal; the CLI harnesses pass | 55 min | G9b2 |
| Session | base before G9a (or reuse G8's candidate), candidate after G9b3 | §6.2 | heavy | all |

The total is ≈ 3 h. G9b3 is the tightest. If it grows, its records (DIAGNOSTICS, ARCH, DECISIONS) move to a docs-only commit after it,
but the code and its tests cannot split, because the build needs both.

## 6. Commits, checks, net size, risks, questions

### 6.1 Commits

| Unit | Commit | Kind | Net (H) |
| --- | --- | --- | ---: |
| G9a | a1 Binding deletions and narrowing, with stale text, BU-24/R5-M28/R5-M162 lines, R5-M164 | behavior-preserving | Binding ≈ −95; Analysis −0 (one argument); tests ±1 |
| G9b1 | b1 helper signature and `out _` re-points | behavior-preserving (tests) | tests ≈ +5 (Describe body) |
| G9b2 | b2 text forms, Benchmark, adapters deleted | behavior-preserving (product unchanged; tests) | Core ≈ −37; tests ≈ −5; docs ±4 |
| G9b3 | b3 the publication record (one commit: code, catalog, tests) | behavior change | Core −9; Emission ≈ −2; Project −5; tests ≈ +25; docs ≈ +8 |
| **Total** | | | **≈ −120**. The plan's ≈ −400 counted the 446 failure strings, which G7 deleted |

### 6.2 Checks

- G9a: incremental build; `VirtualApplicabilityDefinitionTest`, `VirtualCallSelectionTest`, `CallableRegionInvalidationTest`,
  `SpecializationBindingTest`, `TyCtxtTest`, `AssociatedIdentityTest`, `DictionaryLibraryTest`, `DictionaryLiteralEmissionTest`,
  `HirColumnsTest`, `MirBuildTest`, `KimiLibraryShapeTest`, `ArchitectureRulesTest`; the diagnostic snapshot and the semantic snapshot
  (HIR, MIR) against the base (VERIFICATION, behavior-preserving commits).
- G9b1 and G9b2: the `Kimigayo.slnx` build (Benchmark); the whole xUnit suite, whose result XML is compared per name with counts
  against the base run (truncated theory names collide).
- G9b3: `EmissionArtifactsTest`, `LibraryEmissionTest`, `PipelineSwitchTest`, `DiagnosticContractTest`, `DiagnosticOwnerTest`,
  `GenericGenerationDiagnosticTest`, `GenericObjectFactoryTest`, `InlineLayoutTest`, `NativeToolchainTest`, `SolutionInputTest`, the
  preparation tests that receive §2.4's moved cases; `-DiagnosticSnapshot` (expected 0 differences); the five CLI harnesses.
- Session at the end (planning session's go): `'*.ll'` with `compare-fixtures.ps1` (identical: no codegen change), milestones, both
  snapshots.

### 6.3 Risks

| Risk | Response |
| --- | --- |
| A regex mangles an uncommon call (lambda, reused variable, `ref` capture) | the build catches syntax. The per-name pass-set comparison catches meaning. Text forms are G9b2's hand-reviewed list |
| 522 test files collide with R4's or BU's open branches | one-line edits rebase cleanly (H). Announce the window, and land G9b1/G9b2 between their merges |
| `Describe(X)` is evaluated eagerly in `Assert.True(cond, message)` at ≈ 300 more sites | Describe's cost is paid today at 1,163 sites. No allocation-measured lambda contains it (grep) |
| Deleting the repeated `Validate` lets a settings change made after preparation publish | product `ProjectFile`s are not mutated after preparation (H). Tests that mutated them move to preparation (§2.4) |
| Removing the public `WriteIr`, `Validate` and the `Publish` failure parameter breaks an outside caller | none in `src/Playground`, `src/Benchmark` (re-pointed) or the extension (TypeScript) |

### 6.4 DECISIONS entries (drafts; R5-M163 is G8's, so these are R5-M164/165, renumbered at landing)

> **R5-M164 Emission-only Binding members deleted (R6-gen G9a)**: `IsInapplicableVirtualBody`, `ImplementationContext`,
> `IsVerifiedSpecialization`, `SameModuloOpenOrigins`, `AdtDef.IsOrInherits` and `KimiLibraryCatalog.IsArrayOperation` lost their
> last readers at G7. `PrepareTypeStorage` stays (Binding's). `DictionaryStorageCall` builds only the dictionary literal's find call,
> which MIR construction reads. `FunctionItemContext` has no closed-only mode. The stale Binding text naming the old generator is
> trimmed. Amends BU-24, R5-M28 and R5-M162.

> **R5-M165 Publication records its failure; `GenerationFailed_Kd` and the out-string adapters removed (R6-gen G9b)**:
> `EmissionArtifacts.Publish` records `ArtifactPublicationFailed_Kd` (Input; the owner's choice, R5-M18) at the project file, with its
> cause as a note, in the Emission partition, for output I/O and publication-time native configuration and supplies. It records
> `InternalInvariant_Kd` for a missing embedded backend record. It no longer repeats `NativeConfiguration.Validate`, because
> preparation owns that check. `Compilation.ReportPublicationFailure`, `WriteIr`, `Validate` and their texts are deleted. Tests and
> Benchmark call `Generate` and read its records. No `[Transitional("R6-gen")]` remains. Rejected: reusing `ProjectPreparationFailed_Kd`
> or recategorizing `GenerationFailed_Kd` (C-package §3.10); keeping a string adapter for tests (a second entry). Amends R5-M18, R5-M64
> and R5-M162.

### 6.5 Open questions

| # | Question | Recommendation | Who |
| --- | --- | --- | --- |
| Q1 | May G9a run beside G8, since it has no file overlap except G8c's `OwnershipAnalysis.cs` under Q2? | yes, with Q2's argument edit serialized after G8c | planning session |
| Q2 | Should `requireClosed` be removed in G9a? It touches two more hold files and one old-Analysis call | yes: a mode whose `true` arm only tests use is a symptom (AGENTS) | planning session |
| Q3 | `PlaceOriginSource`'s own walk after its first reason ended: permanent (a design reason) or a named stage? | BU states it in BU-24's amendment line. A temporary structure needs a removal condition | BU / planning session |
| Q4 | Dependency-module supplies (`:152`) are a valid unimplemented form, so AGENTS's rule says `Unsupported_Kd`. C-package §3.10 maps it to the publication code. A publication `Unsupported_Kd` adds a reporter (owner). Binding's check is the listed reporter | G9b follows the package. Open a PLAN issue: `Unsupported_Kd` at the `#LibraryImport` from Binding's check (BU) | planning session; owner only for a new reporter |
| Q5 | Note text or typed facts (cause, path) for the new code? | note now, by the Input precedent. Typed facts are a later Diagnostics unit | planning session |
| Q6 | Is the missing embedded backend record a contract failure (`InternalInvariant_Kd`)? | yes: the compiler's release resource, not the project | planning session |
| Q7 | Should the four `DoesNotContain("GenerationFailed_Kd")` become `InternalInvariant_Kd` or be deleted as vacuous? | re-point: same meaning, not weaker | planning session |
| Q8 | Should the public `Publish(Compilation, out irPath, out failure)` overload be deleted or reduced to `irPath`? | delete; tests pass `null` paths (a wrapper without semantics) | planning session |
| Q9 | Message wording | "The build artifacts cannot be published" | Diagnostics |
| Q10 | Should PLAN G147 (2), G158 and G300 drop "the old generator's `GenerationFailed_Kd`" clauses? | yes, in the next PLAN edit | planning session |
