# Results

An implicit Application using i32 `if` results inside a loop, labeled `loop`/`do` exits, Boolean `yield` and short-circuit evaluation. It prints `ok` followed by LF. Result temporaries use typed SSA phi instructions even at O0; local storage is allocated in the entry block.

```powershell
dotnet run --project Kimi -- build examples/Results/Results.kimiproj
dotnet run --project Kimi -- run examples/Results/Results.kimiproj
```

The published NativeAOT compiler accepts the same commands. Requires the pinned LLVM tools and verified Windows backend archive described in [backend setup](../../backend/windows-x64/README.md). See [STATUS.md C.41](../../STATUS.md#4-llvm-generation-coverage) for implementation scope and validation.
