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
Release build, 300 tests, the diagnostic snapshot and 306 native O0/O2 executions. The whole-solution
Session `20260930-170613-497-session-review-fixes-final` also passes its warning-free build and all
14,033 tests.

## Dependent-body follow-up

The harness now measures both inspection-only bodies and the same inputs with references saved
in locals. It reports operations and retained bytes of the dependency, retained-authority and
liveness buffers in addition to logical dependency cells. `tableBytes` excludes root lists,
flags and other ownership state; `firstAnalysisBytes` includes the complete analysis allocation.

The packed implementation uses two bits per dependency/authority cell, one bit per liveness
cell, recorded dependency roots and only genuinely dependent Places in the liveness matrix.
Wide dimension checks enforce the Windows profile's 64 MiB per-table limit before allocation;
cached capacity cannot bypass it. Dense dependencies still have quadratic worst-case growth.

Windows x64, Release, .NET 10.0.12; the inputs and eight warm-ups followed by five samples of
eight analyses are unchanged. The baseline is `e5bf9364` with byte-count instrumentation;
the optimized analysis is committed in `0df54433`. Evidence, including intermediate attempts,
is in `artifacts/benchmarks/20261001-review-fixes/borrow-{before,packed,roots,live,final}.json`.

| Stored references | Three table buffers, before / after (bytes) | First analysis, before / after (bytes) |
| --- | --- | --- |
| 1 | 450 / 54 | 39,920 / 40,056 |
| 32 | 355,431 / 43,454 | 860,088 / 550,304 |
| 128 | 5,648,775 / 691,442 | 7,846,912 / 2,896,472 |

At 128 references the table buffers shrink by 87.8% and complete first-analysis allocation by
63.1%. Inspection-only bodies retain zero bytes in these tables; their first-analysis allocation
increases by 352 bytes because the ownership bodies now contain additional reusable fields.
The 128-reference mean changes from 49.58 to 23.65 ms, while the 32-reference mean changes from
4.17 to 4.87 ms with substantial sample variation. These are fixed-run observations, not a claim
of a general throughput improvement. The regression checks packed storage bounds and zero warm
allocation at both dependent sizes. Unit `20261001-000757-476-unit-bounded-borrow-storage` passes
447 tests, the diagnostic snapshot and 158 native O0/O2 executions.

After the callable repairs, `borrow-current.json` at `30d37b21` reproduces every allocation and
table-byte count above with the same fixed workload. Its 128-reference mean is 25.24 ms and
32-reference mean 4.68 ms; these additional observations retain the same limited timing claim.

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --borrow-storage > artifacts/benchmarks/borrow-storage.json
```
