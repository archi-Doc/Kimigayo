# 11. Properties

[Specification index](../SPEC.md)

## 11.1. Property kinds

A concrete Property is `let` (immutable storage), `var` (mutable storage) or `computed` (accessors only). A Contract `property` requirement promises operations ([§11.9](#119-contract-property-requirements)).

| Declaration | `get` | `set` | Declaration initializer |
| --- | --- | --- | --- |
| `let` | Standard when omitted | Forbidden | Optional for instance; required for static |
| `var` | Standard when omitted | Standard when omitted | Optional for instance; required for static |
| `computed` | Body required | Optional; body required | Forbidden |
| Contract `property` | Required | Optional | Forbidden |

- Instance Properties belong in a struct, static Properties in a group or rootgroup. A group nested in a struct adds no instance Field; its Properties stay static ([§22.4.4](22-core-execution-and-foreign-functions.md#2244-static-storage-keys)). Enums have no Properties. Local `let`/`var` are bindings; a local `computed` is forbidden.
- Properties share the Value namespace. Attributes follow [§6.7](06-declarations-and-containers.md#67-attributes); inline `has` is only for requirements.
- A **Stored Property** is a `let` or `var` with one storage slot, its **Field**. The declaration, never an accessor body, makes it stored. All source access to a Field obeys its Property's accessors.

| Type contract | Meaning |
| --- | --- |
| Stored `: T` | Storage Type |
| Stored custom `get` / `set` | `get` result exactly `T`, proven Copy; `set` input exactly `T`, result Unit |
| Computed or Contract `: T` | Getter result Type; a computed or explicit requirement setter input may differ |

Types are compared by complete structure and Origin bindings. Copy capability selects the acquisition, never a different result Type such as `T` versus `ref/T`.

```kimi
struct Counter
    public let limit: i32 = 100
    public var count: i32 = 0
    public computed remaining: i32
        get() -> i32 => self.limit - self.count
```

## 11.2. Standard access

A bodyless `get` or `set` selects standard access; customizing one accessor does not change the other. Standard `get` is Place access, not a getter call: acquisition follows [§3.5](03-types-and-values.md#35-copy-and-move) and [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition), and a borrow does not acquire the value first. Standard `set` initializes or replaces the storage under the ordinary state and cleanup rules.

An operation on a Field needs these accessible standard accessors, on the Field itself and on every ancestor stored Property of its path:

| Operation | The Field itself | Each ancestor stored Property |
| --- | --- | --- |
| Copy or shared borrow | `get` | `get` |
| Move | `let`: `get`; `var`: `get` and `set` | Its own Move row |
| Exclusive borrow | `get` and `set` | `get` and `set` |
| Write | `set` | `get` and `set` |

A custom accessor never satisfies these requirements: through a custom `get`, reads and borrows use the getter result ([§11.5](#115-getter-results)), never the hidden storage; a custom `set` is called only by an assignment whose final target is the Property; a write, Move or exclusive borrow of a child path through it, including method-receiver and operator adaptations, is an error (the table above). The ordinary capability, initialization, Origin, Loan and Move Path rules also apply. Simple assignment needs the final target's `set` ([§13.7](13-operators-and-assignment.md#137-assignment)).

Borrows follow [§13.5.5](13-operators-and-assignment.md#1355-follow). For a Field of Type `ref/U`, `@ref` borrows the Field as `ref/(ref/U)`; bare acquisition or an expected `ref/U` Copies the stored reference ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)), and `@follow@ref` Reborrows its referent.

```kimi
// position: Point is Copy, with standard get and custom set.
// object.position.x = 10     // Error: the ancestor position needs a standard set.
var next = object.position
next.x = 10
object.position = next        // OK: calls position's set.
```

### 11.2.1. Moves and mutability

Moving out of a `var` requires an accessible standard `set` but does not call it or require a writable receiver. A `let` slot is never exclusively borrowed or written (it has no `set`), and a Moved `let` cannot be reinitialized. Slot immutability is not deep: a stored reference's referent keeps its own capabilities.

```kimi
// resource: public standard get, private standard set; holder is owned.
let view = holder.resource@ref    // OK: shared storage borrow.
// let r = holder.resource@move   // Error: set is inaccessible here.
```

### 11.2.2. Field paths and partial Moves

A safe Field Move requires an owned struct Place, an eligible static Move Path, completed construction, an Initialized and complete target, and the ancestor `drop` conditions. Borrowed and object receivers, static storage and the destruction receiver ([§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)) cannot supply safe extraction. Raw-pointer operations keep their Unsafe rules.

An inherited Field is reached through a unique base path with substituted Type and Origin arguments. The path acquires no intermediate base, converts no `ref/Derived` to `ref/Base`, keeps the receiver's category and grants no private access. A base subobject cannot be Moved, replaced or reconstructed on its own.

Standard access may use a complete remaining Place after a sibling Move. A custom, computed or Contract requirement accessor needs a complete receiver; callers do not infer disjointness from its body or implementation.

```kimi
struct Holder
    public var resource: Resource
    public var count: i32 = 0
    public computed displayCount: i32
        get() -> i32 => self.count + 1

func refill(holder: Holder) -> Holder
    var object = holder@move
    let item = object.resource@move     // Partial Move.
    let count = object.count            // OK: complete remaining storage.
    // let shown = object.displayCount  // Error: a computed getter needs a complete receiver.
    object.resource = makeResource()    // Restore through the standard set.
    return object@move
```

### 11.2.3. Generic Fields

Standard accessors need no Copy constraint. A stored custom getter needs `T is Copy` proven under the declaration's generic premises. A custom setter must be valid for every admitted `T`. When Copy is unproven, a bare read of the Field is an error ([§8.14](08-generics-constraints-and-contracts.md#814-generic-access-effects)): constrain `T is Copy`, borrow the Field, or Move it with `@move`, which leaves the aggregate incomplete. A Move or Loan failure never retries overload selection ([§10.6](10-overload-resolution-and-inference.md#106-usage-legality-and-operators)).

```kimi
struct Box<T>
    public var item: T

func take<T>(box: Box<T>) -> T
    // let x = box.item          // Error: T is not proven Copy.
    return box.item@move          // OK: box is incomplete afterwards.
```

## 11.3. Accessor functions

Custom and computed accessors write their parameter and result Types; only the receiver may be omitted. Accessors use the common Body ([§14.2](14-control-flow.md#142-bodies-and-evaluation-contexts)); a setter discards a single-item expression and completes with Unit. Parameter defaults and `!` boundaries are forbidden. A setter's `value` is an immutable binding with ordinary acquisition and cleanup.

Inside a stored custom accessor, the contextual binding `storage` denotes the Field itself, without calling an accessor again; it obeys the ordinary Place rules and cannot be redeclared ([§9.3](09-names-signatures-and-access.md#93-local-scopes-and-contextual-names)). Computed Properties and requirements have no `storage`.

```kimi
struct Gauge
    public var level: i32 = 0
        get() -> i32
            return storage
        set(value: i32) -> ()
            // let storage = value    // Error: storage cannot be redeclared here.
            storage = if value > 100 => 100 else => value
```

### 11.3.1. Accessor receivers

The receiver of an accessor is fixed by the operation, never by the body. An instance `get` takes `self: ref/Self` and an instance `set` takes `self: uniq/Self` before `value`; omitted, they are inserted. Static accessors have no receiver, and writing one is an error. An instance `get` receiver must be `ref/Self` and an instance `set` receiver `uniq/Self`; Origin annotations are allowed, as in `get(self: ref/Self during static) -> T`. Any other receiver, including `Self`, the other borrow form, the `obj`, `rc`, `arc`, `objref` and `objuniq` forms, or a Core other than `Self`, is an error.

**Getter receiver.** A read `x.p` through a custom, computed or required `get` acquires `x` as a Receiver Expression in shared mode, by the `ref/Self` row of [§7.3.3](07-functions-and-callable-values.md#733-method-calls). It starts no exclusive reservation ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)) and needs no writable lending point ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)), so a `let` binding, an owned parameter, an `rc`/`arc` payload and mutable static storage are readable.

**Setter receiver.** `x.p = v`, compound assignment, `++` and `--` locate the target with write permission ([§13.7.1](13-operators-and-assignment.md#1371-simple-assignment)) and acquire the receiver of a custom, computed or required `set` as `uniq/Self`, never by Copy or Move; `objuniq/T` satisfies it through [§7.3](07-functions-and-callable-values.md#73-receivers). `holder@move.item = value` and `point@copy.x = 10` update the temporary or Copy they create ([§3.6](03-types-and-values.md#36-temporaries)). Compound updates protect the destination as [§13.7.2](13-operators-and-assignment.md#1372-compound-assignment) states.

An operation that needs exclusive access to its receiver or consumes it is a function ([§7.3](07-functions-and-callable-values.md#73-receivers)), named by the pairs of [§4.9.2](04-arrays-indexing-and-slices.md#492-naming-convention).

Calls through object borrows and base-subobject projections require ObjectCallCompatible Proven ([§12.7](12-expressions.md#127-object-receiver-compatibility)); a `ref/Self` accessor is Proven by its shape ([§12.7.1](12-expressions.md#1271-effect-rules)), and a `uniq/Self` receiver alone supplies no proof.

```kimi
struct Meter
    var hits: i32 = 0
    public computed hitCount: i32
        get() -> i32 => self.hits                 // Same as get(self: ref/Self).
    public computed reading: i32
        get(self: uniq/Self) -> i32 => self.hits  // Error: a getter reads through ref/Self.
    public func nextReading(self: uniq/Self) -> i32
        self.hits += 1                            // Advancing state is a function.
        return self.hits
```

### 11.3.2. Accessor accessibility

An accessor has its Property's access unless it states a strictly narrower one. Access narrows from `public` to `protected internal`, then to `protected` or `internal` (unordered), then to `private protected`, then to `private`. A repeated or broader modifier is an error. `set` access need not be contained in `get` access.

### 11.3.3. Custom setters for Non-Copy values

Assigning to `storage` is ordinary assignment ([§13.7.1](13-operators-and-assignment.md#1371-simple-assignment)): it destroys the old value and installs the new one. The exclusive receiver cannot Move the old value out; it may borrow it shared or use an operation that keeps the Field initialized ([§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates)). The setter returns with a complete receiver. It may leave the storage unchanged; an unconsumed `value` is destroyed, not returned to the caller.

```kimi
struct Holder
    public var item: Resource
        set(self: uniq/Self, value: Resource) -> ()
            storage = value@move

holder.item = makeResource()
inspect(holder.item@ref)          // OK: standard shared access.
// let item = holder.item@move    // Error: the custom set blocks a Move.
```

## 11.4. Computed Properties

A computed Property has no storage, initializer, bodyless accessor or inline `has` list. Its getter result matches the header Type after Origin completion. The optional setter returns Unit and may take another input Type; assignment fits to the setter input ([§13.7.1](13-operators-and-assignment.md#1371-simple-assignment)).

**Results.** A getter may return a new value, a Copy of a stored reference, or a borrow of `self`. A shared receiver cannot extract an owned Non-Copy part or Reborrow a stored `uniq/T` exclusively ([§13.5.5](13-operators-and-assignment.md#1355-follow)). No get/set round-trip equality is promised.

```kimi
struct Holder
    private var item: Resource
    public init(item: Resource) => self.item = item@move
    public computed view: ref/Resource
        get() -> ref/Resource => self.item@ref

let holder = Holder.init(makeResource())
inspect(holder.view)                     // OK: a let binding is readable.
```

## 11.5. Getter results

A custom, computed or Contract `get` produces a function result. Its owned Temporary Place and inline descendants cannot be assigned, compound-updated, incremented, decremented or exclusively borrowed. Parentheses, projections, exclusive receiver acquisition ([§7.3](07-functions-and-callable-values.md#73-receivers)) and use as a `match` or `for` Subject ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)) keep the restriction, so a `uniq/Self` method cannot be called on the result. A further getter on the result is allowed. A returned reference keeps its referent's own capabilities. Updating the Property through `set` stays allowed.

The restriction belongs to that Temporary Place only: a value acquired from it into a local or an argument follows the destination's rules ([§3.6](03-types-and-values.md#36-temporaries)). Borrowing never extends the temporary's lifetime ([§3.6](03-types-and-values.md#36-temporaries)). For a standard `get`, `@ref` borrows the storage instead.

```kimi
// position: Point is Copy, with custom get and standard set.
// object.position.x = 10        // Error: updates only the getter temporary.
inspect(object.position@ref)     // OK: the temporary lasts through the call.
let view = object.position@ref
// inspect(view)                 // Error: the initializer temporary has ended.
```

## 11.6. Property Types and Origins

A stored Property's Type is its annotation or, without one, the Type of its declaration initializer; a Property with neither is an error. The Type is never inferred from accessors or later assignments. If typing the initializer needs that Type, directly, through another inferred Property, or through a fact about a Type that contains the Property (its Copy, Owned or layout), the Property needs an annotation (error). Cycles between inferred initializers are errors.

Storage binds every required Origin explicitly ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)); `self` creates no self-borrowing storage. Accessors introduce per-call Origins through borrow annotations ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)), not an Origin list. Attached `origin` clauses precede an accessor's executable items, in source order including excluded syntax ([§19.4](19-compile-time-directives.md#194-excluded-syntax)), and a Property's own clauses precede its accessors. Inherited Origin names cannot be redeclared.

A stored custom accessor's `value` and result inherit omitted Origins from the storage Type `T` (not from `self`), before function elision; written bindings are checked against it. No accessor parameter may narrow the calls the storage allows. Computed and required accessors use ordinary function elision: a getter whose only borrowed input is `self` may elide its result Origin to `self`.

```kimi
group Tags
    public func tagOf<T>() -> i32
        T is Owned
        return 0

struct Entry
    var tag = Tags.tagOf<Entry>()        // Error: proving Entry is Owned needs tag's inferred Type.
    // var tag: i32 = Tags.tagOf<Entry>() // OK: the annotation breaks the cycle.
```

## 11.7. Construction and destruction

Declaration initializers and a constructor's first placement initialize own storage directly, without accessor calls. First placement requires every incoming path to be uninitialized and never to have completed first placement; a Move or destruction does not reset this history. A Field with a declaration initializer counts as initialized.

| Constructor write | Before first placement on every path | Already initialized, or mixed paths |
| --- | --- | --- |
| `let` | Direct initialization | Error |
| `var`, standard `set` | Direct initialization | Ordinary placement or replacement |
| `var`, custom `set` | Direct initialization | Error |

The construction receiver cannot call a custom or computed accessor. Standard `get` may Copy or borrow initialized own storage; a Non-Copy Move, inherited storage access and whole-`self` acquisition or borrowing are forbidden. The other rules of [§6.3](06-declarations-and-containers.md#63-constructors) apply. Layout, Copy derivation, implicit construction, partial Moves and destruction use only stored Fields and base components; destruction calls no accessor.

```kimi
// In a constructor: level has a custom set and no initializer.
if ready
    self.level = 100   // First placement on this path; no setter call.
else
    self.level = 200
// self.level = 300    // Error: construction cannot call the custom set.
```

## 11.8. Static storage

A group or rootgroup stored Property needs an initializer and an Owned complete storage Type ([§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned)). Each slot initializes on first actual access ([§22.4](22-core-execution-and-foreign-functions.md#224-program-startup-and-static-storage)); an accessor call initializes only the slots its execution touches.

A shared borrow of initialized immutable static storage may be `during static`. A borrow of mutable static storage, including an inline subplace, has a finite Origin and leaves the body only as the Static anchors rule of [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls) allows. Copying a reference stored in mutable static storage is not a borrow of that storage; the Copy keeps the stored referent and Origin.

```kimi
group Config
    public let limit: i32 = 100
    public var name: string = ""

func limitRef() -> ref/i32 during static
    return Config.limit@ref       // OK: immutable static storage.
// func nameRef() -> ref/string during static => Config.name@ref  // Error: mutable.
```

## 11.9. Contract property requirements

A `property` requirement is instance-only, with a mandatory `get` and an optional `set`. It has no accessor access modifiers, Attributes, defaults, `!` boundaries, initializer, storage or body. `has get` requires `get(self: ref/Self) -> T` by value, and `has set` requires `set(self: uniq/Self, value: T) -> ()`. The explicit form writes the signatures with the receivers of [§11.3.1](#1131-accessor-receivers); its setter input may differ from `T`. Object handles and base projections satisfy these receivers as [§11.3.1](#1131-accessor-receivers) and [§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection) state.

```kimi
contract Counted
    property count: i32 has get
contract ReplaceableItem
    property item: ref/Resource
        get() -> ref/Resource
        set(value: Resource) -> ()
```

### 11.9.1. Implementation compatibility

Ordinary member lookup selects an implementation; an inherited conformance keeps its mapping ([§8.6](08-generics-constraints-and-contracts.md#86-conformance)). A failure never retries another Name or base. After substituting the requirement's `Self`, generic arguments and associated Types, function requirement compatibility applies: no implicit conversions or stronger preconditions. The storage Type need not equal the header. Unknown generic capabilities establish no compatibility.

### 11.9.2. Standard access witnesses

Standard access to a Field of Type `F` implements a requirement only through these bridges:

| Required operation | Standard implementation |
| --- | --- |
| Shared receiver -> `F` | Copy, if `F` is proven Copy |
| Shared receiver -> `ref/F` | Shared borrow of the Field |
| Exclusive receiver, input `F` -> Unit | Accessible standard `set` of a `var` |

Each bridge needs the standard accessors that the operation needs under [§11.2](#112-standard-access) to be accessible where the conformance is declared, and its result Origin is bounded by the receiver.

Storage behind a custom `get` is unavailable, and nothing else is synthesized. An inherited Field is reached by standard projection ([§11.2.2](#1122-field-paths-and-partial-moves)) under the premises of its conformance path, with no base-subobject receiver ([§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection)). Bridges add no lookup candidates.

```kimi
contract Viewable
    property item: ref/Resource has get

struct Slot
    Self is Viewable
    public var item: Resource    // OK: shared borrow bridge.
    // A by-value `property item: Resource has get` would need Resource to be Copy.
```
