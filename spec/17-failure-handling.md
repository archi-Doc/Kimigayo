# 17. Failure handling

[Specification index](../SPEC.md)

Failure handling determines whether execution continues with an ordinary value or terminates. Abort interacts with cleanup through its explicit termination rules.

## 17.1. Error policy

Kimigayo represents ordinary failures as values and uses Abort Termination only when normal execution cannot continue. It provides no exception throwing or catching mechanism.

Runtime problems fall into three categories:

| Category | Meaning | Representation |
| --- | --- | --- |
| Recoverable Failure | Expected failure that the caller can handle. | `Option<T>` / `Result<T, E>` |
| Unrecoverable Failure | Normal execution cannot continue. | `$abort(...)` or implicit Abort |
| Warning | Processing can continue successfully, but a condition merits notice. | Diagnostic |

These are not a simple severity ranking: `None` represents expected absence, `Err` an operation failure, Abort process termination, and Warning a diagnostic independent of control flow.

The examples use the defined [enum construction](06-declarations-and-containers.md#632-case-construction-and-resolution) and [Pattern](14-control-flow.md#1481-patterns) rules. Example APIs are illustrative.

Dedicated generic failure propagation such as `?` remains undefined. The [require statement](14-control-flow.md#1411-require-statement), including `require condition else => return`, is explicit control flow and performs no automatic Option/Result unwrapping or propagation.

## 17.2. Absence and failure as values

Use `Option` for a contract representing normal absence and `Result` for a contract representing failure with a reason. The API contract determines the choice, regardless of whether an individual caller uses the reason.

### 17.2.1. Option

`Option<T>` represents a value or normal absence without an absence reason.

This is the compiler-recognized Core Option declaration, whose identity is fixed in §22.1. The following enum shows its required shape; it is not an instruction to redeclare it in each program.

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

This is the compiler-recognized Core Result declaration under §22.1. The following enum shows its required shape. The discarded-Result warning refers to that Symbol Identity, including through aliases, rather than every user Type spelled Result.

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

With this expected Type, `.Ok(.Some(user))` constructs success with a value, `.Ok(.None)` normal absence, and `.Err(error)` a failed lookup operation.

Result uses ordinary [enum Copy derivation](03-types-and-values.md#352-enum-copy). Its conformance requires both complete payload Types to be Copy, independently of the active Case; these premises do not restrict Type formation. `Result<i32, i32>` is Copy, while `Result<i32, string>` remains Non-Copy even when it holds Ok. `Result<Data, FileError>` is Copy only if Data is also Copy. Borrow payloads follow their complete Semantics: shared borrows are Copy and retain their dependencies; exclusive borrows and owning object/counting handles are not Copy. An enclosing handle retains its own formation and acquisition rules. Generic proof follows §8.7: both Proven yield Proven, a valid Refuted operand refutes the conjunction, and remaining unresolved cases are Unknown, never assumed Non-Copy. Invalid Types or evidence remain Error.

### 17.2.3. Handling, propagation, and discarding

Recoverable failures are ordinary values: they neither throw exceptions nor propagate implicitly. Handle them with ordinary control flow such as `match`, and propagate them explicitly with `return`:

```kimi
func loadSize(path: string) -> Result<usize, FileError>
    return match readFile(path)
        .Ok(let data) => .Ok(data.count)
        .Err(let error) => .Err(error)
```

The return Type describes contractual absence or recoverable failure; a function returning `Result` may still Abort on an invariant violation or unrecoverable condition.

Discarding a Core `Result` expression in Discard Context is allowed but produces a compile-time warning, independently of Copy capability or runtime Ok/Err state. Identify it by its Core Symbol, including equivalent resolved paths. [Warning priority](#174-warnings) selects one report if several apply to the same discard. The warning does not change control flow. A caller can explicitly handle both variants with match to ignore the outcome intentionally. This section defines no special warning for discarding Option.

Returning a recoverable failure follows normal [Scope Exit](16-scope-exit-and-destruction.md#162-scope-exit-destruction) rules, including their requirement that earlier cleanup complete normally before remaining cleanup or result delivery proceeds. Use this path for ordinary failures requiring resource cleanup.

## 17.3. Abort termination

**Abort Termination** abnormally terminates the entire process executing the program when normal execution cannot continue. Explicit requests and implicit runtime check failures share the termination rules below.

### 17.3.1. Causes and API contracts

Abort is a termination mechanism, not a classification of causes. It may represent a programming defect, such as an invariant violation or unexpected state, or an unrecoverable external condition, such as allocation failure or an unavailable required runtime resource. Bug classification belongs to diagnostics and does not introduce different control flow.

Checked runtime arithmetic, indexing, and conversions Abort on their defined failures: integer overflow, invalid integer division/remainder, indices or Range boundaries, conversions or shift counts, duplicate Dictionary keys, and missing indexed keys. Each operation defines its invalid inputs; floating-point division by zero follows IEEE 754. Failed Type tests and checked object casts instead follow their Boolean/Option/Result contracts.

The same cause can instead be recoverable under a different API contract:

**Basic example.**

```kimi
func tryAllocate(size: usize) -> Option<Buffer>

func allocateRequired(size: usize) -> Buffer
    return match tryAllocate(size)
        .Some(let buffer) => buffer
        .None => $abort("Required memory could not be allocated")
```

`tryAllocate` returns absence when allocation is unavailable; `allocateRequired` treats that outcome as fatal.

Failure to allocate storage required by common Function Type erasure or [Core object creation](13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing) follows Abort Termination, independently of Copy/Move input acquisition. Core strong-owner duplication also Aborts if its count cannot be incremented without overflow.

### 17.3.2. Explicit abort and argument evaluation

`$abort(expression)` evaluates its argument once with expected Type string. If it completes normally, use the string as diagnostic information and Abort the process. Otherwise follow its transfer, divergence, or nested Abort without initiating this Abort. Dynamic message-construction failures follow the same rule.

The `$abort(...)` expression has Type Never and never completes normally. Apply the ordinary [Never](03-types-and-values.md#315-unit-and-never-types) and [result validation](14-control-flow.md#149-result-validation) rules:

```kimi
func requireValue(value: Option<i32>) -> i32
    return match value
        .Some(let x) => x
        .None => $abort("Required value is missing")
```

Abort itself is not a Control Transfer and produces no Completion; distinguish it from a transfer during argument evaluation. The [Evaluation Outcomes](14-control-flow.md#141-completions) are Completion, Divergence, and Abort Termination.

### 17.3.3. Termination, diagnostics, and cleanup

Immediate termination applies once Abort Termination begins: do not resume ordinary program execution or perform Scope Exit before terminating the entire process. This rule sets no wall-clock bound on argument evaluation or termination. Abort cannot be caught, recovered from, or resumed, and performs no Stack Unwinding.

Abort does not start Scope Exit processing. If it begins during Scope Exit, abort that processing immediately: do not execute remaining Deferred Blocks, automatic destruction, or the rest of an executing Deferred Block or `deinit`. Completed cleanup effects are not rolled back. Abort pending `return`, `exit`, `continue`, and `yield`; do not deliver secured results or perform additional cleanup to destroy them.

```kimi
func process()
    let resource = makeResource()
    defer => close(resource)
    $abort("Fatal condition")
```

Here, neither the registered `close(resource)` nor scope-exit destruction of `resource` runs.

Abort diagnostics carry a reason and source location: the failed operation for implicit Abort, or the `$abort(...)` call for explicit Abort. A duplicate dictionary key uses the later key expression. The runtime attempts to emit available information; successful or complete output is not guaranteed. The initial Windows format, stderr destination, and failure code 1 are defined in §22.5.4. Failure to emit diagnostics must not prevent termination.

### 17.3.4. Checks, builds, and constant evaluation

Abort conditions and termination behavior are identical in Debug and Release builds. An implicit check failure is a language-guaranteed termination operation, not merely a rewrite to a replaceable function call. Replacing `$abort` or diagnostic handling cannot make an Abort return normally.

Failures in required compile-time evaluation are compile-time errors. This adds no general constant evaluator: directives use §19, lengths §4, and static index recognition §15.1.3. Recognizing an index literal does not make bounds evaluation mandatory at compile time. A runtime check Aborts only when executed and failed; optimization cannot Abort skipped conditional/short-circuit operations. Unsafe contract violations are not guaranteed to be detected as Abort.

Language-defined static checks, including literal fitting and the exact Dictionary duplicate-key subset in §12.3.4, apply independently of optimization. Outside required constant-evaluation contexts, knowledge obtained only by constant propagation or folding must not turn a specified runtime Abort into a compile-time error. This also applies when the failing value is statically known.

These rules are independent of implementation mechanisms such as a `trap` instruction. APIs returning failures as values use the contracts above; wrapping or saturating integer arithmetic requires separate explicit library APIs.

## 17.4. Warnings

A Warning is diagnostic information about a condition worth reporting while processing can continue and its result remains usable. Examples include deprecated configuration, ignored optional metadata, fallback encoding, and a failed cache update after the primary operation succeeds.

A Warning does not itself change control flow or implicitly produce `None`, `Err`, or Abort. It is not a third state of `Result`. Return warnings to callers as ordinary values when needed:

```kimi
struct ParseReport<T>
    let value: T
    let warnings: Array<ParseWarning>

func parse(source: string) -> Result<ParseReport<Syntax>, ParseError>
```

This API returns warnings with successful results. To preserve warnings on failure, include them in the error value or an outer report containing the Result.

The following subsections define compiler warnings, not returned API values. They do not change Type fitting, execution, or overload choice. For the same value at the same discard occurrence, emit only the highest applicable warning: **unintended Unit > Symbol-specific > effect-free**. Each warning keeps its own triggering conditions. Distinct arm/body discards remain separate occurrences; unrelated diagnostics are not suppressed. Future Symbol-specific warnings occupy the middle tier and must define any same-tier priority when introduced.

### 17.4.1. Unintended Unit inference

Warn about a possibly missing result when all of the following hold:

- A value-used construct or return-inferred anonymous function has inferred result Type Unit.
- Unit was not fixed by a declaration, construct rule, or expected Type. Named functions with omitted return Types are excluded from this warning.
- An indented body supplying Unit has structural end arrival and discards a non-Unit, non-Never value at its end.

Inspect the tail through grouping and labels. For if/match/do, descend into structurally normally completing bodies, checking the single expression or last indented item. Inspect the discarded inner values even when the enclosing discarded construct itself has Type Unit. Stop at explicit `()`, transfers, iterations, statements, and separate functions.

```kimi
let total = do
    if useCache => loadCached()
    else => compute()
// If both calls return i32, total is Unit; warn about the discarded tail results.
let corrected = if useCache => loadCached() else => compute()
```

Suggest yield, return, a named exit, or a single-item body as appropriate.

If the two calls in the example return Result, each discarded arm result receives this warning alone, rather than an additional discarded-Result or effect-free warning: supplying the missing result removes the discard itself.

### 17.4.2. Discarded effect-free values

For a non-Unit expression in Discard Context, warn when evaluation, acquisition, and destruction can be shown to have no observable effect or ownership-state change. This includes functions with fixed Unit returns.

Consider literals, Copy locals, built-in operations/comparisons, Case construction, and Tuples, including their nested operations. Account for calls, user-defined comparisons, Move/Loan effects, destruction, Abort, and possible divergence. Do not inspect callee or destructor bodies to infer purity; if absence of effects cannot be established, omit this warning.

```kimi
func isAdult(age: i32) => age >= 18 // Warning: add -> bool if this is the result.
func answer() => 42               // Warning also with an omitted Unit return Type.
left == right                    // Warning for initialized i32 locals.
if ready => 1                     // Warn on the discarded body value.
func cleanup() => handle.close()  // Do not assume a call is effect-free.
```

Do not warn on the explicit no-op Unit value `()`. Suggest a return annotation or use of the value, without automatically deleting the expression. Defer placement and while-true diagnostics follow §16.1 and §14.9.2.

## 17.5. Test verification operations

### 17.5.1. Syntax and permitted contexts

`$expect(condition)` and `$require(condition)` are nonreplaceable Composition Root verification operations. Each takes one positional bool expression and an optional `message:` string expression. No other arguments, generic arguments, or trailing comma are accepted. Their normal continuation supplies Unit; they are standalone executable items, not argument, initializer, or other expression operands. They introduce neither macros nor exceptions.

| Operation | Permitted bodies | After a failed condition |
| --- | --- | --- |
| `$expect` | Test-only function bodies and their nested local functions, Closures, and defer bodies | Record failure and continue |
| `$require` | The Test function's own body, including its ordinary branches, iterations, and do bodies; exclude nested function, Closure, and defer bodies | Record failure and return from that Test function |

Membership is defined in §18.8. A product helper cannot contain verification operations merely because only tests call it. `$require` does not unwind through helpers or escape a call stack; it performs a normal return from its enclosing Test function. Ordinary `require condition else Body` remains separate (§14.11). Verification adds no Option/Result extraction or Type refinement. An invocation without an active case is misuse regardless of the condition, never success, ignored work, or another case's failure (§22.6.1).

### 17.5.2. Evaluation, failure and cleanup

Evaluate the condition once and secure its bool and any permitted diagnostic scalar snapshots. Preserve ordinary evaluation/short-circuit order, overload selection, Copy/Move, Loans and destruction:

```text
condition -> secure bool
  true  -> condition cleanup -> continue
  false -> latch failure and report basic diagnostic
        -> condition cleanup
        -> evaluate/report optional message -> message temporary cleanup
        -> expect: continue; require: ordinary return and Scope Exit
```

Condition temporaries have the common bool-condition lifetime (§14.2.3). Model message evaluation only on the failure branch, including its Move and initialization effects on later checking. True verifications create no diagnostic strings, failure records, or success events; failures from nested verifications remain independent. Resource/reporting bounds follow §22.6.5 and do not omit language evaluation.

A transfer, Abort, or divergence before securing the bool follows its existing rules and is not an invented verification failure. Once false is secured, subsequent cleanup/message Abort or divergence cannot erase the failure; retain the termination reason too. `$require` returns only after condition/message cleanup, then runs ordinary function Scope Exit. An expect failure during that cleanup adds a failure. Abort stops remaining cleanup under §17.3.

```kimi
#Test
func stopsAfterFailure()
    defer => ::Core.writeLine("cleanup")
    $require(false, message: "Input is not ready")
    ::Core.writeLine("Not reached")
```

### 17.5.3. Message control boundary and observation

Evaluate a supplied message exactly once, only after a false condition and its cleanup. Apply normal visibility and ownership; it gains no special access to condition temporaries. No transfer from the message expression may target outside it. Check explicit return/exit/continue/yield and returns generated by nested `$require`. Transfers completed inside the message's own loops/do bodies and returns from functions declared/called inside it remain legal. Resolve actual targets; do not implicitly wrap the expression in a Closure or add captures. Abort/divergence has no transfer target and retains its rules.

Diagnostics observe values obtained during the original expression evaluation. Never rerun a call, Property or comparison, reread a variable after later operand effects/cleanup, or evaluate a short-circuited operand. An unevaluated operand may be shown as such. Initially snapshot supported bool/numeric scalars, concentrating on operands of an outer comparison. Saving scalar bits in compiler temporaries is permitted; diagnostic display must not introduce a language Copy/Move, extend Loans, or change lifetimes. For unsupported values show source/expression information and indicate omitted values. Do not traverse arbitrary objects or implicitly call user display functions. Label operands by their expressions or left/right, not an inferred actual/expected role.

Both Debug and Release enable verification. Optimization may remove a side-effect-free always-true check only while preserving required evaluation, cleanup and diagnostic meaning. A constant-false runtime condition alone is not a compile-time error. [Appendix A.17](appendices/A-compiler-requirements.md#a17-test-verification-and-runner-requirements) defines verification requirements.
