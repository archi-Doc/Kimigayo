# Disjoint element paths

An exclusive update protects its selected element path. Distinct tuple elements and distinct in-range literal indices of fixed arrays are statically disjoint, including nested paths. This allows `values[0] += values[1]` and `values[0] += values[1]++`. The old value and destination are evaluated once, and the exclusive Loan lasts through the store.

Static array indices follow SPEC's literal-only rule: `1`, `(1)` and `0x1` identify the same element. Names, `+1`, `1 + 0`, conversions and conditional expressions do not supply static indices, even if an optimizer can determine their values. Out-of-range literals retain their ordinary runtime bounds checks.

A dynamic index conservatively covers its known parent subtree. Thus `groups.1[index]` and `groups.2[index]` can be disjoint because the tuple elements differ. `values[i]` and `values[j]` cannot establish disjointness from a runtime `i != j` test. Static selectors following a dynamic index do not recover precision in this implementation.

Locating a receiver does not read or Copy the whole aggregate. The final Copy read or store is checked against its path. A whole-parent Copy, Move or replacement still conflicts with an exclusive Loan into that parent. Equal paths and ancestor/descendant paths overlap. For example, `values[0] += values[0]` remains invalid.

Receiver storage stays protected against any writes, Moves or destruction during index evaluation. This protection is distinct from an element's exclusive Loan; disjoint-path support does not allow writes from an index expression while that root is being located. Independent nested updates end only their own Loans. Normal abandoning transfers release abandoned Loans before cleanup; Abort does not run cleanup.

With the Windows backend prepared, run from the repository root:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementPaths/ElementPaths.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementPaths/ElementPaths.kimiproj
```

Expected stdout is `element paths complete` followed by a newline, exit 0 and empty stderr.

This unit introduced disjointness for Copy-element operations on complete owned tuples and fixed arrays. [ElementReplacements](../ElementReplacements/README.md) extends it to owned Non-Copy replacement. Partial Move, per-element initialization/destruction state, Non-Copy extraction, Properties, struct fields, references, Slices and dynamic collections remain unsupported. See [static paths](../../spec/15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move), [overlap rules](../../spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts) and [receiver protection](../../spec/04-arrays-indexing-and-slices.md#464-bounds-evaluation-and-failure).
