# Numeric element updates

Initialized owned tuples and fixed arrays rooted in a local `var` support numeric compound assignment and integer prefix/postfix increment and decrement. Nested paths and parents containing owned strings are supported.

`values[index()] += amount()` evaluates the destination path once, checks bounds, reads the old element once, evaluates `amount()`, computes the sum, and writes once. Compound assignment returns Unit. Prefix `++`/`--` returns the new value and postfix returns the old value, after a successful write. These operations use the existing scalar arithmetic and storage representations.

Integer overflow, division/remainder by zero, signed minimum divided or reduced modulo −1, and invalid shift counts Abort before storing or producing the update result. Abort does not run cleanup. Shift counts retain their own integer Type through validation. Floating-point `+= -= *= /=` follows IEEE 754, including NaN, infinity and signed zero. The initial profile still excludes i128/u128 division and remainder.

Index evaluation uses shared access protection, allowing reads such as `values[values[0]]`. After bounds resolution, an exclusive Loan protects the update through the old-value read, RHS, computation and store. The Loan ends after storing and before producing the prefix/postfix result. Aliased RHS reads such as `values[0] += values[0]` are rejected. Copy the amount into a local before updating, or use RHS-first simple assignment `values[0] = values[0] + values[0]`.

This implementation conservatively protects the entire parent root: RHS reads, Copies, writes and Moves of sibling elements are also rejected. The language's static disjoint-path rules are not yet implemented here. Different parent roots can be updated, for example `a[0] += b[0]++`. Existing enclosing Loans remain active; an update cannot acquire exclusivity while another access protects the same parent. An abandoning control transfer ends the abandoned update's Loan before its cleanup, so that cleanup can access the parent again. Deferred cleanup within a normally completing RHS still runs under the update's exclusive Loan.

From the repository root, with the backend prepared:

```powershell
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/ElementUpdates/ElementUpdates.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/ElementUpdates/ElementUpdates.kimiproj
```

Expected stdout is `element updates complete` followed by a newline, with exit code 0 and empty stderr.

Non-Copy element acquisition/replacement, partial Move, incomplete parents, borrowed/temporary receivers, Properties and dynamic collections remain outside this implementation unit. Language rules are defined in [unary operators](../../spec/13-operators-and-assignment.md#132-unary-operators), [arithmetic](../../spec/13-operators-and-assignment.md#133-arithmetic-bitwise-and-shift-operators), and [compound assignment](../../spec/13-operators-and-assignment.md#1372-compound-assignment).
