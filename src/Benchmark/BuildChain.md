# BuildChain

Single-shot `kimi build` as a user runs it, the baseline of the Tatara lane (unit A1) and the series that judges its switch at A4.
Every run is a new process, `dotnet Kimi.dll build ...`, created inside a Windows job object from its first instruction
(`PROC_THREAD_ATTRIBUTE_JOB_LIST`, as `TestProcess.Start` does). The job launcher is the series' own
(`Benchmarks/BuildChainMeasurements.Job.cs`). Timing is opt-in, never a Verify assertion.

## Command

The series builds no compiler: it takes built Release `Kimi.dll` folders (`dotnet build Kimigayo.slnx -c Release`, then
`src/Kimi/bin/Release/net10.0` of each checkout) and copies each into the run's folder under a neutral name (`c/A`, `c/B`).

```
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --build-chain <name> <folder A> [--rounds 20] [--toolchain <root>]
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --build-chain <name> <folder A> <folder B> [--rounds 20] [--toolchain <root>]
dotnet src/Benchmark/bin/Release/net10.0/Benchmark.dll --build-chain <name> <folder A> --tatara [--rounds 20] [--toolchain <root>]
```

- One folder: each row runs A `rounds` times.
- Two folders: each row runs A, B, A, ..., A (`rounds` + 1 runs of A, `rounds` of B) from one folder, under one toolchain root
  for both. The same folder given twice is the A=A pass, the variation a budget must exceed.
- `--tatara` (Tatara A3 §4): one folder; each row runs A, B, A, ..., A, where A is `Kimi.dll` single-shot on the external tools
  and B the same `Kimi.dll` as `dotnet Kimi.dll tatara` with standard input from a file holding the row's command as its one line
  (`build <project> --ToolchainRoot <root>` or `build --Manifest <pair> --ToolchainRoot <root>`, paths quoted), then end of input.
  The toolchain root must hold `tatara.dll`. After the O2 source rows of Hello and Milestone30 a session row runs B with k lines
  of that command, k1, k10, k1, ..., k1 (Session table below).
- With two sides the summary adds per row p50(B) / p50(A) of wall time and p95(B) - p95(A) of the job's peak committed memory.
- `--toolchain` defaults to the checkout's `toolchain`. `<name>` is one plain path segment (no separator, not `.` or `..`, not
  rooted), unused under `artifacts/benchmarks` and `temp/build-chain`.
- Arguments reach the child quoted as `CommandLineToArgvW` reads them (backslashes before the closing quote doubled).
- Run it in a booked quiet slot, through `quiet_guard`.

## Rows

Six inputs, each at O0 and O2, each in two rows: a source build (`build <project>`, the whole command) and `build --Manifest`
on a pair `emit`ted beforehand by A (the chain alone). 24 rows in all, in the order below. The inputs are fixed by name and
source SHA-256 at A1 and not re-chosen; the series refuses an input whose bytes differ.

If an input's source changes later, one commit re-fixes it: it changes the hash in `BuildChainMeasurements.Inputs` and in the table
below, and states that the row's stored A1 figures no longer compare. A4's judgment is the paired run, so it does not depend on them.

| Input | Source | SHA-256 |
| --- | --- | --- |
| Empty | generated: `public func main()` with `return` | `A80291B54336AE5BAAE2D979887B2287CDB7D325BDFCFD5B3DF7AD306FDC7815` |
| Hello | `docs/examples/Hello/Hello.kimi` | `145BE933320F459F09AB3407A0DB7C43C562016D9930605E6B0A18153536E731` |
| Milestone1 | `tests/milestones/Milestone1.kimi`, smallest IR | `9BB0047DD071BDF9A59247677C00C56205281B254421456C07CE3347D1343441` |
| Milestone17 | `tests/milestones/Milestone17.kimi`, median IR | `1914A012B7146EF344646C7847EEE8EFEEE599680DA2BA2172FCD49678A71581` |
| Milestone30 | `tests/milestones/Milestone30.kimi`, largest IR | `8A2D0FE6A0DFB4C5F20B7D33B9B39BF4A48033C83921C57C29B99AE32B932533` |
| Large | generated (`BuildChainMeasurements.Generate`) | `D4B7F9FA4884B5F1121A2C84244D03BAD1802DFDEF1A81146E614CBD9546871B` |

The milestones were chosen once on dev `ebd51d8d9` by the bytes of the emitted O0 IR (`<name>.ll`) of the 40 milestones that
build there (39, 41, 42 and 43 do not): Milestone1 20,507 bytes, Milestone17 42,682 (the 20th of 40, the lower median) and
Milestone30 439,090.

The large program has 32 structs of two `i64` fields and a call chain of 256 functions; each constructs one struct and runs a loop
of 8 steps with a branch (`LargeTypes`, `LargeFunctions`, `LargeSteps`). Its O0 IR is about 1.6 MB.

Every input is copied into the run's private folder `temp/build-chain/<name>/in/<input>-<level>/` (`source` and `pair`); nothing is
built beside `tests/milestones`.

## Figures

Per run: the wall time from just before the root's creation to its exit; the job's total CPU time, user and kernel, of every
process the job ever held; the job's peak committed memory; `TotalProcesses`, the number of processes the job held (it proves the
job caught every tool, and is A4's evidence of zero child processes). Per row and compiler: each compiler's first run in the row
(B's first is the row's second run) reported apart and not excluded, p50 and p95 by nearest rank over every run.

- **TotalProcesses** is 14 at O0 and 18 at O2 today: the root and, per tool, one process and one console host each. The tools
  are `opt` (verification), `llc`, `llvm-nm` twice, `llvm-readobj` and `lld-link` at O0 (14 = 2 × (1 + 6)); O2 adds `opt` twice
  (18 = 2 × (1 + 8)). Kimi starts each tool with a new hidden console, and the launcher's `CREATE_NO_WINDOW` gives the root one,
  each a `conhost` inside the job. The tools' console hosts are a cost of today's build. With this launcher, A4's expected value
  for "zero child processes" is 2: the root and its console host.
- **Session** (`--tatara`, not a budget): first-line time = p50 wall(k1); later-line time = (p50 wall(k10) - p50 wall(k1)) / 9;
  growth = p95 peak(k10) - p95 peak(k1), which shows a retained module or a leak. The times are wall times of the whole process; no
  clock is read in the compiler. B's `TotalProcesses` is expected to be 2 (the root and its console host).
- **Memory** is judged at p95 per row, not p50. The tools' allocator commits per LLD worker, so the figure depends on the
  processor count and on contention: a run below the high mode waited for a CPU. `rounds.json` also records per run
  `DiagnosticPeakProcessCommitBytes`, the largest single process's peak committed memory; it is a diagnostic only, never a budget,
  since it names different processes on the two paths.

- **Included:** the root's creation and the runtime's start (`Kimi.dll` is not ReadyToRun, so every run JITs), project load,
  check, generation, publication and the native build; on the external path each tool's creation, image load, text parse and
  write; on the new path `tatara.dll`'s load and its work; LLD's worker threads on both.
- **Excluded:** Defender's scanning (charged outside the job; in wall time only); image-backed and file-cache pages from the
  memory figure, which is the job's peak committed memory; OS-cache-cold starts ("first" means each compiler's first run in the
  row).

## Fixed conditions

- The Release `dotnet Kimi.dll build` form, the same at A1 and A4; the NativeAOT publish only on explicit instruction.
- No CPU pinning and default tiering, both recorded as observed: the affinity mask the root inherits, and `tiering` from the
  inherited `DOTNET_TieredCompilation` and `DOTNET_TC_*` (`default` when none is set). If pinning is ever used: the whole job,
  with `DOTNET_TieredCompilation=0`, equal on both sides.
- Recorded as [O2Corpus](O2Corpus.md) records them: machine, processors, OS, power scheme, .NET SDK version, inherited `KIMI_*`
  and `DOTNET_*`, and the build, test and LLVM processes with their CPU seconds at start and end; plus Defender's real-time state,
  read only; the toolchain root and the `dotnet` host; per compiler its folder, `Kimi.dll` hash, `ProductVersion` (the source
  commit) and runtime (its runtimeconfig's framework at the highest installed patch of that major.minor, the host's default
  roll-forward). A condition read that fails stops the run.

## Judgment

A4 runs this series as an interleaved paired A/B: A = A4's base on the external tools, B = the candidate, with the A=A variation
measured. A1's stored figures are the first reference and the source of the budgets; compile time moves between A1 and A4, so
the stored figures alone cannot judge A4.

Memory at A4: per row the paired p95(B) − p95(A) of the job's peak committed memory, with the A=A pass's p95 difference as the
floor. An absolute comparison with A1's stored figures holds only on the recorded machine.

## Output

`artifacts/benchmarks/<name>/`: `rounds.json` (status, conditions, inputs, every run of every row and the statistics), written
also when a run fails, and `summary.md` (its memory column is p95). Build logs stay under `temp/build-chain/<name>/`.
