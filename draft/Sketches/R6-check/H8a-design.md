# R6-check H8a: the old analysis's `Bodies` and `Issues` readers (design)

Status: revision 1. Revision 0 (`16224c5b7`) got REVISE from R4's design review (B1–B3, N1–N10;
`…\r4-notes\post-exit\review-h8a-design.md`). R5's rulings are in §8, and the changes are marked "(B1)", "(N4)" and so on. Unit row
(H0 §4, R5-M182): "H8a | `Bodies` and `Issues` residue (read through the entry from H5) → MIR dumps, problems or deletion; Q18's test
readers (R4 review N5) | R5 (R4 reviews) | H5, H10a | group G:check-tests | ≈ −900 t"; H0c §2 row 2; H10a-design §3.

Base: `claude/r5-h10a` head `73dbfe4dc` (H10a commits 1–5). Paths: `T/` = `tests/xUnitTest/Tests/`, `X/` = `tests/xUnitTest/`,
`M/` = `tests/mir/`. The counts are greps on the base. The MIR records in §3 come from R4's probe (review §4: a Release CLI `check`).
This session ran no build, test, probe or run.

Working time (Q12), logged in `C:\Users\bwff1\AppData\Local\Temp\claude\r5\h8a\design-state.md`: revision 0 06:06–06:22; revision 1
06:39–06:46 (2026-11-05); 23 minutes of the hour in all.

## 1. Inventory (item 1)

| Read | On the base | Where |
| --- | --- | --- |
| `.AnalyzeLegacyOwnership().Issues` (ratchet 15) | 15 reads: 13 test methods and `LegacyRecords.Of` | `X/LegacyRecords.cs:36`; `T/` BlockEscapeLoan 1, CallReservation 1, ChildMoveActivation 4, ObjectSlotUpdate 1, OwnedStoredReference 3, OwnershipJoin 1, OwnershipStorageLimit 2, ValueCallActivation 1 |
| `.AnalyzeLegacyOwnership().Bodies` (ratchet 28) | 28 reads in 14 files, 3 of them the `Body` helpers with 40 calls (OwnershipAnalysis 5, EnumOwnership 15, MatchOwnership 20); 58 test methods; 2 more `body.Issues` reads (`InternalInvariantTest:19, 24`, `MatchOwnershipTest:153`) | OwnershipAnalysis 7, CallableRegionScaling 5, BorrowDependencyStorage 3, DeferredEmission 3, and 1 each in CallArgumentMapping, ElementUpdateEmission, EnumOwnership, GenericDefault, InternalInvariant, MatchOwnership, ReturnedBorrowAncestry, StringComparisonEmission, TempProbe, WholeValue |
| `.AnalyzeLegacyOwnership().ControlFlow` (ratchet 1) | 1 | `X/LegacyRecords.cs:31` |
| `LegacyRecords.Of` (kept to H10b by R5-M188) | 7 calls in 6 files | `T/AbstractCallReachTest:376`, `ClosureEnvironmentOriginTest:296, 856`, `ClosureResultOriginTest.Makers:393`, `MirOriginsTest:264`, `NeverContinuationTest:446`, `OriginRelationDiagnosticTest:328` |
| Bare entry calls (no `Bodies` or `Issues`) | 7 | `T/OriginObligationVerdictTest:28, 33, 39`; `PipelineSwitchTest:347-349`; `OwnershipStorageLimitTest:80` |
| Benchmarks | 8 files | `src/Benchmark` (H9) |

F13 recount: H0's "H8a 28 → 29" is 28 here (the ratchet's row). H0c's "3 `Body(c)` helpers (29 calls)" is 40 calls, and its "16
`Issues` reads in 10 files" is 15 in 9 (TempProbeTest now reads `Bodies`).

**Q18's test readers (item 3; B1).** Plan Q18 names "the 21 rejecting `pair-cases.json` rows, 15 `Ownership.Issues` and 26
`OwnershipFailure` test lines"; all of them end in commits 1 and 2.
- **Commit 1, under D2.** A failure-line reader whose program has a failure the MIR publishes moves to the published record: ChildMove
  (3 lines), OwnedStoredReference (2), ObjectSlotUpdate (1), OwnershipJoin (1), ValueCallActivation (1) and `MatchOwnershipTest:153` (1,
  `WarningCoveredArm…`; B1). The `Issues` readers without a failure line whose MIR record exists move with them: CallReservation, the
  fourth ChildMove method and the third OwnedStoredReference method.
- **Commit 2, under D1.** A failure line whose failure the MIR does not publish has the old analysis itself as its subject:
  - OwnershipStorageLimit's 2 lines are the old `StorageLimit`; the MIR's own limit is H10a D10's.
  - InternalInvariantTest's 1 line is the old internal check (§3).
- `PipelineSwitchTest.TheMultiCodeHelpersAreTheOwnershipMapping` (4 lines, the `OwnershipIssue.Code` image) holds old-type names, so it
  is H8c's.
- MirCensusTest's 20 lines are MIR requirements, not readers.
- The pair-cases rows already read the published MIR record and its "loan" related location (`GenericOwnershipCaseTest.cs:39-40`).

## 2. Decisions

| # | Decision | Reason | Files |
| --- | --- | --- | --- |
| D1 | **The rule.** A reader whose subject is a rule the MIR decides moves to that rule's lowest public layer: the published record (D2), a MIR assertion (D10), or a MIR dump golden (D5). A reader whose subject is the old analysis itself (its API, plan identity, storage tables, internal checks or limit) is deleted now, and the commit names the MIR counterpart where one exists. A **rejected** program never takes D5 (B1): its rule is the published record's. | Since H5 the old analysis decides nothing (D-c1). Its consumers are these tests and the benchmarks, and H9 re-points the benchmarks, so deleting its self-tests loses no subject that any consumer relies on. Deleting now leaves H10b a pure code deletion (item 4, D-c4). A golden must have no problem (MirCensusTest). | §3 |
| D2 | **Successor of an `Issues` reader**: the published record (`DiagnosticCorpus.Check` or `TestDiagnostics` after `ReportAnalysis`), with its code, its span and the related locations the publisher makes from the MIR's facts (borrow = Anchor, loan = Use or Store, reservation = Reservation; `MirPublicationTest.ASiteFactIsARelatedLocation`). Old fact → MIR (corrected from the probe, N1): `Source` → the span (T44 for statement or declaration spans); `LoanSource` → the borrow related, and for a held loan the loan related at the holder's later use (T49); `Failure`+`Activation` → the code (`CallActivationConflict_Kd`, `CallReservationConflict_Kd` or `ComparisonLoanConflict_Kd`; T46 for codes that differ from the old); `Destroyed` → the borrow related's Place. A reservation overlap (`CallReservationConflict_Kd`) takes the T-10 form at `CallReservationTest:236`: the span is the later input's call (`Input.Call`), the borrow and loan are the earlier reservation's anchor and call (`ConflictingReservation`), and the reservation related is the later input's own. A theory row whose MIR code differs from the theory's constant moves to that code's theory (T46). | `Compilation.Problems` carries only code and subject, so the MIR's facts reach a test through the publisher. The classes' existing MIR pins already take this form (`ChildMoveActivationTest.TheChildFirstPlacementNamesThePlacedChild`, `GenericOwnershipCaseTest`, `CallReservationTest:236`), so it needs no new helper. | the 7 commit-1 `T/` files |
| D3 | **Moved expectations.** These records have been published since H5 but pinned nowhere (group review B N2). R4's probe found no acceptance move across all 31 programs, and every difference is classified (review §4). The commit names each changed expectation per test as one of three kinds. (a) The same fact. (b) A code or location already classified (T44, T46, T49 or a T-unit), cited; the comment keeps its SPEC rule and cites the classification for the location (N2: OwnershipJoin is T44, as at `BlockEscapeLoanTest:262`). (c) A lost fact: the test pins the record without it and cites T49. PLAN gets one open issue (A6, R4) that lists every (c) fact (N3, B3): the placement Note (`ChildMoveActivationTest:102-104`), the reservation related of `CallReservationTest`'s row 2 and of `AReservedStoredReferenceActivatesExclusively` row 3, and the holder's declaration. A record that breaks the test's SPEC rule itself (its count, its code family or the verdict) stops that test (§5, OQ3). | AGENTS: never weaken an expectation; WORKFLOW Review Q13. | commit messages, PLAN |
| D4 | **`LegacyRecords` ends at H8a** (amends R5-M188 in place, §8). The old halves of its callers are deleted: `OldPathGaps` and `Records().Old`, `AssertSides`' D1/D2, `ClosureEnvironmentOriginTest:296`'s old side, and `OnlyTheOldAnalysisRejects…`'s old half (the test is renamed to its MIR half). The other reads are re-pointed:<br>- `NeverContinuationTest:446` becomes `Assert.Empty(c.Problems)`, the MIR's verdict on rows that SPEC 3.1.3 makes valid (N6: the probe shows no MIR problem), and `:434`'s comment says so.<br>- `OriginRelationDiagnosticTest:328` becomes `Assert.Single(MirTargetSide.MirSide(c))` with requirement `Binding(OriginRelation)`. A declaration's relation has no body row (BU-26), so A5 records no twin and MirSide's one row is Binding's. A row that shows otherwise is reported, not branched (N4).<br>- `MirOriginsTest:264`: each of the 5 rows is checked on the base first (N5). A row whose twin is Binding's reads `c.Binding.Issues` (H11 removes it with the twins). A row whose twin was only the old reporter's pins its `Origins(c)` lines literally, since the pairing's other side is old (D1). | R5-M188's reason, "its remaining readers assert the old analysis for its own subject", no longer holds once D1 deletes those readers, so H10b finds no reader (item 4). | `X/LegacyRecords.cs` (deleted), 6 `T/` files |
| D5 | **Successor of a `Bodies` reader** on a valid program whose subject is a lowering rule (cleanup order, conditional cleanup, replacement, construction responsibility, a match's acquisition and cleanup, call evaluation order, element update order): the rule's function in the area's existing golden (`M/scopes`, `updates`, `constructors`, `matches`, `order`, `calls`). A function that already shows the rule is reused and named in the commit; otherwise one valid function is added. No new golden file. A program that becomes a golden function keeps no acceptance line elsewhere: MirCensusTest asserts it, and the class's `Supported*Verify` theory gets a row only for a program that is not a golden function (N8). | The ratchet row's own name is "R4 dump goldens". MIR is the lowest layer that shows a lowering order. One function per rule, deduplicated, keeps the generated data small. | §3, `M/*.kimi`, `.mir` |
| D6 | **Allocation tests keep every measurement.** Since H5, `c.Analyze()` runs no old analysis, so each measured lambda already measures the MIR: WarmEscapeChecks, WarmEnum, WarmMatch, WarmDefaultContexts, DependentBodiesUseCompactTables, InspectionOnlyBodies and GrowingPlansReuseBoundedStorage. Only the old reads go. Where an old read guarded that the workload exercises its form, a MIR count replaces it: GenericDefault's `DefaultContexts` → the bodies' `MirScopeKind.Inlined` scope count; DeferredEmission's operation bound → the MIR body's block count per depth, pinned. The old storage bounds are old representation and go: `BorrowStorageBytes`, `BorrowDependencyCapacity`, `LocalRegion*` and `LocalLoanFlowCapacity`. CallableRegionScaling's 8-iteration loop, vacuous since H5, goes with them. | AGENTS: never relax an allocation assertion. None is relaxed. The zero-allocation reruns are a reuse check, not a scaling check, so H8a does not claim H0c's "a MIR scaling check" (OQ2, §8). | 7 `T/` files |
| D7 | **Split by method.** H8a moves or deletes, once, each method that reads `Bodies`, `Issues` or `LegacyRecords` (directly or through a helper), together with every old name it uses, enums included. H8c takes the old names in the methods left. | One owner per method. This resolves H0c's overlap between its H8a row (the 3 `Body` helpers) and its H8c row (the five classes "deleted with their classes"). | §4 |
| D8 | **TempProbeTest's body dump becomes the MIR dump**, `MirDump.Write(c.BuildMir(), 0, text)` as in `MirBuildTest.Dump`. The tracked KIMI_PROBE dumper keeps its name, and its stale "TEMP: not committed" line is corrected. | The probe's purpose, dumping a program's ownership operations, is the MIR's now. Notice: sessions probing with KIMI_PROBE get MIR text. | `T/TempProbeTest.cs` |
| D9 | **Ratchets.** `ArchitectureRulesTest`'s rows fall commit by commit (§5): `Issues` 15 → 4 → 0, `ControlFlow` 1 → 0 at commit 2, `Bodies` 28 → 27 → 9 → 2 → 1 → 0. Its `:57` comment drops LegacyRecords. H10b turns the rows into zero-reference rows (D-c4). | The ratchets' rule. | `T/ArchitectureRulesTest.cs` |
| D10 | **MIR assertions where a golden cannot state the rule** (B2, N9). Two forms, one helper: a body's `MirDump.Write` text with its ` @` sites removed (sites are syntax ids, which a spelling or an edit changes).<br>- **Designators (B2).** `EnumOwnershipTest.CheckConstruction` asserts that each spelling's top-level MIR body equals the baseline's (`.Full(127)` or `.Empty`) and has no `MirTerminatorKind.Call`. The rule is an equality across 12 spellings, which a golden, one dump per function, cannot state.<br>- **Rebinds (N9).** `RebindingChangedStorage…` and `ChangedPayloadStorageRebuilds…` assert that after the edit and `Bind`, the MIR body equals a fresh compilation's of the edited source (`FloatEmissionTest:162`'s form). That observes the rebuilt case Types, which acceptance alone does not. | AGENTS: assert each rule at the lowest public layer that observes it; no rule loses its test. | `T/EnumOwnershipTest.cs`, `T/MatchOwnershipTest.cs` |

## 3. Readers and fates (item 2)

**Commit 1: D2 moves (Q18's MIR-published readers; B1, B3).** CLC = `ComparisonLoanConflict_Kd`, CAC = `CallActivationConflict_Kd`,
CRC = `CallReservationConflict_Kd`. The MIR record is R4's probe (review §4).

| File | Method (rows) | Subject | MIR record (kind) |
| --- | --- | --- | --- |
| ChildMoveActivationTest | AParentUsedWhileTheMovedChildsLoanIsHeldIsRejectedAtTheUse (4) | 15.6.3, 15.6.7: rejected at the write, not the call | CLC at the statement `p@follow = …`; borrow `p` (`let c = p`); loan the holder's later use (a; T44; T49) |
| | MovingAChildWhileItsOwnChildIsLiveIsRejected | the call activation's conflict | CLC at the argument `c`; borrow `c` (`let d = c`); loan `d@follow` (T46) |
| | AParentPlacedWhileItsChildIsLiveInTheLiteralIsRejected (3) | G53 placement activation | CLC at `p` of `p@move`; borrow `p`; loan the literal or `c@move` (T46; the Note is a (c) fact). Row 1 absorbs `TheChildFirstPlacementNamesThePlacedChild` |
| | AParentUsedWhileItsPlacedChildIsLiveInTheLiteralIsRejected (2) | a read while the placed child is live | CLC at the second `p@uniq` / at the literal (a / T44) |
| CallReservationTest | DiagnosticsIdentifyPreparationOrActivation (2) | 15.6.7: preparation or activation | Row 1: CAC at the call, with the reservation related (a). **Row 2 (B3):** its program does not parse (`OuterCloserInBody_Kd` at `exit to scope 3))`, a test-data defect), so the closers move to their own line. Then CRC at `c.value = 2`, borrow and loan at `c@uniq`, with no reservation related (c). Regression: each row asserts the published records are exactly the conflict, so none is a syntax diagnostic; row 2 fails on the base and passes after the fix |
| ObjectSlotUpdateTest | AStoredPayloadViewPreventsSlotReplacement (3) | replacement under a live `objref` view | CAC at `replace(…)`; borrow `owner@objref`; loan `view.id`; reservation `owner@uniq` (a) |
| OwnedStoredReferenceTest | StoredReferenceArgumentsRespectLiveChildren (3) | live children | CAC, CAC, CLC at `item.0` (a) |
| | AReservedStoredReferenceActivatesExclusively (4) | exclusive activation | Rows 1–2 CRC, which move to the CRC theory (T46); row 3 CRC at `item` with no reservation (T49, c); row 4 CAC (a) |
| | DeclaredOriginReservationsPermitReadsButStillRejectWrites | the overlap at the second lending point | CRC at `poke(item.0)`; borrow change3's `item.0`; loan the `change3(...)` call; reservation poke's `item.0` (T44, T49; D2's reservation form) |
| OwnershipJoinTest | JoinedBorrowCannotOutliveEitherLocalSource | 15.6.6, 16.2.2: one record, never at the join or the later use | one CLC at `let local = Cell.init()`; borrow `local@ref`; loan the block `do` (T44, b; N2) |
| ValueCallActivationTest | AnArgumentCannotTakeWhatTheReceiverHolds (4) | the receiver holds it | CAC with the reservation related (a) |
| MatchOwnershipTest | WarningCoveredArmParticipatesInMovedStateJoin (B1) | a warning-covered arm joins the moved state | `MovedPlace_Kd` at `s` in `let again = s@move`, beside the `UnreachablePattern_Kd` warning; the old place-state and arm reads go |

Commit 1 also carries H10a's follow-ups (§8):
- `MirTargetSide.MirSide`'s doc (N7 (1)): "`Analyze()` rebuilds and analyzes the MIR and clears the ControlFlow and Ownership
  partitions on every call; it publishes nothing (`ReportAnalysis` does)".
- The two consented comments:
  - `MirFact.cs:5-6`: "Output: MirFact values, which MirReport keeps per problem and Core's publisher (`Compilation.Publication`) reads,
    beside the test MIR side (`MirTargetSide.MirSide`), which reads the problems they belong to."
  - `MirProblem.cs:7-8`: "Output: MirProblem values, which MirReport keeps (the Reason in its key), Core's publisher
    (`Compilation.Publication`) publishes and the test MIR side (`MirTargetSide.MirSide`) reads."

**Commit 2: D1 deletions of `Issues` readers, and D4.**

| File | Method or read | Subject | Fate |
| --- | --- | --- | --- |
| BlockEscapeLoanTest | WarmEscapeChecksAllocateNothing (Allocation) | the old escape count | The line goes (D6). The MIR counts and allocation stay; `EveryOutputRelatesTheBorrow` pins the borrow related |
| OwnershipStorageLimitTest | TheOldLimitPublishesNothingBesideIndependentErrors, with the bare call `:80` | OwnershipStorage's logical limit | Deleted (D1). The MIR counterpart is D10 of H10a, `TheMirsArenaLimitPublishesBesideIndependentErrors`; `MirDataflow.Within` checks the logical size, so retained capacity has no counterpart. Follow-up 5 (N7): that theory and `MirAnalysisTest.A5ChecksTheViewsA1Completed` share one workload source, an internal member of MirAnalysisTest, with neither's rows changed |
| InternalInvariantTest | CompletionDisagreementIsAnInternalIssue | the old analysis's internal check | The file is deleted (D1). Its reads: `:17` `c.Analysis.IsVerified` is the MIR's acceptance of a trivial valid program, which every golden also gives; `:18-19` are the old body and its empty issues; `:22-23` inject the old check (`RecordCompletion`, `CheckUnreachable`); `:24-26` are the old check's record (Q18's `Internal` line). None is a MIR rule. The class's claim (a broken invariant is an internal record in every configuration) is the MIR's in `MirValidatorTest` and `MirPublicationTest.AFactThePublisherCannotPublishIsABrokenContract` |
| LegacyRecords' 7 callers | §1 | old halves; Binding records | D4, with follow-up 4: `NeverContinuationTest:484` (or a sibling row) pins `UninitializedPlace_Kd` at `y@(uniq/Never)` and `y@uniq` on the uniq rows (SPEC 14.10.3; R4 confirmed it, N7 (4)) |

**Commits 3–6: `Bodies` readers.**

| Commit | File | Methods | Subject | Fate |
| --- | --- | --- | --- | --- |
| 3 | BorrowDependencyStorageTest | DependentBodiesUseCompactTables, InspectionOnlyBodiesNeedNoLocalDependencyTable (Allocation), InspectionAndStoredLoansExecute | the old borrow tables' compactness | The old lines go (D1, D6). The MIR allocation and the native fixture stay. The lost compactness bound joins OQ2's open issue |
| 3 | CallableRegionScalingTest | GrowingPlansReuseBoundedStorage (Allocation) | the old region storage | The old lines go (D6, OQ2) |
| 3 | CallArgumentMappingTest | SemanticCallKeepsEvaluationOrderAndParameterSlots | named arguments' order and slots with defaults | D5: `M/order` or `M/calls` (`f(c: 3, a: 1)`). `CallAt` after detachment is old API (D1) |
| 3 | DeferredEmissionTest | CleanupSegmentsHaveUniqueMembershipAndDeferredEndpoints | the old plan segmentation | The loop goes (D1). `Generate` stays, and D5 adds `M/scopes` if nested `defer` is missing |
| 3 | | NestedSelfExitsAndContinuesHaveMeasuredBoundedExpansion | the expansion bound | D6: the MIR body's block count per depth |
| 3 | | AnExpansionPastTheOldLimitIsGeneratedOnEveryAnalysis | the old limit's count | Its line goes. The rest stays to H10b, which deletes `DeferredExpansionLimit_Kd` |
| 3 | ElementUpdateEmissionTest | RightSidePrecedesLocationReadAndStore | right side, location, store; loan modes | The fixture stays. D5: `M/updates` (`a[index()] += rhs()`) |
| 3 | GenericDefaultTest | WarmDefaultContextsAllocateNothing (Allocation) | nested default expansion | D6 |
| 3 | ReturnedBorrowAncestryTest | ResultAncestryUsesRecordedInputsAfterSourceDetachment | old API after detachment | The old part goes (D1), and acceptance stays |
| 3 | StringComparisonEmissionTest | ComparisonsPreserveOwnership | the old `ValidateComparisonLoans` | The loop goes (D1). The fixtures and audit stay |
| 3 | TempProbeTest | Dump | the probe | D8 |
| 3 | WholeValueTest | UpdatesRetainTheirMeaningAcrossStorageForms | replace, exchange and swap init state, local and borrowed | D5: `M/updates` (3 × 2 forms). The `OwnershipOperationKind` parameter goes |
| 4 | OwnershipAnalysisTest | ImplicitBorrowIsRecordedAndChecksMovedState | a rejected program | D2 (D1's rejected-program rule): its `c.Problems` code gains the published record's span at `show(s)`'s `s`, under D3. The old operation read goes |
| 4 | | ConditionalReplacementUsesPostRhsState, ExitCleanupFollowsDeclarationOrder…, ReturnDuringLaterArgument…, BorrowedArgumentDoesNotMoveItsSource, StaticStringTemporary…, BranchMoveMakesExitCleanupConditional, ScalarSelfAssignment…, LoopTransferCleansInnerLocal… (8) | cleanup, replacement and move rules | D5: `M/scopes` (reusing `cleanup`, `consumeTwo` and `returnInArgument` where they show the rule) and `M/updates` |
| 4 | | InputStatesReplayWithinBlocks | the old solver's input states | Deleted (D1); A1's replay is `MirInitTest`'s |
| 5 | EnumOwnershipTest | SuccessfulConstruction…, Abandonment…, NestedAbandonment…, ReturnInsidePayload…, ReturningAnEnum…, LoopTransferAbandons…, AbortDoesNotReach…, WholeReplacement…, ConditionalReplacementAndCleanup…, CompletedTemporaryRegisters… (10), and `ReturnDestruction` | enum construction responsibility and abandonment | D5: `M/constructors` |
| 5 | | the 3 designator theories through `CheckConstruction` (12 rows) | every spelling lowers as the baseline, with no call | D10 (B2) |
| 5 | | RebindingChangedStorageInvalidatesTheSupportedTypeCache | the rebuilt case Types after an edit | D10 (N9) |
| 5 | | WarmEnumAnalysisReusesStateAndPlans (Allocation) | warm reuse | D6, keeping `c.Analysis.IsVerified` false after `Bind` |
| 6 | MatchOwnershipTest | NeverSubject…, TestsReadAnIntactSubject…, CopyBindingAlso…, CleanupUsesReverseStructure…, NestedCleanup…, SubjectAndPayloadsHaveFreshLifetimes…, SubjectIsEvaluatedOnce…, TransfersSecureValue…, DeepReturnCleans…, NestedSubjects…, OuterLoopContinue…, SubjectEvaluationTemporaries…, AbortAfterAcquisition…, MatchMayPrecede…, BodyBindingsAreAcquired… (15) | a match's acquisition, cleanup and moved state | D5: `M/matches` |
| 6 | | ChangedPayloadStorageRebuildsMatchAndDecompositionPlans | the rebuilt case Types after an edit | D10 (N9) |
| 6 | | WarmMatchOwnershipAndBindingReuseStorage (Allocation) | warm reuse | D6, with `c.Analysis.IsVerified` in place of `body.IsVerified` |

## 4. The split (item 4)

| Unit | Takes |
| --- | --- |
| H8b | The 64-ratchet control-flow entry calls (for example `OwnershipAnalysisTest.ReanalysisClearsPreviousFlowResults`) and the lint reads. D4 removes `LegacyRecords`' `LintErrorRecords` read, so H8b's 14 become 13 |
| H8c | The enums and old-type names H8a leaves: `PipelineSwitchTest.TheMultiCodeHelpersAreTheOwnershipMapping`, `OwnershipStorageLimitTest.OversizedProducts…`, SharedEngineTotalityTest, MirInitTest, the five classes' other methods, and class names such as `BorrowDependencyStorageTest` (OQ4). Also OQ2's open issue: the per-axis MIR scaling bound for CallableRegionScaling and BorrowDependencyStorage |
| H9 | The 8 benchmark files |
| H10b | The entry with the callers that read neither:<br>- `PipelineSwitchTest.TheLegacyOwnershipEntryRunsOncePerVersion`, deleted with the entry;<br>- `OriginObligationVerdictTest`: only `OwnershipAnalysis.Borrows.cs:77` feeds its `Verdicts` counter, so H10b deletes the counting half and keeps `Accepts` and the record; the dead `Binding.OriginProofMetrics.Verdict` goes at H10b or H11;<br>- `DeferredEmissionTest`'s `DeferredExpansionLimit_Kd` line;<br>- the zero-reference rows |

After H8a no test reads `Bodies`, `Issues`, `ControlFlow` or `LegacyRecords`.

## 5. Verification (item 5)

- **Class: Major, with one design review** (WORKFLOW Design 4; §8). This review was R4's, and R4 also takes the implementation review.
- **Phases** (each within Q12's hour; N10), one commit each: H8a-1 to H8a-6 = commits 1–6, then commit 7.
- **Commits.** All change tests and two consented comments only. The compiler's behavior is unchanged, so no separate restructuring
  commit is needed.
  1. D2 moves of Q18's MIR-published readers, with B3's fix and regression; H10a follow-ups 1 and 3 (M); `Issues` ratchet 15 → 4.
  2. D1 deletions (BlockEscape's line, TheOldLimit…, InternalInvariantTest) with follow-up 5; D4 deletes `LegacyRecords.cs` and
     re-points its callers, with follow-up 4. `Issues` → 0, `ControlFlow` → 0, `Bodies` → 27.
  3. The `Bodies` readers outside the three classes, with their golden functions (D5, D6, D8). `Bodies` → 9.
  4. OwnershipAnalysisTest, with its golden functions. `Bodies` → 2.
  5. EnumOwnershipTest: golden functions and D10. `Bodies` → 1.
  6. MatchOwnershipTest: golden functions and D10. `Bodies` → 0.
  7. Documents and DECISIONS.
- **Under a stop** (OQ3; N10). The stopped test keeps all its old reads in that commit. The ratchet row records the count with it, the
  commit names it, and the test goes to R4 and R5. A class is moved whole or not at all, so none is left half-moved without a record.
  The other commits proceed.
- **Unit checks:**
  - the changed classes: the 28 `T/` files of §1 and §3 (8 `Issues`, 6 `LegacyRecords` callers, 14 `Bodies`) and MirAnalysisTest;
  - `MirDumpTest`: the goldens are rewritten with `KIMI_MIR_GOLDEN=write`, and the diff shows only the added functions' sections;
  - `MirCensusTest`, `ArchitectureRulesTest`, `-trait Purpose=Allocation` of the touched classes;
  - the build with `-warnaserror`.
- **The group's Session window** (H10a's):
  - The diagnostic snapshot shows 0 differences (no producer changes).
  - The semantic snapshot is unchanged against H10a's re-made baseline.
  - The native fixtures need no run, since no fixture call changes.
- **Expected moved expectations:** the §3 commit-1 rows (about 30), each named by D3, plus OwnershipAnalysisTest's span. No acceptance
  move: R4's probe confirmed it for all 31 programs.
- **Documents (Auto):**
  - CODEMAP `:56` drops `LegacyRecords.cs`.
  - DECISIONS: the R5-M188 amendment and the back-references (§8), in commit 7.
  - PLAN, in R5's landing commit: group review B N2 closes. One A6 open issue lists D3's (c) facts. OQ2's open issue goes to H8c.
  - ARCH, STATUS and VERIFICATION are not affected (no new golden file, no tooling).
- **What R4 needs for the review:**
  - D3's per-test list (old assertion → MIR record, its kind and citation);
  - the (c) facts;
  - each golden function added, or reused and named, with its `.mir` section;
  - D10's two forms;
  - the deleted old-subject methods with D1's reason and their MIR counterparts.

## 6. Sizes and limits (item 6)

| Part | Lines (H) |
| --- | --- |
| Commit 1: the readers re-pointed, B3's row | ±90 |
| Commit 2: TheOldLimit…, BlockEscape's line, `InternalInvariantTest.cs` (29), `LegacyRecords.cs` (41) and the old halves; one workload source | −185, +10 |
| Commit 3: the `Bodies` readers outside the three classes | −110 |
| Commits 4–6: the three classes' 43 methods and their helpers; D10's helper and assertions | −570, +45 |
| Golden functions (about 20–25, deduplicated) | +120 kimi, +1,300 generated `.mir` data |

Net ≈ −865/+145 t (≈ −720 t), plus ≈ +1,400 generated golden data and two comment lines in Mir. No structure limit is reached: the test
files shrink, and the goldens are data.

## 7. Open questions for R5

| # | Question | Recommendation |
| --- | --- | --- |
| OQ1 | Record D4 as an amendment of R5-M188, which is not on dev yet, or as a superseding line? (non-blocking) | Ruled: an in-place amendment (§8) |
| OQ2 | Is H0c's "a MIR scaling check" for CallableRegionScalingTest met by the kept zero-allocation reruns, or does R4 want a per-axis bytes bound? (non-blocking) | Ruled: R4 wants the bound. It becomes an open issue placed with H8c (§8) |
| OQ3 | Does D3's stop block only the stopped test's commit? (blocking only if it happens) | Ruled: yes, with §5's state under a stop |
| OQ4 | Do classes named after the old storage (`BorrowDependencyStorageTest`, `CallableRegionScalingTest`) keep their names after H8a? (non-blocking) | Ruled: H8c |

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

### Rulings on R4's design review (REVISE, 2026-11-05; revision 1)

- **B1:** `MatchOwnershipTest.WarningCoveredArmParticipatesInMovedStateJoin` moves under D2 in commit 1. It pins the published
  `MovedPlace_Kd` at `s` in `let again = s@move`, beside the `UnreachablePattern_Kd` warning. §1, §3 and §5 now agree on Q18's readers:
  every Q18 failure-line reader whose program has a failure the MIR publishes goes in commit 1 under D2. A reader whose subject is the
  old analysis's own internal check is deleted under D1, and §3 states which InternalInvariantTest reads are which. D1 gains the rule
  that a rejected program never takes D5, which also moves `OwnershipAnalysisTest.ImplicitBorrow…` to D2.
- **B2:** the designator rule stays observed by a MIR assertion in EnumOwnershipTest (D10): each spelling's MIR body equals the
  baseline's and has no call. A golden cannot express a cross-spelling equality, and the public dump states it once its syntax-id sites
  are removed.
- **B3:** the row's program is fixed so that it parses (a test-data defect). The published record is pinned as it stands:
  `CallReservationConflict_Kd` at `c.value = 2`, with borrow and loan at `c@uniq`. The missing reservation location is a D3 (c) lost
  fact, an open issue (A6, R4) in the PLAN list. The fix's regression is that the row asserts no syntax diagnostic.
- **OQ2 (R4's decision):** R4 wants the per-axis bytes bound. It becomes an open issue outside H8a, placed with H8c, together with
  BorrowDependencyStorage's lost compactness bound (review §5). H8a keeps the zero-allocation reruns and does not claim the check.
- **R4's non-blocking findings:** N1, N2, N4, N5, N6, N7, N8, N9 and N10 are adopted (marked in §2–§5). N3 is adopted together with
  B3's ruling: each (c) fact cites T49, and PLAN gets one A6 row that lists them, not a row per fact. None is declined.
- **H10a follow-ups:**
  - M consented on the board (06:26) to the `MirFact.cs:5-6` and `MirProblem.cs:7-8` comment edits. They go in H8a's commit 1, naming
    Core's publisher (`Compilation.Publication`) as the product reader beside `MirTargetSide.MirSide` (texts in §3). BU's two files
    wait.
  - Follow-up 1's wording follows N7 (1): `Analyze` publishes nothing, so the earlier "republishes" text is corrected.
  - N10's pre-split renumbers the commits:
    - The earlier "commit 1" is now commits 1–2.
    - Follow-up 4 goes in commit 2, with NeverContinuationTest's other edit.
    - The R5-M188 amendment and its back-references, the earlier "commit 5", are now commit 7.
    - "Commits 2–4 proceed" (OQ3) now reads "the other commits proceed".
  - Follow-up 5 is taken in commit 2, since H8a touches OwnershipStorageLimitTest.

### R5's fixes after design review 2 (R4's re-check 1: REVISE, B4; WORKFLOW Review table: the planning session fixes the design)

- **B4:** D10's MIR-dump comparison covers the 12 designator spellings only. `EnumOwnershipTest.RebindingChangedStorageInvalidatesTheSupportedTypeCache`
  (`:229`) and `MatchOwnershipTest.ChangedPayloadStorageRebuildsMatchAndDecompositionPlans` (`:324`) keep their `AdtDef.CaseTypes`
  assertions (NotSame, 2 cases, the new case's Ref and Static component: a Types fact, not the old analysis's); only their old-analysis
  reads go (D1's scope: a read of `Bodies`, `Issues` or `LegacyRecords`, nothing else). Cause judged by R5: B2 left a MIR-observed rule
  without a successor; B4 moved a non-old-analysis assertion into the wrong layer (a scope error against D1). Different causes, so
  WORKFLOW's shared-cause stop does not apply.
- **N11:** D10 names the `M/constructors` function that is the baseline (or pins its text). **N12:** the stripping helper sits beside
  `MirDumpTest.Dump`. **N13:** §8's first block is superseded where the later rulings differ. **N14:** commit 6 may run as its own phase
  under Q12 if the hour is short.
- The implementation review (R4's reviewer, the same one) first checks B4 (WORKFLOW Repeated reviews 1).
