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
```

- One folder: each row runs A `rounds` times.
- Two folders: each row runs A, B, A, ..., A (`rounds` + 1 runs of A, `rounds` of B) from one folder, under one toolchain root
  for both. The same folder given twice is the A=A pass, the variation a budget must exceed.
- `--toolchain` defaults to the checkout's `toolchain`; `<name>` must be unused under `artifacts/benchmarks` and `temp/build-chain`.
- Run it in a booked quiet slot, through `quiet_guard`.

## Rows

Six inputs, each at O0 and O2, each in two rows: a source build (`build <project>`, the whole command) and `build --Manifest`
on a pair `emit`ted beforehand by A (the chain alone). 24 rows in all, in the order below. The inputs are fixed by name and
source SHA-256 at A1 and not re-chosen; the series refuses an input whose bytes differ.

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
job caught every tool, and is A4's evidence of zero child processes). Per row and compiler: the first run reported apart and not
excluded, p50 and p95 by nearest rank over every run.

- **Included:** the root's creation and the runtime's start (`Kimi.dll` is not ReadyToRun, so every run JITs), project load,
  check, generation, publication and the native build; on the external path each tool's creation, image load, text parse and
  write; on the new path `tatara.dll`'s load and its work; LLD's worker threads on both.
- **Excluded:** Defender's scanning (charged outside the job; in wall time only); image-backed and file-cache pages from the
  memory figure, which is the job's peak committed memory; OS-cache-cold starts ("first" means the first run of a row).

## Fixed conditions

- The Release `dotnet Kimi.dll build` form, the same at A1 and A4; the NativeAOT publish only on explicit instruction.
- No CPU pinning and default tiering, both recorded. If pinning is ever used: the whole job, with `DOTNET_TieredCompilation=0`,
  equal on both sides.
- Recorded as [O2Corpus](O2Corpus.md) records them: machine, processors, OS, power scheme, .NET version, inherited `KIMI_*` and
  `DOTNET_*`, and the build, test and LLVM processes with their CPU seconds at start and end; plus Defender's real-time state, read
  only; the compilers' folders and `Kimi.dll` hashes, the toolchain root and the `dotnet` host.

## Judgment

A4 runs this series as an interleaved paired A/B: A = A4's base on the external tools, B = the candidate, with the A=A variation
measured. A1's stored figures are the first reference and the source of the budgets; compile time moves between A1 and A4, so
the stored figures alone cannot judge A4.

## Output

`artifacts/benchmarks/<name>/`: `rounds.json` (status, conditions, inputs, every run of every row and the statistics), written
also when a run fails, and `summary.md`. Build logs stay under `temp/build-chain/<name>/`.
