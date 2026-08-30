# Overview

**Kimigayo** is a programming language designed and built from scratch with the goals of being consistent, fast, simple, fun, safe, and fast.

# Identifier

Kimigayo uses the following information to identify declarations and their meaning:

| Element     | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| `Name`      | The basic human-readable name used to refer to a declaration          |
| `Signature` | The information that distinguishes declarations in the same scope     |
| `Type`      | The meaning of a value or invocation within the type system           |

## Name

A Name is the basic name by which a declaration is written and referred to. The same character rules apply to the names of classes, types, functions, fields, properties, parameters, and other named declarations.

A Name is non-empty and consists of a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`),
- an underscore (`_`), or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`), or Letter Number (`Nl`).

Each continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`), or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`), Connector Punctuation (`Pc`), or Format (`Cf`).

Contextual keywords may be used as Names where an identifier is expected. Reserved keywords may not be used as Names.

For example, `Dog`, `_value`, `point2`, `日本語`, and `ǅelta` are valid Names, while `2point`, `has-value`, and the empty string are not.

## Signature

A declaration has a Signature. A Signature consists of the information required to distinguish the declaration from other declarations in the same declaration scope.

The Signature of each declaration kind consists of the following information:

| Declaration kind | Signature information                                                 |
| ---------------- | --------------------------------------------------------------------- |
| Type             | Type Semantics, Name, and generic parameter count                     |
| Function         | Name, generic parameter count, and an ordered list of parameter Signatures |
| Parameter        | Type                                                                  |
| Field            | Name                                                                  |
| Property         | Name                                                                  |

Each function parameter contributes its Type to the function Signature. Parameter names, return types, default values, and declaration modifiers are not part of the function Signature.

Consequently, two functions in the same scope may share a Name when their generic parameter counts or parameter types differ. Two fields or two properties with the same Name in the same scope have the same Signature and therefore cannot be distinguished by Signature alone.

# Type

> Types are everything for programming languages, words are everything in design.

A Kimigayo type consists of **Type Semantics**, a **Core Type**, and an **Origin**.

```ini
Type = semantics/CoreType from origin
```

- **Type Semantics** — Describe how the value is represented, owned, accessed, or used.
- **Core Type** — Describes what the value is.
- **Origin** — Describes where the value derives from and constrains its lifetime or validity.

For example:

```
ref/Dog from owner
```

means a value with the Core Type `Dog`, accessed with `ref` semantics, whose validity derives from `owner`.

Type Semantics and Origin may be omitted from source notation when they are determined by the language or context. The Core Type is always present.

In short:

```
Type Semantics — How
Core Type      — What
Origin         — Whence
```

These three elements form the core of Kimigayo's type system.

## Core Types

Kimigayo provides a fixed set of primitive Core Types and user-defined structure Core Types.

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

#### Unit and Never Types

`()` is the Unit type. It has one value and represents the absence of a meaningful result.

Never is the type of an expression that does not complete normally and has no values. `return`, `break`, and `continue` expressions have the Never type.

### Structures

A `struct` defines a composite value type.

A structure may contain fields whose Core Types are:

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

# Index, Range, and Slice

An Index is a nonnegative `isize` value. Applying an Index with `value[index]` selects one element. The resolved Index must be less than the length of the indexed value.

A prefix caret denotes an Index measured from the end. `^n` resolves to `length - n`, where `n` is a nonnegative `isize`. Therefore, `^1` selects the last element. `^0` is a valid Range boundary but is not a valid element Index. Infix `^` remains the exclusive-or operator.

A Range is an expression with optional start and end boundaries. Each explicit boundary is either a nonnegative `isize` Index or a from-end Index.

| Form       | Selected boundaries                  |
| ---------- | ------------------------------------ |
| `start..end`  | From `start`, excluding `end`     |
| `start..=end` | From `start`, including `end`     |
| `start..`     | From `start` to the end            |
| `..end`       | From the beginning, excluding `end` |
| `..=end`      | From the beginning, including `end` |
| `..`          | The entire range                   |

The omitted start boundary is zero. The omitted end boundary is the length of the indexed value and is exclusive. An inclusive Range must have an end boundary.

Range operators bind less tightly than logical operators and more tightly than assignment. Ranges are non-associative; an unparenthesized chained Range such as `a..b..c` is invalid.

Applying a Range with `value[range]` produces a Slice over the selected consecutive elements. A Slice does not copy its elements. Its Origin derives from the indexed value, so it cannot outlive that value.

After resolving from-end boundaries, an exclusive Range must satisfy `0 <= start <= end <= length`. An inclusive Range must satisfy `0 <= start <= end < length`.

For a value of length six, `value[1..^1]` selects the elements at Indices 1, 2, 3, and 4.

# Control Flow

An indentation-delimited Block is an expression. Its value is its final expression. An empty Block, or a Block ending in a declaration, has the Unit value. Declarations do not produce values.

`break` exits the innermost loop, and `continue` begins its next iteration.

## `for`

A `for` expression evaluates its iterable once and executes its body once for each yielded value. Its value is Unit, and the value of its body is discarded.

```kimi
for value in values
    process(value)

for (key, value) in dictionary
    process(key, value)
```

A single Name binds each yielded value. A parenthesized, comma-separated binding destructures each yielded value. `in` is a contextual keyword and acts as a delimiter only in a `for` header.

## `while`

A `while` expression evaluates its Boolean condition before each iteration and executes its body while the condition is true. Its value is Unit, and the value of its body is discarded. Parentheses around the condition are optional.

```kimi
while ready
    process()

while (ready)
    process()
```

## `loop`

A `loop` expression repeatedly executes its body without a condition. The normal value of the body is discarded.

```kimi
var result = loop
    if ready
        break value
```

The value and type of a `loop` expression are determined by its reachable `break` expressions. `break` without a value supplies Unit, and all reachable breaks must supply compatible values. A `loop` with no reachable `break` has the Never type. A value-bearing `break` is not permitted in a `for` or `while` expression.

## `match`

A `match` expression evaluates its subject once and tests its arms in source order. The first matching arm is evaluated.

```kimi
match value
    0 => "zero"
    1 =>
        var text = "one"
        text
```

An arm body may be an inline expression or an indentation-delimited Block. A `match` must be exhaustive. Its type is the common type of its reachable arm values; a Never-valued arm does not constrain that type.

# Type Semantics

Type Semantics specify the ownership, borrowing, layout, and safety properties of a typed value.

The qualified syntax is `Semantics/CoreType`. The complete type form adds `from Origin`.

In the syntax below, `T` denotes a Core Type.

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

Type Semantics are orthogonal to the Core Type.

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

Each has the same `Data` Core Type but different ownership, storage, borrowing, or safety semantics.

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
