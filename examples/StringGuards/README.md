# String guards and temporary borrows

The guard reads `value` as `ref/string`. A selected body instead acquires the owned string from the Subject. The `wanted` argument borrows an existing temporary handle without a second handle copy; its lifetime ends at the enclosing expression boundary.

From the repository root, with the backend archive prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release -- build examples/StringGuards/StringGuards.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release -- run examples/StringGuards/StringGuards.kimiproj
```

Expected output:

```text
hello
other
```

This C.55 increment covers owned string Subjects with whole-value Patterns, guard candidate reads and independent direct-call results. General aggregate execution, borrowed storage/results, borrowed Subjects and decomposition remain pending.
