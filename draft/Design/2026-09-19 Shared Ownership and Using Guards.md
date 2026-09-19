# Shared Ownership, Guards, and using — Final Specification Change Proposal

- Date: 2026-09-19
- Status: Final design proposal pending SPEC integration; not implemented support or a verified ABI.
- Scope: local/sync, guards, using, thread capabilities, Weak, object creation and ownership transfer.

Except for the changes stated here, [SPEC.md](../../SPEC.md) and its chapters apply. “Current §” denotes that specification; other section references are local. Sections 1–6 define proposed language requirements, §7 implementation goals and profile obligations, and §9 deferred work. Examples use proposed syntax and are not compiler test results. Snippets are independent; Item and inspectName are defined in §8.1.

## 1. Types and Semantics

### 1.1. Roles and targets

Ownership, payload access, and acquisition lifetime are separate:

| Role | Type or syntax | Meaning |
| --- | --- | --- |
| Exclusive ownership | `obj/T` | One ownership responsibility |
| Shared read-only ownership | `rc/T`, `arc/T` | Non-atomic/atomic reference counting and shared payload access |
| Guarded shared ownership | `local/T` | Non-atomic reference counting and runtime borrow checking |
| Guarded shared ownership | `sync/T` | Atomic reference counting and a non-reentrant Mutex |
| Object borrow | `objref/T`, `objuniq/T` | Shared/exclusive access to a T view; no ownership |
| Acquisition lifetime | `ReadGuard<S>`, `WriteGuard<S>` | Owned values responsible for releasing a borrow or lock; S is the complete source handle Type |
| Scope management | `using name = expression Body` | Sugar for a scoped var binding and do expression |

local/sync are built-in Semantics, not library aliases. They participate in `<s/T>` decomposition and Semantics constraints. T must be a supported object View Target; sync additionally requires §5.2. This proposal does not enable runtime Contract Views or other unintroduced object targets.

`ref/local/T` borrows handle storage; `objref/T` borrows the payload view. Complete-value borrows (`ref/T`, `uniq/T`) remain distinct from object borrows that may refer to a derived object.

### 1.2. Contextual keywords and categories

local/sync/async are recognized only as Semantics prefixes, Semantics constraints, and explicit adaptation targets. `let local = 1`, `func sync() => ()`, `x.async`, and ordinary division `local / count` follow normal name rules. async is reserved only in Semantics contexts and is rejected as unsupported.

| Category | Members |
| --- | --- |
| `value` | owner |
| `valueborrow` | ref, uniq |
| `object` | obj, rc, arc |
| `guarded` | local, sync |
| `counted` | rc, arc, local, sync |
| `objectborrow` | objref, objuniq |
| `borrow` | ref, uniq, objref, objuniq |
| `owning` | owner, obj, rc, arc, local, sync |
| `reference` | ref, uniq, obj, rc, arc, local, sync, objref, objuniq, unsafe |

The new guarded/counted names are contextual only in Semantics constraints. object retains direct shared payload borrowing. guarded supplies the common `write`/`tryWrite` API (§3.2), not direct payload access. counted supplies strong clone and Weak operations, not common payload borrowing. Weak itself has owner Semantics and is not counted. async belongs to none of these sets.

This replaces current §3.3's exclusion of counted. Generic bodies constrained by owning/reference must be checked for the expanded sets; compatibility effects are listed in §10.2.

### 1.3. Acquisition and duplication

obj/rc/arc/local/sync handles and all guards are Non-Copy. Ordinary acquisition Moves without incrementing counts. `Kimi.Intrinsics.clone(handle@ref)` duplicates a counted strong responsibility. obj and guards have no clone; Weak operations follow §6.

No handle `.clone()` or `.share()` member is registered, so rc/arc payload member lookup is unchanged. Omitting `@ref` from clone calls would require a general design for implicit handle-slot argument borrowing, not a clone-specific exception (current §10.2 and §10.9).

## 2. Explicit adaptation with @

### 2.1. Operations and heap allocation

`@` selects an operation from the operand's static Type and explicit target. **Some adaptations perform heap allocation.** It remains a built-in operation with no user-defined conversions, user-code calls by the operation itself, or search for conversion chains. Failure never selects a different operation. No adaptation implicitly clones a strong owner.

M denotes obj/rc/arc/local/sync, C denotes a counted Semantics, and T/V denote object View Targets where applicable. Existing numeric, borrow, pointer, and upcast eligibility rules remain in force.

| Input and target | Operation | Heap allocation by the adaptation |
| --- | --- | --- |
| Same normalized complete Type, including `M/T @M` or `@M/T` | Ordinary Copy/Move; applicable Borrow/Reborrow takes precedence | None |
| Numeric value to an admitted numeric target | Numeric conversion or direct-literal fitting | None |
| Value/reference to an admitted `ref`/`uniq` target | Borrow/Reborrow or storage borrow | None |
| Eligible object to `objref`/`objuniq` | Object Borrow/Reborrow | None |
| Proven complete Sealed object payload to `@ref/T` or `@uniq/T` | Payload projection | None |
| Owning object handle or object borrow to an admitted different view | Same-mode Move or object Borrow/Copy/Reborrow under §2.3 | None |
| Raw pointer to an admitted pointer/integer target, or the reverse | Raw pointer conversion | None |
| `null` to a raw pointer Type | Typed null formation | None |
| Complete owner/T with a valid payload Core to `@M` or `@M/T` | Acquire once by Copy/Move and create a new object | May allocate object and management storage |
| `obj/T` to `@C` or `@C/T` | Move ownership of the same object to C | May allocate management storage; never a replacement payload |

The column excludes allocation while evaluating the operand, receivers, or getters. Materialization for borrowing adds no heap allocation (current §3.6). Creation establishes a new object even when storage allocation can be elided; §7 defines allocation goals, not universal counts. Required allocation failure Aborts before publishing a result. Non-allocating operations can still Abort, for example numeric conversion. Completed effects and Moves are not rolled back.

Different counted modes cannot convert to each other or to obj, even at strong count one. Borrowed values cannot create or transfer owning object handles. No other adaptation is added; in particular, the existing implicit common-function conversion is not made an `@` operation.

Creation and ownership transfer retain T. A single adaptation cannot combine either operation with a view change; write separate operations. `@M` infers T from the input. Access, initialization, Origins, Loans, payload-erasure Owned proofs, and sync formation remain mandatory. Generic bodies prove operation selection for every admitted input and target eligibility under §5.2.2; an unknown Type is not assumed to be an owner Core.

### 2.2. Creation and transfer

Normal creation Moves the acquired complete value into a new payload without repeating constructors, accessors, or deinit. A Copy source remains usable. `value@local` has the ownership meaning of `(value@obj)@local`, but requires neither an intermediate object nor two allocations. Normal creation adds no blanket Owned requirement; external dependencies are retained.

Transfer from obj consumes the handle and preserves Dynamic Type, object identity, payload address, and external dependencies. It neither copies nor relocates the payload. Initialize destination management state before publication; reject the Move if a conflicting Loan exists. Moving a handle is not moving its payload.

**Remove `Kimi.Intrinsics.makeObj`, `makeRc`, and `makeArc`; do not introduce `makeLocal` or `makeSync`.** Complete-value creation and ownership-mode transfer use only `@`. Cyclic builders (§6.2) are a separate callback protocol, not adaptations of complete payload values.

```kimi
var original = Item.init("initial")@obj
var first = original@local // original is Moved; identity and payload address are unchanged.
var second = Kimi.Intrinsics.clone(first@ref)
var independent = Item.init("other")@local // A new object.
```

### 2.3. Views and complete payloads

local/sync use the existing Supports, type-test, checked-cast, and payload-erasure rules within the currently supported target boundary. Same-mode owning view changes Move without changing counts; object-borrow view changes Copy/Reborrow and preserve their Loans. All views keep the same object and management state. A sync destination must also satisfy §5.2.

For the added modes, view-changing adaptations apply to `local/T` and `sync/T` owning handles and to guard-derived `objref/T`/`objuniq/T`. The latter use the existing object-borrow upcast rows. They do not authorize a direct payload borrow from local/sync. **Handle-slot borrows, including `ref/local/T`, `uniq/local/T`, `ref/sync/T`, and `uniq/sync/T`, have no payload upcast.** In particular, `uniq/local/Dog` cannot become `uniq/local/Animal`. No container covariance is introduced.

```kimi
// Dog derives from Animal; required Supports and Owned proofs hold.
var dog = Dog.init()@local
var animal = dog@local/Animal // Same object; dog is Moved.
```

Exclusive access does not prove that a viewed object is exactly T. `objuniq/Animal` does not implicitly become `uniq/Animal`; methods retain ObjectCallCompatible checking. Explicit `@ref/T`/`@uniq/T` payload projection requires the same complete, proven Sealed T under current §13.5.5.1. Its child Loan remains guard-dependent. Payload replacement gives no access to guard management fields. local/sync targets are not generally restricted to Sealed Types.

## 3. Guarded access

### 3.1. Acquisition and failure

local operates on one thread; its name implies neither stack allocation nor thread-local storage. sync uses an atomic count and a non-reentrant Mutex. Both provide exclusive `write`; sync also uses it for read-only payload work.

| Mode and state | read / tryRead | write / tryWrite |
| --- | --- | --- |
| local, unborrowed | Available | Available |
| local, shared borrow active | Another shared borrow available | Conflict |
| local, exclusive borrow active | Conflict | Conflict |
| sync, unlocked | Unsupported | Available |
| sync, locked | Unsupported | Conflict |

| Condition | Ordinary API | try API |
| --- | --- | --- |
| local conflict | Abort without waiting | None |
| sync conflict | Wait | None without waiting |
| Shared-borrow count overflow | Abort before publishing a result | Same; not None |

Only success creates a guard with one release responsibility; a conflict creates neither an acquisition nor a release responsibility. An increment attempted at or above a strong/weak/borrow count limit Aborts before publishing its result. Counts never wrap or saturate into success. Preserve existing rc/arc limits; profiles define limits for added state. Check timing follows §7.3's runtime policy. try results report acquisition conflicts, not recovery from overflow or required runtime-resource failure.

sync release has release ordering; successful write/tryWrite has acquire ordering. Failed tryWrite supplies no acquisition synchronization. Reacquisition of the same object and inconsistent lock order can deadlock; no reentrancy, detection, FIFO fairness, or bounded waiting is promised. Ordinary Abort terminates the process without unwinding, so poisoning is not introduced (current §17.3).

### 3.2. Acquisition API and lookup

The functions belong to Kimi.Intrinsics, use argument label `value`, and have function Origin parameter `source`. S is one valid complete handle Type, decomposable as s/T. Both guard families retain S's target and dependencies and borrow its slot; they do not own S. The allowed modes below constrain both the functions and guard Type formation.

| Function / dot form | Allowed S | Input | Result | Destruction |
| --- | --- | --- | --- | --- |
| `read<S>` / `x.read()` | `local/T` | `ref/S from source` | `ReadGuard<S> from source` | End shared runtime borrow |
| `tryRead<S>` / `x.tryRead()` | `local/T` | Same | `Option<ReadGuard<S> from source>` | Some carries that responsibility |
| `write<S>` / `x.write()` | `local/T`, `sync/T` | Same | `WriteGuard<S> from source` | End local exclusive borrow or unlock sync |
| `tryWrite<S>` / `x.tryWrite()` | `local/T`, `sync/T` | Same | `Option<WriteGuard<S> from source>` | Some carries that responsibility |

These four names are the only registered dot operations; lock/tryLock and LockGuard are not introduced. A handle Place or a ref/uniq to its slot supplies the input by ordinary shared Borrow/Reborrow, including immutable bindings and §3.5 element results. Acquisition does not Move the handle.

A generic `s is guarded` body may use write/tryWrite on a valid s/T; read/tryRead additionally require `s is local`. Target-role evidence follows §5.2.2. The body cannot assume that write never blocks or that its guard has a capability not common to both modes.

Resolution uses standard declaration identity, not matching user-defined names or Contracts. Failure does not retry against a payload member. local/sync payload access always requires a guard; member forwarding, implicit receiver adaptation, casts, and refinement cannot bypass it. Metadata-only tests and view changes grant no payload access.

### 3.3. Guard Types and accessors

ReadGuard<S> and WriteGuard<S> are compiler-managed Non-Copy struct Cores with Origin parameter `source`. S is a complete source handle Type, not a standalone Semantics argument. Valid instantiations are `ReadGuard<local/T>`, `WriteGuard<local/T>`, and `WriteGuard<sync/T>`; `ReadGuard<sync/T>` is currently rejected, even without an acquisition call. Unknown modes retain these constraints. `ReadGuard<S> origin source` is declaration notation and `ReadGuard<S> from source` is a Type use.

Only acquisition functions construct guards. There are no public constructors, mutable management fields, user deinit additions, clone, or manual unlock/release APIs. Ordinary destruction releases the currently owned acquisition once.

Both accessors have no set and keep T's View Target. Each has a fixed get Type, with no receiver-based get overloading. In the table, S decomposes as s/T:

| Guard | Accessor | Receiver | Result |
| --- | --- | --- | --- |
| ReadGuard<S> | value, readValue | `ref/(ReadGuard<S> from source) from guard` | `objref/T from guard` |
| WriteGuard<S> | readValue | `ref/(WriteGuard<S> from source) from guard` | `objref/T from guard` |
| WriteGuard<S> | value | `uniq/(WriteGuard<S> from source) from guard` | `objuniq/T from guard` |

guard is the Origin of the accessor's borrow of the guard itself. Accessors do not update management state; they lend payload access. Ordinary Loans prevent conflicting shared/exclusive children and simultaneous exclusive children. Ending a child Loan permits another borrow but does not itself release the acquisition.

### 3.4. Lifetimes and destruction

```text
source handle slot -> guard -> payload child borrow -> fields, reborrows, captures
```

A guard borrows its source slot and adds no strong owner. source must outlive the guard. Preserve payload dependencies and actual Loan provenance through acquisition, view changes, casts, returns, storage, and captures. Another strong handle keeping the object alive does not make a released guard's child reference valid.

Guards use ordinary Move, storage, return, Origin, Loan, and thread rules, including inside using. Temporary sources keep normal temporary lifetimes (§4.2). Release occurs at ordinary destruction of the responsible guard, including a legal replacement. Last use alone cannot advance an observable release. Abort and divergence follow ordinary Scope Exit (§4.3).

### 3.5. Element acquisition

Extend SharedReadResult for arrays, Slice, and other existing users of that rule:

| Complete element Type | SharedReadResult |
| --- | --- |
| `local/T` | `ref/local/T from source` |
| `sync/T` | `ref/sync/T from source` |

object elements retain their existing objref payload result; guarded elements return a handle-slot borrow. The latter performs no payload borrowing, count increment, or guard acquisition and preserves storage and element dependencies. Generic checking includes both forms. Existing slot-borrow operations such as `@ref` and `tryGet()` are not replaced by SharedReadResult.

```kimi
// items: Slice<local/Item>
let handle = items[0]
using guard = handle.read()
    inspectName(guard.readValue.name@ref)
```

## 4. using as scoped-binding sugar

### 4.1. Syntax

```text
UsingExpression := [ Label : ] using Name [ : Type ] = Expression Body
```

using introduces one ordinary mutable local and a do-style body. There is no var/let modifier, bodyless form, comma-separated binding list, or same-indent header concatenation. Multiple acquisitions use ordinary nesting. Body, indentation, continuation, and grouping follow current §2.2 and §14.2; body-bearing initializers require parentheses.

At an expression start, including after a label, recognize using when followed on the same physical line by a Name and then `=` or `:`; ignore comments for this lookahead. Commit before checking the Type, initializer, or body, without fallback on error. `using = 1`, `using(x)`, and `x.using()` remain ordinary name uses.

### 4.2. Semantic expansion

`L: using x: T = e Body` is a do expression with the ordinary local initialization `var x: T = e` as its entry operation. Omitted labels and annotations remain omitted. Expansion preserves these source boundaries:

1. Resolve the annotation and initializer in the outer environment. Evaluate e once with ordinary var acquisition and inference. Neither the new x, label L, nor declarations inside Body are visible in e. Initializer temporaries end as for an ordinary local initializer; using adds no lifetime extension.
2. Introduce x at the beginning of the body's local scope, before its statements and defers. It follows ordinary local duplicate/shadowing rules and is visible throughout Body after successful initialization. The label is active only in Body, under current §14.4.
3. Apply do's result and transfer rules to the **original Body form and evaluation context**. Expansion is semantic, not a textual rewrite that changes a single-item expression into a discarded block item. Any internal result target is unnameable in source and changes no source transfer lookup.

An initializer may transfer to a valid outer target; failed or abandoned initialization creates no completed binding. Nested initializers can use already initialized outer bindings. This requires no new binding kind, ownership effect, or call-summary contract.

### 4.3. Results and ordinary ownership

Results, named exit, bare-exit transparency, function/defer barriers, and Scope Exit follow do and ordinary locals (current §14.2, §14.3.2, §14.5.2, §14.9, and §16.2). A value-used single-item expression supplies its value; an indented body reaching its end supplies Unit. A non-Unit block result requires a named exit. In Discard Context, named exits must fit Unit. using is not a yield/continue/return target or a lookup barrier.

The binding permits ordinary Move, assignment, Partial Move, borrowing, replace/exchange/swap, and any otherwise legal destruction; no using-specific protection applies. Scope Exit destroys only the initialized contents whose responsibility remains in that scope, in the ordinary reverse order. A moved-out guard is released by its destination owner. Neither initial-value identity nor release of that initial value at the end of using is guaranteed.

Normal result acquisition precedes cleanup. Reject references that depend on a guard destroyed before result delivery; allow independently valid values and transferred guards whose source dependencies survive. Option is not implicitly unwrapped. Abort, divergence, and incomplete-value cleanup retain the existing rules. Optional warnings for whole-binding Move or assignment may flag likely mistakes, but must not change acceptance or add interprocedural preservation analysis.

## 5. Thread capabilities

### 5.1. Complete-Type rules

Kimi.ThreadTransferable (TT) permits transferring a value's ownership to another thread; Kimi.ThreadShareable (TS) permits shared access from another thread. They are independent built-in guarantees. Owned proves neither, and none waives lifetime obligations.

| Complete Type | TT condition | TS condition |
| --- | --- | --- |
| Built-in Scalars, Unit, immutable string | True | True |
| Common Function Types, such as `(i32) -> i32` | Not established after environment erasure | Same |
| Other owner Cores | §5.2's structural/public guarantee | Same, for TS |
| `ref/T` | TS(T) | TS(T) |
| `uniq/T` | TT(T) | TS(T) |
| `obj/T` | TT(T) | TS(T) |
| `objref/T` | TS(T) | TS(T) |
| `objuniq/T` | TT(T) | TS(T) |
| `arc/T` | TT(T) and TS(T) | Same |
| `sync/T` | True if well-formed | Same; payload TS is not required |
| `rc/T`, `local/T` | False | False |
| `Weak<S>` | TT(S) | TS(S) |
| `ReadGuard<local/T>`, `WriteGuard<local/T>` | False | False |
| `WriteGuard<sync/T>` | False | TS(T) |

These are intrinsic rules: guards do not inherit S's capabilities, and analysis of hidden fields cannot override their rows. Ordinary structural validation checks stored components and captures plus destruction, static-state access, and external-resource contracts; inherited base parts follow §5.2.1. Raw pointers and erased environments require validated contracts, not inference from their physical fields. Unknown Types need declared constraints; same-spelled user Contracts and empty conformances prove nothing.

Function Items and concrete Closures retain evidence for structural validation. A common Function Type carries no TT/TS contract, even when its source callable has one; Shared invocation and an Owned environment do not supply it. Capability-qualified erased callable Types are deferred (§9.1). For now, retain the concrete Type as a generic F with `Callable<owner, (i32) -> i32>` and ThreadTransferable, or the required Callable receiver/signature and TT/TS constraints. Generic storage must also retain F instead of erasing it.

All guards are non-transferable. A sync write guard must release on its acquisition thread; a shared reference to it exposes readValue, not exclusive value. OS primitive choices cannot change its public capabilities.

### 5.2. Open Types, inheritance, and sync formation

#### 5.2.1. Public guarantees and base parts

For owner Cores, automatic structural derivation is restricted to Sealed Types. An open Core has TT/TS only through an explicit, validated public guarantee, declared on itself or inherited from a base. The corresponding requirement means the same guarantee in declarations and generic constraints; there is no separate ViewTT/ViewTS predicate.

`Self is ThreadTransferable` and `Self is ThreadShareable` require verification of the declaring Type and every derived Type, including added fields and effects. A derived Type cannot revoke the guarantee and need not repeat its declaration. A declaration is an obligation, not self-justifying proof. Conditional guarantees retain their conditions and substituted arguments throughout the hierarchy and are checked for every admitted binding. This inheritance rule is specific to these capabilities, not general Contracts or Copy.

In structural validation, expand each inherited base part's fields and effects into the complete derived object; do not require that base Type to have its own public capability. Include base destruction and external-resource/static-state effects, not just field layout. Inherited guarantee obligations still apply; unavailable evidence is not success. Record dependencies for separate compilation and revalidation. This proof grants no source access to private base members.

Thus a Sealed Dog may satisfy TT/TS even when its open base Animal has no guarantee. This exception concerns inheritance parts only: an independent owner/Animal field, or a handle/borrow targeting Animal, uses Animal's public capability under §5.1. A view change to Animal loses capabilities not guaranteed by Animal.

```kimi
public open struct Animal
    Self is ThreadTransferable
    public var age: i32

    public init(age: i32)
        self.age = age
```

#### 5.2.2. Target eligibility and generic evidence

For a valid object View Target T, the only additional capability required to form `sync/T` is TT(T). TT alone proves neither owner Semantics nor target eligibility: reference and handle Types can themselves satisfy TT. Distinguish these cases at definition checking:

| Situation | Required evidence |
| --- | --- |
| New object from an otherwise unknown complete Type parameter T | Prove `T is Sealed` for payload/target eligibility; sync additionally requires TT(T). No general open-Core constraint is introduced |
| Known supported Core, including a known open Type or a valid named generic Core application | Check its declaration and ordinary formation conditions; an open sync target additionally needs a validated TT guarantee |
| Operations on an already valid complete handle Type S decomposed as s/T | Retain its target role and dependencies under current §8.1.1. `s is guarded` admits write; `s is local` admits read. Open targets do not additionally require Sealed |

Target eligibility carried by a valid object handle is independent of ownership mode. An admitted transfer retaining T reuses that evidence; only destination-specific conditions need additional proof, notably TT(T) for sync. Thus a valid generic obj/T can transfer to local/T without proving Sealed. This does not add conversions beyond §2.1, prove a complete Sealed payload, or authorize arbitrary use of T as an owner value.

For a view change from s/T to s/V, prove V's target eligibility and Supports(T, V) under the ordinary view rules; eligibility of T alone is not evidence for an unrelated V. No separate per-mode target-eligibility proof is needed, but destination-specific capabilities, payload-erasure Owned proofs, and Loans still apply. Signature-derived obligations remain public under current §8.10; neither a private-body assumption nor a favorable instantiation supplies missing evidence. §8.6 gives examples; additional constraints for creating unknown open Cores remain deferred (§9.1).

obj/arc and object borrows may lose capabilities by changing views; subsequent use relies on the new static Type. Counts, known current objects, or optimizer guesses cannot strengthen a public guarantee. Valid type refinement or checked casts may supply new evidence under their usual rules.

A future runtime Contract View must require the capability in its public Contract and preserve it for every implementation; runtime Contract Views remain deferred. Publish capability guarantees, conditions, and proof dependencies, and revalidate after relevant changes.

### 5.3. Recursive proof

Resolve intrinsic TT/TS structural obligations and dependent Type formation together using the greatest fixed point of positive, finite dependencies. For example, `Node -> Weak<sync/Node> -> sync/Node formation -> TT(Node)` can form one finite group.

Construct the group first; establish external premises and propagate violations such as local/rc or unproven external-resource requirements. Expand open guarantees into their validation obligations; require them for open complete values/views, not for inherited base-part expansion (§5.2.1). Do not admit ordinary Contract-conformance cycles or publish provisional capability results. Infinite inline layout and unbounded growth of Type arguments remain errors under existing rules. Cache completed proofs.

### 5.4. Borrow transfer and execution facilities

Guard-derived child borrows use the ordinary §5.1 rows, including `objref/T`, `objuniq/T`, and permitted complete-payload projections. Preserve their full guard/source/external dependencies. The guards themselves stay on their acquisition thread; local counting and borrow-state operations also stay there. Cross-thread use of a sync guard reference requires its TS rule and never permits remote destruction or exclusive access through that shared reference.

A child borrow can cross threads only when its capabilities and end-of-use before conflicting access or guard destruction are proven. Moving or copying a child reference does not operate on the guard's management state. This proposal introduces no spawn/join/scheduler API; practical use depends on execution facilities that prove completion, such as scoped threads.

## 6. Reference counting, Weak, and cyclic construction

### 6.1. Operations and dependencies

S is a valid complete counted handle Type. Extend the existing Kimi.Intrinsics declarations; argument label is `value` and function Origin parameter is `input`:

| Operation | Input | Result |
| --- | --- | --- |
| Strong clone | `ref/S from input` | S |
| downgrade | `ref/S from input` | Weak<S> |
| upgrade | `ref/Weak<S> from input` | Option<S> |
| Weak clone | `ref/Weak<S> from input` | Weak<S> |

Keep strong and Weak clone as separate declarations, with S naming the handle Type in both. Borrow the input slot only for the call; results retain S's view, mode, Type arguments, and external dependencies, not the operation's temporary slot Loan.

clone adds responsibility without payload copying, user calls, or guard acquisition. Successful upgrade secures a strong owner; payload access still needs a guard for local/sync. clone/upgrade allocate no new management storage; the first downgrade may allocate a side table.

The final-strong race, non-resurrection from strong zero, complete dynamic destruction, original-allocation release, and Weak table lifetime follow current §13.5.8–9 and §21.2.3. Weak does not keep the payload alive, and expiration erases no Type, Origin, or Loan dependency. Weak destruction touches management storage only. obj, object borrows, guards, and async are not valid Weak ownership modes.

### 6.2. Cyclic construction

Complete-value creation uses `@`; cyclic construction remains a function protocol because it supplies self-Weak to a callback before the payload exists.

Strong cycles are not automatically collected; use Weak to break them. Mutable optional fields can be populated after normal construction under write. Required self-Weak or immutable fields instead use the cyclic builder protocol.

Retain `makeRcCyclic<T, F>` and `makeArcCyclic<T, F>` and add `makeLocalCyclic<T, F>` and `makeSyncCyclic<T, F>` in Kimi.Intrinsics. All use argument label `build`, return C/T, and follow one shared contract below, with C fixed by the function name. No generic Semantics-only parameter or new `@` callback conversion is introduced.

T must be a valid complete owner payload Core with `T is Owned`; C/T must be well-formed, including TT(T) for sync. Require `F is Callable<owner, (Weak<C/T>) -> T>`. Acquire F normally and invoke it once on the calling thread; F itself need not be Copy, Owned, or TT. Owned(T) prevents dependencies first obtained by the builder from escaping through an already published Weak.

1. Allocate unpublished object storage and a side table with the construction guard and one builder Weak. Initialize the mode's management state before it can be observed.
2. Pass the Weak by value. During Building, upgrade returns None; there is no strong handle, payload access, or guard acquisition for this object. Other completed objects are unaffected.
3. Finish the builder's normal result and call cleanup, then Move the complete T into the payload. Do not repeat its constructor or expose partially initialized data.
4. Publish Alive and strong one exactly once, with local initially unborrowed and sync initially unlocked. Retain the existing atomic publication ordering for arc/sync.

After publication, the ordinary guard/Weak rules apply. Required allocation or count-overflow failure Aborts before publishing a result. Builder Abort or divergence prevents publication and subsequent work, with no Abort rollback/cleanup guarantee. Ordinary transfers during argument evaluation retain normal cleanup. Cyclic construction starts with a side table and has no one-allocation guarantee.

## 7. Implementation and performance goals

### 7.1. Management and optimization

Each object has one logical management state, shared across clones and views. Count, borrowing, lock state, and release responsibility remain distinct. using adds no runtime state or acquisition permission.

Guards should need neither an extra strong owner nor a per-guard heap allocation. The source-slot Loan is static and need not be followed at runtime. Prefer compact access to management state without stale pointers after guard Moves or state migration. Waiting infrastructure may require separate resources (§7.2).

For local, an implementation may omit checks and state updates only when their absence is unobservable, including conflict Abort, try results, overflow, nested/reentrant acquisition, calls, cleanup, and migration. Absence of clone/downgrade alone is not sufficient proof: one handle may have overlapping guards. Elide acquisition and release effects consistently, preserving logical Loan/lifetime checks. A provable conflict may produce an optional warning, not a new compile-time error or an earlier Abort on an unevaluated path. This optimization and warning are not mandatory analyses.

Handle slots, guards, payloads, and management storage are distinct. Current §21.5.5's ref/uniq attributes concern the immediate storage; do not extend readonly/noalias through a loaded handle to its payload/header without proof, especially with interior mutation and concurrency.

### 7.2. Control word and waiting

The current Windows x64 layout has descriptor at +0, control at +8, and payload at +16; obj control is zero. Preserve payload position and reuse control plus side tables:

| Mode | Implementation goal |
| --- | --- |
| obj to rc/arc | Initialize control=2 (strong one) before publication, with no extra management allocation under the current header profile |
| obj to local | Pack strong count, shared-borrow count, exclusive state, and representation tag into non-atomic control; avoid allocation at initial transfer |
| sync | Prefer a stable header-address waiting key with an external parking table; retain inline counting initially if the combined protocol is verified |

Transfer is from a live obj, not resurrection of a destroyed counted object. After shared publication, arc/sync must not mix atomic and non-atomic management accesses. Maintain the current runtime initialization/release ordering and the additional Mutex ordering.

On local's first downgrade, move counts and borrow state into one published table. Existing guards release against the current representation; count limits follow §7.3.1.

#### 7.2.1. sync representation candidate

Use the original header address as the waiting key, never an adjusted view address or a movable side-table lock address. Keep locked/has-waiters flags at fixed bit positions in the header across inline/table representations. Reserving the low three bits for tag and flags requires table alignment of at least 8 and pointer recovery that clears all three bits while preserving high bits. This is a candidate-specific requirement, not a change to rc/arc's one-bit tag or a fixed sync ABI.

Migration still uses a whole-word CAS and preserves the latest count and flags. Inline count updates must recheck the representation, as in current §21.2.3: an unconditional fetch_add after observing an inline count could instead modify a concurrently published table pointer. Stable table counts may use fetch_add/fetch_sub where §7.3 and the lifetime/order rules permit. upgrade still requires a conditional retain that cannot resurrect zero; clone's fast path cannot replace it.

#### 7.2.2. Bitwise lock fast-path candidate

Evaluate the following operations against a CAS implementation:

| Operation | Candidate | Required interpretation |
| --- | --- | --- |
| Acquire | `fetch_or(LOCKED, Acquire)` | Acquire only if the returned word had LOCKED clear; otherwise take the conflict path |
| Release | `fetch_and(~LOCKED, Release)` | Clear only LOCKED; enter the parking slow path if the returned word had HAS_WAITERS set |

These operations preserve counts, tags, and unrelated flags in either representation. They avoid software CAS retries caused by count-only changes, but still contend on the same cache line and may cause migration/count CAS retries. Neither their performance nor a particular machine instruction is guaranteed. Do not mandate fetch_add for inline counts or describe the whole protocol as CAS-free.

The [parking_lot 0.12.5 Mutex implementation](https://docs.rs/parking_lot/0.12.5/src/parking_lot/raw_mutex.rs.html) uses CAS for its ordinary lock/unlock fast paths; it is not evidence that this proposed bitwise protocol is already verified.

#### 7.2.3. Waiting and lifetime obligations

Verify joint count/tag/flag transitions, waiter registration versus unlock, no lost wakeups, release/acquire synchronization across migration, and final release. Coordinate HAS_WAITERS updates, state validation, and enqueueing under the queue protocol; observing contention alone never permits sleeping. Wakeup must recheck authoritative state. The [parking_lot_core park contract](https://docs.rs/parking_lot_core/0.9.12/parking_lot_core/fn.park.html) illustrates the required coordination between validation and enqueueing.

Waiting callers keep the object alive through source-handle Loans. Detach wait records before header reuse to prevent cross-lifetime address aliasing. Queue storage, initialization, contention, OS resources, and failures require separate accounting. If the combined header protocol cannot be validated economically, a stable side-table lock initialized at creation remains an alternative; §2 permits management allocation during transfer.

### 7.3. Profile obligations and allocation goals

#### 7.3.1. Count limits and check timing

Prefer simple, fast normal paths. For stable table counts, allow fetch_add followed by a limit check and Abort before result publication, subject to §7.2.1's operation-specific conditions. Reserve a substantial fixed margin between the limit and representation overflow; an unshifted u64 with limit `2^63 - 1` provides such a margin. Profiles may assume that simultaneously pending increments cannot exhaust this margin before termination. Document this practical execution assumption; no formal bound covering arbitrarily many stalled overflowing operations is required. Do not add a CAS loop solely to eliminate that theoretical case. A failing check proceeds directly to process Abort, without user calls or unwinding.

Current §21.2.3 requires pre-update checking. Retain it for the existing rc/arc inline encoding (`strong << 1` in u64): this representation has no headroom at its limit and already needs a checked update protocol for table migration. Permit post-update checks for suitable table counts without changing existing rc/arc limits. New local/sync limits must account for packed state and remain unchanged across migration. Representation races and upgrade from zero remain correctness requirements, independent of the practical overflow assumption.

#### 7.3.2. Allocation and measurement

For normal creation, obj/rc/arc/local target one object allocation; the preferred sync candidate targets the same before Weak creation or contention. Later downgrade, external waiting infrastructure, and cyclic builders are separate costs. No total one-allocation guarantee is made. A sync implementation using a separate table may require another allocation during creation or transfer.

Share count/Weak/release machinery between rc/local and arc/sync where their contracts permit it. Bit assignments, limits, table layouts, compact guard representation, queue protocols, and resource cleanup need profile validation and measurement. This proposal alone does not change the fixed ABI or prove that OS resources need no cleanup.

## 8. Examples

### 8.1. local sharing, mutation, and conflicts

```kimi
struct Item
    public var name: string

    public init(name: string)
        self.name = name

func inspectName(name: ref/string) => ()

var first = Item.init("initial")@local
var second = Kimi.Intrinsics.clone(first@ref)

using reader = first.read()
    inspectName(reader.readValue.name@ref)
    match second.tryWrite()
        .None => () // The same object still has a shared acquisition.
        .Some(var writer)
            writer.value.name = "unexpected"

using writer = second.write()
    writer.value.name = "changed"
    inspectName(writer.readValue.name@ref)

using reader = first.read()
    inspectName(reader.readValue.name@ref)
```

reader remains acquired until destruction even after its last use. Replacing tryWrite with write would Abort on conflict. string is Non-Copy and is borrowed for inspection. A Pattern-bound guard already receives ordinary arm-scope cleanup; no nested using is needed.

### 8.2. sync, nesting, and exit

Assume Item satisfies TT. This example creates no threads.

```kimi
var first = Item.init("first")@sync
var second = Item.init("second")@sync
var finished = false

loop
    work: using left = first.write()
        using right = second.write()
            if finished
                exit // Destroy both guards and exit the loop.

            left.value.name = "updated"
            finished = true
            exit to work // Destroy both guards; the loop proceeds to its next iteration.
```

### 8.3. Payload and guard replacement

```kimi
func replaceItem(target: uniq/Item)
    Kimi.Intrinsics.replace(target, with: Item.init("replaced"))

var shared = Item.init("initial")@local
using guard = shared.write()
    replaceItem(guard.value@uniq/Item) // Item is Sealed; replace the complete payload.
    inspectName(guard.readValue.name@ref)

using guard = shared.read()
    Kimi.Intrinsics.replace(guard@uniq, with: shared.read())
    // Legal: install another read guard and release the old acquisition.
    inspectName(guard.readValue.name@ref)

    guard = shared.read() // Ordinary assignment is also legal.
    let taken = guard    // Ordinary Move; guard is now Moved.
    inspectName(taken.readValue.name@ref)
// taken is destroyed; the moved-from guard binding has no remaining responsibility.
```

All acquisitions in the second using depend on the same valid source, with no surviving child Loan at an update. A helper has the same permission. Assignment and replace evaluate/acquire the replacement before destroying the old guard. Read acquisitions can coexist; reacquiring the same object's exclusive guard before releasing it Aborts for local and self-deadlocks for sync. Neither operation means “release, then reacquire.” replace returns Unit; exchange instead returns the old value without destroying it. Their usual evaluation and Loan rules remain distinct.

### 8.4. Weak migration with a live guard

```kimi
var strong = Item.init("initial")@local
using reader = strong.read()
    let weak = Kimi.Intrinsics.downgrade(strong@ref)
    match Kimi.Intrinsics.upgrade(weak@ref)
        .Some(let restored)
            using another = restored.read()
                inspectName(another.readValue.name@ref)
        .None => ()
```

Migration preserves reader's acquisition and release responsibility. In this example strong remains live, so upgrade succeeds; in general it may lose a final-release race. Success never acquires a guard automatically. Bind the successful handle before acquiring a guard, as restored does here: a guard borrowing a temporary handle cannot survive that temporary's ordinary lifetime (§3.4). using does not extend it, and upgrade's Option is not implicitly unwrapped.

A required immutable self-Weak can be initialized by a cyclic builder:

```kimi
struct Node
    public let selfWeak: Weak<local/Node>

    public init(selfWeak: Weak<local/Node>)
        self.selfWeak = selfWeak

func buildNode(weak: Weak<local/Node>) -> Node
    let unavailable = Kimi.Intrinsics.upgrade(weak@ref) // None during Building.
    return Node.init(weak)

var node = Kimi.Intrinsics.makeLocalCyclic(buildNode)
```

### 8.5. Expression results

```kimi
struct Counter
    public var count: i32

    public init(count: i32)
        self.count = count

var shared = Counter.init(41)@local
let next = using guard = shared.read() => guard.value.count + 1

let answer = work: using guard = shared.read()
    exit to work: guard.value.count + 1

let kept = using guard = shared.read() => guard
// Legal: responsibility moves to kept; shared must outlive kept.

// Error: the result borrows a guard that is destroyed before delivery.
// let escaped = using guard = shared.read() => guard.value
```

next and answer are independent scalar results; kept retains a guard and releases it at kept's ordinary destruction. Merely writing a scalar as the last item of the indented body would supply Unit, as with do. Semantic expansion must preserve the single-item result rather than turn it into such a discarded item.

### 8.6. Generic evidence and function-value migration

```kimi
func createShared<T>(value: T) -> rc/T
    T is Sealed
    return value@rc

func createSynchronized<T>(value: T) -> sync/T
    T is Sealed and ThreadTransferable
    return value@sync

func share<s/T>(value: s/T) -> local/T
    s is obj
    return value@local // Reuse target eligibility; no Sealed requirement.

func acquireWrite<s/T> origin source(value: ref/s/T from source)
    -> WriteGuard<s/T> from source
    s is guarded
    return Kimi.Intrinsics.write(value)

func acquireRead<s/T> origin source(value: ref/s/T from source)
    -> ReadGuard<s/T> from source
    s is local
    return Kimi.Intrinsics.read(value)

let createRc = func (value: Item) => value@rc
```

The creation helpers use Sealed as evidence for an otherwise unknown payload Type. share and the acquisition helpers preserve a valid input handle's target, including open targets, without requiring Sealed. Transferring to sync would additionally require TT(T). The closure replaces a factory function value using existing `func` syntax.

### 8.7. Sealed derivation through an open base

```kimi
open struct Animal
    public var age: i32

    public init(age: i32)
        self.age = age

struct Dog : Animal
    public init(age: i32) : base(age)
        ()

var dog = Dog.init(2)@sync // Dog is Sealed; its inherited i32 field is structurally safe.
// let invalid = dog@sync/Animal // Error: Animal has no public TT guarantee.

var exclusive = Dog.init(3)@obj
var baseView = exclusive@obj/Animal // Legal view change; loses Dog's TT/TS evidence.
```

This snippet defines its own Animal without a guarantee, independently of §5.2.1. An independent field of complete Type Animal would still need Animal's public capability; it does not receive the inherited-base exception.

## 9. Boundaries and future work

### 9.1. Excluded features

| Item | Boundary |
| --- | --- |
| async Semantics | Reserve only in Semantics contexts; reject `async/T`, `x@async`, and `s is async` |
| sync read/tryRead and RwLock | Deferred, including `ReadGuard<sync/T>` formation. Parallel readers need a TS policy and guard/thread rules beyond sync's TT requirement; write currently means exclusive Mutex acquisition |
| Automatic reentrancy/deadlock detection | No mandatory thread-ID field or detection; non-reentrancy and possible deadlock are explicit |
| Guards owning a strong handle | Initial APIs borrow the source slot only. A future owning guard could support retained acquisitions from temporary handles, including successful Weak upgrades (§8.4) |
| Counted-mode conversion, recovery to obj, strong-cycle collection | Not introduced; cyclic builders do not collect cycles |
| Manual unsafe capability conformance | Deferred pending a specific safety contract |
| Additional generic open-Core constraints | Deferred for unknown payload creation; known Core applications and operations on valid generic handles remain covered by §5.2.2 |
| Capability-qualified erased callable Types | TT/TS contracts and their syntax are deferred; retain a generic concrete callable under §5.1. Resolve capability preservation for erased callbacks when designing spawn APIs (Appendix D.2) |
| Payload relocation during ownership transfer | Forbidden; handle Move must preserve object identity and external registrations/pointers |
| Same-indent using header concatenation | Deferred; ordinary nesting has no extra layout or comment rules |
| using syntax on match Pattern bindings | No syntax added. Ordinary Pattern bindings already have scoped cleanup; revisit in Appendix D.1 only if a distinct convenience justifies it |

### 9.2. Async direction

A future `await source.write()` and an async write guard may provide objuniq access. Cancellation before acquisition must unregister the waiter; task destruction after acquisition must perform ordinary local/guard cleanup. Release is not rollback of payload changes.

ReadGuard and both synchronous WriteGuard instantiations should not cross await suspension; a future async guard needs a separate policy. Storage in aggregates or closures must not bypass it. Suspension capability is distinct from TT/TS.

Task Types, state machines, await Loans, cancellation/acquisition races, and async guard capabilities remain undesigned. Waiting for acquisition and awaiting asynchronous destruction are separate features; neither is enabled here.

## 10. Integration, compatibility, and verification

### 10.1. Integration targets

| Current specification | Required integration |
| --- | --- |
| [§2 Source](../../spec/02-source-and-lexical-structure.md), [§3 Types](../../spec/03-types-and-values.md) | Contextual names, categories, remove the no-counted statement, guards and complete-Type capability rules |
| [§4 Elements](../../spec/04-arrays-indexing-and-slices.md) | guarded SharedReadResult as a slot borrow |
| [§6 Declarations](../../spec/06-declarations-and-containers.md), [§8 Constraints](../../spec/08-generics-constraints-and-contracts.md) | Guarded decomposition, validated/inherited capabilities, base-part expansion, recursive proofs, target-role evidence and formation |
| [§7 Functions](../../spec/07-functions-and-callable-values.md), [§10 Adaptation](../../spec/10-overload-resolution-and-inference.md), [§11 Properties](../../spec/11-properties.md) | Four acquisition names, ReadGuard<S>/WriteGuard<S>, accessor receivers and Origin contracts |
| [§9 Names](../../spec/09-names-signatures-and-access.md) | Outer initializer/annotation lookup, body-local var binding and label scope during using expansion |
| [§12 Object calls](../../spec/12-expressions.md), [§15 Ownership](../../spec/15-ownership-and-lifetime-analysis.md) | Existing ObjectCallCompatible and Loan checks apply; no using-specific binding or root-preservation rule |
| [§13 Adaptation/ownership](../../spec/13-operators-and-assignment.md) | Allocation table; revise §13.5's no-resource-acquisition statement, §13.5.3's same-Semantics-only restriction, §13.5.6's failure table, and §13.5.7's obj-to-rc/arc exclusion; retire ordinary make APIs; extend clone/Weak and cyclic builders |
| [§14 Control](../../spec/14-control-flow.md), [§16 Destruction](../../spec/16-scope-exit-and-destruction.md) | using grammar/expansion; include using as do sugar in Completion, Body/result, label and §14.5.2 exit tables; reuse ordinary ownership and cleanup |
| [§18 Artifacts](../../spec/18-modules-and-dependencies.md) | Capability/structural/effect proof dependencies and guard Types/Origins; no using-specific effect summary |
| [§21 Runtime](../../spec/21-layout-runtime-and-code-generation.md), [§22 Kimi](../../spec/22-core-execution-and-foreign-functions.md) | Management/synchronization protocols and profile requirements; qualify §21.2.3's pre-update overflow rule under §7.3.1, documenting its practical execution assumption and retaining current inline checks/limits; standard identities and declaration catalog changes |
| [Appendix D](../../spec/appendices/D-deferred-features.md), [Appendix F](../../spec/appendices/F-syntax-summary.md), [SPEC index](../../SPEC.md) | Deferred boundaries, including capability-qualified erased callables alongside D.2's spawn design; using grammar, Semantics constraints, allocation-capable @, factory removal and new APIs |

Update related Type-test, cast, member-adaptation, and acquisition tables so guarded access has no bypass. Current SPEC/STATUS must not report the proposal as integrated or implemented before separate integration and verification.

### 10.2. Compatibility and migration

| Change | Effect and migration |
| --- | --- |
| Ordinary make API removal | Source-breaking: replace complete-value calls with @obj/@rc/@arc. Function references, aliases, and higher-order uses require a closure or named wrapper; @ is not a function value. §8.6 uses the existing `func` syntax and inference rules. makeLocal/makeSync are not introduced; cyclic names remain |
| owning/reference expansion | Revalidate generic bodies, specialization applicability, public proofs, and caches over the larger domains. A body assuming direct object payload access may fail. object membership is unchanged; guarded/counted are new |
| SharedReadResult | Concrete object elements keep objref payload results; guarded elements yield handle-slot ref. Expanded generic domains cannot assume one result shape |
| Thread capabilities | Open complete values/views require explicit or inherited TT/TS guarantees; failure propagates through dependent fields and borrows. Inherited base parts use §5.2.1's structural rule. This is proposed capability design, not existing cross-thread implementation support |
| Earlier draft spellings | Both guards take complete handle Types; sync uses write/tryWrite. using has one nested binding and ordinary var ownership, with no retention guarantee or direct-operation errors. Update old guard annotations, lock calls, header concatenation, and examples accordingly |

### 10.3. Required verification

These are acceptance obligations, not executed test results. Diagnostics identify the relevant operation, binding, Loan, call path, or missing proof.

#### 10.3.1. Types and APIs

| Area | Representative checks |
| --- | --- |
| Names and generic domains | Ordinary contextual-name uses, async rejection, expanded categories, full-domain checking and specialization selection |
| Adaptation | All operation/allocation rows; creation/transfer/Move distinction; old factory calls, aliases and function-value uses; preserved identity/address/dependencies; no combined creation/upcast |
| Target evidence and views | Sealed generic creation, valid-handle eligibility reused across admitted transfers without Sealed, additional sync TT, destination-view eligibility/Supports/Owned proofs, no unsupported open-Core constraint, slot covariance, or direct guarded payload access |
| Guards | ReadGuard<local/T> and both WriteGuard modes, reject ReadGuard<sync/T>, mode-constrained generic calls, fixed accessor receivers, same-named user declarations supply no privilege |
| Capabilities | Sealed derivation through an undeclared open base, required base effects, independent open fields rejected without guarantees, inherited/conditional obligations, recursive failure propagation, child transfer; concrete generic callables retain evidence, common Function Types do not |

#### 10.3.2. Ownership and control flow

| Area | Representative checks |
| --- | --- |
| using expansion | Expression recognition, required Body, normal nesting, no concatenation; outer annotation/initializer lookup, body-local function invisibility there, x shadowing/duplicates, label inactivity in its initializer |
| Results and transfers | Single-item versus block results, Value/Discard Context, Never, valued named exit, bare-exit transparency, function/defer barriers and outward return/continue/yield |
| Acquisition and lifetime | Source-slot dependencies; bind upgraded handles before retained acquisition, reject guards outliving temporary sources; ordinary Move, assignment, Partial Move and remaining-part cleanup; valid guard return versus dangling child result; storage/capture/helper dependencies |
| Replacement and cleanup | Child Loan conflicts, new acquisition before old release, local Abort/sync self-deadlock on same-object exclusive replacement, replace versus exchange results, reverse cleanup, initializer abandonment, Abort/divergence |
| Cyclic builders | Four modes; Owned payload, sync TT, one callback and completed call cleanup, None during Building, no partial-object guards, one publication, escaped Weak and final release |

#### 10.3.3. Runtime and implementation boundaries

| Area | Representative checks |
| --- | --- |
| Acquisition state | local shared/exclusive conflicts, sync exclusive read use, nonblocking try outcomes, overflow Abort before result publication, no wrap/saturating success or hidden strong clone |
| Count protocols | Fixed headroom and documented practical execution assumption; limit-boundary checks and direct Abort without result publication; preserve limits/encoding and current inline checks; table fast paths; upgrade never resurrects zero |
| Migration and waiting | Guard-live downgrade, one authoritative state, stable wait key, three-bit budget/alignment >= 8 and pointer mask for the sync candidate, flag-preserving bitwise operations and whole-word migration CAS, registration/unlock/migration races, atomic ordering, no stale updates or wakeups after address reuse |
| Optimization and artifacts | Preserve observable checks/releases/try outcomes and reentrancy behavior; optional warnings do not change acceptance; measure allocations separately from guarantees; validate alias attributes and cross-module proof invalidation |
