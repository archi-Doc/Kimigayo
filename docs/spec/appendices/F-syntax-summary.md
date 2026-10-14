# Appendix F. Grammar

[Specification index](../../SPEC.md)

Appendix F is the normative grammar. Each `? description ?` placeholder is defined by the section it cites, and every placeholder cites one. This appendix owns token boundaries, keywords, punctuation, layout, productions and precedence; the chapters own meaning and the context-dependent constraints that this appendix references.

## F.1. Notation

- Quoted text is literal syntax. `|` is choice, parentheses group, and `?`, `*` and `+` mean optional, zero or more, and one or more.
- `List<X>` is `X ("," X)*`, and `TrailingList<X>` is `List<X> ","?`. These angle brackets are grammar notation; quoted `"<"` and `">"` are tokens.
- `NEWLINE`, `INDENT` and `DEDENT` are layout events; `ItemList<X>` and `IndentedList<X>` are defined with them in [§F.4.2](#f42-layout-events).
- A production that writes a Name in quotes (such as `"get"`) matches a Name of that spelling; the contexts in which such a word is a keyword are listed in [§F.3.2](#f32-contextual-keywords).
- A trailing `//` comment on a production is a constraint on that production.

## F.2. Lexical grammar

Source text is a sequence of tokens: Names, keywords, literals and punctuators. Encoding, whitespace, comment placement and the meaning of literals are defined in [§2.1](../02-source-and-lexical-structure.md#21-source-text-and-lines), [§2.2](../02-source-and-lexical-structure.md#22-whitespace-and-comments) and [§2.6](../02-source-and-lexical-structure.md#26-number-literals)–[§2.9](../02-source-and-lexical-structure.md#29-string-literals).

### F.2.1. Tokens, comments and punctuation

```ebnf
PhysicalNewline := LF | CR LF | CR
LineComment     := "//" ? characters up to a PhysicalNewline or end of file, [§2.2](../02-source-and-lexical-structure.md#22-whitespace-and-comments) ?
BlockComment    := "/*" ? characters up to the first "*/", [§2.2](../02-source-and-lexical-structure.md#22-whitespace-and-comments) ? "*/"
Token           := Name | Keyword | NumberLiteral | CharLiteral | StringLiteral | Punctuator | "_"
Keyword         := ? a reserved word ([§F.3.1](#f31-reserved-words)) or a contextual keyword in its context ([§F.3.2](#f32-contextual-keywords)) ?
Punctuator      := "(" | ")" | "[" | "]" | "{" | "}" | "," | ";" | "." | ":" | "::"
                 | "->" | "=>" | "@" | "#" | "$" | "?" | "!"
                 | "+" | "-" | "*" | "/" | "%" | "++" | "--"
                 | "=" | "+=" | "-=" | "*=" | "/=" | "%="
                 | "==" | "!=" | "<" | "<=" | ">" | ">="
                 | "&" | "|" | "^" | "<<" | ">>" | "&=" | "|=" | "^=" | "<<=" | ">>="
                 | ".." | "..=" | "&&" | "||"
```

- **Longest match.** Outside comments and literals, the longest punctuator spelling is taken: `a+++b` is `a`, `++`, `+`, `b`. Punctuation that forms no sequence of listed tokens is an error.
- **Generic closers.** When the parser closes Type arguments or generic parameters at a token that begins with `>` (`>`, `>=`, `>>`, `>>=`), it takes the first `>` as the closer and lexes the remaining source again from that point. Expression shifts and comparisons are unaffected.
- `$` and `#` are separate tokens: `$abort` is `$` followed by the operation name `abort`, and `#if` is `#` followed by the keyword `if`.
- `;` occurs only in the task slot and task argument `task;` ([§F.6.3](#f63-functions-parameters-and-bindings)); `!` only as the parameter-list boundary; `{` and `}` only as Origin braces ([§F.8](#f8-origins)). Any other occurrence is an error.
- `&&` and `||` are tokens only so that every use is reported as an error; the logical operators are `and` and `or`.

```kimi
let a: Box<Box<i32>>= make()     // `>`, `>`, `=`
let b: Box<i32>= make()          // `>`, `=`
func f() -> Box<i32>=> make()    // `>`, `=>`
let c = a1 >> 2                  // A shift: no Type arguments are open
```

### F.2.2. Names

```ebnf
Name         := NameStart NameContinue*   // not a reserved word and not "_"
NameStart    := "A".."Z" | "a".."z" | "_"
              | ? a character of Unicode General Category Lu, Ll, Lt, Lm, Lo or Nl, [§2.5](../02-source-and-lexical-structure.md#25-names) ?
NameContinue := NameStart | "0".."9"
              | ? a character of Unicode General Category Mn, Mc, Nd or Pc, [§2.5](../02-source-and-lexical-structure.md#25-names) ?
```

A keyword matches only a complete Name spelling: `ifValue` and `tryGet` are Names. The standalone `_` is a token, not a Name; its positions are listed in [§2.5](../02-source-and-lexical-structure.md#25-names). NFC, excluded characters and Name equality are defined in [§2.5](../02-source-and-lexical-structure.md#25-names).

### F.2.3. Number literals

```ebnf
NumberLiteral   := IntegerLiteral | FloatLiteral
IntegerLiteral  := DecimalSequence
                 | "0" ("b" | "B") "_"* BinaryDigit (BinaryDigit | "_")*
                 | "0" ("o" | "O") "_"* OctalDigit (OctalDigit | "_")*
                 | "0" ("x" | "X") "_"* HexDigit (HexDigit | "_")*
FloatLiteral    := DecimalSequence (Fraction Exponent? | Exponent)
DecimalSequence := DecimalDigit (DecimalDigit | "_")*
Fraction        := "." DecimalDigit (DecimalDigit | "_")*
Exponent        := ("e" | "E") ("+" | "-")? DecimalDigit (DecimalDigit | "_")*
DecimalDigit    := "0".."9"
BinaryDigit     := "0" | "1"
OctalDigit      := "0".."7"
HexDigit        := DecimalDigit | "a".."f" | "A".."F"
TupleIndex      := DecimalDigit+   // only after a member-access "."; spaces and comments may intervene
```

- A literal has no sign; a leading `+` or `-` is an operator. A `.` belongs to a literal only when a decimal digit follows it: `1.` is `1` followed by `.`.
- A Name continuation character directly after a number literal is consumed into one malformed token, which is an error: `0xg`, `0b102`, `123abc`.
- Directly after a member-access `.` (ignoring spaces and comments), digits are scanned as a TupleIndex without fraction, exponent, prefix or separator: `pair.0.1` is `pair`, `.`, `0`, `.`, `1`. The malformed-token rule still applies, so `pair.1_2` is an error.

### F.2.4. Character and string literals

```ebnf
CharacterEscape := "\0" | "\\" | "\e" | "\t" | "\n" | "\r" | '\"' | "\'"
                 | "\u(" HexDigit HexDigit? HexDigit? HexDigit? HexDigit? HexDigit? ")"
CharLiteral     := "'" (DirectScalar | CharacterEscape) "'"
DirectScalar    := ? one Unicode scalar value not excluded by [§2.8](../02-source-and-lexical-structure.md#28-character-literals) ?
StringLiteral   := EscapedString | RawString
EscapedString   := '"' (StringText | CharacterEscape | Interpolation)* '"'
StringText      := ? any Unicode scalar value except '"' and "\", [§2.9](../02-source-and-lexical-structure.md#29-string-literals) ?
Interpolation   := "\(" Expression ")"
RawString       := QuoteRun RawText QuoteRun
QuoteRun        := ? a run of N >= 3 double quotation marks, selected by [§2.9.2](../02-source-and-lexical-structure.md#292-raw-strings) ?
RawText         := ? content up to the terminating quote run, [§2.9.2](../02-source-and-lexical-structure.md#292-raw-strings) ?
Literal         := NumberLiteral | CharLiteral | StringLiteral
                 | "true" | "false" | "null" | "(" ")"
```

```kimi
let count = 1_000 + 0xFF
let ratio = 2.5e-3
let path = """C:\temp\file.txt"""
let text = "count = \(count)\n"
let first = ((1, 2), 3).0.1       // Tuple indices
```

## F.3. Keywords

A reserved word is never a Name. A contextual keyword is a keyword only in the context listed below and an ordinary Name elsewhere. Recognition uses tokens only, never name lookup or Types.

### F.3.1. Reserved words

| Reserved class | Spellings |
| --- | --- |
| Primitive Types | `isize` `usize` `i8` `i16` `i32` `i64` `i128` `u8` `u16` `u32` `u64` `u128` `f32` `f64` `bool` `char` `string` |
| Bindings and functions | `let` `var` `func` |
| Control, tests and literals | `if` `else` `case` `for` `while` `loop` `do` `match` `return` `exit` `continue` `yield` `try` `require` `defer` `is` `not` `and` `or` `true` `false` `null` |
| Access | `public` `internal` `private` `protected` |
| Dedicated forms | `Self` `init` `drop` `base` |
| Compile-time selection | `switch` (only after `#`; there is no runtime switch) |
| Reserved for future use | `as` |

### F.3.2. Contextual keywords

This table is exhaustive.

| Words | Keyword context |
| --- | --- |
| `alias` `rootgroup` `group` `struct` `enum` `contract` `computed` `property` | As a declaration introducer. `extension` in the same position is recognized and rejected as unavailable. |
| `unsafe` `virtual` `override` `abstract` `open` | As declaration modifiers ([§F.3.3](#f33-declaration-modifiers)). `unsafe` also at the start of an executable item when followed by `=>` or the end of the line (UnsafeStatement). |
| `label` | At the start of a Primary when followed on the same physical line by a Name and `:` ([§14.4](../14-control-flow.md#144-labels)). |
| `in` | After the ForBinding of a `for` header. |
| `to` | Immediately after `exit`, `continue` or `yield`. |
| `task` | As the first token after the `(` of a parameter list, argument list or FunctionParameters, when the next token is `;` ([§24.2.1](../24-suspension-and-asynchronous-tasks.md#2421-form)). |
| `self` | As ReceiverShorthand and in AccessorReceiver. `self`, `value` and `storage` are also contextual bindings ([§9.3](../09-names-signatures-and-access.md#93-local-scopes-and-contextual-names), [§11.3](../11-properties.md#113-accessor-functions)). |
| `get` `set` `has` `value` | In accessor and Property-requirement syntax ([§F.7](#f7-properties)). |
| `associate` `when` `specialize` | In associated-Type declarations and specifications, in a ConditionalConformance, and immediately before `func`. |
| `place` | In a result position when followed by `ref` or `uniq` and `/`. |
| `Callable` | At the start of a RequirementAtom or ConformanceAtom when followed by `<`. |
| `effect` `confined` `preserves` `results` | `effect` when it starts an item and is followed on the same line by `confined` or `preserves`, or, in a Contract item, by a ContractSelector; `confined`, `preserves` and `results` only after `effect`. Effect items are valid in a requirement's Constraint region, a Contract `EffectSpecification`, a Callable effect list and the leading region of an original virtual function body ([§8.11.1](../08-generics-constraints-and-contracts.md#8111-declarations)). |
| `during` `outlives` `static` | `during` after the body and `?` suffixes of an AnnotatedType; `outlives` inside an OriginRelation; `static` as an OriginAtom. |
| `origin` | When it starts an item in a position that admits an OriginRelation: an attached clause list; a struct or enum body (ContainerItem, EnumItem); a requirement Constraint region; or the leading region of a function, constructor or accessor body ([§7.5](../07-functions-and-callable-values.md#75-function-constraints)). There the item is always an OriginRelation, including `origin (...) == ...`. |
| `owner` `ref` `uniq` `obj` `rc` `arc` `objref` `objuniq` `raw` | In Semantics positions: Types, requirements, Place results, CallableReceiver, Capture operations and adaptation targets. Elsewhere they are Names, such as a Field `raw`. |
| `value` `valueborrow` `object` `objectborrow` `borrow` `owning` `reference` | As a SemanticsCategory in a requirement. |
| `move` `copy` `follow` `wrap` `bits` | Immediately after `@`; `wrap` and `bits` only when directly followed by `<`, with no whitespace or comment between them. |
| `abort` `tryWrite` `expect` `require` `message` | After `$`, the next token is a Composition Root operation name, matched by spelling even when it is a reserved word ([§13.8](../13-operators-and-assignment.md#138-reserved-syntax-and-the-composition-root)). `message` is the argument label of `$expect` and `$require`. |
| `noinit` | As the entire unparenthesized Initializer after `=`; there it is never a Name, and it is valid only as [§4.3.4](../04-arrays-indexing-and-slices.md#434-noinit) allows. |
| `of` `length` | `of` after an ArrayLength in `[N of T]` and `[N of value]`; `length` at the start of a GenericParameter. |

`open` and `task` are ordinary Names outside these contexts: in `file.open(task; path)`, `open` is a member Name and `task;` is the task argument.

A body's leading region is defined in [§7.5](../07-functions-and-callable-values.md#75-function-constraints). To start a body with an expression that calls a function named `origin`, group it, as in `(origin(b) == a)`.

```kimi
func pick(a: ref/i32, b: ref/i32) -> ref/i32 during a
    origin(b) == a          // An Origin relation: it starts an item in the leading region
    return a

let effect = 1              // An ordinary Name: no bound word follows
```

### F.3.3. Declaration modifiers

A declaration header writes, in this order: Attributes, an access specification, at most one modifier, and the introducer. The modifier is `unsafe`, `virtual` or `override` before `func`, or `open` before `struct`; `abstract` is recognized in the same position and rejected as unavailable. A word among `unsafe`, `virtual`, `override`, `abstract` and `open` is a modifier when only access words and such words follow it on the same line before a declaration introducer. Any other order, a duplicate, two modifiers, or a modifier on a declaration that does not admit it is an error. Elsewhere these words are ordinary Names, as in `func virtual()`, `let override: i32` and `file.open(path)`. Which functions may be `virtual` or `override` is defined in [§6.4](../06-declarations-and-containers.md#64-virtual-members-and-overrides); compound access words follow the `Access` production ([§F.6.1](#f61-source-units-containers-and-attributes)).

```kimi
public open struct Shape
    public virtual func area(self) -> f64 => 0.0
    virtual public func size(self) -> i32 => 0   // Error: the access word comes first
```

## F.4. Layout

### F.4.1. Lines, indentation and bodies

- Each indentation level is four U+0020 spaces. Code indentation must be a multiple of four.
- An **effective line** starts or continues a token outside comments and literal-internal text. Blank and comment-only lines are not effective lines: they produce no layout events and end neither a body nor a branch join.
- Lines inside one multiline literal, outside its interpolations, are not effective lines and are not checked for indentation.
- An interpolation `\( … )` is code inside the literal, and its `\(` opens a delimiter. Its enclosing level is the indentation of the effective line on which the string literal starts; a construct that starts on the line of `\(` uses that level as its header baseline. Every later line of the interpolation is an effective line checked by the ordinary layout rules.
- The **header baseline** of a construct is the indentation of the physical line on which its header starts, not the column of its keyword, a label or a continuation line. An indented body starts exactly one level deeper than its header baseline. A match arm list is one level deeper than the `match` header baseline, and an indented arm body one level deeper than its arm.
- In the single-item form `=> Item`, the `=>` and the start of its item are on the header's ending physical line. A `=>` followed by a line end, a `=>` at the start of a continuation line, and a `:` before a body are errors.

```kimi
let label = "mode: \(match mode
    .Fast => "fast"
    .Slow => "slow"
)"
```

### F.4.2. Layout events

The layout rules turn line structure into the events `NEWLINE`, `INDENT` and `DEDENT`:

- Outside delimiters, each effective line produces `NEWLINE INDENT` when it opens a body one level deeper; one `DEDENT` per closed body level and then `NEWLINE` when it is shallower; and `NEWLINE` when it is at the same level. A continuation line ([§F.4.3](#f43-delimiters-and-continuation), [§F.4.6](#f46-attached-lists-and-leading-dot-lines)) produces no event.
- Inside a delimiter, line breaks produce no events, except within a body nested in that delimiter: such a body produces the events above relative to its own header baseline, and a line that closes it produces only its `DEDENT`s.
- The first effective line of the source produces no event.
- End of file closes the open bodies with `DEDENT`s. An unclosed delimiter, a dedent to a level that no open body has, and an indentation increase that opens no body and continues no line are errors.

```ebnf
ItemList<X>     := X (NEWLINE X)*
IndentedList<X> := NEWLINE INDENT ItemList<X> DEDENT
```

A **body** is every position where the grammar admits `IndentedList<X>`: executable bodies, match arm lists, Container bodies, attached Origin-clause, accessor and effect lists, requirement Constraint regions and `#switch` case lists. Whether a line opens a body is decided by the grammar before continuation is considered. A `NEWLINE` that a production names (AttributePrefix, IfDirective, BranchJoin, RequireJoin) is consumed by that production. A comma, `!` or closer on the same line as the content of an indented body cannot close that body; it must first be closed by a dedent on a following line.

### F.4.3. Delimiters and continuation

- The delimiters are parentheses, brackets, Origin braces, and the angle brackets of Type arguments and generic parameters in Type positions. A comparison `<` opens no delimiter, and Type arguments in an expression never span lines ([§F.9.3](#f93-type-arguments-or-less-than)).
- Content continued inside a delimiter is indented one level per open delimiter beyond the active body or chain level. A matching closer may align with its opening line or with the content.
- Outside delimiters, an expression continues onto the next line only by a leading-`.` chain line ([§F.4.6](#f46-attached-lists-and-leading-dot-lines)), and a header only by a leading `->` line. A trailing binary operator or `=` does not continue a line; the incomplete expression is an error. A line that starts with a binary operator does not continue the previous line.
- Inside a delimiter, and outside any body nested in it, a continuation line may start with a binary operator, which continues the expression. A leading `+` or `-` there is binary when an operand precedes it in the same element.
- A leading `->` continues a function or accessor header only where its grammar still expects `->`, one level deeper than the header baseline. It does not change the baseline. Content of a delimiter opened on the `->` line is indented one level beyond that line. The next effective line at or above the `->` line's level starts the body or the next item.

```kimi
let ok = (ready
    and enabled)        // Continues inside the parentheses
let diff = (a
    - b)                // Binary subtraction
let total = first +     // Error: incomplete; a line end does not continue outside delimiters
    second
```

### F.4.4. Header expressions and delimiter regions

A **header expression** is an expression that a construct header ends with its Body or arm list: the condition of `if`, `else if`, `while` and `require`, the subject of `match`, the `in` expression of `for`, and a match-arm guard. A body-bearing expression (`if`, `match`, `for`, `while`, `loop`, `do`, a labeled construct or an anonymous function) may appear in a header expression only inside a delimiter opened within it: grouping parentheses, an argument list, or a Tuple, Array or Dictionary element. Parameter defaults are not header expressions. A header expression that continues across lines does so only inside such delimiters; grouping the whole construct does not continue its header.

A **delimiter region** starts at each grouped expression, each argument or element in parentheses or brackets (ending at its closer or separator), each item of an indented body and each match arm. Labels, transfer operands and successive `=>` tokens start no region.

- Inside a single-item body, every construct with a Body in the same region (a body-bearing expression, `unsafe`, `defer` or the `else` of `require`) also uses a single-item body. A match arm list is not an executable body and each arm starts a region, so such a match may still have indented arm bodies.
- An `if` inside a single-item `if`, `else if` or `else` body, in the same region, is grouped entirely, with or without its own `else`.
- A statement cannot be grouped as an expression. Missing required grouping and an unmatched `else` are syntax errors.

```kimi
if items.any(func (x: i32) => x > 0) => work()   // The argument list delimits the body
if (if a => b else => c) => work()                 // Grouped
let value = if a => (if b => 1 else => 2) else => 3
if do => ready() => work()                         // Error: body-bearing expression in the header
defer => unsafe
    releaseRaw(pointer)                            // Error: unsafe needs a single-item body here
```

### F.4.5. Branch joins

`BranchJoin` and `RequireJoin` are `NEWLINE?`; an `else` cannot start an item, so the join is unambiguous. An `else` or `else if` follows either on the ending physical line of a single-item body or on the next effective line. After an indented body, the dedent of that line closes the body first. A clause on its own line aligns with the header baseline of the initial `if`, and no other item intervenes. The `else` of `require` follows its condition on the same line or on the next effective line, aligned with `require`.

```kimi
func run(ready: bool, waiting: bool, valid: bool)
    if ready => start()
    else if waiting => wait()
    else
        stop()
    require valid else => return
```

### F.4.6. Attached lists and leading-dot lines

Whether an optional attached list (Origin clauses, stored-Property accessors, a Callable effect list) starts is decided by the first token of the next effective line one level deeper: the list starts only when that token can begin one of its items (`origin`; an access word, `get` or `set`; `effect`). A line starting with `.` never starts such a list.

A leading `.` at a match-arm position starts a Case Pattern, and at the first item of a new executable body it may start an InferredCaseExpression; neither continues the header. Otherwise, a line starting with a single `.` exactly one level deeper than the item it follows continues that expression as a member chain, and later chain lines use the same level. Range tokens are not single dots. These decisions use neither expected Types nor name lookup.

```kimi
let view = makeView()
    .trimmed()          // Chain continuation, not an Origin clause

struct Counter
    var count: i32 = 0
        private set     // Starts the accessor list
```

## F.5. Types

```ebnf
Type               := FunctionType | AnnotatedType
AnnotatedType      := SemanticsType "?"* BorrowOrigin?
FunctionType       := FunctionParameters "->" FunctionResult
FunctionParameters := "(" TaskSlot? TrailingList<Type>? ")"
FunctionResult     := Type | PlaceResult
PlaceResult        := "place" ("ref" | "uniq") "/" SemanticsType BorrowOrigin?
SemanticsType      := Semantics "/" SemanticsType | TypeAtom
TypeAtom           := CoreType | "(" Type ")"
Semantics          := BuiltinSemantics | Name
BuiltinSemantics   := "owner" | "ref" | "uniq" | "obj" | "rc" | "arc" | "objref" | "objuniq" | "raw"
CoreType           := NamedType | UnitType | TupleType | FixedArrayType
UnitType           := "(" ")"
TupleType          := "(" Type "," TrailingList<Type>? ")"
FixedArrayType     := "[" ArrayLength "of" ArrayElementType "]"
ArrayElementType   := Type | "_"
ArrayLength        := "_" | IntegerLiteral | ConstantName | "(" LengthExpression ")"
ConstantName       := "::"? Name ("." Name)*
LengthExpression   := LengthProduct (("+" | "-") LengthProduct)*
LengthProduct      := LengthUnary (("*" | "/" | "%") LengthUnary)*
LengthUnary        := ("+" | "-") LengthUnary | IntegerLiteral | ConstantName
                    | "(" LengthExpression ")"
NamedType          := NamedReference OriginApplication?
NamedReference     := ContainerPath OriginBindingSet?
ContainerPath      := "::"? TypeSegment PathSuffix*
                    | BoundContainerQualifier PathSuffix+
PlainContainerPath := "::"? TypeSegment ("." TypeSegment)*
BoundContainerQualifier := "(" ContainerPath OriginBindingSet ")"
PathSuffix         := "." TypeSegment | "." "(" ContractReference ")" "." Name
TypeSegment        := TypeName TypeArguments?
TypeName           := Name | PrimitiveType | "Self"
PrimitiveType      := "isize" | "usize" | "i8" | "i16" | "i32" | "i64" | "i128"
                    | "u8" | "u16" | "u32" | "u64" | "u128" | "f32" | "f64"
                    | "bool" | "char" | "string"
TypeArguments      := "<" TrailingList<GenericArgument> ">"
GenericArgument    := Type | ArrayLength
GenericParameters  := "<" TrailingList<GenericParameter> ">"
GenericParameter   := Name | Name "/" Name | "length" Name
```

- `Name "/" Name` declares a pair parameter: its pair binder and pair target ([§8.1.1](../08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)). `length Name` declares a function length slot ([§4.4](../04-arrays-indexing-and-slices.md#44-function-length-parameters)). A GenericArgument that is a Name or grouped expression is classified by the declared slot kind, never by its uses ([§4.4](../04-arrays-indexing-and-slices.md#44-function-length-parameters)).
- `_` as an ArrayLength or ArrayElementType is valid only in an initialized local annotation ([§4.3.1](../04-arrays-indexing-and-slices.md#431-array-literals-and-annotation-holes)).
- Where a borrow annotation attaches is defined in [§3.3.2](../03-types-and-values.md#332-nested-semantics-and-origin-annotations). A `ContainerPath` may name a Contract or associated Type in the positions that [§9.7.1](../09-names-signatures-and-access.md#971-bound-container-paths) and [§8.5](../08-generics-constraints-and-contracts.md#85-associated-types) allow; a named target may denote a runtime Contract View ([§8.12](../08-generics-constraints-and-contracts.md#812-runtime-contracts)).
- A BoundContainerQualifier is a Type-side form only; it is unavailable in adaptation targets.
- OriginApplication, OriginBindingSet and BorrowOrigin are defined in [§F.8](#f8-origins).

```kimi
func first<T>(values: ref/Array<T>) -> ref/T during values
    return values[0]@ref

let grid: [2 of [3 of i32]] = [2 of [3 of 0]]
let check: (i32) -> bool = func (x: i32) => x > 0
let maybe: ref/i32? = Option<ref/i32>.None
```

## F.6. Declarations

### F.6.1. Source units, Containers and Attributes

```ebnf
SourceUnit           := ItemList<SourceItem>?
SourceItem           := AliasDeclaration | Declaration | ExecutableItem | Directive<SourceItem>
Declaration          := AttributePrefix? UnattributedDeclaration
UnattributedDeclaration := GroupDeclaration | RootGroupDeclaration | StructureDeclaration
                      | EnumDeclaration | ContractDeclaration | FunctionDefinition
                      | ForeignFunctionDeclaration | SpecializationDeclaration
                      | StoredPropertyDeclaration | ComputedDeclaration
                      | ConstructorDeclaration | DropDeclaration
Attribute            := "#" AttributeName ("(" TrailingList<Argument>? ")")?
AttributeName        := ? a Name whose first character has General Category Lu, [§2.5](../02-source-and-lexical-structure.md#25-names) ?
AttributePrefix      := Attribute (Attribute | NEWLINE)* ("#" "if" CompileCondition NEWLINE)?   // the Attributes attach to the one declaration the #if controls, [§6.7](../06-declarations-and-containers.md#67-attributes)
Access               := "public" | "internal" | "private" | "protected"
                      | "protected" "internal" | "private" "protected"
QualifiedName        := Name ("." Name)*
AliasDeclaration     := "alias" (Name "=>")? ContainerReference OriginClauses?
GroupDeclaration     := Access? "group" Name ContainerBody
RootGroupDeclaration := Access? "rootgroup" QualifiedName ContainerBody
StructureDeclaration := Access? "open"? "struct" Name GenericParameters? OriginHeader?
                        BaseClause? ContainerBody
BaseClause           := ":" NamedReference
ContainerBody        := IndentedList<ContainerItem>?
ContainerItem        := Declaration | ConstraintClause | OriginRelation
                      | AssociatedTypeSpecification | ConditionalConformance
                      | Directive<ContainerItem>
EnumDeclaration      := Access? "enum" Name GenericParameters? OriginHeader?
                        IndentedList<EnumItem>
EnumItem             := EnumCase | AttributedFunctionDefinition | SpecializationDeclaration
                      | ConstraintClause | OriginRelation | AssociatedTypeSpecification
                      | ConditionalConformance | Directive<EnumItem>
EnumCase             := Name ("(" TrailingList<Type> ")")? OriginClauses?
```

- At the source root, `let` and `var` items are LocalBindings; which items each Container admits, nesting, and omitted or empty Container bodies are defined in [§6.1.1](../06-declarations-and-containers.md#611-source-root-and-nested-containers). An enum body remains nonempty after selection.
- Aliases appear only at the source root, before other items ([§18.2](../18-modules-and-dependencies.md#182-aliases)). `=>` in an alias introduces a reference, never a Body.
- Modifier order and recognition: [§F.3.3](#f33-declaration-modifiers).
- The `#` forms are: `#` followed by `if`, `switch` or `case` is a directive ([§F.11](#f11-compile-time-directives)); `#` followed by a Name starting with a Lu character is an Attribute; any other `#Name` is an error. Attribute placement and targets are defined in [§6.7](../06-declarations-and-containers.md#67-attributes).

```kimi
rootgroup App.Tools
    #Test
    func check() => ()
    // #test              // Error: an Attribute Name starts with a Lu letter
```

### F.6.2. Contracts, requirements and conformance

```ebnf
ContractDeclaration  := Access? "contract" Name GenericParameters? ContractParentList?
                        IndentedList<ContractItem>?
ContractParentList   := ":" ContractReference ("," ContractReference)*
ContractReference    := ContainerReference
ContainerReference   := NamedReference
ContractSelector     := ContainerPath | "(" ContractReference ")"
ContractItem         := FunctionRequirement | PropertyRequirement | AssociatedTypeDeclaration
                      | AssociatedTypeSpecification | EffectSpecification | ConstraintClause
                      | Directive<ContractItem>
FunctionRequirement  := "unsafe"? "func" Name GenericParameters?
                        ParameterList<RequirementParameter> ("->" FunctionResult)?
                        RequirementConstraints?
RequirementParameter := ParameterName ":" Type | ReceiverShorthand
RequirementConstraints := IndentedList<ConstraintClause | OriginRelation | EffectClause>
EffectClause         := "effect" EffectBound
EffectSpecification  := "effect" ContractSelector "." Name EffectBound
EffectBound          := "confined" | "preserves" "results"
AssociatedTypeDeclaration := "associate" Name OriginParameters? ("is" IsRequirement)?
                             FormationType? IndentedList<OriginRelation>?
FormationType        := "for" Type
AssociatedTypeSpecification := "associate" AssociatedTypeName OriginParameters?
                               "is" IsRequirement OriginClauses?
AssociatedTypeName   := Name | ContractSelector "." Name
AssociatedTypeReference := TypeQualifier "." Name OriginApplication?
                         | TypeQualifier "." "(" ContractReference ")" "." Name OriginApplication?
TypeQualifier        := ContainerPath
ConditionalConformance := "Self" "is" ContractReference "when" ConformanceConditions
                          IndentedList<ConditionalImplementationItem>?
ConformanceConditions := ConformanceCondition ("," ConformanceCondition)*
ConformanceCondition := ConstraintSubject "is" ConformanceRequirement
ConformanceRequirement := ConformanceAtom ("and" ConformanceAtom)*
ConformanceAtom      := CallableRequirement | Type | Semantics | SemanticsCategory
                      | "(" ConformanceRequirement ")"
ConditionalImplementationItem := AttributedFunctionDefinition
                      | AttributePrefix? ComputedDeclaration
                      | AssociatedTypeSpecification
                      | Directive<ConditionalImplementationItem>
ConstraintClause     := ConstraintSubject "is" IsRequirement CallableEffects?
CallableEffects      := IndentedList<EffectClause>
ConstraintSubject    := Name | "Self" | AssociatedTypeReference
IsRequirement        := "not" Requirement | PositiveRequirement
PositiveRequirement  := RequirementAtom ("and" RequirementUnary)* ("or" RequirementAnd)*
Requirement          := RequirementAnd ("or" RequirementAnd)*
RequirementAnd       := RequirementUnary ("and" RequirementUnary)*
RequirementUnary     := "not" RequirementUnary | RequirementAtom
RequirementAtom      := CallableRequirement | Type | Semantics | SemanticsCategory
                      | "(" Requirement ")"
SemanticsCategory    := "value" | "valueborrow" | "object" | "objectborrow"
                      | "borrow" | "owning" | "reference"
CallableRequirement  := "Callable" "<" (CallableReceiver ",")? FunctionType ">"
CallableReceiver     := "ref" | "uniq" | "owner"
```

- `is` in a requirement consumes its right side through `or`, and a `not` directly after `is` negates all of it: `T is not A or B` means `T is not (A or B)` ([§8.2](../08-generics-constraints-and-contracts.md#82-constraints-and-requirement-expressions)). An outer `and` after a BorrowOrigin belongs to the requirement: `T is ref/U during a and Copy`.
- Placement of Constraint Clauses, effect items and OriginRelations: [§7.5](../07-functions-and-callable-values.md#75-function-constraints), [§8.2](../08-generics-constraints-and-contracts.md#82-constraints-and-requirement-expressions), [§8.11.1](../08-generics-constraints-and-contracts.md#8111-declarations). Associated Types: [§8.5](../08-generics-constraints-and-contracts.md#85-associated-types). Conditional conformance: [§8.8](../08-generics-constraints-and-contracts.md#88-conditional-conformance).

```kimi
contract Source
    associate Element
    func read(self: uniq/Self) -> Element

func apply<F>(f: ref/F, x: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    return f(x)
```

### F.6.3. Functions, parameters and bindings

```ebnf
FunctionHeader       := Access? FunctionModifier? "func" Name GenericParameters?
                        ParameterList<Parameter> ("->" FunctionResult)?
FunctionModifier     := "unsafe" | "virtual" | "override"
FunctionDefinition   := FunctionHeader FunctionBody
AttributedFunctionDefinition := AttributePrefix? FunctionDefinition
ForeignFunctionDeclaration := FunctionHeader   // with #LibraryImport, [§22.6](../22-core-execution-and-foreign-functions.md#226-foreign-function-imports)
FunctionBody         := Body<FunctionItem>
FunctionItem         := Expression | LocalBinding | AttributedFunctionDefinition | Statement
                      | ConstraintClause | OriginRelation | EffectClause
                      | Directive<FunctionItem>
SpecializationDeclaration := "specialize" "func" Name TypeArguments
                             "(" TaskSlot? TrailingList<SpecializationParameter>? ")"
                             ("->" FunctionResult)? ExecutableBody
SpecializationParameter := Name ("=>" Name)? ":" Type | ReceiverShorthand
ConstructorDeclaration := Access? "init" ParameterList<Parameter> BaseInitializer? FunctionBody
BaseInitializer      := ":" "base" "(" TaskSlot? TrailingList<Argument>? ")"
DropDeclaration      := "drop" ExecutableBody
ParameterList<P>     := "(" TaskSlot? (TrailingList<P> | List<P>? "!" TrailingList<P>)? ")"
TaskSlot             := "task" ";"
Parameter            := Attribute* ParameterCore
ParameterCore        := ParameterName ":" Type ("=" Initializer)? | ReceiverShorthand
ParameterName        := Name ("=>" Name)?
ReceiverShorthand    := "self"
LocalBinding         := ("let" | "var") Name (":" Type)? ("=" Initializer)? OriginClauses?
Initializer          := "noinit" | Expression
```

- A function body's leading region holds ConstraintClauses (with their effect lists), OriginRelations and, in an original virtual function, EffectClauses, in any order; the grammar admits them as items, and [§7.5](../07-functions-and-callable-values.md#75-function-constraints) restricts their placement, including the misplaced-Constraint error and the accessor and destructor limits.
- `ReceiverShorthand` is valid only where [§7.3](../07-functions-and-callable-values.md#73-receivers) allows a receiver; the `!` boundary is defined in [§7.2.1](../07-functions-and-callable-values.md#721-argument-name-boundary).
- Which lists admit the task slot is defined in [§24.2.1](../24-suspension-and-asynchronous-tasks.md#2421-form).
- A ForeignFunctionDeclaration has no body and appears only where [§22.6](../22-core-execution-and-foreign-functions.md#226-foreign-function-imports) allows. Explicit specializations follow [§8.13](../08-generics-constraints-and-contracts.md#813-explicit-full-function-specialization), constructors [§6.3](../06-declarations-and-containers.md#63-constructors), and `drop` [§16.3](../16-scope-exit-and-destruction.md#163-destruction-declarations).
- After `func`, a Name starts a function definition and `(` or `[` starts a FunctionExpression ([§F.9.1](#f91-productions)).

```kimi
func find(task; value: i32 ! start: i32, end: i32) -> isize => 0
func scale(value: f64, by => factor: f64 = 2.0) -> f64 => value * factor

func fill()
    unsafe
        var buffer: [4 of u8] = noinit
        buffer[0] = 1
func f(x: i32 = noinit) => x       // Error: noinit is only for a local var
```

## F.7. Properties

```ebnf
StoredPropertyDeclaration := Access? ("let" | "var") Name (":" Type)? ("=" Initializer)?
                             IndentedList<OriginRelation | StoredAccessor>?
ComputedDeclaration  := Access? "computed" Name ":" Type
                        IndentedList<OriginRelation | CustomAccessor>
StoredAccessor       := Access? ("get" | "set") | CustomAccessor
CustomAccessor       := Access? (GetterSignature | SetterSignature) AccessorBody
GetterSignature      := "get" "(" AccessorReceiver? ")" "->" Type
SetterSignature      := "set" "(" (AccessorReceiver ",")? "value" ":" Type ")" "->" UnitType
AccessorReceiver     := "self" ":" Type
AccessorBody         := Body<FunctionItem>
PropertyRequirement  := "property" Name ":" Type
                        ("has" RequiredAccessor ("," RequiredAccessor)*
                         | IndentedList<RequiredSignature>)
RequiredAccessor     := "get" | "set"
RequiredSignature    := (GetterSignature | SetterSignature) OriginClauses?
```

Each accessor occurs at most once, in either order. Accessor rules, omitted receivers and the Property kinds are defined in [§11.3](../11-properties.md#113-accessor-functions); Property requirements in [§11.9](../11-properties.md#119-contract-property-requirements).

```kimi
struct Account
    var balance: i32 = 0
        get
        private set
    computed doubled: i32
        get(self: ref/Self) -> i32 => self.balance * 2

contract Named
    property name: string has get
```

## F.8. Origins

```ebnf
OriginHeader       := "{" TrailingList<Name> "}"
OriginBindingSet   := "{" Name ","? "}"
OriginApplication  := "(" List<OriginAtom> ")"
OriginParameters   := "(" List<Name> ")"
BorrowOrigin       := "during" OriginAtom
OriginExpression   := OriginAtom ("and" OriginAtom)*
OriginAtom         := Name ("." Name)? | "static" | "(" OriginExpression ")"
OriginRelation     := "origin" OriginExpression ("==" | "outlives") OriginExpression
OriginClauses      := IndentedList<OriginRelation>
```

- An OriginHeader follows the generic parameters of a struct or enum. An OriginBindingSet follows a named Type and names one binding set ([§15.3.1](../15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets)). An OriginApplication follows only an associated-Type name in a Type ([§8.5.4](../08-generics-constraints-and-contracts.md#854-origin-parameters)) and is never a call, Tuple or grouping.
- An intersection after `during` is parenthesized: `during (a and b)`. Relation operands take full OriginExpressions.
- OriginRelations are declaration metadata, attached one level below their owner or placed in a leading region ([§15.3.3](../15-ownership-and-lifetime-analysis.md#1533-declaration-attached-relations), [§7.5](../07-functions-and-callable-values.md#75-function-constraints)).

```kimi
struct View<T> {source}
    let items: ref/Array<T> during source

func pick<T>(a: ref/T, b: ref/T) -> ref/T during a
    origin b outlives a
    T is Copy                  // Still in the leading region
    return a
```

## F.9. Expressions

### F.9.1. Productions

```ebnf
Expression       := Assignment
Assignment       := RangeExpression (AssignmentOperator Assignment)?
AssignmentOperator := "=" | "+=" | "-=" | "*=" | "/=" | "%="
                    | "<<=" | ">>=" | "&=" | "^=" | "|="
RangeExpression  := OrExpression | OrExpression? ".." OrExpression?
                  | OrExpression? "..=" OrExpression
OrExpression     := AndExpression ("or" AndExpression)*
AndExpression    := Comparison ("and" Comparison)*
Comparison       := BitOr (("<" | "<=" | ">" | ">=" | "==" | "!=") BitOr)?
                  | BitOr "is" "not"? NamedCoreType
NamedCoreType    := ? a ContainerPath with no Origin, "during" or binding set, also inside its
                      Type arguments, [§13.6.4](../13-operators-and-assignment.md#1364-runtime-is-tests) ?
BitOr            := BitXor ("|" BitXor)*
BitXor           := BitAnd ("^" BitAnd)*
BitAnd           := Shift ("&" Shift)*
Shift            := Additive (("<<" | ">>") Additive)*
Additive         := Multiplicative (("+" | "-") Multiplicative)*
Multiplicative   := Try (("*" | "/" | "%") Try)*
Try              := "try" Try | Adapted
Adapted          := Prefix ("@" (ClosedTarget PostfixSuffix* | OpenTarget))*
Prefix           := ("+" | "-" | "not" | "*" | "^" | "++" | "--") Prefix | Postfix
Postfix          := Primary PostfixSuffix*
PostfixSuffix    := "." (Name | TupleIndex)
                  | "." "(" ContractReference ")" "." Name
                  | "(" TaskSlot? TrailingList<Argument>? ")"
                  | AdjacentTypeArguments | "[" Expression "]" | "++" | "--"
                  | "@" "follow"
AdjacentTypeArguments := TypeArguments   // only as decided by [§F.9.3](#f93-type-arguments-or-less-than)
Argument         := (Name ":")? Expression
Primary          := "::"? Name | ("Self" | PrimitiveType) "." Name | Literal
                  | "(" Expression ")" | TupleExpression | ArrayExpression
                  | DictionaryExpression | FunctionExpression
                  | LabelPrefix? LabelableConstruct | Transfer
                  | BaseInvocation | ConstructionExpression | BoundContainerExpression
                  | InferredCaseExpression | CompositionRootExpression
BaseInvocation   := "base" "." Name AdjacentTypeArguments?
                    "(" TaskSlot? TrailingList<Argument>? ")"
ConstructionExpression := (PlainNamedType | BoundContainerQualifier) "." "init"
                          "(" TaskSlot? TrailingList<Argument>? ")"
PlainNamedType   := ContainerPath
BoundContainerExpression := BoundContainerQualifier
                            ("." Name | "." "(" ContractReference ")" "." Name)
InferredCaseExpression := "." Name ("(" TrailingList<Expression> ")")?
TupleExpression  := "(" Expression "," TrailingList<Expression>? ")"
ArrayExpression  := "[" TrailingList<Expression>? "]" | "[" ArrayLength "of" Expression "]"
DictionaryExpression := "[" ":" "]" | "[" TrailingList<DictionaryEntry> "]"
DictionaryEntry  := Expression ":" Expression
FunctionExpression := "func" CaptureList? "(" TaskSlot? TrailingList<AnonymousParameter>? ")"
                      ("->" FunctionResult)? ExecutableBody
AnonymousParameter := Name (":" Type)?
CaptureList      := "[" TrailingList<Capture>? "]"
Capture          := "var"? Name ("@" ("move" | "ref" | "uniq"))?
CompositionRootExpression := "$" "abort" "(" Expression ")"
                           | "$" "tryWrite" "(" Expression "," StringLiteral ")"
```

- `.init` after a Type qualifier always forms a ConstructionExpression ([§6.3](../06-declarations-and-containers.md#63-constructors)). Qualified enum Cases are ordinary Postfix forms resolved by [§6.5.2](../06-declarations-and-containers.md#652-case-references).
- `("Self" | PrimitiveType) "." Name` selects a static member of the current Type or a primitive Type, as in `Self.marker()` and `isize.MaxValue` ([§9.6](../09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)).
- Runtime `is` takes one NamedCoreType and does not consume an outer `and` or `or`. Requirement `is` uses IsRequirement ([§F.6.2](#f62-contracts-requirements-and-conformance)) in its own contexts.
- The operand of `return`, `exit` and `yield` is a full Expression ([§F.10.2](#f102-control-constructs)).

```kimi
struct Counter
    var count: i32 = 0
    func total(self) -> i32 => self.count + Self.marker()
    func marker() -> i32 => 1

let entry = Pair<i32, string>.init(1, "one")
let scaled = apply(func [factor] (x: i32) => x * factor)
let table = [1: "one", 2: "two"]
```

### F.9.2. Precedence and associativity

Earlier rows bind more tightly. The grammar of [§F.9.1](#f91-productions) encodes this table.

| Level | Operators or syntax | Association |
| --- | --- | --- |
| 1 | `.name`, `(...)`, `<Types>`, `[...]`, `@follow`, postfix `++` `--` | Postfix chain, left to right |
| 2 | Prefix `+` `-` `not` `*` `^` `++` `--` | Right |
| 3 | `@` operations other than `@follow` (including `@raw`, `@raw/U`, `@wrap<U>`, `@bits<U>`) | Left |
| 4 | Prefix `try` | Right |
| 5 | `*` `/` `%` | Left |
| 6 | `+` `-` | Left |
| 7 | `<<` `>>` | Left |
| 8 | `&` | Left |
| 9 | `^` | Left |
| 10 | `\|` | Left |
| 11 | `<` `<=` `>` `>=` `==` `!=`, runtime `is` and `is not` | Non-associative |
| 12 | `and` | Left |
| 13 | `or` | Left |
| 14 | `..` `..=` | Non-associative |
| 15 | `=` and compound assignments | Right |

- An unparenthesized comparison chain such as `a < b < c` or `a < b == flag` is a syntax error.
- A level-2 prefix operator cannot take `try` directly: write `-(try x)`.

| Written form | Grouping |
| --- | --- |
| `flags & mask == 0` | `(flags & mask) == 0` |
| `-x@i64` | `(-x)@i64` |
| `*p@raw` | `(*p)@raw`, the address of the pointee |
| `r@follow.x` | `(r@follow).x` |
| `try x@move * 2` | `(try (x@move)) * 2` |
| `not ready and n != 0` | `(not ready) and (n != 0)` |
| `start + 1..end - 1` | `(start + 1)..(end - 1)` |

### F.9.3. Type arguments or less-than

In an expression, a `<` after a Name (in a postfix suffix, after `base.f`, or after the Type name of an adaptation target following `@`) begins Type arguments exactly when all of these hold:

1. It directly follows a Name, `Self` or a primitive Type name, with no whitespace or comment between them.
2. The tokens after it, on the same line, form a `TypeArguments` closed by a matching `>`; a token beginning with `>` is split as in the generic-closer rule of [§F.2.1](#f21-tokens-comments-and-punctuation).
3. The token after that `>` is `(` or `.`, or a token that cannot begin an expression (for example `)`, `,`, `]`, `==`, `@`, `=>` or the line end).

Otherwise `<` is the less-than operator. The test uses tokens only, never name lookup, and a later failure never revises it. Everywhere else, including Type annotations, the operand of `is` and the Type argument of `@wrap` and `@bits`, a `<` after a Type name always begins Type arguments. After an open adaptation target, a following `(` or `[` is still a syntax error ([§F.9.4](#f94-adaptation-targets)).

```kimi
let a = f<i32>(x)        // Type arguments
let b = i<n              // Less-than: no matching >
let c = (p<q, r>(s))     // The generic call p<q, r>(s)
let d = (p < q, r > s)   // Two comparisons: the first < is preceded by a space
let e = x@Box<i32> == y  // Type arguments: == cannot begin an expression
let g = (x@Box<i32>) + 1 // Grouped: + could begin an operand
```

### F.9.4. Adaptation targets

```ebnf
ClosedTarget     := "move" | "copy" | BuiltinSemantics          // not followed by "/"
                  | (Semantics "/")* DelimitedTarget
OpenTarget       := "wrap" TypeArguments | "bits" TypeArguments
                  | (Semantics "/")* "::"? TypeSegment PathSuffix* OriginBindingSet? "?"*
                  | (Semantics "/")* DelimitedTarget "?"+
DelimitedTarget  := UnitType | "(" Type ")" | TupleType | "[" ArrayLength "of" Type "]"
```

- Postfix suffixes (`.`, a call, an index, `++`, `--`, `@follow`) follow only a ClosedTarget. After an OpenTarget, `(` and `[` are syntax errors and `.` continues the qualified Type head; write `(x@T).name`, `(f@T)()` and `(a@T)[0]`.
- After `@`, a Name or BuiltinSemantics word followed by `/` is a Semantics prefix, whatever the whitespace, so `a@T / b` names the target `T/b`. `move`, `copy` and `follow` are never names, and `follow` is never a target; a `/` after `move` or `copy` is a syntax error and after `follow` it is division. An ungrouped target takes no outer Function Type arrow, and an ungrouped Type head consumes no OriginApplication and no BorrowOrigin; group the target, as in `x@(Self.Item(a))`. A bare `raw` is the address operation ([§5.4](../05-raw-pointers-and-unsafe-memory.md#54-addresses-and-conversions)). The meaning of each form is defined in [§13.5.1](../13-operators-and-assignment.md#1351-forms-and-adaptation-targets).

```kimi
let n = tasks@uniq.length         // Closed target: the postfix chain continues
let x = (point@Point).x           // Open target: group before selection
let w = big@wrap<u8>
let e = items@Array<i32>[0]       // Error: group the adaptation first
```

## F.10. Statements, bodies and patterns

### F.10.1. Bodies and statements

```ebnf
Body<Item>       := "=>" SingleItem | IndentedList<Item>
SingleItem       := Expression | Statement
ExecutableBody   := Body<ExecutableItem>
ExecutableItem   := Expression | LocalBinding | AttributedFunctionDefinition | Statement
                  | Directive<ExecutableItem>
Statement        := UnsafeStatement | DeferStatement | RequireStatement | DiscardStatement
                  | TestVerification
UnsafeStatement  := "unsafe" ExecutableBody
DeferStatement   := "defer" ExecutableBody
RequireStatement := "require" Expression RequireJoin "else" ExecutableBody
RequireJoin      := NEWLINE?
DiscardStatement := "_" "=" Expression
TestVerification := "$" ("expect" | "require") "(" Expression ("," "message" ":" Expression)? ")"
```

- A SingleItem contains no declaration or directive. A TestVerification is a standalone item, never an operand ([§17.6](../17-failure-handling.md#176-test-verification-operations)).
- Body semantics and the nonempty-body rule: [§14.2](../14-control-flow.md#142-bodies-and-evaluation-contexts), [§14.2.1](../14-control-flow.md#1421-body-forms).

```kimi
defer => close(file)
_ = compute()
unsafe
    releaseRaw(pointer)
```

### F.10.2. Control constructs

```ebnf
LabelPrefix        := "label" Name ":"
LabelableConstruct := IfExpression | MatchExpression | ForExpression | WhileExpression
                    | LoopExpression | DoExpression
IfExpression       := "if" Expression ExecutableBody
                      (BranchJoin "else" "if" Expression ExecutableBody)*
                      (BranchJoin "else" ExecutableBody)?
BranchJoin         := NEWLINE?
MatchExpression    := "match" Expression IndentedList<MatchArm>
MatchArm           := Pattern ("if" Expression)? ExecutableBody
ForExpression      := "for" ForBinding "in" Expression ExecutableBody
ForBinding         := ForSlot | "(" ForSlot "," TrailingList<ForSlot>? ")"
ForSlot            := Name | "var" Name | "_"
WhileExpression    := "while" Expression ExecutableBody
LoopExpression     := "loop" ExecutableBody
DoExpression       := "do" ExecutableBody
Transfer           := "return" Expression?
                    | "exit" ("to" Name)? Expression?
                    | "continue" ("to" Name)?
                    | "yield" ("to" Name)? Expression?
```

- `label`, the Label Name, `:` and the construct keyword share one physical line ([§14.4](../14-control-flow.md#144-labels)).
- A match arm list is nonempty and admits no directive.
- A parenthesized ForBinding follows the Tuple convention: `(a, b)` decomposes a pair and `(a,)` a one-element Tuple; `(x)` is a syntax error ([§14.6.1](../14-control-flow.md#1461-for-and-while)).
- `to Label` and the operand start on the keyword's line. The operand is omitted exactly when the next token is the line end, end of file, a layout boundary, or an enclosing `,`, `)`, `]`, `else`, `=>` or parameter-list `!`; `:` and `and` never omit it. A Label and the first token of its operand are separated by whitespace or a block comment: `exit to work (x)`, never `exit to work(x)`. Meaning: [§14.5.1](../14-control-flow.md#1451-syntax-and-operands).

```kimi
label outer: for (key, value) in table
    if key == 0 => continue to outer
    match value
        .Some(let v) if v > 0 => use(v)
        _ => ()
for (only,) in singles => use(only)
// for (x) in xs => use(x)            // Error: write `for x in xs`
```

### F.10.3. Patterns

```ebnf
Pattern          := "_" | LiteralPattern | BindingPattern | CasePattern
                  | UnitPattern | TuplePattern | GroupedPattern
BindingPattern   := ("let" | "var") Name
CasePattern      := CaseReference ("(" TrailingList<Pattern> ")")?
CaseReference    := PlainContainerPath "." Name | "." Name
UnitPattern      := "(" ")"
TuplePattern     := "(" Pattern "," TrailingList<Pattern>? ")"
GroupedPattern   := "(" Pattern ")"
LiteralPattern   := "true" | "false" | "-"? IntegerLiteral | CharLiteral
                  | NonInterpolatedString
NonInterpolatedString := ? a StringLiteral without Interpolation, [§14.8.1](../14-control-flow.md#1481-patterns) ?
```

A CaseReference qualifier writes no binding set or `during` for the enum's own Origins; Types inside its Type arguments are complete Types. Pattern meaning: [§14.8.1](../14-control-flow.md#1481-patterns).

```kimi
match outcome
    .Done((let a, 0)) => use(a)
    Outcome.Failed(let e) => report(e)
    _ => ()
```

## F.11. Compile-time directives

```ebnf
Directive<Item>       := IfDirective<Item> | SwitchDirective<Item>
IfDirective<Item>     := "#" "if" CompileCondition (NEWLINE Item | IndentedList<Item>)
SwitchDirective<Item> := "#" "switch" IndentedList<SwitchArm<Item>>
SwitchArm<Item>       := "#" "case" (CompileCondition | "_") IndentedList<Item>
CompileCondition      := CompileAnd ("or" CompileAnd)*
CompileAnd            := CompileComparison ("and" CompileComparison)*
CompileComparison     := CompileUnary (("==" | "!=") CompileUnary)?
CompileUnary          := "not" CompileUnary | CompileAtom
CompileAtom           := "true" | "false" | ("+" | "-")? IntegerLiteral | PlainString | Name
                       | "(" CompileCondition ")"
PlainString           := ? a StringLiteral without Interpolation, [§19.2](../19-compile-time-directives.md#192-conditions) ?
```

- `#case _` occurs at most once, as the last arm.
- `Item` keeps the category of the surrounding list, so a directive never admits an item that the list forbids. Directives appear only in the item lists that name `Directive<…>` above; which lists those are, and selection, are defined in [§19.1](../19-compile-time-directives.md#191-placement-and-structure). A `#case` outside the arm list of a `#switch` is an error.
- Excluded syntax is parsed with this same grammar ([§19.4](../19-compile-time-directives.md#194-excluded-syntax)).

```kimi
#if windows
alias Kimi.Windows

func platformName() -> string
    #switch
        #case windows
            return "windows"
        #case _
            return "other"
```
