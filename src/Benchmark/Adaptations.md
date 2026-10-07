# Explicit adaptation measurements

After a whole-solution Release build, run with a fresh evidence filename:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --adaptation-plans > artifacts/benchmarks/adaptation-plans.json
```

`tests/Workloads/AdaptationWorkloads.cs` supplies 1, 8 and 32 independent generic Functions. Each adapts one fixed
input through `@s`, with `s is owner or object` and `T is ObjectPayload`, and has owner, obj, rc and arc calls.
The rc call uses bool; the others use i32. Increasing Function count exercises definition case analysis and
closed factory generation together. `GenericAdaptationTest` uses the identical single-Function input for its
zero-allocation Binding, ownership and emission regressions.

The shared `CompilerPlanMeasurements` runner measures these three phases separately, after 32 warm-up iterations,
with seven samples of 64 iterations. Full collections occur outside each measured interval. Reports retain every
timing and thread allocation count, source hashes, compiler/benchmark identities and runtime/platform details.
Each operation must remain valid. Source construction and initial compilation are outside the measurements.

Native regressions separately check payload values and allocation/free counts at O0/O2. These compiler timings
are observations, not thresholds; claim a speedup only from matched before/after runs on the same machine and
configuration. Do not run other builds or measurements concurrently.
