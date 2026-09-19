# 7. Functions and callable values

[Specification index](../SPEC.md)

A function declaration begins with `func`, followed by its Name, optional generic parameters, optional Origin parameters and a parenthesized parameter list. A result Type follows `->`; omitting it in a named function means Unit (`()`), regardless of accessibility or body form. Definitions use the common Body forms (§7.1). Anonymous functions have separate [inference rules](#761-syntax-and-inference).

The declared function Name is a single, unqualified Name, and the declaration belongs to the lexical Container or executable scope in which it appears. A member is declared inside the relevant Container body, including a permitted fragment; `func View.get(...)` and other qualified declaration names are compile-time errors. A qualified declaration cannot attach a function to another Container, introduce an extension, or obtain that Container's private access or generic bindings. Qualified Names at use sites and explicit receivers follow their own rules.

A function declared in a group nested inside a Type is still static. Its lexical `Self` names the nearest outer Self context without creating a receiver. Inherited parameters participate in generic eligibility and definition checking (§6.1.3), and outer arguments at a qualified call must already be bound (§9.6.1).

## 7.1. Function bodies and results

Functions use the common single-item or indented Body (§14.2). A named function's omitted return Type is Unit; neither its body nor its callers infer that signature. A generic definition is bound under its declared Types and Constraints; the body is not reinterpreted at instantiation.

In a single-item body, the expression is discarded if the return Type is already fixed as Unit; otherwise its normal value is the return result. An explicit `return` always has to fit the return Type. A statement supplies Unit only if it structurally completes normally.

In an indented body, every direct expression, including the last, is discarded, and structural arrival at the end supplies Unit. A non-Unit result therefore requires an explicit `return`. Expected Types and all written result sources, including unreachable ones, follow §14.9. Runtime Reachability does not change a function's fixed return Type.

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

A final selection, loop or do expression in an indented body is still discarded; return that expression, or return inside its paths. Anonymous return inference and its fixed-context boundary follow §7.6.1 and §10.5. Other function-like targets are listed in §14.5.3; all of them use the common Scope Exit (§16.2).

## 7.2. Parameters and defaults

Explicit parameters are initialized, immutable `let`-like bindings, including anonymous-function, constructor and receiver parameters; a setter's `value` is also immutable. A Non-Copy parameter may be Moved once, but reassignment, reinitialization and new exclusive borrows of parameter storage are forbidden; use a `var` local for mutable work. An existing `uniq/T` or `objuniq/T` parameter still permits exclusive access to, and Reborrow of, its referent, because binding immutability does not restrict the referent. Construction and destruction receivers keep their special privileges. There is no `var` parameter syntax.

**Argument names.** A parameter may separate its external argument name from its local name with `external => internal: T`.

**Optional parameters.** An optional parameter is written `name?: T = defaultExpression`. The `?` requires a default and means that the argument may be omitted, not that the Type is nullable; a default alone does not make a parameter optional. A required parameter's initializer permits no omission and never runs.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
let result = scale(3, by: 4)

func offset(value: i32, by?: i32 = 1) -> i32 => value + by
let next = offset(3)
let adjusted = offset(3, by: 5)
```

Function Types keep neither argument names nor defaults, and calls through function values supply all arguments positionally. Argument matching and evaluation are described under [invocation](12-expressions.md#1242-invocation-and-generic-application).

**Default evaluation and ownership.** At each call, the explicit arguments are acquired in source order into pending slots. Then each omitted default is evaluated once, in parameter declaration order, in the declaration's scope with access to the prepared preceding slots; it may refer to preceding parameters but not to later parameters or caller-local bindings. Slots do not alias caller variables. A default may Copy a preceding Copy value or inspect it through temporary shared access, but cannot Move, Consume or modify that slot or its owned contents. Its result cannot retain a new Borrow or Reborrow of a preceding argument, but may Copy an existing shared borrow that has external dependencies. Temporary inspection Loans end before the callee is entered. Thus `func f(x: string, y?: string = x) => ()` is invalid, while stringifying a temporary shared borrow of `x` may produce an independent string.

Every default is checked at declaration time, even if all calls supply the argument. Defaults give no evidence for generic inference, and their transfers may target only constructs inside the default.

A normal transfer that abandons argument evaluation destroys the still-owned prepared values and temporaries in reverse acquisition order and skips the callee; Abort does not unwind. After successful preparation, ownership passes from the pending slots to the initialized parameters, and callee cleanup uses reverse parameter order.

## 7.3. Explicit receivers

A function declared directly in a struct or enum, or a Contract function requirement, is an **instance function** exactly when one parameter's **internal Name** is `self`; otherwise it is a **Type function**. At most one `self` is allowed, at any written position, with no rename, default or optional marker. Its normalized Type must be `Self`, `ref/Self`, `uniq/Self` or a permitted object-Semantics `Self` form; unrelated targets, extra reference layers, raw pointers and unconstrained generic receiver Semantics are rejected. Origin annotations follow the normal parameter rules. In groups, rootgroups and local functions without an active contextual `self`, a parameter named `self` has no instance-member meaning.

**Receiver shorthand.** A receiver written as bare `self` is shorthand for `self: ref/Self`. This fixes a shared-borrow receiver before the body is checked; there is no body-based Type or ownership inference. The shorthand is allowed only at an instance function's receiver position, including Contract requirements and full specializations, and keeps that written parameter position; all other named function parameters require explicit Types. Group and rootgroup functions, local functions and constructors cannot use it, and anonymous functions keep their own parameter-inference rules. An explicit receiver Type, such as `uniq/Self` or owning `Self`, overrides the default, and explicit Origin annotations require the typed form. The shorthand has the same Signature, input Origin, callable Type and invocation rules as its expansion. Omitting the receiver parameter entirely still declares a Type function. (Accessor receivers have their own shorthand; see §11.2.)

```kimi
struct Meter
    var measured: i32
    func read(self) -> i32 => self.measured
    func update(self: uniq/Self, value: i32) => self.measured = value
    func constant() -> i32 => 0 // Type function: no receiver.
```

**Method calls.** For `receiver.method(arguments)`, the receiver is evaluated and adapted first, regardless of its parameter position, and recorded at the declared position of `self`. The explicit positional and named arguments are matched against the remaining parameters in their written order; `self` cannot also be supplied by an argument label. Defaults then follow the ordinary order. Cleanup inside the callee still uses the full written parameter order.

**Unbound references.** A Type-qualified instance function reference is unbound: a call through `Type.method` supplies all parameters explicitly in their written positions, including `self` at its declared position, with the ordinary argument order and receiver compatibility checks. An unbound function value likewise keeps `self` as an ordinary position of its callable signature, captures no receiver and remains subject to the unsafe-function restrictions. `value.method` without invocation does not form a bound-method value in this revision. None of this introduces extension functions, implicit `self` lookup, or a conversion for an otherwise incompatible object receiver.

## 7.4. Function constraints

A generic function with an indented body may begin that body with [Constraints](08-generics-constraints-and-contracts.md#82-constraints). Its Constraint Clauses must precede every executable body item; they are processed at compile time and are not executable expressions.

Before lookup, the maximal leading sequence of unparenthesized `ConstraintSubject is IsRequirement` items is parsed as Constraint Clauses. A subject not permitted for the function is an error, never a fallback runtime test. Blank lines and comments do not end the prefix. Parenthesizing the whole test, as in `(value is Dog)`, makes it an executable expression item and ends the prefix, so subsequent value tests are executable. A later clause rooted in a generic parameter is a misplaced-Constraint error. In a nongeneric function the prefix rule does not apply, and `value is Dog` is an expression. Dedicated Type and Contract Constraint regions keep their own rules, even through parentheses.

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

Each clause subject must name a generic parameter of the function or an [associated-Type projection](08-generics-constraints-and-contracts.md#843-associated-types) rooted in one. Leading Constraints are collected before projections in the function signature are resolved; this does not waive Constraint validation. In the example, the two clauses together form the function's Constraints. Function requirements use the [same indented placement](08-generics-constraints-and-contracts.md#841-function-requirements) with a Constraints-only region instead of an executable body.

Every explicit or inferred generic argument at a call site must satisfy the clauses, and body Type checking and instantiation may rely on them. Constraints are not part of the function Signature; declarations that differ only in their Constraints conflict.

## 7.5. Unsafe functions

Use the [`safety` documentation item](02-source-and-lexical-structure.md#235-writing-and-extracting-items) to describe the conditions below. It adds no automatic proof or new calling permission.

An **unsafe function**, declared with `unsafe func`, requires its caller to satisfy documented memory-safety conditions for their documented duration. Calling it requires an [Unsafe Block](14-control-flow.md#1433-unsafe-block), and violating its safety contract is undefined behavior. This runtime safety contract is distinct from Constraints.

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

`unsafe` is not part of the Signature and does not distinguish overloads. Overload resolution ignores the caller's unsafe context; the selected call's requirement is checked afterward, and another overload is never substituted because the selected one is unsafe.

Initially, unsafe functions support direct calls only. Taking one as a function value, assigning it to a variable, passing it as an argument or converting it to an ordinary Function Type is forbidden. Unsafe Function Types are not specified in this revision (§5.6).

```kimi
let reader = read // Error: an unsafe function cannot be taken as a function value.
```

## 7.6. Function expressions

### 7.6.1. Syntax and inference

An anonymous function consists of `func`, an optional Capture List, parameters, an optional result annotation and a common Body (§7.1). There is no bare `(x) => x` form and no external parameter labels, defaults, optional parameters or generic lambdas. The body's expectation and use or discard context are fixed before it is checked (§10.5).

```kimi
let twice = func (value: i32) => value * 2
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2 // Error: no fixed input signature.
```

An omitted parameter Type requires a fixed expected callable signature; parameter Types are never searched for from body operations or later uses. The result is inferred from that expectation or, once the parameters are fixed, from the body under normal result validation. Whole-result inference also keeps the [result Origins and Loans](15-ownership-and-lifetime-analysis.md#1582-closure-dependencies-and-call-results); annotations use the existing elision rules.

Creation evaluates the captures, not the body; capture acquisition occurs in the creation context. Invocation evaluates the body under an independent Function Boundary, with no outer `return`/`exit`/`continue`/`yield` targets and no inherited Unsafe permission. Named nested functions keep their no-capture restriction.

### 7.6.2. Capture acquisition and environment

| List or entry | Creation effect |
| --- | --- |
| No list | Infer the needed outer runtime bindings; Copy only when each complete Type is Copy |
| `[]` | Prohibit runtime captures |
| `[x, y]` | Acquire exactly the listed bindings; unlisted outer runtime bindings are unavailable |
| `x` | Ordinary Copy if Copy, otherwise Move |
| `x@ref` / `x@uniq` | The existing value Borrow, Copy or Reborrow operation for that Semantics |
| `var x` | Ordinary acquisition into a mutable environment binding |

Captures are resolved by Binding Identity. An omitted list never infers a Move, a new external Borrow or Reborrow, or a partial capture: a Non-Copy root is rejected even when only a Copy Field is read. An existing `ref/T` may be copied with its dependencies. Generic implicit capture requires declared Copy evidence at definition checking; unknown Copy is an error, not deferred checking, an inferred Move or a hidden Constraint. An explicit `[x]` can admit Copy or Move when the body and later source uses are valid for both.

Type names and accessible static function declarations are not runtime captures. Contextual `self` and a setter's `value` are never implicitly captured, and explicit captures of them obey all receiver, accessor, construction and destruction restrictions. The contextual `storage` binding cannot be captured by name; ordinary bindings named `storage` elsewhere follow the normal capture rules. No runtime receiver is implicitly bound into a function reference.

Explicit captures execute left to right, including unused entries, and earlier Moves and Loans affect the legality of later entries. Duplicate capture names and collisions with parameters are rejected. Inferred captures execute once each, in order of first occurrence in selected source, including dependencies needed by nested Closures. Excluded compile-time source contributes no capture; a legitimate deferred selection must fix the capture set, order and effects before environment and ownership finalization. Runtime reachability and optimization do not alter the set.

```kimi
let number: i32 = 10
let copied = func () => number             // Copy capture.
let text = makeText()                      // Assume string.
let invalid = func () => text              // Error: explicit list required.
let holder = func [text] () => ()      // Move executes even if unused.
```

Capture targets are binding names only. There are no aliases, initializer expressions, field targets, inter-entry references, `@copy` form or additional object-borrow capture syntax. `var` combines only with ordinary acquisition, not with `@ref` or `@uniq`. Capturing an existing reference follows ordinary adaptation:

| Source | `[x]` | `[x@ref]` | `[x@uniq]` |
| --- | --- | --- | --- |
| `ref/T` | Copy the reference | Copy the reference, without adding a reference layer | Error |
| `uniq/T` | Move the reference | Shared Reborrow | Exclusive Reborrow |

Captures without `var` create `let`-like environment bindings, regardless of the source's mutability or of the binding holding the Closure. Value capture takes a snapshot; no shared heap box is created automatically. A captured exclusive reference can mutate its referent given adequate call access, but assignment to the capture name is not rewritten as assignment to the referent. `var` changes binding mutability only, not deep copying, Copy classification or Origin dependencies.

```kimi
let count: i32 = 0
var next = func [var count] () -> i32
    count += 1
    return count
let first = next()  // 1
let second = next() // 2; outer count is still 0.
```

Environment bindings are not user Fields. Ownership-bearing calls apply ordinary local acquisition and Move Paths, and a consumed `let` cannot be reinitialized. Shared and Exclusive calls cannot move owned captures out. No environment may borrow its own owned capture through another capture; external borrowed dependencies remain legal under the lifetime rules.

**Nested Closures** acquire through every enclosing environment. A binding free only in the inner Closure still has to be captured by the outer one; an outer `[]` or an insufficient explicit list is an error. Every omitted-list boundary independently requires Copy. Outer parameters and body locals need capturing only when the inner Closure is created. Moving an outer environment value into an inner Closure makes the outer call Consuming, and an inner `@uniq` cannot exceed the access of the outer binding.

```text
lexical x -> outer capture x -> inner capture x
            creation of outer  execution of outer, creating inner
```

### 7.6.3. Call receiver and acquisition

Each concrete Closure has one minimum **Call Receiver Requirement**, inferred from all resolved operations in selected source:

| Minimum requirement | Internal receiver | Body access |
| --- | --- | --- |
| Shared | `ref/Self` | Shared access to the environment |
| Exclusive | `uniq/Self` | Exclusive mutation of the environment or captured referents |
| Consuming | `owner/Self` | Moving values out of the call's environment |

These are the access requirements of one body, not three independently selected implementations. A Move on any possible body path requires a Consuming call. Legitimate generic effects are resolved before the requirement is finalized. Overload resolution is not rerun per receiver, `let` and Property permissions are not relaxed, and ownership cannot rescue an otherwise invalid body. The internal `call` and receiver notation introduce no source member or hidden `self` name.

Direct calls acquire the minimum receiver under the normal evaluation, access, initialization and Loan rules. An Exclusive call requires a writable owner, an existing exclusive borrow or a writable temporary; a captured `uniq` alone does not grant exclusive access to a `let`-owned Closure. A Consuming call ordinarily Copies a Copy Closure or Moves a Non-Copy one; no special forced Move is inserted. Borrowed access cannot Move an unowned environment, although a permitted Copy may provide a separate owned call value.

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

The internal call signature keeps the complete receiver, parameter and result Types, per-call Origins, fixed captured Origins and result Loan dependencies. Receiver access is acquired before later arguments are evaluated, and the required Loans last through the uses of the result. Generic calls follow the declared [Callable receiver](08-generics-constraints-and-contracts.md#86-callable-constraints), even when instantiation reveals a weaker body requirement.

### 7.6.4. Function references and common-type conversion

A resolved function reference produces its Function Item Type, including its bound generic arguments and Origin contract; different declarations remain distinct. A Function Item is Copy and Shared-callable, is Owned when its bound arguments satisfy §15.2.3, and does not erase borrowed parameter or result contracts. A runtime method receiver is never bound automatically; explicit receiver arguments are required. Unsafe functions and `deinit` cannot be acquired as values.

```kimi
func add(x: i32, y: i32) -> i32 => x + y
let item = add                         // Concrete Function Item; Copy.
let erased: (i32, i32) -> i32 = add    // Common owned value; Non-Copy.
```

At an initialization, argument or return position with a fixed expected common Function Type, a Function Item or concrete Closure is implicitly converted exactly when its signature fits, its minimum receiver is Shared, its complete environment is Owned, its result does not borrow the hidden environment receiver, all public Origin and Loan contracts hold, and ordinary source acquisition is legal. Non-static capture environments and Exclusive- or Consuming-only bodies are rejected; no dependency may be erased to force conformance.

The existing environment is acquired by normal Copy or Move; conversion never rereads outer bindings or repeats captures. The resulting owned common value is always Non-Copy. Acquiring it again as the same common Type is an ordinary Move, not a new erasure. Shared invocation does not consume it, and borrowing it as `uniq/F` still exposes only Shared call.

```kimi
func makeAdder(offset: i32) -> (i32) -> i32
    return func [offset] (value) => value + offset

let callback = makeAdder(10)
let next = callback        // Move the common value.
callback(5)               // Error: Moved.
let result = next(5)      // 15.
```

Erasure is an owning-container conversion, distinct from source Copy or Move; it does not change the definition of Copy. No target-independent layout, allocation count or inlining is guaranteed. The Windows storage and conversion rules are in §21.2.5, separately from the function ABI; allocation elision there preserves acquisition, validity and cleanup under that section's failure-observability rule. A required allocation failure causes ordinary Abort Termination, with no Move rollback and no normal cleanup guarantee. Concrete `Callable` use creates no erased container, but it does not promise zero runtime cost.
