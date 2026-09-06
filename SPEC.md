# Overview

**Kimigayo** is a programming language designed and built from scratch with the goals of being consistent, fast, simple, fun, and safe.

> **Pre-alpha status:** This document defines the intended language. The current implementation mainly covers project loading, target setup, tokenization, parsing, and diagnostics. Binding, overload and type checking, generic specialization, ownership and Origin analysis, lowering, and code generation are planned unless a section says otherwise. Unsafe functions, Unsafe and Deferred Blocks, and value-producing Labeled Blocks specified here are planned extensions.

Serialization uses SourceCode or binary artifacts, not Koto serialization. SourceCode preserves declarations for reconstruction; binary interfaces must preserve information needed by callers, including whether a function is unsafe. Artifact formats are specified separately.

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
- `=` represents assignment. Values are transferred according to the [Copy and Move](#copy-and-move) rules. Copy/Move checking and related enforcement are not yet implemented.
- `->` represents a Result Type. In function declarations and Function Types, it denotes the result Type associated with the input side.
- `=>` represents a mapping or correspondence. It introduces function, Property accessor, and `if` branch expression bodies, `match` arms, named Origin arguments, and similar constructs.
- `:` represents a structural association: a Name with a Type, a key with a value, or a Label with a Block or Iteration Construct.
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

The [Empty Executable Block](#empty-executable-block) check uses source structure before directive selection, not the number of retained Koto nodes. Removing all executable Syntax from a syntactically nonempty Block is not itself an empty-Block error.

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

A Condition is a Boolean compile-time expression. It may inspect Compilation values, Project settings, generic Core Type parameters, Type Semantics parameters, declared Contract Clauses, and other information available in its evaluation environment.

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

A concrete Type or Type Semantics on the right of `is` tests identity. A named capability declared with `contract` or a named category tests satisfaction of its requirements.

Within a selected `#case` arm, its Condition and the negation of each preceding arm are available as additional facts alongside the declared Type Contract. These inferred facts are not themselves Contract Clauses. Narrowing preserves the concrete Core Type; `T is Comparable` does not replace `T` with `Comparable`.

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

Kimigayo source text uses UTF-8. Invalid UTF-8 source byte sequences are compile-time errors.

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

## Character Escapes

Char Literals and escaped String Literals share the following Character Escapes. Each escape produces exactly one Unicode scalar value.

| Escape | Result |
| ------ | ------ |
| `\0` | Null, U+0000 |
| `\\` | Backslash, U+005C |
| `\e` | Escape, U+001B |
| `\t` | Horizontal tab, U+0009 |
| `\n` | Line feed, U+000A |
| `\r` | Carriage return, U+000D |
| `\"` | Double quotation mark, U+0022 |
| `\'` | Apostrophe, U+0027 |
| `\u(H...)` | The specified Unicode scalar value |

The parentheses in `\u(H...)` must contain one to six ASCII hexadecimal digits (`0–9`, `A–F`, or `a–f`). Leading zeros are allowed; whitespace, signs, digit separators, and a `0x` prefix are not. The value must not exceed U+10FFFF or lie in U+D800..U+DFFF. Each escape is validated independently; surrogate escapes are never combined into a surrogate pair. Unsupported or incomplete escapes are compile-time errors.

```kimi
'\u(41)'            // A
'\u(000041)'        // A
'\u(1f600)'         // 😀
'\u()'              // Error: no digits
'\u(0000041)'       // Error: seven digits
'\u(0x41)'          // Error: prefix
'\u(D800)'          // Error: surrogate
'\u(110000)'        // Error: outside the Unicode range
'\u(D83D)\u(DE00)'  // Error: each escape is a surrogate
```

String interpolation is defined separately under [Escaped strings](#escaped-strings).

## CharLiteral

A `CharLiteral` has Type `char`. It encloses one directly written Unicode scalar value or one [Character Escape](#character-escapes) in single quotation marks. Delimiters are not part of the value.

```text
CharLiteral = "'" (DirectScalar | CharacterEscape) "'"
```

### Content and validation

After escape processing, the content must be exactly one Unicode scalar value. `DirectScalar` is any scalar value except the following, which must be escaped:

| Excluded direct content | Code points |
| --- | --- |
| Apostrophe and backslash | U+0027, U+005C |
| Controls | U+0000..U+001F, U+007F..U+009F |
| Line and paragraph separators | U+2028, U+2029 |

A Char Literal cannot contain a physical line break or tab and does not support string interpolation. Violations are compile-time errors.

```kimi
let letter: char = 'A'       // U+0041
let hiragana: char = 'あ'    // U+3042
let emoji: char = '😀'       // U+1F600
let quote: char = '\''
let slash: char = '\\'
let tab: char = '\t'
let line: char = '\n'
let separator: char = '\u(2028)'
let empty = ''              // Error: no scalar value
let pair = 'ab'             // Error: two scalar values
let flag = '🇯🇵'            // Error: two scalar values
let interpolation = '\(letter)' // Error: interpolation is not supported
```

### Normalization and displayed characters

The compiler does not normalize Char Literal content. Validation uses the content after escape processing. A `char` represents a scalar value, not a grapheme cluster or a displayed character; a combining mark alone is valid.

```kimi
'é'           // Valid: U+00E9
'\u(E9)'      // Valid: U+00E9
'e\u(301)'    // Error: U+0065 and U+0301
'\u(301)'     // Valid: one combining mark
```

The front end parses and validates Char Literals and preserves their original spelling when writing the syntax tree.

## StringLiteral

A `StringLiteral` produces a value of the built-in `string` Type. String contents use UTF-8. A String Literal may occupy one line or multiple lines.

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

Escaped strings support the shared [Character Escapes](#character-escapes) and string interpolation with `\(expression)`.

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

The current front end parses escaped strings, raw strings, and string interpolation, including nested expressions. Escape sequences are validated during parsing; evaluating interpolated strings is deferred to later compilation stages.

# Declarations

## Bindings

Properties and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` declares a mutable binding. A Type annotation and an initializer are independently optional when the omitted information can be inferred.

```kimi
let limit: i32 = 10
var current = 0
```

## Properties

Kimigayo has exactly one kind of value-bearing member: the **Property**. A compiler may lower Property storage to a storage slot, global storage, or another layout entity, but none of these implementation representations constitutes another member kind. A `let` or `var` declared inside an executable Block is a local binding, not a Property.

The current Parser records Properties, inline and block accessors, and basic syntax errors. Explicit getter result annotations, accessor expansion, contextual binding of `self`, `storage`, and `value`, `HasStorage`, access checks, initialization checks, and accessor type checking are planned work.

A Property has a Property Type, a Getter Result Type, a getter, an optional setter, and optionally owned storage. The Property Type determines the Type of owned storage, when present, and the setter's incoming value. A Property read has the Getter Result Type, which need not equal the Property Type. A computed Property retains a Property Type even though it has no storage.

```text
Property
    PropertyType
    GetterResultType
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
| `let x: T` | Default storage read | None | Stored |
| `var x: T` | Default storage read | `set { storage = value }` | Stored |
| `var x: T` with only an explicit `get` | Explicit getter | None | Depends on `storage` use |
| `var x: T` with only an explicit `set` | Default storage read | Explicit setter | Stored |
| `var x: T` with explicit `get` and `set` | Explicit getter | Explicit setter | Depends on `storage` use |

A default storage read is the Copy or shared-borrow operation defined under [Default getter results](#default-getter-results). It contains a bound reference to `storage` and therefore establishes `HasStorage`. It is equivalent to `get => storage` only when reading the storage value by Copy is permitted; it is not a Move from storage.

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

### Default getter results

An omitted or bodyless getter copies a Copy value and otherwise returns shared access to the stored value. It never moves a value out of the containing instance, implicitly duplicates an owning object reference, or returns an exclusive borrow. Copy capability is defined under [Copy and Move](#copy-and-move).

The default Getter Result Type and operation are determined by the complete Property Type, including its Type Semantics:

| Property Type | Default Getter Result Type | Operation |
| --- | --- | --- |
| Copy `owner/T` (also written `T`) | `T` | Copy the stored value |
| Non-Copy `owner/T` | `ref/T` | Shared borrow of the stored value |
| `obj/T` | `objref/T` | Shared borrow of the owned object |
| `rc/T`, `arc/T` | `objref/T` | Shared borrow without incrementing a reference count |
| `ref/T` | `ref/T` | Copy the shared reference |
| `objref/T` | `objref/T` | Copy the shared object reference |
| `uniq/T` | `ref/T` | Shared reborrow through the stored exclusive reference |
| `objuniq/T` | `objref/T` | Shared reborrow through the stored exclusive object reference |
| `unsafe/T` | `unsafe/T` | Copy the pointer value; dereference remains unsafe |

For a default instance getter that creates a borrow or reborrow, the result Origin is `from self`. It is tied to the current receiver Loan, even when the stored value carries a longer-lived Origin. Shared reborrowing of an exclusive reference suspends conflicting access through that reference while the result remains live. Copying a value instead preserves all Origin dependencies already carried by that value; it does not extend them or replace them with `self`.

A default static getter that creates a borrow anchors it to the Property's storage and its current stored value. Static allocation alone does not permit replacement or destruction of that value while the borrow is live. Normal Origin and Loan rules still apply; this does not add support for the deferred feature of borrow escape into global storage.

For example:

```kimi
struct Parent
    var child: obj/Node
```

has these conceptual accessor signatures:

```text
get(self: ref/Parent) -> objref/Node from self
set(self: uniq/Parent, value: obj/Node) -> ()
```

Reading `parent.child` borrows the object. Assigning a new `obj/Node` invokes the setter with an owning value. The default read cannot be used to extract ownership. Ownership extraction requires a separate operation, such as a function consuming the containing value or an explicitly defined replacement operation, subject to the normal Move rules. No extraction syntax is introduced here.

Likewise, a stored `let value: uniq/T from source` has a default getter returning `ref/T from self`. The exclusive reference remains in storage; neither its ownership nor an exclusive capability is returned. This permits shared inspection of an exclusive-borrow-bearing structure without making that structure Copy.

For generic Properties, the default result rule remains dependent on unresolved Type Semantics or Copy capability. Binding must not assume that an unconstrained Core Type parameter is Copy. It may use a result Type only when the constraints or concrete specialization establish the applicable row, and must resolve the operation before finalizing a specialization. This section does not define new syntax for generic Copy constraints.

### Accessors

A getter defines a Property read. It follows the [function body and result rules](#function-bodies-and-results), using the Getter Result Type as its declared Target Result Type. It may be expression-bodied or Block-bodied:

```kimi
var area: f64
    get => width * height

var loggedArea: f64
    get
        logRead()
        return width * height
```

A computed getter must be introduced explicitly with `get`; a bare expression in the Property body is invalid.

A getter may specify its result Type with `get -> ResultType`, including an Origin annotation as part of that Type. Without this annotation, its Getter Result Type is determined by the default result rules above, including for a custom getter. The getter body is checked against that Type; it does not independently infer a different result Type. An explicit result annotation overrides the default result rule. Omitted result Origins on a custom getter follow the normal function Origin elision rules.

```kimi
var child: obj/Node
    get -> objref/Node from self => storage@objref

// A computed getter may explicitly return a newly owned value.
var freshNode: obj/Node
    get -> obj/Node => Node.new()
```

The second example assumes `Node.new()` returns `obj/Node`. The annotation does not grant permission to move from borrowed storage: `get -> obj/Node => storage` is invalid for stored `obj/Node` because the getter has only `ref/Self` access. A custom body must perform any required borrow or reborrow explicitly; the default storage-read operation is synthesized only for omitted or bodyless getters.

A bodyless concrete getter with an explicit result annotation still performs the default storage read. Its result must satisfy the annotated Type and Origin without moving storage or implicitly duplicating ownership; an incompatible annotation is an error. A `let` Property retains its immutable-storage restrictions regardless of a getter result annotation.

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

accessor-declaration := access-restriction? get ('->' ResultType)?
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

A bodyless getter performs the default storage read, and a bodyless setter assigns `value` to `storage`. For a Copy Property such as `i32`, these operations have the following effective implementations:

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

The first requirement is readable; the second is both readable and writable. A conforming Property must have a compatible Property Type and provide every required accessor with sufficient accessibility. Getter conformance additionally checks its Getter Result Type and Origin contract; equality of Property Types alone is insufficient. A required setter accepts the required Property Type under the normal parameter compatibility rules.

A contract getter requirement may specify a result Type, for example `var child: obj/Node has get -> objref/Node from self`. Without an annotation, the required Getter Result Type follows the same default result rules as a concrete Property, but no storage read or Loan is generated by the requirement itself. Here `self` denotes the required shared receiver. A conforming getter must provide a result compatible with the required result Type for every legal receiver Origin; it cannot impose a shorter lifetime than the requirement promises. Generic requirements retain unresolved default result rules until the applicable constraints or specialization determine them.

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
get(self: ref/Self) -> GetterResultType
set(self: uniq/Self, value: PropertyType) -> ()
```

A getter consequently has shared, non-exclusive access to the instance. A setter has exclusive mutable access. The receiver controls access to the containing instance; it does not change the Type of `storage` to `ref/T` or `uniq/T`. The Getter Result Type describes the result of reading or borrowing that storage and is separate from its Type. Static Properties, including members of a `group`, have no instance receiver.

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

A **Block-bodied function** requires an explicit `return` to supply a non-Unit result. Every direct body expression, including the last, is in Discard Context; its value is discarded, with or without a trailing semicolon. Nested Value Contexts, such as initializers, retain their usual meaning.

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

A final `if`, `match`, or `loop` is also in Discard Context and is not an implicit function result. It may produce its own result, which is discarded. Use `return if ...`, `return match ...`, `return loop ...`, or explicit `return` on the appropriate paths.

An **Expression-bodied function** evaluates the expression after `=>` in Value Context and uses its normal result as the function result. A `return` executed inside that expression may also supply the function result. A trailing semicolon does not suppress the implicit result.

```kimi
func add(left: i32, right: i32) -> i32 => left + right
```

Both forms follow the shared [result validation](#result-validation), [reachability](#reachability), and [scope-exit destruction](#scope-exit-destruction) rules. [Function Boundaries](#function-boundaries) lists the other bodies to which these rules apply.

### Function Type Contract

A generic Block-bodied function may begin its body with a [Type Contract](#type-contract). Its Contract Clauses must precede every executable body item and are processed at compile time; they are not executable expressions.

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

The subject of a function's Contract Clause must name one of the function's generic parameters. A clause about a Core Type parameter may require a named capability declared with `contract` or another compile-time type capability. A clause about a Type Semantics parameter may name concrete semantics such as `ref` and `obj`, or a named semantics category. `and`, `or`, `not`, and parentheses combine requirements within a clause. In the example, `s is ref or obj` and `T is Comparable` are two Contract Clauses that together form the function's Type Contract.

At a call site, every explicit or inferred generic argument must satisfy the corresponding Contract Clauses. Within the function body, the Type Contract supplies the conditions on which type checking and compile-time specialization may rely, including the capabilities available for operations on those arguments. A function's Type Contract is not part of its Signature; two declarations that differ only in their Type Contracts therefore conflict.

The current Parser stores leading Contract Clauses separately from executable body items and preserves deferred directives on them. It checks clause subjects against the declared generic parameters and diagnoses clauses placed after executable items. Type Contract validation during Binding and specialization is planned.

### Unsafe functions

An **unsafe function**, declared with `unsafe func`, requires its caller to satisfy documented memory-safety conditions for their documented duration. Calling it requires an [Unsafe Block](#unsafe-block); violating its safety contract is undefined behavior. This runtime safety contract is distinct from a Type Contract and its Contract Clauses.

```kimi
// Safety: pointer must refer to a live, initialized i32 throughout the call,
// with valid range, alignment, provenance, and read permission.
// Access must obey reference, aliasing, and data-race rules.
unsafe func read(pointer: unsafe/i32) -> i32
    unsafe: return *pointer

// Safety: the same requirements as read.
unsafe func forward(pointer: unsafe/i32) -> i32
    unsafe:
        return read(pointer)

unsafe func invalidRead(pointer: unsafe/i32) -> i32
    return *pointer // Error: unsafe func does not make its body an unsafe context.
```

The `unsafe` modifier is not part of the function Signature and cannot distinguish overloads. Resolve overloads normally, without filtering by the caller's unsafe context; then check whether the selected call requires that context. Never choose another overload merely because the selected function is unsafe.

Initially, unsafe functions support direct calls only. Taking a function value, assigning it to a variable, passing it as an argument, or converting it to an ordinary Function Type is forbidden. Unsafe Function Types are specified separately.

```kimi
let reader = read // Error: an unsafe function cannot be taken as a function value.
```

# Declaration Containers

A **Declaration Container** is a named declaration scope whose body may contain Properties, functions, Contract Clauses, or nested Declaration Containers as permitted by its kind. Its body is delimited by indentation.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Properties, functions, and nested Declaration Container declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts Properties and functions in declaration order. Generic parameters, Origins, and a Type Contract are supported. |
| `enum` | Yes | Body parsing is not implemented. |
| `extension` | No | Its Name identifies the target. Body parsing is not implemented. |
| `contract` | No | Specifies associated-type Contract Clauses and Property requirements. The Parser preserves required accessors without generating implementations or storage. |

### Type Contract

A **Type Contract** declares conditions that Types, Type Semantics, and `Self` must satisfy, and establishes the operations and capabilities available within the implementation on the basis of those conditions. The entire section is called a Type Contract; each individual condition declaration is a **Contract Clause**.

A Contract Clause has the form `subject is requirement`. All clauses in a Type Contract must hold. The declaration's user fulfills the applicable requirements, and its implementation may rely on the resulting capabilities. For example, `T is Comparable` establishes that the implementation may use the comparison capabilities specified by `Comparable` for `T`. The Type Contract is therefore both an obligation to fulfill and a basis for the operations the implementation can perform.

The term is not limited to generic parameters: a clause may also describe `Self`, the enclosing Type. Each declaration kind determines which subjects it permits, as described for [functions](#function-type-contract). Type Semantics are part of Kimigayo's Type model and are also covered by the term Type Contract.

A `struct` header may contain generic parameters and an Origin list. Its Type Contract precedes Properties and functions.

```kimi
struct Container<s/T> origin owner, source
    T is Comparable
    s is reference

    var value: s/T
```

Here, the two Contract Clauses form the Type Contract of `Container`: one describes the Core Type parameter `T`, and the other describes the Type Semantics parameter `s`.

The following example illustrates a Type Contract that also includes a requirement on the enclosing Type:

```kimi
struct ComparableContainer<T>
    T is Comparable
    Self is Comparable

    var value: T
```

`T is Comparable` supplies comparison capabilities for the stored value's Type. `Self is Comparable` requires `ComparableContainer<T>` itself to fulfill `Comparable`; it does not automatically derive an implementation from the clause on `T`. The members needed to fulfill that requirement are omitted from this example. Semantic validation of these requirements is planned.

Type Contract names the section of conditions, independently of the current `contract` Declaration Container keyword. A named capability such as `Comparable` can be referenced by a Contract Clause; the clause and the declaration it references are distinct concepts.

### Associated Types and Property requirements

An associated-type Contract Clause in a `contract` begins with `associate`.

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

### Root and nested Declaration Containers

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

#### Char Type

`char` represents one Unicode scalar value and has a fixed storage size of 32 bits (4 bytes). Its valid ranges are U+0000..U+D7FF and U+E000..U+10FFFF, inclusive. Surrogates (U+D800..U+DFFF) and values above U+10FFFF are invalid.

Every value in these ranges is valid, including unassigned code points, private-use characters, noncharacters, controls, and combining marks. Assignment to a character or displayability is not required. Direct spelling in a literal has the additional restrictions defined under [CharLiteral](#charliteral).

The size guarantee does not guarantee the same internal representation as `u32`. Alignment and byte order are not specified here.

##### UTF-8 and strings

`char` is neither a UTF-8 code unit nor a byte sequence. Each scalar value decoded from UTF-8 text can be represented by a `char`.

| Type | Meaning |
| --- | --- |
| `u8` | An 8-bit unsigned integer; it can store a byte or UTF-8 code unit |
| `char` | One Unicode scalar value, stored in 4 bytes |
| `string` | UTF-8 Unicode text |

Single quotation marks produce `char`; double quotation marks produce `string`.

```kimi
'A'     // char
"A"     // string
'😀'    // char
"😀"    // string
'🇯🇵'   // Error: two scalar values
"🇯🇵"   // string
```

Encoding one `char` as UTF-8 produces one to four bytes:

| Unicode scalar value | UTF-8 length |
| --- | --- |
| U+0000..U+007F | 1 byte |
| U+0080..U+07FF | 2 bytes |
| U+0800..U+D7FF, U+E000..U+FFFF | 3 bytes |
| U+10000..U+10FFFF | 4 bytes |

| Literal | Scalar value | UTF-8 bytes of the value |
| --- | --- | --- |
| `'A'` | U+0041 | `41` |
| `'あ'` | U+3042 | `E3 81 82` |
| `'😀'` | U+1F600 | `F0 9F 98 80` |

In a source file, the content `あ` occupies three UTF-8 bytes; the complete literal `'あ'` occupies five (`27 E3 81 82 27`). Its value is U+3042 and its `char` storage size is four bytes. Storage size and UTF-8 encoded length are separate concepts.

#### String Type

`string` is the built-in Core Type for UTF-8 text. Its exact in-memory container and storage layout are implementation-defined.

#### Unit and Never Types

`()` is the Unit type. It has one value and represents the absence of a meaningful result.

Never is the type of an expression that does not complete normally and has no values. `return`, `exit`, `continue`, and `yield` expressions have the Never type. Their operands supply results to their targets without changing the types of the transfer expressions themselves. Never is a Type, not a Completion: a completed transfer has an abrupt Completion, whereas divergence produces no Completion. See [Completions](#completions) and [result validation](#result-validation).

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

This section describes indexing values with a length. [Raw pointer indexing](#pointer-arithmetic-and-indexing) instead uses signed offsets, has no implicit bounds check, and forbids from-end and Range indexing.

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

`unsafe/T` is a non-owning raw pointer to storage for Core Type `T`. The pointee Type controls access and element-sized address arithmetic. The pointer is Copy regardless of whether `T` is Copy: copying or destroying a pointer neither copies nor destroys its pointee, and does not free storage.

```kimi
let first: unsafe/Foo = obtainPointer()
let second = first // Copy the pointer, not Foo.
```

Holding a raw pointer does not guarantee pointee lifetime, initialization, alignment, or access permission. Safe ownership, Loan, Origin, reference, aliasing, and data-race rules remain in force, including when raw pointers access the same storage. Unsafe context permits operations the compiler cannot fully verify; it does not waive those obligations.

Missing required unsafe context, invalid Types, and unsupported operations are compile-time errors. Violating runtime memory-safety requirements is undefined behavior; detection and runtime checks are not guaranteed.

| Operation | Unsafe Block required |
| --- | --- |
| Declare, hold, Copy, pass, or destroy a raw pointer | No |
| Create a contextually typed `null` | No |
| Equality of same-Type pointers, or a pointer and `null` | No |
| Call an unsafe function | Yes |
| Dereference or index a raw pointer | Yes |
| Pointer arithmetic | Yes |
| Pointer-to-pointer or pointer/integer conversion | Yes |

#### Null and equality

`unsafe/T` permits null. A `null` literal requires an expected Type that determines `unsafe/T`; otherwise it is a compile-time error. Safe references `ref/T`, `uniq/T`, `objref/T`, and `objuniq/T` are non-null. Being non-null alone does not establish raw pointer validity.

```kimi
let pointer: unsafe/i32 = null
let unknown = null // Error: no pointer Type can be determined.
let empty = pointer == null
```

`==` and `!=` compare the addresses of two pointers with the same Type and return `bool`, without reading pointees. A comparison with `null` gives the literal the other operand's pointer Type. Null equals null and never equals a non-null pointer.

Initialized pointer values may be compared even if null or dangling. Equal addresses do not imply equal provenance, ownership, or access permission. Different pointer Types require an explicit conversion to a common Type, which itself requires unsafe context. Pointer ordering comparisons are not defined.

#### Dereference and ownership

`*pointer` denotes a memory place of Type `T`. Forming it requires live storage covering the required range, valid alignment, and provenance; null and one-past-the-end pointers cannot be dereferenced. **Provenance** records the allocation a pointer derives from and the basis for its accesses.

Actual reads require initialized, valid `T` values and read permission. Writes require write permission and must obey initialization and replacement rules. All accesses must respect reference, aliasing, and data-race rules.

```kimi
let pointer: unsafe/i32 = obtainPointer()
unsafe:
    let value = *pointer
    *pointer = 10
pointer = other // Error: the let binding cannot be reassigned.
```

Binding mutability and pointee write permission are independent. Copy, Move, and assignment follow their normal Type rules. Moving a non-Copy pointee is allowed and leaves its source storage uninitialized. The programmer must prevent subsequent reads and double destruction through other pointers or the original owner; the compiler does not guarantee identifying that owner or suppressing its automatic destruction.

```kimi
// Foo is non-Copy; pointer refers to an initialized Foo.
unsafe:
    let value = *pointer // Move Foo.
    use(value)
    // The programmer must prevent destruction of the moved source by its old owner.
```

Replacing an initialized pointee uses normal destruction rules. Initializing uninitialized raw storage requires a separately specified operation; ordinary assignment is not a substitute.

#### Pointer arithmetic and indexing

For `p: unsafe/T` and `n: isize`, including negative `n`, only these arithmetic and indexing forms are supported:

| Form | Meaning |
| --- | --- |
| `p + n` | Pointer displaced by `n * sizeof(T)` bytes. |
| `p - n` | Pointer displaced by `-n * sizeof(T)` bytes. |
| `p[n]` | The same memory place as `*(p + n)`. |

Here `sizeof(T)` denotes storage size including padding. Arithmetic requires known layout and positive size. Compute displacement mathematically; a displacement not representable in `isize`, or address wraparound, is undefined behavior.

Zero displacement returns the original pointer, including null, but still requires known layout and positive size. For nonzero displacement, the source must have valid provenance for a live allocation, and both source and result must lie within that allocation or one past its end. The result preserves provenance; coincidentally matching an address is insufficient.

Arithmetic alone requires neither pointee initialization nor alignment. One-past pointers may be held and used in permitted arithmetic, but not dereferenced. Pointer indexing has no length or implicit bounds check and must meet both arithmetic and dereference requirements.

```kimi
unsafe:
    let next = pointer + 1
    let prev = pointer[-1]
    pointer[10] = 123
    pointer[^1]   // Error: no from-end indexing.
    pointer[0..4] // Error: no Range indexing.
```

Pointer subtraction from another pointer, integer-left addition, and other pointer arithmetic are forbidden.

#### Pointer conversions

The `@` operator supports `unsafe/T -> unsafe/U`, `unsafe/T -> usize`, and `usize -> unsafe/T` in unsafe context. Pointer-to-pointer conversion within one address space preserves address and provenance; it does not change memory, initialization, or alignment, and does not establish permission to access the result as `U`.

```kimi
unsafe:
    let bytes = pointer@unsafe/u8
    let shifted = bytes + 13
    let typed = shifted@unsafe/i32
    // Access through typed still requires i32 alignment and a valid i32.
```

`unsafe/u8` permits byte-sized arithmetic, not reads of uninitialized memory. A cast itself does not read a pointee or require a valid, aligned value of the destination pointee Type; dereference and access do.

##### Target and round-trip guarantees

Initially, pointer/integer conversion is supported only for targets whose ordinary data addresses fit losslessly in `usize`, whose pointer address width, address-index width, `usize`, and `isize` widths agree, and which implement the guarantees below. Multiple address spaces and integer conversion of pointers requiring additional state, such as capabilities, are outside this initial model. Unsupported conversions are compile-time errors; concrete CPU/OS support is defined per target.

Null converts to integer zero, and integer zero converts to null, without requiring an all-zero internal pointer representation. These explicit conversions still require unsafe context.

A pointer-to-`usize`-to-original-pointer-Type round trip preserves its address and provenance when the integer obtained from that pointer is unchanged within the same execution and the originating allocation remains live throughout. Copying, storing, or passing that integer is allowed.

```kimi
unsafe:
    let address = pointer@usize
    let saved = address
    let restored = saved@unsafe/i32
    // Preserves address and provenance under the round-trip conditions.
    // Initialization and access permissions must still hold when accessing memory.
```

Conversion does not extend lifetime or restore lost access permissions. Integers changed by arithmetic, coincidentally equal integers, and addresses loaded from another execution have no such provenance guarantee. Arbitrary integer-to-pointer conversion is allowed on supported targets, but its result cannot be used where valid provenance is required unless the target provides an additional applicable guarantee.

##### Backend and separately specified operations

`ptr` is not a Primitive Type. LLVM `ptr` is a backend representation; instructions supply the Types needed for memory access and arithmetic. Lowering must preserve this specification and use properties such as `inbounds` only when their premises hold. Language undefined behavior and LLVM poison are distinct concepts.

Raw pointer acquisition APIs, allocation and deallocation, initialization of raw storage, conversion to or from safe references, ownership acquisition, and Unsafe Function Types are specified separately. Example functions such as `obtainPointer` and `use` are illustrative, not standard API declarations.

## Copy and Move

Copy and Move govern taking a value from a storage location for use as a value. A Copy Type is copied; any other Type is moved if the source permits it, or rejected otherwise. This applies to initialization, assignment sources, by-value arguments, and result transfers, including `return`, `exit`, `yield`, and implicit results. Borrow creation and reborrowing follow their own rules; merely naming a location does not always consume its value.

These rules follow Rust's ownership model. Here, **Copy** names a Type capability, without requiring a particular trait system. Ownership checking is planned, not implemented.

### Operation semantics

**Copy** duplicates a value while leaving the source initialized and usable, with its destruction responsibility unchanged. It requires only duplication of the value representation: it invokes no user-defined operation, deep allocation copy, reference-count increment, or resource acquisition. Copying a reference copies the reference, not its referent.

**Move** transfers a value and its associated ownership and destruction responsibility. The source becomes uninitialized and cannot be read, borrowed, copied, or moved again until reinitialized. Moving a borrow transfers its access capability, not ownership of its referent. Move invokes no user-defined move operation and does not require clearing the source memory.

Neither operation requires an actual memory transfer when optimization can eliminate it. Copy capability is independent of binding mutability: `let` and `var` do not change it.

```kimi
let a: i32 = 10
let b = a                    // Copy
let c = a                    // valid: a remains initialized
```

In the following example, `makeNode()` returns `obj/Node`:

```kimi
var a: obj/Node = makeNode()
let b = a                    // Move: b now owns the object
let c = a                    // error: a is uninitialized
a = makeNode()               // reinitialize a with a new object
let d = a                    // valid Move after reinitialization
```

### Copy classification

Copy capability depends on Type Semantics and stored components, including active Loan requirements, rather than on the Core Type alone.

| Type or semantics | Classification |
| ----------------- | -------------- |
| Owned integers, floating-point values, `bool`, `char`, and Unit | Copy |
| `ref/T`, `objref/T` | Copy regardless of whether the referent `T` is Copy |
| `uniq/T`, `objuniq/T` | Non-Copy |
| `obj/T`, `rc/T`, `arc/T` | Non-Copy even when `T` is Copy |
| `unsafe/T` | The pointer value is Copy |
| Owned Tuples and fixed-length arrays | Copy exactly when every component Type is Copy |
| Owned user-defined structures | Non-Copy by default; explicit opt-in is required |

A user-defined Type may opt into Copy only when every stored component is Copy, it has no custom destruction operation, it carries no active `uniq` Loan requirement, and representation duplication preserves ownership and borrowing guarantees. All-Copy components establish eligibility, not automatic opt-in. Computed Properties without storage do not contribute components to this check.

The Copy classification of owned `string` remains open until its ownership representation is settled; an owning, self-releasing UTF-8 buffer is non-Copy. Slice classification follows its borrowing representation: sharing elements does not itself establish Copy capability.

### Borrowing and initialization

Copy is a read of the source and must satisfy the active Loan rules. Move cannot take a value from a place overlapped by an active Loan. Neither operation bypasses borrowing restrictions.

A non-Copy referent cannot be moved out through `ref/T`, `uniq/T`, `objref/T`, or `objuniq/T`, leaving the borrowed place uninitialized. A separate replacement operation may provide extraction while leaving a valid replacement. Such an operation is not defined here.

Copied or moved borrow values retain their Origin constraints; neither operation extends the referent's lifetime. Reborrowing is distinct from Copy and does not make an exclusive borrow Copy.

A moved place may be reinitialized when ordinary write rules permit it. At a control-flow join, a subsequent use requires initialization on every incoming path that reaches the use.

Partial Move support remains open. If permitted, initialization and destruction responsibility must be tracked per part. Ordinary Property reads invoke accessors and are not direct Moves from backing storage.

### Destruction and explicit duplication

Assignment first secures its source value, then discharges destruction responsibility for the destination's old value before storing the new value. An uninitialized destination has no old value to destroy. [Scope-exit destruction](#scope-exit-destruction) destroys only initialized values for which the scope retains responsibility; a moved value is not destroyed again at its source.

Duplication requiring additional work must be explicit. In particular, duplicating an owning `rc/T` or `arc/T` reference increments a reference count and is not Copy; ordinary by-value transfer uses Move.

The syntax for Copy opt-in, automatic derivation, generic Copy constraints, and explicit duplication remains open. A future trait system may express these capabilities, but its design and the relationship between Copy and a duplication trait are not specified here.

## Origin-Based Lifetime Management

Kimigayo uses **Origins** instead of lifetime variables. An Origin describes how long a borrow remains valid; a **Loan** records which place is borrowed and whether the borrow is shared or exclusive.

The current Parser supports Origin lists on structures and functions, simple and qualified Origin annotations, Origin intersections, and named Origin arguments. Origin name resolution, inference, variance analysis, and borrow checking are not implemented.

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

A declared return Origin is the maximum dependency visible to callers; it does not require the implementation to borrow from that particular input. Each explicit or implicit function result, including one in unreachable code, must be a subtype of the declared result Type, subject to the shared [result validation](#result-validation) and [reachability](#reachability) rules.

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
        observe(self.out)
```

Here `observe` is assumed to accept `ref/Writer`. Reading `self.out` creates a shared reborrow through its stored `uniq/Writer`; it does not extract an exclusive reference. `sink` must remain valid throughout the destructor.

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

- abstract Origin parameters on contracts or trait-like abstractions (the Property getter receiver/result contracts above do not introduce contract-level Origin parameters);
- default Origins for trait objects;
- higher-ranked Origins;
- borrow escape into heap or global storage;
- lending iterators;
- destructor dangling relaxation.

These features require extensions to the core rules above and must not be inferred from this revision.

# Control Flow

Control flow separates four concepts: **Evaluation Context** determines whether an expression's result is consumed or discarded; **Control Boundary** determines transfer targets and lookup barriers; **Control Transfer** requests a change in control; and **Completion** describes how an evaluation finishes.

```text
Evaluation Context
    Value Context
    Discard Context

Control Boundary
    Function Boundary
    Labeled Block Boundary
    Deferred Control Boundary
    Iteration Boundary
        for
        while
        loop
    Selection Boundary
        if
        match

Control Transfer
    return
    exit
    continue
    yield

Completion
    Normal(result)
    Return(target, result)
    Exit(target, result)
    Continue(target)
    Yield(target, result)
```

The collective term **Iteration Construct** means `for`, `while`, or `loop`; an **iteration** is one repetition of its body. Each Iteration Construct establishes an Iteration Boundary. Each `if` or `match` establishes a Selection Boundary, regardless of its result or Evaluation Context. A boundary may accept some transfers, stop lookup for others, and be transparent to the rest; see [target lookup](#target-lookup).

| Control Transfer | Role |
| --- | --- |
| `return` | End the current function and supply its result. |
| `exit` | End the nearest Iteration Construct or Deferred Block, or the enclosing Labeled Block or Iteration Construct named by `from Label`. |
| `continue` | Start the next iteration of the nearest Iteration Construct, or the one named by `Label`. |
| `yield` | End the nearest enclosing selection and supply its result. |

An unlabeled `exit` skips Labeled Blocks. A `loop` or explicitly named Labeled Block can receive a result operand in either Evaluation Context. A Deferred Block accepts only operandless `exit` directed at itself. No transfer may cross a Deferred Control Boundary. The language uses `exit` for iteration termination; `break` is not used.

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
    else => 0                   // Implicit Expression-body result.

    return result               // Supply the function's result.
```

## Completions

**Normal completion**, represented by `Normal(result)`, means that an expression or construct finishes and returns control to its evaluator. A statement's normal Completion uses Unit without making that statement a value-producing expression. **Abrupt completion** is a `Return`, `Exit`, `Continue`, or `Yield` directed at a resolved lexical target. A transfer expression does not complete normally, even when its target subsequently does.

An operandless `return` or `exit` supplies Unit, so the corresponding Completion always contains a result. Whether a source-level operand is required or forbidden is checked separately. `Continue` has no result.

A boundary handles a Completion directed at itself and propagates other valid Completions after the required [Scope Exit](#scope-exit-destruction) processing. A `loop` or Labeled Block handles `Exit(self, result)` by completing with `Normal(result)`; a selection similarly handles `Yield(self, result)`. A Deferred Block catches its own `Exit(self, ())`, finishes its body's cleanup, and resumes the pending Scope Exit. An Iteration Construct handles `Continue(self)` by proceeding to its next iteration. A Function Boundary handles `Return(self, result)` by delivering the secured result to its caller. These rules apply only to valid transfer targets.

**Divergence** means that evaluation never finishes and produces no Completion. Under the broader term **Evaluation Outcome**, Completion and divergence are distinct cases. Never is a static Type describing the absence of normal completion; it is neither a Completion variant nor a synonym for divergence.

## Blocks and evaluation contexts

A **Block** is an indentation-delimited sequence of declarations, expressions, and statements evaluated in order. An ordinary Block completes with Unit on reaching its end, including when its last item is a declaration or conditional compilation removes all its items. Source-level executable bodies must satisfy the nonempty rule below. Nesting an ordinary Block adds no control-transfer target. Constructs with their own result rules apply those rules instead. Function bodies follow [Functions](#function-bodies-and-results).

### Empty Executable Block

An executable Block cannot be empty in source. Its indented body must contain at least one syntactically complete **Syntax item**: a declaration, expression, statement, or compile-time directive. Blank lines, comments, and separators alone do not count. A directive must itself have valid syntax, including its required condition, target, and body.

The Parser reports an empty or missing executable body as an error. If no body item appears before a same- or shallower-indentation item, or before the end of the source, it must not silently accept an empty body or treat the following item as part of that body.

```kimi
defer: // Parse error: no indented body.
closeConnection()

defer:
    // Cleanup is currently unnecessary.
nextOperation() // Parse error: the defer body contains only a comment.
```

Indent the intended body. To explicitly do nothing, use the Unit expression `()`:

```kimi
defer:
    closeConnection()

if condition
    ()

defer:
    ()
```

This rule applies to executable bodies of branches, `match` arms, iterations, Labeled Blocks, Unsafe Blocks, Deferred Blocks, functions, and accessors. It does not define emptiness rules for a `match` arm list or a Declaration Container body. Single-line forms already require one complete InlineStatement.

#### Conditional compilation and results

Check emptiness against source structure **before conditional-compilation selection**, independently of reachability. A syntactically valid `#if` or `#case` makes its containing Block nonempty even if selection later removes all executable Syntax. This does not waive the directive's own syntax or Case Group selection requirements.

```kimi
defer:
    #if windows
        closeHandle()
```

When `windows` is false, the Deferred Block has no executable content but remains valid. The Parser must check source items even when early directive selection does not create Koto nodes for them.

Nonempty syntax does not imply a valid result. After selection, normal Type, result-coverage, and control-transfer rules still apply. In particular, `()` placed directly in a Block is discarded; it does not implicitly supply that Block's result.

```kimi
let result = if condition
    () // Error: nonempty, but this branch must explicitly yield its result.
else
    yield ()
```

Likewise, removing a required `yield` or result-bearing `exit` through conditional compilation may cause a result-coverage error, even though the source Block passes the emptiness check.

### Evaluation contexts

A **Value Context** is a syntactic position that uses an expression's value: an initializer, operand, argument, condition, `match` subject, `return` / `exit` / `yield` operand, or Expression body introduced by `=>`. It remains a Value Context even when the expected Type is Unit or the result is subsequently unused. Reachability, constant evaluation, and optimization do not change it.

A **Discard Context** evaluates an expression and discards its normal result. It does not impose Unit as the expression's result Type, suppress Type checking, or remove ownership and destruction responsibilities for the discarded result. Expressions placed directly in ordinary, Labeled, Unsafe, or Deferred Block bodies, Iteration Construct bodies, Block-bodied branches, and Block-bodied functions use this context, including the final expression. Initializers, arguments, operands, and other nested positions retain their normal Evaluation Contexts.

An expression determines its result; its Evaluation Context determines whether that result is consumed or discarded. A `loop` accepts result operands in either context. Selections follow the unified [Result-requiring Selection](#branch-results) rules.

A trailing semicolon does not change an expression's Evaluation Context or whether an Expression body supplies an implicit result. Body form, not the number of direct expressions or declarations, determines the branch result rule.

### Block constructs

The following constructs give a Block a name, an unsafe context, or deferred execution:

```text
Block
    Labeled Block    Label:
    Unsafe Block    unsafe:
    Deferred Block  defer:
```

| Construct | Category | Execution and result |
| --- | --- | --- |
| Labeled Block | Expression; also usable in Discard Context | Execute now; receive a result through `exit value from Label`. |
| Unsafe Block | Block Statement | Execute now with unsafe permission; no expression result. |
| Deferred Block | Block Statement | Register now and execute at Scope Exit; no expression result. |

A **Block Statement** is a statement with a scoped body, not an expression. Unsafe and Deferred Blocks are allowed only in executable bodies, not directly in Declaration Containers. Both have an indented multiline form and a single-line form containing one [InlineStatement](#inlinestatement). Both forms create an independent body scope and have the same evaluation and cleanup rules.

At statement start, contextual keywords `unsafe:` and `defer:` take precedence over Label parsing. Neither declares a Label. `unsafe/T` remains Type Semantics syntax and `unsafe func` a function declaration modifier; outside their special contexts these spellings follow normal Name rules.

### InlineStatement

An **InlineStatement** is one statement completed on a single line without a following indented Block.

| Form | Requirement |
| --- | --- |
| An expression used as a statement, such as a call or assignment | The complete expression fits on that line. |
| A local `let` or `var` declaration | The complete declaration fits on that line. |
| `return`, `exit`, `continue`, or `yield` | Normal target, operand, and boundary rules apply. |
| Single-line `unsafe:` or `defer:` | Its body is recursively an InlineStatement. |

Function and Declaration Container declarations, Labeled Blocks, and constructs requiring a following indented Block are excluded. A trailing semicolon follows normal rules, but multiple semicolon-separated statements are not allowed in one inline body. In nested forms, the right-hand statement is the body of the immediately preceding colon.

```kimi
unsafe: unsafeOperation()
defer: close()
defer: unsafe: releaseRaw(pointer)
defer: defer: log("nested")
defer: exit // Valid: end this Deferred Block when it executes.

let result = unsafe: unsafeOperation() // Error: not an expression.
let result = defer: close()           // Error: not an expression.
defer: close(); log("done")           // Error: two statements.
defer: return                        // Error: crosses the Deferred Control Boundary.
defer: if condition                  // Error: requires a following Block.
    cleanup()
```

Use the multiline form for such a branch. `defer: unsafe: releaseRaw(pointer)` is equivalent to a Deferred Block containing an Unsafe Block whose body calls `releaseRaw(pointer)`.

### Labeled Block

A Labeled Block begins with `Label:` followed by its indented body on the next line. It receives a result only from an `exit` explicitly targeting that Label. Its trailing expression never implicitly supplies a result, and unlabeled exits still skip it.

A **Result-requiring Labeled Block** either occurs in Value Context, including when Unit is expected, or has a result-bearing `exit` lexically targeting it. Classify it after target lookup and before reachability analysis. Only exits whose resolved target is this Block count, including those written inside nested constructs; results supplied to other targets do not count.

Every reachable path completing that Block must supply an explicit `exit expression from Label`. Use `exit () from Label` for Unit. Falling through is an error, and an operandless self-targeted exit is forbidden, including in unreachable code. Paths leaving for an outer target or never completing need no result for this Block. Follow the common [result validation](#result-validation) rules for Type inference and compatibility, including in Discard Context.

```kimi
let result = resolve:
    if cached()
        exit cachedValue() from resolve
    let value = calculate()
    if acceptable(value)
        exit value from resolve
    exit fallback() from resolve

let missing = work:
    if ready()
        exit 1 from work
    // Error: reachable fall-through has no result.

let unit: () = work:
    process()
    exit () from work

work:
    exit 1 from work
    exit "text" from work // Error: incompatible even though unreachable and discarded.
```

A Labeled Block that does not require a result occurs in Discard Context and has no result-bearing self-targeted exit. It completes with Unit on fall-through or `exit from Label`. A Labeled Block with no reachable normal completion has Expression Type Never; missing required results are errors, not a reason to infer Never.

### Unsafe Block

An **Unsafe Block** executes its body immediately with permission for [unsafe operations](#unsafe). It is a statement and cannot appear as an initializer, argument, or other expression operand, in either body form. It creates no Control Boundary and does not intercept transfer lookup. Its body follows ordinary Evaluation Context and Scope Exit rules.

```kimi
work:
    unsafe:
        if finished()
            exit from work
        unsafeOperation()
```

The implementer must establish safety through runtime checks, internal invariants, or the enclosing unsafe function's documented contract. An Unsafe Block adds no conditions to a safe caller. A safe function must not silently require its caller to meet unchecked memory-safety conditions; a raw address or null check alone cannot establish safe access.

Unsafe permission extends lexically into nested Blocks, including Deferred Blocks, but not across a Function Boundary. Normal Type, ownership, and borrowing checks still apply.

```kimi
unsafe:
    func inner(pointer: unsafe/i32) -> i32
        return *pointer // Error: inner needs its own Unsafe Block.
```

### Deferred Block

A **Deferred Block** registers cleanup when execution reaches `defer`, without evaluating its body, arguments, conditions, or initializers. It has no result value and returns no value to the outside. Expressions within it use their normal positional Evaluation Contexts.

The registration belongs to the innermost executable scope directly containing the statement: a function body, branch or arm Block, current iteration body, Labeled or Unsafe Block, or executing Deferred Block body. It is not automatically function-wide. Unreached registrations do not run; each iteration registers and cleans up independently. Cancellation and manual invocation of a registration are not provided.

```kimi
func process(flag: bool)
    defer: log("function end")
    if flag
        defer: log("branch end")
        work()
    log("after branch")
```

For true `flag`, output is `branch end`, `after branch`, then `function end`. Deferred execution and automatic destruction share the [Scope Exit ordering](#scope-exit-destruction).

#### Deferred Control Boundary

Each Deferred Block establishes a **Deferred Control Boundary**. Transfer lookup cannot cross it. Unlike a Function Boundary, it accepts only operandless `exit` directed at itself, including through nested ordinary or Unsafe Blocks. Result operands, including `()`, are forbidden for this target.

An unlabeled `exit` targets the nearest Iteration Construct or Deferred Block. Consequently, exits and continues of an inner loop retain their normal meaning, as do results of inner selections and Labeled Blocks. A `return` to an outer function, a named transfer to an outer construct, or a `yield` to an outer selection is an error. A separate nested function retains its own Function Boundary and normal returns.

```kimi
defer:
    defer: log("cleanup body end")
    if alreadyClosed()
        exit // Ends this body; its nested defer and remaining outer cleanup still run.
    close()

defer:
    for value in values
        if skip(value)
            continue // Targets the inner for.
        if done(value)
            exit     // Targets the inner for, not the Deferred Block.
    let message = if failed()
        yield "failed"
    else
        yield "done"
    log(message)
```

Early `exit` finishes the current body's cleanup and then resumes the pending outer Scope Exit; it neither cancels other registrations nor replaces a pending return value or transfer. Nested Deferred Blocks cannot transfer across their own boundary to a target in an outer Deferred Block. These restrictions apply even in unreachable code.

```kimi
defer:
    exit () // Error: a Deferred Block accepts no result operand.
```

#### Deferred evaluation and ownership

Resolve Names in the lexical scope at the registration's source position. Later local declarations are not visible and cannot change an earlier binding. Registration does not implicitly Copy or Move referenced locals or create a closure value; accesses occur when the body executes and observe the then-current bindings.

```kimi
var count: i32 = 1
let saved = count
defer: log(saved) // Explicit snapshot: prints 1.
defer: log(count) // Reads at execution: prints 2 first.
count = 2
```

Check initialization, Copy/Move, Loan, Origin, and destruction responsibility on every applicable exit path, including deferred uses in borrow lifetimes. Registration alone does not borrow all referenced values. Later operations are allowed if they remain compatible with the eventual cleanup.

```kimi
// Resource is non-Copy; inspect borrows, consume moves.
let resource = makeResource()
defer: inspect(resource)
consume(resource) // Error: cleanup would access a moved value.

let other = makeResource()
defer: inspect(other)
defer: consume(other) // Error: executes before inspect and moves its input.
```

A valid Move during cleanup removes subsequent automatic destruction responsibility. Raw pointer operations retain their programmer-managed obligations; `defer` does not repair double destruction or extend raw pointer validity. Secured result borrows must remain valid after all cleanup.

#### Nested and unsafe cleanup

An inner `defer` registers while its enclosing Deferred Block executes and runs when that body exits, before the original scope's remaining cleanup. It cannot add a registration to an outer scope already being exited.

```kimi
defer:
    defer: log("inner end")
    log("outer body") // Prints before inner end.

defer: unsafe: releaseRaw(pointer) // Runs at the directly containing scope's exit.
unsafe:
    defer: releaseRaw(other)      // Runs at this Unsafe Block's exit.
    useRaw(other)
```

A Deferred Block does not itself grant unsafe permission. Permission follows the operation's lexical context, never its later caller, and does not cross Function Boundaries. Safety conditions must hold when the delayed operation executes.

#### Implementation model

Keep registration and body execution distinct in control-flow analysis. A non-completing body does not make the statement immediately after its registration unreachable; analyze its completion on actual cleanup paths. Lower registration and destruction into a common exit sequence, retaining registration state only where needed. A dynamic closure, function value, or heap cleanup stack is not required.

For `if condition` containing only `defer: cleanup()`, a true branch registers and runs cleanup before leaving that branch; false registers nothing. No registration flag is needed in this simple case, and Lowering must not move cleanup into the surrounding scope. Example cleanup functions are illustrative, not standard API declarations.

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

A labeled Iteration Construct uses `Label: for ...`, `Label: while ...`, or `Label: loop`. Unlike a [Labeled Block](#labeled-block), adding a Label to an Iteration Construct does not change its result rules; a labeled `loop` may appear in Value Context:

```kimi
var result = outer: loop
    for value in values
        if found(value)
            exit value from outer
```

Labels follow the character rules for [Names](#name) and have a namespace separate from those of variables and Types. Labels with the same Name and overlapping scopes in one function are invalid.

A Label is visible only inside its construct's body, excluding its `for` iterable or `while` condition. A transfer may identify only an enclosing construct in the same Function Boundary without crossing a Deferred Control Boundary. Sibling, inner, and other-function Labels are inaccessible. A Label names a construct, not an instruction address: jumping into a body or back to a completed construct is not supported.

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
| `exit` to a Result-requiring Labeled Block | Required; use `exit () from Label` for Unit. |
| `exit` to another Labeled Block | Omitted; an explicit operand makes the Block Result-requiring. |
| `exit` to `for`, `while`, or a Deferred Block | Forbidden, including `()`; the target completes with Unit. |
| `exit` to a `loop` in either Evaluation Context | Optional; omission supplies Unit. |
| `continue` to an Iteration Construct | Forbidden. |
| `yield` to a Selection Boundary | Required; use `yield ()` for Unit. |

Operands are evaluated before transfer. If operand evaluation leaves by another transfer or never completes, the original transfer does not occur. Otherwise, its result is secured by Copy or Move before [scope-exit destruction](#scope-exit-destruction) and delivery to the target. Each transfer expression itself has type [Never](#unit-and-never-types).

### Target lookup

Resolve targets by walking outward through lexical containment. Resolve the target first, then check operand presence and Type; an unsuitable operand never causes lookup to skip a target.

| Operation | Target without a Label | Named target | Stop before finding a target |
| --- | --- | --- | --- |
| `return` | Nearest Function Boundary | Not allowed | Error at a Deferred Control Boundary or if no function exists. |
| `exit` | Nearest Iteration Construct or Deferred Block | Enclosing Labeled Block or Iteration Construct named by `from Label` | Error at a Function Boundary; a named lookup also stops at a Deferred Control Boundary. |
| `continue` | Nearest Iteration Construct | Enclosing Iteration Construct named by `Label` | Error at a Function or Deferred Control Boundary. |
| `yield` | First enclosing Selection Boundary (`if` / `match`) | Not allowed | Error at an Iteration, Function, or Deferred Control Boundary. |

Failure to find a target is an error. A named target must be of the required kind; `continue work` is invalid if `work` names a Block.

A construct acts as a target or lookup stop only inside its body. Its own condition, iterable expression, or `match` subject does not acquire that construct's boundary.

Ordinary and Unsafe Blocks never stop lookup. A Labeled Block is an `exit` target only when explicitly named; otherwise it is transparent to every transfer. An `if` / `match` never stops `return`, `exit`, or `continue` lookup. A `yield` targets the first encountered Selection Boundary. A `yield` resolving to a Selection Boundary makes that selection Result-requiring, regardless of reachability or Evaluation Context. It never retargets an outer selection because the inner selection lacks `else`, fails coverage, or has an incompatible result Type.

Named `exit` and `continue` may cross intervening Iteration Constructs and ordinary, Labeled, or Unsafe Blocks within the same function. No transfer searches beyond a Function Boundary or across a Deferred Control Boundary. Operand checks never change the selected target: `exit 1` directed at a Deferred Block is an error, not an exit to an outer loop.

```kimi
var result = loop
    for value in values
        exit 10 // Error: the nearest Iteration Construct is for, which forbids an operand.
```

### Function Boundaries

Each of these bodies establishes an independent **Function Boundary**:

- Named functions, including methods and nested functions.
- Anonymous functions and closures.
- Property getters and setters.
- Destructors (`deinit`).

In these control-flow rules, "function" includes all of these bodies. A `return` ends only its own function. Other transfers cannot target an outer function's Labels, Iteration Constructs, or selections.

A getter's result Type is its Getter Result Type, determined by the default Property read rules or an explicit getter result annotation; it need not equal the Property Type. Setters and `deinit` return Unit. Each body follows the [function body and result rules](#function-bodies-and-results). Normal completion of `deinit`, including through `return`, still performs any automatic field destruction required by the Type's destruction rules.

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

A Label selects an outer Iteration Construct explicitly:

```kimi
outer: for x in xs
    for y in ys
        if skipX(x, y)
            continue outer

        if found(x, y)
            exit from outer

        process(x, y)
```

## Iteration Constructs

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
| Self-targeted `exit` | End the Iteration Construct. | End the Iteration Construct. |

### `loop`

`loop` repeats unconditionally. It discards body values and starts the next iteration at the beginning of the body after body fall-through or a self-targeted `continue`.

Only an `exit` targeting that `loop` supplies its normal result. `return`, exits to outer constructs, and exits caught by inner constructs supply no result to it. A self-targeted operandless `exit` supplies Unit. Result operands are permitted in both Value Context and Discard Context. Discarding the result does not change the `loop`'s result Type or exempt its exits from compatibility checks.

```kimi
var result = loop
    let value = next()

    if invalid(value)
        exit -1

    if found(value)
        exit value
```

Only reachable self-targeted exits contribute candidates to this `loop`'s result inference. Unreachable exits must still be compatible with its Target Result Type when one is available; see [result validation](#result-validation). Nested `if` expressions do not intercept `exit`.

```kimi
loop
    exit 10                     // Valid: loop produces an integer, then discards it.
```

Incompatible result operands remain errors when the `loop` result is discarded. An operandless exit contributes Unit rather than being ignored.

```kimi
outer: loop
    loop
        exit from outer
```

The inner `loop` has no result-producing path and has type Never. The outer `loop` completes with Unit. See [result validation](#result-validation) for the common rules.

## `if`, `match`, and `yield`

### Branch results

Each `if` branch and `match` arm has an **Expression body** or a **Block body**. The form is explicit and does not depend on the number of direct declarations or expressions.

| Body form | Evaluation and result rule |
| --- | --- |
| Expression body: `=> Expression` | Evaluate the expression in Value Context and implicitly supply its normal result to the selection. A trailing semicolon does not suppress the result. |
| Block body: an indented Block | Evaluate every direct expression in Discard Context. Use `yield expression` to supply a result to the selection. No expression, including a sole or final expression, is an implicit result. |

For `if`, the Expression body follows the condition or `else` on the same line; a Block body starts on the next line at a greater indentation. For `match`, `=>` also separates the pattern from its body: an expression follows it on the same line, or an indented Block follows it on the next line. Different branches of the same selection may use different body forms.

A **Result-requiring Selection** is an `if` or `match` that meets any of these conditions:

- It occurs in Value Context, including when Unit is expected.
- One of its own branches has an Expression body.
- A `yield` lexically resolves to that selection.

Resolve transfer targets before this classification, without using reachability. Nested constructs' branch forms and yields targeting them do not count for the outer selection. An `else if` chain is one selection.

Every Result-requiring Selection follows three common requirements:

- **Exhaustiveness:** `if` requires a final `else`; `match` must cover all subject values through its patterns or a catch-all arm. Literal conditions, unreachable branches, and paths that never complete do not waive this requirement.
- **Result coverage:** every reachable path that completes normally must supply a result. A Block must use `yield`, including `yield ()` for Unit; declarations, discarded expressions, and bodies emptied by conditional compilation do not supply implicit branch results. Source-level empty executable Blocks are parse errors under the [nonempty rule](#empty-executable-block). A path leaving for an outer target or never finishing needs no result. After a transfer caught internally, analysis follows the continuation.
- **Result compatibility:** explicit and implicit results obey the shared [result validation](#result-validation) rules, even when the selection's result is discarded.

A selection that does not require a result has only Block bodies, no self-targeted `yield`, and occurs in Discard Context. Reaching a selected Block's end or selecting no branch supplies Unit. Paths that leave for an outer target or never finish supply no result to that selection.

### `if`

`if` tests Boolean conditions in order and executes the first selected branch. It may have subsequent `else if` branches and one final `else`. Condition parentheses are optional. Each branch independently chooses an Expression body or a Block body.

A parenthesized condition containing a single initialized `let` or `var` binding tests the bound value. For example, `if (var z = Func()) => 1 else => 0` requires the Type of `z` to be Boolean. The initializer is evaluated once, and an explicit binding Type still constrains the initializer. This also applies to `while` conditions. It does not make declarations in ordinary Blocks produce a result.

```kimi
var compact = if condition => 1
else => 2

var explicit = if condition
    log("true")
    yield 1
else
    yield 2

var mixed = if condition => 1
else
    prepare()
    yield 2

if condition => 1;
else => 2;                      // Valid: the integer result is discarded; semicolons do not change it.
```

`yield` ends the whole target `if`, skipping the rest of its branch. When the `if` requires a result, an `else if` without a final `else` is insufficient for exhaustiveness.

```kimi
if condition => 1               // Error: else is required even in Discard Context.

let missingElse = if condition
    1                           // Error: Value Context requires else and an explicit result.

let missingResult = if condition
    1                           // Error: the Block must yield its result.
else => 2

if condition
    process()                   // Valid: an ordinary selection in Discard Context.
```

An `if` that does not require a result may omit `else`.

### `match`

`match` evaluates its subject once, tests arms in source order, and executes the first matching arm. Every arm uses `pattern => Expression` or `pattern =>` followed by an indented Block. There is no fall-through to another arm.

```kimi
var result = match value
    A =>
        prepare()
        yield 1

    B => 2
```

`yield` ends the whole target `match`. This example assumes `A` and `B` cover every case. A `match` that does not require a result may be non-exhaustive.

### Nested `yield` targets

A Labeled Block does not stop `yield` lookup:

```kimi
var result = if condition
    work:
        yield 10                // Supplies the outer if's result, not work's.
else => 20
```

An Expression body implicitly supplies the result of its expression:

```kimi
var result = if a => calculate()
else => 0
```

In a Block body, a nested selection's result is discarded unless explicitly consumed. Its yields still target the inner selection:

```kimi
var result = if a
    if b
        yield 1                 // Valid: supplies the inner if's discarded result.
    else
        yield 2

    log("done")
    // Error: the outer Block reaches its end without yielding a result.
else => 0
```

Removing `log("done")` does not fix the missing outer result: a sole expression in a Block body is still discarded. Use an initializer and an explicit outer `yield` to consume the inner result:

```kimi
var result = if a
    let inner = if b
        yield 1
    else
        yield 2

    log("done")
    yield inner
else => 0
```

A conditional `yield` inside another `if` targets that inner `if`; it does not implement an early result for the outer selection. In particular, the inner `if` then requires its own `else`:

```kimi
var result = if a
    if invalid()
        yield -1                // Error: the inner result-requiring if needs else.

    yield calculate()
else => 0
```

Put the conditional in the outer `yield` operand to supply the conditional result explicitly:

```kimi
var result = if a
    prepare()
    yield if invalid() => -1
    else => calculate()
else => 0
```

`yield` cannot cross an Iteration Boundary. A direct `yield -1` in the following `loop` body would be an error; `exit` supplies the `loop`'s result, and the outer `yield` supplies that result to the `if`:

```kimi
var result = if condition
    yield loop
        exit -1
else => 0
```

Adding direct expressions before the `yield` does not change this result rule or either transfer's target.

## Result validation

A transfer supplies a result only to its resolved target. Function results follow [Functions](#function-bodies-and-results); Blocks, Iteration Constructs, and branches use their result sources defined above. Discard Context does not exempt a construct from result validation.

**Implementation status:** The current Parser preserves explicit branch body forms, and control-flow analysis checks existing selections and loops. Value-producing Labeled Blocks, Unsafe and Deferred Blocks, and their extended target and cleanup rules are planned. The default type provider handles primitive literals and simple declared Types. General name/overload resolution, numeric conversions, pattern Binding, and Origin compatibility still require Binding; unresolved checks are exposed as pending obligations, not accepted as valid. Bodies containing deferred compile-time directives await directive selection before analysis.

Validate results in this order:

1. Determine Evaluation Contexts and body forms, resolve transfer targets, and classify Result-requiring Selections and Labeled Blocks without excluding unreachable code. Check syntax, Names, operand presence, and local Type correctness. Enforce selection exhaustiveness requirements.
2. Apply [reachability](#reachability) analysis to result sources, required Scope Exit processing, and paths leaving each construct. Collect result candidates only from reachable result-delivery paths. A transfer whose operand or required cleanup cannot complete normally supplies no result to its original target.
3. Check result coverage: reject any reachable path that reaches an end requiring a result without supplying one. Where a construct implicitly supplies Unit, include that Unit as a candidate only when the path is reachable. A non-Unit Block-bodied function may not fall through.
4. Determine the Target Result Type as described below, independently of whether the construct's Expression Type is Never. Unreachable result sources do not contribute candidates or constraints to inference.
5. When a Target Result Type is available, check every explicit result operand and implicit Expression-body result against it, including in unreachable code. Operandless `return` and `exit` supply Unit. Apply normal conversion and Origin compatibility rules. A source that cannot itself complete normally supplies no value to compare; its local operations and any transfers inside it are still checked.

Paths that leave a construct for an outer target or never complete supply no result candidate for that construct. Transfers caught internally may let evaluation continue and must be followed to their continuation.

### Expression Type and Target Result Type

The **Expression Type** describes the value of a normally completing expression. A `loop`, selection, or Labeled Block with no reachable path completing with its own result has Expression Type Never. Missing required results are errors, not a reason to infer Never. Unsafe and Deferred Blocks are statements and have no Expression Type; their body completion is analyzed separately.

The **Target Result Type** constrains results supplied to a control boundary. Determine it from an explicit declaration or an expected Type, or infer it from reachable result candidates using normal type-inference and conversion rules. Expected Types must propagate to result sources even when the construct's Expression Type is Never.

| Source of Target Result Type | Compatibility checking, including unreachable results |
| --- | --- |
| Explicit declaration or expected Type | Check against that Type. |
| Type inferred from reachable result candidates | Check against the inferred Type. |
| No Type supplied and no reachable result candidates | No Target Result Type is available; omit only the comparison against it. |

Never inferred solely from the absence of reachable results does not become a Target Result Type. An explicitly specified Never still constrains results. Unreachable fall-through does not manufacture a Unit result for compatibility checking.

A function retains its declared return Type. Without a declared or expected return Type, infer its return Type from reachable function results; if there are no candidates, expose Never as its inferred return Type without using that fallback as a Target Result Type for unreachable `return` operands. A function value itself has a Function Type.

```kimi
loop
    if false
        exit 1

    if false
        exit "text"

    continue
```

This loop has no declared or expected Type and no reachable result candidates, so no Target Result Type is available and its Expression Type is Never. Both exits are valid. When no Target Result Type is available, unreachable result sources for that boundary need not be mutually compatible. Their operands must still satisfy local Type correctness.

```kimi
let x: i32 = loop
    if false
        exit "text"             // Error: the expected Target Result Type is i32.
```

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

- Follow evaluation order, branches, Iteration Constructs, and resolved transfers. A statically non-completing expression has no edge to the next sequential element.
- Follow the continuation of a construct that catches a transfer, such as the code after a Labeled Block ended by `exit`, or an expression consuming a yielded result.
- A `defer` registration does not execute its body. Analyze registered bodies on the Scope Exit paths that reach them; a non-completing cleanup prevents subsequent cleanup and delivery of the pending transfer or result. An exit caught by the Deferred Block finishes that body's cleanup before resuming the pending Scope Exit.
- Prune condition outcomes only for Boolean literals `true` and `false`, optionally parenthesized, in `if`, `else if`, and `while`. Otherwise, consider both outcomes when condition evaluation completes normally.
- Do not prune additional paths through constant propagation, analysis of called function bodies, or general constant folding.
- Do not prune `for` paths using iterable values or `match` arms using constant subjects. Analyze each arm; pattern exhaustiveness determines whether an unmatched path exists.

Reachability affects **result candidate collection**, **result inference**, and **result coverage**. It does not exempt code from **local Type correctness** checks. When a Target Result Type is available, unreachable result sources are checked against it under the same compatibility rules as reachable result sources. This also applies to implicit Expression-body results, so replacing `yield expression` with `=> expression` does not bypass Type checking.

The compatibility rules above apply whenever a Target Result Type is available. Without one, syntax, Names, transfer targets, operand presence, local Type correctness, and Result-requiring classification are still checked.

```kimi
var result = loop
    if false
        exit                    // Error: Unit is incompatible with the inferred integer result.

    exit 1                      // The only result candidate is an integer.

func f() -> i32
    if false
        return "text"           // Error: string is incompatible with i32.

    return 1

let selected = if false => "text" // Error: string is incompatible with the inferred integer result.
else => 1
```

In each example, the unreachable expression or transfer is locally valid, but its result is incompatible with the target. Undefined Names or Labels, invalid operand operations, and value-bearing exits targeting `for` are also errors in unreachable code. These rules concern runtime control flow; Syntax excluded by [conditional compilation](#compile-time-directives) follows its separate Binding rules.

## Scope-exit destruction

**Scope Exit** combines registered Deferred Blocks and automatic destruction. It applies both to ordinary scope completion and to scopes left by `return`, `exit`, `continue`, or `yield`.

Ownership, temporary-lifetime, and construct-lifetime rules determine each value's owning scope and destruction point. These rules also govern temporaries, `for` iterables and iterators, iteration bindings, `match` subjects, and owned function parameters. A transfer uses those scopes to determine what it leaves.

### Cleanup order

Process departing scopes from inner to outer, completing one scope's cleanup before the next. Within one scope, combine local declaration positions and `defer` statement positions into a single lexical order, then process it in reverse. Later initialization or reassignment does not change a binding's original position.

At each position, execute a Deferred Block only if registered; destroy a value only if initialized and the scope still owns its destruction responsibility. Skip moved and already destroyed values. Responsibility is independent of future-use liveness: a value still owned by the scope must be destroyed even after its last use.

```kimi
let first = makeResource("first")
defer: log("A")
let second = makeResource("second")
defer: log("B")
```

Cleanup order is `B`, destruction of `second`, `A`, destruction of `first`. Deferred Blocks therefore run in reverse registration order.

Bindings introduced at scope entry, such as parameters, `self`, iteration bindings, and pattern bindings, precede the body's statements. Explicit bindings in one parameter list or pattern are ordered left to right and destroyed in reverse order. Explicit `self` follows its written position; individual construct specifications place implicit bindings. These positions do not confer ownership or change the bindings' owning scopes.

```kimi
func process(first: Resource, second: Resource)
    defer: log("end")
```

If both parameters retain owned values, cleanup is `end`, destruction of `second`, then destruction of `first`. Borrowed bindings do not cause destruction of their pointees. Similarly, a defer inspecting an iteration binding runs before that binding's remaining owned value is destroyed.

### Results and transfers

For a transfer with a result operand, or an implicit Expression-body result:

1. Evaluate the operand.
2. Secure the result using normal Copy / Move rules.
3. Destroy temporaries whose normal lifetime ends at completion of that result expression.
4. Run Deferred Blocks and automatic destruction in departing scopes, in the common cleanup order.
5. Deliver the secured result and complete the target's termination or continuation.

Other temporaries are processed at the scopes and positions set by normal lifetime rules; merely being absent from the result is not a reason for earlier destruction. Without a result operand, omit result-related work. If evaluating the operand causes another transfer, process that actual transfer instead of completing the original one.

A moved result is not destroyed again at its source; a copied result leaves the source's destruction responsibility intact. Deferred execution cannot replace the secured result, although ordinary effects on shared objects remain possible.

```kimi
func answer() -> i32
    var value: i32 = 1
    defer: value = 2
    return value // Returns the already copied 1.

func take() -> Resource
    let resource = makeResource()
    defer: inspect(resource) // inspect borrows; Resource is non-Copy.
    return resource // Error: cleanup would access the moved source.
```

Process only scopes actually left. A `continue` cleans up the departing scopes of the current iteration before the next iteration, while retaining outer scopes needed for continuation. Named transfers apply the same rule to all intervening scopes they leave. An exit from a Deferred Block performs its body's nested cleanup and resumes the pending outer cleanup sequence.

Normal ownership, borrowing, and [Drop checking](#drop-checking) apply throughout cleanup. Securing a result first does not permit a borrow of a destroyed local to escape. If partial initialization or partial Move is permitted, destroy parts with remaining responsibility rather than excluding the whole aggregate. Raw pointer access does not guarantee automatic tracking of the original owner's destruction responsibility.

### Completion and abnormal termination

Consume a Deferred Block's registration when its execution starts. Each registration executes once if cleanup reaches it; an inner defer registers in the executing body's own scope, never in an outer scope already being exited.

The pending transfer or result delivery completes only after all required cleanup completes normally. Nonterminating deferred execution or destruction prevents remaining cleanup and the original transfer from completing; general termination proofs are not required.

Forced process termination and undefined behavior provide no cleanup guarantee. Exceptions, panic, cancellation, and stack unwinding, if introduced, require separately defined common rules for Deferred Blocks, destruction, and secured results. This specification does not by itself guarantee cleanup under those mechanisms.
