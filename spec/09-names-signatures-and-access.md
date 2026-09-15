# 9. Names, signatures, and access

[Specification index](../SPEC.md)

Name resolution identifies declarations; overload resolution selects an applicable operation before use-site legality is checked.

During [Mod generation](20-compilation-configuration.md#2072-compilation-and-binding), these rules may produce provisional results from the declarations currently available. Such results do not commit lookup or overload selection for the final program; final Binding runs after generation is complete.

| Term | Meaning |
| --- | --- |
| Binding / Name resolution | Associating source names and operations with declarations and meanings. |
| Lookup environment | The declarations and aliases available for a lookup in one scope; extensions are a future design. |

Module terms, including Compilation root, project root, and source environment, follow [§18](18-modules-and-dependencies.md#18-modules-and-dependencies).

Resolve Names before selecting overloads or checking whether an operation can execute:

```text
Name Resolution
├─ Type name -> Type Name Selection -> Type Legality
└─ Call      -> Candidate Applicability -> Best Candidate -> Usage Legality
```

**No Backtracking:** once a stage commits its result, a later failure must not select another lookup stage, declaration, or overload. Inaccessible or wrong-role declarations do not commit lookup. Legitimate dependencies may defer resolution; unknown Names and unresolvable dependency cycles are errors.

Apply language-defined lexical visibility and compile-time selection order first. File loading, alias order, candidate enumeration, caching, parallelism, and optimization must not change resolution.

## 9.1. Signatures

A Signature determines whether declarations may coexist in one scope:

| Declaration | Signature |
| --- | --- |
| Type declaration | Name and generic parameter count |
| Function | Name, generic parameter count, ordered normalized parameter Types including the receiver |
| Constructor | Declaring structure and ordered normalized parameter Types; no ordinary Name or receiver parameter |
| Field / computed / Property Requirement | Name |
| Enum Case | Name within its enum; no payload overloads |

**GenericArity** counts generic argument slots, including function length slots; a pair counts as one. **OriginArity** counts explicitly declared Origin parameters, excluding Origins projected from a Type slot and inference variables. Origin schemas govern binding and fragment compatibility, not additional overloads. Thus `View<T> origin source` and `View<T> origin left, right` cannot coexist as same-name, same-arity Type overloads.

Normalize by resolved Symbol and Kotonoha/version, expanding transparent aliases and resolved associated-Type projections and removing grouping and redundant owner prefixes. Preserve every Semantics layer. Represent generic expressions structurally using the declared binder and slot position:

| Source expression | Normalized expression |
| --- | --- |
| Ordinary T | `Slot(i)` |
| Pair s / pair T | `OuterSemantics(Slot(i))` / `DirectTarget(Slot(i))` |
| Original pair `s/T` | `Slot(i)` |
| s applied to another slot U | `Apply(OuterSemantics(Slot(i)), Slot(j))` |
| Length N / fixed array | `LengthSlot(i)` / `FixedArray(LengthExpression, ElementType)`; compare lengths under [normalization](04-arrays-indexing-and-slices.md#44-function-length-parameters). |

Apply these rewrites recursively and compare by structural alpha-equivalence. Do not simplify using accidental equality after instantiation or arbitrary Constraint proofs. Pair target T alone is not Slot(i). Slot kind controls binding and validation but cannot alone distinguish overloads: `f<T>(value: T)` and `f<s/U>(value: s/U)` conflict. `ref/T` and `uniq/T`, including receivers, remain distinct. Applied Semantics distinguish use-site Types, not Container identities.

For Signature comparison only, exclude Origin names, lists, and lifetime relations. Retain complete Types and Origin contracts for semantic checks. Return Types, external/internal parameter names, defaults, optionality, access, unsafe modifiers, and Constraints cannot independently distinguish overloads.

An **API signature**, for [accessibility checks](#932-api-signature-accessibility), includes the Types and requirements exposed by a declaration, including results and Constraints. This is broader than the Signature used above for overload identity; exclusion from overload identity does not exempt a component from accessibility checking.

```kimi
struct Reader
    func read(self: ref/Self) -> i32 => 0
    func read(self: uniq/Self) -> i32 => 0 // Distinct receiver Semantics.

func identity<T>(value: T) -> T => value
func identity<U>(value: U) -> U => value // Error: same normalized Signature.
```

Duplicate Signatures are declaration errors. Distinct Symbols imported from different Containers may have the same shape; a use is ambiguous unless overload rules select one. Fragment header-name agreement is separate from parameter-name normalization between different function declarations.

## 9.2. Namespaces, roles, and visibility

Namespaces separate declaration kinds; a **Lookup Role** filters candidates by syntactic use before lookup stops.

| Namespace | Declarations |
| --- | --- |
| Type | Containers, Kotonoha reference names, Core and Semantics parameters, associated Types, `Self`, and built-in Semantics/category requirements |
| Value | Functions, Fields, computed members, enum Cases, parameters, locals, local functions, function length parameters |
| Origin | Origin declarations |
| Label | Labels; use the dedicated transfer-target rules |

| Lookup Role | Namespace | Eligible declarations |
| --- | --- | --- |
| Core | Type | Structures, enums, associated Types, `Self`, and generic bindings proven to meet this role |
| Object View Target | Type | Cores or runtime contracts; validate instantiation and runtime usability after selection |
| Type Semantics | Type | Semantics parameters and language-defined Semantics |
| Qualifier | Type | Groups, Kotonoha references, and Types that can qualify members |
| Requirement | Type | Capabilities, Types, Semantics, and categories allowed by requirement syntax |
| Declaration Container | Type | Containers allowed as an alias or other Container target |
| Value | Value | All value declarations, including non-callable values |
| Enum Case | Value | Cases of the enum fixed by a CaseReference qualifier or the expected Type |
| Origin / Label | Corresponding namespace | Origin / Label declarations |

A declaration may serve several roles. A group is a Qualifier, not a Core. Role filtering does not inspect generic arity, argument Types or labels, expected results, satisfied Constraints, or the existence of a later member. Object-target syntax selects the View Target role; subsequent RuntimeUsable failure does not reopen lookup. There is no callable-only role for `f()`.

Type and Value names may coexist. Within one namespace and scope, only valid Container merging, distinct Type Signatures, and function overloads permit repeated names. Different roles do not permit conflicting declarations such as `group X` and `struct X` in the same scope.

Locals become visible after their declaration; their own initializer uses the outer environment. Inner blocks may shadow outer locals, but a block cannot redeclare a local name. Parameters and the immediate function body share one declaration space for duplicate checks. Local functions are visible from block entry and may overload by Signature; they cannot collide with a local or parameter in that space. Container members allow forward references, without granting permission to read uninitialized values.

```kimi
func example() -> i32
    let x = 10
    if true
        let x = x + 1       // RHS uses the outer x.
    let result = later()    // Local functions allow forward references.
    func later() -> i32 => 20
    return result
```

Type parameters belong to their declaring function or Type scope. Nested functions may use outer Type parameters, but runtime bindings cross a Function Boundary only through the anonymous-function capture rules; named nested functions do not capture runtime locals. Top-level locals and local functions remain private to their SourceDocument's execution scope.

Pattern names follow [arm-local scopes](14-control-flow.md#1482-binding-scopes), with separate candidate and body Identities. A guard candidate is not a capture source, even when its read Type is Copy.

`Self` is reserved and requires a Type or Contract requirement context. `self` and `value` are contextual receiver/accessor bindings; while active they cannot be redeclared. Stored accessor `storage` designates its own slot (§11.2), not a capturable local; elsewhere storage is an ordinary Name. Contextual runtime bindings are not implicitly captured. Origin and Label lookup never falls back to Type or Value names.

## 9.3. Accessibility and reachability

| Access | Scope |
| --- | --- |
| `private` (ordinary default) | Declaring Container and bodies lexically nested within it |
| `internal` | Same Kotonoha |
| `protected` | Declaring structure and bodies of its directly or indirectly derived structures |
| `protected internal` | Same Kotonoha **or** the protected scope |
| `private protected` | Same Kotonoha **and** the protected scope |
| `public` | Also accessible from other Kotonoha libraries, subject to enclosing restrictions |

The protected scope includes bodies lexically nested in the declaring or qualifying derived structure. Protected forms apply only to structure members and their accessors; they are invalid on root/group declarations, group members, and contract requirements. They do not enable nested Container declarations inside structures. A declaration has one access specification; only the two compound forms shown above may combine access words, in the shown order. Duplicate or other combinations are errors. `open` is an inheritance modifier, not an access level. Non-inheritable structures may retain protected members, but gain no derived access sites.

Accessibility grants permission; **Name Reachability** supplies a valid path through scopes, qualification, aliases, or explicit re-exports. Both, plus role compatibility, are required. Public declarations are not automatically imported. Each enclosing Container and access path must allow access; aliases and re-exports cannot widen it. API signatures obey the domain checks below, and consumers naming their Types or requirements must additionally have a reachable path under the [module rules](18-modules-and-dependencies.md#18-modules-and-dependencies).

Private access uses the merged Container Symbol, not the file. Other fragments of that Container have access; unrelated declarations in the same file do not. A parent does not gain access to a child's private members merely by containing it. A future extension must not gain the target's private access. Ordinary accessors inherit their Property's access and may only narrow it. [Enum Cases](06-declarations-and-containers.md#631-cases-and-payloads) and [Contract requirements](#934-conformance-accessibility) instead inherit their declaring Container's effective domain without independent access modifiers. Locals, parameters, and contextual names use lexical visibility instead of access modifiers.

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

An inaccessible declaration is diagnostic evidence, not a candidate that stops lookup. Once a Property or member has been selected, an inaccessible required accessor or missing receiver is an error; do not resume outer lookup.

### 9.3.1. Effective access domains and protected receivers

**Effective access domain** Access(D) is the source contexts allowed by D’s access intersected with every enclosing named Container’s domain. A public member of a private group remains confined to that group. Root private/internal declarations are Kotonoha-local; public roots may be accessed from other libraries without an extra project-root cap. Dependencies and Name Reachability still apply.

Compute domains from Symbol identity, merged Container relationships, and the validated inheritance graph, not file paths or currently observed callers. Internal access refers to the originating Kotonoha, not a package, workspace, source directory, or all libraries in a build. No separate package or friend-module access is defined. Declared public access must remain valid for future consumers, even if no other module currently references it. `internal` and `protected` are not linearly ordered: the former includes unrelated code in one Kotonoha, and the latter can include derived code in another.

Protected access to a member of base B from derived D requires a receiver whose static Effective Core is D or derived from D; a generic receiver may use a proven base constraint. Static B or a sibling of D is insufficient regardless of runtime Type. Check accessors independently. This extra receiver restriction does not apply inside B’s lexical body, to same-Kotonoha access via `protected internal`, or to static members. `private protected` requires both module and protected conditions. Access permission supplies neither a receiver nor an undefined conversion.

For generic declarations, the protected lexical scope includes structures derived from any construction of that declaration; the instance-receiver check still uses the actual derived receiver Type. Inheritance alone never grants private access. A future extension must receive no special private or protected privilege merely by targeting a Type; its design must use its own lexical access context.

For example, if `Left` and `Right` derive from `Base`, code in `Left` may use a protected `Base` Property through a `ref/Left` receiver, but not through a `ref/Base` or `ref/Right` receiver. Ownership, borrow permissions, and accessor availability must also hold.

### 9.3.2. API signature accessibility

Every declaration `D` must satisfy the following for each concrete Type or requirement `T` exposed in its API signature:

```text
Access(D) is a subset of Access(T)
```

Check the effective domains, not merely the written access modifiers. This rule applies to private, internal, and protected declarations as well as public declarations. A direct base Type must satisfy the same condition with the derived Type as `D`. Apply the rule after declaration merging and Type normalization; an invalid exposed signature is a declaration error even if never used. An inferred Type is checked once established and cannot evade this rule.

Check function/constructor parameters and results (including receivers/constructed Types), enum payloads, Field and Property/accessor Types, and generic/associated-Type requirements. Use the enum’s domain for payloads, the Property domain for its header/storage Type, and each accessor’s for additional signature components. A restricted setter does not narrow the Property. Check Constraints even when written inside a body, and validate exposed Origins under their scope/lifetime rules.

Check API Types recursively. A constructed generic Type’s access domain intersects the declaration’s and all concrete arguments’ domains; other compound Types require every constituent to be accessible. Semantics cannot hide an inaccessible Core or View Target. Expand aliases. For associated projections, check the qualifier, defining requirement, and any exposed concrete binding; unresolved projections retain obligations. A Type’s appearance in an API does not expose its private fields or implementation bodies.

Primitive Types and language-defined public requirements impose no additional access restriction. Generic parameters are symbolic API parameters, not private concrete Types: their lexical declaration scope does not restrict the whole generic API to its body. Check their declared constraints instead. In particular, a public generic declaration need not restrict all future type arguments to public Types.

```kimi
public group Api
    private struct Hidden
    public struct Box<T>

    public func identity<T>(value: T) -> T => value
    public func expose(value: Box<Hidden>) -> () => () // Error.
    internal func leak(value: Hidden) -> () => ()      // Error: wider than Hidden.
    private func keep(value: Hidden) -> Hidden
        return identity<Hidden>(value) // Allowed: Hidden is accessible here.

    private group Implementation
        public func keep(value: Hidden) -> Hidden => value
        // Allowed: both keep and Hidden are accessible only within Api's body.
```

Similarly, a generic function exposed beyond a private Contract's domain cannot name that Contract in its constraints. A public `Box<T>` may be instantiated as `Box<Hidden>` wherever `Hidden` is accessible, but that constructed Type cannot be exposed beyond `Hidden`'s domain. An internal API may use an internal Type when its domain covers that API, including a public member inside an internal Container. Accidental equality of domains in one build must not erase the protected or module conditions required for future consumers.

### 9.3.3. Generic bodies and separate compilation

Check generic body access to nondependent Types and helpers in the declaration's definition-site context. Deferred Binding and instantiation retain that context and the original resolved Symbols. A public generic body may use private implementation Types or helper functions without exposing them in its API signature. Instantiation in another Kotonoha does not recheck those implementation accesses as if the body were written by the caller, grant the caller private access, or require the helpers to become public.

At a generic use, check supplied Types and constraints using the normal use-site rules. A caller may supply an accessible private Type to a public generic function; instantiation does not turn that concrete instance into a new externally public declaration. A wrapper or other declaration that exposes the resulting constructed Type must independently satisfy API signature accessibility. Preserve sufficient private implementation metadata for specialization without making its Symbols source-addressable to consumers.

### 9.3.4. Conformance accessibility

Contract requirements, associated-Type requirements, and required accessors cannot declare independent access modifiers. Their effective access is that of the declaring Contract; the ordinary `private` default does not apply.

Conformance has no independent access modifier. For Type `T`, Contract `C`, and each required implementation `M`, require:

```text
Access(Conformance(T, C)) = Access(T) intersect Access(C)
Access(Conformance(T, C)) is a subset of Access(M)
```

Use effective domains, including enclosing Containers, not modifier spellings. Check every ancestor conformance independently; a narrower child Contract cannot narrow an ancestor conformance's implementation obligations. Do not shrink conformance to fit a private member or bypass access with a generated witness thunk. Select the implementation first, then validate access without retrying selection. API signature accessibility still applies to exposed Types, Constraints, and associated-Type bindings.

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

The required getter is public; the extra setter may be private. A private getter would fail this conformance. Required accessors are checked separately; additional accessors retain ordinary access rules.

## 9.4. Unqualified lookup

For the required namespace and role, search these stages in order. A bare Adaptation Target Name in `E@X` uses the two roles specified by [explicit operations](13-operators-and-assignment.md#135-explicit-operations); it does not select a role using conversion success.

1. Current local scope, then enclosing lexical and parameter scopes, each separately.
2. Current Container.
3. Parent Containers, separately, through the project root.
4. Direct-dependency Kotonoha reference names in the Compilation root, for Type qualifiers only.
5. Explicit aliases of the use's SourceDocument, together.
6. Compilation default aliases, together.

Type parameters and contextual names occur at their declaring lexical positions. Stage 4 finds library reference names, not arbitrary library members.

At each stage, collect same-name declarations in the namespace, deduplicate paths to the same Symbol, and filter by role and accessibility. Stop at the first stage with eligible declarations. Same-name functions form one candidate set; a function and a Field/computed member, or other distinct value kinds, conflict at that stage. Type candidates proceed to Type Name Selection.

After stopping, wrong type arguments, labels, constraints, argument Types, or a non-callable value are errors at that stage. Do not search farther, even when an outer declaration would work. Arity-based indexing must preserve this boundary.

```kimi
group Outer
    func f(value: i32) -> i32 => value
    group Inner
        func f(value: string) -> string => value
        func test() -> ()
            f(1)           // Error: Inner.f requires string.
            ::Outer.f(1)   // Explicitly selects Outer.f.
```

A nearer group `X` does not stop Core lookup for an annotation `X`, but does stop Qualifier lookup for `X.member`. If that group lacks `member`, do not switch to an outer `X`. Similarly, an integer local `f` stops Value lookup and makes `f()` a non-callable-value error.

There is no implicit `self`: instance members require `self.member` or another explicit receiver. An unqualified reference that finds only accessible instance members reports a missing receiver instead of searching for an outer static member.

If all stages fail, prefer an inaccessible matching-role declaration diagnostic, then an accessible wrong-role diagnostic, then undefined Name. Diagnostic exploration of outer declarations never makes them valid fallback targets.

## 9.5. Qualified and inherited lookup

Resolve the first component of `A.B.C` by ordinary lookup with its syntactic role, then search only the selected target's members. Do not return to its parents. Intermediate Type-side components are Qualifiers; the final role follows the syntax, such as Core in a Type annotation or Declaration Container in an alias. [Associated-Type projections](08-generics-constraints-and-contracts.md#843-associated-types) additionally interpret `T.C.Element` through `T`'s conformance and resolve `C` as a Contract Name in the source environment, not as a member of `T`. Binding distinguishes projection paths from ordinary qualified paths; distinct successful interpretations remain ambiguous.

Where both Type and Value qualification are syntactically possible, explore both without preferring values:

1. Commit the first eligible lookup stage independently in the Type/Qualifier and Value namespaces.
2. Follow each complete path, checking member roles, accessibility, and static/instance use; retain both paths at any genuine Type/Value branch.
3. Select the sole successful path, report ambiguity for distinct successful paths, or report lookup failure if none succeeds.

Deduplicate identical references to the same Symbol, but not value accesses with different receivers. Once a first component is committed within a namespace, later failure cannot substitute an outer declaration. Call arguments and expected return Types cannot resolve a Type/Value path ambiguity: choose the function-group path before overload resolution. A call needed to determine an intermediate receiver Type must be resolved independently.

```kimi
group Config
    public var count: i32 = 3

// Settings has a public instance Property count.
func read(Config: ref/Settings) -> i32
    return Config.count     // Error: both Type and Value paths succeed.
// ::Config.count selects the group; renaming the parameter selects the value.
```

`::A.B` starts at the Compilation root: project-root declarations and direct-dependency reference names. It ignores local scopes and all aliases but still checks roles and access. A project-root declaration cannot share a dependency reference name, even across namespaces; ordinary namespace and Signature rules still govern root declarations among themselves.

Member lookup searches ordinary members. An accessible, role-compatible ordinary member commits lookup even if no overload applies. There is no active extension stage in this revision. **Future extension constraint:** ordinary members must precede extensions; inaccessible or wrong-role members alone must not block them. A future design must specify lexical, source-alias, and default-alias enablement stages and must not automatically add argument-associated Containers.

**Inherited ordinary lookup.** After substituting base arguments, search the statically selected struct, then its direct bases, one layer at a time. Commit to the first layer with accessible, role-compatible declarations. Its same-name functions form the entire overload set; do not merge farther base overloads. Inaccessible/wrong-role declarations alone do not commit. Receiver compatibility, generic/argument applicability, accessors, and Loans are later checks and cannot reopen lookup. Instance/type functions share the Value role, so invalid receiver use cannot skip a nearer layer. Use this selection for a new explicit Contract implementation, followed by conformance checks. Already inherited conformances retain their [verified mapping](08-generics-constraints-and-contracts.md#844-conformance).

A derived `f(string)` with an accessible base `f(i32)` is a declaration error under the [inherited-Name rule](06-declarations-and-containers.md#622-inheritance-and-open-structures), regardless of call sites. Without an eligible derived f, lookup finds the base group. Reuse of a Name inaccessible to the derived author remains possible; the layer-commit rule still governs that case. Receiver projection below adds no ordinary derived/base conversion.

Generic bodies use their [definition-site source environment](18-modules-and-dependencies.md#18-modules-and-dependencies), including during deferred instantiation; caller aliases and extensions never enlarge their candidate sets.

### 9.5.1. Base subobject receiver projection

For `receiver.member`, when ordinary lookup selects an instance declaration in a base `B` of the receiver's static Effective Core `D`, **Base Subobject Receiver Projection** locates that declaration's inline base subobject along the unique inheritance path. Substitute base Type/Origin arguments at each layer. Check accessibility, including protected-receiver restrictions, against the original receiver before projection. Static members need no projection; Type-qualified unbound calls and function values retain ordinary argument rules.

For a declaration receiver `ref/B` or `uniq/B`, form the corresponding shared Borrow or exclusive Borrow/Reborrow of that subobject using the original receiver's permissions. A shared receiver cannot supply exclusive access. Evaluate the source once, before explicit call arguments; preserve its storage anchor, nested dependencies, and parent Loan restrictions. A borrowed custom/computed accessor or method borrows the base subobject as a whole. Standard Property access instead projects to its permitted storage Place under §11.1.2 without forming a whole-base borrow; preserve the original owned/borrowed/object receiver classification.

For ordinary value receivers, rank `ref/D -> ref/B` and `uniq/D -> uniq/B` as same-semantics reborrow, and `owner/D -> ref/B`, `owner/D -> uniq/B`, and `uniq/D -> ref/B` as cross-semantics Borrow/Reborrow under [argument adaptation](10-overload-resolution-and-inference.md#102-argument-adaptation-and-literals). These are member-receiver operations only, never Exact conversions. Projection adds no preference based on inheritance depth and cannot reopen lookup. Candidate analysis records operations without committing them before selection.

An ordinary borrowed-receiver implementation used through projection requires published [ObjectCompatible Proven](12-expressions.md#1244-object-receiver-compatibility). Reject a use of NotProven without inspecting the private body or retrying overload selection. A method that replaces all of self may work on a complete B, but cannot replace the base inside D. Neither projection nor an escaping unrestricted exclusive borrow may permit whole-base Move, Replacement, reconstruction, or acquisition of a sliced owner. The body retains its declaration's Self and result contract. Receiver-derived results keep the projected Loan and cannot outlive the original storage; construction, destruction, and ancestor-completeness restrictions still apply.

For Object Semantics, adjust the receiver of the statically selected, ObjectCompatible implementation. Preserve the complete object's identity, Dynamic Type, metadata, and cleanup. Projection creates no public object view or owning/counting handle and changes no reference count. An owning receiver requirement (`owner/B`, `obj/B`, `rc/B`, or `arc/B`) cannot be satisfied by projection; it needs an independently permitted acquisition or explicit object upcast.

Projection is confined to this member operation. It adds no standalone base-view expression, argument/result conversion, subtype relation, bound-method value, or change to the explicit adaptation tables.

## 9.6. Type name selection

After committing lookup, filter Type candidates by the number and kinds of explicit type arguments. Resolve the arguments themselves in the use-site context. Select exactly one candidate; zero means type-argument mismatch and several mean ambiguity. Check the selected Type's Constraints afterward, without trying another Type if they fail.

For example, `Box<i32>` selects `Box<T>` from a stage containing `Box<T>` and `Box<T,U>`. A nearer stage containing only `Box<T,U>` blocks an outer `Box<T>`. Different same-arity Types imported at one stage remain ambiguous. Legitimate unresolved argument kinds defer selection with its stage fixed; malformed arguments or unknown Names are errors. Omitted type arguments use only the inference permitted by their construct.
