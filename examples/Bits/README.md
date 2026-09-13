# Bits

An explicit Unit main uses i32 masks, compound bit operations, checked shifts, and a return value secured before deferred mutation. It prints `ok` and `done`, each followed by LF.

```powershell
dotnet run --project Kimi -c Release -- build examples/Bits/Bits.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release -- run examples/Bits/Bits.kimiproj
```

Requires the pinned LLVM tools and verified Windows backend archive described in [backend setup](../../backend/windows-x64/README.md). Shift counts must be between 0 and 31 for i32; invalid counts Abort with `KIMI_E_INT_SHIFT_COUNT`. Left shifts discard high bits without overflow, and signed right shifts extend the sign bit (`-3 >> 1` is `-2`). Bit operations do not accept bool; Boolean `and`/`or` retain short-circuit evaluation.

See [STATUS.md C.45](../../STATUS.md#c45-i32-bitwise-operations-and-checked-shifts-2026-09-13) for coverage. This increment executes i32 values and i32 shift counts; Binding still permits independent integer count Types as specified.
