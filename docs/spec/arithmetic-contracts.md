# Arithmetic Contracts

This chapter supplements [§13.3](13-operators-and-assignment.md#133-arithmetic-bitwise-and-shift-operators). It defines static Contract selection for binary `+`, `-`, `*`, `/`, `%`, unary `-` and their compound assignments. Existing precedence, associativity, built-in numeric behavior and raw-pointer arithmetic remain unchanged.

## 1. Declarations and conformance

Kimi declares the following public Contracts. Recognition uses declaration Identity, never spelling or the presence of a same-named method. Each declares `associate Output`, a complete associated value Type.

| Operation | Ordinary Contract and requirement | Left-operand Contract and requirement |
| --- | --- | --- |
| `+` | `Addable<Rhs>.added` | `LeftAddable<Lhs>.addedFrom` |
| `-` | `Subtractable<Rhs>.subtracted` | `LeftSubtractable<Lhs>.subtractedFrom` |
| `*` | `Multipliable<Rhs>.multiplied` | `LeftMultipliable<Lhs>.multipliedFrom` |
| `/` | `Dividable<Rhs>.divided` | `LeftDividable<Lhs>.dividedFrom` |
| `%` | `RemainderProvider<Rhs>.remainder` | `LeftRemainderProvider<Lhs>.remainderFrom` |
| Unary `-` | `Negatable.negated` | None |

All binary ordinary requirements have the first signature below; all Left requirements have the second. Substitute the table's Contract and function names. Unary negation has the third signature.

```kimi
public contract Multipliable<Rhs>
    associate Output
    func multiplied(self: ref/Self, right: ref/Rhs) -> Self.Output

public contract LeftMultipliable<Lhs>
    associate Output
    func multipliedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output

public contract Negatable
    associate Output
    func negated(self: ref/Self) -> Self.Output
```

The requirements are safe, non-task functions without defaults and return values, not Places. Their inputs always use shared acquisition, independently of Copy. There is no implicit Move, clone or exclusive acquisition. Left requirements compute `left op self`; they are not generated from ordinary requirements, including for apparently commutative operations.

A **built-in numeric Type** is one of the twelve primitive integer Types, `f32`, `f64`, or `Wrapping<I>` for `I is PrimitiveInteger`. This term introduces no new Contract or Type. A **user provider Type** is an ordinary struct or enum with outer `owner` Semantics, including ordinary Kimi structs and enums, but excluding built-in and compiler-closed Types.

Explicit ordinary and unary conformances require a user provider Self. Explicit Left conformances additionally require a built-in numeric `Lhs`. An ordinary `Rhs` is any complete outer-owner value Type, including a Tuple or a Type containing borrows; it need not be numeric. Outer borrow, object and raw Semantics do not satisfy this condition. A direct `string` operand is prohibited on either side. Conformance is declared on Self under the ordinary inheritance, conditional-conformance, access and path-consistency rules (§8–9); there is no external pair registration.

These eligibility conditions are published conformance conditions of the standard Contracts. Definition checking proves them for every admitted binding, using known shapes, Semantics constraints and existing capability facts. Unknown eligibility is not deferred to an instantiation. Outer owner is not the `Owned` capability; internal external dependencies are allowed. Uses of a proven arithmetic Contract may rely on its eligibility conditions.

Determine `Output` under the common [associated-Type rules](associated-type-inference.md): explicit/fixed identities or bounded inference from a uniquely identified implementation's declared value result. A body never determines it. Disambiguate it as `T.(Multipliable<R>).Output` when necessary. It may differ from Self, contain a `Result` or preserve external borrows. It is not a family indexed by each call's fresh borrow Origins. Neither Copy nor Owned is imposed on it.

Ordinary conformance matching, Origin contracts, effects and result-Loan verification apply. These requirements impose no `confined`, `preserves-results`, purity, totality or allocation-free guarantee. Public APIs describe their rounding, domain and failure behavior; the Contracts assert no algebraic laws.

### 1.1. Built-in conformance

For each numeric Type `N`, the following conformances use counterpart `N` and `Output is N`. There are no mixed numeric or built-in Left conformances and no implicit numeric conversions.

| `N` | Binary Contracts | Negatable |
| --- | --- | --- |
| Signed primitive integer | All five | Yes |
| Unsigned primitive integer | All five | No |
| `Wrapping<I>` | All five | Yes, including unsigned `I` |
| `f32`, `f64` | Addable, Subtractable, Multipliable, Dividable | Yes |

Witnesses preserve checked or wrapping arithmetic, division/remainder by zero, `MIN / -1`, `MIN % -1`, IEEE rounding and the prohibition on observable reassociation or fusion (§13.3). They add no named members or constructors to numeric Types, including `Wrapping<I>`.

`T is PrimitiveInteger` entails the five binary conformances with counterpart and Output `T`, but not Negatable. `Wrapping<T>` under that premise entails all five with counterpart and Output `Wrapping<T>`, and Negatable. These are bounded proof rules, not general constraint search or numeric case enumeration. PrimitiveInteger retains its independent literal, conversion and position guarantees. The native i128/u128 division and remainder boundary of IMPL §21.5.3 applies equally to operators and witnesses, including Wrapping; it does not remove language conformance.

### 1.2. Requirement function values

Under `T is Addable<R>`, `T.added` may be selected as a Function Item, stored, called, passed to a compatible Callable or converted to a compatible common Function Type (§7.6, §10.7). The same route applies to ordinary, Left, unary and built-in witnesses. Keep the selected requirement and verified implementation mapping; never resolve again from a same-named method.

The ordinary signature, Origin, dependency and effect compatibility rules apply to each representation. An Item may retain an available `confined` guarantee. Receiver-specific `preserves-results` is not a function-value guarantee, and common Function erasure loses upper effect guarantees (§8.4.10.6–7).

## 2. Selection and inference

Selection is static. Determine operand Types from their syntax, explicit context and published premises, then follow the known safe value-reference layers of §3.4.1. Keep every traversed capability, Origin and Loan. An unknown Type parameter or associated Type is a terminal; do not follow object or raw payloads implicitly.

Choose the provider direction before looking for a conformance:

| Terminal operand Types | Route |
| --- | --- |
| Both built-in numeric | Existing built-in rules, including same-Type requirements |
| Left is a user provider | Its ordinary Contract |
| Left is built-in numeric, right is a user provider | The right Type's Left Contract |
| Raw-pointer forms admitted by §5.3 | Existing raw-pointer rules |
| Otherwise | No provider |

Directly applicable published evidence, such as `L is Addable<R>`, fixes the ordinary route without classifying L as numeric or user-defined. Select the built-in route only when the whole expression is proven to satisfy its rules. One numeric operand is insufficient: `S is PrimitiveInteger, V is LeftMultipliable<S>` selects the right provider for `s * v`.

1. On the fixed provider, enumerate the finite applicable conformances for the operation. Check eligibility, path consistency and **structural** input fitting against `ref/Rhs` or `ref/Lhs`; do not acquire operands during candidate trials or reinfer an already typed operand.
2. Merge paths to the same conformance as §8 requires. A unique selection retains the bound Contract, requirement mapping and Output, including full Origin identities. Distinct conformances do not merge merely because their structures coincide. An Unknown conditional candidate cannot be discarded to establish uniqueness.
3. After selection, check acquisition, Origin fitting relations, Loans and effects, then fit the result to its expected Type. A failure does not retry selection.

Multiple candidates are ambiguous. Do not rank by fitting quality, numeric width, default Type, Origin duration, Output or conditional-override priority. Do not search free functions, imported extensions, conversions or the opposite provider. Expected results never select a provider, conformance or operand Type.

Type shape and directly applicable evidence must agree on direction. Premises `L is Addable<R>` and `R is LeftAddable<L>` for the same operand pair contradict each other at definition checking. Evidence for different counterpart Types does not conflict. This check requires no general satisfiability search.

### 2.1. Literals and incomplete expressions

[§12.3.1](12-expressions.md#1231-type-inference) defines the shared integer and floating-point numeric literal-only classifications and fitting rules. Once a provider is fixed, fit a literal-only operand against the finite counterpart Types; do not enumerate all numeric Types or implicitly construct a user Type. A numeric literal-only left operand establishes the numeric domain for a known right user provider. Position and range literals do not.

Apply defaults only after candidate comparison. If all remaining candidates agree on the counterpart value Type, or the candidate is unique, that Type may guide a nested call or anonymous function under §10.5. Plan shared acquisition after typing; do not analyze a body once per candidate. Otherwise operand typing needs independent evidence.

Thus `v * make()` can guide `make` to `f64` when the fixed provider uniquely requires it; `make() * v` cannot infer a numeric left Type solely from V's Left conformances. Write `make<f64>() * v`. A proven `L is Addable<R>, R is PrimitiveInteger` can type `left + 1` under the all-integer literal rule. `L is Addable<L>` alone cannot construct L from `1`.

Never fits an independently known expected Type but proves neither direction, a counterpart Type nor uniqueness. Check unexecuted operands and continuations under the ordinary rules.

### 2.2. Generic stability

Definition checking fixes the route, direction, bound Contract, requirement mapping, Output, reference layers and acquisition. Instances substitute these facts; they neither traverse additional reference layers nor switch to a built-in route. Known numeric shapes, PrimitiveInteger and Wrapping premises prove Left eligibility without a numeric umbrella Contract.

Reference traversal routes an expression to its terminal Type; it does not grant conformance to the outer reference itself. For `value: ref/T`, use `T is Multipliable<f64>`, not `ref/T is Multipliable<f64>`. An optimization may specialize a known witness but must preserve the definition's acquisition and legality.

## 3. Evaluation, dependencies and failure

A binary operator evaluates and acquires its left operand, then its right operand, exactly once. This order holds for a right provider: its selected unbound requirement call is not a rewrite to `right.method(left)`.

The selected whole operation determines acquisition. Built-in numeric operators retain value reads at each operand's evaluation point. User operators and shared Contract calls borrow both inputs, including a numeric operand, under their requirements. The left inspection Loan starts before evaluating the right operand. Explicit `@move` and `@copy` run normally first and may supply an independent temporary to borrow.

A result must not depend on a new Loan created solely to inspect an input for this operation. It may preserve external dependencies already present in the input Type. A fixed Output does not waive actual dependency checking. Operation-only Loans end when the operation completes; existing Loans, result dependencies and dependencies of temporaries and their destruction keep their ordinary lifetimes. Later operands cannot Move, replace or exclusively borrow an earlier borrowed Place while its Loan is live.

Failure is not implicitly propagated: a Result Output remains a Result. Use `try (a / b)` to propagate; `try a / b` retains the existing parse `(try a) / b`. Abort, transfers and temporary destruction keep their normal rules and do not roll back earlier effects.

Built-in arithmetic checks, including intrinsic witnesses used by operators, report the start of the arithmetic expression or compound update. Operations or explicit Abort inside a user implementation retain their own locations. Explicit requirement calls and function-value calls follow their normal call-location rules.

## 4. Compound assignment

`op=` selects the same `op`; it has no independent update Contract and returns Unit. [§13.7.2](13-operators-and-assignment.md#1372-compound-assignment) owns the common assignment rules:

1. Evaluate and acquire the RHS as the selected operation requires.
2. Locate the target once and acquire its old value once.
3. Execute the selected operation once and secure its result.
4. End operation-only Loans, then perform the ordinary replacement or setter call once.

Do not reevaluate receivers or indices, bypass accessors or manufacture a reference to Uninitialized storage. The result need not have the old value's Type; it must fit the selected setter input by the ordinary rules. Standard get can lend a Non-Copy old value even with custom set. A computed getter result is an ordinary temporary: its dependencies can conflict with set even when the operator result is independent. Do not add early destruction to make such an update legal.

`x += x` is permitted when only operation-only shared Loans need to end before writeback. Existing live Loans or temporary destruction dependencies can still forbid the write. Reject a result depending on old contents lost during replacement. A reference variable is itself the target; write `r@follow += rhs` to update its referent.

If RHS evaluation, target location, the operation or old-value destruction fails or transfers control, do not execute later stages. Ordinary cleanup and Abort rules apply; no atomicity or rollback is promised.

## 5. Examples and boundaries

```kimi
struct Vec2
    Self is Addable<Vec2>
    Self is Multipliable<f64>
    Self is LeftMultipliable<f64>
    associate Addable<Vec2>.Output is Vec2
    associate Multipliable<f64>.Output is Vec2
    associate LeftMultipliable<f64>.Output is Vec2
    public let x: f64
    public let y: f64

    public init(x: f64, y: f64)
        self.x = x
        self.y = y

    public func added(self: ref/Self, right: ref/Vec2) -> Vec2
        return Vec2.init(self.x + right.x, self.y + right.y)

    public func multiplied(self: ref/Self, right: ref/f64) -> Vec2
        return Vec2.init(self.x * right, self.y * right)

    public func multipliedFrom(left: ref/f64, self: ref/Self) -> Vec2
        return Vec2.init(left * self.x, left * self.y)

let a = Vec2.init(1.0, 2.0)
let b = a * 2.0
let c = 2.0 * a
var total = a + b
total += c
total += total // No Copy conformance is required.

func sumPair<T>(left: ref/T, right: ref/T) -> T
    T is Addable<T>
    T.(Addable<T>).Output is T
    let add = T.added
    return add(left, right)
```

A provider with only `LeftMultipliable<f32>` fits `2.0`, `(1.0 + 1.0)` and `(-2.0)` to f32. Providing both f32 and f64 makes those uses ambiguous. An already typed `let scale = 1.0 + 1.0` is f64; it cannot fit f32 later. An integer `2` never fits a floating counterpart; use `2.0` or `2@f64`.

`Time - Time -> Duration` and `Matrix * Vector -> Vector` belong to the left Type. A right Vector may define a Left conformance for numeric f64, but cannot register `Decimal * Vector`: use Decimal's ordinary conformance, a wrapper or a named function.

There are no custom symbols, precedence declarations, extensions of unary `+`, increment/decrement, bitwise or shift operators, implicit conversions, user-Type literal construction, generated commutative counterparts, runtime Contract dispatch or user-operator constant evaluation. Logical, comparison, assignment, adaptation, range and control-transfer rules are unchanged. String concatenation uses interpolation; raw-pointer arithmetic remains §5.3. Optimizer folding cannot broaden constant-expression legality.

## 6. Diagnostics and retained meaning

Under §23, a missing provider or conformance identifies the operator, terminal operand Types and required bound Contract; distinguish an ineligible right-provider domain from missing ordinary conformance. Report invalid eligibility, requirements, associated Types, signatures and duplicate conformances at their declarations. Do not ignore invalid conformance to select another route. A direction conflict identifies the operator and both sources of evidence.

Ambiguity identifies counterpart Types and related conformance declarations. Advice may suggest an explicit Type when it would establish uniqueness, but not select by Output or an Origin fitting failure. Loan, Origin, setter-input and effect failures use their cause-specific diagnostics. Do not describe every missing user operator as a nonnumeric operand; string interpolation advice remains specific to the string prohibition. Valid but unimplemented forms are Unsupported, not language errors, and independent problems remain visible.

Retained meaning identifies the operation, built-in or Contract route, provider side, bound Contract, requirement and implementation Identities, Output, evaluation/acquisition order, reference layers, dependencies, effects and compound writeback. Associate it with the source snapshot. CLI, LSP and future CSP adapters reuse these facts without resolving for display; unimplemented publication is not reported as supported.
