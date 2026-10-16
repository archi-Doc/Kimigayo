# 12. Expressions

[Specification index](../SPEC.md)

Expressions produce values or transfer control. Their syntax is in [§F.9](appendices/F-syntax-summary.md#f9-expressions).

## 12.1. Classification and contexts

A normally completing expression produces a typed result, possibly Unit ([§3.1.3](03-types-and-values.md#313-unit-and-never)). Value and Discard Contexts ([§14.2](14-control-flow.md#142-bodies-and-evaluation-contexts)) decide how a result is used; a discarded result keeps its effects and all Type, ownership and destruction checks.

An indented body is not an expression; use a selection or `do` expression to get a value from several operations. `unsafe`, `defer`, `require` and `_ = expression` are statements. `let` and `var` declarations are neither expressions nor condition bindings ([§14.2.3](14-control-flow.md#1423-conditions)).

```kimi
_ = compute()           // Discard: effects and checks are kept.
// let x = (let y = 1)  // Error: a declaration is not an expression.
```

## 12.2. Evaluation order

Operands are evaluated once, left to right, unless a construct states otherwise.

| Expression | Order |
| --- | --- |
| `a() + b() * c()` | `a`, `b`, `c`, `*`, `+` |
| `receiver().method(a(), b())` | receiver, callee resolution, `a`, `b`, call |
| `array()[index()]` | target, index, element access |
| `(a(), b())`, `[a(), b()]` | elements in source order |
| `[key(): value()]` | per entry: key, duplicate check, value, insertion ([§12.4.3](#1243-dictionary-literals)) |
| `start()..end()` | start, end; an omitted boundary evaluates nothing; no boundary is checked |
| `"\(a()) \(b())"` | each substitution evaluated and written in order; a failure stops the rest |

`and`, `or` and selections evaluate only the operands or branches they need. Assignment evaluates its right-hand side first ([§13.7](13-operators-and-assignment.md#137-assignment)). Type arguments, length arguments and adaptation targets are not evaluated at run time.

An abrupt Completion, divergence or Abort skips the later operands and the enclosing operation. Skipped syntax is still checked for names, Types and transfer targets; syntax excluded by `#if`/`#switch` follows [§19](19-compile-time-directives.md#19-compile-time-directives).

```kimi
let ok = ready() and check()  // check() runs only when ready() is true.
```

## 12.3. Literal typing

### 12.3.1. Expected Types and defaults

Declared Types of bindings, parameters and results are expected Types and propagate into the expression (collection elements, selection branches); otherwise a Type comes from the operands. Built-in numeric operations need operands of one numeric Type: widths, signedness, and integer and floating-point Types never mix implicitly. There are no implicit conversions between `bool`, `char` and numbers, and a condition must be `bool`.

An untyped integer literal adopts an expected integer Type if its value fits; a floating-point literal adopts an expected `f32` or `f64`. Otherwise integer literals default to `i32` (also every range boundary and `^` operand with no other source of its Type, [§4.6.2](04-arrays-indexing-and-slices.md#462-ranges)), floating-point literals to `f64`, and literals in length contexts to `isize` ([§4.2](04-arrays-indexing-and-slices.md#42-length-expressions)); a literal that does not fit the default needs an explicit Type. In a call, defaults apply only after candidate selection ([§10.2.1](10-overload-resolution-and-inference.md#1021-literals)), never during candidate comparison or conformance selection ([§4.7.1](04-arrays-indexing-and-slices.md#471-index-expression-resolution)). Integers never fit floating-point Types implicitly.

```kimi
let a: i64 = 10
let b = a + 20       // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64    // A typed operand is converted explicitly.
let e = a + c        // Error: i64 and i32 do not mix.
```

### 12.3.2. Direct literals

A **direct literal** is a numeric literal, optionally with one prefix sign `+` or `-` applied directly to it (whitespace may separate them), the whole optionally enclosed in any number of parentheses. A direct literal fits a Type when the signed value it denotes is a value of that Type; whether the Type defines unary `+` or `-` does not matter. A sign applied to anything else, such as `-(1)`, is the unary operator, and a `-` between two operands is binary.

```kimi
let a: i8 = -128              // Direct literal: fits.
let b: i8 = ((- 128))         // The same direct literal.
let c: i8 = -(128)            // Error: 128 does not fit i8.
let d: u32 = -0               // The value 0 fits.
let e: u32 = -1               // Error: -1 is not a u32 value.
let f: Wrapping<u8> = -(1)    // Unary - of Wrapping<u8> applied to 1.
```

### 12.3.3. Literal-only expressions

Literal-only expressions are a typing classification:

- **Integer:** an untyped integer literal, or parentheses, unary `+`/`-`, arithmetic, bitwise or shift operations on integer literal-only operands. It fits integer and wrapping integer Types.
- **Floating-point:** an untyped floating-point literal, or parentheses, unary `+`/`-` or binary `+`, `-`, `*`, `/` on floating-point literal-only operands. It fits `f32` and `f64`.
- **Position/range:** `^a` with an integer literal-only operand ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions)), or a range whose written boundaries are integer or from-end literal-only ([§4.6.2](04-arrays-indexing-and-slices.md#462-ranges)).

Nothing else is literal-only, and the classes never mix: `1 + 1.0` is invalid.

A literal-only expression fits a Type when every direct literal fits the Type it receives and every operator is defined for that Type; an integer literal fits a wrapping integer Type when it fits its integer argument. The Type flows to both operands of arithmetic and bitwise operators, to a unary operand, to each range boundary as its `S` or `E` ([§4.6.2](04-arrays-indexing-and-slices.md#462-ranges)), to the operand of `^` as the `T` of `FromEnd<T>`, and only to the left operand of a shift; a shift count is typed on its own ([§13.3.1](13-operators-and-assignment.md#1331-built-in-numeric-operators)). In a numeric conversion only a direct literal fits the target ([§13.5.4](13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions)), so `(200 + 100)@u8` computes in `i32`.

Fitting changes only typing: it checks leaves and operators, never the computed value, and the value never decides between candidates. The expression is evaluated at run time in its fitted Type, operation by operation: `127 + 1` fitted to `i8` Aborts at run time; fitted to `Wrapping<i8>` it is `-128`, and floating-point operations round at every step. Required constant evaluation keeps its own rules, so `[(4 / 0) of u8]` is a compile-time error. Length forms ([§4.2](04-arrays-indexing-and-slices.md#42-length-expressions)), ConstantIndexExpression ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)) and Literal Patterns ([§14.8.1](14-control-flow.md#1481-patterns)) are not extended.

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(2 * 5)                            // Error: both candidates fit in the same way.
let wide: Range<i64, i64> = 0..(1 << 40) // 1 is i64; the count 40 is i32.
let f = (1.0 + 1.0)@f32                  // Computed in f64, then converted.
```

## 12.4. Primary expressions

### 12.4.1. Names, literals and grouping

| Form | Meaning |
| --- | --- |
| `name` | A visible binding, function or other named entity. |
| `Self.member`, `isize.MaxValue` | Static member of the current or a primitive Type ([§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)). |
| `123`, `1.5`, `true`, `'€'`, `"text"` | Literals ([§2](02-source-and-lexical-structure.md#2-source-and-lexical-structure)). |
| `"value = \(value)"` | Interpolated string ([§12.4.2](#1242-interpolated-strings)). |
| `null` | Contextually typed raw null pointer ([§5.1](05-raw-pointers-and-unsafe-memory.md#51-null-and-equality)). |
| `()` | Unit value. |
| `(value)` | Grouping; preserves a Place. |
| `(value,)`, `(a, b)` | Tuple. |
| `[a, b]`, `[]` / `[key: value]`, `[:]` | Array / Dictionary literal. |

An array has one element Type, a Dictionary one key and one value Type. An empty literal needs an expected Type. Array and Dictionary literals use the required Kimi declarations ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)), except for an expected fixed array ([§4](04-arrays-indexing-and-slices.md#4-arrays-indexing-and-slices)); an unresolved element Type never falls back to a universal object Type.

```kimi
let pair = (10, "ten")
let names = [1: "one", 2: "two",]
let empty: Array<i32> = []
let unknown = []        // Error: an empty literal needs an expected Type.
```

### 12.4.2. Interpolated strings

An interpolated literal produces an owning `string`. Each substitution fits the shared input of `Utf8Writer.write` ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)) and needs `Utf8Format` for the selected referent Type; borrow Types do not forward it. A bare Place in a substitution is not Moved. Evaluation and formatting follow [§22.10](22-core-execution-and-foreign-functions.md#2210-formatting-and-interpolation); `$tryWrite(writer, literal)` follows [§22.10.4](22-core-execution-and-foreign-functions.md#22104-trywrite).

```kimi
let name = "Ada"
let count = 3
let line = "name = \(name), count = \(count)"
```

### 12.4.3. Dictionary literals

Equivalent duplicate keys in a Dictionary literal are an error.

**Static check.** Boolean, integer, `char`, plain string and Unit literal keys, grouped or not (integer keys may be direct literals, [§12.3.2](#1232-direct-literals)), are fitted to the key Type and compared by value at compile time, across all entries and in unreachable syntax too. No other key is, not even a constant `let` binding or an operator expression, and optimization never extends the set.

**Runtime check.** Other entries are processed in source order: evaluate the key once; if an equivalent key is already inserted, Abort ([§17.4](17-failure-handling.md#174-abort-termination)) before evaluating this value or any later entry; otherwise evaluate the value once and insert. An entry that does not complete normally is not inserted, no later entry is processed, and no partial Dictionary results; completed effects stay.

**Key equality** is the Equatable mapping, called as `stored.equals(search)`; the key Type needs no Hash conformance. The order and number of equality calls are unspecified, and each call keeps its effects. For `f32` and `f64` keys, all NaN values of one Type are equivalent, and so are both zeros; this does not change `==` ([§13.4.1](13-operators-and-assignment.md#1341-contract-comparison-mapping)). User key equality must be an equivalence relation that does not change for a key while the Dictionary holds, checks or inserts it; a violation may make results unspecified or Abort, but never breaks memory safety.

```kimi
let x: i32 = getKey()
let map = [x: first(), x: second()] // Aborts at the second key; second() is not called.
let bad = [1: "a", (1): "b"]        // Error: duplicate literal key.
```

## 12.5. Access and invocation

### 12.5.1. Member access

`expression.name` selects a member by qualified lookup ([§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)), which never inserts `self`. When a value reference lacks the member, selection continues in its referent ([§3.4.3](03-types-and-values.md#343-reference-path-selection)). After `.` comes a member Name or an in-range decimal integer that selects a Tuple element. There is no dynamic member lookup. Type qualifiers are bound Container paths ([§9.7.1](09-names-signatures-and-access.md#971-bound-container-paths)) and create no value; `.init(...)` is a construction expression ([§6.3](06-declarations-and-containers.md#63-constructors)), never a value-member call.

Property access follows [§11.1](11-properties.md#111-property-kinds). Raw pointers are not followed implicitly: write `(*pointer).name` in an Unsafe Block.

```kimi
let first = (10, "ten").0
var collection = makeCollection()
collection.count = 10   // Needs a setter and write permission.
```

### 12.5.2. Invocation and generic application

`callee(a, b)` calls a function, method or function value; a trailing comma is allowed. A task call writes the task first: `callee(task; a)` ([§24.3](24-suspension-and-asynchronous-tasks.md#243-task-calls)). Explicit generic arguments follow [§8.1.3](08-generics-constraints-and-contracts.md#813-type-arguments). Whether `<` begins Type arguments is decided by [§F.9.3](appendices/F-syntax-summary.md#f93-type-arguments-or-less-than).

Argument mapping and candidate selection follow [§10](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference); named arguments and omission follow [§7.2](07-functions-and-callable-values.md#72-parameters). Function values take positional arguments only, and unsafe calls keep [§7.6](07-functions-and-callable-values.md#76-unsafe-functions). Generic arguments fill Type slots and length slots ([§4.4](04-arrays-indexing-and-slices.md#44-function-length-parameters)) only, within [§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries). After resolution a matching explicit specialization is selected ([§8.13](08-generics-constraints-and-contracts.md#813-explicit-full-function-specialization)). `T(args)` is not a constructor; write `T.init(args)` ([§6.3](06-declarations-and-containers.md#63-constructors)).

```kimi
let a = largest<i64>(1, 2)
let c = i<n            // A comparison: no matching >.
let d = Point(1, 2)    // Error: write Point.init(1, 2).
```

## 12.6. Object member calls

The member, overload, access and public contract are selected from the receiver's Effective Type ([§14.10.1](14-control-flow.md#14101-refinable-bindings-and-effective-types)). A nonvirtual call uses that implementation; a virtual call uses the Dynamic Type's implementation of the same slot ([§6.4.3](06-declarations-and-containers.md#643-calls-and-conditional-slots)). The receiver is adjusted to the selected declaration's subobject ([§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection)). A projection that hides the complete derived Type needs the Owned erasure proof of a View upcast, checked after selection, except for operations on lexical `self` in an override body ([§6.4.5](06-declarations-and-containers.md#645-receiver-safety)).

A call through an object borrow or base-subobject projection needs ObjectCallCompatible Proven ([§12.7](#127-object-receiver-compatibility)), except on a **complete Sealed payload**: when the View Target is exactly the receiver's complete Sealed Type, a `ref/Self` or `uniq/Self` receiver is acquired by the payload follow ([§13.5.5](13-operators-and-assignment.md#1355-follow)), implicitly ([§7.3](07-functions-and-callable-values.md#73-receivers)) or as `handle@follow.method()`, for an ordinary complete-value call (an exclusive one also needs [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)). An argument always needs an explicit `@follow`. Open Views and base subobjects never undergo whole-value replacement or a Move that leaves them incomplete.

## 12.7. Object receiver compatibility

**ObjectCallCompatible** is the public guarantee that a borrowed-receiver call preserves receiver completeness, access authority and its Type, Origin and Loan contract. Each call operation (a function, a custom or computed `get` or `set`, or a standard Contract witness bridge) has one status, Proven or NotProven, never one per instantiation. Standard `get` and `set` are Place operations, not calls: borrowing `Box<T>.item` with `@ref` does not need `T is Copy`.

An operation's ObjectCallCompatible status is part of its public contract. It is the least fixed point of the effect rules of [§12.7.1](#1271-effect-rules) over the operation's body and the bodies of its explicit specializations, determined by these rules alone and not by analysis precision or optimization. Changing a body can therefore change the status, and with it the acceptance of calls through object borrows or base-subobject projections; consumers of the declaring Kotonoha rely on the status. It is checked after overload selection and never excludes or reselects a candidate ([§7.3](07-functions-and-callable-values.md#73-receivers)). Caller premises, Type arguments, the Dynamic Type or a selected specialization cannot strengthen it. An invalid body is an error and a body whose verification is unsupported is Unsupported ([§23.1.1](23-compiler-services.md#2311-categories)); neither is NotProven.

```kimi
open struct Base
    public var count: i32 = 0
    public init() => ()
    public virtual func g(self: objref/Self) -> i32 => 0
    public virtual func f(self: objref/Self) -> i32 => 0
    public func bump(self: uniq/Self) => self.count += 1            // Proven.
    public func reset(self: uniq/Self) => self@follow = Base.init() // NotProven.

struct Derived<U> : Base
    var item: U
    override func f(self: objref/Self) -> i32
        return self.g()   // Projection of lexical self: covered by the slot's evidence.

func use(d: uniq/Derived<i32>)
    d.bump()
    d.reset()  // Error: would replace the base inside Derived.
```

### 12.7.1. Effect rules

Every admitted Type and Origin binding is verified under the declaration's Signature, Constraints and premises ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)–[§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)), never under Constraints inferred from the body, and every Type-checked path counts.

**Storage relations.** An effect's target is related to a receiver or input root as Whole (the root), Base (base edges only), Part (through a Field, Tuple or array element), Separate (proven disjoint from the root by the Place-overlap rules of [§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)) or MayAlias (any relation that is neither Whole, Base, Part nor Separate). A referent reached through a stored reference is related by following its Loan provenance ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)). Separation from one root proves nothing about another, and a payload follow does not turn Whole into Part or Separate.

| Effect on the receiver | Result |
| --- | --- |
| Replace, reconstruct or take ownership of Whole or Base | Violation, unless the target is proven complete and the operation is legal under [§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates) |
| A Move out of it that leaves it incomplete | Violation, even if restored later |
| Part Replacement or Exchange; permitted reads and borrows; effects on a Separate target | No violation |
| Escape of unrestricted exclusive access to it or a base, through a result, a store, a capture or a callee | Violation, unless a legal complete-target borrow |
| MayAlias, or unsafe access that may reach it | Unproven effect |
| Call, accessor, default, cleanup | Callee effects mapped through arguments, captures and mutable-static anchors |

A callee's whole replacement mapped onto a caller's Part keeps that target's base-subobject restrictions, and a permitted effect stays permitted on a target that may alias.

**Reached operations.** Only operations reached through the receiver and what derives from it count: the receiver's Loan is live for the whole call ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)), so an operation on its storage through any other path (another argument, a capture, a mutable-static anchor, a callee with unknown effects) is rejected at that call ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)), or is undefined behavior for raw access ([§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)). A `ref/Self` receiver reaches only reads and shared borrows, so an operation with one, including every custom or computed `get` and shared standard witness bridge ([§11.9.2](11-properties.md#1192-standard-access-witnesses)), is Proven exactly when it is valid.

**Exclusive input bound.** A callee affects its inputs at most as [§8.11.4](08-generics-constraints-and-contracts.md#8114-checking-and-use) rule 1 permits, and its results keep the dependencies its signature states (by the mutable-static anchor rule of [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). A `ref` input has only read and shared-borrow effects ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)). On an exclusive input this is at most one completeness-preserving Replacement of the actual target, unsafe code included ([§5.2.3](05-raw-pointers-and-unsafe-memory.md#523-taking-from-raw-places)), and unproven effects inside a callee contribute no more. `Kimi.Intrinsics.replace` and `exchange` are such a Replacement and `swap` an Exchange ([§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates)). The bound preserves a receiver Part and violates its Whole or Base unless the operation carries the virtual slot completeness guarantee ([§6.4.5](06-declarations-and-containers.md#645-receiver-safety)); that guarantee excuses only this bound, and the call's other effects and Loans still count.

**Calls.** Effects propagate to the least fixed point over recursion, with the summary domain of [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls); recursion alone is not unproven. Indirect and generic-requirement calls use requirement effect bounds ([§8.11](08-generics-constraints-and-contracts.md#811-effect-bounds)), and calls through Callable-constrained parameters compose through the actual arguments and their anchors ([§8.11.6](08-generics-constraints-and-contracts.md#8116-callable-effect-bounds)). Without a validated summary or requirement effect bound, a call contributes the exclusive input bound and, for static storage, the unknown effects of [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls). A receiverless function has input-root effects but no status. The fixed point never proves a circular conformance; Finality applies as usual.

### 12.7.2. Implementation families

An implementation passes [§12.7.1](#1271-effect-rules) when no admitted binding yields a Violation or an Unproven effect. An operation is Proven exactly when its ordinary body and every implementation in its explicit-specialization set pass, whether or not an implementation is called; otherwise it is NotProven. The set is closed after environment selection and declaration collection. The ordinary body is verified over all admitted bindings, without subtracting specialized ones, and each specialization under its substituted contract. An incompatible specialization makes the operation NotProven but is not an error for that reason; its declared Signature, Constraint, Safety and Effect obligations still apply. A use is checked against the original public contract before an implementation is selected ([§8.13.3](08-generics-constraints-and-contracts.md#8133-selection)).
