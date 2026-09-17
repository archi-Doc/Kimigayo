# Kimigayo specification tour

These examples illustrate the source-level rules in [SPEC.md](../../SPEC.md).
They are a reading guide, not an executable conformance suite. Several features
still require compiler work; [STATUS.md](../../STATUS.md) records the boundaries.

Start with [Main.kimi](Main.kimi). All files belong to one Kotonoha, and their
SpecTour fragments share declarations. Helpers are defined locally; standard
operations use the specified Kimi identities.

## Files and topics

| File | Main examples | Specification |
| --- | --- | --- |
| [Main.kimi](Main.kimi) | Explicit main, document aliases, root names, defer | Chapters 9, 18, 22 |
| [Basics.kimi](Basics.kimi) | Literals, interpolation, Tuples, overloads, named/default arguments, Copy, constructors, Properties | Chapters 2–3, 6–13, 19 |
| [Sequences.kimi](Sequences.kimi) | Fixed/dynamic arrays, length parameters, Index/Range/Slice, Dictionary, iteration, partial Move | Chapters 4, 12, 14–15 |
| [Generics.kimi](Generics.kimi) | Static Contracts, refinement, associated Types, witnesses, conditional conformance, specialization | Chapters 8–10 |
| [Lifetimes.kimi](Lifetimes.kimi) | Origins, stored/returned borrows, reborrowing, consumption, reinitialization, deinit | Chapters 3, 15–16 |
| [Callables.kimi](Callables.kimi) | Function Items, Closures, captures, Shared/Exclusive/Consuming calls, Callable, Owned | Chapters 7–8, 15 |
| [ControlFlow.kimi](ControlFlow.kimi) | Enums, match/guards, Option/Result, require, loops, labels, iterators, Abort | Chapters 6, 14, 16–17, 22 |
| [Objects.kimi](Objects.kimi) | Inheritance, object ownership/views, type refinements, Sealed payload borrows, whole-value updates | Chapters 3, 6, 12–15 |
| [Native.kimi](Native.kimi), [Native.Methods.kimi](Native.Methods.kimi) | C layout, fragments, raw pointers, unsafe, LibraryImport | Chapters 5–6, 21–22 |
| [native/observer.c](native/observer.c) | External C implementation reading NativeRecord | Chapters 21–22 |
| [SpecTour.kimiproj](SpecTour.kimiproj) | Application configuration and target selection | Chapter 20 |

## Expected behavior

These results follow the specification; they are not claims of native execution:

- Sequences.sum borrows its fixed array. Incrementing [10, 20, 30, 40] produces a sum of 104.
- Generics.forward selects the classify<i32> specialization and returns 1.
- Lifetimes.MutView retains an external borrow. Finish using get's child borrow before consuming the view with take. Gauge's final value is 50.
- Callables.applyTwice changes its captured count from 10 to 11 to 13; the original count remains 0.
- ControlFlow propagates Result with match. main supplies Some(42); None in required demonstrates Abort.
- Lifetimes finishes with defer B, nested defer, audit 2, defer A, audit 1. securedResult secures 1 before its defer runs.

Objects.create uses Kimi.makeObj. Sensor is non-open, so readPayload and
replacePayload use explicit ref/Sensor and uniq/Sensor projections. The
same-complete-Type overwrite receiver may project implicitly; a Device base
View remains protected. swapPayloads exchanges contents while preserving each
object's Identity and Dynamic Type. The APIs and dependencies are defined in
[whole-value updates](../../spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates).
For an executable ordinary-value example, see [WholeValueReplacement](../WholeValueReplacement/README.md).

## Boundaries and configuration

Object/Weak APIs are specified, but their runtime is not implemented here.
Runtime Contract Views, checked-cast syntax, Mod host APIs, concurrency, and
other deferred features must not be inferred from these examples. Static
Contracts and concrete object views use their existing rules. All tour files
stay in one Kotonoha; dependency artifacts and binary interfaces are separate
compiler concerns.

SpecTour.kimiproj illustrates configuration. Native toolchain/backend paths must
match the host, and observer.lib is not supplied. Changing those paths does not
make currently unsupported language features executable. NativeDemo and the
raw-pointer helpers require valid caller-supplied pointers; null is used only
for typing and comparison.

The parser regression suite reads every tour file under the Windows target.
This verifies syntax, not complete Binding, lifetime analysis, native generation,
or execution. Interpolation may allocate owned strings, so the tour makes no
allocation-free claim. For a minimal executable program, see [Hello](../Hello/README.md).
