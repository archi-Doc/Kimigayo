# 16. Scope exit and destruction

[Specification index](../SPEC.md)

## 16.1. Deferred blocks

A **Deferred Block** (`defer`) registers cleanup when execution reaches it. Registration evaluates nothing in the body. The body is a common Body ([§14.2](14-control-flow.md#142-bodies-and-evaluation-contexts)) with no result. A Deferred Block never suspends ([§24.2.2](24-suspension-and-asynchronous-tasks.md#2422-passing)).

A registration belongs to its directly containing executable body scope: a function, branch, arm, current iteration, `do`, `unsafe`, `require` failure body or executing `defer` body. It runs at that scope's Scope Exit ([§16.2](#162-scope-exit)). Unreached registrations do not run, each iteration registers independently, and registrations cannot be cancelled or invoked manually.

```kimi
func process(flag: bool)
    defer => log("function end")
    if flag
        defer => log("branch end")
        work()
    log("after branch")
// For flag = true: "branch end", "after branch", "function end".
```

### 16.1.1. Deferred control boundary

No outward transfer may cross a Deferred Block ([§14.5.2](14-control-flow.md#1452-targets-and-barriers)). Inside it:

- An `exit` that targets the Deferred Block itself, with no operand or one that fits Unit, ends the body, also from nested selections, `do`, `unsafe` and `require` failure bodies.
- Transfers to targets inside the body keep their meaning: an inner loop receives its `exit` and `continue`, and inner selections and `do` expressions keep their results.
- Any other transfer to a target outside the body is an error, also in unreachable code ([§14.5.2](14-control-flow.md#1452-targets-and-barriers)); a nested function keeps its own Function Boundary.

An early `exit` finishes the body's own cleanup and resumes the pending outer Scope Exit, without cancelling registrations or replacing a pending result or transfer.

```kimi
func run() -> i32
    defer
        defer => log("inner end")
        for value in values()
            if done(value)
                exit       // Targets the inner for.
        if alreadyClosed()
            exit ()        // Ends this Deferred Block; "inner end" still runs.
        close()
    return 0
```

### 16.1.2. Deferred evaluation and ownership

Names and Effective Types are resolved at the registration position. Registration neither Copies, Moves nor borrows locals and creates no closure; the body uses the bindings as they are when it runs, and all ownership and Loan rules are checked there on every exit path. A Move during cleanup removes the later automatic destruction of its source.

An inner `defer` registers while its enclosing Deferred Block runs and runs when that body exits, before the remaining outer cleanup. It cannot register for a scope that is already being exited.

A Deferred Block grants no unsafe permission: permission follows the operation's lexical context ([§14.3](14-control-flow.md#143-do-and-unsafe)), and its safety conditions must hold when the body runs. `defer` neither prevents double destruction nor extends raw pointer validity.

```kimi
var count: i32 = 1
let saved = count
defer => log(saved)          // Prints 1, second.
defer => log(count)          // Prints 2, first.
count = 2
```

```kimi
let resource = makeResource()
defer => inspect(resource@ref)
consume(resource@move)       // Error: the cleanup reads a Moved value.
```

## 16.2. Scope exit

**Scope Exit** runs the registered Deferred Blocks and the automatic destruction of a scope. It happens when the scope completes and when `return`, `exit`, `continue`, `yield` or `try` propagation leaves it. The rules of each construct give the owning scope of the values it creates.

### 16.2.1. Cleanup order

Departing scopes are cleaned up from inner to outer. Within a scope, declarations and `defer` statements are processed in reverse lexical order; later initialization or reassignment does not move a binding. Only registered blocks run, and only Initialized values whose destruction responsibility remains with the scope are destroyed. A last use does not end responsibility.

Bindings introduced at scope entry (parameters, `self`, iteration and Pattern bindings) precede the body's statements, in written order; each construct places its implicit bindings.

```kimi
func process(first: Resource, second: Resource)
    let a = makeResource("a")
    defer => log("A")
    let b = makeResource("b")
    defer => log("B")
// Cleanup: "B", b, "A", a, second, first.
```

**Interrupted expressions.** When a transfer leaves an expression before it completes, each live temporary, each placed component of an unfinished Tuple, array or enum Case construction and each inserted entry of an unfinished Dictionary literal ([§12.4.3](12-expressions.md#1243-dictionary-literals)) belongs to the innermost enclosing body scope at the point where it was acquired. It is cleaned up with that scope in reverse acquisition order, so it is destroyed before that scope's earlier declarations and Deferred Blocks. This includes match guard temporaries and match Subjects. Transferring a value's responsibility removes it from this order.

```kimi
func f() -> Result<i32, ParseError>
    defer => log("defer")
    consume(makeGuard(), try parse()) // On Err: the Guard temporary is destroyed, then "defer" runs.
    return .Ok(0)
```

### 16.2.2. Results and transfers

For a transfer result, or a single-item expression used as its owner's result:

1. Evaluate the operand. If it triggers another transfer, that transfer is processed instead.
2. Secure the result by the Copy and Move rules.
3. Destroy the temporaries whose lifetime ends with that result expression.
4. Run Scope Exit for the departing scopes ([§16.2.1](#1621-cleanup-order)).
5. Deliver the secured result and complete the target's termination or continuation.

Other live temporaries are cleaned up under [§16.2.1](#1621-cleanup-order) (Interrupted expressions). An omitted operand supplies Unit ([§14.5.1](14-control-flow.md#1451-syntax-and-operands)). A Moved result is not destroyed at its source; a Copied one leaves the source's responsibility intact. Cleanup cannot replace the secured result. Borrows in the result must stay valid after all cleanup, and destruction lifetime checking ([§15.6.6](15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking)) applies throughout.

Only scopes actually left are cleaned up: `continue` cleans the current iteration's scopes, and a named transfer cleans every scope it leaves.

```kimi
func answer() -> i32
    var value: i32 = 1
    defer => value = 2
    return value                // Returns the already Copied 1.
```

### 16.2.3. Completion and abnormal termination

A registration is consumed when its body starts and runs at most once. A pending result or transfer is delivered only after all cleanup completes. Cleanup neither suspends nor observes cancellation ([§24.2.2](24-suspension-and-asynchronous-tasks.md#2422-passing), [§22.3.3](22-core-execution-and-foreign-functions.md#2233-cancellation-and-waits)).

Abort, also inside a `drop`, performs no further cleanup and rolls nothing back ([§17.4](17-failure-handling.md#174-abort-termination)). Forced process termination, undefined behavior and an unbroken reference-count cycle ([§13.6.3](13-operators-and-assignment.md#1363-weak-references)) give no cleanup guarantee.

## 16.3. Destruction declarations

A `drop` declaration defines the destruction work of one struct layer.

- It appears only directly in a struct body, including a generated fragment; each struct has at most one after conditional selection and merging.
- It has no parameters, generics, Origins, result annotation or modifiers ([§F.3](appendices/F-syntax-summary.md#f3-keywords)). Its body is a common Body: a single-item expression is discarded, an explicit `return` fits Unit, and unsafe operations need an inner Unsafe Block.
- Only destruction invokes it. Calling it, directly or indirectly, or taking it as a function value is an error.
- Any `drop`, including `drop => ()`, makes the struct non-Copy ([§3.5.2](03-types-and-values.md#352-copy-capability)) and requires every path that Partially Moves the value to restore completeness before whole-value use or destruction ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)). A whole-value Move is allowed.
- Access modifiers never suppress destruction.

```kimi
struct Marker
    Self is Copy      // Error: a struct with a drop cannot be Copy.
    drop => ()
```

### 16.3.1. Destruction receiver

In `drop`, `self` has exclusive access equivalent to `uniq/Self` for access and Loan checks, like the construction receiver ([§6.3.4](06-declarations-and-containers.md#634-the-construction-receiver)). It is a destruction receiver, not an ordinary borrow value or an owner, and cannot be Copied or Moved.

- The whole receiver cannot be an assignment, exchange or swap target, or be passed as `uniq/Self`, including to methods and setters.
- Initialized fields follow ordinary permissions, and no field can be Moved out: the receiver is not an owned struct Place ([§11.2.2](11-properties.md#1122-field-paths-and-partial-moves)), and a referent reached through exclusive access is never left Moved ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)). To take a field's value, use `Kimi.Intrinsics.exchange` or `swap` ([§15.7](15-ownership-and-lifetime-analysis.md#157-whole-value-updates)) or a Type-specific operation.
- `self@ref` is an ordinary `ref/Self` whose Origin ends within the `drop` body. It may be passed to helpers but cannot be captured, stored or returned.
- The destruction receiver cannot become an `obj`, `rc` or `arc` handle or an object view (`objref`, `objuniq`), be stored for later use, or escape.

During the destruction of layer `D`, `Self` is `D`; derived layers already cleaned up are unavailable.

```kimi
struct Connection
    var buffer: Buffer
    drop
        // self.reset()                   // Error: reset takes uniq/Self.
        // let moved = self.buffer@move   // Error: a field of drop's self cannot be Moved out.
        let whole = self@ref
        Audit.inspect(whole)              // Allowed: a helper borrow.
        // let later = func [whole] () => Audit.inspect(whole) // Error: captures the destruction receiver.
        Audit.observe(self.buffer@ref)    // Shared borrow of an initialized field.
        let old = Kimi.Intrinsics.exchange(self.buffer@uniq, with: Buffer.init())
        Audit.release(old@move)
```

### 16.3.2. Component destruction

A complete struct layer is destroyed by running its `drop` and that body's Scope Exit, then destroying its own Fields in reverse logical declaration order ([§6.2.1](06-declarations-and-containers.md#621-logical-declaration-order)), then destroying its direct base the same way. All Fields and the base are Initialized when `drop` starts, and a `return` in `drop` does not skip them. Computed Properties are not components, and cleanup calls no accessors.

| Layer state at destruction | Own `drop` | Fields |
| --- | --- | --- |
| Complete | Runs if declared | All, afterward |
| Partial Move, no own `drop` | None | Remaining Initialized Fields, in reverse order |
| Partial Move, own `drop` | Error: completeness must be restored first ([§15.1.3](15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move)) | — |

The base follows the own Fields and uses its own state. Field assignment order and later Replacement do not change the order.

- Tuple and array elements are destroyed in decreasing index order, each completely before the preceding one.
- An enum destroys only the remaining payloads of its active Case, in reverse declaration order. Payloads transferred by owned decomposition ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)) are skipped.
- Destroying a Slice or UniqSlice never destroys its elements ([§4.8](04-arrays-indexing-and-slices.md#48-slices-and-views)). Destroying a Scalar fixed array reads no elements, even with `noinit` ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)).
- Library containers define the destruction order of their elements in their contracts.

```kimi
struct Triple
    var a: Resource
    var b: Resource
    var c: Resource

func split(t: Triple)
    consume(t.b@move)   // Partial Move; Triple has no drop.
// Scope exit destroys t.c, then t.a.
```

### 16.3.3. Handles and object release

`owner/T` is destroyed as exactly `T`. Destroying `obj/T` destroys the object and then releases its storage. Destroying `rc/T` or `arc/T` releases one strong reference; the release that reaches zero destroys the object and releases its storage. An object is always destroyed as its actual Type, with the complete derived-to-base cleanup, also through a base view.

**Handle destruction and Loans.** Destroying a handle destroys only the handle Place, which is compared with live Loans ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)); storage owned through the object is never its target, because every borrow into it holds a Loan on a handle through which it was reached ([§13.5.5](13-operators-and-assignment.md#1355-follow)). Any `rc` or `arc` release may be final, so it observes what destroying the payload observes ([§15.6.6](15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking)) and contributes that cleanup's effects on other storage, such as static Fields, to the enclosing summary ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls), [§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)). A callee that destroys an owned handle parameter contributes the same effects.

**Erased destruction.** Destroying a value whose Dynamic Type or environment Type is not known statically has unknown effects. They are never taken from the View Target's own `drop` alone or from the derived Types the program declares. This covers an `obj`, `rc` or `arc` handle whose View Target is not Sealed (an open base or a runtime Contract View, such as `rc/Base` or `obj/Shape`) and the environment of a common Function Type value, also as components of the destroyed value. Such a destruction conflicts with every live Loan of a mutable static Field, as an unknown call does ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)), and is an unclassifiable effect under `confined` ([§8.11.2](08-generics-constraints-and-contracts.md#8112-confined)). A handle with a Sealed View Target, and an `owner/T`, use that Type's destruction summary. In a generic body, destroying a value of a Type parameter or associated-Type projection not proven Copy is, for the body's own Loan checks, a destruction that may observe a Loan ([§15.6.6](15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking)) with unknown environment effects; the body's summary publishes it as a parameter-dependent effect that callers compose with the actual Type ([§4.9.6](04-arrays-indexing-and-slices.md#496-loans-dependencies-and-call-effects)), unless [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls) widens it.

```kimi
var action: () -> () = func [resource@move] () => ()
let view = State.text@ref          // State.text is a mutable static Field.
action = func [] () => ()          // Error: destroying the old environment has unknown effects.
inspect(view)
```

**Payload replacement.** Whole payload replacement ([§15.7.3](15-ownership-and-lifetime-analysis.md#1573-storage-update-dependencies)) destroys the old contents in place, keeping the object's storage, counts and lifecycle state; this leaves the storage Uninitialized without resetting `let` initialization history. It then installs the replacement without running constructors, initializers or setters. A payload exchange instead transfers the old contents to its result. Dependencies on old contents end even at the same address.

Destroying a borrow or raw pointer never destroys its referent, and Scalars have no destruction work.

## 16.4. Closure environments and objects under destruction

**Closure environments.** Remaining captures are destroyed in reverse initialization order. In a Consuming call the environment binding precedes the parameters, so its captures are destroyed last, before the result is delivered; Shared and Exclusive calls do not destroy it. Capture destruction observes Origins like any component, even for zero-sized captures.

```kimi
let first = makeResource("first")
let second = makeResource("second")
let job = func [first@move, second@move] () => ()
// Destroying job destroys second, then first.
```

**Objects under destruction.** Runtime dispatch, Type tests and checked casts need a handle or object view, which the destruction receiver cannot become ([§16.3.1](#1631-destruction-receiver)), so none of them applies to the object being destroyed. `upgrade` of a Weak to that object returns `None` ([§13.6.3](13-operators-and-assignment.md#1363-weak-references)). Destruction holds an exclusive Loan on the destroyed storage until it completes, as a call holds its `uniq` receiver ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)); any other access to that storage, including through a static Field, is a Loan conflict. No further check applies to calls in `drop`: a foreign function or a Function value is checked only by the ordinary Origin, Loan and static-effect rules. Violating these restrictions through a raw pointer is undefined behavior ([§5](05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory)).

```kimi
struct File
    var handle: isize
    drop
        Native.close(self.handle)   // Allowed: a foreign call checked by the ordinary rules.
```
