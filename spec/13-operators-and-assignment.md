# 13. Operators and assignment

[Specification index](../SPEC.md)

## 13.1. Precedence and associativity

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

In ordinary expressions, `is` / `is not` ends after one named struct Core; outer `and` / `or` remain Boolean operators. In dedicated compile-time contexts, `is` instead follows the asymmetric [requirement-expression rule](08-generics-constraints-and-contracts.md#83-requirement-expressions). Prefix `not` precedence is unchanged: negate a runtime test with `value is not Dog` or `not (value is Dog)`. `as` is reserved.

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

## 13.2. Unary operators

| Operator | Operand and result |
| --- | --- |
| `+value` | Numeric value, unchanged Type and value. |
| `-value` | Negated signed integer or floating-point value. |
| `not value` | Negated `bool`. |
| `*pointer` | Raw-pointer place under [unsafe dereference rules](05-raw-pointers-and-unsafe-memory.md#52-dereference-and-ownership). |
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

## 13.3. Arithmetic, bitwise, and shift operators

`+ - * /` take operands of the same numeric Type and return that Type. `%` accepts integers only. Integer division truncates toward zero. On mathematical integers, the remainder satisfies `a = (a / b) * b + a % b`; a nonzero remainder has the dividend's sign.

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010
let shifted = bits << 1     // 0b10100
```

Check integer `+ - *`, unary `-`, increment/decrement, and the arithmetic part of compound assignment for overflow. Integer division or remainder by zero is invalid. Signed minimum divided by `-1`, including `% -1`, is also invalid. These failures follow [Abort Termination](17-failure-handling.md#173-abort-termination), including its constant-evaluation rule.

`& | ^` perform bitwise AND, OR, and XOR on the same integer Type; they do not accept `bool`. `<< >>` return the left operand's integer Type and accept any integer Type on the right, requiring `0 <= shift < bit width of left operand`. An invalid count is a check failure. Left shift discards high bits and inserts zero low bits; right shift sign-extends signed integers and zero-extends unsigned integers. Discarded shift bits are not arithmetic overflow.

Floating-point operations follow IEEE 754 for `f32` / `f64`, using round-to-nearest, ties-to-even. They support infinity, NaN, and signed zero; floating-point division by zero does not use integer failure rules. Do not implicitly reassociate or fuse ordinary operations when rounding or NaN results would change.

`string + string` denotes concatenation without implicit numeric stringification. Its operand acquisition and ownership rules, including string `+=`, are deferred to a common operator model. That design must specify Copy/Move or borrowing, Loan duration, result ownership, aliasing/self-update, and failure behavior while preserving the evaluation and write order in §13.7. No particular acquisition strategy is adopted here. These operations cannot pass executable finalization until those rules are defined and implemented; parsing or Type checking alone grants no ownership permission.

Raw-pointer arithmetic is limited to the forms and unsafe conditions in [pointer arithmetic](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing); its undefined-behavior rules are distinct from checked integer arithmetic.

## 13.4. Comparison and logical operators

`== != < <= > >=` return `bool`. Numeric operands must have the same Type. `bool` and Unit support equality only. `char` compares Unicode scalar values. `string` uses UTF-8 byte equality and lexicographic order without normalization or locale processing.

Floating-point `+0.0 == -0.0` is true. With a NaN operand, `== < <= > >=` are false and `!=` is true; floating-point ordering is not total.

Comparisons may borrow their operands and do not Move non-Copy owned values solely to compare them. User-defined comparison requires an explicit Type capability. Safe borrows compare referent values of the same Type using that Type's comparison capability. Tuples support elementwise equality and lexicographic ordering when all corresponding elements support the required comparison.

Built-in comparisons do not implicitly adapt an owned operand to match a safe-borrow operand. Compare two owned values or two safe borrows with matching immediate referent Types. The two outer borrow Origins need not be identical; each operand must remain valid through the comparison.

For a built-in comparison that inspects a non-Copy owned Place, form an implicit shared Loan when that operand is evaluated. Operands are evaluated left to right: the left operand's Loan begins before evaluation of the right operand and remains active through the comparison. Both inspections require Initialized values. Apply the normal [Loan conflict rules](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts) throughout operand evaluation; a later operand cannot Move, replace, destroy, or exclusively borrow the earlier borrowed Place. Optimization cannot change this acceptance rule.

On normal completion, comparison-only Loans end after the comparison; its bool result retains no operand Loan. They also end if a control transfer abandons the comparison. Existing Loans retain their own lifetimes, and temporary operands retain their [normal temporary lifetimes](03-types-and-values.md#362-lifetime-and-borrowing); ending an inspection Loan does not destroy a temporary early. Abort follows §17.3.

```kimi
func take(text: string) -> string => text

let text = "a"
let same = text == "a" // Shared inspection; text is not Moved.
// let invalid = text == take(text)
// Error: the left operand's shared Loan is active when take acquires text by Move.
writeLine(text) // Allowed: the completed comparison's Loan has ended.
```

```kimi
func same(left: ref/string, right: ref/string) -> bool
    return left == right // UTF-8 contents, not reference addresses.

let text = "hello"
if same(text, text) => writeLine("equal") // Two shared argument Loans may overlap.
writeLine(text) // The call's independent bool result retains neither Loan.
```

Value equality and object identity are separate operations; `==` does not implicitly become an address comparison for object Types. Raw-pointer `== !=` are the explicit exception, following [pointer equality](05-raw-pointers-and-unsafe-memory.md#51-null-and-equality).

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

### 13.4.1. Contract comparison mapping

After the built-in cases in this section, comparison of two operands of the same complete user Type requires the recognized Core Contract below. Resolve its conformance mapping once; do not search same-named free functions, imported extensions, or conversion chains. Generic code requires the corresponding Constraint. Built-in comparisons retain priority and cannot be replaced by a conformance declaration.

| Operators | Required operation | Result |
| --- | --- | --- |
| ==, != | Equatable.equals on shared borrows of both operands | returned bool, or its negation |
| <, <=, >, >= | Comparable.compare on shared borrows of both operands | compare returned i32 with zero |

Comparable refines Equatable: compare’s sign must agree with equality and a total order. Integers, char, and string under `owner` Semantics provide both; bool, Unit, and floats provide Equatable. Floats have built-in relational operators but no Comparable because NaN is unordered. Borrow/Tuple comparisons forward or compose these capabilities. Structs and enums—including payload-free enums—need explicit conformance and members; equality/ordering is not derived. Arithmetic Contracts, user operators, and user-defined arithmetic remain deferred, so arbitrary user-Type arithmetic is an error.

For f32/f64, the intrinsic Equatable.equals mapping uses the NaN-reflexive equality defined for Dictionary in §12.3.4, whereas a built-in `==` expression still returns false for NaN. Generic comparison through an Equatable requirement uses its mapping. Do not specialize such a generic call into a floating `==` instruction that changes its meaning. Borrow/Tuple Equatable conformances compose mappings in the same way, separately from built-in operator semantics.

## 13.5. Explicit operations

Explicit operation selection is distinct from subtyping and acquisition legality under [Type relations and expression operations](03-types-and-values.md#38-type-relations-and-expression-operations). Origin restriction fits the selected operation's result; it does not substitute another operation.

`@` is a built-in explicit value operation. It cannot be overloaded, does not search conversion chains, and applies only to its direct operand. The operation itself calls no user code; ordinary operand evaluation, including calls and getters, still does. It never implicitly boxes, acquires resources, duplicates ownership, or increments reference counts.

```text
Explicit @ Operation
└─ Type / Semantics Adaptation: @Type, @ref, @uniq, ...
```

### 13.5.1. Forms and adaptation targets

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

### 13.5.2. Static selection and inference

Resolve the operation from the explicit designation and operand Type/category, then check access, ownership, Loans, and Origins. Numeric conversion, Identity Acquisition, and pointer casts use ordinary value acquisition. Borrow targets use the Borrow table. Failure never selects a different operation, getter, or overload.

Targets may guide permitted literal/generic inference but cannot change an established operand or result Type. An outer expected Type cannot cancel the selected operation. Preserve normal inference boundaries; do not introduce cyclic inference or candidate-by-candidate retries. Check subsequent result fitting statically.

For numeric adaptation, target guidance is limited to the direct unresolved literal cases in §13.5.4. Do not pass the conversion target as an expected Type into a general operand expression, including arithmetic or a generic call. Infer that operand independently before applying the numeric conversion: `(200 + 100)@u8` computes an i32 value and then fails its conversion range check; `id(300)@u8` likewise does not infer the call's numeric Type from u8.

```kimi
// handler is an overloaded function name; the annotation selects its reference.
let f: (i32) -> () = handler
// Conversion to the common Function Type produces a Non-Copy value.
let g = f // Move the common function value; f becomes Moved.
```

Deferred generic effects follow [Generic Access Effects](08-generics-constraints-and-contracts.md#89-generic-access-effects). Resolve effects before finalizing ownership/Loan analysis. `Never` follows ordinary abrupt-completion and Type-fitting rules, not a value conversion. A non-completing operand prevents execution of the outer operation but does not waive syntax, target-Type, or Unsafe checks.

### 13.5.3. Defined adaptations

| Operation | Condition |
| --- | --- |
| Identity Acquisition | Same normalized complete Type; ordinary acquisition is permitted |
| Numeric Conversion | Integer/float values under `owner` Semantics in the numeric table below |
| Borrow / Reborrow | The explicit Borrow table below |
| Object Upcast | The finite [object upcast tables](#1357-object-upcasts), including their specified borrow forms |
| Raw Pointer Conversion | The [pointer conversion rules](05-raw-pointers-and-unsafe-memory.md#54-pointer-conversions) |
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

### 13.5.4. Numeric conversions and literals

| Source -> target | Rule |
| --- | --- |
| Integer -> integer | Check the target range; no truncation or wrapping |
| Integer -> float | Round to nearest, ties to even |
| Float -> float | Same rounding; preserve NaN and infinity |
| Float -> integer | Truncate toward zero, then check the mathematical integer's range |

A finite value rounding to infinity fails. Rounding to a subnormal or zero is allowed. NaN payload preservation is not guaranteed. NaN and infinity cannot convert to integers.

Float-to-float conversion preserves signed zero, including the sign of a nonzero value rounded to zero. Integer zero converts to positive floating zero; either floating zero converts to integer zero. Floating rounding uses roundTiesToEven, with gradual underflow. The initial Windows profile requires the ABI-standard FP environment at entry and across foreign calls (§21.5.4); foreign code that violates this contract is outside the supported boundary, and the runtime does not repair its environment. These rules also apply to literal conversion.

For direct unresolved literals, `@` performs literal fitting in these cases: integer literals with integer targets must fit the target range; floating literals with `f32`/`f64` targets follow [single-rounding rules](02-source-and-lexical-structure.md#26-number-literals); integer literals with `@f32`/`@f64` round once from the exact integer value, without an intermediate default Type. Failure to fit, including floating overflow to infinity, is a compile-time error, not a runtime numeric-conversion failure. Parentheses alone and a direct sign preserve this treatment; the language's literal representation limits still apply.

Floating literals with integer targets first get their ordinary floating source Type, then undergo truncation and range checking. Typed values and general arithmetic expressions use ordinary numeric conversion. Explicit literal adaptation does not widen implicit argument fitting or overload candidate comparison.

The direct-literal category unwraps surrounding parentheses and then permits one unary sign directly attached to the number literal (§12.3.4). Thus `(-128)@i8` and `((-128))@i8` fit the signed literal, while `-(128)@i8` converts an ordinary negation result; `-(128)@u8` fails at runtime. A completed adaptation such as `1@u8` is a typed expression for subsequent argument fitting and overload comparison.

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

### 13.5.5. Explicit borrow and reborrow

In value Borrow/Reborrow rows, `T` is the same normalized immediate Referent Type, which may itself have Semantics. In object rows it is the same View Target; changing that target uses the upcast tables below. Every row requires valid initialization, access, Loans, and Origins. These are explicit adaptations; do not add rows to implicit [argument fitting](10-overload-resolution-and-inference.md#102-argument-adaptation-and-literals) solely because they appear here.

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

Do not upgrade shared to exclusive, derive exclusive object borrows from `rc`/`arc`, or convert between value-borrow and object-borrow representations. A runtime reference count of one does not grant an exception. Temporaries under `owner` Semantics use [materialization and temporary borrowing](03-types-and-values.md#36-temporary-values-places-and-lifetimes).

Custom/computed/required get invokes a getter once. Adapt its declared result Type, including shorthand inference. Borrowing an owned result materializes its temporary and obeys §11.2.3; references retain ordinary Copy/Reborrow rules. Set access grants no hidden-storage borrow. Standard get uses §11.1 Place permissions.

```kimi
// Assume item is computed and its getter returns ref/Resource.
let view = holder.item@ref // Copy that reference.
// holder.item@uniq       // Error if get returns ref/Resource.
inspect(person.age@ref)    // Borrow the getter's Copy result temporary.
```

### 13.5.6. Evaluation, results, and failure

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

Literal fitting is static; other constant evaluation follows [§17.3.4](17-failure-handling.md#1734-checks-builds-and-constant-evaluation).

```kimi
let x: i32 = 300
x@u8 // Abort when evaluated, even when propagation knows x is 300.
```

Abort and cleanup follow ordinary failure/lifetime rules; completed Moves and effects are not rolled back, and no lifetime extension occurs.

### 13.5.7. Object upcasts

Let `S` be the source's static Core View Target and `V` a different target. An upcast requires static proof of `Supports(S, V)` that remains valid for every more-derived Dynamic Type. Concrete Core support follows the base graph. Static conformance alone does not establish this persistence: [inherited conformance](08-generics-constraints-and-contracts.md#844-conformance) is conditional on matching, and the [runtime Contract extension](08-generics-constraints-and-contracts.md#85-runtime-contracts) must guarantee persistent Supports for its Views. Validate [payload erasure](15-ownership-and-lifetime-analysis.md#1581-object-payload-erasure), initialization, access, Origins, and Loans in every row.

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

### 13.5.8. Object ownership creation and sharing

These public Core intrinsics use ordinary inference and argument labels value/build. T is a valid concrete object payload Core; S is a valid complete rc/arc handle Type. Eligibility is an intrinsic formation rule, not a new user Contract, and does not expand the current object/runtime-Contract boundary. Same-named user functions gain no intrinsic behavior.

| API | Input -> result | Contract |
| --- | --- | --- |
| `Core.makeObj<T>(value)` | T -> obj/T | Store a complete value in a new exclusive object |
| `Core.makeRc<T>(value)` | T -> rc/T | Create non-atomic strong ownership, initially one |
| `Core.makeArc<T>(value)` | T -> arc/T | Same with atomic counting |
| `Core.clone<S>(value)` | ref/S -> S | Retain one strong for the same object/view/mode |
| `Core.makeRcCyclic<T,F>(build)` | F -> rc/T | Cyclic construction below |
| `Core.makeArcCyclic<T,F>(build)` | F -> arc/T | Corresponding arc construction |

Normal creation acquires the complete input once by ordinary Copy/Move, allocates object storage, and Moves T into the payload without transferring ownership of its original storage or repeating constructors/accessors/deinit. Publish the initial exact-T view only after metadata and payload initialization. No blanket Owned constraint applies to concrete payload creation; preserve normal external dependencies. View erasure separately requires the existing Owned proof.

Strong clone shared-borrows its input for the operation, leaves it Initialized, and returns independent responsibility without allocation, payload copying, user-code calls or view changes. Preserve the full View Type, Dynamic Type and payload dependencies, without a lasting Loan on the input handle slot. Move/object borrowing never changes counts. rc/arc provides shared payload access even when the count is one. These operations introduce no general deep clone, obj duplication, rc/arc conversion or ownership creation from a borrow.

Objects follow Building -> Alive -> Destroying -> Freed. Building exposes no ordinary strong/payload access. Publish strong = 1 once; the last live strong changes one to zero and begins irreversible destruction. Destroy the complete Dynamic Type, then free the original allocation (§21.2.3). A surviving weak table does not keep the object alive. obj has the corresponding construction/destruction boundary without a strong count.

**Cyclic construction.** The rc factory requires T is Owned and F is `Callable<owner, (Weak<rc/T>) -> T>`; substitute arc for the arc factory. Acquire F normally and call it once with an owned receiver; F itself need not be Copy or Owned. The T constraint is specific to this factory: its dependencies are not yet known when Weak is published to the builder. Inferring them only from F's captures would miss distinct Loans obtained through helpers or mutable static state.

1. Allocate unpublished object storage and a side table holding a construction guard and one builder Weak.
2. Pass that Weak by value to the builder. It may be moved/cloned, but upgrade returns None during Building. Do not expose an uninitialized payload or construction receiver.
3. Wait for the builder's normal result **and call cleanup** to complete, then Move the complete T into the payload.
4. Use the factory's construction authority to publish Alive with the first strong exactly once, then return it. No result may borrow builder storage.

Required allocation failure or increment at the count maximum Aborts before publishing a result; counts never wrap. Builder Abort/nontermination prevents publication and all subsequent work. There is no rollback/cleanup guarantee on Abort. Normal transfer during argument evaluation retains ordinary temporary cleanup. Recoverable creation, allocator choice, raw-storage adoption and strong-cycle collection remain outside this contract.

### 13.5.9. Weak reference operations

S has §3.2.2's eligibility. Each public Core intrinsic evaluates its input once and holds the required shared access during the operation:

| API | Input -> result | Contract |
| --- | --- | --- |
| `Core.downgrade<S>(value)` | ref/S -> `Weak<S>` | Retain a weak responsibility for the same target; do not change strong count |
| `Core.upgrade<S>(value)` | `ref/Weak<S>` -> `Option<S>` | Retain a live strong and return Some, otherwise None |
| `Core.clone<S>(value)` | `ref/Weak<S>` -> `Weak<S>` | Retain another responsibility for the same weak table |

Inputs remain Initialized. downgrade accepts only a completed strong rc/arc handle, not obj, object borrows or raw pointers; its first side table may require allocation. upgrade and clone do not allocate. Their result retains the same object/view/mode. upgrade secures a live strong before reading the table's object pointer; its race with final arc release determines success (§21.2.3). None during Building may precede later publication, whereas failure after final release is permanent. Maximum-count failure Aborts rather than returning None.

| Operation | Dependency/responsibility propagation |
| --- | --- |
| downgrade | Retain payload external dependencies, not a lasting Loan on the source handle slot |
| Weak clone | Retain the same dependencies; do not create an exclusive Loan anchor |
| Move | Transfer responsibility and dependencies, without a count change |
| Successful upgrade | Retain target dependencies in S, not an operation-only borrow of the Weak slot |
| Weak destruction | Release one weak responsibility; touch the management area only, never the payload or strong count |

Preserve full-Type Origins and actual Loan provenance through generic calls, storage and all these operations. Expected runtime expiration cannot erase dependencies potentially required by later upgrade/use. OwnedOrigins scans full S in every state. Normal creation, downgrade and storage impose no blanket Owned requirement; erased storage uses its existing proof. The weak guard survives through object free; final weak release frees only the table. Failure follows §13.5.8.

```kimi
struct Item
    let value: i32
    public init(value: i32)
        self.value = value

func expired() -> Weak<rc/Item>
    let strong = Core.makeRc(Item.init(7))
    return Core.downgrade(strong@ref)

let weak = expired() // Present Weak; the local strong has been destroyed.
let absent: Option<Weak<rc/Item>> = .None // Absence is a different value.
let result = Core.upgrade(weak) // None; no resurrection.
let other = Core.clone(weak) // Shares the table, not the payload.
let moved = other // Move, with no count increment.
```

```kimi
struct Node
    public let selfWeak: Weak<rc/Node>
    public init(selfWeak: Weak<rc/Node>)
        self.selfWeak = selfWeak

let build = func [] (weak: Weak<rc/Node>) -> Node
    let before = Core.upgrade(weak) // None while Building.
    return Node.init(weak)
let node = Core.makeRcCyclic(build)
let after = Core.upgrade(node.selfWeak@ref) // Some after publication.
```

A stored Weak to a payload retaining a local borrow cannot escape that borrow's lifetime merely because the strong is expected to expire. Upgrade is required before payload access or view operations. No liveness-only API, implicit duplication, direct Weak view conversion, unsafe weak pointer, unowned reference or cycle collection is introduced. Any future Weak upcast must define Move/count effects; pointer equality grants no conversion. Atomic arc counting does not authorize source concurrency (Appendix D.2).

## 13.6. Runtime type tests and checked casts

### 13.6.1. Runtime is tests

In ordinary expressions, `value is T` and `value is not T` are non-associative comparisons. The right side is one named struct Core, with optional qualification and resolved type arguments, but no Semantics, Origin, binding name, or requirement composition. Expand aliases and check accessibility. Unresolved type parameters/associated Types and non-struct targets are outside this initial syntax.

The left side must have `obj/S`, `rc/S`, `arc/S`, `objref/S`, or `objuniq/S`, with a struct Core `S`. Evaluate it once, then return `Supports(RuntimeObjectType(value), T)` or its negation. Generic identity includes the relevant arguments. This operation itself neither Copies nor Moves nor Consumes the operand, changes counts, nor acquires stronger authority. Getter/call evaluation, required shared access, temporaries, and cleanup retain normal effects.

```kimi
require value is Dog and value.isHealthy() else => return
value.bark()
// value is Dog or Cat parses as (value is Dog) or Cat, not a two-Type test.
```

The syntax context determines `is` before lookup: Constraint Clauses and associated-Type conditions retain their Requirement Test; ordinary initializers, arguments, and runtime conditions use this test. `T is Comparable` in an ordinary expression is not retried in the Type namespace. Parentheses preserve the surrounding context. Compile-time directives allow only environment conditions and reject every `is` test.

Accept well-typed tests even when static information proves them always true or false; a warning is allowed. Do not omit left-side effects or derive additional unreachable paths from that knowledge. Conditional Type information follows [flow refinement](14-control-flow.md#1410-type-refinement). Ordinary owners, value borrows, pointers, numeric values, Tuples, and enum variants are not test subjects. This syntax does not add optional or pattern binding.

### 13.6.2. General view tests and checked casts

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

Check target validity/accessibility, Semantics preservation, [Owned erasure](15-ownership-and-lifetime-analysis.md#1581-object-payload-erasure), result Origins/Loans, and destruction dependencies statically. Borrowing cannot create ownership or exclusivity; share explicitly before casting when needed. Evaluate/secure the source once. For a source certified by Owned payload erasure, a missing fixed Origin binding may be supplied only as static where the §15.2.3 proof covered that binding. Thus a cast to a concrete `Box<ref/i32 from static>` may be valid when Runtime Type Identity/Supports and all other checks match. This is a static proof, not runtime recovery of an Origin. Preserve the handle's outer borrow Origin. Do not invent non-static bindings, bind per-call callable Origins, or rewrite other information excluded from that proof; reject a target needing information not preserved or certified. API names and Option/Result branching syntax remain design boundaries; these guarantees do not define a cast spelling or add checked casts to `@`.

## 13.7. Assignment

### 13.7.1. Simple assignment

`target = value` returns Unit and evaluates in this order:

1. Evaluate the right side fully and secure a temporary of the statically determined destination Type by Copy/Move; update the source state and responsibility.
2. Evaluate the left receiver and access path left to right once. Invoke intermediate getters needed to locate the target, but not the getter of the final target.
3. Destroy the old value or initialized parts still present at that destination. Skip Moved/Uninitialized parts.
4. Place the temporary by ordinary Copy/Move rules, transferring its responsibility as applicable and leaving the destination Initialized.

For custom/computed/required set, pass the secured input to its setter instead of steps 3–4. Standard set directly places storage under §11.1 permissions and may restore incomplete storage. Constructor first placement follows §11.3.1. A destination rooted in a getter-owned temporary is restricted by §11.2.3.

Replacement uses existing storage without invoking incoming constructors or declaration initializers. Clean complete old values by their exact Type’s full chain; clean incomplete ones under [partial cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup). A derived value’s base view is not a whole-value target. Check destination/ancestor permissions and Loans first. If old cleanup does not complete normally, install nothing; neither restore the old state nor continue with an observable empty destination.

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

### 13.7.2. Compound assignment

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

## 13.8. Extension boundaries and reserved syntax

Operator symbols, precedence, and associativity are fixed by the language. User-defined comparison uses the [Core Contract mapping](#1341-contract-comparison-mapping). User-defined arithmetic remains deferred and unavailable; a same-named method does not authorize an operator. Future arithmetic must preserve evaluation order and counts and assignment's Unit result.

`and`, `or`, `not`, `=`, `@`, `is`, Ranges, and control transfers cannot be reinterpreted by user code. Custom operator symbols and precedence declarations are not defined. Neither are `!`, `&&`, `||`, `~`, `**`, `??`, `?.`, or ternary `?:`; use logical keywords and `if`. Unary `&` is not a borrow operation; use `@ref` / `@uniq`. Prefix `move`, a Move accessor, and a dedicated `<-` Move operator are not defined. Recognition by the lexer alone does not make a token a usable operator.

`#Name` is an Attribute and `#if` / `#switch` are compile-time directives, not runtime unary operators. The **Composition Root** is the reserved root for language-provided operations selected by `$`, independently of ordinary Name lookup; it is neither a value nor a macro facility. This revision defines only `$abort(...)`, under [Abort Termination](17-failure-handling.md#173-abort-termination). Other operations, dependency resolution, and extension/lifetime rules are not defined and confer no source-language permission.
