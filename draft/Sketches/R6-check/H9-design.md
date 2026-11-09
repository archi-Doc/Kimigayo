# R6-check H9: benchmark successors and the `BuildMir` split at stage 3/4 (design)

Status: revision 0, design only. Unit row (H0 §4, R5-M182): "H9 | Benchmark successors (the old-analysis benchmarks call the entry
until here); `BuildMir` split at stage 3/4 | R5 | the sweep; after H8c (D-c3) | group G:check-del | ≈ −350/+30"; H0 D-c5; H0c §3;
PLAN's R5 and R6 bullet (group review N1, N2).

Base: origin/dev `e44d30984`; `origin/claude/r5-h10a` `3ed4d0582` read where G:check-tests touches an H9 file (only
`C/Core/Compilation.Pipeline.cs`: H10a removes `Analyzed`, lines 9, 31-35, 85-98; H9 edits `BuildMir`, lines 140-148, so no overlap).
Paths: `C/` = `src/Kimi/Compiler/`, `B/` = `src/Benchmark/Benchmarks/`, `BM/` = `src/Benchmark/`. No build, test, probe or run;
(H) marks a fact read from code and confirmed only by the first run. Working time: `design-state.md` (08:28-08:47, 2026-11-05).

## 1. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **Split:** `Compilation.ConstructMir()` (new, internal) is `BuildMir`'s first step: `(this.mirBuilder ??= new(this)).Build()`, HIR version check, construction, validation, memoized per version. `BuildMir()` keeps its contract and composes it: `Run(ConstructMir(), …)` then `Collect`. `Analyze` is unchanged | D-c5. Stage 3/4 needs an addressable boundary on the compilation's own builder (stage 4's `Analyze` must reuse it); `BuildMir`'s ~400 test callers and every in-flight branch keep reading the analyzed program | `C/Core/Compilation.Pipeline.cs`; ARCH row "MIR construction" |
| D2 | **Stage map:** stage 3 `MirPublishBuildValidate` = `ConstructMir`; stage 4 `ControlFlowAndOwnership` = `Analyze`: the MIR analyses (A1-A6), the instance collector over stage 3's memoized MIR, the verdict over its rows; it keeps them for generation. Names unchanged (PD26; the O2 corpus harness compares names) | D-c5; R5-M94's map superseded in part | `B/CompilerPipelineBenchmark.cs:81-84, 103`; `BM/CompilerPipeline.md`; `BM/O2Corpus.md:51`; DECISIONS R5-M94 (back-reference line) |
| D3 | **A warm successor rebinds.** The old benchmarks re-ran `OwnershipAnalysis.Analyze()` on one version. The MIR has no such run: `ConstructMir` and `MirAnalysis.Run` return a version's memo, and no benchmark-only seam is allowed (D-c5). Each warm loop's successor is check's warm operation, `Bind`, `CheckStartup`, `Analyze` (`--analyze-cost`'s `warmAnalyze`; H8b D5's test form), named `check` | H0c's "→ `Compilation.Analyze()`" rows would time a cached return (H5 E `178802711` records this departure). One definition of the warm check | §2 rows |
| D4 | **Old counts go** unless the MIR has the same quantity. Old-table sizes (`BorrowStorageBytes`, `LocalRegion*`, `PeakLocalLoanCells`, `DefaultContexts`, `BorrowDependencyCapacity`) and old IR sizes (`Places`, `Operations`) have no MIR counterpart. The MIR's arenas are private scratch, and publishing them for a benchmark would be a seam. Only PairCase's case runs have a MIR equivalent: Σ `MirCases.Count(body)`, as `views` | group review N2; AGENTS Short | §2 rows |
| D5 | **Deleted** where nothing measured exists: the borrow-dependency table (`--borrow-storage`) and the two BenchmarkDotNet classes whose `Analyze` re-ran the old analysis. Their `BindAndAnalyze` is `--analyze-cost`'s `warmAnalyze` on other inputs | H0c §3 | §2 rows |
| D6 | **Series docs** keep their history; each gets one line naming the break ("from H9 the `check` phase times Bind, CheckStartup and Analyze; earlier rows timed the old analysis" or "column removed"). BorrowStorage.md goes with its benchmark. Its evidence stays in `artifacts/benchmarks/borrow-storage-review/` and git | H0c §3; AGENTS update table (README/usage Auto) | §5 documents |
| D7 | **No ratchet row in H9.** H10b adds the zero-reference rows for `src/Benchmark` and `src/Playground` (D-c4; H8b D9). H9's evidence is the grep in §6 | D-c4 stands | — |

## 2. Readers on dev (F13 recount) and their fate

F13: plan "12 files" → H0c "10 + `Program.cs`" → now **9 files + `Program.cs`**. `HoverMeasurements` was re-pointed by GR-1a N1
(`2ee073ed2`; `:78` gates on `Analyze()`), so group review N1 is closed and H9 has nothing for it. 30 reads: 28
`AnalyzeLegacyOwnership()` calls in 8 files, 2 control-flow reads in 1 file; `Program.cs` 3 sites; one doc. `src/Playground`: 0. Two
pipeline-switch readers (`B/CollectionCostMeasurements.cs:183`, `B/DiagnosticMeasurements.cs:207`) stay for H12.

| File (lines) | Member / reads | Measures today | Fate and exact successor |
| --- | --- | --- | --- |
| `B/BorrowStorageMeasurements.cs` (66) + `BM/BorrowStorage.md` (73) | `Run`: entry ×3, `.Bodies`, `.Analyze()` | Old analysis's first allocation, warm re-run, its dependency table | **Delete both**; `Program.cs:120-124` (`--borrow-storage`). Nothing measured exists. Its input `InspectionLoans(32, stored)` stays timed as PairCase's `borrow-dependency-reference` row |
| `B/OwnershipAnalysisBenchmark.cs` (67) | `Analyze`, `BindAndAnalyze`, `Setup` (entry ×3) | BDN: old analysis warm re-run; Bind + old analysis | **Delete**; `Program.cs:207` (D5) |
| `B/MatchOwnershipBenchmark.cs` (55) | same shape (entry ×3) | same, match workload | **Delete**; `Program.cs:208` (D5) |
| `B/PatternBindingBenchmark.cs` | `flow` field, `Setup` `AnalyzeControlFlow()` (:14, :42), `BindAndAnalyze` `Reanalyze` (:60) | Rebind + old control-flow check | Keep `Bind`. `BindAndAnalyze` → `this.compilation.Bind(); return this.compilation.Analyze();` (returns `AnalysisResult`). `Setup` requires `Analyze().IsVerified` (H: PatternBindingTest's rows verify). The `flow` field goes |
| `B/CompilerPlanMeasurements.cs` | phase `ownership` = entry `.Analyze()` (:119); five old counts (:106-110); rebind precondition (:75-79); comment :72-73 | Per-workload old analysis, warm; old table sizes | Phase **`ownership` → `check`**: `c.Bind().IsComplete && c.Binding.CheckStartup(OutputKind.Application).IsComplete && c.Analyze().IsVerified` (D3). The precondition goes (the phase validates itself, and emission follows its last `Analyze`). The five counts go (D4, N2). Results: `{ name, size, stored, phase, sourceSha256, millisecondsPerIteration, bytesPerSample }`. Serves `--object-plans`, `--callable-plans`, `--view-plans`, `--local-regions`, `--fixed-origin-loans`, `--generic-defaults`, `--property-plans`, `--adaptation-plans`, `--element-places`, `--inheritance-plans`, `--arithmetic-plans` |
| `B/InheritanceLifecycleMeasurements.cs` | `Storage` (:139-146), `storageBefore/After` (:55, :72), the growth guard (:73-76), two JSON fields (:91-92) | Old tables before and after the warm run | **Columns and guard go** (D4, N2). `Pipeline` already runs check and generation. Warm reuse stays shown by `warmBytesPerSample` and asserted by the MIR's `Purpose=Allocation` tests |
| `B/PairCaseMeasurements.cs` | `Run`: entry ×4, `.Bodies` counts | Old case runs: first bytes, warm re-run, warm rebind | `firstAnalysisBytes` → **`firstAnalyzeBytes`** (first `c.Analyze()` after Bind and CheckStartup). Warm `Analyze()` → **`check`** (D3): `warmCheckBytes`, `warmCheckMilliseconds`. `warmBindMilliseconds` stays. `bodies`, `places`, `operations` → **`mirBodies`** and **`views`** (Σ `MirCases.Count(body)` over `c.BuildMir().Bodies` with `body.Source.Module < c.SourceModules.Length`). The verdict: OQ-2 |
| `B/OriginProofMeasurements.cs` | `RunCase`: entry `.Result` (:91), `.Issues` (:93), `Check` (:165) | Cold old analysis; codes; warm check | `ownershipMilliseconds` → **`analyzeMilliseconds`** (cold `compilation.Analyze()`); `verified` ← `Analyze().IsVerified`; codes = Binding issues + **`compilation.Problems`** codes (the published rows). `Check()` → Bind, CheckStartup, `Analyze()`. `ownershipRequests/Closures` go: the MIR asks Binding for no Origin proof (no `OriginProofMetrics` site under `Mir/` or the collector), so they would be 0 |
| `B/VerificationMeasurements.cs` | local `Analyze` (:53) | Old analysis warm re-run (live part Loans) | → **`check`** (D3); the JSON is unchanged (`elapsedSeconds`). `LivePartLoans` verifies under the MIR (`BorrowSnapshotTest`) |
| `BM/Program.cs` | `--borrow-storage` branch, `typeof` ×2 | — | Removed with D5 |
| `B/CollectionCostMeasurements.cs` | builds with `new MirBuilder(c).Build()` + `new MirAnalysis(c.Types).Run` | Collection share (R5-M148 gate 10) | **Unchanged.** It must not use `ConstructMir`: its later `Compilation.Analyze()` would rerun the compilation's own `MirAnalysis` over the same program and append its problems twice (`Run` does not clear them; `MirProgram.Clear` is the builder's) |
| `B/StaticKeyMeasurements.cs:286`, `BM/StaticKeys.md:14` | `BuildMir()` | MIR built outside intervals | Unchanged (D1 keeps `BuildMir`) |

## 3. The split (D1, D2)

- **What moves:** nothing between components. `MirBuilder` keeps construction and validation, `MirAnalysis` the analyses, and
  `InstanceCollector` collection (ARCH rows). Core's `Compilation.Pipeline.cs` gains a 3-line member and a doc comment, and the header's
  Output names it. `BuildMir`'s first line becomes `Run(this.ConstructMir(), …)`. Order: `ConstructMir` after `BuildMir` (SA1202/SA1204:
  both internal).
- **Timed stages after it:** stage 3 = construction and validation of every body, the source's and the library's, plus the builder's
  lazy creation. Stage 4 = `Analyze`: two partition invalidations, `BuildMir`'s memo hit on stage 3's program, `MirAnalysis.Run`,
  `Collect` (with the lazy creation of both), and `Analysis` over `Problems`. Stages 1, 2, 5 and 6 are unchanged.
  `mirMeanMicrosecondsPerBody` and `mirMeanAllocatedBytesPerBody` stay on stage 3, so they now mean construction and validation per
  body. CompilerPipeline.md names this break once.
- **Why now:** since H5, `Analyze` is `BuildMir`, and stage 3 already runs the analyses and collection. Stage 4 times only memo checks,
  and its row still says "the old analysis" (`CompilerPipeline.md:17, 24`, stale since H5). After H10b, stage 4 would have no other
  content. D-c3 puts H9 before H10b, and one change of stage meanings is better than two. The sum of stages 3 and 4 is unchanged by the
  split, so H15's PLAN10-P3 record ("BuildMir + collection" = stages 3 + 4 against the R6-gen exit series) stays comparable, while each
  stage again measures one thing.
- **Rejected:** (b) moving analysis and collection out of `BuildMir` into `Analyze` (about 400 test call sites, and H8a-c's tests,
  would read an unanalyzed program); (c) renaming so that `BuildMir` means construction (a silent change of meaning for in-flight
  branches of R4, BU, G216 (a) and G:check-tests); (d) a benchmark-local builder for stage 3 (stage 4's `Analyze` would rebuild,
  and stage 3 would not be the work check runs).

## 4. Measurement

| What | Before/after | Conditions | Pass |
| --- | --- | --- | --- |
| Split (C1, bp) | diagnostic snapshot, semantic snapshot (HIR, MIR), generated IR of one selected family, against the parent's run (VERIFICATION, behavior-preserving commits) | Unit run, same inputs | No difference |
| Stage partition (C2) | `--compiler-pipeline`, A = H9's base, B = C2; interleaved A, B, A, B, A; pinned to one CPU with `DOTNET_TieredCompilation=0`; A and B run from one folder (or same-length sibling folders); compiler ids checked in every report | Quiet slot | Per input: B's stage 3 + stage 4 allocation equals A's within the per-sample boundary noise (E14 precedent: a few hundred bytes); B's stage 4 holds the moved share (Generic 32-128: collection's 2.2-7.7 MB plus the analyses'); total B/A within the A runs' spread. A stage 3 + 4 sum outside the spread is a finding, kept with its evidence |
| Stage checks | `--compiler-pipeline-check`, `--collection-cost-check` (gate 10's instrument, unchanged) | Session's build | PASS |
| Re-pointed benchmarks (C2) | each changed entry once: the 11 `CompilerPlanMeasurements` commands, `--inheritance-lifecycle`, `--pair-cases`, `--verification`, `--origin-proof-case <family> 1` for each family (full `--origin-proof` is optional: one process per case, 30 s limits), BDN `--filter *PatternBindingBenchmark* --job Dry` | after the Session, Release build | Runs to completion with the new columns. These are observations with no before/after: the old work no longer exists. Outputs under `artifacts/benchmarks/h9/` |

- **Allocation regressions: none to update.** No compiler allocation changes (the new member allocates nothing), and no
  `Purpose=Allocation` test reads a removed count or benchmark. The old-storage tests (`BorrowDependencyStorageTest`, ...) belong to
  G:check-tests and H10b.
- **Timing slot on the board: one, about 20 min**, for the stage-partition A/B after the Session. The executions need no slot.

## 5. Verification and delivery

- **Class: Major.** Core plus Benchmark, with no shared representation. **No separate design review.** The choices are within one owner
  each (Core's entry, each benchmark). The design departs from no DECISIONS or SETTLED entry, and H0c's infeasible lines are corrected
  under D3 with H5 E's recorded reason. G:check-del's one group review covers H9 (H0 §4). It stays on the group branch until that
  review passes (WORKFLOW). Base: dev after G:check-tests merges, or `claude/r5-h10a`'s head if R5 starts earlier.
- **Commits (each phase within Q12's hour):**
  1. *C1 (bp):* `ConstructMir`; ARCH "MIR construction" row (`MirBuilder` (`Compilation.ConstructMir`, which `BuildMir` composes)).
     Checks: the `-warnaserror` build and slnx build (unit verify skips Benchmark). Unit run of PipelineSwitchTest, MirAnalysisTest,
     MirBuildTest, InstanceCollectorTest, SemanticSnapshotTest (with `KIMI_SEMANTIC_HIR=1`, `KIMI_SEMANTIC_MIR=1`) and
     `-DiagnosticSnapshot -DiagnosticBaseline <parent run>`, one native family (for example `BorrowSnapshot*.ll`), and
     compare-fixtures against the parent. Net about +5/−1.
  2. *C2 (Benchmark):* the stage move (D2), every §2 row, DECISIONS R5-M94's back-reference line ("Superseded in part by R5-M182's
     D-c5 (H9): stage 3 is `Compilation.ConstructMir`, stage 4 `Analyze`'s analyses and collection") and the documents below.
     Checks: Session (whole-solution build), then §4's executions and the slot's A/B. Net about −330/+60 (code about −245/+35).
- **Documents (Auto):** CompilerPipeline.md (rows 3-4; :21-33, with the stale "old analysis still decides" and the lazy-emitter
  sentence; one break line). O2Corpus.md:51
  (collection is in stage 4, `Analyze`). One line each in CallablePlans.md:15-17, ElementPlaces.md:16, GenericDefaults.md:17,
  Inheritance.md:9/50/55-56 (the 2026-10-07 record at :60-62 stays), ObjectPlans.md:13, Properties.md:11, Views.md:10, Adaptations.md:15, Arithmetic.md:27, PairCases.md:3-11
  (method, the `--borrow-storage` mention, break line) and OriginProof.md:17. VERIFICATION:220 (row deleted) and :230 ("retained
  ownership storage" goes). CODEMAP:140 (the BorrowStorage.md link goes; H10b removes the row). ARCH: the C1 row only. STATUS: none.
  PLAN: R5's bullet at landing, and G147 (8)'s row if OQ-2 (i).
- **DECISIONS:** only the R5-M94 back-reference. Nothing new: no new representation, component, vocabulary entry or temporary
  structure.

## 6. What H10b then finds (D-c4 first step)

After C2, `git grep -nE "AnalyzeLegacyOwnership|AnalyzeControlFlow|\bControlFlowAnalysis\b|\bOwnership(Analysis|Body|Model|Flow|Storage|Result|Issue)\b|BorrowLiveness|PackedAnalysisTable|InterpretationContext|old analysis" -- src/Benchmark src/Playground`
returns **0**. This grep is in C2's commit evidence. H10b's zero-reference rows (H8b D9's pattern, plus the old Analysis names) hold
in both directories from H9. The remaining H10b readers are tests (G:check-tests) and `src/Kimi`, which waits for H11.

## 7. Open questions for R5

| # | Question | Recommendation |
| --- | --- | --- |
| OQ-1 | The name of the step (D1) | `ConstructMir` (the ARCH "MIR construction" row); non-blocking |
| OQ-2 | PairCase's `pair-case-families` is rejected by the MIR until PLAN G147 (8) (H5's (e) list; `GenericCaseAllocationTest:23` asserts `False`). (i) Each workload names its expected verdict, `false` until G147 (8), failing closed both ways, and G147 (8)'s row names this flip. (ii) Report `verified` and gate only on Binding. (iii) Drop the workload until G147 (8) | (i): one verdict for the shared workload, as the test asserts it. **Blocking for C2** |
| OQ-3 | Keep the two deleted BDN classes' drop- and match-heavy inputs as `--analyze-cost` shapes? | No (H0c); a later timing unit may add them; non-blocking |
| OQ-4 | Correct H0c §3's "→ `Compilation.Analyze()`" lines in place (revision mark) or leave the correction to H9's commit | Leave H0c, a working record; H9's commit names D3; non-blocking |
| OQ-5 | Land the Benchmark/Playground zero-reference rows at H9 instead of H10b | No (D-c4, D7); non-blocking |

## 8. R5's rulings (2026-11-05)

- **Class Major without a separate design review** (WORKFLOW Design 4: H0's D-c5 settled the split and its owner, Core; no option
  differs in owning component; no departure). One implementation review by an independent reviewer, then G:check-del's group review.
- **OQ-1:** `ConstructMir`. **OQ-2:** (i): each workload names its expected verdict (`false` for `pair-case-families` until PLAN G147 (8),
  failing closed both ways), and G147 (8)'s row names this flip at R5's landing PLAN commit. **OQ-3:** no. **OQ-4:** leave H0c; H9's commit
  names D3. **OQ-5:** no (D-c4, D7).
- **Base:** G:check-del's branch `claude/r5-check-del` starts from G:check-tests' head (`claude/r5-h10a`), because both groups touch
  `Compilation.Pipeline*.cs` and G:check-del lands after G:check-tests (D-c3); it merges G:check-tests' later commits, never rebases.
- **Timing:** the pinned interleaved A/B runs in a booked slot (VERIFICATION's shared heavy runs; a `*timing*.run` file and a board line),
  from one folder, `DOTNET_TieredCompilation=0` when pinning; failed evidence is kept.
