# 6. Declarations and containers

[Specification index](../SPEC.md)

Kimigayo uses the following information to identify declarations and their meaning:

| Element     | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| `Name`      | The basic human-readable name used to refer to a declaration          |
| `Signature` | The information that distinguishes declarations in the same scope     |
| `Type`      | The meaning of a value or invocation within the type system           |

See [name resolution and access](09-names-signatures-and-access.md#9-names-signatures-and-access) and [overload selection](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference). Field and computed syntax is defined under [Properties](11-properties.md#11-properties).

## 6.1. Declaration containers

`static` is not a declaration modifier in this revision. Reject `static func`, `static let`, and `static var`, including redundant uses in groups. Group/rootgroup members are inherently static, a struct function without a receiver is a type function, and struct Fields and computed members are instance members. The contextual Origin spelling `from static` remains valid; it does not make a declaration static.

**Extension boundary:** This revision does not accept `extension` declarations or imports and supplies no extension candidates. References below to future extension access, identity, and precedence constrain that future design only; they are not active lookup stages or conformance mechanisms. Ordinary member lookup is complete without an extension pass.

A **Declaration Container** is a named declaration scope whose body may contain Fields, computed members, functions, associated-Type declarations/specifications, Constraint Clauses, or nested Declaration Containers as permitted by its kind. Its body is delimited by indentation.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Fields, computed members, functions, and nested Declaration Container declarations. All members are static. Generic parameters and Origins are not supported. |
| `struct` | Yes | Accepts Fields, computed members, functions, conditional conformance declarations, associated-Type specifications, constructors, and at most one selected `deinit`. Generic parameters, Origins, and Constraints are supported. Sealed by default; `open struct` permits derivation. |
| `enum` | Yes | Closed sum Type with Cases, positional payloads, functions, conditional conformance declarations, and Constraints; see [enums](#63-enums). No fragments or inheritance. |
| `extension` (future) | No | Not introduced; see this section’s extension boundary. |
| `contract` | No | Declares function/Property requirements, associated Types, and Constraints; supports multiple-parent refinement. No implementations, storage, generic parameters, or contract-level Origins. See [Contracts](08-generics-constraints-and-contracts.md#84-static-contracts). |

### 6.1.1. Root and nested containers

Each source unit contributes named declarations to the project root. Top-level bindings (`let` and `var`), local functions, and executable items retain a SourceDocument-local scope and are not exported to other files. The exception is an explicit root-level `public main`, an ordinary function in the shared root that retains its declaration-site lookup environment. It cannot capture top-level runtime locals. Other shared functions, Fields, and computed members belong in named Containers. [Program startup](22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization) selects either one document's runtime body or one eligible public main. A `rootgroup` declaration starts at the root and accepts a dot-separated Name. For example:

```kimi
rootgroup A.B
    var value = 1
```

creates the nested group path `A.B`. Ordinary `group` bodies accept nested Declaration Container declarations. In this revision, `struct` bodies do not accept nested Declaration Containers.

Aliases follow [source-local import rules](18-modules-and-dependencies.md#181-external-references-and-aliases). Synthesized intermediate groups in a `rootgroup` path use an explicit group declaration's accessibility when present, otherwise `private`; synthesis is not an independent header fragment. Public paths require explicitly accessible groups.

**Empty declaration bodies.** A group, rootgroup, struct, or contract may omit its indented body entirely. The header is complete when followed by EOF or the next effective item at the same or shallower indentation. Blank/comment-only lines, including indented comments, do not create a body and do not cause the next item to be absorbed. An indented declaration body may also become empty after directive selection. A bodyless contract is a marker Contract with no requirements; a bodyless struct is an empty structure, subject to its base and implicit-constructor rules. These permissions do not apply to enums (which require at least one selected Case), match-arm lists, or executable Blocks. A `()` expression is not an empty-Container marker, since ordinary executable items are not Container members.

### 6.1.2. Container fragments

Group and struct declarations may be split, even within one file. Collect same-parent, same-name fragments and identify a declaration by originating Kotonoha, parent Symbol, name, kind, and generic arity. Reject conflicting kinds such as a group and struct with the same name. Different arities, such as `Box` and `Box<T>`, are different Types. Never merge across Kotonoha libraries or treat an extension as a target declaration fragment; applied `ref`/`uniq` Semantics do not change Container identity.

Matching fragments must agree on generic parameter count/kinds/order/names, Origin count/order/names and declared bounds, declaration kind, semantic modifiers, and accessibility after defaults. Repeat Origin bounds in every fragment and compare their resolved binder identities; each occurrence resolves in its own source environment. Do not widen conflicting accessibility. Exactly one fragment may define the Container's Constraint Clauses, even if duplicate clauses would be identical; other fragments omit them and share that definition's Constraints. Resolve them in their definition-site source environment.

For structures, `open` must agree across all fragments. At most one fragment supplies the base clause; the other fragments share that base without repeating it. Resolve the base in that fragment's source environment, then validate the complete merged inheritance relationship.

After compile-time selection and merging, reject duplicate Field/computed Names, duplicate function Signatures, and namespace conflicts. Selected fragments may contribute Fields, including generated ones, under [split-structure storage order](#621-split-structures-and-storage-order); C layout requires a single storage-bearing fragment (§21.1.2). Do not require a primary fragment. Enums cannot be split: after selection and generation, multiple declarations with the same enum Identity are errors. Contracts likewise cannot be split: after directive selection and source generation, multiple declarations with the same Contract Identity (originating Kotonoha, parent Symbol, name, and contract kind) are declaration errors, even when their contents are identical. User Contracts are nongeneric; no arity distinguishes same-named declarations. Mutually excluded declarations are allowed only when at most one remains. Different Contracts may refine a shared parent, but refinement is not declaration merging.

Constructor Signatures must also be unique across the selected fragments. At most one selected `deinit` body may belong to a merged structure, including generated fragments. Do not concatenate destruction bodies or choose one by source order; duplicates are declaration errors. Mutually excluded bodies may coexist in source only when selection leaves at most one in each fixed target/configuration environment.

```kimi
// A.kimi
struct Box<T>
    T is Comparable
    var count: i32 = 0

// B.kimi
struct Box<T>
    func countValue(self: ref/Self) -> i32 => self.count
// Repeating the Constraints or renaming T to U in B.kimi is an error.
```

**Future design:** Contract fragments are not introduced; any future fragment facility needs explicit contents and merging rules. Extension identity/public names remain separately specified. Static initialization and startup selection are defined under [program startup](22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization).

## 6.2. Structure declarations

A `struct` declares a named composite Core. Its Fields and computed members have complete Types, including their Semantics and Origin dependencies:

```
struct Point
    var x: f64
    var y: f64

struct Node
    var value: i32
    var next: obj/Node

struct View origin source
    var data: ref/Data from source
```

Data is an assumed Core in the View example; the instance borrow's Origin is explicitly declared and bound.

Explicit and implicit constructors follow [construction](#623-constructors); all construction obeys [initialization](11-properties.md#113-types-and-origins) and [construction completeness](15-ownership-and-lifetime-analysis.md#1512-aggregate-construction-and-completeness).

### 6.2.1. Split structures and storage order

A `struct` may be split into [declaration fragments](#612-container-fragments), including fragments produced by a [Mod](20-compilation-configuration.md#207-mods-source-generation); identity, header agreement, member uniqueness, and the C-layout single storage-fragment restriction follow that section. Fields are identified by declaration kind; computed members add no storage.

Apply [logical declaration order](20-compilation-configuration.md#2074-generated-sources-and-declaration-order) to the environment-selected fragments of each instantiation. Only Fields contribute storage slots. This order determines initializer side-effect order independently of physical layout.

Mods may inspect provisional Types under [provisional Binding](20-compilation-configuration.md#2072-compilation-and-binding). Finalize layout only after all Mods complete and the full selected fragment set and storage classification are known. No later generation may append Storage to a finalized Type.

Physical layout and ABI guarantees follow [Structure layout and ABI](21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi).

### 6.2.2. Inheritance and open structures

A structure is **sealed** unless its declaration has `open` immediately before `struct`. A sealed structure cannot be a base Type. `open` permits derivation and is independent of accessibility: `public struct` remains sealed, while `internal open struct` permits derivation only where that Type is accessible. A derived structure is itself sealed unless explicitly declared `open`; openness is not inherited. No separate `sealed` modifier is needed for this default.

A structure may specify one direct base with `: BaseType`, after its Name and generic parameters and before its Origin list. The base must resolve to an accessible constructed or nongeneric `open struct` Core; a generic parameter, Semantics-applied Type, group, enum, or contract is not a base. Omission declares no user-defined base. Reject multiple bases and direct or indirect inheritance cycles, including cycles through different constructions of the same generic declaration. The base's Constraints must hold, and its accessibility must cover the derived Type's [effective access domain](09-names-signatures-and-access.md#932-api-signature-accessibility). Constraint Clauses continue to express capabilities separately from the base clause.

```kimi
public open struct Base
    protected var count: i32 = 0

public struct Leaf : Base
    public func read(self: ref/Self) -> i32 => self.count

public struct Invalid : Leaf // Error: Leaf is sealed.
```

Inheritance preserves members' declared accessibility and grants no private access. Ordinary functions and accessor calls use the implementation selected statically from the receiver's Effective Type, including through concrete Core Object Views. The Runtime Object Type does not replace that implementation. [Inherited lookup](09-names-signatures-and-access.md#95-qualified-and-inherited-lookup) determines the declaration layer; [receiver projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection) supplies eligible instance receivers. Function-value calls retain their own rules.

**Inherited Names.** A derived struct cannot declare a Value-role member with the same Name as a member of any ancestor that is accessible from the derived declaration. This covers functions regardless of Signature or instance/type-function kind, Fields, and stored/computed Properties. Conditional premises and overload applicability do not exempt a declared Name. Check a Property's own access, not an individual accessor's; use the derived receiver for protected access.

Check the completed declaration set after environment selection, Mod output, and fragment merging, with a valid base graph. Diagnose a collision at the derived declaration. Same-layer overloads and other namespaces retain their rules. Inaccessible ancestor Names, including private or cross-Kotonoha internal/private-protected members, may be reused. An explicit specialization is an implementation of its original function, not another member; it gains no right to specialize a base member from a derived Container.

Accessible Value-role Names of an open struct are part of its API to derived Types. Name additions or access expansion can invalidate downstream declarations under [dependency revalidation](21-layout-runtime-and-code-generation.md#2134-artifacts-verification-and-invalidation). Derived Types must tolerate mutations and preconditions exposed by base APIs; stronger application invariants cannot become unchecked memory-safety assumptions.

The direct base is one inline owned subobject, logically preceding the structure's directly declared fields. Base subobjects retain their own field identities and construction-completion facts. Physical offsets remain compiler-selected under [layout rules](21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi); logical ordering does not require flattening or a fixed ABI. Construction runs base first under [constructors](#623-constructors), and destruction runs the derived layer first under [field cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup). The base subobject cannot independently be Moved, replaced, or reconstructed through a base view of a derived value. Whole-value operations retain the exact owning Type and the responsibility for all layers; they cannot slice a derived value. Access to an inherited field still obeys normal permissions and checks all its containing base and derived ancestors for Partial Move restrictions.

Explicit ordinary base-member invocation and additional ordinary derived/base conversions are not introduced. [Object calls](12-expressions.md#1243-object-member-calls) and [explicit object upcasts](13-operators-and-assignment.md#1357-object-upcasts) retain their defined operations. These rules imply no CLR representation, boxing, garbage collection, or value slicing.

### 6.2.3. Constructors

Syntactically, a qualified Type-shaped path followed by `.init(` is always a construction expression. `init` is excluded from ordinary member Names, so there is no competing ordinary-call parse. Qualification may begin with `::` and contain Type arguments. A Name-shaped qualifier such as `value` is accepted syntactically, then checked using Type lookup; if it identifies only a value, Binding reports an error. There is no retry as a value-member call and no reinitialization of existing storage.

A constructor is a dedicated structure declaration: an optional access specification, `init`, a parameter list, an optional `: base(arguments)` clause, and a common executable Body (§14.2), either single-item or indented. It has no ordinary Name, explicit receiver, separate generic or Origin parameters, or result annotation. Unavailable modifiers follow §2.5.1. It uses the containing structure's Type parameters, Origins, and Constraints. Parameter labels, defaults, and Type checking follow ordinary function parameters. Access defaults to `private`; constructor parameters obey API signature accessibility. Only a structure's own fragments may declare its constructors; groups, enums, contracts, extensions, and executable Blocks may not.

```kimi
public open struct Named
    public let name: string
    protected init(name: string)
        self.name = name

public struct Entry : Named
    public let number: i32
    public init(name: string, number: i32) : base(name)
        self.number = number

let entry = Entry.init("item", 1)
```

`Type.init(arguments)` constructs a fresh owner of exactly Type. The reserved `.init` suffix commits to Type lookup: first resolve the structure and all generic arguments, then select among its own accessible constructors by ordinary argument mapping and overload comparison. Expected results cannot change that Type or lookup path. Supply every generic argument unless the qualifier is already fully bound, such as Self in a generic structure.

Bare type-parameter construction, inherited/extension constructors, field-wise or zero-fill fallbacks, and ordinary function-call fallback are unavailable. Unknown or inaccessible constructors are errors. `Type.init` is not a function value; `value.init(...)` cannot reinitialize storage. Construction returns an owner value; creating `obj`, `rc`, or `arc` requires a separate [Kimi ownership operation](13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing).

After normal argument/default evaluation, allocate fresh Uninitialized construction storage and bind parameters. Evaluate base arguments in that constructor’s parameter/source context and invoke the selected direct-base constructor in its subobject. Omitted base initialization selects an accessible zero-argument invocation, including optional parameters; `: base(...)` without a base is an error. Exactly one base call occurs; same-Type delegation and repeated base initialization are unavailable. This dedicated operation permits protected base constructors without an ordinary instance receiver.

Complete base construction before evaluating this layer's Field declaration initializers, in logical declaration order, and then execute its constructor body. Declaration initializers keep their own declaration-site environments: they cannot reference constructor parameters or `self`. Base arguments cannot use `self` either. No constructor silently initializes a field with zero, null, or an element Type's default constructor. Origins required by stored arguments and the completed base must be represented by the constructed Type's declared Origin contract and inferred under ordinary lifetime constraints; hidden or invented Origins cannot make a construction valid.

Constructor `self` is a special Construction receiver. First writes directly initialize own stored Properties only when all incoming paths are before first placement. Later writes to var with standard set use normal state-dependent placement/Replacement; custom set cannot be called, including at mixed-state joins (§11.3.1). Standard get may Copy or borrow initialized own storage; no non-Copy Move is allowed during construction.

Construction forbids computed access, inherited Field access, whole-self acquisition or borrowing, instance-method/custom-accessor calls on self, and capturing or exposing self. The base constructor handles its own fields. Field borrows must end before completion and cannot escape in the result; borrowed inputs may be stored only under the result’s Origin contract. These privileges apply only inside the constructor body, not nested functions, ordinary methods, or Field declaration initializers.

A constructor body is a Function Boundary with Unit control-flow result. Fallthrough, operandless return, or return of Unit requests success. After any return operand, every reachable successful exit must have a completed base and complete, Initialized own Fields. Run normal Scope Exit—including parameters and Deferred Blocks—while retaining construction storage; then recheck completeness and lifetimes before committing this layer. A defer cannot supply initialization missing at the requested exit, nor can completion checks justify borrowing a destroyed parameter. Base completion continues the next layer; only outermost completion yields the owned result. Moving that result does not rerun constructors.

Constructors have no recoverable-failure return or exception mechanism. Use an ordinary factory returning an Option/Result to perform fallible acquisition and validation before calling `Type.init`; its locals have ordinary cleanup on failure. Abort during any construction phase terminates without unwinding. The partial-initialization cleanup rules also govern interrupted aggregate-expression construction and any ordinary Scope Exit that abandons construction storage; they do not add a new failure syntax or turn an incomplete `return` into success.

After merging, synthesize one public zero-parameter constructor iff there is no selected explicit constructor, every own Field has an initializer, and any base has an accessible zero-argument invocation. Its effective access is the Type’s; its implicit Unit body follows normal initialization/completion. Empty structs meet the field condition. Otherwise no implicit constructor exists, without invalidating the Type itself. Any selected explicit/generated constructor—even private—suppresses synthesis; no memberwise constructor is added. Synthesis depends on declarations, not initializer validity: report resulting errors normally and retain generic obligations until instantiation.

### 6.2.4. Virtual members and overrides

**Extension design; not active in this revision.** This section owns the proposed virtual/override semantics and their interaction with runtime Contracts. Current declarations use §6.2.2's static selection and inherited-Name rule. Unavailable modifiers are diagnosed under §2.5.1.

An overridable declaration and each override require explicit designation. The original declaration identifies the slot; equal Names or Signatures never create one implicitly. Only a valid explicit override would be exempt from the inherited-Name prohibition. Existing ordinary members do not become virtual.

An override preserves Shared/Exclusive receiver kind and, after normalization and receiver-Self correspondence, parameter and result Types. No covariant result is added. Labels and defaults follow the statically selected declaration. Overrides cannot strengthen public premises or input-Origin requirements, or weaken result lifetime guarantees; compatibility must follow existing proof rules.

The target and each overridden accessor must be accessible. Preserve their declared access, except that a protected-internal member overridden in another Kotonoha is declared protected there. Private members, and internal/private-protected members outside their Kotonoha, are ineligible. This grants no permission to expose inaccessible API Types.

Initial virtual calls are safe. Each virtual implementation and override independently requires [ObjectCallCompatible Proven](12-expressions.md#1244-object-receiver-compatibility), including its implementation family; failure is a declaration error even without object call sites. This is an explicit guarantee, unlike an ordinary member's inferred public status. A base body's proof cannot validate an override.

Static lookup selects the declaration, overload, access, labels, and public contract. Runtime dispatch selects only that slot's implementation for the Dynamic Type; it never repeats lookup or adds derived-only overloads.

```text
Animal.reset: explicit Exclusive virtual slot
    ├─ Dog.reset mutates permitted state -> valid override
    └─ Cat.reset replaces all of self    -> invalid override declaration
```

When combined with [runtime Contracts](08-generics-constraints-and-contracts.md#85-runtime-contracts), a retained requirement mapping follows the corresponding valid override. Recheck every mapped requirement at that override's declaration; reject an incompatible override rather than silently losing conformance or Supports. Runtime Contract Views remain a separate extension owned by §8.5.

Before introduction, settle declaration spellings, eligible members/receivers/generics, accessor designation, abstract construction restrictions, explicit base-implementation calls, and slot/metadata information for separate compilation together. This design adds no active syntax or fixed ABI.

## 6.3. Enums

An enum is a nominal, closed sum Core. A complete value contains exactly one **Case** and that Case's **payload**, its attached positional data. Equal Case names and payload Types do not make distinct enum declarations the same Type.

```kimi
public enum Message
    Quit
    Write(string)
    Move(i32, i32)
```

### 6.3.1. Cases and payloads

Write one Case per line without a `case` keyword; PascalCase is conventional. Case names are unique within the enum and cannot overload by payload Type or arity. A payload element declares one complete Type in positional order, without a binding name, `let`/`var`, default, or variadic form. `Quit` has no payload; `Quit()` is invalid, whereas `Wrapped(())` has one Unit payload.

The header supports ordinary Generic and Origin parameters. The body permits Cases, Constraint Clauses, associated-Type specifications for declared conformances, ordinary functions and their full specializations, conditional conformances (§8.4.8), and compile-time Directives selecting these items. Constraints use the existing declaration rules; `Self` denotes the enum Core. Function access and explicit receivers follow ordinary rules. Fields, computed members, `init`, `deinit`, nested Declaration Containers, structure inheritance, `open enum`, and external Case additions are not permitted. Enums cannot have [declaration fragments](#612-container-fragments), and every instantiation must retain at least one Case after compile-time selection. Empty enums and uninhabited-value elimination are deferred.

Each Case has the enum's effective access domain, rather than the ordinary member default of `private`. Cases and payload elements take no access modifiers. Anyone allowed to use the enum may construct and decompose every Case; each payload Type must satisfy [API signature accessibility](09-names-signatures-and-access.md#932-api-signature-accessibility) for the enum's domain. A Case colliding with another Value declaration, including a function, is a declaration error. Adding or removing a public Case is a potentially breaking source API change: additions can break exhaustive matches, and removals can break Case references.

Payload elements are anonymous storage, not named Fields or accessors. Apply the same complete-Type, Semantics, and Origin storage checks as struct stored values. Directly declared borrowed elements require explicit valid Origins; nested Types retain all their dependencies. A struct payload can represent data needing named fields.

```kimi
enum View<T> origin source
    Some(ref/T from source)
    None

enum MutView<T> origin source
    Some(uniq/T from source)
    None

func makeView<T>(value: ref/T)
    -> View<T> from (source => value) => .Some(value)
```

The result annotation maps the enum's abstract `source` to the input's Origin. It describes borrows stored in an owned enum, whereas `ref/T from value` annotates a direct result borrow. The existing [single-Origin shorthand](15-ownership-and-lifetime-analysis.md#1531-origin-arguments) permits `View<T> from value`; named mapping makes the assignment explicit.

At construction, bind payload dependencies to the enum's Origin arguments and validate every stored value against that contract. Infer variance and Loan requirements from occurrences in all Cases, retaining the ordinary fixed-point rules. Selecting a Case does not weaken the Type's Origin contract. Storing or moving out a `uniq/T` payload transfers the exclusive reference value, not its referent; shared reading reborrows it and suspends conflicting exclusive access. Ordinary storage, lifetime, and unique Loan-anchor rules still apply.

Enums initially support owned values and value borrows; constructing or matching enums through Object Semantics is deferred. Their payloads may contain existing Object Semantics. Reject direct or indirect inline recursion without finite size; recursive data requires an existing legal indirection, with no implicit heap allocation. Case changes use whole-value assignment/Replacement, not tag mutation. No ordinary `value.0` or `value.Case` payload extraction is added. Copy follows [enum derivation](03-types-and-values.md#352-enum-copy), and remaining payloads follow [aggregate cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup).

Implicit equality/ordering derivation is not introduced; explicit comparison conformance follows §13.4.1. Integer discriminants/conversions, default values, tag size or numbering, niche optimization, and fixed ABI are unspecified. Declaration order does not define numeric values or a wire format.

### 6.3.2. Case construction and resolution

Use `Type.Case` or, with a known expected enum Type, `.Case`.

**Parsing and Binding.** In expression context, every qualified form uses the ordinary Name, generic-application, member-access, and invocation syntax. The Parser never chooses a Case construction from capitalization, a Type lookup, or the expected Type. For example:

~~~text
Message.Move(1, 2)
    Syntax: Invocation(MemberAccess(Name("Message"), "Move"), [1, 2])
    Binding, if Move resolves to an enum Case: EnumCaseConstruction
~~~

The same syntax represents `value.move(1, 2)` and `Namespace.Type.member()`. Binding uses ordinary qualified lookup (§9.5), retaining both Type-side and Value-side interpretations when required; distinct successful interpretations remain ambiguous. Once lookup selects a Case Symbol, classify the operation as enum construction and check payload presence, labels, count, and Types. A failure does not retry an ordinary method or another lookup stage. A selected non-Case callable follows ordinary invocation rules. A qualified payload-free Case is classified from its member access without a call; a payload Case without a call is an error. Case constructors are not first-class callable values.

Only the leading-dot expression form, such as `.Some(1)`, has dedicated inferred-Case syntax. Its arguments are Expressions. A compiler may create a dedicated bound construction node after resolving either form; this does not require a second parse. The reserved `.init(` suffix retains its separate syntactic rule. In Pattern context, qualified and inferred Case references are parsed by the Pattern grammar, with Pattern operands; they do not compete with expression calls.

Leading-dot layout is decided syntactically under [§2.2.2](02-source-and-lexical-structure.md#222-leading-dot-continuation-and-case-references) before expected-Type lookup. The qualifier identifies an enum Core whose Case set can be determined statically after alias expansion. Generic arguments must be explicit or uniquely inferred. Do not write the enum's own Semantics or Origin arguments in the qualifier; complete Types inside generic arguments retain theirs. Infer/check the constructed value's Origin arguments from its expected Type and payload arguments. If these do not determine them, annotate the expected Type; do not invent hidden Origins or `static`.

```kimi
let quit: Message = Message.Quit
let move: Message = Message.Move(10, 20)
let some: Option<i32> = Option<i32>.Some(42)
let none: Option<i32> = .None
// value: ref/T
let view: View<T> from (source => value) = View<T>.Some(value)
```

`.Case` resolves only within the already known expected enum: the owned expected Type for construction, or the Type determined at that [Pattern position](14-control-flow.md#1481-patterns) for matching. Never search all enums by Case name, retry another expected Type, or change a matched value's Origin contract. This is not general expected-type lookup for members. `let bad = .None` has no known enum Type; `View<T> from (source => value).Some(...)` is not a CaseReference.

A payload-free Case produces its value without parentheses. A payload Case requires all positional arguments in declaration order. Omitted/named arguments, partial application, and acquiring a Case constructor as a function value are invalid.

```kimi
let missing: Option<i32> = .Some  // Error: payload argument required.
let extra: Option<i32> = .None()  // Error: payload-free Case takes no parentheses.
```

Evaluate arguments once each, left to right, and initialize payloads using ordinary argument adaptation, literal fitting, and Copy/Move. Commit a complete enum value only after the Case and all payloads are initialized. On an ordinary transfer out of construction, secure the transfer result and destroy initialized payloads in reverse order; Abort does not unwind. Enum Case construction is a dedicated bound operation; its qualified expression syntax remains ordinary member access/invocation. Case Pattern operands are Patterns, not expressions.

During ordinary abandonment of aggregate construction, interleave cleanup of placed components and still-live expression temporaries in reverse order of completed placement/value acquisition, while preserving inner-to-outer scope exit under §16.2.1; transferring responsibility removes the source from that cleanup order.

## 6.4. Bindings

Fields and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` declares a mutable binding. A Type annotation and an initializer are independently optional when the omitted information can be inferred.

**Basic example.**

```kimi
let limit: i32 = 10
var current = 0
```

A local Type must be fixed at declaration, even without an initializer. An explicit local Type with an omitted borrow Origin or required Origin argument needs a declaration initializer under [Origin inference](15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts); a later first assignment cannot supply the missing annotation. A fully specified Type may still omit its initializer under the ordinary initialization rules. Locals become visible after their declaration, so an initializer `let x = x` refers to an outer `x`; duplicate and forward-reference rules follow [name visibility](09-names-signatures-and-access.md#92-namespaces-roles-and-visibility). `let` permits only its first initialization, and Move never resets that history. Definite initialization and permitted reinitialization follow [initialization-state rules](15-ownership-and-lifetime-analysis.md#1511-storage-state-and-responsibility).

## 6.5. Attributes

**Attribute syntax.** `#Name` accepts an optional parenthesized, comma-separated Argument list with a trailing comma. Name must begin with an uppercase Unicode letter; lowercase if/switch/case select directives, and other lowercase forms are errors. Attributes attach in source order to the next same-indentation declaration, on its line or preceding effective lines. Comments/blank lines may intervene; unrelated items and dedents may not. Dangling Attributes are errors.

Attributes are accepted on ordinary Container, function, Field, and computed declarations, and on function parameters before the parameter Name. Explicit specializations and Contract requirements retain their prohibition on Attributes; expression statements, patterns, arguments, and accessor lists do not accept Attribute prefixes. Excluded syntax follows §19.5: argument and declaration-placement grammar is checked only where ordinary parsing is required, and excluded Attributes undergo no semantic resolution. The recognized Attributes are `#Layout` for struct storage (§21.1.2), `#LibraryImport` for [foreign functions](22-core-execution-and-foreign-functions.md#223-foreign-function-imports), and `#Test` for [test definitions](#651-test-definitions). Their concrete argument and target rules are checked after selection; no ordinary Name lookup supplies their literal arguments.

**Mod markers.** A [Mod](20-compilation-configuration.md#207-mods-source-generation) may use Attributes to find targets. A marker does not request execution or consume an Attribute: several Mods may inspect the same Attribute, and a later Mod may emit markers for an already completed Mod without restarting it or causing an error merely for that reason.

Expose marker names, argument syntax, and target Koto before the target Type is fully bound. Queries use environment-selected syntax, excluding discarded declarations. Syntax-name matching does not establish semantic identity between unrelated same-spelled Attributes. A Mod's registration or accompanying contract must identify its markers and argument rules. Semantic argument queries obey the [Binding access period](20-compilation-configuration.md#2072-compilation-and-binding); syntax remains readable afterward. Neither discovery nor argument inspection requires executing the target program or an Attribute constructor.

Marker discovery and validation are distinct. Diagnose a selected Attribute that remains unrecognized by final validation; finding its syntax does not make every unknown Attribute valid. Concrete marker registration/recognition APIs and general Attribute semantics beyond these rules, Layout, LibraryImport, and Test remain design boundaries.

### 6.5.1. Test definitions

`#Test` takes no arguments and marks a test definition. The initial revision assigns one case to each definition. It requires a safe ordinary named function with a body, no parameters or receiver (including defaulted parameters), and Unit return Type, written `-> ()` or omitted. Neither the function nor an ancestor Container may have unresolved generic or Origin parameters. Permit source-level functions and receiver-free functions in nongeneric group, rootgroup, struct, or enum Containers. Do not synthesize an instance for a struct test. Foreign imports, explicit specializations, local functions inside another function, and functions requiring captures are ineligible. Duplicate Test Attributes and invalid targets/signatures are errors, never silently skipped registration.

```kimi
group Arithmetic
    func add(left: i32, right: i32) -> i32 => left + right

    #Test
    func addition()
        $expect(add(1, 2) == 3)
```

In ordinary builds, check selected Attribute/declaration syntax and target conditions decidable from syntax, but do not resolve test function names, check their bodies, generate their code, or require test-only dependencies. Test builds perform full target and body verification. Preserve §19.5's exclusions. Source-level tests retain SourceDocument-local lookup. Product/test membership follows [§18.8](18-modules-and-dependencies.md#188-product-and-test-inputs).

User code may neither call a Test function nor acquire it as a function value. Put shared work in ordinary helpers. Only generated test startup may invoke nonpublic tests; this privilege does not widen a test body's access rights. Discovery and execution follow [§20.9](20-compilation-configuration.md#209-test-command-and-discovery) and [§22.6](22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting).
