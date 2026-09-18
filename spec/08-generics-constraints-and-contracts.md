# 8. Generics, constraints, and contracts

[Specification index](../SPEC.md)

Generic parameters describe the admitted Types and capabilities. Constraints establish the proofs available to a generic body, and Contracts name capabilities and map their requirements to implementations. Explicit specialization changes implementation selection, not the original callable contract.

## 8.1. Generic Type parameters

This section defines Type slots. Function length slots use the explicit `<length N>` form of [length parameters](04-arrays-indexing-and-slices.md#44-function-length-parameters); a plain `<N>` remains a Type slot. A length slot accepts no complete Type and is not decomposed as a pair.

### 8.1.1. Slots and projections

Both parameter forms consume **one complete Type argument**. Binding preserves the Semantics, nested Types and all Origins:

```text
<T>    : T = W
<s/T>  : WholeType = W
         s = OuterSemantics(W)
         T = DirectTarget(W)
         o = OuterOrigin(W)       // Present only for an outer safe borrow.
```

`WholeType`, the projection functions and `o` are explanatory notation, not source bindings. `<s/T>` declares only `s` and `T`; the original `s/T` keeps `W`'s Origins without naming them. Source Origin names use the separate [Origin parameter schema](15-ownership-and-lifetime-analysis.md#153-abstract-origins).

An ordinary `T` denotes a complete value Type, not only a bare Core. The pair's `T` has the fixed internal kind **SemanticsTarget**, whose value is a complete value Type or an Object View Target. Using it as a standalone value Type in a generic body requires proof of that role under the declared Constraints at definition checking; only the remaining proven symbolic substitution may be a [deferred obligation](#810-generic-body-checking-and-deferred-obligations). Its kind does not change at instantiation.

Before projection, transparent aliases, resolved associated-Type projections, grouping and redundant `owner/` are normalized. Alias cycles are rejected, and nominal declaration identity and parameter Binding Identities are kept; no nominal-alias syntax is introduced. Only the outer layer is split: `ref/(uniq/i32 from b) from a` yields `s = ref`, `T = uniq/i32 from b` and outer Origin `a`. Since `owner/V` preserves `V`, `owner/(ref/i32 from a)` has outer Semantics `ref`.

| Outer Semantics | Direct target, and requirements for applying these Semantics to another `U` | Outer Origin |
| --- | --- | --- |
| `owner` | `DirectTarget(W)` is `W` itself; `owner/U` is `U` for any valid complete value Type | None |
| `ref`, `uniq` | Complete Referent Type; `uniq` also requires exclusive acquisition and Loans at use | Required |
| `obj`, `rc`, `arc` | Supported Core or valid runtime Contract View Target | None |
| `objref`, `objuniq` | Supported Core or valid runtime Contract View Target; borrowing requirements are preserved | Required |
| `unsafe` | Complete pointee Type; adds no safe-borrow lifetime guarantee | None |

These rows do not extend the current runtime-Contract or callable restrictions. The absence of an outer Origin does not erase payload dependencies: in `ref/(View<i32> from (source => a)) from b`, `a` belongs to the inner Type and `b` to the outer borrow.

A Proven `T is Sealed` establishes a valid owner Core and a supported object View Target (§8.4.7.1). This evidence is available during generic signature formation; it does not flatten nested Type or Origin layers.

Within one parameter list, every bound name must be distinct: `<T, T>`, `<s/T, s/U>` and `<s/s>` are errors. References use Binding Identity under the ordinary scope rules. `<s>` is an ordinary Type slot; standalone Semantics slots are not introduced. Built-in forms such as `Callable<ref, S>` have their own grammar; they are not general Semantics arguments.

### 8.1.2. Reconstruction and Origin annotations

After transparent alias expansion, the original pair expression `s/T` refers to its WholeType `W`. This correspondence is determined by the declared bindings, not by two targets becoming equal after instantiation. `s/U` with another binding applies only the Semantics kind to `U` and does not copy `W`'s outer Origin. Once formed, equal complete Types acquire no identity differences from their construction history.

An explicit Origin annotation follows the ordinary Type-formation rules. Otherwise the original `s/T` keeps `W`'s Origins, and another `s/U` uses the [position-specific Origin rules](15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts). Storing a Type in a pair slot creates no additional omission permission.

For `W = ref/i32 from a`, `s/T from b` forms `ref/i32 from b`. Formation requires no relationship between `W`'s old outer Origin `a` and the new `b`, and performs no value conversion, but it still validates `b`'s binding, its annotation position, and the formed Type's own inner-Origin outlives constraints. Fitting an actual `from a` value to this Type separately checks the permitted shortening (`a : b`), acquisition and Loans. In particular, `uniq/V` keeps `V` invariant and any required Move or Reborrow; an annotation neither creates nor removes a Reborrow.

### 8.1.3. Argument validity

**WellFormedGenericTypeArgument(A)** requires a valid resolved complete Type, or a legitimately dependent Type with retained obligations. Access, Semantics application, generic and associated-Type arguments, Origin mappings and Constraints are checked before any Origin-erased comparison key is used.

| Argument | Rule |
| --- | --- |
| Primitive, struct, enum, Unit, Tuple | Allowed under ordinary Type formation |
| Common Function Type | Allowed under its existing signature, environment and Origin restrictions |
| Function Item or Closure Type | Allowed through inference or an existing reference form; no new anonymous-Type spelling |
| Safe borrow, object Type, raw pointer, Origin-bearing aggregate | Allowed as a Type argument; acquisition, storage, erasure and Unsafe checks remain separate |
| Never | Allowed as a semantic Type argument, without adding a source name or value; a Never expression alone cannot infer an arbitrary unbound Type |
| Dependent Type | Definition-side bindings and resolvable obligations are kept until their deadlines |
| Unsized or unspecified representation | No such Type is introduced; a valid Type must also meet the layout requirements of each storage use |
| Bare Contract, group, bare Semantics, general value argument | Not a complete value Type argument; a Contract can appear only in an already permitted Object View Type |

An explicit argument list supplies every slot in declaration order, with an optional trailing comma. Omitting the entire list uses only the inference supported by that construct. Partial lists, `_` placeholders, defaults, variadic slots and general Const generics are not introduced; only [function length slots](04-arrays-indexing-and-slices.md#44-function-length-parameters) admit length arguments. `<s/T, U>` takes two arguments, such as `<ref/i32 from a, string>`; `<ref, i32>` cannot supply one pair. Origin parameters have a separate schema and consume no Type argument slots.

A valid Type argument is not permission to use it in every role: constraints on base Types, associated Types, finite layout, Copy derivation and object payload erasure remain. Ordinary storage preserves complete Type-argument dependencies under the [storage contracts](15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts), without an Owned or Storable requirement. Static storage instead requires Owned and the [static-source rules](11-properties.md#1132-static-storage).

## 8.2. Constraints

| Term | Meaning | Example |
| --- | --- | --- |
| **Contract** | A Declaration Container declared with `contract` that specifies a named capability a Type provides. | `contract Comparable` |
| **Constraint** | A condition imposed on a Type or Type Semantics. | `T is Comparable` |
| **Constraints** | The set of conditions required for a declaration to be valid or usable. | The leading clauses of a generic function or structure. |
| **Conformance** | A Type's fulfillment of a Contract, with the required correspondence between requirements and implementations. | A Type fulfills `Comparable`. |

A **Constraint Clause** expresses a Constraint in the form `subject is requirement`. All clauses of a declaration's Constraints must hold, and they establish capabilities that the implementation may use. Subjects include complete Types, Semantics, valid target projections and `Self` (the enclosing Type), according to each requirement's role; each declaration kind restricts the permitted subjects (see [function Constraints](07-functions-and-callable-values.md#74-function-constraints)).

A Type requirement may name a capability declared with `contract` or another compile-time Type capability. A Semantics requirement may name a concrete Semantics, such as `ref` or `obj`, or a Semantics category. Requirements combine with `and`, `or`, `not` and parentheses under the [requirement-expression rules](#83-requirement-expressions).

A `struct` header may contain generic parameters and an Origin list. Its ordinary Constraints precede the members, and conditional conformances may appear at member positions (§8.4.8).

```kimi
struct Container<s/T> origin owner, source
    T is Comparable
    s is reference

    var value: s/T
```

These clauses constrain the pair's target projection `T` and Semantics projection `s`; each requirement must accept its subject's role. Constraints may also apply to the enclosing Type:

```kimi
struct ComparableContainer<T>
    T is Comparable
    Self is Comparable

    var value: T
```

`T is Comparable` supplies comparison capabilities for the stored value's Type. In a Type declaration, `Self is Comparable` is both a Constraint and a [conformance declaration](#844-conformance): it requires `ComparableContainer<T>` itself to fulfill `Comparable` and derives no implementation from the clause on `T`. (The required members are omitted here.) The built-in `Self is Copy` keeps its specific [derivation rules](03-types-and-values.md#351-copy-capability-and-explicit-duplication).

## 8.3. Requirement expressions

`subject is requirement` tests a Type or Type Semantics in Constraint Clauses and in associated-Type declarations and specifications. Its result is a compile-time `bool`; an unresolved requirement never becomes a runtime test. Each context keeps its permitted subjects, grammar and validation rules. Requirement Tests are not allowed in `#if` or `#case` Conditions. Ordinary expressions use [runtime type tests](13-operators-and-assignment.md#1361-runtime-is-tests) instead; the syntax context, including through parentheses, determines the interpretation before lookup, with no fallback between the Type and Value namespaces.

`is` binds on its left at comparison precedence, and its right side consumes a requirement expression through `or` precedence. An immediately following `not` negates the entire right side:

| Form | Meaning |
| --- | --- |
| `T is A and B` | `T` satisfies both `A` and `B`. |
| `T is A or B` | `T` satisfies `A` or `B`. |
| `T is not A or B` | `T` does not satisfy `(A or B)`. |
| `T is A and not B` | `T` satisfies `A` and does not satisfy `B`. |

Use `T is (not A) or B` to limit the negation to `A`. A complete Requirement Test cannot be embedded in an ordinary Boolean expression such as `(T is A) and enabled`; no source syntax provides such a mixed context. Use separate Constraint Clauses for requirements, and environment directives for independently configured source selection. Value equality uses `==`.

## 8.4. Static contracts

A **Contract** is a Declaration Container that defines a named capability through function requirements, Property requirements, associated Types and Constraints. It has no executable implementations or storage. `Self` in a requirement denotes the conforming concrete Type.

```kimi
contract Source
    associate Element
    func read(self: ref/Self) -> Element

contract Sized
    property count: i32 has get

contract SizedSource: Source, Sized
    func reset(self: uniq/Self) -> ()
```

This revision defines static conformance checking and generic use. User-defined Contracts declare no generic or Origin parameters of their own; they inherit the enclosing environment under §6.1.3, including unused Type/Semantics and Origin bindings. Ordinary generic Types and functions, and separately specified built-in requirements such as `Callable<...>`, are unaffected. Runtime Contract Views remain a [future extension](#85-runtime-contracts).

### 8.4.1. Function requirements

A function requirement declares a Name, optional function generic and Origin parameters, explicitly typed parameters and an optional result Type, whose omission means Unit. A requirement with a receiver is an instance function; one without a receiver is a Type function. Receiver, parameter labels, ownership, Origins and safe/unsafe conditions follow the ordinary function rules.

Function-specific Constraints occupy an optional indented region immediately after the header. The region contains one or more Constraint Clauses, with no executable statements, `return`, local declarations or single-item body. Its subjects are the function's generic parameters or associated-Type projections rooted in them. Contract-wide Constraints belong at the Contract body level.

```kimi
contract Factory
    func create<T>(value: T) -> Self
        T is Copy
```

Requirement Constraints are premises for checking implementation compatibility; they are not silently added to the implementation declaration. Requirements have no default or optional parameters, so calls through a requirement supply every ordinary argument, and defaults on an implementation do not permit omission through the Contract. Requirements and required accessors have no independent [access modifiers](09-names-signatures-and-access.md#934-conformance-accessibility).

Property requirements use `has`; their selection and compatibility rules are in [Contract Property requirements](11-properties.md#114-contract-property-requirements).

### 8.4.2. Refinement

`contract C: A, B` refines every listed parent Contract. Parents resolve as bound Contract references (§9.6.1), which permit arguments on outer path segments but not on the Contract itself. All requirements, associated Types and Constraints are inherited. Parent order gives no priority, and direct or indirect cycles are errors. `Self is C` does not declare refinement inside a Contract.

```text
Source                 Sized
  Element, read()        count
       \                 /
             SizedSource
               reset()
```

Conformance to a child entails conformance to every ancestor, and inherited `Self` still denotes the final conforming Type. A child may add requirements or Constraints but cannot remove or weaken inherited ones.

Paths to the same ancestor declaration with the same normalized bindings introduce one Requirement Identity or associated-Type identity. Distinct bindings remain distinct requirements, and all path conditions and validation obligations are preserved (§8.4.9). Independent declarations from different parents remain distinct even when their names match; one compatible implementation may satisfy several requirements.

A refinement is rejected when its requirements cannot coexist as separate implementations under the ordinary declaration rules and are provably impossible to satisfy with one implementation. The check uses both the [implementation-identification key](#845-implementation-matching) and the ordinary [Signature](09-names-signatures-and-access.md#91-signatures) rules: labels affect matching but cannot independently distinguish overloads. Different result Types alone do not prove a conflict; the defined result compatibility, including the subtype and Never rules, applies. Genuinely dependent checks are retained until their prerequisites resolve; unknown is not a proof of contradiction.

### 8.4.3. Associated types

An associated Type is restricted to a Core. Unlike an ordinary generic Type slot, it cannot bind an arbitrary Semantics-applied complete Type.

The only intrinsic exception is the `Element` requirement of `Kimi.Iterable` and `Kimi.Iterator` (§22.1), which binds a complete Type, including Semantics and existing Origins. Its explicit specification uses the same `associate` syntax; this adds no general complete-Type associated declaration facility. For ordinary Core associated requirements, borrow Semantics and operation Origins are specified at use sites. Existing dependencies inside a Type remain subject to ordinary Origin checking, and generic substitutions must prove the restricted role or keep a legitimate obligation.

```kimi
contract BorrowSource
    associate Element
    func read(self: ref/Self) -> ref/Element from self
```

`associate` declares a new associated Type inside a Contract and specifies an existing associated Type inside a conforming Type. Both forms use `is`, never `=`:

| Form | Meaning and location |
| --- | --- |
| `associate Element` | Declares an associated Type in a Contract. |
| `associate Element is Equatable` | Declares it with a capability requirement, or constrains the uniquely identified associated Type in an implementation. |
| `associate Element is i32` | Declares it with a fixed Type, or specifies that Type in an implementation. |
| `associate C.Element is T` | Specifies identity with a generic binding `T`, subject to the associated Type's Core restriction. |

Each associated Type is determined uniquely from explicit specifications and Type-identity Constraints on the Contract or its ancestors. Bindings are never inferred from implementation signatures, member search or function bodies, and a Type is never chosen merely because it satisfies a capability. Bindings are substituted before implementations are matched. An unconstrained `Source.Element` is not inferred as `i32` merely because an implementation of `read` returns `i32`.

**Qualified specifications.** `associate C.Element is T` selects an associated Type through a direct conformance or its ancestor `C`. An unqualified `associate Element is T` is valid only when exactly one distinct associated-Type declaration with that name is available across those conformances and refinements; multiple paths to one declaration count once. Ambiguous names require qualification, and a short form never applies to all same-named declarations. A bare `associate Element` is not an implementation specification.

```kimi
contract Destination
    associate Element

struct Pipe
    Self is Source
    Self is Destination
    associate Source.Element is i32
    associate Destination.Element is string
    public func read(self: ref/Self) -> i32 => 42
```

**Projections.** `T.C.Element` refers to the associated Type of `T`'s conformance to `C`; `T.Element` is the short form when the declaration is unique under the available Constraints. `C` uses ordinary Contract-name and alias lookup, not member lookup on `T`. Conformance evidence is required and is never discovered by searching for a same-named Contract. The Type-side base may be a named or constructed Core, a parameter, `Self` or another associated-Type projection. An intrinsic Element projection that binds a Semantics-applied Type does not become a Core qualifier merely by being an associated projection. A projection is not a value or Semantics-applied expression.

```kimi
func readOne<T>(source: ref/T) -> T.Source.Element
    T is Source
    return source.read()

func readInt<T>(source: ref/T) -> i32
    T is Source
    T.Source.Element is i32
    return source.read()
```

Leading function Constraints are collected before projections in the signature, including its result Type, are resolved; the Constraints themselves are validated and discharged at each use. Type context fixes a projection's namespace, and the dotted syntax is kept until Binding resolves the Contract and associated-Type roles. Distinct successful interpretations are ambiguous; neither expected results nor fallback to value-member lookup resolves that ambiguity.

**Refinement Constraints.** A child may constrain an inherited associated Type through `Self.C.Element`, where `C` is an ancestor. In a Contract-body Constraint subject, a bare associated-Type name is also permitted when it is unique among the own and inherited declarations. These clauses constrain existing declarations; they create no replacement Types or conformances.

```kimi
contract Equatable
    func equals(self: ref/Self, other: ref/Self) -> bool

contract OrderedSource: Source
    Self.Source.Element is Equatable

contract IntSource: Source
    Self.Source.Element is i32
```

An `IntSource` implementation need not repeat the inherited `Source.Element is i32` binding. Explicit Type-identity facts support substitution and normalization, and contradictory bindings are errors. Unresolved bindings remain obligations until the required finalization point. No arbitrary associated-Type inference or proof search is added beyond the [limited proof rules](#87-constraint-proof-system).

### 8.4.4. Conformance

In a Type declaration, `Self is C` is both a Constraint and an explicit declaration of conformance to `C`. Same-named members alone do not register conformance.

```kimi
struct NumberSource
    Self is Source
    associate Source.Element is i32
    public func read(self: ref/Self) -> i32 => 42
```

**Conformance Identity** is the pair of the concrete Type identity and the Contract identity. Different associated-Type bindings do not create different conformances to the same Contract. An explicit conformance and a conformance implied by refinement to the same pair denote one conformance; all simultaneously applicable associated-Type bindings and requirement-to-implementation mappings must agree under the path checks of §8.4.8. A redundant explicit parent conformance is allowed without a required warning.

Unconditional generic Type conformance must hold for every binding allowed by the Type Constraints; conditional conformance uses the additional premises and use checks of §8.4.8. Successful selected instantiations do not validate an unconstrained definition. Ancestor conformances and all effective mappings are verified. Conformance declarations generate no implementations, except explicitly specified intrinsic derivations and the limited standard Property witness bridges of §11.4.2.

The verified requirement-to-Member Identity mapping and the associated-Type bindings are retained. Contract calls use this mapping instead of rediscovering members in the caller's source environment or after instantiation. No external registration, replacement conformance, default implementation or access-bypassing witness thunk is introduced.

**Inherited conformance.** A verified conformance `(B, C)` is inherited by `D : B` only if every requirement, with the requirement-side Self replaced by `D`, is satisfied by the retained mapping. If Member `M` was declared in `A`, its implementation-side Self remains `A`, even through `A -> B -> D`. Type and Origin substitutions along `D`'s base path to `A` are applied, and associated-Type bindings are kept. Only an eligible borrowed receiver may use [base-subobject projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection); other parameters and results gain no base conversion, and owning receivers gain no slicing. The access, Origin, Effect and premise checks of §8.4.5 and the Property rules of §11.4 apply. Projected calls require published ObjectCallCompatible Proven (§12.4.4).

| Requirement shape | Inheritance through this path |
| --- | --- |
| `Self` only in a borrowed receiver, as in Stringify | Possible if all other checks and ObjectCallCompatible succeed |
| `other: ref/Self`, as in Equatable/Comparable | Fails: `ref/A` does not match `ref/D` |
| `func empty() -> Self` | Fails: `A`'s result does not supply `D` |
| `owner/Self`, as in Iterable | Fails: no owning receiver projection |
| A fixed `Self.Element` that normalizes to the same Type | The normalized Types are matched; the spelling `Self` alone does not prohibit inheritance |

One failed inheritance path does not invalidate `D`'s declaration. `(D, C)` is determined from all explicit, conditional and Contract-refinement paths under §8.7; only a proof that all candidates fail gives Refuted. Unresolved generic dependencies remain Unknown until their deadline, and invalid declarations or inconsistent evidence are Error. Successful paths must agree on associated Types and implementation mappings. A new explicit conformance uses ordinary implementation lookup; inherited mappings are not replaced by same-named declarations.

Copy, Owned, Callable and other intrinsic capabilities keep their own derivation rules. In particular, struct Copy requires the derived declaration's opt-in and all stored components; it is not inherited automatically.

No warning is required merely because an open base has a conformance that its descendants cannot inherit. At a failing derived `Self is C` or constraint use, the diagnostic identifies the requirement and the cause: Self mismatch, owning receiver, access, Origin or premise failure, or ObjectCallCompatible NotProven with the responsible implementation. An unresolved proof is diagnosed as Unknown, not Refuted. For example, an Equatable `Shape` may have a valid `Circle : Shape` without `Circle` being Equatable; requiring `Circle`'s conformance reports the `ref/Shape` versus `ref/Circle` mismatch and the inherited-Name prohibition. When descendants need such Self-dependent Contracts, implement them on leaf Types or use composition; the base's own conformance remains valid.

### 8.4.5. Implementation matching

Ordinary declarations are validated first: functions differing only in result Type, Origins, Constraints or `unsafe` cannot coexist in one scope under the Signature rules, and are duplicate declarations before conformance matching.

After substituting `Self`, the conforming Type's arguments and the associated Types, a function implementation is identified by the following key:

| Component | Required match |
| --- | --- |
| Name | Exact name. |
| Function kind | Type function or instance function. |
| Function generic parameters | Same count, kinds and order; they correspond by position, not spelling. |
| Ordinary parameters | Same count, order and external labels; internal names need not match. |
| Receiver | Same presence and normalized Type structure, with only the inherited receiver correspondence allowed by §8.4.4. |
| Parameter Types | Same normalized Type structure. |

Type structure includes resolved Core identity, Semantics, nested structure and Type arguments. Origin names, bindings and lifetime relations are excluded from identification but kept for compatibility. There is no parameter-structure contravariance. Implementations are not ranked by ordinary call overload preferences, adaptations, omitted arguments, Origins, Constraints, conditional-member applicability or Effects; conditions are checked after identification, unlike direct-call applicability (§8.4.8).

These rules identify Contract implementations; `Callable` and common Function Types keep their separate [callable signature compatibility](10-overload-resolution-and-inference.md#107-callable-signature-compatibility) rules.

Zero candidates means a missing implementation; multiple candidates mean ambiguity, even if only one would pass compatibility. For exactly one candidate:

| Aspect | Compatibility obligation |
| --- | --- |
| Constraints | The Type/conformance and requirement premises must prove the implementation's Constraints and any conditional-member premises. |
| Input Origins | Every input allowed by the requirement remains valid; no stronger lifetime precondition. |
| Result Type | The same Type or a subtype permitted by the Type rules. |
| Result Origins | At least the required lifetime guarantees. |
| Access | Usable throughout the [conformance's effective domain](09-names-signatures-and-access.md#934-conformance-accessibility). |
| Calling context and Effects | No stronger calling context or effects than the requirement permits. |

Origin contracts are compared after binder correspondence, using ordinary variance and Loan rules, including invariance where required. A requirement that admits a call-local borrow cannot be implemented by a function requiring that input to be `static`. Origin-free identification neither erases dependencies nor relaxes exclusive access.

The existing Safety, ownership, Origin and Access Effect checks apply; no new effect system is defined here. A safe requirement cannot require an unsafe calling context. Result compatibility inserts no numeric or user conversion, Copy, Borrow/Reborrow or erasure. Core inheritance alone does not prove compatibility of complete Types. On a compatibility failure, the conformance error is reported without searching for another implementation. Properties use the corresponding [accessor rules](11-properties.md#114-contract-property-requirements).

Receiver adaptations in custom accessors and generated Contract witnesses distinguish proven complete Sealed payload calls from protected object/base calls (§12.4.4). Property permissions, right-hand-side-first assignment, witness identity and public premises are preserved; Sealed never changes ObjectViewCompatible.

### 8.4.6. Calls and shared requirements

Under `T is C`, generic code may use the requirements and associated Types of `C` and its ancestors. A Type function is called through the conforming Type, as in `T.empty()`, and needs no instance.

```kimi
contract EmptyConstructible
    func empty() -> Self

func makeEmpty<T>() -> T
    T is EmptyConstructible
    return T.empty()
```

`EmptyConstructible.empty()` is invalid because it identifies no implementation Type, and that Type is never inferred backward from the expected result. A concrete call such as `Buffer.empty()` uses ordinary Type-member lookup; generic requirement calls keep their conformance mapping.

Distinct Requirement Identities may form one call candidate only when their exposed signatures and conditions are equivalent **and** their mappings select the same effective implementation Member Identity for every valid Type substitution allowed by the current Constraints. The comparison covers function generics, parameters and labels, receiver, results, Origins, Constraints and calling conditions, plus the implementation's Type substitutions and receiver correspondence. Equal code, runtime addresses or optimizer sharing supply no proof. The conformance requirements themselves remain distinct. Repeated paths to the same Requirement Identity are already one requirement and need no such proof.

```text
A.reset --+-- equivalent call contract and same mapping proved
B.reset --+                         |
                               one call candidate
```

Only the defined proof rules are used, not enumeration of instantiations or arbitrary theorem proving. If equivalence cannot be proven, the candidates remain separate and ordinary call selection applies; a non-unique result is ambiguous. A coincidental match in one instantiation cannot make an otherwise invalid generic call valid. No qualified-call syntax such as `value@A.reset()` is introduced; `@` keeps its adaptation meaning.

### 8.4.7. Intrinsic contracts and guarantees

Some Contracts are **compiler-intrinsic**, including `Copy`. Each has only the special acquisition, destruction, layout, concurrency or code-generation effects explicitly defined for it. These effects belong to the compiler-recognized Contract identity; a user Contract with the same name or requirements does not gain them, so `Self is MyCopy` does not make a Type Copy. Compiler-derived conformance exists only where individually specified, and `Self is Copy` must pass its ordinary derivation checks.

The [required Kimi declaration table](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) also fixes the identities and signatures of `Stringify`, `Equatable`, `Comparable`, `Iterable` and `Iterator`. Their source conformance follows the ordinary static Contract rules; their only special effects are the explicitly specified interpolation, comparison and iteration mappings.

Conformance proves only statically specified requirements. Documented laws such as the symmetry or transitivity of equality are not enforced by the type system. Conformance does not prove current initialization, absence of conflicting Loans, storage representation or direct Field access; ordinary usage checks and documented unsafe obligations still apply.

#### 8.4.7.1. Sealed

`Kimi.Sealed` is a compiler-intrinsic requirement. A normalized Type satisfies it exactly when its outer Semantics is `owner` and its Core is valid, is not Never, and is not an open struct. A sealed Core admits no derived Types; this covers Scalars, `string`, Unit, enums, Tuples, fixed arrays, collections, Function Items, concrete Closures and common Function Types. Callable and runtime Contract Views are requirements or views, not Cores.

Only the outer Core is tested: a non-open `Cell<T>` is Sealed even when `T` is open or contains borrows. `ref/X`, `uniq/X`, object handles and borrows, and raw pointers do not satisfy Sealed. The guarantee implies neither Copy, Owned nor exclusive access. User conformance, implementations and same-spelled declarations cannot grant it.

Proven Sealed supplies the supported Core / View Target evidence needed to form generic object Types, including `obj/T` and `objuniq/T`. It does not restrict existing object formation for open Cores. Proof uses the ordinary conjunction, disjunction and negation rules of §8.7; Unknown is not false, and `T is not Sealed` proves neither `owner` Semantics nor an open Core.

```kimi
struct Cell<T>
    public var value: T

func readPayload<T>(source: objref/T) -> ref/T from source
    T is Sealed
    return source@ref/T
```

### 8.4.8. Conditional conformance

A generic struct or enum may declare `Self is C when P`. Its ordinary Type Constraints `D` govern every use of the Type, while `P` governs only this conformance to `C` and does not become a Type-formation constraint. Normal Type, storage, Semantics and Origin validity still apply.

```kimi
enum Option<T>
    Self is Copy when T is Copy
    Some(T)
    None
```

`Option<Resource>` remains a usable Type when `Resource` is Non-Copy, and this declaration makes `Option<i32>` Copy. In contrast, a leading `T is Copy` would restrict every use of the Type, and an unconditional `Self is Copy` would promise Copy for every binding admitted by `D`.

#### 8.4.8.1. Conditions and implementation scope

The declaration targets one Contract and appears at a Type member position; ordinary Type Constraint Clauses still precede the members. `when` is contextual only here. Conditions are comma-separated `subject is requirement` clauses, all conjoined. Subjects are the enclosing generic parameters, their Semantics/target projections, and associated-Type projections valid under the existing rules. Requirements must accept their subject's role, and no new parameters are introduced.

Conditions allow positive requirement atoms joined by `and` and parentheses, using the existing Requirement Test interpretations. `or`, `not`, value tests and arbitrary Boolean expressions are rejected, even inside parentheses. This restricted grammar does not change the ordinary PositiveRequirement.

```kimi
Self is C when T is A and B, U is D
// Requires T is A, T is B, and U is D.
```

An optional indented implementation block declares members in the enclosing Type's namespace. It is a condition scope, not a new Type or Value namespace. It allows functions, computed members where the Type kind permits them, and associated-Type specifications; it rejects Fields, enum Cases, constructors, `deinit`, nested Types and nested conformance declarations. Enums still forbid computed members. Conditional conformance never changes storage layout or Case structure.

```kimi
contract Describe
    func describe(self: ref/Self) -> string

struct Box<T>
    var value: T

    Self is Describe when T is Describe
        public func describe(self: ref/Self) -> string
            return self.value.describe()
```

`P` is a published static precondition of each block member and does not leak to other members of the Type. `D` and `P` are collected before dependent signatures, associated-Type specifications and bodies are resolved, and their conditions and noncircular evidence are validated. The block may be omitted only when existing members or an explicitly specified intrinsic derivation satisfy the requirements; ordinary Contract implementations are never generated.

#### 8.4.8.2. Verification and use

At definition, `C` is verified under `D` and `P` for every admitted binding. Implementations are identified by §8.4.5 (Property implementations by §11.4), not by ordinary call overload ranking. Each requirement's premises are added when proving the selected implementation's published conditions, access, Origins, ownership and Effects. A compatibility failure never retries a different implementation. Requirement-to-Member mappings, Field bridges where applicable, and associated-Type bindings are fixed and retained at this stage.

At a use, Type arguments are substituted; for a Type satisfying `D`, a proof of `P` enables the verified conformance. Implementations are never selected again using caller lookup or favorable instantiations.

Direct member use has a distinct applicability step: after lookup commits a function group and Type inference and substitution complete, the member's `P` is proven before Best Candidate comparison. The same condition applies to function references and computed access. Associated-Type projections require evidence of the relevant conformance.

| Judgment of `P` | Member applicability | Conformance use |
| --- | --- | --- |
| Proven | Condition satisfied; other applicability rules are checked | This conformance path is available |
| Refuted | Inapplicable | This path supplies no conformance; the Type remains usable |
| Unknown | Applicability unproven | Evidence for neither conformance nor absence |
| Error | Diagnosed even if another candidate succeeds | Invalid declaration or condition is diagnosed |

Conditions never alter lookup's committed layer or the inherited-Name prohibition, and an inapplicable member cannot reopen outer or base lookup. Applicable members of the committed group are compared by the ordinary overload rules, without ranking condition strength. A Loan or initialization failure after selection does not cause reselection. A generic definition cannot restore a member rejected for lack of proof merely because a later instantiation satisfies `P`. If a legitimate temporary Unknown can affect selection, the decision is deferred rather than committing an alternative.

Unknown dependencies and deadlines follow §8.7 and §8.10; required proof missing at its deadline is an error. Neither the declaration itself nor a circular conformance search supplies evidence. Concrete absence requires completing all relevant paths, including ancestor conformances. Conditional syntax and definitions are checked even if no current Type arguments satisfy `P`; environment-only `#if`/`#switch` cannot replace these checks.

#### 8.4.8.3. Uniqueness and parent contracts

At most one direct conformance declaration is allowed per generic Type and Contract, counting unconditional and conditional declarations together after fragment merging. Multiple declarations are not permitted by proving their conditions disjoint, choosing priorities or specializing implementations.

```kimi
Self is C when T is A
Self is C when T is B // Error: duplicate direct conformance.
```

Block members obey the ordinary duplicate rules: equal Signatures that differ only in their conditions are duplicates, while distinct Signatures may overload under the applicability rules above.

Conformance Identity remains (concrete Type identity, Contract identity). A direct declaration and Contract refinement may provide several evidence paths to the same conformance. At definition, every pair of paths is checked under `D` and both paths' conditions: equal implementation mappings and associated-Type bindings must be proven unless §8.7 can establish that the paths cannot hold together. A failure to prove agreement is a definition error, not a deferred instantiation check, and stronger optional condition solvers cannot broaden acceptance.

A child conformance must satisfy its ancestor Contracts under its own conditions. For `Child : Parent`, verified declarations `Self is Parent when T is A` and `Self is Child when T is B` supply Parent evidence if either `T is A` or `T is B` is proven. The child path does not additionally require `T is A`, but every implementation it uses must be applicable. Redundant explicit parent declarations are valid only with the required path agreement.

#### 8.4.8.4. Intrinsics and boundaries

`Self is Copy when P` requests compiler-derived Copy under `D` and `P`. Every complete own Field or payload Type and the direct base are checked under §3.5; user-defined Copy bodies remain forbidden. Deriving Copy for a `ref/T` component needs no `T is Copy` premise, but an explicitly written `T is Copy` condition is still required and cannot be weakened. An unconditional `Self is Copy` keeps its all-bindings guarantee. Unknown Copy follows the verified conditional acquisition plans of §8.9 and §8.10 and is never assumed to be Non-Copy.

This feature defines static conformance and generic use. It adds no external registration, extension declarations, partial Type specialization, condition-based implementation replacement or runtime Contract View feature.

### 8.4.9. Bound Contracts, collisions, and proof paths

A conformance is identified by the conforming full Type and the bound Contract reference; a requirement or associated Type by its defining declaration and the bindings of its defining Contract. Bindings are substituted along refinement, and `Self` is the final conforming Type. Inherited input conditions become part of the child's public inputs without converting declaration obligations into assumptions or implementation requirements. Full Origin bindings remain part of the evidence.

#### 8.4.9.1. Direct conformance collisions

Direct conformances and merging proof paths use one collision test:

1. Group by Contract declaration; different declarations do not collide. Normalize bindings with the established Type equalities, associated specifications and Origin rules.
2. Unify parameters and fixed free-term structure (nominal Types, Tuples, Function Types) with variable kinds and an occurs-check. The same binder or slot on both sides is one variable. Alpha-normalize local function binders; they are not unified as outer input slots.
3. A provable fixed-structure mismatch establishes non-collision. Otherwise, the substitution and equalities are kept as collision conditions.
4. Unresolved associated projections, Semantics applications, length expressions, Origin intersections and ordering, and other non-free terms remain residual conditions. Syntactic differences alone do not prove non-collision, and unresolved residuals mean a possible collision. In particular, `A.Item` and `B.Item` need not differ when `A` and `B` differ.

Under the Type's ordinary input conditions, every pair of direct conformances must be proven non-colliding at definition time. Duplicate equal references and possible collisions are errors, and mutually exclusive `when` conditions do not exempt direct conformances. This finite check adds no call-site inference, implementation priority, automatic merging or selection after instantiation.

```kimi
struct Family<T>
    public contract Marker

struct Good
    Self is Family<i32>.Marker
    Self is Family<string>.Marker

// Invalid: A and B can become equal.
struct Bad<A, B>
    Self is Family<A>.Marker
    Self is Family<B>.Marker
```

#### 8.4.9.2. Merging paths and cycles

For other combinations of direct conformance, refinement and effective base conformance, each path's availability conditions are added to the collision conditions. When paths may meet, consistent associated bindings and requirements must be proven, and for conformance definitions also consistent implementation mappings. If meeting cannot be excluded and consistency cannot be proven, the definition is rejected. Equal references may share structure but never lose assumptions or obligations. For example, `Family<A>.C` and `Family<B>.C` cannot specify one associated Type as `i32` and `string` while `A = B` remains possible.

A refinement path that revisits a Contract declaration, even with different arguments, is rejected, including `Outer<T>.C` to `Outer<List<T>>.C`. Distinct instantiations on separate paths are not cyclic for that reason alone; processed references are tracked separately from the declarations on the current path. Contract-owned parameters, defaults, external conformance, implementation priority and runtime Contract Views are not introduced.

## 8.5. Runtime contracts

**Extension scope.** Contracts in this revision are static. Runtime-use designation and View associated-Type binding syntax remain to be defined; this section preserves the runtime design for that extension and grants no permission to form such Views in this revision. Object Views with concrete Core targets keep their existing rules. No implicit runtime designation or `where` syntax is introduced.

Compile-time conformance and runtime View usability are separate. A runtime Contract exposes requirements through dynamic dispatch without instance state. A bare Contract name is never a value Type; the extension uses explicit Object Semantics such as `objref/C`, `objuniq/C` and `obj/C`.

An **ObjectViewCompatible** Contract is one that, with its associated Types fixed, can be the View Target of an Object View Type such as `obj/C`, `objref/C` or `objuniq/C`. The Contract is the View Target, not the object's Dynamic Type, which remains the concrete Core actually constructed. This Contract property is distinct from the ObjectCallCompatible guarantee of each implementation's call operations.

**`ObjectViewCompatible(C)`** holds exactly when `C` is explicitly designated for runtime use, has no Type-function requirements (including inherited ones), and, with its associated Types fixed, every instance requirement satisfies:

| Aspect | Initial requirement |
| --- | --- |
| Receiver | Shared or Exclusive object access, without consuming ownership |
| Operation | Safe method or Property accessor |
| `Self` | No implementation-dependent `Self` outside the receiver |
| Binding | No unbound associated Types or method-specific generic parameters |
| Signature | Parameter and result Types are determined without knowing the hidden concrete Type |
| Origins | Expressible through the borrowed receiver, explicit inputs and `static`; no hidden payload Origin, Contract-level abstract Origin or higher-ranked requirement |

Every getter result and setter requirement is checked separately. Forming `objref/C` or another object form requires `ObjectViewCompatible(C)`, even when the concrete payload is unknown; it does not require searching all implementations. Unsupported requirements cannot simply be removed from the view: for example, `equals(other: Self)` cannot become a heterogeneous comparison between arbitrary `objref/C` values.

**`Implements(D, C)`** checks explicit or [validly inherited conformance](#844-conformance): every requirement has one implementation for `D`, with compatible signature, access and Origins, and published ObjectCallCompatible Proven (§12.4.4). Unknown does not establish this guarantee.

The unique static conformance for the concrete Type and Contract identity is used; changing View bindings cannot create another conformance. Static conformance does not generally persist in derived Types, so this extension must ensure that the ObjectViewCompatible restrictions, fixed associated-Type bindings and published implementation guarantees preserve the Supports relation through every derived layer. Static conformance alone does not prove that invariant.

Contract refinement follows [static refinement](#842-refinement). No replacement conformance, default implementation or external registration is introduced. Registration and artifact consistency follow the [metadata rules](21-layout-runtime-and-code-generation.md#2121-type-identity-and-descriptors).

```text
Speaker requires Shared speak() -> string
    ├─ Dog implements Speaker.speak
    └─ Cat implements Speaker.speak
```

```kimi
// Runtime extension: assume runtime designation, conformance, and makeDog() -> obj/Dog.
func announce(value: objref/Speaker) -> string
    return value.speak()

let dog = makeDog()
let message = announce(dog@objref/Speaker)
```

Only requirements accessible through `Speaker` are available from that view. Lifetime-preserving view formation also obeys [object upcasts](13-operators-and-assignment.md#1357-object-upcasts).

## 8.6. Callable constraints

`F is Callable<r, S>` is a built-in Constraint requiring calls with receiver access `r` and signature `S`; `Callable<S>` abbreviates `Callable<ref, S>`. Initially `r` is the literal Semantics `ref`, `uniq` or `owner`, and `S` is `(A1, ..., An) -> R`. The result may keep input Origins or already-bound external dependencies but cannot borrow the hidden environment receiver.

| Constraint receiver | Receiver acquired by a generic call | Admitted minimum call requirement |
| --- | --- | --- |
| `ref` | `ref/F` | Shared |
| `uniq` | `uniq/F` | Shared or Exclusive |
| `owner` | `F` acquired by value | Shared, Exclusive or Consuming |

The admitted Types are Function Items, concrete Closures and common Function Types with [compatible signatures](10-overload-resolution-and-inference.md#107-callable-signature-compatibility); no user `call` member is searched. `S` is a contract, not a conversion to an erased container. `F`'s complete dependencies are preserved under generic substitution, and neither Copy nor Owned is required of `F`. (`owner` denotes acquisition, whereas `Owned` denotes the absence of non-static dependencies.) The result restriction does not constrain direct concrete-Closure calls.

**Per-call input Origins.** An omitted Origin on each direct `ref/T` or `uniq/T` parameter of `S` is bound independently **per call**, for all three receiver kinds. Conformance must hold for every valid call-time Origin under the ordinary Type and Loan rules, not for one fixed long-lived Origin. Origins nested within `T`, and capture-derived Origins within `F`, remain fixed and are not quantified. This limited input-borrow quantification adds neither general higher-ranked Origin syntax nor explicit `from` or Origin parameter declarations inside `S`.

Omitted result Origins are completed under §15.4 from those per-call input Origins, keeping already-bound dependencies; the receiver of `F` is not an elision input. For example, `Callable<(ref/T) -> ref/T>` returns a borrow valid for its argument's Origin. With several direct borrowed inputs, result elision uses their meet and keeps all input Loans. A result requiring exclusive access must also preserve the corresponding exclusive Loan; shortening an Origin grants no access capability.

```kimi
func selectRef<T, F>(value: ref/T, select: ref/F) -> ref/T from value
    F is Callable<(ref/T) -> ref/T>
    return select(value)
```

```kimi
func applyBorrowed<T, U, F>(value: ref/T, transform: ref/F) -> U
    U is Owned
    F is Callable<(ref/T) -> U>
    return transform(value)
```

**Constraint-based calls.** A call first acquires the receiver required by `r` and then adapts to the implementation's minimum requirement:

- `ref`: call through shared access.
- `uniq`: keep exclusive access for the call, reborrowing exclusively or sharing for the actual body.
- `owner`: acquire by ordinary Copy or Move; borrow the acquired value for a Shared or Exclusive body, or transfer it to a Consuming body. Remaining acquired storage is destroyed when the call completes. This owned temporary has normal writable, exclusive access even when copied or moved from a `let` binding.

Instantiation cannot turn an `owner` acquisition into a borrow merely because the body is Shared. Conversely, copying a Consuming callable does not satisfy a `ref` or `uniq` constraint. One public signature is selected; when several available constraints have that signature, the weakest declared receiver is preferred, in the order `ref`, `uniq`, `owner`. A later initialization, access or Loan failure cannot select another receiver. Constraint strength is not an overload-ranking rule.

```kimi
func applyTwice<F>(value: i32, transform: uniq/F) -> i32
    F is Callable<uniq, (i32) -> i32>
    let first = transform(value)
    return transform(first)

let count: i32 = 0
var transform = func [var count] (value: i32) -> i32
    count += 1
    return value + count

let result = applyTwice(10, transform@uniq) // 13; captured count is now 2.
```

A Shared callable also qualifies, but a `uniq/F` argument still needs ordinary exclusive access. Shared environment access does not prevent use of a separate `uniq/T` argument's normal exclusive capability. By-value `F` parameters use normal Copy/Move; the constraint neither silently borrows them nor guarantees repeated calls or non-escape. Generic conformance and result Origin/Loan obligations must resolve before finalization. A returned input borrow need not keep the callable receiver, and no result may borrow call-local storage, including an `owner` receiver temporary.

## 8.7. Constraint proof system

Constraint meaning and available proof are distinct. `and`, `or` and `not` keep their Boolean meanings, but generic checking uses only the rules below. Accepted programs must not depend on optional SAT solving, arbitrary theorem proving, enumeration of Types or optimizer-derived facts. Fully determined concrete conditions still use ordinary Boolean evaluation.

In this section `P` and `Q` denote validated, bound propositions. Requirement expressions are parsed under [requirement precedence](#83-requirement-expressions) before interpretation: `T is (A and B)` supplies the propositions `(T is A) and (T is B)`, and similarly for `or` and the scope of `not`; source precedence is unchanged. Proposition identity uses bound subject and requirement Symbols, substitutions and normalized complete Types, including Semantics and Origins where applicable; equal source spellings alone are insufficient. Parentheses are transparent and `not not P` normalizes to `P`. No De Morgan, distributive or other logical-equivalence normalization is added.

The proof judgment has four outcomes, distinct from those of the Condition evaluator:

| Outcome | Meaning |
| --- | --- |
| **Proven** | The permitted rules establish the proposition. |
| **Refuted** | The permitted rules establish its negation; absence of proof is insufficient. |
| **Unknown** | Neither polarity is established by the permitted rules. This is not a Boolean value or an automatic right to defer. |
| **Error** | Invalid names, subjects, requirements or declarations, or detected contradictory evidence, prevent a valid judgment. |

Evidence comes from the current declaration's validated input Constraints, the defined built-in capability rules, and verified unconditional, conditional or inherited conformance. Facts keep their Binding Identity, substitutions and lexical/instantiation scope. A generic input Constraint is an assumption inside the constrained body and must be discharged at each use. A conformance declaration or `Self is C` obligation cannot prove its own implementation merely by being declared; its required implementations and prerequisite Constraints are validated under the conformance rules. Built-in derivation exceptions such as `Self is Copy` keep their own rules.

| Proof rule | Permitted derivation |
| --- | --- |
| Exact assumption | A matching available proposition proves itself; an available `not P` refutes `P`. |
| Conjunction elimination | An available `P and Q` supplies both `P` and `Q`, recursively. |
| Conjunction introduction | `P and Q` is Proven when both operands are Proven. |
| Disjunction introduction | `P or Q` is Proven when either operand is Proven; this grants no evidence for the other operand. |
| Negation | `not P` exchanges Proven and Refuted; Unknown remains Unknown and Error remains Error. Double negation is normalized as above. |
| Boolean refutation | `P and Q` is Refuted if either operand is Refuted; `P or Q` is Refuted if both are Refuted. |
| Concrete atomic judgment | The defined concrete Type-identity, Semantics/category and built-in capability tests, or the closed conformance judgment below. |
| Verified conformance | A verified explicit or inherited mapping, after its prerequisites are proven, including inheritance matching (§8.4.4) and conditional premises (§8.4.8). Legitimate unresolved prerequisites are retained; cyclic declarations alone prove nothing. |
| Contract refinement | From an available `T is C`, each ancestor conformance and inherited requirement Constraint, substituting `T` for `Self`. This does not discharge an unverified declaration's implementation obligations. |
| Associated-Type identity | Substitute and normalize explicit associated-Type specifications and available Type-identity Constraints under the [associated-Type rules](#843-associated-types). Bindings are not inferred from members, and no satisfying Type is searched for. |
| Other cases | Unknown, unless validation requires Error. |

All proof operands are validated, and Error absorbs even a determined truth result. Otherwise, a Refuted operand can refute a conjunction and a Proven operand can prove a disjunction despite Unknown operands. Exact compound assumptions are usable directly, but only conjunction elimination exposes their parts. `P or Q` together with `not P` cannot prove `Q`: there is no case analysis, contraposition, proof by contradiction or inference from contradiction. These limits govern symbolic proof, not Boolean evaluation of determined concrete judgments.

**Concrete and closed-world judgments.** Fully bound Types are compared by normalized identity, and determined Semantics/category and built-in capability tests use their defined rules. Unbound Types and unresolved prerequisites are not negative results. Absence of a conformance is Refuted only after the concrete declaration, inherited and potentially applicable conditional conformances, and every relevant merge, selection, binding and prerequisite in the fixed environment have been completed and all alternatives ruled out by the specified proofs. A failed lookup alone proves no absence; a malformed conformance is Error, and an unsupported associated-Type operation is not a concrete negative.

**Recursion.** An active obligation revisited with identical arguments supplies no evidence; cycles and in-progress registrations establish neither conformance nor refutation. Independent finite evidence, including evidence found after a temporary cycle result, is considered. Otherwise the result stays Unknown until its deadline and is diagnosed if still required. Recursive declarations alone are valid. Built-in structural analyses use their own recursion and fixed-point rules; no general coinduction is implied.

**Contradictions.** After the specified normalization and conjunction elimination, directly available `P` and `not P` are contradictory evidence and produce Error; so is evidence establishing both polarities of a queried proposition. Arbitrary capabilities are never derived from an inconsistent environment. Detecting more complex contradictions through excluded logical transformations is not required and cannot supply proof.

**Use and finalization.** A required Constraint succeeds only when Proven. Refuted fails the requirement, and Error reports invalid input or contradictory evidence. Unknown may be retained only when an identified later binding, instantiation or prerequisite analysis can resolve it before the applicable deadline; Unknown without such a dependency is an unproven-requirement error when proof is required. Every required concrete-call or instantiation obligation is resolved before finalization. Unknown is never accepted, converted to Refuted or used as evidence for a negated Constraint. Associated-Type inference beyond explicit identity facts and stronger symbolic reasoning remain design boundaries.

## 8.8. Explicit full function specialization

An **explicit full specialization** supplies an implementation for one closed set of static generic arguments of an existing generic function. It preserves that function's call contract but may produce different results or side effects. Selecting a matching specialization is mandatory, not an optional optimization.

Generic arguments here include function length slots, which are evaluated under the [length rules](04-arrays-indexing-and-slices.md#44-function-length-parameters) and leave no unbound length. `LengthKey(N)` identifies a length by its integer value. The Type and Origin checks below apply to Type slots.

### 8.8.1. Declaration and target identification

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func process<T>(value: T) -> ()
    ()
specialize func process<i32>(value: i32) -> ()
    ()
```

`specialize` is a contextual declaration introducer before `func`. A specialization has a single unqualified Name, explicit Type arguments, explicitly typed parameters (the fixed bare `self` shorthand of §7.3 is permitted), an optional result Type and a common single-item or indented Body (§14.2). It declares no Type parameters and does not bring the original function's Type parameter names into its body. Origin binders are inherited, not newly declared. An omitted result means Unit, even if the original's substituted result is non-Unit; write that result explicitly.

All of the function's own generic slots are supplied in declaration order, with their original kinds; ordinary and pair Type slots each take one complete Type. After alias and associated-Type normalization, every Core and Semantics component must be fixed: no unbound Type or Semantics parameter, unresolved projection or outer generic parameter may remain. `List<i32>` is closed; `List<T>` with an unbound `T` is not. `Self` is allowed only when ordinary resolution meets the same rule. Origins are checked separately (§8.8.2). These restrictions apply to specialization declarations, not to dependent Types in ordinary generic bodies.

The target must be a named generic Type function or instance method with an ordinary implementation; constructors, `deinit`, accessors and dedicated operator declarations are excluded. Partial or conditional specialization, omitted arguments, placeholders, priority rules, general Const arguments, specialization of a generic Container's arguments, methods with unbound outer generic parameters and explicit target-identity syntax are not introduced.

Duplicate ordinary declarations are rejected first. Then the original function is identified:

1. Collect same-name generic functions in the same Declaration Container with the same function kind (Type function or instance method) and generic slot count.
2. Bind the written generic arguments using each candidate's slot definitions. Match the substituted receiver presence and Type structure, and the ordinary parameters' count, order and normalized Type structure. Complete Types are formed and validated before Origins are excluded for this structural comparison.
3. Zero matches is an error, and multiple matches make the specialization declaration ambiguous. For exactly one match, the inherited contract is validated.

Result Types, parameter names, Constraint satisfaction, Origin relationships, implicit adaptation, slot kind alone, ordinary overload ranking and declaration or file order never resolve an ambiguity. A failed contract check cannot select another target.

```kimi
func inspect<T>(value: T) -> () => ()
func inspect<T>(value: i32) -> () => ()
specialize func inspect<i32>(value: i32) -> () => ()
// Error: both ordinary declarations have the same substituted input structure.
```

Diagnostics distinguish no target, input-structure mismatch, multiple targets, and a contract mismatch after selection. They include the candidate declaration locations and the relevant Type, receiver or arity differences; diagnostic comparison never chooses a nearest candidate.

### 8.8.2. Inherited contract

The specialization header identifies and checks the original contract; it is not an independent call Signature.

| Item | Specialization rule |
| --- | --- |
| Receiver and ordinary parameters | Restate count, order and substituted Type structure; no implicit adaptation |
| Result | Match the substituted Type; omission means Unit |
| External parameter names | Match the original; not used to identify the target |
| Internal names | Ordinary parameter names may change using `external => internal: Type`; an inherited receiver remains `self` under §7.3 |
| Origin and Loan contract | Inherit the binders and preserve every admitted Origin binding |
| Access, defaults, optionality, generic Constraints | Inherited; never redeclared, strengthened or weakened |
| Attributes, Safety, calling convention, Effect requirements | Inherited under the existing rules; no Attributes or additional modifiers on the specialization declaration |

An inherited optional parameter is written without `?` or a default. Callers still use the original omission and default-evaluation rules:

```kimi
func find<T>(value: T, count?: i32 = 1) -> () => ()
specialize func find<i32>(value: i32, count: i32) -> () => ()
find<i32>(10) // Evaluate the original default, then call the specialization.
```

These restrictions concern the specialization declaration, not ordinary syntax inside its body. A specialization of an unsafe function inherits its Safety contract, but unsafe operations in its body still need an Unsafe Block. Contract inheritance introduces no unsupported async, exception-Effect or variadic feature.

All necessary Type Origins are validated before comparison. After inherited binders are matched, the body must work for **every Origin binding permitted by the original contract**. A specialization cannot add an Origin parameter or lifetime restriction, narrow applicability by Origin, or register another implementation for different Origins. It cannot rescue an invalid ordinary implementation or replace a declaration-required proof; [deferred generic checking](#810-generic-body-checking-and-deferred-obligations) keeps its stated design boundary.

Inferred ObjectCallCompatible is a [common implementation guarantee](12-expressions.md#12443-implementation-families), not an additional declaration obligation inherited from the ordinary body. A specialization may make that public guarantee NotProven without becoming invalid for that reason; the declared Signature, Constraints, Safety and Effect requirements still apply.

### 8.8.3. Selection and declaration ownership

Selection uses the [Implementation Selection Key](21-layout-runtime-and-code-generation.md#2132-identity-and-generation-keys): the original function's declaration identity and its ordered, normalized static generic arguments, with only Origins excluded. Nominal identity, nested structure and every Semantics layer, including an outer object handle, are preserved, so `obj/D` and `rc/D` have different keys even when they identify the same dynamic payload Type. Two specialization declarations with the same key are an error.

Calls and function references first use ordinary lookup, overload resolution, inference and the original contract's Type, Constraint, Origin and Loan checks. Specializations never enter the candidate set or supply inference evidence. Once the static generic arguments determine a key, the matching specialization is used, or the ordinary implementation if none exists. A value's Dynamic Type, including behind a base or Contract View, never changes this selection.

```kimi
// classify and its i32 specialization are declared above.
func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let number: i32 = 10
let result = forward<i32>(number@ref) // 1, including with shared generic code.
```

A generic caller cannot be fixed to the ordinary implementation merely because its generic arguments were initially unknown. This guarantee is independent of code sharing, separate compilation, optimization level and LTO. Function references obey the same choice and their existing restrictions, including the ban on unsafe function values.

There is no direct name for the specialization and no syntax that bypasses it to call the ordinary body. Calling the same function with the same generic arguments from inside its specialization selects that specialization again and is recursive; move common work to a helper. An error in a specialization body never falls back to another body or overload.

Specializations belong to the original function's Kotonoha and Declaration Container. Existing Container fragments may put them in another file; unrelated extensions and other Kotonoha libraries cannot add or replace them. The defining Kotonoha closes the specialization set after environment selection and declaration collection, including generated sources, before finalizing affected call targets and no later than artifact finalization. Declaration and loading order cannot affect selection. Verification, artifact information and invalidation follow [generic code generation](21-layout-runtime-and-code-generation.md#213-generic-code-generation).

## 8.9. Generic access effects

Before ownership and Loan analysis is finalized for an acquisition or adaptation, its **Access Effect** is determined statically and uniquely:

```text
Access Effect
├─ acquisition: Copy / Move / Consume
├─ Loan action: shared Borrow / exclusive Borrow / Reborrow
└─ target Place and Origin dependencies
```

A broad Borrow-versus-acquisition category is insufficient: Copy is distinguished from Move, shared from exclusive, and Borrow from Reborrow. Symbolic generic Types and Origins are allowed if the required effect and dependencies are uniquely expressible. This rule applies to ordinary acquisition and to all generic `@s`, `@s/T` and `@Type` forms.

```text
Generic analysis
├─ effect known -> analyze normally
└─ legitimate dependency -> retain obligation
                            -> constraints / instantiation
                            -> determine effect -> finalize ownership and cleanup
```

Unresolved Copy capability is never treated as proof of Non-Copy, and the effect is never fixed to Move. At definition checking, legality is proven for every effect admitted by the declared Constraints. The exact effect may remain symbolic until instantiation only if every admitted case is legal, including subsequent uses, Loans and cleanup. This is delayed effect determination, not delayed discovery of a required capability. Environment-changing directives still obey their earlier [selection deadlines](19-compile-time-directives.md#194-name-resolution-boundary).

Instantiations may have different effects. The already-verified effect plan is substituted and each concrete body's cleanup derived from it; an analysis for a different effect is never reused without validation. Compile-time directives neither test Types nor select Access Effects. The [generic verification principle](#810-generic-body-checking-and-deferred-obligations) requires the ordinary body to be valid independently of explicit specializations.

```kimi
// s is a declared Semantics parameter; value is an initialized owned value.
value@s
use(value)
```

For `s = ref`, discarding the first result ends its shared Loan; `s = uniq` also requires exclusive writability. The later use is checked after that Loan ends. If the Constraints admit a Non-Copy `s = owner`, the first use Moves the source and makes the definition invalid, even if all current callers pass Copy values; require Copy evidence, or restrict the Semantics and prove borrow permissions. If the result is retained, subsequent uses are checked throughout its Loan lifetime.

## 8.10. Generic body checking and deferred obligations

**Universal body verification.** An ordinary generic body must be semantically valid for every well-formed Type/length/Origin argument binding that satisfies its declared Constraints, any enclosing conditional-conformance premises and its public Signature requirements. This is verified before the definition is accepted or exported, including definitions with no uses. Verification uses the limited proof system of §8.7 and symbolic Type, Origin and effect rules; it neither enumerates available Types nor infers a hidden capability Constraint from the body. Failure to establish a required proof is a definition error, and explicit specializations cannot rescue an invalid ordinary body.

This requirement fixes meaning, not a compiler-pass schedule. Dependencies on other declarations may delay checking within the build, but an unverified definition cannot be accepted merely because selected concrete instantiations succeed. In particular, a generic call to another generic function must prove that function's declared requirements from the caller's declared premises.

For unknown Copy, an acquisition that is legal as either Copy or Move may keep a conditional effect plan. A subsequent read that requires the source to remain Initialized must be legal in both cases; otherwise it requires an explicit `T is Copy`, a borrow that avoids acquisition, or a valid reinitialization before reuse. The conservative state is usable for proof, but the emitted operation must still Copy a Copy Type and Move a Non-Copy Type. A possible Copy never silently becomes a Move, and `T is Copy` is never added to a caller's applicability conditions after the body is checked.

~~~kimi
func transfer<T>(value: T) -> T => value // Valid for both Copy and Move.

func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func invalidTwice<T>(value: T) -> (T, T)
    return (value, value) // Error at definition: Copy is not guaranteed.
~~~

Generic stored acquisition and custom, computed and required getter results keep their declared Types (Chapter 11); none uses a Copy-dependent getter-result family. Copy/Move effects may remain conditional only after all cases are verified. Shared sequence and Pattern reads keep their separately defined correlated result families (§4.6.6); Field acquisition is not generalized to those operations.

A **Deferred Obligation** records remaining substitution or representation work for a verified definition; it is never an unproven body capability. It records its kind, defining bindings and environment, source location, declared premises, symbolic proof and effect plan, dependencies and deadline. Unknown names, missing conformance, possible use after Move and unresolved overload ambiguity cannot be deferred until a favorable instantiation.

| Stage | Required work |
| --- | --- |
| Definition | Resolve definition-side names and roles; prove body Type correctness, selected operations, capability requirements, and symbolic ownership/Origin/cleanup legality under the declared contract |
| Permitted dependency | Retain proven symbolic Type/effect families and explicit representation obligations; Unknown is neither success nor evidence of Non-Copy |
| Instantiation | Check the call's declared contract and ordinary argument/Loan validity; substitute verified plans; resolve concrete layout, representation and exact effects without adding semantic use conditions |
| Finalization | Discharge representation obligations and complete concrete ownership/cleanup plans before lowering executable operations; never retry committed lookup or overload selection |

For example, using the target projection `T` as a local Type inside `func f<s/T>(x: s/T)` must be justified by the declaration's Constraints and slot rules. If an admitted binding could make it an Object View Target rather than a value Type, the use is rejected at definition time rather than only for the affected callers. Positions that prohibit generic parameters outright, such as a base Type, remain prohibited.

**Public dependent obligations.** Only Type and Origin well-formedness conditions implied by the written Signature, generic schema, associated-Type requirements, Constraints and published conditional-member premises may restrict semantic applicability. They must be available to callers and artifacts without inspecting a private body, and are fixed when the declaration is checked; they cannot contain a newly inferred body requirement such as Copy, an extra Contract or a favorable acquisition case. There is no source syntax for arbitrary hidden requirements: a need that cannot be expressed or proven with the existing contract makes the definition invalid.

Concrete layout and representation validity may still depend on substitution or the prepared target, including finite representable storage for an otherwise well-typed body local. Such dependencies are recorded in the definition artifact, with their source and target requirements, before clients instantiate it. These checks concern representability only and cannot disguise a Type, capability or lifetime restriction. A call satisfying the public contract must not fail later because its callee newly discovers a semantic body requirement. Ordinary caller-side initialization and Loan checks, specified runtime checks, target representation failures and documented compiler resource exhaustion remain distinct; resource exhaustion is not semantic invalidity.

**Sealed and complete payloads.** Sealed and complete-payload uses are checked in both signatures and bodies under the declared premises. Missing Type roles, capabilities or lifetime evidence are definition errors; only representation obligations such as finite layout and profile-specific payload alignment may be deferred. A change of openness requires rechecking positive and negative Sealed results, inheritance, projections and public effects, including all permitted specializations (§18.3).

**ObjectCallCompatible** (§12.4.4) is a completed public operation guarantee used by projection and object-call legality; it is neither a conditional applicability premise nor a deferred body requirement. Its common implementation family is verified before publication, and caller-specific instantiations cannot strengthen it. Changes use the dependency revalidation of §21.3.4.

**Diagnostics.** Definition errors identify the body use and the missing declared proof; instantiation diagnostics identify the already-published dependent or representation obligation, the definition site, the arguments and the failed condition. Verified summaries and plans are preserved in compile-time metadata even when runtime keys erase Origins. Caller aliases and extensions are not added, overload choice is not redone for favorable concrete Types, and obligation strength does not rank candidates. Environment directives provide no Type or capability evidence and keep their earlier selection deadlines.
