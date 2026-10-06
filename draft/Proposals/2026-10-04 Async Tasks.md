# Specification change proposal: asynchronous tasks

Date: 2026-10-04

Status: 一部取り込み (partially integrated, 2026-10-04). Everything except §12 is integrated into the specification and frozen; §12 (threads, stage 3) stays open and depends on `2026-10-04 Callable Effect Bounds.md`. Nothing is implemented. Supersedes `2026-10-03 Async Tasks.md` and `2026-10-03 Async Tasks2.md`. The record is in `draft/INTEGRATED.md`.

References: `SPEC §n` is the language specification, `impl §n` the implementation specification, and a bare `§n` this proposal.

## 1. Summary

Kimigayo gains asynchronous tasks on one thread. Threads follow later as structured offload.

- **Suspension is an input.** A function may suspend only if its parameter list begins with a *task slot*, written `task;`. The task slot is neither a Type nor a value, so the authority to suspend can never be captured, stored or returned. A call that passes it writes `task;` too, so every suspension point is visible in the source.
- **No pending-computation values.** There are no futures or task handles, and no `Pin`.
- **Structured only.** Children are started only from Callable arguments of Kimi operations, which return only after their children complete, so children may borrow the caller's locals.
- **Cancellation is a request; observing it yields a value.** Kimi operations request cancellation of the children they started, as task state that no value represents. A wait that observes a request reports it in its `Result`, unless its operation completed first. Nothing is thrown or unwound, and cleanup never suspends or observes cancellation.
- **Two language rules.** Rule 1 defines task slots and rule 2 defines task calls. Everything else is Kimi obligations and library contracts; the compiler knows four task intrinsics, and the executor is written in Kimigayo.

```kimi
func fetchBoth(task; a: ref/Url, b: ref/Url) -> Result<(Page, Page), Net.Error>
    let getA = func [a] (task;) -> Result<Page, Net.Error> => fetchPage(task; a)
    let getB = func [b] (task;) -> Result<Page, Net.Error> => fetchPage(task; b)
    return Async.joinOk(task; getA, getB)    // Task call: the children run concurrently.
```

## 2. Problems in the current specification

1. **No way to wait without blocking.** The program has one execution thread (SPEC §22.2.3); tasks, threads and a memory model are deferred (SPEC §D.2).
2. **No cancellation rules.** Cleanup under cancellation is undefined (SPEC §16.2.3, §15.9).
3. **Other languages' designs conflict with current rules.**
   - `Pin` (Rust) conflicts with byte-transfer Moves (impl §21.4.5).
   - Cancelling by dropping a future needs an abrupt completion that SPEC §16.2.3 lacks.
   - A future that borrows its environment is a receiver-dependent public result, which is deferred (SPEC Appendix D).
   - Unmarked suspension (Go, and Kotlin call sites) defeats local reasoning (Principle 2).
   - Scoped tasks guarded by a destructor are unsound when the guard leaks (Rust's `mem::forget` problem).

## 3. Approach

1. **Authority from an input.** SPEC §8.4.10.2 separates authority obtained from inputs from authority obtained from the environment. The authority to suspend is an input, so the signature shows it.
2. **The authority is neither a Type nor a value.** It is a task slot of the parameter list, not a parameter, so it cannot escape and needs no escape rules.
3. **Task frames are not values.** They are never moved, copied or stored.
4. **Structure from call duration.** Children complete before the operation that started them returns. *Safe-code soundness never depends on a destructor running.*
5. **Cancellation adds no completion kind.** A request is task state, and a wait observes it by returning an `Err`, so SPEC §16.2.3 and Chapter 17 gain no abrupt completion.
6. **Implicit code and constructors never suspend.** Accessors, `drop`, defaults, static initializers and Deferred Blocks run implicitly and cannot suspend; constructors, `main` and test functions have no task slot either (rule 1.1, §8).
7. **Kimi in Kimigayo.** The compiler knows only a minimal task boundary (§5); Kimi writes the rest.
8. **One thread first.** Threads come later as structured offload (§12).

## 4. Language rules (new SPEC Chapter 24)

New Chapter 24, "Suspension and asynchronous tasks", is the only normative statement of rules 1 and 2. Each section they affect (§13) gains a one-sentence reference to it. The chapter defines these terms, which Appendix E also lists:

- **Task.** The execution of a root that `Async.run` starts, or of a child that a task starts through `TaskBoundary.enter`. A root begins a new *task tree*; a child belongs to the tree of the task that started it. A task and its descendants form its *subtree*.
- **Task frame.** The state of one call of a function that has a task slot, from entry to completion, including while it is suspended. A task consists of the task frame of the body that `Async.run` or `TaskBoundary.enter` starts for it and the task frames of the task calls made in its task frames. Suspending the innermost task frame of a task suspends all of that task's task frames, because each outer one is executing the task call that it made, which contains the next inner one. The term is distinct from the call activation of SPEC §15.6.7.
- **Task call.** Defined by rule 2. It is the only place where a task frame can suspend, so a task call is a *suspension point*.

### 4.1. Rule 1: task slots

1. **Form.** A function may declare a *task slot*, written `task;` at the start of its parameter list.
   - **Lists.** Exactly these lists admit it: the parameter list of a named function, Contract function requirement or explicit specialization, the parameter list of an anonymous function, and the Function Parameter List of a Function Type or Callable signature. A call's argument list, including a construction's and a base initializer's, admits the *task argument* `task;` in the same place.
   - **Keyword.** `task` is a contextual keyword only as the first token after the `(` of such a list or of a constructor's parameter list, when the next token is `;`. Elsewhere it is an ordinary Name. In Type position, a parenthesized list that begins with `task;` is always a Function Parameter List and must be followed by `->` (SPEC §3.2.3).
   - **Spelling.** The `;` is always written, also when no other parameter or argument follows: `(task;)`. Without it, `(task)` would read as an ordinary parameter or argument named `task`, or as a grouped Type named `task`. No comma follows the `;`. Whitespace around it has no meaning; canonical formatting writes none before it and one space after it when an item follows on the same line.
   - **Position.** The task slot precedes the receiver and both sections of the `!` boundary, and belongs to neither section (SPEC §7.2.1): `func read(task; self: uniq/Self, buffer: uniq/Array<u8>)`, `func configure(task; ! count: i32)`. A list therefore has at most one.
   - **Where not.** Constructors (`init`), foreign function declarations, an Application's `main` and test functions have no task slot, because no caller passes them a task (SPEC §6.2.3, §22.3.1, §22.2.2, §6.5.1). Accessors and `drop` have no list that admits one (SPEC §11.2, §16.3.1).
   - **Anonymous functions.** An anonymous function has a task slot exactly when its header writes one. A fixed expected call signature never supplies one; it still supplies omitted parameter Types, aligned with its parameters after the task slot (SPEC §7.6.1, §10.5).
   - **Specializations.** An explicit specialization writes a task slot exactly when its original has one (SPEC §8.8).
2. **Passing.** The task slot declares no binding and has no Type. A call passes the task of the innermost function that contains it by writing `task;` first in its argument list, as in `foo(task; x)` or `bar(task;)`. A call writes the task argument exactly when its callee (the selected declaration, Function Type or Callable signature) has a task slot. The task argument may be written only when that innermost function, anonymous functions included, has a task slot, and not directly:
   - in a default expression;
   - in the body of a Deferred Block that the function registers.

   Neither restricts a function expression inside them: it has its own Function Boundary (SPEC §7.6.1, §16.1.1), and its body may pass its own task slot.
3. **Not a parameter.** A task slot is no parameter (SPEC §7.2.2). It has no position number, external name, acquisition mode or matching key, binds no argument, and corresponds to no parameter (SPEC §7.3.1). The receiver and the ordinary parameters keep their numbering in the bound and unbound call sequences and their counts `N` and `K` (SPEC §7.2.2): in `func configure(task; ! count: i32)`, `count` is ordinary parameter 0 and `K = 0`.
4. **Shape.** In one function group (SPEC §7.3, §9.5), either every declaration has a task slot or none does. As for parameter acquisition shapes (SPEC §7.3.1), a group formed by the same-name declarations of one declaration scope (a Container and its fragments, the project root, a local scope, the members of a Type, the requirements of a Contract, including those inherited through refinement) is checked at the declarations, and a group gathered at a use (SPEC §9.4.1, §9.5) is checked at that use. A task slot therefore never distinguishes overloads, and whether a call needs the task argument follows from the Name alone.
5. **Compatibility.** Implementation identification (SPEC §8.4.5), candidate equivalence (SPEC §8.4.6), specialization target identification (SPEC §8.8.1), callable signature compatibility and common Function Type conversion (SPEC §10.7), known- and fixed-signature matching (SPEC §10.5, §10.8) and Function Type identity (SPEC §3.2.1) require both sides to have a task slot or neither. The remaining parameters, or a Function Type's parameter Types, are compared position by position as before.

### 4.2. Rule 2: task calls

> A *task call* is a call whose argument list begins with the task argument `task;`. While a task executes a call that is not a task call, no other task of its tree that started before that call runs. A tree started by `Async.run` during a call runs inside that call as part of the callee, and its effects are compared as effects of that call. A child started by `TaskBoundary.enter` runs inside the task call whose task frame executed `enter` (obligation 2), and its effects are compared as effects of that task call. In the calling body, a task call is compared as a call with unknown environment effects, whatever its published summary or available effect bounds (SPEC §8.4.10.4), against every potentially affected active Loan anchored in static storage, a *static Loan*: a Loan whose Place has a static-key root (SPEC §15.6, §22.2.4), as SPEC §15.6.4 compares unknown effects. These include the static Loans that the call's own inputs hold or depend on: its callee value, its receiver and its arguments. This comparison enters no effect summary or published record (SPEC §18.7.2); the callee's own effects are summarized as usual.

- **Why calls that are not task calls.** Only their extent is fixed: their callee has no task to suspend with (rule 1), so such a call is in progress exactly while its callee runs. A task of an enclosing tree is excluded too, because it is itself executing the call that reached `Async.run`.
- **Why "whatever its published summary or available effect bounds".** Without the clause, rule 2 of SPEC §8.4.10.4 would give a `confined` requirement call no environment effects. A Loan on a mutable static Field could then be held across a suspension while another task replaces the Field: a use after free.
- **Why static Loans only.** Other tasks can reach only state that is not this task's own (§7.1). Loans on locals and parameters are therefore not compared, and they may be held across a suspension. A parameter that borrows static storage was compared at the caller: at the task call that passed it, or at the `Async.run` call whose root captured it. A shared Loan of a static `let` slot is never affected, because its value cannot change after initialization (SPEC §22.2.3).

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
    use(text)
```

### 4.3. Consequences

Each fact follows from rules 1 and 2 and existing rules.

| Fact | Follows from |
| --- | --- |
| The task slot cannot be captured, stored, returned or bound as a Type argument | It declares no binding and has no Type (rule 1.2); `task` elsewhere is an ordinary Name |
| It is not an elision input and has no per-call Origin | It is no parameter (rule 1.3), so SPEC §15.4.3 and the per-call Origins of SPEC §8.6 do not apply |
| Accessors, `drop`, `main`, test functions, static initializers, constructors, defaults and Deferred Blocks never suspend | They cannot reach a task slot (rules 1.1 and 1.2) |
| Loans of locals may be held across a suspension | Call protection covers the whole call (SPEC §15.6.4); locals live until scope exit or Move (SPEC §5.2.1); there are no exceptions (SPEC §17.1); Abort does not unwind (SPEC §17.3.3) |
| Suspension points are locally visible | A task call is written with `task;`, so it is recognized by its syntax, and its task slot can only be the innermost function's |
| A forgotten `task;` never selects another overload | One function group has one shape (rule 1.4), so the call is a missing task argument at its only candidates |
| An implementation that never suspends satisfies a requirement with a task slot | It declares the task slot and does not pass it on; an implementation without one is not identified (rule 1.5, SPEC §8.4.5). Function Types that differ in the task slot do not convert |

**Why not an Origin rule.** If the authority to suspend were a `uniq` borrow, a captured Reborrow would be lifetime-correct (SPEC §7.6.2, §15.6.3), and a synchronous higher-order function could call a Closure that suspends inside its own frame. The hazard is which frames are running, not how long the authority lives, so a task slot states it directly.

```kimi
func misuse(task; delay: Time.Duration) -> ()
    let saved = task                                       // Error: the task slot is no value.
    let later = func [delay] () => _ = Async.sleep(task; delay)
                                                           // Error: the Closure has no task slot.

struct Job
    init(task;) => ()                                      // Error: a constructor has no task slot.

contract Bad
    func late(value: i32, task;) -> ()                     // Error: the task slot comes first.

contract Fetcher
    func fetch(task; self, url: ref/Url) -> Page
    func fetch(self, url: ref/Url, retries: i32) -> Page   // Error: one group, two task-slot shapes.

func runLater<F>(body: F) -> ()
    F is Callable<owner, (i32) -> ()>
    body@move(0)

func caller(task; delay: Time.Duration) -> ()
    let wait = func [delay] (task; value: i32) => _ = Async.sleep(task; delay)
    runLater(wait)                     // Error: F is not Callable<owner, (i32) -> ()>; the task slot differs.
    Console.writeLine(task; "done")                        // Error: writeLine has no task slot.
```

## 5. Task boundary (new SPEC §22.1.x)

The task boundary is an internal Kimi group bound to declaration identity. No alias, re-export or same-spelled declaration grants its capability, as for the storage boundary (SPEC §22.1.2.5). The compiler knows four operations:

```kimi
internal group TaskBoundary
    // The only way to start a task. Returns true when body completed without suspending.
    internal unsafe func enter<F, R>(context: raw/TaskRecord, body: F, result: raw/R) -> bool
        F is Callable<owner, (task;) -> R>
    // The record of the current task: the only access to task state, through the task slot.
    internal func current(task;) -> raw/TaskRecord
    // The only suspension: records the current task frame in waiter and suspends it.
    internal unsafe func park(task; waiter: raw/Waiter) -> ()
    // The only transfer of control into another task frame (obligation 1).
    internal unsafe func resume(handle: raw/TaskFrame) -> ()
```

Everything else is ordinary Kimigayo over these operations and raw storage (SPEC Chapter 5): `run`, `join`, `race`, `each`, `pipe`, `shield`, the executor, timers, IOCP and channels.

- **Wrappers.** A child with extra parameters is wrapped in a Closure of the `(task;) -> R` shape. For `each`, the wrapper is `func [child, item@move] (task;) => child(task; item@move)`: the bare entry copies the `ref/F`, and moving the captured item out makes the body Consuming (SPEC §7.6.2, §7.6.3), which `enter`'s `Callable<owner, ...>` admits (SPEC §8.6).
- **Dependencies.** `enter` takes `body` by value and moves it into the child's task frame, so no Type records the child's dependencies. Internal record Types appear only in private locals and Fields. Only the argument Loans of the public operation that received the child (SPEC §15.6.4) and obligation 2 protect them.
- **Task state.** Code that has a task slot reaches the current task's record, and through it the executor state, only through `current`; `Async.run`, which has no task, reaches the per-thread executor state directly, an environment effect (§9.1). The only input of `current` is its task slot, an input (SPEC §8.4.10.2 as amended in §13.1), so following pointers from the record is no environment effect. `current` and `park` are task calls like any other; `current` never suspends.
- **Trusted base.** The compiler does not check the obligations below. They are unsafe obligations that the Kimi library bears (SPEC §5.2.1), as for the storage boundary (SPEC §22.1.2.5), and they form the trusted base of tasks: a Kimi implementation that violates one makes safe programs unsound, which is undefined behavior (SPEC §7.5). The caller conditions of obligations 1, 2 and 3 are also the safety contracts of `resume`, `enter` and `park`; `current` is safe, because returning a raw pointer obliges its caller to nothing (SPEC §7.5). The barrier clause of obligation 4 is a lowering obligation of the compiler (impl §21). In obligation 5, which `Result` a wait returns is a library contract; that a value moves only at its commit is an unsafe obligation. Obligation 2 is the basis of borrowing across concurrent children.

Kimi obligations:

1. **Resumption.** `resume` takes only a suspended task frame whose wait has ended: its waiter was woken, or the task call at which it is suspended has completed. It resumes each suspension at most once, and runs only while every task of that frame's tree is suspended or complete. A task frame is never resumed after it completes and never destroyed before it completes.
2. **Child completion.** A child started by `enter` completes, and its result is taken, before the task frame that executed `enter` completes, and therefore before the public operation returns. A helper that returns without joining its children must not call `enter`. Because `enter`'s `bool` result keeps none of `body`'s dependencies, the checker ends `body`'s Loans when `enter` returns. Until the child completes, the task frame that executed `enter` treats them as its own live Loans: it performs no operation that would conflict with them if `body` were still one of its locals (SPEC §15.6.2), including destroying, replacing or moving a value that they borrow.
3. **Wait registrations.** Every wait registration (ready link, timer node, `OVERLAPPED`, channel waiter, join count) lives in the waiting task frame or its task record and is removed before the wait returns. An I/O wait, even a cancelled one, returns only after its completion packet is dequeued, so its registration stays until then.
4. **Shared handles.** A handle whose state another task may change (`Async.Sender`, `Async.Receiver`) never has its inline storage written while it is borrowed. Its mutable state lives in Kimi-internal raw storage behind a loaded pointer, and waiting intrinsics are compiler barriers for that state.
5. **Commit of observing waits.** A cancellation request for a task applies to every cancellation-observing wait in that task's subtree, whether pending now or started later. A request that applies to the caller of `Async.shield` reaches the body's subtree only once the call's grace has elapsed; requests made by operations inside the body apply as usual (§6.3). Each such wait *commits* exactly once, either by its operation, at the commit point in the table below, or by cancellation, whichever comes first. It returns what it committed to. Cancellation is reported in the wait's `Result`: as `Err(Async.Cancelled)`, as `Err((value, .Cancelled(reason)))` for `send`, or as the `Cancelled` Case of a domain error (§6.3).
   - A wait that starts while a request applies is committed by cancellation at the call, before its operation starts.
   - A request commits a pending wait at once, except an I/O wait: there the request only cancels the backend operation, and the completion packet commits the wait.
   - A commit is final. A later request, close or counterpart leaves it unchanged, and a resumed wait returns its commit, not the state of the request.
   - A value moves only at a commit. `send` gives its value to the buffer or the receiver only when its operation commits it to `Ok(())`; every other commit returns the value in `Err`. `receive` removes an item only when its operation commits it to `Ok(Some(item))`.
   - Cancellation never interrupts, unwinds or destroys a task frame, and adds no completion kind.

| Wait | Its operation commits it when |
| --- | --- |
| `send` | The value leaves the sender, into the buffer or to the receiver: `Ok(())`. The receiver is closed: `Err` with the value and `Closed` |
| `receive` | An item leaves the buffer or a waiting sender for the receiver: `Ok(Some(item))`. The channel is closed and drained: `Ok(None)` |
| `sleep` | The executor removes its expired timer node: `Ok(())` |
| `checkpoint` | At the call when it does not yield, otherwise when it is resumed: `Ok(())` |
| I/O | An immediate success on a marked handle, at the call (§9.3); otherwise the executor maps its dequeued completion packet to it (§9.3). A packet that reports the operation aborted with no data transferred commits it to cancellation; any other packet commits the outcome that the `Io` design maps from it |

On one thread every commit is made by Kimi code between task steps, so commits are totally ordered and a plain word records each one (§9.2).

## 6. Library: `Kimi.Async`

### 6.1. Declarations

The bodyless declarations show signatures only.

```kimi
public group Async
    public struct Cancelled                 // Fieldless, Copy and Owned; only Kimi creates it.
        Self is Copy
        internal init() => ()

    public enum SendFailure
        Closed
        Cancelled(Async.Cancelled)

    public enum Raced<A, B>                 // The Case names the winner; both final results are kept.
        First(A, B)
        Second(A, B)

    public struct Sender<T>
        public func send(task; self: uniq/Self, value: T) -> Result<(), (T, SendFailure)>

    public struct Receiver<T>
        public func receive(task; self: uniq/Self) -> Result<Option<T>, Cancelled>

    public func run<F, R>(root: F) -> R
        F is Callable<owner, (task;) -> R>

    public func sleep(task; duration: Time.Duration) -> Result<(), Cancelled>
    public func checkpoint(task;) -> Result<(), Cancelled>

    public func shield<F, R>(task; body: F ! grace: Time.Duration) -> R
        F is Callable<owner, (task;) -> R>

    public func join<A, B, RA, RB>(task; first: A, second: B) -> (RA, RB)
        A is Callable<owner, (task;) -> RA>
        B is Callable<owner, (task;) -> RB>

    public func joinOk<A, B, TA, TB, E>(task; first: A, second: B) -> Result<(TA, TB), E>
        A is Callable<owner, (task;) -> Result<TA, E>>
        B is Callable<owner, (task;) -> Result<TB, E>>

    public func race<A, B, RA, RB>(task; first: A, second: B) -> Raced<RA, RB>
        A is Callable<owner, (task;) -> RA>
        B is Callable<owner, (task;) -> RB>

    public func each<I, F>(task; items: I, child: ref/F ! limit: isize) -> Result<(), Cancelled>
        I is Iterator
        F is Callable<(task; I.Item) -> ()>

    public func eachReceived<T, F>(task; items: uniq/Receiver<T>, child: ref/F ! limit: isize)
        -> Result<(), Cancelled>
        F is Callable<(task; T) -> ()>

    public func pipe<P, C, T, R>(task; producer: P, consumer: C ! capacity: isize) -> R
        P is Callable<owner, (task; uniq/Sender<T>) -> ()>
        C is Callable<owner, (task; uniq/Receiver<T>) -> R>
```

Conventions:

- **Arity.** `join`, `joinOk` and `race` have one overload per arity; there are no variadic Type arguments (SPEC §8.1). `Raced` likewise has one declaration per arity, with one Case per child in order, each holding every final result.
- **Children.**
  - A *one-shot child* (of `join`, `joinOk`, `race`, `pipe` and `shield`) is consumed by its call, so it is received as `Callable<owner, ...>`, the "Consume the callback on invocation" row of STYLE §4.3. It may have a Shared, Exclusive or Consuming body (SPEC §8.6). A Non-Copy child held in a `let` is passed with `@move`; a Closure whose captures are all Copy, such as `getA` in §1, is copied.
  - `each` and `eachReceived` call their child concurrently, so they borrow it shared (`ref/F`, the "Shared invocation" row of STYLE §4.3), which admits only a Shared body.
  - `each` requires an `Iterator`, whose items may be retained across later `next` calls (SPEC §22.1.2.4), so running children can hold items from several calls at once (§6.2).
- **Named bounds.** `limit` is the maximum number of children that run at once. `capacity` is the number of items a channel buffers; `capacity: 0` is a rendezvous. `grace` is defined in §6.3. All three are required named arguments without defaults (STYLE §3.2). `limit < 1` and `capacity < 0` Abort as contract violations; `grace` needs no check, because `Time.Duration` is nonnegative (SPEC §22.7.2).
- **No `try` prefix.** An operation that takes a task and returns `Result` has no `try` prefix: its Result reports its own failure and, for waits, cancellation. STYLE §5.2 gains this rule, with `WriteWindow.push` and `WriteWindow.append` (both returning `Result<(), BufferFull>`) as precedents.

### 6.2. Structured operations

Every operation returns only after all its children complete. At its *decision event* it may request cancellation of the children still running.

| Operation | Decision event | Then | Result | Observes cancellation |
| --- | --- | --- | --- | --- |
| `join` | Last completion | — | Every result | No |
| `joinOk` | First `Err`, otherwise last `Ok` | Cancels the others | The first `Err`, or `Ok` of every result; unreturned results are destroyed | No |
| `race` | First completion | Cancels the others | `Raced` with every final result | No |
| `each` | Iterator exhausted and last child complete | — | `Ok(())`, or `Err` once a request stops pulling and all children are joined | Before each pull |
| `eachReceived` | Receiver closed and drained, last child complete | — | `Ok(())`, or `Err` once a request stops receiving and all children are joined | While receiving |
| `pipe` | Consumer completion | Closes the receiver, cancels the producer | The consumer's result | No |
| `shield` | Body completion | — | The body's result | No; it delays requests that apply to the caller (§6.3) |

`joinOk` names its success condition. A `try` prefix would be wrong: a try-prefixed operation is the try form of the operation with the same inputs (STYLE §3.2), and `joinOk` adds sibling cancellation.

**`each`.** `each` calls `next` only in its own task frame and only while fewer than `limit` of its children are running; it takes no item before a child can start. Before each pull it checks for a request that applies to it; once one applies, it pulls no more items. It starts one child per item in the order that `next` returns them, stops at the first `None`, as `for` does (SPEC §22.1.2.1), and destroys the Iterator only after its last child completes. `I is Iterator` makes this sound without another Contract:

- An item depends neither on the receiver Loan of its call nor on the Iterator's Storage (SPEC §15.6.3, §22.1.2.4), and simultaneously live exclusive items are disjoint (SPEC §15.6.2, §22.1.2.3).
- The `preserves results` bound excludes the Loans that earlier items keep through ordinary transfer or Reborrow (SPEC §8.4.10.3), including an item moved into a child's wrapper. `nextPair` (SPEC §22.1.2.4) is the sequential form of this retention.
- Children run only during task calls of `each` (rule 2), and `next` is not one, so no call of `next` overlaps a child's execution.
- Destroying the whole Iterator is outside the bound (SPEC §8.4.10.3), and a remainder destructor that conflicts with a retained item is rejected (SPEC §22.1.2.4), so obligation 2 places it after the last child.

The checker cannot enforce this inside `each`, because `enter` ends an item's Loans when it returns. The `Iterator` constraint and obligation 2 are the guarantee. `eachReceived` likewise receives only while fewer than `limit` children run, and starts a child with each received item before it waits again.

```kimi
func fillBoth(task; left: uniq/Buffer, right: uniq/Buffer) -> ()
    let fillLeft = func [left] (task;) => fill(task; left)          // Exclusive body.
    let fillRight = func [right] (task;) => fill(task; right)
    _ = Async.join(task; fillLeft@move, fillRight@move)

func replyBoth(task; first: Net.Connection, second: Net.Connection) -> ()
    let a = func [first@move] (task;) => respond(task; first@move)  // Consuming body.
    let b = func [second@move] (task;) => respond(task; second@move)
    _ = Async.join(task; a@move, b@move)

func normalizeAll(task; rows: uniq/Array<Row>, limits: ref/Limits) -> Result<(), Async.Cancelled>
    let fix = func [limits] (task; row: uniq/Row) => normalize(task; row, limits)
    return Async.each(task; rows.iterateUniq(), fix@ref, limit: 16)   // Disjoint uniq/Row items.
```

A user function that passes its task on is part of the same task, not a task boundary:

```kimi
func retryOnce<F, R>(task; body: ref/F) -> Result<R, Net.Error>
    F is Callable<(task;) -> Result<R, Net.Error>>
    match body(task;)
        .Ok(let value) => return .Ok(value@move)
        .Err(let error) => _ = error@move
    return body(task;)
```

### 6.3. Cancellation

- **Sources.** Only Kimi operations request cancellation, for the children they started, under their contracts (§6.2). A request is task state; no value represents it, and no public operation makes one (§15.2).
- **Observation.** Only waits for events other than the completion of the caller's own children observe a request (obligation 5): `sleep`, `checkpoint`, I/O, `send` and `receive`, including the receives of `eachReceived`. In addition, `each` and `offloadEach` (§12) check for a request that applies to them before each pull (§6.2); that check is not a wait and has no commit point.
- **One Type.** A wait whose only failure is cancellation returns `Result<T, Async.Cancelled>`. A domain error enum that can report cancellation has exactly one cancellation Case, `Cancelled(Async.Cancelled)` (a STYLE §5.1 addition).
- **Conversion.** `try` combines no error Types (SPEC §17.2.4), so converting `Async.Cancelled` into a domain error is explicit and has the same form everywhere: `.Err(let reason) => return .Err(.Cancelled(reason))`.
- **`checkpoint`.** Reports a pending request. It yields only when another task is ready, or when the task has passed an implementation-defined budget of `checkpoint` calls since it last suspended, so that the executor can poll timers and I/O. It reads no clock.
- **`sleep`.** `sleep(task; d)` returns `Ok(())` no earlier than `d` after the call, as measured by the elapsed-time counter of SPEC §22.7, and registering the wait allocates nothing. `sleep` itself reads no clock: the executor converts its duration into a deadline before its next wait for completions, which comes after the call (§9.2).
- **Effects of task operations.** `sleep`, `checkpoint`, `shield`, the structured operations of §6.2 and the endpoint operations reach executor state only through `TaskBoundary.current`, whose only input is its task slot (SPEC §8.4.10.2), and they read no clock. Apart from the effects of the children they call, they are therefore no environment effects and are usable in `confined` implementations; that state matches no user Loan. I/O waits call foreign functions and are environment effects. The executor's own clock and port calls run inside `Async.run`, which is no `confined` operation (§9.1).
- **Shielding.** `Async.shield(task; body, grace: g)` runs `body` as a child of the calling task while the other tasks keep running, and returns the body's result after the child completes.
  - A request that applies to the calling task, made before or during the call, reaches the child's subtree only once `g` has elapsed since the later of the request and the start of the call. Until then, waits inside behave as if it had not been made.
  - Requests made by operations inside the body apply as usual, so a `race` with `sleep` inside the body still times out.
  - The caller's request stays pending, so its next observing wait after `shield` returns reports it. Nested shields add their graces.
- **Cleanup and Abort.** Cleanup never suspends or observes cancellation (§8); Abort ends the process (SPEC §17.3.3).
- **Timeouts.** A timeout is `race` with `sleep`. Because `race` joins every child, the timeout ends the other child only when that child reaches a cancellation-observing wait, and each `shield` on its path adds at most its grace; a loop without such a wait runs to completion.

```kimi
group Net
    public enum Error
        Refused
        TimedOut
        Cancelled(Async.Cancelled)

func fetchAfter(task; url: ref/Url, delay: Time.Duration) -> Result<Page, Net.Error>
    match Async.sleep(task; delay)
        .Ok(_) => ()
        .Err(let reason) => return .Err(.Cancelled(reason))
    return fetchPage(task; url)
```

### 6.4. Channels and `pipe`

**Endpoints.**
- `receive` returns `Ok(None)` once the channel is closed and drained, and on every later call, as standard iterators do (SPEC §22.1.2.3). A delivered item depends on neither the receiver borrow nor the channel storage.
- `send` returns a rejected value in its `Err`, as `Dictionary.tryInsert` does (STYLE §5.1).
- Endpoint operations obtain authority from their inputs (SPEC §8.4.10.2, first row; §6.3). Interleaving with the other endpoint happens only at task calls (rule 2).
- Endpoints are operated only through `uniq/Self`; §15.2 explains why not through `ref/Self`.

**`pipe`** is the only way to create a channel. It creates the channel state in its own task frame and lends one endpoint to each child as `uniq`.
- With `capacity >= 1` it takes a ring buffer of `capacity` items from the task's arena, with no heap allocation in steady state. A run-time capacity cannot live in a frame, whose size is fixed per instance.
- When the consumer completes, `pipe` closes the receiver, requests cancellation of the producer and joins it. A pending `send` of the producer then commits to `Closed` (obligation 5). When the producer returns first, the sender closes; `receive` returns the buffered items and then `Ok(None)`. Cancelling `pipe` cancels both children.
- Items left in the buffer are destroyed with the channel when `pipe` returns, including values whose `send` returned `Ok(())`. With `capacity >= 1`, `Ok` means that the buffer took the value, not that the consumer received it.

**Generators** are `pipe` with capacity 0. No value owns a suspended task frame, and stopping uses `Closed` and `Cancelled`.

```kimi
func sumEvens(task; limit: u64) -> Result<u64, Async.Cancelled>
    let produce = func [limit] (task; out: uniq/Async.Sender<u64>) -> ()
        var n: u64 = 0
        while n < limit
            match out.send(task; n)
                .Ok(_) => ()
                .Err(_) => exit                 // The consumer finished, or cancellation.
            n += 2
    let consume = func [] (task; inbox: uniq/Async.Receiver<u64>) -> Result<u64, Async.Cancelled>
        var total: u64 = 0
        loop
            match try inbox.receive(task;)
                .Some(let value) => total += value
                .None => return .Ok(total)
    return Async.pipe(task; produce, consume, capacity: 0)
```

**Dynamic work** uses `eachReceived`; there are no detached tasks.
- Jobs of different kinds are sent as erased Function values of Type `(task;) -> ()`, under the erasure conditions of SPEC §7.6.4: a Shared minimum receiver and an Owned environment.
- A job that consumes its captures cannot be erased. Send its state as the channel item instead, and keep the code in the `eachReceived` child, as `serve` does with `Net.Connection`.

```kimi
func serve(task; listener: uniq/Net.Listener) -> Result<(), Async.Cancelled>
    let accept = func [listener] (task; out: uniq/Async.Sender<Net.Connection>) => acceptLoop(task; listener, out)
    let handleAll = func [] (task; inbox: uniq/Async.Receiver<Net.Connection>) -> Result<(), Async.Cancelled>
        let handle = func [] (task; conn: Net.Connection) => respond(task; conn@move)
        return Async.eachReceived(task; inbox, handle@ref, limit: 64)
    return Async.pipe(task; accept@move, handleAll, capacity: 16)

func runJobs(task; jobs: uniq/Async.Receiver<(task;) -> ()>) -> Result<(), Async.Cancelled>
    let runOne = func [] (task; job: (task;) -> ()) => job(task;)
    return Async.eachReceived(task; jobs, runOne@ref, limit: 8)
```

### 6.5. Asynchronous sequences

There is one form: repeat a task call in a `loop` with `match`. It reads both kinds of sequence:

- **Pull.** A task call such as `reader.readLine(task;)`, which returns without suspending when data is buffered.
- **Cross-task.** `Async.Receiver<T>` (§6.4).

`for` is unchanged (SPEC §14.6.2): `LendingIterator.next(self: uniq/Self during step)` has no task slot, and an iterator cannot hold a task. No `AsyncIterator` protocol is added.

```kimi
func countLines(task; reader: uniq/Io.LineReader) -> Result<u64, Io.Error>
    var count: u64 = 0
    loop
        match try reader.readLine(task;)
            .Some(_) => count += 1
            .None => return .Ok(count)
```

### 6.6. Which operations take a task

- Apart from `Async.run`, the bridge from code without a task (§9.1), every Kimi operation that may wait (for I/O, a timer, a channel or its own children) has a task slot and no task-free or `Async`-suffixed counterpart (Principle 1). Console output is the one exception, below.
- Console output (`Console.writeLine`, SPEC §22.4) stays synchronous in this proposal and moves to the task form in a separate later change (§14). Until then it blocks the thread, and its implementation must not run other tasks (obligation 1). That change must also decide how code without a task writes output: `drop`, Deferred Blocks, accessors, constructors, and `main` outside `Async.run`.

## 7. Ownership and lifetime

### 7.1. Loans across a suspension

- Loans of locals and parameters may be held across a suspension (§4.3).
- Other tasks can reach a task's state only through:
  - Loans given to its children, checked as argument dependencies (SPEC §8.6);
  - mutable statics and external state, covered by rule 2;
  - `rc` and `arc` payloads, which allow shared access only (SPEC §13.5.5.2);
  - Kimi-internal raw storage, which publishes no Place, under obligations 3 and 4.
- Tasks interleave only at task calls (rule 2). With the list above, this is why variance, handle destruction and receiver preservation, which SPEC §15.6.2 bases on single-thread execution, hold unchanged for tasks (§13.1); threads must revisit them.
- The Origin of a Loan anchored by a raw-Place borrow has no upper bound (SPEC §5.2.2). When such an anchor is live across a suspension, the CSP reports this as an obligation fact in the existing SPEC §23.5.3 listing of the Unsafe Block that formed it.

### 7.2. Children

- **Loans.** A child's Loans are dependencies of an argument of the operation.
  - A one-shot child is received by value. Its Loans last until the operation destroys it, and through results that depend on them.
  - An `each` or `eachReceived` child is received as `ref/F`. Its Loans are shared argument Loans for the call (SPEC §15.6.4).
- **Borrowing.** Shared borrows fan out as copies of `ref`. Exclusive borrows must be disjoint: separate parameters, Reborrow captures, or region-split iterator items. Overlapping exclusive captures are ordinary Loan conflicts (SPEC §7.6.2, §15.6.7).
- **Owned.** Children need no Owned; only erased jobs (§6.4) do.
- **One thread.** Every task of a tree runs on the thread of its `Async.run` (§9.1). Children may therefore capture `rc` handles, whose counts are not atomic.

### 7.3. Leaks and movement

- **Safe-code soundness never depends on a destructor running.** No value represents a running child or a suspended task frame. An `rc` cycle (SPEC §16.4) or an unsafe leak therefore cannot end a parent's Loans early; Abort ends every observer, and divergence never returns. No `F: 'a`-style constraint and no no-leak rule are needed.
- **A design constraint for future proposals.** This proposal makes no no-leak guarantee ("every non-Owned value is destroyed before its Origins end"). `docs/SETTLED.md` records that rejection, and the sentence above as the constraint that future guards and scopes keep; they use the call-duration shape of §6.2 instead.
- **No `Pin`.** Task frames are not values, so byte-transfer Moves (impl §21.4.5) are unchanged.

## 8. Cleanup and construction

- **Deferred Blocks never suspend** (rule 1.2), and cleanup never observes cancellation. The cancellation sentence of SPEC §16.2.3 becomes: "Cleanup neither suspends nor observes cancellation; Deferred Blocks, destruction and secured results follow §16.2 unchanged." The cancellation boundary of SPEC §15.9 is resolved.
- **`drop` is the only release and never waits.** Each I/O call holds its Loans until it completes, so `drop` never meets an operation in flight.
- **Waiting finalization** (flush, graceful close) is a named operation with a task slot and `self: uniq/Self`; each path handles its `Result`. While a request applies, such a wait reports cancellation unless its operation completes first (obligation 5). To finalize even then, call it through `Async.shield`: the request reaches it only after the grace, and the other tasks keep running. A Deferred Block cannot pass the task (rule 1.2), so finalization stays on explicit paths.
- **Constructors have no task slot.** Asynchronous construction uses a factory, as SPEC §6.2.3.5 already prescribes for fallible construction.

```kimi
public struct Connection
    let socket: Net.Socket

    private init(socket: Net.Socket)
        self.socket = socket@move

    public func open(task; url: ref/Url) -> Result<Connection, Net.Error>
        let socket = try Net.Socket.connect(task; url)    // Cancellation returns Err(.Cancelled(_)).
        return .Ok(Connection.init(socket@move))

func appendLine(task; path: ref/string, line: ref/string) -> Result<(), Io.Error>
    var file = try Io.File.open(task; path)
    // defer => _ = file.flush(task;)                     // Error: a Deferred Block cannot pass the task.
    try file.write(task; line)
    return file.flush(task;)                              // Finalize on the normal path.

func appendLineAlways(task; path: ref/string, line: ref/string) -> Result<(), Io.Error>
    var file = try Io.File.open(task; path)
    let written = file.write(task; line)                  // No try: flush after every outcome.
    let finish = func [file@uniq] (task;) -> Result<(), Io.Error> => file.flush(task;)
    let grace = Time.Duration.init(microseconds: 2000000)
    let flushed = Async.shield(task; finish@move, grace: grace)   // Up to 2 s more under cancellation.
    try written@move
    return flushed@move
```

## 9. Runtime (implementation policy)

This section is policy for impl §21 and `src/Kimi/Library/README.md`. Only the definition of `run` (§9.1) and the contracts stated in §5 and §6 are normative (new SPEC §22.1.x).

### 9.1. `run` and the executor

> `Async.run(root)` starts `root` as the root of a new task tree, all of whose tasks run on the current thread, and returns the root's result when every task of the tree has completed.

```kimi
func loadConfig(path: ref/string) -> Result<Config, Io.Error>      // A synchronous function.
    return Async.run(func [path] (task;) => readConfig(task; path))
```

- **Consequences.**
  - `run` is the only way for code without a task to call task-taking operations. Inside a task it follows the same definition; a nested `run` needs no special rule.
  - No task of an enclosing tree runs inside `run`: the enclosing task is executing the call that reached `run` (rule 2). So no task of an enclosing tree sees a static slot in the Initializing state (SPEC §22.2.3). Reentry within the tree is the ordinary same-thread cycle Abort.
  - A nested `run` therefore suspends every task of the enclosing trees until it returns. An inner tree that waits for one of them, for example on a channel endpoint whose peer is an outer task, cannot progress. Code that has a task finalizes through `Async.shield` instead (§8).
  - `run` takes no task, so it is no task call. It accesses the per-thread executor state, an environment effect, so it is not `confined` (SPEC §8.4.10.2); that Kimi-internal state matches no user Loan.
  - A tree that cannot progress diverges. Detecting this is an optional diagnostic, never an Abort.
- **Executor state.** Each thread has one executor state in Kimi-internal raw storage, kept across `run` calls. It holds:
  - the completion port, created at the first association or the first blocking wait;
  - the dequeue array and the ready-list heads;
  - one timer structure;
  - the arena free lists;
  - a stack of `run` scopes.
- **Nested `run`.** A nested `run` pushes a scope. Wake-ups for tasks of outer scopes (packets, timers, channels) are held on their scope's pending list. When a scope ends, only the pending list of the scope being resumed rejoins its ready list. This implements obligation 1: an enclosing tree has an executing task, so its wake-ups wait.
- **Fast path.** If the root completes without suspending, `run` returns without touching the port or the timers.
- **Why one port.** Windows associates a handle with one completion port until the handle closes, so a port per `run` cannot work.

### 9.2. Waits and timers

- **Wakers.** A waker is a pointer to its registration (obligation 3), with no reference count, generation or allocation. The ready list is an intrusive FIFO with a `queued` bit.
- **Commit words.** Each registration holds a commit word that only its first commit writes (obligation 5). A resumed wait returns from that word, never from the task's cancellation flag. On one thread every commit runs between task steps, so the word needs no atomic operation.
- **Requests.**
  - A request sets flags along the child links, and a child started under a set flag starts with it set.
  - It commits every still-pending timer, channel and yielded `checkpoint` registration in the subtree to cancellation, unlinks timer and channel registrations in O(1), and enqueues each such task whose `queued` bit is clear.
  - A registration already committed, including one whose task is queued but not yet resumed, is left unchanged.
  - An I/O wait issues `CancelIoEx` and keeps its registration until its packet commits it.
  - The child of a `shield` has a shield bit on its task record. The walk stops there. The `shield` task frame arms a grace timer node (obligation 3), converted to a deadline like any other node, which continues the walk into the child when it expires and is unlinked if the child completes first. A shielded child does not inherit a pending request when it starts; `shield` arms the timer at once instead.
- **Timers.**
  - A timer node records its duration. Before each wait for completions, the executor reads QPC (SPEC §22.7) once and converts every newly registered node to that reading plus its duration, rounded up to the next tick. The reading is taken after the call, so `sleep(task; d)` never completes before `d` has elapsed, and the wait itself calls no foreign function (§6.3). Registration allocates nothing.
  - Wait time-outs may expire early, so after every wait the executor rereads QPC and wakes only expired nodes. The millisecond timeout is rounded up and kept below `INFINITE`.
  - A hierarchical timing wheel or an intrusive pairing heap is chosen by benchmark.

### 9.3. IOCP

```kimi
internal group IoCompletion
    #LibraryImport("kernel32", "SetFileCompletionNotificationModes")
    internal unsafe func setFileCompletionNotificationModes(handle: raw/(), flags: u8) -> i32

    #LibraryImport("kernel32", "GetQueuedCompletionStatusEx")
    internal unsafe func getQueuedCompletionStatusEx(
        port: raw/(), entries: raw/OverlappedEntry, count: u32,
        removed: uniq/u32, milliseconds: u32, alertable: i32) -> i32
```

- **Immediate success.**
  - Each handle is associated once, with `FILE_SKIP_COMPLETION_PORT_ON_SUCCESS | FILE_SKIP_SET_EVENT_ON_HANDLE`. Only handles where this succeeded are marked, and only marked handles complete an immediate success in place, without suspending or touching the port.
  - On an unmarked handle a packet still arrives, so the wait registers and parks as for `ERROR_IO_PENDING`. Completing in place there would leave the packet pointing at a dead `OVERLAPPED`.
- **Sockets.** A socket uses the mode only when its `SO_PROTOCOL_INFOW` `dwServiceFlags1` has `XP1_IFS_HANDLES`.
- **Executor round.**
  1. Dispatch a snapshot of the ready list.
  2. Call `GetQueuedCompletionStatusEx` for up to 64 entries. The timeout is 0 when tasks are ready, otherwise the next timer or `INFINITE`. `alertable` is 0, so no APCs run (SPEC §22.3.1).
  3. Map every entry to its waiter, which commits the wait (obligation 5), and enqueue it before running any task, because a nested `run` reuses the array.
- **Non-overlapped handles.** They use synchronous `ReadFile` and `WriteFile` with a null `OVERLAPPED`. This general synchronous file path is new, because SPEC §22.5.3 covers only the standard output and standard error handles, and it belongs to the `Io` design.
- **Imports.** Imports are ordinary `#LibraryImport` declarations, as in `Kimi.Windows` (SPEC §22.7.1), not WindowsRuntimeSymbols entries (SPEC §22.5.6). `BOOL` is `i32`, and `HANDLE` is `raw/()` (STYLE §4.5).

### 9.4. Arena

- **Growth.**
  - The first chunk is taken at the first push, from the smallest class of at least max(frame + header, minimum).
  - An overflow takes a class of at least max(2 × previous, needed), so a chain of depth d uses O(log d) chunks.
  - An emptied top chunk is kept as one spare, so a loop that crosses a chunk boundary does not reacquire it.
- **Reuse.** A completed task returns its chunks to per-thread, per-class free lists. The lists persist across `run` calls and are trimmed by a cap or a high-water mark.
- **Children.** Children run concurrently and take their own chunks. One-shot children usually need none (§10.3).
- **No static sizing.** Frame sizes exist only after CoroSplit and differ between O0 and O2.

## 10. Code generation (impl §21)

### 10.1. ABI and lowering

- **ABI.** Every function with a task slot is entered as `entry(task, continuation, args..., resultSlot) -> i1`, where true means completed. The task is a compiler-chosen context pointer without `noalias` (impl §21.4.2).
- **Plain instances.** Plainness is computed bottom-up for each strongly connected component of the monomorphized instance graph.
  - An instance is plain when it contains no suspension intrinsic, no indirect task call and no task call to a non-plain instance.
  - A plain instance is an ordinary function that emits no `llvm.coro.*` and always reports completion.
  - LLVM already folds such instances at O2 (verified on LLVM 22.1.8). The gain is O0 code size and arena traffic, and modules without coroutines skip the extra `opt` step.
- **Coroutines.** Every other instance becomes a switched-resume coroutine. Its frame represents the task frame, lives in the task's arena and never moves while live; this is a lowering obligation. Borrow parameters of a non-plain entry get no `captures(none)`, because the task frame keeps them past the physical return.
- **O0.** `llc -O0` cannot lower `llvm.coro.*`. A module with coroutines first runs `opt -passes=coro-early,cgscc(coro-split,coro-annotation-elide),coro-cleanup`, written without spaces because `opt` rejects them. The toolchain manifest records this step.

### 10.2. Completion protocol

```llvm
define internal i1 @g(ptr %task, ptr %cont, ptr %result) presplitcoroutine {
  ; coro.id, coro.alloc (an arena push) and coro.begin produce %hdl.
  %done = call i1 @wait_register(ptr %task, ptr %hdl)  ; true when no wait is needed
  br i1 %done, label %complete, label %wait
wait:
  %s = call i8 @llvm.coro.suspend(token none, i1 false)
  switch i8 %s, label %suspend [i8 0, label %complete
                                i8 1, label %never]
complete:
  store i32 42, ptr %result                             ; the result goes to the caller's result slot
  %inramp = call i1 @llvm.coro.is_in_ramp()
  br i1 %inramp, label %end, label %transfer
transfer:
  call void @set_next(ptr %task, ptr %cont)             ; resume the parent directly
  br label %end
suspend:
  br label %end
end:
  %st = phi i1 [true, %complete], [true, %transfer], [false, %suspend]
  call void @llvm.coro.end(ptr %hdl, i1 false, token none)
  ret i1 %st
never:
  unreachable                                           ; no destroy path (obligation 1)
}
```

1. **Result.** The result goes to the result slot that the caller passes.
2. **No destroy path.** The destroy successor of every suspend is `unreachable`. There is no final suspend: completion runs ordinary cleanup and reaches the single `coro.end`. The destroy and cleanup clones become empty at O2; at O0 their code size is recorded as a measurement.
3. **Arena position.** The caller saves the arena position and restores it at completion, whether synchronous or resumed. A plain callee needs neither.
4. **Parent resumption.** A resumed task frame that completes stores its continuation in the task record's `next` word, and the dispatch loop resumes it in the same step, bypassing the ready list. The ready list receives only wake-ups from waits. `llvm.coro.is_in_ramp()` tells synchronous from resumed completion, so the frame needs no flag.

### 10.3. Embedded child frames

```llvm
define internal i1 @join(ptr %task, ptr %cont, ptr %ta, ptr %tb, ptr %results) presplitcoroutine {
  ; The child records are locals of join's own task frame.
  %ra = call i1 @child(ptr %ta, ptr null, ptr %results) coro_elide_safe
  %sb = getelementptr i8, ptr %results, i64 4
  %rb = call i1 @child(ptr %tb, ptr null, ptr %sb) coro_elide_safe
  ; ... wait until both children complete ...
}
; At O2 and in the O0 step of §10.1, both 96-byte child frames are placed in join's frame
; (remark: frame_size=224, align=16; the arena push is 216 bytes), and the children push nothing.
```

- **Rule.** The compiler marks a `TaskBoundary.enter` call `coro_elide_safe` only when the call is in no loop body and no `while` condition. Such a call runs at most once per task frame, and by obligation 2 its child completes before that task frame does.
  - The rule covers `join`, `joinOk`, `race`, `pipe` and `shield` without naming them, and excludes the in-loop starts of `each` and `eachReceived`.
  - A call in a conditional branch qualifies, because it still runs at most once. Recursion is safe, because each recursive task frame joins its own children (obligation 2).
  - Ordinary task calls are never marked: sequential calls reuse one arena region, whereas embedded frames would be summed in the caller's frame.
- **Effect.** CoroAnnotationElide places the child's top frame in the parent's frame, so leaf children never push.
- **Conditions.**
  - Child bodies are never `noinline`.
  - The O0 coroutine step includes the pass, so O0 and O2 agree.
  - An internal switch can drop the attribute.
  - Inlining a function that contains a marked call into a loop drops the mark.
- **Hazards verified on LLVM 22.1.8 without the rule and obligation 2.** A helper that returns before its child completes lets its frame, which holds the child's frame, be reused. A marked call in a `while` condition makes children that are alive at the same time share one frame.
- **Tests.** Functional and allocation tests never depend on elision; its own regression reads the CoroSplit remark.

### 10.4. Frame contents and hot loops

- **Lifetime markers.** Without markers, every local passed by borrow lands in the frame. In a coroutine instance:
  - `llvm.lifetime.start` is emitted at the first initialization of a local's storage.
  - `llvm.lifetime.end` is emitted at the earliest proven point: scope exit, or after the last use when the storage is not address-observed and holds no destruction responsibility or live Loan (impl §21.5.5).
  - For storage lent to a task call, the last use is the caller's resumption point, never the call instruction. Ending it earlier keeps the storage on the ramp's stack while the callee's frame still points to it.
- **Hot loops.** A value live across a suspension is spilled at its definition. A loop that calls `checkpoint` every 4096 iterations therefore loads and stores the frame on every iteration and is not vectorized. On LLVM 22.1.8 at O2, only a loop in a helper that is not inlined into the coroutine was vectorized; the same loop, nested loops in one coroutine and an inlined helper were not.
  - **STYLE.** Split CPU-heavy work into an inner loop without task calls and an outer loop that calls `checkpoint`. Until compiler step 2, put the inner loop in a separate plain function, such as `sumRange` below.
  - **Compiler, step 1.** Do not inline plain functions that contain loops into coroutine bodies, and measure the effect in `src/Benchmark`.
  - **Compiler, step 2,** once step 1's gain is confirmed. Before CoroSplit, outline loops without task calls from coroutine bodies into internal plain functions, so that nesting the loops is enough.

The interim form, until step 2:

```kimi
func sumRange(values: ref/Array<u64>, start: isize, end: isize) -> u64   // No task call.
    var total: u64 = 0
    var index = start
    while index < end
        total += values[index]
        index += 1
    return total

func sumAll(task; values: ref/Array<u64>) -> Result<u64, Async.Cancelled>
    var total: u64 = 0
    var start: isize = 0
    while start < values.length
        var end = start + 4096
        if end > values.length => end = values.length
        total += sumRange(values, start, end)      // Inner work without suspension.
        start = end
        try Async.checkpoint(task;)                // Suspends only between chunks.
    return .Ok(total)
```

### 10.5. Allocation bounds

| Operation | Heap allocations |
| --- | --- |
| Task call | 0: an arena push, none for a plain callee |
| One-shot children with elision | 0: frames inside the parent's frame |
| Task start, `each` child | Pooled records and arena chunks, at most `limit` child records at once for `each`; 0 in steady state |
| Timer, channel operation, I/O wait | 0: registrations live in the task frame; ring buffers |
| `pipe` start | 0 for capacity 0; otherwise an arena push, 0 in steady state |
| `shield` | 0: one grace timer node in its task frame |
| Repeated `run` | 0 in steady state, because the per-thread state persists |

`NativeAllocationAudit`-style O0 and O2 fixtures with `Purpose=Allocation` fix these bounds:

- a loop of N task calls that cross a chunk boundary;
- N top-level `Async.run` calls;
- a read loop on reads that complete immediately, which makes no port calls;
- a pipe ping-pong, which makes at most one port call per round.

`src/Benchmark` measures timing: the cost of each `run`, cost against chain depth, and timeout-heavy workloads.

## 11. Compiler services

| Diagnostic | Primary range | Related locations | Repair |
| --- | --- | --- | --- |
| `ComparisonLoanConflict_Kd` (existing in the implementation), Reason `suspension point (other tasks)` | The call, as for every record of the static call-effect comparison | `loan`, `static`, `suspension` (the task argument), `use` | Advice only: copy the value before the call, or borrow again after it |
| `TaskSlotPosition_Kd` (new) | The task slot, the task argument, or the Name `task` | The innermost function's task slot when it has one; the Deferred Block or the defaulted parameter for those positions | Advice by Reason, below |
| `TaskArgumentMismatch_Kd` (new), Reason `missing` or `extra` | The call's argument list, or the task argument | The callee declaration | Missing: `Repair.PassTask` (new) under the conditions below, otherwise Advice. Extra: Advice, the callee has no task slot and does not suspend; remove `task;` |
| `TaskSlotShapeMismatch_Kd` (new) | The later declaration's task slot, or its list start | The declarations of the group with the other shape | Advice: use one shape per Name; a function that may suspend takes the task slot under its own Name |
| Any other `;`: not part of a task slot, a task argument, or a `task;` that `TaskSlotPosition_Kd` reports | The `;` | — | The existing syntax diagnostic, whose Reason states that `;` occurs only after a leading `task` in the lists of rule 1.1 |
| A form ahead of its stage | — | — | An `Unsupported` code (SPEC §23.3.6.1) |

- **Code naming.** `ComparisonLoanConflict_Kd` exists in the implementation, but SPEC §15.6.4 names no code for its static call-effect comparison today. This change names it in SPEC §15.6.4 and §23 for that comparison, which now includes the task-call comparison of rule 2.
- **One record per Loan.** The task-call comparison is part of the static call-effect comparison, so it adds a Reason to that comparison, not a new requirement. When the same call's summarized or unknown environment effects also conflict with the Loan, one `ComparisonLoanConflict_Kd` record carries both Reasons (SPEC §23.3.6.4). The Reason states that available bounds (`confined`, `preserves results`) do not exclude the task-call comparison. `CallEffectConflict_Kd` keeps covering effects through inputs on Loans of earlier results (SPEC §8.4.10.6) and is unaffected.
- **`TaskSlotPosition_Kd`.** It covers a task slot or task argument at a position that rule 1 forbids, with the position as a Reason value; the message follows the form of `UnsafeFunctionValue_Kd`. Recovery also recognizes `task` followed by `;` after another item of an admitting list and at the start of the lists under "No list for it", although `task` is an ordinary Name there. It reads such a `task;` as a task slot or task argument, so no `;`, missing-Type or unknown-name record follows.
  - **Not first.** A `task;` after another item of its list. Advice: write it first.
  - **No caller passes a task.** A task slot in a constructor, a foreign function declaration, an Application's `main` or a test function. Advice for a constructor: a factory function with a task slot (SPEC §6.2.3.5); for a foreign declaration: remove the task slot and call the import from a function that has one; for `main` and a test function: call `Async.run` in the body.
  - **No list for it.** A `task;` at the start of an accessor signature, an Attribute or `$` operation argument list, an inferred Case payload or a Pattern.
  - **No task slot to pass.** A task argument where the innermost function has no task slot. Advice: give that function a task slot (its call signature changes, so its callers then pass `task;`), or use `Async.run` in code without one.
  - **Cleanup and defaults.** A task argument directly in a default expression or in the body of a Deferred Block that the function registers. Advice: move the call onto each path, through `Async.shield` when it must run under cancellation.
  - **Unresolved `task`.** When a function that lexically encloses the use has a task slot, a Name or capture entry `task` that resolves to nothing, with the Note that the task slot is no value, is passed as `task;`, and can be passed only directly in a function that itself has a task slot, never from a nested anonymous function without one. As the first argument of a call whose callee has a task slot, the use is reported instead as a missing task argument, whose `Repair.PassTask` replaces the Name `task` and the comma after it, if any, with `task;` (`foo(task, x)` becomes `foo(task; x)`, `foo(task)` becomes `foo(task;)`).
- **`TaskArgumentMismatch_Kd`.** One requirement, rule 1.2: a call writes the task argument exactly when its callee has a task slot. Because one function group has one shape (rule 1.4), the Reason is known from the Name or the Function Type before overload resolution, and the task argument binds no parameter, so adding or removing it changes no other matching. A qualified Case construction, a construction `T.init(task; x)` and a base initializer are calls whose callee has no task slot (`extra`). When the callee has no task slot, `extra` is the only record for that task argument, wherever it is written; the `TaskSlotPosition_Kd` Reasons "No task slot to pass" and "Cleanup and defaults" apply only to a task argument whose callee has a task slot.
- **`TaskSlotShapeMismatch_Kd`.** Rule 1.4, located and related as `ParameterShapeMismatch_Kd` is (SPEC §7.3.1): once per later declaration of a group formed by declarations, and at the called Name for a group gathered at a use. A call that the violation leaves without a selection is derived from the declaration error (SPEC §23.3.6.4). Task-slot presence is not part of a function Signature (SPEC §9.1); when the two declarations also have equal Signatures, the duplicate-declaration record is published as well, with the Note that a task slot does not distinguish overloads.
- **Compatibility.** A task-slot mismatch under rule 1.5 keeps the existing record of its site, with the task slot as a Reason value: a missing Contract implementation, whose record relates a same-named declaration that differs only in the task slot (SPEC §8.4.5); an unsatisfied Callable Constraint that leaves no applicable candidate (SPEC §8.6, §10.1 step 5); a Type mismatch at a common Function Type (SPEC §10.2); or a specialization without a target, whose record relates the same-named original that differs only in the task slot (SPEC §8.8.1).
- **`Repair.PassTask`.** On a `missing` record, it inserts `task;` right after the `(` of the argument list, followed by one space when an argument follows on the same line (`bar(task;)`, `foo(task; x)`). On a record that comes from an unresolved first-argument Name `task`, it makes the replacement described under `TaskSlotPosition_Kd` instead. `Selection` does not apply, because the task argument binds no parameter; `UsageLegality` is required, because the repaired call is a task call that rule 2 compares (SPEC §23.3.6.9). It is offered only when:
  - the innermost function that contains the call, anonymous functions included, has a task slot;
  - the call is not directly in a default expression or in the body of a Deferred Block that the function registers.

  Either edit is offered only under these conditions; otherwise the record carries the Advice of the corresponding `TaskSlotPosition_Kd` Reason.
- **Suspension listing.** The CSP lists each suspension point with facts local to the signature and body:
  - the task slot;
  - the Loans and owned values live across the point;
  - whether the point may report cancellation: the selected callee's instantiated result Type reaches `Kimi.Async.Cancelled` (identified by declaration identity) through enum payloads, Tuple elements or `Option` and `Result` arguments. This is a may-fact, unknown for an abstract Type argument, and never derived from bodies (SPEC §18.7.2);
  - for calls that start children, the Loans that the children capture and their regions;
  - for `shield` calls, the grace.
- **Generation facts.** Whether an instance is plain is a fact of the monomorphized instance graph (§10.1). It is reported per instance, apart from the local facts.
- **Frame size.** Frame size is a measurement bound to the O level and toolchain identity, read from CoroSplit remarks (SPEC §23.5.3), not a check fact.
- **Hover.** Hover uses the typed inspection block of SPEC §8.4.10.6. Semantic tokens may mark the task argument of every task call.
- **Checking cost.** For rule 2, each body precomputes a mask of its Loans anchored at mutable statics, and each task call ANDs it with the live-Loan set (guidance in `docs/dev`).

## 12. Threads (stage 3 direction)

Stage 3 adds threads only as structured offload of computation. It requires `2026-10-04 Callable Effect Bounds.md`.

```kimi
public group Async
    public func offload<F, R>(task; child: F) -> R
        F is Callable<owner, () -> R>
            effect confined
        F is Transferable

func checksumAll(task; blocks: ref/Array<Block>) -> u64
    let work = func [blocks] () -> u64 => checksum(blocks)   // Borrows the caller's data; takes no task.
    return Async.offload(task; work)                          // Task call: runs on a worker, then joins.

//  func [config@ref] () -> u64 => use(config)               // Error: the input reaches an rc handle.
//  func [] () -> u64 => Metrics.count                       // Error: confined excludes mutable statics.
```

1. **Operations.** `Async.offload(task; child) -> R` and `Async.offloadEach(task; items, child: ref/F ! limit: isize) -> Result<(), Async.Cancelled>`.
   - Both are task calls. The child runs to completion on a worker thread and is joined before the operation returns.
   - `offloadEach` requires an `Iterator` and follows the `each` rules of §6.2 for `next`, the Iterator and cancellation: it calls `next` on the parent's thread, checks for a request before each pull, stops pulling once one applies, and returns `Err` after every worker child is joined.
   - No task runs on a worker: the child's signature has no task slot, and `confined` excludes `Async.run` (§9.1). Structured operations such as `join` never change threads.
2. **Borrowing.** The parent is suspended until the join, so the child may borrow the parent's locals as `ref` or `uniq`, as with Rust's `thread::scope`. A new obligation 6 provides this: a worker child completes and is joined before the `offload` task frame completes, and until then that task frame treats the child's Loans as live, as obligation 2 requires after `enter`. `offload` children do not pass through `enter`, so obligation 2 does not cover them. Obligation 6 joins the trusted base of §5. No Owned is required.
3. **Effects.** The child must be `confined`, which excludes authority obtained from the environment: direct access to mutable statics, foreign calls, Console output and raw pointers read from immutable statics. A borrow of mutable static storage that the child receives through a capture is authority from an input; rule 2 rejects it, because it is a potentially affected static Loan of an argument of the task call. A shared borrow of a static `let` slot is never affected and may cross, subject to `Transferable`. No per-thread rules for statics are needed.
4. **Transfer requirement.** `offload` and `offloadEach` declare the compiler-intrinsic Constraint `Transferable` on the child Type and the item Type, as APIs declare `Owned` (SPEC §15.2.3). Like Owned, it is derived from the complete Type rather than declared by conformance, by the same traversal; generic callers prove it from their own premises, because instantiation adds no semantic use conditions (SPEC §8.10).
   - A `Transferable` Type reaches no `rc`, no `Weak` of an `rc` and no user raw pointer.
   - Erased Function Types, runtime-Contract views and base-object views (SPEC §15.8.1) are not crossed, because their hidden contents cannot be traversed.
   - Kimi-internal raw pointers, such as the iteration remainders of SPEC §22.1.2.5, pass under a Kimi obligation bound to declaration identity.
   - The result `R` is unconstrained.
5. **Tightening of `confined`.** Reading a value that contains an `rc` handle, or its `Weak`, from an immutable static becomes an environment effect, for the same reason as the raw-pointer row of SPEC §8.4.10.2.
6. **Runtime.** Static initialization becomes a cross-thread once-protocol: a thread waits while another initializes, and only a same-thread cycle Aborts.
7. **Memory model.** Safe code has no data races. Synchronization arises only from `offload` start and join and from the `arc` protocol (impl §21.2.3.3).
8. **Deferred.** Offloading blocking foreign calls waits for foreign effect declarations (SPEC Appendix D). Per-thread I/O executors and cross-thread channels can be added later with the same requirement; cross-thread channels keep the commit points of obligation 5, with a compare-and-swap commit word that a party must win before it moves a value. An overlapped handle stays bound to the port of the thread that opened it.

## 13. Specification changes

### 13.1. Changes by location

| Location | Change |
| --- | --- |
| New Chapter 24, "Suspension and asynchronous tasks" | Rules 1 and 2, the terms of §4, and references to the facts of §4.3 |
| SPEC §1.2, §2.2, §2.4 | `;` occurs only as the separator of the task slot or task argument `task;`; it is never a statement separator or expression-body terminator, and every other occurrence outside comments and literal content is an error. In §2.4, `;` moves from "Recognized but unavailable" to "Structural" |
| SPEC §2.5.1 | `task` becomes a contextual keyword (the first token after the `(` of a list of rule 1.1, before `;`); `open` becomes a contextual keyword (§13.3) |
| SPEC §3.2, §3.2.1, §3.2.3, §3.8 | A Function Parameter List may begin with the task slot: the table gains `(task;) -> U` and `(task; T) -> U`, and a list in Type position that begins with `task;` is always a Function Parameter List followed by `->`. Function Type identity and the compatibility row include task-slot presence |
| SPEC §6.2.3, §6.5.1, §22.2.2, §22.3.1 | References to rule 1.1: a constructor, a test function, an Application's `main` and a foreign import have no task slot |
| SPEC §7.2.1, §7.2.2, §7.3, §7.3.1 | The task slot precedes the receiver and both sections of the `!` boundary, belongs to neither and counts in neither `N` nor `K`; `self` stands at any written position after it; it is no parameter and corresponds to none (rule 1.3); one function group has one task-slot shape (rule 1.4) |
| SPEC §7.6.1, §7.6.3, §10.5, §10.8, Appendix E | An anonymous function has a task slot exactly when its header writes one; the internal call signature and known and fixed call signatures include task-slot presence, and matching fails on a difference |
| SPEC §8.4.1, §8.4.5, §8.4.6, §8.6, §8.8.1, §8.8.2 | Contract requirements may declare a task slot; implementation identification and candidate equivalence compare its presence; a Callable signature `S` is `(A1, ..., An) -> R` or `(task; A1, ..., An) -> R`, with per-call Origins only for `A1` to `An`; a specialization restates its original's task slot |
| SPEC §8.4.10.2 | The inputs are the receiver, each argument and the task slot; the current task's state reached through the task slot is no environment effect and matches no user Loan |
| SPEC §9.1, §10.1, §10.7, §12.4.2 | Task-slot presence is not part of a function Signature and cannot by itself distinguish overloads (rule 1.4). A call writes the task argument exactly when its callee has a task slot; the task argument binds no parameter; Function values keep positional calling after it; callable compatibility and common Function Type conversion require the same task-slot presence |
| SPEC §15.6.2 | Variance, handle destruction and receiver preservation hold unchanged for tasks on one thread: tasks interleave only at task calls (rule 2), and another task reaches a task's state only through Loans given to its children (argument dependencies of the call that starts them), static storage, whose Loans every task call compares, `rc` and `arc` payloads, which grant shared access only, and Kimi-internal raw storage under obligations 3 and 4. Threads must revisit them |
| SPEC §15.6.4 | A reference to rule 2; names `ComparisonLoanConflict_Kd` for the static call-effect comparison, including the task-call comparison |
| SPEC §22.2.3 | A reference to rule 2: while a task executes a call that is not a task call, no other task of its tree that started before that call runs |
| SPEC §16.1, §16.2.3, §15.9 | A reference to rule 1.2 (a Deferred Block cannot pass its function's task); cleanup neither suspends nor observes cancellation; the cancellation boundary is resolved |
| SPEC §22.1 | New SPEC §22.1.x with the task boundary (four intrinsics, the trusted base, obligations 1–5 with the commit points of obligation 5), the definition of `Async.run`, and the `Kimi.Async` declarations and contracts of §6, including `shield` and the `sleep` deadline |
| SPEC §22.4 | None now; the later Console change of §6.6 |
| SPEC §8.4.10.6, §23.3.6, §23.5.3 | The suspension Reason of `ComparisonLoanConflict_Kd`; `TaskSlotPosition_Kd`, `TaskArgumentMismatch_Kd` and `TaskSlotShapeMismatch_Kd`; `Repair.PassTask`; the suspension listing; frame size as a measurement; raw-anchor facts |
| SPEC Appendix D, E, F | SPEC §D.2: single-thread tasks are specified, threads remain deferred. New terms: task, task tree, subtree, task frame, task slot, task argument, task call, suspension point, static Loan. Appendix F: the productions of §13.3 |
| Unchanged | SPEC §8.1.3, §15.4.3 and §22.3.2: a task slot is no Type and no parameter; Chapter 17 |
| impl §21 | Task ABI, plain instances, coroutine lowering with frames that never move, the O0 step, the completion protocol, child-frame elision, lifetime markers, the inlining policy for hot loops and, once measured, automatic loop outlining (§10.4), and a context pointer without `noalias`. impl §21.4.5 is unchanged |
| impl §21 or `src/Kimi/Library/README.md` | Executor, timer, IOCP and arena policy (§9) |
| `docs/dev` | The rule-2 Loan mask (§11); the tokenizer's `;` rule in `DIAGNOSTICS.md` |
| `docs/STYLE.md` | §13.2 |
| `docs/LIBRARY.md` | The `Kimi.Async` declarations and their contracts, when they are implemented |
| `src/kimi-ext` | The TextMate grammar treats `open` and `task` as contextual and highlights `task;` (§13.3), following `src/kimi-ext/AGENTS.md` |
| `docs/SETTLED.md` (on adoption) | An entry for the rejected no-leak guarantee, whose reason records the constraint "safe-code soundness never depends on a destructor running" (§7.3), and entries for the rejected alternatives of §15.2 |
| Stage 3 | `2026-10-04 Callable Effect Bounds.md`; the `Transferable` Constraint; the SPEC §8.4.10.2 row for `rc` read from immutable statics; cross-thread static initialization; obligation 6 for `offload` children |

### 13.2. `docs/STYLE.md`

STYLE gains:

- **APIs.** No task-free or `Async`-suffixed counterpart of a task-taking operation (§6.6). No `try` prefix on task-taking operations that return `Result` (§6.1). Named `limit`, `capacity` and `grace`.
- **Children.** Received by intent (STYLE §4.3): consumed when called once, shared when called concurrently.
- **Cancellation.** One `Cancelled` Case per domain error enum.
- **Finalization.** A waiting finalization is a named operation, called through `Async.shield` when it must run under cancellation. Asynchronous construction uses factories.
- **Bridging.** In a body with a task slot, pass the task on; `Async.run` bridges only from code without one, because it suspends the enclosing tree.
- **Hot loops.** An inner loop without task calls and an outer loop that calls `checkpoint`, with the inner loop in a plain function until automatic outlining exists (§10.4).

The position and spelling of the task slot are grammar (rule 1), so STYLE needs no naming rule for it; its canonical whitespace is stated in rule 1.1.

### 13.3. Contextual keywords `task` and `open`

The task slot needs the contextual keyword `task`, and the Kimi APIs of this proposal name operations `open`, such as `Io.File.open` and the factory `Connection.open` (§8). `open` is a reserved keyword today (SPEC §2.5.1, class "Access and inheritance"), so it could not be a Name. Both changes are applied together with the rest of this proposal.

- **SPEC §2.5.1, reserved table.** The class "Access and inheritance" becomes "Access", with `public`, `internal`, `private` and `protected`.
- **SPEC §2.5.1, contextual table.**
  - A new row, "Inheritance modifier": `open` in a declaration's leading modifier sequence. It is recognized as the unavailable modifiers are: before the declaration introducer of the same logical header, without scanning across a newline, indent or dedent. Elsewhere it is an ordinary Name, such as a function, Field, local or member Name.
  - The row "Parameters and accessors" gains: `task` as the first token after the `(` of a list of rule 1.1, when the next token is `;`. Elsewhere `task` is an ordinary Name.
- **SPEC §1.2, §2.2, §2.4.** `;` moves from "Recognized but unavailable" to "Structural". The first sentence of the SPEC §2.4 paragraph becomes: "`;` occurs only as the separator of the task slot or task argument `task;` (§2.5.1, §24.2). It is never a statement separator or expression-body terminator, and every other occurrence outside comments and literal content is an error." The sentences on separate effective lines and the `if`/`else` example stay unchanged. The SPEC §1.2 notation row and the SPEC §2.2 sentence on separators say the same.
- **Validity of `open`.** Unchanged: `open` is valid only immediately before `struct` (Appendix F, `StructureDeclaration := Access? "open"? "struct" ...`). Elsewhere in a leading modifier sequence it keeps the existing diagnostic that `open` applies only to structures.
- **Meaning of `open`.** Unchanged: `open` permits derivation and is an inheritance modifier, not an access level (SPEC §6.2.2, §9.3).
- **Unavailable modifiers.** The paragraph of SPEC §2.5.1 is unchanged; `abstract open struct` still receives the unavailable-feature diagnostic.
- **Appendix F.** The `StructureDeclaration` production is unchanged; only the class of the `open` token changes. A new production `TaskSlot := "task" ";"` is an optional prefix of exactly these lists:
  - `ParameterList<P>` (named functions, Contract requirements and constructors; constructors, foreign declarations, an Application's `main` and test functions reject it, rule 1.1);
  - the `SpecializationDeclaration` and `FunctionExpression` parameter lists;
  - `FunctionParameters` (Function Types and the `FunctionSignature` of `CallableRequirement`);
  - the argument lists of the call `PostfixSuffix`, `ConstructionExpression` and `BaseInitializer`.

  The ParameterList prose gains: the task slot precedes both `!` sections and the receiver, and no comma follows its `;`. The F.9 row "Function parameters" gains: the leading task slot and the task argument `task;` are defined by Chapter 24 (rule 1). Accessor signatures, `DropDeclaration`, Attribute and `$` operation arguments, `InferredCaseExpression` payloads and Patterns do not admit it.

```kimi
public open struct Base                             // Before `struct`: the inheritance modifier.

open func helper() -> () => ()                      // Error: `open` applies only to structures.

func appendTwice(task; path: ref/string, line: ref/string) -> Result<(), Io.Error>
    var file = try Io.File.open(task; path)         // `open`: a member Name; `task;`: the task argument.
    let open = 2                                    // A local Name.
    let task = open                                 // `task` without `;` is an ordinary Name.
    var count = 0
    while count < task
        try file.write(task; line)
        count += 1
    return file.flush(task;)
```

Implementation:

- The tokenizer's keyword classification (SPEC §2.5.1) and the `;` token, and the parser's recognition of the leading modifier sequence and of the task slot.
- The kimi-ext TextMate grammar (`src/kimi-ext/syntaxes/kimi.tmLanguage.json`) and the semantic-token classification of contextual keywords.
- Diagnostic regressions:
  - `open` and `task` as function, Field, local and member Names;
  - `public open struct` and `abstract open struct`;
  - `open func` and `open enum`, rejected as today;
  - an `open` expression at the start of a line, which must not consume the declaration on the next line;
  - `(task;)`, `(task; x)`, `(task; ! count: i32)` and `(task)` in declarations, calls and Function Types, and `(task;)` without `->` in Type position;
  - a `task;` after another item, `(task;,)`, `task;` in accessor, Attribute and Case lists, and any other `;`.

## 14. Staged plan

- **Stage 0: specification.**
  - Write rules 1 and 2; implement the rule-1 checks with `TaskSlotPosition_Kd`, `TaskArgumentMismatch_Kd`, `TaskSlotShapeMismatch_Kd` and `Repair.PassTask`, and the rule-2 comparison. Execution reports `Unsupported`.
  - Apply the keyword changes of §13.3: SPEC §2.5.1, the tokenizer, the parser, the kimi-ext grammar and their regressions.
  - **Prerequisites** (incomplete in `docs/STATUS.md`): the SPEC §15.6.4 caller comparison of environment effects with static Loans, and mutable static Fields.
- **Stage 1: the smallest sound subset.**
  - Task ABI, coroutine lowering with the O0 step, and the four intrinsics.
  - `run`, `sleep`, `checkpoint`, `shield`, `join`, `joinOk`, `race` and `each`, with the per-thread executor state and timers.
  - Diagnostics and the CSP listing.
  - **Prerequisites** (incomplete in `docs/STATUS.md`):
    - Callable Constraints with owner-acquisition witnesses and arbitrary callable parameters and results;
    - Closures with aggregate captures;
    - borrowing entries and lending-loop dispatch.
  - Until these exist, stage 1 covers sequential task calls and `join` over Closures with scalar captures; other forms report `Unsupported`.
  - Tests include a generic `each` checked against a user `Iterator`, and `shield` grace expiry, a request already pending at the call, a request made inside the body, and nested shields.
- **Stage 2: channels and I/O.**
  - Channels with `uniq` endpoints, `pipe` and `eachReceived`.
  - Commit-point regressions in both directions and for both capacity kinds: a taken value or delivered item whose waiter is cancelled before it resumes; a pending `send` when `pipe` closes the receiver; a timer and a delivery in one executor round under `race`; waits started under a pending request; `shield` around a pending `send` or I/O wait when the grace expires.
  - IOCP for files, pipes and sockets, with the immediate-success path and batched dequeue.
  - Cancellation of I/O waits (`CancelIoEx`, then the packet), so `race`-based timeouts also bound I/O.
  - The inlining policy for hot loops, measured in `src/Benchmark`; then automatic loop outlining, once the policy's gain is confirmed (§10.4).
- **Stage 3: threads.** Structured offload (§12), after `2026-10-04 Callable Effect Bounds.md`.
- **Separate future proposals.**
  - Console output in task form, with the rules for output from code without a task (§6.6);
  - synchronous generators usable by `for`;
  - channels with several senders, and `select`;
  - a deterministic scheduler with virtual time;
  - Origin-bounded erasure;
  - an asynchronous `main` (SPEC §22.2.2);
  - foreign effect declarations and offloading blocking calls.

## 15. Design decisions

### 15.1. Decisions

1. **Placement.** A new Chapter 24 (§4).
2. **Task-slot syntax.** The task slot is written `task;` before the receiver and every ordinary parameter, in declarations, calls and Function Types, with the contextual keyword `task` (§4.1, §13.3).
3. **Cleanup.** Deferred Blocks never suspend; finalization is written on explicit paths and runs through `Async.shield` under cancellation (§8).
4. **Constructors.** `init` has no task slot; asynchronous construction uses a factory (§8).
5. **Fail-fast join.** Named `joinOk` (§6.2).
6. **`limit`.** Required, without a default (§6.1).
7. **Leaks.** No no-leak guarantee; SETTLED records the constraint of §7.3.
8. **Console.** Moves to the task form in a later change (§6.6).
9. **Stage-3 prerequisite.** Effect bounds on Callable Constraints are a separate proposal, completed before stage 3 (§12).
10. **Hot loops.** STYLE guidance plus compiler work: first the inlining policy, then automatic outlining (§10.4).
11. **Terms.** "Task frame" instead of "activation", which SPEC §15.6.7 already uses (§4).
12. **`each` and cancellation.** `each` stops pulling once a request applies and reports it (§6.2).
13. **`open`.** Becomes a contextual keyword, so that Kimi APIs such as `Io.File.open` can use the name (§13.3).
14. **Trusted base.** The obligations of the task boundary are stated as Kimi's trusted base, as unsafe obligations that the Kimi library bears (§5).
15. **One shape per group.** A function group has one task-slot shape, so a task slot never distinguishes overloads (rule 1.4).
16. **Task state and clocks.** Kimi code reaches task state only through `TaskBoundary.current`, and waits read no clock, so they stay usable in `confined` implementations (§5, §6.3, §9.2).

### 15.2. Rejected alternatives

| Alternative | Reason |
| --- | --- |
| `async`/`await` as a Function Type category with keywords | It needs two keywords and an implicit current task for cancellation and spawning (against the explicit context of Principle 2). The task slot carries the authority itself |
| A parameter `task: Async.Task` whose Type name is recognized by declaration identity | It looks like an ordinary parameter, and a call site shows no suspension without the callee's signature. It also needs a name that declares no Type and an exception in the Core role of SPEC §9.2; the task slot is grammar, so every call shows it and no Name or Core-role exception is needed |
| `(task)` and `foo(task)` without `;` when no other parameter or argument follows | Ambiguous with an ordinary parameter or argument named `task` and with a grouped Type named `task`; `task` is too common an identifier to reserve |
| Overloading one Name with and without a task slot | A forgotten `task;` would silently select the variant that does not suspend, and one operation would have two forms (Principle 1); the receiver-shape rule already excludes the analogous receiver pairs |
| A clock read inside `sleep`, `shield` or `checkpoint` | It is a foreign call, an environment effect, so waits could not be used in `confined` implementations; the executor reads the clock after the call instead, which keeps the deadline guarantee |
| Reserving `task` as a keyword | It is a common identifier (jobs, work items); a contextual keyword before `;` is unambiguous |
| Keeping `open` reserved and naming operations `connect` or `openPath` | `open` is the natural name for opening files and connections, and a reserved spelling would force a second vocabulary. As an inheritance modifier, `open` is valid only before `struct`, so contextual recognition is unambiguous (§13.3) |
| The authority as an ordinary `uniq/Async.Task` Type | It needs a list of position bans and a separate fix for associated-Type bindings; a task slot is no Type and excludes them by construction |
| Preventing escape with Origins | The hazard is which frames run, not lifetime (§4.3) |
| A positive "scheduling extent" rule for `resume` | Every point inside a task is within some `run` and some task call, so a containment rule excludes nothing; rule 2 and obligation 1 state the guarantee negatively |
| Comparing every active Loan at a task call, not only static Loans | Other tasks cannot reach this task's locals and parameters (§7.1), and Loans on them across a suspension are the point of the design |
| Futures and `Pin` (Rust) | A second movability concept, conflicting with impl §21.4.5 |
| Stackful tasks (Go) | Hidden suspension (Principle 2); raw pointers into stacks make relocation impossible |
| Writing `await` next to the task argument | Two spellings of one fact (Principle 1; the reason of the SETTLED entry "A string concatenation operator") |
| Join handles, `async let`, detached tasks | Soundness would depend on destruction, and children would gain more than one way to start |
| Implicit waiting for children at scope exit | Hidden suspension (Principle 2); cleanup never suspends (§8) |
| Cancelling by dropping a future, or an asynchronous `drop` | It needs an abrupt completion that SPEC §16.2.3 lacks; `drop` never waits (§8) |
| Actors | Reentrancy at every suspension defeats local reasoning (Principle 2) |
| Failable constructors that report cancellation | A second form of fallible construction beside the factory (Principle 1), plus base-layer failure propagation and a new construction-expression Type |
| A no-leak guarantee | It would bind every future cycle-forming API and unsafe code, and this design does not need it (§7.3) |
| A default for `limit` | The degree of concurrency decides memory use and external load, so a default would hide a resource decision (Principle 3); an unlimited default is unsafe for `eachReceived` |
| Observing through a shared and suspending through an exclusive task | Without a Type there is nothing to borrow, and on one thread a request is issued only while the observer is suspended |
| A public `cancelSiblings` | Ambient authority: a deep callee could cancel its siblings (Principle 2) |
| Deferred Blocks that suspend | Resume states in the cleanup of every exit path; §8 meets SPEC §16.2.3 trivially instead |
| Deferred Blocks that ignore cancellation | Every exit path would carry an implicit, unbounded mask, so outer timeouts and `joinOk` failure propagation would stop working. `Async.shield` masks one explicit call for an explicit grace |
| Finalizing in a nested `Async.run` | Rule 2 suspends the enclosing tree for its duration: outer timeouts cannot fire, every other task stops, and an inner wait on an outer task's endpoint never progresses |
| An unbounded `shield`, or a default grace | A deep callee could hold an ancestor's timeout or `joinOk` failure indefinitely; a default would hide the delay (Principle 3), as for `limit` |
| The name `protect` | `protected` is an access keyword (SPEC §2.5.1), and the name does not say what is withheld |
| Restricting `Async.run` to the program entry | A dynamic check is the rejected Abort on a nested `run`, and lazy static initialization would make it depend on which read comes first. A static ban on direct calls in bodies with a task is bypassed by any synchronous helper, and a complete one would need the rejected blocking effect (waits generic over a blocking capability, below) |
| An `IndependentIterator` Contract, or Owned-only items for `each` | `Iterator` already guarantees retention (SPEC §22.1.2.4); a second Contract duplicates it (Principle 1), and Owned-only items would reject `iterateUniq` |
| The name `Async.parallel` for `offload` | `offload` names the action; with one child the caller is suspended, so nothing runs in parallel with it |
| `joinAll`, `joinFailFast` or `all` instead of `joinOk` | Indistinct from `join`, verbose, or silent about what is awaited |
| An executor value (`Async.Runtime`) | It conflicts with one completion port per handle and adds a parameter to every synchronous bridge; a deterministic scheduler is a future proposal |
| Static arena sizing | Frame sizes exist only after CoroSplit |
| Waits generic over a blocking or a task capability (`Async.Blocking`, `Async.Wait`, `Async.block`) | The effect is the same whichever capability is passed, so the polymorphism adds nothing; a nested `run` is enough |
| Aborting on a nested `run` | Whether it Aborts would depend on dynamic context (Principle 2) |
| Rule 2 as a row of the effect summary | The authority comes from an input, so it is no environment effect, and the row would spread to callers that never suspend |
| Mutating channels through `ref/Self` | Shared mutation, a new mutability concept, and a route to non-Owned `rc` cycles |
| Algebraic effect handlers (Koka, OCaml 5) | Dynamic handler lookup (Principle 2); unresumed continuations need cleanup rules that SPEC §16.2.3 lacks; Koka's multi-shot resumption would duplicate Non-Copy state; exception and yield effects would duplicate Chapter 17 and LendingIterator |
| An `effect suspends` bound | A bound restricts what a call may access (SPEC §8.4.10), but suspension changes how callers call; inferring it from bodies would hide it (Principle 2) |
| Effect polymorphism (`reasync`) | A generic body is verified once (SPEC §8.10), so it gains little; it stays deferred with general effect systems (SPEC Appendix D) |

### 15.3. Other languages

| Aspect | C# | Rust | Swift | Go | Kotlin | This proposal |
| --- | --- | --- | --- | --- | --- | --- |
| Suspension | Heap state machine | `Future` value with `Pin` | Non-movable task-allocator frames | Stackful, copied stacks | CPS with `Continuation` | Stackless task frames at fixed addresses, not values |
| Marker | `async`/`await` | `async`/`.await` | `async`/`await` | None | `suspend`; unmarked calls | The task argument `task;` |
| Structure | By convention | Only `thread::scope` is sound | `TaskGroup`, `async let`, plus detached tasks | `errgroup` by convention | `CoroutineScope`, plus `GlobalScope` | Structured only |
| Cancellation | Token and exception | Dropping the future | Cooperative flag and error | `context.Context` | `CancellationException` | A request as task state; a `Result` value at each observing wait |
| Generators | Separate `yield return` | Separate, unstable `gen` | `AsyncStream` | Push iterators | `sequence {}` on the same mechanism | `pipe` on the same mechanism, with no value |

Taken: static frame sizes and precise borrow checking (Rust); fixed frames in a per-task arena and cooperative cancellation (Swift); the job tree and fail-fast join (Kotlin); channels that hand over ownership (Go); second-class capabilities (Effekt); bounded shielded cancel scopes (Trio); and cancellation that travels with an explicit argument (C#'s `CancellationToken`, Go's `context.Context`), here the task slot, observed as a `Result` value instead of an exception.

### 15.4. Kimigayo Principles

| Principle | How this proposal meets it |
| --- | --- |
| 1. One Concept, One Canonical Form | One suspension marker (`task;`), one way to start children, one cancellation Type, one way to finalize under cancellation (`shield`), one sequence form, and no synchronous/asynchronous API pairs |
| 2. Local Reasoning | Task calls are recognized by their syntax; during a call that is not a task call, no task of the same tree that started earlier runs; there is no inference and no implicit current task; child lifetimes follow call duration; implicit code never suspends |
| 3. Explicit Semantics | Suspension authority and child consumption appear in signatures; a cancellation request is task state set only by Kimi operations, and its observation is an `Err` in the wait's result Type; every resource bound (`limit`, `capacity`, `grace`) is written at the call; hidden allocation is limited to arena pushes, with bounds fixed by regressions |
| 4. Compiler Server Protocol | Listings of suspension points and task boundaries, existing codes with Reason values, repairs whose conditions are verified from facts, and measurements bound to configuration |

### 15.5. Verification record

1. **Design.** Five independent designs (Rust/C#, Swift, Go, Kotlin, effects) were scored and merged, then attacked from three lenses: ownership soundness, consistency with the specification and Principles, and rule minimality.
2. **Improvements.** Four lenses (rule reduction, consistency, performance, applicability) proposed improvements; each was adversarially verified and cross-checked for conflicts.
3. **Drafts.** Reviews from several lenses (syntax and citations, consistency and soundness, completeness, LLVM and Win32 facts, English) checked the drafts, and outside suggestions on the scheduling invariant, commit points, shielding, wording, `each` lifetimes, threads, `race`, the task-slot syntax, static Loans, task suspension and the trusted base were each evaluated against the specification.
   - The LLVM claims were tested on the pinned LLVM 22.1.8: `coro.is_in_ramp`, `coro_elide_safe` elision and its hazards, the folding of coroutines without suspension, empty destroy and cleanup clones at O2, lifetime markers, the O0 pipeline string and the hot-loop vectorization results.
   - The Win32 facts were checked as well: a handle stays associated with one completion port until it closes, the skip-on-success notification mode, and the `XP1_IFS_HANDLES` condition for sockets.
