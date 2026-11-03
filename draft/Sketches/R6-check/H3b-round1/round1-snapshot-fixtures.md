# H3b round 1: input kinds 1 and 4 (snapshot differences, fixtures, milestones and CLI)

Head `4aced0623` (`claude/r6-flip` `1826074d5` + S `93762d21e`), base `5998a0ec0`. Method: H0b §5 (classes §5.3, maps §5.4, form §5.5);
the decisions listed in `classify-common.md`. Evidence only: no code, test, build, run or commit. Inputs:
`artifacts/verify/r6-h3b/round1/` (snapshot run `20261103-113022-505-…`, `fixtures-diff.tsv`, milestones run `20261103-112634-138-…`,
`round-logs/harness`), the base ledger `artifacts/verify/shared-base/5998a0ec0/mir-diff/` (`ledger.tsv`, `raw-55232.tsv`, `sources/`),
Q19's `artifacts/verify/r6-readings-2/` (`snapshot-flip.tsv`, `flip-tests.tsv`, `signature-map.tsv`), and the head's test sources.

## Table

S = snapshot difference (input 1); F = fixture only in the base (input 4; each program's `.ll`, `.exit`, `.observation`, `.stderr` and
`.stdout`, so 40 programs = 200 files); M = milestones and CLI (input 4). "Emits" says why the head has no fixture: **dropped** = the head's
test no longer emits it (a test-only commit removed the emission because the MIR rejects the program, and that test passes);
**rejected** = the emitting test is among the 716 failures and fails at its acceptance assertion (`Analysis.IsVerified = False` with the
records named; Binding complete), so the program is now rejected. No fixture is missing because a test failed for another reason.

| # | input | signature | rows (programs) | example | class | category | clause | named row | rule W | owner | action |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | snapshot `code`, added | A1 `UninitializedPlace_Kd` (Ownership.UninitializedUse), FunctionKoto `10:12-12:27` + BinaryKoto `11:9-11:38`, on a program rejected by Binding's `Unsupported_Kd` (`Weak<rc/Node>`) | 2 (1: `milestone/Milestone41`) | Milestone41 `Node.init` | 4 | d | 23.1.2 | PLAN G105 ("Milestone 41's A1 rows"); ledger `incomplete new mir A1` (A6, 2g-1); R5-M184 / R5-M182's line (direct publication until 2g-1); Q19 `snapshot-flip.tsv` predicted both | dep | R4 | count (UninitializedPlace_Kd +2); snapshot baseline only; no failing test |
| S2a | snapshot `code`, removed | old U `Unsupported_Kd` (Ownership.Unsupported) at a static place use, IdentifierNameKoto | 16 (1: `milestone/Milestone42`) | `33:22-33:30` (`Registry` in `Registry.count = …`) | 1x | a | 22.4, 15.6 | ledger `complete missing old U Unsupported_Kd IdentifierNameKoto` (a; PLAN G106; 2b-8); Q19 predicted all 21 | - | R4 | none; still rejected by the 5 declaration-site `Unsupported_Kd` (P42) |
| S2b | snapshot `code`, removed | as S2a, MemberAccessKoto | 1 (Milestone42) | `38:5-38:22` (`Registry.replaced = …`) | 1x | a | 22.4, 15.6 | ledger `complete missing old U … MemberAccessKoto` (a; G106; 2b-8) | - | R4 | none |
| S2c | snapshot `code`, removed | as S2a, GenericsKoto | 4 (Milestone42) | `41:34-41:44` (`Cache<i32>`) | 1x | a | 22.4.4 | ledger `complete missing old U … GenericsKoto` (a; P42); H0b §5.4 "Milestone 42's first diagnostic": the first record (`13:12-13:52`, `Unsupported_Kd`) is unchanged, so no re-baseline | - | R4 | none |
| S3 | snapshot `code`, added | A3 `RequireFallthrough_Kd` (ControlFlow) at a failure body that parser recovery supplied, on a program both reject for a syntax record | 8 (8: syntax `guessed-body-in-require-body`, `guessed-require-body`, `require-and-symbol`, `require-missing-else`, `require-missing-failure-body`, `require-missing-failure-expression`, `require-written-body-without-else` (second record); mutation `symbolic-and`) | `syntax/require-missing-else` `3:19-3:19` | 4 | d | 14.11, 23.1.2 | none exact; nearest: PLAN G105 (A3 at rejected nodes), R5-M184's direct-publication line (Binding failures). Not predicted by Q19 (the base ledger has no row for these programs) | dep (rests on `MissingSyntax_Kd` / `MisplacedSyntax_Kd` / `ExpectedSyntax_Kd`) | R4 (cause: `MirLints` cannot see that the body was supplied); R5 (whether R5-M184's line covers syntax-rested records) | count (RequireFallthrough_Kd +8) **or** R4 restores the derivation. The corpus rows' `Intended` text and `RequireRecoveryDiagnosticTest` pin the old derived reading (see "Failing tests") |
| F1 | fixture, dropped (O `f29325406`) | A5 `ComparisonLoanConflict_Kd`: writing a part ends no Loan (OldRule P1) | 11 (LocalRegionComposition `FieldEnded`, `InitializerAliasEnded`, `TuplePartEnded`, `TuplePartsEnded`, `NestedPartsEnded`, `FixedPartsEnded`, `TupleBranchEnded`, `TupleCopyAfterParts`; FixedOriginRetentionPart `Array`, `Struct`, `Tuple`) | `LocalRegionCompositionTest.CompleteAndPartialReplacementEndOnlyTheOldLoans` (`a = 3`) | 1r | d | 15.6 (holders; A5 P1) | OldRule P1 (DECISIONS R4-M136), H5 split row O | - | R4 confirms; 1r list to the owner | none (the tests pass on the MIR's record) |
| F2 | fixture, dropped (O) | A5 `ComparisonLoanConflict_Kd`: a write through a reference ends no Loan (OldRule S4) | 2 (LocalRegionCompletion `IndirectReplacement`, `IndirectLoop`) | `LocalRegionCompletionTest.CompletedRegionsComposeWithoutReopeningTheirType` (`n = 3`) | 1r | d | 15.6 (S4) | OldRule S4 (R4-M136), row O | - | R4; 1r list | none |
| F3 | fixture, dropped (O; B12 by S `93762d21e`) | A5 conflict with a live child: Loan identity by base (OldRule S1) | 7 (DictionaryStoredReference `CompoundTuple`, `Literal`, `Local`, `String` (`CallActivationConflict_Kd` at `entries.clear()`), `ExclusiveChild` (`ComparisonLoanConflict_Kd`); `StoredReferenceLoanTransferred` (`ComparisonLoanConflict_Kd` at `value@move`); `GenericCaseB12parentmove` (`ComparisonLoanConflict_Kd` at `let w = value@move`)) | `StoredReferenceLoanTest.AChildRetainsItsInputAfterTransferringTheParentReference` | 1r | d | 15.6.3 (A5 S1) | OldRule S1 (R4-M136); pair-cases B12's `Old` (`Rule: S1`) removed by S | - | R4; 1r list | none for the emitters; related failure `GenericOwnershipCaseTest.JudgesEveryCaseAtDefinition(B12-parent-move)` (kind 2) |
| F4a | fixture, rejected | A5 `ComparisonLoanConflict_Kd` (requirement Assign) after `replace`/`exchange`/`swap` or a write through `slot@uniq` | 4 (ContentUpdate `Replace`, `Assignment`, `Exchange`, `Swap`) | `ContentUpdateTest.UpdatesTransferOnlyTheOldOrNewContentsLoans(Replace)` | 1r | d | 15.6 holders bullet ("`replace`, `exchange` and `swap` completely replace no holder"), 15.7.3 | ledger `complete acceptance mir A5 ComparisonLoanConflict_Kd BinaryKoto` (tagged b: plan §9.2, R4-M50, G147; the same signature holds O's S4 rows); Q19 predicted | - | R4 confirms the per-row verdict (if R4 reads 15.7.3 otherwise: class 2, (c), the owner) | test re-point: expect the MIR's conflict at `a@follow = 9` / `b@follow = 9`, no fixture (as O did) |
| F4b | fixture, rejected | A5 `PlacementActivationConflict_Kd` (CompoundAssign) at `a@follow += 1` after `replace` in a loop | 1 (`ContentUpdateLoop`) | `ContentUpdateTest.ReplacementEndsOldDependenciesOnLoopBackEdges` | 1r | d | 15.6 (a call completely replaces no holder; a compound assignment ends none) | ledger `complete acceptance mir A5 PlacementActivationConflict_Kd BinaryKoto` (d; plan §9.2, R4-M50); Q19 predicted | - | R4; 1r list | test re-point: expect the conflict, no fixture |
| F5 | fixture, rejected | A5 `ComparisonLoanConflict_Kd` GenericsKoto at `make(Raw.allocate<u8>)` | 1 (`CompilerReferenceCallable`) | `CompilerFunctionReferenceTest.ExecutesRawAllocation(Callable)` | 1r | d | 5.7, 15.6.4 | ledger `complete acceptance mir A5 … GenericsKoto` (d; R4-M95 D6, R4-M116 N-S1, R4-M120); Q19 predicted | - | R4; 1r list | test re-point: expect the conflict, no fixture |
| F6 | fixture, rejected | A5 `ComparisonLoanConflict_Kd` ×3 (IdentifierNameKoto + FieldKoto ×2) | 1 (`MemberThroughLayers`) | `MemberThroughLayersTest.FieldsAndTupleElementsAreSelectedThroughLayers` | 1r | a | 3.6, 15.4.3, 15.6.3, 15.6.4 | ledger `complete acceptance mir A5 … IdentifierNameKoto` (a; owner 2026-10-18, plan §9.2, R4-M50); the two FieldKoto records `new mir` (b; A6 unit 1c); Q19 predicted | - | R4; 1r list | test re-point: expect the records, no fixture |
| F7 | fixture, rejected | A5 `ComparisonLoanConflict_Kd` (Assign) through a constructed holder (`Cell.init(link: slot@uniq)`) | 2 (`GenericCaseA4boxok`, `GenericCaseA4fieldok`) | `GenericOwnershipCaseTest.ValidFamiliesRunInEveryInstance(A4-box-ok)` | 2 | c | 15.6 (P1/P10), 8.15 | PLAN G147 (8ii) ("a SPEC reading is pending"); on S's own list | - | the owner (SPEC reading); R4 cause | stays until the reading; then fix or accept |
| F8 | fixture, rejected | A5 `ComparisonLoanConflict_Kd` TypeSemanticsKoto at `S` of `count<S>(items: S)` | 1 (`IntegerRangeResolved`) | `IntegerRangeTest.Executes(Resolved)` | 2 | b | 15.6, 8.15 | ledger `complete acceptance mir A5 … TypeSemanticsKoto` (b; X-1 R4-M80, A6 2g-4b, F7 2c, G147); base source `415c46cf5501`; Q19 mis-attributed it to Binding tests | - | R4 | stays until R4's fix or the owner's acceptance |
| F9 | fixture, rejected | A5 `ComparisonLoanConflict_Kd` (Deref) for a closure store into a captured `holder@uniq` (`value`, `consuming`; the `named` row passes) | 2 (`FixedOriginRetentionStorevalue`, `FixedOriginRetentionStoreconsuming`) | `FixedOriginRetentionTest.FixedCallStoresRetainTheActualArgument(value, false)` | 2 | b | 15.6.4 | ledger `complete acceptance mir A5 … ConversionKoto` (b; X-1 R4-M80, A6 2g-4b, F7 2c, G147); Q19 predicted | - | R4 | stays |
| F10 | fixture, rejected | A1 `StaticMovePathRequired_Kd` ParenthesizedKoto at `(*lens.at)@move` | 1 (`RawOriginLegacyLensWrite`) | `RawStorageTest.RawOriginsGenerateOnTheDefaultPipeline(RawOriginLegacyLensWrite)` | 2 | b | 5.2.3, 15.1.5 | ledger `complete acceptance mir A1 StaticMovePathRequired_Kd ParenthesizedKoto` (b; PLAN G222); base source of raw 55232.463 | - | R4 | stays |
| F11 | fixture, rejected | U `Unsupported_Kd` ConversionKoto (requirement Borrow) on a raw `during self` Field borrow | 2 (`RawOriginLegacyPointerWrite`, `RawOriginLegacyBlockShared`) | `RawStorageTest.RawOriginsGenerateOnTheDefaultPipeline(RawOriginLegacyPointerWrite)` | 2 | e | 5.6, 5.7, 15.6 | ledger `complete acceptance mir U Unsupported_Kd ConversionKoto` (b; R4-M147 A3 E16, G128, R4-M120 PQ-10); the owner's Q-O3 D5 exception (BU-24). H0b §6 predicted "identical verdicts" for Q-O3: the prediction fails | - | the owner (acceptance list); R4 cause | stays until fixed or accepted |
| F12 | fixture, rejected | U `Unsupported_Kd` SyntaxFormKoto (requirement Syntax) in a match on a raw Case payload | 2 (`RawOriginLegacyBlockCaseMatch` ×2 records, `RawOriginLegacyCaseFollow`) | `RawStorageTest.RawOriginsGenerateOnTheDefaultPipeline(RawOriginLegacyCaseFollow)` | 2 | e | 5.7, 15.6 | ledger `complete acceptance mir U Unsupported_Kd SyntaxFormKoto` (b; R4-M120 PQ-10, X-2 PD28); Q-O3 as F11 | - | the owner; R4 cause | stays |
| F13 | fixture, rejected | U `Unsupported_Kd` TypeSemanticsKoto ×2 at a written Function Type with Origins (`let f: (ref/Array<…>) -> ref/(…)`) | 1 (`ReturnedReferenceNestedReference`) | `DereferenceUpdateTest.ReturnedReferencesAreFollowed(NestedReference)` | 2 | e | 15.3.4, 15.6.4, 15.6.5 | ledger `complete acceptance mir U … TypeSemanticsKoto` (b; 2a-1a R4-M113, G216 (2c-D), G195 (I-1)); base source `08a0d0b25b7b` | - | the owner; R4 cause | stays |
| F14 | fixture, rejected | U `Unsupported_Kd` FormattingKoto (requirement Unset) at `$tryWrite(do => return 7, …)` | 1 (`Utf8TryWriteFirstNever`) | `Utf8TryWriteTest.FirstOperandCanTransferControlWithoutAcquiringAWriter` | 2 | e | 15.3.4, 15.6.4 | ledger `complete acceptance mir U … FormattingKoto` (b; G216 (2c-D), G195 (I-1)); Q19 predicted | - | the owner; R4 cause | stays |
| F15 | fixture, rejected | A5 `ComparisonLoanConflict_Kd` ×3 (Name, Let, Let) with `$tryWrite(acquire(writer@uniq), …)` | 1 (`Utf8TryWriteAcquire`) | `Utf8TryWriteTest.BorrowedWriterExpressionEvaluatesOnce` | 5 | b | 15.6.3, 15.6.4 | none: the program is not in the base ledger (a `Purpose=Allocation` test, outside the ledger's Functional run), so Q19 could not predict it | - | ? (R4 best) | best reading: class 2 (b), a MIR false rejection of a valid acquire-and-write; R4 to state whether 15.6's holders make it invalid (then 1r) |
| M1 | milestones, CLI | 40 harnessed milestones (1-38, 40, 44) PASS on the default pipeline (the MIR decides at the head); five CLI harnesses (library, modules, testing, emission, cli) exit 0 | 0 | `round-logs/status.txt` | - | - | - | - | - | - | none: no milestone verdict, first diagnostic or native result changed. Milestones 41 and 42 (not harnessed) are S1 and S2 |

## Summary

Rows (signatures) per class: 1r 7 (F1-F6, 27 programs); 1x 3 (S2a-c, 21 records); 4 2 (S1, S3; 10 records); 2 8 (F7-F14, 12 programs);
5 1 (F15, 1 program); none 1 (M1). Classes 1a and 3: 0. Total 21 classified rows over 31 snapshot records and 40 fixture programs.

Per category: (a) 4 (S2a-c, F6); (d) 8 (S1, S3, F1-F5 with F4a/F4b); (b) 4 (F8, F9, F10, F15); (c) 1 (F7); (e) 4 (F11-F14).

Per code:

| code | snapshot records | fixture programs | rows |
| --- | --- | --- | --- |
| `Unsupported_Kd` | -21 (1x) | 6 (2, e) | S2a-c, F11-F14 |
| `UninitializedPlace_Kd` | +2 (4) | - | S1 |
| `RequireFallthrough_Kd` | +8 (4) | - | S3 |
| `ComparisonLoanConflict_Kd` | - | 28 (1r 22, 2 5, 5 1) | F1 (11), F2 (2), F3 (3), F4a (4), F5, F6, F7 (2), F8, F9 (2), F15 |
| `CallActivationConflict_Kd` | - | 4 (1r) | F3 |
| `PlacementActivationConflict_Kd` | - | 1 (1r) | F4b |
| `StaticMovePathRequired_Kd` | - | 1 (2) | F10 |

Fixture programs by primary code: 28 + 4 + 1 + 1 + 6 = 40 (F6 and F15 have three conflicts each, F12's BlockCaseMatch and F13 two
`Unsupported_Kd` each). Class 4 counts for H5's commit message: `UninitializedPlace_Kd` +2, `RequireFallthrough_Kd` +8 (S3 only if R5
keeps it at 4).

### Class 2 rows (block H5 until fixed or accepted as one owner list)

| # | programs | owner |
| --- | --- | --- |
| F7 | GenericCase A4-box-ok, A4-field-ok | the owner (G147 (8ii) SPEC reading; (c)); R4 cause |
| F8 | IntegerRangeResolved | R4 (fix) |
| F9 | FixedOriginRetentionStore value, consuming | R4 (fix) |
| F10 | RawOriginLegacyLensWrite (G222) | R4 (fix) |
| F11 | RawOriginLegacyPointerWrite, RawOriginLegacyBlockShared | the owner (acceptance of a located `Unsupported_Kd`, (e)); R4 cause |
| F12 | RawOriginLegacyBlockCaseMatch, RawOriginLegacyCaseFollow | the owner ((e)); R4 cause |
| F13 | ReturnedReferenceNestedReference (G216 2c-D) | the owner ((e)); R4 cause |
| F14 | Utf8TryWriteFirstNever (G216 2c-D) | the owner ((e)); R4 cause |

### Class 5 rows (stop H5)

| # | programs | owner |
| --- | --- | --- |
| F15 | Utf8TryWriteAcquire | ? (R4 best; best reading class 2 (b)) |

No new `InternalInvariant_Kd`, no severity change, no acceptance change and no `Outcome` change in the snapshot (432 entries on both
sides). G121's named class-5 row does not appear in these inputs.

## Failing tests named by these rows (by name only; kind 2 classifies them)

- S3 (if counted, the expectations gain one `RequireFallthrough_Kd` each, and the derived code turns into the direct one; this weakens
  a rule-W pin, so R5/R4 decide before any re-point): `SyntaxDiagnosticTest.ACasePublishesExactlyItsExpectedRecords` for
  `require-and-symbol`, `require-missing-else`, `require-missing-failure-body`, `require-missing-failure-expression`,
  `require-written-body-without-else`, `guessed-require-body`, `guessed-body-in-require-body` (7);
  `RequireRecoveryDiagnosticTest.AGuessedFailureBodyRestsOnTheSyntaxError` ×6 (expects `PrerequisiteUnavailable_Kd`, gets
  `RequireFallthrough_Kd`), `.AWrittenFailureBodyIsJudgedOnItsOwn`, `.AFailureBodyInARejectedExpressionIsJudgedOnItsOwn` (8).
- 1r re-points (expectation changes from "accepted and run" to the MIR's record with no fixture, if R4 confirms):
  `ContentUpdateTest.UpdatesTransferOnlyTheOldOrNewContentsLoans` ×4, `ContentUpdateTest.ReplacementEndsOldDependenciesOnLoopBackEdges`,
  `CompilerFunctionReferenceTest.ExecutesRawAllocation(Callable)`, `MemberThroughLayersTest.FieldsAndTupleElementsAreSelectedThroughLayers`.
- 1r, dropped fixtures: no emitting test fails. Related: `GenericOwnershipCaseTest.JudgesEveryCaseAtDefinition(B12-parent-move)` (it reads
  `AnalyzeLegacyOwnership().Issues`, which keeps the old S1 rule, for a SPEC rejection).
- Class 2, expectation unchanged until the fix or acceptance: `GenericOwnershipCaseTest.ValidFamiliesRunInEveryInstance(A4-box-ok)`,
  `(A4-field-ok)` (also `JudgesEveryCaseAtDefinition` for both); `IntegerRangeTest.Executes(Resolved)`;
  `FixedOriginRetentionTest.FixedCallStoresRetainTheActualArgument(value, False)`, `(consuming, False)`;
  `RawStorageTest.RawOriginsGenerateOnTheDefaultPipeline` for `RawOriginLegacyLensWrite`, `…PointerWrite`, `…BlockShared`,
  `…BlockCaseMatch`, `…CaseFollow`; `DereferenceUpdateTest.ReturnedReferencesAreFollowed(NestedReference)`;
  `Utf8TryWriteTest.FirstOperandCanTransferControlWithoutAcquiringAWriter`.
- Class 5: `Utf8TryWriteTest.BorrowedWriterExpressionEvaluatesOnce`.

## Limits of this round's evidence

- Natives did not run (the Session's test step failed), so "a changed native result on a program both accept" is unobserved except
  for the 40 milestones. `compare-fixtures.ps1` refused the failed run; `fixture-diff.py` stood in: 0 files changed and 0 only in the
  head over 30,493 shared fixtures, so no `.ll` changed on a program both accept.
- Ledger categories are per signature. F4a's signature carries (b) although its SPEC text reads as 1r; R4's per-row verdict decides.
- Q19 read `62cbf64f0`; S3 and F15 were not predicted, and F8's test attribution there was wrong.
