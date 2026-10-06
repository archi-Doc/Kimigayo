# 4. Arrays, indexing, and slices

[Specification index](../SPEC.md)

A fixed array `[N of T]` has a compile-time length `N` and a complete element Type `T`, including Semantics and Origins. A dynamic `Array<T>` owns variable-length storage, and `Slice<T>{source}` / `UniqSlice<T>{source}` are shared / exclusive borrowed views. Their [indexing and slicing](#46-indexing-and-slicing) rules are defined together.

## 4.1. Fixed-array identity and layout

Type identity includes the evaluated length and the complete element Type: `[3 of i32]` differs from `[4 of i32]`, while `[(2 + 2) of i32]` equals `[4 of i32]`. There is no implicit conversion between arrays of different lengths, between fixed arrays and Array, or between owning arrays and Slice.

Elements are stored inline in index order; the array's own element storage uses no implicit heap allocation. Nested arrays apply this rule recursively, with the innermost index varying contiguously. Where the enclosing value is stored, and any allocation by individual elements, follow their own rules.

The element layout must be finite and valid. Let `d = stride(T)`; Slice uses the same element spacing. These are specification quantities, not source operators, and follow the [common layout rules](../impl/21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi).

| Quantity | Layout of `[N of T]` |
| --- | --- |
| Offset of element `i` | `i * d`, for `0 <= i < N` |
| Alignment | `alignment(T)`, including when `N = 0` |
| Array stride | `N * d`, the spacing between consecutive array values |
| Size | `N * d`, including all element-stride padding |
| Zero-sized elements | Use `T`'s stride; if `d = 0`, distinct logical elements may share an address |

Size, stride and padding calculations are checked against the target's layout limits and nonnegative `isize`; an unrepresentable concrete layout is a compile-time error. `[0 of T]` has size and stride zero and initializes and destroys no elements, but `T`'s Type, layout and ownership are still checked. Inline embedding is validated before multiplying by zero: a struct that directly stores `[0 of Self]` has a forbidden recursive value layout. Pointer and reference referents do not create inline edges.

## 4.2. Length constants

A length is a nonnegative compile-time integer representable in the target's `isize`. A generic length remains symbolic until instantiation. A length is written as an integer literal, a possibly qualified constant name, a length parameter, or a parenthesized integer constant expression; a compound expression requires parentheses around the entire length.

The initial evaluator admits integer literals, length parameters, **Constant-readable Bindings**, grouping, unary `+` and `-`, and binary `+`, `-`, `*`, `/` and `%`. A Constant-readable Binding is an integer `let` local, or a static stored Property with accessible standard `get`, whose declaration initializer can be evaluated recursively using only these forms, after the normal lookup, access and initialization checks. Parameters, `var`, instance Fields, custom and computed accessors, calls and cyclic initializers are excluded. This is a semantic classification; it neither treats `let` as a general constant nor adds `const` syntax.

Length evaluation uses checked integer arithmetic. Typed constants and length parameters (`isize`) are resolved first. Established operand Types guide [literal-only expressions](12-expressions.md#1231-type-inference) under same-Type arithmetic; without such evidence they default to `isize`. Typed constants keep their Type. Different established integer Types are incompatible even when their widths or values match, and the final nonnegative-`isize` range check does not convert operands. Intermediate overflow is checked in the arithmetic Type, including `i32` inferred for an ordinary binding.

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

Constant accessibility is checked at the definition. A private constant need not become public when its value can be exported without private-name lookup: it is expanded at the definition, and conditions checkable by clients are exported. The expanded value then becomes part of every public API Type or formation condition that uses it. Private access protects the declaration's Name, not the secrecy or compatibility of an exported value; changing the value may break callers and requires dependent invalidation and rebuilding. This exception is intentional: a folded length is an integer, not a private Type or a client-side reference to the private declaration. No compatibility warning is mandated; an implementation may offer an API-change diagnostic. Ordinary API accessibility still applies to element Types and other signature contents.

## 4.3. Initialization and inference

### 4.3.1. Local fixed-array annotations

With an expected fixed-array Type, an array literal constructs that Type and must contain exactly `N` elements; there is no padding or truncation. Elements are acquired in source order under the ordinary acquisition rules (§3.5).

An initialized local `let` or `var` may write `_` for the length, element Type, or both in a fixed-array annotation. Apply the rule recursively only to fixed-array structure actually written in the annotation. The initializer must determine every hole uniquely at the declaration:

- A length hole takes the literal's element count or an established length of the initializer's fixed-array Type. A known element Type supplies an expectation, including to inference through a function's public signature.
- An element hole uses the ordinary element evidence and numeric-default rules. A hole alone does not request another fixed-array dimension: an inner literal without a written fixed-array expectation constructs `Array<T>`.
- Evidence from all elements must agree. A mismatch in written fixed-array structure is an error, never a fallback to dynamic storage.
- Do not use later statements, a callee's implementation body, a dynamic Array's runtime length, or inverse evaluation of a length expression. Acquisition, Origins and candidate resolution retain their ordinary rules.

Holes are forbidden in uninitialized declarations, signatures, Fields, static storage and explicit generic arguments. Function length parameters retain `<length N>` (§4.4).

Without a fixed-array expectation, an independent array literal constructs an Array. A call-argument literal keeps candidate-local fitting under §4.4 instead of first defaulting to Array. An empty literal requires a known element Type. Safe-borrow element Types and their Origins are preserved; permitted Origin shortening does not merge distinct Loans.

```kimi
var values: [_ of i32] = [1, 2, 3] // [3 of i32].
let inferred: [_ of _] = [1, 2, 3] // [3 of i32].
let empty: [_ of i32] = []         // [0 of i32].
let dynamic = [1, 2, 3]           // Array<i32>.
let rows: [_ of _] = [[1], [2, 3]] // [2 of Array<i32>].
let matrix: [_ of [_ of f32]] = [[1.0, 2.0], [3.0, 4.0]] // [2 of [2 of f32]].
// let unknown: [_ of _] = []                 // Error: no element Type.
// let ragged: [_ of [_ of _]] = [[1], [2, 3]] // Error: conflicting inner lengths.
// let wrong: [3 of i32] = [1, 2]             // Error: element count.

func zeros<T>() -> [3 of T]
    T is PrimitiveInteger
    return [3 of 0]

let wide: [_ of i64] = zeros() // T = i64 from the annotation; length 3 from the signature.
```

### 4.3.2. Ordinary initial construction

An annotation-only declaration remains Uninitialized: there is no zero fill or default element construction. Initial construction requires a whole-array initializer or one whole-array assignment; element writes into an unconstructed array are forbidden. After completed construction and a Partial Move, eligible static Move Paths may be reinitialized under ordinary write permissions. Whole-value reads and borrows require completeness.

```kimi
let pending: [2 of i32]
pending = [1, 2] // Whole initial construction; pending[0] = 1 cannot construct it.
```

### 4.3.3. Fill construction

`[Length of value]` constructs a fixed array with or without an expected Type; an `Array<T>` expectation is a mismatch, not a conversion. `Length` follows §4.2, including parentheses around a compound expression. The element Type follows ordinary expectation and literal rules and must be Copy. Evaluate and acquire `value` exactly once, even for length zero, then copy it into all elements; a bare Place is acquired by Copy. This adds neither generator/default construction nor borrowing of uninitialized storage. [Fill-store elimination](utf8-formatting.md#6-optimization-and-output) must preserve evaluation and acquisition of `value`.

```kimi
let zeros: [64 of u8] = [64 of 0]
let flags = [8 of false] // [8 of bool].
```

### 4.3.4. Skipping initial stores with `noinit`

`noinit` is an initialization directive, not a value. Recognize it when the entire initializer after a declaration's `=` is the unparenthesized word `noinit`, before looking up a same-named variable. Check eligibility afterward; an ineligible directive is not reinterpreted as a name. Elsewhere it is an ordinary identifier: `(noinit)` and qualified names are expressions.

The directive requires a local `var`, an explicit fixed-array annotation without holes, and an Unsafe Block. The direct element Type, after aliases are resolved, must be an `owner` Scalar: an integer, wrapping integer, floating-point Type, `bool` or `char`. References, raw pointers, user Types and nested arrays do not qualify. Length zero is permitted. A generic definition must prove eligibility from existing Constraints, such as `PrimitiveInteger`; `Copy` alone is insufficient, and no Scalar Contract is introduced.

The declaration completes construction and marks the ordinary whole array Initialized, while omitting its initial element stores. No special Type, continuing state, provenance flag or per-element initialization tracking attaches to it or to derived values. All later Type, access, assignment, acquisition, Loan, Origin, raw-memory and destruction rules are the ordinary rules for an initialized array.

**Programmer obligation.** Write a valid value before reading an element. A read before that write is undefined behavior; detection is not required. This holds through functions, references, views and raw pointers. Residual bits are not a write, including for `bool` and `char`. Copy, Move and by-value passing of the whole array read all its elements. Later operations require only their ordinary Unsafe permission.

Simple element assignment, address formation, borrowing, `slice` / `sliceUniq`, metadata access and Scalar-array destruction do not read the old elements and are valid immediately. Optimizations and calling conventions must preserve these defined operations: they cannot introduce extra element reads or require referent-value validity before the source program does (§21.5.5).

```kimi
unsafe
    var buffer: [3 of i32] = noinit
    var view = buffer.sliceUniq()
    view[0] = 10
    view[1] = 20
    view[2] = 30
    let copied = view.slice().toArray() // Every element was written before this read.
```

The directive uses its enclosing Unsafe Block's permission (§14.3.3). Compiler services retain its source position and resolved array Type under the ordinary snapshot rules (§23); this does not require tracking subsequent element writes. Diagnostics must not offer adding `unsafe` or `noinit` as an unconditional repair.

## 4.4. Function length parameters

A length slot is declared as `<length N>`. A plain `<N>` remains a Type slot; neither signature use nor the body infers a slot's kind. `length` is contextual only at the start of a generic parameter declaration followed by its Name; it does not affect ordinary names or `.length`.

The kinds, order and names of the declaration list are bound first, rejecting duplicates; then the signature and body are bound. A length slot is a nonnegative `isize` constant in the Value namespace, not a complete Type or a Semantics/Type pair. Kind misuse is diagnosed at the use and identifies the declaration. Only function length slots exist: there are no value parameters on Type declarations, general Const generics or source length-constraint syntax.

```kimi
func process<length N>(values: [N of i32])
    let count: isize = N
    ()

func keep<length N, T>(values: [N of T]) -> [N of T] => values
func work<length N>()
    let buffer: [N of u8] = [N of 0]

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

Fixed arrays and Array expose the [length metadata](#461-access-and-length-metadata) `length` and `indices`, and the read operations `tryGet`, `trySlice`, `splitAt` and `trySplitAt` of §4.6.6. Fixed arrays have no resizing operation. Element writes obey ordinary `var`, `let` and borrowed-access permissions.

A fixed array is Copy exactly when its complete element Type is Copy (§3.5.1). Owned is derived, and element Origins and Loans are retained, recursively. Partial Move follows [Move Paths](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move). [Aggregate cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup) destroys the remaining initialized elements in decreasing index order, including abandoned construction on an ordinary control transfer; Uninitialized and Moved parts are skipped, and partly built elements are cleaned recursively. Abort does not guarantee cleanup.

Array is Non-Copy and accepts any valid complete element Type with a representable element layout; neither Owned nor Copy is required. Its Type preserves `T`'s Origin dependencies and Loan requirements, and its values keep the acquired elements' Loans under [ordinary storage](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision), including after removal, replacement and clear (§4.7.5). A shared view of either owning array form is formed only explicitly, by range indexing or the read operations of §4.6.6; no implicit conversion to Slice exists.

The element position preserves Origin variance, and nested variance composes normally, while `uniq/Array<T>` remains invariant in its complete Referent Type. No covariance between different element Cores is added. The Kimi dynamic mutation operations (§4.7) require exclusive access to the whole Array, whether or not they reallocate.

**Mutation boundary.** Slice grants no element mutation. Use authorized whole-array access, an exclusive element Place, exclusive enumeration (§14.6.2), or an [exclusive view](#4611-shared-and-exclusive-view-methods). All forms retain ordinary bounds, initialization and Loan checks.

```kimi
var values: [4 of i32] = [10, 20, 30, 40]
values[1] = 25
let count = values.length
let middle: Slice<i32> = values[1..3] // Infer the borrow of values' storage.
```

## 4.6. Indexing and slicing

### 4.6.1. Access and length metadata

The built-in indexing operations apply to `[N of T]`, `Array<T>`, `Slice<T>` and `UniqSlice<T>`. Element indexing accepts a position of any Type satisfying `Position`, and range indexing a range of any Type satisfying `PositionRange` (§4.6.2–§4.6.4). Dictionary indexing takes keys instead. Every single-element index expression, including one on a user Type, resolves through the [Indexable Contracts](#469-indexable-contracts); on these sequence Types a position is first resolved to an `isize` element position. [Raw pointers](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing) keep signed `isize` offsets without safe sequence bounds checks and accept no other position or range Type. String indexing units are not introduced.

| Core | Meaning |
| --- | --- |
| Integers, `FromEnd<T>`, `Start`, `End` | Copy, Owned positions: from the start, from the end, the start boundary and the end boundary; retain no target |
| `Range<S, E>`, `ClosedRange<S, E>` | Copy, Owned half-open and closed intervals of positions; iterable when both boundaries have one integer Type |
| `ResolvedRange` | Copy, Owned half-open interval of positions validated against a length; iterable |
| `Slice<T>{source}` | Copy shared view; retains its backing Origin and shared Loan |
| `UniqSlice<T>{source}` | Non-Copy exclusive view; retains its backing Origin and exclusive Loan (§4.6.11) |

These names are not keywords; `::Kimi.End`, for example, disambiguates a hidden alias. Prefix `^` and range syntax always construct the designated Types from the Kimi Kotonoha, never same-named user Types.

**Diagnostic Type display.** Diagnostics and hover displays qualify these Kimi Types when a user declaration hides their normal alias, for example `Kimi.Start`. A Type mismatch shows the structural differences between the actual and expected Types, including the range shape and boundary Type arguments, elides their Origin bindings (§23.3.6.5), and identifies the expression that does not fit (§23.3.6). A difference only in Origins is not a Type mismatch (§3.8).

When a function call rejects a range because its shape differs from a parameter's concrete range Type, its diagnostic identifies the compared argument and candidate parameter Types. Advice is conditional on the function's required capability: a function that only resolves a range for slicing can accept `R is PositionRange`; enumeration requires the appropriate `Iterable`, `UniqIterable` or `IntoIterable` entry and its Item constraints; boundary access requires the appropriate concrete range Type. A function's intent is not inferred from its name or a rejected call alone. The changed body must be verified before offering an automatic repair (§23.5).

**Length metadata.** Fixed arrays, Array, Slice and UniqSlice provide public read-only `length: isize` and `indices: ResolvedRange`; Array, Slice and UniqSlice also provide `isEmpty: bool`. The receiver is evaluated once and requires ordinary initialization, completeness and access legality. A known fixed length does not remove receiver effects or checks.

| Receiver | Acquisition |
| --- | --- |
| Fixed array | Check shared access and use the Type-level `N` without reading elements |
| Array | Share access during the operation and read its current length |
| Slice | Copy the handle and read its stored length, without reading backing elements |
| UniqSlice | Share the handle during the operation and read its stored length, without reading backing elements |

The returned integers, Booleans and `ResolvedRange` values acquire no receiver or source Origin or Loan; this does not release existing Loans. `x.indices` means `(..).resolve(x.length)`: a snapshot of `[0, L)` at acquisition. Resizing an Array does not update a saved snapshot; later accesses check the length current at that time.

**Element Places.** Binding the receiver and index Types, resolving bounds and checking access produce an element Place. It carries the complete stored Type `T`, its source location and its capabilities: `place ref/T` for reads and `place uniq/T` for updates (§4.6.9, §7.1.1). Forming the Place performs no element Copy or Move. Chained access such as `matrix[1][2] = 10` keeps Places without copying an intermediate inner array.

| Use | Operation after locating the Place |
| --- | --- |
| `values[i] = value` | Ordinary initialization or replacement, with write permissions and Loan checks |
| `values[i]@ref` / `@uniq` | Shared or exclusive borrow of that Place; exclusive access requires write permission |
| `values[i]@move` | Transfer, only through an eligible static Move Path of an owned fixed array |
| Bare value read | Bare acquisition (§3.5): Copy for a proven-Copy element, and a Reborrow for an element storing `uniq/U` or `objuniq/U`, which selects `indexUniq` (§4.6.9); any other Non-Copy element is an error without an expected borrow Type. A bare read never Moves |

Static paths use only the [integer-literal recognition rule](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move); Array elements, runtime indices, positions other than integer literals and paths through borrows never become movable. `@ref` borrows the element Place whatever the index form, and a fixed expected `ref/T` borrows it implicitly (§10.2). An exclusive borrow of an owned element needs `@uniq`. An element that stores a reference or handle is instead adapted under the same table, for example Reborrowed; its slot is not borrowed implicitly. Slice element Places are shared-only (§4.6.6).

```kimi
// resources is an owned fixed array of Non-Copy value Type Resource.
let i: isize = 0
let view = resources[i]@ref   // ref/Resource; the element remains.
inspect(resources[i])         // Shared borrow at a ref/Resource parameter.
// End all required uses of view before the following transfer.
let taken = resources[0]@move // Transfer; resources[0] becomes Moved.

var numbers: [2 of i32] = [10, 20]
let copied = numbers[0]     // Copy; the element remains Initialized.
numbers[0] = 30             // Replace the initialized element.
let alsoCopied = numbers[i] // Copy does not require a static Move Path.
```

### 4.6.2. Positions

A **position** denotes a boundary of a target of length `L` and resolves to an integer in `[0, L]`. An **element position** is a position whose resolved value `q` also satisfies `q < L`; it selects one element. The Types satisfying the closed Contract `Kimi.Position` are exactly the twelve integer Types and the Kimi Types `FromEnd<T>`, `Start` and `End` (§22.1).

```kimi
public contract Position: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<isize>
```

`Position` is a [closed Contract](08-generics-constraints-and-contracts.md#847-intrinsic-and-closed-contracts): no other Type can declare conformance. `T is Position` supplies Equatable and Utf8Format by refinement and Copy and Owned through the Contract's Constraints, and `T is PrimitiveInteger` implies `T is Position` (§8.4.7.3). The integer implementation of `tryResolve` is a Kimi internal function; integers gain no members, and the requirement is called through `P is Position`.

**Resolution.** When `L < 0`, every position fails to resolve. Otherwise:

| Type | Written as | Resolves when | Result |
| --- | --- | --- | --- |
| Integer `n` | An integer value | `0 <= n <= L` | `n` |
| `FromEnd<T>` | `^x`, for `x` of any `T is PrimitiveInteger` | `0 <= offset <= L` | `L - offset` |
| `Start` | An omitted range start | Always | `0` |
| `End` | An omitted range end | Always | `L` |

The conditions compare mathematical values: a wide integer is never truncated before its bound is checked, so a `u64` offset beyond `isize.MaxValue` fails.

```kimi
public struct FromEnd<T>
    T is PrimitiveInteger
    Self is Copy
    Self is Equatable
    Self is Position
    public let offset: T

public struct Start      // End has the same form.
    Self is Copy
    Self is Equatable
    Self is Position
```

- **Meaning.** `^0` and `End` denote the end boundary, and `^1` the last element.
- **Members.** `FromEnd<T>` exposes `offset`. The three Types provide `tryResolve` and `resolve(self: Self, length: isize) -> isize`, and have no public constructor: `^x` constructs a `FromEnd<T>`, and omitted range boundaries construct `Start` and `End`.
- **Construction checks nothing.** `^x` stores `x`; `^(-1)` is a valid value that fails to resolve. Only the evaluation of the operand itself can fail, as an overflow in `^(a - b)`.
- **Equality.** `FromEnd<T>` compares offsets; all `Start` values are equal, and so are all `End` values. `0`, `^0` and `End` have different Types and cannot be compared.
- **No other operations.** The non-integer position Types (`FromEnd<T>`, `Start` and `End`) provide neither Comparable, arithmetic nor implicit conversion; `^n + 1` is `(^n) + 1`, a Type error. Integer positions retain their ordinary integer operations. Prefix `^` produces a storable, passable value outside indexing expressions too; infix `^` remains integer exclusive-or. Write `^(n + 1)` for a compound from-end distance.

A position is a read Type: a reference to a position is read as its value wherever a position is expected (§3.5.3), and a Type argument constrained to `Position` is inferred as the referent Type (§10.2.1). Element positions of every integer Type are accepted, so `values[n]` with `n: u8` needs no conversion.

```kimi
let values: [4 of i32] = [10, 20, 30, 40]
let last = ^1          // FromEnd<i32>
let a = values[last]   // 40
let b = values[2]      // 30; the literal is i32.
let n: u8 = 1
let c = values[n]      // 20
let d = values[^0]     // Aborts if executed: ^0 is the end boundary, not an element.
let e = ^(-1)          // Valid FromEnd<i32>; values[e] Aborts if executed.
```

### 4.6.3. Ranges

A **range** is an interval of positions. Range syntax constructs `Range<S, E>` and `ClosedRange<S, E>`, and resolution against a length produces a `ResolvedRange`. The Types satisfying the closed Contract `Kimi.PositionRange` are exactly these three (§4.6.4).

A range value means the same whether it is stored, passed, iterated or used as an index; `for` and indexing add no special cases. No range Type retains a storage Origin or Loan, implements Comparable or arithmetic, or converts implicitly to another range Type.

#### 4.6.3.1. Syntax and Types

| Syntax | Type | Interval |
| --- | --- | --- |
| `a..b` | `Range<S, E>` | From `a`, excluding `b` |
| `a..=b` | `ClosedRange<S, E>` | From `a`, including `b` |
| `a..` | `Range<S, End>` | From `a` to the target's end |
| `..b` | `Range<Start, E>` | From the target's start, excluding `b` |
| `..=b` | `ClosedRange<Start, E>` | From the target's start, including `b` |
| `..` | `Range<Start, End>` | The entire target |

- **Shape.** The syntax alone fixes the shape: `..` is half-open and `..=` closed. Runtime values, expected Types and use as an index never change it. An omitted boundary is a `Start` or `End` value, and `a..=` is a syntax error.
- **Construction checks nothing.** A range stores its boundaries; a reversed range can be constructed.
- **Boundaries.** Each boundary is a value of a Type satisfying `Position`, read through references by the [value read](03-types-and-values.md#353-value-read). The two boundary Types are independent and need not be equal; one integer Type for both is required only for iteration (§4.6.3.4).
- **Independent boundary Types.** A boundary that is not literal-only (§12.3.1) needs an independently known Type, and the expected Type does not re-infer it. A generic or overloaded call that depends on its context requires an annotation, explicit Type arguments or a typed intermediate Binding (§10.5).

**Literal-only boundaries.** Each literal-only boundary takes its Type from the first applicable rule below; a mismatch is an error and never retries a later rule. `^a` is literal-only when `a` is, and its operand receives the `T` of the `FromEnd<T>` so determined.

1. **Its expected Type:** the `S` or `E` of the range's expected Type.
2. **The other boundary:** the integer Type of the other boundary when that Type is established: the Type itself, or `T` for `FromEnd<T>`.
3. **Default:** `i32`, as for every integer literal. Positions have no separate default, so `0..10`, `^1` and `values[3]` are all `i32`.

A literal position that does not fit `i32` needs an explicit Type, as in `values[3_000_000_000@isize]`.

```kimi
let a = 1..^1                 // Range<i32, FromEnd<i32>>
let b = 2..                   // Range<i32, End>
let c = 0@i32..2@i64          // Range<i32, i64>; resolvable but not iterable.
let n: i64 = 2
let d: Range<i64, u8> = n..10 // 10 is u8 from the expected Type.
let e = ..                    // Range<Start, End>
let f = 0..n@ref              // Value read; Range<i64, i64>.
let g = [0..3, 5..8]          // Array<Range<i32, i32>>
```

**Precedence.** Range operators are non-associative and bind below logical and arithmetic operators but above assignment. Prefix `^` has ordinary prefix precedence. `a..b..c` is rejected syntactically, and `(a..b)..c` by its boundary Type.

| Expression | Interpretation |
| --- | --- |
| `a + 1..b * 2` | `(a + 1)..(b * 2)` |
| `^n + 1` | `(^n) + 1`; a Type error because `FromEnd<T>` has no addition |
| `^(n + 1)` | From-end distance `n + 1` |
| `0..^1` | `0..(^1)` |
| `a ^ b..c` | `(a ^ b)..c`; integer exclusive-or first |

#### 4.6.3.2. `Range<S, E>` and `ClosedRange<S, E>`

```kimi
public struct Range<S, E>     // ClosedRange<S, E> has the same form.
    S is Position
    E is Position
    Self is Copy
    Self is Equatable
    Self is PositionRange
    public let start: S
    public let end: E
```

- Owned, Copy and Equatable, and they satisfy `PositionRange`.
- They store only the public read-only `start: S` and `end: E`; `Start` and `End` are zero-sized.
- Public members are `start`, `end`, `tryResolve` and `resolve` (§4.6.4), and the iteration entries (§4.6.3.4).
- Constructed only by range syntax, with no public `init`, factory or `tryCreate`.
- Equality compares boundaries of the same Types. Ranges of different shapes have different Types and cannot be compared: `(0..3) == (0..=2)` is a Type error.
- There is no `length` or `isEmpty`: boundaries may be relative, the element count may fit no integer Type, and a value may be reversed.

#### 4.6.3.3. `ResolvedRange`

`ResolvedRange` is `Range<isize, isize>` with the invariant `0 <= start <= end`, as a distinct Type.

- **Same meaning.** Resolution, iteration, formatting and equality are those of the `Range<isize, isize>` with the same boundaries. No implicit conversion exists in either direction.
- **Consequences of the invariant.** Its resolution checks only `end <= L`, and its iteration entries check no order.
- **Members.** Owned, Copy and Equatable by `start` and `end`, with public read-only `start: isize`, `end: isize`, `length: isize = end - start` and `isEmpty: bool`, `tryResolve` and `resolve` (§4.6.4) and the iteration entries (§4.6.3.4).
- **Production.** Only sequence `indices` (§4.6.1) and the `resolve` and `tryResolve` of the range Types produce it. No public constructor or setter bypasses validation.
- **Application.** It is a numeric interval that names no target, so it applies to any target that is long enough; applying it checks the current length (§4.6.4).

#### 4.6.3.4. Iteration

| Range | Iteration condition | Iterator | Values |
| --- | --- | --- | --- |
| `Range<S, E>` | `S is PrimitiveInteger` and `E is S` | `RangeIterator<S>` | `[start, end)` |
| `ClosedRange<S, E>` | `S is PrimitiveInteger` and `E is S` | `ClosedRangeIterator<S>` | `[start, end]` |
| `ResolvedRange` | None | `RangeIterator<isize>` | Those of the `Range<isize, isize>` with the same boundaries |

- **Conformance.** Each range conforms to `Iterable`, `UniqIterable` and `IntoIterable` under its condition by conditional conformance (§8.4.8); `E is S` is a Type-identity requirement (§8.3). A range with an omitted boundary or a `FromEnd<T>` boundary, or with two different integer Types, does not satisfy the condition and is not iterable.
- **Rejection diagnostics.** The explanation names the entry selected by the Subject mode and the boundary Types that prevent iteration. For `FromEnd`, `Start` or `End` boundaries, Advice suggests resolving against a sequence length, such as `r.resolve(values.length)`. For different integer boundary Types, it suggests explicit conversion to the same integer Type. These suggestions do not silently resolve a range or change its Type, and an automatic repair is offered only when the changed body is verified (§23.5).
- **Entries.** An entry copies the boundaries and keeps no Storage, Origin or Loan of the source. The borrowing entries' `IteratorType(source)` is the same Type for every `source`. An entry initiates Abort when `start > end`; a reversed range is neither empty nor descending.
- **Values.** Values are produced from `start` upward in unit steps. `ClosedRangeIterator<T>` produces `end` last and never computes past it, including at the maximum of `T`. Reassigning a `for var` binding does not change the sequence. Acquisition, borrowing and cleanup follow §14.6.2.
- **Iterators.** `RangeIterator<T>` and `ClosedRangeIterator<T>` are Owned, Non-Copy Iterators whose `Item` is `T`; `next(self: uniq/Self)` returns `Option<T>`. They stay exhausted after `None` and satisfy the [Iterator effect bound](22-core-execution-and-foreign-functions.md#22124-iterator-independence). They have no public constructor and no entry conformance; enumerate an iterator value through `Kimi.Iteration.owning` or `borrowing` (§22.1.2.3). Their representation is unspecified.
- Infinite, descending and stepped ranges are unavailable (Appendix D).

```kimi
let values: [5 of i32] = [10, 20, 30, 40, 50]
let inner = 1..^1
for i in 0..values.length             // Range<isize, isize>
    Console.writeLine("\(values[i])")
for var number in -3..3               // Range<i32, i32>: -3 through 2.
    number += 10                      // Changes only the binding.
for x in 0..=255@u8                   // ClosedRangeIterator<u8>; no addition after 255.
    ()
for i in inner.resolve(values.length) // ResolvedRange: 1, 2, 3.
    ()
for i in inner                        // Error: a range with a FromEnd boundary is not iterable.
    ()

func sum<T>(values: Range<T, T>) -> T
    T is PrimitiveInteger
    var total: T = 0
    for value in values               // T is T holds, so the range is iterable.
        total += value
    return total

func makeNumbers() -> RangeIterator<i32>
    let numbers = 0..3
    return numbers.iterate()          // Keeps no borrow of numbers.

for number in Kimi.Iteration.owning(makeNumbers())
    Console.writeLine("Count \(number)")
```

### 4.6.4. Resolution, evaluation, and failure

```kimi
public contract PositionRange: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>
```

`PositionRange` is a closed Contract satisfied exactly by `Range<S, E>`, `ClosedRange<S, E>` and `ResolvedRange`, with the same implications as `Position` (§4.6.2). Resolution reads only the boundaries and the length, so an implementation may fuse or remove its calls and checks.

**Range resolution.** Let `s` and `e` be the resolved start and end positions (§4.6.2), and `q` the end resolved as an element position.

| Type | Check | Result |
| --- | --- | --- |
| `Range<S, E>` | `s <= e` | `[s, e)` |
| `ClosedRange<S, E>` | `s <= q` | `[s, q + 1)` |
| `ResolvedRange` | `end <= L` | The same interval |

- **Failure.** Resolution fails when `L < 0` or when any position fails to resolve.
- **Order.** The resolved boundaries are compared once. Resolution never clamps and never turns a reversed interval into an empty one. A half-open range whose start is `Start` or whose end is `End` always passes the order check.
- **No overflow.** `q < L <= isize.MaxValue`, so `q + 1` cannot overflow.

```kimi
(1..^1).tryResolve(5)   // Some(1..4)
(1..=3).tryResolve(5)   // Some(1..4)
(2..).tryResolve(5)     // Some(2..5); End resolves to 5.
(2..=1).tryResolve(5)   // None: reversed.
(..=^0).tryResolve(5)   // None: ^0 is not an element position.
(^7..).tryResolve(5)    // None: 7 > 5.
let r = (1..^1).resolve(5)
r.tryResolve(3)         // None: the end 4 exceeds the length 3.
```

**Aborting operations.** The Contracts require only `tryResolve`. Every aborting operation that takes a position or range, such as indexing, `insert`, `remove` and `resolve`, performs the same resolution and initiates Abort when it yields `None`. The position and range Types other than the integers publish `resolve(self: Self, length: isize)` as the naming pair of `tryResolve` (§4.7.1).

**Slicing.** For a target of current length `L`, `x[r]` applies the interval of `r.tryResolve(L)` and Aborts when there is none, and `x.trySlice(r)` returns `None` instead (§4.6.6). An implementation may fuse resolution and application without materializing a `ResolvedRange`. `values[..]` and `values[L..]` also apply to empty arrays; `values[L..L]` and `values[^0..]` are empty; element position `L` or `^0`, and a closed end of `^0`, are invalid.

```kimi
let r = 2..5
let shortArray: [3 of i32] = [1, 2, 3]
let checked = shortArray.trySlice(r) // None.
let failed = shortArray[r]         // Abort if executed.
```

**Failure.** Ordinary element and range indexing initiates Abort on invalid bounds; use the `tryGet`, `trySlice` and `trySplitAt` operations for expected input failures. A try-prefixed API converts only its own length or bounds failure to `None`, not failures in argument evaluation or in other operations; a successful try-prefixed API returns `Some`. Because construction checks nothing, a negative position fails at its use. With `n < 0` and a Slice `s`:

| Operation | Result |
| --- | --- |
| `let p = ^n`, `let r = n..` | Succeeds; construction checks nothing |
| `values[n..]`, `values[^n..]`, `values[^n]`, `values[2..=1]` | Abort in resolution |
| `s.trySlice(n..)`, `s.tryGet(^n)`, `s.trySlice(2..=1)`, `s.trySplitAt(^n)` | `None` |

Integers from external input are passed in their own Type:

```kimi
func tryWindow<T, I>(values: Slice<T> ! start: I, end: I) -> Option<Slice<T> during values.source>
    I is PrimitiveInteger
    return values.trySlice(start..end) // None for negative, reversed or out-of-range boundaries.
```

**Evaluation order.** The receiver and index are each evaluated once under the [evaluation order](12-expressions.md#122-evaluation-order). A range expression evaluates its written boundaries, start before end; an omitted boundary evaluates nothing. A failure inside a boundary expression stops subsequent evaluation, and neither `^x` nor range construction checks anything afterwards. Three independent examples:

```kimi
func sideEffect() -> isize
    return 2 // Represents an observable effect.

let a = (-1)..sideEffect()   // Range<isize, isize>; construction succeeds.
let b = ^(-1)..sideEffect()  // Range<FromEnd<isize>, isize>; construction succeeds too.
let c = values[(-1)..]       // Evaluates the receiver and the boundary, then Aborts in resolution.
```

**Receivers.** The built-in access receiver is located once, whatever its form and whether the range is written directly or saved: an existing Place is used where it is, and only an owned temporary is materialized in a Temporary Place. Direct array element access neither copies the array into a temporary nor borrows it as a whole, so a large array is not copied and the remaining elements after a Partial Move stay directly accessible. While its index is evaluated, modification, destruction, Move and reallocation of that storage are prohibited; shared reads remain allowed, as in `values[values.length - 1]`. For a chained element Copy, the located root is protected through all index evaluations and the final element Copy, and that Copy is acquired before later surrounding operands are evaluated; intermediate arrays are not copied. A temporary receiver keeps its ordinary enclosing-expression lifetime. A write establishes its exclusive Loan after resolving bounds and checks existing Loans. The receiver and boundaries are never reevaluated. For a Slice, the handle is copied first; reassigning the original handle does not change the acquired view. UniqSlice instead borrows its handle in the mode required by §4.6.11; it never copies the owning handle.

**Writes and updates.** [Simple assignment](13-operators-and-assignment.md#1371-simple-assignment) and [compound assignment](13-operators-and-assignment.md#1372-compound-assignment) secure their right-hand side before locating the indexed target; compound assignment then checks bounds, reads the old value, computes and writes back, once each. Increment and decrement use the same target and Loan rules. An arithmetic failure prevents writeback. The right-hand side runs before the target Loan begins; any Loans retained by its secured result must be compatible with the later target access. The exclusive Loan of an element write lasts from bounds resolution through old-value destruction and placement. [Exchange operations](15-ownership-and-lifetime-analysis.md#157-whole-value-updates) evaluate their arguments left to right and reserve each target under §15.6.7 before activating all targets at entry; `Kimi.Intrinsics.swap` requires static non-overlap, not merely a runtime `i != j`.

The common [Abort and constant-evaluation rules](17-failure-handling.md#1734-checks-builds-and-constant-evaluation) apply. Syntax, Type, literal-fitting and required constant-evaluation violations are compile-time errors. An ordinary out-of-bounds `a[10]` on a three-element array instead aborts if executed; a position or range that fails for every length, or for a fixed array's length, is warned about (§17.4), but optimization must not turn such a runtime failure into language-level rejection. Rejection of an ineligible static Move Path is a separate rule.

### 4.6.5. Slice storage, lifetime, and permissions

`Slice<T>{source}` shares an initialized contiguous region of complete element Type `T`. Formation requires the ordinary initialization and access checks and cannot hide Uninitialized or partially Moved storage. Its runtime length is not part of Type identity. Every range access returns a Slice, including constant-length ranges, without copying elements into a fixed array.

Slice indices start at zero; from-end positions and reslicing use the current Slice length, and the original array indices are not retained. Slicing the rows of a nested fixed array produces `Slice<[N of T]>` without flattening. Element inheritance or matching layout does not permit conversion between Slices of different element Types.

**Origins and Loans.** The Origin records the backing lifetime; the Loan records the conflicting Places and permissions. Handle copies, reslices, iterators and references to element slots retain both. They need not borrow the handle variable itself: acquired views remain usable after that variable ends, as long as the backing storage and inherited Loan remain valid.

Under [static Place analysis](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), an array-derived Slice borrows the whole array Place. Reslicing, `splitAt` and empty Slices keep that Loan's conflict footprint; runtime interval resolution neither narrows it nor proves disjointness. Thus a later use of `empty` from `let empty = values[1..1]` conflicts with `values[3] = 10` because of the retained shared Loan, not the Origin alone. The Loan ends after all required derived uses end. Moving, destroying or reallocating the source is forbidden while it would invalidate an active view.

A Slice owns no elements; copying or destroying the handle neither copies nor destroys them. `T` need not be Owned, and all dependencies inside `T` remain intact. `source` may be shortened but never lengthened; the Slice's own Owned classification follows [OwnedOrigins](15-ownership-and-lifetime-analysis.md#1523-static-and-owned), including `source` and `T`.

**Storage and escape.** A Slice may be kept in local aggregates, Array elements and concrete object payloads under [ordinary storage](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision), preserving its backing Loan and nested element dependencies. Static storage requires Owned and a valid static source under the [static storage rules](11-properties.md#1132-static-storage). Copying or storing a Slice never extends the backing lifetime.

Slicing a temporary never extends its [lifetime](03-types-and-values.md#36-temporary-values-places-and-lifetimes). An unused binding is not rejected solely because it holds a borrow of a temporary; only a later use, return or retention that requires the expired dependency is rejected.

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

A `var` Slice permits only handle reassignment, and `uniq/Slice<T>` exclusively borrows the handle, not its elements. Exclusive element access uses the distinct UniqSlice Type (§4.6.11). Implicit conversion to owning arrays, safe raw-pointer construction, Slice equality and implicit elementwise comparison are not introduced.

### 4.6.6. Slice operations and element results

For `s: Slice<T>`, members receive and Copy the handle by value. Element and partial-Slice results retain `s.source` rather than borrowing the handle variable used in the call. All listed operations are public.

| Operation | Result and conditions |
| --- | --- |
| `s.length: isize` / `s.isEmpty: bool` | Read-only count / whether the count is zero |
| `s.indices: ResolvedRange` | Read-only snapshot under the metadata rules |
| `s[index]` | The element Place `place ref/T during s.source`; `index` is a position of any Type (§4.6.9) |
| `s[range]` | `Slice<T>` retaining `s.source`; `range` is a range of any Type, resolved against the current length (§4.6.4) |
| `s.slice()`, `s.slice(range)` | Shared views under §4.6.11; retain `source` |
| `s.toArray()`, `T is Copy` | Independent owning storage under §4.6.6.1 |
| `s.tryGet(index)` | `tryGet<P>(self: Self, index: P) -> Option<ref/T during source>` with `P is Position`; `None` unless `index` resolves to an element position |
| `s.trySlice(range)` | `trySlice<R>(self: Self, range: R) -> Option<Slice<T> during source>` with `R is PositionRange`; `None` when `range` does not resolve |
| `s.splitAt(index)` | `splitAt<P>(self: Self, index: P) -> (Slice<T> during source, Slice<T> during source)` with `P is Position`; resolves `index` once to `p` and returns `(s[..p], s[p..])` |
| `s.trySplitAt(index)` | `Option` of that Tuple; `None` when `index` does not resolve |
| `s.contains(value)`, `T is Equatable` | `bool`; whether some element equals `value: ref/T` |
| `s.firstIndex(of: value)`, `T is Equatable` | `Option<isize>`; the first index whose element equals `value: ref/T` |
| `s.firstIndex(matching: f)` | `Option<isize>`; the first index for which `f`, with `F is Callable<(ref/T) -> bool>`, returns `true` |

The search operations visit elements in increasing index order, stop at the first match and neither Copy nor Move elements; each comparison or call receives a shared reference to the element. Positions and ranges are passed by value and read from references (§3.5.3). Both split operations accept the boundaries zero and length; invalid boundaries abort for `splitAt` and return `None` for `trySplitAt`. `tryGet` and `trySlice` are the try-prefixed forms of `s[k]` for a position and for a range. They have different names because Constraints alone never distinguish overloads (§9.1). `tryGet` deliberately has a fixed reference result, independent of `T`'s Copy capability.

Fixed arrays and Array provide `tryGet`, `trySlice`, `splitAt` and `trySplitAt` with the meaning of the same operation on their whole-range Slice `x[..]`; their results depend on the receiver's shared borrow (`during self`) instead of a Slice source.

A Slice element Place follows the element Place rules of §4.6.1 but is shared-only: writes, Moves and exclusive borrows through a Slice are rejected. No result Type depends on Copy: for an unknown `T`, a by-value `s[0]` needs `T is Copy`, while `s[0]@ref` works for every `T`.

```kimi
func first<T>(s: Slice<T>) -> T
    T is Copy
    return s[0]

func firstRef<T>(s: Slice<T>) -> ref/T during s.source
    return s[0]@ref // Borrow the slot regardless of T's Copy capability.

func head<T, E>(s: Slice<Result<T, E>>) -> ref/Result<T, E> during s.source
    return s[0]@ref // A bare s[0] borrows the same slot at this expected ref Type (§10.2).

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

func tryTail<T>(values: Slice<T>) -> Option<Slice<T> during values.source>
    return values.trySlice(1..) // None for an empty Slice; no static length condition.
```

```kimi
var values: [3 of i32] = [1, 2, 3]
let shared = values[..]
values[0] = 10 // Error: conflicts with a subsequent use of the shared Loan.
let first = shared[0]
shared[0] = 20 // Error: Slice elements are read-only.
```

#### 4.6.6.1. Copying into an owning Array

`toArray(self: Self) -> Array<T>` requires `T is Copy`. Copy each element exactly once, in index order, into independent element storage with the same length. This is neither deep cloning nor implicit ownership transfer. The result need not retain the Slice's outer borrow, but keeps every dependency inside `T`. It reads all elements, including the programmer obligations of §4.3.4.

The operation is O(n). Secure capacity at least equal to the length before copying; do not grow storage during construction. There is at most one allocation for element storage, and none for an empty Slice. Allocation failure follows the ordinary Array rules. Capacity construction and `appendCopies` may share this implementation; bulk copies are permitted only when they preserve Copy, Origin and Loan semantics.

```kimi
var values: [3 of i32] = [1, 2, 3]
let copy = values.slice(1..).toArray()
values[1] = 20 // The outer Slice Loan is no longer needed by copy.
require copy[0] == 2 else => $abort("independent storage")
```

### 4.6.7. Slice iteration and nested Origins

`Slice` conforms to `Iterable`, `UniqIterable` and `IntoIterable`. In every mode, including for Copy elements, the item is `ref/T during source`, in index order (§14.6.2). The iterator holds a handle and a position, not owned elements, so exclusive enumeration lends only the handle and the elements stay shared. Items borrow the backing slots, not the iterator's receiver or Storage, so they may be retained across later `next` calls. The iterator is a standard Iterator (§22.1.2.3–4).

Element-internal Origins are kept separate from slot-borrow Origins:

```kimi
let data: i32 = 10
let refs: [1 of ref/i32] = [data@ref]
let s: Slice<ref/i32> = refs[..] // Infer inner and backing Origins separately.
let value = s[0]       // Copy the inner reference to data.
let slot = s.tryGet(0) // Option containing a shared borrow of refs[0].
for element in s
    () // element: ref/(ref/i32); it shares a slot containing the inner reference.
```

The outer references from `tryGet` and iteration borrow the slots of `refs`, while the inner reference depends on `data`. The inner Origin must stay valid while the outer reference is used. A copied inner reference keeps its own Origin without gaining a new slot borrow.

### 4.6.8. Representation and performance

Construction, resolution and Copy of positions and ranges, and creation, Copy, reslicing, splitting and address calculation of Slices, take O(1) time in the element count and require no additional element storage, heap allocation or reference-count update. Forming or forwarding an element Place is O(1) and requires no heap allocation, element Copy, temporary element storage or reference-count update of its own. Searches, projection arithmetic, the actual acquisition, including any element Copy, and user code are charged separately.

For a fixed Type binding, let `n` be the number of live elements or entries at the start of an enumeration. The standard iterators of borrowed Array, fixed arrays and Dictionary, of Slice and of the ranges satisfy:

| Operation or resource | Bound |
| --- | --- |
| Creation, destruction, and `next` on an empty or exhausted iterator | O(1) |
| `next` of Array, fixed array, Slice, `RangeIterator<T>` and `ClosedRangeIterator<T>` | O(1) each |
| `next` of Dictionary | Amortized O(1) |
| Enumeration up to the first `None` | O(1 + n) |
| Additional iterator storage | O(1), with no heap allocation or reference-count update |

For a Dictionary, `n` is the number of live entries, not the capacity: no capacity scan or element-reference array is built at creation, and sparse Dictionaries keep the bound. Owning iterators obey the same bounds for their traversal management; holding and transferring the source storage, transferring and cleaning up elements, and the results and body the user retains are charged separately. User iterators have no required complexity. Iteration never materializes an array of iteration values first.

A Slice or UniqSlice's semantic representation keeps the backing-element location or equivalent provenance, a nonnegative `isize` length, and static Origins and Loans. Element spacing is `stride(T)`. Empty views and zero-sized elements keep source provenance. No universal pointer-plus-length ABI, runtime lifetime tag or pointer to a disappearing handle variable is required. Implementations use logical counts and positions rather than subtracting element pointers to recover a length, and never form invalid pointers before checking.

Positions and ranges store only their boundaries: `Start` and `End` are zero-sized, and the direction of a position and the shape of a range are static, so resolution branches on neither. The half-open range iterator keeps no end flag.

Checks may be eliminated, shared or hoisted out of loops only when safety is proven without changing effects, Abort behavior or borrow legality; the range information established by a successful `next` may be reused within a region whose length and storage are proven unchanged. Eliminating a bounds check needs only a proof that the interval's end does not exceed the current length, as for `indices` and `resolve` results. Reusing an address or element reference also needs a separate proof that the Storage, borrow and address remain valid; an address from before a reallocation is never used. These proofs are internal to the implementation, and the public Types carry no owner information. Constant-folding position and range operations does not extend the literal-only static Move Path rule. Standard `iterate`, `next` and adapter calls may be inlined, an Option payload may be delivered directly into its binding (§14.6.2), and a materialized `ref/Key` temporary may be elided only under the address-observation, escape and ABI conditions of §21.5.5; none of these is a language acceptance condition, and a user iterator's `next`, `None` test and cleanup are never omitted merely because the Type is an Iterator.

### 4.6.9. Indexable contracts

Single-element indexing is the [Kimi Contract](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) family below. `Key` and `Element` are complete Types. A search key is shared-borrowed and never consumed, and small keys need no heap allocation. A key whose Type is a reference or object handle is written `key@ref`, because §10.2 never borrows such a slot implicitly.

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key)
        -> place ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key)
        -> place uniq/Element during self
```

Both requirements share one `Element`, and the same key selects the same element. `UniqIndexable` grants exclusive access to the element Place; it does not change the stored Type to `uniq/Element`. An index expression `receiver[key]` is resolved statically in this order, without changing runtime evaluation:

1. Determine the `Indexable<Key>` conformance and `Element` from the receiver, after [reference-path selection](03-types-and-values.md#341-reference-path-selection), and the key Type; `Key` is never inferred from the expected result.
2. Determine the capability that the final acquisition plan of the use requires: its bare acquisition (§3.5), including the Reborrow of an element's stored exclusive reference, its adaptation at a fixed expected Type (§10.2), a write, an explicit borrow, a Subject (§15.1.6) or a Receiver Expression (§7.3).
3. Select `index` for reads and shared borrows, and `indexUniq` of the same conformance for updates, exclusive borrows and exclusive Reborrows; the latter requires `UniqIndexable<Key>` and checks the path, initialization and Loans.

The receiver acquisition of a selected `indexUniq` belongs to this Place selection (§3.4.1) and needs no `@uniq` on the receiver. Being synthesized grants it no authority: it uses only the exclusive capability that the receiver's path already has, so a `let`-owned collection or a shared path cannot supply it (§15.1.5).

```kimi
// refs: a writable Array<uniq/Node>.
normalize(refs[0]) // Parameter uniq/Node: indexUniq selects the slot and the stored reference is Reborrowed.
let first = refs[0] // The same selection and Reborrow without an expected Type (§3.5).
```

A shared path cannot satisfy an exclusive requirement. Ordinary functions and getters keep their declared result modes, and a capability or Loan failure never reselects a mode, a candidate or a layer. The receiver and key are evaluated once each in the ordinary call order; assignment evaluates its right-hand side first (§13.7). Every input, including the key, stays Loaned during the call. The result depends on the receiver under its contract; a result that depends on the key's borrow does not satisfy the contract.

**Standard indexing.** `a` is the receiver borrow Origin and `s` the Slice's external source; element-internal Origins are kept separately.

| Target and operation | Input | Result | Depends on |
| --- | --- | --- | --- |
| `Array<E>`, `[N of E]` element | A position, resolved to an element position `q: isize` | `place ref/E` for reads, `place uniq/E` for updates | `a` |
| `Dictionary<K, V>` element | `K` as `ref/K` | `place ref/V` for reads, `place uniq/V` for updates; absence Aborts | `a`, never the key |
| `UniqSlice<E>` element | A position, resolved to `q: isize` | `place ref/E` or `place uniq/E` under the path capability | `a` |
| `Slice<E>` element | A position, resolved to an element position `q: isize` | `place ref/E during s.source` | `s` |
| `Array<E>`, `[N of E]` range | A range, by value | An ordinary `Slice<E>` | The element storage |
| `UniqSlice<E>` range | A range, by value | An ordinary shared `Slice<E>` | `a` and the original source |
| `Slice<E>` range | A range, by value | An ordinary `Slice<E>` | `s` |

Array, fixed arrays and UniqSlice conform to `UniqIndexable<isize>`, Dictionary to `UniqIndexable<K>`, and Slice to `Indexable<isize>`; conformances distinguished by Type arguments and their associated Types are identified under §8.4.9. Out-of-range indices (§4.6.4) and absent keys (§4.7.3) Abort; the try-prefixed operations return `Option` values instead. The Slice implementation publishes `during self.source`, which is stronger than the required `during self`; a generic `S is Indexable<Key>` assumes only the requirement. Range indexing is not part of the Indexable family: it forms a Slice, whose temporary storage is not a Place inside the collection, and no whole-range assignment exists (§4.6.5).

**Position normalization.** On the concrete sequence Types, `x[k]` with `k` of a Type satisfying `Position` resolves `k` to an element position `q` and performs the `Indexable<isize>` indexing `x[q]`; a failed resolution Aborts. The element Place, its capabilities and the choice of `index` or `indexUniq` follow the rules above, and the normalization check and the `Indexable<isize>` bounds check are one check. The key is read as its value (§3.5.3) and resolved before the element access, so it keeps no borrow: `indexes[indexes[0]] = x` and `indexes.remove(indexes[0])` are valid. Normalization applies only to concrete sequence Types: a generic `S is Indexable<Key>` and every other Type select `Indexable<Key>` by the key's own Type, so `s[^1]` or an `i32` key is unavailable through `S is Indexable<isize>`.

A direct fixed-array element is a built-in projection selected with the same inputs and rules, not a call that borrows the whole array. With an in-range integer literal index it keeps its Take capability (§15.1.5), so a Partial Move, later use of the remaining elements and reinitialization of the missing element are possible. Abstract Indexable conformances and ordinary Place results publish neither Take nor Uninitialized Storage. Field access keeps the standard `get`/`set` permissions of §11.1.

### 4.6.10. Disjoint exclusive element pairs

`Array<E>` and `[N of E]` provide `tryGetPairUniq<P, Q>(self: uniq/Self ! first: P, second: Q) -> Option<(uniq/E during self, uniq/E during self)>`, where `P is Position` and `Q is Position`. Both arguments follow ordinary call evaluation and acquisition. The operation resolves the positions against the entry length and returns `None` if either is invalid, is the end boundary, or resolves to the same element as the other. Otherwise it returns two exclusive references in argument order, each to the selected element. Equality and disjointness are about logical element positions, including when zero-sized elements share an address.

The operation splits the receiver's exclusive capability through the standard storage boundary (§22.1.2.5). The references may be used independently and keep the source collection borrowed under the ordinary Loan rules until their last uses. No conflicting whole-collection access, reallocation, destruction or Move is permitted while either reference remains live. The result keeps element-internal dependencies as well as its source dependency. The operation takes O(1) time, allocates nothing, changes no element, length, capacity or order, and calls no element copy, comparison or destructor.

This API adds no inference from runtime inequalities to ordinary indexing: two separately formed exclusive element borrows still require the specified static non-overlap proof (§15.6.2). Shared receivers gain no new capability.

### 4.6.11. Shared and exclusive view methods

Every `slice` / `sliceUniq` entry has a no-argument whole-view overload and a range overload with `range: R`, `R is PositionRange`. `Self` below is the providing Type; a fixed-array member is generic over its element and length.

| Provider | Shared operation | Exclusive operation |
| --- | --- | --- |
| `[N of T]`, `Array<T>` | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |
| `Slice<T>{source}` | `slice(self: Self) -> Slice<T> during source` | None |
| `UniqSlice<T>{source}` | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |

Range overloads keep the same receiver and result contracts. Evaluate the receiver and range once each in ordinary call order, resolve against the current view length, and Abort on invalid bounds (§4.6.4). Range subscripting always forms a shared Slice: `x[r]` has the meaning of `x.slice(r)`. A view's indices start at zero, reslicing is relative to that view, and nested arrays are never flattened.

#### 4.6.11.1. `UniqSlice` operations and acquisition

`UniqSlice<T>{source}` is an ordinary Non-Copy value that exclusively borrows a completed contiguous array region. It owns no elements and destroys none when dropped. Neither Copy nor Owned is required of `T`; preserve all Origins and Loans inside its complete Type. Arrays declared with `noinit` are completed arrays under §4.3.4.

| Member or operation | Contract |
| --- | --- |
| `length: isize`, `isEmpty: bool`, `indices: ResolvedRange` | Read-only metadata, without reading elements |
| `index(self: ref/Self, key: ref/isize) -> place ref/T during self` | The shared `Indexable<isize>` entry |
| `indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/T during self` | The `UniqIndexable<isize>` entry |
| `view[position]` | Position normalization and capability selection under §4.6.9 |
| `view[range]`, `view.slice(...)`, `view.sliceUniq(...)` | The view methods above |

An element may be read, replaced or borrowed with the required authority; it cannot be Moved out leaving a hole. A named owning local needs `var` for exclusive operations. A `let` storing `uniq/UniqSlice<T>` still permits exclusive access through that reference under the ordinary rules; any shared path denies it. Assignment through a temporary exclusive view in the same expression is permitted, without extending a temporary's lifetime.

Owning UniqSlice values gain no implicit Copy, Move, Reborrow or conversion to Slice. Write `view@move` to transfer the value, `view.sliceUniq(...)` for a child exclusive view, and `view.slice(...)` for a shared view. Method receivers use ordinary receiver borrowing. Empty views remain Non-Copy and retain their Loan.

```kimi
var values: [3 of i32] = [1, 2, 3]
values.sliceUniq(0..3)[0] = 10
var view = values.sliceUniq()
let reference = view@uniq
reference[0] = 20 // The reference is immutable; its referent remains exclusively accessible.
// let frozen = values.sliceUniq()
// frozen[0] = 30 // Error: a named owning let supplies no exclusive receiver.
// values[0..3][0] = 40 // Error: range subscripting forms a shared view.
```

#### 4.6.11.2. Dependency and cost guarantees

The complete `T` is invariant. `source` may be shortened under ordinary borrow rules, never lengthened, without duplicating exclusive authority. A child view depends on the original storage and the current borrow of its parent handle. While that borrow is needed, conflicting parent uses are prohibited. Creating a shared child neither releases nor weakens the original exclusive array Loan; writes remain prohibited while that child is live.

An array-derived view borrows the whole array. Different ranges or runtime inequalities do not prove disjointness. Conflicting mutation, reallocation, Move and destruction of the source remain forbidden. Views of temporary arrays retain only the ordinary expression lifetime. Stored views preserve their dependencies under §15.4; they do not extend the source lifetime.

Formation and reslicing take O(1) time, perform no element copies and allocate no heap storage, including for empty and zero-sized-element views. Representation follows §4.6.8. UniqSlice has no iteration conformance, dedicated disjoint split, new try-prefixed operations or public arbitrary-pointer constructor in this profile. Existing APIs on the other sequence Types remain available. It has no `toArray` method: use `view.slice().toArray()`. No `toSlice`, `toUniqSlice` or `toSliceUniq` aliases are provided.

## 4.7. Dynamic collection mutation

### 4.7.1. Common acquisition and outcomes

`Array<T>` and `Dictionary<K, V>` are Non-Copy owning collections. Like Array (§4.5), Dictionary accepts valid complete stored Types without blanket Copy or Owned constraints. Dictionary requires `K is Equatable` and the key stability of §12.3.4, not a public Hash constraint. Types and Origins are fixed at the declaration; mutation never restarts inference.

The operations below are public instance APIs. Mutations use `self: uniq/Self` unless stated otherwise. A collection in receiver position is therefore acquired exclusively without a spelling (`values.append(...)`, [§7.3](07-functions-and-callable-values.md#73-explicit-receivers)), whether it is directly owned or reached through an exclusive reference, provided its lending point is exclusively writable (§15.1.5). Passing an owned collection Place to a `uniq` parameter still needs `@uniq` (§10.2). Value parameters are acquired once under the ordinary acquisition rules (§3.5), without deep cloning or implicit count increments. A rejected Move is not restored; returned inputs can be recovered from a `Result`. Removal transfers the stored responsibility, even for Copy elements. Owning a reference value neither owns nor extends its referent's lifetime.

Precondition failures Abort. Ordinary absence uses `Option`, and recoverable rejection that returns its inputs uses `Result`. A try-prefixed API name promises only its specified recoverable outcome, not propagation by a try expression (§17.2.4); a corresponding Abort API need not exist. A discarded `Result` follows the normal warning rule.

| Normal outcome | Postcondition |
| --- | --- |
| Add | Length increases; prior relative order is preserved; capacity grows if needed |
| Replace | Only the value changes; the stored key, order, length and capacity are preserved |
| Remove | Length decreases; remaining order and capacity are preserved |
| Clear | All elements are destroyed; length becomes zero; capacity is preserved |
| Reserve / shrink | Values, order and length are preserved; capacity and placement change only as specified |
| Lookup, absence or duplicate rejection | Values, order, length and capacity are preserved |

These postconditions do not roll back external effects of arguments, equality or destructors.

**Naming convention.** Because the functions of one Name share one receiver shape (§7.3), acquire corresponding parameters of overlapping Types in one mode (§7.3.1) and an accessor's receiver is fixed by its operation (§11.2), a shared variant and an exclusive variant of one operation need different names, whether the difference lies in the receiver or in an argument (`borrowStorage`/`borrowStorageUniq`), and so do a Property and the exclusive or consuming operation over the same state. Kimi declarations and the examples of this specification follow the convention below; user code is not required to.

| Pair | Convention | Example |
| --- | --- | --- |
| Returns a new value / changes in place | Adjective (past participle) / verb | `sorted` / `sort` |
| Returns a shared reference / returns an exclusive reference | Suffix `Uniq` on the exclusive variant | `tryGet` / `tryGetUniq` |
| Only inspects / advances or takes | Different verbs; against a Property, verb + noun | `peek` / `next`, `reading` / `nextReading` |
| Observes a value through a Property / consumes the receiver to take that value | Noun Property / `into` + noun function | `item` / `intoItem` |

### 4.7.2. Array operations

| Operation | Behavior |
| --- | --- |
| `append(value: T) -> ()` | Add at the end |
| `insert<P>(index: P, value: T) -> ()`, `P is Position` | Insert before the resolved boundary |
| `pop() -> Option<T>` | Remove and return the last element, or `None` when empty |
| `remove<P>(index: P) -> T`, `P is Position` | Remove and return the element at the resolved element position |
| `clear() -> ()` | Destroy all elements in the order of §4.7.6 |

The position is passed by value and resolved once in the body against the entry length `L` (§4.6.2): `insert` requires a boundary and `remove` an element position; invalid positions Abort. There is no range overload.

`insert(^0, value)` appends, including to an empty Array. On a nonempty Array, `insert(^1, value)` inserts before the last element and `remove(^1)` removes it; `remove(^0)` is always invalid. A failure evaluating an argument prevents later argument evaluation. Unlike `remove`, indexed reading never Moves a dynamic element (§4.6.1), and indexed assignment destroys the old value.

```kimi
var values: Array<i32> = []
values.reserve(additional: 3)  // The receiver is acquired exclusively without a spelling.
values.append(10)
values.insert(^0, 30)
values.insert(^1, 20)          // [10, 20, 30]
let last = values.remove(^1)   // 30; capacity is unchanged.
let first = values[0]
values.append(first)
values.append(values[0])       // The element Copy finishes before receiver activation.
```

**Construction, access and further mutation.** Array also provides the following public members. Constructors and mutations follow §4.7.1; access members take a shared or exclusive receiver as their results require.

| Member | Behavior |
| --- | --- |
| `init(! capacity: isize)` | An empty Array with `capacity >= capacity` (§4.7.4); a negative argument Aborts, and zero allocates nothing |
| `init(! repeating: T, count: isize)`, `T is Copy` | `count` Copies of `repeating`; a negative count Aborts |
| `isEmpty: bool` | Read-only; whether `length` is zero |
| `first`, `last: Option<ref/T>` | Read-only, `get(self: ref/Self) -> Option<ref/T during self>`; the first or last element, or `None` when empty |
| `tryGet(index)`, `trySlice(range)`, `splitAt(index)`, `trySplitAt(index)` | The read operations of §4.6.6 on `self[..]`, with results `during self` |
| `tryGetUniq<P>(index: P)`, `P is Position` | `Option<uniq/T during self>` with an exclusive receiver; `None` unless `index` resolves to an element position |
| `tryGetPairUniq<P, Q>(! first: P, second: Q)`, `P is Position`, `Q is Position` | The disjoint exclusive pair operation of §4.6.10 |
| `swap<P, Q>(first: P, second: Q) -> ()`, `P is Position`, `Q is Position` | Exchanges two elements; positions resolving to the same element change nothing; an invalid position Aborts |
| `swapRemove<P>(index: P) -> T`, `P is Position` | Removes and returns the element; the last element takes its position, so order is not preserved |
| `truncate(length: isize) -> ()` | Destroys the elements from `length` to the end in decreasing index order; a length at or above the current length changes nothing, and a negative one Aborts |
| `appendAll(other: Array<T>) -> ()` | Moves every element of `other` to the end in order; `other` is consumed and its storage released |
| `appendCopies(values: Slice<T>) -> ()`, `T is Copy` | Copies each element of `values` to the end in order |
| `removeAll<F>(matching: F) -> ()`, `F is Callable<(ref/T) -> bool>` | Calls `matching` once per element in increasing index order with a shared reference, removes the elements for which it returns `true`, keeps the order of the others and destroys the removed ones after the last call, in an unspecified order |
| `reverse() -> ()` | Reverses the order of the elements |
| `sort() -> ()`, `T is Comparable` | Sorts in nondecreasing `compare` order; not stable |
| `sort<F>(by: F) -> ()`, `F is Callable<(ref/T, ref/T) -> i32>` | The same with `by` as the comparison: negative, zero or positive for less, equal or greater |

Moving elements inside the Array (`swap`, `swapRemove`, `removeAll`, `reverse`, `sort`) neither Copies them nor runs destructors, and no operation in this table allocates except `init` and the growth of `appendAll` and `appendCopies`. `removeAll` and `sort` must not observe the Array through their callbacks: the Array is exclusively borrowed for the whole call, and an Abort in a callback leaves every element initialized exactly once. A comparison that is not a consistent total order yields an unspecified permutation of the same elements.

```kimi
var values = Array<i32>.init(capacity: 4)
values.appendCopies([30, 10, 20][..])
values.sort()                                  // [10, 20, 30]
let removed = values.swapRemove(0)             // 10; values is [30, 20]
values.removeAll(matching: func (value) => value > 25) // [20]
let head = values.first                        // Some: a shared reference to 20
```

### 4.7.3. Dictionary operations and indexed replacement

All searches use the equality mapping, argument orientation and unspecified candidate order/count defined in [§12.3.4](12-expressions.md#1234-dictionary-construction-and-duplicate-keys). These search choices do not change insertion order or the specified iteration and destruction order.

| Operation | Absent key | Equal stored key |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K, V)>` | Append; `Ok(())` | Unchanged; `Err((input key, input value))` |
| `insertOrReplace(key: K, value: V) -> Option<V>` | Append; `None` | Keep the stored key and position; `Some(old value)` |
| `remove(key: ref/K) -> Option<(K, V)>` | `None` | Remove and return the stored key and value |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref/V during self>` | `None` | Shared reference to the stored value |
| `clear() -> ()` | Destroy all entries in the order of §4.7.6 | Same |

A duplicate is the only `Err` outcome of `tryInsert`; no dedicated error Type is introduced. Both value arguments are acquired before lookup, unlike Dictionary literals (§12.3.4). `insertOrReplace` secures the old result, stores the new value and then destroys the unused input key; delivery follows that cleanup. Removing a key and later adding an equal key appends a new position. No API mutates a stored key.

`tryGet` always returns `ref/V`, including for a Copy `V`. It never copies, moves or removes a stored value. Its result protects the whole Dictionary storage and preserves `V`'s dependencies, with no operation-only Loan on the search key. A lookup, or a saved Boolean observation, reserves no future access.

`map[key]` selects the value Place of an existing key through `UniqIndexable<K>` (§4.6.9); absence Aborts. `map[key] = value` replaces an existing value and returns Unit. It shared-borrows the key as `ref/K` rather than consuming it:

1. Evaluate and acquire the right-hand `V` once.
2. Identify the receiver and evaluate the key, left to right and once each.
3. Search with shared access, and Abort if the key is absent.
4. Obtain exclusive access to the value slot, destroy the old value and place the new one.

From receiver identification through placement, structural mutation, Move and destruction of the Dictionary are prevented. Shared access is kept during lookup, and all Loans, including the key's, are checked before writing; the key's borrow is not treated as ended early. Temporary lifetimes follow §3.6 and §10.2. Replacement is rejected if destroying the old value would invalidate dependencies of the secured right-hand side or of retained temporaries. Abort or nontermination during that destruction prevents placement. Compound assignment follows the right-hand-side-first order of §13.7, with one search, read and write.

```kimi
var names: Dictionary<i32, string> = [:]
match names.tryInsert(1, "first")
    .Ok(()) => ()
    .Err(let entry) => Kimi.Console.writeLine(entry.1)
names[1] = "replacement" // Existing-key replacement, not insertion.
let removed = names.remove(1) // A temporary key is borrowed under §10.2.
```

### 4.7.4. Capacity and allocation

Both collections expose read-only `capacity: isize` through shared access: the maximum number of elements or entries supported without internal allocation, not bytes or buckets. The result is a snapshot without a Loan. `0 <= length <= capacity <= isize.MaxValue` always holds. Typed empty `[]` and `[:]` have length and capacity zero and allocate nothing internally; a fixed handle-management cost is permitted. `Array<T>.init(capacity: c)` behaves as `[]` followed by `reserve(additional: c)`.

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

Generic summaries expose parameter-dependent equality and recursive destruction. They are verified at definition checking and substituted through validated specialization mappings; private bodies are not rescanned and legality is not deferred. For the operations of this table, the row, the dependency rules above, allocation, Abort and the accesses through the operation's inputs form the published summary: callers and bound verification (§8.4.5) use it, never a private body. The Kimi library's body of such an operation is verified against its summary with no exemption: a call in that body whose effects cannot be classified (§8.4.10.2), such as a call through an erased Function value or a generic Callable value, which have no available bound (§8.4.10.1, §8.4.10.4), makes the Kimi definition incompatible (§22.1) unless it is a parameter-dependent effect that the row publishes, such as `K` equality. Effects are unioned across permitted branches and implementations. A proven empty summary invents no static access. Receiver authority does not excuse external static reentry; unknown, possibly conflicting mutable-static effects are rejected under §15.6.4.

### 4.7.6. Commit, failure and destruction order

After normal acquisition, the operation resolves bounds or searches, then decides absence, duplicate, replacement or addition. Rejection and absence leave the collection unchanged before any capacity work. Replacement and removal do not run addition limits: duplicate rejection and replacement work even at maximum length. Only addition checks the increased length, required byte sizes and allocation, with checked arithmetic.

Relocation calls no user Copy, Move or `drop`. Equality sees a consistent structure; no partially moved state or conflicting reentry is exposed. These are operation invariants, not source concurrency guarantees.

A normal transfer during argument evaluation secures its transfer result and then cleans up previously acquired caller arguments and temporaries as required; the operation is not called. Abort supplies no result, rollback or later cleanup. Nontermination prevents later work and delivery.

Array `clear` and destruction use reverse current index order. Dictionary `clear` and destruction use reverse insertion order, destroying each value before its key. Owning iterators destroy remaining entries in the same order. Normal abandonment of partial construction cleans up completed acquisitions and placements in reverse order. Moved elements, spare capacity and uninitialized buckets are never read or destroyed.

### 4.7.7. Performance and extension boundary

For fixed Types, let `n` be the length, `c` the capacity and `d` the number of live elements or entries whose complete Types cannot prove cleanup-free destruction. Allocator internals, input generation and destruction bodies are charged separately from collection management. Count release is cleanup; inspecting a runtime enum Case is not a free proof of cleanup-free destruction.

| Operation | Required bound, excluding separately charged user-code cost |
| --- | --- |
| Array `append` / `pop` | Amortized O(1) / O(1) |
| Array `swap`, `swapRemove`, `first`, `last`, `tryGet`, `tryGetUniq`, `isEmpty` | O(1) |
| Array `truncate` | O(1 + d) for the destroyed elements |
| Array `appendAll` / `appendCopies` / `init(repeating:count:)` | Amortized O(1) per added element |
| Array `removeAll`, `reverse`; Slice `contains` / `firstIndex` | O(1 + n) plus the callback or comparison calls |
| Array `sort` | O(1 + n log n) comparisons and moves in the worst case, without allocation |
| Array stable `insert` / `remove` | O(1 + n) |
| Array `clear` | O(1 + d); O(1) for a cleanup-free element Type |
| Dictionary lookup | Search cost + O(1) management |
| Dictionary mutation | Search cost + amortized O(1) management |
| Dictionary `clear` | O(1 + c + d) is permitted |

For `m` operations without `shrinkToFit`, let `C` be the maximum of the initial capacity, observed lengths and `reserve` requests `R`. Total growth preparation and transfer management is O(m + C). Repeated `reserve(additional: 1)` followed by `append`, starting from empty, is O(n), not quadratic; `reserve` itself has no per-call O(1) promise.

Dictionary search cost includes key-content equality and internal hashing work and candidate or bucket probing for every operation; linear search is permitted. This introduces neither a public Hash constraint nor user-defined hash calls. Relocation, index rewriting and order maintenance remain management cost. Reindexing across growth, tombstones and allocation-free cleanup processes O(m + C) keys in total, not O(1) per key lifetime. Variable-length key work is charged at actual search cost; whether hashes are cached or recomputed is an implementation choice. Known-entry deletion, placement and order maintenance remain amortized O(1), including allocation-free full-capacity churn. O(c) auxiliary indices, free lists or linked slots and noncontiguous storage are permitted; avoid temporary arrays or per-element allocations used only to preserve order.

Ordinary free-slot, insertion-order and traversal management after a Dictionary removal is amortized O(1); physical compaction is not required, and search, user code and destruction are charged separately. Fixed-capacity mutation, mutable Slices, bulk removal of ranges, resize, a Dictionary `contains`, an Abort-only Dictionary insert, entry or factory APIs, recoverable allocators and a fixed collection ABI remain outside this contract. `tryGet` followed by indexed replacement may search twice; a future one-search update must define lookup and reference-write authority together.
