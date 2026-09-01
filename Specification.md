# Overview

**Kimigayo** is a programming language designed and built from scratch with the goals of being consistent, fast, simple, fun, and safe.

# Build Model

Kimigayo separates workspace orchestration, project configuration, library source, and target-specific compilation into the following model:

| Element | Responsibility |
| ------- | -------------- |
| Solution | Holds multiple Projects and supplies options shared by their builds. |
| Project | Defines one application or library build unit. It is configured by a `.kimiproj` file. |
| Kotonoha | Defines a named library source unit. It is is built from one or more Kimi source files. |
| Compilation | Compiles one Project for one target OS and architecture. |
| CodeContext | Carries the source-unit and diagnostic context used while source is parsed, generated, or inserted into a Koto tree. |

A Solution discovers and loads Projects. A Project contains common build settings, target triples, project-wide aliases, and external Kotonoha dependency descriptors. For each configured target, the Project creates a separate Compilation.

Each Compilation owns the application's or library's primary Kotonoha. A Compilation also provides the target triple, LLVM data layout, pointer width, conditional-compilation variables, and lookup by Kotonoha identifier. External Kotonoha descriptors are copied from the Project configuration; fetching and loading those external libraries is a later compilation stage and is not currently implemented.

A Kotonoha merges declarations from multiple `SourceDocument` instances into one root Koto tree. Tokenization and parsing occur per source document. Executable syntax written directly at the root—bindings, statements, expressions, and functions—is stored in an implicit generated function owned by the Kotonoha.

A CodeContext belongs to exactly one Kotonoha. It supplies the active Compilation and diagnostic destination to the Tokenizer and Parser. Generated source may be parsed into a selected Declaration Container in that same Kotonoha; inserting nodes into a Declaration Container owned by another Kotonoha is invalid.

The current front-end pipeline is:

```text
Solution -> Project -> Compilation(target)
                         |
                         v
                 Kotonoha + SourceDocuments
                         |
                         v
                 Tokenizer -> Parser -> Koto tree
                         |
                         v
                 LLVM IR -> binary (planned back-end stages)
```

After target preparation, the conditional-compilation environment contains `os`, `windows`, `linux`, `macos`, and `pointerWidth`. Unsupported target architectures or targets without an LLVM data layout do not produce a prepared Compilation.

# Identifier

Kimigayo uses the following information to identify declarations and their meaning:

| Element     | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| `Name`      | The basic human-readable name used to refer to a declaration          |
| `Signature` | The information that distinguishes declarations in the same scope     |
| `Type`      | The meaning of a value or invocation within the type system           |

## Name

A Name is the basic name by which a declaration is written and referred to. The same character rules apply to the names of Declaration Containers, types, functions, Properties, local bindings, parameters, and other named declarations.

A Name is non-empty and consists of a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`),
- an underscore (`_`), or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`), or Letter Number (`Nl`).

Each continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`), or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`), Connector Punctuation (`Pc`), or Format (`Cf`).

Contextual keywords may be used as Names in contexts that accept contextual identifiers. Reserved keywords may not be used as Names. In particular, `in` acts as a delimiter in a `for` header, and `has` introduces an inline Property accessor list; both may be used as Names outside those contexts.

For example, `Dog`, `_value`, `point2`, `日本語`, and `ǅelta` are valid Names, while `2point`, `has-value`, and the empty string are not.

## Signature

A declaration has a Signature. A Signature consists of the information required to distinguish the declaration from other declarations in the same declaration scope.

The Signature of each declaration kind consists of the following information:

| Declaration kind | Signature information                                                 |
| ---------------- | --------------------------------------------------------------------- |
| Type             | Type Semantics, Name, and generic parameter count                     |
| Function         | Name, generic parameter count, and an ordered list of parameter Signatures |
| Parameter        | Type                                                                  |
| Property         | Name                                                                  |

Each function parameter contributes its Type to the function Signature. Parameter names, return types, default values, and declaration modifiers are not part of the function Signature.

Consequently, two functions in the same scope may share a Name when their generic parameter counts or parameter types differ. Two properties with the same Name in the same scope have the same Signature and therefore conflict.

# Literals

## StringLiteral

A `StringLiteral` produces a value of the built-in `string` Type. Kimigayo source text and string contents use UTF-8. A literal may occupy one line or multiple lines.

There are two forms, distinguished by the number of double quotation marks in their delimiters:

| Form | Delimiter | Backslash escapes | Interpolation |
| ---- | --------- | ----------------- | ------------- |
| Escaped string (*Multi-line string with escape*) | One double quotation mark (`"`) on each side | Yes | Yes |
| Raw string (*Multi-line string without escape*) | The same number of double quotation marks, at least three, on each side | No | No |

### Escaped strings

An escaped string is enclosed by one double quotation mark on each side. A backslash introduces an escape sequence:

```kimi
"Hello, world"
"First line\nSecond line"
"
First line
Second line
"
```

The opening and closing delimiters are not part of the value. Any line break between them is part of the string content; `\n` may instead be used when an explicit line-feed escape is preferred.

Only the following escape sequences are supported:

| Escape | Result |
| ------ | ------ |
| `\0` | Null character, U+0000 |
| `\\` | Backslash (`\`) |
| `\e` | Escape character, U+001B |
| `\t` | Horizontal tab, U+0009 |
| `\n` | Line feed, U+000A |
| `\r` | Carriage return, U+000D |
| `\"` | Double quotation mark (`"`) |
| `\'` | Apostrophe (`'`) |
| `\u(H...)` | Unicode scalar value written as one to six hexadecimal digits |
| `\(expression)` | String interpolation |

For `\u(H...)`, the hexadecimal value must be a valid Unicode scalar value: it must not exceed U+10FFFF and must not be in the surrogate range U+D800–U+DFFF. An unsupported or incomplete escape sequence is invalid.

An interpolation begins with `\(` and ends at its matching `)`. The enclosed text is parsed as a Kimigayo expression, including any nested parentheses, and the expression's string representation is inserted into the surrounding string:

```kimi
"Hello, \(name)."
"Total: \(price * quantity)"
```

Interpolation is available only in escaped strings.

### Raw strings

A raw string is enclosed by matching delimiters of three or more consecutive double quotation marks. Backslashes, line breaks, and interpolation-like text are ordinary content; no escape processing or interpolation occurs.

```kimi
"""C:\Users\name\file.txt"""
"""
First line
Second line
"""
```

If the content must contain a run of double quotation marks that would otherwise match the delimiter, the outer delimiter is lengthened. A delimiter of `N` quotation marks permits any shorter run of quotation marks in the content. For example, four quotation marks allow `"""` to appear literally:

```kimi
""""The token """ appears here.""""
```

The opening and closing delimiters must contain the same number of quotation marks and are not part of the value.

# Declarations

## Bindings

Properties and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` declares a mutable binding. A Type annotation and an initializer are independently optional when the omitted information can be inferred.

```kimi
let limit: i32 = 10
var current = 0
```

## Properties

Kimigayo has exactly one kind of value-bearing member: the **Property**. A compiler may lower Property storage to a storage slot, global storage, or another layout entity, but none of these implementation representations constitutes another member kind. A `let` or `var` declared inside an executable Block is a local binding, not a Property.

A Property has a Type, a getter, an optional setter, and optionally owned storage:

```text
Property
    Type
    Storage?
    Getter
    Setter?
```

Every concrete Property is classified semantically as either stored or computed. The classification is derived after inline, bodyless, and omitted accessor behavior has been expanded:

```text
HasStorage
    ⇔ the effective Property representation contains a reference
      bound to the contextual identifier `storage`
```

A reference counts regardless of whether it occurs in an expression-bodied or Block-bodied accessor. Unreachable-code analysis does not change this structural classification. The spelling `storage` outside a Property accessor is an ordinary Name; only an occurrence bound to the accessor's contextual identifier counts.

During accessor binding, `storage` is available provisionally; this does not presuppose that the Property is stored. If no effective accessor binds a reference to it, no Property-owned storage is created.

The semantic processing order is:

```text
Property declaration
    -> expand an inline `has` clause, if present
    -> expand bodyless and implicit accessor behavior
    -> create the effective Property representation
    -> bind accessor bodies and contextual identifiers
    -> detect references bound to `storage`
    -> determine HasStorage
```

- A **Stored Property** has `HasStorage = true` and owns one storage location.
- A **Computed Property** has `HasStorage = false` and owns no storage location.

For an instance Property, owned storage participates in the containing instance's layout. For a static member of a `group`, it has static storage instead. A computed Property contributes no storage slot in either case.

### Effective representation

The common declaration forms expand as follows before storage classification:

| Source form | Effective getter | Effective setter | Usual classification |
| ----------- | ---------------- | ---------------- | -------------------- |
| `let x: T` | `get => storage` | None | Stored |
| `var x: T` | `get => storage` | `set { storage = value }` | Stored |
| `var x: T` with only an explicit `get` | Explicit getter | None | Depends on `storage` use |
| `var x: T` with only an explicit `set` | `get => storage` | Explicit setter | Stored |
| `var x: T` with explicit `get` and `set` | Explicit getter | Explicit setter | Depends on `storage` use |

An initializer does not independently select the classification. It is valid only if the resulting effective representation has storage.

For example:

```kimi
var Count: i32
```

has the effective representation:

```kimi
var Count: i32
    get => storage

    set
        storage = value
```

Likewise, an explicit setter does not suppress the default getter:

```kimi
var Age: i32 = 0
    set
        storage = max(value, 0)
```

is equivalent for classification to:

```kimi
var Age: i32 = 0
    get => storage

    set
        storage = max(value, 0)
```

By contrast, an explicit getter suppresses the default accessors not written with it. Therefore this is a read-only computed Property:

```kimi
var Count: i32
    get => items.Count
```

It contains no reference to `storage`, so `HasStorage = false`.

`let` is reserved for immutable stored data. Its standard effective representation has the default storage-reading getter and no setter, and it cannot be used for a computed Property. A read-only computed Property uses `var` with an explicit getter.

### Accessors

A getter defines a Property read and must produce a value compatible with the Property Type. It may be expression-bodied or Block-bodied:

```kimi
var Area: f64
    get => Width * Height

var LoggedArea: f64
    get
        LogRead()
        return Width * Height
```

A computed getter must be introduced explicitly with `get`; a bare expression in the Property body is invalid.

A setter defines a Property write. Within it, `value` is the incoming value and has the Property Type:

```kimi
var Percentage: i32 = 0
    set
        storage = clamp(value, 0, 100)
```

The setter does not declare this parameter in source. For example, evaluating:

```kimi
obj.Percentage = 120
```

invokes the setter with `value` bound to `120`; the source form is `set`, not `set value`.

Reading a Property invokes its getter. Assignment after initialization invokes its setter; assigning a Property without a setter is invalid. An accessor body may refer to other state instead of owned storage, so custom accessors do not by themselves make a Property computed or stored:

```kimi
var Width: f64
    get => Right - Left

    set
        Right = Left + value
```

Neither accessor refers to `storage`, so this is a read-write computed Property.

### Inline accessor declarations

A Property may declare bodyless accessors inline with a `has` clause:

```kimi
var Count: i32 has get, private set
```

The clause follows the Property initializer when one is present:

```kimi
var Count: i32 = 0 has get, private set
```

The grammar is:

```text
inline-accessors := has accessor-declaration (',' accessor-declaration)*

accessor-declaration := access-restriction? get
                      | access-restriction? set
```

The list must contain at least one accessor. `get` and `set` may each appear at most once. Their order has no semantic effect, although `get` followed by `set` is conventional. Access restrictions follow the same rules as accessors written in a Property body.

#### Concrete Properties

For a concrete Property, `has` expands to the corresponding bodyless accessor declarations before the effective representation is created:

```kimi
var Count: i32 has get, private set
```

is equivalent to:

```kimi
var Count: i32
    get
    private set
```

A bodyless getter and setter have these default implementations:

```kimi
get => storage

set
    storage = value
```

The example therefore has this effective representation:

```kimi
var Count: i32
    get => storage

    private set
        storage = value
```

`has` does not introduce a separate storage rule. After expansion, `HasStorage` is determined from references bound to `storage` in the normal way. For example, `var Value: i32 has get` expands to `get => storage` with no setter and is therefore a stored, read-only Property.

Inline and indentation-delimited accessor lists cannot be combined in one Property declaration. An accessor that needs a custom body must use the indentation-delimited form:

```kimi
var Percentage: i32
    get => storage

    private set
        storage = clamp(value, 0, 100)
```

The normal `let` restrictions continue to apply; in particular, a `let` Property cannot declare a setter.

#### Contract Property Requirements

Inside a `contract`, `has` declares the accessor capabilities that a conforming Property must provide:

```kimi
contract Collection
    var Count: i32 has get

contract MutableCollection
    var Count: i32 has get, set
```

The first requirement is readable; the second is both readable and writable. A conforming Property must have a compatible Type and provide every required accessor with sufficient accessibility.

In this context, `has` introduces no accessor implementation, effective storage representation, or Property storage. A contract Property requirement therefore has no `HasStorage` classification. A conforming Property may be stored or computed:

```kimi
// Stored implementation
var Count: i32 has get

// Computed implementation
var Count: i32
    get => items.Count
```

Both may satisfy `var Count: i32 has get` in a contract. Thus the declaration context determines the meaning of the shared syntax:

```text
Concrete Property
    has ... -> bodyless accessor declarations
            -> default behavior and HasStorage analysis

Contract Property requirement
    has ... -> required accessor availability only
            -> no implementation or storage semantics
```

### Contextual identifiers and receivers

`storage` denotes the actual storage location owned by the current Property, rather than a detached copy. It has the Property Type, and a bound use of it causes that location to exist. `value` is available only in a setter. Neither identifier is globally reserved outside its accessor context.

An instance accessor also has an implicit receiver named `self`:

```text
get: self: ref/Self
set: self: uniq/Self, value: PropertyType
```

Conceptually, the accessors have these signatures:

```text
get(self: ref/Self) -> PropertyType
set(self: uniq/Self, value: PropertyType) -> ()
```

A getter consequently has shared, non-exclusive access to the instance. A setter has exclusive mutable access. The receiver controls access to the containing instance; it does not change the Type of `storage` to `ref/T` or `uniq/T`. Static Properties, including members of a `group`, have no instance receiver.

For owned storage, the receiver permits shared/read access from the getter and exclusive/read-write access from the setter:

```text
get with self: ref/Self   -> shared/read access to storage
set with self: uniq/Self  -> exclusive/read-write access to storage
```

All instance getters currently use `self: ref/Self`, and all instance setters use `self: uniq/Self`. Mutable or exclusive getter receivers are not part of the present language.

### Initialization

An initializer initializes owned storage directly and does not invoke the setter:

```kimi
var Age: i32 = -1
    set
        storage = max(value, 0)
```

Here the initial stored value is `-1`; a later assignment of `-10` invokes the setter and stores `0`. A Property initializer is invalid when `HasStorage = false`, because no Property-owned location exists to initialize. A stored Property without a declaration initializer must be initialized according to the containing type's definite-initialization rules before it is read. After initialization, a `let` Property cannot be assigned.

For example, this declaration is invalid because its explicit getter does not refer to `storage`, so its effective representation is computed:

```kimi
var Value: i32 = 10
    get => CalculateValue()
```

### Access control

Property access control has two levels: the Property's access and, optionally, a more restrictive access for an accessor. An accessor inherits the Property's access unless it declares a restriction, and it may never be more accessible than the Property.

An access-restricted bodyless accessor retains its default implementation whether written inline or in the Property body. For example:

```kimi
public var Count: i32 = 0 has get, private set
```

has the effective behavior:

```kimi
public var Count: i32 = 0
    get => storage

    private set
        storage = value
```

The getter is public and the setter is private. The following is invalid because the setter is broader than its Property:

```kimi
private var Value: i32
    public set
```

### Storage, addressability, and result semantics

Owned storage and the value returned by a getter are separate concepts. A computed Property may return a borrowed or reference-like value without acquiring its own storage:

```kimi
var First: ref/T
    get => items[0]@ref
```

Conversely, a stored Property may have custom accessors because any bound `storage` reference is sufficient for `HasStorage = true`:

```kimi
var Balance: i64 = 0
    get
        AuditRead()
        return storage

    set
        storage = normalize(value)
```

A stored Property has an internal addressable location subject to the normal ownership and borrowing rules. Ordinary Property reads and writes still go through its accessors; address formation must not bypass a custom accessor or its access restrictions. A computed Property has no intrinsic location, although its getter may return a reference to storage owned elsewhere.

Indexer declaration syntax and its accessor semantics are specified separately and are not part of this Property model.

## Functions

A function begins with `func`, followed by its Name, optional generic parameters, and a parenthesized parameter list. An optional return Type follows `->`. A function may have an indentation-delimited body.

```kimi
func add(left: i32, right: i32) -> i32
    left + right
```

# Declaration Containers

A **Declaration Container** is a named declaration scope whose body may contain Properties, functions, constraints, or nested Declaration Containers as permitted by its kind. Its body is delimited by indentation.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Properties, functions, and nested Declaration Container declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts Properties and functions in declaration order. Generic parameters, Origins, and type constraints are supported. |
| `enum` | Yes | Body parsing is not implemented. |
| `extension` | No | Its Name identifies the target. Body parsing is not implemented. |
| `contract` | No | Accepts associated-type constraints and Property requirements. |

A `struct` header may contain generic parameters and an Origin list. Constraint declarations precede Properties and functions.

```kimi
struct Container<s/T> origin owner, source
    T is Comparable
    s is reference

    var value: s/T
```

An associated-type constraint in a `contract` begins with `associate`.

```kimi
contract Sequence
    associate Element is Comparable
```

A Property requirement in a `contract` uses `has` to declare its required accessor capabilities, as specified under Inline accessor declarations.

```kimi
contract Sequence
    associate Element is Comparable
    var Count: i32 has get
```

Each source unit has an implicit root `group`. This root dispatches top-level Declaration Container declarations, Properties, and functions. A `rootgroup` declaration starts at that root and accepts a dot-separated Name. For example:

```kimi
rootgroup A.B
    var value = 1
```

creates the nested group path `A.B`. Ordinary `group` bodies accept nested Declaration Container declarations. `struct` bodies do not currently accept nested Declaration Containers.

An `alias` is a top-level declaration of a qualified Name. Nested aliases are invalid.

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

Kimigayo provides a fixed set of primitive Core Types and user-defined named Core Types.

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

#### String Type

`string` is the built-in Core Type for UTF-8 text. Its exact in-memory container and storage layout are implementation-defined.

#### Unit and Never Types

`()` is the Unit type. It has one value and represents the absence of a meaningful result.

Never is the type of an expression that does not complete normally and has no values. `return`, `break`, `continue`, and `yield` expressions have the Never type. An operand supplied to a control-transfer expression determines the value delivered to its target; it does not change the type of the control-transfer expression itself.

### Compound Type Syntax

A named Core Type may be qualified with dots and may have generic arguments.

```kimi
A.B<T, U>
```

Tuple types use parentheses and commas. Function types use `->` between the parameter type and return type.

```kimi
(i32, string)
(i32, string) -> bool
```

### Structures

A `struct` defines a composite value type.

A structure may contain Properties whose Core Types are:

- primitive types,
- other structure types, or
- types qualified with Type Semantics.

For example:

```
struct Point
    var x: f64
    var y: f64

struct Node
    var value: i32
    var next: obj/Node

struct View
    var source: ref/Data
```

The Type Semantics of a Property determines how the referenced or contained value is represented, owned, borrowed, shared, and accessed.

### Index, Range, and Slice

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

## Type Semantics

Type Semantics specify the ownership, borrowing, layout, and safety properties of a typed value.

The qualified syntax is `Semantics/CoreType`. The complete type form adds `from Origin`.

Within a generic declaration, an identifier in the Semantics position denotes a generic Semantics parameter. For example, `s/T` applies the Semantics parameter `s` to the Core Type parameter `T`.

In the syntax below, `T` denotes a Core Type.

| Category      | Semantics    | Syntax         | Layout or Meaning                     |
| ------------- | ------------ | -------------- | ------------------------------------- |
| Value         | Owner        | `T`, `owner/T` | Data layout                           |
| Value Borrow  | SharedRef    | `ref/T`        | Shared borrow of a value              |
| Value Borrow  | ExclusiveRef | `uniq/T`       | Exclusive mutable borrow of a value   |
| Object        | Owner        | `obj/T`        | Metadata + Data                       |
| Object        | Rc           | `rc/T`         | Rc metadata + Metadata + Data         |
| Object        | Arc          | `arc/T`        | Arc metadata + Metadata + Data        |
| Object Borrow | SharedRef    | `objref/T`     | Shared borrow of an object            |
| Object Borrow | ExclusiveRef | `objuniq/T`    | Exclusive mutable borrow of an object |
| Unsafe        | Pointer      | `unsafe/T`     | Unsafe pointer                        |

### Value

`T` and `owner/T` represent a directly owned value with the data layout of `T`.

```
let x: i32
let p: owner/Point
```

`T` is equivalent to `owner/T`.

### Value Borrow

Value borrows provide non-owning access to value data and are subject to lifetime constraints.

#### `ref/T`

`ref/T` is a shared borrowed reference to a value. Multiple shared references may coexist.

```
func read(value: ref/Data)
```

#### `uniq/T`

`uniq/T` is an exclusive mutable borrowed reference to a value. No conflicting reference may coexist.

```
func modify(value: uniq/Data)
```

### Object

Object semantics represent object metadata followed by the data layout of `T`.

#### `obj/T`

`obj/T` is an exclusively owned object.

```
let node: obj/Node
```

#### `rc/T`

`rc/T` is a shared object with non-atomic reference-count metadata. The object remains alive while an owning reference exists.

```
let object: rc/Object
```

#### `arc/T`

`arc/T` is a shared object with atomic reference-count metadata. Atomic ownership management does not guarantee safe concurrent mutation of `T`.

```
let object: arc/Object
```

### Object Borrow

Object borrows provide non-owning access to an object and are subject to lifetime constraints.

#### `objref/T`

`objref/T` is a shared borrowed reference to an object. Multiple shared references may coexist.

```
func readObject(value: objref/Data)
```

#### `objuniq/T`

`objuniq/T` is an exclusive mutable borrowed reference to an object. No conflicting reference may coexist.

```
func modifyObject(value: objuniq/Data)
```

### Unsafe

#### `unsafe/T`

```
unsafe/T
```

`unsafe/T` represents an unsafe pointer to `T`.

Unlike safe ownership and borrowing semantics, `unsafe/T` is not required to satisfy the normal ownership, lifetime, aliasing, or exclusivity guarantees enforced by the language.

Operations involving `unsafe/T` therefore belong to the unsafe portion of the language and place additional correctness responsibilities on the programmer.

```
let pointer: unsafe/i32
```

## Origin-Based Lifetime Management

Kimigayo uses **Origins** instead of lifetime variables. An Origin describes how long a borrow remains valid; a **Loan** records which place is borrowed and whether the borrow is shared or exclusive.

```text
Type    what the value is
Origin  how long a borrow may remain valid
Loan    which place is borrowed, and in which mode
```

Origin annotations appear in signatures and type declarations. Origins inside function bodies are inferred. When an annotation is omitted, conservative elision rules apply.

### Borrow types and Origins

The safe value-borrow semantics are:

```kimi
ref/T from o   // shared, immutable, and aliasable
uniq/T from o  // exclusive and mutable
```

`uniq/T` is not implicitly copyable and cannot coexist with another overlapping borrow. The corresponding object-borrow semantics, `objref/T` and `objuniq/T`, follow the same shared and exclusive rules. This section uses `ref` and `uniq` in examples.

When `from o` is omitted, §3 determines the Origin.

#### Origin expressions

An Origin is the set of program points at which a borrow is guaranteed to be valid.

| Kind     | Examples                | Meaning                                         |
| -------- | ----------------------- | ----------------------------------------------- |
| Concrete | `x`, `self`, `x.source` | Origin supplied by a parameter or receiver      |
| Abstract | `source`, `left`        | Origin parameter declared by a function or type |
| Static   | `static`                | Built-in maximum Origin                         |

The syntax is:

```text
origin-expression := Name
                   | origin-expression '.' Name
                   | static
                   | origin-expression 'and' origin-expression
```

A borrowed parameter used as an Origin denotes the Origin carried by its value, not the lexical scope of the parameter variable:

```kimi
func first(x: ref/T) -> ref/T from x
```

`x.source` denotes the abstract Origin `source` carried by `x`. Qualification is required so that values of the same Origin-bearing type remain distinguishable:

```kimi
func View.get(self: ref/Self) -> ref/T from self.source
```

Local values also have compiler-internal Origins, but these cannot be named in a public signature.

#### Ordering and intersection

```text
o1 : o2
```

means that `o1` outlives `o2`, or equivalently:

```text
region(o1) ⊇ region(o2)
```

The relation is reflexive and transitive. `static` outlives every Origin.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

Consequently, `o1 and o2` never outlives either operand. A result declared `from x and y` is valid only in the region common to both inputs.

#### `static` and `Owned`

```kimi
func empty() -> ref/string from static
```

A shared borrow from `static` has no non-static lifetime dependency. Safe code cannot derive `uniq/T from static` from longevity alone: an exclusive borrow also requires a unique Loan anchor. For the same reason, an abstract Origin whose Loan requirement is `uniq` cannot be bound to `static` in safe code.

`static` describes an Origin; it does not mean that a type contains no non-static borrow. The `Owned` capability expresses that condition:

```kimi
func spawn<F>(f: F)
    where F: Owned
```

A type is `Owned` when every reachable Origin dependency is absent or bound to `static`.

### Abstract Origins

Functions and types may declare abstract Origin parameters separately from type parameters:

```kimi
func unwrap<T> origin s
    (v: View<T> from (source => s))
    -> ref/T from s

struct View<T> origin source
    let value: ref/T from source
```

Function Origins are universally quantified. Origin parameters occupy a namespace distinct from type parameters.

#### Origin arguments

Named Origin arguments use `from (...)` and `=>`:

```kimi
struct Pair<A, B> origin left, right
    let a: ref/A from left
    let b: ref/B from right

Pair<A, B> from (
    left => a,
    right => b)
```

Parentheses are required for a named argument list, including a one-element list. If a type declares exactly one Origin, this shorthand is allowed:

```kimi
View<T> from v
```

It is equivalent to:

```kimi
View<T> from (source => v)
```

#### Variance

The compiler infers Origin variance from all occurrences and solves recursive types to a fixed point. Explicit variance annotations are not allowed.

| Position                 | Origin                   | Core Type         |
| ------------------------ | ------------------------ | ----------------- |
| `ref/T from o`           | Covariant in `o`         | Covariant in `T`  |
| `uniq/T from o`          | Covariant in `o`         | Invariant in `T`  |
| Function parameter       | Reverses polarity        | Contravariant     |
| Function result          | Preserves polarity       | Covariant         |
| Interior-mutable storage | Representation-dependent | Usually invariant |

For an Origin parameter `p` of `S`:

- covariance permits `S from (p => o1) <: S from (p => o2)` when `o1 : o2`;
- contravariance reverses that relation;
- invariance requires equal Origins.

The direct borrow rules are:

```text
o1 : o2
--------------------------------
ref/T from o1 <: ref/T from o2
uniq/T from o1 <: uniq/T from o2
```

`uniq/T` remains invariant in `T`.

#### Loan requirements

Each abstract Origin has an inferred Loan requirement:

```text
none < ref < uniq
```

Using an Origin in `ref/T` requires `ref`; using it in `uniq/T` requires `uniq`. Multiple uses take the stronger requirement, and requirements propagate through nested Origin-bearing types.

```kimi
struct View<T> origin source
    let value: ref/T from source       // loan(source) = ref

struct MutView<T> origin source
    let value: uniq/T from source      // loan(source) = uniq
```

The requirement determines which caller-side Loan must remain active while a returned or stored Origin-bearing value is live. A type carrying an active `uniq` requirement is non-Copy.

### Origin elision and return contracts

When a result Origin is omitted, the compiler applies these rules in order:

1. If the result contains no borrow, no result-Origin constraint is generated.
2. If there are directly borrowed parameters, each omitted result Origin becomes the meet of all their Origins.
3. Otherwise, an omitted shared result Origin is `static`. If that would create an exclusive static borrow, an explicit valid Origin is required.

Examples:

```kimi
func first(x: ref/T) -> ref/T
// result Origin: x

func choose(x: ref/T, y: ref/T) -> ref/T
// result Origin: x and y

func empty() -> ref/string
// result Origin: static
```

Only direct borrowed parameters participate in rule 2. Origins nested in aggregate inputs must be selected explicitly:

```kimi
func get(v: View<T>) -> ref/T from v.source
```

An explicit `from` clause overrides elision. Thus this result depends on `self`, not on the conservative meet `self and key`:

```kimi
func lookup(self: ref/Self, key: ref/Key)
    -> ref/V from self
```

#### Return contracts

A declared return Origin is the maximum dependency visible to callers; it does not require the implementation to borrow from that particular input. Every returned value must be a subtype of the declared result type.

For example, `ref/T from static` may satisfy `ref/T from x` because `static : x`, provided the Origin position is covariant. Invariant positions require equality, while contravariant positions reverse the subtype direction.

`from x and y` is deliberately conservative in two ways:

- the result region is `region(x) ∩ region(y)`;
- Loans for both possible sources remain active while the result is live.

```kimi
let r = choose(a, b)
b.mutate()       // Error: the Loan on b is still active.
use(r)
```

The caller cannot rely on which argument the implementation actually selected. An Origin-bearing result type with distinct Origin parameters can preserve more precision.

### Exclusive Origins

An exclusive borrow requires both a valid Origin and a unique Loan anchor. An Origin proves longevity but not uniqueness.

A shared borrow may be returned from a stored Origin:

```kimi
func View.get(self: ref/Self)
    -> ref/T from self.source
```

Returning `uniq/T from self.source` from `self: uniq/Self` is invalid because detaching the result from the current `self` Loan could allow a second exclusive borrow:

```kimi
func View.bad(self: uniq/Self)
    -> uniq/T from self.source       // Error
```

There are two valid forms.

Consume the Origin-bearing owner:

```kimi
func View.into_uniq(self: Self)
    -> uniq/T from self.source
```

Moving `self` prevents reuse of the capability.

Alternatively, reborrow through the current exclusive receiver:

```kimi
func View.get_uniq(self: uniq/Self)
    -> uniq/T from self
```

The parent Loan remains active, and access through it is suspended, while the returned reborrow is live.

### Borrow checking

Function bodies are lowered to a control-flow graph. A **program point** is a position immediately before or after an operation. A **place** is an assignable location:

```text
place := local
       | place '.' Name
       | '*' place
       | place '[' _ ']'
```

A **region** is a set of program points. Local regions are inferred; Origins in signatures introduce universal regions; `static` is the maximum region.

A Loan is:

```text
Loan = (place, mode, region)
mode = ref | uniq
```

It is active at program point `P` exactly when `P` belongs to its region. Regions follow actual uses rather than lexical scope, providing non-lexical lifetimes:

```kimi
let r = ref/x
use(r)
x.mutate()       // Allowed: r is no longer live.
```

#### Constraints

Type checking generates these constraints:

| Constraint      | Rule                                                         |
| --------------- | ------------------------------------------------------------ |
| Subtyping       | Assignment and argument passing require `type(value) <: type(destination)`. |
| Liveness        | If a value containing `o` may be used after `P`, then `P` belongs to `region(o)`. |
| Outlives        | `where a : b` requires `region(a) ⊇ region(b)`.              |
| Well-formedness | Every Origin in `T` observable through `ref/T from o` or `uniq/T from o` must outlive `o`. |
| Calls           | Origin arguments and result Loan requirements are instantiated as described in §5.4. |

The well-formedness rule prevents borrowed contents from expiring before the outer borrow.

#### Place overlap and conflicts

Two places overlap when an operation on one may affect the other.

| Places                               | Overlap                                          |
| ------------------------------------ | ------------------------------------------------ |
| `x`, `x` or `x.Property`             | Yes                                              |
| `x.a`, `x.b` for distinct Properties | No                                               |
| Two array elements                   | Conservatively yes unless disjointness is proven |
| Dereferences                         | Yes when their Loan provenance may overlap       |
| Unrelated locals                     | No                                               |

Different Properties of a structure may therefore be borrowed exclusively at the same time.

Each operation is checked against every active Loan on an overlapping place:

| Operation               | Existing `ref` | Existing `uniq` |
| ----------------------- | -------------- | --------------- |
| Read                    | Allowed        | Forbidden       |
| Write or move           | Forbidden      | Forbidden       |
| Create `ref`            | Allowed        | Forbidden       |
| Create `uniq`           | Forbidden      | Forbidden       |
| Drop the borrowed place | Forbidden      | Forbidden       |

This enforces shared aliasing or mutation, but never both simultaneously.

#### Reborrowing

Borrowing through an exclusive borrow creates a child Loan. While the child is live, the parent remains live but access through it is suspended. Overlapping access is rejected by the normal conflict rules.

```kimi
func bump(n: uniq/i32)

var v = 0
bump(v@uniq)
bump(v@uniq)
```

Each call creates a temporary reborrow; the first ends before the second starts.

#### Calls and Origin propagation

For a call, the compiler:

1. creates fresh regions for the callee's abstract Origins;
2. instantiates parameter types and checks argument subtyping;
3. applies declared outlives constraints;
4. instantiates the return type;
5. recursively collects its Origin dependencies and Loan requirements;
6. creates the required caller-side Loans and keeps them active for the corresponding result regions.

This applies to direct borrow results and nested aggregate results:

```kimi
func make(a: ref/A, b: ref/B)
    -> Pair<A, B> from (
        left => a,
        right => b)
```

While the returned `Pair` is live, shared Loans on both `a` and `b` remain active. A dependency requiring `uniq` propagates an exclusive Loan. `static` creates no caller-side Loan.

#### Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

```kimi
func bad(x: ref/T) -> ref/T from x
    let local = T.new()
    return ref/local       // Error
```

#### Drop checking

Dropping storage requires an Origin to remain live only when destruction may observe a value carrying that Origin.

```text
DestructorUsePoints(value, origin) ⊆ region(origin)
```

Trivial destruction adds no constraint. Until the language provides a `may_dangle`-style mechanism, a user-defined destructor is conservatively assumed to observe every reachable Origin.

```kimi
struct Logger origin sink
    let out: uniq/Writer from sink

    deinit
        self.out.flush()
```

Here `sink` must remain valid throughout the destructor.

#### Reference algorithm

A conforming borrow checker may proceed as follows:

```text
1. Type-check and generate subtype constraints.
2. Build the control-flow graph and compute liveness.
3. Generate Origin and well-formedness constraints.
4. Instantiate call-site Origins and propagate result Loan requirements.
5. Solve region constraints to a fixed point.
6. Reject local-to-universal region flows.
7. Compute active Loans and check overlap conflicts.
8. Check reborrows and destructor observations.
```

The region and Loan analyses may be implemented using Datalog or an equivalent fixed-point solver.

### Deferred features

This revision does not define:

- Origins on contracts or trait-like abstractions;
- default Origins for trait objects;
- higher-ranked Origins;
- borrow escape into heap or global storage;
- lending iterators;
- destructor dangling relaxation.

These features require extensions to the core rules above and must not be inferred from this revision.

# Control Flow

An indentation-delimited Block is a sequence of declarations and expressions. Unless a construct below specifies a different branch-result rule, a Block completes normally with the value of its final expression. An empty Block, a Block ending in a declaration, or a Block whose final expression is followed by a semicolon completes with Unit. Declarations do not produce values.

Control-transfer expressions complete abruptly. Their type is Never, so a path ending in a control transfer does not constrain the type of an enclosing expression. The operand of `return`, `break`, or `yield` is evaluated before control is transferred.

## Control-boundary hierarchy

Kimigayo has three strengths of control boundary, from strongest to weakest:

```text
function / return
    >
loop / break, continue
    >
value-producing construct / yield
```

### Construct and keyword correspondence

The control-transfer keywords associated with each construct have the following effects:

| Construct | Control-transfer keyword | Effect on the target construct |
| --------- | ------------------------ | ------------------------------ |
| `func` | `return` | Terminates the current function and optionally supplies its result. |
| `for` | `break` / `continue` | `break` terminates the loop. `continue` advances to the next value from the iterable. |
| `while` | `break` / `continue` | `break` terminates the loop. `continue` proceeds to the next condition evaluation. |
| `loop` | `break` / `continue` | `break` terminates the loop and may supply its result when the loop is value-producing. `continue` begins the next iteration. |
| value-producing `if` | `yield` | Terminates the entire target `if` and supplies its result. |
| value-producing `match` | `yield` | Terminates the entire target `match` and supplies its result. |

This table identifies the class of construct targeted by each keyword. The actual target is always the nearest enclosing eligible construct according to the boundary-resolution rules below. In particular, `yield` terminates its target `if` or `match`, not merely the branch or arm containing it.

Each control-transfer operation targets the nearest enclosing boundary of its own class. While searching for that target, it may pass boundaries that are strictly weaker than its own class, but it may not pass a boundary of the same or a stronger class. The matching boundary is the target and is terminated or resumed; it is not crossed.

| Operation  | Value boundary | Loop boundary          | Function boundary |
| ---------- | -------------: | ---------------------: | ----------------: |
| `return`   | Cross          | Cross                  | Target            |
| `break`    | Cross          | Target: terminate      | Cannot cross      |
| `continue` | Cross          | Target: next iteration | Cannot cross      |
| `yield`    | Target         | Cannot cross           | Cannot cross      |

A nested boundary of the same class always becomes the target. Kimigayo has no labelled form that selects a more distant function, loop, or value boundary.

Only the value-producing forms specifically designated by the language establish value boundaries. In this section those forms are value-producing `if` and `match` expressions. An ordinary Block, or an `if` or `match` whose value is discarded, does not establish a value boundary merely because its syntax is nested.

Target resolution is lexical within the executable region governed by each boundary.

## Function boundary and `return`

A function body establishes a function boundary. `return` terminates the nearest enclosing function and optionally supplies its result.

```kimi
func Test() -> i32
    return 1
```

The following rules apply:

1. `return` may pass value and loop boundaries.
2. A nested named function, anonymous function, or closure establishes a new function boundary. A `return` in that body cannot target an outer function.
3. `return expression` requires the operand to be compatible with the function's declared or inferred return type.
4. A bare `return` supplies Unit and is valid only when Unit is compatible with the return type.
5. Normal completion of the function body supplies the body's value. Consequently, a compatible trailing expression is an implicit function result.
6. Every reachable path of a function with a non-Unit result type must either complete the function body with a compatible value, execute a compatible `return`, or end in an expression of type Never.

For example, the early `return` exits both the `if` and the `while`; normal completion returns the trailing `0`.

```kimi
func Find() -> i32
    while hasNext()
        if found()
            return 10

        advance()

    0
```

A nested function intercepts `return` lookup:

```kimi
func Outer() -> i32
    var f = func () -> i32
        return 1

    f()
```

The `return 1` belongs to the anonymous function. `Outer` completes normally with the value of `f()`.

## Loop boundaries, `break`, and `continue`

`for`, `while`, and `loop` bodies establish loop boundaries. `break` terminates the nearest enclosing loop. `continue` terminates the current iteration of the nearest enclosing loop and begins its next iteration according to that loop's iteration rules.

Both operations may pass any number of value boundaries. Neither may pass a function boundary, including a function nested inside a loop.

`continue` never has an operand. A `break` operand is permitted only when its target is a value-producing `loop`; otherwise `break` must be bare.

### `for`

A `for` expression evaluates its iterable once and executes its body once for each value produced by the iterable. A single Name binds each value. A parenthesized, comma-separated binding destructures it. `in` is a contextual keyword and acts as a delimiter only in a `for` header.

```kimi
for value in values
    process(value)

for (key, value) in dictionary
    process(key, value)
```

The value of a `for` expression is Unit, and the value of its body is discarded. A `break` targeting a `for` therefore has no operand. A `continue` discards the remainder of the current body evaluation and requests the next value from the iterable; if the iterable is exhausted, the loop completes with Unit.

### `while`

A `while` expression evaluates its Boolean condition before each iteration and executes its body while the condition is true. Parentheses around the condition are optional.

```kimi
while ready
    process()

while (ready)
    process()
```

The value of a `while` expression is Unit, and the value of its body is discarded. A `break` targeting a `while` therefore has no operand. A `continue` discards the remainder of the current body evaluation and proceeds directly to the next evaluation of the Boolean condition.

### `loop`

`loop` is an unconditional-loop expression. It has no normal fall-through path: it completes only through a control transfer such as `break` or `return`, or it continues indefinitely.

```kimi
var result = loop
    var x = next()

    if x > 10
        break x
```

A `loop` used in a value context is a value-producing loop. The operand of each reachable `break` targeting that loop contributes to the loop's result type. A bare `break` contributes Unit. All contributing values must have a common type under the normal inference and conversion rules. Thus, a reachable bare `break` is invalid when a non-Unit loop result is required.

A `continue` discards the remainder of the current body evaluation and begins the next iteration at the start of the body. It does not contribute a result because it does not terminate the loop. A `return`, or another expression of type Never, also does not constrain the loop's result type. A `loop` with no reachable `break` has type Never.

```kimi
var result = loop
    var x = next()

    if invalid(x)
        break -1

    if found(x)
        break x
```

Both `break` expressions target the same `loop` and produce compatible `i32` values. The nested statement-context `if` expressions do not intercept them.

A function boundary stops loop-target lookup:

```kimi
loop
    var f = func ()
        break // Error: no loop in this function
```

## Value boundaries and `yield`

An `if` or `match` is value-producing when its result is consumed by an initializer, operand, argument, return value, or enclosing result expression. Such a construct establishes a value boundary for its branch or arm bodies. The same syntax in a discard or statement context produces Unit and does not establish a `yield` target.

`yield expression` evaluates its required operand, terminates the nearest enclosing value-producing construct, and supplies the operand as that construct's result. A bare `yield` is invalid; use `yield ()` to supply Unit explicitly.

`yield` may serve as an early exit from its target. It may not cross an intervening loop or function boundary. A value-producing construct nested inside another one intercepts `yield`; a statement-context construct does not.

### Branch result rules

The following rules apply independently to each branch or arm of a value-producing construct:

1. A body containing exactly one top-level expression may complete normally and implicitly supply that expression's value.
2. A body containing multiple top-level elements must not use its trailing expression implicitly. Every normally completing path must execute `yield` for the target construct.
3. A path that exits the current value-producing construct through `return`, or through `break` or `continue` targeting an enclosing loop, does not need to produce a value for the current construct. The same is true of a path that does not complete normally for another reason.
4. A reachable path that reaches the end of a multi-element body without producing a value is invalid.

"Top-level" refers to direct declarations and expressions in that branch or arm. Blank lines and comments are ignored. A nested `if`, `match`, or loop counts as one top-level expression, regardless of how many elements its own body contains.

A transfer caught by a construct nested inside the current branch does not by itself satisfy the current branch. For example, a `yield` caught by a nested value-producing `if`, or a `break` caught by a nested `loop`, produces the result of that nested construct; coverage analysis then continues after that construct.

All explicit and implicit results of one construct participate in the normal type-inference and conversion rules. Conceptually:

```text
result type = join(reachable branch or arm result types)
```

Never has no values and therefore does not constrain this join.

### `if`

An `if` contains one condition and a body, followed by zero or more `else if` branches and at most one `else` body. Parentheses around conditions are optional.

A `yield` targeting a value-producing `if` terminates the entire `if` immediately and supplies the result of that `if`. Evaluation does not continue in the remainder of the current branch or in any later branch.

```kimi
if ready
    process()
else if waiting
    retry()
else
    cancel()
```

Single-expression branches produce their values implicitly:

```kimi
var x = if condition
    1
else
    2
```

A multi-element branch uses `yield` explicitly:

```kimi
var x = if condition
    log("true")
    yield 1
else
    log("false")
    yield 2
```

The following is invalid because the first branch has multiple top-level elements and therefore cannot use `1` as an implicit trailing result:

```kimi
var x = if condition
    log("true")
    1 // Error: explicit yield required
else
    2
```

Every reachable path must be covered:

```kimi
var x = if condition
    if error
        yield -1

    doSomething() // Error: this path reaches the branch end
else
    0
```

An early `yield` makes the intended paths explicit:

```kimi
var x = if condition
    if invalid
        yield -1

    calculate()
    yield 10
else
    0
```

Here the nested `if` is in statement context, so it does not intercept `yield`; both `yield` expressions target the outer value-producing `if`.

A value-producing nested `if` does intercept it:

```kimi
var x = if a
    var y = if b
        yield 1
    else
        yield 2

    yield y + 1
else
    0
```

The first two `yield` expressions target the inner `if`; `yield y + 1` targets the outer `if`.

An omitted `else` is an implicit Unit path when every condition is false. Therefore, an `if` without `else` cannot produce a non-Unit value.

### `match`

A `match` evaluates its subject once, tests its arms in source order, and evaluates the first matching arm. An arm body may be an inline expression or an indentation-delimited Block.

A `yield` targeting a value-producing `match` terminates the entire `match` immediately and supplies the result of that `match`. Evaluation does not continue in the remainder of the current arm or in any later arm.

```kimi
var x = match value
    A => 1
    B => 2
```

Single-expression arms produce their values implicitly. Multi-element arms require `yield` on every normally completing path:

```kimi
var x = match value
    A =>
        log("A")
        yield 1

    B =>
        log("B")
        yield 2
```

A nested statement-context construct does not intercept `yield`:

```kimi
var x = match value
    A =>
        if special
            yield 10

        yield 20

    B => 30
```

Both `yield 10` and `yield 20` target the `match`. If the nested `if` were itself value-producing, it would establish the nearer target instead.

A value-producing `match` must be exhaustive. If the pattern set is not statically exhaustive, the unmatched case is a reachable path that produces no value. All reachable arm results must have a common type; Never-valued arms do not constrain it. Match arms do not fall through to later arms.

### Stronger intervening boundaries

A loop prevents `yield` from targeting a value construct outside that loop:

```kimi
var x = if condition
    loop
        if invalid
            yield -1 // Error: cannot cross the loop boundary
else
    0
```

The loop must produce its own value with `break`:

```kimi
var x = if condition
    loop
        if invalid
            break -1
else
    0
```

The `break` result becomes the value of the `loop`; the single-expression branch then supplies that value to the `if` structurally.

## Control-transfer resolution

Conceptually, target lookup walks outward through the active lexical control contexts.

For `yield`:

```text
value-producing construct -> target
loop                      -> error
function                  -> error
```

For `break` and `continue`:

```text
value-producing construct -> continue lookup
loop                      -> target
function                  -> error
```

For `return`:

```text
value-producing construct -> continue lookup
loop                      -> continue lookup
function                  -> target
```

Reaching the end of lookup without finding a target is an error. In particular, `return` outside a function, `break` or `continue` outside a loop in the current function, and `yield` outside a value-producing construct in the current loop and function are invalid.

## Structural value propagation

Kimigayo favors ordinary expression results over non-local transfer across stronger constructs:

```kimi
var result = if condition
    loop
        var x = next()

        if invalid(x)
            break -1

        if found(x)
            break x
else
    0
```

The value moves outward one structural level at a time:

```text
break value
    -> loop result
    -> if branch result
    -> if result
    -> result
```

`yield` cannot skip the loop and write directly to the `if`. This restriction gives every transfer keyword one target class and keeps non-local control flow bounded by lexical structure.

## Implementation status

The current front end parses `if`, `match`, `for`, `while`, `loop`, `return`, `break`, `continue`, and `yield`, and represents each control-transfer keyword with its own syntax node. Exhaustiveness and common-result-type checking, contextual target validation, and enforcement of target-specific operand restrictions are later semantic-analysis work and are not yet fully implemented.

# Composition

Structures may freely compose primitive types, structures, and Type Semantics where permitted by the rules of the corresponding semantics.

For example:

```
struct Header
    var version: u32
    var flags: u16

struct Buffer
    var length: usize
    var data: obj/Data

struct SharedState
    var state: arc/State

struct Parser
    var input: ref/Input
    var position: usize
```

Type Semantics are orthogonal to the Core Type.

For example, given:

```
struct Data
    var value: i32
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
