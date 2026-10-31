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

## 7. §O1: A3 and A5 judge the root modules' bodies (design under R4's conditions)

The coordinator's go (2026-10-31): O1 only, under R4's hold; R4 owns `Mir/Analysis`, agreed to O1 and reviews the A3/A5 side; O2 waits
for O1's measurement. The implementation unit asks R4 first; this section edits no source.

### 7.1 The predicate and its owner

A skipped body is one whose MIR is the same in every program. In this MIR there is no other kind of library body:

- `MirBuilder` builds exactly one body per definition whose value node the HIR publishes (its contract header), keyed by `MirDefKey`
  and built from that definition's own syntax; an instance is the collector's replay of that body under `MonoItem.Representative`, never
  a body of its own, so no library body is built for a program instance or specialized over a program Type.
- A Closure body is keyed by its own symbol and built from its definition in its module, so a library Closure captures library locals.
- The library module is bound in the library's scope, which names no program declaration (SPEC 18: a module names only the modules it
  depends on; `Kimi` depends on none of the program's).

So the predicate is a module range read from published facts, not from syntax: steps 1 and 4 judge a body when
`body.Source.Module < rootModules`. `MirBuilder` publishes `MirBody.Source` (the definition's `SourceRef`, its module ordinal);
`Compilation` publishes the module order (the source modules are ordinals `[0, SourceModules.Length)`, the library the next, which
`MirModelTest` asserts) and passes `rootModules = SourceModules.Length`, exactly the range it already passes to
`InstanceCollector.Run(program, rootModules)` ("a range of Source.Module, not a mode"). The invariant O1 rests on, stated in the DECISIONS
entry: a body outside the root range is built from its definition alone; a builder change that builds a library-owned body from program
facts (a per-instance or specialized body, R8's monomorphization included) must place it in the root range or end this rule.

Measured precondition (`C:\Users\bwff1\AppData\Local\Temp\claude\r5\g337\census-134.txt`, probe `LibraryCensus.cs` beside it): 134
programs (tests/milestones 44, tests/mir 32, docs/examples 39, src/Benchmark/Kimi 15 and the four inputs above; 8 with incomplete
Binding), each compiled fresh through `Compilation.Analyze`, every step on every body:

| Fact | Result |
| --- | --- |
| Library bodies per program | 215 in all 134 |
| Program declarations a library body names (its locals' Types and their components, its callee symbols) | 0 |
| Problems at library bodies, any step (A1-A5, A4's rows and the C1-F4 findings at library sites) | 0 in all 134 |
| Construction records and validator findings at library sites | 0 |
| Library MIR dump (`MirDump.Write`, the library module) | 132 identical; Milestone27 and Milestone30 differ only in two location ids |

The two differing dumps are identical with locations normalized: synthesized library nodes (the `for` expansion in `contains`) get their
`SourceRef` id on first use (`SourceRef.Of`: "the next id of its module"), in an order the program can change. A3 and A5 read no id but
as a problem's subject, so their verdicts are program-independent; the ids matter to O2's or O3's reuse, which must assign them when the
library is built. The ledger's own program set was not enumerated (it is the test suite's); the full ledger run of O1-L covers it.

### 7.2 The driver rule (MirAnalysis.cs; contract header and DECISIONS)

`MirAnalysis.Run(MirProgram program, int rootModules)`: step 1 (A3) and step 4 (A5: the Origin judgment, the Loans and their block order
and move paths) visit the bodies with `Source.Module < rootModules`; step 2 (block order, move paths, A1, A2, A4's local facts) and step 3
(A4 over the program) keep visiting every body, and `origins.Findings` stays once per program. A4 stays whole because step 3 composes
callee summaries from library bodies' local facts (`MirEffects.Local` and `Solve`: Fn callees, Closures, implementations, destructors,
static initializers), and code generation reads A2's table of generated library bodies: O1 never leaves step 3 reading a fact step 2 did
not produce. Nothing reads A3's or A5's results but the body's own problems (A5 reads A4's keys; A4 reads no A5 fact; A3 and the
dataflow each clear the one `MirReach` before walking). The C1-F4 view's findings that only library judgments raise move to the library
check (7.3).

Contract header addition (under "Runs them once per version"): "Input rule: steps 1 and 4 judge the bodies of the root modules
(`Source.Module < rootModules`, the collector's range), whose problems are the program's; steps 2 and 3 visit every body, since A4
composes library callees' local facts and code generation reads A2's tables; MirAnalysisTest's library check judges the library."

DECISIONS: one R5 entry (number at landing) naming R4's agreement and its review of the A3/A5 side and the owner's G337 acceptance: the
rule, 7.1's invariant, the measured precondition, the moved check, and the rejected alternatives (a syntax test in `MirAnalysis`; a
per-body library flag, a second copy of the module order; skipping A1, A2 or A4 too, which A4 and code generation read).

### 7.3 The library check (one test per target)

`MirAnalysisTest.TheLibraryHasNoAnalysisProblem(target)`, a Theory over the supported targets (today `x86_64-pc-windows-msvc`): a
compilation of an empty program, `BuildMir`, then a fresh `MirAnalysis` run with `rootModules = SourceModules.Length + 1`, so steps 1 and 4
judge every library body at full strength through exactly the driver check uses. It fails on any construction record, validator finding
or problem at a library site, A1's arena limit stopping a body's views and the C1-F4 findings included, each named by code, body and
subject. A library defect becomes a compiler-test failure, not a user record (acceptable at H3 and after: the library ships with the
compiler). That check uses the range is shown by the deterministic cold-allocation drop of 7.4 (no test-only counter is added).

### 7.4 Checks and expected saving (per fresh compilation)

- MirAnalysisTest, MirInitTest, MirBuildTest, PipelineSwitchTest, KimiLibraryShapeTest and the `Purpose=Allocation` tests whose cold MIR
  bytes fall (baselines updated in the unit, the drop stated); the full mir ledger (`-Pipeline mir`): no change in program records, the
  library rows that disappear classified in the commit (the census expects none); snapshot and semantic snapshot 0; a Session.
- `src/Benchmark` builds (`CollectionCostMeasurements` calls `MirAnalysis.Run`).
- Saving per compilation (not per 8-compilation sample): 39-42 % of stage 3, about 5 ms of the pinned 12.7 ms on Scalar 1 (B's total
  30.1 ms to about 25 ms against A's 17.1 ms) and 0.64-0.77 MB of 3.9-4.1 MB cold; the same time per warm rebuild, warm 0 B unchanged.
  A pinned quiet-slot retake confirms it (O1-M).

### 7.5 Implementation sizing (units of at most one hour)

| Unit | Content | Files (holds) |
| --- | --- | --- |
| O1-U1 | `Run(program, rootModules)`, steps 1 and 4 over the range; callers pass `SourceModules.Length`; contract header; DECISIONS entry; the library check; allocation baselines; unit checks | `Mir/Analysis/MirAnalysis.cs` (R4), `Core/Compilation.Pipeline.cs` (hold), `tests/xUnitTest/Tests/MirAnalysisTest.cs`, `MirInitTest.cs` (two callers), `src/Benchmark/Benchmarks/CollectionCostMeasurements.cs`, `docs/dev/DECISIONS.md` |
| O1-L | The full mir ledger and the Session (heavy runs, not counted); R4's review of the A3/A5 side | - |
| O1-M | Pinned `--compiler-pipeline` and `--analyze-cost` retake on the quiet-slot GO, with the fixed summarizer | - |
