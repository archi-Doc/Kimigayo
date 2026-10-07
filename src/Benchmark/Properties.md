# Property pipeline measurements

After a whole-solution Release build, run from the repository root:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --property-plans > artifacts/benchmarks/property-plans.json
```

The input is the unchanged Milestone 24 source: Non-Copy replacement, borrowing getters,
Contract storage-borrow witnesses and consuming functions. The shared compiler-plan runner
measures Binding, ownership analysis and IR emission separately, with 32 warm-ups and seven
samples of 64 iterations per phase. Collection is outside each sample; every operation must
succeed. Reports include source SHA-256, compiler/benchmark module identities, all samples
and retained Loan-storage sizes. Run in isolation, without builds or other measurements.

These are warm whole-phase observations, including bundled-library work, not cold startup,
native execution timing or evidence of a speedup. PropertyWitnessEmissionTest separately
asserts zero warm allocation; the milestone harness checks original-source O0/O2 output.

The 2026-10-07 run, `artifacts/benchmarks/property-plans-p24-20261007.json`, retains
all 21 samples at zero allocated bytes. Median whole-phase times are 6.46 ms for
Binding, 0.68 ms for ownership and 0.87 ms for emission on this Windows x64 host.
These observations do not isolate the accessor work from the rest of the program.
