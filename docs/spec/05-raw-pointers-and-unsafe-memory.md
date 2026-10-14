# 5. Raw pointers and unsafe memory

[Specification index](../SPEC.md)

An unsafe context delegates verification and exempts no rule, including the data-race rule ([§22.3.7](22-core-execution-and-foreign-functions.md#2237-data-races)). Unsafe Blocks ([§14.3](14-control-flow.md#143-do-and-unsafe)), unsafe functions ([§7.6](07-functions-and-callable-values.md#76-unsafe-functions)) and foreign functions ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)) mark promises the compiler cannot verify; breaking one is undefined behavior ([§1.3](01-overview.md#13-normative-status-and-wording)). Safe code alone never causes undefined behavior.

`raw/T` is a non-owning **raw pointer** to storage for the complete Type `T`, which may itself be a pointer or reference (`raw/raw/i32`). It is Copy for every `T`; copying or destroying it does not touch the pointee.

Only forming a raw Place (`*p`, `p[n]`, [§5.2](#52-raw-places)), pointer arithmetic ([§5.3](#53-pointer-arithmetic-and-indexing)), calling an unsafe function and `noinit` ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)) need an unsafe context; outside one, they are compile-time errors. Everything else, including `null`, `==`, `@raw`, `@raw/U` from a raw pointer, `usize` or `null`, and `@usize` from a raw pointer, needs none and needs no valid provenance. Converting an integer to a pointer is still an environment effect ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)).

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

**Provenance** is the storage a pointer derives from and the range its accesses may cover. `*p` is a **raw Place** of Type `T`, as are `p[n]` and the Fields and elements of a raw Place. Null and one-past-the-end pointers cannot be dereferenced.

Reads and writes follow the ordinary Copy and assignment rules. Only `Raw.initialize` ([§5.6](#56-raw-storage-operations)) initializes Uninitialized raw storage; a `noinit` array ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)) is already Initialized. A raw pointer supplies no Loan, exclusivity or completeness proof.

### 5.2.1. Access conditions

A read, write, Take or borrow of a raw Place is undefined behavior unless all of these hold:

1. **Range.** The accessed range lies within the pointer's provenance: for `P@raw`, the storage of `P` (`x[0]@raw` cannot reach `x[1]`); for `Raw.allocate`, the whole allocation; otherwise [§5.5](#55-integer-addresses) and the producing function's contract.
2. **Liveness and value.** The storage is live and aligned for `T`; a read needs a valid, initialized value. Replacing, moving or taking the contents ends only the contents; the storage stays live, but storage that the old contents owned (for example their heap buffer) ends with them.
3. **Authority.** The access does not exceed the capability that the path from which the address was taken has for that Place ([§13.5.5](13-operators-and-assignment.md#1355-follow) object-payload table, [§11.8](11-properties.md#118-static-storage)). Reading needs read capability; writing, Take and in-place destruction (`_ = (*p)@move`) need write capability. For example, a `let` binding, a `ref` referent, an immutable static, and a payload reached through `let h: obj/T`, `rc`, `arc` or `objref` are read-only; a writable owning path (including the payload of a writable owning `obj/T`) and a mutable static are readable and writable; a `uniq` or `objuniq` referent is readable and writable only while that Loan is live. `Raw.allocate` storage is readable and writable; a pointer from an integer conversion or a foreign function has the authority its contract states ([§5.5](#55-integer-addresses)).
4. **Loans.** The access conflicts with no live Loan of the storage, except the Loan of the path the pointer was taken from. Loan liveness follows the uses of safe values alone ([§15.6](15-ownership-and-lifetime-analysis.md#156-borrow-checking)).

**Local variables.** A local variable's storage lives at one fixed address from its declaration until its scope ends. Moving its value or part of it (`@move`, a Partial Move) or taking from it through a raw Place ends only the lifetime of those contents: the storage stays live and Uninitialized, and a later initialization (assignment to a `var`, or `Raw.initialize` through a raw pointer) fills the same storage. A raw pointer to the variable or to one of its parts therefore stays usable, under conditions 2-4, until the scope ends.

```kimi
var a = make()
let p = a@raw
consume(a@move)
a = make()
unsafe => inspect((*p)@ref)   // Valid: same storage, initialized again.
```

**Object payloads.** A payload's address is fixed from publication until its destruction begins, whatever happens to the handle.

**Retained dependencies.** A value moved into raw storage keeps its Origins and Loan requirements; the Type owning the storage must keep them through a Type argument or a `Loan<T>` Field ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)).

### 5.2.2. Borrowing raw Places

`@ref`, `@uniq`, `@objref` and `@objuniq` may borrow a raw Place. The result's referent is a fresh anchor ([§3.4.2](03-types-and-values.md#342-lending-points)) for a new Loan; no owner or existing Loan is recovered. Its Origin is fitted to the expected Type or destination and may be `static`; the safe-code rule of [§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned) that a `uniq` Origin cannot be bound to `static` does not apply. The anchor is not compared with Places not derived from it ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)). While the result or any value keeping its Origin is used, the conditions of [§5.2.1](#521-access-conditions) must hold for the borrow's accesses. For `uniq` and `objuniq`, exclusivity rests on this promise alone: no other path, including another raw pointer, accesses the storage.

### 5.2.3. Taking from raw Places

Raw Places offer Take ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)) without state tracking. `(*p)@move` takes the value and leaves the storage Uninitialized. The storage stays live; `Raw.initialize` may fill it again. Preventing a later read or a second destruction, also by the original owner, is the author's obligation. `_ = (*p)@move` destroys the value in place. Bare acquisition follows [§3.5](03-types-and-values.md#35-copy-and-move).

## 5.3. Pointer arithmetic and indexing

For `p: raw/T` and `n: isize`, `p + n` and `p - n` displace `p` by `n * stride(T)` bytes, and `p[n]` is `*(p + n)`. `p += n` and `p -= n` are compound assignments ([§13.7.2](13-operators-and-assignment.md#1372-compound-assignment)). No other pointer arithmetic exists.

`stride(T)` is the element spacing including padding ([§4.1](04-arrays-indexing-and-slices.md#41-fixed-arrays), [§21.1](../impl/21-layout-runtime-and-code-generation.md#211-storage-layout)). When it is zero, as for `raw/()`, every displacement is zero, so a generic `raw/T` needs no size Constraint.

A zero displacement preserves the pointer, including null. A nonzero displacement requires live storage, and both the source and the result must lie within the pointer's provenance range (condition 1 of [§5.2.1](#521-access-conditions)) or one past its end; the result keeps the provenance. Overflowing `isize` or wrapping the address is undefined behavior. Indexing has no bounds check.

```kimi
unsafe
    pointer[-1] = 123
    let next = 1 + pointer       // Error: only p + n and p - n exist.
```

## 5.4. Addresses and conversions

| Form | Meaning |
| --- | --- |
| `P@raw` | The address of the Place `P`, of Type `raw/V` for the stored Type `V` of `P` |
| `E@raw/U` | Conversion of a raw pointer, a `usize` or `null` to `raw/U` |
| `E@usize` | For a raw pointer `E`, its numeric address ([§5.5](#55-integer-addresses)) |

Bare `@raw` takes no Type and applies to the written slot: for `p: raw/T`, `p@raw` is `raw/raw/T`, and a referent is `r@follow@raw`. It checks like an immediately ending `@ref`, reads nothing and leaves no Loan; for a Temporary Value, the pointer is valid only while the temporary lives. In an effect summary it accesses `P` ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)).

When `U` and the operand's pointee Type are the same after normalization (aliases expanded), not merely of equal layout, `@raw/U` is an ordinary Copy and keeps the Origins. An integer literal operand is fitted to `usize`. A conversion between raw pointer Types preserves address and provenance. No conversion reads a pointee, creates provenance or grants access; access as `U` still needs [§5.2.1](#521-access-conditions).

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

A `usize` carries no provenance. Converting an address back yields an equal address but no provenance, lifetime or permission; access through it needs a provenance guarantee documented by the implementation, which gives its range and authority; conditions 2 and 4 of [§5.2.1](#521-access-conditions) still apply.

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

- **`allocate`** (safe) allocates uninitialized storage for `count` elements. A negative `count`, an overflow of `count * stride(T)` or a failure Aborts ([§17.4.3](17-failure-handling.md#1743-termination-and-cleanup)). The largest alignment `allocate` supports is implementation-defined; a larger alignment of `T` is Unsupported at compile time. A zero-byte request returns a non-null aligned address with zero-length provenance; zero-byte results may equal each other.
- **`release`** takes null or a zero-byte result (both ignored), or the start of an unreleased `allocate` result, possibly converted to another raw pointer Type. Its elements must already be destroyed or taken. Afterwards every pointer into the allocation is invalid.
- **`initialize`** moves `value` into live, aligned, writable storage holding no initialized value.
- **`slice`** forms a shared `Slice` of the `length` elements starting at `storage`; `length >= 0` is the caller's obligation. When `length == 0`, `storage` may be any value, including null, and is not accessed. When `length > 0`, `storage` is non-null and aligned for `T`, `length * stride(T)` fits in `isize`, and the `length` elements lie contiguously within `storage`'s provenance, each holding an initialized value of `T` (for example written by `Raw.initialize`) or belonging to a `noinit` array ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)). Formation reads no element; an unwritten `noinit` element must still be written before it is read. The result Origin `s` appears only in the result ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)) and has a fresh anchor ([§5.2.2](#522-borrowing-raw-places)); the elements must not be written for the whole Origin.

In an effect summary, `allocate` is an allocation and the others are raw accesses ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)).

```kimi
public struct Buffer<T>
    internal let data: raw/T        // internal: BufferView reads it.
    internal var length: isize
    let capacity: isize             // private: only Buffer uses it.

    public init(capacity: isize)
        self.data = Raw.allocate<T>(capacity)
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
            unsafe => _ = data[self.length]@move   // Destroy in reverse order.
        unsafe => Raw.release(data)

public struct BufferView<T> {source}
    let loan: Loan<ref/Buffer<T> during source>
    let data: raw/T
    let length: isize

    internal init(buffer: ref/Buffer<T> during source)   // internal: Buffer.view calls it.
        self.data = buffer.data
        self.length = buffer.length
        self.loan = Loan.init(buffer)

    public func get(self: ref/Self, index: isize) -> ref/T during self.source
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref
```

Private access covers only the declaring Container and bodies nested in it ([§9.4](09-names-signatures-and-access.md#94-access)), so members the two structs share are `internal`. A live `BufferView` keeps the shared Loan of `source`, so `push` meanwhile is a borrow error. The `raw/T` Field makes `Buffer<T>` invariant in `T` ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)). Because `T` appears in `Buffer<T>`, an inserted value keeps its Loans while the `Buffer` and every value taken or borrowed from it live ([§5.2.1](#521-access-conditions)).
