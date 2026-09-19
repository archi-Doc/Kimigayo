# Shared Ownership, Guards, and using — Revised Specification Proposal

- Date: 2026-09-19
- Status: Proposal pending integration into SPEC; not implemented support or a verified ABI.
- Scope: local/sync, guards, using, thread capabilities, Weak, object creation and ownership transfer.

Except for the changes stated here, [SPEC.md](../../SPEC.md) and its chapters apply. “Current §” denotes that specification; other section references are local. Sections 1–6 define proposed language requirements, §7 implementation goals, and §9 deferred work. Examples use proposed syntax and are not compiler test results. Snippets are independent; Item and inspectName are defined in §8.1.

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
| Acquisition lifetime | `ReadGuard<T>`, `WriteGuard<S>` | Owned values responsible for releasing a borrow or lock; S is a complete guarded handle Type |
| Scope management | `using name = expression Body` | A scoped binding and expression with ordinary Scope Exit |

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

This replaces current §3.3's exclusion of counted. Generic bodies constrained by owning/reference must be checked for the expanded sets; compatibility effects are listed in §10.1.

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

Creation and ownership transfer retain T. A single adaptation cannot combine either operation with a view change; write separate operations. `@M` infers T from the input. Access, initialization, Origins, Loans, payload-erasure Owned proofs, and sync formation remain mandatory. Generic bodies must prove operation selection and validity for every admitted input, without assuming an unknown Type is an owner Core.

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
| Shared-borrow count overflow | Abort before changing state | Same; not None |

Only success creates a guard with one release responsibility. Failure leaves neither an acquired state nor a responsibility to release it. Check strong/weak/borrow count overflow before incrementing; counts never wrap or saturate into success. Preserve existing rc/arc limits; profiles define limits for the added state. try results report acquisition conflicts, not recovery from overflow or required runtime-resource failure.

sync release has release ordering; successful write/tryWrite has acquire ordering. Failed tryWrite supplies no acquisition synchronization. Reacquisition of the same object and inconsistent lock order can deadlock; no reentrancy, detection, FIFO fairness, or bounded waiting is promised. Ordinary Abort terminates the process without unwinding, so poisoning is not introduced (current §17.3).

### 3.2. Acquisition API and lookup

The functions belong to Kimi.Intrinsics, use argument label `value`, and have function Origin parameter `source`. For write/tryWrite, S is a valid complete `local/T` or `sync/T` handle Type, decomposable as `<s/T>` with `s is guarded`. The guard retains S's target and dependencies; it does not own S. Formation checks apply to both the input and the result.

| Function / dot form | Input | Result | Destruction |
| --- | --- | --- | --- |
| `read<T>` / `x.read()` | `ref/local/T from source` | `ReadGuard<T> from source` | End shared runtime borrow |
| `tryRead<T>` / `x.tryRead()` | Same | `Option<ReadGuard<T> from source>` | Some carries that responsibility |
| `write<S>` / `x.write()` | `ref/S from source` | `WriteGuard<S> from source` | End local exclusive borrow or unlock sync |
| `tryWrite<S>` / `x.tryWrite()` | Same | `Option<WriteGuard<S> from source>` | Some carries that responsibility |

These four names are the only registered dot operations; lock/tryLock and LockGuard are not introduced. A handle Place or a ref/uniq to its slot supplies the input by ordinary shared Borrow/Reborrow, including immutable bindings and §3.5 element results. Acquisition does not Move the handle.

A generic `s is guarded` body may use write/tryWrite on a valid s/T; it must not assume that write never blocks, that read exists, or that the resulting guard has a capability not common to both modes. A category constraint alone does not make an arbitrary T a valid object target.

Resolution uses standard declaration identity, not matching user-defined names or Contracts. Failure does not retry against a payload member. local/sync payload access always requires a guard; member forwarding, implicit receiver adaptation, casts, and refinement cannot bypass it. Metadata-only tests and view changes grant no payload access.

### 3.3. Guard Types and accessors

ReadGuard<T> and WriteGuard<S> are compiler-managed Non-Copy struct Cores with Origin parameter `source`. S is one complete guarded handle Type, not a standalone Semantics argument. Thus `WriteGuard<local/T>` and `WriteGuard<sync/T>` are distinct ordinary generic instantiations; an unknown mode retains its constraints. `ReadGuard<T> origin source` is declaration notation and `ReadGuard<T> from source` is a Type use.

Only acquisition functions construct guards. There are no public constructors, mutable management fields, user deinit additions, clone, or manual unlock/release APIs. Ordinary destruction releases the currently owned acquisition once.

Both accessors have no set and keep T's View Target. Each has a fixed get Type, with no receiver-based get overloading. In the table, S decomposes as s/T:

| Guard | Accessor | Receiver | Result |
| --- | --- | --- | --- |
| ReadGuard<T> | value, readValue | `ref/(ReadGuard<T> from source) from guard` | `objref/T from guard` |
| WriteGuard<S> | readValue | `ref/(WriteGuard<S> from source) from guard` | `objref/T from guard` |
| WriteGuard<S> | value | `uniq/(WriteGuard<S> from source) from guard` | `objuniq/T from guard` |

guard is the Origin of the accessor's borrow of the guard itself. Accessors do not update management state; they lend payload access. Ordinary Loans prevent conflicting shared/exclusive children and simultaneous exclusive children. Ending a child Loan permits another borrow but does not itself release the acquisition.

### 3.4. Lifetimes and destruction

```text
source handle slot -> guard -> payload child borrow -> fields, reborrows, captures
```

A guard borrows its source slot and adds no strong owner. source must outlive the guard. Preserve payload dependencies and actual Loan provenance through acquisition, view changes, casts, returns, storage, and captures. Another strong handle keeping the object alive does not make a released guard's child reference valid.

Guards use ordinary Move, storage, return, Origin, Loan, and thread rules, subject to §4.4's direct using-binding restrictions. Temporary sources keep normal temporary lifetimes; using does not extend them unconditionally. Release occurs at ordinary destruction of the responsible guard, including a legal replacement. Last use alone cannot advance an observable release. Abort and divergence follow §4.3.

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

## 4. using and Scope Exit

### 4.1. Syntax and binding

```text
UsingExpression := [ Label : ] using Name [ : Type ] = Expression Body
```

using is an expression with the result and transfer rules of do. It has one binding, no var/let modifier, and a required ordinary Body (current §14.2). There is no bodyless declaration, comma-separated binding list, or same-indent header concatenation. Body-bearing initializers require parentheses; existing indentation, continuation, and single-item delimiter-region rules apply.

At an expression start, including after a label, recognize using when followed on the same physical line by a Name and then `=` or `:`; ignore comments for this lookahead. Commit before checking the Type, initializer, or body, without fallback on error. `using = 1`, `using(x)`, and `x.using()` remain ordinary name uses.

Evaluate the initializer once and use ordinary acquisition, Type inference, and Origin inference. The initialized binding is visible only in its body and follows local name-conflict rules. It permits legal mutation and borrowing with the direct restrictions of §4.4. No special guard-only binding rule is needed.

### 4.2. Nesting, labels, and results

Multiple acquisitions use ordinary nesting. Inner initializers may borrow outer bindings. Each using may have a label, active only in its body, not its own initializer. Labels obey current §14.4's namespace, overlap, and function/defer boundaries. Missing bodies are errors; blank lines and comments use ordinary layout rules and never concatenate headers.

Result rules are exactly those of do, including current §14.9's structural result sources, expected Types, Never, and discarded-result checking:

| Context and body | Result |
| --- | --- |
| Value Context, single-item expression body | The expression's normal result |
| Indented body reaching its end | Unit; direct expressions, including the last, are discarded |
| Named self-targeted `exit to Label: value` | The secured value, checked against the Target Result Type |
| Discard Context | Target Result Type is Unit; named exits must fit Unit |

An omitted exit operand means Unit. `yield` is not a using result mechanism. In particular, expression-valued using does not introduce last-expression results for indented bodies.

```kimi
// first and second are distinct sync/Item objects.
work: using left = first.write()
    using right = second.write()
        left.value.name = "left"
        right.value.name = "right"
        exit to work
// right and then left have been destroyed.
```

Nested acquisition is sequential, not atomic or transactional. If an inner initializer exits normally to an outer target, already initialized outer bindings receive ordinary Scope Exit; previous payload changes are not rolled back.

### 4.3. Transfers and cleanup

Add using beside do in current §14.1's Exit-completion row, §14.2's body/result rules, §14.3–4's expression/label rules, and §14.5.2's named-exit target row. Bare exit still targets the nearest iteration or defer and skips using. using is neither a continue/yield/return target nor a lookup barrier. An exit from an inner loop alone does not leave an enclosing using; function/defer barriers remain unchanged.

On normal completion or a valid outward transfer:

1. Secure the result under ordinary acquisition rules and verify that its dependencies survive cleanup.
2. Clean up body locals, defer actions, and temporaries under the existing Scope Exit rules.
3. Clean up the using binding's current contents, then deliver the result or continue outward. Nesting produces reverse acquisition order.

A result depending on a guard being destroyed is rejected, including dependencies hidden in an aggregate or closure. Independent results remain valid; do not reject a value merely because it was computed under a guard. Directly returning the using-bound guard also violates §4.4.

Initializer temporaries and incomplete values follow existing construction/cleanup rules. Failed initialization creates no completed using binding. try None is an ordinary value; using does not unwrap Option. Abort guarantees no ordinary cleanup; divergence during evaluation or destruction prevents subsequent cleanup and delivery. Last use alone does not trigger early destruction.

### 4.4. Direct binding restrictions

using restricts operations on its binding, not the identity of the value initially stored there:

| Operation | Rule |
| --- | --- |
| Direct whole-binding Move, including owning argument, return, storage, or capture | Forbidden |
| Direct whole-binding assignment or explicit destruction | Forbidden |
| Copy, reading, legal partial mutation, shared/exclusive borrowing | Ordinary rules |
| replace/exchange/swap through uniq, directly called or through a helper | Ordinary ownership, initialization, Origin, and Loan rules |
| Partial Move of accessible parts | Ordinary extraction and remaining-part cleanup rules |

Apply the first two checks after name resolution and ordinary acquisition classification; they are not text matching. Borrowing the slot with `guard@uniq` is permitted. No using-specific root-preservation effect, alias propagation, call-summary requirement, ObjectCallCompatible exception, or runtime protection flag is added. Existing safety/effect checks remain in force.

A legal replace may release the original guard early; exchange/swap may transfer it to another owner with a different destruction time. Every destination must retain the guard's source dependencies and thread restrictions. Live child Loans forbid conflicting updates. Guard management fields remain inaccessible.

using cleans up what the binding holds at exit, including remaining initialized parts after a permitted Partial Move. It guarantees neither preservation of the initial value nor continuous possession of the original lock/resource. A File operation likewise follows its Type's contract, not an “always open” using guarantee.

## 5. Thread capabilities

### 5.1. Complete-Type rules

Kimi.ThreadTransferable (TT) permits transferring a value's ownership to another thread; Kimi.ThreadShareable (TS) permits shared access from another thread. They are independent built-in guarantees. Owned proves neither, and none waives lifetime obligations.

| Complete Type | TT condition | TS condition |
| --- | --- | --- |
| Built-in Scalars, Unit, immutable string | True | True |
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
| `ReadGuard<T>`, `WriteGuard<local/T>` | False | False |
| `WriteGuard<sync/T>` | False | TS(T) |

These are intrinsic rules; for example, structural analysis of a guard's hidden fields cannot override its row. Ordinary structural validation checks the corresponding capabilities of stored components, bases, and captures, together with destruction, static-state access, and external-resource contracts. Raw pointers and erased environments require validated contracts, not an inference from their physical fields. Unknown Types need declared constraints; same-spelled user Contracts and empty conformances prove nothing.

All guards are non-transferable. A sync write guard must release on its acquisition thread; a shared reference to it exposes readValue, not exclusive value. OS primitive choices cannot change its public capabilities.

### 5.2. Open Types, inheritance, and sync formation

For owner Cores, automatic structural derivation is restricted to Sealed Types. An open Core has TT/TS only through an explicit, validated public guarantee, declared on itself or inherited from a base. The corresponding requirement means the same guarantee in declarations and generic constraints; there is no separate ViewTT/ViewTS predicate.

`Self is ThreadTransferable` and `Self is ThreadShareable` require verification of the declaring Type and every derived Type, including added fields and effects. A derived Type cannot revoke the guarantee and need not repeat its declaration. A declaration is an obligation, not self-justifying proof. Conditional guarantees retain their conditions and substituted arguments throughout the hierarchy and are checked for every admitted binding. This inheritance rule is specific to these capabilities, not general Contracts or Copy.

```kimi
public open struct Animal
    Self is ThreadTransferable
    public var age: i32

    public init(age: i32)
        self.age = age
```

For a valid object View Target T, the only additional capability required to form `sync/T` is `T is ThreadTransferable`. **TT alone does not prove target eligibility:** a complete Type parameter might be a reference or handle that itself satisfies TT. Generic signatures must separately establish T's target role through existing Type-role evidence; Sealed remains sufficient, but is not required for known open targets with a guarantee.

View changes recheck destination formation and capabilities. obj/arc and object borrows may lose capabilities by changing views; subsequent use relies on the new static Type. Counts, known current objects, or optimizer guesses cannot strengthen a public guarantee. Valid type refinement or checked casts may supply new evidence under their usual rules.

Open values without the corresponding guarantee are not TT/TS even when their current fields would pass structural analysis. A future runtime Contract View must require the capability in its public Contract and preserve it for every implementation; this does not introduce runtime Contract Views now. Publish capability guarantees, conditions, and proof dependencies, and revalidate after relevant changes.

### 5.3. Recursive proof

Resolve intrinsic TT/TS structural obligations and dependent Type formation together using the greatest fixed point of positive, finite dependencies. For example, `Node -> Weak<sync/Node> -> sync/Node formation -> TT(Node)` can form one finite group.

Construct the group first; establish external premises and propagate violations such as local/rc or unproven external-resource requirements. Expand explicit open guarantees into their validation obligations and reject a missing guarantee. Do not admit ordinary Contract-conformance cycles or publish provisional capability results. Infinite inline layout and unbounded growth of Type arguments remain errors under existing rules. Cache completed proofs.

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

Each object has one logical management state, shared across clones and views. Count, borrowing, lock state, and release responsibility remain distinct. using's direct restrictions cannot replace runtime acquisition checks.

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

On local's first downgrade, move counts and borrow state into one published table. Existing guards release against the current representation. Preserve count limits across representations; migration cannot evade overflow. Existing rc/arc limits remain unchanged.

**sync candidate, not a fixed ABI:** use the original header address as the waiting key, never an adjusted view address or a movable side-table lock address. Keep lock/waiter flags in the stable header control while counts may migrate to a table. The word must still encode counts or a tagged table pointer: “two lock bits” does not make the whole CAS or migration a two-bit operation.

Before adopting this candidate, verify joint count/tag/flag transitions, registration-versus-unlock ordering, no lost wakeups, release/acquire synchronization across migration, and final release. Waiting callers must keep the object alive through existing handle Loans; detach wait records before header reuse to prevent cross-lifetime address aliasing. Wakeup must recheck the authoritative state. Queue storage, initialization, contention, OS resources, and failures require separate accounting.

A stable key removes the need to rekey waiters but does not prove that protocol or eliminate parking-table costs. The [parking_lot_core park contract](https://docs.rs/parking_lot_core/0.9.12/parking_lot_core/fn.park.html) illustrates why state validation and enqueueing must be coordinated under a queue lock. If the combined header protocol cannot be validated economically, a stable side-table lock initialized at creation remains an implementation alternative; §2 therefore permits management allocation during transfer.

### 7.3. Allocation goals and unresolved ABI

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
        .Some(var acquired)
            using writer = acquired
                writer.value.name = "unexpected"

using writer = second.write()
    writer.value.name = "changed"
    inspectName(writer.readValue.name@ref)

using reader = first.read()
    inspectName(reader.readValue.name@ref)
```

Without an explicit replacement, reader remains acquired until destruction even after its last use. Replacing tryWrite with write would Abort on conflict. string is Non-Copy and is borrowed for inspection. A guard held directly by a Pattern binding already receives ordinary arm-scope cleanup; the nested using adds its direct restrictions. §9.1 defers pattern-level using convenience.

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

### 8.3. Direct restrictions and borrowed replacement

```kimi
func replaceItem(target: uniq/Item)
    Kimi.Intrinsics.replace(target, with: Item.init("replaced"))

var shared = Item.init("initial")@local
using guard = shared.write()
    replaceItem(guard.value@uniq/Item) // Item is Sealed; replace the complete payload.
    inspectName(guard.readValue.name@ref)

    // let taken = guard // Error: direct Move of a using binding.
    // guard = shared.write() // Error: direct whole-binding assignment.

using guard = shared.read()
    Kimi.Intrinsics.replace(guard@uniq, with: shared.read())
    // Legal: install another read guard and release the old acquisition.
    inspectName(guard.readValue.name@ref)
```

The second example has no live child borrow during replacement; both acquisitions depend on the same valid source. A helper performing the same replacement has the same permission. A surviving child Loan would reject a conflicting replacement under ordinary rules, without using-specific effect analysis.

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

Migration preserves reader's acquisition and release responsibility. In this example strong remains live, so upgrade succeeds; in general it may lose a final-release race. Success never acquires a guard automatically.

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

// Error: the result borrows a guard that is destroyed before delivery.
// let escaped = using guard = shared.read() => guard.value
```

The two valid results are independent scalar values. Merely writing a scalar expression as the last item of the indented body would supply Unit, as with do.

## 9. Boundaries and future work

### 9.1. Excluded features

| Item | Boundary |
| --- | --- |
| async Semantics | Reserve only in Semantics contexts; reject `async/T`, `x@async`, and `s is async` |
| sync read/tryRead and RwLock | Deferred; parallel readers need a TS policy beyond sync's TT formation rule. write currently means exclusive Mutex acquisition |
| Automatic reentrancy/deadlock detection | No mandatory thread-ID field or detection; non-reentrancy and possible deadlock are explicit |
| Guards owning a strong handle | Initial APIs borrow the source slot only |
| Counted-mode conversion, recovery to obj, strong-cycle collection | Not introduced; cyclic builders do not collect cycles |
| Manual unsafe capability conformance | Deferred pending a specific safety contract |
| Payload relocation during ownership transfer | Forbidden; handle Move must preserve object identity and external registrations/pointers |
| Same-indent using header concatenation | Deferred; ordinary nesting has no extra layout or comment rules |
| using semantics on match Pattern bindings | Future Appendix D.1 topic; no new syntax or implicit using protection. Define acquisition, scope, and direct restrictions before adoption |

### 9.2. Async direction

A future `await source.write()` and an async write guard may provide objuniq access. Cancellation before acquisition must unregister the waiter; task destruction after acquisition must perform ordinary local/guard cleanup. Release is not rollback of payload changes.

ReadGuard and both synchronous WriteGuard instantiations should not cross await suspension; a future async guard needs a separate policy. Storage in aggregates or closures must not bypass it. Suspension capability is distinct from TT/TS.

Task Types, state machines, await Loans, cancellation/acquisition races, and async guard capabilities remain undesigned. Waiting for acquisition and awaiting asynchronous destruction are separate features; neither is enabled here.

## 10. Integration and verification

### 10.1. Integration targets and compatibility

| Current specification | Required integration |
| --- | --- |
| [§2 Source](../../spec/02-source-and-lexical-structure.md), [§3 Types](../../spec/03-types-and-values.md) | Contextual names, categories, remove the no-counted statement, guards and complete-Type capability rules |
| [§4 Elements](../../spec/04-arrays-indexing-and-slices.md) | guarded SharedReadResult as a slot borrow |
| [§6 Declarations](../../spec/06-declarations-and-containers.md), [§8 Constraints](../../spec/08-generics-constraints-and-contracts.md) | Guarded Semantics decomposition, validated/inherited capabilities, recursive proofs, target eligibility and formation |
| [§7 Functions](../../spec/07-functions-and-callable-values.md), [§10 Adaptation](../../spec/10-overload-resolution-and-inference.md), [§11 Properties](../../spec/11-properties.md) | Four acquisition names, generic WriteGuard, accessor receivers and Origin contracts |
| [§12 Object calls](../../spec/12-expressions.md), [§15 Ownership](../../spec/15-ownership-and-lifetime-analysis.md) | Existing ObjectCallCompatible and Loan checks; direct using-binding restrictions without root-preservation extensions |
| [§13 Adaptation/ownership](../../spec/13-operators-and-assignment.md) | Allocation table; revise §13.5's no-resource-acquisition statement, §13.5.3's same-Semantics-only restriction, §13.5.6's failure table, and §13.5.7's obj-to-rc/arc exclusion; retire ordinary make APIs per §2.2; extend clone/Weak and cyclic builders |
| [§14 Control](../../spec/14-control-flow.md), [§16 Destruction](../../spec/16-scope-exit-and-destruction.md) | using expression, Body/results, labels, §14.1 Completion and §14.5.2 exit tables, structural result validation, nested cleanup |
| [§18 Artifacts](../../spec/18-modules-and-dependencies.md) | Capability guarantees/conditions/proof dependencies and ordinary guard Types/Origins; no using-specific effect summary |
| [§21 Runtime](../../spec/21-layout-runtime-and-code-generation.md), [§22 Kimi](../../spec/22-core-execution-and-foreign-functions.md) | Management/synchronization protocols, optimization obligations, standard declaration identities and catalog changes |
| [Appendix D](../../spec/appendices/D-deferred-features.md), [Appendix F](../../spec/appendices/F-syntax-summary.md), [SPEC index](../../SPEC.md) | Deferred pattern/concurrency boundaries, using expression grammar, Semantics constraints, allocation-capable @ and removal of ordinary factory names |

**Compatibility effects:**

- Removing makeObj/makeRc/makeArc is a source-breaking API change; migrate complete-value creation to `@obj`/`@rc`/`@arc`. makeLocal/makeSync are not introduced. Existing cyclic names remain.
- Expanding owning/reference changes generic proof domains and potentially Semantics-based specialization applicability; revalidate bodies, selections, public proofs, and caches. A body that assumed object-style payload borrowing may fail. object itself is unchanged; guarded/counted are new categories.
- SharedReadResult differs by category: object yields a payload objref, guarded a handle-slot ref. Existing concrete object cases retain their result; expanded generic domains cannot assume that shape for every reference/owning Type.
- Open owner Cores need explicit or inherited TT/TS guarantees. Missing guarantees also propagate through dependent borrows and aggregates. This deliberately excludes structurally safe but undeclared open values; TT/TS are proposed capabilities, not existing verified cross-thread support.
- Relative to the earlier draft, sync acquisition uses write/tryWrite and WriteGuard<sync/T>; using is an expression with nesting only, and does not preserve the initial value through borrowed updates. Source depending on the earlier spellings, concatenation, or stronger guarantee must change.

Update Type tests, casts, member adaptation, and acquisition tables consistently so no guarded access bypass appears. This file remains a proposal; current SPEC/STATUS must not report these changes as integrated or implemented before their separate integration and verification.

### 10.2. Required verification

| Area | Representative obligations |
| --- | --- |
| Names and generics | Ordinary local/sync/async/using names, contextual categories, async rejection, full-domain generic revalidation and specialization selection |
| Adaptation | Complete operation/allocation table, creation versus transfer versus Move, factory removal, conflicting Loans, identity/address/dependency preservation, no combined creation/upcast |
| Views | Valid owning/object-borrow upcasts; reject ref/uniq handle-slot covariance, direct guarded payload access, and incomplete payload projections |
| Acquisition | local read/write conflicts, sync exclusive read use, nonblocking try results, overflow, no hidden clone, generic guarded write, fixed accessor permissions |
| Lifetimes | Slot/temporary dependencies, child Loans through return/storage/capture, legal borrowed replacement and rejection while children survive |
| using syntax/results | Expression-position recognition, labels, required Body, nested initializers, no concatenation, single-item versus block results, Unit discard, Never and outward transfers |
| using restrictions | Direct Move/assignment/destruction rejected; Copy, permitted Partial Move, borrowed replace/exchange/swap and helpers use ordinary rules; cleanup of current contents |
| Scope Exit | Bare exit transparency, valued named exit, return/continue/yield, result secured before reverse cleanup, no guard-dependent result escape, initializer failure, Abort/divergence |
| Capabilities | Sealed derivation, undeclared open rejection, inherited/conditional obligations, capability versus target-role proof, recursive rejection propagation, both shared and exclusive child transfer |
| Cyclic construction | Four modes, Owned payload and sync TT, builder invocation/cleanup ordering, None during Building, no partial-object guards, single publication, escaped Weak and final release |
| Runtime | Guard-live downgrade, one authoritative state, stable waiting key, registration/unlock/migration races, count publication ordering, no stale updates/wakeups after address reuse |
| Optimization | Preserve Abort/try/overflow/reentrancy behavior and release timing; optional warning does not alter acceptance; allocation goals measured separately from guarantees; alias attributes and artifact revalidation |

Diagnostics identify the relevant operation, binding, Loan, call path, or missing proof. These are verification requirements, not test results.

### 10.3. Review decisions

The numbers correspond to the review requests; requirements live in the sections above.

| Request | Decision and rationale |
| --- | --- |
| 1. Allocation-capable @ | Adopted: one complete-value creation/transfer syntax, with an allocation column (§2) |
| 2. using protection | Adopted: direct restrictions only; ordinary borrowed updates avoid new interprocedural preservation checking (§4.4) |
| 3. Unified TT/TS | Adopted with target eligibility retained. TT alone cannot establish that a generic complete Type is an object Core; runtime Contract Views remain deferred (§5.2) |
| 4. External waiting table | Partially adopted as the preferred implementation candidate. A stable key helps, but two flags do not prove count/migration/queue ordering or a total one-allocation bound (§7.2–3) |
| 5. using expression | Adopted using existing do result machinery. Rejected an implicit last-expression result for indented bodies because current do discards those expressions (§4.2–3) |
| 6. View-change scope | Adopted: handle-slot upcasts remain forbidden (§2.3) |
| 7. Cyclic builders | Adopted two additional concrete factory names with the existing Building/Owned protocol. A generic makeCyclic API adds no needed behavior and is not introduced (§6.2) |
| 8. Common exclusive API | Adopted: write/tryWrite and one WriteGuard parameterized by a complete handle Type; read remains local-only (§3) |
| 9. Child-borrow transfer | Adopted the ordinary capability and Loan rules for all child-borrow forms (§5.4) |
| 10. Header concatenation | Removed. Restricting concatenation by blank lines/comments would still add a layout exception; ordinary nesting is sufficient (§4.1–2) |
| 11. local optimization | Adopted as an optional as-if optimization and warning, not another mandatory analysis or acceptance rule (§7.1) |
| 12. Compatibility | Added explicit source/proof-domain changes and integration targets (§10.1) |
| 13. Pattern convenience | Deferred for Appendix D.1 integration; no current syntax or match acquisition change (§9.1) |
