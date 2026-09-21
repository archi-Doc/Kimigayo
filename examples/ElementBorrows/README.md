# Shared string element access

Constructed owned tuples and fixed arrays support inspecting static string elements with all six comparisons and passing them to required `ref/string` parameters of supported direct functions. Owners may be locals, owned parameters, literals, direct function results, or `if`/`do`/`loop`/`match` results. These operations borrow the element's existing handle; they do not Move it or change destruction responsibility. See [parameter and temporary examples](../ElementBorrowOwners/README.md).

```kimi
func same(! left: ref/string, right: ref/string) -> bool => left == right
var pair = ("first", "last")
let equal = same(pair.0, pair.0)
pair.0 = "new"
let taken = pair.1
let remaining = pair.0 == "new"
```

Each comparison operand or shared argument starts its Loan before later operands/arguments. It remains active through the comparison or call, including callee cleanup. Normal calls release their argument Loans after securing an independent result. Normal control transfers release abandoned Loans before cleanup, retaining enclosing Loans. Abort does not unwind.

Receiver location retains root protection. After bounds resolution, the selected element's Loan permits shared access and statically disjoint sibling operations. Overlapping Move, replacement, destruction, and exclusive access are rejected. A partially moved parent may supply a remaining initialized element; a moved element or moved ancestor cannot be borrowed.

This executable unit supports tuple selectors, in-range integer literal fixed-array indices (including parentheses, bases and separators), and nested combinations ending in owned string. Partial Move supports locals and [owned parameters](../ElementParameterMoves/README.md); element updates require mutable locals. Dynamic indices for Non-Copy borrowing, borrowed receivers, explicit `@ref`, `uniq`, saved or returned references, aggregate borrowing and dynamic collections remain outside this unit. The limits do not change the language rules.

From the repository root, with the Windows backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementBorrows/ElementBorrows.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementBorrows/ElementBorrows.kimiproj
```

Expected output (exit 0, empty stderr):

```text
shared element
beta
element borrows complete
```

See [comparison](../../spec/13-operators-and-assignment.md#134-comparison-and-logical-operators), [Loan conflicts](../../spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), and [implementation status](../../STATUS.md). Destruction audits cover logical responsibility of Static-backed strings, not a new Heap-string construction API.
