# WP design: owners of `WindowsProfile` (R5 U8)

Base `abc7e28c7` (dev). Design only; implementation waits for the planning session's GO. Major (a new component, a moved one);
behavior-preserving. PLAN WP; census U8; R5-M14 left the owner open; R5-M167 row 15 kept "the toolchain, publication and profile
files" in `C/Emission` until R9.

| Revision | Change |
| --- | --- |
| 0 (0fb6e6b0c) | Split of facts and owners; recommended (b); (d) conditional on BU. |
| 1 | Review REVISE: (d) joins the unit (B1; BU's three conditions); the boundary with Core's `TargetTriple`/`IrTarget` (N1); ARCH edit corrected (N2); CLI harnesses and `-VerifyToolchain` required (N3); partial class rejected (N4); the reduction plan's R9 row among the departures, with the owner's approval (N5); size re-estimated (N6). |

## Required meaning: two kinds of facts

`Emission/WindowsProfile.cs` (114 lines) mixes the windows-x64 **target profile** (IMPL 21's name; SPEC 22: "one executable target
profile") with the **native-toolchain identity** (VERIFICATION's name) read from the compiler's embedded backend record (R5-M165).

| Kind | Members (meaning) | Readers |
| --- | --- | --- |
| Target | `Target` (IMPL 21.4.1) | Project, Project.Testing, Solution, Lsp/WorkspaceCheck, ArtifactPaths, LlvmModuleText, EmissionArtifacts, NativeToolchain, Kernel32Imports, 90 test files, Benchmark |
| Target | `Name`, `DataLayout`, `Cpu`, `Features`, `UnwindTables` (21.4.1); `RelocationModel`, `CodeModel` (the profile's llc settings; the module's PIC Level agrees) | LlvmModuleText (header, `#0`), EmissionArtifacts (manifest), NativeToolchain (llc, manifest check), the record check |
| Target | `EntrySymbol`, `Subsystem` (21.4.2) | RuntimeAbi.Start, EmissionArtifacts, NativeToolchain (link), LibraryEmissionTest |
| Target | `FloatMarker` `_fltused`, `BackendLibrary` `kimi_backend`, `ProvidedSymbols` (21.2 reserved names, 20.4 reserved supply, 21.4.7 helpers) | Binding.Attributes (native resolution), NativeConfiguration, LlvmModuleText, EmissionArtifacts, NativeToolchain |
| Target | `RuntimeDeclarations` and `RuntimeImports`, the same nine kernel32 symbols in the same order (21.4.7, 21.2) | Binding.Attributes, Kernel32ImportsTest; `RuntimeImports`: Kernel32Imports, NativeToolchain |
| Target | `Kernel32Imports.LibraryName` `kernel32` and `Kernel32Imports.Symbols` (IMPL 20.4: the reserved supply's name and symbol set, checked at native resolution), held today by the import-library generator | Binding.Attributes:119, 122, 160; NativeConfiguration:52, 83; EmissionArtifacts:179, 253, 255; NativeToolchain:304, 333, 340, 360; Kernel32Imports.ValidateLibrary; Kernel32ImportsTest:29, 141 |
| Toolchain | `LlvmVersion` (20.3.2's pinned release), `BackendPackageId`, `BackendAbiVersion`, `BackendVersion` (= `CompilerRelease.Version`), `BackendSha256`, `BackendFile`, `Kernel32DefinitionSha256`, `DlltoolSha256`; the static constructor reading `profile.json` (resource `Kimi.WindowsBackendProfile.json`) and cross-checking it against the target facts; `SequenceEqual` | NativeToolchain(.Verification), EmissionArtifacts (manifest; R5-M165's missing-record check), Kernel32Imports, ToolchainResolver; EmissionArtifactsTest, NativeToolchainTest, ToolchainResolverTest |

Kernel32Imports keeps the kernel32 import library's generation identity (`Dll`, `Generator`, the `.def` text, its hash, the dlltool
hash) and its validation. Found while reading: `check` runs the toolchain half. Binding's reads of `ProvidedSymbols` and `RuntimeDeclarations` (static readonly) run the
type initializer, which parses the embedded record; the Kimi library's `#LibraryImport("kernel32", …)` (`Windows.kimi`) makes that every
check. Binding also reads `Kernel32Imports.Symbols`, whose initializer hashes `Kernel32.def` and reads `DlltoolSha256`. Not measured; one
parse per process, and a defect in the record would fail `check` with a TypeInitializationException.

## Owners (AGENTS: one owning component; ARCH: phases and dependencies)

- **Target profile → Core** (`Core/WindowsProfile.cs`, class `Kimi.Compiler.WindowsProfile`, name, namespace and member visibility kept).
  Binding (P2-P5), the Backend (P11-P12), target selection (Project, Solution, LSP) and the native build all read it; Core already holds
  what the phases share (`Compilation`, `SourceRef`, `SourceLocationTable`, which ARCH's Backend row lets the Backend read) and references
  nothing of `C/Emission` (comments only), so Binding → Core, Backend → Core and Emission → Core add no cycle. Commit 1 changes no
  reader of the target half (Binding, Lsp, SolutionAndProject, Backend, Benchmark, the 90 test files). It also takes the kernel32 supply:
  `Kernel32Library` (`kernel32`) and `Kernel32Symbols`, built by its type initializer from `RuntimeDeclarations`' symbols followed by
  the five others, in today's order, so no literal list repeats and Kernel32Imports keeps none (BU's conditions). Binding.Attributes'
  three qualifiers change (BU reviews them); a grep of `Binding/` for the C/Emission types finds no other read, so after U8 Binding reads
  no C/Emission type. The type initializer builds three arrays and reads no file or resource.
- **Boundary with Core's target** (`Compilation.TargetTriple`, `Compilation.IrTarget`, `Misc/Target`): those describe the selected
  triple of any check (validity, `pointerWidth` and the target variables, IMPL 20.2.1); `WindowsProfile` is the one executable target
  profile generation and the native build pin (IMPL 21.4, SPEC 22). `IrTarget` computes exactly `WindowsProfile.DataLayout` for
  `x86_64-pc-windows-msvc`, with nothing tying them. Neither reads the other (the profile pins the layout of its LLVM release; `IrTarget`
  follows LLVM's rule for every triple): U8 adds a test that `IrTarget.Create(TargetTriple.Parse(WindowsProfile.Target)).DataLayout`
  equals `WindowsProfile.DataLayout`, and the ARCH row states the boundary. A second executable profile would reopen it (SPEC 22).
- **Toolchain identity → the native build** (`Emission/ToolchainIdentity.cs`, class `ToolchainIdentity`): only P13 reads it (build,
  publication's manifest, toolchain verify, kernel32 import generation), so it stays in `C/Emission`, the build boundary R9 moves to
  `src/Kimi/Build` (reduction plan, its R9 row). The record reader moves, its target checks qualified `WindowsProfile.`.
- `profile.json`, its resource name and the scripts (`Read-KimiWindowsProfile`, build.ps1, kernel32.ps1) are unchanged: no hash moves,
  so no generator runs. The record keeps its target copies (profile, library, providedSymbols) for the scripts, cross-checked as today.

## Options

- **(a) Move the file to Core unchanged.** No reader edit. Core would own P13's record parse and hashes, R9 would move them out of
  Core again (the second move R5-M167 row 15 avoids), and check keeps parsing the record. Line 34 (210 characters) breaks Core's
  160-character limit, so the move is not even byte-identical. Rejected.
- **(b) Split: target profile in Core, toolchain identity in the native build, the kernel32 supply's name and symbols in the
  profile. Recommended.** It also deletes `RuntimeImports`, a second copy of `RuntimeDeclarations`' symbol column (the 210-character
  line), and `Kernel32Imports.Symbols`.
- **(b′) The toolchain half as a `NativeToolchain` partial** (`NativeToolchain.Supply.cs`). Rejected: four components read it
  (publication, kernel32, resolver, build), and moving the record's static constructor into `NativeToolchain` (none today) would make
  it precise-init, so every member, the `IsToolchainFailure` exception filters included, would run the record parse.
- **(b″) One `partial class WindowsProfile` across Core and Emission.** Rejected: one type initializer, so any target read would still
  run the record parse, and the type would straddle P13.
- **(c) Record only** (C/Emission owns it until R9). No code; leaves P2-P12 reading P13's folder and R9 still has to split, since its
  move cannot take what Binding and the Backend read. Rejected.

## Readers changed, deletions, size

| Commit | File | Change |
| --- | --- | --- |
| 1 | `Emission/WindowsProfile.cs` → `Core/WindowsProfile.cs` | git mv; keeps the target members; contract header (responsibility, no inputs, outputs, invariants, prohibition: no file, resource or toolchain read) |
| 1 | `Emission/ToolchainIdentity.cs` (new, within the structure limits) | the toolchain members, the record reader and `SequenceEqual`; the reader's checks of `Name`, `BackendLibrary` and `ProvidedSymbols` now qualify `WindowsProfile.`; contract header |
| 1 | NativeToolchain.cs (10), NativeToolchain.Verification.cs (7), EmissionArtifacts.cs (9), Kernel32Imports.cs (2), ToolchainResolver.cs (1); EmissionArtifactsTest (3), NativeToolchainTest (1), ToolchainResolverTest (1) | qualifier `WindowsProfile.` → `ToolchainIdentity.`; the 2 `RuntimeImports` readers |
| 1 | a Target test (`AbiTest` or the IrTarget tests' class) | the data-layout tie (N1) |
| 2 | Core/WindowsProfile.cs; Kernel32Imports.cs | `Kernel32Library`, `Kernel32Symbols` added; `LibraryName`, `Symbols` deleted |
| 2 | Binding.Attributes.cs:119, 122, 160 (BU reviews); NativeConfiguration.cs:52, 83; EmissionArtifacts.cs (3); NativeToolchain.cs (4); Kernel32ImportsTest (2) | qualifier → `WindowsProfile.Kernel32Library` / `Kernel32Symbols` |

No hold file. Expected net about +15 to +20 code lines (two contract headers, the symbols initializer, minus `RuntimeImports` and
`Kernel32Imports.Symbols`) and about +6 test lines. Both commits are behavior-preserving and land in one merge, so the DECISIONS rule
is true at landing. Docs: ARCH +1 row and the Backend row, DECISIONS +1 entry and 4 amendment lines, CODEMAP 1 row.

## Records and departures

- **DECISIONS** (next free R5-M): "`WindowsProfile`, the target profile (Core), and `ToolchainIdentity` (the native build)": the split;
  the rule (check, its configuration and target selection read only the target profile; only the native build and publication read
  the toolchain identity and `Kernel32Imports`; the record is checked against the profile); `RuntimeImports` and
  `Kernel32Imports.Symbols` deleted; the boundary with `IrTarget` and its test; rejected (a), (b′), (b″), (c). Departures, each with its
  amendment line: R5-M14 (the owner it left open); R5-M163 and R5-M167 row 15 ("`C/Emission` keeps the toolchain, publication and
  profile files until R9"); the reduction plan's table (`NativeToolchain*`, `EmissionArtifacts`, `Kernel32Imports`, `ToolchainResolver`,
  `Artifact*`, `WindowsProfile` to `src/Kimi/Build` at R9). Their reason, nothing moves twice, holds for the toolchain identity and does
  not apply to the target profile, which R9's move cannot take (P2-P12 would read P13). G11 §5 rejected moving the folder; it never
  considered a split. The owner approved the split on 2026-11-01, verbatim 「(b) 分割を認める」, departing from R5-M167 row 15 and the
  reduction plan's R9 row for the target half only; the DECISIONS entry cites it.
- **ARCH**: a canonical-component row "Target profile | `WindowsProfile`, the one executable target profile, windows-x64 (IMPL 21.4):
  … and the reserved supplies' names and symbols; constants only; not the checked triple (`Compilation.TargetTriple`, `IrTarget`) |
  `Core/WindowsProfile.cs`"; the Backend row's May read adds "Core's target profile (`WindowsProfile`)" (today unlisted). The
  structure-limit sentence (line 151) already says "toolchain and publication files" and needs no edit; line 289 is historical.
- **CODEMAP** row "IR and native artifacts": `WindowsProfile` (Core) and `ToolchainIdentity`. PLAN WP closes at the merge (planning
  session). No STATUS, SPEC or IMPL change.

## Checks for the implementation (all required)

- Non-incremental `-warnaserror` build (StyleCop order; the new files' structure limits).
- Behavior-preserving, against a base run in the same worktree: `-DiagnosticSnapshot -DiagnosticBaseline` 0 differences; the semantic
  snapshot with `KIMI_SEMANTIC_HIR=1` (Binding's qualifiers change); `compare-fixtures.ps1` identical `.ll`, sidecars and native results
  for `-Class MinimalEmissionTest -Fixtures 'Hello*.ll','Empty*.ll'` (every module's header and `#0` come from the profile).
- Unit classes: NativeToolchainTest, EmissionArtifactsTest, ToolchainResolverTest, Kernel32ImportsTest, LibraryEmissionTest,
  LibraryImportTargetBindingTest, DependencyConfigurationTest, MinimalEmissionTest, AbiTest, with ArchitectureRulesTest.
- `test-cli.ps1`, `test-manual-build.ps1` and a `-VerifyToolchain` run: the NativeToolchain.Verification success path, the
  allowed-undefined set, `BuildManifest` and the staged-hash check run only there.
- Session at the end (whole-solution build: Benchmark reads `WindowsProfile.Target`).

## Questions for the planning session

None: Q1 answered by the owner (above), Q2 (`ToolchainIdentity`) and Q3 ((d) in the unit, BU's review) by the planning session.
