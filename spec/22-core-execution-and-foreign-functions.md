# 22. Kimi, program execution, and foreign functions

[Specification index](../SPEC.md)

This chapter defines the required declarations of the Kimi Kotonoha, process startup and shutdown, the foreign-call boundary, minimal standard output, the initial Windows runtime and test execution. Kimi names the foundation library; Core remains a Type component.

<a id="221-required-core-declarations"></a>

## 22.1. Required Kimi declarations

Every Compilation binds exactly one compiler-compatible **Kimi Kotonoha** under the reserved direct reference name `Kimi`. User dependencies and project-root declarations cannot use that name. `Kimi` is not a keyword, so inner scopes follow normal shadowing; `::Kimi` bypasses locals and aliases. The mandatory default alias opens Kimi under §18.1.3.

The Types and Contracts below are public at the library root. Ownership operations belong to the public `Intrinsics` group (§22.1.1), and `writeLine` to the public `Console` group (§22.4). Qualified paths such as `::Kimi.Option<T>` identify these declarations regardless of local shadowing. Compiler metadata records their originating Kotonoha, version and Symbol Identities. The compiler recognizes a Kimi declaration by its original declaration Identity, never by name or user conformance: a same-spelled user declaration or a replacement alias never receives its special behavior.

A missing, duplicate or incompatible Kimi definition is rejected before finalization. The compiler may synthesize these definitions, but synthesized and loaded definitions must have the same language identities and contracts. Kimi itself is built with these identities designated by the compiler.

The table is the minimal set that language rules name, not a promise of a general standard library:

| Declaration | Required shape or operation |
| --- | --- |
| `Option<T>` | enum with Some(T), None in that order; Self is Copy with condition-atom set {T is Kimi.Copy} |
| `Result<T,E>` | enum with Ok(T), Err(E) in that order; Self is Copy with condition-atom set {T is Kimi.Copy, E is Kimi.Copy} |
| `Weak<S>` | Compiler-managed Non-Copy struct over a valid complete rc/arc S; always holds a target table, with no empty constructor. Kimi.Intrinsics.downgrade / upgrade / clone follow §3.2.2 and §13.5.9 |
| `Array<T>` | Non-Copy owning dynamic sequence over a valid complete T; no Owned requirement; public read-only length/capacity: isize and indices: ResolvedRange; `UniqIndexable<isize>` and `UniqIndexable<Index>` (§4.6.9), §4.7 mutation/capacity APIs, literals, and the `Iterable`/`UniqIterable`/`IntoIterable` conformances with the items of §14.6.2 |
| `Index` | Copy, Owned, Equatable direction/offset value; constructor, read-only fields, resolve/tryResolve under §4.6.2 and §4.6.4 |
| `Range` | Copy, Owned, Equatable unresolved boundaries; syntax construction, read-only fields, resolve/tryResolve under §4.6.3 and §4.6.4; not enumerable |
| `ResolvedRange` | Copy, Owned, Equatable validated interval; constructor, read-only fields, and the three iteration conformances with item `isize` under §4.6.3 |
| `Slice<T> {source}` | Copy shared view with all public operations in §4.6.6; `Indexable<isize>` and `Indexable<Index>` publishing `place(ref, T) during self.source`; the three iteration conformances with item `ref/T during source`; backing Origin is explicit or inferred under ordinary rules |
| `Dictionary<K,V>` | Non-Copy owning collection over valid complete K/V requiring K is Equatable; no Owned requirement; literal construction, `UniqIndexable<K>` existing-key indexing, public read-only length/capacity: isize, §4.7 lookup/mutation/capacity APIs and the three iteration conformances with the pair items of §14.6.2 |
| UTF-8 formatting declarations | `Utf8Format`, `BufferWriter`, `WriteWindow`, `Utf8Writer`, `BufferFull` at the root, and the `Text` group: exact signatures, shape, intrinsic Origin/Loan/variance metadata and operations in the [formatting profile](utf8-formatting.md#1-contracts-and-declarations) |
| `Equatable` | `func equals(self: ref/Self, other: ref/Self) -> bool` |
| `Comparable: Equatable` | `func compare(self: ref/Self, other: ref/Self) -> i32`; negative/zero/positive for less/equal/greater |
| `LendingIterator`, `Iterator: LendingIterator`, `Cursor`, `UniqCursor` | The exact declarations of §22.1.2.1: LendingIterator's `LentItem(step)` and `next`; Iterator's step-independent `Item` and the effect bound of §22.1.2.4; Cursor's `Element`, `advance` and Place-publishing `current`/`currentUniq` |
| `Iterable`, `UniqIterable`, `IntoIterable` | The exact declarations of §22.1.2.2: `IteratorType(source)` or `IteratorType` bound to a LendingIterator, and `iterate`, `iterateUniq` or `intoIterator` |
| `Indexable<Key>`, `UniqIndexable<Key>: Indexable<Key>` | `associate Element`; `index(self: ref/Self, key: ref/Key) -> place(ref, Element) during self` and `indexUniq(self: uniq/Self, key: ref/Key) -> place(uniq, Element) during self` (§4.6.9) |
| `Iteration` group | `Owned<I>`, `Borrowed<I>`, `Shared<C>`, `Uniq<C>` and `owned`, `borrowed`, `shared`, `uniq` under §22.1.2.3 |
| `Storage` internal group | `RefRemainder<S>`, `UniqRemainder<S>`, `OwnedRemainder<S>`, `borrowStorage`, `ownStorage`, `splitFirst`, `takeFirst` under §22.1.2.5; usable only inside the Kimi Kotonoha |
| Copy, Owned, Callable, Sealed, ObjectPayload | Compiler-intrinsic requirement identities with exactly their existing derivation, ownership and call rules (§8.4.7 for Sealed and ObjectPayload); not ordinary user-implementable replacements |
| Object ownership intrinsics | Kimi.Intrinsics.makeObj / makeRc / makeArc, strong and Weak Kimi.Intrinsics.clone, Kimi.Intrinsics.downgrade / upgrade, Kimi.Intrinsics.makeRcCyclic / makeArcCyclic, with §13.5.8–9 names, Types and acquisition contracts; every creation declares `T is ObjectPayload` (§8.4.7.2) |
| Whole-value update intrinsics | Ordinary generic declarations `Intrinsics.replace`, `Intrinsics.exchange`, `Intrinsics.swap`, with §15.7 signatures and acquisition/destruction contracts; their signatures impose no Sealed requirement, and completeness is checked at each actual storage target |
| `Console.writeLine` | Overloads `(text: ref/string) -> ()` and `(text: Text.Utf8Slice) -> ()`; §22.4 and the [formatting profile](utf8-formatting.md#61-console-output) |
| `Test.tempDirectory` | `public func tempDirectory() -> string`; independently owned case-directory path, restricted to test-only bodies under the [test profile](testing-profile.md#environment-and-temporary-directory) |

The iteration and indexing Contracts are static Contracts. Their associated Types are complete Types and may take Origin parameters (§8.4.3.1). Table signatures follow the normal associated-Type, receiver, result-Origin and lifetime rules.

Fixed arrays conform to the three iteration entries with the items of §14.6.2. The standard iterators are concrete Kimi Types with the item Types and dependencies of §4.6 and §14.6.2, and they have the common guarantees of §22.1.2.3. A ResolvedRange iterator stores a position and an end; a Slice iterator stores a copied handle, a position and the external source Loan; neither owns the elements it yields. Dependent Types preserve source dependencies through associated Types and Option payloads. Receiving the result of `next` extends no lifetime. These requirements add no public iterator constructors.

The primitive keyword `string` denotes the compiler's UTF-8 string Core, not a shadowable alias. It supports literal and interpolation construction, concatenation, comparison and Utf8Format. The [formatting profile](utf8-formatting.md) defines separate mutable buffers, validated views and `Text.toString` for string copying; it adds no character indexer or formatting options. Fixed-array syntax and layout follow [sequence Types](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices); metadata, indexed Place acquisition and shared reading follow [indexing and slicing](04-arrays-indexing-and-slices.md#46-indexing-and-slicing).

The Copy conditions of Option and Result are compared as atom sets, using the proposition identity and conjunction elimination of §8.7. `T` and `E` denote the corresponding parameter slots, and `Kimi.Copy` the recognized Symbol. Order, transparent grouping and duplicate atoms do not change a set. A missing or unconditional Copy, a missing or extra atom, or a different identity is incompatible. All other required-shape checks remain, without general logical-equivalence reasoning. Generated sources may use the canonical order `T`, `E`, but loaded Kimi definitions cannot be required to use it.

Option and Result Copy and Owned follow the ordinary enum rules; no extra copying is introduced. A changed Kimi contract invalidates dependent capability, acquisition and generation results under §21.3.4. Unchanged Case order and payload structure do not establish binary compatibility with older Kimi artifacts.

Array and Dictionary contents, generic enum payloads and fixed-array elements preserve complete Type, Origin and Loan dependencies under §15.4. Array's Owned classification follows `T`, and Dictionary's follows `K` and `V`, independently of runtime contents. No container grants permission to hide dependencies or extend a referent's lifetime. Checked-cast designs use the required Kimi Option Identity despite deferred View syntax. Dictionary need not expose hashing. Dynamic mutation, allocation, ordering, retained dependencies, effects and complexity follow §4.7; further library APIs remain separate designs.

### 22.1.1. Declaration placement and function reference

`Kimi.Intrinsics` is a public, non-generic group with no Origin parameters. It contains the whole-value update and object ownership operations as one family. `Copy`, `Owned`, `Callable`, `Sealed` and `ObjectPayload` remain directly under `Kimi`; `writeLine` remains under `Kimi.Console`. The default Kimi alias does not recursively open the `Console`, `Intrinsics`, `Test`, `Text` or `Iteration` groups or the internal `Storage` group. Use `Intrinsics.replace(...)` or `Console.writeLine(...)`, a fully qualified path, or an explicit alias that opens the group. A named alias such as `alias Memory => Kimi.Intrinsics` preserves the original declarations' Identities. There are no root-level compatibility declarations such as `Kimi.replace` or `Kimi.makeObj`.

The following reference collects the public function names. Types are abbreviated relative to `Kimi`; the linked sections own all constraints, overload requirements, Origins, acquisition and failure behavior.

| Fully qualified function | Signature / input and result Types | Owning rules |
| --- | --- | --- |
| `Kimi.Console.writeLine` | `(text: ref/string) -> ()`, `(text: Text.Utf8Slice) -> ()` | §22.4 |
| `Kimi.Text` functions | `fixed`, `heap`, `writer`, `utf8`, `validateUtf8`, `toString`, `tryFormat` | [Text operations](utf8-formatting.md#2-text-operations) |
| `Kimi.Test.tempDirectory` | `() -> string` | [Test profile](testing-profile.md#environment-and-temporary-directory) |
| `Kimi.Intrinsics.replace<T>` | `(target: uniq/T, with => value: T) -> ()` | §15.7 |
| `Kimi.Intrinsics.exchange<T>` | `(target: uniq/T, with => value: T) -> T` | §15.7 |
| `Kimi.Intrinsics.swap<T>` | `(first: uniq/T, second: uniq/T) -> ()` | §15.7 |
| `Kimi.Intrinsics.makeObj<T>` | `(value: T) -> obj/T` | §13.5.8 |
| `Kimi.Intrinsics.makeRc<T>` | `(value: T) -> rc/T` | §13.5.8 |
| `Kimi.Intrinsics.makeArc<T>` | `(value: T) -> arc/T` | §13.5.8 |
| `Kimi.Intrinsics.clone<S>` | Strong: `ref/S -> S`; Weak: `ref/Weak<S> -> Weak<S>` | §13.5.8–9 |
| `Kimi.Intrinsics.downgrade<S>` | `ref/S -> Weak<S>` | §13.5.9 |
| `Kimi.Intrinsics.upgrade<S>` | `ref/Weak<S> -> Option<S>` | §13.5.9 |
| `Kimi.Intrinsics.makeRcCyclic<T, F>` | `F -> rc/T` | §13.5.8 |
| `Kimi.Intrinsics.makeArcCyclic<T, F>` | `F -> arc/T` | §13.5.8 |
| `Kimi.Iteration.owned<I>` | `I -> Owned<I>` | §22.1.2.3 |
| `Kimi.Iteration.borrowed<I>` | `uniq/I during source -> Borrowed<I>{r}` with `origin r.source == source` | §22.1.2.3 |
| `Kimi.Iteration.shared<C>` | `C -> Shared<C>` | §22.1.2.3 |
| `Kimi.Iteration.uniq<C>` | `C -> Uniq<C>` | §22.1.2.3 |

Container members stay with their owning Types and Contracts: the iteration, Cursor and Indexable requirements are in §22.1.2 and §4.6.9; collection, indexing, range and Slice APIs in §4.6–7; comparison requirements in §22.1; formatting members in the [profile](utf8-formatting.md). `Option.Some` / `None` and `Result.Ok` / `Err` are enum Cases. Compiler built-ins such as `$abort` and `$tryWrite` are not declarations in these groups.

This reference specifies required APIs, including unimplemented ones. [STATUS.md](../STATUS.md#kimi-library-and-whole-value-updates) records current declaration and runtime coverage separately.

### 22.1.2. Iteration, cursors and storage

#### 22.1.2.1. Iterator and Cursor

```kimi
contract LendingIterator
    associate LentItem(step)
        wellformed uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>

contract Iterator: LendingIterator
    associate Item
    associate LendingIterator.LentItem(step) is Item

contract Cursor
    associate Element
    func advance(self: uniq/Self) -> bool
    func current(self: ref/Self) -> place(ref, Element) during self

contract UniqCursor: Cursor
    func currentUniq(self: uniq/Self) -> place(uniq, Element) during self
```

Every enumeration calls `LendingIterator.next`, which delivers the next item as a value. Each call binds `step` to the receiver borrow of that call and keeps the actual Loan dependencies. An **Iterator** is the ordinary case: its `Item` is one complete Type that names no Origin of `next`, so generic code may retain its items across later calls (§22.1.2.4). A conforming Type writes `associate Iterator.Item is T` and `func next(self: uniq/Self) -> Option<T>`; the inherited `LentItem(step)` is `Item` for every `step`. A **LendingIterator** that is not an Iterator may instead lend items that borrow the Iterator. A general LendingIterator may return `Some` after `None`; `for` stops at the first `None`, and §22.1.2.3 states the guarantees of standard iterators. `None` carries no Loan.

| `LentItem(step)` of a LendingIterator | Retention and the next `next` |
| --- | --- |
| `ref/E during step`, `uniq/E during step` | Depends on that call's receiver borrow and conflicts with the next `next` while retained |
| A Type without `step`, such as `E` or `ref/E during source` | Retained under the ordinary Loan rules, since the annotation alone removes no actual dependency (§22.1.2.4); only an Iterator publishes the effect bound that lets generic code keep it across the next `next` |

A Cursor publishes the Place of its current position. After creation and after a false `advance`, there is no current position, and `current` and `currentUniq` Abort. After a true `advance`, the same logical element stays selected until the next `advance` and may be accessed repeatedly, shared or exclusively. Calling `advance` again after `false` is allowed. The implementation manages this state; the compiler proves no state machine and inserts no flags. The ordinary Loan rules reject conflicting `advance`, updates and destruction.

#### 22.1.2.2. Iteration entries

```kimi
contract Iterable
    associate IteratorType(source) is LendingIterator
        wellformed ref/Self during source
    func iterate(self: ref/Self during source) -> Self.IteratorType(source)

contract UniqIterable
    associate IteratorType(source) is LendingIterator
        wellformed uniq/Self during source
    func iterateUniq(self: uniq/Self during source) -> Self.IteratorType(source)

contract IntoIterable
    associate IteratorType is LendingIterator
    func intoIterator(self: Self) -> Self.IteratorType
```

The three capabilities are independent; `for` requires the conformance of the entry that its Subject mode selects (§14.6.2). The borrowing entries keep the per-call `source`, and the owning entry keeps the dependencies inside `Self`. The item Type is the `LentItem(step)` of the selected `IteratorType` (§14.6.2); the entry mode forces neither `ref`, `uniq` nor an owned item. `UniqIterable` guarantees only the exclusive receiver borrow. The exclusive Place results of `UniqIndexable` and `UniqCursor` are guarantees of those requirement signatures, not of the name `Uniq`. A Type may conform to `Iterator` and to entries at the same time. There is no duck typing by member name, no automatic conformance of references or arbitrary Iterators, no derived Contract and no default body. No standard conformance advances an Iterator through a shared borrow.

```kimi
struct Countdown
    Self is Iterator
    Self is IntoIterable
    Self is UniqIterable
    associate Iterator.Item is i32
    associate IntoIterable.IteratorType is Self
    associate UniqIterable.IteratorType(a) is Kimi.Iteration.Borrowed<Self>{view}
        origin view.source == a
    var remaining: i32 = 3

    public func next(self: uniq/Self) -> Option<i32>
        if self.remaining == 0 => return .None
        self.remaining -= 1
        return .Some(self.remaining)

    public func intoIterator(self: Self) -> Self
        return self@move

    public func iterateUniq(self: uniq/Self during source)
        -> Kimi.Iteration.Borrowed<Self>{result}
        origin result.source == source
        return Kimi.Iteration.borrowed(self)

var total: i32 = 0
var countdown = Countdown.init()
for number in countdown@uniq
    total += number
    exit                       // One item; countdown is reusable once its Loans end.
for number in countdown@move
    total += number            // Consumes the rest.
```

#### 22.1.2.3. Standard adapters

The public group `Kimi.Iteration` declares the following functions and result Types. `I` is a LendingIterator and `C` a Cursor; each result is an ordinary generic struct that conforms to `LendingIterator`, `IntoIterable` (transferring itself) and `UniqIterable` (returning `Borrowed<Self>`).

| Operation | Result Type | Contract |
| --- | --- | --- |
| `owned(iterator)` | `Owned<I>` | Takes `I` by value and forwards its `LentItem(step)` and dependencies; a Non-Copy Place is written `@move` |
| `borrowed(iterator)` | `Borrowed<I>` | Takes `uniq/I` and advances it without Moving it; items keep `I`'s contract |
| `shared(cursor)` | `Shared<C>` | Takes `C` by value; after a true `advance` it borrows `current` and returns `ref/Element during step` |
| `uniq(cursor)` | `Uniq<C>` | Takes a `UniqCursor` by value; after a true `advance` it returns `uniq/Element during step` from `currentUniq` |

`uniq` is a contextual word only in Semantics positions, so `Kimi.Iteration.uniq(cursor)` is an ordinary call, distinct from the borrow `@uniq`.

`Borrowed`'s slot `source` is the outer Origin of its input, and the `step` of each item is the actual Reborrow of `next`. `Owned` and `Borrowed` inherit their input's exhaustion guarantee. They conform to `Iterator` exactly when `I` does, with the same `Item`, dependencies and effect bound.

`Shared` and `Uniq` are LendingIterators whose items depend on `step`. They keep their own finished state and call `advance` once per `next`, calling the corresponding `current` only after `true`. After the first `false` they return `None` without calling the Cursor again, so they stay exhausted whatever the Cursor does later. Their `Some` payload is an ordinary reference value, never a Place.

No adapter allocates, updates a reference count or materializes items in advance. Draining is an ordinary Iterator API that publishes how items are taken and how an early exit treats the remainder; it is not a language protocol.

```kimi
// iterator: a writable owned variable conforming to UniqIterable.
for item in iterator@uniq
    inspect(item)
    exit // The remaining items stay; iterator is reusable once the retained Loans end.
```

**Standard iterators.** Every standard iterator of the Types in §14.6.2 is an Iterator and keeps returning `None` after the first `None`. In exclusive enumeration, a collection iterator proves the non-overlap of its exclusive items by region splitting (§15.6.3). An owning iterator owns the unreturned elements and transfers only the returned element's responsibility; owning Array and Dictionary iterators destroy unreturned elements in §4.7.6 order. A borrowing iterator never destroys the collection.

#### 22.1.2.4. Iterator independence

An Iterator's `Item` names no Origin parameter of `next`, so a returned item depends neither on the receiver Loan of that call nor on the Iterator's own Storage. The ordinary result-Origin checking of every `next` implementation establishes this. Dependencies on the external source, the owner and needed parent Loans remain. An annotation or a Type equality removes no actual dependency. Exclusive items are produced only by splitting the Iterator's authority into non-overlapping regions (§15.6.3, §22.1.2.5), so moving or freeing the Iterator's Storage invalidates no returned item.

`Self is Iterator` also publishes the **effect bound** of `next`: the effects of `next` conflict with no Loan kept by an item that the same Iterator returned earlier, including Loans kept through ordinary transfer or Reborrow of that item. These effects include reads, writes, borrows, the Loans of the result, access to statics and captures, and cleanup inside the call. A Move of the Iterator carries the bound. Conformance verification checks the bound with the common root, Loan and effect summaries and the region-splitting rules. Every implementation, including specializations and callees, must satisfy it, or the conformance is rejected. User Types that declare `Self is Iterator` undergo the same verification; nothing is derived from `LendingIterator` automatically. Generic callers use the published bound (§15.6.4, §21.3.4). The bound promises no purity; Loans unrelated to items, other operations and result lifetimes are checked as usual. The `next` of a standard collection iterator only traverses, splits and transfers; it calls no user comparison, destructor or callback.

```kimi
func nextPair<I>(iterator: uniq/I) -> (Option<I.Item>, Option<I.Item>)
    I is Iterator
    let first = iterator.next()
    let second = iterator.next() // The published effect bound permits retaining first.
    return (first@move, second@move)
```

Destroying or replacing the whole Iterator is an effect separate from `next`: a remainder destructor that conflicts with a retained item is rejected. Standard borrowing iterators end only their handle; owning iterators follow the destruction summary of the remaining element Type. Generic code may accumulate the items of a borrowing Iterator. An owning `collect<I>(it: I)` must also prove that results and cleanup do not conflict. Unknown effects are never treated as empty, and no hidden call condition postpones the check to instantiation. Dependencies that a user adds after an item was returned, and the protection of the original collection, are checked ordinarily. A delegating wrapper that borrows itself for its result cannot declare a step-independent `Item`, so it is a LendingIterator; a wrapper that adds conflicting effects to `next` fails the effect bound. For a LendingIterator that is not an Iterator, a lent item is used up before the next call, or non-conflict is proven case by case from its published contract.

#### 22.1.2.5. Standard storage boundary

The internal group `Kimi.Storage` holds the operations that split standard collection Storage into non-overlapping regions. The group, its Types and its operations are `internal`, so only the Kimi Kotonoha uses them (§9.3). Their capability is bound to the standard declaration identities: no alias, re-export or same-spelled declaration grants it. Public Iterators keep these Types in private Fields and never expose them in results or associated Types. No public exclusive Slice or raw-pointer conversion is added.

| Type | Responsibility |
| --- | --- |
| `RefRemainder<S>` | The shared Loan of its `source` slot and the traversal position of the untaken part; it does not own `S` |
| `UniqRemainder<S>` | The parent Loan of its `source` slot and exclusive access to the untaken part; it does not own `S` |
| `OwnedRemainder<S>` | The Storage transferred from `S` and the destruction responsibility for unreturned elements, with `S`'s internal dependencies |

`S` is one of `Array<E>`, `[N of E]` or `Dictionary<K, V>`. A borrowing remainder requires `ref/S during source` or `uniq/S during source` to be well formed. Each operation is an overload with the corresponding `E`, `K`, `V`, `length N` where needed, and the ordinary Type constraints. The result families below are descriptive:

- `R(a)` is `ref/E during a` for arrays and `(ref/K during a, ref/V during a)` for Dictionary;
- `U(a)` is `uniq/E during a` for arrays and `(ref/K during a, uniq/V during a)` for Dictionary;
- `O` is `E` for arrays and `(K, V)` for Dictionary.

| Signature template | Contract |
| --- | --- |
| `borrowStorage(value: ref/S during a) -> RefRemainder<S>{r}` with `origin r.source == a` | Holds the shared Loan and traverses from the first element |
| `borrowStorage(value: uniq/S during a) -> UniqRemainder<S>{r}` with `origin r.source == a` | Transfers the received whole-collection capability to the untaken part; no independent whole access remains |
| `ownStorage(value: S) -> OwnedRemainder<S>` | Transfers the Storage of `S` and its cleanup responsibility |
| `splitFirst(state: uniq/RefRemainder<S>{r}) -> Option<R(r.source)>` | Lends the untaken first element for shared access and advances |
| `splitFirst(state: uniq/UniqRemainder<S>{r}) -> Option<U(r.source)>` | Splits the untaken first element off as a child Loan and updates the remainder |
| `takeFirst(state: uniq/OwnedRemainder<S>) -> Option<O>` | Moves the unreturned first element out, updating its state and responsibility first |

```kimi
internal func splitFirst<E>(state: uniq/RefRemainder<Array<E>>{r})
    -> Option<ref/E during r.source>
internal func splitFirst<E>(state: uniq/UniqRemainder<Array<E>>{r})
    -> Option<uniq/E during r.source>
internal func takeFirst<E>(state: uniq/OwnedRemainder<Array<E>>)
    -> Option<E>
```

Borrowed results depend on `source`, never on the `state` borrow or slot. The shared form needs no non-overlap proof but still delivers each element once, in order. The following also hold for empty and zero-sized Storage:

1. the constructed target is complete; representation, construction, duplication and initialization-state changes are confined to these operations, and ordinary Field operations grant no capability;
2. `splitFirst` and `takeFirst` return `None` when nothing remains and otherwise the first untaken element exactly once, in the order of §14.6.2, calling no user callback, comparison or destructor;
3. an `OwnedRemainder` remains a complete handle over the unreturned part; it publishes neither a reference to the whole `S` with holes nor Take through a borrow;
4. destroying a `RefRemainder` or `UniqRemainder` ends only the capabilities it holds; destroying an `OwnedRemainder` destroys the unreturned part once in the ordinary cleanup order and frees the region, ending no returned Loan or responsibility and publishing the external effects of that destruction.

The internal operations obey the complexity bounds of §4.6.8. Array and fixed-array traversal keeps the valid untaken range as a start and a count, so a nonempty check proves the next element valid without a public `index` call or a second bounds check. Dictionary traversal uses the ordering information of live entries, distinct from hash-probe tombstones, and never charges a capacity scan to "amortization". This built-in boundary guarantees Storage validity, dynamic non-overlap and initialization state. Everything else an Iterator does, including user delegation, is checked ordinarily, and no user Storage can register with the boundary.

**Published effects.** Result anchors, parent Loans, remainder non-overlap and the effects on statics, captures and cleanup enter the public summary of each operation (§15.6.4). Generic, separately compiled and indirect calls compose those summaries without reanalyzing private bodies and treat unknown effects conservatively. They never erase an existing Loan because a conversion or erasure dropped a guarantee. An Unsafe designation grants neither independence nor a longer Origin. The effect bound of an Iterator's `next` (§22.1.2.4) is checked at each use, separately from the whole-value destruction summary; element-dependent cleanup uses the symbolic summaries of §4.7.5. No general effect syntax or runtime tag is added.

## 22.2. Program startup and static initialization

### 22.2.1. Startup selection

After directive selection, Mods, and Binding, an Application must select exactly one of:

- One SourceDocument with top-level runtime body items.
- One eligible root-level `public func main() -> ()`.

Mixed forms, multiple candidate documents or mains, and no candidate are rejected. Only the current project is searched, not dependencies; enumeration order and optimization never select the winner. EntrySource is not an initial selection mechanism, and naming an empty document cannot make an Application valid.

HasTopLevelRuntimeBodyItem is determined once per document from the selected root items, without descending into functions or Containers. The classification does not require that machine instructions survive:

| Root item | Counts as a runtime body item |
| --- | --- |
| Expression/control expression, unsafe/defer/require/explicit-discard statement | Yes, including Unit and removable expressions |
| Local let/var | Yes, even without an initializer |
| Function declaration, including main | No |
| Container or alias | No |
| Attribute argument, Type expression, constant evaluation, Mod execution itself | No |
| Excluded syntax | No |
| Selected legal generated item | Classify the resulting item by these rules |

Top-level bindings remain SourceDocument-local, not static Properties. Container static Properties do not count and keep first-access initialization. Generated items must be legal in their target context; neither generation nor startup classification expands that grammar.

The chosen document's body items execute in source order, with its source scope, CodeContext, local-function visibility, lifetimes and cleanup. The body is lowered to a private internal function; no public main is synthesized. This physical function creates no source-level return target (§14.5.3).

```kimi
// Implicit startup; the uninitialized local itself also counts.
let pending: i32
::Kimi.Console.writeLine("Hello, world!")
```

A minimal intentionally empty Application is the Unit expression `()`. Empty files and declaration-only files supply no implicit body.

### 22.2.2. Explicit main and Library

An Application's explicit main is exactly lowercase `main`, public and directly at the source root. It has no parameters, receiver, generic parameters or Origin parameters, and a Unit result; ordinary result omission is allowed. It is a safe ordinary function with a body, not unsafe, a foreign import or a specialization. A main inside a group, a struct or another function is not a candidate. Root-level public main is shared-root declaration syntax under §6.1.1 and retains declaration-site aliases; `public` promises no unmangled native symbol or export.

Every root-level public main in an Application is validated as a startup signature. An invalid declaration is diagnosed; no convenient overload is selected instead. Normal Unit return and fallthrough are permitted, and no special ownership rules apply.

```kimi
public func main() -> ()
    let message = "Hello, world!"
    ::Kimi.Console.writeLine(message)
    // message is borrowed for output and remains usable.
```

Adding a top-level `::Kimi.Console.writeLine("Top level")` to this project is an error because it mixes startup forms. Integer-returning main and a safe Kimi.exit API are not initial features; normal termination is 0 and Abort is 1. Runtime.Exit remains internal.

A Library requires no startup candidate, never calls main automatically and emits no OS entry. In a Library, main is an ordinary function without the Application signature restriction. Top-level runtime body items, including uninitialized let/var, are rejected. A Library may be consumed as a Project or distributed as a source package (§18.4–18.6) and then participate in common final generation. Its inspection `.ll` is neither that distribution format nor an external library or DLL ABI; language functions remain internal even when public. Optimization may remove all functions from standalone inspection output, so inspect pre-optimization IR.

### 22.2.3. OS entry, static initialization, and shutdown

The initial Windows Application emits the compiler-reserved external `__kimi_start` with physical signature `void ()`, Windows x64 ccc, `noreturn` and the profile attributes (§21.5), linked with `/entry:__kimi_start`. It performs the required runtime initialization, calls the selected body once, completes normal shutdown and calls Runtime.Exit(0). Empty initialization and shutdown helpers may be omitted. Ordinary mangling prevents source names from colliding with this reserved symbol.

```llvm
; Fragment: profile attributes and internal helper definitions are omitted.
; Each helper has one internal definition, not an additional same-name declare.
define void @__kimi_start() noreturn {
entry:
  call void @__kimi_entry_body()
  call void @__kimi_shutdown()
  call void @__kimi_runtime_exit(i32 0)
  unreachable
}
```

The entry body is the implicit body or a call to the selected main. Required runtime initialization precedes it; heap and standard-handle acquisition may instead occur inside each runtime operation.

The entry handles Kimigayo initialization and cleanup only; it runs no executable CRT startup or C/C++ static constructors. Foreign initialization must already be satisfied, for example by OS DLL loading, or by an explicitly supported adapter. /NODEFAULTLIB is not initialization.

Static stored Properties initialize per slot under §11.3.2. A first read, Borrow, write or other storage operation checks the slot state:

| State | Action |
| --- | --- |
| Not started | Mark Initializing; evaluate the declaration initializer; after normal completion mark Initialized, then perform the operation |
| Initializing | Abort for an initialization cycle |
| Initialized | Perform the operation without rerunning initialization |

A first write initializes before Replacement. Actual access determines dependency order, not fragment, file or link order. Computed execution initializes only the storage it actually accesses. A Type or function reference, an untaken branch or an effect summary initializes no unrelated Field. All initializers are checked even if unused; unused Fields need not be initialized and are not destroyed. An implementation without static execution support diagnoses the uses that require it.

Normal body exit cleans up its locals exactly once. Initialized static values are then destroyed in reverse order of successful initialization, retaining the required lifetime dependencies; dependencies that cannot survive this order are rejected. Initializing new static storage, accessing destroyed storage or reentering a Field's destruction during shutdown Aborts. These language rules include dependencies, although multi-Kotonoha linking is outside the initial profile.

Each cleanup finishes before subsequent cleanup or Exit. Abort stops normal cleanup and unwinding and attempts diagnostics before Runtime.Exit(1) (§17.3, §22.5); secured results are then neither delivered nor separately destroyed.

This revision admits one execution thread, with no source thread creation or concurrent foreign reentry. Atomic arc counts do not expand that permission; synchronization and thread transfer remain §D.2's design boundary.

### 22.2.4. Static storage in inherited environments

A Field directly in a group is static even when that group is nested in a struct. Its storage key is the Field declaration Identity plus the normalized enclosing bindings with Origins recursively erased. The key retains Type structure and every Semantics layer, including unused arguments. Distinct keys have distinct storage, and code sharing never merges them. Different paths to one key use one initialization state, one Loan and effect identity and one destruction responsibility.

```kimi
struct Cache<T>
    public group Statistics
        public var count: i32 = 0
```

Cache<i32>.Statistics.count and Cache<string>.Statistics.count are separate. Cache<ref/i32 during a>.Statistics.count and the corresponding b reference use one key after both full references pass their checks.

The stored value must be Owned and satisfy the existing static-storage conditions; enclosing Type arguments need not be Owned. Full Origins are preserved for access and lifetime checking. Under §8.10 and §21.3, one representation, initialization and destruction plan is verified for all valid Origin bindings that share a key, including the required operations, evidence and callees. Owned storage alone does not prove that an initializer can be shared. Verified typed plans are reused and concrete layout is finalized at instantiation; a plan is never chosen by the first accessing Origin, and a failing plan is never split into Origin-specific storage. Initializers may depend on ordinary runtime state.

The rules of §22.2.3 apply: lazy initialization, initialization-cycle Abort, reverse-initialization shutdown destruction, and the rules for Type and function references and for unused Fields and initializers.

Shared code reaches a key through a supplied initialize-and-address operation (§21.3.3.4), either fixed directly or represented by an existing entry/context pair. Its immutable private context retains the storage and state references and the required initializer and destructor operations; mutable initialization state stays with the storage. No new GenericContext slot kind, per-call context construction or runtime Type search is required. Direct-call optimization must preserve initialization checks, cycle detection and storage Identity.

## 22.3. Foreign function imports

### 22.3.1. Declaration and call contract

`#LibraryImport("library", "symbol")` on a bodyless unsafe func selects the target C calling convention. Both arguments are required nonempty, non-interpolated, NUL-free string literals. The first is a case-sensitive logical native requirement name belonging to the defining Kotonoha (§20.8.2), not a consumer alias or DLL path. Requirements may come from NativeRequirements or a self-targeted combined NativeLibraries record. The second argument is the exact external symbol, independent of the source function name. §20.8.2 validates the actual supply, kind and member closure; that validation does not replace the source and ABI obligations below.

Imports are allowed only directly in a group or rootgroup, or as receiverless struct type functions. Receivers, generic or Origin parameters, parameter defaults, varargs, specializations and executable bodies are rejected. Ordinary parameter-name rules apply, including the `!` boundary and external/internal renaming; name contracts change source argument matching only, not the foreign ABI. Every argument value is required. Calls are direct only; unsafe functions cannot be acquired as values. Ordinary access and unsafe-call rules apply.

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    public unsafe func observe(record: unsafe/NativeRecord) -> ()
```

NativeRecord can be the C-layout example in §21.1.3; the corresponding C declaration is `void observe_record(NativeRecord *record);`. The raw pointer is not read-only. Layout, validity, lifetime, writes, retention, ownership, and active Loans remain the caller's contract; this example supplies no new pointer-acquisition or raw-storage construction API.

Arguments are acquired once, from left to right, and then passed under the selected ABI. No automatic marshalling, retention, allocation, freeing or ownership acquisition occurs. Normal return resumes ordinary cleanup. C++ exceptions, SEH unwind, longjmp, callbacks and reentry must not cross Kimigayo frames; control handled entirely inside the foreign code is allowed. Violations carry no result or cleanup guarantee. The FP boundary contract of §21.5.4 applies, and §21.5.5 governs `nounwind` and `landingpad` generation.

Unresolved logical libraries and unsupported ABI signatures are compilation errors. Required link inputs are recorded; unresolved native symbols fail manual linking or loading before entry. C aggregate passing, export, callbacks, varargs and extra calling conventions remain extensions; C storage layout (§21.1) is specified separately and does not enable them.

### 22.3.2. Initial Windows C ABI

The physical signature is computed once and shared between declaration and call:

| Kimigayo parameter/result | C value | LLVM Type |
| --- | --- | --- |
| i8 / u8 | int8_t / uint8_t | i8 |
| i16 / u16 | int16_t / uint16_t | i16 |
| i32 / u32 | int32_t / uint32_t | i32 |
| i64 / u64 | int64_t / uint64_t | i64 |
| f32 / f64 | float / double | float / double |
| unsafe/T | Corresponding data pointer | ptr, address space 0 |
| Unit, result only | void | void |

These entries use ccc with no signext, zeroext, inreg, byval or sret. i8 and i16 are not widened to i32, and vararg default promotions are not applied. Other numeric conversions are separate language operations. LLVM handles registers, stack arguments beyond the fourth, shadow space and stack alignment; unused upper bits are not meaningful. Additional optimization attributes need independent proof.

```llvm
declare dllimport i8 @native_i8(i8)
declare dllimport i16 @native_u16(i16)
```

All other parameter and result Types are excluded, including bool, char, string, borrows, object handles, aggregates (even C-exchangeable structs and arrays), function and Closure values, i128/u128 and isize/usize. Raw pointees are not passed by value and need not be C-exchangeable when opaque. Future aggregate passing needs a separate argument and result C ABI classification and tests, not direct translation to LLVM aggregate parameters.

## 22.4. Minimal console output

```kimi
Console.writeLine("Hello")
::Kimi.Console.writeLine("Hello")
```

Opening Console explicitly, or through an additional default alias, permits the bare name:

```kimi
alias Kimi.Console
writeLine("Hello")
```

There is no `Kimi.writeLine`, old `Core` compatibility reference or forwarding API. `Core` is an ordinary user name. Console is a group, not a value or special syntax. An intrinsic is a declaration whose Identity has compiler-recognized meaning; no public `Intrinsic` namespace or `Kimi.Intrinsic` group is introduced. Aliases retain that Identity. `$abort`, `$expect`, `$require` and the Composition Root retain their existing roles.

Kimi's public group `Console` provides two ordinary public overloads, `writeLine(text: ref/string) -> ()` and `writeLine(text: Text.Utf8Slice) -> ()`. The default alias makes `Console.writeLine` available without opening Console (§22.1.1). Each overload is safe and nongeneric, with one required argument and no receiver, defaults, formatting parameters or result borrow. The string overload borrows its argument; the view overload copies its borrowed handle. The compiler and runtime supply the implementation; it is not a source LibraryImport taking a string and does not expand the FFI Type surface.

The text is borrowed for the call under the ordinary argument adaptation rules: a string Place stays usable, and a literal, interpolation or `Text.toString` result is materialized and borrowed as a temporary. Both overloads write all UTF-8 bytes of the text plus one LF to standard output. NUL is data. Contents are preserved without normalization, CRLF conversion or locale encoding. Failure may leave partial output; no rollback or device atomicity is promised. The call returns Unit after the host accepts the bytes and this call's runtime buffer is flushed, not necessarily after display or durable storage. Failure to complete initiates Abort under the normal diagnostic and termination rules, even if stdout is unavailable. The call destroys nothing; a temporary argument follows its ordinary lifetime. If the backend cannot provide this operation, an unsupported target or feature is diagnosed.

**Complete application example (implicit startup in one SourceDocument).**

```kimi
::Kimi.Console.writeLine("Hello, world!")
```

The required standard-output bytes are UTF-8 `Hello, world!` followed by LF; normal completion exits with code zero under §22.2. No source main function, user alias, unsafe block, interpolation or user-declared foreign function is needed. General I/O error and result APIs remain outside this minimal operation.

The initial Windows implementation of this operation is specified in §22.5; output settings, the manifest and manual build steps are in §20.8. [STATUS.md](../STATUS.md#4-llvm-generation-coverage) records the first executable implementation milestone and which settings exist or are proposed. A prototype supporting only that subset must identify itself as partial; the milestone does not relax the Kimi identity, shape or validation requirements of a fully conforming Compilation. Unused executable Kimi bodies need not be emitted, but a same-spelled stub without the required identity and contract is not a compatible Kimi definition.

## 22.5. Initial Windows runtime

### 22.5.1. Operations and source context

The compiler emits these six logical operations in the same LLVM module. They are internal abstractions, not source APIs or a dedicated runtime DLL:

```text
Alloc(size: usize) -> ptr
Free(memory: ptr) -> ()
WriteStdout(data: ptr, length: usize) -> ()
Exit(code: u32) -> Never
TryWriteStderr(data: ptr, length: usize) -> bool
Abort(reason: DiagnosticText, sourceLocation: SourceLocation) -> Never
```

Failures of Alloc and WriteStdout, and detected Free failures, Abort. TryWriteStderr returns false without initiating Abort. Abort attempts diagnostics, then calls Exit(1); Exit never returns and performs no Kimigayo cleanup. The normal language path calls Exit(0) only after cleanup.

Physical helpers carry any required private diagnostic context through the compiler-selected internal ABI (§21.4.2); its position and representation are not fixed. Lowering preserves the static logical path, line and column of the original operation, including failures inside Alloc, Free and WriteStdout and generated-source CodeContext provenance. This does not depend on PDBs or stack traces.

Generated arithmetic checks report the start of the failing arithmetic expression. Compound assignment and increment/decrement report the start of the complete update expression. These locations remain the same across optimization levels.

### 22.5.2. Allocation and release

Allocation uses the process heap, MaxObjectSize = 2^63 - 1, and alignment support up to 16. `length * stride`, headers and alignment rounding are checked before allocation; an overflow or limit failure Aborts. Alloc also checks its own limit, obtains GetProcessHeap and calls HeapAlloc(heap, 0, max(size, 1)); a null heap or allocation Aborts. It returns uninitialized raw memory, not an Initialized language value. The substitute byte for size zero does not change a Type's size or stride.

Free(null) succeeds without work. Otherwise Free requires the original live pointer returned by this allocator, never an interior pointer or literal backing. GetProcessHeap failure or a detected HeapFree(heap, 0, memory) failure Aborts. Lowered cleanup runs destruction before Free; Free itself invokes no destructor. Correct ownership and pointers are a duty of static analysis and generation; detection of double frees or arbitrary corruption is not guaranteed.

HeapAlloc flags remain zero: no exception generation, and process-heap synchronization stays enabled. HeapAlloc supplies no last-error on failure, so a stale GetLastError value is never reported for a null allocation. Last-error is obtained immediately after the APIs that provide it, including failed HeapFree and WriteFile.

HeapBuffer completion may use the [optional in-place shrink](utf8-formatting.md#34-optional-in-place-shrinking). This is a non-failing optimization, never a replacement allocation. An implementation using HeapReAlloc must request in-place-only behavior and retain the original allocation on failure.

### 22.5.3. Synchronous byte output

WriteStdout and TryWriteStderr share a checked byte-write adapter. They add no newline, NUL scan, encoding conversion, buffer or pointer retention. Length zero succeeds before handle acquisition or pointer access. A positive length requires length <= MaxObjectSize, nonnull data, a readable live range within one allocation, and no unsigned 64-bit overflow in baseAddress + (length - 1). Numeric checks do not prove allocation validity or lifetime.

GetStdHandle(-11) obtains stdout and GetStdHandle(-12) stderr; null or INVALID_HANDLE_VALUE fails. These handles are never closed. Initial support requires synchronous standard handles. WriteFile receives a 32-bit written-count slot and a null OVERLAPPED; a supplied last-error is captured before another API is called.

```text
remaining = length
while remaining > 0:
    chunk = min(remaining, UINT32_MAX)
    request chunk bytes with WriteFile
    fail if the call fails, written == 0, or written > chunk
    remaining -= written
    if remaining > 0:
        advance within the original buffer with checked pointer arithmetic
```

No next pointer is computed after completion. Partial writes advance by actual progress, and zero progress is never retried indefinitely. WriteStdout turns any detected failure into Abort; TryWriteStderr returns false and must not call Abort, WriteStdout or Alloc. Output may be partial, with no rollback, atomicity, display, durability or bounded synchronous-wait guarantee. Success means that the OS accepted all bytes. These checked buffer rules do not change general unsafe pointer arithmetic.

### 22.5.4. Abort diagnostics and exit

Fixed diagnostics use an ASCII identifier, an English reason and the source location, optionally followed by a valid numeric OS error:

```text
Main.kimi:3:5: abort KIMI_E_STDOUT: Failed to write to stdout (win32=6)
```

Catalog codes are unique. They include KIMI_E_ALLOC_SIZE (allocation size exceeds limit), KIMI_E_PROCESS_HEAP, KIMI_E_ALLOC, KIMI_E_FREE, KIMI_E_STDOUT, KIMI_E_INT_OVERFLOW (Integer overflow), KIMI_E_INT_DIV_ZERO (Integer division or remainder by zero), KIMI_E_INT_SHIFT_COUNT (Shift count out of range), KIMI_E_INT_CONVERSION (Integer conversion out of range) and KIMI_E_INDEX_BOUNDS (Index out of bounds). The codes are used as follows:

- Integer division or remainder by zero uses KIMI_E_INT_DIV_ZERO; signed minimum with divisor -1 uses KIMI_E_INT_OVERFLOW for both operations.
- A shift count outside `0 <= count < left operand bit width` uses KIMI_E_INT_SHIFT_COUNT. Discarded left-shift bits do not trigger overflow.
- A runtime integer conversion outside the target range uses KIMI_E_INT_CONVERSION, as does a float-to-integer failure, including NaN and infinities. A conversion of a finite source that rounds to floating infinity uses `KIMI_E_FLOAT_CONVERSION: Floating conversion out of range`. These codes identify the failures required by §13.5.4; direct literal fitting failures remain compile-time errors.
- Ordinary element indexing outside the receiver bounds uses KIMI_E_INDEX_BOUNDS, including constant indices and zero-length arrays. Dictionary indexing with an absent key uses `KIMI_E_MISSING_KEY: Dictionary key was not found`; an absent result from a try-prefixed operation is not an Abort.
- Formatting defines `KIMI_E_ARG_RANGE: Argument out of range` and `KIMI_E_FORMAT: Formatting failed`; their triggers are in the [formatting profile](utf8-formatting.md).

Unavailable OS codes are omitted, and FormatMessageW is not used. Displayed logical paths escape non-ASCII and control characters as `\u{HEX}` and backslash as `\\`; the actual path and provenance are preserved internally.

Diagnostics are output from constants, valid input strings and small fixed work areas, without requiring heap allocation or one concatenated dynamic string. Explicit `$abort(expression)` keeps ordinary one-time argument evaluation and outputs the resulting UTF-8 string after KIMI_E_ABORT, without translating or escaping its contents. Once Abort starts, that string is not normally destroyed.

TryWriteStderr failure truncates diagnostics and proceeds to Exit(1), with no recursive diagnostic path. Runtime.Exit forwards its u32 to ExitProcess and ends its caller block with `unreachable`. It runs no cleanup; successful shutdown and Abort reach it through their distinct §22.2 paths.

### 22.5.5. String handle and writeLine

The initial internal string storage representation is `{ ptr, i64, i8 }`: data, byteLength and releaseKind. DataLayout determines padding and alignment. Function passing is selected independently under §21.4.2 and need not use aggregate argument or result slots. This storage representation is not a public FFI or binary ABI.

| Component/state | Validity and responsibility |
| --- | --- |
| Length | 0..MaxObjectSize; contents are valid UTF-8 |
| Positive length | Nonnull readable data with the required lifetime |
| Static = 0 | Compiler constant backing, never freed; null permitted only at length zero |
| Heap = 1 | Nonnull original live Runtime.Alloc pointer, capacity >= length, exactly one owning release responsibility |
| Empty Heap | Keep and free the original pointer even at length zero; allocation-free empties use Static |
| Other releaseKind | Invalid; destruction's default branch Aborts instead of freeing an unknown pointer |
| Moved source slot | Unusable as a value; no bit clearing or rewriting is required |

Validity is established at construction, not by revalidating UTF-8 or allocations on every use; corruption detection is not guaranteed. Literal backing may be shared without granting Copy to string. Hello world needs no heap allocation.

The required Kimi.Console.writeLine Symbol (§22.4) receives a reference to a string handle, reads its data and length, calls WriteStdout(data, length), calls WriteStdout on a one-byte LF constant and returns Unit. It neither releases nor modifies the handle; the caller keeps ownership of the string and destroys a temporary argument at its ordinary lifetime. An empty string still emits LF. No concatenation buffer is required.

The Utf8Slice overload passes the view's data and length directly to the same byte-output path. It creates no string handle and transfers no release responsibility. Its declaration has a distinct Kimi overload Identity; an expected Function Type selects an overload, while an untyped function reference is ambiguous.

Raw UTF-8 bytes are written to redirected files and pipes without changing the console code page. Non-ASCII console appearance depends on console configuration; universal Unicode console display is not initially guaranteed. A GetConsoleMode/WriteConsoleW adapter is a future extension, not implicit UTF-16 output.

### 22.5.6. Windows external symbols

WindowsRuntimeSymbols retains each external name, physical signature, calling convention, dllimport setting and link input. Declarations are shared through the module symbol table (§21.5.2), including matching LibraryImport declarations. Only the needed APIs are emitted, using Windows x64 ccc:

```llvm
declare dllimport ptr @GetProcessHeap()
declare dllimport ptr @HeapAlloc(ptr, i32, i64)
declare dllimport i32 @HeapFree(ptr, i32, ptr)
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport i32 @GetLastError()
declare dllimport void @ExitProcess(i32) noreturn
```

HANDLE and data pointers use `ptr`, SIZE_T uses `i64`, DWORD, UINT and BOOL use `i32`, and LPDWORD is a `ptr` to a 32-bit slot. Windows BOOL is not `i1`. Empty parameter parentheses mean no parameters, not omitted Types. `dllimport` specifies reference generation, not automatic linking; the kernel32 input (§20.8.2) supplies these symbols.

The Utf8Slice overload needs no additional external symbol. If optional shrinking is emitted, register `declare dllimport ptr @HeapReAlloc(ptr, i32, ptr, i64)` with kernel32 in the same symbol table; otherwise omit it.

## 22.6. Test execution and reporting

### 22.6.1. Test startup and active case

The test build uses a dedicated generated startup and requires no product Application entry. Explicit main functions and every selected top-level runtime body are verified under the ordinary Type, ownership and control-flow rules but never executed automatically. This applies equally to normal sources and TestSources, and produces no warning merely because startup is not executed. An ordinary build still validates its Application or Library startup rules; passing tests does not establish a valid product entry. Test startup does not initialize top-level let/var, which remain SourceDocument-local runtime bindings (§22.2.1), and tests cannot capture them. Shared preparation belongs in ordinary helpers that each test calls explicitly, or in ordinary lazy static Properties. Product/test membership is defined in §18.8, and discovery in §20.9.

```text
assign case -> prepare minimal reporting runtime -> activate case
  -> user initialization / test body / cleanup / shutdown
  -> report completion -> deactivate case -> child exit
  -> parent recovers processes, channels and temporary storage -> final result
```

All permitted verifications from user initialization through shutdown belong to the same case. Reports distinguish the initialization, body, cleanup and shutdown phases and restore the outer phase after nested initialization or cleanup. Static initialization remains demand-driven under §22.2.3; tests add no eager initialization. A runtime preparation failure is a startup or execution error. A verification without an active case is misuse, whatever its condition. The required initialization and destruction order and ordinary Abort behavior are unchanged. Test host generation introduces no Entry/Provider selection syntax; Composition Root extensions remain deferred (§13.8).

### 22.6.2. Process isolation and recovery

Each selected case runs once in a new child process of the same immutable native executable for its project, which receives the CaseId. Cases share no static state and are not retried automatically, and the executable and its diagnostic table are not replaced during the run. An unsupported execution target is an execution error. The working directory is fixed to the target project root, and each case receives a unique temporary directory that is reclaimed after every outcome. The parent's environment is captured once and project settings are applied; then only reserved case-specific settings, including TMP/TEMP, are overridden. The [test profile](testing-profile.md) defines the temporary-directory API, environment, stdin and limits. Separate per-case stdout and stderr streams are drained continuously, without interleaving the stored logs of different cases.

External files, databases and ports may remain shared. Tests use case-specific resources or the serial mode of §20.9 where needed; temporary-directory recovery performs no user defer or external rollback. Parallel cases are separate processes and add no language-level threads or memory model.

The parent's monotonic clock measures a finite execution deadline and the recovery grace. The execution deadline covers child launch through exit, including runtime preparation, initialization, body, cleanup, shutdown and completion reporting, but not build or queue time. Normal exit, abnormal exit, timeout and cancellation enter the same bounded recovery process. Timed-out and cancelled cases are stopped, and remaining managed descendants are recovered even after a normal child exit.

The process group is managed from launch with an OS management unit or equivalent; later PID enumeration alone is insufficient. The recovery grace bounds process termination waits, channel draining and EOF waits, and temporary cleanup. Surviving processes and unrecovered resources are reported after the grace, preserving the original termination reason. Storage still used by a live process is never claimed as recovered. Forced termination does not guarantee user cleanup. No guarantee extends to external processes outside OS management. An unrecoverable resource shortage fails the run. The [test profile](testing-profile.md) defines the adopted limits and management mechanism.

### 22.6.3. Diagnostic identity

| ID | Identity |
| --- | --- |
| ArtifactId | Executable and static diagnostic table for the compiler, target, settings and inputs |
| TestId | A test declaration within its project |
| CaseId | One case of a definition; initially one default case per TestId |
| SiteId | A verification site within the artifact, with source expression, location and display Types |
| IssueId | One recorded failure occurrence within a case, including repeat visits to one SiteId |

TestId and CaseId must be stable for the same source and settings and cannot depend only on display names, line numbers, absolute paths or execution order. TestId is distinguished by project, Container, Signature and, where needed, relative source path. Combined with the within-definition case identity, it makes CaseId unique in the execution scope. Renaming or moving declarations need not preserve IDs. SiteId display strings and locations are generated from the current source under §18.7.4, including when semantic plans are reused; changed diagnostic tables affect ArtifactId. Mod source-observation invalidation is preserved.

Unique integer IssueIds connect basic failures, saved values and added messages; details are never attached to an implicit last failure. Nested verifications during a message receive their own IDs. A basic failure is preserved even if no details arrive. Omitted failures need no individual ID. IDs are never reused or wrapped; exhaustion switches to bounded detail omission (§22.6.5).

### 22.6.4. Reporting and result classification

Results use a channel separate from stdout and stderr. At startup, the ArtifactId and CaseId are matched, and results for another artifact are rejected. Per-case event order is preserved, and counts, failure state, omission data and completion are validated. Each retained basic failure is sent independently before subsequent user condition cleanup or message execution, not only at child exit. Data the parent has received survives a later child Abort. A reporting failure is an execution error, never success.

On a normal path, the child reports completion after shutdown and exits with code zero; the completion information carries verification failures. This exit code is distinct from the overall CLI exit status (§20.9). **Success requires valid completion, normal child exit, consistent communication, completed recovery and no verification failure.** A normal case with no verifications succeeds. Neither exit zero nor a completion message alone establishes success.

| Outcome | Report |
| --- | --- |
| All success conditions met | Success |
| Normal completion with verification failures | Verification failure |
| Failed `$require` reaching its Abort, reliably identified | Verification failure with Abort termination |
| Other Abort or crash | Abnormal termination, retaining known verification failures |
| Execution deadline exceeded | Timeout |
| User cancellation | Cancelled, including unstarted selected cases; not success or executed skips |
| Startup, communication or recovery failure | Execution error, retaining known termination reason and verification failures |

Failure records, the termination reason and management errors are kept separately; a recovery failure cannot overwrite an earlier Abort or timeout. Abort reasons are inferred only from reliable received information, never from exit code 1 alone. Missing diagnostics may yield an unknown abnormal termination; user code and cleanup never resume after Abort. Cases excluded by a filter are not executed skips.

A `$require` failure neither produces normal completion nor resumes the test function; the parent recovers that case's process and continues managing other cases. A `$require`-initiated Abort is identified from reliable termination information associated with its verification site. A recorded false condition alone does not establish that reason: condition cleanup, message evaluation or message cleanup may Abort or diverge before the operation reaches its own Abort. In those cases, both the already recorded failure and the actual termination reason are retained.

Results are provided in human-readable and versioned machine-readable forms under the [test profile](testing-profile.md). They include the project, target and settings, IDs, source locations and expressions, retained values and messages, phases, durations, termination state, management errors, log paths and omission information. Terminal formatting is not the machine-readable interface.

### 22.6.5. Bounded diagnostics and storage

Per-case and whole-run budgets limit diagnostic counts and bytes, covering failures, saved values and messages, stdout and stderr, and disk spill. An initial storable portion and omission information are retained. Message retention, frames, pending queues and read buffers are bounded. Once budgets are exhausted:

- Ordinary condition, failure-message and cleanup evaluation continues; only storage is omitted.
- Failure state stays latched. Separate finite space is reserved for first-failure notification, completion and abnormal-control information.
- Counts and omission counts saturate and display lower bounds; they never wrap. Abnormal termination reports the observed lower bounds when final counts are unknown.
- The child also limits detail output; omitted events are never transferred into an unbounded parent/child queue.
- Communication is still drained, required information processed and excess discarded, within the execution and recovery bounds.

Intentional detail omission is not an extra execution error. Missing or corrupt required control data, or a failed write of retained data, is an execution error. A storage budget does not bound allocations performed by the user's own message expression.

Verification lowering is shared; only the failure continuation changes (§17.5). Static expression, location and Type tables and compact ID/value events are used, with formatting in the parent. Parent worker slots and buffers are reused, and per-case state is reset. The immutable artifact snapshot is validated once at run startup instead of being copied or revalidated per case, while each child's ID handshake is retained. Reflective registration, per-verification UUIDs and heap objects are not required. Measurements must cover empty or short, I/O-heavy and failure-heavy cases, including startup and recovery, time, parent and child memory, communication and generated code size; no unmeasured speedup is promised.
