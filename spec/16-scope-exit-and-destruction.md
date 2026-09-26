# 16. Scope exit and destruction

[Specification index](../SPEC.md)

[Scope Exit](#162-scope-exit-destruction) secures results and performs cleanup, including the code that Deferred Blocks register for it.

## 16.1. Deferred blocks

A **Deferred Block** registers cleanup when execution reaches `defer`. Registration evaluates none of its body, arguments, conditions or initializers. It uses the common Body forms and has no expression result; the use or discarding of body expressions follows §14.2.

A registration belongs to its directly containing executable body scope: a function, branch, arm, current iteration, `do`, `unsafe`, `require` failure body or executing `defer` body. Unreached registrations do not run, and each iteration registers and cleans up independently. Registrations cannot be cancelled or invoked manually.

```kimi
func process(flag: bool)
    defer => log("function end")
    if flag
        defer => log("branch end")
        work()
    log("after branch")
```

For a true `flag`, the output is `branch end`, `after branch`, then `function end`. Deferred execution and automatic destruction share the [Scope Exit ordering](#162-scope-exit-destruction).

### 16.1.1. Deferred control boundary

Each Deferred Block is a lookup barrier that no outward transfer may cross (§14.5.2). It accepts a self-targeted `exit` whose operand is omitted or fits Unit, including one inside nested selections, do expressions, `unsafe` statements and `require` failure bodies.

Transfers to targets inside the body keep their normal meaning: an inner loop receives its own `exit` and `continue`, and inner selections and do expressions keep their results. A `return` to an outer function, a named transfer to an outer construct and a `yield` to an outer selection are errors. A nested function keeps its own Function Boundary and returns (§14.5.3).

```kimi
defer
    defer => log("cleanup body end")
    if alreadyClosed()
        exit // Ends this body; its nested defer and remaining outer cleanup still run.
    close()

defer
    for value in values
        if skip(value)
            continue // Targets the inner for.
        if done(value)
            exit     // Targets the inner for, not the Deferred Block.
    let message = if failed()
        yield "failed"
    else
        yield "done"
    log(message)
```

An early `exit` finishes the current body's cleanup and then resumes the pending outer Scope Exit; it neither cancels other registrations nor replaces a pending return value or transfer. A nested Deferred Block cannot transfer across its own boundary to a target in an outer Deferred Block. These restrictions apply even in unreachable code.

```kimi
defer
    exit () // Valid: the operand fits the deferred target's Unit result.
```

### 16.1.2. Deferred evaluation and ownership

Names and Effective Types are resolved at the registration's lexical position; later declarations or refinement do not reinterpret the body. Registration neither Copies nor Moves referenced locals and creates no closure value; execution accesses the bindings as they are at that time.

```kimi
var count: i32 = 1
let saved = count
defer => log(saved) // Explicit snapshot: prints 1.
defer => log(count) // Reads at execution: prints 2 first.
count = 2
```

Initialization, Copy/Move, Loan, Origin and destruction responsibility are checked on every applicable exit path, including deferred uses within borrow lifetimes. Registration alone does not borrow all referenced values, and later operations are allowed if they remain compatible with the eventual cleanup.

```kimi
// Resource is non-Copy; inspect borrows, consume takes ownership.
let resource = makeResource()
defer => inspect(resource)
consume(resource@move) // Error: cleanup would access a moved value.

let other = makeResource()
defer => inspect(other)
defer => consume(other@move) // Error: executes before inspect and moves its input.
```

A valid Move during cleanup removes the subsequent automatic destruction responsibility. Raw pointer operations keep their programmer-managed obligations: `defer` neither repairs double destruction nor extends raw pointer validity. Secured result borrows must remain valid after all cleanup.

### 16.1.3. Nested and unsafe cleanup

An inner `defer` registers while its enclosing Deferred Block executes and runs when that body exits, before the original scope's remaining cleanup. It cannot add a registration to an outer scope that is already being exited.

```kimi
defer
    defer => log("inner end")
    log("outer body") // Prints before inner end.

defer => unsafe => releaseRaw(pointer) // Runs at the directly containing scope's exit.
unsafe
    defer => releaseRaw(other)      // Runs at this Unsafe Block's exit.
    useRaw(other)
```

A Deferred Block grants no unsafe permission itself. Permission follows the operation's lexical context, never its later caller, and does not cross Function Boundaries (§14.3.3). Safety conditions must hold when the delayed operation executes.

A `defer` immediately followed by scope exit is valid; it requires no warning and is not automatically replaced with a call. In `if shouldClose => defer => close(resource)`, cleanup runs at the end of the `if` body. To register for an outer scope, put the `defer` there and test the condition inside it, saving the condition first if its registration-time truth is intended:

~~~kimi
let closeAtEnd = shouldClose
defer
    if closeAtEnd => close(resource)
use(resource)
~~~

## 16.2. Scope-exit destruction

**Scope Exit** combines registered Deferred Blocks and automatic destruction. It applies both to ordinary scope completion and to scopes left by `return`, `exit`, `continue` or `yield`.

Ownership, temporary-lifetime and construct-lifetime rules determine each value's owning scope and destruction point. They also govern temporaries, `match` and `for` Subjects, iterators, iteration bindings and owned function parameters. A transfer uses those scopes to determine what it leaves.

### 16.2.1. Cleanup order

Departing scopes are cleaned up from inner to outer, each finishing before the next. Within a scope, local declarations and `defer` statements are processed in reverse combined lexical order; later initialization or reassignment does not change a binding's position.

Only registered defers run, and only initialized values whose destruction responsibility remains with the scope are destroyed; Moved or destroyed values are skipped. A last use does not remove destruction responsibility.

```kimi
let first = makeResource("first")
defer => log("A")
let second = makeResource("second")
defer => log("B")
```

The cleanup order is `B`, destruction of `second`, `A`, destruction of `first`. Deferred Blocks therefore run in reverse registration order.

Bindings introduced at scope entry, such as parameters, `self`, iteration bindings and Pattern bindings, precede the body's statements. Explicit bindings in one parameter list or Pattern are ordered left to right and destroyed in reverse order. An explicit `self` follows its written position, and individual construct specifications place implicit bindings. These positions neither confer ownership nor change the bindings' owning scopes.

```kimi
func process(first: Resource, second: Resource)
    defer => log("end")
```

If both parameters still own their values, the cleanup is `end`, destruction of `second`, then destruction of `first`. Similarly, a `defer` inspecting an iteration binding runs before that binding's remaining owned value is destroyed.

**Abandoned construction.** When an ordinary transfer abandons aggregate construction, cleanup of placed components and of still-live expression temporaries is interleaved in reverse order of completed placement or value acquisition, while preserving the inner-to-outer scope exit above. Transferring responsibility removes the source from that cleanup order.

### 16.2.2. Results and transfers

For a transfer result, or a single-item expression used as its owner's result:

1. Evaluate the operand.
2. Secure the result by the normal Copy/Move rules.
3. Destroy the temporaries whose normal lifetime ends at the completion of that result expression.
4. Run Deferred Blocks and automatic destruction in the departing scopes, in the common cleanup order.
5. Deliver the secured result and complete the target's termination or continuation.

Other temporaries follow their normal lifetime scopes and positions; absence from the result does not justify earlier destruction. An omitted operand supplies Unit (§14.5.1). If an operand triggers another transfer, that transfer is processed instead. A target result discarded after delivery follows normal destruction, and directly discarded body expressions keep their ordinary temporary lifetime.

A moved result is not destroyed again at its source, while a copied result leaves the source's destruction responsibility intact. Deferred execution cannot replace the secured result, although ordinary effects on shared objects remain possible.

```kimi
func answer() -> i32
    var value: i32 = 1
    defer => value = 2
    return value // Returns the already copied 1.

func take() -> Resource
    let resource = makeResource()
    defer => inspect(resource) // inspect borrows; Resource is non-Copy.
    return resource@move // Error: cleanup would access the transferred source.
```

Only scopes actually left are cleaned up. `continue` cleans the current iteration's departing scopes and keeps the outer scopes needed for continuation. Named transfers clean all intervening scopes they leave. Exiting a Deferred Block resumes the pending outer cleanup (§16.1.1).

Normal ownership, borrowing and [destruction lifetime checking](15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking) apply throughout cleanup; securing a result first does not let a borrow of a destroyed local escape. For partial initialization or a Partial Move, [field cleanup](#1632-field-cleanup) applies to the parts with remaining responsibility rather than skipping the whole aggregate. Raw pointer access gives no guarantee that the original owner's destruction responsibility is tracked automatically.

### 16.2.3. Completion and abnormal termination

A Deferred Block's registration is consumed when its execution starts, and each registration executes once if cleanup reaches it.

A pending transfer or result is delivered only after all required cleanup completes normally. Nonterminating cleanup prevents the remaining cleanup and delivery; general termination proofs are not required.

Forced process termination and undefined behavior provide no cleanup guarantee. Abort skips or abandons cleanup under [Abort Termination](17-failure-handling.md#173-abort-termination). This specification provides no cleanup guarantee for cancellation; if cancellation is ever introduced, it needs separate common rules for Deferred Blocks, destruction and secured results.

## 16.3. Aggregate destruction and deinit

`deinit` may be declared only directly in a structure body, including a fragment produced by a Mod. It is invalid in a group, enum, contract, extension, constructor, function, accessor or another `deinit`. After conditional selection and merging, each concrete structure has at most one such declaration. A common Body (§14.2) is required; `deinit => ()` or an indented `()` is an explicit no-op body, which still counts as a user-defined `deinit` for the Copy and Partial Move restrictions.

Its single-item expression is discarded, an explicit `return` must fit Unit (§14.2), and unsafe operations need an inner Unsafe Block. Automatic component cleanup applies with or without a `deinit`. Each derived layer may define its own body, and each layer is processed separately. Legal owners need no private access to invoke mandatory destruction, and access modifiers cannot suppress it.

### 16.3.1. Special receiver

`deinit` is a dedicated destruction declaration with no parameters, generics, Origins, result annotation or modifiers; unavailable modifiers follow §2.5.1. Only the destruction machinery invokes it: explicit calls, indirect calls and obtaining its function value are compile-time errors. User-callable finishing work requires a separate API.

Its `self` has exclusive access equivalent to `uniq/Self` for access and Loan checks, but it is a special destruction receiver, not an ordinary borrow value or a second owner, and cannot be Copied or Moved.

The whole receiver cannot be an assignment, exchange or swap target, or be passed as an ordinary `uniq/Self`, including to methods and setters that take the whole receiver exclusively. Whole-`self` shared borrowing, and direct operations on and borrows of initialized fields, remain subject to ordinary permissions and the Partial Move restrictions; no method or borrow may bypass these rules.

A destruction receiver cannot become an owning or counting handle, be stored for later use, or escape, and helper borrows must end before their storage is destroyed. During the destruction of layer `D`, `Self` is `D`, and already cleaned derived layers are unavailable. Non-escape and compliance with the [object lifetime restrictions](#164-closure-and-object-lifetime-boundaries) are proven from ownership and call effects, and unknown callees that could violate them are rejected. Runtime counts and unchecked assertions cannot replace the proof.

```text
During deinit (conceptual storage operations):
    Kimi.Intrinsics.exchange(self, replacement)       // Error: whole-self replacement.
    self.reset()                     // Error if reset requires uniq/Self.
    observe(sharedBorrow(field))     // Allowed for an initialized field.
    Kimi.Intrinsics.exchange(field, with: newValue)   // Allowed with authorized field access.
```

### 16.3.2. Field cleanup

A complete struct layer is destroyed by running its own `deinit`, completing that body's Scope Exit, then destroying its own Fields in reverse **logical** declaration order, and finally destroying the direct base recursively. The Fields and base are complete and Initialized when `deinit` starts, and a normal `return` does not skip them. Computed Properties add no components, and cleanup invokes no accessors. Splitting, generated declarations and physical layout cannot change this order except through the defined logical ordering.

```kimi
struct ResourcePair
    var first: Resource
    var second: Resource
    deinit
        if skipCustomWork()
            return
        inspect(self.first)
// Either normal exit destroys second, then first.
```

An incomplete structure layer does not run its own `deinit`; its remaining initialized own fields are destroyed in the same reverse order, and then its base subobject if any part of it still carries responsibility. Completion and completeness are checked separately for each component: a complete field or completed base runs its own `deinit`, even if the containing derived layer never completed construction. A partly constructed base recursively cleans its initialized components without running that unfinished base layer's body. Uninitialized and Moved parts are skipped. Field assignment order and later Replacement do not reorder this cleanup.

Tuple elements and array elements are destroyed in decreasing element-index order, from the last logical element to index zero. This covers fixed-length arrays, the initialized elements of a partly built array, and owning array storage used by array literals; spare capacity is not an initialized element. A partially initialized element is cleaned recursively before the preceding element. Unit and empty arrays have no components to destroy.

Enum values destroy only the active Case's remaining initialized payloads, in reverse declaration order. Inactive Cases, and payloads transferred by [owned decomposition](15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime), have no remaining responsibility. Other library containers must define the destruction order of their owned elements in their own contracts.

| Aggregate state at the destruction point | Own `deinit` | Field destruction |
| --- | --- | --- |
| Complete | Run if declared | All fields afterward |
| Wholly Moved | Never run | Nothing |
| Construction not completed | Never run | Initialized fields only |
| Incomplete after a Partial Move, no own `deinit` | None | Remaining fields only |
| Incomplete after a Partial Move, own `deinit` declared | Error: the path must restore completeness before this point (§15.1.3) | Not reached |

The table applies per structure layer; any base cleanup follows the own-field cleanup and uses the base's independent state. For example, a complete `Derived : Base` is destroyed as `Derived.deinit`, `Derived`'s fields in reverse order, `Base.deinit`, `Base`'s fields in reverse order, recursively. If `Derived`'s construction is incomplete but `Base` completed, `Derived.deinit` is omitted, while the remaining `Derived`-field cleanup and the complete `Base` cleanup still run. A layer that completed construction and is then partially Moved keeps its restoration obligation; incompleteness never excuses the `deinit` of an already completed layer.

```text
Declaration order: a, b, c
States: a = Initialized, b = Moved, c = Initialized
Cleanup: c, then a
```

[Destruction lifetime checking](15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking) applies at every actual observation, including field cleanup. If cleanup reaches a responsibility, it executes exactly once; a Move transfers it and prevents double destruction at the source. [Abort Termination](17-failure-handling.md#173-abort-termination) is the sole abnormal-termination policy.

### 16.3.3. Ownership, object release, and reentry

`owner/T` is destroyed as exactly `T`. For `obj/T`, the object is destroyed and then its original storage released if required. Destroying `rc/T` or `arc/T` releases one strong reference; exactly the release that reaches zero performs object destruction and storage release, including under atomic `arc` ownership.

Destruction uses the actual owned Type's complete derived-to-base cleanup, including automatic fields and bases and user `deinit`. Base views keep that dynamic identity and the single destruction responsibility. Original storage is released by its original mechanism, never through an adjusted view pointer with a base size. No delayed garbage-collection finalizer or finalizer thread is implied.

Destruction claims the target's remaining responsibility. Until it completes, only its special receiver and authorized field operations may observe live parts; the target is neither an ordinary owner nor an empty replacement destination. Reentrant destruction, whole-value use and callback replacement are rejected. Normal completion removes the responsibility and leaves surviving storage Uninitialized, after which its owning operation may install a secured replacement. This internal transition provides no source destroy/reset operation and does not reset `let` initialization history.

**Payload replacement.** For whole payload replacement (§15.7.3), content destruction runs in the original storage without a final release of the enclosing object: its allocation, header, mode, counts and lifecycle state are kept, while nested handles are released normally. The preconstructed replacement is installed only after destruction completes, without rerunning constructors, initializers or setters. The special destruction receiver and reentry restrictions still apply. A complete payload exchange instead transfers the old contents and their destruction responsibility to its result. Content lifetime may change while allocation lifetime continues; neither an identical address nor placement restores dependencies on old contents. Final object release later destroys the current payload exactly once.

Destroying a non-owning borrow or raw pointer ends that value's capability or lifetime and never destroys its referent. Scalars and other trivial Copy values have no user destruction work, and copying them creates no resource-release obligation. Automatic cleanup, replacement, abandoned construction, temporary expiration and final object release all use the same recursive rules. If a destructor or component cleanup Aborts or diverges, the remaining components, base layers, pending replacement and allocation release do not run; there is no rollback and no second cleanup attempt.

## 16.4. Closure and object lifetime boundaries

**Closure environments.** Initialized captures with remaining responsibility are destroyed in reverse environment-initialization order, and consumed captures are not destroyed twice. In a Consuming call, the implicit environment binding precedes the explicit parameters, so its remaining captures are cleaned up last, after body locals, `defer` and parameters, and before result delivery. Shared and Exclusive calls do not own the environment's destruction. A failure during capture construction follows the ordinary temporary, partial-initialization, cleanup and Abort rules, without rollback of completed Moves or effects. Dependencies observed by captured destructors are kept, including for zero-sized captures.

**Objects under construction.** Construction completes the base layers before the derived fields under the constructor rules. Until the complete object is initialized, its ordinary object views are not formed or published, and no runtime dispatch, Type test or checked cast is performed on it; having metadata is not proof of completion. A failure cleans up only initialized components with remaining responsibility, including completed base layers; `deinit` is not called for an incomplete layer, and no cleanup is promised on Abort.

**Objects under destruction.** During destruction, new ordinary views, runtime dispatch, Type tests, checked casts and resurrection of that object are prohibited, including through helper calls. An operation remains semantically a runtime dispatch even if optimization resolves it to a direct call. No new owning handle is acquired and no count is incremented to revive the object. The special destruction receiver keeps its authorized direct field operations and shared value borrows, without conversion into ordinary object views. Base cleanup never dispatches back into an already destroyed derived layer. These restrictions concern the object being constructed or destroyed, not independent live objects used by that code.

The shared destruction rules do not promise eventual release on Abort, divergence, forced termination or an unbroken reference-count cycle. Weak references follow §13.5.9 and cyclic construction follows §13.5.8; cycle collection and allocator APIs remain separate designs.
