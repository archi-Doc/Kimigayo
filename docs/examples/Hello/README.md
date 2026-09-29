# Minimal executable

`Hello.kimi` contains exactly one source line:

```kimi
::Kimi.Console.writeLine("Hello, world!")
```

This example checks literal UTF-8 output. See [STATUS](../../STATUS.md) for the broader supported language and remaining limitations.

## Configure

Run the setup script once from the Kimigayo repository root:

```powershell
./src/backend/windows-x64/setup.ps1 -LlvmBin 'C:/App/llvm'
```

It validates the existing LLVM **22.1.8** installation, copies the required executables and adjacent DLLs into `toolchain/`, builds/tests the backend and installs the adopted archive into `toolchain/windows_x64/`. It does not download tools or change PATH. Later backend source changes can be rebuilt with `./src/backend/windows-x64/build.ps1`.

`Hello.kimiproj` uses Tinyhand indentation syntax and needs no LLVM/backend paths. `OutputPath` defaults to `bin/<target>/Hello.ll`; `Optimization` accepts `O0` or `O2` and defaults to `O2`. Source builds find the checkout's toolchain; standalone compiler distributions use toolchain beside Kimi.exe/Kimi.dll. Use `--ToolchainRoot` or `KIMI_TOOLCHAIN_ROOT` to select another root. Legacy LlvmBin and explicit kimi_backend entries remain supported as overrides.

`kernel32.lib` is generated and validated during setup/update, then shared from `toolchain/windows_x64`. No Windows SDK is required. Ordinary builds skip tool identity checks; `kimi toolchain verify` explicitly checks LLVM versions, installed hashes, adopted backend and import generation conditions. See [§20.8.8](../../impl/20-compilation-configuration.md#2088-toolchain-storage-and-native-library-lifecycle).

## Build and run

Run from the repository root with the .NET dependencies restored and toolchain setup completed. Compiler and backend package releases both come from `Directory.Build.props` Version; LLVM and ABI versions are separate.

```powershell
dotnet build src/Kimi/Kimi.csproj -c Release
dotnet src/Kimi/bin/Release/net10.0/Kimi.dll build docs/examples/Hello/Hello.kimiproj
dotnet src/Kimi/bin/Release/net10.0/Kimi.dll run docs/examples/Hello/Hello.kimiproj --no-build
```

`build` publishes `Hello.ll` and schema 3 `Hello.link.json`, verifies IR and native build inputs, and invokes LLVM directly from C#. It links the installed backend/kernel32 libraries and publishes the executable after a successful link. `Hello.link.build.json` records compilation success separately from toolchain verification, which is not performed in ordinary builds.

`run --no-build` executes an existing binary without source compilation or LLVM, requiring a successful build record and matching executable hash. Ordinary `run` builds current sources first and never runs an old binary after failure. `kimi run path/to/program.exe` directly executes the selected binary. Each form forwards stdout/stderr and exit status.

For debugging the compiler's pre-optimization IR, use:

```powershell
dotnet src/Kimi/bin/Release/net10.0/Kimi.dll emit docs/examples/Hello/Hello.kimiproj
```

This performs required semantic/ownership checks and writes only the matched `.ll`/`.link.json` pair. It neither executes LLVM nor links/runs a program. The existing manual builder remains available for separately building this pair:

```powershell
./src/backend/windows-x64/manual-build.ps1 -Manifest docs/examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json
```

The executable is `docs/examples/Hello/bin/x86_64-pc-windows-msvc/Hello.O2.exe` (or `.O0.exe`). The executable itself must emit exactly 14 stdout bytes (`Hello, world!\n`), empty stderr, and exit 0. Compiler and manual-builder status messages are separate from application output.

## Verify

```powershell
./scripts/verify.ps1 -Class XunitTest.NativeToolchainTest,XunitTest.Kernel32ImportsTest -Milestone 1 -VerifyToolchain
./src/backend/windows-x64/test-cli.ps1
./src/backend/windows-x64/test-toolchain-verification.ps1
./src/backend/windows-x64/test-manual-build.ps1 -Manifest docs/examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json
```

Unit tests export inspection fixtures under ignored `temp/emission-fixtures`. The native harness separately verifies/links/runs O0/O2 fixtures and tests fault-injecting adapters without changing the production Windows imports. Reports under `artifacts/verify/emission-native` start incomplete and become passed only when every native case succeeds.
