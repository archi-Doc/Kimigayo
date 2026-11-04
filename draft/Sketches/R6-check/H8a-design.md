# R6-check H8a: the old analysis's `Bodies` and `Issues` readers (design)

Status: revision 0, uncommitted. Unit row (H0 §4, R5-M182): "H8a | `Bodies` and `Issues` residue (read through the entry from H5) → MIR
dumps, problems or deletion; Q18's test readers (R4 review N5) | R5 (R4 reviews) | H5, H10a | group G:check-tests | ≈ −900 t"; H0c §2
row 2; H10a-design §3.

Base: `claude/r5-h10a` head `73dbfe4dc` (H10a commits 1–5). Paths: `T/` = `tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`,
`M/` = `tests/mir/`. Counts are greps on the base. No build, test, probe or run was done.

Working time (Q12), logged in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\h8a\design-state.md`: 06:06–06:22 (2026-11-05).

## 1. Inventory (item 1)

| Read | On the base | Where |
| --- | --- | --- |
| `.AnalyzeLegacyOwnership().Issues` (ratchet 15) | 15 reads: 13 test methods and `LegacyRecords.Of` | `X/LegacyRecords.cs:36`; `T/` BlockEscapeLoan 1, CallReservation 1, ChildMoveActivation 4, ObjectSlotUpdate 1, OwnedStoredReference 3, OwnershipJoin 1, OwnershipStorageLimit 2, ValueCallActivation 1 |
| `.AnalyzeLegacyOwnership().Bodies` (ratchet 28) | 28 reads in 14 files, 3 of them the `Body` helpers, which have 40 calls (OwnershipAnalysis 5, EnumOwnership 15, MatchOwnership 20); 58 test methods; 2 more `body.Issues` reads (`InternalInvariantTest:19, 24`, `MatchOwnershipTest:153`) | OwnershipAnalysis 7, CallableRegionScaling 5, BorrowDependencyStorage 3, DeferredEmission 3, and 1 each in CallArgumentMapping, ElementUpdateEmission, EnumOwnership, GenericDefault, InternalInvariant, MatchOwnership, ReturnedBorrowAncestry, StringComparisonEmission, TempProbe, WholeValue |
| `.AnalyzeLegacyOwnership().ControlFlow` (ratchet 1) | 1 | `X/LegacyRecords.cs:31` |
| `LegacyRecords.Of` (kept to H10b by R5-M188) | 7 calls in 6 files | `T/AbstractCallReachTest:376`, `ClosureEnvironmentOriginTest:296, 856`, `ClosureResultOriginTest.Makers:393`, `MirOriginsTest:264`, `NeverContinuationTest:446`, `OriginRelationDiagnosticTest:328` |
| Bare entry calls (no `Bodies`/`Issues`) | 7 | `T/OriginObligationVerdictTest:28, 33, 39`; `PipelineSwitchTest:347-349`; `OwnershipStorageLimitTest:80` |
| Benchmarks | 8 files | `src/Benchmark` (H9) |

F13 recount: H0's "H8a 28 → 29" is 28 here (the ratchet's row). H0c's "3 `Body(c)` helpers (29 calls)" is 40 calls, and its "16
`Issues` reads in 10 files" is 15 in 9 (TempProbeTest now reads `Bodies`).

**Q18's test readers (item 3).** Plan Q18 names "the 21 rejecting `pair-cases.json` rows, 15 `Ownership.Issues` and 26
`OwnershipFailure` test lines". They map as follows:
- The 15 `Issues` reads are the first row above.
- The failure lines are now 12 old-issue `OwnershipRequirement` tests in 8 files, all inside the readers above: ChildMove 3,
  OwnedStoredReference 2, OwnershipStorageLimit 2, and 1 each in ObjectSlotUpdate, OwnershipJoin, ValueCallActivation,
  InternalInvariant and `MatchOwnershipTest:153`.
  - `PipelineSwitchTest.TheMultiCodeHelpersAreTheOwnershipMapping` (4 lines, the `OwnershipIssue.Code` image) holds old-type names, so
    it is H8c's.
  - MirCensusTest's 20 lines are MIR requirements, not readers.
- The pair-cases rows already read the published MIR record and its "loan" related location (`GenericOwnershipCaseTest.cs:39-40`).
  Nothing is left there.

So all of Q18's readers are H8a-i's (D2, §5), and none remains after it.

## 2. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **The rule.** A reader whose subject is a rule the MIR decides moves to that rule's lowest public layer: the published record (D2) or a MIR dump golden (D5). A reader whose subject is the old analysis itself (its API, plan identity, storage tables, internal checks or limit) is deleted now, and the commit names the MIR counterpart where one exists. | Since H5 the old analysis decides nothing (D-c1). Its consumers are these tests and the benchmarks, and H9 re-points the benchmarks, so deleting its self-tests loses no subject that any consumer relies on. Deleting now leaves H10b a pure code deletion (item 4, D-c4). | §3 |
| D2 | **Successor of an `Issues` reader**: the published record (`DiagnosticCorpus.Check` or `TestDiagnostics` after `ReportAnalysis`), with its code, its span and the related locations the publisher makes from the MIR's facts (borrow = Anchor, loan = Use or Store, reservation = Reservation; `MirPublicationTest.ASiteFactIsARelatedLocation`). Old fact → MIR: `Source` → the span; `LoanSource` → the borrow or loan related; `Failure`+`Activation` → the code (`CallActivationConflict_Kd`, `PlacementActivationConflict_Kd`, `CallReservationConflict_Kd` or `ComparisonLoanConflict_Kd`); `Reservation`/`ConflictingReservation.Call` → the reservation related; `Input.Call` → the call enclosing the span; `Destroyed` → the borrow related's Place at the exit's span; `Borrow` → the borrow related. | `Compilation.Problems` carries only code and subject, so the MIR's facts reach a test through the publisher. The classes' existing MIR pins already take this form (`ChildMoveActivationTest.TheChildFirstPlacementNamesThePlacedChild`, `GenericOwnershipCaseTest`), so it needs no new helper (Short; one form per class). | the 8 `T/` files of row 1 |
| D3 | **Stop rule for moved expectations.** These programs' MIR records have been published since H5 but pinned nowhere (group review B N2), and H3b could not see them because their tests kept passing. The commit names each changed expectation per test as one of three kinds. (a) The same fact. (b) A MIR code or location that H3b or a T-unit already classified, cited. (c) A lost fact: the test pins the MIR's record without it, its comment names the open issue (A6, R4), and PLAN records it; the known one is the placement Note (`ChildMoveActivationTest:102-104`). A MIR record that contradicts the test's SPEC comment (OwnershipJoin's "one record, at the exit ..., never also at the join") or that no classification covers stops the unit and goes to R4 as a class-2 candidate. The comment is never reworded to fit. | AGENTS: never weaken an expectation; WORKFLOW Review Q13. | commit messages |
| D4 | **`LegacyRecords` ends at H8a** (supersedes R5-M188's bullet "stays to H10b"). The old halves of its callers are deleted: `OldPathGaps` and `Records().Old`, `AssertSides`' D1/D2, `ClosureEnvironmentOriginTest:296`'s old side, `OnlyTheOldAnalysisRejects...`'s old half (the test is renamed to its MIR half), and `NeverContinuationTest:446`'s old half (its stale `:434` comment goes too). The Binding-subject reads are re-pointed: `OriginRelationDiagnosticTest:328` → `Assert.Single(MirTargetSide.MirSide(c))` with requirement `Binding(OriginRelation)` (or `c.Binding.Issues` with empty `Problems` if the row is a twin); `MirOriginsTest:264`'s twins → `c.Binding.Issues` (H11 removes them with the twins). | R5-M188's reason, "its remaining readers assert the old analysis for its own subject", no longer holds once D1 deletes those readers. H10b then finds no reader (item 4). | `X/LegacyRecords.cs` (deleted), 6 `T/` files |
| D5 | **Successor of a `Bodies` reader** whose subject is a lowering rule (cleanup order, conditional cleanup, replacement, construction responsibility, a match's acquisition and cleanup, call evaluation order, element update order): the rule's function in the area's existing golden (`M/scopes`, `updates`, `constructors`, `matches`, `order`, `calls`). An existing function that already shows the rule is reused and named in the commit; otherwise one valid function is added, which MirCensusTest requires to have no problem. No new golden file. A test's acceptance line becomes a row of the class's `Supported*Verify` theory when that theory lacks the program; the old reads go. | The ratchet row's own name is "R4 dump goldens". MIR is the lowest layer that shows a lowering order. One function per rule, deduplicated, keeps the generated data small. | §3, `M/*.kimi`, `.mir` |
| D6 | **Allocation tests keep every measurement.** Since H5, `c.Analyze()` runs no old analysis, so each measured lambda already measures the MIR: WarmEscapeChecks, WarmEnum, WarmMatch, WarmDefaultContexts, DependentBodiesUseCompactTables, InspectionOnlyBodies and GrowingPlansReuseBoundedStorage. Only the old reads go. Where an old read guarded that the workload exercises its form, a MIR count replaces it: GenericDefault's `DefaultContexts` → the bodies' `MirScopeKind.Inlined` scope count; DeferredEmission's operation bound → the MIR body's block count per depth, pinned. The old storage bounds are old representation and go: `BorrowStorageBytes`, `BorrowDependencyCapacity`, `LocalRegion*` and `LocalLoanFlowCapacity`. | AGENTS: never relax an allocation assertion. None is relaxed; the zero-allocation reruns are the MIR's bounded reuse (OQ2). | 7 `T/` files |
| D7 | **Split by method.** H8a moves or deletes, once, each method that reads `Bodies`, `Issues` or `LegacyRecords` (directly or through a helper), together with every old name it uses, enums included. H8c takes the old names in the methods left. | One owner per method. This resolves H0c's overlap between its H8a row (the 3 `Body` helpers) and its H8c row (the five classes "deleted with their classes"). | §4 |
| D8 | **TempProbeTest's body dump becomes the MIR dump**, `MirDump.Write(c.BuildMir(), 0, text)` as in `MirBuildTest.Dump`. The tracked KIMI_PROBE dumper keeps its name, and its stale "TEMP: not committed" line is corrected. | The probe's purpose, dumping a program's ownership operations, is the MIR's now. Notice: sessions probing with KIMI_PROBE get MIR text. | `T/TempProbeTest.cs` |
| D9 | **Ratchets.** `ArchitectureRulesTest` goes from 15 to 0 (`Issues`), 1 to 0 (`ControlFlow`) and 28 to 0 (`Bodies`) as the reads leave, and its `:57` comment drops LegacyRecords. H10b turns the rows into zero-reference rows (D-c4). | The ratchets' rule. | `T/ArchitectureRulesTest.cs` |

## 3. Readers and fates (item 2)

**`Issues` readers (H8a-i):**

| File | Method (rows) | Subject | Fate |
| --- | --- | --- | --- |
| ChildMoveActivationTest | AParentUsedWhileTheMovedChildsLoanIsHeldIsRejectedAtTheUse (4) | 15.6.3, 15.6.7: rejected at the write `p@follow`, not at the call; the holder | D2: code, span at `p@follow`, holder related |
| | MovingAChildWhileItsOwnChildIsLiveIsRejected | the call activation's conflict; loan `let d = c` | D2: activation code, borrow related |
| | AParentPlacedWhileItsChildIsLiveInTheLiteralIsRejected (3) | G53 placement activation | D2. The old `PlacementActivationConflict_Kd` becomes the MIR's `ComparisonLoanConflict_Kd`, already pinned at H5 (D3 b), and the Note is lost (D3 c). Row 1 absorbs `TheChildFirstPlacementNamesThePlacedChild`, so no case is duplicated |
| | AParentUsedWhileItsPlacedChildIsLiveInTheLiteralIsRejected (2) | read while the placed child is live | D2 |
| CallReservationTest | DiagnosticsIdentifyPreparationOrActivation (2) | 15.6.7: preparation vs activation | D2: `CallActivationConflict_Kd`/`CallReservationConflict_Kd` and the reservation related |
| ObjectSlotUpdateTest | AStoredPayloadViewPreventsSlotReplacement (3) | replacement under a live `objref` view | D2 |
| OwnedStoredReferenceTest | StoredReferenceArgumentsRespectLiveChildren (3); AReservedStoredReferenceActivatesExclusively (4) | live children, exclusive activation | D2 |
| | DeclaredOriginReservationsPermitReadsButStillRejectWrites | the overlap at the second lending point; Input and ConflictingReservation | D2: `CallReservationConflict_Kd` at `item.0` in `poke(item.0)`, reservation related inside `change3(...)` |
| OwnershipJoinTest | JoinedBorrowCannotOutliveEitherLocalSource | 15.6.6, 16.2.2: one record at the exit; `Destroyed` `local` | D2: one published Error at the exit, borrow `local@ref` (D3's named stop case) |
| ValueCallActivationTest | AnArgumentCannotTakeWhatTheReceiverHolds (4) | the receiver holds it; activation | D2 |
| BlockEscapeLoanTest | WarmEscapeChecksAllocateNothing (3, Allocation) | the old escape count | Its line goes (D6). The MIR counts and allocation stay; `EveryOutputRelatesTheBorrow` pins the borrow related |
| OwnershipStorageLimitTest | TheOldLimitPublishesNothingBesideIndependentErrors (2) | OwnershipStorage's logical limit | Deleted (D1), with the bare call `:80`. Its MIR counterpart is H10a D10's `TheMirsArenaLimitPublishesBesideIndependentErrors`. The MIR checks the logical size (`MirDataflow.Within`), so retained capacity has no counterpart |
| LegacyRecords' 7 callers | §1 | old halves; Binding twins | D4 |

**`Bodies` readers (H8a-ii and H8a-iii):**

| File | Methods | Subject | Fate |
| --- | --- | --- | --- |
| OwnershipAnalysisTest | ImplicitBorrowIsRecorded…, ConditionalReplacementUsesPostRhsState, ExitCleanupFollowsDeclarationOrder…, ReturnDuringLaterArgument…, BorrowedArgumentDoesNotMoveItsSource, StaticStringTemporary…, BranchMoveMakesExitCleanupConditional, ScalarSelfAssignment…, LoopTransferCleansInnerLocal… (9) | cleanup, replacement and moved-state rules | D5: `M/scopes` (reusing `cleanup`, `consumeTwo`, `returnInArgument` where they show the rule) and `M/updates`. The `c.Problems` and acceptance lines stay |
| | InputStatesReplayWithinBlocks | the old solver's input states | deleted (D1); A1's replay is `MirInitTest`'s |
| EnumOwnershipTest | 11 methods (SuccessfulConstruction…, Abandonment…, NestedAbandonment…, ReturnInsidePayload…, ReturningAnEnum…, LoopTransferAbandons…, AbortDoesNotReach…, WholeReplacement…, ConditionalReplacementAndCleanup…, CompletedTemporaryRegisters…, RebindingChangedStorage…), the 3 designator theories through `CheckConstruction`, and `ReturnDestruction` | enum construction responsibility and abandonment | D5: `M/constructors`. `CheckConstruction`'s old operation kinds go, and its acceptance stays. RebindingChangedStorage keeps acceptance after the rebind |
| | WarmEnumAnalysisReusesStateAndPlans (Allocation) | warm reuse | D6, keeping `c.Analysis.IsVerified` false after `Bind` |
| MatchOwnershipTest | 17 methods (NeverSubject…, TestsReadAnIntactSubject…, CopyBindingAlso…, CleanupUsesReverseStructure…, NestedCleanup…, WarningCoveredArm…, SubjectAndPayloadsHaveFreshLifetimes…, SubjectIsEvaluatedOnce…, TransfersSecureValue…, DeepReturnCleans…, NestedSubjects…, OuterLoopContinue…, SubjectEvaluationTemporaries…, AbortAfterAcquisition…, MatchMayPrecede…, BodyBindingsAreAcquired…, ChangedPayloadStorageRebuilds…) | a match's acquisition, cleanup and moved state | D5: `M/matches`, with rows of `SupportedMatchesVerify`. ChangedPayloadStorage keeps acceptance after the rebind |
| | WarmMatchOwnershipAndBindingReuseStorage (Allocation) | warm reuse | D6, with `c.Analysis.IsVerified` in place of `body.IsVerified` |
| BorrowDependencyStorageTest | DependentBodiesUseCompactTables, InspectionOnlyBodiesNeedNoLocalDependencyTable (Allocation), InspectionAndStoredLoansExecute | the old borrow tables' compactness | The old lines go (D1, D6). The MIR allocation and the native fixture stay |
| CallableRegionScalingTest | GrowingPlansReuseBoundedStorage (Allocation) | the old region storage | The old lines go (D6, OQ2) |
| CallArgumentMappingTest | SemanticCallKeepsEvaluationOrderAndParameterSlots | named arguments' order and slots with defaults | D5: `M/order` or `M/calls` (`f(c: 3, a: 1)`). `CallAt` after detachment is old API (D1) |
| DeferredEmissionTest | CleanupSegmentsHaveUniqueMembershipAndDeferredEndpoints | the old plan segmentation | The loop goes (D1). `Generate` stays, and D5 adds `M/scopes` if nested `defer` is missing |
| | NestedSelfExitsAndContinuesHaveMeasuredBoundedExpansion | the expansion bound | D6: the MIR body's block count per depth |
| | AnExpansionPastTheOldLimitIsGeneratedOnEveryAnalysis | the old limit's count | Its line goes. The rest stays to H10b, which deletes `DeferredExpansionLimit_Kd` |
| ElementUpdateEmissionTest | RightSidePrecedesLocationReadAndStore | right side, location, store; loan modes | The fixture stays. D5: `M/updates` (`a[index()] += rhs()`) |
| GenericDefaultTest | WarmDefaultContextsAllocateNothing (Allocation) | nested default expansion | D6 |
| InternalInvariantTest | CompletionDisagreementIsAnInternalIssue | the old analysis's internal check | The file is deleted (D1). The MIR's: `MirValidatorTest`, `MirPublicationTest` (findings → `InternalInvariant_Kd`) |
| ReturnedBorrowAncestryTest | ResultAncestryUsesRecordedInputsAfterSourceDetachment | old API after detachment | The old part goes (D1), and acceptance stays |
| StringComparisonEmissionTest | ComparisonsPreserveOwnership | the old `ValidateComparisonLoans` | The loop goes (D1). The fixtures and audit stay |
| TempProbeTest | Dump | the probe | D8 |
| WholeValueTest | UpdatesRetainTheirMeaningAcrossStorageForms | replace, exchange and swap init state, local and borrowed | D5: `M/updates` (3 × 2 forms). The `OwnershipOperationKind` parameter goes |

## 4. The split (item 4)

| Unit | Takes |
| --- | --- |
| H8b | The 64-ratchet control-flow entry calls (for example `OwnershipAnalysisTest.ReanalysisClearsPreviousFlowResults`) and the lint reads. D4 removes `LegacyRecords`' `LintErrorRecords` read, so H8b's 14 become 13 |
| H8c | The enums and old-type names H8a leaves: `PipelineSwitchTest.TheMultiCodeHelpersAreTheOwnershipMapping`, `OwnershipStorageLimitTest.OversizedProducts…`, SharedEngineTotalityTest, MirInitTest, the five classes' other methods, and class names such as `BorrowDependencyStorageTest` (OQ4) |
| H9 | The 8 benchmark files |
| H10b | The entry with the callers that read neither: `PipelineSwitchTest.TheLegacyOwnershipEntryRunsOncePerVersion` (deleted with it); `OriginObligationVerdictTest`, whose `Verdicts` counter only `OwnershipAnalysis.Borrows.cs:77` feeds (H10b deletes the counting half and keeps `Accepts` and the record; the dead `Binding.OriginProofMetrics.Verdict` goes at H10b or H11); `DeferredEmissionTest`'s `DeferredExpansionLimit_Kd` line; the zero-reference rows |

After H8a no test reads `Bodies`, `Issues`, `ControlFlow` or `LegacyRecords`.

## 5. Verification (item 5)

- **Class: Major, with one design review** (WORKFLOW Design 4). D4 departs from R5-M188, and D3's premise is unverified: the MIR records
  of 13 methods are pinned nowhere. A fresh reviewer takes the design review. R4 reviews the implementation, the MIR-dump successors
  and the moved records, within the group review (H0).
- **Phases** (each within Q12's hour): H8a-i = commit 1; H8a-ii = commits 2–3; H8a-iii = commits 4–5.
- **Commits.** All change tests only. The compiler is unchanged, so every commit preserves its behavior, and none needs a separate
  restructuring commit.
  1. The `Issues` readers → D2 with D3's list. D4 deletes `LegacyRecords.cs`. The `Issues` and `ControlFlow` ratchets reach 0.
  2. The `Bodies` readers outside the three classes, with their golden functions (D5, D6, D8).
  3. OwnershipAnalysisTest's methods and their golden functions.
  4. EnumOwnershipTest's and MatchOwnershipTest's methods and their golden functions. The `Bodies` ratchet reaches 0.
  5. Documents and DECISIONS.
- **Unit checks:**
  - the changed classes: the 28 `T/` files of §1 and §3 (8 `Issues`, 6 `LegacyRecords` callers, 14 `Bodies`);
  - `MirDumpTest`: the goldens are rewritten with `KIMI_MIR_GOLDEN=write`, and the diff shows only the added functions' sections;
  - `MirCensusTest`, `ArchitectureRulesTest`, `-trait Purpose=Allocation` of the touched classes;
  - the build with `-warnaserror`.
- **The group's Session window** (H10a's):
  - The diagnostic snapshot shows 0 differences (no producer changes).
  - The semantic snapshot is unchanged against H10a's re-made baseline.
  - The native fixtures need no run, since no fixture call changes.
- **Expected moved expectations:** about 25 rows in the 13 `Issues` methods, each named by D3. No other expectation moves.
- **Documents (Auto):**
  - CODEMAP `:56` drops `LegacyRecords.cs`.
  - DECISIONS gets one superseding line for D4 (OQ1).
  - PLAN, in R5's landing commit: group review B N2 closes, and D3 (c)'s lost facts become open issues.
  - ARCH, STATUS and VERIFICATION are not affected (no new golden file, no tooling).
- **What R4 needs for the review:**
  - D3's per-test list (old assertion → MIR record, its kind and citation);
  - the lost facts;
  - each golden function added, or reused and named, with its `.mir` section;
  - the deleted old-subject methods with D1's reason and their MIR counterparts.

## 6. Sizes and limits (item 6)

| Part | Lines (H) |
| --- | --- |
| `Issues` readers re-pointed; TheOldLimit… deleted | ±80, −45 |
| `LegacyRecords.cs` (41) and its callers' old halves | −41, −70 |
| `Bodies` readers outside the three classes; `InternalInvariantTest.cs` (29) | −140 |
| The three classes' 43 methods and their helpers; new acceptance rows | −570, +40 |
| Golden functions (about 20–25, deduplicated) | +120 kimi, +1,300 generated `.mir` data |

Net ≈ −870/+120 t (≈ −750 t), plus ≈ +1,400 generated golden data. No compiler change. No structure limit is reached: the test files
shrink, and the goldens are data.

## 7. Open questions for R5

| # | Question | Recommendation |
| --- | --- | --- |
| OQ1 | Record D4 as an amendment of R5-M188, which is not on dev yet, or as a superseding line? (non-blocking) | A superseding line in H8a's commit 5 (AGENTS: a superseded entry stays) |
| OQ2 | Is H0c's "a MIR scaling check" for CallableRegionScalingTest met by the kept zero-allocation reruns, or does R4 want a per-axis bytes bound? That bound needs a MIR storage accessor. (non-blocking) | The zero-allocation reruns; an open issue if R4 wants the bound |
| OQ3 | Does D3's stop (a MIR record contradicting a SPEC comment) block only commit 1? (blocking only if it happens) | Yes: commits 2–4 proceed, and the stopped test is reported to R4 |
| OQ4 | Do classes named after the old storage (`BorrowDependencyStorageTest`, `CallableRegionScalingTest`) keep their names after H8a? (non-blocking) | H8c decides with its old-type names |

## 8. R5's rulings (2026-11-05)

- **Class Major with one design review** (WORKFLOW Design 4: D4 departs from R5-M188; D3's premise is unverified). H0 names R4 as
  H8a's reviewer, and WORKFLOW gives one reviewer both reviews: R4 (or a reviewer R4 names) takes the design review and the
  implementation review; the group review of G:check-tests follows at the group's end.
- **OQ1:** R5-M188 has not landed (H10a's commit 4, same group branch), so H8a's commit 5 **amends R5-M188 in place** ("`LegacyRecords`
  ends at H8a"), with the reason; no superseding entry for an unlanded one. The same commit adds the back-references to R5-M188 in
  R4-M17, R4-M136, R4-M111 and R5-M182 (H10a follow-up 2).
- **OQ2:** the zero-allocation reruns, unless R4's review asks for the bound (then an open issue, not part of H8a).
- **OQ3:** yes; commits 2–4 proceed, and the stopped test goes to R4 and R5.
- **OQ4:** H8c.
- **H10a's follow-ups folded in** (`artifacts/verify/r5-units/h10a/followups.md`): commit 1 rewords `MirTargetSide.MirSide`'s doc
  (`Analyze()` republishes on an unanalyzed version; it is not a no-op) and pins ledger row a1-c3's MIR-only `UninitializedPlace_Kd` on
  NeverContinuationTest's uniq rows (invalid by SPEC 14.10.3) at `:484` or a sibling row.
