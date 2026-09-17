# Language milestones

Fourteen short, independent programs based on the current [SPEC](../SPEC.md).
They are staged compiler implementation targets. Milestones 1–10 have
native execution evidence (2026-09-17); Milestones 11–14 remain specification
targets, and their outputs below are expectations rather than execution claims.
Milestones 6–9 were originally added without compiler capability checks, builds,
or execution; subsequent verification is documented per program below.
Milestones 10–14 were originally added from the specification without compiler
capability checks, builds, or execution. Subsequent verification is recorded below
and in [STATUS.md](../STATUS.md).

| Program | Added concepts |
| --- | --- |
| [Milestone1](Milestone1.kimi) | Hello World, top-level startup, owned string argument |
| [Milestone2](Milestone2.kimi) | `let`/`var`, `i32`, arithmetic, `while`, `if`/`else`, `$abort` |
| [Milestone3](Milestone3.kimi) | Explicit `main`, function calls, arguments/results, `return`, `defer` |
| [Milestone4](Milestone4.kimi) | `struct`, `init`, fields, whole-value Move, owned parameters, `deinit` |
| [Milestone5](Milestone5.kimi) | `uniq`/`ref`, returned borrow, `origin`/`from`, scope and destruction lifetimes |
| [Milestone6](Milestone6.kimi) | Value-producing `loop`, guarded `match`, `continue`, named `exit`, `yield`, `require` |
| [Milestone7](Milestone7.kimi) | Two-dimensional fixed arrays, nested `for`, cross-loop transfers, Slice reads |
| [Milestone8](Milestone8.kimi) | Groups nested in a struct, generic struct/function, generic Copy/Move acquisition |
| [Milestone9](Milestone9.kimi) | Length/type parameters, Copy constraint, callbacks/capture, generic enum, borrowed storage |
| [Milestone10](Milestone10.kimi) | Generic enum/Tuple patterns, guards, value-producing loop/match/if, cross-loop transfers |
| [Milestone11](Milestone11.kimi) | Immutable static member, multiple type/length instantiations, generic forwarding, explicit specialization, sharing invariants |
| [Milestone12](Milestone12.kimi) | Mutable/nested/Move captures, exclusive Callable, concrete and common function values |
| [Milestone13](Milestone13.kimi) | Generic Iterator conformance, Slice storage, external Origins, retained element borrows |
| [Milestone14](Milestone14.kimi) | Generic Slice pipeline, Iterator, exclusive borrowed capture, obj creation/Move/destruction |

Each file is a separate Application; do not combine them into one project.
Under [single-source input rules](../spec/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects),
the intended build/run commands, once the required features are implemented, are:

```powershell
kimi build milestones/Milestone1.kimi
kimi run milestones/Milestone1.kimi
```

Replace `1` with the milestone number. `run` executes an existing build; it does
not compile source. No separate project file is needed. Output uses fixed string
literals so these milestones do not require numeric formatting or interpolation.

## Milestone 1: Hello World

Expected stdout:

```text
Hello, world!
```

Focus: [startup and console output](../spec/22-core-execution-and-foreign-functions.md).

Milestone 1 uses the existing compiler implementation without source changes.
With the pinned Windows x64 LLVM toolchain installed, reproduce its full checks:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone1.ps1 -Configuration Release
```

The script builds this exact file as an implicit single-source Application, then
tests byte-identical renamed copies at O0/O2 through ordinary project settings.
It verifies LLVM IR through the normal build pipeline, runs the generated native
executables and CLI `run`, and checks exact UTF-8 stdout, empty stderr, and exit 0.
Separate variants cover owned string Move, Unicode, embedded NUL, empty strings,
and ten rejected inputs. Results and build identities are retained under
`bin/milestone1/<configuration>/<run-id>/`. Debug is also supported. The compiler
must be built before running the script; it does not build or publish NativeAOT.

The program numbers here are independent of PLAN.md's broader M1–M17 stages.

## Milestone 2: control flow and Abort

Compute 1 through 10's sum and compare it with `expected`.

```text
Sum is 55.
Done.
```

Change `expected` from `55` to `54` to take the Abort branch. The process must
emit an Abort diagnostic to stderr, exit with code 1, and never print `Done.`.
Abort is process termination, not a catchable exception.

Focus: [control flow](../spec/14-control-flow.md) and
[explicit Abort](../spec/17-failure-handling.md#173-abort-termination).

Milestone 2 is verified through native execution, including the `expected = 54`
variant. Reproduce with the pinned Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone2.ps1 -Configuration Release
```

The script builds the original file as a single-source Application, checks
byte-identical renamed copies and separate Abort variants at O0/O2, and verifies
native execution plus both forms of CLI `run`. Normal output is exactly
`Sum is 55.\nDone.\n`, stderr is empty, and exit is 0. The Abort variant has empty
stdout, reports `KIMI_E_ABORT: Unexpected sum` at line 13, column 5 on stderr,
and exits 1 without printing `Done.`. Six invalid inputs must fail before IR or
executable publication. Reports and build identities remain under
`bin/milestone2/<configuration>/<run-id>/`; Debug is also supported.

`AbortEmissionTest` additionally generates native fixtures for UTF-8/NUL/empty
messages, owned strings, shadowing, nested Abort, one-time argument evaluation,
argument return/overflow, skipped cleanup and Never conditions. After running the
managed tests, execute them with
`./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Abort*.ll'`.

## Milestone 3: function calls and cleanup

Extract the sum into `sumTo`. Its result is secured before its deferred cleanup;
the caller receives that result after cleanup completes.

```text
Leaving sumTo.
Sum is 55.
Leaving main.
```

Change `sumTo(10)` to `sumTo(-1)` to exercise Abort. Neither deferred message
runs, even though both defers have been registered: Abort does not unwind.
Normal runs must not mix explicit `main` with top-level executable statements.

Focus: [functions](../spec/07-functions-and-callable-values.md) and
[cleanup and result delivery](../spec/16-scope-exit-and-destruction.md#162-scope-exit-destruction).

Milestone 3 passes with the existing compiler, including the explicit Abort
implementation completed for Milestone 2. With the pinned Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone3.ps1 -Configuration Release
```

The script builds the original single-source input and byte-identical renamed
copies at O0/O2. It checks exact stdout/stderr and exit codes through native
execution and both CLI `run` forms. Additional copies test `sumTo(-1)` (no output
or deferred cleanup, Abort at 5:9, exit 1), `sumTo(9)` (only `Leaving sumTo.` before
the caller Aborts at 18:9), and deferred mutation of `total` after securing its
return value (the original successful output remains unchanged). Ten invalid
argument/result/startup/defer/ownership inputs must fail before emission.
All variants are generated separately; the milestone file is never edited.
Reports and source/compiler/build identities remain in
`bin/milestone3/<configuration>/<run-id>/`; Debug is also supported.

## Milestone 4: struct ownership and destruction

`Counter.init()` initializes its field. `finish(counter)` transfers the entire
Non-Copy struct into an owned parameter. Move does not rerun `init` or `deinit`.
The parameter is destroyed once when `finish` exits, after its defer; the moved
source in `main` has no remaining destruction responsibility.

```text
Counter created.
Sum is 55.
Leaving finish.
Counter destroyed.
Done.
```

As a separate rejection exercise, add `counter.value` after `finish(counter)`:
reading a moved value must be rejected. A struct with user-defined `deinit`
cannot opt into Copy or permit Partial Move that makes it incomplete.

Focus: [construction](../spec/06-declarations-and-containers.md#623-constructors),
[Move](../spec/15-ownership-and-lifetime-analysis.md#1515-movable-places), and
[destruction](../spec/16-scope-exit-and-destruction.md#163-aggregate-destruction-and-deinit).

Reproduce Milestone 4 with the pinned Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone4.ps1 -Configuration Release
```

The script builds the unchanged single-source Application and byte-identical renamed
copies at O0/O2, verifies LLVM through the normal pipeline, and compares native and
both CLI `run` forms against exact output and exit status. Successful runs print
the five lines above, have empty stderr, and exit 0. A separate loop-limit-9 variant
prints only `Counter created.`, reports Abort at 14:9 and exits 1 without either
defer or deinit. A destructor-inspection variant verifies the value is still 55
at destruction. Twelve invalid inputs must fail before IR/executable publication.
Variants never edit the checked-in program. Reports and build/source/compiler
identities are under `bin/milestone4/<configuration>/<run-id>/`; Debug is supported.
After running managed tests, related structure fixtures (including string release
audits) can be executed with
`./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Struct*.ll'`.

## Milestone 5: borrowing, Origins, and Lifetime

`add` mutates through an exclusive borrow without acquiring the Counter's
ownership. `borrowCounter` returns a shared reference whose validity is bounded
by its input Origin. `CounterView` stores that reference under its declared
`source` Origin; creating the view never extends the Counter's lifetime.

```text
Borrowed sum is 55.
View destroyed; counter is still 55.
Final value is 56.
Counter destroyed.
Done.
```

The `do` scope destroys `view` before mutation resumes. Its deinit observes the
borrowed Counter, keeping that shared Loan active through destruction even after
the last explicit `view.read()` call. Destroying a shared reference does not
destroy its referent. After the view's cleanup, another exclusive borrow and
then a whole-value Move are legal.

Try each of these independently as a compile-time rejection exercise:

- Insert `add(counter@uniq, 1)` at the end of the `do` body: mutation conflicts
  with the shared Loan still needed by `view`'s destruction.
- Insert `finish(counter)` at the same location: Move conflicts with that Loan.
- Replace `borrowCounter`'s body with construction of a local Counter followed
  by `return local@ref`: a local lifetime cannot satisfy the caller's Origin.

Focus: [Origins](../spec/15-ownership-and-lifetime-analysis.md#153-abstract-origins),
[Loans and lifetimes](../spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing),
and [destruction lifetime checking](../spec/15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking).

Milestone 5 is verified through native execution. Reproduce with the pinned
Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone5.ps1 -Configuration Release
```

The script verifies LLVM IR and native linking, then checks native execution and
both forms of CLI `run`. Its 47 checks per Debug/Release compiler cover the
unchanged input, byte-identical renamed O0/O2 copies, alternate numeric values,
immediate temporary borrows, two Abort paths and fourteen rejected inputs.
Normal output is exactly the five lines above, with empty stderr and exit 0.
The Abort variants exit 1 with the expected diagnostic and no termination cleanup.
Invalid cases include mutation/Move/replacement while deinit needs the borrow,
temporary escape, shared writes, exclusive aliasing and parent access during a
required reborrow. Reports and source/compiler/build identities are retained under
`bin/milestone5/<configuration>/<run-id>/`. The script does not run NativeAOT.

## Milestone 6: value-producing control flow

Search the positive integers with a `loop`, skip even values and 3 using
`continue`, and leave the loop with 7 through a guarded `match` arm. The label
on the loop makes the result destination explicit. An indented `if` supplies
its result with `yield`; a labeled `do` supplies its result with named `exit`.
The `require` failure body exits that `do`, while the final require Aborts.

```text
Selected 7.
Control flow passed.
```

Change `yield found * 10` to `yield 0` to take the do's early false exit and
the final Abort. An unlabeled `exit` would skip the `do` rather than return its
result. `require` is an ordinary statement, distinct from test-only `$require`.

Focus: [loop results](../spec/14-control-flow.md#1463-loop),
[transfer targets](../spec/14-control-flow.md#1452-target-lookup),
and [require](../spec/14-control-flow.md#1411-require-statement).

Milestone 6 uses the existing compiler implementation without compiler source
changes. Reproduce its complete checks with the pinned Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone6.ps1 -Configuration Release
```

The script builds the unchanged input and byte-identical renamed copies, verifies
LLVM IR and native linking at O0/O2, and runs the executables and both CLI `run`
forms. Exact normal stdout is the two lines above, with empty stderr and exit 0.
The alternate search threshold confirms that the literal `3` arm continues before
the guard and produces 5. Other variants check guard side effects, the if/else
paths, early require failure, final comparison failure and cleanup through
continue/yield/named exit. Abort variants check exact diagnostics, exit 1 and
suppression of pending main cleanup. Twelve invalid inputs cover missing or
wrong transfer targets, result mismatches (including unreachable results), implicit
Unit, a normally continuing require failure body, non-bool guards, non-exhaustive
match and escaped arm-local bindings. Rejections must precede IR/executable
publication. Each configuration has 63 checks; reports and source/compiler/build
identities remain under `bin/milestone6/<configuration>/<run-id>/`. Debug is also
supported. The script does not build or run NativeAOT.

## Milestone 7: arrays and nested iteration

Traverse a fully initialized 3-by-4 fixed array using its `indices` snapshots.
These are iterable ResolvedRange values with isize indices; an unresolved
`0..length` Range is not directly iterable. Double each visited cell, skip the
row beginning with 0, and stop both loops before changing the cell holding 7.
The accumulated total is `2 + 4 + 6 + 8 + 10 + 12 = 42`.

```text
Row finished.
Row finished.
Row finished.
Matrix total is 42.
Borrowed row total is 20.
```

Normal row completion, `continue to rows`, and `exit to rows` each run the row's
defer exactly once. The final Slice borrows the first row instead of acquiring
its elements by ownership. Iteration over the Slice's indices and indexed reads
Copy its i32 elements for arithmetic. Direct Slice iteration would instead yield
`ref/i32`; safe references cannot be converted to owned integers with `@i32`.
Fixed-array storage and Slice creation need no extra heap allocation for element
storage.

As separate rejection exercises, change the outer length to 2 without removing
a row, or attempt to write an element through the Slice. To exercise runtime
bounds failure, change the final `matrix[2][2]` check to `matrix[3][2]`.

Focus: [arrays](../spec/04-arrays-indexing-and-slices.md),
[iteration acquisition](../spec/14-control-flow.md#1462-iteration-protocol-and-acquisition),
and [transfer cleanup](../spec/16-scope-exit-and-destruction.md#162-scope-exit-destruction).

Milestone 7 is verified through native execution. Reproduce with the pinned
Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone7.ps1 -Configuration Release
```

The script performs 52 checks per configuration, covering the unchanged program,
byte-identical renamed O0/O2 copies, alternate arithmetic, outer exit instead of
continue, normal exhaustion, matrix/Slice bounds Abort, and thirteen invalid
inputs rejected before emission. It checks LLVM verification, linking, exact native
stdout/stderr and exit codes, plus both CLI `run` forms. Successful output is the
five lines above, stderr is empty and exit is 0. Bounds failures report the exact
source position and `KIMI_E_INDEX_BOUNDS`, then exit 1. Reports and source/compiler/
build identities are retained under `bin/milestone7/<configuration>/<run-id>/`.
Debug is supported; NativeAOT is not used.

The current executable subset supports the `indices` ResolvedRange adapter and
inferred full Slice views with scalar indexed reads. It does not establish the
broader PLAN.md M8 stage, general user-defined iteration, direct Slice iteration,
or all specified Kimi sequence APIs. Those remain separate implementation work.

## Milestone 8: nested containers and generic ownership

The Toolkit struct contains two static groups. `Toolkit.Storage` contains
`Box<T>`, while its sibling `Toolkit.Selection` contains `choose<T>`. These
declarations add no Toolkit instance or implicit receiver. Both groups and
structs can contain further declaration containers.

```text
Chosen item.
Boxed array total is 12.
Generic scope finished.
```

`choose` acquires both arguments, returns one, and cleans up the other. Neither
it nor `Box` requires Copy: the generic definitions must handle both Copy and
Move acquisition. `take` consumes the Box; a string field Moves out, while the
fixed i32 array field Copies out. The examples exercise both cases. Explicit
public members make the paths accessible from outside their declaring groups.

As separate rejection exercises, reuse `selected` after `selected.take()`, or
add a `deinit` to Box while retaining its generic extracting `take`: the latter
cannot permit a Non-Copy field Move that makes a destructor-bearing Box incomplete.

Focus: [nested containers](../spec/06-declarations-and-containers.md#611-root-and-nested-containers),
[generic checking](../spec/08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations),
and [partial Move](../spec/15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move).

## Milestone 9: a generic search pipeline

`Batch<T>` owns initialized storage and lends an explicitly typed `ref/T` tied
to its receiver. `find<length N, T>` borrows a fixed array, calls a predicate,
and returns a generic enum containing either an index/value pair or no match.
The Copy constraint permits reading a T from shared storage, supplying it to
the callback, and using it again in the successful result.

```text
Found 6 at index 3.
Missing value handled.
Search finished.
Batch destroyed.
```

The first call uses an anonymous function explicitly capturing the Copy target.
The second instantiates N as zero, demonstrating safe exhaustion without any
element access. The result match uses payload binding and a guard, followed by
an unguarded Found arm and Missing arm so that it remains exhaustive. The defer
runs before Batch destruction. For the i32 instantiations shown, returned values
retain no borrow of Batch; in general, a Copy T can itself carry Origin dependencies.

The callback avoids assuming an undeclared comparison capability on arbitrary T.
`@ref/T` in `view` explicitly borrows the stored slot, preserving its layer even
if another instantiation supplies a reference Type as T.

As separate rejection exercises, remove `T is Copy`, use a mismatched explicit
length in the first call, or remove the unguarded Found fallback. The guarded
Found arm alone does not prove coverage of every Found value.

Focus: [function length parameters](../spec/04-arrays-indexing-and-slices.md#44-function-length-parameters),
[function expressions](../spec/07-functions-and-callable-values.md#76-function-expressions),
[enum results](../spec/06-declarations-and-containers.md#63-enums), and
[Origins](../spec/15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts).

Reproduce the Milestone 9 integration checks with the pinned Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone9.ps1 -Configuration Release
```

The script verifies the unchanged program and byte-identical renamed O0/O2 copies,
then executes each native binary and both CLI `run` forms. It checks exact output,
empty stderr and exit 0, including defer-before-destructor ordering. Variants cover
first/last/singleton matches, absence, nonempty exhaustion, i64 instantiation and
Abort without cleanup. Eight invalid programs check payload/callback Types, arity,
Copy constraints, lengths, coverage, moved callbacks and local borrow escape.
There are 59 checks per compiler configuration. Reports and build identities are
retained under `bin/milestone9/<configuration>/<run-id>/`. Debug is also supported;
the script does not build or run NativeAOT.

## Milestone 10: patterns inside result-producing control flow

A fixed array contains generic enum commands whose Data payload is a Tuple.
The guarded `.Data((let value, true))` arm accepts only positive enabled values;
an unguarded Data arm covers every remaining payload. Skip and rejected Data
commands continue the inner for. Stop exits the labeled outer loop with the
running total, so the trailing 100 is never processed. Exhaustion has its own
result exit. Finally an indented if combines require with yield.

```text
Accepted total is 12.
Pattern run finished.
```

The conditional Copy declaration makes the command array Copy for this Tuple
instantiation. As separate exercises, remove the unguarded Data arm to produce
a coverage error, or replace the array's Stop input with Skip to reach 112 and
the final Abort.
On Abort the main defer does not run.

Focus: [patterns and matching](../spec/14-control-flow.md#148-match-expressions-and-patterns),
[transfer targets](../spec/14-control-flow.md#1452-target-lookup), and
[conditional conformance](../spec/08-generics-constraints-and-contracts.md#848-conditional-conformance).

Reproduce the complete program checks with the pinned Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone10.ps1 -Configuration Release
```

The script performs 54 checks per configuration, covering the unchanged source,
byte-identical renamed O0/O2 copies, alternate values, exhaustion, empty input,
immediate Stop, guard cleanup, Abort, and nine invalid inputs. It checks LLVM
verification, native linking, exact UTF-8 stdout/stderr and exit status through
direct execution and both CLI `run` forms. Normal output is the two lines above,
stderr is empty and exit is 0. Abort exits 1 without the main defer. Invalid
payload/pattern shapes, duplicate names, candidate assignment, wrong guard Types,
missing Cases/targets, uninitialized storage and escaped bindings fail before
IR or executable publication. Reports with source/compiler/build identities are
retained under `bin/milestone10/<configuration>/<run-id>/`. Debug is also supported;
the script does not build or run NativeAOT. This independent program's completion
does not imply completion of program 9 or of broader compiler plan stages.

## Milestone 11: generic sharing and implementation selection

The same total/forward definitions process i32, i64, and bool arrays, with lengths
3 and 2. Borrowed element access needs no Copy constraint or per-element owned
temporary. The default weight reads `Weights.defaultWeight`, an immutable static
i32 stored Property initialized to 1; the explicit i32 specialization returns 2.
Group members are inherently static; a `static` modifier is not valid syntax.
The one group slot is shared by all calls and is not instantiated per generic
Type or length. The generic forwarding call must preserve specialization selection.

This static-member step uses only a literal initializer and Copy reads, with no
mutation, user-defined cleanup, or initialization dependencies. It still obeys
[per-slot first-access initialization](../spec/22-core-execution-and-foreign-functions.md#2223-os-entry-static-initialization-and-shutdown);
constant folding may remove machinery only when it preserves the specified
behavior. It does not establish coverage of general static initialization.

```text
Generic weights are 6, 3, 2.
```

This is a code-generation target, not a promise that the source forces one
machine-code body. The compiler establishes a correct baseline sharing plan and
may apply bounded automatic specialization or other permitted optimization.
The result must stay identical across sharing/specialization budget choices;
an automatic specialization budget of zero must still select the explicit i32
implementation. Ordinary output alone cannot prove physical code sharing.
Generation verification inspects emitted body/context identities and selected
call targets in addition to execution, before permitted inlining or elimination.

As a source exercise, remove the explicit specialization and change the expected
Tuple to `(3, 3, 2)`. Do not add a specialization directive: `specialize func`
selects a source implementation, while baseline sharing is a compiler policy.

Focus: [full specialization](../spec/08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization)
and [generic code generation](../spec/21-layout-runtime-and-code-generation.md#213-generic-code-generation).

Reproduce the current executable integration with the pinned Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone11.ps1 -Configuration Release
```

The script checks the unchanged original and byte-identical renamed O0/O2 copies,
exact stdout/stderr and exit status through native execution and both CLI run
forms. Separate variants change default/specialized weights, the specialization
key, array lengths and identifiers; ten invalid inputs must fail before emission.
Debug is also supported. Reports and build/source/compiler hashes are retained
under `bin/milestone11/<configuration>/<run-id>/`. This literal-only static path
does not implement effectful initialization or mutable static storage. Closed
Type specializations without inherited defaults, Constraints or explicit Origin
binders are supported here; broader specialization forms remain explicit limits.

## Milestone 12: capture acquisition and call requirements

`next` owns a mutable snapshot of count; two exclusive calls produce 11 and 13
while the outer count stays zero. `applyTwice` expresses the receiver requirement
with `Callable<uniq, ...>` and accepts the concrete closure without erasing it.
The nested factory copies offset through both capture environments. Its returned
Shared, Owned, Copy closure can also convert to a common function value while
the concrete source remains usable. The common value itself is Non-Copy.

```text
Stateful result is 13.
Nested capture result is 18.
Captured message.
Closure run finished.
```

The final closure explicitly Moves its string capture and consumes it during
the call. As separate rejection exercises, call send twice, change next's
binding to let while keeping its exclusive call, or convert next to a common
`(i32) -> i32` value: common function values admit only Shared call requirements.
Removing offset from the outer factory's explicit capture list must also fail;
the inner closure cannot bypass the enclosing capture boundary.

Focus: [captures and calls](../spec/07-functions-and-callable-values.md#76-function-expressions),
[Callable constraints](../spec/08-generics-constraints-and-contracts.md#86-callable-constraints),
and [closure generation](../spec/21-layout-runtime-and-code-generation.md#2125-concrete-closures-and-common-function-values).

## Milestone 13: a Slice-backed Iterator

Cursor implements the Kimi Iterator Contract and explicitly binds its Element
to `ref/T from source`. It stores a borrowed Slice and a position; it owns no
Sample elements. Its next method yields a borrow of external backing storage,
not of the Cursor or next's exclusive receiver. This makes it non-lending:
the first reference remains valid while later calls advance the iterator.

```text
Even sample total is 6; first is still 1.
Iterator remains exhausted.
Middle slice total is 5.
```

The first sample is secured separately, then Option matching and require/continue
select 2 and 4. Repeated next calls after exhaustion return None. The final for
uses Slice's built-in Iterable/Iterator conformance over the half-open range
`1..3`, yielding shared Sample references for values 2 and 3. Field reads Copy
i32 values; no conversion from a safe scalar reference to an owned integer is
assumed. The fixed backing array, Slice, and Cursor need no separate heap buffer.

As separate rejection exercises, declare the associated Element as i32 without
changing next's result, or Move samples while first still has a later use.
Changing the next result Origin to self would break the advertised external
Element contract and the retained-reference use case.

Focus: [Kimi iteration contracts](../spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations),
[associated Types](../spec/08-generics-constraints-and-contracts.md#843-associated-types),
and [Slice iteration](../spec/04-arrays-indexing-and-slices.md#467-slice-iteration-and-nested-origins).

## Milestone 14: an object-backed processing pipeline

The generic run function uses a Slice's iterator and an exclusive Callable,
stopping after three accepted jobs or exhaustion. Its callback rejects -1,
adds 2, 4, and 6, and stops before 100. Kimi.makeObj acquires a fully initialized
Accumulator into a new exclusive object without repeating construction.
Its methods read or update fields while preserving whole-object completeness.

```text
Accepted three jobs.
Accumulator destroyed.
Object total is 12.
Accumulator destroyed.
Pipeline finished.
```

An explicit uniq local projected from the Sealed Accumulator payload is Moved into the callback's environment using an
ordinary capture. The callback remains concrete: its external borrow and
Exclusive call requirement cannot be erased into a common function value.
The do scope ends its Loan before the owning handle is read or Moved again.
Whole-value exchange then installs fresh payload contents without changing object
Identity. The returned old contents retain their own destruction responsibility;
the program restores the total before they are destroyed. A consuming closure
then transfers that handle to finish; normal parameter
cleanup destroys the payload once and releases its object storage. Neither
object borrowing nor moving the handle creates another object.

As separate rejection exercises, read accumulator inside the do before the
callback's final use, reuse accumulator after capture into complete, or call
complete twice. Change the acceptance limit from 3 to 4 to process 100 as well:
the accepted-count check then Aborts and skips ordinary cleanup.

Focus: [object creation](../spec/13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing),
[object calls](../spec/12-expressions.md#1243-object-member-calls),
[Callable contracts](../spec/08-generics-constraints-and-contracts.md#86-callable-constraints),
and [destruction](../spec/16-scope-exit-and-destruction.md#163-aggregate-destruction-and-deinit).

For all unmodified programs, successful output lines end with LF, stderr is empty,
and normal termination returns exit code 0. Abort variants skip any remaining
ordinary cleanup and return exit code 1 under the specified Windows runtime.
