# String element borrowing from owned parameters and temporaries

Static string elements of owned tuples and fixed arrays can be compared or passed to required `ref/string` parameters. This example uses owned parameters, tuple literals, direct function results, and `if`/`do` results; `loop` and `match` results use the same delivery checks.

```kimi
func same(! a: ref/string, b: ref/string) -> bool => a == b
func make() -> (string, i32) => ("held", 42)
let equal = same(make().0, ("held", 0).0)
```

The receiver is evaluated once. Its completed owner storage supplies the element address without an owned element copy or Move. Call results become available only after normal return, and selection results only after their cleanup and Join. The Loan ends after comparison or normal call result delivery. Ending a Loan does not shorten the owner's specified temporary lifetime.

Conditional cleanup handles both evaluated and skipped short-circuit operands. Completion activates an aggregate's existing lifetime flag; cleanup clears it. Entry initialization covers paths that skip the owner entirely. Normal transfers release abandoned Loans before cleanup, while Abort does not unwind.

Owned parameters also support [static element partial Move](../ElementParameterMoves/README.md). Their immutability forbids updates and reinitialization. Dynamic Non-Copy indices, borrowed receivers, explicit `@ref`/`uniq`, saved/returned references, and temporary element partial Move or updates remain unsupported. Fixed-array literals still need existing type context; direct array literal receiver inference is not extended. A typed local or a function with a fixed-array result supplies that context.

From the repository root, with the Windows backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementBorrowOwners/ElementBorrowOwners.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementBorrowOwners/ElementBorrowOwners.kimiproj
```

Expected program output (exit 0, empty stderr):

```text
owned parameter
temporary owners
conditional lifetime
delivered result
```

See [local element borrowing](../ElementBorrows/README.md), [temporary lifetimes](../../spec/03-types-and-values.md#36-temporary-values-places-and-lifetimes), [destruction](../../spec/16-scope-exit-and-destruction.md), and [implementation status](../../STATUS.md). Native audits verify the logical destruction responsibility of Static-backed strings.
