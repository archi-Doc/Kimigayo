# R6-check H0b: the acceptance flip (H5) with H1 and H4, and the rehearsal's classification method (H3b)

Status: H0's design part b (plan §4.10 rows H0, H1, H3b, H4, H5; PD29, PD12, PD31), revision 0, design only, not reviewed. Working
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
| H0b-1 | **G:R6-check-flip = H1 → Q8-3 (validator rule) → H5**, one branch, one group review, one merge; H1a, Q8-6's renderer and H4 land on dev before it (bp or shadow) | 2, 3, 8 |
| H0b-2 | **H3b is the group branch's first full run, not a throwaway prototype**: the rehearsal applies H1, Q8-3 and H5 as implemented for landing; fixes it finds land on dev as R4/BU units; the branch is rebased and re-run until no row is class 2 or 5 | 5 |
| H0b-3 | H5's Core change: `Analyze` invalidates the ControlFlow and Ownership partitions itself, builds the MIR, runs the old analysis as an **unpublished shadow** (H0c D-c1, agreed); `Analysis` = the MIR of this version exists and its published rows hold no Error; `Problems` = those rows; `ReportAnalysis` publishes them through H3's publisher only | 2.1 |
| H0b-4 | `Analysis.IsVerified` reads **no `Binding.LintErrors` term** from H5: a lint Error rejects through `Accepts`' Binding-partition error state (SPEC 23.2.2); `AnalysisResult.BodyCount` (the old analysis's count) is deleted | 2.1 |
| H0b-5 | `Project.cs` needs **no edit**: `:550-554` already reads `Analyze`, `ReportAnalysis` and `Accepts`, which decide by MIR from H5 | 2.1 |
| H0b-6 | Generation's gate step 1 becomes `!Accepts` alone; gate step 2 (`FunctionCodegen.cs:90-101`, PD12's refusal and R5-M96's arena interim) is deleted; the `Group == -1` invariant (`FunctionCodegen.Drops.cs:25-28`) stays | 2.1 |
| H0b-7 | H5 also deletes what the flip leaves without a caller: both old `ReportDiagnostics` (`OwnershipAnalysis.cs:140, 220-456`, `ControlFlowAnalysis.cs:114-127`) and `Binding.FallthroughKey`; their readers outside tests are only `ReportAnalysis` | 2.2 |
| H0b-8 | **H4 is test-level** (recommended (A)): A4's summary of a row operation's own body replaces `SummarizePublishedOperation`, which has no production caller today; no production record; the four Array compiler-function rows are added (`Row: None`, SPEC 4.9.6 "None"); H4 deletes `Binding.PublishedOperations.cs` before H5 | 4 |
| H0b-9 | Five classes under Q13 (§5.3), one row per signature, pre-classified by a fixed map from ledger kind and category and from snapshot kind; the Session, the snapshot and the milestones decide, the ledger only proposes | 5 |
| H0b-10 | Stop rule: any class 5 row stops H5; class 2 rows block H5 until fixed on dev or accepted by the owner as one list (PD27's form); a second rehearsal round finding a new row of a family already fixed goes one level higher (AGENTS), two on one family to the owner (plan §5.2) | 5.5 |
| H0b-11 | H6 (hover from MIR) lands **right after H5**, before H10a: between them the hover line shows the shadow's verdict | 7 |
| H0b-12 | Rollback window: a revert of the group's merge alone until H10a lands; H10a waits for the post-merge sweep on dev (G3's model) | 2.5 |

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
| SPEC-example and pair tables | `spec-cases.json` 66 cases, 96 variants, 34 `Old` sides (plan: 22), 14 `Mir`; `pair-cases.json` 1 `Old`; `VerifiedSpecExampleTest.Landed` lacks A2, A5's F6–F8, F10 (R4's exit adds them) | `T/VerifiedSpecExampleTest.cs:21, 73-78` |
| Milestones | `tests/milestones/stage-baselines.json`: 39, 41, 43 rejected at Binding, 42 at Ownership (`Unsupported_Kd` at `var count: i32 = Registry.initialCount()`) | — |
| Ledger (latest local run, `artifacts/verify/mir-diff/bu85-u2`) | 258 signatures: acceptance-old 38, acceptance-mir 54, missing-old 56, new-mir 76, location 23, requirement 11; the brief's current figure is 266 signatures, Open (b) 150 (1,399 rows) | `ledger.tsv` |

## 2. H5: the flip, one commit with the old effect checks deleted (PD29)

### 2.1 The exact change in Core

`C/Core/Compilation.Pipeline.cs` (162 lines today), after H3 (H0a: `Compilation.Publication.cs` holds the publication table, keys, the
fact table and the cause resolver; `MirRecords` reads the class rows):

| Member | Today | From H5 |
| --- | --- | --- |
| `Analyze` (`:70-76`) | `BuildMir`, `Ownership.Analyze()`, `Analyzed`, `Analysis` | `Diagnostics.Invalidate(ControlFlow)`, `Invalidate(Ownership)` (moved from `OwnershipAnalysis.cs:68-69`), `BuildMir`, `Ownership.Analyze()` as an unpublished shadow `[Transitional("R6-check")]`, removal H10b (D-c1), `Analyzed` (removal H10a), `Analysis` |
| `Analysis` (`:35-43`) | old `IsVerified` and no MIR record; `BodyCount` the old analysis's | `new(this.mir is not null && errors == 0, errors)` where `errors` counts the published rows of Error severity (Warnings and the derived records' suppression as H3's publisher defines); after `InvalidateOwnership` (`mir == null`) not verified |
| `Problems` (`:51-65`) | old issues, then MIR records | the publisher's rows, in its order (H0a-2) |
| `ReportAnalysis` (`:82-94`) | old CFA, MIR records, old analysis | the publisher's rows only; the table collapses to "every record" (H0a §2's removal condition) |
| `AnalysisResult` (`:157`) | `(IsVerified, BodyCount, ErrorCount)` | `(IsVerified, ErrorCount)`; readers of `BodyCount`: `T/OwnershipAnalysisTest.cs:190, 209` (deleted asserts; the class goes at H8c), `T/ProductTestMembershipTest.cs:23` (re-pointed: the MIR program has no `Test` body), `T/SemanticSnapshotTest.cs:253` (printed; the snapshot is re-baselined) |
| File contract (`:3-9`, `:16-19`) | "Until R6 the legacy path decides" | the MIR decides; the shadow and the event with their removal units |

- **No `LintErrors` term (H0b-4).** Lint records are Binding's non-failing Errors (PD31) and `Accepts` already rejects through the
  Binding partition's error state. A second read in the analysis verdict is a second copy of Binding's decision and keeps
  `Binding.LintErrors` (`Binding.Lints.cs:81`, Transitional "H10") alive past its stage. Effect: a test reading `c.Analyze().IsVerified`
  on a lint-error program without publication sees `true`; H3b lists these as class 3 test re-points (check's verdict is unchanged).
- **No `PendingBinding` term.** The old term covers a node unresolved after complete Binding; MIR construction of such a node is its
  own record or `InternalInvariant_Kd`, and acceptance is computed from published rows (H0a-9), so no successor is added.
- `C/Core/Compilation.Generation.cs`: gate step 1 → `if (!this.Accepts) return false;` (every MIR record now fails `Analysis`); the
  contract lines `:6-8`, `:14-20` lose step 2. `FunctionCodegen.cs:90-101` deleted; its contract comment keeps the `Group == -1` check.
- `C/Core/Compilation.Switch.cs` contract: H3's mir-only selection ends; the switch selects the ledger, the mark and the manifest entry.
- `Project.cs`: none (H0b-5). `src/Kimi/Testing/TestCatalog.cs:82` reads `Analysis.IsVerified` as before.

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
| Old `ReportDiagnostics` (H0b-7) | not in the plan row | `OwnershipAnalysis.cs:140, 220-456` (≈ 238, incl. the derived `Unresolved` fallback `:337-341`), `ControlFlowAnalysis.cs:114-127`; `Binding.FallthroughKey` (`Binding.Lints.cs:134-150`) |
| PD12's refusal | yes | §2.1 |
| `VerifiedSpecExampleTest` MIR-only | — | `:73-78` asserts the published rows against `Mir ?? Expected`; 34 `Old` sides (`spec-cases.json`) and 1 (`pair-cases.json`) deleted; `Landed` names every family |
| OldRule | 25 traits, 13 files | traits and old-side assertions deleted; `MirTargetSide`'s ledger-key half (`tests/xUnitTest/MirTargetSide.cs:24-40` keys, `:44`) deleted, its MIR check is the test's assertion; ratchet row `ArchitectureRulesTest.cs:74-80` deleted |
| `stage-baselines.json`, `MilestoneSourcesTest` | — | re-baselined only where H3b's run shows a changed first diagnostic (each named) |
| STATUS's pipeline lag | `STATUS.md:5` and 16 other lines naming "default pipeline"/"old analysis" | the rows whose verdict H3b's table changes; the rest at H15 |
| DIAGNOSTICS | `:107`, `:152`, `:166` (H1, Q8 §3.7); effect records' place (old: at the violating effect; A4: at the obligation subject) | Auto rows |

Size (H): source ≈ −2,350/+70 (Binding −1,830, Analysis −380, Backend −12, Core +40/−25, Mir 0; R4's `Incomplete` per R4-1), tests
≈ −250/+150, JSON −35 entries. Plan: ±300, ≈ −2,700.

### 2.3 What H5 publishes on the default pipeline (H0a's table at H5)

Every row publishes: construction and collector records, validator findings, A3 (`RequireFallthrough_Kd`, the old CFA's twin no
longer), A1, A2, A5, `MirReport`'s disagreement record, **A4's problems and H4's records** (H4 has none under H0b-8), C1-F4 findings,
derived records as `PrerequisiteUnavailable_Kd` (H0a-8). No old analysis issue, no old CFA issue, no Binding effect record. Binding's
records (H1's `Binding.TransferRequired` included) and the lint records publish through Binding as before. The publication table's class
column becomes dead and goes in H5 (its removal condition).

Successors H0c asked for: `RequireFallthrough_Kd` → A3 (`C/Mir/Analysis/MirLints.cs:40`; its guessed-recovery derived form needs 2g-1's
Binding cause, H0a §6); the derived `Unresolved` fallback → none (§2.1); `ReportUnprovenOriginObligations`
(`OwnershipAnalysis.Borrows.cs:56`) → A5's Origin stratum for relations; MIR reads no `Binding.Obligations` (grep), so every other
obligation kind must already be judged by Binding or A5: an old-rejects/MIR-accepts row would block R4's exit, and H3b confirms (R4-2).

### 2.4 `KIMI_PIPELINE` from H5 to H12

| Span | `legacy` (default) | `mir` |
| --- | --- | --- |
| H5 → H10a | MIR decides and publishes; old analysis runs unpublished | the same, plus the ledger's observation (`Analyzed`), the `[mir]` mark, the manifest entry (`TestCatalog.cs:122`); no gate: VERIFICATION `:94` says a `-Pipeline mir` run is not required after H5 |
| H10a → H12 | as above, no event | the mark and the manifest entry only |
| H12 | switch removed (H0c §4.2) | — |

### 2.5 Rollback

The group merges as one merge commit. While the old Analysis exists and nothing depends on the flip, the rollback is `git revert -m 1`
of that merge (restoring old acceptance, Binding's effect checks and the old TransferRequired sites together, PD29). H10a, H8a–c and
H10b build on H5, so H10a starts only after the post-merge sweep on dev passes (`'*.ll'` O0/O2, 40 milestones, CLI harnesses,
snapshot), as G4 waited for G3's. After H10a a rollback also reverts H10a; after H10b fixes go forward.

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

## 4. H4: A4-based verification of published rows (R4; before H5)

- **(A) Recommended (H0b-8): a test-level successor.** R4 exposes A4's summary of one row operation's own body under its own premises
  with abstract Type parameters, the row's `Runs`, `Destroys` and `Calls` covered (the inputs `SummarizePublishedOperation` reads,
  `Binding.PublishedOperations.cs:20-46`); `T/MirEffectsTest` asserts every row operation with a Kimi body (13 today, plus `truncate` and
  `removeAll`) stays within its row; `EffectBoundImplementationTest`'s two methods move there; `Binding.PublishedOperations.cs` (48)
  is deleted. Reason: the bodies are the compiler's library (SPEC 4.9.6: "the library implementation must stay within it"), no user
  program can produce the record, and today's check is test-only too; a production check would add A4 work on every check for a
  compiler-owned invariant. Departs from the plan row's "its records publish from H5" (nothing to publish).
- (B) A production judgment: each reached row operation's body judged in A4's step 3, `InternalInvariant_Kd` at the operation on a
  violation. Rejected: cost on every check, and a published record no valid input can reach.
- **Array rows**: `Row: None` on `ArrayAppend`, `ArrayInsert`, `ArrayPop`, `ArrayRemove` (4 lines). Today A4 already gives them "None"
  through `Bodyless` → `Inputs`; the row makes SPEC 4.9.6's table the catalog's one answer, so when R8 gives them Kimi bodies A4 composes
  the row, never the body. No verdict changes. Rejected: deferring to R8 (the R8 body would be composed silently).
- When: after R4's exit, before the group's rehearsal; independent of H3 under (A). Review: one R review (D+R under H0). Size ≈ +60/−50,
  t ±60.

## 5. H3b: the rehearsal and its classification

### 5.1 The rehearsal (H0b-2)

Branch `claude/r6-flip` from dev with R4's exit, H3 (H3-1, H3-2), H4, H1a and Q8-6; it carries H1, Q8-3 and H5 as implemented for
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
| Binding effect records → A4's (Binding partition at the violating effect → Ownership partition at the obligation subject) | 3 | the q5 deep-dive's "partition move"; Notes/evidence differ (A4's facts, H0a §4.1) |
| Programs Binding's effect failure left incomplete regain MIR records | 4 | SPEC 23.1.2's gain; counted |
| H1: `Ownership.TransferRequired` → `Binding.TransferRequired`; repair `s` → `(s)` | 3 | Q8 §3.7 |
| H1: the old cascade after it (`MovedPlace_Kd` at a later use, probe p1) | 4 | rule W removal |
| H1: Q14's (a) old misses (S2 unconsumed Places, S3 inferred captures) | 1r | SPEC 3.5, 7.7.2; each Q14 row named |
| G165, G177, G211, G217, G268, G299 (default unsound acceptances the MIR rejects) | 1r | PLAN rows; clauses 15.6.2, 3.6, 15.6, 15.6.7, 15.6, 15.6 |
| G124, G228, G313, G160's default verdicts, G186's old walk | 1 or 3 | per row at the run (H0c §5.3) |
| G218, G291 (old false errors; A5 accepts) | 1a (H) | SPEC 3.4.3; 7.5, 3.5 |
| G121's lost `do` fallthrough record (2x-P10) | 5 unless fixed before H5 (H0c Q-c4) | lost independent record |
| Milestone 42's first diagnostic (old analysis's `Unsupported_Kd`) | 3 if MIR's first record differs; 2 never (it stays rejected, P42) | re-baseline named |

### 5.5 Who classifies, the table's form and the stop rule

- **R5** runs the rounds, groups the inputs into signatures, applies §5.4's maps and assigns each remaining row an owner; **R4** confirms
  every MIR-cause row (class 2 causes, 5's lost records, 1r/1a rows whose verdict is A1–A6's) and fixes its class 2 rows; **BU** the
  Binding-cause rows (H1's, the effect-record move, BU-10/BU-14's guards, A3's gates); **the owner** only class 2 acceptances, soundness
  questions and SPEC (c) rows (Q13). R4 and BU answer within their next session; an unanswered row stays 5.
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

- **Inputs.** dev at R4's exit (every A5 family, A6 2g-D–2g-7, the exit's end-to-end review; R4-M35), with H3, H4, H1a, Q8-6 and
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
  the old analysis runs unpublished until H10b; `KIMI_PIPELINE` selects only the ledger and the mark"; the rows of §5.4's class 1
  close or move with H5's commit; the effect-gap open issue of H0a §5.1 closes; STATUS rows updated; P42's remainder may start
  (R5-M13); H6 next, then H10a after the post-merge sweep.

## 8. Unit table

| Unit | Owner | Files | Size (H) | Review | Prerequisites |
| --- | --- | --- | --- | --- | --- |
| H1a (bp) | BU | `C/Binding/Binding.Check.cs` (new, from `Binding.cs:1006-…`), `Binding.Hir.cs` | ±70 | D+R (Q8) | G2 (done) |
| Q8-6 renderer (bp) | BU | `C/Binding/Binding.PairCases.cs`, `OwnershipAnalysis.cs:359`, `OwnershipAnalysis.Cases.cs:321` | ±60 | D+R (Q8) | before H3 |
| H4 | R4 | `C/Mir/Analysis/MirEffects*.cs` (entry), `KimiLibraryCatalog.Definitions.cs:78-81`, `Binding.PublishedOperations.cs` (−48), `T/MirEffectsTest`, `T/EffectBoundImplementationTest*` | +60/−50, t ±60 | R (D+R under H0) | R4's exit |
| Q14 probe | BU | none (evidence `artifacts/verify/r6-q14/`) | 0 | — | H1a; Q8-3's rule as a probe |
| H3b round 1 | R5 (R4, BU rows) | branch `claude/r6-flip`; table Doc | 0 | R | H3, H4, H1, Q8-3, H5 drafts |
| H1 | BU | `C/Binding/Binding.Transfers.cs` (new: `CheckTransfers`), `Binding.Closures.cs`, `Binding.Diagnostics.cs`, the 10 old sites, `DiagnosticRequirement.cs`; DIAGNOSTICS `:107, :152, :166` | +110/−75, t +150 | G:R6-check-flip | H1a, Q8-6, Q14, OQ11 |
| Q8-3 | R4 reviews, BU implements (H) | `C/Mir/MirValidator*.cs` | +30, t +40 | G:R6-check-flip | H1 |
| H5 | R5 (Core, tests), BU (Binding hunk), R4 (Backend gate, `Incomplete`) | §2 | ≈ −2,350/+70 src | FP (H0) + G:R6-check-flip + H15 | H1, Q8-3, H3b (no open 2/5 row), H4, Q9–Q12, Q20/A-4, A-7, Q18 or H2 |
| H6 | R5 | `HoverBuilder.Virtual.cs:75`; HOVER | ±20 | R | H5 |

## 9. Where this part departs from H0a and H0c

- **H0a-2's `LintErrors` term**: dropped from H5 on (§2.1, H0b-4); under mir between H3 and H5 it is also redundant for `Accepts`
  (same reason), so H0a may drop it too.
- **H0c's `Compilation.Pipeline.cs:84` (old CFA report) at H10b**: it goes at H5, as H0a's table already says (A3 publishes its twin
  from H5; two producers otherwise).
- **H0c's H6 position** (after H9): right after H5 (H0b-11).
- **H0c's `FallthroughKey` at H10b**: at H5, with its only reader (H0b-7). `LintErrors` and `LintErrorRecords` stay to H10b as H0c says.
- **Q8-6's "the old Analysis calls it until H11"**: until H5, which deletes the caller.
- Agreed: D-c1 (the shadow), with the partition invalidation moved to Core so the shadow touches no diagnostics; D-c2 (H10a right
  after H5, after the post-merge sweep); `IsEffectRecord` with its ledger reader; the OldRule traits with ratchet row `:80` and
  `MirTargetSide`'s ledger half; the successors of §2.3.

## 10. Open questions (one line each)

| # | To | Question |
| --- | --- | --- |
| R4-1 | R4 | After H5 deletes gate step 2, does `MirDropTable.Incomplete` (`MirDrops.cs:99`) stay as A2's contract or go in H5's R4 hunk? |
| R4-2 | R4 | Does every `BindingObligation` the old analysis's `ReportUnprovenOriginObligations` rejects have a Binding or A5 judge (no MIR reader of `Binding.Obligations`)? |
| R4-3 | R4 | Do you accept H4 as a test-level successor (A) with the four `Row: None` Array rows, landing after your exit and before the rehearsal? |
| R4-4 | R4 | Will A-4 bring the OldRule ratchet (`ArchitectureRulesTest.cs:80`, 7) to 0 and `VerifiedSpecExampleTest.Landed` to every family by your exit? |
| R4-5 | R4 | Who implements Q8-3's validator rule inside the group: BU (H1's author) with your review, or you? |
| BU-1 | BU | Will H1a and Q8-6 land on dev before H3, and Q14 run before H1's code, with its rows in `artifacts/verify/r6-q14/`? |
| BU-2 | BU | Do you take H5's Binding hunk (§2.2's Binding rows, ≈ −1,830) as its author, with R5's review? |
| O-1 | owner | None now: class 2 rows reach you as one list at H3b's last round; the rest is record selection (Q13). |

## 11. DECISIONS text (H5 writes it)

Draft entry: "R5-M… MIR decides acceptance (R6-check H5, G:R6-check-flip with H1 and Q8-3). `Analysis` is verified when the MIR of
the version exists and its published rows hold no Error; `Problems` and `ReportAnalysis` are those rows; the old analysis runs
unpublished in `Analyze` until H10b; generation's gate is `Accepts`. Deleted: Binding's and the old analysis's effect checks, both old
`ReportDiagnostics`, PD12's refusal, `IsEffectRecord`, `FallthroughKey`, `AnalysisResult.BodyCount`, the OldRule traits and Old sides.
Supersedes R5-M156's 'the old path decides', R4-M1 for the default pipeline, PD12's refusal (R5-M96), Q8's 'until H11' for the
renderer's old caller and H0a's `LintErrors` term. Rejected: an analysis verdict reading `LintErrors` (a second copy of Binding's
decision); a throwaway rehearsal; a production check of the library's rows."

## 12. Not done

- No probe or run: the class counts of §5.4 are proposals; Q14 and Q19's second reading are absent and are inputs of H3b round 1.
- Exact line ranges inside `Binding.CallableEffects.cs` (`CheckCallableEffectUses`, `:179-224`, may record uses the obligations read)
  are checked by H5's author; §2.2 marks it ≈.
- The STATUS rows to rewrite and the per-row clauses of §5.4's known rows (G124, G228, G313, G160) follow from the run.
