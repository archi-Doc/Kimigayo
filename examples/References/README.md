# Shared string arguments

This C.54 example borrows owned string locals, forwards shared parameters, and compares UTF-8 contents. Both owners remain usable after the calls.

From the repository root, with the backend archive prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release -- build examples/References/References.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release -- run examples/References/References.kimiproj
```

Expected output:

```text
equal
hello
hello
```

The current slice supports required `ref/string` parameters of direct functions, owner-local/parameter argument borrowing, reference-parameter forwarding and all six string comparisons. A reference points to the string handle; it neither copies nor destroys that handle. Calls return only independent values.

C.55 also supports borrowing owned string temporaries and reading string guard candidates; see [StringGuards](../StringGuards/README.md). Borrowed locals/results, explicit `@ref`, `uniq` and static/captured/indirect access remain outside execution coverage. Unsupported operations do not trigger another overload selection.
