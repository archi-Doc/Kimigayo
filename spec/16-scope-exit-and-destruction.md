# 16. Scope exit and destruction

[Specification index](../SPEC.md)

Scope exit secures results and performs cleanup. A Deferred Block registers code for scope exit.

## 16.1. Deferred blocks

A **Deferred Block** registers cleanup when execution reaches `defer`. Registration evaluates none of its body, arguments, conditions, or initializers. It uses the common Body forms and has no expression result; body expression use/discard follows §14.2.

A registration belongs to its directly containing executable body scope, including a function, branch, arm, current iteration, do, unsafe, require failure, or executing defer body. Unreached registrations do not run; each iteration registers and cleans up independently. Registrations cannot be cancelled or manually invoked.

**Basic example.**

```kimi
func process(flag: bool)
    defer => log("function end")
    if flag
        defer => log("branch end")
        work()
    log("after branch")
```

For true `flag`, output is `branch end`, `after branch`, then `function end`. Deferred execution and automatic destruction share the [Scope Exit ordering](#162-scope-exit-destruction).

### 16.1.1. Deferred control boundary

Each Deferred Block establishes a lookup barrier that no outward transfer may cross. It accepts self-targeted exit with an omitted or Unit-fitting operand, including through nested selections, do expressions, unsafe statements, and require failure bodies. An omitted operand means `()`.

An unlabeled `exit` targets the nearest Iteration Construct or Deferred Block. Consequently, exits and continues of an inner loop retain their normal meaning, as do results of inner selections and do expressions. A `return` to an outer function, a named transfer to an outer construct, or a `yield` to an outer selection is an error. A separate nested function retains its own Function Boundary and normal returns.

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

Early `exit` finishes the current body's cleanup and then resumes the pending outer Scope Exit; it neither cancels other registrations nor replaces a pending return value or transfer. Nested Deferred Blocks cannot transfer across their own boundary to a target in an outer Deferred Block. These restrictions apply even in unreachable code.

```kimi
defer
    exit () // Valid: the operand fits the deferred target's Unit result.
```

### 16.1.2. Deferred evaluation and ownership

Resolve Names and Effective Types at the registration's lexical position; later declarations or refinement do not reinterpret the body. Registration neither Copies nor Moves referenced locals and creates no closure value. Execution accesses the then-current bindings.

```kimi
var count: i32 = 1
let saved = count
defer => log(saved) // Explicit snapshot: prints 1.
defer => log(count) // Reads at execution: prints 2 first.
count = 2
```

Check initialization, Copy/Move, Loan, Origin, and destruction responsibility on every applicable exit path, including deferred uses in borrow lifetimes. Registration alone does not borrow all referenced values. Later operations are allowed if they remain compatible with the eventual cleanup.

```kimi
// Resource is non-Copy; inspect borrows, consume moves.
let resource = makeResource()
defer => inspect(resource)
consume(resource) // Error: cleanup would access a moved value.

let other = makeResource()
defer => inspect(other)
defer => consume(other) // Error: executes before inspect and moves its input.
```

A valid Move during cleanup removes subsequent automatic destruction responsibility. Raw pointer operations retain their programmer-managed obligations; `defer` does not repair double destruction or extend raw pointer validity. Secured result borrows must remain valid after all cleanup.

### 16.1.3. Nested and unsafe cleanup

An inner `defer` registers while its enclosing Deferred Block executes and runs when that body exits, before the original scope's remaining cleanup. It cannot add a registration to an outer scope already being exited.

```kimi
defer
    defer => log("inner end")
    log("outer body") // Prints before inner end.

defer => unsafe => releaseRaw(pointer) // Runs at the directly containing scope's exit.
unsafe
    defer => releaseRaw(other)      // Runs at this Unsafe Block's exit.
    useRaw(other)
```

A Deferred Block does not itself grant unsafe permission. Permission follows the operation's lexical context, never its later caller, and does not cross Function Boundaries. Safety conditions must hold when the delayed operation executes.

A defer immediately followed by scope exit is valid; this alone requires no warning or automatic replacement with a call. In `if shouldClose => defer => close(resource)`, cleanup runs at the end of the if body. To register for an outer scope, put defer there and test the condition inside it; save the condition first if registration-time truth is intended.

~~~kimi
let closeAtEnd = shouldClose
defer
    if closeAtEnd => close(resource)
use(resource)
~~~

## 16.2. Scope-exit destruction

**Scope Exit** combines registered Deferred Blocks and automatic destruction. It applies both to ordinary scope completion and to scopes left by `return`, `exit`, `continue`, or `yield`.

Ownership, temporary-lifetime, and construct-lifetime rules determine each value's owning scope and destruction point. These rules also govern temporaries, `for` iterables and iterators, iteration bindings, `match` subjects, and owned function parameters. A transfer uses those scopes to determine what it leaves.

### 16.2.1. Cleanup order

Clean departing scopes from inner to outer, finishing each before the next. Within a scope, process local declarations and `defer` statements in reverse combined lexical order. Later initialization or reassignment does not change a binding's position.

Execute only registered defers and destroy only initialized values whose destruction responsibility remains with the scope. Skip moved or destroyed values. Last use does not remove destruction responsibility.

```kimi
let first = makeResource("first")
defer => log("A")
let second = makeResource("second")
defer => log("B")
```

Cleanup order is `B`, destruction of `second`, `A`, destruction of `first`. Deferred Blocks therefore run in reverse registration order.

Bindings introduced at scope entry, such as parameters, `self`, iteration bindings, and pattern bindings, precede the body's statements. Explicit bindings in one parameter list or pattern are ordered left to right and destroyed in reverse order. Explicit `self` follows its written position; individual construct specifications place implicit bindings. These positions do not confer ownership or change the bindings' owning scopes.

```kimi
func process(first: Resource, second: Resource)
    defer => log("end")
```

If both parameters retain owned values, cleanup is `end`, destruction of `second`, then destruction of `first`. Borrowed bindings do not cause destruction of their pointees. Similarly, a defer inspecting an iteration binding runs before that binding's remaining owned value is destroyed.

### 16.2.2. Results and transfers

For a transfer result or a single-item expression used as its owner's result:

1. Evaluate the operand.
2. Secure the result using normal Copy / Move rules.
3. Destroy temporaries whose normal lifetime ends at completion of that result expression.
4. Run Deferred Blocks and automatic destruction in departing scopes, in the common cleanup order.
5. Deliver the secured result and complete the target's termination or continuation.

Other temporaries follow normal lifetime scopes and positions; absence from the result does not justify earlier destruction. Omitted return/exit/yield operands supply Unit; continue has no result. If an operand triggers another transfer, process that transfer instead. A target result discarded after delivery follows normal destruction; directly discarded body expressions keep their ordinary temporary lifetime.

A moved result is not destroyed again at its source; a copied result leaves the source's destruction responsibility intact. Deferred execution cannot replace the secured result, although ordinary effects on shared objects remain possible.

```kimi
func answer() -> i32
    var value: i32 = 1
    defer => value = 2
    return value // Returns the already copied 1.

func take() -> Resource
    let resource = makeResource()
    defer => inspect(resource) // inspect borrows; Resource is non-Copy.
    return resource // Error: cleanup would access the moved source.
```

Clean only scopes actually left. `continue` cleans the current iteration's departing scopes and retains outer scopes needed for continuation. Named transfers clean all intervening scopes they leave. Exiting a Deferred Block completes its nested cleanup, then resumes pending outer cleanup.

Normal ownership, borrowing, and [Destruction lifetime checking](15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking) apply throughout cleanup. Securing a result first does not permit a borrow of a destroyed local to escape. For partial initialization or Partial Move, apply [field cleanup](#1632-field-cleanup) to parts with remaining responsibility rather than skipping the whole aggregate. Raw pointer access does not guarantee automatic tracking of the original owner's destruction responsibility.

### 16.2.3. Completion and abnormal termination

Consume a Deferred Block's registration when its execution starts. Each registration executes once if cleanup reaches it; an inner defer registers in the executing body's own scope, never in an outer scope already being exited.

Deliver a pending transfer or result only after all required cleanup completes normally. Nonterminating cleanup prevents remaining cleanup and delivery; general termination proofs are not required.

Forced process termination and undefined behavior provide no cleanup guarantee. Abort skips or aborts cleanup under [Abort Termination](17-failure-handling.md#173-abort-termination). Cancellation, if introduced, requires separate common rules for Deferred Blocks, destruction, and secured results; this specification provides no cleanup guarantee for it.

## 16.3. Aggregate destruction and deinit

`deinit` may be declared only directly in a structure body, including a fragment produced by a Mod. It is invalid in a group, enum, contract, extension, constructor, function, accessor, or another `deinit`. After conditional selection and merging, each concrete structure has at most one such declaration. A common Body (§14.2) is required; `deinit => ()` or an indented `()` is an explicit no-op body. A no-op body still counts as user-defined `deinit` for Copy and Partial Move restrictions.

`deinit` accepts no parameters, generics, Origins, result annotation, or modifiers. Unavailable modifiers follow §2.5.1. Its single-item expression is discarded; explicit return must fit Unit. Unsafe operations need an inner Unsafe Block. Automatic component cleanup applies without deinit. Each derived layer may define its own body; machinery processes each layer separately. Legal owners need no private access to invoke mandatory destruction, and access modifiers cannot suppress it.

### 16.3.1. Special receiver

`deinit` is a dedicated destruction declaration with no parameters or explicit result Type. Only Destruction machinery invokes it. Explicit calls, indirect calls, and obtaining its function value are compile-time errors; user-callable finishing work requires a separate API.

Its `self` has exclusive access equivalent to `uniq/Self` for access/Loan checks, but is a special Destruction receiver, not an ordinary borrow value or a second owner. It cannot be Copied or Moved.

The whole receiver cannot be an assignment, Exchange, or Swap target, or be passed as ordinary `uniq/Self`. This includes methods and setters taking the whole receiver exclusively. Whole-self shared borrowing and direct operations/borrows on initialized fields remain subject to ordinary permissions and Partial Move restrictions. No method or borrow may bypass these rules.

A Destruction receiver cannot become an owning/counting handle, be stored for later use, or escape. Helper borrows must end before their storage is destroyed. During layer D’s destruction, Self is D; cleaned derived layers are unavailable. Prove nonescape and compliance with the [object lifetime restrictions](#164-closure-and-object-lifetime-boundaries) from ownership/call effects; reject unknown callees that could violate them. Runtime counts and unchecked assertions cannot replace the proof.

```text
During deinit (conceptual storage operations):
    Kimi.exchange(self, replacement)       // Error: whole-self replacement.
    self.reset()                     // Error if reset requires uniq/Self.
    observe(sharedBorrow(field))     // Allowed for an initialized field.
    Kimi.exchange(field, with: newValue)   // Allowed with authorized field access.
```

### 16.3.2. Field cleanup

Destroy a complete struct layer by running its own deinit, completing that body’s Scope Exit, then destroying own Fields in reverse **logical** declaration order and finally the direct base recursively. Fields/base are complete and Initialized when deinit starts. Normal return does not skip them. Computed Properties add no components; cleanup invokes no accessors. Splitting, generated declarations, and physical layout cannot change this order except through defined logical ordering.

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

An incomplete structure layer does not run its own `deinit`; destroy its remaining initialized own fields in the same reverse order, then its base subobject if any part of it still carries responsibility. Apply completion and completeness checks separately to each component: a complete field or completed base runs its own `deinit`, even if its containing derived layer never completed construction. A partly constructed base recursively cleans its initialized components without running that unfinished base layer's body. Skip Uninitialized and Moved parts. Field assignment order and subsequent Replacement do not reorder this cleanup.

Tuple elements and array elements are destroyed in decreasing element-index order, from the last logical element to index zero. This includes fixed-length arrays, initialized elements of a partly built array, and owning array storage used by array literals; spare capacity is not an initialized element. Recurse into a partially initialized element before continuing to the preceding element. Unit and empty arrays have no components to destroy.

Enum values destroy only the active Case's remaining initialized payloads in reverse declaration order. Inactive Cases and payloads moved out by [selected match acquisition](15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime) have no remaining responsibility. Borrow payloads never destroy their referents. Other library containers must define the destruction order of their owned elements in their own contracts.

| Aggregate state | Own deinit | Field destruction |
| --- | --- | --- |
| Complete | Run if declared | All fields afterward |
| Construction not completed | Never run | Initialized fields only |
| Incomplete after Partial Move | Never run | Remaining fields only |

The table applies per structure layer; any base cleanup follows own-field cleanup and uses the base's independent state. For example, a complete `Derived : Base` is destroyed as `Derived.deinit`, Derived's fields in reverse order, `Base.deinit`, Base's fields in reverse order, recursively. If Derived construction is incomplete but Base completed, omit `Derived.deinit` while retaining the remaining Derived-field cleanup and the complete Base cleanup.

Partial Move is forbidden if the aggregate made incomplete or an inline containing ancestor has user-defined `deinit`. The last row does not authorize bypassing that restriction.

```text
Declaration order: a, b, c
States: a = Initialized, b = Moved, c = Initialized
Cleanup: c, then a
```

Destruction lifetime checking applies at every actual observation, including field cleanup. If cleanup reaches a responsibility, execute it exactly once; Move transfers it and prevents double destruction at the source. This guarantee does not promise that every destruction completes when an earlier cleanup diverges, Aborts, or terminates execution. [Abort Termination](17-failure-handling.md#173-abort-termination) remains the sole abnormal-termination policy.

### 16.3.3. Ownership, object release, and reentry

For whole payload replacement, run content destruction in its original storage without final release of the enclosing object (§15.7.3). Keep its allocation, header, mode, counts, and lifecycle state. Nested handles release normally. Install the preconstructed replacement only after destruction completes; do not rerun constructors, initializers, or setters. The special destruction receiver and reentry restrictions below still apply. Final object release later destroys the current payload exactly once.

Destruction claims the target’s remaining responsibility. Until completion, only its special receiver and authorized field operations may observe live parts; it is neither an ordinary owner nor an empty replacement destination. Reject reentrant destruction, whole-value use, and callback replacement. Normal completion removes the responsibility and leaves surviving storage Uninitialized; its owning operation may then install the secured replacement. This internal transition provides no source destroy/reset operation and does not reset let initialization history.

Destroy owner/T as exactly T. Destroy obj/T’s object, then release its original storage if required. Destroying rc/T or arc/T releases one strong reference; exactly the zero-count release performs object destruction and storage release, including under atomic arc ownership.

Use the actual owned Type’s complete derived-to-base cleanup, including automatic fields/base and user deinit. Base views retain that dynamic identity and the single destruction responsibility. Release original storage by its original mechanism, never an adjusted view pointer with a base size. No delayed GC finalizer or finalizer thread is implied.

Destroying a non-owning borrow or raw pointer ends that value's capability/lifetime and never destroys its referent. Scalar and other trivial Copy values have no user destruction work; copying them does not create a resource-release obligation. Automatic cleanup, replacement, abandoned construction, temporary expiration, and final object release all use the same recursive rules. If a destructor or component cleanup Aborts or diverges, remaining components, base layers, pending replacement, and allocation release do not run; there is no rollback or second cleanup attempt.

## 16.4. Closure and object lifetime boundaries

Complete payload exchange transfers the old contents and their destruction responsibility to its result. Content lifetime may change while allocation lifetime continues; neither an identical address nor placement restores dependencies on old contents (§15.7.3).

Destroy initialized captures with remaining responsibility in reverse environment-initialization order. Consumed captures are not destroyed twice. In a Consuming call, the implicit environment binding precedes explicit parameters, so its remaining captures are cleaned last, after body locals/defer and parameters, before result delivery. Shared/Exclusive calls do not own the environment's destruction. Destroying a captured reference does not destroy its referent. Capture-construction failure follows ordinary temporary, partial-initialization, cleanup and Abort rules without rollback of completed Moves or effects. Retain dependencies observed by captured destructors, including zero-sized captures.

Construction completes base layers before derived fields under constructor rules. Until the complete object is initialized, do not form/publish its ordinary object views or perform runtime dispatch, type tests, or checked casts on it. Having metadata is not proof of completion. Failure cleans only initialized components with remaining responsibility, including completed base layers; do not call `deinit` for an incomplete layer, and do not promise cleanup on Abort.

During destruction, prohibit new ordinary views, runtime dispatch, type tests, checked casts, and resurrection of that object, including through helper calls. An operation remains semantically runtime dispatch even if optimization resolves it to a direct call. Do not acquire a new owning handle or increment its count to revive it. The special destruction receiver retains its authorized direct field operations and shared value borrows, without conversion into ordinary object views. Base cleanup never dispatches back into an already destroyed derived layer. These restrictions concern the object being constructed/destroyed, not independent live objects used by that code.

The shared destruction rules do not promise eventual release on Abort, divergence, forced termination, or an unbroken reference-count cycle. Weak references follow §13.5.9 and cyclic construction follows §13.5.8; cycle collection and allocator APIs remain separate designs.
