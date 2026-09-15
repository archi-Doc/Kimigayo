# Tuple and fixed-array results

The conditional acquires a whole tuple before replacing `pair`. The do expression secures that tuple, runs its deferred cleanup and then delivers it to `saved`. A loop similarly delivers a fixed array. Scope cleanup destroys the acquired values in reverse logical order.

From the repository root, with the backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release -- build examples/AggregateResults/AggregateResults.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release -- run examples/AggregateResults/AggregateResults.kimiproj
```

Expected stdout, with exit code 0 and empty stderr:

```text
cleanup
aggregate results complete
```

Supported results include nested and zero-sized tuples/arrays, existing match Subjects and guards, and ordinary transfer cleanup. Aggregate function parameters/results are demonstrated in [AggregateFunctions](../AggregateFunctions/README.md). Element access and partial Move remain pending.
