# Virtual dispatch

This chapter detail belongs to [§6.2.4](06-declarations-and-containers.md#624-virtual-members-and-overrides). It defines virtual slots, their implementations and direct base calls. Ordinary functions, accessors, acquisition and lifetime rules apply unless changed here. Runtime Contract Views remain outside this feature.

## 1. Declarations and contracts

`virtual func` declares a slot and its original implementation. `override func` supplies an implementation of an inherited slot. Both are contextual declaration modifiers; neither reserves an ordinary Name. They cannot be combined. `abstract`, `final`, `sealed override` and `newslot` are not introduced.

| Property | Requirement |
| --- | --- |
| Container | A direct struct member, including merged and generated declarations |
| Original | A safe instance function with a body in an `open struct`; access must not be private (the default remains private) |
| Override | A safe instance function with a body in an open or sealed derived struct |
| Receiver | Normalized `objref/Self` or `objuniq/Self`, with ordinary Origin annotations; bare `self` still means `ref/Self` and is ineligible |
| Parameters | No function-own Type, Semantics or length parameters; enclosing-Type parameters and ordinary Origins are allowed |
| Results | Ordinary value, borrow and Place results; `task;` follows Chapter 24 |
| Excluded declarations | Type/group functions, Contract requirements, Properties/accessors, `init`, `drop`, bodyless declarations and `unsafe func` |

The original declaration alone owns the public contract. An override follows the [implementation contract inheritance of full specialization](08-generics-constraints-and-contracts.md#882-inherited-contract), with receiver-Self correspondence:

| Header element | Override rule |
| --- | --- |
| Inputs | Restate count, order, receiver position and normalized Types; only the receiver maps original Self to the implementing derived Self |
| Receiver mode | Preserve `objref` or `objuniq` |
| Task slot | Explicitly preserve its presence or absence |
| Result | Match value/Place kind, Place mode and normalized Type; omission means Unit; no covariance |
| Parameter names | Preserve external names; internal names may change with `external => internal: Type` |
| Origins | Inherit the original binder and every admitted binding. Omitted annotations inherit; written annotations and relations must express that same contract, not fresh quantification |
| Access, `!`, defaults, constraints, effects | Inherit; do not redeclare, add or remove them |
| Attributes and other modifiers | Do not add them. An explicit Unsafe Block in the body retains ordinary checks |

Inherited access keeps the original declaration's semantic domain: `protected internal` is not reinterpreted relative to the derived Kotonoha. Body name lookup and private access use the derived declaration's lexical context. Body Self never replaces the original public receiver Type. Nonreceiver `Self` keeps the Type resolved where written: if the original `other: objref/Self` means `objref/Base`, the override must use `objref/Base`, not `objref/Derived`.

```kimi
open struct Base
    public virtual func score(self: objref/Self, bonus: i32 = 1) -> i32
        effect confined
        return bonus

struct Derived : Base
    override func score(self: objref/Self, bonus: i32) -> i32
        return base.score(bonus) + 10

let d = Derived.init()@obj
let a = d.score()                    // 11; Base supplies the default.
let b = Base.score(d@objref/Base)     // 11; Type qualification still dispatches.
let operation = Derived.score
let c = operation(d@objref/Base, 1)  // Same original Function Item.
```

An open derived override can be overridden again. An ordinary function cannot be made virtual by declaring a same-named derived function.

## 2. Slot identity and selection

A **SlotId** consists of the original declaration identity and the normalized binding of its declaring Type and base path, retaining the complete Type/Origin contract. It differs from implementation identity, the Origin-erased generation key and the physical table index. None of these other equalities substitutes for Type or Loan proofs.

Resolve an override as follows:

1. Starting at the direct base, commit to the first accessible same-named function group under ordinary inherited lookup; do not gather all ancestors.
2. Bind the override input Types in its lexical environment, augmented by the declared premises common to every accessible virtual in that group after base substitution. Compare task presence, receiver correspondence, input count/order and normalized input Types in that environment.
3. Require exactly one target, then check result, labels, Origins and public guarantees.

The common header premises are the intersection of the substituted declared facts (including conjuncts); ordinary Contract expansion then applies. A refuted substituted premise supplies no evidence. This bounded step neither tests applicability nor assumes a candidate-specific condition. Qualify an associated name when the header environment does not identify it uniquely, for example `U.Catalog.Element` instead of `U.Element`. Once a target is selected, inherit its full premises before completing the result and Origin contract. An unresolved input Type prevents target judgment; it does not establish that no target exists.

For example, a sole `Base<T>` slot constrained by `T is Catalog` permits `U.Element` in an override in `Derived<U> : Base<U>`. If two slots obtain an `Element` from different Contracts, the override names the intended Contract explicitly. Closed associated specifications and derived-Type identities are normalized for input/result comparison; spelling the resulting concrete Type does not create a different slot.

No target, multiple targets and contract mismatch are distinct declaration errors. Results, labels, defaults, conditions, effects, declaration order and overload ranking never disambiguate a target. Failure does not retry a farther ancestor or another body. Distinct `Base<T>.f(ref/T)` and `Base<T>.f(ref/i32)` slots remain distinct after binding `T` to `i32`; an override matching both is ambiguous.

After environment selection, Mod expansion and fragment merging, each derived Type supplies at most one override per SlotId. Different files, identical bodies and disjoint conditions do not exempt duplicates. Unused declarations are checked too.

An override adds no lookup layer, overload candidate or public function: it only changes a slot's implementation. Other overloads in the inherited group remain visible. The inherited-Name prohibition applies to every other declaration, including a new overload or new virtual of an accessible ancestor Name. Legal reuse of an inaccessible Name creates a distinct slot and cannot override the inaccessible member.

For the same bound slot, `Derived.f` and `Base.f` identify the same original declaration and Function Item, with the original public receiver (for example `objref/Base`). Thus `Derived.f(baseObject)` is legal when that public contract fits.

## 3. Calls and conditional members

Lookup, overload selection, Effective Type, access, argument acquisition, defaults and Origin/Loan checks determine the callable slot statically. The object's Dynamic Type then selects its most-derived implementation on that inheritance path, or the original implementation if none overrides it. Runtime dispatch performs no name, overload, constraint or access search.

Member calls, Type-qualified calls, Function Items, Callable calls and erased common Function values dispatch through the same selected slot. Taking a Function Item never fixes a particular body. Unbound calls use ordinary argument rules and add no implicit upcast or receiver projection. Bound method values (`value.f`) are not introduced.

An original slot may have an ordinary conditional-member premise `P` on enclosing parameters. The slot exists as a declaration; calls and Function Item acquisition require `P`. Check an override under the derived Type's constraints and `P` substituted through the base path. A containing conditional-conformance block must follow from those premises; it cannot add an implementation-selection condition. Inapplicable `P` means inapplicability, not fallback or a runtime test. Slot existence alone does not require generation of an inapplicable body.

## 4. Direct base calls

`base.f(arguments)` uses lexical `self` and ordinary lookup starting at the lexical declaring struct's direct base, then directly calls that base's selected implementation. It introduces no value, Type or View. It is available in override and ordinary derived instance bodies for accessible virtual or nonvirtual base instance functions.

In `A → B → C`, a call written in C selects B's implementation, including the implementation B inherits from A when B has no override. Only that call is direct: virtual `self.g()` inside its body still dispatches using the original object's metadata. The public contract and defaults are statically selected; the selected direct body's effect summary may be used. The usual projection, OCC, Loan and construction/destruction checks remain.

An anonymous function needs an explicit `self` capture, with ordinary capture mode and lifetime checks; the base lookup origin stays lexical. A named local function gets no implicit receiver. Bare `base`, Function Item `base.f`, `base.base` and new base-field access are invalid. Receiverless functions use ordinary Type qualification. Constructor `: base(...)` remains separate syntax.

## 5. Receiver safety and completeness

Virtual receiver borrows protect the **whole original complete payload**, including derived fields; they never narrow a Loan or input effect to the inline base portion. Existing shared/exclusive rights, reservation/activation, evaluation order and call/result Loan lifetimes apply. `rc` and `arc` grant shared access only. Ordinary values and value borrows are not implicitly boxed.

All object base-view operations share the [Owned erasure proof](13-operators-and-assignment.md#1357-object-upcasts). This includes receiver projections exposing `objref/Base` or `objuniq/Base`, for virtual, nonvirtual and base calls. Hiding the complete derived payload requires its Owned proof; an already-erased View inherits that evidence. Check after selection without fallback. Same-View-Target borrows require no erasure and no static outer Origin. Ordinary inline `ref/Base` and `uniq/Base` projections are unchanged.

Receiver correspondence retains the checked base path, Type/Origin substitution and erasure evidence, and must satisfy the implementation contract for every admitted binding. An override entry inherits the original slot base-view erasure evidence, so a base call alone requires no additional constraint. This does not assume `Self is Owned` for an original slot called at its exact View Target.

Runtime tests, refinement and casts obey the same erasure proof. Hidden fixed Origins become static only within that proof's scope, never from a Type ID alone. Outer and per-call Origins and Loans remain; result contracts gain no hidden derived Origins. Passing the receiver through helpers preserves these restrictions.

Runtime dispatch on the object being constructed or destroyed is forbidden, including through helpers and after devirtualization. Dedicated constructor/destructor receivers and dynamic destruction keep their existing rules.

Every original and override independently requires **ObjectCallCompatible Proven** at declaration. The slot publicly guarantees that receiver operations preserve completeness, Dynamic Type and Origin bindings. Compose that guarantee onto the receiver's call summary; conservative exclusive-input effects do not themselves imply forbidden whole-payload replacement. Normal reads, writes, Loans and other input effects still apply.

Open base-view and whole-base-subobject replacement remain forbidden. Independently proven complete-payload updates are allowed, including a sealed implementation's `self@follow` replacement, with normal Origin, Loan and cleanup checks. Optimization adds no authority or proof.

Bodies assume the published guarantees of called slots; existing fixed-point and obligation verification proves each implementation's obligations. A base proof does not certify an override. Pending evidence is not Proven, and unused violations are declaration errors. Separate artifacts supply verified public guarantees; checking must not enumerate descendants or private implementations.

## 6. Effects, results and conformance

An original virtual's Constraint region may declare `effect confined` and `effect preserves results`. Clause validation is shared with Contract/Callable bounds (§8.4.10); overrides inherit the bounds. No new Contract Type is created.

Dynamic-call legality uses only the slot's public contract and guarantees. Input reads/writes and Loans use requirement-call rules; environment effects without `confined` are unknown. Result dependencies use the public Origin/Loan contract and conservative indirect-call rules, never a particular body's returned field or static anchor. This remains true for sealed targets. Receiver, argument, default and preparation/cleanup effects are checked and composed separately.

### 6.1. Earlier results

`preserves results` admits `ref`, `uniq`, `objref` and `objuniq` receivers. A result still must not depend on that call's receiver Loan or the receiver's own storage.

Track earlier results for the same actual object and, for dynamic calls, the same original slot; for direct base calls, the same implementation. Compare normalized declaring bindings, distinguishing Type, Semantics and length arguments. Lawful View changes or Origin shortening alone do not break the relation, but the complete Type/Origin/Loan contracts stay checked. Prove object identity through dependency tracking, not Origin equality.

Do not automatically apply an earlier-result Loan exclusion across dynamic and direct-base calls. Delegation retains result provenance and uses the existing delegation check. Devirtualization retains dynamic-call identity for this analysis.

Handle Moves, Reborrows and lawful View changes preserve identity. Replacing, exchanging or Moving payload contents or contents on a delegation path invalidates relations to the old contents and their Loan exclusions; object identity alone cannot restore them. The earlier results' Loans, Origins and anchors remain checked. Guaranteed calls verify their own mutations: conservative input effects alone do not invalidate every call, and invalidation during verification cannot discard the earlier results being protected. This tracking adds no runtime IDs, lifetime tags or generation counters.

### 6.2. Function values and tasks

A virtual Function Item obtains `confined` only from its slot's public bound. It does not automatically establish Callable `preserves results`, since successive unbound calls can pass different receivers. Erasure to a common Function Type loses effect guarantees as usual. Task calls keep Chapter 24's rules, including static Loan checks for other tasks despite `confined`.

### 6.3. Contract witnesses

A virtual implementation of a Contract requirement maps to the slot, not a particular body. First prove that the public slot contract and bounds satisfy the requirement after ordinary Type, Self, Origin and premise correspondence; separately prove every implementation against the slot. Conformance adds no retroactive slot obligation and cannot use stronger accidental body behavior, even for sealed Types. Publish the required bound on the original slot or expose a separate operation/wrapper.

Associated-Type equality, conditional conformance and inherited-Self checks remain. Conformance is neither silently added nor removed, and this feature adds no runtime Contract View.

## 7. Semantic records and diagnostics

Retain distinct records for the public slot, implementation, direct-base selection and proof obligations. Resolve correspondence before body/effect fixed points. Pending proofs do not remove lookup candidates; invalid overrides never fall back to a base implementation.

Distinguish absent/ambiguous targets, signature mismatch, duplicate overrides and OCC/effect violations. Point to the offending modifier or signature element, relate the original slot, conflicting declaration or failed evidence, and state the distinct facts in Reason. Unsupported paths are not language-invalid forms. Offer an `override` repair only with verified target-uniqueness and contract-compatibility preconditions.

Diagnostics, Hover and CSP semantic inspection expose the original slot/public contract, dynamic versus direct-base call, bound implementation mappings, receiver/erasure evidence, completeness/effect guarantees and verification state, lexical base lookup origin, and source snapshot/configuration/dependencies. Physical indices and display strings are not semantic identities.

Contract, base, access, condition, binding, implementation or effect changes invalidate the affected lookup, conformance, proof, generation and display results. Artifacts retain the semantic mappings and verified guarantees needed for checking; missing information never means “no override.” Physical tables and references are rebuilt consistently under the [generation rules](../impl/virtual-dispatch.md).
