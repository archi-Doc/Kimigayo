# Kimigayo Language Specification

The document has six parts. Numbered headings use **chapter → section → subsection**; appendices separate compiler obligations, optional algorithms, implementation status, design boundaries, terminology, and grammar. Each concept has an owning section; cross-references apply its rules without redefining them.

For a broad, non-normative source walkthrough, see the [specification tour](examples/SpecTour/README.md). It illustrates specified language features beyond current executable support and identifies features whose source APIs remain undefined.

For the first executable program, start with [minimal console output](#224-minimal-console-output), [program startup](#222-program-startup-and-static-initialization), and [LLVM output/native build](#208-llvm-output-native-build-and-execution). The language rules below remain distinct from the implementation milestone in [STATUS.md](STATUS.md#c12-first-executable-milestone).

- [Part I. Introduction and source text](#part-i-introduction-and-source-text)
  - [1. Overview](#1-overview)
  - [2. Source and lexical structure](#2-source-and-lexical-structure)
- [Part II. Types, values, and sequences](#part-ii-types-values-and-sequences)
  - [3. Types and values](#3-types-and-values)
  - [4. Arrays, indexing, and slices](#4-arrays-indexing-and-slices)
  - [5. Raw pointers and unsafe memory](#5-raw-pointers-and-unsafe-memory)
- [Part III. Declarations and static semantics](#part-iii-declarations-and-static-semantics)
  - [6. Declarations and containers](#6-declarations-and-containers)
  - [7. Functions and callable values](#7-functions-and-callable-values)
  - [8. Generics, constraints, and contracts](#8-generics-constraints-and-contracts)
  - [9. Names, signatures, and access](#9-names-signatures-and-access)
  - [10. Overload resolution and inference](#10-overload-resolution-and-inference)
  - [11. Properties](#11-properties)
- [Part IV. Expressions and control flow](#part-iv-expressions-and-control-flow)
  - [12. Expressions](#12-expressions)
  - [13. Operators and assignment](#13-operators-and-assignment)
  - [14. Control flow](#14-control-flow)
- [Part V. Ownership, cleanup, and failure](#part-v-ownership-cleanup-and-failure)
  - [15. Ownership and lifetime analysis](#15-ownership-and-lifetime-analysis)
  - [16. Scope exit and destruction](#16-scope-exit-and-destruction)
  - [17. Failure handling](#17-failure-handling)
- [Part VI. Programs, compilation, and runtime](#part-vi-programs-compilation-and-runtime)
  - [18. Modules and dependencies](#18-modules-and-dependencies)
  - [19. Compile-time directives](#19-compile-time-directives)
  - [20. Compilation configuration](#20-compilation-configuration)
    - [Mods: source generation](#207-mods-source-generation)
    - [LLVM output, native build and execution](#208-llvm-output-native-build-and-execution)
  - [21. Layout, runtime metadata, and code generation](#21-layout-runtime-metadata-and-code-generation)
    - [Layout modes and representation](#211-structure-layout-and-abi)
    - [Checked lowering and internal ABI](#214-checked-lowering-and-internal-abi)
    - [LLVM Windows x64 profile](#215-llvm-windows-x64-profile)
  - [22. Core, program execution, and foreign functions](#22-core-program-execution-and-foreign-functions)
    - [Startup and shutdown](#222-program-startup-and-static-initialization)
    - [Foreign function imports](#223-foreign-function-imports)
    - [Initial Windows runtime](#225-initial-windows-runtime)
- [Appendices](#appendices)
  - [Appendix A. Compiler implementation requirements](#appendix-a-compiler-implementation-requirements)
  - [Appendix B. Non-normative reference models](#appendix-b-non-normative-reference-models)
  - [Appendix C. Implementation status](#appendix-c-implementation-status)
  - [Appendix D. Deferred feature index](#appendix-d-deferred-feature-index)
  - [Appendix E. Terminology index](#appendix-e-terminology-index)
  - [Appendix F. Syntax summary](#appendix-f-syntax-summary)

## Part I. Introduction and source text

### 1. Overview

#### 1.1. Purpose

**Kimigayo** is a programming language built from scratch to be consistent, fast, simple, fun, and safe.

This document defines the intended language. Language rules, Compiler requirements, and the recorded implementation snapshot are distinct; see [implementation status](STATUS.md).

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

        #switch
            #case windows
                return "windows"
            #case linux
                return "linux"
            #case _
                return "other"
```
> **Design Note (Do not modify this text!)**
>
> * `$` is not a macro. It is the Composition Root.

#### 1.2. Conventions and notation

Kimigayo prioritizes consistency and language quality over compatibility between versions. Reproducible builds must pin the compiler build, source, dependencies, and configuration; a language-version label alone does not identify a pre-alpha compiler.

User-defined Types, Contracts, and Declaration Containers conventionally use PascalCase; built-in Type keywords retain their specified spellings. Functions, Fields, Properties, local bindings, parameters, and other value names generally use camelCase.

| Notation | Meaning and uses |
| --- | --- |
| `[]` | Array and Dictionary construction, fixed-array Types, indexing, Range-based slicing, and anonymous-function Capture Lists. |
| `()` | Ordered grouping: parameters, arguments, Tuples, Unit, Function Types, conditions, and operator precedence. |
| `<>` | Type arguments and [function length arguments](#44-function-length-parameters). |
| `{}` | Unused; reserved for future language evolution. |
| `=` | Initialization, parameter defaults, or assignment according to context; acquisition follows [Copy and Move](#35-copy-and-move). |
| `@` | Explicit Type/Semantics adaptation; see [explicit operations](#135-explicit-operations). |
| `->` | Result Type associated with the input side of a function declaration or Function Type. |
| `=>` | Introduces a single-item executable Body; also maps parameter names and named Origin arguments in their own grammars. |
| `:` | Separates names from Types, argument names from values, dictionary keys from values, labels from constructs, and named transfer targets from values. It also introduces structure bases, Contract parents, and constructor `: base(...)`, but never an executable Body. In Origin relations, `a : b` means a outlives b, including equal lifetimes. |
| `#` | A compile-time construct. Lowercase reserved directives such as `#if` differ from PascalCase Attributes such as `#Inline`. |
| `$` | Selects a language-provided Composition Root operation; see §13.8. |
| `;` | Forbidden outside comments and literals; never a statement or Type separator. |

Types combine Semantics, a Core or object View Target, and Origins; see [Types and values](#3-types-and-values). Examples are independent unless explicitly connected. Application-specific Types and APIs are assumed, not required library interfaces. API examples may show signatures without implementations; this does not permit bodyless source definitions. `text` fences may show conceptual notation; lines marked Error intentionally violate a rule.

#### 1.3. Reading the rules

Examples illustrate ordinary use, boundaries, or intentional errors; they do not add rules.

| Wording | Meaning |
| --- | --- |
| must / must not | Mandatory requirement or prohibition. The feature defines whether a violation is a compile-time error, Abort, or an Unsafe contract violation. |
| may | Permission within all stated constraints. |
| should | Recommendation, not a condition for language conformance. |
| is planned | Implementation work is intended; this is not a language rule. |
| is deferred | In a design-status note, design is postponed; it grants no language permission. |
| implementation-defined | The implementation chooses within the stated limits and must document the choice. |
| unspecified | Any result within the stated limits is permitted; the choice need not be documented. This does not imply undefined behavior. |

Unqualified declarative rules and imperative requirements are normative even without `must`. Examples illustrate those rules and do not override them. Compiler requirements preserve required information and invariants; a **Non-normative reference model** is an optional algorithm, not an alternative semantics.

**Specified, not implemented** means settled rules with no available implementation. **Partially specified** identifies remaining design questions; **Deferred design** withholds the feature. Neither design status nor implementation plans grant language permission.

### 2. Source and lexical structure

Source structure determines token boundaries and syntactic containment.

The punctuation, layout, and grammar rules in this chapter are language requirements. Error-recovery tokens and current parser acceptance are tracked separately in [STATUS.md](STATUS.md). Literal-specific rules appear with each literal form.

#### 2.1. Source text and encoding

Kimigayo source text uses UTF-8. Invalid UTF-8 source byte sequences are compile-time errors.

A **SourceDocument** is one immutable source snapshot (path and text) belonging to a source module. A **Kotonoha** is that named module; its dependency and source-environment rules appear under [Modules and dependencies](#18-modules-and-dependencies).

#### 2.2. Lines, indentation, and continuation

Use four U+0020 spaces per indentation level; tabs are forbidden in indentation. Physical line endings may be LF, CRLF, or CR. Newlines separate source items where the grammar permits. Commas separate arguments/elements, not statements or binary operands. A leading binary operator does not continue a line.

Each executable Body is either `=>` followed by one expression or statement, or a newline and an indented body (§14.2). The `=>` and start of its item must be on the header's ending physical line. Never use a body colon, `=>` alone before a newline, or a separate leading `=>` continuation line.

The **header baseline** is the indentation of its starting physical line, not its keyword's character column, label length, or final continuation line. An indented body starts exactly one level deeper. A match arm list is one level deeper than match, and an indented arm body one level deeper than the arm.

##### 2.2.1. Layout normalization

An **effective line** starts or continues a source token outside comments and literal-internal text. Blank/comment-only lines do not end a body or branch join. Subsequent content lines within one multiline literal produce no layout events and undergo no indentation validation. Other code indentation must be a multiple of four spaces; error recovery does not make invalid indentation legal.

Track bodies, explicit delimiters, and method-chain continuations separately. At an effective line, first determine from grammar whether a body or arm list starts. Require its header baseline plus four spaces and produce NEWLINE/INDENT events. For a continuation, validate its delimiter/chain alignment without an item separator. Otherwise close completed chains and bodies in inner-to-outer order until the line's enclosing level is reached; reject a dedent to no active level or a jump that invents an empty body. Body-closing boundaries also separate completed enclosing items where the grammar permits SEP. At EOF, diagnose unclosed explicit delimiters or missing bodies and close remaining bodies.

Within parentheses, brackets, or recognized generic delimiters, continued content uses one additional indentation level per open delimiter beyond the active body/chain level. Matching closers may align with their opening line or remain at content indentation. Comparison angles do not establish continuation. Nested bodies generate their own layout events. An outer comma or closer on the same line cannot close an indented body; dedent on the next effective line first.

Header expressions continued across lines must use delimiters inside that expression; grouping the entire construct does not by itself continue its header. A leading `->` may continue a function/accessor header only where its grammar still expects it, at one extra level from the header baseline. It does not change that baseline or authorize a separate `=>` line. Content inside a delimiter opened on that `->` line uses one additional level beyond the `->` line itself, and the continuation ends before the next effective line at or above the `->` line's level, which starts the body or the next item.

A **delimiter region** governs both single-item body nesting and nested-if grouping:

| New region | End |
| --- | --- |
| Grouped expression; each argument/element in parentheses or brackets | Matching closer or separator before the next argument/element |
| Each item of an indented body | End of that item |
| Each match arm | End of that arm |

Labels, transfer operands, and successive `=>` tokens do not start regions. Inside a single-item body, body-bearing constructs in the same region must also use single-item bodies. A match arm list is not an executable body, and each arm starts a region, so such a match may have indented arm bodies.

```kimi
if ready => for item in items => process(item)
defer => unsafe
    releaseRaw(pointer) // Error: unsafe needs a single-item body in this region.

consume(
    match mode
        .Fast => 1
        _
            prepare()
            yield 2
)
```

**Branch joins.** Else/else-if may follow on the ending physical line of a single-item body or the next effective line. After an indented body, close it by dedent on the next effective line before reading the clause. A separate clause line aligns with the initial if's baseline; no other item may intervene. Require's else similarly follows its condition on the same line or the next effective line aligned with require.

Group an entire inner if inside an if/else-if/else single-item body when it shares that delimiter region, whether or not it has an else. Group any body-bearing expression used inside a header expression. Missing required grouping or an unmatched else is a syntax error. A statement cannot be grouped as an expression.

```kimi
let value = if a => (if b => 1 else => 2) else => 3
if (if a => b else => c) => work()
func run() => if ready => work() // No enclosing if: grouping is unnecessary.
```

Formatters preserve Body forms and prefer keeping an if inside a single-item body, including its else, on one line. If it needs an indented enclosing body, offer a separate semantics-checked refactoring (§14.2); formatting alone must not change result use, transfer targets, or cleanup. If a multiline non-final argument needs a leading comma after a dedent, prefer a typed intermediate local or moving it to the last argument only when evaluation and ownership semantics are preserved.

##### 2.2.2. Leading-dot continuation and Case references

Determine a new body or match-arm position from grammar before considering dot continuation. A leading dot at an arm starts a Case Pattern; at the first item of a new executable body it may start Case construction. Neither continues the header. These decisions do not use expected Types or name lookup. A header's following `.where(...)` line is therefore not a header continuation; suggest delimiters inside the header expression rather than reparsing by guesswork.

Otherwise a leading single dot at exactly one extra indentation level after an expression continues it; subsequent chain lines use that same extra level. Thus `.Some(1)` one level below a completed root-level initializer is a member-call continuation, while a Case Pattern below match begins an arm. Range tokens are not single dots. To force an independent Case expression, use its normal item indentation, optionally group it, or qualify its enum Type. Case lookup follows §6.3.2.

#### 2.3. Whitespace and comments

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

`total` is initialized to `3`. Both `work()` and `finish()` belong to `example`'s body. Blank and comment-only lines do not supply an executable body; see the [nonempty Block rule](#1421-nonempty-executable-blocks).

#### 2.4. Tokens and separators

A token's spelling is contiguous. Separate adjacent spellings when their concatenation would form a different token. Names, keywords, literals, punctuation, and operators follow their own token rules; recognizing a token does not make it a permitted expression.

The [notation table](#12-conventions-and-notation) summarizes punctuation. Expression grouping, generic/comparison boundaries, and the token rules for `@` follow [precedence and associativity](#131-precedence-and-associativity).

`;` is never a statement separator or an expression-body terminator. Every occurrence outside comments and literal content is an error. Write separate statements on separate effective lines. In particular, `if condition => 1` joins an aligned following `else` without `;`. `$` and `#` are separate punctuation tokens; `$abort` is `$` followed by the Name `abort`, and `#if` is `#` followed by the keyword `if`.

#### 2.5. Names

A **Name** identifies a declaration in source. All named declarations, including Declaration Containers, Types, functions, Fields, Properties, bindings, and parameters, use the same character rules. A Name contains a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`),
- an underscore (`_`), or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`), or Letter Number (`Nl`).

Each continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`), or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`), or Connector Punctuation (`Pc`).

Names are equal if and only if their Unicode scalar sequences match exactly. Comparison is case-sensitive and culture-independent. No Unicode normalization, case folding, compatibility mapping, or removal of characters is performed for lookup or duplicate-name detection. These rules also apply to compile-time constant Names in the dedicated [Condition environment](#192-environment-condition-forms).

Every Name, in both declarations and references, must already be in Unicode Normalization Form C (NFC). A non-NFC spelling is a compile-time error; the compiler does not silently normalize it. For example, a Name containing U+00E9 (`é`) is permitted, while the canonically equivalent sequence U+0065 U+0301 is rejected. In ordinary source lookup, `Dog` and `dog` are distinct Names, as are ASCII `A` and fullwidth `Ａ`.

All Format (`Cf`) characters are forbidden anywhere in Names, including bidirectional controls and zero-width join/non-join controls. This restriction applies to Names, not to comment or literal contents. It does not exclude every default-ignorable character in other Unicode categories.

Visually confusable Names are not treated as equal. Implementations may provide optional lint warnings for confusable Names; such warnings do not affect name identity or language validity, and the compiler is not required to emit them.

Character-category tests and NFC validation use [Unicode 15.0.0](https://www.unicode.org/versions/Unicode15.0.0/) data. Characters unassigned in that release are not permitted in Names. Host runtime, operating system, and globalization settings must not change these results; adopting another Unicode release requires a language-specification revision.

Contextual keywords may be Names where allowed; reserved keywords may not. `in` delimits a for header and `has` a Property Requirement list. Built-in Semantics names after `@` select shorthand targets; `move` has no special operation meaning and uses ordinary Name lookup. There is no explicit Move operator. `Self` is reserved; `self`, `value`, and accessor `storage` follow contextual binding rules (§9.2 and §11).

`public`, `internal`, `private`, `protected`, and `open` are reserved modifier keywords. The compound access specifications `protected internal` and `private protected` each consist of two keywords; their placement follows [accessibility](#93-accessibility-and-reachability).

`init`, `deinit`, and `base` are reserved for [construction](#623-constructors) and destruction. They do not introduce ordinary callable Names or an implicit base receiver.

`require` and `do` are reserved for the [require statement](#1411-require-statement) and [do expression](#1432-do-expressions).

`specialize` is contextual immediately before `func` in an [explicit specialization declaration](#88-explicit-full-function-specialization); it does not reserve the name in unrelated contexts.

For example, `Dog`, `_value`, `point2`, `Delta`, and `ǅelta` are valid Names, while `2point`, `has-value`, and the empty string are not.

##### 2.5.1. Keyword and punctuation inventory

These tables define token classes independently of internal enum groupings. Unreserved identifier spellings remain Names, subject to the listed contextual rules. Match reserved words only as complete words: ifValue is one Name.

| Reserved class | Spellings |
| --- | --- |
| Primitive Types | `isize`, `usize`, `i8`, `i16`, `i32`, `i64`, `i128`, `u8`, `u16`, `u32`, `u64`, `u128`, `f32`, `f64`, `bool`, `char`, `string` |
| Bindings and functions | `let`, `var`, `func` |
| Control, tests, and literals | `if`, `else`, `case`, `for`, `while`, `loop`, `do`, `match`, `return`, `exit`, `continue`, `yield`, `require`, `defer`, `is`, `not`, `and`, `or`, `true`, `false`, `null` |
| Access and inheritance | `public`, `internal`, `private`, `protected`, `open` |
| Dedicated forms | `Self`, `init`, `deinit`, `base` |
| Compile-time selection | `switch`; used after `#`, with no runtime switch construct. |
| Reserved future syntax | `as` |

| Contextual class | Spellings and recognizing context |
| --- | --- |
| Declarations | alias, rootgroup, group, struct, enum, contract, computed, property; declaration/header positions. extension is reserved contextually for a future declaration, which is rejected in this revision. |
| Unavailable declaration modifiers | virtual, override, abstract; recognized only in a declaration's leading modifier sequence and rejected with the unavailable-feature diagnostic. |
| Parameters, Origins, accessors | in in a for header; origin in an Origin parameter list; from in an Origin annotation; to immediately after exit, continue, or yield; static as the distinguished Origin in Origin expressions; associate in an associated-Type declaration/specification; has, get, set in accessor syntax; specialize immediately before func; when in conditional conformance. |
| Semantics and Safety | owner, ref, uniq, obj, rc, arc, objref, objuniq, unsafe in Semantics positions, including requirements; unsafe also before func and before a Body introducing an Unsafe Statement. |
| Semantics categories | value, valueborrow, object, objectborrow, borrow, owning, reference in Semantics requirements; see [category sets](#33-type-semantics). |
| Contextual bindings and operations | self, value, storage under receiver/accessor rules; abort after $. |
| Fixed arrays and lengths | of only between the length and element Type in `[N of T]`; length only at the start of a generic parameter declaration followed by its Name. |

Recognize unavailable modifiers before the existing modifiers and declaration introducer in the same logical header. This includes Type/function/Property declarations, Contract requirements, constructors, deinit, and accessors, even where access/open modifiers are otherwise forbidden. Do not scan across a newline or indent/dedent that separates independent items. Thus `abstract open struct`, `virtual func`, `virtual init`, `override deinit`, and `abstract get` receive the same diagnostic. Ordinary Names remain valid in `struct abstract`, `func virtual(...)`, `let override: i32`, `x.abstract()`, and `virtual(...)`; a standalone `abstract` expression must not consume the next line's declaration. This recognition adds no valid declaration form or globally reserved word.

| Punctuation/operator class | Spellings |
| --- | --- |
| Structural | `(` `)` `[` `]` `,` `.` `:` `::` `->` `=>` `@` `#` `$` `?` |
| Arithmetic and updates | `+` `-` `*` `/` `%` `++` `--` `+=` `-=` `*=` `/=` `%=` |
| Comparison and assignment | `=` `==` `!=` `<` `<=` `>` `>=` |
| Bitwise and shifts | `&` `\|` `^` `<<` `>>` `&=` `\|=` `^=` `<<=` `>>=` |
| Ranges | `..` `..=` |
| Recognized but unavailable | `{` `}` `;` `!` `&&` `\|\|` |

Outside comments and literals, match the longest punctuation spelling: `..=` before `..` before `.`, `->` before `-`, `<<=` before `<<` before `<`, `>>=` before `>>` before `>`, `::` before `:`, and `=>` or `==` before `=`. Thus `a+++b` is `a`, `++`, `+`, `b`; there is no `+++` token.

Only when closing syntactically recognized generic Type arguments/parameters may `>>` split into two `>` tokens, or `>>=` into `>`, `>`, `=` (or `>`, `>=` when one level closes). Expression shifts remain unchanged. Tuple indices use the exception in §2.6. Unlisted punctuation is invalid unless it forms a grammatically valid sequence of listed tokens.

#### 2.6. Number literals

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

binary-literal       := '0' ('b' | 'B') '_'* binary-digit binary-tail
octal-literal        := '0' ('o' | 'O') '_'* octal-digit octal-tail
hexadecimal-literal  := '0' ('x' | 'X') '_'* hexadecimal-digit hexadecimal-tail

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

`_` is an ignored digit separator. Consecutive separators, separators after a base prefix, and trailing separators are allowed: `1__000`, `123_`, `0x_FF`, and `0b__101__` are valid. Every base prefix requires at least one digit valid for that base; `0x`, `0b`, and `0o___` are compile-time errors. An exponent must start with a decimal digit immediately after `e`/`E` and its optional sign; `1e_2` and `1e+_2` are invalid.

An immediately adjacent Name continuation character cannot start a separate token after a number. Consume the remaining Name continuation characters as part of a malformed numeric token and diagnose it; `0xg`, `0b102`, and `123abc` are errors.

A decimal point belongs to the literal only when followed immediately by a decimal digit: `1.0` is floating point; `1.` is integer `1` followed by a dot. Only decimal literals support fractions and exponents; `0xFF.0` starts with integer `0xFF`.

Exception: immediately following a member-access dot token (ignoring intervening spaces and comments), scan a Tuple index as the maximal run of ASCII decimal digits only. Do not consume a fraction, exponent, base prefix, or digit separator there. Thus `pair.0.1` is `pair`, `.`, `0`, `.`, `1`, and `pair.0.name` accesses a named member of element zero. The parser may equivalently resplit a numeric token using its original spelling. Ordinary `0.1` remains a floating-point literal; the Tuple index must subsequently be in range.

After removing separators, a decimal literal with a fraction or exponent denotes an exact decimal value. Round it once to its determined IEEE 754 `f32` or `f64` Type using round-to-nearest, ties-to-even; no intermediate floating Type is used. A result of either infinity is a compile-time error; subnormal and zero results are permitted. Other decimal literals and all base-prefixed literals are integers. Magnitudes `0` through `2^128 - 1` are stored as 128-bit bit patterns; larger magnitudes are invalid.

Number literals have no Type suffix. [Expression type inference](#1231-type-inference) defines contextual Types and defaults. To specify a literal's Type, use a declaration annotation or [explicit literal fitting](#1354-numeric-conversions-and-literals), such as `123@i32`.

#### 2.7. Character escapes

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

#### 2.8. Character literals

A `CharLiteral` has Type `char`. It encloses one directly written Unicode scalar value or one [Character Escape](#27-character-escapes) in single quotation marks. Delimiters are not part of the value.

```text
CharLiteral = "'" (DirectScalar | CharacterEscape) "'"
```

##### 2.8.1. Content and validation

After escape processing, the content must be exactly one Unicode scalar value. `DirectScalar` is any scalar value except the following, which must be escaped:

| Excluded direct content | Code points |
| --- | --- |
| Apostrophe and backslash | U+0027, U+005C |
| Controls | U+0000..U+001F, U+007F..U+009F |
| Line and paragraph separators | U+2028, U+2029 |

A Char Literal cannot contain a physical line break or tab and does not support string interpolation. Violations are compile-time errors.

```kimi
let letter: char = 'A'       // U+0041
let euro: char = '€'         // U+20AC
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

##### 2.8.2. Normalization and displayed characters

The compiler does not normalize Char Literal content. Validation uses the content after escape processing. A `char` represents a scalar value, not a grapheme cluster or a displayed character; a combining mark alone is valid.

```kimi
'é'           // Valid: U+00E9
'\u(E9)'      // Valid: U+00E9
'e\u(301)'    // Error: U+0065 and U+0301
'\u(301)'     // Valid: one combining mark
```

The front end parses and validates Char Literals and preserves their original spelling when writing the syntax tree.

#### 2.9. String literals

A `StringLiteral` produces UTF-8 text of Type `string`. Both forms support single or multiple lines:

| Form | Delimiter | Backslash escapes | Interpolation |
| ---- | --------- | ----------------- | ------------- |
| Escaped string | One double quotation mark (`"`) on each side | Yes | Yes |
| Raw string | The same number of double quotation marks, at least three, on each side | No | No |

##### 2.9.1. Escaped strings

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

The delimiters are not part of the value. In both escaped and raw string text, each physical LF, CRLF, or CR contributes one LF. No spaces, indentation, initial newline, or final newline are stripped. Escape results and interpolated values are not normalized; use `\r` in an escaped string for an explicit CR. Literal-internal lines do not affect the enclosing layout stack.

Escaped strings support the shared [Character Escapes](#27-character-escapes) and string interpolation with `\(expression)`.

An interpolation begins with `\(` and ends at its matching `)`. The enclosed text is parsed as a Kimigayo expression, including any nested parentheses, and the expression's string representation is inserted into the surrounding string:

Find the matching close using ordinary expression tokenization, including character literals, escaped/raw strings, nested interpolations, and comments. Parentheses inside those token contents do not change the enclosing interpolation depth. Missing delimiters and malformed nested tokens are errors. Interpolation expressions have their own delimiter/layout context; their newlines cannot close an outer source body. The language imposes no fixed nesting limit, but implementations may document a finite resource limit and must diagnose exceeding it rather than truncate or misparse the source.

```kimi
"Hello, \(name)."
"Total: \(price * quantity)"
```

##### 2.9.2. Raw strings

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

**Delimiter selection.** At a string token's start, count the maximal consecutive run of double quotes. One quote begins an escaped string; exactly two quotes form the empty escaped string `""`; a run of N >= 3 begins a raw string with delimiter length N. Never split that opening run into an opening and closing delimiter. In particular, six quotes followed by EOF are an unterminated raw string, not an empty raw string.

After the opening run, the first maximal quote run of length M >= N terminates the raw string: its first M - N quotes are content and its last N quotes are the closing delimiter. Shorter runs are entirely content. The entire terminating run belongs to this token; adjacent string tokens do not concatenate implicitly. Thus `"""text""""` has content `text"`. Use `""` for empty text; a raw string containing a physical newline is not empty.

## Part II. Types, values, and sequences

### 3. Types and values

An ordinary **Type** combines Semantics, a Core, and any required Origins. Its single-layer form is:

```text
Type = Semantics/Core from Origin
```

| Term | Meaning |
| --- | --- |
| Type | The complete type, including Semantics, its target, and all Origin dependencies. |
| Semantics | The value's representation, ownership, borrowing, access, and safety rules. |
| Core | The component that defines the value's kind, structure, and identity. |
| Origin | The set of program points where a borrow is guaranteed valid; constrains borrows and values that retain them. |

For example, `ref/Dog from source` has Semantics `ref`, Core `Dog`, and Origin `source`. Semantics and Origins may be omitted only under the language's inference and elision rules; not every layer has an Origin.

A Core is distinct from the **outer** Semantics and Origins. Its elements and generic arguments may contain complete Types: the Core of `owner/(i32, ref/Dog from source)` is a Tuple whose second element retains its borrow and Origin.

The basic form has two extensions. `ref`, `uniq`, and `unsafe` may enclose a complete Type, as in `ref/obj/Dog`; see [nested Semantics](#336-nested-semantics-and-type-grouping). Object Semantics may target a permitted runtime Contract View instead of a Core; the Contract itself is not a Core. See [object views](#335-object-views-and-identity).

The following tree classifies components, not inheritance relationships or legal combinations:

```text
Type
├─ Semantics
│  ├─ Value: owner
│  ├─ Value Borrow: ref, uniq
│  ├─ Object: obj, rc, arc
│  ├─ Object Borrow: objref, objuniq
│  └─ Unsafe: unsafe
├─ Core
│  ├─ Scalar: Integer, Floating-point, Boolean, Character
│  ├─ String
│  ├─ Unit
│  ├─ Never
│  ├─ Struct
│  ├─ Enum
│  ├─ Tuple
│  ├─ Fixed Array: [N of T]
│  ├─ Callable
│  │  ├─ Function Item
│  │  ├─ Concrete Closure
│  │  └─ Common Function
│  └─ Other named examples: Array<T>, Dictionary<K, V>, Slice<T>
└─ Origin
   ├─ Derived from a borrow source
   ├─ Declared abstract Origin
   ├─ Intersection of Origins
   └─ static
```

Named examples do not introduce separate declaration forms or mutually exclusive categories. Structs and enums follow §6; arrays follow [arrays and slices](#4-arrays-indexing-and-slices). The **Core Kotonoha** in §22 is the language's foundation module, distinct from a Type's Core.

#### 3.1. Primitive cores

Primitive Cores are built in. Listed sizes are storage sizes.

**Scalar** (short for **Primitive scalar**) is the closed subset consisting of the integer Cores (`i8`, `u8`, `i16`, `u16`, `i32`, `u32`, `i64`, `u64`, `i128`, `u128`, `isize`, `usize`), floating-point Cores (`f32`, `f64`), `bool`, and `char`. `string`, Unit, and Never are not Scalars. Scalar is a specification category, not a source-level Type or Constraint name.

Scalar operations use the [generic code generation policy](#213-generic-code-generation). Its default automatic specialization preserves the selected implementation's meaning; it is distinct from an explicitly declared full specialization.

##### 3.1.1. Integer types

| Signed | Unsigned | Size |
| --- | --- | --- |
| `i8` | `u8` | 8 bits (1 byte) |
| `i16` | `u16` | 16 bits (2 bytes) |
| `i32` | `u32` | 32 bits (4 bytes) |
| `i64` | `u64` | 64 bits (8 bytes) |
| `i128` | `u128` | 128 bits (16 bytes) |
| `isize` | `usize` | Native pointer size of the target platform |

##### 3.1.2. Floating-point and boolean types

| Type | Size |
| --- | --- |
| `f32` | 32 bits (4 bytes) |
| `f64` | 64 bits (8 bytes) |
| `bool` | 8 bits (1 byte) |

The only valid `bool` storage representations are `0x00` for `false` and `0x01` for `true`. Reading any other bit pattern as `bool` is undefined behavior under the [valid-value requirement](#52-dereference-and-ownership).

##### 3.1.3. Character type

`char` represents one Unicode scalar value and has a fixed storage size of 32 bits (4 bytes). Its valid ranges are U+0000..U+D7FF and U+E000..U+10FFFF, inclusive. Surrogates (U+D800..U+DFFF) and values above U+10FFFF are invalid.

All scalars in these ranges are valid, including unassigned code points, private-use characters, noncharacters, controls, and combining marks; displayability is irrelevant. [Character literals](#28-character-literals) impose additional direct-spelling restrictions.

The size guarantee does not guarantee the same internal representation as `u32`. Alignment and byte order are not specified here.

##### 3.1.4. UTF-8 and strings

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
| `'€'` | U+20AC | `E2 82 AC` |
| `'😀'` | U+1F600 | `F0 9F 98 80` |

The source literal `'€'` occupies five bytes (`27 E2 82 AC 27`), including its three-byte UTF-8 content. Its value is U+20AC, stored as a four-byte `char`; encoded length and storage size differ.

`string` is the built-in Core for UTF-8 text. Its exact in-memory container and storage layout are implementation-defined. `owner/string` is always non-Copy; see [Copy capability](#351-copy-capability-and-explicit-duplication).

##### 3.1.5. Unit and Never types

`()` is the Unit type. It has one value and represents the absence of a meaningful result.

Unit has storage size 0, alignment 1 byte, and stride 0 under [layout terminology](#211-structure-layout-and-abi). It still has a logical value, initialization state, and lifetime. Materialized Unit places need no distinct address; this grants no permission to access a nonzero-sized Type there. ABI argument/result slots may be omitted or use implementation-defined physical padding without changing Unit's language storage layout.

Never has no values. `return`, `exit`, `continue`, `yield`, and `$abort(...)` have Type Never; transfer operands supply results to their targets. Other control-flow expressions infer Never only under [result validation](#149-result-validation), after checking all result sources and Structural Completion. Runtime Reachability alone cannot determine their Type or excuse a missing result. Never is a Type, not a [Completion](#141-completions): a transfer completes abruptly, divergence produces no Completion, and [Abort](#173-abort-termination) ends the process.

#### 3.2. Compound types

A named Core may be qualified with dots and may have generic arguments.

```kimi
A.B<T, U>
```

Tuple types use parentheses and commas: `()` is Unit, `(T,)` is a one-element Tuple, and `(T, U)` is a two-element Tuple. `(T)` groups a Type without adding a Tuple or changing its Semantics.

A Function Type consists of a parenthesized **Function Parameter List**, `->`, and a result Type. The list is a distinct grammar element, not a grouped or Tuple Type; it accepts an optional trailing comma. `->` associates to the right. A bare Type cannot replace the list.

| Function Type | Parameters |
| --- | --- |
| `() -> U` | None |
| `(T) -> U`, `(T,) -> U` | One of Type `T` |
| `(()) -> U` | One of Type Unit |
| `((T,)) -> U` | One of Type `(T,)` |
| `(T, V) -> U` | Two, of Types `T` and `V` |

```kimi
(i32, string)
(i32, string) -> bool
```

##### 3.2.1. Callable value types

A **Function Value** is a callable value. Distinguish its concrete Type from a common **Function Type** `(A1, ..., An) -> R`:

| Core | Identity and environment | Value capabilities |
| --- | --- | --- |
| Function Item Type | One resolved function declaration and instantiation; no runtime capture environment | Copy, Shared call; Owned when its bound arguments satisfy §15.2.3 |
| Concrete Closure Type | One anonymous-function expression and instantiation; stores its captures and internal call signature | Copy exactly when every capture's complete Type is Copy |
| Common Function Type | A shared calling contract and an owned, type-erased environment | Non-Copy, even for an empty or Copy environment |

Repeated evaluation of the same anonymous-function expression with the same type arguments produces the same anonymous Core; distinct expressions have distinct Types even with identical text and signatures. Each value retains its own Origin bindings. A Closure's environment is compiler-managed storage, not a user-accessible struct; no user-defined `deinit` can be added to the generated Type.

The initial common Function Type requires an `Owned` environment, exposes only Shared call, and cannot return a borrow dependent on its hidden environment receiver. Its arguments and results need not all be owned values. Concrete Closures retain their actual receiver and lifetime contracts; see [function expressions](#76-function-expressions) and [callable constraints](#86-callable-constraints).

Function Type Origin elision follows §15.4. Direct borrowed inputs bind Origins per call; results may depend on those Origins, independently of the owned environment's lifetime. Already-bound nested dependencies remain fixed.

```text
Function Item or concrete Closure
    ├─ keep the concrete Type, including through Callable generics
    └─ convert to a fixed common Function Type when its contract is satisfied
         └─ acquire and own the erased environment and its cleanup responsibility
```

An empty environment or `func []` does not imply purity, a function-pointer ABI, fixed size, no allocation, or concurrency safety. A borrow of an existing common function value uses ordinary Semantics: `ref/F` is Copy and `uniq/F` is Non-Copy. It neither erases a borrowed concrete Closure nor exposes additional call capabilities.

##### 3.2.2. Weak reference values

`Core.Weak<S>` is a compiler-known, Non-Copy owning value Core. Its complete Type argument S must have outer Semantics `rc` or `arc` after normalization, with an otherwise valid object View Target. Thus `Weak<rc/T>` upgrades to `Core.Option<rc/T>`, and `Weak<arc/T>` upgrades to `Core.Option<arc/T>`. A generic definition must prove this argument requirement. A bare payload Core, `obj/T`, an object borrow, or another Weak is not an eligible S.

Weak owns responsibility for weak-management storage, without strongly owning or borrowing the object payload. It introduces no new Semantics: outer `owner` and the existing category sets apply normally. `ref/Weak<S>` borrows the Weak value's storage. Empty and expired Weak values are still Non-Copy; ordinary acquisition Moves them, and duplication is explicit. Weak has compiler-managed storage and cleanup, without user-replaceable Fields or deinit.

Weak preserves S's complete View Type, ownership mode, and Type/Origin arguments. OwnedOrigins traverses S under the ordinary generic-argument rule, including for empty and expired values. No blanket Owned requirement is added to concrete Weak storage. Creation and use retain external Loan dependencies needed by later upgrades and their results, without a new long-lived Loan on the source handle's storage or a new exclusive Loan anchor. Automatic Weak cleanup observes only management storage, not the payload. Runtime expiration does not by itself erase static dependencies or justify a new lifetime refinement. Existing Owned erasure proofs remain required and preserved.

Weak does not directly expose payload Fields, member calls, object borrows, runtime `is`, or checked casts. First obtain a strong owner through [weak operations](#1359-weak-reference-operations), then apply the existing object rules. Weak view conversions are not introduced; change the strong view before creating Weak. Type-level non-Copy and Owned rules apply normally when a Weak value is captured in a Closure.

#### 3.3. Type semantics

Semantics prefixes associate to the right. An unparenthesized `from Origin` annotates the outermost layer. See [Type composition](#3-types-and-values) for the basic form and [nested Semantics](#336-nested-semantics-and-type-grouping) for layer boundaries and permitted combinations.

Only a declared Semantics binding may occupy a generic Semantics position. A pair parameter `<s/T>` binds one complete Type and exposes its outer Semantics and direct target; [generic Type parameters](#81-generic-type-parameters) define this correspondence. Syntax position alone never changes a parameter's kind.

In the syntax below, `T` denotes a Core; in object forms it may also denote a valid runtime-contract View Target.

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

The source-level Semantics categories are closed sets, as follows. These names occupy the Type namespace's Requirement role when the subject is a Semantics binding; they are neither Cores nor concrete Semantics prefixes. In that role their built-in meaning cannot be shadowed; elsewhere they remain ordinary contextual Names. A category cannot be written as a value Type or shorthand adaptation target.

A Requirement on a Semantics binding may also name any concrete Semantics listed above (`owner`, `ref`, `uniq`, `obj`, `rc`, `arc`, `objref`, `objuniq`, `unsafe`), testing equality with that Semantics. Concrete names and categories combine under the [Requirement expression rules](#83-requirement-expressions), as in `s is ref or obj`.

| Category | Members |
| --- | --- |
| value | owner |
| valueborrow | ref, uniq |
| object | obj, rc, arc |
| objectborrow | objref, objuniq |
| borrow | ref, uniq, objref, objuniq |
| owning | owner, obj, rc, arc |
| reference | ref, uniq, obj, rc, arc, objref, objuniq, unsafe |

`reference` includes every non-`owner` representation, including raw pointers; it establishes no safe-borrow guarantee. `s is borrow` requires an outer safe borrow; `s is owning or borrow` permits every outer Semantics except `unsafe`. These are outer-layer tests, not recursive guarantees about nested Types or payloads. Category `owning` is distinct from the recursive `Owned` guarantee. No category named `owned`, `counted`, `pointer`, `safe`, or `all` is introduced.

##### 3.3.1. Value ownership

`T` and `owner/T` are equivalent: both directly own a value with the data layout of `T`. In this section, owning a value or object describes its Semantics, not the recursive [Owned capability](#1523-static-and-owned).

##### 3.3.2. Value borrows

Value borrows provide non-owning access to value data, subject to lifetime constraints.

- `ref/T` is a shared borrow; multiple shared references may coexist.

- `uniq/T` is an exclusive mutable borrow; no conflicting reference may coexist.

##### 3.3.3. Object ownership

Concurrency and payload synchronization remain under the [deferred memory-model and thread-transfer design](#d2-concurrency-memory-model-and-thread-transfer).

An object retains its complete dynamic payload, type metadata, and ownership responsibility. These are logical roles, not a prescribed field order or allocation:

- `obj/T` is an exclusively owned object.

- `rc/T` uses non-atomic reference counting; the object remains alive while an owning reference exists.

- `arc/T` uses atomic reference counting. Atomic ownership management does not guarantee safe concurrent mutation of `T`.

Core [object ownership operations](#1358-object-ownership-creation-and-sharing) create these representations from owned values and explicitly duplicate `rc`/`arc` strong owners. Their release follows [object destruction](#1633-ownership-object-release-and-reentry).

##### 3.3.4. Object borrows

Object borrows provide non-owning access to objects, subject to lifetime constraints.

- `objref/T` is a shared object borrow; multiple shared references may coexist.

- `objuniq/T` is an exclusive mutable object borrow; no conflicting reference may coexist.

##### 3.3.5. Object views and identity

References to Contract targets in the object model describe the [runtime Contract extension](#85-runtime-contracts), outside the static Contracts defined in this revision. Concrete Core targets remain governed by the ordinary Object View rules.

```text
Object View Type = Object Semantics + View Target + Outer Origin (for object borrows)
Object Semantics = obj | rc | arc | objref | objuniq
View Target      = Core | runtime Contract View with fixed associated Types
```

An Object View Type is the complete static Type of an object handle or borrow. As in the [projection table](#811-slots-and-projections), `objref` and `objuniq` require an outer Origin, subject to elision; `obj`, `rc`, and `arc` have none. View Target and payload dependencies remain part of the complete Type. In `objref/Speaker`, the runtime contract `Speaker` is a View Target, not a Core with its own data layout. This extension does not create `owner/Speaker` or `ref/Speaker`.

The **Runtime Object Type** (also **Dynamic Type** or **Dynamic Core**) is the concrete Core actually constructed. Its [Runtime Type Identity](#2121-type-identity-and-descriptors) excludes the handle's outer Semantics and Origin bindings. Static Types still retain every lifetime dependency.

A completed object has exactly one Dynamic Type, unchanged throughout its lifetime. A view determines available operations; changing it preserves the entire object, identity, and destruction responsibility. Replacing metadata or reinitializing the object as another Type is forbidden. A new object later placed at the same address has a new lifetime and inherits no identity, Loan, or flow facts.

**`Supports(D, V)`** relates a concrete Core `D` to a View Target `V`. It holds exactly when either:

- `V` is `D` itself or a direct or indirect base Core of `D`; or
- `V` is a runtime Contract View `C` with fixed associated Types and both `RuntimeUsable(C)` and `Implements(D, C)` hold under [runtime contracts](#85-runtime-contracts).

Upcasts, view-support tests, checked casts, and metadata share this relation. It does not grant access, ownership, `Owned`, Origin/Loan validity, or permission to invoke an incompatible ordinary member. Generic element relationships do not imply container covariance. Core inheritance alone does not establish substitutability of complete Types: no slicing, implicit `owner/Dog -> owner/Animal`, ordinary `ref/Dog -> ref/Animal`, or `uniq/Dog -> uniq/Animal` conversion is introduced. Object upcasts are explicit operations; [inherited receiver projection](#951-base-subobject-receiver-projection) supplies only the receiver of a selected inherited member.

```text
objref/Animal from source       Dog object
    ├─ public view: Animal         ├─ Animal state
    ├─ shared access               ├─ Dog state
    └─ Origin: source              └─ Dog type and destruction information
```

Object ownership does not require heap allocation. Representation choices must preserve identity, access, lifetime, and cleanup.

##### 3.3.6. Nested Semantics and type grouping

`ref/V`, `uniq/V`, and `unsafe/V` may take a complete value Type `V`, including its own Semantics and Origins, as their immediate **Referent Type**. The outer layer refers to storage holding a value of `V`; it does not replace `V`'s Semantics or refer directly to its eventual referent.

```kimi
ref/ref/T                           // Shared borrow of a shared-reference value.
ref/uniq/T                          // Shared borrow of an exclusive-reference value.
uniq/ref/T                          // Exclusive borrow of a shared-reference value.
ref/obj/T                           // Borrow of object-handle storage, not objref/T.
unsafe/ref/T                        // Raw pointer to shared-reference storage.
ref/(ref/T from inner) from outer    // Separate inner and outer Origins.
```

Prefixes associate rightward: `ref/ref/T from outer` annotates only the outer reference. Parentheses group complete Types and permit per-layer annotations. `ref/((i32) -> bool)` borrows a function value; `(ref/i32) -> bool` takes one borrowed integer. `ref/(i32) -> bool` is invalid because a Function Type needs its own parameter list. Grouping adds neither Tuple nor borrow.

Expand aliases and preserve every Type layer for identity, applicability, layout, and Origin analysis: `ref/ref/T` differs from `ref/T`. Grouping and redundant `owner` prefixes normalize away, but `owner/V` preserves V’s references and Object Semantics. Object Semantics require a supported Core or runtime-contract View Target, not an already Semantics-applied Type: `ref/obj/T` is valid, while `obj/ref/T` does not box a reference. Generic substitutions obey the same rules; see [generic slots](#81-generic-type-parameters).

Each Type layer retains its Origin dependencies under [position-specific elision](#154-origin-elision-and-return-contracts); an outer annotation cannot replace an inner one. In parameter Types, only an outer direct borrow may introduce an implicit input Origin; inner borrow Origins and aggregate Origin arguments must be explicit. Local initializers may infer all layers; instance Fields require explicit bindings throughout. The examples above illustrate composition, not unrestricted Origin omission.

At each safe value-borrow layer, all observable Origins of `V` must outlive that layer (`inner : outer` in the last example). Grouping or redundant `owner` cannot add a conflicting annotation to one normalized layer. Raw pointers establish neither safe-reference validity nor longer lifetimes.

Outer Semantics determines Copy: ref/uniq/T is Copy, uniq/ref/T is not. Copying an outer shared reference does not duplicate the inner exclusive capability. Shared access cannot Move a non-Copy inner value, use its uniq exclusively, or mutate the slot; shared Reborrow stays within the outer Loan. Moving/destroying an outer reference leaves its referent intact. Valid exclusive slot replacement must preserve inner Type, Origin, and Loan constraints.

This syntax adds no implicit repeated dereference, safe `*` operator, or pointer/safe-reference conversion. Borrow formation uses the explicit rules in [Borrow and Reborrow](#1355-explicit-borrow-and-reborrow).

#### 3.4. Values, places, and storage

**Storage** is a region holding values. **Destruction responsibility** is the obligation to destroy an owned value at the end of its lifetime, subject to [cleanup and termination](#16-scope-exit-and-destruction).

A **Place** is a storage location that can hold a value. A **Place expression** designates it; parentheses preserve the classification. A Place is distinct from a **Temporary Value**, even when that result is materialized into a separate [Temporary Place](#36-temporary-values-places-and-lifetimes).

An **Access Designator** identifies an access target after Name/Type resolution: a local or parameter, Field, computed/required Property, Tuple element, index, or built-in dereference. It includes computed Properties and user indexers without promising storage or Consume permission.

```text
Access Designator -> operation-specific resolution
                    ├─ Place access
                    ├─ value acquisition, such as a getter result
                    └─ error
```

These are resolution outcomes, not fallback stages. A failed direct Move cannot become a borrow or getter call. Standard Property get exposes permitted Place operations; custom/computed/required get produces its declared result (§11). Permitted function references produce function values. Classify by resolved operation, not spelling; parentheses preserve the category.

An ownership Move transfers ownership and destruction responsibility, preventing a second destruction at the source. Borrow-value Copy/Move duplicates or transfers access capability without owning its referent; destroying a borrow does not destroy the referent. Destroying `rc/T` or `arc/T` releases an owning reference under object-lifetime rules. A non-owning `unsafe/T` neither destroys its referent nor frees its storage.

Initialization places a value in an empty place. Destruction ends a value's lifetime and responsibility; if its storage remains, that place becomes Uninitialized after normal completion. No access is possible after the storage's lifetime ends.

Value Lifetime separates acquisition (Copy / Move), placement (Initialization / Replacement), and Destruction. One assignment may contain both Move and Replacement.

The [initialization-state rules](#1511-storage-state-and-responsibility) define Initialized, Uninitialized, and Moved. A **Partial Move** transfers an inline part and leaves an aggregate incomplete; supported paths and permissions follow [Move Paths](#1513-move-paths-and-partial-move).

#### 3.5. Copy and move

**Copy** implicitly duplicates a value, leaves its source Initialized, and preserves the source's destruction responsibility. It executes no user-defined code, heap-allocating duplication, reference-count change, or resource acquisition. Copying a reference does not copy its referent.

**Move** transfers the value and destruction responsibility, or a borrow value's access capability, and marks the source Moved. It invokes no user code and need not clear source memory.

Ordinary value acquisition selects Copy for a Copy Type and Move otherwise; reject an unavailable operation. This covers initialization, assignment sources, by-value arguments, and result transfers. A non-Copy owned value used in a consuming context Moves, and a value can Move out only from a Movable Place (§15.1.5). Borrow creation/reborrowing is separate. No explicit operator forces a Copy value to Move.

Copy capability is independent of `let`/`var` and flow-dependent Loans. At use, Copy obeys read restrictions and Move must not conflict with overlapping active Loans. Both preserve Origin dependencies without extending referent lifetimes. Reborrowing does not make exclusive references Copy.

```kimi
let a: i32 = 10
let b = a                 // Copy; a stays Initialized.
var node: obj/Node = makeNode()
let owned = node          // Move; node becomes Moved.
// use(node)              // Error until reinitialized.
node = makeNode()
```

##### 3.5.1. Copy capability and explicit duplication

`Copy` is a [compiler-intrinsic Contract](#847-intrinsic-contracts-and-guarantees). User-defined Contracts with the same shape do not grant Copy acquisition semantics.

Classify complete Types using Core, Semantics, and stored components:

| Type | Classification |
| --- | --- |
| Integers, floating-point values, `bool`, `char`, Unit under `owner` Semantics | Copy |
| `ref/T`, `objref/T`, `unsafe/T` | Copy regardless of referent `T` |
| `uniq/T`, `objuniq/T` | Non-Copy |
| `obj/T`, `rc/T`, `arc/T` | Non-Copy even if `T` is Copy |
| Slice<T> from source | Copy shared handle regardless of T; no exclusive-element Slice is introduced |
| Index, Range, ResolvedRange | Copy |
| Function Item | Copy |
| Concrete Closure | Copy exactly when every captured complete Type is Copy; empty environments qualify |
| Common Function Type under `owner` Semantics | Non-Copy regardless of its hidden environment |
| `Core.Weak<S>` under `owner` Semantics | Non-Copy, including empty and expired values; explicit duplication retains weak-management storage |
| Tuple / fixed-length array under `owner` Semantics | Copy exactly when every component Type is Copy |
| User-defined struct under `owner` Semantics | Non-Copy unless explicitly opted in |
| Enum under `owner` Semantics | Non-Copy unless explicitly opted in under [enum derivation](#352-enum-copy) |
| `owner/string` | Non-Copy regardless of its internal representation |

Never has no values and needs no classification. Other Types require their own rules; sharing elements alone does not establish Copy.

`Copy` is compiler-checked. A struct opts in with `Self is Copy`, or conditionally with `Self is Copy when P` (§8.4.8). Under the Type Constraints and any declared conformance condition, every complete own Field Type and the direct base must be Copy, and the struct must have no user `deinit`. Checking the inline base covers inherited storage and destruction. Each derived struct opts in separately; open structs use the same rules. Computed members contribute no fields. All-Copy fields alone do not opt in, users cannot define Copy bodies, and Copy preserves the exact owning Type without slicing. Active Loans affect use, not Type classification.

```kimi
struct Point
    Self is Copy
    var x: i32
    var y: i32

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
```

`T is Copy` is a generic constraint. Do not assume Copy before constraints or instantiation establish it. User-struct derivation is specific to `Self is Copy`, not a general consequence of `Self is Capability`. Compiler-generated Closures instead use the table's automatic rule.

Unknown Copy capability follows [Generic Access Effects](#89-generic-access-effects), preserving conditional acquisition plans and separate shared element-read rules.

Duplication requiring allocation, reference-count increments, or resource duplication uses explicit operations. Core defines [strong-owner duplication](#1358-object-ownership-creation-and-sharing) for `rc`/`arc`; general duplication contracts and final API spellings remain unspecified.

```kimi
let text: string = "Hello"
let copy = text.clone() // Illustrative explicit duplication API.
let moved = text        // Move; text is no longer usable.
```

##### 3.5.2. Enum Copy

`Self is Copy` requests unconditional compiler-derived enum conformance; `Self is Copy when P` requests it under P (§8.4.8). Every complete payload Type in every Case must be Copy under the applicable premises. Payload-free enums also require opt-in. Users cannot supply a Copy body. Copy duplicates the active Case and payload without user code; Move transfers the whole enum responsibility.

```kimi
enum Direction
    Self is Copy
    North
    South

enum CopyOption<T>
    Self is Copy when T is Copy
    Some(T)
    None
```

Derivation must hold for every generic binding admitted by the Type Constraints and the conformance condition, if any. It never invents parameter constraints. `CopyOption<i32>` is Copy, while `CopyOption<Resource>` remains usable but is non-Copy when Resource is non-Copy. An unconditional declaration without a sufficient premise is invalid. An enum storing only `ref/T` needs no T-is-Copy premise for derivation, but any explicitly written condition still applies.

Proof may depend on declared constraints, but successful individual instantiations do not validate an otherwise unproven declaration. [Generic Access Effects](#89-generic-access-effects) may defer exact effect determination only after proving legality for every admitted case; unknown Copy is never treated as non-Copy.

#### 3.6. Temporary values, places, and lifetimes

##### 3.6.1. Materialization

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

A newly owned temporary has exclusive writable capability over its whole Temporary Place unless another rule restricts access; it needs no `let`/`var` binding. Getter-owned result temporaries have the additional restrictions of [getter results](#1123-getter-results-and-temporaries). Materialization realizes this capability without upgrading borrows, granting referent or Property permissions, ignoring readonly parts, or bypassing Loans, Origins, construction, or `deinit` conditions.

##### 3.6.2. Lifetime and borrowing

Unless a construct needs a longer lifetime, a temporary lasts until the outermost expression that created it finishes. Argument temporaries last through the call; iteration sources and `match` subjects last for their required use. Destroy remaining temporaries in reverse creation order. After a Move, the transferred value follows its destination's lifetime, while the original Temporary Place keeps its original lifetime and destroys only remaining Initialized parts.

Each if, else-if, while, require, and match-guard test is a temporary-lifetime boundary: secure its bool result, clean condition temporaries and guard-local temporary Loans, then branch under [condition evaluation](#1423-conditions-and-temporary-lifetimes). Explicit bindings, match subjects, and iterators retain their own scopes; moved values retain destination lifetimes. Store a needed value outside the condition to keep it alive through the body.

New borrows of owned temporaries depend on their Temporary Places and cannot outlive them. Exclusive capability permits the applicable explicit exclusive borrow; it does not bypass the [Borrow table](#1355-explicit-borrow-and-reborrow).

```kimi
inspect(makeResource()@ref)
modify(makeResource()@uniq)
let taken = resource // Non-Copy Resource moves to taken.
inspect(taken@ref) // Borrow the destination; resource remains Moved.

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

Borrow and Slice formation never extend the source's lifetime. Result transfers secure values before common [scope-exit cleanup](#162-scope-exit-destruction); Abort follows [Error Handling](#173-abort-termination).

#### 3.7. Origins and loans: overview

Kimigayo uses **Origins** instead of lifetime variables. An Origin describes how long a borrow remains valid; a **Loan** records which place is borrowed and whether the borrow is shared or exclusive.

The relation `a : b` means that `a` outlives `b`, including equality: `region(a) ⊇ region(b)`. Source declares bounds within an [Origin parameter list](#153-abstract-origins); the same notation is used for derived facts under [Origin ordering](#1522-ordering-and-intersection).

Origin annotations appear in signatures and type declarations. Local Origins may be inferred from initializers and ordinary Origin/Loan constraints. Omission is permitted only by the [position-specific rules](#154-origin-elision-and-return-contracts): direct borrowed inputs introduce input Origins, results use conservative elision, and instance Fields require explicit Origin bindings. Static Fields require `Owned` and the [static-source rules](#1132-static-storage).

The safe value-borrow semantics are:

```kimi
ref/T from o   // shared, immutable, and aliasable
uniq/T from o  // exclusive and mutable
```

`uniq/T` is not implicitly copyable and cannot coexist with another overlapping borrow. The corresponding object-borrow semantics, `objref/T` and `objuniq/T`, follow the same shared and exclusive rules. This section uses `ref` and `uniq` in examples.

When `from o` is omitted, [Origin elision](#154-origin-elision-and-return-contracts) determines the Origin.

#### 3.8. Type relations and expression operations

Type relations and expression operations are distinct even when optimized away: Borrow, Reborrow, Copy, and Move remain value operations. A Type relation alone authorizes no acquisition. This table summarizes the linked rules without adding conversions, adaptation chains, or overload preferences.

Here `A <: B` includes normalized identity and the explicitly defined subtype rules. Complete Types retain Semantics, nested Types, generic arguments, and Origin bindings. Unresolved generic or Origin information retains constraints for later resolution; it is not evidence that Types are identical or compatible.

| Relation or operation | Inputs and normative condition | Effect and boundary |
| --- | --- | --- |
| Normalized Type identity | Two complete Types. Expand aliases and normalize grouping and redundant owner prefixes; compare the resulting Type structure, declaration identity, generic arguments, Semantics, and Origin bindings. Bound Origin parameters correspond by binder, not spelling. | No value operation. Different nominal declarations do not become equal through matching names, fields, or layout. This is not the Origin-erasing Runtime Type Identity used by object views. |
| Alias equivalence | An alias and its resolved target, with substitutions and complete Type information preserved. | Participates in normalized identity; no wrapper, conversion, or ownership change is introduced. Alias lookup still follows ordinary visibility and lookup rules. |
| Subtyping | Two complete Types, under established generic and Origin constraints. Prove identity or a subtype relation explicitly defined by this specification. | Static fitting only. Do not insert acquisition, Borrow/Reborrow, dereference, numeric conversion, object upcast, or user conversion as part of the proof. |
| Origin shortening and variance | Apply [Origin variance](#1532-variance) and outlives constraints at each relevant position. Covariance permits shortening, contravariance reverses the relation, and invariance requires equality. | A subtype proof, not a new borrow. Preserve existing dependencies and Loans; do not extend lifetime or replace inner Origins with an outer annotation. Exclusive Referent Type invariance remains mandatory. |
| Callable signature compatibility | For implementation `(A1, ..., An) -> R` and requirement `(P1, ..., Pn) -> Q`, apply [callable compatibility](#107-callable-signature-compatibility): equal arity, `Pi <: Ai`, `R <: Q`, and compatible Origin/Loan contracts. | Static signature fitting. It does not insert argument/result operations or itself convert a Function Item or Closure to a common Function Type. Receiver and environment requirements remain separate. |
| Expected-result compatibility | An instantiated candidate result Type and an independently established expected Type, under [expected-result filtering](#103-expected-results). Require identity or a defined subtype relation. | Excludes candidates without inserting a value operation. Result acquisition and declared Loan propagation remain required; this is not general implicit expression adaptation. |
| Never fitting | Never has no normally produced value and fits any otherwise valid expected value Type without a value conversion. | No outer value operation executes on a non-completing path. Preserve target, Unsafe, and local correctness checks, and validate transfer operands against their own result boundary under [result validation](#149-result-validation). Keep the Target Result Type separate from inferred Never; check every syntactic result source under §14.9. |
| Implicit expression adaptation | An expression, target Type, and use-site context. Select only adaptations allowed in that position, including the finite [argument adaptation rules](#102-argument-adaptation-and-literals) and fixed-expectation [common function conversion](#764-function-references-and-common-type-conversion). | May require acquisition, Borrow/Reborrow, or a defined conversion. Literal fitting determines an unresolved literal's Type; it does not convert an established numeric Type. No universal implicit-conversion search is permitted. |
| Explicit expression adaptation | An expression and resolved Adaptation Target in context. Select one [defined `@` operation](#1353-defined-adaptations), then enforce its requirements and static result fitting. | May change value representation, view, or Loan state, or perform runtime checks. No hidden sequence of operations is inserted. Origins are inferred as specified for Adaptation Targets. |
| Object upcast | An expression and different object View Target with proof of `Supports(S, V)` and a matching [explicit upcast row](#1357-object-upcasts). | One explicit view/acquisition operation. Inheritance or conformance alone does not establish an implicit complete-value adaptation or callable argument/result conversion. |
| Base subobject receiver projection | An instance member selected in a base layer by [inherited lookup](#951-base-subobject-receiver-projection). | Locate its receiver subobject and apply permitted receiver access; no standalone conversion or owning base value. |
| Borrow / Reborrow | An expression with the required Place, access, and Loan properties, and a permitted implicit or explicit borrow operation. | Establish or derive Loans under the borrow rules. Changing `uniq/T` to `ref/T` requires shared Reborrow; it is not a subtype rule. |
| Numeric conversion | An established numeric value Type and a target admitted by the [explicit numeric conversion table](#1354-numeric-conversions-and-literals). | A value conversion with the specified rounding, range checks, and failure behavior. `i32` is not a subtype of `i64`; representable literal fitting is separate. |
| Acquisition legality | An expression, selected access operation, and current initialization/access/ownership/Loan state. Apply ordinary Copy, Move, Borrow, or Consume requirements. | Type compatibility does not prove legality. An exact Type match can still fail because storage is Moved, access is unavailable, or a Loan conflicts. Failure does not reopen committed lookup or overload selection. |

For example, `ref/T from longer <: ref/T from shorter` may hold when `longer : shorter`, without establishing a new Loan. In contrast, `uniq/T` to `ref/T` needs Reborrow, and an object view change needs its explicit upcast operation. Same normalized Type does not force an identity operation: explicit same-Type exclusive adaptation still selects Reborrow, and ordinary by-value acquisition may Copy or Move.

Candidate analysis may record operation choices and unresolved obligations, but must not commit source-state changes while testing candidates. After selection, enforce the chosen acquisition and adaptation under the specified evaluation order, and apply static result fitting without replacing that operation. The [implementation correspondence](#b5-type-relation-and-operation-plans) is informative; no particular internal API is required.

### 4. Arrays, indexing, and slices

Fixed arrays use `[N of T]`, where N is a compile-time length and T is a complete element Type, including Semantics and Origins. Dynamic `Array<T>` owns variable-length storage; `Slice<T> from source` is a shared borrowed view. Their [indexing and slicing](#46-indexing-and-slicing) rules are defined together.

#### 4.1. Fixed-array identity and layout

Type identity includes the evaluated length and complete element Type. Thus `[3 of i32]` differs from `[4 of i32]`, while `[(2 + 2) of i32]` equals `[4 of i32]`. Arrays of different lengths, fixed arrays and Array, and owning arrays and Slice have no implicit conversions.

Elements are stored inline in index order, without implicit heap allocation for the array's own element storage. Nested arrays apply the rule recursively, with the innermost index varying contiguously. The enclosing value's storage location and allocations performed by individual elements follow their own rules.

Require a finite, valid element layout. Let d = stride(T); Slice uses the same element spacing. These are specification quantities, not source operators, and follow the [common layout rules](#211-structure-layout-and-abi).

| Quantity | Layout of `[N of T]` |
| --- | --- |
| Element i offset | i * d, for 0 <= i < N |
| Alignment | alignment(T), including when N = 0 |
| Array stride | N * d, the spacing between consecutive array values |
| Size | N * d, including all element-stride padding |
| Zero-sized elements | Use T's stride; if d = 0, distinct logical elements may share an address |

Check size, stride, and padding calculations against target layout limits and nonnegative isize. An unrepresentable concrete layout is a compile-time error. `[0 of T]` has size and stride zero and initializes/destroys no elements, but still checks T's Type, layout, and ownership. Validate inline embedding before multiplying by zero: a struct directly storing `[0 of Self]` has forbidden recursive value layout. Pointer and reference referents do not create inline edges.

#### 4.2. Length constants

A length is a nonnegative compile-time integer representable by the target's isize. A generic length remains symbolic until instantiation. Write an integer literal, a possibly qualified constant name, a length parameter, or a parenthesized integer constant expression. Any compound expression requires parentheses around the entire length.

The initial evaluator admits integer literals, length parameters, **Constant-readable Bindings**, grouping, unary `+ -`, and binary `+ - * / %`. A Constant-readable Binding is an integer `let` local or static stored Property with accessible standard get whose declaration initializer can be recursively evaluated using only these forms, after normal lookup, access, and initialization checks. Parameters, `var`, instance Fields, custom/computed accessors, calls, and cyclic initializers are excluded. This is a semantic classification, not general constant treatment of `let` or new `const` syntax.

Length evaluation uses checked integer arithmetic. Resolve typed constants and length parameters (isize) first. Established operand Types guide unresolved literals and literal-only subexpressions under same-Type arithmetic; absent such evidence, default them to isize. Typed constants keep their Type. Different established integer Types are incompatible even if their widths or values match; the final nonnegative-isize range check does not convert operands. Intermediate overflow uses the arithmetic Type, including i32 inferred for an ordinary binding.

Signed intermediate values may be negative. A negative or out-of-range final length, noninteger value, overflow, or zero divisor is a compile-time error. Evaluation is optimization-independent and does not expand ordinary or directive constant evaluation. Type formation runs no static initializer or getter.

```kimi
let Width: isize = 4
let Height: isize = 3
let buffer: [(Width * Height) of u8] // Length 12; still Uninitialized.
let row: [Width of u8] = [1, 2, 3, 4]
let badSyntax: [Width * Height of u8] // Error: parentheses required.
let negative: [(-1) of u8]           // Error: negative length.
let invalid: [(4 / 0) of u8]         // Error: division by zero.
let of = 4                          // Ordinary Name outside the Type delimiter.
```

For an ordinary `let Small = 4`, Small is i32, so `[(Small * 2) of u8]` computes in i32 and then checks the final length range. Combining Small with an independently typed isize operand is invalid; annotate length constants as isize at their declarations when native-width arithmetic is intended. Length expressions introduce no extra conversion syntax.

Separate-compilation artifacts retain folded integer values and Types, or verified slot-dependent expressions and formation obligations. Record referenced declaration Identities and dependencies; changes invalidate dependent artifacts and recompute Type, layout, and selection keys. Length changes have no ABI compatibility guarantee. Check constant accessibility at the definition: a private constant need not become public when its value can be exported without private-name lookup. Expand private constants there and export conditions checkable by clients. Ordinary API accessibility still applies to element Types and other signature contents.

The root-level bindings above are implicit startup locals (§22.2), even when Constant-readable. In a Library, an explicit-main Application, or a declaration-only document, put reusable constants in a group:

```kimi
group Dimensions
    public let Width: isize = 4
    public let Height: isize = 3
```

Use `Dimensions.Width` in a length expression. Compile-time reading does not run its static initializer; runtime access retains ordinary first-access initialization.

A private constant's expanded value becomes part of any public API Type or formation condition that uses it. Private access protects the declaration's Name, not secrecy or compatibility of an exported value. Changing that value may break callers and requires dependent invalidation/rebuilding. This exception is intentional: a folded length contains an integer value rather than a private Type or a client-side reference to the private declaration. No compatibility warning is mandated; an implementation may offer an API-change diagnostic.

#### 4.3. Initialization and inference

With an expected fixed-array Type, an array literal constructs that Type and must contain exactly N elements; no padding or truncation is allowed. Acquire elements in source order by ordinary Copy/Move. A local binding annotation may use `[N of _]`, recursively for nested arrays, to infer only the element Type from its initializer. Require a unique Type at declaration. This placeholder is forbidden in lengths, signatures, and explicit generic arguments.

Without a fixed-array expectation, an independent array literal constructs Array. Call-argument literals remain subject to candidate-local fitting under [length-argument inference](#44-function-length-parameters); do not default them to Array first. Empty literals require an expected element Type. Numeric element defaults follow ordinary inference.

An independent literal may form an Array with safe-borrow elements. Infer and preserve complete element Types and their Origins under the ordinary local rules; apply permitted Origin shortening without merging distinct Loans. A fixed-array expectation still selects a fixed array, for example `let views: [2 of ref/i32] = [x@ref, y@ref]` for initialized integer locals x and y.

An annotation-only declaration remains Uninitialized: no zero fill or default element construction occurs. Initial construction requires a whole-array initializer or one whole-array assignment; element-by-element writes into an unconstructed array are forbidden. After completed construction and Partial Move, missing elements may be reinitialized through eligible static Move Paths with ordinary write permissions. Whole-value reads and borrows require completeness.

**Construction boundary.** Fixed arrays have no fill/repetition, generator, or default construction. Construct a literal of exactly N elements or acquire a complete array from an input/result. A declaration `[N of u8]` alone yields no usable buffer, even for known large N. Generic functions may still inspect/mutate supplied initialized arrays. Future fill/generator rules must define evaluation counts, Copy versus repeated construction, zero length, and partial cleanup.

```kimi
let a: [4 of i32] = [1, 2, 3, 4]
let inferred: [4 of _] = [1, 2, 3, 4] // [4 of i32]
let dynamic = [1, 2, 3, 4]            // Array<i32>
let empty: [0 of i32] = []
let wrong: [3 of i32] = [1, 2]        // Error: element count.
let unknown: [0 of _] = []            // Error: unknown element Type.
let pending: [2 of i32]
pending = [1, 2] // Whole initial construction; pending[0] = 1 cannot construct it.

let matrix: [3 of [4 of f32]] = [
    [1.0, 2.0, 3.0, 4.0],
    [5.0, 6.0, 7.0, 8.0],
    [9.0, 10.0, 11.0, 12.0],
]
let cell = matrix[1][2] // f32 value 7.0; each dimension is checked separately.
```

#### 4.4. Function length parameters

Declare a length slot as `<length N>`. A plain `<N>` remains a Type slot; neither signature use nor the body infers a slot's kind. `length` is contextual only at the start of a generic parameter declaration followed by its Name. It does not change ordinary names or `.length`.

First bind the declaration list's kinds, order, and names and reject duplicates; then bind the signature and body. A length slot is a nonnegative isize constant in the Value namespace, not a complete Type or a Semantics/Type pair. Diagnose kind misuse at the use and identify its declaration. Only function length slots are introduced: no value parameters on Type declarations, general Const generics, or source length-constraint syntax.

```kimi
func process<length N>(values: [N of i32])
    let count: isize = N
    ()

func keep<length N, T>(values: [N of T]) -> [N of T] => values
func work<length N>()
    let buffer: [N of u8] // Formation-only example: Uninitialized, not a usable buffer.
    // No fill/default construction exists; obtain a whole initialized value to use it.

let a: [4 of i32] = [1, 2, 3, 4]
process(a)          // N = 4
process([1, 2, 3])  // N = 3
process<4>(a)      // No length keyword in the argument list.
process<3>(a)      // Error: length mismatch.
let result = keep<4, i32>(a)
work<4>()          // A body-only length must still be supplied.
```

Within each overload candidate, bind explicit arguments first, then infer length and element Type from known fixed-array Types and literals fitted to that candidate. Evidence from all arguments must agree; do not retype an already established Array value. Explicit lists supply all slots in order, using a length constant expression as defined in §4.2, without the length keyword, for length slots and complete Types for Type slots.

Do not solve length equations backward: check `[(N * 2) of T]` after N is known. Expected Types may fill unresolved parts without changing input-established Types or lengths. Evidence may come from a binding annotation, typed assignment destination, declared result, or known parameter. Nested calls and anonymous functions obey [inference boundaries](#105-inference-boundaries-and-specialization); explicit `@` and result constructs follow [adaptation inference](#1352-static-selection-and-inference) and [result validation](#149-result-validation). Do not infer a callee's length arguments from its implementation body, later uses/member accesses, guessed unresolved sibling expressions, or candidate order.

```kimi
func consume<T>(x: Array<T>) => ()
func consume<length N, T>(x: [N of T]) => ()
consume([1, 2, 3]) // Error: both fit and neither candidate is better.
```

Fixed and dynamic array literals receive no automatic preference beyond ordinary candidate comparison.

This ambiguity is intentional when the candidates otherwise tie; it does not prohibit offering both overloads. Use a typed intermediate (`let fixed: [3 of i32] = [1, 2, 3]` or `let dynamic: Array<i32> = [1, 2, 3]`), distinct function names, or an explicit generic argument list whose kinds uniquely select a candidate. Array's default for independent expressions is not an overload-ranking rule.

**Formation obligations.** Each signature length expression E generates `ValidLength(E)`: the indivisible compiler obligation that typed E evaluates with checked integer operations to nonnegative isize. Logical expansion or minimization is not required. A nondependent failure rejects the declaration; a dependent obligation is a public applicability condition. For example, N - 1 needs N >= 1 and N / M needs M != 0, without imposing nonnegativity on all intermediate values.

At concrete calls, check these obligations after inference and before candidate comparison; false obligations eliminate candidates. Their strength neither ranks candidates nor solves lengths backward. A representation failure discovered after selection, such as an excessive concrete layout, cannot reopen overload selection.

Verify the body for every binding satisfying its public conditions. Body lengths and callee conditions must follow from the declaration or produce a definition error; do not defer semantic checking to convenient instantiations or infer extra applicability conditions from the body. Length proof is limited to concrete evaluation, the inherent nonnegative-isize property of length parameters, and reuse of identical normalized formation obligations. Symbolic physical layout remains an ordinary instantiation-time representation obligation.

For signatures, dependent fixed-array Types, and formation obligations, normalize typed length expressions bottom-up:

1. Remove grouping, evaluate nondependent subexpressions with checked arithmetic, and replace parameter Names with their declaration slot positions.
2. At each binary integer `+` or `*`, sort the two normalized operands by a deterministic structural order using node kind, Type Identity, constant value, and bound Symbol/slot Identity.
3. Preserve all Types/operators and the operand order of `-`, `/`, and `%`. Do not flatten, reassociate, distribute, or cancel.

With matching slots and integer Types, `N + 4`, `N + (2 + 2)`, `((N + 4))`, and `4 + N` coincide, as do `N * M` and `M * N`. `(N + 2) + 2` need not equal `N + 4` because intermediate overflow matters. This normalizes pure length expressions only; it does not reorder runtime evaluation. Concrete Type and full-specialization keys use evaluated lengths. Each length slot counts once in GenericArity.

```kimi
func reordered<length N>(value: [(N + 4) of u8]) -> [(4 + N) of u8] => value
// Valid: both the dependent Type and ValidLength obligation normalize identically.
```

#### 4.5. Operations and ownership

Fixed arrays and Array expose public read-only `length: isize` and `indices: ResolvedRange`, with the [metadata acquisition rules](#461-access-and-length-metadata). Fixed arrays have no resizing operation. Element writes obey ordinary `var`, `let`, and borrowed-access permissions.

A fixed array is Copy exactly when its complete element Type is Copy. Derive Owned and retain element Origins/Loans recursively. Partial Move follows [Move Paths](#1513-move-paths-and-partial-move). [Aggregate cleanup](#1632-field-cleanup) destroys remaining initialized elements in decreasing index order, including abandoned construction on ordinary control transfer; skip Uninitialized/Moved parts and recursively clean partly built elements. Abort does not guarantee cleanup.

Array is Non-Copy and accepts any valid complete element Type with representable element layout; neither Owned nor Copy is required. Its Type preserves T's Origin dependencies and Loan requirements, and its values preserve the acquired elements' Loans under [ordinary storage](#154-origin-elision-and-return-contracts). To obtain a shared view of either owning array form, explicitly slice it.

The element position preserves Origin variance; compose nested variance normally, while `uniq/Array<T>` remains invariant in its complete Referent Type. This adds no covariance between different element Cores. Structural mutation operations, when supplied by Core, require exclusive access to the Array as a whole. Their incompatibility with active element/storage views follows ordinary Loan overlap, independently of whether reallocation actually occurs. Ordinary indexing does not gain a Non-Copy Move operation.

Array analysis retains a conservative set of element-originated Loans. Removing or replacing an individual element, including clearing the Array, supplies no element-specific proof that a Loan ended; retain dependencies required by subsequent uses and observable destruction of the Array and derived values. A removal operation transfers the result's dependencies as well. Neither emptiness nor mutation changes the declared T or its Owned classification.

**Mutation boundary.** Slice has no mutable/exclusive-element form in this revision. Mutate initialized array elements through authorized access to the owning array or a `uniq` borrow of the whole array; `uniq/Slice<T>` changes only the handle, never the element permissions. To process a mutable subrange, pass a whole-array exclusive borrow plus bounds and index the array, or iterate its saved indices while accessing each element. A non-lending iterator does not prohibit such indexed mutation. Bounds validation, active Loans, and ordinary initialization checks still apply; no disjoint mutable subviews are implied.

```kimi
var values: [4 of i32] = [10, 20, 30, 40]
values[1] = 25
let count = values.length
let middle: Slice<i32> = values[1..3] // Infer the borrow of values' storage.
```

#### 4.6. Indexing and slicing

##### 4.6.1. Access and length metadata

These built-in operations apply to `[N of T]`, `Array<T>`, and `Slice<T>`. Element indexing accepts isize or Index; range indexing accepts Range or ResolvedRange. Dictionary indexing instead takes keys. [Raw pointers](#53-pointer-arithmetic-and-indexing) retain signed-isize offsets without safe sequence bounds checks and accept neither Index nor Range. String indexing units and user-defined indexers are not introduced.

| Core | Meaning |
| --- | --- |
| Index | Copy, Owned position measured from the start or end; retains no target |
| Range | Copy, Owned unresolved boundaries and end-inclusion flag; not Iterable |
| ResolvedRange | Copy, Owned validated absolute half-open interval; finite isize iteration |
| `Slice<T>` from source | Copy shared view, independent of T's Copy capability; retains backing Origin and shared Loan |

These names are not keywords; `::Core.Index`, for example, disambiguates a hidden alias. Prefix `^` and range syntax always construct the designated Types from the Core Kotonoha, never same-named user Types.

**Length metadata.** Fixed arrays and Array provide length and indices; Slice also provides isEmpty. Evaluate the receiver once and require ordinary initialization, completeness, and access legality. Knowing a fixed length does not erase receiver effects or checks.

| Receiver | Acquisition |
| --- | --- |
| Fixed array | Check shared access and use Type-level N without reading elements |
| Array | Share access during the operation and read its current length |
| Slice | Copy the handle and read its stored length, without reading backing elements |

Returned integers, Booleans, and ResolvedRange values acquire no receiver/source Origin or Loan; this does not release existing Loans. indices is a snapshot of [0, L) at acquisition. Resizing an Array does not update a saved snapshot; later accesses check the then-current length.

**Element Places.** Bind the receiver and index Types, resolve bounds, and check access to produce an element Place carrying complete Type T, source location, and permissions. Place formation itself performs no element Copy/Move. Chained access such as `matrix[1][2] = 10` preserves Places without copying an intermediate inner array.

| Use | Operation after locating the Place |
| --- | --- |
| `values[i] = value` | Ordinary initialization/replacement with write permissions and Loan checks |
| `values[i]@ref` / `@uniq` | Shared/exclusive borrow of that Place; exclusive access requires write permission |
| Non-Copy element acquisition | Move only through an eligible static Move Path of an owned fixed array |
| Ordinary value read | Copy/Move on an eligible owned fixed-array static Move Path; otherwise shared reading |

Static paths use only the [integer-literal recognition rule](#1513-move-paths-and-partial-move). Array, runtime indices, Index values, and paths through borrows do not become movable. Ordinary reads Copy when allowed; eligible Non-Copy static elements Move. Other reads follow the [shared result rules](#466-slice-operations-and-element-results), including Object Semantics and Reborrow rather than an unconditional ref/T result. @ref borrows the element Place regardless of index form. Slice Places are shared-only.

```kimi
// resources is an owned fixed array of Non-Copy value Type Resource.
let i: isize = 0
let view = resources[i]   // ref/Resource; the element remains.
// End all required uses of view before the following Move.
let taken = resources[0] // Ordinary Move; resources[0] becomes Moved.

var numbers: [2 of i32] = [10, 20]
let copied = numbers[0]     // Copy; the element remains Initialized.
numbers[0] = 30             // Replace the initialized element.
let alsoCopied = numbers[i] // Copy does not require a static Move Path.
```

##### 4.6.2. Index

Index exposes public read-only `offset: isize` and `isFromEnd: bool`. Its constructor is `init(offset: isize, fromEnd?: bool = false)`.

| Construction | Meaning |
| --- | --- |
| `Index.init(n)` | Offset n from the start; the first element is zero |
| `Index.init(n, fromEnd: true)` / `^n` | Offset n backward from the end boundary |

Prefix ^ produces a storable, passable Index outside indexing expressions too; infix ^ remains integer exclusive-or. Write `^(n + 1)` for a compound from-end distance. Integer indices, range boundaries, and ^ operands have expected Type isize. Already typed i32/usize values require normal explicit conversion. An isize in an index/boundary position denotes a start-relative offset; this adds no general implicit conversion between isize and Index.

Construction with a negative offset initiates Abort without consulting a target length. At length L, start-relative n resolves to n and end-relative n to L - n. Thus ^1 selects the last element and ^0 denotes the one-past-end boundary.

Index implements Equatable by direction and offset, not by coincidental resolution to the same target position. It provides neither Comparable nor arithmetic.

```kimi
let first: Index = Index.init(0)
let last: Index = ^1
let values: [4 of i32] = [10, 20, 30, 40]
let a = values[first] // 10
let b = values[last]  // 40
let end = ^0         // Valid Index; values[end] is out of bounds.
```

##### 4.6.3. Range and ResolvedRange

**Range** is a nongeneric unresolved range specification, constructed only by range syntax; no Range.init is introduced. Boundaries accept isize or Index and normalize integers to start-relative Index values. A negative integer boundary initiates Abort at construction.

| Syntax | Interval |
| --- | --- |
| `start..end` | Include start, exclude end |
| `start..=end` | Include both boundaries |
| `start..` | From start through the target's end |
| `..end` / `..=end` | From the start, excluding/including end |
| `..` | Entire target |

Range exposes public read-only `start: Index`, `end: Index`, and `isInclusive: bool`. Omitted start normalizes to start-relative zero, omitted end to ^0. An inclusive end cannot be omitted. Construction neither borrows an array nor checks boundary order. A saved Range can apply to different targets; it has no target-independent length or isEmpty.

Range implements Equatable by normalized start, end, and isInclusive. Thus .. equals 0..^0, but 1..3 differs from 1..=2. To compare resolved intervals, compare their ResolvedRange values.

**ResolvedRange** always satisfies 0 <= start <= end <= maximum isize. It exposes public read-only `start: isize`, `end: isize`, `length: isize = end - start`, and `isEmpty: bool`. Obtain it through Range.resolve/tryResolve, sequence indices, or `ResolvedRange.init(start: isize, end: isize)`; invalid constructor bounds initiate Abort. No setter or implicit construction bypasses validation.

ResolvedRange implements Equatable by start and end. It retains no storage Origin/Loan; application to an array or Slice rechecks the target bounds. Neither range Type implements Comparable, and no implicit conversion or cross-Type equality is provided.

**Iteration.** ResolvedRange implements `Iterable` with associated Type `Element = isize`: yield start through end - 1 in unit steps, nothing for an empty interval, and remain exhausted after None. Never compute beyond end, including maximum isize. Range is never Iterable, even with absolute boundaries; conformance cannot depend on spelling/value. Use values.indices or explicitly construct/resolve intervals. Infinite, descending, stepped, or negative ranges and dedicated ResolvedRange syntax are unavailable.

```kimi
let inner: Range = 1..^1
let values: [4 of i32] = [10, 20, 30, 40]
let middle = values[inner]
for i in values.indices
    let value = values[i]
let resolved = inner.resolve(values.length)
for i in resolved
    let value = values[i] // Indices 1 and 2.
for i in 0..values.length // Error: Range is not Iterable.
    ()
```

Ranges are non-associative and bind below logical/arithmetic operations but above assignment. Prefix ^ has ordinary prefix precedence. Reject a..b..c syntactically and (a..b)..c by boundary Type.

| Expression | Interpretation |
| --- | --- |
| `a + 1..b * 2` | (a + 1)..(b * 2) |
| `^n + 1` | (^n) + 1; Type error because Index has no addition |
| `^(n + 1)` | From-end distance n + 1 |
| `0..^1` | 0..(^1) |
| `a ^ b..c` | (a ^ b)..c; integer exclusive-or first |

##### 4.6.4. Bounds, evaluation, and failure

Target length L is a nonnegative isize. Resolve boundaries to absolute positions and check the following conditions without clamping or turning reversed intervals into empty ones.

| Operation | Valid condition | Result |
| --- | --- | --- |
| Index boundary resolution | 0 <= n <= L | n from the start, L - n from the end |
| Element access | 0 <= resolved p < L | Element p |
| Half-open Range | 0 <= start <= end <= L | [start, end) |
| Inclusive Range | 0 <= start <= end < L | Normalize to [start, end + 1) |
| ResolvedRange application | 0 <= start <= end <= L | [start, end) |

Check n <= L before from-end subtraction and end < L before adding one to an inclusive end; valid resolution cannot overflow.

| Operation | Result Type | Invalid length/bounds |
| --- | --- | --- |
| index.resolve(length) | isize boundary | Abort |
| index.tryResolve(length) | `Option<isize>` | None |
| range.resolve(length) | ResolvedRange | Abort |
| range.tryResolve(length) | `Option<ResolvedRange>` | None |

Here index is Index, range is Range, and length is isize. These operations take small Copy values and access no target storage. Successful Index resolution validates a boundary, not an element: (^0).resolve(L) returns L. A valid ResolvedRange may still fail on a shorter target.

```kimi
let r = ResolvedRange.init(start: 2, end: 5)
let shortArray: [3 of i32] = [1, 2, 3]
let checked = shortArray[..].trySlice(r) // None.
let failed = shortArray[r]              // Abort if executed.
```

values[..] and values[L..] also apply to empty arrays. values[L..L] and values[^0..] are empty; element index L or ^0, and an inclusive end of ^0, are invalid.

Ordinary element/range indexing initiates Abort on invalid bounds. Use Slice.tryGet/trySlice for expected input failures. A try operation converts only its own length/bounds failure to None, not failures in argument evaluation or other operations: slice.tryGet(^(-1)) aborts during Index construction, while slice.tryGet(-1) returns None. Successful try operations return Some.

Evaluate the receiver and index once under [evaluation order](#122-evaluation-order). A range expression evaluates start then end, then checks integer boundaries for nonnegativity in the same order. Failures inside boundary expressions, including ^ construction, stop subsequent evaluation immediately. These are independent failure examples:

```kimi
func sideEffect() -> isize
    return 2 // Represents an observable effect.

let a = (-1)..sideEffect()  // Evaluate sideEffect, then Abort constructing Range.
let b = ^(-1)..sideEffect() // Abort constructing Index; do not call sideEffect.
```

Locate the built-in access receiver first. While evaluating its index, prohibit modification, destruction, Move, or reallocation of that storage; shared reads remain allowed, including values[values.length - 1]. Establish a write's exclusive Loan after resolving bounds and check existing Loans. Never reevaluate the receiver or boundaries. For a Slice, first Copy its handle; reassigning the original handle does not change the acquired view.

[Simple assignment](#1371-simple-assignment) secures its RHS before locating the indexed target. [Compound assignment](#1372-compound-assignment) evaluates receiver/index, checks bounds, reads the old value, evaluates the RHS, computes, and writes back, once each. Increment/decrement use the same target/Loan rules. Arithmetic failure prevents writeback, and the established exclusive Loan forbids conflicting RHS access. [Exchange operations](#157-initialization-preserving-exchange) evaluate arguments left to right, retaining each target's exclusive Loan during later arguments; Swap requires static non-overlap, not merely runtime i != j. Exchange/Swap remain conceptual names with no additional source API here.

Apply the common [Abort and constant-evaluation rules](#1734-checks-builds-and-constant-evaluation). Syntax, Type, literal-fitting, and required constant-evaluation violations are compile-time errors. Ordinary out-of-bounds a[10] for a three-element array instead aborts if executed. A compiler may warn, but optimization must not turn such a runtime failure into language-level rejection. Rejection of an ineligible static Move Path is a separate rule.

##### 4.6.5. Slice storage, lifetime, and permissions

`Slice<T> from source` shares an initialized contiguous region of complete element Type T. Formation requires ordinary initialization and access checks and cannot hide Uninitialized or partially Moved storage. Its runtime length does not participate in Type identity. Every range access returns Slice, including constant-length ranges, without copying elements into a fixed array.

Slice indices start at zero; from-end positions and reslicing use the current Slice length. Original array indices are not retained. Slicing rows of a nested fixed array produces `Slice<[N of T]>` without flattening. Element inheritance or matching layout does not permit conversion between different element-Type Slices.

**Origins and Loans.** The Origin records backing lifetime; the Loan records conflicting Places and permissions. Handle copies, reslices, iterators, and references to element slots retain both. They need not borrow the handle variable itself: acquired views remain usable after that variable ends if the backing storage and inherited Loan remain valid.

Under [static Place analysis](#1562-place-overlap-and-conflicts), an array-derived Slice borrows the array Place as a whole. Reslicing, splitAt, and empty Slices retain that Loan's conflict footprint. Runtime interval resolution neither narrows it nor proves disjointness. Thus a later use of empty from `let empty = values[1..1]` conflicts with `values[3] = 10`, because of the retained shared Loan, not the Origin alone. The Loan ends after all required derived uses end. Source Move, destruction, or reallocation is also forbidden while it would invalidate an active view.

Slice owns no elements; handle Copy/destruction neither copies nor destroys them. T need not be Owned, and all dependencies inside T remain intact. source may shorten but cannot lengthen; Slice's own Owned classification follows [OwnedOrigins](#1523-static-and-owned), including source and T.

**Storage and escape.** Slice may be retained in local aggregates, Array elements, and concrete object payloads under [ordinary storage](#154-origin-elision-and-return-contracts). Preserve its backing Loan and nested element dependencies. Static storage requires Owned and a valid static source under [static storage](#1132-static-storage); copying or storing a Slice never extends the backing lifetime.

Borrowing a temporary never extends its [lifetime](#36-temporary-values-places-and-lifetimes). Do not reject an unused binding solely because it contains a temporary borrow; check whether later use, return, or retention requires the expired dependency.

```kimi
func makeArray() -> Array<i32> => [1, 2, 3]
func inspect<T> origin source(values: Slice<T> from source) => ()

inspect(makeArray()[..]) // Temporary array survives through the call.
let escaped = makeArray()[..]
inspect(escaped)         // Error: temporary expired at the initializer boundary.
let owned = makeArray()
let lasting = owned[..]
inspect(lasting)         // Borrows an owning local.
```

A var Slice permits handle reassignment only. `uniq/Slice<T>` exclusively borrows the handle, not its elements. Mutable Slices, implicit owning-array conversion, safe raw-pointer construction, Slice equality, and implicit elementwise comparison are not introduced.

##### 4.6.6. Slice operations and element results

For s: `Slice<T>` from source, members receive and Copy the handle by value. Element/partial-Slice results retain source rather than borrowing the call's handle variable. All listed operations are public.

| Operation | Result and conditions |
| --- | --- |
| s.length: isize / s.isEmpty: bool | Read-only count / whether count is zero |
| s.indices: ResolvedRange | Read-only snapshot under the metadata rules |
| s[index] | Shared element access; accepts isize or Index |
| s[range] | `Slice<T>` from source; accepts Range or ResolvedRange, checked at current length |
| s.tryGet(index) | `Option<ref/T from source>`; separate isize/Index overloads |
| s.trySlice(range) | Option<`Slice<T>` from source>; separate Range/ResolvedRange overloads |
| s.splitAt(index) | (`Slice<T>` from source, `Slice<T>` from source), covering [0, p) and [p, length) |
| s.trySplitAt(index) | Option of that Tuple |

Both split operations have isize and Index overloads and accept boundaries zero and length. Invalid boundaries abort for splitAt and return None for trySplitAt. tryGet and trySlice likewise return None on their own invalid bounds. tryGet deliberately has a fixed reference result independent of T's Copy capability.

A shared element read uses the following **SharedReadResult(T, source)** rules. These are element-access operations, not Field getters, and never move the element or implicitly duplicate object ownership.

| Complete element Type | Result | Operation |
| --- | --- | --- |
| Copy `owner/T` | T | Copy |
| Non-Copy `owner/T` | `ref/T` | Shared storage Borrow |
| `obj/T`, `rc/T`, `arc/T` | `objref/T` | Shared object Borrow; no reference-count change |
| `ref/T`, `objref/T`, `unsafe/T` | Same Type | Copy; pointer dereference remains unsafe |
| `uniq/T` | `ref/T` | Shared Reborrow |
| `objuniq/T` | `objref/T` | Shared object Reborrow |

New borrows/reborrows are bounded by source storage and existing dependencies. Copy preserves the stored reference's Origins; Reborrow suspends conflicting parent access while live. Other sequence operations referencing this table retain their own path and write permissions.

For s[index], `@ref` instead borrows the element Place itself. Element writes, Move, and exclusive borrows through Slice are forbidden.

For `s: Slice<Result<i32, i32>>`, `s[0]` is an owned Copy Result, while `s[0]@ref` borrows its slot and `s.tryGet(0)` returns `Option<ref/Result<i32, i32>>`. Adding conditional Copy to an element Type can therefore change inferred read Types, argument fitting, and Origin obligations. Use explicit slot borrowing when an API requires a reference independently of Copy.

For unknown T, retain the correlated result Type, acquisition effect, and Origins as the internal family SharedReadResult(T, source), not a source-spellable Type. Verify the body for all admitted cases; neither assume unknown means Non-Copy nor defer Type checking until a favorable instantiation. Operations/results that do not fit every case require a constraint or explicit borrow.

```kimi
func first<T> origin source(s: Slice<T> from source) -> T
    T is Copy
    return s[0]

func firstRef<T> origin source(s: Slice<T> from source) -> ref/T from source
    return s[0]@ref // Borrow the slot regardless of T's Copy capability.

func head<T, E> origin source(s: Slice<Result<T, E>> from source) -> ref/Result<T, E> from source
    return s[0]@ref // Plain s[0] fails definition checking: Copy bindings return a value.

let values: [4 of i32] = [10, 20, 30, 40]
let s = values[1..]     // Length 3; s[0] is 20.
let last = s[^1]        // 40
let firstTwo = s[..2]   // 20, 30; retains the backing dependency.
let parts = s.splitAt(1)
let left = parts.0
let right = parts.1
match s.tryGet(10)
    .Some(let value) => ()
    .None => ()

func tryTail<T> origin source(values: Slice<T> from source) -> Option<Slice<T> from source>
    return values.trySlice(1..) // None for an empty Slice; no static length condition.
```

```kimi
var values: [3 of i32] = [1, 2, 3]
let shared = values[..]
values[0] = 10 // Error: conflicts with a subsequent use of the shared Loan.
let first = shared[0]
shared[0] = 20 // Error: Slice elements are read-only.
```

##### 4.6.7. Slice iteration and nested Origins

Slice implements Iterable with Element = ref/T from source, including Copy elements, yielding shared references in index order. The iterator retains a handle and position, not owned elements. Yielded references borrow backing slots, not the iterator's receiver/storage, satisfying the [non-lending protocol](#1462-iteration-protocol-and-acquisition). It stays exhausted after None.

Keep element-internal Origins separate from slot-borrow Origins:

```kimi
let data: i32 = 10
let refs: [1 of ref/i32] = [data@ref]
let s: Slice<ref/i32> = refs[..] // Infer inner and backing Origins separately.
let value = s[0]       // Copy the inner reference to data.
let slot = s.tryGet(0) // Option containing a shared borrow of refs[0].
for element in s
    () // element shares a slot containing the inner reference.
```

The outer references from tryGet and iteration borrow refs' slots; the inner reference depends on data. The inner Origin must remain valid while using the outer reference. After copying an inner reference, preserve its own Origin without attaching a new slot borrow.

##### 4.6.8. Representation and performance

Index/Range/ResolvedRange construction, resolution and Copy, and Slice creation, Copy, reslicing, splitting, and address calculation take O(1) time in element count. They require no additional element storage, heap allocation, or reference-count update. Element Copy and user-code costs are separate. Iteration takes O(n) time and O(1) extra storage; do not first materialize an array of iteration values.

A Slice's semantic representation retains backing-element location or equivalent provenance, nonnegative isize length, and static Origins/Loans. Element spacing is stride(T). Empty Slices and zero-sized elements retain source provenance. No universal pointer-plus-length ABI, runtime lifetime tag, or pointer to a disappearing handle variable is required. Use logical counts/positions rather than subtracting element pointers to recover length; never form invalid pointers before checking.

Checks may be eliminated, shared, or optimized in loops only when safety is proven without changing effects, Abort behavior, or borrow legality. Constant-folding Index/Range operations does not expand the literal-only static Move Path rule.

### 5. Raw pointers and unsafe memory

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

#### 5.1. Null and equality

`unsafe/T` permits `null`, whose expected Type must determine `unsafe/T` or compilation fails. Safe references (`ref/T`, `uniq/T`, `objref/T`, `objuniq/T`) are non-null. Non-nullness alone does not validate a raw pointer.

```kimi
let pointer: unsafe/i32 = null
let typedNull = null@unsafe/i32 // Typed Null Formation, not a pointer cast.
let unknown = null // Error: no pointer Type can be determined.
let empty = pointer == null
```

`==` and `!=` compare the addresses of two pointers with the same Type and return `bool`, without reading pointees. A comparison with `null` gives the literal the other operand's pointer Type. Null equals null and never equals a non-null pointer.

Initialized pointers may be compared even if null or dangling. Equal addresses imply neither equal provenance nor ownership or access permission. Different pointer Types require an explicit unsafe conversion to a common Type. Pointer ordering comparisons are not defined.

#### 5.2. Dereference and ownership

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

#### 5.3. Pointer arithmetic and indexing

For `p: unsafe/T` and `n: isize`, including negative `n`, only these arithmetic and indexing forms are supported:

| Form | Meaning |
| --- | --- |
| `p + n` | Pointer displaced by `n * stride(T)` bytes. |
| `p - n` | Pointer displaced by `-n * stride(T)` bytes. |
| `p[n]` | The same memory place as `*(p + n)`. |

`p += n` and `p -= n` combine these displacements with [compound assignment](#1372-compound-assignment) and require the same unsafe conditions. Pointer increment and decrement are not supported.

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

#### 5.4. Pointer conversions

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

#### 5.5. Target and round-trip guarantees

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

#### 5.6. Raw pointer API design boundaries

Raw pointer acquisition APIs, allocation and deallocation, initialization of raw storage, conversion to or from safe references, ownership acquisition, and Unsafe Function Types are specified separately. Example functions such as `obtainPointer` and `use` are illustrative, not standard API declarations.

## Part III. Declarations and static semantics

### 6. Declarations and containers

Kimigayo uses the following information to identify declarations and their meaning:

| Element     | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| `Name`      | The basic human-readable name used to refer to a declaration          |
| `Signature` | The information that distinguishes declarations in the same scope     |
| `Type`      | The meaning of a value or invocation within the type system           |

See [name resolution and access](#9-names-signatures-and-access) and [overload selection](#10-overload-resolution-and-inference). Field and computed syntax is defined under [Properties](#11-properties).

#### 6.1. Declaration containers

`static` is not a declaration modifier in this revision. Reject `static func`, `static let`, and `static var`, including redundant uses in groups. Group/rootgroup members are inherently static, a struct function without a receiver is a type function, and struct Fields and computed members are instance members. The contextual Origin spelling `from static` remains valid; it does not make a declaration static.

**Extension boundary:** This revision does not accept `extension` declarations or imports and supplies no extension candidates. References below to future extension access, identity, and precedence constrain that future design only; they are not active lookup stages or conformance mechanisms. Ordinary member lookup is complete without an extension pass.

A **Declaration Container** is a named declaration scope whose body may contain Fields, computed members, functions, associated-Type declarations/specifications, Constraint Clauses, or nested Declaration Containers as permitted by its kind. Its body is delimited by indentation.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Fields, computed members, functions, and nested Declaration Container declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts Fields, computed members, functions, conditional conformance declarations, associated-Type specifications, constructors, and at most one selected `deinit`. Generic parameters, Origins, and Constraints are supported. Sealed by default; `open struct` permits derivation. |
| `enum` | Yes | Closed sum Type with Cases, positional payloads, functions, conditional conformance declarations, and Constraints; see [enums](#63-enums). No fragments or inheritance. |
| `extension` (future) | No | Not introduced; see this section’s extension boundary. |
| `contract` | No | Declares function/Property requirements, associated Types, and Constraints; supports multiple-parent refinement. No implementations, storage, generic parameters, or contract-level Origins. See [Contracts](#84-static-contracts). |

##### 6.1.1. Root and nested containers

Each source unit contributes named declarations to the project root. Top-level bindings (`let` and `var`), local functions, and executable items retain a SourceDocument-local scope and are not exported to other files. The exception is an explicit root-level `public main`, an ordinary function in the shared root that retains its declaration-site lookup environment. It cannot capture top-level runtime locals. Other shared functions, Fields, and computed members belong in named Containers. [Program startup](#222-program-startup-and-static-initialization) selects either one document's runtime body or one eligible public main. A `rootgroup` declaration starts at the root and accepts a dot-separated Name. For example:

```kimi
rootgroup A.B
    var value = 1
```

creates the nested group path `A.B`. Ordinary `group` bodies accept nested Declaration Container declarations. In this revision, `struct` bodies do not accept nested Declaration Containers.

Aliases follow [source-local import rules](#181-external-references-and-aliases). Synthesized intermediate groups in a `rootgroup` path use an explicit group declaration's accessibility when present, otherwise `private`; synthesis is not an independent header fragment. Public paths require explicitly accessible groups.

**Empty declaration bodies.** A group, rootgroup, struct, or contract may omit its indented body entirely. The header is complete when followed by EOF or the next effective item at the same or shallower indentation. Blank/comment-only lines, including indented comments, do not create a body and do not cause the next item to be absorbed. An indented declaration body may also become empty after directive selection. A bodyless contract is a marker Contract with no requirements; a bodyless struct is an empty structure, subject to its base and implicit-constructor rules. These permissions do not apply to enums (which require at least one selected Case), match-arm lists, or executable Blocks. A `()` expression is not an empty-Container marker, since ordinary executable items are not Container members.

##### 6.1.2. Container fragments

Group and struct declarations may be split, even within one file. Collect same-parent, same-name fragments and identify a declaration by originating Kotonoha, parent Symbol, name, kind, and generic arity. Reject conflicting kinds such as a group and struct with the same name. Different arities, such as `Box` and `Box<T>`, are different Types. Never merge across Kotonoha libraries or treat an extension as a target declaration fragment; applied `ref`/`uniq` Semantics do not change Container identity.

Matching fragments must agree on generic parameter count/kinds/order/names, Origin count/order/names and declared bounds, declaration kind, semantic modifiers, and accessibility after defaults. Repeat Origin bounds in every fragment and compare their resolved binder identities; each occurrence resolves in its own source environment. Do not widen conflicting accessibility. Exactly one fragment may define the Container's Constraint Clauses, even if duplicate clauses would be identical; other fragments omit them and share that definition's Constraints. Resolve them in their definition-site source environment.

For structures, `open` must agree across all fragments. At most one fragment supplies the base clause; the other fragments share that base without repeating it. Resolve the base in that fragment's source environment, then validate the complete merged inheritance relationship.

After compile-time selection and merging, reject duplicate Field/computed Names, duplicate function Signatures, and namespace conflicts. Selected fragments may contribute Fields, including generated ones, under [split-structure storage order](#621-split-structures-and-storage-order); C layout requires a single storage-bearing fragment (§21.1.2). Do not require a primary fragment. Enums cannot be split: after selection and generation, multiple declarations with the same enum Identity are errors. Contracts likewise cannot be split: after directive selection and source generation, multiple declarations with the same Contract Identity (originating Kotonoha, parent Symbol, name, and contract kind) are declaration errors, even when their contents are identical. User Contracts are nongeneric; no arity distinguishes same-named declarations. Mutually excluded declarations are allowed only when at most one remains. Different Contracts may refine a shared parent, but refinement is not declaration merging.

Constructor Signatures must also be unique across the selected fragments. At most one selected `deinit` body may belong to a merged structure, including generated fragments. Do not concatenate destruction bodies or choose one by source order; duplicates are declaration errors. Mutually excluded bodies may coexist in source only when selection leaves at most one in each fixed target/configuration environment.

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

**Future design:** Contract fragments are not introduced; any future fragment facility needs explicit contents and merging rules. Extension identity/public names remain separately specified. Static initialization and startup selection are defined under [program startup](#222-program-startup-and-static-initialization).

#### 6.2. Structure declarations

A `struct` declares a named composite Core. Its Fields and computed members have complete Types, including their Semantics and Origin dependencies:

```
struct Point
    var x: f64
    var y: f64

struct Node
    var value: i32
    var next: obj/Node

struct View origin source
    var data: ref/Data from source
```

Data is an assumed Core in the View example; the instance borrow's Origin is explicitly declared and bound.

Explicit and implicit constructors follow [construction](#623-constructors); all construction obeys [initialization](#113-types-and-origins) and [construction completeness](#1512-aggregate-construction-and-completeness).

##### 6.2.1. Split structures and storage order

A `struct` may be split into [declaration fragments](#612-container-fragments), including fragments produced by a [Mod](#207-mods-source-generation); identity, header agreement, member uniqueness, and the C-layout single storage-fragment restriction follow that section. Fields are identified by declaration kind; computed members add no storage.

Apply [logical declaration order](#2074-generated-sources-and-declaration-order) to the environment-selected fragments of each instantiation. Only Fields contribute storage slots. This order determines initializer side-effect order independently of physical layout.

Mods may inspect provisional Types under [provisional Binding](#2072-compilation-and-binding). Finalize layout only after all Mods complete and the full selected fragment set and storage classification are known. No later generation may append Storage to a finalized Type.

Physical layout and ABI guarantees follow [Structure layout and ABI](#211-structure-layout-and-abi).

##### 6.2.2. Inheritance and open structures

A structure is **sealed** unless its declaration has `open` immediately before `struct`. A sealed structure cannot be a base Type. `open` permits derivation and is independent of accessibility: `public struct` remains sealed, while `internal open struct` permits derivation only where that Type is accessible. A derived structure is itself sealed unless explicitly declared `open`; openness is not inherited. No separate `sealed` modifier is needed for this default.

A structure may specify one direct base with `: BaseType`, after its Name and generic parameters and before its Origin list. The base must resolve to an accessible constructed or nongeneric `open struct` Core; a generic parameter, Semantics-applied Type, group, enum, or contract is not a base. Omission declares no user-defined base. Reject multiple bases and direct or indirect inheritance cycles, including cycles through different constructions of the same generic declaration. The base's Constraints must hold, and its accessibility must cover the derived Type's [effective access domain](#932-api-signature-accessibility). Constraint Clauses continue to express capabilities separately from the base clause.

```kimi
public open struct Base
    protected var count: i32 = 0

public struct Leaf : Base
    public func read(self: ref/Self) -> i32 => self.count

public struct Invalid : Leaf // Error: Leaf is sealed.
```

Inheritance preserves members' declared accessibility and grants no private access. Ordinary functions and accessor calls use the implementation selected statically from the receiver's Effective Type, including through concrete Core Object Views. The Runtime Object Type does not replace that implementation. [Inherited lookup](#95-qualified-and-inherited-lookup) determines the declaration layer; [receiver projection](#951-base-subobject-receiver-projection) supplies eligible instance receivers. Function-value calls retain their own rules.

**Inherited Names.** A derived struct cannot declare a Value-role member with the same Name as a member of any ancestor that is accessible from the derived declaration. This covers functions regardless of Signature or instance/type-function kind, Fields, and stored/computed Properties. Conditional premises and overload applicability do not exempt a declared Name. Check a Property's own access, not an individual accessor's; use the derived receiver for protected access.

Check the completed declaration set after environment selection, Mod output, and fragment merging, with a valid base graph. Diagnose a collision at the derived declaration. Same-layer overloads and other namespaces retain their rules. Inaccessible ancestor Names, including private or cross-Kotonoha internal/private-protected members, may be reused. An explicit specialization is an implementation of its original function, not another member; it gains no right to specialize a base member from a derived Container.

Accessible Value-role Names of an open struct are part of its API to derived Types. Name additions or access expansion can invalidate downstream declarations under [dependency revalidation](#2134-artifacts-verification-and-invalidation). Derived Types must tolerate mutations and preconditions exposed by base APIs; stronger application invariants cannot become unchecked memory-safety assumptions.

The direct base is one inline owned subobject, logically preceding the structure's directly declared fields. Base subobjects retain their own field identities and construction-completion facts. Physical offsets remain compiler-selected under [layout rules](#211-structure-layout-and-abi); logical ordering does not require flattening or a fixed ABI. Construction runs base first under [constructors](#623-constructors), and destruction runs the derived layer first under [field cleanup](#1632-field-cleanup). The base subobject cannot independently be Moved, replaced, or reconstructed through a base view of a derived value. Whole-value operations retain the exact owning Type and the responsibility for all layers; they cannot slice a derived value. Access to an inherited field still obeys normal permissions and checks all its containing base and derived ancestors for Partial Move restrictions.

Explicit ordinary base-member invocation and additional ordinary derived/base conversions are not introduced. [Object calls](#1243-object-member-calls) and [explicit object upcasts](#1357-object-upcasts) retain their defined operations. These rules imply no CLR representation, boxing, garbage collection, or value slicing.

##### 6.2.3. Constructors

Syntactically, a qualified Type-shaped path followed by `.init(` is always a construction expression. `init` is excluded from ordinary member Names, so there is no competing ordinary-call parse. Qualification may begin with `::` and contain Type arguments. A Name-shaped qualifier such as `value` is accepted syntactically, then checked using Type lookup; if it identifies only a value, Binding reports an error. There is no retry as a value-member call and no reinitialization of existing storage.

A constructor is a dedicated structure declaration: an optional access specification, `init`, a parameter list, an optional `: base(arguments)` clause, and a common executable Body (§14.2), either single-item or indented. It has no ordinary Name, explicit receiver, separate generic or Origin parameters, or result annotation. Unavailable modifiers follow §2.5.1. It uses the containing structure's Type parameters, Origins, and Constraints. Parameter labels, defaults, and Type checking follow ordinary function parameters. Access defaults to `private`; constructor parameters obey API signature accessibility. Only a structure's own fragments may declare its constructors; groups, enums, contracts, extensions, and executable Blocks may not.

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

`Type.init(arguments)` constructs a fresh owner of exactly Type. The reserved `.init` suffix commits to Type lookup: first resolve the structure and all generic arguments, then select among its own accessible constructors by ordinary argument mapping and overload comparison. Expected results cannot change that Type or lookup path. Supply every generic argument unless the qualifier is already fully bound, such as Self in a generic structure.

Bare type-parameter construction, inherited/extension constructors, field-wise or zero-fill fallbacks, and ordinary function-call fallback are unavailable. Unknown or inaccessible constructors are errors. `Type.init` is not a function value; `value.init(...)` cannot reinitialize storage. Construction returns an owner value; creating `obj`, `rc`, or `arc` requires a separate [Core ownership operation](#1358-object-ownership-creation-and-sharing).

After normal argument/default evaluation, allocate fresh Uninitialized construction storage and bind parameters. Evaluate base arguments in that constructor’s parameter/source context and invoke the selected direct-base constructor in its subobject. Omitted base initialization selects an accessible zero-argument invocation, including optional parameters; `: base(...)` without a base is an error. Exactly one base call occurs; same-Type delegation and repeated base initialization are unavailable. This dedicated operation permits protected base constructors without an ordinary instance receiver.

Complete base construction before evaluating this layer's Field declaration initializers, in logical declaration order, and then execute its constructor body. Declaration initializers keep their own declaration-site environments: they cannot reference constructor parameters or `self`. Base arguments cannot use `self` either. No constructor silently initializes a field with zero, null, or an element Type's default constructor. Origins required by stored arguments and the completed base must be represented by the constructed Type's declared Origin contract and inferred under ordinary lifetime constraints; hidden or invented Origins cannot make a construction valid.

Constructor `self` is a special Construction receiver. First writes directly initialize own stored Properties only when all incoming paths are before first placement. Later writes to var with standard set use normal state-dependent placement/Replacement; custom set cannot be called, including at mixed-state joins (§11.3.1). Standard get may Copy or borrow initialized own storage; no non-Copy Move is allowed during construction.

Construction forbids computed access, inherited Field access, whole-self acquisition or borrowing, instance-method/custom-accessor calls on self, and capturing or exposing self. The base constructor handles its own fields. Field borrows must end before completion and cannot escape in the result; borrowed inputs may be stored only under the result’s Origin contract. These privileges apply only inside the constructor body, not nested functions, ordinary methods, or Field declaration initializers.

A constructor body is a Function Boundary with Unit control-flow result. Fallthrough, operandless return, or return of Unit requests success. After any return operand, every reachable successful exit must have a completed base and complete, Initialized own Fields. Run normal Scope Exit—including parameters and Deferred Blocks—while retaining construction storage; then recheck completeness and lifetimes before committing this layer. A defer cannot supply initialization missing at the requested exit, nor can completion checks justify borrowing a destroyed parameter. Base completion continues the next layer; only outermost completion yields the owned result. Moving that result does not rerun constructors.

Constructors have no recoverable-failure return or exception mechanism. Use an ordinary factory returning an Option/Result to perform fallible acquisition and validation before calling `Type.init`; its locals have ordinary cleanup on failure. Abort during any construction phase terminates without unwinding. The partial-initialization cleanup rules also govern interrupted aggregate-expression construction and any ordinary Scope Exit that abandons construction storage; they do not add a new failure syntax or turn an incomplete `return` into success.

After merging, synthesize one public zero-parameter constructor iff there is no selected explicit constructor, every own Field has an initializer, and any base has an accessible zero-argument invocation. Its effective access is the Type’s; its implicit Unit body follows normal initialization/completion. Empty structs meet the field condition. Otherwise no implicit constructor exists, without invalidating the Type itself. Any selected explicit/generated constructor—even private—suppresses synthesis; no memberwise constructor is added. Synthesis depends on declarations, not initializer validity: report resulting errors normally and retain generic obligations until instantiation.

##### 6.2.4. Virtual members and overrides

**Extension design; not active in this revision.** This section owns the proposed virtual/override semantics and their interaction with runtime Contracts. Current declarations use §6.2.2's static selection and inherited-Name rule. Unavailable modifiers are diagnosed under §2.5.1.

An overridable declaration and each override require explicit designation. The original declaration identifies the slot; equal Names or Signatures never create one implicitly. Only a valid explicit override would be exempt from the inherited-Name prohibition. Existing ordinary members do not become virtual.

An override preserves Shared/Exclusive receiver kind and, after normalization and receiver-Self correspondence, parameter and result Types. No covariant result is added. Labels and defaults follow the statically selected declaration. Overrides cannot strengthen public premises or input-Origin requirements, or weaken result lifetime guarantees; compatibility must follow existing proof rules.

The target and each overridden accessor must be accessible. Preserve their declared access, except that a protected-internal member overridden in another Kotonoha is declared protected there. Private members, and internal/private-protected members outside their Kotonoha, are ineligible. This grants no permission to expose inaccessible API Types.

Initial virtual calls are safe. Each virtual implementation and override independently requires [ObjectCompatible Proven](#1244-object-receiver-compatibility), including its implementation family; failure is a declaration error even without object call sites. This is an explicit guarantee, unlike an ordinary member's inferred public status. A base body's proof cannot validate an override.

Static lookup selects the declaration, overload, access, labels, and public contract. Runtime dispatch selects only that slot's implementation for the Dynamic Type; it never repeats lookup or adds derived-only overloads.

```text
Animal.reset: explicit Exclusive virtual slot
    ├─ Dog.reset mutates permitted state -> valid override
    └─ Cat.reset replaces all of self    -> invalid override declaration
```

When combined with [runtime Contracts](#85-runtime-contracts), a retained requirement mapping follows the corresponding valid override. Recheck every mapped requirement at that override's declaration; reject an incompatible override rather than silently losing conformance or Supports. Runtime Contract Views remain a separate extension owned by §8.5.

Before introduction, settle declaration spellings, eligible members/receivers/generics, accessor designation, abstract construction restrictions, explicit base-implementation calls, and slot/metadata information for separate compilation together. This design adds no active syntax or fixed ABI.

#### 6.3. Enums

An enum is a nominal, closed sum Core. A complete value contains exactly one **Case** and that Case's **payload**, its attached positional data. Equal Case names and payload Types do not make distinct enum declarations the same Type.

```kimi
public enum Message
    Quit
    Write(string)
    Move(i32, i32)
```

##### 6.3.1. Cases and payloads

Write one Case per line without a `case` keyword; PascalCase is conventional. Case names are unique within the enum and cannot overload by payload Type or arity. A payload element declares one complete Type in positional order, without a binding name, `let`/`var`, default, or variadic form. `Quit` has no payload; `Quit()` is invalid, whereas `Wrapped(())` has one Unit payload.

The header supports ordinary Generic and Origin parameters. The body permits Cases, Constraint Clauses, associated-Type specifications for declared conformances, ordinary functions and their full specializations, conditional conformances (§8.4.8), and compile-time Directives selecting these items. Constraints use the existing declaration rules; `Self` denotes the enum Core. Function access and explicit receivers follow ordinary rules. Fields, computed members, `init`, `deinit`, nested Declaration Containers, structure inheritance, `open enum`, and external Case additions are not permitted. Enums cannot have [declaration fragments](#612-container-fragments), and every instantiation must retain at least one Case after compile-time selection. Empty enums and uninhabited-value elimination are deferred.

Each Case has the enum's effective access domain, rather than the ordinary member default of `private`. Cases and payload elements take no access modifiers. Anyone allowed to use the enum may construct and decompose every Case; each payload Type must satisfy [API signature accessibility](#932-api-signature-accessibility) for the enum's domain. A Case colliding with another Value declaration, including a function, is a declaration error. Adding or removing a public Case is a potentially breaking source API change: additions can break exhaustive matches, and removals can break Case references.

Payload elements are anonymous storage, not named Fields or accessors. Apply the same complete-Type, Semantics, and Origin storage checks as struct stored values. Directly declared borrowed elements require explicit valid Origins; nested Types retain all their dependencies. A struct payload can represent data needing named fields.

```kimi
enum View<T> origin source
    Some(ref/T from source)
    None

enum MutView<T> origin source
    Some(uniq/T from source)
    None

func makeView<T>(value: ref/T)
    -> View<T> from (source => value) => .Some(value)
```

The result annotation maps the enum's abstract `source` to the input's Origin. It describes borrows stored in an owned enum, whereas `ref/T from value` annotates a direct result borrow. The existing [single-Origin shorthand](#1531-origin-arguments) permits `View<T> from value`; named mapping makes the assignment explicit.

At construction, bind payload dependencies to the enum's Origin arguments and validate every stored value against that contract. Infer variance and Loan requirements from occurrences in all Cases, retaining the ordinary fixed-point rules. Selecting a Case does not weaken the Type's Origin contract. Storing or moving out a `uniq/T` payload transfers the exclusive reference value, not its referent; shared reading reborrows it and suspends conflicting exclusive access. Ordinary storage, lifetime, and unique Loan-anchor rules still apply.

Enums initially support owned values and value borrows; constructing or matching enums through Object Semantics is deferred. Their payloads may contain existing Object Semantics. Reject direct or indirect inline recursion without finite size; recursive data requires an existing legal indirection, with no implicit heap allocation. Case changes use whole-value assignment/Replacement, not tag mutation. No ordinary `value.0` or `value.Case` payload extraction is added. Copy follows [enum derivation](#352-enum-copy), and remaining payloads follow [aggregate cleanup](#1632-field-cleanup).

Implicit equality/ordering derivation is not introduced; explicit comparison conformance follows §13.4.1. Integer discriminants/conversions, default values, tag size or numbering, niche optimization, and fixed ABI are unspecified. Declaration order does not define numeric values or a wire format.

##### 6.3.2. Case construction and resolution

Use `Type.Case` or, with a known expected enum Type, `.Case`.

**Parsing and Binding.** In expression context, every qualified form uses the ordinary Name, generic-application, member-access, and invocation syntax. The Parser never chooses a Case construction from capitalization, a Type lookup, or the expected Type. For example:

~~~text
Message.Move(1, 2)
    Syntax: Invocation(MemberAccess(Name("Message"), "Move"), [1, 2])
    Binding, if Move resolves to an enum Case: EnumCaseConstruction
~~~

The same syntax represents `value.move(1, 2)` and `Namespace.Type.member()`. Binding uses ordinary qualified lookup (§9.5), retaining both Type-side and Value-side interpretations when required; distinct successful interpretations remain ambiguous. Once lookup selects a Case Symbol, classify the operation as enum construction and check payload presence, labels, count, and Types. A failure does not retry an ordinary method or another lookup stage. A selected non-Case callable follows ordinary invocation rules. A qualified payload-free Case is classified from its member access without a call; a payload Case without a call is an error. Case constructors are not first-class callable values.

Only the leading-dot expression form, such as `.Some(1)`, has dedicated inferred-Case syntax. Its arguments are Expressions. A compiler may create a dedicated bound construction node after resolving either form; this does not require a second parse. The reserved `.init(` suffix retains its separate syntactic rule. In Pattern context, qualified and inferred Case references are parsed by the Pattern grammar, with Pattern operands; they do not compete with expression calls.

Leading-dot layout is decided syntactically under [§2.2.2](#222-leading-dot-continuation-and-case-references) before expected-Type lookup. The qualifier identifies an enum Core whose Case set can be determined statically after alias expansion. Generic arguments must be explicit or uniquely inferred. Do not write the enum's own Semantics or Origin arguments in the qualifier; complete Types inside generic arguments retain theirs. Infer/check the constructed value's Origin arguments from its expected Type and payload arguments. If these do not determine them, annotate the expected Type; do not invent hidden Origins or `static`.

```kimi
let quit: Message = Message.Quit
let move: Message = Message.Move(10, 20)
let some: Option<i32> = Option<i32>.Some(42)
let none: Option<i32> = .None
// value: ref/T
let view: View<T> from (source => value) = View<T>.Some(value)
```

`.Case` resolves only within the already known expected enum: the owned expected Type for construction, or the Type determined at that [Pattern position](#1481-patterns) for matching. Never search all enums by Case name, retry another expected Type, or change a matched value's Origin contract. This is not general expected-type lookup for members. `let bad = .None` has no known enum Type; `View<T> from (source => value).Some(...)` is not a CaseReference.

A payload-free Case produces its value without parentheses. A payload Case requires all positional arguments in declaration order. Omitted/named arguments, partial application, and acquiring a Case constructor as a function value are invalid.

```kimi
let missing: Option<i32> = .Some  // Error: payload argument required.
let extra: Option<i32> = .None()  // Error: payload-free Case takes no parentheses.
```

Evaluate arguments once each, left to right, and initialize payloads using ordinary argument adaptation, literal fitting, and Copy/Move. Commit a complete enum value only after the Case and all payloads are initialized. On an ordinary transfer out of construction, secure the transfer result and destroy initialized payloads in reverse order; Abort does not unwind. Enum Case construction is a dedicated bound operation; its qualified expression syntax remains ordinary member access/invocation. Case Pattern operands are Patterns, not expressions.

During ordinary abandonment of aggregate construction, interleave cleanup of placed components and still-live expression temporaries in reverse order of completed placement/value acquisition, while preserving inner-to-outer scope exit under §16.2.1; transferring responsibility removes the source from that cleanup order.

#### 6.4. Bindings

Fields and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` declares a mutable binding. A Type annotation and an initializer are independently optional when the omitted information can be inferred.

**Basic example.**

```kimi
let limit: i32 = 10
var current = 0
```

A local Type must be fixed at declaration, even without an initializer. An explicit local Type with an omitted borrow Origin or required Origin argument needs a declaration initializer under [Origin inference](#154-origin-elision-and-return-contracts); a later first assignment cannot supply the missing annotation. A fully specified Type may still omit its initializer under the ordinary initialization rules. Locals become visible after their declaration, so an initializer `let x = x` refers to an outer `x`; duplicate and forward-reference rules follow [name visibility](#92-namespaces-roles-and-visibility). `let` permits only its first initialization, and Move never resets that history. Definite initialization and permitted reinitialization follow [initialization-state rules](#1511-storage-state-and-responsibility).

#### 6.5. Attributes

**Attribute syntax.** `#Name` accepts an optional parenthesized, comma-separated Argument list with a trailing comma. Name must begin with an uppercase Unicode letter; lowercase if/switch/case select directives, and other lowercase forms are errors. Attributes attach in source order to the next same-indentation declaration, on its line or preceding effective lines. Comments/blank lines may intervene; unrelated items and dedents may not. Dangling Attributes are errors.

Attributes are accepted on ordinary Container, function, Field, and computed declarations, and on function parameters before the parameter Name. Explicit specializations and Contract requirements retain their prohibition on Attributes; expression statements, patterns, arguments, and accessor lists do not accept Attribute prefixes. Excluded syntax follows §19.5: argument and declaration-placement grammar is checked only where ordinary parsing is required, and excluded Attributes undergo no semantic resolution. The recognized Attributes are `#Layout` for struct storage (§21.1.2) and `#LibraryImport` for [foreign functions](#223-foreign-function-imports). Their concrete argument and target rules are checked after selection; no ordinary Name lookup supplies their literal arguments.

**Mod markers.** A [Mod](#207-mods-source-generation) may use Attributes to find targets. A marker does not request execution or consume an Attribute: several Mods may inspect the same Attribute, and a later Mod may emit markers for an already completed Mod without restarting it or causing an error merely for that reason.

Expose marker names, argument syntax, and target Koto before the target Type is fully bound. Queries use environment-selected syntax, excluding discarded declarations. Syntax-name matching does not establish semantic identity between unrelated same-spelled Attributes. A Mod's registration or accompanying contract must identify its markers and argument rules. Semantic argument queries obey the [Binding access period](#2072-compilation-and-binding); syntax remains readable afterward. Neither discovery nor argument inspection requires executing the target program or an Attribute constructor.

Marker discovery and validation are distinct. Diagnose a selected Attribute that remains unrecognized by final validation; finding its syntax does not make every unknown Attribute valid. Concrete marker registration/recognition APIs and general Attribute semantics beyond these rules, Layout, and LibraryImport remain design boundaries.

### 7. Functions and callable values

A function begins with `func`, followed by its Name, optional generic parameters, optional Origin parameters, and a parenthesized parameter list. A result Type follows `->`; omitting it in a named function means Unit (`()`), regardless of accessibility or body form. Definitions use the common Body forms (§7.1). Anonymous functions retain their separate [inference rules](#761-syntax-and-inference).

The declared function Name is a single, unqualified Name. Its declaration belongs to the lexical Container or executable scope in which it appears. Declare a member inside the relevant Container body, including a permitted fragment; `func View.get(...)` and other qualified function declaration names are compile-time errors. A qualified declaration cannot attach a function to another Container, introduce an extension, or obtain that Container's private access or generic bindings. Qualified Names at use sites and explicit receivers remain governed by their existing rules.

Explicit parameters are initialized, immutable let-like bindings, including anonymous-function, constructor, and receiver parameters; setter value is also immutable. Non-Copy values may Move once, but parameter reassignment, reinitialization, and new exclusive storage borrows are forbidden. Use a var local for mutable work. Existing uniq/T or objuniq/T still permits exclusive referent access/Reborrow; binding immutability does not restrict its referent. Construction/Destruction receivers keep their special privileges. There is no var parameter syntax.

#### 7.1. Function bodies and results

Functions use the common single-item or indented Body (§14.2). A named function's omitted return Type is Unit; its body and callers do not infer that signature. Bind a generic definition under its declared Types and Constraints without reinterpreting the body at instantiation.

In a single-item body, an expression is discarded if the return Type is already fixed as Unit; otherwise its normal value is the return result. An explicit return always fits the return Type. A statement supplies Unit only if it structurally completes normally.

Direct expressions in an indented body, including the last, are discarded. Structural end arrival supplies Unit. Non-Unit results require explicit return; expected Types and all written result sources, including unreachable ones, follow §14.9. Runtime Reachability does not change a function's fixed return Type.

```kimi
func direct(left: i32, right: i32) -> i32 => left + right
func indented(left: i32, right: i32) -> i32
    return left + right
func bad(left: i32, right: i32) -> i32
    left + right // Error: structural end supplies Unit.

func cleanup() => handle.close() // Discard even if close returns bool.
func invalid() => return 123     // Error: explicit result does not fit Unit.
func unused() => 123             // Valid Unit function; warn on unused effect-free value.
let twice = func (value: i32) => value * 2 // Anonymous return inference: i32.
```

A final selection, loop, or do expression is still discarded in an indented body. Use return with that expression or returns inside its paths. Anonymous return inference and its fixed-context boundary follow §7.6.1 and §10.5. Other function-like targets are listed in §14.5.3; all use common Scope Exit (§16.2).

#### 7.2. Parameter names and defaults

A parameter may separate its external argument name from its local name with `external => internal: T`. An optional parameter uses `name?: T = defaultExpression`. The `?` requires a default and means argument omission, not a nullable Type; a default alone does not make a parameter optional.

At each call, evaluate omitted defaults once in parameter declaration order, after all explicit arguments. Resolve default expressions in the declaration's scope. They may refer to preceding parameters, but not later parameters or caller-local bindings.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
let result = scale(3, by: 4)

func offset(value: i32, by?: i32 = 1) -> i32 => value + by
let next = offset(3)
let adjusted = offset(3, by: 5)
```

Function Types retain neither argument names nor defaults. Calls through function values supply all arguments positionally. See [invocation](#1242-invocation-and-generic-application) for argument matching and evaluation.

**Default evaluation and ownership.** Acquire explicit arguments in source order into pending slots, then evaluate omitted defaults in parameter order using the declaration environment and prepared preceding slots. Slots do not alias caller variables. A default may Copy a preceding Copy value or inspect it through temporary shared access; it cannot Move/Consume or modify that slot or its owned contents. Its result cannot retain a new Borrow/Reborrow of a preceding argument, but may Copy an existing shared borrow with external dependencies. End temporary inspection Loans before entering the callee. Thus `func f(x: string, y?: string = x) => ()` is invalid; stringifying a temporary shared borrow of x may produce an independent string.

Check every default at declaration time, even if all calls supply the argument. A required parameter’s initializer permits no omission and does not run when supplied. Defaults give no generic-inference evidence; their transfers may target only constructs inside the default.

A normal transfer that abandons argument evaluation destroys still-owned prepared values and temporaries in reverse acquisition order and skips the callee. Abort does not unwind. After successful preparation, ownership passes from pending slots to initialized parameters; callee cleanup uses reverse parameter order.

#### 7.3. Explicit receivers

A function directly in a struct/enum, or a Contract function requirement, is an instance function exactly when a parameter’s **internal Name** is self; otherwise it is a type function. Allow at most one self, at any written position, with no rename, default, or optional marker. Its normalized Type must be Self, ref/Self, uniq/Self, or a permitted object-Semantics Self form. Reject unrelated targets, extra reference layers, raw pointers, and unconstrained generic receiver Semantics. Origin annotations follow normal parameter rules. In groups/rootgroups or local functions without active contextual self, the parameter name self has no instance-member meaning.

For `receiver.method(arguments)`, evaluate and adapt the receiver first, recording it at the self parameter's declared position. Match the explicit positional/named arguments against the remaining parameters in their written order; self cannot also be supplied by an argument label. Defaults then follow ordinary order. The source receiver is evaluated first regardless of its parameter position. Cleanup inside the callee still uses the full written parameter order.

A Type-qualified instance function reference is unbound: a call through `Type.method` supplies all parameters explicitly in their written positions, including self at its declared position, with ordinary argument order and receiver compatibility checks. An unbound function value likewise retains self as an ordinary position in its callable signature; it implicitly captures no receiver and remains subject to unsafe-function restrictions. `value.method` without invocation does not form a bound-method value in this revision. This does not introduce extension functions, implicit self lookup, or a conversion for an otherwise incompatible object receiver.

#### 7.4. Function constraints

A generic function with an indented body may begin it with [Constraints](#82-constraints). Its Constraint Clauses must precede every executable body item and are processed at compile time; they are not executable expressions.

Before lookup, parse the maximal leading sequence of unparenthesized `ConstraintSubject is IsRequirement` items as Constraint Clauses. A subject not permitted for this function is an error, not a fallback runtime test. Blank lines and comments do not end this prefix. Parenthesizing the whole test, as `(value is Dog)`, makes it an executable expression item and ends the prefix; subsequent ordinary value tests are executable. A later clause rooted in a generic parameter remains a misplaced-Constraint error. In a nongeneric function body this prefix rule does not apply, and `value is Dog` is an expression. Dedicated Type/Contract Constraint regions retain their own rules even through parentheses.

**Basic example.**

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

Each clause subject must name a generic parameter of that function or an [associated-Type projection](#843-associated-types) rooted in one. Collect leading Constraints before resolving projections in the function signature; this does not waive Constraint validation. In this example, the two clauses jointly form its Constraints. Function requirements use the [same indented placement](#841-function-requirements) with a Constraints-only region, not an executable body.

Every explicit or inferred generic argument at a call site must satisfy its clauses. Body type checking and instantiation may rely on those requirements. Constraints are not part of the function Signature; declarations differing only in their Constraints conflict.

#### 7.5. Unsafe functions

An **unsafe function**, declared with `unsafe func`, requires its caller to satisfy documented memory-safety conditions for their documented duration. Calling it requires an [Unsafe Block](#1433-unsafe-block); violating its safety contract is undefined behavior. This runtime safety contract is distinct from Constraints and their Constraint Clauses.

```kimi
// Safety: pointer must refer to a live, initialized i32 throughout the call,
// with valid range, alignment, provenance, and read permission.
// Access must obey reference, aliasing, and data-race rules.
unsafe func read(pointer: unsafe/i32) -> i32
    unsafe => return *pointer

// Safety: the same requirements as read.
unsafe func forward(pointer: unsafe/i32) -> i32
    unsafe
        return read(pointer)

unsafe func invalidRead(pointer: unsafe/i32) -> i32
    return *pointer // Error: unsafe func does not make its body an unsafe context.
```

`unsafe` does not affect the Signature or distinguish overloads. Resolve overloads without considering the caller's unsafe context, then check the selected call's requirement. Never substitute another overload because the selected function is unsafe.

Initially, unsafe functions support direct calls only. Taking a function value, assigning it to a variable, passing it as an argument, or converting it to an ordinary Function Type is forbidden. Unsafe Function Types are specified separately.

```kimi
let reader = read // Error: an unsafe function cannot be taken as a function value.
```

#### 7.6. Function expressions

##### 7.6.1. Syntax and inference

An anonymous function requires `func`, an optional Capture List, parameters, an optional result annotation, and a common Body (§7.1). No bare `(x) => x`, external parameter labels, defaults, optional parameters, or generic lambdas are introduced. Fix the body's expectation and use/discard context before checking it (§10.5).

```kimi
let twice = func (value: i32) => value * 2
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2 // Error: no fixed input signature.
```

An omitted parameter Type requires a fixed expected callable signature. Do not search parameter Types from body operations or later uses. Infer the result from that expectation or, after parameters are fixed, the body under normal result validation. Whole-result inference also retains [result Origins and Loans](#1582-closure-dependencies-and-call-results); annotations use existing elision.

Creation evaluates captures, not the function body. Invocation evaluates that body under an independent Function Boundary: no outer return/exit/continue/yield targets or inherited Unsafe permission. Capture acquisition itself occurs in the creation context. Named nested functions retain their no-capture restriction.

##### 7.6.2. Capture acquisition and environment

| List or entry | Creation effect |
| --- | --- |
| No list | Infer needed outer runtime bindings; Copy only when each complete Type is Copy |
| `[]` | Prohibit runtime captures |
| `[x, y]` | Acquire exactly the listed bindings; unlisted outer runtime bindings are unavailable |
| `x` | Ordinary Copy if Copy, otherwise Move |
| `x@ref` / `x@uniq` | The existing value Borrow/Copy/Reborrow operation for that Semantics |
| `var x` | Ordinary acquisition into a mutable environment binding |

Resolve captures by Binding Identity. An omitted list infers no Move, new external Borrow/Reborrow, or partial capture: reject a Non-Copy root even when only a Copy Field is read. Existing ref/T may be copied with its dependencies. Generic implicit capture requires declared Copy evidence at definition checking; unknown Copy is an error, not deferred checking, inferred Move, or a hidden Constraint. Explicit `[x]` can admit Copy/Move when the body and later source uses are valid for both.

Type names and accessible static function declarations are not runtime captures. Contextual `self` and setter `value` are never implicitly captured; explicit captures obey all receiver, accessor, construction, and destruction restrictions. Contextual storage is not a binding-name capture target; ordinary bindings named storage elsewhere use normal capture rules. No runtime receiver is implicitly bound into a function reference.

Explicit captures execute left to right, including unused entries. Earlier Moves and Loans affect later legality. Reject duplicate capture names and collisions with parameters. Inferred captures execute once each in order of first occurrence in selected source, including dependencies needed by nested Closures. Excluded compile-time source contributes no capture; legitimate deferred selection must resolve the set, order, and effects before environment and ownership finalization. Runtime reachability and optimization do not alter that set.

```kimi
let number: i32 = 10
let copied = func () => number             // Copy capture.
let text = makeText()                      // Assume string.
let invalid = func () => text              // Error: explicit list required.
let holder = func [text] () => ()      // Move executes even if unused.
```

Capture targets are binding names only. No aliases, initializer expressions, fields, inter-entry references, `@copy`, or extra object-borrow capture syntax is defined. `var` combines only with ordinary acquisition, not `@ref`/`@uniq`. Existing reference capture follows ordinary adaptation:

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

Environment bindings are not user Fields. Ownership-bearing calls apply ordinary local acquisition and Move Paths; a consumed `let` cannot be reinitialized. Shared/Exclusive calls cannot move out owned captures. No environment may borrow its own owned capture through another capture; external borrowed dependencies remain legal under lifetime rules.

Nested Closures acquire through every enclosing environment. An inner-only free binding still requires the outer Closure to capture it; outer `[]` or an insufficient explicit list is an error. Every omitted-list boundary independently requires Copy. Outer parameters and body locals need capture only when the inner Closure is created. Moving an outer environment value into an inner Closure makes the outer call Consuming; inner `@uniq` cannot exceed the outer binding's access.

```text
lexical x -> outer capture x -> inner capture x
            creation of outer  execution of outer, creating inner
```

##### 7.6.3. Call receiver and acquisition

Each concrete Closure has one minimum **Call Receiver Requirement**, inferred from all resolved operations in selected source:

| Minimum requirement | Internal receiver | Body access |
| --- | --- | --- |
| Shared | `ref/Self` | Shared access to the environment |
| Exclusive | `uniq/Self` | Exclusive mutation of environment or captured referents |
| Consuming | `owner/Self` | Move values out of the call's environment |

These are one body's access requirements, not three independently selected implementations. A Move on any possible body path requires Consuming call. Resolve legitimate generic effects before finalizing the requirement. Do not rerun overload resolution for each receiver, relax `let` or Property permissions, or rescue an otherwise invalid body with ownership. Internal `call` and receiver notation introduce no source member or hidden `self` name.

Direct calls acquire the minimum receiver under normal evaluation, access, initialization, and Loan rules. Exclusive calls require a writable owner, an existing exclusive borrow, or a writable temporary. A captured `uniq` alone does not grant exclusive access to a `let`-owned Closure. Consuming calls ordinarily Copy a Copy Closure or Move a Non-Copy one; no special forced Move is inserted. Borrowed access cannot Move an unowned environment, although a normally permitted Copy may provide a separate owned call value.

```kimi
let text = makeText()
let reader = func [text] () => inspectText(text@ref)
reader()
reader() // Shared call; Move capture does not imply consuming call.

let item = makeResource() // Non-Copy.
let take = func [item] () => item
let first = take() // Moves item from the consuming closure.
take()             // Error: the non-Copy closure was consumed.
```

The internal call signature retains complete receiver/parameter/result Types, per-call Origins, fixed captured Origins, and result Loan dependencies. Acquire receiver access before later argument evaluation and keep required Loans through the result's uses. Generic calls follow the declared [Callable receiver](#86-callable-constraints), even when instantiation reveals a weaker body requirement.

##### 7.6.4. Function references and common-type conversion

A resolved function reference produces its Function Item Type, including its bound generic arguments and Origin contract. Different declarations remain distinct. A Function Item is Copy and Shared-callable, is Owned when its bound arguments satisfy §15.2.3, and does not erase borrowed parameter/result contracts. A runtime method receiver is not automatically bound; follow explicit receiver argument rules. Unsafe functions and `deinit` cannot be acquired as values.

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

Erasure is an owning-container conversion distinct from source Copy/Move; it does not change the definition of Copy. No allocation count, size, physical layout, ABI, or inlining is guaranteed. Inline, stack, or heap placement and allocation elimination must preserve acquisition, validity, and cleanup. Required allocation failure causes ordinary Abort Termination, with no Move rollback or normal cleanup guarantee. Concrete `Callable` use creates no such erased container, but does not promise zero runtime cost.

### 8. Generics, constraints, and contracts

Generic parameters describe admitted Types and capabilities. Constraints establish the proofs available to a generic body; Contracts name capabilities and map their requirements to implementations. Explicit specialization changes implementation selection, not the original callable contract.

#### 8.1. Generic Type parameters

This section defines Type slots. Function length slots use explicit `<length N>` under [length parameters](#44-function-length-parameters); plain `<N>` remains a Type slot. A length slot accepts no complete Type and is not decomposed as a pair.

##### 8.1.1. Slots and projections

Both parameter forms consume **one complete Type argument**. Preserve Semantics, nested Types, and all Origins during binding:

```text
<T>    : T = W
<s/T>  : WholeType = W
         s = OuterSemantics(W)
         T = DirectTarget(W)
         o = OuterOrigin(W)       // Present only for an outer safe borrow.
```

`WholeType`, the projection functions, and `o` are explanatory notation, not source bindings. `<s/T>` declares only `s` and `T`; the original `s/T` retains W's Origins without naming them. Source Origin names use the separate [Origin parameter schema](#153-abstract-origins).

An ordinary `T` denotes a complete value Type, not only a bare Core. The pair's `T` has the fixed internal kind **SemanticsTarget**, whose value is a complete value Type or an Object View Target. Using it as a standalone value Type in a generic body requires proof of that role under the declared Constraints at definition checking; only the remaining proved symbolic substitution may be a [deferred obligation](#810-generic-body-checking-and-deferred-obligations). Its kind does not change at instantiation.

Normalize transparent aliases, resolved associated-Type projections, grouping, and redundant `owner/` before projecting. Reject alias cycles; retain nominal declaration identity and parameter Binding Identities. No new nominal-alias syntax is introduced. Split only the outer layer: `ref/(uniq/i32 from b) from a` yields `s = ref`, `T = uniq/i32 from b`, and outer Origin `a`. Since `owner/V` preserves V, `owner/(ref/i32 from a)` has outer Semantics `ref`.

| Outer Semantics | Direct target and requirements for applying these Semantics to another U | Outer Origin |
| --- | --- | --- |
| `owner` | DirectTarget(W) is W itself; `owner/U` is U for any valid complete value Type | None |
| `ref`, `uniq` | Complete Referent Type; `uniq` also requires exclusive acquisition and Loans at use | Required |
| `obj`, `rc`, `arc` | Supported Core or valid runtime-contract View Target | None |
| `objref`, `objuniq` | Supported Core or valid runtime-contract View Target; preserve borrowing requirements | Required |
| `unsafe` | Complete Pointee Type; adds no safe-borrow lifetime guarantee | None |

These rows do not extend the current runtime-contract or callable restrictions. Absence of an outer Origin does not erase payload dependencies: in `ref/(View<i32> from (source => a)) from b`, a belongs to the inner Type and b to the outer borrow.

Within one parameter list, every bound name must be distinct: `<T, T>`, `<s/T, s/U>`, and `<s/s>` are errors. References use Binding Identity under ordinary scope rules. `<s>` is an ordinary Type slot; standalone Semantics slots are not introduced. Built-in forms such as `Callable<ref, S>` have their own grammar, not general Semantics arguments.

##### 8.1.2. Reconstruction and Origin annotations

After transparent alias expansion, the original pair expression `s/T` refers to its WholeType W. The correspondence is determined by the declared bindings, not by two targets becoming equal after instantiation. `s/U` with another binding applies only the Semantics kind to U; it does not copy W's outer Origin. Once formed, equal complete Types do not acquire extra identity differences from their construction history.

An explicit Origin annotation uses ordinary Type-formation rules. Otherwise the original `s/T` retains W's Origins; another `s/U` uses the [position-specific Origin rules](#154-origin-elision-and-return-contracts). Storing a Type in a pair slot creates no additional omission permission.

For `W = ref/i32 from a`, `s/T from b` forms `ref/i32 from b`. Formation does not require a relationship between W's old outer Origin a and the new b, and performs no value conversion. It must still validate b's binding, its annotation position, and the formed Type's own inner-Origin outlives constraints. Fitting an actual `from a` value to this Type separately checks permitted shortening (`a : b`), acquisition, and Loans. In particular, `uniq/V` retains invariant V and any required Move/Reborrow; an annotation neither creates nor removes Reborrow.

##### 8.1.3. Argument validity

**WellFormedGenericTypeArgument(A)** requires a valid resolved complete Type, or a legitimately dependent Type with retained obligations. Check access, Semantics application, generic and associated-Type arguments, Origin mappings, and Constraints before using any Origin-erased comparison key.

| Argument | Rule |
| --- | --- |
| Primitive, struct, enum, Unit, Tuple | Allowed under ordinary Type formation |
| Common Function Type | Allowed under its existing signature, environment, and Origin restrictions |
| Function Item or Closure Type | Allowed through inference or an existing reference form; no new anonymous-Type spelling |
| Safe borrow, Object Type, raw pointer, Origin-bearing aggregate | Allowed as a Type argument; acquisition, storage, erasure, and Unsafe checks remain separate |
| Never | Allowed as a semantic Type argument, without adding a source name or value; a Never expression alone cannot infer an arbitrary unbound Type |
| Dependent Type | Preserve definition-side bindings and resolvable obligations until their deadlines |
| Unsized or unspecified representation | No new such Type is introduced; a valid Type must additionally meet layout requirements at each storage use |
| Bare Contract, group, bare Semantics, general value argument | Not a complete value Type argument; a Contract can appear only in an already permitted Object View Type |

Explicit argument lists supply every slot in declaration order, with optional trailing commas. Omitting the entire list uses only the inference supported by that construct. Partial lists, `_` placeholders, defaults, variadic slots, and general Const generics are not introduced. Only [function length slots](#44-function-length-parameters) admit the specified length arguments. `<s/T, U>` takes two arguments such as `<ref/i32 from a, string>`; `<ref, i32>` cannot supply one pair. Origin parameters have a separate schema and consume no Type argument slots.

A valid Type argument is not permission to use it in every role. Keep constraints on base Types, associated Types, finite layout, Copy derivation, and Object payload erasure. Ordinary storage preserves complete Type-argument dependencies under [storage contracts](#154-origin-elision-and-return-contracts), without an Owned or Storable requirement. Static storage instead requires Owned and the [static-source rules](#1132-static-storage).

#### 8.2. Constraints

The following terms distinguish declared capabilities, conditions, and their fulfillment:

| Term | Meaning | Example |
| --- | --- | --- |
| **Contract** | A Declaration Container declared with `contract` that specifies a named capability a Type provides. | `contract Comparable` |
| **Constraint** | A condition imposed on a Type or Type Semantics. | `T is Comparable` |
| **Constraints** | The set of conditions required for a declaration to be valid or usable. | The leading clauses of a generic function or structure. |
| **Conformance** | A Type's fulfillment of a Contract, with the required correspondence between requirements and implementations. | A Type fulfills `Comparable`. |

A **Constraint Clause** expresses a Constraint in the form `subject is requirement`. All clauses in a declaration's Constraints must hold. Subjects include complete Types, Semantics, valid target projections, and `Self` (the enclosing Type), subject to each requirement's role. Clauses establish capabilities the implementation may use. Each declaration kind restricts the permitted subjects; see [function Constraints](#74-function-constraints).

Core requirements may name a capability declared with `contract` or another compile-time type capability. Type Semantics requirements may name concrete semantics, such as `ref` or `obj`, or a semantics category. Requirements combine with `and`, `or`, `not`, and parentheses under the [requirement-expression rules](#83-requirement-expressions).

A `struct` header may contain generic parameters and an Origin list. Its ordinary Constraints precede members; conditional conformances may appear at member positions (§8.4.8).

```kimi
struct Container<s/T> origin owner, source
    T is Comparable
    s is reference

    var value: s/T
```

These clauses constrain the pair's target projection `T` and Semantics projection `s`. Each requirement must accept its subject's role. Constraints may also apply to the enclosing Type:

```kimi
struct ComparableContainer<T>
    T is Comparable
    Self is Comparable

    var value: T
```

`T is Comparable` supplies comparison capabilities for the stored value's Type. In a Type declaration, `Self is Comparable` is both a Constraint and a [conformance declaration](#844-conformance). It requires `ComparableContainer<T>` itself to fulfill `Comparable`; it does not derive an implementation from the clause on `T`. Required members are omitted here. The built-in `Self is Copy` retains its specific [derivation rules](#351-copy-capability-and-explicit-duplication).

#### 8.3. Requirement expressions

`subject is requirement` tests a Type or Type Semantics in Constraint Clauses and associated-Type declarations/specifications. Its result is a compile-time `bool`; unresolved requirements never become runtime tests. Each context retains its permitted subjects, grammar, and validation rules. Requirement Tests are not allowed in `#if` or `#case` Conditions. Ordinary expressions instead use [runtime type tests](#1361-runtime-is-tests); syntax context, including through parentheses, determines the interpretation before lookup, with no fallback between Type and Value namespaces.

`is` binds on its left at comparison precedence. Its right side consumes a requirement expression through `or` precedence. An immediately following `not` negates that entire right side:

| Form | Meaning |
| --- | --- |
| `T is A and B` | T satisfies both A and B. |
| `T is A or B` | T satisfies A or B. |
| `T is not A or B` | T does not satisfy `(A or B)`. |
| `T is A and not B` | T satisfies A and does not satisfy B. |

Use `T is (not A) or B` to limit negation to A. A complete Requirement Test cannot be embedded in an ordinary Boolean expression such as `(T is A) and enabled`: source syntax provides no such mixed context. Use separate Constraint Clauses for requirements and environment directives for independently configured source selection. Value equality uses `==`.

#### 8.4. Static contracts

A **Contract** is a Declaration Container defining a named capability through function requirements, Property requirements, associated Types, and Constraints. It has no executable implementations or storage. `Self` in a requirement denotes the conforming concrete Type.

```kimi
contract Source
    associate Element
    func read(self: ref/Self) -> Element

contract Sized
    property count: i32 has get

contract SizedSource: Source, Sized
    func reset(self: uniq/Self) -> ()
```

This revision defines static conformance checking and generic use. User-defined Contracts have no generic or contract-level Origin parameters and cannot capture an enclosing declaration's generic parameters. Ordinary generic Types/functions and separately specified built-in requirements such as `Callable<...>` are unaffected. Runtime Contract Views remain a [future extension](#85-runtime-contracts).

##### 8.4.1. Function requirements

A function requirement declares a Name, optional function Generic and Origin parameters, explicitly typed parameters, and an optional result Type whose omission means Unit. A function with a receiver is an **instance function**; one without a receiver is a **type function**. Receiver, parameter labels, ownership, Origins, and safe/unsafe conditions use ordinary function rules.

Function-specific Constraints occupy an optional indented region immediately after the header. The region contains one or more Constraint Clauses, with no executable statements, `return`, local declarations, or single-item body. Its subjects are that function's generic parameters or associated-Type projections rooted in them. Put Contract-wide Constraints at the Contract body level.

```kimi
contract Factory
    func create<T>(value: T) -> Self
        T is Copy
```

Requirement Constraints are premises for checking implementation compatibility; they are not silently added to the implementation declaration. Requirements have no default or optional parameters. Calls through a requirement supply every ordinary argument; defaults on an implementation do not permit omission through the Contract. Requirements and required accessors have no independent [access modifiers](#934-conformance-accessibility).

Property requirements use `has`; their selection and compatibility rules are defined in [Contract Property requirements](#114-contract-property-requirements).

##### 8.4.2. Refinement

`contract C: A, B` refines every listed parent Contract. Resolve parents as Contract Names, without generic arguments. Inherit all requirements, associated Types, and Constraints. Parent order gives no priority; direct or indirect cycles are errors. Do not use `Self is C` to declare refinement inside a Contract.

```text
Source                 Sized
  Element, read()        count
       \                 /
             SizedSource
               reset()
```

Conformance to a child entails conformance to every ancestor. Inherited `Self` still denotes the final conforming Type. A child may add requirements or Constraints but cannot remove or weaken inherited ones.

Paths to the same ancestor declaration introduce one Requirement Identity or associated-Type Identity. Independent declarations from different parents remain distinct even when their names match; one compatible implementation may satisfy several requirements.

Reject a refinement when its requirements cannot coexist as separate implementations under ordinary declaration rules and are provably impossible to satisfy with one implementation. Use both the [implementation-identification key](#845-implementation-matching) and ordinary [Signature](#91-signatures) rules: labels affect matching but cannot independently distinguish overloads. Different result Types alone do not prove a conflict; apply defined result compatibility, including the existing subtype and Never rules. Retain genuinely dependent checks until their prerequisites resolve; unknown is not a proof of contradiction.

##### 8.4.3. Associated types

An associated Type is restricted to a Core. Unlike an ordinary generic Type slot, it cannot bind an arbitrary Semantics-applied complete Type.

The only intrinsic exception is the Element requirement of Core.Iterable and Core.Iterator (§22.1), which binds a complete Type, including Semantics and existing Origins. Its explicit specification uses the same associate syntax; it adds no general complete-Type associated declaration facility. For ordinary Core-Type associated requirements, specify borrow Semantics and operation Origins at use sites. Existing dependencies inside a Type remain subject to ordinary Origin checking; generic substitutions must prove this restricted role or retain a legitimate obligation.

```kimi
contract BorrowSource
    associate Element
    func read(self: ref/Self) -> ref/Element from self
```

`associate` declares a new associated Type inside a Contract and specifies an existing associated Type inside a conforming Type. Both use `is`, never `=`, for conditions:

| Form | Meaning and location |
| --- | --- |
| `associate Element` | Declare an associated Type in a Contract. |
| `associate Element is Equatable` | Declare it with a capability requirement, or constrain the uniquely identified associated Type in an implementation. |
| `associate Element is i32` | Declare it with a fixed Type, or specify that Type in an implementation. |
| `associate C.Element is T` | Specify identity with a generic binding T, subject to the associated Type's Core restriction. |

Determine each associated Type uniquely from explicit specifications and Type-identity Constraints on the Contract or its ancestors. Do not infer bindings from implementation signatures, member search, or function bodies, and do not choose a Type merely because it satisfies a capability. Substitute bindings before matching implementations. An unconstrained `Source.Element` is not inferred as `i32` merely because an implementation of `read` returns `i32`.

**Qualified specifications.** `associate C.Element is T` selects an associated Type through a direct conformance or its ancestor `C`. An unqualified `associate Element is T` is valid only when exactly one distinct associated-Type declaration with that name is available across those conformances and refinements. Multiple paths to one declaration count once. Ambiguous names require qualification; a short form never applies to all same-named declarations. A bare `associate Element` is not an implementation specification.

```kimi
contract Destination
    associate Element

struct Pipe
    Self is Source
    Self is Destination
    associate Source.Element is i32
    associate Destination.Element is string
    public func read(self: ref/Self) -> i32 => 42
```

**Projections.** `T.C.Element` refers to the associated Type of `T`'s conformance to `C`; `T.Element` is the short form when the declaration is unique under the available Constraints. `C` uses ordinary Contract-name/alias lookup, not member lookup on `T`. Require conformance evidence; never discover it by searching for a same-named Contract. The Type-side base may be a named or constructed Core, a parameter, `Self`, or another associated-Type projection. Intrinsic protocol Element projections that bind a Semantics-applied Type do not become Core-Type qualifiers merely by being associated projections. It is not a value or Semantics-applied expression.

```kimi
func readOne<T>(source: ref/T) -> T.Source.Element
    T is Source
    return source.read()

func readInt<T>(source: ref/T) -> i32
    T is Source
    T.Source.Element is i32
    return source.read()
```

Collect leading function Constraints before resolving projections in its signature, including its result Type. Validate the Constraints themselves and discharge them at each use. Type context fixes a projection's namespace; preserve dotted syntax until Binding resolves Contract and associated-Type roles. Distinct successful interpretations are ambiguous. Expected results and fallback to value-member lookup cannot resolve that ambiguity.

**Refinement Constraints.** A child may constrain an inherited associated Type through `Self.C.Element`, where `C` is an ancestor. In a Contract-body Constraint subject, a bare associated-Type name is also permitted when unique among its own and inherited declarations. These clauses constrain existing declarations; they do not create replacement Types or conformances.

```kimi
contract Equatable
    func equals(self: ref/Self, other: ref/Self) -> bool

contract OrderedSource: Source
    Self.Source.Element is Equatable

contract IntSource: Source
    Self.Source.Element is i32
```

An `IntSource` implementation need not repeat its inherited `Source.Element is i32` binding. Explicit Type-identity facts support substitution and normalization; contradictory bindings are errors. Unresolved bindings remain obligations until the required finalization point. This adds no arbitrary associated-Type inference or proof search beyond the [limited proof rules](#87-constraint-proof-system).

##### 8.4.4. Conformance

In a Type declaration, `Self is C` is both a Constraint and an explicit declaration of conformance to `C`. Same-named members alone do not register conformance.

```kimi
struct NumberSource
    Self is Source
    associate Source.Element is i32
    public func read(self: ref/Self) -> i32 => 42
```

**Conformance Identity** is the pair of the concrete Type Identity and Contract Identity. Different associated-Type bindings do not create different conformances to the same Contract. Explicit conformance and conformance implied by refinement to the same pair denote one conformance; all simultaneously applicable associated-Type bindings and requirement-to-implementation mappings must agree under §8.4.8's path checks. Redundant explicit parent conformance is allowed without a required warning.

Unconditional generic Type conformance must hold for every binding allowed by the Type Constraints; conditional conformance uses the additional premises and use checks of §8.4.8. Successful selected instantiations do not validate an unconstrained definition. Verify ancestor conformances and all effective mappings. Conformance declarations generate no implementations except explicitly specified intrinsic derivations and the limited standard Property witness bridges of §11.4.2.

Retain the verified requirement-to-Member Identity mapping and associated-Type bindings. Contract calls use this mapping rather than rediscovering members in the caller's source environment or after instantiation. No external registration, replacement conformance, default implementation, or access-bypassing witness thunk is introduced.

**Inherited conformance.** A verified `(B, C)` is inherited by `D : B` only if every requirement, with requirement-side Self replaced by D, is satisfied by the retained mapping. If Member M was declared in A, its implementation-side Self remains A, even through A -> B -> D. Apply Type/Origin substitutions along D's base path to A and retain associated-Type bindings. Only an eligible borrowed receiver may use [base-subobject projection](#951-base-subobject-receiver-projection); other parameters/results gain no base conversion, and owning receivers gain no slicing. Check §8.4.5's access, Origins, Effects, and premises and §11.4's Property rules. Projected calls require published ObjectCompatible Proven (§12.4.4).

| Requirement shape | Inheritance through this path |
| --- | --- |
| Self only in a borrowed receiver, as in Stringify | Possible if all other checks and ObjectCompatible succeed |
| `other: ref/Self`, as in Equatable/Comparable | Fails: ref/A does not match ref/D |
| `func empty() -> Self` | Fails: A's result does not supply D |
| `owner/Self`, as in Iterable | Fails: no owning receiver projection |
| Fixed `Self.Element` normalizes to the same Type | Match the normalized Types; the spelling Self alone does not prohibit inheritance |

One failed inheritance path does not invalidate D's declaration. Determine `(D, C)` from all explicit, conditional, and Contract-refinement paths under §8.7; only a proof that all candidates fail gives Refuted. Unresolved generic dependencies remain Unknown until their deadline; invalid declarations or inconsistent evidence are Error. Successful paths must agree on associated Types and implementation mappings. New explicit conformance uses ordinary implementation lookup; inherited mappings are not replaced by same-named declarations.

Copy, Owned, Callable, and other intrinsic capabilities retain their own derivation rules. In particular, struct Copy requires the derived declaration's opt-in and all stored components; it is not inherited automatically.

Do not warn merely because an open base has a conformance its descendants cannot inherit. At a failing derived `Self is C` or constraint use, identify the requirement and cause: Self mismatch, owning receiver, access/Origin/premise failure, or ObjectCompatible NotProven and its responsible implementation. Diagnose an unresolved proof as Unknown, not Refuted. For example, an Equatable Shape may have a valid `Circle : Shape` without Circle being Equatable; requiring Circle's conformance reports the ref/Shape versus ref/Circle mismatch and the inherited-Name prohibition. When descendants need such Self-dependent Contracts, implementing them on leaf Types or using composition avoids this restriction; the base's own conformance remains valid.

##### 8.4.5. Implementation matching

Validate ordinary declarations first. Functions differing only in result Type, Origins, Constraints, or `unsafe` cannot coexist in one scope under the existing Signature rules; they are duplicate declarations before conformance matching.

After substituting `Self`, the conforming Type's arguments, and associated Types, identify a function implementation by the following key:

| Component | Required match |
| --- | --- |
| Name | Exact name. |
| Function kind | Type function or instance function. |
| Function generic parameters | Same count, kinds, and order; correspond by position, not spelling. |
| Ordinary parameters | Same count, order, and external labels; internal names need not match. |
| Receiver | Same presence and normalized Type structure, with only the inherited receiver correspondence allowed by §8.4.4. |
| Parameter Types | Same normalized Type structure. |

Type structure includes resolved Core Identity, Semantics, nested structure, and type arguments. Exclude Origin names, bindings, and lifetime relations from identification, but retain them for compatibility. There is no parameter-structure contravariance. Do not rank implementations by ordinary call overload preferences, adaptations, omitted arguments, Origins, Constraints, conditional-member applicability, or Effects. Conditions are checked after identification, unlike direct-call applicability (§8.4.8).

These rules identify Contract implementations; `Callable` and common Function Types retain their separate [callable signature compatibility](#107-callable-signature-compatibility) rules.

Zero candidates means a missing implementation; multiple candidates mean ambiguity, even if only one would pass compatibility. For one candidate, check:

| Aspect | Compatibility obligation |
| --- | --- |
| Constraints | Type/conformance and requirement premises must prove the implementation's Constraints and any conditional-member premises. |
| Input Origins | Every input allowed by the requirement remains valid; no stronger lifetime precondition. |
| Result Type | The same Type or a subtype permitted by the existing Type rules. |
| Result Origins | At least the required lifetime guarantees. |
| Access | Usable throughout the [conformance's effective domain](#934-conformance-accessibility). |
| Calling context and Effects | No stronger calling context or effects than the requirement permits. |

Compare Origin contracts after binder correspondence using ordinary variance and Loan rules, including invariance where required. A requirement admitting a call-local borrow cannot be implemented by a function requiring that input to be `static`. Origin-free identification does not erase dependencies or relax exclusive access.

Use existing Safety, ownership, Origin, and Access Effect checks; no new effect system is defined here. A safe requirement cannot require an unsafe calling context. Result compatibility inserts no numeric/user conversion, Copy, Borrow/Reborrow, or erasure. Core inheritance alone does not prove compatibility of complete Types. On compatibility failure, report the conformance error without searching for another implementation. Properties use the corresponding [accessor rules](#114-contract-property-requirements).

##### 8.4.6. Calls and shared requirements

Under `T is C`, generic code may use requirements and associated Types of `C` and its ancestors. A type function is called through the conforming Type, as `T.empty()`; it needs no instance.

```kimi
contract EmptyConstructible
    func empty() -> Self

func makeEmpty<T>() -> T
    T is EmptyConstructible
    return T.empty()
```

`EmptyConstructible.empty()` is invalid because it does not identify an implementation Type. Do not infer that Type backward from the expected result. A concrete call such as `Buffer.empty()` uses ordinary type-member lookup; generic requirement calls retain their conformance mapping.

Distinct Requirement Identities may form one call candidate only when both their exposed signatures/conditions are equivalent and their mappings select the same effective implementation Member Identity for every valid type substitution allowed by the current Constraints. Compare function generics, parameters/labels, receiver, results, Origins, Constraints, and calling conditions, plus the implementation's type substitutions and receiver correspondence. Equal code, runtime addresses, or optimizer sharing supply no proof. Keep the conformance requirements distinct. Repeated paths to the same Requirement Identity are already one requirement and need no such additional proof.

```text
A.reset --+-- equivalent call contract and same mapping proved
B.reset --+                         |
                               one call candidate
```

Use only the defined proof rules, not enumeration of instantiations or arbitrary theorem proving. If equivalence cannot be proved, retain the candidates and apply ordinary call selection; a non-unique result is ambiguous. A coincidental match in one instantiation cannot make an otherwise invalid generic call valid. No additional qualified-call syntax such as `value@A.reset()` is introduced; `@` retains its existing adaptation meaning.

##### 8.4.7. Intrinsic contracts and guarantees

The [required Core declaration table](#221-required-core-declarations) also fixes the identities and signatures used for `Stringify`, `Equatable`, `Comparable`, `Iterable`, and `Iterator`. Their source conformance uses the ordinary static Contract rules; their only special effects are the interpolation, comparison, and iteration mappings explicitly specified here.

Some Contracts are **compiler-intrinsic**, including `Copy`. Each has only the special acquisition, destruction, layout, concurrency, or code-generation effects explicitly defined for it. These effects belong to the compiler-recognized Contract Identity; a user Contract with the same name or requirements does not gain them. `Self is MyCopy` does not make a Type Copy. Compiler-derived conformance is available only where individually specified, and `Self is Copy` must pass its ordinary derivation checks.

Conformance proves only statically specified requirements. Documentation laws such as equality symmetry or transitivity are not enforced by the type system. It does not prove current initialization, absence of conflicting Loans, storage representation, or direct Field access. Ordinary usage checks and documented unsafe safety obligations still apply.

##### 8.4.8. Conditional conformance

A generic struct or enum may declare `Self is C when P`. Its ordinary Type Constraints D govern every use of the Type; P governs this declaration's conformance to C and does not become a Type-formation constraint. Normal Type, storage, Semantics, and Origin validity still apply.

```kimi
enum Option<T>
    Self is Copy when T is Copy
    Some(T)
    None
```

`Option<Resource>` remains a usable Type when Resource is non-Copy. This declaration makes `Option<i32>` Copy. In contrast, a leading `T is Copy` would restrict every Type use, while unconditional `Self is Copy` would promise Copy for every binding admitted by D.

###### 8.4.8.1. Conditions and implementation scope

The declaration targets one Contract and appears at a Type member position; ordinary Type Constraint Clauses still precede members. `when` is contextual only here. Conditions are comma-separated `subject is requirement` clauses, all conjoined. Subjects are the enclosing generic parameters, their Semantics/target projections, and associated-Type projections valid under existing rules. Requirements must accept their subject's role; no new parameters are introduced.

Conditions allow positive requirement atoms joined by `and` and parentheses. Atoms use existing Requirement Test interpretations. Reject `or`, `not`, value tests, and arbitrary Boolean expressions even inside parentheses. This restricted grammar does not change ordinary PositiveRequirement.

```kimi
Self is C when T is A and B, U is D
// Requires T is A, T is B, and U is D.
```

An optional indented implementation block declares members in the enclosing Type's namespace. It is a condition scope, not a new Type or Value namespace. Allow functions, computed members where the Type kind permits them, and associated-Type specifications. Reject Fields, enum Cases, constructors, deinit, nested Types, and nested conformance declarations. Enums still forbid computed members. Conditional conformance never changes storage layout or Case structure.

```kimi
contract Describe
    func describe(self: ref/Self) -> string

struct Box<T>
    var value: T

    Self is Describe when T is Describe
        public func describe(self: ref/Self) -> string
            return self.value.describe()
```

P is a published static precondition of each block member; it does not leak to other Type members. Collect D and P before resolving dependent signatures, associated-Type specifications, and bodies, and validate their conditions and noncircular evidence. The block may be omitted only when existing members or an explicitly specified intrinsic derivation satisfy the requirements. Do not generate ordinary Contract implementations.

###### 8.4.8.2. Verification and use

At definition, verify C under D and P for every admitted binding. Identify implementations by §8.4.5 (Property implementations by §11.4), not ordinary call overload ranking. Add each requirement's premises when proving the selected implementation's published conditions, access, Origins, ownership, and Effects. Compatibility failure never retries a different implementation. Fix and retain requirement-to-Member mappings, Field bridges where applicable, and associated-Type bindings at this stage.

At use, substitute Type arguments; for a Type satisfying D, proof of P enables the verified conformance. Do not select implementations again using caller lookup or favorable instantiations.

Direct member use has a distinct applicability step: after lookup commits a function group and Type inference/substitution completes, prove its member's P before Best Candidate comparison. The same condition applies to function references and computed access. Associated-Type projections require evidence of the relevant conformance.

| Judgment of P | Member applicability | Conformance use |
| --- | --- | --- |
| Proven | Condition satisfied; check other applicability rules | This conformance path is available |
| Refuted | Inapplicable | This path supplies no conformance; Type use remains possible |
| Unknown | Applicability unproven | Evidence for neither conformance nor absence |
| Error | Diagnose even if another candidate succeeds | Diagnose invalid declaration or condition |

Conditions never alter lookup's committed layer or the inherited-Name prohibition, and an inapplicable member cannot reopen outer/base lookup. Compare applicable members of the committed group by ordinary overload rules, without ranking condition strength. Loan or initialization failure after selection does not cause reselection. A generic definition cannot restore a member rejected for lack of proof merely because a later instantiation satisfies P. If a legitimate temporary Unknown can affect selection, defer the decision rather than committing an alternative.

Unknown dependencies and deadlines follow §8.7/§8.10. Required proof missing at its deadline is an error. Neither the declaration itself nor circular conformance search supplies evidence. Concrete absence requires completing all relevant paths, including ancestor conformances. Conditional syntax and definitions are checked even if no current Type arguments satisfy P; environment-only `#if`/`#switch` cannot replace these checks.

###### 8.4.8.3. Uniqueness and parent contracts

Allow at most one direct conformance declaration for each generic Type and Contract, counting unconditional and conditional declarations together after fragment merging. Do not permit multiple declarations by proving their conditions disjoint, choosing priorities, or specializing implementations.

```kimi
Self is C when T is A
Self is C when T is B // Error: duplicate direct conformance.
```

Block members obey ordinary duplicate rules; equal Signatures differing only in conditions are duplicates. Distinct Signatures may overload under the applicability rules above.

Conformance Identity remains (concrete Type Identity, Contract Identity). A direct declaration and Contract refinement may provide several evidence paths to that same conformance. At definition, check every pair under D and both paths' conditions: prove equal implementation mappings and associated-Type bindings unless §8.7 can establish that the paths cannot hold together. Failure to prove agreement is a definition error, not a deferred instantiation check; stronger optional condition solvers cannot broaden acceptance.

A Child conformance must satisfy its ancestor Contracts under its own conditions. For `Child : Parent`, verified declarations `Self is Parent when T is A` and `Self is Child when T is B` supply Parent evidence if either T is A or T is B is proven. The child path does not also require T is A, but every implementation it uses must be applicable. Redundant explicit parent declarations are valid only with the required path agreement.

###### 8.4.8.4. Intrinsics and boundaries

`Self is Copy when P` requests compiler-derived Copy under D and P. Check every complete own Field/payload Type and direct base under §3.5; user-defined Copy bodies remain forbidden. Deriving Copy for a `ref/T` component needs no T-is-Copy premise, but an explicitly written T-is-Copy condition is still required and cannot be weakened. Unconditional `Self is Copy` keeps its all-bindings guarantee. Unknown Copy follows the verified conditional acquisition plans of §8.9/§8.10, never an assumption of non-Copy.

This feature defines static conformance and generic use. It adds no external registration, extension declarations, partial Type specialization, condition-based implementation replacement, or new runtime Contract View feature.

#### 8.5. Runtime contracts

**Extension scope.** Contracts in this revision are static. Runtime-use designation and View associated-Type binding syntax remain to be defined; this section preserves the runtime design for that extension, not permission to form such Views in this revision. Object Views with concrete Core targets retain their existing rules. No implicit runtime designation or new `where` syntax is introduced.

Compile-time conformance and runtime View usability are separate. A runtime Contract exposes requirements through dynamic dispatch without instance state. A bare Contract name is never a value Type; the extension uses explicit Object Semantics such as `objref/C`, `objuniq/C`, and `obj/C`.

**`RuntimeUsable(C)`** holds exactly when `C` is explicitly designated for runtime use, it has no type-function requirements (including inherited ones), and, after fixing associated Types, every instance requirement satisfies:

| Aspect | Initial requirement |
| --- | --- |
| Receiver | Shared or Exclusive object access, without consuming ownership |
| Operation | Safe method or Property accessor |
| `Self` | No implementation-dependent `Self` outside the receiver |
| Binding | No unbound associated Types or method-specific generic parameters |
| Signature | Parameter/result Types are determined without knowing the hidden concrete Type |
| Origins | Expressible through borrowed receiver, explicit inputs, and `static`; no hidden payload Origin, contract-level abstract Origin, or higher-ranked requirement |

Check every getter result and setter requirement separately. Forming `objref/C` or another object form requires `RuntimeUsable(C)`, even when the concrete payload is unknown; it does not require searching all implementations. Unsupported requirements cannot simply be removed from the view. For example, `equals(other: Self)` cannot become heterogeneous comparison between arbitrary `objref/C` values.

**`Implements(D, C)`** checks explicit or [validly inherited conformance](#844-conformance). Every requirement has one implementation for D, with compatible signature, access, Origins, and published ObjectCompatible Proven (§12.4.4). Unknown does not establish this guarantee.

Use the unique static conformance for the concrete Type and Contract Identity; changing View bindings cannot create another conformance. Static conformance does not generally persist in derived Types. This extension must ensure that RuntimeUsable restrictions, fixed associated-Type bindings, and published implementation guarantees preserve its Supports relation through every derived layer. Static conformance alone is not sufficient proof of that invariant.

Contract refinement follows [static refinement](#842-refinement). No replacement conformance, default implementation, or external registration is introduced. Registration and artifact consistency follow [metadata](#2121-type-identity-and-descriptors). Static declarations and associated-Type specifications are defined; only runtime-use designation and View binding syntax remain deferred here.

```text
Speaker requires Shared speak() -> string
    ├─ Dog implements Speaker.speak
    └─ Cat implements Speaker.speak
```

```kimi
// Runtime extension: assume runtime designation, conformance, and makeDog() -> obj/Dog.
func announce(value: objref/Speaker) -> string
    return value.speak()

let dog = makeDog()
let message = announce(dog@objref/Speaker)
```

Only requirements accessible through `Speaker` are available from that view. Lifetime-preserving view formation additionally obeys [object upcasts](#1357-object-upcasts).

#### 8.6. Callable constraints

`F is Callable<r, S>` is a built-in Constraint requiring calls with receiver access `r` and signature `S`. `Callable<S>` abbreviates `Callable<ref, S>`. Initially `r` is the literal Semantics `ref`, `uniq`, or `owner`, and `S` is `(A1, ..., An) -> R`. The result may retain input Origins or already-bound external dependencies, but cannot borrow the hidden environment receiver.

| Constraint receiver | Receiver acquired by a generic call | Admitted minimum call requirement |
| --- | --- | --- |
| `ref` | `ref/F` | Shared |
| `uniq` | `uniq/F` | Shared or Exclusive |
| `owner` | F acquired by value | Shared, Exclusive, or Consuming |

Admitted Types are Function Items, concrete Closures, and common Function Types with [compatible signatures](#107-callable-signature-compatibility). No user `call` member is searched. `S` is a contract, not a conversion to an erased container. Preserve `F`'s complete dependencies under generic substitution; neither Copy nor Owned is required of `F`. `owner` denotes acquisition, whereas `Owned` denotes absence of non-static dependencies. The result restriction does not constrain direct concrete-Closure calls.

An omitted Origin on each direct `ref/T` or `uniq/T` parameter of `S` is independently bound **per call**, for all three receiver kinds. Conformance must hold for every valid call-time Origin under ordinary Type/Loan rules, not one fixed long-lived Origin. Origins nested within `T` and capture-derived Origins within `F` remain fixed and are not quantified. This limited input-borrow quantification adds neither general higher-ranked Origin syntax nor explicit `from` or Origin parameter declarations inside `S`.

Complete omitted result Origins using §15.4 with those per-call input Origins; retain already-bound dependencies. The receiver of `F` is not an elision input. For example, `Callable<(ref/T) -> ref/T>` returns a borrow valid for its argument's Origin. With several direct borrowed inputs, result elision uses their meet and retains all input Loans. A result requiring exclusive access must also preserve the corresponding exclusive Loan; shortening an Origin grants no access capability.

```kimi
func selectRef<T, F>(value: ref/T, select: ref/F) -> ref/T from value
    F is Callable<(ref/T) -> ref/T>
    return select(value)
```

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

Instantiation cannot turn an `owner` acquisition into a borrow merely because the body is Shared. Conversely, copying a Consuming callable does not satisfy a `ref` or `uniq` constraint. Select one public signature; for multiple available constraints with that signature, prefer the weakest declared receiver in order `ref`, `uniq`, `owner`. A later initialization, access, or Loan failure cannot select another receiver. Constraint strength is not an overload-ranking rule.

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

A Shared callable also qualifies, but a `uniq/F` argument still needs ordinary exclusive access. Shared environment access does not prevent use of a separate `uniq/T` argument's normal exclusive capability. By-value `F` parameters use normal Copy/Move; this constraint does not silently borrow them or guarantee repeated calls or non-escape. Generic conformance and result Origin/Loan obligations must resolve before finalization. A returned input borrow need not retain the callable receiver; no result may borrow call-local storage, including an `owner` receiver temporary.

#### 8.7. Constraint proof system

Constraint meaning and available proof are distinct. `and`, `or`, and `not` retain their Boolean meanings, but generic checking uses only the rules below. The accepted programs must not depend on optional SAT solving, arbitrary theorem proving, enumeration of Types, or optimizer-derived facts. Fully determined concrete conditions still use ordinary Boolean evaluation.

In this section `P` and `Q` denote validated, bound propositions. Parse requirement expressions under [requirement precedence](#83-requirement-expressions) before interpreting them: `T is (A and B)` supplies the propositions `(T is A) and (T is B)`, and similarly for `or` and the scope of `not`. This interpretation does not change source precedence. Proposition identity uses bound subject and requirement Symbols, substitutions, and normalized complete Types, including Semantics and Origins where applicable; equal source spellings alone are insufficient. Parentheses are transparent and `not not P` normalizes to `P`. No De Morgan, distributive, or other logical-equivalence normalization is added.

The proof judgment has four outcomes, distinct from the Condition evaluator's outcomes:

| Outcome | Meaning |
| --- | --- |
| **Proven** | The permitted rules establish the proposition. |
| **Refuted** | The permitted rules establish its negation; absence of proof is insufficient. |
| **Unknown** | Neither polarity is established by the permitted rules. This is not a Boolean value or an automatic right to defer. |
| **Error** | Invalid names, subjects, requirements, declarations, or detected contradictory evidence prevent a valid judgment. |

Evidence comes from the current declaration's validated input Constraints, defined built-in capability rules, and verified unconditional, conditional, or inherited conformance. Facts retain their Binding Identity, substitutions, and lexical/instantiation scope. A generic input Constraint is an assumption inside the constrained body, but must be discharged at use. A conformance declaration or `Self is C` obligation cannot prove its own implementation merely by being declared; its required implementations and prerequisite Constraints must be validated under the existing conformance rules. Built-in derivation exceptions such as `Self is Copy` retain their own rules.

| Proof rule | Permitted derivation |
| --- | --- |
| Exact assumption | A matching available proposition proves itself; an available `not P` refutes `P`. |
| Conjunction elimination | Available `P and Q` supplies both `P` and `Q`, recursively. |
| Conjunction introduction | Prove `P and Q` when both operands are Proven. |
| Disjunction introduction | Prove `P or Q` when either operand is Proven. This grants no evidence for the other operand. |
| Negation | Exchange Proven and Refuted for `not P`; Unknown remains Unknown and Error remains Error. Double negation is normalized as above. |
| Boolean refutation | Refute `P and Q` if either operand is Refuted; refute `P or Q` if both are Refuted. |
| Concrete atomic judgment | Use defined concrete Type-identity, Semantics/category, and built-in capability tests, or the closed conformance judgment below. |
| Verified conformance | Use a verified explicit or inherited mapping after proving its prerequisites, including inheritance matching (§8.4.4) and conditional premises (§8.4.8). Retain legitimate unresolved prerequisites; cyclic declarations alone prove nothing. |
| Contract refinement | From available `T is C`, use each ancestor conformance and inherited requirement Constraint, substituting `T` for `Self`. This does not discharge an unverified declaration's implementation obligations. |
| Associated-Type identity | Substitute and normalize explicit associated-Type specifications and available Type-identity Constraints under [associated-Type rules](#843-associated-types). Do not infer bindings from members or search for a satisfying Type. |
| Other cases | Unknown, unless validation requires Error. |

Validate all proof operands; Error absorbs even a determined truth result. Otherwise Refuted can refute a conjunction and Proven can prove a disjunction despite unknown operands. Exact compound assumptions are usable directly, but only conjunction elimination exposes their parts. `P or Q` plus `not P` cannot prove Q: there is no case analysis, contraposition, contradiction proof, or inference from contradiction. These limits govern symbolic proof, not Boolean evaluation of determined concrete judgments.

**Concrete and closed-world judgments.** Compare fully bound Types by normalized identity; use the defined rules for determined Semantics/category and built-in capability tests. Unbound Types and unresolved prerequisites are not negative results. Conformance absence is Refuted only after completing the concrete declaration, inherited and potentially applicable conditional conformances, and every relevant merge, selection, binding, and prerequisite in the fixed environment. Rule out all alternatives using the specified proofs. Failed lookup alone proves no absence; malformed conformance is Error, and an unsupported associated-Type operation is not a concrete negative.

**Recursion.** An active obligation revisited with identical arguments supplies no evidence; cycles and in-progress registrations establish no conformance or refutation. Consider independent finite evidence, including evidence found after a temporary cycle result. Otherwise retain Unknown until its deadline and diagnose if still required. Recursive declarations alone are valid. Built-in structural analyses use their own recursion/fixed-point rules; no general coinduction is implied.

**Contradictions.** After the specified normalization and conjunction elimination, directly available `P` and `not P` are contradictory evidence and produce Error. Likewise, evidence establishing both polarities of a queried proposition is Error. Do not derive arbitrary capabilities from an inconsistent environment. Detection of more complex contradictions by excluded logical transformations is not required and cannot supply proof.

**Use and finalization.** A required Constraint succeeds only when Proven. Refuted fails the requirement; Error reports invalid input or contradictory evidence. Unknown may be retained only when an identified later binding, instantiation, or prerequisite analysis can resolve it before the applicable deadline. Unknown without such a dependency is an unproven-requirement error when a proof is required. Every required concrete-call or instantiation obligation must be resolved before finalization. Unknown is never accepted, converted to Refuted, or used as evidence for a negated Constraint. Associated-Type inference beyond explicit identity facts and stronger symbolic reasoning remain design boundaries.

#### 8.8. Explicit full function specialization

An **explicit full specialization** supplies an implementation for one closed set of static generic arguments of an existing generic function. It preserves that function's call contract, but may produce different results or side effects. Selecting a matching specialization is mandatory, not an optional optimization.

Generic arguments here include function length slots. Evaluate them under [length rules](#44-function-length-parameters), leaving no unbound length. `LengthKey(N)` identifies a length by its integer value. The Type/Origin-specific checks below apply to Type slots.

##### 8.8.1. Declaration and target identification

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func process<T>(value: T) -> ()
    ()
specialize func process<i32>(value: i32) -> ()
    ()
```

`specialize` is a contextual declaration introducer before `func`. A specialization has a single unqualified Name, explicit Type arguments, explicitly typed parameters, an optional result Type, and a common single-item or indented Body (§14.2). It declares no Type parameters and does not automatically introduce the original function's Type parameter names into its body. Origin binders are inherited, not newly declared. Omitted results mean Unit, even if the original's substituted result is non-Unit; write that result explicitly.

Supply all of the function's own generic slots in declaration order, using their original kinds. Ordinary and pair Type slots each take one complete Type. After alias and associated-Type normalization, every Core and Semantics component must be fixed: no unbound Type/Semantics parameter, unresolved projection, or outer generic parameter may remain. `List<i32>` is closed; `List<T>` with unbound T is not. `Self` is allowed only when ordinary resolution meets the same rule. Origins are checked separately below. These restrictions apply to specialization declarations, not to dependent Types in ordinary generic bodies.

The target must be a named generic type function or instance method with an ordinary implementation. Constructors, `deinit`, accessors, and dedicated operator declarations are excluded. Partial or conditional specialization, omitted arguments, placeholders, priority rules, general Const arguments, specializing a generic Container's arguments, methods with unbound outer generic parameters, and explicit target-Identity syntax are not introduced.

First reject duplicate ordinary declarations. Then identify the original function:

1. Collect same-name generic functions in the same declaration Container with the same function kind (type function or instance method) and generic slot count.
2. Bind the written generic arguments using each candidate's slot definitions. Match the substituted receiver presence and Type structure and the ordinary parameters' count, order, and normalized Type structure. Form and validate complete Types before excluding Origins for this structural comparison.
3. Zero matches is an error; multiple matches is an ambiguous specialization declaration. For exactly one match, validate the inherited contract.

Do not use result Types, parameter names, Constraint satisfaction, Origin relationships, implicit adaptation, slot kind alone, ordinary overload ranking, or declaration/file order to resolve ambiguity. A failed contract check cannot select another target.

```kimi
func inspect<T>(value: T) -> () => ()
func inspect<T>(value: i32) -> () => ()
specialize func inspect<i32>(value: i32) -> () => ()
// Error: both ordinary declarations have the same substituted input structure.
```

Diagnostics distinguish no target, input-structure mismatch, multiple targets, and contract mismatch after selection. Include candidate declaration locations and relevant Type, receiver, or arity differences; diagnostic comparison never chooses a nearest candidate.

##### 8.8.2. Inherited contract

The specialization header identifies and checks the original contract; it is not an independent call Signature.

| Item | Specialization rule |
| --- | --- |
| Receiver and ordinary parameters | Restate count, order, and substituted Type structure; no implicit adaptation |
| Result | Match the substituted Type; omission means Unit |
| External parameter names | Match the original; not used to identify the target |
| Internal names | Ordinary parameter names may change using `external => internal: Type`; an inherited receiver remains self under §7.3 |
| Origin and Loan contract | Inherit binders and preserve every admitted Origin binding |
| Access, defaults, optionality, generic Constraints | Inherit; do not redeclare, strengthen, or weaken them |
| Attributes, Safety, calling convention, Effect requirements | Inherit existing rules; no attributes or additional modifiers on the specialization declaration |

Write an inherited optional parameter without `?` or a default. Callers still use the original omission and default-evaluation rules:

```kimi
func find<T>(value: T, count?: i32 = 1) -> () => ()
specialize func find<i32>(value: i32, count: i32) -> () => ()
find<i32>(10) // Evaluate the original default, then call the specialization.
```

The restriction concerns the specialization declaration, not ordinary syntax inside its body. A specialization of an unsafe function inherits its Safety contract, but unsafe operations still need an Unsafe Block. Contract inheritance introduces no unsupported async, exception-Effect, or variadic feature.

Validate all necessary Type Origins before comparison. After matching inherited binders, the body must work for **every Origin binding permitted by the original contract**. Do not add an Origin parameter or lifetime restriction, narrow applicability by Origin, or register another implementation for different Origins. A specialization cannot rescue an invalid ordinary implementation or replace a declaration-required proof; [deferred generic checking](#810-generic-body-checking-and-deferred-obligations) retains its stated design boundary.

Inferred ObjectCompatible is a [common implementation guarantee](#12443-implementation-families), not an additional declaration obligation inherited from the ordinary body. A specialization may make that public guarantee NotProven without being invalid for that reason; declared Signature, Constraints, Safety, and Effect requirements still apply.

##### 8.8.3. Selection and declaration ownership

Use the [Implementation Selection Key](#2132-identity-and-generation-keys): the original function's declaration Identity and its ordered, normalized static generic arguments with only Origins excluded. Preserve nominal identity, nested structure, and every Semantics layer, including an outer object handle. `obj/D` and `rc/D` therefore have different keys even when they identify the same dynamic payload Type. Two specialization declarations with the same key are an error.

Calls and function references first use ordinary lookup, overload resolution, inference, and the original contract's Type, Constraint, Origin, and Loan checks. Specializations never enter the candidate set or supply inference evidence. Once the static generic arguments determine a key, use its matching specialization, or the ordinary implementation if none exists. Do not inspect a value's Dynamic Type, including behind a base or Contract View, to change this selection.

```kimi
// classify and its i32 specialization are declared above.
func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let number: i32 = 10
let result = forward<i32>(number@ref) // 1, including with shared generic code.
```

A generic caller cannot be fixed to the ordinary implementation merely because its generic arguments were initially unknown. This guarantee is independent of code sharing, separate compilation, optimization level, and LTO. Function references obey the same choice and their existing restrictions, including the ban on unsafe function values.

There is no direct name for the specialization or syntax to bypass it and call the ordinary body. Calling the same function with the same generic arguments from its specialization selects that specialization again and is recursive; move common work to a helper. A specialization-body error never falls back to another body or overload.

Specializations belong to the original function's Kotonoha and declaration Container. Existing Container fragments may put them in another file; unrelated extensions and other Kotonoha libraries cannot add or replace them. The defining Kotonoha closes the specialization set after environment selection and declaration collection, including generated sources, before finalizing affected call targets and no later than artifact finalization. Declaration and loading order cannot affect selection. Verification, artifact information, and invalidation follow [generic code generation](#213-generic-code-generation).

#### 8.9. Generic access effects

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
                            -> constraints / instantiation
                            -> determine effect -> finalize ownership and cleanup
```

Do not treat unresolved Copy capability as proof of non-Copy or fix the effect to Move. At definition checking, prove legality for every effect admitted by the declared Constraints. The exact effect may remain symbolic until instantiation only if every admitted case is legal, including subsequent uses, Loans, and cleanup. This is delayed effect determination, not delayed discovery of a required capability. Environment-changing directives still obey their earlier [selection deadlines](#194-name-resolution-boundary).

Instantiations may have different effects. Substitute the already-verified effect plan and derive each concrete body's cleanup; never reuse a different-effect analysis without validation. Compile-time directives do not test Types or select Access Effects. The [generic verification principle](#810-generic-body-checking-and-deferred-obligations) requires the ordinary body to be valid independently of explicit specializations.

```kimi
// s is a declared Semantics parameter; value is an initialized owned value.
value@s
use(value)
```

Discarding the first result ends its shared Loan for `s = ref`; `s = uniq` also requires exclusive writability. Check the later use after that Loan ends. If Constraints admit non-Copy `s = owner`, the first use Moves the source and makes the definition invalid, even if current callers all use Copy values. Require Copy evidence or restrict Semantics and prove borrow permissions. If the result is retained, check subsequent uses throughout its Loan lifetime.

#### 8.10. Generic body checking and deferred obligations

**Universal body verification.** An ordinary generic body must be semantically valid for every well-formed Type/length/Origin argument binding satisfying its declared Constraints, any enclosing conditional-conformance premises, and public Signature requirements. Verify this before accepting or exporting the definition, including definitions with no uses. Use the limited proof system of §8.7 and symbolic Type/Origin/effect rules; do not enumerate available Types or infer a hidden capability Constraint from the body. Failure to establish the required proof is a definition error. Explicit specializations cannot rescue an invalid ordinary body.

This requirement fixes meaning, not a physical compiler-pass schedule. Dependencies on other declarations may delay checking within the build, but an unverified definition cannot be accepted merely because selected concrete instantiations succeed. In particular, a generic call to another generic function must prove that function's declared requirements from the caller's declared premises.

For unknown Copy, an acquisition that is legal as either Copy or Move may keep a conditional effect plan. A subsequent read requiring the source to remain Initialized must be legal in both cases; otherwise require explicit `T is Copy`, a borrow that avoids acquisition, or a valid reinitialization before reuse. The conservative state is usable for proof, but the emitted operation must still Copy a Copy Type and Move a non-Copy Type. Never silently turn a possible Copy into a Move, and never add `T is Copy` to a caller's applicability conditions after checking the body.

~~~kimi
func transfer<T>(value: T) -> T => value // Valid for both Copy and Move.

func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func invalidTwice<T>(value: T) -> (T, T)
    return (value, value) // Error at definition: Copy is not guaranteed.
~~~

Generic stored acquisition and custom/computed/required getter results retain their declared Types (§11); none uses a Copy-dependent getter-result family. Copy/Move effects may remain conditional only after all cases are verified. Shared sequence and Pattern reads retain their separately defined correlated result families (§4.6.6); do not generalize Field acquisition to those operations.

A **Deferred Obligation** records remaining substitution or representation work for a verified definition, not an unproven body capability. Record its kind, defining bindings/environment, source location, declared premises, symbolic proof/effect plan, dependencies, and deadline. Unknown names, missing conformance, use-after-Move possibilities, and unresolved overload ambiguity are not deferrable until a favorable instantiation.

| Stage | Required work |
| --- | --- |
| Definition | Resolve definition-side names and roles; prove body Type correctness, selected operations, capability requirements, and symbolic ownership/Origin/cleanup legality under the declared contract |
| Permitted dependency | Retain proved symbolic Type/effect families and explicit representation obligations; Unknown is neither success nor evidence of non-Copy |
| Instantiation | Check the call's declared contract and ordinary argument/Loan validity; substitute verified plans; resolve concrete layout, representation, and exact effects without adding semantic use conditions |
| Finalization | Discharge representation obligations and complete concrete ownership/cleanup plans before lowering executable operations; never retry committed lookup or overload selection |

For example, using the target projection T as a local Type inside `func f<s/T>(x: s/T)` must be justified by the declaration's Constraints and slot rules. If an admitted binding could make it an Object View Target rather than a value Type, reject that use at definition time; do not wait to reject only the affected callers. Positions that prohibit generic parameters outright, such as a base Type, remain prohibited.

**Public dependent obligations.** Only Type/Origin well-formedness conditions implied by the written Signature, generic schema, associated-Type requirements, Constraints, and published conditional-member premises may restrict semantic applicability. They must be available to callers and artifacts without inspecting a private body. Such conditions are fixed when the declaration is checked; they cannot contain a newly inferred body requirement such as Copy, an extra Contract, or a favorable acquisition case. This revision introduces no separate source syntax for arbitrary hidden requirements. A need that cannot be expressed or proved with the existing contract makes the definition invalid.

Concrete layout/representation validity may still depend on substitution or the prepared target, including finite representable storage for an otherwise well-typed body local. Record such dependencies in the definition artifact with their source and target requirements before clients instantiate it. These checks concern representability only and cannot disguise a Type, capability, or lifetime restriction. A call satisfying the public contract must not fail later because its callee newly discovers a semantic body requirement. Ordinary caller-side initialization and Loan checks, specified runtime checks, target representation failures, and documented compiler resource exhaustion remain distinct; resource exhaustion is not semantic invalidity.

ObjectCompatible (§12.4.4) is a completed public operation guarantee used by projection/object-call legality, not a new conditional applicability premise or a deferred body requirement. Verify its common implementation family before publication; caller-specific instantiations cannot strengthen it. Changes use §21.3.4's dependency revalidation.

Diagnostics for definition errors identify the body use and missing declared proof; instantiation diagnostics identify the already-published dependent/representation obligation, definition site, arguments, and failed condition. Preserve verified summaries and plans in compile-time metadata even when runtime keys erase Origins. Do not add caller aliases/extensions, redo overload choice for favorable concrete Types, or use obligation strength to rank candidates. Environment directives provide no Type/capability evidence and retain their earlier selection deadlines.

### 9. Names, signatures, and access

Name resolution identifies declarations; overload resolution selects an applicable operation before use-site legality is checked.

During [Mod generation](#2072-compilation-and-binding), these rules may produce provisional results from the declarations currently available. Such results do not commit lookup or overload selection for the final program; final Binding runs after generation is complete.

| Term | Meaning |
| --- | --- |
| Binding / Name resolution | Associating source names and operations with declarations and meanings. |
| Lookup environment | The declarations and aliases available for a lookup in one scope; extensions are a future design. |

Module terms, including Compilation root, project root, and source environment, follow [§18](#18-modules-and-dependencies).

Resolve Names before selecting overloads or checking whether an operation can execute:

```text
Name Resolution
├─ Type name -> Type Name Selection -> Type Legality
└─ Call      -> Candidate Applicability -> Best Candidate -> Usage Legality
```

**No Backtracking:** once a stage commits its result, a later failure must not select another lookup stage, declaration, or overload. Inaccessible or wrong-role declarations do not commit lookup. Legitimate dependencies may defer resolution; unknown Names and unresolvable dependency cycles are errors.

Apply language-defined lexical visibility and compile-time selection order first. File loading, alias order, candidate enumeration, caching, parallelism, and optimization must not change resolution.

#### 9.1. Signatures

A Signature determines whether declarations may coexist in one scope:

| Declaration | Signature |
| --- | --- |
| Type declaration | Name and generic parameter count |
| Function | Name, generic parameter count, ordered normalized parameter Types including the receiver |
| Constructor | Declaring structure and ordered normalized parameter Types; no ordinary Name or receiver parameter |
| Field / computed / Property Requirement | Name |
| Enum Case | Name within its enum; no payload overloads |

**GenericArity** counts generic argument slots, including function length slots; a pair counts as one. **OriginArity** counts explicitly declared Origin parameters, excluding Origins projected from a Type slot and inference variables. Origin schemas govern binding and fragment compatibility, not additional overloads. Thus `View<T> origin source` and `View<T> origin left, right` cannot coexist as same-name, same-arity Type overloads.

Normalize by resolved Symbol and Kotonoha/version, expanding transparent aliases and resolved associated-Type projections and removing grouping and redundant owner prefixes. Preserve every Semantics layer. Represent generic expressions structurally using the declared binder and slot position:

| Source expression | Normalized expression |
| --- | --- |
| Ordinary T | `Slot(i)` |
| Pair s / pair T | `OuterSemantics(Slot(i))` / `DirectTarget(Slot(i))` |
| Original pair `s/T` | `Slot(i)` |
| s applied to another slot U | `Apply(OuterSemantics(Slot(i)), Slot(j))` |
| Length N / fixed array | `LengthSlot(i)` / `FixedArray(LengthExpression, ElementType)`; compare lengths under [normalization](#44-function-length-parameters). |

Apply these rewrites recursively and compare by structural alpha-equivalence. Do not simplify using accidental equality after instantiation or arbitrary Constraint proofs. Pair target T alone is not Slot(i). Slot kind controls binding and validation but cannot alone distinguish overloads: `f<T>(value: T)` and `f<s/U>(value: s/U)` conflict. `ref/T` and `uniq/T`, including receivers, remain distinct. Applied Semantics distinguish use-site Types, not Container identities.

For Signature comparison only, exclude Origin names, lists, and lifetime relations. Retain complete Types and Origin contracts for semantic checks. Return Types, external/internal parameter names, defaults, optionality, access, unsafe modifiers, and Constraints cannot independently distinguish overloads.

An **API signature**, for [accessibility checks](#932-api-signature-accessibility), includes the Types and requirements exposed by a declaration, including results and Constraints. This is broader than the Signature used above for overload identity; exclusion from overload identity does not exempt a component from accessibility checking.

```kimi
struct Reader
    func read(self: ref/Self) -> i32 => 0
    func read(self: uniq/Self) -> i32 => 0 // Distinct receiver Semantics.

func identity<T>(value: T) -> T => value
func identity<U>(value: U) -> U => value // Error: same normalized Signature.
```

Duplicate Signatures are declaration errors. Distinct Symbols imported from different Containers may have the same shape; a use is ambiguous unless overload rules select one. Fragment header-name agreement is separate from parameter-name normalization between different function declarations.

#### 9.2. Namespaces, roles, and visibility

Namespaces separate declaration kinds; a **Lookup Role** filters candidates by syntactic use before lookup stops.

| Namespace | Declarations |
| --- | --- |
| Type | Containers, Kotonoha reference names, Core and Semantics parameters, associated Types, `Self`, and built-in Semantics/category requirements |
| Value | Functions, Fields, computed members, enum Cases, parameters, locals, local functions, function length parameters |
| Origin | Origin declarations |
| Label | Labels; use the dedicated transfer-target rules |

| Lookup Role | Namespace | Eligible declarations |
| --- | --- | --- |
| Core | Type | Structures, enums, associated Types, `Self`, and generic bindings proven to meet this role |
| Object View Target | Type | Cores or runtime contracts; validate instantiation and runtime usability after selection |
| Type Semantics | Type | Semantics parameters and language-defined Semantics |
| Qualifier | Type | Groups, Kotonoha references, and Types that can qualify members |
| Requirement | Type | Capabilities, Types, Semantics, and categories allowed by requirement syntax |
| Declaration Container | Type | Containers allowed as an alias or other Container target |
| Value | Value | All value declarations, including non-callable values |
| Enum Case | Value | Cases of the enum fixed by a CaseReference qualifier or the expected Type |
| Origin / Label | Corresponding namespace | Origin / Label declarations |

A declaration may serve several roles. A group is a Qualifier, not a Core. Role filtering does not inspect generic arity, argument Types or labels, expected results, satisfied Constraints, or the existence of a later member. Object-target syntax selects the View Target role; subsequent RuntimeUsable failure does not reopen lookup. There is no callable-only role for `f()`.

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

Pattern names follow [arm-local scopes](#1482-binding-scopes), with separate candidate and body Identities. A guard candidate is not a capture source, even when its read Type is Copy.

`Self` is reserved and requires a Type or Contract requirement context. `self` and `value` are contextual receiver/accessor bindings; while active they cannot be redeclared. Stored accessor `storage` designates its own slot (§11.2), not a capturable local; elsewhere storage is an ordinary Name. Contextual runtime bindings are not implicitly captured. Origin and Label lookup never falls back to Type or Value names.

#### 9.3. Accessibility and reachability

| Access | Scope |
| --- | --- |
| `private` (ordinary default) | Declaring Container and bodies lexically nested within it |
| `internal` | Same Kotonoha |
| `protected` | Declaring structure and bodies of its directly or indirectly derived structures |
| `protected internal` | Same Kotonoha **or** the protected scope |
| `private protected` | Same Kotonoha **and** the protected scope |
| `public` | Also accessible from other Kotonoha libraries, subject to enclosing restrictions |

The protected scope includes bodies lexically nested in the declaring or qualifying derived structure. Protected forms apply only to structure members and their accessors; they are invalid on root/group declarations, group members, and contract requirements. They do not enable nested Container declarations inside structures. A declaration has one access specification; only the two compound forms shown above may combine access words, in the shown order. Duplicate or other combinations are errors. `open` is an inheritance modifier, not an access level. Non-inheritable structures may retain protected members, but gain no derived access sites.

Accessibility grants permission; **Name Reachability** supplies a valid path through scopes, qualification, aliases, or explicit re-exports. Both, plus role compatibility, are required. Public declarations are not automatically imported. Each enclosing Container and access path must allow access; aliases and re-exports cannot widen it. API signatures obey the domain checks below, and consumers naming their Types or requirements must additionally have a reachable path under the [module rules](#18-modules-and-dependencies).

Private access uses the merged Container Symbol, not the file. Other fragments of that Container have access; unrelated declarations in the same file do not. A parent does not gain access to a child's private members merely by containing it. A future extension must not gain the target's private access. Ordinary accessors inherit their Property's access and may only narrow it. [Enum Cases](#631-cases-and-payloads) and [Contract requirements](#934-conformance-accessibility) instead inherit their declaring Container's effective domain without independent access modifiers. Locals, parameters, and contextual names use lexical visibility instead of access modifiers.

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

##### 9.3.1. Effective access domains and protected receivers

**Effective access domain** Access(D) is the source contexts allowed by D’s access intersected with every enclosing named Container’s domain. A public member of a private group remains confined to that group. Root private/internal declarations are Kotonoha-local; public roots may be accessed from other libraries without an extra project-root cap. Dependencies and Name Reachability still apply.

Compute domains from Symbol identity, merged Container relationships, and the validated inheritance graph, not file paths or currently observed callers. Internal access refers to the originating Kotonoha, not a package, workspace, source directory, or all libraries in a build. No separate package or friend-module access is defined. Declared public access must remain valid for future consumers, even if no other module currently references it. `internal` and `protected` are not linearly ordered: the former includes unrelated code in one Kotonoha, and the latter can include derived code in another.

Protected access to a member of base B from derived D requires a receiver whose static Effective Core is D or derived from D; a generic receiver may use a proven base constraint. Static B or a sibling of D is insufficient regardless of runtime Type. Check accessors independently. This extra receiver restriction does not apply inside B’s lexical body, to same-Kotonoha access via `protected internal`, or to static members. `private protected` requires both module and protected conditions. Access permission supplies neither a receiver nor an undefined conversion.

For generic declarations, the protected lexical scope includes structures derived from any construction of that declaration; the instance-receiver check still uses the actual derived receiver Type. Inheritance alone never grants private access. A future extension must receive no special private or protected privilege merely by targeting a Type; its design must use its own lexical access context.

For example, if `Left` and `Right` derive from `Base`, code in `Left` may use a protected `Base` Property through a `ref/Left` receiver, but not through a `ref/Base` or `ref/Right` receiver. Ownership, borrow permissions, and accessor availability must also hold.

##### 9.3.2. API signature accessibility

Every declaration `D` must satisfy the following for each concrete Type or requirement `T` exposed in its API signature:

```text
Access(D) is a subset of Access(T)
```

Check the effective domains, not merely the written access modifiers. This rule applies to private, internal, and protected declarations as well as public declarations. A direct base Type must satisfy the same condition with the derived Type as `D`. Apply the rule after declaration merging and Type normalization; an invalid exposed signature is a declaration error even if never used. An inferred Type is checked once established and cannot evade this rule.

Check function/constructor parameters and results (including receivers/constructed Types), enum payloads, Field and Property/accessor Types, and generic/associated-Type requirements. Use the enum’s domain for payloads, the Property domain for its header/storage Type, and each accessor’s for additional signature components. A restricted setter does not narrow the Property. Check Constraints even when written inside a body, and validate exposed Origins under their scope/lifetime rules.

Check API Types recursively. A constructed generic Type’s access domain intersects the declaration’s and all concrete arguments’ domains; other compound Types require every constituent to be accessible. Semantics cannot hide an inaccessible Core or View Target. Expand aliases. For associated projections, check the qualifier, defining requirement, and any exposed concrete binding; unresolved projections retain obligations. A Type’s appearance in an API does not expose its private fields or implementation bodies.

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

##### 9.3.3. Generic bodies and separate compilation

Check generic body access to nondependent Types and helpers in the declaration's definition-site context. Deferred Binding and instantiation retain that context and the original resolved Symbols. A public generic body may use private implementation Types or helper functions without exposing them in its API signature. Instantiation in another Kotonoha does not recheck those implementation accesses as if the body were written by the caller, grant the caller private access, or require the helpers to become public.

At a generic use, check supplied Types and constraints using the normal use-site rules. A caller may supply an accessible private Type to a public generic function; instantiation does not turn that concrete instance into a new externally public declaration. A wrapper or other declaration that exposes the resulting constructed Type must independently satisfy API signature accessibility. Preserve sufficient private implementation metadata for specialization without making its Symbols source-addressable to consumers.

##### 9.3.4. Conformance accessibility

Contract requirements, associated-Type requirements, and required accessors cannot declare independent access modifiers. Their effective access is that of the declaring Contract; the ordinary `private` default does not apply.

Conformance has no independent access modifier. For Type `T`, Contract `C`, and each required implementation `M`, require:

```text
Access(Conformance(T, C)) = Access(T) intersect Access(C)
Access(Conformance(T, C)) is a subset of Access(M)
```

Use effective domains, including enclosing Containers, not modifier spellings. Check every ancestor conformance independently; a narrower child Contract cannot narrow an ancestor conformance's implementation obligations. Do not shrink conformance to fit a private member or bypass access with a generated witness thunk. Select the implementation first, then validate access without retrying selection. API signature accessibility still applies to exposed Types, Constraints, and associated-Type bindings.

```kimi
public contract Readable
    property value: i32 has get

public struct Example
    Self is Readable
    private var storedValue: i32 = 0
    public computed value: i32
        get(self: ref/Self) -> i32 => self.storedValue
        private set(self: uniq/Self, value: i32) -> () => self.storedValue = value
```

The required getter is public; the extra setter may be private. A private getter would fail this conformance. Required accessors are checked separately; additional accessors retain ordinary access rules.

#### 9.4. Unqualified lookup

For the required namespace and role, search these stages in order. A bare Adaptation Target Name in `E@X` uses the two roles specified by [explicit operations](#135-explicit-operations); it does not select a role using conversion success.

1. Current local scope, then enclosing lexical and parameter scopes, each separately.
2. Current Container.
3. Parent Containers, separately, through the project root.
4. Direct-dependency Kotonoha reference names in the Compilation root, for Type qualifiers only.
5. Explicit aliases of the use's SourceDocument, together.
6. Compilation default aliases, together.

Type parameters and contextual names occur at their declaring lexical positions. Stage 4 finds library reference names, not arbitrary library members.

At each stage, collect same-name declarations in the namespace, deduplicate paths to the same Symbol, and filter by role and accessibility. Stop at the first stage with eligible declarations. Same-name functions form one candidate set; a function and a Field/computed member, or other distinct value kinds, conflict at that stage. Type candidates proceed to Type Name Selection.

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

A nearer group `X` does not stop Core lookup for an annotation `X`, but does stop Qualifier lookup for `X.member`. If that group lacks `member`, do not switch to an outer `X`. Similarly, an integer local `f` stops Value lookup and makes `f()` a non-callable-value error.

There is no implicit `self`: instance members require `self.member` or another explicit receiver. An unqualified reference that finds only accessible instance members reports a missing receiver instead of searching for an outer static member.

If all stages fail, prefer an inaccessible matching-role declaration diagnostic, then an accessible wrong-role diagnostic, then undefined Name. Diagnostic exploration of outer declarations never makes them valid fallback targets.

#### 9.5. Qualified and inherited lookup

Resolve the first component of `A.B.C` by ordinary lookup with its syntactic role, then search only the selected target's members. Do not return to its parents. Intermediate Type-side components are Qualifiers; the final role follows the syntax, such as Core in a Type annotation or Declaration Container in an alias. [Associated-Type projections](#843-associated-types) additionally interpret `T.C.Element` through `T`'s conformance and resolve `C` as a Contract Name in the source environment, not as a member of `T`. Binding distinguishes projection paths from ordinary qualified paths; distinct successful interpretations remain ambiguous.

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

Member lookup searches ordinary members. An accessible, role-compatible ordinary member commits lookup even if no overload applies. There is no active extension stage in this revision. **Future extension constraint:** ordinary members must precede extensions; inaccessible or wrong-role members alone must not block them. A future design must specify lexical, source-alias, and default-alias enablement stages and must not automatically add argument-associated Containers.

**Inherited ordinary lookup.** After substituting base arguments, search the statically selected struct, then its direct bases, one layer at a time. Commit to the first layer with accessible, role-compatible declarations. Its same-name functions form the entire overload set; do not merge farther base overloads. Inaccessible/wrong-role declarations alone do not commit. Receiver compatibility, generic/argument applicability, accessors, and Loans are later checks and cannot reopen lookup. Instance/type functions share the Value role, so invalid receiver use cannot skip a nearer layer. Use this selection for a new explicit Contract implementation, followed by conformance checks. Already inherited conformances retain their [verified mapping](#844-conformance).

A derived `f(string)` with an accessible base `f(i32)` is a declaration error under the [inherited-Name rule](#622-inheritance-and-open-structures), regardless of call sites. Without an eligible derived f, lookup finds the base group. Reuse of a Name inaccessible to the derived author remains possible; the layer-commit rule still governs that case. Receiver projection below adds no ordinary derived/base conversion.

Generic bodies use their [definition-site source environment](#18-modules-and-dependencies), including during deferred instantiation; caller aliases and extensions never enlarge their candidate sets.

##### 9.5.1. Base subobject receiver projection

For `receiver.member`, when ordinary lookup selects an instance declaration in a base `B` of the receiver's static Effective Core `D`, **Base Subobject Receiver Projection** locates that declaration's inline base subobject along the unique inheritance path. Substitute base Type/Origin arguments at each layer. Check accessibility, including protected-receiver restrictions, against the original receiver before projection. Static members need no projection; Type-qualified unbound calls and function values retain ordinary argument rules.

For a declaration receiver `ref/B` or `uniq/B`, form the corresponding shared Borrow or exclusive Borrow/Reborrow of that subobject using the original receiver's permissions. A shared receiver cannot supply exclusive access. Evaluate the source once, before explicit call arguments; preserve its storage anchor, nested dependencies, and parent Loan restrictions. A borrowed custom/computed accessor or method borrows the base subobject as a whole. Standard Property access instead projects to its permitted storage Place under §11.1.2 without forming a whole-base borrow; preserve the original owned/borrowed/object receiver classification.

For ordinary value receivers, rank `ref/D -> ref/B` and `uniq/D -> uniq/B` as same-semantics reborrow, and `owner/D -> ref/B`, `owner/D -> uniq/B`, and `uniq/D -> ref/B` as cross-semantics Borrow/Reborrow under [argument adaptation](#102-argument-adaptation-and-literals). These are member-receiver operations only, never Exact conversions. Projection adds no preference based on inheritance depth and cannot reopen lookup. Candidate analysis records operations without committing them before selection.

An ordinary borrowed-receiver implementation used through projection requires published [ObjectCompatible Proven](#1244-object-receiver-compatibility). Reject a use of NotProven without inspecting the private body or retrying overload selection. A method that replaces all of self may work on a complete B, but cannot replace the base inside D. Neither projection nor an escaping unrestricted exclusive borrow may permit whole-base Move, Replacement, reconstruction, or acquisition of a sliced owner. The body retains its declaration's Self and result contract. Receiver-derived results keep the projected Loan and cannot outlive the original storage; construction, destruction, and ancestor-completeness restrictions still apply.

For Object Semantics, adjust the receiver of the statically selected, ObjectCompatible implementation. Preserve the complete object's identity, Dynamic Type, metadata, and cleanup. Projection creates no public object view or owning/counting handle and changes no reference count. An owning receiver requirement (`owner/B`, `obj/B`, `rc/B`, or `arc/B`) cannot be satisfied by projection; it needs an independently permitted acquisition or explicit object upcast.

Projection is confined to this member operation. It adds no standalone base-view expression, argument/result conversion, subtype relation, bound-method value, or change to the explicit adaptation tables.

#### 9.6. Type name selection

After committing lookup, filter Type candidates by the number and kinds of explicit type arguments. Resolve the arguments themselves in the use-site context. Select exactly one candidate; zero means type-argument mismatch and several mean ambiguity. Check the selected Type's Constraints afterward, without trying another Type if they fail.

For example, `Box<i32>` selects `Box<T>` from a stage containing `Box<T>` and `Box<T,U>`. A nearer stage containing only `Box<T,U>` blocks an outer `Box<T>`. Different same-arity Types imported at one stage remain ambiguous. Legitimate unresolved argument kinds defer selection with its stage fixed; malformed arguments or unknown Names are errors. Omitted type arguments use only the inference permitted by their construct.

### 10. Overload resolution and inference

Use the distinct relations in [Type relations and expression operations](#38-type-relations-and-expression-operations): argument adaptation, expected-result compatibility, and acquisition legality are separate judgments. A successful subtype proof does not select or authorize a value operation.

#### 10.1. Candidate applicability

Check each declaration in the committed function group against the following requirements, subject to §10.5's shared-expectation and body-checking boundaries. These steps do not authorize checking a nested call or anonymous body separately for each candidate:

1. Validate explicit type-argument count and kinds.
2. Match positional and named arguments and record omitted defaults.
3. Infer type arguments from the receiver and explicit arguments.
4. Use an independently known expected result Type to fill remaining type arguments, without changing those already fixed.
5. Check permitted argument adaptations, Function Types, Constraints, and any conditional-member premises (§8.4.8) after substitution and before Best Candidate comparison.
6. Reject instantiated result Types incompatible with the expected result, if present.

Zero applicable candidates is an error. Candidate checking records plans; it does not execute or commit runtime Copy/Move, Loans, or defaults. Errors in declarations, such as unknown Types, malformed Constraints, or duplicate Signatures, remain declaration errors even when another candidate succeeds.

Positional arguments precede named arguments and bind parameters in order. Named arguments use external names, may be reordered, and cannot bind a parameter twice. Reject unknown labels, excess positional arguments, and missing required arguments. Evaluate explicit arguments in source order, then omitted defaults in parameter order; defaults follow [declaration-site rules](#72-parameter-names-and-defaults) and supply no generic-inference evidence. Function-value calls supply every argument positionally.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // Valid; evaluate by before value.
scale(3, value: 4)     // Error: value supplied twice.
scale(3, factor: 4)    // Error: factor is an internal name.
```

#### 10.2. Argument adaptation and literals

Compare adaptations in this order, best first:

| Class | Meaning |
| --- | --- |
| Exact | Normalized Type compatibility requiring no adaptation operation, including no borrow/reborrow |
| Literal fitting | Directly fit an unresolved literal to the candidate's Type |
| Same-semantics reborrow | Reborrow while preserving the input Type Semantics |
| Cross-semantics borrow/reborrow | Another permitted borrow or reborrow |

Exact describes Type adaptation, not value transfer: an Exact by-value argument still Copies or Moves under [Copy and Move](#35-copy-and-move). Copy versus Move adds no ranking preference. Origin subtyping that needs no value operation remains permitted.

The initial borrow adaptations are listed below. In the first two rows `T` has owner Semantics; in value-reborrow rows it is the complete immediate Referent Type. Adding a layer around an existing reference or object handle requires a fully specified explicit [storage-borrow target](#1355-explicit-borrow-and-reborrow); it is not an additional implicit argument adaptation.

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

[Inherited receiver projection](#951-base-subobject-receiver-projection) defines its member-only operations and rankings separately; those rows do not apply to ordinary arguments or unbound calls.

Fixed-expectation [common function conversion](#764-function-references-and-common-type-conversion) is handled separately and adds no rank to this table. Do not add implicit object upcasts, numeric-width, signedness, integer/float, or user-defined conversions, or unlimited dereference/conversion chains. Raw dereference is explicit. Existing Never and Origin rules remain Type rules, without new overload priorities.

Untyped integer literals fit representable candidate integer Types directly; floating literals follow the numeric rules. Do not default to `i32`/`f64` before fitting, prefer narrower widths, or break overload ambiguity using defaults. Outside candidate comparison, independent expressions without an expected Type use the ordinary numeric defaults. Generic inference processes receiver, other-argument, and known-result constraints before defaulting. `null`, empty collections, and untyped functions gain no universal fallback Type.

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(1)      // Error: both integer Types fit.
let x = 1      // Independently defaults to i32.
choose(x)      // Exact i32.
choose(1@i64)  // Exact i64.
```

#### 10.3. Expected results

This is the static expected-result judgment in [the relation table](#38-type-relations-and-expression-operations), not the implicit-expression-adaptation judgment.

An expected result may complete inference and exclude otherwise applicable candidates. Compatibility requires normalized Type identity or a defined subtype relation without additional value operations. Instantiate and check Origins, including permitted covariant shortening. Do not insert a new borrow/reborrow, dereference, numeric conversion, or user conversion to retain a candidate. Do not retype a function's body literal to change its established return Type.

Expected results do not rank candidates by result-conversion quality. Result Loan/Origin propagation and Copy/Move still apply. An expectation comes from a surrounding annotation, fixed parameter, or declared result, subject to §10.5; it cannot circularly select its own source candidate. Discarding a call supplies no expected Unit Type. Constructs with Unit-fixed Target Result Types follow §14.2; their directly discarded calls still receive no Unit expectation.

```kimi
func fetch(value: i32) -> string => "text"
func fetch(value: i64) -> i64 => value
let text: string = fetch(1) // Selects fetch(i32) by result compatibility.
fetch(1)                   // Error: discarding leaves both candidates.
```

These declarations have distinct parameter Signatures. Declarations differing only in return Type are invalid regardless of call-site expectations. A candidate returning `T` cannot survive an expected `ref/T` by borrowing its result; nor can `uniq/T` become `ref/T` by a newly inserted reborrow.

#### 10.4. Best candidate

Pairwise comparison yields better, worse, equivalent, or incomparable. **Proceed to the next step only for equivalent candidates.** Select a candidate only if it is better than every other applicable candidate:

1. Compare adaptation quality for the receiver and each explicit source argument. A dominates B only if it is no worse everywhere and better somewhere. All equal proceeds; opposing advantages are incomparable. Match named arguments by the same source expression, not candidate parameter order. Exclude defaults and never sum numeric costs.
2. Compare substituted parameter Types at the same positions. A dominates B if every Type is equal or a defined subtype and at least one is a strict subtype. All equal proceeds; unrelated Types or opposing subtype advantages are incomparable.
3. Prefer a function with no generic parameters of its own, including length parameters. A generic enclosing Type alone does not make the function generic.
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

These rules deliberately leave owner-to-`ref`/`uniq` overloads ambiguous. Any preference would require a language revision. Field Move eligibility and its generic limits follow [Field Move](#1112-move-paths-and-inherited-fields).

#### 10.5. Inference boundaries and specialization

Fix local Types at declaration; later uses cannot infer backward. [Named functions](#7-functions-and-callable-values), including local functions, methods, and Contract requirements, have an explicit result or Unit, independent of access and body form. Neither bodies nor callers infer their signatures. Check [API accessibility](#932-api-signature-accessibility); substituting a written generic result does not reanalyze the body. Local binding and [anonymous-function inference](#761-syntax-and-inference) remain available.

Explicit Type arguments follow the [slot rules](#81-generic-type-parameters). Omitted defaults do not infer generic arguments. Explicit specializations do not participate in inference or overload applicability; select an implementation only after the original declaration and its static generic arguments are determined.

Generic inference and substitution use the [complete-Type slots and projections](#81-generic-type-parameters), preserving all bound Origins and inferred Loan requirements, including nested dependencies. A surrounding borrow such as `ref/T` retains `T`'s internal dependencies alongside its own Origin and Loan. Substitution alone creates no Borrow/Reborrow, releases no Loan, and extends no lifetime; actual call-site checks follow [Ownership and Origin rules](#15-ownership-and-lifetime-analysis).

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

Resolve function references using ordinary evidence, including explicit generics or a fixed expected callable signature. A unique declaration needs no expected Type; an unresolved overload set is not a value. Function values have no labels/defaults and cannot name unsafe functions or deinit. Conformance failure cannot change the chosen overload or capture mode.

**Anonymous body context.** Process explicit Types, independently typable arguments, and generic constraints before checking the body, independent of argument order. Arity and explicit Types may filter candidates. Use the written signature, a common remaining expectation, or an already selected candidate's signature; `Callable<r, S>` may guide parameters while F retains its concrete Closure Type. If unresolved candidates cannot provide the needed expectation, require an annotation. Do not inspect return expressions to select candidates, retry bodies/captures across candidates, or infer parameters from later uses.

If the normalized return Type is Unit before body checking, discard the single-item expression (§7.1). Otherwise use Value Context, including return inference. Unit inferred from another argument before body checking is allowed; its spelling or origin does not matter. Do not reinterpret the body/captures after later Unit inference or generic instantiation. A standalone lambda without an expectation can infer its return; an already typed function value cannot erase its return to Unit. No overload preference between using and discarding lambda results is added.

~~~kimi
func run(action: () -> ()) => action()
func run(action: () -> i32) => action()
run(func () => compute()) // Error: differing expectations; compute returns i32.
run(func ()
    return compute()
) // Same error: an explicit return does not select an expected signature.
run(func () -> i32 => compute()) // Explicit return Type selects the second overload.
func apply<U>(value: U, action: () -> U) -> U => action()
apply((), func () => compute()) // U is Unit before checking the lambda; discard its value.
// func make<T>() -> T => 123 is invalid for arbitrary T; instantiation cannot rescue it.
~~~

After inference, apply the [limited proof system](#87-constraint-proof-system): Proven satisfies a requirement, Refuted rejects it, and Error diagnoses invalid/contradictory evidence. Unknown may retain only a legitimate dependency resolvable by its deadline; it proves neither applicability nor negation. Generic capabilities must be proved at definition acceptance (§8.10). Bind nondependent Names at the definition and use declared Constraints; failure to prove `T is C` does not prove `T is not C`. Deferred members retain the [definition environment](#18-modules-and-dependencies), never caller imports. Prove all necessary Constraints before concrete finalization. No arbitrary theorem proving, Type enumeration, or constraint-strength ranking is allowed.

Environment-selected membership follows §19.4: excluded declarations do not merge or enter candidate sets. Conditional-conformance members (§8.4.8) remain in ordinary lookup; their published conditions affect applicability, not lookup stopping or syntax selection. Preserve each defining generic environment.

#### 10.6. Usage legality and operators

After selection, check unsafe permission, initialization and Move state, actual Loans and lifetimes, required accessor access, write capability, and other control-flow or ownership conditions. Static Type and declaration permissions needed for adaptation are checked earlier; flow-dependent failures never change the selected overload.

```kimi
unsafe func inspect(value: i32) -> () => ()
func inspect(value: ref/i32) -> () => ()
let number: i32 = 1
inspect(number) // Select Exact i32, then reject without an Unsafe Block.
```

Adding a better overload can invalidate existing calls even if it later fails Usage Legality. Failed Move, Loan, or Property permission checks cannot retry a borrow, getter, or same-name declaration.

Operator operands preserve the fixed syntax, evaluation order, and permitted adaptations. Built-in operations and the comparison Contract mappings in §13.4.1 define the available operator candidates. No extension operator candidates exist in this revision. User-defined arithmetic, general indexer declarations, and additional ambiguity-resolution syntax remain deferred; ordinary lookup must not invent them.

Diagnostics distinguish undefined/wrong-role/inaccessible names, path or value-kind conflicts, missing receivers, type-argument or argument mismatch, no applicable overload, ambiguity, inference boundaries, dependency cycles, declaration errors, and usage errors. Show candidate Signatures and declaration locations for ambiguity; retain useful rejection reasons without dumping every tentative error. Resource-limit exhaustion is separate from language ambiguity or mismatch and must request annotations or smaller expressions, never choose the first candidate.

#### 10.7. Callable signature compatibility

Callable variance uses the static Type relations in [the relation table](#38-type-relations-and-expression-operations); common Function Type conversion and receiver acquisition are separate operations.

Common Function Type conversion and `Callable<r, S>` use one rule. For implementation `(A1, ..., An) -> R` and required `(P1, ..., Pn) -> Q`, require equal arity, `Pi <: Ai` at every position, and `R <: Q`. Here `<:` means normalized complete-Type identity or an already defined subtype relation, retaining Semantics and Origins. Parameter labels, defaults, and optionality cannot bridge a mismatch.

Compare parameter/result Origins and Loans for every admitted call; quantified Origins correspond by binder, not spelling. Fixed captures cannot become fresh quantifiers. Compatibility inserts no Borrow/Reborrow, dereference, numeric/user conversion, or argument/result erasure and never reinfers committed Types. Covariant Origin shortening remains valid; exclusive Core invariance and result Loans remain mandatory. Receiver adaptation is separate and adds no argument conversions.

Implicit erasure applies only after the expected common Function Type is fixed. No overload ordering between a concrete direct match and an erasure conversion is defined; use an annotation or typed intermediate when that comparison would be needed. Allocation cost never ranks candidates. Receiver, Copy, Owned, or Loan failure cannot reopen selection.

#### 10.8. Generic argument inference

Apply the additional [fixed-array inference rules](#44-function-length-parameters) to lengths and literal element counts. Keep lengths alongside Type, Semantics, and Origin bindings within each candidate, with results independent of argument traversal order.

Within each candidate, bind explicit arguments first. Otherwise collect structural Type, Semantics, and Origin constraints from the receiver and independently typable arguments together; do not fix the first input and adapt later inputs to it. Use an independently known expected result only for still-unbound parts, without changing input-established Types or Semantics. Apply the existing nested-expression boundaries and literal fitting, using literal defaults only after other evidence has been processed.

Infer unbound s directly from the source's outer Semantics. Do not search implicit Borrow/Reborrow or other conversions for a common Semantics: owner and ref evidence for the same s conflicts. Once a target Type is fixed, including by explicit arguments, check normal adaptation separately. Origin inference uses the [limited principal-solution rules](#1534-generic-origin-inference).

Solve structural, Semantics, and Origin constraints to a fixed point, resolving every required slot uniquely and consistently. An occurs-check rejects infinite substitutions such as X = ref/X; nominal recursive Types instead require valid layout. Cycles supply no result. Retain unresolved work only for an identified dependency that can resolve by its deadline.

Do not use argument/candidate traversal order, arbitrary conversion chains, common-base search, or Constraint strength to choose a solution. Constraint substitution uses the same binding table as Type expressions: `s is reference` checks the Semantics projection; the pair's `T is C` checks its target projection and that requirement's role. Ordinary T retains its complete Semantics. Then check fixed-target argument adaptation, substituted Constraints, and expected-result compatibility; flow-dependent acquisition, Loans, and cleanup follow selection.

#### 10.9. Inference and operation design boundaries

The following boundaries remain separately specified. Implementations must not invent them through broader search:

- General Const arguments beyond [function lengths](#44-function-length-parameters), standalone Semantics slots, partial/default/variadic generic arguments, and partial/conditional specialization are not introduced.
- Additional implicit argument/receiver adaptations beyond the defined applicability table; exact contextual-binding boundaries for additional accessor/function forms. The explicit Borrow table does not add implicit overload preferences.
- Operator/indexer candidate collection and explicit selection syntax; combining optional `?` with external/internal parameter-name syntax. Constructor collection is defined under [constructors](#623-constructors).

### 11. Properties

A concrete Property is `let`, `var`, or `computed`. A Contract uses `property` to require operations.

```text
Property
├─ let       immutable storage
├─ var       mutable storage
├─ computed  accessor functions, no storage
└─ property  Contract requirement, no storage promise
```

| Declaration | Get | Set | Declaration initializer |
| --- | --- | --- | --- |
| `let` | Required; standard when omitted | Forbidden | Optional for instance; required for static |
| `var` | Required; standard when omitted | Required; standard when omitted | Optional for instance; required for static |
| `computed` | Body required | Optional; body required if present | Forbidden |
| Contract `property` | Required operation | Optional operation | Forbidden |

Concrete Properties belong in struct/group/rootgroup bodies: instance in a struct, static in a group/rootgroup. Enums do not permit them. Local `let`/`var` remain bindings; local computed declarations are forbidden. Properties share the Value namespace. Each accessor appears at most once, in either order. Attributes follow §6.5; inline `has` is only for requirements.

A **Stored Property** is a let/var declaration with one storage slot. Storage kind is determined by the declaration, never by accessor bodies. In storage, layout, and Move Path rules, **Field** means this slot, not a separate declaration kind. All source access to it obeys the Property's operation permissions.

| Type contract | Meaning |
| --- | --- |
| Stored `: T` | Storage Type; standard value acquisition also produces T |
| Stored custom get | Result exactly T; T must be proven Copy |
| Stored custom set | Input exactly T; result Unit; no Copy constraint |
| Computed `: T` | Getter result Type; setter input may differ |
| Contract `property p: T` | Required getter result Type; explicit setter input may differ |

Compare complete Type structure and bound Origin dependencies (§11.3), not merely the Core. Copy capability selects acquisition, never a conditional result Type such as `T` versus `ref/T`.

#### 11.1. Standard access and acquisition

**Standard get is a Place access contract, not a getter function.** A bodyless get/set selects a standard operation. Omitted accessors are supplied according to the declaration table; customizing one does not change the other.

```kimi
public let limit: i32 = 100
public var count: i32 = 0
public var resource: Resource
    get
    private set
```

Standard get exposes permitted operations on storage. Value acquisition Copies T if Copy, otherwise Moves only from a Movable Place (§15.1.5). Borrowing follows existing adaptation rules without first acquiring the value. Standard set directly initializes or replaces storage under ordinary state and cleanup rules.

| Direct storage operation | Required accessible standard accessors |
| --- | --- |
| Copy or shared borrow | get |
| Move from let | get |
| Move from var | get and set |
| Exclusive borrow | get and set |
| Write | set |

All rows additionally require valid receiver capabilities, initialization/completeness, Origins, Loans, Move Paths, and construction/destruction conditions. A custom accessor cannot satisfy a requirement for a standard accessor. Simple assignment needs the final target's set, not its get; getters needed to locate that target are checked separately (§13.7).

| var configuration | Read/borrow target | Update |
| --- | --- | --- |
| Standard get + standard set | Storage, subject to the table | Direct set |
| Custom get + standard set | Getter result; no direct storage borrow/Move | Direct set |
| Standard get + custom set | Storage Copy/shared borrow; no direct Move/exclusive borrow | Call set |
| Custom get + custom set | Getter result; no direct storage access | Call set |

Shorthand borrows retain §13.5.5 semantics. For a slot of Type F = ref/U, `@ref` copies the stored shared reference; `@ref/F` borrows the slot as ref/ref/U. Specify the complete slot Type when requesting slot access. Failed Reborrow cannot retry as slot borrowing.

##### 11.1.1. Access and mutability

Move is destructive access, not assignment. Moving a var requires accessible standard set but does not call it or add a receiver write-capability requirement. A let binding may be consumed; Move does not reset its first-initialization history. A moved let cannot be reinitialized or expose its slot for exclusive borrowing. Restoring var requires ordinary write permission.

```kimi
// Outside the declaring struct; holder is owned and complete.
// resource has public standard get and private standard set.
let r = holder.resource       // Error: set is inaccessible for var Move.
let view = holder.resource@ref // OK: shared storage borrow.
```

Slot immutability is not deep immutability. A stored reference or object handle may grant access to a separate referent under its own capabilities and Loans.

##### 11.1.2. Move paths and inherited fields

Safe storage Move requires an owned struct Place, an eligible static Move Path, completed construction, an Initialized and complete target, and valid access, Origins, Loans, and ancestor deinit conditions. Borrowed/object receivers and static storage cannot supply safe extraction. Raw-pointer operations retain their Unsafe rules.

Inherited lookup selects a Field Identity and unique base path, substituting Type/Origin arguments. Following this path does not acquire intermediate bases or imply a ref/Derived-to-ref/Base conversion. Preserve the original receiver category; inheritance grants no private access. Track state and Loans by base path and Field Identity. A base subobject itself cannot independently be Moved, replaced, or reconstructed.

Every storage projection checks the permissions of enclosing Properties. Child writes, borrows, and consumes—including implicit method-receiver or operator adaptations—cannot bypass an ancestor's custom or inaccessible setter. References reaching separate referents follow their own capabilities. A path crossing a custom getter continues from its result under §11.2.3, never its hidden storage.

```kimi
// position: Point is Copy, with standard get and custom set.
object.position.x = 10       // Error: bypasses position's setter.
object.position.x.modify()   // Error if modify requires an exclusive receiver.
var next = object.position
next.x = 10
object.position = next      // OK: calls the setter.
```

Standard access may use a complete remaining Place after a sibling Move. A custom accessor/computed call needs a complete receiver and retains its declared function footprint; callers cannot infer disjointness from its body. Initial-construction restrictions still apply even when all slots are initialized (§11.3.1).

```kimi
// Both fields have standard accessors; partial Move/deinit conditions hold.
let item = object.resource
let count = object.count       // OK: complete remaining storage.
let shown = object.displayCount // Error if this getter borrows incomplete object.
object.resource = makeResource() // Restore through accessible standard set.
```

##### 11.1.3. Generic acquisition

Standard accessors need no Copy constraint. A stored custom getter needs proof of T is Copy under the declaration's generic premises, not merely for a chosen instantiation. Custom setters need no Copy constraint but must be valid for every admitted T.

```kimi
struct Box<T>
    public var item: T

func view<T>(box: ref/Box<T>) -> ref/T from box
    return box.item@ref/T

func readCopy<T>(box: ref/Box<T>) -> T
    T is Copy
    return box.item

struct AssignedBox<T>
    public var item: T
        set(self: uniq/Self, value: T) -> ()
            storage = value
```

AssignedBox cannot supply unconstrained T by value through standard get: its non-Copy case would require forbidden storage Move. Shared borrowing remains available.

When Copy is unproven, verify the conditional Copy/Move state under §8.10. Runtime Copy values still Copy; the result Type remains T. Later use must be valid in both cases. Reinitialization or explicit borrowing may establish valid continued use. Do not retry overload selection after a Move/Loan failure.

```kimi
func test<T>(box: Box<T>) -> ()
    let x = box.item
    inspect(box@ref) // Error: box may be incomplete after non-Copy Move.
// Adding T is Copy makes this subsequent shared use valid.
```

#### 11.2. Accessor functions

Custom and computed accessors explicitly declare receiver, input, and result Types; bodies infer none of them. Both accessors use the common Body (§14.2). A single-item getter follows its fixed declared return Type; a setter discards its single-item expression and completes with Unit. Explicit returns still fit the declared result. Default/optional parameters are forbidden. Static accessors have no receiver. A setter's value parameter is an initialized immutable binding with ordinary argument acquisition and cleanup.

Stored instance custom get uses `self: ref/Self` and returns storage Type T, which must be Copy. Custom set uses `self: uniq/Self, value: T` and returns Unit. Static forms are `get() -> T` and `set(value: T) -> ()`.

```kimi
public var level: i32 = 0
    get(self: ref/Self) -> i32
        return storage
    set(self: uniq/Self, value: i32) -> ()
        storage = clamp(value, 0, 100)
```

Inside a stored accessor, contextual `storage` denotes its own slot without recursive accessor invocation. It obeys receiver capabilities, let/var mutability, initialization, Origins, and Loans. It is unavailable in computed and requirements. A custom getter may Copy a stored reference of Type T; this differs from exposing a direct borrow of the slot, which requires standard get.

##### 11.2.1. Accessor accessibility

Accessors inherit Property access unless explicitly restricted. A restriction must be strictly narrower in declared access; enclosing declarations cannot justify a repeated or broader modifier. Protected forms retain their structure-member requirements.

| Property access | Permitted explicit accessor restrictions |
| --- | --- |
| public | protected internal, protected, internal, private protected, private |
| protected internal | protected, internal, private protected, private |
| protected | private protected, private |
| internal | private protected, private |
| private protected | private |
| private | None |

Neither accessor must retain the Property's full access domain, and set access need not be contained in get access. API signature and protected-receiver checks still apply. Object/projection calls require published ObjectCompatible Proven (§12.4.4); writing ref/Self or uniq/Self alone supplies no proof.

##### 11.2.2. Computed properties

Computed has no storage, initializer, bodyless standard accessor, or inline has list. Its explicit getter result must match the header Type after Origin completion. The optional setter returns Unit and may have a different input Type. Instance receivers follow explicit ordinary function contracts, including ownership-bearing receivers; static accessors omit self.

```kimi
struct Temperature
    private var celsius: f64 = 0.0
    public computed fahrenheit: f64
        get(self: ref/Self) -> f64
            return self.celsius * 1.8 + 32.0
        set(self: uniq/Self, value: f64) -> ()
            self.celsius = (value - 32.0) / 1.8

struct Holder
    private var item: Resource
    public computed view: ref/Resource
        get(self: ref/Self) -> ref/Resource
            return self.item@ref/Resource
    public computed result: Resource
        get(self: Self) -> Resource
            return self.item // Only with valid partial Move/deinit conditions.
```

Non-Copy results must be legally created or acquired. A shared receiver cannot supply an owned non-Copy field by extraction. An owning getter may consume the complete receiver; a later setter cannot reuse it. No hidden duplication, restoration, or get/set round-trip equality is promised.

##### 11.2.3. Getter results and temporaries

Stored custom get, computed get, and Contract get produce function results. Adaptations apply to that result, not backing storage.

**An owned getter-result Temporary Place and its inline descendants cannot be directly assigned, compound-updated, incremented/decremented, or exclusively borrowed.** Parentheses, projections, and implicit exclusive receiver adaptation preserve this restriction. Updating the Property itself through set is separate and remains allowed (§13.7).

```kimi
// position: Point is Copy, with custom get and standard set.
object.position.x = 10          // Error: updates only the getter temporary.
object.position.x += 1          // Error.
let edit = object.position@uniq/Point // Error.
var next = object.position
next.x = 10                     // OK: ordinary local storage.
object.position = next          // OK: calls position's set.
```

The restriction belongs to that Temporary Place, not unlimited value provenance. Value acquisition into a local or ordinary function argument uses the destination's normal rules. An ordinary function's owned result follows §3.6, even if an argument came from a getter. References still pointing into the original restricted temporary retain its restrictions. Separate referents reached through returned references/object handles follow their own capabilities, Origins, and Loans.

```kimi
// identity takes and returns Point by value.
identity(object.position).x = 10 // Ordinary function temporary rules.
// Neither example writes back to object.position.
```

Value acquisition, shared borrowing, and legal ownership transfer remain allowed. Materialization and borrowing never extend temporary lifetime. Required borrow duration is inferred from uses, returns, and retention; reject it if the referent cannot live that long.

```kimi
// visible has a custom getter returning i32.
inspect(object.visible@ref)    // OK: temporary lasts through this call.
let view = object.visible@ref
inspect(view)                 // Error: initializer temporary has ended.
let saved = object.visible
inspect(saved@ref)             // OK: borrow the local instead.
```

No blanket rejection of an unused borrow binding is added. For a standard i32 get, `@ref` instead borrows storage.

##### 11.2.4. Non-Copy custom setters

A custom setter receives ownership of non-Copy value and may replace storage. Assignment to storage secures its RHS, destroys any old value, and installs the new one without calling the setter again. Ordinary assignment/cleanup failure rules apply. An exclusive receiver cannot directly Move out an old non-Copy value; inspect it by shared borrowing or use an authorized initialization-preserving operation. End conflicting Loans before replacement and return with a complete receiver.

```kimi
struct Holder
    public var item: Resource
        set(self: uniq/Self, value: Resource) -> ()
            storage = normalize(value)

holder.item = makeResource()
inspect(holder.item@ref/Resource) // OK: standard shared access.
let item = holder.item            // Error: custom set blocks direct Move.
let edit = holder.item@uniq/Resource // Error: direct exclusive access.
```

A setter may return without updating storage. Destroy unconsumed input normally; do not double-destroy transferred input or automatically restore it to the caller. Use a result-returning function when acceptance/rejection must be reported. These restrictions do not prohibit legal whole-receiver Move or destruction. Initial placement does not invoke validation (§11.3.1).

#### 11.3. Types and Origins

A stored Type may be inferred only from its declaration initializer; otherwise require an annotation. Do not infer it from accessors or later assignments. Storage explicitly binds required Origins, including nested dependencies (§15.4); self does not create a self-borrowing storage contract.

First complete each accessor signature by ordinary position-sensitive function elision. Then compare required Type structure and bound Origins, and verify its body. Stored custom input/result must match storage T including Origin correspondence, not just names. If elision cannot establish that match, require explicit Origins.

```kimi
struct View origin source
    public var value: ref/i32 from source
        get(self: ref/Self) -> ref/i32 from source
            return storage
        set(self: uniq/Self, value: ref/i32 from source) -> ()
            storage = value
```

Omitting from source above would give the getter's unbound direct result from self and the setter's direct input an independent input Origin; neither establishes the storage contract. Existing bound dependencies are never replaced by self.

Computed/required accessors use the same function elision but no shared storage-Type comparison. A getter whose only direct borrowed input is self may elide its result Origin to self. Setter input completion is independent. Create no Origins for absent accessors. Static getter/setter contracts use ordinary receiverless rules. Copy reference/aggregate Types still undergo all lifetime and Loan checks.

##### 11.3.1. Construction and destruction

Declaration initializers and constructor first placement initialize own storage directly, without accessor calls. First placement requires that every incoming path is uninitialized and has never completed first placement. Move or destruction does not reset this history. Declaration initializers count as initialized. Do not dynamically choose initial placement versus custom set at a mixed-state join.

| Constructor write to own storage | Definitely before first placement | Already initialized or mixed paths |
| --- | --- | --- |
| let | Direct initialization | Error |
| var with standard set | Direct initialization | Normal state-dependent placement/replacement |
| var with custom set | Direct initialization | Error: custom set call during construction |

Construction self cannot call custom get/set or computed, even after all slots are initialized. Standard get may Copy or borrow initialized own storage; non-Copy Move, inherited storage access, and whole-self acquisition/borrowing remain forbidden. Construction borrows end before completion (§6.2.3).

```kimi
// In a constructor: level has custom set and no declaration initializer.
if condition
    self.level = 100 // First placement on this path; no setter.
else
    self.level = 200 // First placement on this path; no setter.
self.level = 300     // Error: construction cannot call custom set.
```

If only one branch initializes level, a subsequent unconditional write is also rejected for custom set. A declaration initializer such as level = 999 bypasses validation too; callers needing validated initial values must arrange it explicitly.

Preserve base-first construction, declaration-order initializers, no self/constructor-parameter use in declaration initializers, definite initialization, and completion checks after cleanup. No zero initialization is added. Layout, Copy derivation, implicit construction, partial Move, and destruction use stored slots/base components only. Computed contributes none; automatic destruction invokes no accessors.

##### 11.3.2. Static storage

Group/rootgroup stored Properties require initializers, an Owned complete storage Type under §15.2.3, and §22.2's per-slot lazy state machine. Safe shared borrows from eligible immutable static storage may be retained, including inside aggregates. Check acquisition, initialization, and destruction dependencies separately; Owned supplies no Loan or pointer-validity evidence.

Safe code cannot form a `from static` borrow of mutable static storage, including an inline subplace or backing data that safe mutation can invalidate. A new borrow of such storage has a finite use-bounded Origin and cannot be fitted to static, including through generic substitution or an accessor result. A function or custom getter can expose it only through a result Origin bounded by a borrowed input under §15.4, retaining the Field anchor in its effect summary (§15.6.4); a result Origin elided to static is an error. Copying an already stored reference preserves its original referent and Origin; it is not a borrow of the mutable slot. An eligible static borrow must be anchored in initialized immutable storage whose referenced path remains protected from safe mutation. Local Loans and shutdown checks remain necessary under §15.6.4 and §22.2.3.

Actual slot access triggers initialization. Calling a custom/computed accessor initializes only slots actually touched by its execution or callees, not every summarized effect. A standard write initializes the slot first, then replaces its value; static let permits no external initialization or replacement. Custom set follows its function effects and may never touch storage. Unused initializers still require validation. Static accessors have no self.

#### 11.4. Contract property requirements

A property requirement is instance-only and promises operations, not storage. The header Type T is its getter result. Get is mandatory and set optional, each at most once. Requirements have no accessor access modifiers, Attributes, default/optional parameters, initializer, storage, or body.

```kimi
contract Counted
    property count: i32 has get
contract MutableCounted
    property count: i32 has get, set
contract ReplaceableItem
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource
        set(self: uniq/Self, value: Resource) -> ()
```

`has get` requires get(ref/Self) -> T **by value**, not merely some readable access. `has set` requires set(uniq/Self, value: T) -> Unit. The explicit form states signatures; its getter matches header T and its setter input may differ. Apply ordinary receiver and Origin contracts. A shared receiver cannot Move a non-Copy stored Resource to implement `property item: Resource has get`; a ref/Resource requirement may use shared storage borrowing.

##### 11.4.1. Operation compatibility

Select a new implementation by ordinary member lookup; inherited conformance retains its mapping under §8.4.4. Check the selected let/var/computed operations without retrying another Name/base candidate because of Type, accessor, or accessibility failure. Substitute requirement-side Self, generic arguments, and associated Types while retaining the implementation's declaring Self and permitted receiver correspondence. Apply function requirement compatibility without implicit conversions or stronger implementation preconditions. Check access, Origins/Loans, and generic premises. No blanket equality between storage Type and requirement header is imposed.

Compatible custom/computed accessors implement calls directly. Calls through base projections or object borrows require published ObjectCompatible Proven (§12.4.4). A concrete Core Object View does not require runtime Contract conformance. Unknown generic capabilities cannot establish compatibility.

##### 11.4.2. Standard operation witnesses

**Witness adaptation** may synthesize a requirement operation from an exposed standard Place operation. For storage Type F, only these bridges are supplied:

| Required operation | Standard implementation |
| --- | --- |
| Shared receiver -> F | Copy, if F is proven Copy |
| Shared receiver -> ref/F | Authorized shared slot borrow |
| Exclusive receiver, input F -> Unit | Accessible var standard set |

Check every bridge against receiver, Property permissions, Origins, Loans, and premises. Storage borrows cannot outlive receiver/slot validity; copied references retain original dependencies. Hidden storage behind custom get is unavailable. Other conversions/projections/reborrows need compatible explicit accessors; do not synthesize them.

```text
Requirement + selected Property + Type/Origin substitutions + base path
    -> verified operation witness
    -> call or direct lowering preserving the requirement contract
```

Retain this mapping and the witness operation Identity/public guarantee under §12.4.4 without adding concrete accessors or lookup candidates. It is the limited implementation-generation exception of §8.4.4; no runtime witness-table representation is mandated. An unconstrained F slot may implement shared ref/F get but not unconditional by-value F get.

Contract calls have function boundaries, not caller-visible storage disjointness. Only required operations are available; implementation storage grants no direct Move/exclusive borrow. Getter owned results retain §11.2.3 restrictions even when their witnesses lower to direct operations. Preserve requirement Types, Origins, Loans, and temporary permissions through lowering.

## Part IV. Expressions and control flow

### 12. Expressions

Expressions produce values or transfer control. This chapter defines their syntax, Type rules, and evaluation.

#### 12.1. Classification and contexts

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
│  ├─ Function Expression
│  └─ Enum Case Construction
├─ Member Access
├─ Application
│  ├─ Invocation (including $abort(...))
│  └─ Generic Application
├─ Index / Slice Expression
├─ Explicit @ Operation: Type/Semantics adaptation
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
├─ Iteration / Do Expression: for / while / loop / do / Label: do
└─ Control Transfer Expression: return / exit / continue / yield

Related Syntax
├─ Match Pattern
├─ Statement: unsafe / defer / require
├─ Compile-time Directive: #if / #switch
├─ Attribute: #Name
└─ Composition Root: $
```

Value and access categories follow [Values, places, and storage](#34-values-places-and-storage).

A normally completing expression produces a typed result, including [Unit](#315-unit-and-never-types). [Value and Discard Contexts](#142-blocks-and-evaluation-contexts) determine how that result is used. Discarding it preserves side effects and type, ownership, and destruction checks. Assignment requires a writable [place](#34-values-places-and-storage) or an accessible Property setter; a readable Property need not expose borrowable storage.

An indented body is a syntax container, not a standalone expression. Use a selection or do expression to obtain a value from several operations. Unsafe, defer, and require are statements, not initializers or arguments. Let/var declarations are not expressions or condition-binding syntax; nested bodies inside conditions retain their normal declaration rules (§14.2.3).

Source delimiters and continuation follow [Lines, indentation, and continuation](#22-lines-indentation-and-continuation).

#### 12.2. Evaluation order

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

`and`, `or`, and selections evaluate only the required operands or branches. [Simple assignment](#1371-simple-assignment) evaluates and secures its right side before its target; compound assignment retains target-first evaluation. Type arguments, length arguments, and adaptation-target Type formation are not evaluated at runtime.

An abrupt Completion, divergence, or Abort prevents evaluation of later operands and the enclosing operation. Unevaluated syntax still undergoes name, Type, and transfer-target checks; syntax excluded by `#if` / `#switch` follows [conditional compilation](#19-compile-time-directives). [Temporary lifetimes](#36-temporary-values-places-and-lifetimes) and scope-exit rules govern retained values.

#### 12.3. Primary expressions

##### 12.3.1. Type inference

Expected Types from declarations, parameters, and results propagate into expressions. Otherwise infer from operands. Ordinary numeric operations require the same numeric Type; integer widths, signedness, and integer/floating-point Types do not mix implicitly.

An untyped integer literal adopts an expected integer Type if its value fits. Without one, it defaults to `i32`; a value outside that range requires an explicit Type. A floating-point literal adopts an expected `f32` or `f64`, defaulting to `f64`. Check a directly negated integer literal as a signed value, allowing the minimum of a signed Type.

Explicit `@f32` / `@f64` on a direct untyped integer literal follows the [explicit-literal adaptation rule](#135-explicit-operations), without an intermediate default integer Type. This does not add implicit integer-to-float fitting.

```kimi
let a: i64 = 10
let b = a + 20          // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64       // Convert an already typed operand explicitly.
let minimum: i8 = -128
```

There are no implicit conversions between `bool`, `char`, and numbers. Conditions require `bool`, not an integer or pointer. Borrowing and reborrowing are separate adaptations governed by ownership rules.

During [overload resolution](#10-overload-resolution-and-inference), fit unresolved literals independently to candidates before defaulting; numeric defaults do not break ties. Expected Types and nested-call/function inference are limited by [inference boundaries](#105-inference-boundaries-and-specialization). Locals never infer backward from later uses.

For control-flow results, collect all source constraints before defaulting, including available constraints from enclosing result expressions (§14.9.1). Body nesting, labels, and grouping alone do not force an earlier numeric default.

##### 12.3.2. Names, literals, and grouping

| Form | Meaning |
| --- | --- |
| `name` | Reference to a visible binding, function, or other named entity. |
| `123`, `0xff`, `1.5`, `true`, `'€'`, `"text"` | Numeric, Boolean, Character, and String literals; see [lexical structure](#2-source-and-lexical-structure). |
| `"value = \(value)"` | Interpolated string; the embedded Type must support stringification. |
| `null` | Contextually typed [raw null pointer](#51-null-and-equality). |
| `()` | Unit value. |
| `(value)` | Grouped expression; preserves a place. |
| `(value,)`, `(a, b)` | One-element or multi-element Tuple. |
| `[a, b]`, `[]` | Array literal. |
| `[key: value]`, `[:]` | Dictionary literal. |

Tuples may have different Types at each position. An array has one element Type; a dictionary has one key Type and one value Type. Empty collection literals need an expected Type. Array and Dictionary literals use the [required Cores](#221-required-core-declarations), with the expected fixed-array exception in [sequence Types](#4-arrays-indexing-and-slices); ambiguity does not fall back to a universal object Type.

```kimi
let pair = (10, "ten")
let single = (10,)
let values = [10, 20, 30,]
let names = [1: "one", 2: "two",]
let message = "first = \(values[0])"
```

##### 12.3.3. Interpolation stringification

Each embedded value’s complete Type must satisfy the required Stringify Contract. Evaluate it once, borrow it shared for the call, invoke its verified mapping once, and append the owned result before the next interpolation. Printing does not implicitly Move a non-Copy source. End the call Loan on completion; neither returned nor combined strings retain source borrows. Normal temporary cleanup and Abort rules apply. Allocation may be optimized, but independent owned-string semantics must remain.

Scalars, Unit, and string under `owner` Semantics conform to Stringify. Integers use decimal with a minus sign only for negative values; bool uses true/false, char its scalar’s UTF-8 bytes, Unit `()`, and string its contents. Float formatting is locale-independent; finite, NaN, and infinity spellings are implementation-defined and documented. Safe shared/exclusive borrows forward through shared access without taking ownership. User Types need `Self is Stringify` and a matching public implementation. No implicit object-address/raw-pointer formatting is provided; concatenation remains string-only.

##### 12.3.4. Dictionary construction and duplicate keys

Equivalent duplicate keys are errors. **Mandatory static checking** covers grouped or ungrouped Boolean, integer, char, plain string, and Unit literals; integers may have one direct unary sign. Fit each to the determined built-in key Type, decode escapes/separators, and compare values without user equality. Compare all eligible entries, even across ineligible ones, and diagnose later duplicates even in unreachable syntax.

Names (including constant-readable let), arithmetic, conversions, Tuples, floats, interpolation, and user Types are excluded; optimizer folding cannot expand this set. Otherwise process entries in source order:

1. Evaluate its key once and retain it.
2. Check for an equivalent key among entries already inserted.
3. On a duplicate, initiate implicit Abort Termination before evaluating this entry's value or any later entry.
4. Otherwise evaluate its value once.
5. Insert the retained key and resulting value.

Equivalence follows dictionary key equality; matching hash values alone do not make keys duplicates.

The Core Dictionary uses the Equatable mapping for key equality. An implementation may use linear search and requires no source Hash contract. If it uses hashing internally, equal keys must have equal hashes; hashing must not change which keys are equivalent. User-defined key equality must be an equivalence relation, in addition to the stability conditions below. The key Type must guarantee that the logical equality and hash value of a stored key remain unchanged while the dictionary holds it. Later entries never overwrite existing values.

The intrinsic Equatable mapping for f32/f64 treats all NaN values of the same Type as equal and also treats signed zeros as equal. Other values follow numeric equality. This makes floating keys usable without changing the built-in IEEE `==`/`!=` operators or adding Comparable. Equatable mappings for shared borrows and Tuples compose these Contract mappings; their built-in comparison expressions still follow §13.4. Internal hashes, when used, must agree for all NaNs and for both zeros. Floating keys undergo runtime duplicate checking even when written as literals.

User-defined equality/stability laws are semantic API obligations, not a new compiler proof or unchecked memory-safety permission. Violating them may make logical lookup and duplicate-detection results unspecified, but must not cause memory unsafety, invent values, or bypass Copy/Move and destruction responsibility. An implementation need not detect such law violations; detected API-law failures may Abort. Invalid operations actually executed inside equality still follow their ordinary rules. Normal well-formed keys retain the deterministic equality/duplicate behavior above.

A candidate key's logical equality and hash value must also remain unchanged from the start of duplicate checking through completion of insertion, including evaluation of its value expression.

```kimi
let x: i32 = getKey()
let map = [x: first(), x: second()]
```

When checked at runtime, the first entry is evaluated and inserted before checking the second key. That check fails, so `second()` is not called.

If key evaluation, duplicate checking, or value evaluation does not complete normally, do not insert that entry or process later entries. No partially constructed dictionary is returned, and completed side effects are not rolled back. The duplicate's diagnostic location is the later key expression. Termination and cleanup follow [Abort Termination](#173-abort-termination).

#### 12.4. Access and application

##### 12.4.1. Member access

`expression.name` selects a member. [Qualified lookup](#95-qualified-and-inherited-lookup) distinguishes Container and value paths, reports ambiguity when both succeed, and never implicitly inserts `self`. The right side of an ordinary member-access `.` must be a member Name or an in-range decimal integer literal selecting a Tuple element; `pair.0` selects its first element. The reserved `.init(...)` suffix instead forms a [construction expression](#623-constructors) with a Type qualifier. Dynamic member lookup with an arbitrary expression is not defined.

Property selection follows §11: standard get exposes permitted Place operations, while custom/computed/required get produces a result. Assignment uses accessible set. Check each operation and receiver; getter results never expose hidden storage. Tuple/fixed-array Places retain Move Path rules. Raw pointers do not dereference automatically: use `(*pointer).name` in an Unsafe Block.

```kimi
let count = collection.count
collection.count = 10   // Requires a setter and write permission.
let first = pair.0
```

##### 12.4.2. Invocation and generic application

`callee(arg1, arg2)` invokes a function, method, or function value. Zero arguments and a trailing comma are allowed. `callee<T, U>(args)` applies explicit type arguments before calling.

Argument mapping, Type adaptation, expected-result filtering, candidate comparison, and final usage checks follow [overload resolution](#10-overload-resolution-and-inference). Named arguments use `name: expression`; positional arguments must precede them. Function values retain positional-only calling, and unsafe calls retain their [additional restrictions](#75-unsafe-functions). Explicit function type arguments must provide the entire required list.

In an expression, `<` introducing type arguments must be adjacent to the target name and have a matching `>`. Thus `f<T>(x)` applies type arguments while `a < b` compares values. Nested type arguments may split `>>` into two closing delimiters. Use spaces around comparison operators to avoid ambiguity.

Generic arguments follow [slot binding](#81-generic-type-parameters), [function length slots](#4-arrays-indexing-and-slices), and [inference boundaries](#105-inference-boundaries-and-specialization); other general Const arguments are not introduced. After ordinary call resolution, select any matching [explicit specialization](#88-explicit-full-function-specialization). Structure construction uses the dedicated [Type.init invocation](#623-constructors), with ordinary call argument evaluation and its own Type-only qualifier lookup. `T(args)` is not a constructor shorthand.

##### 12.4.3. Object member calls

Select the ordinary member, overload, access, and public contract statically from the receiver's [Effective Type](#14101-stable-bindings-and-effective-types). Use that implementation even when the Runtime Object Type is more derived. Adjust the receiver to the selected declaration's subobject under §9.5.1; no new lookup or derived-only overload is introduced.

Shared object access admits shared members/getters. Exclusive access may share by Reborrow or invoke an exclusive member/setter under normal Loan rules. Returned interior references retain their receiver Loan; an exclusive result cannot enable overlapping reentry. Calls through an object borrow require the public guarantee below.

##### 12.4.4. Object receiver compatibility

Object borrows may mutate permitted state of the same complete object, but cannot Replace the entire object or base subobject, even with the same Type, or MoveOut a part leaving it incomplete. Field Replacement and initialization-preserving Exchange remain state mutation under normal permissions and Loan checks. Replacing an owning handle instead destroys the old object and owns a different one:

```kimi
func celebrate(animal: objuniq/Animal) -> ()
    animal.age = animal.age + 1

var animal: obj/Animal = makeDog()@obj/Animal
animal = makeCat()@obj/Animal // Destroy the old Dog, then own a separate Cat.
```

Owning-handle replacement retains normal borrowing/destruction conditions. Exclusive object access requires a writable Place or eligible temporary; rc/arc supply only shared access, even at count one. No general objuniq/T-to-uniq/T conversion or owning-receiver slicing is added.

###### 12.4.4.1. Public status and use

**ObjectCompatible** is the public guarantee that a borrowed-receiver call preserves receiver completeness, access authority, and its Type/Origin/Loan contract. Its key is the **call operation Identity**, not a Type substitution or a separate receiver-kind key; receiver kind belongs to the Signature. Functions and custom/computed get/set each use their declaration Identity.

Direct standard get/set remain Place operations, checked by acquisition and Property permissions; they are not implicit getter functions. In particular, borrowing `Box<T>.item` with `@ref` does not require T is Copy. Standard Contract witnesses are call operations: their generated Identity distinguishes the Requirement Identity, Property Identity, and bridge kind within the conformance mapping. Preserve each witness's result Type and public premises; do not merge different witnesses or publish a status for each concrete instantiation.

| Published status | Call through a base-subobject projection or object borrow |
| --- | --- |
| Proven | Allowed if the ordinary call, access, Type, Origin, and Loan checks pass |
| NotProven | Use-site error; calls on complete ordinary values remain subject to their normal rules |

NotProven means absence of a common proof, not Refuted for every binding. Additional caller premises, favorable Type arguments, the exact Dynamic Type, or one selected specialization cannot strengthen this status. A failed use never causes overload reselection. Unknown is internal pending work, never a published status. Invalid bodies, missing mandatory artifact information, and unimplemented verification cannot be hidden as NotProven.

###### 12.4.4.2. Effect verification

Use resolved operations and acquisition plans before optimization. Under the declaration's Signature, Constraints, and conditional premises, verify every admitted Type/Origin binding using §8.7–§8.10. Do not infer hidden caller Constraints from a body. The following abstract rules determine the public result independently of analysis precision, optimization, and processing order; representations and worklist algorithms are implementation choices.

Summaries retain operation kinds and their relation to receiver/input roots, captures, static anchors (§15.6.4), and returned aliases. A storage relation is a finite set of Whole (the root), Base (base edges only), Part (a path including Field/Tuple/array storage), Separate (proven disjoint), or MayAlias (not established). Preserve normal Origin/Loan and target-Place information alongside these relations.

| Operation or dependency | Required summary/check |
| --- | --- |
| Replace, reconstruct, or acquire ownership of receiver Whole/Base | Receiver-preservation violation |
| MoveOut from receiver storage leaving it incomplete | Violation; later restoration does not cancel it |
| Completeness-preserving Part Replacement/Exchange, permitted reads/borrows | No preservation violation by itself; retain access, Loan, and cleanup checks |
| Escape of unrestricted exclusive access capable of operating on receiver/base | Violation; include results, stores, and callee paths |
| Borrowed result | Retain root/alias correspondence and verify return Origins, Loans, and authority |
| Separate operation | No violation against the root from which separation is proven |
| MayAlias or unverified unsafe/indirect effects that may violate receiver preservation | Unproven effect on every potentially affected root |
| Calls, custom accessors, defaults, cleanup | Map and compose published root effects through actual arguments, captures, and static anchors |
| Branches and loops | Union all Type-checked paths; omit only environment-excluded syntax, not paths removed by runtime reasoning or optimization |

Base-only path composition yields Base; a storage-element edge yields Part. A Field storing a reference is not that reference's referent: follow retained anchors/aliases, using MayAlias when the referent relation is unknown. Separation from one input proves nothing about another input, capture, or static root. Already proven harmless effects, such as permitted reads, do not become violations merely because their target may alias.

A callee's whole-value replacement mapped onto a caller's Part is not replacement of the whole caller receiver. Keep the actual target's restrictions, however: Part classification cannot erase a base-subobject use check. Summarize defined completeness-preserving Replacement/Exchange as such, rather than counting their internal lowering transfers as separate illegal MoveOut operations. Retain operation summaries instead of simply propagating a callee's NotProven bit.

For same-build calls, propagate direct effects over the finite declaration-schema/root domain to the least union fixed point, including recursion and the implementation families below. Do not enumerate concrete Types or remove specializations based on a particular call. Recursion alone is not an unproven effect; an unfinished empty summary is not a proof. A verified empty summary is valid.

Separate, indirect, and generic-requirement calls use validated public summaries or requirement Effect contracts. Without an optional effect guarantee, propagate unproven effects to potentially affected roots; do not inspect private bodies or enumerate possible implementations. Missing mandatory artifact data is an artifact error, not an optional missing guarantee. Receiverless helpers may publish input-root summaries without an ObjectCompatible status.

An implementation succeeds only when normal semantic checking completes and every admitted binding has no receiver violation or unproven effect. Pending call/conformance obligations remain explicit until resolved. The effect fixed point does not prove a circular conformance declaration; normal proof deadlines and errors still apply.

###### 12.4.4.3. Implementation families

An operation's public status is Proven exactly when the ordinary body and **every implementation in its closed explicit-specialization set** pass §12.4.4.2. Otherwise a semantically valid family is NotProven. Close the set after environment selection, generation, and declaration collection, regardless of whether implementations are currently called.

Verify the ordinary generic body over all admitted bindings independently; do not subtract bindings covered by specializations. Verify each specialization under its substituted inherited contract. The ordinary body's inferred Proven imposes no extra declaration obligation on a specialization. An incompatible specialization makes the family NotProven, but is not a declaration error for that reason; declared Signature, Constraints, Safety, and Effect obligations still apply. Improving the ordinary body therefore cannot invalidate an unchanged specialization.

Check use against the original public contract before selecting an implementation (§8.8.3). A NotProven diagnostic identifies the responsible implementation and reason, including specialization arguments and dependent callee/witness identities where relevant; private bodies need not be exposed. Publish the completed summaries, status, and dependencies under §18.3 and revalidate changes under §21.3.4.

### 13. Operators and assignment

#### 13.1. Precedence and associativity

Earlier rows bind more tightly. Left associativity groups `a op b op c` as `(a op b) op c`; right associativity groups it as `a op (b op c)`. Grouping does not guarantee type correctness or change evaluation order.

| Level | Operators or syntax | Association |
| --- | --- | --- |
| 1 | `.name`, `(...)`, `<Types>`, `[...]`, postfix `++` `--` | Postfix chain, left to right |
| 2 | Prefix `+` `-` `not` `*` `^` `++` `--` | Right |
| 3 | `@Type`, `@Semantics` | Left |
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

In ordinary expressions, `is` / `is not` ends after one named struct Core; outer `and` / `or` remain Boolean operators. In dedicated compile-time contexts, `is` instead follows the asymmetric [requirement-expression rule](#83-requirement-expressions). Prefix `not` precedence is unchanged: negate a runtime test with `value is not Dog` or `not (value is Dog)`. `as` is reserved.

Unparenthesized comparison chains such as `a < b < c`, `a == b == c`, and `a < b == flag` are syntax errors. Write `a < b and b < c` or `(a < b) == flag`; each comparison still requires valid operand Types.

`@` is one token. All adaptations share this precedence and left associativity, below prefix operators: `-x@i64` means `(-x)@i64`. Targets may contain qualified names, `/`, and generic arguments. Parenthesize an adapted result before selection, calls, or indexing: `(x@T).name`, `(f@T)()`, `(a@T)[0]`. Use `(x@Number) / divisor` for division after adaptation.

Conversion type arguments follow the same adjacent-`<` and matching-`>` rule as generic application: `value@Box<i32>` contains a type argument, whereas `value@i64 < limit` compares the converted value. Selections, iterations, and do expressions have their own body syntax. `return`, `exit`, and `yield` consume a full result expression, so `return a + b` returns the sum.

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

#### 13.2. Unary operators

| Operator | Operand and result |
| --- | --- |
| `+value` | Numeric value, unchanged Type and value. |
| `-value` | Negated signed integer or floating-point value. |
| `not value` | Negated `bool`. |
| `*pointer` | Raw-pointer place under [unsafe dereference rules](#52-dereference-and-ownership). |
| `^value` | From-end Index formed from a nonnegative `isize`. |
| `++target` / `--target` | Increment or decrement an integer; return the updated value. |
| `target++` / `target--` | Increment or decrement an integer; return the old value. |

Increment and decrement require a readable, writable integer place or Property; they do not apply to floats, raw pointers, or arbitrary Types. Resolve, read, and write the target once each. Overflow prevents the write. A prefix operation returns its computed value without reading the Property again. These operations follow the target-validity and ownership requirements of [compound assignment](#1372-compound-assignment).

```kimi
var count: i32 = 1
let before = count++  // before = 1, count = 2
let after = ++count   // after = 3, count = 3
```

`not` binds more tightly than comparison; negate a comparison as `not (a == b)`. Explicit dereference of non-pointer Types is not defined by this operator.

#### 13.3. Arithmetic, bitwise, and shift operators

`+ - * /` take operands of the same numeric Type and return that Type. `%` accepts integers only. Integer division truncates toward zero. On mathematical integers, the remainder satisfies `a = (a / b) * b + a % b`; a nonzero remainder has the dividend's sign.

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010
let shifted = bits << 1     // 0b10100
```

Check integer `+ - *`, unary `-`, increment/decrement, and the arithmetic part of compound assignment for overflow. Integer division or remainder by zero is invalid. Signed minimum divided by `-1`, including `% -1`, is also invalid. These failures follow [Abort Termination](#173-abort-termination), including its constant-evaluation rule.

`& | ^` perform bitwise AND, OR, and XOR on the same integer Type; they do not accept `bool`. `<< >>` return the left operand's integer Type and accept any integer Type on the right, requiring `0 <= shift < bit width of left operand`. An invalid count is a check failure. Left shift discards high bits and inserts zero low bits; right shift sign-extends signed integers and zero-extends unsigned integers. Discarded shift bits are not arithmetic overflow.

Floating-point operations follow IEEE 754 for `f32` / `f64`, using round-to-nearest, ties-to-even. They support infinity, NaN, and signed zero; floating-point division by zero does not use integer failure rules. Do not implicitly reassociate or fuse ordinary operations when rounding or NaN results would change.

`string + string` denotes concatenation without implicit numeric stringification. Its operand acquisition and ownership rules, including string `+=`, are deferred to a common operator model. That design must specify Copy/Move or borrowing, Loan duration, result ownership, aliasing/self-update, and failure behavior while preserving the evaluation and write order in §13.7. No particular acquisition strategy is adopted here. These operations cannot pass executable finalization until those rules are defined and implemented; parsing or Type checking alone grants no ownership permission.

Raw-pointer arithmetic is limited to the forms and unsafe conditions in [pointer arithmetic](#53-pointer-arithmetic-and-indexing); its undefined-behavior rules are distinct from checked integer arithmetic.

#### 13.4. Comparison and logical operators

`== != < <= > >=` return `bool`. Numeric operands must have the same Type. `bool` and Unit support equality only. `char` compares Unicode scalar values. `string` uses UTF-8 byte equality and lexicographic order without normalization or locale processing.

Floating-point `+0.0 == -0.0` is true. With a NaN operand, `== < <= > >=` are false and `!=` is true; floating-point ordering is not total.

Comparisons may borrow their operands and do not Move non-Copy owned values solely to compare them. User-defined comparison requires an explicit Type capability. Safe borrows compare referent values of the same Type using that Type's comparison capability. Tuples support elementwise equality and lexicographic ordering when all corresponding elements support the required comparison.

For a built-in comparison that inspects a non-Copy owned Place, form an implicit shared Loan when that operand is evaluated. Operands are evaluated left to right: the left operand's Loan begins before evaluation of the right operand and remains active through the comparison. Both inspections require Initialized values. Apply the normal [Loan conflict rules](#1562-place-overlap-and-conflicts) throughout operand evaluation; a later operand cannot Move, replace, destroy, or exclusively borrow the earlier borrowed Place. Optimization cannot change this acceptance rule.

On normal completion, comparison-only Loans end after the comparison; its bool result retains no operand Loan. They also end if a control transfer abandons the comparison. Existing Loans retain their own lifetimes, and temporary operands retain their [normal temporary lifetimes](#362-lifetime-and-borrowing); ending an inspection Loan does not destroy a temporary early. Abort follows §17.3.

```kimi
func take(text: string) -> string => text

let text = "a"
let same = text == "a" // Shared inspection; text is not Moved.
// let invalid = text == take(text)
// Error: the left operand's shared Loan is active when take acquires text by Move.
writeLine(text) // Allowed: the completed comparison's Loan has ended.
```

Value equality and object identity are separate operations; `==` does not implicitly become an address comparison for object Types. Raw-pointer `== !=` are the explicit exception, following [pointer equality](#51-null-and-equality).

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

##### 13.4.1. Contract comparison mapping

After the built-in cases in this section, comparison of two operands of the same complete user Type requires the recognized Core Contract below. Resolve its conformance mapping once; do not search same-named free functions, imported extensions, or conversion chains. Generic code requires the corresponding Constraint. Built-in comparisons retain priority and cannot be replaced by a conformance declaration.

| Operators | Required operation | Result |
| --- | --- | --- |
| ==, != | Equatable.equals on shared borrows of both operands | returned bool, or its negation |
| <, <=, >, >= | Comparable.compare on shared borrows of both operands | compare returned i32 with zero |

Comparable refines Equatable: compare’s sign must agree with equality and a total order. Integers, char, and string under `owner` Semantics provide both; bool, Unit, and floats provide Equatable. Floats have built-in relational operators but no Comparable because NaN is unordered. Borrow/Tuple comparisons forward or compose these capabilities. Structs and enums—including payload-free enums—need explicit conformance and members; equality/ordering is not derived. Arithmetic Contracts, user operators, and user-defined arithmetic remain deferred, so arbitrary user-Type arithmetic is an error.

For f32/f64, the intrinsic Equatable.equals mapping uses the NaN-reflexive equality defined for Dictionary in §12.3.4, whereas a built-in `==` expression still returns false for NaN. Generic comparison through an Equatable requirement uses its mapping. Do not specialize such a generic call into a floating `==` instruction that changes its meaning. Borrow/Tuple Equatable conformances compose mappings in the same way, separately from built-in operator semantics.

#### 13.5. Explicit operations

Explicit operation selection is distinct from subtyping and acquisition legality under [Type relations and expression operations](#38-type-relations-and-expression-operations). Origin restriction fits the selected operation's result; it does not substitute another operation.

`@` is a built-in explicit value operation. It cannot be overloaded, does not search conversion chains, and applies only to its direct operand. The operation itself calls no user code; ordinary operand evaluation, including calls and getters, still does. It never implicitly boxes, acquires resources, duplicates ownership, or increments reference counts.

```text
Explicit @ Operation
└─ Type / Semantics Adaptation: @Type, @ref, @uniq, ...
```

##### 13.5.1. Forms and adaptation targets

| Form | Meaning |
| --- | --- |
| `E@Type` | A defined adaptation to the specified target |
| `E@Semantics` | Same form with Core, immediate Referent Type, or object View Target taken from the operand as applicable |

An **Adaptation Target** specifies Semantics and a Core, a complete inner Type for a value-borrow or pointer layer, or an object View Target. Infer result Origins from the operand, operation, Loans, and applicable constraints to obtain the complete result Type. Retain Origin information in aliases, generic Types, and operands; do not erase constraints or extend validity. Runtime targets do not contain `from Origin`; `exit to Label: value` belongs to control-transfer syntax.

```text
Adaptation Target
├─ Core / immediate Referent Type / object View Target: specified, or taken from the operand
├─ Semantics: determined by Type, alias, or explicit Semantics
└─ Origin: inferred during adaptation
    -> complete result Type retains target, Semantics, and Origin
```

**Syntactic extent.** After @, consume an identifier-shaped head followed by slash as a Semantics prefix, recursively and regardless of whitespace; no lookup is needed to make that decision. A prefix must later resolve to a concrete Semantics or declared Semantics binding. Consume a remaining primitive, named/qualified/generic, grouped, Tuple, or fixed-array Type head as the target. Generic adjacency uses §12.4.2; written Origins are forbidden at every target layer. A following slash is division only after that head is complete and cannot begin another syntactic Semantics prefix.

`a@ref/uniq/T` consumes the full prefix chain. `a@T / b` parses target `T/b` and fails Semantics lookup if T is only a Core; whitespace cannot change it. Write `(a@T) / b` or `a@(T) / b` for division. Primitive keywords cannot be Semantics parameters, so `x@i32 / y` already means `(x@i32) / y`. Grouping `x@(i32)` preserves the adaptation. Group complete Function Type targets, as in `x@((i32) -> i32)`; adaptation does not consume a following outer arrow. Parsing commits before Binding and never retries after conversion failure.

For a single-layer value Type with Core `T`, `@ref` and `@ref/T` select the same existing Borrow/Reborrow operation when applicable. For a nested borrow, shorthand retains its immediate Referent Type: applying `@ref` to `ref/ref/T` copies that outer shared reference. It does not add a layer or turn `@ref` into `@objref`. A fully specified target can instead request a borrow of reference-value storage under [Borrow and Reborrow](#1355-explicit-borrow-and-reborrow). Type names, aliases, generic applications, grouping, and Tuple syntax are accepted as target syntax without implying that every adaptation is defined.

```kimi
let wide = number@i64
let view = value@ref
let sameView = value@ref/Value // When value's Core is Value.
let taken = value // Copy if Copy, otherwise Move.
```

For a bare Name `X` in `E@X`, first recognize built-in Semantics names. Otherwise perform **Adaptation Target Lookup** independently for the Type and generic Semantics-parameter roles, using ordinary lookup stages, visibility, and aliases. Type candidates include Type aliases and generic Type bindings subject to the target's role restrictions. Commit each role's first eligible stage and deduplicate paths to the same Symbol.

| Lookup result | Outcome |
| --- | --- |
| Type only | `@Type` |
| Semantics parameter only | `@Semantics` shorthand |
| Both roles, or ambiguity within a role | Ambiguity error |
| Neither | Ordinary wrong-role, inaccessible, or undefined-Name diagnostic |

Operand Types, expected Types, or conversion success cannot resolve a role ambiguity or reopen outer lookup. Qualified names and constructed Types use normal Type syntax; `@s/T` gives `s` the Semantics role and `T` the Core role, extended to the View Target role when `s` is object Semantics. Qualification or an explicit Semantics/Core form may disambiguate a bare name.

##### 13.5.2. Static selection and inference

Resolve the operation from the explicit designation and operand Type/category, then check access, ownership, Loans, and Origins. Numeric conversion, Identity Acquisition, and pointer casts use ordinary value acquisition. Borrow targets use the Borrow table. Failure never selects a different operation, getter, or overload.

Targets may guide permitted literal/generic inference but cannot change an established operand or result Type. An outer expected Type cannot cancel the selected operation. Preserve normal inference boundaries; do not introduce cyclic inference or candidate-by-candidate retries. Check subsequent result fitting statically.

```kimi
// handler is an overloaded function name; the annotation selects its reference.
let f: (i32) -> () = handler
// Conversion to the common Function Type produces a Non-Copy value.
let g = f // Move the common function value; f becomes Moved.
```

Deferred generic effects follow [Generic Access Effects](#89-generic-access-effects). Resolve effects before finalizing ownership/Loan analysis. `Never` follows ordinary abrupt-completion and Type-fitting rules, not a value conversion. A non-completing operand prevents execution of the outer operation but does not waive syntax, target-Type, or Unsafe checks.

##### 13.5.3. Defined adaptations

| Operation | Condition |
| --- | --- |
| Identity Acquisition | Same normalized complete Type; ordinary acquisition is permitted |
| Numeric Conversion | Integer/float values under `owner` Semantics in the numeric table below |
| Borrow / Reborrow | The explicit Borrow table below |
| Object Upcast | The finite [object upcast tables](#1357-object-upcasts), including their specified borrow forms |
| Raw Pointer Conversion | The [pointer conversion rules](#54-pointer-conversions) |
| Typed Null Formation | Contextually type `null` as a raw pointer; no Unsafe context required |

**Identity Acquisition** copies a Copy Type and otherwise Moves. Borrow targets take precedence, including same-Type exclusive Reborrow. Same-Type raw pointer acquisition is ordinary Copy and requires no Unsafe context for the operation itself. `@owner`, `@obj`, `@rc`, and `@arc` allow same-Semantics acquisition; they do not perform [ownership creation or strong-owner duplication](#1358-object-ownership-creation-and-sharing), or convert between ownership representations.

```kimi
number@owner   // Copy if number is Copy.
resource@owner // Ordinary Move if resource is a non-Copy owned value.

```

**Origin Restriction** is common static result fitting, not another value operation. Determine acquisition/Borrow and its effect, then apply only shortening permitted by existing variance and outlives rules. Check Identity Acquisition before this use-site restriction. Preserve Core, Semantics, dependencies, and Loans; do not add Copy, Move, or Borrow, extend lifetime, or rewrite arbitrary nested Origins. For example, fitting `ref/T from longer` to `ref/T from shorter` requires `longer` to outlive `shorter`. Exclusive same-Type adaptation still uses Reborrow.

A target changing both Core and Semantics must be one defined operation. No hidden convert-then-borrow sequence is inserted:

```kimi
// number is i32.
// number@ref/i64 // Error: numeric conversion and Borrow are separate operations.
inspect(number@i64@ref)
```

There is no elementwise Tuple/array conversion, structural struct conversion, checked dynamic cast through `@`, string parsing, numeric conversion involving `bool`/`char`, arbitrary bit reinterpretation, or user-defined conversion. Same-Type acquisition of these Types remains possible. Do not implicitly dereference safe references to convert or extract their owned referents. Raw-pointer/safe-reference conversion and ownership acquisition from raw storage remain separately specified. `as` remains reserved, not an alias of `@`.

##### 13.5.4. Numeric conversions and literals

| Source -> target | Rule |
| --- | --- |
| Integer -> integer | Check the target range; no truncation or wrapping |
| Integer -> float | Round to nearest, ties to even |
| Float -> float | Same rounding; preserve NaN and infinity |
| Float -> integer | Truncate toward zero, then check the mathematical integer's range |

A finite value rounding to infinity fails. Rounding to a subnormal or zero is allowed. NaN payload preservation is not guaranteed. NaN and infinity cannot convert to integers.

Float-to-float conversion preserves signed zero, including the sign of a nonzero value rounded to zero. Integer zero converts to positive floating zero; either floating zero converts to integer zero. Floating rounding uses roundTiesToEven, with gradual underflow. The initial Windows profile requires the ABI-standard FP environment at entry and across foreign calls (§21.5.4); foreign code that violates this contract is outside the supported boundary, and the runtime does not repair its environment. These rules also apply to literal conversion.

For direct unresolved literals, `@` performs literal fitting in these cases: integer literals with integer targets must fit the target range; floating literals with `f32`/`f64` targets follow [single-rounding rules](#26-number-literals); integer literals with `@f32`/`@f64` round once from the exact integer value, without an intermediate default Type. Failure to fit, including floating overflow to infinity, is a compile-time error, not a runtime numeric-conversion failure. Parentheses alone and a direct sign preserve this treatment; the language's literal representation limits still apply.

Floating literals with integer targets first get their ordinary floating source Type, then undergo truncation and range checking. Typed values and general arithmetic expressions use ordinary numeric conversion. Explicit literal adaptation does not widen implicit argument fitting or overload candidate comparison.

```kimi
let minimum = -128@i8
let large = 5000000000@f64 // No intermediate i32 range check.
let single = 1@f32
let negativeZero = -0.0@f32
let truncated = 3.9@i32  // 3
// 256@u8 // Error: direct literal does not fit.
// 300@i8 // Compile-time error; a typed i32 value 300 converted to i8 instead Aborts.
```

Apply rounding and checks at every `@` in a chain. Do not remove an intermediate result if its rounding or failure would change.

##### 13.5.5. Explicit borrow and reborrow

In value Borrow/Reborrow rows, `T` is the same normalized immediate Referent Type, which may itself have Semantics. In object rows it is the same View Target; changing that target uses the upcast tables below. Every row requires valid initialization, access, Loans, and Origins. These are explicit adaptations; do not add rows to implicit [argument fitting](#102-argument-adaptation-and-literals) solely because they appear here.

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

A fully specified ref/V or uniq/V target may borrow a readable or exclusively writable Place of exactly normalized Type V, even when V is a reference or object handle. Materialize Temporary Values under normal lifetime rules. This adds one reference layer and preserves all V dependencies. Same-Type Copy/Reborrow and uniq/V → ref/V Reborrow take precedence; failed Reborrow cannot fall back to storage borrowing. Shorthand `@ref`/`@uniq` adds no layer to an already borrowed value.

```kimi
var number = 1
var reference = number@ref
let same = reference@ref            // ref/i32; preserves number's Origin.
let slot = reference@ref/ref/i32     // ref/ref/i32; also depends on reference's storage.
// reference = other@ref            // Error while slot's Loan is live.
```

For `reference: ref/i32`, `reference@uniq/ref/i32` borrows its writable slot exclusively; it does not grant mutable access to `number`. A `let` reference slot cannot be borrowed this way exclusively. `reference@ref@ref` remains `ref/i32`. Explicit Origins remain forbidden anywhere in an Adaptation Target, including grouped and generic inner Types; their dependencies are inferred or retained from existing Types.

A new Borrow depends on the target Place and owner validity. Copying a shared reference preserves its referent Origins rather than using the lifetime of the variable holding it. Reborrow lends referent capability without moving the parent reference; while the child Loan is live, conflicting access through the parent is forbidden. A `let` binding holding an exclusive reference does not by itself prevent Reborrow. Ordinary by-value acquisition transfers a non-Copy reference itself.

```kimi
var value = makeValue()
let exclusive = value@uniq
inspect(exclusive@ref)
modify(exclusive@uniq) // After the previous child Loan ends.
let transferred = exclusive // Move the reference, not its referent.
```

Do not upgrade shared to exclusive, derive exclusive object borrows from `rc`/`arc`, or convert between value-borrow and object-borrow representations. A runtime reference count of one does not grant an exception. Temporaries under `owner` Semantics use [materialization and temporary borrowing](#36-temporary-values-places-and-lifetimes).

Custom/computed/required get invokes a getter once. Adapt its declared result Type, including shorthand inference. Borrowing an owned result materializes its temporary and obeys §11.2.3; references retain ordinary Copy/Reborrow rules. Set access grants no hidden-storage borrow. Standard get uses §11.1 Place permissions.

```kimi
// Assume item is computed and its getter returns ref/Resource.
let view = holder.item@ref // Copy that reference.
// holder.item@uniq       // Error if get returns ref/Resource.
inspect(person.age@ref)    // Borrow the getter's Copy result temporary.
```

##### 13.5.6. Evaluation, results, and failure

Evaluate each operand and required receiver once; chained adaptations finish from the inside outward. Each operation uses its own acquisition and permission rules. Acquisition never propagates backward through a call/getter into hidden storage.

Assignment remains RHS-first and returns Unit. A custom setter receives the secured result normally; source Move and destination Write permissions are independent. Destruction of the old destination must preserve result Loans/Origins. Loans begin at Borrow/Reborrow, including during later argument evaluation; do not delay an exclusive receiver Loan until after arguments.

```kimi
var x = makeResource() // Non-Copy.
x = x // Move and reinitialize; skip destruction of the Moved old value.
// f(x, x) is invalid if its first argument consumes x and its second reads x.
```

Discard still evaluates and checks acquisition/conversion, then destroys the unused owned result at its normal lifetime. Transfers secure results before cleanup. Returned borrows must survive cleanup; deferred uses of Moved/incomplete values are errors.

| Failure | Handling |
| --- | --- |
| Undefined adaptation; Type, access, initialization, Loan, or Origin violation | Compile-time error |
| Literal fitting or required constant conversion failure | Compile-time error |
| Failed runtime numeric conversion | Abort if evaluated |
| Unsafe memory contract violation | Ordinary Unsafe rules |

Literal fitting is static; other constant evaluation follows [§17.3.4](#1734-checks-builds-and-constant-evaluation).

```kimi
let x: i32 = 300
x@u8 // Abort when evaluated, even when propagation knows x is 300.
```

Abort and cleanup follow ordinary failure/lifetime rules; completed Moves and effects are not rolled back, and no lifetime extension occurs.

##### 13.5.7. Object upcasts

Let `S` be the source's static Core View Target and `V` a different target. An upcast requires static proof of `Supports(S, V)` that remains valid for every more-derived Dynamic Type. Concrete Core support follows the base graph. Static conformance alone does not establish this persistence: [inherited conformance](#844-conformance) is conditional on matching, and the [runtime Contract extension](#85-runtime-contracts) must guarantee persistent Supports for its Views. Validate [payload erasure](#1581-object-payload-erasure), initialization, access, Origins, and Loans in every row.

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

##### 13.5.8. Object ownership creation and sharing

Core provides the following explicit intrinsic operations. The names below describe semantic operations, not final API spellings or new `@` forms. `T` is the exact concrete Core of the input owner and must already be admitted as an object payload; these operations do not extend object support to enums or runtime-contract payloads. `V` is an existing valid View Target.

| Operation | Input | Result and responsibility |
| --- | --- | --- |
| Create exclusive object | `owner/T` by value | Fresh `obj/T` owning the complete payload |
| Create non-atomic counted object | `owner/T` by value | Fresh `rc/T` with one strong owner |
| Create atomic counted object | `owner/T` by value | Fresh `arc/T` with one strong owner |
| Duplicate non-atomic strong owner | Shared access to an initialized `rc/V` handle | Additional `rc/V` owner of the same object; increment its strong count once |
| Duplicate atomic strong owner | Shared access to an initialized `arc/V` handle | Additional `arc/V` owner of the same object; atomically increment its strong count once |

**Creation.** Evaluate and acquire the input once by ordinary by-value rules: Copy leaves its source Initialized; Move transfers responsibility and marks the source Moved. Require a complete payload and valid Loans. Concrete payload storage needs no Owned requirement: preserve its complete Type and value dependencies under [ordinary storage](#154-origin-elision-and-return-contracts), independently of allocation elimination. Subsequent base/contract erasure has its separate Owned requirement.

After acquisition, secure fresh payload storage and any ownership metadata, then Move the prepared value into it without invoking constructors, accessors, duplication code, or `deinit`. Publish the handle only when payload and metadata are complete. Its initial View Target and Dynamic Type are exactly `T`, and its lifetime has a fresh object identity. Any base/contract upcast is a separate operation with its existing erasure checks.

The new owner takes the payload's destruction responsibility and records the original storage-release mechanism. Moving the payload does not transfer ownership of the source's enclosing storage: that storage remains under its original local, temporary, or containing owner's management, with no second destruction of the moved payload. On normal object release, destroy the complete payload before releasing its original object storage under §16.3.3. Physical allocation may be eliminated only while preserving these semantics.

**Strong-owner duplication.** Evaluate the source once and hold shared access to its owning handle for the operation. Leave that handle Initialized with its responsibility unchanged. Do not copy payload data, allocate a new object, change views, or invoke user code. Internal count-storage promotion at the inline representation limit may allocate a side table under §21.2.3; ordinary duplication otherwise needs no allocation. The result preserves the source's complete View Type, object identity, Dynamic Type, and payload dependencies, but retains no new Loan on the source handle's storage. Releasing either handle relinquishes one strong reference; only the final release destroys the payload. Duplication grants no exclusive access and cannot create ownership from `objref`/`objuniq`, duplicate `obj`, or convert `rc` and `arc` into each other.

**Failure and boundaries.** Required storage/metadata allocation failure and an unrepresentable strong-count increment Abort before publishing a result; counts must never wrap. Abort performs no unwinding, input restoration, or promised cleanup. Ordinary non-Abort transfers during operand evaluation retain normal pending-value and temporary cleanup. Recoverable creation, allocator selection, raw-storage adoption, ownership-representation conversion, and cycle collection remain separate designs. Weak references follow §13.5.9. No creation or duplication may publish an incomplete object or resurrect one undergoing destruction.

##### 13.5.9. Weak reference operations

Core supplies these intrinsic semantic operations for eligible `Weak<S>` (§3.2.2). Final API spellings remain deferred; they are not new `@` forms.

| Operation | Input | Result and responsibility |
| --- | --- | --- |
| Empty Weak | Eligible complete S | Empty `Weak<S>` without allocation |
| Downgrade | Shared access to an initialized strong handle S | `Weak<S>` for the same object/view/mode; retain weak-management storage without changing strong count |
| Upgrade | Shared access to `Weak<S>` | `Option<S>`; Some secures one new strong owner, None means empty or expired; source Weak remains Initialized |
| Duplicate Weak | Shared access to `Weak<S>` | New `Weak<S>` retaining the same management storage; empty input needs no count operation |

Evaluate each operand once and acquire its required access before performing the operation. Downgrade accepts only a completed object reached through its `rc`/`arc` strong handle; it may lazily allocate management storage. Borrowed access to that handle lasts through the operation. It does not convert `obj`, object borrows, or raw pointers into Weak.

Upgrade secures a strong count increment before accessing the object pointer, payload, or metadata. It preserves every static dependency and grants only the access available through S. Its successful count update and the last strong release are ordered atomically for arc: if upgrade wins, the new owner keeps the object alive; if final release wins, upgrade returns None. Once the last owner sets strong count to zero, subsequent upgrades permanently fail. An increment at the count maximum Aborts without wrapping or publishing a result; it is not ordinary None. Required allocation failure and weak-count overflow also Abort.

Moving Weak changes no count. Destroying a nonempty Weak releases one weak responsibility and never destroys the payload. The last strong owner destroys the complete object and frees its original allocation even if Weak values remain; only the side table survives until all external Weak values and its internal destruction guard have been released (§21.2.3). Empty Weak destruction is trivial. The guard prevents cleanup of Weak fields inside the payload from freeing the table during object destruction. Abort/nontermination retains ordinary no-cleanup/no-rollback guarantees.

No separate liveness-test API, implicit weak duplication, unsafe weak-pointer API, unowned reference, or cycle collection is introduced. Source concurrency remains subject to Appendix D.2. Cyclic construction with an initially non-upgradeable Weak requires a separately specified factory; ordinary downgrade cannot expose an incomplete object.

#### 13.6. Runtime type tests and checked casts

##### 13.6.1. Runtime is tests

In ordinary expressions, `value is T` and `value is not T` are non-associative comparisons. The right side is one named struct Core, with optional qualification and resolved type arguments, but no Semantics, Origin, binding name, or requirement composition. Expand aliases and check accessibility. Unresolved type parameters/associated Types and non-struct targets are outside this initial syntax.

The left side must have `obj/S`, `rc/S`, `arc/S`, `objref/S`, or `objuniq/S`, with a struct Core `S`. Evaluate it once, then return `Supports(RuntimeObjectType(value), T)` or its negation. Generic identity includes the relevant arguments. This operation itself neither Copies nor Moves nor Consumes the operand, changes counts, nor acquires stronger authority. Getter/call evaluation, required shared access, temporaries, and cleanup retain normal effects.

```kimi
require value is Dog and value.isHealthy() else => return
value.bark()
// value is Dog or Cat parses as (value is Dog) or Cat, not a two-Type test.
```

The syntax context determines `is` before lookup: Constraint Clauses and associated-Type conditions retain their Requirement Test; ordinary initializers, arguments, and runtime conditions use this test. `T is Comparable` in an ordinary expression is not retried in the Type namespace. Parentheses preserve the surrounding context. Compile-time directives allow only environment conditions and reject every `is` test.

Accept well-typed tests even when static information proves them always true or false; a warning is allowed. Do not omit left-side effects or derive additional unreachable paths from that knowledge. Conditional Type information follows [flow refinement](#1410-type-refinement). Ordinary owners, value borrows, pointers, numeric values, Tuples, and enum variants are not test subjects. This syntax does not add optional or pattern binding.

##### 13.6.2. General view tests and checked casts

The object model additionally defines view-support tests for any valid View Target and a distinct exact-type test. A support test queries `Supports(RuntimeObjectType(value), V)`; an exact test compares Runtime Type Identity with a concrete Core and excludes derived Types. The source syntax for contract-view tests and exact tests remains deferred; it does not extend the initial `is` syntax above. A Boolean saved in a variable carries no refinement provenance.

For a statically valid checked cast to `V`, success is exactly the same `Supports` predicate, always using the original object's Dynamic Type, including from a contract view. Failure is an ordinary absent/error result, not Abort or unsafe reinterpretation.

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

Check target validity/accessibility, Semantics preservation, [Owned erasure](#1581-object-payload-erasure), result Origins/Loans, and destruction dependencies statically. Borrowing cannot create ownership or exclusivity; share explicitly before casting when needed. Evaluate/secure the source once. For a source certified by Owned payload erasure, a missing fixed Origin binding may be supplied only as static where the §15.2.3 proof covered that binding. Thus a cast to a concrete `Box<ref/i32 from static>` may be valid when Runtime Type Identity/Supports and all other checks match. This is a static proof, not runtime recovery of an Origin. Preserve the handle's outer borrow Origin. Do not invent non-static bindings, bind per-call callable Origins, or rewrite other information excluded from that proof; reject a target needing information not preserved or certified. API names and Option/Result branching syntax remain design boundaries; these guarantees do not define a cast spelling or add checked casts to `@`.

#### 13.7. Assignment

##### 13.7.1. Simple assignment

`target = value` returns Unit and evaluates in this order:

1. Evaluate the right side fully and secure a temporary of the statically determined destination Type by Copy/Move; update the source state and responsibility.
2. Evaluate the left receiver and access path left to right once. Invoke intermediate getters needed to locate the target, but not the getter of the final target.
3. Destroy the old value or initialized parts still present at that destination. Skip Moved/Uninitialized parts.
4. Place the temporary by ordinary Copy/Move rules, transferring its responsibility as applicable and leaving the destination Initialized.

For custom/computed/required set, pass the secured input to its setter instead of steps 3–4. Standard set directly places storage under §11.1 permissions and may restore incomplete storage. Constructor first placement follows §11.3.1. A destination rooted in a getter-owned temporary is restricted by §11.2.3.

Replacement uses existing storage without invoking incoming constructors or declaration initializers. Clean complete old values by their exact Type’s full chain; clean incomplete ones under [partial cleanup](#1632-field-cleanup). A derived value’s base view is not a whole-value target. Check destination/ancestor permissions and Loans first. If old cleanup does not complete normally, install nothing; neither restore the old state nor continue with an observable empty destination.

The destination Type is known statically; type checking does not evaluate the left side early. RHS-first evaluation lets the destination supply its own old value. It deliberately differs from compound assignment and ordinary receiver calls:

```kimi
values[index()] = makeValue()  // makeValue, index, old destruction, placement.
values[index()] += amount()    // index, old read, amount, compute, write.
obj().prop = arg()             // arg, obj, setter.
obj().setProp(arg())           // obj, arg, method call.
object.view.x = 10 // If computed view returns uniq/Point: RHS, view get, x set.
```

Use explicit locals when a particular order for receiver or index effects is needed.

Destination location uses the state after RHS evaluation. It may locate a Moved writable local's storage without reading its former value, but cannot read a Moved owner/receiver to find a target. Storage must remain valid from location through placement.

**Destruction of the old destination must not invalidate an Origin or Loan required by the secured RHS result.** Check the same dependencies during LHS evaluation, its temporary cleanup, target access, later use, and Destruction. New values at an identical address do not inherit dependencies on the old value.

```text
RHS result borrows data owned by the old destination.
Destroying the old destination would destroy that data.
=> Reject the assignment: placing the result afterward cannot repair its Loan.
```

If the RHS does not complete normally, do not evaluate the LHS. If LHS evaluation fails, do not destroy or place; if old-value destruction fails, do not place. Earlier effects and Moves are never rolled back. Keep the secured result alive during LHS evaluation; an ordinary control transfer cleans remaining temporaries under their normal lifetimes, after securing any transfer result. Abort follows the common termination rules.

```kimi
var x = makeResource() // Non-Copy owned value.
x = x                 // Move to temporary, locate x, skip absent old value, Move back.
// No user-defined deinit runs; no self-assignment exception is needed.

var count: i32 = 0
let done: () = (count = 20)
```

Self-assignment of Copy values uses Copy then Replacement. Non-Copy standard stored `x.p = x.p` may Move out and restore under Property permissions; custom set blocks that source Move. Custom/computed/required get calls its getter, then the selected set if the result fits its input and receiver/Loan conditions hold. Let cannot be reinitialized.

Right associativity parses `a = b = c` as `a = (b = c)`; the inner Unit result makes ordinary numeric chaining invalid. Assignment is not a Boolean condition. Destructuring, whole-Slice assignment, and initialization of raw uninitialized memory need separate rules.

##### 13.7.2. Compound assignment

`+= -= *= /= %= &= |= ^= <<= >>=` perform the corresponding binary operation and return Unit. Resolve the destination once, read its old value once, evaluate the right side, compute, and write once. This is not a textual replacement with `target = target op value`; receivers and indices are not repeated.

String `+=` remains subject to the deferred operator ownership design in [§13.3](#133-arithmetic-bitwise-and-shift-operators); this section's evaluation order does not supply its missing acquisition rules.

Select read and write independently under §11: standard operations use permitted storage access, custom/computed/required operations call their accessors. The read result must support the operator, and its result must fit set input. Require valid receivers and Loans through every stage; an owning getter may consume a receiver needed by set. Do not insert duplication, retry borrowing after failed Move, or bypass a setter through exclusive storage access. Evaluation remains target/read first, then RHS; updates of getter-owned temporaries remain forbidden (§11.2.3).

```kimi
values[nextIndex()] += amount() // Index, old value, amount, addition, write.
// item: Resource has standard get and custom set.
holder.item += x // Cannot acquire non-Copy item by Move for this update.
// User-defined Resource arithmetic is itself unavailable (§13.8).
holder.item = rebuild(holder.item@ref/Resource, x)
// OK if rebuild returns an independent owner and its input Loan ends before set.
```

If the right side or operation does not complete normally, do not write; getter and operand effects already performed remain. Compound assignment is not atomic and does not provide synchronization. Raw-pointer `+=` / `-=` use only the permitted displacement operations and their unsafe conditions; other pointer compound assignments are forbidden.

#### 13.8. Extension boundaries and reserved syntax

Operator symbols, precedence, and associativity are fixed by the language. User-defined comparison uses the [Core Contract mapping](#1341-contract-comparison-mapping). User-defined arithmetic remains deferred and unavailable; a same-named method does not authorize an operator. Future arithmetic must preserve evaluation order and counts and assignment's Unit result.

`and`, `or`, `not`, `=`, `@`, `is`, Ranges, and control transfers cannot be reinterpreted by user code. Custom operator symbols and precedence declarations are not defined. Neither are `!`, `&&`, `||`, `~`, `**`, `??`, `?.`, or ternary `?:`; use logical keywords and `if`. Unary `&` is not a borrow operation; use `@ref` / `@uniq`. Prefix `move`, a Move accessor, and a dedicated `<-` Move operator are not defined. Recognition by the lexer alone does not make a token a usable operator.

`#Name` is an Attribute and `#if` / `#switch` are compile-time directives, not runtime unary operators. The **Composition Root** is the reserved root for language-provided operations selected by `$`, independently of ordinary Name lookup; it is neither a value nor a macro facility. This revision defines only `$abort(...)`, under [Abort Termination](#173-abort-termination). Other operations, dependency resolution, and extension/lifetime rules are not defined and confer no source-language permission.

### 14. Control flow

Control-flow expressions are `if`, `match`, `for`, `while`, `loop`, `do`, and the transfers `return`, `exit`, `continue`, and `yield`. `unsafe`, `defer`, and `require` are statements; they cannot be initializers, arguments, or expression operands.

| Concept | Role |
| --- | --- |
| Delimiter region | Determines syntactic nesting and body/branch joins (§2.2). |
| Body scope | Determines local name scope and cleanup. |
| Transfer target | Receives a return, exit, continue, or yield. |
| Lookup barrier | Stops transfer lookup; functions and deferred bodies have the restrictions in §14.5.2. |
| Evaluation Context | Determines whether a value is used or discarded (§14.2). |
| Completion | Describes normal completion or a transfer to a resolved target. |

Parentheses create a delimiter region, not a body scope, transfer target, or lookup barrier. A do expression receives named exits but does not stop other transfer lookup. An **Iteration Construct** is a `for`, `while`, or `loop`; an **iteration** is one execution of its body. A **selection** is one `if` chain or `match`.

#### 14.1. Completions

**Normal completion**, written `Normal(result)`, returns control to the evaluator. A statement's normal Completion uses Unit without making the statement an expression. An **abrupt completion** is `Return(target, result)`, `Exit(target, result)`, `Continue(target)`, or `Yield(target, result)`. The transfer expression itself does not complete normally, even when its target subsequently does.

Omitted return, exit, and yield operands mean `()`; continue has no result. After required [Scope Exit](#162-scope-exit-destruction), a construct receives its own valid transfer or propagates one addressed to an outer target:

| Target | Received Completion | Action |
| --- | --- | --- |
| Iteration or do expression | `Exit(self, result)` | Complete the expression with that result. |
| Selection | `Yield(self, result)` | Complete the selection with that result. |
| Deferred body | `Exit(self, ())` | Finish this body's cleanup and resume pending Scope Exit. |
| Iteration | `Continue(self)` | Continue at the construct's next iteration point. |
| Function | `Return(self, result)` | Deliver the secured result to the caller. |

**Divergence** never finishes and produces no Completion. **Abort** ends the process without delivering a Completion or performing ordinary Scope Exit. Completion, divergence, and Abort are distinct Evaluation Outcomes. Never is a Type, not a Completion variant; infer it under §14.9, not directly from Runtime Reachability.

#### 14.2. Blocks and evaluation contexts

All executable bodies use the common **Body**:

```text
Body := "=>" (Expression | Statement)
      | NEWLINE INDENT ItemList DEDENT
```

A **single-item body** contains one expression or statement starting on the header's ending physical line. It may span more lines through ordinary expression continuation or a match arm list. An **indented body** (also called a Block body) contains declarations, expressions, statements, and permitted compile-time directives evaluated in order. Its direct expressions, including the last, are discarded; structural end arrival supplies Unit where the owner uses the body result. Iteration body completion instead starts the next iteration.

The two forms apply to selection clauses/arms, iteration, do expressions, unsafe, defer, require failure bodies, functions, anonymous functions/Closures, specializations, accessors, init, and deinit. Declarations and directives require the indented form. Preserve each position's declaration restrictions; explicit function Constraints remain at the start of an indented function body (§7.4). Declaration Containers and match arm lists are not executable bodies. Bodyless declarations and standard accessors retain their existing rules.

| Concept | Question |
| --- | --- |
| Evaluation Context | Is this position's value used or discarded? |
| Expected Type | What Type does the surrounding position require? |
| Target Result Type | What Type constrains results supplied to this target? |
| Expression Type | What Type does the checked expression have? |

Initializers, arguments, conditions, match/iteration subjects, and operator/transfer operands use **Value Context**. Standalone expressions and direct expressions in indented bodies use **Discard Context**. Parentheses preserve both context and expectation. Expected Unit, later non-use, reachability, and optimization do not turn a Value Context into Discard Context.

Discarding a general expression destroys its result at the normal lifetime; it is not an implicit conversion to Unit. Check all local operations, acquisition, ownership, Loans, and required warnings, including discarded Result values. Body expressions use the following rules, fixed before checking that body:

| Owner | Single-item expression and normal completion | Explicit result transfer |
| --- | --- | --- |
| Function / get | Discard and complete with Unit if its return Type is already fixed as Unit; otherwise use Value Context and return the value. | return |
| if / match / do | Inherit the owner's context: use the value in Value Context; discard it and complete with Unit in Discard Context. | yield for selections; named exit for do |
| for / while / loop | Discard and continue iteration. | exit |
| unsafe / defer | Discard and complete the body with Unit; defer executes later during cleanup. | exit to defer; unsafe has no target |
| set / init / deinit | Discard and complete the body with Unit. | return |
| require failure | Discard; normal continuation after require is forbidden, including cleanup. | No target of its own |

Statements in single-item bodies supply Unit only if they structurally complete normally. Values discarded directly inside a body need not have a common Type. Values supplied to a target must fit its Target Result Type even when the target's result is discarded.

The Target Result Type is fixed as Unit for for, while, defer, set, init, deinit, and discarded if/match/do/loop expressions. Explicit transfers still fit that Type: `return 123` in a Unit function and `loop => exit 1` in Discard Context are errors. A value-used loop takes its result from self-targeted exits, never from its body end. Receiving a transfer does not additionally supply an implicit body result.

```kimi
if ready => visited.insert(id)   // A bool result may be discarded.
let bad: i32 = if ready
    compute()                   // Discarded; implicit Unit does not fit i32.
else => 0
```

Changing body form is a semantic refactoring: preserve results, targets, scopes, and destruction order, adding return/yield/named exit when needed. Formatters retain body forms (§2.2).

##### 14.2.1. Nonempty executable blocks

An indented executable body requires at least one complete source item. Blank lines and comments do not count. A directive counts only with its required syntax. Diagnose an absent body before EOF or an item at the same or shallower indentation; do not absorb that item. Write `()` to do nothing. A single-item body already requires one complete expression or statement; local declarations are not single items in this form.

```kimi
defer
    // Error: no source item.
nextOperation()

defer => () // Explicit no-op body.
```

##### 14.2.2. Conditional compilation and results

Check source nonemptiness before directive selection. A complete directive counts even when it selects no executable items. After selection, apply normal Type, transfer, and result checks. The selected match arm list must still contain at least one arm; declaration-container emptiness follows its own rules.

```kimi
defer
    #if windows
        closeHandle()
// Valid source body even if the directive selects nothing.

let value: i32 = if ready
    #switch
        #case windows
            yield 1
        #case _
            yield 2
else => 0
// Directives are allowed in this indented body, even inside an initializer.
```

Selection that removes a needed transfer may expose structural end arrival and therefore a Unit result that fails the target's Type. Nonemptiness does not exempt result checking.

##### 14.2.3. Conditions and temporary lifetimes

If, else-if, while, require, and match guards use a bool expression. No `if let ...`, `while (let ... )`, var condition, or condition Pattern binding is permitted. Nested constructs inside that expression retain their ordinary Body declaration rules.

Each test evaluates the condition once, secures its bool result, destroys remaining condition temporaries in reverse creation order and ends guard-local temporary Loans, then branches using the secured bool. Cleanup effects update state but do not reevaluate the bool. A transfer, divergence, or Abort during evaluation/cleanup prevents the subsequent test continuation on that path. Match subjects, iterators, and explicit bindings retain their own owning scopes; moved values retain their destination lifetime.

```kimi
if (test: do
    let ready = check() // check returns bool; this declaration belongs to the do body.
    exit to test: ready
)
    work()

let guard = registry.lock()
if guard.contains(id) => work() // Keep a needed guard outside the condition.
```

To acquire a new local on every test, use loop with a declaration and require. Parentheses do not change transfer lookup or Evaluation Context. Header grouping and continuation follow §2.2.

#### 14.3. Block constructs

| Construct | Category | Execution |
| --- | --- | --- |
| do / Label: do | Expression | Execute now; receive a named exit when labeled. |
| unsafe | Statement | Execute now with lexical unsafe permission. |
| defer | Statement | Register now; execute at Scope Exit (§16.1). |

##### 14.3.1. Body forms and scopes

Both Body forms create an independent body scope. No standalone indentation-only body is allowed. Unsafe and deferred statements occur in executable scopes, not directly in Declaration Containers. Their body scopes do not make them expressions.

```kimi
defer => unsafe => releaseRaw(pointer)
defer => exit () // Valid: end the deferred body with Unit.
defer => return  // Error: cannot return from the outer function.
```

`unsafe/T` remains Type Semantics syntax and `unsafe func` a modifier. In statement position, unsafe introduces a Body, not a colon-delimited label. Declaration and lexical role rules remain in force.

##### 14.3.2. Do expressions

`do Body` executes once and uses the result rules in §14.2. Its optional label permits self-targeted `exit to Label`. Unlabeled exit skips do expressions, so an unlabeled do with an indented body cannot supply a non-Unit result of its own. Outward transfers and divergence are permitted.

```kimi
let result = work: do
    if cached() => exit to work: cachedValue()
    exit to work: compute()

let direct = do => compute()
do
    let resource = openResource() // open is a reserved modifier keyword.
    defer => close(resource)
    use(resource)

let block = readBlock() // Ordinary identifier; no keyword interpretation.
```

`do` is reserved; `block` is an ordinary Name. Header expressions containing a do expression need grouping under §2.2.

##### 14.3.3. Unsafe block

An **Unsafe Block** is an unsafe statement with a Body. It executes immediately with lexical permission for unsafe operations. It has no transfer target or lookup barrier. To supply a value outward, use return, yield, or a named exit inside the body; `let x = unsafe => readRaw(pointer)` is invalid because unsafe is a statement.

Permission reaches nested deferred bodies but does not cross Function Boundaries. Type, ownership, and Loan checks remain mandatory. An `unsafe func` declaration does not itself grant permission inside the function (§7.5).

```kimi
unsafe => releaseRaw(pointer)
unsafe
    releaseRaw(pointer)
    updateState()
```

#### 14.4. Labels

An optional `Label:` may prefix if, match, for, while, loop, or do on the same physical line. Labels use a namespace separate from variables and Types. Reject equal label names whose active scopes overlap in one function.

A label is active only inside its construct's bodies, not its own conditions, iterable, subject, or guards. A transfer may target only an enclosing construct in the same function, never a sibling, inner, or finished construct. A label names a construct, not an instruction address.

Group a labeled expression where its colon conflicts with a named argument or dictionary separator. A labeled return/exit/yield operand must be parenthesized whether or not the transfer itself names a target.

```kimi
consume(value: if ready => 1 else => 0)       // Named argument.
consume((choice: if ready => 1 else => 0))    // Labeled expression.
return (work: do => compute())
yield to outer: (inner: if ready => 1 else => 0)
```

#### 14.5. Control transfers

##### 14.5.1. Syntax and operands

```text
return [Expression]
exit [Expression] | exit to Label [: Expression]
continue [to Label]
yield [Expression] | yield to Label [: Expression]
```

Brackets mark optional syntax. Omitted return/exit/yield values mean `()`, checked against the target's result Type. For, while, and defer accept Unit expressions as exit operands, not non-Unit values. Continue has no value; return has no named form.

The operand, and `to Label` when present, start on the transfer keyword's physical line. A named value follows `:`; omit that colon when omitting the value. Normal continuation is allowed after the expression starts. `to` is contextual only immediately after exit, continue, or yield; write `exit (to)` to use a variable with that name. Postfix `value to Label` and `value from Label` are not transfer syntax.

```kimi
exit to search: score(item)
exit to outer: -1
continue to outer
yield to choice: match mode
    .Fast => 1
    _ => 0
```

Transfers have Type Never. Operands are still checked against their own target, and result acquisition and delivery follow §16.2.2.

##### 14.5.2. Target lookup

| Transfer | Unlabeled target | Named target | Lookup barrier |
| --- | --- | --- | --- |
| return | Nearest function | None | defer |
| exit | Nearest iteration or defer | Named iteration or do | Function; named lookup also stops at defer. |
| continue | Nearest iteration | Named iteration | Function and defer |
| yield | Nearest if or match | Named if or match | Function and defer |

Resolve the target first, then check context, operand, and Type. Never search farther outward after a mismatch. Missing or wrong-kind targets are errors. An unlabeled yield to a discarded selection is an error even with no value; suggest naming the intended target. Named yields still fit the target's result Type, which is Unit in Discard Context.

This extra unlabeled-yield check prevents a conditional yield from accidentally ending only its nearest inner if. It does not alter Type fitting or make Discard Context transparent to lookup.

A construct acts as a target or barrier only inside its bodies. Unsafe and require create neither. A do expression passes through all transfers except its named exit. Selections pass return/exit/continue, and iterations pass yield. No outward transfer crosses a function or deferred body.

##### 14.5.3. Function boundaries

Function Boundaries include named functions, methods, anonymous functions, Closures, explicit specializations, accessors, init, and deinit. A function declared inside defer retains its own return target. A constructor's Unit control result is distinct from the owned constructed value; deinit return does not skip automatic field destruction. Named functions keep their declared or default Unit result even if their bodies never finish (§7.1).

##### 14.5.4. Label and nesting examples

```kimi
let result = choice: if enabled
    if cached() => yield to choice: cachedValue()
    yield compute()
else => 0
// Omitting 'to choice' in the conditional yield targets the discarded inner if: error.

outer: for row in rows
    for cell in row
        if skipRow(cell) => continue to outer
        if done(cell) => exit to outer
```

#### 14.6. Iteration constructs

Each iteration has a fresh body scope. Discard its normal body result and clean that scope before continuing.

##### 14.6.1. `for` and `while`

For acquires its iterable once, then obtains successive elements. While evaluates its bool condition before each iteration. Both complete with Unit on exhaustion/false or a self-targeted exit; exit operands must fit Unit. Body end and self-targeted continue request the next element or reevaluate the condition.

ForBinding is a single Name or a Tuple of distinct Names, not a general Pattern. Bindings are immutable let bindings. Conditions use §14.2.3, including cleanup before branching and the ban on condition-binding syntax.

```kimi
for (key, value) in dictionary => process(key, value)
while ready => process()
```

While retains a static false-condition path even for `true`. Use loop for unconditional repetition (§14.9.2).

##### 14.6.2. Iteration protocol and acquisition

The recognized Core Iterable and Iterator Contracts define for iteration. Evaluate E once and acquire it, using ordinary Copy/Move, into a hidden iterable local. Invoke its consuming iterate mapping once to obtain a hidden iterator local. Repeatedly invoke Iterator.next with a short exclusive reborrow of that iterator. A Some payload supplies the next element; None terminates the loop. Missing or ambiguous conformances are errors; no method-name duck typing or fallback protocol is used.

~~~text
iterable := acquire(E)
iterator := Iterable.iterate(consume iterable)
repeat:
    step := Iterator.next(exclusive reborrow of iterator)
    if step is None: finish
    acquire Some payload into fresh iteration bindings
    execute body
    clean up iteration bindings before requesting the next step
clean up iterator on normal exit and ordinary transfers
~~~

Each for binding is an immutable let binding scoped to that iteration's body; there is no implicit var form. Payload acquisition is Copy for Copy Types and Move otherwise. Parenthesized bindings require a Tuple with exactly that many elements and acquire its components left to right; names must be distinct. Neither key/value member names nor an arbitrary deconstruction method supplies this Tuple.

End next’s receiver Loan before the loop body. Results may retain existing external dependencies, but cannot borrow that exclusive receiver or iterator-owned storage; lending iteration is deferred.

| Iterable | Yielded value and acquisition |
| --- | --- |
| Array or fixed array under `owner` Semantics | Consume elements as T. |
| ResolvedRange | isize; unresolved Range is not Iterable. |
| Slice | ref/T from source, even for Copy elements. |
| Dictionary under `owner` Semantics | Consume (K,V) pairs in insertion order. |

Direct iteration consumes a non-Copy owning collection; use `for item in values[..]` for shared iteration. A consumed source remains unavailable until valid reinitialization. Preserve source Loans while the iterator or escaped yielded references need them; reject overlapping mutation and allow nonconflicting mutation under ordinary Loan rules. See [ranges](#463-range-and-resolvedrange) and [Slice iteration](#467-slice-iteration-and-nested-origins).

Body fall-through and continue clean up the current bindings before calling next again; exit, return, and outer transfers also clean up the iterator and its unyielded owned elements. This protocol adds no cleanup guarantee on Abort and no rollback of prior Moves.

##### 14.6.3. `loop`

Loop repeats unconditionally. Body end and self-targeted continue restart its body after cleanup. Only self-targeted exit supplies its normal result. Other transfers supply results to their own targets.

In Value Context, infer the loop result from all self-targeted exits under §14.9. In Discard Context its Target Result Type is Unit. A body end is never a loop result source.

```kimi
let found: i32 = search: loop
    for item in items[..]
        if accepts(item) => exit to search: score(item)
    exit -1
// score returns i32. An unnamed exit inside for would have to fit Unit.

let once = loop => exit 1 // Value Context: result 1.
loop => exit 1            // Error: discarded loop requires Unit exits.
loop => work()            // Repeat work, discarding its result.
```

#### 14.7. Conditional expressions and yield

##### 14.7.1. Branch results

Each if clause or match arm independently chooses its Body form. Normal results, discarded values, and self-targeted yields follow the common rules in §14.2 and §14.9.

```kimi
let value = if ready
    prepare()
    yield 1
else => 0

match command
    .Put(let key, let value) => table.insert(key, value) // Discard Option<V>.
    .Clear => table.clear()                           // Unit.
    _ => ()
```

##### 14.7.2. `if`

If tests conditions in order and executes the first true clause, or else if none are true. An else-if chain is one selection. Omitted else is exactly `else => ()`, including its Unit result source in Value Context.

```kimi
if ready => 1                 // Allowed: discard the value.
let bad = if ready => 1        // Error: integer and omitted-else Unit conflict.
let unit: () = if ready => ()  // Valid, including the omitted else.
```

Condition evaluation follows §14.2.3; clause joins and nested-if grouping follow §2.2.

##### 14.7.3. Nested `yield` targets

Yield ends its resolved selection, not a function or iteration. It can cross an iteration but not a function or deferred body. For early completion of a discarded selection, name the target and supply Unit. Conditional transfers should name an outer selection when the nearest if is not the intended target (§14.5.2).

```kimi
let result = selection: if enabled
    for item in items[..]
        if accepts(item) => yield to selection: score(item)
    yield -1
else => 0

action: match event@ref
    .Save
        if readOnly => yield to action
        save()
    _ => ()
```

#### 14.8. Match expressions and patterns

Evaluate and acquire the subject once. Try arms in source order, selecting the first matching Pattern whose optional bool guard is true. Execute only that body; there is no fall-through to another arm. Each arm has its own Body, result rules, and binding scope.

**Match is exhaustive in every Evaluation Context and body form.** Indicate intentionally ignored values with `_ => ()` or suitable Case-specific arms. A selected arm list must be nonempty. Coverage uses only §14.8.4's conservative proof rules.

```kimi
func positiveOrZero(value: Option<i32>) -> i32
    return match value
        .Some(let n) if n > 0 => n
        .Some(_)
            log("non-positive")
            yield 0
        .None => 0
```

##### 14.8.1. Patterns

Patterns are dedicated syntax, not general expressions. Initially they occur only in runtime match arms, not declarations, parameters, `for`, `if`, `while`, `require`, or compile-time `#switch`.

| Pattern | Example | Meaning |
| --- | --- | --- |
| Wildcard | `_` | Accept without testing or acquiring the value |
| Literal | `0`, `-1`, `true`, `'A'`, `"ok"` | Built-in value comparison |
| Enum Case | `.None`, `.Some(let x)`, `FileError.NotFound` | Test the Case and recursively match its payload |
| Binding | `let x`, `var x` | Accept and introduce an arm-local binding |
| Unit / Tuple | `()`, `(let x,)`, `(let x, 0)` | Match the exact arity and elements |
| Grouping | `(.Some(let x))` | Preserve the enclosed Pattern |

```kimi
// result: Result<Option<User>, Error>
match result
    .Ok(let option)
        match option
            .Some(let user) => use(user)
            .None => useDefault()
    .Err(let error) => report(error)
```

Each nested Case's expected Type comes from that position's payload Type, using [Case resolution](#632-case-construction-and-resolution). Bare names, constants, Properties, calls, and arbitrary expressions are invalid Patterns. Use `let x` to bind, `.Some(...)` or `Option<T>.Some(...)` for a Case, and `let x if x == expected` for comparison with an existing value. A misspelling never becomes a catch-all. Pattern execution invokes no constructor, getter, conversion, or user-defined operator.

Case, Tuple, Unit, and Literal Patterns are **structural Patterns** here. At each position they inspect an owned value directly, or implicitly dereference `ref/T` at most once and inspect the immediate referent with shared access if its Type supports that Pattern. Grouping adds no dereference; Wildcard and Binding never dereference. Object Semantics and raw pointers do not implicitly dereference. Type checking uses the original matched Type and this rule, not a Binding's shared-reading result Type.

Each child payload/Tuple position applies the rule independently. Once a path passes through a borrow, descendants retain shared access. Valid nested Types such as `ref/ref/T` and `ref/uniq/T` still have a reference layer after one dereference and cannot take a structural Pattern at that position. Do not flatten Types or repeatedly dereference until a Pattern fits.

A position containing `uniq/T` permits only Wildcard or Binding, with optional Grouping. At the root, write `match value@ref`; for nested payloads, bind first and inspect in an inner match. No Pattern-local `@ref` syntax is introduced.

```kimi
enum Box origin source
    Value(uniq/Option<i32> from source)

match box
    .Value(let value)
        match value@ref
            .Some(let x) => use(x)
            .None => ()
```

On this owned path, `value` acquires the exclusive reference by Move, then the inner match creates a shared Reborrow. `.Value(.Some(let x))` is invalid. On a shared path, the body binding already receives a shared Reborrow under the [acquisition rules](#1516-match-acquisition-and-lifetime). Wildcard and Binding also accept Types that support no structural Pattern; binding a reference value does not acquire its owned referent.

Tuple arity and Case payload count must match exactly. Write `_` for each ignored element; no omitted, named, Rest, or shorthand fields are accepted. `(P)` groups, `(P,)` is a singleton Tuple, and `()` is Unit. `.Some((let x, let y))` matches one Tuple payload; `.Pair(let x, let y)` matches two payloads. Test structure from outside inward and left to right among siblings, stopping at the first mismatch without Copying or Moving payloads.

Literal Patterns support `bool`, integers, `char`, and non-interpolated ordinary or raw `string` literals. A negative integer is `-` followed by an integer literal. Apply the sign to its mathematical value before fitting to the matched integer Type: `-128` fits `i8`, while `128` and `-129` do not. Retain lexical magnitude limits and perform no runtime conversion.

Compare Booleans by value, characters by Unicode scalar value, and strings by exact UTF-8 content without normalization, case folding, or locale rules. Comparison does not consume the value or require runtime construction of an owned string. Coverage uses the fitted value, not source spelling: integer `0`, `-0`, and `0x00` coincide, as do character/string literals equal after escape processing. Reject invalid Types, arities, and out-of-range literals before coverage analysis. Floating-point, `null`, interpolated strings, Ranges, and arbitrary constant expressions are not Literal Patterns; use a guard for additional conditions.

##### 14.8.2. Binding scopes

`let name` and `var name` are irrefutable at their position. They create respectively non-reassignable and reassignable body locals; `var` grants no mutation or exclusive access to the original payload. Names in one Pattern must be unique: `(let x, let x)` is an error, not an equality test. `let _` and `var _` are invalid; `_name` is an ordinary binding with acquisition and possible destruction responsibility.

A Pattern name is visible only in its own arm: as a candidate name in the guard and a separate local in the body. It is not visible in the subject, other arms, after match, or for resolving its Pattern's Types and Case names. Ordinary shadowing and local redeclaration rules apply, and a single-item body also has an arm-local scope. Candidate and body names have distinct Binding Identities, so they may have different Types and acquisition effects. [Guard reading](#1483-guards) determines the former; [selected acquisition](#1516-match-acquisition-and-lifetime) determines the latter.

Pattern body bindings and the immediate arm body share one declaration space: the body cannot redeclare a Pattern binding name, while an explicitly nested block may shadow it.

Binding Type annotations, `name @ Pattern`, and an outer `let` applying to a whole nested Pattern are not introduced. Other Pattern extensions remain [deferred](#d1-enum-and-pattern-extensions).

##### 14.8.3. Guards

Each Binding position has an internal **Candidate Place** designating initialized storage in the Subject or its referent. This Access Designator creates no new storage, value, or owning local. In a guard, its candidate name performs **shared reading**: the Copy, shared Borrow, or Reborrow selected by the [shared element-read rules](#466-slice-operations-and-element-results), without invoking an actual getter. It does not read an uninitialized body local.

| Candidate's stored Type | Guard read Type | Body Type on an owned path |
| --- | --- | --- |
| `i32` | `i32` (Copy) | `i32` |
| Non-Copy `Data` | `ref/Data` | `Data` |
| `obj/User` | `objref/User` | `obj/User` |
| `ref/T` | `ref/T` (Copy) | `ref/T` |
| `uniq/T` | `ref/T` (shared Reborrow) | `uniq/T` |

A shared-path body binding uses the shared-reading Type too. The table omits Origins; complete Types preserve them and their Loans. Resolve each scope's expressions and overloads with its own Types. Do not retry guard lookup using the body Type or automatically transfer guard refinement facts to the body's distinct Identity.

```kimi
// Packet.Data stores Non-Copy Data; accepts takes ref/Data.
match packet
    .Data(let data) if accepts(data) => consume(data)
    .Data(let data) => recover(data)
    .End => ()
```

Here the first `data` is `ref/Data` in the guard and `Data` in the body. Writing `data@ref` in the guard copies that shared reference.

1. On a Pattern mismatch, skip the guard and try the next arm.
2. On a match, expose candidate names in the guard scope and evaluate the guard once.
3. On `true`, clean up guard temporaries and newly created Loans, then initialize body locals from the original Candidate Places, not guard read results, and run the body.
4. On `false`, perform the same cleanup, preserve Subject values, and try the next arm. Cleanup must finish normally before either continuation.

A guard is one expression whose normal result is `bool`. Parentheses are optional; `and`, `or`, and `not` keep ordinary semantics. No `let` conditions, comma condition lists, or guard chains are added. The arm's Body start ends the guard: either `=>` or the indented body. Nested body-bearing expressions in the guard require grouping under §2.2.

Candidate names, including `var` candidates, cannot Move, be reassigned, create exclusive borrows, or be captured. Candidate Places themselves cannot be returned or stored as values. Returning or storing a shared-read result depends on its transitive Origin/Loan dependencies and the destination, not Copyability alone:

| Shared-read result | Escape from the guard |
| --- | --- |
| Copy value without Candidate/Subject lifetime dependence | Ordinary rules permit it |
| Copy of a stored reference | Permitted when existing Origins/Loans and destination allow |
| New Borrow/Reborrow for Candidate reading, or any value depending on it | Forbidden; its Loan must end inside the guard |
| Copy aggregate with an existing Subject dependency | Check its Origins/Loans; it cannot outlive the Subject |

Copying a stored value retains its existing dependencies; reading its Candidate does not itself add a new dependency. These rules apply transitively through aliases, retained values, and callees.

Although ordinary capture acquires values by Binding Identity, a candidate Identity is not an allowed capture source, explicitly or implicitly, even with a Copy read Type. A value read into a separate ordinary local or parameter may be saved or captured under the table and normal rules. Thus `func [x] () => use(x)` directly capturing an `i32` candidate is invalid, while passing its read value to `saveCopy(x)` may permit storage by the callee. No capture spelling such as `@copy` is added.

From Pattern testing through guard cleanup, protect the Subject and traversed referents with shared access. Aliases and callees cannot change or consume them so as to invalidate the Case or candidate positions. Independent side effects are allowed and are not rolled back on a false guard.

If guard evaluation or cleanup does not complete normally, no body binding is ever initialized and no later arm runs. Payloads remain unacquired in the Subject or borrowed storage. On an ordinary transfer out of the guard, follow the [match cleanup rules](#1516-match-acquisition-and-lifetime); borrowed referents are not destroyed. Abort does not unwind, and divergence performs no subsequent acquisition or cleanup. A guard creates no transfer target or lookup barrier, but cannot `yield` to the match whose arm is still being selected. An inner selection may receive its own `yield`.

##### 14.8.4. Coverage and unreachable Patterns

Prove exhaustiveness using only the following limited rules over **unguarded** Patterns. Guarded arms, even `if true`, contribute no coverage. Do not use guard logic, constant subjects, call results, or a body's Never Type to supply missing coverage. Acceptance must not depend on a stronger proof that combines arbitrary Pattern domains.

For this proof, a **whole-position Pattern** is a Wildcard or Binding, a Unit Pattern at a Unit position, or a Tuple Pattern whose elements are all recursively whole-position Patterns. Grouping is transparent. A Literal or Enum Case Pattern is not a whole-position Pattern, even for an enum with one Case. Apply these rules after Pattern Type/arity validation; legal implicit referent inspection retains the same rules.

| Matched domain | Coverage model |
| --- | --- |
| Whole-position Pattern | One such arm covers the entire matched domain |
| `bool` / Unit | `true` and `false` / the single `()` value |
| Enum | Every declared Case has an arm whose payload elements are all whole-position Patterns; a payloadless Case needs only its Case Pattern |
| Tuple | One whole-position Pattern is required; do not combine partial Tuple Patterns |
| Integer / `char` / `string` | A Wildcard or Binding is required; enumerating Literals does not establish exhaustiveness |
| Structurally opaque Type | A Wildcard or Binding is required |

Complex payload and Tuple partitions remain valid Patterns, but their combination supplies no whole-payload or whole-Tuple coverage proof. Add an unguarded catch-all for the affected Case (for example, `.Some(_)`) or the entire subject (`_` or a Binding), or use nested matches with independently checked coverage. The `true`/`false` rule applies to a match whose subject is `bool`; it does not combine `.Some(true)` and `.Some(false)` into coverage of `.Some(_)`. These are limits on accepted proofs, not runtime matching. Treat all declared Cases as possible; do not infer recursively empty or uninhabited payloads.

Prefer Case-specific completion over a whole-subject catch-all when new Cases should trigger diagnostics. For Tuple-based state transitions, nested matches can preserve that check for each enum. Rewrites must preserve subject evaluation count/order, acquisition, and lifetimes; save subjects first when needed.

```kimi
// Copy enums: State has Idle/Running; Event has Start/Stop.
match state
    .Idle
        match event
            .Start => start()
            .Stop => ()
    .Running
        match event
            .Start => ()
            .Stop => stop()
// Adding a Case to either enum makes a match non-exhaustive.
```

```kimi
match flagOption
    .Some(true) => 1
    .Some(false) => 0
    .None => -1
    // Error: Some needs a whole-payload arm despite the Boolean partition.

match flagOption
    .Some(true) => 1
    .Some(false) => 0
    .Some(_) => 0                   // Required catch-all; no mandatory union-based warning.
    .None => -1                     // Valid coverage of Option<bool>.

match numberOption
    .Some(0) => 0
    .None => -1                     // Error: other Some values are missing.

match pair
    (true, _) => 1
    (false, true) => 2
    (false, false) => 3
    // Error without a catch-all: partial Tuple Patterns are not combined.

match pair
    (true, _) => 1
    (_, _) => 2                    // Valid: one whole-position Tuple Pattern.
```

Require an unreachable-pattern warning only if **one earlier unguarded Pattern** contains the later Pattern:

- Wildcard/Binding contains any Pattern at that position.
- Equal fitted Literals and Unit contain themselves.
- Same-Case and equal-arity Tuple Patterns contain another when every corresponding child does.

Ignore grouping, Binding names, and let/var for containment. The later arm may be guarded. Do not combine earlier arms, enumerate payloads, infer containment from partial overlap, or use guard logic such as `if false`. Stronger optional lints may not change acceptance or exhaustiveness proofs.

```kimi
match value
    .Some(_) => 1
    .Some(0) => 2                    // Warning: earlier Pattern covers it.
    .None => 0

// number: i32
match number
    0 => 1
    -0 => 2                         // Warning: same fitted value.
    0x00 => 3                       // Warning: same fitted value.
    _ => 4

match otherValue
    .Some(let x) if x > 0 => 1
    .Some(1) => 2                    // No coverage warning from the guard.
    _ => 0
```

This diagnostic is independent of [control-flow reachability](#1492-reachability). It never removes an arm from Type/ownership checking, result-source collection, or inference. Diagnose invalid Pattern Types/arity first. A failed exhaustiveness proof identifies a missing Case, required whole-payload arm, or required catch-all; it must not claim a runtime value is unmatched when only the limited proof fails. A mandatory unreachable warning identifies the single preceding covering arm.

#### 14.9. Result validation

After compile-time directive selection, distinguish four tasks. They define semantic responsibilities, not compiler passes or a required processing order.

| Task | Purpose |
| --- | --- |
| Result-source collection | Collect supplied values, including unreachable transfers, for Type constraints. |
| Structural Completion | Find normal-completion candidates from syntax and evaluation order; determine implicit Unit and eligibility for Never. |
| Runtime Reachability | Apply initialization, Move, Loans, refinement, and cleanup to the same conservative paths. |
| Type-checking continuation | Check unreachable source with valid local facts without adding execution paths (§14.10.3). |

**Result sources** supply values to a target. After resolving Body forms, contexts, and transfer targets, collect:

| Source | Constraint |
| --- | --- |
| Explicit transfer | Every self-targeted return/yield/exit operand, including unreachable ones; omitted operands supply Unit. |
| Single-item body | Expressions whose values are used as results under §14.2. A statement/discarding body supplies Unit if it structurally completes normally. |
| Indented body | Unit on structural end arrival when its owner uses that completion as a result. |
| Other implicit completion | Unit from omitted else, for exhaustion, and structural while-false completion. |

Assume entry to each body when collecting, including unreachable bodies. A sequence with no structural continuation does not acquire an end Unit, but later written result sources are still checked. Follow the continuation of transfers caught inside the sequence. Loop body ends are not loop result sources.

Always check syntax, Names, target/operand restrictions, local Types, and match coverage. Runtime Reachability and cleanup cannot add/remove result sources or replace inferred Types. Source expressions still use valid refinement information (§14.10); collection of sources and point-specific expression checking are separate responsibilities.

##### 14.9.1. Expression type and target Result type

The **Target Result Type** constrains results supplied to a target. Determine it independently of source traversal order:

1. Use the declaration or fixed Type from §14.2; otherwise use a fixed outer expectation.
2. If still unknown, collect independently typable source constraints together and find one common Type. Never alone supplies no concrete Type candidate.
3. Propagate the fixed Type to sources checkable against it. Apply numeric literal defaults only after other available evidence.
4. Check every source for fitting. If an unresolved call, anonymous function, or empty literal still needs a Type, require an annotation or explicit Type arguments.

Nested result expressions use the same expected-Type propagation. Do not commit an inner result that depends on numeric defaults before processing available outer constraints. Parentheses, labels, and body nesting alone do not commit defaults; an already typed binding is not reinferred from later uses. Preserve §10.5's call/lambda boundaries and §10.8's complete-Type inference rules. Do not search unresolved-source combinations, common bases, numeric conversion chains, or overload candidates by rechecking bodies.

Propagate a target's expectation only to its result sources, not discarded intermediate values or transfers to other targets. Apply normal Semantics, Origin, and fitting rules. If no source exists, or all source Types are already Never, an unknown Target Result Type may proceed to the Never rule below. An unresolved source is not an absent source.

```kimi
let large: i64 = 10
let first = if ready => 1 else => large
let second = if ready => large else => 1 // Both i64; no early default to i32.
let nested = if ready
    yield if cached => 1 else => 2
else => large                           // Inner and outer results use i64.

let small = if cached => 1 else => 2    // Committed i32 at this declaration.
let bad = if ready => small else => large // Error: i32 versus i64.

let conflict = loop
    continue
    exit 1
    exit "x" // Error: unreachable results must also have compatible Types.
```

The **Expression Type** is normally the Target Result Type. After checking every source, it is Never only if both conditions hold:

- There are no result sources, or all sources have Type Never.
- Structural Completion has no normal-completion candidate.

A fixed expectation alone does not create a supplied value. A non-Never source constrains the result even when unreachable. Never cannot rescue a Type mismatch, unresolved source, or missing required result. Runtime Reachability must not replace the Expression Type with Never, including when cleanup prevents delivery.

```kimi
let a = loop => continue      // Initializer Type Never; initialization never finishes.
let b: i32 = loop => continue // Initializer Never fits declared i32.

let typed = loop
    continue
    exit 1 // Still a result source: initializer Type i32, despite no normal path.
```

Named functions retain their declared or default Unit return Type. An anonymous function without a declared/fixed expected result uses these source and Never rules for return inference. The function value itself still has its Function Item or Closure Type. A constructor's Unit control result and deinit's return retain §14.5.3's separate construction/destruction obligations.

##### 14.9.2. Reachability

**Structural Completion** conservatively composes normal and targeted-transfer candidates. Normal completion of an item sequence is **structural end arrival**. Divergence and Abort supply no normal candidate. Do not prune paths using bool literals, condition values, constant propagation, Pattern containment, dynamic-type contradictions, or arbitrary callee-body analysis.

| Construct | Common path rule |
| --- | --- |
| Ordinary expression / sequence | Respect evaluation order; continue only from normal completion. |
| if / require | After condition evaluation, consider both true and false. |
| match | After acquisition, consider all arms and both outcomes of each evaluated guard. A non-completing evaluation does not continue on that path. Exhaustiveness removes an unmatched final completion. |
| and / or | After the left operand, consider both evaluating and skipping the right operand. |
| for / while | After acquisition/condition evaluation, consider both iteration and immediate exhaustion/false completion. |
| loop | Body end/continue repeat; only self-targeted exit completes the loop. |
| Transfers | Evaluate any operand first, then transfer. Do not continue at the source; follow the target's received transfer. |
| do / unsafe | Follow the body; do receives its named exit. |
| Declaration / defer registration | Perform necessary initialization/acquisition, then continue if normal. Do not execute function/defer bodies at declaration/registration. |

Never-returning calls and Abort do not continue normally. Structural Completion does not incorporate delivery blocked by Scope Exit cleanup.

**Runtime Reachability** applies initialization, Move, Loans, refinement, registered defer, and destruction to these same paths. Follow actual required cleanup and the continuation after a received transfer. It is a conservative static approximation, not proof of runtime feasibility. It validates require's failure non-continuation and execution-state legality, without changing result constraints.

```kimi
func incomplete() -> i32
    if true => return 1
// Error: structural false path reaches the body end and supplies Unit.

func shortCircuit(flag: bool) -> i32
    flag and (return 1)
// Error: skipping the right operand reaches the body end.

var port: i32
while true
    port = tryBind()
    if port > 0 => exit
use(port) // Error: the static false path need not initialize port.

var boundPort: i32
loop
    boundPort = tryBind()
    if boundPort > 0 => exit
use(boundPort) // Valid: every delivered exit follows initialization.

let stopped: i32 = work: do
    defer => loop => ()
    exit to work: 1
// Expression Type remains i32; Runtime Reachability stops at cleanup, before delivery.
```

Warn on `while true`, including parenthesized true, suggesting loop for unconditional iteration. The warning itself does not reject the program. `if false => consume(value)` still has a static Move path for non-Copy value; explain this in resulting use errors and suggest `#if` when static exclusion was intended. Optimization may remove dead runtime paths but must not change acceptance.

#### 14.10. Type refinement

##### 14.10.1. Stable bindings and effective types

Runtime Type refinement attaches to the current **Value Instance** of a resolved **Binding Identity**. Eligible subjects are parenthesized or bare names of object-typed local `let` bindings or non-reassignable parameters with struct Core targets. `var`, Fields, computed/required Properties, indexing, and call results can be tested but do not refine later reads. Facts do not transfer to aliases or shadowed bindings.

The **Effective Type** used for a name at a program point narrows only its guaranteed Core. Preserve declared Semantics, Origins, Loans, mutability, initialization state, object identity, and complete destruction responsibility. Writes/reinitialization that could replace the binding invalidate old facts; Move/destruction prevents further use. Mutation of members alone does not invalidate facts while the binding value and Dynamic Type remain the same. This adds no `var` refinement or write permission.

Use Effective Type for member lookup, argument applicability, overload resolution, assignment sources, results, and local inference. Apply each candidate's ordinary fitting/adaptation rules; do not discard a base candidate if those rules fit, retry resolution with the declared Type, or change already fixed declarations or destination Types. Explicit object upcasts remain necessary where ordinarily required.

```kimi
func handle(value: objref/Animal) -> ()
    let alias = value
    require value is Dog else => return
    value.bark()
    let dog = value       // Inferred objref/Dog.
    alias.bark()          // Error: alias has no Dog guarantee.
```

The new `dog` binding uses ordinary typing and acquisition of the RHS Effective Type, not transferred flow facts. An owning Non-Copy source would Move on such acquisition. A binding's declaration Type never changes.

The [inherited-Name rule](#622-inheritance-and-open-structures) keeps the declaration layer of an existing member unchanged by refinement when that member is accessible both to the derived author and at the relevant uses. For public Animal.speak, a Dog cannot add the same Name: a parameter/let before refinement, inside it, and after a join, an unrefined var, and Dog/Animal Views all select Animal's layer. Refinement can add access to a differently named Dog.bark. This does not freeze overload choice based on argument Types or promise identical lookup across access boundaries.

##### 14.10.2. Condition states and joins

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
| Subsequent joins | Only incoming paths retained by Runtime Reachability after transfers and cleanup (§14.9.2) |

Track continuations of constructs that catch transfers. Early return through an ordinary `if` can therefore refine later statements. Loop entries join the first entry and every backedge; exits join condition-false and delivered `exit` paths. Propagate `continue` to its correct iteration point. Solve these rules to a stable result independent of processing order; a previous successful iteration alone is insufficient.

`defer` resolves Names and Effective Types at registration, then checks initialization and Loans on actual cleanup paths. Later refinement cannot reinterpret an earlier registration. Function boundaries do not import outer flow facts. A capture uses the source binding's declared complete Type and the ordinary capture operation; it does not import refinement attached to the outer binding. To capture a narrowed Type, first initialize a new local from the refined expression. Its inferred declaration Type is then available under the normal capture rules.

##### 14.10.3. Type-checking unreachable code

Continue checking unreachable source items with a **type-checking continuation**. Preserve valid Effective Types and enclosing-condition facts; unreachability alone does not reset bindings to their declared Types. Apply ordinary condition, require, and join rules, retaining only common guarantees at joins.

After a transfer, use the state after operand evaluation/acquisition but before that transfer's scope-exit cleanup, at the source's lexical scope. Do not retain facts already invalidated by assignment, Move, or destruction. Do not restore uninitialized/moved values or ended Loans, or import outer refinement across a Function Boundary.

These continuations add no Structural Completion or Runtime Reachability edges, implicit Unit results, or normal successor to a require failure. Do not merge their states into reachable execution paths; still include their result-source Types under §14.9.

~~~kimi
func scoreInBranch(animal: objref/Animal) -> i32
    if animal is Dog
        return 0
        return animal.score() // Unreachable, but retains the Dog refinement.
    return 0

func scoreAfterReturn(animal: objref/Animal) -> i32
    return 0
    require animal is Dog else => return 1
    return animal.score() // Refined within the unreachable region.
// Both calls are Type-correct if Dog has score() -> i32.
~~~

#### 14.11. Require statement

##### 14.11.1. Syntax and placement

`require Expression else Body` is a statement in executable scopes. It evaluates its bool condition once, continues when true, and executes its mandatory failure body when false. The condition, temporary cleanup, and Body forms follow §14.2. The else is on the condition's ending physical line or the next effective line aligned with require.

It has no result, label, transfer target, or lookup barrier. It cannot be an initializer or argument. Its failure body uses the common Body rules.

```kimi
require ready else => return
require valid else
    logFailure()
    return
```

##### 14.11.2. Evaluation and non-continuation

Independently assume entry to the failure body, even for literal true. Under Runtime Reachability, no path including required cleanup may continue normally to the statement after require. Otherwise reject it. Outward transfers, Never-returning calls, divergence, and Abort can satisfy the requirement. Transfers caught inside the failure body must be followed through their continuations. Require inserts no implicit return, exception, or Abort.

```kimi
require true else => logFailure() // Error: normal continuation.
require ready else
    loop => exit                 // Error: only ends the inner loop.
require ready else => while true => () // Error: static false path completes.
require ready else => loop => ()       // Valid: no normal failure continuation.
```

Registering defer does not execute it. Analyze its effects on cleanup paths, using declared call result Types rather than arbitrary termination proofs. A literal false does not remove require's static true successor (§14.9.2).

##### 14.11.3. Transfers and cleanup

Require is transparent to transfer lookup in its condition and failure body; surrounding function/defer barriers still apply. It is not a rewrite to if, which would add a yield target. Secure results before cleaning scopes actually left. Success keeps the enclosing scope active.

```kimi
let answer = if enabled
    require ready else => yield 0 // Targets the outer if.
    yield compute()
else => 0

defer
    require needsCleanup else => exit // Ends this deferred body.
    cleanup()
```

Return still requires a Function Boundary, and a deferred body cannot return from its outer function. Abort performs no ordinary Scope Exit. Refinement after require follows §14.10.

## Part V. Ownership, cleanup, and failure

### 15. Ownership and lifetime analysis

#### 15.1. Initialization and consume analysis

This section checks the initialization, completeness, and Consume legality of values defined by [Values, places, and storage](#34-values-places-and-storage).

##### 15.1.1. Storage, state, and responsibility

Track initialization state and destruction responsibility per place:

| State | Meaning | Read / borrow / Copy / Move | Write to `let` | Write to `var` |
| --- | --- | --- | --- | --- |
| Uninitialized | No initialized value is held | Forbidden | Only if never initialized | Initialization |
| Initialized | An initialized value is held | Subject to Type and access rules | Forbidden | Replacement |
| Moved | The former value/capability and responsibility were transferred | Forbidden | Forbidden | Reinitialization |

Moved records the source's history, not whether the destination value is still alive. Track a `let` place's first initialization separately; neither Move nor internal Destruction resets it. This revision provides no general user operation to explicitly destroy a place and reset that history. State alone grants no access or write permission.

Every read, borrow, Copy, or Move requires initialization on every incoming Runtime Reachability path (§14.9.2). Unreachable source uses the separate checking state in §14.10.3; absence of an execution path does not restore an unusable value. `let` permits one initialization per binding lifetime on each path; `var` permits later initialization/replacement under ordinary permissions. Fields also obey their dedicated access rules.

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

##### 15.1.2. Aggregate construction and completeness

Track two facts independently: **construction completion**, recording successful completion of initialization, and **current completeness**, requiring every stored component to be Initialized and complete. A **complete value** satisfies both. For a derived structure, components are its base subobject and its own Fields; track completion separately at each base/derived layer. For an enum, only the active Case's payloads are components, and [Case construction](#632-case-construction-and-resolution) commits completion. Inactive Cases require no initialization. This revision has no optional-to-initialize stored fields. Computed Properties are not components and require no storage initialization.

Only the [constructor phases](#623-constructors) commit completion: validate success, finish cleanup with storage alive, then commit before exposing/transferring the value. No user operation commits early or resets completion; an incomplete exit or Abort never commits. Initialized fields alone do not skip remaining work. A complete field/base keeps its own completion and destruction responsibility even if its enclosing layer never completes.

Before completeness, whole-value reads, Copy, borrowing, Move, and exposure are forbidden, including ordinary accessors taking the whole `self`. Direct operations on initialized fields follow their own access rules. After a completed construction followed by Partial Move, direct [Field operations](#1112-move-paths-and-inherited-fields) are allowed; this does not authorize initial-construction access.

Partial Move changes current completeness, not the construction-completion fact. Permitted reinitialization of all missing fields restores completeness without rerunning a constructor. If a field cannot be reinitialized, that value remains incomplete and cannot be used/transferred as a whole; its remaining Initialized parts can still be used and cleaned up. No separate permanent-incomplete state is defined. Whole-value Move transfers its construction information; whole replacement uses the new value's information.

Tuple and array construction places elements in increasing element-index order. Each element acquires its own initialization and responsibility only when placement completes normally; a partly built element is tracked recursively. The aggregate commits completion after all elements are placed. If an element expression leaves the construction by ordinary control transfer, first secure that transfer's result, then clean the abandoned construction's remaining elements in decreasing index order. Previously moved arguments and completed side effects are not rolled back. Raw uninitialized memory is not an alternate safe construction syntax. Fixed arrays additionally require [whole initial construction](#43-initialization-and-inference); static-path repair after completed construction remains permitted.

##### 15.1.3. Move paths and partial move

A **Move Path** is a statically trackable path with independent initialization state and destruction responsibility. Initial paths include Fields reached through statically known base-subobject paths, Tuple elements, fixed-length array indices recognized by the literal-only ConstantIndexExpression rule below, and combinations of these. Runtime indices, dynamic containers, and user indexers are not added even for literal indices. Do not use optimization-derived constant propagation or arbitrary integer proofs to expand the accepted paths.

**Constant fixed-array indices.** A ConstantIndexExpression is one nonnegative integer literal token, optionally enclosed in any number of grouping parentheses. All integer bases and digit separators allowed by §2.6 are accepted. Its value must fit the ordinary expected isize Type; remove separators and decode the literal magnitude using the lexical integer rules. This recognition has no arithmetic, conversion, name lookup, general constant evaluation, or target-dependent environment evaluation.

~~~ebnf
ConstantIndexExpression := IntegerLiteral | "(" ConstantIndexExpression ")"
~~~

After resolving a fixed-array Type `[N of T]`, an index recognized this way forms a static element Move Path only when its value n satisfies `0 <= n < N`. Path identity uses the numeric value, so `1`, `0x1`, and `(1)` designate the same element. No optimizer result changes this classification. The same rule defines constant fixed-array indices for overlap analysis (§15.6.2); it grants neither a path through a dynamic collection nor permission to Move through a borrow.

| Index expression | Static fixed-array Move Path |
| --- | --- |
| `0`, `(0)`, `((0))`, `0x1` | Yes, when fitting isize and in bounds |
| `1 + 1`, `-1`, `+1`, `3@isize` | No; operators/conversions are outside this grammar |
| `if condition => 1 else => 2`, an immutable Name, `^1` | No; selection, name propagation, and from-end resolution are not literal recognition |

An out-of-range literal supplies no element path. This does not itself make an otherwise valid evaluated index operation a compile-time error: its bounds check follows §4.6 and §17.3.4 and Aborts if executed. An operation requiring a statically eligible Move Path is rejected if no such path exists, independently of whether its bounds check could fail. Literal-fitting errors remain ordinary compile-time errors. An expression such as array[1 + 1] is therefore still usable for ordinary permitted reads, but cannot gain Partial Move eligibility or prove disjointness from optimization.

Within an owned match Subject, selected Case payload positions also form Move Paths under [match acquisition](#1516-match-acquisition-and-lifetime). This does not expose general payload access or Partial Move from the caller's enum.

A Move Path defines tracking granularity, not access permission:

| Source access | Partial Move |
| --- | --- |
| Tuple element / constant-index fixed array element | Direct place acquisition normally Moves a non-Copy value |
| Stored Property slot | Standard acquisition Copies F when Copy, otherwise Moves under §11.1 permissions and Move Path rules. |

```kimi
var pair: (string, i32) = ("Alice", 30)
let name = pair.0  // Partial Move; pair is incomplete.
let age = pair.1   // Remaining initialized part is usable.
// let all = pair  // Error: incomplete.
pair.0 = "Bob"
let all = pair     // Complete again.
```

Do not Move a non-Copy referent or subpart through `ref`, `uniq`, `objref`, or `objuniq`, leaving the borrowed place Moved/Uninitialized, even if a later reinitialization is planned. Exclusive access does not transfer ownership. Copy acquisition leaves the source initialized; it is not extraction.

User-defined `deinit` assumes a complete value. Reject Partial Move that invalidates this assumption for the aggregate itself or any enclosing ancestor, including nested paths, methods, and Destruction. A complete owned value whose own Type has `deinit` may move as a whole.

Use [Exchange or Swap](#157-initialization-preserving-exchange), or a Type-specific operation, when ordinary extraction is forbidden. Exchange preserves initialization; Type-specific invariants remain the implementation's responsibility and may require restricted storage access. A Field path does not bypass an intervening computed getter.

##### 15.1.4. Consume verification and representation

Separate two static checks; they are not runtime fallback stages:

```text
Consume
├─ Eligibility: does the declaration, Type, and path provide the operation?
│  ├─ supported place kind and ownership path
│  ├─ trackable Move Path
│  ├─ required Field declaration and storage properties
│  └─ structural Partial Move / deinit restrictions
└─ Legality: may this use site perform it?
   ├─ required accessibility
   ├─ target Initialized on every incoming path; complete if an aggregate
   ├─ required receiver construction previously completed
   ├─ no conflicting Loan; valid Origins
   └─ actual ancestor path and Destruction conditions
```

The declaration kind is structural; accessibility depends on the use site. Constraints can prove structural facts, not current initialization or absence of Loans. Unknown structural facts follow [generic Access Effect resolution](#89-generic-access-effects); no new Consume contract syntax is defined.

An ancestor may be incomplete if the target remains Initialized and complete, and can be located without whole-value access to that ancestor. Whole-receiver reads/borrows remain forbidden. User-defined `deinit` can make a path structurally ineligible; also check the actual ancestors at each use. Moving a complete value as a whole is distinct from Partial Move.

Track per-path state, destruction responsibility, first initialization of `let`, construction completion, and current completeness across branches, loops, transfers, and `defer`. Apply [Destruction lifetime checks](#1566-destruction-lifetime-checking). Raw-pointer operations need not recover or repair an untracked original owner's responsibility.

Lowering may elide transfers and temporary storage or use conditional cleanup flags only while preserving values, abstract place identity and lifetime, Move state, Loans, Origins, destruction responsibility, and specified failures. Optimization must not change which programs or Move Paths are legal.

##### 15.1.5. Movable places

A **Movable Place** permits ownership/capability transfer from its current value. Safe direct sources are owned root Places, Tuple elements, fixed-array elements at eligible constant indices, and authorized stored Property slots. Require an Initialized complete target, no conflicting Loan, accessible consuming operations, and valid construction, partial-Move, Origin, and deinit conditions.

Borrowed referents, object fields, static storage, unsupported indices, and hidden Property storage cannot supply safe extraction. Generic owned arguments may move as whole values without an extra Contract. Raw dereference retains its Unsafe obligations.

Ordinary acquisition Copies Copy Types and otherwise Moves. A Move marks the source Moved and transfers its complete Type, dependencies, and destruction responsibility; moving a reference transfers capability, not referent ownership. A let binding may supply a Move but cannot be reinitialized. Restoring var needs write permission. There is no forced Move of Copy Types.

```kimi
let number: i32 = 10
let copied = number // Copy; number remains initialized.
let resource = makeResource()
let taken = resource // Non-Copy Move; resource is now Moved.
```

Getter results are acquired as results, never by moving hidden storage. An already owned temporary transfers to its destination under §3.6. Borrowing its destination does not restore the original source.

##### 15.1.6. Match acquisition and lifetime

`match E` evaluates `E` once and initializes an internal **Subject Place** by ordinary whole-value acquisition: Copy for a Copy Type, otherwise Move. Materialize an existing Temporary Value without an extra acquisition. This happens before arm selection regardless of bindings, Wildcards, or whether any arm succeeds. Optimization cannot change the original Place's Move state, lifetime, or Loans. A custom/computed/required get subject invokes its getter once; standard stored get uses permitted Place acquisition.

```kimi
// Message is Non-Copy.
match message
    _ => ()
// use(message)                    // Error: the whole value was Moved.

match anotherMessage@ref
    .Write(let text) => inspect(text) // text: ref/string
    _ => ()
use(anotherMessage)                // Valid after required Loans end.
```

Once an arm is selected, initialize its body locals left to right from [Candidate Places](#1483-guards). An unguarded arm is selected immediately on Pattern success; a guarded arm requires successful guard cleanup first.

| Access to the Binding position | Acquisition |
| --- | --- |
| Subject itself or owned Tuple/payload path without traversing a borrow | Ordinary Copy/Move of the stored complete Type |
| Element reached by structurally matching through `ref/T` | Shared reading under the [shared element-read rules](#466-slice-operations-and-element-results), without calling a getter |
| Wildcard position | No acquisition; existing responsibility remains with the Subject or referent's owner |

Resolve dependent read Types and Copy/Move/Borrow/Reborrow effects by the [Generic Access Effects deadline](#89-generic-access-effects). Borrowed access cannot Move a non-Copy referent or grant exclusive authority. In `.Some(let r)`, a stored `ref/T` is copied as a reference; a nested Case Pattern through that reference restricts descendant acquisitions to shared access. New borrows require valid referents and owners; copied references retain their existing Origins/Loans.

For `wrapper: Option<Result<i32, i32>>`, matching `wrapper@ref` with `.Some(let r)` Copies the Result into r; borrowing the subject does not force payload bindings to be borrows. A change to payload Copy capability can change binding Types and generic-body validity. No explicit borrow-binding or ordinary payload projection syntax is provided. APIs that need such a borrow may instead accept the whole enum by reference, potentially changing their public Signature; [Pattern acquisition selection](#d1-enum-and-pattern-extensions) remains a design boundary.

For owned decomposition, track each selected Case Identity and positional payload index as a Move Path inside the Subject, including recursive decomposition. Track remaining initialization and destruction responsibility after each acquisition; do not apply these internal paths to the caller's original enum or invent ordinary payload projection syntax.

The Subject lasts for the match evaluation. Secure a match result or outward transfer value first, clean the arm scope under ordinary Scope Exit, then destroy the Subject's remaining initialized parts. Body bindings enter scope left to right and are destroyed in reverse order, before the Subject; body locals and `defer` retain normal reverse registration order. A Wildcard does not destroy immediately. Unselected arms acquire no body ownership. Match is exhaustive and has no unmatched normal-completion path. Each later step requires earlier cleanup to complete normally; Abort does not unwind.

Borrowing through a borrowed Subject depends on the referent and original Loan, not the temporary slot storing that reference. Such a borrow may be returned when its original contract allows. A new borrow into an owned Subject's payload depends on the Subject and cannot escape match. Copying or moving a stored external reference retains that reference's external Origin instead. Returning a reference never extends its lifetime, and destroying a stored non-owning reference never destroys its referent. Guard-specific escape restrictions remain [stricter for new candidate-dependent Loans](#1483-guards).

#### 15.2. Origin expressions and ordering

The basic meaning of Origins and Loans is defined in [Origins and Loans: overview](#37-origins-and-loans-overview). This section defines annotation expressions and their ordering.

##### 15.2.1. Origin expressions

An Origin is the set of program points at which a borrow is guaranteed to be valid.

| Kind     | Examples                | Meaning                                         |
| -------- | ----------------------- | ----------------------------------------------- |
| Concrete | `x`, `self`, `x.source` | Origin carried by a parameter, receiver, or local value |
| Abstract | `source`, `left`        | Origin parameter declared by a function or type |
| Static   | `static`                | Built-in maximum Origin                         |

The syntax is:

```text
origin-expression := Name
                   | origin-expression '.' Name
                   | static
                   | origin-expression 'and' origin-expression
```

A direct safe-borrow parameter, receiver, or local used as an Origin denotes its value's outer borrow Origin, never the lifetime of the variable's storage:

```kimi
func first(x: ref/T) -> ref/T from x
```

`x.source` denotes the abstract Origin `source` carried by `x`. Qualification is required so that values of the same Origin-bearing type remain distinguishable:

```kimi
struct View<T> origin source
    func get(self: ref/Self) -> ref/T from self.source
```

The same projection applies to Origin-bearing local values; ordinary lexical visibility applies, and executable uses require definite initialization. A bare value name with no outer safe-borrow Origin is invalid as an Origin, even if its value has an owned outer Type with borrowed contents; select a declared Origin with `x.source` instead. Thus `from r` for a local `r: ref/T` always refers to r's referent. Borrowing r's slot uses an explicit layered Borrow (§13.5.5) and infers that slot's Origin. Storage Origins of owned locals likewise remain compiler-internal and are inferred from initialization. Local values cannot supply Origins in public signatures or outside their lexical scope.

##### 15.2.2. Ordering and intersection

`o1 : o2` means that `o1` outlives `o2`: `region(o1) ⊇ region(o2)`.

The relation is reflexive and transitive. `static` outlives every Origin.

`and` is the meet of two Origins:

```text
region(o1 and o2) = region(o1) ∩ region(o2)
```

Consequently, the intersection contains no region outside either operand; it may equal an operand. A result declared `from x and y` is valid only in the region common to both inputs.

Normalize intersections using these laws, with outlives simplification requiring proof under the [limited Origin solver](#1534-generic-origin-inference):

```text
a and b         = b and a
(a and b) and c = a and (b and c)
a and a         = a
a : b implies a and b = b
```

For a fixed binding and proof environment, flatten intersections, replace proven-equal or mutually outliving Origins by the least stable representative, remove duplicates and operands proven to outlive another operand, and sort the remainder. Use a deterministic total order based on stable Binding Identity, including declaration binder and parameter position where applicable, not spelling, input traversal order, or memory address. A singleton is its operand.

Renormalize after substitution or changes to proof evidence; cached reductions must validate their proof dependencies. This is a canonical form under the permitted rules, not a general semantic-equivalence test. Keep input Loan dependencies separately: simplifying an Origin expression never removes a distinct input's Loan.

##### 15.2.3. `static` and `Owned`

```kimi
func empty() -> ref/string from static
```

A shared borrow from `static` has no non-static lifetime dependency and must satisfy the [static-source rules](#1132-static-storage). A new safe borrow of mutable static storage has a finite Origin. Safe code cannot derive `uniq/T from static` from longevity alone: an exclusive borrow also requires a unique Loan anchor. An abstract Origin whose Loan requirement is `uniq` cannot be bound to `static` in safe code.

`static` describes an Origin. `Owned` expresses independence from non-static lifetime dependencies, not ownership Semantics or permission to allocate storage:

```kimi
func register<F>(f: F)
    F is Owned
```

A valid complete Type T is `Owned` exactly when every Origin in **OwnedOrigins(T)** equals static; an empty set satisfies the condition. OwnedOrigins is the conservative dependency closure of the Type's outer Origin, its Semantics target (value referent, object payload or View Target, or raw-pointer pointee Type), all instantiated Type and Origin arguments (including unused slots), bases, stored Fields, enum payloads, Tuple components, array elements, and concrete Closure captures. Expand aliases and substitute declaration bindings before traversal. Recursive Types use the structural fixed-point rules, not circular conformance evidence. A base or runtime-contract view contributes its visible Type/Origin arguments; its hidden payload was certified at erasure (§15.8.1).

Callable Types contribute every fixed Origin in their complete Type: a Function Item's bound generic and Origin arguments, a concrete Closure's captures and fixed signature Origins, and fixed Origins written in a common Function Type's parameter/result Types. Only Origins bound per call, such as the direct-input quantification of §8.6 and §15.4, are excluded because they have no fixed binding to prove. A common Function Type's hidden environment is certified Owned at erasure. An Owned proof never infers or rewrites a callable's per-call contract.

Use established outlives facts: `a : static` proves a equal to static because static is the maximum Origin. An unbound/unproved abstract Origin yields Unknown, not proof of `not Owned`; resolve required evidence by the ordinary deadline. A generic definition proves Owned for its own Type parameters and abstract Origins only from its declared Constraints and bounds (§8.10); that proof cannot wait for instantiation. Empty containers and unselected Cases do not weaken this Type-level check. In particular, `unsafe/(ref/i32 from local)` and a wrapper with that non-static Type argument cannot prove Owned even if no safe-borrow Field is visible. Traversing a pointee Type neither dereferences a pointer nor creates a Loan; unsafe implementations must still expose their actual lifetime dependencies and uphold pointer validity.

This revision requires Owned at static storage, concrete payload erasure into base/runtime-contract views, common Function Type environment erasure, and explicitly declared Owned Constraints. A lifetime-hiding library API states that requirement explicitly; the compiler does not infer an "indefinite retention" capability from a private body. Ordinary storage and concrete object allocation impose no blanket Owned requirement. Owned never discharges acquisition, Loan, destruction-order, unsafe, or concurrency checks.

#### 15.3. Abstract origins

Functions and types may declare abstract Origin parameters separately from type parameters:

```kimi
func unwrap<T> origin s(v: View<T> from (source => s))
    -> ref/T from s

struct View<T> origin source
    let value: ref/T from source
```

Function Origins are universally quantified. Origin parameters occupy a namespace distinct from type parameters.

An Origin parameter may have one bound, `name : target`, meaning that `name` outlives `target`. The target is `static` or an abstract Origin visible in the declaration, including any binder in the same list; bind the whole list before resolving bounds. A bound is not a default Origin argument. Duplicate binders and unknown targets are errors. Reflexive bounds are redundant; cycles require equal regions under §15.2.2.

```kimi
struct Holder<T> origin stored
    var value: ref/T from stored

func store<T> origin a, b : a(
    holder: uniq/(Holder<T> from (stored => a)),
    value: ref/T from b)
    holder.value = value
```

Here `b : a` permits shortening the stored reference to `a`. The holder receiver's outer Origin is independently inferred as an input Origin; it does not replace the stored Origin `a`.

Bounds are part of the declaration contract for functions and Origin-bearing Types, including enums and Contract method requirements. Check bodies assuming the declared bounds, and prove the substituted bounds at every call or Type use with the limited solver (§15.3.4). Unknown proof is an error by the ordinary finalization deadline; the body cannot infer additional caller requirements. A Type's members and constructors inherit its bounds. Bounds grant no Loan, initialization, or access permission.

Bounds do not distinguish overloads or specialization keys. Contract implementations must admit every Origin binding allowed by the requirement; specializations inherit the original bounds. Container fragments repeat the same bounds (§6.1.2). Preserve bound identities and proof dependencies in artifacts and invalidate affected uses when they change. No standalone outlives clause, bound on an implicit input Origin, or bound declaration inside a Function Type/Callable signature is introduced; use explicit function Origin parameters to relate inputs.

##### 15.3.1. Origin arguments

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

Omitted Origin arguments follow the [position-specific rules](#154-origin-elision-and-return-contracts). Parameter Types and instance Field Types require explicit arguments for Origin-bearing aggregates; local initializers may infer them, and result Types use result-Origin elision. No argument defaults to `static` merely because it appears in a generic Type argument. References to the containing `Self` retain that Type's already-bound abstract Origins and do not introduce a new omitted argument list.

A named Origin argument list may specify only some of the target Type's declared Origins. Resolve names against that declaration; reject unknown names and duplicate bindings, even when duplicate expressions are identical. Apply the enclosing position's omission rule independently to each unspecified argument. Written arguments remain fixed constraints and are neither replaced nor included as additional inputs for result elision. A partial list therefore cannot omit a required argument in a parameter or instance Field Type. The single-Origin shorthand is a complete binding, not a partial list. Binding correspondence follows declaration identity, not list order.

```kimi
// Pair<A, B> declares Origins left and right as above.
func example<A, B> origin a, b(p: Pair<A, B> from (left => a, right => b))
    let local: Pair<A, B> from (left => a) = p
    // Infer right from initialization and ordinary constraints; left stays bound to a.

func invalid<A, B> origin a(p: Pair<A, B> from (left => a))
// Error: parameter Origin argument right must be explicit.
```

##### 15.3.2. Variance

These are static subtype rules under [Type relations and expression operations](#38-type-relations-and-expression-operations). They do not create or authorize a value operation; acquisition and existing Loan obligations must be checked separately.

The compiler infers Origin variance from all occurrences and solves recursive types to a fixed point. Explicit variance annotations are not allowed.

| Position                 | Origin                   | Core         |
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

`uniq/T` remains invariant in `T`. These variance rules do not add ordinary inheritance upcasts or value operations for callable signature matching; use the separately defined [object adaptations](#1357-object-upcasts).

##### 15.3.3. Loan requirements

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

##### 15.3.4. Generic Origin inference

For both ordinary and pair slots, put inference variables at each unresolved Origin position in the complete Type W. Apply variance recursively to nested Types and aggregate mappings. Never turn an explicitly fixed Origin back into an inference variable.

| Position | Constraints and candidate solution |
| --- | --- |
| Covariant | Inputs a and b contribute `a : alpha` and `b : alpha`. Without an equality constraint, use the meet of all upper bounds: their longest common region |
| Invariant | Require proven Origin equality; do not replace invariant inner positions of `uniq/V` with a meet |
| Contravariant | Reverse constraint direction. If one lower bound provably outlives every other lower bound, choose that least upper bound; do not invent a union of incomparable bounds |
| Mixed or cyclic | Combine equality and ordering constraints; require a representable, unique principal solution modulo proven Origin equivalence |

A **principal solution** is the most general permitted solution under the Type's variance and fitting relation. Multiple shorter solutions do not make a covariant inference ambiguous: choose the longest common region. Reject inference when no principal solution is expressible or only incomparable candidates remain; require an explicit annotation.

The initial solver uses equality substitution, reflexivity and transitivity of established outlives facts, and the common-lower-bound laws of `and`. Use the forced equality for invariant positions, the meet of upper bounds for covariant positions, and the comparable-lower-bound rule above for contravariant positions. Substitute candidates into every constraint, including inner dependencies and use requirements. Do not enumerate arbitrary regions or use general theorem proving. An unresolved dependency may be retained only under [deferred-obligation rules](#810-generic-body-checking-and-deferred-obligations).

```text
choose<s/T>(x: s/T, y: s/T) -> s/T
Inputs:      ref/i32 from a, ref/i32 from b
Constraints: a : alpha, b : alpha
Binding:     W = ref/i32 from (a and b)
Result:      W, retaining both input Loan dependencies
```

This example has no additional constraints. Changing input order does not change the solution; an expected result cannot extend a or b. Even equivalent Origin regions retain separate input Loans. Exclusive use still requires a unique Loan anchor and valid acquisition/Reborrow after Origin inference succeeds.

#### 15.4. Origin elision and return contracts

Origin omission depends on the position of the complete Type. These rules apply to `ref`, `uniq`, `objref`, and `objuniq`, after alias expansion and normalization of grouping and redundant owner prefixes. They do not create a safe-borrow Origin for `unsafe/T`.

| Type position | Meaning of an omitted Origin |
| --- | --- |
| Direct borrowed parameter or borrowed receiver | Introduce an independent input Origin for that input's outer borrow layer. |
| Function result | Apply the result-Origin rules below; anonymous whole-result inference retains its existing rules. A named function with no result annotation returns Unit. |
| Local binding with inferred Type | Infer the Type, Origin dependencies, and Loans from the initializer under ordinary acquisition rules. |
| Explicit local Type containing a borrow or Origin-bearing aggregate | Infer omitted Origins and Origin arguments from the declaration initializer and ordinary Origin/Loan constraints. Without an initializer, omission is a compile-time error. |
| Instance Field | Require explicit bindings for all borrow layers and required Type Origin arguments; do not infer the storage contract from initialization. |
| Static Field | Require Owned and §11.3.2's static-source rules. Omitted shared borrow Origins and aggregate Origin arguments with Loan requirement `none` or `ref` default to static; an exclusive borrow layer or `uniq` Loan requirement is rejected. Preserve bound Type/Origin arguments and callable contracts. |
| Generic Type argument or nested value Type | Recursively apply the enclosing position's rule; being a Type argument does not introduce a separate default. |
| Enum Case payload declaration | Apply the instance storage rule to every payload element, including nested Origins and required aggregate arguments; see [enum payloads](#63-enums). |
| Constructor parameter | Apply the ordinary parameter rule; the constructed value retains the containing Type's declared Origin contract under [construction](#623-constructors). |
| Property accessor | Complete actual getter results and setter inputs independently by function elision (§11.3). Stored custom signatures must then match complete storage T. Computed/required signatures have no shared storage contract. |
| Getter result, explicit or defaulted from P | Apply function result elision with the actual getter receiver; preserve already bound dependencies (§11.3). |
| Adaptation Target | Infer result Origins from the operand, operation, and constraints under [Adaptation Targets](#1351-forms-and-adaptation-targets); this is not signature result elision. |
| Callable constraint signature | Apply its limited per-call direct-input quantification and result restrictions under [Callable constraints](#86-callable-constraints), rather than recursively quantifying every nested borrow. |

An implicit input Origin is universally quantified and supplied by the caller’s argument/receiver, not the parameter variable’s lexical lifetime. Direct borrowed inputs introduce independent Origins unless explicitly related; only those outer Origins participate in result elision. Inner borrow and aggregate/generic argument Origins must be explicit and introduce no extra implicit inputs. Bound generic Types and Self preserve their dependencies. Expected callable signatures and [Callable input quantification](#86-callable-constraints) follow their own rules; no general higher-ranked Origins are added.

For locals, inference means satisfying ordinary subtyping, variance, outlives, and Loan constraints, not requiring literal equality with the initializer's Origin. Permitted shortening remains available. The declaration fixes the local Type and its Origin constraints; later assignments must satisfy that contract and cannot extend a source lifetime or erase a retained Loan dependency. Explicit Origin annotations remain constraints on the initializer and all subsequent assignments.

Local Origin omission requires an initializer, but inference may still fail. Bind each omission to a fixed inference variable and initializer-derived constraints at declaration; later assignment cannot supply a missing annotation. Later uses constrain those variables without reopening Type inference. Region/Loan constraints may remain symbolic during analysis; generic obligations may await permitted substitution/instantiation.

Resolve non-generic omissions before completing body Origin/Loan analysis, and generic obligations before instantiation finalization. Failure to determine or validate the contract by its deadline is a compile-time error requiring an annotation or corrected constraints. Equivalent valid region solutions need not identify one unique point set. Never replace uncertainty with static, an invented abstract Origin, or erased dependencies; static defaults only where elision explicitly allows it.

Instance storage exposes the containing Type's declared Origin contract, including dependencies already bound within complete generic Type arguments. Bind directly written borrow dependencies to declared abstract Origins or explicitly to `static` where valid. An exclusive borrow still needs a unique Loan anchor. Initializers and constructors satisfy this contract; they do not infer it.

```kimi
struct Box<T>
    let value: T
// Box<ref/i32 from a> retains a through its complete Type argument.
// No synthetic named Origin parameter is added to Box.
```

The constructed Box's lifetime, acquisition, and destruction retain that dependency. This does not permit a directly written field `value: ref/i32` to omit its Origin. Static storage can retain the Box when its complete Type proves Owned and its values satisfy §11.3.2. Finite layout, Copy derivation, and Object payload erasure remain separate checks.

**Ordinary storage.** Array, Dictionary, Tuple, fixed array, struct, enum, concrete object payloads (`obj`/`rc`/`arc`), and concrete Closure environments accept valid complete stored Types without a blanket Owned or Storable requirement. Preserve Type/Origin arguments and value-level Loan identities, anchors, and Reborrow relationships through acquisition, storage, Move/Copy, calls, and destruction. Conservative OwnedOrigins checks include Type-level dependencies that create no actual Loan; actual Loans still require provenance. Heap placement neither extends a referent's lifetime nor changes these rules. Loan liveness follows required uses and observable destruction (§15.6), not merely the enclosing lexical scope.

For example, `func singleton<T>(value: T) -> Array<T>` with body `return [value]` is valid without Owned or Copy: acquire T once and propagate its complete dependencies. The same applies to a body-local Array even when no Array appears in the public signature. Verify all admitted Types at definition time under §8.10; representation obligations cannot hide new capability requirements. A copied shared-reference element retains its original referent's lifetime, while a borrow of the element slot is also bounded by Array storage (§4.6.6).

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
    let number: i32 = 1
    var counter: i32 = 0
    let value: ref/i32 = Global.number@ref    // Omitted Origin is static; immutable source.
    let invalid: ref/i32 = Global.counter@ref // Error: a mutable static source has a finite Origin.
```

When a result Origin is omitted, the compiler applies these rules in order:

1. If the complete result Type contains no borrow or required Origin argument, including dependencies retained through aggregate fields, elements, and generic arguments, no result-Origin constraint is generated. An owned outer Semantics does not make an Origin-bearing aggregate borrow-free.
2. If there are directly borrowed parameters, each omitted result Origin becomes the meet of all their Origins.
3. Otherwise, an omitted shared result Origin is `static`; an omitted aggregate Origin argument likewise becomes `static` only if its inferred Loan requirement is `none` or `ref`. If that would create an exclusive static borrow or bind a `uniq` Loan requirement to `static`, an explicit valid Origin is required.

Apply these rules independently to each omitted borrow-layer Origin and required aggregate Origin argument, including unspecified entries of a partial named list. Preserve explicit bindings and already-bound generic dependencies. Check the completed Type's outlives, variance, and Loan requirements; elision is not permission to weaken an invariant position or bind an exclusive Loan requirement to `static`. No result Origin is inferred from a named function's body.

Examples:

```kimi
func first(x: ref/T) -> ref/T
// result Origin: x

func choose(x: ref/T, y: ref/T) -> ref/T
// result Origin: x and y

func empty() -> ref/string
// result Origin: static

func view<T>(x: ref/T) -> View<T>
// View<T> declares Origin source: result is View<T> from (source => x).
// Its owned outer Type does not suppress the retained borrow dependency.

func pair<A, B>(x: ref/A, y: ref/B) -> Pair<A, B> from (left => x)
// left remains x; omitted right becomes x and y.
```

Only direct borrowed parameters participate in rule 2. Origins nested in aggregate inputs must be selected explicitly:

```kimi
func get<T> origin s(v: View<T> from (source => s)) -> ref/T from v.source
```

An explicit `from` clause overrides elision only for the borrow layer or named Origin arguments it binds; omitted bindings elsewhere still follow the rules above. Thus this result depends on `self`, not on the conservative meet `self and key`:

```kimi
func lookup(self: ref/Self, key: ref/Key)
    -> ref/V from self
```

Whole-result inference for anonymous functions also infers environment-derived Origins and Loans under [Closure result rules](#1582-closure-dependencies-and-call-results); an explicit annotation retains the elision above.

##### 15.4.1. Return contracts

A declared return Origin limits the dependency visible to callers without requiring a borrow from that specific input. Every explicit or implicit result, including unreachable ones, must subtype the declared result Type under [result validation](#149-result-validation) and [reachability](#1492-reachability).

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

#### 15.5. Exclusive origins

An exclusive borrow requires both a valid Origin and a unique Loan anchor. An Origin proves longevity but not uniqueness.

A shared borrow may be returned from a stored Origin:

The following independent member-signature excerpts are written inside `View<T> origin source`; bodies are omitted to focus on Origin and Loan contracts.

```kimi
struct View<T> origin source
    func get(self: ref/Self)
        -> ref/T from self.source
```

Returning `uniq/T from self.source` from `self: uniq/Self` is invalid because detaching the result from the current `self` Loan could allow a second exclusive borrow:

```kimi
struct View<T> origin source
    func bad(self: uniq/Self)
        -> uniq/T from self.source       // Error
```

One valid form consumes the Origin-bearing owner:

```kimi
struct View<T> origin source
    func into_uniq(self: Self)
        -> uniq/T from self.source
```

Moving `self` prevents reuse of the capability.

Alternatively, reborrow through the current exclusive receiver:

```kimi
struct View<T> origin source
    func get_uniq(self: uniq/Self)
        -> uniq/T from self
```

The parent Loan remains active, and access through it is suspended, while the returned reborrow is live.

#### 15.6. Borrow checking

Function bodies are lowered to a control-flow graph. A **program point** is a position immediately before or after an operation. A [Place](#34-values-places-and-storage) does not by itself grant write permission. The lowered representation uses these projections:

```text
place := local
       | place '.' FieldIdentity
       | place '.base' BaseIdentity
       | place '.' TupleIndex
       | '*' place
       | place '[' _ ']'
```

These projections describe direct Field and lowered storage Places. Base/Field identities preserve inherited paths; no ordinary base-reference conversion is implied. Custom/computed/required accessors instead use function boundaries (§11). Parentheses preserve Places. Reading Copies, Moves, or borrows according to context and permissions.

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

##### 15.6.1. Constraints

Type checking generates these constraints:

| Constraint      | Rule                                                         |
| --------------- | ------------------------------------------------------------ |
| Subtyping       | Assignment and argument passing require `type(value) <: type(destination)`. |
| Liveness        | If a value containing `o` may be used after `P`, then `P` belongs to `region(o)`. |
| Outlives        | `a : b` requires `region(a) ⊇ region(b)`.                    |
| Well-formedness | Every Origin in `T` observable through `ref/T from o` or `uniq/T from o` must outlive `o`. |
| Calls           | Origin arguments and result Loan requirements are instantiated as described under Calls and Origin propagation. |

The well-formedness rule prevents borrowed contents from expiring before the outer borrow.

##### 15.6.2. Place overlap and conflicts

Two places overlap when an operation on one may affect the other. Static place analysis uses only these structural rules for proving non-overlap:

| Places | Result |
| --- | --- |
| Identical place, or a place and an inline subpart | Overlap |
| Independent local roots and their inline parts | Disjoint |
| Distinct inline stored fields, Tuple elements, or different constant fixed-array indices of one aggregate, and their subparts | Disjoint |
| Referents of simultaneously live valid `uniq`/`objuniq` borrows with distinct Loan anchors | Disjoint by exclusivity |
| Other reference dereferences | Follow Loan provenance and apply these rules |
| Anything not decided above | Non-overlap unproven; reject operations requiring proof |

Inline parts exclude pointer/reference referents. Distinct shared-reference or raw-pointer variables alone do not prove independence. Constant fixed-array indices use only the [ConstantIndexExpression rule](#1513-move-paths-and-partial-move), comparing decoded in-range literal values, not general constant evaluation or optimization; runtime index comparisons such as `i != j` do not establish disjointness. No arbitrary integer proof or optimizer result changes acceptance. Array-derived Slices retain the whole-array Loan footprint through reslicing, splitting, and empty views under [Slice lifetime rules](#465-slice-storage-lifetime-and-permissions). Simultaneous exclusive borrows may be used only through their valid access paths; reborrowing still suspends conflicting parent access.

These are storage rules, not permission to bypass Property accessors. Direct Field operations may borrow disjoint fields separately; custom/computed/required calls retain their receiver footprint.

Each operation is checked against every active Loan on an overlapping place:

| Operation               | Existing `ref` | Existing `uniq` |
| ----------------------- | -------------- | --------------- |
| Read                    | Allowed        | Forbidden       |
| Write or move           | Forbidden      | Forbidden       |
| Create `ref`            | Allowed        | Forbidden       |
| Create `uniq`           | Forbidden      | Forbidden       |
| Destroy the borrowed place | Forbidden      | Forbidden       |

This enforces shared aliasing or mutation, but never both simultaneously.

##### 15.6.3. Reborrowing

Borrowing through an exclusive borrow creates a child Loan. While the child is live, the parent remains live but access through it is suspended. Overlapping access is rejected by the normal conflict rules.

**Basic example.**

```kimi
func bump(n: uniq/i32)

var v = 0
bump(v@uniq)
bump(v@uniq)
```

Each call creates a temporary reborrow; the first ends before the second starts.

##### 15.6.4. Calls and origin propagation

For a call, the compiler:

1. creates fresh regions for the callee's abstract Origins;
2. instantiates parameter types and checks argument subtyping;
3. proves the substituted declared outlives bounds against caller facts (§15.3), rather than assuming them;
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

While the returned `Pair` is live, shared Loans on both `a` and `b` remain active. A dependency requiring `uniq` propagates an exclusive Loan. The spelling `static` alone creates no parameter-root Loan; it does not erase the storage anchors and conflicts of a borrow into static Field storage.

A call's receiver and argument Loans begin as each borrow/reborrow is formed in evaluation order, before later arguments and defaults. In particular, an exclusive receiver is active while explicit arguments are evaluated. No two-phase reservation exception is defined; intrinsic Exchange/Swap use the same rule.

**Static call effects.** Summarize each callable's potentially accessed static Field Identities and read, shared/exclusive borrow, write, replacement, and destruction effects, including callees, defaults, lazy initialization, and cleanup. Compare them with active caller Loans by normal overlap rules. Borrowed results retain Field anchors and dependency paths: immutable-source borrows may be static, whereas mutable-source borrows must retain a finite Origin under §11.3.2. Static allocation never permits replacing/destroying a borrowed current value.

Summaries distinguish first-access initialization effects from ordinary accesses. A live Loan anchored to a Field proves that Field has completed initialization, so its initializer need not be counted again; this proves nothing about an unrelated Field first accessed by the callee. If a result may derive from several static Fields, retain every possible anchor conservatively, independently of the runtime branch selected.

If `let view = State.text@ref` borrows a mutable static string Field, view has a finite Origin; reject State.reset() while view has a later use if reset may replace that Field. A shared Loan still permits read-only calls. Summarize the whole call conservatively; favorable runtime branches need no special analysis. Compute recursive fixed points before acceptance. Separate/indirect calls consume published validated summaries, or conservatively treat unknown effects as conflicting with every potentially affected active static Loan. Clients need not inspect private bodies. Preserve immutable anchors for shutdown dependencies even after erasure; mutable-source borrows cannot cross an Owned boundary. FFI validity and aliasing obligations still apply.

Retain these static/capture anchors when composing [receiver-preservation effects](#12442-effect-verification), even when self is not an explicit argument. The same call may affect multiple roots. Published summaries and their dependencies follow §18.3 and §21.3.4.

##### 15.6.5. Universal regions

Every Origin in a function signature is universally quantified. The implementation must work for every legal caller instantiation, so a local region cannot be widened to satisfy a universal return Origin:

**Error example.**

```kimi
func bad(x: ref/T) -> ref/T from x
    let local = T.new()
    return ref/local       // Error
```

The local value cannot satisfy the universal return Origin `x`; returning its borrow is a compile-time error.

##### 15.6.6. Destruction lifetime checking

The [Destruction rules](#163-aggregate-destruction-and-deinit) and [Scope Exit](#162-scope-exit-destruction) determine responsibility and order. Destruction lifetime checking applies to every Origin/Loan that Destruction may observe and requires validity at each such observation.

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

#### 15.7. Initialization-preserving exchange

| Operation | Old value | Placement | Result |
| --- | --- | --- | --- |
| Initialization | None | Fill empty storage | Unit for assignment |
| Replacement (`=`) | Destroy remaining old parts | Place after destruction | Unit |
| `Exchange` | Transfer without destruction | Keep target initialized | Old value |
| `Swap` | Exchange both values without destruction | Keep both initialized | Unit |

`Exchange(place, with: value)` and `Swap(placeA, placeB)` denote language-provided intrinsic exchange operations. Their semantic requirements are defined here; final API spellings and resolution remain separate. In examples, `place` denotes **authorized direct storage access**, not permission to bypass a Property getter or expose its private storage. Getter-result acquisition grants no access to backing storage.

##### 15.7.1. Evaluation and transfer

- Each target is Initialized and permits exclusive writing. Values have identical complete Types, including Origins; conversions finish before exchange begins.
- Evaluate targets and arguments left to right. A target's exclusive Loan begins when its borrow argument is formed and remains active during later argument evaluation and exchange, just as for an ordinary exclusive receiver call. There is no reservation or delayed-activation exception.
- Exchange itself runs no user code, Destruction, Abort-producing work, or control transfer. Internal empty states cannot be observed by the program. This does not guarantee inter-thread atomicity.
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

##### 15.7.2. Static non-overlap

Use the structural [place analysis](#1562-place-overlap-and-conflicts). Distinct independent roots, distinct inline fields/Tuple elements/constant fixed-array indices, and valid simultaneous exclusive borrows with distinct Loan anchors can prove non-overlap. Identical or containing places overlap. Follow Loan provenance for other dereferences; different raw pointers or shared-reference variables alone prove nothing.

```text
Function parameters: a: uniq/T, b: uniq/T
Swap(referent(a), referent(b)) // Conceptual storage notation: distinct live anchors.
```

Unknown relationships are rejected. Do not accept `Swap(a[i], a[j])` merely from `i != j`, arbitrary integer facts, or optimization. This bounds the required proof and the accepted programs, not just compiler effort.

#### 15.8. Captured and erased dependencies

##### 15.8.1. Object payload erasure

Erasing a concrete payload behind a base or runtime-contract view requires its complete data Type to satisfy `Owned` under §15.2.3's conservative OwnedOrigins closure; the handle's own Origin is not this test. Resolve payload Type/Origin arguments and ordinary exclusive-Loan restrictions before erasure. A local object borrow may still have a local Origin when its payload is Owned.

```text
Dog owns only i32/string data -> Owned payload -> base/contract erasure allowed
Dog stores a local ref       -> non-Owned     -> initial erasure rejected
objref/Animal from local     -> borrow remains local even when payload is Owned
```

This is not a blanket `from static` requirement on object handles or exact concrete views. Same-target operations retain existing lifetime rules. An erased view certifies that this check succeeded; later upcasts/casts inherit it without runtime Origin queries. Only proof-covered fixed Origin bindings may be supplied as static in a checked cast (§13.6.2); per-call callable Origins and the handle's outer Origin are not reconstructed. Existing borrowed-field Types remain valid; hiding their non-static dependencies needs a later existential-view design. Owned does not waive pointer validity, Loan, destruction, or concurrency checks.

##### 15.8.2. Closure dependencies and call results

A Closure recursively retains every captured value's Origin and Loan dependencies, not merely the lifetime of its creation Block. Moving an owned value with no borrowed contents does not borrow its old local storage. Copying a shared reference, moving an exclusive reference, reborrowing, or acquiring a borrowed aggregate preserves the corresponding external Origins, child Loans, and parent restrictions. Do not collapse independent dependencies or discard them at generic substitution or type erasure. Apply §15.2.3's OwnedOrigins to the captured Types when proving the environment Owned.

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
let get = func [text] () => text@ref
// Internal signature: call(self: ref/Self) -> ref/string from self.
let view = get()
let moved = get // Error if view is still used below.
inspectText(view)

let other = makeText()
let invalid = func [other] () -> ref/string => other@ref
// Error: annotated omitted result Origin is static, not the environment borrow.
```

Join multiple results under normal result validation, retaining all possible Loan dependencies when taking an Origin meet. Reject invariant mismatch, cycles, and unexpressible dependencies rather than weakening them. A repeatable exclusive result keeps its call Loan until needed uses end, preventing conflicting reentry. No result may outlive call-local storage or borrow an environment consumed by that call; moving an external reference value out is distinct and may be valid.

The internal call contract is preserved for concrete generic use; callers do not inspect bodies to rediscover lifetimes. Common Function Types and Callable constraints may return input-dependent borrows, but cannot expose hidden-receiver-dependent results (§8.6).

##### 15.8.3. Escape and retention

The Owned guarantee grants no thread-transfer or concurrent-access capability; see the [concurrency design boundary](#d2-concurrency-memory-model-and-thread-transfer).

Escape describes retention across a creation/call boundary, not a permanent syntactic Closure category. Check returned values, saved fields, other Closures, and indirect callees against destination lifetime and capability contracts. Borrow capture is not inherently non-escaping, and Move capture does not remove nested borrow dependencies. Moving or heap-allocating a Closure cannot extend a local referent's lifetime. Temporaries retain their original expiration.

Non-escaping means that the callee retains neither the callable nor its environment dependencies beyond the call. It does not mean one call, no allocation, or waived Loan checks. `ref/F` and an Owned result let a verified callback-only body use a borrowed concrete environment without erasure, but neither `ref/F` nor Callable alone promises that every environment-derived value is unsavable. A separately compiled callee cannot be assumed non-escaping without a verified contract. Non-escaping declaration syntax and borrowed erased callable views remain deferred.

Loan validity includes later uses, results, and dependencies observed by destruction, not just the last body invocation or the lexical end of a binding. Concrete Closure storage follows §15.4; the Owned boundaries are listed in §15.2.3.

#### 15.9. Lifetime design boundaries

This revision does not define:

- abstract Origin parameters on contracts or trait-like abstractions (the [Property getter receiver/result contracts](#1122-computed-properties) do not introduce contract-level Origin parameters);
- existential object views that hide non-static payload dependencies;
- general higher-ranked Origins beyond the direct-input quantification of Callable constraints;
- Origin expressions naming static Places, such as a function result bounded by a mutable static Field; use direct access, an input-bounded result (§11.3.2), a scoped callback, or immutable static storage instead;
- lending iterators;
- cancellation cleanup guarantees.

These features require extensions to the [Ownership and Origin rules](#15-ownership-and-lifetime-analysis) and must not be inferred from this revision.

### 16. Scope exit and destruction

Scope exit secures results and performs cleanup. A Deferred Block registers code for scope exit.

#### 16.1. Deferred blocks

A **Deferred Block** registers cleanup when execution reaches `defer`. Registration evaluates none of its body, arguments, conditions, or initializers. It uses the common Body forms and has no expression result; body expression use/discard follows §14.2.

A registration belongs to its directly containing executable body scope, including a function, branch, arm, current iteration, do, unsafe, require failure, or executing defer body. Unreached registrations do not run; each iteration registers and cleans up independently. Registrations cannot be cancelled or manually invoked.

**Basic example.**

```kimi
func process(flag: bool)
    defer => log("function end")
    if flag
        defer => log("branch end")
        work()
    log("after branch")
```

For true `flag`, output is `branch end`, `after branch`, then `function end`. Deferred execution and automatic destruction share the [Scope Exit ordering](#162-scope-exit-destruction).

##### 16.1.1. Deferred control boundary

Each Deferred Block establishes a lookup barrier that no outward transfer may cross. It accepts self-targeted exit with an omitted or Unit-fitting operand, including through nested selections, do expressions, unsafe statements, and require failure bodies. An omitted operand means `()`.

An unlabeled `exit` targets the nearest Iteration Construct or Deferred Block. Consequently, exits and continues of an inner loop retain their normal meaning, as do results of inner selections and do expressions. A `return` to an outer function, a named transfer to an outer construct, or a `yield` to an outer selection is an error. A separate nested function retains its own Function Boundary and normal returns.

```kimi
defer
    defer => log("cleanup body end")
    if alreadyClosed()
        exit // Ends this body; its nested defer and remaining outer cleanup still run.
    close()

defer
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
defer
    exit () // Valid: the operand fits the deferred target's Unit result.
```

##### 16.1.2. Deferred evaluation and ownership

Resolve Names and Effective Types at the registration's lexical position; later declarations or refinement do not reinterpret the body. Registration neither Copies nor Moves referenced locals and creates no closure value. Execution accesses the then-current bindings.

```kimi
var count: i32 = 1
let saved = count
defer => log(saved) // Explicit snapshot: prints 1.
defer => log(count) // Reads at execution: prints 2 first.
count = 2
```

Check initialization, Copy/Move, Loan, Origin, and destruction responsibility on every applicable exit path, including deferred uses in borrow lifetimes. Registration alone does not borrow all referenced values. Later operations are allowed if they remain compatible with the eventual cleanup.

```kimi
// Resource is non-Copy; inspect borrows, consume moves.
let resource = makeResource()
defer => inspect(resource)
consume(resource) // Error: cleanup would access a moved value.

let other = makeResource()
defer => inspect(other)
defer => consume(other) // Error: executes before inspect and moves its input.
```

A valid Move during cleanup removes subsequent automatic destruction responsibility. Raw pointer operations retain their programmer-managed obligations; `defer` does not repair double destruction or extend raw pointer validity. Secured result borrows must remain valid after all cleanup.

##### 16.1.3. Nested and unsafe cleanup

An inner `defer` registers while its enclosing Deferred Block executes and runs when that body exits, before the original scope's remaining cleanup. It cannot add a registration to an outer scope already being exited.

```kimi
defer
    defer => log("inner end")
    log("outer body") // Prints before inner end.

defer => unsafe => releaseRaw(pointer) // Runs at the directly containing scope's exit.
unsafe
    defer => releaseRaw(other)      // Runs at this Unsafe Block's exit.
    useRaw(other)
```

A Deferred Block does not itself grant unsafe permission. Permission follows the operation's lexical context, never its later caller, and does not cross Function Boundaries. Safety conditions must hold when the delayed operation executes.

A defer immediately followed by scope exit is valid; this alone requires no warning or automatic replacement with a call. In `if shouldClose => defer => close(resource)`, cleanup runs at the end of the if body. To register for an outer scope, put defer there and test the condition inside it; save the condition first if registration-time truth is intended.

~~~kimi
let closeAtEnd = shouldClose
defer
    if closeAtEnd => close(resource)
use(resource)
~~~

#### 16.2. Scope-exit destruction

**Scope Exit** combines registered Deferred Blocks and automatic destruction. It applies both to ordinary scope completion and to scopes left by `return`, `exit`, `continue`, or `yield`.

Ownership, temporary-lifetime, and construct-lifetime rules determine each value's owning scope and destruction point. These rules also govern temporaries, `for` iterables and iterators, iteration bindings, `match` subjects, and owned function parameters. A transfer uses those scopes to determine what it leaves.

##### 16.2.1. Cleanup order

Clean departing scopes from inner to outer, finishing each before the next. Within a scope, process local declarations and `defer` statements in reverse combined lexical order. Later initialization or reassignment does not change a binding's position.

Execute only registered defers and destroy only initialized values whose destruction responsibility remains with the scope. Skip moved or destroyed values. Last use does not remove destruction responsibility.

```kimi
let first = makeResource("first")
defer => log("A")
let second = makeResource("second")
defer => log("B")
```

Cleanup order is `B`, destruction of `second`, `A`, destruction of `first`. Deferred Blocks therefore run in reverse registration order.

Bindings introduced at scope entry, such as parameters, `self`, iteration bindings, and pattern bindings, precede the body's statements. Explicit bindings in one parameter list or pattern are ordered left to right and destroyed in reverse order. Explicit `self` follows its written position; individual construct specifications place implicit bindings. These positions do not confer ownership or change the bindings' owning scopes.

```kimi
func process(first: Resource, second: Resource)
    defer => log("end")
```

If both parameters retain owned values, cleanup is `end`, destruction of `second`, then destruction of `first`. Borrowed bindings do not cause destruction of their pointees. Similarly, a defer inspecting an iteration binding runs before that binding's remaining owned value is destroyed.

##### 16.2.2. Results and transfers

For a transfer result or a single-item expression used as its owner's result:

1. Evaluate the operand.
2. Secure the result using normal Copy / Move rules.
3. Destroy temporaries whose normal lifetime ends at completion of that result expression.
4. Run Deferred Blocks and automatic destruction in departing scopes, in the common cleanup order.
5. Deliver the secured result and complete the target's termination or continuation.

Other temporaries follow normal lifetime scopes and positions; absence from the result does not justify earlier destruction. Omitted return/exit/yield operands supply Unit; continue has no result. If an operand triggers another transfer, process that transfer instead. A target result discarded after delivery follows normal destruction; directly discarded body expressions keep their ordinary temporary lifetime.

A moved result is not destroyed again at its source; a copied result leaves the source's destruction responsibility intact. Deferred execution cannot replace the secured result, although ordinary effects on shared objects remain possible.

```kimi
func answer() -> i32
    var value: i32 = 1
    defer => value = 2
    return value // Returns the already copied 1.

func take() -> Resource
    let resource = makeResource()
    defer => inspect(resource) // inspect borrows; Resource is non-Copy.
    return resource // Error: cleanup would access the moved source.
```

Clean only scopes actually left. `continue` cleans the current iteration's departing scopes and retains outer scopes needed for continuation. Named transfers clean all intervening scopes they leave. Exiting a Deferred Block completes its nested cleanup, then resumes pending outer cleanup.

Normal ownership, borrowing, and [Destruction lifetime checking](#1566-destruction-lifetime-checking) apply throughout cleanup. Securing a result first does not permit a borrow of a destroyed local to escape. For partial initialization or Partial Move, apply [field cleanup](#1632-field-cleanup) to parts with remaining responsibility rather than skipping the whole aggregate. Raw pointer access does not guarantee automatic tracking of the original owner's destruction responsibility.

##### 16.2.3. Completion and abnormal termination

Consume a Deferred Block's registration when its execution starts. Each registration executes once if cleanup reaches it; an inner defer registers in the executing body's own scope, never in an outer scope already being exited.

Deliver a pending transfer or result only after all required cleanup completes normally. Nonterminating cleanup prevents remaining cleanup and delivery; general termination proofs are not required.

Forced process termination and undefined behavior provide no cleanup guarantee. Abort skips or aborts cleanup under [Abort Termination](#173-abort-termination). Cancellation, if introduced, requires separate common rules for Deferred Blocks, destruction, and secured results; this specification provides no cleanup guarantee for it.

#### 16.3. Aggregate destruction and deinit

`deinit` may be declared only directly in a structure body, including a fragment produced by a Mod. It is invalid in a group, enum, contract, extension, constructor, function, accessor, or another `deinit`. After conditional selection and merging, each concrete structure has at most one such declaration. A common Body (§14.2) is required; `deinit => ()` or an indented `()` is an explicit no-op body. A no-op body still counts as user-defined `deinit` for Copy and Partial Move restrictions.

`deinit` accepts no parameters, generics, Origins, result annotation, or modifiers. Unavailable modifiers follow §2.5.1. Its single-item expression is discarded; explicit return must fit Unit. Unsafe operations need an inner Unsafe Block. Automatic component cleanup applies without deinit. Each derived layer may define its own body; machinery processes each layer separately. Legal owners need no private access to invoke mandatory destruction, and access modifiers cannot suppress it.

##### 16.3.1. Special receiver

`deinit` is a dedicated destruction declaration with no parameters or explicit result Type. Only Destruction machinery invokes it. Explicit calls, indirect calls, and obtaining its function value are compile-time errors; user-callable finishing work requires a separate API.

Its `self` has exclusive access equivalent to `uniq/Self` for access/Loan checks, but is a special Destruction receiver, not an ordinary borrow value or a second owner. It cannot be Copied or Moved.

The whole receiver cannot be an assignment, Exchange, or Swap target, or be passed as ordinary `uniq/Self`. This includes methods and setters taking the whole receiver exclusively. Whole-self shared borrowing and direct operations/borrows on initialized fields remain subject to ordinary permissions and Partial Move restrictions. No method or borrow may bypass these rules.

A Destruction receiver cannot become an owning/counting handle, be stored for later use, or escape. Helper borrows must end before their storage is destroyed. During layer D’s destruction, Self is D; cleaned derived layers are unavailable. Prove nonescape and compliance with the [object lifetime restrictions](#164-closure-and-object-lifetime-boundaries) from ownership/call effects; reject unknown callees that could violate them. Runtime counts and unchecked assertions cannot replace the proof.

```text
During deinit (conceptual storage operations):
    Exchange(self, replacement)       // Error: whole-self replacement.
    self.reset()                     // Error if reset requires uniq/Self.
    observe(sharedBorrow(field))     // Allowed for an initialized field.
    Exchange(field, with: newValue)   // Allowed with authorized field access.
```

##### 16.3.2. Field cleanup

Destroy a complete struct layer by running its own deinit, completing that body’s Scope Exit, then destroying own Fields in reverse **logical** declaration order and finally the direct base recursively. Fields/base are complete and Initialized when deinit starts. Normal return does not skip them. Computed Properties add no components; cleanup invokes no accessors. Splitting, generated declarations, and physical layout cannot change this order except through defined logical ordering.

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

Tuple elements and array elements are destroyed in decreasing element-index order, from the last logical element to index zero. This includes fixed-length arrays, initialized elements of a partly built array, and owning array storage used by array literals; spare capacity is not an initialized element. Recurse into a partially initialized element before continuing to the preceding element. Unit and empty arrays have no components to destroy.

Enum values destroy only the active Case's remaining initialized payloads in reverse declaration order. Inactive Cases and payloads moved out by [selected match acquisition](#1516-match-acquisition-and-lifetime) have no remaining responsibility. Borrow payloads never destroy their referents. Other library containers must define the destruction order of their owned elements in their own contracts.

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

Destruction lifetime checking applies at every actual observation, including field cleanup. If cleanup reaches a responsibility, execute it exactly once; Move transfers it and prevents double destruction at the source. This guarantee does not promise that every destruction completes when an earlier cleanup diverges, Aborts, or terminates execution. [Abort Termination](#173-abort-termination) remains the sole abnormal-termination policy.

##### 16.3.3. Ownership, object release, and reentry

Destruction claims the target’s remaining responsibility. Until completion, only its special receiver and authorized field operations may observe live parts; it is neither an ordinary owner nor an empty replacement destination. Reject reentrant destruction, whole-value use, and callback replacement. Normal completion removes the responsibility and leaves surviving storage Uninitialized; its owning operation may then install the secured replacement. This internal transition provides no source destroy/reset operation and does not reset let initialization history.

Destroy owner/T as exactly T. Destroy obj/T’s object, then release its original storage if required. Destroying rc/T or arc/T releases one strong reference; exactly the zero-count release performs object destruction and storage release, including under atomic arc ownership.

Use the actual owned Type’s complete derived-to-base cleanup, including automatic fields/base and user deinit. Base views retain that dynamic identity and the single destruction responsibility. Release original storage by its original mechanism, never an adjusted view pointer with a base size. No delayed GC finalizer or finalizer thread is implied.

Destroying a non-owning borrow or raw pointer ends that value's capability/lifetime and never destroys its referent. Scalar and other trivial Copy values have no user destruction work; copying them does not create a resource-release obligation. Automatic cleanup, replacement, abandoned construction, temporary expiration, and final object release all use the same recursive rules. If a destructor or component cleanup Aborts or diverges, remaining components, base layers, pending replacement, and allocation release do not run; there is no rollback or second cleanup attempt.

#### 16.4. Closure and object lifetime boundaries

Destroy initialized captures with remaining responsibility in reverse environment-initialization order. Consumed captures are not destroyed twice; remaining values in a Consuming call use normal Scope Exit. Destroying a captured reference does not destroy its referent. Capture-construction failure follows ordinary temporary, partial-initialization, cleanup, and Abort rules without rollback of completed Moves or effects. Retain dependencies observed by captured destructors.

Construction completes base layers before derived fields under constructor rules. Until the complete object is initialized, do not form/publish its ordinary object views or perform runtime dispatch, type tests, or checked casts on it. Having metadata is not proof of completion. Failure cleans only initialized components with remaining responsibility, including completed base layers; do not call `deinit` for an incomplete layer, and do not promise cleanup on Abort.

During destruction, prohibit new ordinary views, runtime dispatch, type tests, checked casts, and resurrection of that object, including through helper calls. An operation remains semantically runtime dispatch even if optimization resolves it to a direct call. Do not acquire a new owning handle or increment its count to revive it. The special destruction receiver retains its authorized direct field operations and shared value borrows, without conversion into ordinary object views. Base cleanup never dispatches back into an already destroyed derived layer. These restrictions concern the object being constructed/destroyed, not independent live objects used by that code.

The shared destruction rules do not promise eventual release on Abort, divergence, forced termination, or an unbroken reference-count cycle. Weak references follow §13.5.9; cycle collection, cyclic-construction factories, and allocator APIs remain separate designs.

### 17. Failure handling

Failure handling determines whether execution continues with an ordinary value or terminates. Abort interacts with cleanup through its explicit termination rules.

#### 17.1. Error policy

Kimigayo represents ordinary failures as values and uses Abort Termination only when normal execution cannot continue. It provides no exception throwing or catching mechanism.

Runtime problems fall into three categories:

| Category | Meaning | Representation |
| --- | --- | --- |
| Recoverable Failure | Expected failure that the caller can handle. | `Option<T>` / `Result<T, E>` |
| Unrecoverable Failure | Normal execution cannot continue. | `$abort(...)` or implicit Abort |
| Warning | Processing can continue successfully, but a condition merits notice. | Diagnostic |

These are not a simple severity ranking: `None` represents expected absence, `Err` an operation failure, Abort process termination, and Warning a diagnostic independent of control flow.

The examples use the defined [enum construction](#632-case-construction-and-resolution) and [Pattern](#1481-patterns) rules. Example APIs are illustrative.

Dedicated generic failure propagation such as `?` remains undefined. The [require statement](#1411-require-statement), including `require condition else => return`, is explicit control flow and performs no automatic Option/Result unwrapping or propagation.

#### 17.2. Absence and failure as values

Use `Option` for a contract representing normal absence and `Result` for a contract representing failure with a reason. The API contract determines the choice, regardless of whether an individual caller uses the reason.

##### 17.2.1. Option

`Option<T>` represents a value or normal absence without an absence reason.

This is the compiler-recognized Core Option declaration, whose identity is fixed in §22.1. The following enum shows its required shape; it is not an instruction to redeclare it in each program.

```kimi
enum Option<T>
    Self is Copy when T is Copy
    Some(T)
    None

func findUser(id: UserId) -> Option<User>
```

```kimi
match findUser(id)
    .Some(let user) => use(user)
    .None => useDefault()
```

##### 17.2.2. Result

`Result<T, E>` represents success or failure with a reason. `E` is an ordinary Type; no exception object hierarchy is required.

This is the compiler-recognized Core Result declaration under §22.1. The following enum shows its required shape. The discarded-Result warning refers to that Symbol Identity, including through aliases, rather than every user Type spelled Result.

```kimi
enum Result<T, E>
    Self is Copy when T is Copy, E is Copy
    Ok(T)
    Err(E)

enum FileError
    Self is Copy
    NotFound
    PermissionDenied
    InvalidData
    IoFailure

func readFile(path: string) -> Result<Data, FileError>
```

```kimi
match readFile(path)
    .Ok(let data) => process(data)
    .Err(FileError.NotFound) => createFile(path)
    .Err(let error) => report(error)
```

Combine the Types when an API distinguishes normal absence from operation failure:

```kimi
func lookupUser(id: UserId) -> Result<Option<User>, LookupError>
```

With this expected Type, `.Ok(.Some(user))` constructs success with a value, `.Ok(.None)` normal absence, and `.Err(error)` a failed lookup operation.

Result uses ordinary [enum Copy derivation](#352-enum-copy). Its conformance requires both complete payload Types to be Copy, independently of the active Case; these premises do not restrict Type formation. `Result<i32, i32>` is Copy, while `Result<i32, string>` remains Non-Copy even when it holds Ok. `Result<Data, FileError>` is Copy only if Data is also Copy. Borrow payloads follow their complete Semantics: shared borrows are Copy and retain their dependencies; exclusive borrows and owning object/counting handles are not Copy. An enclosing handle retains its own formation and acquisition rules. Generic proof follows §8.7: both Proven yield Proven, a valid Refuted operand refutes the conjunction, and remaining unresolved cases are Unknown, never assumed Non-Copy. Invalid Types or evidence remain Error.

##### 17.2.3. Handling, propagation, and discarding

Recoverable failures are ordinary values: they neither throw exceptions nor propagate implicitly. Handle them with ordinary control flow such as `match`, and propagate them explicitly with `return`:

```kimi
func loadSize(path: string) -> Result<usize, FileError>
    return match readFile(path)
        .Ok(let data) => .Ok(data.count)
        .Err(let error) => .Err(error)
```

The return Type describes contractual absence or recoverable failure; a function returning `Result` may still Abort on an invariant violation or unrecoverable condition.

Discarding a Core `Result` expression in Discard Context is allowed but produces a compile-time warning, independently of Copy capability or runtime Ok/Err state. Identify it by its Core Symbol, including equivalent resolved paths. [Warning priority](#174-warnings) selects one report if several apply to the same discard. The warning does not change control flow. A caller can explicitly handle both variants with match to ignore the outcome intentionally. This section defines no special warning for discarding Option.

Returning a recoverable failure follows normal [Scope Exit](#162-scope-exit-destruction) rules, including their requirement that earlier cleanup complete normally before remaining cleanup or result delivery proceeds. Use this path for ordinary failures requiring resource cleanup.

#### 17.3. Abort termination

**Abort Termination** abnormally terminates the entire process executing the program when normal execution cannot continue. Explicit requests and implicit runtime check failures share the termination rules below.

##### 17.3.1. Causes and API contracts

Abort is a termination mechanism, not a classification of causes. It may represent a programming defect, such as an invariant violation or unexpected state, or an unrecoverable external condition, such as allocation failure or an unavailable required runtime resource. Bug classification belongs to diagnostics and does not introduce different control flow.

Checked runtime arithmetic, indexing, and conversions Abort on their defined failures: integer overflow, invalid integer division/remainder, indices or Range boundaries, conversions or shift counts, duplicate Dictionary keys, and missing indexed keys. Each operation defines its invalid inputs; floating-point division by zero follows IEEE 754. Failed Type tests and checked object casts instead follow their Boolean/Option/Result contracts.

The same cause can instead be recoverable under a different API contract:

**Basic example.**

```kimi
func tryAllocate(size: usize) -> Option<Buffer>

func allocateRequired(size: usize) -> Buffer
    return match tryAllocate(size)
        .Some(let buffer) => buffer
        .None => $abort("Required memory could not be allocated")
```

`tryAllocate` returns absence when allocation is unavailable; `allocateRequired` treats that outcome as fatal.

Failure to allocate storage required by common Function Type erasure or [Core object creation](#1358-object-ownership-creation-and-sharing) follows Abort Termination, independently of Copy/Move input acquisition. Core strong-owner duplication also Aborts if its count cannot be incremented without overflow.

##### 17.3.2. Explicit abort and argument evaluation

`$abort(expression)` evaluates its argument once with expected Type string. If it completes normally, use the string as diagnostic information and Abort the process. Otherwise follow its transfer, divergence, or nested Abort without initiating this Abort. Dynamic message-construction failures follow the same rule.

The `$abort(...)` expression has Type Never and never completes normally. Apply the ordinary [Never](#315-unit-and-never-types) and [result validation](#149-result-validation) rules:

```kimi
func requireValue(value: Option<i32>) -> i32
    return match value
        .Some(let x) => x
        .None => $abort("Required value is missing")
```

Abort itself is not a Control Transfer and produces no Completion; distinguish it from a transfer during argument evaluation. The [Evaluation Outcomes](#141-completions) are Completion, Divergence, and Abort Termination.

##### 17.3.3. Termination, diagnostics, and cleanup

Immediate termination applies once Abort Termination begins: do not resume ordinary program execution or perform Scope Exit before terminating the entire process. This rule sets no wall-clock bound on argument evaluation or termination. Abort cannot be caught, recovered from, or resumed, and performs no Stack Unwinding.

Abort does not start Scope Exit processing. If it begins during Scope Exit, abort that processing immediately: do not execute remaining Deferred Blocks, automatic destruction, or the rest of an executing Deferred Block or `deinit`. Completed cleanup effects are not rolled back. Abort pending `return`, `exit`, `continue`, and `yield`; do not deliver secured results or perform additional cleanup to destroy them.

```kimi
func process()
    let resource = makeResource()
    defer => close(resource)
    $abort("Fatal condition")
```

Here, neither the registered `close(resource)` nor scope-exit destruction of `resource` runs.

Abort diagnostics carry a reason and source location: the failed operation for implicit Abort, or the `$abort(...)` call for explicit Abort. A duplicate dictionary key uses the later key expression. The runtime attempts to emit available information; successful or complete output is not guaranteed. The initial Windows format, stderr destination, and failure code 1 are defined in §22.5.4. Failure to emit diagnostics must not prevent termination.

##### 17.3.4. Checks, builds, and constant evaluation

Abort conditions and termination behavior are identical in Debug and Release builds. An implicit check failure is a language-guaranteed termination operation, not merely a rewrite to a replaceable function call. Replacing `$abort` or diagnostic handling cannot make an Abort return normally.

Failures in required compile-time evaluation are compile-time errors. This adds no general constant evaluator: directives use §19, lengths §4, and static index recognition §15.1.3. Recognizing an index literal does not make bounds evaluation mandatory at compile time. A runtime check Aborts only when executed and failed; optimization cannot Abort skipped conditional/short-circuit operations. Unsafe contract violations are not guaranteed to be detected as Abort.

Language-defined static checks, including literal fitting and the exact Dictionary duplicate-key subset in §12.3.4, apply independently of optimization. Outside required constant-evaluation contexts, knowledge obtained only by constant propagation or folding must not turn a specified runtime Abort into a compile-time error. This also applies when the failing value is statically known.

These rules are independent of implementation mechanisms such as a `trap` instruction. APIs returning failures as values use the contracts above; wrapping or saturating integer arithmetic requires separate explicit library APIs.

#### 17.4. Warnings

A Warning is diagnostic information about a condition worth reporting while processing can continue and its result remains usable. Examples include deprecated configuration, ignored optional metadata, fallback encoding, and a failed cache update after the primary operation succeeds.

A Warning does not itself change control flow or implicitly produce `None`, `Err`, or Abort. It is not a third state of `Result`. Return warnings to callers as ordinary values when needed:

```kimi
struct ParseReport<T>
    let value: T
    let warnings: Array<ParseWarning>

func parse(source: string) -> Result<ParseReport<Syntax>, ParseError>
```

This API returns warnings with successful results. To preserve warnings on failure, include them in the error value or an outer report containing the Result.

The following subsections define compiler warnings, not returned API values. They do not change Type fitting, execution, or overload choice. For the same value at the same discard occurrence, emit only the highest applicable warning: **unintended Unit > Symbol-specific > effect-free**. Each warning keeps its own triggering conditions. Distinct arm/body discards remain separate occurrences; unrelated diagnostics are not suppressed. Future Symbol-specific warnings occupy the middle tier and must define any same-tier priority when introduced.

##### 17.4.1. Unintended Unit inference

Warn about a possibly missing result when all of the following hold:

- A value-used construct or return-inferred anonymous function has inferred result Type Unit.
- Unit was not fixed by a declaration, construct rule, or expected Type. Named functions with omitted return Types are excluded from this warning.
- An indented body supplying Unit has structural end arrival and discards a non-Unit, non-Never value at its end.

Inspect the tail through grouping and labels. For if/match/do, descend into structurally normally completing bodies, checking the single expression or last indented item. Inspect the discarded inner values even when the enclosing discarded construct itself has Type Unit. Stop at explicit `()`, transfers, iterations, statements, and separate functions.

```kimi
let total = do
    if useCache => loadCached()
    else => compute()
// If both calls return i32, total is Unit; warn about the discarded tail results.
let corrected = if useCache => loadCached() else => compute()
```

Suggest yield, return, a named exit, or a single-item body as appropriate.

If the two calls in the example return Result, each discarded arm result receives this warning alone, rather than an additional discarded-Result or effect-free warning: supplying the missing result removes the discard itself.

##### 17.4.2. Discarded effect-free values

For a non-Unit expression in Discard Context, warn when evaluation, acquisition, and destruction can be shown to have no observable effect or ownership-state change. This includes functions with fixed Unit returns.

Consider literals, Copy locals, built-in operations/comparisons, Case construction, and Tuples, including their nested operations. Account for calls, user-defined comparisons, Move/Loan effects, destruction, Abort, and possible divergence. Do not inspect callee or destructor bodies to infer purity; if absence of effects cannot be established, omit this warning.

```kimi
func isAdult(age: i32) => age >= 18 // Warning: add -> bool if this is the result.
func answer() => 42               // Warning also with an omitted Unit return Type.
left == right                    // Warning for initialized i32 locals.
if ready => 1                     // Warn on the discarded body value.
func cleanup() => handle.close()  // Do not assume a call is effect-free.
```

Do not warn on the explicit no-op Unit value `()`. Suggest a return annotation or use of the value, without automatically deleting the expression. Defer placement and while-true diagnostics follow §16.1 and §14.9.2.

## Part VI. Programs, compilation, and runtime

### 18. Modules and dependencies

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

#### 18.1. External references and aliases

Only directly referenced Kotonoha libraries are source-addressable by library name. Use qualification such as `ExternalLib.GroupA.StructB` or an explicit `alias` declaration; do not search all external members unqualified. Multiple library versions may use different reference names, with configuration syntax separately specified. Loading transitive metadata for type checking does not expose those libraries by name.

`alias ExternalLib.GroupA` opens a Container's direct members for unqualified lookup:

- Declare it at top level before ordinary declarations or executable code; it applies only to that SourceDocument. Nested aliases are invalid.
- Resolve its Container path from the Compilation root, without other source aliases or default aliases. Check target accessibility at the declaration.
- Introduce direct Types, functions, Fields, computed members, and child Containers in their namespaces; do not recursively introduce descendants. Conditional-member use still requires its published premises.
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

**Type-alias boundary.** Source alias only opens a Container; it neither renames Types nor accepts `alias Name = Type`. Imported Names retain complete Types and Symbol Identity; use qualification to avoid conflicts. Alias expansion/equivalence elsewhere applies to internal transparent references and constrains any future Type-alias feature, without adding a source binding kind or alias-cycle checker. Existing reference/dependency cycle checks still apply.

**Design boundary:** Versioned dependency reference configuration and reference-graph diagnostics remain separately specified. Re-export syntax follows [Re-exports](#182-re-exports).

#### 18.2. Re-exports

**Deferred design:** re-export syntax and artifact representation are undefined; aliases never re-export. Consumers naming an external Type/requirement must directly reference its originating Kotonoha and have an accessible Symbol path. Neither a public Signature containing that Type nor transitive metadata loading supplies Name Reachability.

Any future re-export design must preserve the original Symbol, avoid widening access, and reject cycles without a real target. Its syntax and compatibility requirements belong to that feature's specification.

#### 18.3. Source artifacts and binary interfaces

Portable interchange uses source artifacts or binary interfaces, not serialized Koto internals. Source artifacts preserve source/configuration for reconstruction. Binary interfaces record:

- Symbol/version identity, access/enclosing domains, visibility/public paths, and open/base relationships;
- normalized Signatures, complete API Types/requirements, Constraints, Origins, and unsafe requirements;
- Stored Property Types and standard/custom operation permissions, accessor signatures and Origins, verified witness mappings, conditional-conformance premises, and generic specialization inputs;
- ABI, layout, calling conventions, and target/language/compiler identity.

Private generic dependencies preserve defining Symbols and access context without becoming public Names. These are information categories; encoding, required fields, validation, and compatibility belong to the separate artifact-interface specification.

Callable interfaces additionally preserve concrete environment identities, capture dependencies, internal/public call signatures, receiver acquisition contracts, per-call Origin quantification, and conservative effects/returned storage anchors (§15.6.4). Borrowed-receiver operations publish §12.4.4's completed root summaries, operation Identity, Proven/NotProven status, and diagnostic cause information; no pending ObjectCompatible state is exported. Object interfaces retain defined Supports relationships, verified conformance mappings, Runtime Type Identity, receiver adjustment, and complete destruction/storage-release information. These requirements prescribe no encoding or fixed ABI.

Generic interfaces also retain complete slot/projection and Origin schemas, deferred obligations, and the defining Kotonoha's closed explicit-specialization set and mappings. Preserve the body and dependency information needed for correct implementation selection and code generation under [generic artifacts](#2134-artifacts-verification-and-invalidation).

Record the declaration-content dependencies used by inherited-Name checks, conformance, and public effect guarantees, including completed Container contents when absence of a declaration matters. Existing Symbol/access information can supply these contents; no separate Name-set encoding is required. Accessible Names of open structs and public compatibility guarantees are API facts. Adding a Name, expanding access, or withdrawing Proven can break a consumer, but does not require upstream diagnosis of unknown downstream code. Revalidation and consumer-side diagnostics follow §21.3.4; ordinary Type/Origin/layout obligations remain distinct from a completed public status.

### 19. Compile-time directives

Compile-time directives choose source syntax without runtime branching. The following terms are used in this chapter:

| Term | Meaning |
| --- | --- |
| Condition | A compile-time Boolean expression controlling syntax selection. |
| Prepared environment | Fixed target values and configured compile-time settings available before source selection. |
| Environment selection | A choice fixed by target values and configured Project settings, independently of generic arguments. |

Compile-time Directives select Syntax during compilation without producing runtime control flow:

| Form | Purpose |
| --- | --- |
| `#if` | Independently includes or excludes one Syntax node. |
| `#switch` | Introduces an ordered Case Group and selects one arm. |
| `#case` | Introduces an arm directly inside a `#switch` body. |
| `#Name` | Attaches an Attribute; it is not a Compile-time Directive. |

#### 19.1. Syntax and structural selection

Directive placement is restricted to whole items in these categories:

| Permitted surrounding list | Selected item category |
| --- | --- |
| SourceDocument root | Source declarations, source-local aliases, and executable items allowed there |
| group, rootgroup, struct body | Container items allowed by that particular kind |
| enum body | Enum Cases, functions, Constraints, associated-Type specifications |
| contract body | Requirements, associated-Type declarations, Constraints |
| Executable/function body | Executable items and a function's permitted leading Constraints |

Directives cannot replace part of an expression, Pattern, or Type, or appear directly in runtime match-arm, parameter/argument, generic, accessor, Capture, or Tuple/collection-element lists. An indented executable body nested in an expression remains a permitted item list; a single-item Body cannot directly contain a directive (§14.2). Select a whole allowed item. #case occurs only directly in a #switch directive body. Attributes obey §6.5 and do not expand these permissions.

`#if` controls either the next Syntax node at the same indentation or one indented Block:

**Basic example.**

```kimi
#if windows
alias Kimi.Windows

#if debug
    let logging = true
    let assertions = true
```

A **Case Group** is introduced by `#switch` and consists of the `#case` arms indented one level under it. The group's extent is the `#switch` body; nothing outside that body joins the group, so two Case Groups may appear adjacently. Select the first matching arm in source order. The optional catch-all `#case _` must occur once at most, as the final arm.

```kimi
func useImplementation<T>(value: T) -> ()
    #switch
        #case windows
            useWindowsImplementation(value)
        #case linux
            useLinuxImplementation(value)
        #case _
            useGenericImplementation(value)

    #switch
        #case pointerWidth == 64
            useWidePath(value)
        #case _
            useNarrowPath(value)
```

A `#switch` header has no subject expression. Its body must contain at least one `#case` arm and contains only such arms, apart from blank lines and comments. Each arm must have an indented Block. A `#case` outside the direct arm list of a `#switch` body is an error; nested selections require their own `#switch`. Blank lines and comments do not split a group within its body. Runtime `match subject` retains its separate Pattern syntax (§14.8); `#match` is not a directive or an alias for `#switch`.

A `#switch` construct is one Syntax item and may be controlled as a whole by a preceding `#if`. Directive indentation groups source items for selection; it does not introduce a runtime Block or lookup scope. This applies to both indented `#if` targets and `#case` bodies, in executable bodies and Declaration Containers.

Every valid Case Group must select an arm for the prepared environment. Without `#case _`, at least one explicit Condition must evaluate to **True**; otherwise report an error. No generic dependency can defer selection. A catch-all supplies an unconditional alternative, but does not suppress errors in other Conditions.

Splice selected items into the surrounding list in source order, retaining their SourceDocument and CodeContext. The directive creates no lifetime boundary, cleanup point, defer registration scope, or control-transfer target. A selected `defer` registers in the surrounding executable scope; selected locals live and are destroyed there under ordinary rules. Explicit constructs within selected items retain their own scopes. An early-false `#if` target is excluded. Unselected `#case` arms do not undergo ordinary semantic checking or contribute executable code.

The [nonempty Block rule](#1421-nonempty-executable-blocks) checks source structure before selection. Removing all executable Syntax does not itself make a Block invalid.

Validation of excluded targets follows [Diagnostics and excluded syntax](#195-diagnostics-and-excluded-syntax).

#### 19.2. Environment condition forms

A Condition uses only the following closed expression set over the prepared Compilation environment. The whole expression must have Type `bool`; ordinary operator precedence and explicit parentheses apply.

| Form | Rule |
| --- | --- |
| `true`, `false`, integer and string literals | Integers have Type `i64` and must fit its range; a leading `+` or `-` is permitted only directly on an integer literal. Strings use the ordinary literal rules, without interpolation. |
| Compile-time value Name | A built-in Compilation value or an explicitly configured Project setting. |
| `not E`, `E and E`, `E or E` | Boolean operands and the [Condition evaluation rules](#193-condition-evaluation-and-selection). |
| `E == E`, `E != E` | Matching operand Types: `bool`, `i64`, or `string`. No implicit cross-Type conversion; string comparison is ordinal and case-sensitive. |
| `(E)` | Grouping of one permitted expression. |

All other expression forms are invalid Conditions, including every `is`/`is not` test, calls, runtime member access, indexing, arithmetic, ordering comparisons, conversions, collections, interpolation, and floating-point/character/null literals. Reject them even in short-circuited operands or later Conditions after a selected Case. There are no Type-, Semantics-, Contract-, Origin-, or generic-specialization-dependent directive conditions. This restriction applies in every scope, including function bodies.

**Condition lookup.** Resolve Names only in the disjoint built-in and Project-setting environment established before parsing, using the case-sensitive Name rules of §2.5. Built-ins use the spellings in §20.4: `Windows` does not select `windows`, and `POINTERWIDTH` does not select `pointerWidth`; either is unknown unless independently configured as a Project setting. Ordinary declarations, generic parameters, aliases, Types, Contracts, and runtime values neither supply nor shadow Condition values. A missing Name is an error, not a dependency for generic Binding. Constraints supply no additional Condition values or narrowing facts.

```kimi
windows
windows or linux
os == "windows" or os == "linux"
pointerWidth == 64
debug and featureEnabled // featureEnabled must be a configured bool setting.
```

```kimi
#if T is i32            // Error: Type conditions are not supported.
    ()
#if false and (s is ref) // Error even though false determines truth.
    ()
```

Constraint Clauses and ordinary runtime Type tests retain their separate rules. Environment directives select syntax but introduce no Type or capability assumptions into generic proof.

#### 19.3. Condition evaluation and selection

`#if` and `#switch` Conditions use the same evaluation rules. All valid Condition inputs are fixed by the target and Project settings before source selection. Language evaluation has exactly three outcomes:

| Result | Meaning |
| --- | --- |
| **True** | The Condition is satisfied. |
| **False** | The Condition is not satisfied. |
| **Error** | Invalid syntax, an unavailable Name, incompatible operand Types, or a non-Boolean Condition. |

There is no Deferred Condition result. Generic Binding and instantiation cannot supply missing Condition inputs and never change a valid selection within one fixed Compilation environment.

Truth determination does not waive validation. Validate both operands of `and` and `or`, even when one determines truth. **Error** is absorbing for `and`, `or`, and `not`. Otherwise evaluate their ordinary Boolean meaning. For example, `false and missing`, `true or missing`, `debug and missing`, `false and 1`, and `true or (T is i32)` are errors when reached; neither short-circuit truth nor build mode hides the invalid operand.

Evaluate the single Condition of a `#if` and validate every explicit Condition of a reached `#switch`, including later arms whose values cannot change the selection. After successful validation, select the first True arm; False arms are skipped. If none is True, select `#case _` when present; otherwise report an error. No arbitrary theorem proving or enumeration of Types is involved.

Directive validation reaches source items, True #if targets, and **all arms** of a reached #switch; it stops at False #if interiors. Thus an unselected #switch arm still validates nested Conditions unless a False #if encloses them. This source traversal is independent of parser scheduling and semantic reachability; [excluded-syntax rules](#195-diagnostics-and-excluded-syntax) specify the remaining checks.

#### 19.4. Name-resolution boundary

Select directives using only the prepared environment before resolving Names in the affected source. The scope's lookup environment includes selected declarations, overload candidates, and aliases. [Mods](#207-mods-source-generation) may add selected declarations during generation and invalidate provisional Binding; final Binding uses the complete generated declaration set. Generated source uses the same fixed prepared environment. Extension imports are not introduced, and generation, instantiation, and implementation selection never reselect an existing directive or change Condition inputs.

Environment directives may select declarations, imports, or local syntax for a fixed target/configuration. Resolve the spliced items under their surrounding scope's ordinary visibility rules (§19.1); selected local declarations are visible to subsequent items in that scope. For example, `logging` and `assertions` in §19.1 are available after the directive when `debug` is true and are absent otherwise.

```kimi
#if windows
func platformName() -> string => "windows"

func example<T>() -> i32
    #switch
        #case pointerWidth == 64
            let result = 64
            return result
        #case _
            return 32
```

The selected body of `example` is the same for every `T` in that Compilation. Source Types and member declarations cannot affect the prepared environment. Different target/configuration inputs require their own Compilation and selection.

#### 19.5. Diagnostics and excluded syntax

**Excluded Syntax.** Tokenize every SourceDocument and diagnose all encoding, token, and indentation errors. Validate each reached #if’s complete Condition before deciding target grammar checks. In a False target, check only balanced Blocks, required executable bodies, and #switch/#case structural placement and nonemptiness. Skip ordinary expression/declaration grammar; an incomplete initializer is allowed there. Parse every reached #switch arm, applying the same nested False #if exception. Unselected arms skip ordinary semantic checking.

Speculative parsing cannot change acceptance. Suppress speculative ordinary-grammar errors in confirmed False #if targets; retain mandatory token/layout/structure errors. Validate reached Conditions immediately against the prepared environment; truth cannot hide invalid operands or missing Names. Unknown Names are Error, never False or an instantiation dependency, regardless of caching or evaluation schedule.

| Check | False `#if` target | Unselected arm of a reached `#switch` |
| --- | --- | --- |
| Tokenization and indentation | Required | Required |
| Block/directive structure and source-level nonempty executable bodies | Required | Required |
| Ordinary expression/declaration grammar | Skipped, including speculative diagnostics | Required, except inside nested False #if targets |
| Nested Directive Condition evaluation | Skipped | Required under the source-defined traversal, except inside False #if targets |
| Ordinary Name/Type/ownership checks, lowering and code generation after exclusion | Skipped | Skipped |

The controlling #if Condition and all explicit Conditions of the current #switch are checked independently of their targets under [Condition evaluation](#193-condition-evaluation-and-selection). An uppercase-initial #Name has Attribute syntax; other lowercase hash forms are errors under §6.5. Resolving an Attribute is not required in excluded Syntax.

**Error example.**

```kimi
#if false and 1
    useFeature()
```

The numeric operand is not Boolean. Short-circuit truth does not waive validation, so the Condition is a compile-time error.

### 20. Compilation configuration

A Compilation processes one Project for fixed source, dependency, target, and configuration inputs. The source-language rules determine meaning; this chapter defines compilation invariants with implementation requirements and reference algorithms in separate appendices.

#### 20.1. Build units

The build model separates workspace orchestration, project configuration, source modules, and target compilation:

| Element | Responsibility |
| ------- | -------------- |
| Solution | Holds multiple Projects and supplies options shared by their builds. |
| Project | Defines one application or library build unit. It is configured by a `.kimiproj` file. |
| Kotonoha | Defines a named module unit for an application or library, built from one or more SourceDocuments. |
| SourceDocument | An immutable source snapshot, including its path and text. Replacing its text creates a new snapshot. |
| Compilation | Compiles one Project under one fixed set of source, dependency, target, and build inputs. |

A Solution discovers and loads Projects. A Project stores target triples, aliases, and external Kotonoha descriptors, and creates one Compilation for each target.

#### 20.2. Build inputs

Compilation inputs comprise the full target triple (including ABI/environment), backend/layout, build mode and code-affecting options, Project settings, language/compiler version, source snapshots, resolved dependency versions/interfaces, and [Mod registrations, implementations, and additional inputs](#2075-inputs-and-regeneration). Reuse requires all relevant inputs to agree; OS/architecture alone is insufficient. Changed inputs require fresh analysis. Artifact cache formats are separately specified.

#### 20.3. Target preparation

Each Compilation owns the primary Kotonoha and provides target information and compile-time variables.

A target must provide the Kimigayo data-layout and ABI facts required by the language. Backend-specific representations are implementation details.

#### 20.4. Compile-time values

Prepared Compilations provide these values, fixed throughout analysis:

| Name | Type | Value |
| --- | --- | --- |
| `os` | `string` | Canonical lower-case OS family: `windows` for Win32, `macos` for MacOSX, `linux` for Linux; other recognized families use their lower-case target-family name, and an unrecognized OS uses `unknown`. Version suffixes are excluded. |
| `arch` | `string` | Canonical lower-case architecture family, such as `x86`, `x86_64`, `aarch64`, or `riscv64`; target aliases for the same family give the same value. |
| `windows`, `linux`, `macos` | `bool` | Exactly the respective comparisons `os == "windows"`, `os == "linux"`, and `os == "macos"`. At most one is true; all are false for other OS families. |
| `debug`, `release` | `bool` | The selected build mode and its negation: `release == not debug`. |
| `pointerWidth` | `i64` | Default raw-pointer width in bits from the prepared target layout; supported values in this revision are 16, 32, and 64. |

Project settings provide explicit bool, i64, or string values. Validate configured Names under §2.5, including pinned Unicode categories, NFC, and reserved words. Reject exact duplicate setting names and collisions with built-in values; collisions are errors, not overrides. Under [Condition lookup](#192-environment-condition-forms), `Feature` and `FEATURE` are distinct settings, and `WINDOWS` is distinct from the built-in `windows`. Preserve spelling and copy settings into the prepared environment before parsing. `.kimiproj` uses a `CompileTimeSettings` map whose entries set exactly one of Bool, Integer, or String.

#### 20.5. Language-version selection

An optional `.kimiproj` `LangVersion` requests an exact supported language version. If omitted, use the solution's version when supplied, otherwise the compiler's current version. Unsupported requests are errors, never silent fallback. Record the effective language version and compiler build identity in build metadata. This setting does not promise compatibility with older compilers bearing the same pre-alpha version label.

#### 20.6. Compilation invariants

Compilation follows semantic dependencies, not necessarily whole-program passes; [reference models](#appendix-b-non-normative-reference-models) show optional schedules. Resolve Conditions in the prepared environment. Mod analysis may use incomplete declarations under §20.7; complete all generation before committing final Binding or dependent layout and operation plans. Instantiation and implementation selection never reselect directives. Analyses may share facts, but unresolved obligations cannot count as successful finalization.

#### 20.7. Mods: source generation

A **Mod** is Kimigayo's Source Generator: one compiler-invoked generation step. It searches and reads Koto, generates Kimigayo source, and asks the compiler to parse and append it to a Declaration Container. Run each registered Mod once per Compilation, subject to failure or cancellation. A Mod may process many targets, append many fragments, or succeed without output.

Mods process generic declarations, not each generic instantiation; they may emit generic source. Each target-specific Compilation runs its own Mods. Several steps from one package use separate ModIds. There is no automatic retry, marker-driven rerun, or iteration until generation converges.

##### 20.7.1. Registration and execution order

Freeze registrations and dependency lists before execution. Each registration has:

| Field | Meaning |
| --- | --- |
| `ModId` | Stable, case-sensitive ID unique within the Compilation. |
| `Requires` | IDs of required Mods that must finish and integrate their output before this Mod. |
| `RequiresAfter` | IDs of required Mods that must run after this Mod finishes and integrates its output. |

Both lists require their targets to be registered; neither automatically registers them. There is no Priority. Combine both lists into one graph: `A.Requires = [B]` gives `B -> A`, while `A.RequiresAfter = [B]` gives `A -> B`. Duplicate edges count once. Reject duplicate ModIds, missing targets, self-dependencies, and cycles before running any Mod.

Run Mods sequentially. After each successful output integration and Binding update, select the smallest ModId among unexecuted Mods whose graph predecessors have all succeeded. Compare IDs in culture-independent Ordinal order. Dependency-list order and assembly loading order do not affect execution.

```text
A.Model.RequiresAfter = [C.Serializer]
B.Extra.RequiresAfter = [C.Serializer]
D.Report has no dependency declarations

A.Model ----+
            +--> C.Serializer
B.Extra ----+

Execution: A.Model -> B.Extra -> C.Serializer -> D.Report
```

Reconsider readiness after every step: C becomes ready after B and sorts before D. RequiresAfter guarantees an ordering edge, not the last position in the whole Compilation.

At entry, a Mod sees original source and every successful earlier Mod's output, not only directly named dependencies. Required ordering must be declared rather than relying on incidental ID order. A consumer of all members, such as a serializer, must follow every member-producing Mod. The consumer may declare Requires, producers may declare RequiresAfter, or a transitive path may guarantee the order. Final Binding does not detect omitted serialization work if a producer runs too late.

Graph cycles differ from references between generated Types. Mutual Type references are allowed when Mods can emit them without mutually requiring completed Binding; validate them normally after generation, including finite value layout.

##### 20.7.2. Compilation and Binding

The required generation boundaries are:

```text
Fix build inputs and validate Mod graph
    -> parse original sources and select environment directives
    -> collect declarations and perform provisional Binding
    -> for each ready Mod:
         read syntax and any required Binding information
         first parse-and-append call ends Binding access for this Mod
         continue syntax searches and appends as needed
         on success, integrate declarations and update Binding
    -> after all Mods: final Binding and required semantic checks
    -> finalize layouts and operation plans, then lower and emit
```

Only new source needs parsing; reparsing all original documents after each Mod is unnecessary. Generated declarations obey normal merge, header, duplicate-member, access, Type, and ownership rules. The diagram sets dependency boundaries, not an internal ordering for every final check.

**Provisional results.** Whole-program Binding success is not a prerequisite for running Mods. Original source may refer to Types or members that a later Mod supplies. Queries distinguish:

| Result | Meaning |
| --- | --- |
| Resolved | Information is available in the current Binding; it is not a final commitment. |
| Unresolved | Required declarations or information are not yet available. |
| Invalid | Available information establishes a rule violation. |

Unresolved means neither permanent absence nor a promise of later generation. Do not treat a problem that later additions may resolve as a definite error. A Mod may use syntax alone; if resolved semantics are essential but unavailable, it reports an error rather than requesting a retry.

**Binding access period.** A Mod may read Binding from entry until its first parse-and-append call. That call ends access for all targets, including existing Koto. A Mod that never appends may read Binding until return. Syntax searches and further appends remain available after the boundary; no Binding update occurs inside the Mod.

Names, Type names, flags, and other values extracted beforehand may be used to generate source. Retained Symbols or Binding objects must not provide semantic access after the boundary or from another Mod; the API must reject such access. A Mod needing semantics for several targets gathers all required values before appending:

```csharp
// Illustrative API: Analyze returns values and a target Koto, not Symbols.
var plans = context.FindTargets()
    .Select(target => Analyze(context.Binding, target))
    .ToArray(); // Complete all semantic reads before the first append.

foreach (var plan in plans)
    context.ParseAndAppend(plan.Target, Generate(plan));
```

The compiler need not preserve an immutable Binding snapshot after append. After a Mod succeeds, it may discard and rebuild Binding. Any incremental alternative must match full reanalysis, invalidating affected successful lookups as well as unresolved ones; added overloads can change earlier results. Provisional results never constrain final Binding and are not verified generic-definition obligations. Before emission, all required unresolved information must be resolved and all normal checks must succeed.

##### 20.7.3. Koto queries and appending source

Mods receive read-only access to existing Koto. The only mutation operation parses source and appends allowed declarations or members to a Declaration Container in the Compilation's target Kotonoha. The target may be original or generated, including one just added by the current Mod. Do not permit direct collection writes, deletion, replacement, renaming, body rewriting, reparenting, insertion into referenced libraries, or insertion of statements/expressions inside function bodies.

Pass the target Koto and source text directly, conceptually `ParseAndAppend(targetKoto, sourceCode)`. No TargetContainerId, output record, output ID, Parse-call number, or required OriginLocation argument is introduced. Each fragment follows the target's grammar; its top level denotes direct children without copying the target file's indentation. Internal indentation follows ordinary source rules.

Successful appends are immediately visible as syntax. A query fixes its result membership and order when called, not when enumeration starts. Additions cannot extend that result; a fresh query can find them:

```text
Query S1 -> [A, B]
Process A; append C
Continue S1 -> B only
Query S2 -> [A, B, C]  (when this is their logical order)
```

This snapshots the result list, not the whole tree. A new member query on an existing Container may observe newly appended members. Queries use [logical declaration order](#2074-generated-sources-and-declaration-order). For merged declarations, use the first fragment as the ordering key; each API must state whether it returns fragments or merged declarations. Attribute queries follow [Mod marker rules](#65-attributes).

##### 20.7.4. Generated sources and declaration order

Each generated fragment has its own immutable SourceDocument and CodeContext under [source identity](#a1-source-identity-and-incremental-analysis), even when appended below the root. Preserve these sources for regeneration and diagnostics. Resolve generated names in the target's enclosing declaration scopes and the generated document's own alias environment; do not inherit source-local aliases from the target's original file. Use qualified names or aliases permitted by the fragment grammar.

The compiler retains links between the producing Mod, source, target Koto, and generated Koto. No per-output identity or correspondence across builds is required.

Define **logical declaration order** independently of layout and Mod execution order:

1. Ordinary sources precede generated declarations. Sort them by stable logical source name, then source declaration order.
2. Sort generated declarations by ModId in Ordinal order. Within a Mod, preserve Koto addition order and the written order inside each fragment.

Ordinary logical names are normalized project-relative paths; external files need assigned project-relative names. Normalize separators to `/`, remove redundant segments, and reject collisions. Names are independent of absolute checkout and temporary paths and are compared without host case folding or locale rules. Addition order is retained directly and needs no Parse-call numbering.

Identical inputs must produce identical additions and order. Changing dependency declarations alone does not change logical order when ModIds, generated content, and each Mod's addition order remain the same. Renaming sources or Mods, or changing addition order, may change initializer side-effect order. Kimigayo-layout storage uses this order under [split structures](#621-split-structures-and-storage-order). C-layout Fields occupy one fragment in written order (§21.1.2); moving that whole fragment or renaming method-only fragments does not reorder its Fields. Physical layout remains governed by §21.1. Conflicting declarations follow normal integration rules, never last-writer-wins replacement.

##### 20.7.5. Inputs and regeneration

In addition to [general build inputs](#202-build-inputs), record ModIds, both dependency lists, settings, Mod API compatibility, implementation assemblies and dependencies by content, and additional files by logical name and content. Provide compiler-managed access to declared additional inputs, enumerated by normalized logical name in Ordinal order. A Mod runs on the host but obtains target facts from its Compilation.

Generation must be deterministic for identical inputs. Do not implicitly depend on current time, randomness, undeclared environment variables, file enumeration order, or other external state. Supply external data as fixed declared input. This is a Mod contract, not a promise of OS-level isolation for C# code.

Rebuild from original inputs rather than treating previous generated Koto as original source. Replace each Mod's output collection, removing outputs no longer produced, including after a Mod or input is removed. Do not carry old Koto or Binding objects into a new Compilation. Output caches are optional; reuse must validate all relevant inputs, including earlier Mod outputs, and restore equivalent source, target associations, addition order, diagnostics, and provenance.

##### 20.7.6. Failures and diagnostics

A Mod exception, reported error, invalid append operation, generated syntax error, or definite integration error fails the Mod and Compilation. A still-unresolved provisional dependency is not by itself such an error. Skip all descendants of a failed Mod in the combined graph, including RequiresAfter successors. The compiler may stop all remaining Mods; continuing independent diagnostics must not expose failed partial output as valid input.

Partial Koto may be retained for inspection, but never published or cached as successful output. Do not substitute a previous successful output to make a failed build succeed.

Retain ModId, generated source and position, and target Koto for diagnostics. Follow provenance recursively when the target is generated. A diagnostic may point to an input Koto or Attribute, but a compiler cannot infer every cause from the append target alone. Distinguish the recorded append chain from causes explicitly reported by the Mod.

```text
Demo.kimi: Demo
    -> Example.Model adds Item
        -> Example.Describe adds getVersion
            -> diagnostic in generated source: line and column
```

Provide generated-source viewing/saving, dependency and execution-order inspection, per-Mod timing, and failure/skip reasons. Show cycle paths such as `A -> B -> C -> A`. Saved diagnostic copies are not automatically ordinary source inputs.

##### 20.7.7. Two-step example and host boundary

The following API names are illustrative, not existing implementation guarantees. The initial host uses a C# interface and prebuilt assemblies; loading a Mod cannot depend on completion of its target program. A package may register several implementations with distinct IDs.

```csharp
public interface IMod
{
    string ModId { get; }
    IReadOnlyList<string> Requires { get; }
    IReadOnlyList<string> RequiresAfter { get; }
    void Execute(ModContext context);
}
```

Assume the example's GenerateModels and Describe markers are recognized with suitable target/argument contracts. Original source may refer to both generated declarations before either exists:

```kimi
#GenerateModels
public group Demo
    public func readVersion() -> i32 => Item.getVersion()
```

Register `Example.Model.RequiresAfter = [Example.Describe]` and `Example.Describe.Requires = [Example.Model]`. Both describe the same edge; each Mod still runs once. Their Execute bodies are:

```csharp
// Example.Model: syntax-only queries return fragment snapshots.
foreach (var target in context.FindContainersWithAttribute("GenerateModels"))
    context.ParseAndAppend(target, """
        #Describe
        public struct Item
            public var value: i32 = 0
        """);

// Example.Describe, in its later invocation:
foreach (var target in context.FindStructuresWithAttribute("Describe"))
    context.ParseAndAppend(target, """
        public func getVersion() -> i32 => 1
        """);
```

The example's GenerateModels contract restricts its target to a group; a complete implementation validates target and argument rules. Neither body needs Binding, so it can alternate syntax reads and appends. Binding is updated between Mods. The resulting tree is:

```text
Demo                         original source
├─ readVersion               original source
└─ Item                      Example.Model
   ├─ value                  Example.Model
   └─ getVersion             Example.Describe
```

These nodes retain separate source contexts; no original file is rewritten. Final Binding resolves `Item.getVersion()` and validates the complete program.

Concrete query and marker-registration types, assembly packaging/compatibility checks, project configuration syntax, cache formats, and IDE presentation remain implementation design work. Parallel Mod execution, arbitrary Koto rewriting, function-body insertion, per-instantiation execution, and automatic retries are outside this initial model. A single invocation and snapshot queries do not prevent a Mod's own infinite loop; cancellation and time-limit mechanisms belong to the host.

#### 20.8. LLVM output, native build and execution

##### 20.8.1. Output scope and settings

The windows-x64-v1 compiler produces one pre-optimization textual .ll and one .link.json per project/target, after final semantic acceptance and supported-operation checks (§21.4). The `emit-llvm` command stops after publishing this pair. The `build` command continues through external LLVM verification, optimization, object generation and linking. The `run` command executes an existing binary without compilation. These commands do not discover/install LLVM or the Windows SDK. A dedicated runtime DLL is not required; runtime bodies are emitted in the same module, with separate native backend support (§21.5.7).

| Setting | Initial rule |
| --- | --- |
| Targets | x86_64-pc-windows-msvc |
| OutputKind | Application (default) or inspection-only Library (§22.2.2) |
| OutputPath | .ll destination; default bin/<target>/<ProjectName>.ll |
| NativeLibraries | Per-target logical name to kind/input mapping (§20.8.2) |
| Optimization | O0 or O2 (default); applied during native build |
| LlvmBin | Project-relative or absolute LLVM bin directory used by build; emit-llvm records it without executing tools. The CLI --LlvmBin value overrides it and resolves relative to the invoking working directory. Neither changes the target/version contract. |
| EntrySource | Not an initial selection setting; use §22.2's unique-candidate rules |

The first execution subset is ordinary functions, simple local bindings, Unit, string literals, required ownership/cleanup, and Core.writeLine. Arrays, Dictionary, inheritance, closures, static Property execution, general generic sharing, and multiple-Kotonoha linking need not be included in this first execution test. Their language rules are not weakened; unsupported required operations fail. Layout computability, physical ABI support, and runtime availability are separate checks.

Generation success certifies the matched IR/manifest pair, not LLVM acceptance, a linked executable, or successful execution. LLVM verification/object generation, manual linking, and running the produced executable are separately reported stages. Library output supports inspection/verification/object generation only.

##### 20.8.2. NativeLibraries

Each Ordinal logical key maps to `kind` (import or static) and one .lib `input`, never a DLL file. Import produces dllimport declarations; static does not. A simple filename is a linker search name, a relative path with separators is project-relative, and an absolute path is unchanged. Diagnose empty/NUL values or embedded linker options; never execute these strings as commands.

Reserve kernel32 as an automatically generated import library and kimi_backend = static/kimi_backend_windows_x64_v1.lib. A kernel32 entry in NativeLibraries is an error with a diagnostic instructing removal; no SDK kernel32.lib or user-provided replacement is used. Other keys require configuration; do not guess .lib names from DLL names. Explicit backend paths must select the adopted supply.

The compiler embeds the project-owned backend/windows-x64/kernel32.def. It contains KERNEL32.dll and the seven runtime APIs in §22.5.6 plus VirtualAlloc, VirtualProtect and VirtualFree for backend tests. Normalize the definition to UTF-8 without BOM, LF line endings and one terminal newline; verify its SHA-256 against profile.json. During native build, materialize the definition in a fresh staging directory and invoke `llvm-dlltool -m i386:x86-64 -d kernel32.def -l kernel32.lib`. Verify the generated DLL name, x64 COFF formats and exact public/__imp_ symbol set before publishing and linking the library. The runtime still uses the OS-provided KERNEL32.dll. Additional kernel32 imports require a reviewed definition/profile update; the current library is not a replacement for the entire SDK export surface.

Static libraries must not depend on CRT startup, automatic C/C++ dynamic initialization, custom TLS initialization/termination, or automatic atexit handlers. Zero-initialized and constant data are allowed. Code needing such startup/termination needs a supported adapter first. DLL initialization follows §22.2.3. These are supplier/user connection contracts: a .lib filename and /NODEFAULTLIB do not establish or perform initialization.

##### 20.8.3. Link manifest and publication

Replace OutputPath's extension with .link.json in the same directory and write UTF-8 JSON for both output kinds. The schema is:

```json
{
  "schemaVersion": 2,
  "target": "x86_64-pc-windows-msvc",
  "codegen": {
    "profile": "windows-x64-v1",
    "llvmVersion": "22.1.8",
    "cpu": "x86-64",
    "features": ["+sse2"],
    "relocationModel": "pic",
    "codeModel": "small",
    "unwindTables": "async",
    "optimization": "O2"
  },
  "backendSupport": {
    "packageId": "kimi-backend-windows-x64",
    "abiVersion": 1,
    "packageVersion": "0.1.0",
    "library": "kimi_backend",
    "artifactSha256": "<64 hex digits for the adopted archive>",
    "providedSymbols": ["__chkstk", "memcpy", "memmove", "memset"]
  },
  "irFile": "ProjectName.ll",
  "irSha256": "<64 hex digits for the generated IR>",
  "outputKind": "Application",
  "entry": "__kimi_start",
  "subsystem": "console",
  "libraries": [
    { "name": "kernel32", "kind": "import", "generator": "llvm-dlltool", "dll": "KERNEL32.dll", "definitionSha256": "<64 hex digits for the normalized definition>" },
    { "name": "kimi_backend", "kind": "static", "input": "kimi_backend_windows_x64_v1.lib" },
    { "name": "observer", "kind": "import", "input": "observer.lib" }
  ],
  "providedRuntimeSymbols": ["_fltused"],
  "expectedUndefinedSymbols": [
    { "symbol": "__chkstk", "provider": "kimi_backend" }
  ]
}
```

Hashes must be actual SHA-256 values. packageVersion is supplied by Directory.Build.props Version (currently 0.1.0), not a separate backend release counter. A version alone does not establish an adopted archive; the catalog hash and ABI must also match. observer and the __chkstk expected reference are conditional examples; _fltused is always supplied.

- Deduplicate libraries required by external declarations or the profile and sort by Ordinal logical name. Rewrite path inputs relative to the manifest; preserve linker search names. irFile is manifest-relative.
- Library uses null entry and subsystem. Its dependency record does not establish an external .lib/DLL ABI or runnable artifact.
- codegen is mandatory and matches §21.5.1; irSha256 hashes pre-optimization .ll.
- backendSupport comes from the adopted catalog. Its library references a static libraries entry; ABI/version/symbols must agree. Manual builds verify the actual .lib hash before use.
- providedRuntimeSymbols lists generated backend definitions, always _fltused, not ordinary __kimi_ helpers. expectedUndefinedSymbols lists backend references anticipated at generation, with a known provider in libraries. Verify backend symbols against backendSupport.providedSymbols; unknown providers for known dependencies fail generation.
- Sort both symbol arrays by Ordinal symbol name, without duplicates or overlap. They are not the final object's undefined-symbol list; even an empty list cannot promise no later dependency. LibraryImport/Windows inputs are recorded in libraries.
- Generated kernel32 entries have no input path. Validate the generator, DLL and definition hash against the embedded profile; reject substitutions, extra input paths and schema 1 manifests with a re-emission diagnostic. emit-llvm still requires no native tools and publishes no import library.
- Build records retain actual library hashes, generator/tool identities, normalized definition hashes and tool settings. Each project/optimization writes its own .kernel32.def/.kernel32.lib. Generate into a fresh staging directory, publish only after validation, and fail without linking stale libraries if generation fails. Never embed absolute build paths in the generated library; redact local report paths.

Complete both temporary outputs before publication, publish the manifest last, and report success only after both are published. A partial publication is failure; old files are not evidence of current success. Consumers check irSha256 because interruption can leave a mixed pair. Success reports both paths, purpose (Application input or Library inspection), entry, and required link inputs.

When LlvmBin is configured, the manifest may additionally contain `"toolchain": { "llvmBin": "<manifest-relative directory>" }`. Resolve a relative setting from the project directory. This is a local build-tool location, not part of the code-generation profile or evidence of a tool's version. A build command or separately invoked builder may override the location, but must still check the actual tool versions under §20.8.5; a directory name or configured path cannot certify version compatibility. External native library files remain configured separately through NativeLibraries; kernel32 is generated from the embedded definition.

##### 20.8.4. Manual toolchain example

With LLVM 22.1.8 and all manifest inputs resolved, including a verified backend archive:

```powershell
opt -S -passes="default<O2>" -mtriple=x86_64-pc-windows-msvc ProjectName.ll -o ProjectName.opt.ll
llc -O2 -filetype=obj -mtriple=x86_64-pc-windows-msvc -mcpu=x86-64 -mattr=+sse2 -relocation-model=pic -code-model=small ProjectName.opt.ll -o ProjectName.obj
llvm-dlltool -m i386:x86-64 -d kernel32.def -l kernel32.lib
lld-link ProjectName.obj kernel32.lib kimi_backend_windows_x64_v1.lib /entry:__kimi_start /subsystem:console /nodefaultlib /debug /out:Application.exe
.\Application.exe
```

Use every manifest input, not just the example's libraries. O0 omits opt and passes the original .ll to llc -O0 with the same profile. Verify IR before/after optimization and inspect actual object dependencies. /debug does not create Kimigayo line/variable information; CodeView/PDB emission remains separate from Abort source context.

Before adopting a profile, validate it with the pinned LLVM version; another version's preliminary result is not acceptance. Keep semantic tests, representative IR structure/goldens, object ABI/unwind/dependency checks, and execution results distinct (§A.14). Performance decisions use measured execution time, code size, and build time, never weakened checks.

##### 20.8.5. LLVM version checks and exploratory builds

The expected LLVM release has one machine-readable source: `backend/windows-x64/profile.json`, currently 22.1.8. The compiler embeds this catalog, exposes its version through WindowsProfile.LlvmVersion, and writes it as codegen.llvmVersion. Native build and verification scripts read the same catalog; do not duplicate the expected version in executable code. This value describes the intended profile, not a detected installation. Parsing, semantic analysis and IR/manifest generation neither execute LLVM nor require it to be installed.

Immediately before native toolchain work, check every selected version-reporting tool's actual `--version` output. Compare the full release version, including patch level; newer versions are not implicitly supported, and development/prerelease suffixes do not match the release. Parse a recognized tool version banner, not an arbitrary occurrence of the expected number. Missing tools, failed probes, and absent or ambiguous version information are errors even in exploratory mode. Diagnostics identify the tool path, expected version, and actual version or probe failure. The versionless llvm-lib exception remains explicit: retain its executable path/hash and record it as unversioned, never invent a matching version. llvm-dlltool also has no version banner: compare its executable SHA-256 against profile.json kernel32.dlltoolSha256 before invocation. Record expectedSha256, sha256 and hashMatched without fabricating actualVersion. An explicit exploratory override permits a different hash with a warning and unverifiedToolchain=true; it does not bypass definition or generated-library validation. reportedVersionsMatched describes only version-reporting tools.

Normal native builds fail on a mismatch before IR verification, optimization, object generation or linking. `kimi build --AllowUnpinnedToolchain true` and the separately invoked manual/backend builders' `-AllowUnpinnedToolchain` permit exploratory work only. Each mismatched tool then produces a visible warning and may continue through the ordinary verification/build steps. This option does not override manifest/profile mismatches, IR/archive hashes, ABI contracts, dependency checks or other errors. Adoption and generated-module profile verification still require matching tools and cannot use this override.

After successful version probing, native build records retain the expected version, actual per-tool versions and executable identities, reportedVersionsMatched, and unverifiedToolchain. Retain these facts even if subsequent native work fails, with status incomplete. A successfully linked or executed exploratory program remains unverified for the pinned profile; do not relabel its manifest with the detected version or count it as profile adoption evidence. Formal support for another LLVM release requires renewed profile validation and a deliberate catalog update.

##### 20.8.6. Compiler commands and artifact lifecycle

| Command | Required behavior |
| --- | --- |
| `kimi emit-llvm <project-or-solution>` | Perform the required source/ownership/generation checks and publish the matched pre-optimization .ll/.link.json pair. Never execute LLVM, validate an installed LLVM version, link or run. Successful output reports both paths; LLVM acceptance is a separate stage. |
| `kimi build <project-or-solution>` | Generate fresh LLVM inputs, validate the actual tool versions and native inputs, run opt verification (and default<O2> only at O2), llc and lld-link, and publish the executable and a successful build record. Never execute the Application. |
| `kimi run <project>` | Resolve the configured existing executable, require a successful latest build record and matching executable hash, and execute without source analysis, IR generation, LLVM version checks or rebuilding. Source changes do not trigger compilation; users explicitly build when needed. |
| `kimi run <path.exe>` | Execute the explicitly selected existing binary directly without a project or build record. |

Build and emit-llvm accept configured projects/solutions or discover them in the specified directory (current directory when omitted). Loading any selected project unsuccessfully is failure; empty discovery is not a successful build. Run through project/directory/solution discovery requires exactly one loaded Application. Do not silently choose the first of several projects. Standalone source compilation is not part of run. The current native profile supports Windows x64 Applications; unsupported targets or Library emission must receive diagnostics rather than placeholder binaries. The existing limited emitter remains limited (§21.4); command automation does not add language-feature support.

OutputPath continues to name the pre-optimization .ll. For `Name.ll`, optimization O0/O2 selects `Name.O0.obj` / `Name.O2.obj` and `Name.O0.exe` / `Name.O2.exe`; O2 also retains `Name.O2.ll`. The build record is `Name.link.build.json`. A configured --Target selects one of the project's configured targets; the current emission implementation requires exactly one Windows x64 target. The CLI Boolean option requires an explicit value, e.g. `--AllowUnpinnedToolchain true`.

At the beginning of a native build attempt, invalidate the previous success record before semantic analysis. Link into a fresh temporary executable and publish it only after success. A failed build may retain a prior executable for inspection, but the project run command must not treat it as a successful result of that attempt. An emit-llvm command does not rewrite the native build record. Build records retain version, input/tool identity, optimization and executable hash information; descriptive machine paths are remapped and are not execution inputs. Launch through a project derives the executable path from its current output settings and verifies the recorded hash; changing those settings requires the corresponding built artifact. An explicitly selected .exe path remains independently runnable.

External processes are launched directly with separately supplied arguments, without constructing shell commands. Drain native-tool stdout/stderr concurrently, report failures, propagate cancellation to child process trees, and bound individual native-tool invocations (currently five minutes). Run forwards stdin and the child's stdout/stderr, preserves output bytes, and returns the child exit code. Project runs use the project directory as working directory; direct binary runs use the caller's current directory. Build/emit failures and launch errors return 1, successful build/emit return 0, and command cancellation returns 130. No fixed Application runtime timeout is imposed.

##### 20.8.7. Compiler and backend release version

Directory.Build.props Version is the single release-version source for Kimigayo and its backend package. The compiler embeds that MSBuild value as assembly metadata and uses it for the default version display, compiler build identity prefix and backendSupport.packageVersion. Repository tools combine that same props value with profile.json; profile.json retains the LLVM release, ABI, helper symbols and adopted archive hash, without its own package-version literal. Language version, ABI version and LLVM version are independent identifiers and do not change simply because the package release changes. Candidate reports carry the shared release with adopted=false; release equality never substitutes for native archive/hash validation. Changes to adopted archive contents require renewed validation and a shared release update before distribution.

### 21. Layout, runtime metadata, and code generation

Physical representation and code sharing must preserve the language rules for Type identity, ownership, evaluation, and cleanup. The following requirements constrain backend choices without prescribing one internal representation.

#### 21.1. Structure layout and ABI

Storage layout, valid value representation, and function ABI are separate contracts. A layout Attribute selects storage rules; it does not select a calling convention, grant Copy, or make a Type safe for foreign construction. The initial physical rules below belong to the versioned [Windows profile](#215-llvm-windows-x64-profile), not a stable cross-version ABI.

##### 21.1.1. Common layout rules

For a storable Type T, size(T) reserves inline bytes including padding, alignment(T) is a positive power of two, and stride(T) is size rounded up to alignment (zero when size is zero). These are specification notation, not source operators. Check rounding, addition, and multiplication for overflow. In windows-x64-v1, size, stride, and valid Field offsets cannot exceed 2^63 - 1; diagnose any stricter backend limit with its reason. A representable size does not guarantee successful allocation.

Fixed arrays have size and stride N * stride(T), element offset i * stride(T), and alignment(T), including N = 0. Unit has size 0, alignment 1, stride 0. Zero-sized values retain evaluation, initialization, ownership, Loans, and destruction. Shared addresses do not merge logical Places. Positive-sized components cannot overlap; nested padding cannot be reused by outer Fields. Padding has no guaranteed content, even in an initialized value; permitted byte transfers may include it, but typed reads, comparisons, and integer coercions must not treat it as a value.

Layout uses the selected, merged storage declarations after Mods, Type substitution, and storage classification. A struct contains its own Fields and its direct inline base, if any; computed members add no storage, and custom stored accessors do not change slot count. Reject infinite inline storage cycles; borrows, raw pointers, and object handles do not embed their referents. Reordering cannot change Field Identity, logical initialization order, Partial Move, Loans, or destruction order. Identical compiler, target, settings, and selected inputs must reproduce layout.

##### 21.1.2. Layout Attribute and fragments

`#Layout("Kimigayo")` and `#Layout("C")` are compiler-recognized Attributes on struct declarations only. Accept exactly one unnamed, non-interpolated string literal, matched case-sensitively; a trailing comma is allowed. Diagnose missing/extra/named arguments, unknown modes, wrong targets, and repeated Attributes on one fragment, even with equal values. No `#Repr` alternative or layout-query syntax is introduced.

An omitted Attribute means unspecified during fragment merging. Share the explicit mode across selected fragments; equal specifications on different fragments are allowed, conflicting ones are errors. With none, choose Kimigayo. Explicit Kimigayo and omission create no distinct Type Identity.

C layout requires all selected instance Fields, including generated Fields, to occur in one fragment in written order. Other fragments may add methods, computed members, and other declarations without storage; the Attribute need not occur on the storage-bearing fragment. Kimigayo layout uses §20.7.4's logical order. Neither uses filesystem enumeration or Binding order. Finalization follows Mods and selection; storage cannot be appended afterward.

A generic struct shares its mode across instantiations, but each concrete Type has its own size, alignment, offsets, and formation checks. Retain unresolved layout obligations until the required instantiation; do not infer equal layout or code sharing from equal modes.

```kimi
// Record.kimi
#Layout("C")
struct NativeRecord
    var kind: u8
    var value: u64
    var flags: u8

// Methods.kimi: shares C layout without repeating the Attribute.
struct NativeRecord
    func read(self: ref/Self) -> u64 => self.value

#Layout("c") // Error: mode names are case-sensitive.
struct Invalid
    var value: i32
```

##### 21.1.3. Kimigayo and C modes

**Kimigayo.** Each Field/base meets its natural alignment; the aggregate alignment is at least their maximum. Reserved component ranges fit inside the struct. Physical reordering is permitted, but no declaration-order layout, minimum size, base offset zero, C compatibility, or stable ABI across compiler/input changes is promised. This is Kimigayo's own contract, not rustc layout or Rust ABI.

The initial algorithm places the direct base first, then own Fields in logical order, each naturally aligned, and rounds the total to the maximum alignment. Size equals stride. If every component has size zero, size/stride are zero and alignment is the maximum component alignment, or 1 for an empty struct. Base offset zero is an initial implementation choice only.

**C.** The initial contract is Windows x64 MSVC layout with effective packing 16 (/Zp16), without pragma pack, explicit alignment, or bit-fields. Do not inherit host packing settings. Nested Types keep their own modes. Require natural Field alignment at most 16; larger alignment is unsupported, not silently reduced.

```text
cursor = 0
aggregateAlignment = 1
for each Field F in written order:
    a = min(alignment(F.Type), 16)
    cursor = checkedAlignUp(cursor, a)
    offset(F) = cursor
    cursor = checkedAdd(cursor, size(F.Type))
    aggregateAlignment = max(aggregateAlignment, a)
alignment(S) = aggregateAlignment
size(S) = stride(S) = checkedAlignUp(cursor, aggregateAlignment)
```

Reject C layout on open or derived structs, empty structs, direct zero-sized Fields (including Unit and zero-length arrays), and multiple storage-bearing fragments. Methods, constructors, computed members, and deinit do not themselves prevent C layout; foreign construction/destruction is a separate contract. C layout describes the owned payload, not an object handle, allocation header, or reference count.

NativeRecord above has offsets 0, 8, 16, size/stride 24, and alignment 8:

```llvm
%NativeRecord = type { i8, i64, i8 }
; In a function, with valid initialized storage:
%address = getelementptr %NativeRecord, ptr %record, i32 0, i32 1
%value = load i64, ptr %address, align 8
```

```c
#include <stdint.h>
#include <stddef.h>
typedef struct NativeRecord {
    uint8_t kind;
    uint64_t value;
    uint8_t flags;
} NativeRecord;
/* Compile with Windows x64 packing 16 and no pragma pack. */
_Static_assert(offsetof(NativeRecord, value) == 8, "value offset");
_Static_assert(sizeof(NativeRecord) == 24, "size");
_Static_assert(_Alignof(NativeRecord) == 8, "alignment");
```

Use ordinary LLVM structs, not packed `<{ ... }>`, for C layout. Verify allocation size, ABI alignment, and offsets against TypeLayout. Source Field index, LLVM element index, and byte offset are distinct. C mode alone does not define native exports, mangling, DLL compatibility, serialization, or aggregate argument passing.

##### 21.1.4. Initial scalar representation

windows-x64-v1 is little endian, uses address space 0, and has 64-bit pointers. Validate this table against LLVM 22.1.8's target DataLayout. LLVM integer Types carry width, not signedness; select signed/unsigned operations from the language Type.

| Language Type | LLVM storage | LLVM computation | Size / alignment / stride, bytes |
| --- | --- | --- | --- |
| i8 / u8 | i8 | i8 | 1 / 1 / 1 |
| i16 / u16 | i16 | i16 | 2 / 2 / 2 |
| i32 / u32 | i32 | i32 | 4 / 4 / 4 |
| i64 / u64, isize / usize | i64 | i64 | 8 / 8 / 8 |
| i128 / u128 | i128 | i128 | 16 / 16 / 16 |
| f32 / f64 | float / double | float / double | 4 / 4 / 4; 8 / 8 / 8 |
| bool | i8 | i1 | 1 / 1 / 1 |
| char | i32 | i32 | 4 / 4 / 4 |
| unsafe/T | ptr | ptr | 8 / 8 / 8 |
| Unit | May be omitted | No ordinary value | 0 / 1 / 0 |

Valid stored bool bytes are only 0 and 1. Under that validity premise, load i8 and truncate to i1; store by zero-extending i1 to i8. This does not sanitize invalid bytes. Windows BOOL is i32 and converts by comparison with zero. char stores an unsigned Unicode scalar value: surrogates and values above U+10FFFF are invalid. Neither representation establishes compatibility with C char or WCHAR.

This table does not authorize all operations or FFI uses. Safe borrows and object handles must not be inferred to be one raw pointer. Heap alignment support is separately limited to 16 (§22.5.2); never round down a Type's alignment.

##### 21.1.5. Tuple and enum layout

Initial Tuples place elements in logical index order at natural alignment and round the total to the maximum alignment. All-zero-sized elements yield size/stride zero. Keep logical indices separate from physical indices/offsets. Future internal reordering must preserve evaluation/destruction order; Tuple has no Layout Attribute or C ABI. For example, (u8, u64) initially has offsets 0 and 8, size/stride 16, alignment 8.

Initial enums use an explicit i32 tag and an aligned payload area, without niche optimization. Number selected Cases from zero in declaration order; more than 2^32 Cases is unsupported. These internal tags introduce no source discriminants or integer conversion.

Lay out each Case payload in logical element order. Let A be the maximum payload alignment and P the maximum payload size rounded up to A; if all payloads are empty, use A = 1, P = 0. The payload starts at alignUp(4, A); enum alignment is max(4, A), and total size/stride is rounded to that alignment. Only a selected tag paired with its valid active payload is a valid enum value. Initialization, Move, match, and cleanup use that active Case, never unused payload bytes.

Preserve payload alignment in LLVM with a zero-length alignment carrier followed by P bytes: use [0 x i8/i16/i32/i64/i128] for alignment 1/2/4/8/16. The carrier is not a language Field or cleanup target.

```kimi
enum Message
    Quit
    Number(i64)
```

```llvm
; Payload offset 8, total size 16, alignment 8.
%MessagePayload = type { [0 x i64], [8 x i8] }
%Message = type { i32, %MessagePayload }
```

Verify these layouts when nested in structs/arrays. Do not flatten payloads to alignment-1 byte arrays, expose tag-only mutation, or claim C enum/union compatibility.

##### 21.1.6. C exchange eligibility

C layout and C-exchangeable storage are different judgments. Initially allow owned i8/u8 through i64/u64, owned f32/f64, raw unsafe/T pointers, positive-length fixed arrays of eligible elements, and owned C-layout structs whose Fields are recursively eligible.

Exclude Kimigayo-layout structs, Tuples, enums, string, dynamic collections, bool, char, i128/u128, isize/usize, safe borrows, object handles, and function/closure values until their C correspondence is specified. A C-layout struct may contain string if layout succeeds, but it is not C-exchangeable. For example, C-layout Pair<i32> and Pair<u64> have different layouts; Pair<()> violates the zero-sized Field restriction, and Pair<string> gains no marshalling.

A pointer guarantees its value representation only; its pointee may remain opaque. Foreign reads/writes require a separate pointee-layout and validity contract. Foreign construction/overwrite for receipt by Kimigayo initially also requires no user deinit recursively. Constructors, lifetimes, ownership transfer, active Loans, and accessor bypass still require the unsafe contract. No automatic Copy capability, raw-storage initialization API, or safe-to-raw conversion follows.

For ABI comparison, retain target ABI, recursively eligible Field Types, merged order/content, and layout options. Aggregate arguments/results remain excluded from LibraryImport (§22.3), even when storage is C-exchangeable. Packed/transparent layouts, explicit alignment/offsets, unions, bit-fields, flexible array members, external enum representations, and public layout queries remain extensions.

#### 21.2. Object metadata

##### 21.2.1. Type identity and descriptors

Every live object can reach immutable metadata for its Dynamic Type, shareable among objects of that concrete Type. Required logical information is:

```text
Type Descriptor
    ├─ Runtime Type Identity
    ├─ relationships required by defined Supports operations
    ├─ complete dynamic destruction operation
    └─ layout or receiver-adjustment information where required
```

**Runtime Object Type Identity**, also called Runtime Type Identity here, identifies the actual concrete payload Core D. Use these logical functions on Types whose necessary formation checks have succeeded:

```text
N(A)      = normalize transparent aliases, resolved associated-Type projections,
            grouping, and redundant owner prefixes
ArgKey(A) = remove every Origin from N(A), recursively retaining Type structure
            and Semantics; nominal nodes retain Symbol/Kotonoha/version and
            their ordered argument keys
CoreId(D) = the concrete Core's identity computed by the same rules
```

An object handle's Runtime Type Identity is `CoreId(D)`, excluding its root `obj/rc/arc/objref/objuniq`. Do not apply this root-handle removal recursively inside generic arguments: their Object Semantics remain in ArgKey. Preserve Function/Tuple structure and generated Closure declaration identity. A static base or Contract View Target is not a substitute for the actual D.

| Comparison, assuming valid Types | Runtime Type Identity |
| --- | --- |
| `obj/D` and `rc/D` for the same actual D | Equal |
| `Box<ref/i32 from a>` and `Box<ref/i32 from b>` | Equal |
| `Box<ref/i32>` and `Box<i32>` | Different |
| `Box<objref/C>` and `Box<obj/C>` | Different: inner Semantics are retained |
| `obj/Box<ref/i32 from a>` and `obj/Box<i32>` | Different after root-handle removal |

Runtime identity equality does not imply equal value representation, layout, ABI, ownership operations, assignment compatibility, Origins, or Loans. It neither authorizes code sharing nor skips validation. Equal names, layouts, member sets, or descriptor addresses alone also do not define identity. Fixed-array Type structure retains the evaluated length and element-Type key. General Const arguments beyond function lengths are not introduced. Other identity and key purposes are separated under [generation keys](#2132-identity-and-generation-keys).

Declared base and conformance relationships are fixed by validated definitions; no unrelated extension, module search, or runtime registration changes them. Revalidate dependent artifacts under §21.3.4 before generating metadata; their identities, bases, and verified mappings must agree, independently of load order. Receiver adjustment cannot bypass public ObjectCompatible, access, Type, Origin, or Loan checks. ObjectCompatible is compile-time interface information, not a required Descriptor field. A destruction entry does not make deinit a source-level function value.

An extension that introduces runtime implementation selection defines its own additional selection information. The current descriptor requirements impose no member slots or fixed ABI.

##### 21.2.2. Representation and storage responsibilities

Each view must reach the same original object's type identity, the receiver needed by its implementation, a valid adjusted cast result, and preserved Origins/Loans. Owning views must also reach complete destruction and original storage-release information.

Reference counts and allocator state belong to instances/allocations, not the shared immutable Type Descriptor. Logical roles may be co-located physically. The internal Windows x64 object profile in §21.2.3 fixes its handle/header representations. No target-independent base offset zero, equal pointer values between views, one-word borrow, fixed table layout, descriptor representation, calling convention, stable ABI, FFI layout, or dynamic-loading compatibility is promised.

Before emitting an operation, implement its complete layout/receiver adjustment, metadata, ownership, and ABI contract. The object/count/Weak profile is defined in §21.2.3; headers/counts remain outside an owned C-layout payload. Dynamic Array/Dictionary need data/length/capacity, reallocation and element-state contracts; Slice needs reference/length and reslicing rules preserving Loans. Closures/common Function Types need capture/environment storage, call/destruction entries and inline/allocation choices, with logical capture order separate from offsets. These remaining representations and shared-generic metadata passing must not silently become one ptr. Atomic counters introduce no thread facility.

Optimization may share, normalize, omit, or directly resolve metadata only while preserving Type tests, implementation selection, evaluation order/count, effects, failure, Origins/Loans, and destruction. It must not remove information needed by separately compiled consumers. Source legality cannot depend on allocation elimination or direct resolution of an implementation selection.

##### 21.2.3. Windows x64 object and weak profile

This versioned, module-local internal representation is independent of a stable external ABI. Adoption does not certify compiler implementation. See the [object runtime design](doc/Design/2026-09-13%20Weak%20References%20and%20Object%20Runtime.md) for the detailed migration, release, alignment, and validation protocol.

| Representation | Initial storage |
| --- | --- |
| `obj` / `rc` / `arc` / `objref` / `objuniq` handle | One non-null ptr to the original object header, unchanged by a view change |
| `obj` header | Immutable descriptor ptr at offset 0; size/alignment 8 |
| `rc` / `arc` header | Immutable descriptor ptr at offset 0, u64 control at offset 8; size 16, alignment 8 |
| `Weak<S>` value | One nullable ptr to a side table; size/alignment 8; null means empty |
| Side table | u64 strong at offset 0, u64 weak at offset 8, immutable object-header ptr at offset 16; size 24, alignment 8 |

Payload begins at alignUp(headerSize, payloadAlignment). The descriptor reaches dynamic Type identity, payload layout, base/receiver adjustment, complete destruction, and original allocation release. Different allocation-layout descriptors may reference the same Runtime Type Identity. In particular, object borrows cannot infer payload offset from an erased ownership mode. Allocate header and payload together initially; check all size/alignment calculations and preserve the original allocation pointer for over-aligned blocks. No per-instance counter resides in shared immutable metadata.

Control's low bit selects representation: even means `strong << 1`, odd means a side-table pointer tagged with bit 0. Initially control=2, representing one strong owner. Decode the table by clearing bit 0; do not truncate upper pointer bits or assume a 48-bit address space. This encoding requires the Windows x64 integral address-space-0 pointer and table alignment. Inline strong ranges from 1 to 2^63-1; zero is final release. The first downgrade or an increment past the inline limit promotes the current count, unchanged, into one shared side table. Promotion is permanent. The logical strong maximum remains 2^64-1; promotion failure Aborts, and an increment at the logical maximum Aborts before modification.

Table weak count includes one internal guard until complete object destruction and allocation release. Its u64 maximum is 2^64-1 including the guard. External Weak duplication/downgrade increments it explicitly; Move does not. The last strong release sets strong to zero, destroys the complete payload, frees the original object allocation, then releases the guard. The last weak release frees only the side table. An expired table's object pointer is never accessed; upgrade must first secure a positive strong count. The table's own cleanup does not inspect that pointer. No table reuse is permitted while a live Weak retains it.

rc uses non-atomic operations. arc uses atomic control and table counts, with no mixed atomic/non-atomic count accesses to the same allocation. Inline updates use CAS on the complete control word so they cannot overwrite a published table pointer. Promotion prepares a private table with the observed strong count and weak=1, then publishes it with AcqRel CAS; failure retries with the current count or frees the losing unpublished table and uses the winner. Every path resolving a published table performs an Acquire observation before table access. Promotion leaves logical strong responsibility unchanged.

Strong and weak increments use checked CAS (Relaxed for duplication; Acquire on successful upgrade). Upgrade returns None on zero and never accesses the object before succeeding. Inline strong decrement uses Release CAS; side strong/weak decrement uses Release fetch_sub. Each last decrement performs an Acquire fence before the corresponding destruction/free. CAS failure paths must obey LLVM ordering constraints and re-resolve a changed representation. The destruction guard remains held through payload cleanup, including destruction of any contained Weak values. Count one never grants exclusive payload access. Allocation, user destruction, and source thread-safety have no lock-free or concurrency guarantee from this protocol.

#### 21.3. Generic code generation

##### 21.3.1. Policy and sharing conditions

**Instantiation** binds generic arguments. **Explicit full specialization** selects a user-written implementation under [function specialization](#88-explicit-full-function-specialization). **Automatic specialization** generates Type-specific code while preserving that selected implementation's meaning. Instantiation does not require a separate machine-code body.

Use **shared implementations with automatic specialization where needed**:

| Policy | Condition |
| --- | --- |
| Required separation | Shared code, metadata, helpers, and adapters cannot preserve a semantic or representation difference |
| Default automatic specialization | Scalar value representation or operations; use Type-specific code or adapters under the policies below |
| Optional automatic specialization | Inlining, removal of indirect calls, or other optimizations preserve meaning |

Sharing must preserve the selected ordinary or explicit implementation, environment-selected syntax, resolved overloads and Contract mappings; parameter/result representations, layout and receiver adjustments; and Copy/Move/Consume, borrowing, reference counts, failure, and destruction paths. Differences may be moved to common-format metadata or helpers, but must not be lost. Equal representation is insufficient to share implementations with different observable behavior.

Metadata executes validated operations; it does not defer source legality, Origin/Loan checks, or environment selection to runtime. Sharing must preserve evaluation order and count, results, side effects, ownership, and failure behavior. It does not expand runtime Pattern exhaustiveness proofs or mandatory unreachable-pattern diagnostics.

##### 21.3.2. Identity and generation keys

The following are logical distinctions, not prescribed APIs or binary encodings:

| Identity or key | Purpose and retained information |
| --- | --- |
| Complete static Type identity | Normalized nominal identity, all Semantics layers, nested Types, and Origins; used for semantic checks alongside Loan information |
| Implementation Selection Key | Original function declaration Identity plus ordered ArgKey / LengthKey values for all its own generic slots; select a matching explicit implementation from the closed definition-side set |
| Runtime Object Type Identity | CoreId of the actual payload, with the special root-handle rule in [object metadata](#2121-type-identity-and-descriptors) |
| Code Generation Key | Distinguish the selected implementation's meaning and representation, scalar operations, calling convention, embedded operations, and ownership effects that change generated code |
| Code Cache Key | Generation key plus declaration/Kotonoha identity and version, closed specialization set and selection mappings, selected body and dependency content identities, target, compiler build, and optimization/generation settings |

Form, normalize, and validate complete Type arguments before computing an Origin-erased key. Ordinary bindings and semantic metadata retain every Origin and Loan. An Implementation Selection Key retains outer handle Semantics; it is not Runtime Object Type Identity. Origin differences alone neither select another implementation nor require duplicated machine code.

A Code Generation Key may omit concrete Type differences handled entirely through metadata or helpers, provided their correspondence remains available. Embedded scalar operations distinguish `i32` from `u32` even when their sizes match. Any merging across selected implementations must prove preservation of their observable behavior. None of these keys replaces Constraint, Type, Origin, or Loan validation.

##### 21.3.3. Shared operations and Type policies

Shared inputs must expose the required information in a common form, directly or through adapters:

| Purpose | Logical information or operation |
| --- | --- |
| Layout | Size, alignment, stride, and required field offsets |
| Initialization | Move-initialize; copy-initialize only where Copy is permitted |
| Destruction and replacement | Destroy; move-assign/copy-assign when appropriate |
| Type-dependent operations | Member/Contract implementation, receiver adjustment, required identity and dynamic destruction entries |

Initialization writes uninitialized storage; Move transfers responsibility and Copy preserves the source. Destroy only initialized parts whose responsibility remains. Combined assignment helpers must preserve [assignment order](#137-assignment), including securing the right side before evaluating and replacing the left side, and its failure behavior. This is not a requirement for function-pointer tables, a new Copy capability, user-defined Copy, or unrestricted byte copying. Values may be addressed without mandatory heap boxing. Concrete layouts and identities survive sharing; runtime Origin representations are not required.

Let P be an integer Type, `f32`, `f64`, `bool`, or `char`. `string`, Unit, Never, and nominal wrappers containing scalars are not P. Normalize transparent aliases and redundant owner notation without erasing nominal identity.

| Type or operation | Default generation policy |
| --- | --- |
| P passing, results, storage, and temporary Copy/Move | Type-specific automatic specialization or adapters |
| P arithmetic, conversion, and comparison | Type-correct instructions or helpers; automatic specialization by default |
| Only passing or returning `ref/P` or `uniq/P` | Share if representation and borrow operations permit |
| Load/store through `ref/P` or `uniq/P` | Type-correct instructions or helpers; automatic specialization by default |
| `string`, struct, enum, Tuple, and non-scalar value borrows | Share through metadata when the required operations permit |
| `unsafe/T` | Share while retaining pointee size, access operations, and Unsafe preconditions |
| Other existing Type forms | Apply the same representation and operation conditions; introduce no new Type form |

No policy gives writes through `ref` or implicit Copy through `uniq`. Reference comparison follows the allowed language operation, not an assumption that pointer comparison is sufficient. A helper may isolate Type-specific operations so the caller remains shared. A nested scalar, as in `Box<i32>`, does not force whole-body duplication, nor does a fixed `i32` in a declaration require expanding unrelated slots.

Keep `obj`, `rc`, `arc`, `objref`, and `objuniq` separate in ownership-aware intermediate representations. After making ownership, borrowing, and count operations explicit, equivalent low-level code may merge. Sharing within one Semantics still requires a common form for View Targets, preserving member calls, receiver adjustment, dynamic identity, destruction/release, and non-atomic versus atomic counts. Do not assume one-word handles or identical callees.

Unit retains §3.1.5’s zero-sized logical storage even if the calling convention omits transfer or reserves padding; preserve effects and required places without promising distinct addresses or slots. Never has no value: non-completing argument evaluation prevents the call but preserves earlier effects/transfers. Never-returning functions do not return normally; unreachable code still receives required static checks.

```kimi
func pair<P, T>(number: P, value: T) -> (P, T) => (number, value)
// Without explicit specializations, pair<i32, A> and pair<i32, B> may share
// a body if metadata preserves T operations and Tuple layout. Selection keys
// distinguish A/B; a generation key need not embed that metadata-only difference.

func keepBorrow<T>(value: ref/T) -> ref/T from value => value
// No referent access: compatible borrow representations can share immediately.
```

##### 21.3.4. Artifacts, verification, and invalidation

The defining Kotonoha collects and closes its explicit specialization set under [declaration ownership](#883-selection-and-declaration-ownership). Artifacts retain the original declarations, set and mappings, complete contracts, body information, and legitimate [deferred obligations](#810-generic-body-checking-and-deferred-obligations). Preserve defining Symbols, source environments, dependencies, and verification deadlines; private dependencies do not become caller-addressable names.

Check every environment-selected explicit specialization's target, Type arguments, Constraints, inherited contract, and body before finalizing the defining artifact, even if unused or unreachable. Excluded declarations follow [excluded-syntax rules](#195-diagnostics-and-excluded-syntax). A validated unused body need not have machine code; a validated body need not be rechecked at every call. Each use still checks its own contract and lifetime conditions.

Consumers use the artifact's closed set and may not register additions. Calls from shared generic code and function references must reach the implementation selected by their static arguments, independently of separate compilation, optimization, or LTO. Callee information passed at runtime, if used, must still come from that static selection, not from the value's Dynamic Type. No particular propagation or compilation mechanism is required.

**Common dependency validation.** Record the declaration content on which inherited-Name checks, inherited conformance, effect summaries/public ObjectCompatible, and selection/generation depend. Include the completed contents of Containers used to conclude that a Name or specialization is absent, plus environment selection and verification-rule identity. A list of referenced Members alone cannot detect additions. Validate dependency content and Code Cache Keys, not only package versions.

Before reuse, compare recorded dependencies with the actual definitions. A mismatch invalidates affected judgments and generated results, including calls formerly mapped to the ordinary body. Revalidate from the affected stage: accept and update results that still satisfy their requirements; diagnose lost requirements at the dependent declaration, conformance, constraint use, or call. If the required information is unavailable, reject the artifact with a rebuild diagnostic. Never reuse stale proofs, mix old/new mappings, or choose by load order.

Name additions, access expansion, Signature changes, Proven withdrawal, and specialization additions/deletions/body edits can break existing consumers, even without changing a public Type. This is an API compatibility classification, not a requirement that an upstream build know or diagnose all downstream code. Both body-derived and specialization-derived changes use the same validation process. Missing correctness information cannot justify an invalid shared fallback.

Artifacts follow [source and binary interfaces](#183-source-artifacts-and-binary-interfaces); no Koto serialization, fixed ABI, or exact artifact encoding is required.

##### 21.3.5. Generation limits and code merging

Generate needed bodies without enumerating the Cartesian product of Type arguments. Under the same declaration/build conditions, deduplicate generation plans by Code Generation Key; recursion to a key already being generated refers to that plan. Reuse existing code only after Code Cache Key validation.

Limit growing automatic specialization, including Type-growing recursion, with a finite optimization budget and use a verified shared fallback when it preserves the selected implementation. Required separation needs no shared fallback. Metadata/helpers also require finite generation. Budgets cannot change source validity or replace explicit specialization with the ordinary body. Scalar defaults promise neither one final body per Type nor public ABI entries.

A shared path cannot discharge unresolved Type, ownership, or layout obligations. Reject infinite value layout under existing rules. Diagnose unresolved obligations separately from resource exhaustion; Type-growing recursion is not otherwise banned by this section.

Equivalent code may merge only while preserving observable Type/function identity and the selected implementation's results, side effects, failure, ownership, and destruction behavior. Separate entry points may share an internal body. No new function-address comparison, reflection, or stack-trace guarantee follows.

Exact precompilation, callee-information passing, artifact/ABI formats, inlining budgets, and sharing mechanisms remain implementation-design boundaries. Current common Function Type and runtime-contract restrictions are unchanged; ordinary-body legality and remaining representation obligations follow [universal generic verification](#810-generic-body-checking-and-deferred-obligations).

#### 21.4. Checked lowering and internal ABI

##### 21.4.1. Input and generation set

Lowering consumes finalized, read-only semantic information: complete Types, selected implementations/callees, acquisition and evaluation order, CFG edges, initialization/consumption and destruction responsibility, validated Loans/Origins/lifetimes, cleanup plans, source locations, and supported layout/ABI/runtime operations. It does not re-resolve names or reconsider ownership legality. A separate target-independent Lowered IR is optional; direct LLVM generation from verified CFG and semantic information is allowed.

Share stable Identities without cloning the syntax tree. Compute and reuse these distinct records:

| Record | Information |
| --- | --- |
| TypeLayout | Mode, size/alignment/stride, LLVM storage Type, Field/base Identity to offset mapping |
| ValueLowering | Computation/storage representations, conversions, valid-bit constraints |
| FunctionAbi | Physical signature, argument/result slots, calling convention, ABI and justified optimization attributes |
| CleanupPlan | Edge-specific initialized/moved parts, ownership, registration, logical destruction order |

Unresolved Types/obligations, missing cleanup, or unsupported selected operations fail generation. Zero, undef, poison, unreachable, and freeze are not substitutes for unresolved language semantics.

Before optimization, include all selected project implementation bodies without remaining outer/function generic arguments, the startup body, and their required concrete generic implementations, Core, cleanup, and runtime helpers. Foreign imports have no emitted body. Use a worklist keyed by declaration Identity, concrete arguments, and selected implementation. Ordinary recursion reuses a declaration; unbounded distinct instantiations receive a diagnostic at a documented compilation-resource limit.

Unused nongeneric bodies and unexecuted branches within generated bodies still receive unsupported-feature diagnostics; optimization cannot hide them. Excluded syntax is outside this set. Existing semantic verification of uninstantiated generic bodies is unchanged. Unsupported generic sharing/metadata must not silently become a different semantic implementation. LLVM may remove definitions/paths only after these checks.

##### 21.4.2. Physical function signatures

The initial internal ABI is versioned, module-local, and uses LLVM ccc. It applies to user functions, Core, and private runtime helpers in Application and inspection-only Library output. It is not the C ABI or a published inter-module ABI. Derive definitions and calls from the same FunctionAbi.

| Language value | Initial internal passing |
| --- | --- |
| Integers, floats, bool, char, raw pointer | Direct computation Type from §21.1.4 |
| Struct, Tuple, fixed array, string, enum under `owner` Semantics | ptr to a dedicated initialized argument slot |
| Aggregate result | First argument is ptr to caller-provided uninitialized result storage; LLVM result is void |
| Unit | Omit its physical argument slot; return void |
| Never result | No result storage; void/noreturn and nonreturning CFG |
| Object handle and object borrow | Direct ptr under §21.2.3, with an explicit implemented ValueLowering and ownership operations |
| `Core.Weak<S>` under `owner` Semantics | Aggregate argument/result slot rules despite its one-word storage; explicit implemented ValueLowering and cleanup required |
| Value borrow, common function value | Requires an explicit implemented ValueLowering; never assume one ptr |

Preserve logical parameter order, including the analyzed receiver slot. A value receiver is evaluated once before explicit arguments (§7.3); a Type-qualified unbound call supplies self in ordinary argument order. Hidden diagnostic context follows ordinary physical parameters. Argument and result pointers are ordinary ptr parameters: do not add byval or sret automatically. A future ABI change must update both sides.

```kimi
group Samples
    func isPositive(value: i32) -> bool => value > 0
    func echo(text: string) -> string => text
```

```llvm
; Fragments omit profile attributes. echo needs an internal definition in final IR.
define internal i1 @kimi_is_positive(i32 %value) {
entry:
  %result = icmp sgt i32 %value, 0
  ret i1 %result
}
; Physical echo signature: void(ptr %result_storage, ptr %text_storage).
```

##### 21.4.3. Slot responsibility and normal return

Acquire arguments once in source order. Copy preserves its source; Move transfers responsibility into the argument temporary. Allocating a slot is not Copy. A Copy source cannot share a slot that the callee may consume or modify.

| Point | Argument temporaries | Aggregate result slot |
| --- | --- | --- |
| Acquisition / just before call | Acquired values are caller responsibility | Uninitialized |
| Callee entry | Responsibility transfers to callee | Uninitialized |
| Result secured, cleanup running | Remaining parts are callee responsibility | Initialized, still callee responsibility |
| Normal return edge | Consumed or cleaned; caller must not destroy again | Responsibility transfers to caller; caller's Initialized fact begins here |
| Abort or nontermination | No later normal cleanup/return | Caller neither reads nor destroys it |

Transfers during argument acquisition use the caller's cleanup plan for already acquired temporaries. A callee that was never entered cannot perform their cleanup. The callee never frees caller-owned stack storage. Securing a result before cleanup does not make it available to the caller: cleanup must finish before return. If it Aborts or diverges, later cleanup and result delivery do not occur.

If a zero-sized value needs an address, initially use a one-byte substitute slot with the original Type's alignment, such as `alloca i8, align 8`. This changes neither size nor stride. Shared substitute slots meet maximum alignment and remain alive through the last use; Place Identities retain separate state and responsibility. Their existence grants no positive dereferenceable guarantee for the semantic zero-byte value.

##### 21.4.4. Values, Places, and control flow

Keep acquisition, Place evaluation, first placement, and replacement distinct. First placement writes uninitialized storage without destroying an old value. Replacement secures the right side, evaluates the left Place, destroys its remaining old parts, then places the new value. A setter receives the secured value instead of an automatic old-value destruction/store; simple assignment does not call the final target's getter. Compound assignment remains target-first (§13.7).

Standard stored access uses TypeLayout; custom/computed/required access retains the selected callable contract, including restrictions after witness optimization. Self-assignment cannot be removed based only on address equality.

```kimi
values[index()] = makeValue()
// makeValue -> index -> old-value cleanup -> placement
let ok = divisor != 0 and (100 / divisor > 1)
// Division and its checks run only on the true edge of divisor != 0.
```

Use CFG edges for short circuit, branches, match guards, and loops. Evaluate conditions/subjects once. Scalar joins use phi from actual normal predecessor blocks; aggregate joins initialize a common uninitialized result slot only on arriving edges. Unit needs no phi, and Never supplies no fictional value/edge. phi predecessors are the blocks after cleanup. select may replace conditional evaluation only when evaluating both alternatives early is proven legal.

A non-Never Expression Type does not guarantee a normal CFG predecessor: required cleanup may prevent delivery (§14.9). Do not manufacture an incoming value or change the checked Type to Never in that case.

Direct construction into an uninitialized final slot may remove intermediate transfers only if evaluation order, aliasing, Loans, storage identity/lifetime, intermediate observations, partial initialization, and cleanup remain unchanged. Otherwise keep an independent temporary; never overwrite a live replacement target early.

##### 21.4.5. Cleanup and physical transfer

Each scope-leaving edge secures its result, performs exactly the cleanup of scopes left, then delivers the result/transfer. Follow §16.2's inner-to-outer, reverse lexical order of locals and defer; process only registered defer and initialized parts still owned. Use edge-known state directly; introduce runtime flags only where paths must remain distinguishable after a join. Equal cleanup sequences may share code when state and destination match. A heap stack or closure per defer is not required. Never infer partial construction/Move/base state from value bits or addresses, reorder cleanup by physical offsets, or move destruction to last use.

Abort stops cleanup; nontermination blocks subsequent cleanup and delivery. Lack of visible side effects does not license removing a language-permitted infinite loop.

Prefer direct Field/scalar operations and avoid slots created only to use a memory intrinsic. For semantically valid bulk transfers, prefer nonvolatile llvm.memcpy on nonoverlapping ranges, llvm.memmove if overlap is permitted, and llvm.memset for required byte filling. Preserve alignment and constant lengths. These operations do not authorize Copy/Move, constructors, or whole-value reads of partially initialized/moved data. Zero-size data transfers may disappear while state updates and cleanup remain. LLVM chooses expansion/vectorization/libcalls; memcpy.inline is reserved for specific constant-length sites that require no libcall.

```llvm
declare void @llvm.memcpy.p0.p0.i64(ptr, ptr, i64, i1 immarg)
; Inside a function: valid, nonoverlapping 24-byte ranges.
call void @llvm.memcpy.p0.p0.i64(ptr align 8 %dst, ptr align 8 %src, i64 24, i1 false)
```

#### 21.5. LLVM Windows x64 profile

##### 21.5.1. Target and optimization

The initial profile is **windows-x64-v1**: LLVM **22.1.8**, target **x86_64-pc-windows-msvc**, CPU **x86-64**, features **+sse2**, relocation model **pic**, code model **small**, asynchronous unwind tables. Use the target's verified DataLayout (§21.1). Do not infer extra features from the build machine, require AVX, or compensate for absolute 32-bit data references with /FIXED or a low image base. Diagnose static artifacts outside the code-model range separately from heap size limits.

Every generated function definition, including entry and runtime, carries matching target-cpu, target-features, denormal-fp-math=ieee,ieee, and uwtable(async) (LLVM uwtable is equivalent). Emit:

```llvm
target triple = "x86_64-pc-windows-msvc"
; Also emit the verified target datalayout.
!llvm.module.flags = !{!0}
!0 = !{i32 8, !"PIC Level", i32 2}
```

The compiler emits pre-optimization IR. O0 skips the general IR optimization pipeline and uses llc -O0; default O2 uses opt default<O2> followed by llc -O2. opt reads CPU/features from function attributes; do not duplicate them as opt -mcpu/-mattr options. Match llc and manifest settings. Verify before and after optimization.

Use LLVM for inlining, constant propagation, dead-code elimination, SROA, mem2reg, instruction selection, and register allocation. No default O3, unconditional alwaysinline, loop unrolling, or redundant general SSA optimizer is required. Internal ABI changes by whole-module optimization are allowed only when all uses and semantics remain consistent; preserve external ABI and observable storage. O0/O2 cannot change acceptance, checks, cleanup, or nontermination.

##### 21.5.2. Module, symbols, and caches

One project/target emits one .ll with target information, private constants, required external declarations, internal runtime/Core/user/cleanup definitions, and Application entry (§22.2). Source public visibility is not native export.

| Symbol | Linkage |
| --- | --- |
| User functions, public main, implicit body, Core, cleanup, runtime helpers | internal definitions in both output kinds |
| Application __kimi_start | external definition; absent in Library |
| Windows APIs / LibraryImport | external declarations; dllimport follows library kind (§20.8.2) |
| Backend marker _fltused | Strong external data definition (§21.5.7) |
| Internal constants | private; literal sharing follows §21.5.6 |

Emit one definition for each generated function, without a same-name declare. Explanatory signature-only fragments are not complete modules. Mangle source names with Kotonoha/declaration Identity, arguments, and implementation selection.

Use one module symbol table. Same-named external declarations share only if physical Type, calling convention, ABI attributes, dllimport, and resolved library all agree; otherwise diagnose. Function/data and declaration/generated-definition collisions are errors. Reserve __kimi_ for compiler internals, llvm. for LLVM, and _fltused, __chkstk, memcpy, memmove, memset for profile supplies; reject these external names in user LibraryImport. Match exact ABI symbol names. Quote/escape LLVM identifiers and UTF-8 bytes; never insert raw source strings into IR. Shared ptr signatures do not merge source unsafe contracts; attach only guarantees true for every use.

Deterministic output and cache keys retain compiler/layout/internal-ABI versions, target/DataLayout/codegen settings, backend package version/hash, selected fragments/generated sources, complete arguments and selected implementations, cleanup, callees, and helper dependencies. Size/alignment alone is never a sufficient key. Do not depend on absolute working directories, host locale, enumeration order, or host CPU.

##### 21.5.3. Checked instructions and raw pointers

Preserve existing arithmetic, conversion, indexing, and failure order. nsw/nuw, poison, or undefined behavior cannot implement a required Abort check.

| Operation | Initial lowering |
| --- | --- |
| Integer add/subtract/multiply | Signed/unsigned overflow intrinsic or equivalent result-plus-overflow check; Abort on failure |
| Negation, increment/decrement, compound assignment | Check first; commit the write only on success |
| Integer division/remainder | Check zero divisor and signed minimum / -1 before the instruction |
| Shift | Check count in its original Type: 0 <= count < left width, then convert; ashr for signed right shift, lshr for unsigned, shl without treating discarded bits as arithmetic overflow |
| Integer conversion | Check destination range before extension/truncation |
| Float to integer | Ordered range checks below, then fptosi/fptoui only on success |
| Integer to float / float width conversion | Required rounding; detect finite-to-infinity failure |
| Array/index/Range | Required bounds checks before successful address calculation |

Constant evaluation follows §17.3.4. Prove success before removing a check; folding a failing path to Abort or removing an unreachable path is allowed.

```llvm
declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
; Inside a function:
%pair = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %a, i32 %b)
%sum = extractvalue { i32, i1 } %pair, 0
%overflow = extractvalue { i32, i1 } %pair, 1
br i1 %overflow, label %abort_overflow, label %success
; abort_overflow calls the generated noreturn Abort helper with the source site.
```

**Float-to-integer bounds.** For source precision p (24 for f32, 53 for f64) and destination width N = 8,16,32,64 (64 for isize/usize), test x directly:

| Destination | Lower bound | Upper bound |
| --- | --- | --- |
| iN, N <= p | x > -2^(N-1) - 1 | x < 2^(N-1) |
| iN, N > p | x >= -2^(N-1) | x < 2^(N-1) |
| uN | x > -1 | x < 2^N |

All constants are exact in the source format. Ordered comparisons reject NaN/infinities. Only then execute fptosi/fptoui, which truncates toward zero. No trunc/truncf helper or llvm.trunc is needed; do not speculatively convert and hide poison with select. Thus -128.75 to i8 yields -128, -0.75 to u8 yields 0, while -1 to u8 and f32 2147483648 to i32 Abort.

**128-bit subset.** Support storage/acquisition, internal arguments/results, comparisons, bit operations, shifts, integer conversions, and checked add/subtract/negate/increment/decrement/multiply, subject to verification that LLVM 22.1.8 needs no unsupplied helper. Diagnose i128/u128 division/remainder and both directions of f32/f64 conversion, including compound assignments, before optimization even in unused bodies/branches. Required constant evaluation and fitted constant storage are separate. __divti3, __udivti3, __modti3, __umodti3 and __fix*ti/__float*ti* helpers are not supplied initially. Verify actual multiplication expansion rather than assuming a helper from its name.

**Raw pointer operations.** Use the same address-space-0 ptr for allowed pointer-Type casts, icmp eq/ne for same-Type equality/null tests, ptrtoint to i64 and inttoptr from i64 for usize conversions. Use a storage-Type GEP with i64 index for p + n and sub i64 0, n for p - n; no inbounds or nsw/nuw in the initial form. Check GEP spacing against positive stride(T). Zero displacement preserves null as well as other pointers. Arithmetic alone proves neither alignment nor initialization. These operations retain §5's unsafe allocation/provenance, mathematical displacement, and no-wrap conditions; violations need not Abort. Integer reconstruction creates no extra dereference permission. Typed access still needs valid range, alignment, permissions, initialization, and replacement legality. Runtime buffer arithmetic has its own checked contract (§22.5.3).

##### 21.5.4. Floating-point environment

Use ordinary fadd/fsub/fmul/fdiv/fneg/fcmp and conversions, without fast-math, reassociation, approximation, or FP fusion. Preserve §13's NaN, infinity, signed-zero, subnormal, rounding, and Equatable distinctions. denormal-fp-math is ieee,ieee, including any f32 override.

Require Windows x64 ABI-standard MXCSR control state: nearest/ties-even, DAZ off, FTZ off, hardware FP exceptions masked. This is the startup and foreign-code connection contract, also assumed by inspection-only Library IR. External code that temporarily changes control bits restores them before normal return; deliberate environment-changing APIs are unsupported. External initialization follows the same condition. No startup MXCSR setter, per-call save/restore adapter, individual preservation flag, constrained intrinsic, strictfp, or FP-environment noinline is generated.

MXCSR status bits are volatile and not exposed. External code must not depend on status flags left by Kimigayo calculations. Violating these boundary conditions gives no result/cleanup guarantee or automatic repair. Future export, callback, thread entry, dynamic rounding, and exception observation require new contracts. Normal FP optimization may preserve language results and explicit Abort checks without preserving hardware exception status.

##### 21.5.5. Storage, attributes, and unwind information

Keep address-free scalar values in SSA, using phi at joins; mutable locals may use promotable fixed slots. Put required fixed-size allocas in function entry before calls with explicit alignment, while initialization and cleanup stay at their source execution points. Dynamic stack allocation is initially unsupported. Reuse storage only after cleanup and final reference use. Optional lifetime.start/end markers follow actual storage lifetime, not an Origin spelling or Move alone; omit unproven markers.

Prove each optimization attribute separately: actual alignment for align; guaranteed non-nullness for nonnull; valid byte range for dereferenceable; LLVM alias conditions for noalias, not merely uniq; defined bits/no poison for noundef, including padding in any coercion; full GEP conditions for inbounds; no signed/unsigned overflow for nsw/nuw. Do not apply mustprogress, willreturn, or loop.mustprogress uniformly to ordinary functions or loops.

All generated definitions use uwtable(async). Let LLVM produce required .pdata/.xdata, prologue/epilogue and register/stack recovery information, including decisions for optimized leaf functions. This enables OS stack recovery/walking, not language exceptions, Abort cleanup, or foreign unwind permission. Treat nounwind separately; the initial emitter does not infer it merely from LibraryImport's no-unwind contract and generates no exception-catching landingpad. Native assembly needs appropriate Windows unwind information for its own stack/nonvolatile-register operations.

##### 21.5.6. Constants

Retain exact integer magnitudes and decimal values through fitting; round once to the selected f32/f64 and emit its fitted bits using exact LLVM 22.1.8 syntax. Do not round through host f64 or culture-dependent formatting. Verify bit equality after LLVM rereading (for example, 0.1 fitted to f32 has bits 0x3DCCCCCD).

After source newline/escape processing, encode strings as UTF-8 with byte lengths; embedded NUL is data, with no automatic terminator. Preserve interpolation evaluation order. Share identical byte sequences within the module as private unnamed_addr constants; backing addresses do not define string equality/identity. An empty literal is Static/null/length zero with no allocation. Non-Copy ownership remains unchanged (§22.5.5).

```llvm
; UTF-8 for U+3042; length 3, no trailing NUL.
@kimi_text_a = private unnamed_addr constant [3 x i8] c"\E3\81\82", align 1
```

##### 21.5.7. Backend support supply

Memory intrinsics may become external libcalls. Supply memcpy, memmove, memset and Windows x64 assembly __chkstk from the versioned native COFF static archive **kimi_backend_windows_x64_v1.lib**, logical name **kimi_backend**. Do not generate their loop bodies in .ll or use bitcode/LTO members; keep them outside O2 to avoid recursive libcalls. optnone alone is insufficient. Prefer one strong symbol per archive member.

Memory helpers meet Windows x64 C argument/result contracts, including both overlap directions for memmove. __chkstk uses the backend's special ABI, not an ordinary C function: validate the RAX size input/preservation, stack and register restoration, and guard-page probing against emitted calls. Never disable required probes or substitute a similar name from another ABI. All helpers meet this profile's CPU, MXCSR, and unwind contracts, with no extra external dependencies, CRT startup, or dynamic initialization.

Do not emit ssp/sspstrong/sspreq initially. Stack-protector cookies/failure handling and other arithmetic helpers require separately validated supplies before enabling dependent features.

Every Application and Library module, regardless of FP use or optimization, supplies exactly one strong external data definition:

```llvm
@_fltused = global i32 0, align 4
```

This backend marker is not CRT initialization state. No dllimport, weak, or common definition is allowed; other inputs, including the backend archive, must not define it.

The compiler's profile catalog fixes packageId=kimi-backend-windows-x64, abiVersion=1, the actual archive's SHA-256, the profile/LLVM/CPU/FP/unwind contract, and providedSymbols=[__chkstk, memcpy, memmove, memset]. packageVersion comes from the shared compiler/backend release in Directory.Build.props (§20.8.7). Update ABI version when symbol/call contracts change. Filename/path equality is insufficient. If version/ABI/hash are not established, do not claim successful generation using a placeholder supply.

Record the archive as a profile-wide link input even when no known reference currently needs it; unused archive members need not link. Generated _fltused and externally supplied symbols have separate manifest classifications (§20.8.3). Later LLVM may add/remove references: inspect actual object undefined symbols and verify providers. Unknown/unsupplied dependencies fail adoption or linking, never receive empty stub helpers. This supply does not add to the six runtime operations or seven Windows APIs.

### 22. Core, program execution, and foreign functions

This chapter defines the required declarations of the Core Kotonoha, process startup and shutdown, the foreign-call boundary, and minimal standard output. Here, Core names the foundation Kotonoha, not a Type component.

#### 22.1. Required Core declarations

Every Compilation binds exactly one compiler-compatible **Core Kotonoha**, using the reserved direct reference name Core. The declarations below are public at that Kotonoha's root and available through default aliases. Qualified paths such as `::Core.Option<T>` identify them regardless of local shadowing. Compiler metadata records their originating Kotonoha/version and Symbol Identities; a same-spelled user declaration or replacement alias never receives their special behavior. Reject a missing, duplicate, or incompatible Core definition before finalization. The compiler may synthesize these definitions, but synthesized and loaded definitions must have the same language identities and contracts. Core itself is built with these identities designated by the compiler.

This is the minimal set named by language rules, not a promise of a general standard library:

| Declaration | Required shape or operation |
| --- | --- |
| `Option<T>` | enum with Some(T), None in that order; Self is Copy with condition-atom set {T is Core.Copy} |
| `Result<T,E>` | enum with Ok(T), Err(E) in that order; Self is Copy with condition-atom set {T is Core.Copy, E is Core.Copy} |
| `Weak<S>` | Compiler-managed Non-Copy value Core for complete rc/arc handle S; empty, downgrade, upgrade and explicit duplication semantics in §3.2.2 and §13.5.9; final operation spellings deferred |
| `Array<T>` | Non-Copy owning dynamic sequence over a valid complete T; no Owned requirement; public read-only length: isize and indices: ResolvedRange; checked indexing under §4.6, literals, and consuming Iterable conformance |
| `Index` | Copy, Owned, Equatable direction/offset value; constructor, read-only fields, resolve/tryResolve under §4.6.2 and §4.6.4 |
| `Range` | Copy, Owned, Equatable unresolved boundaries; syntax construction, read-only fields, resolve/tryResolve under §4.6.3 and §4.6.4; not Iterable |
| `ResolvedRange` | Copy, Owned, Equatable validated interval; constructor, read-only fields, and `Iterable` with associated Type `Element = isize` under §4.6.3 |
| `Slice<T>` origin source | Copy shared view with all public operations in §4.6.6; implements `Iterable` with associated Type `Element = ref/T from source`; backing Origin is explicit or inferred under ordinary rules |
| `Dictionary<K,V>` | Non-Copy owning collection over valid complete K/V requiring K is Equatable; no Owned requirement; literal construction and existing-key indexing, public read-only length: isize, and consuming Iterable conformance |
| `Stringify` | `func stringify(self: ref/Self) -> string`; returns an independent owned string |
| `Equatable` | `func equals(self: ref/Self, other: ref/Self) -> bool` |
| `Comparable: Equatable` | `func compare(self: ref/Self, other: ref/Self) -> i32`; negative/zero/positive for less/equal/greater |
| `Iterator` | `associate Element`; `func next(self: uniq/Self) -> Option<Self.Element>` |
| `Iterable` | `associate Element`; `associate Iterator is ::Core.Iterator`; `Self.Iterator.Element is Self.Element`; `func iterate(self: owner/Self) -> Self.Iterator` |
| Copy, Owned, Callable | Compiler-intrinsic requirement identities with exactly their existing derivation, ownership, and call rules; they are not ordinary user-implementable replacements |
| Object ownership intrinsics | Creation of `obj`/`rc`/`arc` and duplication of `rc`/`arc` strong owners with exactly the contracts in §13.5.8; final source names/signatures remain deferred |
| `writeLine` | `public func writeLine(text: string) -> ()`; standard-output operation under §22.4, with ordinary owned-argument acquisition |

Iterator and Iterable are static, non-lending Contracts. Their Element requirement is the sole complete-Type exception (§8.4.3) and may bind ref/T from an existing external source; Iterable.Iterator still binds a Core. Table signatures follow normal associated-Type, receiver, result-Origin, and lifetime rules.

Fixed arrays implement Iterable with Element = T. Owning Array/Dictionary iterators retain and destroy unyielded elements. ResolvedRange and Slice use concrete Core iterator identities with §4.6’s element Types and dependencies: range iterators store position/end; Slice iterators store a copied handle, position, and external source Loan. Neither owns yielded elements, and both stay exhausted after None. Dependent Types preserve source dependencies through associated Types and Option payloads. Receiving next’s result extends no lifetime. These requirements add no public iterator constructors or other changes to associated-requirement kinds.

The primitive keyword string denotes the compiler's UTF-8 string Core, not a shadowable alias; its required operations here are literal/interpolation construction, concatenation, comparison, and Stringify. No character indexer, mutable string buffer, allocator, or formatting options are implied. Fixed-array syntax and layout follow [sequence Types](#4-arrays-indexing-and-slices); metadata, indexed Place acquisition, and shared reading follow [indexing and slicing](#46-indexing-and-slicing).

For Option/Result Copy conditions, compare atom sets using §8.7's proposition identity and conjunction elimination. T/E denote the corresponding parameter slots and Core.Copy the recognized Symbol. Order, transparent grouping, and duplicate atoms do not change the set; missing/unconditional Copy, missing/extra atoms, or different identities are incompatible. Retain all other required-shape checks without general logical-equivalence reasoning. Generated sources may use canonical T, E order, but loaded Core definitions cannot be required to use that order.

Option/Result Copy and Owned follow ordinary enum rules; no extra copying is introduced. A changed Core contract invalidates dependent capability, acquisition, and generation results under §21.3.4. Unchanged Case order and payload structure do not establish binary compatibility with older Core artifacts.

Array/Dictionary contents, generic enum payloads, and fixed-array elements preserve complete Type/Origin/Loan dependencies under §15.4. Array's Owned classification follows T, Dictionary's follows K and V, independently of runtime contents; both remain Non-Copy. No container grants permission to hide dependencies or extend a referent's lifetime. Checked-cast designs use the required Core Option Identity despite deferred View syntax. Dictionary need not expose hashing. Further allocation/mutation and library APIs are separately specified.

#### 22.2. Program startup and static initialization

##### 22.2.1. Startup selection

After directive selection, Mods, and Binding, an Application must select exactly one of:

- One SourceDocument with top-level runtime body items.
- One eligible root-level `public func main() -> ()`.

Reject mixed forms, multiple candidate documents/mains, or no candidate. Search the current project, not dependencies; enumeration order and optimization never select the winner. EntrySource is not an initial selection mechanism, and naming an empty document cannot make an Application valid.

Determine HasTopLevelRuntimeBodyItem once per document from selected root items, without descending into functions or Containers. This classification does not require that machine instructions survive:

| Root item | Counts as a runtime body item |
| --- | --- |
| Expression/control expression, unsafe/defer/require statement | Yes, including Unit and removable expressions |
| Local let/var | Yes, even without an initializer |
| Function declaration, including main | No |
| Container or alias | No |
| Attribute argument, Type expression, constant evaluation, Mod execution itself | No |
| Excluded syntax | No |
| Selected legal generated item | Classify the resulting item by these rules |

Top-level bindings remain SourceDocument-local, not static Properties. Container static Properties do not count and keep first-access initialization. Generated items must be legal in their target context; neither generation nor startup classification expands that grammar.

Execute the chosen document's body items in source order with its source scope, CodeContext, local-function visibility, lifetimes, and cleanup. Lower it to a private internal function, without synthesizing public main. This physical function does not create a source-level return target (§14.5.3).

```kimi
// Implicit startup; the uninitialized local itself also counts.
let pending: i32
::Core.writeLine("Hello, world!")
```

A minimal intentionally empty Application is the Unit expression `()`. Empty files and declaration-only files supply no implicit body.

##### 22.2.2. Explicit main and Library

An Application's explicit main is exactly lowercase main, public, directly at the source root, with no parameters/receiver, generic or Origin parameters, and Unit result (ordinary result omission is allowed). It is a safe ordinary function with a body, not unsafe, a foreign import, or a specialization. A main inside a group/struct or another function is not a candidate. Root-level public main is shared-root declaration syntax under §6.1.1, retaining declaration-site aliases; public promises no unmangled native symbol or export.

Validate every root-level public main in an Application as a startup signature. Diagnose invalid declarations rather than selecting a convenient overload. Normal Unit return and fallthrough are permitted; no special ownership rules apply.

```kimi
public func main() -> ()
    let message = "Hello, world!"
    ::Core.writeLine(message)
    // message has been moved; using it again is an error.
```

Adding a top-level `::Core.writeLine("Top level")` to this project is an error because it mixes startup forms. Integer-returning main and a safe Core.exit API are not initial features; normal termination is 0 and Abort is 1. Runtime.Exit remains internal.

A Library requires no startup candidate, never automatically calls main, and emits no OS entry. Treat main as an ordinary function without the Application signature restriction. Reject top-level runtime body items, including uninitialized let/var. Initial Library .ll is for inspection, LLVM verification, and object-generation experiments, not an externally callable library/DLL or dependency artifact; all language functions remain internal, even public ones. Optimization may remove all such functions, so inspect pre-optimization IR.

##### 22.2.3. OS entry, static initialization, and shutdown

The initial Windows Application emits compiler-reserved external `__kimi_start`, physical signature void (), Windows x64 ccc, noreturn, and the profile attributes (§21.5). Link with /entry:__kimi_start. It performs required runtime initialization, calls the selected body once, completes normal shutdown, and calls Runtime.Exit(0). Empty initialization/shutdown helpers may be omitted. Ordinary mangling prevents source names from colliding with this reserved symbol.

```llvm
; Fragment: profile attributes and internal helper definitions are omitted.
; Each helper has one internal definition, not an additional same-name declare.
define void @__kimi_start() noreturn {
entry:
  call void @__kimi_entry_body()
  call void @__kimi_shutdown()
  call void @__kimi_runtime_exit(i32 0)
  unreachable
}
```

The entry body is the implicit body or a call to the selected main. Add required runtime initialization before it; heap/standard-handle acquisition may instead occur inside each runtime operation.

The entry handles Kimigayo initialization/cleanup; it does not run executable CRT startup or C/C++ static constructors. Foreign initialization must already be satisfied, for example by OS DLL loading, or by an explicitly supported adapter. /NODEFAULTLIB is not initialization.

Static stored Properties initialize per slot under §11.3.2. A first read, Borrow, write, or other storage operation checks:

| State | Action |
| --- | --- |
| Not started | Mark Initializing; evaluate the declaration initializer; after normal completion mark Initialized, then perform the operation |
| Initializing | Abort for an initialization cycle |
| Initialized | Perform the operation without rerunning initialization |

A first write initializes before Replacement. Actual access determines dependency order, not fragment/file/link order. Computed execution initializes only storage actually accessed. A Type/function reference, untaken branch, or effect summary initializes no unrelated Field. Check all initializers even if unused; unused Fields need not initialize and are not destroyed. An implementation without static execution support diagnoses required uses rather than treating this specification as an implementation.

Normal body exit cleans its locals exactly once. Then destroy initialized static values in reverse successful-initialization order, retaining required lifetime dependencies; reject dependencies that cannot survive this order. Initializing new static storage, accessing destroyed storage, or reentering a Field's destruction during shutdown Aborts. These language rules include dependencies, although multi-Kotonoha linking is outside the initial profile.

Cleanup must finish before subsequent cleanup or Exit. Abort stops normal cleanup/unwinding and attempts diagnostics before Runtime.Exit(1) (§17.3, §22.5); secured results are not delivered or separately destroyed afterward.

This revision admits one execution thread, with no source thread creation or concurrent foreign reentry. Atomic arc counts do not expand that permission; synchronization and thread transfer remain §D.2's design boundary.

#### 22.3. Foreign function imports

##### 22.3.1. Declaration and call contract

`#LibraryImport("library", "symbol")` on a bodyless unsafe func selects the target C calling convention. Both arguments are required nonempty, non-interpolated, NUL-free string literals. The first is an Ordinal logical library key in NativeLibraries (§20.8.2), not a DLL filename/path; the second is the exact external symbol, independently of the source function name.

Allow imports only directly in group/rootgroup or as receiverless struct type functions. Reject receivers, generic/Origin parameters, default/optional arguments, varargs, specializations, and executable bodies. Calls are direct only; unsafe functions cannot be acquired as values. Ordinary access and unsafe-call rules apply.

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    public unsafe func observe(record: unsafe/NativeRecord) -> ()
```

NativeRecord can be the C-layout example in §21.1.3; the corresponding C declaration is `void observe_record(NativeRecord *record);`. The raw pointer is not read-only. Layout, validity, lifetime, writes, retention, ownership, and active Loans remain the caller's contract; this example supplies no new pointer-acquisition or raw-storage construction API.

Acquire arguments once from left to right, then use the selected ABI. No automatic marshalling, retention, allocation, freeing, or ownership acquisition occurs. Normal return resumes ordinary cleanup. C++ exceptions, SEH unwind, longjmp, callbacks, and reentry must not cross Kimigayo frames; control handled entirely inside the foreign code is allowed. Violations carry no result or cleanup guarantee. Apply the FP boundary contract in §21.5.4. Do not infer nounwind merely from these source restrictions or generate a landingpad to catch violations.

Unresolved logical libraries and unsupported ABI signatures are compilation errors. Record required link inputs; unresolved native symbols fail manual linking/loading before entry. C aggregate passing, export, callbacks, varargs, and extra calling conventions remain extensions; C storage layout is specified separately and does not enable them.

##### 22.3.2. Initial Windows C ABI

Compute a physical signature once and share it between declare and call:

| Kimigayo parameter/result | C value | LLVM Type |
| --- | --- | --- |
| i8 / u8 | int8_t / uint8_t | i8 |
| i16 / u16 | int16_t / uint16_t | i16 |
| i32 / u32 | int32_t / uint32_t | i32 |
| i64 / u64 | int64_t / uint64_t | i64 |
| f32 / f64 | float / double | float / double |
| unsafe/T | Corresponding data pointer | ptr, address space 0 |
| Unit, result only | void | void |

Use ccc with no signext, zeroext, inreg, byval, or sret for these entries. Do not widen i8/i16 to i32 or apply vararg default promotions. Other numeric conversions are separate language operations. LLVM handles registers, stack arguments beyond the fourth, shadow space, and stack alignment; unused upper bits are not meaningful. Additional optimization attributes need independent proof.

```llvm
declare dllimport i8 @native_i8(i8)
declare dllimport i16 @native_u16(i16)
```

Exclude bool, char, string, borrows, object handles, aggregates (including C-exchangeable structs/arrays), function/closure values, i128/u128, and isize/usize. Raw pointees are not passed by value and need not be C-exchangeable when opaque. Future aggregate passing needs separate argument/result C ABI classification and tests, not direct translation to LLVM aggregate parameters.

#### 22.4. Minimal console output

Core provides the public ordinary function `writeLine(text: string) -> ()` at its root, available through the Core default alias. `::Core.writeLine` identifies the required Symbol regardless of local shadowing. It is a safe, nongeneric function with one required owned string argument and no receiver, defaults, formatting parameters, or result borrow. The compiler/runtime supplies its implementation; it is not a source LibraryImport taking a string and does not expand the FFI Type surface.

Acquire text once under Copy/Move rules and write all its UTF-8 bytes plus one LF to standard output. NUL is data. Preserve contents without normalization, CRLF conversion, or locale encoding. Failure may leave partial output; no rollback or device atomicity is promised. Return Unit after host acceptance and flushing this call’s runtime buffer, not necessarily display or durable storage. Failure to complete initiates Abort under normal diagnostic/termination rules, even if stdout is unavailable. Destroy the acquired argument on normal return. Diagnose an unsupported target/feature if the backend cannot provide this operation.

**Complete application example (implicit startup in one SourceDocument).**

```kimi
::Core.writeLine("Hello, world!")
```

The required standard-output bytes are UTF-8 `Hello, world!` followed by LF; normal completion exits with code zero under §22.2. No source main function, user alias, unsafe block, interpolation, or user-declared foreign function is needed. Passing an existing string local Moves it; use its Stringify mapping to obtain an independent owned string when reuse is needed. Borrowed output overloads and general I/O error/result APIs remain outside this minimal operation.

The initial Windows implementation of this operation is specified in §22.5; output settings, manifest, and manual build steps are in §20.8. The first executable implementation milestone and the distinction between existing and proposed settings are recorded in [STATUS.md](STATUS.md#c12-first-executable-milestone). A prototype supporting only that subset must identify itself as partial; the milestone does not relax the Core identity/shape or validation requirements of a fully conforming Compilation. Unused executable Core bodies need not be emitted, but a same-spelled stub without the required identity and contract is not a compatible Core definition.

#### 22.5. Initial Windows runtime

##### 22.5.1. Operations and source context

The compiler emits these six logical operations in the same LLVM module. They are internal abstractions, not source APIs or a dedicated runtime DLL:

```text
Alloc(size: usize) -> ptr
Free(memory: ptr) -> ()
WriteStdout(data: ptr, length: usize) -> ()
Exit(code: u32) -> Never
TryWriteStderr(data: ptr, length: usize) -> bool
Abort(reason: DiagnosticText, sourceLocation: SourceLocation) -> Never
```

Alloc, detected Free failures, and WriteStdout fail by Abort. TryWriteStderr returns false without initiating Abort. Abort attempts diagnostics then Exit(1); Exit never returns and performs no Kimigayo cleanup. The normal language path calls Exit(0) only after cleanup.

Physical helpers may append private diagnostic context after ordinary parameters. Lowering passes static logical path/line/column information for the original operation, including failures inside Alloc/Free/WriteStdout and generated-source CodeContext provenance. This does not depend on PDBs or stack traces.

Generated arithmetic checks report the start of the failing arithmetic expression. Compound assignment and increment/decrement report the start of the complete update expression. These locations remain the same across optimization levels.

##### 22.5.2. Allocation and release

Use the process heap, MaxObjectSize = 2^63 - 1, and alignment support up to 16. Check length * stride, headers, and alignment rounding before allocation; overflow/limit failure Aborts. Alloc checks its own limit too, obtains GetProcessHeap, and calls HeapAlloc(heap, 0, max(size, 1)); null heap/allocation Aborts. It returns uninitialized raw memory, not an Initialized language value. The substitute byte for size zero does not change Type size/stride.

Free(null) succeeds without work. Otherwise require the original live pointer returned by this allocator, never an interior pointer or literal backing. GetProcessHeap failure or detected HeapFree(heap, 0, memory) failure Aborts. Lowered cleanup runs destruction before Free; Free itself invokes no destructor. Correct ownership/pointers are a static-analysis and generation duty; detection of double frees or arbitrary corruption is not guaranteed.

HeapAlloc flags remain zero, without exception generation or disabling process-heap synchronization. HeapAlloc does not supply last-error on failure; do not report a stale GetLastError value for null allocation. Obtain last-error immediately after APIs that provide it, including failed HeapFree/WriteFile.

##### 22.5.3. Synchronous byte output

WriteStdout and TryWriteStderr share a checked byte-write adapter. They add no newline, NUL scan, encoding conversion, buffer, or pointer retention. Length zero succeeds before handle acquisition or pointer access. For positive length, require length <= MaxObjectSize, nonnull data, a readable live range within one allocation, and no unsigned 64-bit overflow in baseAddress + (length - 1). Numeric checks do not prove allocation validity/lifetime.

Use GetStdHandle(-11) for stdout and -12 for stderr; null or INVALID_HANDLE_VALUE fails. Do not close these handles. Initial support requires synchronous standard handles. WriteFile receives a 32-bit written-count slot and null OVERLAPPED; capture a supplied last-error before calling another API.

```text
remaining = length
while remaining > 0:
    chunk = min(remaining, UINT32_MAX)
    request chunk bytes with WriteFile
    fail if the call fails, written == 0, or written > chunk
    remaining -= written
    if remaining > 0:
        advance within the original buffer with checked pointer arithmetic
```

Do not compute a next pointer after completion. Partial writes advance by actual progress; never retry zero progress indefinitely. WriteStdout turns any detected failure into Abort; TryWriteStderr returns false and must not call Abort, WriteStdout, or Alloc. Output may be partial, with no rollback, atomicity, display, durability, or bounded synchronous-wait guarantee. Success means OS acceptance of all bytes. These checked buffer rules do not change general unsafe pointer arithmetic.

##### 22.5.4. Abort diagnostics and exit

Fixed diagnostics use an ASCII identifier, English reason, and source location, optionally a valid numeric OS error:

```text
Main.kimi:3:5: abort KIMI_E_STDOUT: Failed to write to stdout (win32=6)
```

Use unique catalog codes, including KIMI_E_ALLOC_SIZE (allocation size exceeds limit), KIMI_E_PROCESS_HEAP, KIMI_E_ALLOC, KIMI_E_FREE, KIMI_E_STDOUT, KIMI_E_INT_OVERFLOW (Integer overflow), KIMI_E_INT_DIV_ZERO (Integer division or remainder by zero), and KIMI_E_INT_SHIFT_COUNT (Shift count out of range). Integer division/remainder by zero uses KIMI_E_INT_DIV_ZERO; signed minimum with divisor -1 uses KIMI_E_INT_OVERFLOW for both operations. A shift count outside `0 <= count < left operand bit width` uses KIMI_E_INT_SHIFT_COUNT. Discarded left-shift bits do not trigger overflow. Omit unavailable OS codes; do not use FormatMessageW. In displayed logical paths, escape non-ASCII/control characters as `\u{HEX}` and backslash as `\\`; preserve the actual path/provenance internally.

Output diagnostics from constants, valid input strings, and small fixed work areas without requiring heap allocation or one concatenated dynamic string. Explicit `$abort(expression)` retains ordinary one-time argument evaluation and outputs the resulting UTF-8 string after KIMI_E_ABORT without translation or escaping its contents. Once Abort starts, do not normally destroy that string.

TryWriteStderr failure truncates diagnostics and proceeds to Exit(1), with no recursive diagnostic path. Runtime.Exit forwards u32 to ExitProcess and ends its caller block with unreachable. It runs no cleanup; successful shutdown and Abort reach it through their distinct §22.2 paths.

##### 22.5.5. String handle and writeLine

The initial internal string representation is `{ ptr, i64, i8 }`: data, byteLength, releaseKind. Use DataLayout for padding/alignment and the aggregate internal ABI. It is not a public FFI/binary ABI.

| Component/state | Validity and responsibility |
| --- | --- |
| Length | 0..MaxObjectSize; contents are valid UTF-8 |
| Positive length | Nonnull readable data with the required lifetime |
| Static = 0 | Compiler constant backing, never freed; null permitted only at length zero |
| Heap = 1 | Nonnull original live Runtime.Alloc pointer, capacity >= length, exactly one owning release responsibility |
| Empty Heap | Keep and free the original pointer even at length zero; allocation-free empties use Static |
| Other releaseKind | Invalid; destruction's default branch Aborts instead of freeing an unknown pointer |
| Moved source slot | Unusable as a value; no bit clearing or rewriting is required |

Establish validity at construction, not by revalidating UTF-8/allocations on every use; corruption detection is not guaranteed. Literal backing may be shared without granting Copy to string. Hello world needs no heap allocation.

The required Core.writeLine Symbol (§22.4) acquires its owned argument once, calls WriteStdout(data, length), calls WriteStdout on a one-byte LF constant, then normally destroys the argument and returns Unit. Static release does nothing; Heap release calls Free. An empty string still emits LF. Output failure Aborts without normal argument destruction; earlier output is not rolled back. No concatenation buffer is required.

Write raw UTF-8 bytes to redirected files/pipes without changing the console code page. Non-ASCII console appearance depends on console configuration; universal Unicode console display is not initially guaranteed. A GetConsoleMode/WriteConsoleW adapter is a future extension, not implicit UTF-16 output.

##### 22.5.6. Windows external symbols

WindowsRuntimeSymbols retains each external name, physical signature, calling convention, dllimport setting, and link input. Share declarations through the module symbol table (§21.5.2), including matching LibraryImport declarations. Emit only needed APIs, using Windows x64 ccc:

```llvm
declare dllimport ptr @GetProcessHeap()
declare dllimport ptr @HeapAlloc(ptr, i32, i64)
declare dllimport i32 @HeapFree(ptr, i32, ptr)
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport i32 @GetLastError()
declare dllimport void @ExitProcess(i32) noreturn
```

HANDLE and data pointers use ptr, SIZE_T i64, DWORD/UINT/BOOL i32, and LPDWORD ptr to a 32-bit slot. Windows BOOL is not i1. Empty parameter parentheses mean no parameters, not omitted Types. dllimport specifies reference generation, not automatic linking; use the kernel32 input (§20.8.2).

## Appendices

### Appendix A. Compiler implementation requirements

**Normative.** Compiler requirements preserve the language’s information and invariants; they add no source syntax or failure behavior. Appendix B gives optional algorithms. Verification coverage below is required; parsing alone does not establish it.

| Term | Meaning |
| --- | --- |
| Koto / Koto tree | Parsed syntax nodes with source contexts and parent/child relationships; not a bound program or binary interface. |
| CodeContext | Source-local lookup and diagnostic context for one immutable source snapshot. |
| Directive Binding | Resolving and validating compile-time Condition names and dependencies. |
| Validation obligation | Information retained until a required check can be completed. |
| Finalization | The point when a declaration, layout, specialization, or executable body is accepted for subsequent compilation; required checks must be resolved. |
| Lowering | Translating checked source operations into lower-level representations while preserving semantics. |

#### A.1. Source identity and incremental analysis

A CodeContext belongs to one Kotonoha and immutable SourceDocument snapshot (§18). A new snapshot requires fresh context and alias/Binding results even at the same path. Source-less parsing contexts cannot replace parsed nodes’ source identity, and nodes cannot move into another Kotonoha’s Container.

Retain each node/fragment’s original CodeContext and diagnostic/header locations after merging. Resolve its bodies, headers, Types, and Constraints in that context, not the merged Container’s. Generated documents have their own contexts; source-less roots/wrappers supply no alias environment and must preserve wrapped syntax’s original context.

Lowering places a selected top-level runtime body in a private internal function, preserving its SourceDocument scope and CodeContext. It does not synthesize a public main or permit top-level return (§22.2).

The internal name `MacroKoto` does not define language semantics; `$` is the Composition Root.

A Kotonoha tokenizes and parses each `SourceDocument`, merging declarations into one root Koto tree. Root executable syntax is placed as described under [root and nested containers](#611-root-and-nested-containers).

#### A.2. Immediate directive validation and selection

Verify case-sensitive prepared-environment lookup and collisions under §2.5, including differently cased settings and rejection of invalid/non-NFC configured names. Selected `#if`/`#case` items share the surrounding scope: verify later name visibility, duplicate declarations, defer registration, cleanup timing, and transfer targets without an extra directive scope.

The Parser validates and evaluates each reached Condition against the prepared Compilation environment during parsing. The result is True, False, or Error; there is no internal Pending state or later Directive Binding. Unknown Names are diagnosed at their original source locations immediately.

A validated True #if contributes its Target directly; False contributes none. An invalid #if reports its Condition errors and skips its target for recovery. No #if wrapper or pending-condition storage is needed.

A reached #switch validates every explicit arm Condition and parses every arm under §19.3/§19.5, including nested Conditions in unselected arms except inside False #if targets. After successful validation, it contributes the first matching Block directly. Invalid Case Groups may retain this representation for error recovery:

```text
CompileTimeSwitchKoto
    CompileTimeCaseArmKoto[]
        Condition or fallback
        Block
```

Condition validation never invokes ordinary Binding of excluded syntax. Once reported, Condition diagnostics preserve their original SourceDocument and span even when the Condition node is discarded. Instantiation and implementation selection never reselect directives or mutate shared Koto. Eager, deferred, and cached source parsing must agree on acceptance and mandatory diagnostic categories under §19.5.

#### A.3. Binding, caches, and incremental validity

Mod execution and provisional Binding follow [§20.7](#207-mods-source-generation). After generation, final Binding preserves the following stages; Mod updates may repeat affected analysis but never reopen a finalized program:

```text
Complete source parsing and Mod output integration
-> select directives in established environments
-> collect selected declarations, root and scope tables, and alias targets
-> bind headers, Types, Signatures, and Constraints; validate merges/duplicates
-> match explicit specialization headers; close their set before final call targets
-> validate inherited Names; retain candidate conformance/receiver mappings
-> resolve bodies -> test candidates -> select operations; retain pending usage obligations
-> compute effect-family fixed points; complete public guarantees and conformance proofs
-> discharge usage obligations without lookup/overload reselection
-> instantiate static arguments and select the required implementation
-> retain Symbol references and the selected operation plan for lowering
```

Separate Type/Value and Origin/Label tables. Lookup Context retains source, scope, namespace, role, and access; outcomes retain Symbols, function groups, deferred work, or errors. Lowering never resolves strings again. Share normalized Types, but isolate candidate variables, constraints, mappings, tentative bindings, adaptation plans, and rejection reasons; commit only the winner. Early filters must preserve lookup stopping. Pairwise ranking may take O(n²) comparisons, excluding Type-comparison cost.

Pending conformance mappings are not proof evidence. Resolve information needed for lookup before computing effects; an empty or missing summary cannot fill unresolved work. Complete implementation proofs, public statuses, conformance, and use obligations in dependency order. Effect closure cannot justify a cyclic conformance, and no unresolved ObjectCompatible status may be published. Preserve ordinary errors separately from a valid implementation's NotProven status.

Cache only context-independent results or include every relevant dependency:

| Cache | Required distinctions |
| --- | --- |
| Lookup | Scope, name, namespace, role, lexical visibility, source alias environment |
| Accessible lookup | Also use-site Kotonoha, Container relationship, and inheritance/access domains |
| Member lookup | Target Symbol/Type, type arguments, static/instance use, protected receiver Type; no extension candidates in this revision |
| Applicability | Candidate, argument Effective Types/literal values/labels/forms, expected Type, type arguments and constraints |
| Environment selection | Compilation target, configured Project settings, selection state; independent of generic arguments |
| Conditional conformance | Generic declaration, substituted arguments, D/P proof environment, evidence paths, and verified implementation/associated-Type mappings |
| Receiver guarantees | Call operation Identity, complete implementation-family contents, root summaries, and verification dependencies; concrete caller arguments do not create a new public status |

Do not reuse results across different roles or unrelated access Containers. Source, dependency, alias, selection, or Symbol changes invalidate affected caches. Loans/initialization affect Usage Legality, not overload-cache keys; refinement affects input Effective Types and therefore member/applicability keys.

Apply §21.3.4 to all declaration-dependent judgments, including absence dependencies. Test Name/specialization additions, access expansion, Signature changes, and Proven withdrawal against stale artifacts. Reject consumers that lose a requirement, accept compatible changes after revalidation, and diagnose missing rebuild information without depending on load order.

Verify source isolation, merged private access, duplicates/roles/arities, Type/Value paths, aliases, unavailable extensions/modifiers, expected results, incomparable candidates, nested inference, generic environments, and no usage-failure fallback. For §2.5.1, cover declaration, requirement, constructor, deinit, and accessor prefixes while retaining ordinary same-spelled Names and independent next-line declarations. Load, parse, generator-completion, and candidate order must preserve results.

After merging/inference, check recursive API access domains (§9.3), retaining dependent associated-Type obligations. Cover private Types in internal APIs, restricted enclosing Containers, private generic arguments/helpers across Kotonoha, sealed bases, open-fragment agreement, base access, protected receivers, compound access, and §6.2.2's inherited-Name prohibition. Include all ancestor layers, Property access independent of accessor access, conditional members, and fragment/Mod additions. Access caches depend on base graphs/modifiers and completed Container contents as well as aliases.

Retain parameter immutability, receiver index/kind, argument mappings, and default environments (§7). Verify default Loans, partial-call cleanup, and receiver-first evaluation independent of parameter position. Inherited lookup caches preserve the committed layer; applicability/accessor failure cannot add base overloads.

Persist §15.6.4’s static effects and returned Loan anchors in artifacts/callable data. Use stable Field Identities, recursive summaries, and initializer/destructor dependencies; summary changes invalidate callers. Missing/incompatible summaries require conservative effects or diagnostics. Test Loans across direct, recursive, indirect, and separate-module mutation, default/cleanup effects, and permitted shared reads. Runtime Origin erasure must preserve these proof dependencies.

#### A.4. Property implementation requirements

Represent let/var storage, standard operations, custom/computed accessor signatures, and Contract requirements explicitly. Preserve storage/base identities, complete Types/Origins, accessor access, function boundaries, and witness mappings. Standard get is not a synthesized source function; Copy does not change its result Type.

Validate direct/child Place permissions, implicit receiver adaptations, generic Copy proof and conditional Move states, first-placement history, construction-call bans, getter temporary restrictions, and normal cleanup before lowering. Preserve Contract result restrictions through witness optimization. Update parser, writer, grammar, serialization, diagnostics, and artifact invalidation for accessor syntax and removal of explicit Move.

Cover private-set Move rejection; custom-get result versus storage borrowing; non-Copy custom setters and self-assignment; construction branch joins; partial inherited access and ancestor deinit; reference-result Origins; static accessor effects; and Contract by-value versus shared-slot witnesses.

#### A.5. Raw pointer backend requirements

`ptr` is not a Primitive Type. LLVM `ptr` is a backend representation; instructions supply the Types needed for memory access and arithmetic. Lowering must preserve this specification and use properties such as `inbounds` only when their premises hold. Language undefined behavior and LLVM poison are distinct concepts. The initial instruction mapping is specified in §21.5.3; pointer/integer round trips add no provenance guarantee.

#### A.6. Literal representation

Preserve integer magnitudes and exact decimals until fitting. Canonical serialization must preserve literal kind and fitted value for every allowed target; never round decimal literals through f64. Serialize strings with their values after newline normalization and escape/interpolation processing. LLVM emission and sharing follow §21.5.6.

#### A.7. Cleanup analysis and lowering

Analyze defer registration separately from execution: non-completing cleanup affects exit paths, not the path after registration. Lower defer and destruction into one exit sequence, retaining only needed registration state. No dynamic closure, function value, or heap cleanup stack is required. Initial physical lowering and slot responsibility follow §21.4.

In `if condition` containing only `defer => cleanup()`, the true branch registers and runs cleanup before its own exit; false does neither. This needs no registration flag, and lowering cannot move cleanup outside the branch. cleanup is an illustrative API.

Retain constructor choice, initializer environments, per-layer completion, component initialization/responsibility, and the Destruction receiver’s exact layer. Cleanup places the base after own fields; a completed base keeps its destructor if derived construction fails. Interfaces retain constructor access/signatures, destructor presence, logical component order, and exact dynamic cleanup chains. Physical offsets or apparently empty destructor bodies cannot replace these facts.

Verify fragment/selection deinit duplicates; forbidden placement, modifiers, and calls; implicit-constructor suppression; base-constructor access; completeness before/after constructor cleanup; partial Tuple/array construction; reverse element/base order; no destruction accessors; and skipped Moved components. Also cover partial-value replacement, interrupted cleanup, receiver escape, base slicing/replacement rejection, and one final-reference object cleanup.

#### A.8. Callable and object verification

Verify OwnedOrigins through raw pointees, unused Type/Origin slots, captures, and recursive payloads; test `a : static`, unresolved abstract Origins, Function Item bound arguments, and fixed callable signature Origins versus per-call binders. Reject mutable-static borrows at Owned boundaries, including after capture and across modules. Checked casts may supply only proof-covered fixed Origin bindings as static; preserve outer handle Origins and never bind per-call callable Origins. None of these checks may depend on a private body being available to a client.

Verify input-derived Callable results for all three receivers: per-call Origins, multiple-input meets, retained result Loans, rejection of a shared-to-exclusive upgrade, and rejection of hidden-receiver or call-local result dependencies. Common Function Type compatibility uses the same argument/result relation while retaining its separate Owned-environment restriction.

Retain capture bindings, acquisition order/effects, mutability, nested dependencies, environment identities, and capture/call/result Origins and Loans through compilation and artifacts. Resolve required Copy, receiver, Callable, and erasure obligations before finalization. Check concrete Copy independently of receiver kind and erased-container classification.

Verify §12.4.4's single public status per call operation, standard Place/witness distinction, and common implementation-family guarantee. Cover Whole/Base replacement, incomplete MoveOut, exclusive escape, permitted Part replacement/exchange, reference-field versus referent aliases, captures/statics, defaults/cleanup, returned borrows, and separate/indirect/generic calls. Verify recursive fixed points without treating circular conformance as evidence. A NotProven specialization changes the public guarantee without a declaration error for that reason; its cause must be traceable at a failing use. Unknown, missing required data, and unfinished verification cannot supply proof.

Verify multi-level generic base projection, original protected-receiver checks, shared/exclusive access, returned-Loan anchors, direct Field access, and rejection of whole-base replacement, consuming receivers, and unbound derived/base argument conversion. Preserve these results across separate compilation and optimization.

Verify object creation's Copy/Move input states, payload eligibility, fresh identity, initial strong count, allocation failure, and source-storage responsibility. Strong-owner duplication must preserve identity without retaining a Loan on source-handle storage; cover count overflow, release through different views, exactly one final cleanup, and resurrection rejection.

Test capture versus call acquisition, unused/nested explicit captures, let/var, reference/referent lifetimes, Copy/Move-consuming calls, every Callable receiver, per-call Origins, variant cast Loans, static inherited calls, and construction/destruction restrictions. Verify public status, use legality, complete dynamic cleanup, and receiver adjustment independently of load order and optimization.

#### A.9. Refinement and require verification

Retain require’s condition, failure body/form, and layout as a statement; resolve transfers before any Selection-style lowering. Distinguish Requirement Tests from runtime is by syntax context. Prove failure non-continuation independently of condition truth, including caught transfers and actual cleanup paths.

Retain Binding Identity, Value Instance, Effective Type, and validity conditions; coordinate refinement with initialization, Loans, transfers, cleanup, and reachability. Cache Effective Types. Facts cannot cross old aliases, Boolean variables, later defer-registration states, or Function Boundaries. Short-circuit/loop joins must be analysis-order independent.

Cover same/next-line `else`, comments, multiline conditions, missing or empty bodies, forbidden expression placement, both `is` contexts, single evaluation, trivial tests, invalid targets, early transfers, contradictory facts, Move/Replace invalidation, surviving member mutation, alias versus new-binding typing, failure validation even for `require true`, the retained static true successor of `require false`, and unchanged acceptance under optimization. Type-checking continuations must preserve valid refinements without restoring Moved values/ended Loans or adding execution edges; include their result constraints without merging their state into live paths.

#### A.10. Enum and Pattern verification

Preserve Case Symbols, Pattern positions, guard/body Binding Identities, read/binding Types, Access Modes, acquisition effects, and Origins/Loans through compilation. The [reference plan](#b6-match-binding-plan) shows possible storage. Resolve Cases, Copy obligations, and dependent reads by their deadlines. Type alone does not identify owned versus shared access.

Verification must cover at least these boundaries; neither parsing nor optimization establishes the guarantees:

| Boundary | Required result |
| --- | --- |
| `.Some(0)` and `.None` alone; all arms guarded | Missing coverage in any match |
| `Option<bool>`, Tuples, nested enums, multiple earlier arms | Require whole-payload/whole-position arms or catch-alls; partial partitions do not combine into coverage, and unions do not trigger mandatory unreachable warnings |
| Same-Case/Tuple containment, duplicate Literals, guarded earlier/later arms | Mandatory warning only for the specified structural containment by one earlier unguarded Pattern; a later guard does not suppress it |
| Duplicate names, wrong Case/arity/Type, `.Some` or `.None()` construction | Errors regardless of reachability |
| `i8` literals `-128`, `128`, `-129`; `i32` arms `0`, `-0`, `0x00` | Fit after sign; reject the two out-of-range values; warn on the two later equal unguarded values |
| Origin-bearing ref/uniq payload construction, acquisition, cleanup | Preserve dependencies/capabilities; never destroy borrowed referents |
| Named/single-Origin mapping and `View<T>.Some` | Equivalent mapping; reject the enum's own Origin annotation in the Case qualifier |
| Unconditional Copy with unconstrained T / constrained T / only ref/T payloads | Declaration error / valid derivation / no referent Copy premise |
| Conditional Copy when T is Copy | Type remains usable for non-Copy T; Copy is available only when its condition is Proven |
| Core Option/Result conditional Copy | Check both Result payloads, nesting and complete Semantics; active Case does not change capability. Verify reuse versus Move, Unknown versus Refuted, and borrow dependencies after Copy |
| Core condition-atom sets | Accept both orders, grouping, and duplicate atoms; reject missing/unconditional Copy, missing/extra atoms, and wrong Symbol Identity. Check generated and loaded definitions without spelling-based user-enum behavior |
| Shared reads of Copy Result payloads | Slice and ref-Pattern reads produce values; explicit element @ref retains storage/Origin and tryGet keeps its reference result. Generic head with plain s[0] fails definition checking; no borrow-binding selector is introduced |
| Child structural Patterns, Grouping, `ref/ref/T`, `ref/uniq/T`, `uniq/T` payloads | One dereference per position; retain shared access; reject direct structural matching of remaining reference layers; allow binding then inner shared match |
| Same Non-Copy payload Type on owned/shared paths | Body acquisition is Move/shared reading respectively; test string, object, and exclusive-reference payloads |
| Non-Copy subject with only `_`; false guard followed by acquisition | Initial whole Move even without bindings; preserve payload for the next arm without double destruction |
| Same name in guard and body | Separate Identities and scope-specific Types/overload resolution; no automatic refinement transfer |
| Guard Move, exclusive borrow, mutation through aliases, or direct candidate capture | Errors, including direct Copy candidate capture |
| Saved Copy values, stored references, and Copy aggregates | Check transitive Origins/Loans and destination; ordinary argument passing does not itself prohibit storage |
| New candidate-dependent Borrow/Reborrow escaped directly or through a callee | Error; dependent Loans end inside the guard |
| Guard outward `return`; Abort/divergence or non-normal guard cleanup | No body initialization or later arm; ordinary transfer secures its result then cleans guard/Subject, while Abort does not unwind |
| Match return/yield/outward transfer | Result acquisition, arm cleanup, then remaining Subject cleanup; no destruction of borrowed referents or unmatched completion |
| Borrow into owned Subject versus returning a stored external reference | Reject Subject escape; permit external reference only under its existing contract |

#### A.11. Static Contract verification

Keep Contract/Requirement identities, parent edges, associated-Type declarations/projections/specifications, requirement signatures/Constraints, and conformance mappings separate from ordinary members. Collect Constraints before resolving projections; normalize explicit associated identities before matching, never infer them from implementations. Origin-free matching keys must retain Origin contracts elsewhere.

Verify requirement access/parameters, parent cycles/conflicts, duplicates before matching, selection before compatibility, conformance access domains, and explicit/implied parent mapping consistency. Cache projections/candidates with their conformance/Constraint environment. Shared-candidate proofs must cover every substitution independently of optimizer code sharing.

Test qualified/ambiguous associated Names, diamonds/independent declarations, inherited/missing Type specifications, input/Origin matching, getter covariance/setter structure, redundant/conflicting parent mappings, and static calls/unsupported runtime Views. Cover safe/unsafe functions, defaults, nonstrengthening Constraints/Effects, and public conformance with private implementations.

Conditional conformance checks also cover positive-only syntax, permitted block declarations, Type-use versus conformance premises, condition-scoped signatures, duplicate direct registrations across fragments, cyclic/Unknown evidence, and parent-path agreement. Compare applicability only after committed lookup and substitution; test same-group alternatives, no base fallback, no condition-strength ranking, and no instantiation-time reselection. Verify conditional Copy derivation and Field bridges under their declared premises. Artifacts/cache invalidation preserve conditions, mappings, associated-Type bindings, and proof dependencies independently of environment directives.

Test struct-conformance inheritance under §8.4.4: a compatible Stringify-like mapping succeeds; Self arguments/results and owning receivers fail that path without invalidating the derived declaration. Cover fixed associated-Type normalization, alternative paths, intrinsic exclusions, and Unknown/Error distinctions. Across A -> B -> D, retain A's Member Identity/Self and composed Type/Origin/receiver mapping. Diagnose explicit conformance and constraint-use failures with the failed requirement/cause, without warning on the open base. Generic calls use the retained mapping, not caller-side member lookup.

#### A.12. Generic schemas and specialization verification

Preserve complete Type slots rather than flattening a pair into two independent arguments. Keep function length slots distinct from Type slots. A logical schema contains:

```text
DeclarationSchema
    GenericSlots: ordered index, kind (ordinary Type, pair Type, or function length), bound names
    OriginSchema: ordered binders, bounds, inferred variance and Loan requirements
PairSlot
    WholeType: complete or dependent Type
    OuterSemantics, DirectTarget, OuterOrigin: projections of WholeType
LengthSlot
    Value: nonnegative isize constant or bound dependent length expression
DeferredObligation
    requirement, source use, defining bindings/environment, dependencies, deadline
```

Use Origin schemas for argument correspondence, fragment-header matching, and artifact compatibility, not overload identity. Preserve Binding Identities through alias expansion and Signature normalization. Prove role restrictions before using a projection, or retain only a legitimate obligation. Semantic metadata must not discard Origin information merely because runtime descriptors omit it.

Verify declared Origin bounds at both definition and use: forward binder references, unknown/duplicate binders, equality cycles, `static`, substituted call/Type bounds, fragment agreement, nonstrengthening Contract implementations, and inherited specialization bounds. Bounds never supply a Loan or add overload candidates. Distinguish a local borrow's carried Origin from its slot's inferred Origin; reject bare owned-local names as Origins and preserve independent nested dependencies.

Fixed-array verification must cover contextual of/length, explicit slot kinds and conflicts, parenthesized constant expressions, constant-readable eligibility, expected-Type boundaries, candidate-local literals, element counts, zero length, zero-sized elements, recursive/nested layout and stride, normalized dependent expressions, negative intermediates, public ValidLength failures, universal body checking, specialization keys, and artifact invalidation after constant changes. Check whole initial construction, static-path reinitialization after Partial Move, reverse partial cleanup, and literal-only Move Paths.

Verify full-Type binding, one-slot pair decomposition, reconstruction versus applying s elsewhere, duplicate names, trailing commas, occurs-checks, invalid arguments, recursive storage, and input-order-independent principal Origins. Origin simplification must preserve distinct Loans.

Test specialization ambiguity before contract checking; inherited defaults/Safety; closed arguments; Origin-only duplicate keys; obj/D versus rc/D; mandatory selection through shared callers/references; recursion; and no fallback. Verify unused selected declarations, closed-set ownership, and set/body invalidation. Code sharing, budgets, automatic specialization, and LTO must preserve behavior. Cover unconstrained reuse/capture rejection, fixed Property results, shared element-read families, and conditional Copy/Move plans, caller-independent definition checking, and no hidden applicability conditions. Instantiation preserves §8.10’s symbolic ownership/cleanup plans.

#### A.13. Sequence access verification

Bind index versus range access by argument Type, not literal syntax. Retain receiver/element Places, access form, bounds, permissions, acquisition, complete result Type, and Origins/Loans. Nested access cannot read/copy intermediate arrays. Preserve generic SharedReadResult correlations and validate every admitted case before lowering.

Preserve one-time receiver/argument evaluation, index-evaluation protection, exclusive activation after bounds checks, RHS-first assignment, and target-first compound assignment. Apply common increment/exchange rules. Metadata snapshots add no source Loans and waive no receiver validity checks.

| Verification area | Required coverage |
| --- | --- |
| Index and Range | Lengths 0/1, ^0/^1, negative offsets, excessive distances, saved values, half-open/inclusive/reversed ranges, operand evaluation before construction checks |
| ResolvedRange | Maximum-isize end, finite and permanently exhausted iteration, rejection of direct Range iteration, reuse against shorter/resized targets |
| Place acquisition | Copy versus ordinary/explicit Move, literal-only eligibility, runtime/Index shared reading, nested writes without intermediate Copy, reinitialization after Partial Move |
| Slice boundaries | Zero-based reslicing, split endpoints, None from each try operation, failure inside arguments remaining Abort |
| Lifetime and storage | Borrowed Array/Slice retention, temporary/local escape, copied references versus slot borrows, nested Origins, whole-array Loans including empty/split views, rejection of mutable-static borrows at Owned boundaries |
| Metadata and iteration | Receiver effects, completeness checks, no result Loan for metadata, stable saved indices, reference iteration independent of element Copy, no iterator-owned borrowed results |
| Lowering | Identical acceptance, results, effect/Abort order, and Loan legality with optimization enabled/disabled; O(1) view operations without element-proportional allocation or Copy |

#### A.14. Layout, LLVM, and runtime verification

Validate windows-x64-v1 with LLVM 22.1.8, distinguishing semantic acceptance, TypeLayout, IR structure, object ABI/dependencies/unwind, and actual execution. The verifier alone cannot establish layout, foreign ABI, ownership transfer, or correct startup. Keep input/target/settings/expected results together; retain representative golden IR plus structural checks, and normalize only irrelevant internal numbering/paths.

| Area | Required coverage |
| --- | --- |
| Startup | Unique implicit/explicit body; uninitialized top-level let/var and Unit; empty/declaration-only documents; invalid/duplicate main; mixed forms; selected/generated items; Library restrictions; static-only documents |
| Layout | Attribute errors and fragment conflicts; single C storage fragment; moving the entire fragment/renaming method fragments; nested/zero-sized/aligned Types; generic substitution; inline cycles; overflow; C size/alignment/offsetof under packing 16 |
| ABI and ownership | Scalar/aggregate/zero-size passing; separate Copy source; acquisition transfers; result secured before cleanup; Abort/divergence before delivery; partial construction/Move; literal backing never freed; Heap ownership released once |
| Instructions | All checked integer boundaries, signed minimum/-1, invalid shifts; float-to-integer boundaries and adjacent floats for every supported pair, NaN/infinities/fractions/signed zero; finite-to-infinity conversion; i128 supported/unsupported operations and helper dependencies |
| CFG and optimization | Short circuit/guards, scalar/aggregate/Never joins, actual phi predecessors, assignment/setter evaluation count, self-assignment, first placement, direct construction, edge cleanup/defer flags, nontermination, partial values/padding/overlap |
| Constants and FP | Exact reread fitted bits and UTF-8/NUL lengths; literal sharing; NaN Equatable distinction; subnormals/ties/signed zero; ABI-standard MXCSR after external save/change/restore, without comparing status flags |
| FFI and pointer operations | Clang C signatures; signed small integers, maximum unsigned values, mixed FP/integer and 5+ arguments; symbol/library/ABI collisions; null zero-displacement and valid positive/negative/one-past arithmetic; no expected result for unsafe violations |
| Runtime failures | Partial/zero-progress writes, missing stdout, failed stderr without recursion, allocation/free failures, size/address overflow, invalid releaseKind, empty Static/Heap strings, source provenance and ASCII path diagnostics |
| Backend supply | Native COFF only, disassembly and dependency inspection, no self/cyclic libcalls; memcpy/memmove/memset boundaries/alignment/results and both overlap directions; __chkstk page/multipage frames, guard probes and register/stack preservation |
| Profile and publication | CPU/features independent of host, PIC/ASLR at ordinary 64-bit image bases, .pdata/.xdata consistent with prologues, one strong _fltused in both output kinds, package/ABI/hash/supply mismatch, unknown dependencies, mixed publication/hash failure |

Use fault-injecting test adapters where needed without expanding the production six-operation/seven-API set. Compare O0/O2 stdout, stderr, exit status, and observable effect/cleanup order; compiler Debug/Release must preserve acceptance and runtime checks. Run nontermination tests with bounded test-harness time and inspect optimized CFG. Separate required constant evaluation from ordinary constant expressions, and diagnose unsupported generated operations before optimization even in unused bodies.

Adopt helper supplies only after /NODEFAULTLIB custom-entry O0/O2 links and execution establish no hidden CRT/dynamic/TLS/exit-handler dependency. Check all actual object undefined symbols, including references added/removed by LLVM, against real providers. Version/hash/catalog mismatches fail validation. Validate memory intrinsic expansion and libcall paths, and assembly unwind information.

The first executable must itself produce exactly `Hello, world!\n` (14 UTF-8 bytes), empty stderr, and exit code 0; host test-runner output is not a substitute. Also test empty strings, Japanese/NUL bytes under redirection, and Abort code 1. Library verification checks retained pre-optimization bodies/signatures/linkage and object generation, not external linking or execution. Assess unnecessary slots/transfers/flags with representative O2 code without deleting semantically required work.

#### A.15. Control-flow verification

Preserve Body form, evaluation context, transfer target, result sources, and the distinct analyses in §14.9. Verification must cover:

| Area | Required coverage |
| --- | --- |
| Layout | Reserved do and ordinary block identifiers; both Body forms for every executable construct; header baseline and wrapped headers; delimiter regions; nested-if grouping; match arm indentation; permitted directives in nested indented bodies and function-leading Constraints; missing items; rejection of old body colons and standalone `=>` lines |
| Results | Discarded body values versus explicit transfers; Unit-fixed targets; omitted else; loop exits versus iteration ends; dead result sources; delayed numeric defaults through nested results; declared results versus inferred Never |
| Anonymous functions | Explicit/common/unique expectations; Unit fixed by another argument independent of order; no body rechecking, instantiation-time reinterpretation, or result-discard overload priority |
| Transfers | Nearest-target lookup before context/Type checking; named operand colons/grouping; label activation only in bodies; yield through loops; function/defer barriers; discarded-selection bare-yield rejection |
| Paths and cleanup | Shared structural paths without literal pruning; while true versus loop; condition cleanup before branching; result acquisition before cleanup/delivery; defer self-exit resumes pending cleanup; cleanup divergence does not change the checked Type |
| Diagnostics and tools | Unexpected inferred Unit through nested discarded selections/do expressions; one warning per discard under §17.4's priority, including Copy/Non-Copy Result and separate arm occurrences; effect-free discard without assuming call purity; no warning solely for a final defer; formatter preserves Body form; refactorings preserve results, targets, and destruction order |

Coordinate with the cleanup, refinement, and Pattern checks in A.7, A.9, and A.10.

### Appendix B. Non-normative reference models

**Non-normative.** All algorithms in this appendix are optional. Any alternative must preserve the language and Appendix A, including excluded-syntax validation and committed lookup decisions.

#### B.1. Build pipeline reference model

Pass boundaries and scheduling are implementation choices; semantic dependencies remain mandatory.

The logical compilation pipeline is:

```text
Solution -> Project -> Compilation(inputs)
    -> SourceDocuments -> Tokenization -> Parsing / Koto tree
    -> Directive Binding and selection of lookup environments
    -> Declaration/Header Binding and definition-side environments
    -> Body Binding, Type checking and overload selection
    -> CFG construction and symbolic generic-body verification
    -> Common effect-family fixed points, public guarantees, conformance and usage proofs
    -> Generic instantiation and published dependent/representation obligation resolution
       (without directive reselection or new body-derived semantic conditions)
    -> Explicit implementation selection from the closed defining set
    -> Concrete control-flow, Access Effect, ownership, Origin, lifetime and cleanup analysis
    -> Final acceptance and supported-generation-set validation
    -> Layout / ValueLowering / FunctionAbi / CleanupPlan
    -> LLVM lowering -> pre-optimization .ll + .link.json
    -> build: LLVM verification / opt / llc -> object
    -> Manual native linking -> executable -> execution validation
```

Before publication, verify §12.4.4's common implementation-family guarantee; selecting one concrete implementation cannot strengthen it. Concrete generation then analyzes the selected body's substituted CFG/effects. Early shared-body planning/caching is allowed; executable lowering waits for resolved effects, Loans, and cleanup. Sharing preserves the finalized operations (§21.3); no unverified body may be emitted. The initial output boundary is §20.8: generic sharing, external Library ABI, and automated linking/running are not implied by this pipeline.

#### B.2. Directive processing sequence

This schedule preserves §19’s source selection and validation rules.

The evaluation and Syntax-processing sequence is:

```text
Parse a directive Condition
    -> resolve all Names and validate all operands against the prepared environment
        -> True #if: parse the controlled Syntax
        -> False #if: scan only required token/layout/body/directive structure
        -> #switch: validate all arm Conditions; parse every arm with nested #if rules
        -> Error: report required diagnostics and recover structurally
    -> retain any implementation-internal validation work with its source-defined traversal
    -> complete required nested Conditions even in unselected reached #switch arms
    -> discard speculative grammar errors only where False #if rules require skipping grammar
    -> diagnose Names absent from the prepared environment; do not retry after generic Binding or instantiation
    -> resolve selections that change a scope's lookup environment before ordinary Name resolution using that environment begins
    -> require a final result and completed validation before finalization
    -> bind and lower only the selected Syntax
```

#### B.3. Borrow checking reference algorithm

One possible borrow-checking sequence is shown below. Type checking uses the Structural Completion, Runtime Reachability, and unreachable-code rules in §14.9–§14.10; the sequence does not infer Types from optimized CFG reachability. Implementations may interleave these tasks to resolve their dependencies.

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

#### B.4. Callable and object implementation strategies

**Non-normative.** A Closure may lower to an environment plus a call entry. Keep logical capture initialization/destruction order independently of physical field layout. Common Function Types can use inline or allocated storage with call and destruction entries; allocation removal is an optimization, not a typing rule.

Implement §12.4.4's required abstract verification with worklists or equivalent fixed-point algorithms. A statically selected method/accessor may have an ordinary entry and a receiver-adjusting adapter. The adapter implements an already permitted call; it supplies neither a new public guarantee nor an unrestricted exclusive receiver. Summary encoding and physical entry sharing are implementation choices.

Descriptor sharing/canonicalization may permit address comparison when it preserves language identity; distinct physical descriptors may still denote one Runtime Type Identity.

#### B.5. Type relation and operation plans

**Non-normative.** A Binder can map the [normative relation table](#38-type-relations-and-expression-operations) to separate APIs such as the following. Names and signatures are illustrative, not language requirements.

| Illustrative API | Responsibility |
| --- | --- |
| `IsIdenticalType(A, B)` | Compare normalized complete Types, including bound Origin identity. |
| `IsSubtype(A, B, constraints)` | Prove static fitting without inserting value operations. |
| `IsExpectedResultCompatible(A, B, constraints)` | Apply only the static relation permitted during candidate result filtering. |
| `CanImplicitlyAdapt(expression, target, context)` | Select an adaptation permitted in that use-site context. |
| `CanExplicitlyAdapt(expression, target, context)` | Select one operation admitted by the resolved explicit target. |
| `CanAcquire(expression, access, state)` | Check the selected access against Place, initialization, ownership, and Loan state. |

Adaptation/acquisition APIs can return plans containing the selected operation, complete result Type, Origins, access, Copy/Move/Loan effects, and deferred obligations. Static relations can return proven, rejected, or unresolved judgments. Isolate candidate plans until commitment, then validate/lower the selected plan in evaluation order. Runtime checks remain part of that operation, never reasons to seek another conversion.

#### B.6. Match binding plan

An implementation may attach the following information to bound Pattern positions or an acquisition plan. Names and storage layout are illustrative:

| Information | Meaning |
| --- | --- |
| `MatchedType` | Complete Type at the position before implicit dereference |
| `AccessMode` | `Owned` / `Shared`, including that position's implicit dereference |
| `ImplicitDeref` | `None` / `SharedOnce` at this position |
| `MovePath` | Subject position with owned initialization/destruction tracking, when applicable |
| `CandidateSymbol` / `GuardReadType` | Binding position's candidate Identity and shared-read Type |
| `BodySymbol` / `BodyBindingType` | Distinct body-local Identity and acquired Type |

AccessMode describes the path, not the value's Semantics or the Owned capability. The internal label `Owned` denotes by-value access; an implicit dereference changes it to Shared, inherited by descendants. Binding a `ref/E` Subject with `let r` uses `Owned` / `None`; a Case Pattern inspecting its referent uses `Shared` / `SharedOnce`. Guard reading remains shared in either mode.

Retain Candidate positions, Origin/Loan dependencies, and Copy/Move/Borrow/Reborrow plans alongside these facts. Candidate/body Symbols and binding Types are needed only at Binding positions. Shared position tracking gives no Move authority. Do not reconstruct access effects solely from MatchedType or reuse an instantiation's plan when its effects differ.

### Appendix C. Implementation status

Implementation coverage is maintained in [STATUS.md](STATUS.md), separately from language conformance. Parser support alone establishes neither Binding, constant evaluation, ownership checking, nor execution.

### Appendix D. Deferred feature index

This index links to design boundaries owned by the language sections. It adds no syntax or permissions. Implementation coverage is independent and recorded in [Appendix C](#appendix-c-implementation-status). Language-version selection is specified under [Language-version selection](#205-language-version-selection).

| Feature | Status | Owning section |
| --- | --- | --- |
| Fixed-array fill/repetition and element-generator construction | Deferred design; whole initialized inputs/results and element literals remain available | [Fixed-array initialization](#43-initialization-and-inference) |
| Mutable/exclusive-element Slice | Not introduced; mutate through authorized whole-array access and indices | [Sequence operations](#45-operations-and-ownership) |
| Source transparent Type aliases | Not introduced; source alias opens a Container only | [Alias boundary](#181-external-references-and-aliases) |
| Re-export syntax | Deferred design | [Re-exports](#182-re-exports) |
| Binary artifact format | Partially specified | [Source artifacts and binary interfaces](#183-source-artifacts-and-binary-interfaces) |
| Dependency configuration and graph diagnostics | Partially specified | [External references and aliases](#181-external-references-and-aliases) |
| Future Contract fragments and extension identity | Deferred design; Contract splitting and extension declarations are not introduced | [Container fragments](#612-container-fragments) |
| Mods (source generation) | Execution and semantic rules defined; concrete APIs and host configuration remain design work | [Mods](#207-mods-source-generation) |
| Virtual/override members | Extension design; not active in this revision | [Virtual members](#624-virtual-members-and-overrides) |
| Runtime Contract Views: designation, View associated-Type bindings, contract/exact tests and checked casts | Extension design; outside this revision's static Contracts | [Runtime contracts](#85-runtime-contracts), [tests and casts](#1362-general-view-tests-and-checked-casts) |
| User-defined generic Contracts, outer generic capture, default implementations, external conformance, qualified requirement calls | Not introduced | [Contracts](#84-static-contracts) |
| Extra implicit/ordinary base conversions and consuming/generic/type-function runtime requirements | Deferred design | [Object views](#335-object-views-and-identity), [runtime contracts](#85-runtime-contracts) |
| Struct layout modes | Kimigayo/C specified; special layouts deferred | [Structure layout and ABI](#211-structure-layout-and-abi) |
| Concurrency, memory model, and thread-transfer capabilities | Deferred design | [Concurrency boundary](#d2-concurrency-memory-model-and-thread-transfer) |
| User-defined arithmetic and general Attribute semantics | Deferred beyond specified comparison, Layout, LibraryImport, and Mod marker behavior | [Operator boundaries](#138-extension-boundaries-and-reserved-syntax), [Attributes](#65-attributes) |
| String concatenation and string compound-assignment ownership | Deferred to a common operator model; executable acceptance requires defined acquisition, Loans, result ownership and failure rules | [Arithmetic operators](#133-arithmetic-bitwise-and-shift-operators) |
| Associated-Type inference beyond explicit identity facts, arbitrary complete-Type bindings, and stronger symbolic Constraint reasoning | Not introduced | [Associated Types](#843-associated-types), [proof boundaries](#87-constraint-proof-system) |
| Const/value arguments beyond function lengths, standalone Semantics slots, partial/default/variadic generic arguments | Not introduced | [Function length parameters](#44-function-length-parameters), [Generic Type parameters](#81-generic-type-parameters) |
| Partial/conditional explicit specialization, specialization priorities, generic Container specialization | Not introduced | [Full specialization](#88-explicit-full-function-specialization) |
| Exact precompilation, callee propagation, sharing/ABI formats, and optimization budgets | Implementation-design boundaries | [Generic generation limits](#2135-generation-limits-and-code-merging) |
| Automatic toolchain installation, debug information, cross-module/DLL ABI, extra CPU/OS profiles | Deferred beyond the Windows profile; explicit build/run commands are defined in §20.8.6 | [Native build](#208-llvm-output-native-build-and-execution), [LLVM profile](#215-llvm-windows-x64-profile) |
| Stack exhaustion detection, diagnostics, and recovery | Unspecified; no guaranteed conversion to Kimi Abort or recovery contract | [Storage and unwind information](#2155-storage-attributes-and-unwind-information) |
| Dynamic collections, borrow/object/function handle ABI, rc/arc physical counters/order, general shared-generic metadata | Physical representation must be specified before emission; no implicit one-pointer fallback | [Object metadata](#212-object-metadata), [Internal ABI](#2142-physical-function-signatures) |
| C aggregate passing/export/callback/varargs, Unicode console adapter, over-aligned allocation, arbitrary exit codes and FP environment control | Deferred extensions | [FFI](#223-foreign-function-imports), [Windows runtime](#225-initial-windows-runtime) |
| Contract-level abstract Origins, non-static erased views, static-Place Origins, and lending iterators | Deferred design; ordinary retained storage is defined in §15.4 | [Abstract Origins](#153-abstract-origins), [Lifetime design boundaries](#159-lifetime-design-boundaries) |
| Destruction lifetime relaxation | Deferred design | [Destruction lifetime checking](#1566-destruction-lifetime-checking) |
| Additional dynamic Move Paths | Deferred design | [Move Paths and Partial Move](#1513-move-paths-and-partial-move) |
| Object ownership API spellings and recoverable creation; general duplication API | Core object creation/strong-owner duplication semantics defined; remaining APIs deferred | [Object ownership operations](#1358-object-ownership-creation-and-sharing), [explicit duplication](#351-copy-capability-and-explicit-duplication) |
| Weak operation spellings and cyclic-construction factory | Weak Type, ordinary operations and Windows x64 storage specified; API spellings and cyclic construction deferred | [Weak values](#322-weak-reference-values), [weak operations](#1359-weak-reference-operations), [object profile](#2123-windows-x64-object-and-weak-profile) |
| Exchange API and concurrency guarantees | Partially specified | [Initialization-preserving exchange](#157-initialization-preserving-exchange) |
| Raw pointer and FFI APIs | Partially specified | [Raw pointer API design boundaries](#56-raw-pointer-api-design-boundaries) |
| Dedicated Option / Result propagation syntax | Deferred design | [Error policy](#171-error-policy) |
| Additional enum and Pattern forms | Deferred design | [Enum and Pattern extensions](#d1-enum-and-pattern-extensions) |
| String indexing and library indexers | Partially specified | [Indexing and slicing](#46-indexing-and-slicing) |
| Borrowed/Exclusive/Consuming erased callable Types, opaque returns, receiver-dependent public results | Deferred design | [Callable Types](#321-callable-value-types), [Closure lifetimes](#1582-closure-dependencies-and-call-results) |
| Non-escaping declarations, extended capture syntax, concurrency capabilities, general higher-ranked Callable contracts | Deferred design | [Capture rules](#762-capture-acquisition-and-environment), [escape](#1583-escape-and-retention) |
| Direct-match versus callable-erasure overload ranking | Deferred design | [Callable compatibility](#107-callable-signature-compatibility) |
| Composition Root extensions | Partially specified | [Extension boundaries and reserved syntax](#138-extension-boundaries-and-reserved-syntax) |

#### D.1. Enum and Pattern extensions

The initial [enum](#63-enums) and [match](#148-match-expressions-and-patterns) rules do not introduce the following extensions. Future designs must preserve these boundaries:

| Extension | Required design |
| --- | --- |
| Struct Pattern | Distinguish Fields from computed operations and respect private storage and Move permissions. Getter-based decomposition needs explicit evaluation, effects, and coverage rules; it is not inverse constructor execution. `{}` remains reserved. |
| Named payload | Define stable element names and declaration order without changing positional payload meaning |
| Type Pattern | Share runtime `is`/Effective Type rules, Type identity, and Origin preservation; define Pattern binding/coverage separately. Do not infer exhaustive open hierarchies from known subclasses or require hidden dynamic metadata on value borrows. |
| OR Pattern | Agree on binding names, Types, mutability, and acquisition across alternatives; define guard evaluation count |
| Range / Rest / Array | Extend coverage explicitly; Rest lengths and dynamic indices do not become implicit Move Paths |
| Structural Patterns on `uniq` candidates / exclusive decomposition | Define syntax, shared/exclusive Reborrow, overlapping Loans, and invalidation by Case replacement |
| Public non-exhaustive enum | Require explicit declaration and client catch-all rules; do not silently add unknown Cases to closed enums |
| Other binding constructs | Specify permitted refutability, failure control flow, and scopes for each construct |
| Guard candidate capture | Define explicit read-value acquisition syntax/timing and Origin/Loan escape checks; no `@copy` form exists yet |
| Pattern binding acquisition selection | Define how to borrow a Copy-capable payload from its original Place, including syntax, result Type, Origins/Loans, and acquisition timing. No such binding selector is introduced; this is distinct from guard capture. |

Object-Semantics enum construction/matching, empty enums, and representation/ABI guarantees also remain outside the [initial enum rules](#631-cases-and-payloads).

#### D.2. Concurrency, memory model, and thread transfer

**Deferred design.** Source threads/tasks, a language memory model, data-race rules, atomic ordering operations, thread-transfer/shared-access capabilities analogous to Send/Sync, and cross-thread static initialization are not specified. The initial execution model is single-threaded under §22.2. arc guarantees atomic reference-count updates only; it does not guarantee thread-safe payload access, publication, destruction, or transfer. Owned proves lifetime independence, not thread safety. Future concurrency capability requirements may reject programs or foreign integrations previously accepted by a pre-alpha compiler; neither arc nor Owned preauthorizes those integrations.

### Appendix E. Terminology index

This index is a reading aid. The linked sections contain the authoritative definitions and restrictions.

| Term | Meaning | Defined in |
| --- | --- | --- |
| Access Designator | A resolved access target, without a promise of storage or Consume permission. | [Value model](#34-values-places-and-storage) |
| Adaptation Target | Core or object View Target and Semantics requested by `@`; result Origins are inferred. | [Explicit operations](#1351-forms-and-adaptation-targets) |
| API signature | Exposed Types and requirements checked for accessibility, beyond overload identity. | [API signature accessibility](#932-api-signature-accessibility) |
| Associated Type | Ordinarily a Core binding fixed by explicit Type-identity facts; Core iteration Element requirements have the explicit complete-Type exception in §22.1. | [Associated Types](#843-associated-types) |
| Binding | Associating source names and operations with declarations and meanings. | [Name resolution](#9-names-signatures-and-access) |
| Binding Identity / Value Instance | Resolved binding / its currently held value | [Stable bindings](#14101-stable-bindings-and-effective-types) |
| Body | A scoped single expression/statement after `=>`, or an indented sequence whose direct expression values are discarded. | [Body forms and results](#142-blocks-and-evaluation-contexts) |
| Callable / Call Receiver Requirement | Declared generic access / concrete minimum body access | [Callable constraints](#86-callable-constraints), [call receivers](#763-call-receiver-and-acquisition) |
| Candidate Place | Initialized matched storage designated for guard reading and later body acquisition. | [Guards](#1483-guards) |
| Case / Payload | An enum alternative / its attached positional data. | [Enums](#63-enums) |
| Closure / Environment / Capture | A callable body and values acquired when it is created | [Function expressions](#76-function-expressions) |
| CodeContext | Source-local lookup and diagnostic context for one immutable source snapshot. | [Compiler requirements](#appendix-a-compiler-implementation-requirements) |
| Compilation | One Project processed under fixed source, dependency, target, and build inputs. | [Build units](#201-build-units) |
| Compilation root | Lookup entry point for the project root and direct-dependency reference names. | [Modules](#18-modules-and-dependencies) |
| Compiler-intrinsic Contract | A compiler-recognized Contract with individually specified language effects or derivation rules. | [Intrinsic Contracts](#847-intrinsic-contracts-and-guarantees) |
| Complete value | An aggregate with completed construction and all stored fields Initialized. | [Construction and completeness](#1512-aggregate-construction-and-completeness) |
| Completion | Normal or abrupt completion of evaluation, distinct from divergence and Abort Termination. | [Completions](#141-completions) |
| Composition Root | Reserved root for language-provided `$` operations; only `$abort(...)` is defined in this revision. | [Reserved syntax](#138-extension-boundaries-and-reserved-syntax) |
| Conditional Conformance | A verified conformance path available when its declared Type-argument conditions are Proven. | [Conditional conformance](#848-conditional-conformance) |
| Conformance | A Type's fulfillment of a Contract, including associated-Type bindings and requirement-to-implementation mappings. | [Conformance](#844-conformance) |
| Conformance Identity | The pair of concrete Type Identity and Contract Identity, shared by explicit and refinement-implied conformance. | [Conformance](#844-conformance) |
| Constraint | A condition imposed on a Type or Type Semantics. | [Constraints](#82-constraints) |
| Constraint Clause | A declaration clause expressing a Constraint as `subject is requirement`. | [Constraints](#82-constraints) |
| Constraints | The set of conditions required for a declaration to be valid or usable. | [Constraints](#82-constraints) |
| Consume | Non-Copy value acquisition that transfers ownership/capability from a Movable Place. | [Movable Places](#1515-movable-places) |
| Consume Eligibility | Whether the declaration, Type, and path provide the Consume operation. | [Consume verification](#1514-consume-verification-and-representation) |
| Consume Legality | Whether the current use site may perform an eligible Consume. | [Consume verification](#1514-consume-verification-and-representation) |
| Contract | A capability declaration containing requirements, associated Types, and Constraints, without implementations or storage. | [Contracts](#84-static-contracts) |
| Contract refinement | Inheritance of all parent requirements and Constraints; conformance entails ancestor conformance. | [Refinement](#842-refinement) |
| Copy | Implicit value duplication that leaves its source initialized. | [Copy and Move](#35-copy-and-move) |
| Core | A Type's value kind, structure, and identity, distinct from its outer Semantics and Origins. | [Type composition](#3-types-and-values) |
| Core Kotonoha | The compiler-compatible foundation module referenced as `Core`. | [Required declarations](#221-required-core-declarations) |
| Declaration Container | A named declaration scope with members permitted by its kind. | [Containers](#61-declaration-containers) |
| Deferred Block | Cleanup code registered by `defer` for its containing scope's exit. | [Deferred Blocks](#161-deferred-blocks) |
| Deferred Obligation | A legitimate dependent check retained with its evidence, environment, and deadline. | [Generic checking](#810-generic-body-checking-and-deferred-obligations) |
| Delimiter region / Body scope | Syntactic nesting region / local name and cleanup scope. | [Layout](#22-lines-indentation-and-continuation), [Body scopes](#1431-body-forms-and-scopes) |
| Destruction responsibility | Responsibility for ending an owned value's lifetime under the cleanup rules. | [Value model](#34-values-places-and-storage) |
| Directive Binding | Resolution and validation of compile-time Condition names and dependencies. | [Compiler requirements](#appendix-a-compiler-implementation-requirements) |
| Discard Context | An evaluation context that does not retain an expression's result. | [Evaluation contexts](#142-blocks-and-evaluation-contexts) |
| Do expression | Executes a scoped body once; an optional label receives named exit. | [Do expressions](#1432-do-expressions) |
| Dynamic Type / Runtime Type Identity | Actual constructed Core / its runtime comparison identity | [Object views](#335-object-views-and-identity), [metadata](#2121-type-identity-and-descriptors) |
| Effective access domain | Source contexts permitted by a declaration's access and enclosing restrictions. | [Access domains](#931-effective-access-domains-and-protected-receivers) |
| Effective Type / Flow State | Point-specific guaranteed Type / coordinated analysis facts | [Refinement](#1410-type-refinement) |
| Environment Condition | A Boolean directive expression over fixed target and Project settings. | [Condition evaluation](#193-condition-evaluation-and-selection) |
| Finalization | Acceptance of a declaration, layout, specialization, or body after required checks are resolved. | [Compiler terminology](#appendix-a-compiler-implementation-requirements) |
| Function Item / common Function Type | Concrete declaration identity / shared erased calling contract | [Callable Types](#321-callable-value-types) |
| GenericArity / OriginArity | Generic slot count (including function lengths) / explicitly declared Origin count; one pair consumes one slot. | [Signatures](#91-signatures) |
| Getter result Type | The declared result of custom/computed/required get; matches its Property header Type. | [Accessor contracts](#112-accessor-functions) |
| Instantiation / explicit full specialization / automatic specialization | Argument binding / mandatory user implementation selection / meaning-preserving Type-specific code generation. | [Generic code generation](#213-generic-code-generation) |
| Koto | A compiler syntax-tree node. | [Compiler terminology](#appendix-a-compiler-implementation-requirements) |
| Kotonoha | One named source or binary module. | [Modules](#18-modules-and-dependencies) |
| Mod / ModId | One registered source-generation step / its stable identity within a Compilation. | [Mods](#207-mods-source-generation) |
| Loan | A borrowed place, access mode, and validity region. | [Borrow checking](#156-borrow-checking) |
| Lookup environment | Declarations and aliases available for lookup in a scope; extensions are a future design. | [Name resolution](#9-names-signatures-and-access) |
| Move | Transfer of a value and responsibility or capability, marking its source Moved. | [Copy and Move](#35-copy-and-move) |
| Move Path | A statically tracked path with independent initialization state and destruction responsibility. | [Move Paths](#1513-move-paths-and-partial-move) |
| ObjectCompatible | Public receiver-preservation guarantee per call operation | [Object calls](#1244-object-receiver-compatibility) |
| Owned / OwnedOrigins | Lifetime independence from non-static dependencies / the conservative Origin closure proving it | [static and Owned](#1523-static-and-owned) |
| Origin | A set of program points where a borrow is guaranteed valid. | [Origin expressions](#1521-origin-expressions) |
| Partial Move | Transfer of an aggregate's part, leaving the aggregate incomplete. | [Move Paths](#1513-move-paths-and-partial-move) |
| Pattern / Guard | Structural or binding syntax / an optional Boolean test selecting a match arm | [Match](#148-match-expressions-and-patterns) |
| Place | A storage location that can hold a value. | [Value model](#34-values-places-and-storage) |
| Project root | Root of the primary Kotonoha's declaration hierarchy. | [Modules](#18-modules-and-dependencies) |
| Provisional Binding | Mod-time semantic results that do not constrain final Binding. | [Mod Binding](#2072-compilation-and-binding) |
| Property Witness Mapping | Verified requirement-to-Property operations with Type/Origin substitutions and optional standard-operation bridges. | [Witness adaptation](#1142-standard-operation-witnesses) |
| Field | The storage slot of a let/var Property; source access obeys its accessor permissions. | [Stored Properties](#11-properties) |
| Property | A let/var stored member or computed operation member; a Contract property requires operations. | [Properties](#11-properties) |
| Reborrow | A borrow derived from an existing borrow, subject to the parent's capability and Origin. | [Reborrowing](#1563-reborrowing) |
| Result source / Target Result Type / Expression Type | A value-supplying site / its target constraint / the checked expression's Type. | [Results](#149-result-validation) |
| Scalar | Integer, floating-point, Boolean, or Character Core; short for Primitive scalar. | [Primitive cores](#31-primitive-cores) |
| Semantics | A value's representation, ownership, borrowing, access, and safety rules; also called Type Semantics. | [Type Semantics](#33-type-semantics) |
| SemanticsTarget | Fixed kind of a pair's direct target: a complete value Type or permitted Object View Target. | [Generic parameters](#81-generic-type-parameters) |
| Signature | Information distinguishing declarations in the same scope. | [Signatures](#91-signatures) |
| SourceDocument | One immutable source input, including path and text, belonging to a Kotonoha. | [Source text](#21-source-text-and-encoding) |
| Structural Completion / Runtime Reachability | Common structural path model / that model with execution state and cleanup. | [Reachability](#1492-reachability) |
| Subject Place | Internal storage acquired once before match arm selection | [Match lifetime](#1516-match-acquisition-and-lifetime) |
| Temporary Place | Anonymous storage materializing a Temporary Value. | [Materialization](#361-materialization) |
| Temporary Value | An expression's temporary result, distinct from its original persistent Place. | [Materialization](#361-materialization) |
| Transfer target / Lookup barrier | A construct receiving a transfer / a boundary stopping target lookup. | [Target lookup](#1452-target-lookup) |
| Type | A complete type, including Semantics, its target, and all Origin dependencies. | [Type composition](#3-types-and-values) |
| Type-checking continuation | Unreachable-code checking that adds no execution edge. | [Unreachable checking](#14103-type-checking-unreachable-code) |
| Value Context | An evaluation context that requires an expression's value. | [Evaluation contexts](#142-blocks-and-evaluation-contexts) |
| View Target / Supports | Public object target / concrete-Type relationship to that target | [Object views](#335-object-views-and-identity) |

### Appendix F. Syntax summary

**Non-normative syntax reference.** This appendix collects the specified forms; the linked language sections remain authoritative. It is not a standalone parser-generator grammar. Context-sensitive placement, token boundaries, and the open productions listed in F.9 remain part of the syntax definition; an open production does not accept arbitrary text.

In the EBNF below, quoted text is literal syntax, `|` is choice, parentheses group, and `?`, `*`, `+` mean optional, zero or more, and one or more. `? description ?` denotes a lexical class or a production whose definition is linked. `List<X>` abbreviates `X ("," X)*`; `TrailingList<X>` adds an optional final comma. These angle brackets are grammar notation, unlike quoted `"<"` and `">"`.

`NEWLINE`, `INDENT`, `DEDENT`, and `SEP` denote source-layout events under [source structure](#22-lines-indentation-and-continuation), not required internal token kinds. Layout within delimiters and between branch clauses follows the linked constructs. `IndentedList<X>` abbreviates `NEWLINE INDENT ItemList<X> NEWLINE? DEDENT`; executable `Body<X>` additionally permits a single item after `=>` (F.5). `ItemList<X>` is a nonempty sequence separated where the surrounding grammar permits. Declaration Containers may additionally have empty bodies where their own rules permit them.

#### F.1. Lexical grammar

[Source and layout](#2-source-and-lexical-structure), [Names and keywords](#25-names), [numeric literals](#26-number-literals), [escapes](#27-character-escapes), [character literals](#28-character-literals), [strings](#29-string-literals).

```ebnf
Name                 := NameStart NameContinue*

Punctuator           := "(" | ")" | "[" | "]" | "," | ";" | "." | ":" | "::"
                      | "->" | "=>" | "@" | "#" | "$" | "?"
                      | "+" | "-" | "*" | "/" | "%" | "++" | "--"
                      | "=" | "+=" | "-=" | "*=" | "/=" | "%="
                      | "==" | "!=" | "<" | "<=" | ">" | ">="
                      | "&" | "|" | "^" | "<<" | ">>" | "&=" | "|=" | "^="
                      | "<<=" | ">>=" | ".." | "..="
                      | "{" | "}" | "!" | "&&" | "||"
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

binary-literal       := '0' ('b' | 'B') '_'* binary-digit binary-tail
octal-literal        := '0' ('o' | 'O') '_'* octal-digit octal-tail
hexadecimal-literal  := '0' ('x' | 'X') '_'* hexadecimal-digit hexadecimal-tail

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

#### F.2. Type grammar

[Type composition](#3-types-and-values), [compound Types](#32-compound-types), [Semantics](#33-type-semantics), [generic application](#1242-invocation-and-generic-application), [Origins](#153-abstract-origins).

```ebnf
Type                 := FunctionType | TypeHead
FunctionType         := FunctionParameters "->" Type
FunctionParameters   := "(" TrailingList<Type>? ")"
TypeHead             := SemanticsType OriginAnnotation?
SemanticsType        := Semantics "/" SemanticsType | TypeAtom
TypeAtom             := CoreType | "(" Type ")"
ObjectSemantics      := "obj" | "rc" | "arc" | "objref" | "objuniq"
RuntimeContractType  := ContractReference
CoreType             := NamedType | UnitType | TupleType | FixedArrayType
FixedArrayType       := "[" ArrayLength "of" ArrayElementType "]"
ArrayElementType     := Type | "_"
ArrayLength          := IntegerLiteral | ConstantName | "(" LengthExpression ")"
ConstantName         := "::"? Name ("." Name)*
LengthExpression     := LengthProduct (("+" | "-") LengthProduct)*
LengthProduct        := LengthUnary (("*" | "/" | "%") LengthUnary)*
LengthUnary          := ("+" | "-") LengthUnary | IntegerLiteral
                      | ConstantName | "(" LengthExpression ")"
UnitType             := "(" ")"
TupleType            := "(" Type "," TrailingList<Type>? ")"
NamedType            := "::"? TypeSegment ("." TypeSegment)*
TypeSegment          := TypeName TypeArguments?
TypeName             := Name | PrimitiveType | "Self"
TypeArguments        := "<" TrailingList<GenericArgument> ">"
GenericArgument      := Type | ArrayLength
PrimitiveType        := "isize" | "usize" | "i8" | "i16" | "i32" | "i64" | "i128"
                      | "u8" | "u16" | "u32" | "u64" | "u128"
                      | "f32" | "f64" | "bool" | "char" | "string"
Semantics            := "owner" | "ref" | "uniq" | "obj" | "rc" | "arc"
                      | "objref" | "objuniq" | "unsafe" | Name
GenericParameters    := "<" TrailingList<GenericParameter> ">"
GenericParameter     := NamedParameter | PairParameter | LengthParameter
NamedParameter       := Name
PairParameter        := Name "/" Name
LengthParameter      := "length" Name
```

`CoreType` and `NamedCoreType` remain grammar production names. They describe syntactic forms, not the full classification of Cores in §3; Function Types use a separate production, and generated callable Cores have no declaration spelling.

Object-target syntax uses the View Target lookup role; in the [runtime Contract extension](#85-runtime-contracts), a named target may resolve to a valid `RuntimeContractType` instead of a Core. Runtime designation and View bindings are not supplied by this grammar. A bare Contract is not a value Type. This shared syntax permits no arbitrary Object Semantics around an already Semantics-applied Type. Layer legality and Origin attachment follow [nested Semantics](#336-nested-semantics-and-type-grouping). `NamedType` also preserves dotted associated-Type projection syntax; its Contract and Core roles are resolved under F.3. Callable signature syntax appears with requirements below; generated Closure and Function Item Types have no source declaration spelling.

GenericParameters and TypeArguments are nonempty and allow trailing commas. NamedParameter and PairParameter declare Type slots; only LengthParameter declares a function length slot. A pair consumes one complete Type argument and is recognized only by declaration-side `Name / Name`. Preserve syntactically ambiguous Name/grouped GenericArguments until Binding checks their declared slot kind under [length parameters](#44-function-length-parameters); do not infer slot kinds from uses. `of` is contextual only after ArrayLength as the element delimiter. `_` as ArrayElementType is allowed only in a local binding annotation with an initializer. LengthParameter is forbidden on Type declarations. Standalone Semantics slots, general Const arguments, partial/default/variadic arguments, and other `_` Type arguments are not introduced; Origin arguments follow their separate rules.

#### F.3. Declaration grammar

[Containers](#61-declaration-containers), [structures](#62-structure-declarations), [enums](#63-enums), [Constraints](#82-constraints), [bindings](#64-bindings), [functions](#7-functions-and-callable-values), [parameters](#72-parameter-names-and-defaults), [aliases](#181-external-references-and-aliases).

```ebnf
QualifiedName        := Name ("." Name)*
Access               := "private" | "internal" | "public" | "protected"
                      | "protected" "internal" | "private" "protected"
AliasDeclaration     := "alias" QualifiedName
LocalBinding         := ("let" | "var") Name (":" Type)? ("=" Expression)?
GroupDeclaration     := Access? "group" Name ContainerBody
RootGroupDeclaration := Access? "rootgroup" QualifiedName ContainerBody
StructureDeclaration := Access? "open"? "struct" Name GenericParameters?
                        BaseClause? OriginParameters? ContainerBody
BaseClause           := ":" CoreType
EnumDeclaration      := Access? "enum" Name GenericParameters? OriginParameters?
                        IndentedList<EnumItem>
EnumItem             := EnumCase | AttributedFunctionDefinition | SpecializationDeclaration | ConstraintClause
                      | AssociatedTypeSpecification | ConditionalConformance
                      | Directive<EnumItem>
EnumCase             := Name ("(" TrailingList<Type> ")")?
ContractDeclaration  := Access? "contract" Name ContractParentList? IndentedList<ContractItem>?
ContractParentList   := ":" ContractReference ("," ContractReference)*
ContractReference    := "::"? QualifiedName
ContractItem         := ContractRequirement | AssociatedTypeDeclaration
                      | ConstraintClause | Directive<ContractItem>
ContractRequirement  := FunctionRequirement | PropertyRequirement
FunctionRequirement  := "unsafe"? "func" Name GenericParameters? OriginParameters?
                        "(" TrailingList<RequirementParameter>? ")" ("->" Type)?
                        RequirementConstraints?
RequirementParameter := Name ("=>" Name)? ":" Type
RequirementConstraints := IndentedList<ConstraintClause>
PropertyRequirement  := "property" Name ":" Type
                        ("has" RequiredAccessor ("," RequiredAccessor)*
                         | IndentedList<RequiredSignature>)
RequiredAccessor     := "get" | "set"
RequiredSignature    := GetterSignature | SetterSignature
AssociatedTypeDeclaration := "associate" Name ("is" IsRequirement)?
AssociatedTypeSpecification := "associate" AssociatedTypeName "is" IsRequirement
AssociatedTypeName   := Name | ContractReference "." Name
AssociatedTypeReference := TypeQualifier "." Name
                        | TypeQualifier "." ContractReference "." Name
TypeQualifier        := NamedType
ConformanceClause    := "Self" "is" ContractReference
ConditionalConformance := "Self" "is" ContractReference "when" ConformanceConditions
                          IndentedList<ConditionalImplementationItem>?
ConformanceConditions := ConformanceCondition ("," ConformanceCondition)*
ConformanceCondition := ConstraintSubject "is" ConformanceRequirement
ConformanceRequirement := ConformanceRequirementAtom ("and" ConformanceRequirementAtom)*
ConformanceRequirementAtom := CallableRequirement | Type | Semantics | SemanticsCategory
                           | "(" ConformanceRequirement ")"
ConditionalImplementationItem := AttributedFunctionDefinition
                               | AttributePrefix? ComputedDeclaration
                               | AssociatedTypeSpecification
                               | Directive<ConditionalImplementationItem>
ConstructorDeclaration := Access? "init" "(" TrailingList<Parameter>? ")"
                          BaseInitializer? ExecutableBody
BaseInitializer      := ":" "base" "(" TrailingList<Argument>? ")"
FunctionHeader       := Access? "unsafe"? "func" Name
                        GenericParameters? OriginParameters?
                        "(" TrailingList<Parameter>? ")" ("->" Type)?
FunctionDefinition   := FunctionHeader FunctionBody
AttributedFunctionDefinition := AttributePrefix? FunctionDefinition
ForeignFunctionDeclaration := FunctionHeader
// Only with the LibraryImport prefix and restricted header/placement in §22.3.
SpecializationDeclaration := "specialize" "func" Name TypeArguments
                            "(" TrailingList<SpecializationParameter>? ")"
                            ("->" Type)? SpecializationBody
SpecializationParameter := Name ("=>" Name)? ":" Type
SpecializationBody   := ExecutableBody
FunctionBody         := Body<FunctionItem>
Parameter            := Attribute* ParameterCore
ParameterCore        := Name ("=>" Name)? ":" Type ("=" Expression)?
                      | Name "?" ":" Type "=" Expression
ConstraintClause     := ConstraintSubject "is" IsRequirement
ConstraintSubject    := Name | "Self" | AssociatedTypeReference
IsRequirement        := "not" Requirement | PositiveRequirement
PositiveRequirement  := RequirementAtom ("and" RequirementUnary)*
                        ("or" RequirementAnd)*
Requirement          := RequirementAnd ("or" RequirementAnd)*
RequirementAnd       := RequirementUnary ("and" RequirementUnary)*
RequirementUnary     := "not" RequirementUnary | RequirementAtom
RequirementAtom      := CallableRequirement | Type | Semantics | SemanticsCategory
                      | "(" Requirement ")"
SemanticsCategory    := "value" | "valueborrow" | "object" | "objectborrow"
                      | "borrow" | "owning" | "reference"
CallableRequirement  := "Callable" "<" (CallableReceiver ",")? FunctionSignature ">"
CallableReceiver     := "ref" | "uniq" | "owner"
FunctionSignature    := FunctionParameters "->" Type
FunctionItem         := Expression | LocalBinding | AttributedFunctionDefinition
                      | Statement | ConstraintClause | Directive<FunctionItem>
ContainerItem        := Declaration | ConstraintClause | AssociatedTypeSpecification
                      | ConditionalConformance
                      | Directive<ContainerItem>
ContainerBody        := ? optional indented ContainerItem sequence for group/rootgroup/struct,
                         with omission and emptiness governed by §6.1.1 ?
Attribute            := "#" AttributeName ("(" TrailingList<Argument>? ")")?
AttributeName        := ? Name beginning with an uppercase Unicode letter, §6.5 ?
AttributePrefix      := Attribute (Attribute | NEWLINE)*
Declaration          := AttributePrefix? UnattributedDeclaration
UnattributedDeclaration := GroupDeclaration | RootGroupDeclaration
                      | StructureDeclaration | ContractDeclaration
                      | FunctionDefinition | SpecializationDeclaration | StoredPropertyDeclaration | ComputedDeclaration
                      | ConstructorDeclaration | DeinitDeclaration
                      | EnumDeclaration | ForeignFunctionDeclaration
```

Modifier placement and compound-access combinations are constrained by [accessibility](#93-accessibility-and-reachability), even where the shared grammar uses `Access`.

AttributePrefix is allowed only on the declarations and parameters enumerated in §6.5; the shared Declaration wrapper does not authorize Attributes on constructors, deinit, or explicit specializations. Attribute-bearing functions in executable/enum lists use AttributedFunctionDefinition. ForeignFunctionDeclaration requires exactly the LibraryImport form of §22.3 and cannot appear in those lists. `open` applies only to structures. `BaseClause` has the semantic restrictions in [inheritance](#622-inheritance-and-open-structures); unavailable declaration modifiers use §2.5.1's diagnostic recognition, not grammar productions.

`SpecializationDeclaration` is permitted only in the original generic function's declaration Container and Kotonoha. It adds no Type or Origin binder, Constraints, access/unsafe modifiers, attributes, defaults, or optionality markers. Its body uses ordinary executable syntax; the original function's contract is inherited under [full specialization](#88-explicit-full-function-specialization). Written Type arguments must be closed after normalization, except for Origins governed by that inherited contract.

Omitting the result annotation in a named function declaration or Contract function requirement means Unit; the optional grammar does not authorize body-based return inference. Anonymous functions retain their own inference rules.

Ordinary parameter bindings are immutable under §7. Receiver recognition uses the internal Name self, its position, and declaration context under §7.3; the shared Parameter production alone does not grant receiver defaults, renaming, or arbitrary Types. Default evaluation and cleanup follow §7.2. Empty group/rootgroup/struct/contract declarations follow §6.1.1; empty enums remain invalid.

Contract requirements have no access modifiers, default/optional parameters, Property initializers, or executable bodies. `RequirementConstraints` is an optional nonempty indented list of Constraint Clauses; method Generic and Origin parameters remain ordinary function parameters. Property Requirements are instance-only: get is mandatory and set optional, each at most once in either order. No accessor is implied beyond the written list or explicit signatures. Shared/exclusive defaults for has and explicit signature checks follow §11.4.

`AssociatedTypeDeclaration` introduces a name only inside a Contract. `AssociatedTypeSpecification` requires an existing associated Type of the enclosing Type's declared or implied conformances; a bare `associate Element` is not a specification. `ConformanceClause` is the Type-body interpretation of an unconditional Constraint Clause. `ConditionalConformance` instead occupies a member position only in generic struct/enum bodies (§8.4.8), has one target Contract, and introduces no namespace or generic binders. Its positive-only condition grammar does not change PositiveRequirement. Enum conditional blocks reject computed declarations; all blocks reject storage, Cases, constructors, deinit, nested Types, and nested conformances. Intrinsics retain their own rules.

Constraint subjects follow their declaration context: a Contract body constrains its own/inherited associated Types; a function constrains its generic parameters and projections rooted in them; a Type body uses its ordinary Constraints and conformance rules. These productions do not broaden `#if`/`#case` Conditions.

`ContractReference` resolves a nongeneric user-defined Contract through normal qualification and aliases; built-in parameterized requirements have separate productions. `TypeQualifier` must resolve to a Core, parameter, `Self`, or associated-Type projection, not a value or Semantics-applied expression. The Parser distinguishes declarations, specifications, Type positions, and Constraints-only regions by context, preserving dotted paths. Binding resolves the Contract/associated-Type roles and rejects distinct successful interpretations; expected results and value-member fallback cannot disambiguate them. Apply the same rules after ordinary compile-time selection.

`ConstructorDeclaration` and `DeinitDeclaration` are allowed only directly in structure bodies, subject to their merging and selection rules. A constructor has a Unit executable body but produces an owned structure through its dedicated construction operation. Neither declaration is an ordinary function declaration; constructor Origin bindings come from the containing Type's Constraints. See [constructors](#623-constructors) and [destruction declarations](#163-aggregate-destruction-and-deinit).

#### F.4. Expression grammar

[Primary forms](#1232-names-literals-and-grouping), [calls](#1242-invocation-and-generic-application), [precedence](#131-precedence-and-associativity), [runtime type tests](#1361-runtime-is-tests), [explicit operations](#135-explicit-operations), [assignment](#137-assignment).

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
OperationTarget      := Semantics | AdaptationType
AdaptationType       := Semantics "/" AdaptationType | AdaptationAtom
AdaptationAtom       := NamedType | UnitType | "(" OriginFreeType ")"
                      | "(" OriginFreeType "," TrailingList<OriginFreeType>? ")"
                      | "[" ArrayLength "of" OriginFreeType "]"
OriginFreeType       := ? Type with no written Origins at any layer, §13.5.1 ?
Prefix               := ("+" | "-" | "not" | "*" | "^" | "++" | "--") Prefix
                      | Postfix
Postfix              := Primary PostfixSuffix*
PostfixSuffix        := "." (Name | DecimalTupleIndex)
                      | "(" TrailingList<Argument>? ")"
                      | AdjacentTypeArguments | "[" Expression "]" | "++" | "--"
AdjacentTypeArguments := ? TypeArguments adjacent to an eligible Name, §12.4.2 ?
DecimalTupleIndex    := ? decimal integer literal used as a Tuple member, §12.4.1 ?
ConstantIndexExpression := IntegerLiteral | "(" ConstantIndexExpression ")"
// Static fixed-array path recognition only, not a restriction on ordinary indexing (§15.1.3).
Argument             := (Name ":")? Expression
Primary              := "::"? Name | Literal | "(" Expression ")" | TupleExpression
                      | ArrayExpression | DictionaryExpression | FunctionExpression
                      | IfExpression | MatchExpression | LabeledSelection | Iteration | DoExpression
                      | Transfer | CompositionRootExpression | ConstructionExpression
                      | InferredCaseExpression
ConstructionExpression := NamedType "." "init"
                          "(" TrailingList<Argument>? ")"
InferredCaseExpression := "." Name ("(" TrailingList<Expression> ")")?
TupleExpression      := "(" Expression "," TrailingList<Expression>? ")"
ArrayExpression      := "[" TrailingList<Expression>? "]"
DictionaryExpression := "[" ":" "]" | "[" TrailingList<DictionaryEntry> "]"
DictionaryEntry      := Expression ":" Expression
FunctionExpression   := "func" CaptureList? "(" TrailingList<AnonymousParameter>? ")"
                        ("->" Type)? AnonymousBody
AnonymousParameter   := Name (":" Type)?
AnonymousBody        := ExecutableBody
CaptureList          := "[" TrailingList<Capture>? "]"
Capture              := Name ("@" CaptureOperation)? | "var" Name
CaptureOperation     := "ref" | "uniq"
CompositionRootExpression := "$" "abort" "(" Expression ")"
```

Ordinary `is` / `is not` accepts one named struct Core and does not consume outer `and` / `or`. The separate compile-time [requirement expressions](#83-requirement-expressions) retain their existing extent in their dedicated contexts. Anonymous parameter/result omission and Capture Lists obey [function-expression rules](#76-function-expressions). Adaptation-target parsing and generic/comparison disambiguation follow [precedence](#131-precedence-and-associativity); these boundaries are not alternative parses selected by conversion success.

Qualified enum Case expressions have no separate Primary production: ordinary Postfix syntax is classified during Binding under §6.3.2. Only InferredCaseExpression is a dedicated expression production; CaseReference belongs to the Pattern grammar in F.5.

The `.init(` suffix has construction priority under §6.2.3 and cannot use `Name` in `PostfixSuffix`; its qualifier is checked in the Type role during Binding. Adaptation alternatives obey the syntactic prefix commitment of §13.5.1 before lookup, including Origin-free generic arguments. `$abort` accepts exactly one positional Expression, without a label or trailing comma; it must fit `string`.

#### F.5. Statements and Blocks

[Body forms](#142-blocks-and-evaluation-contexts), [layout](#22-lines-indentation-and-continuation), [labels](#144-labels), [transfers](#1451-syntax-and-operands), [patterns](#1481-patterns), [defer](#161-deferred-blocks).

```ebnf
SourceUnit           := ? source-local declarations, aliases, and executable items, §6.1.1 ?
Body<Item>           := "=>" SingleItem | IndentedList<Item>
SingleItem           := Expression | Statement
Statement            := UnsafeStatement | DeferStatement | RequireStatement
ExecutableItem       := Expression | LocalBinding | AttributedFunctionDefinition
                      | Statement | Directive<ExecutableItem>
ExecutableBody       := Body<ExecutableItem>
UnsafeStatement      := "unsafe" ExecutableBody
DeferStatement       := "defer" ExecutableBody
RequireStatement     := "require" Expression RequireJoin "else" ExecutableBody
RequireJoin          := ? same-line or next effective aligned line, §2.2.1 ?
DoExpression         := (Name ":")? "do" ExecutableBody
LabeledSelection     := Name ":" (IfExpression | MatchExpression)
Iteration            := (Name ":")? (ForExpression | WhileExpression | LoopExpression)
ForExpression        := "for" ForBinding "in" Expression ExecutableBody
ForBinding           := Name | "(" List<Name> ")"
WhileExpression      := "while" Expression ExecutableBody
LoopExpression       := "loop" ExecutableBody
IfExpression         := "if" Expression ExecutableBody
                        (BranchJoin "else" "if" Expression ExecutableBody)*
                        (BranchJoin "else" ExecutableBody)?
BranchJoin           := ? same-line single-item join, or next effective aligned line
                          after body closure, §2.2.1 ?
MatchExpression      := "match" Expression IndentedList<MatchArm>
MatchArm             := Pattern ("if" Expression)? ExecutableBody
Pattern              := "_" | LiteralPattern | BindingPattern | CasePattern
                      | UnitPattern | TuplePattern | GroupedPattern
BindingPattern       := ("let" | "var") Name
CaseReference        := NamedType "." Name | "." Name
CasePattern          := CaseReference ("(" TrailingList<Pattern> ")")?
UnitPattern          := "(" ")"
TuplePattern         := "(" Pattern "," TrailingList<Pattern>? ")"
GroupedPattern       := "(" Pattern ")"
LiteralPattern       := BooleanLiteral | "-"? IntegerLiteral
                      | CharLiteral | NonInterpolatedStringLiteral
BooleanLiteral       := "true" | "false"
NonInterpolatedStringLiteral := ? ordinary or raw StringLiteral without interpolation ?
Transfer             := "return" Expression?
                      | "exit" (Expression? | "to" Name (":" Expression)?)
                      | "continue" ("to" Name)?
                      | "yield" (Expression? | "to" Name (":" Expression)?)
DeinitDeclaration    := "deinit" ExecutableBody
```

Conditions and guards must fit bool. Body headers, operand starts, delimiter regions, and required grouping follow §2.2 and §14.5. A label and its construct share a physical line. The keyword do is reserved; to is contextual immediately after exit/continue/yield. SingleItem does not include declarations or directives. Function-like bodies use the corresponding Body item category, not the declaration-container list grammar.

CaseReference qualifiers identify enum Cores without the enum's own Origin annotations; their generic argument Types retain complete Type information. Case existence, expected-Type resolution, payload presence/count, access, and Semantics follow §6.3.2. Payload Cases require parentheses; payload-free Cases prohibit them. Binding and acquisition follow §14.8. Match arm lists and enum bodies remain nonempty after selection.

#### F.6. Property grammar

[Standard access](#111-standard-access-and-acquisition), [accessor functions](#112-accessor-functions), and [requirements](#114-contract-property-requirements) define the semantic checks. PropertyRequirement is defined in F.3.

```ebnf
StoredPropertyDeclaration := Access? ("let" | "var") Name
                             (":" Type)? ("=" Expression)? IndentedList<StoredAccessor>?
ComputedDeclaration  := Access? "computed" Name ":" Type IndentedList<CustomAccessor>
StoredAccessor       := Access? ("get" | "set") | CustomAccessor
CustomAccessor       := Access? GetterSignature AccessorBody
                      | Access? SetterSignature AccessorBody
GetterSignature      := "get" "(" AccessorReceiver? ")" "->" Type
SetterSignature      := "set" "(" (AccessorReceiver ",")? "value" ":" Type ")" "->" UnitType
AccessorReceiver     := "self" ":" Type
AccessorBody         := ExecutableBody
```

Each concrete accessor occurs at most once, in either order. Let forbids set; var supplies omitted standard get/set. A bodyless standard accessor has no explicit signature. Custom accessors require full signatures; stored receiver/Copy/Type restrictions follow §11.2. Static accessors omit self. Computed requires a custom get and optional custom set, with no initializer or inline has. Stored Type omission requires an initializer; static storage always requires one. Attributes/containers follow §6.5 and §11; unavailable modifiers follow §2.5.1.

#### F.7. Origin grammar

[Origin expressions](#1521-origin-expressions), [Origin parameters](#153-abstract-origins), [named arguments](#1531-origin-arguments), [outlives notation](#1522-ordering-and-intersection).

```ebnf
OriginParameters     := "origin" List<OriginParameter>
OriginParameter      := Name (":" OriginBound)?
OriginBound          := Name | "static"
OriginAnnotation     := "from" (origin-expression | "(" TrailingList<OriginArgument> ")")
OriginArgument       := Name "=>" origin-expression
```

```text
origin-expression := Name
                   | origin-expression '.' Name
                   | static
                   | origin-expression 'and' origin-expression
```

An OriginParameter bound declares the [outlives relation](#1522-ordering-and-intersection) under §15.3. OriginBound Names resolve only to abstract Origins. The relation notation elsewhere does not introduce a standalone declaration or statement.

#### F.8. Compile-time directive grammar

[Directive syntax](#191-syntax-and-structural-selection), [closed Condition forms](#192-environment-condition-forms), [excluded-syntax parsing](#195-diagnostics-and-excluded-syntax).

```ebnf
Directive<Item>      := IfDirective<Item> | SwitchDirective<Item>
IfDirective<Item>    := "#" "if" CompileCondition (NEWLINE Item | IndentedList<Item>)
SwitchDirective<Item> := "#" "switch" NEWLINE INDENT CaseList<Item> DEDENT
CaseList<Item>       := CaseArm<Item>+ DefaultArm<Item>? | DefaultArm<Item>
CaseArm<Item>        := "#" "case" CompileCondition IndentedList<Item>
DefaultArm<Item>     := "#" "case" "_" IndentedList<Item>
CompileCondition    := CompileAnd ("or" CompileAnd)*
CompileAnd          := CompileComparison ("and" CompileComparison)*
CompileComparison   := CompileUnary (("==" | "!=") CompileUnary)?

CompileUnary        := "not" CompileUnary | CompileAtom
CompileAtom         := "true" | "false" | SignedInteger | PlainString | Name
                      | "(" CompileCondition ")"
SignedInteger       := ("+" | "-")? IntegerLiteral
IntegerLiteral      := ? integer alternatives of number-literal in F.1 ?
PlainString         := ? StringLiteral without interpolation, §19.2 ?

```

The hash forms use `#` followed by the reserved lowercase keywords `if`, `switch`, and `case`. Uppercase-initial `AttributeName` instead selects the Attribute grammar in F.3; other lowercase hash forms are errors. `Item` retains the surrounding syntax category; directives do not make an otherwise forbidden item legal there. Case layout and excluded-target grammar checking follow the linked sections.

#### F.9. Syntax boundaries

These entries record the limits of a complete syntax summary for this revision. They are not wildcard productions or newly defined features.

| Form or production | Owning syntax and boundary |
| --- | --- |
| Extension declarations | Not introduced; no production or active extension candidate stage exists in this revision. See [Container boundary](#61-declaration-containers). |
| Virtual/abstract/override declarations and ordinary base-member invocation | Not introduced; [extension design](#624-virtual-members-and-overrides). Unavailable modifiers are diagnosed under §2.5.1. Base clauses and base-constructor initializers are defined in F.3. |
| Runtime-contract designations, View associated-type bindings, exact/contract tests and checked casts | The [runtime extension](#85-runtime-contracts) and [object operations](#1362-general-view-tests-and-checked-casts) preserve the design without final source spellings. Static associated-Type specifications/projections are defined in F.3. Ordinary struct `is` and explicit upcasts are defined. |
| Callable extensions | Borrowed or Exclusive/Consuming erased Types, public lending results, non-escaping declarations, capture aliases/initializers/parts, generic receiver Semantics, and `from environment` remain unintroduced. |
| Additional Patterns | The [initial forms](#1481-patterns) are defined; Struct, Type, OR, Range, Rest, and other [extensions](#d1-enum-and-pattern-extensions) remain deferred. |
| Attributes | Syntax, placement, and Mod marker behavior follow [§6.5](#65-attributes); Layout follows [§21.1.2](#2112-layout-attribute-and-fragments), and LibraryImport follows [§22.3](#223-foreign-function-imports). Concrete marker registration and other general semantics remain design boundaries. |
| Additional Composition Root expressions | Only `$abort(...)` is given a complete operation syntax here; see [extension boundaries](#138-extension-boundaries-and-reserved-syntax). |
| Additional type arguments | [Generic application](#1242-invocation-and-generic-application) does not define general constant type arguments. |
| Object ownership operation spellings | [Core creation and strong-owner duplication](#1358-object-ownership-creation-and-sharing) have defined semantics; source names/signatures remain deferred. `Type.init` constructs an owner value, and `@obj`/`@rc`/`@arc` add no allocation or count increment. |
| Function parameters | The combined optional/external-name form remains under [parameter design boundaries](#109-inference-and-operation-design-boundaries); the separate forms are summarized in F.3. |
| Re-export, special FFI layouts, and failure propagation | See [Re-exports](#182-re-exports), [layout boundaries](#211-structure-layout-and-abi), and [error policy](#171-error-policy). |
