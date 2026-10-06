# View and copy measurements

After a Release build:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --view-plans > artifacts/benchmarks/view-plans.json
```

This uses the fixed runner and sampling conditions of [CallablePlans.md](CallablePlans.md):
32 warm-ups, seven samples of 64 iterations, and separate Binding, ownership and emission measurements.
`VerificationWorkloads.ExclusiveViews` and `SliceCopies` are shared with the strict allocation regressions.
The report records source hashes, configuration, environment, time and bytes. It establishes no speedup ratio.

`DynamicArrayCostTest` independently instruments native allocation and growth transfers for empty, zero-sized,
preallocated and nonempty copies, plus repeated views. The source copy loop shares `appendCopies`, reserves before
reading elements and publishes length after initialization. It calls no per-element append or growth helper.

Native O0/O2 measurements from 2026-10-06 are retained under `artifacts/benchmarks/20261006-exclusive-views`
and `20261006-slice-copy`, including source/compiler hashes and copied inputs. Each binary ran seven times;
the workloads form 100,000 parent/child views or copy 1,024 elements 10,000 times. Timings include process startup
and have no previous-implementation baseline.

The preserved workloads can be measured again in isolation:

```powershell
./src/backend/windows-x64/milestone-harness.ps1 -Milestone 0 -Configuration Release -Source src/Benchmark/Kimi/ExclusiveViews.kimi -Expected "checksum 4999950000`n" -WorkRoot ([IO.Path]::GetFullPath('artifacts/benchmarks/exclusive-views')) -Runs 7
./src/backend/windows-x64/milestone-harness.ps1 -Milestone 0 -Configuration Release -Source src/Benchmark/Kimi/SliceCopies.kimi -Expected "checksum 10250000`n" -WorkRoot ([IO.Path]::GetFullPath('artifacts/benchmarks/slice-copies')) -Runs 7
```
