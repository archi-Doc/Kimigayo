# Callable plan measurements

After a whole-solution Release build, run in isolation:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --callable-plans > artifacts/benchmarks/callable-plans.json
```

Use a fresh filename for each report. Four inputs cover fixed Callable reference selection, parameter-subtype ranking,
ranking with substituted per-call Origins, and nested inference with universal-Origin Item erasure. They are the exact inputs
of the strict allocation regressions in `ContextualFunctionReferenceTest`, `FunctionReferenceRankingTest` and
`ApplicabilityChainRegressionTest`, shared through `VerificationWorkloads`.
The regressions retain their original eight warm-up and eight measured iterations.

The opt-in runner shares `CompilerPlanMeasurements` with object measurements. Each binding, ownership and emission
phase has 32 warm-up iterations and seven samples of 64 iterations. Full collections occur outside each sample;
startup and ownership are refreshed after the binding phase. Every operation must remain valid. Reports preserve
all timings, thread-allocation samples, source hashes, configuration and runtime/platform identity.

Timing observations are not test thresholds or speedup evidence. Native O0/O2 regressions separately establish
behavior and cleanup. The object command keeps its existing inputs, phases and repetition counts.
