# 13. Operators and assignment

[Specification index](../SPEC.md)

## 13.1. Grouping and evaluation order

Precedence and associativity are defined by the grammar ([§F.9](appendices/F-syntax-summary.md#f9-expressions)); [§F.9.4](appendices/F-syntax-summary.md#f94-adaptation-targets) decides which adaptation targets take postfix suffixes. Grouping decides only which operands belong to an operator; it neither makes an expression well-typed nor changes evaluation order. Operands are evaluated as [§12.2](12-expressions.md#122-evaluation-order) defines; `and` and `or` short-circuit ([§13.4](#134-comparison-and-logical-operators)).

```kimi
let total = f() + g()     // f's effects happen before g's.
let scaled = (a + b) * c  // Grouping chooses operands, not order: a, b, then c.
```

## 13.2. Unary operators

| Operator | Operand and result |
| --- | --- |
| `+value` | A numeric value, unchanged. |
| `-value` | Built-in negation of a signed integer, wrapping integer or floating-point value, or the terminal Type's `Negatable` requirement ([§13.3.3](#1333-arithmetic-contracts)). On an unsigned integer Type it is an error. |
| `not value` | The negated `bool`. |
| `*pointer` | The pointee Place of a raw pointer ([§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)). Safe references and object handles use `@follow` ([§13.5.5](#1355-follow)). |
| `^value` | `FromEnd<T>` storing `value` of any `T is PrimitiveInteger`, checked when resolved ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions)). |
| `++target`, `--target` | Increments or decrements the target; returns the new value. |
| `target++`, `target--` | Increments or decrements the target; returns the old value. |

Increment and decrement require a readable and writable integer or wrapping integer Place or Property. The target is located, read and written once each. The operation and its Abort check use the read Type (the `get` result Type), the expression's result has the read Type, and the written value is fitted to the setter input ([§13.7.1](#1371-simple-assignment)). The prefix forms do not read the target again. Otherwise they follow compound assignment ([§13.7.2](#1372-compound-assignment)).

```kimi
var count: i32 = 1
let before = count++ // before = 1, count = 2
let after = ++count  // after = 3, count = 3
```

## 13.3. Arithmetic, bitwise and shift operators

Binary `+`, `-`, `*`, `/`, `%` and unary `-` use the built-in operation or an arithmetic Contract ([§13.3.3](#1333-arithmetic-contracts)), as [§13.3.4](#1334-route-selection) selects. Bitwise, shift, increment and decrement operators are built-in only.

### 13.3.1. Built-in numeric operators

For built-in numeric operands ([§13.3.3](#1333-arithmetic-contracts)), `+`, `-`, `*` and `/` require one Type and return it; `%` accepts integer and wrapping integer Types only. Integer division truncates toward zero, and a nonzero remainder has the dividend's sign.

**Integer results.** For the integer and wrapping integer Types ([§3.1.1](03-types-and-values.md#311-wrapping-integer-types)), arithmetic, increment, decrement and compound assignment compute the mathematical result. When the Type cannot represent it, an integer Type Aborts and a wrapping integer Type **wraps** to the unique value congruent to the result modulo 2ᴺ, N being the bit width. Division or remainder by zero and a shift count outside `0 <= count < N` Abort for both kinds. Abort follows [§17.4](17-failure-handling.md#174-abort-termination), including its constant-evaluation rule.

| Expression (32-bit `MIN`, `MAX`) | `i32` | `Wrapping<i32>` |
| --- | --- | --- |
| `MAX + 1` | Abort | `MIN` |
| `MIN / -1` | Abort | `MIN` |
| `MIN % -1` | `0` | `0` |
| `x / 0`, `x % 0` | Abort | Abort |

A wrapping integer Type has the operators of its integer argument, both operands of a binary operation have that one Type, and every wrapping integer Type has unary `-`.

**Bitwise and shift.** `&`, `|` and `^` are bitwise AND, OR and XOR on two operands of one integer or wrapping integer Type; they do not accept `bool`. `<<` and `>>` return the left operand's Type and take any integer Type, but not a wrapping one, as the count. Left shift discards high bits; right shift sign-extends signed and zero-extends unsigned values. Discarded bits are not an unrepresentable result.

**Floating point.** `f32` and `f64` follow IEEE 754 with round-to-nearest, ties to even, including infinities, NaN and signed zero; division by zero does not Abort. Operations are never reassociated or fused when that could change a rounding or NaN result.

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010

func lowestBit(value: u32) -> u32
    let w = value@Wrapping<u32>
    return (w & -w)@u32     // -w wraps; the AND keeps the lowest set bit.
```

### 13.3.2. Strings and raw pointers

`string` has no arithmetic, bitwise, shift or concatenation operator. `+` or `+=` with a `string` operand, directly or through safe reference layers, is an error, even against a user provider. Strings are joined by an interpolated literal, which produces an owning `string` and borrows what it embeds ([§12.4.2](12-expressions.md#1242-interpolated-strings)); text built in steps uses a byte buffer ([§22.9.2](22-core-execution-and-foreign-functions.md#2292-text-operations)). Raw-pointer arithmetic has only the forms of [§5.3](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing).

```kimi
let first = "Hello, "
let joined = "\(first)world"   // An owning string; first is borrowed.
// let bad = first + "world"   // Error: strings are joined by interpolation.
```

### 13.3.3. Arithmetic Contracts

Kimi declares these public Contracts, recognized by declaration, never by spelling; each declares `associate Output`, a complete value Type.

| Operation | Ordinary Contract and requirement | Left Contract and requirement |
| --- | --- | --- |
| `+` | `Addable<Rhs>.added` | `LeftAddable<Lhs>.addedFrom` |
| `-` | `Subtractable<Rhs>.subtracted` | `LeftSubtractable<Lhs>.subtractedFrom` |
| `*` | `Multipliable<Rhs>.multiplied` | `LeftMultipliable<Lhs>.multipliedFrom` |
| `/` | `Dividable<Rhs>.divided` | `LeftDividable<Lhs>.dividedFrom` |
| `%` | `RemainderProvider<Rhs>.remainder` | `LeftRemainderProvider<Lhs>.remainderFrom` |
| Unary `-` | `Negatable.negated` | None |

Ordinary and Left requirements have the shapes below; `negated` is `func negated(self: ref/Self) -> Self.Output`. They are safe, non-task functions without defaults that return values. Their inputs are always shared borrows, whether or not the Type is Copy. A Left requirement computes `left op self` and is never generated from an ordinary one.

```kimi
public contract Multipliable<Rhs>
    associate Output
    func multiplied(self: ref/Self, right: ref/Rhs) -> Self.Output

public contract LeftMultipliable<Lhs>
    associate Output
    func multipliedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output
```

**Terms.** A **built-in numeric Type** is a numeric Type ([§3.1](03-types-and-values.md#31-primitive-cores)), a Type parameter with proven `PrimitiveInteger`, or `Wrapping<T>` of such a parameter. A **user provider Type** is a struct or enum with outer `owner` Semantics that is not a built-in numeric Type.

**Eligibility.** An explicitly declared ordinary or unary conformance requires a user provider Self; an explicitly declared Left conformance also requires a built-in numeric `Lhs`. An ordinary `Rhs` is any complete value Type with outer `owner` Semantics, such as a Tuple or a Type holding borrows. A direct `string` operand is forbidden on either side. Conformance is declared on Self under [§8.6](08-generics-constraints-and-contracts.md#86-conformance); there is no external pair registration. Definition checking proves eligibility for every admitted binding, and uses may rely on it. Outer `owner` is not the `Owned` capability.

**Output** is determined by associated-Type inference ([§8.5.5](08-generics-constraints-and-contracts.md#855-inference)), never by a body, and is written `T.(Multipliable<R>).Output` when needed. It may differ from Self, contain a `Result` or keep external borrows; neither Copy nor Owned is required. The Contracts assert no algebraic law, purity, totality or effect guarantee.

**Built-in conformances.** Each numeric Type `N` conforms with counterpart and Output `N`; there are no mixed-Type or built-in Left conformances. Integers, `Wrapping<I>`, `f32` and `f64` have all five binary Contracts, except that floating-point Types lack `RemainderProvider`; signed integers, `Wrapping<I>` and floating-point Types have `Negatable`. The witnesses behave exactly as [§13.3.1](#1331-built-in-numeric-operators). They add no named members or constructors to numeric Types; the requirement is reachable only through the Contract, as in `T.added` (Function values below). `T is PrimitiveInteger` entails the five binary conformances of `T`, not `Negatable`, and `Wrapping<T>` then has all six. Entailed conformances serve generic use, such as passing `T` to a function requiring `Addable<T>`; they do not change operator routing ([§13.3.4](#1334-route-selection)).

**Function values.** Under `T is Addable<R>`, `T.added` is a Function Item that keeps the selected requirement and implementation; it may be called, stored or converted under the ordinary rules ([§7.7](07-functions-and-callable-values.md#77-function-expressions), [§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility), [§8.11.6](08-generics-constraints-and-contracts.md#8116-callable-effect-bounds)).

```kimi
struct Vec2
    Self is Addable<Vec2>
    Self is LeftMultipliable<f64>
    associate Addable<Vec2>.Output is Vec2
    associate LeftMultipliable<f64>.Output is Vec2
    public let x: f64
    public let y: f64
    public init(x: f64, y: f64)
        self.x = x
        self.y = y
    public func added(self: ref/Self, right: ref/Vec2) -> Vec2
        return Vec2.init(self.x + right.x, self.y + right.y)
    public func multipliedFrom(left: ref/f64, self: ref/Self) -> Vec2
        return Vec2.init(left * self.x, left * self.y)

let a = Vec2.init(1.0, 2.0)
var total = a + 2.0 * a // LeftMultipliable<f64>, then Addable<Vec2>.
total += total          // Both operands are shared borrows; no Copy is needed.
// let b = a * 2.0      // Error: Vec2 has no Multipliable<f64>.

func sumPair<T>(left: ref/T, right: ref/T) -> T
    T is Addable<T>
    T.(Addable<T>).Output is T
    let add = T.added   // A Function Item.
    return add(left, right)
```

### 13.3.4. Route selection

Selection is static. Operand Types come from syntax, explicit context and published premises; safe value-reference layers are followed to the terminal Type ([§3.4.3](03-types-and-values.md#343-reference-path-selection)), keeping their capabilities, Origins and Loans. An unknown Type parameter or associated Type is a terminal; object and raw payloads are not followed. The route is chosen before any conformance is sought:

| Terminal operand Types | Route |
| --- | --- |
| Both built-in numeric | The built-in rules of [§13.3.1](#1331-built-in-numeric-operators) |
| Left is a user provider | The left Type's ordinary Contract |
| Left is built-in numeric, right is a user provider | The right Type's Left Contract |
| Raw-pointer forms of [§5.3](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing) | The raw-pointer rules |
| Otherwise | Error: no provider |

When both terminal Types are built-in numeric, the built-in route applies even if an arithmetic Contract premise for the operation is stated. For other pairs, directly applicable evidence such as `L is Addable<R>` fixes the ordinary route without classifying `L`; one numeric operand is not enough, so under `S is PrimitiveInteger, V is LeftMultipliable<S>`, `s * v` uses the right provider. Premises `L is Addable<R>` and `R is LeftAddable<L>` for one operand pair are a contradiction and an error.

On the fixed provider, the applicable conformances are checked for eligibility and structurally fitted against `ref/Rhs` or `ref/Lhs`, without acquiring operands or retyping a typed operand. Paths to one conformance merge ([§8.9.2](08-generics-constraints-and-contracts.md#892-merging-paths-and-cycles)); an undecided conditional candidate is not discarded. More than one candidate is ambiguous: candidates are never ranked, and free functions, extensions, conversions and the opposite provider are never searched. An invalid conformance is an error, not a reason to choose another route. After selection, acquisition, Origins, Loans and effects are checked and the result is fitted; a failure never reselects, and an expected result Type never selects anything.

**Literals.** Literal fitting follows [§12.3](12-expressions.md#123-literal-typing) and defaults follow [§10.2](10-overload-resolution-and-inference.md#102-common-adaptation). A literal-only operand is fitted against the fixed provider's counterpart Types; a user Type is never built from a literal. A numeric literal-only left operand establishes the numeric domain for a known right user provider. A counterpart Type shared by all remaining candidates may guide a nested call or function expression ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)). A `Never` operand proves no direction or counterpart.

**Generic stability.** Definition checking fixes the route, Contract, requirement, Output, followed layers and acquisition; instances never switch route. Following a reference grants the reference no conformance: for `value: ref/T`, require `T is Multipliable<f64>`, not `ref/T is Multipliable<f64>`.

```kimi
// Vec2 as in the previous example (Addable<Vec2>, LeftMultipliable<f64>); func make<T>() -> T.
func sum<T>(a: T, b: T) -> T
    T is PrimitiveInteger and Addable<T>
    return a + b          // Built-in route: operands are read as values.

let v = Vec2.init(1.0, 2.0)
let w = v + make()        // The unique counterpart Vec2 guides make.
let q = make<f64>() * v   // The Left Contract does not infer make's Type.
// let r = 2 * v          // Error: the integer literal does not fit the counterpart f64.
```

### 13.3.5. Evaluation of arithmetic operators

The left operand is evaluated and acquired before the right; a right provider's call is not a rewrite to `right.method(left)`. The built-in route reads values at each operand's evaluation; a Contract route shared-borrows both inputs, including a numeric one, and the left Loan is live while the right operand is evaluated. Explicit `@move` and `@copy` run first and may supply a temporary to borrow. A result must not depend on a Loan created only for the operation; such Loans end when it completes. A `Result` Output stays a `Result`: write `try (a / b)`, since `try a / b` is `(try a) / b`.

## 13.4. Comparison and logical operators

`==`, `!=`, `<`, `<=`, `>` and `>=` return `bool`.

- Numeric operands have one Type; a wrapping integer Type orders as its integer argument, not cyclically. `char` compares Unicode scalar values; `string` compares UTF-8 bytes lexicographically, without normalization or locale. `bool` and Unit have equality only.
- For floating-point values, `+0.0 == -0.0` is true; with a NaN operand, `!=` is true and the other five are false.
- Tuples whose corresponding elements all support the comparison compare elementwise for equality and lexicographically for ordering, left to right until the result is decided; an unordered pair makes all four ordering operators false.

An operand of safe value-reference layers (`ref`, `uniq`, qualifying pair layers) denotes its terminal Place ([§3.4.3](03-types-and-values.md#343-reference-path-selection)); following acquires nothing. The terminal Types must be equal, or one operand is `Never`, and that Type's comparison applies; outer Origins need not match. Comparison operands have no expected Type ([§12.3](12-expressions.md#123-literal-typing)): only an untyped literal, a literal-only operand or `null` is fitted to the other operand's terminal Type, and an operand that needs an expected Type, such as an inferred Case `.Name` or an empty collection literal, is an error.

Object handle Types (`obj/T`, `rc/T`, `arc/T`, `objref/T`, `objuniq/T`) have no comparison operators; a payload's conformance is not forwarded to them. Payloads are compared explicitly through `@follow` where permitted ([§13.5.5](#1355-follow)); there is no object-identity comparison. Raw pointers compare under [§5.1](05-raw-pointers-and-unsafe-memory.md#51-null-and-equality). In a generic body, a comparison whose operands have Type `T` or `ref/T` under `T is Equatable` (or `Comparable`) uses `T`'s Contract mapping in every instance, even with `T = ref/f32`; for `T = f64` it keeps the NaN-reflexive `equals` of [§13.4.1](#1341-contract-comparison-mapping), never the built-in `==`.

**Inspection Loans.** A comparison never Moves an owned operand. A built-in comparison inspecting a Non-Copy owned Place forms a shared Loan when that operand is evaluated, so the left Loan is live while the right operand is evaluated, under the ordinary conflict rules ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)). Both operands must be Initialized. These Loans end when the comparison completes or is interrupted; the `bool` result keeps none.

<!-- verified-example: comparison-inspection (positive; commented line is a rejection) -->
```kimi
func take(text: string) -> string => text@move

let text = "a"
let same = text == "a" // Shared inspection; text is not Moved.
// let invalid = text == take(text@move)
// Error: the left operand's shared Loan is live when take transfers text.
Console.writeLine(text) // Allowed: the completed comparison's Loan has ended.
```

**Logical operators.** `and`, `or` and `not` take and return `bool`. `left and right` evaluates `right` only when `left` is true; `left or right` only when `left` is false.

```kimi
let found = i < n and matches(values[i]) // Indexes only when in range.
if status == Status.Ready => start()
// if status == .Ready => start()            // Error: a comparison operand has no expected Type.
// a, b: rc/Point, with Point Sealed and Equatable.
let sameValue = a@follow == b@follow        // Compares payloads.
// let bad = a == b                          // Error: object handles have no comparison.
```

### 13.4.1. Contract comparison mapping

Comparing two operands of one user Type requires the Kimi Contract below, whose mapping is resolved once; free functions, extensions and conversions are never searched. Generic code needs the corresponding Constraint. Built-in comparisons take priority.

| Operators | Operation | Result |
| --- | --- | --- |
| `==`, `!=` | `Equatable.equals` on shared borrows of both operands | The `bool`, or its negation |
| `<`, `<=`, `>`, `>=` | `Comparable.compare` on shared borrows of both operands | The `i32` compared with zero |

`Comparable` refines `Equatable`: the sign of `compare` must agree with equality and a total order. Integers, wrapping integers, `char` and `string` provide both; `bool`, Unit, `f32` and `f64` provide `Equatable` only. Safe value-reference and Tuple Types forward or compose these capabilities; object handle Types do not. Structs and enums need explicit conformances; nothing is derived, and comparison grants no arithmetic. The `Equatable.equals` of `f32` and `f64` uses the NaN-reflexive equality of [§12.4.3](12-expressions.md#1243-dictionary-literals), while a built-in `==` on NaN is false.

## 13.5. Explicit operations

`@` is a built-in operation. It cannot be overloaded, searches no conversion chains and applies only to its direct operand. It calls no user code itself, although evaluating the operand may. Only object creation ([§13.6.2](#1362-object-creation-clone-and-lifecycle)) allocates; no form boxes, duplicates ownership or increments a count implicitly. Origin restriction fits the result ([§3.8](03-types-and-values.md#38-type-relations-and-operations)) and never substitutes another operation.

### 13.5.1. Forms and adaptation targets

| Form | Meaning |
| --- | --- |
| `E@Type`, `E@Semantics` | A defined adaptation ([§13.5.3](#1353-defined-adaptations)); shorthand takes the Core, referent Type or View Target from the operand |
| `E@move`, `E@copy` | Transfer, or Copy of a proven-Copy value ([§13.5.3](#1353-defined-adaptations)) |
| `E@follow` | The Place a reference or object handle points to ([§13.5.5](#1355-follow)) |
| `E@raw`, `E@raw/U`, `E@usize` | Raw pointer forms: see [§5.4](05-raw-pointers-and-unsafe-memory.md#54-addresses-and-conversions) |
| `E@wrap<U>`, `E@bits<U>` | Wrapping and bit conversion ([§13.5.4](#1354-numeric-wrapping-and-bit-conversions)) |

An **adaptation target** gives Semantics and a Core, a complete inner Type for a borrow or pointer layer, or an object View Target. Origins written on its outer Semantics chain are an error, even through grouping; they are inferred. Types nested inside, such as an Option payload, keep their annotations. Container path syntax ([§9.7.1](09-names-signatures-and-access.md#971-bound-container-paths)) relaxes none of this, and bound Container qualifiers are unavailable here.

**Syntactic extent.** The extent of a target (Semantics prefixes, `/`, `<`, closed and open targets, and where postfix suffixes may follow) is defined in [§F.9.3](appendices/F-syntax-summary.md#f93-type-arguments-or-less-than) and [§F.9.4](appendices/F-syntax-summary.md#f94-adaptation-targets). Each Semantics prefix must resolve to a concrete Semantics or a declared pair binder; primitive keywords cannot be pair binders, so `x@i32 / y` is division.

A grouped target is always a Type, so `x@(copy)` is no operation. Semantics shorthand takes neither `?` nor `during`; `?` applies to the complete target Type ([§3.2.3](03-types-and-values.md#323-optional-types)), so for `x: i32`, `x@i32?` is an error.

```kimi
let n = tasks@uniq.length            // Closed target: postfix continues.
let x = (point@Point).x              // Open target: group before selection.
let o = saved@(ref/T? during a)      // saved: Option<ref/T during a>; the payload annotation is kept.
// let e = items@Array<i32>[0]       // Error: group the adaptation first.
// let w = value@(ref/T during a)    // Error: Origin written on the outer borrow.
// let d = saved@ref/T? during a     // Error: group the annotated target.
```

**Bare target names.** In `E@X`, built-in Semantics names are recognized first; otherwise `X` is looked up separately as a Type (including aliases and Type parameters) and as a pair binder. A Type only gives `@Type`, a pair binder only gives shorthand, and both, or an ambiguity in one role, is an error that no operand or expected Type resolves.

### 13.5.2. Selection and inference

The operation is chosen from the written target and the operand's Type and category; access, ownership, Loans and Origins are checked afterward, and a failure never selects another operation, getter or overload. A target may guide literal and generic inference where the ordinary inference rules permit, but never changes an established operand or result Type; a numeric, wrapping or bit conversion target guides only a direct literal ([§13.5.4](#1354-numeric-wrapping-and-bit-conversions)). An outer expected Type never cancels the operation. A numeric conversion target is not an expected Type for its operand: `(200 + 100)@u8` computes an `i32` and fails its range check, and `id(300)@u8` does not infer `id` from `u8`. Conversions and Identity Acquisition at a read Type read a reference operand by the value read ([§3.5.3](03-types-and-values.md#353-value-read)), so `number@i64` with `number: ref/i32` converts the referent; borrow, `@follow`, `@copy`, `@move` and object targets take the reference itself. Generic access effects follow [§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects). A `Never` operand prevents the operation from running but not its checks.

### 13.5.3. Defined adaptations

| Operation | Condition |
| --- | --- |
| Transfer | `@move`; a Movable Place or a Temporary Value |
| Copy | `@copy`; a proven-Copy Type |
| Identity Acquisition | The same normalized complete Type, or a complete owning target matching the operand's outer Semantics; a Copy Place, a Temporary Value, or the value read of a reference to a read Type |
| Numeric, wrapping, bit conversion | [§13.5.4](#1354-numeric-wrapping-and-bit-conversions) |
| Borrow, Reborrow | [§13.5.6](#1356-borrow-and-reborrow) |
| Object creation, upcast | [§13.6.2](#1362-object-creation-clone-and-lifecycle), [§13.6.1](#1361-object-upcasts) |
| Raw pointer conversion, address, typed null | The forms of [§5.4](05-raw-pointers-and-unsafe-memory.md#54-addresses-and-conversions); no unsafe context |

**Transfer.** `E@move` on a Movable Place ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)) transfers its value and destruction responsibility and marks it Moved, even for a Copy Type. On a borrow value it moves the reference; a Place reached through a reference is never transferred. On a Temporary Value, including a getter result, it has no effect. `holder.item@move` transfers the Field or getter result, never the receiver ([§7.3](07-functions-and-callable-values.md#73-receivers)).

**Copy.** `E@copy` Copies the value and leaves a Place unchanged; a Non-Copy or Copy-unproven operand is an error, so a generic `T` needs `T is Copy`. `r@copy` Copies the reference; `r@follow@copy` Copies a Copy referent. It is the Copy counterpart of `E@move` where an owned value must be written, as for a ByValue Subject ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)), and names no overload candidate ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)). `move`, `copy` and `follow` take no Type, `?`, `during` or binding set.

**Identity Acquisition** Copies a Copy Place and transfers a Temporary Value; a Non-Copy Place is an error. A target whose outer Semantics is `ref`, `uniq`, `objref` or `objuniq` selects a Borrow or Reborrow ([§13.5.6](#1356-borrow-and-reborrow)), never Identity Acquisition. Bare `@owner` is invalid. Complete owning and generic targets and the short forms `@obj`, `@rc`, `@arc` select same-Type acquisition, object creation or upcast, or are errors; none duplicates a strong owner.

**Origin restriction** then shortens result Origins only as variance and outlives permit; it adds no Copy, Move or Borrow. A target changing both Core and Semantics must be one defined operation; no convert-then-borrow is inserted. There is no elementwise, structural, string, `bool`, `char` or user-defined conversion and no dynamic cast through `@`. A safe reference yields its referent's value only by the value read, the shared-reference adaptation of [§10.2](10-overload-resolution-and-inference.md#102-common-adaptation) or `@follow`; a Non-Copy referent is never extracted.

```kimi
let number: i32 = 1
let a = number@copy        // Copy; number stays usable.
let b = number@i32         // Identity Acquisition: the same Copy.
inspect(number@i64@ref)    // Convert, then borrow the temporary.
// number@ref/i64          // Error: conversion and Borrow are separate operations.
// let e = number@owner    // Error: a bare owning shorthand is not an operation; write @copy.
let c = number@move        // Transfer; number becomes Moved although i32 is Copy.
// let d = number@i32      // Error: number is Moved.
// resource@copy           // Error: Resource is Non-Copy; write @move or borrow.
// resource@Resource       // Error: a bare Non-Copy Place is not transferred; write @move.
```

### 13.5.4. Numeric, wrapping and bit conversions

| Form | Operand and target Types | Result | Failure |
| --- | --- | --- | --- |
| `@Type` | Integer and floating-point Types; `U` ↔ `Wrapping<U>` | The same value, or the rounding below | Abort outside the range |
| `@wrap<U>` | Integer and wrapping integer Types | The value wrapped to `U` | None |
| `@bits<U>` | A floating-point Type and a same-width integer or wrapping integer Type | The same bits | None |

`@Type` never truncates or wraps an integer. Integer → integer checks the range. Integer → float and float → float round to nearest, ties to even, with gradual underflow; a finite value that rounds to infinity fails. Float → integer truncates toward zero and checks the range; NaN and infinities fail. Float-to-float keeps NaN, infinities and signed zero, including the sign of a value rounded to zero; NaN payloads need not be kept. Integer zero converts to positive zero. Rounding and checks apply at every `@` in a chain.

**Wrapping integer Types.** The only `@Type` conversions involving a wrapping integer Type are `U` → `Wrapping<U>` and back, which keep the bits; any other is an error. Crossing a width, signedness or floating-point boundary states the intent: `x@U@Wrapping<U>` checks, `x@wrap<Wrapping<U>>` wraps, `f@bits<Wrapping<U>>` reinterprets.

**Direct literals.** A direct literal ([§12.3](12-expressions.md#123-literal-typing)) is converted once from its exact value, without an intermediate default Type; a failure to fit is a compile-time error. Every other operand is typed first. Literal adaptation does not widen implicit argument fitting.

| Target | Integer literal | Floating literal |
| --- | --- | --- |
| Integer Type | Must fit | Truncated toward zero from the exact value, then must fit |
| `Wrapping<U>` | Must fit `U` | Error |
| `f32`, `f64` | Rounded once | Rounded once ([§2.6](02-source-and-lexical-structure.md#26-number-literals)) |
| `wrap<U>` | Wrapped to `U` | Error |
| `bits<F>`, `F` floating-point | Wrapped to the unsigned Type of `F`'s width, then read as `F` | Error |
| `bits<I>`, `I` integer | Error; use `@wrap` | Rounded to the floating-point Type of `I`'s width, then read as `I` |

**Wrapping conversion.** `E@wrap<U>` converts between any integer and wrapping integer Types, including the same Type, and proven-`PrimitiveInteger` parameters ([§8.7.4](08-generics-constraints-and-contracts.md#874-primitiveinteger)). Narrowing keeps the low bits; widening extends by the operand's signedness; the bits are read as `U`, a complete value Type without Semantics.

**Bit conversion.** `E@bits<U>` converts either way between `f32` and `i32`, `u32`, `Wrapping<i32>` or `Wrapping<u32>`, and between `f64` and the 64-bit counterparts; any other pair is an error. The result is the IEEE 754 encoding, or the value encoded, with NaN payloads and the sign of zero kept. It is unavailable while either Type depends on a Type parameter.

```kimi
let minimum = ((-128))@i8          // Direct literal, as (-128)@i8.
let negated = -(128)@i8            // An i32 negation, then a conversion.
let b = 200@u8@wrap<i8>            // -56
let e = -1@i32@wrap<u32>@u64       // 0xFFFF_FFFF: zero-extended through u32.
let half = 1.5@f32@bits<u32>       // 0x3FC0_0000
let w = (300@u32)@Wrapping<u32>    // Same integer argument.
let n = -(128)@u8                  // Abort: -128 is outside u8.
// let x = 256@u8                  // Error: the direct literal does not fit.
// let y: Wrapping<u32> = -1       // Error: -1 is not a u32 value.
// let z = w@u64                   // Error: different integer argument; write w@u32@u64.
```

### 13.5.5. Follow

`E@follow` selects the Place that a `ref/T` or `uniq/T` value points to, Copying and Moving nothing. The referent of `ref/T` offers Read, that of `uniq/T` Read and Write; neither offers Take. Each `@follow` removes one layer. `let` forbids replacing a slot but does not weaken the reference it stores, and a shared layer never yields exclusive access: `ref/(uniq/T)` and `uniq/(ref/T)` reach `T` shared only.

```kimi
var number: i32 = 1
let r = number@uniq
r@follow = 2              // Updates number.
let child = r@follow@uniq // Exclusive Reborrow of number through r.
// r@uniq                 // Error: r is a let binding; its slot is not exclusively borrowable.
```

**Object payloads.** On `obj/T`, `rc/T`, `arc/T`, `objref/T` or `objuniq/T`, `@follow` selects the payload only when the View Target is exactly the payload Type `T`, internal Origins included, and `T is Kimi.Sealed` is proven ([§8.7.2](08-generics-constraints-and-contracts.md#872-sealed)). The handle slot and the payload are distinct Places; the payload never offers Take.

| Path to the handle | Payload access |
| --- | --- |
| Writable owning `obj/T` path | Read, Write, shared and exclusive borrows |
| Read-only owning path, such as `let h: obj/T` | Read and shared borrows |
| `objuniq/T`, also through a `let` slot | Exclusive within the borrow |
| `rc/T`, `arc/T`, `objref/T`, or any shared path | Shared only |

These permissions also govern View borrows, upcasts and implicit receiver borrows; open Views keep their own rules ([§12.7](12-expressions.md#127-object-receiver-compatibility)). A Borrow whose lending point lies in an object keeps a Loan in its own mode on the handle Place it was reached through: while it is live, that handle cannot be Moved, replaced, exclusively borrowed or destroyed ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)), though a shared Borrow permits reading and cloning it.

```kimi
func borrowPayload<T>(source: objref/T) -> ref/T during source
    T is Sealed and ObjectPayload
    return source@follow@ref
```

**Pair layers.** A **pair layer** is a normalized Type whose outer Semantics is a pair binder `s` ([§8.1.1](08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)): `s/T` with direct target `T`, or `s/U` with direct target `U`; the pair target `T` alone is not one. When the admitted set ([§8.3.4](08-generics-constraints-and-contracts.md#834-semantics-admitted-set)) of `s` is contained in `value or valueborrow`, a pair layer is one safe value-reference layer, present for `s = ref` or `uniq` and absent for `s = owner`, and `@follow` selects the Place storing the direct target; for any other admitted set it is an error. It is not the inverse of `x@s` ([§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects)), which for `s = owner` Copies into a temporary. Each operation applies the rule of each admitted Semantics, and the body must be valid for all of them ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)). For an operand Place `P` with path dependencies `p` and the mode `M` reached so far ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)):

| Aspect | `owner` | `ref` | `uniq` |
| --- | --- | --- | --- |
| Selected Place | `P` itself ([§3.6](03-types-and-values.md#36-temporaries)) | The referent | The referent |
| Access Effect ([§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects)) | Borrow | Reborrow; a Copy of the reference in the adaptation of [§10.2](10-overload-resolution-and-inference.md#102-common-adaptation) | Reborrow |
| Read | When `P` is readable | Yes | Yes |
| Write | When `P` is exclusively writable | No | Yes, also through a `let` slot; never through a shared path |
| Dependencies | `p`; for a value, its Temporary Place ([§3.6](03-types-and-values.md#36-temporaries)) | `o` and the parent Loan, not the slot | As for `ref` |
| Mode of a bare Place Subject | Shared | Shared | The weaker of `M` and Exclusive |
| Mode past the layer | The weaker of `M` and Exclusive, only where the lending point can be exclusively acquired ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)) | Shared | As for `owner` |

A generic body gets the weakest capability and mode of the admitted cases, so Write needs `s is owner or uniq` and, with `owner` admitted, an exclusively writable `P`. Each case is checked with its own dependencies, derived from its whole path; `o` is the outer Origin of the followed occurrence ([§8.1.2](08-generics-constraints-and-contracts.md#812-pair-reconstruction-and-origin-annotations)). Overload ranking uses the worst admitted case ([§10.2.2](10-overload-resolution-and-inference.md#1022-inference-and-adaptation-classes)). Take is never offered, so a Pattern never binds by value past a pair layer. Instantiation does not change the selected Place or its legality. Following a pair layer adds no Copy, runtime branch or allocation. `@follow` on a concrete owned Place is an error.

```kimi
func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c
    s is value or valueborrow
    return c[i]@follow@ref   // owner: Borrow of the element; ref, uniq: shared Reborrow.

func viewUniq<s/T>(c: uniq/Collection<s/T>, i: isize) -> uniq/T during c
    s is owner or uniq
    return c[i]@follow@uniq  // Rejected for Collection<ref/Node>: s is owner or uniq is Refuted.
```

### 13.5.6. Borrow and Reborrow

`E@ref`, `E@ref/V`, `E@uniq` and `E@uniq/V` borrow the written slot `E` itself: for a stored `V` the results are `ref/V` and `uniq/V`, and a written `V` must be exactly the stored Type; it never selects the referent, Copies a same-Type reference or Reborrows, so for `r: ref/T`, `r@ref/T` is an error; write `r` or `r@copy`. The lending point is given by [§3.4.2](03-types-and-values.md#342-lending-points). Initialization, capability, Loans and Origins must be valid; a Temporary Value is materialized ([§3.6](03-types-and-values.md#36-temporaries)). Implicit adaptations at expected Types are in [§10.2](10-overload-resolution-and-inference.md#102-common-adaptation).

| Input | Operation | Result |
| --- | --- | --- |
| Readable Place storing `V` | `@ref`, `@ref/V` | New `ref/V` |
| Exclusively writable Place storing `V` | `@uniq`, `@uniq/V` | New `uniq/V` |
| Readable `obj/T`, `rc/T` or `arc/T` Place | `@objref` | New `objref/T`; no count change |
| Exclusively writable `obj/T` Place | `@objuniq` | New `objuniq/T` |
| `objref/T` | `@objref` | Copy of the shared reference |
| `objuniq/T` | `@objref`, `@objuniq` | Shared or exclusive object Reborrow |
| Raw Place storing `V`, in an unsafe context | As for an owned Place | New reference with a fresh lending point ([§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)) |

A **Reborrow** of a value reference is written through the referent: `r@follow@ref` and `r@follow@uniq` lend the referent's capability without moving `r`. An expected `ref/T` or `uniq/T` Reborrows a `uniq/T` value implicitly ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)), and bare acquisition of a Place storing `uniq/T` Reborrows it ([§3.5.1](03-types-and-values.md#351-reborrow-of-a-stored-exclusive-reference)). While a child Loan is live, conflicting access through the parent is forbidden. A Borrow depends on the borrowed slot; a Reborrow on the referent and the parent Loan. Shared access is never upgraded, `rc` and `arc` give no exclusive borrows, and value and object borrows never convert into each other.

**Getter results.** A custom, computed or required `get` is called once and its result adapted. Borrowing an owned result materializes a temporary under [§11.5](11-properties.md#115-getter-results). A reference returned by a call or getter is a value: an expected Type adapts it, and `@follow` Reborrows its referent ([§7.1](07-functions-and-callable-values.md#71-declarations-bodies-and-results)). `set` grants no borrow of hidden storage.

```kimi
var value = makeValue()
let exclusive = value@uniq
let child: uniq/Value = exclusive // Exclusive Reborrow; exclusive resumes after child's last use.
let moved = exclusive@move        // Transfers the reference, not its referent.
let view = holder.item@follow@ref // item's getter returns ref/Resource: Reborrow of the referent.
```

### 13.5.7. Evaluation and failure

Each operand and required receiver is evaluated once; chained operations complete from the inside out. Acquisition never propagates backward through a call or getter into hidden storage. A Borrow is protected from its formation; exclusive receiver and argument borrows follow call reservations ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)), and other exclusive borrows are live at once. A discarded result is still evaluated and checked. Completed Moves and effects are never rolled back.

| Failure | Handling |
| --- | --- |
| Undefined adaptation; Type, access, initialization, Loan or Origin violation | Compile-time error |
| Literal fitting or required constant conversion failure | Compile-time error |
| Failed runtime numeric conversion | Abort when evaluated, even if the value is known statically |
| Broken unsafe promise | Undefined behavior ([§5](05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory)) |

Other constant evaluation follows [§17.4.4](17-failure-handling.md#1744-builds-and-compile-time-evaluation).

## 13.6. Object operations, runtime tests and checked casts

### 13.6.1. Object upcasts

Let `S` be the source's static View Target and `V` a different target. An upcast requires a static proof that `Supports(S, V)` ([§3.3.1](03-types-and-values.md#331-object-views-and-identity)) holds for every more-derived Dynamic Type: the base graph for concrete Cores, the runtime Contract rules ([§8.12](08-generics-constraints-and-contracts.md#812-runtime-contracts)) for Contract Views. A Core that opts out of ObjectPayload ([§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload)) is never a target. Payload erasure ([§15.8.1](15-ownership-and-lifetime-analysis.md#1581-object-payload-erasure)), access, Origins and Loans are checked.

| Source | Operation | Result |
| --- | --- | --- |
| `obj/S`, `rc/S`, `arc/S` | `@obj/V`, `@rc/V`, `@arc/V` | The same owning handle by ordinary acquisition (a Place needs `@move`) |
| `objref/S` | `@objref/V` | Copy of the shared reference |
| `objuniq/S` | `@objuniq/V`, `@objref/V` | Exclusive or shared child Reborrow |
| Shared-borrowable `obj/S`, `rc/S` or `arc/S` Place | `@objref/V` | Shared object Borrow |
| Exclusively writable `obj/S` Place | `@objuniq/V` | Exclusive object Borrow |

An upcast keeps the object, its identity, Dynamic Type, counts and cleanup responsibility. There is no implicit upcast and no value-borrow upcast; `@ref/V` is not `@objref/V`. A target the source cannot guarantee needs a checked cast ([§13.6.5](#1365-checked-casts-and-target-completion)).

```kimi
let dog: obj/Dog = makeDog()
let animal = dog@move@obj/Animal // dog is Moved; animal owns the whole Dog.
let view = animal@objref
// let bad = view@objref/Dog     // Error: Animal does not statically imply Dog.
```

### 13.6.2. Object creation, clone and lifecycle

For `s` in `obj`, `rc` and `arc`, the forms `@s` and `@s/T`, aliases and generic targets share one rule: `owner/T` to `s/T` proves `T is ObjectPayload` and creates an object; `s/T` to `s/T` is same-Type acquisition; `s/Derived` to `s/Base` is an upcast ([§13.6.1](#1361-object-upcasts)); anything else is another defined adaptation or an error. The input Type is fixed independently of the target and expected Type, so `1@obj` is `obj/i32` and `1@obj/i64` is an error; write `1@i64@obj`. Creation keeps Type arguments, lengths, internal Origins and Loans and is never combined with an upcast. There is no `obj → rc/arc`, `rc ↔ arc`, shared-to-exclusive or borrow-to-owner operation. A `Never` input creates nothing.

`Kimi.Intrinsics` ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)) also declares these functions; their `value` and `build` labels, and the `value` label of the Weak operations ([§13.6.3](#1363-weak-references)), may be omitted, and creating through a function or `@` has the same effect. Creation requires `T is ObjectPayload`, cyclic creation also `T is Owned`; `clone` takes `<s/T>` with `s is rc or arc`.

| Function | Input → result | Effect |
| --- | --- | --- |
| `makeObj<T>(value)` | `T → obj/T` | New exclusive object |
| `makeRc<T>(value)`, `makeArc<T>(value)` | `T → rc/T`, `T → arc/T` | New shared object, strong count one; `arc` counts atomically |
| `clone<s/T>(value)` | `ref/(s/T) → s/T` | One more strong reference to the same object, View and mode |
| `makeRcCyclic<T, F>(build)`, `makeArcCyclic<T, F>(build)` | `F → rc/T`, `F → arc/T` | Cyclic construction, below |

**Creation** acquires the input once (a Non-Copy Place needs `@move`) and Moves it into new storage without running constructors, accessors or `drop`. Creating `arc` grants no cross-thread transfer ([§D.5](appendices/D-deferred-features.md#d5-concurrency)). **Strong clone** shared-borrows the handle slot, written `clone(handle@ref)` because a handle slot is never borrowed implicitly ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)); it allocates nothing and keeps the View, Dynamic Type and dependencies. The input stays Initialized, and the result keeps no Loan on the input handle slot, so the source handle may be Moved or replaced afterwards. Moving or borrowing an object never changes counts.

**Lifecycle.** An object is Building, Alive, Destroying, then Freed. Building exposes no strong or payload access. The final strong release destroys the complete Dynamic Type and frees the storage; a Weak does not keep the object alive. `obj` has the same boundaries without a count.

**`arc` ordering.** For one `arc` object: initialization of the payload happens before any payload use through a strong handle; every strong release happens before the final strong release, and the final release happens before payload destruction. A successful `upgrade` happens before the final strong release; after the final release `upgrade` fails permanently. Returning a new handle to its creating thread creates no cross-thread synchronization. These guarantees cover reference counting and lifetime only, not payload mutation. The atomic orderings that implement them are implementation-defined.

**Cyclic construction.** The `rc` factory requires `F is Callable<owner, (Weak<rc/T>) -> T>` (`arc` analogously); `F` is acquired normally and called once.

1. Allocate unpublished storage and one builder Weak.
2. Pass the Weak by value to the builder. It may be Moved or cloned; `upgrade` returns `None` while Building.
3. After the builder returns and its call's cleanup completes, Move the result into the payload.
4. Publish Alive with the first strong and return it.

An allocation failure or a count increment at the maximum Aborts before publication; counts never wrap. Abort or nontermination of the builder prevents publication.

```kimi
let shared = 7@rc
let again = Kimi.Intrinsics.clone(shared@ref) // Strong count two.
let boxed = "sample"@obj
// let wrong: obj/i64 = 1@obj                 // Error: the input stays i32.
// let duplicate = shared@rc                  // Error: a Non-Copy Place needs @move; this is not clone.
```

### 13.6.3. Weak references

Each function of `Kimi.Intrinsics` requires `s is rc or arc` and the eligibility of [§3.2.2](03-types-and-values.md#322-weak-references) for `s/T`; it evaluates its input once and leaves it Initialized:

| Function | Input → result | Effect |
| --- | --- | --- |
| `downgrade<s/T>(value)` | `ref/(s/T) → Weak<s/T>` | A Weak to the same object; the strong count is unchanged |
| `upgrade<s/T>(value)` | `ref/Weak<s/T> → Option<s/T>` | `Some` new strong reference while the object is Alive, otherwise `None` |
| `clone<s/T>(value)` | `ref/Weak<s/T> → Weak<s/T>` | Another Weak to the same object |

For a `ref/Weak<…>` argument the strong `clone` binds `s = owner`, which is refuted, so the two never both apply. `downgrade` accepts only a strong `rc` or `arc` handle and may allocate; `upgrade` and Weak `clone` do not. Results keep the object, View and mode. `upgrade` competes with a final `arc` release under [§13.6.2](#1362-object-creation-clone-and-lifecycle); `None` while Building may be followed by publication, but failure after the final release is permanent. A count overflow Aborts. A Weak, its clones and an upgraded strong keep the payload's external dependencies but no lasting Loan on the source slot, so a Weak to a payload holding a local borrow cannot outlive that borrow. Moving a Weak changes no count, and destroying one never touches the payload. Payload access requires `upgrade`.

```kimi
struct Node
    public let selfWeak: Weak<rc/Node>
    public init(selfWeak: Weak<rc/Node>)
        self.selfWeak = selfWeak@move

let build = func [] (weak: Weak<rc/Node>) -> Node
    let before = Kimi.Intrinsics.upgrade(weak) // None while Building.
    return Node.init(weak@move)
let node = Kimi.Intrinsics.makeRcCyclic(build)
let after = Kimi.Intrinsics.upgrade(node.selfWeak) // Some after publication.
```

### 13.6.4. Runtime is tests

In an ordinary expression, `value is T` and `value is not T` test the runtime object. The context decides the meaning of `is` before lookup: Constraint Clauses, including the leading region of a body ([§7.5](07-functions-and-callable-values.md#75-function-constraints)), and associated-Type conditions use the requirement test ([§8.2](08-generics-constraints-and-contracts.md#82-constraints-and-requirement-expressions)); compile-time directives reject every `is` test. An `is` test ends after its one target, so `value is Dog or Cat` is `(value is Dog) or Cat`.

The target is one named struct Core, optionally qualified, without Semantics, binding name or composition. Its Type arguments are complete Types; no Origin, `during` or binding set is written anywhere in it, also after alias expansion, and target completion ([§13.6.5](#1365-checked-casts-and-target-completion)) supplies its bindings. The target itself must not be a Type parameter, an associated Type or a non-struct Core; its Type arguments may contain enclosing Type parameters and associated Types (`value is Slot<T>`). The target's object form under the left side's Semantics must be formable ([§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload)).

The left side has Type `obj/S`, `rc/S`, `arc/S`, `objref/S` or `objuniq/S` with a struct Core `S`. It is evaluated once, and the result is `Supports(D, T)` for the Dynamic Type `D` of `value`, or its negation, generic arguments included. The test Copies, Moves and acquires nothing and changes no count. A test with a statically known result is still valid and evaluates its operand. Refinement follows [§14.10](14-control-flow.md#1410-type-refinement).

```kimi
require value is Dog and value.isHealthy() else => return
value.bark()
// if value is T => () // Error: a Type parameter is not a struct Core.
```

### 13.6.5. Checked casts and target completion

A checked cast to `V` succeeds exactly when `Supports` holds on the original object's Dynamic Type; its validity includes forming the result Type, and failure is an absence or error value, never an Abort. This revision defines its meaning but no source spelling; view-support tests for Contract Views and exact-Type tests likewise have no source syntax ([§D](appendices/D-deferred-features.md#appendix-d-deferred-features)).

An `objref/A` source gives `Option<objref/B>` and stays usable; an `objuniq/A` source gives `Option<objuniq/B>` by a child Reborrow, and a failure keeps no child Loan; an `obj`, `rc` or `arc` source `s/A` is Moved once into `Result<s/B, s/A>`, a failure holding the original View.

**Target completion.** Runtime `is` tests, refinement and checked casts complete the bindings of a target that writes no Origin. Let `S` be the operand's View Target (for a refined name, that of its Effective Type, [§14.10.1](14-control-flow.md#14101-refinable-bindings-and-effective-types)) and `D` the target. The relation is judged by `Supports` over runtime identity ([§3.3.1](03-types-and-values.md#331-object-views-and-identity)); a generic body judges it for every admitted binding.

| Relation of `S` and `D` | Bindings of `D` | `is` test | Checked cast |
| --- | --- | --- | --- |
| `D` is proven to be `S` or a base of `S` | Those `S` gives at that position | Always true; adds no fact | Always succeeds, as the upcast |
| `D` is proven to be neither | Every fixed Origin binding is `static`; the completed `D` must be Owned | Refines ([§14.10.2](14-control-flow.md#14102-condition-facts-and-joins)) | Success holds the completed `D` |
| Undecided (only in a generic body) | None | Adds no fact | Error |

The second row relies on single inheritance ([§6.2.2](06-declarations-and-containers.md#622-inheritance-and-open-structures)): success requires a strictly more derived Dynamic Type, whose erasure proved the payload Owned ([§15.8.1](15-ownership-and-lifetime-analysis.md#1581-object-payload-erasure)). A generic body proves Owned from its Constraints. Non-static bindings and per-call callable Origins ([§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned)) are never created, and the handle's Semantics, outer Origin and Loans are kept.

```kimi
open struct Base
    protected init() => ()

struct Slot<U> : Base
    public var value: U
    public init(value: U) : base() => self.value = value@move

func readValue(view: objref/Base) -> ref/i32 during static
    require view is Slot<ref/i32> else => $abort("Not a slot")
    // require view is Slot<ref/i32 during static> else => $abort("Not a slot") // Error: a target writes no Origin.
    return view.value // view is objref/Slot<ref/i32 during static>.
```

## 13.7. Assignment

### 13.7.1. Simple assignment

The target of an assignment, compound assignment, increment or decrement is not a Receiver Expression ([§7.3](07-functions-and-callable-values.md#73-receivers)): it is located with write permission under the path, `let`/`var` and Property permissions of [§11.2](11-properties.md#112-standard-access), needs no `@uniq` or `@objuniq`, and starts no call reservation; compound assignment, increment and decrement protect their destination as [§13.7.2](#1372-compound-assignment) states. Getters called to locate it, and the receiver of a custom, computed or required `set`, are Receiver Expressions ([§11.3.1](11-properties.md#1131-accessor-receivers)).

**Destination Type.** For a standard `set` the destination Type is the Type of the destination Place. When the selected `set` is custom, computed or required, the destination Type is that setter's `value` input Type: the right-hand side of `=` is fitted to it, including expected-Type inference and literal fitting. Compound assignment, increment and decrement compute in the read Type (the `get` result Type): the operation, its range check and Abort, and the result of a prefix or postfix `++`/`--` use that Type; the computed value is then fitted to the setter input by the common adaptation ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)).

`target = value` returns Unit and runs in this order:

1. Evaluate the right-hand side and secure a temporary of the destination Type by the common adaptation ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)), including transfer (`y = x@move` for a Non-Copy Place).
2. Evaluate the target's receivers and access path left to right, once; the target's own getter is not called.
3. Destroy the old value, or its parts still Initialized. Writing a part of a value without construction completion is an error ([§15.1.2](15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness)).
4. Place the temporary; the destination is Initialized.

A custom, computed or required `set` receives the secured value instead of steps 3 and 4. A standard `set` places the value directly under [§11.2](11-properties.md#112-standard-access) and may restore incomplete storage. A constructor's first placement of its own Fields follows [§6.3.4](06-declarations-and-containers.md#634-the-construction-receiver). A destination rooted in a getter temporary is restricted by [§11.5](11-properties.md#115-getter-results).

Replacement runs no constructor or initializer. A complete old value is destroyed by its exact Type's destruction chain, an incomplete one by partial cleanup ([§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)); a base view is not a whole-value target. If that cleanup does not complete, nothing is placed. Locating uses the state after the right-hand side, so it may locate a Moved local's storage but cannot read a Moved owner or receiver. Storage update dependencies follow [§15.7.3](15-ownership-and-lifetime-analysis.md#1573-storage-update-dependencies). If the right-hand side does not complete, the target is not evaluated; if locating fails, nothing is destroyed or placed; nothing is rolled back. Since assignment returns Unit, `a = b = c` assigns Unit to `a`.

```kimi
values[index()] = makeValue()  // makeValue, index, old destruction, placement.
obj().prop = arg()             // arg, obj, setter.
obj().setProp(arg())           // obj, arg, method call.

var x = makeResource()         // Non-Copy.
x = x@move                     // Transfer out, locate x, skip the Moved old value, place.

// g: Gauge, whose computed `level: i32` has `get() -> i32` and `set(value: i64) -> ()`.
g.level = 3_000_000_000 // OK: the literal fits the setter input i64.
// g.level += 1         // Error: the i32 result does not fit the setter input i64.
```

### 13.7.2. Compound assignment

`+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=` and `>>=` perform the binary operation and return Unit. An arithmetic compound assignment selects the same operation as its operator ([§13.3.4](#1334-route-selection)); there is no separate update Contract.

1. Evaluate and acquire the right-hand side as the operation requires.
2. Locate the destination once and read its old value once, by standard access or the selected `get`.
3. Perform the operation once in the read Type ([§13.7.1](#1371-simple-assignment)). An unrepresentable integer result Aborts before the write ([§13.3.1](#1331-built-in-numeric-operators)).
4. End the operation-only Loans, then write once by replacement or the selected `set`.

Receivers and indices are evaluated once. The write is never redirected from a reference to its referent ([§3.5.3](03-types-and-values.md#353-value-read)): write `r@follow += rhs`. Reading and writing are selected independently ([§11.1](11-properties.md#111-property-kinds)): standard access uses permitted storage access, and custom, computed and required accessors are called; a setter is never bypassed. The old value is acquired as the operator requires, with no implicit Move; a standard `get` can lend a Non-Copy old value even with a custom `set`. A getter result is an ordinary temporary whose dependencies can conflict with `set`. Consuming the old value is written explicitly: `a = transform(a@move)`.

**Writeback protection.** From the moment compound assignment, increment or decrement locates its destination until the write, the destination Place (standard `set`) or the receiver of a custom, computed or required `set` is protected like a call reservation ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)): shared reads and shared Borrows, including the `get` call and the operator's operand Loans, may coexist and must end before the write; any write, Move, destruction or exclusive access to it by another path in that interval, including an effect of the `get` or operator implementation, is an error. The exclusive Loan becomes live at the write.

Storage update dependencies follow [§15.7.3](15-ownership-and-lifetime-analysis.md#1573-storage-update-dependencies). If a step does not complete, later steps do not run and nothing is rolled back. Compound assignment is not atomic. A `string` destination has none ([§13.3.2](#1332-strings-and-raw-pointers)). Raw pointers allow only `+=` and `-=` under [§5.3](05-raw-pointers-and-unsafe-memory.md#53-pointer-arithmetic-and-indexing).

```kimi
values[nextIndex()] += amount() // amount, nextIndex, old value, addition, write.
x += x                          // OK: the operand's shared Loan ends before the write.
holder.item += delta            // OK: the shared get and the Addable call only read holder before set.
// log += "!"                   // Error: no string compound assignment; write log = "\(log)!".
```

## 13.8. Reserved syntax and the Composition Root

Operator symbols, precedence and associativity are fixed. User code extends only comparison ([§13.4.1](#1341-contract-comparison-mapping)) and arithmetic ([§13.3.3](#1333-arithmetic-contracts)); a same-named method grants neither. `and`, `or`, `not`, `=`, `@`, `is`, Ranges and control transfers cannot be redefined. There are no custom operators and no `!`, `&&`, `||`, `~`, `**`, `??`, `?.`, ternary `?:`, unary `&` (use `@ref`), prefix `move` or `<-`; `as` is reserved. User arithmetic operators are never evaluated as constants. `#Name` is an Attribute and `#if`/`#switch` are directives, not operators.

**Composition Root.** `$` selects a language-provided operation, independently of Name lookup; recognition of the operation name is defined by the keyword table ([§F.3](appendices/F-syntax-summary.md#f3-keywords)). The operations are `$tryWrite(writer, literal)` ([§22.10.4](22-core-execution-and-foreign-functions.md#22104-trywrite)), `$abort(...)` ([§17.4](17-failure-handling.md#174-abort-termination)), and the test operations `$expect(...)` and `$require(...)` ([§17.6](17-failure-handling.md#176-test-verification-operations)). They cannot be replaced, overloaded or used as function values; any other `$` operation is an error.

```kimi
if n < 0 => $abort("negative input")
// let f = $abort  // Error: a $ operation is not a function value.
// let s = a ?? b  // Error: ?? is not an operator.
```
