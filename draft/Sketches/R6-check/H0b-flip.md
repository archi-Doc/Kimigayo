# R6-check H0b: the acceptance flip (H5) with H1 and H4, and the rehearsal's classification method (H3b)

Status: H0's design part b (plan §4.10 rows H0, H1, H3b, H4, H5; PD29, PD12, PD31), revision 1, design only; integrated by [H0-design](H0-design.md) (the planning session's rulings
R1–R4 and the design review's findings applied in place, each marked "(ruling Rn)" or "(review Bn/Nn)"); revision 2 (after R5-M182's
approval) folds the peers' answers and R4's review, marked "(answer X)" or "(R4 review Nn)". Working
record, not authority: what it decides becomes authority only through H0's DEC entry after OQ11's approval (DECISIONS R5-M1), and its
owner items only by the owner's answers.

Working time (WORKFLOW "Working-time limit", DECISIONS Q12): 2026-11-02 18:01–18:24 (about 23 minutes), one session, within the one-hour limit.

Standing request: the owner approved R6-check's division of work on 2026-11-02 (R5 leads H0, H3b, H5's Core side and the windows; R4
owns the MIR side: H3's publication in `MirReport`, H4, asks A-4/A-5, MIR false rejections; BU owns the Binding side: H1, H7, H11, the
end of the old-analysis D5 exceptions, Binding-cause rows) and told R5 「その役割分担でOKです。H0の設計から始めて下さい。終わったら、
R4の完了を待ってください。」

Base: dev `e8ba140b3` (code, DECISIONS, PLAN, STATUS). Builds on the unreviewed parts H0a (`origin/claude/r5-h0a`, the publication
table, H3) and H0c (`origin/claude/r5-h0c`, deletions after H5); §9 lists where this part disagrees. "Plan" is the
[R5 and R6 Implementation Plan](../../Proposals/2026-10-17%20R5%20and%20R6%20Implementation%20Plan.md); "Q8" is
`draft/Sketches/R5 Step 0/Q8-package.md` (owner-approved, R5-M23). Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`.
Counts are greps over the base. No build, test or probe was run. (H) marks an unchecked inference.

## 0. Decisions in one screen

| # | Decision | § |
| --- | --- | --- |
| H0b-1 | **G:R6-check-flip = H1 → H1b (review B2) → Q8-3 (validator rule) → H5 (with H6, review B3)**, one branch, one group review, one merge; H1a, Q8-6's renderer and H4 land on dev before it (bp or shadow) | 2, 3, 8 |
| H0b-2 | **H3b is the group branch's first full run, not a throwaway prototype**: the rehearsal applies H1, Q8-3 and H5 as implemented for landing; fixes it finds land on dev as R4/BU units; the branch is rebased and re-run until no row is class 2 or 5 | 5 |
| H0b-3 | H5's Core change: `Analyze` invalidates the ControlFlow and Ownership partitions itself and builds the MIR; it no longer runs the old analysis, which runs only through one explicit entry, `Compilation.AnalyzeLegacyOwnership()` (`[Transitional("R6-check")]`, removal H10b), for its test and benchmark readers (review B3); `Analysis` = the MIR of this version exists and its published rows hold no Error; `Problems` = those rows; `ReportAnalysis` publishes them through H3's publisher only | 2.1 |
| H0b-4 | `Analysis.IsVerified` reads **no `Binding.LintErrors` term** from H5: a lint Error rejects through `Accepts`' Binding-partition error state (SPEC 23.2.2); `AnalysisResult.BodyCount` (the old analysis's count) is deleted | 2.1 |
| H0b-5 | `Project.cs` needs **no edit**: `:550-554` already reads `Analyze`, `ReportAnalysis` and `Accepts`, which decide by MIR from H5 | 2.1 |
| H0b-6 | Generation's gate step 1 becomes `!Accepts` alone; gate step 2's refusal (`FunctionCodegen.cs:90-101`, PD12, R5-M96) becomes one contract: an `Incomplete` body implies a published Error (R4-M69), so one reaching generation is `InternalInvariant_Kd`; the `Group == -1` invariant (`FunctionCodegen.Drops.cs:25-28`) stays (review N2). The contract text and its lowest-layer test land first as unit IC, R4's bp dev unit after R4's exit and before H3b; R5's Backend check stays in H5 (answer R4-7) | 2.1 |
| H0b-7 | H5 also deletes what the flip leaves without a caller: both old `ReportDiagnostics` (`OwnershipAnalysis.cs:140, 220-456`, `ControlFlowAnalysis.cs:114-127`) and `Binding.FallthroughKey`; their readers outside tests are only `ReportAnalysis` (ruling R4); the one test reader, `T/OptionalTryDiscardTest.cs:246` (`AnalyzeControlFlow().ReportDiagnostics()`), is re-pointed in H5's test hunk (integration) | 2.2 |
| H0b-8 | **H4 is test-level** (recommended (A)): A4's summary of a row operation's own body replaces `SummarizePublishedOperation`, which has no production caller today; no production record; `Row: None` with tests on the four Array compiler functions and the Kimi-bodied `insert`, `appendAll`, `appendCopies` and four new `KimiDeclarationId` members, `remove`, `swap`, `swapRemove`, `reverse` (ruling on R4 review N7, R4 r2 N2); the other SPEC 4.9.6 "None" families stay body-composed, outside H4 by design (R4 r2 N2); no premise mode in A4: the test reads A4's items (R4 review N6); accepted by R4 (answer R4-8); H4 deletes `Binding.PublishedOperations.cs` before H5 | 4 |
| H0b-9 | Five classes under Q13 (§5.3), one row per signature, pre-classified by a fixed map from ledger kind and category and from snapshot kind; the Session, the snapshot and the milestones decide, the ledger only proposes | 5 |
| H0b-10 | Stop rule: any class 5 row stops H5; class 2 rows block H5 until fixed on dev or accepted by the owner as one list (PD27's form); a second rehearsal round finding a new row of a family already fixed goes one level higher (AGENTS), two on one family to the owner (plan §5.2) | 5.5 |
| H0b-11 | H6 (hover from MIR) is **part of H5** (review B3, superseding ruling R3's "right after H5"): from H5 nothing in the editor runs the old analysis | 2.1 |
| H0b-12 | Rollback window: a revert of the group's merge alone until H10a lands; H10a, P42's remainder, H7, H8b and H8c wait for the post-merge sweep on dev (G3's model; review N1) | 2.5 |
| H0b-13 | **H1b** (BU, in the group, an H5 prerequisite): Binding's verdict at Final, or an `InternalInvariant_Kd` contract, for every obligation the old reporter gives `UnprovenConstraint_Kd`, and a successor for each old CFA pending site (review B2); census first, a located `Unsupported_Kd` where a kind cannot be judged yet, `InternalInvariant_Kd` only where shown unreachable with a reproducer-backed argument (answer BU-5) | 3 |
| H0b-14 | H3b's table carries each row's class and its exit-gate category (a)–(e) by a fixed map (review N4) | 5.3 |

## 1. State on dev (facts)

| Item | State | Evidence |
| --- | --- | --- |
| Acceptance | `Accepts` = Binding complete, startup complete, `Analysis.IsVerified`, no Error through the Ownership partition; `Analysis` = old `Ownership.Result.IsVerified` and no MIR record | `C/Core/Compilation.Generation.cs:46-47`, `C/Core/Compilation.Pipeline.cs:35-43` |
| Old verdict | `verified` = no CFA issue, `Binding.LintErrors == 0`, no CFA `PendingBinding`, no issue (`OwnershipAnalysis.cs:127`); it invalidates the ControlFlow and Ownership partitions itself (`:68-69`); returns early on an incomplete Binding, a stop or an unproven Origin obligation (`:86-104`) | `C/Analysis/OwnershipAnalysis.cs` |
| Publication | `ReportAnalysis` = old CFA's records, the MIR records (location-only, ControlFlow), the old analysis's records (`Compilation.Pipeline.cs:82-94`) | — |
| `Project.cs` | `compilation.Analyze(); compilation.ReportAnalysis(); var accepted = compilation.Accepts;` (`src/Kimi/SolutionAndProject/Project.cs:550-554`; the plan's `:510`) | — |
| PD12 gate | Step 1: `MirRecords(null) > 0 \|\| !Accepts` (`Compilation.Generation.cs:66`); step 2: an `Incomplete` body is refused with its A1/A2 code, `Unsupported_Kd` after an arena skip (`C/Backend/Codegen/FunctionCodegen.cs:90-101`); `MirDropTable.Incomplete`'s only production reader is that line | `C/Mir/Analysis/MirDrops.cs:99` |
| Binding's effect checks | At bind time: `ValidateEffectBounds` (`Binding.cs:511`) and `ValidateVirtualEffectBounds` (`:523`) fail the conformance path or the virtual function (Binding partition, `IncompatibleImplementation` or `Unsupported` at a limit); after the old analysis: `ValidateDestructionEffects`, `ValidateCallableEffects`, `ValidateVirtualDestructionEffects` via `OwnershipAnalysis.EffectBounds.cs:44-65`; records "at the violating effect" (`Binding.cs:805`, `:842`; `Binding.EffectViolations.cs:68`) | — |
| A4's twins | `EffectObligations` rows (`Binding.EffectObligations.cs:25-83`) at the `Self is C` clause, the virtual function or the Callable use, requirements already `Ownership.EffectBound`/`VirtualEffectBound`/`CallableEffectBound`; A4 adds them `NoBody` (`MirEffects.Solve.cs:102-119`) | — |
| `SummarizePublishedOperation` | No production caller; two test callers (`T/EffectBoundImplementationTest.cs:172`, 13 operations; `.PublishedRows.cs:26`, `truncate`/`removeAll`) | `C/Binding/Binding.PublishedOperations.cs` (48) |
| Array `append`/`insertAt`/`pop`/`removeAt` | Compiler functions without a `Row` (`KimiLibraryCatalog.Definitions.cs:78-81`); A4 classifies them bodyless `Inputs` (`TyCtxt.Effects.cs:16-22`, `MirEffects.cs:118-124`), the same "None" SPEC 4.9.6 gives | — |
| TransferRequired | 10 old report sites (`OwnershipAnalysis.cs:841, 1222`, `.Borrows.cs:501`, `.Closures.cs:61`, `.Elements.cs:263`, `.Objects.cs:28`, `.Pointers.cs:91, 306`, `.Sequences.cs:66, 110`; Q8 §3.7's lines moved); Binding's 7 existing failures | grep |
| H1's inputs | H1a (eager Final publication), Q8-6 (one Semantics-case renderer) and Q8-3 (the validator's Copy rule) are **not on dev**; Q21 landed (R5-M63); G:L (Q9–Q12) landed; **no Q14 evidence** (`artifacts/verify/r6-q14/` absent) and no Q19 reading | grep, `artifacts/verify` |
| OldRule | 25 `[Trait("OldRule", …)]` in 13 test files (plan: 19); ratchet `ArchitectureRulesTest.cs:80` (7 without a target side, 0 by R4's exit, A5 plan N8) | grep |
| SPEC-example and pair tables | `spec-cases.json` 66 cases, 96 variants, 34 `Old` sides (plan: 22), 14 `Mir`; `pair-cases.json` 1 `Old`; `VerifiedSpecExampleTest.Landed` lacks F7, F8 and F10 (`Families` has no A2; F6 is retired, R4-M106; R4's exit adds them through 2c-7, 2d-10, 2x-T7: answer R4-9) | `T/VerifiedSpecExampleTest.cs:21, 73-78` |
| Milestones | `tests/milestones/stage-baselines.json`: 39, 41, 43 rejected at Binding, 42 at Ownership (`Unsupported_Kd` at `var count: i32 = Registry.initialCount()`) | — |
| Ledger (latest local run, `artifacts/verify/mir-diff/bu85-u2`) | 258 signatures: acceptance-old 38, acceptance-mir 54, missing-old 56, new-mir 76, location 23, requirement 11; the brief's current figure is 266 signatures, Open (b) 150 (1,399 rows) | `ledger.tsv` |

## 2. H5: the flip, one commit with the old effect checks deleted (PD29)

### 2.1 The exact change in Core

`C/Core/Compilation.Pipeline.cs` (162 lines today), after H3 (H0a: `Compilation.Publication.cs` holds the publisher (review B1), keys, the
fact table and the cause resolver; `MirRecords` reads the class rows):

| Member | Today | From H5 |
| --- | --- | --- |
| `Analyze` (`:70-76`) | `BuildMir`, `Ownership.Analyze()`, `Analyzed`, `Analysis` | `Diagnostics.Invalidate(ControlFlow)`, `Invalidate(Ownership)` (moved from `OwnershipAnalysis.cs:68-69`), `BuildMir`, `Analyzed` (removal H10a), `Analysis`; no `Ownership.Analyze()` (review B3) |
| `Analysis` (`:35-43`) | old `IsVerified` and no MIR record; `BodyCount` the old analysis's | `new(this.mir is not null && errors == 0, errors)` where `errors` counts the published rows of Error severity (Warnings and the derived records' suppression as H3's publisher defines); after `InvalidateOwnership` (`mir == null`) not verified |
| `Problems` (`:51-65`) | old issues, then MIR records | the publisher's rows, in its order (H0a-1) |
| `ReportAnalysis` (`:82-94`) | old CFA, MIR records, old analysis | the publisher's rows only (the old CFA's `:84` and the old analysis's `:93` stop, ruling R2); the table collapses to "every record" (H0a §2's removal condition) |
| `AnalysisResult` (`:157`) | `(IsVerified, BodyCount, ErrorCount)` | `(IsVerified, ErrorCount)`; readers of `BodyCount`: `T/OwnershipAnalysisTest.cs:190, 209` (deleted asserts; the class goes at H8c), `T/ProductTestMembershipTest.cs:23` (re-pointed: the MIR program has no `Test` body), `T/SemanticSnapshotTest.cs:253` (printed; the snapshot is re-baselined) |
| File contract (`:3-9`, `:16-19`) | "Until R6 the legacy path decides" | the MIR decides; the transitional entry and the event with their removal units |

- **No `LintErrors` term (H0b-4).** Lint records are Binding's non-failing Errors (PD31) and `Accepts` already rejects through the
  Binding partition's error state. A second read in the analysis verdict is a second copy of Binding's decision and keeps
  `Binding.LintErrors` (`Binding.Lints.cs:81`, Transitional "H10") alive past its stage. Effect: a test reading `c.Analyze().IsVerified`
  on a lint-error program without publication sees `true`; H3b lists these as class 3 test re-points (check's verdict is unchanged).
- **`PendingBinding` term and the `Unresolved` fallback (review B2).** Not dropped on an assumption: H1b's census gives each of the old
  CFA's 9 pending sites (re-review 2) a successor (a Binding failure, a MIR construction record or an `InternalInvariant_Kd` contract) before H5.
- `C/Core/Compilation.Generation.cs`: gate step 1 → `if (!this.Accepts) return false;` (every MIR record now fails `Analysis`); the
  contract lines `:6-8`, `:14-20` lose step 2. `FunctionCodegen.cs:90-101`'s refusal goes; in its place one contract (review N2): an
  `Incomplete` body implies a published Error, its own or, for an inline copy (index −1), the template's (R4-M69), so an `Incomplete`
  body reaching generation is `InternalInvariant_Kd`; the `Group == -1` check stays and catches only part of it. The contract text and its
  lowest-layer test (every `Incomplete` body has a kept Error problem, or its template's of the same code) land earlier as R4's unit IC
  (bp, after R4's exit, before H3b); H5 adds only R5's Backend check (answer R4-7).
- **One transitional entry for the old analysis (review B3).** `Analyze` no longer runs it: frozen code throws on shapes the MIR
  accepts (`OwnershipAnalysis.Enums.cs:282`, `:306`, `.Match.cs:19`, `:337`, `.Updates.cs:55`), so a shadow in every check and in the
  editor service would fault checks the MIR accepts. `Compilation.AnalyzeLegacyOwnership()` (`[Transitional("R6-check")]`, removal
  H10b) runs it once per version and returns it; its readers are the tests (46 `Ownership.Bodies/Issues/Result` reads in 26 files,
  through their helpers), the ledger's old side until H10a and the benchmarks' 13 `Ownership.Analyze()` calls until H9.
  H5 makes `Compilation.Ownership` private, so the entry's returned object is the only path and no test read passes without the
  analysis having run (re-review 3); its readers are re-pointed in H5 and migrated off by H8a–c and H9. `AnalyzeControlFlow()` is
  already an explicit test-only entry (H8b).
- `C/Core/Compilation.Switch.cs` contract: unchanged by H3 (review B1); the switch selects the ledger, the mark and the manifest entry.
- `Project.cs`: none (H0b-5). `src/Kimi/Testing/TestCatalog.cs:82` reads `Analysis.IsVerified` as before.
- Hover (H6, review B3): `HoverBuilder.Virtual.cs:75` reads "completed" when the function's MIR body has no problem in `MirReport`,
  else "pending" (today it calls `Ownership.TemplateBody`, which would run the old analysis in the editor); `T/VirtualHoverTest.cs:54`.

### 2.2 Deletion list, checked against the base

| Item (plan row H5) | Present? | Site, size |
| --- | --- | --- |
| `EffectSummary` (plan ≈ 1,985) | yes, smaller after R4-M106 | `C/Binding/Binding.EffectBounds.cs` whole file, 1,421 (`EffectSummary` `:123-1398`, `CallInstanceComparer`, `ValidateEffectBounds`, `ValidateDestructionEffects`, `IsEffectLimit`, `EffectBoundViolation`); `CollectCallableEffectUses()` moves to `Binding.cs:511`'s place (the obligations read its uses) |
| `CollectCaseDestructions` and callers | yes | `Binding.EffectBounds.cs:17-18`; `OwnershipAnalysis.Cases.cs:140-141, 155-156` (with `AppendCleanupCase`) |
| Callable and virtual checking parts | yes | `Binding.CallableEffects.cs:10, 12, 67-118, 225-248` (≈ −80; `CollectCallableEffectUses`, `AvailableCallableEffects`, premise failures stay); `Binding.VirtualEffects.cs` whole, 78; `Binding.EffectViolations.cs` whole, 126; `Binding.cs:523`, `:803-806`, `:842` (the effect alternative); `Binding.VirtualDeclarations.cs:82` |
| Old `ValidateEffectBounds` | yes | `C/Analysis/OwnershipAnalysis.EffectBounds.cs` whole, 66; call `OwnershipAnalysis.cs:121`; report branches `:295-309` |
| `IsEffectRecord` + ledger filter | yes | `Binding.EffectObligations.cs:114-120`; `tests/xUnitTest/MirDifferenceLedger.ComparedSet.cs:21, 160` (the plan's `MirDifferenceLedger.cs:44, 279` moved) |
| Violation-table reads in `EffectObligations` (R4-M23) | yes | `Binding.EffectObligations.cs:31-32`, `:63-66`: the obligation follows the path's validity and `BindingFailure` alone |
| R4-M28 twins, `CompilerFunctionEffect.Formatting` | twins inside `EffectSummary` (`ArrayClear`/`Replace` destruction `:744-762`, formatting `:765-776`); `Formatting` member `KimiLibraryKinds.cs:197, 262` | the member goes; A4 reads the rows (`Formats`, `Target`, `Replace`); a vocabulary removal, no owner item (no entry added) |
| `OwnershipBody.CleanupEffects.cs` | yes | whole, 95 |
| `SummarizePublishedOperation` | yes | gone with H4 before H5 (§4) |
| Old `ReportDiagnostics` (H0b-7, ruling R4) | not in the plan row | `OwnershipAnalysis.cs:140, 220-456` (≈ 238, incl. the derived `Unresolved` fallback `:337-341`), `ControlFlowAnalysis.cs:114-127`; `Binding.FallthroughKey` (`Binding.Lints.cs:134-150`) |
| PD12's refusal | yes | §2.1 |
| `VerifiedSpecExampleTest` MIR-only | — | `:73-78` asserts the published rows against `Mir ?? Expected`; 34 `Old` sides (`spec-cases.json`) and 1 (`pair-cases.json`) deleted; `Landed` names every family |
| OldRule | 25 traits, 13 files | traits and old-side assertions deleted; `MirTargetSide`'s ledger-key half (`tests/xUnitTest/MirTargetSide.cs:24-40` keys, `:44`) deleted, its MIR check is the test's assertion; ratchet row `ArchitectureRulesTest.cs:74-80` deleted |
| `stage-baselines.json`, `MilestoneSourcesTest` | — | re-baselined only where H3b's run shows a changed first diagnostic (each named) |
| STATUS's pipeline lag | `STATUS.md:5` and 16 other lines naming "default pipeline"/"old analysis" | the rows whose verdict H3b's table changes; the rest at H15 |
| DIAGNOSTICS | `:107`, `:152`, `:166` (H1, Q8 §3.7); effect records' place (old: at the violating effect; A4: at the obligation subject) | Auto rows |

Size (H): source ≈ −2,350/+90 (Binding −1,830, Analysis −380 (R5's, review N3), Backend −12/+10 (N2's check), Core +40/−25, hover
±20, Mir 0), tests ≈ −250/+210 (the readers' entry calls, review B3), JSON −35 entries. Plan: ±300, ≈ −2,700.

### 2.3 What H5 publishes on every pipeline (H0a §2 at H5; review B1)

Every row publishes: construction and collector records, validator findings, A3 (`RequireFallthrough_Kd`, the old CFA's twin no
longer), A1, A2, A5, `MirReport`'s disagreement record, **A4's problems and H4's records** (H4 has none under H0b-8), C1-F4 findings,
derived records as `PrerequisiteUnavailable_Kd` (H0a-8). No old analysis issue, no old CFA issue, no Binding effect record. Binding's
records (H1's `Binding.TransferRequired` and H1b's included) and the lint records publish through Binding as before. H3's selection of
today's rows (H0a-1) goes in H5 (its removal condition).

Successors of each old report (review B2): the old CFA's `RequireFallthrough_Kd` → A3 (`C/Mir/Analysis/MirLints.cs:40`; its
guessed-recovery derived form needs 2g-1's Binding cause, H0a §6); `ReportUnprovenOriginObligations`' relation branch
(`UnsatisfiedOriginRelation_Kd`, `UnprovenOriginRelation_Kd`) → A5's Origin stratum and Binding's `OriginRelation` twins; its
non-relation branch (`UnprovenConstraint_Kd`) → H1b (Binding at Final); the derived `Unresolved` fallback and the `PendingBinding` term
→ H1b's census of the 9 pending sites (re-review 2); the effect reports (`EffectBound`, `CallableEffectBound`, `VirtualEffectBound`, effect
`Unsupported`) → A4, published from H5; `TransferRequired` → H1 (Binding); the old `DeferredExpansionLimit_Kd` (`OwnershipModel.cs:198`, the deferred-cleanup
limit `DeferredOperationLimit` = 8,192, `OwnershipAnalysis.cs:11`) → none: the builder's `DefaultExpansionLimit_Kd`
(`MirBuilder.Defaults.cs:16`, `:133`) is the inline-default limit, another bound, so programs past the old limit are class 1a
moves listed for the owner under O-1 (re-review 1); every other `OwnershipRequirement` → its A1/A2/A5 code in the
ledger's compared set (`MirDifferenceLedger.ComparedSet.cs`), held by R4's exit gate. MIR reads no `Binding.Obligations` (grep).

### 2.4 `KIMI_PIPELINE` from H5 to H12

| Span | `legacy` (default) | `mir` |
| --- | --- | --- |
| H5 → H10a | MIR decides and publishes; the old analysis runs only when a test or benchmark reader calls `Compilation.AnalyzeLegacyOwnership()` (review B3) | the same, plus the ledger's observation (`Analyzed`), the `[mir]` mark, the manifest entry (`TestCatalog.cs:122`); no gate: VERIFICATION `:94` says a `-Pipeline mir` run is not required after H5 |
| H10a → H12 | as above, no event | the mark and the manifest entry only |
| H12 | switch removed (H0c §4.2) | — |

### 2.5 Rollback

The group merges as one merge commit. While the old Analysis exists and nothing depends on the flip, the rollback is `git revert -m 1`
of that merge (restoring old acceptance, Binding's effect checks and the old TransferRequired sites together, PD29). H10a, H8a–c and
H10b build on H5, so H10a starts only after the post-merge sweep on dev passes (`'*.ll'` O0/O2, 40 milestones, CLI harnesses,
snapshot), as G4 waited for G3's; P42's remainder, H7, H8b and H8c (whose prerequisite is only H5) wait for the same sweep (review N1).
After H10a a rollback also reverts H10a; after H10b fixes go forward. H6 is part of H5 (review B3), so it reverts with the merge.

## 3. H1: TransferRequired judged in Binding (BU; G:R6-check-flip)

What H1 must deliver for H5 (Q8 §3.7 governs; its design is owner-approved, R5-M23):

| Deliverable | Before/in H1 | Note |
| --- | --- | --- |
| H1a: eager Final publication, `Check` split (bp, D+R) | on dev before the group | "any time after G2" (R5-M152); `HirColumnsTest.PublishIsInvisible` re-pointed |
| Q8-6: one Semantics-case renderer in Binding (bp) | on dev before H3 (H0a BU-1) | the old analysis calls it until its `ReportDiagnostics` goes (H5, H0b-7; Q8 said H11) |
| Q14 probe (`artifacts/verify/r6-q14/`) | before H1's code | not run yet; its (a) rows are pre-named class 1 rows of H3b (§5.4), its (b) column defects are fixed first |
| S1–S3 scan, `DefaultArgumentMove_Kd` for PreparedParam roots, S3's omitted-list record, republication assertion | H1 | Q8 §3.7 |
| Delete the 10 old sites and `OwnershipRequirement.TransferRequired` | H1 | `Ownership.TransferRequired` leaves Q7's enum (R5-M24); later Kind values shift, only names publish |
| Validator rule (Q8-3, `MirValidator`) | second commit of the group | Q8 §3.9; Q14 runs it in shadow first |
| Regression tests p1–p4, p7, p8, u2, u3, t01–t03, s01, s03 | H1 | lowest layer: Binding records |

Why in the group with H5 (PD29): a Binding node failure makes the old analysis return early (`OwnershipAnalysis.cs:86-89`), so H1
without the flip loses independent records (SPEC 23.1.2). Review: one reviewer, not BU's implementer, dispatched by BU (Q13: one
review), the final review of Q8's design being the group review (R5-M23); BU also reviews H5's Binding hunk (§2.2's Binding rows).

**H1b (review B2; BU; G:R6-check-flip, an H5 prerequisite).** The old `ReportUnprovenOriginObligations` reports, through its
non-relation branch, `UnprovenConstraint_Kd` (`OwnershipModel.cs:205-207`, `OwnershipAnalysis.Borrows.cs:161-162`) for every
obligation still unchecked that is not an `OriginOutlives` with both ends: an unchecked `OriginInference`, an unverified length
obligation (`TypeFormation` with a `Length`, `Binding.Lengths.cs:423`) and any other `BindingObligationKind` (`TypeRole`, `Loan`,
`StaticStorage`) not verified, discharged by `CallBorrows` or judged (`:72-94`). Nothing in Mir, Backend or Core produces that code;
the ledger credits it to A5 (`MirDifferenceLedger.ComparedSet.cs:51`), which never reports it; R4-M112's departure keeps that row and
names no successor. H1b gives each such kind Binding's verdict at Final (the same code, Binding partition, at the obligation's use),
or an `InternalInvariant_Kd` contract check at Final where it shows the kind cannot stay unchecked. It also censuses the old CFA's 9
`pending.Add` sites (`ControlFlowAnalysis.cs:471-864` and `:951` in `Finish`, re-review 2), the input of the old verdict's `PendingBinding` term and of the derived
`Unresolved` fallback (`OwnershipAnalysis.cs:337-341`): each gets a Binding failure, a MIR construction record or an
`InternalInvariant_Kd` contract (Binding complete implies no pending node). In the group for H1's reason. Size (H) +60, t +60.
BU's answer (answer BU-5): census first; a kind that cannot be judged yet gets a located `Unsupported_Kd`; `InternalInvariant_Kd` only
where a kind is shown unreachable with a reproducer-backed argument.

## 4. H4: A4-based verification of published rows (R4; before H5)

- **(A) Adopted (H0b-8; answer R4-8): a test-level successor.** The test reads A4's existing per-body summary and items of each row
  operation's own body (steps 2–3 build them for every library body) and compares each item with the row's `Runs`, `Destroys` and
  `Calls` (the inputs `SummarizePublishedOperation` reads, `Binding.PublishedOperations.cs:20-46`). No premise mode is added to A4 (a
  mode steering a function); if A4 cannot attribute an Unknown class to its source call, H4's D+R decides the read accessor, production
  unchanged (R4 review N6); `T/MirEffectsTest` asserts every row operation with a Kimi body (13 today, plus `truncate` and
  `removeAll`) stays within its row; `EffectBoundImplementationTest`'s two methods move there; `Binding.PublishedOperations.cs` (48)
  is deleted. Reason: the bodies are the compiler's library (SPEC 4.9.6: "the library implementation must stay within it"), no user
  program can produce the record, and today's check is test-only too; a production check would add A4 work on every check for a
  compiler-owned invariant. Departs from the plan row's "its records publish from H5" (nothing to publish).
- (B) A production judgment: each reached row operation's body judged in A4's step 3, `InternalInvariant_Kd` at the operation on a
  violation. Rejected: cost on every check, and a published record no valid input can reach.
- **"None" rows** (ruling on R4 review N7, R4 r2 N2): `Row: None` on `ArrayAppend`, `ArrayInsert`, `ArrayPop`, `ArrayRemove`
  (compiler functions; A4 already gives them "None" through `Bodyless` → `Inputs`) and on the Kimi-bodied `insert`
  (`ArrayInsertPosition`), `appendAll` and `appendCopies` (`Definitions.cs:232-239`, no row today, so A4 composes their bodies). H4 adds
  four `KimiDeclarationId` members for Array `remove`, `swap`, `swapRemove` and `reverse`, recorded in H4's DECISIONS entry, with
  `KimiLibraryShapeTest` run; the test checks each Kimi-bodied one.
- **Boundary (decision, R4 r2 N2).** The other SPEC 4.9.6 "None" families (Array, fixed-array and Slice access, view, position,
  metadata and capacity operations, whose Kimi-bodied members such as `first`, `last`, `slice` and `iterate` stay body-composed) are
  outside H4: A4 verifies a body-composed member through its body, and a catalog row is needed only where a member is a published row.
  No open issue. For the four, the row makes SPEC 4.9.6's table the catalog's one answer, so when R8 gives them Kimi bodies A4 composes
  the row, never the body; for the seven, A4 stops composing the body and reads the row. No verdict changes (H: the bodies stay within
  "None", which the test checks). Rejected: deferring to R8 (the R8 body would be composed silently); a row for every body-composed member
  (no published row needs it, R4 r2 N2).
- When: after R4's exit, before the group's rehearsal; independent of H3 under (A). Review: one R review (D+R under H0); BU reviews
  its Binding edits (the catalog rows, deleting `Binding.PublishedOperations.cs`) (review N3; answer BU-3). Size ≈ +70/−50, t ±90 (H, the
  wider row scope).

## 5. H3b: the rehearsal and its classification

### 5.1 The rehearsal (H0b-2)

Branch `claude/r6-flip` from dev with R4's exit, H3 (H3-1, H3-2), H4, H1a and Q8-6; it carries H1, H1b (review B2), Q8-3 and H5 as implemented for
landing. Round n runs, on that head and on its dev base (the base's runs reuse `artifacts/verify/shared-base/<sha9>/`):

| Run | Head | Base |
| --- | --- | --- |
| Default Session with `-DiagnosticSnapshot -DiagnosticBaseline <base>/diagnostic-snapshot.json -DiagnosticAllowed <every kind>` (so natives still run), `'*.ll'` at O0 and O2 (registered heavy run) | yes | snapshot, fixtures |
| 40 harnessed milestones; the five CLI harnesses | yes | — |
| `compare-fixtures.ps1`: `.ll` identical for every fixture both accept | yes | yes |
| The ledger's last run: `-Pipeline mir -TestPurpose Functional` with `KIMI_MIR_DIFF` | — | yes (round 1 only; the head has no ledger effect filter) |

The rehearsal is evidence (Evid); its table is the Doc `draft/Sketches/R6-check/H3b-classification.md` (rows) with the raw lists under
`artifacts/verify/r6-h3b/<round>/`. Rejected: a throwaway prototype (the flip written twice, the second copy diverging); classifying
from the ledger alone (it compares A-owned records only: not Binding's effect records, H1's move, Notes, evidence, repairs, milestones
or native results).

### 5.2 Inputs, one row each

1. Each snapshot difference (kinds: case, acceptance, code, severity, attribution, location, text, repair, order; DIAGNOSTICS §9.1),
   grouped by (kind, code before, code after, requirement before/after, form).
2. Each failing test of the head's Session, grouped by the expectation it pins (acceptance, record, count).
3. Each remaining ledger signature (section, kind, side, analysis, code, requirement, form; today 266, Open (b) 150 with 1,399 rows).
4. Each milestone or fixture whose verdict, first diagnostic or native result changed.

### 5.3 The classes (DECISIONS Q13; WORKFLOW "Diagnostics scope")

| Class | Meaning | Q13 standing | Action |
| --- | --- | --- | --- |
| 1 Named move | 1r: an invalid program the base accepted is rejected (an old unsound acceptance closes, like G177); 1a: a valid program the base rejected is accepted (an old false rejection or old-only `Unsupported_Kd` closes); 1x: an old record SPEC does not require disappears from a still-rejected program. Each names its PLAN row or ledger reference and SPEC clause | not a regression once named (Q13 (1) is an *unnamed* move) | none; H5's commit and PLAN name it |
| 2 MIR false rejection | a valid program the base accepted is rejected, or gains a Language error (incl. a located `Unsupported_Kd`, a (e) form) | regression (1) or (2) | R4 (MIR cause) or BU (Binding cause) fixes on dev before H5, or the owner accepts it explicitly, one list (PD27) |
| 3 Record moved | the failure is still reported on the same verdict: location, code, requirement/partition, Note, evidence, repair, case or order change; a test re-pointed for it | none (dependent or independent, rule W mark recorded) | counted by kind and code |
| 4 Counted record | on a program both sides reject: a record added (not `InternalInvariant_Kd`), or removed by rule W | dependent, counted by code (Q13) | counted |
| 5 Stop | anything else: a new `InternalInvariant_Kd`; a lost independent record (not rule W, not 1x); a severity change; an acceptance move with no clause; a library or milestone rejection; a changed native result or `.ll` on a program both accept | — | stops H5 (§5.5) |

Classes and the exit gate's categories (plan §5.5 item 1; PLAN10's (a) old wrong, (b) new wrong, (c) SPEC unclear, (d) intended,
(e) unported under D2; review N4): 1r, 1a, 1x → (a) (or (d) where a decision intends it, with its clause and Appendix B number);
2 → (b), fixed before H5, or (e) when the owner accepts an unported form (STATUS and PLAN); 3, 4 → (d) (rule W, SPEC 23.1.2); 5 → (b)
until fixed; any row whose verdict rests on a SPEC ambiguity → (c), the owner's. The table carries both the class and the category.

### 5.4 Pre-classification maps

Ledger rows (a proposal; the head's run decides each row's verdict):

| Ledger kind/side | (a) | (b) | (d) | (e) |
| --- | --- | --- | --- | --- |
| acceptance/old (old rejects alone) | 1a | 5 if the head accepts (unsound); 3/4 if still rejected | 1a | — |
| acceptance/mir (MIR rejects alone) | 1r | 2 | 1r | 2 (owner list) |
| missing/old | 1x | 3 if a MIR record of another code is in the body (code choice), 4 if rule W, else 5 | 1x | — |
| new/mir | 4 (1r on a base-accepted program) | 4 on a rejected program, 2 on an accepted one; 5 if `InternalInvariant_Kd` | 4 | 2 |
| location/both, requirement/both | 3 | 3 | 3 | — |

Snapshot kinds: acceptance → 1 or 2 (or 5); code, location, attribution, text, repair, order, case → 3; a record only in the head → 4 on
a base-rejected program; severity → 5.

Known rows, named in advance (H where unverified):

| Source | Class | Note |
| --- | --- | --- |
| Binding effect records → A4's (Binding partition at the violating effect → Ownership partition at the obligation subject) | 3 | the q5 deep-dive's "partition move"; A4's records carry no facts, so "the violating effect" is lost too (R4 review N2, H0a §4.1) |
| A5's records at Binding's twin rows; G353's refused stored Type (m1, v1, v2) | 4 | counted; twins to H11 (R4-M108's H11 list, H0c), G353 until R4's small A5 item after BU-73 (answer R4-4) |
| Programs Binding's effect failure left incomplete regain MIR records | 4 | SPEC 23.1.2's gain; counted |
| H1: `Ownership.TransferRequired` → `Binding.TransferRequired`; repair `s` → `(s)` | 3 | Q8 §3.7 |
| H1: the old cascade after it (`MovedPlace_Kd` at a later use, probe p1) | 4 | rule W removal |
| H1: Q14's (a) old misses (S2 unconsumed Places, S3 inferred captures) | 1r | SPEC 3.5, 7.7.2; each Q14 row named |
| G165, G177, G211, G217, G268, G299 (default unsound acceptances the MIR rejects) | 1r | PLAN rows; clauses 15.6.2, 3.6, 15.6, 15.6.7, 15.6, 15.6 |
| G124, G228, G313, G160's default verdicts, G186's old walk | 1 or 3 | per row at the run (H0c §5.3) |
| G218, G291 (old false errors; A5 accepts) | 1a (H) | SPEC 3.4.3; 7.5, 3.5 |
| G121's lost `do` fallthrough record (2x-P10) | 5, named | accepted until G121's construct design (the owner, 2026-11-02, verbatim 「2つとも、推奨どおりでお願いいたす」 (R4's dialog; PLAN d2ee0e158); answer R4-13; R4-M88) |
| Milestone 42's first diagnostic (old analysis's `Unsupported_Kd`) | 3 if MIR's first record differs; 2 never (it stays rejected, P42) | re-baseline named |

### 5.5 Who classifies, the table's form and the stop rule

- **R5** runs the rounds, groups the inputs into signatures, applies §5.4's maps and assigns each remaining row an owner; **R4** confirms
  every MIR-cause row (class 2 causes, 5's lost records, 1r/1a rows whose verdict is A1–A6's) and fixes its class 2 rows; **BU** the
  Binding-cause rows (H1's, H1b's, the effect-record move, BU-10/BU-14's guards, A3's gates); **the owner** class 2 acceptances, the 1a
  list, soundness questions and (c) rows (Q13); the 1r list is the planning session's, reported to the owner for information (O-1,
  recommended). R4 and BU answer within their next session; an unanswered row stays 5.
- Table: one row per signature: `| # | input (snapshot kind / test / ledger signature / milestone) | signature | rows (programs) | example
  (sha or test) | class | clause | named row (PLAN G, ledger reference, Q14 row) | rule W (dep/indep/-) | owner | action |`; a summary
  counts rows per class and per code; class 3 and 4 counts go into H5's commit message (Q13 "counted by diagnostic code").
- **Stop rule.** A class 5 row stops H5: no merge until its owner's fix lands on dev or the owner decides. Class 2 rows block H5 until
  fixed or accepted as one owner list. Each fix is a dev unit; the branch is rebased and the round re-run. A new row of a family a fix
  already closed means the cause was missed: analyze one level higher (AGENTS); two review rounds on one family go to the owner, and
  two working sessions on R6-check that land no unit too (plan §5.2). Each round is one Q12 hour of classification work; more rows than
  an hour covers split by input kind.

## 6. The D5 exceptions that end at R6-check (H0c §5.2): consequences at H5

| Exception | DEC | Consequence at H5 | Class |
| --- | --- | --- | --- |
| PR-1 place result removal | PR-1 | none (code, H10b/H11) | — |
| G147 (2b) whitelist and guard `Unsupported_Kd` | BU-10 | guarded forms take MIR's verdict: accepted → 1a; a MIR Language record → 3 (code change) | 1a / 3 |
| 2x-E7 pair-layer Pattern guard | BU-14 | as BU-10 | 1a / 3 |
| G140 performance memos; Q-O3 views; G86 `RequirementAt` | BU-19, BU-24, BU-44 | none (code at H10b, identical verdicts) | — |
| A5-F7 2c-5 receiver-dependent gate stops the whole analysis | R4-M126, R4-M152 | stopped programs regain their records (4); a valid program with only the stop is accepted (1a, R4-M152) | 1a / 3 / 4 |
| G273 stop at `SupportsType`'s refusal | R4-M159 | the MIR accepts these forms (PLAN `:36`): 1a for a16, a18_raw_input_store, a21, i6, i7, the v60 family | 1a |
| Abstract Calls Reach by Paths U1, U2 | R4-M106 | only where the ledger shows a difference | per row |
| g2never element forms, `AcquireElement` | R5-M159, R5-M160 | per ledger row; MIR's element rules decide | per row |

## 7. The window plan for H5

- **Inputs.** dev at R4's exit (every A5 family, A6 2g-D–2g-7, the exit's end-to-end review; R4-M35), with H3, H4, H1a, Q8-6, Q19's second reading (review N5) and
  the fixes of the last rehearsal round; the group branch rebased on it; the classification table with no class 2 or 5 row open.
- **Checks** (one Session = the evidence; plan §5.5 item 1): default Session with every native fixture (`'*.ll'` O0/O2), the 40
  harnessed milestones, the five CLI harnesses, the snapshot against the base classified (H3b's table, final round), the ledger's last
  run on the base (round 1's, or the latest base if inputs changed), `KimiLibraryShapeTest`, `Purpose=Allocation`; `verify-commit.ps1`
  per commit; the semantic snapshot re-baselined with the reason "the flip, H5".
- **Peers' freeze.** From the final round's Session to the merge, R4 and BU push no input-changing commit to dev (code, DECISIONS,
  ARCH; PLAN and draft pushes are fine); the window is announced in PLAN. R4 holds `Mir/**` edits; BU `Binding/**`.
- **Review.** The design: FP under H0 (two designs, design review, the owner's one look, OQ11). The implementation: one group review of
  G:R6-check-flip (Q13: one review) by a reviewer who wrote none of it, then the merge; the final review at H15 covers it again.
- **"H5 landed"** in PLAN means: dev contains the group's merge; the PLAN position line says "MIR decides acceptance (H5, dev `<sha>`);
  the old analysis runs only for its test and benchmark readers until H10b; `KIMI_PIPELINE` selects only the ledger and the mark"; the
  rows of §5.4's class 1 close or move with H5's commit; STATUS rows updated. Held until the post-merge sweep passes (review N1):
  P42's remainder (R5-M13), H7, H8b, H8c and H10a; then the order of H0c D-c3.

## 8. Unit table

| Unit | Owner | Files | Size (H) | Review | Prerequisites |
| --- | --- | --- | --- | --- | --- |
| H1a (bp) | BU | `C/Binding/Binding.Check.cs` (new, from `Binding.cs:1006-…`), `Binding.Hir.cs` | ±70 | D+R (Q8) | G2 (done) |
| Q8-6 renderer (bp) | BU | `C/Binding/Binding.PairCases.cs`, `OwnershipAnalysis.cs:359`, `OwnershipAnalysis.Cases.cs:321` | ±60 | D+R (Q8) | before H3 |
| H4 | R4 (BU reviews the Binding edits, review N3) | `C/Mir/Analysis/MirEffects*.cs` (read accessor only), `KimiLibraryCatalog.Definitions.cs` (the "None" rows; four new `KimiDeclarationId` members, R4 r2 N2; `KimiLibraryShapeTest`), `Binding.PublishedOperations.cs` (−48), `T/MirEffectsTest`, `T/EffectBoundImplementationTest*` | +70/−50, t ±90 | R (D+R under H0) | R4's exit |
| IC (answer R4-7) | R4 | `C/Mir/Analysis/MirDrops.cs` contract ("an `Incomplete` body implies a published Error"), its lowest-layer test (`T/MirAnalysisTest`) | ±10, t +30 | R | R4's exit; before H3b |
| Q19 (2nd reading) | R4 with R5 (review N5; answer R4-10) | none (evidence: X-1's ledger run at R4's exit head; R4 supplies the MIR-cause residuals, R5 maps rows to tests and snapshot rows) | 0 | — | R4's exit (after Abstract Calls U0–U5, R4-M106, and A6) |
| Q14 probe | BU | none (evidence `artifacts/verify/r6-q14/`) | 0 | — | H1a; Q8-3's rule as a probe |
| H3b round 1 | R5 (R4, BU rows) | branch `claude/r6-flip`; table Doc | 0 | R | H3, H4, Q19, H1, H1b, Q8-3, H5 drafts |
| H1 | BU | `C/Binding/Binding.Transfers.cs` (new: `CheckTransfers`), `Binding.Closures.cs`, `Binding.Diagnostics.cs`, the 10 old sites, `DiagnosticRequirement.cs`; DIAGNOSTICS `:107, :152, :166` | +110/−75, t +150 | G:R6-check-flip | H1a, Q8-6, Q14, OQ11 |
| H1b (review B2) | BU | `C/Binding/Binding.Obligations*.cs` (the verdict at Final, or the contract check), the ledger row `MirDifferenceLedger.ComparedSet.cs:51`; tests at Binding's records | +60, t +60 (H) | G:R6-check-flip | H1a, OQ11 |
| Q8-3 | BU implements, R4 reviews (one review; answer R4-11) | `C/Mir/MirValidator*.cs` | +30, t +40 | G:R6-check-flip | H1 |
| H5 | R5 (Core, Backend gate, `C/Analysis` hunk ≈ −380 (review N3), hover (H6), tests; integration: the Backend files are R5's, H0a-13), BU (Binding hunk; answer BU-3); R4 writes nothing in the group (IC landed first, answer R4-7) | §2 | ≈ −2,350/+90 src | FP (H0) + G:R6-check-flip + H15 | H1, H1b, Q8-3, IC, H3b (no open 2/5 row), H4, Q9–Q12, Q20/A-4, A-7 (met: R4-M106's U0–U5), A-5 met; Q18's test readers at H8a (R4 review N5) |
| H6 | R5 | in H5 (review B3; supersedes ruling R3): `HoverBuilder.Virtual.cs:75`; HOVER | ±20 | (H5's) | (H5's) |

## 9. Where this part departs from H0a and H0c

- **H0a-2's `LintErrors` term**: dropped from H5 on (§2.1, H0b-4); under mir between H3 and H5 it is also redundant for `Accepts`
  (same reason), so H0a may drop it too. (Ruling R1: dropped in H0a as well.)
- **H0c's `Compilation.Pipeline.cs:84` (old CFA report) at H10b**: it goes at H5, as H0a's table already says (A3 publishes its twin
  from H5; two producers otherwise). (Ruling R2: the report stops at H5; its code goes when its last caller goes.)
- **H0c's H6 position** (after H9): right after H5 (H0b-11). (Ruling R3; superseded by review B3: H6 is part of H5.)
- **H0c's `FallthroughKey` at H10b**: at H5, with its only reader (H0b-7). `LintErrors` and `LintErrorRecords` stay to H10b as H0c says.
  (Ruling R4.)
- **Q8-6's "the old Analysis calls it until H11"**: until H5, which deletes the caller (follows from ruling R4).
- Agreed: D-c1, revised by review B3 (no shadow in `Analyze`; the old analysis runs only through the transitional entry), with the partition invalidation moved to Core; D-c2 (H10a right
  after H5, after the post-merge sweep); `IsEffectRecord` with its ledger reader; the OldRule traits with ratchet row `:80` and
  `MirTargetSide`'s ledger half; the successors of §2.3.

## 10. Questions and their answers (revision 2)

| # | To | Question |
| --- | --- | --- |
| R4-1 | R4 | (index R4-7) Yes: A2's contract; it lands as unit IC before the group, so R4 writes nothing in it (answer R4-7) |
| R4-2 | — | Moved to BU as BU-3 (review B2). |
| R4-3 | R4 | (index R4-8) Yes, (A), with notes N6 (no premise mode) and N7 (every "None" row, ruled in) (§4) |
| R4-4 | R4 | (index R4-9) Yes, already R4-exit conditions; the missing families are F7, F8, F10 (§1) |
| R4-5 | R4 | (index R4-11) BU implements Q8-3; R4 reviews it (one review) |
| R4-6 | R4 | (index R4-10) Yes: with R5 at R4's exit, before H3b; input X-1's ledger at the exit head |
| BU-1 | BU | Yes: H1a and Q8-6 before H3-2; Q14 before H1's code; queued after G216 (a) |
| BU-2 | BU | (index BU-3) Yes: BU authors H5's Binding hunk with R5's review and reviews H4's Binding edits |
| BU-3 | BU | (index BU-5) Yes: census first; a located `Unsupported_Kd` where a kind cannot be judged yet; `InternalInvariant_Kd` only with a reproducer-backed argument (§3) |
| O-1 | owner | Answered 「はい」 (R5-M182): 1a as one list, 1r reported. |

## 11. DECISIONS text (H5 writes it)

Draft entry: "R5-M… MIR decides acceptance (R6-check H5, G:R6-check-flip with H1, H1b and Q8-3). `Analysis` is verified when the MIR
of the version exists and its published rows hold no Error; `Problems` and `ReportAnalysis` are those rows; `Analyze` no longer runs
the old analysis, which runs only through `Compilation.AnalyzeLegacyOwnership()` for its test and benchmark readers until H10b (review B3);
hover's 'Ownership checks' line reads the MIR (H6, review B3); generation's gate is `Accepts`, and an `Incomplete` body implies a
published Error (R4-M69), checked as `InternalInvariant_Kd` (review N2). Deleted: Binding's and the old analysis's effect checks, both
old `ReportDiagnostics`, PD12's refusal, `IsEffectRecord`, `FallthroughKey`, `AnalysisResult.BodyCount`, the OldRule traits and Old
sides. Supersedes R5-M156's 'the old path decides', R4-M1 for the default pipeline, PD12's refusal (R5-M96), Q8's 'until H11' for the
renderer's old caller, R4-M112's kept `UnprovenConstraint_Kd` ledger row (successor H1b). Rejected: an analysis verdict reading
`LintErrors` (a second copy of Binding's decision); the old analysis as a shadow in every check (review B3); a throwaway rehearsal; a
production check of the library's rows."

## 12. Not done

- No probe or run: the class counts of §5.4 are proposals; Q14 and Q19's second reading are absent (Q19: an R4/R5 item at R4's exit,
  before H3b, review N5); H1b's census of the obligation kinds and the 9 pending sites is its own first step (review B2, re-review 2).
- Exact line ranges inside `Binding.CallableEffects.cs` (`CheckCallableEffectUses`, `:179-224`, may record uses the obligations read)
  are checked by H5's author; §2.2 marks it ≈.
- The STATUS rows to rewrite and the per-row clauses of §5.4's known rows (G124, G228, G313, G160) follow from the run.
