# Inheritance pipeline measurements

After a whole-solution Release build, run from the repository root:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --inheritance-plans > artifacts/benchmarks/inheritance-plans.json
```

The shared runner measures Binding, ownership and IR emission for the unchanged
Milestone 25 and `VerificationWorkloads.InheritedPlans`: depths 1, 8 and 32 with one
Field per layer, and four layers with 8 or 32 Fields each. Each generated input
updates and checks the last Field of every layer and calls an inherited method
and computed getter. `InheritedPlanReuseTest` verifies the same inputs natively
and asserts zero warm allocation for deep and wide storage.

Conditions are fixed: 32 warm-ups, seven samples of 64 successful operations per
phase, with collection outside each sample. Reports retain every sample, source
SHA-256, assembly identities and Loan-storage sizes. Run without concurrent builds
or measurements. These are warm whole-phase observations, including library work;
they are not cold-start or native timings and do not isolate field selection.

The field lookup visits each declaration member once per searched layer; physical
layouts retain their total storage count instead of recounting the base chain at
every projection step. Neither optimization introduces a separate mutable index.

The 2026-10-07 before/after reports are
`artifacts/benchmarks/p25-inheritance-before-20261007.json` and
`artifacts/benchmarks/p25-inheritance-after-20261007.json`. All 252 samples allocate
zero bytes, with matching workload hashes. Median milliseconds for representative
inputs are below; each cell is before → after.

| Input | Binding | Ownership | IR emission |
| --- | --- | --- | --- |
| Milestone 25 | 6.587 → 6.739 | 0.431 → 0.429 | 0.988 → 0.986 |
| Depth 32, one Field per layer | 6.924 → 6.973 | 0.453 → 0.420 | 0.685 → 0.544 |
| Four layers, 32 Fields per layer | 6.747 → 6.678 | 0.341 → 0.216 | 0.430 → 0.259 |

These are one fixed before/after run on one machine, not a general speedup claim.

## Fresh snapshots and retained storage

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --inheritance-lifecycle > artifacts/benchmarks/inheritance-lifecycle.json
```

This complementary protocol uses the same depth 1/8/32 and width 32 workloads. Each
of seven samples builds a fresh Compilation, then another for an edited immutable
snapshot while retaining the previous one. The edit adds one named group/function;
both source hashes are recorded. Measurements include preparation, parsing,
Binding, startup checks, ownership and IR emission. Processes and library caches
are shared: these are fresh Compilation costs, not cold-process or editor latency.

The final edited Compilation receives 32 fixed warm-ups, then seven samples of 64
complete pipeline repetitions. Collection stays outside every interval. Reports
include allocation bytes and retained ownership storage before/after the warm run;
unchanged checks must not grow those tables. This does not measure total retained
heap size. Keep fresh/edit costs separate from the original warm-phase protocol;
normal allocation assertions remain in `InheritedPlanReuseTest`.

The 2026-10-07 report (`artifacts/benchmarks/inheritance-lifecycle-20261007.json`)
retains all fresh/edit samples, including initial process-cache costs. All 28 warm
samples allocate zero bytes, and the reported ownership tables remain unchanged.
Fresh and edited snapshots allocate memory; neither zero-allocation result nor
warm timing describes their cost. No before/after speedup is claimed.
