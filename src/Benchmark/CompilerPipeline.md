# Compiler pipeline measurements

`--compiler-pipeline` measures six contiguous stages of each fresh compilation.
Inputs contain 1, 32 or 128 scalar helper functions, with or without conditional
returns. Every helper is called from `main`. Source construction is outside timing.

| Stage | Report name | Included work |
| --- | --- | --- |
| 1 | `PreparationTokenizerParser` | `Compilation.CreateForTest`, target `Prepare`, document creation and `Kotonoha.AddSource` (registration, tokenization and parsing) |
| 2 | `BindingAndStartup` | `Compilation.Bind` and application `CheckStartup` |
| 3 | `MirPublishBuildValidate` | `Compilation.BuildMir`: HIR publication, MIR construction and validation of every body, the source's and the Kimi library's, and the MIR analyses (R4; V18 later times them as their own intervals) |
| 4 | `ControlFlowAndOwnership` | `Ownership.Analyze` |
| 5 | `EmissionLowering` | `LlvmEmitter.TryPrepare`: emission preconditions, layouts, ABI and lowered output model |
| 6 | `LlvmIrWriting` | `EmissionModule.WriteIr`: serialize that prepared model to LLVM IR |

Every operation creates a new `Compilation` and runs these stages exactly once in
order. Stage 3 builds the MIR as the `mir` pipeline's `Analyze` does, before the old
analysis, which still decides (R3-R5); a `legacy` compilation does not run it, so its
cost is the total minus stage 3. Each result reports `mirBodies`, the bodies stage 3
builds, and the stage's mean time and allocation per body (`mirMeanMicrosecondsPerBody`,
`mirMeanAllocatedBytesPerBody`): a fresh compilation's builder starts empty, so these are
first-build costs, which a warm rebuild of the same compilation does not pay.
Stage 6 does not call `LlvmEmitter.WriteIr`, which would repeat lowering.
Stage 2 includes lazy Binding construction and embedded Kimi library loading and
parsing; it is not exclusively semantic analysis of user source. Stage 4 similarly
includes lazy emitter initialization. Success checks belong to their respective
stages; stage 3 requires a MIR without construction records or validator findings.
The compiler implementation and public APIs are unchanged.

Seven boundaries record monotonic timestamps and current-thread allocated bytes.
Differences between adjacent boundaries partition the same operation's first-to-last
interval. Stage percentages use summed stage ticks divided by summed total ticks,
so they sum to 100% apart from floating-point rounding. Mean stage times and
allocated bytes likewise sum to the reported total. Measurement bookkeeping and
report creation are outside those intervals; boundary instrumentation overhead is
included and is not subtracted. GC pauses are charged to the stage in which they
occur, not necessarily the stage whose allocations caused the collection.

The fixed protocol is eight uninstrumented warmup compilations followed by seven
samples of eight fresh compilations per input. There is no retry-until-stable loop,
forced GC or outlier removal. JSON retains per-sample total/stage ticks, allocated
bytes and process-wide Gen0/1/2 collection count deltas, together with clock frequency,
input hashes/sizes, compiler and benchmark identities, runtime, OS and configuration.
Allocation is current-thread managed allocation, not peak or retained memory.
Warmups warm the process/runtime; fresh compilation does not mean cold-process startup.

IR is serialized to `TextWriter.Null` during timing. Output buffering, file I/O,
CLI startup, project/dependency discovery, diagnostic presentation, external LLVM
optimization, linking and native execution are excluded. The inputs are synthetic
scalar programs, not representative coverage of every language feature.

`CompilerPipelineBenchmark.FreshCompileToIr` remains a BenchmarkDotNet entry for
the same whole pipeline without timestamp/allocation boundary reads. Its six cases
use the existing MediumRun and MemoryDiagnoser configuration. The old mixed
fresh/reused-state stage entries are removed: their timings cannot describe the
fraction of a fresh compilation. Other feature-specific benchmarks are unchanged.

## Validation

In an isolated checkout, restore dependencies and follow repository verification:

```powershell
dotnet restore Kimigayo.slnx
./scripts/verify.ps1 -Mode Session -Name compiler-pipeline
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --compiler-pipeline-check
```

The check runs every input repeatedly. It verifies nonempty IR and exact agreement
between split lowering/writing and the public emitter, agreement with uninstrumented
fresh compilation, monotonic boundaries and conservation of stage time/allocation
totals. It has no timing thresholds. Save output under `artifacts/verify/`.

## Measurement

Run with other builds and CPU-intensive work stopped, in a clean committed checkout:

```powershell
$run = Join-Path 'artifacts/benchmarks' ('compiler-pipeline-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
git rev-parse HEAD > (Join-Path $run 'commit.txt')
git status --porcelain=v1 > (Join-Path $run 'status.txt')
git diff --binary HEAD > (Join-Path $run 'changes.patch')
dotnet --info > (Join-Path $run 'dotnet-info.txt')
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --compiler-pipeline > (Join-Path $run 'phases.json') 2> (Join-Path $run 'phases.stderr.log')
if ($LASTEXITCODE -ne 0) { throw 'Compiler phase measurement failed; retain the logs.' }
```

Inspect the six stages and their fractions, for example:

```powershell
$report = Get-Content (Join-Path $run 'phases.json') -Raw | ConvertFrom-Json
$report.results | Where-Object { $_.functions -eq 128 -and $_.branches } |
    ForEach-Object { $_.stages } | Format-Table name, meanMilliseconds, percentOfTotal, meanAllocatedBytes
```

Optional whole-pipeline BenchmarkDotNet distribution/GC measurement:

```powershell
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --filter '*CompilerPipelineBenchmark*' --artifacts $run
```

Keep raw results and failed-run logs. A diff does not preserve untracked source
files. Compare repeated runs on identical inputs, configuration and machine
conditions before claiming a speedup. Phase ratios describe these measured inputs
only. Neither measurement replaces functional or allocation/reuse regressions.
NativeAOT is not exercised by this workflow.
