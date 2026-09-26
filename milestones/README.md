# Language milestones

Forty independent programs are planned from the current [SPEC](../SPEC.md).
Programs 1–33 have source files; programs 34–40 have design and verification scopes.
They are staged compiler implementation targets. Execution evidence and support
boundaries are recorded in [STATUS.md](../STATUS.md); expected output alone is
not an execution claim. Milestones 24–28 and 33 are authored targets beyond current
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
| [Milestone5](Milestone5.kimi) | `uniq`/`ref`, returned borrow, `during`, scope and destruction lifetimes |
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
| [Milestone22](Milestone22.kimi) | Concrete generic entries, compound layouts, specialization forwarding and distinct destruction operations |
| [Milestone23](Milestone23.kimi) | Standard/custom/computed Copy Properties, direct storage and assignment evaluation order |
| [Milestone24](Milestone24.kimi) | Non-Copy setter replacement, borrowed/owned getters and a standard-operation Contract witness |
| [Milestone25](Milestone25.kimi) | Inline base construction, inherited standard Properties and Type members, whole-derived Move and layered destruction |
| [Milestone26](Milestone26.kimi) | Generic compound captures, external borrowed captures, shared/exclusive/consuming Callable and owning function-value erasure |
| [Milestone27](Milestone27.kimi) | Saved Index/Range resolution, nested/sub-Slice views, splitting, empty views, backing/element Origins, and user Place results through `Indexable`/`UniqIndexable` |
| [Milestone28](Milestone28.kimi) | User IntoIterable/Iterator mappings, owned elements, continue/early-exit cleanup and retained external element borrows |
| [Milestone29](Milestone29.kimi) | Dynamic `Array<T>` reserve/append/insert/remove/pop/clear, indexed replacement of Non-Copy elements, owning iteration with early exit |
| [Milestone30](Milestone30.kimi) | User/generic comparison Contracts, borrow/Tuple composition, retained witnesses and NaN-reflexive equality through specialization |
| [Milestone31](Milestone31.kimi) | Dictionary duplicate rejection, retained equal keys, replacement/removal, insertion-order iteration and exact entry cleanup |
| [Milestone32](Milestone32.kimi) | User/generic UTF-8 formatting, independent owning strings, short-circuit writes, fixed-buffer reuse and bounded Console interpolation |
| [Milestone33](Milestone33.kimi) | Exclusive objects, base views, struct Type tests/refinement, complete Sealed payload exchange and dynamic layered destruction |

## Program status

As of **2026-09-26**, the execution order was revised: the collection track (27, 39, 28, 31, 40, 26, 37) precedes the Property/object track, and Program 27 owns the Place foundation ([PLAN.md](../PLAN.md#3-current-position)). Program 23 is complete with 67 Debug/Release harness checks in the [Copy Property session](../PLAN_HISTORY.md#p23-completion). That session also passes both full suites; earlier-program native regressions stopped at the user's request after programs 1–16. Programs 1–22 and 29–32 retain their Release harness evidence from the [Dictionary source session](../PLAN_HISTORY.md#p31-kimigayo-library); older Debug results retain their original verification scope. Program 30 is complete; Program 31 retains the separately listed unfinished scope. Build means a native
Application build including LLVM verification and linking; tests mean native
output/exit checks and, where a harness exists, its variants/rejections. Parser
coverage alone is not a native test. NOT_RUN is neither a pass nor a failure.

This table is the source/native status record. `MilestoneSourcesTest` checks the exact
authored source set against it, checks the explicit Binding column, requires complete Binding for passed programs, and
checks each pending program's stage, first diagnostic and source anchor against
[stage-baselines.json](stage-baselines.json). It also binds the recorded supported
declaration prefix independently. A changed failure or a newly successful stage requires
review and fresh evidence; neither is accepted as an arbitrary pending failure. These
checks supplement the focused feature tests and native harnesses; they do not certify
unreached parts of a pending program.

Affected programs now use postfix `during` annotations with the same intended behavior.
An unchanged target run compiles the checked-in program without test-specific edits.
A specification change may require a completed program to be re-spelled. The program
keeps DONE when its unchanged target run and harness pass on the re-spelled source, and
its row names the re-spelling. The 2026-09-25 place, borrowing and iteration change
re-spelled programs 13, 14 and 16 (DONE) and the pending programs 28 and 33, and the
2026-09-26 rename of `@deref` to `@follow` re-spelled 14, 16 and 33 again:

| Program | Re-spelling | Specification |
| --- | --- | --- |
| 13 | `associate Iterator.Item` replaces `Iterator.Element` | SPEC 22.1.2.1 |
| 14 | `accumulator@follow@uniq` replaces `accumulator@uniq/Pipeline.Accumulator` | SPEC 13.5.5.2 |
| 16 | `target@follow@uniq` replaces `target@uniq/Cell` | SPEC 13.5.5.2 |
| 28 | `IntoIterable` with `IteratorType` and `intoIterator` replaces `Iterable`; `Iterator.Item` replaces `Iterator.Element` | SPEC 22.1.2.1, 22.1.2.2 |
| 33 | `@follow@ref`/`@follow@uniq` payload borrows; `owner@move@obj/Base` transfers the bare Place | SPEC 3.5, 13.5.5.2, 13.5.7 |

| Program | Created | Binding | Build | Tests | Implementation | Evidence / boundary |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 2 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 3 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 4 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 5 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 6 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 7 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 8 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 9 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 10 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 11 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 12 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 |
| 13 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target (re-spelled 2026-09-25: `Iterator.Item`), O0/O2 variants and required rejections (harness added 2026-09-23); [evidence](../PLAN_HISTORY.md#program20-completion) |
| 14 | YES | PASS | PASS (Release) | PASS (Release) | DONE | Existing target/variant/rejection harness, O0/O2 (re-spelled 2026-09-25 and 2026-09-26: `@follow@uniq` reborrows) |
| 15 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target, O0/O2 variants and rejections; [completion](../PLAN_HISTORY.md#program15-completion) |
| 16 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target (re-spelled 2026-09-25 and 2026-09-26: `@follow@uniq` reborrow), O0/O2 variants and rejections; [completion](../PLAN_HISTORY.md#program16-completion) |
| 17 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target, O0/O2 variants and rejections; [evidence](../PLAN_HISTORY.md#program17-completion) |
| 18 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target, O0/O2 composite transfers, variants and rejections; [evidence](../PLAN_HISTORY.md#program18-completion) |
| 19 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target, O0/O2 variants and required rejections; [evidence](../PLAN_HISTORY.md#program19-completion) |
| 20 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target, O0/O2 variants and required rejections; [evidence](../PLAN_HISTORY.md#program20-completion) |
| 21 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target, O0/O2 variants and required rejections (harness added 2026-09-23) |
| 22 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged target, O0/O2 variants and required rejections (the InfiniteLayout rejection added 2026-09-23); [evidence](../PLAN_HISTORY.md#program22-completion) |
| 23 | YES | PASS | PASS (Debug/Release, O0/O2) | PASS (67 checks/configuration) | DONE | Unchanged target, name/value/compound/restricted-read/setter-Type variants and required rejections pass; focused lifetime/projection/Move tests and both full suites pass. Earlier-program harness scope was reduced by the user. |
| 24 | YES | FAIL | FAIL (prior Debug/Release, O0/O2 probes) | NOT_RUN | TODO | Current Binding first reports `UnresolvedBinding_Kd` at required `value.item`; ownership-bearing setters/getters and Contract Property calls remain. |
| 25 | YES | PASS | FAIL (prior Debug/Release native probes) | NOT_RUN | IN_PROGRESS | Current Binding passes; ownership analysis stops at inherited `value.count` with `UnsupportedOwnership_Kd`. Explicit base construction and layered destruction have focused native coverage. |
| 26 | YES | FAIL | FAIL (Debug/Release, O0/O2) | NOT_RUN | TODO | The current Binding baseline identifies unsupported generic capture storage at the closure with `UnsupportedBinding_Kd`; dependent declaration/call errors are suppressed. Native build status retains the [authoring evidence](../PLAN_HISTORY.md#programs25-28-authoring). |
| 27 | YES | FAIL | FAIL (prior Debug/Release, O0/O2 probes) | NOT_RUN | TODO | Current Binding first reports `UnresolvedBinding_Kd` at `Range`; general Slice/Range support remains. [Authoring evidence](../PLAN_HISTORY.md#programs25-28-authoring). |
| 28 | YES | FAIL | FAIL (prior Debug/Release, O0/O2 probes) | NOT_RUN | TODO | Re-spelled 2026-09-25 for `Kimi.IntoIterable`. `Drain<T>`, `Batch<T>`, `Cursor<T>` and `View<T>` bind, including the generic Slice element borrow `self.values[index]@ref`; Binding first reports `UnresolvedBinding_Kd` at `item.id` because `for item in batch@move` over a user `IntoIterable` conformance is not yet bound. User `for` protocols, Origin-related associated iterators and generic iterator-state ownership remain. [Authoring evidence](../PLAN_HISTORY.md#programs25-28-authoring). |
| 29 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | Unchanged source, shared-view/cleanup variants and required rejections pass through test-milestone29.ps1 (123 checks per configuration); shared string iteration is a positive case, while zero-sized Array elements retain an ownership-stage unsupported diagnostic. Allocation/cost probes pass. |
| 30 | YES | PASS | PASS (Debug/Release, O0/O2) | PASS (Debug/Release) | DONE | Unchanged target, 43 checks per harness, recursive Tuple/borrow mappings, preserved IEEE/Contract semantics and allocation probes; [completion evidence](../PLAN_HISTORY.md#review-remediation). |
| 31 | YES | PASS | PASS (Debug/Release, O0/O2) | PASS (Debug/Release) | IN_PROGRESS | Unchanged target, 71 checks per harness, mandatory static duplicate rejection, Kimigayo storage algorithms, slot reuse/cleanup and zero-allocation warm compilation; public generic API/capacity source migration, nonempty runtime literals, borrowed indexing and nested owning storage remain. [Evidence](../PLAN_HISTORY.md#p31-kimigayo-library). |
| 32 | YES | PASS | PASS (Debug/Release) | PASS (Debug/Release) | DONE | DONE: unchanged target, O0/O2 UTF-8/NUL/empty/numeric/failure variants and required rejections pass through `test-milestone32.ps1`; runtime costs and full-session regressions pass. [Evidence](../PLAN_HISTORY.md#program32-completion). |
| 33 | YES | FAIL | FAIL (prior Debug/Release native probes) | NOT_RUN | IN_PROGRESS | Re-spelled 2026-09-25 and 2026-09-26 (`@follow` payload borrows, `@move` transfer). Current Binding first stops at refinement-dependent `view.extra` with `UnresolvedBinding_Kd`. Explicit base construction, concrete runtime Type tests and complete dynamic destruction have focused native coverage. |
| 34 | NO (planned) | NOT_RUN | NOT_RUN | NOT_RUN | TODO | Scope assigned in the future-verification table |
| 35 | NO (planned) | NOT_RUN | NOT_RUN | NOT_RUN | TODO | Scope assigned in the future-verification table |
| 36 | NO (planned) | NOT_RUN | NOT_RUN | NOT_RUN | TODO | Scope assigned in the future-verification table |
| 37 | NO (planned) | NOT_RUN | NOT_RUN | NOT_RUN | TODO | Scope assigned in the future-verification table |
| 38 | NO (planned) | NOT_RUN | NOT_RUN | NOT_RUN | TODO | Scope assigned in the future-verification table |
| 39 | NO (planned) | NOT_RUN | NOT_RUN | NOT_RUN | TODO | Scope assigned in the future-verification table; source follows the G21 decision (PLAN §7) |
| 40 | NO (planned) | NOT_RUN | NOT_RUN | NOT_RUN | TODO | Scope assigned in the future-verification table; source follows the G22 decision (PLAN §7) |

[Restructuring audit](../PLAN_HISTORY.md#programs38-restructure) records source/DLL
identities and exact commands: Release compiler/test-project build PASS with zero
warnings/errors; 57 alias/syntax tests PASS; 577 checks across the existing
program 1–12/14 harnesses PASS; program 13 passes two native O0/O2 executions.
That historical audit covered the 24 sources then present. Later completions
supersede its failed probes; the table above records current support. Debug,
full managed regressions and NativeAOT were not run for that restructuring.

Earlier [program 14 regressions](../PLAN_HISTORY.md#program14-completion),
[units 62–67 audit](../PLAN_HISTORY.md#units62-67-verification) and
[18–20 authoring probes](../PLAN_HISTORY.md#programs18-20-design) remain historical
records of their own inputs. In particular, the old combined program-20 result
is not evidence for new program 20 or 21. The current table uses the new audit.

Each file is a separate Application; do not combine them into one project.
Under [single-source input rules](../impl/20-compilation-configuration.md#20861-input-resolution-and-implicit-projects),
the intended build/run commands, once the required features are implemented, are:

```powershell
kimi build milestones/Milestone1.kimi
kimi run milestones/Milestone1.kimi
```

Replace `1` with the milestone number. `run` executes an existing build; it does
not compile source. No separate project file is needed. Output uses fixed string
literals so programs 1–29 do not require numeric formatting or interpolation.
Programs 2–29 use `Console.writeLine` through the default Kimi alias. Only the
Hello World program keeps `::Kimi.Console.writeLine`; no extra alias is needed.

## Roadmap from program 15 to core completion

The current plan has **40 programs**, including **26 programs numbered 15–40**.
Programs 15–33 are concrete; 34–40 are future source targets. Programs 39 and 40
were added on 2026-09-26 for the two collection designs that the Place foundation
leaves open (PLAN issues G21 and G22); their execution order is in [PLAN.md](../PLAN.md#4-milestones-execution-order).
Source creation is not implemented capability. This count is a decomposition of scope, not an effort or delivery
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
| 22 | Generic generation | Monomorphized bodies (initial profile), compound ABI and finite generation limits; separate internal checks. Code sharing is deferred (§21.3.1) |
| 23 | Basic Properties | Standard/custom/computed access over Copy values, permissions and evaluation order |
| 24 | Ownership-bearing Properties | Non-Copy setters, owned/borrowed getter results, temporary lifetimes and Contract witnesses |
| 25 | Inheritance | Base storage, construction/destruction, inherited members and already-verified Property operations |
| 26 | General closures and Callable | Composite/generic captures, dependency retention, function values and permitted erasure |
| 27 | General Slice/Index/Range and the Place foundation | Partial/nested slices, bounds evaluation, permitted element Types and retained Origins; `place ref/T`/`place uniq/T` results, Contract Type parameters and user `Indexable`/`UniqIndexable` conformances |
| 28 | General Iterator/Iterable | User Iterable protocols, owned/borrowed elements, early exit and remaining-element cleanup |
| 29 | Dynamic Array | Capacity, insertion/removal/replacement, Non-Copy elements and owning iteration |
| 30 | Comparison Contracts | Equatable/Comparable, generic requirement calls and composed comparisons before Dictionary |
| 31 | Dictionary | Key equality, mutation, insertion order, iteration, Loans and allocation/complexity requirements |
| 32 | Utf8Format and interpolation | User/generic UTF-8 formatting, buffer reuse, short-circuit writes and owning interpolation |
| 33 | Exclusive objects and views | obj, base views, runtime struct tests/refinement, identity and dynamic destruction |
| 34 | Shared object ownership | rc/arc creation, explicit clone, Move, shared access and final strong release |
| 35 | Weak | Downgrade/upgrade, expiration, cyclic construction and final weak-table release |
| 36 | General static storage | First initialization, effects, cycles, shutdown and inherited generic environment keys |
| 37 | Integrated processing application | Collections, borrows, iteration and closures in one realistic workload |
| 38 | Integrated core application | Properties, inheritance, objects and formatting combined with established features |
| 39 | Semantics-generic follow | A pair `s/T` Place followed to its stored target under the admitted Semantics set; generic accessors over `Collection<s/T>` returning `ref/T` or `uniq/T` |
| 40 | Disjoint exclusive element access | Simultaneous exclusive borrows of distinct elements of one collection through a splitting operation over the internal storage boundary |

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
| 29 | 30, 32 | Separate comparison from Utf8Format/interpolation |
| 30 | 33, 34 | Separate exclusive objects/views from shared ownership |
| 31–34 | 35–38 | Weak, static storage and the two integration targets |
| — | 39, 40 | Added 2026-09-26: Semantics-generic follow and disjoint exclusive element access |

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
- **Implementation contracts:** inspect the active generation profile and ABI and measure the
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

### Verification scopes for programs 22–40

Every row inherits the three evidence gates above. The canonical program is a
small successful Application. Rejection/Abort cases use separate source copies;
IR, allocation, runtime-state and complexity checks use companion tests or tools.
Prerequisites name the main exercised capabilities, not permission to postpone
required legality checks. Where public operations use static-effect summaries,
verify those summaries before accepting calls even though the static-storage
demonstration is program 36. ObjectCallCompatible's deferred stages stay deferred.

| Program / main prerequisites | Canonical program | Separate semantic checks | Implementation evidence |
| --- | --- | --- | --- |
| 22 / 18–21 | Pass compound generic values through per-substitution concrete bodies; preserve different Type operations and selected specializations. | Different layouts and same-layout/different-destructor Types; finite recursion, growing substitutions, invalid infinite layout and required resource-limit diagnostics. | Inspect concrete FunctionAbi and ordinary frames, selected body identities and deterministic finite generation; measure relevant warm allocations. Shared contexts, scratch frames and optional sharing budgets are deferred (§21.3.1). |
| 23 / 4, 7 | Read/write stored standard, custom and computed Copy Properties with observable evaluation order. | Access permissions, differing setter inputs where permitted, single receiver/RHS/getter evaluation; reject writes or exclusive borrows into getter-result temporaries. | Distinguish direct Places from accessor calls; verify standard storage access and custom dispatch without invented get/set round trips. |
| 24 / 16–19, 23 | Replace a Non-Copy value through a setter and return a borrowed view through a getter; use a Contract Property requirement. | Owned getter results and legal receiver consumption, discarded setter inputs, temporary-borrow escape, conflicting Loans, invalid shared extraction and incompatible requirement operations. | Exact old/input/result destruction, getter-temporary lifetime, standard-operation witness identity and permitted bridges; no hidden Copy or storage exposure through a Contract. |
| 25 / 17, 23–24 | Construct a derived value, access inherited members/Properties and destroy complete derived/base storage. | Base initialization order/completeness, inherited access, prohibited redeclarations and invalid Partial Moves; separate early-transfer/Abort construction cases. | Base offsets and declaring-receiver projection, stable member mappings, one construction/destruction responsibility per layer. |
| 26 / 12, 14, 16, 18–22 | Capture a compound/generic value and an external borrow, then invoke through the required Callable mode; separately demonstrate permitted function-value erasure. | Shared/exclusive/consuming calls, nested captures, function items, moved closures, escaping dependencies and erasure without required Copy/Owned evidence. | Environment layout, direct versus common entries, capture destruction, no per-call environment allocation; optional erasure allocation accounted separately. |
| 27 / 7, 13, 16, 19 | Resolve Index/Range values and retain nested/sub-Slice views of external backing storage; publish user Places through `Indexable`/`UniqIndexable` and forward one through a generic Constraint. | Empty/full/from-end bounds, one-time bound evaluation and Abort order; reject conflicting mutation, escaping views and Non-Copy indexed acquisition; reject Take, bare Non-Copy reads and shared-path updates of published Places, and Place results over ending storage. | O(1) views/metadata, no element copying or Slice backing allocation, full nested-Type/Origin/Loan preservation; Place results use the reference ABI. Mutable-element Slice remains excluded. |
| 28 / 13, 18–19, 27 | Implement user Iterable/Iterator protocols, yield owned or externally borrowed elements, then stop early. | Exhaustion, continue/exit/return, correct associated Element/Iterator equality, retained previous borrowed results; reject lending results and missing capability proofs. | Receiver acquisition once, exact yielded/unyielded responsibilities and reverse remaining-element cleanup; no hidden element clone. |
| 29 / 17–18, 27–28 | Grow, insert, replace and remove Non-Copy Array elements; consume an iterator and stop early. | Empty/pop/clear, directional indices, capacity/no-op paths, live and empty-Slice conflicts, retained borrowed contents, normal argument abandonment and Abort. | Count internal allocations; verify within-capacity/no-op/removal guarantees, reverse current-index cleanup, growth amortization and shrink failure preserving original placement. |
| 30 / 19, 21 | Compare user Types through Equatable/Comparable and generic calls, including composed Tuple/borrow comparisons. | Missing/incompatible conformance, equality/order agreement, operand order and no Non-Copy consumption; built-in floating comparison versus NaN-reflexive Equatable mapping. | Retained requirement mappings and specialization preserving comparison meaning; no pointer-identity substitute or synthesized user equality. |
| 31 / 19, 28–30 | Insert/reject/replace/remove Dictionary entries with user-defined equal keys; inspect and iterate insertion order. | Result/Option ownership, duplicate literal diagnostics and runtime duplicates, stored-key preservation, missing-key assignment Abort, lookup/mutation Loans and dependency retention. | Equality effects and invocation order, value-before-key/reverse-insertion cleanup, allocation-free duplicate/lookup/replacement paths, churn reuse and specified management bounds. No public Hash requirement is added. |
| 32 / 18–19 | Interpolate user/generic values through Utf8Format, retaining the original Non-Copy values and independent resulting strings; reuse a fixed buffer through `$tryWrite`. | UTF-8/NUL/empty text, source-order evaluation, once-only formatting, temporary cleanup, missing conformance, exclusive root acquisition and Abort before later interpolation. | Verified Utf8Format writes, buffer ownership and cleanup, no retained source Loan in the combined result; required allocation bounds and the bounded Console stack path. |
| 33 / 14, 24–25 | Create an obj, use a base object view, perform specified struct `is` tests/refinement, preserve identity and destroy the complete Dynamic Type. | Sealed payload projection, borrow/reborrow, legal whole-payload updates, invalid view/acquisition/escape; test expression effects and refinement invalidation. | Header/view identity, dynamic destruction before original storage release, unchanged identity across updates. No runtime Contract View, checked-cast spelling or deferred ObjectCallCompatible inference. |
| 34 / 33 | Create rc and arc values, explicitly clone strong handles, Move them and observe final strong release. | Shared-only access even at count one, no implicit clone, moved-handle rejection, external payload dependencies and separate count-overflow failure probes. | Exact retain/release counts, clone without allocation/payload copy, complete payload destruction once; inspect atomic arc ordering with internal tests. No source concurrency or obj/rc/arc conversion is added. |
| 35 / 26, 34 | Downgrade, upgrade and expire Weak handles; then demonstrate a cyclic factory's Building-to-Alive transition. | Weak clone/Move, upgrade before publication and after final release, payload dependencies, factory Owned/Callable constraints and failed construction. | Separate payload/object/table lifetimes, allocation-free upgrade/clone, final table release; internally test arc upgrade/final-release races without introducing source threading. |
| 36 / 19, 22, 24–25, 34 | Initialize static values on first access, distinguish enclosing generic keys and destroy in reverse successful-initialization order. | First write before replacement, unused storage, alias paths to one key, effects/reentry, non-Owned storage rejection; initialization cycles and invalid shutdown access in separate Abort inputs. | Per-key state/address/destruction identity, preserved keys across body sharing and Origin erasure, no initialization from untaken paths or effect summaries alone. |
| 37 / 26–32 | One bounded processing workload using collections, borrowed views, iteration and closures with exact results and cleanup. | Empty input, alternate values, early stop, rejection/Abort paths and representative Loan violations derived from the workload. | Workload allocation/complexity observations and regressions of prerequisites; no new language mechanism introduced to make the application work. |
| 38 / 24–25, 32–36; 37 as needed | A second application using Properties, inheritance, objects and formatted output with exact lifetime behavior. | Alternate object lifetimes, replacement, empty/expired states, shutdown and relevant rejected accesses. | Cross-feature identity/cleanup/ownership checks and prerequisite regressions. It need not repeat every program-37 collection operation; no new feature family is deferred to this final target. |
| 39 / 8, 18, 27 | Generic code over `Collection<s/T>` follows a pair Place to its stored target for every admitted `s`: shared under `value or valueborrow`, exclusive under `owner or uniq`. | Reject an exclusive follow whose admitted set includes `ref`, Take through the followed Place and any layer choice that would differ between instantiations; the followed Place depends on the outer borrow Origin whenever `s` is a borrow. | The Access Effect is fixed per admitted set at definition checking; monomorphized bodies select the owner Place or the referent without a runtime test and without allocation. Written after the G21 decision. |
| 40 / 27–29, 31 | Borrow two distinct elements of an Array exclusively at once through a splitting operation, update both, and observe `None` for equal indices. | Reject two `@uniq` element borrows of one collection, conflicting whole-collection access while a split part is live, and retention of a part after the collection is moved or resized. | Region splitting through `Kimi.Storage` in O(1) without allocation; both child Loans end before the parent is reused. Written after the G22 decision. |

Owning clauses: [Properties](../spec/11-properties.md),
[generics and Contracts](../spec/08-generics-constraints-and-contracts.md),
[sequences and collections](../spec/04-arrays-indexing-and-slices.md),
[interpolation](../spec/12-expressions.md#1233-interpolation-formatting),
[comparison and objects](../spec/13-operators-and-assignment.md),
[generation/runtime representation](../impl/21-layout-runtime-and-code-generation.md),
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

Focus: [Origins](../spec/15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations),
[Loans and lifetimes](../spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting),
and [destruction lifetime checking](../spec/15-ownership-and-lifetime-analysis.md#1566-destruction-lifetime-checking).

Milestone 5 is verified through native execution. Reproduce with the pinned
Windows x64 toolchain:

```powershell
dotnet build Kimigayo.slnx -c Release --no-restore
./backend/windows-x64/test-milestone5.ps1 -Configuration Release
```

The script verifies LLVM IR and native linking, then checks native execution and
both forms of CLI `run`. Its Debug/Release checks cover the
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
publication. Reports and source/compiler/build
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

The script covers the unchanged program,
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
broader Slice and Iterator milestones (programs 27 and 28), general user-defined iteration, direct Slice iteration,
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
[Origins](../spec/15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision).

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
Reports and build identities are
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

The script covers the unchanged source,
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
and [generic code generation](../impl/21-layout-runtime-and-code-generation.md#213-generic-code-generation).

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
and [closure generation](../impl/21-layout-runtime-and-code-generation.md#2125-concrete-closures-and-common-function-values).

## Milestone 13: a Slice-backed Iterator

Cursor implements the Kimi Iterator Contract and explicitly binds its Element
to `ref/T during source`. It stores a borrowed Slice and a position; it owns no
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
adds 2, 4, and 6, and stops before 100. Kimi.Intrinsics.makeObj acquires a fully initialized
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

Verified stdout:

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

`backend/windows-x64/test-milestone15.ps1 -Configuration Release` reproduces
the harness checks: unchanged source, byte-identical renamed O0/O2 copies, renamed symbols,
different values, reversed selection, zero/five iterations, distinguishable
destructor messages and mutation after the last borrow use, plus five invalid
variants at both O0/O2. Normal runs check exact stdout, empty stderr and exit 0
through direct execution and both CLI run forms. Builds verify LLVM before
linking. Debug is also supported; NativeAOT is not run. Source/compiler hashes
and reports are saved under `bin/milestone15/<configuration>/<run-id>/`.

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
[abstract Origins](../spec/15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations),
[call propagation](../spec/15-ownership-and-lifetime-analysis.md#1564-calls-and-origin-propagation),
and [reborrowing](../spec/15-ownership-and-lifetime-analysis.md#1563-reborrowing-and-region-splitting).

## Milestone 17: repair, replacement and ordered cleanup

The fixed array has no user deinit. Moving its complete Resource element is
permitted even though Resource itself has deinit; moving one of Resource's
Non-Copy fields would be a different operation. Repair restores array completeness.
Exchange transfers Resource 2 into old without destroying it, swap transfers
Resources 3/4 without destruction, and replace destroys Resource 3 at its target.
Literal indices 0 and 1 provide disjoint static paths for the simultaneous Loans.

Verified stdout (Debug/Release compilers, O0/O2):

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

The [dedicated tests](../xUnitTest/Tests/StaticElementUpdateTest.cs) also cover
incomplete-array cleanup, early return, nested paths, retained disjoint borrows,
replacement destruction that Aborts, and serialized reload. Resource identities
and exact output detect duplicate, missing or reordered destruction. Updates use
the existing inline storage and transfer instructions. Generic storage, arbitrary
borrowed-content dependencies and general interprocedural effects remain broader
requirements.

```powershell
./backend/windows-x64/test-milestone17.ps1 -Configuration Release
./backend/windows-x64/test-milestone17.ps1 -Configuration Debug
```

Each run checks the unchanged target, byte-identical O0/O2 renamed copies,
name/value/implicit/typed/literal-index variants, and nine rejection cases at both
optimization levels. Reports include compiler/source hashes, exact output and
exit checks; [the completion record](../PLAN_HISTORY.md#program17-completion)
describes the managed and native regression scope. NativeAOT is not used.

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

Verified stdout (exit 0, empty stderr, Debug/Release O0/O2):

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

The [native harness](../backend/windows-x64/test-milestone18.ps1) checks the
unchanged target, byte-identical renamed O0/O2 copies, renamed declarations,
changed ids, the second Box, early return and unused Delivery cleanup. It also
rejects seven invalid variants at both optimization levels.

```powershell
./backend/windows-x64/test-milestone18.ps1 -Configuration Debug
./backend/windows-x64/test-milestone18.ps1 -Configuration Release
```

Companion tests cover complete Resource/array/struct payloads, nested and partial
bindings, inactive Cases, zero-size and zero-length arrays, aborting cleanup,
equal-size Types with distinct destructors, invalid universal ownership, corrupt
plans and serialized reload. The canonical IR retains five shared bodies and
seven concrete entries with fixed scratch reservations. General shared
ABI/frame/resource guarantees remain program 22's broader acceptance criteria;
program 18 does not require later dedicated features.

Focus: [generic body checking](../spec/08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations),
[generic generation](../impl/21-layout-runtime-and-code-generation.md#213-generic-code-generation),
and [secured results and cleanup](../spec/16-scope-exit-and-destruction.md#1622-results-and-transfers).

## Milestone 19: Contracts, associated Types and conditional conformance

Source requires a Copy Core Element and a shared read operation. NumberSource
and FlagSource explicitly bind that associated Type. Wrapper has unconditional
storage but fulfills Source only when its stored T does. The conditional block
forwards both the associated identity and the verified requirement call.
readTwice returns two associated values; readNumber additionally requires their
Type to equal i32. Nested Wrappers must compose the same evidence. Wrapper<i32>
is legal storage without gaining Source merely because a read member exists.

Verified stdout (Debug/Release, native O0/O2; exit 0, empty stderr):

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

Verification uses another numeric value, renamed Types/members, false Boolean
values, a deeper Wrapper and observable read effects. The harness also rejects
contradictory associated bindings and duplicate conformances, in addition to the
five cases above, before publishing LLVM or executable artifacts.

```powershell
./backend/windows-x64/test-milestone19.ps1 -Configuration Debug
./backend/windows-x64/test-milestone19.ps1 -Configuration Release
```

Each configuration checks the canonical single-source O2 build/run, six
O0/O2 variants through native/executable/input run modes, and seven O0/O2 rejection
cases. `AssociatedForwardingTest` checks projection normalization, retained
requirement/member identities, ownership, invalid evidence, and concrete tuple
results. The focused associated-projection/proof warm Binding test allocates zero
bytes; this is not a whole-program allocation guarantee.

Associated identities normalize after container substitution, including nested
conditional conformances. Requirement calls use verified mappings and concrete
per-substitution bodies; they do not perform caller-side member search or runtime
Contract dispatch. Current native evidence covers the canonical scalar results
and a constructed Copy tuple result. Whole aggregate-field acquisition through a
shared receiver still diagnoses unsupported ownership; general bound/refinement
composition retains the limits in STATUS. Existing Binding tests cover broader
refinement/conditional-proof validity without establishing general native support.
Contract Property operations belong to program 24; complete generation migration
and resource limits belong to program 22.

Debug/Release full suites pass 11,342 tests each; all existing milestone harnesses
(1–12 and 14–19) pass in Release, and program 13 passes exact-source O0/O2 probes.
Reports and identities are linked from the [completion record](../PLAN_HISTORY.md#program19-completion).
No NativeAOT verification was run.

Focus: [associated Types](../spec/08-generics-constraints-and-contracts.md#843-associated-types),
[implementation matching](../spec/08-generics-constraints-and-contracts.md#845-implementation-matching),
and [conditional conformance](../spec/08-generics-constraints-and-contracts.md#848-conditional-conformance).

## Milestone 20: inference and ordinary defaults

pick infers length, element Type and source Origin from a borrowed fixed array.
There is no specialization in this file. An omitted index evaluates defaultIndex
exactly once; an explicit index suppresses it. forward propagates length, Type
and Origin through a dependent call. A second element Type/length and a shorter
local lifetime exercise independent inference inputs without implementation selection.

Expected stdout (verified natively at O0/O2 by `backend/windows-x64/test-milestone20.ps1`):

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
- Add `func make<T>(value: T = $abort("No value")) -> T => value` and call
  `make()` without a result annotation: defaults cannot infer an unbound T.

Companion verification covers conflicting Type/length/Semantics evidence,
expected-result inference, Origin bounds/intersections and ambiguous inference;
defaults inspected at declaration time; receiver/supplied/default evaluation order;
prepared argument slots and cleanup when normal transfer abandons a call. Keep
owned/default-result dependencies and rejected Move/retention cases in focused
fixtures. Explicit specialization belongs to 21; generation budgets belong to 22.
The canonical program does not attempt to close the entire inference/default family.

Focus: [inference](../spec/10-overload-resolution-and-inference.md#108-generic-argument-inference),
[Origin inference](../spec/15-ownership-and-lifetime-analysis.md#1536-limited-origin-inference),
and [argument preparation](../spec/07-functions-and-callable-values.md#72-parameters-and-defaults).

## Milestone 21: explicit full specialization and inherited contracts

All calls supply explicit Type/length arguments to separate selection from
program 20's inference target. Selected references are read as their Copy
referents (SPEC §3.3); prefix `*` belongs to raw pointers only. The complete `<3, i32>` specialization reverses
indexing. It inherits the original universal source Origin and default contract,
without redeclaring an Origin binder or optional marker. Both the direct call
and generic forward must retain that selection. A two-element i64 array selects
the ordinary body; a shorter-lived i32 array uses the same specialization under
another valid Origin binding. Omitted index evaluation still belongs to the
original declaration; supplying it skips that evaluation.

Expected stdout (verified natively at O0/O2 by `backend/windows-x64/test-milestone21.ps1`):

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
Member Identities. Program 22 separately checks concrete entry ABI and finite
generation limits; this program introduces no partial/conditional specialization.

Focus: [full specialization](../spec/08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization)
and [implementation selection and generation](../impl/21-layout-runtime-and-code-generation.md#213-generic-code-generation).

## Milestone 22: concrete generic generation

`forward` calls `relay` with a dependent Type argument. A fixed array and a nested
Tuple exercise different compound layouts; the original Copy array remains usable.
Direct and forwarded `i32` calls select the explicit specialization. `Red` and
`Blue` have identical field layouts but different destruction operations, each
performed once after transfer through both generic entries.

Expected stdout (verified natively at O0/O2 by `backend/windows-x64/test-milestone22.ps1`):

```text
Compound layouts preserved.
Specialization preserved.
Red received.
Red destroyed.
Blue received.
Blue destroyed.
Generic generation finished.
```

Separate checks: vary layouts and array lengths; remove the specialization and
expect 10/20; reuse a moved Non-Copy argument (reject); exercise finite recursive
calls with an unchanged key, growing `T -> Box<T>` substitutions, excessive finite
substitution sets and invalid infinitely recursive inline layouts. Resource limits
must diagnose rather than hang, crash or publish incomplete artifacts.

Native stdout does not prove monomorphization. Companion implementation checks
must inspect concrete entries and call signatures, selected member identities,
per-substitution Type operations and ordinary frames, and confirm removal of the
transitional shared fallback. Allocation measurements belong to implementation
completion. Shared contexts, scratch frames and optional sharing-budget tests are
deferred, not acceptance conditions of this initial profile.

Focus: [generation policy](../impl/21-layout-runtime-and-code-generation.md#2131-policy-and-sharing-conditions),
[finite generation](../impl/21-layout-runtime-and-code-generation.md#2135-generation-limits-and-code-merging),
and [calls and frames](../impl/21-layout-runtime-and-code-generation.md#214-checked-lowering-and-internal-abi).

## Milestone 23: basic Copy Properties

Construction first-places `raw` and `level` without accessor calls. Standard
`raw` access is direct. Assigning to the custom `level` evaluates the RHS before
the receiver, once each, then calls the setter without a getter. One explicit
read invokes the custom getter. The computed `doubled` has no storage and updates
`raw` through its setter; its getter is evaluated once.

Verified stdout (unchanged source, Debug/Release and O0/O2; exit 0, empty stderr):

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

Separate checks: restrict `raw`'s setter and reject an external write/exclusive
borrow while allowing a read; vary values; use a computed setter with a different
input Type; check compound assignment's receiver/getter/RHS/setter order. Reject
writes and exclusive borrows into owned getter-result temporaries, including
nested projections. Verify direct standard operations do not synthesize accessor
calls and custom operations do not expose backing storage.

`backend/windows-x64/test-milestone23.ps1 -Configuration Release` reproduces
67 checks: 39 native/CLI executions and 28 diagnostic rejections. Debug also
passes. `CopyPropertyEmissionTest` adds 41 focused cases, including 21 fixtures
verified and executed at O0/O2, abrupt RHS evaluation, prefix/postfix updates,
getter-result projection/borrowing, and direct Move permissions. Completed
Debug/Release full suites each pass 12,440 tests. Evidence is in
`bin/verify/20260924-111727-097-session-p23-completion`; the earlier-program
regression portion was cancelled at the user's request, and target verification
finished separately. No later-program implementation was started.

Focus: [standard operations](../spec/11-properties.md#111-standard-access-and-acquisition),
[accessor functions](../spec/11-properties.md#112-accessor-functions),
[construction](../spec/11-properties.md#1131-construction-and-destruction),
and [assignment](../spec/13-operators-and-assignment.md#137-assignment).

## Milestone 24: ownership-bearing Properties

`Holder` first-places Resource 1 without calling its setter. Replacement transfers
Resource 2 to the setter and destroys Resource 1 before installing it. The computed
view returns a receiver-bounded borrow. `Viewed.item` maps to the standard shared
slot-borrow operation, even though the implementation has a custom setter; it does
not expose a Move or exclusive borrow. `Parcel.result` consumes its receiver and
returns its owned field, whose destruction responsibility transfers to `owned`.

Expected stdout (specification-derived; native execution is blocked):

```text
Setter entered.
Resource 1 destroyed.
Borrowed resource is 2.
Borrowed resource is 2.
Holder scope finished.
Resource 2 destroyed.
Owned getter received.
Resource 3 destroyed.
Ownership properties finished.
```

Separate checks: a setter that discards its input must destroy that input and keep
the old value; direct standard replacement witnesses and explicit computed
implementations must preserve requirement contracts. Borrow an owned getter result
for one call (allowed), then retain the borrow past its temporary lifetime (reject).
Reject replacement while a view remains live, shared extraction of a Non-Copy
field, use of a consumed Parcel, direct Move/exclusive borrow through the custom
setter, and incompatible requirement operations. Verify exact destruction counts,
temporary lifetimes and retained witness identity without hidden copies.

Focus: [Non-Copy setters](../spec/11-properties.md#1124-non-copy-custom-setters),
[getter temporaries](../spec/11-properties.md#1123-getter-results-and-temporaries),
and [standard witnesses](../spec/11-properties.md#1142-standard-operation-witnesses).

## Milestone 25: inheritance, base storage and layered cleanup

`Derived` constructs its inline `Base` before evaluating its own Field initializer
and constructor body. The base's private `resource` and the derived Field of the
same Name remain distinct. An inherited standard Property reads and writes the
base slot, and the inherited Type function keeps its declaring identity. Moving
the complete derived value transfers both layers; cleanup runs derived `deinit`,
derived Fields, base `deinit`, then base Fields, exactly once.

Expected stdout (specification-derived; native execution is blocked):

```text
Base field initialized.
Base constructor finished.
Derived field initialized.
Derived constructor finished.
Inherited access finished.
Derived value moved.
Derived deinit.
Derived resource destroyed.
Base deinit.
Base resource destroyed.
Inheritance finished.
```

Separate checks: omitted/defaulted base arguments, multiple inheritance layers,
generic base substitution, protected/private access, incomplete construction and
ordinary transfers before construction starts. Abort during base/derived
construction must not promise unwinding. Reject accessible inherited-Name
redeclarations, derivation from a sealed Type, invalid base calls, use after Move
and Partial Moves across a user-`deinit` layer. Inspect base offsets, distinct
Field identities and each layer's construction/cleanup state. Inherited standard
storage and Type members require no ObjectCallCompatible publication; borrowed
method/custom-accessor projection remains subject to that separate deferred proof
boundary and is not silently assumed by this program.

Focus: [inheritance](../spec/06-declarations-and-containers.md#622-inheritance-and-open-structures),
[constructors](../spec/06-declarations-and-containers.md#623-constructors),
[member projection](../spec/09-names-signatures-and-access.md#951-base-subobject-receiver-projection),
and [layered cleanup](../spec/16-scope-exit-and-destruction.md#1632-field-cleanup).

## Milestone 26: general closures, Callable and owning erasure

`inspectTwice` moves an unconstrained generic value into a concrete environment
and retains a borrowed visitor whose own capture borrows external `bias`. Shared
Callable calls inspect the Packet twice and destroy it once on helper return.
An exclusive Callable mutates a captured Tuple snapshot; a consuming Callable
transfers a Packet out of its environment. A separate shared closure with an
Owned, Non-Copy Packet environment is moved into a common Function Type, moved
again as that same Type, called twice and destroyed once. A Function Item is
also copied into a common value while remaining independently callable.

Expected stdout (specification-derived; native execution is blocked):

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

Separate checks: nested captures and capture order, alternative compound Types,
result borrows from external captures, zero-sized captures and common values
with inline versus heap environments. Reject bare capture of unproven-Copy `T`,
escaping external dependencies, moved-closure reuse, exclusive calls on immutable
owned closures, consuming calls through borrowed receivers and erasure of
non-Owned or exclusive/consuming-only environments. Inspect concrete entries,
indirect-call ABI and exact remaining-capture cleanup. Measure no per-call
environment allocation; account for permitted erasure allocation separately.

Focus: [captures and invocation](../spec/07-functions-and-callable-values.md#76-function-expressions),
[Callable](../spec/08-generics-constraints-and-contracts.md#86-callable-constraints),
[closure dependencies](../spec/15-ownership-and-lifetime-analysis.md#1582-closure-dependencies-and-call-results),
and [environment/erasure layout](../impl/21-layout-runtime-and-code-generation.md#2125-concrete-closures-and-common-function-values).

## Milestone 27: general Slice, Index and Range

The two Range boundaries evaluate once in source order. Resolving against a row
length produces `[1, 3)`; slicing nested rows preserves the element Type and
backing storage. `middle` survives the local handles used to form it, is split
without copying elements, and supports a from-end empty view and a generic tail.
`^0` is a boundary but not an element, and applying the saved Range to a shorter
length fails through `tryResolve`. Shared string access preserves its Non-Copy
owner. A `Slice<ref/i32>` read copies the inner reference with its original
dependency, distinct from the Slice's backing-slot dependency.

`Pair<T>` publishes its two stored Places through `Indexable<isize>` and
`UniqIndexable<isize>` (§4.6.9, §7.1.1): `names[1]` at a `ref/string` parameter
selects `index` and borrows the second Place; `counts[0] = ...` selects `indexUniq`
and replaces the first Copy value in place (a Non-Copy replacement through a Place
shares the reference-write boundary, STATUS); `names[0]@ref` keeps one reference
from one search; and the generic `firstPlace` forwards a Place result through the
Contract requirement without acquiring a value, for `Pair<i32>` and `Pair<string>`.

Expected stdout (specification-derived; native execution is blocked):

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
```

Separate checks: full/empty/inclusive ranges, `Index.init`, zero-sized elements,
`tryGet`/`trySlice`/`trySplitAt` success and failure, saved bounds reapplied to
different targets, receiver/boundary evaluation and negative-bound Abort order.
Reject escaping local or temporary backing, mutation conflicting with a retained
view (including an empty one), Non-Copy indexed Move, Slice element writes and
element-Type covariance. Check that resolving metadata adds no storage Loan;
views and reslices preserve the original Loan footprint and nested Origins.
Measure O(1) view operations without backing allocation or element copying.
Mutable-element Slice remains outside this milestone.

Place checks: a bare read of a Non-Copy published Place, `@move` of a published
Place, an update through `index` alone (a `let` pair or a `ref/Pair` receiver),
a Place result that designates a local or a by-value parameter, an `if` whose
branches designate Places without their own `return`, a mismatched `Element`
specification, and a conformance that omits `indexUniq` while claiming
`UniqIndexable` are rejected. A retained `pair[0]@ref` conflicts with a later
`pair[0] = ...`; the exclusive receiver of `indexUniq` is reserved before the key
is evaluated (§15.6.7). The Place result ABI is the reference ABI (§21.2).

Focus: [bounds and failure](../spec/04-arrays-indexing-and-slices.md#464-bounds-evaluation-and-failure),
[Slice lifetimes](../spec/04-arrays-indexing-and-slices.md#465-slice-storage-lifetime-and-permissions),
[element results](../spec/04-arrays-indexing-and-slices.md#466-slice-operations-and-element-results),
[Indexable Contracts](../spec/04-arrays-indexing-and-slices.md#469-indexable-contracts),
[Place results](../spec/07-functions-and-callable-values.md#711-place-results),
and [required costs](../spec/04-arrays-indexing-and-slices.md#468-representation-and-performance).

## Milestone 28: user Iterable/Iterator and element responsibilities

`Batch<T>` maps Iterable's Element and Iterator to `T` and `Drain<T>`. Its consuming
`iterate` runs once, transferring the cursor; each `next` exchanges one Option
slot with `None`. A `continue` destroys Item 1 before the next call. Early exit
destroys Item 2, then the cursor's unyielded Items 4 and 3 in reverse Field order.
`View<T>` separately maps to a cursor over external Slice storage, with explicit
associated-Type Origin equality. A retained first reference survives subsequent
`next` calls and exhaustion; a temporary View also drives a `for` loop.

Expected stdout (specification-derived; native execution is blocked):

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

Separate checks: owned exhaustion and repeated `None`, empty external input,
exhaustive `for`, return/named exit through a loop, receiver evaluation once,
retaining multiple previously yielded references and dependent/non-Copy `T`.
Reject missing Iterable/Iterator proof, mismatched associated Element identity,
bare iteration over a user owner without shared conformance, moved iterable reuse
and lending results that borrow the iterator or its owned storage. Inspect the
retained requirement mappings, short `next` receiver Loans and exact yielded/
unyielded cleanup. No hidden clone or array of iteration values is permitted;
measure O(1) iterator state and no per-element management allocation.

Focus: [iteration acquisition](../spec/14-control-flow.md#1462-iteration-protocol-and-acquisition),
[associated Types](../spec/08-generics-constraints-and-contracts.md#843-associated-types),
[Kimi protocol requirements](../spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations),
and [component cleanup](../spec/16-scope-exit-and-destruction.md#1632-field-cleanup).

## Milestone 29: dynamic Array growth, mutation and owning iteration

A typed empty literal has length and capacity zero and allocates nothing. `reserve`
takes an additional count and never shrinks. `append` and `insert` add Non-Copy
elements by transfer; `insert(^0, ...)` appends and `remove(^1)` transfers the last
element out, preserving capacity. Indexed replacement destroys the old element
before installing the new one, `pop` returns an Option that owns the element, and
`clear` destroys every element in reverse index order while keeping capacity. The
owning iteration consumes the Array; leaving the loop early destroys the current
binding and then the unyielded elements. `last` is destroyed at scope exit.

Expected stdout (verified in Debug/Release at O0/O2):

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

Separate checks: `pop` and `remove(^1)` on an empty Array (`None` and Abort),
`remove(^0)` and out-of-range `insert`/`remove` indices (Abort), `reserve` within
capacity and `reserve(additional: 0)` as no-ops that preserve placement, growth
beyond capacity, `shrinkToFit`, `clear` of an empty Array, a bare read of a
Non-Copy element (shared, never a Move), `values[i]@move` on an Array (reject), a
live element or Slice borrow across a mutation (reject), an exclusive mutation
through a shared receiver (reject), abandonment of an appended argument when an
earlier argument transfer fails normally, and Abort inside a destructor during
`clear`. Count internal allocations: none within capacity, on removal, on
`clear` or for the empty literal; growth is amortized O(1) per `append`.

These runtime and rejection checks pass through `test-milestone29.ps1` and the
`DynamicArray*` native fixtures. Instrumented native probes verify allocation and
growth-transfer bounds, failed shrink preservation and representable capacity
limits; repeated shared views allocate no storage, and warm Binding, ownership,
validation, emission and whole-pipeline probes allocate zero bytes. Zero-sized
elements and shared string iteration, which are outside the verified generation
boundary, are rejected by ownership analysis with `UnsupportedOwnership_Kd`.

Focus: [Array operations](../spec/04-arrays-indexing-and-slices.md#472-array-operations),
[capacity](../spec/04-arrays-indexing-and-slices.md#474-capacity-and-allocation),
[Loans and effects](../spec/04-arrays-indexing-and-slices.md#475-loans-retained-dependencies-and-call-effects)
and [commit and destruction order](../spec/04-arrays-indexing-and-slices.md#476-commit-failure-and-destruction-order).

## Milestone 30: comparison Contracts and composed mappings

`Key` explicitly implements Comparable and its inherited Equatable requirement.
Its `compare` returns -7, 0 or 9, so expressions must inspect the sign rather than
assume -1/1. Generic equality, forwarding and an explicit `f64` specialization
retain the selected mapping. Owner and borrowed operands remain usable after
comparison. Tuples compose user mappings through shared-borrow elements.

Floating Contract equality treats all NaNs as equal and both signed zeros as
equal; ordinary floating and Tuple comparison expressions retain IEEE equality.
The unchanged source exercises both rules through the common recursive comparison plan.

Expected stdout (verified unchanged target, Debug/Release O0/O2):

```text
User comparisons keep their operands.
Tuple comparisons compose witnesses.
Floating Contract equality is NaN-reflexive.
Comparison contracts finished.
```

`backend/windows-x64/test-milestone30.ps1` checks the unchanged target, renamed
declarations, changed values and an `f32` variant at O0/O2. Its independent
rejections cover missing conformance/premises, incompatible or missing witnesses,
an exclusive equality receiver, mixed operand Types, floating Comparable and a
Move conflicting with the left operand's Loan. `-Cases Rejections` checks those
cases independently of the canonical target. Companion managed/native
tests cover primitive boundaries, evaluation/destruction order and NaN mapping.
The review remediation unit now passes the unchanged target and every Debug
harness case, 415 related managed tests and 146 native O0/O2 executions
(`bin/verify/20260924-021918-773-unit-comparison-composition-regressions`).
Composite comparisons preserve witness effects and short-circuit order; floating
Tuple operators retain unordered NaN results, while Contract equality is reflexive.
The measured warm Tuple-operator Binding and native floating-composition workloads
allocate zero bytes. Debug/Release full suites and all 26 Release harnesses now pass; see the
[completion session](../PLAN_HISTORY.md#review-remediation). The [earlier failed probes](../PLAN_HISTORY.md#p30-session1) are historical evidence.

Focus: [comparison mapping](../spec/13-operators-and-assignment.md#1341-contract-comparison-mapping),
[floating key equality](../spec/12-expressions.md#1234-dictionary-construction-and-duplicate-keys),
[Contracts](../spec/08-generics-constraints-and-contracts.md), and
[required declarations](../spec/22-core-execution-and-foreign-functions.md#221-required-kimi-declarations).

## Milestone 31: Dictionary keys, insertion order and entry ownership

`Key.equals` compares an immutable logical code and ignores the tag identifying
each individual key value. `tryInsert` returns both rejected inputs in `Err`;
`insertOrReplace` keeps the original stored key and position, destroys the unused
input key before delivering the old Item, and indexed assignment destroys the
replaced Item. Shared lookup and iteration inspect entries without consuming
them. Removing and reinserting an equal key appends its new entry while retaining
capacity. Owning iteration stops after the first pair; cleanup destroys that
pair, then the unyielded entries in reverse insertion order, value before key.

Expected stdout (verified unchanged target with Debug/Release O0/O2; full milestone completion remains pending):

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

Separate checks: nonempty literals, mandatory literal duplicate diagnostics,
runtime duplicates before value evaluation, NaN/signed-zero keys, missing-key
assignment Abort, empty/clear/shrink paths, complete owning iteration and generic
keys. Reject missing Equatable, moved inputs, key mutation and structural mutation
while a retained lookup/iteration borrow is live; preserve nested dependencies
through rejected inputs and removed/replaced results. Check argument/equality
effects, one-time indexed evaluation and destructor Abort. Measure internal
allocations for empty construction, lookup, duplicate/absence, replacement and
full-capacity churn, plus the specified management bounds; unchanged capacity
alone is not evidence of allocation-free execution. No public Hash is required.

Focus: [Dictionary operations](../spec/04-arrays-indexing-and-slices.md#473-dictionary-operations-and-indexed-replacement),
[duplicate keys](../spec/12-expressions.md#1234-dictionary-construction-and-duplicate-keys),
[Loans and effects](../spec/04-arrays-indexing-and-slices.md#475-loans-retained-dependencies-and-call-effects),
[cleanup](../spec/04-arrays-indexing-and-slices.md#476-commit-failure-and-destruction-order)
and [required costs](../spec/04-arrays-indexing-and-slices.md#477-performance-and-extension-boundary).

## Milestone 33: exclusive object views and complete dynamic destruction

An exclusively owned `Leaf` is inspected through `objref/Base`. A stable
parameter's `is Leaf` test enables the Leaf-only standard Field on the right of
`and`, and a new narrowed binding supports complete Sealed payload projection.
A call-result Type test evaluates its observable operand once. An exclusive base
view is refined before exchanging the complete Leaf payload: the old contents
are destroyed separately, while the object's Dynamic Type remains Leaf and
subsequent reads see the new `let` Field contents. An owning upcast finally moves
the whole object into `obj/Base`; scope exit still destroys the complete Leaf,
its Resource and its Base before releasing object storage.

Expected stdout (specification-derived; native execution is blocked):

```text
Base view refined to Leaf.
Type-test operand evaluated.
Complete payload replaced.
Leaf deinit.
Original resource destroyed.
Base deinit.
Updated object remains a Leaf.
Owning base view retains the complete Leaf.
Leaf deinit.
Replacement resource destroyed.
Base deinit.
Exclusive object run finished.
```

Separate checks: same-target shared/exclusive reborrows, deeper bases and generic
struct identity, failed/unrelated tests, `not`/`or`/join refinement and test-operand
cleanup. Reject implicit or downward upcasts, open/base payload projection,
borrow escape, conflicting access, moved-handle reuse, ObjectPayload opt-outs and
refinement assumed through `var`, aliases or stored Booleans. Check whole-payload
replacement/swap, field-fact invalidation, retained dependencies and destruction
Abort. Inspect header/view identity, unchanged allocation/metadata across payload
updates, and release through the original allocation after dynamic destruction;
the source makes no address-equality claim. Direct standard Fields and complete
Sealed projections require no deferred ObjectCallCompatible inference. Runtime
Contract Views and checked-cast syntax remain outside this program.

Focus: [object views](../spec/03-types-and-values.md#335-object-views-and-identity),
[upcasts](../spec/13-operators-and-assignment.md#1357-object-upcasts),
[runtime tests](../spec/13-operators-and-assignment.md#1361-runtime-is-tests),
[refinement](../spec/14-control-flow.md#1410-type-refinement),
[payload projection](../spec/13-operators-and-assignment.md#13551-follow)
and [dynamic release](../spec/16-scope-exit-and-destruction.md#1633-ownership-object-release-and-reentry).

### Programs 22–24 authoring verification (2026-09-22)

All three sources pass the syntax catalog. Native Application builds were probed
with Debug and Release compilers, each at O0 and O2, using byte-identical source
copies in separate Application projects. Program 22 now builds and runs through
its harness (varied layouts and lengths, specialization removed, finite recursion,
moved Non-Copy reuse, growing keys and an invalid infinitely recursive inline layout,
rejected at Binding with `InvalidInlineLayout_Kd`); its remaining separate check, excessive
finite substitution sets (bounded at 1024 contexts per body), is covered by a unit test. Programs 23
and 24 fail final Binding; no native output/exit test ran for them, and their
expected outputs and separate checks above are targets, not passing test claims.
See [session evidence](../PLAN_HISTORY.md#programs22-24-authoring).

### Programs 25–28 authoring verification (2026-09-24)

All four sources pass syntax parsing. The source catalog explicitly records them
as pending Binding, alongside 23–24, and checks that all 30 authored sources are
present. Sixteen byte-identical-source Application builds were attempted with the
Debug and Release compilers at O0 and O2; all fail Binding before native execution.
The table records those failures, not successful native tests. Full managed
regressions pass 12,119 tests in each configuration with warning-free compiler
builds; the final source catalog/alias checks pass 58 tests in each configuration.
No new milestone completion, rejection-harness or allocation evidence is claimed.
Existing native harness results retain their prior scope. Source/compiler hashes,
commands, diagnostics and logs are recorded in the
[authoring session](../PLAN_HISTORY.md#programs25-28-authoring).

### Programs 31 and 33 authoring verification (2026-09-24)

Both sources pass syntax parsing and are explicitly pending in the Binding source
catalog, which now covers 33 authored programs. Source catalog/alias checks pass
58 tests; the final Debug/Release compiler builds are warning-free and each full
managed suite passes 12,169 tests. Eight Application builds use byte-identical
source copies and the Debug/Release compilers at O0/O2; all fail Binding before
native execution. Their native tests are NOT_RUN, and expected output above is
specification-derived. P31/P33 remain TODO, with no new native harness, rejection
or allocation evidence; existing harness results retain their prior scope.
Commands, source/compiler hashes and diagnostics are in the
[authoring session](../PLAN_HISTORY.md#programs31-33-authoring).

For all unmodified programs, successful output lines end with LF, stderr is empty,
and normal termination returns exit code 0. Abort variants skip any remaining
ordinary cleanup and return exit code 1 under the specified Windows runtime.
