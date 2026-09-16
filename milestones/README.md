# Language milestones

Nine short, independent programs based on the current [SPEC](../SPEC.md).
They are staged compiler implementation targets. Milestones 1–6 are verified through
native execution (2026-09-17); Milestones 7–9 are specification targets, and their
outputs below are specification expectations rather than execution claims.
Milestones 6–9 were originally added without compiler capability checks, builds,
or execution; subsequent verification is documented per program below.

| Program | Added concepts |
| --- | --- |
| [Milestone1](Milestone1.kimi) | Hello World, top-level startup, owned string argument |
| [Milestone2](Milestone2.kimi) | `let`/`var`, `i32`, arithmetic, `while`, `if`/`else`, `$abort` |
| [Milestone3](Milestone3.kimi) | Explicit `main`, function calls, arguments/results, `return`, `defer` |
| [Milestone4](Milestone4.kimi) | `struct`, `init`, fields, whole-value Move, owned parameters, `deinit` |
| [Milestone5](Milestone5.kimi) | `uniq`/`ref`, returned borrow, `origin`/`from`, scope and destruction lifetimes |
| [Milestone6](Milestone6.kimi) | Value-producing `loop`, guarded `match`, `continue`, named `exit`, `yield`, `require` |
| [Milestone7](Milestone7.kimi) | Two-dimensional fixed arrays, nested `for`, cross-loop transfers, Slice reads |
| [Milestone8](Milestone8.kimi) | Nested `group` containers, generic struct/function, generic Copy/Move acquisition |
| [Milestone9](Milestone9.kimi) | Length/type parameters, Copy constraint, callbacks/capture, generic enum, borrowed storage |

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

## Milestone 8: nested containers and generic ownership

`Toolkit.Storage` contains `Box<T>`, while its sibling `Toolkit.Selection`
contains `choose<T>`. These are declaration scopes, not runtime objects;
structs are placed inside groups because this specification does not permit
nested declaration containers inside struct bodies.

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

For all unmodified programs, successful output lines end with LF, stderr is empty,
and normal termination returns exit code 0. Abort variants skip any remaining
ordinary cleanup and return exit code 1 under the specified Windows runtime.
