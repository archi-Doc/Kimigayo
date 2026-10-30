# Compiler pipeline measurements

`--compiler-pipeline` measures six contiguous stages of each fresh compilation.
Each input has a shape and a size of 1, 32, 64 or 128. The `Scalar` and `Branching`
shapes contain that many scalar helper functions, the second with a conditional
return, each called from `main`. The generic-heavy `Generic` shape contains that many
item Types, each instantiating Array, Dictionary, Option, a two-parameter generic
struct and two generic functions under a Contract bound. Stages 5 and 6 are the
pre-R5 collect-plus-codegen baseline ([O2Corpus.md](O2Corpus.md)). Source construction
is outside timing.

| Stage | Report name | Included work |
| --- | --- | --- |
| 1 | `PreparationTokenizerParser` | `Compilation.CreateForTest`, target `Prepare`, document creation and `Kotonoha.AddSource` (registration, tokenization and parsing) |
| 2 | `BindingAndStartup` | `Compilation.Bind` and application `CheckStartup` |
| 3 | `MirPublishBuildValidate` | `Compilation.BuildMir`: HIR publication, MIR construction and validation of every body, the source's and the Kimi library's, and the MIR analyses (R4; V18 later times them as their own intervals); and the instance collector, which lays out every by-value use of the items it admits (R5; on every pipeline since DECISIONS BU-48) |
| 4 | `ControlFlowAndOwnership` | `Compilation.Analyze`: the old analysis; on every pipeline it reuses stage 3's MIR and checked set, memoized, and keeps them for generation |
| 5 | `EmissionLowering` | empty: the collector lays out every by-value use in stage 3 (the old generator's lowering until R6-gen's G4) |
| 6 | `LlvmIrWriting` | `Compilation.WriteIr`, the generation entry (gate, output roots and `ModuleCodegen`) |

Every operation creates a new `Compilation` and runs these stages exactly once in
order. Stage 3 builds, validates and analyzes the MIR and collects its checked set
(`Compilation.BuildMir`), each memoized per program version, so stage 4's `Analyze`
reuses them on either pipeline; the old analysis still decides (R3-R6). Since DECISIONS
R5-M156 every pipeline's `Analyze` builds, analyzes and collects the MIR and publishes its
records, so `KIMI_PIPELINE` changes no stage: stage 3 is work every check runs.
Each result reports `mirBodies`, the bodies stage 3
builds, and the stage's mean time and allocation per body (`mirMeanMicrosecondsPerBody`,
`mirMeanAllocatedBytesPerBody`): a fresh compilation's builder starts empty, so these are
first-build costs, which a warm rebuild of the same compilation does not pay.
Stage 2 includes lazy Binding construction and embedded Kimi library loading and
parsing; it is not exclusively semantic analysis of user source. Stage 4 similarly
includes lazy emitter initialization. Success checks belong to their respective
stages; stage 3 requires a MIR without construction records or validator findings.
The compiler implementation and public APIs are unchanged.

A process follows `KIMI_PIPELINE`, and the report's `compiler` names the non-default
pipeline (`[mir]`). `--compiler-pipeline` measures MIR code generation, the one code
generator since R6-gen's G4 (DECISIONS R5-M160): the collector lays out every by-value use
in stage 3 and code generation reads those layouts as it writes, so stage 5 is empty and
stage 6 is the whole generation entry; the stage names stay, so reports remain comparable
with earlier ones of the old generator (DECISIONS R5-M94). An input that MIR code generation
does not generate fails the run with the entry's failure. Its cost against an old
generator's report is `--collection-cost`'s `cold` collection plus stages 5-6, against
that report's stages 5-6 (DECISIONS R5-M148).

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
programs, not representative coverage of every language feature.

`CompilerPipelineBenchmark.FreshCompileToIr` remains a BenchmarkDotNet entry for
the same whole pipeline without timestamp/allocation boundary reads. Its twelve cases
use the existing MediumRun and MemoryDiagnoser configuration. The old mixed
fresh/reused-state stage entries are removed: their timings cannot describe the
fraction of a fresh compilation. Other feature-specific benchmarks are unchanged.

## Check cost (`--analyze-cost`)

`--analyze-cost` measures the check of the `Generic` shape (sizes 1, 32, 64 and 128) on the process's pipeline (the report's
`compiler` names a non-default one; schema version 3). Every pipeline's `Analyze` builds, validates, analyzes and collects the MIR
and publishes every record of its checked set (DECISIONS BU-48, R5-M156), so its former `legacy`, `mir` and `sel` configurations
measured the same check and one remains (DECISIONS R5-M160). `freshCheck` is one fresh compilation's
`Bind`, `CheckStartup` and `Analyze` (the stages 2 to 4 above; preparation and parsing are untimed); `warmAnalyze` repeats
them on one compilation without an edit, as an LSP server does. Four warmups precede seven samples of four operations;
each result reports mean milliseconds and current-thread allocated bytes, the managed heap the checked compilation retains
(`retainedBytes`, after full collections) and its growth over the warm Analyzes (`warmRetainedGrowthBytes`) and the collected
items. Follow the
measurement protocol below and save `--analyze-cost` output beside it.

## Collection cost (`--collection-cost`)

`--collection-cost` times the instance collector inside one compilation, for each of the twelve inputs above (PLAN10-P3's
collection share at the R5, R6-gen and R6-check exits; DECISIONS R5-M148). Each operation binds a fresh mir-pipeline compilation
and builds, validates and analyzes its MIR untimed with the two components `Compilation.BuildMir` composes (`MirBuilder`,
`MirAnalysis`), not with `BuildMir`, which also collects. It then times three fresh collectors over that program in order:
`cold`, the compilation's first collection, with its `Layout` (the collection-and-layout share and the check-time cost of
collection); `freshLayout`, a new `Layout` with the Type context's memos warm; `warm`, the compilation's `Layout` again.
`layoutMedianMilliseconds` is freshLayout − warm and `firstQueriesMedianMilliseconds` cold − freshLayout, per operation.
Four warmups precede seven samples of four operations; each variant reports median, mean and minimum milliseconds, mean
allocated bytes and its per-operation ticks.
`--collection-cost-check` times nothing: it requires the three collections to equal one another and the compilation's own
checked set (its `Analyze`'s) element by element, the items' keys in admission order and the records' codes and locations; the
comparison to fail on the same collection reordered and on a changed record code or location; and `cold` to allocate more than
`warm` on every input, so a warm `cold` fails. MIR code generation's cost against an old generator's report is then `cold` +
its stages 5-6 against that report's stages 5-6, without a stage-3 subtraction, which is below the noise on small inputs
(DECISIONS R5-M147). Follow the measurement protocol below.

## Validation

In an isolated checkout, restore dependencies and follow repository verification:

```powershell
dotnet restore Kimigayo.slnx
./scripts/verify.ps1 -Mode Session -Name compiler-pipeline
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --compiler-pipeline-check
```

The check runs every input repeatedly. It verifies nonempty IR from stage 6, the public
`Compilation.WriteIr`, its agreement with uninstrumented fresh compilation, monotonic boundaries
and conservation of stage time/allocation totals. It has no timing thresholds. Save output under `artifacts/verify/`.

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
$report.results | Where-Object { $_.size -eq 128 -and $_.shape -eq 'Branching' } |
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
