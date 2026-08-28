

# Overview

Consistent, fast, simple, fun, safe, and fast.

**Kimigayo** is a programming language designed and built from scratch with these goals.



# Type

## Primitive Types and Structures

Kimigayo provides a fixed set of primitive types and user-defined structure types.

### Primitive Types

The following primitive types are built into the language.

Sizes below are storage sizes.

#### Signed Integers

| Type    | Size                |
| ------- | ------------------- |
| `i8`    | 8 bits (1 byte)     |
| `i16`   | 16 bits (2 bytes)   |
| `i32`   | 32 bits (4 bytes)   |
| `i64`   | 64 bits (8 bytes)   |
| `i128`  | 128 bits (16 bytes) |
| `isize` | Native pointer size |

`isize` is a signed integer type whose size corresponds to the native pointer size of the target platform.

#### Unsigned Integers

| Type    | Size                |
| ------- | ------------------- |
| `u8`    | 8 bits (1 byte)     |
| `u16`   | 16 bits (2 bytes)   |
| `u32`   | 32 bits (4 bytes)   |
| `u64`   | 64 bits (8 bytes)   |
| `u128`  | 128 bits (16 bytes) |
| `usize` | Native pointer size |

`usize` is an unsigned integer type whose size corresponds to the native pointer size of the target platform.

#### Floating-Point Types

| Type  | Size              |
| ----- | ----------------- |
| `f32` | 32 bits (4 bytes) |
| `f64` | 64 bits (8 bytes) |

#### Boolean Type

| Type   | Size               |
| ------ | ------------------ |
| `bool` | 8 bits (1 byte)    |

# Structures

A `struct` defines a composite value type.

A structure may contain fields whose underlying types are:

- primitive types,
- other structure types, or
- types qualified with Type Semantics.

For example:

```
struct Point
    x: f64
    y: f64

struct Node
    value: i32
    next: obj/Node

struct View
    source: ref/Data
```

The Type Semantics of a field determines how the referenced or contained value is represented, owned, borrowed, shared, and accessed.

# Type Semantics

Type Semantics specify the ownership, borrowing, layout, and safety properties of a typed value.

The qualified syntax is `semantics/T`, where `T` is the underlying type.

| Category      | Semantics    | Syntax                  | Layout or Meaning                     |
| ------------- | ------------ | ----------------------- | ------------------------------------- |
| Value         | Owner        | `T`, `owner/T`          | Data layout                           |
| Value Borrow  | SharedRef    | `ref/T`                 | Shared borrow of a value              |
| Value Borrow  | ExclusiveRef | `uniq/T`                | Exclusive mutable borrow of a value   |
| Object        | Owner        | `obj/T`                 | Metadata + Data                       |
| Object        | Rc           | `rc/T`                  | Rc metadata + Metadata + Data         |
| Object        | Arc          | `arc/T`                 | Arc metadata + Metadata + Data        |
| Object Borrow | SharedRef    | `objref/T`              | Shared borrow of an object            |
| Object Borrow | ExclusiveRef | `objuniq/T`             | Exclusive mutable borrow of an object |
| Unsafe        | Pointer      | `unsafe/T`              | Unsafe pointer                        |

## Value

`T` and `owner/T` represent a directly owned value with the data layout of `T`.

```
let x: i32
let p: owner/Point
```

`T` is equivalent to `owner/T`.

## Value Borrow

Value borrows provide non-owning access to value data and are subject to lifetime constraints.

### `ref/T`

`ref/T` is a shared borrowed reference to a value. Multiple shared references may coexist.

```
func read(value: ref/Data)
```

### `uniq/T`

`uniq/T` is an exclusive mutable borrowed reference to a value. No conflicting reference may coexist.

```
func modify(value: uniq/Data)
```

## Object

Object semantics represent object metadata followed by the data layout of `T`.

### `obj/T`

`obj/T` is an exclusively owned object.

```
let node: obj/Node
```

### `rc/T`

`rc/T` is a shared object with non-atomic reference-count metadata. The object remains alive while an owning reference exists.

```
let object: rc/Object
```

### `arc/T`

`arc/T` is a shared object with atomic reference-count metadata. Atomic ownership management does not guarantee safe concurrent mutation of `T`.

```
let object: arc/Object
```

## Object Borrow

Object borrows provide non-owning access to an object and are subject to lifetime constraints.

### `objref/T`

`objref/T` is a shared borrowed reference to an object. Multiple shared references may coexist.

```
func readObject(value: objref/Data)
```

### `objuniq/T`

`objuniq/T` is an exclusive mutable borrowed reference to an object. No conflicting reference may coexist.

```
func modifyObject(value: objuniq/Data)
```

## Unsafe

### `unsafe/T`

```
unsafe/T
```

`unsafe/T` represents an unsafe pointer to `T`.

Unlike safe ownership and borrowing semantics, `unsafe/T` is not required to satisfy the normal ownership, lifetime, aliasing, or exclusivity guarantees enforced by the language.

Operations involving `unsafe/T` therefore belong to the unsafe portion of the language and place additional correctness responsibilities on the programmer.

```
let pointer: unsafe/i32
```

# Composition

Structures may freely compose primitive types, structures, and Type Semantics where permitted by the rules of the corresponding semantics.

For example:

```
struct Header
    version: u32
    flags: u16

struct Buffer
    length: usize
    data: obj/Data

struct SharedState
    state: arc/State

struct Parser
    input: ref/Input
    position: usize
```

Type Semantics are orthogonal to the underlying type.

For example, given:

```
struct Data
    value: i32
```

the following are distinct types:

```
owner/Data
ref/Data
uniq/Data
obj/Data
rc/Data
arc/Data
objref/Data
objuniq/Data
unsafe/Data
```

Each has the same underlying `Data` type but different ownership, storage, borrowing, or safety semantics.

# Classification

The Type Semantics hierarchy is:

```
Type Semantics
├─ Value
│  └─ Owner          T, owner/T
│
├─ Value Borrow
│  ├─ SharedRef      ref/T
│  └─ ExclusiveRef   uniq/T
│
├─ Object
│  ├─ Owner          obj/T
│  ├─ Rc             rc/T
│  └─ Arc            arc/T
│
├─ Object Borrow
│  ├─ SharedRef      objref/T
│  └─ ExclusiveRef   objuniq/T
│
└─ Unsafe
   └─ Pointer        unsafe/T
```

This classification separates five usage models:

- **Value** — direct ownership of a value.
- **Value Borrow** — non-owning access to value data.
- **Object** — ownership of metadata and data.
- **Object Borrow** — non-owning access to an object.
- **Unsafe** — access outside the guarantees of the safe Type Semantics system.
