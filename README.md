## Kimigayo
Work in progress

### Check source semantics

```powershell
dotnet run --project Kimi -- check examples/Strings/main.kimi --Target x86_64-pc-windows-msvc
```

`check` accepts the same input paths as `emit` and requires no LLVM installation. It checks source semantics, including all Project dependency bodies, without generating artifacts or running the program. Direct dependency names and aliases resolve in each defining module's environment.

Run `restore <project.kimiproj>` before checking a project with dependencies. Restore resolves exact local Project references and writes deterministic product/test partitions to `<project>.kimi.lock.json`. Source commands validate the required lock and never rewrite it; `--locked` is supported. Source edits require rechecking, but no restore. Supported source-module bodies share final Application generation. Library `emit` produces inspection IR with no OS entry; Library native build/run, package inputs and full module/native supply records remain unfinished.

See the [transitive source dependency example](examples/SourceDependencies/README.md).

The [UTF-8 formatting example](examples/Utf8Formatting/README.md) covers owning interpolation, user formatters, short-circuit writes and allocation-free fixed buffers.

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

### Native requirements

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
