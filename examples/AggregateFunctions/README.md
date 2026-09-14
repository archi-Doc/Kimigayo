# Tuple and fixed-array function values

Ordinary functions acquire whole tuple/array arguments and deliver their result only after deferred cleanup. Nested calls reuse acquired temporaries; unused results are destroyed at the end of their expression. Zero-sized values retain ownership even when no bytes or physical arguments are needed.

From the repository root, with the backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release -- build examples/AggregateFunctions/AggregateFunctions.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release -- run examples/AggregateFunctions/AggregateFunctions.kimiproj
```

Expected stdout, exit code 0, and empty stderr:

```text
leaving echo
leaving echo
aggregate functions complete
```

Array arguments use explicitly typed locals because contextual array-literal inference in calls remains incomplete. Copy element reads are demonstrated in [ElementReads](../ElementReads/README.md). Aggregate borrows, element writes, and partial Move are not implemented.
