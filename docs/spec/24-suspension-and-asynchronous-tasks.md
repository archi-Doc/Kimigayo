# 24. Suspension and asynchronous tasks

[Specification index](../SPEC.md)

Asynchronous tasks rest on two rules: [§24.2](#242-task-slots) states which functions may suspend, and [§24.3](#243-task-calls) states where suspension happens and what other tasks may run meanwhile. The `Kimi.Async` operations, cancellation and `Async.run` are defined in [§22.3](22-core-execution-and-foreign-functions.md#223-asynchronous-tasks). All tasks of one tree run on one thread; structured workers ([§22.3.6](22-core-execution-and-foreign-functions.md#2236-structured-worker-offload)) run ordinary computations on other threads. Every violation of this chapter is a Language error ([§23.1.1](23-compiler-services.md#2311-categories)).

```kimi
func fetchBoth(task; a: ref/PageId, b: ref/PageId) -> Result<(Page, Page), FetchError>
    let getA = func [a] (task;) -> Result<Page, FetchError> => fetchPage(task; a)
    let getB = func [b] (task;) -> Result<Page, FetchError> => fetchPage(task; b)
    return Async.joinOk(task; getA@move, getB@move)   // Task call: the children run concurrently.
```

## 24.1. Tasks and task frames

- **Task.** The execution of a root that `Async.run` starts, or of a child that a Kimi operation starts ([§22.3.2](22-core-execution-and-foreign-functions.md#2232-running-and-structured-operations)). A root begins a new **task tree**; a child belongs to the tree of the task that started it. A task and its descendants form its **subtree**.
- **Task frame.** The state of one call of a function with a task slot, from entry to completion, including while it is suspended. A task consists of the task frame of its body and the task frames of the task calls made in its task frames. Suspending the innermost task frame suspends all task frames of the task, because each outer one is executing the task call that contains the next. A task frame is not a value: it is never moved, copied or stored. It is distinct from the call activation of [§15.6.7](15-ownership-and-lifetime-analysis.md#1567-call-reservations).
- **Task call.** A call defined in [§24.3](#243-task-calls). It is the only place where a task frame can suspend, so a task call is a **suspension point**.

```kimi
func outer(task; delay: Time.Duration) -> ()
    inner(task; delay)               // outer's task frame waits inside this task call.

func inner(task; delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)     // Suspends inner and outer together.
```

## 24.2. Task slots

A function may suspend only when its parameter list begins with a **task slot**, written `task;`. The task slot is neither a Type nor a value: it declares no binding, so it can never be captured, stored, returned or bound as a Type argument.

### 24.2.1. Form

- **Lists.** Only these lists admit a task slot: the parameter list of a named function, a Contract function requirement, an explicit specialization or an anonymous function, and the Function Parameter List of a Function Type or Callable signature ([§3.2](03-types-and-values.md#32-compound-types), [§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)). A call's argument list, including that of a construction or a base initializer, admits the **task argument** `task;` in the same place.
- **Keyword.** `task` is a contextual keyword ([§F.3.2](appendices/F-syntax-summary.md#f32-contextual-keywords)); a Type-position list beginning with `task;` is a Function Parameter List ([§3.2](03-types-and-values.md#32-compound-types)).
- **Spelling.** The `;` is always written, also when nothing follows: `(task;)`. No comma follows it. `(task)` is an ordinary parameter, argument or grouped Type named `task`.
- **Position.** The task slot precedes the receiver and both sections of the `!` boundary, and belongs to neither ([§7.2.1](07-functions-and-callable-values.md#721-argument-name-boundary)). A list has at most one.
- **Where not.** Constructors, foreign function declarations, an Application's `main` and test functions have no task slot ([§6.3](06-declarations-and-containers.md#63-constructors), [§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports), [§22.4.2](22-core-execution-and-foreign-functions.md#2242-explicit-main-and-library), [§6.7.2](06-declarations-and-containers.md#672-test-definitions)). Accessors and `drop` have no list that admits one ([§11.3](11-properties.md#113-accessor-functions), [§16.3.1](16-scope-exit-and-destruction.md#1631-destruction-receiver)); neither do Attribute and `$` operation argument lists, inferred Case payloads or Patterns.
- **Anonymous functions.** An anonymous function has a task slot exactly when its header writes one. An expected call signature never supplies one; it still supplies omitted parameter Types, aligned after the task slot ([§7.7.1](07-functions-and-callable-values.md#771-syntax-and-inference), [§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries)).
- **Specializations and overrides.** An explicit specialization or a virtual override has a task slot exactly when its original has one ([§8.13](08-generics-constraints-and-contracts.md#813-explicit-full-function-specialization), [§6.4.1](06-declarations-and-containers.md#641-declarations)).

```kimi
contract Reader
    func read(task; self: uniq/Self, buffer: uniq/Array<u8>) -> Result<isize, ReadError>
    func late(value: i32, task;) -> ()        // Error: the task slot comes first.

func pause(task; ! delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)

struct Job
    init(task;) => ()                         // Error: a constructor has no task slot.
```

### 24.2.2. Passing

A call passes the task of the innermost enclosing function, anonymous functions included, by writing `task;` first in its argument list: `foo(task; x)`, `bar(task;)`. A call writes the task argument exactly when its callee (the selected declaration, Function Type or Callable signature) has a task slot. A construction, a qualified Case construction and a base initializer have a callee without a task slot. The task argument may be written only when the innermost enclosing function has a task slot, and not directly in a default expression or in the body of a Deferred Block that the function registers. A function expression inside them has its own Function Boundary and may pass its own task slot ([§7.7.1](07-functions-and-callable-values.md#771-syntax-and-inference), [§16.1.1](16-scope-exit-and-destruction.md#1611-deferred-control-boundary)). Cleanup therefore never suspends. When the callee has a task slot and the call's first argument is the Name `task` resolving to no binding, as in `foo(task, x)` or `foo(task)`, the call is a missing task argument.

```kimi
func runAll<F>(task; jobs: ref/Array<F>) -> ()
    F is Callable<(task;) -> ()>
    for job in jobs
        job(task;)

func misuse(task; delay: Time.Duration) -> ()
    let saved = task                                     // Error: the task slot is no value.
    let later = func [delay] () => _ = Async.sleep(task; delay)
                                                         // Error: the Closure has no task slot.
    Console.writeLine(task; "done")                      // Error: writeLine has no task slot.
```

### 24.2.3. Not a parameter; one shape per function group

A task slot is no parameter ([§7.2.2](07-functions-and-callable-values.md#722-name-contract-and-defaults)). It has no position number, external name, acquisition mode or matching key, and binds no argument ([§7.4](07-functions-and-callable-values.md#74-parameter-acquisition-shape)). The receiver and the ordinary parameters keep their numbering and counts `N` and `K`: in `func configure(task; ! count: i32)`, `count` is ordinary parameter 0 and `K = 0`. The task slot is not an elision input ([§15.4.3](15-ownership-and-lifetime-analysis.md#1543-result-defaults)) and has no per-call Origin ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints)).

In one function group ([§7.3](07-functions-and-callable-values.md#73-receivers), [§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)), either every declaration has a task slot or none does. A group formed by the same-name declarations of one declaration scope (a Container and its fragments, the project root, a local scope, the members of a Type, the requirements of a Contract including inherited ones) is checked at its declarations; a group gathered at a use ([§9.5.1](09-names-signatures-and-access.md#951-aliases), [§9.6](09-names-signatures-and-access.md#96-qualified-and-inherited-lookup)) is checked at that use. A task slot never distinguishes overloads, so a forgotten `task;` is a missing task argument, never a call of another overload. Two declarations that differ only in the task slot are also duplicate declarations ([§9.1](09-names-signatures-and-access.md#91-signatures)).

```kimi
contract Fetcher
    func fetch(task; self: ref/Self, id: ref/PageId) -> Page
    func fetch(self: ref/Self, id: ref/PageId, retries: i32) -> Page
                                         // Error: one group, two task-slot shapes.
```

### 24.2.4. Compatibility

Implementation identification ([§8.6.3](08-generics-constraints-and-contracts.md#863-implementation-matching)), specialization target identification ([§8.13.1](08-generics-constraints-and-contracts.md#8131-declaration-and-target)), callable signature compatibility and common Function Type conversion ([§10.7](10-overload-resolution-and-inference.md#107-callable-signature-compatibility)), known- and fixed-signature matching ([§10.5](10-overload-resolution-and-inference.md#105-inference-boundaries), [§10.8](10-overload-resolution-and-inference.md#108-generic-argument-inference)) and Function Type identity ([§3.2.1](03-types-and-values.md#321-callable-value-types)) require both sides to have a task slot or neither; the remaining parameters compare as before. An implementation that never suspends satisfies a requirement with a task slot by declaring one.

```kimi
func runLater<F>(body: F) -> ()
    F is Callable<owner, (i32) -> ()>
    body@move(0)

func caller(task; delay: Time.Duration) -> ()
    let wait = func [delay] (task; value: i32) => _ = Async.sleep(task; delay)
    runLater(wait@move)        // Error: the task slot differs from (i32) -> ().
```

## 24.3. Task calls

A **task call** is a call whose argument list begins with the task argument `task;`.

1. **Scheduling.** While a task executes a call that is not a task call, no other task of its tree that started before that call runs.
2. **Nested trees and children.** A tree that `Async.run` starts during a call runs inside that call as part of the callee, and its effects are compared as effects of that call. A child that a Kimi operation starts runs inside that operation's task call, and its effects are compared as effects of that call ([§22.3.3](22-core-execution-and-foreign-functions.md#2233-cancellation-and-waits)).
3. **Static Loans.** In the calling body, a task call conflicts with every live **static Loan** (a Loan anchored in static storage) that a call with unknown environment effects conflicts with ([§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)), including those its callee value, receiver and arguments hold or depend on, whatever its published summary or available effect bounds ([§8.11.3](08-generics-constraints-and-contracts.md#8113-checking-and-use)). This comparison is not part of the callee's effect summary.

A call that is not a task call is in progress exactly while its callee runs, since the callee cannot suspend; a task of an enclosing tree is itself executing the call that reached `Async.run`. Another task reaches a task's state only through Loans given to its children, which are argument dependencies of the call that starts them ([§8.10](08-generics-constraints-and-contracts.md#810-callable-constraints), [§15.6.4](15-ownership-and-lifetime-analysis.md#1564-calls)); through static storage; through `rc` and `arc` payloads, which grant shared access only ([§13.5.6](13-operators-and-assignment.md#1356-borrow-and-reborrow)); and through Kimi-internal storage, which the implementation keeps under the guarantees of [§22.3](22-core-execution-and-foreign-functions.md#223-asynchronous-tasks). Loans of locals and parameters are therefore not compared and may be held across a suspension. A parameter that borrows static storage was compared at the caller: at the task call that passed it, or at the `Async.run` call whose root captured it.

```kimi
group Settings
    public var greeting: string = "hello"

contract Source
    func take(task; self: uniq/Self) -> u32
        effect confined

func show<S>(task; source: uniq/S) -> ()
    S is Source
    let text = Settings.greeting@ref
    _ = source.take(task;)      // Error: a task call; `confined` does not exclude other tasks.
    inspect(text)
```
