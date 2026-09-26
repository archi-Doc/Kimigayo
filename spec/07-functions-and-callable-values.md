# 7. Functions and callable values

[Specification index](../SPEC.md)

A function declaration consists of `func`, a Name, optional generic parameters and a parenthesized parameter list. A result Type or a Place result (§7.1.1) may follow `->`; a named function that omits it returns Unit (`()`), whatever its accessibility or body form. Functions have no Origin parameter list: a directly written borrow annotation may introduce an implicit scalar Origin (§15.3.4). Declaration-attached `origin` relations precede the executable items, at the same indentation as Type Constraints. Bodies use the common Body forms (§7.1). Anonymous functions follow their own [inference rules](#761-syntax-and-inference).

The declared Name is a single unqualified Name, and the function belongs to the lexical Container or executable scope in which it appears. A member is declared inside its Container body, including a permitted fragment. Qualified declaration names such as `func View.get(...)` are errors: a declaration cannot attach a function to another Container, introduce an extension, or gain another Container's private access or generic bindings. Qualified Names at use sites and explicit receivers (§7.3) follow their own rules.

A function declared in a group nested inside a Type is static and has no receiver (§6.1.3.2). Inherited generic parameters take part in generic eligibility and definition checking (§6.1.3), and outer arguments at a qualified call must already be bound (§9.6.1).

## 7.1. Function bodies and results

Functions use the common single-item or indented Body; §14.2 defines how each form supplies its result. A named function's return Type comes from its declaration alone: its body, its callers and Runtime Reachability never change it. A generic definition is checked under its declared Types and Constraints, and its body is not reinterpreted at instantiation.

A single-item body returns the value of its expression, or discards it when the return Type is fixed as Unit. An indented body discards every direct expression, including the last, and supplies Unit at its structural end, so a non-Unit result requires an explicit `return`. Every explicit `return` must fit the return Type. Expected Types and all written result sources, including unreachable ones, follow §14.9.

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

A final selection, loop or `do` expression in an indented body is also discarded: return the expression, or return inside each of its paths. Anonymous return inference and its fixed-context boundary follow §7.6.1 and §10.5. Other function-like targets are listed in §14.5.3; all of them use the common Scope Exit (§16.2).

### 7.1.1. Place results

A function may publish a Place instead of returning a value:

```text
FunctionResult := Type
                | "place" "(" ("ref" | "uniq") "," Type ")" BorrowOrigin?
```

`place(ref, T)` and `place(uniq, T)` publish an existing, complete Storage whose stored complete Type is `T`. The shared form offers a shared borrow and, for a Copy value, a Copy. The exclusive form also offers exclusive borrows and replacement. Neither form offers Take. `T` is the stored Type: `place(ref, ref/Node)` publishes a slot holding a reference, and `@ref` on it gives `ref/(ref/Node)`. There is no `place(owner, T)`, `place(T)` or `place(ref/T)`.

A Place result is a result category, not a Semantics or a value Type. The same syntax is used in function declarations, anonymous functions with an explicit result, Function Types, Callable requirements and Contract requirements; an unannotated anonymous function never infers a Place result. In result position, an unqualified `place` followed by `(`, with or without whitespace between them, is always this syntax and never a Type name. A following `during` applies to the Place result; the Origins inside `T` are separate.

```kimi
func first<T>(values: ref/Array<T>) -> place(ref, T) during values
    return values[0]       // Publishes the element Place; nothing is acquired.

let view = first(resources@ref)@ref
inspect(view)
```

**Returning a Place.** The operand of a `return`, or the single-item body, of a function with a Place result designates a Place without acquiring a value. It is a Place expression or a call with a compatible Place result; an ordinary value is never materialized to satisfy a Place result. The slot of a local or by-value parameter that ends with the function cannot be returned, but external Storage reached through a local reference can. Every path that completes normally must designate a Place; a Never path supplies none. Structural fall-through supplies Unit, which does not satisfy even `place(ref, ())`. `if`, `match` and `do` do not produce Places, so each candidate Place is returned from its own branch.

The returned Storage's actual Type `A` and the published `T` must agree in Semantics, Core and structure. Their Origins are checked as the ordinary fitting of `ref/A` to `ref/T`, or of `uniq/A` to `uniq/T`, keeping invariant positions and the actual Loans. An exclusive Place may be published as a shared result, never the reverse. Function Types have no implicit conversion between result modes.

**Origins.** The outer Origin of a Place result is introduced, quantified and elided exactly like the outer Origin of the corresponding borrow result (§15.3.4, §15.4.3), and the body's dependencies are always checked. `during self` names the receiver's borrow Origin, not the slot of `self`; an accessor whose result does not depend on its search key writes it. An annotation such as `during self.source` never removes the actual receiver Loan. Place results do not bypass Function Type or Callable Origin rules, and specializations inherit the original contract.

**Contracts and identity.** Overloads that differ only in result mode cannot coexist. The published `T` is fixed from the arguments, explicit Type information and the expected Place contract. The use position is checked under the result adaptation of §10.3; an outer `@ref` never prefers a Place-returning candidate. Function references, Callable, Contract implementations, indirect calls and separate compilation preserve the result mode, capabilities, Origins and Loan contract. A Place result cannot be bound to an ordinary Type argument; a higher-order value API needs an explicit wrapper that returns an ordinary reference. Function Type and Callable compatibility require the same result mode and the corresponding reference contract, under the input contravariance, Origin quantification, Unsafe and Closure rules of §10.7. Contract implementation matching follows §8.4.5.

**Using a Place.** A published Place is acquired under §3.5 and §10.2: a Copy value may be read bare, a Non-Copy value cannot, `@move` is unavailable because the Place offers no Take, and a reference comes from an explicit borrow or from the common adaptation at a fixed expected Type.

```kimi
let view = table.get(key)@ref  // One search; the reference is kept.
inspect(table.get(key))        // Shared borrow at a ref/Resource parameter.
// let value = table.get(key)  // Error when Resource is Non-Copy.
```

A Place is used without acquiring a value when it is returned as a Place, is the operand of an explicit borrow or transfer, is projected, is a Subject or is an assignment target. Discarding an unacquired Place performs the call and its effects but destroys nothing in the published Storage. `_ = expression` instead acquires a value and destroys it, so it cannot discard a Non-Copy borrowed Place. Publishing an exclusive whole `T` permits replacing it with any valid `T`, so a Place through which an internal invariant could be broken is not published. A Dictionary publishes its values, but never an exclusive Place of a key.

## 7.2. Parameters and defaults

Explicit parameters, including anonymous-function, constructor and receiver parameters, are initialized, immutable `let`-like bindings; a setter's `value` is immutable too. A parameter may be transferred once with `@move`, but its storage cannot be reassigned, reinitialized or newly borrowed exclusively; use a `var` local for mutable work. There is no `var` parameter syntax. Binding immutability does not restrict a referent: a `uniq/T` or `objuniq/T` parameter still permits exclusive access to its referent and Reborrow of it. Construction and destruction receivers keep their special privileges.

### 7.2.1. Argument-name boundary

The parameter list of a named function, constructor or Contract function requirement may contain one `!` boundary in place of a comma. Ordinary parameters before it accept positional or named arguments; those after it require their external names. Without a boundary, every ordinary parameter accepts both forms. The boundary adds no parameter, Type modifier, evaluation phase or ABI slot.

The left section may be empty; the right section must contain at least one ordinary parameter, and a receiver does not count. Each section is comma-separated. A trailing comma is permitted only at the end of the whole list, never immediately before or after `!`. Parameter suffixes `name?` and `name!` are errors. An empty list is `()`, not `(!)`.

```kimi
func find(value: i32 ! start: i32, end: i32) => ()
func pair(a: i32, b: i32) => ()       // Both names may be omitted.
func configure(! count: i32 = 10) => () // Supplied count must be named.
find(10, start: 0, end: 100)
configure()
configure(count: 20)
configure(20) // Error: count requires its name.
```

In either section, `external => internal: T` separates the caller-facing name from the local binding. Renaming alone does not make the name required. External names must be unique across the whole list, including the receiver, regardless of defaults or calls; `func bad(! value => a: i32, value => b: i32)` is therefore invalid. Internal names are checked separately under §9.2, and an external name may equal another parameter's internal name.

For lexical and layout processing, a recognized boundary ends the preceding declaration or default expression and its delimiter region, as a comma does (§2.2.1). It cannot close an indented body on the same line, such as one inside a default that contains a selection, `do` expression or anonymous function: dedent first. The owning list is identified by syntactic containment, not by parenthesis depth alone; inner boundaries, literals, comments and `!=` are not boundaries of the outer list. The boundary may stand on its own continuation line. Whitespace around `!` has no meaning; canonical inline formatting surrounds it with spaces.

### 7.2.2. Name contract and defaults

Let `N` be the number of ordinary parameters and `K` the number before the boundary; the receiver counts in neither. Without a boundary `K = N`; with one, `0 <= K < N`. Ordinary parameter `i`, numbered from zero, accepts positional supply exactly when `i < K`, subject to §10.1 matching. The ordered external names of the ordinary parameters and `K` form the declaration's **argument-name contract**; Types, receiver position and defaults are not part of it. Specialization headers inherit this contract instead of computing a new `K` (§8.8.2).

Name contracts are compared in this effective form, not by written boundary position: `(self ! x: i32)` and `(! self, x: i32)` both have `K = 0`. Normalization never moves parameters or receivers. Name contracts affect applicability, not overload identity or ranking (§9.1, §10.1). Contract implementation matching (§8.4.5) and candidate equivalence (§8.4.6) have their own rules.

A call uses the name contract and defaults of the statically selected declaration. The implementation that runs, including an override or specialization, does not replace that contract, and it does not permit declarations forbidden by the inherited-Name rule (§6.2.2). Calls through Contract requirements follow §8.4.1.

A parameter with `= defaultExpression` may be omitted; a parameter without a default requires a value, on either side of the boundary. Every parameter initializer is a default, evaluated only when the argument is omitted. Defaulted parameters may precede parameters without defaults; callers then supply the later arguments by name without filling the earlier positions.

| Ordinary parameter position | Default | Positional supply | Named supply | Omitted value |
| --- | --- | --- | --- | --- |
| Before boundary / no boundary | No | Allowed | Allowed | Rejected |
| Before boundary / no boundary | Yes | Allowed | Allowed | Allowed |
| After boundary | No | Rejected | Allowed | Rejected |
| After boundary | Yes | Rejected | Allowed | Allowed |

```kimi
func scale(value: i32 ! by => factor: i32 = 1) -> i32 => value * factor
scale(3)
scale(3, by: 4)
scale(3, factor: 4) // Error: internal name.
func options(mode: i32 = 0 ! count: i32) => ()
options(count: 3) // Uses mode's default.
options(3)        // Error: 3 supplies mode, not count.
```

Function Types keep no argument names, name contracts or defaults: a call through a function value supplies every parameter positionally, with no named or omitted arguments (§7.6). The boundary is not available in anonymous functions, Function Types, accessor signatures, specialization headers, generic lists, enum payloads, calls, or dedicated attribute and built-in argument syntax. Special restrictions, such as those of `deinit`, still apply. Foreign declarations permit the boundary but not defaults (§22.3).

### 7.2.3. Default evaluation and ownership

At each call, the explicit arguments are acquired in source order into pending slots. Each omitted default is then evaluated once, in parameter declaration order, in the declaration's scope. A default may refer to preceding parameters through their prepared slots, but not to later parameters or caller-local bindings. Slots do not alias caller variables.

A default may Copy a preceding Copy value or inspect it through temporary shared access, but cannot Move, Consume or modify that slot or its owned contents. Its result cannot retain a new Borrow or Reborrow of a preceding argument, but may Copy an existing shared borrow that has external dependencies. Temporary shared inspection also covers a reserved exclusive input ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations)); exclusive access through that input is unavailable during default evaluation. Temporary inspection Loans end before activation and callee entry. Thus `func f(x: string, y: string = x) => ()` is invalid, while `Text.toString(x)` borrows `x` and produces an independent string.

Every default is checked at declaration time, even if every call supplies the argument. Defaults give no evidence for generic inference, and their transfers may target only constructs inside the default.

A normal transfer that abandons argument evaluation skips the callee and destroys the still-owned prepared values and temporaries in reverse acquisition order; Abort does not unwind. After successful preparation, ownership passes from the pending slots to the initialized parameters, and callee cleanup uses reverse parameter order.

## 7.3. Explicit receivers

A function declared directly in a struct or enum, or a Contract function requirement, is an **instance function** exactly when one parameter's **internal Name** is `self`; otherwise it is a **Type function**. At most one `self` is allowed. It may stand at any written position but cannot be renamed or have a default. Its normalized Type must be `Self`, `ref/Self`, `uniq/Self` or a permitted object-Semantics `Self` form; unrelated targets, extra reference layers, raw pointers and unconstrained generic receiver Semantics are rejected. Origin annotations follow the normal parameter rules. In groups, rootgroups and local functions without an active contextual `self`, a parameter named `self` has no instance-member meaning.

A recognized receiver neither starts nor ends an ordinary-parameter section. It may appear on either side of the boundary, but `(! self)` and `(self !)` are invalid because no ordinary parameter follows the boundary. A parameter named `self` without receiver meaning follows the ordinary boundary rules.

**Receiver shorthand.** A receiver written as bare `self` means `self: ref/Self`. The shared-borrow receiver is fixed before the body is checked; nothing about its Type or ownership is inferred from the body. The shorthand is allowed only at the receiver position of an instance function, including Contract requirements and full specializations, and keeps its written position. Every other named-function parameter needs an explicit Type: group and rootgroup functions, local functions and constructors cannot use the shorthand, and anonymous functions keep their own parameter-inference rules. Write the Type to choose another receiver, such as `uniq/Self` or owning `Self`, or to annotate Origins. The shorthand has the same Signature, input Origins, callable Type and invocation rules as its expansion. A function without a receiver parameter is a Type function. Accessor receivers have their own shorthand (§11.2).

```kimi
struct Meter
    var measured: i32
    func read(self) -> i32 => self.measured
    func update(self: uniq/Self, value: i32) => self.measured = value
    func constant() -> i32 => 0 // Type function: no receiver.

var meter = Meter.init(0)
let shown = meter.read()   // Shared receiver: implicit borrow.
meter.update(5)            // Exclusive receiver: the owned Place is acquired implicitly.
```

**Receiver shape.** A receiver's **shape** is `ref/Self`, `uniq/Self`, owning `Self` or one permitted object-Semantics form. In a function group fixed by member lookup (§9.5), every function that has a receiver must have the same shape; functions without a receiver do not count. For a group formed by a Type's declarations, a violation is a declaration error. Different parameters or labels and mutually exclusive `when` conditions (§8.4.8) do not exempt a group. Contract requirements, including refined ones, obey the rule (§8.4.1), and an explicit full specialization keeps its original's shape (§8.8.2). Same-name declarations in a base and a derived struct are already excluded by the inherited-Name rule (§6.2.2). A group gathered from generic Constraints is checked at the use (§9.5). The `get` and `set` of one Property are distinct operations and are exempt. The Name alone therefore fixes a call's receiver acquisition before overload resolution, and Best Candidate comparison covers only the explicit arguments (§10.4). Shared and exclusive variants of one operation need different names; Kimi declarations follow the naming convention of §4.7.1.

```kimi
struct Counter
    var value: i32
    func peek(self) -> i32 => self.value + 1
    func next(self: uniq/Self) -> i32
        self.value += 1
        return self.value
    // func next(self, by: i32) -> i32     // Error: next would have two receiver shapes.

struct Buffer
    func insert(self: uniq/Self, index: isize, value: i32)
    func insert(self: uniq/Self, index: Index, value: i32)   // OK: the same shape.
```

**Method calls.** In `receiver.method(arguments)`, the receiver is evaluated and acquired first, wherever `self` is declared, and is passed at the declared position of `self`. The receiver is a Receiver Expression ([§3.4](03-types-and-values.md#34-values-places-and-storage)) and is acquired implicitly, exactly as if the operation in the following table were written on it; the row depends on the receiver requirement and on whether the input is value-kind or object-kind. The explicit positional and named arguments are matched against the remaining parameters in written order, and no argument label can supply `self`. Defaults follow in the ordinary order. Exclusive preparation follows [call borrow reservations](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations). Cleanup inside the callee uses the full written parameter order.

| Receiver requirement | Value-kind input `p` | Object-kind input `p` |
| --- | --- | --- |
| `ref/Self`, `uniq/Self` | An owned Place or temporary is borrowed as `p@ref`, `p@uniq`; a borrow value is Reborrowed in the required mode as `p@deref@ref`, `p@deref@uniq`, after the [reference-path selection](03-types-and-values.md#341-reference-path-selection) that located the member | The complete payload `p@deref@ref`, `p@deref@uniq` (§13.5.5.1) when the View Target is exactly the same complete Sealed Type `T`; otherwise `p@objref`, `p@objuniq` |
| `objref/Self`, `objuniq/Self` | Not applicable | `p@objref`, `p@objuniq` |
| Declaration in a base `B` | The projection of §9.5.1 | The same |
| Owning receiver: `Self`, or an owning object-Semantics form | An owned temporary passes as is; an owned Place is Copied when Copy and is otherwise not acquired (write `p@move.m()`); a reference supplies a Scalar `Self` by the Scalar read, and otherwise `p@deref.m()` Copies a Copy referent | An owned temporary passes as is; an owned Place requires `p@move`; there is no read from an object borrow |

**Checks.** The acquisition is checked in the following order, and the call is an error if any check fails:

1. *The supplied operation is legal* under the existing explicit rules, including static storage (§15.2.3), values of generic Semantics (§8.9), `rc`/`arc` (shared access only, §13.5.5), Property permissions (§11.1), getter-result storage (§11.2.3) and the exclusive acquisition conditions of §15.1.5.
2. *The path's receiver compatibility holds.* On a value-kind path, the complete result Type must fit the required Type. An object-borrow path follows object receiver compatibility (§12.4.3–4), and a base-projection path follows §9.5.1. For example, `objuniq/T` satisfies a `uniq/Self` receiver through this path rule, not through a Type conversion.
3. *The post-selection use conditions hold.* If a protected object borrow or a base projection was selected, the selected candidate must be ObjectCallCompatible Proven after overload selection (§12.4.4.1, §9.5.1). A complete Sealed payload dereference is an ordinary complete-value call and needs no proof (§12.4.4). Proven never excludes a candidate, and a missing proof never reselects another candidate.

**Consequences.**

- *No fallback.* When a check fails, the call does not switch to a Scalar read, a materialized temporary or any other acquisition; in particular, it never modifies a Copy and discards the update.
- *Explicit spellings.* Writing the table's operation explicitly means the same: for a value-kind input and an exclusive receiver, `p@uniq.m()` equals `p.m()` through the preparation path of §15.6.7, and the redundant spelling is accepted. A different explicit operation means what it says: `tasks@uniq.length` lends `tasks` exclusively and then reads it through shared access, and `r@ref.m()` on a reference `r` borrows the slot of `r` rather than Reborrowing its referent.
- *Chains.* Each call acquires the previous call's result as its receiver by this table. Acquisition never reaches back through an earlier call, and each call has its own reservations (§15.6.7).

```kimi
struct Meter
    var hits: i32 = 0
    public computed reading: i32
        get(self: uniq/Self) -> i32
            self.hits += 1
            return self.hits

group Registry
    public var meter: Meter = Meter.init()

struct Builder
    func add(self: uniq/Self, value: i32) -> uniq/Self   // Returns an exclusive reference.
    func with(self: Self, value: i32) -> Self            // Returns an owned value.
    func view(self) -> ref/Self                          // Returns a shared reference.
    func finish(self: uniq/Self)

// holder and tasks are var locals.
var meter = Meter.init()
let seen = meter.reading              // Supplies meter@uniq.
holder.items.append(1)                // Supplies holder.items@uniq.
let shared = Registry.meter.reading   // Mutable static: supplies Registry.meter@uniq.
makeResource().consume()              // An owned temporary passes as is.

var builder = makeBuilder()
builder.add(1).add(2)                 // The second receiver reborrows the uniq result.
makeBuilder().with(1).finish()        // The owned temporary result of with is borrowed exclusively for finish.
// builder.view().finish()            // Error: no exclusive acquisition from a ref result.

let count: i32 = 0
var next = func [var count] (value: i32) -> i32
    count += 1
    return value + count
let a = next(1)                       // 2. Exclusive call: supplies next@uniq.
let b = applyTwice(10, next@uniq)     // 15. applyTwice of §8.6; an argument needs the spelling.

holder@uniq.items.append(1)           // The same as holder.items.append(1).
tasks@uniq.length                     // A different operation: lend tasks exclusively, then read it through shared access.
```

The following calls fail a check:

```kimi
struct Point
    Self is Copy
    var x: i32
    var y: i32
    func normalize(self: uniq/Self)
        ...

let p = makePoint()
p.normalize()             // Error: p is a let binding; no Copy is modified instead.

func plot(p: Point)
    p.normalize()         // Error: an owned-Type parameter.

func plotRef(p: ref/Point)
    p.normalize()         // Error: only shared permission.

let tick = func [var count] () -> i32   // Copy: the only capture is an i32.
    count += 1
    return count
let n = tick()            // Error: a let-bound Closure; no Copy is advanced instead.

var q = p                 // Valid: the Copy is explicit.
q.normalize()
makePoint().normalize()   // Valid: a temporary from the start.
```

**Diagnostics.** When a Receiver Expression cannot be acquired, the diagnostic names the failed check. A fix is suggested only when the fixed program satisfies every acquisition, permission and Loan condition. The main causes include:

| Failed check | Suggestion |
| --- | --- |
| Immutable owned storage: a `let` binding, an owned-Type parameter, a `let`-bound Closure | Make it `var`; for a parameter, `var local = p@move` (`var local = p` when Copy) |
| Only shared permission: a `ref`/`objref` value, `rc`/`arc`, a shared path | Make the upstream exclusive, for example `self: uniq/Self` on the enclosing method |
| Storage whose direct exclusive borrow is forbidden: a stored Property with a custom `set`, getter-result storage | Read the value into a local, modify it and write it back through `set`; not suggested when the value cannot be acquired or `set` is inaccessible |
| `set` access, receiver incompleteness, ObjectCallCompatible | The existing diagnostics |

A missing spelling at a position other than a Receiver Expression uses the existing exclusive-borrow diagnostic and suggests `@uniq`/`@objuniq`, also for a Place reached through an exclusive reference. A receiver-shape violation reports every declaration or requirement whose shape differs. Loan conflicts at an implicit lending point carry the notes of §15.6.7.

**Unbound references.** A Type-qualified instance function reference is unbound. A call through `Type.method` supplies every parameter explicitly, including `self` at its declared position, under the ordinary argument-order and receiver-compatibility checks. The receiver may be supplied positionally at its declared position or as a named `self:` argument, whatever the written boundary. It is an ordinary argument, not a Receiver Expression, so an owned Place supplied to a `uniq/Self` position needs `@uniq`. Ordinary parameters keep their declared name contracts; positional skipping and positional supply after named arguments are not allowed. An unbound function value likewise keeps `self` as an ordinary position of its callable signature, captures no receiver and remains subject to the unsafe-function restrictions. `value.method` without invocation does not form a bound-method value in this revision. None of this introduces extension functions, implicit `self` lookup or a conversion for an otherwise incompatible object receiver.

## 7.4. Function constraints

A generic function with an indented body may begin that body with [Constraints](08-generics-constraints-and-contracts.md#82-constraints). Its Constraint Clauses must precede every executable body item; they are processed at compile time and are not executable expressions.

Before lookup, the longest leading sequence of unparenthesized `ConstraintSubject is IsRequirement` items is parsed as Constraint Clauses. A subject not permitted for the function is an error, never a fallback runtime test. Blank lines and comments do not end this prefix. Parenthesizing the whole test, as in `(value is Dog)`, makes it an executable expression item and ends the prefix, so later value tests are executable. A later clause rooted in a generic parameter is a misplaced-Constraint error. In a nongeneric function the prefix rule does not apply, and `value is Dog` is an expression. Dedicated Type and Contract Constraint regions keep their own rules, even through parentheses.

```kimi
func inspect<s/T>(value: s/T) -> ()
    s is ref or obj
    T is Comparable

    return
```

Each clause subject must name a generic parameter of the function or an [associated-Type projection](08-generics-constraints-and-contracts.md#843-associated-types) rooted in one. Leading Constraints are collected before the projections in the signature, including the result Type, are resolved; they are still validated. Function requirements use the [same indented placement](08-generics-constraints-and-contracts.md#841-function-requirements), with a Constraints-only region instead of an executable body.

Every explicit or inferred generic argument at a call site must satisfy the clauses, and body Type checking and instantiation may rely on them. Constraints are not part of the function Signature, so declarations that differ only in their Constraints conflict.

## 7.5. Unsafe functions

An **unsafe function**, declared with `unsafe func`, requires its caller to satisfy documented memory-safety conditions for their documented duration. Calling it requires an [Unsafe Block](14-control-flow.md#1433-unsafe-block), and violating its safety contract is undefined behavior. This runtime safety contract is distinct from Constraints. The conditions are described with the [`safety` documentation item](02-source-and-lexical-structure.md#235-writing-and-extracting-items), which adds no automatic proof or calling permission.

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

Unsafe functions support direct calls only. Taking one as a function value, assigning it to a variable, passing it as an argument or converting it to an ordinary Function Type is an error. Unsafe Function Types are not specified in this revision (§5.6).

```kimi
let reader = read // Error: an unsafe function cannot be taken as a function value.
```

## 7.6. Function expressions

### 7.6.1. Syntax and inference

An anonymous function consists of `func`, an optional Capture List, parameters, an optional result annotation and a common Body (§7.1). There is no bare `(x) => x` form, and there are no external parameter labels, defaults, `!` boundaries or generic lambdas. Every parameter requires a value, and calls are positional whatever the written local names. The body's expectation and its use or discard context are fixed before the body is checked (§10.5).

```kimi
let twice = func (value: i32) => value * 2
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2 // Error: no fixed input signature.
```

An omitted parameter Type requires a fixed expected callable signature; parameter Types are never inferred from body operations or later uses. The result is inferred from that expectation or, once the parameters are fixed, from the body under normal result validation. Whole-result inference also keeps the [result Origins and Loans](15-ownership-and-lifetime-analysis.md#1582-closure-dependencies-and-call-results); annotations use the existing elision rules.

A try failure is an expectation-dependent return source (§17.2.4) and cannot supply a return Type candidate. Its operand is inferred independently (§10.5), and success values are never wrapped automatically.

Creation evaluates the captures, not the body, and acquires them in the creation context. Invocation evaluates the body under an independent Function Boundary, with no outer `return`/`exit`/`continue`/`yield` targets and no inherited Unsafe permission. Named nested functions still cannot capture.

### 7.6.2. Capture acquisition and environment

| List or entry | Creation effect |
| --- | --- |
| No list | Infer the needed outer runtime bindings; Copy only when each complete Type is Copy |
| `[]` | Prohibit runtime captures |
| `[x, y]` | Acquire exactly the listed bindings; unlisted outer runtime bindings are unavailable |
| `x` | Bare acquisition: Copy; a Non-Copy binding is an error |
| `x@move` | Transfer, even for a Copy binding |
| `x@ref` / `x@uniq` | The existing value Borrow, Copy or Reborrow operation for that Semantics |
| `var x` / `var x@move` | Copy / transfer into a mutable environment binding |

Captures are resolved by Binding Identity. An omitted list never infers a Move, a new external Borrow or Reborrow, or a partial capture: a Non-Copy root is rejected even when only a Copy Field is read. An existing `ref/T` may be copied with its dependencies. A bare capture of a binding whose Copy capability depends on generic parameters requires declared Copy evidence at definition checking; unknown Copy is an error, never deferred checking, an inferred Move or a hidden Constraint. `x@move` transfers for every binding.

Type names and accessible static function declarations are not runtime captures. Contextual `self` and a setter's `value` are never captured implicitly, and explicit captures of them obey all receiver, accessor, construction and destruction restrictions. The contextual `storage` binding cannot be captured by name; ordinary bindings named `storage` follow the normal capture rules. No runtime receiver is implicitly bound into a function reference.

Explicit captures execute left to right, including unused entries, and earlier Moves and Loans affect the legality of later entries. Duplicate capture names and collisions with parameters are rejected. Inferred captures execute once each, in order of first occurrence in selected source, including dependencies needed by nested Closures. Source excluded at compile time contributes no capture; a legitimate deferred selection must fix the capture set, order and effects before environment and ownership finalization. Runtime reachability and optimization do not change the set.

```kimi
let number: i32 = 10
let copied = func () => number             // Copy capture.
let text = makeText()                      // Assume string.
let invalid = func () => text              // Error: explicit list required.
let holder = func [text@move] () => ()     // Transfer executes even if unused.
```

Capture targets are binding names only. There are no aliases, initializer expressions, field targets, inter-entry references, `@copy` form or additional object-borrow capture syntax. `var` combines only with a bare Copy or `@move`, not with `@ref` or `@uniq`. Capture entries are capture operations, not the slot borrows of §13.5.5.2: on a reference binding, `@ref` and `@uniq` Copy or Reborrow the reference value instead of borrowing the binding's slot:

| Source | `[x]` | `[x@move]` | `[x@ref]` | `[x@uniq]` |
| --- | --- | --- | --- | --- |
| `ref/T` | Copy the reference | Transfer the reference | Copy the reference, without adding a reference layer | Error |
| `uniq/T` | Exclusive Reborrow | Transfer the reference | Shared Reborrow | Exclusive Reborrow |

Captures without `var` create `let`-like environment bindings, whatever the mutability of the source or of the binding that holds the Closure. Value capture takes a snapshot; no shared heap box is created automatically. A captured exclusive reference can mutate its referent given adequate call access, but assignment to the capture name is not rewritten as assignment to the referent. `var` changes only binding mutability, not deep copying, Copy classification or Origin dependencies.

```kimi
let count: i32 = 0
var next = func [var count] () -> i32
    count += 1
    return count
let first = next()  // 1
let second = next() // 2; outer count is still 0.
```

Environment bindings are not user Fields. Ownership-bearing calls apply ordinary local acquisition and Move Paths, and a consumed `let` cannot be reinitialized. Shared and Exclusive calls cannot move owned captures out. No environment may borrow its own owned capture through another capture; external borrowed dependencies remain legal under the lifetime rules.

**Nested Closures** acquire through every enclosing environment. A binding free only in the inner Closure must still be captured by the outer one; an outer `[]` or an insufficient explicit list is an error. Each omitted-list boundary requires Copy on its own. The outer Closure's own parameters and body locals need capturing only when the inner Closure is created. Moving an outer environment value into an inner Closure makes the outer call Consuming, and an inner `@uniq` cannot exceed the access of the outer binding.

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

A direct call acquires the minimum receiver like a method receiver: the callee expression is a Receiver Expression whose receiver requirement is the internal receiver Type, acquired implicitly under [§7.3](#73-explicit-receivers). An owned Closure Place is therefore borrowed without a spelling: shared for a Shared call, and exclusively for an Exclusive call when its lending point is exclusively writable (§15.1.5). `uniq/F` and `ref/F` values are Reborrowed in the required mode, and a `ref/F` value cannot supply an Exclusive call. A Consuming call Copies a Copy Closure and otherwise needs `c@move()`. A temporary Closure passes as is.

A captured `uniq` alone does not grant exclusive access to a `let`-owned Closure: the `let` binding is immutable owned storage, and a Copy of it is never advanced in its place. Borrowed access cannot Move an unowned environment, although a permitted Copy may provide a separate owned call value.

```kimi
let text = makeText()
let reader = func [text@move] () => inspectText(text)
reader()
reader() // Shared call; a transferred capture does not imply a consuming call.

var next = func [var count] () -> i32
    count += 1
    return count
let first = next()      // Exclusive call: the owned closure is acquired implicitly.

let item = makeResource() // Non-Copy.
let take = func [item@move] () => item@move
let taken = take@move() // Transfers item out of the consumed closure.
// take()               // Error: the Non-Copy closure was consumed.

let tick = func [var count] () -> i32 // Copy: the only capture is an i32.
    count += 1
    return count
// let n = tick()       // Error: a let-bound closure cannot be acquired exclusively; no Copy is advanced instead.
```

The internal call signature keeps the complete receiver, parameter and result Types, per-call Origins, fixed captured Origins and result Loan dependencies. Receiver protection starts before later arguments are evaluated; eligible exclusive receivers use the reservation and activation of §15.6.7, also in indirect and Callable calls. The required Loans last through the uses of dependent results. Generic calls follow the declared [Callable receiver](08-generics-constraints-and-contracts.md#86-callable-constraints), even when instantiation reveals a weaker body requirement.

### 7.6.4. Function references and common-type conversion

A resolved function reference produces its Function Item Type, including its bound generic arguments and Origin contract; different declarations have distinct Types. A Function Item is Copy and Shared-callable, is Owned when its bound arguments satisfy §15.2.3, and keeps its borrowed parameter and result contracts. A runtime method receiver is never bound automatically; receiver arguments are explicit. Unsafe functions and `deinit` cannot be acquired as values.

```kimi
func add(x: i32, y: i32) -> i32 => x + y
let item = add                         // Concrete Function Item; Copy.
let erased: (i32, i32) -> i32 = add    // Common owned value; Non-Copy.
```

At an initialization, argument or return position whose expected Type is a fixed common Function Type, a Function Item or concrete Closure is converted implicitly exactly when:

- its signature fits and its minimum receiver is Shared;
- its complete environment is Owned, and its result does not borrow the hidden environment receiver;
- all public Origin and Loan contracts hold, and ordinary acquisition of the source is legal.

Non-static capture environments and Exclusive- or Consuming-only bodies are therefore rejected; no dependency may be erased to force conformance.

The existing environment is acquired by ordinary acquisition (§3.5): a Copy, an explicit `@move` or the transfer of a temporary. Conversion never rereads outer bindings or repeats captures. The resulting owned common value is always Non-Copy. Transferring it again as the same common Type is an ordinary Move, not a new erasure. Shared invocation does not consume it, and borrowing it as `uniq/F` still exposes only the Shared call.

```kimi
func makeAdder(offset: i32) -> (i32) -> i32
    return func [offset] (value) => value + offset

let callback = makeAdder(10)
let next = callback@move   // Transfer the common value.
callback(5)               // Error: Moved.
let result = next(5)      // 15.
```

Erasure is an owning-container conversion, distinct from the Copy or Move of the source, and does not change the definition of Copy. No target-independent layout, allocation count or inlining is guaranteed. The Windows storage and conversion rules are in §21.2.5, separate from the function ABI; allocation elision there preserves acquisition, validity and cleanup under that section's failure-observability rule. A required allocation failure causes ordinary Abort Termination, with no Move rollback and no normal cleanup guarantee. Concrete `Callable` use creates no erased container but does not promise zero runtime cost.
