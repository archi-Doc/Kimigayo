# 5. Raw pointers and unsafe memory

[Specification index](../SPEC.md)

`unsafe/T` is a non-owning raw pointer to storage for the complete immediate Referent Type `T`, which determines access and element-sized arithmetic. This includes reference or pointer values, such as `unsafe/ref/i32` and `unsafe/unsafe/i32`. Pointers are Copy regardless of `T`; copying or destroying one does not copy or destroy its pointee or free storage.

```kimi
let first: unsafe/Foo = obtainPointer()
let second = first // Copy the pointer, not Foo.
```

A raw pointer guarantees neither pointee lifetime, initialization, alignment, nor access permission. Ownership, Loan, Origin, reference, aliasing, and data-race rules still govern the same storage. Unsafe context permits unverifiable operations without waiving these obligations.

Missing required unsafe context, invalid Types, and unsupported operations are compile-time errors. Violating runtime memory-safety requirements is undefined behavior; detection and runtime checks are not guaranteed.

| Operation | Unsafe Block required |
| --- | --- |
| Declare, hold, Copy, Move, pass, or destroy a raw pointer | No |
| Explicit `@` acquisition of the same normalized raw pointer Type | No; operand evaluation may require it |
| Create a contextually typed `null` | No |
| Equality of same-Type pointers, or a pointer and `null` | No |
| Call an unsafe function | Yes |
| Dereference or index a raw pointer | Yes |
| Pointer arithmetic | Yes |
| Conversion between distinct raw pointer Types, or between a raw pointer and an integer | Yes |

## 5.1. Null and equality

`unsafe/T` permits `null`, whose expected Type must determine `unsafe/T` or compilation fails. Safe references (`ref/T`, `uniq/T`, `objref/T`, `objuniq/T`) are non-null. Non-nullness alone does not validate a raw pointer.

```kimi
let pointer: unsafe/i32 = null
let typedNull = null@unsafe/i32 // Typed Null Formation, not a pointer cast.
let unknown = null // Error: no pointer Type can be determined.
let empty = pointer == null
```

`==` and `!=` compare the addresses of two pointers with the same Type and return `bool`, without reading pointees. A comparison with `null` gives the literal the other operand's pointer Type. Null equals null and never equals a non-null pointer.

Initialized pointers may be compared even if null or dangling. Equal addresses imply neither equal provenance nor ownership or access permission. Different pointer Types require an explicit unsafe conversion to a common Type. Pointer ordering comparisons are not defined.

## 5.2. Dereference and ownership

Unsafe access obeys the storage/content lifetime distinction in §15.7.3. A payload update preserves its allocation but may invalidate old-content pointers and dependencies. Raw pointers supply no safe Loan, exclusivity, or completeness proof.

`*pointer` denotes a memory place of Type `T`. Forming it requires live storage covering the required range, valid alignment, and provenance; null and one-past-the-end pointers cannot be dereferenced. **Provenance** records the allocation a pointer derives from and the basis for its accesses.

Actual reads require initialized, valid `T` values and read permission. Writes require write permission and must obey initialization and replacement rules. All accesses must respect reference, aliasing, and data-race rules.

```kimi
let pointer: unsafe/i32 = obtainPointer()
unsafe
    let value = *pointer
    *pointer = 10
pointer = other // Error: the let binding cannot be reassigned.
```

Binding mutability does not determine pointee write permission. Normal Copy, Move, and assignment rules apply. Moving a non-Copy pointee leaves storage uninitialized; the programmer must prevent later reads and double destruction through other pointers or the original owner. The compiler need not identify that owner or suppress its automatic destruction.

```kimi
// Foo is non-Copy; pointer refers to an initialized Foo.
unsafe
    let value = *pointer // Move Foo.
    use(value)
    // The programmer must prevent destruction of the moved source by its old owner.
```

Replacing an initialized pointee uses normal destruction rules. Initializing uninitialized raw storage requires a separately specified operation; ordinary assignment is not a substitute.

## 5.3. Pointer arithmetic and indexing

For `p: unsafe/T` and `n: isize`, including negative `n`, only these arithmetic and indexing forms are supported:

| Form | Meaning |
| --- | --- |
| `p + n` | Pointer displaced by `n * stride(T)` bytes. |
| `p - n` | Pointer displaced by `-n * stride(T)` bytes. |
| `p[n]` | The same memory place as `*(p + n)`. |

`p += n` and `p -= n` combine these displacements with [compound assignment](13-operators-and-assignment.md#1372-compound-assignment) and require the same unsafe conditions. Pointer increment and decrement are not supported.

`stride(T)` is the complete element spacing, including padding, under §21.1; it is the same quantity used by fixed arrays and Slice. No source `sizeof` operator is introduced. Arithmetic requires known layout and positive stride. Mathematical displacement outside `isize`, or address wraparound, is undefined behavior.

Zero displacement preserves the pointer, including null, but requires known layout and positive stride. Thus `unsafe/()` arithmetic and indexing are invalid; holding, comparing, and valid dereference are separate operations. Nonzero displacement requires live-allocation provenance, with both source and result inside or one past the allocation. The result retains provenance; a matching address alone is insufficient.

Arithmetic alone requires neither pointee initialization nor alignment. One-past pointers may be held and used in permitted arithmetic, but not dereferenced. Pointer indexing has no length or implicit bounds check and must meet both arithmetic and dereference requirements.

```kimi
unsafe
    let next = pointer + 1
    let prev = pointer[-1]
    pointer[10] = 123
    pointer[^1]   // Error: no from-end indexing.
    pointer[0..4] // Error: no Range indexing.
```

Pointer subtraction from another pointer, integer-left addition, and other pointer arithmetic are forbidden.

## 5.4. Pointer conversions

When the complete raw pointer Types match after normalization, `@` performs ordinary same-Type acquisition. It copies the pointer, preserves address, provenance, and Origin information and constraints, and does not itself require unsafe context. Expand Type aliases for this comparison; matching size or memory layout alone is insufficient. It grants no new access permission or ownership. Unsafe operations in operand evaluation still require unsafe context.

```kimi
// pointer has Type unsafe/Node.
let a = pointer             // Copy; no unsafe context required.
let b = pointer@unsafe/Node // Same-Type Copy; no unsafe context required.
```

For distinct normalized raw pointer Types, `@` supports `unsafe/T -> unsafe/U` in unsafe context. `unsafe/T -> usize` and `usize -> unsafe/T` also require unsafe context. Fit integer literals used as pointer-cast inputs to `usize` before converting. Pointer casts within one address space preserve address and provenance without changing memory, initialization, or alignment, or granting access as `U`.

```kimi
unsafe
    let bytes = pointer@unsafe/u8
    let shifted = bytes + 13
    let typed = shifted@unsafe/i32
    // Access through typed still requires i32 alignment and a valid i32.
```

`unsafe/u8` permits byte-sized arithmetic, not reads of uninitialized memory. A cast itself does not read a pointee or require a valid, aligned value of the destination pointee Type; dereference and access do.

## 5.5. Target and round-trip guarantees

Pointer/integer conversion initially requires a target whose ordinary data addresses fit losslessly in `usize`, whose pointer-address, address-index, `usize`, and `isize` widths agree, and which provides the guarantees below. Multiple address spaces and integer conversion of pointers carrying extra state (such as capabilities) are excluded. Unsupported conversions are compile-time errors; CPU/OS support is target-specific.

Null converts to integer zero, and integer zero converts to null, without requiring an all-zero internal pointer representation. These explicit conversions still require unsafe context.

Converting a pointer to `usize` guarantees its numeric address only. `usize` is not a language-level carrier of provenance; Copy, Move, argument passing, return, and storage follow ordinary integer rules. Converting the same numeric address back to the original pointer Type within the same execution guarantees address equality, but not preservation or recovery of provenance. Integer arithmetic or serialization cannot strengthen this guarantee.

```kimi
unsafe
    let address = pointer@usize
    let saved = address
    let restored = saved@unsafe/i32
    // Preserves the address only; access validity is a separate requirement.
```

Conversion extends no lifetime and restores no permissions. Supported targets allow arbitrary integer-to-pointer casts, but provenance-dependent use requires a documented Compiler/target guarantee plus normal lifetime, alignment, initialization, and access conditions. Unsafe context alone is insufficient. Retain the original pointer for portable provenance preservation. Addresses from another execution have no validity guarantee.

A raw pointer need not have access provenance merely to be held, copied, moved, passed, destroyed, compared with the same pointer Type, or tested against null; these uses need no unsafe context. Pointer/usize and pointer-Type conversions still require supported-target and unsafe conditions and create no provenance. Arithmetic always requires unsafe context, known layout, and positive stride; nonzero displacement also requires live-allocation provenance and bounds. Dereference/indexing must satisfy place-formation conditions, and actual reads/writes require initialization and access permissions. Address equality proves no access validity.

## 5.6. Raw pointer API design boundaries

Raw pointer acquisition APIs, allocation and deallocation, initialization of raw storage, conversion to or from safe references, ownership acquisition, and Unsafe Function Types are specified separately. Example functions such as `obtainPointer` and `use` are illustrative, not standard API declarations.
