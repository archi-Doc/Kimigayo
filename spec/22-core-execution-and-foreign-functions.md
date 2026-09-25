# 22. Kimi, program execution, and foreign functions

[Specification index](../SPEC.md)

This chapter defines the required declarations of the Kimi Kotonoha, process startup and shutdown, the foreign-call boundary, and minimal standard output. Kimi names the foundation library; Core remains a Type component.

<a id="221-required-core-declarations"></a>

## 22.1. Required Kimi declarations

The library also provides the intrinsic `Kimi.Sealed` and `Kimi.ObjectPayload` requirements (§8.4.7) and the ordinary generic declarations `Kimi.Intrinsics.replace`, `Kimi.Intrinsics.exchange`, and `Kimi.Intrinsics.swap` (§15.7). Recognition uses the original Kimi declaration Identity, not names or user conformance. Their generic signatures impose no Sealed requirement; completeness is checked at each actual storage target.

Every Compilation binds exactly one compiler-compatible **Kimi Kotonoha**, using the reserved direct reference name `Kimi`. User dependencies and project-root declarations cannot use that name. It is not a keyword; inner scopes follow normal shadowing. `::Kimi` bypasses locals and aliases. The Types and Contracts below are public at the library root. Ownership operations belong to the public `Intrinsics` group (§22.1.1); `writeLine` belongs to the public `Console` group (§22.4). The mandatory default alias opens Kimi under §18.1.3. Qualified paths such as `::Kimi.Option<T>` identify them regardless of local shadowing. Compiler metadata records their originating Kotonoha/version and Symbol Identities; a same-spelled user declaration or replacement alias never receives their special behavior. Reject a missing, duplicate, or incompatible Kimi definition before finalization. The compiler may synthesize these definitions, but synthesized and loaded definitions must have the same language identities and contracts. Kimi itself is built with these identities designated by the compiler.

This is the minimal set named by language rules, not a promise of a general standard library:

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
| `Iterator`, `Cursor`, `UniqCursor` | The exact declarations of §22.1.2.1: `Item(step)` with its formation clause and `next(self: uniq/Self during step) -> Option<Self.Item(step)>`; Cursor's `Element`, `advance` and Place-publishing `current`/`currentUniq` |
| `Iterable`, `UniqIterable`, `IntoIterable` | The exact declarations of §22.1.2.2: `IteratorType(source)` or `IteratorType` bound to an Iterator, and `iterate`, `iterateUniq` or `intoIterator` |
| `IndependentIterator: Iterator` | `associate StableItem`; `associate Iterator.Item(step) is StableItem`; the verified independence guarantee of §22.1.2.4 |
| `Indexable<Key>`, `UniqIndexable<Key>: Indexable<Key>` | `associate Element`; `index(self: ref/Self, key: ref/Key) -> place(ref, Element) during self` and `indexUniq(self: uniq/Self, key: ref/Key) -> place(uniq, Element) during self` (§4.6.9) |
| `Iteration` group | `Owned<I>`, `Borrowed<I>`, `Shared<C>`, `Uniq<C>` and `owned`, `borrowed`, `shared`, `uniq` under §22.1.2.3 |
| `Storage` internal group | `RefRemainder<S>`, `UniqRemainder<S>`, `OwnedRemainder<S>`, `borrowStorage`, `ownStorage`, `splitFirst`, `takeFirst` under §22.1.2.5; usable only inside the Kimi Kotonoha |
| Copy, Owned, Callable, Sealed, ObjectPayload | Compiler-intrinsic requirement identities with exactly their existing derivation, ownership, and call rules; they are not ordinary user-implementable replacements |
| Object ownership intrinsics | Kimi.Intrinsics.makeObj / makeRc / makeArc, strong and Weak Kimi.Intrinsics.clone, Kimi.Intrinsics.downgrade / upgrade, Kimi.Intrinsics.makeRcCyclic / makeArcCyclic, with §13.5.8–9 names, Types and acquisition contracts; every creation declares `T is ObjectPayload` (§8.4.7.2) |
| Whole-value update intrinsics | `Intrinsics.replace`, `Intrinsics.exchange`, `Intrinsics.swap`, with §15.7 signatures and acquisition/destruction contracts |
| `Console.writeLine` | Overloads `(text: ref/string) -> ()` and `(text: Text.Utf8Slice) -> ()`; §22.4 and the [formatting profile](utf8-formatting.md#61-console-output) |
| `Test.tempDirectory` | `public func tempDirectory() -> string`; independently owned case-directory path, restricted to test-only bodies under the [test profile](testing-profile.md#environment-and-temporary-directory) |

The iteration and indexing Contracts are static Contracts whose associated Types are complete Types with the Origin parameters of §8.4.3.1; `Item(step)` may depend on the receiver borrow of each `next`, so lending iterators are ordinary conforming Types. Table signatures follow the normal associated-Type, receiver, result-Origin and lifetime rules.

Fixed arrays conform to the three iteration entries with the items of §14.6.2. Owning Array/Dictionary iterators retain and destroy unyielded elements in §4.7.6 order. ResolvedRange and Slice use concrete Kimi iterator identities with §4.6's item Types and dependencies: range iterators store position/end; Slice iterators store a copied handle, position, and external source Loan. Neither owns yielded elements, and all standard iterators stay exhausted after None and conform to `IndependentIterator`. Dependent Types preserve source dependencies through associated Types and Option payloads. Receiving next's result extends no lifetime. These requirements add no public iterator constructors.

The primitive keyword string denotes the compiler's UTF-8 string Core, not a shadowable alias. It supports literal/interpolation construction, concatenation, comparison and Utf8Format. The [formatting profile](utf8-formatting.md) defines separate mutable buffers, validated views and `Text.toString` for string copying; it adds no character indexer or formatting options. Fixed-array syntax and layout follow [sequence Types](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices); metadata, indexed Place acquisition, and shared reading follow [indexing and slicing](04-arrays-indexing-and-slices.md#46-indexing-and-slicing).

For Option/Result Copy conditions, compare atom sets using §8.7's proposition identity and conjunction elimination. T/E denote the corresponding parameter slots and Kimi.Copy the recognized Symbol. Order, transparent grouping, and duplicate atoms do not change the set; missing/unconditional Copy, missing/extra atoms, or different identities are incompatible. Retain all other required-shape checks without general logical-equivalence reasoning. Generated sources may use canonical T, E order, but loaded Kimi definitions cannot be required to use that order.

Option/Result Copy and Owned follow ordinary enum rules; no extra copying is introduced. A changed Kimi contract invalidates dependent capability, acquisition, and generation results under §21.3.4. Unchanged Case order and payload structure do not establish binary compatibility with older Kimi artifacts.

Array/Dictionary contents, generic enum payloads, and fixed-array elements preserve complete Type/Origin/Loan dependencies under §15.4. Array's Owned classification follows T, Dictionary's follows K and V, independently of runtime contents; both remain Non-Copy. No container grants permission to hide dependencies or extend a referent's lifetime. Checked-cast designs use the required Kimi Option Identity despite deferred View syntax. Dictionary need not expose hashing. Dynamic mutation, allocation, ordering, retained dependencies, effects and complexity follow §4.7; further library APIs remain separate designs.

### 22.1.1. Declaration placement and function reference

`Kimi.Intrinsics` is a public, non-generic group with no Origin parameters. It contains the whole-value update and object ownership operations as one family. `Copy`, `Owned`, `Callable`, and `Sealed` remain directly under `Kimi`; `writeLine` remains under `Kimi.Console`. The Console, Intrinsics and Test groups are not opened recursively by the default Kimi alias: use `Intrinsics.replace(...)` / `Console.writeLine(...)`, a fully qualified path, or an explicit alias that opens the corresponding group. A named alias such as `alias Memory => Kimi.Intrinsics` preserves the original declarations' Identities. There are no root-level compatibility declarations such as `Kimi.replace` or `Kimi.makeObj`.

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

Container members stay with their owning Types and Contracts: the iteration, Cursor and Indexable requirements are listed in §22.1.2 and §4.6.9; collection, indexing, range and Slice APIs are defined in §4.6–7; comparison requirements are listed in §22.1; formatting members are in the [profile](utf8-formatting.md). `Text`, `Iteration` and the internal `Storage` group, like Console, are not recursively opened by the default alias. `Option.Some` / `None` and `Result.Ok` / `Err` are enum Cases. Compiler built-ins such as `$abort` and `$tryWrite` are not declarations in these groups.

This reference specifies required APIs, including unimplemented ones. [STATUS.md](../STATUS.md#kimi-library-and-whole-value-updates) records current declaration and runtime coverage separately.

### 22.1.2. Iteration, cursors and storage

#### 22.1.2.1. Iterator and Cursor

```kimi
contract Iterator
    associate Item(step)
        wellformed uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.Item(step)>

contract Cursor
    associate Element
    func advance(self: uniq/Self) -> bool
    func current(self: ref/Self) -> place(ref, Element) during self

contract UniqCursor: Cursor
    func currentUniq(self: uniq/Self) -> place(uniq, Element) during self
```

An Iterator delivers its next item as a value; each `next` binds `step` to the receiver borrow of that call and keeps the actual Loan dependencies. A general Iterator may return `Some` after `None`; `for` stops at the first `None`, and the standard guarantees are in §22.1.2.3. `None` carries no Loan.

| `Item(step)` | Retention and the next `next` |
| --- | --- |
| `E` | Follows the value's own ownership and internal dependencies; independence from the call is verified separately |
| `ref/E during step`, `uniq/E during step` | Depends on that call's receiver borrow and conflicts with the next `next` while retained |
| `ref/E during source` and similar | An external borrow proven independent of the receiver; the required `source` protection remains |

An annotation alone removes no receiver dependency, and `I is Iterator` alone does not permit a retained item across the next call; use `IndependentIterator` (§22.1.2.4). A Cursor publishes the Place of its current position: after creation and after a false `advance` there is no current position and `current`/`currentUniq` Abort; after a true `advance` the same logical element is selected until the next `advance`, with repeated shared or exclusive access, and resuming after `false` is allowed. State management is the implementation's responsibility; the compiler proves no state machine and inserts no flags. Conflicting `advance`, updates and destruction are rejected by the ordinary Loan rules.

#### 22.1.2.2. Iteration entries

```kimi
contract Iterable
    associate IteratorType(source) is Iterator
        wellformed ref/Self during source
    func iterate(self: ref/Self during source) -> Self.IteratorType(source)

contract UniqIterable
    associate IteratorType(source) is Iterator
        wellformed uniq/Self during source
    func iterateUniq(self: uniq/Self during source) -> Self.IteratorType(source)

contract IntoIterable
    associate IteratorType is Iterator
    func intoIterator(self: Self) -> Self.IteratorType
```

The three capabilities are independent; `for` requires the conformance of the entry its Subject mode selects (§14.6.2). The borrowing entries keep the per-call `source`, and the owning entry keeps the dependencies inside `Self`. The item Type is `Iterator.Item(step)` of the selected `IteratorType`; the entry mode forces neither `ref`, `uniq` nor an owned item. `UniqIterable` guarantees only the exclusive receiver borrow; the exclusive Place results of `UniqIndexable` and `UniqCursor` are guarantees of those requirement signatures, not of the name `Uniq`. A Type may conform to `Iterator` and to entries at the same time. There is no duck typing by member name, no automatic conformance of references or arbitrary Iterators, no derived Contract or default body, and no standard conformance that advances an Iterator through a shared borrow.

```kimi
struct Countdown
    Self is Iterator
    Self is IntoIterable
    Self is UniqIterable
    associate Iterator.Item(step) is i32
    associate IntoIterable.IteratorType is Self
    associate UniqIterable.IteratorType(a) is Kimi.Iteration.Borrowed<Self>{view}
        origin view.source == a
    var remaining: i32 = 3

    public func next(self: uniq/Self during step) -> Option<i32>
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

The public group `Kimi.Iteration` declares the following functions and result Types. `I` is an Iterator and `C` a Cursor; each result is an ordinary generic struct that conforms to `Iterator`, `IntoIterable` (transferring itself) and `UniqIterable` (returning `Borrowed<Self>`).

| Operation | Result Type | Contract |
| --- | --- | --- |
| `owned(iterator)` | `Owned<I>` | Takes `I` by value and forwards its `Item(step)` and dependencies; a Non-Copy Place is written `@move` |
| `borrowed(iterator)` | `Borrowed<I>` | Takes `uniq/I` and advances it without Moving it; items keep `I`'s contract |
| `shared(cursor)` | `Shared<C>` | Takes `C` by value; after a true `advance` it borrows `current` and returns `ref/Element during step` |
| `uniq(cursor)` | `Uniq<C>` | Takes a `UniqCursor` by value; after a true `advance` it returns `uniq/Element during step` from `currentUniq` |

`uniq` is a contextual word only in Semantics positions, so `Kimi.Iteration.uniq(cursor)` is an ordinary call, distinct from the borrow `@uniq`. `Borrowed`'s slot `source` is the outer Origin of its input, and each item's `step` is the actual `next` Reborrow. `Owned` and `Borrowed` inherit their input's exhaustion guarantee and conform to `IndependentIterator` exactly when `I` does, with the same `StableItem`, dependencies and effect bound. `Shared` and `Uniq` are lending Types whose items depend on `step`; they keep their own finished state, call `advance` once per `next`, call the corresponding `current` only after `true`, and after the first `false` return `None` without calling the Cursor again, so they are exhausted regardless of the Cursor's later behavior. An Option payload is an ordinary reference value, never a Place. No adapter allocates, updates a reference count or materializes items in advance. Draining is an ordinary Iterator API that publishes how items are taken and how an early exit treats the remainder; it is not a language protocol.

```kimi
// iterator: a writable owned variable conforming to UniqIterable.
for item in iterator@uniq
    inspect(item)
    exit // The remaining items stay; iterator is reusable once the retained Loans end.
```

Every standard collection iterator of §14.6.2 keeps returning `None` after the first `None`, conforms to `IndependentIterator`, and, for exclusive enumeration, proves the non-overlap of its items by region splitting (§15.6.3). An owning iterator owns the unreturned elements and transfers only the returned element's responsibility; a borrowing iterator never destroys the collection.

#### 22.1.2.4. Independent items

```kimi
contract IndependentIterator: Iterator
    associate StableItem
    associate Iterator.Item(step) is StableItem
```

`IndependentIterator` guarantees that a retained item survives the next `next` and a Move of the Iterator. `StableItem` is the complete Type of the delivered value, independent of `step`; it says nothing about immutability or the stored element Type. `for` does not require this conformance. For every valid `step` and admitted Type binding, the conformance requires:

1. the item's complete Type is `StableItem`, independent of `step`;
2. a returned item depends on neither the receiver Loan of `next` nor the Iterator's own Storage, while dependencies on the external source, the owner and needed parent Loans remain;
3. for exclusive region splitting, the returned parts and the remainder's anchors are non-overlapping, and moving or freeing the Iterator's Storage invalidates no returned part;
4. the effects of `next`, including reads, writes, borrows, the Loans of its result, access to statics and captures, and cleanup inside the call, conflict with no Loan that the same Iterator formed or kept when it returned an earlier item, including Loans kept by ordinary transfer or Reborrow of that item; a Move of the Iterator carries the guarantee.

Item 4 is the published effect bound of `next`, verified at conformance with the common root, Loan and effect summaries and the region-splitting rules; every implementation, including specializations and callees, must satisfy it, or the conformance is not published. User Types declare `Self is IndependentIterator` and undergo the same verification; a Type equality alone proves nothing, and nothing is derived from `Iterator` automatically. Generic callers use the published bound (§15.6.4, §21.3.4); it promises no purity, and Loans unrelated to items, other operations and result lifetimes are checked as usual. The `next` of a standard collection iterator only traverses, splits and transfers; it calls no user comparison, destructor or callback.

```kimi
func nextPair<I>(iterator: uniq/I)
    -> (Option<I.StableItem>, Option<I.StableItem>)
    I is IndependentIterator
    let first = iterator.next()
    let second = iterator.next() // The published effect bound permits retaining first.
    return (first@move, second@move)
```

Destruction or replacement of the whole Iterator is a separate effect: a remainder destructor that conflicts with a retained item is rejected. Standard borrowing iterators end only their handle; owning iterators follow the destruction summary of the remaining element Type. A generic accumulation of items from a borrowing Iterator is possible, while an owning `collect<I>(it: I)` must also prove that results and cleanup do not conflict; unknown effects are never treated as empty, and no hidden call condition postpones the check to instantiation. Dependencies a user adds after an item was returned are checked ordinarily, as is protection of the original collection. A delegating wrapper that borrows itself for its result, or adds conflicting effects to `next`, loses independence. For a general Iterator, an item is used up before the next call, or non-conflict is proven from the published contract case by case.

#### 22.1.2.5. Standard storage boundary

The internal group `Kimi.Storage` holds the operations that split standard collection Storage into non-overlapping regions. The group, its Types and its operations are `internal`: only the Kimi Kotonoha itself uses them (§9.3), no alias, re-export or same-spelled declaration grants the capability, and public Iterators keep them in private Fields without exposing them in results or associated Types. No public exclusive Slice or raw-pointer conversion is added.

| Type | Responsibility |
| --- | --- |
| `RefRemainder<S>` | The shared Loan of its `source` slot and the traversal position of the untaken part; it does not own `S` |
| `UniqRemainder<S>` | The parent Loan of its `source` slot and exclusive access to the untaken part; it does not own `S` |
| `OwnedRemainder<S>` | The Storage transferred from `S` and the destruction responsibility for unreturned elements, with `S`'s internal dependencies |

`S` is one of `Array<E>`, `[N of E]` or `Dictionary<K, V>`; a borrowing remainder requires `ref/S during source` or `uniq/S during source` to be well formed, and each operation is an overload with the corresponding `E`, `K`, `V` and, where needed, `length N` and the ordinary Type constraints. The result families below are descriptive: `R(a)` is `ref/E during a` for arrays and `(ref/K during a, ref/V during a)` for Dictionary; `U(a)` is `uniq/E during a` and `(ref/K during a, uniq/V during a)`; `O` is `E` and `(K, V)`.

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

Borrowed results depend on `source`, never on the `state` borrow or slot; the shared form needs no non-overlap proof but delivers each element once in order. The following hold for empty and zero-sized Storage too:

1. the constructed target is complete; representation, construction, duplication and initialization-state changes are confined to these operations, and ordinary Field operations grant no capability;
2. `splitFirst` and `takeFirst` return `None` when nothing remains and otherwise the first untaken element exactly once, in the order of §14.6.2, calling no user callback, comparison or destructor;
3. an `OwnedRemainder` remains a complete handle over the unreturned part; it publishes neither a reference to the whole `S` with holes nor Take through a borrow;
4. destroying a `RefRemainder` or `UniqRemainder` ends only the capabilities it holds; destroying an `OwnedRemainder` destroys the unreturned part once in the ordinary cleanup order and frees the region, ending no returned Loan or responsibility and publishing the external effects of that destruction.

The internal operations obey the complexity bounds of §4.6.8. Array and fixed-array traversal keeps the valid untaken range as a start and count, so a nonempty check proves the next element valid without a public `index` call or a second bounds check; Dictionary traversal uses the ordering information of live entries, distinct from hash-probe tombstones, and never charges a capacity scan to "amortization". This built-in boundary guarantees Storage validity, dynamic non-overlap and initialization state; its capability is bound to the standard declaration identities, never to spellings. Everything else an Iterator does, including user delegation, is checked ordinarily, and no user Storage can register with the boundary.

**Published effects.** Result anchors, parent Loans, remainder non-overlap and the effects on statics, captures and cleanup enter the public summary of each operation (§15.6.4); generic, separately compiled and indirect calls compose those summaries without reanalyzing private bodies, treat unknown effects conservatively, and never erase an existing Loan because a conversion or erasure dropped a guarantee. An Unsafe designation grants neither independence nor a longer Origin. The `next` of an `IndependentIterator` carries the effect bound of §22.1.2.4, checked at each use separately from the whole-value destruction summary; element-dependent cleanup uses the symbolic summaries of §4.7.5. No general effect syntax or runtime tag is added.

## 22.2. Program startup and static initialization

### 22.2.1. Startup selection

After directive selection, Mods, and Binding, an Application must select exactly one of:

- One SourceDocument with top-level runtime body items.
- One eligible root-level `public func main() -> ()`.

Reject mixed forms, multiple candidate documents/mains, or no candidate. Search the current project, not dependencies; enumeration order and optimization never select the winner. EntrySource is not an initial selection mechanism, and naming an empty document cannot make an Application valid.

Determine HasTopLevelRuntimeBodyItem once per document from selected root items, without descending into functions or Containers. This classification does not require that machine instructions survive:

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

Execute the chosen document's body items in source order with its source scope, CodeContext, local-function visibility, lifetimes, and cleanup. Lower it to a private internal function, without synthesizing public main. This physical function does not create a source-level return target (§14.5.3).

```kimi
// Implicit startup; the uninitialized local itself also counts.
let pending: i32
::Kimi.Console.writeLine("Hello, world!")
```

A minimal intentionally empty Application is the Unit expression `()`. Empty files and declaration-only files supply no implicit body.

### 22.2.2. Explicit main and Library

An Application's explicit main is exactly lowercase main, public, directly at the source root, with no parameters/receiver, generic or Origin parameters, and Unit result (ordinary result omission is allowed). It is a safe ordinary function with a body, not unsafe, a foreign import, or a specialization. A main inside a group/struct or another function is not a candidate. Root-level public main is shared-root declaration syntax under §6.1.1, retaining declaration-site aliases; public promises no unmangled native symbol or export.

Validate every root-level public main in an Application as a startup signature. Diagnose invalid declarations rather than selecting a convenient overload. Normal Unit return and fallthrough are permitted; no special ownership rules apply.

```kimi
public func main() -> ()
    let message = "Hello, world!"
    ::Kimi.Console.writeLine(message)
    // message is borrowed for output and remains usable.
```

Adding a top-level `::Kimi.Console.writeLine("Top level")` to this project is an error because it mixes startup forms. Integer-returning main and a safe Kimi.exit API are not initial features; normal termination is 0 and Abort is 1. Runtime.Exit remains internal.

A Library requires no startup candidate, never automatically calls main, and emits no OS entry. Treat main as an ordinary function without the Application signature restriction. Reject top-level runtime body items, including uninitialized let/var. A Library may be consumed as a Project or distributed as a source package (§18.4–18.6), then participate in common final generation. Its inspection `.ll` is not that distribution format or an external library/DLL ABI; language functions remain internal even when public. Optimization may remove all functions from standalone inspection output, so inspect pre-optimization IR.

### 22.2.3. OS entry, static initialization, and shutdown

The initial Windows Application emits compiler-reserved external `__kimi_start`, physical signature void (), Windows x64 ccc, noreturn, and the profile attributes (§21.5). Link with /entry:__kimi_start. It performs required runtime initialization, calls the selected body once, completes normal shutdown, and calls Runtime.Exit(0). Empty initialization/shutdown helpers may be omitted. Ordinary mangling prevents source names from colliding with this reserved symbol.

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

The entry body is the implicit body or a call to the selected main. Add required runtime initialization before it; heap/standard-handle acquisition may instead occur inside each runtime operation.

The entry handles Kimigayo initialization/cleanup; it does not run executable CRT startup or C/C++ static constructors. Foreign initialization must already be satisfied, for example by OS DLL loading, or by an explicitly supported adapter. /NODEFAULTLIB is not initialization.

Static stored Properties initialize per slot under §11.3.2. A first read, Borrow, write, or other storage operation checks:

| State | Action |
| --- | --- |
| Not started | Mark Initializing; evaluate the declaration initializer; after normal completion mark Initialized, then perform the operation |
| Initializing | Abort for an initialization cycle |
| Initialized | Perform the operation without rerunning initialization |

A first write initializes before Replacement. Actual access determines dependency order, not fragment/file/link order. Computed execution initializes only storage actually accessed. A Type/function reference, untaken branch, or effect summary initializes no unrelated Field. Check all initializers even if unused; unused Fields need not initialize and are not destroyed. An implementation without static execution support diagnoses required uses rather than treating this specification as an implementation.

Normal body exit cleans its locals exactly once. Then destroy initialized static values in reverse successful-initialization order, retaining required lifetime dependencies; reject dependencies that cannot survive this order. Initializing new static storage, accessing destroyed storage, or reentering a Field's destruction during shutdown Aborts. These language rules include dependencies, although multi-Kotonoha linking is outside the initial profile.

Cleanup must finish before subsequent cleanup or Exit. Abort stops normal cleanup/unwinding and attempts diagnostics before Runtime.Exit(1) (§17.3, §22.5); secured results are not delivered or separately destroyed afterward.

This revision admits one execution thread, with no source thread creation or concurrent foreign reentry. Atomic arc counts do not expand that permission; synchronization and thread transfer remain §D.2's design boundary.

### 22.2.4. Static storage in inherited environments

A Field directly in a group is static even when that group is nested in a struct. Its storage key is the Field declaration Identity plus normalized enclosing bindings with Origins recursively erased. Retain Type structure and every Semantics layer, including unused arguments. Distinct keys have distinct storage; code sharing never merges them. Different paths to one key use one initialization state, Loan/effect identity and destruction responsibility.

```kimi
struct Cache<T>
    public group Statistics
        public var count: i32 = 0
```

Cache<i32>.Statistics.count and Cache<string>.Statistics.count are separate. Cache<ref/i32 during a>.Statistics.count and the corresponding b reference use one key after both full references pass their checks.

Require the stored value to be Owned and satisfy existing static-storage conditions; do not require every enclosing Type argument to be Owned. Preserve full Origins for access and lifetime checking. Under §8.10 and §21.3, verify one representation, initialization and destruction plan for all valid Origin bindings sharing a key, including the required operations, evidence and callees. Owned storage alone does not prove initializer shareability. Reuse verified typed plans and finalize concrete layout at instantiation; do not choose by the first accessing Origin or split failing plans into Origin-specific storage. Initializers may depend on ordinary runtime state.

Apply §22.2.3 lazy initialization, initialization-cycle Abort and reverse-initialization shutdown destruction. Type/function references alone do not initialize Fields. Check unused Field declarations and initializers, without requiring their runtime initialization or destruction.

Shared code reaches a key through a supplied initialize-and-address operation (§21.3.3), fixed directly or represented by an existing entry/context pair. Its immutable private context retains storage/state references and required initializer/destructor operations; mutable initialization state stays with storage. No new GenericContext slot kind, per-call context construction or runtime Type search is required. Direct-call optimization must preserve initialization checks, cycle detection and storage Identity.

## 22.3. Foreign function imports

### 22.3.1. Declaration and call contract

`#LibraryImport("library", "symbol")` on a bodyless unsafe func selects the target C calling convention. Both arguments are required nonempty, non-interpolated, NUL-free string literals. The first is a case-sensitive logical native requirement name belonging to the defining Kotonoha (§20.8.2), not a consumer alias or DLL path. Requirements may come from NativeRequirements or a self-targeted combined NativeLibraries record. The second argument is the exact external symbol, independently of the source function name. Actual supply/kind and member-closure validation occur under §20.8.2 without replacing the following source/ABI obligations.

Allow imports only directly in group/rootgroup or as receiverless struct type functions. Reject receivers, generic/Origin parameters, parameter defaults, varargs, specializations, and executable bodies. Ordinary parameter-name rules, including the `!` boundary and external/internal renaming, apply; name contracts change source argument matching only, not the foreign ABI. Every argument value is required. Calls are direct only; unsafe functions cannot be acquired as values. Ordinary access and unsafe-call rules apply.

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    public unsafe func observe(record: unsafe/NativeRecord) -> ()
```

NativeRecord can be the C-layout example in §21.1.3; the corresponding C declaration is `void observe_record(NativeRecord *record);`. The raw pointer is not read-only. Layout, validity, lifetime, writes, retention, ownership, and active Loans remain the caller's contract; this example supplies no new pointer-acquisition or raw-storage construction API.

Acquire arguments once from left to right, then use the selected ABI. No automatic marshalling, retention, allocation, freeing, or ownership acquisition occurs. Normal return resumes ordinary cleanup. C++ exceptions, SEH unwind, longjmp, callbacks, and reentry must not cross Kimigayo frames; control handled entirely inside the foreign code is allowed. Violations carry no result or cleanup guarantee. Apply the FP boundary contract in §21.5.4. Do not infer nounwind merely from these source restrictions or generate a landingpad to catch violations.

Unresolved logical libraries and unsupported ABI signatures are compilation errors. Record required link inputs; unresolved native symbols fail manual linking/loading before entry. C aggregate passing, export, callbacks, varargs, and extra calling conventions remain extensions; C storage layout is specified separately and does not enable them.

### 22.3.2. Initial Windows C ABI

Compute a physical signature once and share it between declare and call:

| Kimigayo parameter/result | C value | LLVM Type |
| --- | --- | --- |
| i8 / u8 | int8_t / uint8_t | i8 |
| i16 / u16 | int16_t / uint16_t | i16 |
| i32 / u32 | int32_t / uint32_t | i32 |
| i64 / u64 | int64_t / uint64_t | i64 |
| f32 / f64 | float / double | float / double |
| unsafe/T | Corresponding data pointer | ptr, address space 0 |
| Unit, result only | void | void |

Use ccc with no signext, zeroext, inreg, byval, or sret for these entries. Do not widen i8/i16 to i32 or apply vararg default promotions. Other numeric conversions are separate language operations. LLVM handles registers, stack arguments beyond the fourth, shadow space, and stack alignment; unused upper bits are not meaningful. Additional optimization attributes need independent proof.

```llvm
declare dllimport i8 @native_i8(i8)
declare dllimport i16 @native_u16(i16)
```

Exclude bool, char, string, borrows, object handles, aggregates (including C-exchangeable structs/arrays), function/closure values, i128/u128, and isize/usize. Raw pointees are not passed by value and need not be C-exchangeable when opaque. Future aggregate passing needs separate argument/result C ABI classification and tests, not direct translation to LLVM aggregate parameters.

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

There is no `Kimi.writeLine`, old `Core` compatibility reference, or forwarding API. `Core` is an ordinary user name. Console is a group, not a value or special syntax. An intrinsic is a declaration whose Identity has compiler-recognized meaning; no public `Intrinsic` namespace or `Kimi.Intrinsic` group is introduced. Aliases retain that Identity. `$abort`, `$expect`, `$require`, and the Composition Root retain their existing roles.

Kimi provides the public ordinary overloads `writeLine(text: ref/string) -> ()` and `writeLine(text: Text.Utf8Slice) -> ()` in its ordinary public `group Console`. The mandatory Kimi default alias makes `Console.writeLine` available. It does not recursively open Console. `::Kimi.Console.writeLine` identifies the required Symbol regardless of local shadowing. Each is safe and nongeneric, with one required argument and no receiver, defaults, formatting parameters or result borrow. The string overload borrows; the view overload copies its borrowed handle. The compiler/runtime supplies its implementation; it is not a source LibraryImport taking a string and does not expand the FFI Type surface.

Borrow text for the call under the ordinary argument adaptation rules, so a string Place stays usable and a literal, interpolation or `Text.toString` result is materialized and borrowed as a temporary, and write all its UTF-8 bytes plus one LF to standard output. NUL is data. Preserve contents without normalization, CRLF conversion, or locale encoding. Failure may leave partial output; no rollback or device atomicity is promised. Return Unit after host acceptance and flushing this call’s runtime buffer, not necessarily display or durable storage. Failure to complete initiates Abort under normal diagnostic/termination rules, even if stdout is unavailable. The call destroys nothing; a temporary argument follows its ordinary lifetime. Diagnose an unsupported target/feature if the backend cannot provide this operation.

**Complete application example (implicit startup in one SourceDocument).**

```kimi
::Kimi.Console.writeLine("Hello, world!")
```

The required standard-output bytes are UTF-8 `Hello, world!` followed by LF; normal completion exits with code zero under §22.2. No source main function, user alias, unsafe block, interpolation, or user-declared foreign function is needed. Passing an existing string local borrows it, so `writeLine(name)` leaves `name` usable. The UTF-8 view overload follows the same output rules. General I/O error/result APIs remain outside this minimal operation.

The initial Windows implementation of this operation is specified in §22.5; output settings, manifest, and manual build steps are in §20.8. The first executable implementation milestone and the distinction between existing and proposed settings are recorded in [STATUS.md](../STATUS.md#4-llvm-generation-coverage). A prototype supporting only that subset must identify itself as partial; the milestone does not relax the Kimi identity/shape or validation requirements of a fully conforming Compilation. Unused executable Kimi bodies need not be emitted, but a same-spelled stub without the required identity and contract is not a compatible Kimi definition.

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

Alloc, detected Free failures, and WriteStdout fail by Abort. TryWriteStderr returns false without initiating Abort. Abort attempts diagnostics then Exit(1); Exit never returns and performs no Kimigayo cleanup. The normal language path calls Exit(0) only after cleanup.

Physical helpers carry any required private diagnostic context using the compiler-selected internal ABI (§21.4.2); its position and representation are not fixed. Lowering preserves static logical path/line/column information for the original operation, including failures inside Alloc/Free/WriteStdout and generated-source CodeContext provenance. This does not depend on PDBs or stack traces.

Generated arithmetic checks report the start of the failing arithmetic expression. Compound assignment and increment/decrement report the start of the complete update expression. These locations remain the same across optimization levels.

### 22.5.2. Allocation and release

Use the process heap, MaxObjectSize = 2^63 - 1, and alignment support up to 16. Check length * stride, headers, and alignment rounding before allocation; overflow/limit failure Aborts. Alloc checks its own limit too, obtains GetProcessHeap, and calls HeapAlloc(heap, 0, max(size, 1)); null heap/allocation Aborts. It returns uninitialized raw memory, not an Initialized language value. The substitute byte for size zero does not change Type size/stride.

Free(null) succeeds without work. Otherwise require the original live pointer returned by this allocator, never an interior pointer or literal backing. GetProcessHeap failure or detected HeapFree(heap, 0, memory) failure Aborts. Lowered cleanup runs destruction before Free; Free itself invokes no destructor. Correct ownership/pointers are a static-analysis and generation duty; detection of double frees or arbitrary corruption is not guaranteed.

HeapAlloc flags remain zero, without exception generation or disabling process-heap synchronization. HeapAlloc does not supply last-error on failure; do not report a stale GetLastError value for null allocation. Obtain last-error immediately after APIs that provide it, including failed HeapFree/WriteFile.

HeapBuffer completion may use the [optional in-place shrink](utf8-formatting.md#34-optional-in-place-shrinking). This is a non-failing optimization, never a replacement allocation. An implementation using HeapReAlloc must request in-place-only behavior and retain the original allocation on failure.

### 22.5.3. Synchronous byte output

WriteStdout and TryWriteStderr share a checked byte-write adapter. They add no newline, NUL scan, encoding conversion, buffer, or pointer retention. Length zero succeeds before handle acquisition or pointer access. For positive length, require length <= MaxObjectSize, nonnull data, a readable live range within one allocation, and no unsigned 64-bit overflow in baseAddress + (length - 1). Numeric checks do not prove allocation validity/lifetime.

Use GetStdHandle(-11) for stdout and -12 for stderr; null or INVALID_HANDLE_VALUE fails. Do not close these handles. Initial support requires synchronous standard handles. WriteFile receives a 32-bit written-count slot and null OVERLAPPED; capture a supplied last-error before calling another API.

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

Do not compute a next pointer after completion. Partial writes advance by actual progress; never retry zero progress indefinitely. WriteStdout turns any detected failure into Abort; TryWriteStderr returns false and must not call Abort, WriteStdout, or Alloc. Output may be partial, with no rollback, atomicity, display, durability, or bounded synchronous-wait guarantee. Success means OS acceptance of all bytes. These checked buffer rules do not change general unsafe pointer arithmetic.

### 22.5.4. Abort diagnostics and exit

Fixed diagnostics use an ASCII identifier, English reason, and source location, optionally a valid numeric OS error:

Formatting also defines `KIMI_E_ARG_RANGE: Argument out of range` and `KIMI_E_FORMAT: Formatting failed`; their triggers are in the [formatting profile](utf8-formatting.md).

```text
Main.kimi:3:5: abort KIMI_E_STDOUT: Failed to write to stdout (win32=6)
```

Use unique catalog codes, including KIMI_E_ALLOC_SIZE (allocation size exceeds limit), KIMI_E_PROCESS_HEAP, KIMI_E_ALLOC, KIMI_E_FREE, KIMI_E_STDOUT, KIMI_E_INT_OVERFLOW (Integer overflow), KIMI_E_INT_DIV_ZERO (Integer division or remainder by zero), KIMI_E_INT_SHIFT_COUNT (Shift count out of range), KIMI_E_INT_CONVERSION (Integer conversion out of range), and KIMI_E_INDEX_BOUNDS (Index out of bounds). Integer division/remainder by zero uses KIMI_E_INT_DIV_ZERO; signed minimum with divisor -1 uses KIMI_E_INT_OVERFLOW for both operations. A shift count outside `0 <= count < left operand bit width` uses KIMI_E_INT_SHIFT_COUNT. Discarded left-shift bits do not trigger overflow. A runtime integer conversion outside the target range uses KIMI_E_INT_CONVERSION; direct literal fitting failures remain compile-time errors. Ordinary element indexing outside the receiver bounds uses KIMI_E_INDEX_BOUNDS, including constant indices and zero-length arrays. Dictionary indexing with an absent key uses `KIMI_E_MISSING_KEY: Dictionary key was not found`; an absent result from a try-prefixed operation is not an Abort. Omit unavailable OS codes; do not use FormatMessageW. In displayed logical paths, escape non-ASCII/control characters as `\u{HEX}` and backslash as `\\`; preserve the actual path/provenance internally.

Output diagnostics from constants, valid input strings, and small fixed work areas without requiring heap allocation or one concatenated dynamic string. Explicit `$abort(expression)` retains ordinary one-time argument evaluation and outputs the resulting UTF-8 string after KIMI_E_ABORT without translation or escaping its contents. Once Abort starts, do not normally destroy that string.

The Windows implementation's numeric conversion catalog uses `KIMI_E_FLOAT_CONVERSION: Floating conversion out of range` for a finite source that rounds to floating infinity. Float-to-integer failure, including NaN and infinities, uses `KIMI_E_INT_CONVERSION`. These codes identify the failures required by §13.5.4; direct literal fitting failures remain compile-time errors.

TryWriteStderr failure truncates diagnostics and proceeds to Exit(1), with no recursive diagnostic path. Runtime.Exit forwards u32 to ExitProcess and ends its caller block with unreachable. It runs no cleanup; successful shutdown and Abort reach it through their distinct §22.2 paths.

### 22.5.5. String handle and writeLine

The initial internal string storage representation is `{ ptr, i64, i8 }`: data, byteLength, releaseKind. Use DataLayout for padding/alignment. Function passing is selected independently under §21.4.2 and need not use aggregate argument/result slots. This storage representation is not a public FFI/binary ABI.

| Component/state | Validity and responsibility |
| --- | --- |
| Length | 0..MaxObjectSize; contents are valid UTF-8 |
| Positive length | Nonnull readable data with the required lifetime |
| Static = 0 | Compiler constant backing, never freed; null permitted only at length zero |
| Heap = 1 | Nonnull original live Runtime.Alloc pointer, capacity >= length, exactly one owning release responsibility |
| Empty Heap | Keep and free the original pointer even at length zero; allocation-free empties use Static |
| Other releaseKind | Invalid; destruction's default branch Aborts instead of freeing an unknown pointer |
| Moved source slot | Unusable as a value; no bit clearing or rewriting is required |

Establish validity at construction, not by revalidating UTF-8/allocations on every use; corruption detection is not guaranteed. Literal backing may be shared without granting Copy to string. Hello world needs no heap allocation.

The required Kimi.Console.writeLine Symbol (§22.4) receives a reference to a string handle, reads its data and length, calls WriteStdout(data, length), calls WriteStdout on a one-byte LF constant, and returns Unit. It neither releases nor modifies the handle; the caller keeps the string's ownership, and a temporary argument is destroyed by the caller at its ordinary lifetime. An empty string still emits LF. Output failure Aborts; earlier output is not rolled back. No concatenation buffer is required.

The Utf8Slice overload uses the view's data and length directly with the same byte-output path. It creates no string handle and transfers no release responsibility. Its declaration has a distinct Kimi overload Identity; an expected Function Type selects an overload, while an untyped function reference is ambiguous.

Write raw UTF-8 bytes to redirected files/pipes without changing the console code page. Non-ASCII console appearance depends on console configuration; universal Unicode console display is not initially guaranteed. A GetConsoleMode/WriteConsoleW adapter is a future extension, not implicit UTF-16 output.

### 22.5.6. Windows external symbols

WindowsRuntimeSymbols retains each external name, physical signature, calling convention, dllimport setting, and link input. Share declarations through the module symbol table (§21.5.2), including matching LibraryImport declarations. Emit only needed APIs, using Windows x64 ccc:

```llvm
declare dllimport ptr @GetProcessHeap()
declare dllimport ptr @HeapAlloc(ptr, i32, i64)
declare dllimport i32 @HeapFree(ptr, i32, ptr)
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport i32 @GetLastError()
declare dllimport void @ExitProcess(i32) noreturn
```

HANDLE and data pointers use ptr, SIZE_T i64, DWORD/UINT/BOOL i32, and LPDWORD ptr to a 32-bit slot. Windows BOOL is not i1. Empty parameter parentheses mean no parameters, not omitted Types. dllimport specifies reference generation, not automatic linking; use the kernel32 input (§20.8.2).

The Utf8Slice overload needs no additional external symbol. If optional shrinking is emitted, register `declare dllimport ptr @HeapReAlloc(ptr, i32, ptr, i64)` with kernel32 in the same symbol table; otherwise omit it.

## 22.6. Test execution and reporting

### 22.6.1. Test startup and active case

The test build uses dedicated generated startup and requires no product Application entry. Verify explicit main functions and every selected top-level runtime body with ordinary Type, ownership and control-flow rules, but do not execute them automatically. This applies equally to normal sources and TestSources and produces no warning merely because startup is not executed. Ordinary build still validates its Application/Library startup rules; passing tests does not establish a valid product entry. Top-level let/var remain SourceDocument-local runtime bindings, not static Properties, and are not initialized by test startup. Tests cannot capture those locals. Put shared preparation in ordinary helpers called explicitly by each test, or use ordinary lazy static Properties. Product/test membership is defined in §18.8; discovery is defined in §20.9.

```text
assign case -> prepare minimal reporting runtime -> activate case
  -> user initialization / test body / cleanup / shutdown
  -> report completion -> deactivate case -> child exit
  -> parent recovers processes, channels and temporary storage -> final result
```

All permitted verifications from user initialization through shutdown belong to the same case. Report initialization/body/cleanup/shutdown phases and restore the outer phase after nested initialization or cleanup. Static initialization remains demand-driven under §22.2.3; tests add no eager initialization. Runtime preparation failure is a startup/execution error. A verification without an active case is misuse regardless of its condition. Required initialization/destruction order and ordinary Abort behavior remain unchanged. Test host generation does not introduce Entry/Provider selection syntax; Composition Root extensions remain deferred (§13.8).

### 22.6.2. Process isolation and recovery

Pass a CaseId to the same immutable native executable for its project and execute each selected case once in a new child process. Do not share static state, automatically retry, or replace the executable/diagnostic table while it runs. An unsupported execution target is an execution error. Fix the working directory to the target project root; give each case a unique temporary directory reclaimed after every outcome. Capture the parent's environment once and apply project settings, then override only reserved case-specific settings, including TMP/TEMP. The [test profile](testing-profile.md) defines the temporary-directory API, environment, stdin and limits. Continuously drain separate per-case stdout/stderr streams without interleaving cases' stored logs.

External files, databases and ports may remain shared. Use case-specific resources or §20.9's serial mode where needed; temporary-directory recovery does not perform user defer or external rollback. Parallel cases are separate processes and add no language-level threads or memory model.

Use the parent's monotonic clock for a finite execution deadline and recovery grace. The execution deadline covers child launch through exit, including runtime preparation, initialization, body, cleanup, shutdown and completion reporting; exclude build and queue time. Normal exit, abnormal exit, timeout and cancellation enter the same bounded recovery process. Stop timed-out/cancelled cases and recover remaining managed descendants even after normal child exit.

Manage the process group from launch using an OS management unit or equivalent; later PID enumeration alone is insufficient. Bound process termination waits, channel draining/EOF waits and temporary cleanup by recovery grace. Report surviving processes/unrecovered resources after grace, preserving the original termination reason. Do not claim storage still used by a live process was recovered. Forced termination does not guarantee user cleanup. No guarantee extends to external processes outside OS management. An unrecoverable resource shortage fails the run. The [test profile](testing-profile.md) defines the adopted limits and management mechanism.

### 22.6.3. Diagnostic identity

| ID | Identity |
| --- | --- |
| ArtifactId | Executable and static diagnostic table for the compiler, target, settings and inputs |
| TestId | A test declaration within its project |
| CaseId | One case of a definition; initially one default case per TestId |
| SiteId | A verification site within the artifact, with source expression, location and display Types |
| IssueId | One recorded failure occurrence within a case, including repeat visits to one SiteId |

TestId/CaseId must be stable for the same source/settings and cannot depend only on display names, line numbers, absolute paths or execution order. Distinguish TestId by project, Container, Signature and, where needed, relative source path. Combine it with within-definition case identity to make CaseId unique in the execution scope. Renaming/moving declarations need not preserve IDs. Generate SiteId display strings/locations from current source under §18.7.4, including when semantic plans are reused; changed diagnostic tables affect ArtifactId. Preserve Mod source-observation invalidation.

Use unique integer IssueIds to connect basic failures, saved values and added messages; never attach details to an implicit last failure. Nested verifications during a message receive their own IDs. Preserve basic failure even if no details arrive. Omitted failures need no individual ID. Never reuse or wrap IDs; exhaustion switches to bounded detail omission (§22.6.5).

### 22.6.4. Reporting and result classification

Use a result channel separate from stdout/stderr. At startup match ArtifactId and CaseId; reject results for another artifact. Preserve per-case event order and validate counts, failure state, omission data and completion. Send each retained basic failure independently before subsequent user condition cleanup/message execution, not only at child exit. Parent-received data survives later child Abort. A reporting failure is an execution error, never success.

On a normal path, report completion after shutdown and exit the child with code zero; completion information carries verification failures. This is distinct from the overall CLI exit status (§20.9). **Success requires valid completion, normal child exit, consistent communication, completed recovery and no verification failure.** A normal case with no verifications succeeds. Neither exit zero nor a completion message alone establishes success.

| Outcome | Report |
| --- | --- |
| All success conditions met | Success |
| Normal completion with verification failures | Verification failure |
| Failed `$require` reaching its Abort, reliably identified | Verification failure with Abort termination |
| Other Abort or crash | Abnormal termination, retaining known verification failures |
| Execution deadline exceeded | Timeout |
| User cancellation | Cancelled, including unstarted selected cases; not success or executed skips |
| Startup, communication or recovery failure | Execution error, retaining known termination reason and verification failures |

Keep failure records, termination reason and management errors separately; recovery failure cannot overwrite an earlier Abort/timeout. Infer Abort reasons only from reliable received information, never exit code 1 alone. Missing diagnostics may yield unknown abnormal termination; never resume user code/cleanup after Abort. Cases excluded by a filter are not executed skips.

A `$require` failure does not produce normal completion or resume the test function; the parent recovers that case's process and continues managing other cases. Identify a `$require`-initiated Abort from reliable termination information associated with its verification site. A recorded false condition alone does not establish that reason: condition cleanup, message evaluation or message cleanup may Abort or diverge before the operation reaches its own Abort. Retain both the already recorded failure and the actual termination reason in those cases.

Provide human-readable and versioned machine-readable results using the [test profile](testing-profile.md). Include project/target/settings, IDs, source locations and expressions, retained values/messages, phases, durations, termination state, management errors, log paths and omission information. Terminal formatting is not the machine-readable interface.

### 22.6.5. Bounded diagnostics and storage

Set per-case and whole-run budgets for diagnostic counts and bytes, covering failures, saved values/messages, stdout/stderr and disk spill. Retain an initial storable portion and omission information. Bound message retention, frames, pending queues and read buffers. Once budgets are exhausted:

- Continue ordinary condition, failure-message and cleanup evaluation; omit storage only.
- Keep failure state latched. Reserve separate finite space for first-failure notification, completion and abnormal-control information.
- Saturate counts/omission counts and display lower bounds; never wrap. Abnormal termination reports observed lower bounds when final counts are unknown.
- Limit detail output in the child too; do not transfer omitted events into an unbounded parent/child queue.
- Continue draining communication, processing required information and discarding excess, within execution/recovery bounds.

Intentional detail omission is not an extra execution error. Missing/corrupt required control data or failed writes of retained data are execution errors. A storage budget does not bound allocations performed by the user's own message expression.

Share verification lowering and change only the failure continuation (§17.5). Use static expression/location/Type tables and compact ID/value events, formatting in the parent. Reuse parent worker slots/buffers and reset per-case state. Validate the immutable artifact snapshot once at run startup instead of copying/revalidating it per case, while retaining each child's ID handshake. Do not require reflective registration, per-verification UUIDs or heap objects. Measure empty/short, I/O-heavy and failure-heavy cases, including startup/recovery, time, parent/child memory, communication and generated code size; no unmeasured speedup is promised.
