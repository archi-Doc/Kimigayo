# Copy reads from tuples and fixed arrays

Tuple selectors use decimal positions, such as `pair.0`. Fixed-array indices use `isize`; typed integers of other Types need an explicit conversion. Each index is checked before its element is accessed. Invalid bounds abort with `KIMI_E_INDEX_BOUNDS`, even for a constant index or an empty array.

Chained access locates each element without copying intermediate aggregates. The final scalar or Copy aggregate is acquired while its root is protected from modification, Move, and destruction. A temporary owner's cleanup follows the ordinary expression lifetime.

From the repository root, with the backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release -- build examples/ElementReads/ElementReads.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release -- run examples/ElementReads/ElementReads.kimiproj
```

Expected stdout is `element reads complete` followed by a newline, with exit code 0 and empty stderr.

This example reads a Copy tuple from a temporary containing a string and iterates over a fixed array. Element writes, Non-Copy element acquisition, partial Move, and dynamic collection indexing remain unsupported.
