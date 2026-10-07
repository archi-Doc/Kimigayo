# Verification workflow

## Continuous integration

`.github/workflows/test.yml` runs on pull requests, pushes to `main` and `dev`, and manual dispatch. Ubuntu checks the
managed solution with warnings as errors. Windows uses the ordinary Session verifier for all functional/allocation
regressions and every regenerated native fixture at O0/O2, then runs the native-worker PowerShell regressions, CLI
integration, and extension unit/integration tests with the managed compiler. Neither job runs NativeAOT.

The Windows job downloads the adopted LLVM archive only on a toolchain-cache miss, checks its pinned SHA-256, and calls
the existing backend setup script. Explicit toolchain verification checks cached installations too. Update the archive
version and digest together with an adopted profile change. Logs, source manifests and result records are uploaded even
when a later step fails. A configured lane is not evidence of a successful hosted run; inspect that run's results.

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

Verify accepts both `ClassName` / `ClassName.Method` and their fully qualified forms; short names use the repository's `XunitTest` namespace. A wildcard `*` may occur at the beginning or end. Invalid pattern syntax is rejected before building. Verify expands outer selections to include nested `AllocationTests`, so focused selections retain their allocation coverage.

After the build, Unit verification discovers methods from that exact assembly, checks **every** requested class and method, and records the effective selection in `selected-methods.json` before running tests. A misspelled selector cannot be hidden by another selector that matches. Class selections are ORed, method selections are ORed, and those two groups intersect; the runner also applies the purpose filter. Discovery never checks an older assembly against newly edited test sources. Both zero-method and zero-executed-test selections fail.

`-TestPurpose All` is the default; `Functional` and `Allocation` restrict deliberate Unit checks. Session rejects either restriction. Select relevant native fixtures and milestones explicitly as before. Selector rules and optional allocation companions are regression-checked by `./tests/scripts/VerificationSelectionTest.ps1`.

## Source identity and commit association

Verify writes `inputs-before.json` and `inputs-after.json`. The manifest covers tracked and nonignored untracked files, including new source files, configuration, scripts and specification documents. It excludes `draft/` (not authoritative compiler input), `docs/dev/PLAN.md` and `PLAN_HISTORY.md` (post-verification bookkeeping); ignored build outputs are excluded. Restored `obj/project.assets.json` and NuGet-generated project props/targets are hashed separately. Changed, added or deleted inputs between the two observations fail the run, even if its tests passed. Keep the no-source-edits rule: endpoint comparison does not detect a transient edit that is reverted between observations.

Each source entry records SHA-256 of the actual bytes and the Git-normalized blob identity. After committing a verified unit, associate its evidence with the commit before pushing:

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

## Three purposes

| Purpose | Location / selection | Required execution |
| --- | --- | --- |
| Functional regression | xUnit tests without `Purpose=Allocation` | Related Unit checks and every Session |
| Allocation/reuse regression | xUnit tests with `Purpose=Allocation` | Related hot-path Unit checks and every Session |
| Performance measurement | `src/Benchmark` | Explicit performance work and milestone completion |

Allocation/reuse regressions retain their assertions, input sizes and fixed warm-up/repetition counts. They test zero allocation, storage reuse or storage bounds; they do not collect timing reports. Timing-only loops belong in Benchmark. A test that also needs to emit a native fixture remains selectable with its class and is not omitted from Session.

Use `AllocationMeasurement.Measure` for repeated warm compiler-allocation checks. It prepares a dedicated thread without the runner's ExecutionContext and retires its allocation context before the counter interval. `iterations` and `warmupIterations` preserve each workload's original fixed counts (the default warm-up count equals `iterations`); never add a second caller-side warm-up when migrating. Keep result validity checks outside the interval, retaining failures from every operation. The helper's regressions check measured allocations, count preservation, context isolation and exclusion of warm-up allocations. A passing isolated run does not identify the cause of an earlier raw-thread failure; retain the failed evidence and report that uncertainty.

Formerly serial classes isolate their allocation tests in a nested `AllocationTests` class with disabled parallelization; their functional cases can run concurrently. Shared private static helpers and constants stay in the outer class. Do not remove isolation from measurements merely to increase concurrency, or share mutable compilation instances between tests. Existing allocation tests that already ran concurrently retain their execution policy.

The guard-history and live-part-loan measurement inputs are linked from `tests/Workloads/VerificationWorkloads.cs` into both tests and Benchmark. Their timing loops run explicitly:

```powershell
# After a successful whole-solution Release build; choose a fresh evidence file per run.
New-Item -ItemType Directory -Force artifacts/benchmarks | Out-Null
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --verification > artifacts/benchmarks/verification-measurements.json
```

This command reports input sizes, retained record counts, iteration counts and timings, and fails if an input stops binding or verifying. Allocation guarantees are checked by xUnit, separately from timing. Documentation measurements remain available through the existing Benchmark entries; normal documentation regressions retain their allocation comparisons without timing-only output or unasserted measurement passes. See the measurement-specific documents in `src/Benchmark` for their commands. Compare repeated runs on the same configuration before claiming a speedup.

`src/backend/windows-x64/benchmark-p42.ps1 [-Runs 5]` times the P42 hash and generator workloads (`src/Benchmark/Kimi/HashWrapping.kimi` on wrapping Types, `HashChecked.kimi` widened and masked on checked Types) at O0 and O2 through the milestone harness (`milestone-harness.ps1 -Source -WorkRoot -Runs`: the same build and checked first run, then timing-only repetitions) and writes `summary.json`/`summary.md` under `artifacts/benchmarks/p42-hash-prng/<stamp>/`; see [WrappingArithmetic.md](../../src/Benchmark/WrappingArithmetic.md).

`Benchmark --pair-cases` measures the Semantics-case runs of generic definitions (SPEC §8.10): first analysis allocation, warm analysis bytes and warm analysis and rebinding timings of the case workload shared with `GenericCaseAllocationTest` through `tests/Workloads/VerificationWorkloads.cs`, beside the stored-reference workload as a reference; see [PairCases.md](../../src/Benchmark/PairCases.md).

`Benchmark --borrow-storage` measures shared inspection-only and stored-reference workloads: first analysis allocation, retained dependency-table cells, physical table bytes and warm timings; see [BorrowStorage.md](../../src/Benchmark/BorrowStorage.md). Storage bounds and conflicting later call arguments remain regressions in `BorrowDependencyStorageTest`.

`Benchmark --object-plans` measures direct and stored owning-object view analysis/emission with the same inputs as
`StoredObjectViewTest`; fixed conditions and the command are in [ObjectPlans.md](../../src/Benchmark/ObjectPlans.md).
Allocation assertions remain in the normal Unit/Session suite; timing samples remain opt-in.

`Benchmark --view-plans` measures Binding, ownership and emission for the exclusive-view and Slice-copy inputs
shared with `UniqSliceTest` and `SliceToArrayTest`; fixed conditions and native measurement commands are in
[Views.md](../../src/Benchmark/Views.md).

Constructor inference measurements use the shared `ConstructorInferenceWorkloads` inputs. Run
`Benchmark --constructor-inference` for cold/warm explicit and inferred calls, the full reference check,
failure cases, retained capacity and work counters. Conditions and commands are in
[ConstructorInference.md](../../src/Benchmark/ConstructorInference.md); allocation and differential
assertions remain in `ConstructorInferenceReuseTest`.

## Native fixtures

`verify.ps1 -NativeParallel <n>` controls native fixture workers independently of managed test and milestone parallelism. The default is up to four workers; 1 runs serially. Each fixture verifies its input IR once, then compiles and executes O0 and O2, verifying optimized IR again. stdout, stderr, exit codes, dependency checks and divergent-fixture timeouts are unchanged.

Each fixture owns its output names and log. Workers report completed optimization levels and failures; the parent waits for every worker, preserves failure logs and fails the step if any fixture fails or a result is missing. Verify stores per-fixture logs and `results.json` under its evidence directory, with generated binaries under `temp/`. Fixture patterns are still processed in order, and parallelism is bounded within each pattern.

After changes to native verification, run `./tests/scripts/NativeFixtureRunnerTest.ps1` with the installed toolchain. It checks serial/parallel equivalence, UTF-8 stdout/stderr, nonzero expected exits, mixed independent failures, divergent execution and empty selections. Logs, including intentional failure cases, remain in `artifacts/verify/`.
