# 2. Source and lexical structure

[Specification index](../SPEC.md)

Source structure determines token boundaries and syntactic containment.

The punctuation, layout, and grammar rules in this chapter are language requirements. Error-recovery tokens and current parser acceptance are tracked separately in [STATUS.md](../STATUS.md). Literal-specific rules appear with each literal form.

## 2.1. Source text and encoding

Kimigayo source text uses UTF-8. Invalid UTF-8 source byte sequences are compile-time errors.

A **SourceDocument** is one immutable source snapshot (path and text) belonging to a source module. A **Kotonoha** is that named module; its dependency and source-environment rules appear under [Modules and dependencies](18-modules-and-dependencies.md#18-modules-and-dependencies).

## 2.2. Lines, indentation, and continuation

Use four U+0020 spaces per indentation level; tabs are forbidden in indentation. Physical line endings may be LF, CRLF, or CR. Newlines separate source items where the grammar permits. Commas separate arguments/elements, not statements or binary operands. A leading binary operator does not continue a line.

Each executable Body is either `=>` followed by one expression or statement, or a newline and an indented body (§14.2). The `=>` and start of its item must be on the header's ending physical line. Never use a body colon, `=>` alone before a newline, or a separate leading `=>` continuation line.

The **header baseline** is the indentation of its starting physical line, not its keyword's character column, label length, or final continuation line. An indented body starts exactly one level deeper. A match arm list is one level deeper than match, and an indented arm body one level deeper than the arm.

### 2.2.1. Layout normalization

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

### 2.2.2. Leading-dot continuation and Case references

Determine a new body or match-arm position from grammar before considering dot continuation. A leading dot at an arm starts a Case Pattern; at the first item of a new executable body it may start Case construction. Neither continues the header. These decisions do not use expected Types or name lookup. A header's following `.where(...)` line is therefore not a header continuation; suggest delimiters inside the header expression rather than reparsing by guesswork.

Otherwise a leading single dot at exactly one extra indentation level after an expression continues it; subsequent chain lines use that same extra level. Thus `.Some(1)` one level below a completed root-level initializer is a member-call continuation, while a Case Pattern below match begins an arm. Range tokens are not single dots. To force an independent Case expression, use its normal item indentation, optionally group it, or qualify its enum Type. Case lookup follows §6.3.2.

## 2.3. Whitespace and comments

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

`total` is initialized to `3`. Both `work()` and `finish()` belong to `example`'s body. Blank and comment-only lines do not supply an executable body; see the [nonempty Block rule](14-control-flow.md#1421-nonempty-executable-blocks).

## 2.4. Tokens and separators

A token's spelling is contiguous. Separate adjacent spellings when their concatenation would form a different token. Names, keywords, literals, punctuation, and operators follow their own token rules; recognizing a token does not make it a permitted expression.

The [notation table](01-overview.md#12-conventions-and-notation) summarizes punctuation. Expression grouping, generic/comparison boundaries, and the token rules for `@` follow [precedence and associativity](13-operators-and-assignment.md#131-precedence-and-associativity).

`;` is never a statement separator or an expression-body terminator. Every occurrence outside comments and literal content is an error. Write separate statements on separate effective lines. In particular, `if condition => 1` joins an aligned following `else` without `;`. `$` and `#` are separate punctuation tokens; `$abort` is `$` followed by the Name `abort`, and `#if` is `#` followed by the keyword `if`.

## 2.5. Names

A **Name** identifies a declaration in source. All named declarations, including Declaration Containers, Types, functions, Fields, Properties, bindings, and parameters, use the same character rules. A Name contains a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`),
- an underscore (`_`), or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`), or Letter Number (`Nl`).

Each continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`), or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`), or Connector Punctuation (`Pc`).

Names are equal if and only if their Unicode scalar sequences match exactly. Comparison is case-sensitive and culture-independent. No Unicode normalization, case folding, compatibility mapping, or removal of characters is performed for lookup or duplicate-name detection. These rules also apply to compile-time constant Names in the dedicated [Condition environment](19-compile-time-directives.md#192-environment-condition-forms).

Every Name, in both declarations and references, must already be in Unicode Normalization Form C (NFC). A non-NFC spelling is a compile-time error; the compiler does not silently normalize it. For example, a Name containing U+00E9 (`é`) is permitted, while the canonically equivalent sequence U+0065 U+0301 is rejected. In ordinary source lookup, `Dog` and `dog` are distinct Names, as are ASCII `A` and fullwidth `Ａ`.

All Format (`Cf`) characters are forbidden anywhere in Names, including bidirectional controls and zero-width join/non-join controls. This restriction applies to Names, not to comment or literal contents. It does not exclude every default-ignorable character in other Unicode categories.

Visually confusable Names are not treated as equal. Implementations may provide optional lint warnings for confusable Names; such warnings do not affect name identity or language validity, and the compiler is not required to emit them.

Character-category tests and NFC validation use [Unicode 15.0.0](https://www.unicode.org/versions/Unicode15.0.0/) data. Characters unassigned in that release are not permitted in Names. Host runtime, operating system, and globalization settings must not change these results; adopting another Unicode release requires a language-specification revision.

Contextual keywords may be Names where allowed; reserved keywords may not. `in` delimits a for header and `has` a Property Requirement list. Built-in Semantics names after `@` select shorthand targets; `move` has no special operation meaning and uses ordinary Name lookup. There is no explicit Move operator. `Self` is reserved; `self`, `value`, and accessor `storage` follow contextual binding rules (§9.2 and §11).

`public`, `internal`, `private`, `protected`, and `open` are reserved modifier keywords. The compound access specifications `protected internal` and `private protected` each consist of two keywords; their placement follows [accessibility](09-names-signatures-and-access.md#93-accessibility-and-reachability).

`init`, `deinit`, and `base` are reserved for [construction](06-declarations-and-containers.md#623-constructors) and destruction. They do not introduce ordinary callable Names or an implicit base receiver.

`require` and `do` are reserved for the [require statement](14-control-flow.md#1411-require-statement) and [do expression](14-control-flow.md#1432-do-expressions).

`specialize` is contextual immediately before `func` in an [explicit specialization declaration](08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization); it does not reserve the name in unrelated contexts.

For example, `Dog`, `_value`, `point2`, `Delta`, and `ǅelta` are valid Names, while `2point`, `has-value`, and the empty string are not.

### 2.5.1. Keyword and punctuation inventory

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
| Semantics categories | value, valueborrow, object, objectborrow, borrow, owning, reference in Semantics requirements; see [category sets](03-types-and-values.md#33-type-semantics). |
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

## 2.6. Number literals

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

Number literals have no Type suffix. [Expression type inference](12-expressions.md#1231-type-inference) defines contextual Types and defaults. To specify a literal's Type, use a declaration annotation or [explicit literal fitting](13-operators-and-assignment.md#1354-numeric-conversions-and-literals), such as `123@i32`.

## 2.7. Character escapes

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

## 2.8. Character literals

A `CharLiteral` has Type `char`. It encloses one directly written Unicode scalar value or one [Character Escape](#27-character-escapes) in single quotation marks. Delimiters are not part of the value.

```text
CharLiteral = "'" (DirectScalar | CharacterEscape) "'"
```

### 2.8.1. Content and validation

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

### 2.8.2. Normalization and displayed characters

The compiler does not normalize Char Literal content. Validation uses the content after escape processing. A `char` represents a scalar value, not a grapheme cluster or a displayed character; a combining mark alone is valid.

```kimi
'é'           // Valid: U+00E9
'\u(E9)'      // Valid: U+00E9
'e\u(301)'    // Error: U+0065 and U+0301
'\u(301)'     // Valid: one combining mark
```

The front end parses and validates Char Literals and preserves their original spelling when writing the syntax tree.

## 2.9. String literals

A `StringLiteral` produces UTF-8 text of Type `string`. Both forms support single or multiple lines:

| Form | Delimiter | Backslash escapes | Interpolation |
| ---- | --------- | ----------------- | ------------- |
| Escaped string | One double quotation mark (`"`) on each side | Yes | Yes |
| Raw string | The same number of double quotation marks, at least three, on each side | No | No |

### 2.9.1. Escaped strings

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

### 2.9.2. Raw strings

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
