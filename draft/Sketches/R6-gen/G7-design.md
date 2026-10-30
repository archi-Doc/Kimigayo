# R6-gen G7: deleting the old lowering, plans, driver and instance machinery

Status: G7's census and design (plan §4.9 row G7, review G:R6-gen-del, hold "adv while R4 is active"), revision 1: the planning
session's rulings Q1-Q8 applied, for the implementation on `claude/r6-g7` from dev `70b530151` (dev's commits after `1f2d7dea6` touch
only PLAN, so the line numbers hold). Class Major, no design review. Working record, not authority: what it decides is authority only
as DECISIONS R5-M162 (§7's draft, as landed). Revision 0 is `claude/r6-g7-design` `0bc01768f`.

## Revision 1 (the rulings)

| Item | Revision 0 | Revision 1 | Ruling |
| --- | --- | --- | --- |
| Q1 `StaticScalarEntry` | to G8 | to G8; recorded in R5-M162 | yes |
| Q2 Benchmark readers | re-point both | per reader: re-point only where the measurement still measures MIR code generation, else delete. `CompilerPlanMeasurements`' "emission" phase times warm MIR generation of each workload: **re-pointed**. `InheritanceLifecycleMeasurements`' pipeline times fresh, edited and warm compilations through generation: **re-pointed**. The split-IR check compares `WriteIr` with itself: **deleted**. Each re-pointed command is run once in c1; a workload MIR does not accept loses its row, named in c1's message | yes, per reader |
| Q3 ratchet rows | keep at 0 until G10 | same | yes |
| Q4 evidence | full `'*.ll'` | a base Session (`'*.ll'`, milestones, snapshots) in the G7 worktree before any edit, a candidate Session after the last commit, `.ll` identity by `compare-fixtures.ps1`; each heavy run on the planning session's go | yes |
| Q5 generator views | delete at G7 | delete where the old generator alone reads them: `UsesImport` (with `usedImports`), `GetBorrowInputState`, `TrySymbolPlaceAt`, `GetElementState`, `IsPathWithin`, `ParameterCount`, `DeclaredResultType`; the re-scan after c3 adds only members the deleted views alone called | yes |
| Q6 publication failure | `Compilation.ReportPublicationFailure` | same: `[Transitional("R6-gen")]`, removal G9b, the same record; no vocabulary entry | yes |
| Q7 G8 and G9 lists | additions | recorded in R5-M162 and here; PLAN is the planning session's | yes |
| Q8 PLAN | closures at landing | the planning session closes G69, SP, G157 (5) and (22) | yes |
| Commit order | c1-c6, Binding fourth | Binding last, with its own message, on dev; BU's reviewer reviews it; bu77 (G300) rebases on it (BU's option (a)): bu77 reads none of the deleted members, its overlaps are textual (a context line beside `ResetCallContexts`; `TyCtxt.Instances.cs`' header, which bu77 rebases onto the `bool` wording) | coordinator, BU |
| Holds | §5.1 | Backend/Codegen is R5's; never `Mir/Analysis`; the old Analysis only as §3's deletion; Binding only in BU's commit | coordinator |
| As implemented | — | c1 `af5c1cb7b` (re-points; Kimi +9, Benchmark +2; every plan workload and the lifecycle generate, so no row is deleted), c2 `d1d2e40d0` (Emission −13,206, Core −3, Testing −20), c3 `dfeaf73ea` (Analysis −187; diagnostic snapshot 0 differences), c4 `84cc33d68` (Backend −2), c5 `10893867a` records (DECISIONS R5-M162 and six amendment lines; ARCH, CODEMAP, DIAGNOSTICS, STATUS), c6 `fd71e7e34` Binding (BU; Binding −83, root helpers −12, tests −5). Evidence: base Session `20261030-062743-886`, candidate Session `20261030-074511-258-session-g7-cand` (verify-commit: inputs match `fd71e7e34`), test-emission Release 68 native executions (after copying the worktree's ignored `artifacts/backend/windows-x64/` from the main checkout), the 14 Benchmark commands' output in the main checkout's `artifacts/benchmarks/r6-g7-bench/` | Measured by `git diff --numstat` |
| Freed members left (R5's review) | c3: "the re-scan frees no further Analysis member" | Wrong: the re-scan found only members whose names no longer occur outside their declarations. The old analysis still writes plans that only the deleted generator read: `OwnershipModel.cs:238-255, :302` (`SlotResults`, `ResultArrivals`, `ResultWrites`, `Deliveries`, `StringComparisons`, `CallLoans`, `IsConcrete`) and the unread fields at `:175-179`, `:557`, `:559`. They go to G8's list under Q5's rule (deletion where only the old generator read them; PLAN is the planning session's). STATUS.md:86 and the stale comments (`Binding.ComparisonPlans.cs:23`'s Transitional reason naming G7, `src/Benchmark/O2Corpus.md:51`, the Backend's "both generators" comments) are staged to G8 or G11 | Planning session |
| CODEMAP | drop the stems | 47 table cells lose their deleted stems by rule (an emptied cell is "—", the table's convention); the Lowering-and-layout row becomes "Old physical IR layer (until G8)"; two cells' shorthand stems of `BodyLowering` partials go with it | Auto |

"Plan" is the [R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` =
`src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`, `B/` = `src/Benchmark/Benchmarks/`. Line numbers are at `1f2d7dea6`. No build,
test or probe was run: reach comes from word-boundary greps over `src` (with `src/Benchmark`) and `tests`, two identifier-count scans
(scratch only: a member declared outside the deletion set whose name occurs nowhere outside it but its declaration), and a check for
`using static`, extension members, `typeof` and `nameof` (none). **(H)** marks an unchecked claim. Commit messages say "R6-gen G7"
(PLAN's issue G7 is unrelated).

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| G7-1 | No product path runs the old generator since G4 (R5-M160): `EmissionArtifacts.Publish`, `Project.Emit`, `kimi build` and `TestCommand` reach only `Compilation.Generate`. The one product reach left is `Project.PublishArtifacts`' publication-failure record through `LlvmEmitter.ReportFailure` (`src/Kimi/SolutionAndProject/Project.cs:272`). It moves to `Compilation.ReportPublicationFailure`, a `[Transitional("R6-gen")]` sibling of the G9b adapters, with the same record | 1 |
| G7-2 | The two `src/Benchmark` readers are re-pointed to `Compilation.Analyze()` and `Compilation.WriteIr`, as R5-M94 did for the pipeline benchmark. The split-IR check, which compares `WriteIr` with itself since G4, is deleted | 1 |
| G7-3 | G7 deletes the row's 47 files (13,202 lines), and every member outside them whose callers, readers and writers all lie in them. Exceptions: members of the files G8 deletes whole, and Binding members G9 owns (two added to G9's list). `StaticScalarEntry` moves to G8: its readers are `EmissionModule.Statics` and `LlvmModuleWriter.Statics.cs` | 2 |
| G7-4 | The old Analysis's instance half is what only `AnalyzeInstance` reaches: the instance state, `Resolve`'s instance branch, the `this.instance` guards, `OwnershipModel.Instance`, and the reporting overload's three instance parameters. The Semantics-case machinery in the same file stays until R6-check (SPEC 8.15). So do ~120 lines of `OwnershipAnalysis.Instances.cs`, which K-design-2 counted whole (315). The old generator's read-only views of the old analysis go too (deletion, D5) | 3 |
| G7-5 | The `Binding.ClosureCall` bridge goes with its two readers. `ResetCallContexts` is inlined, as R5-M36 says. `TyCtxt.InstantiatesInLegacy` and `InstanceClass.GenericThroughSpecialization` go (R5-M34), and the class becomes a `bool` memo. One BU-reviewed commit, under holds on `Binding.ClosureInstances.cs` and `TyCtxt.Instances.cs` | 4, 5 |
| G7-6 | Six commits: product re-points, Emission, Analysis, Backend, records and, last, Binding (BU's review). Each is behavior-preserving. One Session at the end with `'*.ll'`, compared with a base run in the same worktree (the plan row). It also checks both snapshots and builds the solution | 6 |

## 1. What stays and why: every caller of the old Emission after G6

The old generator's entry is the `Compilation.Emission` property (`C/Core/Compilation.cs:117-118`, `new LlvmEmitter(this)`). Its callers:

| Caller | Path today | At G7 |
| --- | --- | --- |
| `EmissionArtifacts.Publish` (`C/Emission/EmissionArtifacts.cs:17-90`) | `compilation.Generate(writer)` (`:65`) since C0a | Nothing to re-point. The file stays (G8 keeps it in `C/Emission` or moves it with G11's CODEMAP) |
| `Project.Emit` (`Project.cs:575-583`), `kimi build` (`EmitCommand.cs:27` → `Solution.Generate` → `Project.Generate` `:245` → `BuildCore` → `Emit`) | `PublishArtifacts` (`:263-277`) → `EmissionArtifacts.Publish` | Generation is already MIR's. **One reach left:** `compilation.Emission.ReportFailure(failure)` (`:272`) for a publication failure: settings, native supplies, backend hash or I/O (`EmissionArtifacts.cs:84-88`) |
| `TestCommand` (`src/Kimi/Unit/Command/TestCommand.cs:131`) | `project.PublishArtifacts` | Same single reach |
| LSP, `kimi check` | `Project.Check` (`:252`); no generation (`Lsp/WorkspaceCheck.cs` reads `WindowsProfile` only) | None |
| `Compilation.WriteIr`/`Validate` (`C/Core/Compilation.Generation.cs:55-67`) | `Generate` (MIR) since G4 | None; they stay until G9b |
| `B/CompilerPlanMeasurements.cs:116` (the "emission" phase; set-up `:67`, re-check `:74`) | `c.Emission.WriteIr` after `c.Ownership.Analyze()` | **Re-point** (G7-2) |
| `B/InheritanceLifecycleMeasurements.cs:130-131` (`Pipeline`) | `c.Ownership.Analyze()` then `c.Emission.WriteIr` | **Re-point** (G7-2) |
| `B/CompilerPipelineMeasurements.cs:106-110` (the split-IR check) | Stage 6 is `Compilation.WriteIr` (R5-M94, R5-M160), so it compares `WriteIr` with `WriteIr` on one compilation | **Delete** `:106-109`. Keep the non-empty IR test inside `:113`'s instrumentation comparison. `:119`'s "IR equivalence and accounting" becomes "instrumentation and accounting". `CompilerPipeline.md:116-117` follows (Auto: the documented check changes). Stage names stay (R5-M94, R5-M97) |
| Tests | None: no test reads `Compilation.Emission` since G5 (R5-M161; grep `\.Emission\b` in `tests` finds only `ArchitectureRulesTest`'s patterns) | None |

**`ReportFailure`'s branch today.** `LlvmEmitter.cs:69-82`: it invalidates the Emission partition, then reports retained instance
diagnostics if `Ownership.FailedInstance` is set, else `GenerationFailed_Kd` (or `GenerationResourceLimit_Kd` if `resourceLimit`) at
`Project.FilePath` with the failure text as note. Since G4 no product compilation runs `TryPrepare`, so `FailedInstance` is null and
`resourceLimit` false at every product call (H: nothing else sets them). The record is therefore always `Invalidate(Emission)` plus
`GenerationFailed_Kd` with the note.

G7-1 moves exactly that into `C/Core/Compilation.Generation.cs`:
`[Transitional("R6-gen", Reason = "G9b replaces GenerationFailed_Kd for publication failures.")] internal void ReportPublicationFailure(string failure)`.
`Project.cs:272` calls it. The Emission partition's writers then live in the generation entry's file, beside the other G9b
adapters (`:55`, `:66`, `:184`, `:189`). Rejected:
- two inline statements in `Project` (an unmarked temporary record site in product code);
- keeping `LlvmEmitter` for one method;
- doing G9b's successor code now (G9's design and a DIAGNOSTICS §4.2 row).

Covered by `T/EmissionArtifactsTest.cs:258-262` (O3 fails, O2 publishes), `:62`, `:82`, `:97` and `:328` (H: whether one of them
asserts the record's code), and by the CLI harness `test-cli`.

**Benchmark re-points (G7-2).**
- `CompilerPlanMeasurements`: `:67` and `:74` change `c.Ownership.Analyze()` to `c.Analyze()`. The generation gate needs the
  program `Analyze` keeps: `Generate` reports `InternalInvariant_Kd` without `this.mir` (`Compilation.Generation.cs:97-101`), as
  R5-M94 found for stage 4. `:116` becomes `c.WriteIr(TextWriter.Null, out _)`. The "ownership" phase (`:117`) keeps timing the old
  analysis.
- `InheritanceLifecycleMeasurements.Pipeline`: `c.Analyze()` and `c.WriteIr`.
- The phase names stay, so the md files that describe "Binding, ownership and emission" phases (`ObjectPlans.md`, `Properties.md`,
  `Inheritance.md`, …) stay valid. Their recorded numbers are dated evidence of the old generator.
- Rejected: deleting the emission phase and the lifecycle's emit check. It would edit 10 md files, and the warm MIR generation per
  workload would lose its only instrument.

**Invariant after G7.** No `LlvmEmitter` exists. Every generation request and every Benchmark measurement generates through
`Compilation.Generate`. `C/Emission` holds only G8's physical layer:
- `EmissionModule`, `LlvmModuleWriter*`, `AggregateLayout`, `FunctionAbiPool`, `WindowsLowering` and `StaticScalarEntry`
  (G7-3), with no product reader and readers only in tests (`T/LayoutTest`, `KimiLibraryShapeTest`, `AggregateEmissionTest`,
  `ObjectHandleModeTest`, `DictionaryLibraryTest`, `AbiTest`, `FunctionEmissionTest`, `AggregateFunctionEmissionTest`,
  `ReferenceEmissionTest`, `MinimalEmissionTest:21-53`);
- the toolchain and publication files `NativeToolchain*`, `ToolchainResolver`, `Kernel32Imports`, `WindowsProfile`, `ArtifactFiles`,
  `ArtifactPaths`, `EmissionArtifacts` and the `.ll.in` resources, which stay.

## 2. Deletion census

### 2.1 The plan row's files (`C/Emission/`, 47 files, 13,202 lines)

| Files | Lines | Reach after c1 (callers outside the set) | Fail(" |
| --- | ---: | --- | ---: |
| `BodyLowering*.cs` (37 partials, entry `BodyLowering.cs`) | 10,453 | Only `LlvmEmitter` constructs it (`LlvmEmitter.cs:17, :35`). Outside the set: comments only (`Binding.ClosureInstances.cs:20`, `KimiLibraryShapeTest.cs:22, :458`) | 426 |
| `LlvmEmitter.cs`, `LlvmEmitter.Defaults.cs` | 574 + 48 | `Compilation.Emission` (`Compilation.cs:118`) and, before c1, `Project.cs:272` and the two Benchmark readers | 0 |
| `GenericStoragePlan.cs`, `.Comparisons.cs`, `.Destructors.cs` | 614 + 197 + 90 | `LlvmEmitter`, `BodyLowering`. **Outside:** `AggregateLayout.cs:324` (`IsGeneric`, a compile cut, §2.2); comments in `Binding.ClosureInstances.cs:19`, `TyCtxt.Instances.cs:51` | 20 |
| `ObjectGenerationPlan.cs` | 340 | `LlvmEmitter` | 0 |
| `VirtualGenerationPlan.cs` | 399 | `LlvmEmitter` | 0 |
| `DefaultGenerationPlan.cs` | 173 | `LlvmEmitter` | 0 |
| `CompilerFunctionAdapters.cs` | 250 | `LlvmEmitter` | 0 |
| `NumericArithmetic.cs` | 64 | `BodyLowering` | 0 |
| Total | **13,202** | | **446**, the ratchet's whole count (38 files; §5.3) |

`StaticScalarEntry.cs` (21) is the row's tenth item. Its producer is `LlvmEmitter.cs:154-176`, but its readers are G8's
`EmissionModule.Statics` (`EmissionModule.cs:262, :284`) and `LlvmModuleWriter.Statics.cs`. Its own contract is "closed physical
storage and initialization ABI". So it stays for G8 (Q1). Nested types of the set (`ConversionPlan`, `ObjectCall`, `CallEntry`,
`Template`, `FormattingEstimate`, …) have no reader outside it.

### 2.2 Forced cuts in files G7 keeps

| Site | Change | Lines |
| --- | --- | ---: |
| `C/Core/Compilation.cs:117-118` | the `Emission` property | −3 |
| `C/Core/Compilation.Generation.cs:25-27` | the header's "Outside the gate until G7 deletes LlvmEmitter …" sentence; plus `ReportPublicationFailure` (G7-1) | +8 / −4 |
| `src/Kimi/SolutionAndProject/Project.cs:270-273` | call `ReportPublicationFailure` | 0 |
| `C/Emission/AggregateLayout.cs:75-76, :324-325, :365, :374, :398` | `GenericStoragePlan.IsGeneric(body)` no longer compiles. The hook `InstantiateDestructor` goes with its one producer, `GenericStoragePlan.RequireDestructor` (wired at `LlvmEmitter.cs:39`), and the `genericDestructor` local with it. Unset, the hook already gives null to every pool G8's tests build, so behavior is unchanged. The record's `GenericDestructor` field and its writer (`LlvmModuleWriter.Aggregates.cs:205`) stay for G8's destruction half | ≈ −5 |
| `src/Kimi/Testing/TestRuntime.cs:119-136` | `Create`, `[Transitional("R6-gen")]` "its one caller" (`LlvmEmitter.cs:256`) | −19 |
| `B/CompilerPlanMeasurements.cs`, `B/InheritanceLifecycleMeasurements.cs`, `B/CompilerPipelineMeasurements.cs` | G7-2 | ≈ −4 |

### 2.3 Members freed outside the row (rule G7-3)

G7 deletes every member outside §2.1 whose every caller, reader and writer lies in §2.1 or among G7's other deletions, with two
exceptions. Members of a file G8 deletes whole stay for G8, as does one reached through an object G8 deletes: `EmissionModule`'s
`RuntimeHelpers` instance, `AggregateLayoutPool`, `FunctionAbiPool`. Binding members on G9's list stay for G9.

| Member | Site | Last caller (deleted) | Unit |
| --- | --- | --- | --- |
| Instance half of the old Analysis | §3 | `LlvmEmitter.cs:74, :96, :292, :302, :380`, `LlvmEmitter.Defaults.cs:15` | G7 |
| Generator views of the old Analysis: `UsesImport` with `usedImports` (`OwnershipAnalysis.cs:17, :136, :151, :1563`); `OwnershipBody.GetBorrowInputState` (`OwnershipBody.Borrows.cs:72-82`), `TrySymbolPlaceAt` (`OwnershipBody.Defaults.cs:238-239`), `GetElementState` (`OwnershipBody.MovePaths.cs:36-45`), `IsPathWithin` (`:68-79`); `OwnershipBody.ParameterCount`, `DeclaredResultType` (`OwnershipModel.cs:306, :308`) | Analysis | `LlvmEmitter.cs:327`, `BodyLowering*` | G7 (§3.2) |
| `Binding.ClosureCall`, `closureCalls`, `ResetCallContexts` | §4 | `GenericStoragePlan.cs:316`, `BodyLowering.Closures.cs:85` | G7 |
| `TyCtxt.InstantiatesInLegacy`, `InstanceClass.GenericThroughSpecialization` | `C/Binding/TyCtxt.Instances.cs:38-53, :112-131` | `GenericStoragePlan.cs:68-69` | G7 |
| `Binding.ReportEffectViolation`'s `instanceContext`, `instanceSite` (`C/Binding/Binding.EffectViolations.cs:67-71, :91-97, :111-114`) | Binding | only `ReportDiagnostics`' instance path (§3) | G7 |
| `StartupResult.Function` and `CheckStartup`'s `function` local (`C/Binding/Binding.Startup.cs:26, :28, :93, :112, :117, :129`) | Binding | `LlvmEmitter.cs:265, :462` ("goes at G7", `:26`) | G7 |
| `ElementAccess.ReceiverMatches` (`C/ElementAccess.cs:262-269`), `ScalarTypes.IsCharacterValue` (`C/ScalarTypes.cs:18-19`) | root helpers | `BodyLowering*` | G7 (BU reviews) |
| `AbiParameterKind.PreparedSlot` (`C/Backend/AbiRecords.cs:29`; comment `Abi.cs:20`) | Backend | produced by `DefaultGenerationPlan.cs:149`, read by `BodyLowering.Defaults.cs:43` | G7 |
| `AdtDef.IsOrInherits` (`C/Binding/AdtDef.cs:126-138`), `KimiLibraryCatalog.IsArrayOperation` (`KimiLibraryCatalog.cs:29`, a hold file) | Binding | `BodyLowering*` | **G9** (added to its list) |
| `Binding.IsInapplicableVirtualBody`, `ImplementationContext`, `IsVerifiedSpecialization` (already on G9's list); `PrepareTypeStorage`, `SameModuloOpenOrigins` and `DictionaryStorageCall` keep other Binding callers (G9's greps) | Binding | `LlvmEmitter`, plans | G9 |
| `LegacyRuntimeHelpers.RequestFormattingWrite`, `RequestFormattingConversion`, their lists and loops (`Backend/LegacyRuntimeHelpers.Formatting.cs:103-105, :227-249, :260-268`; `LegacyRuntimeHelpers.cs:404-405`) | Backend | `BodyLowering*`; `WriteFormatting`'s one caller is `LlvmModuleWriter.cs:38` | **G8** (the old module's helper instance) |
| `AggregateLayoutPool.IsSealedObjectTarget`, `CollectionDrop`, `UsedCollectionFields`, `ClearDestructors`, `GetStored`, `StorageOffset`; `EmissionModule.AddCall`, `AddFunction`, `RemoveLastFunction`, `TestRuntime` field; `WindowsLowering.GetCompilerFunction` | G8 files | LlvmEmitter, BodyLowering | **G8** (`RegisterDestructor` keeps `KimiLibraryShapeTest.cs:676`) |

The scans are first-order. Removing a view can free its callees (`TryOwnedBorrowState` and `CompleteState` from
`GetBorrowInputState`, H). After c3 and c6 the implementer re-runs the scan and deletes what it newly finds in the same area.

## 3. The old Analysis's instance half

**Rule.** D5 keeps the old Analysis until R6-check and allows deletion. `check` never runs `AnalyzeInstance` (R5-M88). Its callers
are `LlvmEmitter.cs:380` and `LlvmEmitter.Defaults.cs:15`. So code reached only while `this.instance` is non-null is unreachable once
they go, and code read only by the old generator is unread. Both go. Code that a Semantics case run reaches stays: `Substituting`
(`OwnershipAnalysis.Instances.cs:27`) is "an instance **or a case run**" (SPEC 8.15). That code is the old analysis's own.

**How it was shown.** Every write of `this.instance`, `instanceBody`, `instanceFailed`, `FailedInstance` and `InstanceStorageLimit`
lies in `AnalyzeInstance` (`:65-67`, `:74`, `:92-93`) or `ClearInstances`. The one exception is `OwnershipAnalysis.cs:480`, guarded
by `this.instance is not null`. `OwnershipModel.Instance` is set only from `Reset`'s `instance` argument, which is
`OwnershipAnalysis.cs:513`'s `this.instance` (one caller). Every reader was grepped (`this\.instance\b`, `\.Instance\b`,
`InstanceBinding`, `Substituting`, the property names). `requirementOverride`, `instanceContext` and `instanceSite` are passed only by
`ReportInstanceDiagnostics` (`:133-134`). Its one caller is `LlvmEmitter.cs:76`.

### 3.1 Instance-only: deleted

| Site | Member or branch | After G7 | Lines |
| --- | --- | --- | ---: |
| `C/Analysis/OwnershipAnalysis.Instances.cs:11-15` | `instancePool`, `instanceCount`, `instanceBody`, `instance`, `instanceFailed` | gone | −5 |
| `:22`, `:24` | `InstanceStorageLimit`, `FailedInstance` | gone | −3 |
| `:27` | `Substituting` | `this.caseCount != 0` | 0 |
| `:29-95` | `ClearInstances`, `AnalyzeInstance` | gone | −67 |
| `:303-314` | `Resolve`'s instance branch (`InstantiateStorageType(type, call)`, `instanceFailed`) | the case branch only | −10 |
| `:8` | file summary "Per-substitution plans …" | "Semantics-case substitution (SPEC 8.15)" | 0 |
| `C/Analysis/OwnershipAnalysis.cs:133-134` | `ReportInstanceDiagnostics` | gone | −3 |
| `:142` | `Invalidate`'s `ClearInstances()` | gone | −1 |
| `:186-418` | `ReportDiagnostics`' `requirementOverride`, `instanceContext`, `instanceSite`: `:191-194` (Invalidate becomes unconditional), `:264` (two arguments), `:307`, the local functions `Requirement` `:373`, `Condition` `:375`, `Note` `:407`, `Locations` `:409-419`, and their 25 call arguments (`:200-356`) | `DiagnosticRequirement.Ownership(issue.Failure)`, no condition, the note and related locations themselves (H: `Report`'s `condition` defaults to 0) | ≈ −25 |
| `:450` | `Build`'s `this.instance is null &&` | `this.ResolveCases(function)` | 0 |
| `:478-481` | `BuildOnce`'s `InstanceStorageLimit` | gone | −4 |
| `:491`, `:493-496` | `BuildBody`'s `instanceBody` arms | case and default arms only | −4 |
| `:513` | `Reset(function, this.instance, …)` | without the argument | 0 |
| `OwnershipAnalysis.Borrows.cs:563`, `OwnershipAnalysis.Pointers.cs:15` | `this.instance is null &&` (always true) | dropped | 0 |
| `C/Analysis/OwnershipModel.cs:317-318, :341, :347, :356` | `OwnershipBody.Instance`; `Reset`'s `instance` parameter | gone. `InstanceBinding` stays: it carries the case run's `Binding` (`:356`, `:459`) | −4 |
| `:455-459` | `Concrete`'s instance arm | the case arm | −1 |

### 3.2 Generator views: deleted

The §2.3 views (`UsesImport` and its set, `GetBorrowInputState`, `TrySymbolPlaceAt`, `GetElementState`, `IsPathWithin`,
`ParameterCount`, `DeclaredResultType`), about −45. They are read-only accessors over the analysis's own state. Only `usedImports`
is written, at `OwnershipAnalysis.cs:1563`, after `CollectLibraryBody`, which stays, and nothing else reads it. Deletion changes no
verdict (Q5).

### 3.3 Stays until R6-check (the old analysis's own)

- `OwnershipAnalysis.Instances.cs:17-20` (`cases`, `caseCount`) and `:97-291`: `FollowsReference`, `PairLayerExists`,
  `ReferenceLayers`, `ReadsStoredReference`, `ImplicitlyFollowsReference`, `StoredReference`, `BorrowStoredReference`,
  `BorrowThrough`, `ThroughPairLayers`, `SelectedPlace`. Each serves case runs through `Substituting`, or every run.
- `Resolve`'s case and default stages.
- `InstanceBinding`.
- `OwnershipRequirement.Internal`, still reported at `:116`.
- `TemplateBody` (`OwnershipAnalysis.EffectBounds.cs:18`): also read by Hover (`HoverBuilder.Virtual.cs:75`) and Binding
  (`Binding.EffectBounds.cs:110, :967`, `Binding.VirtualEffects.cs:41`).
- The call-site uses of `Binding.InstantiateStorageType(type, call)`: `OwnershipAnalysis.Defaults.cs:149`,
  `OwnershipBody.CleanupEffects.cs:60`, `OwnershipBody.Defaults.cs:290`. These are default calls and cleanup effects of the
  definition run, not instances. `TyCtxt.StorageSubst` stays until R7 (R5-M36).

The file keeps its name, since the old Analysis is frozen and R6-check deletes it. Net for the Analysis: **≈ −170** (§3.1 ≈ −125,
§3.2 ≈ −45; H). That is against K-design-2 §441's "`OwnershipAnalysis.Instances` (315)": its ~190 lines of pair-layer helpers stay.

## 4. The `Binding.ClosureCall` bridge (BU's files)

**What it bridges.** R5-M36 made a closure context a `Subst` (`BoundType.ClosureContext`). The old generator still keys a closure's
instance by a `CallPlan`. `ClosureCall` (`C/Binding/Binding.ClosureInstances.cs:19-48`) builds one synthetic `CallPlan` per (closure
symbol, context), memoized in `closureCalls` (`:9-11`). The memo is kept across unchanged passes and cleared with the function-item
contexts by `ResetCallContexts` (`:112-118`, called at `Binding.cs:190`). Both are `[Transitional("R6-gen")]` "G7 deletes the old
generator, the bridge's one reader".

**Callers.**
- Product: `GenericStoragePlan.cs:316` (`PrepareClosures`) and `BodyLowering.Closures.cs:85` (value callees), both §2.1.
- Test: `T/GenericClosureIdentityTest.cs:57-58`, two asserts in `TwoClosuresOfOneInstanceKeepTheirOwnBodies`: the bridge keeps one
  call per closure and the same one at every request. They test the bridge's own contract (R5-M36's X10 for the old generator).
- The rule the test stands for stays at MIR's layer: the asserts beside them (`:55-56`, one context shared by the instance's
  closures), the fixture `GenericClosureIdentityTwo`, where each instance runs each closure's own body (native, MIR code
  generation), and the collector's items, one per closure (H: an `InstanceCollectorTest` row).

**The deletion (c6).**
- `closureCalls`, `ClosureCall` and `ResetCallContexts` go. `Binding.cs:190` becomes `this.functionItemContexts.Clear();`, with
  R5-M36's "G7 inlines it".
- `ClosureSignature`, `CloseClosureTypes`, `ClosePart` and `ClosePartCore` stay (MIR's). So do
  `ArchitectureRulesTest`'s `SelfRebuildSites` and `RawOriginRoleSites` entries for the file (`:14`, `:18`, both from
  `ClosePartCore:103`).
- The comments at `:112-113` and `GenericClosureIdentityTest.cs:44-45` drop the old generator.
- In the same commit (BU's area):
  - `TyCtxt.Instances.cs`: `InstanceClass` becomes the memo's `bool` (`Walk` loses `specialized` at `:115`, `:120`, `:128`), and
    the header's "Temporary (R6-gen …)" (`:19-20`) and `:10` go.
  - `T/TyCtxtTest.cs:1079-1081` drop the legacy view's assertion, and `:1059-1063`'s comment its last sentence.
  - `T/GenericClosureDispatchTest.cs:18-21`: `TheOldGeneratorKeepsAClosureInAContainerSpecializationClosed`, which asserts
    `Instantiates` and MIR generation, is renamed for its rule (H: `AClosureInAContainerSpecializationIsGenericInTheContainersSlot`).
  - `EffectViolations` and `Startup` (§2.3).
  - `ElementAccess`, `ScalarTypes`.
- Size ≈ −80 in Binding, ≈ −8 in the root helpers, ≈ −5 in tests.

**BU's review.** The hold files are `Binding.ClosureInstances.cs` and `TyCtxt.Instances.cs` (implementer brief: `Binding/TyCtxt*.cs`).
BU reviews c6 as a hunk owner. PLAN G300's removal condition (R5-M157) is a BU fact. It changes `ClosePartCore` and
`EnclosingFunction` in the same two files, so whichever lands second merges. G7 touches neither function.

## 5. Shared files, holds, declarations and ratchets

### 5.1 Holds (PLAN §3 lines by the planning session)

| File(s) | Owner | G7's line | Known overlap |
| --- | --- | --- | --- |
| `C/Binding/Binding.ClosureInstances.cs`, `C/Binding/TyCtxt.Instances.cs` | BU (R5-M7 hold protocol) | **hold**, c6 only; BU reviews | G300's BU fact (§4) |
| `C/Binding/Binding.cs`, `Binding.EffectViolations.cs`, `Binding.Startup.cs`, `C/ElementAccess.cs`, `C/ScalarTypes.cs` | BU | advisory | `claude/bu80-g296r`, G216 (a)'s `-df`/`-pf` edit `Binding.cs` elsewhere |
| `C/Analysis/OwnershipAnalysis*.cs`, `OwnershipBody*.cs`, `OwnershipModel.cs` | frozen old Analysis (R4 and BU D5 exceptions) | advisory ("adv while R4 is active") | G216 (a) `-df` edits `OwnershipAnalysis.cs:251-252` (inside `ReportDiagnostics`, beside §3.1's edits) and `OwnershipAnalysis.Borrows.cs:131`. Held for a redesign with BU (PLAN `7968c35f6`), so later |
| `C/Emission/BodyLowering.Closures.cs` | G7 | — | G216 (a) `-pf` changes two call arguments there (`:131-149`): modify/delete, the deletion wins |
| `C/Backend/AbiRecords.cs`, `Abi.cs` | R5 | own | — |
| `C/Core/Compilation.cs`, `Compilation.Generation.cs`, `Project.cs`, `src/Kimi/Testing/TestRuntime.cs`, `src/Benchmark/**` | R6-gen / R5 | own | — |

G7 touches no `Mir/**`, `Backend/Codegen/**`, `tests/mir` golden or `MirDifferenceLedger.cs` (H: confirmed by the census's file list).

### 5.2 Declarations, vocabulary, ARCH

- Transitional declarations removed: `Binding.ClosureInstances.cs:10`, `:22`; `TyCtxt.Instances.cs:43`, `:52`; `TestRuntime.cs:123`.
  Added: `Compilation.ReportPublicationFailure` (R6-gen, G9b). The `src` count of `[Transitional("R6-gen"` goes from 9 to 5: the four
  G9b adapters and the new one.
- ARCH's Transitional list:
  - drops "the `Binding.ClosureCall` bridge (R6-gen)" and "the old generator's specialization view `TyCtxt.InstantiatesInLegacy` and
    the class `InstanceClass.GenericThroughSpecialization` it reads (G7, …)" (`COMPILER_ARCHITECTURE.md:265-267`);
  - the out-string adapters' item adds "and the publication failure's record (G7 to G9b)".
- ARCH's freeze (`:216-223`): BU-24's two Emission clauses go, since the code they admit is deleted (`HasInferenceOrigin` in
  `BodyLowering.Values.cs`; the Dictionary checks in `BodyLowering.DictionaryStorage`/`.Dictionaries`). The freeze's Emission scope
  itself ends at G11 with the stage ledger (plan row G11).
- No vocabulary entry changes. `EmissionOpcode` (49) goes at G8. `CompilerFunctionKind` keeps its Binding and Backend readers.
  `AbiParameterKind` is no closed vocabulary.
- Metrics (ARCH R6-gen progress, plan §5.1): M4, Emission `.cs` lines, falls from 18,284 to ≈ 5,075 (Backend ≈ 14,757). M8's
  Emission-failure-string ratchet goes from 446 to 0. G7's entry records both.

### 5.3 Ratchets (`T/ArchitectureRulesTest.cs`)

| Row | Now | At G7 |
| --- | ---: | --- |
| `:39` Emission failure strings (`\bFail\(\$?"` in `C/Emission`) | 446 | **0**. Every occurrence is in §2.1 (38 files). G9's "446 → 0" is done here |
| `:55` Test calls of the old analysis and generation entry points | 0 | 0. Its `.Emission.` alternatives name a deleted property; they stay until G10 folds the row (Q3) |
| `:60` prepared emission module, `:61` instance analysis, `:62` generation failure facts | 0 | 0. Their members are deleted, so the rows can never count again. Kept until G10's zero-reference rows replace them (Q3); R5-M161's "stay at limit 0 until G7" is read as "until the stage that deletes the rows" |
| `:58` `Ownership.Bodies` | 30 | 30 (G7 adds no test read) |
| `SourcePatternStaysAtItsSites` | — | unchanged (§4) |

## 6. Commits, checks, size, risks and open questions

### 6.1 Start condition

Branch from the dev head after G5/G6 (`1f2d7dea6` or newer). First run the bp base in the G7 worktree: `verify.ps1 -Mode Session
-DiagnosticSnapshot -Fixtures '*.ll'` with the milestones and the five CLI harnesses, plus the semantic snapshot baseline (HIR and
MIR, VERIFICATION "Behavior-preserving commits"). Keep the evidence. Then re-run this census's greps at that head and the hold
check of §5.1.

### 6.2 Commits (each behavior-preserving; Unit runs per commit, one Session at the end)

| # | Commit | Content | Unit checks |
| --- | --- | --- | --- |
| c1 | product and Benchmark re-points | G7-1 (`ReportPublicationFailure`, `Project.cs:272`), G7-2 (the two readers, the split-IR check, `CompilerPipeline.md:116-117`). After c1, only `Compilation.Emission` itself reaches `LlvmEmitter` | `EmissionArtifactsTest`, `LibraryEmissionTest`, `PipelineSwitchTest`; slnx build; `Benchmark --compiler-pipeline-check`, `--collection-cost-check`; one run each of `--property-plans` and `--inheritance-lifecycle` (H: run time) |
| c2 | the Emission driver, lowering and plans | §2.1's 47 files; `Compilation.Emission`; `AggregateLayout`'s cut; `TestRuntime.Create`; ratchet `:39` 446 → 0; `Compilation.Generation.cs`' header | `ArchitectureRulesTest`, `LayoutTest`, `AggregateEmissionTest`, `ObjectHandleModeTest`, `DictionaryLibraryTest`, `MinimalEmissionTest`, `KimiLibraryShapeTest`, `GenericClosureDispatchTest`, the test-build classes (`TestExecutionAnalysisTest`, `ProductTestMembershipTest`) |
| c3 | the old Analysis's instance half and views | §3.1, §3.2 | `GenericOwnershipCaseTest`, `PairFollowTest`, `OwnershipStorageLimitTest`, `GenericGenerationDiagnosticTest`, `DiagnosticSnapshotTest`, `SemanticSnapshotTest` (check), Borrowing*, `BlockEscapeLoanTest` |
| c4 | Backend leftover | `AbiParameterKind.PreparedSlot`, `Abi.cs:20` | `AbiTest`, `FunctionCodegenTest` |
| c5 | records | DECISIONS R5-M162 (§7) and the other amendment lines; ARCH §5.2; CODEMAP; DIAGNOSTICS `:59`'s second sentence; STATUS (§6.4 R6) | Doc |
| c6 | Binding: the bridge, the legacy view and the instance-only parameters (BU review) | §4, §2.3's Binding and root rows; ARCH's two Transitional-list items (§5.2) and the R5-M34, R5-M36 amendment lines | `GenericClosureIdentityTest`, `TyCtxtTest`, `GenericClosureDispatchTest`, `VirtualEffectObligationTest`, EffectBound*, startup classes (`BindingTest` startup rows, `MixedStartup*`; H), semantic snapshot with HIR and MIR |
| — | Session on c6's head | `-Mode Session -DiagnosticSnapshot -DiagnosticBaseline <base>/diagnostic-snapshot.json -Fixtures '*.ll'`, milestones, `test-library`, `test-emission`, `test-cli`, `test-modules`, `test-testing`; the semantic snapshot `check` (HIR, MIR); `compare-fixtures.ps1 -Baseline <base> -Candidate <run>`; the non-incremental `-warnaserror` build (StyleCop after deletions) | the proof below |

DECISIONS and ARCH are in the input manifest, so the Session runs after c6, and one Session covers c1-c6 (AGENTS). The R4 MIR ledger
is not needed: no `Mir/**` or MIR-verdict change ("Ledger before review" applies to A5-verdict changes).

**Why no restructuring commit.** G7 adds no row and no function besides the moved `ReportPublicationFailure`. Each commit only
deletes code or moves a call. The proof:
- **Diagnostic snapshot** equal to the base. No check ran the instance half or the bridge.
- **Semantic snapshot** equal with HIR and MIR. Binding's published facts are unchanged: the bridge was memo-only, `Instantiates`
  is unchanged, and `StartupResult.Function` was never published.
- **`'*.ll'`:** `compare-fixtures` reports every `.ll`, sidecar and native result identical, none missing, none added.
- **Ratchets** only fall.
- **Zero-grep** over `src`, `tests` and `src/Benchmark` code (G8's files' comments excepted): `LlvmEmitter`, `BodyLowering`,
  `GenericStoragePlan`, `ObjectGenerationPlan`, `VirtualGenerationPlan`, `DefaultGenerationPlan`, `CompilerFunctionAdapters`,
  `NumericArithmetic`, `AnalyzeInstance`, `FailedInstance`, `InstanceStorageLimit`, `ClearInstances`, `ReportInstanceDiagnostics`,
  `ClosureCall`, `InstantiatesInLegacy`, `GenericThroughSpecialization`, `\.Emission\.` (outside `ArchitectureRulesTest`'s
  patterns).

### 6.3 Expected net size (H)

| Area | Net |
| --- | ---: |
| `C/Emission` | −13,202 (§2.1), −5 (`AggregateLayout`) |
| `C/Analysis` | ≈ −170 |
| `C/Binding` and root helpers | ≈ −90 |
| `C/Core`, `SolutionAndProject`, `Testing` | ≈ −18 |
| `C/Backend` | −2 |
| `src/Benchmark` (code and md) | ≈ −5 |
| `tests/xUnitTest` | ≈ −5 |
| docs (CODEMAP's 51 rows lose 108 references to deleted stems; ARCH, DIAGNOSTICS, STATUS) | ≈ −15; DECISIONS ≈ +40 |
| **Total** | **≈ −13,470**, against the plan's ≈ −13,900: the pair-layer helpers stay (§3.3, ≈ −190), `StaticScalarEntry` moves to G8 (−21), and the Benchmark readers are re-pointed, not deleted |

### 6.4 Risks

| Risk | Response |
| --- | --- |
| R1 A reader the greps missed (reflection, a string) | None found: no `typeof`/`nameof` of a deleted type outside the set. `MinimalEmissionTest:21-53` walks `EmissionModule`'s fields only, and `StaticScalarEntry` stays. The c2 build is the final check |
| R2 §3.1 changes a verdict | The guards were constant on every check run (R5-M88). `ReportDiagnostics`' 25 argument edits are the risky part: the diagnostic snapshot and `DiagnosticSnapshotTest` show them. A wrong default for `condition` would show as a record difference |
| R3 Merge with G216 (a), bu80 or G300's BU unit | §5.1. A modify/delete keeps the deletion; `OwnershipAnalysis.cs:251-252` is a two-line rebase |
| R4 The Benchmark measurements' meaning changes (the emission phase now times MIR generation; the lifecycle includes MIR analysis) | The commit names it. The phase names stay. Recorded numbers are dated |
| R5 CODEMAP's 51 rows are a large doc hunk | Auto, since the removal makes navigation inaccurate: each row loses its deleted stems; G11 restructures Emission → Backend. c5's message lists the rows |
| R6 STATUS's "Generic generation" row still says "concrete ownership checking" (`docs/STATUS.md:83`), the old generator's `AnalyzeInstance` | Removed in c5 (Auto; stale since G4, H: confirm no MIR per-instance ownership claim is meant) |
| R7 The unit timebox (two sessions) | The Analysis and Binding commits are small; the bulk is file deletion |

### 6.5 Open questions

For the planning session (none blocks c1):
- **Q1.** `StaticScalarEntry` to G8 (recommended: its readers are G8's physical layer). Otherwise G7 also deletes
  `EmissionModule.Statics` and `LlvmModuleWriter.Statics.cs` (−71).
- **Q2.** Benchmark: re-point (recommended, G7-2) or delete the emission phase and the lifecycle's emit check.
- **Q3.** The zeroed ratchet rows `:55` (Emission half), `:60-62`: keep at 0 until G10 folds them into its zero-reference rows
  (recommended: G10 owns the row set, and deleting rows reads as weakening). Or delete at G7.
- **Q4.** Evidence: the full `'*.ll'` base and candidate (the plan row; recommended at this deletion size), or G4's reduced set.
- **Q5.** The generator views of the old Analysis (§3.2) at G7 under D5's "deletion" (recommended), or kept until R6-check.
- **Q6.** `ReportPublicationFailure` in `Compilation.Generation.cs` as a G9b Transitional (recommended), or inline in `Project`.
- **Q7.** G9's list gains `AdtDef.IsOrInherits` and `KimiLibraryCatalog.IsArrayOperation`. G8's list gains §2.3's G8 rows
  (`LegacyRuntimeHelpers`' formatting requests, the pool's hooks).
- **Q8.** PLAN after landing: G69 closed (its refusals left with the old generator); row SP closed; G157 (5)'s slot-order clause
  and (22) closed; G157 (7)'s "`Emission.ReportFailure`" reads `ReportPublicationFailure`; next actions G8 and G9.

For the owner: none. G7 adds no vocabulary entry, exception or later removal stage. It weakens no check. Its departures from the plan
row (`StaticScalarEntry` to G8, the readers re-pointed, the pair-layer helpers kept) are unit-content decisions within the adopted
row's intent.

## 7. DECISIONS text (draft)

> **R5-M162 The old generator's driver, lowering, plans and instance machinery deleted (R6-gen G7)** (2026-10-xx, R6-gen; plan §4.9
> row G7, §5.1 M4 and M8; R5-M152 S7; under R5-M5, R5-M88, R5-M160 and R5-M161; Major, G:R6-gen-del)
> - Rule: a component goes with its last caller (S7). G7 deletes `LlvmEmitter*`, `BodyLowering*` (37), `GenericStoragePlan*`,
>   `ObjectGenerationPlan`, `VirtualGenerationPlan`, `DefaultGenerationPlan`, `CompilerFunctionAdapters` and `NumericArithmetic`
>   (13,202 lines) with `Compilation.Emission`. It also deletes every member whose callers were all in them, except G8's physical
>   layer and G9's Binding members. `StaticScalarEntry` is G8's (its readers are `EmissionModule`'s).
> - Product: a publication failure's record (`GenerationFailed_Kd` at the project with its text, until G9b) is
>   `Compilation.ReportPublicationFailure`, Transitional (G9b), the branch `LlvmEmitter.ReportFailure` took on every call since G4.
>   The Benchmark's plan measurements and inheritance lifecycle generate through `Compilation.Analyze()` and `WriteIr`. The pipeline
>   check's comparison of `WriteIr` with itself is deleted.
> - The old Analysis's instance half: `AnalyzeInstance`, `ClearInstances`, `FailedInstance`, `InstanceStorageLimit`, the instance
>   state, `Resolve`'s and `Concrete`'s instance branches, the `this.instance` guards, `OwnershipBody.Instance`, and
>   `ReportDiagnostics`' instance parameters with `ReportInstanceDiagnostics`. Also the old generator's views of the analysis
>   (`UsesImport`, `GetBorrowInputState`, `TrySymbolPlaceAt`, `GetElementState`, `IsPathWithin`, `ParameterCount`,
>   `DeclaredResultType`). The Semantics-case substitution (`Substituting`, the pair-layer helpers) stays until R6-check (SPEC 8.15).
> - Binding (BU's review): the `ClosureCall` bridge and its memo (R5-M36; `ResetCallContexts` inlined), `InstantiatesInLegacy` and
>   `GenericThroughSpecialization` (R5-M34; the class is a `bool`), `ReportEffectViolation`'s instance parameters,
>   `StartupResult.Function`.
> - Shown behavior-preserving: diagnostic and semantic (HIR, MIR) snapshots equal to the base; every `'*.ll'` fixture, sidecar and
>   native result identical. Ratchets: Emission failure strings 446 → 0. M4: Emission 18,284 → ≈ 5,075 lines.
> - Rejected: keeping `LlvmEmitter` for `ReportFailure`; an inline record in `Project`; deleting the Benchmark's emission phases;
>   deleting `OwnershipAnalysis.Instances.cs` whole (its case helpers are the old analysis's own).
> - Unit mapping (the planning session): `StaticScalarEntry`, `LegacyRuntimeHelpers`' formatting requests and lists, and
>   `AggregateLayoutPool`'s remaining hooks join G8; `AdtDef.IsOrInherits` and `KimiLibraryCatalog.IsArrayOperation` join G9.
> - Amends, each with its amendment line: R5-M5 and R5-M88 (done), R5-M34, R5-M36, R5-M94, R5-M152 S7, R5-M160, R5-M161.
