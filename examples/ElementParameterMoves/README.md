# Partial Move from owned parameters

Owned tuple and fixed-array parameters support moving string elements and supported nested Non-Copy aggregates through static paths.

```kimi
func first(pair: (string, string)) -> string => pair.0
```

The selected element's responsibility moves into the result. Callee cleanup destroys the remaining element before delivering that result. The caller does not destroy the consumed argument again. Parameter storage is used directly without an additional whole-aggregate transfer.

After a partial Move, remaining initialized elements may be read, borrowed, or moved. The incomplete whole value and moved elements cannot be acquired. Conditional Moves use path flags initialized when the parameter is received. Cleanup visits remaining parts in reverse logical order; Abort does not unwind, and divergent cleanup prevents result delivery.

Parameters are immutable let-like bindings: reassignment, element updates and reinitialization are forbidden. Move to a `var` local before mutable work. Static fixed-array paths use only in-range integer literals, including parentheses, integer bases and separators. Dynamic indices, temporary/result partial Move, borrowed receivers, user `deinit` and general struct/Property paths are outside this implementation unit.

From the repository root, with the Windows backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementParameterMoves/ElementParameterMoves.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementParameterMoves/ElementParameterMoves.kimiproj
```

Expected program output (exit 0, empty stderr):

```text
first
last
middle
```

See [parameter rules](../../spec/07-functions-and-callable-values.md), [static Move Paths](../../spec/15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move), [local Move and repair](../ElementMoves/README.md), and [implementation status](../../STATUS.md).
