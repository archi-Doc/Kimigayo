# Object plan measurements

After a whole-solution Release build, run:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --object-plans > artifacts/benchmarks/object-plans.json
```

Use a fresh evidence filename per run. `StoredObjectViewTest` shares the exact direct/stored-handle inputs through
`tests/Workloads/VerificationWorkloads.cs`; its zero-allocation assertions keep 32 warm-up and 64 measured iterations.
The opt-in runner measures ownership analysis and IR emission separately, with 32 warm-up iterations and seven samples
of 64 iterations. Each sample follows a full collection outside the measured interval. It reports every timing and
thread allocation count, source hashes and runtime/platform identity, and fails if an input stops verifying or emitting.

Native fixtures separately check values, retained owner Loans and allocation/free counts at O0/O2. Timings are observations,
not pass thresholds or speedup evidence without matched before/after runs on the same machine and configuration.
