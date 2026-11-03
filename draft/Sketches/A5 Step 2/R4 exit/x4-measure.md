# X-4: R4 exit measurements (2026-11-03)

Base: `origin/dev` `3283d9aaa`, detached worktree `bin/worktrees/r4-x4` (toolchain junction, `dotnet restore Kimigayo.slnx`, Release;
removed afterwards). Compiler id: Kimi.dll MVID `d2e24662-891e-4c12-8a7c-dcbfbed3ff1a` (md5 `5aff22b5…`), checked in all 10 Benchmark
reports (`compiler` "0.1.1 (d2e24662-…)") and printed by each probe process. Files: `x4/` beside `r4-notes/` in this scratchpad
(`run-x4.sh`, `summarize-x4.py`, `summary.md`, `out/` raw, `harness/` probe source, `x4-areas.ps1`, `areas/`, `alloc-evidence/`).

## 1. Sizes, hotspots, duplication

`C:\Users\bwff1\repos\archi-Doc\Kimigayo\artifacts\benchmarks\2026-11-03-r4-exit-sizes.md` (`x4-areas.ps1`; points R3 exit `e2774e296`,
`5c42b5773`, `66c856d81`, `df7ae4c45`, `3283d9aaa`). Mir 18,687 lines / 121,164 tokens in 93 files (Mir/Analysis 9,795, A5 6,512; Build
4,327; Validator 1,701; root 2,864); old Analysis 16,020 / 110,898 in 50. No Mir file over 5,000 tokens (max `MirOrigins.Chains` 4,394).
Hotspots (all streams, 346 commits): `MirBorrows.cs` 43, `MirBuilder.Values.cs` 43, `MirValidator.cs` 38, `MirTypes.cs` 32. No six-line
clone in Mir; recorded candidates G191 N1, G136.

## 2. Warm allocation

`pwsh -NoProfile -Command "./scripts/verify.ps1 -Mode Unit -TestPurpose Allocation"`: PASS, xUnitTest Total 1,199, Failed 0, Not Run 3
(Explicit); input stability passed; evidence `artifacts/verify/20261103-035510-856-unit` (copied to `x4/alloc-evidence/`). The probe
also shows warm 0 B for each step separately (MIR build + A1-A6, collection, old analyses) on Hello, Milestone26 and all 44 milestones.

## 3. Timing: "MIR build + A1-A6" against the old analyses (PLAN10 §18 P3)

Registry: `artifacts/heavy-runs/R4-x4-timing-20261103-130057.run`, the only `.run` file throughout; timing 13:00:57-13:10:06, file removed.
Conditions: CPU 2 only (`start /wait /b /high /affinity 4`; reports say `processors: 1`), High priority, `DOTNET_TieredCompilation=0`,
`KIMI_PIPELINE`/`KIMI_CODEGEN` unset, one folder `x4/t/` (Benchmark copy and probe beside each other), 5 interleaved rounds of
`--compiler-pipeline`, `--collection-cost`, the probe on Hello+Milestone26 (cold 20, warm 100) and on the 44 milestones (cold 5, warm 10).
Numbers: median over rounds of each round's median. Both sides come from the same process and operation, so no A/B binary is involved.

- **Instrument A** (`--compiler-pipeline` stage 3 `BuildMir` minus `--collection-cost`'s cold collection, against stage 4, the old
  control flow + ownership; per compilation):

| Input | MIR build + A1-A6 ms | Old ms | MIR/old | MIR MB | Old MB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Scalar 1 / 32 / 64 / 128 | 6.72 / 6.55 / 7.58 / 8.13 | 0.08 / 0.32 / 0.57 / 2.08 | 86 / 20 / 13 / 3.9 | 2.9-3.8 | 0.03-1.8 |
| Branching 1 / 32 / 64 / 128 | 6.97 / 4.48 / 6.97 / 9.02 | 0.09 / 2.73 / 2.22 / 4.66 | 81 / 1.6 / 3.1 / 1.9 | 2.9-4.2 | 0.04-3.3 |
| Generic 1 / 32 / 64 / 128 | 4.32 / 14.59 / 20.75 / 38.11 | 1.80 / 11.83 / 18.40 / 41.69 | 2.4 / 1.2 / 1.1 / 0.9 | 3.2-11.4 | 1.0-15.7 |

  Branching 32's rounds vary (MIR 4.35-10.10, old 2.10-3.87 ms; GC charged to the stage), so its ratio is weak.
- **Instrument B** (the probe: the steps of `Compilation.Analyze` timed separately on one compilation after Bind and CheckStartup):
  Hello cold 4.42 ms vs 0.055 ms (81×), warm 3.41 vs 0.047 (73×); Milestone26 cold 4.14 vs 0.318 (13×), warm 3.59 vs 0.258 (14×);
  44 milestones summed: cold 228.3 vs 117.0 ms (2.0×), warm 166.4 vs 111.9 (1.5×); the 40 that check cleanly in the probe (not 39, 41,
  43: the old analysis returns at BodyCount 0; not 42: errors in the single-file Application probe) cold 211.2 vs 116.6 (1.81×), warm
  151.6 vs 111.6 (1.36×). Collection adds 0.2 ms per small program (14.4 ms cold over the 44).
- Reading: the MIR side has a fixed ~4-7 ms per compilation (every compilation builds, validates and analyzes the library's bodies;
  since R5-M168 A3 and A5 judge only the source's; PLAN G337's known cost, O2/O3 deferred after R7), so small programs are 13-86× the old analyses; at size the ratio falls to
  1.1-1.2× (Generic 32/64) and 0.9× (Generic 128). P3 allows a regression; it is recorded, not judged.

## Not done here (the X-4 row's other items, outside this brief)

V16 shapes, the default pipeline's `-DiagnosticSnapshot` against the step-2 base.
