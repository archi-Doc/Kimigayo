# 5. Raw pointers and unsafe memory

[Specification index](../SPEC.md)

An unsafe context delegates verification; it never exempts a program from a rule. Every ownership, Loan, Origin, initialization, destruction, effect-bound, aliasing and data-race rule applies to unsafe code. Unsafe Blocks (§14.3.3), unsafe function contracts (§7.5) and foreign-function declarations (§22.3.1) mark where the programmer promises what the compiler cannot verify; breaking such a promise is undefined behavior. As long as these promises hold, safe code alone cannot cause undefined behavior.

`raw/T` is a non-owning **raw pointer** to storage for its complete immediate Referent Type `T`. `T` determines access and the element size used by arithmetic. `T` may itself be a reference or pointer Type, as in `raw/ref/i32` and `raw/raw/i32`. Pointers are Copy regardless of `T`. Copying or destroying a pointer neither copies nor destroys its pointee, and frees no storage. The Type name states a capability; `unsafe` marks only the places where a promise is made.

```kimi
let first: raw/Foo = obtainPointer()
let second = first // Copy the pointer, not Foo.
```

A raw pointer guarantees nothing about its pointee's lifetime, initialization, alignment or access permission. Every access through it must satisfy the conditions of §5.2.1.

A missing required unsafe context, an invalid Type and an unsupported operation are compile-time errors. Violating a runtime memory-safety requirement is undefined behavior; neither detection nor runtime checks are guaranteed.

Only an operation whose violation may be undefined behavior requires an unsafe context:

| Operation | Unsafe context required |
| --- | --- |
| Declare, hold, Copy, Move, pass or destroy a raw pointer; create a contextually typed `null`; compare same-Type pointers, or a pointer and `null`, for equality | No |
| Acquisition and conversion with `@raw/U`: the same raw pointer Type, distinct raw pointer Types, `usize` and Typed Null Formation (§5.4) | No |
| Taking the address of a Place with `@raw` (§5.4) | No |
| Forming a raw Place with `*p` or `p[n]`, including borrowing and taking from it (§5.2) | Yes |
| `p + n`, `p - n`, `p += n`, `p -= n` | Yes |
| Calling an unsafe function | Yes |

The operations that need no unsafe context do not require the pointer to have access provenance (§5.2). Whether an operation needs an unsafe context is independent of its effect classification: converting an integer to a pointer needs no unsafe context but is an environment effect (§8.4.10.2).

```kimi
let address = pointer@usize      // No unsafe context.
let bytes = pointer@raw/u8       // No unsafe context.
unsafe
    let value = bytes[13]        // Forming the raw Place needs one.
```

## 5.1. Null and equality

`raw/T` permits `null`. The expected Type of a `null` literal must determine `raw/T`; otherwise compilation fails. Safe references (`ref/T`, `uniq/T`, `objref/T`, `objuniq/T`) are never null. A non-null raw pointer is not necessarily valid.

```kimi
let pointer: raw/i32 = null
let typedNull = null@raw/i32 // Typed Null Formation, not a pointer cast.
let unknown = null // Error: no pointer Type can be determined.
let empty = pointer == null
```

`==` and `!=` compare the addresses of two pointers of the same Type and return `bool`; they do not read the pointees. In a comparison with `null`, the literal takes the other operand's pointer Type. Null equals null and never equals a non-null pointer.

Initialized pointers may be compared even when they are null or dangling. Equal addresses imply neither equal provenance nor ownership or access permission. Comparing pointers of different Types requires an explicit conversion to a common Type. Ordering comparisons are not defined for pointers.

## 5.2. Dereference and ownership

**Provenance** records the storage a pointer derives from and the basis for its accesses. `*pointer` denotes a memory Place of Type `T`, a **raw Place**; `p[n]` and the Fields and elements of a raw Place are raw Places too. Forming one requires live storage covering the required range, valid alignment and provenance; null and one-past-the-end pointers cannot be dereferenced.

Reads and writes of the pointee follow the normal Copy, assignment and replacement rules: a read Copies a Copy value, and assignment replaces an initialized pointee under the normal destruction rules. Initializing uninitialized raw storage requires `Raw.initialize` (§5.6); ordinary assignment cannot do it. Binding mutability does not determine pointee write permission.

```kimi
let pointer: raw/i32 = obtainPointer()
unsafe
    let value = *pointer
    *pointer = 10
pointer = other // Error: the let binding cannot be reassigned.
```

Unsafe access obeys the distinction between storage lifetime and content lifetime (§15.7.3): a payload update keeps the allocation but may invalidate pointers and dependencies into the old contents. A raw pointer alone supplies no safe Loan, exclusivity or completeness proof.

### 5.2.1. Access conditions

An access through a raw pointer (a read, write, take or borrow of a raw Place) is valid only when all of the following hold. The compiler does not check them; a violation is undefined behavior.

1. **Range.** The accessed range lies within the pointer's provenance:
   - for `P@raw`, the storage of the written Place `P`, with one-past-the-end allowed for arithmetic; `x[0]@raw` cannot reach `x[1]`;
   - for `Raw.allocate`, the whole allocation;
   - for an integer conversion or a foreign result, §5.5 and the function's contract.
2. **Liveness and value.** The storage is live. A read requires an initialized, valid value, and a write follows the initialization and replacement rules above. Replacing the contents keeps the storage but invalidates pointers into the old contents (§15.7.3).
3. **Authority.** The access does not exceed the authority of the path from which the address was taken:
   - read only: a `let` binding, a Place reached through `ref`, or any shared path;
   - read and write: a writable owning path, or the result of `Raw.allocate`;
   - read and write only while that Loan is live: a Place reached through `uniq`.
4. **Loans.** The access does not conflict with a live Loan of that storage: a shared Loan forbids writes, and an exclusive Loan forbids every access. An access made inside the Loan of the path from which the pointer was taken does not conflict with that Loan.

The liveness of a Loan is decided by the uses of safe values alone (§15.6). Neither `@raw` nor any use of a raw pointer extends a Loan. The liveness of storage is distinct from the liveness of a Loan of it.

- **Local variables.** The storage of a local variable lives until its scope ends or its value is Moved, whichever comes first.
- **Object payloads.** A payload's address is fixed from publication until destruction begins. Moving, borrowing, cloning, downgrading or upcasting the handle does not move it. A fixed address does not satisfy conditions 3 and 4: a back link to a parent, for example, may be used only when it does not conflict with exclusive access to that parent.

**Retained dependencies.** A value moved into raw storage keeps its Origins and Loan requirements. While the value lives, the author must keep those dependencies in the Type of the value that owns the storage, through a Type argument or a `Loan<T>` Field (§15.3.5).

### 5.2.2. Borrowing raw Places

`@ref`, `@uniq`, `@objref` and `@objuniq` may borrow a raw Place, including a Field or element of one.

- **No recovery.** Neither the original owner nor an existing Loan is recovered from a raw pointer.
- **Fresh anchor.** The referent of the result is a fresh anchor for a new Loan whose mode is the mode of the borrow. Its region follows uses and result fitting as usual. Its Origin is fitted to the expected Type, result or storage destination and has no upper bound; it may be `static`. Adaptation Targets still carry no written Origin (§13.5.1), and the unique-anchor restriction on safe derivations (§15.2.3) does not apply.
- **Overlap.** The anchor is not compared with Places not derived from it, such as other raw Places or the original storage (§15.6.2); the absence of such an unknown overlap is the unsafe obligation of §5.2.1. Accesses through the result, Reborrows from it and their children are checked under the anchor as usual.
- **Caller Loans.** When the result is returned under a signature Origin, the caller keeps the Loan bound to that Origin for the result's region (§15.6.4). Matching an Origin creates neither a Loan nor exclusivity (§15.3.5); the exclusivity of a `uniq` result rests on the unsafe promise alone.
- **Obligation.** Throughout the use of the result and of every derived value that keeps its Origin, the conditions of §5.2.1 must hold for the borrow's accesses. For `uniq`, no other path, including another raw pointer, may access the storage.

```kimi
func get(self: ref/Self, index: isize) -> ref/T
    require 0 <= index and index < self.length else => $abort("Index out of range")
    unsafe => return self.data[index]@ref   // The Origin is fitted to the result Type (self).
```

### 5.2.3. Taking from raw Places

Raw Places, and their Fields and elements, offer Take (§15.1.5). Their state is not tracked.

- `(*p)@move` takes the value out and leaves the storage Uninitialized. Preventing a later read or a second destruction, through another pointer or through the original owner, is the author's obligation; the compiler need not identify that owner or suppress its automatic destruction.
- `_ = (*p)@move` destroys the value in place.
- Bare acquisition follows the rule for every Place: a Copy value is Copied, and a Non-Copy value is an error (§3.5).

```kimi
// Foo is Non-Copy; pointer refers to an initialized Foo.
unsafe
    let value = (*pointer)@move // Take Foo.
    use(value)
    // The author prevents destruction of the taken value by its old owner.
```

## 5.3. Pointer arithmetic and indexing

For `p: raw/T` and `n: isize`, including negative `n`, only these arithmetic and indexing forms exist:

| Form | Meaning |
| --- | --- |
| `p + n` | Pointer displaced by `n * stride(T)` bytes. |
| `p - n` | Pointer displaced by `-n * stride(T)` bytes. |
| `p[n]` | The same memory Place as `*(p + n)`. |

`p += n` and `p -= n` apply these displacements through [compound assignment](13-operators-and-assignment.md#1372-compound-assignment), under the same unsafe conditions. Pointer increment and decrement are not supported. Pointer-from-pointer subtraction, integer-left addition (`n + p`) and all other pointer arithmetic are forbidden.

`stride(T)` is the complete element spacing including padding (§21.1), the same quantity that fixed arrays and Slice use. There is no source `sizeof` operator. Arithmetic requires a known layout. When `stride(T)` is zero, as for `raw/()`, every displacement is zero, so `p + n` and `p - n` are `p` and `p[n]` is the Place `*p`; distinct zero-sized elements may share an address, as in fixed arrays (§4.1). A generic `raw/T` therefore needs no size Constraint.

A zero displacement preserves the pointer, including null. A nonzero displacement requires live-allocation provenance, and both the source and the result must lie inside the allocation or one past its end. The result keeps the provenance; a matching address alone is not enough. A mathematical displacement outside `isize`, or address wraparound, is undefined behavior.

Arithmetic alone requires neither pointee initialization nor alignment. One-past pointers may be held and used in permitted arithmetic, but not dereferenced. Pointer indexing has no length and no implicit bounds check; it must meet both the arithmetic and the dereference requirements.

```kimi
unsafe
    let next = pointer + 1
    let prev = pointer[-1]
    pointer[10] = 123
    pointer[^1]   // Error: an offset is an isize value, not a position.
    pointer[0..4] // Error: no range indexing.
```

## 5.4. Addresses and pointer conversions

| Form | Meaning |
| --- | --- |
| `P@raw` | The address of the Place `P`. The result is `raw/V`, where `V` is the stored Type of `P`. |
| `E@raw/U` | Raw pointer acquisition and conversion. `E` is a raw pointer, a `usize` value or `null`. |

Bare `@raw` is an operation that takes no Type, like `@follow`; it is not Semantics shorthand (§13.5.1). `@raw/U` only converts and never takes an address. For `p: raw/T`, `p@raw` is the address of the slot `p` (`raw/(raw/T)`), while `p@raw/u8` converts the value of `p`. Annotate a binding to state the expected result Type.

**Address.** `@raw` applies to the written slot itself. The referent of a reference is written `r@follow@raw`, the payload of a Sealed object `h@follow@raw`, and a part of a raw Place `(*p).field@raw`. The operation performs the checks of an immediately ending `@ref`: initialization, read capability and Loan conflicts. It reads no value and leaves no Loan. A Temporary Value is materialized as for `@ref`, and the pointer is valid only for the lifetime of that temporary. An implementation treats a local variable whose address is taken as address-observed (impl §21.5.5). In an effect summary, `P@raw` is an access to the Place `P`, an immediately ending borrow; on a mutable static Place it is an environment effect (§8.4.10.2).

```kimi
var number: i32 = 1
let p = number@raw            // raw/i32; no Loan is created.
let r = number@uniq
let q = r@follow@raw          // Writable only inside r's Loan.
unsafe
    *q = 2                    // Valid: an access inside r's Loan.
    *p = 3                    // Undefined behavior: conflicts with r's exclusive Loan.
r@follow += 1                 // r is used later, so its Loan is live at both accesses above.
```

**Same Type.** When two complete raw pointer Types match after normalization (with Type aliases expanded), `@raw/U` performs ordinary same-Type acquisition: it copies the pointer and preserves its address, provenance, and Origin information and constraints. Matching size or memory layout alone does not make Types match. No new access permission or ownership is granted.

```kimi
// pointer has Type raw/Node.
let a = pointer             // Copy.
let b = pointer@raw/Node    // Same-Type Copy.
```

**Conversions.** Between distinct normalized Types, `@` supports the raw pointer conversions `raw/T -> raw/U`, `raw/T -> usize` and `usize -> raw/T`. An integer literal used as a pointer-cast input is first fitted to `usize`. Within one address space, a pointer cast preserves address and provenance; it changes no memory, initialization or alignment and grants no access as `U`.

```kimi
let bytes = pointer@raw/u8
unsafe
    let typed = (bytes + 13)@raw/i32   // The arithmetic needs the unsafe context, not the casts.
    // Access through typed still requires i32 alignment and a valid i32.
```

`raw/u8` permits byte-sized arithmetic, not reads of uninitialized memory. A cast neither reads a pointee nor requires a valid, aligned value of the destination pointee Type; dereference and access do. Conversions create no provenance.

## 5.5. Target and round-trip guarantees

Pointer/integer conversion requires a target whose ordinary data addresses fit losslessly in `usize`, whose pointer-address, address-index, `usize` and `isize` widths agree, and which provides the guarantees below. Multiple address spaces, and integer conversion of pointers that carry extra state such as capabilities, are excluded. An unsupported conversion is a compile-time error; CPU and OS support is target-specific.

Null converts to integer zero, and integer zero converts to null, without requiring an all-zero internal pointer representation.

Converting a pointer to `usize` guarantees only its numeric address. `usize` does not carry provenance in the language; its Copy, Move, argument passing, return and storage follow the ordinary integer rules. Converting the same numeric address back to the original pointer Type within the same execution guarantees an equal address, but neither preserves nor recovers provenance. Integer arithmetic or serialization cannot strengthen this guarantee.

```kimi
let address = pointer@usize
let saved = address
let restored = saved@raw/i32
// Preserves the address only; access validity is a separate requirement.
```

Conversion extends no lifetime and restores no permission. Supported targets allow arbitrary integer-to-pointer casts, but a use that depends on provenance requires a documented compiler/target guarantee in addition to the normal lifetime, alignment, initialization and access conditions; an unsafe context alone is not enough. To preserve provenance portably, keep the original pointer. Addresses from another execution have no validity guarantee, and address equality never proves access validity.

## 5.6. Raw storage operations

The public group `Kimi.Raw` provides the operations on raw storage:

```kimi
public group Raw
    public func allocate<T>(count: isize) -> raw/T
    public unsafe func release<T>(storage: raw/T)
    public unsafe func initialize<T>(storage: raw/T, value: T)
    public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

| Function | Contract |
| --- | --- |
| `allocate` | Safe. Allocates uninitialized storage for `count` elements. A negative `count`, an overflow of `count * stride(T)` or an allocation failure Aborts (§22.5.2). An alignment above 16 is reported as unsupported at compile time. When the size is zero bytes, nothing is allocated, and a non-null address aligned for `T` is returned instead; its provenance is a region of length zero, so the only valid accesses are to zero-sized pointees, and the address may equal other zero-byte results. |
| `release` | `storage` is null or the start address of an `allocate` result not yet released, possibly converted to another raw pointer Type; an interior address is not allowed. Every element must already be destroyed or taken; `release` destroys no element. Null and zero-byte results are ignored. After the release, every pointer derived from that allocation is invalid. |
| `initialize` | `storage` points to live, aligned, writable storage that holds no initialized value. Moves `value` there without destroying any old contents. |
| `slice` | Forms a shared `Slice` of `length` elements starting at `storage`. `length >= 0` is the caller's obligation. When `length == 0`, `storage` may be anything, including null, and is not accessed. When `length > 0`, `storage` is non-null and aligned for `T`, `length * stride(T)` fits in `isize`, and the elements are initialized, valid and within the provenance. The result Origin `s` is a universal Origin appearing only in the result (§15.3.1, §15.3.4), fixed by the caller's expected Type. Like a raw Place borrow, the result has a fresh anchor (§5.2.2); its elements must not be written for the whole Origin. |

Elements are taken and destroyed with the operations of §5.2.3. An allocation that does not Abort may later be added as `tryAllocate`, following the naming pair of §4.7.1.

In an effect summary, `allocate` is an allocation, and `release`, `initialize` and `slice` are raw accesses through their pointer arguments; none is an environment effect (§8.4.10.2).

A user container can then be written without compiler-known primitives:

```kimi
public struct Buffer<T>
    let data: raw/T
    var length: isize
    let capacity: isize

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
        unsafe => return self.data[index]@ref        // The Origin is fitted to self.

    public func items(self: ref/Self) -> Slice<T>
        unsafe => return Raw.slice(self.data, self.length)

    public func view(self: ref/Self) -> BufferView<T>   // source is fitted to self.
        return BufferView<T>.init(self)

    drop
        let data = self.data
        while self.length > 0
            self.length -= 1
            unsafe => _ = data[self.length]@move     // Destroy in reverse order.
        unsafe => Raw.release(data)

public struct BufferView<T> {source}
    let loan: Loan<ref/Buffer<T> during source>
    let data: raw/T
    let length: isize

    init(buffer: ref/Buffer<T> during source)
        self.data = buffer.data
        self.length = buffer.length
        self.loan = Loan.init(buffer)

    public func get(self: ref/Self, index: isize) -> ref/T during self.source
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref
```

- **Views and borrow checking.** While a `BufferView` lives, the shared Loan of `source` is kept, so `push` on the `Buffer` is an ordinary borrow error. Only the library invariant that `data` points to the `Buffer`'s elements is unsafe.
- **Elements with references.** When `T` contains references, as `ref/i32 during a` does, `T` appears in the Type `Buffer<T>`, so an inserted value's Loans are kept while the `Buffer` and every value taken or borrowed from it live (§5.2.1).
- **Variance.** A `raw/T` Field makes `Buffer<T>` invariant in `T` (§15.3.5).

Unsafe Function Types, unsafe function values, unowned references and unsafe weak pointers remain outside this revision ([Appendix D](appendices/D-deferred-features.md)). Example functions such as `obtainPointer` and `use` are illustrative, not standard API declarations.
