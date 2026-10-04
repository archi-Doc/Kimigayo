# Specification change proposal: asynchronous tasks

Date: 2026-10-04

Status: Final proposal. Adoption is undecided; nothing is integrated into the specification or implemented. Supersedes `2026-10-03 Async Tasks.md` and `2026-10-03 Async Tasks2.md`. Stage 3 depends on `2026-10-04 Callable Effect Bounds.md`.

References: `SPEC §n` is the language specification, `impl §n` the implementation specification, and a bare `§n` this proposal.

## 1. Summary

Kimigayo gains asynchronous tasks on one thread. Threads follow later as structured offload.

- **Suspension is an input.** A function may suspend only if it has a *task parameter* `task: Async.Task`. `Async.Task` names a parameter category, not a value Type, so the authority to suspend can never be captured, stored or returned.
- **No pending-computation values.** There are no futures or task handles, and no `Pin`.
- **Structured only.** Children are started only from Callable arguments of Kimi operations, which return only after their children complete, so children may borrow the caller's locals.
- **Cancellation is a request; observing it yields a value.** Kimi operations request cancellation of the children they started, as task state that no value represents. A wait that observes a request reports it in its `Result`, unless its operation completed first. Nothing is thrown or unwound, and cleanup never suspends or observes cancellation.
- **Two language rules.** Rule 1 defines task parameters and rule 2 defines task calls. Everything else is Kimi obligations and library contracts; the compiler knows three task intrinsics, and the executor is written in Kimigayo.

```kimi
func fetchBoth(task: Async.Task, a: ref/Url, b: ref/Url) -> Result<(Page, Page), Net.Error>
    let getA = func [a] (task: Async.Task) -> Result<Page, Net.Error> => fetchPage(task, a)
    let getB = func [b] (task: Async.Task) -> Result<Page, Net.Error> => fetchPage(task, b)
    return Async.joinOk(task, getA, getB)    // Task call: the children run concurrently.
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
2. **The authority is not a value.** It cannot escape, so no escape rules are needed.
3. **Task frames are not values.** They are never moved, copied or stored.
4. **Structure from call duration.** Children complete before the operation that started them returns. *Safe-code soundness never depends on a destructor running.*
5. **Cancellation adds no completion kind.** A request is task state, and a wait observes it by returning an `Err`, so SPEC §16.2.3 and Chapter 17 gain no abrupt completion.
6. **Implicit code and constructors never suspend.** Accessors, `drop`, defaults, static initializers and Deferred Blocks run implicitly and cannot suspend; constructors and `main` take no task either (§8, SPEC §22.2.2).
7. **Kimi in Kimigayo.** The compiler knows only a minimal task boundary (§5); Kimi writes the rest.
8. **One thread first.** Threads come later as structured offload (§12).

## 4. Language rules (new SPEC Chapter 24)

New Chapter 24, "Suspension and asynchronous tasks", is the only normative statement of rules 1 and 2. Each section they affect (§13) gains a one-sentence reference to it. The chapter defines these terms, which Appendix E also lists:

- **Task.** The execution of a root that `Async.run` starts, or of a child that a task starts through `TaskBoundary.enter`. A root begins a new *task tree*; a child belongs to the tree of the task that started it. A task and its descendants form its *subtree*.
- **Task frame.** The state of one call of a function that has a task parameter, from entry to completion, including while it is suspended. A task consists of the task frames of the calls that pass its task on. The term is distinct from the call activation of SPEC §15.6.7.
- **Task call.** Defined by rule 2. It is the only place where a task frame can suspend, so a task call is a *suspension point*.

### 4.1. Rule 1: task parameters

1. **Category.** A parameter is a *task parameter* when its Type is written only as a name, qualified or not, without Semantics, Origin, `?` or grouping, and that name resolves by declaration identity to `Kimi.Async.Task`. A task parameter is a parameter category, not a Semantics or a value Type, as a Place result is a result category (SPEC §7.1.1).
   - It is allowed in named functions, anonymous functions, Function Types, Callable signatures and Contract requirements.
   - It is not allowed in constructors (`init`) or as `self`. Accessors, `drop` and `main` cannot declare one, because their shapes are fixed (SPEC §11.2, §16.3.1, §22.2.2).
   - A parameter list has at most one, and it has no default.
   - An anonymous function whose parameter Type is omitted takes the category from its fixed expected signature (SPEC §7.6.1).
2. **Name.** The parameter's name denotes no value. It may appear only as the argument for a task parameter of a call; a task argument matches only a task parameter (SPEC §10.1). It may not appear:
   - in a default expression;
   - in the body of a Deferred Block that its function registers. This does not restrict a function expression inside the block: it has its own Function Boundary (SPEC §16.1.1), and its body may use its own task parameter.
3. **Compatibility.** Signatures (SPEC §9.1), implementation identification (SPEC §8.4.5) and callable signature compatibility (SPEC §10.7) require the same category at each parameter position: a task position matches only a task position.
4. **Acquisition shape.** A task parameter has no acquisition mode. Its matching key `Task` overlaps only `Task` (SPEC §7.3.1); the key Any, which overlaps every other key, does not overlap `Task`, because no Type argument can bind the category.
5. **Declaration.** SPEC §22.1 lists `Kimi.Async.Task` as a compiler-identified name, like the rows for `Copy` and `Callable`; it declares no Type, value or constructor. In SPEC §9.2, the Core role accepts the name only as the whole Type of a parameter; the name denotes no Core. Any other use is `TaskParameterPosition_Kd`.

### 4.2. Rule 2: task calls

> A *task call* is a call whose selected declaration, Function Type or Callable signature has a task parameter. While a task executes a call that is not a task call, no other task of its tree that started before that call runs. A tree started by `Async.run`, or a child started by `TaskBoundary.enter`, during a call runs inside that call as part of the callee, and its effects are compared as effects of that call. In the calling body, a task call is compared as a call with unknown environment effects, whatever its published summary or available effect bounds (SPEC §8.4.10.4), against every potentially affected active static Loan, including those of its own arguments. This comparison enters no effect summary or published record (SPEC §18.7.2); the callee's own effects are summarized as usual.

- **Why calls that are not task calls.** Only their extent is fixed: their callee has no task to suspend with (rule 1), so such a call is in progress exactly while its callee runs. A task of an enclosing tree is excluded too, because it is itself executing the call that reached `Async.run`.
- **Why "whatever its published summary or available effect bounds".** Without the clause, rule 2 of SPEC §8.4.10.4 would give a `confined` requirement call no environment effects. A Loan on a mutable static Field could then be held across a suspension while another task replaces the Field: a use after free.

```kimi
group Settings
    public var greeting: string = "hello"

contract Source
    func take(self: uniq/Self, task: Async.Task) -> u32
        effect confined

func show<S>(task: Async.Task, source: uniq/S) -> ()
    S is Source
    let text = Settings.greeting@ref
    _ = source.take(task)       // Error: a task call; `confined` does not exclude other tasks.
    use(text)
```

### 4.3. Consequences

Each fact follows from rules 1 and 2 and existing rules. The only wording change they need is the clarification of SPEC §15.4.3 rule 3 in the second row.

| Fact | Follows from |
| --- | --- |
| A task parameter cannot be captured, stored in a local, Field or element, returned, or bound as a Type argument, including inferred and associated-Type bindings | It is not a value: captures bind values (SPEC §7.6.2), and Type arguments must be complete value Types (SPEC §8.1.3) |
| It is not an elision input and has no per-call Origin | SPEC §15.4.3 rule 1 takes direct borrowed inputs, and SPEC §8.6 quantifies only `ref/T` and `uniq/T` parameters. SPEC §15.4.3 rule 3 is clarified so that "every input Type" excludes task parameters |
| It cannot appear in a foreign function declaration and has no `noalias` | It is outside the C ABI table (SPEC §22.3.2) and is not a safe value-borrow argument (impl §21.5.5) |
| Accessors, `drop`, `main`, static initializers, constructors, defaults and Deferred Blocks never suspend | They cannot reach a task parameter (SPEC §11.2, §16.3.1, §22.2.2; rule 1) |
| Loans of locals may be held across a suspension | Call protection covers the whole call (SPEC §15.6.4); locals live until scope exit or Move (SPEC §5.2.1); there are no exceptions (SPEC §17.1); Abort does not unwind (SPEC §17.3.3) |
| Suspension points are locally visible | A task call is known from the selected declaration, as an unsafe call is (SPEC §7.5), and its task argument can only be the body's own task parameter |
| A synchronous implementation satisfies a requirement that takes a task | Compatibility compares the category at each position (rule 1.3); a body need not use its task parameter. The category is declared, never inferred, and Function Types that differ in it do not convert |

**Why not an Origin rule.** If the authority to suspend were a `uniq` borrow, a captured Reborrow would be lifetime-correct (SPEC §7.6.2, §15.6.3), and a synchronous higher-order function could call a Closure that suspends inside its own frame. The hazard is which frames are running, not how long the authority lives, so a category states it directly.

```kimi
func misuse(task: Async.Task, delay: Time.Duration) -> ()
    let saved = task                                                   // Error: task denotes no value.
    let later = func [task, delay] () => _ = Async.sleep(task, delay)  // Error: task cannot be captured.
    let maybe: Option<Async.Task> = .None                              // Error: not a value Type.

struct Job
    let task: Async.Task                        // Error: not a Field Type.
    init(task: Async.Task) => ()                // Error: a constructor takes no task.

contract Bad
    func current() -> Async.Task                // Error: not a result.
    func twice(first: Async.Task, second: Async.Task) -> ()   // Error: at most one task parameter.
    func peek(task: ref/Async.Task) -> ()       // Error: no Semantics, Origin or `?`.

contract Step
    associate Ctx

struct Sleeper
    Self is Step
    associate Step.Ctx is Async.Task            // Error: an associated Type needs a complete value Type.

func runLater<F>(body: F) -> ()
    F is Callable<owner, (i32) -> ()>
    body@move(0)

func caller(task: Async.Task, delay: Time.Duration) -> ()
    let wait = func [delay] (task: Async.Task) => _ = Async.sleep(task, delay)
    runLater(wait)                              // Error: position 0 differs in category.
```

## 5. Task boundary (new SPEC §22.1.x)

The task boundary is an internal Kimi group bound to declaration identity. No alias, re-export or same-spelled declaration grants its capability, as for the storage boundary (SPEC §22.1.2.5). The compiler knows three operations:

```kimi
internal group TaskBoundary
    // The only way to start a task. Returns true when body completed without suspending.
    internal unsafe func enter<F, R>(context: raw/TaskRecord, body: F, slot: raw/R) -> bool
        F is Callable<owner, (Async.Task) -> R>
    // The only suspension: records the current task frame in waiter and suspends it.
    internal unsafe func park(task: Async.Task, waiter: raw/Waiter) -> ()
    // The only transfer of control into another task frame (obligation 1).
    internal unsafe func resume(handle: raw/TaskFrame) -> ()
```

Everything else is ordinary Kimigayo over these operations and raw storage (SPEC Chapter 5): `run`, `join`, `race`, `each`, `pipe`, `shield`, the executor, timers, IOCP and channels.

- **Wrappers.** A child with extra parameters is wrapped in a Closure of the `(Async.Task) -> R` shape. For `each`, the wrapper is `func [child, item@move] (task: Async.Task) => child(task, item@move)`: the bare entry copies the `ref/F`, and moving the captured item out makes the body Consuming (SPEC §7.6.2, §7.6.3), which `enter`'s `Callable<owner, ...>` admits (SPEC §8.6).
- **Dependencies.** `enter` takes `body` by value and moves it into the child's task frame, so no Type records the child's dependencies. Internal record Types appear only in private locals and Fields. Only the argument Loans of the public operation that received the child (SPEC §15.6.4) and obligation 2 protect them.

Kimi obligations:

1. **Resumption.** `resume` takes only a suspended task frame whose wait has ended: its waiter was woken, or the task call at which it is suspended has completed. It resumes each suspension at most once, and runs only while every task of that frame's tree is suspended or complete. A task frame is never resumed after it completes and never destroyed before it completes.
2. **Child completion.** A child started by `enter` completes, and its result is taken, before the task frame that executed `enter` completes, and therefore before the public operation returns. A helper that returns without joining its children must not call `enter`. Because `enter`'s `bool` result keeps none of `body`'s dependencies, the checker ends `body`'s Loans when `enter` returns. Until the child completes, the task frame that executed `enter` treats them as its own live Loans: it performs no operation that would conflict with them if `body` were still one of its locals (SPEC §15.6.2), including destroying, replacing or moving a value that they borrow.
3. **Wait registrations.** Every wait registration (ready link, timer node, `OVERLAPPED`, channel waiter, join count) lives in the waiting task frame or its task record and is removed before the wait returns. An I/O wait, even a cancelled one, returns only after its completion packet is dequeued, so its registration stays until then.
4. **Shared handles.** A handle whose state another task may change (`Async.Sender`, `Async.Receiver`) never has its inline storage written while it is borrowed. Its mutable state lives in Kimi-internal raw storage behind a loaded pointer, and waiting intrinsics are compiler barriers for that state.
5. **Commit of observing waits.** A cancellation request applies to every cancellation-observing wait in the task's subtree, whether pending now or started later, except inside `Async.shield` (§6.3). Each such wait *commits* exactly once, either by its operation, at the commit point in the table below, or by cancellation, whichever comes first. It returns what it committed to. Cancellation is reported in the wait's `Result`: as `Err(Async.Cancelled)`, as `Err((value, .Cancelled(reason)))` for `send`, or as the `Cancelled` Case of a domain error (§6.3).
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
        public func send(self: uniq/Self, task: Async.Task, value: T) -> Result<(), (T, SendFailure)>

    public struct Receiver<T>
        public func receive(self: uniq/Self, task: Async.Task) -> Result<Option<T>, Cancelled>

    public func run<F, R>(root: F) -> R
        F is Callable<owner, (Async.Task) -> R>

    public func sleep(task: Async.Task, duration: Time.Duration) -> Result<(), Cancelled>
    public func checkpoint(task: Async.Task) -> Result<(), Cancelled>

    public func shield<F, R>(task: Async.Task, body: F ! grace: Time.Duration) -> R
        F is Callable<owner, (Async.Task) -> R>

    public func join<A, B, RA, RB>(task: Async.Task, first: A, second: B) -> (RA, RB)
        A is Callable<owner, (Async.Task) -> RA>
        B is Callable<owner, (Async.Task) -> RB>

    public func joinOk<A, B, TA, TB, E>(task: Async.Task, first: A, second: B) -> Result<(TA, TB), E>
        A is Callable<owner, (Async.Task) -> Result<TA, E>>
        B is Callable<owner, (Async.Task) -> Result<TB, E>>

    public func race<A, B, RA, RB>(task: Async.Task, first: A, second: B) -> Raced<RA, RB>
        A is Callable<owner, (Async.Task) -> RA>
        B is Callable<owner, (Async.Task) -> RB>

    public func each<I, F>(task: Async.Task, items: I, child: ref/F ! limit: isize)
        -> Result<(), Cancelled>
        I is Iterator
        F is Callable<(Async.Task, I.Item) -> ()>

    public func eachReceived<T, F>(task: Async.Task, items: uniq/Receiver<T>, child: ref/F ! limit: isize)
        -> Result<(), Cancelled>
        F is Callable<(Async.Task, T) -> ()>

    public func pipe<P, C, T, R>(task: Async.Task, producer: P, consumer: C ! capacity: isize) -> R
        P is Callable<owner, (Async.Task, uniq/Sender<T>) -> ()>
        C is Callable<owner, (Async.Task, uniq/Receiver<T>) -> R>
```

Conventions:

- **Arity.** `join`, `joinOk` and `race` have one overload per arity; there are no variadic Type arguments (SPEC §8.1).
- **Children.**
  - A *one-shot child* (of `join`, `joinOk`, `race`, `pipe` and `shield`) is consumed by its call, so it is received as `Callable<owner, ...>`, the "Consume the callback on invocation" row of STYLE §4.3. It may have a Shared, Exclusive or Consuming body (SPEC §8.6). A Non-Copy child held in a `let` is passed with `@move`; a Closure whose captures are all Copy, such as `getA` in §1, is copied.
  - `each` and `eachReceived` call their child concurrently, so they borrow it shared (`ref/F`, the Shared row of STYLE §4.3), which admits only a Shared body.
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
func fillBoth(task: Async.Task, left: uniq/Buffer, right: uniq/Buffer) -> ()
    let fillLeft = func [left] (task: Async.Task) => fill(task, left)       // Exclusive body.
    let fillRight = func [right] (task: Async.Task) => fill(task, right)
    _ = Async.join(task, fillLeft@move, fillRight@move)

func replyBoth(task: Async.Task, first: Net.Connection, second: Net.Connection) -> ()
    let a = func [first@move] (task: Async.Task) => respond(task, first@move)   // Consuming body.
    let b = func [second@move] (task: Async.Task) => respond(task, second@move)
    _ = Async.join(task, a@move, b@move)

func normalizeAll(task: Async.Task, rows: uniq/Array<Row>, limits: ref/Limits) -> Result<(), Async.Cancelled>
    let fix = func [limits] (task: Async.Task, row: uniq/Row) => normalize(task, row, limits)
    return Async.each(task, rows.iterateUniq(), fix@ref, limit: 16)   // Disjoint uniq/Row items.
```

A user function that passes its task on is part of the same task, not a task boundary:

```kimi
func retryOnce<F, R>(task: Async.Task, body: ref/F) -> Result<R, Net.Error>
    F is Callable<(Async.Task) -> Result<R, Net.Error>>
    match body(task)
        .Ok(let value) => return .Ok(value@move)
        .Err(let error) => _ = error@move
    return body(task)
```

### 6.3. Cancellation

- **Sources.** Only Kimi operations request cancellation, for the children they started, under their contracts (§6.2). A request is task state; no value represents it, and no public operation makes one (§15.2).
- **Observation.** Only waits for events other than the completion of the caller's own children observe a request (obligation 5): `sleep`, `checkpoint`, I/O, `send`, `receive`, the receiving phase of `eachReceived`, and `each` before each pull.
- **One Type.** A wait whose only failure is cancellation returns `Result<T, Async.Cancelled>`. A domain error enum that can report cancellation has exactly one cancellation Case, `Cancelled(Async.Cancelled)` (a STYLE §5.1 addition).
- **Conversion.** `try` combines no error Types (SPEC §17.2.4), so converting `Async.Cancelled` into a domain error is explicit and has the same form everywhere: `.Err(let reason) => return .Err(.Cancelled(reason))`.
- **`checkpoint`.** Reports a pending request; it yields only when another task is ready or a time slice has elapsed.
- **`sleep`.** `sleep(task, d)` returns `Ok(())` no earlier than `d` after the call, as measured by the elapsed-time counter of SPEC §22.7, and registering the wait allocates nothing.
- **Effects of waits.** `sleep`, `checkpoint` and the endpoint operations reach executor state only through the task, which is an input. They are therefore no environment effects and are usable in `confined` implementations; that state matches no user Loan. I/O waits call foreign functions and are environment effects (SPEC §8.4.10.2).
- **Shielding.** `Async.shield(task, body, grace: g)` runs `body` as a child of the calling task while the other tasks keep running, and returns the body's result after the child completes.
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

func fetchAfter(task: Async.Task, url: ref/Url, delay: Time.Duration) -> Result<Page, Net.Error>
    match Async.sleep(task, delay)
        .Ok(_) => ()
        .Err(let reason) => return .Err(.Cancelled(reason))
    return fetchPage(task, url)
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
func sumEvens(task: Async.Task, limit: u64) -> Result<u64, Async.Cancelled>
    let produce = func [limit] (task: Async.Task, out: uniq/Async.Sender<u64>) -> ()
        var n: u64 = 0
        while n < limit
            match out.send(task, n)
                .Ok(_) => ()
                .Err(_) => exit                 // The consumer finished, or cancellation.
            n += 2
    let consume = func [] (task: Async.Task, inbox: uniq/Async.Receiver<u64>) -> Result<u64, Async.Cancelled>
        var total: u64 = 0
        loop
            match try inbox.receive(task)
                .Some(let value) => total += value
                .None => return .Ok(total)
    return Async.pipe(task, produce, consume, capacity: 0)
```

**Dynamic work** uses `eachReceived`; there are no detached tasks.
- Jobs of different kinds are sent as erased Function values of Type `(Async.Task) -> ()`, under the erasure conditions of SPEC §7.6.4: a Shared minimum receiver and an Owned environment.
- A job that consumes its captures cannot be erased. Send its state as the channel item instead, and keep the code in the `eachReceived` child, as `serve` does with `Net.Connection`.

```kimi
func serve(task: Async.Task, listener: uniq/Net.Listener) -> Result<(), Async.Cancelled>
    let accept = func [listener] (task: Async.Task, out: uniq/Async.Sender<Net.Connection>)
        => acceptLoop(task, listener, out)
    let handleAll = func [] (task: Async.Task, inbox: uniq/Async.Receiver<Net.Connection>)
        -> Result<(), Async.Cancelled>
        let handle = func [] (task: Async.Task, conn: Net.Connection) => respond(task, conn@move)
        return Async.eachReceived(task, inbox, handle@ref, limit: 64)
    return Async.pipe(task, accept@move, handleAll, capacity: 16)

func runJobs(task: Async.Task, jobs: uniq/Async.Receiver<(Async.Task) -> ()>) -> Result<(), Async.Cancelled>
    let runOne = func [] (task: Async.Task, job: (Async.Task) -> ()) => job(task)
    return Async.eachReceived(task, jobs, runOne@ref, limit: 8)
```

### 6.5. Asynchronous sequences

There is one form: repeat a task call in a `loop` with `match`. It reads both kinds of sequence:

- **Pull.** A task-taking call such as `reader.readLine(task)`, which returns without suspending when data is buffered.
- **Cross-task.** `Async.Receiver<T>` (§6.4).

`for` is unchanged (SPEC §14.6.2): `LendingIterator.next(self: uniq/Self during step)` has no task parameter, and an iterator cannot hold a task. No `AsyncIterator` protocol is added.

```kimi
func countLines(task: Async.Task, reader: uniq/Io.LineReader) -> Result<u64, Io.Error>
    var count: u64 = 0
    loop
        match try reader.readLine(task)
            .Some(_) => count += 1
            .None => return .Ok(count)
```

### 6.6. Which operations take a task

- Every Kimi operation that may wait (for I/O, a timer, a channel or its own children) takes a task and has no task-free or `Async`-suffixed counterpart (Principle 1). Console output is the one exception, below.
- Console output (`Console.writeLine`, SPEC §22.4) stays synchronous in this proposal and moves to the task form in a separate later change (§14). Until then it blocks the thread, and its implementation must not run other tasks (obligation 1). That change must also decide how code without a task writes output: `drop`, Deferred Blocks, accessors, constructors, and `main` outside `Async.run`.

## 7. Ownership and lifetime

### 7.1. Loans across a suspension

- Loans of locals and parameters may be held across a suspension (§4.3).
- Other tasks can reach a task's state only through:
  - Loans given to its children, checked as argument dependencies (SPEC §8.6);
  - mutable statics and external state, covered by rule 2;
  - `rc` and `arc` payloads, which allow shared access only (SPEC §13.5.5.2);
  - Kimi-internal raw storage, which publishes no Place.
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
- **Waiting finalization** (flush, graceful close) is a named operation that takes `self: uniq/Self` and a task; each path handles its `Result`. While a request applies, such a wait reports cancellation unless its operation completes first (obligation 5). To finalize even then, call it through `Async.shield`: the request reaches it only after the grace, and the other tasks keep running. A Deferred Block cannot call `shield` (rule 1.2), so finalization stays on explicit paths.
- **Constructors take no task.** Asynchronous construction uses a factory, as SPEC §6.2.3.5 already prescribes for fallible construction.

```kimi
public struct Connection
    let socket: Net.Socket

    private init(socket: Net.Socket)
        self.socket = socket@move

    public func open(task: Async.Task, url: ref/Url) -> Result<Connection, Net.Error>
        let socket = try Net.Socket.connect(task, url)    // Cancellation returns Err(.Cancelled(_)).
        return .Ok(Connection.init(socket@move))

func appendLine(task: Async.Task, path: ref/string, line: ref/string) -> Result<(), Io.Error>
    var file = try Io.File.open(task, path)
    // defer => _ = file.flush(task)                      // Error: a Deferred Block cannot use task.
    try file.write(task, line)
    return file.flush(task)                               // Finalize on the normal path.

func appendLineAlways(task: Async.Task, path: ref/string, line: ref/string) -> Result<(), Io.Error>
    var file = try Io.File.open(task, path)
    let written = file.write(task, line)                  // No try: flush after every outcome.
    let finish = func [file@uniq] (task: Async.Task) -> Result<(), Io.Error> => file.flush(task)
    let grace = Time.Duration.init(microseconds: 2000000)
    let flushed = Async.shield(task, finish@move, grace: grace)   // Up to 2 s more under cancellation.
    try written@move
    return flushed@move
```

## 9. Runtime (implementation policy)

This section is policy for impl §21 and `src/Kimi/Library/README.md`. Only the definition of `run` (§9.1) and the contracts stated in §5 and §6 are normative (new SPEC §22.1.x).

### 9.1. `run` and the executor

> `Async.run(root)` starts `root` as the root of a new task tree, all of whose tasks run on the current thread, and returns the root's result when every task of the tree has completed.

```kimi
func loadConfig(path: ref/string) -> Result<Config, Io.Error>      // A synchronous function.
    return Async.run(func [path] (task: Async.Task) => readConfig(task, path))
```

- **Consequences.**
  - `run` is the only way for code without a task to call task-taking operations. Inside a task it follows the same definition; a nested `run` needs no special rule.
  - No task of an enclosing tree runs inside `run`: the enclosing task is executing the call that reached `run` (rule 2). So no other task sees a static slot in the Initializing state (SPEC §22.2.3). Reentry within the tree is the ordinary same-thread cycle Abort.
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
  - The child of a `shield` has a shield bit on its task record. The walk stops there. The `shield` task frame arms a grace timer node (obligation 3), which continues the walk into the child when it expires and is unlinked if the child completes first. A shielded child does not inherit a pending request when it starts; `shield` arms the timer at once instead.
- **Timers.**
  - The deadline is the QPC reading at the call (SPEC §22.7) plus `d`, rounded up to the next tick, so `sleep(task, d)` never completes before `d` has elapsed. Registration allocates nothing.
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

- **ABI.** Every function with a task parameter is entered as `entry(task, continuation, args..., resultSlot) -> i1`, where true means completed. The task is a compiler-chosen context pointer without `noalias` (impl §21.4.2).
- **Plain instances.** Plainness is computed bottom-up for each strongly connected component of the monomorphized instance graph.
  - An instance is plain when it contains no suspension intrinsic, no indirect task call and no task call to a non-plain instance.
  - A plain instance is an ordinary function that emits no `llvm.coro.*` and always reports completion.
  - LLVM already folds such instances at O2 (verified on LLVM 22.1.8). The gain is O0 code size and arena traffic, and modules without coroutines skip the extra `opt` step.
- **Coroutines.** Every other instance becomes a switched-resume coroutine. Its frame represents the task frame, lives in the task's arena and never moves while live; this is a lowering obligation. Borrow parameters of a non-plain entry get no `captures(none)`, because the task frame keeps them past the physical return.
- **O0.** `llc -O0` cannot lower `llvm.coro.*`. A module with coroutines first runs `opt -passes=coro-early,cgscc(coro-split,coro-annotation-elide),coro-cleanup`, written without spaces because `opt` rejects them. The toolchain manifest records this step.

### 10.2. Completion protocol

```llvm
define internal i1 @g(ptr %task, ptr %cont, ptr %slot) presplitcoroutine {
  ; coro.id, coro.alloc (an arena push) and coro.begin produce %hdl.
  %done = call i1 @wait_register(ptr %task, ptr %hdl)  ; true when no wait is needed
  br i1 %done, label %complete, label %wait
wait:
  %s = call i8 @llvm.coro.suspend(token none, i1 false)
  switch i8 %s, label %suspend [i8 0, label %complete
                                i8 1, label %never]
complete:
  store i32 42, ptr %slot                               ; the result goes to the caller's slot
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

1. **Result.** The result goes to the slot that the caller passes.
2. **No destroy path.** The destroy successor of every suspend is `unreachable`. There is no final suspend: completion runs ordinary cleanup and reaches the single `coro.end`. The destroy and cleanup clones become empty at O2; at O0 their code size is recorded as a measurement.
3. **Arena position.** The caller saves the arena position and restores it at completion, whether synchronous or resumed. A plain callee needs neither.
4. **Parent resumption.** A resumed task frame that completes stores its continuation in the task record's `next` word, and the dispatch loop resumes it in the same step, bypassing the ready list. The ready list receives only wake-ups from waits. `llvm.coro.is_in_ramp()` tells synchronous from resumed completion, so the frame needs no flag.

### 10.3. Embedded child frames

```llvm
define internal i1 @join(ptr %task, ptr %cont, ptr %ta, ptr %tb, ptr %slots) presplitcoroutine {
  ; The child records are locals of join's own task frame.
  %ra = call i1 @child(ptr %ta, ptr null, ptr %slots) coro_elide_safe
  %sb = getelementptr i8, ptr %slots, i64 4
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
  - `llvm.lifetime.start` is emitted at a slot's first initialization.
  - `llvm.lifetime.end` is emitted at the earliest proven point: scope exit, or after the last use when the slot is not address-observed and holds no destruction responsibility or live Loan (impl §21.5.5).
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

func sumAll(task: Async.Task, values: ref/Array<u64>) -> Result<u64, Async.Cancelled>
    var total: u64 = 0
    var start: isize = 0
    while start < values.length
        var end = start + 4096
        if end > values.length => end = values.length
        total += sumRange(values, start, end)      // Inner work without suspension.
        start = end
        try Async.checkpoint(task)                 // Suspends only between chunks.
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
| `ComparisonLoanConflict_Kd` (existing in the implementation), Reason `suspension point (other tasks)` | The task argument of the task call | `loan`, `static`, `suspension`, `use` | Advice only: copy the value before the call, or borrow again after it |
| `TaskParameterPosition_Kd` (new) | The offending name, Type, capture or binding | The task parameter, when the use names one | Advice: take a task parameter; the signature changes |
| Missing task argument (an ordinary argument error) | The call | The callee declaration | `Repair.PassTask` (new) under the conditions below. Otherwise Advice: in a Deferred Block or a default expression, move the call onto each path, through `Async.shield` when it must run under cancellation; elsewhere, add a task parameter, or use `Async.run` in code without one |
| A form ahead of its stage | — | — | An `Unsupported` code (SPEC §23.3.6.1) |

- **Code naming.** `ComparisonLoanConflict_Kd` exists in the implementation, but SPEC §15.6.4 names no code for its static call-effect comparison today. This change names it in SPEC §15.6.4 and §23 for that comparison, which now includes the task-call comparison of rule 2.
- **One record per Loan.** The task-call comparison is part of the static call-effect comparison, so it adds a Reason to that comparison, not a new requirement. When the same call's summarized or unknown environment effects also conflict with the Loan, one `ComparisonLoanConflict_Kd` record carries both Reasons (SPEC §23.3.6.4). The Reason states that available bounds (`confined`, `preserves results`) do not exclude the task-call comparison. `CallEffectConflict_Kd` keeps covering effects through inputs on Loans of earlier results (SPEC §8.4.10.6) and is unaffected.
- **`TaskParameterPosition_Kd`.** It is the only new rule-1 code and covers the position rules of rules 1.1, 1.2 and 1.5. The position (capture, local, Type argument, Deferred Block body, constructor, and so on) is a Reason value, and the message follows the form of `UnsafeFunctionValue_Kd`. A category mismatch under rule 1.3 is reported by the existing compatibility diagnostic, with the position and the category as Reason.
- **`Repair.PassTask`.** It inserts the task name at the candidate's task position (`UsageLegality`: required) only when every condition holds; otherwise it is Advice (SPEC §23.3.6.9):
  - the innermost function that directly contains the call, anonymous functions included, has a task parameter;
  - that parameter's name, inserted at the call, resolves to it: no inner block or Pattern binding shadows it (SPEC §9.2);
  - the call is neither in a Deferred Block that the function registers nor in a default expression;
  - exactly one candidate becomes applicable when a task argument is added.
- **Suspension listing.** The CSP lists each suspension point with facts local to the signature and body:
  - the task parameter;
  - the Loans and owned values live across the point;
  - whether the point may report cancellation: the selected callee's instantiated result Type reaches `Kimi.Async.Cancelled` (identified by declaration identity) through enum payloads, Tuple elements or `Option` and `Result` arguments. This is a may-fact, unknown for an abstract Type argument, and never derived from bodies (SPEC §18.7.2);
  - for calls that start children, the Loans that the children capture and their regions;
  - for `shield` calls, the grace.
- **Generation facts.** Whether an instance is plain is a fact of the monomorphized instance graph (§10.1). It is reported per instance, apart from the local facts.
- **Frame size.** Frame size is a measurement bound to the O level and toolchain identity, read from CoroSplit remarks (SPEC §23.5.3), not a check fact.
- **Hover.** Hover uses the typed inspection block of SPEC §8.4.10.6.
- **Checking cost.** For rule 2, each body precomputes a mask of its Loans anchored at mutable statics, and each task call ANDs it with the live-Loan set (guidance in `docs/dev`).

## 12. Threads (stage 3 direction)

Stage 3 adds threads only as structured offload of computation. It requires `2026-10-04 Callable Effect Bounds.md`.

```kimi
public group Async
    public func offload<F, R>(task: Async.Task, child: F) -> R
        F is Callable<owner, () -> R>
            effect confined
        F is Transferable

func checksumAll(task: Async.Task, blocks: ref/Array<Block>) -> u64
    let work = func [blocks] () -> u64 => checksum(blocks)   // Borrows the caller's data; takes no task.
    return Async.offload(task, work)                          // Task call: runs on a worker, then joins.

//  func [config@ref] () -> u64 => use(config)               // Error: the input reaches an rc handle.
//  func [] () -> u64 => Metrics.count                       // Error: confined excludes mutable statics.
```

1. **Operations.** `Async.offload(task, child)` and `Async.offloadEach(task, items, child: ref/F ! limit: isize)`.
   - Both are task calls. The child runs to completion on a worker thread and is joined before the operation returns.
   - `offloadEach` requires an `Iterator` and follows the `each` rules of §6.2 for `next` and the Iterator; it calls `next` on the parent's thread.
   - No task runs on a worker: the child's signature has no task parameter, and `confined` excludes `Async.run` (§9.1). Structured operations such as `join` never change threads.
2. **Borrowing.** The parent is suspended until the join, so the child may borrow the parent's locals as `ref` or `uniq`, as with Rust's `thread::scope`. A new obligation 6 provides this: a worker child completes and is joined before the `offload` task frame completes, and until then that task frame treats the child's Loans as live, as obligation 2 requires after `enter`. `offload` children do not pass through `enter`, so obligation 2 does not cover them. No Owned is required.
3. **Effects.** The child must be `confined`, which excludes authority obtained from the environment: direct access to mutable statics, foreign calls, Console output and raw pointers read from immutable statics. A static borrow that the child receives through a capture is authority from an input; rule 2 rejects it, because it is a static Loan of an argument of the task call. No per-thread rules for statics are needed.
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

| Location | Change |
| --- | --- |
| New Chapter 24, "Suspension and asynchronous tasks" | Rules 1 and 2, the terms of §4, and references to the facts of §4.3 |
| SPEC §7.2, §7.3.1, §7.6.1, §8.4.5, §9.1, §10.1, §10.7 | References to rule 1: the task-parameter category; a task position matches only a task position in Signatures, implementation identification and callable signature compatibility; the matching key `Task`, which Any does not overlap; a task argument matching only a task parameter; the category taken from an expected signature |
| SPEC §9.2 | The Core role accepts `Kimi.Async.Task` only as the whole Type of a parameter; no grammar change |
| SPEC §15.4.3 | In rule 3, "every input Type" excludes task parameters |
| SPEC §15.6.4 | A reference to rule 2; names `ComparisonLoanConflict_Kd` for the static call-effect comparison, including the task-call comparison |
| SPEC §22.2.3 | A reference to rule 2: while a task executes a call that is not a task call, no other task of its tree runs |
| SPEC §16.1, §16.2.3, §15.9 | A reference to rule 1.2 (a Deferred Block cannot use its function's task parameter); cleanup neither suspends nor observes cancellation; the cancellation boundary is resolved |
| SPEC §22.1 | The `Kimi.Async.Task` row; new SPEC §22.1.x with the task boundary (three intrinsics, obligations 1–5 with the commit points of obligation 5), the definition of `Async.run`, and the `Kimi.Async` declarations and contracts of §6, including `shield` and the `sleep` deadline |
| SPEC §22.4 | None now; the later Console change of §6.6 |
| SPEC §8.4.10.6, §23.3.6, §23.5.3 | The suspension Reason of `ComparisonLoanConflict_Kd`; `TaskParameterPosition_Kd`; `Repair.PassTask`; the suspension listing; frame size as a measurement; raw-anchor facts |
| SPEC Appendix D, E | SPEC §D.2: single-thread tasks are specified, threads remain deferred. New terms: task, task tree, task frame, task parameter, task call, suspension point |
| Unchanged | SPEC §8.1.3, §8.6 and §22.3.2 exclude the category without change; SPEC §2.5.1 and Appendix F (no keyword or grammar change); Chapter 17 |
| impl §21 | Task ABI, plain instances, coroutine lowering with frames that never move, the O0 step, the completion protocol, child-frame elision, lifetime markers, the inlining policy for hot loops and, once measured, automatic loop outlining (§10.4), and a context pointer without `noalias`. impl §21.4.5 is unchanged |
| impl §21 or `src/Kimi/Library/README.md` | Executor, timer, IOCP and arena policy (§9) |
| `docs/dev` | The rule-2 Loan mask (§11) |
| `docs/STYLE.md` | See below |
| `docs/LIBRARY.md` | The `Kimi.Async` declarations and their contracts |
| `docs/SETTLED.md` (on adoption) | An entry for the rejected no-leak guarantee, whose reason records the constraint "safe-code soundness never depends on a destructor running" (§7.3), and entries for the rejected alternatives of §15.2 |
| Stage 3 | `2026-10-04 Callable Effect Bounds.md`; the `Transferable` Constraint; the SPEC §8.4.10.2 row for `rc` read from immutable statics; cross-thread static initialization; obligation 6 for `offload` children |

`docs/STYLE.md` gains:

- **Task parameters.** Placed right after `self`, otherwise first, and named `task`. Child Closures also name theirs `task`, shadowing the parent's (SPEC §9.2).
- **APIs.** No task-free or `Async`-suffixed counterpart of a task-taking operation (§6.6). No `try` prefix on task-taking operations that return `Result` (§6.1). Named `limit`, `capacity` and `grace`.
- **Children.** Received by intent (STYLE §4.3): consumed when called once, shared when called concurrently.
- **Cancellation.** One `Cancelled` Case per domain error enum.
- **Finalization.** A waiting finalization is a named operation, called through `Async.shield` when it must run under cancellation. Asynchronous construction uses factories.
- **Bridging.** In a body with a task parameter, pass the task on; `Async.run` bridges only from code without one, because it suspends the enclosing tree.
- **Hot loops.** An inner loop without task calls and an outer loop that calls `checkpoint`, with the inner loop in a plain function until automatic outlining exists (§10.4).

## 14. Staged plan

- **Stage 0: specification.**
  - Write rules 1 and 2; implement the rule-1 checks with `TaskParameterPosition_Kd` and the rule-2 comparison. Execution reports `Unsupported`.
  - **Prerequisites** (incomplete in `docs/STATUS.md`): the SPEC §15.6.4 caller comparison of environment effects with static Loans, and mutable static Fields.
- **Stage 1: the smallest sound subset.**
  - Task ABI, coroutine lowering with the O0 step, and the three intrinsics.
  - `run`, `sleep`, `checkpoint`, `shield`, `join`, `joinOk`, `race` and `each`, with the per-thread executor state and timers.
  - Diagnostics and the CSP listing.
  - **Prerequisites** (incomplete in `docs/STATUS.md`):
    - Callable Constraints with owner-acquisition witnesses and arbitrary callable parameters and results;
    - Closures with aggregate captures;
    - borrowing entries and lending-loop dispatch.
  - Until these exist, stage 1 covers sequential task calls and `join` over Closures with scalar captures; other forms report `Unsupported`.
  - Tests include a generic `each` checked against a user `Iterator`.
- **Stage 2: channels and I/O.**
  - Channels with `uniq` endpoints, `pipe` and `eachReceived`.
  - Commit-point regressions in both directions and for both capacity kinds: a taken value or delivered item whose waiter is cancelled before it resumes; a pending `send` when `pipe` closes the receiver; a timer and a delivery in one executor round under `race`; waits started under a pending request; `shield` grace expiry.
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
2. **Category spelling.** `Kimi.Async.Task` is recognized by declaration identity; the grammar does not change (§4.1).
3. **Cleanup.** Deferred Blocks never suspend; finalization is written on explicit paths and runs through `Async.shield` under cancellation (§8).
4. **Constructors.** `init` takes no task; asynchronous construction uses a factory (§8).
5. **Fail-fast join.** Named `joinOk` (§6.2).
6. **`limit`.** Required, without a default (§6.1).
7. **Leaks.** No no-leak guarantee; SETTLED records the constraint of §7.3.
8. **Console.** Moves to the task form in a later change (§6.6).
9. **Stage-3 prerequisite.** Effect bounds on Callable Constraints are a separate proposal, completed before stage 3 (§12).
10. **Hot loops.** STYLE guidance plus compiler work: first the inlining policy, then automatic outlining (§10.4).
11. **Terms.** "Task frame" instead of "activation", which SPEC §15.6.7 already uses (§4).
12. **`each` and cancellation.** `each` stops pulling once a request applies and reports it (§6.2).

### 15.2. Rejected alternatives

| Alternative | Reason |
| --- | --- |
| `async`/`await` as a Function Type category with keywords | It needs two keywords and an implicit current task for cancellation and spawning (against the explicit context of Principle 2). A parameter category carries the authority itself |
| A contextual keyword for the category, like `place` | It needs SPEC §2.5.1 and Appendix F changes; declaration identity needs neither |
| The authority as an ordinary `uniq/Async.Task` Type | It needs a list of position bans and a separate fix for associated-Type bindings; the category excludes them through existing rules |
| Preventing escape with Origins | The hazard is which frames run, not lifetime (§4.3) |
| A positive "scheduling extent" rule for `resume` | Every point inside a task is within some `run` and some task call, so a containment rule excludes nothing; rule 2 and obligation 1 state the guarantee negatively |
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
| Observing through `ref/Async.Task` and suspending through `uniq` | Incompatible with the category, and on one thread a request is issued only while the observer is suspended |
| A public `cancelSiblings` | Ambient authority: a deep callee could cancel its siblings (Principle 2) |
| Deferred Blocks that suspend | Resume states in the cleanup of every exit path; §8 meets SPEC §16.2.3 trivially instead |
| Deferred Blocks that ignore cancellation | Every exit path would carry an implicit, unbounded mask, so outer timeouts and `joinOk` failure propagation would stop working. `Async.shield` masks one explicit call for an explicit grace |
| Finalizing in a nested `Async.run` | Rule 2 suspends the enclosing tree for its duration: outer timeouts cannot fire, every other task stops, and an inner wait on an outer task's endpoint never progresses |
| An unbounded `shield`, or a default grace | A deep callee could hold an ancestor's timeout or `joinOk` failure indefinitely; a default would hide the delay (Principle 3), as for `limit` |
| The name `protect` | `protected` is an access keyword (SPEC §2.5.1), and the name does not say what is withheld |
| Restricting `Async.run` to the program entry | A dynamic check is the rejected Abort on a nested `run`, and lazy static initialization would make it depend on which read comes first. A static ban on direct calls in bodies with a task is bypassed by any synchronous helper |
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
| Marker | `async`/`await` | `async`/`.await` | `async`/`await` | None | `suspend`; unmarked calls | The task argument (a parameter category) |
| Structure | By convention | Only `thread::scope` is sound | `TaskGroup`, `async let`, plus detached tasks | `errgroup` by convention | `CoroutineScope`, plus `GlobalScope` | Structured only |
| Cancellation | Token and exception | Dropping the future | Cooperative flag and error | `context.Context` | `CancellationException` | A request as task state; a `Result` value at each observing wait |
| Generators | Separate `yield return` | Separate, unstable `gen` | `AsyncStream` | Push iterators | `sequence {}` on the same mechanism | `pipe` on the same mechanism, with no value |

Taken: static frame sizes and precise borrow checking (Rust); fixed frames in a per-task arena and cooperative cancellation (Swift); the job tree and fail-fast join (Kotlin); channels that hand over ownership (Go); second-class capabilities (Effekt); bounded shielded cancel scopes (Trio); and cancellation that travels with an explicit argument (C#'s `CancellationToken`, Go's `context.Context`), here the task parameter, observed as a `Result` value instead of an exception.

### 15.4. Kimigayo Principles

| Principle | How this proposal meets it |
| --- | --- |
| 1. One Concept, One Canonical Form | One suspension marker (the task argument), one way to start children, one cancellation Type, one way to finalize under cancellation (`shield`), one sequence form, and no synchronous/asynchronous API pairs |
| 2. Local Reasoning | Task calls are known from the selected declaration; no other task runs during any other call; there is no inference and no implicit current task; child lifetimes follow call duration; implicit code never suspends |
| 3. Explicit Semantics | Suspension authority and child consumption appear in signatures; a cancellation request is task state set only by Kimi operations, and its observation is an `Err` in the wait's result Type; every resource bound (`limit`, `capacity`, `grace`) is written at the call; hidden allocation is limited to arena pushes, with bounds fixed by regressions |
| 4. Compiler Server Protocol | Listings of suspension points and task boundaries, existing codes with Reason values, repairs whose conditions are verified from facts, and measurements bound to configuration |

### 15.5. Verification record

1. **Design.** Five independent designs (Rust/C#, Swift, Go, Kotlin, effects) were scored and merged, then attacked from three lenses: ownership soundness, consistency with the specification and Principles, and rule minimality.
2. **Improvements.** Four lenses (rule reduction, consistency, performance, applicability) proposed improvements; each was adversarially verified and cross-checked for conflicts.
3. **Drafts.** Reviews from several lenses (syntax and citations, consistency and soundness, completeness, LLVM and Win32 facts, English) checked the drafts, and outside suggestions on the scheduling invariant, commit points, shielding, wording, `each` lifetimes, threads and `race` were each evaluated against the specification.
   - The LLVM claims were tested on the pinned LLVM 22.1.8: `coro.is_in_ramp`, `coro_elide_safe` elision and its hazards, the folding of coroutines without suspension, empty destroy and cleanup clones at O2, lifetime markers, the O0 pipeline string and the hot-loop vectorization results.
   - The Win32 facts were checked as well: a handle stays associated with one completion port until it closes, the skip-on-success notification mode, and the `XP1_IFS_HANDLES` condition for sockets.
