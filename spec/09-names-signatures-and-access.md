# 9. Names, signatures, and access

[Specification index](../SPEC.md)

Name resolution identifies declarations; overload resolution then selects an applicable operation before use-site legality is checked.

| Term | Meaning |
| --- | --- |
| Binding / Name resolution | Associating source names and operations with declarations and meanings. |
| Lookup environment | The declarations and aliases available for a lookup in one scope; extensions are a future design. |

Module terms, including Compilation root, project root and source environment, follow [Chapter 18](18-modules-and-dependencies.md#18-modules-and-dependencies).

Names are resolved before overloads are selected and before it is checked whether an operation can execute:

```text
Name Resolution
├─ Type name -> Type Name Selection -> Type Legality
└─ Call      -> Candidate Applicability -> Best Candidate -> Usage Legality
```

**No backtracking.** Once a stage commits its result, a later failure must not select another lookup stage, declaration or overload. Inaccessible or wrong-role declarations do not commit lookup. Legitimate dependencies may defer resolution; unknown Names and unresolvable dependency cycles are errors.

Language-defined lexical visibility and compile-time selection order apply first. File loading, alias order, candidate enumeration, caching, parallelism and optimization must not change resolution.

During [Mod generation](20-compilation-configuration.md#2072-compilation-and-binding), these rules may produce provisional results from the declarations currently available. Such results commit neither lookup nor overload selection for the final program; final Binding runs after generation completes.

## 9.1. Signatures

A Signature determines whether declarations may coexist in one scope:

| Declaration | Signature |
| --- | --- |
| Type declaration | Name and generic parameter count |
| Function | Name, generic parameter count, and ordered normalized parameter Types including the receiver |
| Constructor | Declaring structure and ordered normalized parameter Types; no ordinary Name or receiver parameter |
| Field / computed / Property Requirement | Name |
| Enum Case | Name within its enum; no payload overloads |

**GenericArity** counts generic argument slots, including function length slots; a pair counts as one. **OriginArity** counts a Type declaration's own scalar schema slots, whether explicit or implicitly discovered. Inherited slots and internal Type-argument dependencies are separate. Function contracts are compared by their completed quantifiers and relations, not a written binder count. It is not a callable-contract equality test (§15.3.7). Origin schemas govern binding and fragment compatibility, not additional overloads: `View<T> {source}` and `View<T> {left, right}` cannot coexist as same-name, same-arity Type overloads.

Types are normalized by resolved Symbol and Kotonoha/version, expanding transparent aliases and resolved associated-Type projections and removing grouping and redundant `owner` prefixes. Every Semantics layer is preserved. Generic expressions are represented structurally by declared binder and slot position:

| Source expression | Normalized expression |
| --- | --- |
| Ordinary `T` | `Slot(i)` |
| Pair `s` / pair `T` | `OuterSemantics(Slot(i))` / `DirectTarget(Slot(i))` |
| Original pair `s/T` | `Slot(i)` |
| `s` applied to another slot `U` | `Apply(OuterSemantics(Slot(i)), Slot(j))` |
| Length `N` / fixed array | `LengthSlot(i)` / `FixedArray(LengthExpression, ElementType)`; lengths compare under [length normalization](04-arrays-indexing-and-slices.md#44-function-length-parameters) |

These rewrites apply recursively, and comparison is by structural alpha-equivalence. There is no simplification from accidental equality after instantiation or from arbitrary Constraint proofs. The pair target `T` alone is not `Slot(i)`. Slot kind controls binding and validation but cannot alone distinguish overloads: `f<T>(value: T)` and `f<s/U>(value: s/U)` conflict. `ref/T` and `uniq/T`, including as receivers, remain distinct. Applied Semantics distinguish use-site Types, not Container identities.

For Signature comparison only, Origin names, lists and lifetime relations are excluded; complete Types and Origin contracts are kept for semantic checks. Return Types, external and internal parameter names, defaults, name-omission permissions, access, `unsafe` and Constraints cannot independently distinguish overloads.

An **API signature**, used for [accessibility checks](#932-api-signature-accessibility), includes the Types and requirements a declaration exposes, including results and Constraints. It is broader than the Signature used for overload identity; exclusion from overload identity does not exempt a component from accessibility checking.

```kimi
struct Reader
    func read(self: ref/Self) -> i32 => 0
    func read(self: uniq/Self) -> i32 => 0 // Distinct receiver Semantics.

func identity<T>(value?: T) -> T => value
func identity<U>(value?: U) -> U => value // Error: same normalized Signature.
```

Duplicate Signatures are declaration errors. Distinct Symbols imported from different Containers may have the same shape; a use is then ambiguous unless the overload rules select one. Header-name agreement between fragments is separate from parameter-name normalization between different function declarations.

## 9.2. Namespaces, roles, and visibility

Namespaces separate declaration kinds; a **Lookup Role** filters candidates by syntactic use before lookup stops.

| Namespace | Declarations |
| --- | --- |
| Type | Containers, Kotonoha reference names, named aliases, Core and Semantics parameters, associated Types, `Self`, and built-in Semantics/category requirements |
| Value | Functions, Fields, computed members, enum Cases, parameters, locals, local functions, function length parameters |
| Origin | Origin declarations |
| Label | Labels; they use the dedicated transfer-target rules |

| Lookup Role | Namespace | Eligible declarations |
| --- | --- | --- |
| Core | Type | Structures, enums, associated Types, `Self`, and generic bindings proven to meet this role |
| Object View Target | Type | Cores or runtime Contracts; instantiation and runtime usability are validated after selection |
| Type Semantics | Type | Semantics parameters and language-defined Semantics |
| Qualifier | Type | Groups, Kotonoha references, and Types that can qualify members |
| Requirement | Type | Capabilities, Types, Semantics and categories allowed by requirement syntax |
| Declaration Container | Type | Containers allowed as an alias or other Container target |
| Value | Value | All value declarations, including non-callable values |
| Enum Case | Value | Cases of the enum fixed by a Case-reference qualifier or the expected Type |
| Origin / Label | Corresponding namespace | Origin / Label declarations |

A declaration may serve several roles; for example, a group is a Qualifier but not a Core. Role filtering does not inspect generic arity, argument Types or labels, expected results, satisfied Constraints, or the existence of a later member. Object-target syntax selects the View Target role, and a subsequent ObjectViewCompatible failure does not reopen lookup. There is no callable-only role for `f()`.

Type and Value names may coexist. Within one namespace and scope, only valid Container merging, distinct Type Signatures and function overloads permit repeated names. Different roles do not permit conflicting declarations such as `group X` and `struct X` in the same scope.

**Local visibility.** A local becomes visible after its declaration, and its own initializer uses the outer environment. Inner blocks may shadow outer locals, but a block cannot redeclare a local name. Parameters and the immediate function body share one declaration space for duplicate checks. Local functions are visible from block entry and may overload by Signature; they cannot collide with a local or parameter in that space. Container members allow forward references, without permission to read uninitialized values.

```kimi
func example() -> i32
    let x = 10
    if true
        let x = x + 1       // RHS uses the outer x.
    let result = later()    // Local functions allow forward references.
    func later() -> i32 => 20
    return result
```

Type parameters belong to their declaring function or Type scope. Nested functions may use outer Type parameters, but runtime bindings cross a Function Boundary only through the anonymous-function capture rules; named nested functions do not capture runtime locals. Top-level locals and local functions stay private to their SourceDocument's execution scope.

Pattern names follow [arm-local scopes](14-control-flow.md#1482-binding-scopes), with separate candidate and body identities. A guard candidate is not a capture source, even when its read Type is Copy.

`Self` is reserved and requires a Type or Contract requirement context. `self` and `value` are contextual receiver and accessor bindings; while active they cannot be redeclared. The stored-accessor binding `storage` designates its own slot (§11.2) and is not a capturable local; elsewhere `storage` is an ordinary Name. Contextual runtime bindings are never implicitly captured. Origin and Label lookup never falls back to Type or Value names.

## 9.3. Accessibility and reachability

| Access | Scope |
| --- | --- |
| `private` (ordinary default) | Declaring Container and bodies lexically nested within it |
| `internal` | Same Kotonoha |
| `protected` | Declaring structure and bodies of its directly or indirectly derived structures |
| `protected internal` | Same Kotonoha **or** the protected scope |
| `private protected` | Same Kotonoha **and** the protected scope |
| `public` | Also accessible from other Kotonoha libraries, subject to enclosing restrictions |

The protected scope includes bodies lexically nested in the declaring or qualifying derived structure. Protected forms apply only to structure members, including nested Containers declared directly in a structure (§9.6.1.2), and to their accessors; they are invalid on root and group declarations, group members and Contract requirements. A declaration has one access specification; only the two compound forms shown may combine access words, in the shown order, and other combinations or duplicates are errors. `open` is an inheritance modifier, not an access level. Non-inheritable structures may keep protected members but gain no derived access sites.

Accessibility grants permission; **Name Reachability** supplies a valid path through scopes, qualification, aliases or explicit re-exports. Both are required, together with role compatibility. Public declarations are not imported automatically. Each enclosing Container and access path must allow access, and aliases and re-exports cannot widen it. API signatures obey the domain checks below, and consumers naming their Types or requirements additionally need a reachable path under the [module rules](18-modules-and-dependencies.md#18-modules-and-dependencies).

Private access uses the merged Container Symbol, not the file: other fragments of that Container have access, while unrelated declarations in the same file do not. A parent gains no access to a child's private members merely by containing it, and a future extension must not gain its target's private access. Ordinary accessors inherit their Property's access and may only narrow it. [Enum Cases](06-declarations-and-containers.md#631-cases-and-payloads) and [Contract requirements](#934-conformance-accessibility) instead take their declaring Container's effective domain and have no access modifiers of their own. Locals, parameters and contextual names use lexical visibility instead of access modifiers.

```kimi
// A.kimi
group Vault
    private var secret: i32 = 7

// B.kimi
group Vault
    func read() -> i32 => secret       // Same merged Container: allowed.
group Other
    func read() -> i32 => Vault.secret // Error, even if moved to A.kimi.
```

An inaccessible declaration is diagnostic evidence, not a candidate that stops lookup. Once a Property or member has been selected, an inaccessible required accessor or a missing receiver is an error, and outer lookup does not resume.

### 9.3.1. Effective access domains and protected receivers

The **effective access domain** `Access(D)` is the set of source contexts allowed by `D`'s access, intersected with the domain of every enclosing named Container. A public member of a private group remains confined to that group. Private and internal root declarations are Kotonoha-local; public root declarations may be accessed from other libraries without an extra project-root restriction. Dependencies and Name Reachability still apply.

Domains are computed from Symbol identity, merged Container relationships and the validated inheritance graph, not from file paths or currently observed callers. Internal access refers to the originating Kotonoha, not to a package, workspace, source directory or all libraries in a build; there is no separate package or friend-module access. Declared public access must remain valid for future consumers, even if no other module currently references it. `internal` and `protected` are not linearly ordered: the former includes unrelated code in one Kotonoha, the latter can include derived code in another.

Protected access to a member of base `B` from derived `D` requires a receiver whose static Effective Core is `D` or derived from `D`; a generic receiver may use a proven base constraint. A static `B` or a sibling of `D` is insufficient, regardless of the runtime Type. Accessors are checked independently. This additional receiver restriction does not apply inside `B`'s lexical body, to same-Kotonoha access via `protected internal`, or to static members. `private protected` requires both the module and the protected condition. Access permission supplies neither a receiver nor an undefined conversion.

For generic declarations, the protected lexical scope includes structures derived from any construction of the declaration, while the instance-receiver check still uses the actual derived receiver Type. Inheritance alone never grants private access. A future extension must receive no special private or protected privilege merely by targeting a Type; its design must use its own lexical access context.

For example, if `Left` and `Right` derive from `Base`, code in `Left` may use a protected `Base` Property through a `ref/Left` receiver, but not through a `ref/Base` or `ref/Right` receiver. Ownership, borrow permissions and accessor availability must also hold.

### 9.3.2. API signature accessibility

Every declaration `D` must satisfy, for each concrete Type or requirement `T` exposed in its API signature:

```text
Access(D) is a subset of Access(T)
```

The effective domains are checked, not merely the written modifiers. The rule applies to private, internal and protected declarations as well as public ones. A direct base Type must satisfy the same condition, with the derived Type as `D`. The rule is applied after declaration merging and Type normalization; an invalid exposed signature is a declaration error even if never used. An inferred Type is checked once established and cannot evade the rule.

The check covers function and constructor parameters and results (including receivers and constructed Types), enum payloads, Field, Property and accessor Types, and generic and associated-Type requirements. Payloads use the enum's domain, a Property's header and storage Type use the Property's domain, and each accessor's additional signature components use that accessor's domain; a restricted setter does not narrow the Property. Constraints are checked even when written inside a body, and exposed Origins are validated under their scope and lifetime rules.

API Types are checked recursively. A constructed generic Type's access domain is the intersection of the declaration's domain and the domains of all concrete arguments; other compound Types require every constituent to be accessible. Semantics cannot hide an inaccessible Core or View Target. Aliases are expanded. For associated projections, the qualifier, defining requirement and any exposed concrete binding are checked, and unresolved projections keep obligations. A Type's appearance in an API exposes neither its private fields nor its implementation bodies.

Primitive Types and language-defined public requirements impose no additional restriction. Generic parameters are symbolic API parameters, not private concrete Types: their lexical declaration scope does not restrict the generic API to its body, and their declared constraints are checked instead. In particular, a public generic declaration need not restrict all future Type arguments to public Types.

```kimi
public group Api
    private struct Hidden
    public struct Box<T>

    public func identity<T>(value?: T) -> T => value
    public func expose(value?: Box<Hidden>) -> () => () // Error.
    internal func leak(value?: Hidden) -> () => ()      // Error: wider than Hidden.
    private func keep(value?: Hidden) -> Hidden
        return identity<Hidden>(value) // Allowed: Hidden is accessible here.

    private group Implementation
        public func keep(value?: Hidden) -> Hidden => value
        // Allowed: both keep and Hidden are accessible only within Api's body.
```

Similarly, a generic function exposed beyond a private Contract's domain cannot name that Contract in its constraints. A public `Box<T>` may be instantiated as `Box<Hidden>` wherever `Hidden` is accessible, but that constructed Type cannot be exposed beyond `Hidden`'s domain. An internal API may use an internal Type when that Type's domain covers the API, including a public member inside an internal Container. Accidental equality of domains in one build must not erase the protected or module conditions required for future consumers.

### 9.3.3. Generic bodies and separate compilation

A generic body's access to nondependent Types and helpers is checked in the declaration's definition-site context. Deferred Binding and instantiation keep that context and the originally resolved Symbols. A public generic body may use private implementation Types or helper functions without exposing them in its API signature. Instantiation in another Kotonoha neither rechecks those accesses as if the caller had written the body, nor grants the caller private access, nor requires the helpers to become public.

At a generic use, the supplied Types and constraints are checked under the normal use-site rules. A caller may supply an accessible private Type to a public generic function; instantiation does not make that concrete instance a new externally public declaration. A wrapper or other declaration that exposes the resulting constructed Type must independently satisfy API signature accessibility. Enough private implementation metadata is kept for specialization, without making its Symbols source-addressable to consumers.

### 9.3.4. Conformance accessibility

Contract requirements, associated-Type requirements and required accessors cannot declare access modifiers. Their effective access is that of the declaring Contract; the ordinary `private` default does not apply.

Conformance has no access modifier of its own. For Type `T`, Contract `C` and each required implementation `M`:

```text
Access(Conformance(T, C)) = Access(T) intersect Access(C)
Access(Conformance(T, C)) is a subset of Access(M)
```

Effective domains, including enclosing Containers, are used rather than modifier spellings. Every ancestor conformance is checked independently; a narrower child Contract cannot narrow an ancestor conformance's implementation obligations. Conformance is never shrunk to fit a private member, and access is never bypassed with a generated witness thunk. The implementation is selected first and its access validated afterward, without retrying selection. API signature accessibility still applies to exposed Types, Constraints and associated-Type bindings.

```kimi
public contract Readable
    property value: i32 has get

public struct Example
    Self is Readable
    private var storedValue: i32 = 0
    public computed value: i32
        get(self: ref/Self) -> i32 => self.storedValue
        private set(self: uniq/Self, value: i32) -> () => self.storedValue = value
```

The required getter is public, and the extra setter may be private; a private getter would fail this conformance. Required accessors are checked separately, and additional accessors keep the ordinary access rules.

## 9.4. Unqualified lookup

For the required namespace and role, lookup searches these stages in order:

1. The current local scope, then each enclosing lexical and parameter scope separately.
2. The current Container.
3. Each parent Container separately, up to the project root.
4. The reserved `Kimi` and direct-dependency Kotonoha reference names in the Compilation root, for Type qualifiers only.
5. The explicit aliases of the use's SourceDocument, together.
6. The defining module's effective default aliases, together (§18.1.3).

Type parameters and contextual names occur at their declaring lexical positions. Stage 4 finds library reference names, not arbitrary library members. A bare Adaptation Target Name in `E@X` uses the two roles specified by [explicit operations](13-operators-and-assignment.md#135-explicit-operations); conversion success never selects the role.

At each stage, same-name declarations in the namespace are collected, paths to the same Symbol are deduplicated, and candidates are filtered by role and accessibility. Lookup stops at the first stage with eligible declarations. Same-name functions form one candidate set, while a function and a Field or computed member, or other distinct value kinds, conflict at that stage. Type candidates proceed to Type Name Selection.

After lookup stops, wrong Type arguments, labels, constraints or argument Types, or a non-callable value, are errors at that stage. Lookup never searches farther, even when an outer declaration would work, and arity-based indexing must preserve this boundary.

```kimi
group Outer
    func f(value?: i32) -> i32 => value
    group Inner
        func f(value?: string) -> string => value
        func test() -> ()
            f(1)           // Error: Inner.f requires string.
            ::Outer.f(1)   // Explicitly selects Outer.f.
```

A nearer group `X` does not stop Core lookup for an annotation `X`, but it does stop Qualifier lookup for `X.member`; if that group lacks `member`, lookup does not switch to an outer `X`. Similarly, an integer local `f` stops Value lookup and makes `f()` a non-callable-value error.

There is no implicit `self`: instance members require `self.member` or another explicit receiver. An unqualified reference that finds only accessible instance members reports a missing receiver instead of searching for an outer static member.

If all stages fail, the diagnostic prefers an inaccessible matching-role declaration, then an accessible wrong-role declaration, then an undefined Name. Diagnostic exploration of outer declarations never makes them valid fallback targets.

### 9.4.1. Named aliases, collisions, and warnings

Named aliases participate in the explicit source-alias stage in the Type namespace and keep their target's Qualifier role; they are not Cores. The Type/Value path rules of §9.5 apply without a preference for aliases. Inner eligible declarations shadow normally, and a selected qualifier lacking a later member never falls back to another alias or an outer declaration.

Explicit name mappings are checked at declaration time, once reference identity is fixed. In one selected SourceDocument, equal names with equal declaration references are duplicates of one mapping, while equal names with distinct references are errors even when unused. Distinct normalized argument bindings mean distinct references. If identity is unresolved, the comparison is deferred, keeping every path's validation obligations (§18.1.2). A prior mapping is never overwritten.

Conflicts with members introduced by opening aliases are checked at use, under the ordinary namespace, role, access and selection rules, deduplicating equal references within the stage. Opened members are not expanded eagerly merely to detect collisions.

**Hidden-alias warning.** After selection and generation, a warning is issued at a successfully resolved and validated named alias if an earlier root qualifier hides it. Candidates are first the same-name project-root Type-namespace Qualifiers accessible throughout the document; if there are none, the reserved or direct-dependency reference of that name. The warning is issued if candidates exist and none is the alias's own declaration reference. It ignores arity applicability, later members and call arguments, and does not enumerate potential uses. There is no warning for equal references, inner-only shadowing, or unresolved, invalid or conflicting aliases. The warning changes no lookup or acceptance rules.

```kimi
// If A is a direct dependency reference:
alias A => ::Kimi.Console // Warning: choose another name.
A.writeLine("Hello")     // Search dependency A, without fallback to Console.
```

## 9.5. Qualified and inherited lookup

The first component of `A.B.C` is resolved by ordinary lookup with its syntactic role; then only the selected target's members are searched, never its parents. Intermediate Type-side components are Qualifiers, and the final role follows the syntax, such as Core in a Type annotation or Declaration Container in an alias. [Associated-Type projections](08-generics-constraints-and-contracts.md#843-associated-types) additionally interpret `T.C.Element` through `T`'s conformance and resolve `C` as a Contract Name in the source environment, not as a member of `T`. Binding distinguishes projection paths from ordinary qualified paths, and distinct successful interpretations remain ambiguous.

Where both Type and Value qualification are syntactically possible, both are explored without preferring values:

1. Commit the first eligible lookup stage independently in the Type/Qualifier namespace and in the Value namespace.
2. Follow each complete path, checking member roles, accessibility and static/instance use; keep both paths at any genuine Type/Value branch.
3. Select the sole successful path, report ambiguity for distinct successful paths, or report lookup failure if none succeeds.

Identical references to the same Symbol are deduplicated, but value accesses with different receivers are not. Once a first component is committed within a namespace, a later failure cannot substitute an outer declaration. Call arguments and expected return Types cannot resolve a Type/Value path ambiguity; the function-group path is chosen before overload resolution. A call needed to determine an intermediate receiver Type is resolved independently.

```kimi
group Config
    public var count: i32 = 3

// Settings has a public instance Property count.
func read(Config?: ref/Settings) -> i32
    return Config.count     // Error: both Type and Value paths succeed.
// ::Config.count selects the group; renaming the parameter selects the value.
```

`::A.B` starts at the Compilation root, which contains the project-root declarations and the direct-dependency reference names. It ignores local scopes and all aliases but still checks roles and access. A project-root declaration cannot share a dependency reference name, even across namespaces; the ordinary namespace and Signature rules govern root declarations among themselves.

Member lookup searches ordinary members. An accessible, role-compatible ordinary member commits lookup even if no overload applies. There is no extension stage in this revision. **Future extension constraint:** ordinary members must precede extensions, and inaccessible or wrong-role members alone must not block them. A future design must specify lexical, source-alias and default-alias enablement stages and must not automatically add argument-associated Containers.

**Inherited ordinary lookup.** After base arguments are substituted, lookup searches the statically selected struct and then its direct bases, one layer at a time, and commits to the first layer with accessible, role-compatible declarations. That layer's same-name functions form the entire overload set; farther base overloads are not merged. Inaccessible or wrong-role declarations alone do not commit. Receiver compatibility, generic and argument applicability, accessors and Loans are later checks and cannot reopen lookup. Instance and Type functions share the Value role, so invalid receiver use cannot skip a nearer layer. A new explicit Contract implementation is selected this way, followed by the conformance checks; already inherited conformances keep their [verified mapping](08-generics-constraints-and-contracts.md#844-conformance).

A derived `f(string)` with an accessible base `f(i32)` is a declaration error under the [inherited-Name rule](06-declarations-and-containers.md#622-inheritance-and-open-structures), regardless of call sites. Without an eligible derived `f`, lookup finds the base group. Reusing a Name that is inaccessible to the derived author remains possible, and the layer-commit rule still governs that case. Receiver projection (§9.5.1) adds no ordinary derived/base conversion.

Generic bodies use their [definition-site source environment](18-modules-and-dependencies.md#18-modules-and-dependencies), including during deferred instantiation; caller aliases and extensions never enlarge their candidate sets.

### 9.5.1. Base subobject receiver projection

For `receiver.member`, when ordinary lookup selects an instance declaration in a base `B` of the receiver's static Effective Core `D`, **Base Subobject Receiver Projection** locates that declaration's inline base subobject along the unique inheritance path, substituting base Type and Origin arguments at each layer. Accessibility, including the protected-receiver restrictions, is checked against the original receiver before projection. Static members need no projection, and Type-qualified unbound calls and function values keep the ordinary argument rules.

For a declaration receiver `ref/B` or `uniq/B`, the corresponding shared Borrow or exclusive Borrow/Reborrow of that subobject is formed with the original receiver's permissions; a shared receiver cannot supply exclusive access. The source is evaluated once, before explicit call arguments, preserving its storage anchor, nested dependencies and parent Loan restrictions. A borrowed custom or computed accessor, or a method, borrows the whole base subobject. Standard Property access instead projects to its permitted storage Place under §11.1.2 without forming a whole-base borrow, preserving the original owned, borrowed or object receiver classification.

For ordinary value receivers, `ref/D -> ref/B` and `uniq/D -> uniq/B` rank as same-Semantics Reborrow, and `owner/D -> ref/B`, `owner/D -> uniq/B` and `uniq/D -> ref/B` as cross-Semantics Borrow/Reborrow under [argument adaptation](10-overload-resolution-and-inference.md#102-argument-adaptation-and-literals). These are member-receiver operations only, never Exact conversions. Projection adds no preference based on inheritance depth and cannot reopen lookup. Candidate analysis records the operations without committing them before selection.

An ordinary borrowed-receiver implementation used through projection requires published [ObjectCallCompatible Proven](12-expressions.md#1244-object-receiver-compatibility); a use of a NotProven implementation is rejected without inspecting the private body or retrying overload selection. A method that replaces all of `self` may work on a complete `B` but cannot replace the base inside `D`. Neither projection nor an escaping unrestricted exclusive borrow may permit whole-base Move, Replacement, reconstruction or acquisition of a sliced owner. The body keeps its declaration's Self and result contract. Receiver-derived results keep the projected Loan and cannot outlive the original storage; construction, destruction and ancestor-completeness restrictions still apply.

For Object Semantics, the receiver of the statically selected ObjectCallCompatible implementation is adjusted, preserving the complete object's identity, Dynamic Type, metadata and cleanup. Projection creates no public object view or owning or counting handle and changes no reference count. An owning receiver requirement (`owner/B`, `obj/B`, `rc/B` or `arc/B`) cannot be satisfied by projection; it needs an independently permitted acquisition or an explicit object upcast.

Projection is confined to this member operation. It adds no standalone base-view expression, argument or result conversion, subtype relation, bound-method value, or change to the explicit adaptation tables. The complete Sealed payload receiver path (§12.4.4) is separate: inherited Self remains the defining base, so a non-open derived Type cannot use that path to replace its base subobject.

## 9.6. Type name selection

After lookup commits, Type candidates are filtered by the number and kinds of explicit Type arguments; the arguments themselves resolve in the use-site context. Exactly one candidate must remain: zero means a Type-argument mismatch and several mean ambiguity. The selected Type's Constraints are checked afterward, without trying another Type if they fail.

For example, `Box<i32>` selects `Box<T>` from a stage containing `Box<T>` and `Box<T, U>`, while a nearer stage containing only `Box<T, U>` blocks an outer `Box<T>`. Different same-arity Types imported at one stage remain ambiguous. Legitimately unresolved argument kinds defer selection with its stage fixed; malformed arguments and unknown Names are errors. Omitted Type arguments use only the inference permitted by their construct.

### 9.6.1. Bound container paths

Types, Contracts, refinement parents, Constraints, associated-Type selectors and aliases use the same Container-reference rules, in three steps:

1. Resolve the declaration path, namespace, role, access and each segment's own arity.
2. Bind its Type and Origin arguments and compose lexical or base substitutions.
3. Validate access, formation, input conditions and Origins for every qualifier and the final target.

A committed lookup layer is not reopened after a failed argument or Constraint check.

Outer Type arguments may be omitted only when the lexical or already-resolved environment supplies them. Inside `Outer<T>`, `Inner<i32>` means `Outer<T>.Inner<i32>`. Outside it, `Outer<i32>.Inner<string>` or `Factory<i32>.make(...)` is required; missing outer arguments are never inferred from expected Types or call inputs. Function-local generic inference is unchanged, so a group function `make<T>(...)` can expose an API that infers `T`.

Inherited lookup keeps the defining declaration and the substituted base environment. With `open struct Base<T>` containing `public struct Node`, and `Derived : Base<i32>`, `Derived.Node` is `Base<i32>.Node`; `Node` is not reparented and its Self is unchanged. A directly declared Container conflicts with an accessible ancestor Container of the same Type-namespace name, regardless of arity. Inaccessible ancestor names and same spellings in different namespaces follow the ordinary rules; inherited declarations are never merged.

#### 9.6.1.1. Origins on paths

A nested declaration's effective Origin schema contains its own and inherited slots; internal dependencies of Type arguments, Fields and bases are not flattened into it. A suffix names the complete binding set of that occurrence. Slots are completed from inherited bindings, relations and the position rules of §15.3–4.

```kimi
struct View<T> {source}
    let value: ref{source}/T
    public struct Tag
    public contract Source
        func read(self: ref/Self) -> ref{source}/T

func inspect<T>(value?: View<T>.Tag{tag}, item?: ref/T)
    origin tag.source == item
    ()
```

Independent Contract references, alias targets and Container qualifiers must have a complete Origin contract. Their enclosing declaration supplies attached relations where inference or inherited bindings are insufficient; no arbitrary expression-level constraint block or implicit `static` is added.

`(Outer<T>{outer}).Inner<U>{inner}` names an intermediate qualifier and a final occurrence without creating runtime values. The parenthesized qualifier must be followed by a member, including `.init`; standalone `(View<T>{v})` is ordinary Type grouping. Preserve and validate intermediate dependencies even when they are absent from the final schema. An associated selector may similarly use `X.(ContractPath{c}).Element`.

Normalize by declaration and binding identity while retaining every intermediate obligation. Binding-set labels have lexical scope and are not Type arguments; a formatter must not relocate them or their relations. Lexical `Self` bindings remain fixed. Case and runtime View restrictions retain their own rules.

#### 9.6.1.2. Access and ambiguity

Inner declarations may access outer private declarations, although instance operations still need an explicit receiver. Parents have no privilege to access a child's private members, and merged fragments share their declaration's access rights. Effective reference and API access include the parent Containers and concrete outer arguments; conformance access is the intersection of the conforming Type and the bound Contract reference.

Protected Containers may be declared directly in structs; protected access is forbidden on declarations directly in groups. Non-instance declarations have no protected receiver restriction. Type parameters conflict with nested declarations in the same declaration namespace, and ordinary ancestor shadowing does not remove inherited slots. Origin-context lookup combines scalar names, binding-set names and eligible value carriers by the nearest lexical scope, with the role and collision rules of §15.3.4. It never falls back past a wrong-role candidate.

A nested Type is not an associated Type and adds no conformance or specification. If `T.Element` resolves both ways, the ambiguity is diagnosed. `T.(C).Element` explicitly selects a Contract; the unparenthesized `T.C.Element` remains valid, but ambiguous successful interpretations are errors. Associated candidates are deduplicated only by defining declaration plus bound defining Contract reference; distinct bindings remain separate even when their resulting Types coincide, and explicit selection must still leave one candidate.
