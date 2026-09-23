# 2. Source and lexical structure

[Specification index](../SPEC.md)

Source structure determines token boundaries and syntactic containment. The punctuation, layout and grammar rules in this chapter are language requirements. Error-recovery tokens and current parser acceptance are tracked separately in [STATUS.md](../STATUS.md).

## 2.1. Source text and encoding

Kimigayo source text is UTF-8. An invalid UTF-8 byte sequence in source is a compile-time error.

A **SourceDocument** is one immutable source snapshot (path and text) belonging to a source module. A **Kotonoha** is such a named module; its dependency and source-environment rules are in [Modules and dependencies](18-modules-and-dependencies.md#18-modules-and-dependencies).

## 2.2. Lines, indentation, and continuation

Each indentation level is four U+0020 spaces; tabs are forbidden in indentation. A physical line ending may be LF, CRLF or CR. Newlines separate source items where the grammar permits. Commas separate arguments and elements, not statements or binary operands. A leading binary operator does not continue a line.

Each executable Body is either `=>` followed by one expression or statement, or a newline followed by an indented body (§14.2). The `=>` and the start of its item must be on the header's ending physical line. A body colon, a `=>` followed by a newline, and a separate leading `=>` continuation line are all invalid.

The **header baseline** is the indentation of the header's starting physical line, not its keyword's column, label length, or final continuation line. An indented body starts exactly one level deeper than its header baseline. A match arm list is one level deeper than its `match`, and an indented arm body is one level deeper than its arm.

### 2.2.1. Layout normalization

An **effective line** starts or continues a source token outside comments and literal-internal text. Blank and comment-only lines neither end a body nor end a branch join. Continuation lines inside one multiline literal produce no layout events and are not validated for indentation. All other code indentation must be a multiple of four spaces; error recovery does not make invalid indentation legal.

Bodies, explicit delimiters and method-chain continuations are tracked separately. At each effective line:

1. Determine from the grammar whether a body or arm list starts. If so, require its header baseline plus four spaces and produce NEWLINE/INDENT events.
2. Otherwise, for a continuation line, validate its delimiter or chain alignment; no item separator is produced.
3. Otherwise, close completed chains and bodies, innermost first, until the line's enclosing level is reached. Reject a dedent that matches no active level and a jump that would invent an empty body. Where the grammar permits a separator (SEP), a body-closing boundary also separates completed enclosing items.

At end of file, diagnose unclosed explicit delimiters and missing bodies, and close the remaining bodies.

Within parentheses, brackets, Origin braces and recognized generic delimiters, continued content uses one additional indentation level per open delimiter beyond the active body or chain level. A matching closer may align with its opening line or stay at the content indentation. Comparison angle brackets do not establish continuation. Nested bodies generate their own layout events. A comma, parameter-name boundary `!` (§7.2.1), or closer on the same line as an indented body's content cannot close that body; dedent on the next effective line first.

A header expression continued across lines must use delimiters inside that expression; grouping the entire construct does not continue its header. A leading `->` may continue a function or accessor header only where its grammar still expects `->`, at one extra level from the header baseline. It changes neither that baseline nor the rule against a separate `=>` line. Content inside a delimiter opened on the `->` line uses one level beyond the `->` line itself. The continuation ends before the next effective line at or above the `->` line's level, which starts the body or the next item.

A **delimiter region** governs both single-item body nesting and nested-`if` grouping:

| New region | Ends at |
| --- | --- |
| A grouped expression; each argument or element in parentheses or brackets | The matching closer, or the separator before the next argument or element |
| Each item of an indented body | The end of that item |
| Each match arm | The end of that arm |

Labels, transfer operands and successive `=>` tokens do not start regions. Inside a single-item body, every body-bearing construct in the same region must also use a single-item body. A match arm list is not an executable body and each arm starts a region, so such a match may still have indented arm bodies.

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

**Branch joins.** An `else` or `else if` may follow on the ending physical line of a single-item body or on the next effective line. After an indented body, the body is closed by the dedent of the next effective line before the clause is read. A clause on its own line aligns with the baseline of the initial `if`, and no other item may intervene. The `else` of `require` likewise follows its condition on the same line or on the next effective line aligned with `require`.

Group an entire inner `if` inside a single-item `if`/`else if`/`else` body when it shares that delimiter region, whether or not the inner `if` has an `else`. Group any body-bearing expression used inside a header expression. Missing required grouping and an unmatched `else` are syntax errors. A statement cannot be grouped as an expression.

```kimi
let value = if a => (if b => 1 else => 2) else => 3
if (if a => b else => c) => work()
func run() => if ready => work() // No enclosing if: grouping is unnecessary.
```

Formatters preserve Body forms and should keep an `if` inside a single-item body, including its `else`, on one line. If the `if` needs an indented enclosing body, a formatter may offer a separate semantics-checked refactoring (§14.2); formatting alone must not change result use, transfer targets or cleanup. If a multiline non-final argument would need a leading comma after a dedent, prefer a typed intermediate local, or moving it to the last argument when evaluation and ownership semantics are preserved.

### 2.2.2. Leading-dot continuation and Case references

A new body or match-arm position is determined from the grammar before dot continuation is considered. A leading dot at an arm position starts a Case Pattern; at the first item of a new executable body it may start a Case construction. Neither continues the header. These decisions use neither expected Types nor name lookup. Consequently, a `.where(...)` line after a header is not a header continuation; a diagnostic should suggest delimiters inside the header expression instead of reparsing by guesswork.

Otherwise, a leading single dot at exactly one extra indentation level after an expression continues that expression, and subsequent chain lines use the same extra level. Thus `.Some(1)` one level below a completed root-level initializer is a member-call continuation, while a Case Pattern below `match` begins an arm. Range tokens are not single dots. To write an independent Case expression, use normal item indentation, group it, or qualify its enum Type. Case lookup follows §6.3.2.

## 2.3. Whitespace and comments

Outside literal content, U+0020 spaces separate tokens; leading spaces determine indentation and trailing spaces are ignored. This neither changes literal contents nor makes tabs valid layout whitespace. Comments contribute no executable syntax and separate adjacent tokens rather than joining them.

- `//` starts a comment that extends to the physical line ending or the end of the source.
- `/*` starts a block comment that ends at the first `*/`; block comments do not nest. A missing terminator is a compile-time error.
- Within literal text, comment delimiters are content. Interpolated expressions follow the ordinary token rules.

A block comment without a physical newline may appear between tokens on one line. The spaces before the comment determine that line's indentation; spaces after `*/` add none.

If a block comment contains a physical newline, only spaces and comments may follow its closing `*/` on the same physical line; code there is a compile-time error. The comment does not join code across lines: subsequent code starts on a following line and obeys the ordinary indentation and continuation rules. Comment-only lines and indentation inside comments do not affect block structure.

```kimi
let total = 1 /* inline comment */ + 2

func example()
    /* A multiline comment.
    */
    work()
    finish()
```

`total` is `3`. Both `work()` and `finish()` belong to the body of `example`. Blank and comment-only lines do not supply an executable body; see the [nonempty Block rule](14-control-flow.md#1421-nonempty-executable-blocks).

### 2.3.1. Documentation text

A **Documentation Comment** is Markdown associated with a declaration. It describes purpose, usage and obligations, without changing name resolution, Type checking or execution. Source-reading tools and Mods may observe its bytes (§18.7.4). Ordinary compilation may discard documentation metadata; enabled documentation tooling follows the rules below.

A lexically recognized line comment is documentation when only U+0020 spaces precede its `///` on the physical line and the following character is not `/`. `///Text` and `/// Text` are both accepted. `////`, trailing `///` and `/** ... */` remain ordinary comments. Literal/block-comment contents cannot introduce documentation. Normal encoding/token/layout validation still applies; documentation creates no executable item or layout event.

Consecutive documentation lines at the same indentation form one **documentation block**. Remove the indentation and `///` from each line, then at most one U+0020. Preserve remaining characters, including trailing spaces; join lines with LF, normalizing LF/CRLF/CR, without adding a final newline. A bare `///` contributes an empty line. Ordinary blank lines/comments end the block without entering its text. An attached empty block differs from absent documentation.

```kimi
/// Returns a shared reference to the first element.
///
/// - abort: `values` is empty.
func first<T>(values: ref/Array<T>) -> ref/T during values
    return values[0]@ref
```

### 2.3.2. Declaration association

A **declaration prelude** is the sequence of the declaration's Attributes, blank lines and comments before its header. Treat each Attribute as one syntax unit: comments inside its arguments or the declaration header are not prelude candidates. Unrelated syntax, directives, actual scope boundaries and document boundaries end the prelude. Comment indentation alone does not end it. This terminology does not change §6.5 Attribute placement/targets, including same-line Attributes.

Each syntactic declaration has at most one documentation block: the closest candidate in its prelude, provided its indentation equals the header baseline. Other candidates are unattached. Never fall back to an earlier candidate when the closest is empty or misindented. Attributes may precede, separate or follow documentation; prefer documentation, Attributes, declaration. Never skip an ineligible item to find a target. During syntax recovery, defer uncertain associations and their derived unattached diagnostics.

```kimi
/// Earlier block: unattached.
// func old() => ()
/// Checks that the sample can be invoked.
#Test
func sample() => ()
```

Eligible targets are declaration items directly in declaration/executable lists, including directive-controlled lists; ordinary grammar still determines permitted placement:

| Target | Included forms |
| --- | --- |
| Containers | group, rootgroup, struct, enum, contract |
| Functions | Named functions and requirements, explicit specializations, init, deinit |
| Properties/bindings | let/var/computed Properties, Contract property requirements, local let/var |
| Cases | Each enum Case, including its payload description |
| Associated Types | Contract associate declarations and implementation-side specifications |

Public main is a named-function target. `rootgroup A.B` documents the written B fragment, not synthesized intermediate groups. Field denotes a Property's storage slot, not another target. Explain parameters, generic/Origin parameters, payloads and accessors in their containing declaration. Pattern bindings, expressions, aliases, Constraints, directives and Attributes are not independent targets. Use a group or separate Markdown document for broader prose; no file/module documentation delimiter is added.

### 2.3.3. Selection and related declarations

Associate only where §19.5 requires ordinary declaration grammar, using original source positions. False #if interiors receive no association/documentation diagnostics. Reached #switch arms are parsed except nested False #if interiors; unselected syntax requires no additional semantic checks or link resolution. Documentation must not increase parsing obligations or migrate to a surviving declaration. Place it after the directive inside its controlled region, including the same-indentation form:

```kimi
#if windows
/// Returns the platform name.
func platformName() -> string => "windows"
```

Publish only selected declarations. Multi-configuration tools parse/select for each configuration and distinguish the results. For merged group/struct fragments, retain independent blocks with provenance in §20.7.4 logical declaration order; never concatenate Markdown scopes or overwrite fragments. Generated sources follow the same rules.

Documentation belongs to its syntactic declaration. Related declarations—an inherited member, implemented requirement or specialization source—may be shown by reference, not implicit text copying/inheritance/merging. Do not expand text from a declaration outside the publication scope. Overloads are independent. Call documentation uses the declaration defining the call contract. Specialization documentation is an implementation note: it may describe §8.8's permitted result/side-effect differences but cannot replace inherited call/Safety obligations.

### 2.3.4. Markdown and links

Use the normative [Documentation Markdown profile](documentation-markdown.md). Its [syntax rules](documentation-markdown.md#2-syntax) define the adopted CommonMark subset and explicit boundary behavior; its [HTML and link rules](documentation-markdown.md#4-html-and-links) define escaping, reference bases, logical source targets, output mapping and URL validation. Full CommonMark compatibility is not required.

### 2.3.5. Writing and extracting items

Recommend a short opening paragraph explaining purpose. Explain meaning, boundaries, effects, ordering, complexity and safety without mechanically repeating declaration facts. Documentation does not prove obligations, override declarations or introduce deprecation metadata.

Use list items for short descriptions and headings for longer standard sections. The profile's [summary and item rules](documentation-markdown.md#3-summary-and-documentation-items) own standard names, extraction, description ranges and declaration-dependent classification.

````kimi
/// Adds two values.
///
/// - left: Left value.
/// - right: Right value.
/// - return: Their sum.
///
/// # example
/// ```kimi
/// let total = add(2, 3)
/// ```
public func add(left: i32, right: i32) -> i32 => left + right
````

### 2.3.6. Tooling and diagnostics

Select documentation processing by use:

| Use | Processing |
| --- | --- |
| Ordinary compilation (default) | Skip `///` like ordinary line comments. Do not collect documentation ranges, extract text, associate declarations or parse Markdown. |
| LSP, documentation generation or documentation checking | Enable range collection in the Tokenizer, separate from ordinary tokens. The Parser associates ranges with declaration fragments; extract text and parse Markdown only on demand. |
| Source-reading Mods | Read the original SourceDocument independently of documentation collection. Documentation edits count as source dependency changes under §18.7.4 and §20.7.5. |

Documentation metadata may be omitted from the Parser's executable syntax tree; Type checking, Analysis, Lowering and Emit do not require it. This does not remove comment text from the original SourceDocument available to source-reading consumers.

When documentation processing is enabled, retain the target fragment and original SourceDocument/range. Publication scope is configurable; public output follows effective accessibility. Optional documentation diagnostics cover unattached/ignored documentation, missing/ambiguous items, missing unsafe `safety` prose and unresolved links. Diagnose ignored `///` only where lexing identified an actual line comment, respecting §2.3.3 exclusions. Documentation diagnostics never affect ordinary language validity, although a separate CI gate may fail. Do not downgrade existing encoding/layout/syntax errors to lint.

Optional writing diagnostics may suggest adopted syntax when text resembles omitted features, such as Setext headings or indented code. Such resemblance is not a definite syntax error. Exhaustive detection and a comparison parser are not required. Tab and trailing-space advice is separate: hard-break spaces remain meaningful. Do not collect diagnostic-only data when these checks are disabled; measure their added cost when enabled.

Canonical formatting uses `/// ` for nonempty lines and `///` for empty ones, preserving extracted text. Moving blocks before Attributes must preserve every block's association and text. No new formatter or command syntax is required.

Disabled collection adds no documentation-specific allocations or retained text/Markdown trees; no candidates means no dedicated candidate buffer. Enabled processing uses source-ordered ranges and original-source mappings without whole-file rescans per declaration or runtime metadata. Never retain disposed tokenizer buffers. Replacement/removal invalidates generated-document associations. Structured documentation queries alone cannot narrow raw-source Mod dependencies. The profile's [processing guarantees](documentation-markdown.md#5-syntax-api-and-processing-guarantees) own immutable publication, positions, reuse, cancellation and resource bounds; [Appendix A.21](appendices/A-compiler-requirements.md#a21-documentation-comments) owns verification.

## 2.4. Tokens, separators, and punctuation

A token's spelling is contiguous. Adjacent spellings must be separated when their concatenation would form a different token. Names, keywords, literals, punctuation and operators follow their own token rules; recognizing a token does not make it a permitted expression. `$` and `#` are separate punctuation tokens: `$abort` is `$` followed by the Name `abort`, and `#if` is `#` followed by the keyword `if`.

`;` is never a statement separator or expression-body terminator; every occurrence outside comments and literal content is an error. Write separate statements on separate effective lines. In particular, `if condition => 1` joins an aligned following `else` without `;`.

| Punctuation/operator class | Spellings |
| --- | --- |
| Structural | `(` `)` `[` `]` `{` `}` `,` `.` `:` `::` `->` `=>` `@` `#` `$` `!` |
| Arithmetic and updates | `+` `-` `*` `/` `%` `++` `--` `+=` `-=` `*=` `/=` `%=` |
| Comparison and assignment | `=` `==` `!=` `<` `<=` `>` `>=` |
| Bitwise and shifts | `&` `\|` `^` `<<` `>>` `&=` `\|=` `^=` `<<=` `>>=` |
| Ranges | `..` `..=` |
| Optional Type suffix | `?` |
| Recognized but unavailable | `;` `&&` `\|\|` |

Outside comments and literals, the longest punctuation spelling is matched: `..=` before `..` before `.`, `->` before `-`, `<<=` before `<<` before `<`, `>>=` before `>>` before `>`, `::` before `:`, and `=>` or `==` before `=`. Thus `a+++b` is `a`, `++`, `+`, `b`; there is no `+++` token. Unlisted punctuation is invalid unless it forms a grammatically valid sequence of listed tokens.

Only when closing syntactically recognized generic Type arguments or parameters may `>>` split into two `>` tokens, and `>>=` into `>`, `>`, `=` (or `>`, `>=` when only one level closes). Expression shifts are unaffected. Tuple indices use the exception in §2.6.

`!` is a parameter-list boundary only where §7.2.1 permits it, never a unary or postfix expression operator. `!=` retains its longest-token spelling.

The [notation table](01-overview.md#12-conventions-and-notation) summarizes the meaning of punctuation. Expression grouping, generic/comparison boundaries and the token rules for `@` follow [precedence and associativity](13-operators-and-assignment.md#131-precedence-and-associativity).

Origin braces use `{` and `}` as one delimiter pair, with ordinary continuation rules and no executable scope. Declaration context selects a Type schema header; a named Type suffix names a binding set. Only a Type declaration header permits empty `{}`. `during` introduces a postfix borrow annotation after an AnnotatedType's body and optional suffixes (§3.3.6), independently of lookup or target eligibility; elsewhere it is an ordinary Name. It adds no line-continuation rule. `origin` introduces a declaration-attached relation and `outlives` is contextual within that relation. `from` has no Origin role. See §15.3.

## 2.5. Names

A **Name** identifies a declaration in source. All named declarations, including Declaration Containers, Types, functions, Fields, Properties, bindings and parameters, use the same character rules. A Name is a start character followed by zero or more continuation characters.

The start character may be:

- an ASCII letter (`A`–`Z` or `a`–`z`);
- an underscore (`_`); or
- a Unicode character in one of the categories Uppercase Letter (`Lu`), Lowercase Letter (`Ll`), Titlecase Letter (`Lt`), Modifier Letter (`Lm`), Other Letter (`Lo`) or Letter Number (`Nl`).

A continuation character may be any valid start character, or:

- an ASCII digit (`0`–`9`); or
- a Unicode character in one of the categories Nonspacing Mark (`Mn`), Spacing Combining Mark (`Mc`), Decimal Digit Number (`Nd`) or Connector Punctuation (`Pc`).

For example, `Dog`, `_value`, `point2`, `Delta` and `ǅelta` are valid Names, while `2point`, `has-value` and the empty string are not.

Every Name, in declarations and references alike, must already be in Unicode Normalization Form C (NFC). A non-NFC spelling is a compile-time error; the compiler does not normalize it. For example, a Name containing U+00E9 (`é`) is permitted, while the canonically equivalent sequence U+0065 U+0301 is rejected.

All Format (`Cf`) characters are forbidden in Names, including bidirectional controls and zero-width joiners and non-joiners. This restriction applies to Names, not to comment or literal contents, and does not exclude default-ignorable characters of other categories.

Two Names are equal if and only if their Unicode scalar sequences match exactly. Comparison is case-sensitive and culture-independent; lookup and duplicate detection perform no normalization, case folding, compatibility mapping or character removal. Thus `Dog` and `dog` are distinct Names, as are ASCII `A` and fullwidth `Ａ`. The same rules apply to the compile-time constant Names of the [Condition environment](19-compile-time-directives.md#192-environment-condition-forms).

Visually confusable Names are not equal. An implementation may provide optional lint warnings for confusable Names; such warnings are not required and affect neither name identity nor validity.

Category tests and NFC validation use [Unicode 15.0.0](https://www.unicode.org/versions/Unicode15.0.0/) data. Characters unassigned in that release are not permitted in Names. Host runtime, operating system and globalization settings must not change these results; adopting another Unicode release requires a revision of this specification.

The complete spelling `try` is reserved. The complete spelling `_` is a reserved token, not a Name. Longer Names such as `tryGet` and `_value`, numeric separators such as `1_000`, and literal/comment contents are unaffected. `_` denotes a syntax-specific omission, with no common acquisition operation:

| Position | Meaning |
| --- | --- |
| Runtime Pattern | Wildcard (§14.8.1) |
| `#case _` | Final catch-all with existing placement/count restrictions (§19) |
| `[N of _]` | Existing element inference in an initialized local annotation (§4) |
| `_ = expression` | Explicit discard statement (§14.2.4) |
| for binding or Tuple binding element | Unnamed iteration binding (§14.6.1) |

Every other standalone `_` is a syntax error, including declarations, references, captures, `let _`, `var _`, and named/anonymous function parameters.

### 2.5.1. Keywords

A reserved keyword cannot be a Name. A contextual keyword is recognized only in its listed context and is otherwise an ordinary Name. Keywords match only as complete words: `ifValue` is one Name. These tables define token classes independently of any internal grouping.

| Reserved class | Spellings |
| --- | --- |
| Primitive Types | `isize`, `usize`, `i8`, `i16`, `i32`, `i64`, `i128`, `u8`, `u16`, `u32`, `u64`, `u128`, `f32`, `f64`, `bool`, `char`, `string` |
| Bindings and functions | `let`, `var`, `func` |
| Control, tests and literals | `if`, `else`, `case`, `for`, `while`, `loop`, `do`, `match`, `return`, `exit`, `continue`, `yield`, `try`, `require`, `defer`, `is`, `not`, `and`, `or`, `true`, `false`, `null` |
| Access and inheritance | `public`, `internal`, `private`, `protected`, `open` |
| Dedicated forms | `Self`, `init`, `deinit`, `base` |
| Compile-time selection | `switch`; used after `#`. There is no runtime switch construct. |
| Reserved future syntax | `as` |

| Contextual class | Spellings and recognizing context |
| --- | --- |
| Declarations | `alias`, `rootgroup`, `group`, `struct`, `enum`, `contract`, `computed`, `property` in declaration and header positions. `extension` is reserved in the same positions for a future declaration and is rejected in this revision. |
| Unavailable declaration modifiers | `virtual`, `override`, `abstract`; recognized only in a declaration's leading modifier sequence, and rejected there with the unavailable-feature diagnostic. |
| Parameters and accessors | `in` in a `for` header; `to` immediately after `exit`, `continue` or `yield`; `associate` in an associated-Type declaration or specification; `has`, `get`, `set` in accessor syntax; `specialize` immediately before `func`; `when` in a conditional conformance. |
| Origins | `during` after an AnnotatedType's body and optional suffixes; `origin` at the start of a declaration-attached relation; `outlives` within that relation; `static` as the distinguished Origin in Origin expressions. |
| Semantics and safety | `owner`, `ref`, `uniq`, `obj`, `rc`, `arc`, `objref`, `objuniq`, `unsafe` in Semantics positions, including requirements. `unsafe` is also recognized before `func` and before a Body that introduces an Unsafe Statement. |
| Transfer | `move` immediately after `@`, as the [transfer operation](13-operators-and-assignment.md#1353-defined-adaptations) `E@move`. |
| Semantics categories | `value`, `valueborrow`, `object`, `objectborrow`, `borrow`, `owning`, `reference` in Semantics requirements; see [category sets](03-types-and-values.md#33-type-semantics). |
| Contextual bindings and operations | `self`, `value`, `storage` under the receiver and accessor rules (§9.2, Chapter 11); `abort` after `$`. |
| Fixed arrays and lengths | `of` only between the length and element Type in `[N of T]`; `length` only at the start of a generic parameter declaration, followed by its Name. |

Further notes on individual keywords:

- `Self` is reserved. After `@`, the built-in Semantics names select shorthand targets and `move` selects the transfer operation; elsewhere `move` is an ordinary Name. There is no prefix `move` operator.
- The compound access specifications `protected internal` and `private protected` each consist of two keywords; their placement follows [accessibility](09-names-signatures-and-access.md#93-accessibility-and-reachability).
- `init`, `deinit` and `base` are reserved for [construction](06-declarations-and-containers.md#623-constructors) and destruction. They introduce no ordinary callable Names and no implicit base receiver.
- `require` and `do` are reserved for the [require statement](14-control-flow.md#1411-require-statement) and the [do expression](14-control-flow.md#1432-do-expressions).
- `specialize` introduces an [explicit specialization declaration](08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization) and reserves nothing in other contexts.

**Unavailable modifiers.** Recognize `virtual`, `override` and `abstract` before the other modifiers and the declaration introducer of the same logical header. This applies to Type, function and Property declarations, Contract requirements, constructors, `deinit` and accessors, even where access and `open` modifiers are otherwise forbidden. Recognition does not scan across a newline, indent or dedent that separates independent items. Thus `abstract open struct`, `virtual func`, `virtual init`, `override deinit` and `abstract get` all receive the unavailable-feature diagnostic. The words remain ordinary Names in `struct abstract`, `func virtual(...)`, `let override: i32`, `x.abstract()` and `virtual(...)`, and a standalone `abstract` expression must not consume the declaration on the next line. This recognition adds no valid declaration form and no globally reserved word.

## 2.6. Number literals

A `NumberLiteral` begins with an ASCII decimal digit. A leading `+` or `-` is an operator, not part of the literal; sign characters may occur only inside a decimal exponent.

| Form | Prefix or syntax | Digits |
| ---- | ---------------- | ------ |
| Decimal integer | None | `0`–`9` |
| Binary integer | `0b` or `0B` | `0`, `1` |
| Octal integer | `0o` or `0O` | `0`–`7` |
| Hexadecimal integer | `0x` or `0X` | `0`–`9`, `a`–`f`, `A`–`F` |
| Decimal floating point | Decimal fraction, exponent, or both | `0`–`9` |

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

`_` is an ignored digit separator. Consecutive separators, separators after a base prefix, and trailing separators are allowed: `1__000`, `123_`, `0x_FF` and `0b__101__` are valid. Every base prefix requires at least one digit valid for that base, so `0x`, `0b` and `0o___` are compile-time errors. An exponent must have a decimal digit immediately after `e`/`E` and its optional sign; `1e_2` and `1e+_2` are invalid.

A Name continuation character immediately after a number cannot start a separate token. The remaining continuation characters are consumed into a malformed numeric token, which is diagnosed: `0xg`, `0b102` and `123abc` are errors.

A decimal point belongs to the literal only when immediately followed by a decimal digit: `1.0` is floating point, while `1.` is the integer `1` followed by a dot. Only decimal literals have fractions and exponents; `0xFF.0` starts with the integer `0xFF`.

**Tuple indices.** Immediately after a member-access dot token (ignoring intervening spaces and comments), a Tuple index is scanned as the maximal run of ASCII decimal digits, without a fraction, exponent, base prefix or digit separator. Thus `pair.0.1` is `pair`, `.`, `0`, `.`, `1`, and `pair.0.name` accesses a named member of element zero. A parser may equivalently resplit a numeric token using its original spelling. An ordinary `0.1` remains a floating-point literal. The Tuple index must be in range.

**Values.** After separators are removed, a decimal literal with a fraction or exponent denotes an exact decimal value. It is rounded once to its determined IEEE 754 `f32` or `f64` Type, round-to-nearest ties-to-even, without an intermediate floating Type. A result of either infinity is a compile-time error; subnormal and zero results are permitted. All other literals are integers. Magnitudes `0` through `2^128 - 1` are stored as 128-bit patterns; larger magnitudes are invalid.

Number literals have no Type suffix. [Expression type inference](12-expressions.md#1231-type-inference) defines their contextual Types and defaults. To give a literal a specific Type, use a declaration annotation or [explicit literal fitting](13-operators-and-assignment.md#1354-numeric-conversions-and-literals), such as `123@i32`.

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

`\u(H...)` requires one to six ASCII hexadecimal digits. Leading zeros are allowed; whitespace, signs, separators and `0x` are forbidden. Values above U+10FFFF and values in U+D800..U+DFFF are invalid. Each escape is validated independently, so surrogate escapes never combine into pairs. Unsupported or incomplete escapes are compile-time errors.

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

String interpolation is defined under [escaped strings](#291-escaped-strings).

## 2.8. Character literals

A `CharLiteral` has Type `char`. It encloses one directly written Unicode scalar value or one [Character Escape](#27-character-escapes) in single quotation marks; the delimiters are not part of the value.

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

A Char Literal cannot contain a physical line break or tab and does not support interpolation. Violations are compile-time errors.

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

The compiler does not normalize Char Literal content; validation uses the content after escape processing. A `char` is a scalar value, not a grapheme cluster or displayed character, so a lone combining mark is valid.

```kimi
'é'           // Valid: U+00E9
'\u(E9)'      // Valid: U+00E9
'e\u(301)'    // Error: U+0065 and U+0301
'\u(301)'     // Valid: one combining mark
```

The front end preserves the original spelling of a Char Literal when writing the syntax tree.

## 2.9. String literals

A `StringLiteral` produces UTF-8 text of Type `string`. Both forms may span one or more lines:

| Form | Delimiter | Backslash escapes | Interpolation |
| ---- | --------- | ----------------- | ------------- |
| Escaped string | One double quotation mark (`"`) on each side | Yes | Yes |
| Raw string | The same number (at least three) of double quotation marks on each side | No | No |

The delimiters are not part of the value. In both forms, each physical LF, CRLF or CR in the text contributes one LF. No spaces, indentation, initial newline or final newline are stripped. Escape results and interpolated values are not normalized; use `\r` in an escaped string for an explicit CR. Literal-internal lines do not affect the enclosing layout.

### 2.9.1. Escaped strings

An escaped string is enclosed by one double quotation mark on each side. A backslash introduces one of the shared [Character Escapes](#27-character-escapes) or an interpolation.

```kimi
"Hello, world"
"First line\nSecond line"
"
First line
Second line
"
```

**Interpolation.** `\(expression)` inserts the string representation of a Kimigayo expression; its value follows [interpolation stringification](12-expressions.md#1233-interpolation-stringification).

```kimi
"Hello, \(name)."
"Total: \(price * quantity)"
```

The interpolation ends at its matching `)`. The matching closer is found with ordinary expression tokenization, including Char Literals, escaped and raw strings, nested interpolations and comments; parentheses inside those tokens do not change the depth. Missing delimiters and malformed nested tokens are errors. An interpolated expression has its own delimiter and layout context, so its newlines cannot close an outer source body. The language imposes no fixed nesting limit; an implementation may document a finite resource limit and must diagnose exceeding it rather than truncate or misparse the source.

### 2.9.2. Raw strings

A raw string is enclosed by matching delimiters of three or more consecutive double quotation marks. Backslashes, line breaks and interpolation-like text are ordinary content; no escape processing or interpolation occurs.

```kimi
"""C:\Users\name\file.txt"""
"""
First line
Second line
"""
```

A delimiter of `N` quotation marks permits shorter quote runs in the content. Lengthen the delimiter when needed; four quotation marks allow a literal `"""`:

```kimi
""""The token """ appears here.""""
```

**Delimiter selection.** At the start of a string token, count the maximal run of consecutive double quotation marks. One quote begins an escaped string; exactly two form the empty escaped string `""`; a run of N ≥ 3 begins a raw string with delimiter length N. The opening run is never split into an opening and a closing delimiter; for example, six quotes followed by end of file are an unterminated raw string, not an empty raw string.

After the opening run, the first maximal quote run of length M ≥ N terminates the raw string: its first M − N quotes are content and its last N quotes are the closing delimiter. Shorter runs are content. The entire terminating run belongs to this token, and adjacent string tokens are never concatenated implicitly. Thus `"""text""""` has content `text"`. Use `""` for empty text; a raw string that contains a physical newline is not empty.
