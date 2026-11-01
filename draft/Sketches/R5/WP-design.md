# WP design: owners of `WindowsProfile` (R5 U8)

Base `abc7e28c7` (dev). Design only; implementation waits for the planning session's GO. Major (a new component, a moved one);
behavior-preserving. PLAN WP; census U8; R5-M14 left the owner open; R5-M167 row 15 kept "the toolchain, publication and profile files" in `C/Emission` until R9.

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
| Toolchain | `LlvmVersion` (20.3.2's pinned release), `BackendPackageId`, `BackendAbiVersion`, `BackendVersion` (= `CompilerRelease.Version`), `BackendSha256`, `BackendFile`, `Kernel32DefinitionSha256`, `DlltoolSha256`; the static constructor reading `profile.json` (resource `Kimi.WindowsBackendProfile.json`) and cross-checking it against the target facts; `SequenceEqual` | NativeToolchain(.Verification), EmissionArtifacts (manifest; R5-M165's missing-record check), Kernel32Imports, ToolchainResolver; EmissionArtifactsTest, NativeToolchainTest, ToolchainResolverTest |

Found while reading: `check` runs the toolchain half. Binding's reads of `ProvidedSymbols` and `RuntimeDeclarations` (static readonly) run the
type initializer, which parses the embedded record; the Kimi library's `#LibraryImport("kernel32", …)` (`Windows.kimi`) makes that every
check. Binding also reads `Kernel32Imports.Symbols`, whose initializer hashes `Kernel32.def` and reads `DlltoolSha256`. Not measured; one
parse per process, and a defect in the record would fail `check` with a TypeInitializationException.

## Owners (AGENTS: one owning component; ARCH: phases and dependencies)

- **Target profile → Core** (`Core/WindowsProfile.cs`, class `Kimi.Compiler.WindowsProfile`, name, namespace and member visibility kept).
  Binding (P2-P5), the Backend (P11-P12), target selection (Project, Solution, LSP) and the native build all read it; Core already holds
  what the phases share (`Compilation`, `SourceRef`, `SourceLocationTable`, which ARCH's Backend row lets the Backend read) and references
  nothing of `C/Emission` (comments only), so Binding → Core, Backend → Core and Emission → Core add no cycle. No reader of the target half changes: Binding.Attributes.cs (BU's),
  Lsp, SolutionAndProject, Backend, Benchmark and the 90 test files compile unchanged. Its type initializer only builds two arrays.
- **Toolchain identity → the native build** (`Emission/ToolchainIdentity.cs`, class `ToolchainIdentity`): only P13 reads it (build,
  publication's manifest, toolchain verify, kernel32 import generation), so it stays in `C/Emission`, the build boundary R9 moves to
  `src/Kimi/Build` (reduction plan, its R9 row). The record reader moves verbatim and checks the record against `WindowsProfile`.
- `profile.json`, its resource name and the scripts (`Read-KimiWindowsProfile`, build.ps1, kernel32.ps1) are unchanged: no hash moves,
  so no generator runs. The record keeps its target copies (profile, library, providedSymbols) for the scripts, cross-checked as today.

## Options

- **(a) Move the file to Core unchanged.** No reader edit. Core would own P13's record parse and hashes, R9 would move them out of
  Core again (the second move R5-M167 row 15 avoids), and check keeps parsing the record. Line 34 (210 characters) breaks Core's
  160-character limit, so the move is not even byte-identical. Rejected.
- **(b) Split: target profile in Core, toolchain identity in the native build. Recommended.** It also deletes `RuntimeImports`, a
  second copy of `RuntimeDeclarations`' symbol column (the 210-character line): `Kernel32Imports.Symbols` and NativeToolchain's
  allowed-undefined set read the declarations' symbols.
- **(b′) The toolchain half as a `NativeToolchain` partial** (`NativeToolchain.Supply.cs`). Rejected: four components read it
  (publication, kernel32, resolver, build), and moving the record's static constructor into `NativeToolchain` (none today) would make
  it precise-init, so every member, the `IsToolchainFailure` exception filters included, would run the record parse.
- **(c) Record only** (C/Emission owns it until R9). No code; leaves P2-P12 reading P13's folder and R9 still has to split, since its
  move cannot take what Binding and the Backend read. Rejected.
- **(d) The kernel32 sibling (same cause).** `Kernel32Imports.LibraryName` and `Symbols` are the kernel32 reserved supply's name and
  symbol set (IMPL 20.4, read at native resolution by Binding and by NativeConfiguration) beside its generation identity (`.def` text,
  its hash, the dlltool hash). Moving them into the target profile needs BU's agreement (Binding.Attributes.cs:119, 122, 160: three
  qualifiers) and edits NativeConfiguration.cs:52, 83. Until then check still initializes `Kernel32Imports` and, through
  `DlltoolSha256`, parses the record. Proposed: U8's second commit if BU agrees, else a PLAN open issue.

## Readers changed, deletions, size (option (b))

| File | Change |
| --- | --- |
| `Emission/WindowsProfile.cs` → `Core/WindowsProfile.cs` | git mv; keeps the target members; contract header (responsibility, inputs none, outputs, invariants, prohibition: no file, resource or toolchain read) |
| `Emission/ToolchainIdentity.cs` (new) | the toolchain members, the constructor and `SequenceEqual`, moved verbatim; contract header |
| NativeToolchain.cs (10), NativeToolchain.Verification.cs (7), EmissionArtifacts.cs (9), Kernel32Imports.cs (2), ToolchainResolver.cs (1) | qualifier `WindowsProfile.` → `ToolchainIdentity.` on toolchain members; `RuntimeImports` readers (2 lines) |
| EmissionArtifactsTest (3), NativeToolchainTest (1), ToolchainResolverTest (1) | the same qualifier |

All files are R5's or unowned (Emission, tests); none is a hold file. Expected net about +10 lines (two contract headers, minus
`RuntimeImports`); 34 line-neutral qualifier occurrences and the 2 `RuntimeImports` reader lines. Docs: ARCH +1 row and 2 edits,
DECISIONS +1 entry and 3 amendment lines, CODEMAP 1 row. One behavior-preserving commit (with (d), a second one).

## Records

- **DECISIONS** (next free R5-M): "`WindowsProfile`, the target profile (Core), and `ToolchainIdentity` (the native build)": the
  split above; the rule (compile phases and target selection read the target profile; only the native build and publication read the
  toolchain identity; the record is checked against the profile); `RuntimeImports` deleted; rejected (a), (b′), (c); (d)'s state.
  Amends R5-M14 (the owner it left open), and R5-M163's and R5-M167 row 15's "`C/Emission` keeps the … profile files": the reason,
  nothing moves twice, holds for the toolchain identity and does not apply to the target profile, which R9's move to `src/Kimi/Build`
  cannot take (P2-P12 would read P13); it also narrows the reduction plan's R9 row to the toolchain identity.
- **ARCH**: a canonical-component row "Target profile | `WindowsProfile`, the windows-x64 target profile (IMPL 21.4): … constants
  only | `Core/WindowsProfile.cs`"; the Backend row's May read adds "Core's target profile (`WindowsProfile`)" (today unlisted); the
  structure-limit sentence's "toolchain, publication and profile files left in `C/Emission`" becomes "toolchain and publication files"
  (the toolchain identity among them). The R6-gen progress line (historical) stays.
- **CODEMAP** row "IR and native artifacts": `WindowsProfile` (Core) and `ToolchainIdentity`. PLAN WP closes at the merge (planning
  session). No STATUS, SPEC or IMPL change.

## Checks for the implementation

- Non-incremental `-warnaserror` build (StyleCop order in the new files; Core's 160-character limit).
- Behavior-preserving, against a base run in the same worktree: `-DiagnosticSnapshot -DiagnosticBaseline` 0 differences;
  `compare-fixtures.ps1` identical `.ll`, sidecars and native results for `-Class MinimalEmissionTest -Fixtures 'Hello*.ll','Empty*.ll'`
  (every module's header and `#0` come from the profile). No semantic snapshot: no Binding or MIR code changes.
- Unit classes: NativeToolchainTest, EmissionArtifactsTest, ToolchainResolverTest, Kernel32ImportsTest, LibraryEmissionTest,
  MinimalEmissionTest, AbiTest, with ArchitectureRulesTest. CLI harnesses: not required (no path or hash moves); `test-cli.ps1` once
  is cheap evidence that `build --Manifest` and `toolchain verify` read the moved members.
- Session at the end (whole-solution build: Benchmark reads `WindowsProfile.Target`).

## Questions for the planning session

1. R5-M167 row 15 was the owner's ruling: amend it under this entry, or ask the owner (non-blocking) first?
2. The name `ToolchainIdentity` (alternatives: `NativeSupply`, after the record's own error text).
3. (d): ask BU now and land it as U8's second commit, or record it as an open issue?
