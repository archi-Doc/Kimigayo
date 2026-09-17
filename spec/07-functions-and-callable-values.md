# 7. Functions and callable values

[Specification index](../SPEC.md)

A group nested inside a Type still declares static functions. Its lexical Self names the nearest outer Self context without creating a receiver. Inherited parameters participate in generic eligibility and definition checking (§6.1.3); outer arguments at a qualified call must already be bound (§9.6.1).

A function begins with `func`, followed by its Name, optional generic parameters, optional Origin parameters, and a parenthesized parameter list. A result Type follows `->`; omitting it in a named function means Unit (`()`), regardless of accessibility or body form. Definitions use the common Body forms (§7.1). Anonymous functions retain their separate [inference rules](#761-syntax-and-inference).

The declared function Name is a single, unqualified Name. Its declaration belongs to the lexical Container or executable scope in which it appears. Declare a member inside the relevant Container body, including a permitted fragment; `func View.get(...)` and other qualified function declaration names are compile-time errors. A qualified declaration cannot attach a function to another Container, introduce an extension, or obtain that Container's private access or generic bindings. Qualified Names at use sites and explicit receivers remain governed by their existing rules.

Explicit parameters are initialized, immutable let-like bindings, including anonymous-function, constructor, and receiver parameters; setter value is also immutable. Non-Copy values may Move once, but parameter reassignment, reinitialization, and new exclusive storage borrows are forbidden. Use a var local for mutable work. Existing uniq/T or objuniq/T still permits exclusive referent access/Reborrow; binding immutability does not restrict its referent. Construction/Destruction receivers keep their special privileges. There is no var parameter syntax.

## 7.1. Function bodies and results

Functions use the common single-item or indented Body (§14.2). A named function's omitted return Type is Unit; its body and callers do not infer that signature. Bind a generic definition under its declared Types and Constraints without reinterpreting the body at instantiation.

In a single-item body, an expression is discarded if the return Type is already fixed as Unit; otherwise its normal value is the return result. An explicit return always fits the return Type. A statement supplies Unit only if it structurally completes normally.

Direct expressions in an indented body, including the last, are discarded. Structural end arrival supplies Unit. Non-Unit results require explicit return; expected Types and all written result sources, including unreachable ones, follow §14.9. Runtime Reachability does not change a function's fixed return Type.

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

A final selection, loop, or do expression is still discarded in an indented body. Use return with that expression or returns inside its paths. Anonymous return inference and its fixed-context boundary follow §7.6.1 and §10.5. Other function-like targets are listed in §14.5.3; all use common Scope Exit (§16.2).

## 7.2. Parameter names and defaults

A parameter may separate its external argument name from its local name with `external => internal: T`. An optional parameter uses `name?: T = defaultExpression`. The `?` requires a default and means argument omission, not a nullable Type; a default alone does not make a parameter optional.

At each call, evaluate omitted defaults once in parameter declaration order, after all explicit arguments. Resolve default expressions in the declaration's scope. They may refer to preceding parameters, but not later parameters or caller-local bindings.

```kimi
func scale(value: i32, by => factor: i32) -> i32 => value * factor
let result = scale(3, by: 4)

func offset(value: i32, by?: i32 = 1) -> i32 => value + by
let next = offset(3)
let adjusted = offset(3, by: 5)
```

Function Types retain neither argument names nor defaults. Calls through function values supply all arguments positionally. See [invocation](12-expressions.md#1242-invocation-and-generic-application) for argument matching and evaluation.

**Default evaluation and ownership.** Acquire explicit arguments in source order into pending slots, then evaluate omitted defaults in parameter order using the declaration environment and prepared preceding slots. Slots do not alias caller variables. A default may Copy a preceding Copy value or inspect it through temporary shared access; it cannot Move/Consume or modify that slot or its owned contents. Its result cannot retain a new Borrow/Reborrow of a preceding argument, but may Copy an existing shared borrow with external dependencies. End temporary inspection Loans before entering the callee. Thus `func f(x: string, y?: string = x) => ()` is invalid; stringifying a temporary shared borrow of x may produce an independent string.

Check every default at declaration time, even if all calls supply the argument. A required parameter’s initializer permits no omission and does not run when supplied. Defaults give no generic-inference evidence; their transfers may target only constructs inside the default.

A normal transfer that abandons argument evaluation destroys still-owned prepared values and temporaries in reverse acquisition order and skips the callee. Abort does not unwind. After successful preparation, ownership passes from pending slots to initialized parameters; callee cleanup uses reverse parameter order.

## 7.3. Explicit receivers

A function directly in a struct/enum, or a Contract function requirement, is an instance function exactly when a parameter’s **internal Name** is self; otherwise it is a type function. Allow at most one self, at any written position, with no rename, default, or optional marker. Its normalized Type must be Self, ref/Self, uniq/Self, or a permitted object-Semantics Self form. Reject unrelated targets, extra reference layers, raw pointers, and unconstrained generic receiver Semantics. Origin annotations follow normal parameter rules. In groups/rootgroups or local functions without active contextual self, the parameter name self has no instance-member meaning.

A receiver written as bare `self` is shorthand for `self: ref/Self`. This fixes a shared-borrow receiver before checking the body; no body-based Type or ownership inference occurs. It is allowed only at an instance function's receiver position, including Contract requirements and full specializations, and retains that written parameter position. All other named function parameters require explicit Types. Group/rootgroup functions, local functions, and constructors cannot use this shorthand; anonymous functions retain their separate parameter-inference rules. An explicit receiver Type overrides the default, including `uniq/Self` or owning `Self`; explicit Origin annotations require the typed form. The shorthand has the same Signature, input Origin, callable Type, and invocation rules as its expansion. Omitting the entire receiver parameter from an ordinary function still declares a type function.

```kimi
struct Meter
    var measured: i32
    func read(self) -> i32 => self.measured
    func update(self: uniq/Self, value: i32) => self.measured = value
    func constant() -> i32 => 0 // Type function: no receiver.
```

For `receiver.method(arguments)`, evaluate and adapt the receiver first, recording it at the self parameter's declared position. Match the explicit positional/named arguments against the remaining parameters in their written order; self cannot also be supplied by an argument label. Defaults then follow ordinary order. The source receiver is evaluated first regardless of its parameter position. Cleanup inside the callee still uses the full written parameter order.

A Type-qualified instance function reference is unbound: a call through `Type.method` supplies all parameters explicitly in their written positions, including self at its declared position, with ordinary argument order and receiver compatibility checks. An unbound function value likewise retains self as an ordinary position in its callable signature; it implicitly captures no receiver and remains subject to unsafe-function restrictions. `value.method` without invocation does not form a bound-method value in this revision. This does not introduce extension functions, implicit self lookup, or a conversion for an otherwise incompatible object receiver.

## 7.4. Function constraints

A generic function with an indented body may begin it with [Constraints](08-generics-constraints-and-contracts.md#82-constraints). Its Constraint Clauses must precede every executable body item and are processed at compile time; they are not executable expressions.

Before lookup, parse the maximal leading sequence of unparenthesized `ConstraintSubject is IsRequirement` items as Constraint Clauses. A subject not permitted for this function is an error, not a fallback runtime test. Blank lines and comments do not end this prefix. Parenthesizing the whole test, as `(value is Dog)`, makes it an executable expression item and ends the prefix; subsequent ordinary value tests are executable. A later clause rooted in a generic parameter remains a misplaced-Constraint error. In a nongeneric function body this prefix rule does not apply, and `value is Dog` is an expression. Dedicated Type/Contract Constraint regions retain their own rules even through parentheses.

**Basic example.**

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

Each clause subject must name a generic parameter of that function or an [associated-Type projection](08-generics-constraints-and-contracts.md#843-associated-types) rooted in one. Collect leading Constraints before resolving projections in the function signature; this does not waive Constraint validation. In this example, the two clauses jointly form its Constraints. Function requirements use the [same indented placement](08-generics-constraints-and-contracts.md#841-function-requirements) with a Constraints-only region, not an executable body.

Every explicit or inferred generic argument at a call site must satisfy its clauses. Body type checking and instantiation may rely on those requirements. Constraints are not part of the function Signature; declarations differing only in their Constraints conflict.

## 7.5. Unsafe functions

An **unsafe function**, declared with `unsafe func`, requires its caller to satisfy documented memory-safety conditions for their documented duration. Calling it requires an [Unsafe Block](14-control-flow.md#1433-unsafe-block); violating its safety contract is undefined behavior. This runtime safety contract is distinct from Constraints and their Constraint Clauses.

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

`unsafe` does not affect the Signature or distinguish overloads. Resolve overloads without considering the caller's unsafe context, then check the selected call's requirement. Never substitute another overload because the selected function is unsafe.

Initially, unsafe functions support direct calls only. Taking a function value, assigning it to a variable, passing it as an argument, or converting it to an ordinary Function Type is forbidden. Unsafe Function Types are specified separately.

```kimi
let reader = read // Error: an unsafe function cannot be taken as a function value.
```

## 7.6. Function expressions

### 7.6.1. Syntax and inference

An anonymous function requires `func`, an optional Capture List, parameters, an optional result annotation, and a common Body (§7.1). No bare `(x) => x`, external parameter labels, defaults, optional parameters, or generic lambdas are introduced. Fix the body's expectation and use/discard context before checking it (§10.5).

```kimi
let twice = func (value: i32) => value * 2
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2 // Error: no fixed input signature.
```

An omitted parameter Type requires a fixed expected callable signature. Do not search parameter Types from body operations or later uses. Infer the result from that expectation or, after parameters are fixed, the body under normal result validation. Whole-result inference also retains [result Origins and Loans](15-ownership-and-lifetime-analysis.md#1582-closure-dependencies-and-call-results); annotations use existing elision.

Creation evaluates captures, not the function body. Invocation evaluates that body under an independent Function Boundary: no outer return/exit/continue/yield targets or inherited Unsafe permission. Capture acquisition itself occurs in the creation context. Named nested functions retain their no-capture restriction.

### 7.6.2. Capture acquisition and environment

| List or entry | Creation effect |
| --- | --- |
| No list | Infer needed outer runtime bindings; Copy only when each complete Type is Copy |
| `[]` | Prohibit runtime captures |
| `[x, y]` | Acquire exactly the listed bindings; unlisted outer runtime bindings are unavailable |
| `x` | Ordinary Copy if Copy, otherwise Move |
| `x@ref` / `x@uniq` | The existing value Borrow/Copy/Reborrow operation for that Semantics |
| `var x` | Ordinary acquisition into a mutable environment binding |

Resolve captures by Binding Identity. An omitted list infers no Move, new external Borrow/Reborrow, or partial capture: reject a Non-Copy root even when only a Copy Field is read. Existing ref/T may be copied with its dependencies. Generic implicit capture requires declared Copy evidence at definition checking; unknown Copy is an error, not deferred checking, inferred Move, or a hidden Constraint. Explicit `[x]` can admit Copy/Move when the body and later source uses are valid for both.

Type names and accessible static function declarations are not runtime captures. Contextual `self` and setter `value` are never implicitly captured; explicit captures obey all receiver, accessor, construction, and destruction restrictions. Contextual storage is not a binding-name capture target; ordinary bindings named storage elsewhere use normal capture rules. No runtime receiver is implicitly bound into a function reference.

Explicit captures execute left to right, including unused entries. Earlier Moves and Loans affect later legality. Reject duplicate capture names and collisions with parameters. Inferred captures execute once each in order of first occurrence in selected source, including dependencies needed by nested Closures. Excluded compile-time source contributes no capture; legitimate deferred selection must resolve the set, order, and effects before environment and ownership finalization. Runtime reachability and optimization do not alter that set.

```kimi
let number: i32 = 10
let copied = func () => number             // Copy capture.
let text = makeText()                      // Assume string.
let invalid = func () => text              // Error: explicit list required.
let holder = func [text] () => ()      // Move executes even if unused.
```

Capture targets are binding names only. No aliases, initializer expressions, fields, inter-entry references, `@copy`, or extra object-borrow capture syntax is defined. `var` combines only with ordinary acquisition, not `@ref`/`@uniq`. Existing reference capture follows ordinary adaptation:

| Source | `[x]` | `[x@ref]` | `[x@uniq]` |
| --- | --- | --- | --- |
| `ref/T` | Copy reference | Copy reference, without adding a reference layer | Error |
| `uniq/T` | Move reference | Shared Reborrow | Exclusive Reborrow |

Captures without `var` create `let`-like environment bindings, regardless of source mutability or the Closure's containing binding. Value capture takes a snapshot; no automatic shared heap box is created. A captured exclusive reference can mutate its referent with adequate call access, but assignment to its capture name is not rewritten as referent assignment. `var` changes binding mutability, not deep-copy, Copy classification, or Origin dependencies.

```kimi
let count: i32 = 0
var next = func [var count] () -> i32
    count += 1
    return count
let first = next()  // 1
let second = next() // 2; outer count is still 0.
```

Environment bindings are not user Fields. Ownership-bearing calls apply ordinary local acquisition and Move Paths; a consumed `let` cannot be reinitialized. Shared/Exclusive calls cannot move out owned captures. No environment may borrow its own owned capture through another capture; external borrowed dependencies remain legal under lifetime rules.

Nested Closures acquire through every enclosing environment. An inner-only free binding still requires the outer Closure to capture it; outer `[]` or an insufficient explicit list is an error. Every omitted-list boundary independently requires Copy. Outer parameters and body locals need capture only when the inner Closure is created. Moving an outer environment value into an inner Closure makes the outer call Consuming; inner `@uniq` cannot exceed the outer binding's access.

```text
lexical x -> outer capture x -> inner capture x
            creation of outer  execution of outer, creating inner
```

### 7.6.3. Call receiver and acquisition

Each concrete Closure has one minimum **Call Receiver Requirement**, inferred from all resolved operations in selected source:

| Minimum requirement | Internal receiver | Body access |
| --- | --- | --- |
| Shared | `ref/Self` | Shared access to the environment |
| Exclusive | `uniq/Self` | Exclusive mutation of environment or captured referents |
| Consuming | `owner/Self` | Move values out of the call's environment |

These are one body's access requirements, not three independently selected implementations. A Move on any possible body path requires Consuming call. Resolve legitimate generic effects before finalizing the requirement. Do not rerun overload resolution for each receiver, relax `let` or Property permissions, or rescue an otherwise invalid body with ownership. Internal `call` and receiver notation introduce no source member or hidden `self` name.

Direct calls acquire the minimum receiver under normal evaluation, access, initialization, and Loan rules. Exclusive calls require a writable owner, an existing exclusive borrow, or a writable temporary. A captured `uniq` alone does not grant exclusive access to a `let`-owned Closure. Consuming calls ordinarily Copy a Copy Closure or Move a Non-Copy one; no special forced Move is inserted. Borrowed access cannot Move an unowned environment, although a normally permitted Copy may provide a separate owned call value.

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

The internal call signature retains complete receiver/parameter/result Types, per-call Origins, fixed captured Origins, and result Loan dependencies. Acquire receiver access before later argument evaluation and keep required Loans through the result's uses. Generic calls follow the declared [Callable receiver](08-generics-constraints-and-contracts.md#86-callable-constraints), even when instantiation reveals a weaker body requirement.

### 7.6.4. Function references and common-type conversion

A resolved function reference produces its Function Item Type, including its bound generic arguments and Origin contract. Different declarations remain distinct. A Function Item is Copy and Shared-callable, is Owned when its bound arguments satisfy §15.2.3, and does not erase borrowed parameter/result contracts. A runtime method receiver is not automatically bound; follow explicit receiver argument rules. Unsafe functions and `deinit` cannot be acquired as values.

```kimi
func add(x: i32, y: i32) -> i32 => x + y
let item = add                         // Concrete Function Item; Copy.
let erased: (i32, i32) -> i32 = add    // Common owned value; Non-Copy.
```

At an initialization, argument, or return position with a fixed expected common Function Type, implicitly convert a Function Item or concrete Closure exactly when its signature fits, its minimum receiver is Shared, its complete environment is Owned, its result does not borrow the hidden environment receiver, all public Origin/Loan contracts hold, and ordinary source acquisition is legal. Non-static capture environments and Exclusive/Consuming-only bodies are rejected; no dependency may be erased to force conformance.

Acquire the existing environment by normal Copy/Move; conversion never rereads outer bindings or repeats captures. The resulting owned common value is always Non-Copy. Acquiring the same common Type again is ordinary Move, not a new erasure. Shared invocation does not consume it; borrowing it as `uniq/F` still exposes only Shared call.

```kimi
func makeAdder(offset: i32) -> (i32) -> i32
    return func [offset] (value) => value + offset

let callback = makeAdder(10)
let next = callback        // Move the common value.
callback(5)               // Error: Moved.
let result = next(5)      // 15.
```

Erasure is an owning-container conversion distinct from source Copy/Move; it does not change the definition of Copy. No target-independent layout, allocation count or inlining is guaranteed. The Windows storage/conversion rules are specified in §21.2.5, separately from function ABI. Allocation elision preserves acquisition, validity and cleanup under that section's explicit failure-observability rule. Required allocation failure causes ordinary Abort Termination, with no Move rollback or normal cleanup guarantee. Concrete `Callable` use creates no such erased container, but does not promise zero runtime cost.
