# Kimigayo Language Specification

- [1. Overview](#1-overview)
  - [1.1. Purpose and implementation status](#11-purpose-and-implementation-status)
  - [1.2. Conventions and notation](#12-conventions-and-notation)
- [2. Compilation](#2-compilation)
  - [2.1. Build model](#21-build-model)
  - [2.2. Compile-time directives](#22-compile-time-directives)
- [3. Lexical structure](#3-lexical-structure)
  - [3.1. Names](#31-names)
  - [3.2. Number literals](#32-number-literals)
  - [3.3. Character escapes](#33-character-escapes)
  - [3.4. Character literals](#34-character-literals)
  - [3.5. String literals](#35-string-literals)
- [4. Types](#4-types)
  - [4.1. Primitive Core Types](#41-primitive-core-types)
  - [4.2. Compound Type syntax](#42-compound-type-syntax)
  - [4.3. Structures](#43-structures)
  - [4.4. Index, Range, and Slice](#44-index-range-and-slice)
  - [4.5. Type Semantics](#45-type-semantics)
  - [4.6. Raw pointers and unsafe operations](#46-raw-pointers-and-unsafe-operations)
- [5. Declarations](#5-declarations)
  - [5.1. Signatures](#51-signatures)
  - [5.2. Declaration Containers](#52-declaration-containers)
  - [5.3. Type Contracts](#53-type-contracts)
  - [5.4. Bindings](#54-bindings)
  - [5.5. Functions](#55-functions)
- [6. Properties](#6-properties)
  - [6.1. Effective representation](#61-effective-representation)
  - [6.2. Default getter results](#62-default-getter-results)
  - [6.3. Accessors](#63-accessors)
  - [6.4. Inline accessor declarations](#64-inline-accessor-declarations)
  - [6.5. Contextual identifiers and receivers](#65-contextual-identifiers-and-receivers)
  - [6.6. Initialization](#66-initialization)
  - [6.7. Access control](#67-access-control)
  - [6.8. Storage, addressability, and result semantics](#68-storage-addressability-and-result-semantics)
- [7. Ownership and lifetimes](#7-ownership-and-lifetimes)
  - [7.1. Copy and Move](#71-copy-and-move)
  - [7.2. Origins and Loans](#72-origins-and-loans)
  - [7.3. Abstract Origins](#73-abstract-origins)
  - [7.4. Origin elision and return contracts](#74-origin-elision-and-return-contracts)
  - [7.5. Exclusive Origins](#75-exclusive-origins)
  - [7.6. Borrow checking](#76-borrow-checking)
  - [7.7. Deferred lifetime features](#77-deferred-lifetime-features)
  - [7.8. Temporary lifetimes](#78-temporary-lifetimes)
- [8. Expressions and operators](#8-expressions-and-operators)
  - [8.1. Classification and contexts](#81-classification-and-contexts)
  - [8.2. Evaluation order](#82-evaluation-order)
  - [8.3. Primary expressions](#83-primary-expressions)
  - [8.4. Access and application](#84-access-and-application)
  - [8.5. Precedence and associativity](#85-precedence-and-associativity)
  - [8.6. Operator semantics](#86-operator-semantics)
  - [8.7. Assignment](#87-assignment)
  - [8.8. Extension boundaries and reserved syntax](#88-extension-boundaries-and-reserved-syntax)
  - [8.9. Implementation status](#89-implementation-status)
- [9. Control flow](#9-control-flow)
  - [9.1. Completions](#91-completions)
  - [9.2. Blocks and evaluation contexts](#92-blocks-and-evaluation-contexts)
  - [9.3. Block constructs](#93-block-constructs)
  - [9.4. Labels](#94-labels)
  - [9.5. Control transfers](#95-control-transfers)
  - [9.6. Iteration Constructs](#96-iteration-constructs)
  - [9.7. Selections: if, match, and yield](#97-selections-if-match-and-yield)
  - [9.8. Result validation](#98-result-validation)
- [10. Scope exit, cleanup, and termination](#10-scope-exit-cleanup-and-termination)
  - [10.1. Deferred Blocks](#101-deferred-blocks)
  - [10.2. Scope-exit destruction](#102-scope-exit-destruction)
  - [10.3. Panic Termination](#103-panic-termination)

## 1. Overview

### 1.1. Purpose and implementation status

**Kimigayo** is a programming language built from scratch to be consistent, fast, simple, fun, and safe.

> **Pre-alpha status:** This document specifies the intended language. The implementation supports project loading, target setup, tokenization, parsing, diagnostics, and basic control-flow analysis, including Unsafe and Deferred Blocks and value-producing Labeled Blocks. Unless stated otherwise, general Binding, overload resolution, type checking, generic specialization, ownership and Origin analysis, lowering, and code generation are planned.

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

        #case (s is ref) and (T is i32)
            return "ref/i32"
        #case s is ref
            return "ref"
        #case _
            return "other"
```
> **Design Note (Do not modify this text!)**
>
> * `$` is not a macro. It is the Composition Root.

### 1.2. Conventions and notation

Kimigayo does not guarantee backward compatibility between language versions. It prioritizes consistency and language quality, leaving room for language evolution; AI-assisted development also makes source migration easier.

Use four spaces per indentation level. Indentation expresses syntactic containment. Types and Declaration Containers use PascalCase; functions, Properties, local bindings, parameters, and other value names generally use camelCase.

| Notation | Meaning and uses |
| --- | --- |
| `[]` | Same-Type sequences and element access: array construction and indexing. |
| `()` | Ordered grouping: parameters, arguments, Tuples, Unit, Function Types, conditions, and operator precedence. |
| `<>` | Generic parameters and arguments, including compile-time parameters and arguments that construct Types. |
| `{}` | Unused; reserved for future language evolution. |
| `=` | Assignment, subject to [Copy and Move](#71-copy-and-move). |
| `->` | Result Type associated with the input side of a function declaration or Function Type. |
| `=>` | Mapping or correspondence: function and accessor expression bodies, `if` expression bodies, `match` arms, and named Origin arguments. |
| `:` | Structural association: Name and Type, key and value, or Label and Block/Iteration Construct. |
| `#` | A compile-time construct. Lowercase reserved directives such as `#if` differ from PascalCase Attributes such as `#Inline`. |

The complete Type form is `semantics/CoreType from origin`; see [Types](#4-types) for its components and omission rules.

## 2. Compilation

### 2.1. Build model

The build model separates workspace orchestration, project configuration, library source, and target compilation:

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

A Kotonoha tokenizes and parses each `SourceDocument`, merging declarations into one root Koto tree. Root executable syntax is placed as described under [root and nested containers](#521-root-and-nested-containers).

A CodeContext belongs to one Kotonoha and supplies its Compilation and diagnostic destination to the Tokenizer and Parser. Nodes cannot be inserted into a Declaration Container of another Kotonoha.

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

Target preparation supplies `os` (a string), Boolean OS/build-mode flags (`windows`, `linux`, `macos`, `debug`, `release`), and `pointerWidth`. Unsupported architectures and targets without an LLVM data layout cannot produce a prepared Compilation.

#### 2.1.1. Serialization

Serialization uses SourceCode or binary artifacts, not Koto serialization. SourceCode preserves declarations for reconstruction. Binary interfaces must preserve caller-required information, including whether a function is unsafe. Artifact formats are specified separately.

### 2.2. Compile-time directives

Compile-time Directives select Syntax during compilation without producing runtime control flow:

| Form | Purpose |
| --- | --- |
| `#if` | Independently includes or excludes one Syntax node. |
| `#case` | Selects one arm from an ordered Case Group. |
| `#Name` | Attaches an Attribute; it is not a Compile-time Directive. |

The former `#If(...)` form is an Attribute. The lowercase `#if` form specified here is a separate language construct.

#### 2.2.1. Syntax and selection

`#if` controls either the next Syntax node at the same indentation or one indented Block:

```kimi
#if windows
alias Kimi.Windows

#if debug
    let logging = true
    let assertions = true
```

Consecutive same-indentation `#case` arms form a **Case Group**. Blank lines and comments preserve the group; any other Syntax node ends it. Select the first matching arm in source order. The optional catch-all `#case _` must occur once at most, as the final arm.

```kimi
func useImplementation<T>(value: T) -> ()
    #case windows
        useWindowsImplementation(value)
    #case T is i32
        useIntegerImplementation(value)
    #case _
        useGenericImplementation(value)
```

Every final evaluation context must select an arm. A catch-all may be omitted if the compiler proves the explicit arms exhaustive. Fully resolved Conditions with no match are a compile-time error.

The selected Block occupies the structural position of the Case Group. Normal Block, result-Type, scope, and control-transfer rules apply after selection. An early-false `#if` target is consumed without creating Koto nodes. Unselected `#case` arms do not undergo ordinary Binding, Lowering, or code generation.

The [nonempty Block rule](#921-nonempty-executable-blocks) checks source structure before selection. Removing all executable Syntax does not itself make a Block invalid.

#### 2.2.2. Staged condition evaluation

`#if` and `#case` share an evaluator. Before ordinary Binding, the Parser evaluates Conditions using the prepared compile-time environment. Directive Binding later resolves remaining Names without binding excluded Syntax.

An evaluation attempt produces exactly one of these results:

| Result | Meaning |
| --- | --- |
| **True** | The Condition is satisfied. |
| **False** | The Condition is not satisfied. |
| **Deferred** | The Condition has a valid compile-time dependency whose value is not yet available. |
| **Error** | The Condition is invalid, non-Boolean, or refers to an unavailable Name. |

After Directive Binding, an unbound declared generic parameter produces **Deferred**, while an unknown Name produces **Error**. `and`, `or`, and `not` use short-circuit reasoning; for example, `false and Deferred` is **False**, while `true and Deferred` is **Deferred**.

The Parser currently treats unresolved Names and unsupported expressions as **Deferred** because it cannot distinguish unknown Names from declared generic parameters. Directive Binding must later classify them and report unknown Names.

Each pass attempts the single Condition of a `#if` and every explicit Condition of a Case Group. Every arm Condition is checked, and an **Error** is reported even when an earlier arm determines the selection. A Case Group is selected as soon as its first-match result is certain:

- a **False** arm is skipped;
- a **True** arm is selected when every preceding arm is **False**;
- a preceding **Deferred** arm prevents selection of a later **True** arm or `#case _`;
- Conditions after an already selectable **True** arm cannot change the selection.

For example, `#case windows` may resolve during parsing, while `T is i32` remains **Deferred** until `T` is bound.

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

#### 2.2.3. Conditions and narrowing

A Condition is a Boolean compile-time expression over Compilation values, Project settings, generic Core Type or Type Semantics parameters, declared Contract Clauses, and other information available in its evaluation environment.

```kimi
windows
windows or linux
os == "windows" or os == "linux"
pointerWidth == 64
s is ref
T is i32
T is Comparable
(s is ref) and (T is Comparable)
```

A concrete Type or Type Semantics on the right of `is` tests identity. A named capability declared with `contract` or a named category tests satisfaction of its requirements.

A selected `#case` arm adds its Condition and the negation of every earlier Condition to the facts available from the Type Contract. These facts are not Contract Clauses. Narrowing preserves the concrete Core Type: `T is Comparable` does not replace `T` with `Comparable`.

Compile-time Conditions do not evaluate runtime values. The initial design does not destructure values or introduce pattern bindings. For example, `#case value is ref/i32 x` is invalid; use `#case (s is ref) and (T is i32)` to narrow a value of Type `s/T` to `ref/i32`. Parentheses separate each [requirement expression](#532-requirement-expressions) from the surrounding condition.

#### 2.2.4. Koto representation

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

Normally only Deferred directives retain directive Koto nodes: an early-true `#if` contributes its Target directly; an early-false one contributes none. Invalid Case Groups may remain for error recovery. Resolving a specialization must not mutate Koto shared with others.

The Parser implements early `#if` and `#case` evaluation and retains Deferred directives as dedicated Koto nodes. Later Binding/specialization evaluation and constraint narrowing are planned.

## 3. Lexical structure

Kimigayo source text uses UTF-8. Invalid UTF-8 source byte sequences are compile-time errors.

### 3.1. Names

A **Name** identifies a declaration in source. All named declarations, including Declaration Containers, Types, functions, Properties, bindings, and parameters, use the same character rules. A Name contains a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`),
- an underscore (`_`), or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`), or Letter Number (`Nl`).

Each continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`), or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`), Connector Punctuation (`Pc`), or Format (`Cf`).

Contextual keywords may be used as Names in contexts that accept contextual identifiers. Reserved keywords may not be used as Names. In particular, `in` acts as a delimiter in a `for` header, and `has` introduces an inline Property accessor list; both may be used as Names outside those contexts.

For example, `Dog`, `_value`, `point2`, `日本語`, and `ǅelta` are valid Names, while `2point`, `has-value`, and the empty string are not.

### 3.2. Number literals

A `NumberLiteral` begins with an ASCII decimal digit. A leading `+` or `-` is an operator and is not part of the literal. The sign characters may occur inside a decimal exponent.

Supported forms are:

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

`_` is an ignored digit separator. Consecutive separators, separators after a base prefix, and trailing separators are allowed: `1__000`, `123_`, `0x_FF`, and `0b__101__` are valid. A base prefix with no digits, including one followed only by separators, denotes zero: `0x` and `0o___` are valid. An exponent must start with a decimal digit immediately after `e`/`E` and its optional sign; `1e_2` and `1e+_2` are invalid.

A decimal point belongs to the literal only when followed immediately by a decimal digit: `1.0` is floating point; `1.` is integer `1` followed by a dot. Only decimal literals support fractions and exponents; `0xFF.0` starts with integer `0xFF`.

After removing separators, a decimal literal with a fraction or exponent converts to IEEE 754 `f64`. Finite results are valid; conversion to either infinity is an error. Other decimal literals and all base-prefixed literals are integers. Magnitudes `0` through `2^128 - 1` are stored as 128-bit bit patterns; larger magnitudes are invalid.

`NumberLiteral` currently has no type suffix. The internal `i128` / `f64` representations do not determine a literal's language Type; [expression type inference](#831-type-inference) defines contextual Types and defaults. To specify a Type, use a declaration annotation or an explicit conversion such as `123@i32`.

The syntax tree canonicalizes spelling: integers render as signed 128-bit decimal values; floating-point values use round-trip `f64` notation with a decimal marker when needed (for example, `1.0`). Compile-time basic-value evaluation currently supports integer representations fitting `i64` and all valid `f64` literals.

### 3.3. Character escapes

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

`\u(H...)` requires one to six ASCII hexadecimal digits (`0–9`, `A–F`, `a–f`). Leading zeros are allowed; whitespace, signs, separators, and `0x` are forbidden. Values above U+10FFFF or in U+D800..U+DFFF are invalid. Validate each escape independently; surrogate escapes never combine into pairs. Unsupported or incomplete escapes are compile-time errors.

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

String interpolation is defined separately under [Escaped strings](#351-escaped-strings).

### 3.4. Character literals

A `CharLiteral` has Type `char`. It encloses one directly written Unicode scalar value or one [Character Escape](#33-character-escapes) in single quotation marks. Delimiters are not part of the value.

```text
CharLiteral = "'" (DirectScalar | CharacterEscape) "'"
```

#### 3.4.1. Content and validation

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

#### 3.4.2. Normalization and displayed characters

The compiler does not normalize Char Literal content. Validation uses the content after escape processing. A `char` represents a scalar value, not a grapheme cluster or a displayed character; a combining mark alone is valid.

```kimi
'é'           // Valid: U+00E9
'\u(E9)'      // Valid: U+00E9
'e\u(301)'    // Error: U+0065 and U+0301
'\u(301)'     // Valid: one combining mark
```

The front end parses and validates Char Literals and preserves their original spelling when writing the syntax tree.

### 3.5. String literals

A `StringLiteral` produces UTF-8 text of Type `string`. Both forms support single or multiple lines:

| Form | Delimiter | Backslash escapes | Interpolation |
| ---- | --------- | ----------------- | ------------- |
| Escaped string | One double quotation mark (`"`) on each side | Yes | Yes |
| Raw string | The same number of double quotation marks, at least three, on each side | No | No |

#### 3.5.1. Escaped strings

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

Escaped strings support the shared [Character Escapes](#33-character-escapes) and string interpolation with `\(expression)`.

An interpolation begins with `\(` and ends at its matching `)`. The enclosed text is parsed as a Kimigayo expression, including any nested parentheses, and the expression's string representation is inserted into the surrounding string:

```kimi
"Hello, \(name)."
"Total: \(price * quantity)"
```

#### 3.5.2. Raw strings

A raw string is enclosed by matching delimiters of three or more consecutive double quotation marks. Backslashes, line breaks, and interpolation-like text are ordinary content; no escape processing or interpolation occurs.

```kimi
"""C:\Users\name\file.txt"""
"""
First line
Second line
"""
```

A delimiter of `N` quotation marks permits shorter runs in the content. Lengthen the delimiter when needed; four quotation marks allow literal `"""`:

```kimi
""""The token """ appears here.""""
```

The opening and closing delimiters must contain the same number of quotation marks and are not part of the value.

The current front end parses escaped strings, raw strings, and string interpolation, including nested expressions. Escape sequences are validated during parsing; evaluating interpolated strings is deferred to later compilation stages.

## 4. Types

A **Type** combines Type Semantics, a Core Type, and an Origin:

```text
semantics/CoreType from origin
```

| Component | Meaning |
| --- | --- |
| Type Semantics | How the value is represented, owned, accessed, or used. |
| Core Type | What the value is. |
| Origin | Where the value derives from; constrains its lifetime or validity. |

For example, `ref/Dog from owner` is a shared borrow of a `Dog` whose validity derives from `owner`. The Core Type is required; Type Semantics and Origin may be omitted when determined by the language or context.

The front end parses much of this syntax. Type resolution, layout validation, subtyping, ownership rules, and most Type semantics are not implemented.

Kimigayo provides a fixed set of primitive Core Types and user-defined named Core Types.

### 4.1. Primitive Core Types

Primitive Core Types are built into the language. Sizes below are storage sizes.

#### 4.1.1. Integer Types

| Signed | Unsigned | Size |
| --- | --- | --- |
| `i8` | `u8` | 8 bits (1 byte) |
| `i16` | `u16` | 16 bits (2 bytes) |
| `i32` | `u32` | 32 bits (4 bytes) |
| `i64` | `u64` | 64 bits (8 bytes) |
| `i128` | `u128` | 128 bits (16 bytes) |
| `isize` | `usize` | Native pointer size of the target platform |

#### 4.1.2. Floating-point and Boolean Types

| Type | Size |
| --- | --- |
| `f32` | 32 bits (4 bytes) |
| `f64` | 64 bits (8 bytes) |
| `bool` | 8 bits (1 byte) |

#### 4.1.3. Character Type

`char` represents one Unicode scalar value and has a fixed storage size of 32 bits (4 bytes). Its valid ranges are U+0000..U+D7FF and U+E000..U+10FFFF, inclusive. Surrogates (U+D800..U+DFFF) and values above U+10FFFF are invalid.

All scalars in these ranges are valid, including unassigned code points, private-use characters, noncharacters, controls, and combining marks; displayability is irrelevant. [Character literals](#34-character-literals) impose additional direct-spelling restrictions.

The size guarantee does not guarantee the same internal representation as `u32`. Alignment and byte order are not specified here.

#### 4.1.4. UTF-8 and strings

`char` is neither a UTF-8 code unit nor a byte sequence. Each scalar value decoded from UTF-8 text can be represented by a `char`.

| Type | Meaning |
| --- | --- |
| `u8` | An 8-bit unsigned integer; it can store a byte or UTF-8 code unit |
| `char` | One Unicode scalar value, stored in 4 bytes |
| `string` | UTF-8 Unicode text |

Single quotation marks produce `char`; double quotation marks produce `string`. For example, `'🇯🇵'` is invalid because it contains two scalars, while `"🇯🇵"` is a valid string.

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

The source literal `'あ'` occupies five bytes (`27 E3 81 82 27`), including its three-byte UTF-8 content. Its value is U+3042, stored as a four-byte `char`; encoded length and storage size differ.

`string` is the built-in Core Type for UTF-8 text. Its exact in-memory container and storage layout are implementation-defined.

#### 4.1.5. Unit and Never Types

`()` is the Unit type. It has one value and represents the absence of a meaningful result.

An expression with no reachable path that completes normally has Type Never, which has no values. `return`, `exit`, `continue`, `yield`, and `$panic(...)` have Type Never. Transfer operands supply results to their targets without changing the Types of the transfer expressions themselves. Never is a Type, not a Completion: a completed transfer has an abrupt Completion, whereas divergence produces no Completion. Missing required results are errors, not Never. See [Completions](#91-completions), [result validation](#98-result-validation), and [Panic Termination](#103-panic-termination).

### 4.2. Compound Type syntax

A named Core Type may be qualified with dots and may have generic arguments.

```kimi
A.B<T, U>
```

Tuple types use parentheses and commas. Function types use `->` between the parameter type and return type.

```kimi
(i32, string)
(i32, string) -> bool
```

### 4.3. Structures

A `struct` defines a composite value Type. Its Properties may use primitive or structure Types, with Type Semantics specifying representation, ownership, and access:

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

### 4.4. Index, Range, and Slice

This section describes indexing values with a length. [Raw pointer indexing](#463-pointer-arithmetic-and-indexing) instead uses signed offsets, has no implicit bounds check, and forbids from-end and Range indexing.

An element Index may be a nonnegative `isize` or a From-end Index `^n`. Applying it with `value[index]` selects one element; the resolved Index must satisfy `0 <= index < length`.

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

The omitted start boundary is zero. The omitted end boundary is the length of the indexed value and is exclusive. An inclusive Range must have an end boundary. A Range retains its boundary information until application to a sequence resolves length-dependent boundaries.

Ranges are non-associative; an unparenthesized chained Range such as `a..b..c` is invalid. Parentheses do not make a Range a valid numeric boundary of another Range. See the [precedence table](#85-precedence-and-associativity).

Applying a Range with `value[range]` produces a Slice over the selected consecutive elements. A Slice does not copy its elements. Its Origin derives from the indexed value, so it cannot outlive that value.

After resolving from-end boundaries, an exclusive Range must satisfy `0 <= start <= end <= length`. An inclusive Range must satisfy `0 <= start <= end < length`. An exclusive Range with equal boundaries is empty.

Invalid Indices or boundaries, including negative `n` in `^n`, are check failures under [Panic Termination](#103-panic-termination). Safe sequence access may omit a check only when safety is proven.

```kimi
let values = [10, 20, 30, 40]
let last = values[^1]       // 40
let middle = values[1..^1]  // Slice referring to 20 and 30.
let all = values[..]
let empty = values[2..2]
```

### 4.5. Type Semantics

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

#### 4.5.1. Owned values

`T` and `owner/T` are equivalent: both directly own a value with the data layout of `T`.

#### 4.5.2. Value borrows

Value borrows provide non-owning access to value data, subject to lifetime constraints.

- `ref/T` is a shared borrow; multiple shared references may coexist.

- `uniq/T` is an exclusive mutable borrow; no conflicting reference may coexist.

#### 4.5.3. Owned objects

Object layout consists of metadata followed by the data layout of `T`:

- `obj/T` is an exclusively owned object.

- `rc/T` uses non-atomic reference counting; the object remains alive while an owning reference exists.

- `arc/T` uses atomic reference counting. Atomic ownership management does not guarantee safe concurrent mutation of `T`.

#### 4.5.4. Object borrows

Object borrows provide non-owning access to objects, subject to lifetime constraints.

- `objref/T` is a shared object borrow; multiple shared references may coexist.

- `objuniq/T` is an exclusive mutable object borrow; no conflicting reference may coexist.

### 4.6. Raw pointers and unsafe operations

`unsafe/T` is a non-owning raw pointer to storage for Core Type `T`, which determines access and element-sized arithmetic. Pointers are Copy regardless of `T`; copying or destroying one does not copy or destroy its pointee or free storage.

```kimi
let first: unsafe/Foo = obtainPointer()
let second = first // Copy the pointer, not Foo.
```

A raw pointer guarantees neither pointee lifetime, initialization, alignment, nor access permission. Ownership, Loan, Origin, reference, aliasing, and data-race rules still govern the same storage. Unsafe context permits unverifiable operations without waiving these obligations.

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

#### 4.6.1. Null and equality

`unsafe/T` permits `null`, whose expected Type must determine `unsafe/T` or compilation fails. Safe references (`ref/T`, `uniq/T`, `objref/T`, `objuniq/T`) are non-null. Non-nullness alone does not validate a raw pointer.

```kimi
let pointer: unsafe/i32 = null
let unknown = null // Error: no pointer Type can be determined.
let empty = pointer == null
```

`==` and `!=` compare the addresses of two pointers with the same Type and return `bool`, without reading pointees. A comparison with `null` gives the literal the other operand's pointer Type. Null equals null and never equals a non-null pointer.

Initialized pointers may be compared even if null or dangling. Equal addresses imply neither equal provenance nor ownership or access permission. Different pointer Types require an explicit unsafe conversion to a common Type. Pointer ordering comparisons are not defined.

#### 4.6.2. Dereference and ownership

`*pointer` denotes a memory place of Type `T`. Forming it requires live storage covering the required range, valid alignment, and provenance; null and one-past-the-end pointers cannot be dereferenced. **Provenance** records the allocation a pointer derives from and the basis for its accesses.

Actual reads require initialized, valid `T` values and read permission. Writes require write permission and must obey initialization and replacement rules. All accesses must respect reference, aliasing, and data-race rules.

```kimi
let pointer: unsafe/i32 = obtainPointer()
unsafe:
    let value = *pointer
    *pointer = 10
pointer = other // Error: the let binding cannot be reassigned.
```

Binding mutability does not determine pointee write permission. Normal Copy, Move, and assignment rules apply. Moving a non-Copy pointee leaves storage uninitialized; the programmer must prevent later reads and double destruction through other pointers or the original owner. The compiler need not identify that owner or suppress its automatic destruction.

```kimi
// Foo is non-Copy; pointer refers to an initialized Foo.
unsafe:
    let value = *pointer // Move Foo.
    use(value)
    // The programmer must prevent destruction of the moved source by its old owner.
```

Replacing an initialized pointee uses normal destruction rules. Initializing uninitialized raw storage requires a separately specified operation; ordinary assignment is not a substitute.

#### 4.6.3. Pointer arithmetic and indexing

For `p: unsafe/T` and `n: isize`, including negative `n`, only these arithmetic and indexing forms are supported:

| Form | Meaning |
| --- | --- |
| `p + n` | Pointer displaced by `n * sizeof(T)` bytes. |
| `p - n` | Pointer displaced by `-n * sizeof(T)` bytes. |
| `p[n]` | The same memory place as `*(p + n)`. |

`p += n` and `p -= n` combine these displacements with [compound assignment](#872-compound-assignment) and require the same unsafe conditions. Pointer increment and decrement are not supported.

`sizeof(T)` includes padding. Arithmetic requires known layout and positive size. Mathematical displacement outside `isize`, or address wraparound, is undefined behavior.

Zero displacement preserves the pointer, including null, but still requires known layout and positive size. Nonzero displacement requires source provenance for a live allocation, with both source and result inside it or one past its end. The result preserves provenance; a coincidentally matching address is insufficient.

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

#### 4.6.4. Pointer conversions

In unsafe context, `@` supports `unsafe/T -> unsafe/U`, `unsafe/T -> usize`, and `usize -> unsafe/T`. Pointer casts within one address space preserve address and provenance without changing memory, initialization, or alignment, or granting access as `U`.

```kimi
unsafe:
    let bytes = pointer@unsafe/u8
    let shifted = bytes + 13
    let typed = shifted@unsafe/i32
    // Access through typed still requires i32 alignment and a valid i32.
```

`unsafe/u8` permits byte-sized arithmetic, not reads of uninitialized memory. A cast itself does not read a pointee or require a valid, aligned value of the destination pointee Type; dereference and access do.

#### 4.6.5. Target and round-trip guarantees

Pointer/integer conversion initially requires a target whose ordinary data addresses fit losslessly in `usize`, whose pointer-address, address-index, `usize`, and `isize` widths agree, and which provides the guarantees below. Multiple address spaces and integer conversion of pointers carrying extra state (such as capabilities) are excluded. Unsupported conversions are compile-time errors; CPU/OS support is target-specific.

Null converts to integer zero, and integer zero converts to null, without requiring an all-zero internal pointer representation. These explicit conversions still require unsafe context.

A round trip through `usize` back to the original pointer Type preserves address and provenance if the pointer-derived integer is unchanged within the same execution and the allocation stays live throughout. The integer may be copied, stored, or passed.

```kimi
unsafe:
    let address = pointer@usize
    let saved = address
    let restored = saved@unsafe/i32
    // Preserves address and provenance under the round-trip conditions.
    // Initialization and access permissions must still hold when accessing memory.
```

Conversion neither extends lifetime nor restores permissions. Arithmetic results, coincidentally equal integers, and addresses from another execution have no round-trip provenance guarantee. Supported targets allow arbitrary integer-to-pointer casts, but using the result where provenance is required needs an additional applicable target guarantee.

#### 4.6.6. Backend and separately specified operations

`ptr` is not a Primitive Type. LLVM `ptr` is a backend representation; instructions supply the Types needed for memory access and arithmetic. Lowering must preserve this specification and use properties such as `inbounds` only when their premises hold. Language undefined behavior and LLVM poison are distinct concepts.

Raw pointer acquisition APIs, allocation and deallocation, initialization of raw storage, conversion to or from safe references, ownership acquisition, and Unsafe Function Types are specified separately. Example functions such as `obtainPointer` and `use` are illustrative, not standard API declarations.

## 5. Declarations

Kimigayo uses the following information to identify declarations and their meaning:

| Element     | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| `Name`      | The basic human-readable name used to refer to a declaration          |
| `Signature` | The information that distinguishes declarations in the same scope     |
| `Type`      | The meaning of a value or invocation within the type system           |

### 5.1. Signatures

A **Signature** distinguishes declarations in the same scope:

| Declaration kind | Signature information                                                 |
| ---------------- | --------------------------------------------------------------------- |
| Type             | Type Semantics, Name, and generic parameter count                     |
| Function         | Name, generic parameter count, and an ordered list of parameter Signatures |
| Parameter        | Type                                                                  |
| Property         | Name                                                                  |

Parameter names, return Types, default values, and declaration modifiers do not affect a function Signature. Same-name functions in one scope must differ in generic parameter count or parameter Types. Same-name Properties in one scope conflict.

The current code defines these Signature shapes, but duplicate-declaration checks and overload Binding are not implemented.

### 5.2. Declaration Containers

A **Declaration Container** is a named declaration scope whose body may contain Properties, functions, Contract Clauses, or nested Declaration Containers as permitted by its kind. Its body is delimited by indentation.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Properties, functions, and nested Declaration Container declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts Properties and functions in declaration order. Generic parameters, Origins, and a Type Contract are supported. |
| `enum` | Yes | Body parsing is not implemented. |
| `extension` | No | Its Name identifies the target. Body parsing is not implemented. |
| `contract` | No | Specifies associated-type Contract Clauses and Property requirements. The Parser preserves required accessors without generating implementations or storage. |

#### 5.2.1. Root and nested containers

Each source unit has an implicit root `group`. Named Declaration Containers are stored there. Top-level executable syntax, including bindings (`let` and `var`), statements, expressions, and functions, is stored in an implicit generated function owned by the Kotonoha. A `rootgroup` declaration starts at the root and accepts a dot-separated Name. For example:

```kimi
rootgroup A.B
    var value = 1
```

creates the nested group path `A.B`. Ordinary `group` bodies accept nested Declaration Container declarations. `struct` bodies do not currently accept nested Declaration Containers.

An `alias` is a top-level declaration of a qualified Name. Nested aliases are invalid.

### 5.3. Type Contracts

A **Type Contract** is a set of **Contract Clauses** of the form `subject is requirement`. All clauses must hold. They constrain Core Types, Type Semantics, or `Self` (the enclosing Type) and establish capabilities the implementation may use. Each declaration kind restricts the permitted subjects; see [function Type Contracts](#552-function-type-contract).

Core Type requirements may name a capability declared with `contract` or another compile-time type capability. Type Semantics requirements may name concrete semantics, such as `ref` or `obj`, or a semantics category. Requirements combine with `and`, `or`, `not`, and parentheses under the [requirement-expression rules](#532-requirement-expressions).

A `struct` header may contain generic parameters and an Origin list. Its Type Contract precedes Properties and functions.

```kimi
struct Container<s/T> origin owner, source
    T is Comparable
    s is reference

    var value: s/T
```

These clauses constrain Core Type parameter `T` and Semantics parameter `s`. A contract may also constrain the enclosing Type:

```kimi
struct ComparableContainer<T>
    T is Comparable
    Self is Comparable

    var value: T
```

`T is Comparable` supplies comparison capabilities for the stored value's Type. `Self is Comparable` requires `ComparableContainer<T>` itself to fulfill `Comparable`; it does not automatically derive an implementation from the clause on `T`. The members needed to fulfill that requirement are omitted from this example. Semantic validation of these requirements is planned.

A Type Contract is a set of conditions, distinct from the `contract` Declaration Container that declares a named capability such as `Comparable`.

#### 5.3.1. Associated Types and Property requirements

Inside a `contract`, `associate` introduces an associated-type Contract Clause, and `has` declares required Property accessors:

```kimi
contract Sequence
    associate Element is Comparable
    var count: i32 has get
```

See [contract Property requirements](#642-contract-property-requirements) for accessor conformance.

#### 5.3.2. Requirement expressions

`subject is requirement` tests requirements on a Type or Type Semantics. Its result is a compile-time `bool`; unresolved requirements must not be deferred to runtime. Type Contracts and compile-time directives apply their own subject and narrowing rules. Runtime `value is T`, pattern bindings, and runtime type narrowing are not defined.

`is` binds on its left at comparison precedence. Its right side consumes a requirement expression through `or` precedence. An immediately following `not` negates that entire right side:

| Form | Meaning |
| --- | --- |
| `T is A and B` | T satisfies both A and B. |
| `T is A or B` | T satisfies A or B. |
| `T is not A or B` | T does not satisfy `(A or B)`. |
| `T is A and not B` | T satisfies A and does not satisfy B. |

Use `(T is A) and enabled` to combine a complete test with another condition. Use `T is (not A) or B` to limit negation to A. Value equality uses `==`.

### 5.4. Bindings

Properties and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` declares a mutable binding. A Type annotation and an initializer are independently optional when the omitted information can be inferred.

```kimi
let limit: i32 = 10
var current = 0
```

### 5.5. Functions

A function begins with `func`, followed by its Name, optional generic parameters, optional Origin parameters, and a parenthesized parameter list. An optional result Type follows `->`. A definition has an indentation-delimited Block body or a single expression introduced by `=>`.

#### 5.5.1. Function bodies and results

A **Block-bodied function** requires explicit `return` for non-Unit results. Every direct expression, including the last, uses Discard Context regardless of trailing semicolons. Nested Value Contexts, such as initializers, retain their usual rules.

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

A final `if`, `match`, or `loop` also discards its own result. Use `return if ...`, `return match ...`, `return loop ...`, or explicit returns on the appropriate paths.

An **Expression-bodied function** evaluates the expression after `=>` in Value Context and uses its normal result as the function result. A `return` executed inside that expression may also supply the function result. A trailing semicolon does not suppress the implicit result.

```kimi
func add(left: i32, right: i32) -> i32 => left + right
```

Both forms follow the shared [result validation](#98-result-validation), [reachability](#982-reachability), and [scope-exit destruction](#102-scope-exit-destruction) rules. [Function Boundaries](#953-function-boundaries) lists the other bodies to which these rules apply.

#### 5.5.2. Function Type Contract

A generic Block-bodied function may begin its body with a [Type Contract](#53-type-contracts). Its Contract Clauses must precede every executable body item and are processed at compile time; they are not executable expressions.

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

Each clause subject must name a generic parameter of that function. In this example, the two clauses jointly form its Type Contract; requirement syntax follows the shared [Type Contract](#53-type-contracts) rules.

Every explicit or inferred generic argument at a call site must satisfy its clauses. Body type checking and specialization may rely on those requirements. A Type Contract is not part of the function Signature; declarations differing only in their contracts conflict.

The current Parser stores leading Contract Clauses separately from executable body items and preserves deferred directives on them. It checks clause subjects against the declared generic parameters and diagnoses clauses placed after executable items. Type Contract validation during Binding and specialization is planned.

#### 5.5.3. Unsafe functions

An **unsafe function**, declared with `unsafe func`, requires its caller to satisfy documented memory-safety conditions for their documented duration. Calling it requires an [Unsafe Block](#933-unsafe-block); violating its safety contract is undefined behavior. This runtime safety contract is distinct from a Type Contract and its Contract Clauses.

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

`unsafe` does not affect the Signature or distinguish overloads. Resolve overloads without considering the caller's unsafe context, then check the selected call's requirement. Never substitute another overload because the selected function is unsafe.

Initially, unsafe functions support direct calls only. Taking a function value, assigning it to a variable, passing it as an argument, or converting it to an ordinary Function Type is forbidden. Unsafe Function Types are specified separately.

```kimi
let reader = read // Error: an unsafe function cannot be taken as a function value.
```

#### 5.5.4. Parameter names and defaults

A parameter may separate its external argument name from its local name with `external => internal: T`. An optional parameter uses `name?: T = defaultExpression`. The `?` requires a default and means argument omission, not a nullable Type; a default alone does not make a parameter optional.

At each call, evaluate omitted defaults once in parameter declaration order, after all explicit arguments. Resolve default expressions in the declaration's scope. They may refer to preceding parameters, but not later parameters or caller-local bindings.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
let result = scale(3, by: 4)

func offset(value: i32, by?: i32 = 1) -> i32 => value + by
let next = offset(3)
let adjusted = offset(3, by: 5)
```

Function Types retain neither argument names nor defaults. Calls through function values supply all arguments positionally. See [invocation](#842-invocation-and-generic-application) for argument matching and evaluation.

## 6. Properties

A **Property** is Kimigayo's only value-bearing member kind. Storage slots, global storage, and other lowered representations are implementation details. A `let` or `var` inside an executable Block is a local binding.

A Property has a **Property Type**, a **Getter Result Type**, a getter, an optional setter, and optional owned storage. The Property Type determines the storage Type and setter input Type, even for a computed Property with no storage. Reads have the Getter Result Type, which may differ from the Property Type.

Storage classification follows this sequence:

```text
Expand inline has declarations
    -> expand bodyless and implicit accessors
    -> bind the effective accessors and contextual identifiers
    -> detect references bound to storage
    -> determine HasStorage
```

`HasStorage` is true exactly when an effective accessor contains a reference bound to contextual `storage`. Expression and Block bodies both count, including unreachable references. During binding, `storage` is provisionally available; a bound reference causes storage to exist. An ordinary Name spelled `storage` outside an accessor does not count.

- A **Stored Property** has `HasStorage = true` and owns one location: part of the instance layout for an instance Property, or static storage for a static member of a `group`.
- A **Computed Property** has `HasStorage = false` and contributes no storage slot.

The Parser records Properties, inline and block accessors, explicit getter result annotations, and basic syntax errors. Control-flow analysis checks known accessor result Types. Accessor expansion, contextual binding of `self`, `storage`, and `value`, storage classification, access and initialization checks, and general accessor type checking are planned.

### 6.1. Effective representation

Expand the declaration before classifying storage:

| Source form | Effective getter | Effective setter | Classification |
| --- | --- | --- | --- |
| `let x: T` | Default storage read | None | Stored |
| `var x: T` | Default storage read | Assign `value` to `storage` | Stored |
| `var x: T` with only explicit `get` | Explicit getter | None | Depends on `storage` use |
| `var x: T` with only explicit `set` | Default storage read | Explicit setter | Stored |
| `var x: T` with explicit `get` and `set` | Explicit getter | Explicit setter | Depends on `storage` use |

A default storage read uses the [default getter rules](#62-default-getter-results): it copies or shared-borrows, never moves. It contains a bound `storage` reference and is equivalent to `get => storage` only when Copy is permitted.

An explicit setter retains the default getter:

```kimi
var age: i32 = 0
    set
        storage = max(value, 0)
// Effective getter: get => storage
```

An explicit getter suppresses unwritten default accessors. This Property is computed and read-only:

```kimi
var count: i32
    get => items.count
```

An initializer does not determine classification; it is valid only if the effective Property has storage. `let` is restricted to immutable stored data. Its standard representation has a default storage-reading getter and no setter. Read-only computed Properties use `var` with an explicit getter.

### 6.2. Default getter results

An omitted or bodyless getter uses the complete Property Type to select a Copy or shared-borrow operation. It never moves from the instance, implicitly duplicates object ownership, or returns an exclusive borrow. [Copy and Move](#71-copy-and-move) defines Copy capability.

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

A default instance getter's borrow or reborrow has Origin `from self`, tied to the current receiver Loan even if storage carries a longer Origin. A shared reborrow suspends conflicting access through the stored exclusive reference while the result is live. A Copy preserves the value's existing Origin dependencies without extending them or replacing them with `self`.

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

Reading `parent.child` borrows the object; assignment passes ownership to the setter. Ownership extraction requires a separate operation, such as consuming the containing value or explicitly replacing storage, subject to Move rules. No extraction syntax is defined here.

A stored `let value: uniq/T from source` similarly returns `ref/T from self`, leaving the exclusive capability in storage. Shared inspection does not make the containing structure Copy.

For generic Properties, unresolved Type Semantics or Copy capability leave the default rule dependent. Binding may use a result Type only once constraints or specialization establish its row, and must resolve the operation before finalizing a specialization. An unconstrained Core Type parameter is not assumed Copy. Generic Copy-constraint syntax remains unspecified.

### 6.3. Accessors

A getter defines a read and follows the [function body and result rules](#551-function-bodies-and-results), with the Getter Result Type as its Target Result Type:

```kimi
var area: f64
    get => width * height

var loggedArea: f64
    get
        logRead()
        return width * height
```

A computed getter must explicitly start with `get`; a bare expression in the Property body is invalid.

`get -> ResultType` specifies the Getter Result Type, including any Origin annotation. Without it, even a custom getter uses the [default result Type](#62-default-getter-results); its body does not infer a different Type. Omitted result Origins on custom getters follow function Origin elision.

```kimi
var child: obj/Node
    get -> objref/Node from self => storage@objref

var freshNode: obj/Node
    get -> obj/Node => Node.new() // Assumes new returns obj/Node.
```

A result annotation does not permit moving borrowed storage: `get -> obj/Node => storage` is invalid for stored `obj/Node` because the receiver is `ref/Self`. Custom bodies must explicitly perform required borrows or reborrows; only omitted or bodyless getters synthesize a default read.

A bodyless getter with an explicit result annotation still performs the default read. The result must satisfy the annotated Type and Origin without moving storage or implicitly duplicating ownership. An incompatible annotation is an error, and annotations do not relax `let` restrictions.

A setter defines a write. Its implicit input `value` has the Property Type; source uses `set`, never `set value`:

```kimi
var percentage: i32 = 0
    set
        storage = clamp(value, 0, 100)

// obj.percentage = 120 invokes the setter with value = 120.
```

Reads invoke the getter; assignments after initialization invoke the setter and are invalid if none exists. Accessors may operate on other state:

```kimi
var width: f64
    get => right - left

    set
        right = left + value
```

Neither accessor uses `storage`, so this Property is computed and read-write.

### 6.4. Inline accessor declarations

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

#### 6.4.1. Concrete Properties

For a concrete Property, `has` expands to bodyless accessor declarations before storage classification. A bodyless getter performs the default read; a bodyless setter assigns `value` to `storage`. For example:

```kimi
var count: i32 has get, private set
```

expands through `get` and `private set` to:

```kimi
var count: i32
    get => storage

    private set
        storage = value
```

`has` adds no storage rule: normal bound `storage` references determine `HasStorage`. Thus `var value: i32 has get` is stored and read-only.

Inline and indentation-delimited accessor lists cannot be combined. Use the latter for custom bodies. Normal `let` restrictions apply, including the prohibition on setters.

#### 6.4.2. Contract Property requirements

Inside a `contract`, `has` declares the accessor capabilities that a conforming Property must provide:

```kimi
contract Collection
    var count: i32 has get

contract MutableCollection
    var count: i32 has get, set
```

Conformance requires a compatible Property Type and all required accessors with sufficient accessibility. A getter must also satisfy the required Getter Result Type and Origin contract; a setter accepts the required Property Type under normal parameter compatibility rules.

A contract getter requirement may specify a result Type, for example `var child: obj/Node has get -> objref/Node from self`. Without an annotation, the required Getter Result Type follows the same default result rules as a concrete Property, but no storage read or Loan is generated by the requirement itself. Here `self` denotes the required shared receiver. A conforming getter must provide a result compatible with the required result Type for every legal receiver Origin; it cannot impose a shorter lifetime than the requirement promises. Generic requirements retain unresolved default result rules until the applicable constraints or specialization determine them.

A contract requirement creates no implementation, storage read, Loan, effective storage representation, or `HasStorage` classification. Both a stored `var count: i32 has get` and a computed `get => items.count` may satisfy the readable requirement. In concrete declarations, `has` defines bodyless accessors; in contracts, it specifies required capabilities only.

### 6.5. Contextual identifiers and receivers

`storage` denotes the Property's actual owned location, not a copy. It has the Property Type, and a bound use causes the location to exist. `value` is available only in setters. These Names are not globally reserved.

Instance accessors have these implicit signatures:

```text
get(self: ref/Self) -> GetterResultType
set(self: uniq/Self, value: PropertyType) -> ()
```

The getter has shared/read access to the instance and its storage; the setter has exclusive/read-write access. Receivers do not change the Type of `storage` to `ref/T` or `uniq/T`. The Getter Result Type separately describes the read or borrow result.

Mutable or exclusive getter receivers are not supported. Static Properties, including `group` members, have no instance receiver.

### 6.6. Initialization

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

### 6.7. Access control

Accessors inherit the Property's accessibility unless they declare a stricter restriction. An accessor cannot be more accessible than its Property. Restrictions do not change a bodyless accessor's default implementation, whether inline or in the Property body:

```kimi
public var count: i32 = 0 has get, private set
// Public default getter; private default setter.

private var value: i32
    public set // Error: broader access than the Property.
```

### 6.8. Storage, addressability, and result semantics

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

## 7. Ownership and lifetimes

### 7.1. Copy and Move

Taking a stored value copies it if its Type is Copy; otherwise it moves the value if permitted, or is rejected. This applies to initialization, assignment sources, by-value arguments, and explicit or implicit result transfers (`return`, `exit`, and `yield`). Borrow creation and reborrowing have separate rules; naming a location does not always consume it.

**Copy** names a Type capability, without requiring a particular trait system. Ownership checking is planned, not implemented.

#### 7.1.1. Operation semantics

**Copy** duplicates the value representation, leaving the source initialized and usable with unchanged destruction responsibility. It performs no user-defined operation, deep allocation copy, reference-count increment, or resource acquisition. A reference Copy does not copy its referent.

**Move** transfers the value, ownership, and destruction responsibility. Until reinitialized, the source cannot be read, borrowed, copied, or moved again. Moving a borrow transfers access capability without owning the referent. Move invokes no user-defined operation and need not clear source memory.

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

#### 7.1.2. Copy classification

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

Copy opt-in requires all stored components to be Copy, no custom destruction, no active `uniq` Loan requirement, and representation duplication that preserves ownership and borrowing. All-Copy components permit but do not imply opt-in. Computed Properties contribute no stored components.

The Copy classification of owned `string` remains open until its ownership representation is settled; an owning, self-releasing UTF-8 buffer is non-Copy. Slice classification follows its borrowing representation: sharing elements does not itself establish Copy capability.

#### 7.1.3. Borrowing and initialization

Copy is a read of the source and must satisfy the active Loan rules. Move cannot take a value from a place overlapped by an active Loan. Neither operation bypasses borrowing restrictions.

A non-Copy referent cannot be moved out through `ref/T`, `uniq/T`, `objref/T`, or `objuniq/T` and leave the borrowed place uninitialized. Extraction with a valid replacement requires a separate operation, not defined here.

Copied or moved borrow values retain their Origin constraints; neither operation extends the referent's lifetime. Reborrowing is distinct from Copy and does not make an exclusive borrow Copy.

A moved place may be reinitialized when ordinary write rules permit it. At a control-flow join, a subsequent use requires initialization on every incoming path that reaches the use.

Partial Move support remains open. If permitted, initialization and destruction responsibility must be tracked per part. Ordinary Property reads invoke accessors and are not direct Moves from backing storage.

#### 7.1.4. Destruction and explicit duplication

[Assignment](#87-assignment) resolves its destination before securing its source value, and destroys the destination's initialized old value before storing the replacement. An uninitialized destination has no old value to destroy. [Scope-exit destruction](#102-scope-exit-destruction) destroys only initialized values for which the scope retains responsibility; a moved value is not destroyed again at its source.

Duplication requiring additional work must be explicit. In particular, duplicating an owning `rc/T` or `arc/T` reference increments a reference count and is not Copy; ordinary by-value transfer uses Move.

The syntax for Copy opt-in, automatic derivation, generic Copy constraints, and explicit duplication remains open. A future trait system may express these capabilities, but its design and the relationship between Copy and a duplication trait are not specified here.

### 7.2. Origins and Loans

Kimigayo uses **Origins** instead of lifetime variables. An Origin describes how long a borrow remains valid; a **Loan** records which place is borrowed and whether the borrow is shared or exclusive.

The Parser supports Origin lists on structures and functions, simple and qualified annotations, intersections, and named arguments. Origin name resolution, inference, variance analysis, and borrow checking are not implemented.

Origin annotations appear in signatures and type declarations. Origins inside function bodies are inferred. When an annotation is omitted, conservative elision rules apply.

The safe value-borrow semantics are:

```kimi
ref/T from o   // shared, immutable, and aliasable
uniq/T from o  // exclusive and mutable
```

`uniq/T` is not implicitly copyable and cannot coexist with another overlapping borrow. The corresponding object-borrow semantics, `objref/T` and `objuniq/T`, follow the same shared and exclusive rules. This section uses `ref` and `uniq` in examples.

When `from o` is omitted, the Origin elision rules below determine the Origin.

#### 7.2.1. Origin expressions

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

#### 7.2.2. Ordering and intersection

`o1 : o2` means that `o1` outlives `o2`: `region(o1) ⊇ region(o2)`.

The relation is reflexive and transitive. `static` outlives every Origin.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

Consequently, `o1 and o2` never outlives either operand. A result declared `from x and y` is valid only in the region common to both inputs.

#### 7.2.3. `static` and `Owned`

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

### 7.3. Abstract Origins

Functions and types may declare abstract Origin parameters separately from type parameters:

```kimi
func unwrap<T> origin s(v: View<T> from (source => s))
    -> ref/T from s

struct View<T> origin source
    let value: ref/T from source
```

Function Origins are universally quantified. Origin parameters occupy a namespace distinct from type parameters.

#### 7.3.1. Origin arguments

Named Origin arguments use `from (...)` and `=>`:

```kimi
struct Pair<A, B> origin left, right
    let a: ref/A from left
    let b: ref/B from right

Pair<A, B> from (
    left => a,
    right => b)
```

Named argument lists require parentheses, even for one argument. For a Type with exactly one Origin, `View<T> from v` abbreviates `View<T> from (source => v)`.

#### 7.3.2. Variance

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

#### 7.3.3. Loan requirements

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

### 7.4. Origin elision and return contracts

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

#### 7.4.1. Return contracts

A declared return Origin limits the dependency visible to callers without requiring a borrow from that specific input. Every explicit or implicit result, including unreachable ones, must subtype the declared result Type under [result validation](#98-result-validation) and [reachability](#982-reachability).

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

### 7.5. Exclusive Origins

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

One valid form consumes the Origin-bearing owner:

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

### 7.6. Borrow checking

Function bodies are lowered to a control-flow graph. A **program point** is a position immediately before or after an operation. A **place** is a memory location holding a value; being a place does not itself grant write permission. The lowered representation uses these projections:

```text
place := local
       | place '.' Name
       | place '.' TupleIndex
       | '*' place
       | place '[' _ ']'
```

These projections describe storage, including lowered slots, rather than granting direct access to source-level Property storage. [Properties](#68-storage-addressability-and-result-semantics) use accessors. Parentheses preserve a place. Reading a place copies, moves, or borrows according to the required Type and access permissions.

A **region** is a set of program points. Local regions are inferred; Origins in signatures introduce universal regions; `static` is the maximum region.

A Loan is:

```text
Loan = (place, mode, region)
mode = ref | uniq
```

It is active at program point `P` exactly when `P` belongs to its region. Regions follow actual uses rather than lexical scope, providing non-lexical lifetimes:

```kimi
let r = x@ref
use(r)
x.mutate()       // Allowed: r is no longer live.
```

#### 7.6.1. Constraints

Type checking generates these constraints:

| Constraint      | Rule                                                         |
| --------------- | ------------------------------------------------------------ |
| Subtyping       | Assignment and argument passing require `type(value) <: type(destination)`. |
| Liveness        | If a value containing `o` may be used after `P`, then `P` belongs to `region(o)`. |
| Outlives        | `a : b` requires `region(a) ⊇ region(b)`.                    |
| Well-formedness | Every Origin in `T` observable through `ref/T from o` or `uniq/T from o` must outlive `o`. |
| Calls           | Origin arguments and result Loan requirements are instantiated as described under Calls and Origin propagation. |

The well-formedness rule prevents borrowed contents from expiring before the outer borrow.

#### 7.6.2. Place overlap and conflicts

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

#### 7.6.3. Reborrowing

Borrowing through an exclusive borrow creates a child Loan. While the child is live, the parent remains live but access through it is suspended. Overlapping access is rejected by the normal conflict rules.

```kimi
func bump(n: uniq/i32)

var v = 0
bump(v@uniq)
bump(v@uniq)
```

Each call creates a temporary reborrow; the first ends before the second starts.

#### 7.6.4. Calls and Origin propagation

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

#### 7.6.5. Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

```kimi
func bad(x: ref/T) -> ref/T from x
    let local = T.new()
    return ref/local       // Error
```

#### 7.6.6. Drop checking

The [scope-exit destruction rules](#102-scope-exit-destruction) determine which values are destroyed and in what order. At each destruction point, an Origin must remain live only when destruction may observe a value carrying that Origin.

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

#### 7.6.7. Reference algorithm

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

### 7.7. Deferred lifetime features

This revision does not define:

- abstract Origin parameters on contracts or trait-like abstractions (the Property getter receiver/result contracts above do not introduce contract-level Origin parameters);
- default Origins for trait objects;
- higher-ranked Origins;
- borrow escape into heap or global storage;
- lending iterators;
- destructor dangling relaxation.

These features require extensions to the core rules above and must not be inferred from this revision.

### 7.8. Temporary lifetimes

Unless a construct needs a longer lifetime, an owned temporary lasts until evaluation of the outermost expression that created it ends. Destroy remaining temporaries in reverse creation order. Argument temporaries last through the call. Values moved into bindings or results follow the destination's lifetime. Iteration sources and `match` subjects last for their required use by the construct.

A borrow does not extend its source's lifetime. A Slice of a temporary array cannot survive destruction of that array. Transfers secure results before [scope-exit cleanup](#102-scope-exit-destruction); [Panic Termination](#103-panic-termination) provides no cleanup guarantee.

## 8. Expressions and operators

This chapter defines expression syntax and intended semantics. Parsing a form does not imply that its name resolution, type checking, or runtime behavior is implemented; see [implementation status](#89-implementation-status).

### 8.1. Classification and contexts

This source-language classification is independent of the internal Koto inheritance hierarchy:

```text
Expressions
├─ Primary Expression
│  ├─ Name
│  ├─ Literal
│  │  ├─ Number / Boolean / Character / String / Null / Unit
│  │  ├─ Interpolated String
│  │  └─ Tuple / Array / Dictionary
│  ├─ Parenthesized Expression
│  └─ Function Expression
├─ Member Access
├─ Application
│  ├─ Invocation (including $panic(...))
│  └─ Generic Application
├─ Index / Slice Expression
├─ Conversion Expression
├─ Unary Expression
│  ├─ Sign / Logical Negation / Dereference / From-end Index
│  └─ Prefix / Postfix Increment and Decrement
├─ Binary Expression
│  ├─ Arithmetic / Shift / Bitwise
│  ├─ Comparison / Logical
│  └─ Type Requirement Test
├─ Range Expression
├─ Assignment Expression
│  ├─ Simple Assignment
│  └─ Compound Assignment
├─ Selection Expression: if / match
├─ Iteration / Labeled Block Expression: for / while / loop / Label:
└─ Control Transfer Expression: return / exit / continue / yield

Related Syntax
├─ Block Statement: unsafe: / defer:
├─ Compile-time Directive: #if / #case
├─ Attribute: #Name
└─ Composition Root: $
```

A normally completing expression produces a typed result, including [Unit](#415-unit-and-never-types). [Value and Discard Contexts](#92-blocks-and-evaluation-contexts) determine how that result is used. Discarding it preserves side effects and type, ownership, and destruction checks. Assignment requires a writable [place](#76-borrow-checking) or an accessible Property setter; a readable Property need not expose borrowable storage.

An ordinary indented Block is a syntax container, not an arbitrary value expression. Use a selection or Labeled Block to obtain a value from several operations. `unsafe:` and `defer:` are statements and cannot be initializers or arguments. `let` / `var` declarations are not general expressions; their use in `if` / `while` conditions follows the dedicated [condition syntax](#972-if).

#### 8.1.1. Delimiters and line breaks

Newlines and `;` separate expressions; commas separate arguments or elements and are not binary operators. Indentation rules still apply within `()` and `[]`: indent continued arguments and elements one level, and optionally align the closing delimiter with the opening line. A method-chain continuation starting with `.` also uses one extra indentation level.

This specification does not allow arbitrary binary operators at the start of a line to continue the previous line. `:`, `=>`, `->`, and `in` are delimiters for their respective constructs, not general binary operators. A trailing semicolon does not change result or evaluation-context rules.

### 8.2. Evaluation order

Evaluate operands once, from left to right, unless a construct specifies conditional evaluation. Precedence determines grouping; evaluation order determines the order of effects.

| Expression | Evaluation order |
| --- | --- |
| `a() + b() * c()` | a, b, c, multiplication, addition. |
| `receiver().method(a(), b())` | Receiver, resolve callee, a, b, call. |
| `array()[index()]` | Target, index, element access. |
| `(a(), b())` / `[a(), b()]` | Elements in source order. |
| `[key(): value(), ...]` | Per entry: key, duplicate check, value, insertion. |
| `start()..end()` | Start boundary, end boundary. |
| `"\(a()) / \(b())"` | Evaluate and stringify each interpolation in source order. |

`and`, `or`, and selections evaluate only the required operands or branches. [Assignment](#87-assignment) resolves its target before evaluating its source. Type arguments and conversion target Types are not evaluated at runtime.

An abrupt Completion, divergence, or Panic prevents evaluation of later operands and the enclosing operation. Unevaluated syntax still undergoes name, Type, and transfer-target checks; syntax excluded by `#if` / `#case` follows [conditional compilation](#22-compile-time-directives). [Temporary lifetimes](#78-temporary-lifetimes) and scope-exit rules govern retained values.

### 8.3. Primary expressions

#### 8.3.1. Type inference

Expected Types from declarations, parameters, and results propagate into expressions. Otherwise infer from operands. Ordinary numeric operations require the same numeric Type; integer widths, signedness, and integer/floating-point Types do not mix implicitly.

An untyped integer literal adopts an expected integer Type if its value fits. Without one, it defaults to `i32`; a value outside that range requires an explicit Type. A floating-point literal adopts an expected `f32` or `f64`, defaulting to `f64`. Check a directly negated integer literal as a signed value, allowing the minimum of a signed Type.

```kimi
let a: i64 = 10
let b = a + 20          // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64       // Convert an already typed operand explicitly.
let minimum: i8 = -128
```

There are no implicit conversions between `bool`, `char`, and numbers. Conditions require `bool`, not an integer or pointer. Borrowing and reborrowing are separate adaptations governed by ownership rules.

#### 8.3.2. Names, literals, and grouping

| Form | Meaning |
| --- | --- |
| `name` | Reference to a visible binding, function, or other named entity. |
| `123`, `0xff`, `1.5`, `true`, `'あ'`, `"text"` | Scalar literals; see [lexical structure](#3-lexical-structure). |
| `"value = \(value)"` | Interpolated string; the embedded Type must support stringification. |
| `null` | Contextually typed [raw null pointer](#461-null-and-equality). |
| `()` | Unit value. |
| `(value)` | Grouped expression; preserves a place. |
| `(value,)`, `(a, b)` | One-element or multi-element Tuple. |
| `[a, b]`, `[]` | Array literal. |
| `[key: value]`, `[:]` | Dictionary literal. |

Tuples may have different Types at each position. An array has one element Type; a dictionary has one key Type and one value Type. Empty collection literals need an expected Type. Concrete collection Types and storage are defined by the library; ambiguity does not fall back to a universal object Type.

```kimi
let pair = (10, "ten")
let single = (10,)
let values = [10, 20, 30,]
let names = [1: "one", 2: "two",]
let message = "first = \(values[0])"
```

#### 8.3.3. Dictionary construction and duplicate keys

Equivalent duplicate keys are errors. A duplicate detectable as a constant is a compile-time error. Otherwise, process each entry in source order:

1. Evaluate its key once and retain it.
2. Check for an equivalent key among entries already inserted.
3. On a duplicate, initiate implicit Panic Termination before evaluating this entry's value or any later entry.
4. Otherwise evaluate its value once.
5. Insert the retained key and resulting value.

Equivalence follows dictionary key equality; matching hash values alone do not make keys duplicates. The key Type must guarantee that the logical equality and hash value of a stored key remain unchanged while the dictionary holds it. Later entries never overwrite existing values.

A candidate key's logical equality and hash value must also remain unchanged from the start of duplicate checking through completion of insertion, including evaluation of its value expression.

```kimi
let x: i32 = getKey()
let map = [x: first(), x: second()]
```

When checked at runtime, the first entry is evaluated and inserted before checking the second key. That check fails, so `second()` is not called.

If key evaluation, duplicate checking, or value evaluation does not complete normally, do not insert that entry or process later entries. No partially constructed dictionary is returned, and completed side effects are not rolled back. The duplicate's diagnostic location is the later key expression. Termination and cleanup follow [Panic Termination](#103-panic-termination).

### 8.4. Access and application

#### 8.4.1. Member access

`expression.name` selects a member. Name resolution distinguishes a Declaration Container qualification such as `Group.name` from value access such as `object.name`. The right side of `.` must be a member Name or an in-range decimal integer literal selecting a Tuple element; `pair.0` selects its first element. Dynamic member lookup with an arbitrary expression is not defined.

Check accesses using the [Property](#6-properties) getter result Type, setter availability, and receiver permissions. Raw pointers do not dereference automatically: write `(*pointer).name` in an Unsafe Block.

```kimi
let count = collection.count
collection.count = 10   // Requires a setter and write permission.
let first = pair.0
```

#### 8.4.2. Invocation and generic application

`callee(arg1, arg2)` invokes a function, method, or function value. Zero arguments and a trailing comma are allowed. `callee<T, U>(args)` applies explicit type arguments before calling.

Match positional arguments first, then named arguments written `name: expression` using external parameter names. Named arguments may appear in any order, but evaluate in source order. Unknown names, duplicate bindings to a parameter, and missing or excess arguments are errors. Omitted optional arguments follow [parameter defaults](#554-parameter-names-and-defaults).

Arguments, type arguments, and constraints must select a unique overload; declaration order never breaks a tie. Return Types alone do not distinguish overloads. Function values use the positional calling rules in the parameter section, and unsafe calls retain their [additional restrictions](#553-unsafe-functions).

In an expression, `<` introducing type arguments must be adjacent to the target name and have a matching `>`. Thus `f<T>(x)` applies type arguments while `a < b` compares values. Nested type arguments may split `>>` into two closing delimiters. Use spaces around comparison operators to avoid ambiguity.

Type-argument inference and specialization, constant type arguments, and user-defined construction conventions require further implementation or specification. This syntax does not automatically make every `T(args)` a valid construction.

#### 8.4.3. Function expressions

An anonymous function uses `func (parameters) -> Result => expression` or an indented body. Creating it does not execute its body; invocation does. Results follow [function body rules](#551-function-bodies-and-results).

```kimi
let twice = func (value: i32) -> i32 => value * 2
let result = twice(5)
```

Initially, function values cannot capture outer local bindings. Diagnose capture until capture lists, Copy/Move behavior, borrow lifetimes, and calling capabilities are specified together. Do not implicitly copy captured bindings or extend their lifetimes.

#### 8.4.4. Indexing and slicing

`value[index]` selects an element; `value[range]` produces a Slice. [Index, Range, and Slice](#44-index-range-and-slice) defines sequence boundaries and lifetimes. [Raw pointer indexing](#463-pointer-arithmetic-and-indexing) instead uses signed `isize` displacements, permits negative offsets, and has no length check; From-end Indices and Ranges are forbidden.

Dictionary indexing is a separate operation: reading `dictionary[key]` requires an existing key and initiates implicit Panic if it is absent. Fallible lookup, insertion, and user-defined indexer declarations require separate library rules. Integer indexing into a `string` does not yet select a character; the specification must first choose byte, Unicode scalar, or grapheme indexing.

### 8.5. Precedence and associativity

Earlier rows bind more tightly. Left associativity groups `a op b op c` as `(a op b) op c`; right associativity groups it as `a op (b op c)`. Grouping does not guarantee type correctness or change evaluation order.

| Level | Operators or syntax | Association |
| --- | --- | --- |
| 1 | `.name`, `(...)`, `<Types>`, `[...]`, postfix `++` `--` | Postfix chain, left to right |
| 2 | Prefix `+` `-` `not` `*` `^` `++` `--` | Right |
| 3 | `@Type` | Left |
| 4 | `*` `/` `%` | Left |
| 5 | `+` `-` | Left |
| 6 | `<<` `>>` | Left |
| 7 | `&` | Left |
| 8 | `^` | Left |
| 9 | `\|` | Left |
| 10 | `<` `<=` `>` `>=` `==` `!=` | Non-associative |
| 11 | `and` | Left |
| 12 | `or` | Left |
| 13 | `..` `..=` | Non-associative |
| 14 | `=`, compound assignments | Right |

`is` has the asymmetric [requirement-expression rule](#532-requirement-expressions): comparison strength on the left, and a requirement through `or` on the right. It is separate from the six value comparisons above. `as` is reserved.

Unparenthesized comparison chains such as `a < b < c`, `a == b == c`, and `a < b == flag` are syntax errors. Write `a < b and b < c` or `(a < b) == flag`; each comparison still requires valid operand Types.

`@` takes a Type and binds less tightly than prefix operators: `-x@i64` means `(-x)@i64`; use `-(x@i64)` to negate after conversion. In particular, `-128@i8` converts the complete negative value. Because a target Type may contain qualified names, access a converted value's member as `(x@T).name`.

Conversion type arguments follow the same adjacent-`<` and matching-`>` rule as generic application: `value@Box<i32>` contains a type argument, whereas `value@i64 < limit` compares the converted value. Selections, iterations, and Labeled Blocks have their own body syntax. `return`, `exit`, and `yield` consume a full result expression, so `return a + b` returns the sum.

| Written form | Grouping |
| --- | --- |
| `flags & mask == 0` | `(flags & mask) == 0` |
| `a \| b ^ c & d` | `a \| (b ^ (c & d))` |
| `1 << n + 1` | `1 << (n + 1)` |
| `a + b << count` | `(a + b) << count` |
| `a + b@i64 * c` | `a + ((b@i64) * c)` |
| `value@i64@f64` | `(value@i64)@f64` |
| `not ready and flags & mask != 0` | `(not ready) and ((flags & mask) != 0)` |
| `a < b and b <= c or done` | `((a < b) and (b <= c)) or done` |
| `start + 1..end - 1` | `(start + 1)..(end - 1)` |
| `target = flags & mask == 0` | `target = ((flags & mask) == 0)` |

### 8.6. Operator semantics

#### 8.6.1. Unary operators

| Operator | Operand and result |
| --- | --- |
| `+value` | Numeric value, unchanged Type and value. |
| `-value` | Negated signed integer or floating-point value. |
| `not value` | Negated `bool`. |
| `*pointer` | Raw-pointer place under [unsafe dereference rules](#462-dereference-and-ownership). |
| `^value` | From-end Index formed from a nonnegative `isize`. |
| `++target` / `--target` | Increment or decrement an integer; return the updated value. |
| `target++` / `target--` | Increment or decrement an integer; return the old value. |

Increment and decrement require a readable, writable integer place or Property; they do not apply to floats, raw pointers, or arbitrary Types. Resolve, read, and write the target once each. Overflow prevents the write. A prefix operation returns its computed value without reading the Property again. These operations follow the target-validity and ownership requirements of [compound assignment](#872-compound-assignment).

```kimi
var count: i32 = 1
let before = count++  // before = 1, count = 2
let after = ++count   // after = 3, count = 3
```

`not` binds more tightly than comparison; negate a comparison as `not (a == b)`. Explicit dereference of non-pointer Types is not defined by this operator.

#### 8.6.2. Arithmetic, bitwise, and shift operators

`+ - * /` take operands of the same numeric Type and return that Type. `%` accepts integers only. Integer division truncates toward zero. On mathematical integers, the remainder satisfies `a = (a / b) * b + a % b`; a nonzero remainder has the dividend's sign.

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010
let shifted = bits << 1     // 0b10100
```

Check integer `+ - *`, unary `-`, increment/decrement, and the arithmetic part of compound assignment for overflow. Integer division or remainder by zero is invalid. Signed minimum divided by `-1`, including `% -1`, is also invalid. These failures follow [Panic Termination](#103-panic-termination), including its constant-evaluation rule.

`& | ^` perform bitwise AND, OR, and XOR on the same integer Type; they do not accept `bool`. `<< >>` return the left operand's integer Type and accept any integer Type on the right, requiring `0 <= shift < bit width of left operand`. An invalid count is a check failure. Left shift discards high bits and inserts zero low bits; right shift sign-extends signed integers and zero-extends unsigned integers. Discarded shift bits are not arithmetic overflow.

Floating-point operations follow IEEE 754 for `f32` / `f64`, using round-to-nearest, ties-to-even. They support infinity, NaN, and signed zero; floating-point division by zero does not use integer failure rules. Do not implicitly reassociate or fuse ordinary operations when rounding or NaN results would change.

`string + string` concatenates without implicit numeric stringification. Raw-pointer arithmetic is limited to the forms and unsafe conditions in [pointer arithmetic](#463-pointer-arithmetic-and-indexing); its undefined-behavior rules are distinct from checked integer arithmetic.

#### 8.6.3. Comparison and logical operators

`== != < <= > >=` return `bool`. Numeric operands must have the same Type. `bool` and Unit support equality only. `char` compares Unicode scalar values. `string` uses UTF-8 byte equality and lexicographic order without normalization or locale processing.

Floating-point `+0.0 == -0.0` is true. With a NaN operand, `== < <= > >=` are false and `!=` is true; floating-point ordering is not total.

Comparisons may borrow their operands and do not Move non-Copy owned values solely to compare them. User-defined comparison requires an explicit Type capability. Safe borrows compare referent values of the same Type using that Type's comparison capability. Tuples support elementwise equality and lexicographic ordering when all corresponding elements support the required comparison.

Value equality and object identity are separate operations; `==` does not implicitly become an address comparison for object Types. Raw-pointer `== !=` are the explicit exception, following [pointer equality](#461-null-and-equality).

| Logical operation | Evaluation |
| --- | --- |
| `left and right` | If left is false, return false; otherwise evaluate right. |
| `left or right` | If left is true, return true; otherwise evaluate right. |
| `not value` | Reverse true and false. |

All logical operands and results are `bool`. User code cannot change short-circuit behavior.

```kimi
let valid = index >= 0 and index < count
let found = valid and matches(values[index])
let clear = flags & mask == 0
```

#### 8.6.4. Explicit conversion

`expression@Type` explicitly converts a value. A Type-Semantics-only target, such as `value@ref`, retains the source Core Type. Infer Origins in runtime expressions; do not write `from origin` here.

| Conversion | Rule |
| --- | --- |
| Integer to integer | Check that the value fits; no truncation or wrapping. |
| Integer to float; float to float | Round to nearest, ties to even. A finite value rounding to infinity fails. Float-to-float preserves NaN and infinity, without guaranteeing NaN payloads. |
| Float to integer | Truncate toward zero, then check that the mathematical integer fits. NaN and infinity fail. |
| Value to a borrow such as `ref` / `uniq` | Require a valid place, access permissions, and Origin; do not extend lifetime. |
| Raw pointer to raw pointer; raw pointer to/from `usize` | Follow [pointer conversion and provenance](#464-pointer-conversions) rules in an Unsafe Block. |

Check failures follow [Panic Termination](#103-panic-termination). Built-in numeric conversions exclude `bool` / `char`, string parsing, and arbitrary bit reinterpretation. Conversion does not acquire ownership from a safe borrow, upgrade shared access to exclusive access, or implicitly increase a reference count.

```kimi
let wide = value@i64
let truncated = 3.9@i32  // 3
let negative = -128@i8
let shared = data@ref
```

`as` is reserved, even though an internal binary node exists. It is neither an alias of `@` nor a defined dynamic cast. Fallible dynamic conversion requires a separately specified result Type.

### 8.7. Assignment

#### 8.7.1. Simple assignment

`target = value` updates its destination and returns Unit, not the assigned value:

1. Evaluate the left receiver and indices from left to right and resolve the destination once. Do not call a Property getter.
2. Evaluate the right side and secure a result of the destination Type by Copy or Move.
3. For a place, destroy its initialized old value and store the new one. For a Property, pass the new value to its setter.
4. Return Unit after the write completes normally.

If the right side does not complete normally, do not write; preceding side effects remain. The destination must remain valid from resolution through writing. Borrow checking rejects conflicting mutations or Moves from the right side. Initialization of an uninitialized local follows definite-initialization rules and is distinct from writing raw uninitialized memory.

```kimi
var count: i32 = 0
count = 10
let done: () = (count = 20)
// A condition such as `if count = 30` fails: assignment returns Unit, not bool.
```

Right associativity parses `a = b = c` as `a = (b = c)`. The inner Unit result makes ordinary chained numeric assignment a type error; use separate assignments. Destructuring and whole-Slice assignment require separate rules.

#### 8.7.2. Compound assignment

`+= -= *= /= %= &= |= ^= <<= >>=` perform the corresponding binary operation and return Unit. Resolve the destination once, read its old value once, evaluate the right side, compute, and write once. This is not a textual replacement with `target = target op value`; receivers and indices are not repeated.

A Property uses one getter and one setter. Its getter result must support the operation, and the computed result must fit the Property Type. Do not insert hidden Moves or duplication to supply missing capabilities. Destination validity and ownership follow simple assignment.

```kimi
values[nextIndex()] += amount() // Index, old value, amount, addition, write.
```

If the right side or operation does not complete normally, do not write; getter and operand effects already performed remain. Compound assignment is not atomic and does not provide synchronization. Raw-pointer `+=` / `-=` use only the permitted displacement operations and their unsafe conditions; other pointer compound assignments are forbidden.

### 8.8. Extension boundaries and reserved syntax

Operator symbols, precedence, and associativity are fixed by the language. User-defined arithmetic and comparison may be supplied through explicit Type Contracts once their declaration syntax, required members, and resolution rules are specified. Such extensions must preserve evaluation order and counts, comparison's `bool` result, and assignment's Unit result.

`and`, `or`, `not`, `=`, `@`, `is`, Ranges, and control transfers cannot be reinterpreted by user code. Custom operator symbols and precedence declarations are not defined. Neither are `!`, `&&`, `||`, `~`, `**`, `??`, `?.`, or ternary `?:`; use logical keywords and `if`. Unary `&` is not a borrow operation; use `@ref` / `@uniq`. Recognition by the lexer alone does not make a token a usable operator.

`#Name` is an Attribute and `#if` / `#case` are compile-time directives, not runtime unary operators. `$` denotes the Composition Root; `$panic(...)` follows [Panic Termination](#103-panic-termination). Other Composition Root operations, dependency resolution, lifetimes, and failure rules remain separately specified. The internal name `MacroKoto` does not define language semantics.

### 8.9. Implementation status

As of 2026-09-07, the Parser implements the precedence table, diagnoses unparenthesized value-comparison and Range chains, and distinguishes generic type arguments from comparisons. Basic expressions, argument labels/defaults, collections, and anonymous functions have syntax-tree support. The `is` right-side rule has regression coverage. Member-name restrictions still need additional validation.

General type inference, overload and argument matching, function-value execution, numeric checks, dictionary duplicate detection, evaluation order during execution, and single-access Property updates require semantic analysis and runtime implementation. Panic name/type validation, diagnostics, and common termination handling are also planned. Control-flow and unsafe checks are partial; ownership, Origins, and runtime cleanup remain planned. Examples using application-specific functions or Types illustrate semantics rather than promise standard-library APIs.

Implementation references:

- [Parser.cs](Kimi/Compiler/Parsing/Parser.cs) and [expression Koto nodes](Kimi/Compiler/Parsing/Koto/Expressions): syntax and precedence.
- [ControlFlowAnalysis.cs](Kimi/Compiler/Analysis/ControlFlowAnalysis.cs): results, transfers, short-circuit paths, and partial unsafe checks.
- [ExpressionPrecedenceTest.cs](xUnitTest/Tests/ExpressionPrecedenceTest.cs), [ParserRegressionTest.cs](xUnitTest/Tests/ParserRegressionTest.cs), and [SpecConformanceParseTest.cs](xUnitTest/Tests/SpecConformanceParseTest.cs): grouping, diagnostics, generic boundaries, and expression syntax.
- [CollectionLiteralParseTest.cs](xUnitTest/Tests/CollectionLiteralParseTest.cs) and [RangeIndexParseTest.cs](xUnitTest/Tests/RangeIndexParseTest.cs): collection and boundary syntax.
- [ControlFlowAnalysisTest.cs](xUnitTest/Tests/ControlFlowAnalysisTest.cs) and [ControlFlowRevisionParseTest.cs](xUnitTest/Tests/ControlFlowRevisionParseTest.cs): control constructs and result checks.

## 9. Control flow

Control flow distinguishes four concepts:

| Concept | Meaning | Forms |
| --- | --- | --- |
| **Evaluation Context** | Whether an expression's result is consumed or discarded. | Value Context; Discard Context |
| **Control Boundary** | Transfer targets and lookup barriers. | Function, Labeled Block, Deferred, Iteration (`for`, `while`, `loop`), Selection (`if`, `match`) |
| **Control Transfer** | A requested change in control. | `return`, `exit`, `continue`, `yield` |
| **Completion** | How evaluation finishes. | `Normal(result)`, `Return(target, result)`, `Exit(target, result)`, `Continue(target)`, `Yield(target, result)` |

An **Iteration Construct** is a `for`, `while`, or `loop`; an **iteration** is one execution of its body. Each construct establishes an Iteration Boundary. Every `if` and `match` establishes a Selection Boundary regardless of its result or context. Boundaries may accept, stop, or pass through a transfer lookup, as specified under [target lookup](#952-target-lookup).

| Transfer | Role |
| --- | --- |
| `return` | End the current function and supply its result. |
| `exit` | End the nearest Iteration Construct or Deferred Block, or an enclosing Labeled Block or Iteration Construct named by `from Label`. |
| `continue` | Start the next iteration of the nearest Iteration Construct, or the one named by `Label`. |
| `yield` | End the nearest enclosing selection and supply its result. |

Unlabeled `exit` skips Labeled Blocks. A `loop` or explicitly named Labeled Block can receive a result in either Evaluation Context. Deferred Blocks accept only operandless self-targeted `exit`; no transfer may cross their boundary. Kimigayo uses `exit`, not `break`, for iteration termination.

### 9.1. Completions

**Normal completion**, represented by `Normal(result)`, means that an expression or construct finishes and returns control to its evaluator. A statement's normal Completion uses Unit without making that statement a value-producing expression. **Abrupt completion** is a `Return`, `Exit`, `Continue`, or `Yield` directed at a resolved lexical target. A transfer expression does not complete normally, even when its target subsequently does.

An operandless `return` or `exit` supplies Unit, so the corresponding Completion always contains a result. Whether a source-level operand is required or forbidden is checked separately. `Continue` has no result.

After required [Scope Exit](#102-scope-exit-destruction) processing, a boundary handles its own valid Completion or propagates one targeting an outer boundary:

| Boundary | Self-targeted Completion | Action |
| --- | --- | --- |
| `loop` or Labeled Block | `Exit(self, result)` | Complete with `Normal(result)`. |
| Selection | `Yield(self, result)` | Complete with `Normal(result)`. |
| Deferred Block | `Exit(self, ())` | Finish body cleanup and resume pending Scope Exit. |
| Iteration Construct | `Continue(self)` | Proceed to the next iteration. |
| Function | `Return(self, result)` | Deliver the secured result to the caller. |

**Divergence** means that evaluation never finishes and produces no Completion. Under the broader term **Evaluation Outcome**, Completion, divergence, and [Panic Termination](#103-panic-termination) are distinct cases. Panic ends the program without a Completion delivered to a lexical target. Never describes the absence of normal completion; it is neither a Completion variant nor a synonym for divergence.

### 9.2. Blocks and evaluation contexts

A **Block** is an indentation-delimited sequence of declarations, expressions, and statements evaluated in order. An ordinary Block completes with Unit on reaching its end, including when its last item is a declaration or conditional compilation removes all its items. Source-level executable bodies must satisfy the nonempty rule below. Nesting an ordinary Block adds no control-transfer target. Constructs with their own result rules apply those rules instead. Function bodies follow [Functions](#551-function-bodies-and-results).

A **Value Context** is a syntactic position that uses an expression's value: an initializer, operand, argument, condition, `match` subject, `return` / `exit` / `yield` operand, or Expression body introduced by `=>`. It remains a Value Context even when the expected Type is Unit or the result is subsequently unused. Reachability, constant evaluation, and optimization do not change it.

A **Discard Context** discards an expression's normal result without imposing Unit or suppressing Type, ownership, or destruction checks. Every direct expression, including the last, in an ordinary, Labeled, Unsafe, Deferred, iteration, branch, or function Block body uses this context. Nested initializers, arguments, and operands retain their positional contexts.

An expression determines its result; its Evaluation Context determines whether that result is consumed or discarded. A `loop` accepts result operands in either context. Selections follow the unified [Result-requiring Selection](#971-branch-results) rules.

A trailing semicolon does not change an expression's Evaluation Context or whether an Expression body supplies an implicit result. Body form, not the number of direct expressions or declarations, determines the branch result rule.

#### 9.2.1. Nonempty executable Blocks

An executable Block requires at least one complete source **Syntax item**: a declaration, expression, statement, or compile-time directive. Blank lines, comments, and separators do not count. Directives must include their required condition, target, and body.

The Parser must report a missing or empty body if no item appears before end of source or an item at the same or shallower indentation. It must not absorb that following item into the body.

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

#### 9.2.2. Conditional compilation and results

Check source emptiness **before directive selection**, independently of reachability. A valid `#if` or `#case` counts even when selection removes all executable Syntax; its own syntax and selection requirements still apply.

```kimi
defer:
    #if windows
        closeHandle()
```

When `windows` is false, the Block remains valid. The Parser must check source items even when early selection creates no Koto nodes. After selection, normal Type, result-coverage, and transfer rules apply. A direct `()` is discarded and does not supply a Block result.

```kimi
let result = if condition
    () // Error: nonempty, but this branch must explicitly yield its result.
else
    yield ()
```

Likewise, removing a required `yield` or result-bearing `exit` through conditional compilation may cause a result-coverage error, even though the source Block passes the emptiness check.

### 9.3. Block constructs

| Construct | Category | Execution and result |
| --- | --- | --- |
| Labeled Block (`Label:`) | Expression; also usable in Discard Context | Execute now; receive a result through `exit value from Label`. |
| Unsafe Block (`unsafe:`) | Block Statement | Execute now with unsafe permission; no expression result. |
| [Deferred Block](#101-deferred-blocks) (`defer:`) | Block Statement | Register now and execute at Scope Exit; no expression result. |

A **Block Statement** is a statement with a scoped body, not an expression. Unsafe and Deferred Blocks are allowed only in executable bodies, not directly in Declaration Containers. Both have an indented multiline form and a single-line form containing one [InlineStatement](#931-inlinestatement). Both forms create an independent body scope and have the same evaluation and cleanup rules.

At statement start, contextual keywords `unsafe:` and `defer:` take precedence over Label parsing. Neither declares a Label. `unsafe/T` remains Type Semantics syntax and `unsafe func` a function declaration modifier; outside their special contexts these spellings follow normal Name rules.

#### 9.3.1. InlineStatement

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

#### 9.3.2. Labeled Block

A Labeled Block begins with `Label:` followed by its indented body on the next line. It receives a result only from an `exit` explicitly targeting that Label. Its trailing expression never implicitly supplies a result, and unlabeled exits still skip it.

A **Result-requiring Labeled Block** occurs in Value Context (even with expected Unit) or has a result-bearing self-targeted `exit`. Classify after target lookup, before reachability analysis. Count resolved self-targeted exits even inside nested constructs, but not results sent elsewhere.

Every reachable completing path must use `exit expression from Label`, including `exit () from Label` for Unit. Fall-through is an error; operandless self-targeted exits are forbidden even if unreachable. Paths transferring outward or never completing need no Block result. Shared [result validation](#98-result-validation) applies in both contexts.

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

#### 9.3.3. Unsafe Block

An **Unsafe Block** executes its body immediately with permission for [unsafe operations](#46-raw-pointers-and-unsafe-operations). It is a statement and cannot appear as an initializer, argument, or other expression operand, in either body form. It creates no Control Boundary and does not intercept transfer lookup. Its body follows ordinary Evaluation Context and Scope Exit rules.

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

### 9.4. Labels

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

A labeled Iteration Construct uses `Label: for ...`, `Label: while ...`, or `Label: loop`. Unlike a [Labeled Block](#932-labeled-block), adding a Label to an Iteration Construct does not change its result rules; a labeled `loop` may appear in Value Context:

```kimi
var result = outer: loop
    for value in values
        if found(value)
            exit value from outer
```

Labels follow the character rules for [Names](#31-names) and have a namespace separate from those of variables and Types. Labels with the same Name and overlapping scopes in one function are invalid.

A Label is visible only inside its construct's body, excluding its `for` iterable or `while` condition. A transfer may identify only an enclosing construct in the same Function Boundary without crossing a Deferred Control Boundary. Sibling, inner, and other-function Labels are inaccessible. A Label names a construct, not an instruction address: jumping into a body or back to a completed construct is not supported.

### 9.5. Control transfers

#### 9.5.1. Syntax and operands

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

Operands are evaluated before transfer. If operand evaluation leaves by another transfer or never completes, the original transfer does not occur. Otherwise, its result is secured by Copy or Move before [scope-exit destruction](#102-scope-exit-destruction) and delivery to the target. Each transfer expression itself has type [Never](#415-unit-and-never-types).

#### 9.5.2. Target lookup

Resolve targets by walking outward through lexical containment. Resolve the target first, then check operand presence and Type; an unsuitable operand never causes lookup to skip a target.

| Operation | Target without a Label | Named target | Stop before finding a target |
| --- | --- | --- | --- |
| `return` | Nearest Function Boundary | Not allowed | Error at a Deferred Control Boundary or if no function exists. |
| `exit` | Nearest Iteration Construct or Deferred Block | Enclosing Labeled Block or Iteration Construct named by `from Label` | Error at a Function Boundary; a named lookup also stops at a Deferred Control Boundary. |
| `continue` | Nearest Iteration Construct | Enclosing Iteration Construct named by `Label` | Error at a Function or Deferred Control Boundary. |
| `yield` | First enclosing Selection Boundary (`if` / `match`) | Not allowed | Error at an Iteration, Function, or Deferred Control Boundary. |

Failure to find a target is an error. A named target must be of the required kind; `continue work` is invalid if `work` names a Block.

A construct acts as a target or lookup stop only inside its body. Its own condition, iterable expression, or `match` subject does not acquire that construct's boundary.

Ordinary and Unsafe Blocks are transparent to lookup. A Labeled Block is transparent except to an explicitly named `exit`. Selections are transparent to `return`, `exit`, and `continue`. A resolved `yield` makes its first enclosing selection Result-requiring, regardless of context or reachability; missing `else`, failed coverage, or incompatible results never retarget it outward.

Named `exit` and `continue` may cross intervening iterations and ordinary, Labeled, or Unsafe Blocks, but no transfer crosses a Function or Deferred Control Boundary. Thus `exit 1` targeting a Deferred Block is an error; it cannot select an outer loop.

```kimi
var result = loop
    for value in values
        exit 10 // Error: the nearest Iteration Construct is for, which forbids an operand.
```

#### 9.5.3. Function Boundaries

Each of these bodies establishes an independent **Function Boundary**:

- Named functions, including methods and nested functions.
- Anonymous functions. Capturing closures remain deferred under [function expressions](#843-function-expressions).
- Property getters and setters.
- Destructors (`deinit`).

In these control-flow rules, "function" includes all of these bodies. A `return` ends only its own function. Other transfers cannot target an outer function's Labels, Iteration Constructs, or selections.

Getters return their [Getter Result Type](#62-default-getter-results); setters and `deinit` return Unit. All follow [function body and result rules](#551-function-bodies-and-results). Normal `deinit` completion, including `return`, still performs automatic field destruction required by the Type.

```kimi
func outer() -> i32
    let f = func () -> i32
        return 1                // Returns from f only.

    return f()
```

#### 9.5.4. Label and nesting examples

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

### 9.6. Iteration Constructs

#### 9.6.1. `for` and `while`

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

#### 9.6.2. `loop`

`loop` repeats unconditionally. It discards body values and starts the next iteration at the beginning of the body after body fall-through or a self-targeted `continue`.

Only self-targeted `exit` supplies the loop's normal result; operandless `exit` supplies Unit. Returns and exits targeting outer or inner constructs supply no result to it. Both Value and Discard Contexts allow result operands and require the same result-Type compatibility.

```kimi
var result = loop
    let value = next()

    if invalid(value)
        exit -1

    if found(value)
        exit value
```

Only reachable self-targeted exits contribute to inference. Unreachable exits are checked against any available Target Result Type under [result validation](#98-result-validation). Nested selections do not intercept `exit`.

```kimi
loop
    exit 10                     // Valid: loop produces an integer, then discards it.
```

```kimi
outer: loop
    loop
        exit from outer
```

The inner `loop` has no result-producing path and has type Never. The outer `loop` completes with Unit. See [result validation](#98-result-validation) for the common rules.

### 9.7. Selections: if, match, and yield

#### 9.7.1. Branch results

Each `if` branch and `match` arm explicitly chooses an **Expression body** or **Block body**, regardless of its item count:

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
- **Result coverage:** every reachable path that completes normally must supply a result. A Block must use `yield`, including `yield ()` for Unit; declarations, discarded expressions, and bodies emptied by conditional compilation do not supply implicit branch results. Source-level empty executable Blocks are parse errors under the [nonempty rule](#921-nonempty-executable-blocks). A path leaving for an outer target or never finishing needs no result. After a transfer caught internally, analysis follows the continuation.
- **Result compatibility:** explicit and implicit results obey the shared [result validation](#98-result-validation) rules, even when the selection's result is discarded.

A selection that does not require a result has only Block bodies, no self-targeted `yield`, and occurs in Discard Context. Reaching a selected Block's end or selecting no branch supplies Unit. Paths that leave for an outer target or never finish supply no result to that selection.

#### 9.7.2. `if`

`if` tests Boolean conditions in order and executes the first selected branch. It may have subsequent `else if` branches and one final `else`. Condition parentheses are optional. Each branch independently chooses an Expression body or a Block body.

In `if` and `while`, a parenthesized condition containing one initialized `let` or `var` tests the bound Boolean value. For example, `if (var z = Func()) => 1 else => 0` requires Boolean `z`. Evaluate the initializer once and enforce any explicit binding Type. Ordinary Block declarations still produce no result.

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

#### 9.7.3. `match`

`match` evaluates its subject once, tests arms in source order, and executes the first matching arm. Every arm uses `pattern => Expression` or `pattern =>` followed by an indented Block. There is no fall-through to another arm.

```kimi
var result = match value
    A =>
        prepare()
        yield 1

    B => 2
```

`yield` ends the whole target `match`. This example assumes `A` and `B` cover every case. A `match` that does not require a result may be non-exhaustive.

#### 9.7.4. Nested `yield` targets

A Labeled Block does not stop `yield` lookup:

```kimi
var result = if condition
    work:
        yield 10                // Supplies the outer if's result, not work's.
else => 20
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

### 9.8. Result validation

A transfer supplies a result only to its resolved target. Function results follow [Functions](#551-function-bodies-and-results); Blocks, Iteration Constructs, and branches use their result sources defined above. Discard Context does not exempt a construct from result validation.

**Implementation status:** The Parser preserves explicit branch body forms. Control-flow analysis checks selections, loops, value-producing Labeled Blocks, lexical transfer targets, and the completion effects of explicitly registered Deferred Blocks. It also checks lexical Unsafe permission for known operations and binder-selected function references. The default type provider handles primitive literals, simple declared Types, and basic raw-pointer and contextual `null` checks. General name/overload resolution, conversions, pattern Binding, ownership, automatic destruction, Origin compatibility, and runtime cleanup generation remain planned; unresolved checks are exposed as pending obligations. Bodies containing deferred compile-time directives await directive selection before analysis.

Validate results in this order:

1. Determine Evaluation Contexts and body forms, resolve transfer targets, and classify Result-requiring Selections and Labeled Blocks without excluding unreachable code. Check syntax, Names, operand presence, and local Type correctness. Enforce selection exhaustiveness requirements.
2. Apply [reachability](#982-reachability) analysis to result sources, required Scope Exit processing, and paths leaving each construct. Collect result candidates only from reachable result-delivery paths. A transfer whose operand or required cleanup cannot complete normally supplies no result to its original target.
3. Check result coverage: reject any reachable path that reaches an end requiring a result without supplying one. Where a construct implicitly supplies Unit, include that Unit as a candidate only when the path is reachable. A non-Unit Block-bodied function may not fall through.
4. Determine the Target Result Type as described below, independently of whether the construct's Expression Type is Never. Unreachable result sources do not contribute candidates or constraints to inference.
5. When a Target Result Type is available, check every explicit result operand and implicit Expression-body result against it, including in unreachable code. Operandless `return` and `exit` supply Unit. Apply normal conversion and Origin compatibility rules. A source that cannot itself complete normally supplies no value to compare; its local operations and any transfers inside it are still checked.

Paths that leave a construct for an outer target or never complete supply no result candidate for that construct. Transfers caught internally may let evaluation continue and must be followed to their continuation.

#### 9.8.1. Expression Type and Target Result Type

The **Expression Type** describes a normally completing expression's value. An expression, including a `loop`, selection, or Labeled Block, with no reachable path that completes normally has Type Never. Missing required results are errors, not Never. Unsafe and Deferred Blocks are statements with no Expression Type; analyze their body completion separately.

The **Target Result Type** constrains results supplied to a boundary. Use a declared or expected Type, or infer it from reachable result candidates under normal inference and conversion rules. Propagate expected Types to result sources even if the Expression Type is Never.

| Source of Target Result Type | Compatibility checking, including unreachable results |
| --- | --- |
| Explicit declaration or expected Type | Check against that Type. |
| Type inferred from reachable result candidates | Check against the inferred Type. |
| No Type supplied and no reachable result candidates | No Target Result Type is available; omit only the comparison against it. |

Never inferred solely from the absence of reachable results does not become a Target Result Type. An explicitly specified Never still constrains results. Unreachable fall-through does not manufacture a Unit result for compatibility checking.

A function keeps its declared return Type. Without a declared or expected Type, infer from reachable results. If none exist, expose inferred return Type Never, but do not use that fallback to constrain unreachable returns. The function value itself has a Function Type.

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

#### 9.8.2. Reachability

Reachability is determined statically within each Function Boundary. Treat a path as reachable unless the following analysis proves otherwise. Optimization settings must not change type-checking results.

- Follow evaluation order, branches, Iteration Constructs, and resolved transfers. A statically non-completing expression has no edge to the next sequential element.
- Follow the continuation of a construct that catches a transfer, such as the code after a Labeled Block ended by `exit`, or an expression consuming a yielded result.
- A `defer` registration does not execute its body. Analyze registered bodies on the Scope Exit paths that reach them; a non-completing cleanup prevents subsequent cleanup and delivery of the pending transfer or result. An exit caught by the Deferred Block finishes that body's cleanup before resuming the pending Scope Exit.
- Prune condition outcomes only for Boolean literals `true` and `false`, optionally parenthesized, in `if`, `else if`, and `while`. Otherwise, consider both outcomes when condition evaluation completes normally.
- Do not prune additional paths through constant propagation, analysis of called function bodies, or general constant folding.
- Do not prune `for` paths using iterable values or `match` arms using constant subjects. Analyze each arm; pattern exhaustiveness determines whether an unmatched path exists.

Reachability affects result candidate collection, inference, and coverage, but not syntax, Names, transfer targets, operand presence, local Type correctness, or Result-requiring classification. Every result source, explicit or implicit, must satisfy any available Target Result Type even if unreachable. Replacing `yield expression` with `=> expression` does not bypass checking.

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

In each example, the unreachable expression or transfer is locally valid, but its result is incompatible with the target. Undefined Names or Labels, invalid operand operations, and value-bearing exits targeting `for` are also errors in unreachable code. These rules concern runtime control flow; Syntax excluded by [conditional compilation](#22-compile-time-directives) follows its separate Binding rules.

## 10. Scope exit, cleanup, and termination

### 10.1. Deferred Blocks

A **Deferred Block** registers cleanup when execution reaches `defer`. Registration evaluates none of its body, arguments, conditions, or initializers. The statement has no result; expressions inside retain their positional Evaluation Contexts.

A registration belongs to its directly containing executable scope: function, branch, arm, current iteration, Labeled or Unsafe Block, or executing Deferred Block body. Unreached registrations do not run; each iteration registers and cleans up independently. Registrations cannot be cancelled or manually invoked.

```kimi
func process(flag: bool)
    defer: log("function end")
    if flag
        defer: log("branch end")
        work()
    log("after branch")
```

For true `flag`, output is `branch end`, `after branch`, then `function end`. Deferred execution and automatic destruction share the [Scope Exit ordering](#102-scope-exit-destruction).

#### 10.1.1. Deferred Control Boundary

Each Deferred Block establishes a **Deferred Control Boundary** that no transfer lookup may cross. It accepts only operandless self-targeted `exit`, including through nested ordinary or Unsafe Blocks; even `()` is forbidden as an operand.

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

#### 10.1.2. Deferred evaluation and ownership

Resolve Names at the registration's lexical position; later declarations are invisible and cannot change those bindings. Registration neither Copies nor Moves referenced locals and creates no closure value. Execution accesses the then-current bindings.

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

#### 10.1.3. Nested and unsafe cleanup

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

#### 10.1.4. Implementation model

Analyze registration separately from execution: a non-completing deferred body affects actual cleanup paths, not reachability immediately after registration. Lower defers and destruction into one exit sequence, retaining registration state only as needed. No dynamic closure, function value, or heap cleanup stack is required.

For `if condition` containing only `defer: cleanup()`, a true branch registers and runs cleanup before leaving that branch; false registers nothing. No registration flag is needed in this simple case, and Lowering must not move cleanup into the surrounding scope. Example cleanup functions are illustrative, not standard API declarations.

### 10.2. Scope-exit destruction

**Scope Exit** combines registered Deferred Blocks and automatic destruction. It applies both to ordinary scope completion and to scopes left by `return`, `exit`, `continue`, or `yield`.

Ownership, temporary-lifetime, and construct-lifetime rules determine each value's owning scope and destruction point. These rules also govern temporaries, `for` iterables and iterators, iteration bindings, `match` subjects, and owned function parameters. A transfer uses those scopes to determine what it leaves.

#### 10.2.1. Cleanup order

Clean departing scopes from inner to outer, finishing each before the next. Within a scope, process local declarations and `defer` statements in reverse combined lexical order. Later initialization or reassignment does not change a binding's position.

Execute only registered defers and destroy only initialized values whose destruction responsibility remains with the scope. Skip moved or destroyed values. Last use does not remove destruction responsibility.

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

#### 10.2.2. Results and transfers

For a transfer with a result operand, or an implicit Expression-body result:

1. Evaluate the operand.
2. Secure the result using normal Copy / Move rules.
3. Destroy temporaries whose normal lifetime ends at completion of that result expression.
4. Run Deferred Blocks and automatic destruction in departing scopes, in the common cleanup order.
5. Deliver the secured result and complete the target's termination or continuation.

Other temporaries follow normal lifetime scopes and positions; absence from the result does not justify earlier destruction. Omit result work for operandless transfers. If the operand triggers another transfer, process that transfer instead.

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

Clean only scopes actually left. `continue` cleans the current iteration's departing scopes and retains outer scopes needed for continuation. Named transfers clean all intervening scopes they leave. Exiting a Deferred Block completes its nested cleanup, then resumes pending outer cleanup.

Normal ownership, borrowing, and [Drop checking](#766-drop-checking) apply throughout cleanup. Securing a result first does not permit a borrow of a destroyed local to escape. If partial initialization or partial Move is permitted, destroy parts with remaining responsibility rather than excluding the whole aggregate. Raw pointer access does not guarantee automatic tracking of the original owner's destruction responsibility.

#### 10.2.3. Completion and abnormal termination

Consume a Deferred Block's registration when its execution starts. Each registration executes once if cleanup reaches it; an inner defer registers in the executing body's own scope, never in an outer scope already being exited.

Deliver a pending transfer or result only after all required cleanup completes normally. Nonterminating cleanup prevents remaining cleanup and delivery; general termination proofs are not required.

Forced process termination and undefined behavior provide no cleanup guarantee. Panic follows [Panic Termination](#103-panic-termination). Exceptions, cancellation, and stack unwinding, if introduced, require separate common rules for Deferred Blocks, destruction, and secured results; this specification provides no cleanup guarantee for them.

### 10.3. Panic Termination

**Panic Termination** abnormally ends the entire program when execution cannot continue normally. Explicit requests and implicit runtime check failures share this rule:

```text
Panic Termination
├─ explicit
│  └─ $panic(...)
└─ implicit runtime check failure
   ├─ integer overflow
   ├─ integer division or remainder by zero
   ├─ out-of-range index or invalid Range boundary
   ├─ invalid conversion
   ├─ invalid shift count
   ├─ duplicate dictionary key
   └─ missing dictionary key on indexed read
```

Every runtime check failure specified by this document initiates implicit Panic. Each operation defines its invalid values; IEEE 754 floating-point division by zero is not an integer division failure.

#### 10.3.1. Explicit Panic

`$panic(message)` is a Composition Root termination operation with a `string` argument. Evaluate the argument once as in an ordinary call. If it completes normally, initiate Panic Termination. The call has Type Never and never returns normally.

```kimi
func requirePositive(value: i32) -> i32
    if value <= 0
        $panic("value must be positive")
    return value
```

#### 10.3.2. Common termination rules

Panic diagnostics carry a reason and source location: the failed operation for implicit Panic, or the `$panic(...)` call for explicit Panic. A duplicate dictionary key uses the later key expression. The runtime chooses the diagnostic format and destination; successful output is not a prerequisite for termination.

Panic is unrecoverable and terminates the program, not only the current thread. It delivers no result to a normal control target and guarantees no stack unwinding, Deferred Block execution, or destruction. Panic during cleanup also provides no guarantee of remaining cleanup or delivery of a secured result.

Panic conditions and termination behavior are identical in Debug and Release builds. An implicit check failure is a language-guaranteed termination operation, not merely a rewrite to a replaceable function call. Replacing `$panic` or diagnostic handling cannot make a Panic return normally.

Invalid operations found during required compile-time constant evaluation are compile-time errors. A runtime operation initiates Panic only if it is actually evaluated and its check fails. Optimization must not introduce Panic from an operation skipped by short-circuit or conditional evaluation. Undefined behavior from an unsafe contract violation is not guaranteed to be detected as Panic.

Panic Termination defines language behavior independently of implementation mechanisms such as a `trap` instruction. Operations that return failure as a value, and wrapping or saturating integer arithmetic, require separate explicit library APIs.
