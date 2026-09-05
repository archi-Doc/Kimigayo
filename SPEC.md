# Overview

**Kimigayo** is a programming language designed and built from scratch with the goals of being consistent, fast, simple, fun, and safe.

> **Pre-alpha status:** This document defines the intended language. The current implementation mainly covers project loading, target setup, tokenization, parsing, diagnostics, and Koto serialization. Binding, overload and type checking, generic specialization, ownership and Origin analysis, lowering, and code generation are planned unless a section says otherwise.

```kimi
alias Kimi.Base

#if windows
alias Kimi.Windows

public group Program
    public func main(arg: string) -> ()
        var array = [0, 1, 2,]
        var map = [0:"Zero", 1:"One", ]
        return

    func getString<s/T>(value: s/T) -> string
        s is ref or obj
        T is Comparable

        #case s is ref and T is i32
            return "ref/i32"
        #case s is ref
            return "ref"
        #case _
            return "other"
```

**Principles**

- Backward Compatibility: Kimigayo does not guarantee backward compatibility between language versions. To preserve room for future language evolution, and because AI-assisted development has made source migration easier, Kimigayo prioritizes consistency and language quality over compatibility with existing code.
- Indentation: Four spaces are used for indentation. Indentation represents nesting, that is, the syntactic containment relationship between constructs.
- `[]` represents a sequence of elements with the same Type and access to its elements. It is used for array construction and index access.
- `()` represents ordered grouping of values or Types. It is used for function parameter and argument lists, Tuples, Unit, Function Types, grouping conditions, and controlling operator precedence.
- `<>` represents Generic parameters and Generic arguments. It is used for compile-time parameters and arguments that construct Types.
- `{}` is currently unused. It is reserved for future language evolution.
- Type: The complete conceptual form of a Kimigayo Type is `semantics/CoreType from origin`. Type Semantics describe how a value is handled, the Core Type describes what the value is, and the Origin describes where the value derives from and how long it remains valid. Type Semantics and Origin may be omitted when determined by the language or context.
- `=` represents assignment. Under Kimigayo's ownership rules, the effective operation may be either Copy or Move depending on the Type and context. Precise Copy/Move classification, use-after-move checking, and related enforcement are not yet implemented.
- `->` represents a Result Type. In function declarations and Function Types, it denotes the result Type associated with the input side.
- `=>` represents a mapping or correspondence. It introduces function and Property accessor expression bodies, match arms, named Origin arguments, and similar constructs.
- `:` represents a structural association: a Name with a Type, a key with a value, or a Label with a Block or Loop.
- Naming Convention: Types and Declaration Containers use PascalCase. Functions, Properties, local bindings, parameters, and other value names generally use camelCase.
- Compile-time Construct: A construct beginning with `#` is evaluated or processed during compilation. Built-in directives such as `#if` and `#case` use lowercase reserved names and are distinct from PascalCase Attributes such as `#Inline`.

# Build Model

Kimigayo separates workspace orchestration, project configuration, library source, and target-specific compilation into the following model:

| Element | Responsibility |
| ------- | -------------- |
| Solution | Holds multiple Projects and supplies options shared by their builds. |
| Project | Defines one application or library build unit. It is configured by a `.kimiproj` file. |
| Kotonoha | Defines a named library source unit. It is built from one or more Kimi source files. |
| Compilation | Compiles one Project for one target OS and architecture. |
| CodeContext | Carries the source-unit and diagnostic context used while source is parsed, generated, or inserted into a Koto tree. |

A Solution discovers and loads Projects. A Project stores target triples, aliases, and external Kotonoha descriptors, and creates one Compilation for each target.

Project language-version selection is planned but not implemented. For now, every Project uses the compiler's current language version.

Each Compilation owns the primary Kotonoha and provides target information and compile-time variables. Loading external Kotonoha libraries is planned.

A Kotonoha merges declarations from multiple `SourceDocument` instances into one root Koto tree. Tokenization and parsing occur per source document. Executable syntax written directly at the root—bindings, statements, expressions, and functions—is stored in an implicit generated function owned by the Kotonoha.

A CodeContext belongs to one Kotonoha. It supplies the Compilation and diagnostic destination to the Tokenizer and Parser. A node cannot be inserted into a Declaration Container owned by another Kotonoha.

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

After target preparation, the conditional-compilation environment contains `os`, `windows`, `linux`, `macos`, `pointerWidth`, `debug`, and `release`. The OS and build-mode flags are Boolean values; `os` is a string. Unsupported target architectures or targets without an LLVM data layout do not produce a prepared Compilation.

# Compile-time Directives

Compile-time Directives select Syntax during compilation and do not produce runtime control flow. Built-in directives use lowercase reserved names and are distinct from PascalCase Attributes:

| Form | Purpose |
| --- | --- |
| `#if` | Independently includes or excludes one Syntax node. |
| `#case` | Selects one arm from an ordered Case Group. |
| `#Name` | Attaches an Attribute; it is not a Compile-time Directive. |

The former `#If(...)` form is an Attribute. The lowercase `#if` form specified here is a separate language construct.

## Syntax and selection

`#if` controls either the next Syntax node at the same indentation or one indented Block:

```kimi
#if windows
alias Kimi.Windows

#if debug
    let logging = true
    let assertions = true
```

Consecutive `#case` arms at the same indentation form one Case Group. Blank lines and comments do not end the group; any other Syntax node does. Conditions are considered in source order, and the first matching arm is selected. `#case _` matches every remaining case, may occur at most once, and must be the final arm.

```kimi
func useImplementation<T>(value: T) -> ()
    #case windows
        useWindowsImplementation(value)
    #case T is i32
        useIntegerImplementation(value)
    #case _
        useGenericImplementation(value)
```

A Case Group must select an arm in every final evaluation context. The final `#case _` may be omitted when the compiler can prove that the explicit arms are exhaustive. If all Conditions are resolved and no arm matches, compilation fails.

The selected Block occupies the structural position of the Case Group. Normal Block, result-Type, scope, and control-transfer rules apply after selection. An early-false `#if` target is consumed without creating Koto nodes. Unselected `#case` arms do not undergo ordinary Binding, Lowering, or code generation.

## Staged condition evaluation

`#if` and `#case` use the same staged evaluator. The Parser first evaluates Conditions from the prepared compile-time environment, before ordinary Binding. Later Directive Binding resolves remaining Names without binding excluded Syntax.

An evaluation attempt produces exactly one of these results:

| Result | Meaning |
| --- | --- |
| **True** | The Condition is satisfied. |
| **False** | The Condition is not satisfied. |
| **Deferred** | The Condition has a valid compile-time dependency whose value is not yet available. |
| **Error** | The Condition is invalid, non-Boolean, or refers to an unavailable Name. |

After Directive Binding, an unbound declared generic parameter produces **Deferred**, while an unknown Name produces **Error**. `and`, `or`, and `not` use short-circuit reasoning; for example, `false and Deferred` is **False**, while `true and Deferred` is **Deferred**.

The current Parser cannot yet distinguish an unknown Name from a declared generic parameter. It provisionally treats unresolved Names and unsupported expressions as **Deferred**. Later Directive Binding must classify them and report unknown Names.

Each pass attempts the single Condition of a `#if` and every explicit Condition of a Case Group. Every arm Condition is checked, and an **Error** is reported even when an earlier arm determines the selection. A Case Group is selected as soon as its first-match result is certain:

- a **False** arm is skipped;
- a **True** arm is selected when every preceding arm is **False**;
- a preceding **Deferred** arm prevents selection of a later **True** arm or `#case _`;
- Conditions after an already selectable **True** arm cannot change the selection.

For example, `#case windows` may be resolved during parsing. A generic Condition such as `T is i32` remains **Deferred** until `T` is bound. A true arm may be selected immediately when every earlier arm is false; later Deferred arms cannot change that choice.

The evaluation and Syntax-processing sequence is:

```text
Parse a directive Condition
    -> evaluate known target and Project values
        -> True: parse the controlled Syntax without a directive Koto
        -> False: consume the controlled Syntax without creating Koto nodes
        -> Deferred: parse the controlled Syntax and retain a directive Koto
        -> Error: report a diagnostic and discard the controlled Syntax
    -> resolve Names in retained Conditions
    -> re-evaluate after generic Binding and for each specialization
    -> require a final result before finalization
    -> bind and lower only the selected Syntax
```

A still-Deferred Condition is an error when its containing declaration, layout, specialization, or executable body must be finalized. Deferral is valid only when a later compilation phase can provide the missing dependency before that point.

## Conditions and narrowing

A Condition is a Boolean compile-time expression. It may inspect Compilation values, Project settings, generic Core Type parameters, Type Semantics parameters, declared constraints, and other information available in its evaluation environment.

```kimi
windows
windows or linux
os == "windows" or os == "linux"
pointerWidth == 64
s is ref
T is i32
T is Comparable
s is ref and T is Comparable
```

A concrete Type or Type Semantics on the right of `is` tests identity. A contract or named category tests constraint satisfaction.

Within a selected `#case` arm, its Condition and the negation of each preceding arm are available as additional constraints. Narrowing preserves the concrete Core Type; `T is Comparable` does not replace `T` with `Comparable`.

Compile-time Conditions do not evaluate runtime values. The initial design does not destructure values or introduce pattern bindings. For example, `#case value is ref/i32 x` is invalid; use `#case s is ref and T is i32` to narrow a value of Type `s/T` to `ref/i32`.

## Koto representation

The Parser represents directives explicitly rather than evaluating them as Attributes:

```text
CompileTimeIfKoto
    Condition
    Target

CompileTimeCaseGroupKoto
    CompileTimeCaseArmKoto[]
        Condition or fallback
        Block
```

Only a Deferred directive normally needs a directive Koto. An early-true `#if` contributes its Target directly, and an early-false `#if` contributes no Koto. An invalid Case Group may remain as Koto for error recovery. Resolving one specialization must not alter Koto shared by other specializations.

The Parser implements early `#if` and `#case` evaluation and retains Deferred directives as dedicated Koto nodes. Later Binding/specialization evaluation and constraint narrowing are planned.

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

The current code defines these Signature shapes, but duplicate-declaration checks and overload Binding are not implemented.

# Literals

## NumberLiteral

A `NumberLiteral` begins with an ASCII decimal digit. A leading `+` or `-` is an operator and is not part of the literal. The sign characters may occur inside a decimal exponent.

Kimigayo supports decimal integers, binary integers, octal integers, hexadecimal integers, and decimal floating-point literals:

| Form | Prefix or syntax | Digits |
| ---- | ---------------- | ------ |
| Decimal integer | None | `0`-`9` |
| Binary integer | `0b` or `0B` | `0`, `1` |
| Octal integer | `0o` or `0O` | `0`-`7` |
| Hexadecimal integer | `0x` or `0X` | `0`-`9`, `a`-`f`, `A`-`F` |
| Decimal floating point | Decimal fraction, exponent, or both | `0`-`9` |

The lexical grammar is:

```text
number-literal       := decimal-literal
                      | binary-literal
                      | octal-literal
                      | hexadecimal-literal

decimal-literal      := decimal-sequence fraction? exponent?
fraction             := '.' decimal-digit decimal-tail
exponent             := ('e' | 'E') ('+' | '-')? decimal-digit decimal-tail

binary-literal       := '0' ('b' | 'B') binary-tail
octal-literal        := '0' ('o' | 'O') octal-tail
hexadecimal-literal  := '0' ('x' | 'X') hexadecimal-tail

decimal-sequence     := decimal-digit decimal-tail
decimal-tail         := (decimal-digit | '_')*
binary-tail          := (binary-digit | '_')*
octal-tail           := (octal-digit | '_')*
hexadecimal-tail     := (hexadecimal-digit | '_')*

decimal-digit        := '0' .. '9'
binary-digit         := '0' | '1'
octal-digit          := '0' .. '7'
hexadecimal-digit    := decimal-digit | 'a' .. 'f' | 'A' .. 'F'
```

An underscore (`_`) is a digit separator and has no effect on the value. Consecutive separators are permitted, as are separators immediately after a base prefix and at the end of a digit sequence. Consequently, `1__000`, `123_`, `0x_FF`, and `0b__101__` are valid. A base prefix followed by no digits, or only separators, has the integer value zero; for example, `0x` and `0o___` are valid zero literals. The one stricter position is the start of an exponent: a decimal digit must immediately follow `e` or `E` and its optional sign, so `1e_2` and `1e+_2` are invalid.

A decimal point belongs to a `NumberLiteral` only when it is immediately followed by a decimal digit. Thus `1.0` is a floating-point literal, but `1.` is the integer literal `1` followed by a dot token. Fractions and exponents are supported only for decimal literals; `0xFF.0`, for example, begins with the hexadecimal integer literal `0xFF` rather than forming a hexadecimal floating-point literal.

A decimal literal containing a recognized fraction or exponent is interpreted as an IEEE 754 `f64` value. Separators are removed before conversion. A finite result is valid; a value that converts to positive or negative infinity is invalid. A decimal literal containing neither a fraction nor an exponent, and every base-prefixed literal, is an integer. Integer magnitudes from zero through `2^128 - 1` are accepted and stored as a 128-bit bit pattern; a larger magnitude is invalid.

`NumberLiteral` currently has no type suffix. Internally, integer literals are retained as `i128` and floating-point literals as `f64`; their resulting Types are inferred appropriately from context. To specify a Type, use an explicit conversion expression, such as `123@i32`.

The parsed syntax tree stores a canonical representation rather than the original spelling. Integer literals are rendered as decimal from their signed 128-bit bit pattern. Floating-point literals are rendered with a round-trip `f64` representation and retain a decimal marker when necessary; for example, an integral floating-point value is rendered as `1.0`. Compile-time basic-value evaluation currently supports integer representations that fit in `i64` and all valid `f64` literals.

## StringLiteral

A `StringLiteral` produces a value of the built-in `string` Type. Kimigayo source text and string contents use UTF-8. A literal may occupy one line or multiple lines.

There are two forms, distinguished by the number of double quotation marks in their delimiters:

| Form | Delimiter | Backslash escapes | Interpolation |
| ---- | --------- | ----------------- | ------------- |
| Escaped string (*Multi-line string with escape sequences*) | One double quotation mark (`"`) on each side | Yes | Yes |
| Raw string (*Multi-line string without escape sequences*) | The same number of double quotation marks, at least three, on each side | No | No |

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

The current front end parses escaped and raw strings. String interpolation is specified above but is not yet accepted as a complete expression.

# Declarations

## Bindings

Properties and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` declares a mutable binding. A Type annotation and an initializer are independently optional when the omitted information can be inferred.

```kimi
let limit: i32 = 10
var current = 0
```

## Properties

Kimigayo has exactly one kind of value-bearing member: the **Property**. A compiler may lower Property storage to a storage slot, global storage, or another layout entity, but none of these implementation representations constitutes another member kind. A `let` or `var` declared inside an executable Block is a local binding, not a Property.

The current Parser records Properties, inline and block accessors, and basic syntax errors. Accessor expansion, contextual binding of `self`, `storage`, and `value`, `HasStorage`, access checks, initialization checks, and accessor type checking are planned semantic work.

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
var count: i32
```

has the effective representation:

```kimi
var count: i32
    get => storage

    set
        storage = value
```

Likewise, an explicit setter does not suppress the default getter:

```kimi
var age: i32 = 0
    set
        storage = max(value, 0)
```

is equivalent for classification to:

```kimi
var age: i32 = 0
    get => storage

    set
        storage = max(value, 0)
```

By contrast, an explicit getter suppresses the default accessors not written with it. Therefore this is a read-only computed Property:

```kimi
var count: i32
    get => items.count
```

It contains no reference to `storage`, so `HasStorage = false`.

`let` is reserved for immutable stored data. Its standard effective representation has the default storage-reading getter and no setter, and it cannot be used for a computed Property. A read-only computed Property uses `var` with an explicit getter.

### Accessors

A getter defines a Property read. It follows the [function body and result rules](#function-bodies-and-results), with its result Type defined under [Function Boundaries](#function-boundaries). It may be expression-bodied or Block-bodied:

```kimi
var area: f64
    get => width * height

var loggedArea: f64
    get
        logRead()
        return width * height
```

A computed getter must be introduced explicitly with `get`; a bare expression in the Property body is invalid.

A setter defines a Property write. Within it, `value` is the incoming value and has the Property Type:

```kimi
var percentage: i32 = 0
    set
        storage = clamp(value, 0, 100)
```

The setter does not declare this parameter in source. For example, evaluating:

```kimi
obj.percentage = 120
```

invokes the setter with `value` bound to `120`; the source form is `set`, not `set value`.

Reading a Property invokes its getter. Assignment after initialization invokes its setter; assigning to a Property without a setter is invalid. An accessor body may refer to other state instead of owned storage, so custom accessors do not by themselves make a Property computed or stored:

```kimi
var width: f64
    get => right - left

    set
        right = left + value
```

Neither accessor refers to `storage`, so this is a read-write computed Property.

### Inline accessor declarations

A Property may declare bodyless accessors inline with a `has` clause:

```kimi
var count: i32 has get, private set
```

The clause follows the Property initializer when one is present:

```kimi
var count: i32 = 0 has get, private set
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
var count: i32 has get, private set
```

is equivalent to:

```kimi
var count: i32
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
var count: i32
    get => storage

    private set
        storage = value
```

`has` does not introduce a separate storage rule. After expansion, `HasStorage` is determined from references bound to `storage` in the normal way. For example, `var value: i32 has get` expands to `get => storage` with no setter and is therefore a stored, read-only Property.

Inline and indentation-delimited accessor lists cannot be combined in one Property declaration. An accessor that needs a custom body must use the indentation-delimited form:

```kimi
var percentage: i32
    get => storage

    private set
        storage = clamp(value, 0, 100)
```

The normal `let` restrictions continue to apply; in particular, a `let` Property cannot declare a setter.

#### Contract Property Requirements

Inside a `contract`, `has` declares the accessor capabilities that a conforming Property must provide:

```kimi
contract Collection
    var count: i32 has get

contract MutableCollection
    var count: i32 has get, set
```

The first requirement is readable; the second is both readable and writable. A conforming Property must have a compatible Type and provide every required accessor with sufficient accessibility.

In this context, `has` introduces no accessor implementation, effective storage representation, or Property storage. A contract Property requirement therefore has no `HasStorage` classification. A conforming Property may be stored or computed:

```kimi
// Stored implementation
var count: i32 has get

// Computed implementation
var count: i32
    get => items.count
```

Both may satisfy `var count: i32 has get` in a contract. Thus the declaration context determines the meaning of the shared syntax:

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
var age: i32 = -1
    set
        storage = max(value, 0)
```

Here the initial stored value is `-1`; a later assignment of `-10` invokes the setter and stores `0`. A Property initializer is invalid when `HasStorage = false`, because no Property-owned location exists to initialize. A stored Property without a declaration initializer must be initialized according to the containing type's definite-initialization rules before it is read. After initialization, a `let` Property cannot be assigned.

For example, this declaration is invalid because its explicit getter does not refer to `storage`, so its effective representation is computed:

```kimi
var value: i32 = 10
    get => calculateValue()
```

### Access control

Property access control has two levels: the Property's access and, optionally, a more restrictive access for an accessor. An accessor inherits the Property's access unless it declares a restriction, and it may never be more accessible than the Property.

An access-restricted bodyless accessor retains its default implementation whether written inline or in the Property body. For example:

```kimi
public var count: i32 = 0 has get, private set
```

has the effective behavior:

```kimi
public var count: i32 = 0
    get => storage

    private set
        storage = value
```

The getter is public and the setter is private. The following is invalid because the setter is broader than its Property:

```kimi
private var value: i32
    public set
```

### Storage, addressability, and result semantics

Owned storage and the value returned by a getter are separate concepts. A computed Property may return a borrowed or reference-like value without acquiring its own storage:

```kimi
var first: ref/T
    get => items[0]@ref
```

Conversely, a stored Property may have custom accessors because any bound `storage` reference is sufficient for `HasStorage = true`:

```kimi
var balance: i64 = 0
    get
        auditRead()
        return storage

    set
        storage = normalize(value)
```

A stored Property has an internal addressable location subject to the normal ownership and borrowing rules. Ordinary Property reads and writes still go through its accessors; address formation must not bypass a custom accessor or its access restrictions. A computed Property has no intrinsic location, although its getter may return a reference to storage owned elsewhere.

Indexer declaration syntax and its accessor semantics are specified separately and are not part of this Property model.

## Functions

A function begins with `func`, followed by its Name, optional generic parameters, optional Origin parameters, and a parenthesized parameter list. An optional result Type follows `->`. A definition has an indentation-delimited Block body or a single expression introduced by `=>`.

### Function bodies and results

A **Block-bodied function** requires an explicit `return` to supply a non-Unit result. Every direct body expression, including the last, is in statement context; its value is discarded, with or without a trailing semicolon. Nested value contexts, such as initializers, retain their usual meaning.

```kimi
func add(left: i32, right: i32) -> i32
    return left + right

func invalidAdd(left: i32, right: i32) -> i32
    left + right // Error: return is required.
```

Reachable body fall-through contributes Unit to result inference. For a Unit function, it is equivalent to `return ()`; for a declared or inferred non-Unit result, it is an error. Paths that never complete do not need a result.

```kimi
func process()
    prepare()
    execute() // Its value is discarded; process returns Unit.

func find() -> i32
    if found()
        return 10

    return 0
```

A final `if`, `match`, or `loop` is also in statement context and is not an implicit function result. Use `return if ...`, `return match ...`, `return loop ...`, or explicit `return` on the appropriate paths.

An **Expression-bodied function** evaluates the expression after `=>` in value context and uses its normal result as the function result. A `return` executed inside that expression may also supply the function result.

```kimi
func add(left: i32, right: i32) -> i32 => left + right
```

Both forms follow the shared [result validation](#result-validation), [reachability](#reachability), and [scope-exit destruction](#scope-exit-destruction) rules. [Function Boundaries](#function-boundaries) lists the other bodies to which these rules apply.

### Generic constraints

A generic Block-bodied function may begin its body with constraint declarations. Constraint declarations must precede every executable body item and are processed at compile time; they are not executable expressions.

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

The left operand of a function constraint must name one of the function's generic parameters. A Core Type parameter may be constrained by contracts or other compile-time type capabilities. A Type Semantics parameter may be constrained by concrete semantics such as `ref` and `obj`, or by a named semantics category. `and`, `or`, `not`, and parentheses combine constraint requirements.

At a call site, every explicit or inferred generic argument must satisfy its corresponding constraints. Within the function body, those constraints are available during type checking and compile-time specialization. Function constraints are not part of the function Signature; two declarations that differ only in constraints therefore conflict.

The current Parser reads these constraint lines as ordinary body expressions. Separating them from executable statements and enforcing them during Binding and specialization are planned.

# Declaration Containers

A **Declaration Container** is a named declaration scope whose body may contain Properties, functions, constraints, or nested Declaration Containers as permitted by its kind. Its body is delimited by indentation.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Properties, functions, and nested Declaration Container declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts Properties and functions in declaration order. Generic parameters, Origins, and type constraints are supported. |
| `enum` | Yes | Body parsing is not implemented. |
| `extension` | No | Its Name identifies the target. Body parsing is not implemented. |
| `contract` | No | Specifies associated-type constraints and Property requirements. The current Parser accepts only associated-type constraints. |

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
    var count: i32 has get
```

Each source unit has an implicit root `group`. Named Declaration Containers are stored there. Top-level executable syntax, including `let`, `var`, expressions, and functions, is stored in an implicit generated function. A `rootgroup` declaration starts at the root and accepts a dot-separated Name. For example:

```kimi
rootgroup A.B
    var value = 1
```

creates the nested group path `A.B`. Ordinary `group` bodies accept nested Declaration Container declarations. `struct` bodies do not currently accept nested Declaration Containers.

An `alias` is a top-level declaration of a qualified Name. Nested aliases are invalid.

# Type

> Types are everything for programming languages; words are everything in design.

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

The current front end parses much of this Type syntax. Type resolution, layout validation, subtyping, ownership rules, and most Type semantics are not implemented.

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

Never is the type of an expression that does not complete normally and has no values. `return`, `exit`, `continue`, and `yield` expressions have the Never type. Their operands supply results to their targets without changing the types of the transfer expressions themselves. See [result validation](#result-validation) for constructs with no result-producing paths.

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

The Type Semantics of a Property determine how the referenced or contained value is represented, owned, borrowed, shared, and accessed.

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

The current Parser supports Origin lists on structures and simple `from Name` annotations. Function Origin parameters, qualified and intersected Origin expressions, named Origin arguments, inference, and borrow checking are not implemented.

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

When `from o` is omitted, the Origin elision rules below determine the Origin.

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
    F is Owned
```

A type is `Owned` when every reachable Origin dependency is absent or bound to `static`.

### Abstract Origins

Functions and types may declare abstract Origin parameters separately from type parameters:

```kimi
func unwrap<T> origin s(v: View<T> from (source => s))
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

A declared return Origin is the maximum dependency visible to callers; it does not require the implementation to borrow from that particular input. Each reachable function result must be a subtype of the declared result type, subject to the shared [result validation](#result-validation) and [reachability](#reachability) rules.

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
| Outlives        | `a : b` requires `region(a) ⊇ region(b)`.                    |
| Well-formedness | Every Origin in `T` observable through `ref/T from o` or `uniq/T from o` must outlive `o`. |
| Calls           | Origin arguments and result Loan requirements are instantiated as described under Calls and Origin propagation. |

The well-formedness rule prevents borrowed contents from expiring before the outer borrow.

#### Place overlap and conflicts

Two places overlap when an operation on one may affect the other.

| Places                               | Overlap                                          |
| ------------------------------------ | ------------------------------------------------ |
| `x`, `x` or `x.property`             | Yes                                              |
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

The [scope-exit destruction rules](#scope-exit-destruction) determine which values are destroyed and in what order. At each destruction point, an Origin must remain live only when destruction may observe a value carrying that Origin.

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

Each control-transfer keyword has a specific target and role:

```text
Function Boundary
    return              End the current function and supply its result.

Labeled Block
    exit from Label     End the named Block.

for / while / loop
    exit                End the nearest Loop.
    continue            Start its next iteration.

value-producing if / match
    yield               End the construct and supply its result.
```

An unlabeled `exit` targets the nearest Loop, skipping Labeled Blocks. `exit from Label` targets the named enclosing Block or Loop. Only a value-producing `loop` accepts an `exit` operand. The language uses `exit` for loop termination; `break` is not used.

```kimi
func calculate() -> i32
    work:
        if skipPreparation()
            exit from work      // Continue after work.

        prepare()

    for value in values()
        if shouldSkip(value)
            continue            // Request the next value.

        if shouldStop(value)
            exit                // Continue after the for.

        process(value)

    let result = if ready()
        prepareResult()
        yield 1                 // Supply this if's result.
    else
        0                       // Implicit single-expression result.

    return result               // Supply the function's result.
```

## Blocks and evaluation contexts

**Normal completion** means that a construct produces a result and returns control to its evaluator. This includes catching a transfer directed at that construct: an `exit` does not complete normally, but its target Loop may complete normally through that `exit`.

A **Block** is an indentation-delimited sequence of declarations and expressions evaluated in order. An ordinary Block discards expression values, including the last, and produces Unit on reaching its end. Empty Blocks and Blocks ending in a declaration behave the same way. Nesting an ordinary Block adds no control-transfer target. Constructs with their own result rules, such as value-producing branches, apply those rules instead. Function bodies follow [Functions](#function-bodies-and-results).

A **Labeled Block** produces Unit when it reaches its end or catches an `exit from Label` directed at itself. It discards its trailing expression and never accepts an `exit` operand, including `()`. Paths that leave for an outer target or never finish produce no result for that Block.

A **value context** is a syntactic position that uses an expression's value: an initializer, operand, argument, condition, `match` subject, `return` / `exit` / `yield` operand, function expression after `=>`, or implicit single-expression branch result. It remains a value context even when the expected Type is Unit or the result is unused. Reachability, constant evaluation, and optimization do not change it.

A **statement context** evaluates an expression and discards its value. Direct expressions in ordinary Blocks, Labeled Blocks, Loop bodies, and Block-bodied functions use this context, including the final expression. Nested value contexts remain intact.

An `if` or `match` is value-producing only in value context. In statement context, it produces Unit on normal completion and cannot be a `yield` target. A `loop` accepts result operands only in value context.

## Labels

Labels may be attached to Blocks, `for`, `while`, and `loop`:

```kimi
work:
    process()

outer: for value in values
    process(value)

retry: while condition
    process()

search: loop
    process()
```

A Labeled Block places its indented body after `Label:` on the next line. A labeled Loop uses `Label: Loop`. Labels do not change a Loop's result rules; a labeled `loop` may appear in value context:

```kimi
var result = outer: loop
    for value in values
        if found(value)
            exit value from outer
```

Labels follow the character rules for [Names](#name) and have a namespace separate from those of variables and Types. Labels with the same Name and overlapping scopes in one function are invalid.

A Label is visible only inside its construct's body, excluding its `for` iterable or `while` condition. It may identify only an enclosing construct in the same Function Boundary. Sibling, inner, and other-function Labels are inaccessible. A Label names a construct, not an instruction address: jumping into a body or back to a completed construct is not supported.

## Control transfers

### Syntax and operands

```text
return [expression]
exit [expression] [from Label]
continue [Label]
yield expression
```

Brackets indicate optional syntax. `exit name` uses `name` as a result expression; only `exit from name` identifies a Label. The Name after `continue` is always a Label, never a result expression.

| Operation and target | Result operand |
| --- | --- |
| `return` to a function | Optional; omission supplies Unit. |
| `exit` to a Labeled Block, `for`, or `while` | Forbidden; the target completes with Unit. |
| `exit` to a value-context `loop` | Optional; omission supplies Unit. |
| `exit` to a statement-context `loop` | Forbidden; the target completes with Unit. |
| `continue` to a Loop | Forbidden. |
| `yield` to a value-producing `if` or `match` | Required; use `yield ()` for Unit. |

Operands are evaluated before transfer. If operand evaluation leaves by another transfer or never completes, the original transfer does not occur. Otherwise, its result is secured by Copy or Move before [scope-exit destruction](#scope-exit-destruction) and delivery to the target. Each transfer expression itself has type [Never](#unit-and-never-types).

### Target lookup

Resolve targets by walking outward through lexical containment. Resolve the target first, then check operand presence and Type; an unsuitable operand never causes lookup to skip a target.

| Operation | Target without a Label | Named target | Stop before finding a target |
| --- | --- | --- | --- |
| `return` | Nearest Function Boundary | Not allowed | Error if none exists. |
| `exit` | Nearest Loop | Enclosing Labeled Block or Loop named by `from Label` | Error at a Function Boundary. |
| `continue` | Nearest Loop | Enclosing Loop named by `Label` | Error at a Function Boundary. |
| `yield` | First enclosing `if` / `match`, which must be value-producing | Not allowed | Error at a statement-context `if` / `match`, Loop, or Function Boundary. |

Failure to find a target is an error. A named target must be of the required kind; `continue work` is invalid if `work` names a Block.

A construct acts as a target or lookup stop only inside its body. Its own condition, iterable expression, or `match` subject does not acquire that construct's boundary.

Ordinary Blocks never stop lookup. A Labeled Block is an `exit` target only when explicitly named; otherwise it is transparent to every transfer. An `if` / `match` never stops `return`, `exit`, or `continue` lookup. For `yield`, the first encountered `if` / `match` either accepts it in value context or causes an error in statement context; lookup never retries at an outer value-producing construct.

Named `exit` and `continue` may cross intervening Loops and Blocks within the same function. No transfer searches beyond a Function Boundary.

```kimi
var result = loop
    for value in values
        exit 10 // Error: the nearest Loop is for, which forbids an operand.
```

### Function Boundaries

Each of these bodies establishes an independent **Function Boundary**:

- Named functions, including methods and nested functions.
- Anonymous functions and closures.
- Property getters and setters.
- Destructors (`deinit`).

In these control-flow rules, "function" includes all of these bodies. A `return` ends only its own function. Other transfers cannot target an outer function's Labels, Loops, or value-producing constructs.

A getter's result Type is the Property Type; setters and `deinit` return Unit. Each body follows the [function body and result rules](#function-bodies-and-results). Normal completion of `deinit`, including through `return`, still performs any automatic field destruction required by the Type's destruction rules.

```kimi
func outer() -> i32
    let f = func () -> i32
        return 1                // Returns from f only.

    return f()
```

### Label and nesting examples

Adding a Labeled Block does not change the target of an unlabeled `exit` or `continue`:

```kimi
while running
    work:
        if failed()
            exit                // Ends while; advance() is skipped.

        process()

    advance()
```

At the same position, `exit from work` ends only `work` and proceeds to `advance()`. `continue` reevaluates the `while` condition. A result operand also skips the Block:

```kimi
var result = loop
    work:
        exit 10                 // Supplies 10 to loop, not work.
```

A Label selects an outer Loop explicitly:

```kimi
outer: for x in xs
    for y in ys
        if skipX(x, y)
            continue outer

        if found(x, y)
            exit from outer

        process(x, y)
```

## Loops

### `for` and `while`

`for` evaluates its iterable once and executes its body for each supplied value. A single Name binds the value; a parenthesized, comma-separated binding destructures it. `while` evaluates a Boolean condition before each iteration and executes its body while that condition is true. Condition parentheses are optional.

```kimi
for (key, value) in dictionary
    process(key, value)

while ready
    process()
```

Both constructs discard body results and produce Unit on completion. Neither accepts an `exit` operand.

| Event | `for` | `while` |
| --- | --- | --- |
| Body end or self-targeted `continue` | Request the next value; finish if exhausted. | Reevaluate the condition; finish if false. |
| Self-targeted `exit` | End the Loop. | End the Loop. |

### `loop`

`loop` repeats unconditionally. It discards body values and starts the next iteration at the beginning of the body after body fall-through or a self-targeted `continue`.

Only an `exit` targeting that `loop` supplies its normal result. `return`, exits to outer constructs, and exits caught by inner constructs supply no result to it. A self-targeted operandless `exit` supplies Unit. Result operands are permitted only in value context.

```kimi
var result = loop
    let value = next()

    if invalid(value)
        exit -1

    if found(value)
        exit value
```

Only these self-targeted exits contribute to this loop's result Type. Nested statement-context `if` expressions do not intercept `exit`.

```kimi
outer: loop
    loop
        exit from outer
```

The inner `loop` has no result-producing path and has type Never. The outer `loop` completes with Unit. See [result validation](#result-validation) for the common rules.

## `if`, `match`, and `yield`

### Branch results

Each branch of a value-producing `if` and each arm of a value-producing `match` follows these rules:

| Direct body contents | Result rule |
| --- | --- |
| One expression | Evaluate it in value context and implicitly supply its normal result. |
| One expression with a trailing semicolon | Evaluate it in statement context and supply Unit on normal completion. |
| Empty body or one declaration | Supply Unit on normal completion. |
| Multiple declarations or expressions | Evaluate direct expressions in statement context; supply the construct's result explicitly with `yield`. No implicit trailing result. |

Count only declarations and expressions directly in the body. Comments and blank lines do not count. A nested construct counts as one expression regardless of its contents.

A path leaving for an outer target or never finishing needs no branch result. A transfer caught by an inner construct does not satisfy the outer branch: analysis continues after that construct. [Result validation](#result-validation) checks coverage and Types.

### `if`

`if` tests Boolean conditions and executes the selected branch. It may have subsequent `else if` branches and one final `else`. Condition parentheses are optional.

```kimi
var value = if condition
    log("true")
    yield 1
else
    2
```

`yield` ends the whole target `if`, skipping the rest of its branch. Without `else`, the path on which all conditions are false supplies Unit; that path participates in result checking when reachable.

### `match`

`match` evaluates its subject once, tests arms in source order, and executes the first matching arm. An arm has a single expression or an indented body. There is no fall-through to another arm.

```kimi
var result = match value
    A =>
        prepare()
        yield 1

    B => 2
```

`yield` ends the whole target `match`. A value-producing `match` must be exhaustive; this example assumes `A` and `B` cover every case. In statement context, an unmatched subject executes no arm and the `match` completes with Unit.

### Nested `yield` targets

A Labeled Block does not stop `yield` lookup:

```kimi
var result = if condition
    work:
        yield 10                // Supplies the outer if's result, not work's.
else
    20
```

A single-expression branch passes value context to a nested `if`:

```kimi
var result = if a
    if b
        yield 1
    else
        yield 2
else
    0
```

Both yields target the inner `if`, whose result becomes the outer branch's result. Adding a direct statement places the inner `if` in statement context and makes those yields invalid:

```kimi
var result = if a
    if b
        yield 1 // Error: the inner if is in statement context.
    else
        yield 2 // Error: the inner if is in statement context.

    log("done")
else
    0
```

The yields never retarget the outer `if`, even if the added statement is unreachable. Use an initializer to preserve the inner value context and an explicit outer `yield`:

```kimi
var result = if a
    let inner = if b
        yield 1
    else
        yield 2

    log("done")
    yield inner
else
    0
```

Likewise, a conditional early `yield` inside a statement-context `if` is invalid. Put the conditional in the result operand instead:

```kimi
var result = if a
    prepare()
    yield if invalid()
        -1
    else
        calculate()
else
    0
```

`yield` also cannot cross a Loop. A direct `yield -1` in the following Loop body would be an error; `exit` supplies the Loop's result, which the single-expression branch passes to the outer `if`:

```kimi
var result = if condition
    loop
        exit -1
else
    0
```

If that branch gains more direct elements, it must explicitly supply its result with `yield` under the branch rules.

## Result validation

A transfer supplies a result only to its resolved target. Function results follow [Functions](#function-bodies-and-results); Blocks, Loops, and branches use their result sources defined above.

Validate results in this order:

1. Determine syntactic contexts, resolve transfer targets, and check whether operands are required or forbidden.
2. Apply [reachability](#reachability) analysis to result sources and paths leaving each construct. A transfer whose operand cannot complete supplies no result to its original target.
3. Reject any reachable path that reaches an end requiring a result without supplying one. Where a construct implicitly supplies Unit, include that Unit as a candidate. A non-Unit Block-bodied function may not fall through.
4. Combine reachable explicit and implicit results using normal type-inference and conversion rules. Each result must also satisfy any required result Type.

Paths that leave a construct for an outer target or never complete supply no result candidate for that construct. Transfers caught internally may let evaluation continue and must be followed to their continuation.

A `loop` or value-producing `if` / `match` with no path completing with its own result has type Never. Missing required results are errors, not a reason to infer Never. A function with no result-producing paths retains its declared result Type; if its result Type is inferred and there are no candidates, infer Never.

```kimi
func choose(flag: bool) -> i32
    let result = if flag
        return 1
    else
        return 2
```

The `if` has type Never: neither `result` initialization nor function-body fall-through occurs. Both returns supply integer function results. In contrast, `yield` itself has type Never but supplies a result to its target `if` / `match`.

### Reachability

Reachability is determined statically within each Function Boundary. Treat a path as reachable unless the following analysis proves otherwise. Optimization settings must not change type-checking results.

- Follow evaluation order, branches, loops, and resolved transfers. A statically non-completing expression has no edge to the next sequential element.
- Follow the continuation of a construct that catches a transfer, such as the code after a Labeled Block ended by `exit`, or an expression consuming a yielded result.
- Prune condition outcomes only for Boolean literals `true` and `false`, optionally parenthesized, in `if`, `else if`, and `while`. Otherwise, consider both outcomes when condition evaluation completes normally.
- Do not prune additional paths through constant propagation, analysis of called function bodies, or general constant folding.
- Do not prune `for` paths using iterable values or `match` arms using constant subjects. Analyze each arm; pattern exhaustiveness determines whether an unmatched path exists.

Unreachable code is still checked for syntax, Names, transfer targets, operand presence, and the validity of operations inside expressions. Unreachable transfer results are excluded from target result-type combination, target-type compatibility, and result coverage, even when the target has a declared result Type.

```kimi
var result = loop
    if false
        exit                    // Unreachable Unit is not a result candidate.

    exit 1                      // The only result candidate is an integer.

func f() -> i32
    if false
        return "text"           // Allowed: excluded from target compatibility.

    return 1
```

The string expression is valid by itself and is not compared with `i32`. Undefined Names or Labels, invalid operand operations, and value-bearing exits targeting `for` remain errors in unreachable code.

## Scope-exit destruction

When `return`, `exit`, `continue`, or `yield` leaves lexical scopes, destroy each initialized owned value for which a departing scope still has destruction responsibility. Skip moved, uninitialized, and already destroyed values. Responsibility is independent of future-use liveness: a value whose last use has passed must still be destroyed if its scope retains responsibility.

Ownership, temporary-lifetime, and construct-lifetime rules determine each value's owning scope and destruction point. These rules also govern temporaries, `for` iterables and iterators, iteration bindings, `match` subjects, and owned function parameters. A transfer uses those scopes to determine what it leaves.

For a transfer with a result operand:

1. Evaluate the operand.
2. Secure the result using normal Copy / Move rules.
3. Destroy values in departing scopes.
4. Deliver the result and complete the target's termination or continuation.

Without an operand, omit the first two steps. A moved result is not destroyed again at its source; a copied result leaves the source's destruction responsibility intact. Implicit results from `=>` bodies and value-producing branches are also secured before scope destruction.

The original transfer completes only if all required destruction completes normally. Nonterminating destruction prevents completion even when a result has been secured. If exceptions or abnormal termination are provided, common abnormal-exit rules govern remaining destruction and the secured result.

Destroy departing scopes from inner to outer. Within one scope, process local bindings in reverse declaration order, skipping values without remaining responsibility. Later initialization does not change that order. The same ordering applies to ordinary scope completion.

Destroy only values in scopes actually left. A `continue` leaves the current iteration's departing scopes but preserves values in scopes retained for the target Loop's continuation. Named transfers apply the same rules to every intervening scope they leave.

Normal ownership, borrowing, and [Drop checking](#drop-checking) apply at every destruction point. Securing a result first does not permit a borrow of a destroyed local to escape its valid lifetime. If partial initialization or partial Move is permitted by the ownership rules, destroy the parts with remaining responsibility under those rules; do not simply exclude the whole aggregate.
