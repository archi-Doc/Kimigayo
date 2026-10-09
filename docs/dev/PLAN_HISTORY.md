# Kimigayo Plan History

A few lines per session. Current work belongs in [PLAN.md](PLAN.md), support boundaries in [STATUS.md](../STATUS.md). Detailed records through this compaction remain in the [2026-10-08 source snapshot](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/PLAN_HISTORY.md), including every failed run, intermediate experiment, commit and measurement. Retrieve it locally with `git show 2834c7bf41d458a4fc1744fb9466ad03eccc2e5a:docs/dev/PLAN_HISTORY.md`; older pre-2026-09-22 details remain at `git show 32324537:PLAN_HISTORY.md`.

Archive line numbers below refer to that immutable snapshot, not this file. Historical completion and pending statements describe their checkpoints, not current support. Ignored `artifacts/verify/` and `artifacts/benchmarks/` need separate retention/backup; Git preserves the records and references, not those artifacts. Keep new sessions above the compacted index.

## Sessions

- **2026-10-09 — G34-3 (pair follow narrowed, F15 fixed):** a follow through a layer whose binder admits no `owner` keeps the slot met with the borrows that reach the operand when `uniq` is admitted (`PairOrigin`, shared by `@follow`, receivers and Subjects), and `PathReborrowOrigin` steps through an outer pair layer; A.10, A.10b, `holder-detach2`, the nested form and the R3 form are `UnprovenOriginRelation_Kd` like their concrete twins, while `-> uniq/T during c` siblings run. Instances bind `o` in meets, Origin arguments, annotations and forwarded call Origins, which also removes an `IndexOutOfRangeException` in generation for a borrow-only follow passed to a generic call. A Place below a pair-followed referent keeps a body-local Origin (task filed). The whole suite passed as feedback (20,400). Unit `artifacts/verify/20261009-060816-870-unit-g34-3-pair-follow`: 256 tests, 0 snapshot differences, 130 native O0/O2, P39.
- **2026-10-09 — G73-C (G73 closed):** the G73 units S and 1–6 are complete, including the optional G73-6, whose gate held; `Array.init(! repeating:count:)` and `init(! capacity:)` are public bodiless constructors defined by internal Kimigayo bodies through the catalog link, and callers use the published summaries of the Dictionary operations. Session `artifacts/verify/20261009-054008-766-session-g73-c-session`: 20,390 tests, 0 snapshot differences, 194 native O0/O2, milestones 29 and 37.
- **2026-10-09 — Debugger KD0 (prototypes, no product change):** throwaway Win32 Debug API and LLVM 22.1.8 prototypes for `draft/Proposals/2026-10-09 Kimi Debug Adapter Plan.md`, each rerun by an independent verifier. KD-Q3 is SuspendThread (0.011–0.032 ms, always the user thread; DebugBreakProcess 0.6–1.2 ms on an injected thread); KD-Q4 is DbgHelp `StackWalkEx` without `SYMOPT_DEFERRED_LOADS` (which silently returned wrong frames), so no own unwinder; KD-Q2 evidence favors `#dbg_declare` slots plus the side table. Plan corrections: a per-thread DR0 return-slot watch for step over/out (fib(20) 1 event vs 6,764), the call test `[RSP]` in (prev RIP, prev RIP+15], scope-end positions for shared cleanups, reproducible staged PDB links, `/debug` as the third machine-code difference, and about 190 µs per breakpoint hit. Evidence `artifacts/verify/20261009-kd0/` (RESULTS.md).
- **2026-10-09 — G73-6 (capacity construction on the same link, gate held):** `init(! capacity:)` loses its compiler kind and lowering (`CompilerFunctionKind.ArrayWithCapacity` and `LowerArrayConstruction` removed) and is defined by the internal Kimigayo `initCapacity` (`[]` then `reserve`) through the catalog link; every capacity, cost and abort-location test and allocation count passed unchanged, and an O2 IR inspection shows the implementation inlined into its caller. Catalog counts 178/183. Compile-time cost is one more small library body bound each pass (not measured separately; G73-5 measured +1.0% for the repeating body). Unit `artifacts/verify/20261009-053716-508-unit-g73-6-capacity-link`: 265 tests, 0 snapshot differences, 180 native O0/O2, milestones 29 and 37.
- **2026-10-09 — G73-5 (IR shape and warm-stage evidence):** a rewritten failing allocation Aborts with `KIMI_E_ALLOC` at the repeating construction (the implementation carries the caller location), the IR has no per-element append or place helper, the implementation is analyzed only for a program that constructs one, and the shared warm Array pipeline theory, now with a repeating construction, still allocates nothing. Library Bind A/B (`artifacts/benchmarks/g73-4-library-bind`): warm +1.0% median, cold +0.9%, cold bytes +19.3 KB, from binding the added library body every pass. Unit `artifacts/verify/20261009-051409-968-unit-g73-5-ir-shape`: 78 tests, 96 native O0/O2.
- **2026-10-09 — G73-4 (repeating construction through a catalog link, F9 and F10 fixed):** the bodiless public `init(! repeating:count:)` (`T is Copy`) in ArrayOperations.kimi is linked to the internal Kimigayo `Array.initRepeating` (`Entry.Implementation`, static link invariants, one signature row checked for both, `ValidImplementationLink`); a call selects, checks and presents the public constructor and executes the implementation (`ExecutedTarget`, `PresentedTarget` in re-checks, effects, hover and ownership and lowering text), which reserves once, places Copies with `Raw.initialize` and publishes the length. Native rows cover explicit, inferred and expected Types, zero, struct, fixed-array and unit elements, generic bodies (A.8) and borrowed elements; a negative count Aborts at the construction; allocation is at most one with no growth transfer. Deviation: no mutation row for the link itself (declaration modifiers are immutable in tests); catalog counts are 177/182. Unit `artifacts/verify/20261009-050755-156-unit-g73-4-repeating-construction`: 559 tests, 0 snapshot differences, 192 native O0/O2, milestones 29 and 37.
- **2026-10-09 — G73-3 (callers use published summaries, F12 fixed):** ownership records no generic-call effect regions or results for a call whose selected declaration publishes its summary (`PublishesSummary`), so a generic body may keep a Dictionary `tryInsert` result across another `tryInsert` or a user generic call (A.8b, A.8c run natively); a user generic function, even one that calls such an operation, keeps the unbounded model. Unit `artifacts/verify/20261009-044424-042-unit-g73-3-published-callers`: 226 tests, 0 snapshot differences, 6 native O0/O2.
- **2026-10-09 — G73-2 (published summaries flagged and verified):** the catalog flag `PublishedSummary` (`PublishesSummary`, O(1)) marks the Kimi-bodied §4.7.5 operations: Dictionary `tryGet`, `tryInsert`, `remove`, `insertOrReplace`, indexed replacement (`indexUniq`), `reserve`, `shrinkToFit` and `clear`. `SummarizePublishedOperation` checks each body with abstract Type parameters against its row, covering only the `K` equality the row publishes; all hold under `confined`, and all but `indexUniq` (whose exclusive receiver lends its result) under preserves results. The Array rows are compiler functions; the Array position entries `insert`/`remove` have no catalog identity yet. Unit `artifacts/verify/20261009-044009-323-unit-g73-2-published-summaries`: 160 tests.
- **2026-10-09 — G73-1 (storage and raw families classified, F11 fixed):** `ClassifyCompilerFunction` gives every compiler function one effect class: the Storage and Raw families are input accesses by family (`IsStorageOperation`, `IsRawOperation`), so `setArrayLength`, `borrowStorageUniq` and the index-bounds Abort no longer make `appendCopies`, `Slice.toArray` or UniqSlice indexing unclassified under `confined`; Console output and the test temporary directory are environment effects; formatting dispatch stays followed first. `EveryCompilerFunctionHasOneEffectClass` pins every kind. Unit `artifacts/verify/20261009-043323-831-unit-g73-1-storage-families`: 138 tests, 0 snapshot differences.
- **2026-10-09 — G80-C (G80 closed):** the G80 units S, P1–P4 and K0–K4 are complete; the exclusive remainder (inherited custom setters, inherited `uniq/Self` methods and their witnesses, exclusive object accessors) transfers to a new PLAN OCC-X row with the fix plan §3.5 "selection complete, preservation proof pending" design input, and the function-reference K0 gap moves to G10. The P25 completion review is the user's decision. Session `artifacts/verify/20261009-041427-369-session-g80-k4-session` (20,355 tests) and an O0 sweep of its 6,193 fixtures passed; pinned A/B `artifacts/benchmarks/g80-k3-derived-synthesis` (`--inheritance-plans`, `--inheritance-lifecycle`) within noise.
- **2026-10-09 — G73-S (repeating construction specified):** SPEC §4.7.4 defines `init(repeating: v, count: n)` as `init(capacity: n)` followed by `n` appends of a Copy of `v` (one acquisition, Abort before placement on a negative count, at most one allocation); §4.7.2 cites it, the §4.7.5 "None" row lists both Array constructors and §22.1.2.5 names the `setArrayLength` use. SETTLED records why there is no constructor delegation, whole-`self` placement or factory spelling. Documentation only; the Session after G80-K4 (`artifacts/verify/20261009-041427-369-session-g80-k4-session`, 20,355 tests) verified the code it describes as unchanged.
- **2026-10-09 — G80-K4 (implicit-constructor workloads):** `VerificationWorkloads.InheritedPlans` takes `implicitConstructors`, which drops `init() => ()` from every layer so each layer synthesizes its constructor; `InheritedPlanReuseTest` runs 1x1, 32x1 and 4x32 natively and keeps warm Bind, ownership and emission allocation-free for 32x1 and 4x32; `--inheritance-plans` and `--inheritance-lifecycle` measure the variant. Unit `artifacts/verify/20261009-041245-346-unit-g80-k4-implicit-workloads`: 75 tests, 44 native O0/O2.
- **2026-10-09 — G80-K3 (derived implicit constructors, F4 fixed):** a derived structure without an explicit constructor whose own Fields all have initializers indexes its synthesized constructor as pending (declared, not in the `init` group); `CompleteImplicitConstructors`, the last step of `BindDeclarations`, decides each base first with `SelectOmittedBaseConstructor`, joins a selected one to the group and withdraws the others with their nodes and obligations; an invalid base graph or unproven base clause Constraints leave it resting on the clause, and a base withdrawn for an Unknown premise or another failure propagates that decision. A construction or base call without a constructor names the reason (`UnresolvedBinding_Kd`) or the Unknown premise (`UnprovenConstraint_Kd`, premise and base clause related); the base call is anchored at the base clause in its own fragment; `RevalidateImplicitConstructors` re-judges after bodies and fails closed on disagreement. Deviation: a base default keeps its own Abort location (not the base clause). Full suite 20,350 passed; Unit `artifacts/verify/20261009-040740-282-unit-g80-k3-derived-synthesis`: 1,008 tests, 0 snapshot differences, 166 native O0/O2, milestones 25 and 31.
- **2026-10-09 — G80-K2 (non-publishing selection core):** `BindCallCore` evaluates each candidate through `EvaluateCallCandidate` and decides through the pure `ClassifySelection` (`Binding.CallSelection`, with `MeasureCandidates`), keeping every publication branch in its order. `SelectOmittedBaseConstructor` runs the same evaluation and classification over the direct base constructors with no arguments from the derived constructor scope and restores obligations, candidate bounds, counters and per-call state; behind `CaptureOmittedBaseQueries` the declaration pass records it for explicit omitted-base constructors. Differential rows (access, defaults, ambiguity, absence, conditional, Unknown, refuted, generic, Origin-bearing, cross-module) agree with the bound call; repeated queries allocate nothing. Full suite 20,322 passed; pinned A/B `artifacts/benchmarks/g80-k2-selection-core` within noise; Unit `artifacts/verify/20261009-033745-418-unit-g80-k2-selection-core`: 786 tests, 0 snapshot differences, 6 native O0/O2.
- **2026-10-09 — G80-K1 (constructor absence and compiler-managed Types, F5 and F23 fixed):** every struct records how its merged members determine constructors (`StructKoto.ConstructorAvailability`: Explicit, MissingInitializer with the Field, Eligible, CompilerManaged from the catalog flag `ManagesRepresentation`, prepared after the struct symbol is declared so the Kimi identity is visible). Compiler-managed Kimi Types receive no synthesized `init()`; a construction or base call of a Type without constructors is one Language `UnresolvedBinding_Kd` at `T.init` or the base call target with a reason Note (`FailConstructorAbsence`), replacing `UnsupportedBinding_Kd`/`InvalidTypeFormation_Kd`; derived eligible structures and Type parameters under identity premises stay Unsupported. The three `CallReservationTest` Dictionary rows now use `[:]`. Full Functional suite passed (one transient file-replace timing failure passed on rerun); Unit `artifacts/verify/20261009-030426-211-unit-g80-k1-constructor-absence`: 802 tests, 0 snapshot differences, 20 native O0/O2.
- **2026-10-09 — G80-K0 (Unknown premises that cannot affect selection, F26 fixed):** a call candidate whose applicability is unproven only by an Unknown premise, after every argument and result check, is marked (`EvaluatedCandidate.PremiseUnknown`) and ranked as applicable by the ordinary comparison (`PremisesCannotAffect`, the shared `StrictBestCandidate` with `CandidateOrder` over the wider set); an applicable candidate still strictly best is selected, and a better, tying or incomparable Unknown candidate leaves the call unproven. `ValidateFixedConstruction` applies the same rule, so an inferred construction keeps the selection. SPEC §8.4.8.2 now states the criterion. Function reference selection still defers on every Unknown condition (PLAN G80). Full Functional suite 19,233 passed; Unit `artifacts/verify/20261009-024237-450-unit-g80-k0-unknown-premises`: 181 tests, 0 snapshot differences, 6 native O0/O2.
- **2026-10-09 — G80-P4 (pending exclusive witnesses, F8 fixed):** a conformance whose only unproven witness is an exclusive implementation reached through a base records that cause on its path (`BoundConformancePath.PendingExclusive`, `PendingExclusiveProof`) instead of folding an Unknown into the proof, and is published as one located `UnsupportedBinding_Kd` naming the implementation: at an explicit `Self is C`, whose uses derive from it, or at the use of an inherited conformance (`FailPendingConstraint`); an unused inherited one says nothing and acceptance is unchanged. An independent error is still reported. Unit `artifacts/verify/20261009-022610-165-unit-g80-p4-pending-exclusive`: 186 tests, 0 snapshot differences.
- **2026-10-09 — G80-P3 (compound updates through inherited getters, F7 fixed):** a compound update or increment of an inherited Property with a custom `get` and a standard `set` reads through the getter with the whole located receiver borrowed or reborrowed at its own Type and its base prefix lent, as `PrepareCallArgument` does for a `BaseBorrow` (`BaseBorrowTypes`, shared); own Properties and standard `set` paths are unchanged. Owned, `uniq`, increment, assignment-then-update and generic-base rows run natively, a live borrow across the update conflicts, and the separate exclusive setter stays Unsupported. Unit `artifacts/verify/20261009-020611-493-unit-g80-p3-compound-updates`: 235 tests, 96 native O0/O2.
- **2026-10-09 — G80-P2 (inherited standard bridges, F3 fixed):** a standard witness bridge for a base path has the same body as an own one, a member of the conforming Type that reaches the inherited Field by standard projection (`StructStorage.FindField`), so `PropertyWitnessFunction` drops its base-path guard, `InstantiatePropertyRequirementCall` never projects a storage bridge's receiver, and the cache rebuilds when the implementing Property or the operation changes. Copy, set, compound, increment, borrow (Loan kept, later write after last use), inherited conformance, multilevel generic base, conditional path, custom `get` with standard `set`, Origin-bearing Field and Non-Copy set run natively; inherited `let` and private `var` stay refuted; warm reuse allocates nothing. Edit rows were not added: the tests have no in-place edit harness. The whole Functional suite passed as feedback (19,215). Unit `artifacts/verify/20261009-015854-747-unit-g80-p2-inherited-bridges`: 274 tests, 0 snapshot differences, 88 native O0/O2, P24/P25. Measurement `artifacts/benchmarks/g80-p2-inherited-bridges` (against U0, in-tree builds): `--property-plans` and `--inheritance-plans` per-phase B/A 0.938-1.049, zero bytes per sample, identical plan table sizes.
- **2026-10-09 — G80-P1 (bridge premise scope, F6 fixed):** a standard witness bridge takes its conformance path's scope (`PropertyWitnessFunction`; `ConstraintScope` returns it for a property-witness body) and the conforming declaration as its container, so `when` premises reach its Copy proof: A.4 prints 3 at O0 and O2, and `S<string>` through `read` stays `NoApplicableOverload_Kd` with the refuted-premise Note and no IR. Unit `artifacts/verify/20261009-014711-959-unit-g80-p1-bridge-premises`: 159 tests, 0 snapshot differences, 32 native O0/O2.
- **2026-10-09 — G80-S (constructor and bridge rules):** SPEC §6.2.3.6 decides an implicit constructor by the omitted base clause's ordinary selection from the derived declaration (no applicable or an ambiguous candidate: none; an Unknown premise: unproven, reported at a construction with derived dependents; behaves as `init() => ()`), §6.2.3.3 states that selection, §22.1 gives a compiler-managed Kimi Type exactly its declared constructors (an empty Dictionary is `[:]`; the plan's premise that LIBRARY lists `Dictionary.init()` was wrong, so K1 moves three tests to `[:]`), and §11.4.2 names an inherited bridge's receiver and premises. SETTLED records the conditional implicit constructor; STATUS lists the observed G80 gaps (F3, F5-F8, F23). Documentation only: diffs, references and a `check` of the new examples (both parse; `Tally.init()` and `Picked.init()` show the recorded K1/K3 gaps).
- **2026-10-09 — G34-2 (case-conditional slot premises, F2 fixed):** a premise edge from a pair layer's explicit slot carries its binder as a Semantics-case condition (`PairSlotCondition`, a bit among the use's pair binders, `OriginPremiseClosure` edge conditions); a relation carries the case it is required in (`BindingObligation.Condition`) and its closure follows only edges whose condition it contains. Type occurrences, fits, call relations, input and result well-formedness and the record builder condition a relation at a pair slot; other proofs stay unconditional, which only rejects. A.2, A.2b, A.2c and both slot exploits are `UnprovenOriginRelation_Kd` at the definition; A.14, `cond-fit-mixed`, `sp-target-direct2` and a seven-binder row stay accepted. The whole Functional suite passed as feedback (19,187). Unit `artifacts/verify/20261009-013113-734-unit-g34-2-conditional-premises`: 565 tests, 0 snapshot differences, 130 native O0/O2, P28/P39. Measurement `artifacts/benchmarks/g34-2-conditional-premises` (against U0, with W1): `--origin-proof` identical outcomes, warm +1.2% median, cold +0.6%; `--pair-cases` warm analysis +2-3%, warm Bind +1-1.6%, warm allocation still zero.
- **2026-10-09 — G34-1 (pair Field storage pinned):** tests only. `GenericStorageEmissionTest` runs 18 pair-Field programs natively (owner, ref, uniq, inferred, obj, objref and raw `Holder` instances, a destructor, a closure capture, an Array literal, `s/U during a`, `Option<s/T>`, `s/Option<U> during a`, `Array<s/U during a>`, nested layers, an enum payload and an object family) and pins their Loan, escape, transfer and missing-Object-Target checks; `PairAnnotationBindingTest` adds conditional Field slots per admitted set and `AssociatedPairFormationTest` ref and uniq family instances. STATUS drops the stale storage claim and states the G10 construction-slot codes. Unit `artifacts/verify/20261009-011312-984-unit-g34-1-pair-storage`: 110 tests, 40 native O0/O2, P39.
- **2026-10-09 — G34/G73/G80 fix plan, G34-S (pair-slot premise rules):** SPEC §15.6.5 states that in a body with pair binders a pair layer's outer-Origin slot, `o` included, is a premise only in its binder's cases that store it; §15.6.1 gives Advice for an `o` end; §8.4.3.1 publishes a pair layer's formation conditions only for those cases. SETTLED records the rejected case-unconditional premise and slot-only uniq follow; STATUS lists F2, F14, F15 and F19 as the current gaps. The sibling repair plan is closed as 完了 in `draft/Obsolete`. Documentation only: diffs, references and a `check` of the new examples (both parse; their acceptance is the recorded F2/F15 gap).
- **2026-10-09 — G34/G73/G80 fix plan, W1 (input well-formedness at uses, G85 closed):** SPEC §15.6.4 step 3 now proves every instantiated parameter Type's well-formedness at the use (§15.6.1 locates it at the argument, §23.3.6.5 reports it as `wellFormed`). One visitor serves result and input well-formedness: a call's collected input patterns shorten only an already-limited covariant meet where the other bounds do not prove the relation (`OriginInference.WellFormed`, never verified by the solve), the selected call judges each argument whose instantiated Type changed and whose fit holds, and references, Item conversions, Callable arguments and conformances prove the replaced positions. The A.1 family (direct, pair, value call, Item argument, conversion, witness) is rejected; the valid twins run. Feedback: the whole Functional suite found three regressions (unprojected receiver patterns, a bound that opened a receiver Borrow, a `Never` argument), all fixed; re-checking the probe archive (`artifacts/verify/20261009-g34-g73-g80-u0/w1-sweep.txt`) changes exactly the 20 A.1-family probes, and the A.2 family (F2) stays for G34-2. Unit `artifacts/verify/20261009-005356-848-unit-w1-input-wellformed`: 437 tests, 0 snapshot differences, 56 native O0/O2, P19/P20/P26/P28/P37/P39. Measurement `artifacts/benchmarks/w1-input-wellformed` (`--origin-proof`, interleaved against U0): identical outcomes, no censored case, warm check +1.0% median (range -3.9% to +5.4%), cold +0.4%, and a constant 92 more proof requests per case from library calls.
- **2026-10-09 — G34/G73/G80 fix plan, U0 (evidence and records):** the [fix plan](../../draft/Proposals/2026-10-09%20G34%20G73%20G80%20Fix%20Plan.md) (revision 2, `ce940acb`) is adopted. Its 804 probe folders are archived with sources, `check`/`run` outputs and a one-line index in `artifacts/verify/20261009-g34-g73-g80-u0/`; the baseline diagnostic snapshot comes from the clean worktree on `ce940acb` (`artifacts/verify/20261009-000013-898-unit-g34-g73-g80-baseline`). PLAN: G34 is rewritten from the findings (its storage claims are stale), G10 states the observed construction-slot codes (F22), and G85 (input well-formedness, wrong code) and G86 (F25) are added. Documentation only.
- **2026-10-09 — G74 U5, S1 and U6 (closed):** environments live for one request and release every reference; edits, restores, alternating sources and Compilations keep their own judgments, warm Binding and ownership allocate nothing, and broader reuse is not adopted (0.2-0.4 ms of a 36-45 ms warm check). Session `artifacts/verify/20261008-191936-882-session-g74-u5` on d59d6d15: 20,205 tests, 0 snapshot differences, 172 native O0/O2, P20/P26/P27/P37. S1 publishes the proof universe in SPEC §15.3.6 (a98337da). U6 removes the comparison mode and the previous search; the plan closes as 取り込み済み in `draft/Changes`. Final Session `artifacts/verify/20261008-192744-794-session-g74-final` on c6506786 in the clean worktree: 20,205 tests, toolchain verified, 0 snapshot differences, 234 native O0/O2, P20/P26/P27/P37; final measurement `artifacts/benchmarks/g74-final` (no censored case, size-16/size-1 warm ratios 0.80-1.40).
- **2026-10-09 — G74 U4 (discharge owners, one verdict):** obligations record their discharge owner at creation (`OriginDischarge`; a direct borrowed input's well-formedness belongs to call-site borrow formation) and ownership computes one verdict per obligation, shared with the emission check and version-checked. The whole Functional suite in comparison mode found 0 proof or owner mismatches (338 owners the old recognition missed, each discharged by its previous judgment; `artifacts/verify/20261008-191034-611-unit-g74-u4-origin-oracle`). Unit `artifacts/verify/20261008-191633-307-unit-g74-u4` on 25ea5579: 582 tests, 0 snapshot differences, P20/P26/P27/P37.
- **2026-10-09 — G74 U2–U3 (premise closure):** proof is one closure over the extracted catalog R1–R5 per request; the SPEC-rule oracle, catalog extraction and metamorphic tests pass, every family completes through size 32 with linear environments (`artifacts/benchmarks/g74-u3`), and the whole Functional suite in comparison mode agreed with the previous search on 9,277,054 requests (641 over its budget, 0 mismatches, no environment met an incomplete contract; `artifacts/verify/20261008-185607-378-unit-g74-u3-origin-oracle`). Session `artifacts/verify/20261008-190205-902-session-g74-u3` on cc01af6d: 20,183 tests, 0 snapshot differences, 234 native O0/O2, P20/P26/P27/P37.
- **2026-10-09 — G74 U0b–U1:** ownership checks the structural input well-formedness exclusion before proof (Unit `artifacts/verify/20261008-180702-787-unit-g74-u0b`, 0 snapshot differences). Proof is pure: an audit of the whole suite found the initializer-inference effect only at the top of fits and selected-call relations, now `FitOriginOutlives`; every top-level request checks a state version, and candidate trials buffer bounds so only the selected candidate's apply.
- **2026-10-09 — G74 U0 (evidence and measurement contracts):** thirteen scalable workload families, a scaling test of their SPEC outcomes, opt-in proof counters and an isolated `--origin-proof` driver; the baseline (`artifacts/benchmarks/g74-u0`) shows exponential proof calls in every family with fewer than 400 distinct queries. Targets: `src/Benchmark/OriginProof.md`.
- **2026-10-09 — G59 U6–U7, closed:** composition rows (nested projections, stored-reference slots, separate Loans, value-read controls, retained borrows through references), corrupted route records, edit-and-reanalysis, and a zero-allocation `--element-places` workload; value reads and updates match 649a7f4e (`artifacts/benchmarks/g59-u7`). No known source reaches the fail-closed default. The plan closes as 完了 in `draft/Obsolete`; F1/F2 stay in PLAN G59. Final Session `artifacts/verify/20261008-171548-872-session-g59-final` on a38011ab in a clean worktree: 20,133 tests, 1,250 native O0/O2 executions, P17/P27/P29/P31/P37/P40.
- **2026-10-09 — G59 U5 (paths through references, snapshot route removed):** elements through `ref`/`uniq` Arrays, fixed-array references and Slices take the element route; owned Arrays below references are path-classified (`b.items[0].n` reads through `ref/Bank`, writes through `uniq/Bank`). The counted snapshot route and `SnapshotBorrows` are gone; emission rejects every snapshot borrow and no known source reaches the fail-closed default. Probes `artifacts/verify/20261008-g59-u0-baseline/u5-probes`; Unit `artifacts/verify/20261008-170108-930-unit-g59-u5b` (P17/P27/P29/P31/P37/P40), matched to 7bda1420 by the clean-worktree rerun `artifacts/verify/20261008-170453-472-unit-g59-u5-clean` (the shared tree held another session's untracked `i.html`).
- **2026-10-09 — G59 U4 (runtime fixed-array selectors):** an owned fixed-array element at a runtime index and its parts are borrowed in place in every mode (exclusive fixed-array index route; string comparisons through `CompareInPlace`), so `bump(ys[i]@uniq)`, `ys[i].name`, `fixed[i] == "a"` and `same(a[i], a[0])` execute; a moved element still blocks runtime selection. Probes `artifacts/verify/20261008-g59-u0-baseline/u4-probes`; Unit `artifacts/verify/20261008-163657-128-unit-g59-u4` (P17/P27; its inputs included another session's untracked `i.html`), matched to f988909b by the identical clean-worktree run `artifacts/verify/20261008-163946-089-unit-g59-u4-clean`.
- **2026-10-09 — G59 U3 (owned element parts):** a stored part below an owned Array element is borrowed through the element's own borrow (`ElementAccess.ElementPathBase`; the element reference keeps its Place Origin), so the original G59 forms, the former wrong-code forms (now 42) and element consumers (receivers, `match`, iteration, nested collections) execute natively, and the Array stays borrowed while the part borrow is live. Probes `artifacts/verify/20261008-g59-u0-baseline/u3-probes`; Unit `artifacts/verify/20261008-161218-344-unit-g59-u3` (P29/P31/P37/P40); full functional suite passed.
- **2026-10-09 — G82 U8, closed:** a generated replica matrix (`GenericDefaultTest.ReplicasAgreeWithTheirBody`: 8 snippets × 8 placements, 181 executed cells, 84 native at O0/O2) agrees with ordinary generic bodies; on the pre-repair compiler 114 cells fail. Warm default contexts allocate nothing (`WarmDefaultContextsAllocateNothing`, `Benchmark --generic-defaults`); `--local-regions`/`--callable-plans`/`--adaptation-plans` match 149e3c26 within run-to-run variation (`artifacts/benchmarks/g82-u8`). SETTLED records the inline-replica decision; the proposal moves to Changes. Final Session `artifacts/verify/20261008-152646-737-session-g82-final` (20,092 tests, 454 native executions, P20/P22); evidence `artifacts/verify/g82-u8`.
- **2026-10-09 — G82 U6–U7:** Callable receiver Loans, value calls and replica requirement arguments resolve declared Types in their operation's context; D3 and D6 accept with correct native output and the original input runs. All 94 U2 root-context sites are classified (`artifacts/verify/g82-u7/audit.md`); the formatting estimate stays conservative in replicas. Unit `artifacts/verify/20261008-150947-480-unit-g82-u6-u7b`; full functional suite 19,036 passed.
- **2026-10-08 — G82 U4–U5:** projection Types resolve in their locating operation's context; residual Pattern Types in their Subject's context, removing D5's spurious unknown destruction effect. D4 stays unreproduced (regressions kept). Unit `artifacts/verify/20261008-144552-037-unit-g82-u4-u5`.
- **2026-10-08 — G82 U3:** replicas resolve committed Copy-or-Move exactly under the context's proof environment (root default invocation); D1's fault and D2's generation failure are gone, and requirement calls on abstract Types stay forwarded. Unit `artifacts/verify/20261008-143945-675-unit-g82-u3`; full functional suite 19,022 passed.
- **2026-10-08 — G82 U0–U2:** baseline on 149e3c26 equals pre-C1 861d7b50 (`artifacts/verify/g82-baseline-149e3c26`): D1 faults, D2/D3 fail generation, D5 adds a spurious unknown destruction effect, D6 replicas report Unsupported, D4 does not reproduce. SPEC §7.2.3 states the instantiation rule (U1, b75a1de2). U2 resolves every declared Type/call through an explicit `InterpretationContext` with composed default substitution; a checked mode matched the chained form on 8,519 resolutions. Session `artifacts/verify/20261008-141839-285-session-g82-u2` (20,061 tests).
- **2026-10-08 — G59 U2 (use-derived capability):** a synthesized element access is representation, not a path layer; `AccessType(node, exclusive)` takes it exclusively when the real path grants Uniq. Members below Non-Copy elements of `uniq` Arrays and fixed-array references are written and exclusively borrowed as receivers (native O0/O2); `ref` paths still deny. Unit `artifacts/verify/20261008-140026-144-unit-g59-u2`; full functional suite 19,007 passed.
- **2026-10-08 — G59 U0–U1 (fail closed):** re-established the baseline on e1904fe8 (`artifacts/verify/20261008-g59-u0-baseline`: 37 probes, five new wrong-code receiver/Tuple forms, suite audit with no accepted snapshot dependence). Place borrows without a route now fail closed with one `UnsupportedOwnership_Kd` and its `feature` Reason; shared Copy snapshots are counted; emission rejects exclusive/address snapshots. Unit `artifacts/verify/20261008-135009-401-unit-g59-u1b`. Order agreed: G59 U2, G82, G59 U3–U7, G74.
- **2026-10-08 — G82 plan revision:** restructured the [G82 proposal](../../draft/Changes/2026-10-08%20G82%20Generic%20Default%20Context%20Repair%20Plan.md) around a §8.10 instantiation oracle, one explicit interpretation context per operation (composed substitution and proof environment), stage-separated resolvers, a behavior-preserving context unit before the repairs, generated context replicas and an inline-versus-instance default decision with measurable reopening triggers. It proposes one non-semantic SPEC §7.2.3 cross-reference through a separate intake unit. No implementation, SPEC or support change. Documentation-only diff/reference review; no builds, tests or native verification run.
- **2026-10-08 — G82 repair planning:** created the [generic-default context repair proposal](../../draft/Changes/2026-10-08%20G82%20Generic%20Default%20Context%20Repair%20Plan.md), combining operation-aware Type/acquisition APIs with ownership, effect, lowering and lifecycle regressions. Prior probe results and historical attribution remain subject to a clean baseline; no implementation or support change is claimed. Documentation-only diff/reference review; no builds, tests or native verification run.

<a id="associated-inference-completion"></a>
- **2026-10-08 — Bounded associated-Type inference (G84, U0–U4) complete:** spec intake `b53b3791` and shared staged headers, complete-Type identity, frozen declaration evidence, dependency invalidation and public diagnostics/Hover. Final Session `20261008-121525-798-session-associated-type-inference-final` also verifies the combined compiler unit: warning-free Release, 20,032 functional/allocation tests without failures or skips, 180 native O0/O2 executions, P19/P43, verified toolchain and stable inputs. Initial Session `20261008-120011-849-session-associated-type-inference` failed six tests; one receiver-diagnostic regression was repaired and five old-spec expectations were aligned with the adopted inference/complete-Type/result-mode rules. Feedback and both Sessions remain under `artifacts/verify/`.
- Fixed final measurements: `artifacts/benchmarks/associated-inference-20261008/measurements-final.json` and `inputs-final.json`; 48 explicit/inferred inputs, 240 zero-byte warm samples, inferred whole-Bind medians 6.77–8.20 ms. [Conditions and limits](../../src/Benchmark/AssociatedInference.md). No overall speedup claim; existing artifact, object receiver and general Origin boundaries remain in STATUS. The integrated proposal stays frozen and unchanged.

- **2026-10-08 — Verification workflow simplification:** a successful final Session may also verify the last Unit when source/configuration and all required checks match, retaining explicit native/milestone selections and commit association. Replaced the blanket non-compiler exception with verification by change type, aligned execution guidance, and corrected the CI description to main pushes only. Documentation-only diff/reference review; no builds, tests or native verification run. No language rules or support boundaries changed.

- **2026-10-08 — Diagnostic workflow scope:** moved the detailed workflow to [DIAGNOSTIC_WORKFLOW.md](DIAGNOSTIC_WORKFLOW.md), activated only by an explicit user request for the workflow or a detailed diagnostic review. Ordinary implementation now uses simple diagnostic information; implementation prompts and workflow references agree. Documentation-only diff and reference checks; no tests or native verification run. No language rules or support boundaries changed.

- **2026-10-08 — Developer document cleanup:** consolidated six guides and retired ARITHMETIC, RANGES_REVIEW and DIAGNOSTICS_REVIEW after preserving unresolved findings and immutable archive links. LLVM_OPTIMIZATION and prompts are unchanged; no specification, implementation or support boundary changed. Document checks: 302 links, incoming references, historical anchors and PLAN length. Final Unit `20261008-095311-605-unit-dev-doc-cleanup-unit-final`: 19 tests; final Session `20261008-095456-211-session-dev-doc-cleanup-session-final`: warning-free Release, 19,950 tests, zero diagnostic differences and stable inputs. Initial sandboxed Session `20261008-094612-649-session-dev-doc-cleanup-session` failed 39 tests, including file-access and process-start failures; its logs remain retained. The final runs used the same implementation/tests outside that sandbox. Review and backups: `artifacts/verify/dev-document-cleanup/`.

<a id="g55-g60-pause"></a>
- **2026-10-08 — G55/G60 paused by the user:** 24 verified units, `b6d3cb15`–`6736cb30`; both remain IN_PROGRESS. Resume only when instructed, using [G55_G60.md](G55_G60.md). Closing Unit `20261008-090610-959-unit-g55-g60-closing-regressions-final`: 1,653 tests, 210 native executions. Final Session `20261008-091149-266-session-g55-g60-pause-reviewed-final`: warning-free Release, 19,950 functional/allocation tests, 11,984 native O0/O2 executions, P19/P20/P21/P22/P26/P32, verified toolchain, zero diagnostic differences and stable inputs. Initial Session `20261008-085217-789-session-g55-g60-pause-final` failed seven tests; four shared-header regressions were repaired and three obsolete static-Origin expectations corrected. All failures remain retained. Parser observation: `artifacts/benchmarks/g55-g60-closing-parser`; no matched overall speedup claim.

<a id="g65-completion"></a>
- **2026-10-08 — G65 fixed-input scope complete:** adopted actual-Loan transport/last-holder recovery and shared ordinary call/storage flow. Final Unit `20261008-005735-486-unit-g65-shared-acquisition-reviewed`: 839 tests, 300 native executions, P20/P26/P27/P37, zero diagnostic differences. Final Session `20261008-010002-364-session-g65-shared-acquisition-final`: warning-free Release, 19,572 tests, no failures/skips and stable inputs; toolchain identity was not rechecked. Earlier stored-content, replacement and ancestry failures were repaired and retained. Fixed measurements `g65-regions-final-{before,after}-20261008.json` / `g65-fixed-final-20261008.json` under artifacts/benchmarks retain 399 zero-byte phase samples; no general speedup or linear-scaling claim. Independent boundaries remain in PLAN/STATUS.

<a id="arithmetic-completion"></a>
- **2026-10-08 — Arithmetic A0–A5 and P43 complete:** declaration/provider selection, literal fitting, common requirement Items, acquisition and all replacement paths, plus shared inline inherited conformance mappings. Final Session `20261007-231931-542-session-arithmetic-reviewed-final`: warning-free Release, 19,498 tests, 776 native O0/O2 executions, P22/P25/P26/P42/P43, verified toolchain, zero diagnostic differences and stable inputs. Failed feedback/Session runs remain retained. Matched `artifacts/benchmarks/arithmetic-plans-final-20261008.json` has 210 zero-byte samples; native results: `arithmetic-native/20261008-082816-final/summary.json`. Conditions: [Arithmetic.md](../../src/Benchmark/Arithmetic.md); no speedup claim. G78/G83, exclusive OCC, CSP and native i128/u128 division/remainder remain separate.
- The retired [A0–A5 plan and acceptance matrix](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/ARITHMETIC.md) are historical. Required behavior is in [arithmetic Contracts](../spec/arithmetic-contracts.md); current implementation/test entry points are in CODEMAP. The matrix covers Non-Copy operators, distinct Output, Left literal fitting, Origin-independent selection, direction conflicts, acquisition/retention, Property/index replacement and native 128-bit limits.

<a id="hover-verification"></a>
- **2026-10-07/08 — Hover and improvements:** the original implementation Session `20261006-173140-848-session-hover-final` passed 18,161 tests. Client verification: LSP 13, commands 21, selection 14, extension unit 65 with two platform skips (`artifacts/verify/hover-client-20261007/lsp.log`); this did not certify later presentation changes. Smart App Control then blocked the improved xUnit assembly (0x800711C7); the user authorized skipping execution after six feedback failures were repaired but not rerun. `20261007-170818-300-unit-hover-improvements-build-only` is build-only evidence. The later Virtual Session `20261007-183827-283-session-virtual-remaining-session-repaired` passed all 19,318 tests and supersedes that automated-test gap. Actual-client visual review remains user-owned, awaits their report, and must not be automated. The earlier display attempt stopped before review. [Policy, measurement conditions and observations](HOVER.md).

- **2026-10-07/08 — Virtual shared paths:** C1/C2, inherited slot contracts, effects, shared ownership/dispatch, conditional/generic generation, source access and Hover were verified in individual units. Remaining-plan Session `20261007-183827-283-session-virtual-remaining-session-repaired`: 19,318 tests, 428 native executions, P25/P34/P37 and zero diagnostic differences; earlier stale access expectations and generic Advice defects were repaired, with failures retained. Cleanup Session `20261007-190218-731-session-virtual-cleanup-session`: 19,317 tests, 432 native executions and zero differences. Fixed Callable/inference/object/adaptation measurements retain warm zero-allocation samples, without a speedup claim. The broader V1–V6 plan remains open (PLAN G81/G83).
- **2026-10-07 — Callable/local regions and constructor inference:** U1–U10 shared length/Origin inference, Place/function-value paths, compiler adapters and snapshot invalidation; constructor/common inference U0–U6 shared slot mapping, selection and fixed-binding validation. Detailed units and intermediate support boundaries are retained in archive lines 315–343. G65 was subsequently completed only within the scope above.
- **2026-10-06 — G77/G72/G70 complete:** source `89da9e02`, Session `20261006-050738-516-session-g77-g72-g70-verified` passed warning-free Release, 17,760 tests and 962 related native executions. Earlier Sessions exposed stored-Loan, default-expectation and allocation failures; repairs and traces remain retained. Fixed Callable/object measurements allocate zero under unchanged conditions. Remaining expression/ancestry limits stay in STATUS.

## Retired audit records

<a id="diagnostics-checkpoints"></a>
- **2026-09-30–10-03 — D0–D5 / R1–R7:** D4 evaluated an 80-case corpus, D5 a 202-case corpus and 77 syntax forms, then R1–R7 added structured repairs, check JSON and LSP quick fixes. These are dated checkpoints, not exhaustive conformance certificates. Stage details and measurements remain in the [archived diagnostics evaluations](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/DIAGNOSTICS.md#9-evaluation) and [completed plan tables](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/PLAN.md#diagnostics-track-complete). Final Repair Session: `20261002-174643-505-session-repair-track-session`. Manual VS Code Quick Fix inspection remains unverified; omitted capture-list and check-test-unit candidates were outside that scope. Current recorder rules and unresolved findings remain in DIAGNOSTICS.

<a id="diagnostics-audit"></a>
- **2026-09-30 — Diagnostic conformance audit:** repaired prerequisite aggregation, source order, catalog/fault validation, input failures, duplicate evidence and bounded excerpts; second-review repairs are in `f4a9c43b`. The [full audit](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/DIAGNOSTICS_REVIEW.md) preserves the specification map, public-output review, failed runs and exact measurements. Session `20260930-122850-706-session-diagnostics-audit-final` passed its build, 13,795 managed tests and snapshot but **failed** milestone 12's parallel native O2 build with an access error; its unchanged isolated O0/O2 rerun passed. The earlier Session `20260930-121855-044-session-diagnostics-audit-complete` similarly failed milestone 2 before a passing isolated rerun. Neither Session is relabeled successful; the cause was unestablished. Excerpt allocation became 2,104 B/finalization for both tested prefix sizes; warm rebind/report stayed 120 B, without a general throughput claim. Residual Note-omission and recorder findings are retained in [DIAGNOSTICS §11](DIAGNOSTICS.md#11-audit-findings-d1).

<a id="ranges-audit"></a>
- **2026-09-30 — Ranges audit complete:** `ae55e615` completed the proposal/specification audit and diagnostic repairs; `d7ba81f5` / `f4a9c43b` added the second-review corrections. Final Unit `20260930-112258-944-unit-ranges-review-unit`: 393 tests, 122 native executions and P41; Session `20260930-112705-566-session-ranges-review-complete`: 13,755 tests and all 32 then-complete O0/O2 harnesses. Second Session `20260930-142114-408-session-second-review-session`: 13,834 tests; `20260930-142414-817-unit-second-review-milestones` covered all 32 harnesses. The [full audit](https://github.com/archi-Doc/Kimigayo/blob/2834c7bf41d458a4fc1744fb9466ad03eccc2e5a/docs/dev/RANGES_REVIEW.md) preserves the scope matrix, failed build/native attempts and toolchain repair. This closed the earlier paused audit; the frozen proposal was unchanged. Its four residual findings are explicitly tracked in [DIAGNOSTICS §11](DIAGNOSTICS.md#11-audit-findings-d1), with current support in STATUS. No new timing claim.

## Earlier session index

Each entry points to its full session record by line in the immutable source snapshot linked above. Retained anchors continue to identify the same historical events; the index does not replace their verification details.

| Date | Session / outcome recorded at that checkpoint | Archive line |
| --- | --- | --- |
| 2026-10-07 | Accepted review repairs (`97faa986`–`8747f68c`) | L67 |
| 2026-10-07 | 60-minute P25 session, IN_PROGRESS | L68 |
| 2026-10-07 | 60-minute P24 session, DONE | L69 |
| 2026-10-06 | G72 Semantics cases | L70 |
| 2026-10-06 | G72 general expressions | L71 |
| 2026-10-06 | G72 first unit | L72 |
| 2026-10-06 | G77 | L73 |
| 2026-10-06 | <a id="g75-closure"></a>G75 DONE (U6 closure) | L74 |
| 2026-10-06 | G75 U0–U3b | L75 |
| 2026-10-06 | G75 plan C (planning only) | L76 |
| 2026-10-06 | G76 resumed and completed | L77 |
| 2026-10-06 | G75 planning only | L78 |
| 2026-10-05 | <a id="p26-completion"></a>P26 DONE by user decision, with recorded gaps | L79 |
| 2026-10-04 | P26 (18:32:33–19:32:33 JST) | L80 |
| 2026-10-04 | U5 (17:12:28–18:12:28 JST), then independent P26 | L81 |
| 2026-10-04 | Review repairs (started 15:04:34 JST) | L82 |
| 2026-10-04 | P26 (13:44:06–14:44:06 JST) | L83 |
| 2026-10-04 | U4 (12:09:56–13:09:56 JST), then independent P26 | L84 |
| 2026-10-04 | P26 (10:47:33–11:47:33 JST) | L85 |
| 2026-10-04 | Review follow-up | L86 |
| 2026-10-04 | Shared object U4 (05:29:40–06:29:40 JST) | L87 |
| 2026-10-04 | P26 review follow-up | L88 |
| 2026-10-04 | P26 (02:03:34–03:03:34 JST) | L89 |
| 2026-10-04 | Shared object U3, then P26 (00:53–01:53 JST) | L90 |
| 2026-10-04 | P40 completion, then shared object U1–U2 (23:44–00:44 JST) | L91 |
| 2026-10-03 | Shared object ownership (rc/arc) plan and U0 | L92 |
| 2026-10-03 | Lexing and parsing review (`src/Kimi/Compiler/Lexing`, `Parsing`) | L93 |
| 2026-10-03 | Decisions G54 and G58, Program 40 | L94 |
| 2026-10-03 | Session 10 of 10 (15:35–16:35 JST, units ended 16:07), P26 | L95 |
| 2026-10-03 | Session 9 of 10 (14:59–15:59 JST, units ended 15:30), P26 | L96 |
| 2026-10-03 | Session 8 of 10 (14:24–15:24 JST, units ended 14:47), P26 | L97 |
| 2026-10-03 | Session 7 of 10 (14:06–15:06 JST, units ended 14:20), P26 | L98 |
| 2026-10-03 | Session 6 of 10 (13:23–14:23 JST, units ended 14:00), P26 as independent work while P31 waits (G58) | L99 |
| 2026-10-03 | P31 session 5 of 10 (12:47–13:47 JST, units ended 13:16) | L100 |
| 2026-10-03 | P31 session 4 of 10 (12:12–13:12 JST, units ended 12:41) | L101 |
| 2026-10-03 | P31 session 3 of 10 (11:28–12:28 JST, units ended 12:06) | L102 |
| 2026-10-03 | P31 session 2 of 10 (10:48–11:48 JST, units ended 11:24; the remaining G56/G57 items needed more than the window) | L103 |
| 2026-10-03 | P31 session 1 of 10 (09:53–10:53 JST, units ended 10:42) | L104 |
| 2026-10-03 | Binding review (`src/Kimi/Compiler/Binding`, 07:50–09:55 JST) | L105 |
| 2026-10-03 | P31 G44 and G43 (60-minute window, 06:54–07:31 JST) | L106 |
| 2026-10-03 | Accessor receiver shapes, parameter acquisition shape, structured repair candidates | L107 |
| 2026-10-02 | G50, misplaced attributes in function headers (user choice: recovery only, no SPEC adjacency rule) | L108 |
| 2026-10-02 | VS Code syntax highlighting (kimi-ext 0.0.11) | L109 |
| 2026-10-02 | G49, integer-only operators on floats | L110 |
| 2026-10-02 | Parsing review (`src/Kimi/Compiler/Parsing`) | L111 |
| 2026-10-02 | String operators removed (review item 5) | L112 |
| 2026-10-02 | Checking review (`src/Kimi/Checking`) | L113 |
| 2026-10-02 | Analysis review (`Compiler/Analysis`) | L114 |
| 2026-10-02 | G45, raw Place borrows under their fresh anchor (SPEC 5.2.2) | L115 |
| 2026-10-02 | LSP quiet period 1000 ms | L116 |
| 2026-10-02 | Header line recovery (DIAGNOSTICS §4.4, §11) | L117 |
| 2026-10-02 | Require recovery and `&&`/`\|\|` (DIAGNOSTICS §4.3, §4.4, §11) | L118 |
| 2026-10-02 | Effect bound refinements, declared Origin slots, raw pointers and the unsafe boundary | L119 |
| 2026-10-02 | Implicit receiver retained-Loan conflict reported once (DIAGNOSTICS §10, §11) | L120 |
| 2026-10-02 | Excluded syntax, borrow acquisition and effect bounds | L121 |
| 2026-10-01 | String literal spans (DIAGNOSTICS §4.4, §10) | L122 |
| 2026-10-01 | P31 owned stored references, 60-minute window (20:56–21:36 JST) | L123 |
| 2026-10-01 | Time integration with dev | L124 |
| 2026-10-01 | Diagnostics D5 S4, evaluation and closure | L125 |
| 2026-10-01 | Diagnostics D5 S3 unit 3, closure | L126 |
| 2026-10-01 | Diagnostics D5 S3 unit 2, parser cause propagation | L127 |
| 2026-10-01 | Diagnostics D5 S3 unit 1, Binding cause propagation | L128 |
| 2026-10-01 | Diagnostics D5 S2 unit 4, legacy codes removed | L129 |
| 2026-10-01 | Diagnostics D5 S2 unit 3, patterns, compile-time directives, Types, expressions, Origin clause positions and qualified Names | L130 |
| 2026-10-01 | Diagnostics D5 S2 unit 2, accessors, parameters, arguments and reader helpers | L131 |
| 2026-10-01 | Diagnostics D5 S2 unit 1, declarations and modifiers | L132 |
| 2026-10-01 | Diagnostics D5 S1, syntax forms (DIAGNOSTICS §4.4) | L133 |
| 2026-10-01 | G42, conversion operands are read positions (SPEC §3.5.3, §13.5.2) | L134 |
| 2026-10-01 | Windows/Time and scalar statics | L135 |
| 2026-10-01 | Time verification | L136 |
| 2026-10-01 | P42 Session | L137 |
| 2026-10-01 | P42 U11, hash/generator timing (P42 DONE) | L138 |
| 2026-10-01 | P42 U10, Program 42 | L139 |
| 2026-10-01 | P42 U9, literal-operand check omission (impl §21.5.3) | L140 |
| 2026-10-01 | P42 U8, exact direct-literal conversions | L141 |
| 2026-10-01 | P42 U7, the bit conversion `@bits<U>` | L142 |
| 2026-10-01 | P42 U6, the wrapping conversion `@wrap<U>` | L143 |
| 2026-10-01 | P42 U4+U5, literals, patterns, keys and generic `Wrapping<T>` | L144 |
| 2026-10-01 | P42 U2+U3, the wrapping integer Scalar | L145 |
| 2026-10-01 | P31 unit close | L146 |
| 2026-10-01 | P42 intake and integer results | L147 |
| 2026-10-01 | P31 source dispatch, 60-minute window | L148 |
| 2026-10-01 | Review repairs, 90-minute unit window | L149 |
| 2026-10-01 | P31 source migration, 90 minutes | L150 |
| 2026-10-01 | Next 90-minute P31 session | L151 |
| 2026-10-01 | 90-minute P31 session | L152 |
| 2026-10-01 | Review fixes | L153 |
| 2026-10-01 | 60-minute compiler implementation | L154 |
| 2026-09-30 | Unterminated-string diagnostics | L155 |
| 2026-09-30 | Ranges B and diagnostics second review | L156 |
| 2026-09-30 | kimi-ext run default | L157 |
| 2026-09-30 | Diagnostic conformance review | L158 |
| 2026-09-30 | Ranges audit completed (`ae55e615`) | L159 |
| 2026-09-30 | Ranges audit checkpoint, stopped at the user's request: repaired branch value reads, parameter-identity equivalence/capability propagation and width-aware position warnings | L160 |
| 2026-09-30 | Compiler navigation | L161 |
| 2026-09-30 | Verification workflow | L162 |
| 2026-09-30 | Contextual labels and drop DONE | L163 |
| 2026-09-30 | Diagnostics track D2b–D4 | L164 |
| 2026-09-30 | Draft lifecycle policy | L165 |
| 2026-09-30 | Contextual-label proposal revision | L166 |
| 2026-09-30 | P41 completion follow-up | L167 |
| 2026-09-30 | Verification performance | L168 |
| 2026-09-30 | P41 positions and ranges (DONE) | L169 |
| 2026-09-29 | Release-first verification | L170 |
| 2026-09-29 | P29 reopened and P28 completed | L171 |
| 2026-09-29 | Implementation review follow-up | L172 |
| 2026-09-29 | P28 generic bound references (90-minute window 10:27–11:57 JST; first unit 10:28) | L173 |
| 2026-09-29 | P28 G28 item transfers (90-minute window 08:51–10:21 JST; first unit 08:53) | L174 |
| 2026-09-29 | P28 bound-reference disambiguation (60-minute window 03:59–04:59 JST; first unit 04:03) | L175 |
| 2026-09-29 | P28 Dictionary `for`, G28 and fixed-array storage (60-minute window 03:07:07–04:07:07 JST; first unit 03:07) | L176 |
| 2026-09-29 | P28 Dictionary storage boundary (60-minute window 02:11:01–03:11:01 JST; first unit 02:14) | L177 |
| 2026-09-29 | P28 declaration validation and Slice costs (60-minute window 01:23:49–02:23:49 JST; first unit 01:31) | L178 |
| 2026-09-29 | Non-iterable `for` subjects (G31 resolved) | L179 |
| 2026-09-29 | Integer ranges | L180 |
| 2026-09-28 | Extension notifications 0.0.9 | L181 |
| 2026-09-28 | Linux CI repairs | L182 |
| 2026-09-28 | Extension packaging 0.0.8 | L183 |
| 2026-09-28 | LSP verification access | L184 |
| 2026-09-28 | Build entry points | L185 |
| 2026-09-28 | Temporary work and retained artifacts | L186 |
| 2026-09-28 | Repository layout | L187 |
| 2026-09-28 | VS Code extension integration | L188 |
| 2026-09-28 | P28 declaration contracts (60-minute window 15:25:23–16:25:23 JST; first unit 15:28) | L189 |
| 2026-09-28 | P28 owning source (60-minute window 14:05:45–15:05:45 JST) | L190 |
| 2026-09-28 | P28 source storage (60-minute window 12:30:02–13:30:02 JST; first unit 12:31) | L191 |
| 2026-09-28 | LSP follow-up review | L192 |
| 2026-09-28 | Test consolidation | L193 |
| 2026-09-28 | LSP review | L194 |
| 2026-09-28 | Compiler services (SPEC Chapter 23) | L195 |
| 2026-09-27 | Review follow-up (paused) | L196 |
| 2026-09-27 | P28 block 2/2 (60 minutes from 20:32:06 JST; first unit 20:33) | L197 |
| 2026-09-27 | LSP diagnostics implementation plan | L198 |
| 2026-09-27 | P28 block 1/2 (60 minutes from 19:59:19 JST; first unit 20:01) | L199 |
| 2026-09-27 | P28 region splitting (block from 18:58:37 JST) | L200 |
| 2026-09-27 | Review remediation (from 16:16:58 JST, at the user's request) | L201 |
| 2026-09-27 | P28 Origin effects, stopped early at the user's request | L202 |
| 2026-09-27 | P28 Iterator independence (60-minute window 14:06:50-15:06:50 JST; first unit 14:09) | L203 |
| 2026-09-27 | P28 borrowing entries (60-minute window 12:52:20-13:52:20 JST; first unit 12:56) | L204 |
| 2026-09-27 | P28 formation domains (60-minute window 11:37:08-12:37:08 JST; first unit 11:39:16) | L205 |
| 2026-09-27 | P28 associated Origins (60-minute window 10:21:24–11:21:24 JST; first unit 10:23:59) | L206 |
| 2026-09-27 | P28 owned user iteration (60-minute window 09:07:22–10:07:22 JST) | L207 |
| 2026-09-27 | <a id="p39-completion"></a>P39 DONE, conditional members (60-minute block from 07:28 JST) | L208 |
| 2026-09-27 | <a id="p39-pair-layers"></a>P39 pair layers, Array members and P37 DONE (300-minute block from 01:58 JST) | L209 |
| 2026-09-27 | <a id="programs34-39-authoring"></a>Library style and Programs 34–39 authored | L210 |
| 2026-09-26 | <a id="follow-copy-split"></a>Follow, Copy, syntax cleanup and the implementation split | L211 |
| 2026-09-26 | <a id="spec-implementation-review"></a>SPEC and implementation review | L212 |
| 2026-09-26 | <a id="places-followup"></a>Places follow-up decisions and fixes | L213 |
| 2026-09-26 | <a id="places-borrowing-iteration"></a>Places, borrowing and iteration | L214 |
| 2026-09-25 | Storage Provision draft revision | L215 |
| 2026-09-25 | Storage Provision design draft | L216 |
| 2026-09-24 | <a id="subject-rule-alignment"></a>Subject rule alignment (user-selected) | L217 |
| 2026-09-24 | <a id="p23-completion"></a>P23 DONE | L218 |
| 2026-09-24 | <a id="p31-kimigayo-library"></a>P31 / Dictionary in Kimigayo | L219 |
| 2026-09-24 | <a id="review-remediation"></a>Review remediation | L220 |
| 2026-09-24 | <a id="programs31-33-authoring"></a>Programs 31 and 33 authored | L221 |
| 2026-09-24 | <a id="p30-session1"></a>P30 session 1 | L222 |
| 2026-09-24 | Text implementation policy | L223 |
| 2026-09-24 | <a id="programs25-28-authoring"></a>Programs 25–28 authored | L224 |
| 2026-09-24 | Implicit exclusive receivers and ObjectPayload | L225 |
| 2026-09-24 | <a id="program32-completion"></a>UTF-8 formatting integration and P32 DONE | L226 |
| 2026-09-23 | Borrow Origin suffix | L227 |
| 2026-09-23 | P29 session 7 (60-minute run, 17:21–18:21 JST) | L228 |
| 2026-09-23 | P29 session 6 (second requested 60-minute run) | L229 |
| 2026-09-23 | P29 session 5 (first requested 60-minute run) | L230 |
| 2026-09-09 – 09-16 | Parser, Koto syntax and reload | L231 |
| 2026-09-17 | Completion plan with M1–M15 / I1–I34 (units 1–32) | L232 |
| 2026-09-18 | Ownership joins and terminal guards (units 33–67) | L233 |
| 2026-09-19 | Program 16 | L234 |
| 2026-09-20 | Independent Documentation Markdown parser and product switch (DM1–DM6) | L235 |
| 2026-09-21 | Origin system redesign integrated | L236 |
| 2026-09-22 | Named argument boundary | L237 |
| 2026-09-22 | Restructuring: initial profile monomorphizes and generic code sharing is deferred (§21.3.1, Appendix D) | L238 |
| 2026-09-22 | P22 probe (reverted, no code commit): lowering a verified generic body with substituted Place Types and signature Types through the ordinary `BodyLowering` fails for every existing generic fixture, because the verified plan itself depends on the Types (`CopyOrMove` acquisitions, `ScalarResult` value flow such as Phi vs. slot arrivals) | L239 |
| 2026-09-22 | P22 units `131c1b38` (per-substitution ownership analysis and concrete instance lowering | L240 |
| 2026-09-22 | P22 units `7f74795c` (forwarded generic calls inside instances bind to the callee's instance entries), `a0c5dfa3` (committed CopyOrMove payload acquisitions resolve per instance) and `08c9da68` (owned result joins store substituted result slots). 11,324 tests per configuration | L241 |
| 2026-09-22 | Harness variant fixes (user-approved): milestone 5 `ImmediateTemporary` and `MissingStoredOrigin` use the current `ref{origin}/T` syntax, milestone 16 `Values` replaces the current `Cell.init(n)` | L242 |
| 2026-09-22 | Optional Types, try and explicit discard integrated into the formal specification and compiler (`11672349`, `2c31111f`, `fd61538c`) | L243 |
| 2026-09-23 | P22 instance shapes, seven units: `14a9b61e` (borrowed-array element borrows as bounds-checked `ArrayAddress`), `1bd8197a` (forwarded string references | L244 |
| 2026-09-23 | P22 mandatory instances, six units: `c3157b72` (Pattern Types inside instances), `0233fc25` (Callable receiver Loans under the instance substitution), `22d4377a` (CopyOrMove Pattern bindings per instance), `ad35b654` (selected specializations called directly | L245 |
| 2026-09-23 | P22 shared-path retirement, three units: `31ecddd7` (every template concrete-only | L246 |
| 2026-09-23 | <a id="program22-completion"></a>Program 22 complete | L247 |
| 2026-09-23 | P20 started: Program 20 binds except its five `*inferred == 10` checks (SPEC §3.3/§13.2 define no safe `*` | L248 |
| 2026-09-23 | <a id="program20-completion"></a>Program 20 complete | L249 |
| 2026-09-23 | Review follow-ups and P29 session 4 | L250 |
| 2026-09-23 | P29 session 3 (60 min) | L251 |
| 2026-09-23 | P29 session 2 (60 min) | L252 |
| 2026-09-23 | P21 session 1 (60 min) | L253 |
| 2026-09-23 | Explicit transfer and exclusive borrow (lending rule) | L254 |
| 2026-09-23 | <a id="p22-cleanup"></a>P22 cleanup and the remaining approved improvements | L255 |
| 2026-09-22 | Optional/try design record (§8, non-normative): keep operand inference expectation-free to avoid partial expectations and overload retries | L256 |

## Retained anchors

The following anchors preserve earlier links. Full records remain in the source snapshot and, for pre-compaction entries, in commit 32324537.

<a id="programs22-24-authoring"></a>
- **2026-09-22 — Programs 22–24 authored** — archive L258.

<a id="program19-completion"></a>
- **2026-09-22 — Program 19 complete** — archive L266.

<a id="program14-completion"></a>
- **Program 14** — archive L273.

<a id="program15-completion"></a>
- **Program 15** — archive L276.

<a id="program16-completion"></a>
- **Program 16** — archive L279.

<a id="program17-completion"></a>
- **Program 17** — archive L282.

<a id="program18-completion"></a>
- **Program 18** — archive L285.

<a id="units62-67-verification"></a>
- **Units 62–67 audit** — archive L288.

<a id="programs38-restructure"></a>
- **38-program roadmap** — archive L291.

<a id="programs18-20-design"></a>
- **Programs 18–20 authoring** — archive L294.

<a id="documentation-markdown-product-switch-20260920"></a>
- **Documentation Markdown product switch** — archive L297.

<a id="p27-completion"></a>
- **P27 completion** — archive L300.

P26 completion on 2026-10-05 was an explicit user decision waiving PLAN §5 condition 4 for the recorded gaps; it did not certify neighboring unsupported forms. G75 later closed its adopted U0–U6 scope, leaving the residual boundaries recorded in PLAN/STATUS.
