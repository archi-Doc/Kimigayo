# Owned element replacement

Complete owned tuples and fixed arrays rooted in an initialized local `var` support replacement of string elements and nested aggregates containing strings. Fixed arrays accept runtime `isize` indices as well as literal indices. The right side can construct a value, Move an independent local, or obtain a function or control-flow result.

For `values[index()] = make()`, execution is:

1. Evaluate and secure `make()`, transferring its ownership responsibility.
2. Locate `values`, evaluate `index()` once, and check bounds.
3. Establish the selected element's exclusive Loan.
4. Destroy the old element by its exact Type, then transfer the secured value into its storage.
5. Release that Loan and return Unit.

The whole parent remains complete after replacement. Its eventual cleanup destroys the new element and the unaffected siblings in reverse logical order. The consumed right-side temporary is not destroyed again. Replacement introduces no intermediate copy of the old element, per-element live flags, or runtime Loan locks. String transfer copies the handle fields and transfers responsibility; it does not clone the text.

Receiver storage remains protected throughout index evaluation. After location, [static disjoint paths](../ElementPaths/README.md) allow replacing a sibling during an outer numeric update. Overlapping paths and whole-parent operations still conflict. Copy-element assignment uses the same exclusive-write lifetime and preserves its RHS-first order.

An ordinary transfer during index evaluation cleans the secured input under the normal temporary-lifetime rules without replacing the old element. Bounds Abort performs no cleanup. A nonterminating RHS/index cleanup prevents later location, destruction, or placement as applicable. Replacement never rolls back prior side effects or Moves.

This unit does not add Non-Copy element extraction, partial Move/repair, borrowing results, borrowed/temporary receivers, struct/Property operations, user-defined destructors, or dynamic collections. Static Non-Copy extraction, repair, and `values[0] = values[0]` are now supported by the subsequent [partial Move unit](../ElementMoves/README.md). Supported owned values have no retained borrow dependencies. General Loan/Origin-dependent replacement remains outside this executable subset.

With the Windows backend prepared, run from the repository root:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementReplacements/ElementReplacements.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementReplacements/ElementReplacements.kimiproj
```

Expected stdout is `element replacements complete` followed by a newline, exit 0, and empty stderr.

The tests audit logical string destruction counts and order, including Static-backed strings. They do not establish source-level Heap-string construction support. See [simple assignment](../../spec/13-operators-and-assignment.md#1371-simple-assignment), [index protection](../../spec/04-arrays-indexing-and-slices.md#464-bounds-evaluation-and-failure), and [implementation status](../../STATUS.md).
