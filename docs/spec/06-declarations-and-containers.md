# 6. Declarations and containers

[Specification index](../SPEC.md)

A declaration has a Name, a Signature that distinguishes it in its scope ([§9.1](09-names-signatures-and-access.md#91-signatures)), and a Type. Fields and computed members are defined in [§11.1](11-properties.md#111-property-kinds).

## 6.1. Declaration containers

A **Declaration Container** is a named scope whose kind decides its permitted members.

| Kind | Instances | Contents |
| --- | --- | --- |
| `group` | No | Fields, computed members, functions, nested Containers; all members static; no own parameters |
| `struct` | Yes | Nested groups, structs, enums and Contracts, Fields, computed members, functions, conformances, associated-Type specifications, constructors, at most one selected `drop`; generic parameters, Origins, Constraints |
| `enum` | Yes | A closed sum Type ([§6.5](#65-enums)) |
| `contract` | No | Requirements, associated Types, Constraints, parents; own Type parameters but no Origin or length parameters, implementations or storage ([§8.4](08-generics-constraints-and-contracts.md#84-contracts)) |

There is no `extension` declaration; lookup has no extension candidates.

**Static members.** `static` is not a declaration modifier. Group and rootgroup members are static; a struct function without a receiver is a Type function; struct Fields and computed members are instance members. The Origin `static` is unrelated.

```kimi
group Limits
    let max: i32 = 100                   // Static: a group member.
    // static func one() -> i32 => 1     // Error: `static` is not a modifier.
```

### 6.1.1. Source root and nested containers

Each SourceDocument contributes its Container declarations (including rootgroup paths) to the project root. A SourceDocument's **execution scope** holds its top-level `let`/`var` bindings, its root function declarations and its executable items; other documents never see it. Root function declarations, including `public main`, lie inside it and find each other by unqualified lookup ([§9.5](09-names-signatures-and-access.md#95-unqualified-lookup)); as named functions they cannot capture top-level bindings ([§9.3](09-names-signatures-and-access.md#93-local-scopes-and-contextual-names)). Container bodies lie outside it: lookup from a Container never finds document-local bindings or functions. A root function declaration other than `public main` is document-local and takes no access modifier; writing one is an error. Startup is defined in [§22.4](22-core-execution-and-foreign-functions.md#224-program-startup-and-static-storage).

```kimi
func twice(x: i32) -> i32 => x * 2

public func main()
    let n = twice(2)                  // OK: main is inside the execution scope.

group G
    func f() -> i32 => twice(1)       // Error: a Container body does not see document-local functions.

// public func helper() -> i32 => 0  // Error: only `main` may be a public root function.
```

Root functions admit explicit full specialization in the same SourceDocument execution scope ([§8.13](08-generics-constraints-and-contracts.md#813-explicit-full-function-specialization)); another document cannot specialize them.

**rootgroup.** `rootgroup A.B` declares the nested group path `A.B` and is allowed only directly at the source root (including selected root directive items). Its path creates groups only; a segment naming an existing non-group Type is a kind conflict ([§9.1](09-names-signatures-and-access.md#91-signatures)). A group the path creates has the access of an explicit group declaration with that path if one exists, and `private` otherwise.

**Nesting.** The root, groups and structs may contain groups, structs, enums and Contracts; enums, Contracts, executable Blocks and conditional-conformance Blocks contain none. Depth has no language limit; an implementation limit is a Resource diagnostic. Membership adds no storage, outer instance, receiver, inheritance or conformance.

**Empty bodies.** A group, rootgroup, struct or contract may omit its body; blank and comment-only lines do not form one, and a body may become empty after directive selection. A bodyless contract is a marker Contract. An enum body is never empty, also after selection; an executable Block needs a source item before selection ([§14.2.1](14-control-flow.md#1421-body-forms)).

### 6.1.2. Container fragments

Group and struct declarations may be split into fragments, even within one file. Fragments with the same originating Kotonoha, parent, Name, kind and generic arity form one declaration (kind conflicts: [§9.1](09-names-signatures-and-access.md#91-signatures)); they never merge across Kotonoha libraries.

Fragments must agree on generic parameter count, kinds, order and names, on modifiers (`open` included) and on access after defaults; conflicting access is an error. Either every struct fragment writes the same Origin header or none does, which declares no own slots ([§15.3.2](15-ownership-and-lifetime-analysis.md#1532-type-schemas-and-storage)). Members generated into an existing Container use the header of the Type they join.

At most one fragment supplies the shared Constraint region (Type Constraints and `origin` relations), even if the clauses would be identical, and at most one supplies the base clause; each clause resolves in its own SourceDocument. A binding-set name on a base clause (`: Base<T>{b}`) is visible only in the fragment that writes it; an `origin` relation that uses it must be in that fragment's Constraint region, so that fragment also supplies the shared Constraint region.

After directive selection and merging, duplicate Field and computed-member Names, duplicate function or constructor Signatures and namespace conflicts are errors, and a struct has at most one selected `drop`. No primary fragment is required. C layout keeps storage in one fragment ([§6.7.1](#671-layout)).

Enums and Contracts cannot be split: two with the same identity (Kotonoha, parent, Name, kind, own Type-parameter count) are errors, even with identical contents. Contract identity identifies declarations; conformances are identified by bound references ([§8.6](08-generics-constraints-and-contracts.md#86-conformance)).

```kimi
// A.kimi
struct Box<T>
    T is Comparable
    var count: i32 = 0

// B.kimi
struct Box<T>
    func countValue(self: ref/Self) -> i32 => self.count
// Repeating `T is Comparable` or renaming T to U in B.kimi is an error.
```

### 6.1.3. Inherited environments and declaration references

Every nested Container, including a group or Contract, inherits the enclosing generic environment, but no runtime variables or receivers. An inner declaration is generic whenever an enclosing parameter exists, even an unused one.

A **declaration reference** is a declaration identity (originating Kotonoha, merged parent, Name, kind, own arity) plus the normalized Type, Semantics, length and Origin arguments of the declaration and its enclosing declarations, kept even when unused. Proof and lookup context are not part of it. A parameter slot (Type, Semantics, length or Origin) is identified by its binder and ordinal, never by spelling.

```kimi
struct Outer<T>
    public struct Inner<U>
        var first: T
        var second: U
// Outer<i32>.Inner<string> and Outer<i64>.Inner<string> are distinct Types.
```

### 6.1.4. Constraint roles

Input conditions supplied by the user of a declaration are assumptions while its definition is checked. Declaration obligations, such as an outer struct's conformance of Self and closed conditions, need their own proof. An inner declaration may use outer input conditions and proven evidence, but cannot turn an obligation into an assumption or read the outer Self as its own; its own input conditions do not propagate outward.

In a Contract, a condition only on inherited arguments is a reference input condition, one on the conforming Self is an implementation requirement, and a closed one is a declaration obligation. In a Type declaration, `Self is not ObjectPayload` ([§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload)) is an opt-out, neither condition nor obligation. Function Constraint subjects follow [§7.5](07-functions-and-callable-values.md#75-function-constraints). A struct may implement a Contract nested in it. A conformance is never evidence for its own identification and compatibility ([§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof), [§8.11.4](08-generics-constraints-and-contracts.md#8114-checking-and-use)). Obligations must hold for every admitted binding (Finality, [§8.3](08-generics-constraints-and-contracts.md#83-constraint-proof)).

### 6.1.5. Self

`Self` denotes the innermost struct or enum with all its bindings, or the conforming Type in a Contract. A group creates no Self context: inside it `Self` names the nearest outer Self Type. Group members have no receiver, but an explicitly typed parameter named `self` is valid ([§7.3](07-functions-and-callable-values.md#73-receivers)). A group body cannot directly contain a Constraint Clause, conformance or associated-Type specification.

```kimi
struct Parser
    group Helpers
        func identity(value: Self) -> Self => value@move       // Self is Parser.
        struct State
            func identity(value: Self) -> Self => value@move   // Self is State.
```

## 6.2. Structure declarations

A `struct` declares a named composite Core. Its Fields have complete Types, including Semantics and Origins; only Fields contribute storage.

**Finite inline storage.** A struct or enum declaration is invalid if its inline storage (Fields, the direct base, Case payloads, Tuple and fixed-array elements, including zero-length arrays, and Type arguments that a declaration stores inline) reaches the same declaration again, with any Type arguments, without passing through a borrow, raw pointer or object handle. The check is made at the declaration, whether or not the Type is used or instantiated, and is a Language error, never a Resource diagnostic. Only growth of distinct instantiations through an indirection (for example polymorphic recursion) can reach a generation limit.

```kimi
struct Shared
    var next: Option<rc/Shared>         // OK: an object handle is an indirection.

struct Node
    var next: Option<Node>              // Error: Node contains itself inline.

struct Grow<T>
    var more: Option<Grow<(T, T)>>      // Error: inline recursion, whatever the arguments.
```

### 6.2.1. Logical declaration order

Declarations from different SourceDocuments are ordered by the documents' logical names ([§20.1](../impl/20-compilation-configuration.md#201-projects-and-sources)), compared as UTF-8 byte sequences over the whole name (`/` is an ordinary byte), and then by position within each document. Generated fragments follow all ordinary ones in an implementation-defined deterministic order. Field initializers run in this order and Fields are destroyed in reverse order ([§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)), independently of layout, so renaming a source can change the order of initializer side effects. A package's source list carries no order.

```kimi
// a.kimi
struct Config
    var first: i32 = Log.trace(1)
// b.kimi
struct Config
    var second: i32 = Log.trace(2)
// Initialization calls Log.trace(1), then Log.trace(2) (Log: an application group).
```

Layout is implementation-defined unless `#Layout("C")` applies ([§6.7.1](#671-layout)).

### 6.2.2. Inheritance and open structures

A structure is **sealed** unless `open` precedes `struct`; a sealed structure cannot be a base. Openness is independent of access and is not inherited; an ObjectPayload opt-out ([§8.7.3](08-generics-constraints-and-contracts.md#873-objectpayload)) is inherited.

A structure names at most one direct base with `: BaseType` after its Name, generic parameters and Origin list. The base must be an accessible `open struct` Core; a generic parameter, a Semantics-applied Type, a group, an enum or a contract is not a base. Inheritance cycles, including through different constructions of one generic declaration, are errors. The base's Constraints must hold, and its access must cover the derived Type's effective access domain ([§9.4.4](09-names-signatures-and-access.md#944-api-signature-accessibility)).

```kimi
public open struct Base
    protected var count: i32 = 0

public struct Leaf : Base
    public func read(self: ref/Self) -> i32 => self.count

public struct Invalid : Leaf     // Error: Leaf is sealed.
```

Inheritance grants no private access. Nonvirtual calls use the implementation selected statically from the receiver's Effective Type, also through object views; inherited lookup ([§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)) and receiver projection ([§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection)) apply.

**Inherited Names.** Except for a valid override ([§6.4](#64-virtual-members-and-overrides)), a derived struct cannot declare a Value-role member (function of any Signature, Field or Property) with the Name of an ancestor member that is accessible from the derived declaration; conditional premises and overloading give no exemption. For a Property its own access is checked; protected access is checked with the derived receiver. Inaccessible ancestor Names may be reused. An explicit specialization is not another member.

**Base subobject.** The direct base is one inline owned subobject that logically precedes the directly declared Fields. Construction runs the base first ([§6.3.3](#633-construction-order)); destruction runs the derived layer first ([§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)). A base subobject cannot be Moved, replaced or reconstructed through a base view, and whole-value operations keep the exact Type: a value is never sliced. Access to an inherited Field obeys the Partial Move restrictions of every containing layer. Object upcasts are defined in [§13.6.1](13-operators-and-assignment.md#1361-object-upcasts).

## 6.3. Constructors

### 6.3.1. Declaration

A constructor is an optional access specification, `init`, a parameter list, an optional `: base(arguments)` clause and a Body ([§14.2](14-control-flow.md#142-bodies-and-evaluation-contexts)). It has no Name, explicit receiver, task slot, own generic parameters or result annotation; modifiers follow [§F.3](appendices/F-syntax-summary.md#f3-keywords). It uses the structure's Type parameters, Origins and Constraints. Borrow annotations may introduce per-call Origins ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)), input omission follows [§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision), parameter labels, defaults and Type checking follow ordinary function parameters, and Origin relations precede the executable items. Access defaults to `private`. Only the structure's own fragments declare its constructors.

```kimi
public open struct Named
    public let name: string
    protected init(name: string)
        self.name = name@move           // Transfer the Non-Copy parameter.

public struct Entry : Named
    public let number: i32
    public init(name: string, number: i32) : base(name@move)
        self.number = number            // Copy.

let entry = Entry.init("item", 1)
```

### 6.3.2. Construction expressions

A qualified Type-shaped path followed by `.init(` is always a construction expression; the qualifier may begin with `::` and contain Type arguments. A qualifier that resolves only to a value is an error.

`Type.init(arguments)` constructs a fresh owner of exactly the resolved Type. The structure and its outer environment are resolved first ([§9.7](09-names-signatures-and-access.md#97-type-name-selection)); then one of its own accessible constructors is selected ([§10](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference)). Its own Type-argument list may be omitted ([§10.8.1](10-overload-resolution-and-inference.md#1081-constructor-type-inference)); the result is as if the inferred arguments were written. Arguments and the expected Type never select another declaration or infer an outer Container.

```kimi
struct Box<T>
    public let value: T
    public init(value: T) => self.value = value@move

let number: i32 = 42
let box = Box.init(number)               // Box<i32>.
let wide: Box<i64> = Box.init(42)        // The literal fits i64.
// let wrong: Box<i64> = Box.init(number) // Error: the input binds T to i32.
```

Constructors are not inherited, there is no field-wise or zero-fill fallback, and a bare Type parameter cannot be constructed. `Type.init` is not a function value, and `value.init(...)` is invalid. Object handles are created separately ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)).

### 6.3.3. Construction order

After the arguments are evaluated, Uninitialized storage is allocated. The base arguments are evaluated next and may not access `self`; then the base constructor runs in the base subobject. Omitting the base clause selects a base constructor callable with no arguments; `: base(...)` without a base is an error. Exactly one base call occurs; same-Type delegation does not exist. Protected base constructors are accessible here ([§9.4.3](09-names-signatures-and-access.md#943-protected-receivers)).

Then this layer's Field initializers run in logical declaration order ([§6.2.1](#621-logical-declaration-order)), and then the body. Field initializers cannot reference parameters or `self`. No Field is initialized implicitly. The Origins of stored arguments must be expressible by the Type's declared Origin contract ([§15.3](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations)).

### 6.3.4. The construction receiver

For access and Loan checks, constructor `self` is an exclusively writable root for its initialized own Fields, as a `uniq/Self` receiver is. These Fields may be read, Copied, borrowed shared or exclusively (including the implicit acquisition of a Receiver Expression, [§7.3](07-functions-and-callable-values.md#73-receivers)) and Replaced, subject to the Property permissions of [§11.2](11-properties.md#112-standard-access) (an initialized `let` Field is read-only). No Non-Copy value may be Moved out of them. First placement into an Uninitialized Field follows [§11.7](11-properties.md#117-construction-and-destruction) and [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition); a custom `set` is never called.

Construction forbids computed access, inherited Field access, acquiring or borrowing the whole `self`, method and custom-accessor calls on `self`, and capturing or exposing `self`. Field borrows end before completion. These privileges exist only in the constructor body itself, not in nested functions, methods or Field initializers.

```kimi
struct Tally
    var count: i32
    var total: i32
    init(start: i32)
        self.count = start                                // First placement.
        self.total = 0
        self.count = self.count + 1                       // Replacement of an initialized var Field.
        Kimi.Intrinsics.replace(self.total@uniq, with: 5) // Exclusive borrow of an initialized Field.
```

### 6.3.5. Completion and failure

A constructor body returns Unit. Every reachable successful exit must have a completed base and Initialized own Fields; Scope Exit then runs (a `defer` cannot supply missing initialization). Constructors cannot fail: fallible or asynchronous construction uses a factory function ([§24.2.1](24-suspension-and-asynchronous-tasks.md#2421-form)). No ordinary exit leaves a constructor with an incomplete layer: an incomplete `return` is an error, and only Abort, which performs no cleanup, can interrupt construction.

### 6.3.6. Implicit constructor

After merging, one zero-parameter constructor is synthesized exactly when no constructor is selected (even a private one), every own Field has an initializer, and, with a base, the omitted base clause selects a base constructor. No memberwise constructor exists.

The base selection is ordinary selection with no arguments, made from the derived declaration ([§9.4.3](09-names-signatures-and-access.md#943-protected-receivers)) under its Constraints. If no candidate applies or the selection is ambiguous, there is no implicit constructor and the Type stays valid. If a candidate premise is Unknown, there is no implicit constructor and constructing the Type is an error. Synthesis does not depend on the validity of initializers. The synthesized constructor has the Type's effective access and behaves as `init() => ()`. The Kimi Types listed in [§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) have only their declared constructors.

```kimi
public open struct Counter
    protected var count: i32 = 0
    protected init() => ()

public struct Tally : Counter        // Synthesized public init() calls the protected base constructor.

public open struct Choice
    public init(first: i32 = 1) => ()
    public init(second: bool = true) => ()

public struct Picked : Choice        // Valid; the base selection is ambiguous, so no constructor.
// let picked = Picked.init()       // Error: Picked has no constructor.
```

## 6.4. Virtual members and overrides

Ordinary members are nonvirtual. A virtual slot is selected statically; the object's Dynamic Type selects only its implementation.

### 6.4.1. Declarations

`virtual func` declares a virtual slot and its original implementation; `override func` supplies an implementation of an inherited slot. Modifier recognition and order follow [§F.3](appendices/F-syntax-summary.md#f3-keywords); the two cannot be combined. Both must be safe instance functions with a body and an `objref/Self` or `objuniq/Self` receiver (bare `self` is `ref/Self` and ineligible), without own Type, Semantics or length parameters. An original is a non-private member of an `open struct`; an override is a member of a derived struct. Type and group functions, requirements, Properties, accessors, `init` and `drop` are never virtual.

The original alone owns the public contract; an override inherits it as an explicit specialization does ([§8.13.2](08-generics-constraints-and-contracts.md#8132-inherited-contract)), mapping only the receiver Self to the derived Self:

| Override header | Rule |
| --- | --- |
| Inputs, receiver mode | Same count, order and normalized Types; same `objref` or `objuniq` |
| Task slot | As the original ([§24.2.1](24-suspension-and-asynchronous-tasks.md#2421-form)) |
| Result | Same kind and normalized Type; no covariance |
| Parameter names | Same external names; internal names may change |
| Origins | Inherited; written annotations must state the same contract |
| Access, `!`, defaults, Constraints, effects | Inherited with the original's access domain (`protected internal` is not reinterpreted relative to the derived Kotonoha); never redeclared |
| Attributes, other modifiers | None |

Body lookup and private access use the derived declaration's context. A non-receiver `Self` keeps the Type it had where written: if the original's `other: objref/Self` means `objref/Base`, the override writes `objref/Base`. An override in an open struct can be overridden again.

```kimi
open struct Base
    public virtual func score(self: objref/Self, bonus: i32 = 1) -> i32
        effect confined
        return bonus

struct Derived : Base
    override func score(self: objref/Self, bonus: i32) -> i32
        return base.score(bonus) + 10

let d = Derived.init()@obj
let a = d.score()                     // 11; the original supplies the default.
let b = Base.score(d@objref/Base)     // 11; Type qualification still dispatches.
```

### 6.4.2. Override target selection

A **virtual slot** is identified by its original declaration and the normalized binding of its declaring Type and base path. An override selects its target as follows:

1. Starting at the direct base, take the first accessible same-named function group of inherited lookup ([§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)). Only virtual slots are targets.
2. Bind the override's input Types in its lexical environment, extended by the common premises: the substituted declared premises shared by every accessible virtual in the group, compared as normalized propositions; a substituted premise that is Refuted supplies no evidence. Compare task presence, receiver mode and inputs by normalized Type structure, as for explicit specialization targets ([§8.13.1](08-generics-constraints-and-contracts.md#8131-declaration-and-target)), not by Constraint proof. An input Type that cannot be resolved there is a declaration error.
3. Exactly one slot must match. The override then inherits its full premises and is checked against its contract.

No target, several targets and a contract mismatch are declaration errors; results, labels, defaults and declaration order never disambiguate, and farther ancestors are not tried. A derived Type has at most one override per slot, even in disjoint conditional-conformance Blocks.

An override adds no lookup layer, overload or public function. `Derived.f` and `Base.f` are the same Function Item with the original public receiver, so `Derived.f(baseObject)` is valid when its access ([§9.4.3](09-names-signatures-and-access.md#943-protected-receivers)) and receiver Type fit.

```kimi
open struct Base<T>
    public virtual func f(self: objref/Self, x: ref/T) -> i32 => 0
    public virtual func f(self: objref/Self, x: ref/i32) -> i32 => 1
struct D : Base<i32>
    // override func f(self: objref/Self, x: ref/i32) -> i32 => 2
    // Error: matches two slots.
```

### 6.4.3. Calls and conditional slots

Static selection fixes the slot. At run time the Dynamic Type selects the most-derived implementation on the inheritance path, or the original, with no name, overload, Constraint or access search. Member calls, Type-qualified calls, Function Items and Function values all dispatch; unbound calls add no implicit upcast.

An original may have a conditional-member premise `P` on enclosing parameters; calls and Function Item acquisition require `P`. An override is checked under the derived Type's Constraints with `P` substituted through the base path as an assumption. If the substituted `P` is Refuted or Error, the override is a declaration error: it implements a slot that never exists for the derived Type. If it is Unknown, it is an ordinary assumption. An override inside a conditional-conformance Block requires that Block's conditions to follow from these premises; it cannot add an implementation-selection condition, and an inapplicable `P` never selects a fallback or runtime test.

```kimi
open struct Base<T>
    public virtual func same(self: objref/Self, x: ref/T) -> bool
        T is Equatable
        return false
struct Plain
struct Derived : Base<Plain>
    // override func same(self: objref/Self, x: ref/Plain) -> bool => true
    // Error: Plain is Equatable is Refuted, so the slot does not exist here.
```

### 6.4.4. Direct base calls

`base.f(arguments)` uses lexical `self`, looks `f` up from the lexical declaring struct's direct base, and calls that base's implementation directly (in `A → B → C`, C's call runs B's own or inherited implementation). It is available in derived instance bodies for accessible base instance functions; calls inside the called body still dispatch. A direct base call uses the statically selected public contract and defaults, and may use the selected body's effect summary. An anonymous function needs an explicit `self` capture. Bare `base`, `base.f` as a value, `base.x` for a Field and `base.base` are invalid.

### 6.4.5. Receiver safety

A virtual receiver borrow covers the whole payload, including derived Fields. Every object base-view operation needs the Owned erasure proof of [§13.6.1](13-operators-and-assignment.md#1361-object-upcasts). In an override body, the original slot's Owned erasure evidence covers every object base-view operation whose source is the lexical `self` object: receiver projections for virtual, nonvirtual and direct base (`base.f(...)`) calls, and explicit upcasts of `self`. Base-view operations on any other object need their own proof. An original virtual body gets no such evidence. The object being destroyed cannot be dispatched on ([§16.4](16-scope-exit-and-destruction.md#164-closure-environments-and-objects-under-destruction)).

Every original and override must be ObjectCallCompatible Proven ([§12.7](12-expressions.md#127-object-receiver-compatibility)); the slot guarantees that receiver operations preserve completeness, Dynamic Type and Origin bindings. Replacing a base view or base subobject is forbidden; a proven complete-payload update, such as a sealed implementation's `self@follow` replacement, is allowed. Each implementation is checked separately, and a violation is an error even if unused.

```kimi
open struct Base
    public virtual func g(self: objref/Self) -> i32 => 0
    public virtual func f(self: objref/Self) -> i32 => 0
struct Derived<U> : Base
    var item: U
    override func f(self: objref/Self) -> i32
        return self.g()   // OK: projection of self is covered by the slot's evidence.
```

### 6.4.6. Effects and Contract witnesses

Slot effect bounds follow [§8.11.1](08-generics-constraints-and-contracts.md#8111-declarations). A dynamic call uses only the slot's public contract and bounds, even for a sealed target: its effects follow rules 1 to 3 of [§8.11.4](08-generics-constraints-and-contracts.md#8114-checking-and-use), and results use only the public Origin and Loan contract. An original or override whose result may carry a mutable-static anchor is an error ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)).

On a slot, `preserves results` ([§8.11.3](08-generics-constraints-and-contracts.md#8113-preserves-results)) relates calls on the same object (by dependency tracking, not Origin equality) through the same slot, or for direct base calls the same implementation; replacing or Moving the payload contents ends the relation. Handle Moves, Reborrows and lawful View changes preserve the relation; a guaranteed call's conservative input effects alone do not end it.

A virtual implementation of a Contract requirement maps to the slot: the slot's public contract and bounds must satisfy the requirement, never a body's stronger behavior.

## 6.5. Enums

An enum is a nominal, closed sum Core. A value holds exactly one **Case** and that Case's **payload**, its positional data.

### 6.5.1. Cases and payloads

Each Case is written on its own line; Case names are unique. A payload element is one complete Type, with no name, default or variadic form. `Quit` has no payload and `Quit()` is invalid; `Wrapped(())` has a Unit payload.

The header takes generic parameters and an optional Origin header ([§15.3.2](15-ownership-and-lifetime-analysis.md#1532-type-schemas-and-storage)). The body may contain Cases, Constraint Clauses, associated-Type specifications, functions, conditional conformances ([§8.8](08-generics-constraints-and-contracts.md#88-conditional-conformance)) and directives; no Fields, computed members, `init`, `drop`, nested Containers or inheritance. At least one Case must remain after selection. Every Case has the enum's access domain and takes no modifier; payload Types must be accessible there ([§9.4.4](09-names-signatures-and-access.md#944-api-signature-accessibility)). A Case colliding with another Value declaration is an error.

Payload elements are anonymous storage checked like Fields; their dependencies bind to the enum's Origin arguments, variance and Loan requirements come from all Cases ([§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins)), and selecting a Case never weakens the enum's Origin contract. A `uniq/T` payload reached through a shared path is bound as `ref/(uniq/T)`, and a fixed expected `ref/T` Reborrows it through both layers ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation)). Storage must be finite ([§6.2](#62-structure-declarations)); no heap allocation is implied. A Case change is a whole-value assignment; there is no `value.0` extraction. Equality and ordering are never derived ([§13.4.1](13-operators-and-assignment.md#1341-contract-comparison-mapping)); discriminants, tag size and ABI are unspecified.

**Objects.** An enum object is created from an owned enum value by `@obj`, `@rc` or `@arc` ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)). Its payload is matched by following the handle with `@follow` ([§13.5.5](13-operators-and-assignment.md#1355-follow)) and matching the resulting value Place. `.Case` at an object expected Type is an error ([§6.5.2](#652-case-references)); no operation creates an object implicitly. A Case Pattern never selects through an object handle ([§14.8.1](14-control-flow.md#1481-patterns)). Payloads may contain Object Semantics.

```kimi
let h = Option<i32>.Some(1)@obj
let n = match h@follow           // Sealed payload: matches the Option value Place.
    .Some(let x) => x            // x: ref/i32; value-read.
    .None => 0
// let bad: obj/Option<i32> = .Some(1)   // Error: `.Some` needs an owned expected enum Type.
```

### 6.5.2. Case references

A Case is referenced only as `Type.Case` or `.Case`; an unqualified Name never selects a Case, even through an opening alias ([§9.5](09-names-signatures-and-access.md#95-unqualified-lookup)). A qualified form is an ordinary member access or call; when lookup ([§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)) selects a Case, it is a Case construction, with no retry as another call. `.Case` ([§F.9.1](appendices/F-syntax-summary.md#f91-productions)) resolves only in the known expected enum: the owned expected Type, or the Type at a Pattern position ([§14.8.1](14-control-flow.md#1481-patterns)). A payload Case takes all positional arguments, evaluated left to right; a payload-free Case takes no parentheses. Cases are not function values.

The enum's own Type arguments are inferred as follows:

1. Qualifier: an omitted own Type-argument list selects the enum by [§9.7](09-names-signatures-and-access.md#97-type-name-selection).
2. Construction: when the expected Type is an owned Type of the same enum, its fixed arguments are supplied first; the payload arguments are then adapted to the resulting payload Types ([§10.2](10-overload-resolution-and-inference.md#102-common-adaptation), including Function Type erasure [§7.7.4](07-functions-and-callable-values.md#774-function-references-and-erasure)); remaining own slots are inferred from the payload arguments ([§10.8](10-overload-resolution-and-inference.md#108-generic-argument-inference)); a slot still unbound after literal defaults ([§10.2.1](10-overload-resolution-and-inference.md#1021-literals)) is an error. The leading-dot form needs an expected Type that fixes the enum. Explicit arguments that differ from the expected Type's are an error.
3. Pattern: a qualified Case Pattern may omit the enum's own arguments; they come from the Type at the Pattern position, and explicit arguments must equal them.

Outer Container arguments are never inferred ([§9.7.1](09-names-signatures-and-access.md#971-bound-container-paths)). The qualifier writes no Semantics or Origin arguments of the enum and no binding-set suffix (`View<T>{v}.Some(...)` is not a Case reference); complete Types inside its Type arguments keep theirs. Origin arguments are inferred from the expected Type and the payloads, never invented; if these do not determine them, annotate the expected Type.

```kimi
enum Message
    Quit
    Write(string)
    func make() -> Message => .Quit
    func wrap(s: string) -> Message => Self.Write(s@move)
    // func bad() -> Message => Quit   // Error: a Case needs `Self.` or `.`.

let a = Option.Some(1)               // Option<i32>, from the payload.
let b: Option<i64> = Option.Some(1)  // The expected Type supplies T first.
// let c = Option.None               // Error: nothing determines T.
// let d: Option<i32> = .None()      // Error: a payload-free Case takes no parentheses.
let n = match a
    Option.Some(let x) => x          // T from the matched Type.
    .None => 0
```

## 6.6. Bindings

Fields and local bindings begin with `let` (immutable) or `var` (mutable). The Type annotation or the initializer may be omitted when inferable; an unannotated Field takes its initializer's Type ([§11.6](11-properties.md#116-property-types-and-origins)). A local's Type is fixed at its declaration; an explicit Type that omits an Origin needs an initializer ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)). A local is visible after its declaration, so in `let x = x` the initializer refers to an outer `x` ([§9.3](09-names-signatures-and-access.md#93-local-scopes-and-contextual-names)). Initialization follows [§15.1.1](15-ownership-and-lifetime-analysis.md#1511-initialization-state).

```kimi
let limit: i32 = 10
var later: i32                     // Fully specified: no initializer needed.
later = limit
// let r: ref/i32                  // Error: the omitted Origin needs an initializer.
```

## 6.7. Attributes

An Attribute is `#Name` with optional arguments ([§F.6.1](appendices/F-syntax-summary.md#f61-source-units-containers-and-attributes)). Attributes attach to the next declaration at the same indentation; comments and blank lines may intervene. One before an `#if` whose target is a single declaration shares its selection. An Attribute before a directive Block or a `#switch`, or one followed by an unrelated item or a dedent, is dangling; a dangling Attribute is an error.

Attributes are accepted on Container, function, Field and computed declarations and on parameters; not on explicit specializations or Contract requirements. In excluded syntax they are checked syntactically only ([§19.4](19-compile-time-directives.md#194-excluded-syntax)). The recognized Attributes are `#Layout`, `#LibraryImport` ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)) and `#Test`; any other selected Attribute is an error.

```kimi
#Test
func check() => ()
// #test   // Error: an Attribute Name starts with an uppercase (Lu) letter.
```

### 6.7.1. Layout

`#Layout("Kimigayo")` and `#Layout("C")` apply only to struct declarations and take exactly one unnamed, non-interpolated string literal, matched case-sensitively. Each fragment has at most one `#Layout`, even with an equal value; fragments of one struct must not specify different modes; without one the mode is Kimigayo. In C mode all selected instance Fields, including generated ones, must be in one fragment, in written order; the Attribute may be on any fragment. Kimigayo layout is implementation-defined; C layout follows the target's C ABI, and a target with no C layout reports `#Layout("C")` as Unsupported. C layout is unavailable for an open or derived struct, an empty struct, and a struct with a zero-sized Field (Unit, Never, a zero-length array or a zero-sized struct); a use that needs such a layout is a Language error ([§8.15.3](08-generics-constraints-and-contracts.md#8153-concrete-layout-and-representation)).

```kimi
#Layout("C")
struct NativeRecord
    var kind: u8
    var value: u64

#Layout("c") // Error: mode names are case-sensitive.
struct Invalid
    var value: i32
```

### 6.7.2. Test definitions

`#Test` takes no arguments and marks one test case. The target must be a safe, named function with a body, no parameters, no receiver, no task slot and a Unit result, at the root or in a Container. Neither the function nor an enclosing Container may have generic or Origin parameters. Foreign imports, specializations and local functions are ineligible. An ineligible target or a duplicate `#Test` is an error. Outside a test build, test bodies are not checked beyond syntax. User code may neither call a Test function nor take it as a value; only the test runner invokes tests ([§20.6.1](../impl/20-compilation-configuration.md#2061-selection-and-preparation)).

```kimi
group Arithmetic
    func add(left: i32, right: i32) -> i32 => left + right

    #Test
    func addition()
        $expect(add(1, 2) == 3)
```
