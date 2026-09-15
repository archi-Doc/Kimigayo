# Copy element assignment

Initialized owned tuples and fixed arrays rooted in a local `var` support simple `=` replacement of Copy elements. The parent may contain strings; its other elements and destruction responsibility remain intact. A Copy tuple or fixed array can also replace an entire nested element.

Simple assignment secures the right side first, then evaluates the destination path once from left to right. For `values[index()] = make()`, `make()` runs before `index()`. Each array access checks bounds before forming its element address; Unit and zero-size elements retain these checks and all source effects. Assignment returns Unit.

Destination access protects the parent during index evaluation. Moving, replacing or writing that protected parent or its elements is currently rejected, including from an index's deferred cleanup. This conservative root protection does not implement general disjoint-path borrowing. The assignment releases only its own access protection immediately before storing; unrelated Loans remain active.

From the repository root, with the backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementAssignments/ElementAssignments.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementAssignments/ElementAssignments.kimiproj
```

Expected stdout is `element assignments complete` followed by a newline, with exit code 0 and empty stderr.

Numeric compound updates and integer increment/decrement are covered in [ElementUpdates](../ElementUpdates/README.md). Non-Copy element replacement/acquisition, partial Move, incomplete parents, borrowed/temporary receivers, and dynamic collections remain outside this implementation unit. These are implementation limits; the language rules are in [assignment](../../spec/13-operators-and-assignment.md#137-assignment) and [element Places](../../spec/04-arrays-indexing-and-slices.md#461-access-and-length-metadata).
