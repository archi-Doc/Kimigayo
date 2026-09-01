# Overview

**Kimigayo** is a programming language designed and built from scratch with the goals of being consistent, fast, simple, fun, and safe.

# Build Model

Kimigayo separates workspace orchestration, project configuration, library source, and target-specific compilation into the following model:

| Element | Responsibility |
| ------- | -------------- |
| Solution | Holds multiple Projects and supplies options shared by their builds. It corresponds to a C# solution. |
| Project | Defines one application or library build unit. It corresponds to a C# project and is configured by a `.kimiproj` file. |
| Kotonoha | Defines a named library source unit. It is comparable to a NuGet package boundary and is built from one or more Kimi source files. |
| Compilation | Compiles one Project for one target OS and architecture. |
| CodeContext | Carries the source-unit and diagnostic context used while source is parsed, generated, or inserted into a Koto tree. |

A Solution discovers and loads Projects. A Project contains common build settings, target triples, project-wide aliases, and external Kotonoha dependency descriptors. For each configured target, the Project creates a separate Compilation.

Each Compilation owns the application's or library's primary Kotonoha. A Compilation also provides the target triple, LLVM data layout, pointer width, conditional-compilation variables, and lookup by Kotonoha identifier. External Kotonoha descriptors are copied from the Project configuration; fetching and loading those external libraries is a later compilation stage and is not currently implemented.

A Kotonoha merges declarations from multiple `SourceDocument` instances into one root Koto tree. Tokenization and parsing occur per source document. Executable syntax written directly at the root—fields, statements, expressions, and functions—is stored in an implicit generated function owned by the Kotonoha.

A CodeContext belongs to exactly one Kotonoha. It supplies the active Compilation and diagnostic destination to the Tokenizer and Parser. Generated source may be parsed into a selected Collection in that same Kotonoha; inserting nodes into a Collection owned by another Kotonoha is invalid.

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

A Name is the basic name by which a declaration is written and referred to. The same character rules apply to the names of collections, types, functions, fields, parameters, and other named declarations.

A Name is non-empty and consists of a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`),
- an underscore (`_`), or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`), or Letter Number (`Nl`).

Each continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`), or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`), Connector Punctuation (`Pc`), or Format (`Cf`).

Contextual keywords may be used as Names in contexts that accept contextual identifiers. Reserved keywords may not be used as Names. In particular, `in` acts as a delimiter in a `for` header but may be used as a Name in other supported identifier contexts.

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

# Declarations

## Bindings

Fields and local bindings begin with `let` or `var`. `let` declares an immutable binding, while `var` declares a mutable binding. A Type annotation and an initializer are independently optional.

```kimi
let limit: i32 = 10
var current = 0
```

## Functions

A function begins with `func`, followed by its Name, optional generic parameters, and a parenthesized parameter list. An optional return Type follows `->`. A function may have an indentation-delimited body.

```kimi
func add(left: i32, right: i32) -> i32
    left + right
```

# Collections

A Collection is a named declaration scope. Collection bodies are delimited by indentation, and each collection kind defines which body declarations it accepts.

| Collection kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts fields, functions, and nested Collection declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts fields and functions in declaration order. Generic parameters, Origins, and type constraints are supported. |
| `enum` | Yes | Body parsing is not implemented. |
| `extension` | No | Its Name identifies the target. Body parsing is not implemented. |
| `contract` | No | Currently accepts associated-type constraints only. |

A `struct` header may contain generic parameters and an Origin list. Constraint declarations precede fields and functions.

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

Each source unit has an implicit root `group`. This root dispatches top-level Collection declarations, fields, and functions. A `rootgroup` declaration starts at that root and accepts a dot-separated Name. For example:

```kimi
rootgroup A.B
    var value = 1
```

creates the nested group path `A.B`. Ordinary `group` bodies accept nested Collection declarations. `struct` bodies do not currently accept nested Collections.

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

`string` is the built-in Core Type for text. Its storage representation is implementation-defined.

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

A structure may contain fields whose Core Types are:

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

# Type Semantics

Type Semantics specify the ownership, borrowing, layout, and safety properties of a typed value.

The qualified syntax is `Semantics/CoreType`. The complete type form adds `from Origin`.

Within a generic declaration, an identifier in the Semantics position denotes a generic Semantics parameter. For example, `s/T` applies the Semantics parameter `s` to the Core Type parameter `T`.

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
