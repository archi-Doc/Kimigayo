# Borrow dependency storage

`Benchmark --borrow-storage` shares its input with `BorrowDependencyStorageTest` through
`tests/Workloads/VerificationWorkloads.cs`. It measures first ownership-analysis allocations and
five samples of eight warm analyses after eight fixed warm-ups. Parsing and Binding are outside
the measurement. Normal verification asserts storage bounds and warm zero allocation without timing.

The workload calls a `ref/string` parameter with owned string locals. These call-wide inspections
already have explicit Loan extents and retain no local Origin dependency. References saved in locals
and ordinary scalar-reference arguments do need local dependency tracking; they are not covered by
this zero-table claim. The regression also checks a write in a later argument against the live inspection.

## 2026-10-01 review

Windows x64, Release, .NET 10.0.12. Only the dependency table's allocation/clearing point changed:
it now waits for the first nonempty local dependency. Existing tables remain reusable. No new
borrow exception, liveness rule or resource cutoff was introduced.

| String locals | Places in measured body | Retained table cells before / after | First analysis bytes before / after |
| --- | --- | --- | --- |
| 1 | 7 | 49 / 0 | 38,784 / 38,664 |
| 32 | 193 | 37,249 / 0 | 454,288 / 416,784 |
| 128 | 769 | 591,361 / 0 | 2,409,128 / 1,816,936 |

The 128-local case allocates 592,192 fewer bytes (24.6%). Timings are retained as observations;
one before/after run with visible sample variation does not establish a throughput improvement.
Evidence: `artifacts/benchmarks/borrow-storage-review/string-{before,after}.json`.
Earlier scalar-reference probes retain their dependency tables as required; their failed zero-table
expectations and a stale incremental-build measurement are retained separately and are not results
of this optimization. Unit `20260930-170137-557-unit-lazy-borrow-dependency-storage` passes a warning-free
Release build, 300 tests, the diagnostic snapshot and 306 native O0/O2 executions. Completion also
requires the final whole-solution Session build because Benchmark changed.

Bodies with actual local dependencies still use quadratic tables. Large-body overflow/resource
diagnostics and a more compact representation remain separate unfinished work.

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --borrow-storage > artifacts/benchmarks/borrow-storage.json
```
