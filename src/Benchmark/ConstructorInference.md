# Constructor inference measurements

After the whole-solution Release build, run:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --constructor-inference > artifacts/benchmarks/constructor-inference.json
```

Inputs are shared with `ConstructorInferenceReuseTest`. Each case uses a fresh Compilation, then 32 warm-up binds and seven samples of 64 binds. Collection is outside each sample. Cold measurements include preparation, parsing and the first Bind in a process whose runtime and bundled library have been prepared; they are not cold-process or disk timings.

The report compares explicit and inferred forms across candidate count, slot count, Type depth and call count. Ambiguity, missing evidence, conflicting Types, unproved correlation and changed fixed-binding selection are separate workloads. The full reference check is a correctness/measurement switch, not a language mode. Retained contract/dependency capacity and mask words accompany mappings, comparisons, candidate checks, correlations and fixed rechecks/reuses from the last Bind. Candidate checks include repeated waiting completion; correlation edges can be quadratic in declaration count, even though Best Candidate uses at most two linear scans. No whole-inference linear bound or speedup is claimed.

Successful warm paths and retained-capacity bounds are independently asserted by allocation regressions. Rejected paths may allocate diagnostic facts; the report retains their measured cost without changing assertions or adding warm-up. Timings depend on the host and are not test thresholds.

## Recorded run (2026-10-07)

`artifacts/benchmarks/constructor-inference-final-repaired-20261007.json` records 35 cases and 245 samples on .NET 10.0.12,
Windows x64, Release. All 175 successful warm samples allocate zero bytes. The 16-candidate inferred case reuses
all 16 fixed-check outcomes; the full reference path rechecks all 16, with the same selection. Contract capacity is
128 entries for 120 retained pair contracts. No growing retained storage was observed by the repeated-Bind regressions.

Warm medians are approximately 6.2–6.5 ms per whole Bind, including the bundled library; these totals do not isolate
inference cost or establish a speedup. Ambiguity, missing evidence and conflicting inputs allocate respectively
104, 520 and 64 bytes per Bind for failure state. Correlation and changed-selection samples are zero except for one
128-byte reference-correlation sample (64 Binds); that isolated allocation is retained without attributing a cause.
The earlier `constructor-inference-final-20261007.json` is retained too. Remeasurement followed the repair that keeps
independent signature-refusal facts when another argument is missing; the fixed warm-up and sample counts were not changed.
