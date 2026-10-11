# Associated-Type inference measurements

After a whole-solution Release build, run from the repository root, without other builds or measurements:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --associated-inference > artifacts/benchmarks/associated-inference.json
```

`tests/Workloads/AssociatedInferenceWorkloads.cs` supplies both regression and measurement inputs. Eight axes vary declarations, requirements, overload candidates, nominal depth, shared Type graphs, converging refinement paths, external projection chains and unrelated edits. Each axis has three sizes and explicit/inferred variants: 48 inputs in total. Depth and shared graphs use 2/4/6 levels; the other axes use 1/8/24 items.

Each input records one fresh Compilation preparation/parse/Bind, then 16 fixed warm-ups and five samples of 16 whole-Bind iterations. GC collection is outside samples. The unrelated-edit axis also records adding one source and rebinding it. Every Bind must succeed. Reports retain all samples, input SHA-256, runtime/OS, build configuration, compiler/benchmark module identities and retained collection capacities. Capacities sum logical entries, not bytes; cold and edited runs include allocations. Save a verification input manifest alongside the report to identify the source snapshot.

The final 2026-10-08 Windows x64/.NET 10.0.12 run is retained as `artifacts/benchmarks/associated-inference-20261008/measurements-final.json`, with `inputs-final.json` identifying the source snapshot. All 240 warm samples allocated zero bytes. Inferred-input median whole-Bind times ranged from 6.77 to 8.20 ms; most explicit and inferred medians were near 7 ms. These are observations of one run including bundled-library work, not an isolated inference cost or evidence of a speedup. Cold timings include runtime startup effects. Larger graphs and different applications require their own measurements. The earlier pre-repair measurement remains in the same directory.

`AssociatedInferenceTest` and `AssociatedInferenceBoundaryTest` separately enforce warm zero allocation for successful/rejected inference, stable capacities through repeated binds and edit/recovery, and unchanged outcomes for the shared workloads. Session verification includes those regressions; the associated native fixtures compare generic calls, Function Items and Vec2 operations at O0/O2.
