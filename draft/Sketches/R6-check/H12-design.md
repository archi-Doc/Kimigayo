# R6-check H12: removing the pipeline switch, the `[mir]` mark and `-Pipeline` (design)

Status: revision 0, written by a design agent for R5. Unit row (H0 §4, R5-M182): "H12 | Remove the pipeline switch, `[mir]`,
`-Pipeline` | R5 | H10a, H10b | group G:check-del | ≈ −600". Inputs: H0-design §3 (D-c3), §4, §5, §6 (F13); H0c §4.2, §5.1, §5.3
item 4; H10a-design D2, D9, §4; H10b-design §4 ("H12 is unchanged"); DECISIONS R5-M152 S1, R5-M156, R5-M160 (G4's removal of
`KIMI_CODEGEN`, the precedent this design follows) and `draft/Sketches/R6-gen/G4-design.md` (c1 tests, c2 tooling, c3 selector).

Base: `origin/claude/r5-h10a` `9253ebd42` merged with `origin/claude/r5-check-del` `4064ae3e5`, H10b's design as the next unit. As
H10b states: `src/Kimi` and tests are h10a's, `src/Benchmark` is check-del's, docs are h10a's plus H9's three VERIFICATION lines.
Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`, `B/` = `src/Benchmark/Benchmarks/`.
Line numbers are on that composition; the implementer recounts on G:check-del's head after H10b. No build, test or probe was run.

Working time (Q12), logged in `design-state.md` beside this file: 11:32–11:52 (2026-11-05), 20 minutes of the hour.

## 0. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **Delete `C/Core/Compilation.Switch.cs` whole** (68 lines): `DefaultPipeline`, `pipeline`, `Pipeline`, `PipelineOf`, `CompilerPipeline` (the 5 `[Transitional("R6")]` declarations) and `CompilerIdentity`. MIR is the only pipeline; nothing remains to select | The file's own contract: "Removal (DECISIONS R5-M152): H12". Since H5 every value checks, publishes and generates alike (R5-M185); since H10a the switch selects only the mark and the manifest entry (H10a D2), both of which go here | `C/Core/Compilation.Switch.cs` |
| D2 | **No guard for a stale `KIMI_PIPELINE`.** Nothing reads it; a set value selects nothing and is ignored like any unknown variable | R5-M160's rule for `KIMI_CODEGEN`: a guard would keep the name alive as a second reading of a removed name. No silent fallback exists, since there is no second pipeline to fall back from | — |
| D3 | **`CompilerIdentity` goes; its readers read `Compilation.CompilerVersion`.** The check document's `compiler` is `CompilerVersion` (the schema's "compiler build identity"; `CompilerVersion`'s own doc: "the compiler version and module build identity") | One concept, one name. Without the mark the two members are equal; keeping both would be a wrapper without semantics | `src/Kimi/Checking/CheckDocument.cs:50`, `T/CheckJsonOutputTest.cs:91`, `B/AnalyzeCostMeasurements.cs:35`, `B/CompilerPipelineMeasurements.cs:71, 97`, `B/SyntaxEditMeasurements.cs:68` |
| D4 | **The identity format after the mark: `CompilerVersion`, unchanged for the default.** Nothing stored changes for default runs: check documents, the test manifest and its `ArtifactId` (the `pipeline mir` rows were added only under mir), benchmark `compiler` fields, Kotonoha's source cache (keyed by `CompilerVersion`), the diagnostic snapshot (records no identity). Only artifacts made under `KIMI_PIPELINE=mir` (` [mir]` documents, test artifact ids with the pipeline rows) become unreproducible; nothing reuses them except `kimi test`'s artifact cache, which misses and rebuilds. `--analyze-cost` keeps schema 3 (the field stays) | Default output is byte-identical, so no baseline or cache key moves | — |
| D5 | **Test manifest:** `TestCatalog`'s pipeline rows (`:122-125`) go | Their selection is gone (D1); the default manifest never had them | `src/Kimi/Testing/TestCatalog.cs` |
| D6 | **`PipelineSwitchTest` → `CompilationEntryTest`** (`git mv`, class renamed, summary rewritten to the entry points: `Analyze`, `Problems`, `ReportAnalysis`, the generation gate). Deleted with the switch: `TheEnvironmentSelectsTheDefault`, `ThreadsMakingTheFirstCompilationsAgreeOnTheDefaults` + `RaceTheFirstCompilationsOfAFreshProcess` (PLAN G199's once-per-process read ends), `TheNonDefaultPipelineMarksTheIdentity`, `AnAcceptedProgramIsTheSameOnEitherPipeline` (the switch's revocation), the `Pipelines` field, the `Check` and `Manifest` helpers, `using System.Reflection`. Kept, each loop collapsed to its one compilation: `MirCodegenWritesEachOutputForm`, `TheGateRefusesMirRecordsForEveryCaller`, `TheTestCommandPublishesTheInstancesRecordAtCheck`, `EveryPipelinePublishesTheCollectorRecords` → `CheckPublishesTheCollectorRecords`, `MirCodegenRejectsAnOversizedLayoutAtCheck`, `AGateRefusalClearsAnEarlierEmissionRecord`, `EveryPipelinePublishesTheMirRecords` → `CheckPublishesTheMirRecords`, `MirRecordsAtOneLocationAreOneProblem`, `TheMirDecidesAcceptance` | The 9 kept tests pin R5-M64, R5-M156, R5-M52, R5-M165, R4-M71 and R5-M185 at the entry points, not the switch; deleting them (H0c's "449") would weaken coverage (AGENTS). The name must follow the subject (consistent naming); `CompilationEntryTest` is ARCH's term ("the `Compilation` entry points"). Before deleting a kept test as a duplicate, the implementer names the exact holder (none assumed) | `T/PipelineSwitchTest.cs` → `T/CompilationEntryTest.cs` |
| D7 | **Other test readers:** `MirBuildTest.Places.cs:227-243` loses its two-pipeline loop (one compilation); `RawStorageTest.RawOriginsGenerateOnTheDefaultPipeline` → `RawOriginsGenerate` (`:357`, `:386`, `:392`) | The selector type no longer exists (Places); "default pipeline" names a removed alternative (RawStorage) | `T/MirBuildTest.Places.cs`, `T/RawStorageTest.cs` |
| D8 | **`FreshProcess`' whitelist goes.** `SelectionVariables` and its `||` clause are deleted; a child keeps every variable outside the `KIMI_` namespace and none inside it; the summary drops "with the parent's pipeline selection". `FreshProcessTest`'s method → `TheChildKeepsNoVariableOfTheKimiNamespace`; `kimi_pipeline` moves from `kept` to the dropped rows (it still proves the case-insensitive prefix) | An empty whitelist is a mechanism without a member (Short). G4 removed `KIMI_CODEGEN` from it the same way | `X/FreshProcess.cs`, `T/FreshProcessTest.cs` |
| D9 | **Benchmark readers:** `CollectionCostMeasurements.Checked` loses `c.Pipeline = CompilerPipeline.Mir` (`:189`) and "on the mir pipeline" (`:185`); `DiagnosticMeasurements` loses the report field `pipeline` (`:207`; no reader in src/Benchmark or docs) | Copies of the selection (G4-design's "Benchmark copies") | `B/CollectionCostMeasurements.cs`, `B/DiagnosticMeasurements.cs` |
| D10 | **`verify.ps1 -Pipeline` goes:** the parameter (`:41`), its comment and example (`:25-27`), the set/restore of `KIMI_PIPELINE` (`:69-71`, `:328`) and `summary.json`'s `pipeline` field (`:329`). Readers: `compare-fixtures.ps1` drops `pipeline` from its run record, JSON report and text line (`:66`, `:104`, `:118`); `tests/scripts/FixtureComparisonTest.ps1:20` drops it from its synthetic summary. Old summaries that carry `pipeline` still compare (the field is ignored) | R5-M160 removed `-Codegen` the same way. A caller still passing `-Pipeline` fails at parameter binding, loudly, which is the intended notice (§3) (corrected at c6: a leftover `-Pipeline` binds PowerShell's common `-PipelineVariable` by prefix and has no effect, the one pipeline runs; callers drop it; a loud-failure guard is rejected, R5-M191) | `scripts/verify.ps1`, `scripts/compare-fixtures.ps1`, `tests/scripts/FixtureComparisonTest.ps1` |
| D11 | **Backend scripts:** `benchmark-o2-corpus.ps1` loses the inherited-selector refusal (`:20-22`) and its header's "A runs the default pipeline" (`:5-6`); `-CandidateEnvironment` stays as the general A/B variable mechanism (OQ4). `test-library.ps1` loses its `selectors` record (`:75-76`) and the "pipeline this process selects" comment (`:24-25`) | Their only selector ends (D1); keeping the guard would be a second reading of the removed name (D2) | `src/backend/windows-x64/benchmark-o2-corpus.ps1`, `test-library.ps1` |
| D12 | **Comments.** Lines naming a removed token (`KIMI_PIPELINE`, `CompilerPipeline`, `-Pipeline`, `[mir]`) or false after H12 ("both/either/each pipeline(s)", "the process's pipeline", "the mir pipeline", `MirEffectsTest.Destructions.cs:88`'s "the default and `-Pipeline mir` runs cover both") are rewritten in H12 (exit-gate item 4's grep). The vacuous qualifiers ("on every pipeline", "the default pipeline"; 40 lines) are dropped in the same comment commit where the file's owner consents (OQ2); without consent the line stays and c4's message names it | One name per concept; the token lines must go for H15's grep. The vacuous ones are true but describe a removed dimension | ≈ 55 lines in ≈ 40 files (§1) |
| D13 | **Order (item 4):** on G:check-del's branch right after H10b's C1–C3, before or beside H11; not after H10b-2 (`ControlFlowAnalysis.cs` reads no switch). c4 (comments) lands after H11 merges into the group branch, or rebases over it | H12 shares `T/PipelineSwitchTest.cs` with H10b C1 (which deletes `TheLegacyOwnershipEntryRunsOncePerVersion`) and the R5-M7 hold pair `Compilation.Pipeline.cs`/`Compilation.Switch.cs`, so it follows H10b C1 rather than running beside it. H11 (BU) edits Binding files, `X/MirTargetSide.cs` and `ArchitectureRulesTest`; H12's code commits touch none of them, so the two can run in parallel worktrees; only c4's comment lines may meet H11's test edits | — |
| D14 | **Class Major, no separate design review** (WORKFLOW Design 4). The cause is certain (a temporary structure at its removal stage), every site is a deletion of the same switch with no option differing in owning component or representation, and R5-M160 is the exact precedent. The one departure from R5-M182's row (D6 keeps and renames 9 entry-point tests, so ≈ −220 instead of ≈ −600) follows AGENTS ("never weaken") and is recorded in D16. One implementation review by a reviewer who wrote neither (R4 or BU), then G:check-del's group review | H9's precedent ("Major, no separate design review") | — |
| D15 | **Commits** (each with its own Unit evidence, `verify-commit.ps1`): **c1 tests** (D6, D7, D8; the compiler still has the switch); **c2 tooling** (D10, D11; VERIFICATION `:94`, O2Corpus.md `:34-36`); **c3 the selector** (D1, D3, D5, D9; ARCH, CODEMAP, DIAGNOSTICS, CompilerPipeline.md; DECISIONS D16); **c4 comments** (D12). Phases (Q12): **H12-i** c1 + c2; **H12-ii** c3 + c4 and R5's landing PLAN commit | G4's c1–c3 shape; c3 carries DECISIONS because AGENTS records a decision in the commit that implements it | §2 |
| D16 | **One DECISIONS entry in c3** (the next free R5-M after H10b's): the switch ends (D1, D2, D3, D5); the identity format (D4); FreshProcess's whitelist (D8); `-Pipeline` and the summary field (D10); the kept entry-point tests and the recount (D6; departs from R5-M182's H12 size and H0c §4.2's "`T/PipelineSwitchTest.cs` (449)"). Amendment lines, as R5-M160 did: R5-M152 (S1's `CompilerIdentity` marks; G1's switch file, R5-M7's hold pair), R5-M156 (`KIMI_PIPELINE=mir` "until H12"), R5-M160 (`KIMI_PIPELINE` and `PipelineSwitchTest`'s loops "until H12", fulfilled), R5-M182 (H12's row) | AGENTS "Record decisions": an ended temporary structure, superseded clauses named | `docs/dev/DECISIONS.md` |

## 1. Inventory (item 1)

| Path | Member or line | Fate | Commit |
| --- | --- | --- | --- |
| `C/Core/Compilation.Switch.cs` (68) | `DefaultPipeline`, `pipeline`, `CompilerIdentity`, `Pipeline`, `PipelineOf`, `CompilerPipeline` | deleted (D1, D3) | c3 |
| `src/Kimi/Checking/CheckDocument.cs:50` | `Compilation.CompilerIdentity` | → `CompilerVersion` (D3) | c3 |
| `src/Kimi/Testing/TestCatalog.cs:122-125` | `pipeline mir` manifest rows | deleted (D5) | c3 |
| `B/AnalyzeCostMeasurements.cs:35`, `B/CompilerPipelineMeasurements.cs:71, 97`, `B/SyntaxEditMeasurements.cs:68` | `CompilerIdentity` | → `CompilerVersion` (D3) | c3 |
| `B/CollectionCostMeasurements.cs:185, 189`; `B/DiagnosticMeasurements.cs:207` | `c.Pipeline = Mir`; report field `pipeline` | deleted (D9) | c3 |
| `T/PipelineSwitchTest.cs` (405; ≈ 389 after H10b C1) | 4 switch tests + race child, `Pipelines`, `Check`, `Manifest`; 4 loops | deleted / collapsed; file → `CompilationEntryTest.cs` ≈ 260 (D6) | c1 |
| `T/MirBuildTest.Places.cs:206, 227-243` | two-pipeline loop, "on every pipeline" | one compilation (D7, D12) | c1 |
| `T/RawStorageTest.cs:357, 386, 392` | `RawOriginsGenerateOnTheDefaultPipeline` | → `RawOriginsGenerate` (D7) | c1 |
| `X/FreshProcess.cs:11-12, 16-18, 22-26` | `SelectionVariables`, its clause and text | deleted (D8) | c1 |
| `T/FreshProcessTest.cs:9-14, 23, 30` | method name, `kimi_pipeline` row | renamed; row dropped-side (D8) | c1 |
| `T/CheckJsonOutputTest.cs:91` | `CompilerIdentity` | → `CompilerVersion` (D3) | c3 |
| `scripts/verify.ps1:25-27, 41, 69-71, 328-329` | `-Pipeline`, `KIMI_PIPELINE`, summary `pipeline` | deleted (D10) | c2 |
| `scripts/compare-fixtures.ps1:66, 104, 118`; `tests/scripts/FixtureComparisonTest.ps1:20` | `pipeline` field | deleted (D10) | c2 |
| `src/backend/windows-x64/benchmark-o2-corpus.ps1:5-6, 20-22`; `test-library.ps1:24-25, 75-76` | selector guard; `selectors` record | deleted (D11) | c2 |
| `docs/dev/VERIFICATION.md:94` | the `-Pipeline mir` paragraph (its `-Codegen` sentence is R5-M160's history) | deleted | c2 |
| `src/Benchmark/O2Corpus.md:34-36` | `KIMI_PIPELINE=mir` example and refusal | neutral example, refusal sentence deleted | c2 |
| `src/Benchmark/CompilerPipeline.md:23-26, 41-42, 78-80, 91` (check-del's lines) | `KIMI_PIPELINE` changes no stage; `[mir]` in `compiler`; "the process's pipeline", "every pipeline's", "mir-pipeline compilation" | rewritten (one pipeline) | c3 |
| `docs/dev/COMPILER_ARCHITECTURE.md:96, 98, 151, 255, 286` | switch row and file; "on every pipeline" ×2; temporary structure `KIMI_PIPELINE`; hold pair `Compilation.Switch.cs` | removed / dropped (Auto: recorded contract and temporary structure end) | c3 |
| `docs/dev/CODEMAP.md:13, 22` | `Compilation.Switch.cs` link, `KIMI_PIPELINE`, `PipelineSwitchTest` ×2 | removed; → `CompilationEntryTest` | c3 |
| `docs/dev/DIAGNOSTICS.md:59, 296, 364` | "on every pipeline" | dropped (Auto: the publication contract names the removed set; OQ3) | c3 |
| `src/Kimi`, tests: comments | token lines (D12): `MirEffectsTest.Destructions.cs:88`; false lines ≈ 12 (`both pipelines` 5, `either` 2, `each` 2, `process's` 2, `mir pipeline` 1); vacuous ≈ 40 (`every pipeline` 28 in 20 files, `default pipeline` 12 in 9) | rewritten; vacuous with consent (OQ2) | c4 |
| `docs/STATUS.md` rows 22, 24, 40, 52, 77, 79, 80 | 11 "both/every pipeline(s)" phrases | H15's STATUS pass (no boundary changes at H12; OQ3) | — |
| `docs/dev/PLAN.md:24`, `:29` | "`KIMI_PIPELINE` selects only the ledger and the mark" (twice; or H10a's landing rewording) | R5's landing PLAN commit | — |
| `src/kimi-ext`, `src/Playground`, `docs/spec`, `docs/impl`, README | none (verified: the extension reads only `KIMI_EXTENSION_UNDER_TEST`, `KIMI_TEST_SERVER_PATH`) | — | — |
| DECISIONS, PLAN_HISTORY, `draft/` | history | kept | — |

**F13 recount:** plan "6 declarations, ≈ −300"; H0 "5 + `CompilerIdentity`, ≈ −600". On the base: 6 members in one file (5 Transitional
R6 plus `CompilerIdentity`), 68 lines; ≈ **−220 net** (§4), because 9 of `PipelineSwitchTest`'s tests test the entry points and stay.

## 2. Checks and verification (item 5)

| Commit | Required checks (Unit unless stated) |
| --- | --- |
| c1 | `CompilationEntryTest`, `MirBuildTest` (also a FreshProcess user), `RawStorageTest`, `FreshProcessTest`, the other FreshProcess users `AllocationMeasurementTest` and `Kernel32ImportsTest`, `ArchitectureRulesTest` (common); build `-warnaserror` |
| c2 | Tooling row (VERIFICATION scope): `FixtureComparisonTest.ps1`, `VerificationSelectionTest.ps1`, `VerificationInputsTest.ps1`; a PowerShell parse of `benchmark-o2-corpus.ps1` and `test-library.ps1`; c2's own Unit run (verify.ps1 without `-Pipeline`, `summary.json` without `pipeline`); `compare-fixtures.ps1 -Baseline <a pre-H12 run> -Candidate <c2 run>` showing an old summary still compares. verify.ps1's environment handling changes the test pipeline, so the window's Session also covers c2 |
| c3 | slnx build (Benchmark); `CompilationEntryTest`, `CheckJsonOutputTest`, `TestProtocolTest`, `FunctionCodegenTest` (`.Testing` reads `ArtifactId`), `CheckServiceTest`; `Benchmark --compiler-pipeline-check`, `--collection-cost-check`, an `--analyze-cost` smoke run; the exit-gate grep (below) returns only c4's comment lines and PLAN `:24`, `:29` |
| c4 | build `-warnaserror`; `ArchitectureRulesTest`; the grep below returns only PLAN `:24`, `:29` (the landing commit's) |
| Window (G:check-del) | Session with `-DiagnosticSnapshot` (0 differences: no producer changes), `compare-fixtures.ps1` against the window base (IR identical), the 40 milestones and the five CLI harnesses (`test-library` writes no `selectors`); no `-Pipeline mir` run exists. Natives: no family needed for H12 alone (no generated code changes) |

- **Exit-gate grep** (H0c §5.3 item 4; the `CompilerPipeline` alternative skips `CompilerPipeline.md`, `.kimi`, `…Benchmark`,
  `…Measurements`): `git grep -n -E 'KIMI_PIPELINE|CompilerPipeline(\.(Legacy|Mir)|[^.MB]|$)|DefaultPipeline|PipelineOf|CompilerIdentity|\[mir\]|-Pipeline\b|\.Pipeline\b' -- src tests scripts docs ':!docs/dev/DECISIONS.md' ':!docs/dev/PLAN_HISTORY.md'`.
  Checked on the base (check-del): it returns exactly §1's sites (23 files, PLAN `:24` and `:29` included) and nothing else.
- **Performance:** no compiled path changes; `Compilation` loses one byte field and its type initializer one environment read. No
  measurement claimed or needed.
- **Documents (Auto):** ARCH, CODEMAP, DIAGNOSTICS (c3); VERIFICATION, O2Corpus.md (c2, usage invalidated); CompilerPipeline.md
  (c3); DECISIONS (D16). PLAN at landing (position; `:24`). STATUS: none (OQ3). README: none.

## 3. Notices to other sessions (item 3; R5 posts them on the board before H12's merge)

| Consumer (found in the notes folders, 2026-11-05) | Effect after H12 | Action | To |
| --- | --- | --- | --- |
| R4's window scripts (`r4w14`–`r4w16/window.ps1`, lane d `verify.ps1 -Pipeline mir`); `run-ledger*.ps1` | `-Pipeline` fails at parameter binding (corrected at c6: it binds `-PipelineVariable` by prefix and has no effect, so lane d is a duplicate default run) | drop lane d (the mir ledger already ended at H10a) and the argument | M (R4) |
| BU's window (`G216a-U2/window/window.ps1` lane d); `G404/r*-unit-mir.ps1` beside `-legacy.ps1`; `G426`, `G386`, `G233` `ledger.sh`/`unit.sh` | same failure for `-Pipeline` (corrected at c6: no failure; a duplicate default run, as above); the mir Unit copies duplicate the default | drop lane d and the mir copies | F (BU) |
| The generated gate and oracle (`G216a-U2/gate/one.sh`, `oracle/one.sh`, `artifacts/verify/g216a-oracle/one.sh`: `KIMI_PIPELINE=$pipe check` on legacy and mir) | both passes run the same check (already since H5); no `[mir]` in `compiler` | keep one pass (halves the gate's time); do not key rows on the mark | F, G |
| Refusal guards listing `KIMI_PIPELINE` among inherited variables (BU's window, R5's `h9` scripts) | harmless | none | — |
| Any unit class list naming `PipelineSwitchTest`, `TheChildKeepsThePipelineSelectionAndNoVariableOfTheRunsEvidence` or `RawOriginsGenerateOnTheDefaultPipeline` | verify fails an unmatched selector | use the new names (D6–D8) | all |
| Evidence: `shared-base/*/summary.json` (`pipeline`), ` [mir]` check documents, mir-built test artifacts | old summaries still compare; nothing else reads them | none | — |

## 4. Sizes and structure limits (item 6)

| Area | Lines (H) |
| --- | --- |
| `src/Kimi`: Switch −68, TestCatalog −4, CheckDocument ±1 | ≈ −72 |
| `src/Benchmark`: 4 identity sites ±4, 2 selection lines −2 | ≈ −2 |
| tests: `CompilationEntryTest` ≈ −130, Places −4 (±13), FreshProcess(Test) −4 (±8), CheckJson ±1, RawStorage ±3 | ≈ −140 |
| scripts: verify.ps1 −8, compare-fixtures ±3, FixtureComparisonTest ±1, two backend scripts −3 (±4) | ≈ −11 |
| docs: VERIFICATION −2, ARCH/CODEMAP/DIAGNOSTICS/Benchmark docs ≈ −3 (±25); DECISIONS +14 | ≈ +9 |
| c4 comments | ±55, net 0 |
| **Net** | **≈ −220** (H0: ≈ −600) |

Structure limits: deletions and renames only; no file or member grows; `CompilationEntryTest` ≈ 260 lines, far under the limits; the
`Compilation` partial class loses one file.

## 5. Open questions for R5 (all non-blocking)

| # | Question | Recommendation |
| --- | --- | --- |
| OQ1 | Rename `PipelineSwitchTest` (D6), keep its name, or move its 9 tests into `MirPublicationTest` and a gate class? | Rename to `CompilationEntryTest` (one `git mv`, history kept; a move spreads one subject over two classes) |
| OQ2 | Sweep the ≈ 40 vacuous "every pipeline"/"default pipeline" comment lines in H12 (c4, with consents from R4 for `C/Mir/MirProgram.cs:32` and the `Mir*Test` files, and BU for its test files by PLAN §3's areas), or leave them? | Sweep in c4; without consent the line stays and is named |
| OQ3 | DIAGNOSTICS's three and STATUS's eleven "every/both pipeline(s)" phrases: H12 or H15? | DIAGNOSTICS in c3 (its publication contract names the removed set: Auto); STATUS at H15's STATUS pass (H12 changes no boundary) |
| OQ4 | `benchmark-o2-corpus.ps1 -CandidateEnvironment`, whose documented use was the selectors: keep as a general A/B variable mechanism or delete (≈ −15)? | Keep; its example becomes a neutral variable |
| OQ5 | D14's "no separate design review": if R5 reads D6 as a departure from R5-M182 needing review, a short design review by BU | R5 decides; H9 is the precedent for none |
| OQ6 | Run H12 beside H10b in a separate worktree instead of after C1? | No: both edit `PipelineSwitchTest` and the R5-M7 hold pair; H10b-i is one phase |

## 6. R5's rulings (2026-11-05)

- **Class Major without a separate design review** (OQ5: no departure in owning component or representation; renaming a test class
  within its subject is not a departure from R5-M182). One implementation review by an independent reviewer; the group review follows.
- **OQ1:** rename to `CompilationEntryTest` (`git mv`). **OQ2:** sweep in c4 with consents (R4 for `C/Mir/MirProgram.cs:32` and the
  `Mir*Test` files, BU for its test files); without consent the line stays and is named. **OQ3:** DIAGNOSTICS in c3, STATUS at H15.
  **OQ4:** keep (a neutral example). **OQ6:** no: H12 runs after H10b's C1 on G:check-del's branch.
- **Tooling notice before H12 lands** (R5 posts it on the board): `verify.ps1 -Pipeline` is removed, so R4's and BU's lane-d window
  scripts and BU's G404 mir copies must drop it; the gate's and oracle's `KIMI_PIPELINE` mir passes become duplicates.
