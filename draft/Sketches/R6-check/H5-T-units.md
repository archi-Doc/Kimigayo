# H5 T-units: the test re-point plan (R6-check H3b round 1)

Standing request: R5 (the planning session) leads R6-check (H0 owner-approved, DECISIONS R5-M182). H5 is on `claude/r6-flip`. This is the
T-unit plan of H5-split §1 row "T1…Tn" and §4: test re-points on the branch, file-disjoint, about 25 edits (one hour) each, one review
each. Planning only: no code, no test edit, no build or run, no commit. Working record, not authority.

- Inputs: dev `aa70507e0` `draft/Sketches/R6-check/H3b-classification.md`, `H3b-round1/round1-tests.{md,tsv}`, `H5-split.md`; R4's
  `r4-notes/post-exit/h3b-r4-verdicts.md`, `h3b-A.md`, `h3b-B.md`, `brief-h3b-u{a2,b1}.md`; test sources at head `4aced0623`.
- Coordinator additions (2026-11-03): BU's three InstanceCollector conditions; T60 is BU's G409 (out); the owner accepted the 1a list
  (「推奨どおり」), so T10-T15, T62 ×5, NeverContinuation and InstanceCollector's frame row are in, with acceptance plus evidence; the
  owner-accepted (e) rows keep their expectations (H5's (e) list).
- Paths: `T/` = `tests/xUnitTest/Tests/`; `D/` = `tests/diagnostics/`. (H) marks an estimate not checked against the source.

## 0. Scope and counting

| Set | Rows (tests) | Groups |
| --- | --- | --- |
| class 3 | 362 | T16 2, T35 4, T40-T49 354, T34 13.7.1/0 2 (VerifiedSpecExample and HirForms) |
| class 4 | 161 | T50 74, T51 87 |
| 1x | 20 | T20 17; T63 prelude static 1, InstanceCollector "self joined", "self assigned" 2 |
| 1r | 27 | T01-T04 21; T30 ContentUpdate ×5, CompilerFunctionReference(Callable) 1 |
| (e) decided | 2 | T31c truncateGenOk, removeAllGenOk (R4-M147 D″) |
| 1a (owner, 2026-11-03) | 84 | T10 19, T11 17, T12 10, T13 5, T14 10, T15 16, T62 5, T63 NeverContinuation 1, InstanceCollector frame 1 |
| **total** | **656** | 101 test files plus `D/spec-cases.json` (and `D/syntax.json` only if R6-1 leaves a row failing) |

Out: T4A, S3 and T63's require row (R6-1 fixes them, no re-point); T60 (G409); class 2 (b) rows R4 fixes on dev (T30 ClosureReceiver ×4,
T31b, T32b, T36, T63 DictionaryStoredReference ×2); T61 (R5's branch fix, no test edit); the owner's (e) list, which keeps its
expectations: GXS (F9 ×2, InputDependentValueCall, F15, MemberThroughLayers, F8), G147 (8) (PairCaseFamilies, GenericOwnershipCase
A4-box-ok/A4-field-ok ×4), PQ-10 (T31a), the depth bound (T31d), F13, F14, G220 (T32a ×6), T34 7.7.2/0.

**Edits** count distinct source edits, not failing tests: one theory row, one method body or helper, one JSON variant, one moved row.
They come from the parameter lists of the failing rows and the assertion lines read at the head: a row whose expectation is a theory
argument (`at`, `records`, `note`, a code) is one edit, a theory whose expectation is in the method body is one or two. Total ≈ 520
(H; class 3/4 ≈ 445, 1a ≈ 75).

## 1. Units

Reviewer rule (brief): R4 for A-cause families (A1-A6 records: location, code, evidence, counts) and for R5's own rows (T16, T43, the 1x
rows, T34's data fix); BU for the Binding-cause effect families T40-T42. R4 also reviews the 1a acceptances (its verdicts, h3b-A/B). BU
reads the InstanceCollector rows (its consent, BU-60). R5 writes every unit (ruling 6; R4 offered to write ContentUpdate, CFR, T35, T31c).

Waits: a unit waits for a fix when the fix edits one of its files, or when the fix's analysis changes the kind of record its rows pin:
U-A2 (A2 `MirOrigins.Chains`, Fixed meet; adds `MirOriginsTest` rows; Origin-relation records), U-A3 (A5 activation check sets; adds
activation records; its own rows are in `DictionaryStoredReferenceTest`), U-B1 (A1 `MirMoveData.Resolve`; RawPlaceAccess/RawStorage),
U-B2 (A4 Requirement-call branch on a concrete Self; adds a `MirEffectsTest` row; A4 effect records), U-A1 (A5 closure receiver store W;
`ClosureReceiverClassificationTest`), R6-1 (A3 `RequireFallthrough_Kd` at a recovery's guess), G409 (BU: Binding's closure-header Origin
rows; `ClosureEnvironmentOriginTest`), T61 (the 576 B; an Allocation-purpose row). "Ledger check (H)" means no file overlap and an
unlikely record move: the unit starts now, and its reviewer reads the fix's mir-ledger moves before approving; a moved program in the
unit's files returns to the unit. U-B3, U-B4, U-B5, GXS, U-O3 land after H5 under the (e) list: no T-unit waits for them, and they
re-point what they move themselves.

| id | family / rows | test files (`T/`) | failing tests | edits (est.) | reviewer | waits for | est. time |
| --- | --- | --- | --- | --- | --- | --- | --- |
| T-01 | 1r A5: T01 ×13, T02, ContentUpdate ×5, CFR(Callable); T31c ×2 (e); T51 ×1 | `MirBorrowsTest.ArrayDestructions.cs`, `MirBorrowsTest.Raw.cs`, `ContentUpdateTest.cs`, `CompilerFunctionReferenceTest.cs` | 23 | 25 (16 rows; ContentUpdate 6, H: run rows move to a rejection theory) | R4 | — | 1 h |
| T-02 | 1x T20 ×16; 1a T15 LayoutAttribute twins ×8 | `LayoutAttributeBindingTest.CLayoutUses.cs` | 24 | 24 (rows) | R4 | — | 50 min |
| T-03 | 1x collector rows and Never twins: InstanceCollector ×3 (1x ×2, 1a frame); T20/T15 nested inherited Origin ×2; T14 ×10; NeverContinuation ×1 | `InstanceCollectorTest.EnclosingInstances.cs`, `ClosureResultOriginTest.Arguments.cs`, `UnreachableOwnershipTest.cs`, `NeverContinuationTest.cs` | 16 | 16 (H: T14 3 + 9 rows if `use`/`line` go) | R4 (BU reads InstanceCollector) | — | 50 min |
| T-04 | R5's verdict reads T16 ×2, T43 ×20; T35 ×4; 1a T13 ×5, T15 Deferred ×1 | `OwnershipStorageLimitTest.cs`, `OriginObligationVerdictTest.cs`, `MatchOwnershipTest.cs`, `ModuleBindingTest.cs`, `DeferredEmissionTest.cs`, `OriginPremiseLifecycleTest.cs`, `GenericOwnershipCaseTest.cs`, `AbortEmissionTest.cs`, `ScalarDefaultEmissionTest.cs` | 32 | 20 (H: T43 is method bodies) | R4 | — | 1 h |
| T-05 | 1x prelude static ×1; 1a T11 ×17 | `PathCapabilityDiagnosticTest.cs` | 18 | 21 (11 `records` rows; 6 rows move to an accepted theory; prelude 2) | R4 | — | 1 h |
| T-06 | 1a T10 ×19, T15 Erasure ×1, PairBare ×1; T62 ElementMove ×1, ElementParameterMove ×1 | `PublishedPlaceProjectionTest.cs`, `ErasureOwnedDiagnosticTest.cs`, `PairBarePositionTest.cs`, `ElementMoveEmissionTest.cs`, `ElementParameterMoveEmissionTest.cs` | 23 | 18 (H: 3 if T10's `element` argument stays as the evidence anchor, 21 if it goes) | R4 | ledger check U-A3 (H) | 50 min |
| T-07 | T44 ×33, T49 ×2 (BlockEscape) | `BlockEscapeLoanTest.cs` | 35 | **32** (30 `Escapes` rows; over 25, unsure) | R4 | — | 1 h 15 (park and split at row 15 if past the hour) |
| T-08 | T44 rest; T50 ×1; T51 ×4 | `SharedEngineTotalityTest.cs`, `StructEmissionTest.cs`, `DefaultOwnershipTest.cs`, `BorrowedArrayWriteTest.cs`, `SharedObjectOwnershipTest.cs`, `BorrowingIterationTest.cs`, `InputDependentValueCallTest.cs` | 28 | 21 | R4 | ledger check U-A3 (H) | 1 h |
| T-09 | T49 evidence; T50 ×3; T51 misc ×10 | `StoredReferenceLoanTest.cs`, `DefaultAccessTest.cs`, `GenericCaseDiagnosticTest.cs`, `BorrowedDefaultTest.cs`, `AssociatedIdentityTest.cs`, `BorrowedDictionaryIndexTest.cs`, `CaptureDiagnosticTest.cs`, `DynamicArrayIndexTest.cs`, `InheritedStorageTest.cs`, `OwnershipPropertyEmissionTest.cs`, `QualificationRequiredTest.cs`, `GenericStorageEmissionTest.cs`, `UniqSliceTest.cs` | 24 | 20 | R4 | — | 1 h |
| T-10 | T04 ×4 (1r), T44 ×11, T46 ×18, T49 ×11; 1a T15 AWriteDisjoint ×1 | `CallReservationTest.cs` | 45 | **27** (H: T46's 9- and 8-row theories split by code) | R4 | **U-A3** | 1 h 15 (split at the T46 theories if past) |
| T-11 | T46 activation; T03 ×3 (1r); T44 ×7; T49 ×1; T62 ×2 (1a) | `DictionaryStoredReferenceTest.cs`, `PlacedExclusiveReferenceTest.cs`, `FollowedReferentFieldTest.cs`, `UserArithmeticUpdateTest.cs`, `OwnedStoredReferenceTest.cs`, `ChildMoveActivationTest.cs`, `AbstractCallReachTest.cs`, `BorrowedSlotReferenceTest.cs`, `ElementBorrowEmissionTest.cs`, `InheritedAccessorEmissionTest.cs`, `FixedOriginRetentionTest.cs`, `DependentDictionaryLiteralTest.cs` | 40 | 24 | R4 | **U-A3** (its rows and file) | 1 h |
| T-12 | T40 direct effects; 1a T15 AnUnclassifiedEffect ×1 | `Utf8ReserveEffectTest.cs`, `EffectBoundImplementationTest.cs`, `ForeignBorrowImportTest.cs`, `RequirementIdentityTest.cs`, `RawAddressTest.cs`, `UserArithmeticOperatorTest.cs` | 34 | 20 (H: 14 `Binding.*` reads in Utf8Reserve) | BU | **U-B2** | 1 h |
| T-13 | T40/T41 virtual | `VirtualEffectObligationTest.cs`, `VirtualEffectSelectionTest.cs`, `VirtualOwnershipTest.cs`, `VirtualCallableGuaranteeTest.cs` | 19 | 17 | BU | **U-B2** | 50 min |
| T-14 | T41 callable/report; T42 ×8; 1a T15 ImmutableStaticStorage ×1 | `CallableEffectBoundTest.cs`, `EffectBoundReportTest.cs`, `MirEffectsTest.Destructions.cs` | 25 | 19 | BU | **U-B2** (its `MirEffectsTest` row first) | 1 h |
| T-15 | T51 ×21 (part 1: ANestedSignaturesPerCall ×13, AJoinRests ×2, AnIllFormedHead ×3) | `CallableOriginForwardingTest.Conversions.cs` | 18 | 18 (rows) | R4 | **U-A2** | 45 min |
| T-16 | T51 ×18, T50 ×1 (part 2: AConversionAssumes ×10, EveryCallableComparison ×12; U-A2's t1j family) | same file as T-15 | 22 | 22 (rows) | R4 | **U-A2**, then T-15 | 50 min |
| T-17 | T44/T45/T49/T50/T51 Origin relations | `OriginRelationDiagnosticTest.cs` | 28 | **25-28** (H; AnOriginOnlyFailure ×11 may be 2 method edits) | R4 | **U-A2** | 1 h (split at the method boundary if past) |
| T-18 | T47/T50/T51 contextual parts and failed clauses; T50 ×1 MirOrigins | `OriginRelationDiagnosticTest.ContextualParts.cs`, `OriginRelationDiagnosticTest.FailedClauses.cs`, `MirOriginsTest.cs` | 20 | 20 | R4 | **U-A2** (its `MirOriginsTest` rows) | 50 min |
| T-19 | T45/T47/T50/T51 result premises | `ResultPremiseTest.cs`, `ResultPremiseTest.NestedResults.cs` | 22 | 22 (H) | R4 | **U-A2** | 1 h |
| T-20 | T45/T47/T50 local clauses and rows | `LocalOriginClauseTest.cs`, `OriginRowsTest.Boundary.cs` | 24 | 22 | R4 | **U-A2** (checks OriginRowsTest) | 1 h |
| T-21 | T44/T45/T49/T50 call relations; T34 13.7.1/0 (R5 data); spec T50 ×2, T51 ×3; T51 ×3 | `CallOriginRelationTest.cs`, `D/spec-cases.json` (VerifiedSpecExampleTest, HirFormsTest unedited), `LocalRegionInferenceTest.cs`, `TypeArgumentOriginEvidenceTest.cs`, `OriginPremiseCatalogTest.cs` | 30 | 23 | R4 | **U-A2** | 1 h |
| T-22 | T44/T45/T47/T50 returned closures; 1a T12 ×10 | `ClosureResultOriginTest.cs`, `ClosureResultOriginTest.Makers.cs` | 41 | 16-22 (H: two mixed-family theories may need per-row data) | R4 | **U-A2**, **G409** (H) | 1 h |
| T-23 | T44/T46/T48/T50/T51 closure environments; T48 ×2; T49 ×3; T51 ×4 | `ClosureEnvironmentOriginTest.cs`, `ClosureReceiverClassificationTest.cs`, `CallbackEmissionTest.cs`, `PropertyBindingTest.InferredClosures.cs` | 30 | 25 | R4 | **U-A1**, **G409** (both edit or move these files) | 1 h |
| T-24 | T51 RequireFallthrough/syntax rows; T46 ×2; T44 ancestry ×9 | `SyntaxDiagnosticTest.cs` (`D/syntax.json` only if a row still fails), `RequireRecoveryDiagnosticTest.cs`, `DiagnosticPrecisionTest.cs`, `StoredReborrowAncestryTest.cs`, `DisjointSplitLoanTest.cs` | 20 | 10-17 (syntax rows are likely S3's: 0 if R6-1 restores them) | R4 | **U-A3**, **R6-1** | 45 min |
| T-25 | T48 ×7; T45 ×1; T49 ×5; 1a T15 PairFollowRead ×1, T62 Ring ×1 | `RawStorageTest.cs`, `RawPlaceAccessTest.cs`, `TypeDagScalingTest.cs`, `ComponentSummaryTest.cs`, `DefaultInitializationTest.cs`, `GenericDefaultTest.cs` | 15 | 13 | R4 | **U-B1**, **U-B2** (GenericDefault's T32b rows), **T61** (DefaultInitialization's Allocation row) | 40 min |

25 units, ≈ 520 edits, ≈ 24 agent-hours. H5-split §4 sends "more than 6 T-units" to the planning session: this table is that input.

**Shared files.** Files that also hold rows outside this plan (land the T-unit first; the other unit then edits the same file
sequentially, never in parallel): `RawStorageTest` (T31a ×12, PQ-10 (e)), `TypeDagScalingTest` (T31d ×2 (e)), `RawPlaceAccessTest`
(T31b ×2, U-B1), `GenericOwnershipCaseTest` (T30 ×4, G147 (8) (e); T-04 edits only `B12-parent-move`), `FixedOriginRetentionTest` and
`InputDependentValueCallTest` (GXS (e) rows; InputDependentValueCall's T44 rows are in the same theory as its (e) row), `GenericDefaultTest`
(T32b, U-B2), `DictionaryStoredReferenceTest` (T63 ×2, U-A3), `RequireRecoveryDiagnosticTest` (T4A ×6, T63, R6-1),
`ClosureEnvironmentOriginTest` (T60, G409), `ClosureReceiverClassificationTest` (T30 ×4, U-A1), `CallableOriginForwardingTest.Conversions`
(T36, U-A2). T61's rows (BlockEscape ×2, CallableEffectBound, FixedOriginRetention, VirtualOwnership, VirtualCallableGuarantee) need no
edit.

**Each unit's check.** Its re-pointed rows pass in its unit run, and each touched class's remaining failures are exactly round 1's rows
outside this plan, by name (X3's condition 1 read for shared classes). A row that comes out otherwise stops the unit and goes to the
planning session as a round-2 input (never re-pointed to "whatever the head says").

## 2. Order

```
now (parallel, file-disjoint): T-01 T-02 T-03 T-04 T-05 T-06 T-07 T-08 T-09
after U-A2 lands and the branch merges dev:  T-15→T-16, T-17, T-18, T-19, T-20, T-21   (T-22 also after G409)
after U-A3:                                   T-10, T-11                                  (T-24 also after R6-1)
after U-B2:                                   T-12, T-13, T-14                            (T-25 also after U-B1 and T61)
after U-A1 and G409:                          T-23
```

- R4's lane: U-B1 ∥ U-A2 (step 1), U-A3 ∥ U-B2 (step 2), R6-1 (3), U-A1 (4). G409 lands before H5 (BU). T61 is R5's branch commit.
- Parallel units run in separate worktrees off the branch; the branch keeps one writer at a time (H5-split §2), so they land in turn,
  each after its own checks. T-units touch no file of D1, D2, B-del or D3 (those touch `OptionalTryDiscardTest`,
  `GenericAdaptationEffectTest`, `GenericCleanupCaseTest`, `MirDifferenceTest`), so they interleave with them freely.
- Each wave starts only after the branch merges the dev that holds its fix; the round-2 Session then checks every landed unit.

## 3. Expectation forms

General: never weaken. A record that was asserted stays asserted by code and location; a record that was asserted absent stays absent
by code. Evidence (related roles, labels, Notes) is pinned exactly as the head publishes it (an empty related list or a null Note is
pinned too); each fact a Binding or old record carried and the MIR record lacks is named in the commit message with the component that
will publish it. Each commit message carries the unit's class 3/4 counts by code (Q13) and names its 1r/1x/1a rows.

### 1r (the MIR closes an unsound acceptance; planning session's 1r list)

- **T01** (`ATruncateOrRemoveAllCallPublishesItsRecordsOnBothPipelines` ×13): each Writer/Item/Cap truncate, removeAll or clear form's
  `records` lists the head's A5 `ComparisonLoanConflict_Kd` at `y = 5`, as its sibling `...KeepsW` row lists; Gen Bad also R4-M147's
  located `Unsupported_Kd` at the call. Comment: SPEC 15.6, 4.9.6, 15.6.4; G160, G264 S1, BU-67; 1r.
- **T02**: `Accepts` false; Problems hold A5 `ComparisonLoanConflict_Kd` at `y = 5` (G264 S1, the owner's (B)).
- **T03** ×3: `Accepts` false with `ComparisonLoanConflict_Kd` at the row's `conflict` text (the second Reborrow or the write); the
  old-side acceptance assert in `AnalyzedFieldReborrow` goes; the names stop saying "known acceptance" (G231 closed by the MIR).
- **T04** ×4: `Accepts` false with `CallReservationConflict_Kd` at `cur = b@follow@uniq` (SPEC 15.6.7; G217, R4-M106); "recorded
  difference" leaves the name.
- **ContentUpdate** ×5: the five programs leave the run theories; a rejection theory asserts A5 `ComparisonLoanConflict_Kd` at the
  head's span per row and no generation (SPEC 15.6 holders bullets 1-2, 15.6.3, 15.6.4; R4-M136).
- **CFR(Callable)**: the row leaves `ExecutesRawAllocation`; a rejection row asserts `ComparisonLoanConflict_Kd` at the head's span
  (R4-M95 D6).

### (e), decided

- **T31c** (truncateGenOk, removeAllGenOk): `records` = R4-M147's located `Unsupported_Kd` at the call; the comment cites R4-M147 D″ and
  the owner's Q1/Q2 (a). `Accepts` false.

### 1x (the old analysis's record goes; R5's rows, R4 confirms)

- **T20** ×17: the Language record alone, `InvalidCLayout_Kd` at the use (×16) or `ComparisonLoanConflict_Kd` (nested inherited Origin
  bad twin); the old `Unsupported_Kd` beside it is absent (each `records` row loses its `|Unsupported_Kd ...` part).
- **T63 prelude static**: the expected StaticStorage records are the collector's at each static the head reports (P42; R5-M82,
  R5-M156), without the old analysis's record at (2211, 13); the comment's count changes from 26 and names the 1x reason.
- **T63 InstanceCollector** (BU's consent, conditions 1-3):
  1. "self joined" and "self assigned" are 1x: `records` = `Unsupported_Kd {Sized}; Unsupported_Kd g()` (the head's records). The old
     read-site gap at `self.n` and the old analysis's `UninitializedPlace_Kd` cascade go; the collector's gap at the call `g()` stays.
     The row comment says so.
  2. "holder in the owner's frame" is 1a (owner, 2026-11-03): `records` empty, plus acceptance evidence: the collector's item for
     `Box<i64>`'s `n` closes (the class's `Closed` check, `InstanceCollectorTest.EnclosingInstances.cs:122`), or a native run.
  3. The theory's comment ("dev's records") names BU-60, the 1x and 1a reasons, and the flip (H5).

### 1a (owner-accepted; acceptance with evidence)

Each row asserts `Accepts` true (no Problems, Binding complete) **and** evidence that the program is really accepted: generation
succeeds through the published pipeline (`Generate` with no record and the function emitted), or, where the class already runs
programs, its run output, or a MIR fact the class already reads. A row moves out of a theory of rejected programs into an accepted theory
of the same class (or that class's positive theory) instead of a per-row branch. Theory arguments that only located the old record
(`at`, `element`, `call`, `use`/`line`) either anchor the evidence or leave the rows.

- **T10** ×19 (BU-10's old emission reborrow limit): accepted; generation emits `f`; the name drops "IsUnsupported".
- **T11** ×17 (G203, BU-15/16, P42 path): the `Unsupported_Kd` rows of `AStaticsElementIsWritableAsItsField` (×6) move to an accepted
  theory with generation evidence; `AnOwnedInlineElementIsWritableWhereItsOwnerIs`' rows (×11) get empty `records` plus the evidence;
  the `InvalidAssignment_Kd` rows stay. If generation refuses a static element (P42 storage), stop: that row is the collector's limit,
  not 1a.
- **T12** ×10 (R4-M152): accepted; the call's result is used (generation; `MirBorrowsTest` already judges these forms).
- **T13** ×5 (BU-14): accepted; generation evidence.
- **T14** ×10 and **NeverContinuation** ×1 (R5-M159's twin rule, SPEC 14.10.3, 15.1.5): accepted, the dead use has no record;
  generation evidence; NeverContinuation's `AMoveBelowADivergingKey...` row asserts its reachable twin's verdict (accepted).
- **T15** ×16: each row accepted with generation evidence; `DeferredEmissionTest.ExcessiveExpansion...` asserts what generation does
  with the expansion (the test's `Generate` false is re-read first; if generation still refuses it with a limit record, the row is a MIR
  limit, not 1a: stop); `EffectBoundImplementationTest.AnUnclassifiedEffect...` also asserts A4 publishes nothing for it.
- **T62** ×5 (R4: 1a, SPEC 4.7.4, 15.6.2, 4.9.6, 15.6.3, 15.6.6, 16.3.3, X-1 (a)): each row leaves its "...IsRejected"/"...Rejected"
  theory for the class's accepted or emission theory; evidence is that class's emission (the `*EmissionTest` classes) or, for
  `TypeDagScalingTest.ARingThroughAHandle...` and `DependentDictionaryLiteralTest`, generation.

### Class 3

- **T16** ×2 (H0b-3): the old analysis's `StorageLimit` issue is read through `AnalyzeLegacyOwnership()` (until H10b); the published
  records are the MIR's: the independent errors stay asserted by code and location, and `OwnershipStorageLimit_Kd` is asserted absent by
  code; `Analysis.IsVerified` asserts the MIR's verdict.
- **T35** ×4 (R4: SPEC 14.10.3 decides with its own examples): the located `Unsupported_Kd` becomes the Language record
  `MovedPlace_Kd` / `UninitializedPlace_Kd` at the use after `$abort` or the non-completing initializer; the names drop "IsUnsupported".
- **T34 13.7.1/0** (R5's data fix): `D/spec-cases.json` variant 0 of `13.7.1-assignment-order` gains `Mir.Records`
  `[Unsupported_Kd at obj().prop = arg()]`, Binding's record, with variant 1's category, clause and note (restoring what S dropped);
  `HirFormsTest.AcceptedPrograms` then leaves 13.7.1/0 out with no edit.
- **T40** (BU): Binding complete with no Binding effect issue; `Accepts` false; A4's record of the same code
  (`IncompatibleContractImplementation_Kd` or `UnsatisfiedEffectBound_Kd`) in Problems at the obligation subject: the `Self is C`
  clause, the virtual function, or the Callable use. A program the head accepts is a class 5 row (unsound acceptance): stop the unit.
- **T41** (BU): A4's record (code, subject) as in T40; the violating-effect label, Note text ("confined", "external operation",
  "premise", "erasure", overload) and the bound/effect related roles are pinned as A4 publishes them (none today, H0a §4.1, H0b §5.4);
  the commit lists each lost fact.
- **T42** ×8 (BU): `records` = `IncompatibleContractImplementation_Kd` at `Self is Sink` instead of Binding's at the call; `problems`
  unchanged.
- **T43** ×20 (R5-M185 (F), H0b-4): on a Binding-rejected program, `Accepts` false with Binding's record (by code) instead of
  `Analysis.IsVerified` false; the old-verdict counts (`metrics.Verdicts`) read `AnalyzeLegacyOwnership()` (until H10b).
  `GenericOwnershipCaseTest.JudgesEveryCaseAtDefinition` `B12-parent-move`: first confirm the legacy entry still runs the old analysis
  there; if its issues are empty, the row asserts the MIR's record (code, span).
- **T44** ×94: one A5 `ComparisonLoanConflict_Kd` stays one record; its primary span moves from the operand to the enclosing statement,
  declaration or initializer (an exit's destination to the declaration), as the failure message shows; related roles stay asserted.
- **T45** ×36: `UnsatisfiedOriginRelation_Kd` / `UnprovenOriginRelation_Kd` at the statement (`return X`, `let v: T = X`) or the
  initializer instead of the operand `X`.
- **T46** ×48: the MIR's code at the MIR's span (`CallActivationConflict_Kd` / `PlacementActivationConflict_Kd` ↔
  `ComparisonLoanConflict_Kd` / `CallReservationConflict_Kd`), one record staying one record; a row whose code differs from its theory's
  constant moves to the theory of that code.
- **T47** ×21: the MIR's code, requirement and Reason: `UnprovenOriginRelation_Kd` → `UnsatisfiedOriginRelation_Kd`,
  `UnprovenConstraint_Kd` → `UnsatisfiedOriginRelation_Kd`, `==` → `outlives`, Reason `wellFormed` → `fit`; a move to A5's located
  `Unsupported_Kd` is named as an (e) row (G216 (b)-style limit) in the commit.
- **T48** ×10: the program stays rejected with the MIR's record: `Unsupported_Kd` → `ComparisonLoanConflict_Kd` /
  `UninitializedPlace_Kd`; depth 70 → `OwnershipDepthLimit_Kd` (R5-M44); `UnsatisfiedOriginRelation_Kd` → `Unsupported_Kd` /
  `MovedPlace_Kd` / `UninitializedPlace_Kd`.
- **T49** ×49: the MIR record's related roles, labels and Note as published (for example no implicit-borrow Note and no
  `preceding prepared parameter` label until A6 publishes them); the evidence assertion stays, with the new values.

### Class 4 (count updates, rule W "dep")

- **T50** ×74: +1 `UnsatisfiedOriginRelation_Kd` or `UnprovenOriginRelation_Kd` (and the one `MovedPlace_Kd` / `ComparisonLoanConflict_Kd`
  the failure lists) at Binding's twin row, at the same span or at `return X` beside `X`. Reason: H0b §5.4 "A5's records at Binding's
  twin rows", twins until H11 (R4-M108).
- **T51** ×87: + the MIR records the failure lists on a Binding- or parser-rejected program, by code (`Unsupported_Kd` 38,
  `UninitializedPlace_Kd`, `ComparisonLoanConflict_Kd`, `RequireFallthrough_Kd`, `MovedPlace_Kd`, `UnprovenOriginRelation_Kd`, and the
  rest of round 1's per-code row). Reason: R5-M184, direct publication until A6 2g-1 (R5-M182's superseding line). The syntax rows
  (`MissingSyntax_Kd` ×4, `RequireFallthrough_Kd` ×2, `ExpectedSyntax_Kd`) are most likely R6-1's S3 cases: re-point only what still
  fails after R6-1.

## 4. Risks

1. **Effect re-points (T-12, T-13) may find no A4 record.** Round 1 saw A4's record only in MirEffectsTest, EffectBoundImplementation
   and RequirementIdentity; Utf8ReserveEffect (16), VirtualEffectObligation/Selection, ForeignBorrowImport, RawAddress and
   UserArithmeticOperator read only `Binding.*`. U-B2 changes exactly the concrete-Self Requirement call that operator witnesses use. A
   program accepted after U-B2 is an unsound acceptance (class 5) and stops H5. First step of each: read `Accepts` on its programs.
2. **Estimates.** Theory-body methods (T43, T40/T41, ClosureResultOrigin, the 1a evidence) are (H). T-07, T-10 and T-17 are near or above
   25; each names its split point (H5-split §4: park on `held/r5-h5-t<k>` past the hour).
3. **"May move" waits.** T-06 and T-08 start before U-A3 on a ledger check; the U-A2 wave assumes A2's meet fix moves Origin records in
   all six files (only CallableOriginForwarding, MirOrigins and OriginRows are certain).
4. **1a evidence.** Generation is the lowest layer that shows acceptance; a row whose generation refuses (T11's static elements under
   P42, T15's DeferredEmission) is not 1a and returns to the planning session.

## Working time

2026-11-03 21:27-21:45 by the session clock (log: `tplan-state.md`), within the one-hour limit. The unit table was cross-checked
against round 1's per-test list: every one of the 656 rows maps to exactly one unit, and no file is in two units (T-15/T-16 share one
file in sequence).

## 4. The planning session's ruling (R5, 2026-11-03)

This table is adopted as the T-unit plan. It goes beyond H5-split §4's "more than 6 T-units" and H0b's estimate (t −250/+210)
because round 1's class 3/4 rows (523) and the owner's 1a list (84) are more than H0b predicted. The plan stays file-disjoint and holds
units of an hour or less. Amendments:

- **Reviews.** Each unit gets one review by an agent of R5's who wrote no part of it (Q13). The reviewer checks the unit against this
  plan's expectation forms and R4's verdict tables. BU reviews T-12, T-13 and T-14, as H5-split's Binding-cause rule says, and reads
  the InstanceCollector rows (BU-60). R4's lane is full with the fix units, so R4 is asked only when a reviewer or an implementer finds a
  record whose MIR verdict looks wrong. Such a row stops and becomes a round-2 input, never a re-point.
- **Parallelism.** At most three T-units run at once, each in its own worktree on its own branch `claude/r5-h5-t<nn>` from
  `claude/r6-flip`. R5 merges them into the branch one at a time, merges only.
- **Evidence.** A touched class keeps failing rows outside this plan (X3), and verify-commit refuses a failing run. So the commit's
  evidence is a passing Unit run of the unit's re-pointed methods. The class run, whose failures must equal round 1's rows outside the
  plan by name, is backed up beside it and cited in the commit message.
