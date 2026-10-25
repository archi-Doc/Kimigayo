# 5. Raw pointers and unsafe memory

[Specification index](../SPEC.md)

An unsafe context delegates verification and exempts no rule, including the data-race rule ([§22.3.7](22-core-execution-and-foreign-functions.md#2237-data-races)). Unsafe Blocks ([§14.3](14-control-flow.md#143-do-and-unsafe)), unsafe functions ([§7.6](07-functions-and-callable-values.md#76-unsafe-functions)) and foreign functions ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)) mark promises the compiler cannot verify; breaking one is undefined behavior ([§1.3](01-overview.md#13-normative-status-and-wording)). Safe code alone never causes undefined behavior.

`raw/T` is a non-owning **raw pointer** to storage for the complete Type `T`, which may itself be a pointer or reference (`raw/raw/i32`). It is Copy for every `T`; copying or destroying it does not touch the pointee. A raw layer may state which storage it addresses with an Origin, `raw/T during o` ([§5.7](#57-raw-pointer-origins)).

Only forming a raw Place (`*p`, `p[n]`, [§5.2](#52-raw-places)), pointer arithmetic ([§5.3](#53-pointer-arithmetic-and-indexing)), calling an unsafe function, `noinit` ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)) and a claim of a raw Origin ([§5.7](#57-raw-pointer-origins)) need an unsafe context; outside one, they are compile-time errors. Everything else, including `null`, `==`, `@raw`, `@raw/U` from a raw pointer, `usize` or `null`, and `@usize` from a raw pointer, needs none and needs no valid provenance. Converting an integer to a pointer is still an environment effect ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)).

```kimi
let bytes = pointer@raw/u8       // No unsafe context.
unsafe
    let value = bytes[13]        // Forming a raw Place needs one.
let first = bytes[0]             // Error: raw Place outside an unsafe context.
```

## 5.1. Null and equality

A `null` literal needs an expected raw pointer Type, or must be the operand of `@raw/U` (Typed Null Formation, [§5.4](#54-addresses-and-conversions)). Safe references (`ref`, `uniq`, `objref`, `objuniq`) are never null. A non-null raw pointer need not be valid.

`==` and `!=` compare the addresses of two pointers of the same Type, or of a pointer and `null`; they read no pointee and work on null and dangling pointers. Null equals null and never equals a non-null pointer. Equal addresses imply neither equal provenance nor permission. Pointers have no ordering.

```kimi
let pointer: raw/i32 = null
let empty = pointer == null
let typed = null@raw/i32              // Typed Null Formation, not a cast.
let unknown = null                    // Error: no pointer Type.
let before = pointer < pointer        // Error: pointers have no ordering.
```

## 5.2. Raw Places

**Provenance** is the storage a pointer derives from and the range of it that the pointer's accesses may cover (condition 1 of [§5.2.1](#521-access-conditions)). A Place lies within a range when its bytes do or, if it is zero-sized, when its address is in the range or at its end. A Place lies within the pointer's provenance when it lies within the provenance's range and belongs to the provenance's storage; a punned Place belongs to the storage whose bytes it covers.

`*p` is a **raw Place** of Type `T`, as are `p[n]` and the Fields and elements of a raw Place; a null pointer cannot be dereferenced. A raw Place that an operation acts on designates one Place at the raw Place's address within the pointer's provenance, whose Type is the raw Place's Type under normalized Type identity with Origins treated as equal ([§3.8](03-types-and-values.md#38-type-relations-and-operations)); the base of a Field or element projection designates none. Distinct zero-sized Places of one Type may share an address ([§4.1](04-arrays-indexing-and-slices.md#41-fixed-arrays)); a raw Place may designate any of them, and no address, index or Field name selects one. A value written or placed through a raw Place into a Place outside raw storage must fit that Place's Type, Origins included, as a write through its access path would ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)), judged through the writer's contract: a Place the writing body reaches from its inputs has the Type its signature gives it. Raw storage has no declared Place; Retained dependencies ([§5.2.1](#521-access-conditions)) governs it.

Reads and writes follow the ordinary Copy and assignment rules. A whole-value read, replacement, Take or borrow of a raw Place needs a complete value ([§15.1.2](15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness)). A raw pointer supplies no Loan, exclusivity or completeness proof.

**Placement.** Places are initialized through a raw pointer only by construction or relocation (below) that an **operation contract** states, as the contract of `Raw.initialize` ([§5.6](#56-raw-storage-operations)) states construction. An operation contract is the `- safety:` conditions of a foreign import ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)) or the contract of a Kimi function that the implementation supplies without a body ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)); a function with a body initializes Places only through the operations it performs. A Place **holds no value** when it is Uninitialized or Moved ([§15.1.1](15-ownership-and-lifetime-analysis.md#1511-initialization-state)). Initializing a Place is a write of it that meets the conditions of [§5.2.1](#521-access-conditions) for the pointer through which it is initialized. That Place must be an existing Place or, in raw storage, a new one; it must hold no value, and no Initialized Place other than its ancestors may hold any of its bytes. A `noinit` array ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)) is already Initialized.

**Designation.** A set of operations is **closed** when it contains every operation that happens before one of its members ([§22.3.7](22-core-execution-and-foreign-functions.md#2237-data-races)). A closed set is **consistent** when some single choice of the Place each raw Place designates, the Place each placement fills and the values each relocation moves lets its members, safe or unsafe, meet their rules with no data race among them on the chosen Places. Behavior is undefined from an operation X when a closed set that contains X, but no operation that X happens before, is inconsistent while the set without X is consistent. The choice is not part of execution: an operation acts at its address as its Type directs, whatever the choice assigns to it; the choice only decides whether behavior is defined.

**Type-punned access.** A raw Place whose Type is **punnable** and positive-sized may, when a read, write or borrow acts on it or `Raw.slice` takes it as an element, instead designate a **punned Place**: a Place of that Type at the raw Place's address within the pointer's provenance, over bytes that Initialized Places hold, none of those bytes in a part that holds no value. The punnable Types are the `owner` Scalars ([§3.1](03-types-and-values.md#31-primitive-cores)), raw pointer Types, and Tuples and fixed arrays of punnable Types; the Fields and elements of a raw Place are raw Places of their own Types, so `(*p).f` may pun a punnable Field of a struct that is not punnable. A punned Place exists only for the access that acts on it or the Loan that borrows it. Placement, relocation and release disregard its bytes and value, but its Loans count under condition 4 of [§5.2.1](#521-access-conditions). It holds a complete value. A read of it, also through its borrow or as a `Raw.slice` element, needs its bytes to represent a valid value of its Type; padding ([§21.1.1](../impl/21-layout-runtime-and-code-generation.md#2111-common-rules)) and bytes that an enum's active Case does not use represent none. A punned raw pointer has the provenance of the raw pointer value whose bytes it reads when a write, placement or relocation of a raw pointer last wrote them, and otherwise none, as a pointer restored from an integer ([§5.5](#55-integer-addresses)); one whose Type states an Origin promises what a claim of that Origin promises ([§5.7](#57-raw-pointer-origins)). A write through a punned Place, also through its borrow, may change a byte only when the byte is padding or the innermost Initialized Place that holds it has a punnable Type, and that Place must hold a valid value of its Type by the time it is next accessed other than through a punned Place: punning constructs no payload and changes no Case.

```kimi
#Layout("C")
struct Header
    public var kind: u8
    public var length: u32

func readLength(bytes: raw/u8) -> u32            // bytes: Initialized and aligned for Header.
    let header = bytes@raw/Header
    unsafe => return (*header).length   // A u32 Place punned over the bytes, though Header is not punnable.
```

**Relocation.** When an operation contract states that its operation copies the bytes of a source range into raw storage, the operation may relocate, when it completes, values whose Places lie within the source range and the source pointer's provenance. A component moves only with its value: a relocated value moves with its components, and a component of a value that does not relocate, for example because the value's Place extends beyond the source range, stays. Each relocated value, with its components and their state, then occupies the Place at the same offset in the destination, and its source Place holds no value. Each destination Place meets the placement rule, counting the relocated source Places as holding no value, and conditions 3 and 4 of [§5.2.1](#521-access-conditions) apply to each relocated source Place as to a Take.

### 5.2.1. Access conditions

A read, write, Take or borrow of a raw Place is undefined behavior unless all of these hold:

1. **Provenance.** What the access acts on lies within the pointer's provenance ([§5.2](#52-raw-places)): for `P@raw`, the storage of `P` (`x[0]@raw` cannot reach `x[1]`, even at the same address); for `Raw.allocate`, the whole allocation; otherwise [§5.5](#55-integer-addresses) or the operation contract ([§5.2](#52-raw-places)) of the operation that produced the pointer.
2. **Liveness and value.** The storage is live and aligned for `T`, also when `T` is zero-sized; a read needs a valid, initialized value. Replacing, moving or taking the contents ends only the contents; the storage stays live, but storage that the old contents owned (for example their heap buffer) ends with them.
3. **Authority.** The access does not exceed the capability that the path from which the address was taken has for that Place ([§13.5.5](13-operators-and-assignment.md#1355-follow) object-payload table, [§11.8](11-properties.md#118-static-storage)) or, for a punned Place ([§5.2](#52-raw-places)), instead for each Place that holds one of its bytes. Reading needs read capability; writing, Take and in-place destruction (`_ = (*p)@move`) need write capability; a borrow needs the capabilities that the operation table of [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition) requires of its mode. For example, a `let` binding, a `ref` referent, an immutable static, and a payload reached through `let h: obj/T`, `rc`, `arc` or `objref` are read-only; a writable owning path (including the payload of a writable owning `obj/T`) and a mutable static are readable and writable; a `uniq` or `objuniq` referent is readable and writable only while that Loan is live. `Raw.allocate` storage is readable and writable; a pointer from an integer conversion has the authority that the guarantee of [§5.5](#55-integer-addresses) gives, and a pointer that an operation with an operation contract produces, such as a foreign import, the authority that contract states. Three limits apply to every pointer:
   - A raw pointer read from a Place past a shared layer ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)), or past a raw pointer whose Origin keeps only shared Loans, and every pointer derived from it (copies, displacements, conversions between raw pointer Types, fits that drop the Origin), has no write authority.
   - A pointer whose Type states an Origin has no more authority than that Origin keeps, judged where the Origin is bound ([§5.7](#57-raw-pointer-origins)). A pointer read from a raw layer that a declaration states, and every pointer derived from it, has no write authority when the layer's Origin keeps only shared Loans in that declaration: for a Field's or Case payload's layer, when it states a slot whose Loan requirement is `ref`; for a layer written in the signature of a function, closure or accessor (a parameter's or the result's, not a Function Type's), when that signature's inputs state the Origin, every borrow whose Origin it is there is a shared borrow (a `ref` or `objref` borrow or a Slice, in each admitted Semantics case of a pair layer), and every slot they bind it to has the Loan requirement `ref`. Borrows formed in a body do not count for an Origin the signature binds; a body-local borrow's own Origin is judged by that borrow ([§5.7](#57-raw-pointer-origins)).
   - A pointer formed through an own step, into an object payload or into storage a raw Field declares `during self` ([§5.7](#57-raw-pointer-origins)), has authority only while the owning value stays in the holder it was reached from; a Move of that value ends it. A pointer read again from a `during self` Field reaches the value's current holder. A Warning is required where an operation in a body may end the owning value's stay in its holder while a value carrying a pointer formed through that own step may still be used: a Move or Take of the owning value's Place or of an ancestor holding it with no dereference between them, a write that replaces either, or its destruction, or an exclusive Borrow of the Place or of any ancestor, as a call that takes it by `uniq` forms (`exchange`, `swap` and `replace` among them). Places are compared by their access paths ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)).
4. **Loans.** The access conflicts with no live Loan of a Place overlapping what it acts on, or of the dynamic collection whose element storage it acts on ([§4.9](04-arrays-indexing-and-slices.md#49-dynamic-collections)), except the Loan of the path the pointer was taken from. Loan liveness follows the uses of values, raw pointers included through the holders they reach ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)).
5. **Reach.** An access that reads or writes a value carrying a Loan acts in a holder the pointer reaches or in storage the body does not own ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)): a pointer the body cannot trace to a holder, such as one a call stored in static storage or one restored from an integer, must not be used to read or write such values in the body's own holders, by the body or by a call to which the body passes the pointer, directly or within a value or storage.

```kimi
func h() -> ()
    var x: i32 = 0
    let r = x@ref
    let p = r@follow@raw          // raw/i32 during the Origin of r's shared borrow.
    unsafe => *p = 1              // Undefined behavior: that Origin keeps only shared Loans.
```

**Local variables.** A local variable's storage lives at one fixed address from its declaration until its scope ends. Moving its value or part of it (`@move`, a Partial Move) or taking from it through a raw Place ends only the lifetime of those contents: the storage stays live, the Places of those contents hold no value, and a later initialization (assignment to a `var`, or `Raw.initialize` through a raw pointer) fills the same Places. A raw pointer to the variable or to one of its parts therefore stays usable, under conditions 2-5, until the scope ends.

```kimi
var a = make()
let p = a@raw
consume(a@move)
a = make()
unsafe => inspect((*p)@ref)   // Valid: the same Place, initialized again.
```

**Object payloads.** A payload's address is fixed from publication until its destruction begins, whatever happens to the handle.

**Retained dependencies.** A value moved into raw storage keeps its Origins and Loan requirements, and its Loans follow the storage ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)): storage a raw Field declares `during self` is part of the value holding the Field, whose Type counts the pointee as stored content ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)); storage a pointer addresses `during o` is covered by the Loans of `o`; any other storage is what the pointer reaches. A function stores through raw storage reached from its inputs only values that its public contract lets the call retain there ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). A raw layer without an Origin that the call writes lets it retain there every argument with what reads through it carry, but not the own value of the argument it writes through (narrower for the published transfer operations, [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)), so `Raw.initialize`, `Array.append` and `insert` and the Array and Dictionary literals keep the Loans and addresses of a stored value of any Type, `(raw/u8, i32)` included. This applies only to a layer that states no Origin: a layer stating `during self` or `during o` retains as that Origin's borrow would ([§5.7](#57-raw-pointer-origins)), and the unstated layers in the storage it addresses follow this rule.

### 5.2.2. Borrowing raw Places

`@ref`, `@uniq`, `@objref` and `@objuniq` may borrow a raw Place. The result's referent is a fresh anchor ([§3.4.2](03-types-and-values.md#342-lending-points)) for a new Loan; the anchor recovers no owner or Loan. Its Origin is fitted to the expected Type or destination and may be `static`, but through a pointer that states an Origin it lies within that Origin ([§5.7](#57-raw-pointer-origins)); the result also carries what the pointer reaches ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)). The safe-code rule of [§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned) that a `uniq` Origin cannot be bound to `static` does not apply. The anchor is not compared with Places not derived from it ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)). While the result or any value keeping its Origin is used, the conditions of [§5.2.1](#521-access-conditions) must hold for the borrow's accesses. For `uniq` and `objuniq`, exclusivity rests on this promise alone: no other path, including another raw pointer, accesses a Place overlapping the borrowed one.

### 5.2.3. Taking from raw Places

Raw Places offer Take ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)) without state tracking. `(*p)@move` takes the value and leaves the designated Place holding no value; the storage stays live, and `Raw.initialize` may place a value there again. Preventing a later read or a second destruction, also by the original owner, is the author's obligation. `_ = (*p)@move` destroys the value in place, at statement temporary cleanup ([§14.2.4](14-control-flow.md#1424-explicit-discard)): its destruction, its `drop` included, acts on that Place, which then holds no value. Bare acquisition follows [§3.5](03-types-and-values.md#35-copy-and-move).

## 5.3. Pointer arithmetic and indexing

For `p: raw/T` and `n: isize`, `p + n` and `p - n` displace `p` by `n * stride(T)` bytes, and `p[n]` is `*(p + n)`. `p += n` and `p -= n` are compound assignments ([§13.7.2](13-operators-and-assignment.md#1372-compound-assignment)). No other pointer arithmetic exists.

`stride(T)` is the element spacing including padding ([§4.1](04-arrays-indexing-and-slices.md#41-fixed-arrays), [§21.1](../impl/21-layout-runtime-and-code-generation.md#211-storage-layout)). When it is zero, as for `raw/()`, every displacement is zero, so a generic `raw/T` needs no size Constraint; pointer equality then cannot find the end of a sequence of such elements, which is traversed by count.

A zero displacement preserves the pointer, including null. A nonzero displacement requires live storage, and both the source and the result must lie within the range of the pointer's provenance ([§5.2](#52-raw-places)) or one past its end; the result keeps the provenance and the pointer's Type, its Origin included. Overflowing `isize` or wrapping the address is undefined behavior. Indexing has no bounds check.

```kimi
unsafe
    pointer[-1] = 123
    let next = 1 + pointer       // Error: only p + n and p - n exist.
```

## 5.4. Addresses and conversions

| Form | Meaning |
| --- | --- |
| `P@raw` | The address of the Place `P`, of Type `raw/V` for the stored Type `V` of `P`, stating the Origin of the last borrow or raw pointer that `P` is reached through, if any: a borrow's outer Origin, or the Origin a raw pointer states ([§5.7](#57-raw-pointer-origins)) |
| `E@raw/U` | Conversion of a raw pointer, a `usize` or `null` to `raw/U` |
| `E@(raw/U during o)` | The same conversion stating the Origin `o`: a fit when the raw Origin of `E`, or `null`, fits `o` ([§3.8](03-types-and-values.md#38-type-relations-and-operations)), otherwise a claim ([§5.7](#57-raw-pointer-origins)), which needs an unsafe context |
| `E@usize` | For a raw pointer `E`, its numeric address ([§5.5](#55-integer-addresses)) |

Bare `@raw` takes no Type and applies to the written slot: for `p: raw/T`, `p@raw` is `raw/raw/T`, and a referent is `r@follow@raw`. It checks like an immediately ending `@ref`, reads nothing and leaves no Loan; for a Temporary Value, the pointer is valid only while the temporary lives. In an effect summary it accesses `P` ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)).

When `U` and the operand's pointee Type are the same after normalization (aliases expanded), not merely of equal layout, `@raw/U` is an ordinary Copy and keeps the Origins. An integer literal operand is fitted to `usize`. A conversion between raw pointer Types preserves address, provenance and the operand's raw Origin, unless the target states another. No conversion reads a pointee, creates provenance or grants access; access as `U` still needs [§5.2](#52-raw-places) and [§5.2.1](#521-access-conditions).

```kimi
var number: i32 = 1
let p = number@raw            // raw/i32; no Loan is created.
let r = number@uniq
let q = r@follow@raw
unsafe
    *q = 2                    // Valid: inside r's Loan.
    *p = 3                    // Undefined behavior: conflicts with r's Loan.
r@follow += 1                 // r's Loan is live above.
```

## 5.5. Integer addresses

Pointer/integer conversion requires a target with one address space, whose pointers carry no state beyond the address, whose addresses fit `usize` and whose pointer, `usize` and `isize` widths agree; elsewhere it is Unsupported. Null and zero convert to each other.

A `usize` carries no provenance. Converting an address back yields an equal address but no provenance, lifetime, permission or Origin, and the pointer reaches only storage the body does not own ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)); access through it needs a guarantee documented by the implementation that gives the pointer's provenance ([§5.2](#52-raw-places)), its authority and the values placed within that provenance; conditions 2, 4 and 5 of [§5.2.1](#521-access-conditions) still apply. A punned read of a raw pointer from bytes that no raw pointer wrote gives such a pointer ([§5.2](#52-raw-places)).

```kimi
let restored = pointer@usize@raw/i32   // Equal address only; no provenance.
```

## 5.6. Raw storage operations

```kimi
public group Raw   // Kimi.Raw
    public func allocate<T>(count: isize) -> raw/T
    public unsafe func release<T>(storage: raw/T)
    public unsafe func initialize<T>(storage: raw/T, value: T)
    public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

- **`allocate`** (safe) allocates raw storage for `count` elements; it holds no value. A negative `count`, an overflow of `count * stride(T)` or a failure Aborts ([§17.4.3](17-failure-handling.md#1743-termination-and-cleanup)). The largest alignment `allocate` supports is implementation-defined; a larger alignment of `T` is Unsupported at compile time. A zero-byte request returns a non-null aligned address with zero-length provenance of its own; zero-byte results may equal each other.
- **`release`** takes null, a zero-byte result or the start of an unreleased `allocate` result, possibly converted to another raw pointer Type; it ignores null and frees nothing for a zero-byte result, which may be released again. Every Non-Copy value placed in the storage must already be destroyed, taken or relocated, whole or, if destroying it whole would only destroy its components ([§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)), by its remaining Non-Copy components under the same rule. Afterwards every pointer into the storage is invalid.
- **`initialize`** moves `value` into a Place of `T` at `storage` under the placement rule of [§5.2](#52-raw-places).
- **`slice`** forms a shared `Slice` of the `length` elements starting at `storage`; `length >= 0` is the caller's obligation. When `length == 0`, `storage` may be any value, including null, and is not accessed. When `length > 0`, `storage` is non-null and aligned for `T`, `length * stride(T)` fits in `isize`, and for each `i` below `length`, `storage[i]` designates a distinct Place within `storage`'s provenance holding a complete value of `T` (for example placed by `Raw.initialize`) or belonging to a `noinit` array ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)); element `i` of the result is that Place. Formation reads no element; an unwritten `noinit` element must still be written before it is read. The result Origin `s` appears only in the result ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)) and has a fresh anchor ([§5.2.2](#522-borrowing-raw-places)), within the Origin that the argument for `storage` states before its fit to `raw/T`, if any ([§5.7](#57-raw-pointer-origins)); the elements must not be written for the whole Origin.

In an effect summary, `allocate` is an allocation and the others are raw accesses ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)).

```kimi
public struct Buffer<T>
    internal let data: raw/T during self   // Buffer owns the storage; internal: BufferView reads it.
    internal var length: isize
    let capacity: isize                    // private: only Buffer uses it.

    public init(capacity: isize)
        unsafe => self.data = Raw.allocate<T>(capacity)@(raw/T during self)   // A claim (§5.7).
        self.length = 0
        self.capacity = capacity

    public func push(self: uniq/Self, value: T)
        require self.length < self.capacity else => $abort("Buffer is full")
        unsafe => Raw.initialize(self.data + self.length, value@move)
        self.length += 1

    public func get(self: ref/Self, index: isize) -> ref/T
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref

    public func items(self: ref/Self) -> Slice<T>
        unsafe => return Raw.slice(self.data, self.length)

    public func view(self: ref/Self) -> BufferView<T>
        return BufferView<T>.init(self)

    drop
        let data = self.data
        while self.length > 0
            self.length -= 1
            unsafe => _ = data[self.length]@move   // Destroy in place, in reverse order.
        unsafe => Raw.release(data)

public struct BufferView<T> {source}
    let loan: Loan<ref/Buffer<T> during source>
    let data: raw/T during source
    let length: isize

    internal init(buffer: ref/Buffer<T> during source)   // internal: Buffer.view calls it.
        self.data = buffer.data                          // raw/T during source: no claim.
        self.length = buffer.length
        self.loan = Loan.init(buffer)

    public func get(self: ref/Self, index: isize) -> ref/T during self.source
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref
```

Private access covers only the declaring Container and bodies nested in it ([§9.4](09-names-signatures-and-access.md#94-access)), so members the two structs share are `internal`. A live `BufferView` keeps the shared Loan of `source`, so `push` meanwhile is a borrow error. The `raw/T` Field makes `Buffer<T>` invariant in `T` ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)). Because `data` is `during self`, the storage is part of the Buffer and its holder carries the Loans of the inserted values, so an inserted value keeps its Loans while the `Buffer` and every value taken or borrowed from it live ([§5.2.1](#521-access-conditions)). `BufferView.init` reads `buffer.data` through `source`, so the value already states `source` and needs no claim; since `data` states `source`, whose Loan requirement is `ref`, a pointer read from it has read authority only ([§5.2.1](#521-access-conditions)).

## 5.7. Raw pointer Origins

A raw layer may state an Origin with `during` ([§3.3.2](03-types-and-values.md#332-nested-semantics-and-origin-annotations)). The Origin states which storage the pointer addresses. It forms no Loan, grants no access and establishes no validity, and the pointer's uses add no point to its region ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)): like any raw pointer, one with an Origin may dangle ([§5.1](#51-null-and-equality)), and its Origin keeps no Loan live.

| Form | The pointer addresses |
| --- | --- |
| `raw/T` | Storage it does not state: what the value reaches ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)), and at a call what [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls) gives |
| `raw/T during o` | Storage that `o` covers: what the Loans of the borrow `o` belongs to protect, or static storage for `static` (the bytes a string literal spells outside its interpolations included, [§3.1.2](03-types-and-values.md#312-characters-and-strings)) |
| `raw/T during self` | Storage the value holding the layer owns: an own step of that value ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)), as an object payload is |

- **Origins.** A written `o` is one Origin atom other than an intersection ([§15.3.1](15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets)), and it belongs to a borrow: a slot, also projected from a value (`x.source`), to the stored safe references and `Loan<T>` Fields that keep it, which together admit what their Referent Types admit; the outer Origin of a borrow value (an input's or, in a body, a local's) to that borrow; and `static` to static storage. In a signature a raw layer's Origin must belong to an input's borrow, a slot or `static`. `self` names the holding value only on a raw layer in the inline storage of a struct or enum (Fields, Tuple and fixed-array elements, Case payloads), never inside a Type argument. In the Type's own bodies `self` is the receiver's Origin ([§15.2.1](15-ownership-and-lifetime-analysis.md#1521-origin-expressions)); in a constructor it is the value being constructed and in `drop` the destruction receiver ([§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)), both exclusive. An Origin that a raw layer obtains by reading a Field, by a typed path or by substituting a slot or input may be of any form, an intersection included, and belongs to what the Origin it comes from belongs to: for inputs `x: ref/T` and `y: ref/T` and `choose` of [§15.3.6](15-ownership-and-lifetime-analysis.md#1536-origin-inference), `choose(x, y)@follow@raw` is a `raw/T during (x and y)`, which a local's inferred Type may state but no written Type can.
- **Reading a Field.** Reading a raw Field whose layer states `self` through a path that dereferences a borrow or raw pointer gives a pointer stating the Origin of the last borrow or raw pointer it dereferences: a borrow's outer Origin, the Origin a raw pointer states, or none when that pointer states none. Through no such dereference it gives a pointer without an Origin that reaches what `@raw` of the Field's storage reaches ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)). A Tuple or fixed-array Place that holds such a layer within the same holding value is read element by element: the layer's element is read by this rule and the others as they are. A slot of the Field's Type is replaced by the value's binding, as for any Field.
- **Writing a Field.** Through a path that dereferences a borrow or raw pointer, a raw layer that states `self` has the Origin that reading the Field through that path gives: a source written there must fit it, and a borrow of its Place states it. Through no such dereference, except through `self` in the constructors and `drop` of the struct or enum whose inline storage holds the layer, a `during self` layer's Place admits reads (a Move included), shared borrows and writes of `null` only: any other write (a compound assignment, or a source with any Origin, `static` included, or with none) and an exclusive borrow of the Place (also as an argument of `exchange`, `swap` or `replace`) are Origin relation errors ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)), since no Origin covers the storage the holding value owns there: it is an own step, not a region, so no fit by `outlives` applies. Such a write goes through an exclusive borrow of the holding value or of a Place containing it (`let r = b@uniq`), whose Origin a claim can name. A Tuple or fixed-array Place that holds a `during self` layer within the same holding value is read, Moved, written and borrowed as the layer's Place is, element by element: a Move reads the layer's element and Moves the others, and a restore writes `null` in the layer's position; for a local `b` with the Field `pair: (raw/u8 during self, i32)` and a raw pointer `q` other than `null`, both `b.pair = (q, 1)` and `exchange(b.pair@uniq, with: (q, 1))` are Origin relation errors. A Case whose payload has a `during self` layer is constructed with `null` in that layer; any other payload there is an Origin relation error too.
- **Typed paths.** `P@raw` states the Origin of the last borrow or raw pointer that `P` is reached through in the same way ([§5.4](#54-addresses-and-conversions)). A displacement ([§5.3](#53-pointer-arithmetic-and-indexing)) and a conversion between raw pointer Types keep the Origin. None of these needs an unsafe context.
- **Fits and claims.** A raw layer without `during` counts as stating an Origin that every Origin outlives, and a raw layer is covariant in its Origin ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)): `raw/T during o` is a subtype of `raw/T` ([§3.8](03-types-and-values.md#38-type-relations-and-operations)), so dropping an Origin, or narrowing it to one it outlives, is a fit. A change that is not a fit is a **claim**, formed only by `E@(raw/U during o)` in an unsafe context ([§5.4](#54-addresses-and-conversions)); elsewhere it is an Origin relation error ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)). `null` takes any Origin. A claim promises that every access through the pointer, and through every pointer derived from it, lies in storage that `o` covers; breaking it is undefined behavior.
- **Authority.** A raw Origin grants no authority, and no access through a raw pointer is an error for want of authority: whether an access to a raw Place is defined, a read or a `@ref` or `@objref` Borrow (read capability) and a write, Take, in-place destruction or `@uniq` or `@objuniq` Borrow (write capability) alike, is decided only by [§5.2.1](#521-access-conditions), while the unsafe context it needs and the Origin relations above (fits, claims, Writing a Field) are checked as everywhere. An Origin **keeps only shared Loans** when everything it belongs to (Origins above) does: each borrow is a shared borrow (a `ref` or `objref` borrow or a Slice) and each slot has the Loan requirement `ref` ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)), in each admitted Semantics case. Each Origin is judged where it is bound: an input's or a slot's by the signature or Type declaration that binds it, never by the caller's actual borrow, and the outer Origin of a borrow formed in a body by that borrow itself. Through a pointer whose Origin keeps only shared Loans, an access that needs write capability exceeds condition 3 and is undefined behavior.
- **Slots and ownership.** A raw layer's Origin is a covariant position for variance but no use of a slot for Loan requirements or Phantom status; a slot that a raw layer states must not be Phantom, or the declaration is invalid. A `during self` pointee counts as stored content of its Type for slot properties and OwnedOrigins, values stored there keep their Loans in the holder of the owning value ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)), and a struct or enum whose inline storage holds a `during self` layer is not Copy. The layer's Type and a Tuple or fixed array holding it are Copy ([§3.5.2](03-types-and-values.md#352-copy-capability)); their Places follow "Writing a Field" and an explicit `@move` ([§13.5.3](13-operators-and-assignment.md#1353-defined-adaptations)).
- **Calls.** Through a raw layer with an Origin a call may store what the Referent Type of each borrow that Origin belongs to admits (for `self`, the holding value's Type; for `static`, the pointee Type), and that storage retains as the borrow would, without forming a Loan. Through a raw layer without an Origin in a parameter, a call may store what its pointee Type admits. A raw layer without an Origin that a call may write, in its result or in storage it writes through an argument, also as a Tuple element, an Option payload, a Type argument or a Field, may address storage the callee reaches through any argument (through the one it writes through, only by reads through it; narrower for the published transfer operations) and storage the body does not own; behind a borrow in the result (a Slice's or UniqSlice's elements included), only storage the callee reaches through a raw argument ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)).

```kimi
struct Lens<T> {source}
    internal let loan: Loan<ref/T during source>
    internal let at: raw/T during source        // Storage that source covers.

    internal init(target: ref/T during source)
        self.at = target@follow@raw             // raw/T during source: a typed path, no claim.
        self.loan = Loan.init(target)

func read(lens: ref/Lens<i32>) -> i32
    unsafe => return *lens.at                   // Valid: reads storage source covers.

func clear(lens: uniq/Lens<i32>)
    unsafe => *lens.at = 0                      // Undefined behavior: source keeps only a shared Loan.

func adopt(pointer: raw/i32) -> raw/i32 during static
    // return pointer                           // Error: stating an Origin needs a claim.
    unsafe => return pointer@(raw/i32 during static)   // A claim: pointer addresses static storage.
```

```kimi
struct Packet
    internal var pair: (raw/u8 during self, i32)
    public init() => self.pair = (null, 0)

func demo() -> i32
    var b = Packet.init()
    let t = b.pair                // A Copy: t.0 addresses b's storage, without an Origin.
    let moved = b.pair@move       // Reads the layer's element and Moves the i32.
    b.pair = (null, 2)            // The restore writes null in the layer's position.
    return t.1 + moved.1
```

A layer without an Origin that a call writes retains what the call may reach; stating an Origin narrows it:

```kimi
func fill(r: ref/i32, k: ref/i32, slot: uniq/(raw/i32))
    if k@follow > 0 => slot@follow = r@follow@raw   // slot may retain r and k (§15.6.4).

func fillFrom(r: ref/i32, k: ref/i32, slot: uniq/(raw/i32 during r))
    if k@follow > 0 => slot@follow = r@follow@raw   // slot retains r alone.

func caller()
    var x: i32 = 1
    var y: i32 = 2
    var s: raw/i32 = null
    fill(x@ref, y@ref, s@uniq)
    // y = 3                                        // Error: s may retain y's Loan and is used below.
    usep(s)
    let r = x@ref
    var t: raw/i32 during r = null
    fillFrom(r, y@ref, t@uniq)
    y = 4                                           // Valid: t retains r's Loan, not y's.
    usep(t)
```

A pointer stored within an aggregate keeps its address, as the pointer stored alone does:

```kimi
var y: i32 = 1
let w = y@ref
let q = w@raw@raw/u8                                // Addresses w, which carries y's Loan.
let pairs: Array<(raw/u8, isize)> = [(q, 1)]        // pairs keeps q's address.
// y = 5                                            // Error: pairs reaches w and is used below.
inspect(pairs)
```

Neither an item lent behind a borrow nor an item an owning Iterator moves out keeps its receiver's Loan:

```kimi
let shared: Array<raw/u8> = [null, null]
var items = shared.iterate()
let first = items.next()
let second = items.next()                           // Valid: behind the item's borrow only raw arguments are retained.
let owned: Array<raw/u8> = [null, null]
var taken = owned@move.intoIterator()
let third = taken.next()
let fourth = taken.next()                           // Valid: an Iterator's item keeps no Loan of its receiver (§22.2.3).
inspect(first)
inspect(second)
inspect(third)
inspect(fourth)
```
