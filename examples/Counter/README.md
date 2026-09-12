# Counter

An executable implicit Application exercising i32 locals, checked addition, a while loop and a Unit if/else. It prints `tick` three times, then `done`, each followed by LF.

```powershell
dotnet run --project Kimi -- build examples/Counter/Counter.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -- run examples/Counter/Counter.kimiproj
```

Requires the pinned LLVM tools and the verified Windows backend archive described in [backend setup](../../backend/windows-x64/README.md). See [STATUS.md C.40](../../STATUS.md#c40-scalar-control-flow-emission-and-nativeaot-2026-09-13) for supported operations and limits.
