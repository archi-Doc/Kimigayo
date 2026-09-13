# Results

An implicit Application using i32 `if` results inside a loop, labeled `loop`/`do` exits, Boolean `yield` and short-circuit evaluation. It prints `ok` followed by LF. Result temporaries use typed SSA phi instructions even at O0; local storage is allocated in the entry block.

```powershell
dotnet run --project Kimi -- build examples/Results/Results.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -- run examples/Results/Results.kimiproj
```

The published NativeAOT compiler accepts the same commands. Requires the pinned LLVM tools and verified Windows backend archive described in [backend setup](../../backend/windows-x64/README.md). See [STATUS.md C.41](../../STATUS.md#c41-scalar-selection-and-loop-results-2026-09-13) for implementation scope and validation.
