# Minimal executable

`Hello.kimi` contains exactly one source line:

```kimi
::Core.writeLine("Hello, world!")
```

The current emitter supports this literal-output slice of SPEC, including empty strings and UTF-8/NUL contents. Other selected implementation bodies and executable operations receive unsupported diagnostics. It is a partial compiler, not a complete Core library or the broader first-executable subset.

## Configure

Edit `Hello.kimiproj`. The file uses Tinyhand indentation syntax, not JSON. LLVM's location is independent of the target/version contract:

```text
LlvmBin="C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin"
NativeLibraries=
  x86_64-pc-windows-msvc=
    kernel32=
      Kind="import"
      Input="C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin/kernel32.lib"
    kimi_backend=
      Kind="static"
      Input="../../backend/windows-x64/bin/kimi_backend_windows_x64_v1.lib"
```

The supplied machine path is retained in the example. Its tools actually report **22.1.5**, whereas SPEC requires **22.1.8**. Generation records the location as a manual-build hint and does not execute it. The manual builder rejects the mismatch. Set `LlvmBin` to a 22.1.8 installation, or pass `-LlvmBin` to the manual builder. No system PATH change or automatic tool installation occurs.

`kernel32.lib` is configured independently: the file at the supplied path matches Windows SDK 10.0.22621.0's x64 import library. Relative setting paths resolve from the project directory and are rebased relative to the manifest. `OutputPath` defaults to `bin/<target>/Hello.ll`; `Optimization` accepts `O0` or `O2` and defaults to `O2`.

## Build and run

Run from the repository root with the .NET dependencies restored. In the verified local workspace, LLVM 22.1.8 is extracted under `bin/toolchain/bin`; substitute another actual 22.1.8 bin directory for a fresh checkout.

```powershell
$llvmBin = "$PWD/bin/toolchain/bin"
$kernel32 = 'C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin/kernel32.lib'

./backend/windows-x64/build.ps1 -LlvmBin $llvmBin -Kernel32 $kernel32
dotnet build Kimi/Kimi.csproj -c Release
dotnet Kimi/bin/Release/net10.0/Kimi.dll build examples/Hello/Hello.kimiproj
./backend/windows-x64/manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json -LlvmBin $llvmBin -Run
```

The compiler only publishes `Hello.ll` and `Hello.link.json`. The explicitly invoked manual script verifies hashes/profile/tool versions, runs LLVM and the linker, records actual libraries/tool identities in `Hello.link.build.json`, and runs the executable only with `-Run`. Without an explicit library path, this script resolves a simple library filename in the manifest directory; provide resolved paths when the libraries reside elsewhere. It does not discover an SDK.

The executable is `examples/Hello/bin/x86_64-pc-windows-msvc/Hello.O2.exe` (or `.O0.exe`). The executable itself must emit exactly 14 stdout bytes (`Hello, world!\n`), empty stderr, and exit 0. Compiler and manual-builder status messages are separate from application output.

## Verify

```powershell
dotnet test xUnitTest/xUnitTest.csproj -c Debug
dotnet test xUnitTest/xUnitTest.csproj -c Release
./backend/windows-x64/test-emission.ps1 -LlvmBin $llvmBin -Kernel32 $kernel32 -Configuration Debug
./backend/windows-x64/test-emission.ps1 -LlvmBin $llvmBin -Kernel32 $kernel32 -Configuration Release
./backend/windows-x64/test-manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json -LlvmBin $llvmBin -MismatchedLlvmBin 'C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin'
```

Unit tests export inspection fixtures under ignored `bin/emission-fixtures`. The native harness separately verifies/links/runs O0/O2 fixtures and tests fault-injecting adapters without changing the production Windows imports. Reports under `bin/emission-native` start incomplete and become passed only when every native case succeeds.
