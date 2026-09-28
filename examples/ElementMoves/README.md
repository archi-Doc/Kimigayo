# Partial Move and repair

Owned tuples and fixed arrays rooted in a constructed local `let` or `var` support acquisition of string elements and supported nested Non-Copy aggregates. Tuple selectors and in-range integer literal array indices identify static Move Paths; parentheses, integer bases and separators preserve their numeric identity.

```kimi
var pair = ("first", 42)
let taken = pair.0
let number = pair.1
pair.0 = "replacement"
let complete = pair
```

Moving an element makes its ancestors incomplete. Remaining initialized siblings stay usable; whole-value acquisition or borrowing requires completeness. Repairing every missing part restores completeness. A `let` can supply a Move but cannot be repaired. Moving a whole child prevents navigation into that child's former storage; replace the child itself before navigating through it again.

Simple assignment keeps its RHS-first order. `pair.0 = pair.0` secures the RHS, locates the destination, establishes its exclusive Loan, skips destruction of the moved old element, and installs the secured value. Replacing a partially moved parent destroys only its remaining parts. Numeric updates keep the existing location → exclusive Loan → old-value Copy → RHS → calculation → store order.

Cleanup visits remaining parts in reverse logical order, including after branches, loops, transfers and `defer`. Untouched array ranges use reverse loops; complete parts reuse the existing Type-specific destructor. Runtime flags are needed only for conditionally live remainders. Bounds Abort does not unwind; nonterminating cleanup prevents later cleanup and placement.

The implementation tracks referenced paths rather than expanding all declared array elements. Copy acquisition remains Copy. Dynamic Copy reads and replacements require a complete known prefix; dynamic Non-Copy extraction remains unsupported. Initial construction of an uninitialized array one element at a time remains forbidden. Static local string elements now support [comparison and shared arguments](../ElementBorrows/README.md) without Move, including remaining initialized elements of partially moved parents. [Owned parameters and temporaries also support string element borrowing](../ElementBorrowOwners/README.md), and [owned parameters support static element partial Move](../ElementParameterMoves/README.md). Partial Move from temporaries remains unsupported. Borrowed receivers, struct/Property paths, user `deinit`, retained borrow dependencies and dynamic collections are outside this implementation unit.

From the repository root, with the Windows backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementMoves/ElementMoves.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementMoves/ElementMoves.kimiproj
```

Expected program output:

```text
taken
element moves complete
```

Exit is 0 with empty stderr. Tests audit logical destruction of Static-backed strings; this example does not add Heap-string construction APIs. See [Move Paths](../../spec/15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move), [destruction](../../spec/16-scope-exit-and-destruction.md), and [implementation status](../../STATUS.md).
