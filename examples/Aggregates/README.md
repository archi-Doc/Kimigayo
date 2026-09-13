# Whole tuples and fixed arrays

This example constructs, copies or moves, replaces and destroys whole aggregate values. String-containing aggregates are moved; integer arrays are copied. Components are destroyed in reverse logical order, including inside nested aggregates and loop iterations.

From the repository root, with the backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release -- build examples/Aggregates/Aggregates.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release -- run examples/Aggregates/Aggregates.kimiproj
```

Expected output: `aggregates complete`, followed by a newline; exit code 0.

C.56 covers local whole values. Element access, destructuring, aggregate parameters/results, struct/enum execution and dynamic collections remain pending.
