# Language milestones

Five short, independent programs based on the current [SPEC](../SPEC.md).
They are staged compiler implementation targets. Milestones 1–3 are verified through
native execution (2026-09-16); Milestones 4–5 remain future targets, and their
outputs below are specification expectations rather than execution claims.

| Program | Added concepts |
| --- | --- |
| [Milestone1](Milestone1.kimi) | Hello World, top-level startup, owned string argument |
| [Milestone2](Milestone2.kimi) | `let`/`var`, `i32`, arithmetic, `while`, `if`/`else`, `$abort` |
| [Milestone3](Milestone3.kimi) | Explicit `main`, function calls, arguments/results, `return`, `defer` |
| [Milestone4](Milestone4.kimi) | `struct`, `init`, fields, whole-value Move, owned parameters, `deinit` |
| [Milestone5](Milestone5.kimi) | `uniq`/`ref`, returned borrow, `origin`/`from`, scope and destruction lifetimes |

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

For all unmodified programs, successful output lines end with LF, stderr is empty,
and normal termination returns exit code 0. Abort variants skip any remaining
ordinary cleanup and return exit code 1 under the specified Windows runtime.
