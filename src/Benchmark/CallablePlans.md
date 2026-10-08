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

## Local-region and Callable scaling

`--local-regions` uses the same fixed 32 warm-ups, seven samples and 64 iterations per sample, with
three axes at 4, 8 and 16: normalized Callable candidates, anonymous result sources, and local region
variables with independent joins and a loop. Inputs are shared with `CallableRegionScalingTest`.
The candidate workload checks an uninstantiated generic body; its emission timing does not measure
native execution of the selected generic call. Concrete candidate execution has separate functional fixtures.

Reports include packed borrow-table payload bytes, region-flow/retention/referent array and list payload
bytes, region hash-index entry capacities, and peak requested region-flow cells. Payload counts exclude
object headers, hash buckets and shared pre-existing CFG/worklist storage; they are not total heap size.
Flow storage is reused per borrowed root and bounded by operations times sparse relevant storage paths,
with the common geometric capacity policy. Referent queries retain their union of targets; this can grow
with both queried values and reachable targets. Regression checks keep the packed-table budget unchanged,
assert stable repeated capacities and zero warm allocation, and bound retained flow capacity by its peak
request. Timing has no pass/fail threshold; retain every report, including failures.

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --local-regions > artifacts/benchmarks/local-regions.json
```

`--fixed-origin-loans` uses the same phases and fixed measurement conditions for premise-based fixed-Origin fits.
The unchanged `FixedInputValueCall` is a control. Calls, independent borrowed roots and CFG joins grow separately
at 4/8/16; the inputs and capacity/allocation checks are shared with `CallableRegionScalingTest`. The region
index count also includes retained call-input contract entries; byte counts exclude dictionary entry payloads
and buckets. Origin fitting adds no runtime Loan representation or extra acquisition.

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --fixed-origin-loans > artifacts/benchmarks/fixed-origin-loans.json
```
