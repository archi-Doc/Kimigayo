# Minimal executable

`Hello.kimi` contains exactly one source line:

```kimi
::Kimi.Console.writeLine("Hello, world!")
```

The current emitter supports this literal-output slice of SPEC, including empty strings and UTF-8/NUL contents. Other selected implementation bodies and executable operations receive unsupported diagnostics. It is a partial compiler, not a complete Kimi library or the broader first-executable subset.

## Configure

Run the setup script once from the Kimigayo repository root:

```powershell
./backend/windows-x64/setup.ps1 -LlvmBin 'C:/App/llvm'
```

It validates the existing LLVM **22.1.8** installation, copies the required executables and adjacent DLLs into `toolchain/`, builds/tests the backend and installs the adopted archive into `toolchain/windows_x64/`. It does not download tools or change PATH. Later backend source changes can be rebuilt with `./backend/windows-x64/build.ps1`.

`Hello.kimiproj` uses Tinyhand indentation syntax and needs no LLVM/backend paths. `OutputPath` defaults to `bin/<target>/Hello.ll`; `Optimization` accepts `O0` or `O2` and defaults to `O2`. Source builds find the checkout's toolchain; standalone compiler distributions use toolchain beside Kimi.exe/Kimi.dll. Use `--ToolchainRoot` or `KIMI_TOOLCHAIN_ROOT` to select another root. Legacy LlvmBin and explicit kimi_backend entries remain supported as overrides.

`kernel32.lib` is generated for each project/optimization from the embedded `.def`; no Windows SDK import library is needed. Remove old kernel32 entries and re-emit schema 1/2 manifests. Tool versions, dlltool identity, backend ABI/release and archive SHA-256 are checked before linking. See [SPEC §20.8.8](../../impl/20-compilation-configuration.md#2088-toolchain-storage-and-native-library-lifecycle) for the complete lifecycle.

## Build and run

Run from the repository root with the .NET dependencies restored and toolchain setup completed. Compiler and backend package releases both come from `Directory.Build.props` Version; LLVM and ABI versions are separate.

```powershell
dotnet build Kimi/Kimi.csproj -c Release
dotnet Kimi/bin/Release/net10.0/Kimi.dll build examples/Hello/Hello.kimiproj
dotnet Kimi/bin/Release/net10.0/Kimi.dll run examples/Hello/Hello.kimiproj
```

`build` publishes `Hello.ll` and schema 3 `Hello.link.json`, generates `Hello.O2.kernel32.def`/`.lib`, verifies tool versions, hashes and native dependencies, and invokes LLVM and the linker directly from C#. No PowerShell runtime is needed by the compiler. It writes tool/definition/library identities to `Hello.link.build.json` and publishes the executable only after a successful link. Simple external native library filenames resolve in the manifest directory; configure explicit paths for libraries elsewhere. Failed generation never links a previous import library.

`run` executes the existing binary without reading or recompiling source contents and without requiring LLVM. A missing executable, incomplete build record, or changed binary hash fails with a diagnostic. After editing sources, run `build` explicitly. You can also execute a binary directly with `kimi run path/to/program.exe`; no project or record is required in that form. The child's stdout/stderr and exit code are forwarded.

For debugging the compiler's pre-optimization IR, use:

```powershell
dotnet Kimi/bin/Release/net10.0/Kimi.dll emit examples/Hello/Hello.kimiproj
```

This performs required semantic/ownership checks and writes only the matched `.ll`/`.link.json` pair. It neither executes LLVM nor links/runs a program. The existing manual builder remains available for separately building this pair:

```powershell
./backend/windows-x64/manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json
```

The executable is `examples/Hello/bin/x86_64-pc-windows-msvc/Hello.O2.exe` (or `.O0.exe`). The executable itself must emit exactly 14 stdout bytes (`Hello, world!\n`), empty stderr, and exit 0. Compiler and manual-builder status messages are separate from application output.

## Verify

```powershell
dotnet test xUnitTest/xUnitTest.csproj -c Debug
dotnet test xUnitTest/xUnitTest.csproj -c Release
./backend/windows-x64/test-kernel32.ps1
./backend/windows-x64/test-emission.ps1 -Configuration Debug
./backend/windows-x64/test-emission.ps1 -Configuration Release
./backend/windows-x64/test-manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json -MismatchedLlvmBin 'C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin'
./backend/windows-x64/test-cli.ps1 -MismatchedLlvmBin 'C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin'
```

Unit tests export inspection fixtures under ignored `bin/emission-fixtures`. The native harness separately verifies/links/runs O0/O2 fixtures and tests fault-injecting adapters without changing the production Windows imports. Reports under `bin/emission-native` start incomplete and become passed only when every native case succeeds.
