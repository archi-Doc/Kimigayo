# Kimi Library

A concise reference to the public Kimi APIs required by the language specification.

- **Authority:** [SPEC.md](SPEC.md) and its chapters define required behavior and take precedence over this reference.
- **Support:** [STATUS.md](STATUS.md) records implemented and verified support. Inclusion here does not imply implementation.
- **Implementation:** [Kimi/Library/README.md](Kimi/Library/README.md) describes source embedding and implementation policy.
- **Conventions:** [STYLE.md](STYLE.md) defines coding and API conventions.

Names are relative to `Kimi`. The default alias opens its root, but not nested groups such as `Text` or `Intrinsics`. Use group-qualified names for their members.

Tables omit `public` and may omit `func`. Each signature includes its receiver and result; `Self` denotes the containing Type. Properties are read-only unless stated otherwise. An argument Type written as `A or B` abbreviates two overloads, not a union Type. `during` annotations identify result dependencies; they never extend storage lifetimes.

This reference lists required constructors and named operations, and describes common conformances once. Concrete iterator constructors and compiler-internal helpers are not required APIs. Current source-only interfaces are noted in §7. Detailed syntax, effect bounds and compiler operations remain in the linked specification.

## 1. Foundational Types and Contracts

### 1.1. Intrinsic Contracts

These are compiler identities, not user-implementable replacements.

| Declaration | Guarantee | Specification |
| --- | --- | --- |
| `contract Copy` | Bare acquisition or `@copy` duplicates a value without consuming its source, subject to access and Loan rules. | [Copy and Move](spec/03-types-and-values.md#35-copy-and-move) |
| `contract Owned` | All lifetime dependencies are `static`; qualifying static borrows are allowed. This is distinct from owner semantics. | [static and Owned](spec/15-ownership-and-lifetime-analysis.md#1523-static-and-owned) |
| `contract Callable` | `F is Callable<r, (A...) -> R>` requires a call with receiver mode `ref`, `uniq` or `owner`. Omitting `r` means `ref`. | [Callable constraints](spec/08-generics-constraints-and-contracts.md#86-callable-constraints) |
| `contract Sealed` | The outer semantics are owner and the Core is a valid, non-open, non-Never Type. It does not imply Copy or Owned. | [Sealed](spec/08-generics-constraints-and-contracts.md#8471-sealed) |
| `contract ObjectPayload` | The value may become a new object payload: owner semantics, a non-Never Core, and no applicable opt-out. Open structs may qualify. | [ObjectPayload](spec/08-generics-constraints-and-contracts.md#8472-objectpayload) |

### 1.2. Comparison

[Specification: comparison mapping](spec/13-operators-and-assignment.md#1341-contract-comparison-mapping).

| Contract | Member | Guarantee |
| --- | --- | --- |
| `Equatable` | `equals(self: ref/Self, other: ref/Self) -> bool` | Equality for Contract-based comparisons and Dictionary keys. |
| `Comparable: Equatable` | `compare(self: ref/Self, other: ref/Self) -> i32` | Negative, zero or positive for less, equal or greater. |

The floating-point Equatable mapping treats all NaNs as equal and signed zeros as equal. Built-in floating-point `==` retains its IEEE behavior; floats do not conform to Comparable.

### 1.3. Option and Result

[Specification: required declarations](spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) and [try propagation](spec/17-failure-handling.md#1724-try-propagation).

| Declaration | Cases, in order | Guarantee |
| --- | --- | --- |
| `enum Option<T>` | `Some(T)`, `None` | Expected absence; Copy exactly when T is Copy. |
| `enum Result<T, E>` | `Ok(T)`, `Err(E)` | Recoverable failure; Copy exactly when T and E are Copy. |

`try` propagates `None` or `Err` from a compatible enclosing function; Result propagation keeps the same error Type. Payload Origins and Loans follow ordinary enum rules.

## 2. Iteration and Indexing

### 2.1. Iterator Contracts

[Specification: LendingIterator and Iterator](spec/22-core-execution-and-foreign-functions.md#22121-lendingiterator-and-iterator) and [iterator independence](spec/22-core-execution-and-foreign-functions.md#22124-iterator-independence).

```kimi
contract LendingIterator
    associate LentItem(step) for uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>

contract Iterator: LendingIterator
    associate Item
    associate LendingIterator.LentItem(step) is Item
```

`next` exclusively borrows the iterator so it can advance its state. A lending item may borrow that call's receiver and prevent another call while retained. Iterator instead publishes a step-independent Item and an effect bound that permits retaining earlier items across later calls. External source dependencies still apply.

`for` stops at the first `None`. A general LendingIterator, including a user Iterator, may later return `Some`. Standard collection, Slice and ResolvedRange iterators remain exhausted after `None`.

### 2.2. Iteration Entries and Adapters

[Specification: iteration entries](spec/22-core-execution-and-foreign-functions.md#22122-iteration-entries) and [standard adapters](spec/22-core-execution-and-foreign-functions.md#22123-standard-adapters).

```kimi
contract Iterable
    associate IteratorType(source) is LendingIterator for ref/Self during source
    func iterate(self: ref/Self during source) -> Self.IteratorType(source)

contract UniqIterable
    associate IteratorType(source) is LendingIterator for uniq/Self during source
    func iterateUniq(self: uniq/Self during source) -> Self.IteratorType(source)

contract IntoIterable
    associate IteratorType is LendingIterator
    func intoIterator(self: Self) -> Self.IteratorType
```

These capabilities are independent. The selected entry determines how the subject is acquired, not the item's access mode. Conformance is explicit; a matching method name alone is insufficient.

The `Iteration` group provides the following adapters, where `I is LendingIterator`:

| Declaration | Guarantee |
| --- | --- |
| `struct Owned<I>` | Owns and forwards to an iterator. |
| `struct Borrowed<I> {source}` | Exclusively borrows and forwards to an iterator. |
| `owned<I>(iterator: I) -> Owned<I>` | Transfers the iterator into an adapter. |
| `borrowed<I>(iterator: uniq/I during source) -> Borrowed<I> during source` | Creates an adapter retaining the receiver Loan. |

Both adapters conform to LendingIterator, IntoIterable and UniqIterable, and to Iterator when I does. They preserve the wrapped item's actual dependencies and exhaustion behavior, without allocation, reference-count updates or collecting items in advance.

### 2.3. Standard Iteration Modes

[Specification: for iteration](spec/14-control-flow.md#1462-iteration-protocol-and-acquisition) and [Slice iteration](spec/04-arrays-indexing-and-slices.md#467-slice-iteration-and-nested-origins).

For an owning collection `values`, a bare subject uses Iterable, `values@uniq` uses UniqIterable, and `values@move` or an owned temporary uses IntoIterable. Below, `a` is the collection borrow and `s` is the Slice's backing source.

| Subject Type | Shared item | Exclusive item | By-value item |
| --- | --- | --- | --- |
| `Array<E>`, `[N of E]` | `ref/E during a` | `uniq/E during a` | `E` |
| `Dictionary<K, V>` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` | `(K, V)` |
| `Slice<E>` | `ref/E during s` | `ref/E during s` | `ref/E during s` |
| `ResolvedRange` | `isize` | `isize` | `isize` |

Sequence iteration uses index order; Dictionary iteration uses insertion order. Dictionary keys remain shared during exclusive iteration. Nested references are not flattened. Owning collection iterators destroy unreturned elements or entries; borrowing iterators do not destroy the collection.

### 2.4. Indexable Contracts

[Specification: Indexable contracts](spec/04-arrays-indexing-and-slices.md#469-indexable-contracts).

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> place ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key) -> place uniq/Element during self
```

These Contracts provide indexed Places for shared access and exclusive access. The key borrow is not a result dependency. Standard out-of-bounds indexing and missing Dictionary keys Abort; optional lookup uses the named `try` operations below.

## 3. Positions, Views and Collections

### 3.1. Index and Ranges

[Specification: Index](spec/04-arrays-indexing-and-slices.md#462-index), [ranges](spec/04-arrays-indexing-and-slices.md#463-range-and-resolvedrange) and [bounds](spec/04-arrays-indexing-and-slices.md#464-bounds-evaluation-and-failure).

Index, Range and ResolvedRange are Copy, Owned and Equatable. None provides Comparable or arithmetic.

| Type | Member | Guarantee |
| --- | --- | --- |
| `Index` | `offset: isize`, `isFromEnd: bool` | Direction and nonnegative distance. |
| | `init(offset: isize, fromEnd: bool = false)` | Negative offsets Abort at construction. `^n` constructs a from-end Index. |
| | `resolve(self: ref/Self, length: isize) -> isize` | Returns a boundary in `[0, length]`; invalid length or bounds Abort. |
| | `tryResolve(self: ref/Self, length: isize) -> Option<isize>` | Returns None for invalid length or bounds. |
| `Range` | `start: Index`, `end: Index`, `isInclusive: bool` | Unresolved boundaries; no target-independent length and no iteration. |
| | `resolve(self: ref/Self, length: isize) -> ResolvedRange` | Resolves and validates the interval; invalid length or bounds Abort. |
| | `tryResolve(self: ref/Self, length: isize) -> Option<ResolvedRange>` | Returns None for invalid length or bounds. |
| `ResolvedRange` | `start: isize`, `end: isize`, `length: isize`, `isEmpty: bool` | Half-open interval with `0 <= start <= end <= isize.MaxValue`; length is end minus start. |
| | `init(! start: isize, end: isize)` | Invalid bounds Abort. |

Range is constructed only by syntax: `a..b`, `a..=b`, `a..`, `..b`, `..=b` or `..`. It has no specified public constructor or factory. Negative integer boundaries Abort immediately; ordering is checked on resolution. Omitted start and end normalize to `0` and `^0`.

`^0` is the end boundary, not an element. Applying a ResolvedRange to storage rechecks that storage's bounds. Equality compares Index direction/offset, normalized Range fields, or ResolvedRange endpoints; it does not compare coincidentally equivalent ranges across Types.

### 3.2. Slice<T> {source}

[Specification: Slice operations](spec/04-arrays-indexing-and-slices.md#466-slice-operations-and-element-results) and [storage and permissions](spec/04-arrays-indexing-and-slices.md#465-slice-storage-lifetime-and-permissions).

A Copy view of contiguous elements with a shared backing Loan. Copying the handle does not copy elements. Elements cannot be written, moved or exclusively borrowed through a Slice.

In the table, `source` means the backing Origin `self.source`, not a borrow of the handle.

| Member | Guarantee |
| --- | --- |
| `length: isize`, `isEmpty: bool`, `indices: ResolvedRange` | Metadata without element access. |
| `s[i]`, where i is `isize or Index` | `place ref/T during source`; invalid element bounds Abort. |
| `s[r]`, where r is `Range or ResolvedRange` | `Slice<T> during source`; invalid range bounds Abort. |
| `tryGet(self: Self, index: isize or Index) -> Option<ref/T during source>` | None for an invalid element index. |
| `trySlice(self: Self, range: Range or ResolvedRange) -> Option<Slice<T> during source>` | None for invalid range bounds. |
| `splitAt(self: Self, index: isize or Index) -> (Slice<T> during source, Slice<T> during source)` | Splits into `[0, p)` and `[p, length)`; invalid boundaries Abort. |
| `trySplitAt(self: Self, index: isize or Index) -> Option<(Slice<T> during source, Slice<T> during source)>` | The same split, returning None for an invalid boundary. |

Split boundaries include zero and length. Both results retain the source dependency, including empty results. Indexable and the three iteration conformances follow §2.

Try-prefixed operations handle only their own bounds failures: `tryGet(-1)` returns None, but `tryGet(^(-1))` Aborts while constructing the argument.

### 3.3. Common Dynamic Collection Rules

[Specification: acquisition](spec/04-arrays-indexing-and-slices.md#471-common-acquisition-and-outcomes), [capacity](spec/04-arrays-indexing-and-slices.md#474-capacity-and-allocation), [Loans](spec/04-arrays-indexing-and-slices.md#475-loans-retained-dependencies-and-call-effects) and [costs](spec/04-arrays-indexing-and-slices.md#477-performance-and-extension-boundary).

Array and Dictionary are Non-Copy owning containers. Their elements may carry borrowed dependencies; there is no blanket Owned constraint. Mutations take an exclusive receiver, even when the operation does nothing. Live element or Slice Loans, including empty views, prevent conflicting mutation, movement or destruction.

Both Types provide:

| Member | Guarantee |
| --- | --- |
| `length: isize`, `capacity: isize` | Element or entry counts with `0 <= length <= capacity`; capacity is not a byte count. |
| `reserve(self: uniq/Self, additional: isize) -> ()` | Ensures capacity for the length at call entry plus additional. Negative input, checked-size failure or required allocation failure Aborts. Never shrinks; sufficient capacity preserves placement; zero is a no-op. |
| `shrinkToFit(self: uniq/Self) -> ()` | May allocate to reduce capacity, keeping it between length and the old capacity. Exact fit and release to the OS are not guaranteed. |

If a shrink candidate's size representation or allocation fails, the operation returns normally with values, order, length, capacity and placement unchanged. This does not catch arbitrary Abort.

Typed empty literals allocate no internal storage; fixed handle-management cost is allowed. Addition within capacity, sufficient-capacity reserve, lookup, replacement, removal, clear, absence and duplicate rejection perform no internal allocation. Arguments, equality and destructors retain their own costs and effects.

Complexity bounds describe collection management for fixed Types. Allocator internals, input generation and user equality/destruction are charged separately.

### 3.4. Array<T>

[Specification: Array operations](spec/04-arrays-indexing-and-slices.md#472-array-operations) and [destruction order](spec/04-arrays-indexing-and-slices.md#476-commit-failure-and-destruction-order).

An ordered, growable sequence constructed with `[]` or `[a, b, ...]`.

| Member | Guarantee |
| --- | --- |
| `indices: ResolvedRange` | The interval `[0, length)`. |
| `values[i]`, where i is `isize or Index` | An element Place: shared for reads, exclusive for mutation; invalid bounds Abort. |
| `values[r]`, where r is `Range or ResolvedRange` | A shared Slice; invalid bounds Abort. |
| `append(self: uniq/Self, value: T) -> ()` | Adds at the end; amortized O(1). |
| `insert(self: uniq/Self, index: isize or Index, value: T) -> ()` | Inserts at a boundary in `[0, length]`, preserving order; invalid bounds Abort. O(1 + n). |
| `pop(self: uniq/Self) -> Option<T>` | Returns the last element, or None when empty. O(1). |
| `remove(self: uniq/Self, index: isize or Index) -> T` | Returns the removed element, preserving order; invalid bounds Abort. O(1 + n). |
| `clear(self: uniq/Self) -> ()` | Destroys elements in reverse index order and retains capacity. |

Capacity operations are in §3.3; indexing Contracts and iteration modes are in §2.

### 3.5. Dictionary<K, V>, where K is Equatable

[Specification: Dictionary operations](spec/04-arrays-indexing-and-slices.md#473-dictionary-operations-and-indexed-replacement) and [literal duplicate keys](spec/12-expressions.md#1234-dictionary-construction-and-duplicate-keys).

An owning map constructed with `[:]` or `[k: v, ...]`. It preserves insertion order. Key equality must remain stable while stored; no public hashing API is required.

| Member | Guarantee |
| --- | --- |
| `dict[key]` | A Place for the existing value; an absent key Aborts. Indexed replacement preserves the stored key and position. |
| `tryInsert(self: uniq/Self, key: K, value: V) -> Result<(), (K, V)>` | Appends a new entry; a duplicate returns both inputs in Err and leaves the collection unchanged. |
| `insertOrReplace(self: uniq/Self, key: K, value: V) -> Option<V>` | A new entry returns None. Replacement returns the old value, retains the stored key and position, and destroys the unused input key. |
| `remove(self: uniq/Self, key: ref/K) -> Option<(K, V)>` | Returns the stored pair, or None when absent. |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref/V during self>` | Returns a shared value reference, or None when absent. |
| `clear(self: uniq/Self) -> ()` | Destroys entries in reverse insertion order, each value before its key; retains capacity. |

Duplicate literal keys in the specification's statically comparable literal forms are compile-time errors. Other duplicates Abort at runtime after evaluating the key, before its value or later entries. Capacity operations are in §3.3; iteration modes are in §2.

## 4. UTF-8 Formatting and Buffers

All lengths and capacities here are byte counts of Type `isize`. [The formatting profile](spec/utf8-formatting.md) defines validation, effects, representations and required costs.

### 4.1. Root Contracts and Error

[Specification: declarations](spec/utf8-formatting.md#1-contracts-and-declarations) and [formatting](spec/utf8-formatting.md#4-formatting).

| Declaration | Member | Guarantee |
| --- | --- | --- |
| `struct BufferFull` | `init()` | Stateless Copy error for insufficient destination capacity. |
| `contract BufferWriter` | `reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>` | Reserves a Window dependent on the receiver borrow. Its effect bound permits only authority supplied through self. |
| `contract Utf8Format` | `format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>` | Writes a complete UTF-8 representation on success, with no output dependency on the input borrow. |

User formatting may allocate or have side effects. Each adapter write calls it once, without a size pre-pass or retry. Success must not abbreviate output to fit.

### 4.2. WriteWindow {source}

[Specification: Window operations](spec/utf8-formatting.md#32-window-operations).

A Non-Copy reservation over a destination's uncommitted suffix. It requires an exclusive source Loan, opts out of ObjectPayload and has no public initializer.

| Member | Guarantee |
| --- | --- |
| `written: isize`, `remaining: isize` (getters, `ref/Self`) | Written bytes and remaining capacity. |
| `push(self: uniq/Self, byte: u8) -> Result<(), BufferFull>` | Appends one byte, or returns BufferFull without changes. |
| `append(self: uniq/Self, bytes: Slice<u8>) -> Result<(), BufferFull>` | Appends the entire Slice, or returns BufferFull without changes. |
| `limit(self: owner/Self, maximum: isize) -> Self` | Consumes the Window and caps capacity at the lesser of its old capacity and maximum; maximum below written Aborts. Preserves the source Loan. |
| `commit(self: owner/Self) -> isize` | Commits the written prefix and returns its length. |

Limiting neither enlarges nor commits. Dropping an uncommitted Window makes no buffer-state change. Window management allocates nothing.

### 4.3. Utf8Writer {target}

[Specification: adapter operations](spec/utf8-formatting.md#23-adapter-operations) and [write failures](spec/utf8-formatting.md#41-failure-and-writes).

A Non-Copy adapter created by `Text.writer`. It exclusively borrows a BufferWriter, erases its concrete Type without owning it, and opts out of ObjectPayload.

| Member | Guarantee |
| --- | --- |
| `write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>`, where `T is Utf8Format` | Formats one value and records the first failure. |
| `status(self: ref/Self) -> Result<(), BufferFull>` | Returns the recorded failure, if any. |

Failure is sticky: later writes skip formatting and reservation, though ordinary call arguments are still evaluated. Earlier committed output and side effects remain. There is no reset; end the adapter's uses and create a new one to resume. Adapter management allocates nothing.

### 4.4. Text Functions and Views

[Specification: Text operations](spec/utf8-formatting.md#2-text-operations) and [UTF-8 views](spec/utf8-formatting.md#22-utf-8-views).

All names in this subsection belong to `Text`.

| Function | Guarantee |
| --- | --- |
| `fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer` | Exclusively borrows an initialized array; length zero, capacity N. The result depends on destination. |
| `heap(capacity: isize) -> HeapBuffer` | Length zero, capacity at least the request; zero starts unallocated. Negative capacity, invalid allocation size or required allocation failure Aborts. |
| `writer<W>(destination: uniq/W) -> Utf8Writer`, where `W is BufferWriter` | Creates an adapter dependent on destination, with no recorded failure. |
| `utf8(text: ref/string) -> Utf8Slice` | Borrows string bytes without allocation, copying or validation; the result depends on text. |
| `validateUtf8(bytes: Slice<u8>) -> Result<Utf8Slice during bytes.source, InvalidUtf8>` | Validates once without allocation or copying. |
| `toString<T>(value: ref/T) -> string`, where `T is Utf8Format` | Formats into an owning string; BufferFull Aborts. Also the standard string-copy operation. |
| `tryFormat<T, length N>(value: ref/T, destination: uniq/[N of u8]) -> Result<Utf8Slice during destination, BufferFull>`, where `T is Utf8Format` | Formats once into the fixed array. Partial bytes may remain on failure. Source Loan rules are in §4.5. |

| Type | Member | Guarantee |
| --- | --- | --- |
| `struct InvalidUtf8` | `init()` | Stateless Copy error for invalid UTF-8. |
| `struct Utf8Slice {source}` | `length: isize` (getter, `self: Self`) | Copy validated view; reports its byte length without owning the source. |
| | `bytes(self: Self) -> Slice<u8> during self.source` | Returns the backing bytes without copying. |

Validation accepts Unicode scalar encodings exactly; it rejects malformed UTF-8 without replacement or normalization. NUL is data. A byte subrange is not automatically a validated view.

### 4.5. Text.FixedBuffer and Text.HeapBuffer

[Specification: standard buffers](spec/utf8-formatting.md#21-standard-buffers), [reservation](spec/utf8-formatting.md#31-reservation-and-growth) and [validation and destruction](spec/utf8-formatting.md#33-validation-and-destruction).

`FixedBuffer {source}` is Non-Copy, retains an exclusive array Loan and opts out of ObjectPayload. `HeapBuffer` is Non-Copy and owns its allocation. Both conform to BufferWriter and are created by Text functions, without public initializers.

| Common member | Guarantee |
| --- | --- |
| `length: isize`, `capacity: isize` (getters, `ref/Self`) | Committed byte count and total capacity. |
| `bytes(self: ref/Self) -> Slice<u8> during self` | Views committed bytes only. |
| `text(self: ref/Self) -> Result<Text.Utf8Slice during self, Text.InvalidUtf8>` | Validates the unvalidated suffix without recording success. |
| `validate(self: uniq/Self) -> Result<(), Text.InvalidUtf8>` | Records validation only on complete success; failure changes nothing. |
| `clear(self: uniq/Self) -> ()` | Resets committed and validated lengths, retaining capacity; erasure is not promised. |
| `reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow during self, BufferFull>` | Negative input Aborts. Success leaves committed data unchanged and returns a Window with written zero and remaining at least minimum. |

FixedBuffer returns BufferFull when unused capacity is insufficient. HeapBuffer grows when necessary; required allocation and size failures Abort rather than returning BufferFull. Zero alone causes no allocation.

| Type | Consuming member | Guarantee |
| --- | --- | --- |
| `FixedBuffer` | `intoText(self: owner/Self) -> Result<Text.Utf8Slice during self.source, Text.InvalidUtf8>` | Validates the remaining suffix, then returns a view of the original source. |
| `HeapBuffer` | `intoString(self: owner/Self) -> Result<string, Text.InvalidUtf8>` | Validates the remaining suffix, then transfers the allocation and sole release responsibility without copying. |

Both conversions consume the buffer on success and failure. Failed intoString destroys the consumed buffer; FixedBuffer never frees its source. An unallocated empty HeapBuffer becomes a Static string. An allocated empty buffer retains its allocation.

Conflicting views, Windows and adapters prevent mutation, movement, growth and destruction. Already validated bytes are not rescanned. Shared results from intoText and tryFormat retain their exclusive source Loan: while live, access to the array is through the returned view.

## 5. Ownership Operations

### 5.1. Whole-Value Updates

[Specification: whole-value updates](spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates). All functions belong to `Intrinsics`.

| Function | Guarantee |
| --- | --- |
| `replace<T>(target: uniq/T ! with => value: T) -> ()` | Destroys the old value in its original slot, then stores the new value. |
| `exchange<T>(target: uniq/T ! with => value: T) -> T` | Stores the new value and returns the old value. |
| `swap<T>(first: uniq/T, second: uniq/T) -> ()` | Exchanges two values. |

Targets must contain complete values; these operations do not repair uninitialized storage. They impose no Copy, Owned or Sealed constraint. Conflicting targets are rejected under ordinary Loan and non-overlap rules.

### 5.2. Object Creation and Strong Handles

[Specification: object ownership](spec/13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing). All functions belong to `Intrinsics`.

| Function | Constraint | Guarantee |
| --- | --- | --- |
| `makeObj<T>(value: T) -> obj/T` | `T is ObjectPayload` | Creates a uniquely owned object. |
| `makeRc<T>(value: T) -> rc/T` | `T is ObjectPayload` | Creates an object with strong count one and non-atomic reference counting. |
| `makeArc<T>(value: T) -> arc/T` | `T is ObjectPayload` | Creates an object with strong count one and atomic reference counting. |
| `clone<S>(value: ref/S) -> S` | S is a valid complete rc/arc handle Type. | Duplicates the strong handle without allocation, retaining the same object and view. |

Ordinary creation does not require Owned; payload dependencies survive in the object handle. Strong clone is not a deep copy or an obj duplicator. The argument borrows the handle slot, as in `Intrinsics.clone(handle@ref)`. Atomic counting alone grants no payload thread-safety guarantee.

### 5.3. Weak Handles and Cyclic Construction

[Specification: Weak values](spec/03-types-and-values.md#322-weak-reference-values) and [Weak operations](spec/13-operators-and-assignment.md#1359-weak-reference-operations).

`struct Weak<S>` is a Non-Copy root Type, where S is a valid complete rc/arc handle Type. It always refers to a target table, has no empty initializer and does not expose the payload directly. Use `Option<Weak<S>>` for absence. Expiration does not erase its Type's dependencies.

The following functions belong to `Intrinsics`:

| Function | Guarantee |
| --- | --- |
| `downgrade<S>(value: ref/S) -> Weak<S>` | Creates a weak handle without retaining the payload strongly; creating the weak table may allocate. |
| `upgrade<S>(value: ref/Weak<S>) -> Option<S>` | Retains and returns the strong handle if the target is alive; otherwise None. |
| `clone<S>(value: ref/Weak<S>) -> Weak<S>` | Duplicates the weak handle. |
| `makeRcCyclic<T, F>(build: F) -> rc/T` | Requires `T is ObjectPayload`, `T is Owned` and `F is Callable<owner, (Weak<rc/T>) -> T>`. |
| `makeArcCyclic<T, F>(build: F) -> arc/T` | Requires `T is ObjectPayload`, `T is Owned` and `F is Callable<owner, (Weak<arc/T>) -> T>`. |

Cyclic construction calls build once. Upgrading its Weak returns None while construction is in progress; the object becomes alive only after the payload and required cleanup are complete. F itself need not be Copy or Owned. Required allocation failure and reference-count overflow Abort.

## 6. Console and Test Utilities

| Function | Guarantee | Specification |
| --- | --- | --- |
| `Console.writeLine(text: ref/string) -> ()` | Writes the exact UTF-8 bytes followed by one LF; no newline conversion. Output failure may leave partial output and Aborts. | [Console output](spec/22-core-execution-and-foreign-functions.md#224-minimal-console-output) |
| `Console.writeLine(text: Text.Utf8Slice) -> ()` | The same output behavior, borrowing the view's bytes without constructing a string. | [View output](spec/utf8-formatting.md#61-console-output) |
| `Test.tempDirectory() -> string` | Returns an independently owned absolute path to the active test case's directory. Available only in test-only bodies; the directory remains available through case cleanup and is reclaimed by the parent afterward. | [Test profile](impl/testing-profile.md#environment-and-temporary-directory) |

## 7. Current Source Differences

The source library currently has the following differences from the required API. These entries record public source interfaces without changing the specification.

| Source interface | Difference and intended treatment |
| --- | --- |
| [Iterator.kimi](Kimi/Library/Iterator.kimi) and [IntoIterable.kimi](Kimi/Library/IntoIterable.kimi) | The source declares Iterator directly with Item and next, and binds IntoIterable.IteratorType to Iterator. The required declarations refine or refer to LendingIterator as shown in §2. |
| `Index.init(! unchecked: isize)` in [Core.kimi](Kimi/Library/Core.kimi) | A normalization helper can admit negative values, unlike specified Index construction. It must not be treated as the specified constructor. |
| `Range.between`, `from`, `to`, `all`, `through`, `upTo` in [Core.kimi](Kimi/Library/Core.kimi) | Public helpers used by syntax lowering. The specified construction interface is range syntax only. |
| `Slice.iterate(self: Self) -> SliceIterator<T> during self.source` and `SliceIterator.init(values: Slice<T> during source)` in [Slice.kimi](Kimi/Library/Slice.kimi) | Source convenience interfaces. The required iteration entries and item guarantees are in §2; no concrete iterator constructor is required. |

`SliceIterator<T> {source}` is the current public concrete Slice iterator. Its `next(self: uniq/Self) -> Option<ref/T during source>` returns elements in order and retains the backing Loan. Source declaration shape and compiler-provided support may differ during implementation; use [STATUS.md](STATUS.md) to assess support.
