# Functions

A Unit-returning explicit `public func main()` calls ordinary i32 functions. The example checks source-order acquisition of reordered named arguments, shallow recursion, and a return value secured before deferred mutation. It prints `ok` and `done`, each followed by LF.

```powershell
dotnet run --project Kimi -c Release -- build examples/Functions/Functions.kimiproj
dotnet run --project Kimi -c Release -- run examples/Functions/Functions.kimiproj
```

Requires the pinned LLVM tools and verified Windows backend archive described in [backend setup](../../backend/windows-x64/README.md). Parameters/results use direct scalar SSA values; ordinary mutable locals retain their existing slots. Recursion is intentionally shallow: stack exhaustion has no specified Kimi Abort/recovery contract.

See [STATUS.md C.44](../../STATUS.md#4-llvm-generation-coverage) for supported signatures and remaining ownership boundaries. This increment does not cover captures, default arguments, aggregate function signatures, or indirect calls.
