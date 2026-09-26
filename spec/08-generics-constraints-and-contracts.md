# 8. Generics, constraints, and contracts

[Specification index](../SPEC.md)

Generic parameters describe the admitted Types and capabilities. Constraints establish the proofs available to a generic body. Contracts name capabilities and map their requirements to implementations. Explicit specialization changes implementation selection, not the original callable contract.

## 8.1. Generic Type parameters

This section defines Type slots. Function length slots use the explicit `<length N>` form of [length parameters](04-arrays-indexing-and-slices.md#44-function-length-parameters); a plain `<N>` is a Type slot. A length slot accepts no complete Type and is not decomposed as a pair.

### 8.1.1. Slots and projections

Both parameter forms consume **one complete Type argument**. Binding preserves its Semantics, nested Types and all Origins:

```text
<T>    : T = W
<s/T>  : WholeType = W
         s = OuterSemantics(W)
         T = DirectTarget(W)
         o = OuterOrigin(W)       // Present only for an outer safe borrow.
```

`WholeType`, the projection functions and `o` are explanatory notation, not source bindings. `<s/T>` declares only `s` and `T`; the original `s/T` keeps `W`'s Origins without naming them. Source Origin names use the separate [Origin schema and naming rules](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations).

An ordinary `T` denotes a complete value Type, not only a bare Core. The pair's `T` has the fixed internal kind **SemanticsTarget**: a complete value Type or an Object View Target. Using it as a standalone value Type in a generic body requires proof of that role under the declared Constraints at definition checking; only the remaining proven symbolic substitution may be a [deferred obligation](#810-generic-body-checking-and-deferred-obligations). Its kind does not change at instantiation.

Before projection, transparent aliases, resolved associated-Type projections, grouping and redundant `owner/` are normalized. Alias cycles are rejected, and nominal declaration identity and parameter Binding Identities are kept; there is no nominal-alias syntax. Only the outer layer is split: `ref/(uniq/i32 during b) during a` yields `s = ref`, `T = uniq/i32 during b` and outer Origin `a`. Since `owner/V` is `V`, `owner/(ref/i32 during a)` has outer Semantics `ref`.

| Outer Semantics | Direct target, and requirements for applying these Semantics to another `U` | Outer Origin |
| --- | --- | --- |
| `owner` | `DirectTarget(W)` is `W` itself; `owner/U` is `U` for any valid complete value Type | None |
| `ref`, `uniq` | Complete Referent Type; `uniq` also requires exclusive acquisition and Loans at use | Required |
| `obj`, `rc`, `arc` | Object Target (§8.4.7.2) | None |
| `objref`, `objuniq` | Object Target (§8.4.7.2); borrowing requirements are preserved | Required |
| `unsafe` | Complete pointee Type; adds no safe-borrow lifetime guarantee | None |

These rows do not extend the current runtime-Contract or callable restrictions. The absence of an outer Origin does not erase payload dependencies: in `ref/(View<i32>{v}) during b`, `v.source` belongs to the inner Type and `b` to the outer borrow.

Forming an object form over a generic target requires [Object Target](#8472-objectpayload) evidence at definition checking, in signatures and bodies alike. For the pair's own `T`, **pair evidence** suffices: the [admitted Semantics set](#87-constraint-proof-system) of `s` is contained in the object family (`obj`, `rc`, `arc`, `objref`, `objuniq`). The caller-formed `s/T` itself needs no evidence. Applying `s` to another `U` requires `U` to be an Object Target whenever the admitted set of `s` meets the object family, and every admitted Semantics must still form a valid Type with `U` (a View Target `C` admits `obj/C` but not `owner/C`). `T is Sealed` is not object-formation evidence (§8.4.7.1). No evidence is derived from the well-formedness of a written signature, and evidence never flattens nested Type or Origin layers.

Within one parameter list, every bound name must be distinct: `<T, T>`, `<s/T, s/U>` and `<s/s>` are errors. References use Binding Identity under the ordinary scope rules. `<s>` is an ordinary Type slot; there are no standalone Semantics slots. Built-in forms such as `Callable<ref, S>` have their own grammar and are not general Semantics arguments.

### 8.1.2. Reconstruction and Origin annotations

After transparent alias expansion, the original pair expression `s/T` refers to its WholeType `W`. The declared bindings determine this correspondence, not two targets becoming equal after instantiation. `s/U` with another binding applies only the Semantics kind to `U` and does not copy `W`'s outer Origin. Once formed, equal complete Types have no identity differences from their construction history.

An explicit Origin annotation follows the ordinary Type-formation rules. Otherwise the original `s/T` keeps `W`'s Origins, and another `s/U` uses the [position-specific Origin rules](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision). Storing a Type in a pair slot grants no additional omission permission.

For `W = ref/i32 during a`, `s/T during b` forms `ref/i32 during b`. Formation requires no relationship between the old outer Origin `a` and the new `b` and performs no value conversion, but it still validates the binding of `b`, its annotation position and the formed Type's own inner-Origin outlives constraints. Fitting an actual `{a}` value to this Type separately checks the permitted shortening (`a : b`), acquisition and Loans. In particular, `uniq/V` keeps `V` invariant and any required Move or Reborrow; an annotation neither creates nor removes a Reborrow.

For another target `s/U`, omitted Origins follow the position rules symbolically even when `s` is unknown. A direct input records, at definition, an independent outer-Origin slot that is active only when `s` is a safe borrow. Otherwise the slot contributes no binding or constraint; all dependencies of `U` remain, and `owner/U` normalizes to `U`. Fields and nested borrow layers gain no new omission permission. Type formation and body legality must hold for every admitted binding. An explicit `s/T during a` or `s/U during a` requires the admitted Semantics set of `s` (§8.7) to be contained in the `borrow` category. The original unannotated `s/T` keeps its WholeType and receives no new Origin.

Conditional slots and result plans are kept in the [canonical contract](15-ownership-and-lifetime-analysis.md#1537-canonical-contracts-and-verification). Instantiation substitutes that plan; it introduces no binders and discovers no missing definition proofs.

### 8.1.3. Argument validity

**WellFormedGenericTypeArgument(A)** requires a valid resolved complete Type, or a legitimately dependent Type with retained obligations. Access, Semantics application, generic and associated-Type arguments, Origin bindings, relations and Constraints are checked before any Origin-erased comparison key is used.

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

An explicit argument list supplies every slot in declaration order, with an optional trailing comma. Omitting the entire list uses only the inference that the construct supports. There are no partial lists, `_` placeholders, defaults, variadic slots or general Const generics; only [function length slots](04-arrays-indexing-and-slices.md#44-function-length-parameters) admit length arguments. `<s/T, U>` takes two arguments, such as `<ref/i32 during a, string>`; `<ref, i32>` cannot supply one pair. Origin parameters have a separate schema and consume no Type argument slots.

A valid Type argument is not permission to use it in every role: the constraints on base Types, associated Types, finite layout, Copy derivation and object payload erasure still apply. Ordinary storage preserves complete Type-argument dependencies under the [storage contracts](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision), without an Owned or Storable requirement. Static storage instead requires Owned and the [static-source rules](11-properties.md#1132-static-storage).

## 8.2. Constraints

| Term | Meaning | Example |
| --- | --- | --- |
| **Contract** | A Declaration Container declared with `contract` that specifies a named capability a Type provides. | `contract Comparable` |
| **Constraint** | A condition imposed on a Type or Type Semantics. | `T is Comparable` |
| **Constraints** | The set of conditions required for a declaration to be valid or usable. | The leading clauses of a generic function or structure. |
| **Conformance** | A Type's fulfillment of a Contract, with the required correspondence between requirements and implementations. | A Type fulfills `Comparable`. |

A **Constraint Clause** expresses a Constraint in the form `subject is requirement`. All clauses of a declaration's Constraints must hold, and they establish capabilities that the implementation may use. Subjects include complete Types, Semantics, valid target projections and `Self` (the enclosing Type), according to each requirement's role; each declaration kind restricts the permitted subjects (see [function Constraints](07-functions-and-callable-values.md#74-function-constraints)). In a struct or enum declaration, `Self is C` declares conformance (§8.4.4). `Self is not C` is permitted there only as the opt-out of an intrinsic requirement that defines one, currently [ObjectPayload](#8472-objectpayload); any other negated `Self` clause in a Type declaration is an error.

A Type requirement may name a capability declared with `contract` or another compile-time Type capability. A Semantics requirement may name a concrete Semantics, such as `ref` or `obj`, or a Semantics category. Requirements combine with `and`, `or`, `not` and parentheses under the [requirement-expression rules](#83-requirement-expressions).

A `struct` header may contain generic parameters and an Origin list. Its ordinary Constraints precede the members, and conditional conformances may appear at member positions (§8.4.8).

```kimi
struct Container<s/T> {owner, source}
    T is Comparable
    s is reference

    var value: s/T
```

These clauses constrain the pair's target projection `T` and its Semantics projection `s`; each requirement must accept its subject's role. Constraints may also apply to the enclosing Type:

```kimi
struct ComparableContainer<T>
    T is Comparable
    Self is Comparable

    var value: T
```

`T is Comparable` supplies comparison capabilities for the stored value's Type. In a Type declaration, `Self is Comparable` is both a Constraint and a [conformance declaration](#844-conformance): it requires `ComparableContainer<T>` itself to fulfill `Comparable` and derives no implementation from the clause on `T`. (The required members are omitted here.) The built-in `Self is Copy` keeps its own [derivation rules](03-types-and-values.md#351-copy-capability-and-explicit-duplication).

## 8.3. Requirement expressions

`subject is requirement` tests a Type or Type Semantics in Constraint Clauses and in associated-Type declarations and specifications. Its result is a compile-time `bool`; an unresolved requirement never becomes a runtime test. Each context keeps its permitted subjects, grammar and validation rules. Requirement Tests are not allowed in `#if` or `#case` Conditions. Ordinary expressions use [runtime type tests](13-operators-and-assignment.md#1361-runtime-is-tests) instead. The syntax context, including through parentheses, fixes the interpretation before lookup, with no fallback between the Type and Value namespaces.

`is` binds on its left at comparison precedence, and its right side consumes a requirement expression through `or` precedence. An immediately following `not` negates the entire right side:

| Form | Meaning |
| --- | --- |
| `T is A and B` | `T` satisfies both `A` and `B`. |
| `T is A or B` | `T` satisfies `A` or `B`. |
| `T is not A or B` | `T` does not satisfy `(A or B)`. |
| `T is A and not B` | `T` satisfies `A` and does not satisfy `B`. |

Write `T is (not A) or B` to limit the negation to `A`. A complete Requirement Test cannot be embedded in an ordinary Boolean expression such as `(T is A) and enabled`; no source syntax provides such a mixed context. Use separate Constraint Clauses for requirements and environment directives for independently configured source selection. Value equality uses `==`.

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

This revision defines static conformance checking and generic use; runtime Contract Views remain a [future extension](#85-runtime-contracts). A Contract may declare its own **Type parameters** with the ordinary generic-parameter syntax, but no Origin or length parameters. It also inherits the enclosing environment under §6.1.3, including unused Type/Semantics and Origin bindings. The same Type-argument syntax is used at the declaration, at references, in parent lists and in conformance declarations, and it binds complete Types under the ordinary generic rules. `Indexable<isize>` and `Indexable<Index>` are therefore distinct bound Contract references with independent requirements and associated Types (§8.4.9). Separately specified built-in requirements such as `Callable<...>` keep their own grammar.

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> place(ref, Element) during self
```

### 8.4.1. Function requirements

A function requirement declares a Name, optional function generic parameters, implicitly introduced signature Origins (§15.3.4), explicitly typed parameters (the receiver may use the shorthand of §7.3) and an optional result Type or Place result (§7.1.1); an omitted result means Unit. A requirement with a receiver is an instance function; one without a receiver is a Type function. Receiver, parameter labels, ownership, Origins and safe/unsafe conditions follow the ordinary function rules.

Function-specific Constraints occupy an optional indented region immediately after the header. The region contains one or more Constraint Clauses and no executable statements, `return`, local declarations or single-item body. Its subjects are the function's generic parameters or associated-Type projections rooted in them. Contract-wide Constraints belong at the Contract body level.

```kimi
contract Factory
    func create<T>(value: T) -> Self
        T is Copy
```

Requirement Constraints are premises for checking implementation compatibility; they are not silently added to the implementation declaration. Same-name requirements with receivers in one Contract, including those inherited through refinement (§8.4.2), must share one receiver shape ([§7.3](07-functions-and-callable-values.md#73-explicit-receivers)); a Contract that violates this is a declaration error. Requirements permit the `!` argument-name boundary (§7.2) but prohibit defaults. A call through a requirement supplies every ordinary argument and follows the requirement's name-omission permissions; defaults or name-omission permissions on the implementation do not change this call surface. Requirements and required accessors have no [access modifiers](09-names-signatures-and-access.md#934-conformance-accessibility) of their own.

Property requirements use `has`; their selection and compatibility rules are in [Contract Property requirements](11-properties.md#114-contract-property-requirements).

### 8.4.2. Refinement

`contract C: A, B` refines every listed parent Contract. Parents resolve as bound Contract references (§9.6.1), which may carry arguments on outer path segments and the parent Contract's own Type arguments (§8.4), as in `contract UniqIndexable<Key>: Indexable<Key>`. All requirements, associated Types and Constraints are inherited. Parent order gives no priority, and direct or indirect cycles are errors. `Self is C` does not declare refinement inside a Contract.

```text
Source                 Sized
  Element, read()        count
       \                 /
             SizedSource
               reset()
```

Conformance to a child entails conformance to every ancestor, and inherited `Self` still denotes the final conforming Type. A child may add requirements or Constraints but cannot remove or weaken inherited ones.

Paths to the same ancestor declaration with the same normalized bindings introduce one Requirement Identity or associated-Type identity. Distinct bindings remain distinct requirements, and all path conditions and validation obligations are preserved (§8.4.9). Independent declarations from different parents remain distinct even when their names match; one compatible implementation may satisfy several requirements.

A refinement is rejected when its requirements cannot coexist as separate implementations under the ordinary declaration rules and provably cannot be satisfied by one implementation. The check uses both the [implementation-identification key](#845-implementation-matching) and the ordinary [Signature](09-names-signatures-and-access.md#91-signatures) rules: labels affect matching but cannot by themselves distinguish overloads. Different result Types alone do not prove a conflict; the defined result compatibility, including the subtype and Never rules, applies. Genuinely dependent checks wait until their prerequisites resolve; Unknown is not a proof of contradiction.

### 8.4.3. Associated types

An associated Type denotes a **complete Type**: Semantics, Type arguments, nested structure and internal Origins, like a generic Type argument or `Self`. Semantics compose layers and never overwrite an existing layer. Binding a complete Type into an associated Type, projecting it or expanding an alias neither rebinds its Origins nor supplies `static`.

`associate` declares a new associated Type inside a Contract and specifies an existing associated Type inside a conforming Type. Both forms use `is`, never `=`:

| Form | Meaning and location |
| --- | --- |
| `associate Element` | Declares an associated Type in a Contract. |
| `associate Element is Equatable` | Declares it with a capability requirement, or constrains the uniquely identified associated Type in an implementation. |
| `associate Element is i32` | Declares it with a fixed Type, or specifies that Type in an implementation. |
| `associate C.Element is T` | Specifies identity with a Type `T` for the associated Type declared by `C`. |
| `associate LentItem(step)` | Declares an associated Type with an Origin parameter (below). |
| `associate C.LentItem(a) is ref/E during a` | Specifies an Origin-parameterized associated Type; `a` is the implementation's parameter for the first requirement parameter. |

Each associated Type is determined uniquely by explicit specifications and Type-identity Constraints on the Contract or its ancestors. Bindings are never inferred from implementation signatures, member search, function bodies, implicit adaptations, Copy judgments or instantiated Types, and a Type is never chosen merely because it satisfies a capability; contradictory identity facts from several requirements are rejected. Bindings are substituted before implementations are matched. For example, an unconstrained `Source.Element` is not inferred as `i32` merely because an implementation of `read` returns `i32`.

**Qualified specifications.** `associate C.Element is T` selects an associated Type through a direct conformance or its ancestor `C`; `C` may carry the Contract's Type arguments. An unqualified `associate Element is T` is valid only when exactly one distinct associated-Type declaration with that name is available across those conformances and refinements; several paths to one declaration count once. Ambiguous names require qualification, and a short form never applies to all same-named declarations. A bare `associate Element` is not an implementation specification.

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

**Projections.** `T.(C).Element` is the associated Type of `T`'s conformance to the bound Contract reference `C`, and `T.(C).LentItem(a)` also applies Origin arguments (§8.4.3.1). `T.Element` is the short form; outside a Contract it is valid only when the requirement is unique under the available Constraints, and inside a Contract a short name refers to that Contract's own or inherited requirement. `C` is resolved by ordinary Contract-name and alias lookup, not by member lookup on `T`, and an unparenthesized `T.C.Element` is an ordinary qualified path, never a projection. Conformance evidence is required and is never discovered by searching for a same-named Contract. The Type-side base may be a named or constructed Core, a parameter, `Self` or another associated-Type projection. A projection is not a value or a Semantics-applied expression; a projected complete Type keeps its Semantics and does not become a Core qualifier merely because it is a projection.

```kimi
func readOne<T>(source: ref/T) -> T.(Source).Element
    T is Source
    return source.read()

func readInt<T>(source: ref/T) -> i32
    T is Source
    T.(Source).Element is i32
    return source.read()
```

Leading function Constraints are collected before the projections in the signature are resolved (§7.4). Type context fixes a projection's namespace, and the dotted syntax is kept until Binding resolves the Contract and associated-Type roles. Distinct successful interpretations are ambiguous; neither expected results nor fallback to value-member lookup resolves the ambiguity.

**Refinement Constraints.** A child may constrain an inherited associated Type through `Self.(C).Element`, where `C` is an ancestor. In a Contract-body Constraint subject, a bare associated-Type name is also permitted when it is unique among the own and inherited declarations. These clauses constrain existing declarations; they create no replacement Types or conformances. A child may also refine an inherited Origin-parameterized requirement with `associate Parent.LentItem(a) is E`, which requires the equality for every `a` in the parent's domain; `Iterator` does this with `associate LendingIterator.LentItem(step) is Item` (§22.1.2.1). `E` need not use `a`, but the actual Loan independence of values is checked separately (§15.6.3).

```kimi
contract Equatable
    func equals(self: ref/Self, other: ref/Self) -> bool

contract OrderedSource: Source
    Self.(Source).Element is Equatable

contract IntSource: Source
    Self.(Source).Element is i32
```

An `IntSource` implementation need not repeat the inherited `Source.Element is i32` binding. Explicit Type-identity facts support substitution and normalization, and contradictory bindings are errors. Unresolved bindings remain obligations until the required finalization point. There is no associated-Type inference or proof search beyond the [limited proof rules](#87-constraint-proof-system).

#### 8.4.3.1. Origin parameters

An associated Type may declare **Origin parameters**, one or more simple Names in parentheses after its Name:

```text
associate LentItem(step)                          // A requirement
associate LentItem(step) is E                     // Fixed: an owned value, for example
associate LentItem(step) is ref/E during step
associate LentItem(step) is uniq/E during step
associate LentItem(step) is ref/E during source   // source is bound in the enclosing environment
```

A parameter is an Origin binder whose scope is the right-hand side of its declaration and the attached clauses. It is not visible to sibling requirements. A same-spelled Origin in a method signature is a different binder, which applies the associated Type explicitly, as in `Self.LentItem(step)`. Duplicate and hiding names follow the ordinary Origin naming rules (§15.3.4).

In Type context, `LentItem(a)` substitutes existing Origin atoms positionally for the parameters and denotes an ordinary complete Type. Arguments use the same atoms as `during`; an intersection is parenthesized, as in `LentItem((a and b))`. Declaration and application take one or more arguments, and their counts must match. Unapplied, partially applied, `_` and omitted arguments are invalid, and an unapplied family cannot be passed as a Type argument or aliased. Renaming a parameter does not change the Contract. An application is never a runtime call, a Type computation or a Place Type. Covariance of an associated Type is not assumed: the applied Type undergoes the ordinary variance, shortening and Reborrow checks.

**Formation conditions.** The enclosing Contract and the requirement's attached `origin` relations fix the domain of a new requirement. A requirement whose right-hand side is a fixed complete Type automatically publishes that Type's Origin well-formedness conditions; a capability requirement such as `is LendingIterator` fixes no Type. A requirement without a fixed Type may add an indented **`wellformed Type`** clause, which publishes the Origin formation conditions of `Type` without fixing the associated Type. `wellformed` is contextual only there. The structure, Semantics, capabilities and lengths of `Type` are first proven from the enclosing public Constraints; no capability is inferred, and a Place result is not a `Type`.

```kimi
// E is a complete Type parameter of the enclosing Contract.
associate View(a) is ref/E during a

associate LentItem(step)
    wellformed uniq/Self during step
```

The first publishes the condition that the observable Origins of `E` outlive `a`; the second fixes no Type and admits every `step` for which `uniq/Self during step` is well formed. A clause may also name an existing Origin, as in `origin source outlives a`. Formation conditions create no Loan or capability; actual borrows are checked separately.

Attached clauses are processed in the order of §15.3.3. Equalities first bind the unbound Origins of the right-hand side, which fixes the complete Type; the remaining conditions become the published domain. For example, `Borrowed<Self>{view}` with `origin view.source == a` defines the unbound `source` as `a`. Naming a set alone creates no Origin, and a bound Type is never rebound. A use proves the substituted conditions. An implementation or a refining requirement proves the remaining conditions and inherited obligations after binding its right-hand side; it can neither add nor strengthen call conditions from its right-hand side or body. A requirement with neither a right-hand side nor conditions admits every Origin the Contract allows. Closed contradictions, circular self-proof and treating Unknown as success are rejected. Conditions, bindings and formation obligations are static metadata; Origin parameters add no runtime representation.

#### 8.4.3.2. Origin application and binding sets

The role of a parenthesized or braced suffix is fixed by its syntactic position, never by whitespace, lookup results or Type-check success:

| Syntax | Meaning |
| --- | --- |
| `associate LentItem(a)` in a declaration or specification | Introduces Origin parameters |
| `Self.LentItem(a)` or `T.(C).LentItem(a)` in Type context | Applies existing Origins; the resolved requirement must declare corresponding parameters |
| `View<T>{v}` on a named Type | Names a binding set (§15.3.1); requires a nonempty known schema |
| `Self.LentItem(a){v}` | Names the binding set of an applied Type; valid only when its internal schema is known |

Only a parenthesized list directly after a named Type in Type context is an Origin application. Type grouping, Tuple and Function Types, and value-context calls keep their meanings and are never reinterpreted. Origin application on an ordinary Type, a wrong argument count and an unapplied family are errors. Braces are never read as positional Origin arguments.

### 8.4.4. Conformance

In a Type declaration, `Self is C` is both a Constraint and an explicit declaration of conformance to `C`. Same-named members alone do not register conformance. A negated `Self` clause never declares or denies conformance (§8.2).

```kimi
struct NumberSource
    Self is Source
    associate Source.Element is i32
    public func read(self: ref/Self) -> i32 => 42
```

**Conformance Identity** is the pair of the concrete Type identity and the Contract identity. Different associated-Type bindings do not create different conformances to the same Contract. An explicit conformance and a conformance implied by refinement for the same pair are one conformance; all simultaneously applicable associated-Type bindings and requirement-to-implementation mappings must agree under the path checks of §8.4.8. A redundant explicit parent conformance is allowed, and no warning is required.

An unconditional conformance of a generic Type must hold for every binding the Type Constraints allow; a conditional conformance uses the additional premises and use checks of §8.4.8. Successful selected instantiations do not validate an unconstrained definition. Ancestor conformances and all effective mappings are verified. Conformance declarations generate no implementations, except explicitly specified intrinsic derivations and the limited standard Property witness bridges of §11.4.2.

The verified requirement-to-Member Identity mapping and the associated-Type bindings are retained. Contract calls use this mapping instead of rediscovering members in the caller's source environment or after instantiation. There is no external registration, replacement conformance, default implementation or access-bypassing witness thunk.

**Inherited conformance.** A verified conformance `(B, C)` is inherited by `D : B` only if the retained mapping satisfies every requirement with the requirement-side Self replaced by `D`. A Member `M` declared in `A` keeps `A` as its implementation-side Self, even through `A -> B -> D`. The Type and Origin substitutions along `D`'s base path to `A` are applied, and associated-Type bindings are kept. Only an eligible borrowed receiver may use [base-subobject projection](09-names-signatures-and-access.md#951-base-subobject-receiver-projection); other parameters and results gain no base conversion, and owning receivers gain no slicing. The access, Origin, Effect and premise checks of §8.4.5 and the Property rules of §11.4 apply. Projected calls require published ObjectCallCompatible Proven (§12.4.4).

| Requirement shape | Inheritance through this path |
| --- | --- |
| `Self` only in a borrowed receiver, as in Utf8Format | Possible if all other checks and ObjectCallCompatible succeed |
| `other: ref/Self`, as in Equatable/Comparable | Fails: `ref/A` does not match `ref/D` |
| `func empty() -> Self` | Fails: `A`'s result does not supply `D` |
| An owning `Self` receiver, as in `IntoIterable.intoIterator` | Fails: no owning receiver projection |
| A fixed `Self.Element` that normalizes to the same Type | The normalized Types are matched; the spelling `Self` alone does not prevent inheritance |

One failed inheritance path does not invalidate `D`'s declaration. `(D, C)` is determined from all explicit, conditional and Contract-refinement paths under §8.7; it is Refuted only when all candidates are proven to fail. Unresolved generic dependencies remain Unknown until their deadline, and invalid declarations or inconsistent evidence are Error. Successful paths must agree on associated Types and implementation mappings. A new explicit conformance uses ordinary implementation lookup; same-named declarations do not replace inherited mappings.

Copy, Owned, Callable and other intrinsic capabilities keep their own derivation rules. In particular, struct Copy requires the derived declaration's own opt-in and Copy stored components; it is not inherited automatically.

No warning is required merely because an open base has a conformance that its descendants cannot inherit. When a derived `Self is C` or a Constraint use fails, the diagnostic identifies the requirement and the cause: Self mismatch, owning receiver, access, Origin or premise failure, or ObjectCallCompatible NotProven with the responsible implementation. An unresolved proof is diagnosed as Unknown, not Refuted. For example, an Equatable `Shape` may have a valid `Circle : Shape` that is not Equatable; requiring `Circle`'s conformance reports the `ref/Shape` versus `ref/Circle` mismatch and the inherited-Name prohibition. When descendants need such Self-dependent Contracts, implement them on leaf Types or use composition; the base's own conformance remains valid.

### 8.4.5. Implementation matching

Ordinary declarations are validated first: functions that differ only in result Type, Origins, Constraints or `unsafe` are duplicate declarations under the Signature rules (§9.1), before any conformance matching.

After `Self`, the conforming Type's arguments and the associated Types are substituted, a function implementation is identified by the following key:

| Component | Required match |
| --- | --- |
| Name | Exact name. |
| Function kind | Type function or instance function. |
| Function generic parameters | Same count, kinds and order; they correspond by position, not spelling. |
| Ordinary parameters | Same count, order and external labels; internal names and K need not match. |
| Receiver | Same presence and normalized Type structure, with only the inherited receiver correspondence allowed by §8.4.4. |
| Parameter Types | Same normalized Type structure. |
| Result category | Value result or Place result with the same mode (§7.1.1). |

Type structure includes resolved Core identity, Semantics, nested structure, Type arguments and the substituted complete associated Types, including Origin-parameterized ones matched by parameter position. Origin names, bindings and lifetime relations are excluded from identification but kept for compatibility. Identification uses no parameter-structure contravariance, no Function Type contravariance and no call-site implicit adaptation. Implementations are not ranked by call overload preferences, adaptations, omitted arguments, Origins, Constraints, conditional-member applicability or Effects; unlike direct-call applicability (§8.4.8), conditions are checked after identification.

These rules identify Contract implementations; `Callable` and common Function Types keep their separate [callable signature compatibility](10-overload-resolution-and-inference.md#107-callable-signature-compatibility) rules.

Zero candidates means a missing implementation; several candidates are ambiguous, even if only one would pass compatibility. For exactly one candidate:

| Aspect | Compatibility obligation |
| --- | --- |
| Constraints | The Type/conformance and requirement premises must prove the implementation's Constraints and any conditional-member premises. |
| Input Origins | Every input allowed by the requirement remains valid; no stronger lifetime precondition. |
| Result Type | The same Type or a subtype permitted by the Type rules. |
| Result Origins | At least the required lifetime guarantees; a Place result may publish a stronger guarantee, such as `during self.source` for a required `during self`. |
| Access | Usable throughout the [conformance's effective domain](09-names-signatures-and-access.md#934-conformance-accessibility). |
| Calling context and Effects | No stronger calling context or effects than the requirement permits. |

`BufferWriter.reserve` has the [formatting profile's effect upper bound](utf8-formatting.md#12-effects-and-erasure); its complete transitive summary, including lazy initialization and destruction, is checked at conformance. Erased adapter calls use that bound; same-spelled user Contracts receive no special effect guarantee.

Origin contracts use the [common compatibility procedure](15-ownership-and-lifetime-analysis.md#1537-canonical-contracts-and-verification), preserving ordinary variance and Loan rules, including invariance where required. A requirement that admits a call-local borrow cannot be implemented by a function that requires that input to be `static`. Origin-free identification neither erases dependencies nor relaxes exclusive access.

The existing Safety, ownership, Origin and Access Effect checks apply; no new effect system is defined here. A safe requirement cannot require an unsafe calling context. Result compatibility inserts no numeric or user conversion, Copy, Borrow/Reborrow or erasure. Core inheritance alone does not prove compatibility of complete Types. A compatibility failure is reported as a conformance error without searching for another implementation. Properties use the corresponding [accessor rules](11-properties.md#114-contract-property-requirements).

Receiver adaptations in custom accessors and generated Contract witnesses distinguish proven complete Sealed payload calls from protected object and base calls (§12.4.4). Property permissions, right-hand-side-first assignment, witness identity and public premises are preserved; Sealed never changes ObjectViewCompatible.

### 8.4.6. Calls and shared requirements

Under `T is C`, generic code may use the requirements and associated Types of `C` and its ancestors. A Type function is called through the conforming Type, as in `T.empty()`, and needs no instance.

```kimi
contract EmptyConstructible
    func empty() -> Self

func makeEmpty<T>() -> T
    T is EmptyConstructible
    return T.empty()
```

`EmptyConstructible.empty()` is invalid because it identifies no implementation Type, which is never inferred backward from the expected result. A concrete call such as `Buffer.empty()` uses ordinary Type-member lookup; generic requirement calls keep their conformance mapping.

Distinct Requirement Identities may form one call candidate only when their exposed signatures and conditions are equivalent **and** their mappings select the same effective implementation Member Identity for every valid Type substitution the current Constraints allow. The comparison covers function generics, parameters and labels, normalized K (§7.2.2), receiver, results, Origins, Constraints and calling conditions, plus the implementation's Type substitutions and receiver correspondence. Equal code, runtime addresses or optimizer sharing supply no proof. The conformance requirements themselves remain distinct. Repeated paths to the same Requirement Identity are already one requirement and need no such proof.

```text
A.reset --+-- equivalent call contract and same mapping proved
B.reset --+                         |
                               one call candidate
```

Only the defined proof rules are used, not enumeration of instantiations or arbitrary theorem proving. If equivalence cannot be proven, the candidates remain separate and ordinary call selection applies; a non-unique result is ambiguous. A coincidental match in one instantiation cannot make an otherwise invalid generic call valid. There is no qualified-call syntax such as `value@A.reset()`; `@` keeps its adaptation meaning.

### 8.4.7. Intrinsic contracts and guarantees

`Copy`, `Owned`, `Callable`, `Sealed` and `ObjectPayload` are **compiler-intrinsic** Contracts. Each has only the special acquisition, destruction, layout, concurrency or code-generation effects explicitly defined for it. These effects belong to the compiler-recognized Contract identity; a user Contract with the same name or requirements does not gain them, so `Self is MyCopy` does not make a Type Copy. Compiler-derived conformance exists only where individually specified, and `Self is Copy` must pass its ordinary derivation checks.

The [required Kimi declaration table](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations) also fixes the identities and signatures of `Utf8Format`, `BufferWriter`, `Equatable`, `Comparable`, the iteration Contracts and the Indexable Contracts. Their source conformance follows the ordinary static Contract rules; their special behavior is limited to the specified formatting, buffer effects, comparison, iteration and indexing mappings.

Conformance proves only statically specified requirements. The type system does not enforce documented laws such as the symmetry or transitivity of equality. Conformance does not prove current initialization, absence of conflicting Loans, storage representation or direct Field access; ordinary usage checks and documented unsafe obligations still apply.

#### 8.4.7.1. Sealed

`Kimi.Sealed` is a compiler-intrinsic requirement. A normalized Type satisfies it exactly when its outer Semantics is `owner` and its Core is valid, is not Never and is not an open struct. A sealed Core admits no derived Types; Scalars, `string`, Unit, enums, Tuples, fixed arrays, collections, Function Items, concrete Closures and common Function Types are sealed. Callable and runtime Contract Views are requirements or views, not Cores.

Only the outer Core is tested: a non-open `Cell<T>` is Sealed even when `T` is open or contains borrows. `ref/X`, `uniq/X`, object handles and borrows, and raw pointers are not Sealed. Sealed implies neither Copy, Owned nor exclusive access. User conformance, implementations and same-spelled declarations cannot grant it.

Sealed is not object-formation evidence: forming `obj/T` or `objuniq/T` over a generic `T` needs Object Target evidence (§8.4.7.2), and a Sealed Type may still opt out of ObjectPayload. Proof uses the ordinary conjunction, disjunction and negation rules of §8.7; Unknown is not false, and `T is not Sealed` proves neither `owner` Semantics nor an open Core.

```kimi
struct Cell<T>
    public var value: T

func readPayload<T>(source: objref/T) -> ref/T during source
    T is Sealed and ObjectPayload   // Sealed for the dereference, ObjectPayload to form objref/T
    return source@deref@ref
```

#### 8.4.7.2. ObjectPayload

`Kimi.ObjectPayload` is a compiler-intrinsic requirement stating that a value of the Type may become the payload of a new object. A normalized Type satisfies it exactly when its outer Semantics is `owner`, its Core is a valid complete Core other than Never, and neither the Core's nominal declaration nor any of its bases opts out. Open structs, Scalars, `string`, Unit, enums, Tuples, fixed arrays, collections, Function Items, concrete Closures and common Function Types all qualify. Only the outer Core is tested: `Box<Parser>` satisfies it whenever `Box` does not opt out. Callable requirements and runtime Contract Views are not Cores and do not satisfy it. Object ownership does not require heap allocation (§3.3.3); these rules govern Type formation and creation whatever the representation.

**Opt-out.** A struct or enum declaration opts out by writing `Self is not ObjectPayload` in its leading Constraint region. The clause is unconditional (no `when`), stands alone (no `and`/`or`), may not be repeated in one declaration, and must resolve to `Kimi.ObjectPayload` (qualify it when a user declaration shadows the name). It is a declaration, not a conformance obligation or a proposition to verify: inside the Type and its derived Types, `Self is ObjectPayload` is Refuted. Derived structs inherit the opt-out; restating it is redundant but allowed, and no positive clause restores the capability. Users cannot grant ObjectPayload: `Self is ObjectPayload` in a Type declaration is an error. Just as `Self is Copy` grants Copy, `Self is not ObjectPayload` renounces ObjectPayload; both are declarations about intrinsic capabilities.

**Proof.** When the outer structure the definition needs (outer Semantics, Core and opt-out state) is determined, the judgment is direct even with unbound Type arguments: `Box<T>` is Proven when `Box` does not opt out, an opted-out `Parser<T>` and `ref/T` are Refuted, and an invalid Type is Error. The outer Semantics of a pair's own `s/T` is judged from the admitted set of `s` (§8.7). Where the outer structure is undetermined (Type parameters, pair targets, unresolved associated projections), the only evidence is a declared premise `T is ObjectPayload` or its derivation by Contract refinement (§8.7); `T is Sealed` does not imply it. A Proven `T is ObjectPayload` establishes that `T` is a complete value Type with `owner` outer Semantics, which also supplies the value-Type role of a pair target (§8.1.1). It implies neither Copy, Owned, Sealed nor any thread-transfer property.

**Object Target.** A Type `X` is an Object Target when `X is ObjectPayload` is Proven, when `X` is a valid runtime Contract View Target (§8.5), or when `X` is the target `T` of a pair `<s/T>` whose admitted Semantics set is contained in the object family (pair evidence, §8.1.1: the caller's valid `s/T` already establishes the target). Forming `obj/X`, `rc/X`, `arc/X`, `objref/X` or `objuniq/X` anywhere requires `X` to be an Object Target: in signatures, Fields, locals, Type arguments, upcast and checked-cast results, `Weak<rc/X>`, and the object form implied by a runtime `is` test (§13.6.1). Pair evidence and View Targets do not prove ObjectPayload. Creating a new object from a value (§13.5.8, and any future value-to-View erasure, §8.5) requires ObjectPayload itself. Operations on an existing handle, such as borrows, view changes and casts, re-prove nothing; they are checked by result-Type formation and their own conditions, such as `Supports` for upcasts, Owned for the first erasure and the Loan rules.

An opted-out Core is never an Object Target. Every object form over it is rejected, including object receivers and Fields inside its own declaration, creation, upcasts, casts, `is` tests and Weak handles, and it cannot conform to a Contract whose environment proves `Self is ObjectPayload`. Nothing else is affected: values, `ref`/`uniq`/`unsafe`, storage as a Field, payload, Tuple or element of another Type, objects over such containing Types (`obj/Box<X>`), Closure captures, common Function Types, static storage, and the Copy, Owned and Sealed judgments. The opt-out prevents direct object creation; it does not guarantee that a value never reaches the heap. Because generic bodies are verified once (§8.10), instantiation never discovers an opt-out; callers meet the declared `T is ObjectPayload` requirement instead.

**Contracts.** Inside a Contract, `Self is ObjectPayload` and `Self is not ObjectPayload` are Self-dependent implementation requirements (§6.1.3.1) and change no opt-out. A Contract whose requirement signatures form object forms over `Self` must have `Self is ObjectPayload` Proven in its Constraint environment, declared directly or inherited by refinement (§8.4.2); a user deriving `T is C` obtains `T is ObjectPayload` by Contract refinement without restating it. A positive and a negative form in one environment are contradictory evidence (§8.7). An opted-out derived Type fails the inherited-conformance path of such a Contract (§8.4.4); the base's own conformance stays valid.

```kimi
struct Parser
    Self is not ObjectPayload
    public var pos: i32 = 0

func boxed<T>(value: T) -> obj/T
    T is ObjectPayload
    return Kimi.Intrinsics.makeObj(value@move)

func inspect<s/T>(handle: s/T) -> i32
    s is object                          // Admitted set {obj, rc, arc}: pair evidence for T.
    let view: objref/T = handle@objref   // No ObjectPayload needed.
    return 0

contract Shape
    Self is ObjectPayload
    func area(self: objref/Self) -> f64

// let o = boxed(Parser.init())   // Error: Parser is not ObjectPayload.
```

**Compatibility.** The opt-out is part of a declaration's public summary (§18.7, §21.3.4). Adding an opt-out or a `T is ObjectPayload` requirement invalidates dependents. Removing an opt-out changes a published capability whose negative results dependents may have used, and removing a requirement widens a candidate's applicability (§10.1); dependents are revalidated in both cases.

### 8.4.8. Conditional conformance

A generic struct or enum may declare `Self is C when P`. Its ordinary Type Constraints `D` govern every use of the Type, while `P` governs only this conformance to `C` and does not constrain Type formation. Normal Type, storage, Semantics and Origin validity still apply.

```kimi
enum Option<T>
    Self is Copy when T is Copy
    Some(T)
    None
```

`Option<Resource>` remains a usable Type when `Resource` is Non-Copy, and this declaration makes `Option<i32>` Copy. In contrast, a leading `T is Copy` would restrict every use of the Type, and an unconditional `Self is Copy` would promise Copy for every binding `D` admits.

#### 8.4.8.1. Conditions and implementation scope

The declaration targets one Contract and appears at a Type member position; ordinary Type Constraint Clauses still precede the members. `when` is contextual only here. Conditions are comma-separated `subject is requirement` clauses, all of which must hold. Subjects are the enclosing generic parameters, their Semantics and target projections, and associated-Type projections valid under the existing rules. Requirements must accept their subject's role, and no new parameters are introduced.

Conditions allow positive requirement atoms joined by `and` and parentheses, with the existing Requirement Test interpretations. `or`, `not`, value tests and arbitrary Boolean expressions are rejected, even inside parentheses. This restricted grammar does not change the ordinary PositiveRequirement.

```kimi
Self is C when T is A and B, U is D
// Requires T is A, T is B, and U is D.
```

An optional indented implementation block declares members in the enclosing Type's namespace. It is a condition scope, not a new Type or Value namespace. It allows functions, computed members where the Type kind permits them (enums still forbid them), and associated-Type specifications; it rejects Fields, enum Cases, constructors, `deinit`, nested Types and nested conformance declarations. Conditional conformance never changes storage layout or Case structure.

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

At definition, `C` is verified under `D` and `P` for every admitted binding. Implementations are identified by §8.4.5 (Property implementations by §11.4), not by call overload ranking. Each requirement's premises are added when proving the selected implementation's published conditions, access, Origins, ownership and Effects. A compatibility failure never retries a different implementation. Requirement-to-Member mappings, Field bridges where applicable, and associated-Type bindings are fixed and retained at this stage.

At a use, Type arguments are substituted; for a Type satisfying `D`, a proof of `P` enables the verified conformance. Implementations are never selected again by caller lookup or favorable instantiations.

Direct member use has its own applicability step: after lookup commits a function group and Type inference and substitution complete, the member's `P` must be proven before Best Candidate comparison. The same condition applies to function references and computed access. Associated-Type projections require evidence of the relevant conformance.

| Judgment of `P` | Member applicability | Conformance use |
| --- | --- | --- |
| Proven | Condition satisfied; other applicability rules are checked | This conformance path is available |
| Refuted | Inapplicable | This path supplies no conformance; the Type remains usable |
| Unknown | Applicability unproven | Evidence for neither conformance nor absence |
| Error | Diagnosed even if another candidate succeeds | Invalid declaration or condition is diagnosed |

Conditions never change lookup's committed layer or the inherited-Name prohibition, and an inapplicable member cannot reopen outer or base lookup. Applicable members of the committed group are compared by the ordinary overload rules, without ranking condition strength. A Loan or initialization failure after selection does not cause reselection. A generic definition cannot restore a member rejected for lack of proof merely because a later instantiation satisfies `P`. If a legitimate temporary Unknown can affect selection, the decision is deferred instead of committing to an alternative.

Unknown dependencies and deadlines follow §8.7 and §8.10; a required proof still missing at its deadline is an error. Neither the declaration itself nor a circular conformance search supplies evidence. Concrete absence requires completing all relevant paths, including ancestor conformances. Conditional syntax and definitions are checked even if no current Type arguments satisfy `P`; environment-only `#if`/`#switch` cannot replace these checks.

#### 8.4.8.3. Uniqueness and parent contracts

At most one direct conformance declaration is allowed per generic Type and Contract, counting unconditional and conditional declarations together after fragment merging. Proving conditions disjoint, choosing priorities or specializing implementations does not permit a second declaration.

```kimi
Self is C when T is A
Self is C when T is B // Error: duplicate direct conformance.
```

Block members obey the ordinary duplicate rules: equal Signatures that differ only in their conditions are duplicates, while distinct Signatures may overload under the applicability rules above.

Conformance Identity is still the pair of concrete Type identity and Contract identity. A direct declaration and Contract refinement may provide several evidence paths to the same conformance. At definition, every pair of paths is checked under `D` and both paths' conditions: equal implementation mappings and associated-Type bindings must be proven unless §8.7 establishes that the paths cannot hold together. A failure to prove agreement is a definition error, not a deferred instantiation check, and stronger optional condition solvers cannot broaden acceptance.

A child conformance must satisfy its ancestor Contracts under its own conditions. For `Child : Parent`, the verified declarations `Self is Parent when T is A` and `Self is Child when T is B` supply Parent evidence if either `T is A` or `T is B` is proven. The child path does not additionally require `T is A`, but every implementation it uses must be applicable. Redundant explicit parent declarations are valid only with the required path agreement.

#### 8.4.8.4. Intrinsics and boundaries

`Self is Copy when P` requests compiler-derived Copy under `D` and `P`. Every complete own Field or payload Type and the direct base are checked under §3.5; user-defined Copy bodies remain forbidden. Deriving Copy for a `ref/T` component needs no `T is Copy` premise, but an explicitly written `T is Copy` condition is still required and cannot be weakened. An unconditional `Self is Copy` keeps its all-bindings guarantee. Unknown Copy is never assumed to be Non-Copy; a bare acquisition needs Copy evidence (§8.9, §8.10).

Conditional conformance defines static conformance and generic use only. It adds no external registration, extension declarations, partial Type specialization, condition-based implementation replacement or runtime Contract View feature.

### 8.4.9. Bound Contracts, collisions, and proof paths

A conformance is identified by the conforming full Type and the bound Contract reference, including the Contract's own Type arguments. A requirement or associated Type is identified by its defining declaration and the bindings of its defining Contract. Several paths to one requirement are one requirement only when their associated Types, Origin conditions and implementation mappings agree; independent same-named requirements are never merged. Bindings are substituted along refinement, and `Self` is the final conforming Type. Inherited input conditions become part of the child's public inputs without turning declaration obligations into assumptions or implementation requirements. Full Origin bindings remain part of the evidence.

#### 8.4.9.1. Direct conformance collisions

Direct conformances and merging proof paths use one collision test:

1. Group by Contract declaration; different declarations do not collide. Normalize bindings with the established Type equalities, associated specifications and Origin rules.
2. Unify parameters and fixed free-term structure (nominal Types, Tuples, Function Types) with variable kinds and an occurs-check. The same binder or slot on both sides is one variable. Local function binders are alpha-normalized and not unified as outer input slots.
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

A refinement path that revisits a Contract declaration, even with different arguments, is rejected, including `Outer<T>.C` to `Outer<List<T>>.C`. Distinct instantiations on separate paths are not cyclic for that reason alone; processed references are tracked separately from the declarations on the current path. Defaults, external conformance and implementation priority are not introduced.

## 8.5. Runtime contracts

**Extension scope.** Contracts in this revision are static. Runtime-use designation and View associated-Type binding syntax remain to be defined. This section records the runtime design for that extension and grants no permission to form such Views in this revision. Object Views with concrete Core targets keep their existing rules. There is no implicit runtime designation and no `where` syntax.

Compile-time conformance and runtime View usability are separate. A runtime Contract exposes requirements through dynamic dispatch without instance state. A bare Contract name is never a value Type; the extension uses explicit Object Semantics such as `objref/C`, `objuniq/C` and `obj/C`.

An **ObjectViewCompatible** Contract is one that, with its associated Types fixed, can be the View Target of an Object View Type such as `obj/C`, `objref/C` or `objuniq/C`. The Contract is the View Target, not the object's Dynamic Type, which remains the concrete Core actually constructed. This Contract property is distinct from the ObjectCallCompatible guarantee of each implementation's call operations.

**`ObjectViewCompatible(C)`** holds exactly when `C` is explicitly designated for runtime use, has no Type-function requirements (including inherited ones), and, with its associated Types fixed, every instance requirement satisfies the following:

| Aspect | Initial requirement |
| --- | --- |
| Receiver | Shared or Exclusive object access, without consuming ownership |
| Operation | Safe method or Property accessor |
| `Self` | No implementation-dependent `Self` outside the receiver |
| Binding | No unbound associated Types or method-specific generic parameters |
| Signature | Parameter and result Types are determined without knowing the hidden concrete Type |
| Origins | Expressible through the borrowed receiver, explicit inputs and `static`; no hidden payload Origin, Contract-level abstract Origin or higher-ranked requirement |

Every getter result and setter requirement is checked separately. Forming `objref/C` or another object form requires `ObjectViewCompatible(C)`, even when the concrete payload is unknown, but does not require searching all implementations. Erasing a value or a concrete payload into a View creates an object, so the complete data Type must satisfy ObjectPayload (§8.4.7.2) as well as the Owned proof of §15.8.1. Changing the View of an existing handle re-proves neither and follows the `Supports`, Owned and Loan rules. Pair evidence admits View Targets but never creation from a value. Unsupported requirements cannot simply be removed from the view: for example, `equals(other: Self)` cannot become a heterogeneous comparison between arbitrary `objref/C` values.

**`Implements(D, C)`** checks explicit or [validly inherited conformance](#844-conformance): every requirement has one implementation for `D` with compatible signature, access and Origins, and published ObjectCallCompatible Proven (§12.4.4). Unknown does not establish this guarantee.

The unique static conformance for the concrete Type and Contract identity is used; changing View bindings cannot create another conformance. Static conformance does not generally persist in derived Types, so the extension must ensure that the ObjectViewCompatible restrictions, fixed associated-Type bindings and published implementation guarantees preserve the Supports relation through every derived layer; static conformance alone does not prove that invariant.

Contract refinement follows [static refinement](#842-refinement). There is no replacement conformance, default implementation or external registration. Registration and artifact consistency follow the [metadata rules](21-layout-runtime-and-code-generation.md#2121-type-identity-and-descriptors).

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

`F is Callable<r, S>` is a built-in Constraint requiring calls with receiver access `r` and signature `S`; `Callable<S>` abbreviates `Callable<ref, S>`. In this revision `r` is the literal Semantics `ref`, `uniq` or `owner`, and `S` is `(A1, ..., An) -> R`, where `R` may be a Place result (§7.1.1). The result may keep input Origins or already-bound external dependencies but cannot borrow the hidden environment receiver.

| Constraint receiver | Receiver acquired by a generic call | Admitted minimum call requirement |
| --- | --- | --- |
| `ref` | `ref/F` | Shared |
| `uniq` | `uniq/F` | Shared or Exclusive |
| `owner` | `F` acquired by value | Shared, Exclusive or Consuming |

The admitted Types are Function Items, concrete Closures and common Function Types with [compatible signatures](10-overload-resolution-and-inference.md#107-callable-signature-compatibility); no user `call` member is searched. `S` is a contract, not a conversion to an erased container. `F`'s complete dependencies are preserved under generic substitution, and neither Copy nor Owned is required of `F`: `owner` denotes acquisition, whereas `Owned` denotes the absence of non-static dependencies. The result restriction does not constrain direct concrete-Closure calls.

**Per-call input Origins.** An omitted Origin on each direct `ref/T` or `uniq/T` parameter of `S` is bound independently **per call**, for all three receiver kinds. Conformance must hold for every valid call-time Origin under the ordinary Type and Loan rules, not for one fixed long-lived Origin. Origins nested within `T`, and capture-derived Origins within `F`, remain fixed and are not quantified. This adds no general higher-ranked Origin binder syntax. No new named scalar Origin is introduced inside `S`, and Callable signatures keep their ban on written direct borrow annotations. Aggregate occurrences may introduce binding-set names owned by the containing declaration; their input slots must be fixed by complete Types or by equality to outer Origin expressions. Result slots follow the signature's ordinary completion rules (§15.4), without exposing its internal per-call binders through an external projection.

Omitted result Origins are completed under §15.4 from those per-call input Origins, keeping already-bound dependencies; the receiver of `F` is not an elision input. For example, `Callable<(ref/T) -> ref/T>` returns a borrow valid for its argument's Origin. With several direct borrowed inputs, result elision uses their meet and keeps all input Loans. A result that requires exclusive access must also preserve the corresponding exclusive Loan; shortening an Origin grants no access capability.

```kimi
func selectRef<T, F>(value: ref/T, select: ref/F) -> ref/T during value
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
- `uniq`: keep exclusive access for the call, and Reborrow exclusively or share it for the actual body. The callee is a Receiver Expression ([§7.3](07-functions-and-callable-values.md#73-explicit-receivers)): an owned callable Place is acquired exclusively without a spelling, and a `uniq/F` value is Reborrowed (§7.6.3).
- `owner`: acquire by bare Copy or `@move`; borrow the acquired value for a Shared or Exclusive body, or transfer it to a Consuming body. Remaining acquired storage is destroyed when the call completes. This owned temporary has normal writable, exclusive access even when it was copied or transferred from a `let` binding.

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

A Shared callable also qualifies, but a `uniq/F` argument still needs ordinary exclusive access. `transform` is passed at an argument position, not as a Receiver Expression, so the owned Closure Place is written `transform@uniq`; the calls `transform(value)` inside `applyTwice` acquire the `uniq/F` parameter implicitly. Shared environment access does not prevent use of the normal exclusive capability of a separate `uniq/T` argument. By-value `F` parameters use ordinary acquisition (a Copy or `@move`); the constraint neither borrows them silently nor guarantees repeated calls or non-escape. Generic conformance and result Origin and Loan obligations must resolve before finalization. A returned input borrow need not keep the callable receiver, and no result may borrow call-local storage, including an `owner` receiver temporary.

## 8.7. Constraint proof system

Constraint meaning and available proof are distinct. `and`, `or` and `not` keep their Boolean meanings, but generic checking uses only the rules below. Accepted programs must not depend on optional SAT solving, arbitrary theorem proving, enumeration of Types or optimizer-derived facts. Fully determined concrete conditions still use ordinary Boolean evaluation.

In this section `P` and `Q` denote validated, bound propositions. Requirement expressions are parsed under [requirement precedence](#83-requirement-expressions) before interpretation: `T is (A and B)` supplies the propositions `(T is A) and (T is B)`, and likewise for `or` and the scope of `not`; source precedence is unchanged. Proposition identity uses bound subject and requirement Symbols, substitutions and normalized complete Types, including Semantics and Origins where applicable; equal source spellings alone are insufficient. Parentheses are transparent, and `not not P` normalizes to `P`. There is no De Morgan, distributive or other logical-equivalence normalization.

The proof judgment has four outcomes, distinct from those of the Condition evaluator:

| Outcome | Meaning |
| --- | --- |
| **Proven** | The permitted rules establish the proposition. |
| **Refuted** | The permitted rules establish its negation; absence of proof is insufficient. |
| **Unknown** | Neither polarity is established by the permitted rules. This is not a Boolean value or an automatic right to defer. |
| **Error** | Invalid names, subjects, requirements or declarations, or detected contradictory evidence, prevent a valid judgment. |

Evidence comes from the current declaration's validated input Constraints, the defined built-in capability rules, and verified unconditional, conditional or inherited conformance. Facts keep their Binding Identity, substitutions and lexical or instantiation scope. A generic input Constraint is an assumption inside the constrained body and must be discharged at each use. A conformance declaration or `Self is C` obligation cannot prove its own implementation merely by being declared; its required implementations and prerequisite Constraints are validated under the conformance rules. Built-in derivations such as `Self is Copy` keep their own rules.

| Proof rule | Permitted derivation |
| --- | --- |
| Exact assumption | A matching available proposition proves itself; an available `not P` refutes `P`. |
| Conjunction elimination | An available `P and Q` supplies both `P` and `Q`, recursively. |
| Conjunction introduction | `P and Q` is Proven when both operands are Proven. |
| Disjunction introduction | `P or Q` is Proven when either operand is Proven; this grants no evidence for the other operand. |
| Negation | `not P` exchanges Proven and Refuted; Unknown remains Unknown and Error remains Error. Double negation is normalized as above. |
| Boolean refutation | `P and Q` is Refuted if either operand is Refuted; `P or Q` is Refuted if both are Refuted. |
| Concrete atomic judgment | The defined concrete Type-identity, Semantics/category and built-in capability tests, or the closed conformance judgment below. |
| Semantics admitted set | A requirement on a Semantics binding is decided by containment of the binding's admitted set, defined below. |
| Verified conformance | A verified explicit or inherited mapping, after its prerequisites are proven, including inheritance matching (§8.4.4) and conditional premises (§8.4.8). Legitimate unresolved prerequisites are retained; cyclic declarations alone prove nothing. |
| Contract refinement | From an available `T is C`, each ancestor conformance and inherited requirement Constraint, substituting `T` for `Self`. This does not discharge an unverified declaration's implementation obligations. |
| Associated-Type identity | Substitute and normalize explicit associated-Type specifications and available Type-identity Constraints under the [associated-Type rules](#843-associated-types). Bindings are not inferred from members, and no satisfying Type is searched for. |
| Other cases | Unknown, unless validation requires Error. |

All proof operands are validated, and Error absorbs even a determined truth result. Otherwise, a Refuted operand can refute a conjunction and a Proven operand can prove a disjunction despite Unknown operands. Exact compound assumptions are usable directly, but only conjunction elimination exposes their parts. `P or Q` together with `not P` cannot prove `Q`: there is no case analysis, contraposition, proof by contradiction or inference from contradiction. These limits govern symbolic proof, not the Boolean evaluation of determined concrete judgments.

**Concrete and closed-world judgments.** Fully bound Types are compared by normalized identity, and determined Semantics, category and built-in capability tests use their defined rules. Unbound Types and unresolved prerequisites are not negative results. The absence of a conformance is Refuted only after the concrete declaration, the inherited and potentially applicable conditional conformances, and every relevant merge, selection, binding and prerequisite in the fixed environment have been completed, and the specified proofs have ruled out all alternatives. A failed lookup alone proves no absence; a malformed conformance is Error, and an unsupported associated-Type operation is not a concrete negative.

**Semantics admitted set.** The Semantics premises available at a point determine the **admitted set** of a Semantics binding `s`. A concrete name contributes that one Semantics and a category its members (§3.3); `and`, `or` and `not` take the intersection, union and complement over the nine Semantics; separate clauses intersect; without premises all nine are admitted. A requirement `s is R` whose member set is `S` is Proven when the admitted set is contained in `S`, Refuted when the two are disjoint and Unknown otherwise; an empty admitted set is contradictory evidence and Error. This rule decides every Semantics requirement, including pair evidence for object forms (§8.4.7.2), `Weak<S>` over a pair (§3.2.2) and borrow annotations on a Semantics parameter (§8.1.2, §15.3). It adds no reasoning about Type requirements.

**Recursion.** An active obligation revisited with identical arguments supplies no evidence; cycles and in-progress registrations establish neither conformance nor refutation. Independent finite evidence, including evidence found after a temporary cycle result, is considered. Otherwise the result stays Unknown until its deadline and is diagnosed if still required. Recursive declarations alone are valid. Built-in structural analyses use their own recursion and fixed-point rules; no general coinduction is implied.

**Contradictions.** After the specified normalization and conjunction elimination, directly available `P` and `not P` are contradictory evidence and produce Error; so is evidence establishing both polarities of a queried proposition. Arbitrary capabilities are never derived from an inconsistent environment. Detecting more complex contradictions through excluded logical transformations is not required and cannot supply proof.

**Use and finalization.** A required Constraint succeeds only when Proven. Refuted fails the requirement, and Error reports invalid input or contradictory evidence. Unknown may be retained only when an identified later binding, instantiation or prerequisite analysis can resolve it before the applicable deadline; otherwise it is an unproven-requirement error wherever proof is required. Every required concrete-call or instantiation obligation is resolved before finalization. Unknown is never accepted, converted to Refuted or used as evidence for a negated Constraint. Associated-Type inference beyond explicit identity facts and stronger symbolic reasoning remain design boundaries.

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

The target must be a named generic Type function or instance method with an ordinary implementation; constructors, `deinit`, accessors and dedicated operator declarations are excluded. There is no partial or conditional specialization, omitted argument, placeholder, priority rule, general Const argument, specialization of a generic Container's arguments, specialization of a method with unbound outer generic parameters, or explicit target-identity syntax.

Duplicate ordinary declarations are rejected first. Then the original function is identified:

1. Collect same-name generic functions in the same Declaration Container with the same function kind (Type function or instance method) and generic slot count.
2. Bind the written generic arguments using each candidate's slot definitions. Match the substituted receiver presence and Type structure, and the ordinary parameters' count, order and normalized Type structure. Complete Types are formed and validated before Origins are excluded for this structural comparison.
3. Zero matches is an error, and several matches make the specialization declaration ambiguous. For exactly one match, the inherited contract is validated.

Result Types, parameter names, Constraint satisfaction, Origin relationships, implicit adaptation, slot kind alone, overload ranking and declaration or file order never resolve an ambiguity. A failed contract check cannot select another target.

```kimi
func inspect<T>(value: T) -> () => ()
func inspect<T>(value: i32) -> () => ()
specialize func inspect<i32>(value: i32) -> () => ()
// Error: both ordinary declarations have the same substituted input structure.
```

Diagnostics distinguish no target, an input-structure mismatch, several targets and a contract mismatch after selection. They include the candidate declaration locations and the relevant Type, receiver or arity differences; diagnostic comparison never chooses a nearest candidate.

### 8.8.2. Inherited contract

The specialization header identifies and checks the original contract; it is not an independent call Signature.

| Item | Specialization rule |
| --- | --- |
| Receiver and ordinary parameters | Restate count, order and substituted Type structure, keeping the original's receiver shape (§7.3); no implicit adaptation |
| Result | Match the substituted Type; omission means Unit |
| External parameter names | Match the original; not used to identify the target |
| Internal names | Ordinary parameter names may change using `external => internal: Type`; an inherited receiver remains `self` under §7.3 |
| Origin and Loan contract | Inherit the binders and preserve every admitted Origin binding |
| Access, defaults, name-omission permissions, generic Constraints | Inherited; never redeclared, strengthened or weakened |
| Attributes, Safety, calling convention, Effect requirements | Inherited under the existing rules; no Attributes or additional modifiers on the specialization declaration |

A specialization header contains neither an `!` boundary nor parameter defaults. It inherits the original external names, K and defaults; the absence of a boundary does not mean K = N. Callers still use the original omission and default-evaluation rules:

```kimi
func find<T>(value: T, count: i32 = 1) -> () => ()
specialize func find<i32>(value: i32, count: i32) -> () => ()
find<i32>(10) // Evaluate the original default, then call the specialization.
```

These restrictions concern the specialization declaration, not ordinary syntax inside its body. A specialization of an unsafe function inherits its Safety contract, but unsafe operations in its body still need an Unsafe Block. Contract inheritance introduces no unsupported async, exception-Effect or variadic feature.

Omitted specialization Origins are inherited, not quantified. The normalized input structure is matched first, mapping pending Origin positions to each candidate, and exactly one target is selected; Origin conditions and results never break ties. The Origins are then completed from that contract, and explicit bindings, result, formation, bounds and Loans are validated. A failed validation never retries another candidate. After the inherited binders are matched, the body must work for **every Origin binding the original contract permits**. A specialization cannot add an Origin parameter or lifetime restriction, narrow applicability by Origin, or register another implementation for different Origins. It cannot rescue an invalid ordinary implementation or replace a declaration-required proof; [deferred generic checking](#810-generic-body-checking-and-deferred-obligations) keeps its stated design boundary.

Inferred ObjectCallCompatible is a [common implementation guarantee](12-expressions.md#12443-implementation-families), not an additional declaration obligation inherited from the ordinary body. A specialization may make that public guarantee NotProven without becoming invalid for that reason; the declared Signature, Constraints, Safety and Effect requirements still apply.

### 8.8.3. Selection and declaration ownership

Selection uses the [Implementation Selection Key](21-layout-runtime-and-code-generation.md#2132-identity-and-generation-keys): the original function's declaration identity and its ordered, normalized static generic arguments, excluding only Origins. Nominal identity, nested structure and every Semantics layer, including an outer object handle, are preserved, so `obj/D` and `rc/D` have different keys even when they identify the same dynamic payload Type. Two specialization declarations with the same key are an error.

Calls and function references first use ordinary lookup, overload resolution, inference and the original contract's Type, Constraint, Origin and Loan checks. Specializations never enter the candidate set or supply inference evidence. Once the static generic arguments determine a key, the matching specialization is used, or the ordinary implementation if there is none. A value's Dynamic Type, including behind a base or Contract View, never changes this selection.

```kimi
// classify and its i32 specialization are declared above.
func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let number: i32 = 10
let result = forward<i32>(number@ref) // 1, including with shared generic code.
```

A generic caller cannot be fixed to the ordinary implementation merely because its generic arguments were initially unknown. This guarantee is independent of code sharing, separate compilation, optimization level and LTO. Function references obey the same choice and their existing restrictions, including the ban on unsafe function values.

There is no direct name for the specialization and no syntax that bypasses it to call the ordinary body. Calling the same function with the same generic arguments from inside its specialization selects that specialization again and recurses; move common work to a helper. An error in a specialization body never falls back to another body or overload.

Specializations belong to the original function's Kotonoha and Declaration Container. Existing Container fragments may place them in another file; unrelated extensions and other Kotonoha libraries cannot add or replace them. The defining Kotonoha closes the specialization set after environment selection and declaration collection, including generated sources, before it finalizes affected call targets and no later than artifact finalization. Declaration and loading order cannot affect selection. Verification, artifact information and invalidation follow [generic code generation](21-layout-runtime-and-code-generation.md#213-generic-code-generation).

## 8.9. Generic access effects

Before ownership and Loan analysis is finalized for an acquisition or adaptation, its **Access Effect** is determined statically and uniquely:

```text
Access Effect
├─ acquisition: Copy / Move / Consume
├─ Loan action: shared Borrow / exclusive Borrow / Reborrow
└─ target Place and Origin dependencies
```

A broad Borrow-versus-acquisition category is insufficient: Copy is distinguished from Move, shared from exclusive, and Borrow from Reborrow. Symbolic generic Types and Origins are allowed if the required effect and dependencies are uniquely expressible. This rule applies to ordinary acquisition and to all generic `@s`, `@s/T` and `@Type` forms.

An eligible exclusive call adaptation uses [reservation and activation](15-ownership-and-lifetime-analysis.md#1567-call-borrow-reservations) once its Access Effect is determined. Reservation neither relaxes the definition-side proof of any admitted effect nor introduces a new Semantics or runtime operation.

```text
Generic analysis
├─ effect known -> analyze normally
└─ legitimate dependency -> retain obligation
                            -> constraints / instantiation
                            -> determine effect -> finalize ownership and cleanup
```

A by-value acquisition's effect follows its spelling:

- a bare Place Copies and requires Copy evidence at definition checking; unproven Copy is an error, never deferred checking or an inferred Move;
- `@move` transfers;
- `@s` follows the binding of `s`: it borrows for a borrow binding and, for an owning binding, performs an ordinary same-Type acquisition, which needs Copy evidence.

Unresolved Copy capability is never treated as proof of Non-Copy. Borrow effects may remain symbolic until instantiation only if every admitted case is legal, including subsequent uses, Loans and cleanup. This delays the determination of an effect, not the discovery of a required capability. Environment-changing directives still obey their earlier [selection deadlines](19-compile-time-directives.md#194-name-resolution-boundary).

Instantiations may have different effects. The already-verified effect plan is substituted, and each concrete body's cleanup is derived from it; an analysis for a different effect is never reused without validation. Compile-time directives neither test Types nor select Access Effects. The [generic verification principle](#810-generic-body-checking-and-deferred-obligations) requires the ordinary body to be valid independently of explicit specializations.

```kimi
// s is a declared Semantics parameter; value is an initialized owned value.
value@s
use(value)
```

For `s = ref`, discarding the first result ends its shared Loan; `s = uniq` also requires exclusive writability. The later use is checked after that Loan ends. If the Constraints admit `s = owner`, the first use is a bare same-Type acquisition and needs `T is Copy` at definition; without it the definition is invalid, so restrict `s` to borrows or prove Copy. If the result is retained, subsequent uses are checked throughout its Loan lifetime.

## 8.10. Generic body checking and deferred obligations

**Universal body verification.** An ordinary generic body must be semantically valid for every well-formed Type, length and Origin argument binding that satisfies its declared Constraints, any enclosing conditional-conformance premises and its public Signature requirements. This is verified before the definition is accepted or exported, including definitions with no uses. Verification uses the limited proof system of §8.7 and symbolic Type, Origin and effect rules; it neither enumerates available Types nor infers a hidden capability Constraint from the body. Failing to establish a required proof is a definition error, and explicit specializations cannot rescue an invalid ordinary body.

This requirement fixes meaning, not a compiler-pass schedule. Dependencies on other declarations may delay checking within the build, but an unverified definition cannot be accepted merely because selected concrete instantiations succeed. In particular, a generic call to another generic function must prove that function's declared requirements from the caller's declared premises.

Unknown Copy never changes an acquisition's effect: a bare by-value Place requires Copy evidence (§8.9), a shared or exclusive Subject binds references, and an owned Subject or iteration item transfers its parts (§15.1.6). A read that must leave the source Initialized therefore needs an explicit `T is Copy`, a borrow that avoids acquisition, or a valid reinitialization before reuse. `T is Copy` is never added to a caller's applicability conditions after the body is checked.

~~~kimi
func transfer<T>(value: T) -> T => value@move // Moves a Copy or Non-Copy T alike; bare `value` would need Copy evidence.

func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func invalidTwice<T>(value: T) -> (T, T)
    return (value, value) // Error at definition: Copy is not guaranteed.
~~~

Generic stored acquisition, element Places and custom, computed and required getter results keep their declared Types (Chapter 11, §4.6.1). No operation has a Copy-dependent result Type family, and a bare read of an unknown `T` needs Copy evidence wherever it appears.

A **Deferred Obligation** records remaining substitution or representation work for a verified definition; it is never an unproven body capability. It records its kind, defining bindings and environment, source location, declared premises, symbolic proof and effect plan, dependencies and deadline. Unknown names, missing conformance, possible use after Move and unresolved overload ambiguity cannot be deferred in the hope of a favorable instantiation.

| Stage | Required work |
| --- | --- |
| Definition | Resolve definition-side names and roles; prove body Type correctness, selected operations, capability requirements, and symbolic ownership/Origin/cleanup legality under the declared contract |
| Permitted dependency | Retain proven symbolic Type/effect families and explicit representation obligations; Unknown is neither success nor evidence of Non-Copy |
| Instantiation | Check the call's declared contract and ordinary argument/Loan validity; substitute verified plans; resolve concrete layout, representation and exact effects without adding semantic use conditions |
| Finalization | Discharge representation obligations and complete concrete ownership/cleanup plans before lowering executable operations; never retry committed lookup or overload selection |

For example, using the target projection `T` as a local Type inside `func f<s/T>(x: s/T)` must be justified by the declaration's Constraints and slot rules. If an admitted binding could make it an Object View Target instead of a value Type, the use is rejected at definition time, not only for the affected callers. Positions that prohibit generic parameters outright, such as a base Type, remain prohibited.

**Public dependent obligations.** Only the Type and Origin well-formedness conditions implied by the written Signature, generic schema, associated-Type requirements, Constraints and published conditional-member premises may restrict semantic applicability. They are fixed when the declaration is checked and must be available to callers and artifacts without inspecting a private body; they cannot contain a newly inferred body requirement such as Copy, an extra Contract or a favorable acquisition case. There is no source syntax for arbitrary hidden requirements: a need that the existing contract cannot express or prove makes the definition invalid.

Concrete layout and representation validity may still depend on substitution or the prepared target, including finite representable storage for an otherwise well-typed body local. Such dependencies are recorded in the definition artifact, with their source and target requirements, before clients instantiate it. These checks concern representability only and cannot disguise a Type, capability or lifetime restriction. A call that satisfies the public contract must not fail later because its callee newly discovers a semantic body requirement. Ordinary caller-side initialization and Loan checks, specified runtime checks, target representation failures and documented compiler resource exhaustion remain distinct; resource exhaustion is not semantic invalidity.

**Sealed, ObjectPayload and complete payloads.** Sealed, ObjectPayload, Object Target and complete-payload uses are checked in both signatures and bodies under the declared premises. Missing Type roles, capabilities or lifetime evidence are definition errors; only representation obligations such as finite layout and profile-specific payload alignment may be deferred. A change of openness or of an ObjectPayload opt-out requires rechecking positive and negative Sealed and ObjectPayload results, inheritance, projections and public effects, including all permitted specializations (§18.3).

**ObjectCallCompatible** (§12.4.4) is a completed public operation guarantee used by projection and object-call legality; it is neither a conditional applicability premise nor a deferred body requirement. Its common implementation family is verified before publication, and caller-specific instantiations cannot strengthen it. Changes use the dependency revalidation of §21.3.4.

**Diagnostics.** A definition error identifies the body use and the missing declared proof. An instantiation diagnostic identifies the already-published dependent or representation obligation, the definition site, the arguments and the failed condition. Verified summaries and plans are preserved in compile-time metadata even when runtime keys erase Origins. Caller aliases and extensions are not added, overload choice is not redone for favorable concrete Types, and obligation strength does not rank candidates. Environment directives provide no Type or capability evidence and keep their earlier selection deadlines.
