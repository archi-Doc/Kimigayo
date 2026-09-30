# Verification workflow

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

## Three purposes

| Purpose | Location / selection | Required execution |
| --- | --- | --- |
| Functional regression | xUnit tests without `Purpose=Allocation` | Related Unit checks and every Session |
| Allocation/reuse regression | xUnit tests with `Purpose=Allocation` | Related hot-path Unit checks and every Session |
| Performance measurement | `src/Benchmark` | Explicit performance work and milestone completion |

Allocation/reuse regressions retain their assertions, input sizes and fixed warm-up/repetition counts. They test zero allocation, storage reuse or storage bounds; they do not collect timing reports. Timing-only loops belong in Benchmark. A test that also needs to emit a native fixture remains selectable with its class and is not omitted from Session.

Formerly serial classes isolate their allocation tests in a nested `AllocationTests` class with disabled parallelization; their functional cases can run concurrently. Shared private static helpers and constants stay in the outer class. Do not remove isolation from measurements merely to increase concurrency, or share mutable compilation instances between tests. Existing allocation tests that already ran concurrently retain their execution policy.

The guard-history and live-part-loan measurement inputs are linked from `tests/Workloads/VerificationWorkloads.cs` into both tests and Benchmark. Their timing loops run explicitly:

```powershell
# After a successful whole-solution Release build; choose a fresh evidence file per run.
New-Item -ItemType Directory -Force artifacts/benchmarks | Out-Null
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --verification > artifacts/benchmarks/verification-measurements.json
```

This command reports input sizes, retained record counts, iteration counts and timings, and fails if an input stops binding or verifying. Allocation guarantees are checked by xUnit, separately from timing. Documentation measurements remain available through the existing Benchmark entries; normal documentation regressions retain their allocation comparisons without timing-only output or unasserted measurement passes. See the measurement-specific documents in `src/Benchmark` for their commands. Compare repeated runs on the same configuration before claiming a speedup.

`Benchmark --borrow-storage` measures the shared string-inspection workload's first analysis allocation, retained dependency-table cells and warm timings; see [BorrowStorage.md](../../src/Benchmark/BorrowStorage.md). Stored references and conflicting later call arguments remain functional controls in `BorrowDependencyStorageTest`.

## Native fixtures

`verify.ps1 -NativeParallel <n>` controls native fixture workers independently of managed test and milestone parallelism. The default is up to four workers; 1 runs serially. Each fixture verifies its input IR once, then compiles and executes O0 and O2, verifying optimized IR again. stdout, stderr, exit codes, dependency checks and divergent-fixture timeouts are unchanged.

Each fixture owns its output names and log. Workers report completed optimization levels and failures; the parent waits for every worker, preserves failure logs and fails the step if any fixture fails or a result is missing. Verify stores per-fixture logs and `results.json` under its evidence directory, with generated binaries under `temp/`. Fixture patterns are still processed in order, and parallelism is bounded within each pattern.

After changes to native verification, run `./tests/scripts/NativeFixtureRunnerTest.ps1` with the installed toolchain. It checks serial/parallel equivalence, UTF-8 stdout/stderr, nonzero expected exits, mixed independent failures, divergent execution and empty selections. Logs, including intentional failure cases, remain in `artifacts/verify/`.
