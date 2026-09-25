# Storage Provision, Projection, and Acquisition

Date: 2026-09-25

Status: independent specification draft for discussion. Not integrated into SPEC.md, not implemented, and not evidence of compiler support.

This document proposes a new design, rather than a patch constrained by the current language. Its rules are self-contained within the storage/access domain. Existing language rules and other proposals do not silently fill contradictions in this design. Module syntax, general overload ranking, raw memory, concurrency, and the full object ABI are outside its scope. The syntax shown here is proposed syntax; examples are design examples, not executable compiler tests.

The baseline below uses the recommended choices in section 15. Those choices are provisional, not decisions already approved by the user. A change to a choice must update its dependent examples and rules together. Section 18 records how the earlier Place Access Model revision is incorporated and distinguishes additional design choices from previously proposed principles.

## 1. Objective and division of responsibility

> Storage providers expose places, permitted operations, and dependencies. Consumers select an operation within those bounds.

The model has four stages:

```text
Storage
  -> Projection: identify a Place and preserve its Provision
  -> Access planning and validation: acquire a value or update storage
  -> Binding / Placement: initialize or replace destination storage
```

Traversal selects successive sources. Pattern matching selects structural sources. Neither defines another ownership system.

The required separations are:

- Stored Type versus access-path authority.
- Potential capability versus legality at a particular program point.
- Reference/handle storage versus the storage it targets.
- Borrow authority versus ownership extraction.
- Source acquisition versus destination mutability.
- Selecting an element versus acquiring that element.

There is no universal `Storage.read(mode)` function whose result Type changes according to Copy capability. There is no `for`-specific assignment or write-through rule.

## 2. Terms and semantic objects

### 2.1. Storage and complete Types

**Storage** is a location with a logical identity, extent, initialization state, and destruction responsibility. It may be local, static, inline within an aggregate, separately allocated, or materialized for a temporary. Zero-sized storage has logical identity without requiring a unique address.

A **complete Type** includes ownership/reference semantics, generic arguments, and all Origin dependencies. `ref/T`, `uniq/T`, and an owning object handle are complete Types distinct from `T`.

`ref/T` grants shared access to a referent. `uniq/T` grants exclusive access, subject to reborrowing and current Loans. Neither owns the referent. Moving a reference transfers its capability; it does not move its referent.

Copy is a property of the complete Type. Copy duplicates a value without executing user duplication code, acquiring resources, or incrementing ownership counts. A shared reference is Copy; an exclusive reference and an owning resource handle are not. A Copy aggregate may not contain a Non-Copy component or require a user destructor whose responsibility would be duplicated.

### 2.2. Place

A **Place** is the result of identifying storage. Conceptually it carries:

| Component | Meaning |
| --- | --- |
| StoredType | The complete Type held by the storage. |
| Path | The root and projections identifying the storage, including overlap information. |
| Provision | The operations potentially exposed at this use site. |
| Dependencies | Storage anchors, Origins, parent/child Loans, and ownership dependencies. |

A Place is a compiler-tracked result category, not a value Type. It cannot be an ordinary local's Type, an aggregate element Type, a generic Type argument, or an Option payload. A binding such as `let x = expression` acquires a value from a Place result; it does not save the Place itself.

The compiler may represent a Place by an address, indices, or other location information. Such representation must not alter its abstract identity, capabilities, or dependencies. No runtime descriptor allocation is required by this model.

### 2.3. Provision and current state

**Storage Provision** describes potential operations under declaration, Type, accessibility, and path conditions. It is not an unconditional permission token.

**Access validation** additionally checks the actual flow state: initialization, completeness, active Loans, valid Origins, construction state, and cleanup dependencies.

Contract conformance must not appear or disappear because a local happens to be borrowed or moved. Accessibility is checked at the use or exposure boundary; an inaccessible private field cannot be accessed directly merely because its Type supports a capability.

**Aggregate semantics** means the rules relating a whole storage object to its inline parts, separate referents, initialization state, and destruction obligations. It does not replace the complete Types of its children.

## 3. Provision and access operations

### 3.1. Independent capabilities

Provision distinguishes these capabilities:

| Capability | Potential operation |
| --- | --- |
| Read | Observe the initialized stored value. |
| Write | Initialize or replace the stored value, subject to state and destruction conditions. |
| Take | Transfer the stored value and its destruction responsibility, leaving that storage uninitialized. |

These are not an ordered permission scale. An immutable owned local may provide Read and Take without subsequent Write. A borrowed mutable element may provide Read and Write without Take.

Stable storage and authority must additionally be retainable for a Borrow. An ordinary computed result or setter is not a Place merely because it supports a read-like or write-like operation.

### 3.2. Common operation checks

| Operation | Required conditions |
| --- | --- |
| Copy | Read; initialized complete target value; complete Type is Copy; no conflicting Loan. |
| Shared Borrow | Read; initialized complete target value; stable storage and dependencies for the Loan. |
| Exclusive Borrow | Read and Write; initialized complete target value; exclusive authority; no conflicting Loan. |
| Initialize | Write or the one initial construction permission; target is uninitialized; incoming value can be stored safely. |
| Replace | Write; target is initialized; old value can be destroyed without invalidating the secured new value or other live dependencies. |
| Move | Take; initialized complete target value; trackable Move Path; valid ancestor/destructor conditions; no conflicting Loan. |

Whole-value operations require a complete target. An independently identifiable initialized child can be used while a sibling is uninitialized, provided locating it does not require whole-value access and no enclosing destructor invariant is violated.

A Place with uninitialized storage can be an initialization destination. It cannot first be turned into `uniq/T`: a safe reference requires an initialized referent. Borrow is therefore not the universal precursor to every access operation.

### 3.3. Extraction and aggregate cleanup

Take requires a direct ownership path whose post-Move initialization state and remaining destruction responsibility can be tracked. A borrowed referent never acquires Take merely by becoming exclusive.

The baseline supports owned roots, accessible inline fields and Tuple components, and fixed-array elements selected by an in-range integer literal, with parentheses transparent. Runtime indices, computed indices, dynamic collection elements, and arbitrary user projections do not provide a statically trackable Take path. Optimizer constant folding does not add one.

An enclosing user destructor that requires a complete value prohibits a partial Move that would violate that requirement. Moving the whole owning value remains a separate operation.

Removing an element from a dynamic collection is an ordinary ownership-returning operation. It must update collection state and cleanup responsibility before returning the value. It does not grant callers Take on an indexed borrowed Place. `replace` and `exchange` likewise preserve initialization on normal completion.

## 4. Projection and aggregate composition

### 4.1. Projection operations

Projection identifies storage without acquiring the stored value. Selectors include a local root, inline field, Tuple position, index key, reference dereference, Cursor current element, and matched payload position.

A Projection must preserve the selected child's complete Type. It derives path authority and dependencies from the parent and the selected declaration. It may restrict authority; it cannot create it.

The effect of an access is tracked against the path and all affected ancestors. Provision is therefore not only a bit mask: a Move, replacement, or Loan also has a path-specific effect on initialization, overlap, and cleanup.

### 4.2. Inline projection

An inline child inherits the parent's applicable access ceiling and adds its own accessibility and mutability restrictions. An immutable inline field cannot be replaced through a mutable parent. A shared path to an aggregate does not permit exclusive borrowing of its inline children.

Borrowing an inline child protects the anchors necessary to keep it valid. Moving or destroying the parent is rejected while that would invalidate the child Loan. Parent-wide and overlapping child operations conflict. Distinct declared fields and distinct in-range literal indices of the same fixed array may supply a disjointness proof; arbitrary user projections and dynamic indices do not do so automatically.

### 4.3. Reference and handle projection

The baseline introduces a safe, explicit dereference Place expression:

```text
*r
```

For a safe reference, this denotes its immediate referent. It neither copies nor moves the reference or referent. Dereferencing an exclusive reference establishes access through that capability without first copying its Non-Copy value.

The reference slot and the referent are separate storage objects:

```kimi
var number: i32 = 1
let r = number@uniq
*r = 2                  // Updates number; r's slot is immutable.
// r = anotherReference // Error: r is an immutable binding.
```

A direct immutable slot containing `uniq/T` does not make its referent immutable. However, reaching that capability through an intervening shared borrow prevents exclusive use of it. In particular, dereferencing a `ref/uniq/T` does not recover unrestricted exclusive authority from the inner Type.

`uniq/ref/T` permits updating the outer reference slot, but its inner referent remains shared. No automatic flattening of nested semantics takes place.

The same distinction applies to object handles: replacing a writable `rc/T` handle does not grant write access to its payload. In this baseline, a directly held exclusive `obj/T` handle can lend its payload exclusively even when the handle binding is `let`; shared paths restrict that authority. Object views and incomplete base subobjects cannot be whole-value replaced merely because a pointer exists; the target must be a known complete storage value with compatible destruction obligations.

There is no implicit repeated dereference. Examples use `(*r).field` explicitly. Automatic member-receiver dereference would be optional syntax sugar requiring a separate decision; it is not assumed here. Raw pointer dereference belongs to a separate unsafe specification and cannot manufacture safe Provision without proof.

### 4.4. Demand propagation and boundaries

A consumer's demand follows the storage projection chain it actually uses. It does not propagate into arbitrary selector computations, unrelated arguments, or through ordinary value-returning calls.

For `matrix[i][j] = value`, the write requirement follows the relevant projections to the target. For `matrix[computeIndex()][j]`, it does not change the acquisition rules inside `computeIndex()`.

An ordinary call uses its declared receiver and parameter requirements. A returned reference starts a path with that reference's actual authority. A getter returning `ref/Row` cannot be retried as an exclusive access to hidden storage because an outer operation wants to write.

## 5. Acquisition and explicit notation

### 5.1. Baseline acquisition table

| Source / notation | Meaning |
| --- | --- |
| Bare Place in a value position | Copy its complete stored Type; reject if that Type is not Copy. |
| Ordinary temporary value in a value position | Transfer the existing value into its destination without another Copy. |
| `P@ref` | Shared-borrow the exact Place P, yielding `ref/T` when P stores T. |
| `P@uniq` | Exclusively borrow the exact Place P, yielding `uniq/T`. |
| `P@owner` | Move P's complete stored value; require Take and mark P uninitialized. |

`@owner` is a transfer operation in this draft, not a conversion to an owning version of the referent Type. On an ordinary temporary it passes that value onward. On a stored reference it moves the reference. On a Copy Place it still performs a Move rather than Copy. The alternative spelling `@move` is a naming decision, not an additional operation in this baseline.

Borrowing an ordinary temporary materializes it into temporary storage, preserving its existing responsibility. A mutable temporary can be exclusively borrowed. Materialization never turns a getter result into its source's hidden Place.

### 5.2. Slot borrowing and reborrowing

Borrow notation always targets its immediate Place. This removes a source-Type-dependent choice between slot borrowing and referent reborrowing:

```kimi
let r = number@uniq
let child = (*r)@uniq    // Reborrow number through r.
let shared = (*child)@ref
let slot = r@ref        // ref/(uniq/i32), borrowing r's slot.
```

These statements are subject to normal Loan liveness; a conflicting later use must wait until the relevant child Loan ends. Forming or copying an outer shared reference never duplicates inner exclusive authority.

An unannotated `let child = r` is rejected when r's stored Type is `uniq/T`. The language does not silently choose between transferring that capability, borrowing its slot, and reborrowing its referent.

### 5.3. Expected Types and adaptation

An expected Type constrains the acquired result; it does not invent a Borrow or Move in an ordinary initializer, argument, assignment source, or return. Borrow formation and reborrowing use the explicit operations above. A method receiver, a control Subject, or a protocol mapping may have a statically specified acquisition operation, which is checked by the same access engine.

There is no implicit Copy read through a safe reference in this baseline: read `*r` when the referent value is wanted. Copying r itself copies a Copy reference. Any future Copy-read shorthand must preserve one-layer boundaries and cannot change inferred unannotated binding Types.

Consequently, `let x = values[i]` is valid as a bare read only when the stored complete element Type is proven Copy. `let x = values[i]@ref` has a stable reference Type independent of that proof. No `SharedReadResult(T)` family is needed.

For generic code, unknown Copy capability is not treated as either Copy or Non-Copy. A bare value read requiring Copy needs an explicit proof in the public constraints. Borrowing the Place remains available when the path permits it.

### 5.4. Temporary lifetime

Ordinary expression temporaries survive through completion and cleanup of their containing statement, but a stored borrow does not silently extend their lifetime. To keep a borrow longer, first store the owning value in an explicit local and then borrow that local.

A `for` or `match` creates an explicit hidden owning local for a temporary Subject/source; that local has the construct's owning scope. This is specified materialization, not a general borrow-driven lifetime extension. Results escaping the construct must not depend on that hidden local.

## 6. Exposing Place results

### 6.1. Result categories

Place-returning signatures state their maximum exposed authority explicitly:

```text
place(ref, T) during origin
place(uniq, T) during origin
```

`T` is a complete stored Type. These are result categories, not value Types. `Option<place(ref, T)>`, a stored Place field, and a `place(owner, T)` result are not defined. Absence is returned using ordinary optional references or values, or represented by a Cursor state.

`place(ref, T)` exposes shared borrowing and permitted Copy reads. `place(uniq, T)` additionally exposes exclusive borrowing and replacement. Neither exposes Take or an uninitialized destination across the function boundary.

A function may expose less authority than its receiver has. A shared receiver is not automatically permission to expose an exclusive result. The body must prove that the declared capability is independently available from the permitted inputs and dependencies.

### 6.2. Return checking and Origins

A Place-returning `return` designates storage; it does not Copy or Move its value. Every normal return must designate initialized storage of exactly the declared complete Type, with the required capability and valid dependencies through callee cleanup and caller use.

For an instance function with a direct borrow receiver, an omitted outer result Origin defaults to that receiver's input Origin. The expression `during self` denotes that referent/receiver Loan Origin, not the stack lifetime of the parameter slot. Other cases require an explicit Origin unless a unique input dependence is established by a general elision rule.

`during self.source` may name a declared source Origin, but it cannot erase actual parent Loans, ownership anchors, or aliasing constraints. The signature must expose all dependency relations needed for modular checking. Where the implementation cannot prove a result independent of a receiver Loan, it must retain that dependence rather than claim a longer external lifetime.

Returning a callee local, destroyed temporary, or computed value as a Place is invalid. A computed value stored in a persistent Cursor field may be exposed as that field's Place. A temporary receiver owned by the caller may supply a Place within its actual materialized lifetime.

### 6.3. Public API invariants

An API exposing an exclusive whole-T Place authorizes ordinary valid T replacement. A provider must not expose it when such replacement would violate private representation invariants. Accessibility checks and result capabilities are not predicates restricting callers to a provider's preferred values.

For example, a Dictionary may expose a mutable value Place but not a freely replaceable key Place. Internal mutability of an entry does not imply public replaceability of the entry or key.

Computed getters return values and computed setters are calls. A getter result can be materialized and borrowed as its own temporary; neither operation grants access to nonexistent or hidden storage. There is no fallback from a rejected Place operation to a getter/setter pair.

## 7. Index projection Contracts

The baseline uses generic Contracts and complete associated Types. The following is proposed declaration syntax:

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: Key)
        -> place(ref, Element) during self

contract MutableIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: Key)
        -> place(uniq, Element) during self
```

The inherited Element identity is shared. Each conformance has one coherent mapping for a given complete receiver Type and Key Type. The requirements promise corresponding logical selections; they need not have identical implementations or perform identical side effects.

`E[key]` selects the shared projection for reading or shared borrowing, and the exclusive projection for replacement, compound update, or exclusive borrowing. Selection precedes legality checks. Missing capability or a failed check is an error, not a reason to try another operation. A direct explicit call to `indexUniq` retains its declared exclusive receiver requirement even if the eventual consumer only reads.

Receiver and key expressions are evaluated once in their defined order. Bounds or missing-key failure occurs according to the selected mapping's documented behavior. The built-in checked sequence mappings Abort on invalid bounds; Dictionary indexing refers to existing values and Aborts on a missing key. Insertion is a separate operation.

Direct structural fixed-array projection additionally retains the compiler-known Move Path described in section 3.3. Abstract Indexable evidence does not promise this privilege, even when a particular implementation uses a fixed array.

The position value Type may continue to be called `Index`; the access Contract is called `Indexable` to keep those concepts distinct.

## 8. Assignment, initialization, and replacement

### 8.1. Simple assignment

`a = b` is an Assignment operation returning Unit:

1. Evaluate b and acquire its value by section 5.
2. Secure that value and its cleanup responsibility.
3. Evaluate a as a destination and identify its Place once.
4. Validate storage permission, state, and dependencies.
5. Initialize uninitialized writable storage, or destroy the old initialized value and replace it.
6. Clean up remaining statement temporaries in reverse acquisition order.

The destination can be located after a source Move when doing so needs no read of the moved value. A writable `a = a@owner` can therefore reinitialize a moved local. An immutable binding cannot be initialized a second time after a Move.

The source must remain valid through old-value destruction. A replacement cannot destroy the owner of a reference stored in the secured result. If old-value destruction Aborts or diverges, placement does not occur. Abort performs no unwinding in this draft; an ordinary control transfer follows the normal cleanup rules. No ownership state is silently rolled back.

### 8.2. Compound assignment

The proposed baseline deliberately gives compound assignment the same **right-hand-side-first** order as simple assignment:

1. Evaluate and acquire the right-hand operand once.
2. Locate the left-hand Place once with exclusive update authority.
3. Acquire the old operand by the selected operator's ordinary Copy or explicit/protocol-specified Borrow operation; never implicitly Move it from the target.
4. Compute and secure the operator result.
5. End obsolete child borrows and perform the common replacement operation.

No location or user index operation is reevaluated. Authority protecting the target remains valid through computation and replacement. Conflicting effects of a user operator are rejected by ordinary Loan rules. The result cannot retain a borrow invalidated by replacement.

This order is a provisional design choice (D5), not an assertion about existing Kimigayo. It avoids a second default operand-order rule within assignment syntax. Prefix/postfix update forms, if provided, must lower to the same access model with their specified result value.

## 9. Binding and patterns

`let x = value` and `var x = value` create new local storage. The difference is permission for subsequent replacement of that local slot. Neither declaration makes x an alias of the source Place.

Mutability is not deep: a `let` holding `uniq/T` may access its referent exclusively; a `var` holding `ref/T` cannot. `x = value` always assigns to x's slot. `*x = value` assigns to its referent, if x is a suitable reference and the path permits it.

Iteration bindings and selected match-arm bindings obey exactly this rule. The baseline does not introduce Place aliases. Such aliases would require separate general syntax and could not be inferred from `var`, Copy capability, or an enclosing loop.

Structural patterns use the following acquisition policy, including recursively nested Tuple and enum payload patterns:

| Structural source | Binding acquisition |
| --- | --- |
| Owned temporary or owned internal Subject part | Transfer the selected complete stored value into the new local, including Copy values; preserve residual cleanup. |
| Shared borrowed Place storing T | Shared-borrow that exact Place as `ref/T`. |
| Exclusively borrowed Place storing T | Exclusively borrow that exact Place as `uniq/T`, when permitted. |
| Wildcard | No binding acquisition; responsibility remains with the current owner. |

A binding at a reference-valued slot borrows that slot under a borrowed policy; it does not flatten nested references. A structural pattern that needs to follow a stored reference uses an explicit dereference pattern `*Pattern`. This changes the path, retaining its authority ceiling, before further structural matching. It never acquires referent ownership from an owned reference value.

Exclusive decomposition is strict: a requested exclusive binding at a read-only component is an error, not a silent shared downgrade. An API needing mixed permissions supplies an ordinary view value containing those permissions, as in the Dictionary example below.

## 10. Cursor and Iterator

### 10.1. Cursor exposes Places

```kimi
contract Cursor
    associate Element
    func advance(self: uniq/Self) -> bool
    func current(self: ref/Self)
        -> place(ref, Element) during self

contract MutableCursor: Cursor
    func currentUniq(self: uniq/Self)
        -> place(uniq, Element) during self
```

A newly created Cursor has no current element. After `advance()` returns true, current denotes the selected logical element until the next advance. Before the first successful advance and after false, current operations Abort. After false, advance remains false; these Contracts provide no reset operation.

Multiple shared current results may coexist when ordinary Loans permit it. Conflicting exclusive results or advancing while a current Loan depends on the Cursor are rejected. Ending a binding's lexical scope is not necessary when its Loan has already ended, and is not sufficient when a dependent value escaped into another live binding.

Elements need not reside inside the Cursor: backing-storage dependencies are retained. A cache field containing a generated value is also real storage. A Cursor adapter may therefore lend a generated value until the next advance without claiming that it was originally stored in the source collection.

### 10.2. One value-delivery protocol for for

For lowering, the baseline chooses **one Iterator protocol**, including lending results, instead of making for search between Cursor and Iterator capabilities. Cursor adapters use this protocol.

To express it, associated Types may bind a named Origin parameter:

```kimi
contract Iterator
    associate Item {step}
    func next(self: uniq/Self during step) -> Option<Self.Item{step}>
```

`Item{step}` is a family of ordinary complete Types indexed by an Origin, not a family of Place categories or a runtime type computation. The Item binder is alpha-renamed and instantiated with next's receiver Origin for each call. Each call has its own receiver Loan. Generic checking preserves this dependence rather than erasing it.

Examples of associated specifications, using proposed syntax:

```text
associate Item{step} is E
associate Item{step} is ref/E during source
associate Item{step} is uniq/E during source
associate Item{step} is ref/E during step
associate Item{step} is uniq/E during step
associate Item{step} is (ref/K during source, uniq/V during source)
```

The family must be well-formed for every permitted call Origin under the declaration's public constraints. Fixed source dependencies remain part of the complete Type. Implementations are checked under those symbolic constraints, including all actual parent Loans. An implementation cannot claim source-only results when their authority actually depends on the live receiver Loan.

When Item depends on step, retaining it can prevent another next call, mutation, or destruction of the iterator. When the result is genuinely independent of step, next's receiver Loan may end before the body and independently valid results may be retained across calls. Generic code cannot assume that independence without a public Type/dependency guarantee.

This associated-Origin facility is a substantive general type-system addition, explicitly identified as decision D4. It is not hidden inside a special for lifetime exception.

### 10.3. Adapters and Drain

A shared Cursor adapter implements Iterator with `Item{step} = ref/E during step`. It advances once, returns None on false, and otherwise borrows current. A mutable adapter uses `uniq/E during step` and currentUniq. The returned reference is an ordinary Option payload; no `Option<place(...)>` is formed. Implementations must preserve the Cursor's dependencies through the adapter's receiver.

An ownership iterator returns `Item{step} = E`, transferring elements from state that it updates internally. A generator may use the same shape. An external shared iterator may return a source-bound reference independent of step.

After next returns None, it remains exhausted. Dropping an iterator or adapter performs its ordinary cleanup. An owning iterator destroys any elements it still owns; a borrowed iterator never destroys the borrowed collection merely because iteration ends.

Drain is not another language-recognized protocol. A drain API returns an Iterator with a documented removal/cleanup policy. It may own the collection or exclusively borrow it; early-exit policy belongs to that API, never to a heuristic inside for.

## 11. for source acquisition and lowering

### 11.1. Three explicit entry capabilities

```kimi
contract Iterable
    associate IteratorType {source} is Iterator
    func iterate(self: ref/Self during source) -> Self.IteratorType{source}

contract MutableIterable
    associate IteratorType {source} is Iterator
    func iterateUniq(self: uniq/Self during source) -> Self.IteratorType{source}

contract IntoIterable
    associate IteratorType is Iterator
    func intoIterator(self: Self) -> Self.IteratorType
```

The borrowed factories' associated iterator families are instantiated with the factory receiver Origin. This use of the same associated-Origin mechanism is necessary: a single fixed complete IteratorType cannot capture a fresh receiver Origin on every call. A returned aggregate iterator binds its appropriate source Origin to that argument, while preserving any additional fixed dependencies. The owning factory uses a complete iterator Type whose dependencies come from its consumed Self and public contract, not from a borrow of its local parameter slot. No factory result may borrow a destroyed factory local. The schema and all relations are part of the requirement mapping, not inferred from private implementation bodies.

These capabilities are independent: supporting exclusive iteration does not require an implementation to offer a shared one. Their names and members select exactly one protocol per mode; all return Iterator. There is no fallback and no direct method-name duck typing.

### 11.2. Header modes

| Header | Source acquisition and fixed requirement |
| --- | --- |
| `for pattern in E` | Shared access; Iterable.iterate. |
| `for pattern in E@ref` | Shared access; Iterable.iterate. |
| `for pattern in E@uniq` | Exclusive access; MutableIterable.iterateUniq. |
| `for pattern in E@owner` | Value transfer; IntoIterable.intoIterator. |

In a for or match header, a final outer `@ref`, `@uniq`, or `@owner`, with grouping parentheses transparent, is a Subject acquisition qualifier. It is applied exactly once to E. Inner adaptations remain ordinary operations and are not stripped or retried. There is no mode inference from the body, binding mutability, Copy capability, or available conformance. This contextual header notation is part of the proposed grammar.

For borrowed modes, a Place source is lent in the specified mode; an ordinary temporary is materialized into the construct's hidden owning source local and lent. For ByValue mode, a Place is explicitly transferred even if Copy, and a temporary is acquired as its existing value. A source holding a reference or view is still that complete stored Type: ownership of its referent is never inferred. Dereference explicitly to iterate the referent when needed, for example `for x in (*r)@uniq`.

There is no implicit Iterable conformance for every Iterator, reference, or view. A library can declare coherent forwarding implementations; an unrelated added capability never changes which entry requirement an existing header selects.

### 11.3. Loop execution

1. Evaluate E once, acquire the source as specified, and call the selected factory once.
2. Store the returned iterator in a hidden mutable local.
3. Call next with a fresh exclusive receiver Loan and bind its step Origin.
4. On None, finish. On Some, transfer its ordinary payload into that iteration's hidden owned item storage.
5. Apply the binding pattern to that item, creating ordinary let/var locals. A name without a let/var keyword defaults to let. A Tuple pattern decomposes an owned Tuple item. An explicit dereference pattern may select borrowed structural parts under section 9.
6. Execute the body. On normal body completion or continue, clean up iteration locals and residual item storage before requesting the next step.
7. On exhaustion or an ordinary outward transfer, clean up the iterator and remaining hidden source storage in reverse creation order. A transfer result must remain valid through that cleanup.

A live result depending on step can make step 3 of the next iteration illegal. The compiler checks loop backedges and continue paths; it does not force-end such a Loan. Independent source-bound results may survive when all source and exclusivity constraints remain satisfied.

When the source is acquired by value into intoIterator, the factory owns its cleanup responsibility; no second hidden owner destroys it. The returned iterator owns only the state actually transferred into it.

### 11.4. Binding examples

```kimi
for x in numbers             // Shared adapter yields ref/i32.
    print(*x)

for x in numbers@uniq        // Mutable adapter yields uniq/i32.
    *x += 1                  // Ordinary dereference Place update.

for var x in numbers@owner   // Owning iterator yields i32.
    x += 1                   // Changes only the local item.
```

`for var x in numbers` stores a shared reference in a mutable local slot. It does not permit `*x = 1`. Conversely, the immutable x in the second example permits referent updates through its exclusive capability.

For Dictionary, an exclusive iterator can yield an owned Tuple `(ref/K, uniq/V)`:

```kimi
for (key, value) in dictionary@uniq
    print(*key)
    update((*value)@uniq)
```

The key reference remains shared. Decomposing the ordinary Tuple transfers its reference values into locals; it does not claim a physical `(K, V)` Place. A minimal implementation may make these references step-dependent and prohibit retaining them across next. A source-dependent implementation requires an actual proof of capability splitting and non-overlap, not merely a longer Origin annotation.

## 12. match source acquisition and guards

match uses the same header modes: bare or @ref means Shared, @uniq means Exclusive, and @owner means ByValue. Bare temporary Subjects are materialized and borrowed, rather than silently treated as ByValue. `match makeValue()@owner` explicitly requests owned decomposition.

The Subject is evaluated once before arm testing. Structural patterns select actual component Places without acquiring body bindings. Borrowed modes apply the borrowed policies in section 9; ByValue begins with an owned Subject, but an explicit reference traversal still cannot transfer referent ownership.

Each guard exposes candidate names only for shared inspection. A candidate use designates a shared candidate Place; a bare use Copies its stored Type only if Copy, and a Non-Copy inspection requires an explicit shared borrow. Candidates cannot be assigned, transferred, borrowed exclusively, or directly captured. New candidate-dependent borrows must end before guard continuation; copying an existing reference retains and checks its original dependencies.

Pattern testing and guard cleanup protect the tested discriminants and candidate locations against invalidating changes. False destroys guard temporaries and ends guard-local Loans before trying the next arm. It performs no payload Move and creates no arm-local exclusive binding. Independent side effects are not rolled back.

After successful guard cleanup, acquire body bindings left to right from the original candidate Places according to the selected Subject policy. Wildcards retain existing ownership responsibility. Remaining owned Subject parts are cleaned up on normal or ordinary transfer exit. Borrowed Subjects never destroy their referents.

```kimi
match optionalNumber@uniq
    .Some(let number) => *number = 42
    .None => ()
```

Here number stores `uniq/i32` in an immutable local. Its referent is the payload. Replacing the whole enum while that payload Loan remains live is invalid. Declaring `var number` would only add permission to replace the reference slot.

An owned field binding that cannot legally Move because of an enclosing destructor requirement is rejected; match does not provide a hidden partial-Move exemption. A whole-value binding or borrowed match remains possible when its own requirements hold.

## 13. Contract mapping and inference

Ordinary associated Types bind complete Types. Origin-parameterized associated Types additionally declare their Origin binders explicitly. No facility is special to Iterator.Element or Iterator.Item.

An implementation may omit an associated specification only when the complete mapping is uniquely determined from explicit declarations, declared identity constraints, and the signature of an already unambiguously identified implementation member.

Mapping proceeds as follows:

1. Resolve the conformance and requirement identities; collect explicit associated specifications.
2. Identify the intended implementation declarations by explicit mapping or unambiguous names and signature structure. Do not pick an overload by first guessing the missing associated Type.
3. Unify complete signature identities, including receiver modes, result categories, generic binders, Origin schemas, and declared relations.
4. Require a unique consistent substitution. A capability constraint alone does not choose a concrete Type.
5. Validate implementation compatibility and bodies under the completed public contract.

There is no inference from executable bodies, implicit adaptations, Copy choices, subtyping search, or a successful convenient specialization. Related requirements must agree. Recursive inference with no unique grounded solution requires an explicit associated specification.

Origin-family mappings are compared structurally modulo binder renaming. Inferring an iterator's concrete Type does not invent missing source-Origin relations or prove that its result is independent of step.

## 14. Required examples and diagnostics

These are acceptance criteria for a future implementation, not a report of test execution.

| Case | Required result |
| --- | --- |
| Bare Copy local / Copy element read | Same common Copy acquisition. |
| Bare Non-Copy local / Non-Copy element read | Reject; explicit borrow or a permitted transfer is needed. |
| `let r = n@uniq; *r = 2` | Allow when n and its Loans permit it. |
| Reassign immutable r | Reject independently of referent permission. |
| Shared path containing an inner uniq | Cannot regain exclusive authority through another dereference. |
| Borrow a reference slot with `r@ref` | Add one reference layer; do not reborrow the referent. |
| `(*r)@ref` / `(*r)@uniq` | Borrow the referent under ordinary path checks. |
| Move an immutable owned root | Allow if Take and all state conditions hold. |
| Move a borrowed element even through uniq | Reject. |
| Initialize uninitialized var | Allow without forming a safe reference to uninitialized T. |
| Partial Move under a destructor requiring completeness | Reject. |
| Shared-only index used as an update target | Reject without retrying a getter or copying a temporary. |
| Nested update with a shared intermediate path | Reject at that path. |
| User index expression with side effects | Evaluate once for the selected operation. |
| Return a Place into a destroyed callee local | Reject. |
| Borrow a caller-owned temporary's Place within its lifetime | Allow. |
| Persist such a borrow beyond that lifetime | Reject. |
| `for var x` over shared borrowed elements | x's slot is mutable; elements remain shared. |
| `for let x` over exclusive borrowed elements | x's slot is immutable; authorized referent updates remain possible. |
| Cursor current before success / after exhaustion | Abort. |
| Keep a step-dependent Item across another next | Reject when its receiver dependence conflicts. |
| Keep a genuinely independent shared source Item across next | Allow if its public dependencies and actual Loans permit it. |
| Claim independent mutable items without capability-splitting proof | Reject the implementation. |
| Dictionary exclusive iteration | Shared keys and exclusive values; no fabricated Tuple Place. |
| False guard before owned decomposition | No payload transfer; later arms see initialized payloads. |
| Replace enum while a dependent payload Loan is live | Reject. |
| Return / continue / early exit | Preserve ordinary cleanup and dependency checks. |
| Add an unrelated Contract conformance | Do not change a selected access mode or iteration entry. |
| Generic unknown Copy used in bare value read | Require Copy proof; do not infer a borrow result Type. |
| Conflicting inferred associated Types or Origins | Diagnose the mapping conflict, not a body-dependent fallback. |

Diagnostics should identify the requested operation, selected storage path, and failed condition: missing exposed capability, inaccessible declaration, uninitialized/incomplete value, untrackable Take path, conflicting Loan, invalid Origin, or an invariant-breaking exposure.

## 15. Decisions requiring review

The following recommended choices define this draft's provisional baseline. None should be silently settled by current compiler limitations.

| ID | Decision | Baseline recommendation | Alternative and consequence |
| --- | --- | --- | --- |
| D1 | Does `for var x` create local storage or alias the element? | New local storage, consistently with all let/var declarations. | Place aliases need a separate general binding mechanism, lifetime rules, and syntax; do not make var context-dependent. |
| D2 | What does a bare Non-Copy Place read do? | Reject; request Borrow or Transfer explicitly. Borrowed control bindings keep stable reference Types. | Implicit shared reads require a common Type-dependent acquisition family and can change inferred Types when Copy conformance changes. |
| D3 | How are a reference slot and its referent distinguished? | Explicit safe `*r`; @ref/@uniq borrow the exact operand Place. No implicit Copy dereference in ordinary expressions. | Semantics-sensitive shorthand can reborrow automatically, but needs disambiguation and precedence rules for nested reference storage. |
| D4 | How does generic for support both lending and non-lending items? | General Origin-parameterized associated Item Types; one Iterator protocol; Cursor adapters. | Retain distinct traversal protocols with explicit public driver selection and a specified generic item/dependency model. Do not infer the driver from whichever capability happens to exist. |
| D5 | Compound-assignment order | RHS first, matching simple assignment. | Target first is possible but requires its own ordering and target-protection specification; do not expand it into reevaluated indexing. |
| D6 | Bare temporary for/match source | Shared materialized Subject; @owner explicitly selects ByValue. | Automatically acquire temporaries by value, but then source category as well as header notation determines the mode. |
| D7 | Does let on an owning exclusive handle freeze its payload? | It freezes the handle slot; separate target authority follows the handle and path. | Deep immutability needs an explicit capability restriction across indirection, consistently for handles and exclusive references. |
| D8 | Public Place syntax and transfer spelling | Explicit `place(ref, T)` / `place(uniq, T)`; @owner means transfer of the complete value. | Short forms or @move may improve readability, but must denote the same precisely selected operation and never change the complete stored Type. |

D1-D4 are the most consequential decisions. D4 adds expressiveness to the general Type/Origin system; it should receive a separate soundness and inference review before integration. D3 is a deliberate new surface design that removes several implicit adaptations; accepting Projection alone does not imply accepting this syntax policy.

## 16. Scope of further specification work

This draft specifies the storage/access architecture and the recommended semantics of its principal consumers. Before integration, the language definition must additionally finalize:

- Concrete grammar for Origin-parameterized associated Types, their projection syntax, and factory result-Origin relations.
- General variance and well-formedness rules for those families, including their use through nested generic Types and function values; no automatic covariance of mutable storage may be assumed.
- The exact mapping from object views and dynamic destruction to complete replaceable storage targets.
- Declaration/member syntax, receiver-call sugar, source diagnostics, and unsafe primitives that allow libraries to prove disjoint mutable iteration.
- Library choices for which collections offer independent source-bound items versus conservative step-bound adapters, plus each drain API's early-exit behavior.

These are explicit remaining design obligations. They are not permission to erase dependencies, fabricate places, strengthen shared access, or describe unverified implementation behavior as supported.

## 17. Implementation architecture (non-normative)

One useful compiler decomposition is:

```text
ResolveProjection
    root / field / index / dereference / current / payload
    -> typed Place path, exposure authority, and dependencies

PlanAccess
    Place or ordinary Value + selected operation
    -> Copy / Borrow / Move / Initialize / Replace plan

ValidateAccess
    declaration + flow state + Loans + Origins + cleanup
    -> legality and state transitions

BindOrPlaceValue
    acquired ordinary value + destination
    -> new local or storage update
```

Index, for, and match should supply selectors, protocol mappings, and acquisition contexts to these common operations. They should not independently recompute Copy-dependent result Types, flatten nested semantics, or implement separate ownership-transfer rules.

Plans and descriptors are compile-time structures. Lowering may eliminate temporary storage and transfers while preserving observable effects, evaluation counts/order, storage identity, Loans, initialization state, and cleanup. Runtime allocation or copying must not be introduced merely to bridge these abstractions.

## 18. Incorporation of the earlier Place Access Model revision

This section records design correspondence, not precedence over the formal language specification. The earlier revision's compatible ideas are expressed as rules in this document rather than left as unstated assumptions.

| Earlier idea | Incorporation in this draft |
| --- | --- |
| Providers expose permissions and dependencies as well as storage identity. | Sections 1-4 and 6.3: Provision, access validation, path composition, and protection of provider invariants. |
| Copy, Borrow, and Move are distinct acquisitions; Move cannot be derived from uniq. | Sections 3 and 5: independent Read/Write/Take, direct Move paths, and no Take on a returned borrowed Place. |
| Eliminate SharedReadResult only together with its Copy-dependent default result selection. | Section 5.3 and D2: ordinary bare Place reads require Copy; explicit borrows and borrowed structural bindings have stable Types. |
| Explicit shared capability plus an exclusive extension; a bare receiver must not secretly become access-polymorphic. | Sections 6, 7, and 10.1: declared Place result modes and separate shared/exclusive projection requirements. |
| Demand follows projection paths, not arbitrary nested calls, keys, or getter implementations. | Section 4.4: projection-only propagation and fixed ordinary-call boundaries, with no fallback. |
| Preserve both declared Origins and actual Loans; source lifetime is not authority to issue more exclusive references. | Sections 6.2 and 10-11: transitive dependency contracts, per-step Loans, and explicit proof obligations for independent mutable items. |
| Persistent cached results can be real Places; callee-local temporaries cannot escape. | Sections 5.4, 6.2, and 10.1: explicit materialization, caller-owned temporary bounds, and Cursor cache fields. |
| Cursor exposes Places, Iterator returns values, and Drain is an Iterator use rather than a third language protocol. | Section 10: both public concepts remain, with adapters connecting Cursor to the single for delivery protocol. |
| Specify Cursor validity and exhaustion; use ordinary Loan conflicts to restrict advancement. | Sections 10.1 and 14: explicit state behavior, Abort on invalid current, and retention checks. |
| Do not invent a contiguous Tuple Place for Dictionary keys and values or expose writable keys. | Sections 6.3 and 11.4: ordinary Tuple values containing shared key and exclusive value references. |
| for and match share explicit source acquisition, including temporary handling and transfer of reference values. | Sections 11-12 and D6: Shared, Exclusive, and ByValue; moving a view never invents ownership of its referent. |
| A for entry fixes its delivery protocol instead of searching available conformances. | Section 11: each mode selects one named entry, all returning Iterator; the explicitly tagged dual-driver option remains D4's alternative. |
| Guard inspection precedes arm acquisition and cannot consume candidates or keep new candidate Loans across selection. | Section 12: protected testing, shared candidates, cleanup, and acquisition only after selection. |
| for/match bindings are ordinary reference/value locals, not context-dependent assignment aliases. | Section 9, section 11.4, and D1: let/var affect new local storage; dereference uses the same Place rules everywhere. |
| Associated Types are complete Types; signature inference uses unique identity unification and never executable bodies. | Section 13: general associated Types, explicit dependencies, and deterministic mapping. |

The revision's distinction between storage access and ordinary value adaptation remains intact. This draft additionally makes several surface choices that are not necessary consequences of that distinction:

- D3 proposes explicit safe dereference and exact-slot borrow notation, rather than retaining implicit Copy reads and semantics-sensitive Reborrow shorthand.
- D4 recommends Origin-parameterized associated Types and Cursor adapters. The earlier `Places<C>` / `Values<I>` driver distinction remains a valid competing architecture, but its generic item and lifetime contract must be specified if selected. Both architectures preserve fixed dispatch and ordinary Loan validation; only one should be the baseline for for lowering.
- D5 proposes RHS-first compound assignment rather than preserving the earlier target-first order. It is independent of the Projection model and can be changed without rejecting that model.
- D7 defines let on a handle as slot immutability rather than deep payload immutability. This is a separate policy decision, not inferred from the earlier illustrative access-path table.

These differences remain explicit review items. Incorporating the earlier ideas does not assert that the newly proposed syntax, evaluation-order changes, or associated-Type extension has already been accepted.
