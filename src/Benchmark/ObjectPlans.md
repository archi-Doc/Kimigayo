# Object plan measurements

After a whole-solution Release build, run:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --object-plans > artifacts/benchmarks/object-plans.json
```

Use a fresh evidence filename per run. `StoredObjectViewTest` shares the exact direct/stored-handle inputs through
`tests/Workloads/VerificationWorkloads.cs`; its zero-allocation assertions keep 32 warm-up and 64 measured iterations.
`SharedObjectRuntimeTest` and `SharedArcRuntimeTest` share the rc/arc factory/clone input with the same fixed counts.
Reports name all four workloads; only the factory spelling changes between rc and arc.
The opt-in runner measures ownership analysis and IR emission separately, with 32 warm-up iterations and seven samples
of 64 iterations. Each sample follows a full collection outside the measured interval. It reports every timing and
thread allocation count, source hashes and runtime/platform identity, and fails if an input stops verifying or emitting.

Native fixtures separately check values, retained owner Loans and allocation/free counts at O0/O2. Timings are observations,
not pass thresholds or speedup evidence without matched before/after runs on the same machine and configuration.

For uninstrumented native finalization timings, run after the same Release build:

```powershell
./src/backend/windows-x64/benchmark-object-finalization.ps1 -Name before
```

The two fixed workloads perform 1,048,576 factory/clone/final-release rounds and 262,144 two-entry Dictionary owning
iterations. Each object's destructor checks its payload; the first execution checks stdout, stderr and exit status.
The harness builds each unchanged source once at O0/O2 and runs each binary seven times, without allocation-audit
instrumentation. The JSON report retains all samples, source/compiler hashes and machine identity. Times include
process startup; there is no warm-up run. Keep these counts unchanged for before/after comparisons, and do not run
builds or other measurements concurrently. Allocation/free guarantees remain separate native regressions.

Both rc and arc run by default; select `-Mode Rc` or `-Mode Arc` for one mode. Arc sources are derived from the fixed rc
inputs by replacing only factory/mode spellings, then retained with their source hashes in the measurement directory.
The round counts, payloads, checksums and repetition protocol are identical. See [SharedObjects.md](SharedObjects.md)
for the protocol coverage and remaining completion boundaries.
