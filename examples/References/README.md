# Shared string arguments

This C.54 example borrows owned string locals, forwards shared parameters, and compares UTF-8 contents. Both owners remain usable after the calls.

From the repository root, with the backend archive prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release -- build examples/References/References.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi/Kimi.csproj -c Release -- run examples/References/References.kimiproj
```

Expected output:

```text
equal
hello
hello
```

The current slice supports required `ref/string` parameters of direct functions, owner-local/parameter argument borrowing, reference-parameter forwarding and all six string comparisons. A reference points to the string handle; it neither copies nor destroys that handle. Calls return only independent values.

Borrowed locals/results, explicit `@ref`, temporary materialization, `uniq`, static/captured/indirect access and string guard candidates remain outside execution coverage. Borrowing an owned temporary participates in Binding and overload selection, then receives an unsupported ownership diagnostic. The compiler does not silently choose another overload to avoid this implementation limit.
