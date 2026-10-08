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

The command shares `CompilerPlanMeasurements`: binding, ownership and emission phases with 32 warm-up iterations and
seven samples of 64 iterations, full collections outside each sample, and every operation required to remain valid.
`defaultContexts` reports the interpretation contexts recorded in the analyzed bodies (zero after the binding phase,
which resets ownership). Timing has no pass/fail threshold and is not speedup evidence; retain every report.
