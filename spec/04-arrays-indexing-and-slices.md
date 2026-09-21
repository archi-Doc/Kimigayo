# 4. Arrays, indexing, and slices

[Specification index](../SPEC.md)

A fixed array `[N of T]` has a compile-time length `N` and a complete element Type `T`, including Semantics and Origins. A dynamic `Array<T>` owns variable-length storage, and `Slice<T>{source}` is a shared borrowed view. Their [indexing and slicing](#46-indexing-and-slicing) rules are defined together.

## 4.1. Fixed-array identity and layout

Type identity includes the evaluated length and the complete element Type. Thus `[3 of i32]` differs from `[4 of i32]`, while `[(2 + 2) of i32]` equals `[4 of i32]`. There are no implicit conversions between arrays of different lengths, between fixed arrays and Array, or between owning arrays and Slice.

Elements are stored inline in index order, without implicit heap allocation for the array's own element storage. Nested arrays apply this rule recursively, with the innermost index varying contiguously. Where the enclosing value is stored, and any allocation performed by individual elements, follow their own rules.

The element layout must be finite and valid. Let `d = stride(T)`; Slice uses the same element spacing. These are specification quantities, not source operators, and follow the [common layout rules](21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi).

| Quantity | Layout of `[N of T]` |
| --- | --- |
| Offset of element `i` | `i * d`, for `0 <= i < N` |
| Alignment | `alignment(T)`, including when `N = 0` |
| Array stride | `N * d`, the spacing between consecutive array values |
| Size | `N * d`, including all element-stride padding |
| Zero-sized elements | Use `T`'s stride; if `d = 0`, distinct logical elements may share an address |

Size, stride and padding calculations are checked against the target's layout limits and nonnegative `isize`; an unrepresentable concrete layout is a compile-time error. `[0 of T]` has size and stride zero and initializes and destroys no elements, but `T`'s Type, layout and ownership are still checked. Inline embedding is validated before multiplying by zero: a struct that directly stores `[0 of Self]` has a forbidden recursive value layout. Pointer and reference referents do not create inline edges.

## 4.2. Length constants

A length is a nonnegative compile-time integer representable in the target's `isize`. A generic length remains symbolic until instantiation. A length is written as an integer literal, a possibly qualified constant name, a length parameter, or a parenthesized integer constant expression; any compound expression requires parentheses around the entire length.

The initial evaluator admits integer literals, length parameters, **Constant-readable Bindings**, grouping, unary `+` and `-`, and binary `+`, `-`, `*`, `/` and `%`. A Constant-readable Binding is an integer `let` local, or a static stored Property with accessible standard `get`, whose declaration initializer can be evaluated recursively using only these forms, after the normal lookup, access and initialization checks. Parameters, `var`, instance Fields, custom and computed accessors, calls and cyclic initializers are excluded. This is a semantic classification; it neither treats `let` as a general constant nor adds `const` syntax.

Length evaluation uses checked integer arithmetic. Typed constants and length parameters (`isize`) are resolved first. Established operand Types guide unresolved literals and literal-only subexpressions under same-Type arithmetic; without such evidence they default to `isize`. Typed constants keep their Type. Different established integer Types are incompatible even when their widths or values match, and the final nonnegative-`isize` range check does not convert operands. Intermediate overflow is checked in the arithmetic Type, including `i32` inferred for an ordinary binding.

Signed intermediate values may be negative. A negative or out-of-range final length, a noninteger value, overflow, and a zero divisor are compile-time errors. Evaluation is independent of optimization and does not extend ordinary or directive constant evaluation. Type formation runs no static initializer or getter.

```kimi
let Width: isize = 4
let Height: isize = 3
let buffer: [(Width * Height) of u8] // Length 12; still Uninitialized.
let row: [Width of u8] = [1, 2, 3, 4]
let badSyntax: [Width * Height of u8] // Error: parentheses required.
let negative: [(-1) of u8]           // Error: negative length.
let invalid: [(4 / 0) of u8]         // Error: division by zero.
let of = 4                          // Ordinary Name outside the Type delimiter.
```

For an ordinary `let Small = 4`, `Small` is `i32`, so `[(Small * 2) of u8]` computes in `i32` and then checks the final length range. Combining `Small` with an independently typed `isize` operand is invalid; annotate length constants as `isize` when native-width arithmetic is intended. Length expressions introduce no extra conversion syntax.

The root-level bindings above are implicit startup locals (§22.2), even when Constant-readable. In a Library, an explicit-`main` Application, or a declaration-only document, put reusable constants in a group:

```kimi
group Dimensions
    public let Width: isize = 4
    public let Height: isize = 3
```

A length expression may then use `Dimensions.Width`. Reading it at compile time does not run its static initializer; runtime access keeps the ordinary first-access initialization.

**Separate compilation and access.** Separate-compilation artifacts keep folded integer values and their Types, or verified slot-dependent expressions and formation obligations. They record the referenced declaration identities and dependencies; a change invalidates dependent artifacts and recomputes Type, layout and selection keys. Length changes have no ABI compatibility guarantee.

Constant accessibility is checked at the definition. A private constant need not become public when its value can be exported without private-name lookup: it is expanded at the definition, and conditions checkable by clients are exported. The expanded value then becomes part of every public API Type or formation condition that uses it. Private access protects the declaration's Name, not the secrecy or compatibility of an exported value; changing the value may break callers and requires dependent invalidation and rebuilding. This exception is intentional, because a folded length contains an integer rather than a private Type or a client-side reference to the private declaration. No compatibility warning is mandated; an implementation may offer an API-change diagnostic. Ordinary API accessibility still applies to element Types and other signature contents.

## 4.3. Initialization and inference

With an expected fixed-array Type, an array literal constructs that Type and must contain exactly `N` elements; there is no padding or truncation. Elements are acquired in source order by ordinary Copy or Move. A local binding annotation may use `[N of _]`, recursively for nested arrays, to infer only the element Type from its initializer; a unique Type is required at the declaration. The placeholder is forbidden in lengths, signatures and explicit generic arguments.

Without a fixed-array expectation, an independent array literal constructs an Array. A literal in a call argument remains subject to candidate-local fitting under [length-argument inference](#44-function-length-parameters) and is not first defaulted to Array. An empty literal requires an expected element Type. Numeric element defaults follow ordinary inference.

An independent literal may form an Array with safe-borrow elements. Complete element Types and their Origins are inferred and preserved under the ordinary local rules; permitted Origin shortening applies without merging distinct Loans. A fixed-array expectation still selects a fixed array, for example `let views: [2 of ref/i32] = [x@ref, y@ref]` for initialized integer locals `x` and `y`.

An annotation-only declaration remains Uninitialized: there is no zero fill or default element construction. Initial construction requires a whole-array initializer or one whole-array assignment; element-by-element writes into an unconstructed array are forbidden. After construction has completed and a Partial Move has occurred, missing elements may be reinitialized through eligible static Move Paths with ordinary write permissions. Whole-value reads and borrows require completeness.

**Construction boundary.** Fixed arrays have no fill/repetition, generator or default construction. Construct a literal of exactly `N` elements, or acquire a complete array from an input or result. A declaration `[N of u8]` alone yields no usable buffer, even for a known large `N`. Generic functions may still inspect and mutate supplied initialized arrays. Future fill or generator rules must define evaluation counts, Copy versus repeated construction, zero length and partial cleanup.

```kimi
let a: [4 of i32] = [1, 2, 3, 4]
let inferred: [4 of _] = [1, 2, 3, 4] // [4 of i32]
let dynamic = [1, 2, 3, 4]            // Array<i32>
let empty: [0 of i32] = []
let wrong: [3 of i32] = [1, 2]        // Error: element count.
let unknown: [0 of _] = []            // Error: unknown element Type.
let pending: [2 of i32]
pending = [1, 2] // Whole initial construction; pending[0] = 1 cannot construct it.

let matrix: [3 of [4 of f32]] = [
    [1.0, 2.0, 3.0, 4.0],
    [5.0, 6.0, 7.0, 8.0],
    [9.0, 10.0, 11.0, 12.0],
]
let cell = matrix[1][2] // f32 value 7.0; each dimension is checked separately.
```

## 4.4. Function length parameters

A length slot is declared as `<length N>`. A plain `<N>` remains a Type slot; neither signature use nor the body infers a slot's kind. `length` is contextual only at the start of a generic parameter declaration followed by its Name; it does not affect ordinary names or `.length`.

The kinds, order and names of the declaration list are bound first, rejecting duplicates; then the signature and body are bound. A length slot is a nonnegative `isize` constant in the Value namespace, not a complete Type or a Semantics/Type pair. Kind misuse is diagnosed at the use and identifies the declaration. Only function length slots exist: there are no value parameters on Type declarations, general Const generics or source length-constraint syntax.

```kimi
func process<length N>(values: [N of i32])
    let count: isize = N
    ()

func keep<length N, T>(values: [N of T]) -> [N of T] => values
func work<length N>()
    let buffer: [N of u8] // Formation-only example: Uninitialized, not a usable buffer.
    // No fill/default construction exists; obtain a whole initialized value to use it.

let a: [4 of i32] = [1, 2, 3, 4]
process(a)          // N = 4
process([1, 2, 3])  // N = 3
process<4>(a)      // No length keyword in the argument list.
process<3>(a)      // Error: length mismatch.
let result = keep<4, i32>(a)
work<4>()          // A body-only length must still be supplied.
```

**Inference.** Within each overload candidate, explicit arguments are bound first; then the length and element Type are inferred from known fixed-array Types and from literals fitted to that candidate. Evidence from all arguments must agree, and an already established Array value is never retyped. An explicit list supplies all slots in order: a length constant expression (§4.2), without the `length` keyword, for each length slot, and a complete Type for each Type slot.

Length equations are not solved backward: `[(N * 2) of T]` is checked after `N` is known. Expected Types may fill unresolved parts without changing Types or lengths established by inputs. Evidence may come from a binding annotation, a typed assignment destination, a declared result or a known parameter. Nested calls and anonymous functions obey the [inference boundaries](10-overload-resolution-and-inference.md#105-inference-boundaries-and-specialization); explicit `@` and result constructs follow [adaptation inference](13-operators-and-assignment.md#1352-static-selection-and-inference) and [result validation](14-control-flow.md#149-result-validation). A callee's length arguments are never inferred from its implementation body, from later uses or member accesses, from guessed unresolved sibling expressions, or from candidate order.

```kimi
func consume<T>(x: Array<T>) => ()
func consume<length N, T>(x: [N of T]) => ()
consume([1, 2, 3]) // Error: both fit and neither candidate is better.
```

Fixed and dynamic array literals receive no preference beyond ordinary candidate comparison, so this ambiguity is intentional when the candidates otherwise tie; declaring both overloads is still allowed. Resolve it with a typed intermediate (`let fixed: [3 of i32] = [1, 2, 3]` or `let dynamic: Array<i32> = [1, 2, 3]`), distinct function names, or an explicit generic argument list whose kinds select a unique candidate. Array's default for independent expressions is not an overload-ranking rule.

**Formation obligations.** Each signature length expression `E` generates `ValidLength(E)`: the indivisible compiler obligation that the typed `E` evaluates with checked integer operations to a nonnegative `isize`. Logical expansion or minimization is not required. A nondependent failure rejects the declaration; a dependent obligation is a public applicability condition. For example, `N - 1` needs `N >= 1` and `N / M` needs `M != 0`, without requiring every intermediate value to be nonnegative.

At a concrete call, these obligations are checked after inference and before candidate comparison; a false obligation eliminates its candidate. Their strength does not rank candidates or solve lengths backward. A representation failure discovered after selection, such as an excessive concrete layout, cannot reopen overload selection.

The body is verified for every binding that satisfies the public conditions. Body lengths and callee conditions must follow from the declaration or produce a definition error; semantic checking is not deferred to convenient instantiations, and no extra applicability conditions are inferred from the body. Length proof is limited to concrete evaluation, the inherent nonnegative-`isize` property of length parameters, and reuse of identical normalized formation obligations. Symbolic physical layout remains an ordinary instantiation-time representation obligation.

**Normalization.** For signatures, dependent fixed-array Types and formation obligations, typed length expressions are normalized bottom-up:

1. Remove grouping, evaluate nondependent subexpressions with checked arithmetic, and replace parameter Names with their declaration slot positions.
2. At each binary integer `+` or `*`, sort the two normalized operands by a deterministic structural order using node kind, Type identity, constant value, and bound Symbol/slot identity.
3. Preserve all Types and operators, and the operand order of `-`, `/` and `%`. Do not flatten, reassociate, distribute or cancel.

With matching slots and integer Types, `N + 4`, `N + (2 + 2)`, `((N + 4))` and `4 + N` coincide, as do `N * M` and `M * N`. `(N + 2) + 2` need not equal `N + 4`, because intermediate overflow matters. This normalizes pure length expressions only and does not reorder runtime evaluation. Concrete Type and full-specialization keys use evaluated lengths. Each length slot counts once in GenericArity.

```kimi
func reordered<length N>(value: [(N + 4) of u8]) -> [(4 + N) of u8] => value
// Valid: both the dependent Type and ValidLength obligation normalize identically.
```

## 4.5. Operations and ownership

Fixed arrays and Array expose public read-only `length: isize` and `indices: ResolvedRange`, under the [metadata acquisition rules](#461-access-and-length-metadata). Fixed arrays have no resizing operation. Element writes obey ordinary `var`, `let` and borrowed-access permissions.

A fixed array is Copy exactly when its complete element Type is Copy. Owned is derived, and element Origins and Loans are retained, recursively. Partial Move follows [Move Paths](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move). [Aggregate cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup) destroys the remaining initialized elements in decreasing index order, including abandoned construction on an ordinary control transfer; Uninitialized and Moved parts are skipped, and partly built elements are cleaned recursively. Abort does not guarantee cleanup.

Array is Non-Copy and accepts any valid complete element Type with a representable element layout; neither Owned nor Copy is required. Its Type preserves `T`'s Origin dependencies and Loan requirements, and its values keep the acquired elements' Loans under [ordinary storage](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision), including after removal, replacement and clear (§4.7.5). To obtain a shared view of either owning array form, slice it explicitly.

The element position preserves Origin variance, and nested variance composes normally, while `uniq/Array<T>` remains invariant in its complete Referent Type. No covariance between different element Cores is added. The Kimi dynamic mutation operations (§4.7) require exclusive access to the whole Array, independently of reallocation. Ordinary indexing gains no Non-Copy Move operation.

**Mutation boundary.** Slice has no mutable or exclusive-element form in this revision. Initialized array elements are mutated through authorized access to the owning array or a `uniq` borrow of the whole array; `uniq/Slice<T>` changes only the handle, never element permissions. To process a mutable subrange, pass an exclusive borrow of the whole array plus bounds and index the array, or iterate saved indices while accessing each element. A non-lending iterator does not prohibit such indexed mutation. Bounds validation, active Loans and ordinary initialization checks still apply, and no disjoint mutable subviews are implied.

```kimi
var values: [4 of i32] = [10, 20, 30, 40]
values[1] = 25
let count = values.length
let middle: Slice<i32> = values[1..3] // Infer the borrow of values' storage.
```

## 4.6. Indexing and slicing

### 4.6.1. Access and length metadata

The built-in indexing operations apply to `[N of T]`, `Array<T>` and `Slice<T>`. Element indexing accepts `isize` or `Index`; range indexing accepts `Range` or `ResolvedRange`. Dictionary indexing takes keys instead. [Raw pointers](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing) keep signed `isize` offsets without safe sequence bounds checks and accept neither `Index` nor `Range`. String indexing units and user-defined indexers are not introduced.

| Core | Meaning |
| --- | --- |
| `Index` | Copy, Owned position measured from the start or the end; retains no target |
| `Range` | Copy, Owned unresolved boundaries and end-inclusion flag; not Iterable |
| `ResolvedRange` | Copy, Owned validated absolute half-open interval; finite `isize` iteration |
| `Slice<T>{source}` | Copy shared view, independent of `T`'s Copy capability; retains the backing Origin and shared Loan |

These names are not keywords; `::Kimi.Index`, for example, disambiguates a hidden alias. Prefix `^` and range syntax always construct the designated Types from the Kimi Kotonoha, never same-named user Types.

**Length metadata.** Fixed arrays and Array provide `length` and `indices`; Slice additionally provides `isEmpty`. The receiver is evaluated once and requires ordinary initialization, completeness and access legality. A known fixed length does not remove receiver effects or checks.

| Receiver | Acquisition |
| --- | --- |
| Fixed array | Check shared access and use the Type-level `N` without reading elements |
| Array | Share access during the operation and read its current length |
| Slice | Copy the handle and read its stored length, without reading backing elements |

The returned integers, Booleans and `ResolvedRange` values acquire no receiver or source Origin or Loan; this does not release existing Loans. `indices` is a snapshot of `[0, L)` at acquisition. Resizing an Array does not update a saved snapshot; later accesses check the length current at that time.

**Element Places.** Binding the receiver and index Types, resolving bounds and checking access produce an element Place carrying the complete Type `T`, its source location and its permissions. Forming the Place performs no element Copy or Move. Chained access such as `matrix[1][2] = 10` keeps Places without copying an intermediate inner array.

| Use | Operation after locating the Place |
| --- | --- |
| `values[i] = value` | Ordinary initialization or replacement, with write permissions and Loan checks |
| `values[i]@ref` / `@uniq` | Shared or exclusive borrow of that Place; exclusive access requires write permission |
| Non-Copy element acquisition | Move only through an eligible static Move Path of an owned fixed array |
| Ordinary value read | Copy or Move on an eligible owned fixed-array static Move Path; otherwise a shared read |

Static paths use only the [integer-literal recognition rule](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move); Array elements, runtime indices, `Index` values and paths through borrows never become movable. Ordinary reads Copy when allowed, and eligible Non-Copy static elements Move. All other reads follow the [shared result rules](#466-slice-operations-and-element-results), including Object Semantics and Reborrow, rather than always producing `ref/T`. `@ref` borrows the element Place regardless of the index form. Slice Places are shared-only.

```kimi
// resources is an owned fixed array of Non-Copy value Type Resource.
let i: isize = 0
let view = resources[i]   // ref/Resource; the element remains.
// End all required uses of view before the following Move.
let taken = resources[0] // Ordinary Move; resources[0] becomes Moved.

var numbers: [2 of i32] = [10, 20]
let copied = numbers[0]     // Copy; the element remains Initialized.
numbers[0] = 30             // Replace the initialized element.
let alsoCopied = numbers[i] // Copy does not require a static Move Path.
```

### 4.6.2. Index

`Index` exposes public read-only `offset: isize` and `isFromEnd: bool`. Its constructor is `init(offset: isize, fromEnd: bool = false)`.

| Construction | Meaning |
| --- | --- |
| `Index.init(n)` | Offset `n` from the start; the first element is zero |
| `Index.init(n, fromEnd: true)` / `^n` | Offset `n` backward from the end boundary |

Prefix `^` produces a storable, passable `Index` outside indexing expressions too; infix `^` remains integer exclusive-or. Write `^(n + 1)` for a compound from-end distance. Integer indices, range boundaries and `^` operands have expected Type `isize`; values already typed `i32` or `usize` require the normal explicit conversion. An `isize` in an index or boundary position denotes a start-relative offset; this adds no general implicit conversion between `isize` and `Index`.

Construction with a negative offset initiates Abort without consulting a target length. At length `L`, start-relative `n` resolves to `n` and end-relative `n` to `L - n`. Thus `^1` selects the last element and `^0` denotes the one-past-end boundary.

`Index` implements Equatable by direction and offset, not by coincidental resolution to the same position. It provides neither Comparable nor arithmetic.

```kimi
let first: Index = Index.init(0)
let last: Index = ^1
let values: [4 of i32] = [10, 20, 30, 40]
let a = values[first] // 10
let b = values[last]  // 40
let end = ^0         // Valid Index; values[end] is out of bounds.
```

### 4.6.3. Range and ResolvedRange

**Range** is a nongeneric, unresolved range specification constructed only by range syntax; there is no `Range.init`. Boundaries accept `isize` or `Index`; integers normalize to start-relative `Index` values. A negative integer boundary initiates Abort at construction.

| Syntax | Interval |
| --- | --- |
| `start..end` | Includes start, excludes end |
| `start..=end` | Includes both boundaries |
| `start..` | From start through the target's end |
| `..end` / `..=end` | From the start, excluding / including end |
| `..` | The entire target |

`Range` exposes public read-only `start: Index`, `end: Index` and `isInclusive: bool`. An omitted start normalizes to start-relative zero and an omitted end to `^0`; an inclusive end cannot be omitted. Construction neither borrows an array nor checks boundary order. A saved Range can apply to different targets, so it has no target-independent length or `isEmpty`.

`Range` implements Equatable by normalized start, end and `isInclusive`. Thus `..` equals `0..^0`, but `1..3` differs from `1..=2`. To compare resolved intervals, compare their `ResolvedRange` values.

**ResolvedRange** always satisfies `0 <= start <= end <= maximum isize`. It exposes public read-only `start: isize`, `end: isize`, `length: isize = end - start` and `isEmpty: bool`. It is obtained from `Range.resolve`/`tryResolve`, from sequence `indices`, or from the constructor declared as `init(! start: isize, end: isize)`; invalid constructor bounds initiate Abort. No setter or implicit construction bypasses validation.

`ResolvedRange` implements Equatable by start and end. It retains no storage Origin or Loan, and applying it to an array or Slice rechecks the target bounds. Neither range Type implements Comparable, and there is no implicit conversion or cross-Type equality between them.

**Iteration.** `ResolvedRange` implements `Iterable` with associated Type `Element = isize`: it yields `start` through `end - 1` in unit steps, nothing for an empty interval, and stays exhausted after `None`. It never computes beyond `end`, including at maximum `isize`. `Range` is never Iterable, even with absolute boundaries, because conformance cannot depend on spelling or value; use `values.indices` or explicitly construct or resolve intervals. Infinite, descending, stepped and negative ranges and dedicated `ResolvedRange` syntax are unavailable.

```kimi
let inner: Range = 1..^1
let values: [4 of i32] = [10, 20, 30, 40]
let middle = values[inner]
for i in values.indices
    let value = values[i]
let resolved = inner.resolve(values.length)
for i in resolved
    let value = values[i] // Indices 1 and 2.
for i in 0..values.length // Error: Range is not Iterable.
    ()
```

**Precedence.** Range operators are non-associative and bind below logical and arithmetic operators but above assignment. Prefix `^` has ordinary prefix precedence. `a..b..c` is rejected syntactically, and `(a..b)..c` by its boundary Type.

| Expression | Interpretation |
| --- | --- |
| `a + 1..b * 2` | `(a + 1)..(b * 2)` |
| `^n + 1` | `(^n) + 1`; a Type error because `Index` has no addition |
| `^(n + 1)` | From-end distance `n + 1` |
| `0..^1` | `0..(^1)` |
| `a ^ b..c` | `(a ^ b)..c`; integer exclusive-or first |

### 4.6.4. Bounds, evaluation, and failure

The target length `L` is a nonnegative `isize`. Boundaries are resolved to absolute positions and checked as follows, without clamping and without turning reversed intervals into empty ones:

| Operation | Valid condition | Result |
| --- | --- | --- |
| Index boundary resolution | `0 <= n <= L` | `n` from the start; `L - n` from the end |
| Element access | `0 <= p < L` for resolved `p` | Element `p` |
| Half-open Range | `0 <= start <= end <= L` | `[start, end)` |
| Inclusive Range | `0 <= start <= end < L` | Normalized to `[start, end + 1)` |
| ResolvedRange application | `0 <= start <= end <= L` | `[start, end)` |

`n <= L` is checked before the from-end subtraction and `end < L` before adding one to an inclusive end, so valid resolution cannot overflow.

| Operation | Result Type | Invalid length or bounds |
| --- | --- | --- |
| `index.resolve(length)` | `isize` boundary | Abort |
| `index.tryResolve(length)` | `Option<isize>` | `None` |
| `range.resolve(length)` | `ResolvedRange` | Abort |
| `range.tryResolve(length)` | `Option<ResolvedRange>` | `None` |

Here `index` is an `Index`, `range` a `Range` and `length` an `isize`. These operations take small Copy values and access no target storage. Successful `Index` resolution validates a boundary, not an element: `(^0).resolve(L)` returns `L`. A valid `ResolvedRange` may still fail on a shorter target.

```kimi
let r = ResolvedRange.init(start: 2, end: 5)
let shortArray: [3 of i32] = [1, 2, 3]
let checked = shortArray[..].trySlice(r) // None.
let failed = shortArray[r]              // Abort if executed.
```

`values[..]` and `values[L..]` also apply to empty arrays. `values[L..L]` and `values[^0..]` are empty; element index `L` or `^0`, and an inclusive end of `^0`, are invalid.

Ordinary element and range indexing initiates Abort on invalid bounds; use `Slice.tryGet`/`trySlice` for expected input failures. A try operation converts only its own length or bounds failure to `None`, not failures in argument evaluation or other operations: `slice.tryGet(^(-1))` aborts during `Index` construction, while `slice.tryGet(-1)` returns `None`. A successful try operation returns `Some`.

**Evaluation order.** The receiver and index are each evaluated once under the [evaluation order](12-expressions.md#122-evaluation-order). A range expression evaluates its start, then its end, and then checks integer boundaries for nonnegativity in the same order. A failure inside a boundary expression, including `^` construction, stops subsequent evaluation immediately. These are independent failure examples:

```kimi
func sideEffect() -> isize
    return 2 // Represents an observable effect.

let a = (-1)..sideEffect()  // Evaluate sideEffect, then Abort constructing Range.
let b = ^(-1)..sideEffect() // Abort constructing Index; do not call sideEffect.
```

The built-in access receiver is located first. While its index is evaluated, modification, destruction, Move and reallocation of that storage are prohibited; shared reads remain allowed, as in `values[values.length - 1]`. For a chained Copy read, the located root is protected through all index evaluations and the final element Copy, and that Copy is acquired before later surrounding operands are evaluated; intermediate arrays are not copied. A temporary receiver keeps its ordinary enclosing-expression lifetime. A write establishes its exclusive Loan after resolving bounds and checks existing Loans. The receiver and boundaries are never reevaluated. For a Slice, the handle is copied first; reassigning the original handle does not change the acquired view.

**Writes and updates.** [Simple assignment](13-operators-and-assignment.md#1371-simple-assignment) secures its right-hand side before locating the indexed target. [Compound assignment](13-operators-and-assignment.md#1372-compound-assignment) evaluates the receiver and index, checks bounds, reads the old value, evaluates the right-hand side, computes and writes back, once each. Increment and decrement use the same target and Loan rules. An arithmetic failure prevents writeback, and the established exclusive Loan forbids conflicting access from the right-hand side. The exclusive Loan of an element write lasts from bounds resolution through old-value destruction and placement. [Exchange operations](15-ownership-and-lifetime-analysis.md#157-whole-value-updates) evaluate their arguments left to right and reserve each target under §15.6.7 before activating all targets at entry; `Kimi.Intrinsics.swap` requires static non-overlap, not merely a runtime `i != j`.

The common [Abort and constant-evaluation rules](17-failure-handling.md#1734-checks-builds-and-constant-evaluation) apply. Syntax, Type, literal-fitting and required constant-evaluation violations are compile-time errors. An ordinary out-of-bounds `a[10]` on a three-element array instead aborts if executed; a compiler may warn, but optimization must not turn such a runtime failure into language-level rejection. Rejection of an ineligible static Move Path is a separate rule.

### 4.6.5. Slice storage, lifetime, and permissions

`Slice<T>{source}` shares an initialized contiguous region of complete element Type `T`. Formation requires the ordinary initialization and access checks and cannot hide Uninitialized or partially Moved storage. Its runtime length is not part of Type identity. Every range access returns a Slice, including constant-length ranges, without copying elements into a fixed array.

Slice indices start at zero; from-end positions and reslicing use the current Slice length, and the original array indices are not retained. Slicing the rows of a nested fixed array produces `Slice<[N of T]>` without flattening. Element inheritance or matching layout does not permit conversion between Slices of different element Types.

**Origins and Loans.** The Origin records the backing lifetime; the Loan records the conflicting Places and permissions. Handle copies, reslices, iterators and references to element slots retain both. They need not borrow the handle variable itself: acquired views remain usable after that variable ends, as long as the backing storage and inherited Loan remain valid.

Under [static Place analysis](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), an array-derived Slice borrows the whole array Place. Reslicing, `splitAt` and empty Slices keep that Loan's conflict footprint; runtime interval resolution neither narrows it nor proves disjointness. Thus a later use of `empty` from `let empty = values[1..1]` conflicts with `values[3] = 10` because of the retained shared Loan, not the Origin alone. The Loan ends after all required derived uses end. Moving, destroying or reallocating the source is forbidden while it would invalidate an active view.

A Slice owns no elements; copying or destroying the handle neither copies nor destroys them. `T` need not be Owned, and all dependencies inside `T` remain intact. `source` may be shortened but never lengthened; the Slice's own Owned classification follows [OwnedOrigins](15-ownership-and-lifetime-analysis.md#1523-static-and-owned), including `source` and `T`.

**Storage and escape.** A Slice may be kept in local aggregates, Array elements and concrete object payloads under [ordinary storage](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision), preserving its backing Loan and nested element dependencies. Static storage requires Owned and a valid static source under the [static storage rules](11-properties.md#1132-static-storage). Copying or storing a Slice never extends the backing lifetime.

Borrowing a temporary never extends its [lifetime](03-types-and-values.md#36-temporary-values-places-and-lifetimes). An unused binding is not rejected solely because it contains a temporary borrow; the check is whether a later use, return or retention requires the expired dependency.

```kimi
func makeArray() -> Array<i32> => [1, 2, 3]
func inspect<T>(values: Slice<T>) => ()

inspect(makeArray()[..]) // Temporary array survives through the call.
let escaped = makeArray()[..]
inspect(escaped)         // Error: temporary expired at the initializer boundary.
let owned = makeArray()
let lasting = owned[..]
inspect(lasting)         // Borrows an owning local.
```

A `var` Slice permits only handle reassignment, and `uniq/Slice<T>` exclusively borrows the handle, not its elements. Mutable Slices, implicit conversion to owning arrays, safe raw-pointer construction, Slice equality and implicit elementwise comparison are not introduced.

### 4.6.6. Slice operations and element results

For `s: Slice<T>`, members receive and Copy the handle by value. Element and partial-Slice results retain `s.source` rather than borrowing the handle variable used in the call. All listed operations are public. In the table, each resulting Slice keeps that same source binding.

| Operation | Result and conditions |
| --- | --- |
| `s.length: isize` / `s.isEmpty: bool` | Read-only count / whether the count is zero |
| `s.indices: ResolvedRange` | Read-only snapshot under the metadata rules |
| `s[index]` | Shared element access; accepts `isize` or `Index` |
| `s[range]` | `Slice<T>` retaining `s.source`; accepts `Range` or `ResolvedRange`, checked against the current length |
| `s.tryGet(index)` | `Option<ref{s.source}/T>`; separate `isize` and `Index` overloads |
| `s.trySlice(range)` | `Option<Slice<T>>` retaining `s.source`; separate `Range` and `ResolvedRange` overloads |
| `s.splitAt(index)` | `(Slice<T>, Slice<T>)`, both retaining `s.source`, covering `[0, p)` and `[p, length)` |
| `s.trySplitAt(index)` | `Option` of that Tuple |

Both split operations have `isize` and `Index` overloads and accept the boundaries zero and length. Invalid boundaries abort for `splitAt` and return `None` for `trySplitAt`; `tryGet` and `trySlice` likewise return `None` on their own invalid bounds. `tryGet` deliberately has a fixed reference result, independent of `T`'s Copy capability.

A shared element read uses the following **SharedReadResult(T, source)** rules. These are element-access operations, not Field getters; they never move the element or implicitly duplicate object ownership.

| Complete element Type | Result | Operation |
| --- | --- | --- |
| Copy `owner/T` | `T` | Copy |
| Non-Copy `owner/T` | `ref/T` | Shared storage Borrow |
| `obj/T`, `rc/T`, `arc/T` | `objref/T` | Shared object Borrow; no reference-count change |
| `ref/T`, `objref/T`, `unsafe/T` | Same Type | Copy; pointer dereference remains unsafe |
| `uniq/T` | `ref/T` | Shared Reborrow |
| `objuniq/T` | `objref/T` | Shared object Reborrow |

New borrows and reborrows are bounded by the source storage and its existing dependencies. A Copy preserves the stored reference's Origins; a Reborrow suspends conflicting parent access while it is live. Other sequence operations that refer to this table keep their own path and write permissions.

For `s[index]`, `@ref` instead borrows the element Place itself. Element writes, Moves and exclusive borrows through a Slice are forbidden.

For `s: Slice<Result<i32, i32>>`, `s[0]` is an owned Copy `Result`, while `s[0]@ref` borrows its slot and `s.tryGet(0)` returns `Option<ref/Result<i32, i32>>`. Adding conditional Copy to an element Type can therefore change inferred read Types, argument fitting and Origin obligations. Use explicit slot borrowing when an API needs a reference regardless of Copy.

For an unknown `T`, the correlated result Type, acquisition effect and Origins are kept as the internal family `SharedReadResult(T, source)`, which is not a source-spellable Type. The body is verified for all admitted cases; unknown is neither assumed to mean Non-Copy nor deferred until a favorable instantiation. An operation or result that does not fit every case requires a constraint or an explicit borrow.

```kimi
func first<T>(s: Slice<T>) -> T
    T is Copy
    return s[0]

func firstRef<T>(s: Slice<T>) -> ref{s.source}/T
    return s[0]@ref // Borrow the slot regardless of T's Copy capability.

func head<T, E>(s: Slice<Result<T, E>>) -> ref{s.source}/Result<T, E>
    return s[0]@ref // Plain s[0] fails definition checking: Copy bindings return a value.

let values: [4 of i32] = [10, 20, 30, 40]
let s = values[1..]     // Length 3; s[0] is 20.
let last = s[^1]        // 40
let firstTwo = s[..2]   // 20, 30; retains the backing dependency.
let parts = s.splitAt(1)
let left = parts.0
let right = parts.1
match s.tryGet(10)
    .Some(let value) => ()
    .None => ()

func tryTail<T>(values: Slice<T>) -> Option<Slice<T>{tail}>
    origin tail.source == values.source
    return values.trySlice(1..) // None for an empty Slice; no static length condition.
```

```kimi
var values: [3 of i32] = [1, 2, 3]
let shared = values[..]
values[0] = 10 // Error: conflicts with a subsequent use of the shared Loan.
let first = shared[0]
shared[0] = 20 // Error: Slice elements are read-only.
```

### 4.6.7. Slice iteration and nested Origins

`Slice` implements Iterable with `Element = ref{source}/T`, including for Copy elements, yielding shared references in index order. The iterator keeps a handle and a position, not owned elements. Yielded references borrow the backing slots, not the iterator's receiver or storage, which satisfies the [non-lending protocol](14-control-flow.md#1462-iteration-protocol-and-acquisition). The iterator stays exhausted after `None`.

Element-internal Origins are kept separate from slot-borrow Origins:

```kimi
let data: i32 = 10
let refs: [1 of ref/i32] = [data@ref]
let s: Slice<ref/i32> = refs[..] // Infer inner and backing Origins separately.
let value = s[0]       // Copy the inner reference to data.
let slot = s.tryGet(0) // Option containing a shared borrow of refs[0].
for element in s
    () // element shares a slot containing the inner reference.
```

The outer references from `tryGet` and iteration borrow the slots of `refs`, while the inner reference depends on `data`. The inner Origin must stay valid while the outer reference is used. A copied inner reference keeps its own Origin without gaining a new slot borrow.

### 4.6.8. Representation and performance

Construction, resolution and Copy of `Index`, `Range` and `ResolvedRange`, and creation, Copy, reslicing, splitting and address calculation of Slices, take O(1) time in the element count and require no additional element storage, heap allocation or reference-count update. Element Copy and user-code costs are separate. Iteration takes O(n) time and O(1) extra storage; an array of iteration values is never materialized first.

A Slice's semantic representation keeps the backing-element location or equivalent provenance, a nonnegative `isize` length, and static Origins and Loans. Element spacing is `stride(T)`. Empty Slices and zero-sized elements keep source provenance. No universal pointer-plus-length ABI, runtime lifetime tag or pointer to a disappearing handle variable is required. Implementations use logical counts and positions rather than subtracting element pointers to recover a length, and never form invalid pointers before checking.

Checks may be eliminated, shared or hoisted out of loops only when safety is proven without changing effects, Abort behavior or borrow legality. Constant-folding `Index` and `Range` operations does not extend the literal-only static Move Path rule.

## 4.7. Dynamic collection mutation

### 4.7.1. Common acquisition and outcomes

`Array<T>` and `Dictionary<K, V>` are Non-Copy owning collections. They accept valid complete stored Types without blanket Copy or Owned constraints. Dictionary requires `K is Equatable` and the key stability of §12.3.4, not a public Hash constraint. Types and Origins are fixed at the declaration; mutation never restarts inference.

The operations below are public instance APIs. Mutations use `self: uniq/Self` unless stated otherwise. Value parameters are acquired once by ordinary Copy or Move, without deep cloning or implicit count increments. A rejected Move is not restored; returned inputs can be recovered from a `Result`. Removal transfers the stored responsibility, even for Copy elements. Owning a reference value neither owns nor extends its referent's lifetime.

Precondition failures Abort. Ordinary absence uses `Option`, and recoverable rejection that returns its inputs uses `Result`. A `try` name promises only its specified recoverable outcome; a corresponding Abort API need not exist. A discarded `Result` follows the normal warning rule.

| Normal outcome | Postcondition |
| --- | --- |
| Add | Length increases; prior relative order is preserved; capacity grows if needed |
| Replace | Only the value changes; the stored key, order, length and capacity are preserved |
| Remove | Length decreases; remaining order and capacity are preserved |
| Clear | All elements are destroyed; length becomes zero; capacity is preserved |
| Reserve / shrink | Values, order and length are preserved; capacity and placement change only as specified |
| Lookup, absence or duplicate rejection | Values, order, length and capacity are preserved |

These postconditions do not roll back external effects of arguments, equality or destructors.

### 4.7.2. Array operations

| Operation | Behavior |
| --- | --- |
| `append(value: T) -> ()` | Add at the end |
| `insert(index: isize, value: T) -> ()` | Insert before the resolved position |
| `insert(index: Index, value: T) -> ()` | Same, with a directional `Index` |
| `pop() -> Option<T>` | Remove and return the last element, or `None` when empty |
| `remove(index: isize) -> T` | Remove and return the selected element |
| `remove(index: Index) -> T` | Same, with a directional `Index` |
| `clear() -> ()` | Destroy all elements in the order of §4.7.6 |

The index is resolved once in the body against the entry length `L`. For a from-end `Index`, `offset <= L` is required before `p = L - offset`. Insert requires `0 <= p <= L` and remove requires `0 <= p < L`; invalid indices Abort. There is no implicit `isize`/`Index` conversion and no Range overload.

`insert(^0, value)` appends, including to an empty Array. On a nonempty Array, `insert(^1, value)` inserts before the last element and `remove(^1)` removes it; `remove(^0)` is always invalid. A failure constructing an `Index` prevents later argument evaluation. Indexed reading still cannot Move a Non-Copy dynamic element, and indexed assignment destroys the old value, unlike `remove`.

```kimi
var values: Array<i32> = []
values.reserve(additional: 3)
values.append(10)
values.insert(^0, 30)
values.insert(^1, 20) // [10, 20, 30]
let last = values.remove(^1) // 30; capacity is unchanged.
let first = values[0]
values.append(first)
values.append(values[0]) // The element Copy finishes before receiver activation.
```

### 4.7.3. Dictionary operations and indexed replacement

| Operation | Absent key | Equal stored key |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K, V)>` | Append; `Ok(())` | Unchanged; `Err((input key, input value))` |
| `insertOrReplace(key: K, value: V) -> Option<V>` | Append; `None` | Keep the stored key and position; `Some(old value)` |
| `remove(key: ref/K) -> Option<(K, V)>` | `None` | Remove and return the stored key and value |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref{self}/V>` | `None` | Shared reference to the stored value |
| `clear() -> ()` | Destroy all entries in the order of §4.7.6 | Same |

A duplicate is the only `Err` outcome of `tryInsert`; no dedicated error Type is introduced. Both value arguments are acquired before lookup, unlike Dictionary literals (§12.3.4). `insertOrReplace` secures the old result, stores the new value and then destroys the unused input key; delivery follows that cleanup. Removing a key and later adding an equal key appends a new position. No API mutates a stored key.

`tryGet` always returns `ref/V`, including for a Copy `V`. It never copies, moves or removes a stored value. Its result protects the whole Dictionary storage and preserves `V`'s dependencies, with no operation-only Loan on the search key. A lookup, or a saved Boolean observation, reserves no future access.

`map[key] = value` replaces an existing value and returns Unit; absence Aborts. It shared-borrows the key as `ref/K` rather than consuming it:

1. Evaluate and acquire the right-hand `V` once.
2. Identify the receiver and evaluate the key, left to right and once each.
3. Search with shared access, and Abort if the key is absent.
4. Obtain exclusive access to the value slot, destroy the old value and place the new one.

From receiver identification through placement, structural mutation, Move and destruction of the Dictionary are prevented. Shared access is kept during lookup, and all Loans, including the key's, are checked before writing; that borrow is not treated as ended early. Temporary lifetimes follow §3.6 and §10.2. Replacement is rejected if destroying the old value would invalidate dependencies of the secured right-hand side or of retained temporaries. Abort or nontermination during that destruction prevents placement. Compound assignment follows the target-first order of §13.7, with one search, read, right-hand evaluation and write.

```kimi
var names: Dictionary<i32, string> = [:]
match names.tryInsert(1, "first")
    .Ok(()) => ()
    .Err(let entry) => Kimi.Console.writeLine(entry.1)
names[1] = "replacement" // Existing-key replacement, not insertion.
let removed = names.remove(1) // A temporary key is borrowed under §10.2.
```

### 4.7.4. Capacity and allocation

Both collections expose read-only `capacity: isize` through shared access: the maximum number of elements or entries supported without internal allocation, not bytes or buckets. The result is a snapshot without a Loan. `0 <= length <= capacity <= isize.MaxValue` always holds. Typed empty `[]` and `[:]` have length and capacity zero and allocate nothing internally; a fixed handle-management cost is permitted.

| Operation | Contract |
| --- | --- |
| `reserve(additional: isize) -> ()` | Requires a nonnegative `additional`; computes the checked `R = body-entry length + additional`, aborting on failure. Ensures `capacity >= R` without shrinking. If `R <= capacity`, internal placement is preserved too; zero is always a no-op. |
| `shrinkToFit() -> ()` | Attempts `length <= new capacity <= old capacity`. Neither an exact fit nor returning memory to the OS is guaranteed. |

`reserve` accepts a positional argument, but examples and diagnostics should explain its **additional** meaning. There is no `reserveCapacity` API taking a total.

No internal allocation is permitted for addition within capacity, `reserve` with sufficient capacity, empty construction, lookup, existing-value replacement, `pop`/`remove`/`clear`, or absence and duplicate rejection. This covers same-capacity reallocation, scratch storage and Dictionary auxiliary storage. Churn from deletion and re-addition must reuse existing storage. Arguments, equality and destructors keep their own effects.

Addition beyond capacity, and `reserve` that needs growth, may Abort on a required allocation failure. Growth follows §4.7.7. Optional overshoot or rounding is clamped to a representable capacity; rounding overflow alone cannot reject a representable request.

`shrinkToFit` may allocate. If the candidate allocation, or representing the candidate size, fails, it returns normally with values, order, length, capacity **and internal placement** unchanged: the new storage is prepared while the old storage is intact and transferred only after success. This does not catch arbitrary Abort and is outside the growth amortization guarantee.

### 4.7.5. Loans, retained dependencies and call effects

The whole-collection exclusive receiver is reserved before later arguments and activated before entry under [call borrow reservations](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations), even for runtime no-ops. Temporary shared inspection may finish during preparation. Element, Slice and empty-Slice Loans still live at activation conflict with mutation. Their duration follows later uses and observable destruction, not necessarily lexical scope. A removal requires exclusive access and cannot run through an overlapping receiver reservation.

These public dependency rules apply without inspecting private bodies:

| Operation | Conservative propagation |
| --- | --- |
| Add / replace | Union the input dependencies into the collection's prior set |
| `tryInsert` | Union both input sets regardless of success; `Err` also keeps the input dependencies |
| Remove / return of an old value | Return the pre-update potential dependencies of the stored full Types; replacement-input dependencies are not mixed into the old result |
| Remove / replace / clear | No subtraction of individual collection dependencies |
| Capacity operations | Dependencies preserved |
| `tryGet` | The result adds the outer storage Loan and `V`'s dependencies; the collection's set is unchanged |

Dependencies are not refined by literal index or rolled back for `None` or `Err`. Emptiness never changes Type, Origin or Owned classification. Owned return values keep no operation-only receiver or key Loan but keep their full-Type dependencies. Actual Loan identities, storage anchors and Reborrow relationships are preserved; equal Origin names do not merge Loans. Duplicating dependency information does not duplicate exclusive authority. In particular, a removed `uniq/T` is not assumed independent of the remaining `Array<uniq/T>` dependencies; conservative conflicts are rejected until normal Loan liveness permits the use.

The following user-code effects are published and composed with argument and default evaluation and later temporary and result cleanup:

| Operation | User-code effects inside the operation |
| --- | --- |
| Array `append`/`insert`/`pop`/`remove`; `reserve`/`shrinkToFit` of both collections | None |
| Dictionary `tryGet`/`tryInsert`/`remove` | `K` equality |
| Dictionary `insertOrReplace` | `K` equality and destruction of the unused input `K` |
| Dictionary indexed replacement | `K` equality and destruction of the old `V` |
| Array `clear`, destruction and indexed replacement | Destruction of the relevant `T` |
| Dictionary `clear` and destruction | Destruction of `V` and `K` |

Generic summaries expose parameter-dependent equality and recursive destruction. They are verified at definition checking and substituted through validated specialization mappings; private bodies are not rescanned and legality is not deferred. Effects are unioned across permitted branches and implementations. A proven empty summary invents no static access. Receiver authority does not excuse external static reentry; unknown, possibly conflicting mutable-static effects are rejected under §15.6.4.

### 4.7.6. Commit, failure and destruction order

After normal acquisition, the operation resolves bounds or searches, then decides absence, duplicate, replacement or addition. Rejection and absence leave the collection unchanged before any capacity work. Replacement and removal do not run addition limits: duplicate rejection and replacement work even at maximum length. Only addition checks the increased length, required byte sizes and allocation, with checked arithmetic.

Relocation calls no user Copy, Move or `deinit`. Equality sees a consistent structure; no partially moved state or conflicting reentry is exposed. These are operation invariants, not source concurrency guarantees.

A normal transfer during argument evaluation secures its transfer result and then cleans up previously acquired caller arguments and temporaries as required; the operation is not called. Abort supplies no result, rollback or later cleanup. Nontermination prevents later work and delivery.

Array `clear` and destruction use reverse current index order. Dictionary `clear` and destruction use reverse insertion order, destroying each value before its key. Owning iterators destroy remaining entries in the same order. Normal abandonment of partial construction cleans up completed acquisitions and placements in reverse order. Moved elements, spare capacity and uninitialized buckets are never read or destroyed.

### 4.7.7. Performance and extension boundary

For fixed Types, let `n` be the length, `c` the capacity and `d` the number of live elements or entries whose complete Types cannot prove cleanup-free destruction. Allocator internals, input generation and destruction bodies are charged separately from collection management. Count release is cleanup; inspecting a runtime enum Case is not a free proof of cleanup-free destruction.

| Operation | Required bound, excluding separately charged user-code cost |
| --- | --- |
| Array `append` / `pop` | Amortized O(1) / O(1) |
| Array stable `insert` / `remove` | O(1 + n) |
| Array `clear` | O(1 + d); O(1) for a cleanup-free element Type |
| Dictionary lookup | Search cost + O(1) management |
| Dictionary mutation | Search cost + amortized O(1) management |
| Dictionary `clear` | O(1 + c + d) is permitted |

For `m` operations without `shrinkToFit`, let `C` be the maximum of the initial capacity, observed lengths and `reserve` requests `R`. Total growth preparation and transfer management is O(m + C). Repeated `reserve(additional: 1)` followed by `append`, starting from empty, is O(n), not quadratic; `reserve` itself has no per-call O(1) promise.

Dictionary search cost includes key-content equality and internal hashing work and candidate or bucket probing for every operation; linear search is permitted. This introduces neither a public Hash constraint nor user-defined hash calls. Relocation, index rewriting and order maintenance remain management cost. Reindexing across growth, tombstones and allocation-free cleanup processes O(m + C) keys in total, not O(1) per key lifetime. Variable-length key work is charged at actual search cost; whether hashes are cached or recomputed is an implementation choice. Known-entry deletion, placement and order maintenance remain amortized O(1), including allocation-free full-capacity churn. O(c) auxiliary indices, free lists or linked slots and noncontiguous storage are permitted; avoid temporary arrays or per-element allocations used only to preserve order.

Fixed-capacity mutation, mutable Slices and new borrowing iterators, bulk, resize and unordered removal, `contains`, an Abort-only Dictionary insert, entry or factory APIs, recoverable allocators and a fixed collection ABI remain outside this contract. `tryGet` followed by indexed replacement may search twice; a future one-search update must define lookup and reference-write authority together.
