# 9. Names, signatures, and access

[Specification index](../SPEC.md)

Name resolution associates each Name with a declaration. A Type Name then goes through Type Name Selection ([§9.7](#97-type-name-selection)) and Type Legality; a call through overload resolution ([§10](10-overload-resolution-and-inference.md#10-overload-resolution-and-inference)) and use-site legality.

**No backtracking.** Once a lookup stage, declaration or overload is selected, a later failure is an error and never selects another. Unknown Names and unresolvable dependency cycles are errors. File, alias and candidate order never change the result.

## 9.1. Signatures

A Signature decides whether declarations may coexist in one scope.

| Declaration | Signature |
| --- | --- |
| Type declaration | Kind, Name and generic parameter count |
| Function | Name, generic parameter count, ordered normalized parameter Types (receiver included) |
| Constructor | Declaring structure, ordered normalized parameter Types |
| Field, computed member, Property Requirement | Name |
| Enum Case | Name within its enum; payloads do not overload |

In one scope, same-name Type-namespace declarations must have the same kind (group, struct, enum or contract); a kind conflict is an error whatever the arities. Same-kind declarations with different generic arities are distinct Types. Same-kind, same-arity group or struct declarations are fragments ([§6.1.2](06-declarations-and-containers.md#612-container-fragments)); other duplicates are errors. A `rootgroup` path segment that names a non-group Type of the same scope is a kind conflict; `rootgroup` never synthesizes a group beside it.

The generic parameter count (**GenericArity**) includes length slots; a pair counts as one. Origin schemas do not overload: `View<T> {source}` and `View<T> {left, right}` cannot coexist.

**Normalization.** Each Name resolves to its Symbol and Kotonoha; transparent aliases and completed associated-Type projections ([§8.6](08-generics-constraints-and-contracts.md#86-conformance)) are expanded; grouping and redundant `owner` prefixes are removed; every Semantics layer is kept. Generic positions become structural:

| Source | Normalized |
| --- | --- |
| Ordinary `T`, or an original `s/T` | `Slot(i)` |
| Pair binder `s` / pair target `T` | `OuterSemantics(Slot(i))` / `DirectTarget(Slot(i))` |
| `s` applied to another slot `U` | `Apply(OuterSemantics(Slot(i)), Slot(j))` |
| Length `N` / fixed array | `LengthSlot(i)` / `FixedArray(Length, Element)`, lengths as in [§4.4](04-arrays-indexing-and-slices.md#44-function-length-parameters) |
| `Self` | Struct or enum: `S<Slot(0), …>`; Contract: the conforming-Type binder `SelfSlot` |

Comparison is structural alpha-equivalence, never simplified by instantiation or Constraint proofs. Slot kind does not distinguish Signatures: `f<T>(value: T)` and `f<s/U>(value: s/U)` conflict. `ref/T` and `uniq/T` stay distinct, though [§7.3](07-functions-and-callable-values.md#73-receivers) forbids two receiver shapes under one Name. A receiver is compared like any other parameter; its name does not matter.

These never distinguish Signatures: Origins and their relations, return Types, parameter names, defaults, the argument-name contract ([§7.2.2](07-functions-and-callable-values.md#722-name-contract-and-defaults)), access, `unsafe`, Constraints and task slots ([§24.2.3](24-suspension-and-asynchronous-tasks.md#2423-not-a-parameter-one-shape-per-function-group)). Duplicate Signatures are errors; distinct ones may still conflict under the parameter acquisition shape ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)). Equal Signatures from different Containers make a use ambiguous unless overload resolution selects one.

```kimi
struct Box
struct Box<T>                  // Valid: same kind, different arity.
// group Box                   // Error: kind conflict with struct Box.
struct A<T>
// rootgroup A.B               // Error: A is a struct in this scope.

struct Reader
    func read(self: ref/Self, at: i32) -> i32 => 0
    // func read(r: ref/Reader, i: i32) -> i32 => 0 // Error: same Signature.

func identity<T>(value: T) -> T => value@move
// func identity<U>(value: U) -> U => value@move     // Error: same Signature.
```

## 9.2. Namespaces and lookup roles

A **Lookup Role**, chosen by the syntactic use, filters the candidates of a namespace.

| Namespace | Declarations |
| --- | --- |
| Type | Containers, Kotonoha reference names, named aliases, Type parameters, pair binders, associated Types, `Self`, built-in Semantics and categories |
| Value | Functions, Fields, computed members, enum Cases, parameters, locals, length parameters |
| Origin / Label | Origins / Labels |

| Lookup Role | Namespace | Eligible declarations |
| --- | --- | --- |
| Core | Type | Structures, enums, associated Types, `Self`, Type parameters and pair targets |
| Object View Target | Type | Cores and runtime Contracts |
| Type Semantics | Type | Pair binders and language-defined Semantics |
| Qualifier | Type | Groups, Kotonoha references and Types that can qualify members |
| Requirement | Type | What requirement syntax admits |
| Declaration Container | Type | Groups, structs, enums, contracts and Kotonoha references |
| Value | Value | All value declarations, callable or not |
| Enum Case | Value | Cases of the enum fixed by a qualifier or the expected Type; never an unqualified Name ([§9.5](#95-unqualified-lookup)) |

Role filtering looks only at the declaration kind and the syntactic use, never at arity, arguments, expected results, Constraints or later members; there is no callable-only role. Object-target syntax selects the Object View Target role. After selection, Type Legality checks that the binding may be used in that role under its declared Constraints ([§8.1.1](08-generics-constraints-and-contracts.md#811-type-slots-and-pair-parameters)); a failure there, or a later ObjectViewCompatible failure, is an error and never resumes lookup outward.

Type and Value names may coexist. In one namespace and scope a Name repeats only by [§9.1](#91-signatures). Origin and Label lookup never falls back to Type or Value names.

```kimi
struct T

func f<s/T>(x: s/T, y: T) -> () => ()
// Error: T here is the pair target, not struct T, and is not proven a value Type.
```

## 9.3. Local scopes and contextual names

A local is visible after its declaration; its initializer sees the outer scope. An inner block may shadow an outer local; one block cannot declare a Name twice, and parameters share the immediate body's space. Local functions are visible from block entry, may overload, and cannot collide with a local or parameter. Container members may be used before their declaration, never to read an uninitialized value.

A nested function may use outer Type parameters, but a named nested function captures no runtime local; only anonymous functions capture ([§7.7.2](07-functions-and-callable-values.md#772-captures)). Naming an outer Origin in a signature is not a capture ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)). The SourceDocument execution scope is defined in [§6.1.1](06-declarations-and-containers.md#611-source-root-and-nested-containers); Pattern scopes in [§14.8.2](14-control-flow.md#1482-binding-scopes).

`Self` is reserved for Type and Contract contexts. `self`, `value` and, inside a stored accessor, `storage` ([§11.3](11-properties.md#113-accessor-functions)) are contextual bindings: they cannot be redeclared while active, are never captured implicitly, and are not active inside a local named function ([§7.3](07-functions-and-callable-values.md#73-receivers)). Elsewhere `storage` is an ordinary Name.

```kimi
func example(input: i32) -> i32
    let x = input
    if true
        let x = x + 1     // Valid: the initializer uses the outer x.
    // let input = 0      // Error: a parameter and a local share one space.
    return x
```

## 9.4. Access

A use needs access, a path through scopes, qualification or aliases (**Name Reachability**) and a compatible role. Aliases never widen access.

### 9.4.1. Access levels and domains

A declaration has one access specification (syntax: `Access`, [§F.6.1](appendices/F-syntax-summary.md#f61-source-units-containers-and-attributes); order with modifiers: [§F.3.3](appendices/F-syntax-summary.md#f33-declaration-modifiers)). Protected forms apply only to structure members (nested Containers and accessors included).

An access domain is a set expression over these atoms:

- `Body(C)`: the lexical body of Container `C` with all its fragments and nested bodies;
- `Kotonoha(K)`: all source of Kotonoha `K` (identity: [§18.4.1](18-modules-and-dependencies.md#1841-identities-and-reference-names));
- `Derived(S)`: the bodies of every structure, present or future, deriving directly or indirectly from any construction of `S`; empty when `S` is not `open`;
- `All`.

| Access of a member of Container `C` in Kotonoha `K` | Domain |
| --- | --- |
| `private` (default) | `Body(C)` |
| `internal` | `Kotonoha(K)` |
| `protected` | `Body(C) ∪ Derived(C)` |
| `protected internal` | `Kotonoha(K) ∪ Body(C) ∪ Derived(C)` |
| `private protected` | `Kotonoha(K) ∩ (Body(C) ∪ Derived(C))` |
| `public` | `All` |

The effective domain `Access(D)` intersects this with the domain of every enclosing Container; the domain of a private root declaration is `Kotonoha(K)`. A constructed generic Type's domain intersects its declaration's domain with those of its concrete arguments. There is no package or friend access.

Private access follows the merged Container, not the file; a parent has no access to a child's private members. Accessor access follows [§11.3.2](11-properties.md#1132-accessor-accessibility). Enum Cases and Contract requirements take their Container's domain and admit no modifier. Once a member is selected, an inaccessible required accessor or a missing receiver is an error.

### 9.4.2. Domain inclusion

`A ⊆ B` is decided on atoms only. Write `A` as a union of intersections and `B` as an intersection of unions. `A ⊆ B` holds when every intersection of `A`, for every union `U` of `B`, either contains atoms of two different Kotonoha (it is empty) or contains an atom included in `U`. An atom is included in `U` when it is included in some atom of `U`, or when it is `Derived(S)` and `Access(S) ⊆ U`.

Atom inclusion: every atom is included in itself and in `All`; `Derived(S)` of a non-`open` `S` is included in every atom; `Body(C) ⊆ Body(E)` when `E` is or encloses `C`; `Body(C) ⊆ Kotonoha(K)` when `C` belongs to `K`; `Body(C) ⊆ Derived(S)` when `C` is or is nested in a structure deriving from `S`; `Derived(S) ⊆ Derived(S')` when `S` is or derives from `S'`. No other inclusion holds. `Derived` follows the declared `open` modifiers and base clauses, never the structures of one build.

```kimi
internal struct Secret

public open struct Base
    protected func peek(value: Secret) -> () => ()
    // Error: Derived(Base) is not included in Kotonoha(K).
```

### 9.4.3. Protected receivers

From a derived structure `D`, protected access to an instance member of base `B` needs a receiver whose static Effective Core is `D` or derives from `D` (a generic receiver may use a proven base Constraint); a `B` or sibling receiver is not enough. Accessors are checked separately. The restriction does not apply inside `B`'s body, to same-Kotonoha `protected internal` access, or to non-instance declarations: Type functions, constructors and nested Containers. Inheritance never grants private access.

Where the restriction applies, a protected instance function is used only as `receiver.f(...)`; a Type-qualified call (`Left.f(x)`) or Function Item (`Left.f`) of it is an error. A derived body may call an accessible protected base constructor in its base clause or as `Base.init(...)`.

```kimi
open struct Base
    protected init() => ()
    protected virtual func f(self: objref/Self) -> i32 => 0

struct Left : Base
    init() : base() => ()
    func make() -> Base => Base.init()   // Valid: constructors have no receiver.
    func probe(self: objref/Self, other: objref/Base) -> i32
        // let g = Left.f                // Error: protected Function Item outside Base.
        // other.f()                     // Error: the receiver must be Left or derived.
        return self.f()

func outside() -> Base => Base.init()    // Error: Base.init is protected.
```

### 9.4.4. API signature accessibility

The **API signature** of a declaration is every Type and requirement it exposes. For each declaration `D` and each concrete Type or requirement `T` in it, `Access(D) ⊆ Access(T)` must hold ([§9.4.2](#942-domain-inclusion)), whether or not `D` is used. A direct base obeys this with the derived Type as `D`.

It covers parameters, receivers and results; enum payloads (enum's domain); Field and Property Types (Property's domain); each accessor's own components (its domain; a restricted setter does not narrow the Property); and requirements, including Constraints written in a body. Inferred Types, such as an unannotated Field's, are checked like written ones. It is recursive after alias expansion; Semantics cannot hide an inaccessible Core. For an associated projection the qualifier, the requirement and any exposed binding are checked. Generic parameters are symbolic: their Constraints are checked, and a public generic declaration accepts private Type arguments.

```kimi
public group Api
    private struct Hidden
    public struct Box<T>

    public func identity<T>(value: T) -> T => value@move
    public func expose(value: Box<Hidden>) -> () => () // Error: Box<Hidden> is private to Api.
    internal func leak(value: Hidden) -> () => ()      // Error: wider than Hidden.
    private func keep(value: Hidden) -> Hidden
        return identity<Hidden>(value@move)            // Valid.
```

### 9.4.5. Generic bodies

A generic body resolves Names and checks access in its definition-site context and source environment ([§18](18-modules-and-dependencies.md#18-modules-and-dependencies)) wherever it is instantiated; caller aliases never add candidates. It may use private Types and helpers its API signature does not expose, and instantiation elsewhere grants the caller no access. Type arguments and Constraints are checked at the use.

### 9.4.6. Conformance accessibility

For Type `T`, bound Contract reference `C` and each required implementation `M`:

```text
Access(Conformance(T, C)) = Access(T) ∩ Access(C)
Access(Conformance(T, C)) ⊆ Access(M)
```

Each ancestor conformance is checked on its own. The implementation is selected first and its access checked afterward, without retrying selection; conformance never shrinks to fit a private member.

```kimi
public contract Readable
    property value: i32 has get

public struct Example
    Self is Readable
    private var stored: i32 = 0
    public computed value: i32
        get(self: ref/Self) -> i32 => self.stored        // Required: must be public.
        private set(self: uniq/Self, value: i32) -> () => self.stored = value
```

## 9.5. Unqualified lookup

For the required namespace and role, lookup searches these stages in order:

1. The local scope, then each enclosing lexical and parameter scope. For a root function declaration these include its document's execution scope; Container bodies are not enclosed by it ([§6.1.1](06-declarations-and-containers.md#611-source-root-and-nested-containers)).
2. The current Container.
3. Each parent Container, up to the project root of the use's Kotonoha.
4. For Type qualifiers only: the reference names in the Compilation root of the use's Kotonoha ([§18](18-modules-and-dependencies.md#18-modules-and-dependencies)).
5. The explicit aliases of the use's SourceDocument, together.
6. The default aliases of the defining Kotonoha, together ([§18.2.3](18-modules-and-dependencies.md#1823-default-aliases)).

Type parameters and contextual names are found at their declaring positions. A bare Adaptation Target in `E@X` uses the roles of [§13.5](13-operators-and-assignment.md#135-explicit-operations).

At each stage, same-name declarations are collected, paths to one Symbol deduplicated, and candidates filtered by role and access. Lookup stops at the first stage with eligible declarations. Same-name functions form one **function group**; a function and another value kind at one stage conflict. After the stop, wrong Type arguments, labels, Constraints, argument Types or a non-callable value are errors; lookup never searches farther. A nearer group `X` does not stop Core lookup of `X` but does stop `X.member`, even without `member`.

**No implicit receiver or inheritance.** An unqualified Name never selects an enum Case, a member that needs a receiver, or an inherited declaration. If the first stage with eligible declarations has only such Cases and members, lookup stops with an error: qualification required. When stage 2 or 3 searches a struct with no own eligible declaration, it examines the base layers in order (conditional members included, [§6.2.2](06-declarations-and-containers.md#622-inheritance-and-open-structures)) and stops with the same error at the first layer with an accessible, role-compatible declaration of the Name. The examination needs no base arguments, ignores the protected receiver restriction and skips a base's generic parameters and `Self`. A struct's own base clause resolves without it; a cycle it still forms, as in `struct S : S.N`, is an unresolvable dependency cycle. This rule does not apply to Origin or Label lookup ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)).

```kimi
group Outer
    func marker() -> i32 => 1

    open struct Base
        protected var count: i32 = 0
        public func marker() -> i32 => 25

    struct Derived : Base
        func total(self) -> i32 => self.count + Self.marker() // Valid: qualified.
        func root() -> i32 => ::Outer.marker()                // Valid: Outer.marker.
        func first() -> i32 => marker()          // Error: stops at Base.marker.
        func second(self) -> i32 => count        // Error: write self.count.
```

### 9.5.1. Aliases

Named aliases join stage 5 in the Type namespace with their target's Qualifier role; they are not Cores. A selected alias lacking a later member never falls back. In one SourceDocument, two named aliases with one name must denote the same reference (normalized arguments included), even if unused.

Members opened by opening aliases ([§18.2.1](18-modules-and-dependencies.md#1821-forms-and-targets)) are checked for collisions at use. A function group gathered there from several sources must satisfy [§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape) and have one task-slot shape ([§24.2.3](24-suspension-and-asynchronous-tasks.md#2423-not-a-parameter-one-shape-per-function-group)); otherwise every use is an error, and `A.f` selects one source.

## 9.6. Qualified and inherited lookup

The first component of `A.B.C` is found by unqualified lookup; later components search only the selected target's members. Intermediate Type-side components are Qualifiers; the final role follows the syntax. Associated-Type projections follow [§8.5](08-generics-constraints-and-contracts.md#85-associated-types) and [§9.7.3](#973-nested-types-and-associated-projections).

Where Type and Value qualification are both possible, each namespace commits its own first stage and follows its complete path (roles, access, static or instance use); a stop under [§9.5](#95-unqualified-lookup) fails the path. The sole successful path is selected; several are ambiguous, and arguments or expected Types never decide. Paths to one Symbol count once; value accesses through different receivers are distinct paths.

`::A.B` starts at the Compilation root of the use's Kotonoha ([§18](18-modules-and-dependencies.md#18-modules-and-dependencies)), ignoring locals and aliases. Root-name collisions with reference names are governed by [§18.4.1](18-modules-and-dependencies.md#1841-identities-and-reference-names).

**Inherited lookup.** Member lookup on a struct searches it, then its bases one layer at a time with base arguments substituted, and stops at the first layer with accessible, role-compatible declarations; its same-name functions are the whole function group. Receiver, applicability, accessor and Loan checks come later and never reopen lookup. An override adds no lookup layer ([§6.4.2](06-declarations-and-containers.md#642-override-target-selection)); a derived `f` beside an accessible base `f` is governed by [§6.2.2](06-declarations-and-containers.md#622-inheritance-and-open-structures). A new explicit Contract implementation is selected this way, then checked ([§8.6](08-generics-constraints-and-contracts.md#86-conformance)).

**Groups gathered from Constraints.** Member lookup on a generic parameter gathers same-name requirements from its Constraints ([§8.6.4](08-generics-constraints-and-contracts.md#864-calls-through-requirements)); the gathered group is checked at the use ([§7.3.2](07-functions-and-callable-values.md#732-receiver-shape), [§7.4.4](07-functions-and-callable-values.md#744-declaration-and-call-contracts), [§24.2.3](24-suspension-and-asynchronous-tasks.md#2423-not-a-parameter-one-shape-per-function-group)).

```kimi
group Config
    public var count: i32 = 3

struct Settings
    public var count: i32 = 0

func read(Config: ref/Settings) -> i32
    return Config.count                  // Error: both the Type and the Value path succeed.
```

### 9.6.1. Base subobject receiver projection

For `receiver.member`, when lookup selects an instance declaration of a base `B` of the receiver's static Effective Core `D`, the receiver is projected to its inline `B` subobject with base arguments substituted. Access is checked against the original receiver first. Type functions need no projection; Type-qualified calls and function values follow the ordinary argument rules after [§9.4.3](#943-protected-receivers).

For a declared `ref/B` or `uniq/B`, projection forms the shared or exclusive Borrow or Reborrow of the subobject with the original permission. It is part of implicit receiver acquisition ([§7.3](07-functions-and-callable-values.md#73-receivers)): a bare owned Place of Type `D` is projected exclusively when its lending point is exclusively writable ([§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition)). The receiver is evaluated once, before the arguments. A method or custom accessor borrows the whole subobject; standard Property access projects to the storage Place ([§11.2.2](11-properties.md#1122-field-paths-and-partial-moves)) and keeps the original receiver's owned, borrowed or object classification.

Projection is no conversion and plays no part in Best Candidate comparison ([§10.4](10-overload-resolution-and-inference.md#104-best-candidate)). A borrowed-receiver implementation used through it requires ObjectCallCompatible Proven ([§12.7](12-expressions.md#127-object-receiver-compatibility)), checked after overload selection; NotProven is an error and never excludes a candidate. Projection never permits whole-base Move, Replacement or a sliced owner; receiver-derived results keep its Loan. On objects it keeps identity and Dynamic Type, creates no handle or count change, and needs Owned erasure evidence ([§6.4.5](06-declarations-and-containers.md#645-receiver-safety)). It never satisfies `owner/B`, `obj/B`, `rc/B` or `arc/B`.

## 9.7. Type name selection

After lookup stops, Type candidates are filtered by the number and kinds of explicit Type arguments, which resolve in the use-site context. Exactly one must remain; none is a mismatch, several are ambiguous. Constraints are checked afterward, never trying another Type.

A qualifier whose own Type-argument list is omitted before `.init` or a Case reference (expression or Pattern) selects zero-own-arity references if any exist, otherwise the single generic reference; several are ambiguous. Arguments and the expected Type never decide. Case argument inference is in [§6.5.2](06-declarations-and-containers.md#652-case-references).

```kimi
struct Box<T>
struct Box<T, U>

let a = Box<i32>.init()               // Valid: selects Box<T>.
// let b = Box.init()                 // Error: two generic references.
let c = Option.Some(1)                // Valid: Option<i32>.
```

### 9.7.1. Bound container paths

Container references resolve in three steps: the path (role, access, each segment's arity); the Type and Origin arguments with lexical or base substitution; validation of every qualifier and the target. A later failure never reopens a layer.

Outer Type arguments are omitted only where the environment supplies them: inside `Outer<T>`, `Inner<i32>` means `Outer<T>.Inner<i32>`. They are never inferred from expected Types or inputs; `Outer<i32>.Inner.init(value)` infers only `Inner`'s own slots.

Inherited Containers keep their defining declaration: with `open struct Base<T>` containing `struct Node` and `Derived : Base<i32>`, `Derived.Node` is `Base<i32>.Node`. A Container conflicts with an accessible ancestor Container of the same Type-namespace Name, whatever its arity.

### 9.7.2. Origins on paths

A nested declaration's Origin schema holds its own and inherited slots, not those of its Type arguments, Fields or bases. A suffix names the complete binding set of that occurrence, completed under [§15.3](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations). Contract references, alias targets and Container qualifiers need a complete Origin contract; relations not supplied by inference or inherited bindings are attached to the enclosing declaration. There is no expression-level constraint block and no implicit `static`.

`(Outer<T>{outer}).Inner<U>{inner}` binds the Origins of an intermediate qualifier without creating values; intermediate obligations are kept. Binding-set labels are lexically scoped and are not Type arguments.

### 9.7.3. Nested Types and associated projections

Type parameters conflict with nested declarations of the same namespace; shadowing an ancestor name never removes inherited slots. Origin lookup combines scalar names, binding-set names and value carriers by nearest scope ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)), never falling back past a wrong-role candidate.

A nested Type is not an associated Type. `T.(C).Element` selects Contract `C`; `T.C.Element` is a nested path. `T.Element` that resolves both ways is ambiguous. Associated candidates are deduplicated only by defining declaration and bound Contract reference.
