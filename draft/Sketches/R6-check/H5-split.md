# R6-check H5: implementation split of the Core side (R5)

Status: plan only, no code (R5's planning session; H0 owner-approved, DECISIONS R5-M182 with its amendments). Working record, not
authority. Inputs: [H0b](H0b-flip.md) §2, §5, §7, §8, §11; [H0](H0-design.md) rows H5, H6, H3b, §5; [H0c](H0c-deletions.md) D-c1, D-c5,
§2's constraints; [H3-2](H3-2-design.md) §3.1, §4 (N-a); DECISIONS R5-M183, R5-M184 (`origin/claude/r5-h32-u0`), BU-84, BU-85, BU-88
(`origin/claude/r6-flip`), Q12, Q13, R7-M2. Paths: `C/` = `src/Kimi/Compiler/`, `T/` = `tests/xUnitTest/Tests/`. Sizes are line counts
at dev `c66e317a8` (H3-2's files at `origin/claude/r5-h32-u2`); (H) marks an estimate. No build, test or probe was run.

Working time (WORKFLOW "Working-time limit", Q12): 2026-11-03 18:18–18:33, one agent, within the hour.

## 0. Facts that shape the split

- **Compile cycle Analysis ↔ Binding.** `OwnershipAnalysis.EffectBounds.cs` calls Binding's three validators and `OwnershipAnalysis.Cases.cs:115-116,
  130-131` calls `Binding.CollectCaseDestructions`; Binding's `EffectSummary` (`Binding.EffectBounds.cs:98, 973, 1061`) and
  `Binding.VirtualEffects.cs:41` read `Compilation.Ownership.TemplateBody`/`.ControlFlow` and write `OwnershipBody.CleanupEffects.cs`'s
  `AddCaseDestruction`/`CaseDestructions`. So R5's Analysis→Binding edges go before BU's deletion, and `CleanupEffects.cs` and the
  private `Ownership` after it. `IsEffectRecord` (`Binding.EffectObligations.cs:120`) reads BU-deleted tables: it goes before BU's hunk.
- **PD29 per commit.** One commit (F) stops both old producers of effect records: Binding's bind-time calls (`Binding.cs:511`, `:523`)
  and the old analysis's (`OwnershipAnalysis.cs:121`), while A4's problems start publishing; the then-dead code goes in the group's
  next commits (D1, D2, BU's B-del, D3). No commit has both producers or neither; dev sees one merge; the rollback (H0b §2.5) is
  unchanged. The literal "one commit with the deletion" would make one ≈ −2,300-line, two-author unit of about three hours (§4, X1).
- **Tests fail between F and the last T-unit by design.** H3b's round 1 is the group branch's first Session with H5 as it lands
  (H0b-2); its failing tests are round 1's input (class 3 re-points). Each unit passes its own checks (the rows it adds or
  re-points); the final round's Session is the group's evidence (X3).
- `Compilation.Ownership` is `Compilation.cs:129`; its test reads are 30 `.Bodies`, 16 `.Issues`, 1 `.ControlFlow` (47) in 26 files,
  `LegacyRecords.cs` included, matching ratchet rows `ArchitectureRulesTest.cs:63-66`; `src/Benchmark` has 13 `Ownership.Analyze()`
  calls in 8 files.
- From H5, `PublishMirRecords` publishes every `MirReport.Merged` problem through `Publish` plus the validator findings, and the class
  loop `MirRecords` (`Compilation.Publication.cs:114-140` at U2; `Compilation.Pipeline.cs:131-157` on dev) goes (R5-M184's removal
  condition).

## 1. Units (each one Q12 hour of implementation)

Kinds: **bp** behavior-preserving; **beh** behavior change; **del** deletion of code without a caller after F (verdict-neutral);
**t** test-only. Owner R5 unless marked. "Consent" = BU holds the Binding file for that hunk and reviews it (R7-M2).

| # | Unit | Kind | Files and content | Completion and tests (lowest layer) | Depends | Size |
| --- | --- | --- | --- | --- | --- | --- |
| E | Transitional entry | bp | `C/Core/Compilation.Pipeline.cs`: `AnalyzeLegacyOwnership()` (`[Transitional("R6-check")]`, removal H10b) runs the old analysis once per version and returns it; the 47 reads in 26 test files (incl. `LegacyRecords.cs:34, 39` and the three `Body(c)` helpers `EnumOwnershipTest.cs:306`, `MatchOwnershipTest.cs:368`, `OwnershipAnalysisTest.cs:311`) and the 13 Benchmark calls go through it; ratchet rows `:63-66` re-pointed to the entry's reads, same counts | New row: a second call on one version re-runs nothing (`Purpose=Allocation`, 0 B); the 26 classes; slnx build (Benchmark); bp: snapshots vs parent | dev merged into the branch (H3-2, IC, H4) | Core +15, t ±52, bench ±13 |
| O | OldRule and Old target | t | The 25 `[Trait("OldRule", …)]` in 13 files and their old-side assertions; `T/../MirTargetSide.cs` (91) loses `LedgerKey`, `Keys()` and the hash loop (`:18`, `:26-40`, `:44`, `:63-70`), its MIR check stays to H10a; `ArchitectureRulesTest.cs:28-34` (constants), `:74-80` (row, 7), `:93-~105` (its pattern test); the 7 OldRule tests without a MIR side get `MirTargetSide.Check` | The 13 classes; `ArchitectureRulesTest`; the 7 each assert their MIR records (fail-first by editing the expected record) | E | t ≈ −150/+25 (H) |
| F | The flip | beh | `Compilation.Pipeline.cs` (169; 129 after U0): `Analyze` = `Invalidate(ControlFlow)`, `Invalidate(Ownership)`, `BuildMir`, `Analyzed`, `Analysis`; `Analysis` = `(mir is not null && errors == 0, errors)` over the published rows of Error severity and findings; `Problems` = those rows in the publisher's order; `ReportAnalysis` = `PublishMirRecords` only (`:85`, `:94` stop); `AnalysisResult(IsVerified, ErrorCount)`; contract `:3-10`, `:17-20`, `Analyzed`'s doc. `Compilation.Publication.cs` (141): `PublishMirRecords` loops `Merged` through `Publish`, then findings; `MirRecords` and the "until H5" invariants go. `Compilation.Generation.cs` gate step 1 → `!Accepts`, contract `:14-17`. `OwnershipAnalysis.cs:68-69` (moved to Core), `:121` (old effect check unhooked). **BU's lines, consent:** `Binding.cs:511` → `CollectCallableEffectUses()`, `:523` gone. BodyCount readers: `OwnershipAnalysisTest.cs:190, 209` (asserts go), `ProductTestMembershipTest.cs:23` (re-pointed), `SemanticSnapshotTest.cs:253` (printed) | Fail-first rows in `PipelineSwitchTest`: an A1 problem rejects and publishes at its subject on every pipeline; a lint Error alone leaves `Analysis` verified and `Accepts` false; a shape the old analysis throws on (`OwnershipAnalysis.Enums.cs:282`) checks without a fault; A4's effect record publishes once (no Binding twin). `MirPublicationTest`, `PipelineSwitchTest` pass; records §5 | E, O, H3-2 (U3) on dev and merged | Core +45/−80, An −3, Bind ±2, t +60/−6 (H) |
| S | Spec examples MIR-only | t | `VerifiedSpecExampleTest.cs:50-78` asserts published rows against `Mir ?? Expected`; 34 `Old` sides in `tests/diagnostics/spec-cases.json` (164 lines, compact rows edited in place) and 1 in `pair-cases.json`; `Landed` names every family (F7, F8, F10 at R4's exit, R4-9) | `VerifiedSpecExampleTest` | F | t ±10, JSON ≈ ±35 |
| G | `Incomplete` contract in generation | beh (broken contracts only) | `C/Backend/Codegen/FunctionCodegen.cs:90-101` (301): the refusal goes; an `Incomplete` body reaching `Write` is `InternalInvariant_Kd` at its subject (IC's contract, `MirDrops.cs:95-103`); `Group == -1` check stays (`FunctionCodegen.Drops.cs`); `Compilation.Generation.cs` contract `:5-8`, `:18-20`; ARCH `:48`, `:153` (the interim `Unsupported_Kd` and step 2) | `FunctionCodegenTest.Drops.cs:364-415`: `AnArenaSkippedBodyIs…` → injected `Incomplete` records `InternalInvariant_Kd` (fail-first); `AnInexactBody…`, `ACopysProblem…` → check rejects with the A1/A2 code published, `Generate` adds none | F | Backend −12/+6, Core −6/+2, t ±30 |
| H6 | Hover from MIR | beh | `src/Kimi/Checking/Hover/HoverBuilder.Virtual.cs:75` (78): "completed" when the function's MIR body has no merged problem, else "pending"; one Core query over `MirReport.Merged`'s members (H); HOVER row | `VirtualHoverTest.cs:54` (pending) plus a completed row (fail-first: today the old analysis runs in the editor) | F | ±20 (H) |
| D1 | Old report paths | del | `OwnershipAnalysis.cs:140`, `:220-444` (226 at dev, 204 on the branch after H1), report-only helpers `OwnershipAnalysis.Reservations.cs:9-33` (H), `OwnershipAnalysis.Cases.cs:174-186` `CaseFact`, `ControlFlowAnalysis.cs:114-127` and `GuessedBy` (H); **consent:** `Binding.Lints.cs:134-150` `FallthroughKey`; `T/OptionalTryDiscardTest.cs:246` re-pointed to A3's published `RequireFallthrough_Kd` | Unit classes of the touched files; `OptionalTryDiscardTest` | F | An ≈ −310, Bind −17, t ±3 (H) |
| D2 | Analysis→Binding effect edges | del | `OwnershipAnalysis.EffectBounds.cs` (66, whole); `OwnershipAnalysis.Cases.cs:115-116`, `:130-131`; `CaseDestructions` reads in `GenericAdaptationEffectTest.cs:38`, `GenericCleanupCaseTest.cs:42-45` (re-pointed to A4's items or deleted where `MirEffectsTest` covers them, H); **consent:** `IsEffectRecord` (`Binding.EffectObligations.cs:118-120`) with the ledger filter `MirDifferenceLedger.ComparedSet.cs:21-22, 40-42, 160` | `MirDifferenceTest`, the two Generic classes, `MirEffectsTest` | D1 | An −70, Bind −3, t −25 (H) |
| B-del | **BU's Binding hunk** | del | `Binding.EffectBounds.cs` (1,411), `.VirtualEffects.cs` (78), `.EffectViolations.cs` (126), `.CallableEffects.cs` (≈ −80 of 253), `Binding.cs:807`, `:844`, `.VirtualDeclarations.cs:82`, `.EffectObligations.cs:32`, `:66`, `KimiLibraryKinds.cs` `Formatting` (H0b §2.2); R5 reviews (BU-3) | BU's | D2 | ≈ −1,720 |
| D3 | Ownership private | del | `OwnershipBody.CleanupEffects.cs` (95), `OwnershipModel.cs:328`; `Compilation.cs:129` private (the entry its only path, re-review 3); ratchet rows `:63-66` drop the `.Ownership.` patterns the compiler now forbids, keep the entry's | `ArchitectureRulesTest`; slnx build | B-del | An −96, Core ±2 |
| T1…Tn | Round re-points | t | H3b's class 3 test rows, one unit per family (effect records Binding → A4 at the obligation subject; lint verdicts read without publication; old-code `Problems` reads; PD12 refusal readers in `MirBorrowsTest*`, `CallOriginRelationTest`, …) | Each family's classes; the final round's Session | round n's table | t ≈ −250/+210 total (H0b, H) |
| Rec | Records | t/doc | STATUS, PLAN, `stage-baselines.json`, semantic snapshot (§5) | final round's Session | all above | docs |

## 2. Order with H3b and BU

```
dev merge (H3-2 U3, IC, H4) → E → O → F → S → G → H6 → [H3b round 1] → D1 → D2 → B-del (BU) → D3 → T1…Tn → Rec → [final round] → group review → merge (R5's window)
```

- **Before round 1** (the rehearsal must see H5 as it lands, H0b-2): E (else 26 classes fail on unanalyzed reads), O and S (pre-named
  class 3 rows that would only add noise), F, G (natives under the final gate), H6 (hover tests in the Session).
- **After a round** (verdict-neutral after F): D1, D2, B-del, D3 and the T-units. All land before the final round, whose Session is the
  window's evidence (H0b §7); a D-unit that moves a verdict is a class 5 row.
- **BU meets R5** at four points: F's two `Binding.cs` lines (BU's consent and review), D1's `FallthroughKey` and D2's `IsEffectRecord`
  (consent), and B-del between D2 and D3 (BU authors, R5 reviews). The branch has one writer at a time: BU until R5 announces H5 on
  the board (after Q8-3b), then turns by announcement; each writer pushes after its unit's checks.
- **A round's fixes come back** by unit and by owner: class 2/5 causes land on dev as R4 (MIR), BU (Binding) or R5 (Core, Backend,
  publisher) units in windows; `claude/r6-flip` then merges `origin/dev` (merge only, never rebase; H0b-2's "rebased" reads "merges
  dev" under the branch rule) and the round re-runs. A fix to a unit already on the branch is a new commit by that unit's owner, never
  an amend. Class 3 rows become T-units on the branch (R5; BU reviews Binding-cause ones). Merges between rounds conflict on Core
  files H3-2 also touches: R5 resolves them (diff3; drop the base marker).

## 3. Sizes (dev `c66e317a8`; Binding at dev, H1 deltas on the branch)

R5 source: Core ≈ +62/−88 (E, F, G, D3), Backend −12/+6 (G), `C/Analysis` ≈ −480 (F −3, D1 ≈ −310 with helpers (H), D2 −70, D3 −96;
H0b's ≈ −380 omitted the report-only helpers), hover ±20, Binding by consent ≈ −22 (F, D1, D2). Tests: E ±52, O ≈ −150/+25, F +60/−6, S ±10, G ±30,
D1–D3 ≈ −30, T ≈ −250/+210 (H, from round 1's table). JSON ≈ 35 rows. BU: ≈ −1,720 (file counts; H0b said −1,830). Net (Q5) ≈ −2,500 source.

## 4. Risks and their splits

- **E past the hour** (26 files, 47 reads, 8 Benchmark files): split E1 (entry, its test, `LegacyRecords`, the three helpers, Benchmark)
  and E2 (the direct reads). Risk: a test reading `.Bodies` before `Analyze` now runs the analysis; pre-flip it changes nothing.
- **O**: the 7 OldRule tests without a MIR side (ratchet row 7; R4's exit should have made it 0) may lack a pinnable MIR record before
  the flip: such a test moves to a T-unit after round 1 as a class 1/3 row. Split O1 (traits, ledger half, ratchet) / O2 (the 7).
- **F** is small in code but its failing list is large; it needs H3-2 U3 on dev first. If `Problems`' readers need the publisher's
  order beyond `Merged`'s, F stops and asks (no second order).
- **D1** helpers' extent is (H): split D1a (`ReportDiagnostics` bodies, `FallthroughKey`, the CFA re-point) / D1b (helpers).
- **T-units** are unknown until round 1 (H0b: t −250/+210): one family or ≈ 25 re-points per hour; past the hour a unit parks on
  `held/r5-h5-t<k>` and splits at a table row. More than 6 T-units, or a family recurring after a fix, goes to the planning session.
- **B-del** waits for D2 by compile order; BU's slip holds D3 and the final round, not the earlier units.
- Benchmark: E and D3 change `src/Benchmark` callers; a Unit run does not build it, so each also builds the slnx.

## 5. Records each commit carries

- **F**: the DECISIONS entry of H0b §11 ("R5-M… MIR decides acceptance …"), adding: "PD29 holds per commit: F stops both old producers;
  their code goes in D1, D2, B-del and D3 within the group's one merge" and the transitional entry's removal unit (H10b); supersedes as
  §11 lists. DIAGNOSTICS: the dated exception note at §4.3 "Later phases" (`:113`, H3-2 §4, N-a, verbatim) and that bullet's two old-analysis sentences (the
  `LintErrors` rejection; `UninitializedPlace_Kd` resting on `FallthroughKey`); the effect records' place (A4 at the obligation
  subject) and the successors of the old reports (H0b §2.3) where DIAGNOSTICS states them. The commit message
  names stage 4's series break (D-c5) and the Q5 net size.
- **G**: ARCH's code-generation row (`:153`) and `:48` (gate step 2, the interim `Unsupported_Kd`, R5-M96/M146). **H6**: HOVER's row.
- **D1–D3**: CODEMAP where navigation to the old report or `CleanupEffects` changes; Q5 sizes.
- **Rec** (after the final round): STATUS `:5` (pipeline lag, `OldRule`, `Old` entries) and the rows H3b's table changes, nothing
  unverified; `tests/milestones/stage-baselines.json` only where the run shows a changed first diagnostic, each named (milestone 42);
  `SemanticSnapshotTest` re-baselined, reason "the flip, H5"; PLAN: the window line, §5.4's class 1 rows named or closed, F14's rows
  (G164, G203, G213, G218, G268, G291) reworded; class 3/4 counts by code in the message (Q13). The "H5 landed" position line
  (H0b §7) is the planning session's window commit after the merge.

## 6. Departures for the planning session

| # | Point | Recommendation |
| --- | --- | --- |
| X1 | PD29's "one commit" read per commit (§0) | adopt; F's DECISIONS entry states it |
| X2 | O lands before the flip: the old-side assertions stop pinning the old verdict for the few commits before F, on the branch only | adopt (R5-M182 deletes them at H5; the MIR side stays asserted) |
| X3 | Commits between F and the last T-unit fail Session tests outside their own rows (H3b's input) | adopt as H0b-2's reading; the group review checks the final Session |

## Rulings (the planning session, 2026-11-03)

- **X1 adopted.** PD29's intent is that dev never holds a state where both old producers or neither run. The flip group lands as one
  merge (H0b-1), so no intermediate commit of the branch reaches dev, and PD29 holds per merge. Its "one commit" is read per commit only
  on the branch: F stops both producers, and the dead code goes in D1-D3 inside the same merge. Reason: WORKFLOW's one-hour limit
  (DECISIONS Q12), since a literal single commit is a three-hour, two-author unit. F's DECISIONS entry states this reading as a line
  under R5-M182. The owner is told as a planning decision: it changes no acceptance and no published record on dev.
- **X2 adopted**, on one condition: O's unit run shows no change on the default pipeline (traits and ledger sides only). Any selection
  change under the mir lane is recorded as H3b round 1's input.
- **X3 adopted**, on three conditions:
  - each unit's own classes pass in its unit run;
  - the branch Session's failures between F and the last T-unit are recorded as that round's H3b input (the table), never hidden;
  - no unit changes an expectation except a class-3 re-point that H3b's table names (Q13).
  The group review checks the final round's Session.
- **Start.** H5 starts now. R5 tells BU (the branch's writer until now), merges dev into `claude/r6-flip`, and begins with E. H3-2 lands on
  dev in its own R5 window; the branch merges dev again before H3b round 1.
