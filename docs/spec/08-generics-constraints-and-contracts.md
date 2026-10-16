# 8. Generics, constraints, and contracts

[Specification index](../SPEC.md)

Constraints state what a generic body may assume and each use must prove. Contracts name capabilities and map requirements to implementations. Explicit specialization changes which implementation runs, never the call contract.

## 8.1. Generic parameters

### 8.1.1. Type slots and pair parameters

Type slots `<T>` and pair parameters `<s/T>` each consume one complete Type argument `W` and keep its Semantics, nested Types and Origins. Length slots use `<length N>` ([§4.4](04-arrays-indexing-and-slices.md#44-function-length-parameters)); a plain `<N>` is a Type slot. Origin parameters have their own schema ([§15.3](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations)).

- `<T>` binds `T` to `W`, a complete value Type, not only a Core.
- `<s/T>` splits the outer layer of `W`: `s` is the **pair binder** (the outer Semantics) and `T` is the **pair target** (the direct target). The original `s/T` still denotes `W`. When the outer layer is a safe borrow, `W` has an outer Origin `o`, which has no source name. `o` is one fixed Origin of the body ([§15.6.5](15-ownership-and-lifetime-analysis.md#1565-judging-origin-relations)) per pair binder, shared by every occurrence of the original `s/T` and present only in the admitted borrow cases of `s`.

Before splitting, aliases, completed projections, grouping and redundant `owner/` are normalized; alias cycles are errors. `ref/(uniq/i32 during b) during a` gives `s = ref`, `T = uniq/i32 during b` and outer Origin `a`; `owner/(ref/i32 during a)` has outer Semantics `ref`.

| Outer Semantics | Pair target; requirement for applying `s` to another `U` | Outer Origin |
| --- | --- | --- |
| `owner` | `W` itself; `owner/U` is `U` | none |
| `ref`, `uniq` | Complete Referent Type; `uniq` also needs exclusive acquisition and Loans | yes |
| `obj`, `rc`, `arc` | Object Target ([§8.7.3](#873-objectpayload)) | none |
| `objref`, `objuniq` | Object Target | yes |
| `raw` | Complete pointee Type; no lifetime guarantee | none |

A missing outer Origin erases no payload dependency: in `ref/(View<i32>{v}) during b`, `v.source` belongs to the inner Type.

**Pair target as a value Type.** `T` may be used as a standalone value Type in the body (for example `Option<T>`) only when it is a value Type in every admitted case of `s`. It is one for `owner`, `ref`, `uniq` and `raw`. If the admitted set of `s` meets the object family, `T is ObjectPayload` must be Proven ([§8.7.3](#873-objectpayload)). Otherwise the use is an error at definition checking. This is a Type Legality check after lookup has selected `T` ([§9.2](09-names-signatures-and-access.md#92-namespaces-and-lookup-roles)).

**Object forms.** Forming an object form over a generic target requires Object Target evidence ([§8.7.3](#873-objectpayload)) in signatures and bodies. For the pair's own `T`, **pair evidence** suffices: the admitted set of `s` ([§8.3.4](#834-semantics-admitted-set)) lies in the object family (`obj`, `rc`, `arc`, `objref`, `objuniq`). Applying `s` to another `U` requires `U` to be an Object Target whenever the admitted set meets the object family, and every admitted Semantics must form a valid Type with `U`. Evidence is never derived from a written signature's well-formedness.

The names of one parameter list are distinct: `<T, T>`, `<s/T, s/U>` and `<s/s>` are errors. `<s>` is an ordinary Type slot; there are no standalone Semantics slots.

```kimi
func keep<s/T>(x: s/T)
    s is ref or uniq
    let empty: Option<T> = .None      // Valid: T is a Referent Type in both cases.

func lose<s/T>(x: s/T)
    let empty: Option<T> = .None      // Error: s may be obj, and T is not proven ObjectPayload.
```

### 8.1.2. Pair reconstruction and Origin annotations

The original `s/T` refers to `W` by its declared binding, not by Type equality after instantiation. `s/U` for another `U` applies only the Semantics kind and does not copy the outer Origin of `W`.

- **Annotation.** `s/T during b` or `s/U during b` binds that occurrence's outer-Origin slot, which is active only in the admitted borrow cases of `s`; so `Collection<s/Option<T> during a>` is written the same whatever `s` admits. For `W = ref/i32 during a`, `s/T during b` forms `ref/i32 during b` without relating `a` and `b` or converting a value; fitting a value to it then checks shortening, acquisition and Loans. An annotation neither creates nor removes a Reborrow.
- **Omission.** `s/U` follows the position rules for omitted Origins ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)) symbolically: a direct input gets an independent outer-Origin slot, active only when `s` is a safe borrow. Fields and nested borrow layers gain no omission permission.
- **Following.** Following a pair layer ([§13.5.5](13-operators-and-assignment.md#1355-follow)) uses the outer-Origin slot of the followed occurrence; each dependency is active exactly in the Semantics cases ([§8.15.2](#8152-semantics-cases)) that produce it.

Type formation and body legality must hold for every admitted binding. Instantiation substitutes the canonical contract ([§15.3.7](15-ownership-and-lifetime-analysis.md#1537-contract-comparison)) and discovers no missing proof.

### 8.1.3. Type arguments

A Type argument is a valid complete Type or a Type depending on the definition's parameters: a primitive, struct, enum, Unit, Tuple, common Function Type, safe borrow, object Type, raw pointer or Never ([§3.1.3](03-types-and-values.md#313-unit-and-never)). A Function Item or Closure Type is supplied through inference or an existing reference form. A Contract, group, Semantics or value is not a Type argument. A Never expression alone cannot infer an unbound Type.

An explicit argument list supplies every slot in order, with an optional trailing comma; omitting the whole list uses the construct's inference ([§10.8](10-overload-resolution-and-inference.md#108-generic-argument-inference)). There are no partial lists, `_` placeholders, defaults, variadic slots or Const generics. `<s/T, U>` takes two arguments, such as `<ref/i32 during a, string>`.

A valid argument is not valid in every role: base-Type, associated-Type, layout ([§8.15.3](#8153-concrete-layout-and-representation)), Copy-derivation and payload rules still apply. Ordinary storage keeps argument dependencies without requiring Owned ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)); static storage requires Owned ([§11.8](11-properties.md#118-static-storage)).

```kimi
func choose<T>(a: T, b: T) -> T => b@move

let y = choose<Option<string>>(.None, .Some("a"))
// let z = choose<i32, i32>(1, 2)   // Error: choose has one Type slot.
```

## 8.2. Constraints and requirement expressions

### 8.2.1. Constraint clauses

A **Constraint Clause** `subject is requirement` states a condition; all clauses of a declaration's **Constraints** must hold, and the body may use what they prove. Subjects are complete Types, pair binders, pair targets, associated-Type projections and `Self`, as the requirement's role permits; each declaration kind restricts them ([§7.5](07-functions-and-callable-values.md#75-function-constraints)). A requirement names a Contract ([§8.4](#84-contracts)) or another capability, a complete Type ([§8.2.3](#823-type-identity-and-identity-premises)), or for a pair binder a Semantics or Semantics category ([§3.3](03-types-and-values.md#33-semantics)).

A struct or enum declares its Constraints before its members; conditional conformances appear at member positions ([§8.8](#88-conditional-conformance)). In a Type declaration, `Self is C` declares conformance ([§8.6.1](#861-conformance-declarations-and-identity)); compiler-intrinsic Contracts and the one negated `Self` clause are covered by [§8.7.1](#871-compiler-intrinsic-contracts).

```kimi
contract Describe
    func describe(self: ref/Self) -> string

struct Labeled<T>
    T is Describe
    Self is Describe         // Labeled<T> conforms; nothing is derived from T is Describe.
    var value: T
    public func describe(self: ref/Self) -> string => self.value.describe()
```

### 8.2.2. Requirement expressions

`subject is requirement` is a compile-time test in Constraint Clauses and associated-Type declarations and specifications. It is never a runtime test and is not allowed in `#if` or `#case`; expressions use runtime `is` tests ([§13.6.4](13-operators-and-assignment.md#1364-runtime-is-tests)). Requirements combine with `and`, `or`, `not` and parentheses (grammar: `IsRequirement`, [§F.6](appendices/F-syntax-summary.md#f6-declarations)); a `not` directly after `is` negates the whole right side:

| Form | Meaning |
| --- | --- |
| `T is A and B` / `T is A or B` | `T` satisfies both / either. |
| `T is not A or B` | `T` satisfies neither. |
| `T is (not A) or B` | `T` does not satisfy `A`, or satisfies `B`. |

A Requirement Test cannot be part of a Boolean expression such as `(T is A) and enabled`.

### 8.2.3. Type identity and identity premises

`X is U` with a complete Type `U` requires `X` and `U` to be the same Type. Identity implies no conversion.

**Identity premises.** In a scope, the available Type-identity facts (premises `X is U` that name complete Types, and associated-Type specifications) group Types into classes: the smallest equivalence relation that contains these facts and is closed under replacing a Type by an identical one inside any Type, including projections (if `X` and `U` are one Type, so are `Option<X>` and `Option<U>`, and `X.(C).Item` and `U.(C).Item`). Two Types are identical when they normalize into the same class, and all Type checking in the scope treats them as one Type. The scope is the constrained declaration, or the conformance scope and block of a conditional conformance ([§8.8.1](#881-conditions-and-implementation-blocks)). A declaration is an error when one class contains two different fully bound Types, or when a Type would have to contain itself as a proper part. Identity is never decomposed: `Option<U> is Option<i32>` does not imply `U is i32`.

```kimi
func same<T, U>(value: T) -> i32
    T is Option<U>
    T is Option<i32>
    return 0      // Option<U> and Option<i32> are one Type here; U and i32 are not.

func cyclic<T, U>(value: T) -> i32
    T is Option<U>
    U is Option<T>  // Error: T would contain itself.
    return 0
```

## 8.3. Constraint proof

### 8.3.1. Propositions and outcomes

Generic checking proves Constraints only by the rules of this section, never by SAT solving, theorem proving, enumeration of Types or optimizer facts. Fully determined concrete conditions use Boolean evaluation. Callable effect bounds are not propositions ([§8.11.6](#8116-callable-effect-bounds)).

Requirement precedence applies first: `T is (A and B)` supplies `(T is A) and (T is B)`. Propositions are identified by bound Symbols and normalized complete Types, not spelling. Parentheses are transparent and `not not P` is `P`; there is no other logical normalization.

| Outcome | Meaning |
| --- | --- |
| **Proven** | The rules establish the proposition. |
| **Refuted** | The rules establish its negation; absence of proof is not enough. |
| **Unknown** | Neither polarity is established. |
| **Error** | Invalid names, subjects or declarations, or contradictory evidence. |

Evidence comes from the declaration's input Constraints, built-in capability rules and verified conformances; environment directives supply none. An input Constraint is assumed in the body and proven at each use. A required Constraint succeeds only when Proven.

### 8.3.2. Proof rules

| Rule | Derivation |
| --- | --- |
| Exact assumption | An available `P` proves `P`; an available `not P` refutes it. |
| Conjunction | An available `P and Q` supplies both; `P and Q` is Proven when both are, Refuted when either is. |
| Disjunction | `P or Q` is Proven when either is (no evidence for the other), Refuted when both are. |
| Negation | `not P` exchanges Proven and Refuted. |
| Concrete judgment | Concrete Type-identity, Semantics, category and built-in capability tests, and the closed-world rule below. |
| Admitted set | A requirement on a pair binder is decided by [§8.3.4](#834-semantics-admitted-set). |
| Verified conformance | A verified explicit, conditional ([§8.8](#88-conditional-conformance)) or inherited ([§8.6.2](#862-inherited-conformance)) conformance supplies evidence once its implementations are identified and compatible ([§8.6.3](#863-implementation-matching)); effect bounds are checked separately ([§8.11.4](#8114-checking-and-use)). |
| Contract refinement | `T is C` supplies each ancestor conformance and inherited Constraint, with `T` for `Self`. |
| Type and associated-Type identity | Proven when both sides are in one identity class ([§8.2.3](#823-type-identity-and-identity-premises)), after substituting completed associated-Type bindings; Refuted when both are fully bound and differ. Proof never searches members or chooses a Type. |
| Closed implication | `T is PrimitiveInteger` supplies `T is Position` ([§8.7.4](#874-primitiveinteger)). |
| Other cases | Unknown. |

Error absorbs any result; otherwise a Refuted operand refutes a conjunction and a Proven one proves a disjunction despite Unknown operands. `P or Q` with `not P` does not prove `Q`: there is no case analysis or proof by contradiction. Directly available `P` and `not P`, or evidence for both polarities of a queried proposition, is contradictory evidence (Error).

**Closed world.** Unbound Types and unresolved prerequisites are never negative results. Absence of a conformance is Refuted only when every applicable path (declaration, inherited and conditional conformances, prerequisites) is complete and ruled out; a failed lookup proves nothing. A primitive Type's conformances are fixed (intrinsic capabilities and built-in comparison, formatting, Position and arithmetic, [§13.3.3](13-operators-and-assignment.md#1333-arithmetic-contracts)), so its conformance to any other resolved Contract is Refuted.

```kimi
func pick<T>(value: T) -> T
    T is Copy or Describe
    return value     // Error: Copy or Describe does not prove Copy, so value needs @move.
```

### 8.3.3. Recursion and Finality

**Recursion.** A conformance cannot use itself as evidence: an obligation revisited while it is being proven supplies no evidence, and no coinduction is assumed. Recursive declarations alone are valid; structural analyses such as Owned use [§15.3.5](15-ownership-and-lifetime-analysis.md#1535-variance-loan-requirements-and-phantom-origins). When a Contract's associated-Type Constraint leads back to the same Contract, the Contract is valid, but no concrete Type can be proven to conform. The depth of a proof search is limited by an implementation-defined resource limit; a proof that exceeds it is a resource failure ([§23.1.1](23-compiler-services.md#2311-categories)), never Unknown or Refuted.

**Finality.** A judgment uses every fact of the program that does not depend on that judgment; the order in which an implementation checks declarations never changes the result. A fact that depends on its own judgment, directly or through other declarations, supplies no evidence. Where proof is required, Unknown is an error (unproven requirement); it is never accepted or treated as Refuted. A generic definition is judged under its declared premises when it is checked; instantiation adds no semantic use condition and never repeats lookup or overload selection, and only layout and representation obligations depend on instantiation ([§8.15.3](#8153-concrete-layout-and-representation)).

```kimi
contract Tree
    associate Child is Tree      // Valid declaration.

struct Node
    Self is Tree                 // Error: proving Node is Tree needs Node is Tree.
    associate Tree.Child is Node
```

### 8.3.4. Semantics admitted set

The Semantics premises at a point give the **admitted set** of a pair binder `s`: a Semantics name contributes itself and a category its members ([§3.3](03-types-and-values.md#33-semantics)); `and`, `or` and `not` are intersection, union and complement over the nine Semantics; separate clauses intersect; without premises all nine are admitted. `s is R` is Proven when the admitted set lies within the members of `R`, Refuted when they are disjoint and Unknown otherwise; an empty set is Error. This decides every Semantics requirement, including pair evidence, `Weak<S>` over a pair ([§3.2.2](03-types-and-values.md#322-weak-references)) and borrow annotations on a pair binder ([§15.3](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations)).

```kimi
func peek<s/T>(value: s/T) -> i32
    s is object                         // Admitted set {obj, rc, arc}.
    let view: objref/T = value@objref   // Valid: pair evidence for T.
    return 0
```

## 8.4. Contracts

### 8.4.1. Contract declarations

A **Contract** is a Declaration Container declared with `contract` that defines a capability through function requirements, Property requirements, associated Types and Constraints, with no implementations or storage. `Self` denotes the conforming Type. Contracts are static; runtime Contract Views are [§8.12](#812-runtime-contracts). A Contract may declare Type parameters (not Origin or length parameters) and inherits its enclosing environment ([§6.1.3](06-declarations-and-containers.md#613-inherited-environments-and-declaration-references)). `Indexable<isize>` and `Indexable<string>` are distinct bound Contract references ([§8.6.1](#861-conformance-declarations-and-identity)).

```kimi
contract Source
    associate Element
    func read(self: ref/Self) -> Element

contract Sized
    property count: i32 has get

contract SizedSource: Source, Sized
    func reset(self: uniq/Self) -> ()

contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> ref/Element during self
```

### 8.4.2. Function requirements

A function requirement has a Name, optional generic parameters, implicit signature Origins ([§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)), an optional task slot ([§24.2.1](24-suspension-and-asynchronous-tasks.md#2421-form)), typed parameters (receiver shorthand: [§7.3](07-functions-and-callable-values.md#73-receivers)) and an optional result Type; an omitted result is Unit. With a receiver it is an instance function, otherwise a Type function. An optional indented region after the header holds its own Constraint Clauses, Origin relations ([§15.3.3](15-ownership-and-lifetime-analysis.md#1533-declaration-attached-relations)) and effect clauses ([§8.11.1](#8111-declarations)), with subjects rooted in its generic parameters. These are premises for compatibility ([§8.6.3](#863-implementation-matching)), not added to the implementation.

- Same-name requirements with receivers in one Contract, inherited ones included, share one receiver shape ([§7.3](07-functions-and-callable-values.md#73-receivers)); all same-name requirements share one task-slot shape ([§24.2.3](24-suspension-and-asynchronous-tasks.md#2423-not-a-parameter-one-shape-per-function-group)).
- Requirements allow the `!` boundary ([§7.2](07-functions-and-callable-values.md#72-parameters)) but no defaults. A call through a requirement supplies every ordinary argument under the requirement's name-omission permissions.
- Requirements take no access modifiers ([§9.4.6](09-names-signatures-and-access.md#946-conformance-accessibility)). Property requirements use `has` ([§11.9](11-properties.md#119-contract-property-requirements)).

```kimi
contract Factory
    func create<T>(value: T) -> Self
        T is Copy
```

### 8.4.3. Refinement

`contract C: A, B` refines each parent, given as a bound Contract reference ([§9.7.1](09-names-signatures-and-access.md#971-bound-container-paths)), as in `contract UniqIndexable<Key>: Indexable<Key>`, and inherits its requirements, associated Types and Constraints. Parent order gives no priority, and cycles are errors. Inside a Contract, `Self is C` for a user Contract does not declare refinement (intrinsic parents: [§8.7.1](#871-compiler-intrinsic-contracts)). Conformance to a child entails every ancestor, with `Self` the final conforming Type. A child may add but never remove or weaken. Paths to one ancestor declaration with equal bindings give one requirement ([§8.9.2](#892-merging-paths-and-cycles)); independent same-named declarations stay distinct, and one implementation may satisfy several.

A refinement is an error when its requirements can neither coexist as separate implementations ([§8.6.3](#863-implementation-matching), [§9.1](09-names-signatures-and-access.md#91-signatures)) nor be satisfied by one implementation. Labels alone never distinguish overloads, and different result Types alone prove no conflict, since result compatibility ([§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)) applies. Unknown proves no conflict.

## 8.5. Associated Types

### 8.5.1. Declaration and specification

An associated Type denotes a complete Type. Binding, projecting or expanding it neither rebinds its Origins nor supplies `static`. `associate` declares one in a Contract and specifies one in a conforming Type, always with `is`:

| Form | Meaning |
| --- | --- |
| `associate Element` | Declares it in a Contract; in a conforming Type it is an error (use `is`). |
| `associate Element is R` | In a Contract, declares it with requirement `R`. In a conforming Type, specifies or constrains the uniquely named associated Type. |
| `associate C.Element is R` | Specifies or constrains the one declared by `C`. |
| `associate LentItem(step) ...` | With Origin parameters ([§8.5.4](#854-origin-parameters)). |

In a conforming Type, `associate C.X is R` specifies `X` when conjunction elimination ([§8.3.2](#832-proof-rules)) of `R` yields a Type-identity atom; otherwise it leaves `X` to be specified elsewhere or inferred ([§8.5.5](#855-inference)). In both cases the completed Type must satisfy all of `R`. Several identity atoms follow [§8.2.3](#823-type-identity-and-identity-premises). Each associated Type of a conformance is determined uniquely by specifications, identity Constraints, inherited bindings or inference; one left undetermined is an error.

**Qualification.** `C` in `associate C.Element` is a direct conformance or an ancestor, with its Type arguments. The unqualified form is valid only when one associated Type with that name is available across the conformances and refinements (paths to one declaration with equal bindings count once, [§8.9.2](#892-merging-paths-and-cycles)); `C<i32>.Element` and `C<i64>.Element` need qualifiers when both are available.

```kimi
contract Destination
    associate Element

struct Pipe
    Self is Source
    Self is Destination
    associate Source.Element is i32
    associate Destination.Element is string
    public func read(self: ref/Self) -> i32 => 42

struct A
    Self is Source
    associate Source.Element is i32 and Copy   // Specifies i32; Copy is checked.
    public func read(self: ref/Self) -> i32 => 0

struct B
    Self is Source
    associate Source.Element is i32 or i64     // Leaves Element to inference; the result must be i32 or i64.
    public func read(self: ref/Self) -> i64 => 0
```

### 8.5.2. Projections

`T.(C).Element` is the associated Type of `T`'s conformance to the bound Contract reference `C`; `T.(C).LentItem(a)` applies Origin arguments. `T.Element` is valid outside a Contract only when the associated Type is unique under the available Constraints; inside a Contract a short name means its own or inherited one. `C` is found by Contract-name lookup, and `T.C.Element` is an ordinary path. Conformance evidence is required and never found by name search. The base may be a Core, a parameter, `Self` or another projection. A projection is a Type that keeps its Semantics. A function's leading Constraints apply to the projections of its signature ([§7.5](07-functions-and-callable-values.md#75-function-constraints)). Distinct successful interpretations are ambiguous.

```kimi
func readOne<T>(source: ref/T) -> T.(Source).Element
    T is Source
    return source.read()

func readInt<T>(source: ref/T) -> i32
    T is Source
    T.(Source).Element is i32
    return source.read()
```

### 8.5.3. Constraints on inherited associated Types

A child Contract may constrain an inherited associated Type with `Self.(C).Element` for an ancestor `C`, or with a bare name unique among own and inherited declarations; this creates no new Type. It may refine an inherited family with `associate Parent.LentItem(a) is E`, which holds for every `a` in the parent's domain, as `Iterator` does with `associate LendingIterator.LentItem(step) is Item` ([§22.2.1](22-core-execution-and-foreign-functions.md#2221-iterator-contracts)). Loan independence is checked separately ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)).

```kimi
contract OrderedSource: Source
    Self.(Source).Element is Equatable

contract IntSource: Source
    Self.(Source).Element is i32    // Conformances need not repeat the binding.
```

### 8.5.4. Origin parameters

An associated Type may declare **Origin parameters**, simple Names in parentheses. Their scope is the declaration's right-hand side, formation Type and attached clauses, not sibling requirements; a same-spelled Origin in a method signature is a different binder that applies the family, as in `Self.LentItem(step)`. Names follow [§15.3.4](15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification).

**Application.** In Type context, `LentItem(a)` substitutes Origin atoms positionally and denotes an ordinary complete Type; an intersection is parenthesized, as in `LentItem((a and b))`. Counts must match. A specification in a conforming Type repeats the parameter count; its parameter names correspond to the requirement's parameters by position, and renaming them does not change the Contract. An unapplied family, `_` and omitted arguments are errors, and an unapplied family is neither a Type argument nor aliasable. Only a parenthesized list directly after a named Type in Type context is an application, and it is an error unless that Type is an associated Type with Origin parameters; braces name a binding set ([§15.3.1](15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets)). The applied Type gets the ordinary variance, shortening and Reborrow checks.

**Formation conditions.** The Contract and the declaration's attached `origin` relations fix the domain. A fixed right-hand side publishes its well-formedness: for `associate View(a) is ref/E during a`, the Origins in OwnedOrigins(E) outlive `a` ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)). A capability requirement fixes no Type, but may end with a **formation Type** `for Type`, which publishes the formation conditions of `Type`, as in `associate IteratorType(source) is LendingIterator for ref/Self during source`; `Type` must be provable from the enclosing Constraints. Clauses such as `origin source outlives a` are processed in the order of [§15.3.3](15-ownership-and-lifetime-analysis.md#1533-declaration-attached-relations): equalities bind unbound Origins of the right-hand side, and the rest form the domain. Without right-hand side or conditions, every Origin the Contract allows is admitted.

A use proves the substituted conditions; a pair layer publishes its outer-Origin conditions only in its binder's cases with an outer Origin ([§8.1.2](#812-pair-reconstruction-and-origin-annotations)). An implementation proves the remaining conditions after binding its right-hand side and cannot add call conditions. Origin parameters create no Loan and have no runtime representation.

```kimi
contract Lender<E>
    associate View(a) is ref/E during a
    associate LentItem(step) for uniq/Self during step
    func lend(self: uniq/Self during step) -> Self.LentItem(step)
    // func bad(self: ref/Self) -> Self.LentItem   // Error: the family is unapplied.
```

### 8.5.5. Inference

A struct or enum conformance may omit an associated Type without Origin parameters; inference completes it from the declared result of an implementation. Inference never runs at instantiation, and an inferred binding is the same contract as an equal explicit one.

**Frozen premises.** Each conformance path's premises are frozen first: its specifications, the identity Constraints of its Contract and ancestors, its inherited bindings and their consequences ([§8.3](#83-constraint-proof)). Only premises valid throughout the path count; premises of one requirement or implementation never determine a binding. An associated identity is the conforming Type, the associated declaration and the bound defining Contract; equal names do not establish it, and an identity conditional on one path is not unconditional on another. Fixed identities substitute forward: Iterator's `LentItem(step) is Item` exposes `Item`.

The current definition is one struct or enum with its conformances; nested definitions are separate. No value inferred for it enters its frozen premises. A value inferred for another definition may be used as soon as that value is determined, without waiting for the other definition's remaining values. Evidence for a value of the current definition that depends, directly or through other definitions' values, on a value inferred in the current definition supplies no evidence.

**Identification.** Each requirement's implementation is identified once by the key of [§8.6.3](#863-implementation-matching) under the frozen premises, never filtered by result, Origins, Constraints or Effects. The outcome is **pending** when some candidate's key comparison depends on a missing associated Type of the definition; otherwise it is **unique**, **missing** or **ambiguous**. Only a unique match supplies evidence. After completion, only pending identifications are identified again, for compatibility only; missing and ambiguous ones are final. Function results supply evidence, `index` and `indexUniq` among them; Property and accessor results supply none. The implementation's result is its declared result, or Unit when omitted. Bodies, call sites and instantiations supply nothing.

**Extraction.** The normalized results are traversed only along structure leading to missing associated Types: a missing position takes the complete implementation Type (a whole Function Type included); same nominal constructions and Tuples follow corresponding arguments; fixed arrays follow elements only when lengths are already equal; concrete Semantics layers follow the same layer. Function Types, unresolved projections and unresolved Semantics applications are not entered. The extracted Type keeps everything, including Origins, and every binder in it must be valid in the conformance scope: a method's generic parameter, per-call Origin or elided receiver Origin makes it unusable. Other result parts get ordinary compatibility; there is no Origin solving, conversion, Copy judgment or search for a capable Type.

**Completion.** Evidence from another path is usable when the destination's premises prove the source's premises. All usable evidence for an identity must be one complete Type, which becomes the binding; differing Types, including differing Origins, are an error, and no evidence needs an explicit specification. Then all ordinary checks apply ([§8.6.3](#863-implementation-matching)); a compatibility failure never reselects. Explicit bindings keep ordinary result compatibility (under `longer outlives shorter`, `ref/T during longer` satisfies a binding `ref/T during shorter`), but inference never finds such a Type. Inherited bindings are not inferred again ([§8.6.2](#862-inherited-conformance)). Acceptance never depends on an inference work budget; exhausting a limit is a resource failure ([§23.1.1](23-compiler-services.md#2311-categories)).

```kimi
struct NumberSource
    Self is Source
    public func read(self: ref/Self) -> i32 => 42   // Infers Source.Element is i32.

contract P
    associate X
    associate Z
    func x(self: ref/Self) -> Self.X
    func z(self: ref/Self) -> Self.Z

contract Q
    associate Y
    associate W
    func y(self: ref/Self) -> Self.Y
    func w(self: ref/Self) -> Self.W

struct PA
    Self is P
    public func x(self: ref/Self) -> PB.(Q).Y => 0   // PA.X is i64, from PB.Y.
    public func z(self: ref/Self) -> i32 => 0

struct PB
    Self is Q
    public func y(self: ref/Self) -> i64 => 0
    public func w(self: ref/Self) -> PA.(P).Z => 0   // PB.W is i32, from PA.Z.
```

## 8.6. Conformance

### 8.6.1. Conformance declarations and identity

In a Type declaration, `Self is C` declares conformance to `C` (compiler-intrinsic Contracts: [§8.7.1](#871-compiler-intrinsic-contracts)); same-named members alone never do.

**Conformance Identity** is the pair of the complete conforming Type and the bound Contract reference: the Contract declaration together with the normalized bindings of its own Type arguments and of its enclosing declarations. `Self is Multipliable<f64>` and `Self is Multipliable<Vec2>` are two conformances. Associated-Type bindings do not distinguish conformances, and an explicit conformance and one implied by refinement with the same bound reference are one conformance; a redundant explicit one is allowed. Contract identity ([§6.1.2](06-declarations-and-containers.md#612-container-fragments)) identifies declarations only. Equal bound references of one Type are duplicates, whatever their conditions; other direct conformances to one Contract declaration follow [§8.9.1](#891-direct-conformance-collisions).

An unconditional conformance of a generic Type must hold for every binding its Constraints allow (conditional: [§8.8](#88-conditional-conformance)); instantiations never validate it. Ancestor conformances and mappings are verified. A conformance generates no implementations except intrinsic derivations ([§8.7.1](#871-compiler-intrinsic-contracts)) and Property witness bridges ([§11.9.2](11-properties.md#1192-standard-access-witnesses)). Its requirement-to-member mapping and associated-Type bindings are fixed by the conformance, and calls through the Contract use them, never a lookup at the caller or after instantiation. There is no external registration, replacement conformance or default implementation.

```kimi
contract C

struct Twice<T>
    Self is C
    Self is C when T is Copy        // Error: the same bound reference twice.
```

### 8.6.2. Inherited conformance

A verified conformance of `B` to `C` is inherited by `D : B` only when its mapping satisfies every requirement with the requirement-side `Self` replaced by `D`. A member declared in `A` keeps `A` as its implementation-side `Self`, even through `A -> B -> D`, with the base-path substitutions applied. Only an eligible borrowed receiver may use base-subobject projection ([§9.6.1](09-names-signatures-and-access.md#961-base-subobject-receiver-projection)); other parameters gain no base conversion, and owning receivers no slicing. Projected calls require ObjectCallCompatible Proven ([§12.7](12-expressions.md#127-object-receiver-compatibility)).

| Requirement shape | Inherited by `D` |
| --- | --- |
| `Self` only in a borrowed receiver | Yes, if the other checks succeed |
| `other: ref/Self` | No: `ref/A` does not match `ref/D` |
| `func empty() -> Self` | No |
| An owning `Self` receiver | No |
| `Self.Element` normalizing to the same Type | Yes |

A failed path does not invalidate `D`; `D`'s conformance is Refuted only when every explicit, conditional and refinement path is proven to fail, and successful paths must agree ([§8.9.2](#892-merging-paths-and-cycles)). An explicit conformance of `D` uses ordinary lookup, and same-named declarations do not replace inherited mappings.

```kimi
contract Same
    func same(self: ref/Self, other: ref/Self) -> bool

open struct Shape
    Self is Same
    public func same(self: ref/Self, other: ref/Self) -> bool => true

struct Circle : Shape
    Self is Same    // Error: the inherited same takes ref/Shape, not ref/Circle.
```

### 8.6.3. Implementation matching

Duplicate declarations ([§9.1](09-names-signatures-and-access.md#91-signatures)) and overlap checks ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape), [§8.9.1](#891-direct-conformance-collisions)) compare completed headers, after associated-Type completion. Associated-Type inference never depends on these checks, so explicit and inferred bindings give the same result.

After substituting `Self`, the Type's arguments and the available associated Types, the implementation of a function requirement is identified by: exact name; function kind; generic parameter count, kinds and order; task-slot presence ([§24.2.4](24-suspension-and-asynchronous-tasks.md#2424-compatibility)); ordinary parameter count, order and external labels; receiver presence and structure (with the correspondence of [§8.6.2](#862-inherited-conformance)); and normalized parameter Type structure. Type structure includes Semantics, arguments and completed associated Types; Origins are excluded, but kept for compatibility. Identification uses no contravariance or adaptation, never filters by the result and never ranks by preferences, Origins, Constraints or Effects. Zero candidates is a missing implementation; several are ambiguous. `Callable` uses [§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility) instead.

One implementation may satisfy several requirements, including requirements of different bindings of one Contract, when it passes identification and every compatibility obligation for each requirement. Distinct implementation declarations must obey the ordinary coexistence rules ([§9.1](09-names-signatures-and-access.md#91-signatures)); result Types alone never distinguish them. Conformance collisions and agreement still follow [§8.9](#89-collisions-and-proof-paths), and distinct requirements remain distinct call candidates ([§8.6.4](#864-calls-through-requirements)).

| Aspect | Compatibility obligation |
| --- | --- |
| Constraints | The Type, conformance and requirement premises prove the implementation's Constraints and conditions. |
| Inputs | Every input the requirement admits stays valid; a call-local borrow is not narrowed to `static`. |
| Result Type | Result compatibility ([§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)). |
| Result Origins | At least the required guarantees, and no mutable-static anchor ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). |
| Access | Usable throughout the conformance's domain ([§9.4.6](09-names-signatures-and-access.md#946-conformance-accessibility)). |
| Context and Effects | No stronger calling context; a safe requirement never needs unsafe. No effect outside the applicable bounds ([§8.11](#811-effect-bounds)). |

Bounds are checked against each implementation's effect summary ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)), with destruction as in [§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release); an effect the summary cannot classify is a conflict, never an empty effect. Bounds are checked when [§8.11.4](#8114-checking-and-use) states. Origin contracts use [§15.3.7](15-ownership-and-lifetime-analysis.md#1537-contract-comparison). Compatibility inserts no conversion, Copy, Borrow or erasure, and a failure is a conformance error, never a reason to try another implementation. Properties and accessor witnesses follow [§11.9](11-properties.md#119-contract-property-requirements) and [§12.7](12-expressions.md#127-object-receiver-compatibility).

```kimi
contract Maker<T>
    func make() -> T

contract Both: Maker<i32>, Maker<string>

struct Stop
    Self is Both
    public func make() -> Never => $abort("No value") // One implementation satisfies both requirements.

struct Incomplete
    Self is Both
    public func make() -> i32 => 1                   // Error: does not satisfy Maker<string>.
    // public func make() -> string => "one"         // Error if added: duplicate Signature.

struct S
    Self is Source
    public func read(self: ref/Self) -> i32 => 1    // Infers Source.Element is i32.
    public func take(value: Self.Element) -> () => ()
    public func take(value: i32) -> () => ()        // Error: duplicate of the previous take.
```

### 8.6.4. Calls through requirements

Under `T is C`, generic code may use the requirements and associated Types of `C` and its ancestors. A Type function is called through the Type: under `T is Empty`, where `Empty` declares `func empty() -> Self`, write `T.empty()`; `Empty.empty()` is invalid, since the Type is never inferred from the expected result. `T.compare` under `T is Comparable` is the requirement's Function Item with `Self` bound ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)). Distinct requirements are distinct call candidates, and a call that matches several is ambiguous. There is no qualified requirement call; call through a helper constrained by one Contract.

```kimi
contract ResetA
    func reset(self: uniq/Self)
contract ResetB
    func reset(self: uniq/Self)

func resetA<T>(x: uniq/T)
    T is ResetA
    x.reset()          // Valid: only ResetA.reset is available.

func resetBoth<T>(x: uniq/T)
    T is ResetA
    T is ResetB
    x.reset()          // Error: ResetA.reset and ResetB.reset are distinct requirements.
```

## 8.7. Compiler-intrinsic and closed Contracts

### 8.7.1. Compiler-intrinsic Contracts

Copy, Owned, Callable, Sealed, ObjectPayload, PrimitiveInteger and Transferable are the **compiler-intrinsic Contracts** declared by Kimi ([§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations)). Each has only the effects defined for it; a user Contract with the same name or members gains none. In a struct or enum declaration:

| X | `Self is X` (optionally `when P`) |
| --- | --- |
| Copy | Requests derived Copy ([§3.5.2](03-types-and-values.md#352-copy-capability)). |
| Owned | Checked assertion: the Type must be Owned ([§15.2.3](15-ownership-and-lifetime-analysis.md#1523-static-and-owned)) for every binding its Constraints (and `P`) admit. It grants nothing. |
| Sealed, ObjectPayload, PrimitiveInteger, Callable, Transferable | Error. These are decided by the Type's structure alone. |

In a struct or enum declaration, `Self is not ObjectPayload` is the only negated `Self` clause; it is the opt-out of [§8.7.3](#873-objectpayload). In a Contract, `Self is X` (and `Self is not ObjectPayload`) is a requirement that every conforming Type satisfies X by X's own rules; listing X as a refinement parent (`contract C: Copy`) means the same as `Self is X` in its body.

The identities of `Utf8Format`, `BufferWriter`, `Equatable`, `Comparable`, the iteration and the Indexable Contracts are fixed by [§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations); they are ordinary Contracts with their declared bounds. The arithmetic Contracts are ordinary declarations with built-in witnesses ([§13.3.3](13-operators-and-assignment.md#1333-arithmetic-contracts)). Conformance proves only the stated requirements: documented laws such as symmetry of equality are not enforced, and nothing is proven about initialization, Loans or representation.

```kimi
struct Pair<T>
    Self is Copy when T is Copy     // Derived Copy.
    Self is Owned when T is Owned   // Checked; grants nothing.
    var first: T
    var second: T

struct Plain
    Self is Sealed                  // Error: Sealed is structural.
```

### 8.7.2. Sealed

`Kimi.Sealed` holds for a normalized Type exactly when its outer Semantics is `owner` and its Core is valid, not Never and not an open struct. Scalars, `string`, Unit, enums, Tuples, fixed arrays, collections, Function Items, concrete Closures and common Function Types are sealed. Only the outer Core is tested: a non-open `Cell<T>` is Sealed even when `T` is open. Sealed implies neither Copy, Owned, exclusive access nor Object Target, and `T is not Sealed` proves neither `owner` nor an open Core.

### 8.7.3. ObjectPayload

`Kimi.ObjectPayload` holds exactly when the outer Semantics is `owner`, the Core is valid and not Never, and neither the Core's declaration nor any base opts out. Only the outer Core is tested: `Box<Parser>` qualifies when `Box` does not opt out. Every Core that satisfies it can be a concrete object payload ([§3.3.1](03-types-and-values.md#331-object-views-and-identity)); object ownership needs no heap allocation ([§3.3](03-types-and-values.md#33-semantics)).

**Opt-out.** `Self is not ObjectPayload` in a struct or enum's leading Constraint region opts out. It is unconditional, stands alone, appears once and must resolve to `Kimi.ObjectPayload`. Derived structs inherit the opt-out, and nothing restores the capability.

**Proof.** With the outer Semantics, Core and opt-out determined, the judgment is direct even with unbound arguments: `Box<T>` is Proven, an opted-out `Parser<T>` and `ref/T` are Refuted. For a pair's own `s/T`, the outer Semantics is judged from the admitted set of `s` ([§8.3.4](#834-semantics-admitted-set)). Otherwise (Type parameters, pair targets, unresolved projections) the only evidence is a premise `T is ObjectPayload` or its refinement. A Proven `T is ObjectPayload` makes `T` a complete value Type with `owner` Semantics; it implies neither Copy, Owned, Sealed nor Transferable.

**Object Target.** `X` is an Object Target when `X is ObjectPayload` is Proven, when `X` is a runtime Contract View Target ([§8.12](#812-runtime-contracts)), or when `X` is a pair target with pair evidence ([§8.1.1](#811-type-slots-and-pair-parameters)). Every object form over `X` (`obj`, `rc`, `arc`, `objref`, `objuniq`, including `Weak<rc/X>`, casts and runtime `is` tests) requires an Object Target. Creating an object from a value ([§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)) requires ObjectPayload itself; operations on an existing handle re-prove nothing. An opted-out Core is never an Object Target, even in its own declaration, and cannot conform to a Contract that proves `Self is ObjectPayload`; values, borrows, storage inside other Types (`obj/Box<X>`) and captures are unaffected.

**Contracts.** A Contract whose requirement signatures form object forms over `Self` must have `Self is ObjectPayload` Proven, declared or inherited; `T is C` then supplies `T is ObjectPayload`. A positive and a negative form together are contradictory evidence.

```kimi
struct Parser
    Self is not ObjectPayload
    public var pos: i32 = 0

func boxed<T>(value: T) -> obj/T
    T is ObjectPayload
    return Kimi.Intrinsics.makeObj(value@move)

// let o = boxed(Parser.init())   // Error: Parser is not ObjectPayload.
```

### 8.7.4. PrimitiveInteger

`Kimi.PrimitiveInteger` has no members. It holds exactly for `owner` Semantics over the twelve integer Types (`i8` to `i128`, `u8` to `u128`, `isize`, `usize`). A proven `T is PrimitiveInteger` supplies:

- Scalar, Copy, Owned, `Position` ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions)) and the built-in Equatable, Comparable and Utf8Format conformances; a reference to `T` is read by value ([§3.5.3](03-types-and-values.md#353-value-read)).
- The arithmetic Contracts and operators of [§13.3.3](13-operators-and-assignment.md#1333-arithmetic-contracts) and [§13.3.4](13-operators-and-assignment.md#1334-route-selection); unary `-` is unavailable.
- Checked `value@U` between PrimitiveInteger Types, `value@Wrapping<T>` and back, and `value@wrap<U>` ([§13.5.4](13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions)).
- Untyped literals 0 through 127, which fit all twelve Types ([§12.3](12-expressions.md#123-literal-typing)).
- `Wrapping<T>` ([§3.1.1](03-types-and-values.md#311-wrapping-integer-types)) with these capabilities and unary `-`.

The body is checked once against these capabilities; each instantiation performs the operations of its concrete Type (for example its overflow checks). `T is i8 or u8 or ...` supplies none of this, because proof has no case analysis.

```kimi
func sum<T>(values: Range<T, T>) -> T
    T is PrimitiveInteger
    var total: T = 0
    for value in values
        total += value     // -value would be an error: unary - needs a signed Type.
    return total

let small = sum(1..5@u8)   // 10
```

### 8.7.5. Closed Contracts

`Kimi.Position` and `Kimi.PositionRange` are **closed**: their conforming Types are fixed by [§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations). `Position` is satisfied by the twelve integer Types, `FromEnd<T>`, `Start` and `End`; `PositionRange` by `Range<S, E>`, `ClosedRange<S, E>` and `ResolvedRange` ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions), [§4.6.3](04-arrays-indexing-and-slices.md#463-resolution)). A conformance to them in any other Type is an error. Integers gain no members, and every implementation reads only its boundaries and the length and has no other effect.

```kimi
struct Offset
    Self is Position    // Error: Position is closed.
```

## 8.8. Conditional conformance

### 8.8.1. Conditions and implementation blocks

A generic struct or enum may declare `Self is C when P` at a member position. The Type's Constraints `D` govern every use; `P` governs only this conformance. `P` is a comma-separated list of clauses that must all hold, with subjects among the enclosing parameters, pair binders and targets, and projections, and positive requirements joined by `and` (grammar: `ConformanceCondition`, [§F.6](appendices/F-syntax-summary.md#f6-declarations)); `or`, `not` and value tests are errors. An identity atom between enclosing parameters makes them one Type in the conformance scope ([§8.2.3](#823-type-identity-and-identity-premises)).

An optional indented **implementation block** declares members in the Type's namespace: functions, computed members where the Type kind allows them and associated-Type specifications, but no Fields, Cases, constructors, `drop`, nested Types or conformances. Layout never changes. `P` is a precondition of each block member only. The block may be omitted when existing members or an intrinsic derivation satisfy `C`. There is no external registration, extension declaration or partial specialization.

```kimi
enum Option<T>
    Self is Copy when T is Copy   // Option<i32> is Copy; Option<Resource> stays usable.
    Some(T)
    None

struct Box<T>
    var value: T
    Self is Describe when T is Describe
        public func describe(self: ref/Self) -> string
            return self.value.describe()
```

### 8.8.2. Verification and use

At definition, `C` is verified under `D` and `P` for every admitted binding, even if no Type satisfies `P`. Implementations are identified by [§8.6.3](#863-implementation-matching); completion uses frozen path-wide premises ([§8.5.5](#855-inference)). Each requirement is checked in a **conformance scope** of `D`, `P` and the requirement's premises, with both sides normalized there; under `I is Iterator`, `Option<I.(LendingIterator).LentItem(step)>` is `Option<I.Item>`. An implementation outside the block keeps its own signature.

At a use, a proof of `P` for the substituted arguments enables the verified conformance; implementations are never selected again. A block member, or a conditional member ([§7.5](07-functions-and-callable-values.md#75-function-constraints)) with its clauses joined to `P`, is applicable after lookup and inference only as `P` is judged; the same applies to function references and computed access:

| `P` | Member applicability | Conformance use |
| --- | --- | --- |
| Proven | Satisfied | Path available |
| Refuted | Inapplicable | No conformance from this path |
| Unknown | Unproven ([§10.1](10-overload-resolution-and-inference.md#101-candidate-applicability)) | Neither conformance nor absence |
| Error | Error | Error |

Conditions never change the committed lookup layer, an inapplicable member never reopens lookup, condition strength does not rank candidates, and a later Loan failure never causes reselection. Step 5 of [§10.1](10-overload-resolution-and-inference.md#101-candidate-applicability) owns how Unknown applicability affects selection.

### 8.8.3. Parent Contracts

For `Child: Parent`, Parent evidence comes from either the direct Parent conformance or the Child conformance, and the child path needs only the child's conditions. Every parent implementation that the child path uses must therefore be applicable under the child's conditions. A member inside the parent's conditional block carries the parent's condition and cannot serve. Implement it outside the block, either unconditionally or with a condition that the child's conditions prove. Paths must agree ([§8.9.2](#892-merging-paths-and-cycles)). Block members with equal Signatures that differ only in conditions are duplicates.

```kimi
contract Named
    func name(self: ref/Self) -> string
contract Tagged: Named
    func tag(self: ref/Self) -> i32

struct Tag<T>
    var value: T
    public func name(self: ref/Self) -> string => "tag"   // Outside both blocks.
    Self is Named when T is Copy
    Self is Tagged when T is Equatable
        public func tag(self: ref/Self) -> i32 => 1
// Moving name into a Self is Named when T is Copy block makes the Tagged declaration an error.
```

## 8.9. Collisions and proof paths

A conformance is identified as [§8.6.1](#861-conformance-declarations-and-identity) states; a requirement or associated Type by its defining declaration and the bindings of its defining Contract. Bindings are substituted along refinement, and inherited input conditions become the child's public inputs.

### 8.9.1. Direct conformance collisions

Direct conformances and merging paths use one collision test:

1. Group by Contract declaration; different declarations never collide. Normalize with the identity premises and Origin rules.
2. Unify parameters and fixed structure (nominal Types, Tuples, Function Types and applications of a determined Semantics) with an occurs check; local function binders are renamed apart.
3. A mismatch of fixed structure proves non-collision; otherwise the substitution is a collision condition.
4. The remaining unresolved terms are residual conditions: unresolved associated projections, applications of a pair binder, non-constant length expressions, Origin intersections and orderings, and other non-free terms. Unresolved residuals mean a possible collision; `A.Item` and `B.Item` may be equal.

Every pair of direct conformances must be proven non-colliding under the Type's Constraints; mutually exclusive `when` conditions do not exempt them.

```kimi
struct Family<T>
    public contract Marker

struct Good
    Self is Family<i32>.Marker
    Self is Family<string>.Marker

struct Bad<A, B>
    Self is Family<A>.Marker
    Self is Family<B>.Marker   // Error: A and B can be the same Type.
```

### 8.9.2. Merging paths and cycles

Refinement paths to the same ancestor declaration with the same normalized bindings give one requirement and one associated Type. Their associated-Type bindings, Origin conditions and implementation mappings must agree; if agreement cannot be proven, the definition is an error. Paths with different bindings, and independent declarations with the same name, stay distinct requirements.

Other combinations of direct, refined and inherited conformances add each path's conditions to the collision conditions; paths that may meet must agree: `Family<A>.C` and `Family<B>.C` cannot specify one associated Type as `i32` and `string`. A refinement path that revisits a Contract declaration, even with different arguments (`Outer<T>.C` to `Outer<List<T>>.C`), is an error. Different instantiations of one Contract on separate paths (for example `contract C: A<i32>, A<i64>`) are not a cycle.

## 8.10. Callable constraints

`F is Callable<r, S>` requires calls with receiver access `r` (`ref`, `uniq` or `owner`) and signature `S`, `(A1, ..., An) -> R` or `(task; A1, ..., An) -> R` ([§24.2](24-suspension-and-asynchronous-tasks.md#242-task-slots)); `Callable<S>` means `Callable<ref, S>`. Function Items, concrete Closures and common Function Types with compatible signatures ([§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)) qualify; no `call` member is searched, and `F` need not be Copy or Owned. The result may keep input Origins but cannot borrow the hidden callable receiver.

| `r` | Receiver acquired | Callee may be |
| --- | --- | --- |
| `ref` | `ref/F` | Shared |
| `uniq` | `uniq/F` | Shared or Exclusive |
| `owner` | `F` by value | Shared, Exclusive or Consuming |

**Per-call Origins.** An omitted Origin on each direct borrowed parameter of `S` is bound independently per call, and conformance must hold for every call-time Origin; nested and capture Origins stay fixed. `S` writes no `during` ([§15.3.1](15-ownership-and-lifetime-analysis.md#1531-borrow-annotations-and-binding-sets)). Aggregate occurrences in `S` may name binding sets owned by the containing declaration; their input slots must be fixed by complete Types or by equality to outer Origin expressions. Omitted result Origins come from the per-call input Origins ([§15.4](15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision)), their meet for several inputs. A result that requires exclusive access keeps the corresponding exclusive Loan.

**Calls.** `ref` calls through shared access. `uniq` keeps exclusive access (the callee is a Receiver Expression, [§7.3](07-functions-and-callable-values.md#73-receivers)). `owner` acquires by Copy or `@move`, borrows the value for a Shared or Exclusive body or transfers it to a Consuming one (the acquired temporary is exclusively accessible even when copied from a `let` binding), and destroys the rest after the call; instantiation never turns it into a borrow.

**Several Constraints.** The distinct normalized signatures of the Callable Constraints on `F` are the call's candidates, each with the weakest receiver declared with it. The call is an error unless they share one receiver, task-slot shape ([§24.2.3](24-suspension-and-asynchronous-tasks.md#2423-not-a-parameter-one-shape-per-function-group)) and parameter acquisition shape ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)); they are then compared under [§10.1](10-overload-resolution-and-inference.md#101-candidate-applicability) and [§10.4](10-overload-resolution-and-inference.md#104-best-candidate). Different signatures are not contradictory evidence ([§8.3.2](#832-proof-rules)); a Type bound to `F` must satisfy each of them. Callable effect bounds are defined in [§8.11.6](#8116-callable-effect-bounds).

```kimi
func applyTwice<F>(value: i32, transform: uniq/F) -> i32
    F is Callable<uniq, (i32) -> i32>
    let first = transform(value)
    return transform(first)

let count: i32 = 0
var bump = func [var count] (value: i32) -> i32
    count += 1
    return value + count
let result = applyTwice(10, bump@uniq)   // 13
```

## 8.11. Effect bounds

An **effect bound** is a guarantee about what a call may access, which every conforming implementation satisfies and generic callers rely on. The vocabulary is closed: `confined` and `preserves results`. The classification is the same in safe and unsafe code ([§5](05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory)).

### 8.11.1. Declarations

Bounds are declared in four positions (grammar: `EffectClause`, `EffectSpecification`, [§F.6](appendices/F-syntax-summary.md#f6-declarations); keywords: [§F.3](appendices/F-syntax-summary.md#f3-keywords)):

- an `effect` clause in a function requirement's Constraint region bounds that requirement;
- a Contract item `effect A.r bound` bounds the requirement `r` inherited from the ancestor `A`, which must identify exactly one function requirement;
- an `effect` clause under a Callable Constraint bounds calls through it ([§8.11.6](#8116-callable-effect-bounds));
- an `effect` clause in the leading region ([§7.5](07-functions-and-callable-values.md#75-function-constraints)) of an original virtual function bounds the slot; overrides inherit it.

An effect item in any other body, including an override, a non-virtual function or after the leading region, is an error. Implementations cannot add or remove a requirement's bound. The same bound twice for one requirement in one Contract is an error; restating an ancestor's bound forms one guarantee. `T is C` guarantees exactly the bounds of `C` and its ancestors.

```kimi
contract Take
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>

contract StableTake: Take
    effect Take.take preserves results

func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is StableTake
    let first = source.take()
    let second = source.take() // Valid: the bound excludes the Loans of first.
    return (first@move, second@move)

open struct Base
    public virtual func score(self: objref/Self) -> i32
        effect confined          // Slot bound, inherited by overrides.
        return 1

struct Derived : Base
    override func score(self: objref/Self) -> i32
        // effect confined       // Error: an override cannot declare a bound.
        return 2
```

With only `S is Take`, the second `take` is rejected while `first` is live.

### 8.11.2. `confined`

The effect summary ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)) of a `confined` implementation contains no **environment effect** and no unclassifiable effect. An environment effect obtains authority over mutable state from the environment instead of from the inputs; accesses with authority once obtained are no further effects.

| Operation | Classification |
| --- | --- |
| Following an input (receiver, argument or task slot), or the input itself | Not an environment effect; Loan checking still applies. |
| Using Storage created in the call, including `Raw.allocate` ([§5.6](05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations)) | Not an environment effect. |
| `@raw` of a Place ([§5.4](05-raw-pointers-and-unsafe-memory.md#54-addresses-and-conversions)) | Classified like the Place's other accesses. |
| Converting between raw pointer Types or from a raw pointer to `usize`; a raw access ([§5.4](05-raw-pointers-and-unsafe-memory.md#54-addresses-and-conversions)) | Not an environment effect; raw accesses are compared with Loans by [§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places). |
| Reading or borrowing an immutable static whose Type is proven Transferable ([§15.2.4](15-ownership-and-lifetime-analysis.md#1524-transferable)) | Not an environment effect; its initializer enters the summary. |
| Reading or borrowing any other immutable static (Transferable refuted or unproven) | Environment effect. |
| Any access to a mutable static Field, including first-access initialization | Environment effect. |
| A Kimi operation that accesses state outside the program, such as Console output | Environment effect. |
| Calling a foreign function ([§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)); converting an integer to a pointer ([§5.5](05-raw-pointers-and-unsafe-memory.md#55-integer-addresses)) | Environment effect. |
| Destroying or replacing a value whose destruction depends on a Type the definition leaves abstract, such as a Type parameter or associated-Type projection not proven Copy | An effect that cannot be classified: a conflict under either bound. |
| A call or a destruction whose environment effects are unknown, including erased destruction ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)) | An effect that cannot be classified: a conflict under either bound. |

The test is on the static's Type, so a base-object view, runtime-Contract view or common Function Type in that Type counts as hidden `rc` or raw-pointer content, and an abstract Type needs a Transferable premise. Allocation and Abort are permitted. A pointer obtained from the environment is an effect even if unused. A foreign call also makes the accesses its signature permits through its arguments; a `preserves results` implementation may call a foreign function when those accesses do not conflict with the Loans of earlier results. Task operations are classified in [§22.3.3](22-core-execution-and-foreign-functions.md#2233-cancellation-and-waits). A static borrow passed in as an argument is authority from the input.

```kimi
contract Sink
    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        effect confined

group Metrics
    var puts: isize = 0
    public func record() => Metrics.puts += 1

struct Item
    public var value: i32 = 7

group Shared
    public let limit: i32 = 10
    public let node: rc/Item = Kimi.Intrinsics.makeRc(Item.init())

struct Reader
    Self is Sink
    var count: isize = 0
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        self.count += 1              // Valid: authority from self.
        let a = Shared.limit         // Valid: i32 is Transferable.
        let b = Shared.node@ref      // Error: rc/Item is not Transferable.
        Metrics.record()             // Error: reaches the mutable static Metrics.puts.
        return .Ok(())

contract Reset
    func reset(self: uniq/Self)
        effect confined

struct Slot<T>
    Self is Reset
    var item: Option<T>
    public func reset(self: uniq/Self)
        self.item = .None   // Error: destroying the old T has unknown effects.

struct CopySlot<T>
    T is Copy
    Self is Reset
    var item: Option<T>
    public func reset(self: uniq/Self)
        self.item = .None   // Valid: a Copy value has no destruction effects.
```

### 8.11.3. `preserves results`

**Eligibility.** The receiver is a borrow (`ref`, `uniq`, `objref` or `objuniq` of `Self`), and in every admitted binding no result of the canonical contract ([§15.3.7](15-ownership-and-lifetime-analysis.md#1537-contract-comparison)) depends on the receiver Loan of its call or on the receiver's Storage ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)), judged after normalizing premises, result Origin defaults ([§15.4.3](15-ownership-and-lifetime-analysis.md#1543-result-defaults)) and aliases. Otherwise the bound is an error.

**Guarantee.** Under `T is C` with `preserves results` on `r`, a call of `r` on a value `v` conflicts with no Loan kept by a result of an earlier call of `r` on `v`, including Loans kept after transfer or Reborrow of that result. The excluded effects include reads, writes, borrows, the Loans of the new result, access to statics and captures, and cleanup inside the call; dependencies added to a result after it was returned are outside the guarantee. Destroying or replacing `v`, other requirements and unrelated Loans are outside it; it promises no purity. Conformance compares the implementation's summary with the Loans its results may keep, using region splitting ([§15.6.3](15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting)).

```kimi
contract Peek
    associate Item
    func peek(self: ref/Self) -> Option<ref/Self.Item>
        effect preserves results // Error: the elided result Origin is self.
```

### 8.11.4. Checking and use

A conformance supplies evidence ([§8.3.2](#832-proof-rules)) once its implementations are identified and their signatures, associated Types and premises are compatible ([§8.6.3](#863-implementation-matching)). Its effect bounds are a separate obligation of the conformance, checked against the completed transitive summaries, including the recursive fixed point of [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls). A bound violation makes the conformance declaration an error; it never changes a selection that used the conformance as evidence. Ancestor bounds are checked through the ancestor conformances, conditional ones under their conditions.

A requirement call without a known concrete callee uses the bounds that the Contracts proven by its premises declare for the requirement, never bounds of implementations. Its effects, in implementation checks and at callers, are:

1. *Through inputs:* every Place reachable from the receiver and arguments in each parameter's access mode, only shared past a shared layer ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)); an abstract input may reach every Loan its Origins may denote; destroying an owned handle argument adds [§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release).
2. *Environment:* none with `confined`; otherwise unknown, affecting static Loans as [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls) states. A task call is also compared with static Loans ([§24.3](24-suspension-and-asynchronous-tasks.md#243-task-calls)).
3. *Earlier results:* with `preserves results`, the Loans kept by earlier results of the same requirement on the same value are excluded.

A possible conflict with a live Loan ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)) is an error at the later call. An invalid effect item supplies no bound.

```kimi
contract Feed
    func next(self: uniq/Self) -> Option<i32>
        effect confined

group Feeds
    public func pull<F>(feed: uniq/F) -> Option<i32>
        F is Feed
        return feed.next()

struct Countdown
    Self is Feed
    var left: i32
    public func next(self: uniq/Self) -> Option<i32>
        if self.left == 0 => return .None
        self.left -= 1
        return Feeds.pull(self)   // Valid: Countdown is Feed is evidence; the bound is checked on the completed summary.
```

### 8.11.5. Delegation

When an implementation of `r` with `preserves results` is verified, a Loan that its result keeps from the result of a requirement `d` called on a value `w`, reached from `self` through a Field path (borrowed Fields included), counts as a Loan of an earlier result of `d` on `w`; rule 3 of [§8.11.4](#8114-checking-and-use) excludes it when `d` has `preserves results` for `w`. If the body replaces, swaps or moves a value on the path to `w`, nothing is excluded. Loans from arguments, other requirements or other values are compared normally.

```kimi
struct Drain<J>
    J is Iterator
    Self is StableTake
    associate Take.Item is J.Item
    var inner: J
    public func take(self: uniq/Self) -> Option<J.Item>
        return self.inner.next()                // Valid.

struct Merge<J>
    J is Iterator
    Self is StableTake
    associate Take.Item is J.Item
    var left: J
    var right: J
    public func take(self: uniq/Self) -> Option<J.Item>
        match self.left.next()                  // Error: may conflict with an earlier result from right.
            .Some(let item) => return .Some(item@move)
            .None => return self.right.next()
```

### 8.11.6. Callable effect bounds

A Callable Constraint may carry an indented list of `effect` clauses (grammar: `CallableEffects`, [§F.6](appendices/F-syntax-summary.md#f6-declarations)) when its whole requirement is one `Callable<...>`. The list is allowed in function, struct, enum and function-requirement Constraint regions (conditional-member premises included), not in Contract-wide clauses, associated-Type declarations or conformance conditions, and it does not end a leading region ([§7.5](07-functions-and-callable-values.md#75-function-constraints)). Each kind appears at most once in one Constraint; `preserves results` needs receiver `ref` or `uniq`.

A Type bound to `F is Callable<r, S>` must satisfy each bound for a call acquired under `r`, including acquisition and, for `owner`, destroying the rest. Captures are reached through the receiver, an input. A bound covers only the call; destroying a value elsewhere contributes its destruction effects to the enclosing summary ([§16.3.3](16-scope-exit-and-destruction.md#1633-handles-and-object-release)). `preserves results` excludes conflict with Loans of earlier results of the same callable value.

Applicability ([§10.1](10-overload-resolution-and-inference.md#101-candidate-applicability)) and proofs ([§8.3](#83-constraint-proof)) ignore bounds. A bound is an obligation of the selected call, function reference ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)) or Type formation, checked on completed summaries. A selected callable argument that violates an effect bound of its Callable Constraint is an error at that argument or Type argument.

| Bound Type | Evidence |
| --- | --- |
| Function Item, nonvirtual | Its effect summary ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). |
| Function Item of a virtual slot | Slot bound, for `confined` only ([§6.4.6](06-declarations-and-containers.md#646-effects-and-contract-witnesses)). |
| Function Item of a requirement | Available bounds, for `confined` only. |
| Concrete Closure | Its body summary with acquisition and environment destruction. |
| Type parameter or projection | A premise on it with the same signature, an equal or stronger receiver (`ref` < `uniq` < `owner`) and the bound. |
| Common Function Type | None. |

Binding a Function Item or Closure whose result may carry a mutable-static anchor to a Callable-constrained Type is an error ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). A call through `F` uses the bounds of premises with its signature and an equal or stronger receiver, under rules 1 to 3 of [§8.11.4](#8114-checking-and-use). Callers compose a declaration's calls through Callable inputs with the actual argument ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)). An implementation cannot add an unrequired Callable bound.

```kimi
func apply<F>(transform: ref/F, value: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    return transform(value)

contract Mapper
    func map<F>(self: uniq/Self, transform: ref/F) -> i32
        F is Callable<(i32) -> i32>
            effect confined      // Bounds calls of transform.
        effect confined          // Bounds the requirement map.
```

## 8.12. Runtime Contracts

Runtime Contract Views (Object Views over a Contract) need a runtime-use designation that is not yet defined, so they cannot be formed in this revision; these rules apply to that extension. A bare Contract name is never a value Type.

**ObjectViewCompatible(C)** holds when `C` is designated for runtime use, has no Type-function requirements, and, with its associated Types fixed, each instance requirement is a safe method or accessor with a non-consuming object receiver, uses `Self` only in the receiver, has no unbound associated Types or method generics, and has Types and Origins expressible without the hidden Type (through the receiver, inputs and `static`). Each getter result and setter requirement is checked separately. Forming an object form over `C` requires it. Erasing a value into a View requires ObjectPayload ([§8.7.3](#873-objectpayload)) and Owned ([§15.8.1](15-ownership-and-lifetime-analysis.md#1581-object-payload-erasure)). **Implements(D, C)** needs an explicit or inherited conformance ([§8.6.2](#862-inherited-conformance)) with ObjectCallCompatible Proven ([§12.7](12-expressions.md#127-object-receiver-compatibility)); the unique conformance for the bound Contract reference ([§8.6.1](#861-conformance-declarations-and-identity)) is used.

```kimi
contract Speaker
    Self is ObjectPayload
    func speak(self: objref/Self) -> string

func announce(value: objref/Speaker) -> string   // Error in this revision: runtime Contract Views cannot be formed.
    return value.speak()
```

## 8.13. Explicit full function specialization

An **explicit full specialization** supplies the implementation of a generic function for one closed set of static generic arguments. It keeps the call contract but may behave differently, and a matching specialization must be used.

### 8.13.1. Declaration and target

`specialize func` declares one: a single unqualified Name, explicit arguments for every slot in order (lengths by [§4.4](04-arrays-indexing-and-slices.md#44-function-length-parameters)), a task slot exactly when the original has one, typed parameters, an optional result and a body. It declares no Type parameters and cannot see the original's names. An omitted result is Unit. The arguments must be closed: no unbound Type parameter or pair binder, unresolved projection or outer generic parameter. The target is a generic root function, Type function or method with an ordinary body, not a constructor, `drop`, accessor or operator; there is no partial, conditional or Container-argument specialization.

The specialization and its target belong to the same declaration scope: a root function's SourceDocument execution scope ([§6.1.1](06-declarations-and-containers.md#611-source-root-and-nested-containers)), or the same Container for a member. The target is the unique function there with the same name, kind and slot count whose substituted task slot, receiver and parameters match the header's normalized structure, Origins excluded. Zero or several matches are errors; results, Constraints and order never break ties.

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func inspect<T>(value: T) -> () => ()
func inspect<T>(value: i32) -> () => ()
specialize func inspect<i32>(value: i32) -> () => ()
// Error: both declarations have the same substituted input structure.
```

### 8.13.2. Inherited contract

The header restates the original's substituted receiver shape ([§7.3](07-functions-and-callable-values.md#73-receivers)), parameters and result without adaptation, and its external names; internal names may change with `external => internal: Type`. Access, defaults, the `!` boundary, Constraints, Attributes, Safety, effect bounds and Origin binders are inherited and never written. Callers keep the original's defaults: `find<i32>(10)` evaluates the original default, then runs the specialization. The body must work for every Origin binding the original admits. A specialization cannot add Origin restrictions, narrow applicability or replace a required proof; it may leave ObjectCallCompatible ([§12.7.2](12-expressions.md#1272-implementation-families)) NotProven. Override headers follow the same discipline ([§6.4.1](06-declarations-and-containers.md#641-declarations)).

### 8.13.3. Selection

The selection key is the original declaration with its normalized static arguments, excluding only Origins; `obj/D` and `rc/D` differ. Two specializations with one key are an error. Calls and function references use ordinary lookup, overload resolution and the original contract; specializations are never candidates. Once the arguments determine a key, the matching specialization runs, whether the arguments were known at the call or after generic substitution; Dynamic Types never matter. A specialization has no name and cannot be bypassed; calling the same function with the same arguments inside it recurses. Specializations belong to the original's Kotonoha and declaration scope (§8.13.1), and declaration or loading order never affects selection.

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let number: i32 = 10
let result = forward<i32>(number@ref) // 1: the i32 specialization runs.
```

## 8.14. Generic access effects

An acquisition or adaptation has a statically unique **Access Effect**: an acquisition (Copy or Move), a Loan action (shared Borrow, exclusive Borrow or Reborrow), and its target Place and Origin dependencies. Consume is not a kind of acquisition; it names the static check of [§15](15-ownership-and-lifetime-analysis.md#15-ownership-and-lifetime-analysis) that a use may perform an access operation of [§15.1.5](15-ownership-and-lifetime-analysis.md#1515-access-operations-and-acquisition). Exclusive call adaptations use reservation ([§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations)).

- A bare Place follows [§3.5](03-types-and-values.md#35-copy-and-move) in each admitted case: it Copies where Copy is proven and Reborrows where its Type is proven a `uniq` or `objuniq` reference. A case with neither is an error at definition checking, never an inferred Move.
- `@copy` Copies with the same evidence; `@move` transfers.
- `@s`, `@s/T` and `@Type` follow the adaptation table ([§13.5](13-operators-and-assignment.md#135-explicit-operations)); where `s` admits `owner`, `value@s` is a same-Type acquisition there and needs Copy evidence.

Unknown Copy is never Non-Copy, and compile-time directives never select Access Effects ([§19.3](19-compile-time-directives.md#193-evaluation-and-selection)). When the admitted cases differ, the plan is a Copy or Reborrow per case, verified per Semantics case ([§8.15.2](#8152-semantics-cases)). `@s` and `@follow` are not inverses: `@follow` selects a Place and never restores a Take.

```kimi
func inspectLocal<s/T>(value: s/T)
    s is owner or uniq
    T is Copy
    let local = value         // owner: Copy; uniq: Reborrow.
    // _ = value              // Error at definition: in the uniq case it conflicts with local.
    _ = local

func invalid<T>(value: T)
    let local = value         // Error at definition: neither Copy nor an exclusive reference is proven.
```

```text
Conceptual cases of inspectLocal:
  s = owner: func inspectOwner<T>(value: T)                         // local is a Copy of T.
  s = uniq:  func inspectUniq<T, origin o>(value: uniq/T during o)  // local is a Reborrow within o.
```

## 8.15. Generic body checking

### 8.15.1. Universal verification

A generic body must be valid for every well-formed Type, length and Origin binding that satisfies its Constraints, the premises of an enclosing conditional conformance and its Signature. It is checked when the definition is checked, uses or not, with the rules of [§8.3](#83-constraint-proof). Types are never enumerated and no hidden Constraint is inferred: what the contract cannot express or prove makes the definition invalid. A generic call proves its callee's requirements from the caller's premises. Specializations cannot rescue an invalid body.

The public obligations of a declaration are the well-formedness conditions of its Signature, schema, associated Types, Constraints and conditional-member premises, never a body requirement such as Copy. A read that leaves its source initialized needs `T is Copy`, a borrow or reinitialization; match Subjects and iteration items follow [§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects). Stored acquisitions, elements and getter results keep their declared Types ([§4.7](04-arrays-indexing-and-slices.md#47-indexing)). ObjectCallCompatible ([§12.7](12-expressions.md#127-object-receiver-compatibility)) is a guarantee of the implementation, not an applicability premise, and instantiations never strengthen it.

```kimi
func transfer<T>(value: T) -> T => value@move   // Moves a Copy or Non-Copy T alike.

func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func invalidTwice<T>(value: T) -> (T, T)
    return (value, value) // Error at definition: Copy is not guaranteed.
```

### 8.15.2. Semantics cases

Legality on a pair layer ([§13.5.5](13-operators-and-assignment.md#1355-follow)) is judged once for all admitted cases. Ownership analysis then runs under each **Semantics case**: one admitted Semantics ([§8.3.4](#834-semantics-admitted-set)) for each pair binder in scope (the body's, its Container's and, for a Closure, its enclosing declarations'). In a case each pair layer is that case's complete Type, with its ordinary Access Effect, dependencies, Loans, cleanup and Closure receiver ([§7.7.3](07-functions-and-callable-values.md#773-call-receiver)). Cases are never merged, so a case that Copies receives no Loan that only a Reborrow case creates. An instantiation is the case its arguments select. Semantics cases are used only by this analysis; they add no premise to the proofs of [§8.3](#83-constraint-proof).

A generic definition is valid when every Semantics case is valid; how an implementation enumerates, shares or factors the cases is not specified. The number of Semantics cases an implementation analyzes for one definition is implementation-defined, and exceeding that limit is a resource failure ([§23.1.1](23-compiler-services.md#2311-categories)), not a ruling that the program is invalid.

```kimi
func count<s/T>(value: s/T) -> i32
    s is ref or uniq          // s is a pair binder; the body is checked for ref and for uniq.
    let view = value@follow@ref
    return 0
```

### 8.15.3. Concrete layout and representation

A concrete Type's layout obligations (layout-mode restrictions such as C-layout zero-sized Fields, alignment limits and size limits) are checked when the program stores, creates, passes or returns a value of that Type by value, or needs its size (Fields, locals, array elements, object payloads, raw allocation). Forming the Type only behind a borrow, raw pointer or object handle, or naming it in a Constraint, does not check them. A failed obligation is a Language error at the use that needs the layout, reported by checking ([§23.2.2](23-compiler-services.md#2322-results-and-acceptance)); an exceeded implementation limit is a resource failure, not semantic invalidity; a layout the implementation cannot generate is reported as Unsupported ([§23.1.1](23-compiler-services.md#2311-categories)).

These checks cover every instantiation that a checked nongeneric body requires, directly or through other instantiations, whether or not the body is reachable or generated; layout obligations are always checked for this checked set. A generic body that no checked nongeneric body instantiates is checked only symbolically. Whether `check`, rather than a build, reports a target's unsupported operations is implementation-defined, but whichever command reports them covers the whole checked set.

Polymorphic recursion, where a generic function calls itself with a growing Type argument, is valid. An implementation that generates one copy per instantiation may reach an implementation-defined limit; that is a resource failure, not an invalid program.

```kimi
func divide<T>(a: T, b: T) -> T
    T is PrimitiveInteger
    return a / b

func unusedA(a: i128, b: i128) -> i128 => a / b        // Unsupported where i128 division is unsupported.
func unusedB(a: i128, b: i128) -> i128 => divide(a, b)  // Same: divide<i128> is in the checked set.

func depth<T>(value: T, n: i32) -> i32
    require n > 0 else => return 0
    return depth<Option<T>>(.Some(value@move), n - 1) + 1   // Valid; may reach a resource limit.
```
