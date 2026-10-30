# G11 design: the R6-gen exit (census, records, measurements, owner review)

Design only (2026-10-31, R5 planning session's G11 designer). Base `25570bb00` (dev with G9, batch 67) plus `claude/r6-g10` `c15b4ab1e`
(G10, reviewed); `origin/dev` `209fbc730` did not contain `25570bb00` when this was written. Authority: plan §4.9 row G11, §5.1 (M4, M8,
PLAN10-P3), §5.4 items 1-7, OQ17 (c); DECISIONS R5-M148 (the model), R5-M152, R5-M156 … R5-M165 and G10's line on R5-M162; ARCH (ledger,
freeze D5, temporary structures, code rules); AGENTS "Performance and Compiler Structure" (DECISIONS Q9). Census counts are greps at
`c15b4ab1e` unless stated.

## 1. Exit gate (§5.4) item by item

| # | Item | Already met (evidence) | G11 still runs or records |
| --- | --- | --- | --- |
| 1 | Session green with `'*.ll'` at O0/O2, all milestone harnesses and the CLI harnesses, on MIR codegen by default | G3's sweep at `0d4108f0d` (R5-M158); G7's and G8's bp Sessions (12,240 natives, 40 milestones, 5 CLI; `.ll` identical, 30,608 files); G9: Session 25,627/0 (6 not run), snapshot 0, 5 CLI, **no natives**; G10: Unit only | The exit sweep E2 on the exit head (§3.1): Session + `'*.ll'` + snapshot, the 40 harnessed milestones, the 5 CLI harnesses; compared with the base sweep E1 |
| 2 | Snapshot differences classified; the 20 instance-failure methods assert check-time records; `stage-baselines.json` re-baselined with reasons | G2 (R5-M156: 108 P42 rows, Milestone 42, the 9 extra rows in 5 methods), G3 (R5-M158), G4-G10 snapshot 0 against each base (G9: 0 vs `f600ef4c8`) | E2's snapshot against E1's: 0 expected (G11 changes no check) |
| 3 | O2 corpus within variance; audit counts unchanged or lower; warm allocation tests pass | O2: G3 run 3 (`artifacts/benchmarks/r6-g3-run3/`, A = G2 `678e03e5d` old generator, B = G3 `519d88f38`): every kernel within A=A or explained by R5-M135/R5-M147 controls. Audits: G287's M5 = 0 (`artifacts/benchmarks/r6-gen/g287-m5/`). Allocation: 1,179 `Purpose=Allocation` tests pass on the default pipeline at G2 | O2: the kernel IR identity check (§3.3); a timing re-run only if it differs. Audits: re-run `count.sh` at the exit SHA and read the sites (G287's note). Allocation: E2 runs them (TestPurpose All); record the count and the not-run ones |
| 4 | Zero-reference rows for the Emission types; M8's Emission rows at 0 | G10 (`c41ebd519`, `c15b4ab1e`): 12 type names + instance entry points, limit 0 over `*.cs` of `src/Kimi`, `src/Benchmark`, `src/Playground`, `tests`; `LegacyRuntimeHelpers` until R8; failure strings 0 (G7) | Merge G10 first. The rows scan `*.cs` only: G11 fixes the two non-`.cs` hits (`O2Corpus.md:51`, `WindowsRuntime.ll.in:18`) and records the rows' scope |
| 5 | No `[Transitional("R6-gen")]`; `GenerationFailed_Kd` replaced; `AnalyzeInstance` deleted | 0 declarations (grep: R6 51, R6-check 9, R7 12, R8 28); R5-M165 (`ArtifactPublicationFailed_Kd`); R5-M162 | Nothing; the owner table (§4) lists the 100 declarations |
| 6 | PLAN10-P3: "BuildMir + collection + codegen" against the old Analyze + Emission; G2's default check and warm LSP Analyze | G2's half: R5-M156 Cost (`artifacts/benchmarks/r6-g2-cost/`, owner 「受け入れる」). R5-M148 item 10 measured collection + MIR codegen against the old stages 5-6 (before G2) | The whole-compilation A/B (§3.2) with A = the last old-generator default; one `--analyze-cost` A/B for the exit's check (G8c removed old-analysis work) |
| 7 | Records and the final review (no NativeAOT run, OQ17 (c)) | — | §2 records, §3.4 areas, §4 owner table, §6 final review |

## 2. Document census

Authority column: AGENTS Documentation table. **Explicit** rows need the owner's scope before G11 edits them.

| Document / site | Change | Authority |
| --- | --- | --- |
| STATUS `:57-58` | Drop the transition sentence ("MIR code generation is the one code generator from R6-gen's G4 …"): STATUS keeps current facts; the code generator needs no qualifier | Auto (known-limit text) |
| STATUS `:86` (Raw pointers and FFI) | "both pipelines move the value into a temporary" describes code generation, which no longer depends on a pipeline: "code generation moves …" | Auto |
| STATUS `:62`, `:64`, `:66` ("pass checking and fail generation": P34's G class, `(p@uniq, 1)`, conditional temporaries) | Probe each reproducer under MIR codegen (`check`, `build`, run): generated and correct → drop the clause; rejected at check → the row states the rejection; still failing (`InternalInvariant_Kd`, crash) → keep with the MIR outcome. The claims date from the old generator (P34_REMAINING names `BodyLowering`) | Auto (boundary changes); probes in G11a |
| CODEMAP feature table | Column "Lowering / IR" → "Code generation"; row "Backend (R5)" → "Backend"; `:74`'s note keeps one clause; "IR and native artifacts" paths follow §5's folder decision | Auto (navigation) |
| DIAGNOSTICS | No change found: G9b rewrote the Emission partition row (`:94`); `:361` is current; `:220` "the generators" means the corpus's case generators. The reviewer confirms | Auto (none) |
| VERIFICATION (the plan's "fixture policy") | No change: G4 deleted OQ9's rows (two generators, sweeps at group reviews); `:94` (`-Pipeline mir`) stays until H12; `:138` is history. A new policy (e.g. which fixtures a Backend change selects) would be a policy change | Auto none; policy = Explicit |
| ARCH ledger R6 row | "R6-gen done 2026-10-31 (DECISIONS R5-M166 …)"; R6-check pending R4's exit | Auto (stage state), after the owner's table |
| ARCH freeze (D5) | "Emission until R6-gen" → ended; the Emission clauses (G147's "where the old Emission is shown to generate them", g2never's generator clause, BU-24's ended exceptions) read as ended; the Analysis freeze and its exceptions continue | Auto (recorded contract), owner row 12 |
| ARCH "R6-gen progress" and "R4 beside R5, then R6-gen" | Progress paragraph ends (M4, M8 final values); the hold protocol per owner row 13 | Auto / owner |
| DECISIONS | R5-M166: the exit gate item by item, the owner review, the folder and comment decisions, the corrections | Auto |
| PLAN §3 | Position (R6-gen done; next), G330 and G157's R6-gen-staged items re-staged (§6) | Auto |
| `src/Benchmark/O2Corpus.md:51` | "lowering collects … re-analyses each (`AnalyzeInstance`)" describes the deleted generator: stages 5-6 are MIR code generation, collection in stage 3 (the following sentence already says so) | Auto (benchmark usage text G7 invalidated) |
| `src/Benchmark/CompilerPipeline.md:19`, `:117` | `Compilation.WriteIr` (deleted at G9b2) → `Compilation.Generate` | Auto (G9b invalidated) |
| `docs/dev/LLVM_OPTIMIZATION.md:684-686` | Three bullets link 10+ deleted files (`WindowsLowering`, `FunctionAbiPool`, `EmissionModule`, `BodyLowering.*`, `LlvmModuleWriter.*`) | **Explicit** (unlisted document; links are not usage instructions) |
| `docs/dev/P34_REMAINING.md` (G class, `BodyLowering.ValidateStringInspection`, `GenerationFailed_Kd`) | Causes name the old generator; G11a's probes give the facts | **Explicit** (unlisted); STATUS links it |
| `docs/dev/BINDING_REVIEW.md:22`, ARCH `:233`, README `:52` | History / unrelated | None |
| SPEC, IMPL, LIBRARY, GUIDE, extension | No old-generator text (grep) | None |

Code text (not documents; G11a, comments only): present-tense two-generator claims, 20 sites: `scripts/compare-fixtures.ps1:128`,
`:177`; `tests/xUnitTest/GeneratedIr.cs:9`, `NativeAllocationAudit.cs:15`; Tests `AbstractCallReachTest:238`, `ElementBorrowEmissionTest:136`,
`FunctionCodegenTest:44`, `:1187`, `FunctionCodegenTest.Erasures:28`, `:235`, `GeneratedIrTest:12`, `InlineLayoutTest:143-144`,
`SharedArcRuntimeTest:35`, `StringEmissionTest:126`, `StringFunctionEmissionTest:56`, `Utf8FloatTest:153`, `Utf8InterpolationTest:138`;
`Analysis/OwnershipAnalysis.Match.cs:38` (the old Analysis, frozen; a comment fix is allowed). Kept: provenance phrases that cite a
decision ("the old generator's text … PD2", "in the old generator's order", 31 files / 41 lines in all) and the Backend's "legacy row"
vocabulary (`MonoKind.LegacyRow`, R8). `FreshProcessTest.cs:24` keeps `KIMI_CODEGEN` on purpose (a stale variable selects nothing).

## 3. Measurements

### 3.1 Sweeps (heavy runs; register in `artifacts/heavy-runs/`, output outside the tree)

- **E1 (base)**, dev with G10 merged, before G11c: `pwsh -Command "./scripts/verify.ps1 -Mode Session -Fixtures '*.ll' -DiagnosticSnapshot
  -NativeParallel 4 -Name g11-e1-<sha>"`.
- **E2 (exit)**, after every G11 code and record commit except the owner-dependent ledger line: the same with `-DiagnosticBaseline
  <E1>/diagnostic-snapshot.json -Name g11-e2-<sha>`; `verify.ps1 -Milestone <X1's 40> -Name g11-e2-milestones-<sha>`; the 5 CLI harnesses on
  E2's Release `Kimi.dll` (x1-sweep.ps1 step 7's commands); `compare-fixtures.ps1 -Baseline <E1> -Candidate <E2> -O2Metrics`.
- Expected: snapshot 0; sidecars and native results identical; each `.ll` differs from E1's only in the template comment lines of §5
  (shown by a scratch script that drops those lines and compares bytes, kept with the evidence). If G11c is not taken, `.ll` identical.

### 3.2 PLAN10-P3 compile time (quiet slot; G2's method, R5-M156)

- A = `acc2f12ad` (G2's merge base: the old generator is the default, `Analyze` builds no MIR); B = the exit head. Release `Kimi` and
  `Benchmark` of each, copied into two sibling folders of equal path length under one parent; check each report's compiler id.
- `Benchmark --compiler-pipeline` and `--collection-cost`, CPU 2 pinned, High priority, `DOTNET_TieredCompilation=0`, 5 interleaved
  rounds, Scalar, Branching and Generic at 1/32/64/128; times medians, bytes exact.
- Record: A's default build = stages 1, 2, 4, 5, 6 (its stage 3 is the harness's explicit `BuildMir`, which A's default never ran); B's =
  stages 1-4, 6 (stage 5 empty, stage 4 the old analysis over stage 3's memoized MIR, until R6-check). The plan's figure is B's stage 3 +
  stage 6 against A's stage 4 + 5 + 6, beside both totals and the allocations.
- `--analyze-cost` A = G2's head `90697ecfa`, B = exit (fresh check, warm Analyze, retained), and BU-48's pinned Hello/Milestone26 harness:
  the exit's check cost after G7-G8c's deletions from the old analysis.
- Output `artifacts/benchmarks/r6-gen-exit/` (scripts, raw JSON, `summary.md`); figures go to R5-M166.

### 3.3 O2 corpus

G4-G10 changed no code generation (G4, G7, G8: every common fixture `.ll` identical; G9: publication and tests only; G10: a test). Run 3
stands if the 8 kernels' IR is identical: build `src/Benchmark/Kimi/Corpus/*.kimi` with G3's B (`519d88f38`; reuse `bin/worktrees/r6-g3`'s
Release build if present) and with E1's tree, compare the `.ll` bytes (no timing, any slot). Different → a quiet-slot run of
`benchmark-o2-corpus.ps1` (A = `519d88f38`, B = exit) under R5-M135/R5-M147's controls.

### 3.4 Areas, hotspots, duplication candidates (R5's method)

- Copy `artifacts/benchmarks/r5-exit/x3-areas.ps1` to `artifacts/benchmarks/r6-gen-exit/g11-areas.ps1`; change only the commit set:
  R6-gen's commits are the non-merge commits that "Merge claude/r6-g<N>…" merges of G0-G11 bring in plus first-parent commits whose
  subject starts "R6-gen"; the other `claude/r6-*` merges (fi, g167, g293, g320, …) are the R5 session's follow-ups, a separate column.
  `-Base 66c856d81` (R5's area record) `-Head <exit>`; `-Also 5c42b5773` keeps the R5-start column.
- Output `artifacts/benchmarks/r6-gen-exit-areas.md` beside `r5-exit-areas.md`: sizes per area and sub-area, M4 (Emission `.cs` 18,284
  at R5's exit → 1,362 lines in 8 files at `25570bb00`, templates 2,335; Backend), R6-gen hotspots per file, files over 5,000 tokens,
  line-clone candidates (window 6) plus the known ones (G157 (5)'s twin witness assembly, the provenance comments of §2).
- Audits: `bash artifacts/benchmarks/r6-gen/g287-m5/count.sh` at the exit SHA, sites read; result beside the areas.

## 4. Owner review: exceptions and Transitional declarations at R6-gen's exit

100 declarations at `25570bb00` (R5's exit: 102 = R6 50, R6-gen 14, R7 11, R8 27). Since then: R6-gen −14 (G4, G7, G9b); R6 +1 (R4's
`a35f79cbb`, R4-M152: `FunctionKoto.ClosureAsCreated`); R6-check +9 (R4-M152's gate, BU-62's chain copy); R7 +1, R8 +1 (R5-M148's
`StorageNesting`, `BodylessWitness`). File `artifacts/benchmarks/r6-gen-exit/exceptions-and-transitional.md` carries this table.

| # | Item (declarations) | Stage, condition | Basis | Recommendation |
| ---: | --- | --- | --- | --- |
| 1 | `KIMI_PIPELINE` switch (`Compilation.Switch.cs` 5) and the ledger's `Analyzed` event (`Compilation.Pipeline.cs` 1) ("R6") | H12 (§5.5 item 4) | ARCH temporaries | Stands |
| 2 | Koto forwarding properties (37, "R6"; +1 since R5) | R7; Parsing/root reads at R6-check; attributes change at H14 | R5-M9 (owner) | Stands |
| 3 | Binding old-path markers: `Binding.Lints` 3 (H10), `EffectObligations` 1, `OriginFits` 1 (H11) ("R6") | R6-check | R5-M23, R4-M111 | Stands |
| 4 | Legacy comparison composition: H11 parts (`ComparisonPlans` 2, `Formatting` 1; "R6") and the R7 fallback (`ComparisonPlans` 2, `Formatting` 2, `BoundComparison` 1) | H11; A-12, else R7 | R5-M68, R5-M148 | Stands (G7 updated the reason text) |
| 5 | R4's receiver-dependence gate (`ValueCallGate` 4, `CallPlan` 3, `OwnershipAnalysis` 1) and BU-62's chain copy (`OriginClauses.Chains` 1) ("R6-check") | R6-check (G318 for the copy) | R4-M152, BU-62 | Stands; R4's and BU's, listed for the count |
| 6 | Capability rows `StorageNesting`, `EnclosingInstance` ("R7"), `BodylessWitness` ("R8") | R7 (PD30, G149/G300); R8 | R5-M146, R5-M148, R5-M157 | Stands |
| 7 | HIR publisher walk (`Binding.Hir` 2, `.Adjust` 1, `.Operations` 1; "R7") | R7 | R2b-D2 | Stands |
| 8 | `TyCtxt.StorageSubst` (1, "R7") | R7's CallPlan pipeline | R5-M36 | Stands |
| 9 | `LegacyRuntimeHelpers` (19 in 5 files) and `MonoKind.LegacyRow` (`Instantiation` 1) ("R8"); excluded from G10's rows | R8 | R5-M16, R5-M116, R5-M121 | Stands |
| 10 | `Layout.Rows` handle words (1), F2c Dictionary-storage view (2), temporary intrinsics (4; `IntrinsicId` limit 34, 32 used) ("R8") | R8 (limit back to 28) | R5-M19, R5-M28, R5-M137 | Stands |
| 11 | Gate step 2's arena-skip `Unsupported_Kd` (a statement) | H3 | R5-M96, R5-M148 | Stands |
| 12 | Freeze D5, Emission half | Ends at this exit (no old Emission code remains); the Analysis half and its exceptions continue to R6-check | R4-M35 | **Owner**: approve the end; ARCH marks the Emission clauses ended |
| 13 | R5-M7's shared-file hold protocol, carried to R6-gen beside R4 (R5-M148) | R6-gen's exit | ARCH | **Owner**: carry it to the R5 session's next work beside R4 until R4's exit (recommended), or end it |
| 14 | `DiagnosticPartition.Emission` and the `Emission` requirement, "through R6" (R5-M18) | Unscheduled | R5-M18 | **Owner**: lasting, no attribute: the partition is the generation request's (recommended) |
| 15 | `C/Emission`'s survivors (toolchain, publication, profile; the `.ll.in` templates) | §5 | R5-M163 | **Owner**: §5's recommendation |
| 16 | `CodeRuleException("KR2006", Limit = 67)` (`MirBuilder.Values`) | Lasting | ARCH limits | Stands |
| 17 | Open issues staged to R6-gen: G330 (thrown code generation contract failures, "with G11 or after"); G157 (7) (a configuration and a generation failure hiding each other at publication, staged to G9b) | — | PLAN | G11d checks whether G9b closed G157 (7) (R5-M165 clears the partition per request) and re-stages what remains; G330 per §6 Q1; neither is an exit item |
| 18 | Unlisted documents LLVM_OPTIMIZATION §6.1, P34_REMAINING | — | AGENTS | **Owner**: scope to update both (recommended) |

## 5. The `.ll.in` comment and the folder

**Comment** (`WindowsRuntime.ll.in:18` names the deleted `WindowsLowering`; `:1` says "the emitter's common #0 profile"; the template is
spliced into every module, so every `.ll` carries both lines). Options: (a) name the owner, `RuntimeAbi` (whose `:131` summary documents
the expansion) and "the Backend's" profile; (b) delete line 18 (the code side documents it); (c) leave it (a deleted name shipped in
every output; G10's rows cannot see it). **Recommend (a)**: one declared `.ll` text change in one commit (G11c), E1/E2 showing nothing
else changed.

**Folder** (`C/Emission`: 8 `.cs` files, 1,362 lines, namespace `Kimi.Compiler`; 5 templates). Options: (A) keep everything, recorded;
(B) move the 5 templates to `Backend/` (their readers are the Backend's `LlvmModuleText` and `LegacyRuntimeHelpers`, P11-P12's
"LLVM text"), keep the toolchain and publication files; (C) move or rename everything now. Cost of a template move: `Kimi.csproj` 5
lines, 2 manifest names (`LlvmModuleText.cs:227`, `LegacyRuntimeHelpers.Formatting.cs:296`), `Kernel32ImportsTest` 2 paths,
`compare-fixtures.ps1:131`, `generate-ryu.ps1:74`, CODEMAP, LLVM_OPTIMIZATION's links (explicit). **Recommend (B) in G11c** with the
comment, as a behavior-preserving move commit before the comment commit; keep the rest in `C/Emission`, recorded: the target pipeline's
P13 (Build) is outside the compiler, R9 moves the toolchain out, and the folder's main type and the partition keep the name "Emission"
(owner row 14), so renaming now would move the files twice. (C) is that double move.

## 6. Units, review, questions

| Unit | Content | Verification | Agent time |
| --- | --- | --- | --- |
| G11a | Text: §2's Auto rows except ARCH/DECISIONS/PLAN; the 20 comment sites; STATUS probes (CLI probe recipe, scratch `.kimiproj`) and rows | `-warnaserror` non-incremental build; Unit `ArchitectureRulesTest`; covered by E2 | ≤ 1 h (probes 20 min) |
| G11c | (B)'s move (bp), then (a)'s comment (declared `.ll` change) | E1 before, E2 after (§3.1) | ≤ 40 min + runs |
| G11b | §3.2-§3.4: timings in a quiet slot, kernel IR check, areas, audits | Benchmark runs; no product change | ≤ 1 h + runs |
| G11d | Records: R5-M166, ARCH (ledger after the owner), PLAN, the owner table file | E2 covers all but the ledger line (a Unit run for its evidence) | ≤ 45 min |

Order: G10's merge; G11a ∥ G11b's areas and audits (read-only); E1; G11c; G11d's records; E2; G11b's quiet slot; the final review; the
owner's table; the ledger line.

**Final review** (one reviewer who wrote none of R6-gen; plan G11 "final review", no NativeAOT run): §1's table against the evidence;
the doc diff against §2; residual names in every file type (not only `*.cs`); the generation entry's contract (`Compilation.Generate`,
`EmissionArtifacts.Publish`, G330's open exceptions) against DIAGNOSTICS; ARCH's freeze, temporaries and ledger against the code; the
measurement method (same folder, pinned, compiler ids); the owner table's counts.

**Questions.** Planning session: Q1 G330 inside G11 or the first unit after the exit (recommended: after; it needs its own design); Q2
§5's choices; Q3 A for PLAN10-P3 (`acc2f12ad` recommended over R5's `66c856d81`: closest old-generator default); Q4 whether G9's
missing native run needs E1 (yes: E1 is also G11c's base). Owner: §4 rows 12-15 and 18, approval of the table, and the ledger's
"R6-gen done".
