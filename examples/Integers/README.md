# Integers

An explicit Unit main passes negative i8 and high-bit u8 arguments, divides the maximum u64 value, shifts a u8 using an i64 count, and returns a secured u64 before deferred mutation. It prints `ok` and `done`, each followed by LF.

```powershell
dotnet run --project Kimi -c Release -- build examples/Integers/Integers.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release -- run examples/Integers/Integers.kimiproj
```

Requires the pinned LLVM tools and verified Windows backend archive described in [backend setup](../../backend/windows-x64/README.md). Supported integer Types are i8/u8, i16/u16, i32/u32, i64/u64 and isize/usize (64 bits on this target). Arithmetic checks use each Type's width and signedness. Shift counts are checked in their original Type before width conversion; untyped count literals fit the left Type.

See [STATUS.md C.46](../../STATUS.md#c46-integer-execution-through-64-bits-2026-09-13). General integer conversions, i128/u128, floating-point execution, char operations and the existing capture/ownership boundaries remain outside this increment.
