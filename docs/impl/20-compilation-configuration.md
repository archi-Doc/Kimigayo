# 20. Compilation configuration

[Implementation specification index](../IMPL.md)

This chapter defines the projects, commands, outputs and test execution of the windows-x64 profile.

## 20.1. Projects and sources

A **Project** (`.kimiproj`) defines one Application or Library and its Kotonoha. A **Solution** (`.kimisln`) lists Projects and may supply a default `LangVersion`. A Compilation compiles one Project for one target and one Debug setting.

- **Product sources** are the regular `*.kimi` files directly in the directory of the `.kimiproj`; subdirectories are not searched. No other file joins a Project.
- Files listed in `TestSources` are not product sources; they are test-only ([§18.6](../spec/18-modules-and-dependencies.md#186-product-and-test-inputs)). Entries are project-relative paths inside the project directory and may name files in subdirectories. Globs, malformed paths and duplicates are errors.
- A source's **logical name** is its project-relative path with `/` separators. Logical names must be NFC, and two names equal under simple case folding are an error.

```text
App/App.kimiproj
App/Main.kimi          product source, logical name Main.kimi
App/sub/Helper.kimi    not a source unless listed in TestSources
```

## 20.2. Settings

The surface syntax of `.kimiproj` and `.kimisln` is implementation-defined. An unknown or duplicate setting or an ill-typed value is an error.

| Setting | Type | Default | Owner |
| --- | --- | --- | --- |
| OutputKind | `Application` or `Library` | `Application` | [§20.3.2](#2032-builds-and-artifacts) |
| Targets | target triples | required | [§20.2.1](#2021-targets-and-compile-time-settings) |
| PackageId, PackageVersion | strings | none | [§18.4.1](../spec/18-modules-and-dependencies.md#1841-identities-and-reference-names) |
| LangVersion | version | see owner | [§20.2.2](#2022-language-version) |
| Alias | default aliases | none | [§18.2.3](../spec/18-modules-and-dependencies.md#1823-default-aliases) |
| CompileTimeSettings | Name to `bool`, `i64` or `string` | empty | [§20.2.1](#2021-targets-and-compile-time-settings) |
| Dependencies, PackageSources | dependency records | empty | [§18.4](../spec/18-modules-and-dependencies.md#184-dependencies) |
| TestSources | project-relative paths | empty | [§20.1](#201-projects-and-sources) |
| TestDependencies | as Dependencies | empty | [§18.6](../spec/18-modules-and-dependencies.md#186-product-and-test-inputs) |
| TestProjectId | nonempty string | none | [§20.6.2](#2062-options-settings-and-identity) |
| Test | case settings | see owner | [§20.6.2](#2062-options-settings-and-identity) |
| NativeRequirements, NativeLibraries | per-target records | empty | [§20.4](#204-native-libraries) |
| Optimization | `O0` or `O2` | `O2` | [§20.3.2](#2032-builds-and-artifacts) |
| OutputPath | `.ll` path | `bin/<target>/<Name>.ll` | [§20.3.2](#2032-builds-and-artifacts) |

### 20.2.1. Targets and compile-time settings

The windows-x64 profile has one target, `x86_64-pc-windows-msvc`; other targets follow [§22](../spec/22-core-execution-and-foreign-functions.md#22-kimi-program-execution-and-foreign-functions). Project `Targets` and a dependency's allowed targets compare as exact, case-sensitive strings.

The built-in Conditions `os` and `arch` ([§19.2](../spec/19-compile-time-directives.md#192-conditions)) come from the triple's OS and architecture, with any OS version suffix removed:

| Triple component | Value |
| --- | --- |
| OS `windows`, `win32` | `windows` |
| OS `linux` | `linux` |
| OS `macos`, `macosx`, `darwin` | `macos` |
| any other OS | `unknown` |
| arch `x86_64`, `amd64` | `x86_64` |
| arch `i386`, `i486`, `i586`, `i686`, `x86` | `x86` |
| arch `aarch64`, `arm64` | `aarch64` |
| arch `riscv64` | `riscv64` |
| any other arch | `unknown` |

Each `CompileTimeSettings` entry adds a Condition with exactly one `bool`, `i64` or `string` value. Names are identifiers ([§2.5](../spec/02-source-and-lexical-structure.md#25-names)); a duplicate Name is an error, and a built-in Name is rejected by [§19.2.1](../spec/19-compile-time-directives.md#1921-built-in-conditions).

```kimi
// CompileTimeSettings={ Tracing=true }
group Log
    #if Tracing
        func note(text: ref/string) -> () => Console.writeLine(text)
```

### 20.2.2. Language version

`LangVersion`, when present, must name a supported version. This revision supports only `0.0.2`; any other value is an error. When it is omitted, the solution's `LangVersion` applies, and otherwise the compiler's current version. A packed Project must declare it.

## 20.3. Commands

| Command | Behavior |
| --- | --- |
| `restore <project>` | Resolves dependencies and replaces the lock ([§20.5](#205-packages-and-locks)); no network access. |
| `check <input>` | Selects inputs, targets and Debug settings as `build` does, and checks one Product unit per selected project. [§23.2.3](../spec/23-compiler-services.md#2323-check-commands) defines ordering, continued checking after unit failures, aggregate acceptance and cancellation; [§23.3](../spec/23-compiler-services.md#233-json-check-output) defines the single JSON command document. |
| `emit <input>` | Checks and publishes the `.ll` and `.link.json` pair; runs no LLVM tool. |
| `build <input>` | Checks current product sources and publishes artifacts by OutputKind (§20.3.2). With `--Manifest <file>`, builds an emitted Application pair without loading sources; Library manifests are rejected. Generation settings come from the manifest, and source, target, Debug and lock options are errors. |
| `run <input>` | Builds as `build` does, then runs the Application; a failed build launches nothing. With `--no-build`, runs the existing executable if the latest build record succeeded, names the resolved input and matches the executable's hash. `run <file.exe>` runs that file directly. |
| `tatara` | Runs a session in one process. It reads lines from standard input: a `build ...` or `run ...` line runs as `kimi build ...` or `kimi run ...`; `exit` or end of input ends the session; an empty line does nothing; any other line fails. A failed line, one whose exit code is not 0, does not end the session; the session returns 1 if any line failed and 0 otherwise. Ctrl+C during a line cancels only that line (the line's exit code is 130), and a second Ctrl+C during that line ends the process with 130; at the prompt Ctrl+C shows the prompt again, and while redirected input is awaited it cancels the session. A fatal error of the native build ends the session. |
| `test <input>` | Runs tests ([§20.6](#206-test-execution)). |
| `pack`, `publish`, `store verify` | Package operations ([§20.5](#205-packages-and-locks)). |
| `toolchain verify` | Checks the toolchain the compiler carries, without loading a project ([§20.3.2](#2032-builds-and-artifacts)). `--Report <path>` writes its record. |
| `lsp` | Serves the Language Server Protocol on standard input and output ([§23.4](../spec/23-compiler-services.md#234-language-server)). |

- Option names are case-sensitive and spelled as this chapter writes them (`--Format`, never `--format`). `--Format` takes `text`, the default, or `json`. Boolean options take `true` or `false`; `--no-build` alone means true.
- `--Target` selects a configured target for `check`, `emit`, `build`, `run` and `pack`, and for `test` in every selected Project; without it, each Project's sole target is used, and several are an error. Release is the default mode; `--Debug true` selects Debug, independently of `Optimization`.
- `check` returns 0 when the whole command is accepted ([§23.2.3](../spec/23-compiler-services.md#2323-check-commands)) and 1 otherwise; other commands return 0 on success and 1 on failure. `run` returns the program's exit code, or 1 for a failure before launch, reported on standard error. `test` uses [§20.6.4](#2064-results-and-exit-codes). Cancellation returns 130. A second Ctrl+C while a command is being cancelled ends the process at once with 130, except during a publication [§23.2.3](../spec/23-compiler-services.md#2323-check-commands) protects.
- `run` forwards standard streams and runs in the project directory (a direct `.exe`: the caller's). Like running tests ([§20.6.1](#2061-selection-and-preparation)), it requires the windows-x64 host. A program `run` starts while Kimi has no standard input reads end of input. The program and its descendants end with `run`: any still running when the program exits, when `run` is cancelled, or when Kimi's process ends.

### 20.3.1. Input resolution and implicit projects

Inputs are relative to the invocation directory; no input means the current directory. An input ending in `.kimi`, `.kimiproj`, `.kimisln` or `.exe` (ASCII case-insensitive) selects only that path. Any other input `A` resolves to the first that exists:

1. the path `A`;
2. the file `A.kimiproj`;
3. the file `A.kimi`;
4. otherwise an error naming the attempted paths.

An unreadable, invalid or unsupported selected file is an error, never a reason to try the next candidate. A directory input looks only in that directory: exactly one `.kimisln` selects that solution, and more than one is an error; with none, every `.kimiproj` is selected, and none is an error. A failure to load any selected Project fails the command. `run` needs exactly one selected Application.

A selected `.kimi` file forms an **implicit project**: named after the file without `.kimi`, in the file's directory, with that file as its only source, the host target (or `--Target`), and otherwise an `O2` Application with default settings. It has no dependencies and no lock, and no project file is written. Each build record names its input, the selected `.kimiproj` or `.kimi` file.

```text
kimi build A.kimi            # implicit project; writes bin/T/A.O2.exe
kimi run A --no-build        # resolves A.kimiproj; Error: the record names A.kimi
```

### 20.3.2. Builds and artifacts

Every source build checks the Product unit in its selected target and Debug environment, excluding TestSources (§20.6.1).

| OutputKind | Build succeeds when | Artifacts |
| --- | --- | --- |
| Application | Required checks and executable generation succeed | Executable and successful build record |
| Library | Required product checks succeed | Successful build record; no executable |

A Solution or directory build applies these rules to every selected Project, including Libraries, and succeeds only if all succeed. A Library build invokes no native tools. `build --Manifest` accepts only Application manifests: it cannot establish a Library's source-check result. Use a source build for Libraries, `emit` for IR and `pack` for distribution packages.

For `OutputPath` `Name.ll`, `emit` writes `Name.ll` and `Name.link.json`. An Application build also writes `Name.O0.exe` or `Name.O2.exe`; every build writes `Name.link.build.json` and keeps `Name.link.build.lock`, an empty file. A Library build writes only the record; its IR, when requested with `emit`, is for inspection only. `build --Manifest Name.link.json` builds that manifest and the `Name.ll` beside it, the pair as `emit` writes it, into the files a build of `OutputPath` `Name.ll` writes; a manifest of another name, or one naming another IR, is an error (names compare ignoring case). A successful Library record identifies the input, selected target, Debug setting, language and compiler versions, and completed product checks. The formats of `.link.json` and of build records are implementation-defined.

- `emit` reports success only after both outputs are published; a partial publication is a failure.
- A build first invalidates the latest successful build record. It publishes success only after the required checks and artifacts complete; an Application publishes its executable only on success. Except as the next sentence states, failure or cancellation leaves no successful record for that attempt, and `run` never runs an older executable left by a failed build. If the record cannot be written, or the process ends between a build's success record and its executable's move, the record keeps the last state written (the earlier success when the first invalidation fails; this attempt's success when the process ends, or the move and the record's rewrite both fail, after that success was written), and `run --no-build` runs only an executable at its path whose hash the record names.
- Two builds that write one build record (one `OutputPath` at either optimization, or `build --Manifest` of its `.link.json`) never run at once: the later one waits until the earlier one ends, and Ctrl+C cancels the wait; `test` builds in a directory of its own.
- A native build compiles the IR its record names: from hashing the pair's IR until the build ends, it holds the IR open and lets others only read it, so a write, replacement or deletion of the IR meanwhile fails (an `emit` of that output then reports a publication failure).
- Reuse of earlier results follows [§A.2](appendices/A-compiler-requirements.md#a2-reuse-of-earlier-work).
- An Unsupported operation produces no placeholder binary.

**Toolchain.** A native build reads no toolchain root and runs no LLVM tool: it runs Tatara (LLVM and LLD in one library), which the compiler carries, and checks Tatara's ABI and LLVM version before any LLVM or LLD work; the reserved supplies are supplied by the compiler (§20.4). Tatara is linked into the published `kimi.exe`; a development `Kimi.dll` loads the `tatara.dll` beside it. PATH is never searched and nothing is downloaded. `toolchain verify` reads only what the compiler carries and runs no tool: it checks Tatara's ABI and LLVM version as a build does, reports them with Tatara's build identity, and checks the bytes of each reserved supply the compiler carries against its pinned hashes. A Tatara that does not load or match, or a supply's mismatch, is a toolchain failure. No command reads a toolchain root, and the compiler reads `KIMI_TOOLCHAIN_ROOT` nowhere. The options `--ToolchainRoot` and `--LlvmBin` are invalid options of every command, named by an argument as any other option is, and their error names the removal: `test` reports the first invalid option it reads (exit code 2, §20.6.4), and every other command reports each occurrence on standard error and returns 1 without doing its work (a `kimi tatara` line fails alone). The project setting `LlvmBin` and a link manifest's `toolchain` member are errors: a project file that has the setting fails to load, naming the removal, and `build --Manifest` rejects such a manifest, asking for it to be emitted again.

## 20.4. Native libraries

A `#LibraryImport` ([§22.6](../spec/22-core-execution-and-foreign-functions.md#226-foreign-function-imports)) names a native logical name of its own Kotonoha; names compare case-sensitively.

- `NativeRequirements` maps each target to logical names with a `Kind` (`static` or `import`) and optional `ContractId` and `Sha256`. Every selected import outside the reserved names needs one; checking reads no native file.
- `NativeLibraries` maps each target to supplies: `Name`, `Input`, optional `Package` (default: the declaring Project), `ContractId` and `Sha256`. A supply for the declaring Project may add `Kind`, which also declares the requirement. A supply must match a required `ContractId` and never changes another module's requirements. Several supplies for one name and target must agree in content and `ContractId`.
- linux-x64 links only `static` libraries: a requirement or supply of Kind `import` under its key is accepted, and each `#LibraryImport` of that library is Unsupported for linux-x64.
- `Input` is a `.lib` path, relative to the declaring Project's directory unless absolute; no search path is used. Each input is hashed when read, its hash assertions are checked, and the linker receives those bytes.
- `kernel32`, `kimi_backend` and `c` are reserved supplies, supplied by the compiler for the profile that has them: windows-x64 has `kernel32` and `kimi_backend`, whose pinned bytes the compiler carries, stages and links as it does an input, and linux-x64 has `c`, musl 1.2.6's C library and startup files, whose pinned bytes the compiler carries. They need no entry, and a `NativeLibraries` or `NativeRequirements` entry for any of them under any target is an error. `kernel32` provides every kernel32 function that the Kimi library and runtime import; `c` provides at least `write`, `_exit`, `exit`, `mmap`, `munmap` and `clock_gettime`. Each reserved supply's exact symbol set is implementation-defined. `c` includes no compiler runtime: its `cpow`, `cpowf` and `cpowl` call `__muldc3`, `__mulsc3` and `__mulxc3`, which no reserved supply defines, so a program whose supplied static library calls one of those three functions links only if a supplied library defines the helper. An import of a function that no profile's supply of that name provides is an error at native resolution; one that another profile's supply provides but the selected target's profile does not is Unsupported.
- A supplied static library must not depend on CRT startup, automatic C/C++ dynamic initialization, TLS initialization or termination callbacks, or automatic `atexit` handlers; a windows-x64 program provides none of them. Zero-initialized and constant data are allowed.
- Symbol resolution and linker directives are implementation-defined, but an ambiguous or unsupported native dependency, or one inconsistent with its declared `Kind`, is an error, never resolved by link order.

```kimi
public group Observer
    #LibraryImport("observer", "observer_notify")
    public func notify(code: i32) -> i32
// NativeLibraries: observer={ Kind="static" Input="native/observer.lib" }
```

## 20.5. Packages and locks

The formats of source packages (`.kimipkg`), release tables and lock files are implementation-defined. Each carries a schema version; readers reject unknown versions, keys and required features. The SourceId is the SHA-256 of the exact manifest bytes (format version 1 hashes `"Kimigayo.Source.v1"`, NUL, then the manifest), so equal SourceIds are guaranteed only within one format version.

- **Lock.** A lock has a product and a test partition ([§18.4.3](../spec/18-modules-and-dependencies.md#1843-resolution-and-pinning)). A missing lock with no dependencies is an empty resolution; a corrupt lock is an error, never an empty resolution.
- **restore** resolves the product partition, then the test partition, and saves both in the lock. A product failure fails it and leaves the test partition unresolved; a test-only failure is reported and recorded as unresolved but does not fail it.
- Every other command validates its partitions (`test` both, others the product one) and fails when a restore is needed, without restoring; `--locked` requests the same check explicitly. A restore is needed when dependency IDs, versions, reference names, edges or pinned Package content change; source edits alone need none.
- **pack** verifies a Library Project in the environment `build` would select (`--verify-all`: every target and mode) and writes it and its Project dependencies as packages to `bin/packages` or `--output`; nothing is written after a failure.
- **publish** adds a package closure to a local store directory, where an ID and version map permanently to one SourceId. Repeating a mapping succeeds; any conflict rejects the whole publication.
- **store verify** rechecks the user cache and invalidates corrupt content. The cache location is implementation-defined.

## 20.6. Test execution

### 20.6.1. Selection and preparation

`test` resolves its input as [§20.3.1](#2031-input-resolution-and-implicit-projects) does; a solution selects each listed Project once. A dependency's tests run only when its Project is selected itself. Running tests requires the windows-x64 host and target ([§20.2.1](#2021-targets-and-compile-time-settings)).

**Test preparation** checks a Project's product and test sources with its test dependencies for one target, without discovering or running tests; its result is a Test-mode check result. Every selected Project is prepared before listing or filtering, so a filter never hides an error, and any load, check or build failure prevents every case from running. Tests ([§6.7.2](../spec/06-declarations-and-containers.md#672-test-definitions)) are discovered at compile time.

The startup rules of [§22.4](../spec/22-core-execution-and-foreign-functions.md#224-program-startup-and-static-storage) apply only to product builds. A test build checks the explicit `main` functions and top-level runtime body items of every source, TestSources included, but never runs them. In a test executable every user operation runs while its case is active, so verification operations ([§17.6](../spec/17-failure-handling.md#176-test-verification-operations)) and `Kimi.Test.tempDirectory()` always have an active case.

```kimi
// Main.kimi, a product source
group Paths
    func scratch() -> string => Kimi.Test.tempDirectory() // Error: not a test-only body
```

### 20.6.2. Options, settings and identity

- `--list` checks and lists cases. `--filter TEXT` selects cases whose fully qualified name contains `TEXT` (case-sensitive). `--case ID` selects one CaseId; an unknown one is an error. `--allow-empty` permits an empty selection.
- `--jobs N` limits live cases across all Projects; `--no-parallel` means one. `--timeout DURATION` sets each case's deadline: a positive integer with `ms`, `s` or `m`, at most 24 hours.
- `--Format json` writes one versioned result document to standard output.
- Unknown, repeated, invalid or conflicting options (`--case` with `--filter`, `--jobs` with `--no-parallel`) are errors, never clamped.
- A Project's `Test` setting holds `Timeout` (default `30s`), which options override, and `Environment`, a variable map in which `TMP`, `TEMP` and `KIMI_TEST_*` are reserved.
- Default `--jobs`, result location and retention, recovery bounds and the result schema are implementation-defined.

A test's **fully qualified name** is its declaring Container path from the project root and its Name, joined by `.` (`MeasureTests.doubled`). The **project identity** is `TestProjectId`, else `PackageId`, else the project file name with extension; an implicit project uses its source file name (`A.kimi`). Two selected Projects with one identity are an error. TestId and CaseId formats are implementation-defined, stable while the project identity, fully qualified name and logical source path are unchanged, and independent of absolute paths and solution position.

### 20.6.3. Case execution

Each selected case runs once, in its own process of its Project's test executable, and is never retried. Case order is unspecified.

- The working directory is the Project directory and standard input is closed. Each case gets synchronous standard output and error handles, logged separately.
- The environment adds `Test.Environment` and sets `TMP` and `TEMP` to the case's new temporary directory, whose absolute path `Kimi.Test.tempDirectory()` returns ([§17.6.1](../spec/17-failure-handling.md#1761-forms-and-placement)). The directory is removed after every outcome.
- The deadline runs from launch to exit. After any outcome the runner stops the case's remaining processes.
- An Abort ends only its case ([§17.6.2](../spec/17-failure-handling.md#1762-evaluation-and-failure)). A case that Aborts is reported once, as failed when the Abort is that of a false `$require` and as aborted otherwise; later output from its other threads never changes that result.
- The protocol between `test` and a case process is implementation-defined. A case passes only when its process completes normally and reports no failure; a lost or malformed report is a runner failure.

### 20.6.4. Results and exit codes

Each case is reported as passed, failed (a false `$expect` or `$require`, including the Abort of a false `$require`), aborted (an Abort or another abnormal process termination), timed out, cancelled (including unstarted cases) or a runner failure. Listings and results are sorted by fully qualified name, then logical source path, then project identity.

Exit codes, the first applicable of 130, 3, 2, 1, else 0:

- 0: a successful listing, all selected cases passed, or an allowed empty selection.
- 1: a case failed, aborted or timed out.
- 2: invalid options, settings or selection (an empty one without `--allow-empty`, an unsupported host or target), or a load, check or build failure.
- 3: a runner failure in startup, communication, recovery or result storage.
- 130: cancelled.
