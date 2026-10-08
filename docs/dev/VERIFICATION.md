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
| Compiler, Kimi library or compiler tests | Related Unit verification and one final Session; select relevant native fixtures and milestones explicitly. |
| Benchmark or Playground code | Session verification, including the whole-solution build, before completion; execute the affected scenario or relevant fixed-condition measurements. |
| Verification scripts, CI or other tooling | Related script regressions and affected build/execution paths. Native-runner changes require `NativeFixtureRunnerTest.ps1`. Changes affecting the managed build or test pipeline also require Session. A local check does not certify a hosted CI run. |
| VS Code extension, including its build/distribution scripts | Follow [the extension instructions](../../src/kimi-ext/AGENTS.md). |

Keep the selected checks and their results in the commit and `artifacts/verify/`; do not run unrelated suites merely
because a non-documentation file changed. Performance changes retain the measurement requirements below. NativeAOT
still requires explicit instruction.

## Feedback and completion

During edits, build the test project incrementally and run the relevant methods. These commands do not establish warning-free completion:

```powershell
dotnet build tests/xUnitTest/xUnitTest.csproj --no-restore -c Release
dotnet tests/xUnitTest/bin/Release/net10.0/xUnitTest.dll -method XunitTest.ContinuationVerificationTest.ReplayCannotRestoreAbandonedGuardProtection -failSkips
```

Choose an existing method from the test source or runner discovery (`-list methods`). For fast functional feedback within a class, use `-class <class> -trait- Purpose=Allocation`. Direct runner selections of a moved allocation method use `<outer-class>+AllocationTests.<method>`.

At Unit completion, `scripts/verify.ps1` builds the test project and its Kimi dependency non-incrementally with warnings as errors. At Session completion it builds the entire solution, including Benchmark and Playground, then runs all managed regressions. The existing analyzer check is retained in both modes. Changes to Benchmark or Playground need the Session build before completion. Neither mode runs NativeAOT.

```powershell
./scripts/verify.ps1 -Class XunitTest.ContinuationVerificationTest -Fixtures 'VerificationContinuation*.ll'
./scripts/verify.ps1 -Class XunitTest.ContinuationVerificationTest -TestPurpose Allocation
./scripts/verify.ps1 -Mode Session
```

For the last unit, a successful Session can replace a separate Unit run if it verifies the same source/configuration
and covers every required Unit check. Session runs all managed regressions, but native fixtures and milestones still
need explicit `-Fixtures` and `-Milestone` arguments. Include any required diagnostic snapshot or other additional checks
as well; external checks remain required. An unselected check is not covered merely because the mode is Session.

Prepare implementation, tests and affected documentation first, run the combined final verification, then commit,
associate the evidence as described below and push. Record that the Session also satisfied the final Unit. Do not repeat
either mode solely to obtain a second mode label. Missing checks must pass against the same inputs; changes to covered
inputs invalidate their evidence and require reverification. PLAN/history bookkeeping retains the manifest exception below.

Verify accepts both `ClassName` / `ClassName.Method` and their fully qualified forms; short names use the repository's `XunitTest` namespace. A wildcard `*` may occur at the beginning or end. Invalid pattern syntax is rejected before building. Verify expands outer selections to include nested `AllocationTests`, so focused selections retain their allocation coverage.

After the build, Unit verification discovers methods from that exact assembly, checks **every** requested class and method, and records the effective selection in `selected-methods.json` before running tests. A misspelled selector cannot be hidden by another selector that matches. Class selections are ORed, method selections are ORed, and those two groups intersect; the runner also applies the purpose filter. Discovery never checks an older assembly against newly edited test sources. Both zero-method and zero-executed-test selections fail.

`-TestPurpose All` is the default; `Functional` and `Allocation` restrict deliberate Unit checks. Session rejects either restriction. Select relevant native fixtures and milestones explicitly as before. Selector rules and optional allocation companions are regression-checked by `./tests/scripts/VerificationSelectionTest.ps1`.

## Source identity and commit association

Verify writes `inputs-before.json` and `inputs-after.json`. The manifest covers tracked and nonignored untracked files, including new source files, configuration, scripts and specification documents. It excludes `draft/` (not authoritative compiler input), `docs/dev/PLAN.md` and `PLAN_HISTORY.md` (post-verification bookkeeping); ignored build outputs are excluded. Restored `obj/project.assets.json` and NuGet-generated project props/targets are hashed separately. Changed, added or deleted inputs between the two observations fail the run, even if its tests passed. Keep the no-source-edits rule: endpoint comparison does not detect a transient edit that is reverted between observations.

Each source entry records SHA-256 of the actual bytes and the Git-normalized blob identity. After committing a unit verified by Unit or Session Verify, associate its evidence with the commit before pushing:

```powershell
./scripts/verify-commit.ps1 -Evidence artifacts/verify/<run> [-Commit HEAD]
```

The association requires a successful Verify summary and exact path/blob equality with the committed tree under the same manifest policy; missing untracked inputs and additional committed sources fail. `commit-match.json` records the full commit and manifest hash. Git line-ending/filter normalization is explicit: it is not a claim that a checkout's raw bytes equal a commit's bytes. Later PLAN/history bookkeeping can share the same verified source manifest. The manifests are evidence, not an isolated or hermetic build: SDK/runtime, external package contents and tools outside the repository remain external; use `-VerifyToolchain` for the separately recorded native-toolchain identity check. Manifest and commit-matching regressions run with `./tests/scripts/VerificationInputsTest.ps1`.

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

Acquisition has one authority in SPEC §3.5; a published Place result uses §3.4.1/§7.1.1 and indexing §4.6.9. Origin compatibility does not establish Loan identity or access authority (§15.3.5–7 and §15.6). Read those contracts together instead of introducing container-specific borrow exceptions. Record unresolved interactions in PLAN §7 and STATUS rather than broadening a completion claim.

For duration-bound sessions, record implementation start, the last unit's start, verification completion and final documentation separately. Report actual total elapsed time, including required verification after the unit-work deadline. Keep failed runs and superseded experiments with the successful evidence; do not use a serial retry or extra warming to certify a failed parallel allocation check.

## Writing tests

Test code is reviewed and maintained like compiler code. Each case should add coverage that no existing case provides.

- **Placement.** Add a case to the class that already owns the feature; find it through the representative tests in [CODEMAP.md](CODEMAP.md) or by searching for the diagnostic code or SPEC section. Prefer a new `[InlineData]` row on an existing theory when the assertions are the same. Create a new class only for a feature without an owning class, and name it after the feature, never after a fix unit or issue.
- **Shared helpers.** Use `CompilationTestHelper` (`Parse`, `BindSuccess`, `Reload`, `WriteIr`), `DiagnosticCorpus.Check`, `TestDiagnostics.Of`, `MinimalEmissionTest.Analyze`/`Describe`, `ScalarEmissionTest.EmitFixture` and `AllocationMeasurement.Measure`. Do not copy a helper or an inlined `Reload` round trip into a test class; extend a shared helper, or move a helper needed by two classes into a shared file.
- **One layer per rule.** Assert a rule at the lowest layer that observes it. A diagnostic case checks its code, category, span and the label, Related or Advice specific to that case. Console rendering and LSP placement (`Finalize`, `DiagnosticConsole`, `WorkspaceCheck.Place`) are shared contracts covered by `DiagnosticContractTest`; repeat them only when a change affects that path or a case's own Related/Advice publication is in question.
- **Native fixtures.** Emit a fixture only when the behavior depends on generated code: runtime results, layout, ABI or cleanup order. Choose representative combinations under the dimensions above rather than a fixture per case.
- **No duplicates.** Before adding a case, search for one with the same source and expectation. Do not add a case that differs only in details irrelevant to the rule.
- **Comments.** State the rule a case checks and its SPEC section. Do not narrate earlier behavior, fix history or issue identifiers; those belong in commit messages.

## Three purposes

| Purpose | Location / selection | Required execution |
| --- | --- | --- |
| Functional regression | xUnit tests without `Purpose=Allocation` | Related Unit checks and every Session |
| Allocation/reuse regression | xUnit tests with `Purpose=Allocation` | Related hot-path Unit checks and every Session |
| Performance measurement | `src/Benchmark` | Explicit performance work and milestone completion |

Allocation/reuse regressions retain their assertions, input sizes and fixed warm-up/repetition counts. They test zero allocation, storage reuse or storage bounds; they do not collect timing reports. Timing-only loops belong in Benchmark. A test that also needs to emit a native fixture remains selectable with its class and is not omitted from Session.

Use `AllocationMeasurement.Measure` for repeated warm compiler-allocation checks. It prepares a dedicated thread without the runner's ExecutionContext and retires its allocation context before the counter interval. `iterations` and `warmupIterations` preserve each workload's original fixed counts (the default warm-up count equals `iterations`); never add a second caller-side warm-up when migrating. Keep result validity checks outside the interval, retaining failures from every operation. The helper's regressions check measured allocations, count preservation, context isolation and exclusion of warm-up allocations. A passing isolated run does not identify the cause of an earlier raw-thread failure; retain the failed evidence and report that uncertainty.

Formerly serial classes isolate their allocation tests in a nested `AllocationTests` class with disabled parallelization; their functional cases can run concurrently. Shared private static helpers and constants stay in the outer class. Do not remove isolation from measurements merely to increase concurrency, or share mutable compilation instances between tests. Existing allocation tests that already ran concurrently retain their execution policy.

Measurement commands and fixed conditions belong to their dedicated documents below. Run them after a successful whole-solution Release build, use fresh evidence files under `artifacts/benchmarks/` and retain exact input/configuration identity. Shared regression workloads must still pass; allocation guarantees stay in xUnit, timing remains opt-in. Compare repeated matched runs before claiming a speedup. Documentation regressions keep allocation comparisons, not timing-only output.

| Measurement entry | Inputs / conditions |
| --- | --- |
| `Benchmark --verification` | Guard-history/live-part-loan inputs shared from `tests/Workloads/VerificationWorkloads.cs`; reports sizes, retained records, counts and timing, failing if Binding/verification fails. Save stdout to a fresh JSON file. |
| `benchmark-p42.ps1 [-Runs 5]` | [WrappingArithmetic.md](../../src/Benchmark/WrappingArithmetic.md); harness checks the first O0/O2 run before timing-only repetitions. |
| `Benchmark --pair-cases` | [PairCases.md](../../src/Benchmark/PairCases.md) |
| `Benchmark --borrow-storage` | [BorrowStorage.md](../../src/Benchmark/BorrowStorage.md) |
| `Benchmark --object-plans` | [ObjectPlans.md](../../src/Benchmark/ObjectPlans.md) |
| `Benchmark --adaptation-plans` | [Adaptations.md](../../src/Benchmark/Adaptations.md) |
| `Benchmark --view-plans` | [Views.md](../../src/Benchmark/Views.md) |
| `Benchmark --associated-inference` | [AssociatedInference.md](../../src/Benchmark/AssociatedInference.md); shared explicit/inferred declaration workloads, cold/warm costs and retained capacities. |
| `Benchmark --constructor-inference` | [ConstructorInference.md](../../src/Benchmark/ConstructorInference.md) |
| `Benchmark --inheritance-plans`, `--inheritance-lifecycle` | [Inheritance.md](../../src/Benchmark/Inheritance.md); lifecycle adds fresh/edited snapshots and retained ownership storage. |
| `Benchmark --arithmetic-plans`, `benchmark-arithmetic.ps1` | [Arithmetic.md](../../src/Benchmark/Arithmetic.md) |
| `Benchmark --hover` | [HOVER.md](HOVER.md#responsiveness-measurements) |

## Native fixtures

`verify.ps1 -NativeParallel <n>` controls native fixture workers independently of managed test and milestone parallelism. The default is up to four workers; 1 runs serially. Each fixture verifies its input IR once, then compiles and executes O0 and O2, verifying optimized IR again. stdout, stderr, exit codes, dependency checks and divergent-fixture timeouts are unchanged.

Each fixture owns its output names and log. Workers report completed optimization levels and failures; the parent waits for every worker, preserves failure logs and fails the step if any fixture fails or a result is missing. Verify stores per-fixture logs and `results.json` under its evidence directory, with generated binaries under `temp/`. Fixture patterns are still processed in order, and parallelism is bounded within each pattern.

Native selection and its input hashes include only `.ll` files, even with a broad pattern such as `*`.
Sidecar expectation files never become compiler inputs; a pattern matching only sidecars fails as an empty selection.

After changes to native verification, run `./tests/scripts/NativeFixtureRunnerTest.ps1` with the installed toolchain. It checks serial/parallel equivalence, UTF-8 stdout/stderr, nonzero expected exits, mixed independent failures, divergent execution and empty selections. Logs, including intentional failure cases, remain in `artifacts/verify/`.
