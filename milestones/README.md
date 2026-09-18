# Language milestones

Thirty-eight independent programs are planned from the current [SPEC](../SPEC.md).
Programs 1–21 have source files; programs 22–38 have design and verification scopes.
They are staged compiler implementation targets. Execution evidence and support
boundaries are recorded in [STATUS.md](../STATUS.md); expected output alone is
not an execution claim. Milestones 15–21 are specification targets beyond current
verified executable coverage; the status table below distinguishes untested
programs from attempted builds that failed.
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
| [Milestone15](Milestone15.kimi) | Initialization/Move/Loan joins, loop backedges, repair before continue |
| [Milestone16](Milestone16.kimi) | Multiple external Origins, aggregate/enum forwarding, intersection, exclusive reborrow |
| [Milestone17](Milestone17.kimi) | Partial Move repair, element exchange/swap/replace, secured results and destruction order |
| [Milestone18](Milestone18.kimi) | Generic composite/Non-Copy arguments, results, temporaries, Copy acquisition and destruction |
| [Milestone19](Milestone19.kimi) | Contract requirements, associated Types/equalities, conditional and nested conformance |
| [Milestone20](Milestone20.kimi) | Type/length/Origin inference, ordinary defaults and generic forwarding |
| [Milestone21](Milestone21.kimi) | Full length/Type specialization, inherited defaults/Origins and preserved implementation selection |

## Program status

As of **2026-09-18**, after the 38-program restructuring. Build means a native
Application build including LLVM verification and linking; tests mean native
output/exit checks and, where a harness exists, its variants/rejections. Parser
coverage alone is not a native test. NOT_RUN is neither a pass nor a failure.

| Program | Created | Build | Tests | Evidence / boundary |
| --- | --- | --- | --- | --- |
| 1 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 2 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 3 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 4 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 5 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 6 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 7 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 8 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 9 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 10 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 11 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 12 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 13 | YES | PASS (Release) | PASS (Release) | Exact copied source, O0/O2 output/exit checks |
| 14 | YES | PASS (Release) | PASS (Release) | Existing target/variant/rejection harness, O0/O2 |
| 15 | YES | FAIL (Release/O2) | NOT_RUN | TypeMismatch_Kd / ControlFlow_Kd at the joined borrow |
| 16 | YES | FAIL (Release/O2) | NOT_RUN | UnsupportedOwnership_Kd and dependent Loan/initialization diagnostics |
| 17 | YES | FAIL (Release/O2) | NOT_RUN | UnsupportedOwnership_Kd for element update targets; Move/Loan diagnostics |
| 18 | YES | FAIL (Release/O2) | NOT_RUN | GenerationFailed_Kd: invalid shared storage/projection |
| 19 | YES | FAIL (Release/O2) | NOT_RUN | InvalidPattern_Kd / UnprovenConstraint_Kd for associated results/nested conformance |
| 20 | YES | FAIL (Release/O2) | NOT_RUN | UnsupportedBinding_Kd when dereferencing generic returned element borrows |
| 21 | YES | FAIL (Release/O2) | NOT_RUN | Length specialization / inherited Origin Binding unsupported; cascading diagnostics |
| 22 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 23 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 24 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 25 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 26 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 27 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 28 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 29 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 30 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 31 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 32 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 33 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 34 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 35 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 36 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 37 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |
| 38 | NO (planned) | NOT_RUN | NOT_RUN | Scope assigned in the future-verification table |

[Restructuring audit](../PLAN_HISTORY.md#programs38-restructure) records source/DLL
identities and exact commands: Release compiler/test-project build PASS with zero
warnings/errors; 57 alias/syntax tests PASS; 577 checks across the existing
program 1–12/14 harnesses PASS; program 13 passes two native O0/O2 executions.
The syntax-catalog test includes all 21 existing milestone sources. Programs
15–21 fail before native execution; their expected output remains specification-derived.
Programs 22–38 have no source files or executed tests yet. Debug, full managed
regressions and NativeAOT were not run for this restructuring.

Earlier [program 14 regressions](../PLAN_HISTORY.md#program14-completion),
[units 62–67 audit](../PLAN_HISTORY.md#units62-67-verification) and
[18–20 authoring probes](../PLAN_HISTORY.md#programs18-20-design) remain historical
records of their own inputs. In particular, the old combined program-20 result
is not evidence for new program 20 or 21. The current table uses the new audit.

Each file is a separate Application; do not combine them into one project.
Under [single-source input rules](../spec/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects),
the intended build/run commands, once the required features are implemented, are:

```powershell
kimi build milestones/Milestone1.kimi
kimi run milestones/Milestone1.kimi
```

Replace `1` with the milestone number. `run` executes an existing build; it does
not compile source. No separate project file is needed. Output uses fixed string
literals so programs 1–21 do not require numeric formatting or interpolation.
Programs 2–21 use `Console.writeLine` through the default Kimi alias. Only the
Hello World program keeps `::Kimi.Console.writeLine`; no extra alias is needed.

## Roadmap from program 15 to core completion

The current plan has **38 programs**, including **24 programs numbered 15–38**.
Programs 15–21 are concrete below; 22–38 are future source targets, not implemented
capabilities. This count is a decomposition of scope, not an effort or delivery
estimate. Passing programs 13/14 does not
establish general Slice, Iterator, callable, or object support.

Here, core completion means combining the finalized language features and basic
collections in a useful single Application. Full Package distribution, Mod host
integration, the test runner, and comprehensive FFI/platform integration are
separate product work. Unintroduced/deferred features in
[Appendix D](../spec/appendices/D-deferred-features.md) are excluded, including
mutable-element Slice, lending iterators, virtual/override members and runtime
Contract Views. "Complete generics" means the adopted specification, not future
generic forms. ObjectCallCompatible inference/publication and release checking
remain subject to the [explicit deferral](../spec/appendices/D-deferred-features.md#objectcallcompatible).

| Program | Main subject | Intended coverage |
| --- | --- | --- |
| 15 | Ownership and control-flow joins | Initialization, Move and Loans across branches, backedges and continue |
| 16 | General Origins | Multiple Origins, returned references, reborrowing and aggregate/enum dependencies |
| 17 | Ownership, cleanup and updates | Partial Move, repair, defer/deinit, whole-value updates and call effects |
| 18 | Generic value operations | Composite and Non-Copy arguments, results, temporaries and destruction |
| 19 | Contracts and associated Types | Constraints, conditional conformance, associated Types and requirement calls |
| 20 | Generic inference and defaults | Type/length/Origin inference, ordinary optional arguments and forwarding; no specialization |
| 21 | Explicit full specialization | Closed Type/length selection, inherited default/Origin contracts and generic forwarding |
| 22 | Generic generation | Shared bodies, compound ABI, fixed frames and finite generation limits; separate internal checks |
| 23 | Basic Properties | Standard/custom/computed access over Copy values, permissions and evaluation order |
| 24 | Ownership-bearing Properties | Non-Copy setters, owned/borrowed getter results, temporary lifetimes and Contract witnesses |
| 25 | Inheritance | Base storage, construction/destruction, inherited members and already-verified Property operations |
| 26 | General closures and Callable | Composite/generic captures, dependency retention, function values and permitted erasure |
| 27 | General Slice/Index/Range | Partial/nested slices, bounds evaluation, permitted element Types and retained Origins |
| 28 | General Iterator/Iterable | User Iterable protocols, owned/borrowed elements, early exit and remaining-element cleanup |
| 29 | Dynamic Array | Capacity, insertion/removal/replacement, Non-Copy elements and owning iteration |
| 30 | Comparison Contracts | Equatable/Comparable, generic requirement calls and composed comparisons before Dictionary |
| 31 | Dictionary | Key equality, mutation, insertion order, iteration, Loans and allocation/complexity requirements |
| 32 | Stringify and interpolation | User/generic stringification, evaluation order, temporary cleanup and independent owned results |
| 33 | Exclusive objects and views | obj, base views, runtime struct tests/refinement, identity and dynamic destruction |
| 34 | Shared object ownership | rc/arc creation, explicit clone, Move, shared access and final strong release |
| 35 | Weak | Downgrade/upgrade, expiration, cyclic construction and final weak-table release |
| 36 | General static storage | First initialization, effects, cycles, shutdown and inherited generic environment keys |
| 37 | Integrated processing application | Collections, borrows, iteration and closures in one realistic workload |
| 38 | Integrated core application | Properties, inheritance, objects and formatting combined with established features |

### Number migration from the 34-program plan

Programs 1–19 retain their numbers and subjects. Former program 20 is split into
20 (inference/defaults) and 21 (specialization); both source files are independent.
Earlier program-20 build evidence describes the combined source, not either new
target. Remaining changes affect planned scopes only:

| Former number | Current number(s) | Change |
| --- | --- | --- |
| 21 | 22 | Generic generation |
| 22 | 23, 24 | Separate basic access from ownership/borrow/witness behavior |
| 23–27 | 25–29 | Inheritance, closures, Slice, Iterator, Array |
| 28 | 31 | Dictionary follows comparison Contracts |
| 29 | 30, 32 | Separate comparison from Stringify/interpolation |
| 30 | 33, 34 | Separate exclusive objects/views from shared ownership |
| 31–34 | 35–38 | Weak, static storage and the two integration targets |

The dependency direction is ownership/Origins, then generic foundations, then
general member/call/sequence operations, collections and runtime integration.
Implement prerequisites needed by a target even if their broader program comes
later; the ordering does not postpone correctness checks. Revisit generality
after each group rather than accumulating program-specific special cases.

Keep one main subject and one or two interactions per program. Existing programs
are intentionally short; there is no minimum line count. Split independent
mechanisms rather than compressing them into dense expressions. Do not pad a
program to 80–180 lines or put every negative/performance case in its main body.
Integration programs may be larger. Programs remain independent Applications.

Each implementation target needs three kinds of evidence:

- **Successful execution:** exact output, exit status, values, evaluation order
  and destruction responsibilities, with LLVM verification and native O0/O2
  execution on the specified Windows x64 profile.
- **Required rejection:** separate variants for invalid Moves, escaping Origins,
  conflicting Loans and failed constraints. Do not edit the canonical program
  to run a variant; reject invalid sources before artifact publication.
- **Implementation contracts:** inspect shared generation/ABI and measure the
  relevant allocation, complexity and resource bounds. Correct stdout alone
  cannot prove these requirements. No NativeAOT verification is implied.

Map evidence to the owning specification clauses. A representative program's
success closes only its exercised slice, not an entire feature family. The
programs and the exercises below define future targets; they are not an automated
conformance suite. Companion fixtures and verification scripts belong to the
subsequent implementation work.

This README owns program design and expected behavior. [PLAN.md](../PLAN.md)
continues to own active execution scope, acceptance tracking, dependencies,
states and exact next actions; [PLAN_HISTORY.md](../PLAN_HISTORY.md) owns run
history. This roadmap does not replace the active target or mark any work done.

### Verification scopes for future programs 22–38

Every row inherits the three evidence gates above. The canonical program is a
small successful Application. Rejection/Abort cases use separate source copies;
IR, allocation, runtime-state and complexity checks use companion tests or tools.
Prerequisites name the main exercised capabilities, not permission to postpone
required legality checks. Where public operations use static-effect summaries,
verify those summaries before accepting calls even though the static-storage
demonstration is program 36. ObjectCallCompatible's deferred stages stay deferred.

| Program / main prerequisites | Canonical program | Separate semantic checks | Implementation evidence |
| --- | --- | --- | --- |
| 22 / 18–21 | Pass compound generic values through shared bodies and concrete entries; preserve different Type operations and selected specializations. | Different layouts and same-layout/different-destructor Types; finite recursive metadata, growing substitutions, invalid infinite layout; required-limit diagnostics versus optional-budget fallback. | Inspect entry/context ABI, operation dispatch, fixed scratch-frame reservations and reuse; compare budget-zero/bounded optimization semantics; measure warm allocations and deterministic logical plans. |
| 23 / 4, 7 | Read/write stored standard, custom and computed Copy Properties with observable evaluation order. | Access permissions, differing setter inputs where permitted, single receiver/RHS/getter evaluation; reject writes or exclusive borrows into getter-result temporaries. | Distinguish direct Places from accessor calls; verify standard storage access and custom dispatch without invented get/set round trips. |
| 24 / 16–19, 23 | Replace a Non-Copy value through a setter and return a borrowed view through a getter; use a Contract Property requirement. | Owned getter results and legal receiver consumption, discarded setter inputs, temporary-borrow escape, conflicting Loans, invalid shared extraction and incompatible requirement operations. | Exact old/input/result destruction, getter-temporary lifetime, standard-operation witness identity and permitted bridges; no hidden Copy or storage exposure through a Contract. |
| 25 / 17, 23–24 | Construct a derived value, access inherited members/Properties and destroy complete derived/base storage. | Base initialization order/completeness, inherited access, prohibited redeclarations and invalid Partial Moves; separate early-transfer/Abort construction cases. | Base offsets and declaring-receiver projection, stable member mappings, one construction/destruction responsibility per layer. |
| 26 / 12, 14, 16, 18–22 | Capture a compound/generic value and an external borrow, then invoke through the required Callable mode; separately demonstrate permitted function-value erasure. | Shared/exclusive/consuming calls, nested captures, function items, moved closures, escaping dependencies and erasure without required Copy/Owned evidence. | Environment layout, direct versus common entries, capture destruction, no per-call environment allocation; optional erasure allocation accounted separately. |
| 27 / 7, 13, 16 | Resolve Index/Range values and retain nested/sub-Slice views of external backing storage. | Empty/full/from-end bounds, one-time bound evaluation and Abort order; reject conflicting mutation, escaping views and Non-Copy indexed acquisition. | O(1) views/metadata, no element copying or Slice backing allocation, full nested-Type/Origin/Loan preservation. Mutable-element Slice remains excluded. |
| 28 / 13, 18–19, 27 | Implement user Iterable/Iterator protocols, yield owned or externally borrowed elements, then stop early. | Exhaustion, continue/exit/return, correct associated Element/Iterator equality, retained previous borrowed results; reject lending results and missing capability proofs. | Receiver acquisition once, exact yielded/unyielded responsibilities and reverse remaining-element cleanup; no hidden element clone. |
| 29 / 17–18, 27–28 | Grow, insert, replace and remove Non-Copy Array elements; consume an iterator and stop early. | Empty/pop/clear, directional indices, capacity/no-op paths, live and empty-Slice conflicts, retained borrowed contents, normal argument abandonment and Abort. | Count internal allocations; verify within-capacity/no-op/removal guarantees, reverse current-index cleanup, growth amortization and shrink failure preserving original placement. |
| 30 / 19, 21 | Compare user Types through Equatable/Comparable and generic calls, including composed Tuple/borrow comparisons. | Missing/incompatible conformance, equality/order agreement, operand order and no Non-Copy consumption; built-in floating comparison versus NaN-reflexive Equatable mapping. | Retained requirement mappings and specialization preserving comparison meaning; no pointer-identity substitute or synthesized user equality. |
| 31 / 19, 28–30 | Insert/reject/replace/remove Dictionary entries with user-defined equal keys; inspect and iterate insertion order. | Result/Option ownership, duplicate literal diagnostics and runtime duplicates, stored-key preservation, missing-key assignment Abort, lookup/mutation Loans and dependency retention. | Equality effects and invocation order, value-before-key/reverse-insertion cleanup, allocation-free duplicate/lookup/replacement paths, churn reuse and specified management bounds. No public Hash requirement is added. |
| 32 / 18–19, 26 | Interpolate user/generic values through Stringify, retaining the original Non-Copy values and independent resulting strings. | UTF-8/NUL/empty text, source-order evaluation, once-only stringification, temporary cleanup, missing conformance and Abort before later interpolation. | Verified Stringify calls, ownership of each produced string and cleanup, no retained source Loan in the combined result. Do not invent formatting options or deferred concatenation semantics. |
| 33 / 14, 24–25 | Create an obj, use a base object view, perform specified struct `is` tests/refinement, preserve identity and destroy the complete Dynamic Type. | Sealed payload projection, borrow/reborrow, legal whole-payload updates, invalid view/acquisition/escape; test expression effects and refinement invalidation. | Header/view identity, dynamic destruction before original storage release, unchanged identity across updates. No runtime Contract View, checked-cast spelling or deferred ObjectCallCompatible inference. |
| 34 / 33 | Create rc and arc values, explicitly clone strong handles, Move them and observe final strong release. | Shared-only access even at count one, no implicit clone, moved-handle rejection, external payload dependencies and separate count-overflow failure probes. | Exact retain/release counts, clone without allocation/payload copy, complete payload destruction once; inspect atomic arc ordering with internal tests. No source concurrency or obj/rc/arc conversion is added. |
| 35 / 26, 34 | Downgrade, upgrade and expire Weak handles; then demonstrate a cyclic factory's Building-to-Alive transition. | Weak clone/Move, upgrade before publication and after final release, payload dependencies, factory Owned/Callable constraints and failed construction. | Separate payload/object/table lifetimes, allocation-free upgrade/clone, final table release; internally test arc upgrade/final-release races without introducing source threading. |
| 36 / 19, 22, 24–25, 34 | Initialize static values on first access, distinguish enclosing generic keys and destroy in reverse successful-initialization order. | First write before replacement, unused storage, alias paths to one key, effects/reentry, non-Owned storage rejection; initialization cycles and invalid shutdown access in separate Abort inputs. | Per-key state/address/destruction identity, preserved keys across body sharing and Origin erasure, no initialization from untaken paths or effect summaries alone. |
| 37 / 26–32 | One bounded processing workload using collections, borrowed views, iteration and closures with exact results and cleanup. | Empty input, alternate values, early stop, rejection/Abort paths and representative Loan violations derived from the workload. | Workload allocation/complexity observations and regressions of prerequisites; no new language mechanism introduced to make the application work. |
| 38 / 24–25, 32–36; 37 as needed | A second application using Properties, inheritance, objects and formatted output with exact lifetime behavior. | Alternate object lifetimes, replacement, empty/expired states, shutdown and relevant rejected accesses. | Cross-feature identity/cleanup/ownership checks and prerequisite regressions. It need not repeat every program-37 collection operation; no new feature family is deferred to this final target. |

Owning clauses: [Properties](../spec/11-properties.md),
[generics and Contracts](../spec/08-generics-constraints-and-contracts.md),
[sequences and collections](../spec/04-arrays-indexing-and-slices.md),
[interpolation](../spec/12-expressions.md#1233-interpolation-stringification),
[comparison and objects](../spec/13-operators-and-assignment.md),
[generation/runtime representation](../spec/21-layout-runtime-and-code-generation.md),
and [startup/static storage](../spec/22-core-execution-and-foreign-functions.md).
These scopes partition work; neither source brevity nor one passing representative
program certifies the full owning chapter. Package/Mod/test-runner/FFI integration
remains separate product work, as defined above.

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

The program numbers here are independent of PLAN.md's broader M stages.

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

Verified program-14 reproduction (build the selected compiler configuration first):

```powershell
./backend/windows-x64/test-milestone14.ps1 -Configuration Release -ToolchainRoot ./toolchain
```

Debug is also supported. The script checks the unchanged target, O0/O2 copies,
input/name/limit variants and rejected ownership/call variants. It writes reports
under `bin/milestone14/<configuration>/<run-id>/` and does not run NativeAOT.
[STATUS](../STATUS.md) and [execution evidence](../PLAN_HISTORY.md#program14-completion)
record the verified source state and remaining object/generic boundaries.

## Milestone 15: ownership across control-flow joins

Both calls exercise the same function with different initialization branches.
The first and third iterations select different shared Loan sources, then resume
mutation of current after the selected reference's last use. The second iteration
Moves current into consume, repairs it, and continues. The loop must reach a
consistent usable state on both backedges; cleanup still runs on continue.

Expected stdout (not execution evidence):

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

For each call, totals are initial + 30 (40 and 42), and the repaired current ends
at 21. Exactly three Items are destroyed: the consumed original, fallback, then
the repaired current. Initialization on mutually exclusive paths must not cause
double destruction or an uninitialized read.

Separate rejection exercises:

- Remove the else initialization: the following current.value read is invalid.
- Remove repair before continue: a later iteration/final read can see Moved storage.
- Read current immediately after consume and before repair.
- Inside the selected scope, mutate current before the selected.value read;
  one incoming Loan can still refer to current.

Future verification should also reverse the selection condition with adjusted
expected totals, exercise zero iterations with adjusted final checks, and vary
the loop count to require additional backedge traversals. Check both acceptance
and rejection at O0/O2 without optimizer-dependent ownership legality.

Focus: [initialization and Move](../spec/15-ownership-and-lifetime-analysis.md#151-initialization-and-consume-analysis),
[Loan conflicts](../spec/15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts),
and [scope-exit cleanup](../spec/16-scope-exit-and-destruction.md#162-scope-exit-destruction).
This target does not alone close arbitrary CFG, divergent cleanup or effect analysis.

## Milestone 16: forwarding external Origins

Pair stores two independently named Origins. choose returns an enum whose stored
reference has their intersection, preserving both input dependencies. Moving and
matching that enum forwards the external reference through a named exit from do.
The Pair and enum Subject end before the selected reference is used; neither
wrapper's storage is its referent. Both selection paths execute.

Expected stdout (not execution evidence):

```text
External borrow survived.
Both sources updated.
External borrow survived.
Both sources updated.
Origin forwarding finished.
```

The selected values are 10 and 20 in the two calls. Once the retained borrow is
finished, add exclusively reborrows each source, then resumes access through the
parent parameter. Final values are 13 and 24. Copying the shared reference or
destroying its wrappers never copies or destroys either Cell.

Separate rejection exercises:

- Replace choose's result Origin with static: the input sources cannot prove it.
- Declare only self.left as the result Origin while keeping the right-source arm;
  no outlives relation between the two abstract Origins is declared.
- Construct first inside the result-producing do and attempt to return its borrow
  to the existing outer use.
- Mutate either source before selected's final read. The declared intersection
  does not allow dropping the other input dependency based on the selected arm.
- In add, access target between the child declaration and its final read/write.

Future verification should add named Origin arguments, explicit outlives bounds,
and a Missing-path variant handled without Abort. Those variants extend coverage;
this program alone does not certify the full Origin solver, variance, recursive
dependencies or universal-region checking.

Focus: [Origin intersections](../spec/15-ownership-and-lifetime-analysis.md#1522-ordering-and-intersection),
[abstract Origins](../spec/15-ownership-and-lifetime-analysis.md#153-abstract-origins),
[call propagation](../spec/15-ownership-and-lifetime-analysis.md#1564-calls-and-origin-propagation),
and [reborrowing](../spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing).

## Milestone 17: repair, replacement and ordered cleanup

The fixed array has no user deinit. Moving its complete Resource element is
permitted even though Resource itself has deinit; moving one of Resource's
Non-Copy fields would be a different operation. Repair restores array completeness.
Exchange transfers Resource 2 into old without destroying it, swap transfers
Resources 3/4 without destruction, and replace destroys Resource 3 at its target.
Literal indices 0 and 1 provide disjoint static paths for the simultaneous Loans.

Expected stdout (not execution evidence):

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

The returned array contains Resources 4 and 5. Return secures it before the
prepare defer and extracted's cleanup; items has no remaining destruction
responsibility. Caller cleanup destroys array elements in reverse order. Each
of the five constructed Resources is destroyed exactly once.

Separate rejection exercises:

- Remove items[0]'s repair: returning the incomplete array is forbidden.
- Read items[0] between extraction and repair, or read old after its do ends.
- Change swap's second target to items[0]: the exclusive targets overlap.
- Pass items[1] itself as exchange's later replacement argument: the established
  target Loan conflicts with acquiring that value.
- Add a defer that reads items before return: deferred execution would read the
  source after return has Moved it.

Future verification should separately cover incomplete-array cleanup without
repair/whole return (only remaining initialized elements are destroyed), cleanup
through early transfer, and Abort during replacement destruction (no subsequent
placement or ordinary cleanup). Resource ids and exact output detect duplicate,
missing or reordered destruction; inspect generated updates to exclude extra
element allocations. Borrowed-content updates, generic storage and general
interprocedural effects remain broader requirements.

Focus: [partial Move](../spec/15-ownership-and-lifetime-analysis.md#1513-move-paths-and-partial-move),
[whole-value updates](../spec/15-ownership-and-lifetime-analysis.md#157-whole-value-updates),
[secured results](../spec/16-scope-exit-and-destruction.md#1622-results-and-transfers),
and [aggregate cleanup](../spec/16-scope-exit-and-destruction.md#1632-field-cleanup).

## Milestone 18: generic composite values and destruction

`choose` accepts two temporary Boxes containing Non-Copy Resource arrays. It
secures the selected result before destroying the unselected parameter. `take`,
`relay` and `package` then forward the selected array through a generic field,
local, result and enum payload. None of these transfers duplicates ownership.
The same `relay` also accepts a nested owned-string Tuple and a Copy array; the
Copy source remains usable. No generic function assumes that arbitrary T is Copy.

Expected stdout (not execution evidence):

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

Resources 3/4 are destroyed inside choose, and 1/2 after the receiving match arm.
Each array destroys in reverse element order. The relay defer executes after
securing its result; its moved local/parameter must not destroy the result again.

Separate rejection exercises:

- Use selected after selected.take(), or delivered after its consuming match.
- Return `(value, value)` from an unconstrained generic duplicate function:
  selected Copy instantiations cannot justify a universally invalid definition.
- Add a Box deinit while retaining extraction of its Non-Copy field.
- Add a deferred read of pending in relay: the return may Move that storage.

Future verification should select the second Box with adjusted ids/output, route
the owned array through an early return, and exercise unused Delivery cleanup.
Inspect compound result storage, per-Type Copy/Move operations and destruction
dispatch in addition to stdout; shared ABI/frame/resource guarantees belong to
program 22's broader acceptance criteria.

Focus: [generic body checking](../spec/08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations),
[generic generation](../spec/21-layout-runtime-and-code-generation.md#213-generic-code-generation),
and [secured results and cleanup](../spec/16-scope-exit-and-destruction.md#1622-results-and-transfers).

## Milestone 19: Contracts, associated Types and conditional conformance

Source requires a Copy Core Element and a shared read operation. NumberSource
and FlagSource explicitly bind that associated Type. Wrapper has unconditional
storage but fulfills Source only when its stored T does. The conditional block
forwards both the associated identity and the verified requirement call.
readTwice returns two associated values; readNumber additionally requires their
Type to equal i32. Nested Wrappers must compose the same evidence. Wrapper<i32>
is legal storage without gaining Source merely because a read member exists.

Expected stdout (not execution evidence):

```text
Associated numbers are 21, 21.
Associated flags are true, true.
Contract forwarding finished.
```

Separate rejection exercises:

- Call readNumber(flag@ref): bool does not satisfy the i32 equality.
- Call readTwice(storageOnly@ref): the conditional Source premise is absent.
- Remove a concrete associated-Type specification: method results do not infer it.
- Change NumberSource.read to an exclusive receiver: it cannot implement a
  requirement callable through a shared receiver.
- Remove `T is Source` from readTwice: favorable concrete callers cannot provide
  missing definition-side evidence.

Future verification should use another numeric value, inspect retained
requirement-to-member mappings, and reject contradictory associated bindings or
duplicate conformances. No runtime Contract View or caller-side member search is
introduced. Refinement diamonds, bound nested Contracts, ambiguity and conditional
proof failures belong to program 19's companion tests, not its short canonical
body. Contract Property requirements and operation witnesses belong to program 24;
generation/ABI inspection belongs to program 22.

Focus: [associated Types](../spec/08-generics-constraints-and-contracts.md#843-associated-types),
[implementation matching](../spec/08-generics-constraints-and-contracts.md#845-implementation-matching),
and [conditional conformance](../spec/08-generics-constraints-and-contracts.md#848-conditional-conformance).

## Milestone 20: inference and ordinary defaults

pick infers length, element Type and source Origin from a borrowed fixed array.
There is no specialization in this file. An omitted index evaluates defaultIndex
exactly once; an explicit index suppresses it. forward propagates length, Type
and Origin through a dependent call. A second element Type/length and a shorter
local lifetime exercise independent inference inputs without implementation selection.

Expected stdout (not execution evidence):

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

Separate rejection exercises:

- Supply `<2, i32>` with numbers: the fixed-array lengths disagree.
- Supply only `<3>`: partial generic argument lists are not introduced.
- Return the borrow of local outside its do scope, then use it.
- Change pick's result Origin to static: its input supplies no such guarantee.
- Add `func make<T>(value?: T = $abort("No value")) -> T => value` and call
  `make()` without a result annotation: defaults cannot infer an unbound T.

Companion verification covers conflicting Type/length/Semantics evidence,
expected-result inference, Origin bounds/intersections and ambiguous inference;
defaults inspected at declaration time; receiver/supplied/default evaluation order;
prepared argument slots and cleanup when normal transfer abandons a call. Keep
owned/default-result dependencies and rejected Move/retention cases in focused
fixtures. Explicit specialization belongs to 21; generation budgets belong to 22.
The canonical program does not attempt to close the entire inference/default family.

Focus: [inference](../spec/10-overload-resolution-and-inference.md#108-generic-argument-inference),
[Origin inference](../spec/15-ownership-and-lifetime-analysis.md#1534-generic-origin-inference),
and [argument preparation](../spec/07-functions-and-callable-values.md#72-parameters-and-defaults).

## Milestone 21: explicit full specialization and inherited contracts

All calls supply explicit Type/length arguments to separate selection from
program 20's inference target. The complete `<3, i32>` specialization reverses
indexing. It inherits the original universal source Origin and default contract,
without redeclaring an Origin binder or optional marker. Both the direct call
and generic forward must retain that selection. A two-element i64 array selects
the ordinary body; a shorter-lived i32 array uses the same specialization under
another valid Origin binding. Omitted index evaluation still belongs to the
original declaration; supplying it skips that evaluation.

Expected stdout (not execution evidence):

```text
Default index evaluated.
Specialized selection is 30.
Explicit selection is 10.
Default index evaluated.
Forwarded selection is 30.
Default index evaluated.
Ordinary selection is 4.
Default index evaluated.
Local selection is 9.
Specialization finished.
```

Separate rejection exercises:

- Redeclare `index?` or a default on the specialization, or add an Origin binder.
- Change its result Origin to static or otherwise narrow the inherited contract.
- Mismatch its result Type, labels, parameter structure or receiver contract.
- Declare a partial specialization, duplicate normalized selection key or an
  ambiguous original target. Invalid ordinary generic bodies remain invalid.

Companion verification covers constrained originals, receiver specializations,
closed compound/pair arguments and inherited defaults/Origins, with positive and
negative contract matching. Remove the specialization and adjust expected values
to 10/30/10/4/7; supply every index to suppress default messages; vary source
lifetimes without changing selection. Inspect direct and forwarded selected
Member Identities. Program 22 separately checks shared entry ABI, fixed frames and
budget invariance; this program introduces no partial/conditional specialization.

Focus: [full specialization](../spec/08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization)
and [implementation selection and generation](../spec/21-layout-runtime-and-code-generation.md#213-generic-code-generation).

For all unmodified programs, successful output lines end with LF, stderr is empty,
and normal termination returns exit code 0. Abort variants skip any remaining
ordinary cleanup and return exit code 1 under the specified Windows runtime.
