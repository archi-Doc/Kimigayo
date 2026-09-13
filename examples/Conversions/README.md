# Conversions

Explicit integer adaptation with `E@T`: direct literal fitting, signed and unsigned widening, checked narrowing, mixed-width shifts, and a secured return before deferred mutation. Prints `ok` followed by LF.

```powershell
dotnet run --project Kimi -c Release --no-build -- build examples/Conversions/Conversions.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release --no-build -- run examples/Conversions/Conversions.kimiproj
```

Requires the pinned tools and Windows backend archive in [backend setup](../../backend/windows-x64/README.md). Targets are i8/u8, i16/u16, i32/u32, i64/u64 and isize/usize. A typed value outside the destination's mathematical range Aborts with `KIMI_E_INT_CONVERSION`; `256@u8` is instead a static literal error. `(200 + 100)@u8` computes in i32 and then Aborts on conversion.

Conversions evaluate their operand once. Checks precede truncation and result delivery; Abort does not run pending cleanup. Same-width adaptations retain their language Types and range checks without copying bits. There are no implicit integer conversions. Floating-point conversions, i128/u128 and ownership adaptations remain pending. See [STATUS C.47](../../STATUS.md#c47-explicit-integer-conversions-2026-09-13).
