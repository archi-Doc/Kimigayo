# Owned string functions

Ordinary functions can receive and return owned strings. Passing a local moves it; an unused parameter or discarded call result is destroyed. This example combines nested calls, named arguments, a result secured before deferred cleanup, and local reinitialization. It prints `cleanup` and `after`, each followed by LF, and exits 0.

```powershell
dotnet run --project Kimi -c Release --no-build -- build examples/StringFunctions/StringFunctions.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release --no-build -- run examples/StringFunctions/StringFunctions.kimiproj
```

Requires the pinned tools and backend archive in [backend setup](../../backend/windows-x64/README.md). The compiler currently passes string arguments through acquired slots and returns strings through a separate hidden result slot. These are implementation choices, not a public ABI. Cleanup must complete before the caller receives the result; Abort and nontermination do not deliver it.

Borrowed/generic/default signatures, indirect calls, string comparisons, concatenation/interpolation, explicit ownership adaptations and Heap-string construction remain unsupported. See [STATUS C.50](../../STATUS.md#c50-owned-string-function-parameters-and-results-2026-09-13).
