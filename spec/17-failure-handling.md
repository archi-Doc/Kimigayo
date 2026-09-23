# 17. Failure handling

[Specification index](../SPEC.md)

Failure handling determines whether execution continues with an ordinary value or terminates. Abort interacts with cleanup only through its explicit termination rules.

## 17.1. Error policy

Kimigayo represents ordinary failures as values and uses Abort Termination only when normal execution cannot continue. It has no exception throwing or catching mechanism.

Runtime problems fall into three categories:

| Category | Meaning | Representation |
| --- | --- | --- |
| Recoverable failure | An expected failure that the caller can handle. | `Option<T>` / `Result<T, E>` |
| Unrecoverable failure | Normal execution cannot continue. | `$abort(...)` or implicit Abort |
| Warning | Processing can continue successfully, but a condition merits notice. | Diagnostic |

These categories are not a severity ranking: `None` represents expected absence, `Err` an operation failure, Abort process termination, and a warning a diagnostic independent of control flow.

The examples use the [enum construction](06-declarations-and-containers.md#632-case-construction-and-resolution) and [Pattern](14-control-flow.md#1481-patterns) rules; their APIs are illustrative.

Explicit try propagates Kimi Option/Result failure (§17.2.4). Value postfix ? and user-defined propagation are not defined. The [require statement](14-control-flow.md#1411-require-statement), including `require condition else => return`, is explicit control flow and performs no automatic Option/Result unwrapping or propagation.

## 17.2. Absence and failure as values

Use `Option` when a contract represents normal absence, and `Result` when it represents failure with a reason. The API contract determines the choice, regardless of whether an individual caller uses the reason.

### 17.2.1. Option

`Option<T>` represents a value or normal absence, without a reason for the absence.

This is the compiler-recognized Kimi `Option` declaration, whose identity is fixed in §22.1. The enum below shows its required shape; programs do not redeclare it.

```kimi
enum Option<T>
    Self is Copy when T is Copy
    Some(T)
    None

func findUser(id: UserId) -> Option<User>
```

```kimi
match findUser(id)
    .Some(let user) => use(user)
    .None => useDefault()
```

### 17.2.2. Result

`Result<T, E>` represents success or failure with a reason. `E` is an ordinary Type; no exception object hierarchy is required.

This is the compiler-recognized Kimi `Result` declaration (§22.1), and the enum below shows its required shape. The discarded-Result warning refers to that Symbol identity, including through aliases, not to every user Type spelled `Result`.

```kimi
enum Result<T, E>
    Self is Copy when T is Copy, E is Copy
    Ok(T)
    Err(E)

enum FileError
    Self is Copy
    NotFound
    PermissionDenied
    InvalidData
    IoFailure

func readFile(path: string) -> Result<Data, FileError>
```

```kimi
match readFile(path)
    .Ok(let data) => process(data)
    .Err(FileError.NotFound) => createFile(path)
    .Err(let error) => report(error)
```

Combine the Types when an API distinguishes normal absence from operation failure:

```kimi
func lookupUser(id: UserId) -> Result<Option<User>, LookupError>
```

With this expected Type, `.Ok(.Some(user))` constructs success with a value, `.Ok(.None)` normal absence, and `.Err(error)` a failed lookup.

`Result` uses ordinary [enum Copy derivation](03-types-and-values.md#352-enum-copy): its conformance requires both complete payload Types to be Copy, independently of the active Case, and these premises do not restrict Type formation. `Result<i32, i32>` is Copy, while `Result<i32, string>` is Non-Copy even when it holds `Ok`, and `Result<Data, FileError>` is Copy only if `Data` is also Copy. Borrow payloads follow their complete Semantics: shared borrows are Copy and keep their dependencies, while exclusive borrows and owning object or counting handles are not Copy. An enclosing handle keeps its own formation and acquisition rules. Generic proof follows §8.7: two Proven operands yield Proven, a valid Refuted operand refutes the conjunction, and the remaining unresolved cases are Unknown, never assumed Non-Copy. Invalid Types or evidence are Error.

### 17.2.3. Handling, propagation, and discarding

Recoverable failures are ordinary values: they neither throw exceptions nor propagate implicitly. They are handled with ordinary control flow such as `match` and propagated explicitly with `return`:

```kimi
func loadSize(path: string) -> Result<usize, FileError>
    return match readFile(path)
        .Ok(let data) => .Ok(data.count)
        .Err(let error) => .Err(error@move)
```

The return Type describes contractual absence or recoverable failure; a function returning `Result` may still Abort on an invariant violation or unrecoverable condition.

Discarding a Kimi `Result` expression in Discard Context is allowed but produces a compile-time warning, independently of Copy capability and of the runtime `Ok`/`Err` state. The value is identified by its Kimi Symbol, including through equivalent resolved paths. [Warning priority](#174-warnings) selects one report if several apply to the same discard. The warning does not change control flow; a caller can intentionally ignore the outcome with `_ = expression` (§14.2.4), or by handling both variants with `match`. There is no special warning for discarding `Option`.

Returning a recoverable failure follows the normal [Scope Exit](16-scope-exit-and-destruction.md#162-scope-exit-destruction) rules, including the requirement that earlier cleanup completes normally before the remaining cleanup and result delivery. Use this path for ordinary failures that need resource cleanup.

### 17.2.4. Try propagation

`try expression` is a right-associative prefix expression that binds below `@` and above the multiplicative operators (§13.1). Calls, selection, indexing and `@` bind more tightly. Its operand is required and need not be a call.

```kimi
try prepare() + 1     // (try prepare()) + 1
try prepare().count   // try (prepare().count)
(try prepare()).count // Select from the success payload.
try pending@move      // try (pending@move): transfer the stored Result, then extract.
(try prepare())@i64   // Extract before converting.
try try nested       // Extract twice; check each propagation separately.
```

The normalized outer operand Type must be an owned compiler-recognized Kimi Option or Result. Aliases and redundant owner prefixes normalize normally. Names or structurally similar user enums grant no support. `Option<ref/T>` is valid; `ref/Option<T>`, `uniq/Result<T,E>` and `obj/Option<T>` are not automatically dereferenced. Generic definitions require proof of the actual enum structure.

**Evaluation.** Evaluate and acquire the operand once in Value Context without an expected Type, as an owned position: Copy a Copy Place, require `@move` for a Non-Copy Place, and transfer an existing temporary without extra acquisition. Inspect its Case:

| Operand | Normal value | Failure return | Required return target |
| --- | --- | --- | --- |
| `Option<T>` | Some payload of Type T | `.None` | `Option<U>` |
| `Result<T,E>` | Ok payload of Type T | `.Err(error)` | `Result<U,F>`; error fits F by ordinary rules |

Only one layer is extracted. The normal result is a value of complete Type T, not a payload Place. Existing borrowed payloads retain permissions, Origins and Loans; try creates no borrow. T and U need not agree. Failure constructs the return target's enum, never reinterprets the original storage. Multiple error Types are not automatically combined; explicitly adapt errors when ordinary fitting cannot return them. `.Some(.None)` continues with the inner None; mixed nested Option/Result does not bypass either try's checks.

**Target and cleanup.** The semantics correspond to a match with ordinary `return .None`/`return .Err(error)` at the same position, without textual duplication or a hidden Closure. Reuse that return's lexical target and barriers; do not search an outer target after a Type mismatch. Functions, Closures and getters have their own targets. Selections, loops, do and unsafe introduce none. Outward transfers forbidden from defer, defaults or verification messages remain forbidden. Main, init, set and deinit have Unit targets; source top-level items have no return target. Separate nested functions keep their own boundaries. Check targets and Types even in unreachable code or for known Some/Ok.

Secure the successful payload or return value before ordinary temporary and Scope Exit cleanup. Transferred payloads are not destroyed twice; sources retain ordinary Moved states and are not reset to None. Failure during argument/aggregate evaluation skips remaining evaluation and cleans acquired parts in ordinary order. Abort, divergence and other transfers keep their rules; incomplete cleanup prevents pending return delivery. Returning a borrowed payload cannot let dependencies escape cleanup.

**Inference.** Resolve the operand from its receiver, arguments and explicit Type information under existing inference and overload rules. Then fit its known success Type to any expectation on the try expression and check the failure return. Neither expectation nor return target feeds back into operand inference or retries overload selection. Require explicit Type arguments or an annotated intermediate when unresolved.

```kimi
// parse<T>() -> Result<T, ParseError>; no input determines T.
let bad: i64 = try parse()   // Error: the annotation cannot infer T.
let value = try parse<i64>() // Valid in a compatible return target.
let pending: Result<i64, ParseError> = parse()
let other = try pending@move // The stored Non-Copy Result is transferred.
```

`try .None` and `try .Err(error)` lack an enum expectation and fail. An anonymous function's implicit failure returns are expectation-dependent result sources like written Case returns; they cannot invent a return Type. Obtain it from an annotation, a fixed expected signature or other independently typed sources, without source-order dependence or instantiation-time body reinterpretation.

```kimi
// getOpt() -> Option<i32>; f has no expected signature.
let f = func () => try getOpt() // Error: inferred i32 cannot accept None.
let g = func () -> Option<i32> => .Some(try getOpt())
```

A Never success payload produces no normal value but still requires failure checking. An operand itself typed Never, without resolved Option/Result structure, is invalid: `try $abort(...)` is not an exemption.

**Success results and diagnostics.** There is no automatic Some/Ok wrapping. Named/anonymous functions and getters keep single-item and indented Body rules, including omitted Unit returns.

```kimi
// save() -> Result<(), Error>.
func bad() -> Result<(), Error> => try save() // Error: () is not Result.
func wrapped() -> Result<(), Error> => .Ok(try save())
func forwarded() -> Result<(), Error> => save()
func run() -> Result<(), Error>
    try save() // Unit success needs no discard warning.
    return .Ok(())
```

For mismatched normal or failure results, diagnose the path and Types. Suggest a suitable return annotation where missing, explicit success wrapping where needed, or removing try when its operand already has the complete return Type and is itself the normal result. These are alternatives, not automatic fixes. Validate changes to inference, calls, ownership and cleanup. For `try X.m`, explain grouping and suggest `(try X).m` only as a checked candidate; do not search unlimited alternate interpretations.

There is no user-defined try support, try block, Option/Result interconversion, special implicit error conversion, automatic borrowing from borrowed enums, success wrapping, None-from-null or new Option layout guarantee. A try-prefixed API name is separate: it promises its documented result, not propagation or interception of argument Abort.

## 17.3. Abort termination

**Abort Termination** abnormally terminates the entire process executing the program when normal execution cannot continue. Explicit requests and implicit runtime check failures share the termination rules below.

### 17.3.1. Causes and API contracts

Abort is a termination mechanism, not a classification of causes. It may represent a programming defect, such as an invariant violation or an unexpected state, or an unrecoverable external condition, such as allocation failure or an unavailable required runtime resource. Classifying bugs belongs to diagnostics and introduces no different control flow.

Checked runtime operations Abort on their defined failures: integer overflow; invalid integer division or remainder; invalid indices or Range boundaries; invalid conversions or shift counts; duplicate Dictionary keys; and missing indexed keys. Each operation defines its invalid inputs; floating-point division by zero follows IEEE 754. Failed Type tests and checked object casts instead follow their Boolean, Option or Result contracts.

The same cause can be recoverable under a different API contract:

```kimi
func tryAllocate(size: usize) -> Option<Buffer>

func allocateRequired(size: usize) -> Buffer
    return match tryAllocate(size)
        .Some(let buffer) => buffer
        .None => $abort("Required memory could not be allocated")
```

`tryAllocate` returns absence when allocation is unavailable, while `allocateRequired` treats that outcome as fatal.

Failure to allocate storage required by common Function Type erasure or by [Kimi object creation](13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) follows Abort Termination, independently of Copy/Move input acquisition. Kimi strong-owner duplication also Aborts if its count cannot be incremented without overflow.

### 17.3.2. Explicit abort and argument evaluation

`$abort(expression)` evaluates its argument once, with expected Type `string`. If the argument completes normally, the string becomes diagnostic information and the process Aborts. Otherwise, the argument's transfer, divergence or nested Abort takes effect, and this Abort is not initiated. Dynamic message-construction failures follow the same rule.

A `$abort(...)` expression has Type Never and never completes normally; the ordinary [Never](03-types-and-values.md#315-unit-and-never-types) and [result validation](14-control-flow.md#149-result-validation) rules apply:

```kimi
func requireValue(value: Option<i32>) -> i32
    return match value
        .Some(let x) => x
        .None => $abort("Required value is missing")
```

Abort itself is not a control transfer and produces no Completion; it is distinct from a transfer during argument evaluation. The [evaluation outcomes](14-control-flow.md#141-completions) are Completion, divergence and Abort Termination.

### 17.3.3. Termination, diagnostics, and cleanup

Once Abort Termination begins, ordinary program execution never resumes and no Scope Exit is performed before the entire process terminates. This rule sets no wall-clock bound on argument evaluation or termination. Abort cannot be caught, recovered from or resumed, and performs no stack unwinding.

Abort does not start Scope Exit processing. If it begins during Scope Exit, that processing stops immediately: the remaining Deferred Blocks, automatic destruction, and the rest of an executing Deferred Block or `deinit` do not run. Completed cleanup effects are not rolled back. Pending `return`, `exit`, `continue` and `yield` are abandoned, secured results are not delivered, and no additional cleanup destroys them.

```kimi
func process()
    let resource = makeResource()
    defer => close(resource)
    $abort("Fatal condition")
```

Here, neither the registered `close(resource)` nor the scope-exit destruction of `resource` runs.

Abort diagnostics carry a reason and a source location: the failed operation for an implicit Abort, or the `$abort(...)` call for an explicit Abort. A duplicate Dictionary key reports the later key expression. The runtime attempts to emit the available information, but successful or complete output is not guaranteed. The initial Windows format, stderr destination and failure code 1 are defined in §22.5.4. A failure to emit diagnostics must not prevent termination.

### 17.3.4. Checks, builds, and constant evaluation

Abort conditions and termination behavior are identical in Debug and Release builds. An implicit check failure is a language-guaranteed termination operation, not merely a call to a replaceable function; replacing `$abort` or diagnostic handling cannot make an Abort return normally.

Failures in required compile-time evaluation are compile-time errors. This adds no general constant evaluator: directives use Chapter 19, lengths Chapter 4, and static index recognition §15.1.3. Recognizing an index literal does not make bounds evaluation mandatory at compile time. A runtime check Aborts only when executed and failing, and optimization cannot Abort skipped conditional or short-circuit operations. Unsafe contract violations are not guaranteed to be detected as Abort.

Language-defined static checks, including literal fitting and the exact Dictionary duplicate-key subset of §12.3.4, apply independently of optimization. Outside required constant-evaluation contexts, knowledge obtained only by constant propagation or folding must not turn a specified runtime Abort into a compile-time error, even when the failing value is statically known.

These rules are independent of implementation mechanisms such as a `trap` instruction. APIs that return failures as values use the contracts above; wrapping or saturating integer arithmetic requires separate explicit library APIs.

## 17.4. Warnings

A warning is diagnostic information about a condition worth reporting while processing continues and its result remains usable. Examples include deprecated configuration, ignored optional metadata, fallback encoding, and a failed cache update after the primary operation succeeds.

A warning does not itself change control flow or implicitly produce `None`, `Err` or Abort, and it is not a third state of `Result`. Return warnings to callers as ordinary values when needed:

```kimi
struct ParseReport<T>
    let value: T
    let warnings: Array<ParseWarning>

func parse(source: string) -> Result<ParseReport<Syntax>, ParseError>
```

This API returns warnings with successful results. To preserve warnings on failure, include them in the error value or in an outer report containing the `Result`.

The following subsections define compiler warnings, not returned API values; they change neither Type fitting, execution nor overload choice. For the same value at the same discard occurrence, emit only the highest applicable warning: **unintended Unit inference > Kimi Result discard > try success discard > effect-free value discard**. Keep each warning's existing triggering conditions; independent occurrences and unrelated diagnostics remain independent. Explicit discard (§14.2.4) suppresses only the discarded result's warning.

### 17.4.1. Unintended Unit inference

A warning about a possibly missing result is issued when all of the following hold:

- a value-used construct, or a return-inferred anonymous function, has the inferred result Type Unit;
- Unit was not fixed by a declaration, a construct rule or an expected Type (named functions with omitted return Types are excluded);
- an indented body supplying Unit has structural end arrival and discards a non-Unit, non-Never value at its end.

The tail is inspected through grouping and labels. For `if`, `match` and `do`, inspection descends into bodies that structurally complete normally, checking the single expression or the last indented item, and inspects the discarded inner values even when the enclosing discarded construct has Type Unit. It stops at an explicit `()`, transfers, iterations, statements and separate functions.

```kimi
let total = do
    if useCache => loadCached()
    else => compute()
// If both calls return i32, total is Unit; warn about the discarded tail results.
let corrected = if useCache => loadCached() else => compute()
```

The diagnostic should suggest `yield`, `return`, a named `exit` or a single-item body, as appropriate.

If the two calls in the example return `Result`, each discarded arm result receives only this warning, not an additional discarded-Result or effect-free warning, because supplying the missing result removes the discard itself.

### 17.4.2. Discarded effect-free values

For a non-Unit expression in Discard Context, a warning is issued when its evaluation, acquisition and destruction can be shown to have no observable effect or ownership-state change. This includes functions with fixed Unit returns.

The analysis considers literals, Copy locals, built-in operations and comparisons, Case construction and Tuples, including their nested operations. It accounts for calls, user-defined comparisons, Move and Loan effects, destruction, Abort and possible divergence. Callee and destructor bodies are not inspected to infer purity; if the absence of effects cannot be established, no warning is issued.

```kimi
func isAdult(age: i32) => age >= 18 // Warning: add -> bool if this is the result.
func answer() => 42               // Warning also with an omitted Unit return Type.
left == right                    // Warning for initialized i32 locals.
if ready => 1                     // Warn on the discarded body value.
func cleanup() => handle.close()  // Do not assume a call is effect-free.
```

The explicit no-op Unit value `()` receives no warning. The diagnostic should suggest a return annotation or a use of the value and never deletes the expression automatically. Diagnostics for `defer` placement and `while true` follow §16.1 and §14.9.2.

### 17.4.3. Try success and intentional discard

Warn when the try expression's result itself is in Discard Context, is not provably Unit, and is not Never. A Result payload instead receives the higher-priority Result warning, including Result<(), E>. Option<()> success is Unit and excluded; discarding an entire Option adds no Type-specific warning.

Use existing Value/Discard Context propagation, including parentheses and branch bodies, without tracking value provenance. Initializing a local, passing an argument, returning or using the result in another operation is use; later nonuse is not a try warning. The operand's outer Result is processed by try and is not itself discarded. At generic definition time, warn unless Unit or Never is proved by existing normalization and evidence; do not rediagnose per instantiation.

| Form (in Discard Context) | Outcome | Warning |
| --- | --- | --- |
| prepare() returning Result<Data,E> | Ignore success or error | Result |
| _ = prepare() | Explicitly ignore success or error | None for this result |
| try prepare() | Propagate error, discard Data | try success |
| _ = try prepare() | Propagate error, explicitly discard Data | None for this result |
| save() returning Result<(),E> | Ignore success or error | Result |
| try save() | Propagate error, continue with Unit | None |
| _ = try save() | Explicit Unit discard | None; discard marker optional |

A try-success warning explains that the extracted value is unused, not that failure is unhandled. Suggest using it or writing `_ = try ...`. For Result discard, suggest applicable options in order: propagate and use success (or explicitly discard it), handle with match, then explicitly ignore the entire Result. Do not suggest bare try as warning-free for a non-Unit payload. Fixes target the actual discard site, preserve Body form/Context/ownership, and are not applied automatically.

## 17.5. Test verification operations

### 17.5.1. Syntax and permitted contexts

`$expect(condition)` and `$require(condition)` are nonreplaceable Composition Root verification operations. Each takes one positional `bool` expression and an optional `message:` string expression; no other arguments, generic arguments or trailing comma are accepted. They are standalone executable items, not argument, initializer or other expression operands, and when the condition is true their normal continuation supplies Unit. They introduce neither macros nor exceptions.

| Operation | Permitted bodies | After a false condition |
| --- | --- | --- |
| `$expect` | Test-only function bodies and their nested local functions, Closures and `defer` bodies | Record the failure and continue |
| `$require` | Test-only function bodies and their nested local functions, Closures and `defer` bodies | Record the failure and Abort the current case's process |

Membership is defined in §18.8. A product helper cannot contain verification operations merely because only tests call it, while test-only helpers, including helpers with non-Unit results, may contain both. These placement restrictions, and the message-transfer restrictions of §17.5.3, are checked before ordinary control-flow checking. `$require` uses the Abort termination semantics of `$abort` (§17.3): a failed `$require` has no normal continuation or return target and does not unwind through helpers. Ordinary `require condition else Body` is separate (§14.11). Verification adds no Option/Result extraction and no Type refinement. An invocation without an active case is misuse regardless of the condition; it is never success, ignored work or another case's failure (§22.6.1).

### 17.5.2. Evaluation, failure and cleanup

The condition is evaluated once, and its `bool` and any permitted diagnostic scalar snapshots are secured. Ordinary evaluation and short-circuit order, overload selection, Copy/Move, Loans and destruction are preserved:

```text
condition -> secure bool
  true  -> condition cleanup -> continue
  false -> latch failure and report basic diagnostic
        -> condition cleanup
        -> evaluate/report optional message -> message temporary cleanup
        -> expect: continue; require: Abort the current case's process
```

Condition temporaries have the common `bool`-condition lifetime (§14.2.3). Message evaluation is modeled only on the failure branch, including its Move and initialization effects on later checking. A true verification creates no diagnostic strings, failure records or success events, and failures from nested verifications remain independent. Resource and reporting bounds follow §22.6.5 and never omit language evaluation.

A transfer, Abort or divergence before the `bool` is secured follows its existing rules and is not an invented verification failure. Once `false` is secured, a subsequent Abort or divergence in cleanup or message evaluation cannot erase the failure, and the termination reason is kept too. `$require` initiates its Abort only after condition and message temporary cleanup completes normally; an `$expect` failure during that cleanup adds a failure. Once Abort starts, the enclosing scopes' `defer` bodies do not run, remaining locals are not destroyed and normal static shutdown is not performed; an Abort during cleanup stops its remaining work under §17.3.

The `$require` Abort identifies the failed verification site. It reuses the Abort termination path without reevaluating the condition or message and without creating a second verification failure for the same check. The parent runner keeps both the verification failure and the actual termination reason (§22.6.4). The Abort terminates this case's child process; it does not itself terminate the parent runner or other cases.

```kimi
#Test
func stopsAfterFailure()
    defer => ::Kimi.Console.writeLine("Not run after Abort")
    $require(false, message: "Input is not ready")
    ::Kimi.Console.writeLine("Not reached")
```

### 17.5.3. Message control boundary and observation

A supplied message is evaluated exactly once, only after a false condition and its cleanup. Normal visibility and ownership apply, and the message gains no special access to condition temporaries. No transfer from the message expression may target a construct outside it: explicit `return`, `exit`, `continue` and `yield` are checked, while transfers completed inside the message's own loops and do bodies, and returns from functions declared or called inside it, remain legal. Actual targets are resolved; the expression is not implicitly wrapped in a Closure, and no captures are added. Abort and divergence have no transfer target and keep their rules, including an Abort initiated by a nested `$require`.

Diagnostics observe values obtained during the original evaluation of the expression. They never rerun a call, Property or comparison, reread a variable after later operand effects or cleanup, or evaluate a short-circuited operand; an unevaluated operand may be shown as such. Initially, supported `bool` and numeric scalars are snapshotted, concentrating on the operands of an outer comparison. Saving scalar bits in compiler temporaries is permitted, but diagnostic display must not introduce a language Copy or Move, extend Loans or change lifetimes. For unsupported values, source and expression information is shown and the omitted values are indicated. Arbitrary objects are not traversed and user display functions are not called implicitly. Operands are labeled by their expressions or as left and right, not by an inferred actual/expected role.

Verification is enabled in both Debug and Release. Optimization may remove a side-effect-free, always-true check only while preserving the required evaluation, cleanup and diagnostic meaning. A constant-false runtime condition alone is not a compile-time error. [Appendix A.17](appendices/A-compiler-requirements.md#a17-test-verification-and-runner-requirements) defines the verification requirements.
