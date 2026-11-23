# Windows x64 backend

This directory supplies the native helpers for SPEC §21.4.7. The reviewed package is pinned in [profile.json](profile.json); [ADOPTION.md](ADOPTION.md) records its scope and validation. Local builds remain candidates until their actual hash matches the adopted catalog. The helpers are separate from the six runtime operations emitted into Kimigayo LLVM modules. LLVM **22.1.8** is the current profile version.

## Build and test

Use the repository's `scripts/verify.ps1` for implementation-unit and end-of-session evidence:

```powershell
./scripts/verify.ps1 -Class XunitTest.StorageBoundaryTest -Fixtures 'StorageBoundary*.ll' -Milestone 29
./scripts/verify.ps1 -Mode Session -Fixtures 'StorageBoundary*.ll' -Milestone 27,29
# Debug is opt-in in either mode:
./scripts/verify.ps1 -Configuration Debug -Class XunitTest.StorageBoundaryTest -Fixtures 'StorageBoundary*.ll' -Milestone 29
./scripts/verify.ps1 -Mode Session -Configuration Debug
```

Both modes default to one Release compiler build and keep non-incremental builds with warnings as errors.
Use `-Configuration Debug` explicitly for Debug verification. Unit mode runs the selected tests;
Session mode runs the full suite once. Builds, tests, fixture selection and milestone harnesses all use
the selected compiler configuration; native O0/O2 coverage is unchanged. Test collections run up to eight at a time,
respecting each test collection's disabled-parallelization setting. Milestone harnesses run in separate
processes, up to eight at a time; both defaults are capped by the logical processor count.
Use `-TestParallel 1 -Parallel 1` for serial execution, or set either limit independently. Every
stage announces its start and records its duration, along with the total duration and concurrency
settings and selected compiler configuration, in `artifacts/verify/<run>/summary.json`. Do not edit sources during verification.

For bounded local verification with separate stdout/stderr logs and a JSON result, run commands through `invoke-verification.ps1` from the repository root:

```powershell
./src/backend/windows-x64/invoke-verification.ps1 -FilePath dotnet -ArgumentList @('test', '--project', 'tests/xUnitTest/xUnitTest.csproj', '-c', 'Release', '--no-build', '--no-restore', '--minimum-expected-tests', '1', '--parallel', 'none')
```

Build the selected configuration first. The default command deadline is 900 seconds and output draining is bounded to five seconds. Optional `-InputPath` records SHA-256 hashes of exact input files and verifies that they remain unchanged. Records use unique directories under ignored `artifacts/verify/commands`. On Windows, a waiting worker is assigned to a [job with kill-on-close](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects) before receiving the target command. Closing the job removes descendants even after the target exits; no target process starts before containment. Other hosts use process-tree termination while the root remains alive.

Set up the checkout's `toolchain/` once from the extracted LLVM 22.1.8 release archive (`clang+llvm-22.1.8-x86_64-pc-windows-msvc.tar.xz`). This copies the required tools (no DLL of the archive: no tool imports one), builds/tests the backend's candidates, which must reproduce the adopted libraries (it installs no library; an existing `toolchain/windows_x64/` stays and nothing reads it), then builds `tatara.dll` from the archive's `lib` and `include` ([src/Tatara](../../Tatara/README.md)) and installs it into `toolchain/`. Setup's prerequisites beyond the archive are Visual Studio's C++ x64 tools, a Windows SDK and the DIA SDK that Visual Studio installs. A file whose installed copy already has the same SHA-256 is not written, so an unchanged rerun writes nothing; a changed `tatara.dll` replaces the installed one by a move, renaming a loaded one aside (`tatara.dll.<time>.old`, deleted by a later run), and `installation.json` is written last:

```powershell
./src/backend/windows-x64/setup.ps1 -LlvmRoot 'C:/App/clang+llvm-22.1.8-x86_64-pc-windows-msvc'
# Later candidate rebuilds and checks use the installed tools:
./src/backend/windows-x64/build.ps1
```

Projects require no LlvmBin, and `kernel32` and `kimi_backend` are the compiler's supplies: a NativeLibraries entry for either is an error. Kimi carries its toolchain: a native `kimi` build runs Tatara (the `tatara.dll` beside `Kimi.dll`, which the build of Kimi copies from this checkout's `toolchain`), stages and links the two libraries the compiler carries, reads no toolchain root and runs no LLVM tool. The options `--ToolchainRoot` and `--LlvmBin`, a project's `LlvmBin` and a link manifest's `toolchain.llvmBin` are errors (Kimi carries its toolchain; re-emit such a manifest), and Kimi reads `KIMI_TOOLCHAIN_ROOT` nowhere. The scripts' `-ToolchainRoot` (else this checkout's `toolchain`; they fail while `KIMI_TOOLCHAIN_ROOT` is set) selects the development toolchain they check and run, and their `-LlvmBin` the LLVM tools they run directly. No PATH lookup occurs. See [SPEC §20.3.2](../../../docs/impl/20-compilation-configuration.md#2032-builds-and-artifacts) for storage, resolution, generation, installation and Application build details.

No Windows SDK import library is required. The compiler carries the adopted `kernel32.lib` and `kimi_backend_windows_x64_v1.lib` committed in this directory (embedded resources, their SHA-256 the catalog's `kernel32.sha256` and `artifactSha256`); a build stages each as `<output>.native/<sha256>.lib`, as it does a program's library, and records its hash. Setup/update generates and validates kernel32 from the shared definition and fails unless both candidates reproduce the catalog's hashes; it installs neither (Tatara B6b6). The scalar fixtures, the emission tests and the tool control comparison read the committed files, as builds do (`toolchain.ps1`'s `Get-KimiBackendArchive` and `kernel32.ps1`'s `Get-KimiKernel32` name them). `kimi toolchain verify` checks what the compiler carries (it reports Tatara's build identity and checks its ABI and LLVM version, and the embedded libraries' hashes against the catalog) and reads no root; `./src/backend/windows-x64/check-toolchain.ps1` checks a development root's tools and `tatara.dll` against `installation.json` and the catalog and is setup's last step; `verify.ps1 -VerifyToolchain` runs both once. The backend candidate builder alone retains `-AllowUnpinnedToolchain` for unverified experiments. Profile adoption checks remain strict.

Run `./src/backend/windows-x64/test-toolchain.ps1` for version-policy regression tests without an LLVM installation. Run `./src/backend/windows-x64/test-kernel32.ps1 -LlvmBin C:/App/llvm` for import generation, tool identity, failure, privacy and reproducibility tests.

Candidate archives and kernel32 import libraries (with the definition they were generated from), per-member COFF/unwind/disassembly reports and `verification.json`, which names both candidates, remain under ignored `artifacts/backend/windows-x64/`. Intermediate objects, generated test IR and native test executables go to `temp/backend-build/<run>/`. No candidate is installed: `setup.ps1` fails unless both reproduce the catalog's hashes with pinned tools, and exploratory/different-hash candidates warn. An LLVM update that changes either library adopts its new hash in `profile.json` and commits the reproduced file here, both in one commit. A failed run leaves `status: incomplete`; old files do not establish current success. Executable tests have a 30-second timeout.

Saved native diagnostics, build/verification JSON reports, and LLVM source metadata replace the checkout root with `/_/project` and the current user's home directory with `/_/user`. Archive member names and LLVM object output names use filenames without directories. These paths describe the inputs; use the recorded SHA-256 hashes to identify the actual files. Tool invocation and link manifests still use resolvable paths. LLVM program string constants are preserved. Run `./src/backend/windows-x64/test-artifact-paths.ps1` to check this policy. Local .NET/NuGet `obj/` caches and build logs may still contain absolute paths; they are ignored by Git and are not shareable build reports.

## Implementation

The archive's one member defines one strong external symbol, `__chkstk` (supply ABI 3). The C memory functions (`memcmp`, `memcpy`, `memmove`, `memset`) are not in the archive: every generated module defines them in its runtime's LLVM IR (IMPL 21.4.7; the Tatara plan's decision 24).

The helper is assembled directly to native COFF. It contains no bitcode, CRT startup, TLS, default library directives, stack protector dependencies, or `_fltused`, and has Windows unwind metadata. `__chkstk` takes and preserves RAX, preserves the other general registers, probes downward in 4096-byte steps, and leaves the caller to allocate its frame. It uses no ordinary C wrapper around the special probe ABI.

The sources are project-owned MIT-licensed implementations, not copied compiler-rt files. LLVM is used as the assembler and toolchain.

## Current checks

- Enforce the version of tools that report it; llvm-lib exposes no version switch and is explicitly recorded as unversioned with its executable hash. Pin the versionless llvm-dlltool binary hash; retain source, build-script, normalized definition and generated kernel32 hashes, and reject source changes during verification.
- Inspect the member for native COFF, unwind records, absent undefined symbols/default-library dependencies, and no calls. Check the archive's one exported symbol, `__chkstk`, and absence of `_fltused`.
- Check probe sizes around page boundaries and across multiple pages, RAX/RCX/RDX preservation, and a compiler-generated 32 KiB stack frame.
- Verify test LLVM IR, run O0 and `default<O2>`/llc O2, link with a custom entry and `/NODEFAULTLIB`, and require native process exit code zero.

The production archive has no external dependencies. The memory functions' cases (`tests/memory.c`: byte/word boundaries, offsets 0–15, both overlap directions, self-move, exact return pointers, memcmp's order at every difference position, buffers adjacent to inaccessible pages) run on the runtime's memory part with no archive, in `TataraExecutorTest.TheMemoryPartPassesTheHelperSuite`. The test executable supplies its own `_fltused` marker and imports ExitProcess; it does not rely on CRT initialization.

## Adoption boundary

The report is a **tested candidate**, not proof that a local rebuild is adopted: `adopted` stays false. `packageVersion` follows `Directory.Build.props` Version, shared with Kimigayo. The catalog hash/ABI must still match before use. Hashes are computed from the actual output, never placeholders.

The current catalog was frozen after assembly/unwind review and generated Kimigayo module validation described in ADOPTION.md. A different archive needs renewed review, native and generated-module verification, and a new immutable catalog version. Helper tests alone do not cover all runtime/ABI features, i128 expansion, all possible machine states, or compiler emission. They do not establish a throughput improvement.

Rebuilding is repeatable from these sources and recorded tools; adoption must compare the resulting archive hash rather than assume filename or timestamp equality. The build script installs only a verified reproduction into the local toolchain; it does not publish a runtime package or adopt a new archive.

For the one-line Application and compiler commands, see [the Hello example](../../../docs/examples/Hello/README.md). An Application's `kimi build` directly runs LLVM through C# and needs no PowerShell runtime; a Library build checks product sources and writes only a build record, without native tools. `kimi build --Manifest <path.link.json>` processes an existing Application artifact pair through the native pipeline and rejects Library manifests. `manual-build.ps1` wraps that entry point; it uses the Release source-built compiler by default, or `-Configuration Debug` / `-Compiler <Kimi.dll-or-exe>`, and `-Run` executes the result. Backend bootstrap and native test oracles remain independent. `test-emission.ps1` verifies generated modules and test-only runtime fault adapters; `test-cli.ps1` covers both build entry points, declared clock/foreign imports, input assertions, failed-link publication, run, emit and toolchain policy.

`test-scalars.ps1 [-Configuration Release]` builds the fixtures generated by the C# emission tests at O0 and O2 in `kimi tatara` sessions of that configuration's `Kimi.dll` (each `build --Manifest` of an IR/manifest pair, through Tatara) and runs them. Use `-FixturePattern 'String*.ll'` for owned-string cases. These include original generated modules and runtime-only lifetime audits; the latter count destruction per Static handle and check expected totals at exit, preserving source-generated functions. Audit-control fixtures intentionally exit 120 when destruction counts disagree. They do not create Heap strings or replace the independent runtime allocation/free tests. Divergent fixtures use the shared CPU-observation/process-tree-kill path.

Use `-FixtureDirectory <archived-directory>` to execute an isolated, freshly generated fixture inventory instead of the shared `temp/scalar-fixtures` directory. Keep each `.ll` with its `.stdout`, `.stderr`, `.exit` and optional `.observation` oracle, and record the generating compiler/test configuration and hashes. Scratch outputs default to `temp/scalar-native`; use a fresh `-OutputDirectory <directory>` for each run (concurrent runs must use distinct paths), while sharing the installed import library. The runner decodes stdout/stderr as strict UTF-8 independently of the parent console code page.

A positive .observation value is a process CPU budget in milliseconds for a nonterminating fixture; zero or no sidecar expects completion. -ExecutionDeadlineMilliseconds (default 30000) independently bounds execution from launch. A separate fixed 5000 ms recovery grace covers killing, exit wait and both output streams together. Insufficient observation or unavailable launch/recovery/capture is recorded as incomplete and fails verification, including for empty expected output. Completed invalid UTF-8 bytes and observed output/exit violations are failed. Results retain executions, measurements and operation/violation facts, including on failure. Regenerate inventories made with the former .timeout elapsed-time oracle.

Run `pwsh -File src/backend/windows-x64/test-testing.ps1` to verify the language test runner at O0/O2, including assertions, cleanup, temporary directories, budgets, timeout and solution aggregation. It uses the managed compiler and pinned native tools; it does not run NativeAOT.

Milestone wrappers share `milestone-harness.ps1`: original source bytes and filenames, two builds and two direct executions (O0/O2), exact output/exit checks, source/compiler hashes and preparation/build/run timings. No source variants or rejection generation remain. Dedicated feature fixtures and CLI tests cover those contracts. `test-toolchain-verification.ps1` checks explicit verification and modified/missing installation artifacts.
