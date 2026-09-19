## Kimigayo
Work in progress

### Check source semantics

```powershell
dotnet run --project Kimi -- check examples/Strings/main.kimi --Target x86_64-pc-windows-msvc
```

`check` accepts the same input paths as `emit` and requires no LLVM installation. It checks source semantics, including all Project dependency bodies, without generating artifacts or running the program. Direct dependency names and aliases resolve in each defining module's environment.

Run `restore <project.kimiproj>` before checking a project with dependencies. Restore resolves exact local Project references and writes deterministic product/test partitions to `<project>.kimi.lock.json`. Source commands validate the required lock and never rewrite it; `--locked` is supported. Source edits require rechecking, but no restore. Supported source-module bodies share final Application generation. Library `emit` produces inspection IR with no OS entry; Library native build/run, package inputs and full module/native supply records remain unfinished.

See the [transitive source dependency example](examples/SourceDependencies/README.md).

### Toolchain (Windows x64)

Use LLVM **22.1.8**, as specified in [profile.json](backend/windows-x64/profile.json).
Place `toolchain/` at the repository root, or beside `Kimi.exe` / `Kimi.dll` for a standalone installation.

For building Kimi applications (`kimi build`), include:

```text
toolchain/
  opt.exe
  llc.exe
  lld-link.exe
  llvm-nm.exe
  llvm-readobj.exe
  llvm-dlltool.exe
  <supporting LLVM DLLs, if required>
  windows_x64/
    kimi_backend_windows_x64_v1.lib
```

To also build the native backend library, add `clang.exe`, `llvm-lib.exe`, and
`llvm-objdump.exe` to `toolchain/`. No Windows SDK or extra Clang headers are required.
The backend archive and `llvm-dlltool.exe` must match the hashes in `profile.json`.

From the repository root, copy the tools and supporting DLLs from an existing LLVM
installation, then build, test, and install the backend library with:

```powershell
./backend/windows-x64/setup.ps1 -LlvmBin 'C:/path/to/LLVM/bin'
```

To rebuild the native library later, run `./backend/windows-x64/build.ps1`.
Building the C# compiler itself (`dotnet build`) does not require this LLVM toolchain.

### Add toolchain to PATH

From the repository root, run this in PowerShell to use the tools in the current session:

```powershell
$env:Path = "$(Join-Path $PWD 'toolchain');$env:Path"
```

For a permanent setting, open Windows **Environment Variables**, edit your user
**Path**, add the full path to `toolchain`, and open a new terminal.

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

### Using kimi

Use `kimi <command> [project.kimiproj | solution.kimisln | directory] [options]`.
Omitting the path searches the current directory for a solution or projects.

- `build`: compile and link; requires the LLVM toolchain above.
- `run`: run the existing executable and forward its output and exit code. Run
  `build` first, and again after source changes; `run` does not rebuild or require LLVM.
- `emit`: write LLVM IR (`.ll`) and a link manifest (`.link.json`) without
  invoking LLVM or linking.

Examples from the repository root, with `kimi` available on `PATH`:

```powershell
kimi build examples/Hello/Hello.kimiproj
kimi run examples/Hello/Hello.kimiproj
kimi emit examples/Hello/Hello.kimiproj
kimi build examples/Hello/Hello
kimi emit examples/Hello/Hello.kimi
kimi build examples/Hello --ToolchainRoot 'C:/tools/kimi/toolchain'
kimi run examples/Hello/bin/x86_64-pc-windows-msvc/Hello.O2.exe
```

The direct `.exe` form of `run` needs no project or build record.
For `build`, `emit`, and `run`, an extensionless input `A` selects the exact path
first, then `A.kimiproj`, then `A.kimi`. Explicit extensions select only that file.
A selected `.kimi` becomes an in-memory Application project using only that source,
the host target (currently Windows x64), and O2. No `.kimiproj` is created and sibling
sources are not included. `--Target` overrides the implicit target. Artifacts go to
`bin/<target>/` beside the source; `run A.kimi` executes its existing build without
compiling it. Invalid selected inputs fail without trying another candidate.

Each loaded project prints a one-line summary of its name, project/source file,
Targets, OutputKind, and Optimization before the command proceeds. Implicit projects
are marked `implicit`; an explicit `--Target` selection is shown separately.

For a source checkout, build the compiler with .NET 10 and replace `kimi` in the
examples with `dotnet Kimi/bin/Release/net10.0/Kimi.dll`:

```powershell
dotnet build Kimi/Kimi.csproj -c Release
dotnet Kimi/bin/Release/net10.0/Kimi.dll build examples/Hello/Hello.kimiproj
dotnet Kimi/bin/Release/net10.0/Kimi.dll run examples/Hello/Hello.kimiproj
```
