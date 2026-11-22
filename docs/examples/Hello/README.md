# Minimal executable

`Hello.kimi` contains exactly one source line:

```kimi
::Kimi.Console.writeLine("Hello, world!")
```

This example checks literal UTF-8 output. See [STATUS](../../STATUS.md) for the broader supported language and remaining limitations.

## Configure

Run the setup script once from the Kimigayo repository root:

```powershell
./src/backend/windows-x64/setup.ps1 -LlvmRoot 'C:/App/clang+llvm-22.1.8-x86_64-pc-windows-msvc'
```

It validates the extracted LLVM **22.1.8** release archive, copies the required executables into `toolchain/`, builds/tests the backend's candidates against the adopted libraries the compiler carries (installing none), and builds and installs `tatara.dll` (which needs Visual Studio's C++ x64 tools, a Windows SDK and the DIA SDK). It does not download tools or change PATH. Later backend source changes can be rebuilt with `./src/backend/windows-x64/build.ps1`.

`Hello.kimiproj` uses Tinyhand indentation syntax and needs no LLVM/backend paths. `OutputPath` defaults to `bin/<target>/Hello.ll`; `Optimization` accepts `O0` or `O2` and defaults to `O2`. A build reads no toolchain root: it loads the `tatara.dll` beside the compiler (building Kimi copies the checkout's `toolchain/tatara.dll` beside `Kimi.dll`, so build Kimi after setup), links the compiler's own `kernel32` and backend libraries, and runs no LLVM tool. The options `--ToolchainRoot` and `--LlvmBin` and a project's `LlvmBin` are errors (Kimi carries its toolchain); Kimi reads `KIMI_TOOLCHAIN_ROOT` nowhere, and `kimi toolchain verify` reads no root. A `kimi_backend` or `kernel32` entry is an error: both are supplied by the compiler.

`kernel32.lib` is generated and validated during setup/update, and the compiler carries the adopted copy (`src/backend/windows-x64/kernel32.lib`). No Windows SDK is required. Ordinary builds skip tool identity checks; `kimi toolchain verify` checks what the compiler carries (Tatara's ABI and LLVM version, the embedded libraries' hashes against the catalog), and `./src/backend/windows-x64/check-toolchain.ps1` checks a development toolchain's tools, hashes and installed libraries. See [§20.3.2](../../impl/20-compilation-configuration.md#2032-builds-and-artifacts).

## Build and run

Run from the repository root with the .NET dependencies restored and toolchain setup completed. Compiler and backend package releases both come from `Directory.Build.props` Version; LLVM and ABI versions are separate.

```powershell
dotnet build src/Kimi/Kimi.csproj -c Release
dotnet src/Kimi/bin/Release/net10.0/Kimi.dll build docs/examples/Hello/Hello.kimiproj
dotnet src/Kimi/bin/Release/net10.0/Kimi.dll run docs/examples/Hello/Hello.kimiproj --no-build
```

`build` publishes `Hello.ll` and schema 4 `Hello.link.json`, verifies IR and native build inputs, and invokes LLVM directly from C#. It stages and links the compiler's backend and kernel32 libraries (`Hello.O2.native/<sha256>.lib`) and publishes the executable after a successful link. `Hello.link.build.json` records compilation success separately from toolchain verification, which is not performed in ordinary builds.

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
