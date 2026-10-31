# Verification workflow

## Continuous integration

`.github/workflows/test.yml` runs only on pushes to `main`, including merge updates, as recorded in [SETTLED.md](../SETTLED.md#running-full-ci-before-integration-into-main). Ubuntu checks the
managed solution with warnings as errors. Windows uses the ordinary Session verifier for all functional/allocation
regressions and every regenerated native fixture at O0/O2, then runs the native-worker PowerShell regressions, CLI
integration, and extension unit/integration tests with the managed compiler. Neither job runs NativeAOT.

The Windows job downloads the adopted LLVM archive only on a toolchain-cache miss, checks its pinned SHA-256, and calls
the existing backend setup script. Explicit toolchain verification checks cached installations too. Update the archive
version and digest together with an adopted profile change. Logs, source manifests and result records are uploaded even
when a later step fails. A configured lane is not evidence of a successful hosted run; inspect that run's results.

## Verification scope

Select checks by the files and behavior changed. Mixed changes combine the applicable rows; documentation accompanying
an executable change does not make that change documentation-only.

| Change | Completion checks |
| --- | --- |
| Documentation only | Review diffs and affected references. Do not run builds, tests, native fixtures or milestone harnesses. |
| Compiler, Kimi library or compiler tests | Unit verification of the related checks (library changes add `KimiLibraryShapeTest`) with relevant native fixtures and milestones, all selected explicitly, plus the common `ArchitectureRulesTest` that Verify adds; a successful Session at the end of the working session. |
| Benchmark or Playground code | Session verification, including the whole-solution build, before completion; execute the affected scenario or relevant fixed-condition measurements. |
| Verification scripts, CI or other tooling | Related script regressions and affected build/execution paths. Native-runner changes require `NativeFixtureRunnerTest.ps1`. Changes affecting the managed build or test pipeline also require Session. A local check does not certify a hosted CI run. |
| VS Code extension, including its build/distribution scripts | Follow [the extension instructions](../../src/kimi-ext/AGENTS.md). |

Keep the selected checks and their results in the commit and `artifacts/verify/`; do not run unrelated suites merely
because a non-documentation file changed. Performance changes retain the measurement requirements below. NativeAOT
still requires explicit instruction.

## Code rules

The Code Rules Analyzer, its configuration/baseline and CompilerSizeBudgetTest were removed in
`2e341155`. DECISIONS CR-R1 retires their Report, SARIF-marker and commit-approval workflow.
`verify.ps1` still builds nonincrementally with warnings as errors, validates every requested
test selector against the new assembly, rejects zero executed tests and a requested selector
that executed none, and checks source/NuGet input stability. `verify-commit.ps1` still
requires a successful run and exact committed inputs.
`ArchitectureRulesTest` retains its existing no-growth assertions and joins every Unit selection.

The structure, dependency and vocabulary rules in COMPILER_ARCHITECTURE remain review
obligations; code size has no caps (DECISIONS Q5). Report net size changes and exceptions in
commits/artifacts; do not claim automated coverage for the removed analyzer.

## Feedback and completion

During edits, build the test project incrementally and run the relevant methods. These commands do not establish warning-free completion:

```powershell
dotnet build tests/xUnitTest/xUnitTest.csproj --no-restore -c Release
dotnet tests/xUnitTest/bin/Release/net10.0/xUnitTest.dll -method XunitTest.FunctionCodegenTest.OnlyRuntimeReachedBlocksAreGenerated -failSkips
```

Choose an existing method from the test source or runner discovery (`-list methods`). For fast functional feedback within a class, use `-class <class> -trait- Purpose=Allocation`. Direct runner selections of a moved allocation method use `<outer-class>+AllocationTests.<method>`.

Before a formal run, Verify's build flags catch analyzer and StyleCop failures in minutes; the result is feedback, not evidence:

```powershell
dotnet build tests/xUnitTest/xUnitTest.csproj --no-restore --no-incremental -c Release -warnaserror -p:EmitCompilerGeneratedFiles=false
```

At Unit completion, `scripts/verify.ps1` builds the test project and its Kimi dependency non-incrementally with warnings as errors. At Session completion it builds the entire solution, including Benchmark and Playground, then runs all managed regressions. Both modes retain the installed build analyzers and warnings-as-errors policy. Changes to Benchmark or Playground need the Session build before completion. Neither mode runs NativeAOT.

The build uses parallel project builds and the shared compiler server; the script regressions run while it compiles. Tests run up to eight collections at once (`-TestParallel`). The test host uses Server GC without dynamic heap-count adaptation and with a 64 MB gen0 budget per heap (`xUnitTest.csproj`): under the workstation GC, raising the threads from 4 to 14 made each test 2.7 times slower, so the run shrank only from 756 s to 622 s.

```powershell
./scripts/verify.ps1 -Class DeferredEmissionTest -Fixtures 'Deferred*.ll'
./scripts/verify.ps1 -Class XunitTest.DeferredEmissionTest -TestPurpose Allocation
./scripts/verify.ps1 -Mode Session
```

Unit Verify adds the common compiler check `ArchitectureRulesTest` to every class or
method selection, once; the purpose filter applies to them too. Purpose-filtered runs such as the allocation example
supplement, rather than replace, the remaining required checks. Commit each unit after its checks pass, associate the
evidence (below) and push; run the final Session at the end of the working session. Ordinary Verify does not check
native-toolchain identity: check it at setup/update or with `-VerifyToolchain`. CLI tests use `run --no-build` for
built CLI artifacts, except when testing automatic builds.

For the last unit, a successful Session can replace a separate Unit run if it verifies the same source/configuration
and covers every required Unit check. Session runs all managed regressions, but native fixtures and milestones still
need explicit `-Fixtures` and `-Milestone` arguments. Include any required diagnostic snapshot or other additional checks
as well; external checks remain required. An unselected check is not covered merely because the mode is Session.

Prepare implementation, tests and affected documentation first, run the combined final verification, then commit,
associate the evidence as described below and push. Record that the Session also satisfied the final Unit. Do not repeat
either mode solely to obtain a second mode label. Missing checks must pass against the same inputs; changes to covered
inputs invalidate their evidence and require reverification. PLAN/history bookkeeping retains the manifest exception below.

Verify accepts both `ClassName` / `ClassName.Method` and their fully qualified forms; short names use the repository's `XunitTest` namespace. A wildcard `*` may occur at the beginning or end. Invalid pattern syntax is rejected before building. Verify expands outer selections to include nested `AllocationTests`, so focused selections retain their allocation coverage.

After the build, Unit verification discovers methods from that exact assembly, checks **every** requested class and method, and records the effective selection in `selected-methods.json` before running tests. A misspelled selector cannot be hidden by another selector that matches. Class selections are ORed, method selections are ORed, and those two groups intersect; the runner also applies the purpose filter. The common checks join each nonempty group, as `Class.*` among methods, so they add to the selection rather than narrowing it. Discovery never checks an older assembly against newly edited test sources. Both zero-method and zero-executed-test selections fail. After the run, every requested class and method must also match a test the run executed, read from its per-test results (`tests-*.xml`), even when the common checks or another selector execute tests: a selector fails when it matches only Explicit methods, which discovery lists and xUnit reports Not Run, or when the class/method intersection or the purpose filter leaves it no test.

`-Pipeline mir` (R3–R6; default `legacy`) sets `KIMI_PIPELINE` for every compiler process of the run, the tests, fixtures and milestone harnesses, and records the pipeline in `summary.json`. From R6-gen's G2 every pipeline publishes the same records (DECISIONS R5-M156), so `-Pipeline mir` adds only the difference ledger's observation and the identity mark, and the mir lane runs only as the ledger's run below (DECISIONS R5-M152, OI-3 (a)). MIR code generation is the one code generator: `-Codegen` and `KIMI_CODEGEN` went with the selector at R6-gen's G4 (DECISIONS R5-M160).

R4's MIR-builder merges run the default checks and select the affected native families and O2 kernels (DECISIONS R5-M8, ask A-10).

The MIR difference ledger (R4–R6, DECISIONS R4-M17) is recorded by the test assembly. `KIMI_MIR_DIFF=<directory>` makes its fixture `MirDifferenceLedger` compare every mir-pipeline compilation once per Binding pass and append raw rows and sources to that directory, which must be fresh (the aggregation reads every `raw-*.tsv` there), aggregated into `ledger.tsv` and `summary.md` (plan §8's metrics: the families landed on the MIR, the SPEC examples of `tests/diagnostics/spec-cases.json` checked there, and the open blocking rows, DECISIONS R4-M47) when the run ends; `KIMI_MIR_LEDGER=<directory>` with `MirDifferenceTest.RequestedLedger` aggregates a directory's raw rows again. The mir measurement of a unit runs the Functional suite under `KIMI_PIPELINE=mir` with `KIMI_MIR_DIFF` (tier 1 is `MirDifferenceTest.RequestedCorpus`, tier 2 the rest of the suite) and the `Purpose=Allocation` tests under mir without it, since the observer allocates; the Functional failure set must equal a mir run without the ledger. The ledger of a unit is kept under `artifacts/verify/mir-diff/<unit>/`.

A unit that the A5 step 2 plan marks +cen (DECISIONS R4-M74, OQ1) also runs the census test that the plan's unit 2x-T1 adds (its prerequisite, plan §4): no problem of any MIR analysis in the library and the 44 milestone programs (DECISIONS R4-M12), the 31 `tests/mir` goldens or the accepting `tests/diagnostics/spec-cases.json` variants. The census is `MirCensusTest`, which the normal suite runs and `-Class MirCensusTest` selects; it asserts no validator finding and no A5 problem in any program of those sets (R4-M12), and no MIR construction record and no problem of any analysis in those Binding accepts (R4-M47), and `KIMI_MIR_PROBLEMS=<directory>` makes it write each set's problems there as `census-<set>.tsv`.

`-TestPurpose All` is the default; `Functional` and `Allocation` restrict deliberate Unit checks. Session rejects either restriction, and class or method selections. Select relevant native fixtures and milestones explicitly as before. Selector rules, the common checks and optional allocation companions are regression-checked by `./tests/scripts/VerificationSelectionTest.ps1`.

## Source identity and commit association

Verify writes `inputs-before.json` and `inputs-after.json`. The manifest covers tracked and nonignored untracked files, including new source files, configuration, scripts and specification documents. It excludes `draft/` (not authoritative compiler input), `docs/dev/PLAN.md` and `PLAN_HISTORY.md` (post-verification bookkeeping); ignored build outputs are excluded. Restored `obj/project.assets.json` and NuGet-generated project props/targets are hashed separately. Changed, added or deleted inputs between the two observations fail the run, even if its tests passed. Keep the no-source-edits rule: endpoint comparison does not detect a transient edit that is reverted between observations.

Each source entry records SHA-256 of the actual bytes and the Git-normalized blob identity. After committing a unit verified by Unit or Session Verify, associate its evidence with the commit before pushing:

```powershell
./scripts/verify-commit.ps1 -Evidence artifacts/verify/<run> [-Commit HEAD]
```

The association requires a successful Verify summary and exact path/blob equality with the committed tree under the same manifest policy; missing untracked inputs and additional committed sources fail. `commit-match.json` records the full commit and manifest hash. Git line-ending/filter normalization is explicit: it is not a claim that a checkout's raw bytes equal a commit's bytes. Later PLAN/history bookkeeping can share the same verified source manifest. The manifests are evidence, not an isolated or hermetic build: SDK/runtime, external package contents and tools outside the repository remain external; use `-VerifyToolchain` for the separately recorded native-toolchain identity check. Manifest and commit-matching regressions run with `./tests/scripts/VerificationInputsTest.ps1`.

## Behavior-preserving commits

A restructuring commit (AGENTS, Implementation Design) shows that behavior is unchanged against the run of the commit
before it, on the same inputs and configuration:

- the diagnostic snapshot: `-DiagnosticSnapshot -DiagnosticBaseline <previous run>/diagnostic-snapshot.json`, which must
  report no difference;
- the semantic snapshot: `SemanticSnapshotTest` with `KIMI_SEMANTIC_SNAPSHOT=check` against the previous baseline
  (`KIMI_SEMANTIC_BASELINE`), with `KIMI_SEMANTIC_HIR=1` and `KIMI_SEMANTIC_MIR=1` when Binding or MIR code changes;
- the generated IR: `./scripts/compare-fixtures.ps1 -Baseline <previous run> -Candidate <run>` reports the `.ll` files
  under `fixtures-<Configuration>`, their expectation sidecars and the native results identical (regression:
  `tests/scripts/FixtureComparisonTest.ps1`); fixture IR records no checkout path, so runs from any worktree compare
  equal byte for byte.

A difference means the commit changes behavior: split it, or treat it as a behavior change with its own tests. Do not
compare `Kimi.dll` bytes instead: its informational version embeds the commit id (`0.1.1+<commit>`) and its PDB path
depends on the worktree, so the DLL changes between commits even when behavior does not.

`-O2Metrics` also writes `o2-metrics.json`: instructions and blocks per `define` of each natively run fixture's optimized
IR (`<work>/native*/<name>.O2.ll`, so both runs' `temp/verify` work directories must remain), totalled over the whole
module and, as information, over the runtime helpers (the functions of the checkout's runtime template) and the generated
bodies; the fixtures measured in both runs ranked by candidate/baseline whole-module instruction growth, and those
without an O2 file in a run. The module is the measure because at O2 the runtime helpers are not the same text in both
runs: inlining and interprocedural constant propagation move code between them and the bodies (until 2026-10-31,
`growth` was body growth; an earlier `o2-metrics.json` keeps each fixture's `instructions`, from which module growth
follows). A fixture whose program folds to constants keeps `__kimi_start` as its only body (1,308 of 6,137 on
2026-10-31), so it measures the runtime helpers alone, not generated code. The metrics are information, not part of the
result; R5 measured MIR code generation against a legacy run with them (DECISIONS R5-M3).

## Shared heavy runs and bases

Sessions working in parallel on one machine share its load and their full-suite baselines (DECISIONS R4-M119). Both live in
the main checkout's `artifacts/`, which is ignored and therefore separate in every worktree: a unit in `bin/worktrees/<name>`
uses the main checkout's directories by absolute path.

A heavy run is a full Session (on any pipeline or code generator), a full `-TestPurpose Functional` run (also a ledger run
with `KIMI_MIR_DIFF`), or a native fixture or milestone sweep; a Unit run of selected classes is not. At most three heavy
runs execute at once across the sessions. Before starting one, list `artifacts/heavy-runs/*.run` and delete those older
than four hours (stale); if three or more remain, do targeted work and check again later. Otherwise create
`<R4|BU|R5>-<unit>-<yyyyMMdd-HHmmss>.run` there, one line naming the worktree and the command, start the run, and delete
the file when the run ends, passed or failed. A script running several heavy runs in sequence keeps one file.

`artifacts/verify/shared-base/<sha9>/` holds the bases of one commit, a dev commit or a branch head other units start
from: `diagnostic-snapshot.json` (the snapshot step checks its fixed corpus whatever the selection, so
`./scripts/verify.ps1 -DiagnosticSnapshot` alone, the build and that step, suffices), `mir-diff/` (the whole
`KIMI_MIR_DIFF` directory of `-Pipeline mir -TestPurpose Functional`), optionally `semantic/` (`KIMI_SEMANTIC_SNAPSHOT=write`
output, with the `KIMI_SEMANTIC_HIR`/`KIMI_SEMANTIC_MIR` flags it used) and `README.txt` naming the commit, the commands, the
evidence directory and the date. Reuse an existing base for the same commit; produce a missing one, the ledger as a
registered heavy run, and copy it there. Never edit a base in place: delete a wrong one whole and remake it. A unit compares
against the base of the commit it starts from; pass `-DiagnosticBaseline` an absolute Windows path (`C:/...` or `C:\...`),
since `verify.ps1` does not resolve a Git Bash path such as `/c/...`.

## Specification acceptance and support boundaries

Derive expected acceptance and rejection from the owning SPEC rules before selecting an implementation path. A specification-valid form that currently reports Unsupported is an implementation boundary, not a language rejection. Name and record that boundary with its PLAN owner. When implementing it, replace the temporary rejection with native acceptance and retain the independently invalid counterpart; a passing Milestone Program alone does not close neighboring implementation work.

For acquisition, borrowing and calls, choose the applicable rows below when defining a unit's tests. Exercise each distinct compiler path and each relevant semantic interaction; do not generate an indiscriminate cross-product or duplicate whole fixtures.

| Dimension | Representative counterparts |
| --- | --- |
| Entry path | Direct entry, ordinary syntax such as a subscript, generic Contract dispatch; concrete and common Function calls where applicable |
| Stored value | Scalar, Tuple or named aggregate; Copy and Non-Copy; reference held directly or inside Option/pattern bindings |
| Access and dependency | Shared/exclusive paths; storage slot versus stored referent; independent inputs even when their Origins are equal; child Loan ending before or after a parent operation |
| Execution | Source evaluation order, one selector evaluation, zero-sized values, Move, exactly-once cleanup, abandoned arguments and independent errors |
| Reuse and publication | Reanalysis with retained capacity, failed-request recovery, exact source/configuration evidence, relevant CLI/LSP records and strict allocation checks |

Acquisition has one authority in SPEC §3.5; a returned reference follows §7.1 and §13.5.5, and indexing §4.7.1. Origin compatibility does not establish Loan identity or access authority (§15.3.5–7 and §15.6). Read those contracts together instead of introducing container-specific borrow exceptions. Record unresolved interactions in PLAN §7 and STATUS rather than broadening a completion claim.

For duration-bound sessions, record implementation start, the last unit's start, verification completion and final documentation separately. Report actual total elapsed time, including required verification after the unit-work deadline. Keep failed runs and superseded experiments with the successful evidence; do not use a serial retry or extra warming to certify a failed parallel allocation check; the helper's discarding of a window whose pooled allocations follow trims (below) is a validity rule of the measurement, not such a retry.

## Writing tests

Test code is reviewed and maintained like compiler code. Each case should add coverage that no existing case provides.

- **Placement.** Add a case to the class that already owns the feature; find it through the representative tests in [CODEMAP.md](CODEMAP.md) or by searching for the diagnostic code or SPEC section. Prefer a new `[InlineData]` row on an existing theory when the assertions are the same. Create a new class only for a feature without an owning class, and name it after the feature, never after a fix unit or issue.
- **Analysis entry points.** Run and read analysis and generation through `Compilation` (`Analyze`, `Analysis`, `Problems` with `SyntaxOf`, `ReportAnalysis`, `Generate`), never through `Ownership` or `Emission`. A Comparison Loan conflict or an unproven Origin, each reported under several codes, is matched with `IsLoanConflict` or `IsUnprovenOrigin` (`CompilationTestHelper`). `ArchitectureRulesTest` counts the reads of the old path's structure that remain until the stage that replaces them.
- **Shared helpers.** Use `CompilationTestHelper` (`Parse`, `BindSuccess`, `Reload`, `WriteIr`), `DiagnosticCorpus.Check`, `TestDiagnostics.Of`, `MinimalEmissionTest.Analyze`/`Describe`, `ScalarEmissionTest.EmitFixture` and `AllocationMeasurement.Measure`. Do not copy a helper or an inlined `Reload` round trip into a test class; extend a shared helper, or move a helper needed by two classes into a shared file.
- **Semantic facts.** Read Binding facts of a node through `SemanticQueryTestExtensions` (`TypeOf`, `SymbolOf`, `CallOf`, `StateOf`, `FailureOf` and the other `Compilation.Semantics` forwards), never through Koto semantic slots; stage a state Binding would not publish only with its `SetType`, `SetSymbol` or `SetConversion`. `ArchitectureRulesTest` counts the direct slot references that remain.
- **One layer per rule.** Assert a rule at the lowest layer that observes it. A diagnostic case checks its code, category, span and the label or Related specific to that case. Console rendering and LSP placement (`Finalize`, `DiagnosticConsole`, `WorkspaceCheck.Place`) are shared contracts covered by `DiagnosticContractTest`; repeat them only when a change affects that path or a case's own Related publication is in question.
- **Native fixtures.** Emit a fixture only when the behavior depends on generated code: runtime results, layout, ABI or cleanup order. Choose representative combinations under the dimensions above rather than a fixture per case.
- **No duplicates.** Before adding a case, search for one with the same source and expectation. Do not add a case that differs only in details irrelevant to the rule.
- **Comments.** State the rule a case checks and its SPEC section. Do not narrate earlier behavior, fix history or issue identifiers; those belong in commit messages.

## Three purposes

| Purpose | Location / selection | Required execution |
| --- | --- | --- |
| Functional regression | xUnit tests without `Purpose=Allocation` | Related Unit checks and every Session |
| Allocation/reuse regression | xUnit tests with `Purpose=Allocation` | Related hot-path Unit checks and every Session |
| Performance measurement | `src/Benchmark` | Explicit performance work and milestone completion |

Allocation/reuse regressions keep their input sizes and measure warm work in the standard window of `AllocationMeasurement.Measure`; longer searches for slow growth belong in Benchmark. Native allocation guarantees remain unchanged; compiler allocation assertions may change for a documented speed or code-size tradeoff with reasonable memory growth under AGENTS' performance policy. Measure time, allocations and retained memory for equivalent work before adopting that change; never adjust assertions merely to pass. These regressions test allocation, storage reuse or storage bounds, not timing. Timing-only loops belong in Benchmark. A test that also emits a native fixture remains selectable with its class and included in Session.

Use `AllocationMeasurement.Measure` for repeated warm compiler-allocation checks. Its standard window, owned by the helper, runs 4 warm-up and then 12 measured operations on a dedicated thread without the runner's ExecutionContext and retires the thread's allocation context with a gen0 collection before the counter interval; a full collection would also trim shared pools under concurrent measurements (owner decision, 2026-10-16). `ArrayPool<T>.Shared` empties every thread's pooled buffers when it trims under High memory pressure (from about 81% machine memory load) after a full collection, or after any collection of a pool first used in the process; the next Rent then allocates. A window is therefore invalid when the measuring thread raised pooled-buffer events in it, as `ArrayPoolEventSource` reports, and each has a cause outside the operation: an allocation of the pool and length of a buffer that a High-pressure trim dropped since the window's warm-up began, or a drop by a full pool. A window is also invalid when it allocated while the measuring thread compiled the class constructor of `EqualityComparer<T>` or `Comparer<T>` between two marker methods that it compiles around the window, in the thread's order of the runtime's JIT events (an `EventListener` on the runtime provider's JIT keyword; the helper waits at most 10 s for the end marker's event and otherwise excuses nothing): the first tier-0 run of a comparison creates that default comparer (88 bytes on .NET 10) once per process on the running thread, whenever tiering first runs it at tier 0, while optimized code needs none. Any other pooled allocation, such as storage that first grows after the warm-up, is measured with its event's allocation, and so is any allocation in a window where the thread compiled other code. A comparer whose class constructor is precompiled or shared (a reference type T) or compiled by another thread is not excused, and an invalid window excuses all its allocations, the operation's own one-time allocation in it included, as with a trim. The helper then runs the warm-up and a window of eight operations more than it has already run, at most three windows (12, 28 and 60 measured operations), and fails, naming each window's cause, when every window was invalid. It reports the largest allocation of 12 consecutive measured operations, the whole standard window (owner decision, 2026-10-17). Call sites pass no counts and run no caller-side warm-up loop; setup that the measured operation does not repeat (another phase, a Reload's first binding) runs once before the measurement, and an assertion of retained identity or capacity runs again after it. A regression that passes only with more warm-up shows storage that does not reach steady state within the window: fix the compiler or record an owner-approved exception with its removal condition in PLAN, never more warm-up; the invalid-window rules re-measure a window whose allocations have a cause outside the operation, which can also leave the operation's own one-time allocation in that window unmeasured. After p operations on the measured state and b operations of the helper (b = 4 for the standard window), a window of b + 8 operations adds entries k(p + b) + 1 to k(p + 2b + 8) of a leak of k entries per operation; while p ≤ 8 that range doubles, so each window detects such a leak into doubling storage whose capacity after its first allocation is below k(p + 2b + 8), k(p + 16) for the standard window. Keep setup before a measurement under that bound. A cold measurement of one operation, such as a growth ladder across input sizes, reads the thread's counter around that operation. Keep result validity checks outside the interval, retaining failures from every operation. The helper's regressions check measured allocations, the standard counts, context isolation and exclusion of warm-up allocations, discarding of a window whose pooled allocations follow trims under forced High pressure, measurement of any other pooled allocation, discarding of a window in which the measuring thread created a default comparer, measurement of a late allocation whose operation compiles other code, and failure when every window is invalid. A passing isolated run does not identify the cause of an earlier raw-thread failure; retain the failed evidence and report that uncertainty.

Formerly serial classes isolate their allocation tests in a nested `AllocationTests` class in the shared `AllocationIsolationCollection`, whose collection definition disables parallelization; their functional cases can run concurrently. Shared private static helpers and constants stay in the outer class. Do not remove isolation from measurements merely to increase concurrency, or share mutable compilation instances between tests. Existing allocation tests that already ran concurrently retain their execution policy.

Measurement commands and fixed conditions belong to their dedicated documents below. Run them after a successful whole-solution Release build, use fresh evidence files under `artifacts/benchmarks/` and retain exact input/configuration identity. Shared regression workloads must still pass; allocation guarantees stay in xUnit, timing remains opt-in. Compare repeated matched runs before claiming a speedup. Documentation regressions keep allocation comparisons, not timing-only output.

| Measurement entry | Inputs / conditions |
| --- | --- |
| `Benchmark --verification` | Guard-history/live-part-loan inputs shared from `tests/Workloads/VerificationWorkloads.cs`; reports sizes, retained records, counts and timing, failing if Binding/verification fails. Save stdout to a fresh JSON file. |
| `benchmark-p42.ps1 [-Runs 5]` | [WrappingArithmetic.md](../../src/Benchmark/WrappingArithmetic.md); harness checks the first O0/O2 run before timing-only repetitions. |
| `Benchmark --pair-cases` | [PairCases.md](../../src/Benchmark/PairCases.md) |
| `Benchmark --borrow-storage` | [BorrowStorage.md](../../src/Benchmark/BorrowStorage.md) |
| `Benchmark --object-plans` | [ObjectPlans.md](../../src/Benchmark/ObjectPlans.md) |
| `benchmark-object-finalization.ps1 [-Runs 7] [-Mode Rc,Arc] [-Name <n>]` | [ObjectPlans.md](../../src/Benchmark/ObjectPlans.md); uninstrumented native finalization timings; protocol coverage in [SharedObjects.md](../../src/Benchmark/SharedObjects.md). |
| `Benchmark --callable-plans`, `--local-regions`, `--fixed-origin-loans` | [CallablePlans.md](../../src/Benchmark/CallablePlans.md); `--fixed-origin-loans` also prints the `--generic-defaults` report after its own. |
| `Benchmark --property-plans` | [Properties.md](../../src/Benchmark/Properties.md) |
| `Benchmark --adaptation-plans` | [Adaptations.md](../../src/Benchmark/Adaptations.md) |
| `Benchmark --generic-defaults` | [GenericDefaults.md](../../src/Benchmark/GenericDefaults.md) |
| `Benchmark --element-places` | [ElementPlaces.md](../../src/Benchmark/ElementPlaces.md) |
| `Benchmark --origin-proof` | [OriginProof.md](../../src/Benchmark/OriginProof.md); each case in its own process with a time limit, censored cases recorded. |
| `Benchmark --view-plans`; ExclusiveViews and SliceCopies native runs | [Views.md](../../src/Benchmark/Views.md); the native runs are its `milestone-harness.ps1 -Milestone 0 ... -Runs 7` commands. |
| `Benchmark --associated-inference` | [AssociatedInference.md](../../src/Benchmark/AssociatedInference.md); shared explicit/inferred declaration workloads, cold/warm costs and retained capacities. |
| `Benchmark --constructor-inference` | [ConstructorInference.md](../../src/Benchmark/ConstructorInference.md) |
| `Benchmark --inheritance-plans`, `--inheritance-lifecycle` | [Inheritance.md](../../src/Benchmark/Inheritance.md); lifecycle adds fresh/edited snapshots and retained ownership storage. |
| `Benchmark --arithmetic-plans`, `benchmark-arithmetic.ps1` | [Arithmetic.md](../../src/Benchmark/Arithmetic.md) |
| `Benchmark --hover` | [HOVER.md](HOVER.md#responsiveness-measurements) |
| `Benchmark --compiler-pipeline`, `--compiler-pipeline-check`, `--analyze-cost`, `--collection-cost`, `--collection-cost-check` | [CompilerPipeline.md](../../src/Benchmark/CompilerPipeline.md); the checks have no timing thresholds; save their output under `artifacts/verify/`. |
| `Benchmark --named-arguments` | [NamedArguments.md](../../src/Benchmark/NamedArguments.md#reproduce) |
| `Benchmark --diagnostics` | No document; `DiagnosticMeasurements` writes its input projects under `temp/diagnostic-measurements/` and prints one JSON report to stdout: compiler and runtime versions, and per input the median and minimum microseconds and median bytes per operation. |
| `benchmark-o2-corpus.ps1` | [O2Corpus.md](../../src/Benchmark/O2Corpus.md) |

## Native fixtures

`verify.ps1 -NativeParallel <n>` controls native fixture workers independently of managed test and milestone parallelism. The default is up to four workers; 1 runs serially. Each fixture verifies its input IR once, then compiles and executes O0 and O2, verifying optimized IR again. stdout, stderr, exit-code and dependency oracles are checked at each level.

Each fixture owns its output names and log. Workers publish status (passed, failed or incomplete), completed optimization levels and per-level execution measurements; the parent waits for every worker, preserves failure logs and fails the step if any fixture fails, is incomplete or has no result. Verify stores per-fixture logs and `results.json` under its evidence directory, with generated binaries under `temp/`. Fixture patterns are still processed in order, and parallelism is bounded within each pattern.

A nonzero .observation sidecar is a process CPU observation budget in milliseconds, written by ScalarEmissionTest's
observationMilliseconds argument. Zero or no sidecar expects completion. The separate native-runner
-ExecutionDeadlineMilliseconds (default 30000) starts before launch. A divergent process that exits fails; one still
running after sufficient observed CPU time is stopped and its output compared. If the deadline arrives first, its
status is incomplete, even for empty expected output. The measurements establish bounded execution evidence, never
a proof of nontermination. Regenerate old .timeout fixtures; their elapsed-time oracle is no longer used.
Incomplete status remains visible in results.json and the Verify summary: it fails verification. Only historical fixture
comparison accepts old ok records; it rejects incomplete results.

Native selection and its input hashes include only `.ll` files, even with a broad pattern such as `*`.
Sidecar expectation files never become compiler inputs; a pattern matching only sidecars fails as an empty selection.

After changes to native verification, run `./tests/scripts/NativeFixtureRunnerTest.ps1` with the installed toolchain. It checks serial/parallel equivalence, UTF-8 stdout/stderr, nonzero expected exits, mixed independent failures, CPU observation, delayed execution, insufficient observation, expected-completion deadlines, lost executable launches, invalid UTF-8, evidence failure and empty selections. Native recovery uses one fixed 5-second grace separate from the execution deadline; unavailable launch/CPU/recovery/capture is incomplete, while captured invalid UTF-8 is a failure. Logs, including intentional failure cases, remain in `artifacts/verify/`.
