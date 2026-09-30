# Compiler pipeline measurements

`CompilerPipelineBenchmark` measures valid scalar functions, calls and optional
conditional returns at 1, 32 and 128 helper functions. Every helper is called from
`main`. Setup requires successful parsing, binding, startup, ownership verification
and nonempty LLVM IR. Failed operations throw, including during measurement.

| Entry | Included work | Initial state |
| --- | --- | --- |
| `FreshParse` | Compilation creation, target preparation, source registration and tokenization/parsing | New compilation per operation |
| `FreshCompileToIr` | `FreshParse`, binding, startup, ownership, checked lowering and IR serialization | New compilation per operation |
| `RebindAndStartup` | Binding pipeline and application startup check | Existing syntax and retained semantic storage |
| `ReanalyzeOwnership` | Control flow, ownership verification and cleanup planning | Bound compilation and retained analysis storage |
| `ReemitIr` | Emission precondition checks, lowering and IR serialization | Verified compilation and retained emission storage |

Fresh state is not a cold process: BenchmarkDotNet warms the runtime. Reused-phase
times do not sum to fresh compilation time. Source construction occurs in setup.
IR is serialized to `TextWriter.Null` during measurement; output buffering, file
I/O, CLI startup, project/dependency discovery, diagnostic presentation, external
LLVM optimization and linking are excluded. This is a synthetic scalar workload,
not coverage of every language feature or an end-to-end CLI build benchmark.

The existing Benchmark configuration uses MediumRun and MemoryDiagnoser. Reports
include time distributions, managed allocation per operation and GC counts;
allocation is not peak or retained process memory. Timing reports do not replace
allocation/reuse regression assertions.

## Validation and measurement

Use an isolated checkout when another agent is changing the compiler. Restore
dependencies there, then complete the repository Session verification before
claiming verified support for this benchmark:

```powershell
dotnet restore Kimigayo.slnx
./scripts/verify.ps1 -Mode Session -Name compiler-pipeline
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --compiler-pipeline-check
```

The check command executes every input and entry twice, without timing assertions.
Save its output under `artifacts/verify/`. Run timing measurements when other
builds, tests and CPU-intensive work have stopped:

```powershell
$run = Join-Path 'artifacts/benchmarks' ('compiler-pipeline-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
git rev-parse HEAD > (Join-Path $run 'commit.txt')
git status --porcelain=v1 > (Join-Path $run 'status.txt')
git diff --binary HEAD > (Join-Path $run 'changes.patch')
dotnet --info > (Join-Path $run 'dotnet-info.txt')
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --filter '*CompilerPipelineBenchmark*' --artifacts $run
```

Prefer a clean committed checkout; a diff does not capture untracked files.
Retain BenchmarkDotNet logs and reports, including failed runs. Compare repeated
runs with identical inputs, runtime, configuration and machine conditions. Keep
native O0/O2 build measurements separate when extending this suite. NativeAOT is
not required or exercised by this workflow.
