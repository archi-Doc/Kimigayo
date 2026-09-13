## Kimigayo
Work in progress

See [SPEC.md](SPEC.md) for language rules and [STATUS.md](STATUS.md) for executable coverage.

Build and test with the .NET 10 SDK:

```powershell
dotnet build Kimigayo.slnx -c Release
dotnet test xUnitTest/xUnitTest.csproj -c Release
```

Prepare the shared LLVM/backend toolchain once from an existing LLVM 22.1.8 directory:

```powershell
./backend/windows-x64/setup.ps1 -LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release -- build examples/Hello/Hello.kimiproj
```

Tools are stored in `toolchain/`, and the verified backend library in `toolchain/windows_x64/`. Projects need no machine-specific paths. See [SPEC §20.8.8](SPEC.md#2088-toolchain-storage-and-native-library-lifecycle) for generation, installation, lookup and linking. When distributing the compiler outside this checkout, place the toolchain beside Kimi.exe/Kimi.dll or select it with `--ToolchainRoot` / `KIMI_TOOLCHAIN_ROOT`.

Publish the compiler with NativeAOT on Windows x64 using the .NET NativeAOT C++ toolchain prerequisites:

```powershell
dotnet publish Kimi/Kimi.csproj -c Release -r win-x64 -p:PublishAot=true -o bin/native-compiler
bin/native-compiler/Kimi.exe build examples/Counter/Counter.kimiproj
bin/native-compiler/Kimi.exe run examples/Counter/Counter.kimiproj
```

The compiler's NativeAOT toolchain is separate from the pinned LLVM tools and backend archive used to compile Kimigayo programs. See [backend setup](backend/windows-x64/README.md) for those inputs. `IsAotCompatible` enables trimming/AOT analysis in ordinary compiler builds; CLI registration and LSP serialization use statically registered metadata.

After generating test fixtures, verify native scalar execution and the published compiler:

```powershell
backend/windows-x64/test-scalars.ps1
backend/windows-x64/test-cli.ps1 -NativeCompiler bin/native-compiler/Kimi.exe
backend/windows-x64/test-lsp.ps1 -CompilerPath bin/native-compiler/Kimi.exe
```
