# 13. Operators and assignment

[Specification index](../SPEC.md)

## 13.1. Precedence and associativity

Earlier rows bind more tightly. Left associativity groups `a op b op c` as `(a op b) op c`; right associativity groups it as `a op (b op c)`. Grouping guarantees neither Type correctness nor a change in evaluation order.

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

In ordinary expressions, `is`/`is not` ends after one named struct Core, and outer `and`/`or` remain Boolean operators. In dedicated compile-time contexts, `is` instead follows the asymmetric [requirement-expression rule](08-generics-constraints-and-contracts.md#83-requirement-expressions). Prefix `not` keeps its precedence, so a runtime test is negated as `value is not Dog` or `not (value is Dog)`. `as` is reserved.

Unparenthesized comparison chains such as `a < b < c`, `a == b == c` and `a < b == flag` are syntax errors; write `a < b and b < c` or `(a < b) == flag`. Each comparison still requires valid operand Types.

`@` is one token. All adaptations share one precedence level and left associativity, below the prefix operators: `-x@i64` means `(-x)@i64`. Targets may contain qualified names, `/` and generic arguments, so an adapted result must be parenthesized before selection, calls or indexing: `(x@T).name`, `(f@T)()`, `(a@T)[0]`. Use `(x@Number) / divisor` for division after adaptation.

Conversion Type arguments follow the same adjacent-`<` and matching-`>` rule as generic application: `value@Box<i32>` contains a Type argument, whereas `value@i64 < limit` compares the converted value. Selections, iterations and do expressions have their own body syntax. `return`, `exit` and `yield` consume a full result expression, so `return a + b` returns the sum.

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
| `+value` | Numeric value; unchanged Type and value. |
| `-value` | Negated signed integer or floating-point value. |
| `not value` | Negated `bool`. |
| `*pointer` | Raw-pointer Place under the [unsafe dereference rules](05-raw-pointers-and-unsafe-memory.md#52-dereference-and-ownership). |
| `^value` | From-end `Index` formed from a nonnegative `isize`. |
| `++target` / `--target` | Increments or decrements an integer and returns the updated value. |
| `target++` / `target--` | Increments or decrements an integer and returns the old value. |

Increment and decrement require a readable, writable integer Place or Property; they do not apply to floats, raw pointers or other Types. The target is resolved, read and written once each, and overflow prevents the write. A prefix operation returns its computed value without reading the Property again. These operations follow the target-validity and ownership requirements of [compound assignment](#1372-compound-assignment).

```kimi
var count: i32 = 1
let before = count++  // before = 1, count = 2
let after = ++count   // after = 3, count = 3
```

`not` binds more tightly than comparison; negate a comparison as `not (a == b)`. This operator defines no dereference of non-pointer Types.

## 13.3. Arithmetic, bitwise, and shift operators

`+`, `-`, `*` and `/` take operands of the same numeric Type and return that Type; `%` accepts integers only. Integer division truncates toward zero. On mathematical integers the remainder satisfies `a = (a / b) * b + a % b`, and a nonzero remainder has the dividend's sign.

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010
let shifted = bits << 1     // 0b10100
```

Integer `+`, `-`, `*`, unary `-`, increment and decrement, and the arithmetic part of compound assignment are checked for overflow. Integer division or remainder by zero is invalid, as is the signed minimum divided by `-1`, including `% -1`. These failures follow [Abort Termination](17-failure-handling.md#173-abort-termination), including its constant-evaluation rule.

`&`, `|` and `^` perform bitwise AND, OR and XOR on operands of the same integer Type; they do not accept `bool`. `<<` and `>>` return the left operand's integer Type and accept any integer Type on the right, requiring `0 <= shift < bit width of the left operand`; an invalid count is a check failure. Left shift discards high bits and inserts zero low bits; right shift sign-extends signed integers and zero-extends unsigned integers. Discarded shift bits are not arithmetic overflow.

Floating-point operations follow IEEE 754 for `f32`/`f64`, rounding to nearest with ties to even. They support infinities, NaN and signed zero, and floating-point division by zero does not use the integer failure rules. Ordinary operations are not implicitly reassociated or fused when rounding or NaN results would change.

**String concatenation.** `string + string` denotes concatenation, without implicit numeric stringification. Its operand acquisition and ownership rules, including those of string `+=`, are deferred to a common operator model. That design must specify Copy/Move or borrowing, Loan duration, result ownership, aliasing and self-update, and failure behavior, preserving the evaluation and write order of §13.7; no particular acquisition strategy is adopted here. These operations cannot pass executable finalization until those rules are defined and implemented; parsing or Type checking alone grants no ownership permission.

Raw-pointer arithmetic is limited to the forms and unsafe conditions of [pointer arithmetic](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing); its undefined-behavior rules are distinct from checked integer arithmetic.

## 13.4. Comparison and logical operators

`==`, `!=`, `<`, `<=`, `>` and `>=` return `bool`. Numeric operands must have the same Type. `bool` and Unit support equality only. `char` compares Unicode scalar values. `string` uses UTF-8 byte equality and lexicographic byte order, without normalization or locale processing.

For floating-point values, `+0.0 == -0.0` is true. With a NaN operand, `==`, `<`, `<=`, `>` and `>=` are false and `!=` is true; floating-point ordering is not total.

Comparisons may borrow their operands and never Move Non-Copy owned values solely to compare them. User-defined comparison requires an explicit Type capability (§13.4.1). Safe borrows compare referent values of the same Type using that Type's comparison capability. Tuples support elementwise equality and lexicographic ordering when all corresponding elements support the required comparison.

Built-in comparisons do not implicitly adapt an owned operand to match a safe-borrow operand: compare two owned values, or two safe borrows with matching immediate Referent Types. The outer borrow Origins need not be identical, but each operand must remain valid through the comparison.

**Inspection Loans.** A built-in comparison that inspects a Non-Copy owned Place forms an implicit shared Loan when that operand is evaluated. Operands are evaluated left to right, so the left operand's Loan begins before the right operand is evaluated and lasts through the comparison. Both inspections require Initialized values. The normal [Loan conflict rules](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts) apply throughout operand evaluation: a later operand cannot Move, replace, destroy or exclusively borrow the earlier borrowed Place. Optimization cannot change this acceptance rule.

On normal completion, comparison-only Loans end after the comparison, and the `bool` result keeps no operand Loan. They also end if a control transfer abandons the comparison. Existing Loans keep their own lifetimes, and temporary operands keep their [normal temporary lifetimes](03-types-and-values.md#362-lifetime-and-borrowing); ending an inspection Loan does not destroy a temporary early. Abort follows §17.3.

```kimi
func take(text: string) -> string => text

let text = "a"
let same = text == "a" // Shared inspection; text is not Moved.
// let invalid = text == take(text)
// Error: the left operand's shared Loan is active when take acquires text by Move.
Console.writeLine(text) // Allowed: the completed comparison's Loan has ended.
```

```kimi
func same(left: ref/string, right: ref/string) -> bool
    return left == right // UTF-8 contents, not reference addresses.

let text = "hello"
if same(text, text) => Console.writeLine("equal") // Two shared argument Loans may overlap.
Console.writeLine(text) // The call's independent bool result retains neither Loan.
```

Value equality and object identity are separate operations: `==` never implicitly becomes an address comparison for object Types. Raw-pointer `==` and `!=` are the explicit exception, following [pointer equality](05-raw-pointers-and-unsafe-memory.md#51-null-and-equality).

| Logical operation | Evaluation |
| --- | --- |
| `left and right` | If `left` is false, the result is false; otherwise `right` is evaluated. |
| `left or right` | If `left` is true, the result is true; otherwise `right` is evaluated. |
| `not value` | Reverses true and false. |

All logical operands and results are `bool`, and user code cannot change short-circuit behavior.

```kimi
let valid = index >= 0 and index < count
let found = valid and matches(values[index])
let clear = flags & mask == 0
```

### 13.4.1. Contract comparison mapping

Beyond the built-in cases above, comparing two operands of the same complete user Type requires the recognized Kimi Contract below. Its conformance mapping is resolved once; same-named free functions, imported extensions and conversion chains are never searched. Generic code requires the corresponding Constraint. Built-in comparisons keep priority and cannot be replaced by a conformance declaration.

| Operators | Required operation | Result |
| --- | --- | --- |
| `==`, `!=` | `Equatable.equals` on shared borrows of both operands | The returned `bool`, or its negation |
| `<`, `<=`, `>`, `>=` | `Comparable.compare` on shared borrows of both operands | The returned `i32` compared with zero |

`Comparable` refines `Equatable`: the sign of `compare` must agree with equality and with a total order. Integers, `char` and `string` under `owner` Semantics provide both; `bool`, Unit and floats provide Equatable only. Floats have built-in relational operators but no Comparable, because NaN is unordered. Borrow and Tuple comparisons forward or compose these capabilities. Structs and enums, including payload-free enums, need explicit conformance and members; equality and ordering are never derived. Arithmetic Contracts, user operators and user-defined arithmetic remain deferred, so arithmetic on arbitrary user Types is an error.

For `f32`/`f64`, the intrinsic `Equatable.equals` mapping uses the NaN-reflexive equality defined in §12.3.4, whereas a built-in `==` expression still returns false for NaN. Generic comparison through an Equatable requirement uses the mapping, and such a generic call must not be specialized into a floating `==` instruction that changes its meaning. Borrow and Tuple Equatable conformances compose mappings the same way, separately from the built-in operator semantics.

## 13.5. Explicit operations

Explicit operation selection is distinct from subtyping and acquisition legality under [Type relations and expression operations](03-types-and-values.md#38-type-relations-and-expression-operations). Origin restriction fits the selected operation's result; it never substitutes another operation.

`@` is a built-in explicit value operation. It cannot be overloaded, searches no conversion chains, and applies only to its direct operand. The operation itself calls no user code, although ordinary operand evaluation, including calls and getters, still may. It never implicitly boxes, acquires resources, duplicates ownership or increments reference counts.

```text
Explicit @ Operation
└─ Type / Semantics Adaptation: @Type, @ref, @uniq, ...
```

### 13.5.1. Forms and adaptation targets

| Form | Meaning |
| --- | --- |
| `E@Type` | A defined adaptation to the specified target |
| `E@Semantics` | The same, with the Core, immediate Referent Type or object View Target taken from the operand as applicable |

An **Adaptation Target** specifies Semantics and a Core, a complete inner Type for a value-borrow or pointer layer, or an object View Target. The result Origins are inferred from the operand, the operation, Loans and applicable constraints to obtain the complete result Type. Origin information in aliases, generic Types and operands is kept; constraints are never erased and validity is never extended. Adaptation and runtime `is` targets contain no written Origin list at any layer; `exit to Label: value` belongs to control-transfer syntax.

```text
Adaptation Target
├─ Core / immediate Referent Type / object View Target: specified, or taken{the} operand
├─ Semantics: determined by Type, alias, or explicit Semantics
└─ Origin: inferred during adaptation
    -> complete result Type retains target, Semantics, and Origin
```

**Syntactic extent.** After `@`, an identifier-shaped head followed by a slash is consumed as a Semantics prefix, recursively and regardless of whitespace; no lookup is needed for this decision. Each prefix must later resolve to a concrete Semantics or a declared Semantics binding. The remaining primitive, named, qualified, generic, grouped, Tuple or fixed-array Type head is consumed as the target. Generic adjacency follows §12.4.2, and written direct borrow annotations are forbidden at every target layer; named aggregate occurrences may introduce binding sets governed by the enclosing declaration's relations (§15.4.4). A following slash is division only after that head is complete and cannot begin another Semantics prefix.

`a@ref/uniq/T` consumes the full prefix chain. `a@T / b` parses the target `T/b` and fails Semantics lookup if `T` is only a Core; whitespace cannot change this. Write `(a@T) / b` or `a@(T) / b` for division. Primitive keywords cannot be Semantics parameters, so `x@i32 / y` already means `(x@i32) / y`. Grouping, as in `x@(i32)`, preserves the adaptation. Group a complete Function Type target, as in `x@((i32) -> i32)`; adaptation does not consume a following outer arrow. Parsing commits before Binding and is never retried after a conversion failure.

For a single-layer value Type with Core `T`, `@ref` and `@ref/T` select the same Borrow/Reborrow operation when applicable. For a nested borrow, shorthand keeps the immediate Referent Type: applying `@ref` to `ref/ref/T` copies that outer shared reference. It adds no layer and never becomes `@objref`. A fully specified target can instead request a borrow of reference-value storage under [Borrow and Reborrow](#1355-explicit-borrow-and-reborrow). Type names, aliases, generic applications, grouping and Tuple syntax are accepted as target syntax without implying that every adaptation is defined.

```kimi
let wide = number@i64
let view = value@ref
let sameView = value@ref/Value // When value's Core is Value.
let taken = value // Copy if Copy, otherwise Move.
```

**Bare target names.** For a bare Name `X` in `E@X`, built-in Semantics names are recognized first. Otherwise, **Adaptation Target Lookup** runs independently for the Type role and the generic Semantics-parameter role, using the ordinary lookup stages, visibility and aliases. Type candidates include Type aliases and generic Type bindings, subject to the target's role restrictions. Each role commits its first eligible stage, and paths to the same Symbol are deduplicated.

| Lookup result | Outcome |
| --- | --- |
| Type only | `@Type` |
| Semantics parameter only | `@Semantics` shorthand |
| Both roles, or ambiguity within a role | Ambiguity error |
| Neither | Ordinary wrong-role, inaccessible or undefined-Name diagnostic |

Operand Types, expected Types and conversion success cannot resolve a role ambiguity or reopen outer lookup. Qualified names and constructed Types use normal Type syntax; `@s/T` gives `s` the Semantics role and `T` the Core role, extended to the View Target role when `s` is an object Semantics. Qualification or an explicit Semantics/Core form may disambiguate a bare name.

The extended Container path syntax (§9.6.1) neither relaxes the Origin restrictions on Adaptation Targets nor permits groups or Contracts as value Types, and bound Container qualifiers are unavailable here because their Origin argument list is mandatory. Other grouping and selector forms keep their existing rules.

### 13.5.2. Static selection and inference

The operation is resolved from the explicit designation and the operand's Type and category; access, ownership, Loans and Origins are checked afterward. Numeric conversion, Identity Acquisition and pointer casts use ordinary value acquisition, and Borrow targets use the Borrow table. A failure never selects a different operation, getter or overload.

Targets may guide permitted literal and generic inference but cannot change an established operand or result Type, and an outer expected Type cannot cancel the selected operation. The normal inference boundaries apply: no cyclic inference and no candidate-by-candidate retries. Subsequent result fitting is checked statically.

For numeric adaptation, target guidance is limited to the direct unresolved literal cases of §13.5.4. The conversion target is never passed as an expected Type into a general operand expression, including arithmetic or a generic call; the operand is inferred independently before the numeric conversion. Thus `(200 + 100)@u8` computes an `i32` value and then fails its conversion range check, and `id(300)@u8` does not infer the call's numeric Type from `u8`.

```kimi
// handler is an overloaded function name; the annotation selects its reference.
let f: (i32) -> () = handler
// Conversion to the common Function Type produces a Non-Copy value.
let g = f // Move the common function value; f becomes Moved.
```

Deferred generic effects follow [generic access effects](08-generics-constraints-and-contracts.md#89-generic-access-effects) and are resolved before ownership and Loan analysis is finalized. `Never` follows the ordinary abrupt-completion and Type-fitting rules; it is not a value conversion. A non-completing operand prevents execution of the outer operation but does not waive syntax, target-Type or Unsafe checks.

### 13.5.3. Defined adaptations

| Operation | Condition |
| --- | --- |
| Identity Acquisition | Same normalized complete Type; ordinary acquisition is permitted |
| Numeric Conversion | Integer and floating values under `owner` Semantics, per the numeric table in §13.5.4 |
| Borrow / Reborrow | The explicit Borrow tables of §13.5.5 |
| Object Upcast | The finite [object upcast table](#1357-object-upcasts), including its specified borrow forms |
| Raw Pointer Conversion | The [pointer conversion rules](05-raw-pointers-and-unsafe-memory.md#54-pointer-conversions) |
| Typed Null Formation | Contextually typing `null` as a raw pointer; no Unsafe context required |

**Identity Acquisition** copies a Copy Type and otherwise Moves. Borrow targets take precedence, including a same-Type exclusive Reborrow. Same-Type raw pointer acquisition is an ordinary Copy and needs no Unsafe context for the operation itself. `@owner`, `@obj`, `@rc` and `@arc` allow same-Semantics acquisition only; they neither perform [ownership creation or strong-owner duplication](#1358-object-ownership-creation-and-sharing) nor convert between ownership representations.

```kimi
number@owner   // Copy if number is Copy.
resource@owner // Ordinary Move if resource is a non-Copy owned value.
```

**Origin Restriction** is common static result fitting, not another value operation. The acquisition or Borrow and its effect are determined first; then only the shortening permitted by the variance and outlives rules is applied. Identity Acquisition is checked before this use-site restriction. Core, Semantics, dependencies and Loans are preserved; no Copy, Move or Borrow is added, lifetimes are not extended, and arbitrary nested Origins are not rewritten. For example, fitting `ref{longer}/T` to `ref{shorter}/T` requires `longer` to outlive `shorter`. An exclusive same-Type adaptation still uses Reborrow.

A target that changes both Core and Semantics must be one defined operation; no hidden convert-then-borrow sequence is inserted:

```kimi
// number is i32.
// number@ref/i64 // Error: numeric conversion and Borrow are separate operations.
inspect(number@i64@ref)
```

There is no elementwise Tuple or array conversion, structural struct conversion, checked dynamic cast through `@`, string parsing, numeric conversion involving `bool` or `char`, arbitrary bit reinterpretation or user-defined conversion; same-Type acquisition of these Types remains possible. Safe references are never implicitly dereferenced to convert or extract their owned referents. Conversions between raw pointers and safe references, and ownership acquisition from raw storage, are not specified in this revision (§5.6). `as` remains reserved; it is not an alias of `@`.

### 13.5.4. Numeric conversions and literals

| Source -> target | Rule |
| --- | --- |
| Integer -> integer | Check the target range; no truncation or wrapping |
| Integer -> float | Round to nearest, ties to even |
| Float -> float | Same rounding; preserves NaN and infinities |
| Float -> integer | Truncate toward zero, then check the mathematical integer's range |

A finite value that rounds to infinity fails; rounding to a subnormal or zero is allowed. NaN payload preservation is not guaranteed. NaN and infinities cannot be converted to integers.

Float-to-float conversion preserves signed zero, including the sign of a nonzero value rounded to zero. Integer zero converts to positive floating zero, and either floating zero converts to integer zero. Floating rounding uses roundTiesToEven, with gradual underflow. The initial Windows profile requires the ABI-standard floating-point environment at entry and across foreign calls (§21.5.4); foreign code that violates this contract is outside the supported boundary, and the runtime does not repair its environment. These rules also apply to literal conversion.

**Direct literals.** For a direct unresolved literal, `@` performs literal fitting:

- an integer literal with an integer target must fit the target range;
- a floating literal with an `f32`/`f64` target follows the [single-rounding rule](02-source-and-lexical-structure.md#26-number-literals);
- an integer literal with `@f32`/`@f64` rounds once from the exact integer value, without an intermediate default Type.

A failure to fit, including floating overflow to infinity, is a compile-time error, not a runtime numeric-conversion failure. The language's literal representation limits still apply. A floating literal with an integer target first gets its ordinary floating source Type and then undergoes truncation and range checking. Typed values and general arithmetic expressions use ordinary numeric conversion. Explicit literal adaptation does not widen implicit argument fitting or overload candidate comparison.

The direct-literal category unwraps surrounding parentheses and then permits one unary sign directly attached to the number literal (§12.3.1). Thus `(-128)@i8` and `((-128))@i8` fit the signed literal, while `-(128)@i8` converts the result of an ordinary negation, and `-(128)@u8` fails at runtime. A completed adaptation such as `1@u8` is a typed expression for subsequent argument fitting and overload comparison.

```kimi
let minimum = -128@i8
let large = 5000000000@f64 // No intermediate i32 range check.
let single = 1@f32
let negativeZero = -0.0@f32
let truncated = 3.9@i32  // 3
// 256@u8 // Error: direct literal does not fit.
// 300@i8 // Compile-time error; a typed i32 value 300 converted to i8 instead Aborts.
```

Rounding and checks apply at every `@` in a chain; an intermediate result is never removed if its rounding or failure would change.

### 13.5.5. Explicit borrow and reborrow

#### 13.5.5.1. Complete object payload projection

A fully specified `@ref/T` or `@uniq/T` may project a complete object payload when `T is Kimi.Sealed` is Proven (§8.4.7.1). The source View Target and the immediate result Referent Type must be exactly the same complete `T`, including generic arguments and internal Origins; only the outer borrow lifetime may shorten. The target contains no written direct borrow annotation; the result's Origin is inferred from the source and keeps both the referent and the owning-handle dependencies.

| Input | Explicit target | Result |
| --- | --- | --- |
| `objref/T` or `objuniq/T` | `@ref/T` | Shared child `ref/T` |
| Readable `obj/T`, `rc/T` or `arc/T` Place | `@ref/T` | Shared payload `ref/T` |
| `objuniq/T` | `@uniq/T` | Exclusive child `uniq/T` |
| Exclusively writable `obj/T` Place | `@uniq/T` | Exclusive payload `uniq/T` |

Ordinary initialization, access, reborrow, Loan and Origin checks apply. Shared projections may coexist; conflicting parent access, moving or releasing the owner, and replacing the handle remain forbidden while a dependent projection is live. No ownership transfer or reference-count operation occurs. Neither `rc` nor `arc` grants exclusive access, even with one strong reference.

The effective static Type and declared constraints are used; an inventory of derived Types or an optimizer's guess of the Dynamic Type is not Sealed evidence. Open Views, runtime Contract Views and base subobjects do not qualify. No ordinary argument receives an implicit payload projection; only a selected same-complete-Type borrowed receiver may use the implicit path of §12.4.4.

```kimi
func borrowPayload<T>(source: objref/T) -> ref{source}/T
    T is Sealed
    return source@ref/T
func borrowPayloadMut<T>(source: objuniq/T) -> uniq{source}/T
    T is Sealed
    return source@uniq/T

// a and b are writable obj/Cell<i32> Places; Cell is non-open.
Kimi.Intrinsics.swap(a@uniq/Cell<i32>, b@uniq/Cell<i32>)         // Exchange payload contents.
Kimi.Intrinsics.swap(a@uniq/obj/Cell<i32>, b@uniq/obj/Cell<i32>) // Exchange handle values.
```

The shorthand operations of §13.5.5.2 keep their meaning: `@ref`/`@uniq` never select payload projection, and `@uniq/obj/T` borrows handle storage. No reverse conversion from value borrows to object borrows is introduced. A payload projection changes access to the complete payload, not its View Target; it is distinct from an upcast (§13.5.7) and never supplies base-subobject replacement permission.

#### 13.5.5.2. Existing borrow and storage operations

In the value Borrow/Reborrow rows, `T` is the same normalized immediate Referent Type, which may itself have Semantics. In the object rows it is the same View Target; changing the target uses the upcast table (§13.5.7). Every row requires valid initialization, access, Loans and Origins. These are explicit adaptations; appearing here does not add them to implicit [argument fitting](10-overload-resolution-and-inference.md#102-argument-adaptation-and-literals).

| Input | Operation | Result |
| --- | --- | --- |
| Readable owned `T` Place | `@ref` | New `ref/T` |
| Exclusively writable owned `T` Place | `@uniq` | New `uniq/T` |
| `ref/T` | `@ref` | Copy of the shared reference, preserving its Origins |
| `uniq/T` | `@ref` | Shared Reborrow |
| `uniq/T` | `@uniq` | Exclusive Reborrow |
| Readable `obj/T`, `rc/T` or `arc/T` Place | `@objref` | New `objref/T`; no reference-count increment |
| Exclusively writable `obj/T` Place | `@objuniq` | New `objuniq/T` |
| `objref/T` | `@objref` | Copy of the shared object reference |
| `objuniq/T` | `@objref` | Shared object Reborrow |
| `objuniq/T` | `@objuniq` | Exclusive object Reborrow |

**Storage borrows.** A fully specified `ref/V` or `uniq/V` target may borrow a readable or exclusively writable Place of exactly the normalized Type `V`, even when `V` is a reference or object handle; Temporary Values are materialized under the normal lifetime rules. This adds one reference layer and preserves all of `V`'s dependencies. Same-Type Copy or Reborrow and the `uniq/V` to `ref/V` Reborrow take precedence, and a failed Reborrow cannot fall back to storage borrowing. Shorthand `@ref`/`@uniq` adds no layer to an already borrowed value.

```kimi
var number = 1
var reference = number@ref
let same = reference@ref            // ref/i32; preserves number's Origin.
let slot = reference@ref/ref/i32     // ref/ref/i32; also depends on reference's storage.
// reference = other@ref            // Error while slot's Loan is live.
```

For `reference: ref/i32`, `reference@uniq/ref/i32` borrows the writable slot exclusively; it grants no mutable access to `number`. A `let` reference slot cannot be exclusively borrowed this way. `reference@ref@ref` remains `ref/i32`. Direct borrow annotations remain forbidden in an Adaptation Target, including grouped and generic inner Types. Binding-set names are allowed, and any attached relations belong to the enclosing declaration; actual result dependencies are inferred or kept from existing Types.

A new Borrow depends on the target Place and on the owner's validity. Copying a shared reference preserves its referent Origins rather than using the lifetime of the variable holding it. A Reborrow lends referent capability without moving the parent reference; while the child Loan is live, conflicting access through the parent is forbidden. A `let` binding holding an exclusive reference does not by itself prevent Reborrow. Ordinary by-value acquisition transfers a Non-Copy reference itself.

```kimi
var value = makeValue()
let exclusive = value@uniq
inspect(exclusive@ref)
modify(exclusive@uniq) // After the previous child Loan ends.
let transferred = exclusive // Move the reference, not its referent.
```

Shared access is never upgraded to exclusive, exclusive object borrows are never derived from `rc`/`arc`, and value borrows and object borrows are never converted into each other; a runtime reference count of one grants no exception. Temporaries under `owner` Semantics use [materialization and temporary borrowing](03-types-and-values.md#36-temporary-values-places-and-lifetimes).

**Getter results.** A custom, computed or required `get` invokes the getter once and adapts its declared result Type, including for shorthand inference. Borrowing an owned result materializes its temporary and obeys §11.2.3; references keep the ordinary Copy and Reborrow rules. `set` access grants no borrow of hidden storage. Standard `get` uses the Place permissions of §11.1.

```kimi
// Assume item is computed and its getter returns ref/Resource.
let view = holder.item@ref // Copy that reference.
// holder.item@uniq       // Error if get returns ref/Resource.
inspect(person.age@ref)    // Borrow the getter's Copy result temporary.
```

### 13.5.6. Evaluation, results, and failure

Each operand and required receiver is evaluated once, and chained adaptations finish from the inside outward. Each operation uses its own acquisition and permission rules. Acquisition never propagates backward through a call or getter into hidden storage.

Assignment remains right-hand-side-first and returns Unit. A custom setter receives the secured result normally; source Move and destination write permissions are independent. Destroying the old destination value must preserve result Loans and Origins. Borrow/Reborrow protection begins at formation. Eligible exclusive receiver and argument borrows, including direct explicit adaptations, follow [call reservation and activation](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations); other exclusive borrows are immediately active.

```kimi
var x = makeResource() // Non-Copy.
x = x // Move and reinitialize; skip destruction of the Moved old value.
// f(x, x) is invalid if its first argument consumes x and its second reads x.
```

A discarded result is still evaluated and its acquisition or conversion checked; the unused owned result is destroyed at its normal lifetime. Transfers secure results before cleanup. Returned borrows must survive cleanup, and deferred uses of Moved or incomplete values are errors.

| Failure | Handling |
| --- | --- |
| Undefined adaptation; Type, access, initialization, Loan or Origin violation | Compile-time error |
| Literal fitting or required constant conversion failure | Compile-time error |
| Failed runtime numeric conversion | Abort if evaluated |
| Unsafe memory contract violation | Ordinary Unsafe rules |

Literal fitting is static; other constant evaluation follows [§17.3.4](17-failure-handling.md#1734-checks-builds-and-constant-evaluation).

```kimi
let x: i32 = 300
x@u8 // Abort when evaluated, even when propagation knows x is 300.
```

Abort and cleanup follow the ordinary failure and lifetime rules: completed Moves and effects are not rolled back, and no lifetime is extended.

### 13.5.7. Object upcasts

Let `S` be the source's static Core View Target and `V` a different target. An upcast requires a static proof of `Supports(S, V)` that remains valid for every more-derived Dynamic Type. Concrete Core support follows the base graph. Static conformance alone does not establish this persistence: [inherited conformance](08-generics-constraints-and-contracts.md#844-conformance) depends on matching, and the [runtime Contract extension](08-generics-constraints-and-contracts.md#85-runtime-contracts) must guarantee persistent Supports for its Views. [Payload erasure](15-ownership-and-lifetime-analysis.md#1581-object-payload-erasure), initialization, access, Origins and Loans are validated in every row.

| Source | Explicit operation | Acquisition and result |
| --- | --- | --- |
| `obj/S`, `rc/S`, `arc/S` | `@obj/V`, `@rc/V`, `@arc/V` respectively | Move of the same owning handle; no count change |
| `objref/S` | `@objref/V` | Copy of the shared reference with a changed view |
| `objuniq/S` | `@objuniq/V` | Exclusive child Reborrow with a changed view |
| Shared-borrowable `obj/S`, `rc/S` or `arc/S` Place | `@objref/V` | Shared object Borrow; the owner is unchanged |
| Exclusively writable `obj/S` Place | `@objuniq/V` | Exclusive object Borrow |
| `objuniq/S` | `@objref/V` | Shared child Reborrow with a changed view |

Each row is one defined operation, evaluated once, not a search for convert-then-borrow chains. The entire object, its identity, Dynamic Type and cleanup responsibility are preserved. There is no implicit upcast from annotations, arguments or returns. A same-target Borrow or Reborrow follows the existing table and does not newly erase a Type. Temporaries keep their original materialization lifetime.

```kimi
let dog: obj/Dog = makeDog()
let animal = dog@obj/Animal // dog is Moved; animal still owns the entire Dog.
let view = animal@objref
let invalid = view@objref/Dog // Error: Animal does not statically imply Dog.
```

A checked cast (§13.6.2) is needed when the source view cannot guarantee the target, including Contract-to-concrete and cross-Contract conversions. No ordinary value-borrow upcast, ownership from a borrow, shared-to-exclusive upgrade, `obj`-to-`rc`/`arc` conversion or container covariance is added. `@ref/V` is not shorthand for `@objref/V`, and `rc`/`arc` supply no exclusive borrows.

### 13.5.8. Object ownership creation and sharing

These public functions belong to `Kimi.Intrinsics` (§22.1.1) and use ordinary inference and the argument labels `value` and `build`. Both labels precede any name-required boundary and permit name omission; every argument value is required. The Weak operations in §13.5.9 likewise permit omission of their `value` label. `T` is a valid concrete object payload Core, and `S` a valid complete `rc`/`arc` handle Type. Eligibility is an intrinsic formation rule, not a user Contract, and does not extend the current object and runtime-Contract boundary. Same-named user functions gain no intrinsic behavior.

| API | Input -> result | Contract |
| --- | --- | --- |
| `Kimi.Intrinsics.makeObj<T>(value)` | `T -> obj/T` | Store a complete value in a new exclusive object |
| `Kimi.Intrinsics.makeRc<T>(value)` | `T -> rc/T` | Create non-atomic strong ownership, initially one |
| `Kimi.Intrinsics.makeArc<T>(value)` | `T -> arc/T` | The same, with atomic counting |
| `Kimi.Intrinsics.clone<S>(value)` | `ref/S -> S` | Retain one more strong reference to the same object, view and mode |
| `Kimi.Intrinsics.makeRcCyclic<T, F>(build)` | `F -> rc/T` | Cyclic construction, below |
| `Kimi.Intrinsics.makeArcCyclic<T, F>(build)` | `F -> arc/T` | The corresponding `arc` construction |

Any valid complete owner Core other than Never may be the concrete payload, including open struct Cores; only projection to ordinary value borrows additionally requires Sealed (§13.5.5.1). Generic signatures must prove the target's validity from their declared constraints (§8.10).

**Normal creation** acquires the complete input once by ordinary Copy or Move, allocates object storage and Moves `T` into the payload, without transferring ownership of the original storage and without repeating constructors, accessors or `deinit`. The initial exact-`T` view is published only after metadata and payload initialization. No blanket Owned constraint applies to concrete payload creation, and normal external dependencies are preserved; view erasure separately requires the existing Owned proof.

**Strong clone** shared-borrows its input for the operation, leaves it Initialized, and returns an independent responsibility without allocation, payload copying, user-code calls or view changes. It preserves the full View Type, Dynamic Type and payload dependencies, without a lasting Loan on the input handle slot. Moving or borrowing an object never changes counts, and `rc`/`arc` provide shared payload access even at count one. These operations introduce no general deep clone, `obj` duplication, `rc`/`arc` conversion or ownership creation from a borrow.

**Lifecycle.** Objects pass through Building, Alive, Destroying and Freed. Building exposes no ordinary strong or payload access. Strong = 1 is published once; the last live strong changes one to zero and begins irreversible destruction, which destroys the complete Dynamic Type and then frees the original allocation (§21.2.3). A surviving weak table does not keep the object alive. `obj` has the corresponding construction and destruction boundary without a strong count.

**Cyclic construction.** The `rc` factory requires `T is Owned` and `F is Callable<owner, (Weak<rc/T>) -> T>`; the `arc` factory substitutes `arc`. `F` is acquired normally and called once with an owned receiver; `F` itself need not be Copy or Owned. The `T` constraint is specific to this factory: `T`'s dependencies are not yet known when the Weak is published to the builder, and inferring them only from `F`'s captures would miss distinct Loans obtained through helpers or mutable static state.

1. Allocate unpublished object storage and a side table holding a construction guard and one builder Weak.
2. Pass that Weak by value to the builder. It may be moved or cloned, but `upgrade` returns `None` during Building. No uninitialized payload or construction receiver is exposed.
3. Wait until the builder's normal result **and the call's cleanup** have completed, then Move the complete `T` into the payload.
4. Using the factory's construction authority, publish Alive with the first strong exactly once, and return it. No result may borrow builder storage.

A required allocation failure, or an increment at the count maximum, Aborts before a result is published; counts never wrap. Abort or nontermination of the builder prevents publication and all subsequent work, with no rollback or cleanup guarantee on Abort. A normal transfer during argument evaluation keeps ordinary temporary cleanup. Recoverable creation, allocator choice, raw-storage adoption and strong-cycle collection are outside this contract.

### 13.5.9. Weak reference operations

`S` has the eligibility of §3.2.2. Each public Kimi intrinsic evaluates its input once and holds the required shared access during the operation:

| API | Input -> result | Contract |
| --- | --- | --- |
| `Kimi.Intrinsics.downgrade<S>(value)` | `ref/S -> Weak<S>` | Retain a weak responsibility for the same target; the strong count is unchanged |
| `Kimi.Intrinsics.upgrade<S>(value)` | `ref/Weak<S> -> Option<S>` | Retain a live strong and return `Some`; otherwise `None` |
| `Kimi.Intrinsics.clone<S>(value)` | `ref/Weak<S> -> Weak<S>` | Retain another responsibility for the same weak table |

Inputs remain Initialized. `downgrade` accepts only a completed strong `rc`/`arc` handle, not `obj`, object borrows or raw pointers, and its first side table may require allocation; `upgrade` and `clone` do not allocate. Results keep the same object, view and mode. `upgrade` secures a live strong before reading the table's object pointer, and its race with a final `arc` release determines success (§21.2.3). `None` during Building may precede later publication, whereas failure after the final release is permanent. A maximum-count failure Aborts rather than returning `None`.

| Operation | Dependency and responsibility propagation |
| --- | --- |
| `downgrade` | Keeps the payload's external dependencies, but no lasting Loan on the source handle slot |
| Weak `clone` | Keeps the same dependencies; creates no exclusive Loan anchor |
| Move | Transfers the responsibility and dependencies, with no count change |
| Successful `upgrade` | Keeps the target's dependencies in `S`, but no operation-only borrow of the Weak slot |
| Weak destruction | Releases one weak responsibility, touching only the management area, never the payload or strong count |

Full-Type Origins and actual Loan provenance are preserved through generic calls, storage and all these operations. Expected runtime expiration cannot erase dependencies that a later `upgrade` or use may need. OwnedOrigins scans the full `S` in every state. Normal creation, `downgrade` and storage impose no blanket Owned requirement; erased storage uses its existing proof. The weak guard survives until the object is freed, and the final weak release frees only the table. Failures follow §13.5.8.

```kimi
struct Item
    let value: i32
    public init(value: i32)
        self.value = value

func expired() -> Weak<rc/Item>
    let strong = Kimi.Intrinsics.makeRc(Item.init(7))
    return Kimi.Intrinsics.downgrade(strong@ref)

let weak = expired() // Present Weak; the local strong has been destroyed.
let absent: Option<Weak<rc/Item>> = .None // Absence is a different value.
let result = Kimi.Intrinsics.upgrade(weak) // None; no resurrection.
let other = Kimi.Intrinsics.clone(weak) // Shares the table, not the payload.
let moved = other // Move, with no count increment.
```

```kimi
struct Node
    public let selfWeak: Weak<rc/Node>
    public init(selfWeak: Weak<rc/Node>)
        self.selfWeak = selfWeak

let build = func [] (weak: Weak<rc/Node>) -> Node
    let before = Kimi.Intrinsics.upgrade(weak) // None while Building.
    return Node.init(weak)
let node = Kimi.Intrinsics.makeRcCyclic(build)
let after = Kimi.Intrinsics.upgrade(node.selfWeak@ref) // Some after publication.
```

A stored Weak to a payload that keeps a local borrow cannot outlive that borrow merely because the strong is expected to expire. `upgrade` is required before payload access or view operations. No liveness-only API, implicit duplication, direct Weak view conversion, unsafe weak pointer, unowned reference or cycle collection is introduced. A future Weak upcast must define its Move and count effects; pointer equality grants no conversion. Atomic `arc` counting does not authorize source concurrency (Appendix D.2).

## 13.6. Runtime type tests and checked casts

### 13.6.1. Runtime is tests

In ordinary expressions, `value is T` and `value is not T` are non-associative comparisons. The right side is one named struct Core, optionally qualified and with resolved Type arguments, but without Semantics, Origin, binding name or requirement composition. Aliases are expanded and accessibility is checked. Unresolved Type parameters, associated Types and non-struct targets are outside this initial syntax.

The left side must have Type `obj/S`, `rc/S`, `arc/S`, `objref/S` or `objuniq/S` with a struct Core `S`. It is evaluated once, and the result is `Supports(RuntimeObjectType(value), T)` or its negation. Generic identity includes the relevant arguments. The test itself neither Copies, Moves nor Consumes the operand, changes no counts, and acquires no stronger authority; getter and call evaluation, required shared access, temporaries and cleanup keep their normal effects.

```kimi
require value is Dog and value.isHealthy() else => return
value.bark()
// value is Dog or Cat parses as (value is Dog) or Cat, not a two-Type test.
```

The syntax context determines the meaning of `is` before lookup: Constraint Clauses and associated-Type conditions keep the Requirement Test, while ordinary initializers, arguments and runtime conditions use this test. `T is Comparable` in an ordinary expression is not retried in the Type namespace, and parentheses preserve the surrounding context. Compile-time directives allow only environment conditions and reject every `is` test.

Well-typed tests are accepted even when static information proves them always true or always false; a warning is allowed. The left side's effects are not omitted, and no additional unreachable paths are derived from that knowledge. Conditional Type information follows [flow refinement](14-control-flow.md#1410-type-refinement). Ordinary owners, value borrows, pointers, numeric values, Tuples and enum variants are not test subjects. This syntax adds no optional or Pattern binding.

### 13.6.2. General view tests and checked casts

The object model also defines view-support tests for any valid View Target and a distinct exact-Type test. A support test queries `Supports(RuntimeObjectType(value), V)`; an exact test compares Runtime Type Identity with a concrete Core and excludes derived Types. The source syntax for Contract-view tests and exact tests remains deferred and does not extend the initial `is` syntax above. A `bool` saved in a variable carries no refinement provenance.

For a statically valid checked cast to `V`, success is exactly the same `Supports` predicate, always evaluated on the original object's Dynamic Type, including from a Contract view. Failure is an ordinary absence or error result, never Abort or unsafe reinterpretation.

| Source | Conceptual result | Acquisition and failure |
| --- | --- | --- |
| `objref/A` | `Option<objref/B>` | The source stays usable; the referent Origin is preserved |
| `objuniq/A` | `Option<objuniq/B>` | A child is reborrowed; failure carries no child Loan |
| `s/A`, where `s` is `obj`, `rc` or `arc` | `Result<s/B, s/A>` | The source is Moved once; success owns the target view, failure owns the original view |

```text
objref/Speaker holding Dog
    ├─ checked cast to Dog or Animal -> success
    ├─ checked cast to Named         -> success iff Supports(Dog, Named)
    └─ checked cast to Cat           -> failure

owned animal -> checked cast -> success(dog) or failure(original)
               animal is Moved in either branch
```

For an exclusive result whose variant is not yet known, the possible child Loan is tracked conservatively; the parent cannot conflict until that dependency ends. An owning cast never restores the source binding on failure. It neither destroys nor copies the object and changes no reference counts. Destroying the result follows the normal responsibility of the branch it holds.

Target validity and accessibility, Semantics preservation, [Owned erasure](15-ownership-and-lifetime-analysis.md#1581-object-payload-erasure), result Origins and Loans, and destruction dependencies are checked statically. Borrowing cannot create ownership or exclusivity; share explicitly before casting when needed. The source is evaluated and secured once. For a source certified by Owned payload erasure, a missing fixed Origin binding may be supplied as `static` only where the §15.2.3 proof covered that binding. Thus a cast to a concrete `Box<ref{static}/i32>` may be valid when Runtime Type Identity, Supports and all other checks match; this is a static proof, not a runtime recovery of an Origin. The handle's outer borrow Origin is preserved. Non-static bindings are never invented, per-call callable Origins are never bound, and no other information excluded from that proof is rewritten; a target needing unpreserved or uncertified information is rejected. API names and Option/Result branching syntax remain design boundaries: these guarantees define no cast spelling and add no checked cast to `@`.

## 13.7. Assignment

### 13.7.1. Simple assignment

`target = value` returns Unit and is evaluated in this order:

1. Evaluate the right-hand side fully and secure a temporary of the statically determined destination Type by Copy or Move, updating the source's state and responsibility.
2. Evaluate the left receiver and access path, left to right, once. Getters needed to locate the target are invoked, but not the final target's getter.
3. Destroy the old value, or the initialized parts still present, at the destination; Moved and Uninitialized parts are skipped.
4. Place the temporary by the ordinary Copy/Move rules, transferring its responsibility as applicable and leaving the destination Initialized.

For a custom, computed or required `set`, the secured input is passed to the setter instead of steps 3–4. A standard `set` places storage directly under the permissions of §11.1 and may restore incomplete storage. A constructor's first placement follows §11.3.1. A destination rooted in a getter-owned temporary is restricted by §11.2.3.

Replacement uses the existing storage without invoking incoming constructors or declaration initializers. A complete old value is cleaned up by its exact Type's full destruction chain, and an incomplete one under [partial cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup). A derived value's base view is not a whole-value target. Destination and ancestor permissions and Loans are checked first. If cleanup of the old value does not complete normally, nothing is installed; the old state is not restored, and execution does not continue with an observably empty destination.

The destination Type is known statically, so Type checking does not evaluate the left side early. Right-hand-side-first evaluation lets the destination supply its own old value. It deliberately differs from compound assignment and from ordinary receiver calls:

```kimi
values[index()] = makeValue()  // makeValue, index, old destruction, placement.
values[index()] += amount()    // index, old read, amount, compute, write.
obj().prop = arg()             // arg, obj, setter.
obj().setProp(arg())           // obj, arg, method call.
object.view.x = 10 // If computed view returns uniq/Point: RHS, view get, x set.
```

Use explicit locals when a particular order of receiver or index effects is needed.

Locating the destination uses the state after right-hand-side evaluation. It may locate a Moved writable local's storage without reading the former value, but cannot read a Moved owner or receiver to find a target. The storage must remain valid from location through placement. The shared [storage update dependency rules](15-ownership-and-lifetime-analysis.md#1573-storage-update-dependencies) apply throughout acquisition, target evaluation, destruction, placement and cleanup.

If the right-hand side does not complete normally, the left side is not evaluated. If left-side evaluation fails, nothing is destroyed or placed; if destroying the old value fails, nothing is placed. Earlier effects and Moves are never rolled back. The secured result stays alive during left-side evaluation; an ordinary control transfer cleans up the remaining temporaries under their normal lifetimes after securing any transfer result. Abort follows the common termination rules.

```kimi
var x = makeResource() // Non-Copy owned value.
x = x                 // Move to temporary, locate x, skip absent old value, Move back.
// No user-defined deinit runs; no self-assignment exception is needed.

var count: i32 = 0
let done: () = (count = 20)
```

Self-assignment of a Copy value is a Copy followed by a Replacement. A Non-Copy standard stored `x.p = x.p` may Move out and restore under Property permissions, whereas a custom `set` blocks that source Move. A custom, computed or required `get` calls its getter and then the selected `set`, provided the result fits the setter input and the receiver and Loan conditions hold. A `let` cannot be reinitialized.

Right associativity parses `a = b = c` as `a = (b = c)`; the inner Unit result makes ordinary numeric chaining invalid. Assignment is not a Boolean condition. Destructuring, whole-Slice assignment and initialization of raw uninitialized memory would need separate rules.

### 13.7.2. Compound assignment

`+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=` and `>>=` perform the corresponding binary operation and return Unit. The destination is resolved once, its old value read once, the right-hand side evaluated, the result computed and written once. This is not a textual rewrite to `target = target op value`: receivers and indices are not repeated.

String `+=` remains subject to the deferred operator ownership design of [§13.3](#133-arithmetic-bitwise-and-shift-operators); this section's evaluation order does not supply its missing acquisition rules.

Reading and writing are selected independently under Chapter 11: standard operations use permitted storage access, and custom, computed and required operations call their accessors. The read result must support the operator, and the operator's result must fit the `set` input. Valid receivers and Loans are required through every stage, and an owning getter may consume a receiver that `set` needs. No duplication is inserted, borrowing is not retried after a failed Move, and a setter is never bypassed through exclusive storage access. Evaluation remains target and read first, then the right-hand side, and updates of getter-owned temporaries remain forbidden (§11.2.3).

```kimi
values[nextIndex()] += amount() // Index, old value, amount, addition, write.
// item: Resource has standard get and custom set.
holder.item += x // Cannot acquire non-Copy item by Move for this update.
// User-defined Resource arithmetic is itself unavailable (§13.8).
holder.item = rebuild(holder.item@ref/Resource, x)
// OK if rebuild returns an independent owner and its input Loan ends before set.
```

If the right-hand side or the operation does not complete normally, nothing is written; getter and operand effects already performed remain. Compound assignment is not atomic and provides no synchronization. Raw-pointer `+=` and `-=` use only the permitted displacement operations and their unsafe conditions; other pointer compound assignments are forbidden.

## 13.8. Extension boundaries and reserved syntax

Operator symbols, precedence and associativity are fixed by the language. User-defined comparison uses the [Kimi Contract mapping](#1341-contract-comparison-mapping). User-defined arithmetic remains deferred and unavailable, and a same-named method does not authorize an operator. Future arithmetic must preserve evaluation order and counts and assignment's Unit result.

`and`, `or`, `not`, `=`, `@`, `is`, Ranges and control transfers cannot be reinterpreted by user code. Custom operator symbols and precedence declarations are not defined, and neither are `!`, `&&`, `||`, `~`, `**`, `??`, `?.` or a ternary `?:`; use the logical keywords and `if`. Unary `&` is not a borrow operation; use `@ref`/`@uniq`. Prefix `move`, a Move accessor and a dedicated `<-` Move operator are not defined. Recognition by the lexer alone does not make a token a usable operator.

`#Name` is an Attribute and `#if`/`#switch` are compile-time directives, not runtime unary operators.

**Composition Root.** The Composition Root is the reserved root for language-provided operations selected by `$`, independently of ordinary Name lookup; it is neither a value nor a macro facility. This revision defines `$abort(...)` under [Abort Termination](17-failure-handling.md#173-abort-termination) and the standalone test verification operations `$expect(...)` and `$require(...)` under [§17.5](17-failure-handling.md#175-test-verification-operations). These built-ins cannot be replaced, overloaded or acquired as function values, and unknown `$` operations are errors. Entry declarations and references, Provider selection and final composition remain unsettled; the withdrawn Composition Root design adds no source permission and does not redirect `Kimi.Console.writeLine`. Dependency configuration and artifacts are specified independently in Chapter 18.
