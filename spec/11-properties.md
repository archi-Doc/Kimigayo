# 11. Properties

[Specification index](../SPEC.md)

A concrete Property is `let`, `var` or `computed`. A Contract uses `property` to require operations.

```text
Property
├─ let       immutable storage
├─ var       mutable storage
├─ computed  accessor functions, no storage
└─ property  Contract requirement, no storage promise
```

| Declaration | Get | Set | Declaration initializer |
| --- | --- | --- | --- |
| `let` | Required; standard when omitted | Forbidden | Optional for instance; required for static |
| `var` | Required; standard when omitted | Required; standard when omitted | Optional for instance; required for static |
| `computed` | Body required | Optional; body required if present | Forbidden |
| Contract `property` | Required operation | Optional operation | Forbidden |

Concrete Properties belong in struct, group and rootgroup bodies: instance Properties in a struct, static Properties in a group or rootgroup. Enums do not permit them. Local `let`/`var` declarations remain bindings, and local computed declarations are forbidden. Properties share the Value namespace. Each accessor appears at most once, in either order. Attributes follow §6.5; inline `has` is only for requirements.

Properties declared directly in a group nested inside a struct remain static; the group adds no instance Field. Their storage identity, initialization, sharing and cleanup follow [§22.2.4](22-core-execution-and-foreign-functions.md#2224-static-storage-in-inherited-environments).

A **Stored Property** is a `let` or `var` declaration with one storage slot. The storage kind is determined by the declaration, never by accessor bodies. In storage, layout and Move Path rules, **Field** means this slot, not a separate declaration kind. All source access to a Field obeys its Property's operation permissions.

| Type contract | Meaning |
| --- | --- |
| Stored `: T` | Storage Type; standard value acquisition also produces `T` |
| Stored custom `get` | Result exactly `T`; `T` must be proven Copy |
| Stored custom `set` | Input exactly `T`; result Unit; no Copy constraint |
| Computed `: T` | Getter result Type; the setter input may differ |
| Contract `property p: T` | Required getter result Type; an explicit setter input may differ |

Types are compared by complete structure and bound Origin dependencies (§11.3), not merely by Core. Copy capability selects the acquisition; it never selects a conditional result Type such as `T` versus `ref/T`.

## 11.1. Standard access and acquisition

**Standard `get` is a Place access contract, not a getter function.** A bodyless `get` or `set` selects a standard operation. Omitted accessors are supplied according to the declaration table; customizing one does not change the other.

```kimi
public let limit: i32 = 100
public var count: i32 = 0
public var resource: Resource
    get
    private set
```

Standard `get` exposes permitted operations on the storage. Value acquisition Copies `T` if it is Copy and otherwise Moves, only from a Movable Place (§15.1.5). Borrowing follows the existing adaptation rules without first acquiring the value. Standard `set` directly initializes or replaces the storage under the ordinary state and cleanup rules.

| Direct storage operation | Required accessible standard accessors |
| --- | --- |
| Copy or shared borrow | `get` |
| Move from `let` | `get` |
| Move from `var` | `get` and `set` |
| Exclusive borrow | `get` and `set` |
| Write | `set` |

Every row additionally requires valid receiver capabilities, initialization and completeness, Origins, Loans, Move Paths, and construction and destruction conditions. A custom accessor cannot satisfy a requirement for a standard accessor. Simple assignment needs the final target's `set`, not its `get`; getters needed to locate that target are checked separately (§13.7).

| `var` configuration | Read/borrow target | Update |
| --- | --- | --- |
| Standard `get` + standard `set` | Storage, subject to the table | Direct `set` |
| Custom `get` + standard `set` | Getter result; no direct storage borrow or Move | Direct `set` |
| Standard `get` + custom `set` | Storage Copy or shared borrow; no direct Move or exclusive borrow | Call `set` |
| Custom `get` + custom `set` | Getter result; no direct storage access | Call `set` |

Shorthand borrows keep the semantics of §13.5.5. For a slot of Type `F = ref/U`, `@ref` copies the stored shared reference, while `@ref/F` borrows the slot as `ref/ref/U`. Specify the complete slot Type to request slot access; a failed Reborrow cannot retry as slot borrowing.

### 11.1.1. Access and mutability

A Move is destructive access, not assignment. Moving out of a `var` requires an accessible standard `set` but neither calls it nor adds a receiver write-capability requirement. A `let` may be consumed, but the Move does not reset its first-initialization history: a Moved `let` cannot be reinitialized or expose its slot for exclusive borrowing. Restoring a `var` requires ordinary write permission.

```kimi
// Outside the declaring struct; holder is owned and complete.
// resource has public standard get and private standard set.
let r = holder.resource       // Error: set is inaccessible for var Move.
let view = holder.resource@ref // OK: shared storage borrow.
```

Slot immutability is not deep immutability: a stored reference or object handle may grant access to a separate referent under its own capabilities and Loans.

### 11.1.2. Move paths and inherited fields

A safe storage Move requires an owned struct Place, an eligible static Move Path, completed construction, an Initialized and complete target, and valid access, Origins, Loans and ancestor `deinit` conditions. Borrowed and object receivers and static storage cannot supply safe extraction. Raw-pointer operations keep their Unsafe rules.

Inherited lookup selects a Field identity and a unique base path, substituting Type and Origin arguments. Following the path acquires no intermediate bases and implies no `ref/Derived`-to-`ref/Base` conversion. The original receiver category is preserved, and inheritance grants no private access. State and Loans are tracked by base path and Field identity. A base subobject itself cannot be independently Moved, replaced or reconstructed.

Every storage projection checks the permissions of the enclosing Properties. Child writes, borrows and consumes, including implicit method-receiver or operator adaptations, cannot bypass an ancestor's custom or inaccessible setter. References that reach separate referents follow their own capabilities. A path crossing a custom getter continues from its result under §11.2.3, never from its hidden storage.

```kimi
// position: Point is Copy, with standard get and custom set.
object.position.x = 10       // Error: bypasses position's setter.
object.position.x.modify()   // Error if modify requires an exclusive receiver.
var next = object.position
next.x = 10
object.position = next      // OK: calls the setter.
```

Standard access may use a complete remaining Place after a sibling Move. A custom accessor or computed call needs a complete receiver and keeps its declared function footprint; callers cannot infer disjointness from its body. Initial-construction restrictions still apply even when all slots are initialized (§11.3.1).

```kimi
// Both fields have standard accessors; partial Move/deinit conditions hold.
let item = object.resource
let count = object.count       // OK: complete remaining storage.
let shown = object.displayCount // Error if this getter borrows incomplete object.
object.resource = makeResource() // Restore through accessible standard set.
```

### 11.1.3. Generic acquisition

Standard accessors need no Copy constraint. A stored custom getter needs a proof of `T is Copy` under the declaration's generic premises, not merely for a chosen instantiation. Custom setters need no Copy constraint but must be valid for every admitted `T`.

```kimi
struct Box<T>
    public var item: T

func view<T>(box?: ref/Box<T>) -> ref{box}/T
    return box.item@ref/T

func readCopy<T>(box?: ref/Box<T>) -> T
    T is Copy
    return box.item

struct AssignedBox<T>
    public var item: T
        set(self: uniq/Self, value: T) -> ()
            storage = value
```

`AssignedBox` cannot supply an unconstrained `T` by value through standard `get`, because its Non-Copy case would require a forbidden storage Move; shared borrowing remains available.

When Copy is unproven, the conditional Copy/Move state is verified under §8.10. Runtime Copy values still Copy, and the result Type remains `T`. A later use must be valid in both cases; reinitialization or explicit borrowing may establish valid continued use. Overload selection is not retried after a Move or Loan failure.

```kimi
func test<T>(box?: Box<T>) -> ()
    let x = box.item
    inspect(box@ref) // Error: box may be incomplete after non-Copy Move.
// Adding T is Copy makes this subsequent shared use valid.
```

## 11.2. Accessor functions

Custom and computed accessors declare their input and result Types explicitly; only the receiver may use the fixed shorthand below, and bodies infer none of these Types. Both accessors use the common Body (§14.2). A single-item getter follows its fixed declared return Type; a setter discards its single-item expression and completes with Unit. Explicit returns must still fit the declared result. Parameter defaults and `?` markers are forbidden; Property access supplies the receiver and setter value through its dedicated syntax, without ordinary argument labels. Static accessors have no receiver. A setter's `value` parameter is an initialized immutable binding with ordinary argument acquisition and cleanup.

**Receiver shorthand.** In an instance Property, an accessor signature without a written receiver gets `self: ref/Self` inserted for `get`, and `self: uniq/Self` before `value` for `set`. This applies to stored custom accessors, computed accessors and explicit Contract requirement signatures: `get() -> T` and `set(value: U) -> ()` keep an instance receiver and bind contextual `self` in their bodies and Origin annotations. The containing Property determines instance or static kind, so group and rootgroup accessors remain receiverless. An explicit receiver Type remains available and must satisfy the accessor restrictions. Parameter lists, setter input Types and result Types are still required in custom and explicit requirement signatures; bare `get`/`set` keep their standard-accessor rules. No receiver Type is inferred from the body, and operations needing a stronger receiver do not change the default. Origin completion, signature matching and call/borrow behavior are those of the expanded signature, with the receiver as input slot zero.

A stored instance custom `get` uses `self: ref/Self` and returns the storage Type `T`, which must be Copy. A custom `set` uses `self: uniq/Self, value: T` and returns Unit. The static forms are `get() -> T` and `set(value: T) -> ()`.

```kimi
public var level: i32 = 0
    get() -> i32
        return storage
    set(value: i32) -> ()
        storage = clamp(value, 0, 100)
```

Inside a stored accessor, the contextual `storage` binding denotes the accessor's own slot without recursive accessor invocation. It obeys receiver capabilities, `let`/`var` mutability, initialization, Origins and Loans, and is unavailable in computed Properties and requirements. A custom getter may Copy a stored reference of Type `T`; this differs from exposing a direct borrow of the slot, which requires standard `get`.

### 11.2.1. Accessor accessibility

Accessors inherit their Property's access unless explicitly restricted. A restriction must be strictly narrower in declared access; enclosing declarations cannot justify a repeated or broader modifier. Protected forms keep their structure-member requirements.

| Property access | Permitted explicit accessor restrictions |
| --- | --- |
| public | protected internal, protected, internal, private protected, private |
| protected internal | protected, internal, private protected, private |
| protected | private protected, private |
| internal | private protected, private |
| private protected | private |
| private | None |

Neither accessor needs to keep the Property's full access domain, and `set` access need not be contained in `get` access. API signature and protected-receiver checks still apply. Object and projection calls require published ObjectCallCompatible Proven (§12.4.4); writing `ref/Self` or `uniq/Self` alone supplies no proof.

### 11.2.2. Computed properties

A computed Property has no storage, initializer, bodyless standard accessor or inline `has` list. Its explicit getter result must match the header Type after Origin completion. The optional setter returns Unit and may have a different input Type. Instance receivers follow explicit ordinary function contracts, including ownership-bearing receivers; static accessors omit `self`.

```kimi
struct Temperature
    private var celsius: f64 = 0.0
    public computed fahrenheit: f64
        get() -> f64
            return self.celsius * 1.8 + 32.0
        set(value: f64) -> ()
            self.celsius = (value - 32.0) / 1.8

struct Holder
    private var item: Resource
    public computed view: ref/Resource
        get(self: ref/Self) -> ref/Resource
            return self.item@ref/Resource
    public computed result: Resource
        get(self: Self) -> Resource
            return self.item // Only with valid partial Move/deinit conditions.
```

Non-Copy results must be legally created or acquired; a shared receiver cannot supply an owned Non-Copy field by extraction. An owning getter may consume the complete receiver, and a later setter cannot reuse it. No hidden duplication, restoration or get/set round-trip equality is promised.

### 11.2.3. Getter results and temporaries

Stored custom `get`, computed `get` and Contract `get` produce function results; adaptations apply to that result, not to backing storage.

**An owned getter-result Temporary Place and its inline descendants cannot be directly assigned, compound-updated, incremented or decremented, or exclusively borrowed.** Parentheses, projections and implicit exclusive receiver adaptation preserve this restriction. Updating the Property itself through `set` is separate and remains allowed (§13.7).

```kimi
// position: Point is Copy, with custom get and standard set.
object.position.x = 10          // Error: updates only the getter temporary.
object.position.x += 1          // Error.
let edit = object.position@uniq/Point // Error.
var next = object.position
next.x = 10                     // OK: ordinary local storage.
object.position = next          // OK: calls position's set.
```

The restriction belongs to that Temporary Place, not to all values derived from it. Value acquisition into a local or an ordinary function argument follows the destination's normal rules, and an ordinary function's owned result follows §3.6 even if an argument came from a getter. References still pointing into the original restricted temporary keep its restrictions. Separate referents reached through returned references or object handles follow their own capabilities, Origins and Loans.

```kimi
// identity takes and returns Point by value.
identity(object.position).x = 10 // Ordinary function temporary rules.
// Neither example writes back to object.position.
```

Value acquisition, shared borrowing and legal ownership transfer remain allowed. Materialization and borrowing never extend a temporary's lifetime. The required borrow duration is inferred from uses, returns and retention, and a borrow is rejected if its referent cannot live that long. No blanket rejection of an unused borrow binding is added.

```kimi
// visible has a custom getter returning i32.
inspect(object.visible@ref)    // OK: temporary lasts through this call.
let view = object.visible@ref
inspect(view)                 // Error: initializer temporary has ended.
let saved = object.visible
inspect(saved@ref)             // OK: borrow the local instead.
```

For a standard `i32` `get`, `@ref` instead borrows the storage.

### 11.2.4. Non-Copy custom setters

A custom setter receives ownership of a Non-Copy `value` and may replace the storage. Assigning to `storage` secures its right-hand side, destroys any old value and installs the new one without calling the setter again; the ordinary assignment and cleanup failure rules apply. An exclusive receiver cannot directly Move an old Non-Copy value out: inspect it by shared borrowing, or use an authorized initialization-preserving operation. Conflicting Loans must end before replacement, and the setter must return with a complete receiver.

```kimi
struct Holder
    public var item: Resource
        set(self: uniq/Self, value: Resource) -> ()
            storage = normalize(value)

holder.item = makeResource()
inspect(holder.item@ref/Resource) // OK: standard shared access.
let item = holder.item            // Error: custom set blocks direct Move.
let edit = holder.item@uniq/Resource // Error: direct exclusive access.
```

A setter may return without updating the storage. Unconsumed input is destroyed normally; transferred input is neither destroyed twice nor automatically restored to the caller. Use a result-returning function when acceptance or rejection must be reported. These restrictions do not prohibit a legal whole-receiver Move or destruction. Initial placement does not invoke validation (§11.3.1).

Explicit accessor signatures may declare per-call Origins immediately after `get` or `set`: `get {a}(...) -> T`, `set {a}(...) -> ()`. Bodyless standard accessors have no declaration list. Inherited Origin names cannot be redeclared. The same rules apply to explicit Contract requirement signatures.

## 11.3. Types and Origins

A stored Type may be inferred only from its declaration initializer; otherwise an annotation is required. It is never inferred from accessors or later assignments. Storage explicitly binds the required Origins, including nested dependencies (§15.4); `self` does not create a self-borrowing storage contract.

Complete an accessor from an existing storage contract before applying function elision. A stored custom value input or result inherits omitted Origins from the corresponding complete storage Type `T`; explicit bindings are checked, not overwritten. The receiver retains its independent per-call Origin. Verify complete Type correspondence and the body afterward. No new accessor parameter may narrow the calls required by storage.

```kimi
struct View {source}
    public var value: ref{source}/i32
        get(self: ref/Self) -> ref{source}/i32
            return storage
        set(self: uniq/Self, value: ref{source}/i32) -> ()
            storage = value
```

Omitting `{source}` in the accessor Types above inherits the storage Origin. It does not default to `self` or introduce an independent setter input. The field itself still requires its explicit storage contract.

Computed and required accessors use the same function elision but no shared storage-Type comparison. A getter whose only direct borrowed input is `self` may elide its result Origin to `self`; setter input completion is independent. No Origins are created for absent accessors. Static getter and setter contracts use the ordinary receiverless rules. Copy reference and aggregate Types still undergo all lifetime and Loan checks.

### 11.3.1. Construction and destruction

Declaration initializers and a constructor's first placement initialize own storage directly, without accessor calls. First placement requires every incoming path to be uninitialized and never to have completed first placement; Move or destruction does not reset this history. Declaration initializers count as initialized. A mixed-state join never chooses dynamically between initial placement and a custom `set`.

| Constructor write to own storage | Definitely before first placement | Already initialized or mixed paths |
| --- | --- | --- |
| `let` | Direct initialization | Error |
| `var` with standard `set` | Direct initialization | Normal state-dependent placement or Replacement |
| `var` with custom `set` | Direct initialization | Error: custom `set` call during construction |

The construction receiver cannot call a custom `get`/`set` or a computed accessor, even after all slots are initialized. Standard `get` may Copy or borrow initialized own storage; Non-Copy Move, inherited storage access and whole-`self` acquisition or borrowing remain forbidden. Construction borrows end before completion (§6.2.3).

```kimi
// In a constructor: level has custom set and no declaration initializer.
if condition
    self.level = 100 // First placement on this path; no setter.
else
    self.level = 200 // First placement on this path; no setter.
self.level = 300     // Error: construction cannot call custom set.
```

If only one branch initialized `level`, a subsequent unconditional write would also be rejected for a custom `set`. A declaration initializer such as `level = 999` also bypasses validation; callers needing validated initial values must arrange it explicitly.

Construction keeps base-first order, declaration-order initializers, the ban on `self` and constructor parameters in declaration initializers, definite initialization, and completion checks after cleanup (§6.2.3); no zero initialization is added. Layout, Copy derivation, implicit construction, partial Move and destruction use stored slots and base components only. Computed Properties contribute none, and automatic destruction invokes no accessors.

### 11.3.2. Static storage

Group and rootgroup stored Properties require an initializer, an Owned complete storage Type (§15.2.3), and the per-slot lazy state machine of §22.2. Safe shared borrows of eligible immutable static storage may be retained, including inside aggregates. Acquisition, initialization and destruction dependencies are checked separately; Owned supplies no Loan or pointer-validity evidence.

Safe code cannot form a `{static}` borrow of mutable static storage, including an inline subplace or backing data that safe mutation can invalidate. A new borrow of such storage has a finite, use-bounded Origin and cannot be fitted to `static`, including through generic substitution or an accessor result. A function or custom getter can expose it only through a result Origin bounded by a borrowed input under §15.4, keeping the Field anchor in its effect summary (§15.6.4); a result Origin elided to `static` is an error. Copying an already stored reference preserves its original referent and Origin; it is not a borrow of the mutable slot. An eligible static borrow must be anchored in initialized immutable storage whose referenced path stays protected from safe mutation. Local Loans and shutdown checks remain necessary (§15.6.4, §22.2.3).

Actual slot access triggers initialization. Calling a custom or computed accessor initializes only the slots actually touched by its execution or callees, not every summarized effect. A standard write initializes the slot first and then replaces its value; a static `let` permits no external initialization or replacement. A custom `set` follows its function effects and may never touch the storage. Unused initializers still require validation. Static accessors have no `self`.

## 11.4. Contract property requirements

A `property` requirement is instance-only and promises operations, not storage. The header Type `T` is its getter result. `get` is mandatory and `set` optional, each at most once. Requirements have no accessor access modifiers, Attributes, parameter defaults or `?` markers, initializer, storage or body.

```kimi
contract Counted
    property count: i32 has get
contract MutableCounted
    property count: i32 has get, set
contract ReplaceableItem
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource
        set(self: uniq/Self, value: Resource) -> ()
```

`has get` requires `get(ref/Self) -> T` **by value**, not merely some readable access, and `has set` requires `set(uniq/Self, value: T) -> ()`. The explicit form states the signatures: its getter matches the header `T`, and its setter input may differ. Ordinary receiver and Origin contracts apply. For example, a shared receiver cannot Move a Non-Copy stored `Resource` to implement `property item: Resource has get`, whereas a `ref/Resource` requirement may use shared storage borrowing.

### 11.4.1. Operation compatibility

A new implementation is selected by ordinary member lookup; an inherited conformance keeps its mapping under §8.4.4. The selected `let`/`var`/computed operations are checked without retrying another Name or base candidate after a Type, accessor or accessibility failure. Requirement-side Self, generic arguments and associated Types are substituted, while the implementation's declaring Self and permitted receiver correspondence are kept. Function requirement compatibility applies, without implicit conversions or stronger implementation preconditions, and access, Origins and Loans, and generic premises are checked. No blanket equality between the storage Type and the requirement header is imposed.

Compatible custom and computed accessors implement calls directly. Calls through base projections or object borrows require published ObjectCallCompatible Proven (§12.4.4). A concrete Core object view does not require runtime Contract conformance. Unknown generic capabilities cannot establish compatibility.

### 11.4.2. Standard operation witnesses

**Witness adaptation** may synthesize a requirement operation from an exposed standard Place operation. For a storage Type `F`, only these bridges are supplied:

| Required operation | Standard implementation |
| --- | --- |
| Shared receiver -> `F` | Copy, if `F` is proven Copy |
| Shared receiver -> `ref/F` | Authorized shared slot borrow |
| Exclusive receiver, input `F` -> Unit | Accessible `var` standard `set` |

Every bridge is checked against the receiver, Property permissions, Origins, Loans and premises. Storage borrows cannot outlive receiver or slot validity, and copied references keep their original dependencies. Hidden storage behind a custom `get` is unavailable. Other conversions, projections and reborrows need compatible explicit accessors and are never synthesized.

```text
Requirement + selected Property + Type/Origin substitutions + base path
    -> verified operation witness
    -> call or direct lowering preserving the requirement contract
```

This mapping and the witness operation's identity and public guarantee (§12.4.4) are retained without adding concrete accessors or lookup candidates. It is the limited implementation-generation exception of §8.4.4; no runtime witness-table representation is mandated. An unconstrained `F` slot may implement a shared `ref/F` get but not an unconditional by-value `F` get.

Contract calls have function boundaries, not caller-visible storage disjointness. Only the required operations are available; implementation storage grants no direct Move or exclusive borrow. Owned getter results keep the restrictions of §11.2.3 even when their witnesses lower to direct operations. Requirement Types, Origins, Loans and temporary permissions are preserved through lowering.
