# Language milestones

These 44 independent Applications follow the adopted compiler implementation progression.
Programs 1–18 retain the established foundations; later numbers follow the completed tracks
and the remaining implementation order in [PLAN](../../docs/dev/PLAN.md#4-milestones-execution-order).
An earlier program can use a supported subset of a feature whose general coverage comes later.
The numbering is not a claim that every feature first appears at its own milestone.

This README owns the program catalog, expected output and source/native status.
[PLAN](../../docs/dev/PLAN.md) owns active scope, completion conditions and next actions;
[STATUS](../../docs/STATUS.md) owns compiler support boundaries;
[PLAN_HISTORY](../../docs/dev/PLAN_HISTORY.md) and verification artifacts retain run history.

## Implementation order

The **Former ID** column maps the numbering used before 2026-10-15. Historical commits,
drafts, report names and evidence paths keep their original IDs. Current filenames, harnesses,
tests and plan references use the new IDs; there are no compatibility aliases. Program 44 was added on
2026-10-17 (A5 plan §9.1) and has no former ID.

| Program | Former ID | Main subject |
| --- | --- | --- |
| [1](Milestone1.kimi) | 1 | Hello World, top-level startup, owned string argument |
| [2](Milestone2.kimi) | 2 | `let`/`var`, `i32`, arithmetic, `while`, `if`/`else`, `$abort` |
| [3](Milestone3.kimi) | 3 | Explicit `main`, function calls, arguments/results, `return`, `defer` |
| [4](Milestone4.kimi) | 4 | `struct`, `init`, fields, whole-value Move, owned parameters, `drop` |
| [5](Milestone5.kimi) | 5 | `uniq`/`ref`, returned borrow, `during`, scope and destruction lifetimes |
| [6](Milestone6.kimi) | 6 | Value-producing `loop`, guarded `match`, `continue`, named `exit`, `yield`, `require` |
| [7](Milestone7.kimi) | 7 | Fixed-array inference, borrowed `noinit` writes, nested `for`, cross-loop transfers, Slice reads |
| [8](Milestone8.kimi) | 8 | Groups nested in a struct, generic struct/function, generic Copy/Move acquisition |
| [9](Milestone9.kimi) | 9 | Length/type parameters, Copy constraint, callbacks/capture, generic enum, borrowed storage |
| [10](Milestone10.kimi) | 10 | Generic enum/Tuple patterns, guards, value-producing loop/match/if, cross-loop transfers |
| [11](Milestone11.kimi) | 11 | Generic instantiations, immutable static member, forwarding and explicit specialization selection |
| [12](Milestone12.kimi) | 12 | Mutable/nested/Move captures, exclusive Callable, function values and shared calls with an owned capture |
| [13](Milestone13.kimi) | 13 | Generic Iterator conformance, Slice storage, external Origins, retained element borrows |
| [14](Milestone14.kimi) | 14 | Generic Slice pipeline, Iterator, exclusive borrowed capture, obj creation/Move/destruction |
| [15](Milestone15.kimi) | 15 | Initialization/Move/Loan joins, loop backedges, repair before continue |
| [16](Milestone16.kimi) | 16 | Multiple external Origins, aggregate/enum forwarding, intersection, exclusive reborrow |
| [17](Milestone17.kimi) | 17 | Partial Move repair, element exchange/swap/replace, secured results and destruction order |
| [18](Milestone18.kimi) | 18 | Generic composite/Non-Copy arguments, results, temporaries, Copy acquisition and destruction |
| [19](Milestone19.kimi) | 22 | Concrete generic entries, compound layouts, specialization forwarding and distinct destruction operations |
| [20](Milestone20.kimi) | 19 | Contract requirements, associated Types/equalities, conditional and nested conformance |
| [21](Milestone21.kimi) | 20 | Type/length/Origin inference, ordinary defaults and generic forwarding |
| [22](Milestone22.kimi) | 21 | Full length/Type specialization, inherited defaults/Origins and preserved implementation selection |
| [23](Milestone23.kimi) | 29 | Dynamic `Array<T>` reserve/append/insert/remove/pop/clear, indexed replacement of Non-Copy elements, owning iteration with early exit |
| [24](Milestone24.kimi) | 32 | User/generic UTF-8 formatting, independent owning strings, short-circuit writes, fixed-buffer reuse and bounded Console interpolation |
| [25](Milestone25.kimi) | 30 | User/generic comparison Contracts, borrow/Tuple composition, retained witnesses and NaN-reflexive equality through specialization |
| [26](Milestone26.kimi) | 23 | Standard/custom/computed Copy Properties, direct storage and assignment evaluation order |
| [27](Milestone27.kimi) | 27 | Saved bounds, shared/exclusive child views, Move, Slice.toArray, `noinit` view writes, backing/element Origins, and user Places |
| [28](Milestone28.kimi) | 39 | Semantics-generic follow and reference-valued collection elements |
| [29](Milestone29.kimi) | 28 | User IntoIterable/Iterator mappings, owned elements, continue/early-exit cleanup and retained external element borrows |
| [30](Milestone30.kimi) | 41 | Positions, ranges, resolution, iteration and formatting |
| [31](Milestone31.kimi) | 42 | Wrapping integers, integer results, conversions and generic arithmetic |
| [32](Milestone32.kimi) | 31 | Dictionary duplicate rejection, retained equal keys, replacement/removal, insertion-order iteration and exact entry cleanup |
| [33](Milestone33.kimi) | 40 | Disjoint exclusive Array and fixed-array element access |
| [34](Milestone34.kimi) | 26 | Generic compound captures, external borrowed captures, shared/exclusive/consuming Callable and owning function-value erasure |
| [35](Milestone35.kimi) | 37 | Integrated collection, borrow, iteration and closure workload |
| [36](Milestone36.kimi) | 24 | Non-Copy setter replacement, a borrowed getter, a consuming function and a standard-operation Contract witness |
| [37](Milestone37.kimi) | 43 | User arithmetic Contracts, Function values, element replacement and Result propagation |
| [38](Milestone38.kimi) | 25 | Inline base construction, inherited standard Properties and Type members, whole-derived Move and layered destruction |
| [39](Milestone39.kimi) | 33 | Exclusive objects, base views, struct Type tests/refinement, complete Sealed payload exchange and dynamic layered destruction |
| [40](Milestone40.kimi) | 34 | rc/arc creation, clone, shared access and final strong release |
| [41](Milestone41.kimi) | 35 | Weak downgrade/upgrade, expiration and cyclic construction |
| [42](Milestone42.kimi) | 36 | Static initialization, generic storage keys and shutdown |
| [43](Milestone43.kimi) | 38 | Integrated Properties, inheritance, objects, static state and formatting |
| [44](Milestone44.kimi) | — | Virtual slots, overrides, direct base calls and dispatch by Dynamic Type through views, Items, erased values and handles |

Programs 1–37 and 44 are complete within their recorded boundaries. The remaining main track is
**38 (inheritance) → 39 (exclusive objects) → 41 (Weak) → 42 (static storage) → OCC-X → 43 (integration)**.
Program **40 (shared objects)** is the alternating track; its strong ownership and nonnull
Option prerequisites precede Weak. The compiler reduction and owner holds in PLAN still govern
when implementation resumes. Renumbering does not start or complete any compiler work.

## Program status

`PASS` records verification of the authored program, not all forms of its feature family.
`NOT_RUN` is neither success nor failure. DONE additionally requires the completion conditions
in [PLAN §5](../../docs/dev/PLAN.md#5-completion-conditions-every-program-milestone), including
any explicitly recorded owner decision. The pending diagnostic baselines remain unchanged.

| Program | Created | Binding | Build | Tests | Implementation | Evidence / boundary |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 2 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 3 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 4 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 5 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 6 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 7 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 8 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 9 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 10 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 11 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Explicit specialization selection; generic code sharing is deferred (IMPL §21.3.1). |
| 12 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 13 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 14 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 15 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 16 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 17 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 18 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 19 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 20 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 21 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 22 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 23 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 24 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 25 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 26 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 27 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 28 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 29 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 30 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 31 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 32 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Owner completion (G58); capacity callback work subsequently completed with the closure track. |
| 33 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Original-source O0/O2 evidence; focused semantic tests cover neighboring forms. |
| 34 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Owner completion with recorded gaps; G10/G65/G75/G78 and STATUS retain the remaining boundaries. |
| 35 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Integrated workload; `WorkloadCostTest` retains allocation and scaling checks. |
| 36 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Non-Copy setter, borrowed getter, standard witness and consuming function; SPEC §11.3. |
| 37 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Arithmetic completion is recorded in [PLAN_HISTORY](../../docs/dev/PLAN_HISTORY.md#arithmetic-completion); G10/G83 and native 128-bit division remain separate limits. |
| 38 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | IN_PROGRESS | O0/O2 target passes; exclusive receiver projections belong to OCC-X. Completion review remains with the owner. |
| 39 | YES | FAIL | FAIL (prior native probe) | NOT_RUN | IN_PROGRESS | Binding: `UnresolvedBinding_Kd` at `view.extra`; flow refinement remains incomplete. |
| 40 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | IN_PROGRESS | Original-source O0/O2 passes; U7 defects and completion audit remain in [shared-object work](../../docs/dev/P34_REMAINING.md). |
| 41 | YES | FAIL | NOT_RUN | NOT_RUN | TODO | Binding: `Unsupported_Kd` at `Weak`; Weak/cyclic construction declarations remain incomplete (G4). |
| 42 | YES | PASS | NOT_RUN | NOT_RUN | IN_PROGRESS | Binding passes; Ownership: `Unsupported_Kd` at `Registry`; static initialization/shutdown remain incomplete. |
| 43 | YES | FAIL | NOT_RUN | NOT_RUN | TODO | Binding: `Unsupported_Kd` at `Weak`; also depends on refinement, object accessors and OCC-X. |
| 44 | YES | PASS | PASS (O0/O2) | PASS (O0/O2) | DONE | Shared `objref` dispatch within the STATUS "Virtual calls" boundary; `Virtual*Test` cover neighboring forms. |

`MilestoneSourcesTest` checks the exact 44-file set, these Binding results and every pending
stage, first published Error and underlined source anchor in [stage-baselines.json](stage-baselines.json).
It also binds the supported declaration prefix independently. A different failure or a newly
successful stage needs reviewed evidence; an arbitrary pending failure never passes.

## Running and verifying

Each source is a separate Application. Do not combine the files into one project.
With Kimi and the Windows x64 toolchain installed:

```powershell
kimi build tests/milestones/Milestone1.kimi
kimi run tests/milestones/Milestone1.kimi
```

`run` builds a source input automatically; `run --no-build` requires an existing build.
See [input resolution](../../docs/impl/20-compilation-configuration.md#2031-input-resolution-and-implicit-projects).
Replace `1` with the desired number. Programs with a pending stage cannot yet run.

From a source checkout, select the related verification explicitly:

```powershell
./scripts/verify.ps1 -Class MilestoneSourcesTest -Milestone 1
./scripts/verify.ps1 -Mode Session -Milestone ((1..38) + 40 + 44)
```

The second command selects all 40 existing harnesses, including the executable targets
38 and 40 whose broader implementation work remains IN_PROGRESS. There are no harnesses
for 39, 41, 42 or 43 yet. Release is the default compiler configuration; Debug is opt-in.

Each `test-milestone<N>.ps1` uses the shared original-source harness: one build and one direct
execution at each of O0/O2, preserving the source bytes and filename. It checks exact UTF-8
stdout, empty stderr and exit 0 and records source/compiler hashes, configuration, toolchain
verification status and timings. Historical variant counts describe older harness versions.
NativeAOT is separate and was not requested.

Feature legality, failure paths and cost guarantees belong to focused tests:

| Contract | Representative tests |
| --- | --- |
| Startup, scalars and control flow | `MinimalEmissionTest`, `ScalarEmissionTest`, `ControlFlowConformanceTest` |
| Ownership, Origins and closures | `OwnershipAnalysisTest`, `OwnershipJoinTest`, `OriginRedesignTest`, `ConcreteClosureTest` |
| Generics and Contracts | `ContractBindingTest`, `SpecializationBindingTest`, `AssociatedOriginBindingTest` |
| Properties, collections and iteration | `PropertyCompletionBindingTest`, `IndexableContractTest`, `IterationAdapterTest`, `ComparisonContractTest`, `WorkloadCostTest` |
| Formatting and output failure | `Utf8ConsoleTest`, `Utf8IntegrationTest`, `Utf8InterpolationTest`, `Utf8TryWriteTest` |
| CLI and installation | `test-cli.ps1`, `NativeToolchainTest`, `Kernel32ImportsTest` |

A program has one main subject and only the interactions needed to observe it; integration
programs combine established subjects. Invalid inputs and Abort probes stay in dedicated tests.
Correct stdout alone does not prove ownership legality, ABI, allocation or complexity guarantees.
The [SPEC](../../docs/SPEC.md) and [IMPL](../../docs/IMPL.md) remain the authority for those contracts.

## Source corrections

Renumbering preserves each program body except these authoring corrections:

- Program 11 describes concrete instantiation and explicit specialization, removing obsolete
  shared-generation/automatic-specialization claims (IMPL §21.3.1).
- Program 36 (former 24) calls `Parcel.intoResult` a consuming function in its Abort/output text;
  it is not a getter (SPEC §11.3). Its harness and expected output use the same wording.
- Programs 24 and 37 (former 32 and 43) now have numbered introductory comments.

Specification respellings:

- Programs 27 and 28 (2026-10-16, Place result removal, SPEC §7.1 and §4.7.1): `index` and `indexUniq` return
  `ref/T during self` and `uniq/T during self` and borrow explicitly (`self.first@ref`, `self.items[key]@uniq`), and
  Program 27's `firstPlace` returns `items[0]@ref`. The uses, including `firstPlace(counts) == 42` (a comparison through
  the reference), and the expected output are unchanged.
- Program 27 (2026-10-17, S1 and design A3's loss P4, SPEC §15.6 and §15.6.4; owner decision O3, DECISIONS R4-M64):
  `references.sliceUniq().slice().toArray()` became `references.slice().toArray()`. Under SPEC 15.6 the temporary
  UniqSlice's holder carries its own exclusive Loan on `references` beside the elements' Loans, and a read through the
  Slice carries both, so the copy kept that Loan and `references = [copied]` conflicted. The copy through a shared
  view keeps the section's intent (the outer dependency dropped, the element Origins kept); the output is unchanged.

Earlier specification respellings and authoring corrections remain in Git and PLAN_HISTORY.
No target was simplified to fit an implementation limit. In particular, the refinement, Weak,
static-storage and integrated-object targets retain their specified behavior and pending stages.

## Expected output

Every block ends with LF and expects empty stderr and exit 0. For pending programs these are
specification-derived expectations, not execution claims; consult the status table above.
The completed-source output is checked byte for byte by its native harness.

### Milestone 1

[Source](Milestone1.kimi) — Hello World, top-level startup, owned string argument.

```text
Hello, world!
```

### Milestone 2

[Source](Milestone2.kimi) — `let`/`var`, `i32`, arithmetic, `while`, `if`/`else`, `$abort`.

```text
Sum is 55.
Done.
```

### Milestone 3

[Source](Milestone3.kimi) — Explicit `main`, function calls, arguments/results, `return`, `defer`.

```text
Leaving sumTo.
Sum is 55.
Leaving main.
```

### Milestone 4

[Source](Milestone4.kimi) — `struct`, `init`, fields, whole-value Move, owned parameters, `drop`.

```text
Counter created.
Sum is 55.
Leaving finish.
Counter destroyed.
Done.
```

### Milestone 5

[Source](Milestone5.kimi) — `uniq`/`ref`, returned borrow, `during`, scope and destruction lifetimes.

```text
Borrowed sum is 55.
View destroyed; counter is still 55.
Final value is 56.
Counter destroyed.
Done.
```

### Milestone 6

[Source](Milestone6.kimi) — Value-producing `loop`, guarded `match`, `continue`, named `exit`, `yield`, `require`.

```text
Selected 7.
Control flow passed.
```

### Milestone 7

[Source](Milestone7.kimi) — Fixed-array inference, borrowed `noinit` writes, nested `for`, cross-loop transfers, Slice reads.

```text
Row finished.
Row finished.
Row finished.
Matrix total is 42.
Borrowed row total is 20.
Inferred arrays and noinit writes verified.
```

### Milestone 8

[Source](Milestone8.kimi) — Groups nested in a struct, generic struct/function, generic Copy/Move acquisition.

```text
Chosen item.
Boxed array total is 12.
Generic scope finished.
```

### Milestone 9

[Source](Milestone9.kimi) — Length/type parameters, Copy constraint, callbacks/capture, generic enum, borrowed storage.

```text
Found 6 at index 3.
Missing value handled.
Search finished.
Batch destroyed.
```

### Milestone 10

[Source](Milestone10.kimi) — Generic enum/Tuple patterns, guards, value-producing loop/match/if, cross-loop transfers.

```text
Accepted total is 12.
Pattern run finished.
```

### Milestone 11

[Source](Milestone11.kimi) — Generic instantiations, immutable static member, forwarding and explicit specialization selection.

```text
Generic weights are 6, 3, 2.
```

### Milestone 12

[Source](Milestone12.kimi) — Mutable/nested/Move captures, exclusive Callable, function values and shared calls with an owned capture.

```text
Stateful result is 13.
Nested capture result is 18.
Captured message.
Captured message.
Closure run finished.
```

### Milestone 13

[Source](Milestone13.kimi) — Generic Iterator conformance, Slice storage, external Origins, retained element borrows.

```text
Even sample total is 6; first is still 1.
Iterator remains exhausted.
Middle slice total is 5.
```

### Milestone 14

[Source](Milestone14.kimi) — Generic Slice pipeline, Iterator, exclusive borrowed capture, obj creation/Move/destruction.

```text
Accepted three jobs.
Accumulator destroyed.
Object total is 12.
Accumulator destroyed.
Pipeline finished.
```

### Milestone 15

[Source](Milestone15.kimi) — Initialization/Move/Loan joins, loop backedges, repair before continue.

```text
Iteration finished.
Item destroyed.
Iteration finished.
Iteration finished.
Flow checked.
Item destroyed.
Item destroyed.
Iteration finished.
Item destroyed.
Iteration finished.
Iteration finished.
Flow checked.
Item destroyed.
Item destroyed.
Ownership joins finished.
```

### Milestone 16

[Source](Milestone16.kimi) — Multiple external Origins, aggregate/enum forwarding, intersection, exclusive reborrow.

```text
External borrow survived.
Both sources updated.
External borrow survived.
Both sources updated.
Origin forwarding finished.
```

### Milestone 17

[Source](Milestone17.kimi) — Partial Move repair, element exchange/swap/replace, secured results and destruction order.

```text
Exchange scope finished.
Resource 2 destroyed.
Resource 3 destroyed.
Prepared result.
Resource 1 destroyed.
Result received.
Resource 5 destroyed.
Resource 4 destroyed.
Cleanup finished.
```

### Milestone 18

[Source](Milestone18.kimi) — Generic composite/Non-Copy arguments, results, temporaries, Copy acquisition and destruction.

```text
Resource 4 destroyed.
Resource 3 destroyed.
Generic result secured.
Resources received.
Resource 2 destroyed.
Resource 1 destroyed.
Generic result secured.
Tuple received.
Generic result secured.
Generic values finished.
```

### Milestone 19

[Source](Milestone19.kimi) — Concrete generic entries, compound layouts, specialization forwarding and distinct destruction operations.

```text
Compound layouts preserved.
Specialization preserved.
Red received.
Red destroyed.
Blue received.
Blue destroyed.
Generic generation finished.
```

### Milestone 20

[Source](Milestone20.kimi) — Contract requirements, associated Types/equalities, conditional and nested conformance.

```text
Associated numbers are 21, 21.
Associated flags are true, true.
Contract forwarding finished.
```

### Milestone 21

[Source](Milestone21.kimi) — Type/length/Origin inference, ordinary defaults and generic forwarding.

```text
Default index evaluated.
Inferred selection is 10.
Explicit selection is 30.
Default index evaluated.
Forwarded selection is 10.
Default index evaluated.
Ordinary selection is 4.
Default index evaluated.
Local selection is 7.
Inference and defaults finished.
```

### Milestone 22

[Source](Milestone22.kimi) — Full length/Type specialization, inherited defaults/Origins and preserved implementation selection.

```text
${default}Specialized selection is 30.
Explicit selection is 10.
${default}Forwarded selection is 30.
${default}Ordinary selection is 4.
${default}Local selection is 9.
Specialization finished.
```

### Milestone 23

[Source](Milestone23.kimi) — Dynamic `Array<T>` reserve/append/insert/remove/pop/clear, indexed replacement of Non-Copy elements, owning iteration with early exit.

```text
No tasks.
Many tasks.
Removed the last task.
Task 1 destroyed.
Popped a task.
Task 3 destroyed.
Two tasks.
Task 6 destroyed.
Cleared the spare tasks.
Iterating task 5.
Task 5 destroyed.
Task 2 destroyed.
Array run finished.
Task 4 destroyed.
```

### Milestone 24

[Source](Milestone24.kimi) — User/generic UTF-8 formatting, independent owning strings, short-circuit writes, fixed-buffer reuse and bounded Console interpolation.

```text
source retained
reading destroyed
value=Reading(42)
君: value=Reading(42)
done=true
My number is 42
```

### Milestone 25

[Source](Milestone25.kimi) — User/generic comparison Contracts, borrow/Tuple composition, retained witnesses and NaN-reflexive equality through specialization.

```text
User comparisons keep their operands.
Tuple comparisons compose witnesses.
Floating Contract equality is NaN-reflexive.
Comparison contracts finished.
```

### Milestone 26

[Source](Milestone26.kimi) — Standard/custom/computed Copy Properties, direct storage and assignment evaluation order.

```text
Standard access finished.
Input evaluated.
Receiver evaluated.
Custom set.
Custom get.
Computed set.
Computed get.
Copy properties finished.
```

### Milestone 27

[Source](Milestone27.kimi) — Saved bounds, shared/exclusive child views, Move, Slice.toArray, `noinit` view writes, backing/element Origins, and user Places.

```text
Start evaluated.
End evaluated.
Nested views retain 20 and 30.
End boundary is not an element.
Short target rejected.
Last text.
Last text.
Slice Origins preserved.
Pair second.
Pair replaced.
Exclusive views and independent copies verified.
```

### Milestone 28

[Source](Milestone28.kimi) — Semantics-generic follow and reference-valued collection elements.

```text
Owned element updated to 5.
Borrowed elements total 30.
Exclusive referent updated to 33.
Totals are 6, 30 and 73.
Semantics-generic follow finished.
```

### Milestone 29

[Source](Milestone29.kimi) — User IntoIterable/Iterator mappings, owned elements, continue/early-exit cleanup and retained external element borrows.

```text
Owned iterator acquired.
Continue after item 1.
Item 1 destroyed.
Exit after item 2.
Item 2 destroyed.
Item 4 destroyed.
Item 3 destroyed.
Owned iteration finished.
Borrowed iterator acquired.
External first is 10; remaining total is 50.
Borrowed iterator acquired.
General iteration finished.
```

### Milestone 30

[Source](Milestone30.kimi) — Positions, ranges, resolution, iteration and formatting.

```text
First 12, last 50.
Inner 1..^1 has 4; tail 44 and 50; head 3.
Span 2..^1 resolves to 2..5, sum 100.
1..=4 covers 4
^8.. does not resolve
The end boundary is not an element.
A closed end past the length is refused.
Queue 4 2 3 1 after removing 9.
Closed ranges reach 255 without overflow.
```

### Milestone 31

[Source](Milestone31.kimi) — Wrapping integers, integer results, conversions and generic arithmetic.

```text
Wrapped 4, -128, 0, -128, -128.
Half is 1069547520 in bits; 300 wraps to 44; 4 leaves the wrapping Type.
Literals 3, 9007199254740993, 100000000000000000000000000000000000000.
Hashes 1093368299 and 2595871832.
Draws 0 98 46 35; checksums 10 and 15781573103004042447.
Counter wrapped to 4, a key seen 3 times.
```

### Milestone 32

[Source](Milestone32.kimi) — Dictionary duplicate rejection, retained equal keys, replacement/removal, insertion-order iteration and exact entry cleanup.

```text
Duplicate returned both inputs.
Rejected item destroyed.
Rejected key destroyed.
Replacement key destroyed.
Replacement returned item 10.
Item 10 destroyed.
Item 11 destroyed.
Stored keys and insertion order preserved.
Removed the original key and item.
Item 12 destroyed.
Key 1 destroyed.
Owning iteration starts at key 2.
Item 20 destroyed.
Key 2 destroyed.
Item 30 destroyed.
Key 3 destroyed.
Item 40 destroyed.
Key 4 destroyed.
Dictionary run finished.
```

### Milestone 33

[Source](Milestone33.kimi) — Disjoint exclusive Array and fixed-array element access.

```text
Balances 20, 25, 25.
Closed Chi.
Closed Aki.
Eve holds 20.
Ben holds 25.
Dan holds 25.
Fay holds 5.
Settled 10 30 20 40 after 3 exchanges.
Positions ^1 and 3 name one element.
Limits 0 and 19.
Disjoint element access finished.
Closed Fay.
Closed Dan.
Closed Ben.
Closed Eve.
```

### Milestone 34

[Source](Milestone34.kimi) — Generic compound captures, external borrowed captures, shared/exclusive/consuming Callable and owning function-value erasure.

```text
Packet destroyed.
Generic capture total is 20.
Compound capture advanced to 8.
Consumed packet.
Packet destroyed.
Erased calls finished.
Packet destroyed.
General closures finished.
```

### Milestone 35

[Source](Milestone35.kimi) — Integrated collection, borrow, iteration and closure workload.

```text
No orders to count.
Large orders: 3 overall, 2 in the tail.
First order over 40 is 3.
Running total is 155.
Category 10 totals 75.
Category 20 totals 20.
Category 30 totals 60.
Stopped at order 4.
Removed order 1.
Order 1 destroyed.
Processing finished.
Order 5 destroyed.
Order 4 destroyed.
Order 3 destroyed.
Order 2 destroyed.
```

### Milestone 36

[Source](Milestone36.kimi) — Non-Copy setter replacement, a borrowed getter, a consuming function and a standard-operation Contract witness.

```text
Setter entered.
Resource 1 destroyed.
Borrowed resource is 2.
Borrowed resource is 2.
Holder scope finished.
Resource 2 destroyed.
Consuming function received.
Resource 3 destroyed.
Ownership properties finished.
```

### Milestone 37

[Source](Milestone37.kimi) — User arithmetic Contracts, Function values, element replacement and Result propagation.

```text
Shared operators and function values: 4, 6.
One located element replaced: 4, 6.
Explicit Result propagation: 42, error 7.
```

### Milestone 38

[Source](Milestone38.kimi) — Inline base construction, inherited standard Properties and Type members, whole-derived Move and layered destruction.

```text
Base field initialized.
Base constructor finished.
Derived field initialized.
Derived constructor finished.
Inherited access finished.
Derived value moved.
Derived drop.
Derived resource destroyed.
Base drop.
Base resource destroyed.
Inheritance finished.
```

### Milestone 39

[Source](Milestone39.kimi) — Exclusive objects, base views, struct Type tests/refinement, complete Sealed payload exchange and dynamic layered destruction.

```text
Base view refined to Leaf.
Type-test operand evaluated.
Complete payload replaced.
Leaf drop.
Original resource destroyed.
Base drop.
Updated object remains a Leaf.
Owning base view retains the complete Leaf.
Leaf drop.
Replacement resource destroyed.
Base drop.
Exclusive object run finished.
```

### Milestone 40

[Source](Milestone40.kimi) — rc/arc creation, clone, shared access and final strong release.

```text
Handle reads payload 1.
Cloned handle shares payload 1.
Released one strong handle.
Payload 1 destroyed.
Arc views read payload 2.
Payload 2 destroyed.
Borrowed counter is 5.
Shared ownership finished.
```

### Milestone 41

[Source](Milestone41.kimi) — Weak downgrade/upgrade, expiration and cyclic construction.

```text
Upgrade succeeded while alive.
Item destroyed.
Upgrade failed after the final release.
Weak clone stays expired.
Upgrade fails while Building.
Self weak upgrades after publication.
Weak run finished.
Node destroyed.
```

### Milestone 42

[Source](Milestone42.kimi) — Static initialization, generic storage keys and shutdown.

```text
Main started.
Count initialized.
Count is 11.
Resource 1 initialized.
Primary is 1.
Resource 2 initialized.
Static resource 2 destroyed.
Replaced the second resource.
Generic keys are distinct.
Main finished.
Static resource 3 destroyed.
Static resource 1 destroyed.
```

### Milestone 43

[Source](Milestone43.kimi) — Integrated Properties, inheritance, objects, static state and formatting.

```text
desk (40 W, level 30)
Lamp desk destroyed.
Device desk released.
shelf (25 W, level 0)
Lamp shelf destroyed.
Device shelf released.
door at level 50
Device door released.
Registry starts empty.
hall (60 W, level 0)
Hall lamp still owned once.
Lamp hall destroyed.
Device hall released.
Registry entry expired.
Application finished.
```

### Milestone 44

[Source](Milestone44.kimi) — Virtual slots, overrides, direct base calls and dispatch by Dynamic Type through views, Items, erased values and handles.

```text
Device 1 draws 1 W.
Lamp 2 draws 40 W.
Lamp 3 draws 45 W.
Fan 4 draws 2 W.
Static slots, dynamic implementations.
Device 4 released.
Device 3 released.
Device 2 released.
Device 1 released.
Shared handles dispatch on their payload.
Device 6 released.
Device 5 released.
Virtual dispatch finished.
```
