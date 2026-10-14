# 17. Failure handling

[Specification index](../SPEC.md)

## 17.1. Error policy

Kimigayo has no exceptions. A failure the caller can handle is a value of `Option<T>` or `Result<T, E>` ([§17.2](#172-option-and-result)); when normal execution cannot continue, the process Aborts ([§17.4](#174-abort-termination)). An API returns runtime warnings as ordinary values. Failure values propagate only through explicit `try` ([§17.3](#173-try-propagation)); there is no postfix `?`. The `require` statement ([§14.11](14-control-flow.md#1411-require)) is explicit control flow and performs no Option or Result unwrapping or propagation.

```kimi
// loadSize(path: string) -> Result<usize, FileError>
func run(path: string) -> Result<(), FileError>
    let size = try loadSize(path) // Explicit propagation.
    // Error: no postfix operator: let size = loadSize(path)?
    return .Ok(())
```

## 17.2. Option and Result

`Option<T>` represents a value or normal absence without a reason. `Result<T, E>` represents success or failure with a reason; `E` is an ordinary Type. Both are Kimi declarations whose required shape is given in [§22.1](22-core-execution-and-foreign-functions.md#221-required-kimi-declarations); programs do not redeclare them.

An API's contract chooses `Option` for normal absence and `Result` for failure with a reason; `Result<Option<T>, E>` distinguishes both. Both follow ordinary enum Copy derivation ([§3.5.2](03-types-and-values.md#352-copy-capability)): `Result<i32, i32>` is Copy, while `Result<i32, string>` is not Copy even when it holds `Ok`.

Failure values are handled with ordinary control flow and never propagate implicitly; returning one runs normal Scope Exit ([§16.2](16-scope-exit-and-destruction.md#162-scope-exit)). A function returning `Result` may still Abort.

```kimi
// lookup(id: UserId) -> Result<Option<User>, LookupError>
match lookup(id)
    .Ok(.Some(let user)) => greet(user@move)
    .Ok(.None) => greetGuest()
    .Err(let error) => report(error@move)
```

## 17.3. Try propagation

`try expression` extracts the success payload of an Option or Result, or returns its failure as `return` would at the same position. Grouping follows the grammar (`Try`, [§F.9](appendices/F-syntax-summary.md#f9-expressions)): calls, selection, indexing and `@` bind more tightly; multiplicative operators bind less tightly.

```kimi
// Inside a function returning Result<…, E>; parseCount() -> Result<i32, E>
try parseCount() + 1         // (try parseCount()) + 1
try parseCount().isEven      // Error: groups as try (parseCount().isEven); Result has no isEven.
try pending@move             // Transfer the stored Result, then extract.
```

**Operand.** The operand's normalized Type must be an owned Kimi `Option` or `Result` (by Symbol, not by name or shape), proven in a generic definition. `Option<ref/T>` is valid; `ref/Option<T>`, `uniq/Result<T, E>` and `obj/Option<T>` are not. An operand typed Never is invalid (`try $abort(...)`); a Never success payload is valid.

**Evaluation.** The operand is evaluated once in Value Context without an expected Type and acquired as an owned value: a Copy Place is copied, a non-Copy Place requires `@move`, and a temporary is transferred. Then:

| Operand | Normal value | Failure return | Required return target |
| --- | --- | --- | --- |
| `Option<T>` | Payload of `Some`, Type `T` | `.None` | `Option<U>` |
| `Result<T, E>` | Payload of `Ok`, Type `T` | `.Err(error)` | `Result<U, F>`, where `error` fits `F` by ordinary rules |

Only one layer is extracted. The normal value is a value, not a payload Place; a borrowed payload keeps its permissions, Origins and Loans, and `try` creates no borrow. `T` and `U` need not agree; error Types are never combined or converted implicitly.

**Return target.** `try` behaves as a `match` with `return .None` or `return .Err(error)` at the same position, with that return's target and restrictions: functions, Closures and getters have targets; selections, loops, `do` and `unsafe` do not. `try` is therefore invalid where `return` is forbidden (`defer` bodies, default values, verification messages, source top-level items) and in `main`, `init`, `set` and `drop`, whose targets are Unit. This is checked even in unreachable code and when the operand is known to be `Some` or `Ok`.

**Cleanup.** The payload or failure value is secured before temporary and Scope Exit cleanup; the operand's source keeps its ordinary Moved state. A failure during argument or aggregate evaluation skips the remaining evaluation and cleans up acquired parts as Interrupted expressions ([§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order)); pending return delivery follows [§16.2.3](16-scope-exit-and-destruction.md#1623-completion-and-abnormal-termination).

**Inference.** The operand is resolved from its own receiver, arguments and explicit Types; neither the expectation on `try` nor the return target feeds back into its inference or overload selection, so `try .None` is invalid. Like a written Case return, an anonymous function's implicit failure return depends on the expected Type and supplies no return Type candidate; the return Type comes from an annotation, a fixed expected signature ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)) or another independently typed result. Success results are never wrapped in `Some` or `Ok` automatically.

```kimi
// Inside a function returning Result<…, ParseError>
// parse<T>() -> Result<T, ParseError>; getOpt() -> Option<i32>
let value = try parse<i64>()            // Valid in a compatible return target.
let bad: i64 = try parse()              // Error: the annotation does not infer T.
let g = func () -> Option<i32> => .Some(try getOpt())
let f = func () => try getOpt()         // Error: inferred i32 cannot return None.
func h() -> Option<i32> => try getOpt() // Error: i32 is not Option<i32>.
```

There is no user-defined `try`, `try` block, Option/Result interconversion or implicit error conversion. An API whose name starts with `try` is unrelated: it promises only its documented result.

## 17.4. Abort termination

**Abort Termination** abnormally terminates the whole process. Explicit `$abort` and failed runtime checks share the rules of this section.

### 17.4.1. Causes

Abort signals a programming defect or an unrecoverable external condition; every cause has the same control flow. Checked runtime operations Abort on: an integer result the Type cannot represent (wrapping integer Types wrap instead, [§13.3.1](13-operators-and-assignment.md#1331-built-in-numeric-operators)); integer division or remainder by zero (floating-point division follows IEEE 754); a failed position or range resolution; the start of iteration over a reversed range; an invalid numeric conversion or shift count (`@wrap` and `@bits` never fail); a duplicate Dictionary literal key or a missing indexed key; forbidden static storage access ([§22.4.3](22-core-execution-and-foreign-functions.md#2243-static-initialization-and-shutdown)); failed allocation for Kimi object creation or Function Type erasure; and strong-owner count overflow. Each Kimi operation defines its invalid inputs; the Abort codes are listed in [§22.5.2](22-core-execution-and-foreign-functions.md#2252-abort-codes). Failed Type tests and checked object casts follow their bool, Option or Result contracts instead. The same cause can be recoverable under another API contract:

```kimi
// tryAllocate(size: usize) -> Option<Buffer>
func allocateRequired(size: usize) -> Buffer
    return match tryAllocate(size)
        .Some(let buffer) => buffer@move
        .None => $abort("Required memory could not be allocated")
```

### 17.4.2. Explicit abort

`$abort(message)` evaluates its argument once with expected Type `string`, then Aborts; if the argument does not complete normally, its own transfer, divergence or Abort takes effect. The expression has Type Never ([§3.1.3](03-types-and-values.md#313-unit-and-never)). Abort is not a control transfer and produces no Completion ([§14.1](14-control-flow.md#141-completions)).

```kimi
func requireValue(value: Option<i32>) -> i32
    return match value
        .Some(let x) => x
        .None => $abort("Required value is missing")
```

### 17.4.3. Termination and cleanup

Abort Termination begins at most once per process. The thread that begins it never resumes ordinary execution, performs no Scope Exit and terminates the whole process. When several threads initiate Abort, exactly one of them begins Abort Termination and attempts the report; every other initiating thread never returns and reports nothing. Other threads may keep running until the process terminates; what they do in that interval is unspecified, and none of them performs Scope Exit as part of the termination. This rule sets no wall-clock bound on argument evaluation or termination. Abort cannot be caught, recovered from or resumed, and performs no stack unwinding. Abort Termination performs no static shutdown ([§22.4.3](22-core-execution-and-foreign-functions.md#2243-static-initialization-and-shutdown)).

If Abort begins during Scope Exit, cleanup stops immediately: the remaining Deferred Blocks, automatic destruction and the rest of a running Deferred Block or `drop` do not run, and completed cleanup is not rolled back. Pending `return`, `exit`, `continue` and `yield` are abandoned, and their secured results are neither delivered nor destroyed.

```kimi
func process()
    let resource = makeResource()
    defer => close(resource)
    $abort("Fatal condition") // Neither close(resource) nor the destruction of resource runs.
```

Stack exhaustion is not undefined behavior. It terminates the process, and no Scope Exit runs. Whether it is detected and reported as an Abort, its diagnostics and the exit status are unspecified.

Abort ends the process with an unsuccessful exit status. The runtime attempts to report the reason and source location (the failed operation, or the `$abort` call; see [§22.5.1](22-core-execution-and-foreign-functions.md#2251-abort-location)); a failure to report never prevents termination. The diagnostic format, output stream and exit code are implementation-defined.

### 17.4.4. Builds and compile-time evaluation

Abort conditions and behavior are identical in all builds, and nothing can make an implicit check failure return. Wrapping instead of Abort is selected only by wrapping integer Types ([§3.1.1](03-types-and-values.md#311-wrapping-integer-types)) and `@wrap` ([§13.5.4](13-operators-and-assignment.md#1354-numeric-wrapping-and-bit-conversions)), never by a build setting or context; there is no saturating mode. A user arithmetic Contract defines its own failure behavior ([§13.3.5](13-operators-and-assignment.md#1335-evaluation-of-arithmetic-operators)).

A runtime check Aborts only when it executes and fails; optimization must not Abort for a skipped conditional or short-circuited operation. Undefined behavior is not guaranteed to be detected as an Abort.

A failure in required compile-time evaluation (directives, lengths) is a compile-time error, and language-defined static checks such as literal fitting and duplicate keys ([§12.4.3](12-expressions.md#1243-dictionary-literals)) apply regardless of optimization. Constant folding must never turn a runtime Abort into a compile-time error.

## 17.5. Warnings

Warnings change neither Type fitting, execution nor overload choice.

For one value at one discard occurrence, only the highest applicable warning is issued: unintended Unit inference, then discarded `Result`, then discarded `try` success, then effect-free value. An explicit discard `_ = expression` ([§14.2.4](14-control-flow.md#1424-explicit-discard)) suppresses the warning for the discarded result only.

Passing an interpolated string literal directly to `Utf8Writer.write` warns that it first creates an owning string ([§22.10.4](22-core-execution-and-foreign-functions.md#22104-trywrite)); this warning is independent of the priority above.

### 17.5.1. Discarded Result and try success

Discarding a Kimi `Result` in Discard Context warns, whatever its Copy capability or runtime Case. The Kimi `Result` Symbol identifies it, through aliases too; a user Type named `Result` does not. Discarding an `Option` has no Type-specific warning.

Discarding the result of `try` warns unless it is provably Unit or Never; `Option<()>` success is Unit. Discard Context propagates through parentheses and branch bodies. A generic definition warns unless Unit or Never is proved there; instantiations are not rechecked.

```kimi
// prepare() -> Result<Data, E>; save() -> Result<(), E>
func run() -> Result<(), E>
    prepare()          // Warning: Result discarded.
    _ = prepare()      // No warning.
    try prepare()      // Warning: the success value Data is discarded.
    _ = try prepare()  // No warning.
    save()             // Warning: Result discarded.
    try save()         // No warning: the success value is Unit.
    return .Ok(())
```

### 17.5.2. Unintended Unit inference

A warning is issued when a value-used construct or a return-inferred anonymous function has the inferred result Type Unit, Unit was not fixed by a declaration, construct rule or expected Type (an omitted named-function return Type fixes it), and an indented body supplying Unit reaches its end discarding a non-Unit, non-Never value. Inspection passes through grouping and labels into `if`, `match` and `do` bodies that complete normally, and stops at an explicit `()`, transfers, iterations, statements and separate functions.

```kimi
let total = do
    if useCache => loadCached()
    else => compute()
// Warning: total is Unit; the i32 results of both arms are discarded.
let corrected = if useCache => loadCached() else => compute()
```

### 17.5.3. Discarded effect-free values

A non-Unit expression in Discard Context warns when it is built only from literals, Copy locals, built-in operations and comparisons, Case construction and Tuples, and so provably has no effect. Calls, user-defined comparisons, Moves, Loans, destruction, Abort and divergence count as effects; callee bodies are not inspected. The explicit Unit value `()` never warns.

```kimi
func isAdult(age: i32) => age >= 18 // Warning: the bool is discarded.
left == right                       // Warning for initialized i32 locals.
func cleanup() => handle.close()    // No warning: a call is not assumed effect-free.
```

### 17.5.4. Positions and ranges that always fail

A position or range built only from literal-only expressions ([§12.3](12-expressions.md#123-literal-typing)), `^` applied to one, and omitted boundaries warns when its resolution ([§4.6.1](04-arrays-indexing-and-slices.md#461-positions), [§4.6.3](04-arrays-indexing-and-slices.md#463-resolution)) fails for every length (the element positions `-1` and `^0`, the closed end `..=^0`, and the reversed ranges `2..=1` and `^3..^5`), or for the length of the fixed array it indexes or is passed to. The judgment is exact and independent of optimization; boundaries from other expressions are never judged. The operation still Aborts only when executed, and an operation whose name starts with `try` returns `None` instead.

```kimi
let fixed: [3 of i32] = [1, 2, 3]
let a = fixed[3]          // Warning: position 3 fails for length 3.
let c = values[2..=1]     // Warning: reversed for every length.
let d = values[1..^1]     // No warning: resolves when the length is at least 2.
let e = values.remove(^0) // Warning: ^0 is never an element position.
```

## 17.6. Test verification operations

### 17.6.1. Forms and placement

`$expect(condition)` and `$require(condition)` take one positional `bool` expression and an optional `message:` string expression (grammar: `TestVerification`, [§F.10](appendices/F-syntax-summary.md#f10-statements-bodies-and-patterns)). They are standalone items, never expression operands, and complete with Unit when the condition is true. They are allowed only in test-only function bodies ([§18.6](18-modules-and-dependencies.md#186-product-and-test-inputs)) and their nested local functions, Closures and `defer` bodies, not in a product helper called only by tests. Verification adds no Option or Result extraction and no Type refinement.

### 17.6.2. Evaluation and failure

The condition is evaluated once with ordinary evaluation, short-circuiting, ownership and Loans; its temporaries have the `bool`-condition lifetime ([§14.2.3](14-control-flow.md#1423-conditions)). On `true`, its temporaries are cleaned up and execution continues. On `false`, the failure is recorded, the condition's temporaries are cleaned up, the message (if any) is evaluated once and its temporaries are cleaned up; then `$expect` continues and `$require` Aborts. For ownership and initialization checking, the message and its Moves and initializations occur only on the failure path. A transfer, Abort or divergence before the `bool` is obtained is not a verification failure; after `false` is obtained, nothing erases the failure. The `$require` Abort follows [§17.4.3](#1743-termination-and-cleanup); it reports the verification site and does not evaluate the condition or message again. In a test run, the Abort ends only the current case; other cases still run and report. How cases are isolated is implementation-defined.

```kimi
#Test
func stopsAfterFailure()
    defer => ::Kimi.Console.writeLine("Not run after Abort")
    $require(false, message: "Input is not ready")
    ::Kimi.Console.writeLine("Not reached")
```

### 17.6.3. Message and observed values

The message cannot use the condition's temporaries, and no `return`, `exit`, `continue` or `yield` in it may target a construct outside it.

A failure report shows only values obtained during the original evaluation: it never re-evaluates or rereads an operand or evaluates a short-circuited one, calls no user code, and adds no Copy, Move, Loan or lifetime change. Which values are shown and how is implementation-defined. Verification runs in all builds; a constant-false condition is not a compile-time error.
