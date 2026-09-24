# 14. Control flow

[Specification index](../SPEC.md)

The control-flow expressions are `if`, `match`, `for`, `while`, `loop`, `do` and the transfers `return`, `exit`, `continue` and `yield`. `unsafe`, `defer` and `require` are statements; they cannot be initializers, arguments or expression operands. Test-only bodies additionally admit the standalone verification items `$expect` and `$require`, whose placement, evaluation and continuation rules are in [§17.5](17-failure-handling.md#175-test-verification-operations).

| Concept | Role |
| --- | --- |
| Delimiter region | Determines syntactic nesting and body and branch joins (§2.2). |
| Body scope | Determines local name scope and cleanup. |
| Transfer target | Receives a `return`, `exit`, `continue` or `yield`. |
| Lookup barrier | Stops transfer lookup; functions and deferred bodies have the restrictions of §14.5.2. |
| Evaluation Context | Determines whether a value is used or discarded (§14.2). |
| Completion | Describes normal completion or a transfer to a resolved target. |

Parentheses create a delimiter region, not a body scope, transfer target or lookup barrier. A do expression receives named exits but does not stop other transfer lookup. An **Iteration Construct** is a `for`, `while` or `loop`, and an **iteration** is one execution of its body. A **selection** is one `if` chain or `match`.

## 14.1. Completions

**Normal completion**, written `Normal(result)`, returns control to the evaluator; a statement's normal Completion uses Unit without making the statement an expression. An **abrupt completion** is `Return(target, result)`, `Exit(target, result)`, `Continue(target)` or `Yield(target, result)`. The transfer expression itself does not complete normally, even when its target subsequently does.

Omitted `return`, `exit` and `yield` operands mean `()`; `continue` has no result. After the required [Scope Exit](16-scope-exit-and-destruction.md#162-scope-exit-destruction), a construct receives its own valid transfer or propagates one addressed to an outer target:

| Target | Received Completion | Action |
| --- | --- | --- |
| Iteration or do expression | `Exit(self, result)` | Complete the expression with that result. |
| Selection | `Yield(self, result)` | Complete the selection with that result. |
| Deferred body | `Exit(self, ())` | Finish this body's cleanup and resume the pending Scope Exit. |
| Iteration | `Continue(self)` | Continue at the construct's next iteration point. |
| Function | `Return(self, result)` | Deliver the secured result to the caller. |

**Divergence** never finishes and produces no Completion. **Abort** ends the process without delivering a Completion or performing ordinary Scope Exit. Completion, divergence and Abort are distinct evaluation outcomes. Never is a Type, not a Completion variant, and is inferred under §14.9, not directly from Runtime Reachability.

## 14.2. Blocks and evaluation contexts

All executable bodies use the common **Body**:

```text
Body := "=>" (Expression | Statement)
      | NEWLINE INDENT ItemList DEDENT
```

A **single-item body** contains one expression or statement starting on the header's ending physical line; it may span more lines through ordinary expression continuation or a match arm list. An **indented body** (also called a Block body) contains declarations, expressions, statements and permitted compile-time directives, evaluated in order. Its direct expressions, including the last, are discarded, and structural arrival at its end supplies Unit where the owner uses the body result. For an iteration body, completion instead starts the next iteration.

Both forms apply to selection clauses and arms, iterations, do expressions, `unsafe`, `defer`, `require` failure bodies, functions, anonymous functions and Closures, specializations, accessors, `init` and `deinit`. Declarations and directives require the indented form. Each position keeps its declaration restrictions; explicit function Constraints stay at the start of an indented function body (§7.4). Declaration Containers and match arm lists are not executable bodies. Bodyless declarations and standard accessors keep their own rules.

| Concept | Question |
| --- | --- |
| Evaluation Context | Is this position's value used or discarded? |
| Expected Type | What Type does the surrounding position require? |
| Target Result Type | What Type constrains the results supplied to this target? |
| Expression Type | What Type does the checked expression have? |

Initializers, arguments, conditions, match and iteration subjects, and operator and transfer operands use **Value Context**. Standalone expressions and direct expressions in indented bodies use **Discard Context**. Parentheses preserve both the context and the expectation. An expected Unit, later non-use, reachability and optimization never turn a Value Context into a Discard Context.

Discarding a general expression destroys its result at the normal lifetime; it is not an implicit conversion to Unit. All local operations, acquisition, ownership, Loans and required warnings, including warnings for discarded `Result` values, are still checked. Body expressions follow these rules, fixed before the body is checked:

| Owner | Single-item expression and normal completion | Explicit result transfer |
| --- | --- | --- |
| Function / `get` | If the return Type is already fixed as Unit, discard and complete with Unit; otherwise use Value Context and return the value. | `return` |
| `if` / `match` / `do` | Inherit the owner's context: use the value in Value Context; discard it and complete with Unit in Discard Context. | `yield` for selections; named `exit` for `do` |
| `for` / `while` / `loop` | Discard and continue the iteration. | `exit` |
| `unsafe` / `defer` | Discard and complete the body with Unit; `defer` executes later during cleanup. | `exit` for `defer`; `unsafe` has no target |
| `set` / `init` / `deinit` | Discard and complete the body with Unit. | `return` |
| `require` failure | Discard; normal continuation after `require`, including after cleanup, is forbidden. | No target of its own |

Statements in single-item bodies supply Unit only if they structurally complete normally. Values discarded directly inside a body need no common Type, but values supplied to a target must fit its Target Result Type, even when the target's result is discarded.

The Target Result Type is fixed as Unit for `for`, `while`, `defer`, `set`, `init`, `deinit`, and discarded `if`/`match`/`do`/`loop` expressions. Explicit transfers must still fit it: `return 123` in a Unit function and `loop => exit 1` in Discard Context are errors. A value-used loop takes its result from self-targeted exits, never from its body end. Receiving a transfer does not additionally supply an implicit body result.

```kimi
if ready => visited.insert(id)        // A bool result may be discarded.
let bad: i32 = if ready
    compute()                   // Discarded; implicit Unit does not fit i32.
else => 0
```

Changing a body's form is a semantic refactoring: it must preserve results, targets, scopes and destruction order, adding `return`, `yield` or a named `exit` where needed. Formatters keep body forms (§2.2).

### 14.2.1. Nonempty executable blocks

An indented executable body requires at least one complete source item; blank lines and comments do not count, and a directive counts only with its required syntax. An absent body is diagnosed before the end of file or before an item at the same or shallower indentation, and that item is not absorbed. Write `()` to do nothing. A single-item body already requires one complete expression or statement; local declarations are not single items in this form.

```kimi
defer
    // Error: no source item.
nextOperation()

defer => () // Explicit no-op body.
```

### 14.2.2. Conditional compilation and results

Source nonemptiness is checked before directive selection, so a complete directive counts even when it selects no executable items. After selection, the normal Type, transfer and result checks apply. A selected match arm list must still contain at least one arm; Declaration Container emptiness follows its own rules.

```kimi
defer
    #if windows
        closeHandle()
// Valid source body even if the directive selects nothing.

let value: i32 = if ready
    #switch
        #case windows
            yield 1
        #case _
            yield 2
else => 0
// Directives are allowed in this indented body, even inside an initializer.
```

A selection that removes a needed transfer may expose structural end arrival, and therefore a Unit result that fails the target's Type; nonemptiness does not exempt result checking.

### 14.2.3. Conditions and temporary lifetimes

`if`, `else if`, `while`, `require` and match guards use a `bool` expression. There is no `if let ...`, `while (let ...)`, `var` condition or condition Pattern binding. Nested constructs inside the condition expression keep their ordinary Body declaration rules.

Each test evaluates its condition once, secures the `bool` result, destroys the remaining condition temporaries in reverse creation order, ends guard-local temporary Loans, and then branches using the secured `bool`. Cleanup effects update state but do not reevaluate the `bool`. A transfer, divergence or Abort during evaluation or cleanup prevents the test's continuation on that path. Match subjects, iterators and explicit bindings keep their own owning scopes, and Moved values follow their destination's lifetime.

```kimi
if (test: do
    let ready = check() // check returns bool; this declaration belongs to the do body.
    exit to test: ready
)
    work()

let guard = registry.lock()
if guard.contains(id) => work() // Keep a needed guard outside the condition.
```

To acquire a new local on every test, use `loop` with a declaration and `require`. Parentheses change neither transfer lookup nor the Evaluation Context. Header grouping and continuation follow §2.2.

### 14.2.4. Explicit discard

`_ = Expression` is a dedicated executable statement, recognized by reserved `_` followed by `=`. It is not assignment, permits no compound form, and cannot be nested as an expression. Normal completion supplies Unit. Existing Body and continuation rules apply.

Check the right side in Value Context without an expected Type, as for an unannotated initializer, but introduce no local or lifetime extension. Evaluate once, acquire by bare acquisition or transfer, and destroy the result at statement temporary cleanup: `_ = x@move` destroys a Non-Copy Place early, a bare Non-Copy Place is an error, and `_ = x@ref` discards a borrow without effect. A non-completing operand supplies no result to discard. Changing `expr` to `_ = expr` changes Context and may require a common branch Type.

Explicit discard suppresses only warnings for discarding that result (§17.4), not internal discards, unintended Unit inference inside the operand, Type/ownership errors or unrelated diagnostics.

```kimi
_ = prepare()     // Ignore the entire Result, including Err.
_ = try prepare() // Propagate failure; explicitly discard success.
_ = resource@move // Transfer and destroy a Non-Copy value early.
// _ = resource   // Error: a bare Non-Copy Place is not transferred.
_ = .None         // Error without enough Type information.
_ = loop => exit  // Valid Unit result.
_ = do
    prepare()     // This independent implicit discard still warns.
consume(_ = prepare()) // Error: not an expression.
_ += prepare()         // Error: no compound form.
```

Ignoring Result is permitted; its error payload is destroyed normally and execution continues. This neither catches nor suppresses Abort.

## 14.3. Block constructs

| Construct | Category | Execution |
| --- | --- | --- |
| `do` / `Label: do` | Expression | Executes immediately; receives a named `exit` when labeled. |
| `unsafe` | Statement | Executes immediately with lexical unsafe permission. |
| `defer` | Statement | Registers immediately; executes at Scope Exit (§16.1). |

### 14.3.1. Body forms and scopes

Both Body forms create an independent body scope; there is no standalone indentation-only body. `unsafe` and `defer` statements occur in executable scopes, not directly in Declaration Containers, and their body scopes do not make them expressions.

```kimi
defer => unsafe => releaseRaw(pointer)
defer => exit () // Valid: end the deferred body with Unit.
defer => return  // Error: cannot return from the outer function.
```

`unsafe/T` remains Type Semantics syntax and `unsafe func` a modifier. In statement position, `unsafe` introduces a Body, not a colon-delimited label. Declaration and lexical role rules remain in force.

### 14.3.2. Do expressions

`do Body` executes once and uses the result rules of §14.2. An optional label permits a self-targeted `exit to Label`. An unlabeled `exit` skips do expressions, so an unlabeled `do` with an indented body cannot supply a non-Unit result of its own. Outward transfers and divergence are permitted.

```kimi
let result = work: do
    if cached() => exit to work: cachedValue()
    exit to work: compute()

let direct = do => compute()
do
    let resource = openResource() // Only the complete word open is reserved.
    defer => close(resource)
    use(resource)

let block = readBlock() // Ordinary identifier; no keyword interpretation.
```

`do` is reserved, while `block` is an ordinary Name. A header expression containing a do expression needs grouping under §2.2.

### 14.3.3. Unsafe block

An **Unsafe Block** is an `unsafe` statement with a Body. It executes immediately with lexical permission for unsafe operations and has no transfer target or lookup barrier. To supply a value outward, use `return`, `yield` or a named `exit` inside the body; `let x = unsafe => readRaw(pointer)` is invalid because `unsafe` is a statement.

The permission reaches nested deferred bodies but does not cross Function Boundaries. Type, ownership and Loan checks remain mandatory. An `unsafe func` declaration does not itself grant permission inside the function (§7.5).

```kimi
unsafe => releaseRaw(pointer)
unsafe
    releaseRaw(pointer)
    updateState()
```

## 14.4. Labels

An optional `Label:` may prefix `if`, `match`, `for`, `while`, `loop` or `do` on the same physical line. Labels use a namespace separate from variables and Types. Equal label names whose active scopes overlap in one function are rejected.

A label is active only inside its construct's bodies, not in its own conditions, iterable, subject or guards. A transfer may target only an enclosing construct in the same function, never a sibling, inner or finished construct. A label names a construct, not an instruction address.

Group a labeled expression where its colon would conflict with a named argument or Dictionary separator. A labeled `return`/`exit`/`yield` operand must be parenthesized, whether or not the transfer itself names a target.

```kimi
consume(value: if ready => 1 else => 0)       // Named argument.
consume((choice: if ready => 1 else => 0))    // Labeled expression.
return (work: do => compute())
yield to outer: (inner: if ready => 1 else => 0)
```

## 14.5. Control transfers

### 14.5.1. Syntax and operands

```text
return [Expression]
exit [Expression] | exit to Label [: Expression]
continue [to Label]
yield [Expression] | yield to Label [: Expression]
```

Brackets mark optional syntax. Omitted `return`/`exit`/`yield` values mean `()` and are checked against the target's result Type. `for`, `while` and `defer` accept Unit expressions as `exit` operands, not non-Unit values. `continue` has no value, and `return` has no named form.

The operand, and `to Label` when present, start on the transfer keyword's physical line. A named value follows `:`; the colon is omitted when the value is omitted. Normal continuation is allowed after the expression starts. `to` is contextual only immediately after `exit`, `continue` or `yield`; write `exit (to)` to use a variable of that name. Postfix `value to Label` and `value{Label}` are not transfer syntax.

```kimi
exit to search: score(item)
exit to outer: -1
continue to outer
yield to choice: match mode
    .Fast => 1
    _ => 0
```

Transfers have Type Never. Their operands are still checked against their own target, and result acquisition and delivery follow §16.2.2.

### 14.5.2. Target lookup

| Transfer | Unlabeled target | Named target | Lookup barrier |
| --- | --- | --- | --- |
| `return` | Nearest function | None | `defer` |
| `exit` | Nearest iteration or `defer` | Named iteration or `do` | Function; named lookup also stops at `defer` |
| `continue` | Nearest iteration | Named iteration | Function and `defer` |
| `yield` | Nearest `if` or `match` | Named `if` or `match` | Function and `defer` |

The target is resolved first; then context, operand and Type are checked, and lookup never searches farther outward after a mismatch. Missing or wrong-kind targets are errors. An unlabeled `yield` to a discarded selection is an error even without a value; the diagnostic should suggest naming the intended target. Named yields still fit the target's result Type, which is Unit in Discard Context. This extra check prevents a conditional `yield` from accidentally ending only its nearest inner `if`; it neither alters Type fitting nor makes Discard Context transparent to lookup.

A construct acts as a target or barrier only inside its bodies. `unsafe` and `require` create neither. A do expression passes through all transfers except its named `exit`; selections pass `return`/`exit`/`continue`, and iterations pass `yield`. No outward transfer crosses a function or deferred body.

### 14.5.3. Function boundaries

Function Boundaries include named functions, methods, anonymous functions, Closures, explicit specializations, accessors, `init` and `deinit`. A function declared inside `defer` keeps its own return target. A constructor's Unit control result is distinct from the owned constructed value, and a `return` in `deinit` does not skip automatic field destruction. Named functions keep their declared or default Unit result even if their bodies never finish (§7.1).

### 14.5.4. Label and nesting examples

```kimi
let result = choice: if enabled
    if cached() => yield to choice: cachedValue()
    yield compute()
else => 0
// Omitting 'to choice' in the conditional yield targets the discarded inner if: error.

outer: for row in rows
    for cell in row
        if skipRow(cell) => continue to outer
        if done(cell) => exit to outer
```

## 14.6. Iteration constructs

Each iteration has a fresh body scope. Its normal body result is discarded, and its scope is cleaned up before continuing.

### 14.6.1. `for` and `while`

`for` acquires its iterable once and then obtains successive elements; `while` evaluates its `bool` condition before each iteration. Both complete with Unit on exhaustion or a false condition, or on a self-targeted `exit`, whose operand must fit Unit. The body end and a self-targeted `continue` request the next element or reevaluate the condition.

A `for` binding is one Name or `_`, or a Tuple of those slots, not a general Pattern or nested decomposition. Named slots must be distinct; each `_` is a separate unnamed immutable iteration binding and cannot be referenced or captured. Conditions follow §14.2.3, including cleanup before branching and the ban on condition-binding syntax.

```kimi
for (key, value) in dictionary => process(key, value)
while ready => process()
```

`while` keeps a static false-condition path, even for `true`; use `loop` for unconditional repetition (§14.9.2).

### 14.6.2. Iteration protocol and acquisition

The recognized Kimi Iterable and Iterator Contracts define `for` iteration. The iterable expression `E` is evaluated once and acquired into a hidden iterable local under the [subject rule](15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime): an owned Place is shared-borrowed, even when its Type is Copy; a borrow value is copied or shared-reborrowed; a Temporary Value, including the result of `E@move`, is acquired by value. Its consuming `iterate` mapping is invoked once on that local, consuming a borrowed local's Copy reference, to obtain a hidden iterator local. `Iterator.next` is then invoked repeatedly with a short exclusive reborrow of that iterator; a `Some` payload supplies the next element, and `None` terminates the loop. Missing or ambiguous conformances are errors; there is no method-name duck typing or fallback protocol.

~~~text
iterable := acquire(E)
iterator := Iterable.iterate(consume iterable)
repeat:
    step := Iterator.next(exclusive reborrow of iterator)
    if step is None: finish
    acquire Some payload into fresh iteration bindings
    execute body
    clean up iteration bindings before requesting the next step
clean up iterator on normal exit and ordinary transfers
~~~

Each `for` binding is an immutable `let` binding scoped to that iteration's body; there is no implicit `var` form. Payload acquisition is Copy for Copy Types and Move otherwise. Parenthesized bindings require a Tuple with exactly that many elements and acquire its components left to right, and named slots must be distinct. When the yielded element is a shared reference to a Tuple (`ref/(A, B) during source`, as bare iteration over an owned array yields), each component is acquired under the [match rules for borrowed Subjects](15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime): a Copy component is copied and a Non-Copy component binds as `ref/T during source`. Neither key/value member names nor an arbitrary deconstruction method supplies this Tuple.

The receiver Loan of `next` ends before the loop body. Results may keep existing external dependencies but cannot borrow that exclusive receiver or iterator-owned storage; lending iteration is deferred.

| Iterable | Yielded value and acquisition |
| --- | --- |
| `ref/Array<T>`, `ref/[N of T]` | `ref/T during source`, as the Slice iteration of `values[..]` |
| `ref/Dictionary<K, V>` | `(ref/K during source, ref/V during source)` pairs in insertion order, through the Kimi shared pair iterator |
| `ref/Slice<T>`, `ref/ResolvedRange` | The Copy value is read and iterated as below; the local keeps no Loan of its own |
| Array or fixed array under `owner` Semantics | Elements consumed as `T` |
| `ResolvedRange` | `isize`; an unresolved `Range` is not Iterable |
| `Slice` | `ref/T during source`, even for Copy elements |
| Dictionary under `owner` Semantics | `(K, V)` pairs consumed in insertion order |

Bare iteration over an owned collection Place therefore borrows it and yields shared references, while `for item in values@move` consumes the collection and yields owned elements. A consumed source remains unavailable until validly reinitialized. A user Type offers shared iteration through a member that returns an Iterable view, such as a Slice; a shared Iterable requirement is a design boundary ([Appendix D](appendices/D-deferred-features.md#d1-enum-and-pattern-extensions)).

```kimi
for item in items        // Shared iteration; items remains usable.
    inspect(item)
for item in items@move   // Consuming iteration.
    store(item@move)
``` Source Loans are kept while the iterator or escaped yielded references need them; overlapping mutation is rejected, and nonconflicting mutation is allowed under the ordinary Loan rules. See [ranges](04-arrays-indexing-and-slices.md#463-range-and-resolvedrange) and [Slice iteration](04-arrays-indexing-and-slices.md#467-slice-iteration-and-nested-origins).

Body fall-through and `continue` clean up the current bindings before calling `next` again; `exit`, `return` and outer transfers also clean up the iterator and its unyielded owned elements. The protocol adds no cleanup guarantee on Abort and no rollback of prior Moves.

Unnamed slots follow named acquisition, dependencies and lifetime. Do not skip acquisition or destroy their values before the body. Binding a Result is not expression discard. Binding shape determines the unit: `for _ in pairs` acquires one Tuple; `for (_, _) in pairs` acquires its components separately. Tuple components and separate bindings both clean up last-to-first (§16.2–3), after body locals and defers. Borrowed bindings never destroy referents. This differs from Pattern wildcards and explicit-discard temporary cleanup.

```kimi
// n: isize, n >= 0. Range itself is not Iterable.
for _ in (0..n).resolve(n) => tick()
for (key, _) in pairs => use(key)
```

### 14.6.3. `loop`

`loop` repeats unconditionally. The body end and a self-targeted `continue` restart its body after cleanup. Only a self-targeted `exit` supplies its normal result; other transfers supply results to their own targets.

In Value Context, the loop's result is inferred from all self-targeted exits under §14.9; in Discard Context its Target Result Type is Unit. A body end is never a loop result source.

```kimi
let found: i32 = search: loop
    for item in items[..]
        if accepts(item) => exit to search: score(item)
    exit -1
// score returns i32. An unnamed exit inside for would have to fit Unit.

let once = loop => exit 1 // Value Context: result 1.
loop => exit 1            // Error: discarded loop requires Unit exits.
loop => work()            // Repeat work, discarding its result.
```

## 14.7. Conditional expressions and yield

### 14.7.1. Branch results

Each `if` clause or match arm chooses its Body form independently. Normal results, discarded values and self-targeted yields follow the common rules of §14.2 and §14.9.

```kimi
let value = if ready
    prepare()
    yield 1
else => 0

match command@move
    .Put(let key, let value) => table.insert(key@move, value@move) // Discard Option<V>.
    .Clear => table.clear()                                        // Unit.
    _ => ()
```

### 14.7.2. `if`

`if` tests its conditions in order and executes the first true clause, or the `else` clause if none is true. An `else if` chain is one selection. An omitted `else` is exactly `else => ()`, including its Unit result source in Value Context.

```kimi
if ready => 1                 // Allowed: discard the value.
let bad = if ready => 1        // Error: integer and omitted-else Unit conflict.
let unit: () = if ready => ()  // Valid, including the omitted else.
```

Condition evaluation follows §14.2.3; clause joins and nested-`if` grouping follow §2.2.

### 14.7.3. Nested `yield` targets

`yield` ends its resolved selection, not a function or iteration. It can cross an iteration but not a function or deferred body. For early completion of a discarded selection, name the target and supply Unit. A conditional transfer should name an outer selection when the nearest `if` is not the intended target (§14.5.2).

```kimi
let result = selection: if enabled
    for item in items[..]
        if accepts(item) => yield to selection: score(item)
    yield -1
else => 0

action: match event@ref
    .Save
        if readOnly => yield to action
        save()
    _ => ()
```

## 14.8. Match expressions and patterns

The subject is evaluated and acquired once. Arms are tried in source order, and the first arm whose Pattern matches and whose optional `bool` guard is true is selected. Only that arm's body executes; there is no fall-through to another arm. Each arm has its own Body, result rules and binding scope.

**Match is exhaustive in every Evaluation Context and body form.** Intentionally ignored values are handled with `_ => ()` or suitable Case-specific arms. A selected arm list must be nonempty. Coverage uses only the conservative proof rules of §14.8.4.

```kimi
func positiveOrZero(value: Option<i32>) -> i32
    return match value
        .Some(let n) if n > 0 => n
        .Some(_)
            log("non-positive")
            yield 0
        .None => 0
```

### 14.8.1. Patterns

Patterns are dedicated syntax, not general expressions. Initially they occur only in runtime match arms, not in declarations, parameters, `for`, `if`, `while`, `require` or compile-time `#switch`.

| Pattern | Example | Meaning |
| --- | --- | --- |
| Wildcard | `_` | Accept without testing or acquiring the value |
| Literal | `0`, `-1`, `true`, `'A'`, `"ok"` | Built-in value comparison |
| Enum Case | `.None`, `.Some(let x)`, `FileError.NotFound` | Test the Case and recursively match its payload |
| Binding | `let x`, `var x` | Accept and introduce an arm-local binding |
| Unit / Tuple | `()`, `(let x,)`, `(let x, 0)` | Match the exact arity and elements |
| Grouping | `(.Some(let x))` | Preserve the enclosed Pattern |

```kimi
// result: Result<Option<User>, Error>
match result
    .Ok(let option)
        match option
            .Some(let user) => use(user)
            .None => useDefault()
    .Err(let error) => report(error)
```

Each nested Case's expected Type comes from the payload Type at its position, using [Case resolution](06-declarations-and-containers.md#632-case-construction-and-resolution). Case qualifiers are plain Container paths: the enum's own and inherited Origin slots are bound from the matched Type, without binding-set suffixes, and outer Type arguments must already be known (§9.6.1). Complete Types inside generic arguments keep their Origins, and parenthesized bound qualifiers cannot bypass the Case restrictions.

Bare names, constants, Properties, calls and arbitrary expressions are invalid Patterns. Use `let x` to bind, `.Some(...)` or `Option<T>.Some(...)` for a Case, and `let x if x == expected` for comparison with an existing value; a misspelling never becomes a catch-all. Pattern execution invokes no constructor, getter, conversion or user-defined operator.

**Structural Patterns.** Case, Tuple, Unit and Literal Patterns are structural Patterns. At each position they inspect an owned value directly, or implicitly dereference `ref/T` at most once and inspect the immediate referent with shared access if its Type supports that Pattern. Grouping adds no dereference, and Wildcard and Binding never dereference. Object Semantics and raw pointers are not implicitly dereferenced. Type checking uses the original matched Type and this rule, not a binding's shared-reading result Type.

Each child payload or Tuple position applies the rule independently. Once a path passes through a borrow, its descendants keep shared access. Valid nested Types such as `ref/ref/T` and `ref/uniq/T` still have a reference layer after one dereference and cannot take a structural Pattern at that position. Types are never flattened, and dereferencing is never repeated until a Pattern fits.

A position containing `uniq/T` permits only Wildcard or Binding, optionally grouped. At the root, write `match value@ref`; for nested payloads, bind first and inspect in an inner match. There is no Pattern-local `@ref` syntax.

```kimi
enum Box {source}
    Value(uniq/Option<i32> during source)

match box
    .Value(let value)
        match value@ref
            .Some(let x) => use(x)
            .None => ()
```

On this owned path, `value` acquires the exclusive reference by Move, and the inner match creates a shared Reborrow; `.Value(.Some(let x))` is invalid. On a shared path, the body binding already receives a shared Reborrow under the [acquisition rules](15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime). Wildcard and Binding also accept Types that support no structural Pattern, and binding a reference value does not acquire its owned referent.

Tuple arity and Case payload count must match exactly; write `_` for each ignored element. Omitted, named, Rest and shorthand fields are not accepted. `(P)` groups, `(P,)` is a singleton Tuple, and `()` is Unit; `.Some((let x, let y))` matches one Tuple payload, while `.Pair(let x, let y)` matches two payloads. Structure is tested from the outside inward and left to right among siblings, stopping at the first mismatch, without Copying or Moving payloads.

**Literal Patterns** support `bool`, integers, `char`, and non-interpolated ordinary or raw `string` literals. A negative integer is `-` followed by an integer literal; the sign applies to the mathematical value before fitting to the matched integer Type, so `-128` fits `i8` while `128` and `-129` do not. Lexical magnitude limits apply, and there is no runtime conversion.

Booleans compare by value, characters by Unicode scalar value, and strings by exact UTF-8 content, without normalization, case folding or locale rules. Comparison neither consumes the value nor constructs an owned string at runtime. Coverage uses the fitted value, not the source spelling: integer `0`, `-0` and `0x00` coincide, as do Character and String literals that are equal after escape processing. Invalid Types, arities and out-of-range literals are rejected before coverage analysis. Floating-point values, `null`, interpolated strings, Ranges and arbitrary constant expressions are not Literal Patterns; use a guard for additional conditions.

### 14.8.2. Binding scopes

`let name` and `var name` are irrefutable at their position. They create non-reassignable and reassignable body locals respectively; `var` grants no mutation of, or exclusive access to, the original payload. Names within one Pattern must be unique: `(let x, let x)` is an error, not an equality test. `let _` and `var _` are invalid, while `_name` is an ordinary binding with acquisition and possible destruction responsibility.

A Pattern name is visible only in its own arm: as a candidate name in the guard and as a separate local in the body. It is not visible in the subject, in other arms, after the match, or for resolving its Pattern's Types and Case names. Ordinary shadowing and local redeclaration rules apply, and a single-item body also has an arm-local scope. Candidate and body names have distinct Binding Identities, so they may have different Types and acquisition effects: [guard reading](#1483-guards) determines the former and [selected acquisition](15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime) the latter.

Pattern body bindings and the immediate arm body share one declaration space: the body cannot redeclare a Pattern binding name, although an explicitly nested block may shadow it.

Binding Type annotations, `name @ Pattern`, and an outer `let` applying to a whole nested Pattern are not introduced. Other Pattern extensions remain [deferred](appendices/D-deferred-features.md#d1-enum-and-pattern-extensions).

### 14.8.3. Guards

Each Binding position has an internal **Candidate Place** designating initialized storage in the Subject or its referent. This Access Designator creates no new storage, value or owning local. In a guard, its candidate name performs **shared reading**: the Copy, shared Borrow or Reborrow selected by the [shared element-read rules](04-arrays-indexing-and-slices.md#466-slice-operations-and-element-results), without invoking an actual getter. It never reads an uninitialized body local.

| Candidate's stored Type | Guard read Type | Body Type on an owned path |
| --- | --- | --- |
| `i32` | `i32` (Copy) | `i32` |
| Non-Copy `Data` | `ref/Data` | `Data` |
| `obj/User` | `objref/User` | `obj/User` |
| `ref/T` | `ref/T` (Copy) | `ref/T` |
| `uniq/T` | `ref/T` (shared Reborrow) | `uniq/T` |

On a shared path, the body binding also uses the shared-reading Type. The table omits Origins; complete Types preserve them and their Loans. Each scope resolves its expressions and overloads with its own Types. Guard lookup is not retried with the body Type, and guard refinement facts do not transfer automatically to the body's distinct Identity.

A guarded arm proceeds as follows:

1. On a Pattern mismatch, the guard is skipped and the next arm is tried.
2. On a match, the candidate names are exposed in the guard scope and the guard is evaluated once.
3. On `true`, guard temporaries and newly created Loans are cleaned up; then the body locals are initialized from the original Candidate Places, not from the guard's read results, and the body runs.
4. On `false`, the same cleanup runs, the Subject's values are preserved, and the next arm is tried.

Cleanup must finish normally before either continuation.

```kimi
// Packet.Data stores Non-Copy Data; accepts takes ref/Data, consume and recover take Data.
match packet@move
    .Data(let data) if accepts(data) => consume(data@move)
    .Data(let data) => recover(data@move)
    .End => ()
```

Here the first `data` is `ref/Data` in the guard and `Data` in the body; writing `data@ref` in the guard copies that shared reference.

```kimi
func same(a: ref/string, b: ref/string) -> bool => a == b

match "hello"
    let text if same(text, "hello") => Console.writeLine(text)
    _ => ()
```

The guard reads `text` as `ref/string`, and the literal argument is also borrowed for the call. After guard cleanup, the selected body acquires the owned string from the Subject. Comparing `text == "hello"` directly would mix a borrow with an owned value, which built-in comparisons do not adapt (§13.4).

**Guard syntax.** A guard is one expression whose normal result is `bool`. Parentheses are optional, and `and`, `or` and `not` keep their ordinary semantics. There are no `let` conditions, comma-separated condition lists or guard chains. The arm's Body start, either `=>` or the indented body, ends the guard. Nested body-bearing expressions in a guard require grouping under §2.2.

**Candidate restrictions.** Candidate names, including `var` candidates, cannot be Moved or reassigned, create exclusive borrows or be captured. Candidate Places themselves cannot be returned or stored as values. Whether a shared-read result may be returned or stored depends on its transitive Origin and Loan dependencies and on the destination, not on Copyability alone:

| Shared-read result | Escape from the guard |
| --- | --- |
| Copy value without Candidate or Subject lifetime dependence | Permitted by the ordinary rules |
| Copy of a stored reference | Permitted when the existing Origins and Loans and the destination allow |
| New Borrow/Reborrow for candidate reading, or any value depending on it | Forbidden; its Loan must end inside the guard |
| Copy aggregate with an existing Subject dependency | Its Origins and Loans are checked; it cannot outlive the Subject |

Copying a stored value keeps its existing dependencies; reading its candidate adds no new dependency itself. These rules apply transitively through aliases, retained values and callees.

Although ordinary capture acquires values by Binding Identity, a candidate Identity is never an allowed capture source, explicitly or implicitly, even with a Copy read Type. A value read into a separate ordinary local or parameter may be saved or captured under the table and the normal rules. Thus `func [x] () => use(x)` directly capturing an `i32` candidate is invalid, while passing its read value to `saveCopy(x)` may allow the callee to store it. There is no capture spelling such as `@copy`.

**Subject protection.** From Pattern testing through guard cleanup, the Subject and traversed referents are protected with shared access: aliases and callees cannot change or consume them in a way that invalidates the Case or candidate positions. Independent side effects are allowed and are not rolled back on a false guard.

If guard evaluation or cleanup does not complete normally, no body binding is ever initialized and no later arm runs; payloads remain unacquired in the Subject or in borrowed storage. An ordinary transfer out of the guard follows the [match cleanup rules](15-ownership-and-lifetime-analysis.md#1516-match-acquisition-and-lifetime), and borrowed referents are not destroyed. Abort does not unwind, and divergence performs no subsequent acquisition or cleanup. A guard creates no transfer target or lookup barrier, but cannot `yield` to the match whose arm is still being selected; an inner selection may receive its own `yield`.

### 14.8.4. Coverage and unreachable Patterns

Exhaustiveness is proven using only the following limited rules over **unguarded** Patterns. Guarded arms, even `if true`, contribute no coverage. Guard logic, constant subjects, call results and a body's Never Type never supply missing coverage, and acceptance must not depend on a stronger proof that combines arbitrary Pattern domains.

For this proof, a **whole-position Pattern** is a Wildcard or Binding, a Unit Pattern at a Unit position, or a Tuple Pattern whose elements are all, recursively, whole-position Patterns. Grouping is transparent. A Literal or Enum Case Pattern is not a whole-position Pattern, even for an enum with one Case. These rules apply after Pattern Type and arity validation, and legal implicit referent inspection keeps the same rules.

| Matched domain | Coverage model |
| --- | --- |
| Whole-position Pattern | One such arm covers the entire matched domain |
| `bool` / Unit | `true` and `false` / the single `()` value |
| Enum | Every declared Case has an arm whose payload elements are all whole-position Patterns; a payload-free Case needs only its Case Pattern |
| Tuple | One whole-position Pattern is required; partial Tuple Patterns are not combined |
| Integer / `char` / `string` | A Wildcard or Binding is required; enumerating Literals does not establish exhaustiveness |
| Structurally opaque Type | A Wildcard or Binding is required |

Complex payload and Tuple partitions remain valid Patterns, but their combination supplies no whole-payload or whole-Tuple coverage proof. Add an unguarded catch-all for the affected Case (for example `.Some(_)`) or for the entire subject (`_` or a Binding), or use nested matches with independently checked coverage. The `true`/`false` rule applies to a match whose subject is `bool`; it does not combine `.Some(true)` and `.Some(false)` into coverage of `.Some(_)`. These are limits on accepted proofs, not on runtime matching. All declared Cases are treated as possible; recursively empty or uninhabited payloads are not inferred.

Prefer Case-specific completion over a whole-subject catch-all when new Cases should trigger diagnostics. For Tuple-based state transitions, nested matches can keep that check for each enum. Rewrites must preserve subject evaluation count and order, acquisition and lifetimes; save subjects first when needed.

```kimi
// Copy enums: State has Idle/Running; Event has Start/Stop.
match state
    .Idle
        match event
            .Start => start()
            .Stop => ()
    .Running
        match event
            .Start => ()
            .Stop => stop()
// Adding a Case to either enum makes a match non-exhaustive.
```

```kimi
match flagOption
    .Some(true) => 1
    .Some(false) => 0
    .None => -1
    // Error: Some needs a whole-payload arm despite the Boolean partition.

match flagOption
    .Some(true) => 1
    .Some(false) => 0
    .Some(_) => 0                   // Required catch-all; no mandatory union-based warning.
    .None => -1                     // Valid coverage of Option<bool>.

match numberOption
    .Some(0) => 0
    .None => -1                     // Error: other Some values are missing.

match pair
    (true, _) => 1
    (false, true) => 2
    (false, false) => 3
    // Error without a catch-all: partial Tuple Patterns are not combined.

match pair
    (true, _) => 1
    (_, _) => 2                    // Valid: one whole-position Tuple Pattern.
```

**Unreachable-pattern warning.** A warning is required only if **one earlier unguarded Pattern** contains the later Pattern:

- A Wildcard or Binding contains any Pattern at that position.
- Equal fitted Literals, and Unit, contain themselves.
- Same-Case and equal-arity Tuple Patterns contain another when every corresponding child does.

Grouping, binding names and `let`/`var` are ignored for containment, and the later arm may be guarded. Earlier arms are not combined, payloads are not enumerated, containment is not inferred from partial overlap, and guard logic such as `if false` is not used. Stronger optional lints may not change acceptance or exhaustiveness proofs.

```kimi
match value
    .Some(_) => 1
    .Some(0) => 2                    // Warning: earlier Pattern covers it.
    .None => 0

// number: i32
match number
    0 => 1
    -0 => 2                         // Warning: same fitted value.
    0x00 => 3                       // Warning: same fitted value.
    _ => 4

match otherValue
    .Some(let x) if x > 0 => 1
    .Some(1) => 2                    // No coverage warning from the guard.
    _ => 0
```

This diagnostic is independent of [control-flow reachability](#1492-reachability): it never removes an arm from Type or ownership checking, result-source collection or inference. Invalid Pattern Types and arities are diagnosed first. A failed exhaustiveness proof identifies a missing Case, a required whole-payload arm or a required catch-all; it must not claim that a runtime value is unmatched when only the limited proof fails. A mandatory unreachable warning identifies the single preceding covering arm.

## 14.9. Result validation

After compile-time directive selection, four tasks are distinguished. They define semantic responsibilities, not compiler passes or a required processing order.

| Task | Purpose |
| --- | --- |
| Result-source collection | Collect supplied values, including unreachable transfers, as Type constraints. |
| Structural Completion | Find normal-completion candidates from syntax and evaluation order; determine implicit Unit and eligibility for Never. |
| Runtime Reachability | Apply initialization, Move, Loans, refinement and cleanup to the same conservative paths. |
| Type-checking continuation | Check unreachable source with valid local facts without adding execution paths (§14.10.3). |

**Result sources** supply values to a target. After Body forms, contexts and transfer targets are resolved, the following are collected:

| Source | Constraint |
| --- | --- |
| Explicit transfer | Every self-targeted `return`/`yield`/`exit` operand, including unreachable ones; omitted operands supply Unit. |
| Single-item body | Expressions whose values are used as results under §14.2. A statement or discarding body supplies Unit if it structurally completes normally. |
| Indented body | Unit on structural end arrival, when its owner uses that completion as a result. |
| Other implicit completion | Unit from an omitted `else`, from `for` exhaustion and from structural `while`-false completion. |

Entry to each body is assumed during collection, including unreachable bodies. A sequence with no structural continuation acquires no end Unit, but later written result sources are still checked. The continuations of transfers caught inside the sequence are followed. Loop body ends are not loop result sources.

Syntax, Names, target and operand restrictions, local Types and match coverage are always checked. Runtime Reachability and cleanup can neither add nor remove result sources nor replace inferred Types. Source expressions still use valid refinement information (§14.10); collecting sources and checking expressions at particular points are separate responsibilities.

### 14.9.1. Expression type and target Result type

The **Target Result Type** constrains the results supplied to a target. It is determined independently of source traversal order:

1. Use the declaration or the fixed Type from §14.2; otherwise use a fixed outer expectation.
2. If it is still unknown, collect the independently typable source constraints together and find one common Type. Never alone supplies no concrete Type candidate.
3. Propagate the fixed Type to sources checkable against it; apply numeric literal defaults only after all other available evidence.
4. Check every source for fitting. If an unresolved call, anonymous function or empty literal still needs a Type, require an annotation or explicit Type arguments.

Nested result expressions use the same expected-Type propagation. An inner result that depends on numeric defaults is not committed before available outer constraints are processed. Parentheses, labels and body nesting alone do not commit defaults, and an already typed binding is not reinferred from later uses. The call, lambda and try boundaries of §10.5 and the complete-Type inference rules of §10.8 apply. Combinations of unresolved sources, common bases, numeric conversion chains and overload candidates are never searched by rechecking bodies.

A target's expectation propagates only to its result sources, not to discarded intermediate values or transfers to other targets, and the normal Semantics, Origin and fitting rules apply. If no source exists, or all source Types are already Never, an unknown Target Result Type may proceed to the Never rule below. An unresolved source is not an absent source.

```kimi
let large: i64 = 10
let first = if ready => 1 else => large
let second = if ready => large else => 1 // Both i64; no early default to i32.
let nested = if ready
    yield if cached => 1 else => 2
else => large                           // Inner and outer results use i64.

let small = if cached => 1 else => 2    // Committed i32 at this declaration.
let bad = if ready => small else => large // Error: i32 versus i64.

let conflict = loop
    continue
    exit 1
    exit "x" // Error: unreachable results must also have compatible Types.
```

The **Expression Type** is normally the Target Result Type. After every source is checked, it is Never only if both conditions hold:

- there are no result sources, or all sources have Type Never; and
- Structural Completion has no normal-completion candidate.

A fixed expectation alone does not create a supplied value. A non-Never source constrains the result even when unreachable. Never cannot rescue a Type mismatch, an unresolved source or a missing required result, and Runtime Reachability must not replace the Expression Type with Never, including when cleanup prevents delivery.

```kimi
let a = loop => continue      // Initializer Type Never; initialization never finishes.
let b: i32 = loop => continue // Initializer Never fits declared i32.

let typed = loop
    continue
    exit 1 // Still a result source: initializer Type i32, despite no normal path.
```

Named functions keep their declared or default Unit return Type. An anonymous function without a declared or fixed expected result uses these source and Never rules for return inference, while the function value itself keeps its Function Item or Closure Type. A constructor's Unit control result and a `deinit`'s `return` keep the separate construction and destruction obligations of §14.5.3.

### 14.9.2. Reachability

**Structural Completion** conservatively composes normal and targeted-transfer candidates. Normal completion of an item sequence is **structural end arrival**. Divergence and Abort supply no normal candidate. Paths are never pruned using `bool` literals, condition values, constant propagation, Pattern containment, dynamic-Type contradictions or arbitrary callee-body analysis.

| Construct | Common path rule |
| --- | --- |
| Ordinary expression / sequence | Respect evaluation order; continue only from normal completion. |
| `if` / `require` | After condition evaluation, consider both true and false. |
| `match` | After acquisition, consider all arms and both outcomes of each evaluated guard. A non-completing evaluation does not continue on that path. Exhaustiveness removes an unmatched final completion. |
| `and` / `or` | After the left operand, consider both evaluating and skipping the right operand. |
| `for` / `while` | After acquisition or condition evaluation, consider both an iteration and immediate exhaustion or false completion. |
| `loop` | The body end and `continue` repeat; only a self-targeted `exit` completes the loop. |
| Transfers | Evaluate any operand first, then transfer. Do not continue at the source; follow the target's received transfer. |
| `do` / `unsafe` | Follow the body; `do` receives its named `exit`. |
| Declaration / `defer` registration | Perform the necessary initialization or acquisition, then continue if normal. Function and `defer` bodies are not executed at declaration or registration. |

Never-returning calls and Abort do not continue normally. Structural Completion does not incorporate delivery blocked by Scope Exit cleanup.

**Runtime Reachability** applies initialization, Move, Loans, refinement, registered `defer` and destruction to these same paths, following actual required cleanup and the continuation after a received transfer. It is a conservative static approximation, not a proof of runtime feasibility. It validates the non-continuation of `require` failure and execution-state legality, without changing result constraints.

```kimi
func incomplete() -> i32
    if true => return 1
// Error: structural false path reaches the body end and supplies Unit.

func shortCircuit(flag: bool) -> i32
    flag and (return 1)
// Error: skipping the right operand reaches the body end.

var port: i32
while true
    port = tryBind()
    if port > 0 => exit
use(port) // Error: the static false path need not initialize port.

var boundPort: i32
loop
    boundPort = tryBind()
    if boundPort > 0 => exit
use(boundPort) // Valid: every delivered exit follows initialization.

let stopped: i32 = work: do
    defer => loop => ()
    exit to work: 1
// Expression Type remains i32; Runtime Reachability stops at cleanup, before delivery.
```

A warning is issued for `while true`, including a parenthesized `true`, suggesting `loop` for unconditional iteration; the warning does not reject the program. `if false => consume(value)` still has a static Move path for a Non-Copy `value`; resulting use errors should explain this and suggest `#if` when static exclusion was intended. Optimization may remove dead runtime paths but must not change acceptance.

## 14.10. Type refinement

### 14.10.1. Stable bindings and effective types

Runtime Type refinement attaches to the current **Value Instance** of a resolved **Binding Identity**. Eligible subjects are parenthesized or bare names of object-typed local `let` bindings or non-reassignable parameters with struct Core targets. `var` bindings, Fields, computed and required Properties, indexing and call results can be tested but do not refine later reads. Facts do not transfer to aliases or shadowed bindings.

The **Effective Type** of a name at a program point narrows only its guaranteed Core. Declared Semantics, Origins, Loans, mutability, initialization state, object identity and complete destruction responsibility are preserved. Writes or reinitialization that could replace the binding invalidate old facts, and Move or destruction prevents further use. Mutation of members alone does not invalidate facts while the binding's value and Dynamic Type remain the same. This adds no `var` refinement and no write permission.

A complete payload update invalidates facts about the old field contents and their projections, including `let` fields, while preserving object Identity and Dynamic Type refinements and still-valid storage access (§15.7.3); a `let` field is not an object-lifetime constant. Replacing or moving a handle follows the separate invalidation rules above.

The Effective Type is used for member lookup, argument applicability, overload resolution, assignment sources, results and local inference. Each candidate's ordinary fitting and adaptation rules apply; a base candidate is not discarded if those rules fit, resolution is not retried with the declared Type, and already fixed declarations and destination Types do not change. Explicit object upcasts remain necessary where ordinarily required.

```kimi
func handle(value: objref/Animal) -> ()
    let alias = value
    require value is Dog else => return
    value.bark()
    let dog = value       // Inferred objref/Dog.
    alias.bark()          // Error: alias has no Dog guarantee.
```

The new `dog` binding uses ordinary typing and acquisition of the right-hand side's Effective Type, not transferred flow facts. The acquisition rules of §3.5 still apply: a bare owning Non-Copy Place requires an explicit transfer such as `@move`; refinement never introduces an implicit Move. A binding's declaration Type never changes.

The [inherited-Name rule](06-declarations-and-containers.md#622-inheritance-and-open-structures) keeps the declaration layer of an existing member unchanged by refinement when that member is accessible both to the derived author and at the relevant uses. For a public `Animal.speak`, `Dog` cannot add the same Name, so a parameter or `let` before refinement, inside it and after a join, an unrefined `var`, and `Dog`/`Animal` views all select `Animal`'s layer. Refinement can add access to a differently named `Dog.bark`. This does not freeze overload choice based on argument Types or promise identical lookup across access boundaries.

### 14.10.2. Condition states and joins

**Flow State** describes refinement, initialization and Move state, Loans and reachability; it need not be one data structure or pass. Let `True(C, I)` and `False(C, I)` be the states after condition `C` from input state `I`. Evaluation effects are applied first. Refinement cannot restore a Moved value, discard Loans or create Boolean exits from a non-completing evaluation.

For an eligible `x`, the table updates only refinement; "retain" means the facts still valid after evaluation:

| Condition | True path | False path |
| --- | --- | --- |
| `x is T` | Add `x is T or derived` | Retain |
| `x is not T` | Retain | Add the same positive fact |
| `not C` | `False(C, I)` | `True(C, I)` |
| `(C)` | `True(C, I)` | `False(C, I)` |
| Other Boolean expression | No new fact | No new fact |

For `A and B`, `B` is analyzed under `True(A, I)`; the true state is `B`'s true state, and the false state joins `A`-false and `B`-false. For `A or B`, `B` is analyzed under `False(A, I)`; the false state is `B`'s false state, and the true state joins `A`-true and `B`-true. These rules apply at every expression position. Short-circuiting skips runtime evaluation, not static syntax, Name or Type checks. No exclusion Types, union Types, or provenance through stored Booleans, `== true` or arbitrary calls is added.

On one path, compatible positive facts select the most derived guaranteed Type. At joins, the most derived base guaranteed by every reachable incoming path is retained, never wider than the declaration Type. Incompatible facts on one path revert that binding to declaration-Type checking; they neither make the path unreachable nor prove arbitrary Types. A contradiction alone may warn but is not an error. Other Flow State components keep their own joins.

| Construct | Propagation |
| --- | --- |
| `if` / `else` | Condition true / false; `else if` starts with the preceding false facts |
| `while` | True to the body; false to the condition exit |
| `require` | True to subsequent statements; false to the failure body |
| Subsequent joins | Only incoming paths retained by Runtime Reachability after transfers and cleanup (§14.9.2) |

Continuations of constructs that catch transfers are tracked, so an early `return` through an ordinary `if` can refine later statements. Loop entries join the first entry and every backedge, and exits join the condition-false and delivered `exit` paths. `continue` propagates to its correct iteration point. These rules are solved to a stable result independent of processing order; a previous successful iteration alone is insufficient.

`defer` resolves Names and Effective Types at registration and then checks initialization and Loans on the actual cleanup paths; later refinement cannot reinterpret an earlier registration. Function boundaries import no outer flow facts. A capture uses the source binding's declared complete Type and the ordinary capture operation, without importing refinement attached to the outer binding. To capture a narrowed Type, first initialize a new local from the refined expression; its inferred declaration Type is then available under the normal capture rules.

### 14.10.3. Type-checking unreachable code

Unreachable source items are still checked, with a **type-checking continuation**. Valid Effective Types and enclosing-condition facts are preserved; unreachability alone does not reset bindings to their declared Types. The ordinary condition, `require` and join rules apply, keeping only common guarantees at joins.

After a transfer, the continuation uses the state after operand evaluation and acquisition but before that transfer's scope-exit cleanup, at the source's lexical scope. Facts already invalidated by assignment, Move or destruction are not retained; uninitialized or Moved values and ended Loans are not restored, and outer refinement is not imported across a Function Boundary.

Comparison-only Loans abandoned by that transfer end after its operand acquisition and before its cleanup, and are absent from its type-checking continuation. Loans of comparisons that the transfer does not abandon remain active.

These continuations add no Structural Completion or Runtime Reachability edges, no implicit Unit results and no normal successor to a `require` failure. Their states are never merged into reachable execution paths, but their result-source Types are still included under §14.9.

~~~kimi
func scoreInBranch(animal: objref/Animal) -> i32
    if animal is Dog
        return 0
        return animal.score() // Unreachable, but retains the Dog refinement.
    return 0

func scoreAfterReturn(animal: objref/Animal) -> i32
    return 0
    require animal is Dog else => return 1
    return animal.score() // Refined within the unreachable region.
// Both calls are Type-correct if Dog has score() -> i32.
~~~

## 14.11. Require statement

### 14.11.1. Syntax and placement

`require Expression else Body` is a statement in executable scopes. It evaluates its `bool` condition once, continues when it is true, and executes its mandatory failure body when it is false. The condition, temporary cleanup and Body forms follow §14.2. The `else` is on the condition's ending physical line or on the next effective line, aligned with `require`.

`require` has no result, label, transfer target or lookup barrier, and cannot be an initializer or argument. Its failure body uses the common Body rules.

```kimi
require ready else => return
require valid else
    logFailure()
    return
```

### 14.11.2. Evaluation and non-continuation

Entry to the failure body is assumed independently, even for a literal `true`. Under Runtime Reachability, no path, including required cleanup, may continue normally to the statement after `require`; otherwise the statement is rejected. Outward transfers, Never-returning calls, divergence and Abort satisfy the requirement, and transfers caught inside the failure body are followed through their continuations. `require` inserts no implicit `return`, exception or Abort.

```kimi
require true else => logFailure() // Error: normal continuation.
require ready else
    loop => exit                 // Error: only ends the inner loop.
require ready else => while true => () // Error: static false path completes.
require ready else => loop => ()       // Valid: no normal failure continuation.
```

Registering a `defer` does not execute it; its effects are analyzed on the cleanup paths using declared call result Types, not arbitrary termination proofs. A literal `false` does not remove the static true successor of `require` (§14.9.2).

### 14.11.3. Transfers and cleanup

`require` is transparent to transfer lookup in its condition and failure body, while the surrounding function and `defer` barriers still apply. It is not a rewrite to `if`, which would add a `yield` target. Results are secured before the scopes actually left are cleaned up, and success keeps the enclosing scope active.

```kimi
let answer = if enabled
    require ready else => yield 0 // Targets the outer if.
    yield compute()
else => 0

defer
    require needsCleanup else => exit // Ends this deferred body.
    cleanup()
```

`return` still requires a Function Boundary, and a deferred body cannot return from its outer function. Abort performs no ordinary Scope Exit. Refinement after `require` follows §14.10.
