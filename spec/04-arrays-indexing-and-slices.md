# 4. Arrays, indexing, and slices

[Specification index](../SPEC.md)

Fixed arrays use `[N of T]`, where N is a compile-time length and T is a complete element Type, including Semantics and Origins. Dynamic `Array<T>` owns variable-length storage; `Slice<T> from source` is a shared borrowed view. Their [indexing and slicing](#46-indexing-and-slicing) rules are defined together.

## 4.1. Fixed-array identity and layout

Type identity includes the evaluated length and complete element Type. Thus `[3 of i32]` differs from `[4 of i32]`, while `[(2 + 2) of i32]` equals `[4 of i32]`. Arrays of different lengths, fixed arrays and Array, and owning arrays and Slice have no implicit conversions.

Elements are stored inline in index order, without implicit heap allocation for the array's own element storage. Nested arrays apply the rule recursively, with the innermost index varying contiguously. The enclosing value's storage location and allocations performed by individual elements follow their own rules.

Require a finite, valid element layout. Let d = stride(T); Slice uses the same element spacing. These are specification quantities, not source operators, and follow the [common layout rules](21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi).

| Quantity | Layout of `[N of T]` |
| --- | --- |
| Element i offset | i * d, for 0 <= i < N |
| Alignment | alignment(T), including when N = 0 |
| Array stride | N * d, the spacing between consecutive array values |
| Size | N * d, including all element-stride padding |
| Zero-sized elements | Use T's stride; if d = 0, distinct logical elements may share an address |

Check size, stride, and padding calculations against target layout limits and nonnegative isize. An unrepresentable concrete layout is a compile-time error. `[0 of T]` has size and stride zero and initializes/destroys no elements, but still checks T's Type, layout, and ownership. Validate inline embedding before multiplying by zero: a struct directly storing `[0 of Self]` has forbidden recursive value layout. Pointer and reference referents do not create inline edges.

## 4.2. Length constants

A length is a nonnegative compile-time integer representable by the target's isize. A generic length remains symbolic until instantiation. Write an integer literal, a possibly qualified constant name, a length parameter, or a parenthesized integer constant expression. Any compound expression requires parentheses around the entire length.

The initial evaluator admits integer literals, length parameters, **Constant-readable Bindings**, grouping, unary `+ -`, and binary `+ - * / %`. A Constant-readable Binding is an integer `let` local or static stored Property with accessible standard get whose declaration initializer can be recursively evaluated using only these forms, after normal lookup, access, and initialization checks. Parameters, `var`, instance Fields, custom/computed accessors, calls, and cyclic initializers are excluded. This is a semantic classification, not general constant treatment of `let` or new `const` syntax.

Length evaluation uses checked integer arithmetic. Resolve typed constants and length parameters (isize) first. Established operand Types guide unresolved literals and literal-only subexpressions under same-Type arithmetic; absent such evidence, default them to isize. Typed constants keep their Type. Different established integer Types are incompatible even if their widths or values match; the final nonnegative-isize range check does not convert operands. Intermediate overflow uses the arithmetic Type, including i32 inferred for an ordinary binding.

Signed intermediate values may be negative. A negative or out-of-range final length, noninteger value, overflow, or zero divisor is a compile-time error. Evaluation is optimization-independent and does not expand ordinary or directive constant evaluation. Type formation runs no static initializer or getter.

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

For an ordinary `let Small = 4`, Small is i32, so `[(Small * 2) of u8]` computes in i32 and then checks the final length range. Combining Small with an independently typed isize operand is invalid; annotate length constants as isize at their declarations when native-width arithmetic is intended. Length expressions introduce no extra conversion syntax.

Separate-compilation artifacts retain folded integer values and Types, or verified slot-dependent expressions and formation obligations. Record referenced declaration Identities and dependencies; changes invalidate dependent artifacts and recompute Type, layout, and selection keys. Length changes have no ABI compatibility guarantee. Check constant accessibility at the definition: a private constant need not become public when its value can be exported without private-name lookup. Expand private constants there and export conditions checkable by clients. Ordinary API accessibility still applies to element Types and other signature contents.

The root-level bindings above are implicit startup locals (§22.2), even when Constant-readable. In a Library, an explicit-main Application, or a declaration-only document, put reusable constants in a group:

```kimi
group Dimensions
    public let Width: isize = 4
    public let Height: isize = 3
```

Use `Dimensions.Width` in a length expression. Compile-time reading does not run its static initializer; runtime access retains ordinary first-access initialization.

A private constant's expanded value becomes part of any public API Type or formation condition that uses it. Private access protects the declaration's Name, not secrecy or compatibility of an exported value. Changing that value may break callers and requires dependent invalidation/rebuilding. This exception is intentional: a folded length contains an integer value rather than a private Type or a client-side reference to the private declaration. No compatibility warning is mandated; an implementation may offer an API-change diagnostic.

## 4.3. Initialization and inference

With an expected fixed-array Type, an array literal constructs that Type and must contain exactly N elements; no padding or truncation is allowed. Acquire elements in source order by ordinary Copy/Move. A local binding annotation may use `[N of _]`, recursively for nested arrays, to infer only the element Type from its initializer. Require a unique Type at declaration. This placeholder is forbidden in lengths, signatures, and explicit generic arguments.

Without a fixed-array expectation, an independent array literal constructs Array. Call-argument literals remain subject to candidate-local fitting under [length-argument inference](#44-function-length-parameters); do not default them to Array first. Empty literals require an expected element Type. Numeric element defaults follow ordinary inference.

An independent literal may form an Array with safe-borrow elements. Infer and preserve complete element Types and their Origins under the ordinary local rules; apply permitted Origin shortening without merging distinct Loans. A fixed-array expectation still selects a fixed array, for example `let views: [2 of ref/i32] = [x@ref, y@ref]` for initialized integer locals x and y.

An annotation-only declaration remains Uninitialized: no zero fill or default element construction occurs. Initial construction requires a whole-array initializer or one whole-array assignment; element-by-element writes into an unconstructed array are forbidden. After completed construction and Partial Move, missing elements may be reinitialized through eligible static Move Paths with ordinary write permissions. Whole-value reads and borrows require completeness.

**Construction boundary.** Fixed arrays have no fill/repetition, generator, or default construction. Construct a literal of exactly N elements or acquire a complete array from an input/result. A declaration `[N of u8]` alone yields no usable buffer, even for known large N. Generic functions may still inspect/mutate supplied initialized arrays. Future fill/generator rules must define evaluation counts, Copy versus repeated construction, zero length, and partial cleanup.

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

Declare a length slot as `<length N>`. A plain `<N>` remains a Type slot; neither signature use nor the body infers a slot's kind. `length` is contextual only at the start of a generic parameter declaration followed by its Name. It does not change ordinary names or `.length`.

First bind the declaration list's kinds, order, and names and reject duplicates; then bind the signature and body. A length slot is a nonnegative isize constant in the Value namespace, not a complete Type or a Semantics/Type pair. Diagnose kind misuse at the use and identify its declaration. Only function length slots are introduced: no value parameters on Type declarations, general Const generics, or source length-constraint syntax.

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

Within each overload candidate, bind explicit arguments first, then infer length and element Type from known fixed-array Types and literals fitted to that candidate. Evidence from all arguments must agree; do not retype an already established Array value. Explicit lists supply all slots in order, using a length constant expression as defined in §4.2, without the length keyword, for length slots and complete Types for Type slots.

Do not solve length equations backward: check `[(N * 2) of T]` after N is known. Expected Types may fill unresolved parts without changing input-established Types or lengths. Evidence may come from a binding annotation, typed assignment destination, declared result, or known parameter. Nested calls and anonymous functions obey [inference boundaries](10-overload-resolution-and-inference.md#105-inference-boundaries-and-specialization); explicit `@` and result constructs follow [adaptation inference](13-operators-and-assignment.md#1352-static-selection-and-inference) and [result validation](14-control-flow.md#149-result-validation). Do not infer a callee's length arguments from its implementation body, later uses/member accesses, guessed unresolved sibling expressions, or candidate order.

```kimi
func consume<T>(x: Array<T>) => ()
func consume<length N, T>(x: [N of T]) => ()
consume([1, 2, 3]) // Error: both fit and neither candidate is better.
```

Fixed and dynamic array literals receive no automatic preference beyond ordinary candidate comparison.

This ambiguity is intentional when the candidates otherwise tie; it does not prohibit offering both overloads. Use a typed intermediate (`let fixed: [3 of i32] = [1, 2, 3]` or `let dynamic: Array<i32> = [1, 2, 3]`), distinct function names, or an explicit generic argument list whose kinds uniquely select a candidate. Array's default for independent expressions is not an overload-ranking rule.

**Formation obligations.** Each signature length expression E generates `ValidLength(E)`: the indivisible compiler obligation that typed E evaluates with checked integer operations to nonnegative isize. Logical expansion or minimization is not required. A nondependent failure rejects the declaration; a dependent obligation is a public applicability condition. For example, N - 1 needs N >= 1 and N / M needs M != 0, without imposing nonnegativity on all intermediate values.

At concrete calls, check these obligations after inference and before candidate comparison; false obligations eliminate candidates. Their strength neither ranks candidates nor solves lengths backward. A representation failure discovered after selection, such as an excessive concrete layout, cannot reopen overload selection.

Verify the body for every binding satisfying its public conditions. Body lengths and callee conditions must follow from the declaration or produce a definition error; do not defer semantic checking to convenient instantiations or infer extra applicability conditions from the body. Length proof is limited to concrete evaluation, the inherent nonnegative-isize property of length parameters, and reuse of identical normalized formation obligations. Symbolic physical layout remains an ordinary instantiation-time representation obligation.

For signatures, dependent fixed-array Types, and formation obligations, normalize typed length expressions bottom-up:

1. Remove grouping, evaluate nondependent subexpressions with checked arithmetic, and replace parameter Names with their declaration slot positions.
2. At each binary integer `+` or `*`, sort the two normalized operands by a deterministic structural order using node kind, Type Identity, constant value, and bound Symbol/slot Identity.
3. Preserve all Types/operators and the operand order of `-`, `/`, and `%`. Do not flatten, reassociate, distribute, or cancel.

With matching slots and integer Types, `N + 4`, `N + (2 + 2)`, `((N + 4))`, and `4 + N` coincide, as do `N * M` and `M * N`. `(N + 2) + 2` need not equal `N + 4` because intermediate overflow matters. This normalizes pure length expressions only; it does not reorder runtime evaluation. Concrete Type and full-specialization keys use evaluated lengths. Each length slot counts once in GenericArity.

```kimi
func reordered<length N>(value: [(N + 4) of u8]) -> [(4 + N) of u8] => value
// Valid: both the dependent Type and ValidLength obligation normalize identically.
```

## 4.5. Operations and ownership

Fixed arrays and Array expose public read-only `length: isize` and `indices: ResolvedRange`, with the [metadata acquisition rules](#461-access-and-length-metadata). Fixed arrays have no resizing operation. Element writes obey ordinary `var`, `let`, and borrowed-access permissions.

A fixed array is Copy exactly when its complete element Type is Copy. Derive Owned and retain element Origins/Loans recursively. Partial Move follows [Move Paths](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move). [Aggregate cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup) destroys remaining initialized elements in decreasing index order, including abandoned construction on ordinary control transfer; skip Uninitialized/Moved parts and recursively clean partly built elements. Abort does not guarantee cleanup.

Array is Non-Copy and accepts any valid complete element Type with representable element layout; neither Owned nor Copy is required. Its Type preserves T's Origin dependencies and Loan requirements, and its values preserve the acquired elements' Loans under [ordinary storage](15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts). To obtain a shared view of either owning array form, explicitly slice it.

The element position preserves Origin variance; compose nested variance normally, while `uniq/Array<T>` remains invariant in its complete Referent Type. This adds no covariance between different element Cores. Core's dynamic mutation operations are defined in §4.7; they require exclusive access to the Array as a whole, independently of reallocation. Ordinary indexing does not gain a Non-Copy Move operation.

Array analysis retains element-originated Loans under §4.7.5's common collection dependency contract, including after removal, replacement and clear.

**Mutation boundary.** Slice has no mutable/exclusive-element form in this revision. Mutate initialized array elements through authorized access to the owning array or a `uniq` borrow of the whole array; `uniq/Slice<T>` changes only the handle, never the element permissions. To process a mutable subrange, pass a whole-array exclusive borrow plus bounds and index the array, or iterate its saved indices while accessing each element. A non-lending iterator does not prohibit such indexed mutation. Bounds validation, active Loans, and ordinary initialization checks still apply; no disjoint mutable subviews are implied.

```kimi
var values: [4 of i32] = [10, 20, 30, 40]
values[1] = 25
let count = values.length
let middle: Slice<i32> = values[1..3] // Infer the borrow of values' storage.
```

## 4.6. Indexing and slicing

### 4.6.1. Access and length metadata

These built-in operations apply to `[N of T]`, `Array<T>`, and `Slice<T>`. Element indexing accepts isize or Index; range indexing accepts Range or ResolvedRange. Dictionary indexing instead takes keys. [Raw pointers](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing) retain signed-isize offsets without safe sequence bounds checks and accept neither Index nor Range. String indexing units and user-defined indexers are not introduced.

| Core | Meaning |
| --- | --- |
| Index | Copy, Owned position measured from the start or end; retains no target |
| Range | Copy, Owned unresolved boundaries and end-inclusion flag; not Iterable |
| ResolvedRange | Copy, Owned validated absolute half-open interval; finite isize iteration |
| `Slice<T>` from source | Copy shared view, independent of T's Copy capability; retains backing Origin and shared Loan |

These names are not keywords; `::Core.Index`, for example, disambiguates a hidden alias. Prefix `^` and range syntax always construct the designated Types from the Core Kotonoha, never same-named user Types.

**Length metadata.** Fixed arrays and Array provide length and indices; Slice also provides isEmpty. Evaluate the receiver once and require ordinary initialization, completeness, and access legality. Knowing a fixed length does not erase receiver effects or checks.

| Receiver | Acquisition |
| --- | --- |
| Fixed array | Check shared access and use Type-level N without reading elements |
| Array | Share access during the operation and read its current length |
| Slice | Copy the handle and read its stored length, without reading backing elements |

Returned integers, Booleans, and ResolvedRange values acquire no receiver/source Origin or Loan; this does not release existing Loans. indices is a snapshot of [0, L) at acquisition. Resizing an Array does not update a saved snapshot; later accesses check the then-current length.

**Element Places.** Bind the receiver and index Types, resolve bounds, and check access to produce an element Place carrying complete Type T, source location, and permissions. Place formation itself performs no element Copy/Move. Chained access such as `matrix[1][2] = 10` preserves Places without copying an intermediate inner array.

| Use | Operation after locating the Place |
| --- | --- |
| `values[i] = value` | Ordinary initialization/replacement with write permissions and Loan checks |
| `values[i]@ref` / `@uniq` | Shared/exclusive borrow of that Place; exclusive access requires write permission |
| Non-Copy element acquisition | Move only through an eligible static Move Path of an owned fixed array |
| Ordinary value read | Copy/Move on an eligible owned fixed-array static Move Path; otherwise shared reading |

Static paths use only the [integer-literal recognition rule](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move). Array, runtime indices, Index values, and paths through borrows do not become movable. Ordinary reads Copy when allowed; eligible Non-Copy static elements Move. Other reads follow the [shared result rules](#466-slice-operations-and-element-results), including Object Semantics and Reborrow rather than an unconditional ref/T result. @ref borrows the element Place regardless of index form. Slice Places are shared-only.

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

Index exposes public read-only `offset: isize` and `isFromEnd: bool`. Its constructor is `init(offset: isize, fromEnd?: bool = false)`.

| Construction | Meaning |
| --- | --- |
| `Index.init(n)` | Offset n from the start; the first element is zero |
| `Index.init(n, fromEnd: true)` / `^n` | Offset n backward from the end boundary |

Prefix ^ produces a storable, passable Index outside indexing expressions too; infix ^ remains integer exclusive-or. Write `^(n + 1)` for a compound from-end distance. Integer indices, range boundaries, and ^ operands have expected Type isize. Already typed i32/usize values require normal explicit conversion. An isize in an index/boundary position denotes a start-relative offset; this adds no general implicit conversion between isize and Index.

Construction with a negative offset initiates Abort without consulting a target length. At length L, start-relative n resolves to n and end-relative n to L - n. Thus ^1 selects the last element and ^0 denotes the one-past-end boundary.

Index implements Equatable by direction and offset, not by coincidental resolution to the same target position. It provides neither Comparable nor arithmetic.

```kimi
let first: Index = Index.init(0)
let last: Index = ^1
let values: [4 of i32] = [10, 20, 30, 40]
let a = values[first] // 10
let b = values[last]  // 40
let end = ^0         // Valid Index; values[end] is out of bounds.
```

### 4.6.3. Range and ResolvedRange

**Range** is a nongeneric unresolved range specification, constructed only by range syntax; no Range.init is introduced. Boundaries accept isize or Index and normalize integers to start-relative Index values. A negative integer boundary initiates Abort at construction.

| Syntax | Interval |
| --- | --- |
| `start..end` | Include start, exclude end |
| `start..=end` | Include both boundaries |
| `start..` | From start through the target's end |
| `..end` / `..=end` | From the start, excluding/including end |
| `..` | Entire target |

Range exposes public read-only `start: Index`, `end: Index`, and `isInclusive: bool`. Omitted start normalizes to start-relative zero, omitted end to ^0. An inclusive end cannot be omitted. Construction neither borrows an array nor checks boundary order. A saved Range can apply to different targets; it has no target-independent length or isEmpty.

Range implements Equatable by normalized start, end, and isInclusive. Thus .. equals 0..^0, but 1..3 differs from 1..=2. To compare resolved intervals, compare their ResolvedRange values.

**ResolvedRange** always satisfies 0 <= start <= end <= maximum isize. It exposes public read-only `start: isize`, `end: isize`, `length: isize = end - start`, and `isEmpty: bool`. Obtain it through Range.resolve/tryResolve, sequence indices, or `ResolvedRange.init(start: isize, end: isize)`; invalid constructor bounds initiate Abort. No setter or implicit construction bypasses validation.

ResolvedRange implements Equatable by start and end. It retains no storage Origin/Loan; application to an array or Slice rechecks the target bounds. Neither range Type implements Comparable, and no implicit conversion or cross-Type equality is provided.

**Iteration.** ResolvedRange implements `Iterable` with associated Type `Element = isize`: yield start through end - 1 in unit steps, nothing for an empty interval, and remain exhausted after None. Never compute beyond end, including maximum isize. Range is never Iterable, even with absolute boundaries; conformance cannot depend on spelling/value. Use values.indices or explicitly construct/resolve intervals. Infinite, descending, stepped, or negative ranges and dedicated ResolvedRange syntax are unavailable.

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

Ranges are non-associative and bind below logical/arithmetic operations but above assignment. Prefix ^ has ordinary prefix precedence. Reject a..b..c syntactically and (a..b)..c by boundary Type.

| Expression | Interpretation |
| --- | --- |
| `a + 1..b * 2` | (a + 1)..(b * 2) |
| `^n + 1` | (^n) + 1; Type error because Index has no addition |
| `^(n + 1)` | From-end distance n + 1 |
| `0..^1` | 0..(^1) |
| `a ^ b..c` | (a ^ b)..c; integer exclusive-or first |

### 4.6.4. Bounds, evaluation, and failure

Target length L is a nonnegative isize. Resolve boundaries to absolute positions and check the following conditions without clamping or turning reversed intervals into empty ones.

| Operation | Valid condition | Result |
| --- | --- | --- |
| Index boundary resolution | 0 <= n <= L | n from the start, L - n from the end |
| Element access | 0 <= resolved p < L | Element p |
| Half-open Range | 0 <= start <= end <= L | [start, end) |
| Inclusive Range | 0 <= start <= end < L | Normalize to [start, end + 1) |
| ResolvedRange application | 0 <= start <= end <= L | [start, end) |

Check n <= L before from-end subtraction and end < L before adding one to an inclusive end; valid resolution cannot overflow.

| Operation | Result Type | Invalid length/bounds |
| --- | --- | --- |
| index.resolve(length) | isize boundary | Abort |
| index.tryResolve(length) | `Option<isize>` | None |
| range.resolve(length) | ResolvedRange | Abort |
| range.tryResolve(length) | `Option<ResolvedRange>` | None |

Here index is Index, range is Range, and length is isize. These operations take small Copy values and access no target storage. Successful Index resolution validates a boundary, not an element: (^0).resolve(L) returns L. A valid ResolvedRange may still fail on a shorter target.

```kimi
let r = ResolvedRange.init(start: 2, end: 5)
let shortArray: [3 of i32] = [1, 2, 3]
let checked = shortArray[..].trySlice(r) // None.
let failed = shortArray[r]              // Abort if executed.
```

values[..] and values[L..] also apply to empty arrays. values[L..L] and values[^0..] are empty; element index L or ^0, and an inclusive end of ^0, are invalid.

Ordinary element/range indexing initiates Abort on invalid bounds. Use Slice.tryGet/trySlice for expected input failures. A try operation converts only its own length/bounds failure to None, not failures in argument evaluation or other operations: slice.tryGet(^(-1)) aborts during Index construction, while slice.tryGet(-1) returns None. Successful try operations return Some.

Evaluate the receiver and index once under [evaluation order](12-expressions.md#122-evaluation-order). A range expression evaluates start then end, then checks integer boundaries for nonnegativity in the same order. Failures inside boundary expressions, including ^ construction, stop subsequent evaluation immediately. These are independent failure examples:

```kimi
func sideEffect() -> isize
    return 2 // Represents an observable effect.

let a = (-1)..sideEffect()  // Evaluate sideEffect, then Abort constructing Range.
let b = ^(-1)..sideEffect() // Abort constructing Index; do not call sideEffect.
```

Locate the built-in access receiver first. While evaluating its index, prohibit modification, destruction, Move, or reallocation of that storage; shared reads remain allowed, including values[values.length - 1]. Establish a write's exclusive Loan after resolving bounds and check existing Loans. Never reevaluate the receiver or boundaries. For a Slice, first Copy its handle; reassigning the original handle does not change the acquired view.

[Simple assignment](13-operators-and-assignment.md#1371-simple-assignment) secures its RHS before locating the indexed target. [Compound assignment](13-operators-and-assignment.md#1372-compound-assignment) evaluates receiver/index, checks bounds, reads the old value, evaluates the RHS, computes, and writes back, once each. Increment/decrement use the same target/Loan rules. Arithmetic failure prevents writeback, and the established exclusive Loan forbids conflicting RHS access. [Exchange operations](15-ownership-and-lifetime-analysis.md#157-initialization-preserving-exchange) evaluate arguments left to right, retaining each target's exclusive Loan during later arguments; Swap requires static non-overlap, not merely runtime i != j. Exchange/Swap remain conceptual names with no additional source API here.

Apply the common [Abort and constant-evaluation rules](17-failure-handling.md#1734-checks-builds-and-constant-evaluation). Syntax, Type, literal-fitting, and required constant-evaluation violations are compile-time errors. Ordinary out-of-bounds a[10] for a three-element array instead aborts if executed. A compiler may warn, but optimization must not turn such a runtime failure into language-level rejection. Rejection of an ineligible static Move Path is a separate rule.

### 4.6.5. Slice storage, lifetime, and permissions

`Slice<T> from source` shares an initialized contiguous region of complete element Type T. Formation requires ordinary initialization and access checks and cannot hide Uninitialized or partially Moved storage. Its runtime length does not participate in Type identity. Every range access returns Slice, including constant-length ranges, without copying elements into a fixed array.

Slice indices start at zero; from-end positions and reslicing use the current Slice length. Original array indices are not retained. Slicing rows of a nested fixed array produces `Slice<[N of T]>` without flattening. Element inheritance or matching layout does not permit conversion between different element-Type Slices.

**Origins and Loans.** The Origin records backing lifetime; the Loan records conflicting Places and permissions. Handle copies, reslices, iterators, and references to element slots retain both. They need not borrow the handle variable itself: acquired views remain usable after that variable ends if the backing storage and inherited Loan remain valid.

Under [static Place analysis](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), an array-derived Slice borrows the array Place as a whole. Reslicing, splitAt, and empty Slices retain that Loan's conflict footprint. Runtime interval resolution neither narrows it nor proves disjointness. Thus a later use of empty from `let empty = values[1..1]` conflicts with `values[3] = 10`, because of the retained shared Loan, not the Origin alone. The Loan ends after all required derived uses end. Source Move, destruction, or reallocation is also forbidden while it would invalidate an active view.

Slice owns no elements; handle Copy/destruction neither copies nor destroys them. T need not be Owned, and all dependencies inside T remain intact. source may shorten but cannot lengthen; Slice's own Owned classification follows [OwnedOrigins](15-ownership-and-lifetime-analysis.md#1523-static-and-owned), including source and T.

**Storage and escape.** Slice may be retained in local aggregates, Array elements, and concrete object payloads under [ordinary storage](15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts). Preserve its backing Loan and nested element dependencies. Static storage requires Owned and a valid static source under [static storage](11-properties.md#1132-static-storage); copying or storing a Slice never extends the backing lifetime.

Borrowing a temporary never extends its [lifetime](03-types-and-values.md#36-temporary-values-places-and-lifetimes). Do not reject an unused binding solely because it contains a temporary borrow; check whether later use, return, or retention requires the expired dependency.

```kimi
func makeArray() -> Array<i32> => [1, 2, 3]
func inspect<T> origin source(values: Slice<T> from source) => ()

inspect(makeArray()[..]) // Temporary array survives through the call.
let escaped = makeArray()[..]
inspect(escaped)         // Error: temporary expired at the initializer boundary.
let owned = makeArray()
let lasting = owned[..]
inspect(lasting)         // Borrows an owning local.
```

A var Slice permits handle reassignment only. `uniq/Slice<T>` exclusively borrows the handle, not its elements. Mutable Slices, implicit owning-array conversion, safe raw-pointer construction, Slice equality, and implicit elementwise comparison are not introduced.

### 4.6.6. Slice operations and element results

For s: `Slice<T>` from source, members receive and Copy the handle by value. Element/partial-Slice results retain source rather than borrowing the call's handle variable. All listed operations are public.

| Operation | Result and conditions |
| --- | --- |
| s.length: isize / s.isEmpty: bool | Read-only count / whether count is zero |
| s.indices: ResolvedRange | Read-only snapshot under the metadata rules |
| s[index] | Shared element access; accepts isize or Index |
| s[range] | `Slice<T>` from source; accepts Range or ResolvedRange, checked at current length |
| s.tryGet(index) | `Option<ref/T from source>`; separate isize/Index overloads |
| s.trySlice(range) | Option<`Slice<T>` from source>; separate Range/ResolvedRange overloads |
| s.splitAt(index) | (`Slice<T>` from source, `Slice<T>` from source), covering [0, p) and [p, length) |
| s.trySplitAt(index) | Option of that Tuple |

Both split operations have isize and Index overloads and accept boundaries zero and length. Invalid boundaries abort for splitAt and return None for trySplitAt. tryGet and trySlice likewise return None on their own invalid bounds. tryGet deliberately has a fixed reference result independent of T's Copy capability.

A shared element read uses the following **SharedReadResult(T, source)** rules. These are element-access operations, not Field getters, and never move the element or implicitly duplicate object ownership.

| Complete element Type | Result | Operation |
| --- | --- | --- |
| Copy `owner/T` | T | Copy |
| Non-Copy `owner/T` | `ref/T` | Shared storage Borrow |
| `obj/T`, `rc/T`, `arc/T` | `objref/T` | Shared object Borrow; no reference-count change |
| `ref/T`, `objref/T`, `unsafe/T` | Same Type | Copy; pointer dereference remains unsafe |
| `uniq/T` | `ref/T` | Shared Reborrow |
| `objuniq/T` | `objref/T` | Shared object Reborrow |

New borrows/reborrows are bounded by source storage and existing dependencies. Copy preserves the stored reference's Origins; Reborrow suspends conflicting parent access while live. Other sequence operations referencing this table retain their own path and write permissions.

For s[index], `@ref` instead borrows the element Place itself. Element writes, Move, and exclusive borrows through Slice are forbidden.

For `s: Slice<Result<i32, i32>>`, `s[0]` is an owned Copy Result, while `s[0]@ref` borrows its slot and `s.tryGet(0)` returns `Option<ref/Result<i32, i32>>`. Adding conditional Copy to an element Type can therefore change inferred read Types, argument fitting, and Origin obligations. Use explicit slot borrowing when an API requires a reference independently of Copy.

For unknown T, retain the correlated result Type, acquisition effect, and Origins as the internal family SharedReadResult(T, source), not a source-spellable Type. Verify the body for all admitted cases; neither assume unknown means Non-Copy nor defer Type checking until a favorable instantiation. Operations/results that do not fit every case require a constraint or explicit borrow.

```kimi
func first<T> origin source(s: Slice<T> from source) -> T
    T is Copy
    return s[0]

func firstRef<T> origin source(s: Slice<T> from source) -> ref/T from source
    return s[0]@ref // Borrow the slot regardless of T's Copy capability.

func head<T, E> origin source(s: Slice<Result<T, E>> from source) -> ref/Result<T, E> from source
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

func tryTail<T> origin source(values: Slice<T> from source) -> Option<Slice<T> from source>
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

Slice implements Iterable with Element = ref/T from source, including Copy elements, yielding shared references in index order. The iterator retains a handle and position, not owned elements. Yielded references borrow backing slots, not the iterator's receiver/storage, satisfying the [non-lending protocol](14-control-flow.md#1462-iteration-protocol-and-acquisition). It stays exhausted after None.

Keep element-internal Origins separate from slot-borrow Origins:

```kimi
let data: i32 = 10
let refs: [1 of ref/i32] = [data@ref]
let s: Slice<ref/i32> = refs[..] // Infer inner and backing Origins separately.
let value = s[0]       // Copy the inner reference to data.
let slot = s.tryGet(0) // Option containing a shared borrow of refs[0].
for element in s
    () // element shares a slot containing the inner reference.
```

The outer references from tryGet and iteration borrow refs' slots; the inner reference depends on data. The inner Origin must remain valid while using the outer reference. After copying an inner reference, preserve its own Origin without attaching a new slot borrow.

### 4.6.8. Representation and performance

Index/Range/ResolvedRange construction, resolution and Copy, and Slice creation, Copy, reslicing, splitting, and address calculation take O(1) time in element count. They require no additional element storage, heap allocation, or reference-count update. Element Copy and user-code costs are separate. Iteration takes O(n) time and O(1) extra storage; do not first materialize an array of iteration values.

A Slice's semantic representation retains backing-element location or equivalent provenance, nonnegative isize length, and static Origins/Loans. Element spacing is stride(T). Empty Slices and zero-sized elements retain source provenance. No universal pointer-plus-length ABI, runtime lifetime tag, or pointer to a disappearing handle variable is required. Use logical counts/positions rather than subtracting element pointers to recover length; never form invalid pointers before checking.

Checks may be eliminated, shared, or optimized in loops only when safety is proven without changing effects, Abort behavior, or borrow legality. Constant-folding Index/Range operations does not expand the literal-only static Move Path rule.

## 4.7. Dynamic collection mutation

### 4.7.1. Common acquisition and outcomes

`Array<T>` and `Dictionary<K,V>` are Non-Copy owning collections. They accept valid complete stored Types without blanket Copy or Owned constraints. Dictionary requires K is Equatable and §12.3.4's key stability, not a public Hash constraint. Types and Origins are fixed at declaration; mutation never restarts inference.

These are public instance APIs. Mutations use self: uniq/Self unless stated otherwise. Value parameters acquire once by ordinary Copy/Move, without deep cloning or implicit count increments. Rejected Moves are not restored; returned inputs can be recovered from Result. Removal transfers stored responsibility even for Copy elements. Owning a reference value neither owns nor extends its referent's lifetime.

Precondition failures Abort. Ordinary absence uses Option; recoverable rejection with inputs uses Result. A try name promises only its specified recoverable outcome and need not have a corresponding Abort API. Discarded Result follows the normal warning rule.

| Normal outcome | Postcondition |
| --- | --- |
| Add | Increase length, preserve prior relative order, grow capacity if needed |
| Replace | Change only the value; preserve stored key, order, length and capacity |
| Remove | Decrease length; preserve remaining order and capacity |
| Clear | Destroy all elements; set length to zero; preserve capacity |
| Reserve / shrink | Preserve values, order and length; change capacity/placement only as specified |
| Lookup, absence or duplicate rejection | Preserve values, order, length and capacity |

These postconditions do not roll back external effects from arguments, equality or destructors.

### 4.7.2. Array operations

| Operation | Behavior |
| --- | --- |
| `append(value: T) -> ()` | Add at the end |
| `insert(index: isize, value: T) -> ()` | Insert before the resolved position |
| `insert(index: Index, value: T) -> ()` | Same, with a directional Index |
| `pop() -> Option<T>` | Remove and return the last element, or None when empty |
| `remove(index: isize) -> T` | Remove and return the selected element |
| `remove(index: Index) -> T` | Same, with a directional Index |
| `clear() -> ()` | Destroy all elements in §4.7.6 order |

Resolve the index once in the body against entry length L. For a from-end Index, require offset <= L before p = L - offset. Insert requires 0 <= p <= L; remove requires 0 <= p < L. Invalid indices Abort. There is no implicit isize/Index conversion or Range overload.

insert(^0, value) appends, including to an empty Array. On a nonempty Array, insert(^1, value) inserts before the last element and remove(^1) removes it. remove(^0) is always invalid. Failure constructing an Index prevents later argument evaluation. Indexed reading still cannot Move a Non-Copy dynamic element; indexed assignment destroys the old value, unlike remove.

```kimi
var values: Array<i32> = []
values.reserve(additional: 3)
values.append(10)
values.insert(^0, 30)
values.insert(^1, 20) // [10, 20, 30]
let last = values.remove(^1) // 30; capacity is unchanged.
let first = values[0]
values.append(first)
// values.append(values[0]) is invalid: the exclusive receiver is already active.
```

### 4.7.3. Dictionary operations and indexed replacement

| Operation | Absent key | Equal stored key |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K, V)>` | Append; Ok(()) | Unchanged; Err((input key, input value)) |
| `insertOrReplace(key: K, value: V) -> Option<V>` | Append; None | Keep stored key and position; Some(old value) |
| `remove(key: ref/K) -> Option<(K, V)>` | None | Remove and return stored key and value |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref/V from self>` | None | Shared reference to stored value |
| `clear() -> ()` | Destroy all entries in §4.7.6 order | Same |

Duplicates are tryInsert's only Err outcome; no dedicated error Type is introduced. Both value arguments are acquired before lookup, unlike Dictionary literals (§12.3.4). insertOrReplace secures the old result, stores the new value, then destroys the unused input key; delivery follows cleanup. Removing and later re-adding an equal key appends a new position. No API mutates a stored key.

tryGet always returns ref/V, including for Copy V. It never copies, moves or removes a stored value. Its result protects the whole Dictionary storage and preserves V's dependencies, with no operation-only Loan on the search key. A lookup or saved Boolean observation reserves no future access.

`map[key] = value` replaces an existing value and returns Unit; absence Aborts. It shared-borrows the key as ref/K rather than consuming it:

1. Evaluate and acquire the right-hand V once.
2. Identify the receiver and evaluate the key left to right, once each.
3. Search with shared access and Abort if absent.
4. Obtain exclusive access to the value slot, destroy the old value, and place the new one.

From receiver identification through placement, prevent structural mutation, Move and destruction of the Dictionary. Keep shared access during lookup and check all Loans, including the key's, before writing; do not pretend that borrow ended. Temporary lifetimes follow §3.6 and §10.2. Reject replacement if old-value destruction would invalidate dependencies of the secured right side or retained temporaries. Abort/nontermination during destruction prevents placement. Compound assignment follows §13.7's target-first order, with one search, read, right-side evaluation and write.

```kimi
var names: Dictionary<i32, string> = [:]
match names.tryInsert(1, "first")
    .Ok(()) => ()
    .Err(let entry) => Core.writeLine(entry.1)
names[1] = "replacement" // Existing-key replacement, not insertion.
let removed = names.remove(1) // A temporary key is borrowed under §10.2.
```

### 4.7.4. Capacity and allocation

Both collections expose read-only capacity: isize through shared access: the maximum element/entry count supported without internal allocation, not bytes or buckets. The result is a snapshot without a Loan. Maintain 0 <= length <= capacity <= isize.MaxValue. Typed empty [] and [:] have length/capacity zero and no internal allocation; fixed handle-management cost remains permitted.

| Operation | Contract |
| --- | --- |
| `reserve(additional: isize) -> ()` | Require nonnegative additional; checked R = body-entry length + additional. Abort on failure. Ensure capacity >= R without shrinking. If R <= capacity, preserve internal placement too; zero is always a no-op. |
| `shrinkToFit() -> ()` | Attempt length <= new capacity <= old capacity. Neither exact fit nor returning memory to the OS is guaranteed. |

reserve accepts positional arguments, but examples/diagnostics should explain its **additional** meaning. No reserveCapacity API taking a total is introduced.

No internal allocation is permitted for addition within capacity, reserve with sufficient capacity, empty construction, lookup, existing-value replacement, pop/remove/clear, absence or duplicate rejection. This includes same-capacity reallocation, scratch and Dictionary auxiliary storage. Deletion/re-addition churn must reuse existing storage. Arguments, equality and destructors retain their own effects.

Addition beyond capacity and reserve needing growth may Abort on required allocation failure. Growth follows §4.7.7. Clamp optional overshoot/rounding to representable capacity; rounding overflow alone cannot reject a representable request.

shrinkToFit may allocate. Candidate allocation or candidate-size representation failure returns normally with values, order, length, capacity **and internal placement** unchanged. Prepare while old storage is intact and transfer only after success. This does not catch arbitrary Abort and is outside the growth amortization guarantee.

### 4.7.5. Loans, retained dependencies and call effects

Acquire the whole-collection exclusive receiver before later arguments, even for runtime no-ops. Active element, Slice and empty-Slice Loans conflict with mutation. Duration follows later uses and observable destruction, not necessarily lexical scope. No two-phase reservation exists: obtain an owned Copy or removal result first, then mutate in a separate expression.

Use these public dependency rules without inspecting private bodies:

| Operation | Conservative propagation |
| --- | --- |
| Add / replace | Union input dependencies into the collection's prior set |
| tryInsert | Union both input sets regardless of success; Err also retains input dependencies |
| Remove / return old value | Return pre-update potential dependencies of stored full Types; do not mix replacement-input dependencies into the old result |
| Remove / replace / clear | Do not subtract individual collection dependencies |
| Capacity operations | Preserve dependencies |
| tryGet | Result adds outer storage Loan and V dependencies; collection set unchanged |

Do not refine by literal index or roll back dependencies for None/Err. Emptiness never changes Type, Origin or Owned classification. Owned return values retain no operation-only receiver/key Loan but keep full-Type dependencies. Preserve actual Loan identities, storage anchors and Reborrow relationships; equal Origin names do not merge Loans. Duplicating dependency information does not duplicate exclusive authority. In particular, do not assume a removed uniq/T is independent of remaining `Array<uniq/T>` dependencies; reject conservative conflicts until normal Loan liveness permits use.

Publish these user-code effects, composed with argument/default evaluation and later temporary/result cleanup:

| Operation | User-code effects inside the operation |
| --- | --- |
| Array append/insert/pop/remove; both collections' reserve/shrink | None |
| Dictionary tryGet/tryInsert/remove | K equality |
| Dictionary insertOrReplace | K equality and unused input K destruction |
| Dictionary indexed replacement | K equality and old V destruction |
| Array clear/destruction/indexed replacement | Relevant T destruction |
| Dictionary clear/destruction | V and K destruction |

Generic summaries expose parameter-dependent equality and recursive destruction. Verify at definition checking and substitute validated specialization mappings; do not rescan private bodies or defer legality. Union effects across permitted branches/implementations. A proven empty summary invents no static access. Receiver authority does not excuse external static reentry; unknown possibly conflicting mutable-static effects are rejected under §15.6.4.

### 4.7.6. Commit, failure and destruction order

After normal acquisition, resolve bounds/search and decide absence, duplicate, replacement or addition. Rejection/absence leave the collection unchanged before capacity work. Replacement/removal do not run addition limits: duplicate rejection and replacement work even at maximum length. Only addition checks increased length, required byte sizes and allocation, with checked arithmetic.

Relocation introduces no user Copy, Move or deinit calls. Equality sees consistent structure; no partially moved state or conflicting reentry may be exposed. These are operation invariants, not source concurrency guarantees.

Normal transfer during argument evaluation secures its transfer result, then cleans previously acquired caller arguments/temporaries as required; the operation is not called. Abort supplies no result, rollback or later cleanup. Nontermination prevents later work and delivery.

Array clear/destruction uses reverse current index order. Dictionary clear/destruction uses reverse insertion order, each value before its key. Owning iterators destroy remaining entries in the same order. Normal abandonment of partial construction cleans completed acquisitions/placements in reverse order. Never read or destroy moved elements, spare capacity or uninitialized buckets.

### 4.7.7. Performance and extension boundary

For fixed Types, let n be length, c capacity and d the number of live elements/entries whose complete Types cannot prove cleanup-free destruction. Charge allocator internals, input generation and destruction bodies separately from collection management. Count release is cleanup; inspecting a runtime enum Case is not a free proof.

| Operation | Required bound, excluding separately charged user-code cost |
| --- | --- |
| Array append / pop | Amortized O(1) / O(1) |
| Array stable insert/remove | O(1 + n) |
| Array clear | O(1 + d); O(1) for a cleanup-free element Type |
| Dictionary lookup | Search cost + O(1) management |
| Dictionary mutation | Search cost + amortized O(1) management |
| Dictionary clear | O(1 + c + d) is permitted |

For m operations without shrink, let C be the maximum of initial capacity, observed lengths and reserve requests R. Total growth preparation and transfer management is O(m + C). Repeated reserve(additional: 1) followed by append from empty is O(n), not quadratic; reserve itself has no per-call O(1) promise.

Dictionary search cost includes key-content equality/internal-hash work and candidate/bucket probing for every operation; linear search is permitted. This introduces neither a public Hash constraint nor user-defined hash calls. Relocation, index rewriting and order maintenance remain management cost. Reindexing across growth, tombstones and allocation-free cleanup processes O(m + C) keys in total, not O(1) per key's lifetime. Charge variable-length key work at actual search cost; cached versus recomputed hashes is an implementation choice. Known-entry deletion, placement and order maintenance remain amortized O(1), including allocation-free full-capacity churn. O(c) auxiliary indices/free lists/linked slots and noncontiguous storage are permitted. Avoid temporary arrays or per-element allocations just to preserve order.

Fixed-capacity mutation, mutable Slice/new borrowing iterators, bulk/resize/unordered removal, contains, an Abort-only Dictionary insert, entry/factory APIs, recoverable allocators and a fixed collection ABI remain outside this contract. tryGet followed by indexed replacement may search twice; a future one-search update must define lookup and reference-write authority together.
