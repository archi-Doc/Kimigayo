# Kimigayo Language Specification

- [Part I. Language foundations](#part-i-language-foundations)
  - [1. Overview](#1-overview)
  - [2. Source and lexical structure](#2-source-and-lexical-structure)
  - [3. Types and basic value model](#3-types-and-basic-value-model)
  - [4. Declarations and contracts](#4-declarations-and-contracts)
  - [5. Name resolution, overload resolution, and inference](#5-name-resolution-overload-resolution-and-inference)
- [Part II. Language constructs and semantics](#part-ii-language-constructs-and-semantics)
  - [6. Expressions and operators](#6-expressions-and-operators)
  - [7. Control flow](#7-control-flow)
  - [8. Properties](#8-properties)
  - [9. Ownership and lifetime analysis](#9-ownership-and-lifetime-analysis)
  - [10. Scope exit and destruction](#10-scope-exit-and-destruction)
  - [11. Failure handling](#11-failure-handling)
- [Part III. Program and compilation environment](#part-iii-program-and-compilation-environment)
  - [12. Modules and dependencies](#12-modules-and-dependencies)
  - [13. Compile-time directives](#13-compile-time-directives)
  - [14. Compilation model](#14-compilation-model)
- [Appendices](#appendices)
  - [Appendix A. Compiler implementation requirements](#appendix-a-compiler-implementation-requirements)
  - [Appendix B. Non-normative reference models](#appendix-b-non-normative-reference-models)
  - [Appendix C. Implementation status](#appendix-c-implementation-status)
  - [Appendix D. Deferred feature index](#appendix-d-deferred-feature-index)
  - [Appendix E. Terminology index](#appendix-e-terminology-index)
  - [Appendix F. Syntax summary](#appendix-f-syntax-summary)

# Part I. Language foundations

## 1. Overview

### 1.1. Purpose

**Kimigayo** is a programming language built from scratch to be consistent, fast, simple, fun, and safe.

This document defines the intended language. Language rules, Compiler requirements, and the recorded implementation snapshot are distinct; see [implementation status](#appendix-c-implementation-status).

**Basic example.**

```kimi
alias Kimi.Base

#if windows
alias Kimi.Windows

public group Program
    public func runExample(arg: string) -> ()
        var array = [0, 1, 2,]
        var map = [0:"Zero", 1:"One", ]
        return

    func getString<s/T>(value: s/T) -> string
        s is ref or obj
        T is Comparable

        #match
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

Kimigayo does not guarantee backward compatibility between language versions. It prioritizes consistency and language quality. Reproducible builds must pin the compiler build as well as their source, dependencies, and configuration; a language-version label alone does not identify a pre-alpha compiler implementation.

User-defined Types, Contracts, and Declaration Containers conventionally use PascalCase; built-in Type keywords retain their specified spellings. Functions, Properties, local bindings, parameters, and other value names generally use camelCase.

| Notation | Meaning and uses |
| --- | --- |
| `[]` | Array and Dictionary construction, indexing, Range-based slicing, and anonymous-function Capture Lists. |
| `()` | Ordered grouping: parameters, arguments, Tuples, Unit, Function Types, conditions, and operator precedence. |
| `<>` | Generic parameters and arguments, including compile-time parameters and arguments that construct Types. |
| `{}` | Unused; reserved for future language evolution. |
| `=` | Initialization, parameter defaults, or assignment according to context; acquisition follows [Copy and Move](#35-copy-and-move). |
| `@` | Explicit Type/Semantics adaptation or Consume (`@move`); see [explicit operations](#664-explicit-operations). |
| `->` | Result Type associated with the input side of a function declaration or Function Type. |
| `=>` | Mapping or correspondence: function and accessor expression bodies, `if` expression bodies, `match` arms, and named Origin arguments. |
| `:` | Structural association: Name and Type, key and value, or Label and Block/Iteration Construct. |
| `#` | A compile-time construct. Lowercase reserved directives such as `#if` differ from PascalCase Attributes such as `#Inline`. |
| `$` | Selects the Composition Root; it is not a macro prefix. |

The single-layer ordinary Type form is `semantics/CoreType from origin`; value borrows and pointers may enclose a complete Type, as in `ref/ref/T`. Object Types also admit a runtime-contract View Target. See [Types](#3-types-and-basic-value-model) for composition, grouping, and omission rules. Examples are independent unless explicitly connected. Application-specific Types and APIs illustrate assumed declarations, not promised library interfaces. Fences marked `text` may use conceptual storage or compiler notation rather than source syntax; lines marked Error are intentional boundary examples.

### 1.3. Reading the rules

Examples appear beside the rules they illustrate. **Basic examples** show ordinary use; **boundary examples** explain edge cases; **error examples** identify the violated rule.

| Wording | Meaning |
| --- | --- |
| must / must not | Mandatory requirement or prohibition. The feature defines whether a violation is a compile-time error, Panic, or an Unsafe contract violation. |
| may | Permission within all stated constraints. |
| should | Recommendation, not a condition for language conformance. |
| is planned | Implementation work is intended; this is not a language rule. |
| is deferred | In a design-status note, design is postponed; this is distinct from a Deferred Condition. |
| implementation-defined | The implementation chooses within the stated limits and must document the choice. |
| unspecified | Any result within the stated limits is permitted; the choice need not be documented. This does not imply undefined behavior. |

Unqualified declarative rules and imperative requirements are normative even without `must`. Examples illustrate those rules and do not override them. Compiler requirements preserve required information and invariants; a **Non-normative reference model** is an optional algorithm, not an alternative semantics.

**Specified, not implemented** means the stated rules are settled but their implementation is unavailable. **Partially specified** means rules exist but identified design details remain open. **Deferred design** means that feature's design is withheld in this revision. These are distinct from **Deferred** as a valid compile-time Condition result. Implementation plans grant no language permission.

## 2. Source and lexical structure

Source structure determines token boundaries and syntactic containment. Literal-specific rules appear with each literal form.

### 2.1. Source text and encoding

Kimigayo source text uses UTF-8. Invalid UTF-8 source byte sequences are compile-time errors.

A **SourceDocument** is one immutable source snapshot (path and text) belonging to a source module. A **Kotonoha** is that named module; its dependency and source-environment rules appear under [Modules and dependencies](#12-modules-and-dependencies).

### 2.2. Lines, indentation, and continuation

Use four U+0020 spaces per indentation level. Tabs are not permitted in indentation. Indentation expresses syntactic containment.

Physical line endings may be LF, CRLF, or CR.

Newlines separate Syntax items where the surrounding grammar permits separation. Commas separate arguments or elements and are not binary operators. Indentation rules still apply within `()` and `[]`: indent continued arguments and elements one level, and optionally align the closing delimiter with the opening line. A method-chain continuation starting with `.` also uses one extra indentation level.

This specification does not allow arbitrary binary operators at the start of a line to continue the previous line. `:`, `=>`, `->`, and `in` are delimiters for their respective constructs, not general binary operators.

### 2.3. Whitespace and comments

Outside literal content, U+0020 spaces separate tokens; leading spaces determine indentation, and trailing spaces are ignored. This does not change literal contents or make tabs valid layout whitespace. Comments contribute no executable syntax items and separate rather than concatenate adjacent tokens.

- `//` starts a comment extending to the physical line ending or end of source.
- `/*` starts a block comment ending at the first `*/`; block comments do not nest. A missing terminator is a compile-time error.
- Within literal text, comment delimiters are content. Interpolated expressions follow ordinary token rules.

A block comment containing no physical newline may appear between tokens on the same line. Leading spaces before the comment determine that line's indentation; spaces after `*/` do not add indentation.

If a block comment contains a physical newline, no code may follow its closing `*/` on the same physical line. Only spaces and comments may follow it; violating this rule is a compile-time error. The comment does not join code across physical lines. Subsequent code must appear on a following line and obey the ordinary indentation and line-continuation rules. Comment-only lines and indentation inside comments do not affect block structure.

```kimi
let total = 1 /* inline comment */ + 2

func example()
    /* A multiline comment.
    */
    work()
    finish()
```

`total` is initialized to `3`. Both `work()` and `finish()` belong to `example`'s body. Blank and comment-only lines do not supply an executable body; see the [nonempty Block rule](#721-nonempty-executable-blocks).

### 2.4. Tokens and separators

A token's spelling is contiguous. Separate adjacent spellings when their concatenation would form a different token. Names, keywords, literals, punctuation, and operators follow their own token rules; recognizing a token does not make it a permitted expression.

The [notation table](#12-conventions-and-notation) summarizes punctuation. Expression grouping, generic/comparison boundaries, and the token rules for `@` follow [precedence and associativity](#65-precedence-and-associativity).

### 2.5. Names

A **Name** identifies a declaration in source. All named declarations, including Declaration Containers, Types, functions, Properties, bindings, and parameters, use the same character rules. A Name contains a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`),
- an underscore (`_`), or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`), or Letter Number (`Nl`).

Each continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`), or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`), or Connector Punctuation (`Pc`).

Names are equal if and only if their Unicode scalar sequences match exactly. Comparison is case-sensitive and culture-independent. No Unicode normalization, case folding, compatibility mapping, or removal of characters is performed for name lookup or duplicate-name detection.

Every Name, in both declarations and references, must already be in Unicode Normalization Form C (NFC). A non-NFC spelling is a compile-time error; the compiler does not silently normalize it. For example, a Name containing U+00E9 (`é`) is permitted, while the canonically equivalent sequence U+0065 U+0301 is rejected. `Dog` and `dog` are distinct Names, as are ASCII `A` and fullwidth `Ａ`.

All Format (`Cf`) characters are forbidden anywhere in Names, including bidirectional controls and zero-width join/non-join controls. This restriction applies to Names, not to comment or literal contents. It does not exclude every default-ignorable character in other Unicode categories.

Visually confusable Names are not treated as equal. Implementations may provide optional lint warnings for confusable Names; such warnings do not affect name identity or language validity, and the compiler is not required to emit them.

The Unicode character-category and normalization data version is implementation-defined. Implementations must document their data source/version policy. The current .NET compiler uses its host runtime's Unicode category and normalization APIs; the effective data may depend on the runtime and operating system/globalization backend. This specification does not pin a Unicode release, so builds requiring identical character acceptance must use the same runtime and globalization data.

Contextual keywords may be used as Names in contexts that accept contextual identifiers. Reserved keywords may not. `in` delimits a `for` header and `has` introduces an inline Property accessor list; both may be Names elsewhere. `move` is an **@-context reserved name**: immediately after `@`, it selects Consume without Type/Semantics lookup. Elsewhere it may be an ordinary Name, so `move(...)` is an ordinary call. There is no prefix `move` or Move accessor. A Type named `move` requires a nonconflicting spelling, such as an alias, in an Adaptation Target. Built-in Semantics names likewise select their Semantics in shorthand targets. `Self` is reserved; `self`, `storage`, and `value` follow [contextual name rules](#511-namespaces-roles-and-visibility).

`public`, `internal`, `private`, `protected`, and `open` are reserved modifier keywords. The compound access specifications `protected internal` and `private protected` each consist of two keywords; their placement follows [accessibility](#512-accessibility-and-reachability).

`init`, `deinit`, and `base` are reserved for [construction](#433-constructors) and destruction. They do not introduce ordinary callable Names or an implicit base receiver.

`require` is reserved for the [require statement](#793-require-statement).

For example, `Dog`, `_value`, `point2`, `日本語`, and `ǅelta` are valid Names, while `2point`, `has-value`, and the empty string are not.

### 2.6. Number literals

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

Number literals have no Type suffix. Internal representations do not determine a literal's language Type; [expression type inference](#631-type-inference) defines contextual Types and defaults. To specify a Type, use a declaration annotation or an explicit conversion such as `123@i32`.

### 2.7. Character escapes

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

String interpolation is defined separately under [Escaped strings](#291-escaped-strings).

### 2.8. Character literals

A `CharLiteral` has Type `char`. It encloses one directly written Unicode scalar value or one [Character Escape](#27-character-escapes) in single quotation marks. Delimiters are not part of the value.

```text
CharLiteral = "'" (DirectScalar | CharacterEscape) "'"
```

#### 2.8.1. Content and validation

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

#### 2.8.2. Normalization and displayed characters

The compiler does not normalize Char Literal content. Validation uses the content after escape processing. A `char` represents a scalar value, not a grapheme cluster or a displayed character; a combining mark alone is valid.

```kimi
'é'           // Valid: U+00E9
'\u(E9)'      // Valid: U+00E9
'e\u(301)'    // Error: U+0065 and U+0301
'\u(301)'     // Valid: one combining mark
```

The front end parses and validates Char Literals and preserves their original spelling when writing the syntax tree.

### 2.9. String literals

A `StringLiteral` produces UTF-8 text of Type `string`. Both forms support single or multiple lines:

| Form | Delimiter | Backslash escapes | Interpolation |
| ---- | --------- | ----------------- | ------------- |
| Escaped string | One double quotation mark (`"`) on each side | Yes | Yes |
| Raw string | The same number of double quotation marks, at least three, on each side | No | No |

#### 2.9.1. Escaped strings

An escaped string is enclosed by one double quotation mark on each side. A backslash introduces an escape sequence:

**Basic example.**

```kimi
"Hello, world"
"First line\nSecond line"
"
First line
Second line
"
```

The opening and closing delimiters are not part of the value. Any line break between them is part of the string content; `\n` may instead be used when an explicit line-feed escape is preferred.

Escaped strings support the shared [Character Escapes](#27-character-escapes) and string interpolation with `\(expression)`.

An interpolation begins with `\(` and ends at its matching `)`. The enclosed text is parsed as a Kimigayo expression, including any nested parentheses, and the expression's string representation is inserted into the surrounding string:

```kimi
"Hello, \(name)."
"Total: \(price * quantity)"
```

#### 2.9.2. Raw strings

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

## 3. Types and basic value model

An ordinary **Type** combines Type Semantics, a Core Type, and Origins. Value borrows and raw pointers can recursively enclose a complete Type under [nested Semantics](#336-nested-semantics-and-type-grouping). Object Types additionally admit runtime-contract View Targets under [object views](#335-object-views-and-identity). The single-layer form is:

```text
semantics/CoreType from origin
```

| Component | Meaning |
| --- | --- |
| Type Semantics | How the value is represented, owned, accessed, or used. |
| Core Type | What the value is. |
| Origin | Where the value derives from; constrains its lifetime or validity. |

For example, `ref/Dog from owner` is a shared borrow of a `Dog` whose validity derives from `owner`. A Core Type or valid object View Target is required; Type Semantics and Origin may be omitted when determined by the language or context.

Kimigayo provides a fixed set of primitive Core Types and user-defined named Core Types.

A structure is a user-defined composite Core Type introduced by a [`struct` declaration](#43-structure-declarations).

### 3.1. Primitive core types

Primitive Core Types are built into the language. Sizes below are storage sizes.

#### 3.1.1. Integer types

| Signed | Unsigned | Size |
| --- | --- | --- |
| `i8` | `u8` | 8 bits (1 byte) |
| `i16` | `u16` | 16 bits (2 bytes) |
| `i32` | `u32` | 32 bits (4 bytes) |
| `i64` | `u64` | 64 bits (8 bytes) |
| `i128` | `u128` | 128 bits (16 bytes) |
| `isize` | `usize` | Native pointer size of the target platform |

#### 3.1.2. Floating-point and boolean types

| Type | Size |
| --- | --- |
| `f32` | 32 bits (4 bytes) |
| `f64` | 64 bits (8 bytes) |
| `bool` | 8 bits (1 byte) |

#### 3.1.3. Character type

`char` represents one Unicode scalar value and has a fixed storage size of 32 bits (4 bytes). Its valid ranges are U+0000..U+D7FF and U+E000..U+10FFFF, inclusive. Surrogates (U+D800..U+DFFF) and values above U+10FFFF are invalid.

All scalars in these ranges are valid, including unassigned code points, private-use characters, noncharacters, controls, and combining marks; displayability is irrelevant. [Character literals](#28-character-literals) impose additional direct-spelling restrictions.

The size guarantee does not guarantee the same internal representation as `u32`. Alignment and byte order are not specified here.

#### 3.1.4. UTF-8 and strings

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

`string` is the built-in Core Type for UTF-8 text. Its exact in-memory container and storage layout are implementation-defined. Owned `string` is always non-Copy; see [Copy capability](#351-copy-capability-and-explicit-duplication).

#### 3.1.5. Unit and never types

`()` is the Unit type. It has one value and represents the absence of a meaningful result.

An expression with no reachable path that completes normally has Type Never, which has no values. `return`, `exit`, `continue`, `yield`, and `$panic(...)` have Type Never. Transfer operands supply results to their targets without changing the Types of the transfer expressions themselves. Never is a Type, not a Completion: a completed transfer has an abrupt Completion, whereas divergence produces no Completion. Missing required results are errors, not Never. See [Completions](#71-completions), [result validation](#78-result-validation), and [Panic Termination](#113-panic-termination).

### 3.2. Compound type syntax

A named Core Type may be qualified with dots and may have generic arguments.

```kimi
A.B<T, U>
```

Tuple types use parentheses and commas: `()` is Unit, `(T,)` is a one-element Tuple, and `(T, U)` is a two-element Tuple. `(T)` groups a Type without adding a Tuple or changing its Semantics. Function types use `->` between the parameter type and return type. `(T) -> U` has one parameter of Type `T`; `((T,)) -> U` has one parameter whose Type is a one-element Tuple.

```kimi
(i32, string)
(i32, string) -> bool
```

#### 3.2.1. Callable value types

A **Function Value** is a callable value. Distinguish its concrete Type from a common **Function Type** `(A1, ..., An) -> R`:

| Core Type | Identity and environment | Owned-value classification |
| --- | --- | --- |
| Function Item Type | One resolved function declaration and specialization; no runtime capture environment | Copy, Owned, Shared call |
| Concrete Closure Type | One anonymous-function expression and specialization; stores its captures and internal call signature | Copy exactly when every capture's complete Type is Copy |
| Common Function Type | A shared calling contract and an owned, type-erased environment | Non-Copy, even for an empty or Copy environment |

Repeated evaluation of the same anonymous-function expression with the same type arguments produces the same anonymous Core Type; distinct expressions have distinct Types even with identical text and signatures. Each value retains its own Origin bindings. A Closure's environment is compiler-managed storage, not a user-accessible struct; no user-defined `deinit` can be added to the generated Type.

The initial common Function Type requires an `Owned` environment, exposes only Shared call, and cannot return a borrow dependent on its hidden environment receiver. Its arguments and results need not all be owned values. Concrete Closures retain their actual receiver and lifetime contracts; see [function expressions](#643-function-expressions) and [callable constraints](#444-callable-constraints).

```text
Function Item or concrete Closure
    ├─ keep the concrete Type, including through Callable generics
    └─ convert to a fixed common Function Type when its contract is satisfied
         └─ acquire and own the erased environment and its cleanup responsibility
```

An empty environment or `func []` does not imply purity, a function-pointer ABI, fixed size, no allocation, or concurrency safety. A borrow of an existing common function value uses ordinary Semantics: `ref/F` is Copy and `uniq/F` is Non-Copy. It neither erases a borrowed concrete Closure nor exposes additional call capabilities.

### 3.3. Type semantics

Type Semantics specify the ownership, borrowing, layout, and safety properties of a typed value.

The single-layer syntax is `Semantics/CoreType`, extended to object View Targets below. Semantics prefixes associate to the right; value-borrow and pointer prefixes may enclose a complete inner Type. An unparenthesized `from Origin` annotates the outermost layer. See [nested Semantics](#336-nested-semantics-and-type-grouping) for layer boundaries and permitted combinations.

Within a generic declaration, an identifier in the Semantics position denotes a generic Semantics parameter. For example, `s/T` applies the Semantics parameter `s` to the Core Type parameter `T`.

In the syntax below, `T` denotes a Core Type; in object forms it may also denote a valid runtime-contract View Target.

| Category      | Semantics    | Syntax         | Layout or Meaning                     |
| ------------- | ------------ | -------------- | ------------------------------------- |
| Value         | Owner        | `T`, `owner/T` | Data layout                           |
| Value Borrow  | SharedRef    | `ref/T`        | Shared borrow of a value              |
| Value Borrow  | ExclusiveRef | `uniq/T`       | Exclusive mutable borrow of a value   |
| Object        | Owner        | `obj/T`        | Exclusive object ownership            |
| Object        | Rc           | `rc/T`         | Non-atomic counted ownership          |
| Object        | Arc          | `arc/T`        | Atomic counted ownership              |
| Object Borrow | SharedRef    | `objref/T`     | Shared borrow of an object            |
| Object Borrow | ExclusiveRef | `objuniq/T`    | Exclusive mutable borrow of an object |
| Unsafe        | Pointer      | `unsafe/T`     | Unsafe pointer                        |

#### 3.3.1. Owned values

`T` and `owner/T` are equivalent: both directly own a value with the data layout of `T`.

#### 3.3.2. Value borrows

Value borrows provide non-owning access to value data, subject to lifetime constraints.

- `ref/T` is a shared borrow; multiple shared references may coexist.

- `uniq/T` is an exclusive mutable borrow; no conflicting reference may coexist.

#### 3.3.3. Owned objects

An object retains its complete dynamic payload, type metadata, and ownership responsibility. These are logical roles, not a prescribed field order or allocation:

- `obj/T` is an exclusively owned object.

- `rc/T` uses non-atomic reference counting; the object remains alive while an owning reference exists.

- `arc/T` uses atomic reference counting. Atomic ownership management does not guarantee safe concurrent mutation of `T`.

#### 3.3.4. Object borrows

Object borrows provide non-owning access to objects, subject to lifetime constraints.

- `objref/T` is a shared object borrow; multiple shared references may coexist.

- `objuniq/T` is an exclusive mutable object borrow; no conflicting reference may coexist.

#### 3.3.5. Object views and identity

```text
Object View Type = Object Semantics + View Target + Origin
Object Semantics = obj | rc | arc | objref | objuniq
View Target      = Core Type | runtime contract specialization
```

An Object View Type is the complete static Type of an object handle or borrow. In `objref/Speaker`, the runtime contract `Speaker` is a View Target, not a Core Type with its own data layout. This extension does not create `owner/Speaker` or `ref/Speaker`.

The **Runtime Object Type** (also **Dynamic Type** or **Dynamic Core Type**) is the concrete Core Type actually constructed. Its [Runtime Type Identity](#1481-type-identity-and-descriptors) excludes the handle's outer Semantics and Origin bindings. Static Types still retain every lifetime dependency.

A completed object has exactly one Dynamic Type, unchanged throughout its lifetime. A view determines available operations; changing it preserves the entire object, identity, and destruction responsibility. Replacing metadata or reinitializing the object as another Type is forbidden. A new object later placed at the same address has a new lifetime and inherits no identity, Loan, or flow facts.

**`Supports(D, V)`** relates a concrete Core Type `D` to a View Target `V`. It holds exactly when either:

- `V` is `D` itself or a direct or indirect base Core Type of `D`; or
- `V` is a runtime contract specialization `C` and both `RuntimeUsable(C)` and `Implements(D, C)` hold under [runtime contracts](#443-runtime-contracts).

Upcasts, view-support tests, checked casts, and metadata share this relation. It does not grant access, ownership, `Owned`, Origin/Loan validity, or permission to invoke an incompatible ordinary member. Generic element relationships do not imply container covariance. Core Type inheritance alone does not establish substitutability of complete Types: no slicing, implicit `owner/Dog -> owner/Animal`, ordinary `ref/Dog -> ref/Animal`, or `uniq/Dog -> uniq/Animal` conversion is introduced. Object upcasts are explicit operations.

```text
objref/Animal from source       Dog object
    ├─ public view: Animal         ├─ Animal state
    ├─ shared access               ├─ Dog state
    └─ Origin: source              └─ Dog type and destruction information
```

Object ownership does not require heap allocation. Representation choices must preserve identity, access, lifetime, and cleanup. Neither `arc` nor `Owned` implies safe concurrent access.

#### 3.3.6. Nested Semantics and type grouping

`ref/V`, `uniq/V`, and `unsafe/V` may take a complete value Type `V`, including its own Semantics and Origins, as their immediate **Referent Type**. The outer layer refers to storage holding a value of `V`; it does not replace `V`'s Semantics or refer directly to its eventual referent.

```kimi
ref/ref/T                           // Shared borrow of a shared-reference value.
ref/uniq/T                          // Shared borrow of an exclusive-reference value.
uniq/ref/T                          // Exclusive borrow of a shared-reference value.
ref/obj/T                           // Borrow of object-handle storage, not objref/T.
unsafe/ref/T                        // Raw pointer to shared-reference storage.
ref/(ref/T from inner) from outer    // Separate inner and outer Origins.
```

Prefixes associate to the right and bind more tightly than `->`. Thus `ref/ref/T from outer` annotates only the outer reference; it does not assign `outer` to the inner reference. Parentheses enclose a complete Type and allow annotations at each layer. For example, `ref/((i32) -> bool)` borrows a common function value, while `ref/(i32) -> bool` is a function Type with a `ref/i32` parameter. Grouping never creates a Tuple or an additional borrow.

Expand aliases and retain every layer for Type identity, applicability, layout, and Origin analysis. `ref/ref/T` is distinct from `ref/T`. `owner/V` is redundant ownership notation for the existing value Type `V`, not an operation that removes its references or Object Semantics; redundant owner prefixes and grouping normalize away. Object Semantics still require a supported Core Type or runtime-contract View Target, not an already Semantics-applied value Type. Consequently `ref/obj/T` is permitted but `obj/ref/T` does not implicitly box a reference. Generic Semantics parameters obey the same restrictions after substitution; generic parameter roles are unchanged.

Each layer retains its own Origin dependencies. The [position-specific Origin rules](#94-origin-elision-and-return-contracts) apply to each omitted Origin; an explicit outer annotation does not overwrite an inner one. In a parameter Type, only the outer direct borrow may introduce an implicit input Origin; inner borrow Origins and Origin arguments of aggregate inputs must be explicit. Local initializers may supply inference for all layers, while instance Stored Properties require explicit bindings throughout. The unannotated forms above illustrate Type composition, not permission to omit Origins in every position. At every safe value-borrow layer, all observable Origins of `V` must outlive that layer's Origin. For the last example above, `inner : outer` is required. Grouping or a redundant owner prefix cannot supply a second, conflicting annotation to the same normalized layer. Raw pointers do not establish safe-reference validity or extend lifetime.

Copy classification uses the outer effective Semantics: `ref/uniq/T` is Copy, whereas `uniq/ref/T` is Non-Copy. Copying the outer shared reference does not copy the stored exclusive capability. Shared access through an outer reference cannot Move a non-Copy inner value, obtain exclusive access through a stored `uniq`, or mutate the reference slot. A shared reborrow of that stored capability remains bounded by the outer Loan. Destroying or moving an outer reference does not destroy or move its referent. Replacing a reference slot through valid exclusive access must preserve all inner Type, Origin, and Loan constraints.

This syntax adds no implicit repeated dereference, safe `*` operator, or pointer/safe-reference conversion. Borrow formation uses the explicit rules in [Borrow and Reborrow](#6645-explicit-borrow-and-reborrow).

### 3.4. Values, places, and storage

**Storage** is a region holding values. **Destruction responsibility** is the obligation to destroy an owned value at the end of its lifetime, subject to [cleanup and termination](#10-scope-exit-and-destruction).

A **Place** is a storage location that can hold a value. A **Place expression** designates it; parentheses preserve the classification. A Place is distinct from a **Temporary Value**, even when that result is materialized into a separate [Temporary Place](#36-temporary-values-places-and-lifetimes).

An **Access Designator** identifies an access target after Name/Type resolution: a local or parameter, Property, Tuple element, index, or built-in dereference. It includes computed Properties and user indexers without promising storage or Consume permission.

```text
Access Designator -> operation-specific resolution
                    ├─ Place access
                    ├─ value acquisition, such as a getter result
                    └─ error
```

These are resolution outcomes, not fallback stages. A failed Consume cannot become Read. Permitted function/method references produce function values; locals holding such values are Access Designators. Classify by resolved meaning, not spelling alone. Parentheses do not change the category.

Owned Move transfers ownership and destruction responsibility, preventing a second destruction at the source. Borrow-value Copy/Move duplicates or transfers access capability without owning its referent; destroying a borrow does not destroy the referent. Destroying `rc/T` or `arc/T` releases an owning reference under object-lifetime rules. A non-owning `unsafe/T` neither destroys its referent nor frees its storage.

Initialization places a value in an empty place. Destruction ends a value's lifetime and responsibility; if its storage remains, that place becomes Uninitialized after normal completion. No access is possible after the storage's lifetime ends.

Value Lifetime separates acquisition (Copy / Move), placement (Initialization / Replacement), and Destruction. One assignment may contain both Move and Replacement.

The [initialization-state rules](#911-storage-state-and-responsibility) define Initialized, Uninitialized, and Moved. A **Partial Move** transfers an inline part and leaves an aggregate incomplete; supported paths and permissions follow [Move Paths](#913-move-paths-and-partial-move).

### 3.5. Copy and move

**Copy** implicitly duplicates a value, leaves its source Initialized, and preserves the source's destruction responsibility. It executes no user-defined code, heap-allocating duplication, reference-count change, or resource acquisition. Copying a reference does not copy its referent.

**Move** transfers the value and destruction responsibility, or a borrow value's access capability, and marks the source Moved. It invokes no user code and need not clear source memory.

Ordinary value acquisition selects Copy for a Copy Type and Move otherwise; reject an unavailable operation. This covers initialization, assignment sources, by-value arguments, and explicit/implicit result transfers. Explicit [Consume](#915-explicit-consume) with `@move` forces Move even for Copy Types. Borrow creation and reborrowing are separate operations; a Name or Property read does not necessarily consume storage.

Copy capability is independent of `let`/`var` and flow-dependent Loans. At use, Copy obeys read restrictions and Move must not conflict with overlapping active Loans. Both preserve Origin dependencies without extending referent lifetimes. Reborrowing does not make exclusive references Copy.

```kimi
let a: i32 = 10
let b = a                 // Copy; a stays Initialized.
var node: obj/Node = makeNode()
let owned = node          // Move; node becomes Moved.
// use(node)              // Error until reinitialized.
node = makeNode()
```

#### 3.5.1. Copy capability and explicit duplication

`T` and `owner/T` are the same owned Type. Classify complete Types using Core Type, Semantics, and stored components:

| Type | Classification |
| --- | --- |
| Owned integers, floating-point values, `bool`, `char`, Unit | Copy |
| `ref/T`, `objref/T`, `unsafe/T` | Copy regardless of referent `T` |
| `uniq/T`, `objuniq/T` | Non-Copy |
| `obj/T`, `rc/T`, `arc/T` | Non-Copy even if `T` is Copy |
| Slice | Shared-borrow representation: Copy; exclusive-borrow representation: non-Copy |
| Function Item | Copy |
| Concrete Closure | Copy exactly when every captured complete Type is Copy; empty environments qualify |
| Owned common Function Type | Non-Copy regardless of its hidden environment |
| Owned Tuple / fixed-length array | Copy exactly when every component Type is Copy |
| Owned user-defined struct | Non-Copy unless explicitly opted in |
| Owned `string` | Non-Copy regardless of its internal representation |

Never has no values and needs no classification. Other Types require their own rules; sharing elements alone does not establish Copy.

`Copy` is a compiler-checked built-in capability. `Self is Copy` opts a struct into compiler derivation exactly when every directly declared stored field's complete Type is Copy, its direct base Type (if any) is Copy, and the struct has no user-defined `deinit`. The base is an inline owned component for this check, so the test recursively covers inherited storage and destruction. Each derived declaration must opt in independently; a base's opt-in is not inherited. These conditions suffice, including for an `open struct`: no additional active-Loan requirement belongs to Type classification. Computed Properties contribute no fields, all-Copy fields do not imply opt-in, and users cannot supply a Copy body. An owned Copy always copies the complete value of its exact Core Type, never a sliced base part.

```kimi
struct Point
    Self is Copy
    var x: i32
    var y: i32

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
```

`T is Copy` is a generic constraint. Do not assume Copy before constraints or specialization establish it. User-struct derivation is specific to `Self is Copy`, not a general consequence of `Self is Capability`. Compiler-generated Closures instead use the table's automatic rule.

Unknown Copy capability follows [Generic Access Effects](#527-generic-access-effects), including dependent acquisition and default-getter rules.

Duplication requiring allocation, reference-count increments, or resource duplication uses explicit methods or contracts. No standard duplication contract or API spelling is specified.

```kimi
let text: string = "Hello"
let copy = text.clone() // Illustrative explicit duplication API.
let moved = text        // Move; text is no longer usable.
```

### 3.6. Temporary values, places, and lifetimes

#### 3.6.1. Materialization

| Term | Meaning |
| --- | --- |
| Temporary Value | An expression's temporary result; not its original persistent Place |
| Temporary Place | Anonymous storage holding that value |
| Materialization | Giving a Temporary Value a stable Temporary Place when an operation needs storage |

Unless qualified, *temporary* means Temporary Value. Merely using physical storage does not make a result its source Place.

```text
Temporary Value
    -> materialize if stable storage is needed
       -> Temporary Place
          ├─ permitted Borrow / Reborrow
          └─ permitted field / element operations
```

Materialization neither reevaluates the expression nor adds Copy, resource duplication, reference-count increments, heap allocation, or lifetime extension. It preserves the same value and destruction responsibility. It must not turn failed Consume into a Read or restore a moved source.

A newly owned temporary has exclusive writable capability over its whole Temporary Place unless another rule restricts access; it needs no `let`/`var` binding. Materialization realizes this capability without upgrading borrows, granting referent or Property permissions, ignoring readonly parts, or bypassing Loans, Origins, construction, or `deinit` conditions.

#### 3.6.2. Lifetime and borrowing

Unless a construct needs a longer lifetime, a temporary lasts until the outermost expression that created it finishes. Argument temporaries last through the call; iteration sources and `match` subjects last for their required use. Destroy remaining temporaries in reverse creation order. After a Move, the transferred value follows its destination's lifetime, while the original Temporary Place keeps its original lifetime and destroys only remaining Initialized parts.

New borrows of owned temporaries depend on their Temporary Places and cannot outlive them. Exclusive capability permits the applicable explicit exclusive borrow; it does not bypass the [Borrow table](#6645-explicit-borrow-and-reborrow).

```kimi
inspect(makeResource()@ref)
modify(makeResource()@uniq)
inspect(resource@move@ref) // resource remains Moved.

let view = makeResource()@ref
// inspect(view) // Error: borrowed temporary expired after the initializer.

let owned = makeResource()
let lastingView = owned@ref // Borrow the retained local instead.
inspect(lastingView)
```

A temporary that is already a borrow follows shared-reference Copy or Reborrow rules. Preserve its referent Origins; do not substitute a borrow of storage holding the reference value.

```kimi
let view = makeView()@ref // If makeView returns ref/T, preserve its Origins.
```

Borrow and Slice formation never extend the source's lifetime. Result transfers secure values before common [scope-exit cleanup](#102-scope-exit-destruction); Panic follows [Error Handling](#113-panic-termination).

### 3.7. Origins and loans: overview

Kimigayo uses **Origins** instead of lifetime variables. An Origin describes how long a borrow remains valid; a **Loan** records which place is borrowed and whether the borrow is shared or exclusive.

Origin annotations appear in signatures and type declarations. Local Origins may be inferred from initializers and ordinary Origin/Loan constraints. Omission is permitted only by the [position-specific rules](#94-origin-elision-and-return-contracts): direct borrowed inputs introduce input Origins, results use conservative elision, and instance Stored Properties require explicit Origin bindings. Static Stored Properties may not retain safe borrows in this revision.

The safe value-borrow semantics are:

```kimi
ref/T from o   // shared, immutable, and aliasable
uniq/T from o  // exclusive and mutable
```

`uniq/T` is not implicitly copyable and cannot coexist with another overlapping borrow. The corresponding object-borrow semantics, `objref/T` and `objuniq/T`, follow the same shared and exclusive rules. This section uses `ref` and `uniq` in examples.

When `from o` is omitted, [Origin elision](#94-origin-elision-and-return-contracts) determines the Origin.

### 3.8. Raw pointers and unsafe operations

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

#### 3.8.1. Null and equality

`unsafe/T` permits `null`, whose expected Type must determine `unsafe/T` or compilation fails. Safe references (`ref/T`, `uniq/T`, `objref/T`, `objuniq/T`) are non-null. Non-nullness alone does not validate a raw pointer.

```kimi
let pointer: unsafe/i32 = null
let typedNull = null@unsafe/i32 // Typed Null Formation, not a pointer cast.
let unknown = null // Error: no pointer Type can be determined.
let empty = pointer == null
```

`==` and `!=` compare the addresses of two pointers with the same Type and return `bool`, without reading pointees. A comparison with `null` gives the literal the other operand's pointer Type. Null equals null and never equals a non-null pointer.

Initialized pointers may be compared even if null or dangling. Equal addresses imply neither equal provenance nor ownership or access permission. Different pointer Types require an explicit unsafe conversion to a common Type. Pointer ordering comparisons are not defined.

#### 3.8.2. Dereference and ownership

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

#### 3.8.3. Pointer arithmetic and indexing

For `p: unsafe/T` and `n: isize`, including negative `n`, only these arithmetic and indexing forms are supported:

| Form | Meaning |
| --- | --- |
| `p + n` | Pointer displaced by `n * sizeof(T)` bytes. |
| `p - n` | Pointer displaced by `-n * sizeof(T)` bytes. |
| `p[n]` | The same memory place as `*(p + n)`. |

`p += n` and `p -= n` combine these displacements with [compound assignment](#672-compound-assignment) and require the same unsafe conditions. Pointer increment and decrement are not supported.

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

#### 3.8.4. Pointer conversions

When the complete raw pointer Types match after normalization, `@` performs ordinary same-Type acquisition. It copies the pointer, preserves address, provenance, and Origin information and constraints, and does not itself require unsafe context. Expand Type aliases for this comparison; matching size or memory layout alone is insufficient. It grants no new access permission or ownership. Unsafe operations in operand evaluation still require unsafe context.

```kimi
// pointer has Type unsafe/Node.
let a = pointer             // Copy; no unsafe context required.
let b = pointer@unsafe/Node // Same-Type Copy; no unsafe context required.
```

For distinct normalized raw pointer Types, `@` supports `unsafe/T -> unsafe/U` in unsafe context. `unsafe/T -> usize` and `usize -> unsafe/T` also require unsafe context. Fit integer literals used as pointer-cast inputs to `usize` before converting. Pointer casts within one address space preserve address and provenance without changing memory, initialization, or alignment, or granting access as `U`.

```kimi
unsafe:
    let bytes = pointer@unsafe/u8
    let shifted = bytes + 13
    let typed = shifted@unsafe/i32
    // Access through typed still requires i32 alignment and a valid i32.
```

`unsafe/u8` permits byte-sized arithmetic, not reads of uninitialized memory. A cast itself does not read a pointee or require a valid, aligned value of the destination pointee Type; dereference and access do.

#### 3.8.5. Target and round-trip guarantees

Pointer/integer conversion initially requires a target whose ordinary data addresses fit losslessly in `usize`, whose pointer-address, address-index, `usize`, and `isize` widths agree, and which provides the guarantees below. Multiple address spaces and integer conversion of pointers carrying extra state (such as capabilities) are excluded. Unsupported conversions are compile-time errors; CPU/OS support is target-specific.

Null converts to integer zero, and integer zero converts to null, without requiring an all-zero internal pointer representation. These explicit conversions still require unsafe context.

Converting a pointer to `usize` guarantees its numeric address only. `usize` is not a language-level carrier of provenance; Copy, Move, argument passing, return, and storage follow ordinary integer rules. Converting the same numeric address back to the original pointer Type within the same execution guarantees address equality, but not preservation or recovery of provenance. Integer arithmetic or serialization cannot strengthen this guarantee.

```kimi
unsafe:
    let address = pointer@usize
    let saved = address
    let restored = saved@unsafe/i32
    // Preserves the address only; access validity is a separate requirement.
```

Conversion neither extends lifetime nor restores permissions. Supported targets allow arbitrary integer-to-pointer casts, but dereferencing the result or performing another operation requiring provenance needs an additional documented Compiler/target guarantee and the usual lifetime, alignment, initialization, and access conditions. Unsafe context alone does not establish these conditions. Portable code that needs to preserve provenance should retain the original pointer value. Addresses from another execution have no validity guarantee.

A raw pointer value may exist without sufficient provenance for memory access. Under ordinary value-use rules, it can be held, copied, moved, passed, destroyed, compared by address with the same pointer Type, or tested against `null` without unsafe context. Converting it back to `usize` or casting to a different pointer Type requires the ordinary supported-target and unsafe conditions and does not establish missing provenance. Zero-displacement arithmetic retains the existing unsafe, known-layout, and positive-size conditions; nonzero displacement additionally needs live-allocation provenance and bounds. Dereference and indexing must satisfy their place-formation conditions, with initialization and access permissions additionally checked as required by the actual read or write. Value-level equality does not prove access validity.

#### 3.8.6. Raw pointer API design boundaries

Raw pointer acquisition APIs, allocation and deallocation, initialization of raw storage, conversion to or from safe references, ownership acquisition, and Unsafe Function Types are specified separately. Example functions such as `obtainPointer` and `use` are illustrative, not standard API declarations.

### 3.9. Type relations and expression operations

The following table is normative. It separates relations between complete Types from operations on expressions. The distinction is semantic, not whether machine instructions are emitted: a Borrow, Reborrow, Copy, or Move remains a value operation even when optimized away. A static Type relation does not by itself authorize acquisition of a value. The referenced rules define each relation's scope; this table adds no conversions, adaptation chains, or overload preferences.

Here `A <: B` includes normalized identity and the explicitly defined subtype rules. Complete Types retain Semantics, nested Types, generic arguments, and Origin bindings. Unresolved generic or Origin information retains constraints for later resolution; it is not evidence that Types are identical or compatible.

| Relation or operation | Inputs and normative condition | Effect and boundary |
| --- | --- | --- |
| Normalized Type identity | Two complete Types. Expand aliases and normalize grouping and redundant owner prefixes; compare the resulting Type structure, declaration identity, generic arguments, Semantics, and Origin bindings. Bound Origin parameters correspond by binder, not spelling. | No value operation. Different nominal declarations do not become equal through matching names, fields, or layout. This is not the Origin-erasing Runtime Type Identity used by object views. |
| Alias equivalence | An alias and its resolved target, with substitutions and complete Type information preserved. | Participates in normalized identity; no wrapper, conversion, or ownership change is introduced. Alias lookup still follows ordinary visibility and lookup rules. |
| Subtyping | Two complete Types, under established generic and Origin constraints. Prove identity or a subtype relation explicitly defined by this specification. | Static fitting only. Do not insert acquisition, Borrow/Reborrow, dereference, numeric conversion, object upcast, or user conversion as part of the proof. |
| Origin shortening and variance | Apply [Origin variance](#932-variance) and outlives constraints at each relevant position. Covariance permits shortening, contravariance reverses the relation, and invariance requires equality. | A subtype proof, not a new borrow. Preserve existing dependencies and Loans; do not extend lifetime or replace inner Origins with an outer annotation. Exclusive Referent Type invariance remains mandatory. |
| Callable signature compatibility | For implementation `(A1, ..., An) -> R` and requirement `(P1, ..., Pn) -> Q`, apply [callable compatibility](#528-callable-signature-compatibility): equal arity, `Pi <: Ai`, `R <: Q`, and compatible Origin/Loan contracts. | Static signature fitting. It does not insert argument/result operations or itself convert a Function Item or Closure to a common Function Type. Receiver and environment requirements remain separate. |
| Expected-result compatibility | An instantiated candidate result Type and an independently established expected Type, under [expected-result filtering](#523-expected-results). Require identity or a defined subtype relation. | Excludes candidates without inserting a value operation. Result acquisition and declared Loan propagation remain required; this is not general implicit expression adaptation. |
| Never fitting | Never has no normally produced value and fits any otherwise valid expected value Type without a value conversion. | No outer value operation executes on a non-completing path. Preserve target, Unsafe, and local correctness checks, and validate transfer operands against their own result boundary under [result validation](#78-result-validation). Do not use inferred Never to constrain unreachable result sources. |
| Implicit expression adaptation | An expression, target Type, and use-site context. Select only adaptations allowed in that position, including the finite [argument adaptation rules](#522-argument-adaptation-and-literals) and fixed-expectation [common function conversion](#6434-function-references-and-common-type-conversion). | May require acquisition, Borrow/Reborrow, or a defined conversion. Literal fitting determines an unresolved literal's Type; it does not convert an established numeric Type. No universal implicit-conversion search is permitted. |
| Explicit expression adaptation | An expression and resolved Adaptation Target in context. Select one [defined `@` operation](#6643-defined-adaptations), then enforce its requirements and static result fitting. | May change value representation, view, or Loan state, or perform runtime checks. No hidden sequence of operations is inserted. Origins are inferred as specified for Adaptation Targets. |
| Object upcast | An expression and different object View Target with proof of `Supports(S, V)` and a matching [explicit upcast row](#6647-object-upcasts). | One explicit view/acquisition operation. Inheritance or conformance alone does not establish an implicit complete-value adaptation or callable argument/result conversion. |
| Borrow / Reborrow | An expression with the required Place, access, and Loan properties, and a permitted implicit or explicit borrow operation. | Establish or derive Loans under the borrow rules. Changing `uniq/T` to `ref/T` requires shared Reborrow; it is not a subtype rule. |
| Numeric conversion | An established numeric value Type and a target admitted by the [explicit numeric conversion table](#6644-numeric-conversions-and-literals). | A value conversion with the specified rounding, range checks, and failure behavior. `i32` is not a subtype of `i64`; representable literal fitting is separate. |
| Acquisition legality | An expression, selected access operation, and current initialization/access/ownership/Loan state. Apply ordinary Copy, Move, Borrow, or Consume requirements. | Type compatibility does not prove legality. An exact Type match can still fail because storage is Moved, access is unavailable, or a Loan conflicts. Failure does not reopen committed lookup or overload selection. |

For example, `ref/T from longer <: ref/T from shorter` may hold when `longer : shorter`, without establishing a new Loan. In contrast, `uniq/T` to `ref/T` needs Reborrow, and an object view change needs its explicit upcast operation. Same normalized Type does not force an identity operation: explicit same-Type exclusive adaptation still selects Reborrow, and ordinary by-value acquisition may Copy or Move.

Candidate analysis may record operation choices and unresolved obligations, but must not commit source-state changes while testing candidates. After selection, enforce the chosen acquisition and adaptation under the specified evaluation order, and apply static result fitting without replacing that operation. The [implementation correspondence](#b5-type-relation-and-operation-plans) is informative; no particular internal API is required.

## 4. Declarations and contracts

Kimigayo uses the following information to identify declarations and their meaning:

| Element     | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| `Name`      | The basic human-readable name used to refer to a declaration          |
| `Signature` | The information that distinguishes declarations in the same scope     |
| `Type`      | The meaning of a value or invocation within the type system           |

Name lookup, accessibility, and invocation selection follow [Name resolution, overload resolution, and inference](#5-name-resolution-overload-resolution-and-inference). Property declaration syntax is defined under [Properties](#8-properties).

### 4.1. Signatures

A Signature determines whether declarations may coexist in one scope:

| Declaration | Signature |
| --- | --- |
| Type declaration | Name and generic parameter count |
| Function | Name, generic parameter count, ordered normalized parameter Types including the receiver |
| Constructor | Declaring structure and ordered normalized parameter Types; no ordinary Name or receiver parameter |
| Property | Name |

Normalize Core Types by resolved Symbol, including their Kotonoha/version, and include Type Semantics exactly once. Normalize equivalent spellings (`T` and `owner/T`) and represent generic parameters by position and kind, not name. `ref/T` and `uniq/T`, including receivers, remain distinct. Applied Semantics distinguish use-site Types, not Type declarations or Container identities.

For Signature comparison only, exclude Origin names, lists, and lifetime relations. Retain complete Types and Origin contracts for semantic checks. Return Types, external/internal parameter names, defaults, optionality, access, unsafe modifiers, and Constraints cannot independently distinguish overloads.

An **API signature**, for [accessibility checks](#5122-api-signature-accessibility), includes the Types and requirements exposed by a declaration, including results and Constraints. This is broader than the Signature used above for overload identity; exclusion from overload identity does not exempt a component from accessibility checking.

```kimi
struct Reader
    func read(self: ref/Self) -> i32 => 0
    func read(self: uniq/Self) -> i32 => 0 // Distinct receiver Semantics.

func identity<T>(value: T) -> T => value
func identity<U>(value: U) -> U => value // Error: same normalized Signature.
```

Duplicate Signatures are declaration errors. Distinct Symbols imported from different Containers may have the same shape; a use is ambiguous unless overload rules select one. Fragment header-name agreement is separate from parameter-name normalization between different function declarations. Existing syntax-based Signature structures do not yet implement these semantic checks.

### 4.2. Declaration containers

A **Declaration Container** is a named declaration scope whose body may contain Properties, functions, Constraint Clauses, or nested Declaration Containers as permitted by its kind. Its body is delimited by indentation.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Properties, functions, and nested Declaration Container declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts Properties, functions, constructors, and at most one selected `deinit`. Generic parameters, Origins, and Constraints are supported. Sealed by default; `open struct` permits derivation. |
| `enum` | Yes | Enum declaration container. |
| `extension` | No | Its Name identifies the target. |
| `contract` | No | Specifies associated-type Constraint Clauses and Property requirements. The Parser preserves required accessors without generating implementations or storage. |

#### 4.2.1. Root and nested containers

Each source unit contributes named declarations to the project root. Top-level executable syntax, including bindings (`let` and `var`) and local functions, has a SourceDocument-local execution scope and is not exported into the Compilation root or visible to another file. Its source-local scope and lookup environment must be preserved. Shared functions and Properties belong in named Containers. Cross-source execution order remains unspecified. A `rootgroup` declaration starts at the root and accepts a dot-separated Name. For example:

```kimi
rootgroup A.B
    var value = 1
```

creates the nested group path `A.B`. Ordinary `group` bodies accept nested Declaration Container declarations. In this revision, `struct` bodies do not accept nested Declaration Containers.

Aliases follow [source-local import rules](#121-external-references-and-aliases). Synthesized intermediate groups in a `rootgroup` path use an explicit group declaration's accessibility when present, otherwise `private`; synthesis is not an independent header fragment. Public paths require explicitly accessible groups.

#### 4.2.2. Container fragments

Group and struct declarations may be split, even within one file. Collect same-parent, same-name fragments and identify a declaration by originating Kotonoha, parent Symbol, name, kind, and generic arity. Reject conflicting kinds such as a group and struct with the same name. Different arities, such as `Box` and `Box<T>`, are different Types. Never merge across Kotonoha libraries or treat an extension as a target declaration fragment; applied `ref`/`uniq` Semantics do not change Container identity.

Matching fragments must agree on generic parameter count/kinds/order/names, Origin count/order/names, declaration kind, semantic modifiers, and accessibility after defaults. Do not widen conflicting accessibility. Exactly one fragment may define the Container's Constraints, even if duplicate clauses would be identical; other fragments omit them and share that definition's Constraints. Resolve them in their definition-site source environment.

For structures, `open` must agree across all fragments. At most one fragment supplies the base clause; the other fragments share that base without repeating it. Resolve the base in that fragment's source environment, then validate the complete merged inheritance relationship.

After compile-time selection and merging, reject duplicate Properties, duplicate function Signatures, and namespace conflicts. All selected fragments may contribute Stored Properties, including generated ones, under [split-structure storage order](#431-split-structures-and-storage-order). Do not require a primary fragment. Enum/contract fragment contents and extension identity need further rules.

Constructor Signatures must also be unique across the selected fragments. At most one selected `deinit` body may belong to a merged structure, including generated fragments. Do not concatenate destruction bodies or choose one by source order; duplicates are declaration errors. Mutually excluded bodies may coexist in source only when selection leaves at most one in each concrete specialization.

```kimi
// A.kimi
struct Box<T>
    T is Comparable
    var count: i32 = 0

// B.kimi
struct Box<T>
    func countValue(self: ref/Self) -> i32 => self.count
// Repeating the Constraints or renaming T to U in B.kimi is an error.
```

**Design boundary:** Enum/contract fragment contents, extension identity/public names, cross-fragment static group initialization, and cross-source execution order remain separately specified.

### 4.3. Structure declarations

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

Explicit and implicit constructors follow [construction](#433-constructors); all construction obeys [initialization](#86-initialization) and [construction completeness](#912-aggregate-construction-and-completeness).

#### 4.3.1. Split structures and storage order

A `struct` may be split into compatible declaration fragments, including fragments produced by a Source Generator. Multiple fragments may contribute Stored Properties. Merge fragments for the same declaration, validate their headers and member uniqueness, and classify storage using the normal Property rules, including implicit accessors and `has` expansion. No primary fragment is required. Detailed declaration identity and header checks follow the [Container integration rules](#422-container-fragments).

Define a **logical declaration order** independently of physical memory layout:

1. Ordinary source documents precede generated source documents. Order ordinary documents by their stable logical source identifiers.
2. Order generated documents by stable Generator identifier, then by the Generator's logical output identifier.
3. Within each document, use source declaration order, including the written order of multiple fragments of the same structure.

Source identifiers are build metadata independent of absolute checkout paths, temporary output paths, and processing order. Ordinary sources use normalized project-relative logical paths; sources outside the project directory require an assigned stable project-relative logical name. Generated output identifiers are logical names assigned by the Generator. Normalize path separators to `/` and remove redundant path segments; compare identifiers ordinally without host-specific case folding or locale rules. Require unique ordinary source identifiers, unique Generator identifiers within a build, and unique output identifiers within each Generator; identifier collisions are build errors.

Apply this order to the selected declarations of each specialization. Only Stored Properties contribute storage slots. File enumeration, parser completion, and Generator completion order must not affect the result. Renaming a source or generated output may change logical order and therefore initializer side-effect order.

Generated declaration availability must satisfy the [name-resolution boundary](#134-name-resolution-boundary). Do not begin consuming a provisional type and append generated Storage later. Finalize layout only after the complete selected fragment set and storage classification are known. Generated documents retain their own source context and logical identifiers. Generator APIs, inputs, scheduling, and dependency checks beyond the established output deadline remain separately specified; generator dependencies that prevent establishing the required environment are errors, not permission to revise resolved Names.

Physical layout and ABI guarantees follow [Structure layout and ABI](#146-structure-layout-and-abi).

#### 4.3.2. Inheritance and open structures

A structure is **sealed** unless its declaration has `open` immediately before `struct`. A sealed structure cannot be a base Type. `open` permits derivation and is independent of accessibility: `public struct` remains sealed, while `internal open struct` permits derivation only where that Type is accessible. A derived structure is itself sealed unless explicitly declared `open`; openness is not inherited. No separate `sealed` modifier is needed for this default.

A structure may specify one direct base with `: BaseType`, after its Name and generic parameters and before its Origin list. The base must resolve to an accessible constructed or nongeneric `open struct` Core Type; a generic parameter, Semantics-applied Type, group, enum, or contract is not a base. Omission declares no user-defined base. Reject multiple bases and direct or indirect inheritance cycles, including cycles through different constructions of the same generic declaration. The base's Constraints must hold, and its accessibility must cover the derived Type's [effective access domain](#5122-api-signature-accessibility). Constraint Clauses continue to express capabilities separately from the base clause.

```kimi
public open struct Base
    protected var count: i32 = 0

public struct Leaf : Base
    public func read(self: ref/Self) -> i32 => self.count

public struct Invalid : Leaf // Error: Leaf is sealed.
```

Inheritance uses the C# class model for the single-base relationship and access permissions, with Kotonoha as the module boundary. Kimigayo's default `private` access, Container nesting rules, explicit receivers, and Type Semantics still apply. Derivation does not grant the base's private access or change inherited members' declared accessibility. Inherited ordinary members are considered before extensions; detailed hiding and inherited overload-group construction belong to the member-lookup design below. `open` does not make every member virtual. Access through a base-typed expression is checked against the statically selected declaration, not a runtime override.

An override must target an accessible overridable member and preserve its declared accessibility. The cross-Kotonoha exception is a `protected internal` base member: its override in another Kotonoha declares `protected`. The same rule applies to overridden accessors, each of which must be accessible to the overriding code. A private member cannot be overridden, and an internal or private-protected member cannot be overridden from another Kotonoha. These rules do not allow an override to expose an otherwise inaccessible API Type.

The direct base is one inline owned subobject, logically preceding the structure's directly declared fields. Base subobjects retain their own field identities and construction-completion facts. Physical offsets remain compiler-selected under [layout rules](#146-structure-layout-and-abi); logical ordering does not require flattening or a fixed ABI. Construction runs base first under [constructors](#433-constructors), and destruction runs the derived layer first under [field cleanup](#1032-field-cleanup). The base subobject cannot independently be Moved, replaced, or reconstructed through a base view of a derived value. Whole-value operations retain the exact owning Type and the responsibility for all layers; they cannot slice a derived value. Access to an inherited field still obeys normal permissions and checks all its containing base and derived ancestors for Partial Move restrictions.

**Design boundary:** Virtual/abstract/override declaration syntax, inherited member hiding and overload-group construction, and explicit ordinary base-member invocation remain separately specified. [Virtual semantics](#434-virtual-members-and-overrides), [object dispatch](#645-object-member-calls), and [explicit object upcasts](#6647-object-upcasts) are defined below; additional ordinary derived/base conversions remain outside this revision. Construction/destruction order, base-constructor invocation, and Copy derivation over inherited storage are specified here and in their owning sections. C# compatibility for inheritance access does not implicitly import CLR representation, boxing, garbage collection, or value slicing, or add conversions to the existing adaptation tables.

#### 4.3.3. Constructors

A constructor is a dedicated structure declaration: an optional access specification, `init`, a parameter list, an optional `: base(arguments)` clause, and a nonempty indented executable Block. It has no ordinary Name, explicit receiver, separate generic or Origin parameters, result annotation, expression body, or virtual/override modifier. It uses the containing structure's Type parameters, Origins, and Constraints. Parameter labels, defaults, and Type checking follow ordinary function parameters. Access defaults to `private`; constructor parameters obey API signature accessibility. Only a structure's own fragments may declare its constructors; groups, enums, contracts, extensions, and executable Blocks may not.

```kimi
public open struct Named
    public let name: string
    protected init(name: string)
        self.name = name

public struct Entry : Named
    public let number: i32
    public init(name: string, number: i32) : base(name)
        self.number = number

let entry = Entry.init("item", 1)
```

`Type.init(arguments)` constructs a fresh owned value of exactly `Type`. The reserved `.init` suffix selects Type lookup, never Value lookup: resolve the structure and all its type arguments first, then consider only its own accessible constructors using ordinary argument mapping and overload comparison. Constructor results are fixed to that owned Type; expected results cannot select another Type or an alternative lookup path. All generic type arguments must be supplied unless the qualifier already denotes a fully bound Type, such as `Self` in a generic structure. Bare type-parameter construction is not defined. Constructors are not inherited, and extensions cannot contribute candidates. Unknown or inaccessible constructors are errors; there is no field-wise, zero-fill, or function-call fallback. `Type.init` without invocation is not a function value; `value.init(...)` cannot reinitialize existing storage. This syntax does not introduce object allocation or conversions to `obj`, `rc`, or `arc`.

After explicit arguments and omitted defaults have been evaluated in the ordinary call order, create fresh construction storage with all components Uninitialized and bind the constructor's parameters. For a derived structure, evaluate the base arguments in that constructor's parameter/source context, then invoke the selected direct-base constructor in the base subobject. If the base initializer is omitted, select an accessible zero-argument base invocation by the same rules, including optional parameters. A `: base(...)` initializer on a structure with no direct base is an error. Exactly one base invocation occurs; constructor delegation within the same Type and explicit repeated base initialization are not defined. The base call is a dedicated construction operation, so protected constructors may be used by a derived constructor without an ordinary instance receiver.

Complete base construction before evaluating this layer's Property declaration initializers, in logical declaration order, and then execute its constructor body. Declaration initializers keep their own declaration-site environments: they cannot reference constructor parameters or `self`. Base arguments cannot use `self` either. No constructor silently initializes a field with zero, null, or an element Type's default constructor. Origins required by stored arguments and the completed base must be represented by the constructed Type's declared Origin contract and inferred under ordinary lifetime constraints; hidden or invented Origins cannot make a construction valid.

Within the constructor body, `self` is a special Construction receiver. Direct `self.field` access to a Stored Property declared by that structure denotes its storage for initialization and field operations, without invoking a user getter or setter. The first write initializes a `let` or `var`; later writes obey their ordinary first-initialization and Replacement rules. Reading an Initialized field uses its complete Type's standard Copy/shared-borrow rule, including normal Loan checks. Construction does not authorize `@move` from a field or a hidden Move on read. Computed Properties, inherited fields, whole-self acquisition or borrowing, instance-method/accessor calls on `self`, and capturing or exposing `self` are forbidden during construction. The base constructor is responsible for its own fields. Temporary borrows of this construction's fields must end before completion; they cannot escape in the constructed value. Borrowed constructor inputs may be stored only when the result's Origin contract permits them. These privileges do not extend to nested functions, ordinary methods, or Property declaration initializers.

The body establishes a Function Boundary with a Unit control-flow result: falling through, `return`, or `return` with a Unit-typed expression requests successful completion, not a failure result. After evaluating any return operand, every such reachable exit must have every own Stored Field Initialized and complete, and the base completed. Then run the body's normal Scope Exit, including remaining parameters and Deferred Blocks, while keeping construction storage alive. Recheck completeness and lifetime validity after cleanup, and only then commit this layer's construction completion. A `defer` may not supply initialization missing at the requested exit. Passing a completion check is not permission to return a borrow of a destroyed parameter. A base completion continues construction of the next layer; only the outermost completion produces the owned result. A Move into the caller's destination does not rerun any constructor.

Constructors have no recoverable-failure return or exception mechanism. Use an ordinary factory returning an Option/Result to perform fallible acquisition and validation before calling `Type.init`; its locals have ordinary cleanup on failure. Panic during any construction phase terminates without unwinding. The partial-initialization cleanup rules also govern interrupted aggregate-expression construction and any ordinary Scope Exit that abandons construction storage; they do not add a new failure syntax or turn an incomplete `return` into success.

After merging, a structure with no selected explicit constructor receives one implicit zero-parameter constructor exactly when every directly declared Stored Property has a declaration initializer and its base, if present, has an accessible zero-argument invocation. It is declared `public`, so its effective access is exactly that of its containing Type, and has an implicit Unit body and the same initialization/completion rules. Empty structures satisfy the field condition. Otherwise there is no implicit constructor; this alone does not make a Type declaration invalid. A selected explicit or source-generated constructor suppresses implicit synthesis even if private. No memberwise constructor is synthesized. Synthesis depends on structural declarations, not whether initializer code happens to type-check; errors in a synthesized constructor are diagnosed normally. Generic dependencies retain validation obligations until specialization.

#### 4.3.4. Virtual members and overrides

An overridable member and each override must be explicitly designated; equal names and signatures do not create an implicit override. Ordinary non-virtual calls use the statically selected implementation. The words `virtual` and `override` here specify semantics; final declaration spellings remain a syntax boundary.

An override preserves the shared/exclusive receiver kind and, after normalization and correspondence of receiver `Self`, the parameter and result Types. No covariant-result extension is added. Argument labels and defaults follow the statically selected declaration. Access follows the existing inheritance rules, including the cross-Kotonoha `protected internal` exception. Overrides cannot strengthen preconditions or input-Origin requirements, or weaken result lifetime guarantees. Compare Origin contracts after receiver-name correspondence; accept only compatibility proven by existing rules.

Initial virtual calls are safe operations. Every virtual implementation and each override must independently satisfy [ObjectCompatible](#6451-object-receiver-compatibility) for the declared receiver kind. Do not reuse the base body's proof for an override. Inherited runtime-contract conformance must remain valid after replacement of an implementation.

| Operation | Check and failure boundary |
| --- | --- |
| Ordinary non-virtual member | Check when generating/using its object entry; reject that object use |
| Virtual member | Check its declaration; reject an incompatible declaration |
| Override | Independently check its declaration; reject an incompatible declaration |
| Runtime-contract implementation | Check `Implements(D, C)`; reject incompatible conformance |
| Override implementing inherited contract requirements | At the override declaration, also recheck every corresponding requirement; reject the declaration on failure |

Check Property accessors separately: Shared for getters, Exclusive for setters. An invalid virtual/override declaration cannot remain valid merely by disabling object conversion or invocation. Separately compiled callers rely on the declaration guarantee without enumerating all derived Types. Legitimate generic dependencies remain declaration obligations until specialization finalization; reject a failing specialization even when it has no object call sites.

```text
Animal.reset: Exclusive virtual, ObjectCompatible
    ├─ Dog.reset changes permitted state -> valid override
    └─ Dog.reset replaces self entirely  -> invalid override declaration
```

Derived Types must tolerate mutations and preconditions exposed by base APIs. Application invariants stronger than those APIs guarantee cannot become unchecked hidden memory-safety assumptions of safe operations.

### 4.4. Constraints

The following terms distinguish declared capabilities, conditions, and their fulfillment:

| Term | Meaning | Example |
| --- | --- | --- |
| **Contract** | A Declaration Container declared with `contract` that specifies a named capability a Type provides. | `contract Comparable` |
| **Constraint** | A condition imposed on a Type or Type Semantics. | `T is Comparable` |
| **Constraints** | The set of conditions required for a declaration to be valid or usable. | The leading clauses of a generic function or structure. |
| **Conformance** | A Type's fulfillment of a Contract, with the required correspondence between requirements and implementations. | A Type fulfills `Comparable`. |

A **Constraint Clause** expresses a Constraint in the form `subject is requirement`. All clauses in a declaration's Constraints must hold. They constrain Core Types, Type Semantics, or `Self` (the enclosing Type) and establish capabilities the implementation may use. Each declaration kind restricts the permitted subjects; see [function Constraints](#462-function-constraints).

Core Type requirements may name a capability declared with `contract` or another compile-time type capability. Type Semantics requirements may name concrete semantics, such as `ref` or `obj`, or a semantics category. Requirements combine with `and`, `or`, `not`, and parentheses under the [requirement-expression rules](#442-requirement-expressions).

A `struct` header may contain generic parameters and an Origin list. Its Constraints precede Properties and functions.

```kimi
struct Container<s/T> origin owner, source
    T is Comparable
    s is reference

    var value: s/T
```

These clauses constrain Core Type parameter `T` and Semantics parameter `s`. Constraints may also apply to the enclosing Type:

```kimi
struct ComparableContainer<T>
    T is Comparable
    Self is Comparable

    var value: T
```

`T is Comparable` supplies comparison capabilities for the stored value's Type. `Self is Comparable` requires `ComparableContainer<T>` itself to fulfill `Comparable`; it does not automatically derive an implementation from the clause on `T`. The members needed to fulfill that requirement are omitted from this example. The built-in `Self is Copy` is a specific [compiler-derivation exception](#351-copy-capability-and-explicit-duplication).

Constraints may require Conformance to a Contract, but may also test concrete Types, Type Semantics, or categories. The `contract` Declaration Container declares the named capability; a Constraint such as `T is Comparable` requires its fulfillment.

#### 4.4.1. Associated types and property requirements

Inside a `contract`, `associate` introduces an associated-type Constraint Clause, and `has` declares required Property accessors:

```kimi
contract Sequence
    associate Element is Comparable
    var count: i32 has get
```

See [contract Property requirements](#842-contract-property-requirements) for accessor conformance.

Constraint entailment uses the [limited proof system](#445-constraint-proof-system). **Design boundary:** Associated-Type resolution/equality remains separately specified; the proof system does not invent associated-Type bindings or solve projection equality.

#### 4.4.2. Requirement expressions

`subject is requirement` tests a Type or Type Semantics in Constraint Clauses, Associated Clauses, `#if` conditions, and `#case` conditions. Its result is a compile-time `bool`; unresolved requirements never become runtime tests. Each context retains its permitted subjects, grammar, and validation rules. Ordinary expressions instead use [runtime type tests](#6651-runtime-is-tests); syntax context, including through parentheses, determines the interpretation before lookup, with no fallback between Type and Value namespaces.

`is` binds on its left at comparison precedence. Its right side consumes a requirement expression through `or` precedence. An immediately following `not` negates that entire right side:

| Form | Meaning |
| --- | --- |
| `T is A and B` | T satisfies both A and B. |
| `T is A or B` | T satisfies A or B. |
| `T is not A or B` | T does not satisfy `(A or B)`. |
| `T is A and not B` | T satisfies A and does not satisfy B. |

Use `(T is A) and enabled` to combine a complete test with another condition. Use `T is (not A) or B` to limit negation to A. Value equality uses `==`.

#### 4.4.3. Runtime contracts

Compile-time capability satisfaction and runtime view usability are separate. A runtime contract exposes requirements through dynamic dispatch without defining instance state or requiring a separate `virtual` designation for each requirement. Same-named members alone do not register conformance.

**`RuntimeUsable(C)`** holds exactly when `C` is explicitly designated for runtime use and, after substituting type arguments and associated Types, every instance requirement satisfies:

| Aspect | Initial requirement |
| --- | --- |
| Receiver | Shared or Exclusive object access, without consuming ownership |
| Operation | Safe method or Property accessor |
| `Self` | No implementation-dependent `Self` outside the receiver |
| Binding | No unbound generic/associated Types or method-specific generic parameters |
| Signature | Parameter/result Types are determined without knowing the hidden concrete Type |
| Origins | Expressible through borrowed receiver, explicit inputs, and `static`; no hidden payload Origin, contract-level abstract Origin, or higher-ranked requirement |

Check every getter result and setter requirement separately. Forming `objref/C` or another object form requires `RuntimeUsable(C)`, even when the concrete payload is unknown; it does not require searching all implementations. Unsupported requirements cannot simply be removed from the view. For example, `equals(other: Self)` cannot become heterogeneous comparison between arbitrary `objref/C` values.

**`Implements(D, C)`** checks explicit or inherited conformance. Every requirement has one effective implementation for `D`, with compatible signature, access, Origins, and `ObjectCompatible` at the required receiver kind. Retain legitimate generic obligations until finalization; unknown does not mean satisfied.

Conformance is unique for the same concrete Type and fully bound contract view. Multiple different contracts are allowed; declaration identity distinguishes same-named requirements. Inherited conformance persists in derived Types: virtual requirements follow valid overrides; added same-named non-overrides do not change the mapping. Reject an override that breaks conformance rather than making `Supports(D, C)` disappear for that derived Type.

No duplicate replacement conformance, default implementation, contract inheritance, or external conformance registration is introduced; registration and artifact consistency follow [metadata](#1481-type-identity-and-descriptors). Final declaration and associated-type-binding syntax remains deferred; the conformance and dispatch rules are normative.

```text
Speaker requires Shared speak() -> string
    ├─ Dog implements Speaker.speak
    └─ Cat implements Speaker.speak
```

```kimi
// Assume the conformance above and makeDog() -> obj/Dog.
func announce(value: objref/Speaker) -> string
    return value.speak()

let dog = makeDog()
let message = announce(dog@objref/Speaker)
```

Only requirements accessible through `Speaker` are available from that view. Lifetime-preserving view formation additionally obeys [object upcasts](#6647-object-upcasts).

#### 4.4.4. Callable constraints

`F is Callable<r, S>` is a built-in Constraint requiring calls with receiver access `r` and signature `S`. `Callable<S>` abbreviates `Callable<ref, S>`. Initially `r` is the literal Semantics `ref`, `uniq`, or `owner`, and `S` is `(A1, ..., An) -> R` with an `Owned` complete result Type.

| Constraint receiver | Receiver acquired by a generic call | Admitted minimum call requirement |
| --- | --- | --- |
| `ref` | `ref/F` | Shared |
| `uniq` | `uniq/F` | Shared or Exclusive |
| `owner` | Owned `F` | Shared, Exclusive, or Consuming |

Admitted Types are Function Items, concrete Closures, and common Function Types with [compatible signatures](#528-callable-signature-compatibility). No user `call` member is searched. `S` is a contract, not a conversion to an erased container. Preserve `F`'s complete dependencies under generic substitution; neither Copy nor Owned is required of `F`. `owner` denotes acquisition, whereas `Owned` denotes absence of non-static dependencies. The result restriction does not constrain direct concrete-Closure calls.

An omitted Origin on each direct `ref/T` or `uniq/T` parameter of `S` is independently bound **per call**, for all three receiver kinds. Conformance must hold for every valid call-time Origin under ordinary Type/Loan rules, not one fixed long-lived Origin. Origins nested within `T` and capture-derived Origins within `F` remain fixed and are not quantified. This limited input-borrow quantification adds neither general higher-ranked Origin syntax nor explicit `from` or Origin parameter declarations inside `S`.

```kimi
func applyBorrowed<T, U, F>(value: ref/T, transform: ref/F) -> U
    U is Owned
    F is Callable<(ref/T) -> U>
    return transform(value)
```

A constraint-based call first acquires the receiver required by `r`, then adapts to the implementation's minimum requirement:

- `ref`: call through shared access.
- `uniq`: retain exclusive access for the call, reborrowing exclusively or sharing for the actual body.
- `owner`: acquire by ordinary Copy/Move; borrow that acquired value for a Shared/Exclusive body or transfer it to a Consuming body. Remaining acquired storage is destroyed on call completion. This owned temporary has normal writable/exclusive access even when copied or moved from a `let` binding.

Specialization cannot turn an `owner` acquisition into a borrow merely because the body is Shared. Conversely, copying a Consuming callable does not satisfy a `ref` or `uniq` constraint. Select one public signature; for multiple available constraints with that signature, prefer the weakest declared receiver in order `ref`, `uniq`, `owner`. A later initialization, access, or Loan failure cannot select another receiver. Constraint strength is not an overload-ranking rule.

```kimi
func applyTwice<F>(value: i32, transform: uniq/F) -> i32
    F is Callable<uniq, (i32) -> i32>
    let first = transform(value)
    return transform(first)

let count: i32 = 0
var transform = func [var count] (value: i32) -> i32
    count += 1
    return value + count

let result = applyTwice(10, transform@uniq) // 13; captured count is now 2.
```

A Shared callable also qualifies, but a `uniq/F` argument still needs ordinary exclusive access. Shared environment access does not prevent use of a separate `uniq/T` argument's normal exclusive capability. By-value `F` parameters use normal Copy/Move; this constraint does not silently borrow them or guarantee repeated calls or non-escape. Generic conformance and result-Owned obligations must resolve before finalization.

#### 4.4.5. Constraint proof system

Constraint meaning and available proof are distinct. `and`, `or`, and `not` retain their Boolean meanings, but generic checking uses only the rules below. The accepted programs must not depend on optional SAT solving, arbitrary theorem proving, enumeration of Types, or optimizer-derived facts. Fully determined concrete conditions still use ordinary Boolean evaluation.

In this section `P` and `Q` denote validated, bound propositions. Parse requirement expressions under [requirement precedence](#442-requirement-expressions) before interpreting them: `T is (A and B)` supplies the propositions `(T is A) and (T is B)`, and similarly for `or` and the scope of `not`. This interpretation does not change source precedence. Proposition identity uses bound subject and requirement Symbols, substitutions, and normalized complete Types, including Semantics and Origins where applicable; equal source spellings alone are insufficient. Parentheses are transparent and `not not P` normalizes to `P`. No De Morgan, distributive, or other logical-equivalence normalization is added.

The proof judgment has four outcomes, distinct from the Condition evaluator's outcomes:

| Outcome | Meaning |
| --- | --- |
| **Proven** | The permitted rules establish the proposition. |
| **Refuted** | The permitted rules establish its negation; absence of proof is insufficient. |
| **Unknown** | Neither polarity is established by the permitted rules. This is not a Boolean value or an automatic right to defer. |
| **Error** | Invalid names, subjects, requirements, declarations, or detected contradictory evidence prevent a valid judgment. |

Evidence comes from the current declaration's validated input Constraints, facts introduced by selected compile-time conditions, defined built-in capability rules, and verified explicit or inherited conformance. Facts retain their Binding Identity, substitutions, and lexical/specialization scope. A generic input Constraint is an assumption inside the constrained body, but must be discharged at use. A conformance declaration or `Self is C` obligation cannot prove its own implementation merely by being declared; its required implementations and prerequisite Constraints must be validated under the existing conformance rules. Built-in derivation exceptions such as `Self is Copy` retain their own rules.

| Proof rule | Permitted derivation |
| --- | --- |
| Exact assumption | A matching available proposition proves itself; an available `not P` refutes `P`. |
| Conjunction elimination | Available `P and Q` supplies both `P` and `Q`, recursively. |
| Conjunction introduction | Prove `P and Q` when both operands are Proven. |
| Disjunction introduction | Prove `P or Q` when either operand is Proven. This grants no evidence for the other operand. |
| Negation | Exchange Proven and Refuted for `not P`; Unknown remains Unknown and Error remains Error. Double negation is normalized as above. |
| Boolean refutation | Refute `P and Q` if either operand is Refuted; refute `P or Q` if both are Refuted. |
| Concrete atomic judgment | Use defined concrete Type-identity, Semantics/category, and built-in capability tests, or the closed conformance judgment below. |
| Verified conformance | Prove an explicit or inherited conformance after proving its prerequisites and validating its effective implementations. Retain legitimate unresolved prerequisites. |
| Other cases | Unknown, unless validation requires Error. |

All operands must be valid. Error is absorbing even when another operand determines truth. Otherwise, an unresolved operand does not prevent conjunction refutation from a Refuted operand or disjunction introduction from a Proven operand. Exact compound assumptions remain usable without proving each operand separately, but only conjunction elimination exposes component facts. In particular, `P or Q` together with `not P` does **not** prove `Q` in this system. No case analysis, contraposition, proof by contradiction, or inference from contradiction is permitted. These restrictions apply to symbolic proof, not to Boolean evaluation after concrete atomic judgments have determined operand values.

**Concrete and closed-world judgments.** Fully bound Types may be compared by normalized identity; fully determined Semantics/category and built-in capability tests use their respective rules. An unbound Type or an unresolved structural prerequisite must not be treated as a negative result. For conformance, absence is Refuted only after the relevant concrete declaration, inherited conformances, and all potentially applicable conditional conformances are complete in the fixed compilation environment, with no pending merge, selection, binding, or prerequisite that could establish it. All such alternatives must be ruled out by the specified rules. A failed lookup alone does not establish negative conformance. Malformed or invalid conformance declarations are Error, not evidence of absence. An unsupported associated-Type operation is not a concrete negative judgment.

**Recursion.** Revisiting the same active proof obligation with the same bound arguments supplies no evidence. A cycle alone cannot prove or refute conformance, and an in-progress registration is not verified conformance. Independent finite evidence may still resolve the obligation; otherwise it remains Unknown until its resolution deadline and is diagnosed if still required. A temporary cycle result must not prevent later independently established evidence from being considered. Do not reject recursive Types merely because their declarations are recursive. Structural built-in analyses with separately defined recursion or fixed-point rules use those rules; this section adds no general coinductive conformance assumption.

**Contradictions.** After the specified normalization and conjunction elimination, directly available `P` and `not P` are contradictory evidence and produce Error. Likewise, evidence establishing both polarities of a queried proposition is Error. Do not derive arbitrary capabilities from an inconsistent environment. Detection of more complex contradictions by excluded logical transformations is not required and cannot supply proof.

**Use and finalization.** A required Constraint succeeds only when Proven. Refuted fails the requirement; Error reports invalid input or contradictory evidence. Unknown may be retained only when an identified later binding, specialization, or prerequisite analysis can resolve it before the applicable deadline. Unknown without such a dependency is an unproven-requirement error when a proof is required. Every required concrete-call or specialization obligation must be resolved before finalization. Unknown is never accepted, converted to Refuted, or used as evidence for a negated Constraint. Associated-Type resolution and stronger symbolic reasoning remain design boundaries.

### 4.5. Bindings

Properties and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` declares a mutable binding. A Type annotation and an initializer are independently optional when the omitted information can be inferred.

**Basic example.**

```kimi
let limit: i32 = 10
var current = 0
```

A local Type must be fixed at declaration, even without an initializer. An explicit local Type with an omitted borrow Origin or required Origin argument needs a declaration initializer under [Origin inference](#94-origin-elision-and-return-contracts); a later first assignment cannot supply the missing annotation. A fully specified Type may still omit its initializer under the ordinary initialization rules. Locals become visible after their declaration, so an initializer `let x = x` refers to an outer `x`; duplicate and forward-reference rules follow [name visibility](#511-namespaces-roles-and-visibility). `let` permits only its first initialization, and Move never resets that history. Definite initialization and permitted reinitialization follow [initialization-state rules](#911-storage-state-and-responsibility).

### 4.6. Functions

A function begins with `func`, followed by its Name, optional generic parameters, optional Origin parameters, and a parenthesized parameter list. A result Type follows `->`; whether it is mandatory or may be inferred depends on declared accessibility under [inference boundaries](#525-inference-boundaries-and-specialization). A definition has an indentation-delimited Block body or a single expression introduced by `=>`.

#### 4.6.1. Function bodies and results

A **Block-bodied function** requires explicit `return` for non-Unit results. Every direct expression, including the last, uses Discard Context. Nested Value Contexts, such as initializers, retain their usual rules.

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

An **Expression-bodied function** evaluates the expression after `=>` in Value Context and uses its normal result as the function result. A `return` executed inside that expression may also supply the function result.

```kimi
func add(left: i32, right: i32) -> i32 => left + right
```

Both forms follow the shared [result validation](#78-result-validation), [reachability](#782-reachability), and [scope-exit destruction](#102-scope-exit-destruction) rules. [Function Boundaries](#753-function-boundaries) lists the other bodies to which these rules apply.

#### 4.6.2. Function constraints

A generic Block-bodied function may begin its body with [Constraints](#44-constraints). Its Constraint Clauses must precede every executable body item and are processed at compile time; they are not executable expressions.

**Basic example.**

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

Each clause subject must name a generic parameter of that function. In this example, the two clauses jointly form its Constraints; requirement syntax follows the shared [Constraints](#44-constraints) rules.

Every explicit or inferred generic argument at a call site must satisfy its clauses. Body type checking and specialization may rely on those requirements. Constraints are not part of the function Signature; declarations differing only in their Constraints conflict.

#### 4.6.3. Unsafe functions

An **unsafe function**, declared with `unsafe func`, requires its caller to satisfy documented memory-safety conditions for their documented duration. Calling it requires an [Unsafe Block](#733-unsafe-block); violating its safety contract is undefined behavior. This runtime safety contract is distinct from Constraints and their Constraint Clauses.

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

#### 4.6.4. Parameter names and defaults

A parameter may separate its external argument name from its local name with `external => internal: T`. An optional parameter uses `name?: T = defaultExpression`. The `?` requires a default and means argument omission, not a nullable Type; a default alone does not make a parameter optional.

At each call, evaluate omitted defaults once in parameter declaration order, after all explicit arguments. Resolve default expressions in the declaration's scope. They may refer to preceding parameters, but not later parameters or caller-local bindings.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
let result = scale(3, by: 4)

func offset(value: i32, by?: i32 = 1) -> i32 => value + by
let next = offset(3)
let adjusted = offset(3, by: 5)
```

Function Types retain neither argument names nor defaults. Calls through function values supply all arguments positionally. See [invocation](#642-invocation-and-generic-application) for argument matching and evaluation.

## 5. Name resolution, overload resolution, and inference

Name resolution identifies declarations; overload resolution selects an applicable operation before use-site legality is checked.

| Term | Meaning |
| --- | --- |
| Binding / Name resolution | Associating source names and operations with declarations and meanings. |
| Lookup environment | The declarations, aliases, and extensions available for a lookup in one scope. |
| Project root | Root of the primary module's declaration hierarchy. |
| Compilation root | Lookup entry point for the project root and directly referenced module names. |
| Source environment | The definition-site aliases and lookup context of one source document. |

### 5.1. Name resolution

Resolve Names before selecting overloads or checking whether an operation can execute:

```text
Name Resolution
├─ Type name -> Type Name Selection -> Type Legality
└─ Call      -> Candidate Applicability -> Best Candidate -> Usage Legality
```

**No Backtracking:** once a stage commits its result, a later failure must not select another lookup stage, declaration, or overload. Inaccessible or wrong-role declarations do not commit lookup. Legitimate dependencies may defer resolution; unknown Names and unresolvable dependency cycles are errors.

Apply language-defined lexical visibility and compile-time selection order first. File loading, alias order, candidate enumeration, caching, parallelism, and optimization must not change resolution.

#### 5.1.1. Namespaces, roles, and visibility

Namespaces separate declaration kinds; a **Lookup Role** filters candidates by syntactic use before lookup stops.

| Namespace | Declarations |
| --- | --- |
| Type | Containers, Kotonoha reference names, Core Type and Semantics parameters, associated Types, `Self` |
| Value | Functions, Properties, parameters, locals, local functions |
| Origin | Origin declarations |
| Label | Labels; use the dedicated transfer-target rules |

| Lookup Role | Namespace | Eligible declarations |
| --- | --- | --- |
| Core Type | Type | Structures, enums, Core Type parameters, associated Types, `Self` |
| Object View Target | Type | Core Types or runtime contracts; validate specialization and runtime usability after selection |
| Type Semantics | Type | Semantics parameters and language-defined Semantics |
| Qualifier | Type | Groups, Kotonoha references, and Types that can qualify members |
| Requirement | Type | Capabilities, Types, Semantics, and categories allowed by requirement syntax |
| Declaration Container | Type | Containers allowed as an alias or other Container target |
| Value | Value | All value declarations, including non-callable values |
| Origin / Label | Corresponding namespace | Origin / Label declarations |

A declaration may serve several roles. A group is a Qualifier, not a Core Type. Role filtering does not inspect generic arity, argument Types or labels, expected results, satisfied Constraints, or the existence of a later member. Object-target syntax selects the View Target role; subsequent RuntimeUsable failure does not reopen lookup. There is no callable-only role for `f()`.

Type and Value names may coexist. Within one namespace and scope, only valid Container merging, distinct Type Signatures, and function overloads permit repeated names. Different roles do not permit conflicting declarations such as `group X` and `struct X` in the same scope.

Locals become visible after their declaration; their own initializer uses the outer environment. Inner blocks may shadow outer locals, but a block cannot redeclare a local name. Parameters and the immediate function body share one declaration space for duplicate checks. Local functions are visible from block entry and may overload by Signature; they cannot collide with a local or parameter in that space. Container members allow forward references, without granting permission to read uninitialized values.

```kimi
func example() -> i32
    let x = 10
    if true
        let x = x + 1       // RHS uses the outer x.
    let result = later()    // Local functions allow forward references.
    func later() -> i32 => 20
    return result
```

Type parameters belong to their declaring function or Type scope. Nested functions may use outer Type parameters, but runtime bindings cross a Function Boundary only through the anonymous-function capture rules; named nested functions do not capture runtime locals. Top-level locals and local functions remain private to their SourceDocument's execution scope.

`Self` is reserved, cannot be redeclared, and requires a valid Type context. `self`, `storage`, and `value` are contextual names introduced by receiver and accessor rules. While active, they cannot be redeclared as locals or parameters; elsewhere they are ordinary Names. Their runtime bindings are not implicitly captured by nested functions. Origin and Label lookup never falls back to Type or Value names.

#### 5.1.2. Accessibility and reachability

| Access | Scope |
| --- | --- |
| `private` (default) | Declaring Container and bodies lexically nested within it |
| `internal` | Same Kotonoha |
| `protected` | Declaring structure and bodies of its directly or indirectly derived structures |
| `protected internal` | Same Kotonoha **or** the protected scope |
| `private protected` | Same Kotonoha **and** the protected scope |
| `public` | Also accessible from other Kotonoha libraries, subject to enclosing restrictions |

The protected scope includes bodies lexically nested in the declaring or qualifying derived structure. Protected forms apply only to structure members and their accessors; they are invalid on root/group declarations, group members, and contract requirements. They do not enable nested Container declarations inside structures. A declaration has one access specification; only the two compound forms shown above may combine access words, in the shown order. Duplicate or other combinations are errors. `open` is an inheritance modifier, not an access level. Non-inheritable structures may retain protected members, but gain no derived access sites.

Accessibility grants permission; **Name Reachability** supplies a valid path through scopes, qualification, aliases, or explicit re-exports. Both, plus role compatibility, are required. Public declarations are not automatically imported. Each enclosing Container and access path must allow access; aliases and re-exports cannot widen it. API signatures obey the domain checks below, and consumers naming their Types or requirements must additionally have a reachable path under the [module rules](#12-modules-and-dependencies).

Private access uses the merged Container Symbol, not the file. Other fragments of that Container have access; unrelated declarations in the same file do not. A parent does not gain access to a child's private members merely by containing it. An extension does not gain the target's private access. Accessors inherit their Property's access and may only narrow it. Locals, parameters, and contextual names use lexical visibility instead of access modifiers.

```kimi
// A.kimi
group Vault
    private var secret: i32 = 7

// B.kimi
group Vault
    func read() -> i32 => secret       // Same merged Container: allowed.
group Other
    func read() -> i32 => Vault.secret // Error, even if moved to A.kimi.
```

An inaccessible declaration is diagnostic evidence, not a candidate that stops lookup. Once a Property or member has been selected, an inaccessible required accessor or missing receiver is an error; do not resume outer lookup.

##### 5.1.2.1. Effective access domains and protected receivers

The **effective access domain** `Access(D)` is the set of source contexts permitted to access declaration `D`, considering its declared access and every enclosing named Container. For a member of a named Container, intersect the domain supplied by its access specification with that Container's effective domain. Thus a public member of a private group is confined to that group's domain. A root declaration has no enclosing named Container: `private` and `internal` restrict it to its Kotonoha, while `public` permits access from other Kotonoha libraries. The project root does not impose an additional module-only cap on public root declarations. Root declarations remain subject to dependency and Name Reachability requirements.

Compute domains from Symbol identity, merged Container relationships, and the validated inheritance graph, not file paths or currently observed callers. Internal access refers to the originating Kotonoha, not a package, workspace, source directory, or all libraries in a build. No separate package or friend-module access is defined. Declared public access must remain valid for future consumers, even if no other module currently references it. `internal` and `protected` are not linearly ordered: the former includes unrelated code in one Kotonoha, and the latter can include derived code in another.

When access to an instance member of base structure `B` relies on the protected permission of derived structure `D`, the receiver's static Core Type (its Effective Type at that use) must be `D` or derive from `D`. A generic receiver may use a proven corresponding base constraint. A receiver statically typed as `B` or as a sibling of `D` is insufficient, even if its runtime value is a `D`. Check each required accessor independently. Access inside `B`'s own lexical body does not need this extra derived-receiver restriction. For `protected internal`, access authorized by the same-Kotonoha branch likewise does not need it; `private protected` must satisfy both the Kotonoha and protected conditions. Static members have no instance-receiver restriction. None of these rules supplies a missing receiver or an otherwise undefined receiver conversion.

For generic declarations, the protected lexical scope includes structures derived from any construction of that declaration; the instance-receiver check still uses the actual derived receiver Type. Inheritance alone never grants private access. An extension receives no special private or protected privilege merely by targeting a Type; check accesses using its own lexical context and any independently established inheritance relationship.

For example, if `Left` and `Right` derive from `Base`, code in `Left` may use a protected `Base` Property through a `ref/Left` receiver, but not through a `ref/Base` or `ref/Right` receiver. Ownership, borrow permissions, and accessor availability must also hold.

##### 5.1.2.2. API signature accessibility

Every declaration `D` must satisfy the following for each concrete Type or requirement `T` exposed in its API signature:

```text
Access(D) is a subset of Access(T)
```

Check the effective domains, not merely the written access modifiers. This rule applies to private, internal, and protected declarations as well as public declarations. A direct base Type must satisfy the same condition with the derived Type as `D`. Apply the rule after declaration merging and Type normalization; an invalid exposed signature is a declaration error even if never used. An inferred Type is checked once established and cannot evade this rule.

API components include function parameter and result Types (including receivers), constructor parameter Types and the constructed result Type, Property Types, accessor parameter and result Types, and Types or Contracts named by generic constraints and exposed associated-type requirements. Use the Property's domain for its declared Property Type, and each accessor's domain for its additional signature components. A restricted setter does not narrow the Property's domain. Constraints require this check even when textually written inside a function body. Validate exposed Origin contracts under their own scope and lifetime rules as well.

Inspect compound Types recursively. A constructed generic Type has the intersection of the generic declaration's domain and all its concrete type arguments' domains. Tuples, function Types, arrays, and other compound Types likewise require every constituent Type to be accessible. Type Semantics such as `ref`, `obj`, and `unsafe` do not conceal an inaccessible Core Type or runtime-contract View Target. Expand Type aliases for this check. For an associated-type projection, check its qualifier and defining requirement, and any exposed concrete binding established by the associated-type rules; an unresolved projection retains an obligation rather than silently becoming public. Do not recursively inspect a Type's private fields or implementation bodies merely because that Type appears in an API.

Primitive Types and language-defined public requirements impose no additional access restriction. Generic parameters are symbolic API parameters, not private concrete Types: their lexical declaration scope does not restrict the whole generic API to its body. Check their declared constraints instead. In particular, a public generic declaration need not restrict all future type arguments to public Types.

```kimi
public group Api
    private struct Hidden
    public struct Box<T>

    public func identity<T>(value: T) -> T => value
    public func expose(value: Box<Hidden>) -> () => () // Error.
    internal func leak(value: Hidden) -> () => ()      // Error: wider than Hidden.
    private func keep(value: Hidden) -> Hidden
        return identity<Hidden>(value) // Allowed: Hidden is accessible here.

    private group Implementation
        public func keep(value: Hidden) -> Hidden => value
        // Allowed: both keep and Hidden are accessible only within Api's body.
```

Similarly, a generic function exposed beyond a private Contract's domain cannot name that Contract in its constraints. A public `Box<T>` may be instantiated as `Box<Hidden>` wherever `Hidden` is accessible, but that constructed Type cannot be exposed beyond `Hidden`'s domain. An internal API may use an internal Type when its domain covers that API, including a public member inside an internal Container. Accidental equality of domains in one build must not erase the protected or module conditions required for future consumers.

##### 5.1.2.3. Generic bodies and separate compilation

Check generic body access to nondependent Types and helpers in the declaration's definition-site context. Deferred Binding and specialization retain that context and the original resolved Symbols. A public generic body may use private implementation Types or helper functions without exposing them in its API signature. Instantiation in another Kotonoha does not recheck those implementation accesses as if the body were written by the caller, grant the caller private access, or require the helpers to become public.

At a generic use, check supplied Types and constraints using the normal use-site rules. A caller may supply an accessible private Type to a public generic function; specialization does not turn that concrete instance into a new externally public declaration. A wrapper or other declaration that exposes the resulting constructed Type must independently satisfy API signature accessibility. Preserve sufficient private implementation metadata for specialization without making its Symbols source-addressable to consumers.

#### 5.1.3. Unqualified lookup

For the required namespace and role, search these stages in order. A bare Adaptation Target Name in `E@X` uses the two roles specified by [explicit operations](#664-explicit-operations); it does not select a role using conversion success.

1. Current local scope, then enclosing lexical and parameter scopes, each separately.
2. Current Container.
3. Parent Containers, separately, through the project root.
4. Direct-dependency Kotonoha reference names in the Compilation root, for Type qualifiers only.
5. Explicit aliases of the use's SourceDocument, together.
6. Compilation default aliases, together.

Type parameters and contextual names occur at their declaring lexical positions. Stage 4 finds library reference names, not arbitrary library members.

At each stage, collect same-name declarations in the namespace, deduplicate paths to the same Symbol, and filter by role and accessibility. Stop at the first stage with eligible declarations. Same-name functions form one candidate set; a function and a Property, or other distinct value kinds, conflict at that stage. Type candidates proceed to Type Name Selection.

After stopping, wrong type arguments, labels, constraints, argument Types, or a non-callable value are errors at that stage. Do not search farther, even when an outer declaration would work. Arity-based indexing must preserve this boundary.

```kimi
group Outer
    func f(value: i32) -> i32 => value
    group Inner
        func f(value: string) -> string => value
        func test() -> ()
            f(1)           // Error: Inner.f requires string.
            ::Outer.f(1)   // Explicitly selects Outer.f.
```

A nearer group `X` does not stop Core Type lookup for an annotation `X`, but does stop Qualifier lookup for `X.member`. If that group lacks `member`, do not switch to an outer `X`. Similarly, an integer local `f` stops Value lookup and makes `f()` a non-callable-value error.

There is no implicit `self`: instance members require `self.member` or another explicit receiver. An unqualified reference that finds only accessible instance members reports a missing receiver instead of searching for an outer static member.

If all stages fail, prefer an inaccessible matching-role declaration diagnostic, then an accessible wrong-role diagnostic, then undefined Name. Diagnostic exploration of outer declarations never makes them valid fallback targets.

#### 5.1.4. Qualification and extensions

Resolve the first component of `A.B.C` by ordinary lookup with its syntactic role, then search only the selected target's members. Do not return to its parents. Intermediate Type-side components are Qualifiers; the final role follows the syntax, such as Core Type in a Type annotation or Declaration Container in an alias.

Where both Type and Value qualification are syntactically possible, explore both without preferring values:

1. Commit the first eligible lookup stage independently in the Type/Qualifier and Value namespaces.
2. Follow each complete path, checking member roles, accessibility, and static/instance use; retain both paths at any genuine Type/Value branch.
3. Select the sole successful path, report ambiguity for distinct successful paths, or report lookup failure if none succeeds.

Deduplicate identical references to the same Symbol, but not value accesses with different receivers. Once a first component is committed within a namespace, later failure cannot substitute an outer declaration. Call arguments and expected return Types cannot resolve a Type/Value path ambiguity: choose the function-group path before overload resolution. A call needed to determine an intermediate receiver Type must be resolved independently.

```kimi
group Config
    public var count: i32 = 3

// Settings has a public instance Property count.
func read(Config: ref/Settings) -> i32
    return Config.count     // Error: both Type and Value paths succeed.
// ::Config.count selects the group; renaming the parameter selects the value.
```

`::A.B` starts at the Compilation root: project-root declarations and direct-dependency reference names. It ignores local scopes and all aliases but still checks roles and access. A project-root declaration cannot share a dependency reference name, even across namespaces; ordinary namespace and Signature rules still govern root declarations among themselves.

Member lookup searches ordinary members before extensions. An accessible, role-compatible ordinary member commits lookup even if no overload applies; inaccessible or wrong-role members alone do not block extensions. Search only extensions enabled by the use's lexical scope, then its source aliases, then default aliases, respecting separate stages and combining candidates within a stage. Do not add argument-associated Containers automatically.

Generic bodies use their [definition-site source environment](#12-modules-and-dependencies), including during deferred specialization; caller aliases and extensions never enlarge their candidate sets.

#### 5.1.5. Type name selection

After committing lookup, filter Type candidates by the number and kinds of explicit type arguments. Resolve the arguments themselves in the use-site context. Select exactly one candidate; zero means type-argument mismatch and several mean ambiguity. Check the selected Type's Constraints afterward, without trying another Type if they fail.

For example, `Box<i32>` selects `Box<T>` from a stage containing `Box<T>` and `Box<T,U>`. A nearer stage containing only `Box<T,U>` blocks an outer `Box<T>`. Different same-arity Types imported at one stage remain ambiguous. Legitimate unresolved argument kinds defer selection with its stage fixed; malformed arguments or unknown Names are errors. Omitted type arguments use only the inference permitted by their construct.

### 5.2. Overload resolution and inference

Use the distinct relations in [Type relations and expression operations](#39-type-relations-and-expression-operations): argument adaptation, expected-result compatibility, and acquisition legality are separate judgments. A successful subtype proof does not select or authorize a value operation.

#### 5.2.1. Candidate applicability

Check each declaration in the committed function group independently:

1. Validate explicit type-argument count and kinds.
2. Match positional and named arguments and record omitted defaults.
3. Infer type arguments from the receiver and explicit arguments.
4. Use an independently known expected result Type to fill remaining type arguments, without changing those already fixed.
5. Check permitted argument adaptations, Function Types, and Constraints.
6. Reject instantiated result Types incompatible with the expected result, if present.

Zero applicable candidates is an error. Candidate checking records plans; it does not execute or commit runtime Copy/Move, Loans, or defaults. Errors in declarations, such as unknown Types, malformed Constraints, or duplicate Signatures, remain declaration errors even when another candidate succeeds.

Positional arguments precede named arguments and bind parameters in order. Named arguments use external names, may be reordered, and cannot bind a parameter twice. Reject unknown labels, excess positional arguments, and missing required arguments. Evaluate explicit arguments in source order, then omitted defaults in parameter order; defaults follow [declaration-site rules](#464-parameter-names-and-defaults) and supply no generic-inference evidence. Function-value calls supply every argument positionally.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // Valid; evaluate by before value.
scale(3, value: 4)     // Error: value supplied twice.
scale(3, factor: 4)    // Error: factor is an internal name.
```

#### 5.2.2. Argument adaptation and literals

Compare adaptations in this order, best first:

| Class | Meaning |
| --- | --- |
| Exact | Normalized Type compatibility requiring no adaptation operation, including no borrow/reborrow |
| Literal fitting | Directly fit an unresolved literal to the candidate's Type |
| Same-semantics reborrow | Reborrow while preserving the input Type Semantics |
| Cross-semantics borrow/reborrow | Another permitted borrow or reborrow |

Exact describes Type adaptation, not value transfer: an Exact by-value argument still Copies or Moves under [Copy and Move](#35-copy-and-move). Copy versus Move adds no ranking preference. Origin subtyping that needs no value operation remains permitted.

The initial borrow adaptations are listed below. In the first two rows `T` has owner Semantics; in value-reborrow rows it is the complete immediate Referent Type. Adding a layer around an existing reference or object handle requires a fully specified explicit [storage-borrow target](#6645-explicit-borrow-and-reborrow); it is not an additional implicit argument adaptation.

| Input | Expected | Operation/class |
| --- | --- | --- |
| Readable `T` place | `ref/T` | Shared borrow / cross |
| Exclusively writable `T` place | `uniq/T` | Exclusive borrow / cross |
| `uniq/T` | `uniq/T` | Call reborrow / same |
| `uniq/T` | `ref/T` | Shared reborrow / cross |
| Accessible `obj/T` place | `objref/T` | Shared object borrow / cross |
| Exclusively writable `obj/T` place | `objuniq/T` | Exclusive object borrow / cross |
| `objuniq/T` | `objuniq/T` | Object call reborrow / same |
| `objuniq/T` | `objref/T` | Shared object reborrow / cross |

A required exclusive reborrow is not Exact even when the written Types match. Check Type/declaration permissions and place-versus-temporary form during applicability; check flow-dependent initialization and active Loan conflicts after selection. Borrow adaptations neither extend lifetimes nor duplicate ownership. Do not infer missing `rc`/`arc` or temporary adaptations from this table; the complete finite adaptation table remains a separate requirement.

Fixed-expectation [common function conversion](#6434-function-references-and-common-type-conversion) is handled separately and adds no rank to this table. Do not add implicit object upcasts, numeric-width, signedness, integer/float, or user-defined conversions, or unlimited dereference/conversion chains. Raw dereference is explicit. Existing Never and Origin rules remain Type rules, without new overload priorities.

Untyped integer literals fit representable candidate integer Types directly; floating literals follow the numeric rules. Do not default to `i32`/`f64` before fitting, prefer narrower widths, or break overload ambiguity using defaults. Outside candidate comparison, independent expressions without an expected Type use the ordinary numeric defaults. Generic inference processes receiver, other-argument, and known-result constraints before defaulting. `null`, empty collections, and untyped functions gain no universal fallback Type.

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(1)      // Error: both integer Types fit.
let x = 1      // Independently defaults to i32.
choose(x)      // Exact i32.
choose(1@i64)  // Exact i64.
```

#### 5.2.3. Expected results

This is the static expected-result judgment in [the relation table](#39-type-relations-and-expression-operations), not the implicit-expression-adaptation judgment.

An expected result may complete inference and exclude otherwise applicable candidates. Compatibility requires normalized Type identity or a defined subtype relation without additional value operations. Instantiate and check Origins, including permitted covariant shortening. Do not insert a new borrow/reborrow, dereference, numeric conversion, or user conversion to retain a candidate. Do not retype a function's body literal to change its established return Type.

Expected results do not rank candidates by result-conversion quality. Declared result Loan/Origin propagation and result Copy/Move still apply. The expected Type must come from a surrounding annotation, fixed parameter, or declared result, not circularly from the candidate being selected. Discard Context supplies no expected Unit Type.

```kimi
func fetch(value: i32) -> string => "text"
func fetch(value: i64) -> i64 => value
let text: string = fetch(1) // Selects fetch(i32) by result compatibility.
fetch(1)                   // Error: discarding leaves both candidates.
```

These declarations have distinct parameter Signatures. Declarations differing only in return Type are invalid regardless of call-site expectations. A candidate returning `T` cannot survive an expected `ref/T` by borrowing its result; nor can `uniq/T` become `ref/T` by a newly inserted reborrow.

#### 5.2.4. Best candidate

Pairwise comparison yields better, worse, equivalent, or incomparable. **Proceed to the next step only for equivalent candidates.** Select a candidate only if it is better than every other applicable candidate:

1. Compare adaptation quality for the receiver and each explicit source argument. A dominates B only if it is no worse everywhere and better somewhere. All equal proceeds; opposing advantages are incomparable. Match named arguments by the same source expression, not candidate parameter order. Exclude defaults and never sum numeric costs.
2. Compare substituted parameter Types at the same positions. A dominates B if every Type is equal or a defined subtype and at least one is a strict subtype. All equal proceeds; unrelated Types or opposing subtype advantages are incomparable.
3. Prefer a function with no type parameters of its own. A generic enclosing Type alone does not make the function generic.
4. Prefer fewer defaults used by this call.
5. Otherwise report ambiguity.

Do not rank numeric Types by width, Constraints by strength or clause count, or generic declarations by general pattern partial ordering. Do not invent `uniq/T <: ref/T` from the ability to reborrow. Incomparability at an earlier step cannot be rescued by nongeneric status or fewer defaults; declaration, file, alias, and name order never break ties.

```kimi
func inspect(value: ref/i32) -> () => ()
func inspect(value: uniq/i32) -> () => ()
var x: i32 = 0
inspect(x)      // Error: both cross-semantics borrows; Types incomparable.
inspect(x@ref)  // Shared candidate: Exact.
inspect(x@uniq) // Exclusive candidate: same-semantics beats cross-semantics.
let action: (uniq/i32) -> () = inspect
```

The exclusive candidate wins for an exclusive input because its Semantics match, not because exclusivity is stronger. Two candidates `(i32, ref/i32)` and `(ref/i32, i32)` are incomparable for two `i32` locals. Likewise, `f<T>(T)` and `f<U>(Box<U>)` remain tied for `Box<i32>` when substitution makes both parameter Types equal and later steps tie.

These rules deliberately leave owner-to-`ref`/`uniq` overloads ambiguous. Any preference would require a language revision. Property Consume eligibility and its generic limits follow [Property Consume](#89-property-consume).

#### 5.2.5. Inference boundaries and specialization

Fix local Types at declaration; later uses cannot infer backward. Functions declared `public`, `protected`, or `protected internal` require explicit return Types, even if an enclosing Container narrows their effective domain. Functions declared `internal`, `private protected`, or `private` may infer returns, but fix the return Type before the function participates in another expression's overload resolution and validate [API signature accessibility](#5122-api-signature-accessibility). Inference cycles require return annotations. Generic return Types may remain expressions over parameters; substituting them does not reanalyze a body.

Explicit function type arguments must supply the full list. Partial lists and inference placeholders are not defined. Omitted defaults do not infer generic arguments.

Generic inference and substitution preserve all bound Origins and inferred Loan requirements of substituted Types, including nested dependencies. This does not change Core Type/Semantics parameter roles. A surrounding borrow such as `ref/T` retains `T`'s internal dependencies alongside the borrow's own Origin and Loan. Substitution alone creates no Borrow/Reborrow, releases no Loan, and extends no lifetime; actual call-site Origins and Loans follow the [Ownership and Origin rules](#9-ownership-and-lifetime-analysis).

Bidirectional checking supports literals, function references, anonymous functions, and expressions directly checkable against a candidate Type. It does not search combinations by rerunning an inner overload for every outer candidate in `f(g(x))`:

- First process independently typable arguments and explicit Type information.
- An inner expression may use an expected Type shared by all remaining outer candidates.
- If one outer candidate is already determined, use its expectation without reviving eliminated candidates after failure.
- Otherwise require an annotation, explicit type arguments, or a typed intermediate binding.

```kimi
func inner(value: i32) -> i32 => value
func inner(value: i64) -> i64 => value
func outer(value: i32) -> () => ()
func outer(value: i64) -> () => ()
outer(inner(1))                    // Error: would require nested search.
let intermediate: i64 = inner(1)  // Expected result fixes the inner call.
outer(intermediate)
```

Resolve a function reference to one declaration using ordinary selection evidence, including explicit generic arguments or a fixed expected callable signature. A unique candidate needs no expected Type; an unresolved overload set is not a value. Anonymous-function arity and explicit Types may filter candidates, but its body is checked only after a common expected signature or a single candidate is determined. A fixed `Callable<r, S>` signature may guide parameter inference while `F` retains the concrete Closure Type. Do not rerun a body for competing signatures, infer parameters from later uses, or repeat capture effects during candidate trials. Function values carry neither labels nor defaults and cannot name unsafe functions or `deinit`. Later conformance failure never selects another overload or capture mode.

After inference, check Constraints using the [limited proof system](#445-constraint-proof-system). Proven satisfies a requirement, Refuted fails it, and Error diagnoses invalid or contradictory evidence. Unknown retains an obligation only for a legitimate dependency that can resolve before the applicable deadline; it is not an applicable result or a negative fact. Nondependent names bind at the definition. Generic bodies use evidence from declared constraints and selected conditions; failure to prove `T is C` does not prove `T is not C`. Deferred members use the [definition-site source environment](#12-modules-and-dependencies), never caller imports. All necessary constraints must be Proven before finalizing a concrete call or specialization. Do not use arbitrary theorem proving, enumeration of available Types, or constraint strength for overload ranking.

Conditional membership follows the [name-resolution boundary](#134-name-resolution-boundary). Excluded declarations do not merge or enter candidate sets. Compiler requirements preserve independent specialization environments.

#### 5.2.6. Usage legality and operators

After selection, check unsafe permission, initialization and Move state, actual Loans and lifetimes, required accessor access, write capability, and other control-flow or ownership conditions. Static Type and declaration permissions needed for adaptation are checked earlier; flow-dependent failures never change the selected overload.

```kimi
unsafe func inspect(value: i32) -> () => ()
func inspect(value: ref/i32) -> () => ()
let number: i32 = 1
inspect(number) // Select Exact i32, then reject without an Unsafe Block.
```

Adding a better overload can therefore invalidate existing calls even if that overload later fails Usage Legality. `@move` resolves its Access Designator for Consume and checks eligibility and legality; it does not fall back to Read or another same-name declaration.

Operator operands use the same adaptation and candidate-comparison rules while preserving fixed syntax and evaluation order. Extension operators obey member lookup stages. Collection of built-in, contract, and extension operator candidates, construction/indexer integration, and explicit ambiguity-resolution syntax require their own declaration rules; ordinary lookup must not invent them.

Diagnostics distinguish undefined/wrong-role/inaccessible names, path or value-kind conflicts, missing receivers, type-argument or argument mismatch, no applicable overload, ambiguity, inference boundaries, dependency cycles, declaration errors, and usage errors. Show candidate Signatures and declaration locations for ambiguity; retain useful rejection reasons without dumping every tentative error. Resource-limit exhaustion is separate from language ambiguity or mismatch and must request annotations or smaller expressions, never choose the first candidate.

#### 5.2.7. Generic access effects

Before finalizing ownership/Loan analysis for an acquisition or adaptation, determine its **Access Effect** statically and uniquely:

```text
Access Effect
├─ acquisition: Copy / Move / Consume
├─ Loan action: shared Borrow / exclusive Borrow / Reborrow
└─ target Place and Origin dependencies
```

A broad Borrow-versus-acquisition category is insufficient: distinguish Copy from Move, shared from exclusive, and Borrow from Reborrow. Symbolic generic Types/Origins are allowed if the required effect and dependencies are uniquely expressible. Apply this rule to ordinary acquisition and all generic `@s`, `@s/T`, and `@Type` forms.

```text
Generic analysis
├─ effect known -> analyze normally
└─ legitimate dependency -> retain obligation
                            -> constraints / selected conditions / specialization
                            -> determine effect -> finalize ownership and cleanup
```

Do not treat unresolved Copy capability as proof of non-Copy or fix the effect to Move. Generic checking need not finish at definition time. Deferral is permitted only when a later phase can resolve the dependency before finalization; otherwise report an error. Environment-changing directives still obey their earlier [selection deadlines](#134-name-resolution-boundary).

Specializations may have different effects. Check each body and cleanup with its own effects; never reuse a different-effect analysis without validation. An explicit `#case` is not required when specialization directly resolves the operation.

```kimi
// s is a declared Semantics parameter; value is an initialized owned value.
value@s
use(value)
```

With the first result discarded, `s = ref` creates a shared Loan ending in that expression; `s = uniq` similarly requires exclusive writability. The later use is checked normally after the Loan ends. `s = owner` copies a Copy value but moves a non-Copy value, making the later use an error. Retaining a borrow result instead requires checking all later uses in its Loan lifetime.

#### 5.2.8. Callable signature compatibility

Callable variance uses the static Type relations in [the relation table](#39-type-relations-and-expression-operations); common Function Type conversion and receiver acquisition are separate operations.

Common Function Type conversion and `Callable<r, S>` use one rule. For implementation `(A1, ..., An) -> R` and required `(P1, ..., Pn) -> Q`, require equal arity, `Pi <: Ai` at every position, and `R <: Q`. Here `<:` means normalized complete-Type identity or an already defined subtype relation, retaining Semantics and Origins. Parameter labels, defaults, and optionality cannot bridge a mismatch.

Compare all parameter/result Origin and Loan contracts for every admitted call; quantified Origins correspond by abstract parameter, not spelling. Fixed captured Origins cannot be replaced by fresh quantified ones. Do not insert Borrow/Reborrow, dereference, numeric/user conversions, or argument/result erasure to manufacture compatibility, or reinfer committed body/literal Types. Legal covariant Origin shortening remains available; exclusive Core Type invariance and result Loan requirements remain mandatory. Receiver adaptation is checked separately and does not add argument conversions.

Implicit erasure applies only after the expected common Function Type is fixed. No overload ordering between a concrete direct match and an erasure conversion is defined; use an annotation or typed intermediate when that comparison would be needed. Allocation cost never ranks candidates. Receiver, Copy, Owned, or Loan failure cannot reopen selection.

### 5.3. Inference and operation design boundaries

The following boundaries remain separately specified. Implementations must not invent them through broader search:

- Detailed Core Type/Semantics parameter-to-argument correspondence.
- Additional implicit argument/receiver adaptations beyond the defined applicability table; exact contextual-binding boundaries for additional accessor/function forms. The explicit Borrow table does not add implicit overload preferences.
- Operator/indexer candidate collection and explicit selection syntax; combining optional `?` with external/internal parameter-name syntax. Constructor collection is defined under [constructors](#433-constructors).

# Part II. Language constructs and semantics

## 6. Expressions and operators

Expressions produce values or transfer control. This chapter defines their syntax, Type rules, and evaluation.

### 6.1. Classification and contexts

The source-language expression forms are:

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
├─ Explicit @ Operation: Type/Semantics adaptation or Consume
├─ Unary Expression
│  ├─ Sign / Logical Negation / Dereference / From-end Index
│  └─ Prefix / Postfix Increment and Decrement
├─ Binary Expression
│  ├─ Arithmetic / Shift / Bitwise
│  ├─ Comparison / Logical
│  └─ Runtime Type Test
├─ Range Expression
├─ Assignment Expression
│  ├─ Simple Assignment
│  └─ Compound Assignment
├─ Selection Expression: if / match
├─ Iteration / Labeled Block Expression: for / while / loop / Label:
└─ Control Transfer Expression: return / exit / continue / yield

Related Syntax
├─ Block Statement: unsafe: / defer:
├─ Require Statement: require condition else ...
├─ Compile-time Directive: #if / #match
├─ Attribute: #Name
└─ Composition Root: $
```

Value and access categories follow [Values, places, and storage](#34-values-places-and-storage).

A normally completing expression produces a typed result, including [Unit](#315-unit-and-never-types). [Value and Discard Contexts](#72-blocks-and-evaluation-contexts) determine how that result is used. Discarding it preserves side effects and type, ownership, and destruction checks. Assignment requires a writable [place](#34-values-places-and-storage) or an accessible Property setter; a readable Property need not expose borrowable storage.

An ordinary indented Block is a syntax container, not an arbitrary value expression. Use a selection or Labeled Block to obtain a value from several operations. `unsafe:`, `defer:`, and `require` are statements and cannot be initializers or arguments. `let` / `var` declarations are not general expressions; their use in `if` / `while` conditions follows the dedicated [condition syntax](#772-if).

Source delimiters and continuation follow [Lines, indentation, and continuation](#22-lines-indentation-and-continuation).

### 6.2. Evaluation order

Evaluate operands once, from left to right, unless a construct specifies an exception or conditional evaluation. Precedence determines grouping; evaluation order determines the order of effects.

| Expression | Evaluation order |
| --- | --- |
| `a() + b() * c()` | a, b, c, multiplication, addition. |
| `receiver().method(a(), b())` | Receiver, resolve callee, a, b, call. |
| `array()[index()]` | Target, index, element access. |
| `(a(), b())` / `[a(), b()]` | Elements in source order. |
| `[key(): value(), ...]` | Per entry: key, duplicate check, value, insertion. |
| `start()..end()` | Start boundary, end boundary. |
| `"\(a()) / \(b())"` | Evaluate and stringify each interpolation in source order. |

`and`, `or`, and selections evaluate only the required operands or branches. [Simple assignment](#671-simple-assignment) evaluates and secures its right side before its target; compound assignment retains target-first evaluation. Type arguments and conversion target Types are not evaluated at runtime.

An abrupt Completion, divergence, or Panic prevents evaluation of later operands and the enclosing operation. Unevaluated syntax still undergoes name, Type, and transfer-target checks; syntax excluded by `#if` / `#match` follows [conditional compilation](#13-compile-time-directives). [Temporary lifetimes](#36-temporary-values-places-and-lifetimes) and scope-exit rules govern retained values.

### 6.3. Primary expressions

#### 6.3.1. Type inference

Expected Types from declarations, parameters, and results propagate into expressions. Otherwise infer from operands. Ordinary numeric operations require the same numeric Type; integer widths, signedness, and integer/floating-point Types do not mix implicitly.

An untyped integer literal adopts an expected integer Type if its value fits. Without one, it defaults to `i32`; a value outside that range requires an explicit Type. A floating-point literal adopts an expected `f32` or `f64`, defaulting to `f64`. Check a directly negated integer literal as a signed value, allowing the minimum of a signed Type.

Explicit `@f32` / `@f64` on a direct untyped integer literal follows the [explicit-literal adaptation rule](#664-explicit-operations), without an intermediate default integer Type. This does not add implicit integer-to-float fitting.

```kimi
let a: i64 = 10
let b = a + 20          // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64       // Convert an already typed operand explicitly.
let minimum: i8 = -128
```

There are no implicit conversions between `bool`, `char`, and numbers. Conditions require `bool`, not an integer or pointer. Borrowing and reborrowing are separate adaptations governed by ownership rules.

During [overload resolution](#52-overload-resolution-and-inference), fit unresolved literals independently to candidates before defaulting; numeric defaults do not break ties. Expected Types and nested-call/function inference are limited by [inference boundaries](#525-inference-boundaries-and-specialization). Locals never infer backward from later uses.

#### 6.3.2. Names, literals, and grouping

| Form | Meaning |
| --- | --- |
| `name` | Reference to a visible binding, function, or other named entity. |
| `123`, `0xff`, `1.5`, `true`, `'あ'`, `"text"` | Scalar literals; see [lexical structure](#2-source-and-lexical-structure). |
| `"value = \(value)"` | Interpolated string; the embedded Type must support stringification. |
| `null` | Contextually typed [raw null pointer](#381-null-and-equality). |
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

#### 6.3.3. Dictionary construction and duplicate keys

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

If key evaluation, duplicate checking, or value evaluation does not complete normally, do not insert that entry or process later entries. No partially constructed dictionary is returned, and completed side effects are not rolled back. The duplicate's diagnostic location is the later key expression. Termination and cleanup follow [Panic Termination](#113-panic-termination).

### 6.4. Access and application

#### 6.4.1. Member access

`expression.name` selects a member. [Qualified lookup](#514-qualification-and-extensions) distinguishes Container and value paths, reports ambiguity when both succeed, and never implicitly inserts `self`. The right side of an ordinary member-access `.` must be a member Name or an in-range decimal integer literal selecting a Tuple element; `pair.0` selects its first element. The reserved `.init(...)` suffix instead forms a [construction expression](#433-constructors) with a Type qualifier. Dynamic member lookup with an arbitrary expression is not defined.

Ordinary Property reads use the getter; writes require the setter; `property@move` follows [Property Consume](#89-property-consume). Check the selected operation's permissions and receiver conditions. Type/Semantics `@` on a Property adapts its getter result, not backing storage. Tuple and fixed-array places follow [Move Paths](#913-move-paths-and-partial-move). Raw pointers do not dereference automatically: write `(*pointer).name` in an Unsafe Block; this does not grant safe struct-Property Consume.

```kimi
let count = collection.count
collection.count = 10   // Requires a setter and write permission.
let first = pair.0
```

#### 6.4.2. Invocation and generic application

`callee(arg1, arg2)` invokes a function, method, or function value. Zero arguments and a trailing comma are allowed. `callee<T, U>(args)` applies explicit type arguments before calling.

Argument mapping, Type adaptation, expected-result filtering, candidate comparison, and final usage checks follow [overload resolution](#52-overload-resolution-and-inference). Named arguments use `name: expression`; positional arguments must precede them. Function values retain positional-only calling, and unsafe calls retain their [additional restrictions](#463-unsafe-functions). Explicit function type arguments must provide the entire required list.

In an expression, `<` introducing type arguments must be adjacent to the target name and have a matching `>`. Thus `f<T>(x)` applies type arguments while `a < b` compares values. Nested type arguments may split `>>` into two closing delimiters. Use spaces around comparison operators to avoid ambiguity.

Type-argument inference and specialization follow the declared inference boundaries. Constant type-argument conventions need additional rules. Structure construction uses the dedicated [Type.init invocation](#433-constructors), with ordinary call argument evaluation and its own Type-only qualifier lookup. `T(args)` is not a constructor shorthand.

#### 6.4.3. Function expressions

##### 6.4.3.1. Syntax and inference

An anonymous function requires `func`, an optional Capture List, parameters, an optional result annotation, and either `=> expression` or a nonempty indented Block. No bare `(x) => x`, external parameter labels, defaults, optional parameters, or generic lambdas are introduced. An expression body returns its value; a Block requires explicit non-Unit `return` and treats reachable fall-through as Unit.

```kimi
let twice = func (value: i32) => value * 2
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2 // Error: no fixed input signature.
```

An omitted parameter Type requires a fixed expected callable signature. Do not search parameter Types from body operations or later uses. Infer the result from that expectation or, after parameters are fixed, the body under normal result validation. Whole-result inference also retains [result Origins and Loans](#982-closure-dependencies-and-call-results); annotations use existing elision.

Creation evaluates captures, not the function body. Invocation evaluates that body under an independent Function Boundary: no outer return/exit/continue/yield targets or inherited Unsafe permission. Capture acquisition itself occurs in the creation context. Named nested functions retain their no-capture restriction.

##### 6.4.3.2. Capture acquisition and environment

| List or entry | Creation effect |
| --- | --- |
| No list | Infer needed outer runtime bindings; Copy only when each complete Type is Copy |
| `[]` | Prohibit runtime captures |
| `[x, y]` | Acquire exactly the listed bindings; unlisted outer runtime bindings are unavailable |
| `x` | Ordinary Copy if Copy, otherwise Move |
| `x@move` | Explicit Move, even for Copy |
| `x@ref` / `x@uniq` | The existing value Borrow/Copy/Reborrow operation for that Semantics |
| `var x` / `var x@move` | Ordinary acquisition / forced Move into a mutable environment binding |

Resolve capture sources by Binding Identity, not spelling. No omitted-list Move or new external Borrow/Reborrow is inferred. A Non-Copy root is rejected even when only its Copy Property result is used; no partial capture is inferred. An existing `ref/T` may be implicitly copied with its dependencies. Unknown generic Copy retains a Copy-required obligation until finalization; it neither selects Move nor adds an implicit public `T is Copy` constraint. Use explicit `[x]` to admit both acquisition effects.

Type names and accessible static function declarations are not runtime captures. `self`, `storage`, and `value` are never implicitly captured; explicit captures obey all receiver, accessor, construction, and destruction restrictions. No runtime receiver is implicitly bound into a function reference.

Explicit captures execute left to right, including unused entries. Earlier Moves and Loans affect later legality. Reject duplicate capture names and collisions with parameters. Inferred captures execute once each in order of first occurrence in selected source, including dependencies needed by nested Closures. Excluded compile-time source contributes no capture; legitimate deferred selection must resolve the set, order, and effects before environment and ownership finalization. Runtime reachability and optimization do not alter that set.

```kimi
let number: i32 = 10
let copied = func () => number             // Copy capture.
let text = makeText()                      // Assume string.
let invalid = func () => text              // Error: explicit list required.
let holder = func [text@move] () => ()      // Move executes even if unused.
```

Capture targets are binding names only. No aliases, initializer expressions, fields, inter-entry references, `@copy`, or extra object-borrow capture syntax is defined. `var` combines only with ordinary acquisition or `@move`, not `@ref`/`@uniq`. Existing reference capture follows ordinary adaptation:

| Source | `[x]` | `[x@ref]` | `[x@uniq]` |
| --- | --- | --- | --- |
| `ref/T` | Copy reference | Copy reference, without adding a reference layer | Error |
| `uniq/T` | Move reference | Shared Reborrow | Exclusive Reborrow |

Captures without `var` create `let`-like environment bindings, regardless of source mutability or the Closure's containing binding. Value capture takes a snapshot; no automatic shared heap box is created. A captured exclusive reference can mutate its referent with adequate call access, but assignment to its capture name is not rewritten as referent assignment. `var` changes binding mutability, not deep-copy, Copy classification, or Origin dependencies.

```kimi
let count: i32 = 0
var next = func [var count] () -> i32
    count += 1
    return count
let first = next()  // 1
let second = next() // 2; outer count is still 0.
```

Environment bindings are not user Stored Properties. Ownership-bearing calls apply ordinary local acquisition, Move Paths, and `@move`; a consumed `let` cannot be reinitialized. Shared/Exclusive calls cannot move out owned captures. No environment may borrow its own owned capture through another capture; external borrowed dependencies remain legal under lifetime rules.

Nested Closures acquire through every enclosing environment. An inner-only free binding still requires the outer Closure to capture it; outer `[]` or an insufficient explicit list is an error. Every omitted-list boundary independently requires Copy. Outer parameters and body locals need capture only when the inner Closure is created. Moving an outer environment value into an inner Closure makes the outer call Consuming; inner `@uniq` cannot exceed the outer binding's access.

```text
lexical x -> outer capture x -> inner capture x
            creation of outer  execution of outer, creating inner
```

##### 6.4.3.3. Call receiver and acquisition

Each concrete Closure has one minimum **Call Receiver Requirement**, inferred from all resolved operations in selected source:

| Minimum requirement | Internal receiver | Body access |
| --- | --- | --- |
| Shared | `ref/Self` | Shared access to the environment |
| Exclusive | `uniq/Self` | Exclusive mutation of environment or captured referents |
| Consuming | Owned `Self` | Move values out of the call's environment |

These are one body's access requirements, not three independently selected implementations. A Move on any possible body path requires Consuming call. Resolve legitimate generic effects before finalizing the requirement. Do not rerun overload resolution for each receiver, relax `let` or Property permissions, or rescue an otherwise invalid body with ownership. Internal `call` and receiver notation introduce no source member or hidden `self` name.

Direct calls acquire the minimum receiver under normal evaluation, access, initialization, and Loan rules. Exclusive calls require a writable owner, an existing exclusive borrow, or a writable temporary. A captured `uniq` alone does not grant exclusive access to a `let`-owned Closure. Consuming calls ordinarily Copy a Copy Closure or Move a Non-Copy one; no special forced Move is inserted. Borrowed access cannot Move an unowned environment, although a normally permitted Copy may provide a separate owned call value.

```kimi
let text = makeText()
let reader = func [text@move] () => inspectText(text@ref)
reader()
reader() // Shared call; Move capture does not imply consuming call.

let number: i32 = 7
let take = func [number] () => number@move
let a = take()              // Consume a Copy; take remains initialized.
let b = take()
let c = (take@move)()       // Explicitly consume the original.
take()                     // Error: Moved.
```

The internal call signature retains complete receiver/parameter/result Types, per-call Origins, fixed captured Origins, and result Loan dependencies. Acquire receiver access before later argument evaluation and keep required Loans through the result's uses. Generic calls follow the declared [Callable receiver](#444-callable-constraints), even when specialization reveals a weaker body requirement.

##### 6.4.3.4. Function references and common-type conversion

A resolved function reference produces its Function Item Type, including its generic Type/Semantics specialization and Origin contract. Different declarations remain distinct. Function Item ownership is Copy/Owned/Shared and does not erase borrowed parameter/result contracts. A runtime method receiver is not automatically bound; follow explicit receiver argument rules. Unsafe functions and `deinit` cannot be acquired as values.

```kimi
func add(x: i32, y: i32) -> i32 => x + y
let item = add                         // Concrete Function Item; Copy.
let erased: (i32, i32) -> i32 = add    // Common owned value; Non-Copy.
```

At an initialization, argument, or return position with a fixed expected common Function Type, implicitly convert a Function Item or concrete Closure exactly when its signature fits, its minimum receiver is Shared, its complete environment is Owned, its result does not borrow the hidden environment receiver, all public Origin/Loan contracts hold, and ordinary source acquisition is legal. Non-static capture environments and Exclusive/Consuming-only bodies are rejected; no dependency may be erased to force conformance.

Acquire the existing environment by normal Copy/Move; conversion never rereads outer bindings or repeats captures. The resulting owned common value is always Non-Copy. Acquiring the same common Type again is ordinary Move, not a new erasure. Shared invocation does not consume it; borrowing it as `uniq/F` still exposes only Shared call.

```kimi
func makeAdder(offset: i32) -> (i32) -> i32
    return func [offset] (value) => value + offset

let callback = makeAdder(10)
let next = callback        // Move the common value.
callback(5)               // Error: Moved.
let result = next(5)      // 15.
```

Erasure is an owning-container conversion distinct from source Copy/Move; it does not change the definition of Copy. No allocation count, size, physical layout, ABI, or inlining is guaranteed. Inline, stack, or heap placement and allocation elimination must preserve acquisition, validity, and cleanup. Required allocation failure causes ordinary Panic Termination, with no Move rollback or normal cleanup guarantee. Concrete `Callable` use creates no such erased container, but does not promise zero runtime cost.

#### 6.4.4. Indexing and slicing

This section describes indexing values with a length. [Raw pointer indexing](#383-pointer-arithmetic-and-indexing) instead uses signed offsets, has no implicit bounds check, and forbids from-end and Range indexing.

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

Ranges are non-associative; an unparenthesized chained Range such as `a..b..c` is invalid. Parentheses do not make a Range a valid numeric boundary of another Range. See the [precedence table](#65-precedence-and-associativity).

Applying a Range with `value[range]` produces a Slice over the selected consecutive elements. A Slice does not copy its elements. Its Origin derives from the indexed value, so it cannot outlive that value.

After resolving from-end boundaries, an exclusive Range must satisfy `0 <= start <= end <= length`. An inclusive Range must satisfy `0 <= start <= end < length`. An exclusive Range with equal boundaries is empty.

Invalid Indices or boundaries, including negative `n` in `^n`, are check failures under [Panic Termination](#113-panic-termination). Safe sequence access may omit a check only when safety is proven.

```kimi
let values = [10, 20, 30, 40]
let last = values[^1]       // 40
let middle = values[1..^1]  // Slice referring to 20 and 30.
let all = values[..]
let empty = values[2..2]
```

Dictionary indexing is a separate operation: reading `dictionary[key]` requires an existing key and initiates implicit Panic if it is absent. Fallible lookup, insertion, and user-defined indexer declarations require separate library rules. Integer indexing into a `string` does not yet select a character; the specification must first choose byte, Unicode scalar, or grapheme indexing.

Target and index evaluation follows [evaluation order](#62-evaluation-order).

#### 6.4.5. Object member calls

Select the member, overload, access, and public contract statically from the receiver's [Effective Type](#791-stable-bindings-and-effective-types). Dynamic dispatch then selects only the corresponding implementation for the Runtime Object Type. It never repeats name lookup or chooses a derived-only overload. Declaration identity distinguishes inherited virtual slots from each runtime contract's requirements.

```text
animal.speak()
    ├─ static: select Animal.speak and check its public contract
    └─ dynamic: Dog -> Dog override; Cat -> Cat override
```

Shared object access admits shared members/getters. Exclusive access may share by Reborrow or invoke an exclusive member/setter under normal Loan rules. A getter-only contract grants no setter. Returned interior references retain their receiver Loan; an exclusive result cannot detach from the current call to enable overlapping reentry.

##### 6.4.5.1. Object receiver compatibility

Object borrows may mutate permitted state of the same complete object, but cannot Replace the entire object or base subobject, even with another instance of the same Type, or MoveOut a part leaving it incomplete. Ordinary field replacement and initialization-preserving exchange remain state mutation under Property/access/Loan rules. Owning-handle replacement is a different operation:

```kimi
func celebrate(animal: objuniq/Animal) -> ()
    animal.age = animal.age + 1

var animal: obj/Animal = makeDog()@obj/Animal
animal = makeCat()@obj/Animal // Destroy old Dog, then own a separate Cat.
```

The latter requires the old object's borrowing and destruction conditions; it does not change one object's Dynamic Type. `obj/T` permits exclusive mutation only through valid exclusive access to a writable Place or temporary. `rc/T` and `arc/T` supply shared access; a reference count of one does not create exclusive permission. Ownership responsibility is not unrestricted write authority.

**`ObjectCompatible(f, receiverKind)`** holds when static verification, including callees and receiver-derived borrows, establishes all of:

- operations fit the declared Shared or Exclusive access;
- no whole-object/base Replace or MoveOut leaving the receiver incomplete occurs;
- no unrestricted ordinary `uniq/Self` escapes or alternative path enables prohibited operations;
- parameter/result Types, accessor permissions, Origins, Loans, and returned-reference dependencies satisfy their contracts.

This is a specification predicate, not source syntax or a prescribed analysis algorithm. Unknown separate or indirect callees cannot be assumed safe. Apply the [declaration/use validation boundaries](#434-virtual-members-and-overrides).

Ordinary getter/setter declaration receivers remain `ref/Self` / `uniq/Self`. A standard or custom body may share a verified object entry without adding general `objuniq/T -> uniq/T` conversion, reselecting overloads, changing result contracts, or duplicating conformance. A custom accessor gains neither standard field-scoped access nor Property Consume eligibility. `MoveOut` is an effect; existing `@move` Consume is an acquisition operation with its own eligibility rules.

```kimi
// Property declaration excerpt; clamp does not access the receiver.
public var age: i32 = 0
    get
    set
        storage = clamp(value, 0, 150)
```

The setter changes one Property's storage, not all of `self`. Bodyless `get` is the standard getter; set-only explicit accessors add no getter, and `get => storage` would be custom.

### 6.5. Precedence and associativity

Earlier rows bind more tightly. Left associativity groups `a op b op c` as `(a op b) op c`; right associativity groups it as `a op (b op c)`. Grouping does not guarantee type correctness or change evaluation order.

| Level | Operators or syntax | Association |
| --- | --- | --- |
| 1 | `.name`, `(...)`, `<Types>`, `[...]`, postfix `++` `--` | Postfix chain, left to right |
| 2 | Prefix `+` `-` `not` `*` `^` `++` `--` | Right |
| 3 | `@Type`, `@Semantics`, `@move` | Left |
| 4 | `*` `/` `%` | Left |
| 5 | `+` `-` | Left |
| 6 | `<<` `>>` | Left |
| 7 | `&` | Left |
| 8 | `^` | Left |
| 9 | `\|` | Left |
| 10 | `<` `<=` `>` `>=` `==` `!=`, runtime `is` / `is not` | Non-associative |
| 11 | `and` | Left |
| 12 | `or` | Left |
| 13 | `..` `..=` | Non-associative |
| 14 | `=`, compound assignments | Right |

In ordinary expressions, `is` / `is not` ends after one named Core Type; outer `and` / `or` remain Boolean operators. In dedicated compile-time contexts, `is` instead follows the asymmetric [requirement-expression rule](#442-requirement-expressions). Prefix `not` precedence is unchanged: negate a runtime test with `value is not Dog` or `not (value is Dog)`. `as` is reserved.

Unparenthesized comparison chains such as `a < b < c`, `a == b == c`, and `a < b == flag` are syntax errors. Write `a < b and b < c` or `(a < b) == flag`; each comparison still requires valid operand Types.

`@` is one token. All explicit `@` operations share this precedence and left associativity, below prefix operators: `-x@i64` means `(-x)@i64`, and `(a + b)@move` moves the sum while `a + b@move` moves only b. Use `-(x@move)` to negate after Move. An Adaptation Target may contain qualified names, `/`, and generic arguments; spaces do not necessarily separate Type syntax from operators. Parenthesize the result before member access, calls, or indexing: `(x@T).name`, `(f@move)()`, `(a@move)[0]`. Use `(x@Number) / divisor` for division after adaptation.

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

### 6.6. Operator semantics

#### 6.6.1. Unary operators

| Operator | Operand and result |
| --- | --- |
| `+value` | Numeric value, unchanged Type and value. |
| `-value` | Negated signed integer or floating-point value. |
| `not value` | Negated `bool`. |
| `*pointer` | Raw-pointer place under [unsafe dereference rules](#382-dereference-and-ownership). |
| `^value` | From-end Index formed from a nonnegative `isize`. |
| `++target` / `--target` | Increment or decrement an integer; return the updated value. |
| `target++` / `target--` | Increment or decrement an integer; return the old value. |

Increment and decrement require a readable, writable integer place or Property; they do not apply to floats, raw pointers, or arbitrary Types. Resolve, read, and write the target once each. Overflow prevents the write. A prefix operation returns its computed value without reading the Property again. These operations follow the target-validity and ownership requirements of [compound assignment](#672-compound-assignment).

```kimi
var count: i32 = 1
let before = count++  // before = 1, count = 2
let after = ++count   // after = 3, count = 3
```

`not` binds more tightly than comparison; negate a comparison as `not (a == b)`. Explicit dereference of non-pointer Types is not defined by this operator.

#### 6.6.2. Arithmetic, bitwise, and shift operators

`+ - * /` take operands of the same numeric Type and return that Type. `%` accepts integers only. Integer division truncates toward zero. On mathematical integers, the remainder satisfies `a = (a / b) * b + a % b`; a nonzero remainder has the dividend's sign.

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010
let shifted = bits << 1     // 0b10100
```

Check integer `+ - *`, unary `-`, increment/decrement, and the arithmetic part of compound assignment for overflow. Integer division or remainder by zero is invalid. Signed minimum divided by `-1`, including `% -1`, is also invalid. These failures follow [Panic Termination](#113-panic-termination), including its constant-evaluation rule.

`& | ^` perform bitwise AND, OR, and XOR on the same integer Type; they do not accept `bool`. `<< >>` return the left operand's integer Type and accept any integer Type on the right, requiring `0 <= shift < bit width of left operand`. An invalid count is a check failure. Left shift discards high bits and inserts zero low bits; right shift sign-extends signed integers and zero-extends unsigned integers. Discarded shift bits are not arithmetic overflow.

Floating-point operations follow IEEE 754 for `f32` / `f64`, using round-to-nearest, ties-to-even. They support infinity, NaN, and signed zero; floating-point division by zero does not use integer failure rules. Do not implicitly reassociate or fuse ordinary operations when rounding or NaN results would change.

`string + string` concatenates without implicit numeric stringification. Raw-pointer arithmetic is limited to the forms and unsafe conditions in [pointer arithmetic](#383-pointer-arithmetic-and-indexing); its undefined-behavior rules are distinct from checked integer arithmetic.

#### 6.6.3. Comparison and logical operators

`== != < <= > >=` return `bool`. Numeric operands must have the same Type. `bool` and Unit support equality only. `char` compares Unicode scalar values. `string` uses UTF-8 byte equality and lexicographic order without normalization or locale processing.

Floating-point `+0.0 == -0.0` is true. With a NaN operand, `== < <= > >=` are false and `!=` is true; floating-point ordering is not total.

Comparisons may borrow their operands and do not Move non-Copy owned values solely to compare them. User-defined comparison requires an explicit Type capability. Safe borrows compare referent values of the same Type using that Type's comparison capability. Tuples support elementwise equality and lexicographic ordering when all corresponding elements support the required comparison.

Value equality and object identity are separate operations; `==` does not implicitly become an address comparison for object Types. Raw-pointer `== !=` are the explicit exception, following [pointer equality](#381-null-and-equality).

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

#### 6.6.4. Explicit operations

Explicit operation selection is distinct from subtyping and acquisition legality under [Type relations and expression operations](#39-type-relations-and-expression-operations). Origin restriction fits the selected operation's result; it does not substitute another operation.

`@` is a built-in explicit value operation. It cannot be overloaded, does not search conversion chains, and applies only to its direct operand. The operation itself calls no user code; ordinary operand evaluation, including calls and getters, still does. It never implicitly boxes, acquires resources, duplicates ownership, or increments reference counts.

```text
Explicit @ Operation
├─ Type / Semantics Adaptation: @Type, @ref, @uniq, ...
└─ Value Lifetime Operation: @move
```

##### 6.6.4.1. Forms and adaptation targets

| Form | Meaning |
| --- | --- |
| `E@Type` | A defined adaptation to the specified target |
| `E@Semantics` | Same form with Core Type, immediate Referent Type, or object View Target taken from the operand as applicable |
| `E@move` | Explicit Consume; no Adaptation Target |

An **Adaptation Target** specifies Semantics and a Core Type, a complete inner Type for a value-borrow or pointer layer, or an object View Target. Infer result Origins from the operand, operation, Loans, and applicable constraints to obtain the complete result Type. Retain Origin information in aliases, generic Types, and operands; do not erase constraints or extend validity. Runtime targets do not contain `from Origin`; `exit ... from Label` belongs to control-transfer syntax.

```text
Adaptation Target
├─ Core Type / immediate Referent Type / object View Target: specified, or taken from the operand
├─ Semantics: determined by Type, alias, or explicit Semantics
└─ Origin: inferred during adaptation
    -> complete result Type retains target, Semantics, and Origin
```

For a single-layer value Type with Core Type `T`, `@ref` and `@ref/T` select the same existing Borrow/Reborrow operation when applicable. For a nested borrow, shorthand retains its immediate Referent Type: applying `@ref` to `ref/ref/T` copies that outer shared reference. It does not add a layer or turn `@ref` into `@objref`. A fully specified target can instead request a borrow of reference-value storage under [Borrow and Reborrow](#6645-explicit-borrow-and-reborrow). Type names, aliases, generic applications, grouping, and Tuple syntax are accepted as target syntax without implying that every adaptation is defined.

```kimi
let wide = number@i64
let view = value@ref
let sameView = value@ref/Value // When value's Core Type is Value.
let taken = value@move
```

For a bare Name `X` in `E@X`, first recognize `move` and built-in Semantics names. Otherwise perform **Adaptation Target Lookup** independently for the Type and generic Semantics-parameter roles, using ordinary lookup stages, visibility, and aliases. Type candidates include Type aliases and generic Core Type parameters. Commit each role's first eligible stage and deduplicate paths to the same Symbol.

| Lookup result | Outcome |
| --- | --- |
| Type only | `@Type` |
| Semantics parameter only | `@Semantics` shorthand |
| Both roles, or ambiguity within a role | Ambiguity error |
| Neither | Ordinary wrong-role, inaccessible, or undefined-Name diagnostic |

Operand Types, expected Types, or conversion success cannot resolve a role ambiguity or reopen outer lookup. Qualified names and constructed Types use normal Type syntax; `@s/T` gives `s` the Semantics role and `T` the Core Type role, extended to the View Target role when `s` is object Semantics. Qualification or an explicit Semantics/Core Type form may disambiguate a bare name.

##### 6.6.4.2. Static selection and inference

Resolve the operation from the explicit designation and operand Type/category, then check access, ownership, Loans, and Origins. Numeric conversion, Identity Acquisition, and pointer casts use ordinary value acquisition. Borrow targets use the Borrow table; `@move` uses Consume resolution. Failure never selects a different operation, getter, or overload.

Targets may guide permitted literal or generic inference but cannot change an established operand or function result Type. An outer expected Type cannot cancel the explicit operation. Since `@move` preserves Type, its expected Type may guide operand inference, function-reference selection, and anonymous-function checking within normal inference boundaries. It must not replace Consume with Copy/Borrow, read a getter to make Consume fit, or introduce cyclic inference or candidate-by-candidate retries. Check subsequent result fitting statically.

```kimi
// handler is an overloaded function name; the annotation selects its reference.
let f: (i32) -> () = handler@move
// Move the temporary function value; no function declaration becomes Moved.
let g = f@move // f is a Place and becomes Moved.
```

Deferred generic effects follow [Generic Access Effects](#527-generic-access-effects). Resolve effects before finalizing ownership/Loan analysis. `Never` follows ordinary abrupt-completion and Type-fitting rules, not a value conversion. A non-completing operand prevents execution of the outer operation but does not waive syntax, target-Type, or Unsafe checks.

##### 6.6.4.3. Defined adaptations

| Operation | Condition |
| --- | --- |
| Identity Acquisition | Same normalized complete Type; ordinary acquisition is permitted |
| Numeric Conversion | Owned integer/float values in the numeric table below |
| Borrow / Reborrow | The explicit Borrow table below |
| Object Upcast | The finite [object upcast tables](#6647-object-upcasts), including their specified borrow forms |
| Raw Pointer Conversion | The [pointer conversion rules](#384-pointer-conversions) |
| Typed Null Formation | Contextually type `null` as a raw pointer; no Unsafe context required |

**Identity Acquisition** copies a Copy Type and otherwise Moves. Borrow targets take precedence, including same-Type exclusive Reborrow. Same-Type raw pointer acquisition is ordinary Copy and requires no Unsafe context for the operation itself. `@owner`, `@obj`, `@rc`, and `@arc` allow same-Semantics acquisition; they do not create ownership or convert between ownership representations.

```kimi
number@owner   // Copy if number is Copy.
resource@owner // Ordinary Move if resource is a non-Copy owned value.
number@move    // Explicit Move even if number is Copy.
```

**Origin Restriction** is common static result fitting, not another value operation. Determine acquisition/Borrow and its effect, then apply only shortening permitted by existing variance and outlives rules. Check Identity Acquisition before this use-site restriction. Preserve Core Type, Semantics, dependencies, and Loans; do not add Copy, Move, or Borrow, extend lifetime, or rewrite arbitrary nested Origins. For example, fitting `ref/T from longer` to `ref/T from shorter` requires `longer` to outlive `shorter`. Exclusive same-Type adaptation still uses Reborrow.

A target changing both Core Type and Semantics must be one defined operation. No hidden convert-then-borrow sequence is inserted:

```kimi
// number is i32.
// number@ref/i64 // Error: numeric conversion and Borrow are separate operations.
inspect(number@i64@ref)
```

There is no elementwise Tuple/array conversion, structural struct conversion, checked dynamic cast through `@`, string parsing, numeric conversion involving `bool`/`char`, arbitrary bit reinterpretation, or user-defined conversion. Same-Type acquisition of these Types remains possible. Do not implicitly dereference safe references to convert or extract their owned referents. Raw-pointer/safe-reference conversion and ownership acquisition require separately specified operations. `as` remains reserved, not an alias of `@`.

##### 6.6.4.4. Numeric conversions and literals

| Source -> target | Rule |
| --- | --- |
| Integer -> integer | Check the target range; no truncation or wrapping |
| Integer -> float | Round to nearest, ties to even |
| Float -> float | Same rounding; preserve NaN and infinity |
| Float -> integer | Truncate toward zero, then check the mathematical integer's range |

A finite value rounding to infinity fails. Rounding to a subnormal or zero is allowed. NaN payload preservation is not guaranteed. NaN and infinity cannot convert to integers.

Float-to-float conversion preserves signed zero, including the sign of a nonzero value rounded to zero. Integer zero converts to positive floating zero; either floating zero converts to integer zero. Floating rounding always uses roundTiesToEven independently of ambient rounding modes. Flush-to-zero or similar settings must not change the specified result. These rules also apply to literal conversion.

Direct untyped integer literals with integer targets are fitted to that Type, including a directly applied negative sign. Direct floating literals with `f32`/`f64` targets are interpreted in that Type. A direct untyped integer literal with `@f32`/`@f64` is rounded once from its exact integer value, without an intermediate default `i32` or `f64`. Parentheses alone and a direct sign preserve this treatment; the language's literal representation limits still apply.

Floating literals with integer targets first get their ordinary floating source Type, then undergo truncation and range checking. Typed values and general arithmetic expressions use ordinary numeric conversion. Explicit literal adaptation does not widen implicit argument fitting or overload candidate comparison.

```kimi
let minimum = -128@i8
let large = 5000000000@f64 // No intermediate i32 range check.
let single = 1@f32
let negativeZero = -0.0@f32
let truncated = 3.9@i32  // 3
// 256@u8 // Error: direct literal does not fit.
```

Apply rounding and checks at every `@` in a chain. Do not remove an intermediate result if its rounding or failure would change.

##### 6.6.4.5. Explicit borrow and reborrow

In value Borrow/Reborrow rows, `T` is the same normalized immediate Referent Type, which may itself have Semantics. In object rows it is the same View Target; changing that target uses the upcast tables below. Every row requires valid initialization, access, Loans, and Origins. These are explicit adaptations; do not add rows to implicit [argument fitting](#522-argument-adaptation-and-literals) solely because they appear here.

| Input | Operation | Result |
| --- | --- | --- |
| Readable owned `T` place | `@ref` | New `ref/T` |
| Exclusively writable owned `T` place | `@uniq` | New `uniq/T` |
| `ref/T` | `@ref` | Copy the shared reference and preserve its Origins |
| `uniq/T` | `@ref` | Shared Reborrow |
| `uniq/T` | `@uniq` | Exclusive Reborrow |
| Readable `obj/T`, `rc/T`, or `arc/T` place | `@objref` | New `objref/T`; no reference-count increment |
| Exclusively writable `obj/T` place | `@objuniq` | New `objuniq/T` |
| `objref/T` | `@objref` | Copy the shared object reference |
| `objuniq/T` | `@objref` | Shared object Reborrow |
| `objuniq/T` | `@objuniq` | Exclusive object Reborrow |

A fully specified target `ref/V` or `uniq/V` can borrow the storage of an operand whose complete normalized Type is `V`, including when `V` is itself a reference or object handle. This requires a readable or exclusively writable operand Place, respectively; a Temporary Value is materialized under ordinary lifetime rules. It adds exactly one reference layer and retains every dependency of `V`. Same-Type Copy/Reborrow and the existing `uniq/V -> ref/V` Reborrow retain their meanings and take precedence; a failed Reborrow never becomes a storage borrow. Shorthand `@ref` / `@uniq` does not request an additional layer for an already borrowed value.

```kimi
var number = 1
var reference = number@ref
let same = reference@ref            // ref/i32; preserves number's Origin.
let slot = reference@ref/ref/i32     // ref/ref/i32; also depends on reference's storage.
// reference = other@ref            // Error while slot's Loan is live.
```

For `reference: ref/i32`, `reference@uniq/ref/i32` borrows its writable slot exclusively; it does not grant mutable access to `number`. A `let` reference slot cannot be borrowed this way exclusively. `reference@ref@ref` remains `ref/i32`. Explicit Origins remain forbidden anywhere in an Adaptation Target, including grouped and generic inner Types; their dependencies are inferred or retained from existing Types.

A new Borrow depends on the target Place and owner validity. Copying a shared reference preserves its referent Origins rather than using the lifetime of the variable holding it. Reborrow lends referent capability without moving the parent reference; while the child Loan is live, conflicting access through the parent is forbidden. A `let` binding holding an exclusive reference does not by itself prevent Reborrow. Use `@move` to transfer the reference itself.

```kimi
var value = makeValue()
let exclusive = value@uniq
inspect(exclusive@ref)
modify(exclusive@uniq) // After the previous child Loan ends.
let transferred = exclusive@move
```

Do not upgrade shared to exclusive, derive exclusive object borrows from `rc`/`arc`, or convert between value-borrow and object-borrow representations. A runtime reference count of one does not grant an exception. Owned temporaries use [materialization and temporary borrowing](#36-temporary-values-places-and-lifetimes).

A Property operand of Type/Semantics adaptation is read through its getter once. Adapt the **Getter Result Type**, also when inferring the shorthand Core Type. A Copy getter result is a Temporary Value; borrowing it does not borrow the original Property storage. A borrowed getter result follows Copy/Reborrow rules. Neither an accessible setter nor `@ref`/`@uniq` exposes backing storage; accessor-internal field-scoped operations retain their own rules.

```kimi
let view = holder.item@ref // If get returns ref/Resource, Copy that reference.
// holder.item@uniq       // Error if get returns ref/Resource.
inspect(person.age@ref)    // Borrow the getter's Copy result temporary.
```

##### 6.6.4.6. Evaluation, results, and failure

Evaluate each operand and required receiver once; finish chained operations from the inside outward. `@move` follows [Consume](#915-explicit-consume); only its final operand result is explicitly moved. `@move` is an expression usable in initialization, assignment, arguments, and results; its result retains the operand Type.

```kimi
// Independent examples; x is i32.
x@i64@move // Move the converted temporary; x remains Initialized.
x@move@i64 // Move x, then convert; x is Moved.
x@ref@move // Move the borrow value, not x.
x@move@ref // Move x, then borrow the resulting temporary.
```

Surrounding evaluation order is unchanged. [Assignment](#671-simple-assignment) remains RHS-first and returns Unit. A custom destination setter receives the secured result normally; source Consume and destination Write permissions are separate. Destruction of the old destination must not invalidate result Loans/Origins. Loans begin when Borrow/Reborrow occurs, including while later arguments are evaluated; do not delay an exclusive receiver Loan until after argument evaluation.

```kimi
var x = makeResource()
x = x@move // Legal Move and reinitialization; skip destruction of the Moved old value.
// f(x@move, x) // Error: later argument uses Moved x.
```

Discard does not omit evaluation, conversion checks, or Consume. Destroy an unused owned result at its normal lifetime. `return` and other transfers secure the result before common cleanup; returned borrows must remain valid afterward, and deferred uses of Moved/incomplete values are errors.

| Failure | Handling |
| --- | --- |
| Undefined adaptation, Type/access/initialization/Loan/Origin violation | Compile-time error |
| Literal fitting failure | Compile-time error |
| Failed conversion in required constant evaluation | Compile-time error |
| Failed runtime numeric conversion | Panic if evaluated |
| Unsafe memory contract violation | Ordinary Unsafe rules |

Literal fitting is a static language rule. Otherwise, constant propagation/folding cannot change specified runtime Panic into a compile-time error outside required constant evaluation. Optimization cannot introduce failure from skipped evaluation.

```kimi
let x: i32 = 300
x@u8 // Panic when evaluated, even if propagation knows x is 300.
```

Panic and cleanup during or after `@` evaluation follow Error Handling and Value Lifetime. There is no operation-specific rollback of completed Moves or side effects, and no lifetime extension.

##### 6.6.4.7. Object upcasts

Let `S` be the source's static Core Type View Target and `V` a different target. An upcast requires static proof of `Supports(S, V)`. Inheritance and conformance persist in derived Types, so this guarantee holds even when the actual object is more derived than `S`. Validate [payload erasure](#981-object-payload-erasure), initialization, access, Origins, and Loans in every row.

| Source | Explicit operation | Acquisition/result |
| --- | --- | --- |
| `obj/S`, `rc/S`, `arc/S` | `@obj/V`, `@rc/V`, `@arc/V`, respectively | Move the same owning handle; no count change |
| `objref/S` | `@objref/V` | Copy the shared reference and change view |
| `objuniq/S` | `@objuniq/V` | Exclusive child Reborrow and change view |
| Shared-borrowable `obj/S`, `rc/S`, `arc/S` Place | `@objref/V` | Shared object Borrow; owner remains unchanged |
| Exclusively writable `obj/S` Place | `@objuniq/V` | Exclusive object Borrow |
| `objuniq/S` | `@objref/V` | Shared child Reborrow and change view |

Each row is one defined operation, evaluated once, not a search for arbitrary convert-then-borrow chains. Preserve the entire object, identity, Dynamic Type, and cleanup responsibility. There is no implicit upcast from annotations, arguments, or returns. Same-target Borrow/Reborrow follows the existing table and does not newly erase a Type. Temporaries retain their original materialization lifetime.

```kimi
let dog: obj/Dog = makeDog()
let animal = dog@obj/Animal // dog is Moved; animal still owns the entire Dog.
let view = animal@objref
let invalid = view@objref/Dog // Error: Animal does not statically imply Dog.
```

Use a checked cast when the source view cannot guarantee the target, including contract-to-concrete and cross-contract conversion. No ordinary value-borrow upcast, ownership-from-borrow, shared-to-exclusive upgrade, `obj -> rc/arc`, or container covariance is added. `@ref/V` is not shorthand for `@objref/V`; `rc/arc` do not supply exclusive borrows.

#### 6.6.5. Runtime type tests and checked casts

##### 6.6.5.1. Runtime is tests

In ordinary expressions, `value is T` and `value is not T` are non-associative comparisons. The right side is one named struct Core Type, with optional qualification and resolved type arguments, but no Semantics, Origin, binding name, or requirement composition. Expand aliases and check accessibility. Unresolved type parameters/associated Types and non-struct targets are outside this initial syntax.

The left side must have `obj/S`, `rc/S`, `arc/S`, `objref/S`, or `objuniq/S`, with a struct Core Type `S`. Evaluate it once, then return `Supports(RuntimeObjectType(value), T)` or its negation. Generic identity includes the relevant arguments. This operation itself neither Copies nor Moves nor Consumes the operand, changes counts, nor acquires stronger authority. Getter/call evaluation, required shared access, temporaries, and cleanup retain normal effects.

```kimi
require value is Dog and value.isHealthy() else return
value.bark()
// value is Dog or Cat parses as (value is Dog) or Cat, not a two-Type test.
```

The syntax context determines `is` before lookup: Contract/Associated Clauses and compile-time directive conditions retain their Requirement Test; ordinary initializers, arguments, and runtime conditions use this test. `T is Comparable` in an ordinary expression is not retried in the Type namespace. Parentheses preserve the surrounding context; use `#if` / `#match` for compile-time selection.

Accept well-typed tests even when static information proves them always true or false; a warning is allowed. Do not omit left-side effects or derive additional unreachable paths from that knowledge. Conditional Type information follows [flow refinement](#79-type-refinement-and-require). Ordinary owners, value borrows, pointers, numeric values, Tuples, and enum variants are not test subjects. This syntax does not add optional or pattern binding.

##### 6.6.5.2. General view tests and checked casts

The object model additionally defines view-support tests for any valid View Target and a distinct exact-type test. A support test queries `Supports(RuntimeObjectType(value), V)`; an exact test compares Runtime Type Identity with a concrete Core Type and excludes derived Types. The source syntax for contract-view tests and exact tests remains deferred; it does not extend the initial `is` syntax above. A Boolean saved in a variable carries no refinement provenance.

For a statically valid checked cast to `V`, success is exactly the same `Supports` predicate, always using the original object's Dynamic Type, including from a contract view. Failure is an ordinary absent/error result, not Panic or unsafe reinterpretation.

| Source | Conceptual result | Acquisition and failure |
| --- | --- | --- |
| `objref/A` | `Option<objref/B>` | Keep source usable and preserve referent Origin |
| `objuniq/A` | `Option<objuniq/B>` | Reborrow a child; failure carries no child Loan |
| `s/A`, where `s` is `obj`, `rc`, or `arc` | `Result<s/B, s/A>` | Move source once; success owns target view, failure owns original view |

```text
objref/Speaker holding Dog
    ├─ checked cast to Dog or Animal -> success
    ├─ checked cast to Named         -> success iff Supports(Dog, Named)
    └─ checked cast to Cat           -> failure

owned animal -> checked cast -> success(dog) or failure(original)
               animal is Moved in either branch
```

For an exclusive result whose variant is not yet known, conservatively track the possible child Loan; the parent cannot conflict until that dependency ends. An owning cast never restores the source binding on failure. It neither destroys/copies the object nor changes reference counts. Destroying the result follows the normal responsibility of the branch it holds.

Check target validity/accessibility, Semantics preservation, [Owned erasure](#981-object-payload-erasure), result Origins/Loans, and destruction dependencies statically. Borrowing cannot create ownership or exclusivity; share explicitly before casting when needed. Evaluate/secure the source once. No test or cast reconstructs erased generic lifetime bindings. API names and Option/Result branching syntax remain design boundaries; these guarantees do not define a cast spelling or add checked casts to `@`.

### 6.7. Assignment

#### 6.7.1. Simple assignment

`target = value` returns Unit and evaluates in this order:

1. Evaluate the right side fully and secure a temporary of the statically determined destination Type by Copy/Move; update the source state and responsibility.
2. Evaluate the left receiver and indices left to right, locating the destination once without invoking its getter.
3. Destroy the old value or initialized parts still present at that destination. Skip Moved/Uninitialized parts.
4. Place the temporary by ordinary Copy/Move rules, transferring its responsibility as applicable and leaving the destination Initialized.

For a custom Property setter, pass the secured result to that setter instead of directly performing steps 3–4; its internal direct assignments follow these rules. Standard setters apply them to the field and can [restore incomplete instances](#810-access-after-partial-move). Initial Property construction uses its dedicated rules.

Replacement operates on existing storage; it neither invokes a constructor for the incoming value nor reruns declaration initializers. Destroy an old complete value by its exact owning Type's full derived-to-base cleanup chain. For an old incomplete value, destroy only components with remaining responsibility under [partial cleanup](#1032-field-cleanup). A base view of a derived value is not a whole-value replacement target. Destination permission, all ancestor restrictions, and actual Loan validity are checked before permitting the operation. If old-value destruction does not complete normally, no new value is installed; do not restore the old state or continue with an empty observable destination.

The destination Type is known statically; type checking does not evaluate the left side early. RHS-first evaluation lets the destination supply its own old value. It deliberately differs from compound assignment and ordinary receiver calls:

```kimi
values[index()] = makeValue()  // makeValue, index, old destruction, placement.
values[index()] += amount()    // index, old read, amount, compute, write.
obj().prop = arg()             // arg, obj, setter.
obj().setProp(arg())           // obj, arg, method call.
```

Use explicit locals when a particular order for receiver or index effects is needed.

Destination location uses the state after RHS evaluation. It may locate a Moved writable local's storage without reading its former value, but cannot read a Moved owner/receiver to find a target. Storage must remain valid from location through placement.

**Destruction of the old destination must not invalidate an Origin or Loan required by the secured RHS result.** Check the same dependencies during LHS evaluation, its temporary cleanup, target access, later use, and Destruction. New values at an identical address do not inherit dependencies on the old value.

```text
RHS result borrows data owned by the old destination.
Destroying the old destination would destroy that data.
=> Reject the assignment: placing the result afterward cannot repair its Loan.
```

If the RHS does not complete normally, do not evaluate the LHS. If LHS evaluation fails, do not destroy or place; if old-value destruction fails, do not place. Earlier effects and Moves are never rolled back. Keep the secured result alive during LHS evaluation; an ordinary control transfer cleans remaining temporaries under their normal lifetimes, after securing any transfer result. Panic follows the common termination rules.

```kimi
var x = makeResource() // Non-Copy owned value.
x = x                 // Move to temporary, locate x, skip absent old value, Move back.
// No user-defined deinit runs; no self-assignment exception is needed.

var count: i32 = 0
let done: () = (count = 20)
```

Self-assignment of a Copy value uses Copy then Replacement. Property `x.p = x.p` uses its getter/setter and may be invalid for a borrowed getter result; `x.p = x.p@move` is separately governed by Property Consume. `let` reinitialization remains forbidden.

Right associativity parses `a = b = c` as `a = (b = c)`; the inner Unit result makes ordinary numeric chaining invalid. Assignment is not a Boolean condition. Destructuring, whole-Slice assignment, and initialization of raw uninitialized memory need separate rules.

#### 6.7.2. Compound assignment

`+= -= *= /= %= &= |= ^= <<= >>=` perform the corresponding binary operation and return Unit. Resolve the destination once, read its old value once, evaluate the right side, compute, and write once. This is not a textual replacement with `target = target op value`; receivers and indices are not repeated.

A Property uses one getter and one setter. Its getter result must support the operation, and the computed result must fit the Property Type. Do not insert hidden Moves or duplication to supply missing capabilities. Destination validity and destruction dependencies follow simple assignment, but evaluation remains target/read first, then RHS. Thus it is not equivalent to the RHS-first simple-assignment form.

```kimi
values[nextIndex()] += amount() // Index, old value, amount, addition, write.
```

If the right side or operation does not complete normally, do not write; getter and operand effects already performed remain. Compound assignment is not atomic and does not provide synchronization. Raw-pointer `+=` / `-=` use only the permitted displacement operations and their unsafe conditions; other pointer compound assignments are forbidden.

### 6.8. Extension boundaries and reserved syntax

Operator symbols, precedence, and associativity are fixed by the language. User-defined arithmetic and comparison may be supplied through explicit Constraints once their declaration syntax, required members, and resolution rules are specified. Such extensions must preserve evaluation order and counts, comparison's `bool` result, and assignment's Unit result.

`and`, `or`, `not`, `=`, `@` (including `@move`), `is`, Ranges, and control transfers cannot be reinterpreted by user code. Custom operator symbols and precedence declarations are not defined. Neither are `!`, `&&`, `||`, `~`, `**`, `??`, `?.`, or ternary `?:`; use logical keywords and `if`. Unary `&` is not a borrow operation; use `@ref` / `@uniq`. Prefix `move`, a Move accessor, and a dedicated `<-` Move operator are not defined. Recognition by the lexer alone does not make a token a usable operator.

`#Name` is an Attribute and `#if` / `#match` are compile-time directives, not runtime unary operators. `$` denotes the Composition Root; `$panic(...)` follows [Panic Termination](#113-panic-termination). Other Composition Root operations, dependency resolution, lifetimes, and failure rules remain separately specified.

## 7. Control flow

Control flow distinguishes four concepts:

| Concept | Meaning | Forms |
| --- | --- | --- |
| **Evaluation Context** | Whether an expression's result is consumed or discarded. | Value Context; Discard Context |
| **Control Boundary** | Transfer targets and lookup barriers. | Function, Labeled Block, Deferred, Iteration (`for`, `while`, `loop`), Selection (`if`, `match`) |
| **Control Transfer** | A requested change in control. | `return`, `exit`, `continue`, `yield` |
| **Completion** | How evaluation finishes. | `Normal(result)`, `Return(target, result)`, `Exit(target, result)`, `Continue(target)`, `Yield(target, result)` |

An **Iteration Construct** is a `for`, `while`, or `loop`; an **iteration** is one execution of its body. Each construct establishes an Iteration Boundary. Every `if` and `match` establishes a Selection Boundary regardless of its result or context. Boundaries may accept, stop, or pass through a transfer lookup, as specified under [target lookup](#752-target-lookup).

| Transfer | Role |
| --- | --- |
| `return` | End the current function and supply its result. |
| `exit` | End the nearest Iteration Construct or Deferred Block, or an enclosing Labeled Block or Iteration Construct named by `from Label`. |
| `continue` | Start the next iteration of the nearest Iteration Construct, or the one named by `Label`. |
| `yield` | End the nearest enclosing selection and supply its result. |

Unlabeled `exit` skips Labeled Blocks. A `loop` or explicitly named Labeled Block can receive a result in either Evaluation Context. Deferred Blocks accept only operandless self-targeted `exit`; no transfer may cross their boundary. Kimigayo uses `exit`, not `break`, for iteration termination.

### 7.1. Completions

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

**Divergence** means that evaluation never finishes and produces no Completion. Under the broader term **Evaluation Outcome**, Completion, divergence, and [Panic Termination](#113-panic-termination) are distinct cases. Panic ends the entire process without a Completion delivered to a lexical target. Never describes the absence of normal completion; it is neither a Completion variant nor a synonym for divergence.

### 7.2. Blocks and evaluation contexts

A **Block** is an indentation-delimited sequence of declarations, expressions, and statements evaluated in order. An ordinary Block completes with Unit on reaching its end, including when its last item is a declaration or conditional compilation removes all its items. Source-level executable bodies must satisfy the nonempty rule below. Nesting an ordinary Block adds no control-transfer target. Constructs with their own result rules apply those rules instead. Function bodies follow [Functions](#461-function-bodies-and-results).

A **Value Context** is a syntactic position that uses an expression's value: an initializer, operand, argument, condition, `match` subject, `return` / `exit` / `yield` operand, or Expression body introduced by `=>`. It remains a Value Context even when the expected Type is Unit or the result is subsequently unused. Reachability, constant evaluation, and optimization do not change it.

A **Discard Context** discards an expression's normal result without imposing Unit or suppressing Type, ownership, or destruction checks. Every direct expression, including the last, in an ordinary, Labeled, Unsafe, Deferred, iteration, branch, or function Block body uses this context. Nested initializers, arguments, and operands retain their positional contexts. Discarding a `Result` produces a [compile-time warning](#1123-handling-propagation-and-discarding).

An expression determines its result; its Evaluation Context determines whether that result is consumed or discarded. A `loop` accepts result operands in either context. Selections follow the unified [Result-requiring Selection](#771-branch-results) rules.

Body form, not the number of direct expressions or declarations, determines the branch result rule.

#### 7.2.1. Nonempty executable blocks

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

#### 7.2.2. Conditional compilation and results

Check source emptiness **before directive selection**, independently of reachability. A valid `#if` or `#match` counts even when selection removes all executable Syntax; its own syntax and selection requirements still apply.

```kimi
defer:
    #if windows
        closeHandle()
```

When `windows` is false, the Block remains valid. Source-item checks still apply when early conditional selection excludes all executable items. After selection, normal Type, result-coverage, and transfer rules apply. A direct `()` is discarded and does not supply a Block result.

```kimi
let result = if condition
    () // Error: nonempty, but this branch must explicitly yield its result.
else
    yield ()
```

Likewise, removing a required `yield` or result-bearing `exit` through conditional compilation may cause a result-coverage error, even though the source Block passes the emptiness check.

### 7.3. Block constructs

| Construct | Category | Execution and result |
| --- | --- | --- |
| Labeled Block (`Label:`) | Expression; also usable in Discard Context | Execute now; receive a result through `exit value from Label`. |
| Unsafe Block (`unsafe:`) | Block Statement | Execute now with unsafe permission; no expression result. |
| [Deferred Block](#101-deferred-blocks) (`defer:`) | Block Statement | Register now and execute at Scope Exit; no expression result. |

A **Block Statement** is a statement with a scoped body, not an expression. Unsafe and Deferred Blocks are allowed only in executable bodies, not directly in Declaration Containers. Both have an indented multiline form and a single-line form containing one [InlineStatement](#731-inlinestatement). Both forms create an independent body scope and have the same evaluation and cleanup rules.

At statement start, contextual keywords `unsafe:` and `defer:` take precedence over Label parsing. Neither declares a Label. `unsafe/T` remains Type Semantics syntax and `unsafe func` a function declaration modifier; outside their special contexts these spellings follow normal Name rules.

#### 7.3.1. InlineStatement

An **InlineStatement** is one statement completed on a single line without a following indented Block.

| Form | Requirement |
| --- | --- |
| An expression used as a statement, such as a call or assignment | The complete expression fits on that line. |
| A local `let` or `var` declaration | The complete declaration fits on that line. |
| `return`, `exit`, `continue`, or `yield` | Normal target, operand, and boundary rules apply. |
| Single-line `unsafe:` or `defer:` | Its body is recursively an InlineStatement. |
| Single-line `require condition else ...` | Its condition and complete failure body fit on that line. |

Function and Declaration Container declarations, Labeled Blocks, and constructs requiring a following indented Block are excluded. In nested forms, the right-hand statement is the body of the immediately preceding colon.

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

#### 7.3.2. Labeled block

A Labeled Block begins with `Label:` followed by its indented body on the next line. It receives a result only from an `exit` explicitly targeting that Label. Its trailing expression never implicitly supplies a result, and unlabeled exits still skip it.

A **Result-requiring Labeled Block** occurs in Value Context (even with expected Unit) or has a result-bearing self-targeted `exit`. Classify after target lookup, before reachability analysis. Count resolved self-targeted exits even inside nested constructs, but not results sent elsewhere.

Every reachable completing path must use `exit expression from Label`, including `exit () from Label` for Unit. Fall-through is an error; operandless self-targeted exits are forbidden even if unreachable. Paths transferring outward or never completing need no Block result. Shared [result validation](#78-result-validation) applies in both contexts.

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

#### 7.3.3. Unsafe block

An **Unsafe Block** executes its body immediately with permission for [unsafe operations](#38-raw-pointers-and-unsafe-operations). It is a statement and cannot appear as an initializer, argument, or other expression operand, in either body form. It creates no Control Boundary and does not intercept transfer lookup. Its body follows ordinary Evaluation Context and Scope Exit rules.

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

### 7.4. Labels

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

A labeled Iteration Construct uses `Label: for ...`, `Label: while ...`, or `Label: loop`. Unlike a [Labeled Block](#732-labeled-block), adding a Label to an Iteration Construct does not change its result rules; a labeled `loop` may appear in Value Context:

```kimi
var result = outer: loop
    for value in values
        if found(value)
            exit value from outer
```

Labels follow the character rules for [Names](#25-names) and have a namespace separate from those of variables and Types. Labels with the same Name and overlapping scopes in one function are invalid.

A Label is visible only inside its construct's body, excluding its `for` iterable or `while` condition. A transfer may identify only an enclosing construct in the same Function Boundary without crossing a Deferred Control Boundary. Sibling, inner, and other-function Labels are inaccessible. A Label names a construct, not an instruction address: jumping into a body or back to a completed construct is not supported.

### 7.5. Control transfers

#### 7.5.1. Syntax and operands

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

Operands are evaluated before transfer. If operand evaluation leaves by another transfer or never completes, the original transfer does not occur. Otherwise, its result is secured by Copy or Move before [scope-exit destruction](#102-scope-exit-destruction) and delivery to the target. Each transfer expression itself has type [Never](#315-unit-and-never-types).

#### 7.5.2. Target lookup

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

#### 7.5.3. Function boundaries

Each of these bodies establishes an independent **Function Boundary**:

- Named functions, including methods and nested functions.
- Anonymous functions, including capturing Closures under [function expressions](#643-function-expressions).
- Property getters and setters.
- Constructor bodies (`init`); their completion additionally follows [construction checks](#433-constructors).
- Destructors (`deinit`).

In these control-flow rules, "function" includes all of these bodies. A `return` ends only its own function. Other transfers cannot target an outer function's Labels, Iteration Constructs, or selections.

Getters return their [Getter Result Type](#82-default-getter-results); setters, `init` bodies, and `deinit` return Unit. All follow [function body and result rules](#461-function-bodies-and-results). An `init` body's Unit result is distinct from the owned value produced by the enclosing construction operation. Normal `deinit` completion, including `return`, still performs automatic field destruction required by the Type.

```kimi
func outer() -> i32
    let f = func () -> i32
        return 1                // Returns from f only.

    return f()
```

#### 7.5.4. Label and nesting examples

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

### 7.6. Iteration constructs

#### 7.6.1. `for` and `while`

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

#### 7.6.2. `loop`

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

Only reachable self-targeted exits contribute to inference. Unreachable exits are checked against any available Target Result Type under [result validation](#78-result-validation). Nested selections do not intercept `exit`.

```kimi
loop
    exit 10                     // Valid: loop produces an integer, then discards it.
```

```kimi
outer: loop
    loop
        exit from outer
```

The inner `loop` has no result-producing path and has type Never. The outer `loop` completes with Unit. See [result validation](#78-result-validation) for the common rules.

### 7.7. Selections: if, match, and yield

#### 7.7.1. Branch results

Each `if` branch and `match` arm explicitly chooses an **Expression body** or **Block body**, regardless of its item count:

| Body form | Evaluation and result rule |
| --- | --- |
| Expression body: `=> Expression` | Evaluate the expression in Value Context and implicitly supply its normal result to the selection. |
| Block body: an indented Block | Evaluate every direct expression in Discard Context. Use `yield expression` to supply a result to the selection. No expression, including a sole or final expression, is an implicit result. |

For `if`, the Expression body follows the condition or `else` on the same line; a Block body starts on the next line at a greater indentation. For `match`, `=>` also separates the pattern from its body: an expression follows it on the same line, or an indented Block follows it on the next line. Different branches of the same selection may use different body forms.

A **Result-requiring Selection** is an `if` or `match` that meets any of these conditions:

- It occurs in Value Context, including when Unit is expected.
- One of its own branches has an Expression body.
- A `yield` lexically resolves to that selection.

Resolve transfer targets before this classification, without using reachability. Nested constructs' branch forms and yields targeting them do not count for the outer selection. An `else if` chain is one selection.

Every Result-requiring Selection follows three common requirements:

- **Exhaustiveness:** `if` requires a final `else`; `match` must cover all subject values through its patterns or a catch-all arm. Literal conditions, unreachable branches, and paths that never complete do not waive this requirement.
- **Result coverage:** every reachable path that completes normally must supply a result. A Block must use `yield`, including `yield ()` for Unit; declarations, discarded expressions, and bodies emptied by conditional compilation do not supply implicit branch results. Source-level empty executable Blocks are parse errors under the [nonempty rule](#721-nonempty-executable-blocks). A path leaving for an outer target or never finishing needs no result. After a transfer caught internally, analysis follows the continuation.
- **Result compatibility:** explicit and implicit results obey the shared [result validation](#78-result-validation) rules, even when the selection's result is discarded.

A selection that does not require a result has only Block bodies, no self-targeted `yield`, and occurs in Discard Context. Reaching a selected Block's end or selecting no branch supplies Unit. Paths that leave for an outer target or never finish supply no result to that selection.

#### 7.7.2. `if`

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
else => 2                       // Valid: the integer result is discarded.
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

#### 7.7.3. `match`

`match` evaluates its subject once, tests arms in source order, and executes the first matching arm. Every arm uses `pattern => Expression` or `pattern =>` followed by an indented Block. There is no fall-through to another arm.

```kimi
var result = match value
    A =>
        prepare()
        yield 1

    B => 2
```

`yield` ends the whole target `match`. This example assumes `A` and `B` cover every case. A `match` that does not require a result may be non-exhaustive.

#### 7.7.4. Nested `yield` targets

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

### 7.8. Result validation

A transfer supplies a result only to its resolved target. Function results follow [Functions](#461-function-bodies-and-results); Blocks, Iteration Constructs, and branches use their result sources defined above. Discard Context does not exempt a construct from result validation.

Validate results in this order:

1. Determine Evaluation Contexts and body forms, resolve transfer targets, and classify Result-requiring Selections and Labeled Blocks without excluding unreachable code. Check syntax, Names, operand presence, and local Type correctness. Enforce selection exhaustiveness requirements.
2. Apply [reachability](#782-reachability) analysis to result sources, required Scope Exit processing, and paths leaving each construct. Collect result candidates only from reachable result-delivery paths. A transfer whose operand or required cleanup cannot complete normally supplies no result to its original target.
3. Check result coverage: reject any reachable path that reaches an end requiring a result without supplying one. Where a construct implicitly supplies Unit, include that Unit as a candidate only when the path is reachable. A non-Unit Block-bodied function may not fall through.
4. Determine the Target Result Type as described below, independently of whether the construct's Expression Type is Never. Unreachable result sources do not contribute candidates or constraints to inference.
5. When a Target Result Type is available, check every explicit result operand and implicit Expression-body result against it, including in unreachable code. Operandless `return` and `exit` supply Unit. Apply normal conversion and Origin compatibility rules. A source that cannot itself complete normally supplies no value to compare; its local operations and any transfers inside it are still checked.

Paths that leave a construct for an outer target or never complete supply no result candidate for that construct. Transfers caught internally may let evaluation continue and must be followed to their continuation.

#### 7.8.1. Expression type and target Result type

The **Expression Type** describes a normally completing expression's value. An expression, including a `loop`, selection, or Labeled Block, with no reachable path that completes normally has Type Never. Missing required results are errors, not Never. Unsafe and Deferred Blocks are statements with no Expression Type; analyze their body completion separately.

The **Target Result Type** constrains results supplied to a boundary. Use a declared or expected Type, or infer it from reachable result candidates under normal inference and conversion rules. Propagate expected Types to result sources even if the Expression Type is Never.

| Source of Target Result Type | Compatibility checking, including unreachable results |
| --- | --- |
| Explicit declaration or expected Type | Check against that Type. |
| Type inferred from reachable result candidates | Check against the inferred Type. |
| No Type supplied and no reachable result candidates | No Target Result Type is available; omit only the comparison against it. |

Never inferred solely from the absence of reachable results does not become a Target Result Type. An explicitly specified Never still constrains results. Unreachable fall-through does not manufacture a Unit result for compatibility checking.

A function keeps its declared return Type. Without a declared or expected Type, infer from reachable results. If none exist, expose inferred return Type Never, but do not use that fallback to constrain unreachable returns. The function value retains its concrete Function Item or Closure Type unless converted to a common Function Type.

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

#### 7.8.2. Reachability

Reachability is determined statically within each Function Boundary. Treat a path as reachable unless the following analysis proves otherwise. Optimization settings must not change type-checking results.

- Follow evaluation order, branches, Iteration Constructs, and resolved transfers. A statically non-completing expression has no edge to the next sequential element.
- Follow the continuation of a construct that catches a transfer, such as the code after a Labeled Block ended by `exit`, or an expression consuming a yielded result.
- A `defer` registration does not execute its body. Analyze registered bodies on the Scope Exit paths that reach them; a non-completing cleanup prevents subsequent cleanup and delivery of the pending transfer or result. An exit caught by the Deferred Block finishes that body's cleanup before resuming the pending Scope Exit.
- Prune condition outcomes only for Boolean literals `true` and `false`, optionally parenthesized, in `if`, `else if`, `while`, and `require`. Otherwise, consider both outcomes when condition evaluation completes normally.
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

In each example, the unreachable expression or transfer is locally valid, but its result is incompatible with the target. Undefined Names or Labels, invalid operand operations, and value-bearing exits targeting `for` are also errors in unreachable code. These rules concern runtime control flow; Syntax excluded by [conditional compilation](#13-compile-time-directives) follows its separate Binding rules.

### 7.9. Type refinement and require

#### 7.9.1. Stable bindings and effective types

Runtime Type refinement attaches to the current **Value Instance** of a resolved **Binding Identity**. Eligible subjects are parenthesized or bare names of object-typed local `let` bindings or non-reassignable parameters with struct Core Type targets. `var`, Properties, indexing, and call results can be tested but do not refine later reads. Facts do not transfer to aliases or shadowed bindings.

The **Effective Type** used for a name at a program point narrows only its guaranteed Core Type. Preserve declared Semantics, Origins, Loans, mutability, initialization state, object identity, and complete destruction responsibility. Writes/reinitialization that could replace the binding invalidate old facts; Move/destruction prevents further use. Mutation of members alone does not invalidate facts while the binding value and Dynamic Type remain the same. This adds no `var` refinement or write permission.

Use Effective Type for member lookup, argument applicability, overload resolution, assignment sources, results, and local inference. Apply each candidate's ordinary fitting/adaptation rules; do not discard a base candidate if those rules fit, retry resolution with the declared Type, or change already fixed declarations or destination Types. Explicit object upcasts remain necessary where ordinarily required.

```kimi
func handle(value: objref/Animal) -> ()
    let alias = value
    require value is Dog else return
    value.bark()
    let dog = value       // Inferred objref/Dog.
    alias.bark()          // Error: alias has no Dog guarantee.
```

The new `dog` binding uses ordinary typing and acquisition of the RHS Effective Type, not transferred flow facts. An owning Non-Copy source would Move on such acquisition. A binding's declaration Type never changes.

#### 7.9.2. Condition states and joins

**Flow State** describes refinement, initialization/Move state, Loans, and reachability; it need not be one data structure or pass. Let `True(C, I)` and `False(C, I)` be states after condition `C` from input state `I`. Apply evaluation effects first. Refinement cannot restore a Moved value, discard Loans, or create Boolean exits from non-completing evaluation.

For eligible `x`, the table updates only refinement; “retain” means facts still valid after evaluation:

| Condition | True path | False path |
| --- | --- | --- |
| `x is T` | Add `x` is `T` or derived | Retain |
| `x is not T` | Retain | Add the same positive fact |
| `not C` | `False(C, I)` | `True(C, I)` |
| `(C)` | `True(C, I)` | `False(C, I)` |
| Other Boolean expression | No new fact | No new fact |

For `A and B`, analyze B under `True(A, I)`; true is B's true state, false joins A-false and B-false. For `A or B`, analyze B under `False(A, I)`; false is B's false state, true joins A-true and B-true. These rules apply at every expression position. Short-circuiting skips runtime evaluation, not static syntax/name/Type checks. No exclusion Types, union Types, or provenance through stored Booleans, `== true`, or arbitrary calls is added.

On one path, compatible positive facts select the most derived guaranteed Type. At joins, retain the most derived base guaranteed by every reachable incoming path, never wider than the declaration Type. Incompatible facts on one path revert that binding to declaration-Type checking; they neither make the path unreachable nor prove arbitrary Types. Contradiction alone may warn but is not an error. Other Flow State components keep their own joins.

| Construct | Propagation |
| --- | --- |
| `if` / `else` | Condition true / false; `else if` starts with preceding false facts |
| `while` | True to body; false to condition exit |
| `require` | True to subsequent statements; false to failure body |
| Subsequent joins | Only paths that actually reach the point after transfers and cleanup |

Track continuations of constructs that catch transfers. Early return through an ordinary `if` can therefore refine later statements. Loop entries join the first entry and every backedge; exits join condition-false and delivered `exit` paths. Propagate `continue` to its correct iteration point. Solve these rules to a stable result independent of processing order; a previous successful iteration alone is insufficient.

`defer` resolves Names and Effective Types at registration, then checks initialization and Loans on actual cleanup paths. Later refinement cannot reinterpret an earlier registration. Function boundaries do not import outer flow facts. A capture uses the source binding's declared complete Type and the ordinary capture operation; it does not import refinement attached to the outer binding. To capture a narrowed Type, first initialize a new local from the refined expression. Its inferred declaration Type is then available under the normal capture rules.

#### 7.9.3. Require statement

##### 7.9.3.1. Syntax and placement

`require condition else failureBody` is an executable statement, allowed in Blocks and SourceDocument execution scope, not directly in Declaration Containers. It has no Expression Type, result target, Control Boundary, or Label. Successful statement Completion is `Normal(())`. It adds no implicit assertion, Panic, exception, import, or failure propagation.

The condition is `bool`, with normal Never rules. Parentheses are optional; no `let`/`var` condition, comma-separated condition, optional binding, or pattern binding is added. Existing Boolean binding in `if`/`while` remains available without test-provenance propagation through that binding.

`else` is mandatory: place it on the condition's final physical line or the next effective line, ignoring blank/comment-only lines, aligned with the starting `require`. No statement, directive, or semicolon may intervene. Multiline conditions follow normal continuation. A same-line failure body is one InlineStatement; otherwise use a nonempty Block one indentation level deeper than `require`. Both forms create a failure-local scope. No colon, `=>`, or braces are used.

```kimi
require ready else return

require (
    firstCondition and
    secondCondition
)
else
    logFailure()
    return
```

Only a wholly single-line form is itself an InlineStatement. In nested single-line forms, an `else` belongs to the innermost unmatched `require`/`if` within that construct. Parenthesize selection-expression conditions to delimit their `else`.

##### 7.9.3.2. Evaluation and non-continuation

Evaluate the condition once. True continues with its true Flow State; false executes the failure body with its false state. Non-completing condition evaluation follows its transfer/divergence/Panic and executes neither branch afterward.

Independently assume entry to the failure body, even for literal `true`. Every reachable path, including required Scope Exit, must fail to continue normally to the statement after `require`; otherwise reject it. Outer-targeted return/exit/continue/yield, a non-returning call, divergence, or Panic can satisfy this rule. Transfers caught inside the failure body must be followed through their continuations.

```kimi
require ready else logFailure() // Error: normally continues.
require true else ()            // Error even though the condition is true.
require ready else
    loop
        exit                   // Error: only exits the inner loop.
```

A loop completes normally only through a reachable exit targeting it whose operand and cleanup can deliver a result. Use resolved call result Types, not analysis of arbitrary callees' bodies. Registering a defer does not execute it; analyze its effects on actual exits.

##### 7.9.3.3. Transfers and cleanup

`require` is transparent to transfer-target lookup in both condition and failure body. Existing Function/Deferred boundaries and result rules remain in force. It is not a syntactic rewrite to `if`, which would introduce a Selection Boundary.

```kimi
let answer = if enabled
    require ready else yield 0 // Targets the outer if.
    yield calculate()
else => 0

defer:
    require needsCleanup else exit // Ends this Deferred Block.
    cleanup()
```

No implicit function or process exit is created at top level; `return` there still needs a Function Boundary. Deferred Blocks still cannot return from an outer function. Secure transfer results before cleanup of the scopes actually left; success leaves the enclosing scope active. Panic follows ordinary termination without Scope Exit.

Apply existing literal-only reachability rules. `require false` has no normal successor, but subsequent syntax, Names, Types, and targets remain checked. Dynamic-type contradictions and general constant propagation cannot add unreachable paths or change acceptance across optimization settings.

## 8. Properties

A **Property** is Kimigayo's only value-bearing member kind. Storage slots, global storage, and other lowered representations are implementation details. A `let` or `var` inside an executable Block is a local binding.

A Property has a **Property Type**, optional `get` and `set` accessors, and optional owned storage. The Property Type determines storage, Consume result, and setter input Types. A read uses the **Getter Result Type**, which may differ. Custom getters/setters remain supported; [Property Consume](#89-property-consume) requires accessible standard get and set instead of a separate Move accessor.

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

An instance Stored Property must explicitly bind every borrow Origin and required Type Origin argument in its complete Property Type, including nested layers and generic Type arguments, under [Origin binding](#94-origin-elision-and-return-contracts). Bindings use the containing Type's declared abstract Origins or `static` where valid; `self` does not provide an implicit self-borrowing storage contract. Neither a declaration initializer nor constructor assignments infer the Property's Origin contract.

A static Stored Property may not retain a safe borrow in this revision, directly or through stored aggregate fields, elements, or a closure environment. This prohibition includes borrows bound to `static`; `Owned` alone is not sufficient to permit such storage. Apply the check after alias expansion and generic substitution, retaining unresolved generic obligations until specialization. A callable signature that accepts or returns a borrow, or a raw pointer's Referent Type, does not itself mean that the stored value retains that borrow. Computed Properties have no storage to which this prohibition applies; their getter results obey the existing result rules.

### 8.1. Effective representation

Expand source accessors before classifying storage, but preserve whether each is compiler-provided standard access or a custom body:

| Source form | Effective access |
| --- | --- |
| `let x: T`, no explicit accessors | Standard get |
| `var x: T`, no explicit accessors | Standard get and set |
| Any explicit block or `has` accessor list | Exactly the written accessors; no implicit additions |

An explicit list suppresses unwritten accessors, including get when only set is written. A bodyless getter performs the standard Copy/shared read; a bodyless setter applies Initialization/Replacement. Each binds `storage`; custom accessors determine storage by their bound uses. Equivalent user code does not confer standard field-scoped semantics. Standard get/set also provide the declaration-side prerequisites for Consume, without a separate opt-in accessor.

```kimi
var age: i32 = 0
    get
    set
        storage = max(value, 0)
// Explicit standard get plus custom set. Omitting get makes this write-only.

var count: i32
    get => items.count // Computed and read-only if items is separate state.
```

An initializer does not create storage by itself; it is valid only for a Stored Property. `let` provides immutable stored data without a setter and cannot be consumed as a Property. This differs from an owned `let` local or receiver. Read-only computed Properties use `var` and an explicit getter.

### 8.2. Default getter results

An omitted or bodyless getter uses the complete Property Type to select a Copy or shared-borrow operation. It never moves from the instance, implicitly duplicates object ownership, or returns an exclusive borrow. [Copy capability](#351-copy-capability-and-explicit-duplication) determines this classification.

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

For ordinary borrowed receivers, a default instance getter's borrow/reborrow has Origin `from self`, bounded by the receiver even when storage carries a longer Origin. On a statically identified owned receiver, [standard field access](#810-access-after-partial-move) creates a Loan on the field instead of borrowing the entire instance; its Origin is bounded by field storage and owner validity. A shared reborrow suspends conflicting access through the stored exclusive reference while the result is live. A Copy preserves the value's existing Origin dependencies without extending them or replacing them with `self`.

A default static getter that creates a borrow anchors it to the Property's storage and its current stored value. Static allocation alone does not permit replacement or destruction of that value while the borrow is live. Normal Origin and Loan rules still apply. Borrowing an otherwise permitted static stored value for local use is distinct from storing a borrowed value in static storage; the latter is forbidden in this revision.

For example:

```kimi
struct Parent
    public var child: obj/Node
```

has these conceptual signatures for ordinary borrowed-receiver access:

```text
get(self: ref/Parent) -> objref/Node from self
set(self: uniq/Parent, value: obj/Node) -> ()
```

Reading `parent.child` borrows the object; assignment passes ownership to its setter. The standard get/set declaration above permits [Property Consume](#89-property-consume) when the caller owns the struct receiver and all use-site conditions hold. An authorized exchange or Type-specific operation follows its own rules.

A stored `let value: uniq/T from source` similarly returns `ref/T from self`, leaving the exclusive capability in storage. Shared inspection does not make the containing structure Copy.

For generic Properties, unresolved Type Semantics or Copy capability leave the default rule dependent. Binding may use a result Type only once constraints or specialization establish its row, and must resolve the operation before finalizing a specialization. An unconstrained Core Type parameter is not assumed Copy. Use `T is Copy` when Copy capability is required.

### 8.3. Accessors

A getter defines a read and follows the [function body and result rules](#461-function-bodies-and-results), with the Getter Result Type as its Target Result Type:

```kimi
var area: f64
    get => self.width * self.height

var loggedArea: f64
    get
        logRead()
        return self.width * self.height
```

A computed getter must explicitly start with `get`; a bare expression in the Property body is invalid.

`get -> ResultType` specifies the Getter Result Type, including any Origin annotation. Without it, even a custom getter uses the [default result Type](#82-default-getter-results); its body does not infer a different Type. Omitted result Origins on custom getters follow function Origin elision.

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
    get => self.right - self.left

    set
        self.right = self.left + value
```

Neither accessor uses `storage`, so this Property is computed and read-write.

### 8.4. Inline accessor declarations

A Property may declare bodyless accessors inline with a `has` clause:

**Basic example.**

```kimi
public var count: i32 has get, private set
```

The clause follows the Property initializer when one is present:

```kimi
public var count: i32 = 0 has get, private set
```

The grammar is:

```text
inline-accessors := has accessor-declaration (',' accessor-declaration)*

accessor-declaration := access-restriction? get ('->' ResultType)?
                      | access-restriction? set
```

The list must contain at least one accessor. `get` and `set` may each appear at most once, in either order; unwritten accessors are not added. Access restrictions match those in a Property body. `move` is not an accessor declaration.

#### 8.4.1. Concrete properties

`has` expands to the same bodyless declarations as an indented list, before storage classification:

```kimi
public var count: i32 has get, private set
// Same standard accessor declarations as:
public var otherCount: i32
    get
    private set
```

Standard get performs the default read and standard set writes storage. Their intrinsic storage references determine `HasStorage`; `has get` is Stored and read-only. Preserve the standard-accessor marker and field-scoped semantics during expansion rather than turning them into ordinary user bodies.

Inline and indented accessor lists cannot be combined. Use an indented list for custom bodies. `let` cannot declare a setter.

#### 8.4.2. Contract property requirements

Inside a `contract`, `has` declares the accessor capabilities that a conforming Property must provide:

```kimi
contract Collection
    var count: i32 has get

contract MutableCollection
    var count: i32 has get, set
```

Conformance requires a compatible Property Type and all required accessors with sufficient accessibility. A getter must also satisfy the required Getter Result Type and Origin contract; a setter accepts the required Property Type under normal parameter compatibility rules.

A contract getter requirement may specify a result Type, for example `var child: obj/Node has get -> objref/Node from self`. Without an annotation, the required Getter Result Type follows the same default result rules as a concrete Property, but no storage read or Loan is generated by the requirement itself. Here `self` denotes the required shared receiver. A conforming getter must provide a result compatible with the required result Type for every legal receiver Origin; it cannot impose a shorter lifetime than the requirement promises. Generic requirements retain unresolved default result rules until the applicable constraints or specialization determine them.

A contract requirement creates no implementation, storage read, Loan, effective storage representation, or `HasStorage` classification. There is no Move accessor requirement or new Consume contract syntax. Both a stored `var count: i32 has get` and a computed `get => items.count` may satisfy the readable requirement. In concrete declarations, `has` defines bodyless accessors; in contracts, it specifies required capabilities only.

Generic requirements establish declared capabilities, not flow state at a use. They do not prove that a value is currently Initialized or that no conflicting Loan exists. These conditions must be checked at each use; `has get, set` alone does not prove Stored representation or compiler-provided standard accessors.

### 8.5. Contextual identifiers and receivers

`storage` denotes the Property's actual owned location, not a copy. It has the Property Type, and a bound use causes the location to exist. `value` is available only in setters. These Names are not globally reserved.

Ordinary instance getter/setter access has these implicit signatures:

```text
get(self: ref/Self) -> GetterResultType
set(self: uniq/Self, value: PropertyType) -> ()
```

The getter has shared/read access to the instance and its storage; the setter has exclusive/read-write access. Receivers do not change the Type of `storage` to `ref/T` or `uniq/T`. The Getter Result Type separately describes the read or borrow result.

For statically identified owned places, compiler-provided standard getter/setter access instead follows [field-scoped operations](#810-access-after-partial-move), including after Partial Move. Property Consume has no ordinary borrow receiver. Custom bodies retain the signatures above. Mutable or exclusive getter receivers are not supported. Static Properties, including `group` members, have no instance receiver.

Object receiver use requires a verified [ObjectCompatible entry](#6451-object-receiver-compatibility); sharing a body does not convert object borrows into unrestricted ordinary value borrows or confer standard-accessor privileges on custom accessors.

### 8.6. Initialization

An initializer initializes owned storage directly and does not invoke the setter:

```kimi
var age: i32 = -1
    set
        storage = max(value, 0)
```

Here the initial stored value is `-1`; a later assignment of `-10` invokes the setter and stores `0`. A Property initializer is invalid when `HasStorage = false`, because no Property-owned location exists to initialize. A stored Property without a declaration initializer must be initialized according to the containing type's definite-initialization rules before it is read. After initialization, a `let` Property cannot be assigned.

For a structure, evaluate declaration initializers once in [logical declaration order](#431-split-structures-and-storage-order), preserving their observable side effects regardless of physical layout or parallel compilation. A Property becomes initialized only after its initializer completes normally and its result has been secured in that Property's storage. This does not implicitly initialize Properties that have no declaration initializer.

An instance Property declaration initializer must not access the partially constructed `self`, including by reading, borrowing, or writing another Property of that instance, invoking a member on `self`, or passing or otherwise exposing `self` to another operation. This restriction also applies to earlier Properties of that instance that have already been initialized. Cross-Property dependencies must be expressed in an explicit or source-generated [constructor body](#433-constructors), subject to its restricted direct field access and definite-initialization rules. Ordinary independent calls and accesses to other fully initialized values remain allowed under normal Type and lifetime rules and run in logical order. A declaration initializer is not textually inserted into a constructor: it cannot use constructor parameters or transfer control out of that constructor. Any transfer within its expression must target a construct contained in that expression.

Construction completion and current completeness follow [Value Lifetime](#912-aggregate-construction-and-completeness); a constructor commits only after its successful body exit and cleanup satisfy the completion checks. Normal Scope Exit abandoning unfinished storage destroys only initialized components still owned by construction, in reverse logical component order, never the unfinished layer's own `deinit`. A completed base is cleaned up after that layer's remaining fields, including the base's own `deinit` when present. For declaration initializers this is also reverse initialization order because they execute in logical order. Cleanup does not undo effects; [Panic Termination](#113-panic-termination) does not unwind. See [field cleanup](#1032-field-cleanup). This introduces no exception or failure-constructor syntax.

For example, this declaration is invalid because its explicit getter does not refer to `storage`, so its effective representation is computed:

```kimi
var value: i32 = 10
    get => calculateValue()
```

### 8.7. Access control

Accessors inherit the Property's accessibility unless they declare a stricter restriction. An accessor cannot be more accessible than its Property. Restrictions do not change a bodyless accessor's default implementation, whether inline or in the Property body:

```kimi
public var count: i32 = 0 has get, private set
// Public default getter; private default setter.

private var value: i32
    public set // Error: broader access than the Property.
```

An explicit accessor restriction must be strictly narrower in declared access, using this table; enclosing restrictions that happen to make effective domains equal do not permit a broader or repeated modifier. Protected forms also require the structure-member context defined by [general accessibility](#512-accessibility-and-reachability).

| Property declared access | Permitted explicit accessor restrictions |
| --- | --- |
| `public` | `protected internal`, `protected`, `internal`, `private protected`, `private` |
| `protected internal` | `protected`, `internal`, `private protected`, `private` |
| `protected` | `private protected`, `private` |
| `internal` | `private protected`, `private` |
| `private protected` | `private` |
| `private` | None; omit the accessor restriction |

For example, a `protected` Property cannot have an `internal` setter, because unrelated code in the Kotonoha would gain access. Property and accessor API Types obey [signature accessibility](#5122-api-signature-accessibility). A selected getter or setter must also pass the [protected receiver check](#5121-effective-access-domains-and-protected-receivers), when applicable. Override accessibility follows [inheritance](#432-inheritance-and-open-structures); a private or otherwise inaccessible base accessor cannot be made overridable by overriding the Property.

### 8.8. Storage, addressability, and result semantics

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

### 8.9. Property consume

`receiver.property@move` explicitly extracts the stored value under the common [Consume rules](#915-explicit-consume). Properties expose only `get` and `set`; there is no Move accessor, separate Move accessibility, or prefix `move` expression.

#### 8.9.1. Eligibility and permissions

Property Consume requires all of the following:

| Structural condition | Requirement |
| --- | --- |
| Representation | Instance Stored Property of a struct |
| Accessors | Both compiler-provided standard `get` and standard `set` |
| Receiver | Caller-owned struct place, including an owned Temporary Place |
| Path | A supported static Move Path |

At the use site, both accessors must be accessible, receiver construction must have completed, and the target must satisfy initialization, completeness, Loan, Origin, and `deinit` conditions. Standard accessor annotations do not relax these requirements. Consume invokes neither accessor.

Getter visibility bounds read access; setter visibility bounds changes to storage. Consume requires both permissions, without turning get permission alone into permission to extract ownership. Requiring standard accessors prevents bypassing custom validation or transformation. A public standard setter therefore exposes Consume wherever the standard getter is also accessible.

Custom accessors, computed Properties, static Properties, and setter-less `let` Properties are ineligible. An owned `let` **receiver** may still contain a consumable `var` Property: receiver writability is needed for reinitialization, not extraction.

```kimi
struct Person
    public var name: string has get, set
    public var age: i32 has get, set

let person = makePerson()
let name = person.name@move // Allowed with the common Consume conditions.
// person.name = "Alice"   // Error: receiver is not writable.
```

Get/set requirements on a generic Property do not establish Stored representation or standard accessors. [Generic constraints](#842-contract-property-requirements) may establish structural facts, but each use still needs the common legality checks. No new Consume contract syntax is introduced.

#### 8.9.2. Receiver evaluation and result

Evaluate the receiver once by its ordinary rules, then locate and Consume the target storage without invoking its getter or setter. Do not bypass another Property getter while evaluating the receiver. A local receiver designates its storage without acquiring the whole instance by value.

An owned struct Temporary Value is [materialized](#36-temporary-values-places-and-lifetimes) before extracting its field. Remaining fields follow the temporary's usual lifetime; the extracted value follows its destination.

```kimi
let item = makeHolder().item@move
// Move item out of the owned temporary; clean up its remaining fields normally.

let name = outer.inner.name@move
// Evaluate outer.inner through its getter.
// A borrowed receiver forbids Consume; a complete owned temporary may allow it.
// This never bypasses inner's getter to extract from its original storage.
```

The result has the complete **Property Type**, including Origins, rather than the Getter Result Type. Copy fields also become Moved. Moving a reference or pointer transfers that value/capability, not ownership of its referent. Failed Consume never falls back to a getter or another Property.

To extract from original storage at several Property boundaries, make each operation explicit:

```kimi
var inner = outer.inner@move
let name = inner.name@move
inner.name = "Alice"
outer.inner = inner@move
```

Each extraction requires Consume permission; each restoration requires writable storage and an accessible standard setter. Borrowed or object receivers remain ineligible, including `uniq/Self`; an owned struct **field value** of Type `obj/Node` is allowed when the other conditions hold.

### 8.10. Access after partial move

For a statically identified owned place whose construction previously completed, standard getters/setters operate on the target field without creating a whole-instance `ref/Self` or `uniq/Self`. This applies to all standard field operations, not just consumable Properties. Equivalent custom bodies do not gain this behavior.

| Operation on an incomplete instance | Condition |
| --- | --- |
| Standard `get` | Target value is Initialized and, if an aggregate, complete; readable/borrowable |
| `property@move` | All Property Consume conditions hold for the target |
| Standard `set` | Storage is writable and setter accessible |
| Custom accessor or whole-receiver method | Forbidden until the instance is complete |
| Whole-instance read, Copy, Move, or borrow | Forbidden until complete |

A new standard-getter Loan targets the field and is bounded by its storage and owner validity. Copying a stored shared borrow instead preserves that borrow's Origins. A whole-instance Loan overlaps every field; a Loan on a disjoint field need not block Consume. Results and remaining fields keep dependencies that later destruction or replacement must not invalidate.

```kimi
var person = makePerson() // Person as defined above.
let borrowed = person.name
inspect(borrowed)         // Last use of this borrow.
let name = person.name@move
let age = person.age@move // Allowed: age is complete even though person is not.
person.name = "Alice"
person.age = 30
use(person)              // Complete again.
```

Using `borrowed` after the first Move would create a conflicting Loan. Deferred uses participate in the same checks.

Standard `set` follows RHS-first [assignment](#671-simple-assignment). An Initialized destination undergoes Replacement; Moved or legally Uninitialized storage undergoes Initialization. A partially initialized aggregate field first destroys only its remaining parts. Apply these rules per path at joins. Restoring every field makes an already-constructed instance complete without rerunning its constructor; it grants no access during initial construction.

```kimi
person.name = person.name@move // Extract, then reinitialize the empty field.
// person.name = person.name  // Error: getter yields ref/string; setter needs string.
```

## 9. Ownership and lifetime analysis

### 9.1. Initialization and consume analysis

This section checks the initialization, completeness, and Consume legality of values defined by [Values, places, and storage](#34-values-places-and-storage).

#### 9.1.1. Storage, state, and responsibility

Track initialization state and destruction responsibility per place:

| State | Meaning | Read / borrow / Copy / Move | Write to `let` | Write to `var` |
| --- | --- | --- | --- | --- |
| Uninitialized | No initialized value is held | Forbidden | Only if never initialized | Initialization |
| Initialized | An initialized value is held | Subject to Type and access rules | Forbidden | Replacement |
| Moved | The former value/capability and responsibility were transferred | Forbidden | Forbidden | Reinitialization |

Moved records the source's history, not whether the destination value is still alive. Track a `let` place's first initialization separately; neither Move nor internal Destruction resets it. This revision provides no general user operation to explicitly destroy a place and reset that history. State alone grants no access or write permission.

Every read, borrow, Copy, or Move requires initialization on every reachable incoming path. `let` permits one initialization per path during its binding lifetime; `var` permits later initialization/replacement under ordinary permissions. Stored `let` Properties follow the same first-initialization limit and their dedicated access rules.

```kimi
var number: i32
if condition
    number = 1
else
    number = 2
print(number)             // Both paths initialize number.

let resource: Resource
resource = makeResource()
consume(resource)         // Move.
resource = makeResource() // Error: let cannot be initialized again.
```

#### 9.1.2. Aggregate construction and completeness

Track two facts independently: **construction completion**, recording successful completion of initialization, and **current completeness**, requiring every stored component to be Initialized and complete. A **complete value** satisfies both. For a derived structure, components are its base subobject and its own Stored Fields; track completion separately at each base/derived layer. This revision has no optional-to-initialize stored fields. Computed Properties are not components and require no storage initialization.

Constructors verify all fields and success conditions at a requested successful exit, complete body Scope Exit while retaining construction storage, and then commit completion before exposing or transferring the complete value. These are the [constructor phases](#433-constructors); there is no user operation that commits early or resets a completed layer. An abrupt incomplete exit or Panic does not commit it. Merely assigning all fields does not bypass remaining constructor work or cleanup. A complete field or base may own a valid value while its containing layer is still under construction; failure to complete the containing layer does not erase that component's completion fact or destruction responsibility.

Before completeness, whole-value reads, Copy, borrowing, Move, and exposure are forbidden, including ordinary accessors taking the whole `self`. Direct operations on initialized fields follow their own access rules. After a completed construction followed by Partial Move, field-scoped [standard Property operations](#810-access-after-partial-move) are allowed; this does not authorize initial-construction access.

Partial Move changes current completeness, not the construction-completion fact. Permitted reinitialization of all missing fields restores completeness without rerunning a constructor. If a field cannot be reinitialized, that value remains incomplete and cannot be used/transferred as a whole; its remaining Initialized parts can still be used and cleaned up. No separate permanent-incomplete state is defined. Whole-value Move transfers its construction information; whole replacement uses the new value's information.

Tuple and array construction places elements in increasing element-index order. Each element acquires its own initialization and responsibility only when placement completes normally; a partly built element is tracked recursively. The aggregate commits completion after all elements are placed. If an element expression leaves the construction by ordinary control transfer, first secure that transfer's result, then clean the abandoned construction's remaining elements in decreasing index order. Previously moved arguments and completed side effects are not rolled back. Raw uninitialized memory is not an alternate safe construction syntax.

#### 9.1.3. Move paths and partial move

A **Move Path** is a statically trackable path with independent initialization state and destruction responsibility. Initial paths include stored fields, Tuple elements, fixed-length array indices determined by language constant evaluation during semantic analysis, and combinations of these. Runtime indices, dynamic containers, and user indexers are not added even for literal indices. Do not use optimization-derived constant propagation or arbitrary integer proofs to expand the accepted paths.

A Move Path defines tracking granularity, not access permission:

| Source access | Partial Move |
| --- | --- |
| Tuple element / constant-index fixed array element | Direct place acquisition normally Moves a non-Copy value |
| Struct Property | Ordinary access uses `get`; `@move` requires Property Consume eligibility and permissions |

```kimi
var pair: (string, i32) = ("Alice", 30)
let name = pair.0  // Partial Move; pair is incomplete.
let age = pair.1   // Remaining initialized part is usable.
// let all = pair  // Error: incomplete.
pair.0 = "Bob"
let all = pair     // Complete again.
```

Do not Move a non-Copy referent or subpart through `ref`, `uniq`, `objref`, or `objuniq`, leaving the borrowed place Moved/Uninitialized, even if a later reinitialization is planned. Exclusive access does not transfer ownership. Explicit Consume likewise cannot extract through borrowed referents or borrowed Property receivers, even for Copy values.

User-defined `deinit` assumes a complete value. Reject Partial Move that invalidates this assumption for the aggregate itself or any enclosing ancestor, including nested paths, methods, and Destruction. A complete owned value whose own Type has `deinit` may move as a whole.

Use [Exchange or Swap](#97-initialization-preserving-exchange), or a Type-specific operation, when ordinary extraction is forbidden. Exchange preserves initialization; Type-specific invariants remain the implementation's responsibility and may require restricted storage access. A Move Path never bypasses an accessor.

#### 9.1.4. Consume verification and representation

Separate two static checks; they are not runtime fallback stages:

```text
Consume
├─ Eligibility: does the declaration, Type, and path provide the operation?
│  ├─ supported place kind and ownership path
│  ├─ trackable Move Path
│  ├─ required storage and accessor properties
│  └─ structural Partial Move / deinit restrictions
└─ Legality: may this use site perform it?
   ├─ required accessibility
   ├─ target Initialized on every incoming path; complete if an aggregate
   ├─ required receiver construction previously completed
   ├─ no conflicting Loan; valid Origins
   └─ actual ancestor path and Destruction conditions
```

An accessor's existence is structural; accessibility depends on the use site. Constraints can prove structural facts, not current initialization or absence of Loans. Unknown structural facts follow [generic Access Effect resolution](#527-generic-access-effects); no new Consume contract syntax is defined.

An ancestor may be incomplete if the target remains Initialized and complete, and can be located without whole-value access to that ancestor. Whole-receiver reads/borrows remain forbidden. User-defined `deinit` can make a path structurally ineligible; also check the actual ancestors at each use. Moving a complete value as a whole is distinct from Partial Move.

Track per-path state, destruction responsibility, first initialization of `let`, construction completion, and current completeness across branches, loops, transfers, and `defer`. Apply [Destruction lifetime checks](#966-destruction-lifetime-checking). Raw-pointer operations need not recover or repair an untracked original owner's responsibility.

Lowering may elide transfers and temporary storage or use conditional cleanup flags only while preserving values, abstract place identity and lifetime, Move state, Loans, Origins, destruction responsibility, and specified failures. Optimization must not change which programs or Move Paths are legal.

#### 9.1.5. Explicit consume

`E@move` transfers a value/capability and its applicable destruction responsibility. Unlike ordinary acquisition, it forces Move even for Copy Types. It produces a Temporary Value with the source's complete Type and Origin dependencies; a source Place becomes Moved. It neither converts the Type nor extends lifetime.

```text
E@move
├─ Access Designator -> resolve Consume
│                      ├─ eligible and legal -> extract from the Place
│                      └─ otherwise -> compile-time error
└─ other value-producing expression -> evaluate normally, then Move its temporary
```

[Access Designator](#34-values-places-and-storage) classification does not grant Consume permission. Failure never falls back to Read, a getter/indexer result, or materialization of that result. This applies to computed Properties and unsupported runtime indices or indexers as well as direct places. Parentheses preserve the classification. Receiver, index, and pointer subexpressions follow ordinary evaluation; Consume does not propagate into them or bypass intervening getters.

Safe source kinds are owned root places, Tuple elements, fixed-array elements at language-constant indices, and eligible [stored struct Properties](#89-property-consume). Apply the same ownership and path conditions to Copy Types. Exclude safe extraction through borrowed referents, object receivers, static Properties, user indexers, and runtime indices. Generic owned arguments can move as whole values without an extra Consume contract.

Raw dereference such as `(*p)@move` follows Unsafe rules instead of safe path tracking; preventing later reads or double destruction by an untracked owner is the programmer's obligation.

Move is permitted from an owned `let` local without granting reinitialization. A setter-less `let` Property is ineligible, while a `var` Property inside an owned `let` receiver may be eligible. Restoring storage still requires its usual Write permission, and moving it does not reset `let`'s first-initialization history. There is no separate permanent-incomplete state.

```kimi
let number: i32 = 10
let taken = number@move // number is Moved despite being Copy.
let result = transform(holder.item)@move
// Ordinary argument/getter evaluation; only transform's result is explicitly moved.
```

To move a Read result intentionally, first obtain it with `let value = expression`, then use `value@move`. References and pointers move as values/capabilities, not as ownership of their referents. Borrowing or reborrowing a Move result follows the ordinary temporary, Loan, and Origin rules; it does not restore the original place.

Value transfer, destruction-responsibility transfer, and source-state updates form one operation, with no intervening user code, Destruction, or control transfer. Source memory need not be erased. Operand evaluation may execute user code, and completed effects are not rolled back on later failure.

```kimi
x@move@move // Move from x, then Move the resulting temporary.
```

### 9.2. Origin expressions and ordering

The basic meaning of Origins and Loans is defined in [Origins and Loans: overview](#37-origins-and-loans-overview). This section defines annotation expressions and their ordering.

#### 9.2.1. Origin expressions

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

#### 9.2.2. Ordering and intersection

`o1 : o2` means that `o1` outlives `o2`: `region(o1) ⊇ region(o2)`.

The relation is reflexive and transitive. `static` outlives every Origin.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

Consequently, `o1 and o2` never outlives either operand. A result declared `from x and y` is valid only in the region common to both inputs.

#### 9.2.3. `static` and `Owned`

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

### 9.3. Abstract origins

Functions and types may declare abstract Origin parameters separately from type parameters:

```kimi
func unwrap<T> origin s(v: View<T> from (source => s))
    -> ref/T from s

struct View<T> origin source
    let value: ref/T from source
```

Function Origins are universally quantified. Origin parameters occupy a namespace distinct from type parameters.

#### 9.3.1. Origin arguments

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

Omitted Origin arguments follow the [position-specific rules](#94-origin-elision-and-return-contracts). Parameter Types and instance Stored Property Types require explicit arguments for Origin-bearing aggregates; local initializers may infer them, and result Types use result-Origin elision. No argument defaults to `static` merely because it appears in a generic Type argument. References to the containing `Self` retain that Type's already-bound abstract Origins and do not introduce a new omitted argument list.

#### 9.3.2. Variance

These are static subtype rules under [Type relations and expression operations](#39-type-relations-and-expression-operations). They do not create or authorize a value operation; acquisition and existing Loan obligations must be checked separately.

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

`uniq/T` remains invariant in `T`. These variance rules do not add ordinary inheritance upcasts or value operations for callable signature matching; use the separately defined [object adaptations](#6647-object-upcasts).

#### 9.3.3. Loan requirements

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

The requirement determines which caller-side Loan must remain active while a returned or stored Origin-bearing value is live. It is not an additional Copy classification condition: [Copy capability](#351-copy-capability-and-explicit-duplication) is structural, while actual Loan conflicts are checked at each use.

### 9.4. Origin elision and return contracts

Origin omission depends on the position of the complete Type. These rules apply to `ref`, `uniq`, `objref`, and `objuniq`, after alias expansion and normalization of grouping and redundant owner prefixes. They do not create a safe-borrow Origin for `unsafe/T`.

| Type position | Meaning of an omitted Origin |
| --- | --- |
| Direct borrowed parameter or borrowed receiver | Introduce an independent input Origin for that input's outer borrow layer. |
| Function result | Apply the result-Origin rules below; whole-result inference retains its existing rules. |
| Local binding with inferred Type | Infer the Type, Origin dependencies, and Loans from the initializer under ordinary acquisition rules. |
| Explicit local Type containing a borrow or Origin-bearing aggregate | Infer omitted Origins and Origin arguments from the declaration initializer and ordinary Origin/Loan constraints. Without an initializer, omission is a compile-time error. |
| Instance Stored Property | Require explicit bindings for all borrow layers and required Type Origin arguments; do not infer the storage contract from initialization. |
| Static Stored Property | Safe-borrow retention is forbidden, including nested borrows and explicitly `static` borrows. |
| Generic Type argument or nested value Type | Recursively apply the enclosing position's rule; being a Type argument does not introduce a separate default. |

An implicit input Origin is universally quantified for that input and supplied from the caller's argument or receiver. It is not the lexical lifetime of the parameter variable. Different direct borrowed inputs introduce independent Origins unless explicit annotations relate them. Only their outer direct borrow Origins participate in result elision. Borrow Origins nested inside a parameter's Referent Type, generic arguments, or aggregate Type must be explicit; they are not additional implicitly quantified inputs. An already-bound generic Type parameter or `Self` preserves its existing dependencies. Fixed expected callable signatures and the limited input quantification of [Callable constraints](#444-callable-constraints) retain their own rules; this section adds no general higher-ranked Origins.

For locals, inference means satisfying ordinary subtyping, variance, outlives, and Loan constraints, not requiring literal equality with the initializer's Origin. Permitted shortening remains available. The declaration fixes the local Type and its Origin constraints; later assignments must satisfy that contract and cannot extend a source lifetime or erase a retained Loan dependency. Explicit Origin annotations remain constraints on the initializer and all subsequent assignments.

Instance storage exposes the containing Type's declared Origin contract. Bind each retained dependency to the containing Type's abstract Origins, or explicitly to `static` where ordinary validity and Loan rules permit. An exclusive borrow still requires a valid unique Loan anchor; longevity alone is insufficient. Initializers and constructors must satisfy these bindings, rather than determine them.

```kimi
func f<T>(x: ref/T, y: ref/T)
// Independent input Origins x and y.

func nested<T> origin inner(x: ref/(ref/T from inner))
// The outer borrow has implicit input Origin x; inner is explicit.

func invalid<T>(x: ref/ref/T) // Error: the inner Origin is omitted.

struct View<T> origin source
    var value: ref/T from source

func use<T>(x: ref/T)
    var local: ref/T = x // Infer an Origin satisfying initialization and use constraints.
    var missing: ref/T  // Error: no initializer to infer the omitted Origin.

group Global
    var value: ref/i32 from static // Error: static storage may not retain a safe borrow.
```

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
func get<T> origin s(v: View<T> from (source => s)) -> ref/T from v.source
```

An explicit `from` clause overrides elision. Thus this result depends on `self`, not on the conservative meet `self and key`:

```kimi
func lookup(self: ref/Self, key: ref/Key)
    -> ref/V from self
```

Whole-result inference for anonymous functions also infers environment-derived Origins and Loans under [Closure result rules](#982-closure-dependencies-and-call-results); an explicit annotation retains the elision above.

#### 9.4.1. Return contracts

A declared return Origin limits the dependency visible to callers without requiring a borrow from that specific input. Every explicit or implicit result, including unreachable ones, must subtype the declared result Type under [result validation](#78-result-validation) and [reachability](#782-reachability).

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

### 9.5. Exclusive origins

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

### 9.6. Borrow checking

Function bodies are lowered to a control-flow graph. A **program point** is a position immediately before or after an operation. A [Place](#34-values-places-and-storage) does not by itself grant write permission. The lowered representation uses these projections:

```text
place := local
       | place '.' Name
       | place '.' TupleIndex
       | '*' place
       | place '[' _ ']'
```

These projections describe storage, including lowered slots, rather than granting direct access to source-level Property storage. [Properties](#88-storage-addressability-and-result-semantics) use accessors. Parentheses preserve a place. Reading a place copies, moves, or borrows according to the required Type and access permissions.

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

#### 9.6.1. Constraints

Type checking generates these constraints:

| Constraint      | Rule                                                         |
| --------------- | ------------------------------------------------------------ |
| Subtyping       | Assignment and argument passing require `type(value) <: type(destination)`. |
| Liveness        | If a value containing `o` may be used after `P`, then `P` belongs to `region(o)`. |
| Outlives        | `a : b` requires `region(a) ⊇ region(b)`.                    |
| Well-formedness | Every Origin in `T` observable through `ref/T from o` or `uniq/T from o` must outlive `o`. |
| Calls           | Origin arguments and result Loan requirements are instantiated as described under Calls and Origin propagation. |

The well-formedness rule prevents borrowed contents from expiring before the outer borrow.

#### 9.6.2. Place overlap and conflicts

Two places overlap when an operation on one may affect the other. Static place analysis uses only these structural rules for proving non-overlap:

| Places | Result |
| --- | --- |
| Identical place, or a place and an inline subpart | Overlap |
| Independent local roots and their inline parts | Disjoint |
| Distinct inline stored fields, Tuple elements, or different constant fixed-array indices of one aggregate, and their subparts | Disjoint |
| Referents of simultaneously live valid `uniq`/`objuniq` borrows with distinct Loan anchors | Disjoint by exclusivity |
| Other reference dereferences | Follow Loan provenance and apply these rules |
| Anything not decided above | Non-overlap unproven; reject operations requiring proof |

Inline parts exclude pointer/reference referents. Distinct shared-reference or raw-pointer variables alone do not prove independence. Constants use language constant evaluation, not optimization; runtime index comparisons such as `i != j` do not establish disjointness. No arbitrary integer proof or optimizer result changes acceptance. Simultaneous exclusive borrows may be used only through their valid access paths; reborrowing still suspends conflicting parent access.

These are storage rules, not permission to bypass Property accessors. Field-scoped standard operations may borrow disjoint fields separately; custom whole-receiver operations retain their whole-instance footprint.

Each operation is checked against every active Loan on an overlapping place:

| Operation               | Existing `ref` | Existing `uniq` |
| ----------------------- | -------------- | --------------- |
| Read                    | Allowed        | Forbidden       |
| Write or move           | Forbidden      | Forbidden       |
| Create `ref`            | Allowed        | Forbidden       |
| Create `uniq`           | Forbidden      | Forbidden       |
| Destroy the borrowed place | Forbidden      | Forbidden       |

This enforces shared aliasing or mutation, but never both simultaneously.

#### 9.6.3. Reborrowing

Borrowing through an exclusive borrow creates a child Loan. While the child is live, the parent remains live but access through it is suspended. Overlapping access is rejected by the normal conflict rules.

**Basic example.**

```kimi
func bump(n: uniq/i32)

var v = 0
bump(v@uniq)
bump(v@uniq)
```

Each call creates a temporary reborrow; the first ends before the second starts.

#### 9.6.4. Calls and origin propagation

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

A call's receiver and argument Loans begin as each borrow/reborrow is formed in evaluation order, before later arguments and defaults. In particular, an exclusive receiver is active while explicit arguments are evaluated. No two-phase reservation exception is defined; intrinsic Exchange/Swap use the same rule.

#### 9.6.5. Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

**Error example.**

```kimi
func bad(x: ref/T) -> ref/T from x
    let local = T.new()
    return ref/local       // Error
```

The local value cannot satisfy the universal return Origin `x`; returning its borrow is a compile-time error.

#### 9.6.6. Destruction lifetime checking

The [Destruction rules](#103-aggregate-destruction-and-deinit) and [Scope Exit](#102-scope-exit-destruction) determine responsibility and order. Destruction lifetime checking applies to every Origin/Loan that Destruction may observe and requires validity at each such observation.

```text
DestructorUsePoints(value, origin) ⊆ region(origin)
```

Destruction that observes no Origin/Loan adds no lifetime requirement. Conservatively assume that every user-defined `deinit` observes all reachable Origins even if its body does not use them, and apply the same checking recursively to field Destruction. No relaxation mechanism is defined.

```kimi
struct Logger origin sink
    let out: uniq/Writer from sink

    deinit
        observe(self.out)
```

Here `observe` accepts `ref/Writer`; reading `self.out` shares the stored capability instead of extracting it. `sink` must remain valid during Destruction, even if the `deinit` body were replaced with `()`.

### 9.7. Initialization-preserving exchange

| Operation | Old value | Placement | Result |
| --- | --- | --- | --- |
| Initialization | None | Fill empty storage | Unit for assignment |
| Replacement (`=`) | Destroy remaining old parts | Place after destruction | Unit |
| `Exchange` | Transfer without destruction | Keep target initialized | Old value |
| `Swap` | Exchange both values without destruction | Keep both initialized | Unit |

`Exchange(place, with: value)` and `Swap(placeA, placeB)` denote language-provided intrinsic exchange operations. Their semantic requirements are defined here; final API spellings and resolution remain separate. In examples, `place` denotes **authorized direct storage access**, not permission to bypass a Property getter or expose its private storage. Property Consume permission alone does not grant such access.

#### 9.7.1. Evaluation and transfer

- Each target is Initialized and permits exclusive writing. Values have identical complete Types, including Origins; conversions finish before exchange begins.
- Evaluate targets and arguments left to right. A target's exclusive Loan begins when its borrow argument is formed and remains active during later argument evaluation and exchange, just as for an ordinary exclusive receiver call. There is no reservation or delayed-activation exception.
- Exchange itself runs no user code, Destruction, Panic-producing work, or control transfer. Internal empty states cannot be observed by the program. This does not guarantee inter-thread atomicity.
- If argument evaluation fails, do not exchange; apply normal temporary cleanup and preserve Origin/Loan dependencies.

`Exchange` secures its replacement first, then transfers the old target value and responsibility to the result and the replacement's responsibility to the target. Preparing the replacement must not empty the target or create a conflicting borrow. `Swap` transfers both values and responsibilities while keeping both targets Initialized and returns Unit; static non-overlap is required.

```text
Before: target = old, replacement = new
After:  target = new, result = old
```

```kimi
// p denotes an authorized writable i32 place.
Exchange(p, with: p + 1) // Error: later read conflicts with the target Loan.
let next = p + 1
Exchange(p, with: next)  // Valid: computed before borrowing the target.

// x is a writable non-Copy owned value.
x = x                   // Valid: RHS-first Move and reinitialization.
Exchange(x, with: x)     // Error: replacement would empty the target.
```

A Type-specific operation with `uniq/Self` may use authorized field exchange to return the old owned field without leaving the receiver incomplete. It must preserve the Type's invariants and all dependencies.

#### 9.7.2. Static non-overlap

Use the structural [place analysis](#962-place-overlap-and-conflicts). Distinct independent roots, distinct inline fields/Tuple elements/constant fixed-array indices, and valid simultaneous exclusive borrows with distinct Loan anchors can prove non-overlap. Identical or containing places overlap. Follow Loan provenance for other dereferences; different raw pointers or shared-reference variables alone prove nothing.

```text
Function parameters: a: uniq/T, b: uniq/T
Swap(referent(a), referent(b)) // Conceptual storage notation: distinct live anchors.
```

Unknown relationships are rejected. Do not accept `Swap(a[i], a[j])` merely from `i != j`, arbitrary integer facts, or optimization. This bounds the required proof and the accepted programs, not just compiler effort.

### 9.8. Captured and erased dependencies

#### 9.8.1. Object payload erasure

Erasing a concrete payload behind a base or runtime-contract view requires its complete data Type to satisfy `Owned`. Recursively check the base, derived fields, and dependencies retained by generic arguments; the handle's own Origin is not this test. Resolve payload Type/Origin arguments and ordinary exclusive-Loan restrictions before erasure. A local object borrow may still have a local Origin when its payload is Owned.

```text
Dog owns only i32/string data -> Owned payload -> base/contract erasure allowed
Dog stores a local ref       -> non-Owned     -> initial erasure rejected
objref/Animal from local     -> borrow remains local even when payload is Owned
```

This is not a blanket `from static` requirement on object handles or exact concrete views. Same-target operations retain existing lifetime rules. An already erased view statically certifies that this check succeeded; later upcasts/casts inherit that guarantee without runtime Origin queries. No erased generic lifetime binding is reconstructed from Runtime Type Identity. Existing borrowed-field Types remain valid; hiding their non-static dependencies needs a later existential-view design. Owned does not waive heap/global escape limits, pointer validity, or concurrency checks.

#### 9.8.2. Closure dependencies and call results

A Closure recursively retains every captured value's Origin and Loan dependencies, not merely the lifetime of its creation Block. Moving an owned value with no borrowed contents does not borrow its old local storage. Copying a shared reference, moving an exclusive reference, reborrowing, or acquiring a borrowed aggregate preserves the corresponding external Origins, child Loans, and parent restrictions. Do not collapse independent dependencies or discard them at generic substitution or type erasure. Owned applies to all reachable environment dependencies.

Distinguish the Closure's captured dependencies from each call's receiver and result dependencies:

| Result source | Contract |
| --- | --- |
| Borrow of environment-owned data | Depends on the current Closure receiver borrow |
| Copied captured external shared reference | Retains its external Origin |
| Reborrow of captured exclusive reference | Also depends on the current exclusive call Loan |
| External reference value moved out by Consuming call | Transfers that reference's external Origin and capability |
| Borrowed argument | Follows the input/result Origin contract |

When the whole result Type is omitted and inferred from the body, infer its Origin and Loan dependencies too. Explicit result annotations and fixed expected signatures retain ordinary result-Origin elision: the hidden environment receiver is not a new source-level `self` or directly written borrow parameter.

```kimi
let text = makeText()
let get = func [text@move] () => text@ref
// Internal signature: call(self: ref/Self) -> ref/string from self.
let view = get()
let moved = get@move // Error if view is still used below.
inspectText(view)

let other = makeText()
let invalid = func [other@move] () -> ref/string => other@ref
// Error: annotated omitted result Origin is static, not the environment borrow.
```

Join multiple results under normal result validation, retaining all possible Loan dependencies when taking an Origin meet. Reject invariant mismatch, cycles, and unexpressible dependencies rather than weakening them. A repeatable exclusive result keeps its call Loan until needed uses end, preventing conflicting reentry. No result may outlive call-local storage or borrow an environment consumed by that call; moving an external reference value out is distinct and may be valid.

The internal call contract is preserved for concrete generic use; callers do not inspect bodies to rediscover lifetimes. Initial common Function Types cannot expose hidden-receiver-dependent results, and initial Callable constraints require an Owned complete result.

#### 9.8.3. Escape and retention

Escape describes retention across a creation/call boundary, not a permanent syntactic Closure category. Check returned values, saved fields, other Closures, and indirect callees against destination lifetime and capability contracts. Borrow capture is not inherently non-escaping, and Move capture does not remove nested borrow dependencies. Moving or heap-allocating a Closure cannot extend a local referent's lifetime. Temporaries retain their original expiration.

Non-escaping means that the callee retains neither the callable nor its environment dependencies beyond the call. It does not mean one call, no allocation, or waived Loan checks. `ref/F` and an Owned result let a verified callback-only body use a borrowed concrete environment without erasure, but neither `ref/F` nor Callable alone promises that every environment-derived value is unsavable. A separately compiled callee cannot be assumed non-escaping without a verified contract. Non-escaping declaration syntax and borrowed erased callable views remain deferred.

Loan validity includes later uses, results, and dependencies observed by destruction, not just the last body invocation or the lexical end of a binding. Heap/global borrow escape remains outside this revision. `Owned` can constrain indefinite retention but grants no thread-safety guarantee.

### 9.9. Lifetime design boundaries

This revision does not define:

- abstract Origin parameters on contracts or trait-like abstractions (the [Property getter receiver/result contracts](#82-default-getter-results) do not introduce contract-level Origin parameters);
- existential object views that hide non-static payload dependencies;
- general higher-ranked Origins beyond the direct-input quantification of Callable constraints;
- borrow escape into heap or global storage; in particular, static Stored Properties are forbidden from retaining safe borrows, including `static` borrows and borrows nested in stored values, under [Property storage rules](#8-properties);
- lending iterators;
- cancellation cleanup guarantees.

These features require extensions to the [Ownership and Origin rules](#9-ownership-and-lifetime-analysis) and must not be inferred from this revision.

## 10. Scope exit and destruction

Scope exit secures results and performs cleanup. A Deferred Block registers code for scope exit; it is unrelated to a Deferred compile-time Condition.

### 10.1. Deferred blocks

A **Deferred Block** registers cleanup when execution reaches `defer`. Registration evaluates none of its body, arguments, conditions, or initializers. The statement has no result; expressions inside retain their positional Evaluation Contexts.

A registration belongs to its directly containing executable scope: function, branch, arm, current iteration, Labeled or Unsafe Block, or executing Deferred Block body. Unreached registrations do not run; each iteration registers and cleans up independently. Registrations cannot be cancelled or manually invoked.

**Basic example.**

```kimi
func process(flag: bool)
    defer: log("function end")
    if flag
        defer: log("branch end")
        work()
    log("after branch")
```

For true `flag`, output is `branch end`, `after branch`, then `function end`. Deferred execution and automatic destruction share the [Scope Exit ordering](#102-scope-exit-destruction).

#### 10.1.1. Deferred control boundary

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

Normal ownership, borrowing, and [Destruction lifetime checking](#966-destruction-lifetime-checking) apply throughout cleanup. Securing a result first does not permit a borrow of a destroyed local to escape. For partial initialization or Partial Move, apply [field cleanup](#1032-field-cleanup) to parts with remaining responsibility rather than skipping the whole aggregate. Raw pointer access does not guarantee automatic tracking of the original owner's destruction responsibility.

#### 10.2.3. Completion and abnormal termination

Consume a Deferred Block's registration when its execution starts. Each registration executes once if cleanup reaches it; an inner defer registers in the executing body's own scope, never in an outer scope already being exited.

Deliver a pending transfer or result only after all required cleanup completes normally. Nonterminating cleanup prevents remaining cleanup and delivery; general termination proofs are not required.

Forced process termination and undefined behavior provide no cleanup guarantee. Panic skips or aborts cleanup under [Panic Termination](#113-panic-termination). Cancellation, if introduced, requires separate common rules for Deferred Blocks, destruction, and secured results; this specification provides no cleanup guarantee for it.

### 10.3. Aggregate destruction and deinit

`deinit` may be declared only directly in a structure body, including a fragment produced by a Source Generator. It is invalid in a group, enum, contract, extension, constructor, function, accessor, or another `deinit`. After conditional selection and merging, each concrete structure has at most one such declaration. A body is required and follows the nonempty executable-Block rule; `deinit` followed by an indented `()` is an explicit no-op body. A no-op body still counts as user-defined `deinit` for Copy and Partial Move restrictions.

The complete declaration is `deinit` followed by that Block. Parameters, generic/Origin parameter lists, result annotations, expression bodies, and access, unsafe, open, virtual, or override modifiers are invalid. Use an Unsafe Block for an unsafe operation inside the body. A structure without `deinit` still receives automatic component cleanup. The declaration is not inherited or overridden: a derived Type may declare its own body, and destruction machinery separately processes every base layer. There is no requirement that a caller have private access to the Type's body in order to destroy a value it legally owns, and no source-level access modifier can suppress mandatory cleanup.

#### 10.3.1. Special receiver

`deinit` is a dedicated destruction declaration with no parameters or explicit result Type. Only Destruction machinery invokes it. Explicit calls, indirect calls, and obtaining its function value are compile-time errors; user-callable finishing work requires a separate API.

Its `self` has exclusive access equivalent to `uniq/Self` for access/Loan checks, but is a special Destruction receiver, not an ordinary borrow value or a second owner. It cannot be Copied or Moved.

The whole receiver cannot be an assignment, Exchange, or Swap target, or be passed as ordinary `uniq/Self`. This includes methods and setters taking the whole receiver exclusively. Whole-self shared borrowing and direct operations/borrows on initialized fields remain subject to ordinary permissions and Partial Move restrictions. No method or borrow may bypass these rules.

The receiver cannot be converted to a new owning value or reference-counted handle, stored for later use, or otherwise escape Destruction. Shared borrows passed to ordinary helper functions must expire before the relevant storage is destroyed. During destruction of a layer `D`, `Self` denotes `D`; derived layers already cleaned up are unavailable. Calls to virtual members on the under-destruction `self` are forbidden, including through a borrowed view passed to a helper; devirtualization cannot waive this language restriction. This prevents a base destructor from reentering a destroyed derived layer; it does not prohibit ordinary calls on independently live field values. Static knowledge of ownership and call effects must establish these conditions; if an unknown callee could expose that receiver or virtually dispatch on it, reject the call. Runtime reference counts or an unchecked assertion cannot supply the missing proof.

```text
During deinit (conceptual storage operations):
    Exchange(self, replacement)       // Error: whole-self replacement.
    self.reset()                     // Error if reset requires uniq/Self.
    observe(sharedBorrow(field))     // Allowed for an initialized field.
    Exchange(field, with: newValue)   // Allowed with authorized field access.
```

#### 10.3.2. Field cleanup

Destroy a complete structure layer by running its own user-defined `deinit`, if present, then, after its body and Scope Exit finish normally, destroying that layer's directly declared Stored Fields in reverse **logical** declaration order. Finally destroy its direct base subobject by the same rule. All fields and the base are Initialized and complete when that layer's `deinit` starts. Ordinary `return` is allowed and does not skip component cleanup. Computed Properties contribute no component, and destruction does not call Property getters or setters. Splitting a Type, changing physical layout, or using a source-generated declaration cannot change this order except through the specified logical declaration order itself.

```kimi
struct ResourcePair
    var first: Resource
    var second: Resource
    deinit
        if skipCustomWork()
            return
        inspect(self.first)
// Either normal exit destroys second, then first.
```

An incomplete structure layer does not run its own `deinit`; destroy its remaining initialized own fields in the same reverse order, then its base subobject if any part of it still carries responsibility. Apply completion and completeness checks separately to each component: a complete field or completed base runs its own `deinit`, even if its containing derived layer never completed construction. A partly constructed base recursively cleans its initialized components without running that unfinished base layer's body. Skip Uninitialized and Moved parts. Field assignment order and subsequent Replacement do not reorder this cleanup.

Tuple elements and array elements are destroyed in decreasing element-index order, from the last logical element to index zero. This includes fixed-length arrays, initialized elements of a partly built array, and owning array storage used by array literals; spare capacity is not an initialized element. Recurse into a partially initialized element before continuing to the preceding element. Unit and empty arrays have no components to destroy. Enum values destroy only the active variant's payload, in reverse payload-declaration order (last positional payload first); inactive variants have no live payload responsibility. These rules define lifetime behavior without adding enum construction syntax or new dynamic Move Paths. Other library containers must define the destruction order of their owned elements in their own contracts.

| Aggregate state | Own deinit | Field destruction |
| --- | --- | --- |
| Complete | Run if declared | All fields afterward |
| Construction not completed | Never run | Initialized fields only |
| Incomplete after Partial Move | Never run | Remaining fields only |

The table applies per structure layer; any base cleanup follows own-field cleanup and uses the base's independent state. For example, a complete `Derived : Base` is destroyed as `Derived.deinit`, Derived's fields in reverse order, `Base.deinit`, Base's fields in reverse order, recursively. If Derived construction is incomplete but Base completed, omit `Derived.deinit` while retaining the remaining Derived-field cleanup and the complete Base cleanup.

Partial Move is forbidden if the aggregate made incomplete or an inline containing ancestor has user-defined `deinit`. The last row does not authorize bypassing that restriction.

```text
Declaration order: a, b, c
States: a = Initialized, b = Moved, c = Initialized
Cleanup: c, then a
```

Destruction lifetime checking applies at every actual observation, including field cleanup. If cleanup reaches a responsibility, execute it exactly once; Move transfers it and prevents double destruction at the source. This guarantee does not promise that every destruction completes when an earlier cleanup diverges, Panics, or terminates execution. [Panic Termination](#113-panic-termination) remains the sole abnormal-termination policy.

#### 10.3.3. Ownership, object release, and reentry

Starting Destruction claims the target's remaining responsibility for the destruction machinery. Until cleanup finishes, the target is under destruction, not an ordinary usable initialized owner or an empty replacement destination. Only the special receiver and authorized field operations may observe its still-live parts. Reject reentrant destruction, whole-value use, and attempts to install another value in that target from cleanup callbacks. After normal completion, that responsibility is gone and any surviving storage is Uninitialized; the operation that owns the storage may then place its secured replacement. This internal transition is not an explicit destroy/reset operation for source code and does not reset a `let` binding's initialization history.

Destroying a complete `owner/T` runs the cleanup for exactly `T`. Destroying `obj/T` destroys its owned object and, when required, releases its original storage after cleanup completes by the corresponding storage mechanism. Destroying an `rc/T` or `arc/T` handle releases one strong ownership reference; only the release that reaches zero destroys the object and performs any required original-storage release. Atomic ownership for `arc` must designate exactly one such release. Object cleanup uses the actual owned Type and its complete derived-to-base chain; metadata retained by any permitted base view cannot discard that identity or allocate a second destruction responsibility. The descriptor selects complete dynamic cleanup, including automatic field and base cleanup, not just a user `deinit`. A missing virtual-destructor modifier cannot omit derived cleanup. Never free an adjusted view pointer using a base Type size; storage ownership retains the original release mechanism. No delayed GC finalizer or separate finalizer thread is implied.

Destroying a non-owning borrow or raw pointer ends that value's capability/lifetime and never destroys its referent. Scalar and other trivial Copy values have no user destruction work; copying them does not create a resource-release obligation. Automatic cleanup, replacement, abandoned construction, temporary expiration, and final object release all use the same recursive rules. If a destructor or component cleanup Panics or diverges, remaining components, base layers, pending replacement, and allocation release do not run; there is no rollback or second cleanup attempt.

### 10.4. Closure and object lifetime boundaries

Destroy initialized captures with remaining responsibility in reverse environment-initialization order. Consumed captures are not destroyed twice; remaining values in a Consuming call use normal Scope Exit. Destroying a captured reference does not destroy its referent. Capture-construction failure follows ordinary temporary, partial-initialization, cleanup, and Panic rules without rollback of completed Moves or effects. Retain dependencies observed by captured destructors.

Construction completes base layers before derived fields under constructor rules. Until the complete object is initialized, do not form/publish its ordinary object views or perform runtime dispatch, type tests, or checked casts on it. Having metadata is not proof of completion. Failure cleans only initialized components with remaining responsibility, including completed base layers; do not call `deinit` for an incomplete layer, and do not promise cleanup on Panic.

During destruction, prohibit new ordinary views, runtime dispatch, type tests, checked casts, and resurrection of that object, including through helper calls. Do not acquire a new owning handle or increment its count to revive it. The special destruction receiver retains its authorized direct field operations and shared value borrows, without conversion into ordinary object views. Base cleanup never dispatches back into an already destroyed derived layer. These restrictions concern the object being constructed/destroyed, not independent live objects used by that code.

The shared destruction rules do not promise eventual release on Panic, divergence, forced termination, or an unbroken reference-count cycle. Weak references, cycle collection, and allocator APIs remain separate designs.

## 11. Failure handling

Failure handling determines whether execution continues with an ordinary value or terminates. Panic interacts with cleanup through its explicit termination rules.

### 11.1. Error policy

Kimigayo represents ordinary failures as values and uses Panic Termination only when normal execution cannot continue. It provides no exception throwing or catching mechanism.

Runtime problems fall into three categories:

| Category | Meaning | Representation |
| --- | --- | --- |
| Recoverable Failure | Expected failure that the caller can handle. | `Option<T>` / `Result<T, E>` |
| Unrecoverable Failure | Normal execution cannot continue. | `$panic(...)` or implicit Panic |
| Warning | Processing can continue successfully, but a condition merits notice. | Diagnostic |

These are not a simple severity ranking: `None` represents expected absence, `Err` an operation failure, Panic process termination, and Warning a diagnostic independent of control flow.

**Partially specified:** Enum payload, construction, and pattern syntax used in the Option/Result examples belongs to those Type and pattern specifications. Example APIs are illustrative.

Dedicated generic failure propagation such as `?` remains undefined. The [require statement](#793-require-statement), including `require condition else return`, is explicit control flow and performs no automatic Option/Result unwrapping or propagation.

### 11.2. Absence and failure as values

Use `Option` for a contract representing normal absence and `Result` for a contract representing failure with a reason. The API contract determines the choice, regardless of whether an individual caller uses the reason.

#### 11.2.1. Option

`Option<T>` represents a value or normal absence without an absence reason.

```kimi
enum Option<T>
    Some(T)
    None

func findUser(id: UserId) -> Option<User>
```

```kimi
match findUser(id)
    Some(user) => use(user)
    None => useDefault()
```

#### 11.2.2. Result

`Result<T, E>` represents success or failure with a reason. `E` is an ordinary Type; no exception object hierarchy is required.

```kimi
enum Result<T, E>
    Ok(T)
    Err(E)

enum FileError
    NotFound
    PermissionDenied
    InvalidData
    IoFailure

func readFile(path: string) -> Result<Data, FileError>
```

```kimi
match readFile(path)
    Ok(data) => process(data)
    Err(FileError.NotFound) => createFile(path)
    Err(error) => report(error)
```

Combine the Types when an API distinguishes normal absence from operation failure:

```kimi
func lookupUser(id: UserId) -> Result<Option<User>, LookupError>
```

Here, `Ok(Some(user))` means a successful lookup with a value, `Ok(None)` normal absence, and `Err(error)` a failed lookup operation.

#### 11.2.3. Handling, propagation, and discarding

Recoverable failures are ordinary values: they neither throw exceptions nor propagate implicitly. Handle them with ordinary control flow such as `match`, and propagate them explicitly with `return`:

```kimi
func loadSize(path: string) -> Result<usize, FileError>
    return match readFile(path)
        Ok(data) => Ok(data.count)
        Err(error) => Err(error)
```

The return Type describes contractual absence or recoverable failure; a function returning `Result` may still Panic on an invariant violation or unrecoverable condition.

Discarding a `Result` expression in Discard Context is allowed but produces a compile-time warning, regardless of its runtime `Ok` / `Err` state. The warning does not change control flow. A caller can explicitly handle both variants with `match` to ignore the outcome intentionally. This section defines no special warning for discarding `Option`.

Returning a recoverable failure follows normal [Scope Exit](#102-scope-exit-destruction) rules, including their requirement that earlier cleanup complete normally before remaining cleanup or result delivery proceeds. Use this path for ordinary failures requiring resource cleanup.

### 11.3. Panic termination

**Panic Termination** abnormally terminates the entire process executing the program when normal execution cannot continue. Explicit requests and implicit runtime check failures share the termination rules below.

#### 11.3.1. Causes and API contracts

Panic is a termination mechanism, not a classification of causes. It may represent a programming defect, such as an invariant violation or unexpected state, or an unrecoverable external condition, such as allocation failure or an unavailable required runtime resource. Bug classification belongs to diagnostics and does not introduce different control flow.

Runtime operations explicitly specified as checked arithmetic, indexing, or conversions initiate implicit Panic on their defined invalid inputs; failed type tests and checked object casts instead follow their Boolean/Option/Result contracts. These include integer overflow, invalid integer division or remainder, invalid indices or Range boundaries, invalid conversions or shift counts, duplicate dictionary keys, and missing keys on indexed reads. Each operation defines its invalid values; IEEE 754 floating-point division by zero is not an integer division failure.

The same cause can instead be recoverable under a different API contract:

**Basic example.**

```kimi
func tryAllocate(size: usize) -> Option<Buffer>

func allocateRequired(size: usize) -> Buffer
    return match tryAllocate(size)
        Some(buffer) => buffer
        None => $panic("Required memory could not be allocated")
```

`tryAllocate` returns absence when allocation is unavailable; `allocateRequired` treats that outcome as fatal.

Failure to allocate storage required by common Function Type erasure follows Panic Termination, independently of whether the source environment acquisition was Copy or Move.

#### 11.3.2. Explicit panic and argument evaluation

`$panic(expression)` is a Composition Root termination operation. Evaluate its argument once under ordinary expression rules with expected Type `string`. On normal completion, use that string as diagnostic information and initiate Panic Termination. If evaluation instead transfers control, diverges, or initiates another Panic, follow that outcome without initiating this call's Panic. Failures while dynamically constructing a message follow the same argument-evaluation rules.

The `$panic(...)` expression has Type Never and never completes normally. Apply the ordinary [Never](#315-unit-and-never-types) and [result validation](#78-result-validation) rules:

```kimi
func requireValue(value: Option<i32>) -> i32
    return match value
        Some(x) => x
        None => $panic("Required value is missing")
```

Panic itself is not a Control Transfer and produces no Completion; distinguish it from a transfer during argument evaluation. The [Evaluation Outcomes](#71-completions) are Completion, Divergence, and Panic Termination.

#### 11.3.3. Termination, diagnostics, and cleanup

Immediate termination applies once Panic Termination begins: do not resume ordinary program execution or perform Scope Exit before terminating the entire process. This rule sets no wall-clock bound on argument evaluation or termination. Panic cannot be caught, recovered from, or resumed, and performs no Stack Unwinding.

Panic does not start Scope Exit processing. If it begins during Scope Exit, abort that processing immediately: do not execute remaining Deferred Blocks, automatic destruction, or the rest of an executing Deferred Block or `deinit`. Completed cleanup effects are not rolled back. Abort pending `return`, `exit`, `continue`, and `yield`; do not deliver secured results or perform additional cleanup to destroy them.

```kimi
func process()
    let resource = makeResource()
    defer: close(resource)
    $panic("Fatal condition")
```

Here, neither the registered `close(resource)` nor scope-exit destruction of `resource` runs.

Panic diagnostics carry a reason and source location: the failed operation for implicit Panic, or the `$panic(...)` call for explicit Panic. A duplicate dictionary key uses the later key expression. The runtime chooses the format and destination and attempts to emit available information; successful or complete output is not guaranteed. Failure to emit diagnostics must not prevent termination.

#### 11.3.4. Checks, builds, and constant evaluation

Panic conditions and termination behavior are identical in Debug and Release builds. An implicit check failure is a language-guaranteed termination operation, not merely a rewrite to a replaceable function call. Replacing `$panic` or diagnostic handling cannot make a Panic return normally.

Invalid operations found during required compile-time constant evaluation are compile-time errors. A runtime operation initiates Panic only if it is actually evaluated and its check fails. Optimization must not introduce Panic from an operation skipped by short-circuit or conditional evaluation. Undefined behavior from an unsafe contract violation is not guaranteed to be detected as Panic.

Language-defined static checks, including literal fitting, apply independently of optimization. Outside required constant-evaluation contexts, knowledge obtained only by constant propagation or folding must not turn a specified runtime Panic into a compile-time error. This also applies when the failing value is statically known.

These rules are independent of implementation mechanisms such as a `trap` instruction. APIs returning failures as values use the contracts above; wrapping or saturating integer arithmetic requires separate explicit library APIs.

### 11.4. Warnings

A Warning is diagnostic information about a condition worth reporting while processing can continue and its result remains usable. Examples include deprecated configuration, ignored optional metadata, fallback encoding, and a failed cache update after the primary operation succeeds.

A Warning does not itself change control flow or implicitly produce `None`, `Err`, or Panic. It is not a third state of `Result`. Return warnings to callers as ordinary values when needed:

```kimi
struct ParseReport<T>
    let value: T
    let warnings: Array<ParseWarning>

func parse(source: string) -> Result<ParseReport<Syntax>, ParseError>
```

This API returns warnings with successful results. An API that must preserve warnings on failure includes them in its error value or in an outer report containing the `Result`.

# Part III. Program and compilation environment

## 12. Modules and dependencies

A module groups source declarations and controls the names exposed through dependencies. Source environments belong to definitions, not to their callers or merged-container wrappers.

| Term | Meaning |
| --- | --- |
| Kotonoha | One named source or binary module. |
| SourceDocument | One immutable source input belonging to a Kotonoha. |
| Compilation root | Entry point for the project root and direct-dependency reference names. |
| Project root | Root of the primary Kotonoha's declaration hierarchy. |
| Source environment | A document's definition-site aliases and lookup context. |

Merged declarations keep each fragment's definition-site source environment for names, Types, Constraints, and diagnostics. Merging must not apply one fragment's aliases to another. Generated documents have independent source environments.

Each Compilation owns a **Compilation root**, direct-dependency reference-name mappings, and default aliases. The primary Kotonoha's Container hierarchy ends at the **project root**. A resolved Symbol identifies its declaration, including originating Kotonoha/version, independently of spelling or alias path.

### 12.1. External references and aliases

Only directly referenced Kotonoha libraries are source-addressable by library name. Use qualification such as `ExternalLib.GroupA.StructB` or an explicit `alias` declaration; do not search all external members unqualified. Multiple library versions may use different reference names, with configuration syntax separately specified. Loading transitive metadata for type checking does not expose those libraries by name.

`alias ExternalLib.GroupA` opens a Container's direct members for unqualified lookup:

- Declare it at top level before ordinary declarations or executable code; it applies only to that SourceDocument. Nested aliases are invalid.
- Resolve its Container path from the Compilation root, without other source aliases or default aliases. Check target accessibility at the declaration.
- Introduce direct Types, functions, Properties, and child Containers in their namespaces; do not recursively introduce descendants.
- Retain a reference to the target Container. Check member access at each actual use, rather than caching one source-wide list of accessible members.
- Treat explicit aliases together at their lookup stage and defaults together at a later stage. Order is irrelevant; deduplicate paths to the same Symbol. Different same-name functions form one candidate set, distinct Types use Type Name Selection, and mixed value kinds conflict.
- Do not automatically re-export source aliases to other files or consumers.

```kimi
// A.kimi; GroupA exports StructB and Child.StructC.
alias ExternalLib.GroupA
group Work
    func accept(value: StructB) -> () => ()
    func nested(value: Child.StructC) -> () => ()

// B.kimi: merged Work does not inherit A.kimi's alias.
group Work
    func reject(value: StructB) -> () => () // Error: not imported here.
```

Import `ExternalLib.GroupA.Child` explicitly to use `StructC` alone; `alias Child` cannot resolve through another alias. Library reference-name configuration is distinct from source `alias`.

**Design boundary:** Versioned dependency reference configuration and reference-graph diagnostics remain separately specified. Re-export syntax follows [Re-exports](#122-re-exports).

### 12.2. Re-exports

**Deferred design:** re-export declaration syntax and artifact representation are not yet defined; source aliases never act as re-exports. Until that feature is specified, a consumer that names a Type or requirement originating in another Kotonoha must directly reference that originating library and have an accessible path to the Symbol. A public Signature referring to an external public Type does not itself create that path. Merely loading transitive metadata does not satisfy Name Reachability.

Any future re-export design must preserve the original Symbol, avoid widening access, and reject cycles without a real target. Its syntax and compatibility requirements belong to that feature's specification.

### 12.3. Source artifacts and binary interfaces

Portable interchange uses source artifacts or binary interfaces, not serialized Koto implementation details. Source artifacts preserve the source and configuration needed for reconstruction. Binary-interface information includes Symbol/version identity, declared access and enclosing domains, visibility and public paths, open/base relationships, normalized Signatures, complete API signature Types and requirements, Constraints and Origin contracts, unsafe requirements, Property/accessor capabilities and field-operation semantics, generic specialization inputs, ABI/layout/calling conventions, and target/language/compiler identity. Private generic-body dependencies retain their defining Symbols and access context without becoming public source names. These are information categories, not a complete compatibility format: encoding, required fields, validation, and compatibility rules belong to a separate artifact-interface specification.

Callable interfaces additionally preserve concrete environment identities, capture dependencies, internal/public call signatures, receiver acquisition contracts, and per-call Origin quantification where needed for verification. Object interfaces retain Supports/conformance, validated virtual/contract entries, Runtime Type Identity, and complete destruction/storage-release information. These requirements do not prescribe an effect-summary encoding or fixed ABI.

## 13. Compile-time directives

Compile-time directives choose source syntax without runtime branching. The following terms are used in this chapter:

| Term | Meaning |
| --- | --- |
| Condition | A compile-time Boolean expression controlling syntax selection. |
| Lookup environment | The declarations, aliases, and extensions visible to name lookup in a scope. |
| Prepared environment | Fixed target values and configured compile-time settings available before source selection. |
| Deferred | A valid Condition dependency whose value is not yet available. |
| Finalization | Acceptance of a declaration, layout, specialization, or body after its required dependencies and checks are resolved. |

Compile-time Directives select Syntax during compilation without producing runtime control flow:

| Form | Purpose |
| --- | --- |
| `#if` | Independently includes or excludes one Syntax node. |
| `#match` | Introduces an ordered Case Group and selects one arm. |
| `#case` | Introduces an arm directly inside a `#match` body. |
| `#Name` | Attaches an Attribute; it is not a Compile-time Directive. |

The former `#If(...)` form is an Attribute. The lowercase `#if` form specified here is a separate language construct.

### 13.1. Syntax and structural selection

`#if` controls either the next Syntax node at the same indentation or one indented Block:

**Basic example.**

```kimi
#if windows
alias Kimi.Windows

#if debug
    let logging = true
    let assertions = true
```

A **Case Group** is introduced by `#match` and consists of the `#case` arms indented one level under it. The group's extent is the `#match` body; nothing outside that body joins the group, so two Case Groups may appear adjacently. Select the first matching arm in source order. The optional catch-all `#case _` must occur once at most, as the final arm.

```kimi
func useImplementation<T>(value: T) -> ()
    #match
        #case windows
            useWindowsImplementation(value)
        #case T is i32
            useIntegerImplementation(value)
        #case _
            useGenericImplementation(value)

    #match
        #case pointerWidth == 64
            useWidePath(value)
        #case _
            useNarrowPath(value)
```

A `#match` header has no subject expression. Its body must contain at least one `#case` arm and contains only such arms, apart from blank lines and comments. Each arm must have an indented Block. A `#case` outside the direct arm list of a `#match` body is an error; nested selections require their own `#match`. Blank lines and comments do not split a group within its body.

A `#match` construct is one Syntax item and may be controlled as a whole by a preceding `#if`. The `#match` wrapper does not itself introduce an additional lookup scope; the selected arm's Block follows the existing scope rules. These rules apply in both executable bodies and Declaration Containers.

Every final evaluation context must select an arm. Without `#case _`, at least one explicit Condition must evaluate to **True** in that context using the specified evaluator. If none is True and a value remains dependent, retain the group until its finalization deadline; if all are False, report an error. The initial language requires no symbolic exhaustiveness proof, enumeration of Types, or Constraint theorem proving. A catch-all supplies an unconditional alternative without such proof.

The selected Block occupies the structural position of the Case Group. Normal Block, result-Type, scope, and control-transfer rules apply after selection. An early-false `#if` target is excluded. Unselected `#case` arms do not undergo ordinary semantic checking or contribute executable code.

The [nonempty Block rule](#721-nonempty-executable-blocks) checks source structure before selection. Removing all executable Syntax does not itself make a Block invalid.

Validation of excluded targets follows [Diagnostics and excluded syntax](#135-diagnostics-and-excluded-syntax).

### 13.2. Condition forms and narrowing

A Condition uses the following closed initial expression set. The whole expression must have Type `bool`; ordinary operator precedence and explicit parentheses apply.

| Form | Rule |
| --- | --- |
| `true`, `false`, integer and string literals | Integers have Type `i64` and must fit its range; a leading `+` or `-` is permitted only directly on an integer literal. Strings use the ordinary literal rules, without interpolation. |
| Compile-time value Name | A built-in Compilation value or an explicitly configured Project setting. |
| `not E`, `E and E`, `E or E` | Boolean operands and the [Condition evaluation rules](#133-condition-evaluation-and-selection). |
| `E == E`, `E != E` | Equal scalar Types (`bool`, `i64`, or `string`); no implicit cross-Type conversion. String comparison is ordinal and case-sensitive. |
| `P is R`, `P is not R` | `P` names a declared generic Core Type or Type Semantics parameter; `R` is a simple primitive Type, a Semantics name, or a simple/qualified Type, Contract, or category Name. Constructed Types and runtime value patterns are outside this initial Condition grammar. |
| `(E)` | Grouping of one permitted expression. |

All other expression forms are invalid Conditions, including calls (even purported compile-time calls), runtime member access, indexing, arithmetic, ordering comparisons, conversions, collections, interpolation, and floating-point/character/null literals. Reject them even in short-circuited operands.

**Condition lookup.** Scalar value lookup searches only the disjoint built-in and Project-setting environment established before parsing; ordinary source `let`/`var` declarations, Properties, aliases to values, and functions are not compile-time values. For `is`, resolve the subject among lexically visible generic parameters, using the nearest declaration first. Resolve Type/Contract/category names using normal Type Name Selection, qualification, source aliases, and then default aliases, restricted to the already established environment of §13.4. This permits unconditional same-Container Types and accessible Contracts in directly referenced libraries, without permitting a Condition to depend on declarations whose availability it controls. Constraint Clauses supply evidence about these parameters, not an additional value namespace. Preserve the definition's source context during specialization.

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

A selected `#if` target adds its Condition to the facts available while analyzing that target. A selected `#case` arm adds its Condition and the negation of every earlier Condition in the same `#match`; `#case _` adds only the earlier negations. These facts are local to the selected target or arm, do not leak into following Syntax or sibling groups, and are not Constraint Clauses. Narrowing preserves the concrete Core Type: `T is Comparable` does not replace `T` with `Comparable`. Use these assumptions only through the [limited proof rules](#445-constraint-proof-system). An assumed disjunction does not expose either alternative, and negated compound conditions are not decomposed through De Morgan. Facts from a target cannot justify the selection that makes that target available.

Compile-time Conditions do not evaluate runtime values. The initial design does not destructure values or introduce pattern bindings. For example, `#case value is ref/i32 x` is invalid; use `#case (s is ref) and (T is i32)` to narrow a value of Type `s/T` to `ref/i32`. Parentheses separate each [requirement expression](#442-requirement-expressions) from the surrounding condition.

### 13.3. Condition evaluation and selection

`#if` and `#match` Conditions use the same evaluation rules. Known target and Project values may determine selection before generic dependencies are bound. Validation must resolve remaining Names without semantically checking excluded controlled Syntax.

After dependency classification, language evaluation has exactly four outcomes:

| Result | Meaning |
| --- | --- |
| **True** | The Condition is satisfied. |
| **False** | The Condition is not satisfied. |
| **Deferred** | The Condition has a valid compile-time dependency whose value is not yet available. |
| **Error** | The Condition is invalid, non-Boolean, or refers to an unavailable Name. |

For a bound requirement test, [Constraint proof](#445-constraint-proof-system) maps Proven to True and Refuted to False. Proof Error maps to Error. Unknown maps to Deferred only if an identified, validated compile-time dependency can resolve it before the required deadline; otherwise report an unproven-requirement Error when evaluation is required. Lack of symbolic proof alone is not Deferred, and is never False. Concrete requirement operands whose atomic judgments are determined evaluate `and`, `or`, and `not` normally; the limited symbolic proof rules do not suppress concrete Boolean evaluation.

After name and dependency validation, a value still dependent on an unbound declared generic parameter produces **Deferred**, while an unknown Name produces **Error**. A requirement already Proven or Refuted from permitted evidence does not remain Deferred merely because its subject is generic. Short-circuit reasoning determines truth, but does not waive validation of any operand in a Condition. **Error** is absorbing for `and` and `or`, regardless of operand order: `Error and X`, `X and Error`, `Error or X`, and `X or Error` are **Error** for every result `X`; `not Error` is **Error**. Otherwise, `false and Deferred` is **False**, `true or Deferred` is **True**, and `true and Deferred`, `false or Deferred`, and `not Deferred` are **Deferred**.

Truth determination and validation are separate. Every reached Condition must be valid, including operands and later arm Conditions whose values cannot affect selection. Unknown Names and invalid operands are errors. A known truth value does not authorize finalization before validation is complete, and does not require obtaining an irrelevant valid dependent value. **Deferred** means a validated compile-time dependency, never an unsupported feature or implementation limitation.

For example, `false and missing`, `true or missing`, and their operand-reversed forms are **Error** if `missing` is unknown. `debug and missing` must diagnose that unknown Name in both Debug and Release configurations. `false and 1` and `true or 1` are **Error** because the numeric operand is non-Boolean.

Evaluation checks the single Condition of a `#if` and every explicit Condition of a Case Group. Every arm Condition is checked, and an **Error** is reported even when an earlier arm determines the selection. Early selection does not waive validation of any explicit arm Condition, including later arms whose values cannot change selection. This requirement concerns Conditions of directives reached by parsing; it does not require parsing directives inside an excluded `#if` target. A Case Group is selected as soon as its first-match result is certain:

- a **False** arm is skipped;
- a **True** arm is selected when every preceding arm is **False**;
- a preceding **Deferred** arm prevents selection of a later **True** arm or `#case _`;
- Conditions after an already selectable **True** arm cannot change the selection.

For example, `#case windows` may resolve during parsing, while `T is i32` remains **Deferred** until `T` is bound.

A still-Deferred Condition is an error when its containing declaration, layout, specialization, or executable body must be finalized. Deferral is valid only when a later compilation phase can provide the missing dependency before that point.

### 13.4. Name-resolution boundary

**Scope lookup environments.** A Condition that changes a scope's lookup environment must have its selection resolved before ordinary Name resolution using that environment begins. Until then, do not begin that resolution. The environment includes declarations and overload candidates, as well as applicable alias and extension imports; a later selection must not add, remove, or replace candidates in an environment already in use.

Resolve and evaluate such Conditions using an already established environment independent of the conditional declarations in the affected scope. Condition names may be resolved in that independent environment before ordinary Name resolution begins. A Condition must not depend on a declaration whose availability it controls, directly or through a cycle.

This boundary applies per scope, not once to the entire program. Conditions that select only expressions or statements without changing a lookup environment may remain Deferred until specialization. A selected branch may also contain local declarations if Name resolution using that branch's environment starts only after selection. Follow normal scope rules: a declaration introduced into an enclosing scope must be selected before resolution using that enclosing environment begins. A directive does not create an extra scope merely to defer this requirement.

| Controlled Syntax | Required selection point |
| --- | --- |
| Declarations or imports that change an enclosing lookup environment | Before ordinary Name resolution using that environment begins. |
| Expressions and statements that do not change a lookup environment | May wait for specialization, subject to the finalization deadline. |
| Local declarations inside a branch first analyzed after specialization | Before ordinary Name resolution using the selected branch's environment begins. |

```kimi
#if windows
func Test() -> () => ()
```

The target setting selects whether `Test` is present before Name resolution using its containing environment begins.

**Boundary example.**

```kimi
func kind<T>() -> i32
    #match
        #case T is i32
            return 32
        #case _
            return 0

func example<T>() -> i32
    #match
        #case T is i32
            let result = 32
            return result
        #case _
            return 0
```

Both functions may defer selection until `T` is known. In `example`, select the branch and establish its local declarations before resolving `result`. This does not change an enclosing lookup environment that has already been used.

Scopes with established environments may proceed independently. An affected scope must wait if later Binding or specialization can establish its environment; otherwise, diagnose the unresolved dependency when that scope must be analyzed or finalized. Never begin with a provisional candidate set and revise resolved Names later. This rule fixes conditional membership in the lookup environment; ordinary declaration-order visibility rules still apply.

### 13.5. Diagnostics and excluded syntax

**Checks on excluded Syntax.** Every SourceDocument is tokenized, so encoding, token validity, and indentation errors are always diagnosed. An early-False `#if` target is scanned for balanced Block structure, required executable bodies, and the structural placement/nonemptiness rules of `#match` and `#case`. Its ordinary expression/declaration grammar is not checked; for example, an incomplete initializer in that target is permitted. A target parsed while its Condition is pending, and every `#match` arm body, undergo ordinary parsing before selection; later exclusion does not retract parse diagnostics.

| Check | Early-False `#if` target | Already parsed target or unselected `#match` arm body |
| --- | --- | --- |
| Tokenization and indentation | Required | Required |
| Block/directive structure and source-level nonempty executable bodies | Required | Required |
| Ordinary expression/declaration grammar | Skipped | Required during parsing |
| Nested Directive Condition evaluation | Skipped | Performed only as reached during parsing; remaining obligations in an excluded body need no Binding |
| Ordinary Name/Type/ownership checks, lowering and code generation after exclusion | Skipped | Skipped |

The controlling `#if` Condition and all explicit Conditions of the current `#match` are checked independently of their targets under [Condition evaluation](#133-condition-evaluation-and-selection). A non-reserved `#Name` denotes an Attribute, not an unknown directive; resolving that Attribute is not required in excluded Syntax.

**Error example.**

```kimi
#if false and 1
    useFeature()
```

The numeric operand is not Boolean. Short-circuit truth does not waive validation, so the Condition is a compile-time error.

## 14. Compilation model

A Compilation processes one Project for fixed source, dependency, target, and configuration inputs. The source-language rules determine meaning; this chapter defines compilation invariants with implementation requirements and reference algorithms in separate appendices.

### 14.1. Build units

The build model separates workspace orchestration, project configuration, source modules, and target compilation:

| Element | Responsibility |
| ------- | -------------- |
| Solution | Holds multiple Projects and supplies options shared by their builds. |
| Project | Defines one application or library build unit. It is configured by a `.kimiproj` file. |
| Kotonoha | Defines a named module unit for an application or library, built from one or more SourceDocuments. |
| SourceDocument | An immutable source snapshot, including its path and text. Replacing its text creates a new snapshot. |
| Compilation | Compiles one Project under one fixed set of source, dependency, target, and build inputs. |
| Compilation root | The lookup entry point for the project root and direct-dependency reference names. |
| project root | The root of the primary Kotonoha's declaration hierarchy. |

A Solution discovers and loads Projects. A Project stores target triples, aliases, and external Kotonoha descriptors, and creates one Compilation for each target.

### 14.2. Build inputs

Compilation inputs include the complete target triple (including ABI/environment), backend and target layout, build mode and code-affecting options, Project compile-time settings, language/compiler version, source snapshots, and resolved dependency versions and interfaces. Reuse analysis or artifacts only when all relevant inputs agree; OS and architecture alone are not a cache key. Changing inputs requires a fresh analysis. Artifact cache formats are separately specified.

### 14.3. Target preparation

Each Compilation owns the primary Kotonoha and provides target information and compile-time variables.

A target must provide the Kimigayo data-layout and ABI facts required by the language. Backend-specific representations are implementation details.

### 14.4. Compile-time values

Prepared Compilations provide these immutable-for-analysis scalar values:

| Name | Type | Value |
| --- | --- | --- |
| `os` | `string` | Canonical lower-case OS family: `windows` for Win32, `macos` for MacOSX, `linux` for Linux; other recognized families use their lower-case target-family name, and an unrecognized OS uses `unknown`. Version suffixes are excluded. |
| `arch` | `string` | Canonical lower-case architecture family, such as `x86`, `x86_64`, `aarch64`, or `riscv64`; target aliases for the same family give the same value. |
| `windows`, `linux`, `macos` | `bool` | Exactly the respective comparisons `os == "windows"`, `os == "linux"`, and `os == "macos"`. At most one is true; all are false for other OS families. |
| `debug`, `release` | `bool` | The selected build mode and its negation: `release == not debug`. |
| `pointerWidth` | `i64` | Default raw-pointer width in bits from the prepared target layout; supported values in this revision are 16, 32, and 64. |

Project compile-time settings have explicit `bool`, `i64`, or `string` values. Their Names must be valid identifiers and must not collide with reserved words or built-in Compilation values; collisions are errors rather than an override order. They are copied into the prepared environment before parsing. The `.kimiproj` representation is `CompileTimeSettings`, a Name-to-setting map whose entries specify exactly one of `Bool`, `Integer`, or `String`.

### 14.5. Language-version selection

An optional `.kimiproj` `LangVersion` requests an exact supported language version. If omitted, use the solution's version when supplied, otherwise the compiler's current version. Unsupported requests are errors, never silent fallback. Record the effective language version and compiler build identity in build metadata. This setting does not promise compatibility with older compilers bearing the same pre-alpha version label.

### 14.6. Structure layout and ABI

The compiler derives physical layout from the selected storage declarations, their Types, the target, and the applicable layout mode. Default layout must be reproducible for identical build inputs, compiler, and configuration, but need not use logical declaration order for physical offsets. It must preserve the observable initialization order defined under [Initialization](#86-initialization). Ordinary structs do not guarantee a stable ABI across source or toolchain changes. An explicit fixed-layout facility for FFI or other binary interfaces is specified separately; default layout must not be treated as that facility.

Layout includes exactly one direct base subobject plus the structure's own Stored Fields. The recursively embedded owned-value storage graph must be finite; reject cycles through inline base/field components that require infinite layout. Object handles, borrows, and raw pointers do not inline their referents and therefore do not create such layout edges. Physical reordering cannot alter field identity, initialization/completeness tracking, or the prescribed derived-to-base and reverse-component destruction order.

### 14.7. Compilation invariants

Compilation must respect semantic dependencies; it need not use one whole-program pass per stage. [The reference compilation models](#appendix-b-non-normative-reference-models) illustrate valid arrangements. Parsing may select known directives early. Condition validation and specialization recur per affected scope under [staged evaluation](#134-name-resolution-boundary); establish that scope's lookup environment before using it. Analyses may share facts, but unresolved obligations must not be treated as successful finalization.

### 14.8. Object metadata

#### 14.8.1. Type identity and descriptors

Every live object can reach immutable metadata for its Dynamic Type, shareable among objects of that concrete Type. Required logical information is:

```text
Type Descriptor
    ├─ Runtime Type Identity
    ├─ base relationships and runtime-contract conformance for Supports
    ├─ virtual/contract requirement -> effective verified implementation
    ├─ complete dynamic destruction operation
    └─ layout or receiver-adjustment information where required
```

Runtime Type Identity comes from the normalized concrete Core Type: declaration Symbol and Kotonoha/version, plus generic arguments in declared order and kind. Preserve nested Types, Semantics, and compile-time value arguments that distinguish the Type. Exclude the handle's outer `obj/rc/arc/objref/objuniq` and all Origin bindings. Different aliases normalize to the same Type; equal names, layouts, member sets, or descriptor addresses alone do not define identity. Runtime identity never establishes static lifetime compatibility.

Conformance is registered by the concrete Type's definition and fixed when metadata is generated. No unrelated extension, module search, or runtime registration changes it. Artifacts for one Type must agree on identity, bases, conformance, and effective implementation mapping; reject disagreement at build/link time rather than using load order. An entry must satisfy the declaration and ObjectCompatible guarantees; receiver adjustment cannot bypass access, Type, Origin, or Loan checks. A destruction entry does not make `deinit` a source-level function value.

#### 14.8.2. Representation and storage responsibilities

Each view must reach the same original object's type identity, the receiver needed by its implementation, a valid adjusted cast result, and preserved Origins/Loans. Owning views must also reach complete destruction and original storage-release information.

Reference counts and allocator state belong to instances/allocations, not the shared immutable Type Descriptor. Logical roles may be co-located physically. A vtable is one dispatch structure, not all metadata. No base offset zero, equal pointer values between views, one-word borrow, fixed table/slot layout, descriptor representation, calling convention, stable ABI, FFI layout, or dynamic-loading compatibility is promised.

Optimization may share, normalize, omit, or directly resolve metadata only while preserving Type tests, implementation selection, evaluation order/count, effects, failure, Origins/Loans, and destruction. It must not remove information needed by separately compiled consumers. Source legality cannot depend on allocation elimination or devirtualization.

# Appendices

## Appendix A. Compiler implementation requirements

**Normative.** These requirements preserve information and invariants needed by the language rules. They do not add user-visible syntax or change failure behavior. Implementation-specific representations are distinct from the reference algorithms below.

| Term | Meaning |
| --- | --- |
| Koto / Koto tree | Parsed syntax nodes with source contexts and parent/child relationships; not a bound program or binary interface. |
| CodeContext | Source-local lookup and diagnostic context for one immutable source snapshot. |
| Directive Binding | Resolving and validating compile-time Condition names and dependencies. |
| Validation obligation | Information retained until a required check can be completed. |
| Finalization | The point when a declaration, layout, specialization, or executable body is accepted for subsequent compilation; required checks must be resolved. |
| Lowering | Translating checked source operations into lower-level representations while preserving semantics. |

### A.1. Source identity and incremental analysis

A source-derived CodeContext belongs to one Kotonoha and one immutable SourceDocument snapshot. A different snapshot at the same path requires a fresh context and fresh alias/Binding results; path equality does not establish revision identity. Source-less parsing entry points may create such contexts but must not themselves become the source identity of parsed nodes. Nodes cannot be inserted into another Kotonoha's Declaration Container. See [source contexts and dependencies](#12-modules-and-dependencies).

Each source syntax node and declaration fragment retains its original CodeContext. After Container merging, resolve member bodies, headers, Type annotations, and Constraints using their own source contexts, not a single context attached to the merged Container. Preserve fragment locations for diagnostics and header checks. Source-less roots and generated wrapper nodes do not represent a source alias environment; moving source syntax into them preserves its origin context. Generated source documents have their own contexts.

Lowering may place top-level executable syntax in an implicit generated function owned by the Kotonoha, but must preserve source scopes and CodeContexts.

The internal name `MacroKoto` does not define language semantics; `$` is the Composition Root.

A Kotonoha tokenizes and parses each `SourceDocument`, merging declarations into one root Koto tree. Root executable syntax is placed as described under [root and nested containers](#421-root-and-nested-containers).

### A.2. Directive representation and validation obligations

The Parser represents directives explicitly rather than evaluating them as Attributes:

```text
CompileTimeIfKoto
    Condition
    Target

CompileTimeMatchKoto
    CompileTimeCaseArmKoto[]
        Condition or fallback
        Block
```

A directive whose selection still awaits validation or a dependent value retains a directive Koto node. An early-true `#if` contributes its Target directly; an early-false one contributes none. Independently, the enclosing scope retains pending Condition validation obligations even when the directive Koto or unselected Syntax is discarded. Each obligation retains the full Condition and its original CodeContext and identifies the enclosing scope for Directive Binding. These obligations are separate from executable syntax and must not cause excluded targets to undergo ordinary Binding. Invalid Case Groups may remain for error recovery. Resolving a specialization must not mutate Koto shared with others.

Retain a Condition with unresolved Names or unvalidated dependencies even when early evaluation determines True or False. Its validation obligation preserves the source, diagnostic context, and enclosing lookup scope until Directive Binding can complete the [Condition validation](#133-condition-evaluation-and-selection). An early truth result does not discharge that obligation.

An implementation must not label an unimplemented language feature **Deferred**; it must either expose a distinct pending Binding obligation or report an implementation limitation.

### A.3. Binding, caches, and incremental validity

Binding must preserve these semantic stages, without requiring a single-pass implementation:

```text
Parse per source and collect fragments/generator output
-> select directives in established environments
-> collect selected declarations, root and scope tables, and alias targets
-> bind headers, Types, Signatures, and Constraints; validate merges/duplicates
-> resolve bodies -> test candidates -> select -> check usage
-> retain Symbol references and the selected operation plan for lowering
```

Keep Type/Value and Origin/Label tables separate. A Lookup Context includes source context, scope, namespace, role, and accessibility. Preserve resolved Symbol, function-group, deferred, and error outcomes. Lowering must not re-resolve strings. Share normalized Types, but isolate candidate type variables, constraints, argument mappings, tentative bindings, adaptation plans, and rejection reasons. Commit only the selected candidate. Cheap filters may precede inference only if lookup stopping is unchanged. Pairwise comparison may require O(n²) comparisons; type-comparison cost is additional.

Cache only context-independent results or include every relevant dependency:

| Cache | Required distinctions |
| --- | --- |
| Lookup | Scope, name, namespace, role, lexical visibility, source alias environment |
| Accessible lookup | Also use-site Kotonoha, Container relationship, and inheritance/access domains |
| Member lookup | Target Symbol/Type, type arguments, static/instance use, protected receiver Type, extensions |
| Applicability | Candidate, argument Effective Types/literal values/labels/forms, expected Type, type arguments and constraints |
| Conditional work | Compilation target, selection state, specialization |

Do not reuse a role-filtered lookup for a different role, or source-wide access results across unrelated Containers. Invalidate affected caches when sources, dependencies, aliases, selections, or Symbols change. Flow-dependent Loan and initialization state belongs to Usage Legality, not overload-selection cache keys. Refinement changes the input Effective Type and therefore must be reflected in member/applicability cache keys.

Validation must cover source-context isolation, merged private access, duplicate/role/arity boundaries, Type/Value paths, aliases, extension precedence, expected results, incomparable candidates, nested inference, specialization environments, and no fallback after usage failure. Reordering load, parse, generator completion, or candidate enumeration must preserve results; parsing examples alone does not validate Binding.

Validate effective access-domain inclusion for every exposed API component, recursively through constructed Types and constraints, after merging and inference. Preserve deferred associated-type obligations. Cover private Types used by internal APIs, public members in restricted Containers, private type arguments at generic uses, and definition-site private helpers during external specialization. Inheritance access validation must cover sealed-base rejection, open-fragment agreement, base accessibility, protected receiver restrictions, both compound access forms, and the cross-Kotonoha override exception. Access-cache validity must account for base-graph and modifier changes, independently of the source alias environment.

### A.4. Property implementation requirements

Preserve Stored representation, standard-accessor markers, accessor accessibility, and field-scoped semantics through Binding and separate-compilation interfaces. Retain the abstract Type/storage information needed to validate Consume without exposing physical layout. Runtime state must not select between accessor implementations.

Check declarations, paths, initialization/completeness, Loans, Origins, and destruction responsibility before lowering. Consume is a direct value/state transfer, not a user function call.

### A.5. Raw pointer backend requirements

`ptr` is not a Primitive Type. LLVM `ptr` is a backend representation; instructions supply the Types needed for memory access and arithmetic. Lowering must preserve this specification and use properties such as `inbounds` only when their premises hold. Language undefined behavior and LLVM poison are distinct concepts.

### A.6. Literal representation

The syntax tree canonicalizes spelling: integers render as signed 128-bit decimal values; floating-point values use round-trip `f64` notation with a decimal marker when needed (for example, `1.0`).

### A.7. Cleanup analysis and lowering

Analyze registration separately from execution: a non-completing deferred body affects actual cleanup paths, not reachability immediately after registration. Lower defers and destruction into one exit sequence, retaining registration state only as needed. No dynamic closure, function value, or heap cleanup stack is required.

For `if condition` containing only `defer: cleanup()`, a true branch registers and runs cleanup before leaving that branch; false registers nothing. No registration flag is needed in this simple case, and Lowering must not move cleanup into the surrounding scope. Example cleanup functions are illustrative, not standard API declarations.

Retain constructor selection, declaration-initializer source environments, per-layer construction completion, per-component initialization/responsibility, and the active Destruction receiver's exact layer. Cleanup plans must include the base as the last component after own fields; a completed base retains its own destructor even when derived construction is abandoned. Store enough interface information to reproduce constructor access/signatures, destructor presence, logical component order, and the exact object's destruction chain across separate compilation. Do not infer these facts from current physical offsets or from whether a destructor body appears empty.

Validation must cover duplicate and conditionally excluded `deinit` declarations across fragments, forbidden placements/modifiers/calls, implicit-constructor suppression, base-constructor accessibility, successful-exit checks before and after constructor cleanup, partial Tuple/array construction, reverse element and base order, no getter/setter invocation by destruction, and skipped Moved components. Include replacement after Partial Move, interruption during old-value cleanup, constructor/destructor receiver escape, base slicing/replacement rejection, and exactly one object cleanup on the final owning-reference release. These are semantic obligations, not guarantees supplied by successful parsing alone.

### A.8. Callable and object verification

Preserve selected capture bindings, acquisition order/effects, mutable-binding flags, nested capture dependencies, environment Type identities, and all captured/call/result Origin and Loan relationships through Binding, specialization, lowering, and artifacts. Do not finalize unresolved Copy-required capture, minimum receiver, Callable conformance, or common-type erasure obligations. Verify concrete Copy separately from receiver kind and erased-container classification.

Verify ObjectCompatible at each specified declaration/use boundary, including separately compiled/indirect callees and inherited contract overrides. Metadata must agree with verified effective implementations and full object identity/cleanup. Unknown effects or artifact disagreement cannot supply proof.

Conformance tests must distinguish capture versus call-time acquisition, unused explicit captures, per-boundary nested captures, `let`/`var`, reference versus referent lifetime, Copy-consuming versus Move-consuming calls, all Callable receiver kinds, per-call Origin quantification, variant Loan handling in casts, inherited overrides, and construction/destruction restrictions. Parsing alone does not establish any of these guarantees.

### A.9. Refinement and require verification

Retain `require` as a statement with its condition, explicit failure body, body form, and source layout; do not lower it into a Selection Boundary before resolving transfers. Distinguish compile-time Requirement Tests from runtime `is` by syntax context. Check the failure body's non-continuation independently of condition truth, including nested caught transfers and real cleanup paths.

Retain Binding Identity, Value Instance, Effective Type, and its validity conditions; coordinate refinement with initialization, Loans, transfers, cleanup, and conservative reachability. Overload/member caches must distinguish Effective Types. Do not transfer facts through old aliases, Boolean variables, later defer registration states, or Function Boundaries. Validate short-circuit and loop joins independently of analysis order.

Cover same/next-line `else`, comments, multiline conditions, missing or empty bodies, forbidden expression placement, both `is` contexts, single evaluation, trivial tests, invalid targets, early transfers, contradictory facts, Move/Replace invalidation, surviving member mutation, alias versus new-binding typing, `require true` failure validation, `require false` successor checks, and unchanged acceptance under optimization.

## Appendix B. Non-normative reference models

**Non-normative.** These algorithms illustrate valid implementation strategies. A different strategy must preserve all language rules and Compiler requirements, including validation of excluded syntax and immutable lookup decisions.

### B.1. Build pipeline reference model

**Non-normative reference model.** The required dependencies and invariants remain normative; pass boundaries and scheduling are implementation choices.

The logical compilation pipeline, including stages not yet implemented, is:

```text
Solution -> Project -> Compilation(inputs)
    -> SourceDocuments -> Tokenization -> Parsing / Koto tree
    -> Directive Binding and selection of lookup environments
    -> Declaration and Name Binding, Type checking and overload resolution
    -> Required generic specialization and remaining directive selection
    -> Control-flow, ownership, lifetime and Origin analysis
    -> Lowering -> backend IR -> binary
```

### B.2. Directive processing sequence

**Non-normative reference model.** This sequence is one way to preserve the required selection, excluded-syntax, and validation behavior. The preceding Compiler requirements and language rules remain mandatory.

The evaluation and Syntax-processing sequence is:

```text
Parse a directive Condition
    -> evaluate known target and Project values
        -> independently retain the Condition and its context if validation requires Directive Binding
        -> True: parse the controlled Syntax without a directive Koto
        -> False: consume the controlled Syntax without creating Koto nodes
        -> unresolved validation or Deferred value: parse the controlled Syntax and retain a directive Koto
        -> Error: report a diagnostic and discard the controlled Syntax
    -> resolve Names and validate operands in all retained Conditions, including early-True/False Conditions and Conditions of unselected case arms
    -> re-evaluate after generic Binding and for each specialization
    -> resolve selections that change a scope's lookup environment before ordinary Name resolution using that environment begins
    -> require a final result and completed validation before finalization
    -> bind and lower only the selected Syntax
```

### B.3. Borrow checking reference algorithm

**Non-normative reference model.** Implementations may use another algorithm that preserves the Ownership and Origin rules.

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

### B.4. Callable and object implementation strategies

**Non-normative.** A Closure may lower to an environment plus a call entry. Keep logical capture initialization/destruction order independently of physical field layout. Common Function Types can use inline or allocated storage with call and destruction entries; allocation removal is an optimization, not a typing rule.

ObjectCompatible can be proven by effects over receiver/base/field paths, callee summaries, finite monotone fixed points for recursion, and generic Access Effect obligations. Separate compilation may preserve verified summaries, bodies, or generated object entries; no particular summary format or whole-program analysis is mandatory. Helpers can use verified object entries rather than creating unrestricted ordinary exclusive receivers.

```text
one selected method/accessor body
    ├─ ordinary value entry
    └─ verified object entry or receiver-adjusting adapter

Dog descriptor
    ├─ Animal virtual slot -> Dog override entry
    └─ Speaker requirement -> verified conformance entry
```

Descriptor sharing/canonicalization may permit address comparison when it preserves language identity; distinct physical descriptors may still denote one Runtime Type Identity.

### B.5. Type relation and operation plans

**Non-normative.** A Binder can map the [normative relation table](#39-type-relations-and-expression-operations) to separate APIs such as the following. Names and signatures are illustrative, not language requirements.

| Illustrative API | Responsibility |
| --- | --- |
| `IsIdenticalType(A, B)` | Compare normalized complete Types, including bound Origin identity. |
| `IsSubtype(A, B, constraints)` | Prove static fitting without inserting value operations. |
| `IsExpectedResultCompatible(A, B, constraints)` | Apply only the static relation permitted during candidate result filtering. |
| `CanImplicitlyAdapt(expression, target, context)` | Select an adaptation permitted in that use-site context. |
| `CanExplicitlyAdapt(expression, target, context)` | Select one operation admitted by the resolved explicit target. |
| `CanAcquire(expression, access, state)` | Check the selected access against Place, initialization, ownership, and Loan state. |

Despite the illustrative `Can` names, adaptation and acquisition checks benefit from returning a plan rather than only a Boolean. A plan can retain the selected operation, complete result Type, Origin constraints, required access, Copy/Move and Loan effects, and any deferred generic obligations. Static relation checks can likewise distinguish proven, rejected, and unresolved constraints. Keep candidate plans isolated from committed program state. Once selection is complete, validate and lower the selected plan in evaluation order rather than repeating operation selection with different rules. Defined runtime checks, such as numeric range checks, remain part of the operation rather than reasons to search for another conversion.

## Appendix C. Implementation status

Implementation coverage is informative and does not weaken language rules or Compiler requirements. All implementation-progress notes are centralized here; `planned` in a retained snapshot means implementation work, not permission to use an undesigned feature.

### C.1. Coverage summary

This table defines the recorded status of each compiler stage. The notes below describe covered cases and specific remaining work; they do not assign a separate stage status. Parsing alone does not guarantee execution. **Implemented** and **Partial** are limited to the coverage described below. **Not implemented** means the recorded stage is unavailable; it does not imply that all related design details are settled; **Not assessed** means the recorded notes do not establish that stage's coverage. **N/A** means the feature has no such stage.

| Feature | Parsing | Binding | Analysis | Lowering | Runtime |
| --- | --- | --- | --- | --- | --- |
| Functions and Constraint Clauses | Partial | Not implemented | Partial | Not implemented | Not implemented |
| `#if` / `#match` | Implemented | Not implemented | Partial | Not assessed | N/A |
| Types | Partial | Not implemented | Partial | Not implemented | Not implemented |
| Origins | Partial | Not implemented | Not implemented | Not implemented | Not implemented |
| Properties | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| Constructors and aggregate destruction | Not assessed | Not implemented | Partial | Not implemented | Not implemented |
| `@Type` | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| `@move` / Consume | Not implemented | Not implemented | Not implemented | Not implemented | Not implemented |
| Control flow and `defer` | Implemented | Not implemented | Partial | Not implemented | Not implemented |
| Generic specialization | Not assessed | Not implemented | Not implemented | Not implemented | Not implemented |
| Option / Result / Panic | Not assessed | Not implemented | Not implemented | Not implemented | Not implemented |

Build inputs, source snapshots, strings, generated sources, and backend restrictions are described in the detailed notes. A stage marked Implemented does not certify a fully checked or executable program. A rule's design status is listed separately under Deferred features.

The integrated Closure/capture/Callable model, runtime `is` and refinement, `require`, and Object Type System are language specifications, not implementation completion claims. Their complete parser, Binding, ownership, lowering, and runtime coverage has not been assessed here; existing broad coverage rows do not certify these additions.

### C.2. Builds, modules, and source artifacts

**Current implementation status:** project loading, target preparation, tokenization, parsing, early directive selection, and partial control-flow/type analysis are implemented. The current LLVM backend preparation requires a supported pointer width and LLVM data-layout string. The current `Build` API reports front-end checks only; it does not certify a finalized program or produce a binary.

Loading external Kotonoha libraries is not yet implemented.

The recorded compiler supports only its current language version. Exact version selection, fallback defaults, rejection of unsupported versions, and build metadata are specified.

The current source-snapshot serialization/reparse facility is not a [binary interface](#123-source-artifacts-and-binary-interfaces).

### C.3. Lexical forms and types

The current front end parses escaped strings, raw strings, and string interpolation, including nested expressions. Escape sequences are validated during parsing; evaluating interpolated strings is deferred to later compilation stages.

Compile-time basic-value evaluation currently supports integer representations fitting `i64` and all valid `f64` literals.

The front end parses recursive Semantics prefixes, distinct grouped and Tuple Types, and independently annotated inner Origins, preserving them through writing and source serialization. Type resolution, layout validation, subtyping, ownership rules, and most Type semantics remain unimplemented; parsing a nested Type or storage-borrow target does not establish its semantic legality. Syntax-level control-flow facts retain supported nested pointer Types and leave unresolved reference/Origin checks pending.

Body parsing for `enum` and `extension` is not implemented.

Split-structure integration, generated-source integration, and layout generation are planned, not implemented.

Inheritance access domains, recursive API signature accessibility, and protected-receiver checks are specified but not implemented by general Binding. Lexing has modifier token kinds, which does not establish complete parsing or semantic support for `open struct`, base clauses, compound access, or overrides. Inheritance parsing coverage is not assessed here. Inherited lookup and virtual/override declaration spellings retain their syntax boundaries. Object dispatch, metadata, upcasts, casts, compatibility, base lifetime, and Copy rules are specified; this documentation integration does not establish their implementation.

The Parser supports Origin lists on structures and functions, simple and qualified annotations, intersections, and named arguments. Origin name resolution, inference, variance analysis, and borrow checking are not implemented.

### C.4. Declarations and compile-time directives

The current Parser stores leading Constraint Clauses separately from executable body items and preserves deferred directives on them. It checks clause subjects against the declared generic parameters and diagnoses clauses placed after executable items. Semantic validation of Constraints during Binding and specialization is not implemented.

**Current implementation status:** the Parser validates the closed Condition expression set, evaluates known scalar operations, and propagates Errors before short-circuit truth results. Its internal **Pending** result represents an attempt awaiting Name/requirement Binding; it is not the language result **Deferred**. Pending directives retain dedicated Koto nodes, and validation obligations survive early selection. Control-flow analysis exposes encountered obligations as pending Binding. Unknown-Name classification by later Directive Binding, `is` evaluation, specialization, lookup-environment enforcement, and constraint narrowing remain planned.

### C.5. Properties

The Parser records Properties, inline and block accessors, explicit getter result annotations, and basic syntax errors. Control-flow analysis checks known accessor result Types. Accessor expansion, contextual binding of `self`, `storage`, and `value`, storage classification, access and initialization checks, general accessor type checking, Property Consume, and field-scoped standard operations are not implemented; successful parsing alone does not validate them.

### C.6. Expressions and operators

The Parser supports `@Type` precedence, left associativity, and generic/comparison boundaries. Treating a `@move` operand as a Type does not implement a lifetime operation; its stage status is listed in the coverage table. Basic expressions, argument labels/defaults, collections, and anonymous functions have syntax-tree support. Member-name restrictions still need additional validation.

General type inference, overload and argument matching, function-value execution, numeric checks, dictionary duplicate detection, evaluation order during execution, and single-access Property updates require semantic analysis and runtime implementation. Panic name/type validation, diagnostics, and common termination handling are also planned. Control-flow and cleanup coverage is detailed below. Examples using application-specific functions or Types illustrate semantics rather than promise standard-library APIs.

Implementation references:

- [Parser.cs](Kimi/Compiler/Parsing/Parser.cs) and [expression Koto nodes](Kimi/Compiler/Parsing/Koto/Expressions): syntax and precedence.
- [ControlFlowAnalysis.cs](Kimi/Compiler/Analysis/ControlFlowAnalysis.cs): results, transfers, short-circuit paths, and partial unsafe checks.
- [ExpressionPrecedenceTest.cs](xUnitTest/Tests/ExpressionPrecedenceTest.cs), [ParserRegressionTest.cs](xUnitTest/Tests/ParserRegressionTest.cs), and [SpecConformanceParseTest.cs](xUnitTest/Tests/SpecConformanceParseTest.cs): grouping, diagnostics, generic boundaries, and expression syntax.
- [CollectionLiteralParseTest.cs](xUnitTest/Tests/CollectionLiteralParseTest.cs) and [RangeIndexParseTest.cs](xUnitTest/Tests/RangeIndexParseTest.cs): collection and boundary syntax.
- [ControlFlowAnalysisTest.cs](xUnitTest/Tests/ControlFlowAnalysisTest.cs) and [ControlFlowRevisionParseTest.cs](xUnitTest/Tests/ControlFlowRevisionParseTest.cs): control constructs and result checks.

### C.7. Control flow and failure handling

The constructor/destruction coverage row includes only the existing analysis of executable-body control flow. Constructor selection and synthesis, completion checks, general destructor declaration validation, per-component/base cleanup, reentry prevention, and final object release require semantic and runtime implementation. Complete parsing coverage for `init` declarations, `Type.init` calls, and base-constructor initializers is not assessed; body parsing or a recognized keyword does not establish lifetime support.

**Implementation status:** The Parser preserves explicit branch body forms. Control-flow analysis checks selections, loops, value-producing Labeled Blocks, lexical transfer targets, and the completion effects of explicitly registered Deferred Blocks. It also checks lexical Unsafe permission for known operations and binder-selected function references. The default type provider handles primitive literals, simple declared Types, and basic raw-pointer and contextual `null` checks. General name/overload resolution, conversions, pattern Binding, ownership, automatic destruction, Origin compatibility, and runtime cleanup generation remain planned; unresolved checks are exposed as pending obligations. Bodies containing deferred compile-time directives await directive selection before analysis.

## Appendix D. Deferred feature index

This index links to design boundaries owned by the language sections. It adds no syntax or permissions. Implementation coverage is independent and recorded in [Appendix C](#appendix-c-implementation-status). Language-version selection is specified under [Language-version selection](#145-language-version-selection).

| Feature | Status | Owning section |
| --- | --- | --- |
| Re-export syntax | Deferred design | [Re-exports](#122-re-exports) |
| Binary artifact format | Partially specified | [Source artifacts and binary interfaces](#123-source-artifacts-and-binary-interfaces) |
| Dependency configuration and graph diagnostics | Partially specified | [External references and aliases](#121-external-references-and-aliases) |
| Declaration fragments and source execution order | Partially specified | [Container fragments](#422-container-fragments) |
| Source Generators | Partially specified | [Split structures and storage order](#431-split-structures-and-storage-order) |
| Inherited lookup and virtual/override declaration syntax | Partially specified | [Virtual members](#434-virtual-members-and-overrides) |
| Runtime-contract declaration/binding syntax; contract/exact test and checked-cast spellings | Partially specified | [Runtime contracts](#443-runtime-contracts), [tests and casts](#6652-general-view-tests-and-checked-casts) |
| Extra implicit/ordinary base conversions, contract inheritance/defaults, consuming/generic runtime requirements, external conformance | Deferred design | [Object views](#335-object-views-and-identity), [runtime contracts](#443-runtime-contracts) |
| Fixed FFI layout | Deferred design | [Structure layout and ABI](#146-structure-layout-and-abi) |
| Limited Constraint proof | Defined; implementation tracked separately | [Constraint proof system](#445-constraint-proof-system) |
| Associated-Type resolution/equality and stronger symbolic Constraint reasoning | Deferred design | [Associated Types and Property requirements](#441-associated-types-and-property-requirements), [proof boundaries](#445-constraint-proof-system) |
| Generic specialization and operation selection | Partially specified | [Inference and operation design boundaries](#53-inference-and-operation-design-boundaries) |
| Abstract Origins, escaping borrows, and lending iterators | Deferred design | [Lifetime design boundaries](#99-lifetime-design-boundaries) |
| Destruction lifetime relaxation | Deferred design | [Destruction lifetime checking](#966-destruction-lifetime-checking) |
| Additional dynamic Move Paths | Deferred design | [Move Paths and Partial Move](#913-move-paths-and-partial-move) |
| Standard duplication API | Deferred design | [Copy capability and explicit duplication](#351-copy-capability-and-explicit-duplication) |
| Exchange API and concurrency guarantees | Partially specified | [Initialization-preserving exchange](#97-initialization-preserving-exchange) |
| Raw pointer and FFI APIs | Partially specified | [Raw pointer API design boundaries](#386-raw-pointer-api-design-boundaries) |
| Option / Result syntax and propagation | Partially specified | [Error policy](#111-error-policy) |
| String indexing and library indexers | Partially specified | [Indexing and slicing](#644-indexing-and-slicing) |
| Borrowed/Exclusive/Consuming erased callable Types, opaque returns, receiver-dependent public results | Deferred design | [Callable Types](#321-callable-value-types), [Closure lifetimes](#982-closure-dependencies-and-call-results) |
| Non-escaping declarations, extended capture syntax, concurrency capabilities, general higher-ranked Callable contracts | Deferred design | [Capture rules](#6432-capture-acquisition-and-environment), [escape](#983-escape-and-retention) |
| Direct-match versus callable-erasure overload ranking | Deferred design | [Callable compatibility](#528-callable-signature-compatibility) |
| Composition Root extensions | Partially specified | [Extension boundaries and reserved syntax](#68-extension-boundaries-and-reserved-syntax) |

## Appendix E. Terminology index

This index is a reading aid. The linked sections contain the authoritative definitions and restrictions.

| Term | Short meaning | Defined in |
| --- | --- | --- |
| Access Designator | A resolved access target, without a promise of storage or Consume permission. | [Value model](#34-values-places-and-storage) |
| Adaptation Target | Core Type or object View Target and Semantics requested by `@`; result Origins are inferred. | [Explicit operations](#6641-forms-and-adaptation-targets) |
| API signature | Exposed Types and requirements checked for accessibility, beyond overload identity. | [API signature accessibility](#5122-api-signature-accessibility) |
| Binding | Associating source names and operations with declarations and meanings. | [Name resolution](#5-name-resolution-overload-resolution-and-inference) |
| CodeContext | Source-local lookup and diagnostic context for one immutable source snapshot. | [Compiler requirements](#appendix-a-compiler-implementation-requirements) |
| Compilation | One Project processed under fixed source, dependency, target, and build inputs. | [Build units](#141-build-units) |
| Compilation root | Lookup entry point for the project root and direct-dependency reference names. | [Modules](#12-modules-and-dependencies) |
| Complete value | An aggregate with completed construction and all stored fields Initialized. | [Construction and completeness](#912-aggregate-construction-and-completeness) |
| Completion | Normal or abrupt completion of evaluation, distinct from divergence and Panic Termination. | [Completions](#71-completions) |
| Composition Root | The language facility selected by `$`. | [Reserved syntax](#68-extension-boundaries-and-reserved-syntax) |
| Conformance | A Type's fulfillment of a Contract, including the correspondence between requirements and implementations. | [Constraints](#44-constraints) |
| Constraint | A condition imposed on a Type or Type Semantics. | [Constraints](#44-constraints) |
| Constraint Clause | A declaration clause expressing a Constraint as `subject is requirement`. | [Constraints](#44-constraints) |
| Constraints | The set of conditions required for a declaration to be valid or usable. | [Constraints](#44-constraints) |
| Consume | Explicit acquisition using `@move` that forces Move even for Copy Types. | [Explicit Consume](#915-explicit-consume) |
| Consume Eligibility | Whether the declaration, Type, and path provide the Consume operation. | [Consume verification](#914-consume-verification-and-representation) |
| Consume Legality | Whether the current use site may perform an eligible Consume. | [Consume verification](#914-consume-verification-and-representation) |
| Contract | A Declaration Container declared with `contract` that specifies a named capability a Type provides. | [Constraints](#44-constraints) |
| Control Boundary | A lexical boundary governing control-transfer target lookup. | [Control flow](#7-control-flow) |
| Copy | Implicit value duplication that leaves its source initialized. | [Copy and Move](#35-copy-and-move) |
| Core Type | The component of a Type that identifies what the value is. | [Type composition](#3-types-and-basic-value-model) |
| Declaration Container | A named declaration scope with members permitted by its kind. | [Containers](#42-declaration-containers) |
| Deferred Condition | A valid compile-time Condition whose dependency value is not yet available. | [Condition evaluation](#133-condition-evaluation-and-selection) |
| Deferred Block | Cleanup code registered by `defer` for its containing scope's exit. | [Deferred Blocks](#101-deferred-blocks) |
| Destruction responsibility | Responsibility for ending an owned value's lifetime under the cleanup rules. | [Value model](#34-values-places-and-storage) |
| Directive Binding | Resolution and validation of compile-time Condition names and dependencies. | [Compiler requirements](#appendix-a-compiler-implementation-requirements) |
| Discard Context | An evaluation context that does not retain an expression's result. | [Evaluation contexts](#72-blocks-and-evaluation-contexts) |
| Effective access domain | Source contexts permitted by a declaration's access and enclosing restrictions. | [Access domains](#5121-effective-access-domains-and-protected-receivers) |
| Finalization | Acceptance of a declaration, layout, specialization, or body after required checks are resolved. | [Compiler terminology](#appendix-a-compiler-implementation-requirements) |
| Getter Result Type | The Type returned by a Property read, which may differ from its Property Type. | [Default getter results](#82-default-getter-results) |
| Koto | A compiler syntax-tree node. | [Compiler terminology](#appendix-a-compiler-implementation-requirements) |
| Kotonoha | One named source or binary module. | [Modules](#12-modules-and-dependencies) |
| Loan | A borrowed place, access mode, and validity region. | [Borrow checking](#96-borrow-checking) |
| Lookup environment | Declarations, aliases, and extensions available for lookup in a scope. | [Name resolution](#5-name-resolution-overload-resolution-and-inference) |
| Move | Transfer of a value and responsibility or capability, marking its source Moved. | [Copy and Move](#35-copy-and-move) |
| Move Path | A statically tracked path with independent initialization state and destruction responsibility. | [Move Paths](#913-move-paths-and-partial-move) |
| Origin | A set of program points where a borrow is guaranteed valid. | [Origin expressions](#921-origin-expressions) |
| Partial Move | Transfer of an aggregate's part, leaving the aggregate incomplete. | [Move Paths](#913-move-paths-and-partial-move) |
| Place | A storage location that can hold a value. | [Value model](#34-values-places-and-storage) |
| Project root | Root of the primary Kotonoha's declaration hierarchy. | [Modules](#12-modules-and-dependencies) |
| Property | A value-bearing member with a Property Type and applicable accessors and storage. | [Properties](#8-properties) |
| Reborrow | A borrow derived from an existing borrow, subject to the parent's capability and Origin. | [Reborrowing](#963-reborrowing) |
| Signature | Information distinguishing declarations in the same scope. | [Signatures](#41-signatures) |
| SourceDocument | One immutable source input, including path and text, belonging to a Kotonoha. | [Source text](#21-source-text-and-encoding) |
| Temporary Place | Anonymous storage materializing a Temporary Value. | [Materialization](#361-materialization) |
| Temporary Value | An expression's temporary result, distinct from its original persistent Place. | [Materialization](#361-materialization) |
| Type | Core Type or object View Target, Semantics, and Origin together. | [Type composition](#3-types-and-basic-value-model) |
| Type Semantics | How a value is represented, owned, accessed, or used. | [Type Semantics](#33-type-semantics) |
| Value Context | An evaluation context that requires an expression's value. | [Evaluation contexts](#72-blocks-and-evaluation-contexts) |

Callable, object, and flow terms:

| Term | Meaning | Defined in |
| --- | --- | --- |
| Closure / Environment / Capture | A callable body and values acquired when it is created | [Function expressions](#643-function-expressions) |
| Function Item / common Function Type | Concrete declaration identity / shared erased calling contract | [Callable Types](#321-callable-value-types) |
| Callable / Call Receiver Requirement | Declared generic access / concrete minimum body access | [Callable constraints](#444-callable-constraints), [call receivers](#6433-call-receiver-and-acquisition) |
| Dynamic Type / Runtime Type Identity | Actual constructed Core Type / its runtime comparison identity | [Object views](#335-object-views-and-identity), [metadata](#1481-type-identity-and-descriptors) |
| View Target / Supports | Public object target / concrete-Type relationship to that target | [Object views](#335-object-views-and-identity) |
| ObjectCompatible | Verified restrictions on object receiver use | [Object calls](#6451-object-receiver-compatibility) |
| Binding Identity / Value Instance | Resolved binding / its currently held value | [Stable bindings](#791-stable-bindings-and-effective-types) |
| Effective Type / Flow State | Point-specific guaranteed Type / coordinated analysis facts | [Refinement](#79-type-refinement-and-require) |

## Appendix F. Syntax summary

**Non-normative syntax reference.** This appendix collects the specified forms; the linked language sections remain authoritative. It is not a standalone parser-generator grammar. Context-sensitive placement, token boundaries, and the open productions listed in F.9 remain part of the syntax definition; an open production does not accept arbitrary text.

In the EBNF below, quoted text is literal syntax, `|` is choice, parentheses group, and `?`, `*`, `+` mean optional, zero or more, and one or more. `? description ?` denotes a lexical class or a production whose definition is linked. `List<X>` abbreviates `X ("," X)*`; `TrailingList<X>` adds an optional final comma. These angle brackets are grammar notation, unlike quoted `"<"` and `">"`.

`NEWLINE`, `INDENT`, `DEDENT`, and `SEP` denote source-layout events under [source structure](#22-lines-indentation-and-continuation), not required internal token kinds. Layout within delimiters and between branch clauses follows the linked constructs. `Body<X>` abbreviates `NEWLINE INDENT ItemList<X> DEDENT`; `ItemList<X>` is a nonempty sequence separated where the surrounding grammar permits. Declaration Containers may additionally have empty bodies where their own rules permit them.

### F.1. Lexical grammar

[Source and layout](#2-source-and-lexical-structure), [Names and keywords](#25-names), [numeric literals](#26-number-literals), [escapes](#27-character-escapes), [character literals](#28-character-literals), [strings](#29-string-literals).

```ebnf
Name                 := NameStart NameContinue*
NameStart            := "A".."Z" | "a".."z" | "_"
                      | ? Unicode Lu, Ll, Lt, Lm, Lo, or Nl ?
NameContinue         := NameStart | "0".."9" | ? Unicode Mn, Mc, Nd, or Pc ?
PhysicalNewline      := LF | CR LF | CR
LineComment          := "//" ? text up to a physical newline or EOF ?
BlockComment         := "/*" ? text up to the first closing delimiter ? "*/"
CharacterEscape      := "\0" | "\\" | "\e" | "\t" | "\n" | "\r"
                      | '\"' | "\'" | "\u(" HexDigits1To6 ")"
HexDigits1To6         := ? one to six ASCII hexadecimal digits ?
EscapedString        := '"' (StringText | CharacterEscape | Interpolation)* '"'
Interpolation        := "\(" Expression ")"
StringText           := ? literal text excluding unescaped quote and backslash ?
RawString            := QuoteRun RawText QuoteRun
QuoteRun             := ? matching run of N double quotes, N >= 3 ?
RawText              := ? raw content delimited by that QuoteRun ?
Literal              := number-literal | CharLiteral | EscapedString | RawString
                      | "true" | "false" | "null" | "(" ")"
```

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

```text
CharLiteral = "'" (DirectScalar | CharacterEscape) "'"
```

`DirectScalar` is the character class defined in [character content and validation](#281-content-and-validation); keyword exclusions and contextual Name roles follow [Names](#25-names).

### F.2. Type grammar

[Type composition](#3-types-and-basic-value-model), [compound Types](#32-compound-type-syntax), [Semantics](#33-type-semantics), [generic application](#642-invocation-and-generic-application), [Origins](#93-abstract-origins).

```ebnf
Type                 := TypeHead ("->" Type)?
TypeHead             := SemanticsType OriginAnnotation?
SemanticsType        := Semantics "/" SemanticsType | TypeAtom
TypeAtom             := CoreType | "(" Type ")"
ObjectSemantics      := "obj" | "rc" | "arc" | "objref" | "objuniq"
RuntimeContractType  := NamedType
CoreType             := NamedType | UnitType | TupleType
UnitType             := "(" ")"
TupleType            := "(" Type "," TrailingList<Type>? ")"
NamedType            := TypeSegment ("." TypeSegment)*
TypeSegment          := TypeName TypeArguments?
TypeName             := Name | PrimitiveType | "Self"
TypeArguments        := "<" TrailingList<Type> ">"
PrimitiveType        := "isize" | "usize" | "i8" | "i16" | "i32" | "i64" | "i128"
                      | "u8" | "u16" | "u32" | "u64" | "u128"
                      | "f32" | "f64" | "bool" | "char" | "string"
Semantics            := "owner" | "ref" | "uniq" | "obj" | "rc" | "arc"
                      | "objref" | "objuniq" | "unsafe" | Name
GenericParameters    := "<" List<GenericParameter> ">"
GenericParameter     := Name | Name "/" Name
```

Object-target syntax uses the View Target lookup role; a named target may resolve to a valid `RuntimeContractType` instead of a Core Type. This shared syntax does not permit an ordinary value of a runtime contract or arbitrary Object Semantics around an already Semantics-applied Type. Layer legality and Origin attachment follow [nested Semantics](#336-nested-semantics-and-type-grouping). Callable signature syntax appears with requirements below; generated Closure and Function Item Types have no source declaration spelling.

### F.3. Declaration grammar

[Containers](#42-declaration-containers), [structures](#43-structure-declarations), [Constraints](#44-constraints), [bindings](#45-bindings), [functions](#46-functions), [parameters](#464-parameter-names-and-defaults), [aliases](#121-external-references-and-aliases).

```ebnf
QualifiedName        := Name ("." Name)*
Access               := "private" | "internal" | "public" | "protected"
                      | "protected" "internal" | "private" "protected"
AliasDeclaration     := "alias" QualifiedName
LocalBinding         := ("let" | "var") Name (":" Type)? ("=" Expression)?
InitializedBinding   := ("let" | "var") Name (":" Type)? "=" Expression
GroupDeclaration     := Access? "group" Name ContainerBody
RootGroupDeclaration := Access? "rootgroup" QualifiedName ContainerBody
StructureDeclaration := Access? "open"? "struct" Name GenericParameters?
                        BaseClause? OriginParameters? ContainerBody
BaseClause           := ":" CoreType
ContractDeclaration  := Access? "contract" Name ContainerBody
ConstructorDeclaration := Access? "init" "(" TrailingList<Parameter>? ")"
                          BaseInitializer? ExecutableBlock
BaseInitializer      := ":" "base" "(" TrailingList<Argument>? ")"
FunctionHeader       := Access? "unsafe"? "func" QualifiedName
                        GenericParameters? OriginParameters?
                        "(" TrailingList<Parameter>? ")" ("->" Type)?
FunctionDefinition   := FunctionHeader FunctionBody
FunctionBody         := "=>" Expression | Body<FunctionItem>
Parameter            := Name ("=>" Name)? ":" Type ("=" Expression)?
                      | Name "?" ":" Type "=" Expression
ConstraintClause     := (Name | "Self") "is" IsRequirement
AssociatedClause     := "associate" Name "is" IsRequirement
IsRequirement        := "not" Requirement | PositiveRequirement
PositiveRequirement  := RequirementAtom ("and" RequirementUnary)*
                        ("or" RequirementAnd)*
Requirement          := RequirementAnd ("or" RequirementAnd)*
RequirementAnd       := RequirementUnary ("and" RequirementUnary)*
RequirementUnary     := "not" RequirementUnary | RequirementAtom
RequirementAtom      := CallableRequirement | Type | Semantics | "(" Requirement ")"
CallableRequirement  := "Callable" "<" (CallableReceiver ",")? FunctionSignature ">"
CallableReceiver     := "ref" | "uniq" | "owner"
FunctionSignature    := "(" TrailingList<Type>? ")" "->" Type
FunctionItem         := ExecutableItem | ConstraintClause
ContainerItem        := Declaration | ConstraintClause | AssociatedClause
                      | Directive<ContainerItem>
ContainerBody        := ? indented ContainerItem sequence permitted by its kind ?
Declaration          := GroupDeclaration | RootGroupDeclaration
                      | StructureDeclaration | ContractDeclaration
                      | FunctionDefinition | PropertyDeclaration
                      | ConstructorDeclaration | DeinitDeclaration
                      | EnumDeclaration | ExtensionDeclaration
```

Modifier placement and compound-access combinations are constrained by [accessibility](#512-accessibility-and-reachability), even where the shared grammar uses `Access`. `open` applies only to structures. `BaseClause` has the semantic restrictions in [inheritance](#432-inheritance-and-open-structures); member-level virtual/override syntax is not supplied by this grammar.

`ConstructorDeclaration` and `DeinitDeclaration` are allowed only directly in structure bodies, subject to their merging and selection rules. A constructor has a Unit executable body but produces an owned structure through its dedicated construction operation. Neither declaration is an ordinary function declaration; constructor Origin bindings come from the containing Type's Constraints. See [constructors](#433-constructors) and [destruction declarations](#103-aggregate-destruction-and-deinit).

### F.4. Expression grammar

[Primary forms](#632-names-literals-and-grouping), [calls](#642-invocation-and-generic-application), [precedence](#65-precedence-and-associativity), [runtime type tests](#6651-runtime-is-tests), [explicit operations](#664-explicit-operations), [assignment](#67-assignment).

```ebnf
Expression           := Assignment
Assignment           := RangeExpression (AssignmentOperator Assignment)?
AssignmentOperator   := "=" | "+=" | "-=" | "*=" | "/=" | "%="
                      | "<<=" | ">>=" | "&=" | "^=" | "|="
RangeExpression      := OrExpression
                      | OrExpression? ".." OrExpression?
                      | OrExpression? "..=" OrExpression
OrExpression         := AndExpression ("or" AndExpression)*
AndExpression        := Comparison ("and" Comparison)*
Comparison           := BitOr (("<" | "<=" | ">" | ">=" | "==" | "!=") BitOr)?
                      | BitOr "is" "not"? NamedCoreType
NamedCoreType        := NamedType
BitOr                := BitXor ("|" BitXor)*
BitXor               := BitAnd ("^" BitAnd)*
BitAnd               := Shift ("&" Shift)*
Shift                := Additive (("<<" | ">>") Additive)*
Additive             := Multiplicative (("+" | "-") Multiplicative)*
Multiplicative       := Adapted (("*" | "/" | "%") Adapted)*
Adapted              := Prefix ("@" OperationTarget)*
OperationTarget      := "move" | Semantics | AdaptationType
AdaptationType       := ? Type target syntax without written Origin annotations, §6.6.4.1 ?
Prefix               := ("+" | "-" | "not" | "*" | "^" | "++" | "--") Prefix
                      | Postfix
Postfix              := Primary PostfixSuffix*
PostfixSuffix        := "." (Name | DecimalTupleIndex)
                      | "(" TrailingList<Argument>? ")"
                      | AdjacentTypeArguments | "[" Expression "]" | "++" | "--"
AdjacentTypeArguments := ? TypeArguments adjacent to an eligible Name, §6.4.2 ?
DecimalTupleIndex    := ? decimal integer literal used as a Tuple member, §6.4.1 ?
Argument             := (Name ":")? Expression
Primary              := Name | Literal | "(" Expression ")" | TupleExpression
                      | ArrayExpression | DictionaryExpression | FunctionExpression
                      | IfExpression | MatchExpression | Iteration | LabeledBlock
                      | Transfer | CompositionRootExpression | ConstructionExpression
ConstructionExpression := "::"? NamedType "." "init"
                          "(" TrailingList<Argument>? ")"
TupleExpression      := "(" Expression "," TrailingList<Expression>? ")"
ArrayExpression      := "[" TrailingList<Expression>? "]"
DictionaryExpression := "[" ":" "]" | "[" TrailingList<DictionaryEntry> "]"
DictionaryEntry      := Expression ":" Expression
FunctionExpression   := "func" CaptureList? "(" TrailingList<AnonymousParameter>? ")"
                        ("->" Type)? AnonymousBody
AnonymousParameter   := Name (":" Type)?
AnonymousBody        := "=>" Expression | ExecutableBlock
CaptureList          := "[" TrailingList<Capture>? "]"
Capture              := Name ("@" CaptureOperation)? | "var" Name ("@" "move")?
CaptureOperation     := "move" | "ref" | "uniq"
CompositionRootExpression := "$" "panic" "(" TrailingList<Argument>? ")"
```

Ordinary `is` / `is not` accepts one named struct Core Type and does not consume outer `and` / `or`. The separate compile-time [requirement expressions](#442-requirement-expressions) retain their existing extent in their dedicated contexts. Anonymous parameter/result omission and Capture Lists obey [function-expression rules](#643-function-expressions). Adaptation-target parsing and generic/comparison disambiguation follow [precedence](#65-precedence-and-associativity); these boundaries are not alternative parses selected by conversion success.

### F.5. Statements and Blocks

[Source items](#721-nonempty-executable-blocks), [inline bodies](#731-inlinestatement), [Labels](#74-labels), [transfers](#751-syntax-and-operands), [iterations](#76-iteration-constructs), [selections](#77-selections-if-match-and-yield), [defer](#101-deferred-blocks), [destruction declarations](#103-aggregate-destruction-and-deinit).

```ebnf
SourceUnit           := ? source-local declarations, aliases, and executable items, §4.2.1 ?
ExecutableItem       := Expression | LocalBinding | FunctionDefinition
                      | UnsafeStatement | DeferStatement | RequireStatement
                      | Directive<ExecutableItem>
ExecutableBlock      := Body<ExecutableItem>
UnsafeStatement      := "unsafe" ":" (InlineStatement | ExecutableBlock)
DeferStatement       := "defer" ":" (InlineStatement | ExecutableBlock)
RequireStatement     := "require" Expression RequireJoin "else" RequireElseBody
RequireElseBody      := InlineStatement | ExecutableBlock
RequireJoin          := ? same-line or next effective aligned line, §7.9.3.1 ?
InlineStatement      := ? one permitted single-line item, with no following Block, §7.3.1 ?
LabeledBlock         := Name ":" ExecutableBlock
Iteration            := (Name ":")? (ForExpression | WhileExpression | LoopExpression)
ForExpression        := "for" ForBinding "in" Expression ExecutableBlock
ForBinding           := Name | "(" List<Name> ")"
WhileExpression      := "while" Condition ExecutableBlock
LoopExpression       := "loop" ExecutableBlock
IfExpression         := "if" Condition BranchBody
                        (BranchJoin "else" "if" Condition BranchBody)*
                        (BranchJoin "else" BranchBody)?
Condition            := Expression | "(" InitializedBinding ")"
BranchBody           := "=>" Expression | ExecutableBlock
BranchJoin           := ? permitted same-line or line-separated else boundary, §7.7.2 ?
MatchExpression      := "match" Expression Body<MatchArm>
MatchArm             := Pattern "=>" (Expression | ExecutableBlock)
Transfer             := "return" Expression?
                      | "exit" Expression? ("from" Name)?
                      | "continue" Name?
                      | "yield" Expression
DeinitDeclaration    := "deinit" ExecutableBlock
```

### F.6. Property grammar

[Property declarations](#8-properties), [accessor bodies](#83-accessors), [inline accessors](#84-inline-accessor-declarations), [access restrictions](#87-access-control), [contract requirements](#842-contract-property-requirements).

```ebnf
PropertyDeclaration  := Access? ("let" | "var") Name
                        (":" Type)? ("=" Expression)?
                        (inline-accessors | Body<Accessor>)?
Accessor             := Access? "get" ("->" Type)? AccessorBody?
                      | Access? "set" AccessorBody?
AccessorBody         := "=>" Expression | ExecutableBlock
```

```text
inline-accessors := has accessor-declaration (',' accessor-declaration)*

accessor-declaration := access-restriction? get ('->' ResultType)?
                      | access-restriction? set
```

`access-restriction` denotes `Access`; `ResultType` denotes `Type`.

### F.7. Origin grammar

[Origin expressions](#921-origin-expressions), [Origin parameters](#93-abstract-origins), [named arguments](#931-origin-arguments), [outlives notation](#922-ordering-and-intersection).

```ebnf
OriginParameters     := "origin" List<Name>
OriginAnnotation     := "from" (origin-expression | "(" TrailingList<OriginArgument> ")")
OriginArgument       := Name "=>" origin-expression
```

```text
origin-expression := Name
                   | origin-expression '.' Name
                   | static
                   | origin-expression 'and' origin-expression
```

`o1 : o2` is the [outlives relation notation](#922-ordering-and-intersection), not an additional source declaration production.

### F.8. Compile-time directive grammar

[Directive syntax](#131-syntax-and-structural-selection), [closed Condition forms](#132-condition-forms-and-narrowing), [excluded-syntax parsing](#135-diagnostics-and-excluded-syntax).

```ebnf
Directive<Item>      := IfDirective<Item> | MatchDirective<Item>
IfDirective<Item>    := "#" "if" CompileCondition (NEWLINE Item | Body<Item>)
MatchDirective<Item> := "#" "match" NEWLINE INDENT CaseList<Item> DEDENT
CaseList<Item>       := CaseArm<Item>+ DefaultArm<Item>? | DefaultArm<Item>
CaseArm<Item>        := "#" "case" CompileCondition Body<Item>
DefaultArm<Item>     := "#" "case" "_" Body<Item>
CompileCondition    := CompileAnd ("or" CompileAnd)*
CompileAnd          := CompileComparison ("and" CompileComparison)*
CompileComparison   := CompileUnary (("==" | "!=") CompileUnary)?
                      | Name "is" "not"? ConditionRequirement
CompileUnary        := "not" CompileUnary | CompileAtom
CompileAtom         := "true" | "false" | SignedInteger | PlainString | Name
                      | "(" CompileCondition ")"
SignedInteger       := ("+" | "-")? IntegerLiteral
IntegerLiteral      := ? integer alternatives of number-literal in F.1 ?
PlainString         := ? StringLiteral without interpolation, §13.2 ?
ConditionRequirement := QualifiedName | PrimitiveType | Semantics
```

The hash forms use `#` followed by reserved lowercase directive names. `Item` retains the surrounding syntax category; directives do not make an otherwise forbidden item legal there. Case layout and excluded-target grammar checking follow the linked sections.

### F.9. Syntax boundaries

These entries record the limits of a complete syntax summary for this revision. They are not wildcard productions or newly defined features.

| Form or production | Owning syntax and boundary |
| --- | --- |
| `EnumDeclaration`, `ExtensionDeclaration` | [Container kinds](#42-declaration-containers) identify the forms; [fragment and identity boundaries](#422-container-fragments) remain open. No complete enum payload or extension-body grammar is supplied. |
| Virtual/abstract/override declarations and ordinary base-member invocation | Access, compatibility, dispatch, and destruction semantics are specified; final member modifier/body spellings remain deferred. Base clauses and base-constructor initializers are defined in F.3. |
| Runtime-contract designations, associated-type bindings, exact/contract tests and checked casts | [Contracts](#443-runtime-contracts) and [object operations](#6652-general-view-tests-and-checked-casts) define meaning without final source spellings. Ordinary struct `is` and explicit upcasts are defined. |
| Callable extensions | Borrowed or Exclusive/Consuming erased Types, public lending results, non-escaping declarations, capture aliases/initializers/parts, generic receiver Semantics, and `from environment` remain unintroduced. |
| `Pattern` | [Match arms](#773-match) specify the arm wrapper. Full pattern, enum construction, and payload syntax remain [partially specified](#111-error-policy). |
| Attributes | `#Name` is distinct from a directive under [reserved syntax](#68-extension-boundaries-and-reserved-syntax). General attribute arguments and placement need their own specification. |
| Additional Composition Root expressions | Only `$panic(...)` is given a complete operation syntax here; see [extension boundaries](#68-extension-boundaries-and-reserved-syntax). |
| Additional type arguments and object allocation | [Generic application](#642-invocation-and-generic-application) does not define general constant type arguments. [Constructors](#433-constructors) define owned structure construction, not allocation APIs for object Semantics; those APIs must preserve [object destruction](#1033-ownership-object-release-and-reentry). |
| Function parameters | The combined optional/external-name form remains under [parameter design boundaries](#53-inference-and-operation-design-boundaries); the separate forms are summarized in F.3. |
| Re-export, fixed FFI layout, and failure propagation | See [Re-exports](#122-re-exports), [layout boundaries](#146-structure-layout-and-abi), and [error policy](#111-error-policy). |
