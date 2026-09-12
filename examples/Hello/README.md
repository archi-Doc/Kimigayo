# Minimal executable

`Hello.kimi` contains exactly one source line:

```kimi
::Core.writeLine("Hello, world!")
```

The current emitter supports this literal-output slice of SPEC, including empty strings and UTF-8/NUL contents. Other selected implementation bodies and executable operations receive unsupported diagnostics. It is a partial compiler, not a complete Core library or the broader first-executable subset.

## Configure

Edit `Hello.kimiproj`. The file uses Tinyhand indentation syntax, not JSON. LLVM's location is independent of the target/version contract:

```text
LlvmBin="C:/App/llvm"
NativeLibraries=
  x86_64-pc-windows-msvc=
    kimi_backend=
      Kind="static"
      Input="../../backend/windows-x64/bin/kimi_backend_windows_x64_v1.lib"
```

The [profile catalog](../../backend/windows-x64/profile.json) requires LLVM **22.1.8**. The example uses `C:/App/llvm`; it must contain opt, llc, lld-link, llvm-nm, llvm-readobj and llvm-dlltool. Backend verification also needs clang, llvm-lib and llvm-objdump. `emit-llvm` records the directory without executing tools. `build` checks reporting tools' versions and the approved SHA-256 of the versionless llvm-dlltool. `--LlvmBin` overrides the directory. For exploratory work only, `--AllowUnpinnedToolchain true` warns and records `unverifiedToolchain: true` on a version/tool-hash mismatch; integrity checks still apply. No system PATH change or automatic tool installation occurs.

`kernel32.lib` is generated automatically from the compiler's embedded `.def` by llvm-dlltool; no Windows SDK import library is needed. Remove any old `kernel32` entry from NativeLibraries and re-emit schema 1 manifests. The compiler validates the generated DLL, x64 format and imports before linking. Relative external-library paths resolve from the project directory and are rebased relative to the manifest. `OutputPath` defaults to `bin/<target>/Hello.ll`; `Optimization` accepts `O0` or `O2` and defaults to `O2`.

## Build and run

Run from the repository root with the .NET dependencies restored. Substitute your actual matching LLVM bin directory. The backend must already have been built and its hash must match the adopted profile. Compiler and backend package releases both come from `Directory.Build.props` Version; LLVM and ABI versions are separate.

```powershell
$llvmBin = 'C:/App/llvm'

./backend/windows-x64/build.ps1 -LlvmBin $llvmBin
dotnet build Kimi/Kimi.csproj -c Release
dotnet Kimi/bin/Release/net10.0/Kimi.dll build examples/Hello/Hello.kimiproj --LlvmBin $llvmBin
dotnet Kimi/bin/Release/net10.0/Kimi.dll run examples/Hello/Hello.kimiproj
```

`build` publishes `Hello.ll` and schema 2 `Hello.link.json`, generates `Hello.O2.kernel32.def`/`.lib`, verifies tool versions, hashes and native dependencies, and invokes LLVM and the linker directly from C#. No PowerShell runtime is needed by the compiler. It writes tool/definition/library identities to `Hello.link.build.json` and publishes the executable only after a successful link. Simple external native library filenames resolve in the manifest directory; configure explicit paths for libraries elsewhere. Failed generation never links a previous import library.

`run` executes the existing binary without reading or recompiling source contents and without requiring LLVM. A missing executable, incomplete build record, or changed binary hash fails with a diagnostic. After editing sources, run `build` explicitly. You can also execute a binary directly with `kimi run path/to/program.exe`; no project or record is required in that form. The child's stdout/stderr and exit code are forwarded.

For debugging the compiler's pre-optimization IR, use:

```powershell
dotnet Kimi/bin/Release/net10.0/Kimi.dll emit-llvm examples/Hello/Hello.kimiproj
```

This performs required semantic/ownership checks and writes only the matched `.ll`/`.link.json` pair. It neither executes LLVM nor links/runs a program. The existing manual builder remains available for separately building this pair:

```powershell
./backend/windows-x64/manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json -LlvmBin $llvmBin
```

The executable is `examples/Hello/bin/x86_64-pc-windows-msvc/Hello.O2.exe` (or `.O0.exe`). The executable itself must emit exactly 14 stdout bytes (`Hello, world!\n`), empty stderr, and exit 0. Compiler and manual-builder status messages are separate from application output.

## Verify

```powershell
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug
dotnet test --project xUnitTest/xUnitTest.csproj -c Release
./backend/windows-x64/test-kernel32.ps1 -LlvmBin $llvmBin
./backend/windows-x64/test-emission.ps1 -LlvmBin $llvmBin -Configuration Debug
./backend/windows-x64/test-emission.ps1 -LlvmBin $llvmBin -Configuration Release
./backend/windows-x64/test-manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json -LlvmBin $llvmBin -MismatchedLlvmBin 'C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin'
./backend/windows-x64/test-cli.ps1 -LlvmBin $llvmBin -MismatchedLlvmBin 'C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin'
```

Unit tests export inspection fixtures under ignored `bin/emission-fixtures`. The native harness separately verifies/links/runs O0/O2 fixtures and tests fault-injecting adapters without changing the production Windows imports. Reports under `bin/emission-native` start incomplete and become passed only when every native case succeeds.
