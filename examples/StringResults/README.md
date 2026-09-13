# String results

String-valued selections and named do exits secure their result before cleanup. This example moves `original` into a nested result, reinitializes it in defer, then prints the secured `before` and the new `after`. A conditional self-replacement prints `new` last. Each line ends with LF.

```powershell
dotnet run --project Kimi -c Release --no-build -- build examples/StringResults/StringResults.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release --no-build -- run examples/StringResults/StringResults.kimiproj
```

Requires the pinned tools and backend archive in [backend setup](../../backend/windows-x64/README.md). A common result slot receives only the selected value; cleanup must complete before the enclosing expression can consume it. Abort or nontermination during cleanup prevents delivery and subsequent destruction. String results from if/else, yield, do and valued loop exits can initialize/replace locals, enter another result, or be consumed directly by Core.writeLine.

User-function string parameters/results, concatenation/interpolation, string comparisons, explicit ownership adaptations and Heap-string construction remain unsupported. See [STATUS C.49](../../STATUS.md#c49-owned-string-control-flow-results-2026-09-13).
