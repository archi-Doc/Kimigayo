# 24. Suspension and asynchronous tasks

Virtual and override task headers preserve the original task slot and public contract ([virtual dispatch](virtual-dispatch.md#1-declarations-and-contracts)). A slot effect bound never removes the static Loan checks required for task execution.

[Specification index](../SPEC.md)

This chapter defines the two language rules of asynchronous tasks: [task slots](#242-task-slots) state which functions may suspend, and [task calls](#243-task-calls) state where suspension happens and what other tasks may run meanwhile. Tasks share one thread. The task boundary, the `Kimi.Async` operations, cancellation and the definition of `Async.run` belong to [§22.1.3](22-core-execution-and-foreign-functions.md#2213-asynchronous-tasks); everything else follows from these two rules and the existing ownership, effect and cleanup rules. Threads remain deferred (Appendix D.2).

```kimi
func fetchBoth(task; a: ref/Url, b: ref/Url) -> Result<(Page, Page), Net.Error>
    let getA = func [a] (task;) -> Result<Page, Net.Error> => fetchPage(task; a)
    let getB = func [b] (task;) -> Result<Page, Net.Error> => fetchPage(task; b)
    return Async.joinOk(task; getA, getB)    // Task call: the children run concurrently.
```

Structured workers (§22.1.3.7) run ordinary computations on other threads; a task tree itself stays on its run thread. Implementation of this chapter and workers is deferred (Appendix D.5.2).

## 24.1. Tasks and task frames

- **Task.** The execution of a root that `Async.run` starts, or of a child that a task starts through the task boundary's `enter` (§22.1.3.1). A root begins a new **task tree**; a child belongs to the tree of the task that started it. A task and its descendants form its **subtree**.
- **Task frame.** The state of one call of a function that has a task slot, from entry to completion, including while it is suspended. A task consists of the task frame of the body that `Async.run` or `enter` starts for it and the task frames of the task calls made in its task frames. Suspending the innermost task frame of a task suspends all of that task's task frames, because each outer one is executing the task call that it made, which contains the next inner one. A task frame is not a value: it is never moved, copied or stored. The term is distinct from the call activation of §15.6.7.
- **Task call.** A call defined by [§24.3](#243-task-calls). It is the only place where a task frame can suspend, so a task call is a **suspension point**.

## 24.2. Task slots

The authority to suspend is an input of a function, not a value. A function may suspend only when its parameter list begins with a **task slot**, written `task;`. The task slot is neither a Type nor a value, so the authority can never be captured, stored, returned or bound as a Type argument.

### 24.2.1. Form

- **Lists.** Exactly these lists admit a task slot: the parameter list of a named function, Contract function requirement or explicit specialization, the parameter list of an anonymous function, and the Function Parameter List of a Function Type or Callable signature (§3.2, §8.6). A call's argument list, including that of a construction and a base initializer, admits the **task argument** `task;` in the same place.
- **Keyword.** `task` is a contextual keyword only as the first token after the `(` of such a list or of a constructor's parameter list, when the next token is `;` (§2.5.1). Elsewhere it is an ordinary Name. In Type position, a parenthesized list that begins with `task;` is always a Function Parameter List and must be followed by `->` (§3.2.3).
- **Spelling.** The `;` is always written, also when no other parameter or argument follows: `(task;)`. Without it, `(task)` would read as an ordinary parameter or argument named `task`, or as a grouped Type named `task`. No comma follows the `;`. Whitespace around it has no meaning; canonical formatting writes none before it and one space after it when an item follows on the same line.
- **Position.** The task slot precedes the receiver and both sections of the `!` boundary, and belongs to neither section (§7.2.1). A list therefore has at most one.
- **Where not.** Constructors, foreign function declarations, an Application's `main` and test functions have no task slot, because no caller passes them a task (§6.2.3, §22.3.1, §22.2.2, §6.5.1). Accessors and `drop` have no list that admits one (§11.2, §16.3.1).
- **Anonymous functions.** An anonymous function has a task slot exactly when its header writes one. A fixed expected call signature never supplies one; it still supplies omitted parameter Types, aligned with its parameters after the task slot (§7.6.1, §10.5).
- **Specializations.** An explicit specialization writes a task slot exactly when its original has one (§8.8).

```kimi
contract Reader
    func read(task; self: uniq/Self, buffer: uniq/Array<u8>) -> Result<isize, Io.Error>

func pause(task; ! delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)

func runAll<F>(task; jobs: ref/Array<F>) -> ()
    F is Callable<(task;) -> ()>
    for job in jobs
        job(task;)
```

### 24.2.2. Passing

The task slot declares no binding and has no Type. A call passes the task of the innermost function that contains it by writing `task;` first in its argument list, as in `foo(task; x)` or `bar(task;)`. A call writes the task argument exactly when its callee, the selected declaration, Function Type or Callable signature, has a task slot. The task argument may be written only when that innermost function, anonymous functions included, has a task slot, and not directly:

- in a default expression;
- in the body of a Deferred Block that the function registers.

Neither restricts a function expression inside them: it has its own Function Boundary (§7.6.1, §16.1.1), and its body may pass its own task slot.

### 24.2.3. Not a parameter, and one shape per group

A task slot is no parameter (§7.2.2). It has no position number, external name, acquisition mode or matching key, binds no argument, and corresponds to no parameter (§7.3.1). The receiver and the ordinary parameters keep their numbering in the bound and unbound call sequences and their counts `N` and `K` (§7.2.2): in `func configure(task; ! count: i32)`, `count` is ordinary parameter 0 and `K = 0`.

In one function group (§7.3, §9.5), either every declaration has a task slot or none does. As for parameter acquisition shapes (§7.3.1), a group formed by the same-name declarations of one declaration scope (a Container and its fragments, the project root, a local scope, the members of a Type, the requirements of a Contract, including those inherited through refinement) is checked at the declarations, and a group gathered at a use (§9.4.1, §9.5) is checked at that use. A task slot therefore never distinguishes overloads, and whether a call needs the task argument follows from the Name alone.

### 24.2.4. Compatibility

Implementation identification (§8.4.5), candidate equivalence (§8.4.6), specialization target identification (§8.8.1), callable signature compatibility and common Function Type conversion (§10.7), known- and fixed-signature matching (§10.5, §10.8) and Function Type identity (§3.2.1) require both sides to have a task slot or neither. The remaining parameters, or a Function Type's parameter Types, are compared position by position as before. A body need not pass its task slot on, so an implementation that never suspends satisfies a requirement with a task slot by declaring the task slot.

## 24.3. Task calls

A **task call** is a call whose argument list begins with the task argument `task;`.

1. **Scheduling.** While a task executes a call that is not a task call, no other task of its tree that started before that call runs.
2. **Nested trees and children.** A tree started by `Async.run` during a call runs inside that call as part of the callee, and its effects are compared as effects of that call. A child started by `enter` runs inside the task call whose task frame executed `enter` (§22.1.3.1, obligation 2), and its effects are compared as effects of that task call.
3. **Static Loans.** In the calling body, a task call is compared as a call with unknown environment effects, whatever its published summary or available effect bounds (§8.4.10.4), against every potentially affected active Loan anchored in static storage, a **static Loan**: a Loan whose Place has a static-key root (§15.6, §22.2.4), as §15.6.4 compares unknown effects. These include the static Loans that the call's own inputs hold or depend on: its callee value, its receiver and its arguments. This comparison enters no effect summary or published record (§18.7.2); the callee's own effects are summarized as usual.

The rules have these grounds:

- **Calls that are not task calls.** Only their extent is fixed: their callee has no task to suspend with (§24.2), so such a call is in progress exactly while its callee runs. A task of an enclosing tree is excluded too, because it is itself executing the call that reached `Async.run`.
- **Whatever the summary or bounds.** Without this clause, rule 2 of §8.4.10.4 would give a `confined` requirement call no environment effects. A Loan on a mutable static Field could then be held across a suspension while another task replaces the Field.
- **Static Loans only.** Another task reaches a task's state only through Loans given to its children, which are argument dependencies of the call that starts them (§8.6, §15.6.4); through static storage, whose Loans every task call compares; through `rc` and `arc` payloads, which grant shared access only (§13.5.5.2); and through Kimi-internal raw storage under the obligations of §22.1.3.1. Loans on locals and parameters are therefore not compared, and they may be held across a suspension. A parameter that borrows static storage was compared at the caller: at the task call that passed it, or at the `Async.run` call whose root captured it. A shared Loan of a static `let` slot is never affected, because its value cannot change after initialization (§22.2.3).

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

Because tasks interleave only at task calls and reach each other's state only through the routes above, variance (§15.3.5), handle destruction (§16.3.3) and receiver preservation (§12.4.4.2) hold unchanged for tasks on one thread (§15.6.2).

## 24.4. Consequences

Each fact follows from §24.2, §24.3 and existing rules.

| Fact | Follows from |
| --- | --- |
| The task slot cannot be captured, stored, returned or bound as a Type argument | It declares no binding and has no Type (§24.2.2); `task` elsewhere is an ordinary Name |
| It is not an elision input and has no per-call Origin | It is no parameter (§24.2.3), so §15.4.3 and the per-call Origins of §8.6 do not apply |
| Accessors, `drop`, `main`, test functions, static initializers, constructors, defaults and Deferred Blocks never suspend | They cannot reach a task slot (§24.2.1, §24.2.2) |
| Loans of locals may be held across a suspension | Call protection covers the whole call (§15.6.4); locals live until scope exit or Move (§5.2.1); there are no exceptions (§17.1); Abort does not unwind (§17.3.3) |
| Suspension points are locally visible | A task call is written with `task;`, so it is recognized by its syntax, and its task slot can only be the innermost function's |
| A forgotten `task;` never selects another overload | One function group has one shape (§24.2.3), so the call is a missing task argument at its only candidates |
| Function Types that differ in the task slot do not convert | §24.2.4 |
| Cleanup neither suspends nor observes cancellation | A Deferred Block cannot pass its function's task (§24.2.2); cancellation is observed only by waits (§22.1.3.4) |

If the authority to suspend were a `uniq` borrow, a captured Reborrow would be lifetime-correct (§7.6.2, §15.6.3), and a synchronous higher-order function could call a Closure that suspends inside its own frame. The hazard is which frames are running, not how long the authority lives, so the task slot states it directly.

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

## 24.5. Diagnostics

| Code | Requirement | Primary range | Related locations |
| --- | --- | --- | --- |
| `TaskSlotPosition_Kd` | A task slot or task argument stands only where §24.2 permits it | The task slot, the task argument, or the Name `task` | The innermost function's task slot when it has one; the Deferred Block or the defaulted parameter for those positions |
| `TaskArgumentMismatch_Kd` | A call writes the task argument exactly when its callee has a task slot (§24.2.2) | The argument list, or the task argument | The callee declaration |
| `TaskSlotShapeMismatch_Kd` | One function group has one task-slot shape (§24.2.3) | The later declaration's task slot or list start, or the called Name for a group gathered at a use | The declarations with the other shape |

All three are `Language` Errors (§23.3.6.1).

- **`TaskSlotPosition_Kd`.** The position is a Reason value. Recovery also recognizes `task` followed by `;` after another item of an admitting list and at the start of the lists under "No list for it", although `task` is an ordinary Name there. It reads such a `task;` as a task slot or task argument, so no `;`, missing-Type or unknown-name record follows.
  - **Not first.** A `task;` after another item of its list.
  - **No caller passes a task.** A task slot in a constructor, a foreign function declaration, an Application's `main` or a test function.
  - **No list for it.** A `task;` at the start of an accessor signature, an Attribute or `$` operation argument list, an inferred Case payload or a Pattern.
  - **No task slot to pass.** A task argument where the innermost function has no task slot.
  - **Cleanup and defaults.** A task argument directly in a default expression or in the body of a Deferred Block that the function registers.
  - **Unresolved `task`.** When a function that lexically encloses the use has a task slot, a Name or capture entry `task` that resolves to nothing, with the Note that the task slot is no value, is passed as `task;`, and can be passed only directly in a function that itself has a task slot, never from a nested anonymous function without one. As the first argument of a call whose callee has a task slot, the use is reported instead as a missing task argument, whose `Repair.PassTask` replaces the Name `task` and the comma after it, if any, with `task;` (`foo(task, x)` becomes `foo(task; x)`, `foo(task)` becomes `foo(task;)`).
- **`TaskArgumentMismatch_Kd`.** The Reason is `missing` or `extra`. Because one function group has one shape, the Reason is known from the Name or the Function Type before overload resolution, and the task argument binds no parameter, so adding or removing it changes no other matching. A qualified Case construction, a construction `T.init(task; x)` and a base initializer are calls whose callee has no task slot (`extra`). When the callee has no task slot, `extra` is the only record for that task argument, wherever it is written; the `TaskSlotPosition_Kd` Reasons "No task slot to pass" and "Cleanup and defaults" apply only to a task argument whose callee has a task slot. A `missing` record carries `Repair.PassTask` under the conditions of §23.3.6.9. An `extra` record carries no candidate.
- **`TaskSlotShapeMismatch_Kd`.** Located and related as `ParameterShapeMismatch_Kd` is (§7.3.1): once per later declaration of a group formed by declarations, and at the called Name for a group gathered at a use. A call that the violation leaves without a selection is derived from the declaration error (§23.3.6.4). Task-slot presence is not part of a function Signature (§9.1); when the two declarations also have equal Signatures, the duplicate-declaration record is published as well, with the Note that a task slot does not distinguish overloads.
- **Compatibility.** A task-slot mismatch under §24.2.4 keeps the existing record of its site, with the task slot as a Reason value: a missing Contract implementation, whose record relates a same-named declaration that differs only in the task slot (§8.4.5); an unsatisfied Callable Constraint that leaves no applicable candidate (§8.6, §10.1 step 5); a Type mismatch at a common Function Type (§10.2); or a specialization without a target, whose record relates the same-named original that differs only in the task slot (§8.8.1).
- **Static Loans.** A task call that conflicts with a static Loan under §24.3 is `ComparisonLoanConflict_Kd` (§15.6.4), located at the call, with the Reason `suspension point (other tasks)`, which states that available bounds (`confined`, `preserves results`) do not exclude the comparison. Related locations name the Loan, its static anchor, the task argument and the later use. When the same call's summarized or unknown environment effects also conflict with the Loan, one record carries both Reasons (§23.3.6.4).
- **Other `;`.** Any other `;`, not part of a task slot, a task argument, or a `task;` that `TaskSlotPosition_Kd` reports, keeps its syntax diagnostic, whose Reason states that `;` occurs only after a leading `task` in the lists of §24.2.1 (§2.4).

The Compiler Server Protocol lists each suspension point (§23.5.3).
