# G337: the fresh small-build fixed cost after R6-gen (design phase)

Standing request: the owner accepted PLAN10-P3's small-build cost (verbatim 「(a) 受け入れる」, 2026-10-31) with a measured breakdown of
the fixed cost as R5's next unit (PLAN G337). This sketch measures where the cost goes and proposes options; no source change.
Base `origin/dev` `b9d44fd18`. Instrument: a throwaway console harness (assembly `Benchmark`, InternalsVisibleTo) that replicates
`MirAnalysis.Run` step by step in its order, plus throwaway timers around the source and library halves of `Binding.PublishHir` and
`MirBuilder.Build`, reverted. Raw outputs: `C:\Users\bwff1\AppData\Local\Temp\claude\r5\g337\probe-run*.txt` (scratch).

## 0. PLAN10-P3's absolute figures are 8 times too large

`summarize-p3.py` reads each sample's `stageTicks` / `stageAllocatedBytes`, which `CompilerPipelineMeasurements.Measure` sums over
`iterationsPerSample` (8) compilations, and never divides by 8. Every ms and MB of the `--compiler-pipeline` table in
`summary-output.txt` is 8 times the per-compilation value; ratios are unaffected. Per compilation (the raw JSON's `meanMilliseconds`):
Scalar 1 B total 30.1 ms, A default build 17.1 ms (A total 31.1 minus A's stage 3, 13.95: the benchmark calls `BuildMir` on A too),
B stage 3 12.7 ms and 3.83 MB (the table: 99.84 ms, 30.73 MB). So the accepted "+72 to +120 ms" is **+9 to +15 ms per fresh
compilation** (1.36-1.78 unchanged). `--collection-cost` and `--analyze-cost` already report per operation. The whole difference is
B's stage 3; stages 4-6 are under 1 ms on the small inputs.

## 1. Census (deterministic)

| Input | Source bodies | Library bodies built and analyzed | Checked set (roots) | Library body items | Generated (library) | Library Fn-call closure |
| --- | ---: | ---: | --- | ---: | ---: | ---: |
| Scalar 1 | 3 (19 statements) | 215 (2,892 blocks, 6,796 statements, 2,533 locals) | 3 (3): Body 3 | 0 | 2 (0) | 0 |
| Hello | 1 (9) | 215 | 2 (1): Body 1, DropGlue 1 | 0 | 2 (0) | 0 |
| Milestone26 | 9 (174) | 215 | 10 (9): Body 9, DropGlue 1 | 0 | 9 (0) | 0 |
| Generic 1 | 8 (198) | 215 | 30 (5): Body 24, LegacyRow 3, DropGlue 2, Builtin 1 | 16 | 29 (16) | 9 |

The 215 library bodies are Fn 205, Getter 9, StaticInit 1, every one a body of `KimiLibrary`'s module. "Library Fn-call closure" is
the direct `Fn` call closure from the source bodies (a lower bound of A4's reach: no requirement, virtual, value or drop edges).

Cold allocation of stage 3 per fresh compilation (bytes are deterministic):

| Input | Stage 3 | Library: HIR publish / build / A1, A2, A4 local / A3, A5 | Source bodies | Program steps, collection |
| --- | ---: | --- | ---: | ---: |
| Scalar 1 | 3.91 MB | 0.11 / 2.75 / 0.18 / 0.76 MB (3.80 MB, 97 %) | 27 KB | 34 KB, 50 KB |
| Hello | 3.91 MB | 0.11 / 2.75 / 0.18 / 0.76 MB (3.80 MB, 97 %) | 22 KB | 34 KB, 49 KB |
| Milestone26 | 4.07 MB | 0.11 / 2.78 / 0.19 / 0.77 MB (3.84 MB, 94 %) | 141 KB | 34 KB, 53 KB |
| Generic 1 | 4.14 MB | 0.11 / 2.89 / 0.17 / 0.64 MB (3.81 MB, 92 %) | 196 KB | 34 KB, 98 KB |

## 2. Time per sub-stage (indicative: unpinned, `DOTNET_TieredCompilation=0`, 61 fresh compilations, medians)

Shared machine, other sessions running; a tiered run gave the same shares. A pinned run waits for a quiet-slot GO.

| Sub-stage (ms) | Scalar 1 | Hello | Milestone26 | Generic 1 |
| --- | ---: | ---: | ---: | ---: |
| HIR publish, library (`Binding.PublishHir`) | 2.34 | 2.31 | 2.16 | 1.93 |
| MIR construction + per-body validation, library | 4.32 (validation 1.26) | 3.94 (1.24) | 3.73 (1.14) | 2.73 (1.03) |
| A1, A2, A4 local with block order and move paths, library | 1.68 | 1.64 | 1.54 | 1.38 |
| A3 and A5 (Origin judgment, Loans, their block order and move paths), library | 6.47 | 5.81 | 5.64 | 4.66 |
| All source-body steps (publish, build, A1-A5) | 0.10 | 0.14 | 0.51 | 0.61 |
| Program steps (program validation, A4 solve, A5 findings, scratch) | 0.10 | 0.10 | 0.09 | 0.09 |
| Collection | 0.30 | 0.30 | 0.29 | 0.51 |
| Stage 3 total | 15.3 | 14.2 | 14.0 | 11.9 |
| Code generation and IR write (stage 6, not split) | 0.04 | 0.04 | 0.13 | 0.51 |

Library work is 90-97 % of stage 3 time. The largest single step is A5's Loan stratum over library bodies (4.6-5.5 ms; one body,
`tryGetPairUniq`, has 210 statements); A6 runs inside the steps' reports and is not separable.

## 3. Cause

Each fresh compilation creates its own `KimiLibrary` (Binding) and `TyCtxt`, so the library's syntax, symbols and Types are
compilation-local. Since G2 (R5-M156) `Compilation.Analyze` runs `BuildMir`: `HirTables.Publish` walks every module, the library's
included; `MirBuilder.Build` builds and validates one body per published definition of **every** module (`Module(compilation.Library...)`);
`MirAnalysis.Run` runs A3, A1, A2, A4 and A5 on every built body. None of this depends on the user's program, and for the three small
inputs no library body reaches any consumer: the checked set holds no library body item, nothing library is generated, and the MIR
analyses' problems are unpublished (R4-M1 until H3). The old pipeline analyzed only source bodies (A's stage 4: 0.07 ms) and emitted
only what the program reached. Consumers of a library body's facts: A4 reads callee bodies' summaries (`MirEffects.Local`, `Solve`:
Fn callees, closures, requirement implementations, destructors, static initializers through `MirProgram.Callee`/`this[key]`/`StaticInit`,
an absent body read as bodyless); code generation reads A2's table (`program.Drops`) of generated bodies; nothing reads a library body's
A3 or A5 result.

## 4. Options

**O1 (inside `MirAnalysis`, small): A3 and A5 judge the root modules' bodies.** Both are body-local: their results are the body's own
problems, which no other step reads (A5 reads A4's keys, A4 does not read A5). Steps 1 (A3) and 4 (A5) run over the bodies of the root
modules, a module range as the collector's `rootModules` (not a mode); A1, A2 and A4 keep every body. Deletes the library's A3/A5 pass;
adds the range. Saving: 39-42 % of stage 3 (4.7-6.5 ms indicative; about 5 ms of the pinned 12.7), 0.64-0.77 MB per fresh compilation,
the same per warm rebuild. Risks: a library defect A3/A5 would find is no longer seen by every check, so a library self-check test runs
the analyses with the library as the root range on each target profile (the old analysis judged only source bodies: A's stage 4 is
0.07 ms); cold allocation baselines fall (updated in the unit); warm rebuild stays 0 B. `Mir/Analysis/**` is R4's
hold file.

**O2 (inside `MirBuilder` and `MirProgram`, with O1): build the library bodies the program reaches.** The builder builds every root-module
body; a library body is built when a consumer first looks it up, `MirProgram`'s key lookups (`this[key]`, `Callee`, `StaticInit`) being
the one demand point (a library definition not yet built must be told apart from a definition without a body, never an absent-means-
bodyless fallback). The analysis driver runs steps 1-2 and A4 Local on bodies appended while it runs; A4's solve, which first names
destructors, closures and implementations under concrete Types, takes a body appended during the fixed point into its worklist; bodies
the collector appends get A1/A2 before generation. Saving with O1: the library build and analysis, 78-81 % of stage 3 on the small inputs
(about 10 ms of the pinned 12.7; 3.7 MB), about 90 % of the library's work on Generic 1 (16 of 215 bodies reached by the collector, 9 by direct
calls); the library HIR publish (2 ms, 0.11 MB) stays until R7 replaces the publisher. Expected B total on Scalar 1: about 20 ms against
A's 17 ms. Risks: A4 correctness if a needed body is missed (the demand point, not a precomputed reach, prevents a second copy of the
collector's or A4's edge rules); warm 0 B (`MirBuildTest`) with a program-dependent body set; `MirBuildTest.Dump` writes the library
module; a library self-check test builds, validates and analyzes all library bodies (the library as a root module); hold files
`MirBuilder*.cs`, `Mir/Analysis/**`, `Core/Compilation.Pipeline.cs`.

**O3 (shared structure, large): one frozen library per process.** Binding binds and publishes the library once per process (per target
profile and language version); the MIR of its bodies is built and analyzed once; each compilation's `TyCtxt`, symbols and MIR layer over
the frozen library. Saves stage 3's library share and Binding's (stage 2, 17 ms, library-dominated per the 2026-10-03 Binding note),
potentially 25 of 30 ms. Owners: `KimiLibrary`, `Binding`, `TyCtxt`, `MirBuilder`. Deletes the per-compilation library construction and
its per-pass reset. Risks: identity of library symbols and Types across compilations, thread safety under the LSP, retained memory,
every allocation baseline. Not a G337 unit: a PLAN candidate after R7.

## 5. Sizing (units of at most one hour)

| Unit | Content | Holds |
| --- | --- | --- |
| U1 (O1) | Steps 1 and 4 over the root-module range; the library self-check test (A3/A5 clean per target); cold allocation baselines | `Mir/Analysis/MirAnalysis.cs` (R4) |
| U2a (O2, bp) | `MirProgram` lookups as the demand point and `MirBuilder` building one body by key, still building every body eagerly | `MirBuilder*.cs` |
| U2b (O2) | Build only root-module bodies eagerly; the driver analyzes appended bodies (steps 1-2, A4 Local); library self-check builds all | `MirBuilder*.cs`, `Mir/Analysis/**`, `Compilation.Pipeline.cs` |
| U2c (O2) | A4's solve takes bodies first named during the fixed point (Local, then their nodes); likeliest to grow, split on the limit | `Mir/Analysis/MirEffects*.cs` |
| U2d (O2) | Collector-appended bodies get A1/A2 before generation; `MirBuildTest.Dump` and library-MIR tests; warm 0 B | Backend, tests |
| M | Pinned quiet-slot retake of `--compiler-pipeline` and `--analyze-cost` (heavy run, not counted), fixed summarizer | - |

## 6. Open questions

1. May the library's own A3/A5 verdicts (O1), and with O2 every library body outside the program's reach, be verified once by a library
   self-check test per target rather than by every check? (SPEC 23.2.2 reports every error of the checked set; an unreached library body
   is not in it.)
2. Schedule: O1 and O2 edit R4's hold files; under R4's hold protocol now, or at R6-check after R4's exit?
3. PLAN G337 and DECISIONS R5-M167 state "+72 to +120 ms"; correct them to "+9 to +15 ms per compilation" with the summarizer fix?
4. O3 as a PLAN candidate after R7?
