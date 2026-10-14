# 14. Control flow

[Specification index](../SPEC.md)

The control-flow expressions are `if`, `match`, `for`, `while`, `loop`, `do` and the transfers `return`, `exit`, `continue` and `yield`. `unsafe`, `defer`, `require` and explicit discard are statements: they cannot be initializers, arguments or operands. Test bodies also admit `$expect` and `$require` ([§17.6](17-failure-handling.md#176-test-verification-operations)).

An **Iteration Construct** is a `for`, `while` or `loop`; an **iteration** is one execution of its body. A **selection** is one `if` chain or one `match`. Parentheses only group: they create no scope, transfer target or lookup barrier.

## 14.1. Completions

Evaluation ends in a **Completion**, in **divergence** (it never finishes) or in **Abort** (the process ends without a Completion or Scope Exit, [§17.4](17-failure-handling.md#174-abort-termination)). **Normal completion** `Normal(result)` returns to the evaluator; a statement completes with Unit without becoming an expression. An **abrupt completion** is `Return(target, result)`, `Exit(target, result)`, `Continue(target)` or `Yield(target, result)`; the transfer expression itself never completes normally.

After the required Scope Exit ([§16.2](16-scope-exit-and-destruction.md#162-scope-exit)), a construct receives a transfer addressed to it and propagates any other outward:

| Target | Receives | Action |
| --- | --- | --- |
| Iteration or `do` | `Exit(self, result)` | Complete with `result`. |
| Selection | `Yield(self, result)` | Complete with `result`. |
| Deferred body | `Exit(self, ())` | End the body; the pending Scope Exit resumes. |
| Iteration | `Continue(self)` | Start the next iteration. |
| Function | `Return(self, result)` | Deliver `result` to the caller. |

Never is a Type, not a Completion; it is inferred under [§14.9](#149-result-validation).

```kimi
func clamp(value: i32) -> i32
    if value < 0 => return 0                   // Return passes through the if.
    let size = label measure: do
        if value > 100 => exit to measure 100  // Exit completes the do with 100.
        exit to measure value
    return size
```

## 14.2. Bodies and evaluation contexts

### 14.2.1. Body forms

Every executable body is a **Body** (grammar: [§F.10](appendices/F-syntax-summary.md#f10-statements-bodies-and-patterns)):

- A **single-item body**, `=> item`, holds one expression or statement starting on the header's line; it may continue by ordinary expression continuation or a match arm list.
- An **indented body** holds declarations, expressions, statements and permitted directives, run in order. It needs at least one complete source item (blank lines and comments do not count); write `()` to do nothing. A missing body is an error and does not absorb the next item.

Selection clauses and arms, iterations, `do`, `unsafe`, `defer`, `require` failure bodies, functions, anonymous functions, Closures, specializations, accessors, `init` and `drop` have Bodies. Container bodies and match arm lists are layout bodies ([§F.4.2](appendices/F-syntax-summary.md#f42-layout-events)) but not Bodies. Each Body is its own scope. Declarations and directives need the indented form, and each position keeps its declaration restrictions ([§7.5](07-functions-and-callable-values.md#75-function-constraints)).

Nonemptiness is checked before directive selection, so a complete directive counts even when it selects nothing. After selection the ordinary Type, transfer and result checks apply.

```kimi
defer => ()               // Explicit no-op.
defer
    #if windows
        closeHandle()     // Valid even if nothing is selected.
defer
    // Error: no source item.
nextOperation()
```

### 14.2.2. Evaluation contexts and body results

**Value Context** covers initializers, arguments, conditions, `match` and `for` Subjects, and operator and transfer operands. **Discard Context** covers standalone expressions and the direct expressions of indented bodies. Parentheses keep the context and expected Type. None of an expected Unit, non-use, reachability or optimization makes a Value Context a Discard Context. Discarding destroys the result at its normal lifetime; it is not a conversion to Unit, and every check still applies, including the discarded-`Result` warning ([§17.5](17-failure-handling.md#175-warnings)).

The direct expressions of an indented body, including the last, are discarded; **structural end arrival**, the normal completion of the body's item sequence, supplies Unit where the owner uses the body result. A single-item body follows this table; these rules are fixed before the body is checked:

| Owner | Single-item expression | Result transfer |
| --- | --- | --- |
| Function, `get` | Return Type fixed as Unit: discard and complete with Unit; otherwise return the value | `return` |
| `if`, `match`, `do` | Use the value in Value Context; discard it and complete with Unit in Discard Context | `yield`; named `exit` for `do` |
| `for`, `while`, `loop` | Discard; continue the iteration | `exit` |
| `unsafe`, `defer`, `set`, `init`, `drop` | Discard; complete with Unit | `exit` (`defer`), `return` (`set`, `init`, `drop`) |
| `require` failure | Discard; must not continue ([§14.11](#1411-require)) | none |

A statement in a single-item body supplies Unit only if it can complete normally. Discarded values need no common Type, but every value supplied to a target must fit its **Target Result Type** ([§14.9.2](#1492-target-result-type-and-expression-type)), even when the target's result is discarded. It is Unit for `for`, `while`, `defer`, `set`, `init`, `drop`, and for `if`, `match`, `do` and `loop` in Discard Context. Receiving a transfer supplies no implicit body result.

```kimi
if ready => visited.insert(id)   // The bool is discarded.
let bad: i32 = if ready
    compute()                    // Error: discarded; the implicit Unit does not fit i32.
else => 0
loop => exit 1                   // Error: a discarded loop requires Unit exits.
```

### 14.2.3. Conditions

The conditions of `if`, `else if`, `while` and `require` and match guards are `bool` expressions; there is no `if let`, `var` condition or condition Pattern. A body-bearing expression in a header expression follows the header-expression rule of [§F.4.4](appendices/F-syntax-summary.md#f44-header-expressions-and-delimiter-regions).

A test evaluates its condition once, secures the `bool`, destroys the remaining condition temporaries in reverse creation order, ends guard-local temporary Loans and then branches; cleanup effects do not reevaluate it. A transfer, divergence or Abort ends the test on that path. Match Subjects, iterators and bindings keep their own scopes.

```kimi
let guard = registry.lock()
if guard.contains(id) => work()   // A guard created in the condition would be destroyed before work().
```

### 14.2.4. Explicit discard

`_ = Expression` is a statement (not assignment, not an expression, no compound form) that completes with Unit. The operand is checked in Value Context without an expected Type, introduces no local, is acquired by bare acquisition ([§3.5](03-types-and-values.md#35-copy-and-move)) or transfer, and is destroyed at statement temporary cleanup. A bare Non-Copy Place is an error unless it stores `uniq/T` or `objuniq/T`: then `_ = r` Reborrows a child and drops it, and `_ = r@move` consumes `r`. Explicit discard suppresses only the discard warnings for that result ([§17.5](17-failure-handling.md#175-warnings)); discarding a `Result` destroys its error payload and does not catch Abort.

```kimi
_ = prepare()          // Ignore the whole Result, including Err.
_ = try prepare()      // Propagate failure; discard success.
_ = resource@move      // Destroy a Non-Copy value early.
_ = r                  // r: uniq/Node; r stays usable.
// _ = resource        // Error: a bare Non-Copy Place.
// _ = .None           // Error: no Type information.
// consume(_ = prepare())   // Error: not an expression.
```

## 14.3. `do` and `unsafe`

`do Body` is an expression that runs once, under [§14.2.2](#1422-evaluation-contexts-and-body-results). A label permits a self-targeted `exit to Label`; an unlabeled `exit` skips `do`, so an unlabeled indented `do` cannot supply a non-Unit result.

`unsafe` and `defer` are statements of executable scopes. `defer` registers its Body for Scope Exit ([§16.1](16-scope-exit-and-destruction.md#161-deferred-blocks)). An **Unsafe Block** (`unsafe` with a Body; `unsafe func` is a modifier) runs at once with lexical permission for the unsafe operations of [§5](05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory) and for `noinit` ([§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)), which uses the permission even when nothing is read. The permission reaches nested deferred bodies but not across Function Boundaries; an operation uses the innermost Unsafe Block. An `unsafe func` grants none inside itself ([§7.6](07-functions-and-callable-values.md#76-unsafe-functions)). An Unsafe Block is no transfer target or barrier and relaxes no Type, ownership or Loan check. One whose permission no operation uses is the warning `UnnecessaryUnsafeBlock_Kd`.

```kimi
let result = label work: do
    if cached() => exit to work cachedValue()
    exit to work compute()
do
    let resource = openResource()
    defer => close(resource)
    use(resource)
defer => unsafe => releaseRaw(pointer)
unsafe
    let address = pointer@usize        // Warning: nothing needs unsafe permission.
// let x = unsafe => readRaw(pointer)  // Error: unsafe is a statement.
```

## 14.4. Labels

`label Label:` names the `if`, `match`, `for`, `while`, `loop` or `do` that follows on the same physical line (grammar: [§F.10](appendices/F-syntax-summary.md#f10-statements-bodies-and-patterns)). It adds no scope or binding and prefixes nothing else. Recognition of the prefix follows [§F.3.2](appendices/F-syntax-summary.md#f32-contextual-keywords).

Labels have their own namespace and are visible only inside their construct's bodies, not its conditions, Subject or guards. Equal labels with overlapping scopes in one function are an error. A label adds no grouping: `return (label c: if ready => a else => b) + 1` adds on both paths, while without the parentheses only the `else` path adds.

```kimi
consume(label choice: if ready => 1 else => 0)   // Labeled positional argument.
return label work: do => compute()
let label: bool = true
let accepted: bool = label label: do
    exit to label label   // Label namespace target; Value namespace operand.
```

## 14.5. Control transfers

### 14.5.1. Syntax and operands

The forms are `return [E]`, `exit [to Label] [E]`, `continue [to Label]` and `yield [to Label] [E]` (grammar: [§F.10](appendices/F-syntax-summary.md#f10-statements-bodies-and-patterns)). Transfers have Type Never. An omitted operand is `()`, checked against the target's Target Result Type; delivery follows [§16.2.2](16-scope-exit-and-destruction.md#1622-results-and-transfers).

- `to` is contextual directly after `exit`, `continue` or `yield` and commits to the named form; `return to` returns the value `to`. The Label is exactly one Name, never a computed destination.
- An omitted operand is decided by [§F.10.2](appendices/F-syntax-summary.md#f102-control-constructs); a full expression is never retried.

```kimi
exit to search score(item)
continue to outer
yield to choice match mode
    .Fast => 1
    _ => 0
// [exit: value]            // Error: ':' does not omit the operand.
// [exit to work x: value]  // A Dictionary: the colon follows the complete operand.
```

### 14.5.2. Targets and barriers

| Transfer | Unlabeled target | Named target | Lookup barrier |
| --- | --- | --- | --- |
| `return` | function | none | `defer` |
| `exit` | iteration or `defer` | iteration or `do` | function; named also `defer` |
| `continue` | iteration | iteration | function, `defer` |
| `yield` | `if` or `match` | `if` or `match` | function, `defer` |

The nearest target is resolved first and checked afterwards; a mismatch never resumes the search. A missing or wrong-kind target is an error, and so is an unlabeled `yield` to a selection in Discard Context. A construct is a target or barrier only inside its bodies; `unsafe` and `require` are neither. A transfer targets only an enclosing construct of the same function and never crosses a function or deferred body.

**Function Boundaries** are named functions, methods, anonymous functions, Closures, specializations, accessors, `init` and `drop`. A function declared in `defer` has its own `return` target. `return` in `drop` does not skip Field destruction ([§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)). A named function keeps its declared or default Unit result even if its body never finishes ([§7.1](07-functions-and-callable-values.md#71-declarations-bodies-and-results)).

```kimi
let result = label choice: if enabled
    if cached() => yield to choice cachedValue()   // Unlabeled, it would target the discarded inner if.
    yield compute()
else => 0
label outer: for row in rows
    for cell in row
        if skipRow(cell) => continue to outer
defer => exit ()     // Ends the deferred body.
// defer => return   // Error: defer is a barrier for return.
```

## 14.6. Iteration constructs

Each iteration has a fresh body scope, cleaned up before the next. The body's normal result is discarded.

### 14.6.1. `for` and `while`

`for` acquires its Subject once and takes successive items; `while` tests its `bool` condition before each iteration. Both complete with Unit on exhaustion, a false condition or a self-targeted `exit`. `while` keeps a static false path even for `true` ([§14.9.3](#1493-reachability)); use `loop` to repeat unconditionally.

A `for` binding is one slot or a parenthesized Tuple of slots (grammar: `ForBinding`, [§F.10](appendices/F-syntax-summary.md#f10-statements-bodies-and-patterns)), never a general Pattern: `(a, b)` decomposes a pair, `(a,)` a one-element Tuple, and `(x)` is invalid. A slot is a Name (immutable), `var Name` (a reassignable local; [§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects) covers reference items) or `_` (an unnamed binding that cannot be used). `var _` is invalid; named slots are distinct.

```kimi
for (key, value) in dictionary => process(key, value)
for (only,) in singles => use(only)   // singles: Array<(i32,)>
for var number in numbers@move
    number += 1                       // Updates only the iteration local.
while ready => process()
// for (x) in xs => use(x)            // Error: write `for x in xs`.
```

### 14.6.2. Iteration protocol

`for` uses only `LendingIterator` ([§22.2.1](22-core-execution-and-foreign-functions.md#2221-iterator-contracts)) and the three iteration entries ([§22.2.1](22-core-execution-and-foreign-functions.md#2221-iterator-contracts)). The Subject is acquired under [§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects); its mode selects the entry, called once on a receiver acquired by reference-path selection ([§3.4.3](03-types-and-values.md#343-reference-path-selection)). Each step calls `LendingIterator.next` on a short exclusive reborrow of the iterator, finishes on `None` and binds the `Some` payload.

| Subject mode | Typical Subjects | Entry |
| --- | --- | --- |
| Shared | `values`, `values@ref`, a `ref/C` value | `Iterable.iterate` |
| Exclusive | `values@uniq`, a `uniq/C` value | `UniqIterable.iterateUniq` |
| ByValue | `values@move`, a temporary, `range@copy` | `IntoIterable.intoIterator` |

ByValue never takes ownership of a referent. A missing or ambiguous conformance is an error; nothing else is tried. An iterator value is enumerated through `Kimi.Iteration.owning(it@move)` or `Kimi.Iteration.borrowing(it@uniq)` ([§22.2.2](22-core-execution-and-foreign-functions.md#2222-standard-adapters-and-iterators)).

The item Type is `LendingIterator.LentItem(step)`, or `Iterator.Item` for an Iterator. A single slot binds it unchanged. A parenthesized binding requires a Tuple item with exactly as many elements as slots, or a safe reference to one ([§14.8.1](#1481-patterns)), and decomposes it under [§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects): `ref/(A, B)` binds `ref/A` and `ref/B`. `_` still takes and cleans up the item.

| Enumerated value | `Iterable` item | `UniqIterable` item | `IntoIterable` item |
| --- | --- | --- | --- |
| `Array<E>`, `[N of E]` | `ref/E during a` | `uniq/E during a` | `E` |
| `Dictionary<K, V>` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` | `(K, V)` |
| `Slice<E>` | `ref/E during s` | `ref/E during s` | `ref/E during s` |
| `Range<T, T>`, `ClosedRange<T, T>` (`T is PrimitiveInteger`) | `T` | `T` | `T` |
| `ResolvedRange` | `isize` | `isize` | `isize` |

`a` is the Subject borrow's Origin and `s` the Slice's `source`; element Origins stay nested (`ref/(ref/Node during b) during a`). Arrays and Slices go in index order, Dictionaries in insertion order. A range with an omitted, from-end or mixed-Type boundary is not enumerable ([§4.6.4](04-arrays-indexing-and-slices.md#464-range-iteration)).

**Loans and cleanup.** The Subject Loan is live while the iterator or an escaped item needs it, and a Loan that conflicts with the next `next` makes the back edge an error. Fall-through and `continue` to this loop clean up the iteration scope before the next `next`. Every transfer that leaves the loop (`exit`, `return`, `try` propagation, outward `yield`, and `continue` to an outer loop) cleans up the iteration scope, then the iterator, then the internal Subject ([§16.2.2](16-scope-exit-and-destruction.md#1622-results-and-transfers)). An early exit consumes no further items. Bindings clean up last to first, after body locals and defers ([§16.2](16-scope-exit-and-destruction.md#162-scope-exit)).

```kimi
for item in items => inspect(item)            // ref/Item; items stays usable.
for item in items@uniq => update(item)        // uniq/Item
for item in items@move => store(item@move)    // Item
for (a, b) in pairs => total += a + b         // pairs: Array<(i32, i32)>; a, b: ref/i32
// for x in counter => use(x)                 // Error: counter: Counter, a LendingIterator with no entry conformance; write Kimi.Iteration.owning(counter@move).
```

### 14.6.3. `loop`

`loop` repeats its body; the body end and `continue` restart it. Only a self-targeted `exit` supplies its result: inferred under [§14.9](#149-result-validation) in Value Context, Unit in Discard Context.

```kimi
let found: i32 = label search: loop
    for item in items[..]
        if accepts(item) => exit to search score(item)   // Unlabeled, exit would end the for.
    exit -1
loop => work()
```

## 14.7. `if` and `yield`

`if` runs the first clause whose condition is true, or `else`. An `else if` chain is one selection. An omitted `else` is exactly `else => ()`, including its Unit result in Value Context. Each clause and arm chooses its Body form independently. `yield` ends its selection ([§14.5.2](#1452-targets-and-barriers)); to end a discarded selection early, name it and supply Unit.

```kimi
let value = if ready
    prepare()
    yield 1
else => 0
let unit: () = if ready => ()   // Valid, with the omitted else.
// let bad = if ready => 1      // Error: i32 and Unit conflict.
label action: match event@ref
    .Save
        if readOnly => yield to action
        save()
    _ => ()
```

## 14.8. `match`

The Subject is evaluated once and acquired under [§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects), which fixes its mode. The first arm, in source order, whose Pattern matches and whose guard is true is selected, and only its body runs. A match has at least one arm; arm lists contain no directives ([§19.1](19-compile-time-directives.md#191-placement-and-structure)). A match is exhaustive in every context ([§14.8.4](#1484-coverage-and-unreachable-patterns)).

```kimi
func positiveOrZero(value: Option<i32>) -> i32
    return match value
        .Some(let n) if n > 0 => n   // n: ref/i32, value-read at the i32 result.
        .Some(_) => 0
        .None => 0
```

### 14.8.1. Patterns

Patterns occur only in `match` arms.

| Pattern | Example | Meaning |
| --- | --- | --- |
| Wildcard | `_` | Accept without testing or acquiring. |
| Literal | `0`, `-1`, `true`, `'A'`, `"ok"` | Compare with a built-in value. |
| Enum Case | `.None`, `.Some(let x)`, `FileError.NotFound` | Test the Case, then its payload. |
| Binding | `let x`, `var x` | Accept and bind an arm-local name. |
| Unit / Tuple | `()`, `(let x,)`, `(let x, 0)` | Match the exact arity and elements. |
| Grouping | `(.Some(let x))` | The enclosed Pattern. |

A nested Case Pattern takes its expected Type from its position; a qualified Case Pattern's qualifier is a plain Container path without binding-set suffixes: its own and inherited Origin slots are bound from the matched Type, and its omitted enum arguments follow the Pattern rule of [§6.5.2](06-declarations-and-containers.md#652-case-references). Names, constants, Properties, calls and other expressions are not Patterns; compare with a value through a guard. Pattern testing calls no constructor, getter, conversion or user operator.

**Structural Patterns** (Case, Tuple, Unit, Literal) on a safe value reference (`ref/T`, `uniq/T` or a qualifying pair layer, [§13.5.5](13-operators-and-assignment.md#1355-follow)) without the needed structure select its referent, repeatedly ([§3.4.3](03-types-and-values.md#343-reference-path-selection)); Wildcard, Binding and grouping never do, and Object Semantics and raw pointers are never selected. Each position selects independently: below `ref` everything is shared, below `uniq` access is exclusive only if the Subject mode and every enclosing layer allow it, and a pair layer gives the weakest access over its cases.

Arity and payload count must match exactly. `(P)` groups and `(P,)` is a one-element Tuple: `.Some((let x, let y))` matches one Tuple payload, `.Pair(let x, let y)` two. Testing goes outside in, left to right, stops at the first mismatch and Copies or Moves nothing.

**Literal Patterns** are `bool`, integer, `char` and non-interpolated `string` literals. A negative integer Pattern is `-` and a literal, fitted after the sign (`-128` fits `i8`, `128` does not); `Wrapping<T>` takes literals that fit `T`. Characters compare by Unicode scalar value and strings by exact UTF-8 content. Floating-point values, `null`, interpolated strings and Ranges are not Patterns.

```kimi
enum Box {source}
    Value(uniq/Option<i32> during source)

match box@uniq
    .Value(.Some(let x)) => x@follow += 1   // x: uniq/i32
    _ => ()
match box
    .Value(.Some(let x)) => use(x)          // Shared Subject: x: ref/i32
    _ => ()
```

### 14.8.2. Binding scopes

`let name` and `var name` always match and create a non-reassignable or reassignable body local; `var` grants no access to the payload ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)). Names in one Pattern are distinct. `let _` and `var _` are invalid; `_name` is an ordinary binding. A name is visible only in its arm: as a candidate in the guard and as a separate local, with its own Binding Identity, in the body. The body cannot redeclare it, though a nested block may shadow it. Further Pattern forms are deferred ([§D.3](appendices/D-deferred-features.md#d3-enums-patterns-and-failure)).

```kimi
match pair                     // pair: (i32, i32)
    (let x, let y) if x == y => same(x)
    (let x, _) => other(x)
// (let x, let x) => ()        // Error: duplicate name, not an equality test.
```

### 14.8.3. Guards

Each Binding position designates a **Candidate Place**, initialized storage in the Subject or its referent. In the guard, the candidate name has Type `ref/T during g`, whatever the Subject mode, where `T` is the stored complete Type with its own Origins unchanged and `g` is a guard-local Origin that ends at guard cleanup. In the body the binding is `T`, `ref/T` or `uniq/T` for an owned, shared or exclusive path ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)). Reads and comparisons follow reference layers ([§3.5.3](03-types-and-values.md#353-value-read), [§13.4](13-operators-and-assignment.md#134-comparison-and-logical-operators)). Guard and body resolve with their own Types, and guard facts do not carry to the body.

A guard is one `bool` expression and a header expression ([§F.4.4](appendices/F-syntax-summary.md#f44-header-expressions-and-delimiter-regions)); there are no `let` conditions or guard chains. If the Pattern matches, the guard runs once. Then, on either result, the candidate references, guard temporaries and new Loans are cleaned up. On `true` the body locals are initialized from the Candidate Places and the body runs; on `false` the next arm is tried with the Subject unchanged.

**Candidates.** Whether a value obtained in the guard may be stored or used after it follows from the ordinary Origin and Loan checks against `g`: a value that depends on `g` cannot leave the guard, while a value read or copied out of the candidate keeps only the Origins of the stored value. Calls are judged by the callee's signature (its Origins and effect bounds), never by its body. Candidate names cannot be Moved, reassigned, exclusively borrowed or captured; a value read into an ordinary local may be captured under the ordinary rules.

```kimi
// Packet.Data stores Data; Packet.Link stores ref/Data during a.
// keep(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool
var saved = fallback@ref                                  // fallback: Data
match packet
    .Data(let d) if keep(saved@uniq, d) => ()               // Error: d's Origin ends at guard cleanup, but saved is used below
    .Link(let r) if keep(saved@uniq, r@follow@copy) => ()   // Valid: the copied reference keeps Origin a
    _ => ()
use(saved)
```

**Subject protection.** From the start of Pattern testing until guard cleanup completes, a shared Loan is live on the Subject Place and on every referent that Pattern testing reaches through a reference. Conflicts with this Loan are judged by the ordinary Place-overlap and call rules ([§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts), [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)); a guard may change only Places that do not overlap the protected ones, and such changes are not rolled back when the guard is false.

```kimi
// var state: State; var pair: (Option<i32>, i32)
match state
    .Idle if restart(state@uniq) => ()                // Error: state is protected during the guard
    _ => ()
match pair
    (.Some(let a), _) if bump(pair.1@uniq) => ()      // Error: the whole Subject is protected, Wildcard positions included
    _ => ()
```

If the guard or its cleanup does not complete normally, no body binding is initialized and no later arm runs. An ordinary transfer out of the guard follows the match cleanup rules ([§15.1.6](15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)) and the order for interrupted expressions ([§16.2.1](16-scope-exit-and-destruction.md#1621-cleanup-order)). A guard is no transfer target or barrier, but it cannot `yield` to its own match.

### 14.8.4. Coverage and unreachable Patterns

Exhaustiveness is proven only from **unguarded** arms. Guarded arms (even `if true`), guard logic, constant Subjects, a body of Type Never and a Never payload never supply coverage; every declared Case is possible.

A **whole-position Pattern** is a Wildcard, a Binding, `()` at a Unit position, or a Tuple Pattern of whole-position Patterns (grouping is transparent); a Literal or Case Pattern never is.

| Matched domain | Covered when |
| --- | --- |
| Any | one arm has a whole-position Pattern |
| `bool` / Unit | `true` and `false` / `()` appear |
| Enum | every Case has an arm whose payload elements are all whole-position |
| Tuple, integer, `char`, `string`, other | a whole-position Pattern; partial Patterns are not combined |

Literals compare by fitted value (`0`, `-0` and `0x00` coincide). The `bool` rule applies only to a `bool` Subject, not to `.Some(true)` with `.Some(false)`.

A warning is required when one earlier unguarded Pattern contains a later Pattern: a Wildcard or Binding contains anything at its position; equal fitted Literals and Unit contain themselves; same-Case or equal-arity Tuple Patterns contain another when each child does. Binding names, `let`/`var` and grouping are ignored, the later arm may be guarded, and earlier arms are not combined. The warning never removes an arm from checking or result collection, and optional lints never change acceptance.

```kimi
match flagOption          // Error: Some needs a whole-payload arm.
    .Some(true) => 1
    .Some(false) => 0
    .None => -1
match r                       // r: Result<i32, Never>
    .Ok(let v) => use(v)
    .Err(_) => ()             // Still required.
match value
    .Some(_) => 1
    .Some(0) => 2             // Warning: the earlier Pattern contains it.
    .None => 0
```

## 14.9. Result validation

After directive selection, three responsibilities apply. **Result-source collection** gathers every value supplied to a target. **Structural Completion** finds normal-completion candidates from syntax ([§14.9.3](#1493-reachability)). **Runtime Reachability** applies initialization, Move, Loans, refinement and cleanup to the same paths. Unreachable items are checked in the assumed-path state ([§14.10.3](#14103-unreachable-code)).

### 14.9.1. Result sources

| Source | Supplies |
| --- | --- |
| Explicit transfer | each self-targeted `return`, `yield` or `exit` operand, reachable or not; omitted: Unit |
| Single-item body | its value where [§14.2.2](#1422-evaluation-contexts-and-body-results) uses it; Unit from a statement or discarding body that can complete |
| Indented body | Unit on structural end arrival, where the owner uses it |
| Implicit completion | Unit from an omitted `else`, `for` exhaustion and the `while` false path |

Every body is assumed entered. A sequence with no structural continuation supplies no end Unit. Loop body ends are not loop results. Runtime Reachability never adds or removes sources or changes inferred Types.

```kimi
let conflict = loop
    continue
    exit 1
    exit "x"      // Error: unreachable results must also fit.
```

### 14.9.2. Target Result Type and Expression Type

The **Target Result Type** is found independently of source order:

1. Use the declared Type, the Type fixed by [§14.2.2](#1422-evaluation-contexts-and-body-results) or a fixed outer expectation.
2. Otherwise find one common Type of the independently typable sources ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)). Source Types are compared by their structural part only ([§15.6.1](15-ownership-and-lifetime-analysis.md#1561-origin-relations-and-well-formedness)): the common Type's Origins are local regions, and each source's fit to it yields ordinary Origin relations, never a Type mismatch. Never supplies no candidate. Sources that differ only in safe reference layers over one Type, or are literals of it ([§12.3](12-expressions.md#123-literal-typing)), have that common Type and are value-read ([§3.5.3](03-types-and-values.md#353-value-read)). A control-flow expression used as a call argument is classified by the Argument kinds rule of [§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries).
3. Propagate the Type to sources checkable against it (such as `0..3` beside `0..n`); literal defaults apply as [§10.2](10-overload-resolution-and-inference.md#102-common-adaptation) states.
4. Check every source. A call, anonymous function or empty literal that still lacks a Type needs an annotation.

Nested results propagate the same way, and a literal result is not fixed before outer constraints. A typed binding is never reinferred from later uses. Inference never searches combinations by rechecking bodies ([§10.8](10-overload-resolution-and-inference.md#108-generic-argument-inference)).

The **Expression Type** is the Target Result Type, or Never when every source (if any) is Never and Structural Completion has no normal candidate. Never does not excuse a mismatch or missing result, and Runtime Reachability never makes a Type Never. An anonymous function without a declared or expected result infers its return Type by these rules.

```kimi
let large: i64 = 10
let second = if ready => large else => 1   // i64; no early default to i32.
var x = 1
var y = 2
let r = if ready => x@ref else => y@ref   // r: ref/i32; its Origin covers both Loans
x = 3                                      // Error: the Loan of x is still live
use(r)
let a = loop => continue                   // Never
let b = loop
    continue
    exit 1                                 // i32: an unreachable source still counts.
```

### 14.9.3. Reachability

**Structural Completion** is conservative: no path is pruned by `bool` literals, condition values, constants, Pattern containment, Type contradictions or callee bodies, and divergence, Never calls and Abort supply no normal candidate.

| Construct | Paths |
| --- | --- |
| Sequence | evaluation order; continue only after normal completion |
| `if`, `require` | both true and false |
| `match` | every arm, both outcomes of each guard; no unmatched path |
| `and`, `or` | evaluating and skipping the right operand |
| `for`, `while` | an iteration and immediate completion |
| `loop` | only a self-targeted `exit` completes |
| Transfer | operand, then the target; nothing continues at the source |
| `defer`, local function | registered or declared, not run |

Structural Completion ignores cleanup. **Runtime Reachability** follows the same paths with the actual cleanup, which may block delivery, and checks that `require` failure bodies do not continue. `while true`, also with `(true)`, gets a warning that it keeps a static false path. Optimization never changes acceptance.

```kimi
func incomplete() -> i32
    if true => return 1          // Error: the false path supplies Unit.
var port: i32
while true
    port = tryBind()
    if port > 0 => exit
use(port)                        // Error: the false path need not initialize port.
let stopped: i32 = label work: do
    defer => loop => ()
    exit to work 1               // Type i32; the cleanup blocks delivery.
```

## 14.10. Type refinement

### 14.10.1. Refinable bindings and Effective Types

Runtime `is` tests refine local `let` bindings and non-reassignable parameters of object Type with struct Core targets, named bare or parenthesized. `var` bindings, Fields, Properties, indexing and calls are not refined, and facts do not pass to aliases.

The **Effective Type** of such a name is its declaration Type with the View Target replaced by the **Effective Core**, the most derived Core guaranteed there; a derived Core's Origin bindings come from Target completion ([§13.6.5](13-operators-and-assignment.md#1365-checked-casts-and-target-completion)). Everything else about the binding is unchanged. Reassignment ends its facts; member mutation does not; a complete payload update ends facts about old Field contents ([§15.7.3](15-ownership-and-lifetime-analysis.md#1573-storage-update-dependencies)).

Every value use of the name uses its Effective Type, including lookup, overloads, inference, explicit operations ([§13.5](13-operators-and-assignment.md#135-explicit-operations)) and receiver acquisition ([§7.3](07-functions-and-callable-values.md#73-receivers)); each overload candidate is fitted against it by the ordinary rules, without a retry with the declaration Type. The slot operations `@ref`, `@uniq` and `@raw` ([§13.5.6](13-operators-and-assignment.md#1356-borrow-and-reborrow), [§5.4](05-raw-pointers-and-unsafe-memory.md#54-addresses-and-conversions)) and captures use the declaration Type. Refinement never adds an implicit Move, and it never changes which layer an existing member Name selects ([§6.2.2](06-declarations-and-containers.md#622-inheritance-and-open-structures)).

```kimi
func handle(value: objref/Animal) -> ()
    let alias = value
    require value is Dog else => return
    let dog = value          // objref/Dog
    let slot = value@ref     // ref/(objref/Animal)
    alias.bark()             // Error: alias has no Dog guarantee.
```

### 14.10.2. Condition facts and joins

| Condition | True path | False path |
| --- | --- | --- |
| `x is T` | add `x is T` | keep |
| `x is not T` | keep | add `x is T` |
| `not C`, `(C)` | false / true path of `C` | true / false path of `C` |
| other | nothing new | nothing new |

In `A and B`, `B` is checked on `A`'s true path; the false paths join. `A or B` is the dual. This holds at every expression position. Facts are not tracked through stored Booleans or calls.

A fact `x is D` on Effective Core `S`, judged as in Target completion ([§13.6.5](13-operators-and-assignment.md#1365-checked-casts-and-target-completion)) and for every admitted binding of a generic body ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)), narrows `x` to `D` when `D` is proven strictly derived from `S`. When `D` is proven unrelated, no Dynamic Type remains: `x` is checked with its declaration Type on that reachable path, later tests add nothing there, and a warning is allowed. Otherwise nothing is added.

At a join, the most derived base guaranteed by every incoming path that still has possible Dynamic Types is kept; when no incoming path has a possible Dynamic Type, neither does the join. Only paths kept by Runtime Reachability ([§14.9.3](#1493-reachability)) join. `if` and `while` pass true facts to the body and false facts onward (an `else if` starts from the preceding false facts); `require` passes true facts to the following statements. Loop entries join every back edge; the result is the most refined stable solution. A `defer` body keeps the facts of its registration point ([§16.1.2](16-scope-exit-and-destruction.md#1612-deferred-evaluation-and-ownership)); functions and captures import none.

```kimi
// Dog and Cat derive from Animal; only Dog declares score() -> i32.
func afterDeadBranch(animal: objref/Animal) -> i32
    require animal is Dog else => return 0
    if animal is Cat => log("dead")   // animal is checked as Animal here.
    return animal.score()             // Valid: the contradicted path constrains nothing.
func insideContradiction(animal: objref/Animal) -> i32
    require animal is Dog else => return 0
    require animal is Cat else => return 1
    return animal.score()             // Error: animal is checked as Animal.
```

### 14.10.3. Unreachable code

An item that no Runtime Reachability path reaches is still checked, in a state built from assumed paths. On an assumed path, these Never forms are taken to complete normally: a transfer, `$abort` and a call whose result Type is Never continue after their operands are evaluated and before any cleanup, at their own lexical scope; a `loop` without a self-targeted `exit` continues with its iteration-entry state. Cleanup is checked the same way, so a transfer whose delivery a cleanup blocks is also assumed delivered. An item with at least one real incoming path uses only real paths; otherwise it uses the join of its assumed incoming paths, keeping only what holds on all of them. Loans that only an abandoned comparison needed end before the assumed continuation. All checks apply in this state: reading an uninitialized or Moved value, a second Move and a Loan conflict are errors, and Effective Types and condition facts carry over as on real paths. Assumed paths add no implicit result, no normal completion and no Runtime Reachability edge; explicit result sources in unreachable code still count ([§14.9.1](#1491-result-sources)).

```kimi
func scoreInBranch(animal: objref/Animal) -> i32
    if animal is Dog
        return 0
        return animal.score()   // Valid: unreachable, but keeps the Dog refinement.
    return 0
func scoreAfterReturn(animal: objref/Animal) -> i32
    return 0
    require animal is Dog else => return 1
    return animal.score()   // Valid: refined in the unreachable region.

var p: i32
label w: do
    defer => loop => ()
    exit to w          // Delivery is blocked by the cleanup.
use(p)                 // Error: p is uninitialized on the assumed path.

consume(x@move)
fail()                 // fail: () -> Never
consume(x@move)        // Error: x was Moved.
```

## 14.11. `require`

`require Expression else Body` evaluates its `bool` condition once ([§14.2.3](#1423-conditions)), continues when it is true and runs its failure body when it is false. The `else` is on the condition's last line or the next line, aligned with `require`. `require` has no result, label, target or barrier; it is not an `if`.

The failure body is always assumed entered, and a `false` condition keeps the static true path. No path through the failure body, including cleanup, may continue normally after the statement. Outward transfers, Never calls, divergence and Abort satisfy this; nothing is inserted implicitly.

```kimi
let answer = if enabled
    require ready else => yield 0       // Targets the outer if.
    yield compute()
else => 0
defer
    require needsCleanup else => exit   // Ends this deferred body.
    cleanup()
require true else => logFailure()        // Error: normal continuation.
require ready else => while true => ()   // Error: the static false path completes.
require ready else => loop => ()         // Valid.
```
