# 8. Generics, constraints, and contracts

[Specification index](../SPEC.md)

Generic parameters describe admitted Types and capabilities. Constraints establish the proofs available to a generic body; Contracts name capabilities and map their requirements to implementations. Explicit specialization changes implementation selection, not the original callable contract.

## 8.1. Generic Type parameters

This section defines Type slots. Function length slots use explicit `<length N>` under [length parameters](04-arrays-indexing-and-slices.md#44-function-length-parameters); plain `<N>` remains a Type slot. A length slot accepts no complete Type and is not decomposed as a pair.

### 8.1.1. Slots and projections

Both parameter forms consume **one complete Type argument**. Preserve Semantics, nested Types, and all Origins during binding:

```text
<T>    : T = W
<s/T>  : WholeType = W
         s = OuterSemantics(W)
         T = DirectTarget(W)
         o = OuterOrigin(W)       // Present only for an outer safe borrow.
```

`WholeType`, the projection functions, and `o` are explanatory notation, not source bindings. `<s/T>` declares only `s` and `T`; the original `s/T` retains W's Origins without naming them. Source Origin names use the separate [Origin parameter schema](15-ownership-and-lifetime-analysis.md#153-abstract-origins).

An ordinary `T` denotes a complete value Type, not only a bare Core. The pair's `T` has the fixed internal kind **SemanticsTarget**, whose value is a complete value Type or an Object View Target. Using it as a standalone value Type in a generic body requires proof of that role under the declared Constraints at definition checking; only the remaining proved symbolic substitution may be a [deferred obligation](#810-generic-body-checking-and-deferred-obligations). Its kind does not change at instantiation.

Normalize transparent aliases, resolved associated-Type projections, grouping, and redundant `owner/` before projecting. Reject alias cycles; retain nominal declaration identity and parameter Binding Identities. No new nominal-alias syntax is introduced. Split only the outer layer: `ref/(uniq/i32 from b) from a` yields `s = ref`, `T = uniq/i32 from b`, and outer Origin `a`. Since `owner/V` preserves V, `owner/(ref/i32 from a)` has outer Semantics `ref`.

| Outer Semantics | Direct target and requirements for applying these Semantics to another U | Outer Origin |
| --- | --- | --- |
| `owner` | DirectTarget(W) is W itself; `owner/U` is U for any valid complete value Type | None |
| `ref`, `uniq` | Complete Referent Type; `uniq` also requires exclusive acquisition and Loans at use | Required |
| `obj`, `rc`, `arc` | Supported Core or valid runtime-contract View Target | None |
| `objref`, `objuniq` | Supported Core or valid runtime-contract View Target; preserve borrowing requirements | Required |
| `unsafe` | Complete Pointee Type; adds no safe-borrow lifetime guarantee | None |

These rows do not extend the current runtime-contract or callable restrictions. Absence of an outer Origin does not erase payload dependencies: in `ref/(View<i32> from (source => a)) from b`, a belongs to the inner Type and b to the outer borrow.

Within one parameter list, every bound name must be distinct: `<T, T>`, `<s/T, s/U>`, and `<s/s>` are errors. References use Binding Identity under ordinary scope rules. `<s>` is an ordinary Type slot; standalone Semantics slots are not introduced. Built-in forms such as `Callable<ref, S>` have their own grammar, not general Semantics arguments.

### 8.1.2. Reconstruction and Origin annotations

After transparent alias expansion, the original pair expression `s/T` refers to its WholeType W. The correspondence is determined by the declared bindings, not by two targets becoming equal after instantiation. `s/U` with another binding applies only the Semantics kind to U; it does not copy W's outer Origin. Once formed, equal complete Types do not acquire extra identity differences from their construction history.

An explicit Origin annotation uses ordinary Type-formation rules. Otherwise the original `s/T` retains W's Origins; another `s/U` uses the [position-specific Origin rules](15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts). Storing a Type in a pair slot creates no additional omission permission.

For `W = ref/i32 from a`, `s/T from b` forms `ref/i32 from b`. Formation does not require a relationship between W's old outer Origin a and the new b, and performs no value conversion. It must still validate b's binding, its annotation position, and the formed Type's own inner-Origin outlives constraints. Fitting an actual `from a` value to this Type separately checks permitted shortening (`a : b`), acquisition, and Loans. In particular, `uniq/V` retains invariant V and any required Move/Reborrow; an annotation neither creates nor removes Reborrow.

### 8.1.3. Argument validity

**WellFormedGenericTypeArgument(A)** requires a valid resolved complete Type, or a legitimately dependent Type with retained obligations. Check access, Semantics application, generic and associated-Type arguments, Origin mappings, and Constraints before using any Origin-erased comparison key.

| Argument | Rule |
| --- | --- |
| Primitive, struct, enum, Unit, Tuple | Allowed under ordinary Type formation |
| Common Function Type | Allowed under its existing signature, environment, and Origin restrictions |
| Function Item or Closure Type | Allowed through inference or an existing reference form; no new anonymous-Type spelling |
| Safe borrow, Object Type, raw pointer, Origin-bearing aggregate | Allowed as a Type argument; acquisition, storage, erasure, and Unsafe checks remain separate |
| Never | Allowed as a semantic Type argument, without adding a source name or value; a Never expression alone cannot infer an arbitrary unbound Type |
| Dependent Type | Preserve definition-side bindings and resolvable obligations until their deadlines |
| Unsized or unspecified representation | No new such Type is introduced; a valid Type must additionally meet layout requirements at each storage use |
| Bare Contract, group, bare Semantics, general value argument | Not a complete value Type argument; a Contract can appear only in an already permitted Object View Type |

Explicit argument lists supply every slot in declaration order, with optional trailing commas. Omitting the entire list uses only the inference supported by that construct. Partial lists, `_` placeholders, defaults, variadic slots, and general Const generics are not introduced. Only [function length slots](04-arrays-indexing-and-slices.md#44-function-length-parameters) admit the specified length arguments. `<s/T, U>` takes two arguments such as `<ref/i32 from a, string>`; `<ref, i32>` cannot supply one pair. Origin parameters have a separate schema and consume no Type argument slots.

A valid Type argument is not permission to use it in every role. Keep constraints on base Types, associated Types, finite layout, Copy derivation, and Object payload erasure. Ordinary storage preserves complete Type-argument dependencies under [storage contracts](15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts), without an Owned or Storable requirement. Static storage instead requires Owned and the [static-source rules](11-properties.md#1132-static-storage).

## 8.2. Constraints

The following terms distinguish declared capabilities, conditions, and their fulfillment:

| Term | Meaning | Example |
| --- | --- | --- |
| **Contract** | A Declaration Container declared with `contract` that specifies a named capability a Type provides. | `contract Comparable` |
| **Constraint** | A condition imposed on a Type or Type Semantics. | `T is Comparable` |
| **Constraints** | The set of conditions required for a declaration to be valid or usable. | The leading clauses of a generic function or structure. |
| **Conformance** | A Type's fulfillment of a Contract, with the required correspondence between requirements and implementations. | A Type fulfills `Comparable`. |

A **Constraint Clause** expresses a Constraint in the form `subject is requirement`. All clauses in a declaration's Constraints must hold. Subjects include complete Types, Semantics, valid target projections, and `Self` (the enclosing Type), subject to each requirement's role. Clauses establish capabilities the implementation may use. Each declaration kind restricts the permitted subjects; see [function Constraints](07-functions-and-callable-values.md#74-function-constraints).

Core requirements may name a capability declared with `contract` or another compile-time type capability. Type Semantics requirements may name concrete semantics, such as `ref` or `obj`, or a semantics category. Requirements combine with `and`, `or`, `not`, and parentheses under the [requirement-expression rules](#83-requirement-expressions).

A `struct` header may contain generic parameters and an Origin list. Its ordinary Constraints precede members; conditional conformances may appear at member positions (§8.4.8).

```kimi
struct Container<s/T> origin owner, source
    T is Comparable
    s is reference

    var value: s/T
```

These clauses constrain the pair's target projection `T` and Semantics projection `s`. Each requirement must accept its subject's role. Constraints may also apply to the enclosing Type:

```kimi
struct ComparableContainer<T>
    T is Comparable
    Self is Comparable

    var value: T
```

`T is Comparable` supplies comparison capabilities for the stored value's Type. In a Type declaration, `Self is Comparable` is both a Constraint and a [conformance declaration](#844-conformance). It requires `ComparableContainer<T>` itself to fulfill `Comparable`; it does not derive an implementation from the clause on `T`. Required members are omitted here. The built-in `Self is Copy` retains its specific [derivation rules](03-types-and-values.md#351-copy-capability-and-explicit-duplication).

## 8.3. Requirement expressions

`subject is requirement` tests a Type or Type Semantics in Constraint Clauses and associated-Type declarations/specifications. Its result is a compile-time `bool`; unresolved requirements never become runtime tests. Each context retains its permitted subjects, grammar, and validation rules. Requirement Tests are not allowed in `#if` or `#case` Conditions. Ordinary expressions instead use [runtime type tests](13-operators-and-assignment.md#1361-runtime-is-tests); syntax context, including through parentheses, determines the interpretation before lookup, with no fallback between Type and Value namespaces.

`is` binds on its left at comparison precedence. Its right side consumes a requirement expression through `or` precedence. An immediately following `not` negates that entire right side:

| Form | Meaning |
| --- | --- |
| `T is A and B` | T satisfies both A and B. |
| `T is A or B` | T satisfies A or B. |
| `T is not A or B` | T does not satisfy `(A or B)`. |
| `T is A and not B` | T satisfies A and does not satisfy B. |

Use `T is (not A) or B` to limit negation to A. A complete Requirement Test cannot be embedded in an ordinary Boolean expression such as `(T is A) and enabled`: source syntax provides no such mixed context. Use separate Constraint Clauses for requirements and environment directives for independently configured source selection. Value equality uses `==`.

## 8.4. Static contracts

A **Contract** is a Declaration Container defining a named capability through function requirements, Property requirements, associated Types, and Constraints. It has no executable implementations or storage. `Self` in a requirement denotes the conforming concrete Type.

```kimi
contract Source
    associate Element
    func read(self: ref/Self) -> Element

contract Sized
    property count: i32 has get

contract SizedSource: Source, Sized
    func reset(self: uniq/Self) -> ()
```

This revision defines static conformance checking and generic use. User-defined Contracts have no generic or contract-level Origin parameters and cannot capture an enclosing declaration's generic parameters. Ordinary generic Types/functions and separately specified built-in requirements such as `Callable<...>` are unaffected. Runtime Contract Views remain a [future extension](#85-runtime-contracts).

### 8.4.1. Function requirements

A function requirement declares a Name, optional function Generic and Origin parameters, explicitly typed parameters, and an optional result Type whose omission means Unit. A function with a receiver is an **instance function**; one without a receiver is a **type function**. Receiver, parameter labels, ownership, Origins, and safe/unsafe conditions use ordinary function rules.

Function-specific Constraints occupy an optional indented region immediately after the header. The region contains one or more Constraint Clauses, with no executable statements, `return`, local declarations, or single-item body. Its subjects are that function's generic parameters or associated-Type projections rooted in them. Put Contract-wide Constraints at the Contract body level.

```kimi
contract Factory
    func create<T>(value: T) -> Self
        T is Copy
```

Requirement Constraints are premises for checking implementation compatibility; they are not silently added to the implementation declaration. Requirements have no default or optional parameters. Calls through a requirement supply every ordinary argument; defaults on an implementation do not permit omission through the Contract. Requirements and required accessors have no independent [access modifiers](09-names-signatures-and-access.md#934-conformance-accessibility).

Property requirements use `has`; their selection and compatibility rules are defined in [Contract Property requirements](11-properties.md#114-contract-property-requirements).

### 8.4.2. Refinement

`contract C: A, B` refines every listed parent Contract. Resolve parents as Contract Names, without generic arguments. Inherit all requirements, associated Types, and Constraints. Parent order gives no priority; direct or indirect cycles are errors. Do not use `Self is C` to declare refinement inside a Contract.

```text
Source                 Sized
  Element, read()        count
       \                 /
             SizedSource
               reset()
```

Conformance to a child entails conformance to every ancestor. Inherited `Self` still denotes the final conforming Type. A child may add requirements or Constraints but cannot remove or weaken inherited ones.

Paths to the same ancestor declaration introduce one Requirement Identity or associated-Type Identity. Independent declarations from different parents remain distinct even when their names match; one compatible implementation may satisfy several requirements.

Reject a refinement when its requirements cannot coexist as separate implementations under ordinary declaration rules and are provably impossible to satisfy with one implementation. Use both the [implementation-identification key](#845-implementation-matching) and ordinary [Signature](09-names-signatures-and-access.md#91-signatures) rules: labels affect matching but cannot independently distinguish overloads. Different result Types alone do not prove a conflict; apply defined result compatibility, including the existing subtype and Never rules. Retain genuinely dependent checks until their prerequisites resolve; unknown is not a proof of contradiction.

### 8.4.3. Associated types

An associated Type is restricted to a Core. Unlike an ordinary generic Type slot, it cannot bind an arbitrary Semantics-applied complete Type.

The only intrinsic exception is the Element requirement of Core.Iterable and Core.Iterator (§22.1), which binds a complete Type, including Semantics and existing Origins. Its explicit specification uses the same associate syntax; it adds no general complete-Type associated declaration facility. For ordinary Core-Type associated requirements, specify borrow Semantics and operation Origins at use sites. Existing dependencies inside a Type remain subject to ordinary Origin checking; generic substitutions must prove this restricted role or retain a legitimate obligation.

```kimi
contract BorrowSource
    associate Element
    func read(self: ref/Self) -> ref/Element from self
```

`associate` declares a new associated Type inside a Contract and specifies an existing associated Type inside a conforming Type. Both use `is`, never `=`, for conditions:

| Form | Meaning and location |
| --- | --- |
| `associate Element` | Declare an associated Type in a Contract. |
| `associate Element is Equatable` | Declare it with a capability requirement, or constrain the uniquely identified associated Type in an implementation. |
| `associate Element is i32` | Declare it with a fixed Type, or specify that Type in an implementation. |
| `associate C.Element is T` | Specify identity with a generic binding T, subject to the associated Type's Core restriction. |

Determine each associated Type uniquely from explicit specifications and Type-identity Constraints on the Contract or its ancestors. Do not infer bindings from implementation signatures, member search, or function bodies, and do not choose a Type merely because it satisfies a capability. Substitute bindings before matching implementations. An unconstrained `Source.Element` is not inferred as `i32` merely because an implementation of `read` returns `i32`.

**Qualified specifications.** `associate C.Element is T` selects an associated Type through a direct conformance or its ancestor `C`. An unqualified `associate Element is T` is valid only when exactly one distinct associated-Type declaration with that name is available across those conformances and refinements. Multiple paths to one declaration count once. Ambiguous names require qualification; a short form never applies to all same-named declarations. A bare `associate Element` is not an implementation specification.

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

**Projections.** `T.C.Element` refers to the associated Type of `T`'s conformance to `C`; `T.Element` is the short form when the declaration is unique under the available Constraints. `C` uses ordinary Contract-name/alias lookup, not member lookup on `T`. Require conformance evidence; never discover it by searching for a same-named Contract. The Type-side base may be a named or constructed Core, a parameter, `Self`, or another associated-Type projection. Intrinsic protocol Element projections that bind a Semantics-applied Type do not become Core-Type qualifiers merely by being associated projections. It is not a value or Semantics-applied expression.

```kimi
func readOne<T>(source: ref/T) -> T.Source.Element
    T is Source
    return source.read()

func readInt<T>(source: ref/T) -> i32
    T is Source
    T.Source.Element is i32
    return source.read()
```

Collect leading function Constraints before resolving projections in its signature, including its result Type. Validate the Constraints themselves and discharge them at each use. Type context fixes a projection's namespace; preserve dotted syntax until Binding resolves Contract and associated-Type roles. Distinct successful interpretations are ambiguous. Expected results and fallback to value-member lookup cannot resolve that ambiguity.

**Refinement Constraints.** A child may constrain an inherited associated Type through `Self.C.Element`, where `C` is an ancestor. In a Contract-body Constraint subject, a bare associated-Type name is also permitted when unique among its own and inherited declarations. These clauses constrain existing declarations; they do not create replacement Types or conformances.

```kimi
contract Equatable
    func equals(self: ref/Self, other: ref/Self) -> bool

contract OrderedSource: Source
    Self.Source.Element is Equatable

contract IntSource: Source
    Self.Source.Element is i32
```

An `IntSource` implementation need not repeat its inherited `Source.Element is i32` binding. Explicit Type-identity facts support substitution and normalization; contradictory bindings are errors. Unresolved bindings remain obligations until the required finalization point. This adds no arbitrary associated-Type inference or proof search beyond the [limited proof rules](#87-constraint-proof-system).

### 8.4.4. Conformance

In a Type declaration, `Self is C` is both a Constraint and an explicit declaration of conformance to `C`. Same-named members alone do not register conformance.

```kimi
struct NumberSource
    Self is Source
    associate Source.Element is i32
    public func read(self: ref/Self) -> i32 => 42
```

**Conformance Identity** is the pair of the concrete Type Identity and Contract Identity. Different associated-Type bindings do not create different conformances to the same Contract. Explicit conformance and conformance implied by refinement to the same pair denote one conformance; all simultaneously applicable associated-Type bindings and requirement-to-implementation mappings must agree under §8.4.8's path checks. Redundant explicit parent conformance is allowed without a required warning.

Unconditional generic Type conformance must hold for every binding allowed by the Type Constraints; conditional conformance uses the additional premises and use checks of §8.4.8. Successful selected instantiations do not validate an unconstrained definition. Verify ancestor conformances and all effective mappings. Conformance declarations generate no implementations except explicitly specified intrinsic derivations and the limited standard Property witness bridges of §11.4.2.

Retain the verified requirement-to-Member Identity mapping and associated-Type bindings. Contract calls use this mapping rather than rediscovering members in the caller's source environment or after instantiation. No external registration, replacement conformance, default implementation, or access-bypassing witness thunk is introduced.

**Inherited conformance.** A verified `(B, C)` is inherited by `D : B` only if every requirement, with requirement-side Self replaced by D, is satisfied by the retained mapping. If Member M was declared in A, its implementation-side Self remains A, even through A -> B -> D. Apply Type/Origin substitutions along D's base path to A and retain associated-Type bindings. Only an eligible borrowed receiver may use [base-subobject projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection); other parameters/results gain no base conversion, and owning receivers gain no slicing. Check §8.4.5's access, Origins, Effects, and premises and §11.4's Property rules. Projected calls require published ObjectCompatible Proven (§12.4.4).

| Requirement shape | Inheritance through this path |
| --- | --- |
| Self only in a borrowed receiver, as in Stringify | Possible if all other checks and ObjectCompatible succeed |
| `other: ref/Self`, as in Equatable/Comparable | Fails: ref/A does not match ref/D |
| `func empty() -> Self` | Fails: A's result does not supply D |
| `owner/Self`, as in Iterable | Fails: no owning receiver projection |
| Fixed `Self.Element` normalizes to the same Type | Match the normalized Types; the spelling Self alone does not prohibit inheritance |

One failed inheritance path does not invalidate D's declaration. Determine `(D, C)` from all explicit, conditional, and Contract-refinement paths under §8.7; only a proof that all candidates fail gives Refuted. Unresolved generic dependencies remain Unknown until their deadline; invalid declarations or inconsistent evidence are Error. Successful paths must agree on associated Types and implementation mappings. New explicit conformance uses ordinary implementation lookup; inherited mappings are not replaced by same-named declarations.

Copy, Owned, Callable, and other intrinsic capabilities retain their own derivation rules. In particular, struct Copy requires the derived declaration's opt-in and all stored components; it is not inherited automatically.

Do not warn merely because an open base has a conformance its descendants cannot inherit. At a failing derived `Self is C` or constraint use, identify the requirement and cause: Self mismatch, owning receiver, access/Origin/premise failure, or ObjectCompatible NotProven and its responsible implementation. Diagnose an unresolved proof as Unknown, not Refuted. For example, an Equatable Shape may have a valid `Circle : Shape` without Circle being Equatable; requiring Circle's conformance reports the ref/Shape versus ref/Circle mismatch and the inherited-Name prohibition. When descendants need such Self-dependent Contracts, implementing them on leaf Types or using composition avoids this restriction; the base's own conformance remains valid.

### 8.4.5. Implementation matching

Validate ordinary declarations first. Functions differing only in result Type, Origins, Constraints, or `unsafe` cannot coexist in one scope under the existing Signature rules; they are duplicate declarations before conformance matching.

After substituting `Self`, the conforming Type's arguments, and associated Types, identify a function implementation by the following key:

| Component | Required match |
| --- | --- |
| Name | Exact name. |
| Function kind | Type function or instance function. |
| Function generic parameters | Same count, kinds, and order; correspond by position, not spelling. |
| Ordinary parameters | Same count, order, and external labels; internal names need not match. |
| Receiver | Same presence and normalized Type structure, with only the inherited receiver correspondence allowed by §8.4.4. |
| Parameter Types | Same normalized Type structure. |

Type structure includes resolved Core Identity, Semantics, nested structure, and type arguments. Exclude Origin names, bindings, and lifetime relations from identification, but retain them for compatibility. There is no parameter-structure contravariance. Do not rank implementations by ordinary call overload preferences, adaptations, omitted arguments, Origins, Constraints, conditional-member applicability, or Effects. Conditions are checked after identification, unlike direct-call applicability (§8.4.8).

These rules identify Contract implementations; `Callable` and common Function Types retain their separate [callable signature compatibility](10-overload-resolution-and-inference.md#107-callable-signature-compatibility) rules.

Zero candidates means a missing implementation; multiple candidates mean ambiguity, even if only one would pass compatibility. For one candidate, check:

| Aspect | Compatibility obligation |
| --- | --- |
| Constraints | Type/conformance and requirement premises must prove the implementation's Constraints and any conditional-member premises. |
| Input Origins | Every input allowed by the requirement remains valid; no stronger lifetime precondition. |
| Result Type | The same Type or a subtype permitted by the existing Type rules. |
| Result Origins | At least the required lifetime guarantees. |
| Access | Usable throughout the [conformance's effective domain](09-names-signatures-and-access.md#934-conformance-accessibility). |
| Calling context and Effects | No stronger calling context or effects than the requirement permits. |

Compare Origin contracts after binder correspondence using ordinary variance and Loan rules, including invariance where required. A requirement admitting a call-local borrow cannot be implemented by a function requiring that input to be `static`. Origin-free identification does not erase dependencies or relax exclusive access.

Use existing Safety, ownership, Origin, and Access Effect checks; no new effect system is defined here. A safe requirement cannot require an unsafe calling context. Result compatibility inserts no numeric/user conversion, Copy, Borrow/Reborrow, or erasure. Core inheritance alone does not prove compatibility of complete Types. On compatibility failure, report the conformance error without searching for another implementation. Properties use the corresponding [accessor rules](11-properties.md#114-contract-property-requirements).

### 8.4.6. Calls and shared requirements

Under `T is C`, generic code may use requirements and associated Types of `C` and its ancestors. A type function is called through the conforming Type, as `T.empty()`; it needs no instance.

```kimi
contract EmptyConstructible
    func empty() -> Self

func makeEmpty<T>() -> T
    T is EmptyConstructible
    return T.empty()
```

`EmptyConstructible.empty()` is invalid because it does not identify an implementation Type. Do not infer that Type backward from the expected result. A concrete call such as `Buffer.empty()` uses ordinary type-member lookup; generic requirement calls retain their conformance mapping.

Distinct Requirement Identities may form one call candidate only when both their exposed signatures/conditions are equivalent and their mappings select the same effective implementation Member Identity for every valid type substitution allowed by the current Constraints. Compare function generics, parameters/labels, receiver, results, Origins, Constraints, and calling conditions, plus the implementation's type substitutions and receiver correspondence. Equal code, runtime addresses, or optimizer sharing supply no proof. Keep the conformance requirements distinct. Repeated paths to the same Requirement Identity are already one requirement and need no such additional proof.

```text
A.reset --+-- equivalent call contract and same mapping proved
B.reset --+                         |
                               one call candidate
```

Use only the defined proof rules, not enumeration of instantiations or arbitrary theorem proving. If equivalence cannot be proved, retain the candidates and apply ordinary call selection; a non-unique result is ambiguous. A coincidental match in one instantiation cannot make an otherwise invalid generic call valid. No additional qualified-call syntax such as `value@A.reset()` is introduced; `@` retains its existing adaptation meaning.

### 8.4.7. Intrinsic contracts and guarantees

The [required Core declaration table](22-core-execution-and-foreign-functions.md#221-required-core-declarations) also fixes the identities and signatures used for `Stringify`, `Equatable`, `Comparable`, `Iterable`, and `Iterator`. Their source conformance uses the ordinary static Contract rules; their only special effects are the interpolation, comparison, and iteration mappings explicitly specified here.

Some Contracts are **compiler-intrinsic**, including `Copy`. Each has only the special acquisition, destruction, layout, concurrency, or code-generation effects explicitly defined for it. These effects belong to the compiler-recognized Contract Identity; a user Contract with the same name or requirements does not gain them. `Self is MyCopy` does not make a Type Copy. Compiler-derived conformance is available only where individually specified, and `Self is Copy` must pass its ordinary derivation checks.

Conformance proves only statically specified requirements. Documentation laws such as equality symmetry or transitivity are not enforced by the type system. It does not prove current initialization, absence of conflicting Loans, storage representation, or direct Field access. Ordinary usage checks and documented unsafe safety obligations still apply.

### 8.4.8. Conditional conformance

A generic struct or enum may declare `Self is C when P`. Its ordinary Type Constraints D govern every use of the Type; P governs this declaration's conformance to C and does not become a Type-formation constraint. Normal Type, storage, Semantics, and Origin validity still apply.

```kimi
enum Option<T>
    Self is Copy when T is Copy
    Some(T)
    None
```

`Option<Resource>` remains a usable Type when Resource is non-Copy. This declaration makes `Option<i32>` Copy. In contrast, a leading `T is Copy` would restrict every Type use, while unconditional `Self is Copy` would promise Copy for every binding admitted by D.

#### 8.4.8.1. Conditions and implementation scope

The declaration targets one Contract and appears at a Type member position; ordinary Type Constraint Clauses still precede members. `when` is contextual only here. Conditions are comma-separated `subject is requirement` clauses, all conjoined. Subjects are the enclosing generic parameters, their Semantics/target projections, and associated-Type projections valid under existing rules. Requirements must accept their subject's role; no new parameters are introduced.

Conditions allow positive requirement atoms joined by `and` and parentheses. Atoms use existing Requirement Test interpretations. Reject `or`, `not`, value tests, and arbitrary Boolean expressions even inside parentheses. This restricted grammar does not change ordinary PositiveRequirement.

```kimi
Self is C when T is A and B, U is D
// Requires T is A, T is B, and U is D.
```

An optional indented implementation block declares members in the enclosing Type's namespace. It is a condition scope, not a new Type or Value namespace. Allow functions, computed members where the Type kind permits them, and associated-Type specifications. Reject Fields, enum Cases, constructors, deinit, nested Types, and nested conformance declarations. Enums still forbid computed members. Conditional conformance never changes storage layout or Case structure.

```kimi
contract Describe
    func describe(self: ref/Self) -> string

struct Box<T>
    var value: T

    Self is Describe when T is Describe
        public func describe(self: ref/Self) -> string
            return self.value.describe()
```

P is a published static precondition of each block member; it does not leak to other Type members. Collect D and P before resolving dependent signatures, associated-Type specifications, and bodies, and validate their conditions and noncircular evidence. The block may be omitted only when existing members or an explicitly specified intrinsic derivation satisfy the requirements. Do not generate ordinary Contract implementations.

#### 8.4.8.2. Verification and use

At definition, verify C under D and P for every admitted binding. Identify implementations by §8.4.5 (Property implementations by §11.4), not ordinary call overload ranking. Add each requirement's premises when proving the selected implementation's published conditions, access, Origins, ownership, and Effects. Compatibility failure never retries a different implementation. Fix and retain requirement-to-Member mappings, Field bridges where applicable, and associated-Type bindings at this stage.

At use, substitute Type arguments; for a Type satisfying D, proof of P enables the verified conformance. Do not select implementations again using caller lookup or favorable instantiations.

Direct member use has a distinct applicability step: after lookup commits a function group and Type inference/substitution completes, prove its member's P before Best Candidate comparison. The same condition applies to function references and computed access. Associated-Type projections require evidence of the relevant conformance.

| Judgment of P | Member applicability | Conformance use |
| --- | --- | --- |
| Proven | Condition satisfied; check other applicability rules | This conformance path is available |
| Refuted | Inapplicable | This path supplies no conformance; Type use remains possible |
| Unknown | Applicability unproven | Evidence for neither conformance nor absence |
| Error | Diagnose even if another candidate succeeds | Diagnose invalid declaration or condition |

Conditions never alter lookup's committed layer or the inherited-Name prohibition, and an inapplicable member cannot reopen outer/base lookup. Compare applicable members of the committed group by ordinary overload rules, without ranking condition strength. Loan or initialization failure after selection does not cause reselection. A generic definition cannot restore a member rejected for lack of proof merely because a later instantiation satisfies P. If a legitimate temporary Unknown can affect selection, defer the decision rather than committing an alternative.

Unknown dependencies and deadlines follow §8.7/§8.10. Required proof missing at its deadline is an error. Neither the declaration itself nor circular conformance search supplies evidence. Concrete absence requires completing all relevant paths, including ancestor conformances. Conditional syntax and definitions are checked even if no current Type arguments satisfy P; environment-only `#if`/`#switch` cannot replace these checks.

#### 8.4.8.3. Uniqueness and parent contracts

Allow at most one direct conformance declaration for each generic Type and Contract, counting unconditional and conditional declarations together after fragment merging. Do not permit multiple declarations by proving their conditions disjoint, choosing priorities, or specializing implementations.

```kimi
Self is C when T is A
Self is C when T is B // Error: duplicate direct conformance.
```

Block members obey ordinary duplicate rules; equal Signatures differing only in conditions are duplicates. Distinct Signatures may overload under the applicability rules above.

Conformance Identity remains (concrete Type Identity, Contract Identity). A direct declaration and Contract refinement may provide several evidence paths to that same conformance. At definition, check every pair under D and both paths' conditions: prove equal implementation mappings and associated-Type bindings unless §8.7 can establish that the paths cannot hold together. Failure to prove agreement is a definition error, not a deferred instantiation check; stronger optional condition solvers cannot broaden acceptance.

A Child conformance must satisfy its ancestor Contracts under its own conditions. For `Child : Parent`, verified declarations `Self is Parent when T is A` and `Self is Child when T is B` supply Parent evidence if either T is A or T is B is proven. The child path does not also require T is A, but every implementation it uses must be applicable. Redundant explicit parent declarations are valid only with the required path agreement.

#### 8.4.8.4. Intrinsics and boundaries

`Self is Copy when P` requests compiler-derived Copy under D and P. Check every complete own Field/payload Type and direct base under §3.5; user-defined Copy bodies remain forbidden. Deriving Copy for a `ref/T` component needs no T-is-Copy premise, but an explicitly written T-is-Copy condition is still required and cannot be weakened. Unconditional `Self is Copy` keeps its all-bindings guarantee. Unknown Copy follows the verified conditional acquisition plans of §8.9/§8.10, never an assumption of non-Copy.

This feature defines static conformance and generic use. It adds no external registration, extension declarations, partial Type specialization, condition-based implementation replacement, or new runtime Contract View feature.

## 8.5. Runtime contracts

**Extension scope.** Contracts in this revision are static. Runtime-use designation and View associated-Type binding syntax remain to be defined; this section preserves the runtime design for that extension, not permission to form such Views in this revision. Object Views with concrete Core targets retain their existing rules. No implicit runtime designation or new `where` syntax is introduced.

Compile-time conformance and runtime View usability are separate. A runtime Contract exposes requirements through dynamic dispatch without instance state. A bare Contract name is never a value Type; the extension uses explicit Object Semantics such as `objref/C`, `objuniq/C`, and `obj/C`.

**`RuntimeUsable(C)`** holds exactly when `C` is explicitly designated for runtime use, it has no type-function requirements (including inherited ones), and, after fixing associated Types, every instance requirement satisfies:

| Aspect | Initial requirement |
| --- | --- |
| Receiver | Shared or Exclusive object access, without consuming ownership |
| Operation | Safe method or Property accessor |
| `Self` | No implementation-dependent `Self` outside the receiver |
| Binding | No unbound associated Types or method-specific generic parameters |
| Signature | Parameter/result Types are determined without knowing the hidden concrete Type |
| Origins | Expressible through borrowed receiver, explicit inputs, and `static`; no hidden payload Origin, contract-level abstract Origin, or higher-ranked requirement |

Check every getter result and setter requirement separately. Forming `objref/C` or another object form requires `RuntimeUsable(C)`, even when the concrete payload is unknown; it does not require searching all implementations. Unsupported requirements cannot simply be removed from the view. For example, `equals(other: Self)` cannot become heterogeneous comparison between arbitrary `objref/C` values.

**`Implements(D, C)`** checks explicit or [validly inherited conformance](#844-conformance). Every requirement has one implementation for D, with compatible signature, access, Origins, and published ObjectCompatible Proven (§12.4.4). Unknown does not establish this guarantee.

Use the unique static conformance for the concrete Type and Contract Identity; changing View bindings cannot create another conformance. Static conformance does not generally persist in derived Types. This extension must ensure that RuntimeUsable restrictions, fixed associated-Type bindings, and published implementation guarantees preserve its Supports relation through every derived layer. Static conformance alone is not sufficient proof of that invariant.

Contract refinement follows [static refinement](#842-refinement). No replacement conformance, default implementation, or external registration is introduced. Registration and artifact consistency follow [metadata](21-layout-runtime-and-code-generation.md#2121-type-identity-and-descriptors). Static declarations and associated-Type specifications are defined; only runtime-use designation and View binding syntax remain deferred here.

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

Only requirements accessible through `Speaker` are available from that view. Lifetime-preserving view formation additionally obeys [object upcasts](13-operators-and-assignment.md#1357-object-upcasts).

## 8.6. Callable constraints

`F is Callable<r, S>` is a built-in Constraint requiring calls with receiver access `r` and signature `S`. `Callable<S>` abbreviates `Callable<ref, S>`. Initially `r` is the literal Semantics `ref`, `uniq`, or `owner`, and `S` is `(A1, ..., An) -> R`. The result may retain input Origins or already-bound external dependencies, but cannot borrow the hidden environment receiver.

| Constraint receiver | Receiver acquired by a generic call | Admitted minimum call requirement |
| --- | --- | --- |
| `ref` | `ref/F` | Shared |
| `uniq` | `uniq/F` | Shared or Exclusive |
| `owner` | F acquired by value | Shared, Exclusive, or Consuming |

Admitted Types are Function Items, concrete Closures, and common Function Types with [compatible signatures](10-overload-resolution-and-inference.md#107-callable-signature-compatibility). No user `call` member is searched. `S` is a contract, not a conversion to an erased container. Preserve `F`'s complete dependencies under generic substitution; neither Copy nor Owned is required of `F`. `owner` denotes acquisition, whereas `Owned` denotes absence of non-static dependencies. The result restriction does not constrain direct concrete-Closure calls.

An omitted Origin on each direct `ref/T` or `uniq/T` parameter of `S` is independently bound **per call**, for all three receiver kinds. Conformance must hold for every valid call-time Origin under ordinary Type/Loan rules, not one fixed long-lived Origin. Origins nested within `T` and capture-derived Origins within `F` remain fixed and are not quantified. This limited input-borrow quantification adds neither general higher-ranked Origin syntax nor explicit `from` or Origin parameter declarations inside `S`.

Complete omitted result Origins using §15.4 with those per-call input Origins; retain already-bound dependencies. The receiver of `F` is not an elision input. For example, `Callable<(ref/T) -> ref/T>` returns a borrow valid for its argument's Origin. With several direct borrowed inputs, result elision uses their meet and retains all input Loans. A result requiring exclusive access must also preserve the corresponding exclusive Loan; shortening an Origin grants no access capability.

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

A constraint-based call first acquires the receiver required by `r`, then adapts to the implementation's minimum requirement:

- `ref`: call through shared access.
- `uniq`: retain exclusive access for the call, reborrowing exclusively or sharing for the actual body.
- `owner`: acquire by ordinary Copy/Move; borrow that acquired value for a Shared/Exclusive body or transfer it to a Consuming body. Remaining acquired storage is destroyed on call completion. This owned temporary has normal writable/exclusive access even when copied or moved from a `let` binding.

Instantiation cannot turn an `owner` acquisition into a borrow merely because the body is Shared. Conversely, copying a Consuming callable does not satisfy a `ref` or `uniq` constraint. Select one public signature; for multiple available constraints with that signature, prefer the weakest declared receiver in order `ref`, `uniq`, `owner`. A later initialization, access, or Loan failure cannot select another receiver. Constraint strength is not an overload-ranking rule.

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

A Shared callable also qualifies, but a `uniq/F` argument still needs ordinary exclusive access. Shared environment access does not prevent use of a separate `uniq/T` argument's normal exclusive capability. By-value `F` parameters use normal Copy/Move; this constraint does not silently borrow them or guarantee repeated calls or non-escape. Generic conformance and result Origin/Loan obligations must resolve before finalization. A returned input borrow need not retain the callable receiver; no result may borrow call-local storage, including an `owner` receiver temporary.

## 8.7. Constraint proof system

Constraint meaning and available proof are distinct. `and`, `or`, and `not` retain their Boolean meanings, but generic checking uses only the rules below. The accepted programs must not depend on optional SAT solving, arbitrary theorem proving, enumeration of Types, or optimizer-derived facts. Fully determined concrete conditions still use ordinary Boolean evaluation.

In this section `P` and `Q` denote validated, bound propositions. Parse requirement expressions under [requirement precedence](#83-requirement-expressions) before interpreting them: `T is (A and B)` supplies the propositions `(T is A) and (T is B)`, and similarly for `or` and the scope of `not`. This interpretation does not change source precedence. Proposition identity uses bound subject and requirement Symbols, substitutions, and normalized complete Types, including Semantics and Origins where applicable; equal source spellings alone are insufficient. Parentheses are transparent and `not not P` normalizes to `P`. No De Morgan, distributive, or other logical-equivalence normalization is added.

The proof judgment has four outcomes, distinct from the Condition evaluator's outcomes:

| Outcome | Meaning |
| --- | --- |
| **Proven** | The permitted rules establish the proposition. |
| **Refuted** | The permitted rules establish its negation; absence of proof is insufficient. |
| **Unknown** | Neither polarity is established by the permitted rules. This is not a Boolean value or an automatic right to defer. |
| **Error** | Invalid names, subjects, requirements, declarations, or detected contradictory evidence prevent a valid judgment. |

Evidence comes from the current declaration's validated input Constraints, defined built-in capability rules, and verified unconditional, conditional, or inherited conformance. Facts retain their Binding Identity, substitutions, and lexical/instantiation scope. A generic input Constraint is an assumption inside the constrained body, but must be discharged at use. A conformance declaration or `Self is C` obligation cannot prove its own implementation merely by being declared; its required implementations and prerequisite Constraints must be validated under the existing conformance rules. Built-in derivation exceptions such as `Self is Copy` retain their own rules.

| Proof rule | Permitted derivation |
| --- | --- |
| Exact assumption | A matching available proposition proves itself; an available `not P` refutes `P`. |
| Conjunction elimination | Available `P and Q` supplies both `P` and `Q`, recursively. |
| Conjunction introduction | Prove `P and Q` when both operands are Proven. |
| Disjunction introduction | Prove `P or Q` when either operand is Proven. This grants no evidence for the other operand. |
| Negation | Exchange Proven and Refuted for `not P`; Unknown remains Unknown and Error remains Error. Double negation is normalized as above. |
| Boolean refutation | Refute `P and Q` if either operand is Refuted; refute `P or Q` if both are Refuted. |
| Concrete atomic judgment | Use defined concrete Type-identity, Semantics/category, and built-in capability tests, or the closed conformance judgment below. |
| Verified conformance | Use a verified explicit or inherited mapping after proving its prerequisites, including inheritance matching (§8.4.4) and conditional premises (§8.4.8). Retain legitimate unresolved prerequisites; cyclic declarations alone prove nothing. |
| Contract refinement | From available `T is C`, use each ancestor conformance and inherited requirement Constraint, substituting `T` for `Self`. This does not discharge an unverified declaration's implementation obligations. |
| Associated-Type identity | Substitute and normalize explicit associated-Type specifications and available Type-identity Constraints under [associated-Type rules](#843-associated-types). Do not infer bindings from members or search for a satisfying Type. |
| Other cases | Unknown, unless validation requires Error. |

Validate all proof operands; Error absorbs even a determined truth result. Otherwise Refuted can refute a conjunction and Proven can prove a disjunction despite unknown operands. Exact compound assumptions are usable directly, but only conjunction elimination exposes their parts. `P or Q` plus `not P` cannot prove Q: there is no case analysis, contraposition, contradiction proof, or inference from contradiction. These limits govern symbolic proof, not Boolean evaluation of determined concrete judgments.

**Concrete and closed-world judgments.** Compare fully bound Types by normalized identity; use the defined rules for determined Semantics/category and built-in capability tests. Unbound Types and unresolved prerequisites are not negative results. Conformance absence is Refuted only after completing the concrete declaration, inherited and potentially applicable conditional conformances, and every relevant merge, selection, binding, and prerequisite in the fixed environment. Rule out all alternatives using the specified proofs. Failed lookup alone proves no absence; malformed conformance is Error, and an unsupported associated-Type operation is not a concrete negative.

**Recursion.** An active obligation revisited with identical arguments supplies no evidence; cycles and in-progress registrations establish no conformance or refutation. Consider independent finite evidence, including evidence found after a temporary cycle result. Otherwise retain Unknown until its deadline and diagnose if still required. Recursive declarations alone are valid. Built-in structural analyses use their own recursion/fixed-point rules; no general coinduction is implied.

**Contradictions.** After the specified normalization and conjunction elimination, directly available `P` and `not P` are contradictory evidence and produce Error. Likewise, evidence establishing both polarities of a queried proposition is Error. Do not derive arbitrary capabilities from an inconsistent environment. Detection of more complex contradictions by excluded logical transformations is not required and cannot supply proof.

**Use and finalization.** A required Constraint succeeds only when Proven. Refuted fails the requirement; Error reports invalid input or contradictory evidence. Unknown may be retained only when an identified later binding, instantiation, or prerequisite analysis can resolve it before the applicable deadline. Unknown without such a dependency is an unproven-requirement error when a proof is required. Every required concrete-call or instantiation obligation must be resolved before finalization. Unknown is never accepted, converted to Refuted, or used as evidence for a negated Constraint. Associated-Type inference beyond explicit identity facts and stronger symbolic reasoning remain design boundaries.

## 8.8. Explicit full function specialization

An **explicit full specialization** supplies an implementation for one closed set of static generic arguments of an existing generic function. It preserves that function's call contract, but may produce different results or side effects. Selecting a matching specialization is mandatory, not an optional optimization.

Generic arguments here include function length slots. Evaluate them under [length rules](04-arrays-indexing-and-slices.md#44-function-length-parameters), leaving no unbound length. `LengthKey(N)` identifies a length by its integer value. The Type/Origin-specific checks below apply to Type slots.

### 8.8.1. Declaration and target identification

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func process<T>(value: T) -> ()
    ()
specialize func process<i32>(value: i32) -> ()
    ()
```

`specialize` is a contextual declaration introducer before `func`. A specialization has a single unqualified Name, explicit Type arguments, explicitly typed parameters, an optional result Type, and a common single-item or indented Body (§14.2). It declares no Type parameters and does not automatically introduce the original function's Type parameter names into its body. Origin binders are inherited, not newly declared. Omitted results mean Unit, even if the original's substituted result is non-Unit; write that result explicitly.

Supply all of the function's own generic slots in declaration order, using their original kinds. Ordinary and pair Type slots each take one complete Type. After alias and associated-Type normalization, every Core and Semantics component must be fixed: no unbound Type/Semantics parameter, unresolved projection, or outer generic parameter may remain. `List<i32>` is closed; `List<T>` with unbound T is not. `Self` is allowed only when ordinary resolution meets the same rule. Origins are checked separately below. These restrictions apply to specialization declarations, not to dependent Types in ordinary generic bodies.

The target must be a named generic type function or instance method with an ordinary implementation. Constructors, `deinit`, accessors, and dedicated operator declarations are excluded. Partial or conditional specialization, omitted arguments, placeholders, priority rules, general Const arguments, specializing a generic Container's arguments, methods with unbound outer generic parameters, and explicit target-Identity syntax are not introduced.

First reject duplicate ordinary declarations. Then identify the original function:

1. Collect same-name generic functions in the same declaration Container with the same function kind (type function or instance method) and generic slot count.
2. Bind the written generic arguments using each candidate's slot definitions. Match the substituted receiver presence and Type structure and the ordinary parameters' count, order, and normalized Type structure. Form and validate complete Types before excluding Origins for this structural comparison.
3. Zero matches is an error; multiple matches is an ambiguous specialization declaration. For exactly one match, validate the inherited contract.

Do not use result Types, parameter names, Constraint satisfaction, Origin relationships, implicit adaptation, slot kind alone, ordinary overload ranking, or declaration/file order to resolve ambiguity. A failed contract check cannot select another target.

```kimi
func inspect<T>(value: T) -> () => ()
func inspect<T>(value: i32) -> () => ()
specialize func inspect<i32>(value: i32) -> () => ()
// Error: both ordinary declarations have the same substituted input structure.
```

Diagnostics distinguish no target, input-structure mismatch, multiple targets, and contract mismatch after selection. Include candidate declaration locations and relevant Type, receiver, or arity differences; diagnostic comparison never chooses a nearest candidate.

### 8.8.2. Inherited contract

The specialization header identifies and checks the original contract; it is not an independent call Signature.

| Item | Specialization rule |
| --- | --- |
| Receiver and ordinary parameters | Restate count, order, and substituted Type structure; no implicit adaptation |
| Result | Match the substituted Type; omission means Unit |
| External parameter names | Match the original; not used to identify the target |
| Internal names | Ordinary parameter names may change using `external => internal: Type`; an inherited receiver remains self under §7.3 |
| Origin and Loan contract | Inherit binders and preserve every admitted Origin binding |
| Access, defaults, optionality, generic Constraints | Inherit; do not redeclare, strengthen, or weaken them |
| Attributes, Safety, calling convention, Effect requirements | Inherit existing rules; no attributes or additional modifiers on the specialization declaration |

Write an inherited optional parameter without `?` or a default. Callers still use the original omission and default-evaluation rules:

```kimi
func find<T>(value: T, count?: i32 = 1) -> () => ()
specialize func find<i32>(value: i32, count: i32) -> () => ()
find<i32>(10) // Evaluate the original default, then call the specialization.
```

The restriction concerns the specialization declaration, not ordinary syntax inside its body. A specialization of an unsafe function inherits its Safety contract, but unsafe operations still need an Unsafe Block. Contract inheritance introduces no unsupported async, exception-Effect, or variadic feature.

Validate all necessary Type Origins before comparison. After matching inherited binders, the body must work for **every Origin binding permitted by the original contract**. Do not add an Origin parameter or lifetime restriction, narrow applicability by Origin, or register another implementation for different Origins. A specialization cannot rescue an invalid ordinary implementation or replace a declaration-required proof; [deferred generic checking](#810-generic-body-checking-and-deferred-obligations) retains its stated design boundary.

Inferred ObjectCompatible is a [common implementation guarantee](12-expressions.md#12443-implementation-families), not an additional declaration obligation inherited from the ordinary body. A specialization may make that public guarantee NotProven without being invalid for that reason; declared Signature, Constraints, Safety, and Effect requirements still apply.

### 8.8.3. Selection and declaration ownership

Use the [Implementation Selection Key](21-layout-runtime-and-code-generation.md#2132-identity-and-generation-keys): the original function's declaration Identity and its ordered, normalized static generic arguments with only Origins excluded. Preserve nominal identity, nested structure, and every Semantics layer, including an outer object handle. `obj/D` and `rc/D` therefore have different keys even when they identify the same dynamic payload Type. Two specialization declarations with the same key are an error.

Calls and function references first use ordinary lookup, overload resolution, inference, and the original contract's Type, Constraint, Origin, and Loan checks. Specializations never enter the candidate set or supply inference evidence. Once the static generic arguments determine a key, use its matching specialization, or the ordinary implementation if none exists. Do not inspect a value's Dynamic Type, including behind a base or Contract View, to change this selection.

```kimi
// classify and its i32 specialization are declared above.
func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let number: i32 = 10
let result = forward<i32>(number@ref) // 1, including with shared generic code.
```

A generic caller cannot be fixed to the ordinary implementation merely because its generic arguments were initially unknown. This guarantee is independent of code sharing, separate compilation, optimization level, and LTO. Function references obey the same choice and their existing restrictions, including the ban on unsafe function values.

There is no direct name for the specialization or syntax to bypass it and call the ordinary body. Calling the same function with the same generic arguments from its specialization selects that specialization again and is recursive; move common work to a helper. A specialization-body error never falls back to another body or overload.

Specializations belong to the original function's Kotonoha and declaration Container. Existing Container fragments may put them in another file; unrelated extensions and other Kotonoha libraries cannot add or replace them. The defining Kotonoha closes the specialization set after environment selection and declaration collection, including generated sources, before finalizing affected call targets and no later than artifact finalization. Declaration and loading order cannot affect selection. Verification, artifact information, and invalidation follow [generic code generation](21-layout-runtime-and-code-generation.md#213-generic-code-generation).

## 8.9. Generic access effects

Before finalizing ownership/Loan analysis for an acquisition or adaptation, determine its **Access Effect** statically and uniquely:

```text
Access Effect
├─ acquisition: Copy / Move / Consume
├─ Loan action: shared Borrow / exclusive Borrow / Reborrow
└─ target Place and Origin dependencies
```

A broad Borrow-versus-acquisition category is insufficient: distinguish Copy from Move, shared from exclusive, and Borrow from Reborrow. Symbolic generic Types/Origins are allowed if the required effect and dependencies are uniquely expressible. Apply this rule to ordinary acquisition and all generic `@s`, `@s/T`, and `@Type` forms.

```text
Generic analysis
├─ effect known -> analyze normally
└─ legitimate dependency -> retain obligation
                            -> constraints / instantiation
                            -> determine effect -> finalize ownership and cleanup
```

Do not treat unresolved Copy capability as proof of non-Copy or fix the effect to Move. At definition checking, prove legality for every effect admitted by the declared Constraints. The exact effect may remain symbolic until instantiation only if every admitted case is legal, including subsequent uses, Loans, and cleanup. This is delayed effect determination, not delayed discovery of a required capability. Environment-changing directives still obey their earlier [selection deadlines](19-compile-time-directives.md#194-name-resolution-boundary).

Instantiations may have different effects. Substitute the already-verified effect plan and derive each concrete body's cleanup; never reuse a different-effect analysis without validation. Compile-time directives do not test Types or select Access Effects. The [generic verification principle](#810-generic-body-checking-and-deferred-obligations) requires the ordinary body to be valid independently of explicit specializations.

```kimi
// s is a declared Semantics parameter; value is an initialized owned value.
value@s
use(value)
```

Discarding the first result ends its shared Loan for `s = ref`; `s = uniq` also requires exclusive writability. Check the later use after that Loan ends. If Constraints admit non-Copy `s = owner`, the first use Moves the source and makes the definition invalid, even if current callers all use Copy values. Require Copy evidence or restrict Semantics and prove borrow permissions. If the result is retained, check subsequent uses throughout its Loan lifetime.

## 8.10. Generic body checking and deferred obligations

**Universal body verification.** An ordinary generic body must be semantically valid for every well-formed Type/length/Origin argument binding satisfying its declared Constraints, any enclosing conditional-conformance premises, and public Signature requirements. Verify this before accepting or exporting the definition, including definitions with no uses. Use the limited proof system of §8.7 and symbolic Type/Origin/effect rules; do not enumerate available Types or infer a hidden capability Constraint from the body. Failure to establish the required proof is a definition error. Explicit specializations cannot rescue an invalid ordinary body.

This requirement fixes meaning, not a physical compiler-pass schedule. Dependencies on other declarations may delay checking within the build, but an unverified definition cannot be accepted merely because selected concrete instantiations succeed. In particular, a generic call to another generic function must prove that function's declared requirements from the caller's declared premises.

For unknown Copy, an acquisition that is legal as either Copy or Move may keep a conditional effect plan. A subsequent read requiring the source to remain Initialized must be legal in both cases; otherwise require explicit `T is Copy`, a borrow that avoids acquisition, or a valid reinitialization before reuse. The conservative state is usable for proof, but the emitted operation must still Copy a Copy Type and Move a non-Copy Type. Never silently turn a possible Copy into a Move, and never add `T is Copy` to a caller's applicability conditions after checking the body.

~~~kimi
func transfer<T>(value: T) -> T => value // Valid for both Copy and Move.

func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func invalidTwice<T>(value: T) -> (T, T)
    return (value, value) // Error at definition: Copy is not guaranteed.
~~~

Generic stored acquisition and custom/computed/required getter results retain their declared Types (§11); none uses a Copy-dependent getter-result family. Copy/Move effects may remain conditional only after all cases are verified. Shared sequence and Pattern reads retain their separately defined correlated result families (§4.6.6); do not generalize Field acquisition to those operations.

A **Deferred Obligation** records remaining substitution or representation work for a verified definition, not an unproven body capability. Record its kind, defining bindings/environment, source location, declared premises, symbolic proof/effect plan, dependencies, and deadline. Unknown names, missing conformance, use-after-Move possibilities, and unresolved overload ambiguity are not deferrable until a favorable instantiation.

| Stage | Required work |
| --- | --- |
| Definition | Resolve definition-side names and roles; prove body Type correctness, selected operations, capability requirements, and symbolic ownership/Origin/cleanup legality under the declared contract |
| Permitted dependency | Retain proved symbolic Type/effect families and explicit representation obligations; Unknown is neither success nor evidence of non-Copy |
| Instantiation | Check the call's declared contract and ordinary argument/Loan validity; substitute verified plans; resolve concrete layout, representation, and exact effects without adding semantic use conditions |
| Finalization | Discharge representation obligations and complete concrete ownership/cleanup plans before lowering executable operations; never retry committed lookup or overload selection |

For example, using the target projection T as a local Type inside `func f<s/T>(x: s/T)` must be justified by the declaration's Constraints and slot rules. If an admitted binding could make it an Object View Target rather than a value Type, reject that use at definition time; do not wait to reject only the affected callers. Positions that prohibit generic parameters outright, such as a base Type, remain prohibited.

**Public dependent obligations.** Only Type/Origin well-formedness conditions implied by the written Signature, generic schema, associated-Type requirements, Constraints, and published conditional-member premises may restrict semantic applicability. They must be available to callers and artifacts without inspecting a private body. Such conditions are fixed when the declaration is checked; they cannot contain a newly inferred body requirement such as Copy, an extra Contract, or a favorable acquisition case. This revision introduces no separate source syntax for arbitrary hidden requirements. A need that cannot be expressed or proved with the existing contract makes the definition invalid.

Concrete layout/representation validity may still depend on substitution or the prepared target, including finite representable storage for an otherwise well-typed body local. Record such dependencies in the definition artifact with their source and target requirements before clients instantiate it. These checks concern representability only and cannot disguise a Type, capability, or lifetime restriction. A call satisfying the public contract must not fail later because its callee newly discovers a semantic body requirement. Ordinary caller-side initialization and Loan checks, specified runtime checks, target representation failures, and documented compiler resource exhaustion remain distinct; resource exhaustion is not semantic invalidity.

ObjectCompatible (§12.4.4) is a completed public operation guarantee used by projection/object-call legality, not a new conditional applicability premise or a deferred body requirement. Verify its common implementation family before publication; caller-specific instantiations cannot strengthen it. Changes use §21.3.4's dependency revalidation.

Diagnostics for definition errors identify the body use and missing declared proof; instantiation diagnostics identify the already-published dependent/representation obligation, definition site, arguments, and failed condition. Preserve verified summaries and plans in compile-time metadata even when runtime keys erase Origins. Do not add caller aliases/extensions, redo overload choice for favorable concrete Types, or use obligation strength to rank candidates. Environment directives provide no Type/capability evidence and retain their earlier selection deadlines.
