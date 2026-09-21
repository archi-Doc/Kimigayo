# 6. Declarations and containers

[Specification index](../SPEC.md)

Kimigayo identifies declarations and their meaning by:

| Element     | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| `Name`      | The basic human-readable name used to refer to a declaration          |
| `Signature` | The information that distinguishes declarations in the same scope     |
| `Type`      | The meaning of a value or invocation within the type system           |

See [names, signatures and access](09-names-signatures-and-access.md#9-names-signatures-and-access) and [overload selection](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference). Field and computed-member syntax is defined in [Properties](11-properties.md#11-properties).

## 6.1. Declaration containers

A **Declaration Container** is a named declaration scope whose indented body may contain Fields, computed members, functions, associated-Type declarations and specifications, Constraint Clauses, or nested Declaration Containers, as permitted by its kind.

| Declaration Container kind | Instantiable | Main characteristics |
| --------------- | ------------ | -------------------- |
| `group` | No | Accepts Fields, computed members, functions and nested Declaration Containers. All members are static. It declares no parameters of its own and inherits the enclosing generic and Origin environment. |
| `struct` | Yes | Accepts nested groups, structs, enums and Contracts, Fields, computed members, functions, conditional conformance declarations, associated-Type specifications, constructors, and at most one selected `deinit`. Supports generic parameters, Origins and Constraints. Sealed by default; `open struct` permits derivation. |
| `enum` | Yes | A closed sum Type with Cases, positional payloads, functions, conditional conformance declarations and Constraints; see [enums](#63-enums). No fragments or inheritance. |
| `contract` | No | Declares function and Property requirements, associated Types and Constraints; supports refinement of multiple parents. No implementations, storage or parameters of its own; enclosing generic and Origin bindings are inherited. See [Contracts](08-generics-constraints-and-contracts.md#84-static-contracts). |
| `extension` (future) | — | Not introduced; see the extension boundary below. |

**Static members.** `static` is not a declaration modifier in this revision: `static func`, `static let` and `static var` are rejected, including redundant uses in groups. Members of a group or rootgroup are inherently static, a struct function without a receiver is a Type function, and struct Fields and computed members are instance members. The contextual Origin spelling `{static}` remains valid and does not make a declaration static.

**Extension boundary.** This revision accepts no `extension` declarations or imports and supplies no extension candidates. Later references to future extension access, identity and precedence constrain that future design only; they are not active lookup stages or conformance mechanisms. Ordinary member lookup is complete without an extension pass.

### 6.1.1. Root and nested containers

Each source unit contributes named declarations to the project root. Top-level bindings (`let` and `var`), local functions and executable items keep a SourceDocument-local scope and are not exported to other files. The exception is an explicit root-level `public main`: an ordinary function in the shared root that keeps its declaration-site lookup environment and cannot capture top-level runtime locals. Other shared functions, Fields and computed members belong in named Containers. [Program startup](22-core-execution-and-foreign-functions.md#222-program-startup-and-static-initialization) selects either one document's runtime body or one eligible public `main`. Aliases follow the [source-local import rules](18-modules-and-dependencies.md#181-external-references-and-aliases).

A `rootgroup` declaration starts at the root and accepts a dot-separated Name:

```kimi
rootgroup A.B
    var value = 1
```

This creates the nested group path `A.B`. A `rootgroup` is allowed only directly at the source root, including selected root directive items. Its path creates groups; it never synthesizes or redefines an intermediate struct. A synthesized intermediate group uses the accessibility of an explicit group declaration of the same path when one exists, and `private` otherwise; synthesis is not an independent header fragment. A public path therefore requires explicitly accessible groups.

| Declaration site | Allowed nested Containers |
| --- | --- |
| Project root, group, struct | group, struct, enum, contract |
| enum, contract | None |
| Executable Block or conditional-conformance implementation Block | None |

Nesting is recursive, with no fixed language depth limit; implementation limits require resource diagnostics. Membership adds no storage, implicit outer instance or receiver, inheritance, Copy, ownership or conformance.

```kimi
struct Parser
    public enum Result
        Success
        Failure

    private struct State
        var position: i32 = 0

    public group Diagnostics
        public struct Location
            var line: i32 = 0
```

**Empty declaration bodies.** A group, rootgroup, struct or contract may omit its indented body entirely. The header is complete when followed by the end of file or by the next effective item at the same or shallower indentation. Blank and comment-only lines, including indented comments, neither create a body nor cause the next item to be absorbed. An indented body may also become empty after directive selection. A bodyless contract is a marker Contract with no requirements; a bodyless struct is an empty structure, subject to its base and implicit-constructor rules. These permissions do not extend to enums (which require at least one selected Case), match-arm lists or executable Blocks. A `()` expression is not an empty-Container marker, because ordinary executable items are not Container members.

### 6.1.2. Container fragments

Group and struct declarations may be split into fragments, even within one file. Same-parent, same-name fragments are collected; a declaration is identified by its originating Kotonoha, parent Symbol, name, kind and generic arity. Conflicting kinds, such as a group and a struct with the same name, are rejected. Different arities, such as `Box` and `Box<T>`, are different Types. Fragments never merge across Kotonoha libraries, and an extension is never a fragment of its target declaration. Applied `ref`/`uniq` Semantics do not change Container identity.

Matching fragments must agree on generic parameter count, kinds, order and names; declaration kind; semantic modifiers; and accessibility after defaults. Every struct fragment writes the same closed Origin header, including `{}` for no own slots; names, order and count must match. Conflicting accessibility is not widened. Exactly one fragment may supply the shared Constraint region, including Type Constraints and `origin` relations, even if duplicate clauses would be identical. The other fragments share that contract; each clause resolves in its defining SourceDocument.

For structures, `open` must agree across all fragments. At most one fragment supplies the base clause; the others share that base without repeating it. The base resolves in its fragment's source environment, and then the complete merged inheritance relationship is validated.

After compile-time selection and merging, duplicate Field and computed Names, duplicate function Signatures, duplicate constructor Signatures and namespace conflicts are rejected. Selected fragments, including generated ones, may contribute Fields under [split-structure storage order](#621-split-structures-and-storage-order); C layout requires a single storage-bearing fragment (§21.1.2). No primary fragment is required. At most one selected `deinit` body may belong to a merged structure, including generated fragments; destruction bodies are neither concatenated nor chosen by source order, and duplicates are declaration errors. Mutually exclusive declarations or bodies may coexist in source only when selection leaves at most one in each fixed target/configuration environment.

Enums and Contracts cannot be split. After directive selection and source generation, multiple declarations with the same enum identity, or the same Contract identity (originating Kotonoha, parent Symbol, name and kind), are declaration errors, even when their contents are identical. User Contracts declare no generic slots of their own, so inherited bindings do not distinguish declarations. Different Contracts may refine a shared parent; refinement is not declaration merging.

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

**Future design.** Contract fragments are not introduced; a future fragment facility needs explicit contents and merging rules. Extension identity and public names remain separately specified.

### 6.1.3. Inherited environments and declaration references

Every nested Container, including a Contract or a group, inherits the enclosing generic environment; a group does not stop inheritance. Runtime variables, values and receivers are not inherited. An inner declaration is checked as generic whenever any enclosing parameter exists, even if it is unused.

Three kinds of context are kept separate:

| Context | Contents | Part of reference bindings? |
| --- | --- | --- |
| Argument bindings | Type/Semantics arguments and complete Origin slot bindings | Yes |
| Proof context | Input conditions, Origin bounds, obligations, evidence and dependencies | No |
| Lookup context | Definition-site lexical scope, each fragment's aliases, access context | No |

A **declaration reference** is the original declaration identity plus normalized argument bindings. Identity uses the originating Kotonoha, the merged parent declaration, the name, the kind and the declaration's own arity. A concrete parent's arguments belong to the bindings, not to the declaration identity. Outer arguments are retained even when unused, and own arity excludes inherited slots. Every slot is identified by its declaring binder and ordinal, never by spelling alone.

```kimi
struct Outer<T>
    public struct Inner<U>
        var first: T
        var second: U

    public struct Tag

    public contract Sink
        func write(self: ref/Self, value?: T) -> ()
```

`Outer<i32>.Inner<string>` and `Outer<i64>.Inner<string>` are distinct Types. The corresponding empty `Tag` Types and bound `Sink` references also differ. Full Types and conformance evidence keep their Origins; an Origin-erased runtime or generation key proves neither equal evidence nor permission to share code.

Group and struct fragments are merged recursively under the merged parent identity before bindings are applied. Each fragment repeats only its own header. Each fragment's source environment is preserved, and fragments are never selected or merged separately per instantiation. Completing the outer Type's layout is not a prerequisite for resolving a child declaration.

#### 6.1.3.1. Constraint roles and definition checking

Each Constraint keeps its original binder, declaration and role. Caller-supplied input conditions are assumptions during definition checking. Declaration obligations, including an outer struct's Self conformance and closed conditions, require independent proof. Inner declarations may use the outer input conditions and valid proven evidence, but cannot turn obligations into assumptions or reinterpret the outer Self as the inner Self. Additional inner input conditions do not propagate outward.

For a Contract, a condition that depends only on inherited arguments is a reference input condition; one that depends on the conforming Self is an implementation requirement; a closed condition is a declaration obligation. The existing restrictions on function Constraint subjects still apply. Declarations are collected and references resolved before proofs are completed, so a struct may implement its nested Contract without a containment-induced proof cycle. A conformance being checked cannot prove itself. Actual evidence dependencies are retained, and every obligation is discharged by its existing deadline for all admitted bindings, not just favorable instantiations.

#### 6.1.3.2. Self and member ownership

`Self` denotes the innermost struct or enum with all its bindings, or the conforming Type inside a Contract. A group creates no Self context; inside it, `Self` names the nearest outer Self Type. Further outer Types need an explicit path. Group functions and Properties are static and cannot use receiver shorthand; an explicitly typed ordinary parameter named `self` remains valid (§7.3).

```kimi
struct Parser
    group Helpers
        func identity(value?: Self) -> Self => value // Parser
        struct State
            func identity(value?: Self) -> Self => value // State
```

Neither function has an implicit receiver. A group body cannot directly contain a Constraint Clause, conformance declaration or associated-Type specification; its own functions and nested Types keep their permitted Constraints.

## 6.2. Structure declarations

A `struct` declares a named composite Core. Its Fields and computed members have complete Types, including their Semantics and Origin dependencies:

```kimi
struct Point
    var x: f64
    var y: f64

struct Node
    var value: i32
    var next: obj/Node

struct View {source}
    var data: ref{source}/Data
```

`Data` is an assumed Core; the instance borrow's Origin is explicitly declared and bound.

Explicit and implicit constructors follow [§6.2.3](#623-constructors). All construction obeys the [initialization rules](11-properties.md#113-types-and-origins) and [construction completeness](15-ownership-and-lifetime-analysis.md#1512-aggregate-construction-and-completeness).

### 6.2.1. Split structures and storage order

A `struct` may be split into [declaration fragments](#612-container-fragments), including fragments produced by a [Mod](20-compilation-configuration.md#207-mods-source-generation); identity, header agreement, member uniqueness and the C-layout single-storage-fragment restriction follow that section. Only Fields contribute storage slots; computed members add none.

The [logical declaration order](20-compilation-configuration.md#2074-generated-sources-and-declaration-order) applies to the environment-selected fragments of each instantiation. It determines the side-effect order of Field initializers, independently of physical layout.

Mods may inspect provisional Types under [provisional Binding](20-compilation-configuration.md#2072-compilation-and-binding). Layout is finalized only after all Mods complete and the full selected fragment set and storage classification are known; no later generation may append storage to a finalized Type.

Physical layout and ABI guarantees follow [structure layout and ABI](21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi).

### 6.2.2. Inheritance and open structures

A structure is **sealed** unless `open` immediately precedes `struct` in its declaration. A sealed structure cannot be a base Type. `open` permits derivation and is independent of accessibility: `public struct` remains sealed, while `internal open struct` permits derivation only where the Type is accessible. A derived structure is sealed unless it is itself declared `open`; openness is not inherited. No separate `sealed` modifier exists.

A structure may name one direct base with `: BaseType`, after its Name, generic parameters and optional Origin list. The base must resolve to an accessible constructed or nongeneric `open struct` Core; a generic parameter, a Semantics-applied Type, a group, an enum and a contract are not bases. Omitting the clause declares no user-defined base. Multiple bases and direct or indirect inheritance cycles, including cycles through different constructions of one generic declaration, are rejected. The base's Constraints must hold, and its accessibility must cover the derived Type's [effective access domain](09-names-signatures-and-access.md#932-api-signature-accessibility). Constraint Clauses continue to express capabilities separately from the base clause.

```kimi
public open struct Base
    protected var count: i32 = 0

public struct Leaf : Base
    public func read(self: ref/Self) -> i32 => self.count

public struct Invalid : Leaf // Error: Leaf is sealed.
```

Inheritance preserves the members' declared accessibility and grants no private access. Ordinary function and accessor calls use the implementation selected statically from the receiver's Effective Type, including through concrete Core object views; the Runtime Object Type does not replace that implementation. [Inherited lookup](09-names-signatures-and-access.md#95-qualified-and-inherited-lookup) determines the declaration layer, and [receiver projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection) supplies eligible instance receivers. Function-value calls keep their own rules.

**Inherited Names.** A derived struct cannot declare a Value-role member with the same Name as a member of any ancestor that is accessible from the derived declaration. This covers functions, regardless of Signature or instance/Type-function kind, as well as Fields and stored and computed Properties. Conditional premises and overload applicability do not exempt a declared Name. A Property's own access, not an individual accessor's, is checked, and protected access uses the derived receiver.

The check runs on the completed declaration set after environment selection, Mod output and fragment merging, with a valid base graph, and a collision is diagnosed at the derived declaration. Same-layer overloads and other namespaces keep their rules. Inaccessible ancestor Names, including private or cross-Kotonoha internal and private-protected members, may be reused. An explicit specialization implements its original function and is not another member; it gains no right to specialize a base member from a derived Container.

The accessible Value-role Names of an open struct are part of its API toward derived Types. Adding Names or expanding access can invalidate downstream declarations under [dependency revalidation](21-layout-runtime-and-code-generation.md#2134-artifacts-verification-and-invalidation). Derived Types must tolerate the mutations and preconditions exposed by base APIs; stronger application invariants cannot become unchecked memory-safety assumptions.

**Base subobject.** The direct base is one inline owned subobject, logically preceding the structure's directly declared fields. Base subobjects keep their own field identities and construction-completion facts. Physical offsets remain compiler-selected under the [layout rules](21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi); logical ordering requires neither flattening nor a fixed ABI. Construction runs the base first (§6.2.3), and destruction runs the derived layer first ([field cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup)). A base subobject cannot be independently Moved, replaced or reconstructed through a base view of a derived value. Whole-value operations keep the exact owning Type and the responsibility for all layers; they cannot slice a derived value. Access to an inherited field still obeys normal permissions and checks every containing base and derived layer for Partial Move restrictions.

Explicit ordinary base-member invocation and additional ordinary derived/base conversions are not introduced. [Object calls](12-expressions.md#1243-object-member-calls) and [explicit object upcasts](13-operators-and-assignment.md#1357-object-upcasts) keep their defined operations. None of these rules imply a CLR-like representation, boxing, garbage collection or value slicing.

### 6.2.3. Constructors

#### 6.2.3.1. Declaration

A constructor is a dedicated structure declaration: an optional access specification, `init`, a parameter list, an optional `: base(arguments)` clause, and a common executable Body (§14.2), either single-item or indented. It has no ordinary Name, explicit receiver, separate generic parameters, or result annotation; unavailable modifiers follow §2.5.1. It uses the containing structure's Type parameters, Origins and Constraints. Its directly written borrow annotations may introduce implicit per-call scalar Origins (§15.3.4); there is no list after `init`. Attached Origin relations precede executable items and do not add hidden result slots. Input omission follows §15.4 and is never inferred backward from field assignments. Parameter labels, name-omission permissions, defaults and Type checking follow ordinary function parameters. Access defaults to `private`, and constructor parameters obey API signature accessibility. Only a structure's own fragments may declare its constructors; groups, enums, contracts, extensions and executable Blocks may not.

```kimi
public open struct Named
    public let name: string
    protected init(name?: string)
        self.name = name

public struct Entry : Named
    public let number: i32
    public init(name?: string, number?: i32) : base(name)
        self.number = number

let entry = Entry.init("item", 1)
```

#### 6.2.3.2. Construction expressions

Syntactically, a qualified Type-shaped path followed by `.init(` is always a construction expression. `init` is excluded from ordinary member Names, so no competing ordinary-call parse exists. The qualification may begin with `::` and contain Type arguments. A Name-shaped qualifier such as `value` is accepted syntactically and then checked with Type lookup; if it identifies only a value, Binding reports an error. There is no retry as a value-member call and no reinitialization of existing storage.

`Type.init(arguments)` constructs a fresh owner of exactly `Type`. The reserved `.init` suffix commits to Type lookup: the structure and all generic arguments are resolved first, and then one of its own accessible constructors is selected by ordinary argument mapping and overload comparison. Expected results cannot change that Type or lookup path. Every generic argument must be supplied unless the qualifier is already fully bound, such as `Self` in a generic structure.

Bare Type-parameter construction, inherited or extension constructors, field-wise or zero-fill fallbacks and ordinary function-call fallback are unavailable. Unknown or inaccessible constructors are errors. `Type.init` is not a function value, and `value.init(...)` cannot reinitialize storage. Construction returns an owner value; creating `obj`, `rc` or `arc` requires a separate [Kimi ownership operation](13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing).

#### 6.2.3.3. Evaluation order

After normal argument and default evaluation, fresh Uninitialized construction storage is allocated and the parameters are bound. The base arguments are evaluated in the constructor's parameter and source context, and the selected direct-base constructor runs in its subobject. An omitted base initialization selects an accessible zero-argument invocation, including one whose parameters all have defaults; `: base(...)` without a base is an error. Exactly one base call occurs; same-Type delegation and repeated base initialization are unavailable. This dedicated operation may call protected base constructors without an ordinary instance receiver.

Base construction completes before this layer's Field declaration initializers are evaluated, in logical declaration order, and then the constructor body executes. Declaration initializers keep their own declaration-site environments: they cannot reference constructor parameters or `self`, and neither can base arguments. No constructor silently initializes a field with zero, null or an element Type's default constructor. Origins required by stored arguments and by the completed base must be represented by the constructed Type's declared Origin contract and inferred under ordinary lifetime constraints; hidden or invented Origins cannot make a construction valid.

#### 6.2.3.4. The construction receiver

Constructor `self` is a special construction receiver. A first write directly initializes an own stored Property only when every incoming path is before its first placement. Later writes to a `var` with standard `set` use normal state-dependent placement or Replacement; a custom `set` cannot be called, including at mixed-state joins (§11.3.1). Standard `get` may Copy or borrow initialized own storage; no Non-Copy Move is allowed during construction.

Construction forbids computed access, inherited Field access, whole-`self` acquisition or borrowing, instance-method and custom-accessor calls on `self`, and capturing or exposing `self`. The base constructor handles its own fields. Field borrows must end before completion and cannot escape in the result; borrowed inputs may be stored only under the result's Origin contract. These privileges apply only inside the constructor body, not in nested functions, ordinary methods or Field declaration initializers.

#### 6.2.3.5. Completion and failure

A constructor body is a Function Boundary with a Unit control-flow result. Fallthrough, an operandless `return`, or a `return` of Unit requests success. After any `return` operand, every reachable successful exit must have a completed base and complete, Initialized own Fields. Normal Scope Exit then runs, including parameter cleanup and Deferred Blocks, while construction storage is retained; completeness and lifetimes are rechecked before this layer is committed. A `defer` cannot supply initialization missing at the requested exit, and completion checks cannot justify borrowing a destroyed parameter. Completion of a base layer continues with the next layer; only the outermost completion yields the owned result. Moving that result does not rerun constructors.

Constructors have no recoverable-failure return or exception mechanism. Use an ordinary factory returning an `Option` or `Result` to perform fallible acquisition and validation before calling `Type.init`; its locals have ordinary cleanup on failure. Abort during any construction phase terminates without unwinding. The partial-initialization cleanup rules also govern interrupted aggregate-expression construction and any ordinary Scope Exit that abandons construction storage; they add no failure syntax and do not turn an incomplete `return` into success.

#### 6.2.3.6. Implicit constructor

After merging, one public zero-parameter constructor is synthesized if and only if there is no selected explicit constructor, every own Field has an initializer, and any base has an accessible zero-argument invocation. Its effective access is the Type's, and its implicit Unit body follows normal initialization and completion. Empty structs meet the field condition. Otherwise no implicit constructor exists, and the Type itself remains valid. Any selected explicit or generated constructor, even a private one, suppresses synthesis; no memberwise constructor is added. Synthesis depends on declarations, not on initializer validity: resulting errors are reported normally, and generic obligations are retained until instantiation.

### 6.2.4. Virtual members and overrides

**Extension design; not active in this revision.** This section owns the proposed virtual/override semantics and their interaction with runtime Contracts. Current declarations use the static selection and inherited-Name rule of §6.2.2, and unavailable modifiers are diagnosed under §2.5.1.

An overridable declaration and each override require explicit designation. The original declaration identifies the slot; equal Names or Signatures never create one implicitly. Only a valid explicit override would be exempt from the inherited-Name prohibition. Existing ordinary members do not become virtual.

An override preserves the Shared/Exclusive receiver kind and, after normalization and receiver-Self correspondence, the parameter and result Types. No covariant result is added. Labels, name-omission permissions and defaults follow the statically selected declaration. An override cannot strengthen public premises or input-Origin requirements, or weaken result lifetime guarantees; compatibility follows the existing proof rules.

The target and each overridden accessor must be accessible. Their declared access is preserved, except that a protected-internal member overridden in another Kotonoha is declared protected there. Private members, and internal or private-protected members outside their Kotonoha, are ineligible. This grants no permission to expose inaccessible API Types.

Initial virtual calls are safe: each virtual implementation and override independently requires [ObjectCallCompatible Proven](12-expressions.md#1244-object-receiver-compatibility), including its implementation family, and failure is a declaration error even without object call sites. This is an explicit guarantee, unlike an ordinary member's inferred public status. A base body's proof cannot validate an override.

Static lookup selects the declaration, overload, access, labels and public contract. Runtime dispatch selects only that slot's implementation for the Dynamic Type; it never repeats lookup or adds derived-only overloads.

```text
Animal.reset: explicit Exclusive virtual slot
    ├─ Dog.reset mutates permitted state -> valid override
    └─ Cat.reset replaces all of self    -> invalid override declaration
```

With [runtime Contracts](08-generics-constraints-and-contracts.md#85-runtime-contracts), a retained requirement mapping follows the corresponding valid override. Every mapped requirement is rechecked at that override's declaration, and an incompatible override is rejected rather than silently losing conformance or Supports. Runtime Contract Views remain a separate extension owned by §8.5.

Before introduction, declaration spellings, eligible members, receivers and generics, accessor designation, abstract construction restrictions, explicit base-implementation calls, and slot and metadata information for separate compilation must be settled together. This design adds no active syntax or fixed ABI.

## 6.3. Enums

An enum is a nominal, closed sum Core. A complete value contains exactly one **Case** and that Case's **payload**, its attached positional data. Equal Case names and payload Types do not make distinct enum declarations the same Type.

```kimi
public enum Message
    Quit
    Write(string)
    Move(i32, i32)
```

### 6.3.1. Cases and payloads

Each Case is written on its own line, without a `case` keyword; PascalCase is conventional. Case names are unique within the enum and cannot be overloaded by payload Type or arity. A payload element declares one complete Type, in positional order, without a binding name, `let`/`var`, default or variadic form. `Quit` has no payload, and `Quit()` is invalid, whereas `Wrapped(())` has one Unit payload.

The header supports ordinary generic parameters and an optional closed Origin schema, including `{}`. Without a header, at most one own scalar Origin may be discovered across all payload Types (§15.3.2). The body permits Cases, Constraint Clauses, associated-Type specifications for declared conformances, ordinary functions and their full specializations, conditional conformances (§8.4.8), and compile-time directives selecting these items. Constraints follow the ordinary declaration rules, and `Self` denotes the enum Core. Function access and explicit receivers follow the ordinary rules. Fields, computed members, `init`, `deinit`, nested Declaration Containers, structure inheritance, `open enum` and external Case additions are not permitted. Enums cannot have [declaration fragments](#612-container-fragments), and every instantiation must keep at least one Case after compile-time selection. Empty enums and uninhabited-value elimination are deferred.

**Access.** Each Case has the enum's effective access domain, rather than the ordinary member default of `private`; Cases and payload elements take no access modifiers. Anyone allowed to use the enum may construct and decompose every Case, and each payload Type must satisfy [API signature accessibility](09-names-signatures-and-access.md#932-api-signature-accessibility) for the enum's domain. A Case that collides with another Value declaration, including a function, is a declaration error. Adding or removing a public Case is a potentially breaking source API change: additions can break exhaustive matches, and removals can break Case references.

**Payload storage.** Payload elements are anonymous storage, not named Fields or accessors, and follow the same complete-Type, Semantics and Origin storage checks as struct stored values. Directly declared borrowed elements require explicit valid Origins; nested Types keep all their dependencies. Use a struct payload for data that needs named fields.

```kimi
enum View<T> {source}
    Some(ref{source}/T)
    None

enum MutView<T> {source}
    Some(uniq{source}/T)
    None

func makeView<T>(value?: ref/T)
    -> View<T>{result}
    origin result.source == value
    return .Some(value)
```

The result annotation maps the enum's abstract `source` to the input's Origin. It describes borrows stored in an owned enum, whereas `ref{value}/T` annotates a direct result borrow. The [single-Origin shorthand](15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets) also permits `View<T>{value}`; the named mapping makes the assignment explicit.

At construction, payload dependencies bind to the enum's Origin arguments, and every stored value is validated against that contract. Variance and Loan requirements are inferred from the occurrences in all Cases under the ordinary fixed-point rules. Selecting a Case does not weaken the Type's Origin contract. Storing or moving out a `uniq/T` payload transfers the exclusive reference value, not its referent; a shared read reborrows it and suspends conflicting exclusive access. The ordinary storage, lifetime and unique Loan-anchor rules still apply.

Enums initially support owned values and value borrows; constructing or matching enums through Object Semantics is deferred, though payloads may contain existing Object Semantics. Direct or indirect inline recursion without a finite size is rejected; recursive data requires an existing legal indirection, with no implicit heap allocation. A Case change is a whole-value assignment or Replacement, not a tag mutation. No ordinary `value.0` or `value.Case` payload extraction exists. Copy follows [enum Copy](03-types-and-values.md#352-enum-copy), and remaining payloads follow [aggregate cleanup](16-scope-exit-and-destruction.md#1632-field-cleanup).

Implicit equality or ordering derivation is not introduced; explicit comparison conformance follows §13.4.1. Integer discriminants and conversions, default values, tag size or numbering, niche optimization and a fixed ABI are unspecified. Declaration order defines neither numeric values nor a wire format.

### 6.3.2. Case construction and resolution

A Case is constructed with `Type.Case` or, when the expected enum Type is known, with `.Case`.

**Parsing and Binding.** In expression context, every qualified form uses the ordinary Name, generic-application, member-access and invocation syntax. The parser never chooses a Case construction from capitalization, a Type lookup or the expected Type. For example:

~~~text
Message.Move(1, 2)
    Syntax: Invocation(MemberAccess(Name("Message"), "Move"), [1, 2])
    Binding, if Move resolves to an enum Case: EnumCaseConstruction
~~~

The same syntax represents `value.move(1, 2)` and `Namespace.Type.member()`. Binding uses ordinary qualified lookup (§9.5), keeping both the Type-side and the Value-side interpretation when required; distinct successful interpretations remain ambiguous. Once lookup selects a Case Symbol, the operation is classified as enum construction, and payload presence, labels, count and Types are checked. A failure does not retry an ordinary method or another lookup stage. A selected non-Case callable follows the ordinary invocation rules. A qualified payload-free Case is classified from its member access without a call; a payload Case without a call is an error. Case constructors are not first-class callable values.

Only the leading-dot expression form, such as `.Some(1)`, has dedicated inferred-Case syntax; its arguments are expressions. A compiler may create a dedicated bound construction node after resolving either form without a second parse. The reserved `.init(` suffix keeps its separate syntactic rule. In Pattern context, qualified and inferred Case references are parsed by the Pattern grammar, with Pattern operands; they do not compete with expression calls.

Leading-dot layout is decided syntactically under [§2.2.2](02-source-and-lexical-structure.md#222-leading-dot-continuation-and-case-references), before expected-Type lookup. The qualifier identifies an enum Core whose Case set is statically determinable after alias expansion. Generic arguments must be explicit or uniquely inferred. The enum's own Semantics and Origin arguments are not written in the qualifier; complete Types inside generic arguments keep theirs. The constructed value's Origin arguments are inferred and checked from its expected Type and payload arguments; if these do not determine them, annotate the expected Type. Hidden Origins or `static` are never invented.

```kimi
let quit: Message = Message.Quit
let move: Message = Message.Move(10, 20)
let some: Option<i32> = Option<i32>.Some(42)
let none: Option<i32> = .None
// value: ref/T
let view: View<T> = View<T>.Some(value)
    origin view.source == value
```

`.Case` resolves only within the already known expected enum: the owned expected Type for construction, or the Type determined at the [Pattern position](14-control-flow.md#1481-patterns) for matching. It never searches all enums by Case name, retries another expected Type, or changes a matched value's Origin contract; this is not general expected-Type member lookup. `let bad = .None` has no known enum Type, and `View<T>{v}.Some(...)` is not a Case reference.

A payload-free Case produces its value without parentheses. A payload Case requires all positional arguments in declaration order. Omitted or named arguments, partial application, and acquiring a Case constructor as a function value are invalid.

```kimi
let missing: Option<i32> = .Some  // Error: payload argument required.
let extra: Option<i32> = .None()  // Error: payload-free Case takes no parentheses.
```

Arguments are evaluated once each, left to right, and payloads are initialized by ordinary argument adaptation, literal fitting and Copy/Move. A complete enum value is committed only after the Case and all payloads are initialized. On an ordinary transfer out of construction, the transfer result is secured and initialized payloads are destroyed in reverse order; Abort does not unwind. Cleanup of abandoned aggregate construction follows §16.2.1.

## 6.4. Bindings

Fields and local bindings begin with `let` or `var`. For a local binding, `let` declares an immutable binding and `var` a mutable one. The Type annotation and the initializer are each optional when the omitted information can be inferred.

```kimi
let limit: i32 = 10
var current = 0
```

A local's Type must be fixed at its declaration, even without an initializer. An explicit local Type that omits a borrow Origin or a required Origin argument needs a declaration initializer under [Origin inference](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision); a later first assignment cannot supply the missing information. A fully specified Type may omit its initializer under the ordinary initialization rules. A local becomes visible after its declaration, so the initializer of `let x = x` refers to an outer `x`; duplicate and forward-reference rules follow [name visibility](09-names-signatures-and-access.md#92-namespaces-roles-and-visibility). `let` permits only its first initialization, and a Move never resets that history. Definite initialization and permitted reinitialization follow the [initialization-state rules](15-ownership-and-lifetime-analysis.md#1511-storage-state-and-responsibility).

## 6.5. Attributes

[Documentation association](02-source-and-lexical-structure.md#232-declaration-association) uses the declaration prelude without changing Attribute targets or placement. Attributes carry metadata; documentation remains explanatory Markdown.

**Syntax.** `#Name` accepts an optional parenthesized, comma-separated argument list, with an optional trailing comma. `Name` must begin with an uppercase Unicode letter; lowercase forms other than the `if`/`switch`/`case` directives are errors. Attributes attach, in source order, to the next declaration at the same indentation, on its line or on preceding effective lines. Comments and blank lines may intervene; unrelated items and dedents may not. A dangling Attribute is an error.

**Placement.** Attributes are accepted on ordinary Container, function, Field and computed declarations, and on function parameters before the parameter Name. Explicit specializations and Contract requirements keep their prohibition on Attributes; expression statements, Patterns, arguments and accessor lists accept no Attribute prefixes. Excluded syntax follows §19.5: argument and declaration-placement grammar is checked only where ordinary parsing is required, and excluded Attributes undergo no semantic resolution.

**Recognized Attributes.** `#Layout` applies to struct storage (§21.1.2), `#LibraryImport` to [foreign functions](22-core-execution-and-foreign-functions.md#223-foreign-function-imports), and `#Test` to [test definitions](#651-test-definitions). Their concrete argument and target rules are checked after selection; ordinary Name lookup does not supply their literal arguments.

**Mod markers.** A [Mod](20-compilation-configuration.md#207-mods-source-generation) may use Attributes to find targets. A marker neither requests execution nor is consumed: several Mods may inspect the same Attribute, and a later Mod may emit markers for an already completed Mod without restarting it or causing an error merely for that reason.

Marker names, argument syntax and target Koto are exposed before the target Type is fully bound. Queries use environment-selected syntax and exclude discarded declarations. Matching syntax names does not establish semantic identity between unrelated same-spelled Attributes. A Mod's registration or accompanying contract must identify its markers and argument rules. Semantic argument queries obey the [Binding access period](20-compilation-configuration.md#2072-compilation-and-binding); syntax remains readable afterward. Neither discovery nor argument inspection executes the target program or an Attribute constructor.

Marker discovery and validation are distinct: a selected Attribute that remains unrecognized at final validation is diagnosed, and finding its syntax does not make an unknown Attribute valid. Concrete marker registration and recognition APIs, and general Attribute semantics beyond these rules, Layout, LibraryImport and Test, remain design boundaries.

### 6.5.1. Test definitions

`#Test` takes no arguments and marks a test definition; the initial revision assigns one case to each definition. The target must be a safe, ordinary, named function with a body, no parameters (including defaulted ones) and no receiver, and a Unit return Type written `-> ()` or omitted. Neither the function nor an ancestor Container may have unresolved generic or Origin parameters. Source-level functions and receiver-free functions in nongeneric group, rootgroup, struct or enum Containers are eligible; no instance is synthesized for a struct test. Foreign imports, explicit specializations, local functions inside another function and functions requiring captures are ineligible. Duplicate Test Attributes and invalid targets or signatures are errors; registration is never silently skipped.

```kimi
group Arithmetic
    func add(left?: i32, right?: i32) -> i32 => left + right

    #Test
    func addition()
        $expect(add(1, 2) == 3)
```

Ordinary builds check the selected Attribute and declaration syntax and the target conditions decidable from syntax, but they do not resolve test function names, check test bodies, generate their code or require test-only dependencies. Test builds perform full target and body verification. §19.5's exclusions are preserved. Source-level tests keep SourceDocument-local lookup. Product and test membership follows [§18.8](18-modules-and-dependencies.md#188-product-and-test-inputs).

User code may neither call a Test function nor acquire it as a function value; put shared work in ordinary helpers. Only generated test startup may invoke nonpublic tests, and this privilege does not widen a test body's access rights. Discovery and execution follow [§20.9](20-compilation-configuration.md#209-test-command-and-discovery) and [§22.6](22-core-execution-and-foreign-functions.md#226-test-execution-and-reporting).
