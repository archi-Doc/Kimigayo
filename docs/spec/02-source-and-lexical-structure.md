# 2. Source and lexical structure

[Specification index](../SPEC.md)

This chapter defines source text, lines, comments, tokens, Names and literals.

## 2.1. Source text and lines

Source text is UTF-8. An invalid UTF-8 byte sequence is a compile-time error. A source file may begin with one U+FEFF byte order mark; it is not part of the source text.

Each source file is a SourceDocument of a Kotonoha ([§18.1](18-modules-and-dependencies.md#181-modules-and-source-environments)).

A line ends at LF, CRLF or CR. A source location is a line and a column, both counted from 1; the column counts Unicode scalar values from the start of the line.

Indentation, header baselines, single-item and indented bodies, layout events, continuation, header expressions, branch joins and leading-dot lines are defined in [§F.4](appendices/F-syntax-summary.md#f4-layout).

```kimi
func double(x: i32) -> i32 => x * 2

func run(ready: bool, mode: Mode) -> i32
    if ready => work()
    let n = match mode
        .Fast => 1
        .Slow
            prepare()
            yield 2
    return n

func broken() =>       // Error: the item must follow `=>` on the same line
    work()
```

## 2.2. Whitespace and comments

Outside literals and comments, the only whitespace is U+0020 and line endings; a tab, a no-break space or any other whitespace or control character there is a compile-time error. Spaces separate tokens, leading spaces give the indentation, and trailing spaces are ignored. Comments separate adjacent tokens and never join them.

- `//` starts a comment that ends at the line ending or the end of the source.
- `/*` starts a block comment that ends at the first `*/`. Block comments do not nest. A missing `*/` is a compile-time error.
- Inside literal text, comment delimiters are content. Inside an interpolation, the ordinary token rules apply.

A block comment within one line may appear between tokens; the spaces before it give the line's indentation. If a block comment contains a line ending, only spaces and comments may follow its `*/` on that line.

```kimi
let total = 1 /* inline comment */ + 2   // 3

func example()
    /* A multiline comment.
    */
    work()
    finish()                              // Both calls are in the body

func bad()
    /* comment
    */ work()                             // Error: code after a multiline comment's */
```

## 2.3. Documentation comments

A **documentation line** is a line comment whose `///` is preceded only by spaces on its line and is not followed by another `/`. `///Text` and `/// Text` both qualify. `////`, a `///` after code on the same line and `/** ... */` are ordinary comments. Ordinary compilation treats `///` as an ordinary comment. Documentation never affects validity or behavior.

Consecutive documentation lines at the same indentation form one **documentation block**. A blank line or an ordinary comment ends the block.

**Attachment.** The prelude of a declaration is the sequence of Attributes, blank lines and comments before its header. A declaration has at most one documentation block: the block closest to its header in its prelude, if that block's indentation equals the [header baseline](appendices/F-syntax-summary.md#f41-lines-indentation-and-bodies). Any other block is unattached; attachment never falls back to an earlier block or skips over unrelated syntax, a directive or a scope boundary. Attributes may precede, separate or follow the block; the recommended order is documentation, Attributes, declaration.

These declarations can carry documentation:

| Target | Forms |
| --- | --- |
| Containers | group, rootgroup, struct, enum, contract |
| Functions | named functions (including `main`) and requirements, explicit specializations, `init`, `drop` |
| Properties and bindings | let, var and computed Properties, Property requirements, local `let` and `var` |
| Cases | each enum Case |
| Associated Types | associate declarations and specifications |

Parameters, generic and Origin parameters, payloads and accessors are described in the documentation of their declaration. Documentation on `rootgroup A.B` belongs to that fragment of `B`. Each fragment and each overload has its own documentation; documentation is never inherited or copied from an overridden member, an implemented requirement or a specialized function. Syntax excluded by a compile-time directive ([§19.4](19-compile-time-directives.md#194-excluded-syntax)) attaches no documentation, so documentation of a conditional declaration is written inside the region the directive controls.

Documentation text is Markdown. By convention, a list item `- name: text` describes a parameter or one of the standard items `return`, `abort`, `safety`, `note`, `warning` and `example`. The supported Markdown, extraction, rendering and documentation diagnostics are implementation-defined.

```kimi
/// Adds two values.
///
/// - left: Left value.
/// - right: Right value.
/// - return: Their sum.
func add(left: i32, right: i32) -> i32 => left + right

/// Checks that add can be called.
#Test
func addWorks() => ()

    /// Unattached: its indentation differs from the header baseline.
func other() => ()

#if windows
/// Returns the platform name.
func platformName() -> string => "windows"
```

## 2.4. Tokens and punctuation

A token's spelling is contiguous. Adjacent tokens must be separated when their concatenation would form a different token. Recognizing a token does not make it valid in a given position. Outside comments and literals, a character that begins no token is an error. The punctuation tokens, longest match, generic-closer splitting, the separate tokens `$` and `#`, and the restricted tokens `;`, `!`, `{`, `}`, `&&` and `||` are defined in [§F.2.1](appendices/F-syntax-summary.md#f21-tokens-comments-and-punctuation).

```kimi
let r = 0..=9          // ..= is one token
let ne = p != q
let ok = p and q
let no = p && q        // Error: misplaced &&; write `and`
```

## 2.5. Names

A **Name** identifies a declaration. Every Name, of any declaration kind, has the form of the `Name` production ([§F.2.2](appendices/F-syntax-summary.md#f22-names)).

A Name must be in Unicode Normalization Form C; a non-NFC spelling is an error and is not normalized. Format characters (`Cf`), including bidirectional controls and zero-width joiners, are not allowed in Names. Two Names are equal exactly when their Unicode scalar sequences are equal: comparison is case-sensitive, culture-independent and performs no normalization or folding. Visually confusable Names are distinct. Character categories and NFC use Unicode 15.0.0; characters unassigned in that version are not allowed, and the host environment does not change these results. The same rules apply to Condition names ([§19.2](19-compile-time-directives.md#192-conditions)).

Reserved words and contextual keywords are listed in [§F.3](appendices/F-syntax-summary.md#f3-keywords).

The spelling `_` alone is a reserved token, not a Name. It is valid only in these positions:

| Position | Meaning |
| --- | --- |
| Runtime Pattern | Wildcard ([§14.8.1](14-control-flow.md#1481-patterns)) |
| `#case _` | Final catch-all case ([§19](19-compile-time-directives.md#19-compile-time-directives)) |
| `[_ of T]`, `[N of _]`, `[_ of _]` in an initialized local's annotation | Inferred length or element Type ([§4.3.1](04-arrays-indexing-and-slices.md#431-array-literals-and-annotation-holes)) |
| `_ = expression` | Explicit discard ([§14.2.4](14-control-flow.md#1424-explicit-discard)) |
| `for` binding or one of its Tuple elements | Unnamed iteration binding ([§14.6.1](14-control-flow.md#1461-for-and-while)) |

Any other `_`, including `let _`, `var _`, and `_` as a parameter or a reference, is a syntax error. Longer Names such as `_value` are unaffected.

```kimi
let point2 = 1
let _value = 2
let délta = 3          // Valid: U+00E9 in NFC
let Dog = 4
let dog = 5            // A different Name from Dog
let 2point = 6         // Error: a Name cannot start with a digit
let _ = 7              // Error: `_` is not a Name
```

## 2.6. Number literals

A number literal begins with an ASCII decimal digit; its grammar is in [§F.2.3](appendices/F-syntax-summary.md#f23-number-literals). A sign applied directly to a number literal forms a direct literal ([§12.3](12-expressions.md#123-literal-typing)). Number literals have no Type suffix; their Types come from context ([§12.3](12-expressions.md#123-literal-typing)) or from explicit fitting such as `123@i32` ([§13.5.4](13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions)).

A digit separator `_` is ignored. A floating-point literal denotes its exact decimal value rounded once, to nearest with ties to even, to its `f32` or `f64` Type. An infinite result is an error; zero and subnormal results are allowed. Every other literal is an integer; a magnitude above 2^128 − 1 is an error.

```kimi
let a = 1_000_000
let b = 0x_FF          // 255
let c = 0b__101__      // 5
let d = 1.5e-3
let e = 1.             // Error: the integer 1 followed by a `.` with no member
let f = 0x             // Error: no hexadecimal digit
let g = 123abc         // Error: malformed number
let h = 1e_2           // Error: the exponent needs a digit first
```

## 2.7. Character escapes

Char Literals and escaped strings share these escapes. Each produces exactly one Unicode scalar value.

| Escape | Result |
| --- | --- |
| `\0` | U+0000 |
| `\\` | U+005C backslash |
| `\e` | U+001B escape |
| `\t` | U+0009 tab |
| `\n` | U+000A line feed |
| `\r` | U+000D carriage return |
| `\"` | U+0022 quotation mark |
| `\'` | U+0027 apostrophe |
| `\u(H...)` | The scalar value with hexadecimal code H |

A `\u(...)` value above U+10FFFF or in U+D800..U+DFFF is an error; each escape is checked alone, so two surrogate escapes never form a pair. Any other or incomplete escape is an error.

```kimi
let a = '\u(41)'            // A
let b = '\u(1f600)'         // 😀
let c = '\u()'              // Error: no digits
let d = '\u(0000041)'       // Error: seven digits
let e = '\u(D83D)'          // Error: surrogate
```

## 2.8. Character literals

A Char Literal has Type `char` and holds, between single quotation marks, exactly one Unicode scalar value written directly or as an escape ([§2.7](#27-character-escapes)). A `char` is one scalar value, not a displayed character, so a lone combining mark is valid; the content is not normalized.

These must be escaped: the apostrophe and backslash, controls U+0000..U+001F and U+007F..U+009F, and U+2028 and U+2029. A Char Literal has no interpolation.

```kimi
let letter: char = 'A'
let euro: char = '€'
let quote: char = '\''
let mark: char = '\u(301)'     // A combining mark alone
let empty = ''                 // Error: no scalar value
let pair = 'ab'                // Error: two scalar values
let accent = 'e\u(301)'        // Error: two scalar values
let flag = '🇯🇵'               // Error: two scalar values
```

## 2.9. String literals

A string literal has Type `string` and produces UTF-8 text. Both forms may span lines.

| Form | Delimiter | Escapes | Interpolation |
| --- | --- | --- | --- |
| Escaped string | one `"` on each side | yes | yes |
| Raw string | the same run of three or more `"` on each side | no | no |

String content may contain any Unicode scalar value directly, including controls, tab, U+2028 and U+2029, except the closing delimiter and, in an escaped string, an unescaped `\`. The Char Literal exclusions ([§2.8](#28-character-literals)) do not apply to strings.

Each line ending inside a literal contributes one LF, whatever its source form. No indentation or initial or final newline is removed, and nothing is normalized. Write `\r` for an explicit CR. Literal text does not take part in layout ([§F.4.1](appendices/F-syntax-summary.md#f41-lines-indentation-and-bodies)).

### 2.9.1. Escaped strings and interpolation

In an escaped string, `\` starts an escape ([§2.7](#27-character-escapes)) or an interpolation. `\(expression)` inserts the text of the expression's value ([§12.4.2](12-expressions.md#1242-interpolated-strings)). The interpolation ends at its matching `)`, found with ordinary tokenization, so parentheses inside nested literals and comments do not count. Its layout follows [§F.4.1](appendices/F-syntax-summary.md#f41-lines-indentation-and-bodies). Nesting depth limits are implementation-defined.

```kimi
let greeting = "Hello, \(name)."
let total = "Total: \(price * quantity)"
let lines = "First line\nSecond line"
let block = "
First line
Second line
"                                  // Starts and ends with LF
```

### 2.9.2. Raw strings

In a raw string, backslashes, line breaks and `\(` are ordinary content.

At the start of a string token, count the run of `"`. One begins an escaped string, exactly two are the empty string `""`, and a run of N ≥ 3 begins a raw string with delimiter length N. The opening run is never split, so raw string content cannot begin with `"`; write such text as an escaped string. The first later run of M ≥ N quotes ends the raw string: its first M − N quotes are content and its last N are the delimiter. Shorter runs are content, so `""""""` begins an unterminated raw string, not an empty one. Adjacent string literals are never joined.

```kimi
let path = """C:\Users\name\file.txt"""
let token = """"The token """ appears here.""""
let tail = """text""""             // Content is text"
let a = """"x""""                  // Content is x
let b = "\"x\""                    // Content is "x"
```
