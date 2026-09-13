# Cleanup

An implicit Application demonstrating a secured i32 result, reverse deferred execution, a nested defer, and self-targeted `exit` from cleanup. It prints `cleanup` and `ok`, each followed by LF. The result retains `1` while cleanup changes the original local to `2`.

```powershell
dotnet run --project Kimi -- build examples/Cleanup/Cleanup.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -- run examples/Cleanup/Cleanup.kimiproj
```

The published NativeAOT compiler accepts the same commands. Requires the pinned LLVM tools and verified Windows backend archive described in [backend setup](../../backend/windows-x64/README.md). See [STATUS.md C.42](../../STATUS.md#c42-deferred-cleanup-execution-2026-09-13) for implementation scope and limits.
