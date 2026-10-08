# Element Place measurements

After a whole-solution Release build, run in isolation:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --element-places > artifacts/benchmarks/element-places.json
```

Use a fresh filename for each report. `VerificationWorkloads.ElementPlaces` supplies four axes at 4 and 16 statements,
shared with the zero-allocation regression `ElementBorrowEmissionTest.WarmElementPlaceWorkloadsAllocateNothing`:

- `reads` and `updates`: value-path controls, `total += xs[0].n` and `xs[0].n += 1` on an owned Array element's part.
- `borrows`: `bump(xs[0].n@uniq)` and `show(xs[0].n)`, the part borrowed exclusively and shared through the element route.
- `paths`: `bump(b.items[0].n@uniq)` below an Array reached through a `uniq` struct reference.

The command shares `CompilerPlanMeasurements`: binding, ownership and emission phases with 32 warm-up iterations and
seven samples of 64 iterations, full collections outside each sample, and every operation required to remain valid.
Compilers before PLAN G59 U3 cannot verify the `borrows` and `paths` inputs; compare them on the controls only. Timing has
no pass/fail threshold and is not speedup evidence; retain every report.
