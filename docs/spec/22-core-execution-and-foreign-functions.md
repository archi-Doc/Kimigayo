# 22. Kimi, program execution, and foreign functions

[Specification index](../SPEC.md)

This revision defines one executable target profile, windows-x64 ([§21.4](../impl/21-layout-runtime-and-code-generation.md#214-windows-x64-runtime)). Whether a program is valid does not depend on the target except through Conditions ([§19.2](19-compile-time-directives.md#192-conditions)) and the size limits of concrete layouts ([§8.15.3](08-generics-constraints-and-contracts.md#8153-concrete-layout-and-representation)); an Unsupported report may depend on it. Building, running or testing a program for any other target is Unsupported.

## 22.1. Required Kimi declarations

Every Compilation binds exactly one **Kimi Kotonoha** under the reserved direct reference name `Kimi` ([§18.4.1](18-modules-and-dependencies.md#1841-identities-and-reference-names)). `Kimi` is not a keyword: inner scopes may shadow it; `::Kimi` cannot be shadowed. The mandatory default alias opens `Kimi` ([§18.2.3](18-modules-and-dependencies.md#1823-default-aliases)).

A Kimi declaration is recognized by its declaration identity, never by name or by a user conformance; a same-spelled user declaration or a replacement alias never gets its special behavior. A Kimi Kotonoha is compatible when it declares every row below with the stated shape; it may declare more. Whether a compiler accepts a Kimi Kotonoha other than its own is implementation-defined. Every Kimi declaration, including `Kimi.Windows` and `Kimi.Time`, exists on every target.

This table is the only list of required Kimi declarations (names relative to `Kimi`; "the iteration entries" means `Iterable`, `UniqIterable` and `IntoIterable` with the items of [§14.6.2](14-control-flow.md#1462-iteration-protocol)):

| Declaration | Required shape |
| --- | --- |
| `Never` | public, non-open, non-generic struct with no Origins, base or members; the compiler-intrinsic uninhabited value Type of [§3.1.3](03-types-and-values.md#313-unit-and-never) |
| `Option<T>` | enum `Some(T)`, `None` in that order; Copy when `T` is Copy |
| `Result<T, E>` | enum `Ok(T)`, `Err(E)` in that order; Copy when `T` and `E` are Copy |
| `Equatable` | `func equals(self: ref/Self, other: ref/Self) -> bool` |
| `Comparable: Equatable` | `func compare(self: ref/Self, other: ref/Self) -> i32`, negative, zero or positive for less, equal or greater |
| `Copy`, `Owned`, `Callable`, `Sealed`, `ObjectPayload`, `PrimitiveInteger`, `Transferable` | compiler-intrinsic Contracts ([§8.7](08-generics-constraints-and-contracts.md#87-compiler-intrinsic-and-closed-contracts)); `Transferable` is public and has no parameters, parents or members |
| Arithmetic Contracts | the eleven Contracts of [§13.3.3](13-operators-and-assignment.md#1333-arithmetic-contracts) |
| `Position: Equatable, Utf8Format` | closed Contract, `Self is Copy`, `Self is Owned`, `func tryResolve(self: Self, length: isize) -> Option<isize>`; conforming Types exactly the twelve integer Types, `FromEnd<T>`, `Start`, `End` ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions)) |
| `PositionRange: Equatable, Utf8Format` | closed Contract, `Self is Copy`, `Self is Owned`, `func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>`; conforming Types exactly `Range`, `ClosedRange`, `ResolvedRange` ([§4.6.3](04-arrays-indexing-and-slices.md#463-resolution)) |
| `FromEnd<T>`, `Start`, `End` | Copy, Owned, Equatable positions created only by `^x` and omitted range boundaries; `FromEnd<T>` requires `T is PrimitiveInteger` and stores only `offset: T` ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions)) |
| `Range<S, E>`, `ClosedRange<S, E>` | Copy, Owned, Equatable intervals over `S, E is Position`, storing `start: S`, `end: E`, created only by syntax; the iteration entries when `S is PrimitiveInteger` and `E is S` ([§4.6.2](04-arrays-indexing-and-slices.md#462-ranges), [§4.6.3](04-arrays-indexing-and-slices.md#463-resolution)) |
| `ResolvedRange` | Copy, Owned, Equatable validated interval; `length`, `isEmpty`, `resolve`; the iteration entries ([§4.6.2](04-arrays-indexing-and-slices.md#462-ranges)) |
| `Wrapping<T>` | fieldless struct, `T is PrimitiveInteger`, no members; represented as `T` with wrapping arithmetic, the conversions of [§13.5.4](13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions), the conformances of `T` and of [§13.3.3](13-operators-and-assignment.md#1333-arithmetic-contracts); neither `PrimitiveInteger` nor `Position` ([§3.1.1](03-types-and-values.md#311-wrapping-integer-types)) |
| `Array<T>` | Non-Copy owning sequence; `capacity` and the metadata of [§4.7.3](04-arrays-indexing-and-slices.md#473-length-metadata); `UniqIndexable<isize>`; the operations of [§4.8.2](04-arrays-indexing-and-slices.md#482-slice-operations), [§4.8.4](04-arrays-indexing-and-slices.md#484-views-and-uniqslice), [§4.8.5](04-arrays-indexing-and-slices.md#485-disjoint-exclusive-element-pairs), [§4.9](04-arrays-indexing-and-slices.md#49-dynamic-collections); the iteration entries |
| `Dictionary<K, V>` | Non-Copy owning collection, `K is Equatable`; `length`, `capacity`; `UniqIndexable<K>` for existing keys; the operations of [§4.9](04-arrays-indexing-and-slices.md#49-dynamic-collections); the iteration entries |
| `Slice<T> {source}` | Copy shared view over `ref/T during source`; the metadata of [§4.7.3](04-arrays-indexing-and-slices.md#473-length-metadata); the operations of [§4.8.2](04-arrays-indexing-and-slices.md#482-slice-operations); `Indexable<isize>`; the iteration entries |
| `UniqSlice<T> {source}` | Non-Copy exclusive view; `length`, `isEmpty`, `indices`; `UniqIndexable<isize>`; the views of [§4.8.4](04-arrays-indexing-and-slices.md#484-views-and-uniqslice) |
| `Loan<T>` | zero-sized struct over a borrow Type `T` with outer Semantics `ref`, `uniq`, `objref` or `objuniq`; `init(value: T)`; Copy exactly when `T` is ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)) |
| `Weak<s/T>` | Non-Copy struct, `s is rc or arc` ([§13.6.3](13-operators-and-assignment.md#1363-weak-references)) |
| `LendingIterator`, `Iterator`; `Iterable`, `UniqIterable`, `IntoIterable` | [§22.2.1](#2221-iterator-contracts) |
| `Indexable<Key>`, `UniqIndexable<Key>: Indexable<Key>` | `associate Element`; `index(self: ref/Self, key: ref/Key) -> ref/Element during self`; `indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self`; `receiver[key]` designates the referent of the returned reference ([§4.7.1](04-arrays-indexing-and-slices.md#471-index-expression-resolution)) |
| Standard iterators | `ArrayIterator<T>`, `ArrayUniqIterator<T>`, `ArrayOwningIterator<T>`, `FixedArrayOwningIterator<T, A>`, `SliceIterator<T>`, `DictionaryIterator<K, V>`, `DictionaryUniqIterator<K, V>`, `DictionaryOwningIterator<K, V>`, `RangeIterator<T>`, `ClosedRangeIterator<T>` ([§22.2.2](#2222-standard-adapters-and-iterators)) |
| `Iteration` group | `OwningIterator<I>`, `BorrowingIterator<I>`, `owning`, `borrowing` ([§22.2.2](#2222-standard-adapters-and-iterators)) |
| `Intrinsics` group | non-generic; `makeObj`, `makeRc`, `makeArc`, `makeRcCyclic`, `makeArcCyclic`, `clone`, `downgrade`, `upgrade` ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle), [§13.6.3](13-operators-and-assignment.md#1363-weak-references)); `replace`, `exchange`, `swap` ([§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates)) |
| `Raw` group | `allocate`, `release`, `initialize`, `slice` ([§5.6](05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations)) |
| `Async` group | `Cancelled`, `SendFailure`, `Raced` (arities 2–4), `Sender<T>`, `Receiver<T>`, `run`, `sleep`, `checkpoint`, `shield`, `join`, `joinOk`, `race`, `each`, `eachReceived`, `pipe`, `offload`, `offloadEach` ([§22.3](#223-asynchronous-tasks)) |
| `Console` group | `writeLine` ([§22.7](#227-console-output)) |
| `Time` group | `Duration`, `Stopwatch` ([§22.8](#228-time)) |
| `Windows` group | `queryPerformanceCounter`, `queryPerformanceFrequency` ([§22.8.3](#2283-windows-counter-functions)) |
| UTF-8 formatting | `Utf8Format`, `BufferWriter`, `WriteWindow`, `Utf8Writer`, `BufferFull`, `Text` group ([§22.9](#229-utf-8-text-and-buffers)) |
| `Test` group | `tempDirectory() -> string`, only in test-only bodies ([§17.6.1](17-failure-handling.md#1761-forms-and-placement); [§20.6.3](../impl/20-compilation-configuration.md#2063-case-execution)) |

The iteration and indexing Contracts are static; their associated Types are complete and may take Origin parameters ([§8.5.4](08-generics-constraints-and-contracts.md#854-origin-parameters)). `Array`, `Dictionary`, `Slice`, `UniqSlice`, `Wrapping`, `Loan`, `Weak` and `Never` have exactly the constructors their Kimi declarations declare, such as `Array<T>.init(capacity:)` (`Weak`, `Wrapping` and `Never` declare none); none is synthesized ([§6.3.6](06-declarations-and-containers.md#636-implicit-constructor)).

Every fixed array `[N of T]`, for every element Type and length, including a length parameter, has built-in public members (the metadata of [§4.7.3](04-arrays-indexing-and-slices.md#473-length-metadata), the read operations of [§4.8.2](04-arrays-indexing-and-slices.md#482-slice-operations), the views of [§4.8.4](04-arrays-indexing-and-slices.md#484-views-and-uniqslice) and [§4.8.5](04-arrays-indexing-and-slices.md#485-disjoint-exclusive-element-pairs)) and built-in conformances to `UniqIndexable<isize>` and the iteration entries. These cannot be declared in source.

Array is Owned when `T` is, and Dictionary when `K` and `V` are. The keyword `string` denotes the built-in UTF-8 string Type: literals, interpolation, comparison and `Utf8Format`, no concatenation operator ([§13.3.2](13-operators-and-assignment.md#1332-strings-and-raw-pointers)).

**Placement.** Opening `Kimi` makes its groups visible, never their members ([§18.2.3](18-modules-and-dependencies.md#1823-default-aliases)). An alias such as `alias Memory => Kimi.Intrinsics` keeps the declarations' identities. There are no root-level names such as `Kimi.replace` or `Kimi.writeLine`. Built-in operations such as `$abort` and `$tryWrite` belong to no group.

```kimi
let watch = Time.Stopwatch.init()      // Time is visible through the default alias.
Console.writeLine("ready")
// writeLine("ready")                 // Error: Console's members are not opened.
var slot: i32 = 1
let old = Intrinsics.exchange(slot@uniq, with: 2)
// let bad = Kimi.exchange(slot@uniq, with: 3)   // Error: no root-level name.
```

## 22.2. Iteration

### 22.2.1. Iterator Contracts

```kimi
contract LendingIterator
    associate LentItem(step) for uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>

contract Iterator: LendingIterator
    associate Item
    associate LendingIterator.LentItem(step) is Item

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

Every enumeration calls `LendingIterator.next`, which returns the next item as a value; each call binds `step` to that call's receiver borrow. `None` carries no Loan. A LendingIterator may return `Some` after `None`; `for` stops at the first `None`.

- An **Iterator** has one complete `Item` Type that names no Origin of `next`; a conforming Type writes `associate Iterator.Item is T` and `func next(self: uniq/Self) -> Option<T>`.
- A LendingIterator that is not an Iterator may lend items that borrow it: an item such as `ref/E during step` conflicts with the next `next` while retained. An item without `step` follows the ordinary Loan rules ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)).

The three entries are independent; `for` requires the entry that its Subject mode selects, and its item is the `LentItem(step)` of the selected `IteratorType` ([§14.6.2](14-control-flow.md#1462-iteration-protocol)). The borrowing entries keep the per-call `source`; the owning entry keeps the dependencies inside `Self`. A Type may conform to `Iterator` and to entries at once. There is no conformance by member name, no automatic conformance of references or Iterators and no default body. No standard conformance advances an Iterator through a shared borrow.

```kimi
struct Countdown
    Self is Iterator
    associate Iterator.Item is i32
    var remaining: i32 = 3

    public func next(self: uniq/Self) -> Option<i32>
        if self.remaining == 0 => return .None
        self.remaining -= 1
        return .Some(self.remaining)

for number in Kimi.Iteration.owning(Countdown.init()) => use(number)   // 2, 1, 0
// for number in Countdown.init() => use(number)   // Error: Countdown declares no entry.
```

### 22.2.2. Standard adapters and iterators

`Kimi.Iteration` declares two adapters over a LendingIterator `I`. Each result conforms to `LendingIterator`, to `IntoIterable` (itself) and to `UniqIterable` (`BorrowingIterator<Self>`), and to `Iterator` exactly when `I` does, with the same `Item` and dependencies. Adapters keep their input's exhaustion guarantee, allocate nothing and take no item in advance.

- `owning(iterator) -> OwningIterator<I>` takes `I` by value (a Non-Copy Place is written `@move`) and forwards its `LentItem(step)` and dependencies.
- `borrowing(iterator) -> BorrowingIterator<I>` takes `uniq/I` and advances it without moving it; its `source` is the outer Origin of the input; its items keep the contract of `I`, and the `step` of each item is the actual Reborrow of `next`.

**Standard iterators.** The `IteratorType` of each standard entry is one of the ten standard iterator Types of [§22.1](#221-required-kimi-declarations). Each is a Non-Copy Iterator whose `Item` is the item of [§14.6.2](14-control-flow.md#1462-iteration-protocol); `RangeIterator<T>` and `ClosedRangeIterator<T>` require `T is PrimitiveInteger` and are Owned. None has a public constructor or an entry conformance; enumerate an iterator value through `Iteration.owning` or `Iteration.borrowing`. A standard iterator keeps returning `None` after the first `None`; yields each element exactly once, in the order of [§14.6.2](14-control-flow.md#1462-iteration-protocol); never returns overlapping live exclusive items; calls no user code in `next`; when owning, owns the unreturned elements and destroys them in the order of [§4.9.7](04-arrays-indexing-and-slices.md#497-failure-and-destruction-order), and when borrowing, destroys nothing; and meets the bounds of [§4.8.6](04-arrays-indexing-and-slices.md#486-costs) at each step.

```kimi
let values: Array<i32> = [1, 2]
var it = values.iterate()
for x in Kimi.Iteration.borrowing(it@uniq) => use(x)
// for x in values.iterate() => use(x)   // Error: an iterator has no entry conformance.
```

### 22.2.3. Iterator independence

An Iterator's items satisfy the independence of published results ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)): an item depends neither on the receiver Loan of its call nor on the Iterator's Storage. An exclusive item is a child split from the Iterator's authority or a value it transfers out, so moving or freeing the Iterator invalidates no returned item, and an item of an Iterator reached through a borrow keeps no Loan of that borrow.

Keeping earlier items across later calls needs no bound: a later `next` reaches what its receiver Borrow reaches ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use)), and an item keeps no Loan of that Borrow. Every implementation, including user Types that declare `Self is Iterator`, returns nothing contained in its receiver's Origin ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)) and keeps the promise of [§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places) for every item it lends from raw storage. Destroying a whole Iterator is a destruction that may observe a Loan ([§15.6.6](15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking)). Unknown and parameter-dependent effects follow [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls), [§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release) and [§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects). A wrapper that borrows itself for its result is only a LendingIterator.

```kimi
func nextPair<I>(iterator: uniq/I) -> (Option<I.Item>, Option<I.Item>)
    I is Iterator
    let first = iterator.next()
    let second = iterator.next()   // Valid: first keeps no receiver Loan.
    return (first@move, second@move)
```

## 22.3. Asynchronous tasks

`Kimi.Async` provides the asynchronous tasks of [§24](24-suspension-and-asynchronous-tasks.md#24-suspension-and-asynchronous-tasks). Tasks have no handles: no value represents a pending computation, a running child or a suspended task frame. How tasks are implemented (task frames, waiters, the executor and its per-thread state) is implementation-defined; the implementation must provide every guarantee of this section and of [§24](24-suspension-and-asynchronous-tasks.md#24-suspension-and-asynchronous-tasks). Library code that implements it is unsafe code ([§5](05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory)), and a broken guarantee is undefined behavior.

### 22.3.1. Declarations

```kimi
public group Async
    public func run<F, R>(root: F) -> R
        F is Callable<owner, (task;) -> R>
    public func sleep(task; duration: Time.Duration) -> Result<(), Cancelled>
    public func checkpoint(task;) -> Result<(), Cancelled>
    public func shield<F, R>(task; body: F ! grace: Time.Duration) -> R
        F is Callable<owner, (task;) -> R>
    public func join<A, B, RA, RB>(task; first: A, second: B) -> (RA, RB)
        A is Callable<owner, (task;) -> RA>
        B is Callable<owner, (task;) -> RB>
    public func joinOk<A, B, TA, TB, E>(task; first: A, second: B) -> Result<(TA, TB), E>
        A is Callable<owner, (task;) -> Result<TA, E>>
        B is Callable<owner, (task;) -> Result<TB, E>>
    public func race<A, B, RA, RB>(task; first: A, second: B) -> Raced<RA, RB>
        A is Callable<owner, (task;) -> RA>
        B is Callable<owner, (task;) -> RB>
    public func each<I, F>(task; items: I, child: ref/F ! limit: isize) -> Result<(), Cancelled>
        I is Iterator
        F is Callable<(task; I.Item) -> ()>
    public func eachReceived<T, F>(task; items: uniq/Receiver<T>, child: ref/F ! limit: isize)
        -> Result<(), Cancelled>
        F is Callable<(task; T) -> ()>
    public func pipe<P, C, T, R>(task; producer: P, consumer: C ! capacity: isize) -> R
        P is Callable<owner, (task; uniq/Sender<T>) -> ()>
        C is Callable<owner, (task; uniq/Receiver<T>) -> R>

    public struct Cancelled                 // Fieldless; only Kimi creates it.
        Self is Copy
        internal init() => ()
    public enum SendFailure
        Closed
        Cancelled(Async.Cancelled)
    public enum Raced<A, B>                 // The Case names the winner; every final result is kept.
        First(A, B)
        Second(A, B)
    public struct Sender<T>
        public func send(task; self: uniq/Self, value: T) -> Result<(), (T, SendFailure)>
    public struct Receiver<T>
        public func receive(task; self: uniq/Self) -> Result<Option<T>, Cancelled>
```

- **Arity.** `join`, `joinOk` and `race` have exactly one overload for each arity from 2 to 4 children, and `Raced` one declaration for each arity from 2 to 4 with Cases `First`, `Second`, `Third` and `Fourth` in child order, each holding every final result. A call with more children has no candidate; nest calls or use `each`.
- **Children.** A one-shot child (of `join`, `joinOk`, `race`, `pipe` and `shield`) is consumed, so it may have a Shared, Exclusive or Consuming body ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)); a Non-Copy child in a `let` is passed with `@move`. `each` and `eachReceived` borrow their child shared and call it concurrently, so it needs a Shared body.
- **Bounds.** `limit` is the most children running at once; `capacity` is the number of items a channel buffers (`0` is a rendezvous). `limit < 1` and `capacity < 0` Abort with `KIMI_E_ARG_RANGE`.
- **Ownership.** A child's Loans are dependencies of an argument ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)); a one-shot child's Loans last until the operation destroys it and through results that depend on them. Overlapping exclusive captures are ordinary Loan conflicts ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)). Children need no Owned.

```kimi
func loadAll(task;) -> (Config, Users, Rules)
    return Async.join(task; loadConfig, loadUsers, loadRules)   // Three children.
// Async.join(task; f1, f2, f3, f4, f5)   // Error: no overload for five children.
```

### 22.3.2. Running and structured operations

`Async.run(root)` starts `root` as the root of a new task tree whose tasks all run on the current thread, and returns the root's result once every task of the tree has completed. It is the only way for code without a task to call task operations. No task of an enclosing tree runs inside a nested `run`, so an inner tree that waits for an outer task cannot progress. A tree that cannot progress diverges; detecting this is an optional diagnostic, never an Abort. Children may capture `rc` handles; no non-Owned value is guaranteed to be destroyed before its Origins end. A user function that passes its task on is part of the same task.

Every structured operation returns only after all its children complete. At its **decision event** it may cancel the children still running.

| Operation | Decision event | Then | Result |
| --- | --- | --- | --- |
| `join` | Last completion | — | Every result |
| `joinOk` | First `Err`, otherwise last `Ok` | Cancels the others | The first `Err`, or `Ok` of every result; unreturned results are destroyed |
| `race` | First completion | Cancels the others | `Raced` with every final result |
| `each` | Iterator exhausted, last child complete | — | `Ok(())`, or `Err` once a request stops pulling and all children are joined |
| `eachReceived` | Receiver closed and drained, last child complete | — | Likewise, a request stops receiving |
| `pipe` | Consumer completion | Closes the receiver, cancels the producer | The consumer's result |
| `shield` | Body completion | — | The body's result |

`join`, `joinOk` and `race` start their children in argument order, and each child runs until it first suspends or completes before the next starts; a child started after the decision event starts with the cancellation request applied ([§22.3.3](#2233-cancellation-and-waits)). The order in which ready tasks resume is unspecified.

`each` calls `next` only in its own task frame and only when fewer than `limit` children run. Before each pull it checks for a request that applies to it and, once one applies, pulls no more. This check is not a wait and has no commit point. It starts one child per item in order, stops at the first `None`, and destroys the Iterator after its last child completes. `eachReceived` likewise receives only when fewer than `limit` children run, and starts a child with each received item before it receives again.

```kimi
func pick(task;) -> i32
    let a = func [] (task;) -> i32 => 1
    let b = func [] (task;) -> i32 => 2
    match Async.race(task; a, b)
        .First(let x, _) => return x     // Always First: a completes before b starts.
        .Second(_, let y) => return y
```

### 22.3.3. Cancellation and waits

Only Kimi operations request cancellation, for the children they started ([§22.3.2](#2232-running-and-structured-operations)); no value represents a request. A request for a task applies to every **cancellation-observing wait** in that task's subtree, pending now or started later: `sleep`, `checkpoint`, I/O waits, `send` and `receive`. Each observing wait **commits** exactly once, by its operation (table below) or by cancellation, whichever comes first, and returns what it committed to; cancellation is `Err(Async.Cancelled)`, `Err((value, .Cancelled(reason)))` for `send`, or the `Cancelled` Case of a domain error.

- A wait that starts while a request applies commits by cancellation at the call. A request commits a pending wait at once, except an I/O wait.
- A commit is final; a later request, close or counterpart changes nothing. Commits within one task tree are totally ordered.
- A value moves only at a commit: `send` gives its value away only when committing to `Ok(())` and otherwise returns it in `Err`; `receive` removes an item only when committing to `Ok(Some(item))`.
- Cancellation never interrupts, unwinds or destroys a task frame.

| Wait | Its operation commits it when |
| --- | --- |
| `send` | The value leaves the sender: `Ok(())`; the receiver is closed: `Err` with the value and `Closed` |
| `receive` | An item reaches the receiver: `Ok(Some(item))`; the channel is closed and drained: `Ok(None)` |
| `sleep` | Its deadline has passed when the executor resumes it |
| `checkpoint` | At the call when it does not yield, otherwise when it is resumed |
| I/O | At the call when the operation completes immediately, otherwise when its completion is received |

**I/O.** Kimi requires no I/O operation. Each I/O operation an implementation provides takes a task slot and is a cancellation-observing wait. A request does not commit a pending I/O wait; it asks the platform to cancel the operation, and the completion commits the wait: to cancellation when it reports that the operation was cancelled before any data was transferred, otherwise to the outcome it reports. An I/O wait, even a cancelled one, returns only after its completion is received. How completions are delivered is implementation-defined.

- **`checkpoint`** reports a pending request; it yields only when another task is ready or after an implementation-defined number of calls, and reads no clock.
- **`sleep(task; d)`** returns `Ok(())` no earlier than `d` after the call; registering it allocates nothing. `sleep` and `shield` measure monotonic elapsed time, the clock of `Time.Stopwatch`. Deadlines and sums of nested graces are exact: a large `Time.Duration` never Aborts, and a deadline beyond the clock's range is never reached.
- **`shield(task; body, grace: g)`** runs `body` as a child while other tasks keep running. A request that applies to the caller reaches the body's subtree only once `g` has elapsed since the later of the request and the start of the call; requests made inside the body apply as usual. The caller's request stays pending for its next observing wait. Nested shields add their graces.
- **Effects.** A Kimi operation that runs children (`run`, `shield`, `join`, `joinOk`, `race`, `each`, `eachReceived`, `pipe`, `offload`, `offloadEach`) has in its effect summary the effects of calling each child through its Callable input, parameter-relative ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)), including every part that runs after a suspension. Apart from those child effects, the effects of the Iterator that `each` steps and destroys, and the destruction of values the operation owns, `sleep`, `checkpoint`, `shield`, `join`, `joinOk`, `race`, `each`, `eachReceived`, `pipe`, `send` and `receive` have no environment effect and may be used in `confined` implementations. `run` accesses per-thread executor state, an environment effect, so it is never `confined`. I/O waits are environment effects.
- **Other rules.** Converting `Async.Cancelled` into a domain error is explicit ([§17.3](17-failure-handling.md#173-try-propagation)). A timeout is `race` with `sleep`; the other child ends only at an observing wait. A Deferred Block cannot pass the task ([§24.2.2](24-suspension-and-asynchronous-tasks.md#2422-passing)), so a waiting finalization is an explicit call, made through `shield` to run even under cancellation.

```kimi
func flushAlways(task; log: uniq/Log) -> Result<(), LogError>
    let finish = func [log] (task;) -> Result<(), LogError> => log.flush(task;)
    let grace = Time.Duration.init(microseconds: 2000000)
    return Async.shield(task; finish@move, grace: grace)   // Up to 2 s more under cancellation.
```

### 22.3.4. Channels

`pipe` is the only way to create a channel; it lends one endpoint to each child as `uniq`. With `capacity >= 1` it buffers items without heap allocation in steady state. When the consumer completes, `pipe` closes the receiver and cancels and joins the producer, whose pending `send` commits to `Closed`. When the producer returns first, the sender closes and `receive` returns the buffered items, then `Ok(None)` on every later call. `Ok` from `send` means the channel took the value, not that the consumer received it. A delivered item depends on neither the receiver borrow nor the channel storage. Endpoints interleave only at task calls ([§24.3](24-suspension-and-asynchronous-tasks.md#243-task-calls)).

Delivery is FIFO in the commit order of successful sends. Before `pipe` returns, remaining buffered items are destroyed in reverse successful-send commit order, each completely before the next. These orders are independent of task resumption order.

There are no detached tasks: dynamic work uses `eachReceived` with erased jobs of Type `(task;) -> ()` ([§7.7.4](07-functions-and-callable-values.md#774-function-references-and-erasure)). A generator is `pipe` with capacity 0.

```kimi
func sumEvens(task; limit: u64) -> Result<u64, Async.Cancelled>
    let produce = func [limit] (task; out: uniq/Async.Sender<u64>) -> ()
        var n: u64 = 0
        while n < limit
            match out.send(task; n)
                .Ok(_) => ()
                .Err(_) => exit                 // The consumer finished, or cancellation.
            n += 2
    let consume = func [] (task; inbox: uniq/Async.Receiver<u64>) -> Result<u64, Async.Cancelled>
        var total: u64 = 0
        loop
            match try inbox.receive(task;)
                .Some(let value) => total += value
                .None => return .Ok(total)
    return Async.pipe(task; produce, consume, capacity: 0)
```

### 22.3.5. Sequences and blocking calls

An asynchronous sequence is read by repeating a task call in a `loop`; `for` is unchanged, since `next` has no task slot ([§14.6.2](14-control-flow.md#1462-iteration-protocol)). Apart from `Async.run`, every Kimi operation that may wait has a task slot and no task-free counterpart, except `Console.writeLine`, which blocks the thread and runs no other task meanwhile.

### 22.3.6. Structured worker offload

Computation runs on other threads only through these operations:

```kimi
public group Async
    public func offload<F, R>(task; child: F) -> R
        F is Callable<owner, () -> R>
            effect confined
        F is Transferable
    public func offloadEach<I, F>(task; items: I, child: ref/F ! limit: isize) -> Result<(), Cancelled>
        I is Iterator
        I.Item is Transferable
        F is Callable<(I.Item) -> ()>
            effect confined
        F is Transferable
```

- A worker child has no task slot, never runs a task, and neither observes cancellation nor unwinds; a timeout cannot preempt it. `offload` acquires its child by value; `offloadEach` borrows it shared and passes each item by value, keeping its Iterator on the parent thread, and otherwise follows `each` ([§22.3.2](#2232-running-and-structured-operations)).
- Both operations keep the child, its arguments and their Loans until the worker finishes, and join on every path before returning; no partially started operation returns while a child is live. A failure to start a worker Aborts with `KIMI_E_THREAD`.
- `confined` ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)) excludes environment authority, such as mutable statics, foreign calls and Console output, and lets a worker and every callee in its transitive summary read an immutable static only as that section allows. The static Loans of the operation's inputs are checked separately ([§24.3](24-suspension-and-asynchronous-tasks.md#243-task-calls), [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). `Transferable` ([§15.2.4](15-ownership-and-lifetime-analysis.md#1524-transferable)) excludes thread-unsafe inputs.
- The result needs no Transferable or Owned bound; it is delivered after join. Abort in a worker follows [§17.4.3](17-failure-handling.md#1743-termination-and-cleanup).

### 22.3.7. Data races

Two accesses **conflict** when they touch overlapping Places ([§3.4](03-types-and-values.md#34-places-and-storage)) or the reference counts of one object ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)), at least one of them writes, and they are not both atomic accesses made by the implementation (such as `arc` count updates). Takes, destructions and placements count as writes; a relocation also writes its source Places, and a release writes every Place in its storage. A **data race** is a pair of conflicting accesses from different threads that happens-before does not order. Happens-before is per-thread sequencing plus these edges, closed transitively:

- everything the parent does before a worker child starts happens before that child's accesses;
- everything the worker child does, including destruction of the child value and construction of its result, happens before the parent continues after join;
- publishing an Initialized static slot happens before every access that observes it ([§22.4.3](#2243-static-initialization-and-shutdown)).

The `arc` count protocol ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)) orders handle state and lifetime only, not payload accesses. A data race is undefined behavior; safe code cannot cause one: Loan checking and `Transferable` admit no conflicting accesses between threads outside these edges. Loans create no ordering at runtime; they only forbid conflicting accesses. Other threading features are outside this revision ([§D.5](appendices/D-deferred-features.md#d5-concurrency)).

## 22.4. Program startup and static storage

### 22.4.1. Startup selection

An Application has exactly one startup form, judged on the current project's root items after Condition selection and source generation: one SourceDocument with top-level runtime body items, or one root-level `public func main() -> ()`. Mixed forms, several candidates and no candidate are errors. Runtime body items are expressions, control expressions, `unsafe`, `defer`, `require` and discard statements, and local `let`/`var`, even without an initializer; declarations, aliases and excluded syntax are not, and a generated item counts as the item it produces. A minimal empty Application is the Unit expression `()`.

The chosen document's body items run once, in source order, in the document's execution scope ([§6.1.1](06-declarations-and-containers.md#611-source-root-and-nested-containers)); its top-level bindings are document-local, not static Properties, and the body has no return target ([§14.5.2](14-control-flow.md#1452-targets-and-barriers)).

### 22.4.2. Explicit main and Library

An explicit main is exactly `public func main()` at the source root ([§6.1.1](06-declarations-and-containers.md#611-source-root-and-nested-containers)): a safe function with a body, a Unit result (which may be omitted), and no task slot, parameters, receiver, generic or Origin parameters; not an import or a specialization. Every root-level `public main` of an Application is checked against this; an invalid one is an error. `public` exports no native symbol. Normal completion ends the process with a successful exit status; Abort follows [§17.4.3](17-failure-handling.md#1743-termination-and-cleanup).

A Library needs no startup form and never calls `main` automatically; there `main` is an ordinary function, and top-level runtime body items are errors.

```kimi
public func main() -> ()
    let message = "Hello, world!"
    Console.writeLine(message)          // message is borrowed and stays usable.
// Console.writeLine("Top level")       // Error: mixes the two startup forms.
```

### 22.4.3. Static initialization and shutdown

Static stored Properties ([§11.8](11-properties.md#118-static-storage)) initialize per slot on first access. A program starts on one thread; only structured worker offload ([§22.3.6](#2236-structured-worker-offload)) runs code on other threads. A slot is Not started, Initializing, Initialized or Finished, and every read, Borrow, write or other access checks its state:

| State | Action |
| --- | --- |
| Not started | Before shutdown: exactly one accessing thread marks the slot Initializing and runs the initializer; after normal completion it marks the slot Initialized, then performs the access. During shutdown: Abort with `KIMI_E_STATIC_SHUTDOWN` |
| Initializing | Accessed by the initializing thread: Abort with `KIMI_E_STATIC_CYCLE`. Accessed by another thread: wait until the slot is Initialized; a cross-thread wait cycle may deadlock |
| Initialized | Perform the access |
| Finished | Abort with `KIMI_E_STATIC_SHUTDOWN` |

A first write initializes before it replaces. Only accesses that execute initialize: a Type or function reference, an untaken branch or an effect summary initializes nothing. Every initializer is checked even if unused; an uninitialized slot is not destroyed. These rules apply to the slots of every Kotonoha, dependencies included.

After the startup body completes and its locals are cleaned up, shutdown destroys every Initialized static `var` slot and then every Initialized static `let` slot, each phase in reverse order of the moment each slot became Initialized (one order across all threads), marking each slot Finished before destroying its value. When destroying a static `let` slot accesses another static `let` slot, the access succeeds if that slot finished initialization earlier (it is destroyed later), and otherwise Aborts with `KIMI_E_STATIC_SHUTDOWN`; the outcome depends on the run-time initialization order. An Abort during shutdown follows [§17.4.3](17-failure-handling.md#1743-termination-and-cleanup).

```kimi
group Registry
    public let names: Names = Names.init()
    public let probe: Probe = Probe.init()   // Probe's drop reads Registry.names.
// Initialized names, then probe: probe is destroyed first and its drop succeeds.
// Initialized probe, then names: names is Finished first, so probe's drop Aborts.
```

### 22.4.4. Static storage keys

A Field directly in a group is static even when the group is nested in a struct. Its **static storage key** is the Field declaration plus the enclosing Type arguments with Origins erased; Semantics layers and unused arguments still distinguish keys. Loan checking and effect summaries do not: all keys of one Field are one root ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). Each key has one slot, with one initialization state and one destruction, whatever path reaches it. The stored value must be Owned and meet the static-storage conditions ([§11.8](11-properties.md#118-static-storage)); the enclosing Type arguments need not be Owned, and access keeps the full Origins for Loan and lifetime checking. The initializer and destruction must be valid for every Origin binding that shares the key (universal checking, [§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)); otherwise the declaration is an error. The rules of [§22.4.3](#2243-static-initialization-and-shutdown) apply.

```kimi
struct Cache<T>
    public group Statistics
        public var count: i32 = 0
// Cache<i32>.Statistics.count and Cache<string>.Statistics.count are separate slots.
// Cache<ref/i32 during a> and Cache<ref/i32 during b> share one slot.
```

## 22.5. Abort locations and codes

The Abort contract is [§17.4.3](17-failure-handling.md#1743-termination-and-cleanup); the windows-x64 output format is in [§21.4.2](../impl/21-layout-runtime-and-code-generation.md#2142-process-entry-and-exit).

### 22.5.1. Abort location

An Abort reports one source location, the same at every optimization level and instantiation:

- a failure inside a Kimi operation (a public function, accessor or member of the Kimi Kotonoha) that user code invokes by a call or by syntax (indexing, slicing, literals), including the operation's internal code, allocations, static-storage accesses and `$abort`: the start of the invoking user expression; for a call through a function value, that call, not where the value was formed; for a Dictionary literal entry's insertion, its allocation and a duplicate key included, the start of that entry's key expression, so a duplicate reports the later key expression;
- user code that a Kimi operation calls, such as `equals` or `drop`: that code's own location;
- an implicit iteration entry or `next` call of `for`: the start of the `for` expression;
- an arithmetic, shift or conversion check in user code: the start of the failing expression; for compound assignment and `++`/`--`, the whole update expression;
- a static-storage Abort in user code: the start of the Place expression that designates the slot, including its qualifier (`Cache<i32>.Statistics.count` reports `Cache`);
- `$abort` in user code: the `$abort` expression;
- allocation of an erased function value: the conversion expression.

```kimi
group Values
    public let first: i64 = Values.second + 1
    public let second: i64 = Values.first + 1   // Reading Values.first finds it Initializing: KIMI_E_STATIC_CYCLE, reported at `Values`.
let value = Values.first
```

```kimi
let items: Array<i32> = [1]
let x = items[5]          // KIMI_E_INDEX_BOUNDS at the start of items[5], not inside Kimi code.
```

### 22.5.2. Abort codes

Every Abort cause of [§17.4.1](17-failure-handling.md#1741-causes) and of the Kimi operations maps to exactly one row of this closed table.

| Code and reason | Cause |
| --- | --- |
| `KIMI_E_INT_OVERFLOW: Integer overflow` | A checked integer result is not representable, including the signed minimum divided by -1 |
| `KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero` | Integer `/` or `%` by zero, also for wrapping Types |
| `KIMI_E_INT_SHIFT_COUNT: Shift count out of range` | Shift count outside `0 <= count < bit width` |
| `KIMI_E_INT_CONVERSION: Integer conversion out of range` | Runtime integer conversion outside the target, or float-to-integer failure including NaN and infinities |
| `KIMI_E_FLOAT_CONVERSION: Floating conversion out of range` | A finite source rounds to floating infinity |
| `KIMI_E_INDEX_BOUNDS: Index out of bounds` | Any failed resolution of a position or range: indexing, slicing, `resolve`, and positions of `insert`/`remove` |
| `KIMI_E_MISSING_KEY: Dictionary key was not found` | Dictionary indexing with an absent key |
| `KIMI_E_DUPLICATE_KEY: Dictionary literal contains an equivalent key` | Runtime duplicate key in a Dictionary literal |
| `KIMI_E_ARG_RANGE: Argument out of range` | A numeric argument out of range: negative count, capacity or length, `limit < 1`, `capacity < 0`, iteration over a reversed range |
| `KIMI_E_FORMAT: Formatting failed` | Formatting failure ([§22.10](#2210-formatting-and-interpolation)) |
| `KIMI_E_REF_COUNT: Reference count limit exceeded` | Strong or weak count at its maximum in `clone`, `downgrade` or `upgrade` |
| `KIMI_E_STATIC_CYCLE: Static initialization cycle` | Access to a slot that the same thread is initializing |
| `KIMI_E_STATIC_SHUTDOWN: Static storage unavailable during shutdown` | Access to a Finished slot, or a first access during shutdown |
| `KIMI_E_ALLOC_SIZE: Allocation size exceeds limit` | A requested size overflows or exceeds the maximum object size |
| `KIMI_E_ALLOC: Failed to allocate memory` | The allocator returns no memory |
| `KIMI_E_FREE: Failed to free memory` | A detected release failure |
| `KIMI_E_PROCESS_HEAP: Failed to obtain process heap` | The runtime heap is unavailable |
| `KIMI_E_STDOUT: Failed to write to stdout` | Standard output fails |
| `KIMI_E_STRING_RELEASE: Invalid string release kind` | A detected invalid string representation during destruction (detection not guaranteed) |
| `KIMI_E_CLOCK: Elapsed-time counter failed` | The elapsed-time counter fails, reports a nonpositive frequency or goes backward |
| `KIMI_E_THREAD: Failed to start worker` | A worker cannot be started ([§22.3.6](#2236-structured-worker-offload)) |
| `KIMI_E_REQUIRE: <reason>` | The Abort of a false `$require` ([§17.6.2](17-failure-handling.md#1762-evaluation-and-failure)); the reason is its evaluated `message:` unchanged, or `Test requirement failed` when omitted |
| `KIMI_E_ABORT: <message>` | Explicit `$abort(message)`; the message is written unchanged |

## 22.6. Foreign function imports

`#LibraryImport("library", "symbol")` on a bodyless `func`, safe or unsafe, declares a C function. Both arguments are nonempty, non-interpolated string literals without NUL: a case-sensitive logical native library name of the defining Kotonoha, resolved when the program is built ([§20.4](../impl/20-compilation-configuration.md#204-native-libraries)), and the exact external symbol.

An import is declared directly in a group or rootgroup, or as a receiverless struct type function, and has no task slot, receiver, parameter default, varargs, specialization or body. An import must not be generic: neither the import nor any enclosing Container may declare a generic parameter of any kind (Type, Semantics, length or Origin, [§6.1.3](06-declarations-and-containers.md#613-inherited-environments-and-declaration-references)), even an unused one; Origins introduced by borrow annotations in its own signature are allowed, and a raw layer may state one of them ([§5.7](05-raw-pointers-and-unsafe-memory.md#57-raw-pointer-origins)) as part of the import's safety contract. Every argument is required; imports are only called directly, never used as function values.

The declaration promises that, for every call meeting its signature and `- safety:` conditions, the foreign function behaves as a Kimigayo function with that signature would: it accesses only what its arguments permit, and only during the call: reads through `ref`, exclusive access through `uniq`, and through `raw/T` what its `- safety:` conditions allow; stores only valid values, constructs or overwrites only values whose Type has no user `drop`, recursively (a relocation, [§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places), moves values and does neither), retains no borrow beyond its result's Origins, returns a valid result, and lets no unwinding, non-local jump, callback or reentry cross Kimigayo frames. A false promise is undefined behavior. An import is `unsafe` only when callers have obligations its Types cannot express ([§7.6](07-functions-and-callable-values.md#76-unsafe-functions)). A call's effects are the accesses its signature permits plus an environment effect ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)). Arguments are evaluated once, left to right, and passed without marshalling, retention or ownership transfer; the floating-point boundary contract of [§21.2](../impl/21-layout-runtime-and-code-generation.md#212-foreign-function-abi) applies.

| Parameter or result Type | C meaning |
| --- | --- |
| `i8`…`i64`, `u8`…`u64`, `f32`, `f64` | `int8_t`…`int64_t`, `uint8_t`…`uint64_t`, `float`, `double` |
| `raw/T` | data pointer; `T` need not be C-exchangeable |
| `ref/T`, `uniq/T` | non-null `const T *`, `T *`; `T` C-exchangeable |
| `Option<ref/T>`, `Option<uniq/T>` | pointer, `None` is null |
| Unit (result only) | `void` |

A C-exchangeable Type is a fixed-width integer, `f32` or `f64`, `raw/T`, `ref/T` or `uniq/T` over a C-exchangeable referent and their `Option`, a positive-length fixed array of C-exchangeable elements, or a C-layout struct ([§21.1.2](../impl/21-layout-runtime-and-code-generation.md#2112-layout-modes)) whose Fields are all C-exchangeable. A parameter or result whose Type has no C counterpart — `string`, dynamic collections, object handles, function and Closure values, and borrows of such Types — is an error. Any other Type outside the table — `bool`, `char`, `isize`/`usize`, `i128`/`u128`, Tuples, enums, structs or fixed arrays passed by value, and borrows of a referent that is not C-exchangeable — is Unsupported.

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    public func observe(record: uniq/NativeRecord) -> ()     // C: void observe_record(NativeRecord *);

    #LibraryImport("observer", "observe_raw")
    public unsafe func observeRaw(record: raw/NativeRecord) -> ()   // - safety: states layout, lifetime, retention.

    // #LibraryImport("observer", "print")
    // public func print(text: ref/string) -> ()   // Error: string has no C counterpart.
```

## 22.7. Console output

The group `Kimi.Console` declares two safe, non-generic overloads, `writeLine(text: ref/string) -> ()` and `writeLine(text: Text.Utf8Slice) -> ()`. Overload resolution picks the string overload for strings and the view overload for views; there is no implicit conversion, and a function reference without an expected Type is ambiguous. Neither overload has a task slot: the call is synchronous ([§22.3.5](#2235-sequences-and-blocking-calls)).

The text is borrowed for the call: a string Place stays usable, and a literal or interpolation is a borrowed temporary. Each call writes all UTF-8 bytes of the text and one LF to standard output, with no normalization, line-ending or encoding conversion; NUL is data. It returns after the host has accepted the bytes, not necessarily after display or durable storage. Failure Aborts with `KIMI_E_STDOUT` and may leave partial output. The call destroys nothing.

```kimi
Console.writeLine("Hello, world!")       // Writes "Hello, world!" and LF; exits successfully.
::Kimi.Console.writeLine("Hello")
// writeLine("Hello")                    // Error: Console is not opened.
```

## 22.8. Time

The group `Kimi.Time` measures monotonic elapsed time. No civil-clock adjustment applies; the clock source and its resolution are implementation-defined. Time operations allocate nothing.

### 22.8.1. Duration

`Time.Duration` is a Copy interval of whole microseconds, not a date or timestamp. `init(! microseconds: u64)` creates the exact value (every `u64` is representable), read back through `rawMicroseconds: u64`. The getters `seconds`, `milliseconds` and `microseconds: f64` are `rawMicroseconds@f64` divided by 1,000,000, 1,000 and 1 ([§13.5](13-operators-and-assignment.md#135-explicit-operations)).

### 22.8.2. Stopwatch

`Time.Stopwatch` is Non-Copy. It holds accumulated whole microseconds and, while running, the start of its current interval; the read-only `isRunning: bool` reports whether it runs.

| Declaration | Behavior |
| --- | --- |
| `init()`, `reset(self: uniq/Self) -> ()` | Zero, stopped; reads no clock |
| `start(self: uniq/Self) -> ()` | Starts a new interval when stopped; no effect when running |
| `stop(self: uniq/Self) -> ()` | Adds the current interval and stops; no effect when stopped |
| `restart(self: uniq/Self) -> ()` | Zero, then starts a new interval |
| `elapsed(self: ref/Self) -> Duration` | Accumulated time plus the running interval, without mutation; reads no clock when stopped |

For a counter difference `d` and counter frequency `f`, an interval contributes exactly `floor(d × 1,000,000 / f)` microseconds, computed without intermediate overflow when the result fits `u64`. `stop` discards the interval's sub-microsecond fraction; no fraction carries between intervals. Accumulating beyond `u64` Aborts with `KIMI_E_INT_OVERFLOW`; a clock failure Aborts with `KIMI_E_CLOCK`.

```kimi
var watch = Time.Stopwatch.init()
watch.start()
// Measured work.
watch.stop()
let seconds: f64 = watch.elapsed().seconds
```

### 22.8.3. Windows counter functions

The group `Kimi.Windows` declares two ordinary imports ([§22.6](#226-foreign-function-imports)). They exist on every target; their imports resolve only when a program is built for windows-x64.

```kimi
public group Windows
    #LibraryImport("kernel32", "QueryPerformanceCounter")
    public func queryPerformanceCounter(value: uniq/i64) -> i32

    #LibraryImport("kernel32", "QueryPerformanceFrequency")
    public func queryPerformanceFrequency(value: uniq/i64) -> i32
```

Each returns the native flag (nonzero is success) and writes a count, or counts per second, into the borrowed `i64`. They are safe, allocate nothing and do not Abort.

## 22.9. UTF-8 text and buffers

Lengths and capacities in this section and [§22.10](#2210-formatting-and-interpolation) are byte counts of Type `isize`.

### 22.9.1. Declarations

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        effect confined

contract Utf8Format
    func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
```

Both are ordinary, user-implementable static Contracts; `Utf8Format` is the only formatting Contract, and formatting has no options, locales or runtime format strings. The result Origin of `reserve` elides to `self`. The outer borrow of a `Utf8Writer` and its `target` are independent input Origins; the target must outlive the outer borrow.

- `BufferFull` (in `Kimi`) and `Text.InvalidUtf8` are stateless Copy structs with a public `init()`.
- `WriteWindow {source}` and `Utf8Writer {target}` (in `Kimi`) and `Text.FixedBuffer {source}` are Non-Copy structs with a `Loan<uniq/u8 during source>` (or `target`) Field ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)) and are not ObjectPayloads.
- `Text.HeapBuffer` is a Non-Copy struct owning a growable allocation; `Text.Utf8Slice {source}` is a Copy view of valid UTF-8 with required Loan `ref`.

`WriteWindow`, `Utf8Writer`, `FixedBuffer`, `HeapBuffer` and `Utf8Slice` have no public constructor; they are obtained only through the operations of [§22.9.2](#2292-text-operations) and [§22.9.4](#2294-reservation-and-windows).

Only `Text.fixed`, `Text.writer` and `reserve` form these Loans; windows, views and adapters allocate nothing.

`BufferWriter.reserve` has the `confined` bound ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)). `Utf8Writer` erases the Writer Type, so `format` is not generic over it. The adapter calls only the Writer's `reserve`, never owns, moves or exposes the Writer, and the Writer keeps its Loans; an erased `reserve` call is checked against the `confined` bound ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use)).

### 22.9.2. Text operations

```kimi
group Text
    public func fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer
    public func heap(capacity: isize) -> HeapBuffer
    public func writer<W>(destination: uniq/W) -> Utf8Writer
        W is BufferWriter
    public func utf8(text: ref/string) -> Utf8Slice
    public func validateUtf8(bytes: Slice<u8>) -> Result<Utf8Slice during bytes.source, InvalidUtf8>
    public func toString<T>(value: ref/T) -> string
        T is Utf8Format
    public func tryFormat<T, length N>(value: ref/T, destination: uniq/[N of u8])
        -> Result<Utf8Slice during destination, BufferFull>
        T is Utf8Format
```

- `fixed` borrows an initialized array: length zero, capacity `N`. `heap` has length zero and at least the requested capacity; zero allocates nothing, and a negative capacity Aborts with `KIMI_E_ARG_RANGE`. `writer` creates an adapter with no recorded failure.
- `utf8` views a string's bytes; `validateUtf8` validates bytes once; neither allocates or copies.
- `toString` formats into a new string, Aborting with `KIMI_E_FORMAT` on `BufferFull`; it is also how a string is copied.
- `tryFormat` formats once into the array, without a pre-pass or retry. The result keeps the exclusive Loan of `destination`, so while it is live the array is read only through the view. On `BufferFull` the array may hold partial bytes.

```kimi
var bytes: [3 of u8] = [3 of 0]
let result = Text.tryFormat(123, bytes@uniq)
// let first = bytes[0]  // Error: result still holds the exclusive Loan.
match result@move
    .Ok(let text) => Console.writeLine(text)
    .Err(_) => Console.writeLine("too small")
```

### 22.9.3. Buffers and validation

`FixedBuffer` and `HeapBuffer` implement `BufferWriter`. Each has a committed `length`, a `capacity` (getters) and a validated length, with `0 <= validated <= length <= capacity`; committed bytes are initialized, and the first `validated` bytes are valid UTF-8.

| Operation | Receiver | Behavior |
| --- | --- | --- |
| `bytes()` | `ref/Self` | `Slice<u8>` of the committed bytes |
| `text()` | `ref/Self` | `Result<Utf8Slice, InvalidUtf8>`; validates the unvalidated suffix, records nothing |
| `validate()` | `uniq/Self` | `Result<(), InvalidUtf8>`; records validation only on complete success |
| `clear()` | `uniq/Self` | Sets both lengths to zero and keeps capacity |
| `intoText()` (FixedBuffer) | `owner/Self` | `Result<Utf8Slice during self.source, InvalidUtf8>`; validates the suffix |
| `intoString()` (HeapBuffer) | `owner/Self` | `Result<string, InvalidUtf8>`; validates the suffix and transfers the allocation without copying |

`intoText` keeps the exclusive Loan of the original array, so it may return a view of the caller's array but never lets a local array escape. A raw `commit` leaves the validated length unchanged; an adapter commit advances it only when `validated == length` at reservation, so writing only through adapters needs no later scan.

Validation accepts exactly the UTF-8 encodings of Unicode scalar values and rejects truncated sequences, overlong encodings, surrogates and out-of-range values, with no replacement or normalization; NUL is data. A `Utf8Slice` has `length` and `bytes() -> Slice<u8> during self.source`. `FixedBuffer`, `WriteWindow` and `Utf8Writer` have no `drop`; `HeapBuffer` frees its allocation once.

### 22.9.4. Reservation and windows

A successful `reserve(minimum)` returns a window with `written == 0` and `remaining >= minimum` and changes no committed byte; a negative `minimum` Aborts with `KIMI_E_ARG_RANGE`. `FixedBuffer` returns `BufferFull` exactly when its unused capacity is too small. `HeapBuffer` never returns `BufferFull`: if `length + minimum` exceeds the maximum object size it Aborts with `KIMI_E_ALLOC_SIZE`; if it exceeds the capacity, the buffer grows to `max(length + minimum, 2 * capacity)` (to `length + minimum` when `2 * capacity` exceeds the maximum object size), copying the committed bytes.

| Window operation | Behavior |
| --- | --- |
| `written`, `remaining` | Bytes written and capacity left |
| `push(byte: u8)`, `append(bytes: Slice<u8>)` | `Result<(), BufferFull>`; fits completely or changes nothing |
| `limit(maximum: isize)` (`owner/Self`) | The window with capacity `min(capacity, maximum)`; `maximum < written` Aborts with `KIMI_E_ARG_RANGE` |
| `commit()` (`owner/Self`) | Commits the written prefix and returns its length |

A window writes contiguously from its start; dropping it uncommitted changes nothing. A user Writer forwards or limits the windows of a standard buffer; the adapter checks each user window (`written == 0`, `remaining >= minimum`) and on violation drops it and records `BufferFull`.

```kimi
struct Limited
    Self is BufferWriter
    var inner: Text.HeapBuffer
    let maximum: isize

    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        let remaining = self.maximum - self.inner.length
        if minimum > remaining => return .Err(BufferFull.init())
        let window = try self.inner.reserve(minimum)
        return .Ok(window@move.limit(remaining))
```

## 22.10. Formatting and interpolation

### 22.10.1. Writer adapter

`Utf8Writer` has `write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>` with `T is Utf8Format`, and `status(self: ref/Self) -> Result<(), BufferFull>`, which returns the recorded failure. Every adapter `write`, including those of interpolation and `Text` operations, returns the recorded failure without formatting if there is one; otherwise it calls `format` once and, if `format` returns `BufferFull` or a nested write recorded one, records and returns the first failure, so a formatter cannot hide a failed write. There is no reset: after a failure, end the adapter's uses, optionally `clear` the buffer and create a new adapter. Committed output is never rolled back. A direct call to `format` has no such guard. Passing an interpolated literal to `write` first builds an owning string.

`format` borrows its input, and its output depends on neither that borrow nor the Writer. Successful output is the complete representation, the same for the same value and external state whatever the destination; a Writer reports `BufferFull` rather than abbreviating. User formatting may have effects and runs once. Built-in formatting reserves the exact encoded length once (nothing for empty output) and commits complete UTF-8, so `123` fits a three-byte destination.

### 22.10.2. Default representations

| Value | Representation |
| --- | --- |
| Integer, `Wrapping<T>` | ASCII decimal, `-` only for negatives, no leading zeros |
| `bool`, `char`, Unit | `true`/`false`; the UTF-8 encoding; `()` |
| `string`, `Utf8Slice` | Their bytes, including NUL |
| `FromEnd<T>`, `Start`, `End` | `^` and the offset (`^1`, `^-1`); empty; empty |
| `Range`, `ClosedRange`, `ResolvedRange` | Start, `..` or `..=`, end: `1..^1`, `..=3`, `2..`, `1..4` |

A finite nonzero float uses the fewest significant digits that round back to the same value in its own width (nearest-even); ties go to the closest candidate, then to an even last digit. With normalized decimal exponent `e`, fixed notation is used for `-4 <= e < 16`, otherwise scientific notation with lowercase `e`, no `+` and no leading exponent zeros; the decimal point is `.`, with no unnecessary trailing zeros or decimal point, and the output does not depend on locale. Special values are `0`, `-0`, `Infinity`, `-Infinity` and `NaN` (whatever the NaN's sign and payload). Borrows do not forward conformance (interpolation adapts them); object handles, pointers, Tuples, arrays and user Types have no automatic conformance.

### 22.10.3. Owning interpolation

The lexical form is [§2.9](02-source-and-lexical-structure.md#29-string-literals). Each embedded expression is passed as `write<T>(value: ref/T)` ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)) with `T is Utf8Format`: an owned Place or temporary is borrowed shared with `T` its Type; `ref/U` passes as is and `uniq/U` is Reborrowed shared, both with `T = U`. A bare expression does not move its source; `\(x@move)` borrows the moved temporary. Expressions are evaluated once, left to right, each write completing before the next expression. The first failure Aborts with `KIMI_E_FORMAT`; on success the result is an owning string.

```kimi
let count = 3
let message = "count = \(count), range = \(1..^1)"   // "count = 3, range = 1..^1"
```

### 22.10.4. `$tryWrite`

`$tryWrite(writer, literal)` is a built-in operation with result `Result<(), BufferFull>`; it builds no combined string and is not a function value. The first operand is acquired as `uniq/Utf8Writer` and is not a Receiver Expression ([§7.3](07-functions-and-callable-values.md#73-receivers)): a bare owned Place there is an error, a borrow value is Reborrowed. The second is an ordinary or raw string literal, with or without substitutions (grammar: [§F](appendices/F-syntax-summary.md#appendix-f-grammar)). The first operand is evaluated once and its borrow starts before any embedded expression; segments and values are then written left to right as in [§22.10.3](#22103-owning-interpolation). At the first failure, or if the adapter has already failed, later expressions are not evaluated (they are still checked). Embedded expressions cannot use the adapter or its buffer. Temporary scopes and the targets of `return`, `exit` and `yield` are unchanged.

```kimi
func printNumber(n: i64) -> Result<(), BufferFull>
    var scratch: [64 of u8] = [64 of 0]
    var buffer = Text.fixed(scratch@uniq)
    var writer = Text.writer(buffer@uniq)
    try $tryWrite(writer@uniq, "My number is \(n)")
    // try $tryWrite(writer, "x")     // Error: a bare owned Place is not borrowed implicitly.
    match buffer.text()
        .Ok(let text) => Console.writeLine(text)
        .Err(_) => $abort("unexpected invalid UTF-8")
    return .Ok(())
```

### 22.10.5. Allocation

A value is **bounded** when the Type that its write formats ([§22.10.3](#22103-owning-interpolation)) is a primitive integer, `Wrapping<T>`, `bool`, `char`, Unit, `f32` or `f64`; every other Type is unbounded.

- Built-in formatting into a `FixedBuffer`, or into a `HeapBuffer` with enough capacity, allocates nothing.
- `text`, `validate`, `intoText` and `intoString` scan nothing when the buffer is fully validated.
- An interpolation makes at most one heap allocation when every value is bounded, or when its only unbounded value is one `string` or `Utf8Slice` preceded only by literal text.
- `Text.toString` makes at most one heap allocation when built-in formatting reserves the whole output once. It calls the formatter once, without a sizing pass or retry.
- These bounds count private output-buffer allocations, including the storage retained by the resulting string, but exclude allocations by embedded expressions or user formatters. All other private-buffer allocation counts for interpolation and `Text.toString` are implementation-defined. An implementation may elide, merge, delay or place them on the stack, so their allocation failures need not occur; results, output, evaluation order and count, and user effects never depend on this.

```kimi
let s = "xyz"
let line = "abc\(s)def"     // One unbounded value preceded only by literals: at most one allocation.
```
