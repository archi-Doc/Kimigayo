# Generic default context measurements

After a whole-solution Release build, run in isolation:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --generic-defaults > artifacts/benchmarks/generic-defaults.json
```

Use a fresh filename for each report. `VerificationWorkloads.GenericDefaults` supplies two axes, shared with the
zero-allocation regression `GenericDefaultTest.AllocationTests.WarmDefaultContextsAllocateNothing`:

- `siblings` at 4, 8 and 16: one omitted generic default whose replica builds, matches and destroys an `Option<T>`,
  expanded that many times in one body with an owner and a Copy Type alternating.
- `depth` at 1, 4 and 8: that many omitted defaults nested over it, each making the inner level's sample, called once
  with each Type.

The command shares `CompilerPlanMeasurements`: binding, check and emission phases with 32 warm-up iterations and
seven samples of 64 iterations, full collections outside each sample, and every operation required to remain valid.
From R6-check H9 the `check` phase times Bind, CheckStartup and Analyze; earlier reports' `ownership` phase timed the legacy ownership pass. Until H9 `defaultContexts` reported the interpretation contexts the legacy pass recorded; H9 removed the
column. Timing has no pass/fail threshold and is not speedup evidence; retain every report.
