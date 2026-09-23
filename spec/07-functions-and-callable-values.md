# 7. Functions and callable values

[Specification index](../SPEC.md)

A function declaration begins with `func`, its Name, optional generic parameters and a parenthesized parameter list. A result Type follows `->`; omitting it in a named function means Unit (`()`), regardless of accessibility or body form. Functions have no Origin parameter list: directly written borrow annotations can introduce implicit scalar Origins under §15.3.4. Declaration-attached `origin` relations precede executable items, at the same indentation as Type Constraints. Definitions use the common Body forms (§7.1). Anonymous functions have separate [inference rules](#761-syntax-and-inference).

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

Explicit parameters are initialized, immutable `let`-like bindings, including anonymous-function, constructor and receiver parameters; a setter's `value` is also immutable. A parameter may be transferred once with `@move`, but reassignment, reinitialization and new exclusive borrows of parameter storage are forbidden; use a `var` local for mutable work. An existing `uniq/T` or `objuniq/T` parameter still permits exclusive access to, and Reborrow of, its referent, because binding immutability does not restrict the referent. Construction and destruction receivers keep their special privileges. There is no `var` parameter syntax.

### 7.2.1. Argument-name boundary

A named function parameter list may contain one `!` boundary in place of a comma. Ordinary parameters before it accept positional or named arguments; those after it require their external names. Without a boundary, all ordinary parameters accept both forms. Constructors and Contract function requirements use the same boundary. The boundary adds no parameter, Type modifier, evaluation phase or ABI slot.

The left section may be empty; the right must contain at least one ordinary parameter, excluding a receiver. Each section is comma-separated. A trailing comma is permitted only at the end of the whole list, never immediately before or after `!`. Parameter suffixes `name?` and `name!` are errors. Empty lists remain `()`, not `(!)`.

```kimi
func find(value: i32 ! start: i32, end: i32) => ()
func pair(a: i32, b: i32) => ()       // Both names may be omitted.
func configure(! count: i32 = 10) => () // Supplied count must be named.
find(10, start: 0, end: 100)
configure()
configure(count: 20)
configure(20) // Error: count requires its name.
```

In either section, `external => internal: T` separates the caller-facing name from the local binding. Renaming alone does not require a label. External names must be unique across the complete list, including the receiver, regardless of defaults or calls. Check internal names independently under §9.2. An external name may equal another parameter's internal name. For example, `func bad(! value => a: i32, value => b: i32)` has duplicate external names and is invalid.

For lexical and layout processing, a recognized boundary ends the preceding declaration/default expression and delimiter region just as a comma does (§2.2.1). It cannot close an indented body on the same line: dedent first. This applies to defaults containing selections, do expressions or anonymous functions. Use syntactic containment, not parentheses depth alone, to identify the owning list; inner boundaries, literals, comments and `!=` are not outer boundaries. The boundary may occupy its own continuation line. Whitespace adds no meaning; canonical inline formatting separates `!` from the surrounding parameters with spaces.

### 7.2.2. Name contract and defaults

Let `N` be the number of ordinary parameters and `K` the number before the boundary, excluding the receiver from both counts. A declaration without a boundary has `K = N`; with a boundary, `0 <= K < N`. Ordinary parameter `i`, numbered from zero, accepts positional supply exactly when `i < K`, subject to §10.1 matching. The ordered ordinary external names and `K` form its **argument-name contract**. Types, receiver position and defaults remain separate. Specialization headers inherit this contract rather than computing a new `K` (§8.8.2).

Compare effective contracts, not written boundary positions. `(self ! x: i32)` and `(! self, x: i32)` both have `K = 0`. Normalization never moves parameters or receivers. Name contracts affect applicability, not overload identity or ranking (§9.1, §10.1); Contract implementation matching and candidate equivalence have distinct rules (§8.4.5).

Calls use the name contract and defaults of the statically selected declaration. Selecting its implementation, including an override or specialization, does not replace that call contract or permit declarations forbidden by the inherited-Name rules (§6.2.2). Calls through Contract requirements follow §8.4.1.

A parameter with `= defaultExpression` may be omitted; one without a default requires a value, independently of the boundary. Every parameter initializer is a default and is evaluated only when omitted. Defaults may precede parameters without defaults. Callers supply later arguments by name without filling earlier positions.

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

Function Types retain neither argument names, name contracts nor defaults. Function-value calls supply all parameters positionally, with no named or omitted arguments (§7.6). The boundary is unavailable in anonymous functions, Function Types, accessor signatures, specialization headers, generic lists, enum payloads, calls and dedicated attribute/built-in argument syntax. Existing special restrictions, including those of `deinit`, remain in force. Foreign declarations permit the boundary but no defaults (§22.3).

### 7.2.3. Default evaluation and ownership

At each call, the explicit arguments are acquired in source order into pending slots. Then each omitted default is evaluated once, in parameter declaration order, in the declaration's scope with access to the prepared preceding slots; it may refer to preceding parameters but not to later parameters or caller-local bindings. Slots do not alias caller variables. A default may Copy a preceding Copy value or inspect it through temporary shared access, but cannot Move, Consume or modify that slot or its owned contents. Its result cannot retain a new Borrow or Reborrow of a preceding argument, but may Copy an existing shared borrow that has external dependencies. This includes shared inspection of a reserved exclusive input under [§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations); exclusive access through that input is unavailable during default evaluation. Temporary inspection Loans end before activation and callee entry. Thus `func f(x: string, y: string = x) => ()` is invalid, while `Text.toString(x)` borrows `x` and produces an independent string.

Every default is checked at declaration time, even if all calls supply the argument. Defaults give no evidence for generic inference, and their transfers may target only constructs inside the default.

A normal transfer that abandons argument evaluation destroys the still-owned prepared values and temporaries in reverse acquisition order and skips the callee; Abort does not unwind. After successful preparation, ownership passes from the pending slots to the initialized parameters, and callee cleanup uses reverse parameter order.

### 7.2.4. Migration from language version 0.0.1

The version change follows §20.5. First diagnose duplicate external names; resolving them requires an explicit API and caller review, never automatic renaming. For other named declarations, place `!` immediately before the first ordinary parameter whose name was required, replacing that comma. If none required a name, omit the boundary. Remove all former `?` suffixes and keep Types, names, defaults and parameter order. Specializations keep boundary-free headers and inherit the original contract.

An old name-optional parameter after a name-required one becomes name-required: positional matching could not reach it under the old rules either. This conversion preserves the accepted direct argument forms without rearranging parameters.

```text
0.0.1: func f(a?: i32, b: i32, c?: i32 = 0)
0.0.2: func f(a: i32 ! b: i32, c: i32 = 0)
```

Required Kimi declarations follow the same conversion: `writeLine` and `swap` have no boundary; `replace` and `exchange` put it before `with` (§15.7, §22.4).

## 7.3. Explicit receivers

A function declared directly in a struct or enum, or a Contract function requirement, is an **instance function** exactly when one parameter's **internal Name** is `self`; otherwise it is a **Type function**. At most one `self` is allowed, at any written position, with no rename or default. Its normalized Type must be `Self`, `ref/Self`, `uniq/Self` or a permitted object-Semantics `Self` form; unrelated targets, extra reference layers, raw pointers and unconstrained generic receiver Semantics are rejected. Origin annotations follow the normal parameter rules. In groups, rootgroups and local functions without an active contextual `self`, a parameter named `self` has no instance-member meaning.

A recognized receiver does not start or end either ordinary-parameter section. It may appear on either side of the boundary; `(! self)` and `(self !)` are invalid because no ordinary parameter follows it. A parameter named `self` without receiver meaning follows the ordinary boundary rules.

**Receiver shorthand.** A receiver written as bare `self` is shorthand for `self: ref/Self`. This fixes a shared-borrow receiver before the body is checked; there is no body-based Type or ownership inference. The shorthand is allowed only at an instance function's receiver position, including Contract requirements and full specializations, and keeps that written parameter position; all other named function parameters require explicit Types. Group and rootgroup functions, local functions and constructors cannot use it, and anonymous functions keep their own parameter-inference rules. An explicit receiver Type, such as `uniq/Self` or owning `Self`, overrides the default, and explicit Origin annotations require the typed form. The shorthand has the same Signature, input Origin, callable Type and invocation rules as its expansion. Omitting the receiver parameter entirely still declares a Type function. (Accessor receivers have their own shorthand; see §11.2.)

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

**Receiver shape.** A receiver's **shape** is `ref/Self`, `uniq/Self`, owning `Self` or one permitted object-Semantics form. Within a function group fixed by member lookup (§9.5), every function that has a receiver must have the same receiver shape; functions without a receiver are not counted. For a group formed by a Type's declarations, a violation is a declaration error: different parameters or labels do not exempt it, mutually exclusive `when` conditions (§8.4.8) do not exempt it, Contract requirements and refined requirements obey it (§8.4.1), an explicit full specialization keeps its original's receiver shape (§8.8.2), and same-name declarations in a base and a derived struct are already excluded by the inherited-Name rule (§6.2.2). A group gathered from generic constraints is checked at the use (§9.5). The `get` and `set` of one Property are distinct operations and are exempt. The receiver acquisition of a call is therefore fixed by the Name alone, before overload resolution, and Best Candidate comparison covers only the explicit arguments (§10.4). Shared and exclusive variants of one operation need different names; Kimi declarations follow the naming convention of §4.7.1.

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

**Method calls.** For `receiver.method(arguments)`, the receiver is evaluated and acquired first, regardless of its parameter position, and recorded at the declared position of `self`. The receiver expression is a Receiver Expression ([§3.4](03-types-and-values.md#34-values-places-and-storage)) and is acquired implicitly: its acquisition is the same as writing the operation of the following table on it, selected by the receiver requirement and by whether the input is value-kind or object-kind. The explicit positional and named arguments are matched against the remaining parameters in their written order; `self` cannot also be supplied by an argument label. Defaults then follow the ordinary order. Exclusive preparation follows [call borrow reservations](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations). Cleanup inside the callee still uses the full written parameter order.

| Receiver requirement | Value-kind input `p` | Object-kind input `p` |
| --- | --- | --- |
| `ref/Self`, `uniq/Self` | `p@ref`, `p@uniq`; a borrow value is reborrowed in the required mode | The complete payload projection `p@ref/T`, `p@uniq/T` (§13.5.5.1) when the View Target is exactly the same complete Sealed Type `T`; otherwise `p@objref`, `p@objuniq` |
| `objref/Self`, `objuniq/Self` | Not applicable | `p@objref`, `p@objuniq` |
| Declaration in a base `B` | The projection of §9.5.1 | The same |
| Owning receiver: `Self`, or an owning object-Semantics form | An owned temporary passes as is; an owned Place is Copied when Copy and otherwise nothing is supplied (write `p@move.m()`); a `ref/T` or `uniq/T` value is Copy read when the referent is Copy (§10.2) | An owned temporary passes as is; an owned Place requires `p@move`; there is no Copy read from an object borrow |

**Checks.** The acquisition is checked in the following order, and the call is an error when any check fails:

1. *The supplied operation is legal* under the existing explicit rules, including static storage (§15.2.3), values of generic Semantics (§8.9), `rc`/`arc` (shared access only, §13.5.5.2), Property permissions (§11.1), getter-result storage (§11.2.3) and the exclusive acquisition conditions of §15.1.5.
2. *The path's receiver compatibility holds.* For a value-kind path, the complete result Type must fit the required Type; an object-borrow path follows object receiver compatibility (§12.4.3–4) and a base-projection path follows §9.5.1. For example, `objuniq/T` satisfies a `uniq/Self` receiver through this path rule, not through a Type conversion.
3. *The post-selection use conditions hold.* When a protected object borrow or a base projection was selected, the selected candidate must be ObjectCallCompatible Proven after overload selection (§12.4.4.1, §9.5.1); a complete Sealed payload projection is an ordinary complete-value call and needs no proof (§12.4.4). Proven never excludes a candidate, and a missing proof never reselects another candidate.

**Consequences.**

- *No fallback.* When a check fails, the call does not switch to a Copy read, a materialized temporary or any other acquisition; in particular, no Copy is modified with its update discarded.
- *Explicit spellings.* Writing the table's operation explicitly has the same meaning: for a value-kind input and a receiver that requires exclusivity, `p@uniq.m()` equals `p.m()` through the preparation path of §15.6.7, and a redundant spelling is accepted. A different explicit operation is that other operation, as written: `tasks@uniq.length` lends `tasks` exclusively and then reads it through shared access.
- *Chains.* Each call acquires the previous call's result as its receiver by this table. Acquisition never reaches back through an earlier call, and reservations are separated per call (§15.6.7).

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

**Diagnostics.** When a Receiver Expression cannot be acquired, the diagnostic follows the failed check, and a fix is suggested only when the fixed program satisfies every acquisition, permission and Loan condition. The main causes, not an exhaustive list, are:

| Failed check | Suggestion |
| --- | --- |
| Immutable owned storage: a `let` binding, an owned-Type parameter, a `let`-bound Closure | Make it `var`; for a parameter, `var local = p@move` (`var local = p` when Copy) |
| Only shared permission: a `ref`/`objref` value, `rc`/`arc`, a shared path | Make the upstream exclusive, for example `self: uniq/Self` on the enclosing method |
| Storage whose direct exclusive borrow is forbidden: a stored Property with a custom `set`, getter-result storage | Read the value into a local, modify it and write it back through `set`; not suggested when the value cannot be acquired or `set` is inaccessible |
| `set` access, receiver incompleteness, ObjectCallCompatible | The existing diagnostics |

A missing spelling at a position other than a Receiver Expression uses the existing exclusive-borrow diagnostic and suggests `@uniq`/`@objuniq`, including for a Place reached through an exclusive reference. A receiver-shape violation reports every declaration or requirement whose shape differs. Loan conflicts at an implicit lending point carry the notes of §15.6.7.

**Unbound references.** A Type-qualified instance function reference is unbound: a call through `Type.method` supplies all parameters explicitly in their written positions, including `self` at its declared position, with the ordinary argument order and receiver compatibility checks. The receiver accepts either positional supply at its declared position or a named `self:` argument, regardless of the written boundary; it is an ordinary argument, not a Receiver Expression, so an owned Place supplied to a `uniq/Self` position needs `@uniq`. Ordinary parameters keep their declared name contracts; no positional skipping or positional supply after named arguments is allowed. An unbound function value likewise keeps `self` as an ordinary position of its callable signature, captures no receiver and remains subject to the unsafe-function restrictions. `value.method` without invocation does not form a bound-method value in this revision. None of this introduces extension functions, implicit `self` lookup, or a conversion for an otherwise incompatible object receiver.

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

An anonymous function consists of `func`, an optional Capture List, parameters, an optional result annotation and a common Body (§7.1). There is no bare `(x) => x` form and no external parameter labels, defaults, `!` boundaries or generic lambdas. Anonymous parameters require values and their calls are positional, independently of the written local names. The body's expectation and use or discard context are fixed before it is checked (§10.5).

```kimi
let twice = func (value: i32) => value * 2
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2 // Error: no fixed input signature.
```

An omitted parameter Type requires a fixed expected callable signature; parameter Types are never searched for from body operations or later uses. The result is inferred from that expectation or, once the parameters are fixed, from the body under normal result validation. Whole-result inference also keeps the [result Origins and Loans](15-ownership-and-lifetime-analysis.md#1582-closure-dependencies-and-call-results); annotations use the existing elision rules.

A try failure is an expectation-dependent return source (§17.2.4); it cannot supply a return Type candidate. Its operand is inferred independently (§10.5), and success values are never automatically wrapped.

Creation evaluates the captures, not the body; capture acquisition occurs in the creation context. Invocation evaluates the body under an independent Function Boundary, with no outer `return`/`exit`/`continue`/`yield` targets and no inherited Unsafe permission. Named nested functions keep their no-capture restriction.

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

Captures are resolved by Binding Identity. An omitted list never infers a Move, a new external Borrow or Reborrow, or a partial capture: a Non-Copy root is rejected even when only a Copy Field is read. An existing `ref/T` may be copied with its dependencies. Generic capture of a bare binding requires declared Copy evidence at definition checking; unknown Copy is an error, not deferred checking, an inferred Move or a hidden Constraint. `x@move` transfers under every binding.

Type names and accessible static function declarations are not runtime captures. Contextual `self` and a setter's `value` are never implicitly captured, and explicit captures of them obey all receiver, accessor, construction and destruction restrictions. The contextual `storage` binding cannot be captured by name; ordinary bindings named `storage` elsewhere follow the normal capture rules. No runtime receiver is implicitly bound into a function reference.

Explicit captures execute left to right, including unused entries, and earlier Moves and Loans affect the legality of later entries. Duplicate capture names and collisions with parameters are rejected. Inferred captures execute once each, in order of first occurrence in selected source, including dependencies needed by nested Closures. Excluded compile-time source contributes no capture; a legitimate deferred selection must fix the capture set, order and effects before environment and ownership finalization. Runtime reachability and optimization do not alter the set.

```kimi
let number: i32 = 10
let copied = func () => number             // Copy capture.
let text = makeText()                      // Assume string.
let invalid = func () => text              // Error: explicit list required.
let holder = func [text@move] () => ()     // Transfer executes even if unused.
```

Capture targets are binding names only. There are no aliases, initializer expressions, field targets, inter-entry references, `@copy` form or additional object-borrow capture syntax. `var` combines only with bare Copy or `@move`, not with `@ref` or `@uniq`. Capturing an existing reference follows ordinary adaptation:

| Source | `[x]` | `[x@move]` | `[x@ref]` | `[x@uniq]` |
| --- | --- | --- | --- | --- |
| `ref/T` | Copy the reference | Transfer the reference | Copy the reference, without adding a reference layer | Error |
| `uniq/T` | Exclusive Reborrow | Transfer the reference | Shared Reborrow | Exclusive Reborrow |

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

A direct call acquires the minimum receiver as a method receiver: the callee expression is a Receiver Expression whose receiver requirement is the internal receiver Type, and it is acquired implicitly under [§7.3](#73-explicit-receivers). An owned Closure Place is thus borrowed without a spelling for a Shared or Exclusive call when its lending point is exclusively writable (§15.1.5), `uniq/F` and `ref/F` values are reborrowed in the required mode, a `ref/F` value cannot supply an Exclusive call, and a Consuming call Copies a Copy Closure and otherwise needs `c@move()`. A temporary Closure passes as is.

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

The internal call signature keeps the complete receiver, parameter and result Types, per-call Origins, fixed captured Origins and result Loan dependencies. Receiver protection starts before later arguments; eligible exclusive receivers use §15.6.7 reservation and activation, including indirect and Callable calls. The required Loans last through uses of dependent results. Generic calls follow the declared [Callable receiver](08-generics-constraints-and-contracts.md#86-callable-constraints), even when instantiation reveals a weaker body requirement.

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
let next = callback@move   // Transfer the common value.
callback(5)               // Error: Moved.
let result = next(5)      // 15.
```

Erasure is an owning-container conversion, distinct from source Copy or Move; it does not change the definition of Copy. No target-independent layout, allocation count or inlining is guaranteed. The Windows storage and conversion rules are in §21.2.5, separately from the function ABI; allocation elision there preserves acquisition, validity and cleanup under that section's failure-observability rule. A required allocation failure causes ordinary Abort Termination, with no Move rollback and no normal cleanup guarantee. Concrete `Callable` use creates no erased container, but it does not promise zero runtime cost.
