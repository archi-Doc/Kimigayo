# String comparisons

Strings support `==`, `!=`, `<`, `<=`, `>` and `>=` without consuming either operand. Ordering compares exact UTF-8 bytes, with no normalization or locale rules. A local or parameter is protected by a shared comparison Loan while later operands are evaluated. A conflicting Move or replacement is rejected; the Loan ends when the comparison completes or a transfer abandons it.

This example compares a local with a literal and a function result, then moves the still-live local to `writeLine`. It prints `equal`, `ordered` and `apple`, each followed by LF, and exits 0.

```powershell
dotnet run --project Kimi -c Release --no-build -- build examples/StringComparisons/StringComparisons.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release --no-build -- run examples/StringComparisons/StringComparisons.kimiproj
```

Requires the pinned tools and ABI 2 backend archive from [backend setup](../../backend/windows-x64/README.md). Equality checks lengths before comparing data. Ordering uses backend `memcmp` for the common prefix, then compares lengths. Neither path allocates or reads the release tag or padding.

These implicit Loans do not add general borrow execution, string concatenation, interpolation or Heap construction. See [STATUS C.51](../../STATUS.md#c51-string-comparisons-and-backend-memcmp-2026-09-13).
