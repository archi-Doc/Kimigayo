# 7. Functions and callable values

[Specification index](../SPEC.md)

## 7.1. Declarations, bodies and results

A function declaration is `func`, one unqualified Name, optional generic parameters, a parenthesized parameter list and an optional `->` result: a Type or a Place result ([§7.1.1](#711-place-results)) (grammar: [§F.6](appendices/F-syntax-summary.md#f6-declarations)). A named function without `->` returns Unit (`()`). Functions have no Origin parameter list; a written borrow annotation may introduce an implicit scalar Origin ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)).

A function belongs to the Container or executable scope in which it is written; a qualified declaration name such as `func View.get(...)` is an error.

The result Type comes from the declaration alone. A generic body is checked under its declared Types and Constraints and is not reinterpreted at instantiation.

Bodies supply results under [§14.2](14-control-flow.md#142-bodies-and-evaluation-contexts), and every `return` must fit the result Type ([§14.9](14-control-flow.md#149-result-validation)).

```kimi
func direct(left: i32, right: i32) -> i32 => left + right
func indented(left: i32, right: i32) -> i32
    return left + right
func unused() => 123            // Valid: a Unit function discards 123.
func bad(left: i32, right: i32) -> i32
    left + right                // Error: the structural end supplies Unit.
```

### 7.1.1. Place results

A Place result publishes an existing, complete Storage whose stored Type is `T` instead of returning a value. `place ref/T` offers a shared borrow and, for a Copy `T`, a Copy; `place uniq/T` also offers an exclusive borrow and replacement of the whole `T`. Neither offers Take; there is no `place owner/T` or `place T`. The spelling after `place` is the Type that `@ref` or `@uniq` on the Place produces: `place ref/(ref/Node)` publishes a slot holding a reference, and `place ref/(T?)` a slot storing `Option<T>`. A following `during` applies to the Place result. A Place result is a result category, not a Type; it may be written wherever a result is, and is never inferred.

**Returning.** The `return` operand, or the single-item body, designates a Place without acquiring a value: a Place expression or a call with a compatible Place result. The slot of a local or by-value parameter cannot be returned; Storage reached through a local reference can. Every normally completing path designates a Place, and `if`, `match` and `do` produce none, so each branch returns its own. The Storage's actual Type `A` agrees with `T` in Semantics, Core and structure, and Origins are checked as fitting `ref/A` to `ref/T` (or `uniq/A` to `uniq/T`). An exclusive Place may be published as a shared result, never the reverse.

**Contracts.** The outer Origin is introduced, quantified and elided like that of a borrow result ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification), [§15.4.3](15-ownership-and-lifetime-analysis.md#1543-result-defaults)); `during self` names the receiver's borrow Origin, and `during self.source` never removes the actual receiver Loan. Overloads that differ only in result mode cannot coexist. `T` is fixed from the arguments, explicit Type information and the expected Place contract ([§10.3](10-overload-resolution-and-inference.md#103-expected-results)). Function references, Callable, Contract implementations ([§8.6](08-generics-constraints-and-contracts.md#86-conformance)) and indirect calls keep the result mode, Origins and Loan contract; compatibility needs the same result mode ([§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)). A Place result cannot bind an ordinary Type argument.

**Using a Place.** A published Place is acquired under [§3.5](03-types-and-values.md#35-copy-and-move), without Take. A Place that is returned as a Place, borrowed explicitly, projected, used as a Subject or assigned to is not acquired. Discarding an unacquired Place performs the call and destroys nothing; `_ = call()` acquires a value, so it fails where bare acquisition fails.

```kimi
func first<T>(values: ref/Array<T>) -> place ref/T during values
    return values[0]                 // Publishes the element Place.

// resources: Array<Resource>, where Resource is Non-Copy.
let view = first(resources@ref)@ref  // The element Place is borrowed; the reference is kept.
inspect(first(resources@ref))        // Shared borrow at a ref/Resource parameter.
// let value = first(resources@ref)  // Error: a Non-Copy value cannot be read bare.
```

## 7.2. Parameters

Explicit parameters, including anonymous-function, constructor and receiver parameters and a setter's `value`, are initialized, immutable `let`-like bindings; there is no `var` parameter. A parameter may be transferred once with `@move`, but its storage cannot be reassigned, reinitialized or newly borrowed exclusively. A `uniq/T` or `objuniq/T` parameter still permits exclusive access to its referent. Construction and destruction receivers keep their own privileges ([§6.3.4](06-declarations-and-containers.md#634-the-construction-receiver), [§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)).

```kimi
func grow(items: uniq/Array<i32>, extra: Array<i32>)
    items.appendAll(extra@move)   // Valid: exclusive access to the referent; one transfer.
    // extra = Array.init(capacity: 0)  // Error: a parameter cannot be reassigned.
```

### 7.2.1. Argument-name boundary

The parameter list of a named function, constructor or Contract function requirement may contain one `!` in place of a comma. Ordinary parameters before it accept positional or named arguments; those after it need their external names. Without `!`, every ordinary parameter accepts both. The left section may be empty; the right one needs at least one ordinary parameter; a receiver may stand in either section but does not count, so `(! self)` and `(self !)` are invalid. A trailing comma may end only the whole list, never touch `!`. The suffixes `name?` and `name!` are errors. A task slot ([§24.2](24-suspension-and-asynchronous-tasks.md#242-task-slots)) precedes both sections: `func pause(task; ! delay: Time.Duration)`.

`external => internal: T` renames a parameter in either section; renaming alone does not make the name required. External names are unique across the whole list, including the receiver; internal names follow [§9.3](09-names-signatures-and-access.md#93-local-scopes-and-contextual-names).

No other list has a boundary, and specialization headers write none. Foreign declarations allow it but no defaults ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)).

### 7.2.2. Name contract and defaults

Let `N` be the number of ordinary parameters and `K` the number before the boundary (`K = N` without one); the receiver and the task slot count in neither. Parameter `i`, from zero, may be supplied positionally exactly when `i < K` ([§10.1.1](10-overload-resolution-and-inference.md#1011-argument-matching)). The ordered external names and `K` form the **argument-name contract**; Types, receiver position and defaults are not part of it, so `(self ! x: i32)` and `(! self, x: i32)` have the same contract. Specialization headers inherit it ([§8.13.2](08-generics-constraints-and-contracts.md#8132-inherited-contract)). It affects applicability, not overload identity or ranking ([§9.1](09-names-signatures-and-access.md#91-signatures)).

A parameter with `= default` may be omitted; one without needs an argument on either side of the boundary. Defaulted parameters may precede required ones, which callers then name. A call uses the name contract and defaults of the statically selected declaration, never those of an override or specialization; calls through Contract requirements follow [§8.4.2](08-generics-constraints-and-contracts.md#842-function-requirements). A function value has no names or defaults: its calls supply every parameter positionally ([§7.7](#77-function-expressions)).

```kimi
func scale(value: i32 ! by => factor: i32 = 1) -> i32 => value * factor
func options(mode: i32 = 0 ! count: i32) => ()

scale(3, by: 4)
scale(3, factor: 4)   // Error: factor is the internal name.
scale(3, 4)           // Error: by requires its name.
options(count: 3)     // mode uses its default.
options(3)            // Error: 3 supplies mode, and count is missing.
```

### 7.2.3. Default evaluation

At each call, the explicit arguments are acquired in source order into pending slots; then each omitted default is evaluated once, in parameter order, in the declaration's scope; it may use preceding parameters, but not later parameters or the caller's locals. It may Copy a preceding Copy value or inspect it through temporary shared access (also a reserved exclusive input, [§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations), without its exclusive access), but cannot Move or modify that slot. Its result cannot keep a new Borrow or Reborrow of a preceding argument, but may Copy an existing shared borrow. Inspection Loans end before callee entry. Transfers inside a default target only constructs inside it.

A default is checked at its declaration like a generic body ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)): it must fit its parameter Type for every binding that satisfies the Constraints, and it is no evidence for inference. Each evaluation instantiates it with the call's bindings; beyond the call's ordinary argument and Loan checks, it can make the call invalid only through the effects it contributes to effect bounds ([§8.11](08-generics-constraints-and-contracts.md#811-effect-bounds)).

A transfer that interrupts argument evaluation skips the callee; prepared values and temporaries are cleaned up under [§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order). Abort does not unwind. After preparation, the pending slots become the parameters, cleaned up by the callee in reverse parameter order.

```kimi
func inc(value: i32) -> i32 => value + 1
func label(x: string, y: string = Text.toString(x)) => ()  // Borrows x; y is independent.
func bad(x: string, y: string = x) => ()                   // Error: a default cannot Move x.
func erased(action: (i32) -> i32 = inc) => ()              // Valid: inc is erased.
func run<F>(action: F = inc)                               // Error: inc's Type is not every such F.
    F is Callable<(i32) -> i32>
    return
```

## 7.3. Receivers

### 7.3.1. Instance functions and receiver Types

A function declared directly in a struct or enum, or a Contract function requirement, is an **instance function** exactly when one parameter's internal Name is `self`; otherwise it is a **Type function**. At most one `self` is allowed; it may stand at any position after the task slot ([§24.2.1](24-suspension-and-asynchronous-tasks.md#2421-form)) and cannot be renamed or have a default.

The receiver's normalized Type is `S/Self` for one concrete Semantics `S` other than `raw`: `Self`, `ref/Self`, `uniq/Self`, `obj/Self`, `rc/Self`, `arc/Self`, `objref/Self` or `objuniq/Self`, under the ordinary Type formation rules (object Semantics need `Self` to be an ObjectPayload). Origin annotations follow the ordinary parameter rules. Any other receiver Type is an error: `raw/Self`, a generic Semantics `s/Self` whether or not `s` is constrained, extra reference layers such as `ref/(ref/Self)`, or another target. Virtual functions use object-borrow receivers ([§6.4.1](06-declarations-and-containers.md#641-declarations)).

Local named functions, groups and rootgroups have no receiver: a parameter named `self` there is an ordinary parameter. The enclosing function's contextual `self` is not visible inside a local named function, which captures nothing.

**Shorthand.** A bare `self` means `self: ref/Self`, fixed before the body is checked. It is allowed only at the receiver position of an instance function, including Contract requirements and full specializations; every other parameter of a named function or constructor needs a Type. Accessor receivers have their own shorthand ([§11.3](11-properties.md#113-accessor-functions)).

```kimi
struct Meter
    var measured: i32 = 0
    func read(self) -> i32 => self.measured           // ref/Self
    func update(self: uniq/Self, value: i32) => self.measured = value
    func total(self) -> i32
        func helper(self: ref/Meter) -> i32 => 1      // Valid: an ordinary parameter.
        return helper(self)
    // func f<s/T>(self: s/Self) => ()                // Error: generic receiver Semantics.

var meter = Meter.init()
let shown = meter.read()     // Shared receiver: implicit borrow.
meter.update(5)              // Exclusive receiver: meter is borrowed implicitly.
```

### 7.3.2. Receiver shape

A receiver's **shape** is its Semantics; one function group ([§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)) has one shape, counting only functions with a receiver. Among a Type's declarations or a Contract's requirements ([§8.4.2](08-generics-constraints-and-contracts.md#842-function-requirements)) a violation is a declaration error, whatever the parameters, labels or `when` conditions; a group gathered from Constraints is checked at the use. A full specialization keeps its original's shape ([§8.13.2](08-generics-constraints-and-contracts.md#8132-inherited-contract)). The `get` and `set` of a Property are separate operations, exempt from this rule; their shapes are fixed by [§11.3](11-properties.md#113-accessor-functions). So the Name fixes the receiver acquisition before overload resolution, and Best Candidate comparison covers only the explicit arguments ([§10.4](10-overload-resolution-and-inference.md#104-best-candidate)). A group also has one task-slot shape ([§24.2.3](24-suspension-and-asynchronous-tasks.md#2423-not-a-parameter-one-shape-per-function-group)).

```kimi
struct Counter
    var value: i32 = 0
    func peek(self) -> i32 => self.value
    func next(self: uniq/Self) -> i32
        self.value += 1
        return self.value
    // func next(self, by: i32) -> i32 => by   // Error: next would have two shapes.
```

### 7.3.3. Method calls

In `receiver.method(arguments)`, the receiver is evaluated and acquired first and passed at the declared position of `self`. It is a Receiver Expression ([§3.4](03-types-and-values.md#34-places-and-storage)), acquired as if the table's operation were written on it. The explicit arguments match the remaining parameters; no label supplies `self`, and the task argument binds no parameter ([§24.2.2](24-suspension-and-asynchronous-tasks.md#2422-passing)). Exclusive preparation follows [§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations).

| Receiver Type | Value-kind input `p` | Object-kind input `p` |
| --- | --- | --- |
| `ref/Self`, `uniq/Self` | Owned Place or temporary: `p@ref`, `p@uniq`; borrow value: `p@follow@ref`, `p@follow@uniq` on the path of [§3.4.3](03-types-and-values.md#343-reference-path-selection) | `p@follow@ref`, `p@follow@uniq` when the View Target is exactly the complete Sealed Type ([§13.5.5](13-operators-and-assignment.md#1355-follow)); otherwise `p@objref`, `p@objuniq` |
| `objref/Self`, `objuniq/Self` | Not applicable | `p@objref`, `p@objuniq` |
| Declared in a base `B` | The projection of [§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection) | The same |
| `Self`, `obj/Self`, `rc/Self`, `arc/Self` | Temporary as is; owned Place Copied when Copy, otherwise not acquired (write `p@move.m()`); a reference to a read Type ([§3.5.3](03-types-and-values.md#353-value-read)) gives the value read, otherwise nothing is acquired (write `p@follow.m()` to Copy a Copy referent) | Temporary as is; owned Place needs `p@move`; an object borrow supplies nothing (no read) |

A pair layer ([§13.5.5](13-operators-and-assignment.md#1355-follow)) on the path is followed like a borrow value; through it a by-value receiver gets only the value read of a read Type.

The call is an error unless, in order: (1) the supplied operation is legal under its explicit rules (static storage, generic Semantics, shared-only `rc`/`arc`, Property permissions, getter-result storage, exclusive acquisition: [§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned), [§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects), [§13.5.5](13-operators-and-assignment.md#1355-follow), [§11.2](11-properties.md#112-standard-access), [§11.5](11-properties.md#115-getter-results), [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)); (2) the path's receiver compatibility holds: on a value-kind path the complete Type fits the required Type; object-borrow and base-projection paths follow [§12.6](12-expressions.md#126-object-member-calls), [§12.7](12-expressions.md#127-object-receiver-compatibility) and [§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection), so `objuniq/T` satisfies `uniq/Self` by the path rule, not by conversion; (3) after selection, a protected object borrow or base projection is ObjectCallCompatible Proven ([§12.7](12-expressions.md#127-object-receiver-compatibility)), and a missing proof never reselects.

A failed check never falls back to another acquisition, so a Copy is never modified and discarded. Writing the table's operation explicitly means the same; a different one means what it says (`tasks@uniq.length` lends `tasks` exclusively, then reads it). In a chain, each call acquires the previous result by the table.

```kimi
struct Point
    Self is Copy
    var x: i32
    func normalize(self: uniq/Self) => self.x = 0
    func view(self) -> ref/Self => self

var q = makePoint()
q.normalize()                   // Supplies q@uniq.
makePoint().normalize()         // A temporary is borrowed exclusively.
// q.view().normalize()         // Error: no exclusive acquisition from a ref result.
let p = makePoint()
// p.normalize()                // Error: p is a let binding; no Copy is modified instead.
```

### 7.3.4. Unbound references

A Type-qualified instance function reference such as `Meter.update` is unbound. A call through it supplies every parameter; the receiver is supplied positionally at its declared position when every preceding parameter is supplied positionally, or as a named `self:` argument. The other parameters keep their name contract ([§7.2.2](#722-name-contract-and-defaults)); only a function value formed from the reference is called positionally. The receiver is an ordinary argument, not a Receiver Expression, so an owned Place at `uniq/Self` needs `@uniq`; an unbound function value captures no receiver. `value.method` without a call forms no bound-method value.

```kimi
struct Meter
    func update(self: uniq/Self, value: i32) => ()
    func mark(x: i32 ! y: i32, self) => ()

var meter = makeMeter()
Meter.update(meter@uniq, value: 5)    // The receiver is positional; value may also be positional.
Meter.mark(1, y: 2, self: meter@ref)  // The receiver cannot be positional after y.
// Meter.update(meter, 5)             // Error: an owned Place at uniq/Self needs @uniq.
```

## 7.4. Parameter acquisition shape

In one function group, how an argument is acquired follows from the Name and the position, never from the candidate that other arguments select.

### 7.4.1. Acquisition modes and admitted cases

The **acquisition mode** of a parameter is what it requires of a bare owned Place argument, judged on the outer Semantics of its normalized Type (aliases expanded, grouping and redundant `owner` removed). The names are those of the Subject modes ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)).

| Mode | Outer Semantics |
| --- | --- |
| ByValue | `owner` (a Core, Tuple, fixed array or Function Type), `obj`, `rc`, `arc`, `raw` |
| Shared | `ref`, `objref` |
| Exclusive | `uniq`, `objuniq` |

A parameter whose outer Semantics is bound by substitution has one **admitted case** per Semantics it may take, each judged as if written with it: for `s/U` with a pair binder `s`, the admitted set of `s` ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)); for a Type parameter `T` of the function or an enclosing Type, or an associated projection that does not normalize, every Semantics ([§8.1.1](08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)).

Inference binds a function's own slot to the argument's complete Type; for a bare owned Place, only explicit Type arguments bind it to a Semantics other than `owner` ([§10.1](10-overload-resolution-and-inference.md#101-candidate-applicability), [§10.8](10-overload-resolution-and-inference.md#108-generic-argument-inference)). Explicit Type arguments reach only declarations whose generic lists have the same length and kinds, so against any other declaration a function slot that admits `owner` has the `owner` case alone. A projection of a slot is bound from other arguments and keeps every case.

### 7.4.2. Corresponding parameters

A declaration has two parameter sequences: the bound call `value.f(...)` without the receiver, and the unbound call `Type.f(...)` with it at its written position. A function without a receiver has only the unbound sequence. Two parameters of two declarations **correspond** when, in the same sequence, both can be supplied positionally at the same position number, or they have the same external name. In the unbound sequence, the receiver can be supplied positionally exactly when every parameter before it can ([§7.3.4](#734-unbound-references)); otherwise it is supplied only as `self:` and corresponds only by that name. Bound-call receivers follow [§7.3.2](#732-receiver-shape). Defaults, counts, the position of `!`, labels, Constraints and `when` conditions do not change correspondence; a task slot corresponds to nothing and shifts no numbering.

### 7.4.3. Matching keys and overlap

| Parameter Type | Matching key |
| --- | --- |
| `U` (that is, `owner/U`), `ref/U`, `uniq/U` | `Value(U)` |
| `obj/U`, `rc/U`, `arc/U`, `objref/U`, `objuniq/U` | `Object(U)` |
| `s/U` for a pair binder `s` | The keys of the admitted cases of `s`, each formed as if written with that Semantics (a `raw` case gives `Value(raw/U)`) |
| A bare Type parameter `T`, an original `s/T`, a pair target alone, an associated projection that does not normalize | Any: it overlaps every key |
| Anything else (`raw/U`, a Core, Tuple, fixed array or Function Type) | `Value` of the Type itself |

Two matching keys overlap when the unification of [§8.9.1](08-generics-constraints-and-contracts.md#891-direct-conformance-collisions) steps 2-4 unifies them (without the Constraint normalization of step 1). Only a mismatch of fixed structure proves non-overlap: different nominal Types, Tuples or Function Types, different determined Semantics (`uniq/Node` and `Node`), or two lengths that are both constants with different values. Slots, unresolved associated projections and non-constant lengths overlap with anything they could bind to; Origins are ignored. Slots inferred at a use are independent per candidate, and fixed outer slots are shared; distinct slots can unify, so `Array<A>` and `ref/Array<B>` overlap for enclosing slots `A` and `B`. Type Constraints are never consulted. Overlap is judged on completed headers ([§8.6](08-generics-constraints-and-contracts.md#86-conformance)).

### 7.4.4. Declaration and call contracts

For each pair of corresponding parameters of two declarations in one group:

1. Check every combination of admitted cases with independently bound slots. If all overlapping combinations have the same mode, the pair is valid.
2. Otherwise, repeat assuming the permitted correlations: the same construction-target slot, or same-numbered slots of two function generic lists of the same length and kinds (equal projections from equal sources). If the pair now has one mode wherever keys overlap, it is valid with a contract relying on those correlations.
3. Otherwise the declarations are invalid together.

A violation among the same-name declarations of one scope, including a Contract's inherited requirements ([§8.4.3](08-generics-constraints-and-contracts.md#843-refinement)), is a declaration error; in a group gathered at a use ([§9.5.1](09-names-signatures-and-access.md#951-aliases), [§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)) it is an error at that use, whatever its arguments. Counts, labels, defaults and `when` conditions exempt no pair; a full specialization keeps its original's contract. Built-in operator, index and comparison candidates obey the rule (`index` and `indexUniq`).

**Call contract.** A call without explicit Type arguments to a group whose declaration-time validity relies on correlated slots (step 2) must establish each relied-on correlation before candidates are filtered by count, labels, defaults, Constraints, conditions or expected results. A correlation holds when every compared candidate binds the slot from the same evidence: the same fixed explicit or environment binding; the same independently typable or Literal argument ([§10.5.1](10-overload-resolution-and-inference.md#1051-argument-kinds)) at corresponding parameters, matched by the same inference step of [§10.1](10-overload-resolution-and-inference.md#101-candidate-applicability) (structural or call-signature matching) along the same structural path (the Type constructors and argument indices from the parameter Type to the slot); or the call's expected result along the same structural path of the result Types. The argument's Type need not be resolved yet: a shared unresolved literal argument correlates the slot, and literal fitting and defaulting then proceed as usual ([§10.2.1](10-overload-resolution-and-inference.md#1021-literals)). An argument filled by a default gives no evidence. If any candidate has an additional evidence source for the slot, the correlation fails. Showing from this evidence that the overlapping positions get the same acquisition mode is sufficient; equal complete Types are not required. Candidate elimination, conversion search, associated-Type solving and waiting-argument analysis never establish a correlation. A call that cannot establish it is an error unless it writes explicit Type arguments.

**Consequence.** Where keys overlap, every candidate acquires the argument the same way, and within a candidate a reference argument is acquired as its own Type decides ([§10.2.2](10-overload-resolution-and-inference.md#1022-inference-and-adaptation-classes)). A forgotten `@uniq` is an error at the only candidate ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)), never a silent selection of a by-value candidate. The single-mode guarantee holds only where keys overlap; for non-overlapping keys the argument's Type selects the candidate, as in `inspect(value: uniq/(uniq/Node))` versus `inspect(value: ref/Node)`.

```kimi
func bump(score: Score ! flag: bool) => ()
func bump(score: uniq/Score ! flag: i32) => ()  // Error: ByValue and Exclusive at position 0.
func g<T>(value: T) => ()
func g(value: uniq/Node) => ()                  // Error: T may be Node.

func show(value: Node) => ()
func show(value: objref/Node) => ()             // Valid: Value(Node) and Object(Node).
func f(value: Node) => ()
func f<T>(value: T) => ()                       // Valid: T has the owner case alone.
func inspect(value: uniq/(uniq/Node)) => ()
func inspect(value: ref/Node) => ()             // Valid: different determined Semantics.

struct Item<T>
    public var value: T
    public init(value: T, code: i32) => self.value = value@move
    public init(value: T, name: string) => self.value = value@move

let item = Item.init(42, 0)   // Item<i32>: both candidates bind T from the same argument.
```

If one of these constructors also took `witness: Tag<T>` and the other `witness: Tag<ref/T>`, a supplied `Tag<ref/i32>` would be an extra evidence source in one candidate, and the call would need explicit Type arguments.

## 7.5. Function constraints

The indented body of a function, constructor, destructor or accessor begins with a **leading region**: the longest leading sequence of unparenthesized Constraint Clauses ([§8.2](08-generics-constraints-and-contracts.md#82-constraints-and-requirement-expressions); each with its indented Callable effect list, [§8.11.6](08-generics-constraints-and-contracts.md#8116-callable-effect-bounds)), `origin` relation clauses ([§15.3.3](15-ownership-and-lifetime-analysis.md#1533-declaration-attached-relations)) and, in an original virtual function only, effect items ([§8.11.1](08-generics-constraints-and-contracts.md#8111-declarations)), in any order. Blank lines and comments do not end it; a parenthesized test such as `(value is Dog)` is an executable item and ends it; items inside compile-time directives count by their own kinds, in every arm ([§19.4](19-compile-time-directives.md#194-excluded-syntax)). A later Constraint Clause rooted in a generic parameter is a misplaced-Constraint error, and an effect item elsewhere is an error. An accessor's leading region may hold only `origin` relations; a destructor body has no leading region items.

Each subject names a generic parameter of the function or of the declaring Type, or an associated projection rooted in one ([§8.5](08-generics-constraints-and-contracts.md#85-associated-types)). The signature, including the result Type, may rely on the leading Constraints; identity premises follow [§8.2](08-generics-constraints-and-contracts.md#82-constraints-and-requirement-expressions). Function requirements have their own indented region ([§8.4.2](08-generics-constraints-and-contracts.md#842-function-requirements)).

Every generic argument at a call must satisfy the clauses, and the body may rely on them. Constraints are not part of the Signature: declarations that differ only in Constraints conflict.

A member function or constructor whose clauses constrain a generic parameter of its declaring Type is a **conditional member**. Its premise is those clauses and any enclosing `when` condition, with the receiver's or qualifier's Type arguments substituted, and it decides applicability as in [§8.8.2](08-generics-constraints-and-contracts.md#882-verification-and-use): a Refuted premise makes the member inapplicable without changing lookup, and a generic caller must prove it. A conditional member needs no target Contract and grants no conformance.

```kimi
func pick<T>(a: ref/T, b: ref/T) -> ref/T during a
    origin b outlives a
    T is Copy                         // Still in the leading region.
    return a

struct Box<E>
    public var item: E
    public init(item: E) => self.item = item@move
    public func same(self, other: ref/Self) -> bool
        E is Equatable
        return self.item == other.item

let yes = Box<i32>.init(1).same(Box<i32>.init(1))  // Valid: i32 is Equatable.
// For a non-Equatable Point, same is inapplicable on Box<Point>.
```

## 7.6. Unsafe functions

An **unsafe function**, declared `unsafe func`, has a safety contract: memory-safety conditions that its caller must satisfy and its Types cannot express, documented with the `safety` item ([§2.3](02-source-and-lexical-structure.md#23-documentation-comments)). Calling it requires an Unsafe Block ([§14.3](14-control-flow.md#143-do-and-unsafe)); violating the contract is undefined behavior. `unsafe func` does not make its own body an unsafe context.

`unsafe` is not part of the Signature; overload resolution ignores it and the caller's unsafe context, and the requirement is checked after selection. An unsafe function supports direct calls only: taking it as a value, passing it or converting it is an error. Unsafe Function Types are deferred ([§D](appendices/D-deferred-features.md#appendix-d-deferred-features)).

```kimi
/// - safety: pointer refers to a live, initialized, readable i32 for the call.
unsafe func read(pointer: raw/i32) -> i32
    unsafe => return *pointer

unsafe func invalidRead(pointer: raw/i32) -> i32
    return *pointer      // Error: the body is not an unsafe context.

let reader = read        // Error: an unsafe function is not a value.
```

## 7.7. Function expressions

### 7.7.1. Syntax and inference

An anonymous function is `func`, an optional Capture List, parameters, an optional result annotation and a body (grammar: [§F.9](appendices/F-syntax-summary.md#f9-expressions)). There is no bare `(x) => x` form, and there are no labels, defaults, `!` boundaries or generic anonymous functions. It has a task slot exactly when its header writes one ([§24.2.1](24-suspension-and-asynchronous-tasks.md#2421-form)). Every parameter needs an argument, and calls are positional.

An omitted parameter Type needs a fixed expected call signature ([§10.5.2](10-overload-resolution-and-inference.md#1052-call-signatures)), which supplies the Types aligned after any task slot; parameter Types are never inferred from the body or later uses. The result comes from that expectation or, once the parameters are fixed, from the body, with its result Origins and Loans ([§15.8.2](15-ownership-and-lifetime-analysis.md#1582-closure-dependencies)). A body's result is never evidence for an enclosing call's slots: the anonymous function is a waiting argument ([§10.5.1](10-overload-resolution-and-inference.md#1051-argument-kinds)), and a Closure Type bound to a slot fills no other slot ([§10.8](10-overload-resolution-and-inference.md#108-generic-argument-inference)). A `try` failure supplies no result Type candidate ([§17.3](17-failure-handling.md#173-try-propagation)); success values are never wrapped.

Creation evaluates only the captures. A call runs the body under its own Function Boundary, with no outer transfer targets and no inherited Unsafe permission.

```kimi
let twice = func (value: i32) => value * 2              // Result: i32.
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2                 // Error: no fixed input signature.
```

### 7.7.2. Captures

| List or entry | Creation effect |
| --- | --- |
| No list | Copies each outer runtime binding the body uses; each must be Copy |
| `[]` | No runtime captures |
| `[x, y]` | Exactly the listed bindings |
| `x` / `var x` | Initializes the environment binding `x` as `let x = x` / `var x = x` would |
| `x@op` / `var x@op` | As `let x = x@op` / `var x = x@op` would; `op` is `move`, `ref` or `uniq` |

Captures resolve by Binding Identity. Without a list, no Move, Borrow, Reborrow ([§3.5](03-types-and-values.md#35-copy-and-move)) or partial capture is inferred, so a Non-Copy root is rejected even when only a Copy Field is read; an existing `ref/T` is Copied with its dependencies, and Copy that depends on generic parameters needs declared evidence.

An explicit entry names one binding and acquires it at creation exactly as its initialization would ([§3.5](03-types-and-values.md#35-copy-and-move), [§13.5](13-operators-and-assignment.md#135-explicit-operations), [§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects)): a bare entry Reborrows a binding holding `uniq/T` or `objuniq/T` and rejects other Non-Copy bindings, and `x@ref` or `x@uniq` borrows the outer slot, on which the Closure then depends ([§15.8](15-ownership-and-lifetime-analysis.md#158-captured-and-erased-dependencies)).

Only outer runtime locals and parameters are captured. Type names, Containers, static storage (static Fields and Properties, whose direct use is a static access, [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)) and named functions, including local named functions, are not captures. Top-level locals are ordinary locals and are captured like any other. Contextual `self` and a setter's `value` are never captured implicitly; explicit captures of them obey the receiver and accessor rules. In a constructor or `drop`, no entry captures `self` ([§6.3.4](06-declarations-and-containers.md#634-the-construction-receiver), [§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)). The contextual `storage` binding cannot be captured by name.

Explicit entries execute left to right, unused ones included; duplicates and names of parameters are errors. Inferred captures execute once each, in order of first occurrence in selected source.

A capture without `var` is a `let`-like environment binding, whatever its source's mutability; a value capture is a snapshot, and `var` changes only the binding's mutability. In a call, each environment binding acts as a local of the call with its capture's `let`/`var` mutability: a Shared call only reads it, an Exclusive call may also assign a `var` binding and use captured `uniq` referents, and only a Consuming call may move a capture out. A consumed `let` capture cannot be reinitialized. Environment bindings are not parameters, so the owned-Type parameter restriction of [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition) does not apply to them. No environment may borrow its own owned capture through another capture.

**Nested Closures** acquire through every enclosing environment: a binding used only by the inner Closure must also be captured by the outer one, and each omitted list requires Copy on its own. Moving an outer environment value into an inner Closure makes the outer call Consuming. An inner `[x@uniq]` borrows the outer environment binding `x`: an error when it is `let`, otherwise judged by the outer call's permission ([§7.7.3](#773-call-receiver)).

```kimi
let number: i32 = 10
let copied = func () => number              // Copy capture.
let text = makeText()                       // string: Non-Copy.
// let bad = func () => text                // Error: an explicit list is required.
let holder = func [text@move] () => ()      // The transfer happens even if unused.

var value: i32 = 1
let r = value@uniq
var bump = func [r] () => r@follow += 1     // [r] Reborrows r as uniq/i32.
bump()                                      // An Exclusive call.
```

### 7.7.3. Call receiver

Each concrete Closure has one minimum **Call Receiver Requirement**, from all resolved operations of its body in selected source:

| Requirement | Receiver | Body access |
| --- | --- | --- |
| Shared | `ref/Self` | Reads the environment |
| Exclusive | `uniq/Self` | Mutates the environment or captured referents |
| Consuming | `owner/Self` | Moves values out of the environment |

A Move on any possible path requires Consuming. Where a conditional acquisition plan ([§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects)) gives admitted cases different effects, each case has its own requirement, and every use is checked in every case ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)); for example, a bare acquisition of a captured value whose Type is a pair layer makes the call Exclusive exactly in the cases where it Reborrows.

A direct call acquires the receiver like a method receiver ([§7.3.3](#733-method-calls)). An owned Closure Place is borrowed shared for a Shared call and exclusively for an Exclusive call when its lending point is exclusively writable ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)); `uniq/F` and `ref/F` values are Reborrowed, and `ref/F` cannot supply an Exclusive call. A Consuming call Copies a Copy Closure and otherwise needs `c@move()`.

The call signature keeps task-slot presence, receiver, parameter and result Types, Origins and result Loan dependencies; a call through a signature with a task slot is a task call ([§24.3](24-suspension-and-asynchronous-tasks.md#243-task-calls)). Receiver protection starts before later arguments are evaluated ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)), also in indirect and Callable calls. Generic calls follow the declared Callable receiver ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)).

```kimi
let count: i32 = 0
var next = func [var count] () -> i32
    count += 1
    return count
let first = next()        // 1: an Exclusive call; the outer count stays 0.

let item = makeResource() // Non-Copy.
let take = func [item@move] () => item@move
let taken = take@move()   // Consuming: transfers item out.
// take()                 // Error: take was consumed.
```

### 7.7.4. Function references and erasure

A function reference is resolved under [§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries) and has its Function Item Type, including bound generic arguments and Origin contract; different declarations have distinct Types. `drop` cannot be taken as a value. Requirement references, including built-in witnesses ([§13.3.3](13-operators-and-assignment.md#1333-arithmetic-contracts)), follow the same rules. A safe compiler-implemented function is referenced the same way, and a requirement value keeps its selected implementation.

At a position whose fixed expected Type ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)) is a common Function Type, a Function Item or concrete Closure is erased implicitly exactly when:

- its signature fits, with the same task-slot presence ([§24.2.4](24-suspension-and-asynchronous-tasks.md#2424-compatibility)), and its minimum receiver is Shared;
- its complete environment is Owned, and its result does not borrow the environment;
- all public Origin and Loan contracts hold, its results carry no mutable-static anchor ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)), and ordinary acquisition of the source is legal.

Erasure is never chained with another implicit adaptation: a borrowed Closure is not erased ([§3.2.1](03-types-and-values.md#321-callable-value-types)), an erased value is not then borrowed, and nothing inside an existing structure is erased. Explicit acquisition may precede erasure ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)). The common-Type search of [§14.9.2](14-control-flow.md#1492-target-result-type-and-expression-type) never erases. A qualified Case construction erases its payload like the leading-dot form ([§6.5.2](06-declarations-and-containers.md#652-case-references)).

Erasure is an owning conversion, distinct from a Copy or Move of the source; it acquires the source ordinarily ([§3.5](03-types-and-values.md#35-copy-and-move)) and never repeats captures. The erased value is Non-Copy. Its layout, allocation count and inlining are implementation-defined; an omitted allocation preserves values, Moves, Loans and cleanup ([§15](15-ownership-and-lifetime-analysis.md#15-ownership-and-lifetime-analysis)) and cannot fail. If erasure allocates and the allocation fails, the program Aborts, with no Move rollback (location: [§22.5.1](22-core-execution-and-foreign-functions.md#2251-abort-location)). Passing a concrete Closure where a `Callable` is expected is not erasure.

```kimi
func inc(value: i32) -> i32 => value + 1
func twice(action: ref/((i32) -> i32), value: i32) -> i32 => action(action(value))

let text = "hello"
let closure = func [text@move] () => ()
let erased: () -> () = closure@move                    // Transfer, then erasure; closure is Moved.
// let view: ref/(() -> ()) = func () => ()             // Error: erasure cannot then borrow.

var handler: (i32) -> i32 = inc                        // Erased: Non-Copy.
handler = inc                                          // Erased again; the old value is destroyed.
let next = handler@move
// let mixed = if ready => next@move else => inc       // Error: no common Function Type.
// let result = twice(inc, 1)                          // Error: erasure and a borrow are two operations.
```
