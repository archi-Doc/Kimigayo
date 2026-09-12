## Kimigayo
Work in progress

See [SPEC.md](SPEC.md) for language rules and [STATUS.md](STATUS.md) for executable coverage.

Build and test with the .NET 10 SDK:

```powershell
dotnet build Kimigayo.slnx -c Release
dotnet test xUnitTest/xUnitTest.csproj -c Release
```

Publish the compiler with NativeAOT on Windows x64 using the .NET NativeAOT C++ toolchain prerequisites:

```powershell
dotnet publish Kimi/Kimi.csproj -c Release -r win-x64 -p:PublishAot=true -o bin/native-compiler
bin/native-compiler/Kimi.exe build examples/Counter/Counter.kimiproj --LlvmBin C:/App/llvm
bin/native-compiler/Kimi.exe run examples/Counter/Counter.kimiproj
```

The compiler's NativeAOT toolchain is separate from the pinned LLVM tools and backend archive used to compile Kimigayo programs. See [backend setup](backend/windows-x64/README.md) for those inputs. `IsAotCompatible` enables trimming/AOT analysis in ordinary compiler builds; CLI registration and LSP serialization use statically registered metadata.

After generating test fixtures, verify native scalar execution and the published compiler:

```powershell
backend/windows-x64/test-scalars.ps1 -LlvmBin C:/App/llvm
backend/windows-x64/test-cli.ps1 -LlvmBin C:/App/llvm -NativeCompiler bin/native-compiler/Kimi.exe
backend/windows-x64/test-lsp.ps1 -CompilerPath bin/native-compiler/Kimi.exe
```
