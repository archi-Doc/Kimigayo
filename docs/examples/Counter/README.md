# Counter

An executable implicit Application exercising i32 locals, checked addition, a while loop and a Unit if/else. It prints `tick` three times, then `done`, each followed by LF.

```powershell
dotnet run --project src/Kimi -- build docs/examples/Counter/Counter.kimiproj
dotnet run --project src/Kimi -- run docs/examples/Counter/Counter.kimiproj
```

Requires the pinned LLVM tools and the verified Windows backend archive described in [backend setup](../../../src/backend/windows-x64/README.md). See [STATUS.md C.40](../../STATUS.md#4-llvm-generation-coverage) for supported operations and limits.
