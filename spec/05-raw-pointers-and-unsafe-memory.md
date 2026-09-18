# 5. Raw pointers and unsafe memory

[Specification index](../SPEC.md)

`unsafe/T` is a non-owning raw pointer to storage for its complete immediate Referent Type `T`, which determines access and element-sized arithmetic. `T` may itself be a reference or pointer Type, as in `unsafe/ref/i32` and `unsafe/unsafe/i32`. Pointers are Copy regardless of `T`; copying or destroying a pointer neither copies nor destroys its pointee and frees no storage.

```kimi
let first: unsafe/Foo = obtainPointer()
let second = first // Copy the pointer, not Foo.
```

A raw pointer guarantees neither the pointee's lifetime, initialization or alignment, nor access permission. Ownership, Loan, Origin, reference, aliasing and data-race rules still govern the same storage. An unsafe context permits operations the compiler cannot verify; it does not waive these obligations.

A missing required unsafe context, an invalid Type and an unsupported operation are compile-time errors. Violating a runtime memory-safety requirement is undefined behavior; detection and runtime checks are not guaranteed.

| Operation | Unsafe context required |
| --- | --- |
| Declare, hold, Copy, Move, pass or destroy a raw pointer | No |
| Explicit `@` acquisition of the same normalized raw pointer Type | No; evaluating the operand may require it |
| Create a contextually typed `null` | No |
| Equality of same-Type pointers, or of a pointer and `null` | No |
| Call an unsafe function | Yes |
| Dereference or index a raw pointer | Yes |
| Pointer arithmetic | Yes |
| Conversion between distinct raw pointer Types, or between a raw pointer and an integer | Yes |

None of the operations that need no unsafe context require the pointer to have access provenance.

## 5.1. Null and equality

`unsafe/T` permits `null`; its expected Type must determine `unsafe/T`, or compilation fails. Safe references (`ref/T`, `uniq/T`, `objref/T`, `objuniq/T`) are non-null. Non-nullness alone does not make a raw pointer valid.

```kimi
let pointer: unsafe/i32 = null
let typedNull = null@unsafe/i32 // Typed Null Formation, not a pointer cast.
let unknown = null // Error: no pointer Type can be determined.
let empty = pointer == null
```

`==` and `!=` compare the addresses of two pointers of the same Type and return `bool`, without reading the pointees. In a comparison with `null`, the literal takes the other operand's pointer Type. Null equals null and never equals a non-null pointer.

Initialized pointers may be compared even when null or dangling. Equal addresses imply neither equal provenance nor ownership or access permission. Pointers of different Types require an explicit unsafe conversion to a common Type. Pointer ordering comparisons are not defined.

## 5.2. Dereference and ownership

`*pointer` denotes a memory Place of Type `T`. Forming it requires live storage covering the required range, valid alignment and provenance; null and one-past-the-end pointers cannot be dereferenced. **Provenance** records the allocation a pointer derives from and the basis for its accesses.

An actual read requires an initialized, valid `T` value and read permission. A write requires write permission and obeys the initialization and replacement rules. Every access respects the reference, aliasing and data-race rules.

```kimi
let pointer: unsafe/i32 = obtainPointer()
unsafe
    let value = *pointer
    *pointer = 10
pointer = other // Error: the let binding cannot be reassigned.
```

Binding mutability does not determine pointee write permission. Normal Copy, Move and assignment rules apply. Moving a Non-Copy pointee leaves the storage Uninitialized; the programmer must prevent later reads and double destruction through other pointers or through the original owner. The compiler need not identify that owner or suppress its automatic destruction.

```kimi
// Foo is Non-Copy; pointer refers to an initialized Foo.
unsafe
    let value = *pointer // Move Foo.
    use(value)
    // The programmer must prevent destruction of the moved source by its old owner.
```

Replacing an initialized pointee uses the normal destruction rules. Initializing uninitialized raw storage requires a separate operation (§5.6); ordinary assignment is not a substitute.

Unsafe access obeys the distinction between storage lifetime and content lifetime in §15.7.3: a payload update keeps the allocation but may invalidate pointers and dependencies into the old contents. Raw pointers supply no safe Loan, exclusivity or completeness proof.

## 5.3. Pointer arithmetic and indexing

For `p: unsafe/T` and `n: isize`, including negative `n`, only these arithmetic and indexing forms are supported:

| Form | Meaning |
| --- | --- |
| `p + n` | Pointer displaced by `n * stride(T)` bytes. |
| `p - n` | Pointer displaced by `-n * stride(T)` bytes. |
| `p[n]` | The same memory Place as `*(p + n)`. |

`p += n` and `p -= n` combine these displacements with [compound assignment](13-operators-and-assignment.md#1372-compound-assignment) under the same unsafe conditions. Pointer increment and decrement are not supported. Subtracting one pointer from another, integer-left addition and all other pointer arithmetic are forbidden.

`stride(T)` is the complete element spacing, including padding (§21.1), the same quantity used by fixed arrays and Slice. There is no source `sizeof` operator. Arithmetic requires a known layout and a positive stride, so `unsafe/()` arithmetic and indexing are invalid, while holding, comparing and valid dereference remain separate operations. Zero displacement preserves the pointer, including null. Nonzero displacement requires live-allocation provenance, with both source and result inside the allocation or one past its end; the result keeps the provenance, and a matching address alone is insufficient. A mathematical displacement outside `isize`, or address wraparound, is undefined behavior.

Arithmetic alone requires neither pointee initialization nor alignment. One-past pointers may be held and used in permitted arithmetic, but not dereferenced. Pointer indexing has no length and no implicit bounds check, and must meet both the arithmetic and the dereference requirements.

```kimi
unsafe
    let next = pointer + 1
    let prev = pointer[-1]
    pointer[10] = 123
    pointer[^1]   // Error: no from-end indexing.
    pointer[0..4] // Error: no Range indexing.
```

## 5.4. Pointer conversions

When two complete raw pointer Types match after normalization (with Type aliases expanded), `@` performs ordinary same-Type acquisition: it copies the pointer and preserves its address, provenance, and Origin information and constraints. It does not itself require an unsafe context, although unsafe operations in the operand still do. Matching size or memory layout alone does not make Types match. No new access permission or ownership is granted.

```kimi
// pointer has Type unsafe/Node.
let a = pointer             // Copy; no unsafe context required.
let b = pointer@unsafe/Node // Same-Type Copy; no unsafe context required.
```

For distinct normalized raw pointer Types, `@` supports `unsafe/T -> unsafe/U` in an unsafe context. `unsafe/T -> usize` and `usize -> unsafe/T` also require an unsafe context; an integer literal used as a pointer-cast input is first fitted to `usize`. Within one address space, a pointer cast preserves address and provenance; it changes no memory, initialization or alignment and grants no access as `U`.

```kimi
unsafe
    let bytes = pointer@unsafe/u8
    let shifted = bytes + 13
    let typed = shifted@unsafe/i32
    // Access through typed still requires i32 alignment and a valid i32.
```

`unsafe/u8` permits byte-sized arithmetic, not reads of uninitialized memory. A cast neither reads a pointee nor requires a valid, aligned value of the destination pointee Type; dereference and access do. Conversions create no provenance.

## 5.5. Target and round-trip guarantees

Pointer/integer conversion initially requires a target whose ordinary data addresses fit losslessly in `usize`, whose pointer-address, address-index, `usize` and `isize` widths agree, and which provides the guarantees below. Multiple address spaces, and integer conversion of pointers carrying extra state such as capabilities, are excluded. An unsupported conversion is a compile-time error; CPU and OS support is target-specific.

Null converts to integer zero, and integer zero converts to null, without requiring an all-zero internal pointer representation. These explicit conversions still require an unsafe context.

Converting a pointer to `usize` guarantees only its numeric address. `usize` is not a language-level carrier of provenance; Copy, Move, argument passing, return and storage follow the ordinary integer rules. Converting the same numeric address back to the original pointer Type within the same execution guarantees address equality, but neither preservation nor recovery of provenance. Integer arithmetic or serialization cannot strengthen this guarantee.

```kimi
unsafe
    let address = pointer@usize
    let saved = address
    let restored = saved@unsafe/i32
    // Preserves the address only; access validity is a separate requirement.
```

Conversion extends no lifetime and restores no permissions. Supported targets allow arbitrary integer-to-pointer casts, but a provenance-dependent use requires a documented compiler/target guarantee in addition to the normal lifetime, alignment, initialization and access conditions; an unsafe context alone is insufficient. Keep the original pointer for portable provenance preservation. Addresses from another execution have no validity guarantee, and address equality never proves access validity.

## 5.6. Raw pointer API design boundaries

This revision does not specify raw pointer acquisition APIs, allocation and deallocation, initialization of raw storage, conversion to or from safe references, ownership acquisition, or Unsafe Function Types; they remain design work ([Appendix D](appendices/D-deferred-features.md)). Their absence adds no executable syntax, and the constraints of this chapter apply to any future design. Example functions such as `obtainPointer` and `use` are illustrative, not standard API declarations.
