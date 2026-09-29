# Kimigayo

Kimigayo is a pre-alpha programming language for AI, with a C# compiler, a core library written in Kimigayo and a VS Code extension. Native execution currently targets Windows x64. See [STATUS.md](docs/STATUS.md) for verified capabilities and remaining limitations.

- [Repository layout](#repository-layout)
- [Getting started](#getting-started)
- [Using kimi](#using-kimi)
- [Toolchain setup](#toolchain-windows-x64)
- [Development and verification](#development-and-verification)
- [Visual Studio Code](#visual-studio-code)

## Repository layout

| Folder | Purpose |
| --- | --- |
| `src/` | Product code and supporting development projects. |
| `src/Kimi/` | C# compiler, command-line interface and language server. |
| `src/Kimi/Library/` | Core library implemented in Kimigayo and embedded in the compiler. |
| `src/Benchmark/` | Performance benchmarks and reviewed measurement data. |
| `src/Playground/` | Small programs for compiler development and experiments. |
| `src/kimi-ext/` | VS Code extension source, tests and packaging configuration. |
| `src/backend/` | Native backend sources, target profiles and verification scripts. |
| `src/third_party/` | Vendored source code and its licenses. |
| `tests/` | Regression tests and milestone acceptance programs. |
| `tests/xUnitTest/` | Managed regression tests and test data. |
| `tests/milestones/` | Programs that define end-to-end implementation milestones. |
| `docs/` | Specifications, guides, library reference and development records. |
| `docs/spec/` | Language specification chapters, indexed by [SPEC.md](docs/SPEC.md). |
| `docs/impl/` | Implementation contracts, indexed by [IMPL.md](docs/IMPL.md). |
| `docs/examples/` | Runnable examples and their instructions. |
| `docs/dev/` | Current plan, session history and development prompts. |
| `scripts/` | Repository-wide verification entry point. |
| `draft/` | Proposals. Integrated proposals remain frozen. |
| `.github/` | CI and package publishing workflows. |
| `.vscode/` | Shared VS Code tasks and extension debugging configuration. |
| `toolchain/` | Locally installed LLVM tools, backend and shared kernel32 import library. |
| `temp/` | Disposable scripts, shared fixtures and intermediate build or test files. |
| `artifacts/` | Retained verification evidence, measurements and distribution packages. |

The root contains the solution, shared build settings, license and contributor instructions in [AGENTS.md](AGENTS.md). Public library declarations are listed in [LIBRARY.md](docs/LIBRARY.md); coding conventions are in [STYLE.md](docs/STYLE.md).

### Generated files and retention

`temp/`, `artifacts/` and `toolchain/` are excluded from Git. You may delete `temp/` when no build or test is running; scripts recreate the directories they need. Keep `artifacts/` when retaining verification history or release packages. Git does not back it up.

- `artifacts/verify/`: logs, test results, source identities and replay inputs. Milestone bundles retain their reports and the programs and outputs needed to investigate a result.
- `artifacts/benchmarks/`: generated measurements and comparison reports.
- `artifacts/packages/`: Published Kimi binaries, NuGet packages and VSIX files.
- `artifacts/backend/`: native backend candidates and verification reports.
- `artifacts/legacy/`: older records retained during the output-directory migration.
- `artifacts/migrations/`: inventories mapping old output locations to their new locations. Existing raw logs keep their original paths as historical evidence.

Each .NET project keeps its standard `bin/` and `obj/` directories. Kimi applications also keep their documented project-local `bin/` output paths. These are separate from the repository's `temp/` and `artifacts/` directories.

## Getting started

Install the .NET 10 SDK selected by [global.json](global.json). Run the following commands from the repository root:

```powershell
dotnet restore Kimigayo.slnx
dotnet build src/Kimi/Kimi.csproj -c Release
dotnet src/Kimi/bin/Release/net10.0/Kimi.dll check docs/examples/Strings/main.kimi --Target x86_64-pc-windows-msvc
```

Checking source and emitting LLVM IR do not need LLVM installed. To build and run a native program, prepare the [Windows x64 toolchain](#toolchain-windows-x64), then run:

```powershell
dotnet src/Kimi/bin/Release/net10.0/Kimi.dll run docs/examples/Hello/Hello.kimiproj
```

## Using kimi

### Build, run and emit

Use `kimi <command> [project.kimiproj | solution.kimisln | directory] [options]`.
Omitting the path searches the current directory for a solution or projects.

- `build`: compile and link; requires the LLVM toolchain above.
- `run`: build the current source, then run the resulting executable and forward
  its output and exit code. Requires the LLVM toolchain; a failed build does not run.
  Add `--no-build` to run the existing executable without compiling or requiring LLVM.
- `emit`: write LLVM IR (`.ll`) and a link manifest (`.link.json`) without
  invoking LLVM or linking.

Examples from the repository root, with `kimi` available on `PATH`:

```powershell
kimi build docs/examples/Hello/Hello.kimiproj
kimi run docs/examples/Hello/Hello.kimiproj
kimi run docs/examples/Hello/Hello.kimiproj --no-build
kimi emit docs/examples/Hello/Hello.kimiproj
kimi build docs/examples/Hello/Hello
kimi emit docs/examples/Hello/Hello.kimi
kimi build docs/examples/Hello --ToolchainRoot 'C:/tools/kimi/toolchain'
kimi run docs/examples/Hello/bin/x86_64-pc-windows-msvc/Hello.O2.exe
```

The direct `.exe` form of `run` executes that binary without building and needs no
project, build record or LLVM toolchain.
For `build`, `emit`, and `run`, an extensionless input `A` selects the exact path
first, then `A.kimiproj`, then `A.kimi`. Explicit extensions select only that file.
A selected `.kimi` becomes an in-memory Application project using only that source,
the host target (currently Windows x64), and O2. No `.kimiproj` is created and sibling
sources are not included. `--Target` overrides the implicit target. Artifacts go to
`bin/<target>/` beside the source; `run A.kimi` builds that source before executing
it. Invalid selected inputs fail without trying another candidate.

Each loaded project prints a one-line summary of its name, project/source file,
Targets, OutputKind, and Optimization before the command proceeds. Implicit projects
are marked `implicit`; an explicit `--Target` selection is shown separately.

For a source checkout, replace `kimi` in these examples with `dotnet src/Kimi/bin/Release/net10.0/Kimi.dll`.

### Check source and restore dependencies

`check` accepts the same input paths as `emit` and requires no LLVM installation. It checks source semantics, including all Project dependency bodies, without generating artifacts or running the program. Direct dependency names and aliases resolve in each defining module's environment.

Run `restore <project.kimiproj>` before checking a project with dependencies. Restore resolves exact local Project references and writes deterministic product/test partitions to `<project>.kimi.lock.json`. Source commands validate the required lock and never rewrite it; `--locked` is supported. Source edits require rechecking, but no restore. Supported source-module bodies share final Application generation. Library `emit` produces inspection IR with no OS entry; Library native build/run, package inputs and full module/native supply records remain unfinished.

See the [transitive source dependency example](docs/examples/SourceDependencies/README.md).

The [UTF-8 formatting example](docs/examples/Utf8Formatting/README.md) covers owning interpolation, user formatters, short-circuit writes and allocation-free fixed buffers.

### Native library requirements

A module declares the native libraries its `#LibraryImport` functions need per target.
Checking reads only these settings, never the `.lib` files:

```text
NativeRequirements=
  "x86_64-pc-windows-msvc"=
    codec={ Kind="static" ContractId="example.codec.v1" }
```

A `NativeLibraries` entry of the same project declares the requirement and its supply
together (`Kind` is required there unless a matching requirement supplies it; overlapping
`Kind`, `ContractId` and `Sha256` values must agree):

```text
NativeLibraries=
  "x86_64-pc-windows-msvc"=
    observer={ Kind="static" Input="native/observer.lib" }
```

`kernel32` and `kimi_backend` are reserved and need no entry; a `kernel32` entry is an
error, and an import of a reserved supply is limited to its catalog: the reviewed
project-owned kernel32 definition or the backend's provided symbols.
The old `NativeBindings` setting is rejected with migration guidance. Imports are
checked for requirement names, declaration shape and the initial Windows C ABI Types.
Declarations of one external symbol must also agree on their physical signature and on
the requirement `Kind` that selects dllimport generation, and an import of a kernel32 API
the runtime itself declares must use the reserved `kernel32` supply with that exact
signature. Calls inside an `unsafe` block are generated for integer, `f32`/`f64` and
raw-pointer signatures, with a `dllimport` declaration for `import` supplies. `build`
links the project's own `NativeLibraries` supplies that its imports require and checks
`Sha256` assertions against a staged copy that is linked instead of the original;
actual archive member kinds are not checked yet. Raw pointers can be passed, returned,
stored, compared with `==`/`!=` (including `null`), converted with `@` to other pointer
Types or `usize`, and displaced with `p + n`, `p - n`, `p += n` or `p -= n` (`n: isize`);
`*p` reads and writes non-`bool` scalar and pointer pointees; compound writes through
pointers, indexing and C layout are not generated yet. Any external symbol spelling is
supported, including MSVC-mangled names; supplies required by dependency modules are not.

A target may instead list `Name`/`Input` records. A record with `Package=` supplies a
native name required by that dependency module; it cannot declare `Kind`, and when the
module's requirement has a `ContractId` the record must assert the same one (a `Sha256`
assertion must also agree). Several supplies of one module's name, from any project in the
graph, must not assert different `ContractId` or `Sha256` values. A supply for a name that
module does not require stays unused:

```text
NativeLibraries=
  "x86_64-pc-windows-msvc"=
    {
      Package={ PackageId="example.codec" PackageVersion="1.0.0" }
      Name="codec"
      ContractId="example.codec.v1"
      Input="native/codec.lib"
    }
    { Name="observer" Kind="static" Input="native/observer.lib" }
```

## Toolchain (Windows x64)

Use LLVM **22.1.8**, as specified in [profile.json](src/backend/windows-x64/profile.json).
Place `toolchain/` at the repository root, or beside `Kimi.exe` / `Kimi.dll` for a standalone installation.

For building Kimi applications (`kimi build`), include:

```text
toolchain/
  clang.exe
  opt.exe
  llc.exe
  lld-link.exe
  llvm-nm.exe
  llvm-readobj.exe
  llvm-dlltool.exe
  llvm-lib.exe
  llvm-objdump.exe
  <supporting LLVM DLLs, if required>
  installation.json
  windows_x64/
    kimi_backend_windows_x64_v1.lib
    kernel32.def
    kernel32.lib
    kernel32.json
```

Setup and explicit verification use the complete tool set above. Ordinary builds
need only the build tools and installed libraries. No Windows SDK or extra Clang headers are required.
The backend archive and `llvm-dlltool.exe` must match the hashes in `profile.json`.

From the repository root, copy the tools and supporting DLLs from an existing LLVM
installation, then build, test, and install the backend library with:

```powershell
./src/backend/windows-x64/setup.ps1 -LlvmBin 'C:/path/to/LLVM/bin'
```

To rebuild the native library later, run `./src/backend/windows-x64/build.ps1`.
Setup also installs `windows_x64/kernel32.lib` and its generation metadata. Ordinary builds reuse it without tool identity probes or regeneration; a missing library requires setup. To recheck an installation, run `kimi toolchain verify`.

Building the C# compiler itself (`dotnet build`) does not require this LLVM toolchain.

### Toolchain path resolution

Kimigayo selects the toolchain root in this order:

1. `--ToolchainRoot <path>`.
2. The `KIMI_TOOLCHAIN_ROOT` environment variable.
3. An existing `toolchain/` beside `Kimi.exe` / `Kimi.dll`.
4. For source builds, the repository's `toolchain/`, found by walking up from the
   compiler directory.
5. Otherwise, `toolchain/` beside the compiler (the expected installation location).

Relative CLI/environment paths use the current working directory. Explicit roots
do not fall back if missing. Automatic discovery does not search the user's project,
working directory, or `PATH`; adding LLVM to `PATH` is only for direct tool use.

LLVM executables come directly from the selected root. `--LlvmBin <path>` overrides
their location, followed by the project's `LlvmBin` setting (relative to the project).
These overrides do not change the backend root: the default library is
`<root>/windows_x64/kimi_backend_windows_x64_v1.lib`. Explicit backend inputs override
that default and resolve relative to the link manifest.

### Add toolchain to PATH

From the repository root, run this in PowerShell to use the tools in the current session:

```powershell
$env:Path = "$(Join-Path $PWD 'toolchain');$env:Path"
```

For a permanent setting, open Windows **Environment Variables**, edit your user
**Path**, add the full path to `toolchain`, and open a new terminal.

## Development and verification

To publish a Windows x64 NativeAOT Release build, run `./scripts/build-kimi.ps1`.
It requires the .NET SDK and Windows NativeAOT build prerequisites, and writes to
`artifacts/packages/kimi/win-x64/` with debug symbols and XML documentation disabled.
Both build scripts resolve paths from their own location and stop on command failures.

Restore dependencies with `dotnet restore Kimigayo.slnx` after a fresh checkout or a project change. Use the verification script for changes; a direct `dotnet build` is not verification evidence.

```powershell
# Focused verification for one change.
./scripts/verify.ps1 -Class XunitTest.ToolchainResolverTest

# Include the related native fixtures and milestone when needed.
./scripts/verify.ps1 -Class XunitTest.ContainerNestingTest -Fixtures 'ContainerNestingExample.ll' -Milestone 1

# Once at session end: Release build and all managed tests.
./scripts/verify.ps1 -Mode Session

# Debug is opt-in in either mode.
./scripts/verify.ps1 -Configuration Debug -Class XunitTest.ToolchainResolverTest
./scripts/verify.ps1 -Mode Session -Configuration Debug
```

Milestones use only original checked-in programs, building and directly executing each once at O0 and O2. Dedicated feature tests cover variants/rejections; CLI tests cover `run` and `--no-build`. Toolchain identity is checked at setup/update or by `kimi toolchain verify` (`--Report <path>` saves evidence). Add `-VerifyToolchain` to run it once before tests; ordinary verification records it as not performed.

Both modes default to Release and use one compiler configuration for the build, tests, fixtures and milestone harnesses. Add `-Fixtures` and `-Milestone` to a session run for the native checks relevant to the change. These checks require the LLVM toolchain and retain O0/O2 coverage regardless of the compiler configuration. NativeAOT tests are separate and are not run by these commands.

LSP tests require directory-listing access from the OS temporary directory through every
ancestor to the filesystem root: project discovery must distinguish an unreadable directory
from one containing no projects (SPEC §23.4.3). Session verification and selections of the
implicit-source LSP test classes check this access before building. If the preflight fails,
inspect `lsp-discovery-access.log` in the evidence directory and rerun in a process with the
required access, outside a restricting sandbox. Do not treat an unreadable directory as empty
or skip the tests; otherwise missing diagnostics appear as assertion failures or timeouts.

Each run records results in `artifacts/verify/<run>/`, including failed runs. Native scratch files go to `temp/verify/<run>/`. Fixture inputs and their expected results stay with the evidence so they can be replayed after scratch files are deleted. Do not edit sources or remove working directories during verification.

The [current plan](docs/dev/PLAN.md) describes ongoing work; [session history](docs/dev/PLAN_HISTORY.md) links to retained evidence. Extension-specific checks are listed under [Visual Studio Code](#extension-development).

## Visual Studio Code

The [kimi-ext](src/kimi-ext/) extension provides diagnostics and build/run/check commands. It shares the compiler and toolchain described in this README.

### QuickStart

1. Install .NET 10, Node.js LTS and VS Code 1.138 or later (1.x). From the repository root, build the compiler and [prepare the toolchain](#toolchain-windows-x64) for native builds:

   ```powershell
   dotnet build src/Kimi/Kimi.csproj -c Release
   ./src/backend/windows-x64/setup.ps1 -LlvmBin 'C:/path/to/LLVM/bin'
   ```

   Use `src/Kimi/bin/Release/net10.0/Kimi.exe` with its accompanying files. Diagnostics and Check need no LLVM tools. Standalone installations keep `toolchain/` beside the executable, or set `KIMI_TOOLCHAIN_ROOT` before starting VS Code.

2. Package and install the extension from the repository root:

   ```powershell
   ./scripts/install-extension.ps1
   ```

   The script runs `build-extension.ps1`, then installs `artifacts/packages/kimi-ext.vsix` using the VS Code `code` CLI on PATH. Use `-CodeCommand 'C:/path/to/VS Code/bin/code.cmd'` for another installation, or `-ExtensionsDirectory 'C:/path/to/test-extensions'` for an isolated extension directory. Run `./scripts/build-extension.ps1` when only a VSIX is needed. Both scripts keep the current version; locked npm dependencies include the packaging tool. Packaging compiles the extension and includes this section and the root MIT license. After updating, run **Developer: Reload Window** in VS Code.

3. Run **Preferences: Open User Settings (JSON)** and set your actual compiler path:

   ```json
   {
     "kimi.serverPath": "C:/path/to/Kimigayo/src/Kimi/bin/Release/net10.0/Kimi.exe",
     "kimi.runBuilds": true
   }
   ```

   Current Kimi builds sources in `run`, so enable `kimi.runBuilds` to avoid a separate build. Leave it `false` only for older executables whose `run` requires an existing build.

4. Open a trusted source folder and a saved `.kimi` or `.kimiproj`. Press **Ctrl+F5** or **F1 > Kimi: Build and Run**. If asked, choose **Kimi (Run Without Debugging)**. An unambiguous target runs without a picker; `.kimiproj` is optional. Diagnostics appear in the editor and Problems panel.

### Commands and targets

With the settings above:

| F1 command | Action |
| --- | --- |
| Kimi: Build | Save inputs and build. |
| Kimi: Run / Build and Run | Save inputs, then invoke `kimi run` once to build and run. |
| Kimi: Check | Save inputs and check without building. |
| Kimi: Select Target and ... | Choose a target explicitly for any of the four actions. |

Palette commands and Ctrl+F5 prefer the active `.kimiproj`, then a project beside the active source, then a project in the active editor's workspace folder. Without an active folder, all workspace folders and open project tabs are candidates. One project is selected automatically; multiple projects show a picker. With no project, the active saved `.kimi` is used. Outside-workspace sources search only their own directory. **Select Target and ...** always allows a manual choice or Browse; no selection is remembered.

The editor play button and **Kimi** context menus use the clicked file exactly. A single `.kimi` includes only that source. Build and Run keeps one target throughout execution.

Output and input use task terminals. **Stop / Shift+F5** stops a Ctrl+F5 session; **Tasks: Terminate Task** stops palette tasks. Commands sharing a target directory cannot overlap. Execution is supported; breakpoints, stepping and application arguments are not.

Ctrl+F5 needs no `launch.json`. If another language's configuration is selected, choose a **Kimi: Build and Run** configuration instead. Its optional `program` accepts an exact `.kimi` or `.kimiproj` path, `${file}`, or a path relative to its workspace folder. Omit it for automatic selection.

### Settings and troubleshooting

- **`kimi.serverPath`**: absolute executable path or a name on PATH, without arguments. Invalid paths and server errors offer **Open Settings** and **Show Output** once per unchanged setting during an extension session, shared by diagnostics and build/run/check commands. Repeated failures remain in the **Kimi** Output channel. Changing the setting allows a new notification and restarts the server; **Kimi: Restart Language Server** retries the same setting without repeating the popup. A failed connection does not automatically restart in a loop.
- **`kimi.runBuilds`**: enable for current Kimi. The compatibility default is `false`, which uses `build` then `run` for Build and Run and leaves Run's behavior to the executable.
- **`kimi.trace.server`**: `off` (default), `messages`, or `verbose`; see the **Kimi** Output channel.

Before compilation, the extension saves the selected source, or all dirty open Kimi files for a project. Untitled files are skipped. VS Code separately saves editors through its task/debug settings; `task.saveBeforeRun: "never"` disables task-wide saving for all extensions.

Tasks need an open folder; diagnostics also work in standalone editor windows. Avoid `%` in native build paths with the verified Windows linker. Completion, hover, navigation and syntax highlighting are not included. Kimi.exe manages the toolchain.

VS Code 1.139.1 can emit `DEP0169` (`url.parse()`) from its own CLI marketplace metadata request after installing a local VSIX ([upstream issue](https://github.com/microsoft/vscode/issues/326998)). This is outside the extension; the install script preserves the warning while awaiting an upstream fix.

The `vsce` 4.0.0 message that `extension.js` is **large** means that this one file accounts for more than 85% of the unpacked package, not that it exceeds an absolute size limit. A minified single bundle naturally dominates this small VSIX (about 97 KB compressed). Keep the bundle; splitting it or adding filler only to change this ratio would not improve loading or download size.

### Extension development

Run from the repository root:

```powershell
npm --prefix src/kimi-ext ci
npm --prefix src/kimi-ext test
$env:KIMI_TEST_SERVER_PATH = (Resolve-Path src/Kimi/bin/Release/net10.0/Kimi.exe).Path
npm --prefix src/kimi-ext run test:integration
```

Integration tests use isolated VS Code profiles. Set `VSCODE_EXECUTABLE_PATH` to reuse an installed VS Code; otherwise the test runner downloads it. Compiler-dependent cases are skipped without `KIMI_TEST_SERVER_PATH`. Open this repository and select **Kimi Extension** in Run and Debug to launch its development host.

The npm override for Mocha selects supported `glob` 13 while `@vscode/test-cli` still depends on Mocha 11. Keep integration tests passing when updating this override, and remove it when the upstream dependency no longer selects deprecated `glob` 10. Packaging uses the locally installed, locked `@vscode/vsce` rather than an independent `npx` download. The prepublish step bundles the extension and its runtime dependencies with esbuild, retaining license notices; the VSIX excludes tests, build tools and `node_modules`.

Edit this README section and the root `LICENSE`; packaging generates ignored copies under `src/kimi-ext/`. For a new release, run `npm --prefix src/kimi-ext run version:patch` once (updates the package and lockfile without a Git tag), update `src/kimi-ext/CHANGELOG.md`, test and package with `npm --prefix src/kimi-ext run package`. Only the final number increments automatically; major/minor changes require explicit user instruction. See [maintenance rules](src/kimi-ext/AGENTS.md).
