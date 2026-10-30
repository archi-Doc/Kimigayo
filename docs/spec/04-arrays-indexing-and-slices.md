# 4. Arrays, indexing, and slices

[Specification index](../SPEC.md)

A fixed array `[N of T]` has a compile-time length `N` and stores its elements inline. `Array<T>` owns variable-length storage, and `Slice<T>{source}` and `UniqSlice<T>{source}` are shared and exclusive views of contiguous elements. `Dictionary<K, V>` shares the indexing and mutation rules of this chapter.

## 4.1. Fixed arrays

The identity of `[N of T]` includes the evaluated length and the complete element Type, including Semantics and Origins: `[(2 + 2) of i32]` is `[4 of i32]`. No implicit conversion exists between arrays of different lengths, between fixed arrays and `Array<T>`, or between owning arrays and Slices.

Elements are stored inline in index order, without heap allocation; nested arrays vary the innermost index contiguously. With `d = stride(T)`, element `i` is at offset `i * d`, the alignment is `alignment(T)` (also for `N = 0`), and size and array stride are `N * d`. When `d = 0`, distinct elements may share an address. Sizes are checked against the target's limits and nonnegative `isize`; an unrepresentable concrete layout is a compile-time error. `[0 of T]` has size zero and initializes and destroys nothing, but `T` is still checked, and a struct storing `[0 of Self]` inline is a recursive layout error. Pointer and reference referents create no inline edge.

```kimi
let a: [4 of i32] = [1, 2, 3, 4]
let b: [(2 + 2) of i32] = a    // Same Type.
let c: Array<i32> = a          // Error: no conversion from a fixed array.

struct Node
    var children: [0 of Node]  // Error: recursive inline layout.
```

## 4.2. Length expressions

A length is a nonnegative compile-time integer representable in the target's `isize`; a generic length stays symbolic until instantiation (grammar: `ArrayLength`, `LengthExpression`, [§F.5](appendices/F-syntax-summary.md#f5-types)).

A length expression admits integer literals, length parameters, Constant-readable Bindings, grouping, unary `+` and `-`, and binary `+`, `-`, `*`, `/` and `%`. A **Constant-readable Binding** is an integer `let` local, or a static stored Property with an accessible standard `get`, whose initializer uses only these forms, recursively. Parameters, `var`, instance Fields, custom or computed accessors, calls and cyclic initializers are excluded. In a dependent length it is replaced by its initializer (Normalization, [§4.4.2](#442-formation-obligations-and-normalization)).

Evaluation uses checked arithmetic and runs no initializer or getter. Typed constants keep their Type and length parameters are `isize`; a literal-only subexpression takes an established operand Type ([§12.3](12-expressions.md#123-literal-typing)), otherwise `isize`. Different integer Types never combine, and intermediates may be negative. So for `let Small = 4`, `[(Small * 2) of u8]` computes in `i32`. A negative or out-of-range final length, overflow (also of an intermediate), a noninteger value and a zero divisor are compile-time errors.

Root-level bindings are startup locals ([§22.4](22-core-execution-and-foreign-functions.md#224-program-startup-and-static-storage)); put shared length constants in a group. A public signature may use a private constant in a length; its value becomes part of that public API, and changing a length changes every Type that uses it.

```kimi
group Dimensions
    public let Width: isize = 4
    public let Height: isize = 3

func fill()
    let buffer: [(Dimensions.Width * Dimensions.Height) of u8] = [12 of 0]
    let bad: [Dimensions.Width * 2 of u8]  // Error: parentheses required.
    let negative: [(-1) of u8]             // Error: negative length.
    let invalid: [(4 / 0) of u8]           // Error: division by zero.
```

## 4.3. Array construction

### 4.3.1. Array literals and annotation holes

With an expected fixed-array Type, an array literal constructs that Type and must have exactly `N` elements, acquired in source order ([§3.5](03-types-and-values.md#35-copy-and-move)). Otherwise an independent array literal constructs `Array<T>`, and a call-argument literal is fitted per candidate ([§4.4.1](#441-declaration-and-inference)). An empty literal needs a known element Type.

An initialized local `let` or `var` may write `_` for the length, the element Type or both in a fixed-array annotation, at any depth of written fixed-array structure. The initializer alone must determine every hole: a length hole takes the literal's element count or the initializer's fixed-array length; an element hole follows ordinary element evidence and numeric defaults, and an inner literal without a written fixed-array expectation constructs `Array<T>`. All elements must agree; a mismatch is an error, never a fallback to `Array<T>`. A written part of the annotation is an expectation for the initializer, also for inference through a callee's signature. Holes are forbidden in uninitialized declarations, signatures, Fields, static storage and explicit generic arguments.

```kimi
func zeros<T>() -> [3 of T]
    T is PrimitiveInteger
    return [3 of 0]

var values: [_ of i32] = [1, 2, 3]                       // [3 of i32]
let wide: [_ of i64] = zeros()                           // T = i64; length 3 from the signature.
let rows: [_ of _] = [[1], [2, 3]]                       // [2 of Array<i32>]
let matrix: [_ of [_ of f32]] = [[1.0, 2.0], [3.0, 4.0]] // [2 of [2 of f32]]
let unknown: [_ of _] = []                  // Error: no element Type.
let ragged: [_ of [_ of _]] = [[1], [2, 3]] // Error: conflicting inner lengths.
let wrong: [3 of i32] = [1, 2]              // Error: element count.
```

### 4.3.2. Initial construction

An annotation-only declaration stays Uninitialized; there is no zero fill. A whole-array initializer or assignment constructs it; part-wise writes before construction are errors ([§15.1.2](15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness)).

```kimi
var pending: [2 of i32]
pending[0] = 1   // Error: pending was never constructed.
pending = [1, 2]
```

### 4.3.3. Fill construction

`[Length of value]` constructs a fixed array; an `Array<T>` expectation is a mismatch. `Length` follows [§4.2](#42-length-expressions), and the element Type must be Copy. `value` is evaluated and acquired exactly once, even for length zero, and copied into every element. The array carries `value`'s Loans ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)) at every length, so `[0 of v]` keeps them too.

```kimi
let zeros: [64 of u8] = [64 of 0]
let flags = [8 of false] // [8 of bool]
var x = 1
let none = [0 of x@ref]  // [0 of ref/i32] carries x's Loan.
// x = 2                 // Error: none is used below.
inspect(none)
```

### 4.3.4. `noinit`

`noinit` is recognized, before name lookup, when it is the entire unparenthesized initializer after the `=` of any declaration, including stored Property initializers and parameter defaults. It is valid only for a local `var` with an explicit fixed-array annotation without holes, inside an Unsafe Block ([§14.3](14-control-flow.md#143-do-and-unsafe)); elsewhere it is an error, never a Name. `(noinit)` is an ordinary expression.

The element Type, after alias resolution, must be an `owner` Scalar: an integer, wrapping integer or floating-point Type, `bool` or `char`. A generic definition must prove this from its Constraints, such as `PrimitiveInteger`; `Copy` alone is insufficient. The declaration completes construction of the whole array but omits its initial element stores; afterwards all rules are those of an initialized array.

A program must write an element before reading it; an earlier read is undefined behavior, also through functions, references, views and raw pointers. Residual bits are not a write. Copy, Move and by-value passing of the whole array read every element. Element assignment, address formation, borrowing, `slice` / `sliceUniq`, metadata access and destruction read no element. No optimization or calling convention may read an element before the program does.

```kimi
unsafe
    var buffer: [3 of i32] = noinit
    var view = buffer.sliceUniq()
    view[0] = 10
    view[1] = 20
    view[2] = 30
    let copied = view.slice().toArray() // Every element was written first.

func f(x: i32 = noinit) => x // Error: noinit is only for a local var.
```

## 4.4. Function length parameters

### 4.4.1. Declaration and inference

A length slot is declared `<length N>`; a plain `<N>` is a Type slot. A length slot is a nonnegative `isize` constant in the Value namespace and counts once in GenericArity. Only functions have length slots; Type declarations have no value parameters.

An explicit generic argument list supplies every slot in order: a length expression, without `length`, or a Type. Otherwise, within each candidate, lengths and element Types are inferred from known fixed-array Types and from literals fitted to that candidate; all evidence must agree, and an established Array is never retyped. Expected Types fill only unresolved parts. Length equations are not solved backward: `[(N * 2) of T]` is checked once `N` is known. Lengths are never inferred from a body, later uses or candidate order ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)). Fixed and dynamic literals get no preference in candidate comparison.

```kimi
func process<length N>(values: [N of i32]) => ()
func keep<length N, T>(values: [N of T]) -> [N of T] => values@move
func work<length N>()
    let buffer: [N of u8] = [N of 0]

let a: [4 of i32] = [1, 2, 3, 4]
process([1, 2, 3]) // N = 3
process<3>(a)      // Error: length mismatch.
work<4>()          // A body-only length must be supplied.

func consume<T>(x: Array<T>) => ()
func consume<length N, T>(x: [N of T]) => ()
consume([1, 2, 3]) // Error: ambiguous.
```

### 4.4.2. Formation obligations and normalization

Each signature length expression `E` generates `ValidLength(E)`: `E` evaluates with checked operations to a nonnegative `isize`. A nondependent failure rejects the declaration. A dependent obligation is a public applicability condition (`N - 1` needs `N >= 1`), checked at a concrete call after inference and before candidate comparison; a false one eliminates the candidate and never ranks it. A layout failure after selection does not reopen selection.

The body is checked for every binding that meets the public conditions; its lengths and callee conditions must follow from the declaration, using only concrete evaluation, nonnegativity of length slots and identical normalized obligations. No condition is inferred from the body. Concrete layout is an instantiation-time obligation ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)).

**Normalization.** Signatures, dependent Types and obligations compare normalized length expressions; concrete Types compare evaluated lengths. A Constant-readable Binding in a length expression stands for its initializer: replace it, recursively, by its typed initializer before normalizing. A normalized length then contains only constants, length-slot positions and operators.

1. Remove grouping, evaluate nondependent subexpressions, and replace length parameters with slot positions.
2. Order the operands of each binary integer `+` or `*` by node kind, Type identity, constant value and slot position.
3. Keep all Types and operators and the operand order of `-`, `/` and `%`; never reassociate, distribute or cancel.

So `N + (2 + 2)` and `4 + N` coincide, but `(N + 2) + 2` need not equal `N + 4`.

```kimi
func reordered<length N>(value: [(N + 4) of u8]) -> [(4 + N) of u8] => value

func f<length N>(a: [(N + 1) of u8])
    let k: isize = N + 1
    let b: [k of u8] = a // Valid: k normalizes to N + 1.
```

## 4.5. Array ownership

A fixed array is Copy exactly when its element Type is Copy ([§3.5.2](03-types-and-values.md#352-copy-capability)); Owned is derived, and element Origins and Loans are retained. Partial Move follows [§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move). Cleanup destroys the remaining initialized elements in decreasing index order ([§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)); interrupted construction follows [§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order).

`Array<T>` is Non-Copy and accepts any complete element Type with a representable layout. It keeps `T`'s Origin dependencies and the acquired elements' Loans ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)), also after removal and clear ([§4.9.6](#496-loans-dependencies-and-call-effects)). The element position preserves Origin variance; `uniq/Array<T>` is invariant.

A shared view of an owning array is formed only explicitly: by range indexing, `slice()` / `slice(range)` ([§4.8.4](#484-views-and-uniqslice)) or the read operations of [§4.8.2](#482-slice-operations). Fixed arrays cannot resize, and Array mutations ([§4.9](#49-dynamic-collections)) need exclusive access to the whole Array. Slices grant no element mutation; use the array, an exclusive element Place, exclusive enumeration ([§14.6.2](14-control-flow.md#1462-iteration-protocol)) or a UniqSlice.

```kimi
var values: [4 of i32] = [10, 20, 30, 40]
let copy = values                     // Copy: i32 is Copy.
let middle: Slice<i32> = values[1..3] // Explicit shared view.
let all: Slice<i32> = values          // Error: no implicit conversion.
```

## 4.6. Positions and ranges

Positions and ranges are Kimi Types ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)); their names are not keywords, but `^` and range syntax always construct the Kimi Types. They retain no storage Origin or Loan.

### 4.6.1. Positions

A **position** denotes a boundary of a target of length `L` and resolves to an integer in `[0, L]`; an **element position** also satisfies `q < L`. The Types satisfying the closed Contract `Kimi.Position` ([§8.7](08-generics-constraints-and-contracts.md#87-compiler-intrinsic-and-closed-contracts)) are exactly the twelve integer Types, `FromEnd<T>`, `Start` and `End`; `T is PrimitiveInteger` implies `T is Position`.

```kimi
public contract Position: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<isize>
```

When `L < 0` every position fails. Otherwise, comparing mathematical values without truncation:

| Type | Written as | Resolves when | Result |
| --- | --- | --- | --- |
| Integer `n` | An integer value | `0 <= n <= L` | `n` |
| `FromEnd<T>` | `^x`, `T is PrimitiveInteger` | `0 <= offset <= L` | `L - offset` |
| `Start` / `End` | An omitted range start / end | Always | `0` / `L` |

`FromEnd<T>` has a read-only `offset: T`; `Start` and `End` are zero-sized. The three provide `tryResolve` and `resolve(self: Self, length: isize) -> isize` and have no public constructor. Integers gain no members: their `tryResolve` is reached only through `P is Position`, and they have no `resolve`. Construction checks nothing: `^(-1)` is valid and fails to resolve. `FromEnd<T>` compares offsets, all `Start` values are equal, and all `End` values are equal; they have no ordering, arithmetic or conversion, so `^n + 1` is a Type error (write `^(n + 1)`). A reference to a position is read as its value ([§3.5.3](03-types-and-values.md#353-value-read)), and a Type argument constrained to `Position` is inferred as the referent Type ([§10.2.2](10-overload-resolution-and-inference.md#1022-inference-and-adaptation-classes)).

```kimi
let values: [4 of i32] = [10, 20, 30, 40]
let last = ^1        // FromEnd<i32>
let a = values[last] // 40
let n: u8 = 1
let b = values[n]    // 20: every integer Type is a position.
let c = values[^0]   // Aborts if executed: ^0 is not an element position.
let d = ^1 + 1       // Error: FromEnd<i32> has no addition.
```

### 4.6.2. Ranges

A **range** is an interval of positions with the same meaning whether stored, passed, iterated or used as an index. Precedence is defined by [§F.9](appendices/F-syntax-summary.md#f9-expressions).

| Syntax | Type |
| --- | --- |
| `a..b` / `a..=b` | `Range<S, E>` / `ClosedRange<S, E>`: excluding / including `b` |
| `a..` | `Range<S, End>` |
| `..b` / `..=b` | `Range<Start, E>` / `ClosedRange<Start, E>` |
| `..` | `Range<Start, End>` |

Syntax alone fixes the shape. Construction checks nothing; a reversed range is a valid value. Each boundary is a value of a `Position` Type, read through references ([§3.5.3](03-types-and-values.md#353-value-read)), and the two boundary Types are independent. A boundary that is not literal-only ([§12.3](12-expressions.md#123-literal-typing)) needs its own known Type. A literal-only boundary (`^a` is literal-only when `a` is) takes its Type from the first applicable rule, and a mismatch never retries a later one:

1. The `S` or `E` of the range's expected Type.
2. The established integer Type of the other boundary (`T` for `FromEnd<T>`).
3. Default: `i32`, the integer-literal default outside length contexts ([§12.3](12-expressions.md#123-literal-typing)). Positions have no separate default, so `0..10`, `^1` and `values[3]` use `i32`.

`Range<S, E>` and `ClosedRange<S, E>` are Copy, Owned and Equatable, with read-only `start: S` and `end: E`, `tryResolve`, `resolve` and iteration ([§4.6.4](#464-range-iteration)); only range syntax constructs them, and they have no `length` or `isEmpty`. Different shapes are different Types and cannot be compared.

`ResolvedRange` is a distinct Type with the invariant `0 <= start <= end`, meaning the same as the `Range<isize, isize>` with the same boundaries, without conversion either way. It is Copy, Owned and Equatable and also has `length` and `isEmpty`. Only `indices` ([§4.7.3](#473-length-metadata)) and range resolution produce it, and it applies to any target that is long enough.

```kimi
let a = 1..^1                 // Range<i32, FromEnd<i32>>
let c = 0@i32..2@i64          // Range<i32, i64>: resolvable, not iterable.
let n: i64 = 2
let d: Range<i64, u8> = n..10 // 10 is u8 from the expected Type.
let f = (0..3) == (0..=2)     // Error: different range Types.
```

### 4.6.3. Resolution

```kimi
public contract PositionRange: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>
```

`PositionRange` is closed and satisfied exactly by the three range Types. Resolution reads only the boundaries and the length. With `s` and `e` the resolved boundaries and `q` the end resolved as an element position: `Range` requires `s <= e` and yields `[s, e)`; `ClosedRange` requires `s <= q` and yields `[s, q + 1)`; `ResolvedRange` requires `end <= L`. Resolution fails when `L < 0` or a position fails, and never clamps a reversed interval to an empty one.

The non-integer position and range Types publish `resolve` as the naming pair of `tryResolve` ([§4.9.2](#492-naming-convention)). Every aborting operation that takes a position or range (such as indexing, slicing, `slice`/`sliceUniq` ranges, `resolve`, `insert`, `remove`, `swapRemove` and `swap`) performs the same resolution and Aborts with `KIMI_E_INDEX_BOUNDS` ([§22.5.2](22-core-execution-and-foreign-functions.md#2252-abort-codes)) on `None`. A try-prefixed operation returns `None` only for its own bounds failure.

```kimi
(1..=3).tryResolve(5) // Some(1..4)
(2..=1).tryResolve(5) // None: reversed.
(..=^0).tryResolve(5) // None: ^0 is not an element position.
(^7..).tryResolve(5)  // None: 7 > 5.
```

### 4.6.4. Range iteration

`Range<S, E>` and `ClosedRange<S, E>` conform to `Iterable`, `UniqIterable` and `IntoIterable` when `S is PrimitiveInteger` and `E is S` ([§8.8](08-generics-constraints-and-contracts.md#88-conditional-conformance)), with `RangeIterator<S>` and `ClosedRangeIterator<S>`; `ResolvedRange` always does, with `RangeIterator<isize>`. Ranges with omitted or from-end boundaries or two integer Types are not iterable. Entries copy the boundaries and keep no Loan of the range, and the borrowing entries' `IteratorType(source)` does not depend on `source`. An entry Aborts when `start > end` ([§22.5.2](22-core-execution-and-foreign-functions.md#2252-abort-codes)); a reversed range is neither empty nor descending. Values ascend in unit steps; a closed range yields `end` last without computing past it, even at the maximum of `T`. The iterator Types are those of [§22.2.2](22-core-execution-and-foreign-functions.md#2222-standard-adapters-and-iterators). Infinite, descending and stepped ranges are deferred ([§D](appendices/D-deferred-features.md#appendix-d-deferred-features)).

```kimi
let values: [5 of i32] = [10, 20, 30, 40, 50]
for i in 0..values.length               // Range<isize, isize>
    Console.writeLine("\(values[i])")
for x in 0..=255@u8                     // Stops after 255.
    ()
for i in (1..^1).resolve(values.length) // 1, 2, 3
    ()
for i in 1..^1                          // Error: a FromEnd boundary is not iterable.
    ()
```

## 4.7. Indexing

Element and range indexing are built in for `[N of T]`, `Array<T>`, `Slice<T>` and `UniqSlice<T>`. Raw pointers take only `isize` offsets, without bounds checks ([§5.3](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing)).

### 4.7.1. Index-expression resolution

Every single-element index expression, also on user Types and Dictionary, resolves through these Kimi Contracts, whose requirements return a reference to the selected element. The key is shared-borrowed, never consumed; a key of reference or handle Type is written `key@ref` ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)).

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self
```

Both requirements return a reference to the same element for the same key; `indexUniq` grants exclusive access without changing the stored Type. `receiver[key]` resolves statically:

1. After reference-path selection ([§3.4.3](03-types-and-values.md#343-reference-path-selection)), the receiver's Type gives its `Indexable<Key>` conformances. On a concrete sequence Type (fixed array, Array, Slice, UniqSlice) the key is a position: it is typed by [§4.6.2](#462-ranges) and normalized (Position normalization below). On every other receiver the key is the argument of the parameter `key: ref/Key` of each conformance and fits a conformance when it fits that parameter under common adaptation ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)); a literal-only key fits under [§12.3](12-expressions.md#123-literal-typing) before any default. Exactly one conformance must fit: none is a Type error and several are an ambiguity error. A literal default never chooses a conformance, and `Key` is never inferred from the expected result.
2. The use determines the capability: bare acquisition ([§3.5](03-types-and-values.md#35-copy-and-move)), adaptation to a fixed expected Type ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)), a write, an explicit borrow, a Subject ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)) or a Receiver Expression ([§7.3](07-functions-and-callable-values.md#73-receivers)).
3. Reads and shared borrows select `index`; updates, exclusive borrows and exclusive Reborrows select `indexUniq` of the same conformance.

Through a conformance, the index expression designates the referent of the reference that the selected call returns, the Place `@follow` would select ([§13.5.5](13-operators-and-assignment.md#1355-follow)): its stored Type is `Element`, and its capability and dependencies are those of that reference; it never offers Take. A written call such as `names.index(1)` is an ordinary call whose value is that reference; only the index operator follows it, never a function Name or result Type.

Selecting `indexUniq` acquires the receiver exclusively without `@uniq`, but only with the capability its path already has; a `let`-owned or shared path cannot supply it ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)). A failure never reselects a mode or candidate. Receiver and key stay Loaned during the call. The element depends on what the returned reference depends on, never on the key, and not on the temporary holding the reference ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation), [§13.5.6](13-operators-and-assignment.md#1356-borrow-and-reborrow), [§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)); the Origins inside `Element` and their actual Loans are kept as usual.

**Position normalization.** On a concrete sequence Type, `x[k]` resolves `k` to an element position `q` and performs `x[q]` through `Indexable<isize>`, with one bounds check. The key is read and resolved first and keeps no borrow, so `indexes[indexes[0]] = x` is valid. A generic `S is Indexable<Key>` and every other Type select a conformance by step 1, so `s[^1]` is unavailable through `S is Indexable<isize>`.

| Target | Element through a conformance | Range result | Depends on |
| --- | --- | --- | --- |
| `Array<E>`, `[N of E]`, `UniqSlice<E>` | Referent of `ref/E` or `uniq/E` | `Slice<E>` | `a`, the receiver borrow |
| `Slice<E>` | Referent of `ref/E during s.source` | `Slice<E>` | `s.source` |
| `Dictionary<K, V>` | Referent of `ref/V` or `uniq/V`; absence Aborts | None | `a`, never the key |

Array, fixed arrays and UniqSlice conform to `UniqIndexable<isize>`, Dictionary to `UniqIndexable<K>` and Slice to `Indexable<isize>` ([§8.6.1](08-generics-constraints-and-contracts.md#861-conformance-declarations-and-identity)). Range indexing is not part of the Indexable family, and no whole-range assignment exists.

A direct fixed-array element is a built-in projection: it neither copies the array nor borrows it as a whole. Array element access calls `index` or `indexUniq` and borrows the whole Array.

```kimi
var names: Dictionary<i64, string> = [:]
_ = names.tryInsert(1, "one")
names[1] = "uno"   // 1 fits the only Key, i64.
let id: i32 = 1
names[id] = "eins" // Error: an i32 key fits no conformance.
let lent = names.index(1) // ref/string: a written call returns the reference.
// names.indexUniq(1) = "x" // Error: the target is the reference, not its referent.
// For a Type conforming to Indexable<i32> and Indexable<i64>,
// pair[1] is ambiguous; write pair[1@i64].
```

### 4.7.2. Element Places

An index expression denotes an element Place with the selected capability (through a conformance, the referent of the returned reference, [§4.7.1](#471-index-expression-resolution)); forming it copies nothing, so `matrix[1][2] = 10` copies no inner array. `values[i] = v` initializes or replaces; `@ref` and `@uniq` borrow the Place; `@move` transfers only through a static Move Path of an owned fixed array, which only in-range integer literal indices form ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)). A bare read is bare acquisition ([§3.5](03-types-and-values.md#35-copy-and-move)): a Copy of a Copy element, or a Reborrow of a stored `uniq/U` or `objuniq/U` through `indexUniq`; any other Non-Copy element needs a borrow. A bare read never Moves. A fixed expected `ref/T` borrows an owned element implicitly ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)); an element that stores a reference or handle is adapted instead, for example Reborrowed.

```kimi
// resources: a complete owned [2 of Resource]; i: isize; refs: a writable Array<uniq/Node>.
inspect(resources[i]) // Shared borrow at a ref/Resource parameter.
normalize(refs[0])    // indexUniq; the stored reference is Reborrowed.
```

### 4.7.3. Length metadata

Fixed arrays, Array, Slice and UniqSlice provide read-only `length: isize` and `indices: ResolvedRange`; all but fixed arrays also provide `isEmpty: bool`. They read no element, and a fixed array still checks access to its receiver. The results keep no Loan. `x.indices` is the snapshot `(..).resolve(x.length)`; resizing an Array does not update it.

```kimi
var values: Array<i32> = [1, 2, 3]
let range = values.indices // 0..3
values.append(4)           // range is still 0..3.
```

### 4.7.4. Evaluation, failure and updates

The receiver and index are evaluated once each in evaluation order ([§12.2](12-expressions.md#122-evaluation-order)); a range evaluates its written boundaries, start first. The receiver is located once: an existing Place is used in place, and only an owned temporary is materialized. While the index is evaluated, the receiver's storage may be read but not modified, moved, destroyed or reallocated. A chained element Copy completes before later surrounding operands. A Slice receiver's handle is copied first; a UniqSlice handle is borrowed.

Element and range indexing Abort on invalid bounds; `tryGet`, `trySlice` and `trySplitAt` return `None`. `values[L..L]` and `values[^0..]` are empty; element position `L` or `^0` and a closed end `^0` are invalid. An out-of-bounds index is never by itself a compile-time error ([§17.5](17-failure-handling.md#175-warnings)).

Assignment secures its right-hand side before locating the target ([§13.7.1](13-operators-and-assignment.md#1371-simple-assignment), [§13.7.2](13-operators-and-assignment.md#1372-compound-assignment)); compound assignment, increment and decrement then check bounds, read, compute and write back once each. The exclusive Loan of an element write lasts from bounds resolution through placement. Exchange operations follow [§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates); `Kimi.Intrinsics.swap` needs static non-overlap.

```kimi
let values: [3 of i32] = [1, 2, 3]
let r = (-1)..2               // Valid: construction checks nothing.
let s = values[r]             // Aborts in resolution.
let t = values.trySlice(2..5) // None.
```

## 4.8. Slices and views

### 4.8.1. Slice

`Slice<T>{source}` is a Copy view of an initialized contiguous region of `T`; its length is not part of its Type. Every range access returns a Slice. Indices start at zero, and from-end positions and reslicing use the Slice's own length. Rows of a nested array form `Slice<[N of T]>`, never a flattened Slice, and Slices of different element Types never convert.

The Origin records the backing lifetime and the Loan the conflicting Places. Copies, reslices, iterators and element references retain both and do not borrow the handle variable. While an array-derived view is live, it Loans the whole array Place ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)), also after reslicing or when empty: conflicting writes, Move, destruction and reallocation are rejected.

A Slice owns no elements, and `T` need not be Owned. `source` may be shortened, never lengthened; Owned follows OwnedOrigins ([§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned)). A Slice is stored under ordinary storage ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)), in static storage only when Owned ([§11.8](11-properties.md#118-static-storage)), and never extends the backing lifetime, also of a temporary ([§3.6](03-types-and-values.md#36-temporaries)). A `var` Slice permits only handle reassignment, and `uniq/Slice<T>` borrows the handle, not the elements. Slices have no equality.

```kimi
var values: Array<i32> = [1, 2, 3]
let view = values[0..2]
let moved = values@move     // Error: values is Loaned by view.
let first = view[0]

func makeArray() -> Array<i32> => [1, 2, 3]
inspect(makeArray()[..])    // The temporary lives through the call.
let escaped = makeArray()[..]
inspect(escaped)            // Error: the temporary has expired.
```

### 4.8.2. Slice operations

Slice members take the handle by value, and results retain `s.source`.

| Operation | Contract |
| --- | --- |
| `toArray(self: Self) -> Array<T>`, `T is Copy` | Copies each element once, in order, into new storage |
| `tryGet<P>(self: Self, index: P) -> Option<ref/T during source>`, `P is Position` | `None` unless `index` is an element position |
| `trySlice<R>(self: Self, range: R) -> Option<Slice<T> during source>`, `R is PositionRange` | `None` when `range` does not resolve |
| `splitAt<P>(self: Self, index: P) -> (Slice<T> during source, Slice<T> during source)`, `P is Position` | Resolves `index` once to `p`; `(s[..p], s[p..])` |
| `trySplitAt<P>(self: Self, index: P) -> Option<(Slice<T> during source, Slice<T> during source)>` | The same, or `None` |
| `contains(self: Self, value: ref/T) -> bool`, `T is Equatable` | Whether an element equals `value` |
| `firstIndex(self: Self, of: ref/T) -> Option<isize>`, `T is Equatable` | The first index whose element equals `of` |
| `firstIndexWhere<F>(self: Self, predicate: F) -> Option<isize>`, `F is Callable<(ref/T) -> bool>` | The first index for which `predicate` returns `true` |

Searches visit elements in index order, stop at the first match and pass shared references. `tryGet` always returns a reference. Fixed arrays and Array provide `tryGet`, `trySlice`, `splitAt` and `trySplitAt` as on `x[..]`, with results `during self`. Slice element Places are shared-only. `toArray` reads every element ([§4.3.4](#434-noinit)), allocates at most once and not when empty, and keeps the dependencies inside `T` but not the outer borrow.

```kimi
let values: [4 of i32] = [10, 20, 30, 40]
let s = values[..]
let a = s.firstIndex(of: 30)                          // Some(2)
let b = s.firstIndexWhere(func (value) => value > 15) // Some(1)
let parts = s.splitAt(^1)                             // (10, 20, 30), (40)
s[0] = 5                                              // Error: Slice elements are read-only.

func first<T>(s: Slice<T>) -> ref/T during s.source
    return s[0]@ref // Valid for every T; a by-value `let x = s[0]` needs T is Copy.
```

### 4.8.3. Slice iteration

Slice conforms to `Iterable`, `UniqIterable` and `IntoIterable`; in every mode the item is `ref/T during source`, in index order ([§14.6.2](14-control-flow.md#1462-iteration-protocol)). Items borrow the backing slots, not the iterator, and may be kept across `next` calls. An inner reference stored in an element keeps its own Origin, separate from the slot borrow.

```kimi
let data: i32 = 10
let refs: [1 of ref/i32] = [data@ref]
let s: Slice<ref/i32> = refs[..]
let value = s[0]       // Copies the inner reference to data.
for element in s
    ()                 // element: ref/(ref/i32), borrowing the slot of refs.
```

### 4.8.4. Views and UniqSlice

Each `slice` and `sliceUniq` has a whole-view overload and a range overload taking `R is PositionRange`, which Aborts on invalid bounds.

| Provider | Shared view | Exclusive view |
| --- | --- | --- |
| `[N of T]`, `Array<T>` | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |
| `Slice<T>{source}` | `slice(self: Self) -> Slice<T> during source` | None |
| `UniqSlice<T>{source}` | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |

`x[r]` means `x.slice(r)`: range subscripting always forms a shared Slice.

`UniqSlice<T>{source}` is a Non-Copy exclusive borrow of an initialized contiguous region; it owns no elements, and `T` need not be Copy or Owned. It has the metadata, `index` and `indexUniq` (`UniqIndexable<isize>`) and the view methods. Elements can be read, replaced or borrowed, never Moved out. Exclusive operations need a `var` owner or an exclusive reference. There is no implicit Copy, Move, Reborrow or conversion: write `view@move`, `view.sliceUniq(...)` or `view.slice(...)`. UniqSlice has no iteration and no `toArray`. `T` is invariant. A child view depends on the parent's current borrow; a shared child keeps writes prohibited while live. Different ranges never prove disjointness.

```kimi
var values: [3 of i32] = [1, 2, 3]
values.sliceUniq(0..3)[0] = 10
var view = values.sliceUniq()
let reference = view@uniq
reference[0] = 20     // The referent stays exclusively accessible.
values[0..3][0] = 40  // Error: range subscripting forms a shared view.
```

### 4.8.5. Disjoint exclusive element pairs

`Array<E>` and `[N of E]` provide `tryGetPairUniq<P, Q>(self: uniq/Self ! first: P, second: Q) -> Option<(uniq/E during self, uniq/E during self)>` with `P is Position` and `Q is Position`. It returns `None` if either position is not an element position or both select the same logical element; otherwise exclusive references to the two elements in argument order, which never overlap. It is O(1), changes and allocates nothing, and calls no user code. Separately formed exclusive element borrows still need static non-overlap ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)).

```kimi
var values: Array<i32> = [1, 2, 3]
match values.tryGetPairUniq(first: 0, second: ^1)
    .Some(let pair) => exchange(pair.0, pair.1)
    .None => ()
let i: isize = 0
let a = values[i]@uniq
let b = values[1]@uniq // Error: no static non-overlap.
inspect(a)
```

### 4.8.6. Costs

Constructing, resolving and copying positions and ranges, and forming, copying, reslicing and splitting views and element Places, take O(1) time without allocation or reference-count update. Slice and UniqSlice elements are spaced by `stride(T)`.

For standard iterators of arrays, Slices, ranges and Dictionary, let `n` be the number of live elements or entries when enumeration starts; free capacity is excluded. Creation, destruction and each `next` take O(1) time (amortized for Dictionary), and full enumeration takes O(1 + n). Bookkeeping uses O(1) storage in `n`; owned element storage is separate. An inline owned `[N of E]` occupies `N * stride(E)` bytes, and an owning dynamic-collection iterator retains the existing element storage. Iterator operations make no new heap allocation. Element movement and destruction costs are charged separately, including movement of inline array storage.

## 4.9. Dynamic collections

### 4.9.1. Common rules

`Array<T>` and `Dictionary<K, V>` are Non-Copy owning collections. Dictionary accepts any complete stored Types, requires `K is Equatable` and the key stability of [§12.4.3](12-expressions.md#1243-dictionary-literals), and has no public Hash constraint.

Mutations take `self: uniq/Self`, so a collection in receiver position is acquired exclusively without `@uniq` ([§7.3](07-functions-and-callable-values.md#73-receivers)) when its lending point is exclusively writable ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)); a `uniq` parameter still needs `@uniq` ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)). Removal transfers the stored responsibility. Precondition failures Abort, absence returns `Option`, and recoverable rejection returns its inputs in `Result`.

Adding keeps prior order and grows capacity if needed; replacing changes only the value; removing keeps the remaining order and capacity; clearing destroys all elements and keeps capacity; lookups and rejections change nothing; reserving and shrinking keep values, order and length. Effects of arguments, equality and destructors are not rolled back.

```kimi
var values: Array<i32> = []
values.append(10)       // Exclusive receiver, no @uniq.
grow(values@uniq)       // A uniq parameter needs @uniq.
```

### 4.9.2. Naming convention

Functions of one Name share one receiver shape ([§7.3](07-functions-and-callable-values.md#73-receivers)) and parameter modes ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)), and accessor receivers are fixed ([§11.3](11-properties.md#113-accessor-functions)), so shared and exclusive variants, and a Property and a consuming operation over the same state, need different Names. Kimi declarations and this specification's examples follow these pairs:

| Pair | Convention | Example |
| --- | --- | --- |
| New value / in place | Past participle / verb | `sorted` / `sort` |
| Shared / exclusive reference | `Uniq` suffix on the exclusive one | `tryGet` / `tryGetUniq` |
| Inspects / advances or takes | Different verbs; against a Property, verb + noun | `peek` / `next`, `reading` / `nextReading` |
| Observes / consumes to take | Noun Property / `into` + noun | `item` / `intoItem` |

### 4.9.3. Array operations

Positions are passed by value and resolved once against the entry length ([§4.6.3](#463-resolution)).

| Member | Behavior |
| --- | --- |
| `init(! capacity: isize)` | Empty; negative Aborts |
| `init(! repeating: T, count: isize)`, `T is Copy` | `count` Copies; `repeating` acquired once, even when `count` is zero; a negative count Aborts before any element is placed; allocates at most once, and not when `count` is zero |
| `append(value: T)` / `pop() -> Option<T>` | Add at / remove from the end |
| `insert<P>(index: P, value: T)` | Insert at the boundary; `insert(^0, v)` appends |
| `remove<P>(index: P) -> T` / `swapRemove<P>(index: P) -> T` | Remove an element, keeping order / moving the last element into its place |
| `swap<P, Q>(first: P, second: Q)` | Exchange two elements; the same element changes nothing; an invalid position Aborts |
| `truncate(length: isize)` / `clear()` | Destroy the elements from `length` on / all, in the order of [§4.9.7](#497-failure-and-destruction-order); a larger length changes nothing, a negative one Aborts |
| `appendAll(other: Array<T>)` | Move all elements of `other` to the end |
| `appendCopies(values: Slice<T>)`, `T is Copy` | Copy all elements to the end |
| `removeAll<F>(matching: F)`, `F is Callable<(ref/T) -> bool>` | Call `matching` once per element in index order; remove matches, keep the others' order; destroys them in the order of [§4.9.7](#497-failure-and-destruction-order) |
| `reverse()` | Reverse the order |
| `sort()`, `T is Comparable` / `sort<F>(by: F)`, `F is Callable<(ref/T, ref/T) -> i32>` | Nondecreasing order by `compare` / `by`; not stable; `by` returns a negative, zero or positive value for less, equal or greater |
| `first`, `last: Option<ref/T during self>` (shared receiver) | `None` when empty |
| `tryGetUniq<P>(index: P) -> Option<uniq/T during self>` | `None` unless an element position |

`P` and `Q` are Position Types. Moving elements within the Array copies none and runs no destructor. Allocation guarantees are defined in [§4.9.5](#495-capacity-and-allocation). During `removeAll` and `sort` callbacks cannot observe the Array, and an Abort in a callback leaves every element initialized exactly once. An inconsistent comparison yields an unspecified permutation.

```kimi
var values = Array<i32>.init(capacity: 4)
values.appendCopies([30, 10, 20][..])
values.sort()                                  // [10, 20, 30]
let removed = values.swapRemove(0)             // 10; values is [30, 20]
values.removeAll(matching: func (v) => v > 25) // [20]
values.append(values[0])                       // The Copy precedes receiver activation.
let bad = values.remove(^0)                    // Aborts: ^0 is not an element position.
```

### 4.9.4. Dictionary operations

Searches use the equality mapping of [§12.4.3](12-expressions.md#1243-dictionary-literals) and never change insertion order.

| Operation | Absent key | Equal stored key |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K, V)>` | Append; `Ok(())` | `Err((key, value))` |
| `insertOrReplace(key: K, value: V) -> Option<V>` | Append; `None` | Keep the stored key; `Some(old value)` |
| `remove(key: ref/K) -> Option<(K, V)>` | `None` | The stored entry |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref/V during self>` | `None` | Shared reference to the value |

Both arguments are acquired before lookup. `insertOrReplace` destroys the unused input key after storing. No operation mutates a stored key, and `tryGet` never copies a value; its result Loans the whole Dictionary but not the key. A re-added key appends a new position. `clear` destroys all entries ([§4.9.7](#497-failure-and-destruction-order)).

`map[key]` selects the value Place of an existing key ([§4.7.1](#471-index-expression-resolution)); absence Aborts. `map[key] = value` acquires the right-hand side, evaluates receiver and key, searches with shared access, then destroys the old value and places the new one. Throughout, the Dictionary cannot be restructured, moved or destroyed, all Loans, including the key's borrow, are checked before writing, and replacement is rejected if destroying the old value would invalidate a dependency of the right-hand side. Compound assignment searches once.

```kimi
var names: Dictionary<i32, string> = [:]
match names.tryInsert(1, "first")
    .Ok(()) => ()
    .Err(let entry) => Kimi.Console.writeLine(entry.1)
names[1] = "replacement"      // Replacement, not insertion.
names[2] = "two"              // Aborts if executed: key 2 is absent.
```

### 4.9.5. Capacity and allocation

Both collections expose read-only `capacity: isize`, the elements or entries storable without allocation; `0 <= length <= capacity`. Typed empty `[]` and `[:]` allocate nothing, and `init(capacity: c)` behaves as `[]` then `reserve(additional: c)`. `reserve(additional:)`, which also accepts its argument positionally, Aborts on a negative or overflowing request and ensures `capacity >= length + additional`, never shrinks, and when capacity suffices neither allocates nor moves elements. Adding within capacity, lookup, replacement, removal, `clear` and rejection never allocate; growth may Abort on allocation failure ([§22.5.2](22-core-execution-and-foreign-functions.md#2252-abort-codes)). Exact capacities are implementation-defined.

`shrinkToFit()` may allocate to reduce capacity; the resulting capacity is between length and the old capacity, inclusive. It preserves values, order and length. If size calculation or allocation fails, it returns normally and also preserves capacity and element placement. Exact fit and release to the OS are not guaranteed.

### 4.9.6. Loans, dependencies and call effects

The exclusive receiver is reserved before later arguments and activated at entry ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)); element and Slice Loans live at activation conflict. Added and replacing inputs' dependencies join the collection's set, also when `tryInsert` returns `Err`; removed values carry the stored Types' full dependencies; no operation subtracts dependencies; `tryGet` results add the storage Loan. A removed `uniq/T` is not independent of the remaining `Array<uniq/T>` until Loan liveness permits.

| Operation | User-code effects inside the operation |
| --- | --- |
| Array `clear`, `truncate`, destruction, Array and fixed-array owning-iterator destruction and indexed replacement | Destruction of the relevant `T` |
| Array `removeAll` | Calls of `matching`; destruction of the removed `T` and of `matching` |
| Array `sort()` | `T`'s `compare` |
| Array `sort(by:)` | Calls of `by` |
| Slice `contains`, `firstIndex(of:)` | `T` equality |
| Slice `firstIndexWhere` | Calls of `predicate` |
| Dictionary `tryGet` / `tryInsert` / `remove` and indexing (`index`, `indexUniq`) | `K` equality |
| Dictionary `insertOrReplace` | `K` equality and destruction of the unused input `K` |
| Dictionary indexed replacement | `K` equality and destruction of the old `V` |
| Dictionary `clear`, destruction and owning-iterator destruction | Destruction of `V` and `K` |
| Every other operation of [§4.6](#46-positions-and-ranges) to [§4.9](#49-dynamic-collections): construction, `append`, `insert`, `pop`, `remove`, `swap`, `swapRemove`, `reverse`, `appendAll`, `appendCopies`, Array, fixed-array and Slice access, view, position, metadata and capacity operations | None |

A row, with the dependency rules above, allocation, Abort and the accesses through the operation's inputs, is the operation's complete published effect. Callers use it, never a library body, and the library implementation must stay within it. Calls of a Callable argument are composed at the caller through the actual argument ([§8.11.4](08-generics-constraints-and-contracts.md#8114-callable-effect-bounds)). A concrete instantiation such as `Array<i32>` uses the row directly. Parameter-dependent destruction is composed at callers for Loan checking, unless [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls) widens it; bound verification ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use)) substitutes the definition's own bindings, and destruction that still depends on an abstract Type is unclassifiable there. A generic body checks such a destruction against its own Loans under [§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release).

```kimi
var values: Array<i32> = [3, 1, 2]
let view = values[..]
values.sort()          // Error: view's Loan is live at activation.
let first = view[0]
```

### 4.9.7. Failure and destruction order

An operation resolves bounds or searches before deciding its outcome; rejection and absence change nothing, and only addition checks size and allocation. Relocation calls no user code. A transfer during argument evaluation skips the call ([§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order)).

Array `truncate`, `clear` and destruction destroy elements in decreasing index order. `removeAll` destroys its removed elements after the last call of `matching`, in an unspecified order. Dictionary `clear` and destruction use reverse insertion order, each value before its key. Owning iterators destroy their remaining elements in the same orders.

```kimi
var log: Array<Logged> = [Logged.init(1), Logged.init(2), Logged.init(3)]
log.truncate(1) // Destroys index 2, then index 1 (Logged 3, then Logged 2).
```

### 4.9.8. Costs

With `n` the length, `c` the capacity and `d` the destroyed elements whose Types need cleanup, excluding user code:

| Operation | Bound |
| --- | --- |
| Array `append` / `pop` | Amortized O(1) / O(1) |
| Array `swap`, `swapRemove`, `first`, `last`, `tryGet`, `tryGetUniq` | O(1) |
| Array `truncate`, `clear` | O(1 + d) |
| Array `appendAll`, `appendCopies`, `init(repeating:count:)` | Amortized O(1) per element |
| Array `insert`, `remove`, `removeAll`, `reverse`; Slice `contains` / `firstIndex` / `firstIndexWhere` | O(1 + n) |
| Array `sort` | O(n log n) worst case, without allocation |
| Dictionary lookup / mutation | Search cost + O(1) / amortized O(1) |
| Dictionary `clear` | O(1 + c + d) |

Over `m` operations without `shrinkToFit`, total growth is O(m + C), where `C` is the largest capacity, length or `reserve` request, so repeated `reserve(additional: 1)` and `append` is O(n). Dictionary search cost includes equality, hashing and probing; linear search is permitted.
