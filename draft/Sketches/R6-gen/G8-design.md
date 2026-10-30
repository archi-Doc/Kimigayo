# R6-gen G8: deleting the physical IR layer of the old Emission

Status: G8's census and design (plan §4.9 row G8, review G:R6-gen-del, hold "no"), revision 1: the planning session's rulings Q1-Q4
applied and the implementation's departures recorded, on `claude/r6-g8` from dev `f406b0905` (PLAN-only after `9613a512e`, so the line
numbers hold). Class Major, no design review. Working record, not authority: what it decides is authority only as DECISIONS R5-M163.
Revision 0 is `claude/r6-g8-design` `b4d5b9abf`.

## Revision 1 (rulings and as implemented)

| Item | Revision 0 | Revision 1 | Source |
| --- | --- | --- | --- |
| Q1 split, G8b's scope | G8a-G8d; the whole instance half | yes | ruling |
| Q2 edited-import reuse | delete the pool's test; MIR twin open | deleted in G8a; no MIR test showed it, so G8d adds `FunctionCodegenTest.AnEditedImportKeepsItsSignature`: one module writes the program before and after a parameter rename, and its `Import` returns the same `FunctionAbi` (review 1's B1: an allocation window cannot see the rebuild, since `AllocationMeasurement` warms up over the first generation; under a probe that reuses only for the same declaration the identity assertion fails) | ruling, review 1 |
| Q3 stale Binding text | G9 or G11 | G9: `Binding.ComparisonPlans.cs:22`, `TyCtxt.Substitutions.cs:65`, `Binding.PlaceAuthority.cs:200-201`, DECISIONS `:6790`; STATUS, `O2Corpus.md`, `LLVM_OPTIMIZATION.md` stay G11's | ruling |
| Q4 Sessions | one base, one candidate | base `20261030-160406-055-session-g8-base` + semantic base `g8-semantic-base` | ruling |
| G8b, found by the re-scan | §2.2 | also the CheckKey Dictionary row (no MIR site requests it, R5-M124) with `WriteKeyCheck`, its callback and `HelperNeeds.DictionaryRuntime`; `DictionaryHelper.Related` (MIR passes -1); `RuntimeAbi.DictionaryInit`; `Compilation.cs`' two "both generators" summaries; ARCH's and CODEMAP's "both/every generator" wording | implementer |
| G8c, found by scan (3) and the writers | §4 | also `OwnershipDeferredPlan.Continuation` (read only by G7's `BodyLowering`) and, on review 1's N1, `OwnershipMatchArmPlan.BodyEntry`, the result declarations' table, `Deliver`'s secured value, `HasCallInspection`, `Condition`'s cleanup start; slot results get no result join | implementer |
| G8d | a collector row | `InstanceCollectorTest.TwoClosuresOfOneInstanceAreItsTwoItems` over `GenericClosureIdentityTest.TwoClosures` (now shared) | implementer |
| As implemented | §6.1 | a1 Emission −3,746, tests −230, Backend −3, docs +22, this file; b1 Backend −388, docs +9; c1 Analysis −97, docs +7; d1 tests +51, docs +4. M4: Emission `.cs` 5,078 → 1,332 (8 files), Backend 14,756 → 14,365 | `git diff --numstat` |

"Plan" is the [R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md). Paths: `C/` =
`src/Kimi/Compiler/`, `E/` = `C/Emission/`, `B/` = `C/Backend/`, `T/` = `tests/xUnitTest/Tests/`. Line numbers are at `9613a512e`. No
build, test or probe was run. Reach comes from word-boundary greps over `src` (with `src/Benchmark` and `src/Playground`) and `tests`,
and three scratch scans: (1) every identifier the deleted files use that is declared outside them and occurs nowhere else; (2) every
qualified `Owner.Member` they use whose member name occurs at most once outside them; (3) every positional record field of
`OwnershipModel.cs` whose name occurs nowhere but its declaration. **(H)** marks an unchecked claim. Commit messages say "R6-gen G8"
(PLAN's issue G8 is unrelated).

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| G8-1 | No product code reads the physical layer since G7: `EmissionModule`, `LlvmModuleWriter*`, `AggregateLayout`/`AggregateLayoutPool`, `FunctionAbiPool`, `WindowsLowering` and `StaticScalarEntry` (21 files, 3,746 lines) are read only by each other, by comments and by 10 test files. All 21 go whole | 1, 2.1 |
| G8-2 | Neither half of `AggregateLayoutPool` nor `FunctionAbiPool`'s `FunctionKoto` keying has a Backend reader: MIR code generation lays out through `Layout.Of` and keys signatures by checked-set item (`ModuleCodegen.Signatures.cs:254`, `ModuleCodegen.Imports.cs:77`). Nothing moves between `C/Emission` and `Backend`; no move commit | 3 |
| G8-3 | `LegacyRuntimeHelpers`' instance half goes, not only the formatting requests (R5-M162's mapping): its one instance is `EmissionModule.RuntimeHelpers` (`E/EmissionModule.cs:253`), so every instance member, and the statics only it calls, lose their last reader. The class keeps its static rows and writers for R8 | 2.2 |
| G8-4 | `C/Emission` keeps 8 `.cs` files (1,332 lines) and the 5 `.ll.in` resources, each with a product reader (§1). They stay in place: the resources' manifest names (`Kimi.Compiler.Emission.*.ll.in`) and `scripts/compare-fixtures.ps1:131` name the folder. A rename or move is G11's (CODEMAP "Emission → Backend") or later, not G8's | 1 |
| G8-5 | The old Analysis's plans that only the deleted generator read go under D5's deletion clause (Q5's rule): 6 lists, `IsConcrete`, 7 unread record fields and the writers that exist only to fill them. `SlotResultPlaces` stays: the analysis reads it to choose a Place | 4 |
| G8-6 | Four units, each one owning component: G8a physical layer (Emission and its test readers), G8b `LegacyRuntimeHelpers`' instance half and the Backend's stale contracts, G8c the old Analysis's plans, G8d the `InstanceCollectorTest` row. G8c is independent and can run in parallel. Binding's stale text, STATUS, `O2Corpus.md` and `LLVM_OPTIMIZATION.md` go to G11 | 6, 7 |

## 1. What stays and why

`C/Emission` after G7 holds 30 `.cs` files (5,078 lines; R5-M162's M4) and 5 `.ll.in` resources (2,335 lines).

### 1.1 Survivors (8 `.cs` files, 1,332 lines; 5 resources)

| File | Lines | Product readers | Test readers |
| --- | ---: | --- | --- |
| `EmissionArtifacts.cs` | 303 | `C/Core/Compilation.Generation.cs`, `src/Kimi/SolutionAndProject/Project.cs` (`PublishArtifacts`: `kimi build`, `kimi test`) | `EmissionArtifactsTest`, `LibraryEmissionTest`, `PipelineSwitchTest` |
| `NativeToolchain.cs`, `.Verification.cs` | 545 + 133 | `Project.cs`; `Unit/Command/` `BuildCommand`, `CommandExecution`, `RunCommand`, `TestCommand`, `ToolchainCommand` | `NativeToolchainTest`, `EmissionArtifactsTest`, `LibraryEmissionTest`, `Kernel32ImportsTest` |
| `ToolchainResolver.cs` | 55 | `NativeToolchain.cs:274, :312`, `NativeToolchain.Verification.cs:28` | `ToolchainResolverTest` |
| `ArtifactPaths.cs` | 27 | `Project.cs`, `TestCommand.cs` | `NativeToolchainTest`, `SolutionInputTest` |
| `ArtifactFiles.cs` | 61 | `DependencyLock.cs`, `TestCommand.cs`, `EmissionArtifacts.cs:6` | `EmissionArtifactsTest`, `LibraryEmissionTest`, `NativeToolchainTest` |
| `Kernel32Imports.cs` | 94 | `C/Binding/Binding.Attributes.cs`, `SolutionAndProject/NativeConfiguration.cs` | `Kernel32ImportsTest`, `EmissionArtifactsTest` |
| `WindowsProfile.cs` | 114 | `B/LlvmModuleText.cs`, `B/RuntimeAbi.cs`, `Binding.Attributes.cs`, `Lsp/WorkspaceCheck.cs`, `Project*.cs`, `Solution.cs`, `src/Benchmark/Benchmarks/SyntaxEditMeasurements.cs` | ~90 classes (the target) |
| `WindowsRuntime.ll.in` | 304 | `B/LlvmModuleText.cs:227` (manifest name) | `Kernel32ImportsTest:30, :63` (path); `scripts/compare-fixtures.ps1:131` (path) |
| `Utf8BufferRuntime`, `Utf8FormatRuntime`, `Utf8FloatRuntime`, `Utf8FloatRyu` `.ll.in` | 431 + 607 + 217 + 776 | `B/LegacyRuntimeHelpers.Formatting.cs:351` (manifest names; MIR's formatting runtime, `ModuleCodegen.Frame.cs:119`) | via `FunctionCodegenTest.Formatting` |

None of the survivors reads a deleted type (grep of the 8 files for the 21 files' types: none).

### 1.2 The physical layer (21 files, 3,746 lines): no product reader

| Type | Readers outside the 21 files |
| --- | --- |
| `EmissionModule` (with `EmissionOpcode`, `EmissionOperandKind`, `EmissionOperand`, `EmissionSlot`, `EmissionSubslot`, `ArithmeticCheckKind`, `EmissionInstruction`, `PatternTestStep`, `SliceShape`, `EmissionFunction`) | Comments: `B/AbiRecords.cs:9`, `B/AbortReasons.cs:9`, `B/LegacyRuntimeHelpers.cs:58`, `B/LlvmModuleText.cs:19`, `B/LlvmScalarText.cs:23`, `B/ObjectRuntime.cs:21`, `B/RuntimeAbi.cs:10`. Test: `T/MinimalEmissionTest.cs:21-54` (`typeof`) |
| `LlvmModuleWriter*` (16 partials, 2,814) | Comment `T/KimiLibraryShapeTest.cs:458` |
| `AggregateLayout`, `AggregateLayoutPool` | Comments `B/Layout.cs:5, :22, :37`, `B/AbiRecords.cs:10`, `B/ObjectRuntime.cs:22`. Tests: `LayoutTest`, `KimiLibraryShapeTest:669-680`, `AggregateEmissionTest:74-75`, `ObjectHandleModeTest:40-63`, `DictionaryLibraryTest:255` |
| `FunctionAbiPool` | Comments `B/Abi.cs:13, :18`, `B/AbiRecords.cs:10`. Tests: `AbiTest:73`, `FunctionEmissionTest:80-158`, `AggregateFunctionEmissionTest:105-157`, `ReferenceEmissionTest:180-249` |
| `WindowsLowering` | Comments `B/Abi.cs:20`, `B/AbiRecords.cs:10`, `B/Layout.cs:35`. Tests: `LayoutTest:46, :75`; comment `KimiLibraryShapeTest:22` |
| `StaticScalarEntry` | None outside (`EmissionModule.Statics`, `LlvmModuleWriter.Statics`) |

`src/Benchmark` and `src/Playground` read none of the 21 files' types, nor `LegacyRuntimeHelpers`' instance members, nor §4's members.

## 2. Deletion census

### 2.1 Whole files (`E/`, 21 files, 3,746 lines)

| Files | Lines | Last callers (all inside the set) |
| --- | ---: | --- |
| `EmissionModule.cs` | 331 | `LlvmModuleWriter*`; G7 deleted its producer (`BodyLowering`, `LlvmEmitter`) |
| `LlvmModuleWriter.cs` and 15 partials (`Aggregates` 312, `BuiltinComparisons` 128, `Closures` 314, `Comparisons` 103, `Conversions` 51, `Elements` 69, `MovePaths` 128, `Objects` 30, `Patterns` 176, `Scalars` 282, `Sequences` 436, `Statics` 68, `Strings` 93, `Testing` 74, `Updates` 37, `Virtuals` 38) | 475 + 2,339 | none: `LlvmModuleWriter.Write`'s callers were `LlvmEmitter.cs` (deleted at G7) |
| `AggregateLayout.cs` | 462 | `EmissionModule`, `LlvmModuleWriter*`, `FunctionAbiPool`; the hooks `CollectionDrop`, `IsSealedObjectTarget`, `RegisterDestructor`, `ClearDestructors` were set by `LlvmEmitter`/`BodyLowering` (G7 §2.3); `RegisterDestructor`'s last caller is `KimiLibraryShapeTest.cs:676`, re-pointed (§2.3) |
| `FunctionAbiPool.cs` | 88 | none outside tests |
| `WindowsLowering.cs` | 30 | `AggregateLayoutPool`, `FunctionAbiPool`; `GetCompilerFunction` none |
| `StaticScalarEntry.cs` | 21 | `EmissionModule.cs:262, :284`, `LlvmModuleWriter.Statics.cs` |
| **Total** | **3,746** | `Fail("` count in them: 0 (the ratchet is already 0, R5-M162) |

### 2.2 Members outside the set that lose their last reader (Backend; G8b)

Scan (1) found 6 members; reading their callers and the instance half's bodies found the rest. Line ranges (H) ±2.

| Member | Site | Last caller | Lines |
| --- | --- | --- | ---: |
| The instance state: `arrayHelpers`, `FunctionReferences`, `NeedsArrayRuntime`, `NeedsStringComparison`, `Clear`, `JoinNeeds` | `B/LegacyRuntimeHelpers.cs:272-283, :394-419` | `EmissionModule.cs:253, :280`, `LlvmModuleWriter.cs:27, :41`; the rows' `Needs` stay (MIR reads `HelperNeeds`: `ModuleCodegen.Frame.cs:76-83`) | ≈ −38 |
| `Request(ArrayHelper)`, `WriteArrays` | `LegacyRuntimeHelpers.Arrays.cs:349-375` | `LlvmModuleWriter.cs:37`; `WriteArray`, `WriteArrayRuntime` stay (`ModuleCodegen.Arrays.cs:87`, `ModuleCodegen.Frame.cs:114`) | ≈ −27 |
| `dictionaryCallees`, `dictionaryHelpers`, `DictionaryHelpers`, `NeedsDictionaryRuntime`, `Callee`, `Resolve`, `Request(DictionaryHelper)`, `HasUnresolvedReference`, `WriteDictionaries`, `WriteDictionaryInit`; `DuplicateAdapter` (only `WriteDictionaries`); `RuntimeAbi.DictionaryInit` (`B/RuntimeAbi.cs:71`, only `WriteDictionaryInit`) | `LegacyRuntimeHelpers.Dictionaries.cs:100-~125, :144-243, :294-307` | `EmissionModule.cs:311`, `LlvmModuleWriter.cs:39`; `WriteDictionary`, `DictionaryRow`, `Construction` stay (`ModuleCodegen.Dictionaries.cs:61`, `InstanceCollector.Edges.cs:271`) | ≈ −115 |
| `formattingWrites`, `formattingConversions`, `NeedsFormattingRuntime`, `RequestFormattingWrite`, `RequestFormattingConversion`, `WriteFormatting`; `WriteFormattingWrite` (only `WriteFormatting`) | `LegacyRuntimeHelpers.Formatting.cs:102-105, :112-114, :220-268, :372-402` | no caller since G7 / `LlvmModuleWriter.cs:38`; `WriteFormattingRuntime`, `WriteFormattingConversion`, `FormattingRow` stay (`ModuleCodegen.Frame.cs:119`, `ModuleCodegen.Formatting.cs:105`) | ≈ −90 |
| `NeedsStorageBytes`, `WriteStorageBytes` | `LegacyRuntimeHelpers.Storage.cs:81-82, :308-317` | `WriteDictionaries`; `TryAllocate`, `TransferBytes` stay (`ModuleCodegen.Frame.cs:126-127`) | ≈ −13 |
| `WriteRawSlice` | `LegacyRuntimeHelpers.Storage.cs:91-115` | `LlvmModuleWriter.Sequences.cs:83` (DECISIONS:11107 "goes with it") | ≈ −25 |
| `LlvmScalarText.WritePositionConversion` | `B/LlvmScalarText.Conversions.cs:83-111` | `LlvmModuleWriter.Conversions.cs:36` | ≈ −28 |
| `sealed` instance class → `static partial class` (5 declarations) | `LegacyRuntimeHelpers*.cs` | — | 0 |
| **Total** | | | **≈ −336** (H) |

Scans (1) and (2) are first-order; the implementer re-runs them after G8a and after G8b in the same area (`scratch` script, as G7). Scan (2)
found no enum member outside the set read only by it (the 49 `EmissionOpcode`, 13 `EmissionOperandKind`, `ArithmeticCheckKind` and
`SliceShape` members are the set's own). `Abi.ResultType` (`B/Abi.cs:169`) keeps its in-class caller (`:207`) and may become
private (optional, H). `Layout.Row` and `Abi.HasResultSlot` keep MIR readers.

### 2.3 Test readers (G8a, same commit as the deletion)

| File | Site | Action | Lines |
| --- | --- | --- | ---: |
| `MinimalEmissionTest` | `PhysicalModuleStorageHasNoTransitiveSemanticReferences` `:21-54` | delete: its subject is deleted | −35 |
| `FunctionEmissionTest` | `AbiPoolReusesSignaturesWithoutRetainingSyntax` `:80-104`, `AbiPoolReusesAnEditedImportWithoutAllocating` `:123-139`, `Import` `:141-151`, `RegisterTemporary` `:153-159` | delete (Q2 on R5-M58's MIR-side coverage) | −58 |
| `AggregateFunctionEmissionTest` | `PhysicalSignatureCacheReusesShapesWithoutRetainingSyntax` `:103-125`, `Populate` `:150-157` | delete | −31 |
| `ReferenceEmissionTest` | `ReparseReusesThePhysicalAbi` `:179-195`, `PhysicalAbiPoolDoesNotRetainOriginBinders` `:197-206`, `Populate` `:242-249` | delete | −35 |
| `ObjectHandleModeTest` | `EqualSizedHandlesKeepModeSpecificDestructionLayouts` `:40-63` | delete: the old pool's handle interning; the MIR's per-mode release is `FunctionCodegenTest.Objects`' (H) | −24 |
| `AbiTest` | `:73` and the comment's "old pool" clause `:67-68` | delete the line, trim the comment | −2 |
| `LayoutTest` | the old-pool and gate assertions `:46, :75, :89, :125, :137, :162-164, :182-184, :495, :525, :536, :554-556, :587`; comment clauses `:13, :44, :72, :82, :119, :152, :174, :545` | delete the assertions; each method keeps its `Layout` half | ≈ −22 |
| `AggregateEmissionTest` | `:74-75` | re-point to `c.Layout.Of(type).Value!` | −1 |
| `DictionaryLibraryTest` | `PrivateHandleUsesTheExpectedPlatformLayout` `:255-256` | re-point to `c.Layout.Of(...)`; `CLayout` from the Type's `Adt.IsCLayout` (H) | 0 |
| `KimiLibraryShapeTest` | `Layout(path)` `:669-680`; comments `:22`, `:458` | re-point: return `(ValueLowering, BoundType)` from `Library.Value.Layout.Of(type).Value!`; offsets by `FieldOffsets.Span[AdtDef.IndexOf(type, name)]` at `:561`, `:583`, sizes at `:558`, `:586`. `RegisterDestructor` goes with the pool (the plan's "pool use re-pointed") | ≈ −4 |
| **Total** | | | **≈ −212** (H) |

No `ArchitectureRulesTest` row names a deleted type; no test enforces ARCH's vocabulary table by name (grep).

### 2.4 Records and comments

| Site | Change | Unit |
| --- | --- | --- |
| Backend comments naming a deleted type (§1.2's 16 lines: `Abi.cs:4, :13, :18, :20`, `AbiRecords.cs:9-10`, `AbortReasons.cs:9`, `Layout.cs:5, :20-22, :35, :37`, `LegacyRuntimeHelpers.cs:58`, `LlvmModuleText.cs:19`, `LlvmScalarText.cs:23`, `ObjectRuntime.cs:21-22`, `RuntimeAbi.cs:10`) | drop the clause or the prohibition's deleted type | G8a |
| The Backend's other "old generator", "both generators", "legacy .ll", "old lowering" wording (≈ 32 lines in `B/` by grep, ≈ 14 of them in `B/Codegen/`, e.g. `LegacyRuntimeHelpers.cs:33, :55`, `ModuleCodegen.cs:10, :37`, `DropGlue.cs:64`), and `LegacyRuntimeHelpers.cs`' contract (requests, request order, byte identity with the legacy writer) | rewrite to the one generator | G8b |
| ARCH `:57` `EmissionOpcode` (49) | row deleted (G7's design: "goes at G8") | G8a |
| ARCH `:143`, `:144` (the old pool's and `FunctionAbiPool`'s clauses) | clauses deleted (Auto: recorded contract) | G8a |
| CODEMAP `:37-38`, `:60` ("Old physical IR layer (until R6-gen's G8)" row), `:73`, `:77`, `:62`'s clauses naming `EmissionModule.RuntimeHelpers`, `WindowsLowering`, `AggregateLayoutPool`, `FunctionAbiPool`, `LlvmModuleWriter.*` and the requests; and stems in the feature table (21 references by grep) | Auto: navigation | G8a, G8b |
| DECISIONS: one entry R5-M163 (§6.4) with amendment lines on R5-M162 (unit mapping done), R5-M58 (the old pool's reuse half gone), R5-M148 (the gate's old half) | Auto | G8a (G8b–G8d add lines) |
| STATUS `:58`, `:86` (old generator's defects, "legacy code generation" in the raw row); `Binding.ComparisonPlans.cs:22` (Transitional reason naming G7), `TyCtxt.Substitutions.cs:65`, `Binding.PlaceAuthority.cs:200-201` and DECISIONS `:6790` (PlaceOriginSource's exception: its first reason, "the old Emission reads it", ended at G7; the second stays); `src/Benchmark/O2Corpus.md:51`; `docs/dev/LLVM_OPTIMIZATION.md:684-686` (an unlisted document) | **G11**: STATUS rows and Transitional reviews are G11's list; the Binding lines need BU's review and G8 touches no Binding file; LLVM_OPTIMIZATION needs explicit scope | G11 |

## 3. `AggregateLayoutPool` and `FunctionAbiPool`

- **Destruction half** (`Destructor`, `NeedsDestruction`, `CollectionDrop`, `GenericDestructor`, `ObjectPayloadDrop`, `RegisterDestructor`,
  `ClearDestructors`, `UsedCollectionFields`, the collection and sealed-handle interning, `E/AggregateLayout.cs:12, :54-83, :104-172`):
  read only by `LlvmModuleWriter.Aggregates`/`.Objects` and the hooks' setters (deleted at G7). The MIR's destruction is `DropGlue`
  (`B/DropGlue.cs`) and `FunctionCodegen.Glue`; no Backend file names the pool but in comments. **Goes.**
- **Representation half** (`Get`, `Represent`, `GetCore`, `GetEnum`, `Used`, `ResourceLimitFailure`, `Layout`): its facts are
  `Layout.Of`'s (`Represent` reads them, `:185-203`); its readers are `EmissionModule`, `FunctionAbiPool.GetValue` and tests. **Goes**;
  tests re-point to `Layout.Of` (§2.3).
- **`FunctionAbiPool`**: the ordinal-and-`FunctionKoto` cache (`Get`, `:38-60`), `Import` (`:68-76`), the gate (`Supports`, `GetValue`,
  `HasResultSlot`, `ResultType`, `:24-33`). MIR code generation builds through `Abi.Of`/`Abi.Matches` keyed by item and import index
  (`ModuleCodegen.Signatures.cs:254-260`, `ModuleCodegen.Imports.cs:77-83`). **Goes whole.**
- **Moves:** none. ARCH's "Storage layout" and "Function signatures" rows already name `Layout` and `Abi` (Backend) as the owners; the
  pools were the old generator's caches over them.

## 4. The old Analysis's plans (G8c)

Rule (D5, Q5 of G7): a plan only the deleted generator read goes, with the writers that exist only to fill it; a writer that also
decides the analysis's own verdict stays. Shown by word greps over `src` and `tests` (readers and writers listed below are all of
them), scan (3) for fields, and reading each writer's body.

| Member (`C/Analysis/OwnershipModel.cs`) | Writers | Readers | Verdict role | Action |
| --- | --- | --- | --- | --- |
| `ResultWrites` `:240`, record `OwnershipResultWrite` `:546-547` | `OwnershipAnalysis.Results.cs:63-66` (`WriteResult`) | none (`Clear` `:369`) | none: the `Write` operation is emitted before | delete field, record, `Clear` line, the `Add` block |
| `Deliveries` `:242`, `OwnershipDelivery` `:536-537` | `Results.cs:77-80` (`Deliver`) | none | none: the `Deliver` operation stays; `value` and `delivery` exist only for the plan | delete; keep `this.Emit(Deliver, …)` |
| `SlotResults` `:238`, `ResultArrivals` `:239`; `OwnershipSlotResult` `:541-542`, `OwnershipResultArrival` `:544` | `Results.cs:104-147` (`FinalizeResults`' non-scalar branch) | only `ResultArrivals.Count` inside the same writer | none: Phi inputs (scalar) stay | delete the branch; `ResultJoin` (`:24-28`) registers scalar joins only, `resultJoins` drops `Declare`; `resultDeclarations` keeps its writer (`:46`) only if another reader remains (H: after `WriteResult`'s block goes, none; the `Declare` emission stays) |
| `SlotResultPlaces` `:241` | `Results.cs:39` | `Results.cs:36` (Place sharing across deferred replicas) | **yes** (Place identity) | **stays** |
| `StringComparisons` `:253`, `OwnershipStringComparison` `:559` | `OwnershipAnalysis.Comparisons.cs:132` | none (the `B/` and `ModuleCodegen` hits are `LegacyRuntimeHelpers.StringComparisons`, a different member) | none: the `StringComparison` value kind is set at `:131` | delete; `InspectString`'s `out loan` (`:24`) becomes a discard at `:124-125` (H) |
| `CallLoans` `:255`, `OwnershipCallLoans` `:557` | `OwnershipAnalysis.cs:1592-1595, :1621-1625` | none | none: `EndComparisonLoans` (`:1621`) stays as a statement; `borrows` (`:1536-1552`) exists only for these two writes (H: `HasCallInspection` is a pure query) | delete the writes, `loanEnd`, `borrows` |
| `IsConcrete` `:302` (public) | `OwnershipAnalysis.cs:661`, reset `OwnershipModel.cs:350` | none | none | delete |
| `OwnershipMatchPlan.ArmStart`, `ArmCount` `:175` | positional at `OwnershipAnalysis.Match.cs:101`, `Iteration.cs:52` | none | none | delete the fields and arguments |
| `OwnershipMatchArmPlan.DecompositionStart`, `DecompositionCount`, `GuardBranch`, `GuardValue`, `GuardCleanupStart` `:178-179` | positional at `Match.cs:193`, `Iteration.cs:72, :87` | none (`Test`, `GuardEntry`, `BodyEntry`, `GuardLoan` are read: `OwnershipBody.Comparisons.cs:198, :378-386`, `Iteration.cs`) | none | delete the fields; the locals `guardValue`, `guardCleanupStart` (`Match.cs:142-143, :170-173`) go if only the plan read them (`Condition(guard, out _)`, H) |

Expected `C/Analysis` net ≈ −75 (H). Evidence: the diagnostic snapshot and the semantic snapshot (HIR, MIR) equal to the base, and the
Analysis classes that read these bodies (`OwnershipAnalysisTest`, `MatchOwnershipTest`, `EnumOwnershipTest`, `DeferredEmissionTest`,
`PatternBindingTest`, `VirtualOwnershipTest`). `C/Analysis` is outside R4's hold (`Mir/Analysis/**`); D5 allows deletion.

## 5. Owners, holds, vocabulary, ratchets, metrics

| Area | Owner | G8's use | Hold |
| --- | --- | --- | --- |
| `C/Emission` | R6-gen (frozen, D5) | G8a deletes 21 files | none needed |
| `B/` outside `Codegen/` | R5 | G8a comments, G8b members and contracts | none (R5's own) |
| `B/Codegen/**` | R5 (PLAN, 2026-10-31) | G8b comment wording only (≈ 14 lines) | none (R5's own) |
| `C/Analysis` (old) | D5 (until R6-check) | G8c deletion | announce to R4; no `Mir/Analysis` file |
| `Binding/**` | BU | none (stale text to G11) | none |
| `T/InstanceCollectorTest.cs` | R5 | G8d row | none |
| ARCH, CODEMAP, DECISIONS | planning session / Auto | §2.4 | serialize with dev's windows |

- **Vocabulary:** `EmissionOpcode` (49) leaves ARCH's table (G8a). No entry is added. `CompilerFunctionKind` keeps its readers.
- **Transitional:** none of G8's members carries one; `LegacyRuntimeHelpers`' 19 `[Transitional("R8")]` stay; the 5 `R6-gen` ones in
  `Compilation.Generation.cs` are G9b's.
- **Ratchets:** no `ArchitectureRulesTest` row moves (the Emission failure-string row is already 0; the zeroed `.Emission.` rows stay at
  0 until G10, R5-M162 Q3). G10's zero-reference rows gain nothing to exclude: after G8 `EmissionModule` and `LlvmModuleWriter` occur only
  in history.
- **M4:** Emission `.cs` 5,078 (30 files) → 1,332 (8 files); Backend 14,756 → ≈ 14,410 (H).

## 6. Commits, checks, size, risks, questions

### 6.1 Commits (behavior-preserving deletions; no move)

| Unit | Commit | Content | Net (H) |
| --- | --- | --- | ---: |
| G8a | a1 | §2.1's 21 files; §2.3's test edits; §2.4's G8a comments; ARCH `:57`, `:143-144`; CODEMAP; DECISIONS R5-M163 with its amendment lines | Emission −3,746; tests −212; Backend ≈ −10; docs ≈ −15 |
| G8b | b1 | §2.2's members; `LegacyRuntimeHelpers` static; the Backend's contracts and "both generators" wording; CODEMAP `:62`; R5-M163 line | Backend ≈ −340 |
| G8c | c1 | §4; R5-M163 line (or its own entry if it lands first) | Analysis ≈ −75 |
| G8d | d1 | `InstanceCollectorTest`: one Fact (or a row of `ClosuresAndItemsOfInstancesAreTheirBodies`, `:768-791`) over `GenericClosureIdentityTest`'s source asserting four items `Closure pair [bool]` ×2 and `Closure pair [i32]` ×2, distinct (reusing `Label`); `GenericClosureIdentityTest.cs:57`'s `Assert.NotSame` deleted | tests ≈ +10 |
| **Total** | | | **≈ −4,390**, against the plan's ≈ −3,600 (which counted neither §2.2, §2.3, §4 nor the tests) |

### 6.2 Checks

- **Base Session** in the implementation worktree before any edit (`'*.ll'`, the 40 harnessed milestones, the diagnostic and semantic
  snapshots, `-DiagnosticSnapshot`), on the planning session's go; **candidate Session** after the last of G8a/G8b/G8d (and G8c if it has
  landed), `compare-fixtures.ps1` for `.ll`, sidecar and native identity; the five CLI harnesses (no harness path changes); the
  `Kimigayo.slnx` build (`src/Benchmark`, `src/Playground`).
- Per unit (feedback): incremental build; G8a `LayoutTest`, `KimiLibraryShapeTest`, `AbiTest`, `AggregateEmissionTest`,
  `ObjectHandleModeTest`, `DictionaryLibraryTest`, `FunctionEmissionTest`, `AggregateFunctionEmissionTest`, `ReferenceEmissionTest`,
  `MinimalEmissionTest`, `ArchitectureRulesTest`; G8b `FunctionCodegenTest` (Formatting, Dictionaries, Arrays, Strings),
  `InstanceCollectorTest`, `Kernel32ImportsTest`, the allocation tests of the touched classes; G8c §4's classes and both snapshots;
  G8d `InstanceCollectorTest`, `GenericClosureIdentityTest`.
- `.ll` identity is the behavior proof for G8a/G8b: no deleted line is reachable from `Compilation.Generate` (§1.2).

### 6.3 Risks

| Risk | Response |
| --- | --- |
| A reflection or string reader of a deleted type (`MinimalEmissionTest`'s walk is one) | grep of type names over `tests` and `src` found only §2.3's; the build and the Unit classes catch the rest |
| `LegacyRuntimeHelpers` as a static class changes a static initializer's order (`FormatLiteral` "initialized after the rows it reads", `Formatting.cs:107-110`) | keep declaration order; `sealed partial` → `static partial` changes no initializer; `FunctionCodegenTest.Formatting` covers it |
| §4's field deletions shift a positional constructor call silently | delete fields only with their positional arguments in the same edit; the diagnostic snapshot must show 0 differences |
| A test's coverage of R5-M58's edited-import reuse disappears with `FunctionEmissionTest`'s pool test | Q2 |
| G8c touches files R4 may read while A5 work continues | G8c edits `C/Analysis` only, never `Mir/Analysis`; announce the window |

### 6.4 DECISIONS entry (draft; R5-M163 is dev's next free number at `9613a512e`, renumbered at landing if taken)

> **R5-M163 The old Emission's physical IR layer deleted (R6-gen G8)**: `EmissionModule`, `LlvmModuleWriter*`, `AggregateLayout`/
> `AggregateLayoutPool`, `FunctionAbiPool`, `WindowsLowering` and `StaticScalarEntry` (21 files, 3,746 lines) went with their last
> callers (G7's `LlvmEmitter`); the tests that read them were deleted or re-pointed to `Layout.Of`. `LegacyRuntimeHelpers` lost its
> instance half (its one instance was `EmissionModule`'s) and is a static class until R8. The old Analysis's plans only the deleted
> generator read went (D5's deletion; `SlotResultPlaces` stays, a Place decision). `C/Emission` keeps the toolchain, publication and
> profile files and the `.ll.in` resources, whose names and paths the Backend, the tests and `compare-fixtures.ps1` read. ARCH drops
> `EmissionOpcode` (49). Rejected: moving the survivors at G8 (resource names, harness paths; G11's CODEMAP decides the folder).

### 6.5 Open questions

| # | Question | Recommendation | Who |
| --- | --- | --- | --- |
| Q1 | Accept the split G8a–G8d (§7) and G8b's widening beyond R5-M162's "formatting requests" to the whole instance half | yes: one instance, one rule (S7) | planning session |
| Q2 | R5-M58's edited-import reuse: delete `AbiPoolReusesAnEditedImportWithoutAllocating` only, or add its MIR twin (`ModuleCodegen.Imports`' `Stamped` reuse, a warm `WriteIr` after a renamed parameter allocating 0 B)? | delete in G8a; open a PLAN issue for the MIR twin unless a warm `WriteIr` allocation test with an import already covers it (H) | planning session |
| Q3 | Binding's stale text and DECISIONS `:6790`: G11 (as here) or G9 (already a BU-reviewed Binding unit)? | G9: it already edits Binding under BU, and G11 is doc-only | planning session |
| Q4 | Base Session: one for G8a/b/d together, G8c covered by its snapshots plus the candidate Session if it lands first | yes | planning session |

## 7. Sizing and split

Estimated implementation time without heavy runs: **≈ 2 h 10 min**, so the unit splits. Each sub-unit has one owning component, its
own commit and completion condition.

| Unit | Owner | Content | Completion condition | Time (H) | After |
| --- | --- | --- | --- | ---: | --- |
| G8a | old Emission | §2.1, §2.3, §2.4 G8a rows, R5-M163 | `C/Emission` holds §1.1's files only; build green; the G8a classes pass; no grep hit of the 21 types outside history | 55 min | — |
| G8b | Backend (`LegacyRuntimeHelpers`, `LlvmScalarText`) | §2.2, the contracts and wording, CODEMAP `:62` | scans (1)/(2) find nothing new; class is static; G8b classes pass | 35 min | G8a |
| G8c | old Analysis (D5) | §4 | both snapshots 0 differences; §4's classes pass | 30 min | — (parallel) |
| G8d | Backend tests (collector) | §6.1 d1 | the row fails if two Closures share an item (checked by reasoning, H) and passes | 10 min | — |
| Session | — | base before G8a; candidate after the last unit | §6.2 | heavy | all |

G8a at 55 minutes is the tightest; if it grows, its test edits (§2.3) cannot split from the deletion (the build needs both), but its
records (CODEMAP, ARCH, DECISIONS) can move to a docs-only commit after it.
