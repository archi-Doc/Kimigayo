# 11. Properties

[Specification index](../SPEC.md)

A concrete Property is `let`, `var`, or `computed`. A Contract uses `property` to require operations.

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

Concrete Properties belong in struct/group/rootgroup bodies: instance in a struct, static in a group/rootgroup. Enums do not permit them. Local `let`/`var` remain bindings; local computed declarations are forbidden. Properties share the Value namespace. Each accessor appears at most once, in either order. Attributes follow §6.5; inline `has` is only for requirements.

A **Stored Property** is a let/var declaration with one storage slot. Storage kind is determined by the declaration, never by accessor bodies. In storage, layout, and Move Path rules, **Field** means this slot, not a separate declaration kind. All source access to it obeys the Property's operation permissions.

| Type contract | Meaning |
| --- | --- |
| Stored `: T` | Storage Type; standard value acquisition also produces T |
| Stored custom get | Result exactly T; T must be proven Copy |
| Stored custom set | Input exactly T; result Unit; no Copy constraint |
| Computed `: T` | Getter result Type; setter input may differ |
| Contract `property p: T` | Required getter result Type; explicit setter input may differ |

Compare complete Type structure and bound Origin dependencies (§11.3), not merely the Core. Copy capability selects acquisition, never a conditional result Type such as `T` versus `ref/T`.

## 11.1. Standard access and acquisition

**Standard get is a Place access contract, not a getter function.** A bodyless get/set selects a standard operation. Omitted accessors are supplied according to the declaration table; customizing one does not change the other.

```kimi
public let limit: i32 = 100
public var count: i32 = 0
public var resource: Resource
    get
    private set
```

Standard get exposes permitted operations on storage. Value acquisition Copies T if Copy, otherwise Moves only from a Movable Place (§15.1.5). Borrowing follows existing adaptation rules without first acquiring the value. Standard set directly initializes or replaces storage under ordinary state and cleanup rules.

| Direct storage operation | Required accessible standard accessors |
| --- | --- |
| Copy or shared borrow | get |
| Move from let | get |
| Move from var | get and set |
| Exclusive borrow | get and set |
| Write | set |

All rows additionally require valid receiver capabilities, initialization/completeness, Origins, Loans, Move Paths, and construction/destruction conditions. A custom accessor cannot satisfy a requirement for a standard accessor. Simple assignment needs the final target's set, not its get; getters needed to locate that target are checked separately (§13.7).

| var configuration | Read/borrow target | Update |
| --- | --- | --- |
| Standard get + standard set | Storage, subject to the table | Direct set |
| Custom get + standard set | Getter result; no direct storage borrow/Move | Direct set |
| Standard get + custom set | Storage Copy/shared borrow; no direct Move/exclusive borrow | Call set |
| Custom get + custom set | Getter result; no direct storage access | Call set |

Shorthand borrows retain §13.5.5 semantics. For a slot of Type F = ref/U, `@ref` copies the stored shared reference; `@ref/F` borrows the slot as ref/ref/U. Specify the complete slot Type when requesting slot access. Failed Reborrow cannot retry as slot borrowing.

### 11.1.1. Access and mutability

Move is destructive access, not assignment. Moving a var requires accessible standard set but does not call it or add a receiver write-capability requirement. A let binding may be consumed; Move does not reset its first-initialization history. A moved let cannot be reinitialized or expose its slot for exclusive borrowing. Restoring var requires ordinary write permission.

```kimi
// Outside the declaring struct; holder is owned and complete.
// resource has public standard get and private standard set.
let r = holder.resource       // Error: set is inaccessible for var Move.
let view = holder.resource@ref // OK: shared storage borrow.
```

Slot immutability is not deep immutability. A stored reference or object handle may grant access to a separate referent under its own capabilities and Loans.

### 11.1.2. Move paths and inherited fields

Safe storage Move requires an owned struct Place, an eligible static Move Path, completed construction, an Initialized and complete target, and valid access, Origins, Loans, and ancestor deinit conditions. Borrowed/object receivers and static storage cannot supply safe extraction. Raw-pointer operations retain their Unsafe rules.

Inherited lookup selects a Field Identity and unique base path, substituting Type/Origin arguments. Following this path does not acquire intermediate bases or imply a ref/Derived-to-ref/Base conversion. Preserve the original receiver category; inheritance grants no private access. Track state and Loans by base path and Field Identity. A base subobject itself cannot independently be Moved, replaced, or reconstructed.

Every storage projection checks the permissions of enclosing Properties. Child writes, borrows, and consumes—including implicit method-receiver or operator adaptations—cannot bypass an ancestor's custom or inaccessible setter. References reaching separate referents follow their own capabilities. A path crossing a custom getter continues from its result under §11.2.3, never its hidden storage.

```kimi
// position: Point is Copy, with standard get and custom set.
object.position.x = 10       // Error: bypasses position's setter.
object.position.x.modify()   // Error if modify requires an exclusive receiver.
var next = object.position
next.x = 10
object.position = next      // OK: calls the setter.
```

Standard access may use a complete remaining Place after a sibling Move. A custom accessor/computed call needs a complete receiver and retains its declared function footprint; callers cannot infer disjointness from its body. Initial-construction restrictions still apply even when all slots are initialized (§11.3.1).

```kimi
// Both fields have standard accessors; partial Move/deinit conditions hold.
let item = object.resource
let count = object.count       // OK: complete remaining storage.
let shown = object.displayCount // Error if this getter borrows incomplete object.
object.resource = makeResource() // Restore through accessible standard set.
```

### 11.1.3. Generic acquisition

Standard accessors need no Copy constraint. A stored custom getter needs proof of T is Copy under the declaration's generic premises, not merely for a chosen instantiation. Custom setters need no Copy constraint but must be valid for every admitted T.

```kimi
struct Box<T>
    public var item: T

func view<T>(box: ref/Box<T>) -> ref/T from box
    return box.item@ref/T

func readCopy<T>(box: ref/Box<T>) -> T
    T is Copy
    return box.item

struct AssignedBox<T>
    public var item: T
        set(self: uniq/Self, value: T) -> ()
            storage = value
```

AssignedBox cannot supply unconstrained T by value through standard get: its non-Copy case would require forbidden storage Move. Shared borrowing remains available.

When Copy is unproven, verify the conditional Copy/Move state under §8.10. Runtime Copy values still Copy; the result Type remains T. Later use must be valid in both cases. Reinitialization or explicit borrowing may establish valid continued use. Do not retry overload selection after a Move/Loan failure.

```kimi
func test<T>(box: Box<T>) -> ()
    let x = box.item
    inspect(box@ref) // Error: box may be incomplete after non-Copy Move.
// Adding T is Copy makes this subsequent shared use valid.
```

## 11.2. Accessor functions

Custom and computed accessors declare input and result Types explicitly; the receiver may use the fixed shorthand below. Bodies infer none of these Types. Both accessors use the common Body (§14.2). A single-item getter follows its fixed declared return Type; a setter discards its single-item expression and completes with Unit. Explicit returns still fit the declared result. Default/optional parameters are forbidden. Static accessors have no receiver. A setter's value parameter is an initialized immutable binding with ordinary argument acquisition and cleanup.

In an instance Property, an accessor signature without a written receiver inserts `self: ref/Self` for `get`, or `self: uniq/Self` before `value` for `set`. This applies to stored custom accessors, computed accessors, and explicit Contract requirement signatures. Thus `get() -> T` and `set(value: U) -> ()` retain an instance receiver and bind contextual `self` in their bodies and Origin annotations. The containing Property determines instance/static kind; group/rootgroup accessors remain receiverless. An explicit receiver Type remains available and must satisfy the existing accessor restrictions. Parameter lists, setter input Types, and result Types are still required for custom/explicit requirement signatures; bare `get`/`set` retain their standard-accessor rules. No receiver Type is inferred from the body, and operations requiring a stronger receiver do not change this default. Origin completion, signature matching, and call/borrow behavior are identical to the expanded signature, with receiver input slot zero.

Stored instance custom get uses `self: ref/Self` and returns storage Type T, which must be Copy. Custom set uses `self: uniq/Self, value: T` and returns Unit. Static forms are `get() -> T` and `set(value: T) -> ()`.

```kimi
public var level: i32 = 0
    get() -> i32
        return storage
    set(value: i32) -> ()
        storage = clamp(value, 0, 100)
```

Inside a stored accessor, contextual `storage` denotes its own slot without recursive accessor invocation. It obeys receiver capabilities, let/var mutability, initialization, Origins, and Loans. It is unavailable in computed and requirements. A custom getter may Copy a stored reference of Type T; this differs from exposing a direct borrow of the slot, which requires standard get.

### 11.2.1. Accessor accessibility

Accessors inherit Property access unless explicitly restricted. A restriction must be strictly narrower in declared access; enclosing declarations cannot justify a repeated or broader modifier. Protected forms retain their structure-member requirements.

| Property access | Permitted explicit accessor restrictions |
| --- | --- |
| public | protected internal, protected, internal, private protected, private |
| protected internal | protected, internal, private protected, private |
| protected | private protected, private |
| internal | private protected, private |
| private protected | private |
| private | None |

Neither accessor must retain the Property's full access domain, and set access need not be contained in get access. API signature and protected-receiver checks still apply. Object/projection calls require published ObjectCallCompatible Proven (§12.4.4); writing ref/Self or uniq/Self alone supplies no proof.

### 11.2.2. Computed properties

Computed has no storage, initializer, bodyless standard accessor, or inline has list. Its explicit getter result must match the header Type after Origin completion. The optional setter returns Unit and may have a different input Type. Instance receivers follow explicit ordinary function contracts, including ownership-bearing receivers; static accessors omit self.

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

Non-Copy results must be legally created or acquired. A shared receiver cannot supply an owned non-Copy field by extraction. An owning getter may consume the complete receiver; a later setter cannot reuse it. No hidden duplication, restoration, or get/set round-trip equality is promised.

### 11.2.3. Getter results and temporaries

Stored custom get, computed get, and Contract get produce function results. Adaptations apply to that result, not backing storage.

**An owned getter-result Temporary Place and its inline descendants cannot be directly assigned, compound-updated, incremented/decremented, or exclusively borrowed.** Parentheses, projections, and implicit exclusive receiver adaptation preserve this restriction. Updating the Property itself through set is separate and remains allowed (§13.7).

```kimi
// position: Point is Copy, with custom get and standard set.
object.position.x = 10          // Error: updates only the getter temporary.
object.position.x += 1          // Error.
let edit = object.position@uniq/Point // Error.
var next = object.position
next.x = 10                     // OK: ordinary local storage.
object.position = next          // OK: calls position's set.
```

The restriction belongs to that Temporary Place, not unlimited value provenance. Value acquisition into a local or ordinary function argument uses the destination's normal rules. An ordinary function's owned result follows §3.6, even if an argument came from a getter. References still pointing into the original restricted temporary retain its restrictions. Separate referents reached through returned references/object handles follow their own capabilities, Origins, and Loans.

```kimi
// identity takes and returns Point by value.
identity(object.position).x = 10 // Ordinary function temporary rules.
// Neither example writes back to object.position.
```

Value acquisition, shared borrowing, and legal ownership transfer remain allowed. Materialization and borrowing never extend temporary lifetime. Required borrow duration is inferred from uses, returns, and retention; reject it if the referent cannot live that long.

```kimi
// visible has a custom getter returning i32.
inspect(object.visible@ref)    // OK: temporary lasts through this call.
let view = object.visible@ref
inspect(view)                 // Error: initializer temporary has ended.
let saved = object.visible
inspect(saved@ref)             // OK: borrow the local instead.
```

No blanket rejection of an unused borrow binding is added. For a standard i32 get, `@ref` instead borrows storage.

### 11.2.4. Non-Copy custom setters

A custom setter receives ownership of non-Copy value and may replace storage. Assignment to storage secures its RHS, destroys any old value, and installs the new one without calling the setter again. Ordinary assignment/cleanup failure rules apply. An exclusive receiver cannot directly Move out an old non-Copy value; inspect it by shared borrowing or use an authorized initialization-preserving operation. End conflicting Loans before replacement and return with a complete receiver.

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

A setter may return without updating storage. Destroy unconsumed input normally; do not double-destroy transferred input or automatically restore it to the caller. Use a result-returning function when acceptance/rejection must be reported. These restrictions do not prohibit legal whole-receiver Move or destruction. Initial placement does not invoke validation (§11.3.1).

## 11.3. Types and Origins

A stored Type may be inferred only from its declaration initializer; otherwise require an annotation. Do not infer it from accessors or later assignments. Storage explicitly binds required Origins, including nested dependencies (§15.4); self does not create a self-borrowing storage contract.

First complete each accessor signature by ordinary position-sensitive function elision. Then compare required Type structure and bound Origins, and verify its body. Stored custom input/result must match storage T including Origin correspondence, not just names. If elision cannot establish that match, require explicit Origins.

```kimi
struct View origin source
    public var value: ref/i32 from source
        get(self: ref/Self) -> ref/i32 from source
            return storage
        set(self: uniq/Self, value: ref/i32 from source) -> ()
            storage = value
```

Omitting from source above would give the getter's unbound direct result from self and the setter's direct input an independent input Origin; neither establishes the storage contract. Existing bound dependencies are never replaced by self.

Computed/required accessors use the same function elision but no shared storage-Type comparison. A getter whose only direct borrowed input is self may elide its result Origin to self. Setter input completion is independent. Create no Origins for absent accessors. Static getter/setter contracts use ordinary receiverless rules. Copy reference/aggregate Types still undergo all lifetime and Loan checks.

### 11.3.1. Construction and destruction

Declaration initializers and constructor first placement initialize own storage directly, without accessor calls. First placement requires that every incoming path is uninitialized and has never completed first placement. Move or destruction does not reset this history. Declaration initializers count as initialized. Do not dynamically choose initial placement versus custom set at a mixed-state join.

| Constructor write to own storage | Definitely before first placement | Already initialized or mixed paths |
| --- | --- | --- |
| let | Direct initialization | Error |
| var with standard set | Direct initialization | Normal state-dependent placement/replacement |
| var with custom set | Direct initialization | Error: custom set call during construction |

Construction self cannot call custom get/set or computed, even after all slots are initialized. Standard get may Copy or borrow initialized own storage; non-Copy Move, inherited storage access, and whole-self acquisition/borrowing remain forbidden. Construction borrows end before completion (§6.2.3).

```kimi
// In a constructor: level has custom set and no declaration initializer.
if condition
    self.level = 100 // First placement on this path; no setter.
else
    self.level = 200 // First placement on this path; no setter.
self.level = 300     // Error: construction cannot call custom set.
```

If only one branch initializes level, a subsequent unconditional write is also rejected for custom set. A declaration initializer such as level = 999 bypasses validation too; callers needing validated initial values must arrange it explicitly.

Preserve base-first construction, declaration-order initializers, no self/constructor-parameter use in declaration initializers, definite initialization, and completion checks after cleanup. No zero initialization is added. Layout, Copy derivation, implicit construction, partial Move, and destruction use stored slots/base components only. Computed contributes none; automatic destruction invokes no accessors.

### 11.3.2. Static storage

Group/rootgroup stored Properties require initializers, an Owned complete storage Type under §15.2.3, and §22.2's per-slot lazy state machine. Safe shared borrows from eligible immutable static storage may be retained, including inside aggregates. Check acquisition, initialization, and destruction dependencies separately; Owned supplies no Loan or pointer-validity evidence.

Safe code cannot form a `from static` borrow of mutable static storage, including an inline subplace or backing data that safe mutation can invalidate. A new borrow of such storage has a finite use-bounded Origin and cannot be fitted to static, including through generic substitution or an accessor result. A function or custom getter can expose it only through a result Origin bounded by a borrowed input under §15.4, retaining the Field anchor in its effect summary (§15.6.4); a result Origin elided to static is an error. Copying an already stored reference preserves its original referent and Origin; it is not a borrow of the mutable slot. An eligible static borrow must be anchored in initialized immutable storage whose referenced path remains protected from safe mutation. Local Loans and shutdown checks remain necessary under §15.6.4 and §22.2.3.

Actual slot access triggers initialization. Calling a custom/computed accessor initializes only slots actually touched by its execution or callees, not every summarized effect. A standard write initializes the slot first, then replaces its value; static let permits no external initialization or replacement. Custom set follows its function effects and may never touch storage. Unused initializers still require validation. Static accessors have no self.

## 11.4. Contract property requirements

A property requirement is instance-only and promises operations, not storage. The header Type T is its getter result. Get is mandatory and set optional, each at most once. Requirements have no accessor access modifiers, Attributes, default/optional parameters, initializer, storage, or body.

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

`has get` requires get(ref/Self) -> T **by value**, not merely some readable access. `has set` requires set(uniq/Self, value: T) -> Unit. The explicit form states signatures; its getter matches header T and its setter input may differ. Apply ordinary receiver and Origin contracts. A shared receiver cannot Move a non-Copy stored Resource to implement `property item: Resource has get`; a ref/Resource requirement may use shared storage borrowing.

### 11.4.1. Operation compatibility

Select a new implementation by ordinary member lookup; inherited conformance retains its mapping under §8.4.4. Check the selected let/var/computed operations without retrying another Name/base candidate because of Type, accessor, or accessibility failure. Substitute requirement-side Self, generic arguments, and associated Types while retaining the implementation's declaring Self and permitted receiver correspondence. Apply function requirement compatibility without implicit conversions or stronger implementation preconditions. Check access, Origins/Loans, and generic premises. No blanket equality between storage Type and requirement header is imposed.

Compatible custom/computed accessors implement calls directly. Calls through base projections or object borrows require published ObjectCallCompatible Proven (§12.4.4). A concrete Core Object View does not require runtime Contract conformance. Unknown generic capabilities cannot establish compatibility.

### 11.4.2. Standard operation witnesses

**Witness adaptation** may synthesize a requirement operation from an exposed standard Place operation. For storage Type F, only these bridges are supplied:

| Required operation | Standard implementation |
| --- | --- |
| Shared receiver -> F | Copy, if F is proven Copy |
| Shared receiver -> ref/F | Authorized shared slot borrow |
| Exclusive receiver, input F -> Unit | Accessible var standard set |

Check every bridge against receiver, Property permissions, Origins, Loans, and premises. Storage borrows cannot outlive receiver/slot validity; copied references retain original dependencies. Hidden storage behind custom get is unavailable. Other conversions/projections/reborrows need compatible explicit accessors; do not synthesize them.

```text
Requirement + selected Property + Type/Origin substitutions + base path
    -> verified operation witness
    -> call or direct lowering preserving the requirement contract
```

Retain this mapping and the witness operation Identity/public guarantee under §12.4.4 without adding concrete accessors or lookup candidates. It is the limited implementation-generation exception of §8.4.4; no runtime witness-table representation is mandated. An unconstrained F slot may implement shared ref/F get but not unconditional by-value F get.

Contract calls have function boundaries, not caller-visible storage disjointness. Only required operations are available; implementation storage grants no direct Move/exclusive borrow. Getter owned results retain §11.2.3 restrictions even when their witnesses lower to direct operations. Preserve requirement Types, Origins, Loans, and temporary permissions through lowering.
